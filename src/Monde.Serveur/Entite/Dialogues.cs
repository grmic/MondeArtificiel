using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Stockage;

namespace Monde.Serveur.Entite;

/// <summary>Une réplique pour le navigateur : les mots de l'Entité, leur traduction, ce qu'en ont fait les règles, la réponse de l'humain.</summary>
public sealed record RepliqueDto(int Rang, string Date, string Entite, IReadOnlyList<string> Traduction, IReadOnlyList<string> Regles, string Mots,
    string Pensee, IReadOnlyList<string> Libre, string? Juge, string? Erreur, double Secondes, long JournalPremier);

/// <summary>
/// Un dialogue pour le navigateur. <paramref name="Attend"/> : les mots de l'Entité en cours de traitement (null : rien en cours) ;
/// <paramref name="Croit"/> : ce qu'il croit en ce moment (« incompris », « doute », « croit »).
/// </summary>
public sealed record DialogueDto(long Id, int Humain, string Nom, string Mode, string Voie, int Priere, string? TextePriere, string Debut, string? Fin,
    string? FinTexte, bool EnCours, string? Attend, int Repliques, string Croit, IReadOnlyList<RepliqueDto> Liste, string? Erreur);

/// <summary>Un dialogue dans une liste.</summary>
public sealed record DialogueResumeDto(long Id, int Humain, string Nom, string Voie, string Debut, int Repliques, string? FinTexte, bool EnCours, string Croit);

/// <summary>« X t'appelle » : une prière forte, à laquelle le joueur peut répondre par un dialogue.</summary>
public sealed record AppelDto(int Priere, int Humain, string Nom, string Texte);

/// <summary>
/// Le dialogue direct entre l'Entité (le joueur) et un humain joué par le LLM local (0.36.2). Chaque réplique : le LLM traduit les
/// mots de l'Entité en paroles codées ; les règles décident (comprendre, croire, obéir, hésiter, refuser, partir) ; le LLM fait
/// parler l'humain d'après ce résultat, et le juge de cohérence relit ses mots. Avec l'Entité libre, l'humain peut aussi changer de
/// lui-même son état (émotions, croyance, liens, projets), dans les limites de la 0.35.2. L'horloge du monde est arrêtée pendant le
/// dialogue. Un seul dialogue à la fois.
/// </summary>
public sealed class Dialogues(ServiceMonde service, ClientLlm client, EntiteLlm entite, IHubContext<MondeHub> hub, ILogger<Dialogues> journal) : BackgroundService
{
    /// <summary>Voies possibles : le signe ne se prête pas à une conversation.</summary>
    public static readonly ModeParole[] Voies = [ModeParole.Reve, ModeParole.Voix, ModeParole.Apparition];

    /// <summary>Longueur maximale d'une réplique de l'Entité.</summary>
    public const int ReplicaMax = 1200;

    /// <summary>Paroles codées au plus par réplique.</summary>
    public const int ParolesMax = 2;

    /// <summary>Sans un mot de l'Entité pendant ce temps (réel), le dialogue s'achève et l'horloge repart.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan DelaiReponse = TimeSpan.FromMinutes(5);

    /// <summary>Ce que l'humain peut changer de lui-même, avec l'Entité libre : son cœur, sa croyance, ses liens, ses projets.</summary>
    public static readonly string[] SortesLibres =
        ["humeur", "peur", "chagrin", "colere", "desir", "croyance", "amour", "crainte", "revolte", "ferveur", "lien", "pensee", "communaute"];

    /// <summary>Le dialogue en cours (null : aucun).</summary>
    private sealed class EnCours
    {
        public required long Id;
        public required int Humain;
        public required ModeParole Mode;
        public required int Priere;
        public int Rang;
        public bool? Croit;
        public bool Comprise;
        public DateTime Contact = DateTime.UtcNow;
        public string? Attend;
        public string? Erreur;
        public required MondeSimule Monde;
    }

    private readonly Lock _verrou = new();
    private EnCours? _courant;
    private MondeSimule? _monde;
    private int _dernierePriere = -1;

    // ───────────── Ouvrir, dire, finir ─────────────

    /// <summary>Ouvre un dialogue avec un humain vivant (ou rend celui qui est ouvert avec lui).</summary>
    public DialogueDto Ouvrir(int humain, string mode, int priere)
    {
        if (string.IsNullOrEmpty(entite.Connexion.Modele)) throw new ArgumentException(L.T("llm.erreur.pasDeModele"));
        if (!Enum.TryParse<ModeParole>(mode, out var voie) || Array.IndexOf(Voies, voie) < 0) throw new ArgumentException(L.T("dlg.erreur.voie"));
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        if (humain < 1 || humain > humains.Count || !humains[humain - 1].EstVivant) throw new ArgumentException(L.T("erreur.humainVivant"));
        if (humains[humain - 1].EstBebe(maintenant)) throw new ArgumentException(L.T("dlg.erreur.bebe"));
        lock (_verrou)
        {
            if (_courant is { } c)
            {
                if (c.Humain == humain && ReferenceEquals(c.Monde, monde)) return Dto(c.Id);
                if (c.Attend is not null) throw new ArgumentException(L.T("dlg.erreur.occupe"));
                Finir(c, FinDialogue.Joueur);
            }
            if (priere != 0 && monde.Prieres().FirstOrDefault(p => p.Id == priere)?.HumainId != humain) priere = 0;
            var id = service.Depot.AjouterDialogue(new DialogueEntite(0, humain, (int)voie, priere, maintenant, 0, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                entite.Connexion.Libelle, 0, 0, 0, -1, false, 0));
            _courant = new EnCours { Id = id, Humain = humain, Mode = voie, Priere = priere, Monde = monde };
            monde.EnDialogue = true;
        }
        _ = Diffuser();
        return Dto(_courant!.Id);
    }

    /// <summary>L'Entité dit quelque chose : la réplique est traitée en arrière-plan, le résultat arrive par « Dialogue ».</summary>
    public DialogueDto Dire(long id, string texte)
    {
        texte = (texte ?? "").Trim();
        if (texte.Length == 0) throw new ArgumentException(L.T("dlg.erreur.vide"));
        if (texte.Length > ReplicaMax) texte = texte[..ReplicaMax];
        EnCours c;
        lock (_verrou)
        {
            if (_courant is not { } x || x.Id != id) throw new ArgumentException(L.T("dlg.erreur.fini"));
            if (x.Attend is not null) throw new ArgumentException(L.T("dlg.erreur.occupe"));
            c = x;
            c.Attend = texte;
            c.Erreur = null;
            c.Contact = DateTime.UtcNow;
        }
        var langue = L.Courante.Code;
        _ = Task.Run(async () =>
        {
            L.Definir(langue);
            try
            {
                await Repliquer(c, texte, CancellationToken.None);
            }
            catch (Exception e)
            {
                journal.LogWarning(e, "Dialogue : erreur imprévue");
                c.Erreur = e.Message;
            }
            finally
            {
                lock (_verrou)
                {
                    c.Attend = null;
                    c.Contact = DateTime.UtcNow;
                }
                await Diffuser();
            }
        });
        _ = Diffuser();
        return Dto(id);
    }

    /// <summary>Le joueur met fin au dialogue.</summary>
    public DialogueDto Terminer(long id)
    {
        lock (_verrou)
        {
            if (_courant is { } c && c.Id == id)
            {
                if (c.Attend is not null) throw new ArgumentException(L.T("dlg.erreur.occupe"));
                Finir(c, FinDialogue.Joueur);
            }
        }
        _ = Diffuser();
        return Dto(id);
    }

    /// <summary>Le dialogue en cours, s'il y en a un.</summary>
    public DialogueDto? Courant()
    {
        long? id;
        lock (_verrou) id = _courant?.Id;
        return id is { } i ? Dto(i) : null;
    }

    /// <summary>Clôt le dialogue (sous le verrou) : la fin passe par les règles, l'horloge repart.</summary>
    private void Finir(EnCours c, FinDialogue fin)
    {
        var d = service.Depot.Dialogue(c.Id);
        long ligne = 0;
        if (ReferenceEquals(c.Monde, service.Monde))
        {
            try
            {
                ligne = c.Monde.FinirDialogue(c.Humain, c.Mode, c.Rang, c.Croit == true, c.Comprise, fin, 0);
            }
            catch (ArgumentException)
            {
            }
            service.Sauvegarder();
        }
        if (d is not null)
            service.Depot.MettreAJourDialogue(d with { Fin = c.Monde.Secondes, Statut = 1, FinRaison = (int)fin, Repliques = c.Rang, Croit = c.Croit is { } b ? b ? 1 : 0 : -1,
                Comprise = c.Comprise, JournalFin = ligne });
        c.Monde.EnDialogue = false;
        if (ReferenceEquals(_courant, c)) _courant = null;
    }

    // ───────────── Une réplique ─────────────

    private async Task Repliquer(EnCours c, string texte, CancellationToken arret)
    {
        var monde = c.Monde;
        var connexion = entite.Connexion;
        var reglages = entite.Reglages;
        var chrono = Stopwatch.StartNew();
        var precedentes = service.Depot.Repliques(c.Id);
        var brut = new StringBuilder();

        // 1. Traduire ses mots en paroles codées.
        List<ActeLlm> paroles;
        try
        {
            paroles = await Traduire(c, texte, precedentes, connexion, brut, arret);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            c.Erreur = e is TaskCanceledException ? L.T("llm.erreur.delai") : L.T("llm.erreur.connexion", ("detail", e.Message));
            return;
        }
        if (!ReferenceEquals(monde, service.Monde)) return;

        // 2. Les règles décident.
        c.Rang++;
        var rang = c.Rang;
        var regles = new List<string>();
        var issues = new List<(ActeLlm Acte, Reponse? Reponse, string Texte)>();
        var reponses = new List<Reponse>();
        long premier = 0, dernier = -1;
        void Plage(long p, long d)
        {
            if (d < p) return;
            if (premier == 0 || p < premier) premier = p;
            dernier = Math.Max(dernier, d);
        }
        foreach (var a in paroles)
        {
            try
            {
                var (fait, p, d) = monde.Executer(m => Actes.Appliquer(m, a, FamillesLlm.Toutes, []));
                Registre.Noter(service, Registre.Joueur, a, fait, p, d);
                Plage(p, d);
                var r = monde.DerniereReponse(c.Humain);
                if (r is { } rr) reponses.Add(rr);
                var t = string.Join(" ", fait.Resultats);
                issues.Add((a, r, t));
                regles.Add($"{Libelle(a)} → {t}");
            }
            catch (ArgumentException e)
            {
                regles.Add($"{Libelle(a)} → {L.T("llm.resultat.refuse", ("raison", e.Message))}");
            }
        }
        var (regle, p2, d2) = monde.Executer(m => m.Ecouter(c.Humain, c.Mode, rang, c.Croit, reponses, ClientLlm.Couper(texte, 240)));
        Plage(p2, d2);
        c.Comprise |= regle.Comprise;
        if (regle.Comprise || c.Croit is not null) c.Croit = regle.Croit;
        regles.Add(L.T(!regle.Comprise ? "dlg.r.incomprise" : regle.Croit ? "dlg.r.croit" : "dlg.r.doute"));
        if (regle.Fin != FinDialogue.Aucune) regles.Add(L.T($"dlg.r.fin.{regle.Fin}"));
        service.Sauvegarder();

        // 3. L'humain parle (sauf s'il n'est plus là pour répondre).
        string mots = "", pensee = "", juge = "", lu = "";
        string? erreur = null;
        var libreTextes = new List<string>();
        string libreJson = "";
        var (_, humains) = monde.InstantaneHumains();
        var h = humains[c.Humain - 1];
        if (regle.Fin != FinDialogue.Absent && h.EstVivant)
        {
            var libre = reglages.Libre;
            var consigne = Consigne(h, c, libre);
            var demande = Demande(c, h, texte, precedentes, issues, regle);
            lu = consigne + "\n\n----\n\n" + demande;
            var messages = new List<(string, string)> { ("user", demande) };
            try
            {
                for (var essai = 0; essai < 2; essai++)
                {
                    using var delai = CancellationTokenSource.CreateLinkedTokenSource(arret);
                    delai.CancelAfter(DelaiReponse);
                    var reponse = await client.Demander(connexion, consigne, messages, Schema(libre), delai.Token);
                    brut.AppendLine($"---- {L.T("dlg.trace.humain")} ({essai + 1}) ----").AppendLine(reponse);
                    if (!Lire(reponse, out var m, out var pe, out var acte, out var faute))
                    {
                        erreur = faute;
                        messages.Add(("assistant", reponse));
                        messages.Add(("user", L.T("llm.d.corriger", ("erreurs", faute))));
                        continue;
                    }
                    (mots, pensee) = (m, pe);
                    if (acte is not null && !libre) acte = null;
                    string? fauteLibre = null;
                    if (acte is not null) fauteLibre = VerifierLibre(acte, c.Humain);
                    // Le juge relit ses mots (et sa réaction libre) au regard de sa fiche et de ce que les règles ont décidé.
                    if (fauteLibre is null && reglages.Juge)
                    {
                        using var delaiJuge = CancellationTokenSource.CreateLinkedTokenSource(arret);
                        delaiJuge.CancelAfter(DelaiReponse);
                        var avis = await client.Demander(connexion, L.T("dlg.juge.consigne", ("langue", L.Courante.Nom)),
                            [("user", L.T("dlg.juge.demande", ("contexte", demande), ("decision", reponse)))], Libres.SchemaJuge(), delaiJuge.Token);
                        brut.AppendLine($"---- {L.T("libre.juge.nom")} ({essai + 1}) ----").AppendLine(avis);
                        var verdict = Libres.LireVerdict(avis);
                        if (!verdict.Coherent) fauteLibre = verdict.Raison;
                        juge = verdict.Coherent ? "" : verdict.Raison;
                    }
                    if (fauteLibre is not null && essai == 0)
                    {
                        messages.Add(("assistant", reponse));
                        messages.Add(("user", L.T("dlg.corriger", ("raison", fauteLibre))));
                        continue;
                    }
                    erreur = null;
                    if (fauteLibre is not null) acte = null;
                    if (acte is not null && ReferenceEquals(monde, service.Monde))
                    {
                        acte = acte with { Recit = string.IsNullOrWhiteSpace(acte.Recit) ? L.T("dlg.libre.recit", ("nom", h.Nom)) : acte.Recit, Visible = false };
                        libreJson = JsonSerializer.Serialize(acte, Actes.Json);
                        try
                        {
                            var (fait, p, d) = monde.Executer(x => Libres.Appliquer(x, acte));
                            Registre.Noter(service, Registre.Llm, acte, fait, p, d);
                            Plage(p, d);
                            libreTextes.AddRange(Libres.Decrire(acte));
                            libreTextes.AddRange(fait.Resultats);
                        }
                        catch (ArgumentException e)
                        {
                            libreTextes.Add(L.T("llm.resultat.refuse", ("raison", e.Message)));
                        }
                    }
                    break;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                erreur = e is TaskCanceledException ? L.T("llm.erreur.delai") : L.T("llm.erreur.connexion", ("detail", e.Message));
            }
        }
        if (dernier >= premier && premier > 0) MarquesLlm.Ajouter(premier, dernier);
        service.Depot.AjouterReplique(new RepliqueEntite(0, c.Id, rang, monde.Secondes, texte, JsonSerializer.Serialize(paroles, Actes.Json),
            JsonSerializer.Serialize(regles, Actes.Json), mots, pensee, JsonSerializer.Serialize(new { acte = libreJson, resultats = libreTextes }, Actes.Json), juge, lu,
            brut.ToString(), chrono.ElapsedMilliseconds, premier, dernier, erreur));
        var d0 = service.Depot.Dialogue(c.Id);
        if (d0 is not null)
            service.Depot.MettreAJourDialogue(d0 with { Repliques = rang, Croit = c.Croit is { } b ? b ? 1 : 0 : -1, Comprise = c.Comprise });
        c.Erreur = erreur;
        if (regle.Fin != FinDialogue.Aucune)
            lock (_verrou)
                if (ReferenceEquals(_courant, c)) Finir(c, regle.Fin);
        service.Sauvegarder();
        await hub.Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
    }

    /// <summary>Une parole codée en mots courts : « N'aie pas peur », « Unis-toi (à Mana) ».</summary>
    private string Libelle(ActeLlm a)
    {
        var (_, humains) = service.Monde.InstantaneHumains();
        var nom = L.Existe($"parole.{a.Parole}") ? L.T($"parole.{a.Parole}") : a.Parole ?? "";
        if (a.Cible is > 0 and var x && x <= humains.Count) nom += $" ({humains[x - 1].Nom})";
        return nom;
    }

    // ───────────── Traduire ─────────────

    private static JsonObject SchemaTraduction() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["paroles"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["parole"] = new JsonObject { ["type"] = "string" },
                        ["cible"] = new JsonObject { ["type"] = "integer" },
                        ["param"] = new JsonObject { ["type"] = "integer" },
                    },
                    ["required"] = new JsonArray("parole"),
                },
            },
        },
        ["required"] = new JsonArray("paroles"),
    };

    /// <summary>Le catalogue des paroles qu'on peut adresser à un humain, pour la traduction.</summary>
    public static string Catalogue()
    {
        var c = new StringBuilder();
        foreach (var p in Enum.GetValues<Parole>().Where(p => p != Parole.Penchant && !Paroles.Collective(p)))
        {
            var precision = p switch
            {
                Parole.PartsVers => " " + L.T("llm.cat.paramDirection"),
                Parole.PlanteDangereuse => " " + L.T("llm.cat.paramPlante"),
                Parole.NeVaPasALaGuerre => " " + L.T("llm.cat.paramCommunaute"),
                _ => "",
            };
            c.AppendLine($"- {p} : {L.T($"parole.{p}")}{(Paroles.VeutCible(p) ? " " + L.T("llm.cat.veutCible") : "")}{precision}");
        }
        c.AppendLine(L.T("llm.cat.directions", ("directions", string.Join(", ", Enumerable.Range(0, 8).Select(d => $"{d} = {L.T($"direction.{d}")}")))));
        return c.ToString();
    }

    private async Task<List<ActeLlm>> Traduire(EnCours c, string texte, List<RepliqueEntite> precedentes, ConnexionLlm connexion, StringBuilder brut,
        CancellationToken arret)
    {
        var monde = c.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var h = humains[c.Humain - 1];
        var consigne = L.T("dlg.traduire.consigne", ("langue", L.Courante.Nom), ("max", ParolesMax)) + "\n\n" + Catalogue();
        var d = new StringBuilder();
        d.AppendLine(L.T("dlg.t.humain", ("nom", h.Nom), ("id", h.Id)));
        var proches = h.Liens.Where(l => l.Id >= 1 && l.Id <= humains.Count && humains[l.Id - 1].EstVivant)
            .OrderByDescending(l => Math.Abs(l.Affection)).Take(20)
            .Select(l => $"n°{l.Id} {humains[l.Id - 1].Nom}").ToList();
        if (proches.Count > 0) d.AppendLine(L.T("dlg.t.proches", ("liste", string.Join(", ", proches))));
        var plantes = h.Savoirs.Where(s => s.Espece > 0).Select(s => s.Espece).Distinct().Take(20)
            .Select(e => $"{e} = {L.Rediger((Monde.Simulation.Recits.Recit)monde.Flore.Get(e).Dit)}").ToList();
        if (plantes.Count > 0) d.AppendLine(L.T("dlg.t.plantes", ("liste", string.Join(", ", plantes))));
        var communautes = monde.Communautes().Where(x => x.Active).Take(25).Select(x => $"n°{x.Id} {L.Communaute(x.Nom)}").ToList();
        if (communautes.Count > 0) d.AppendLine(L.T("dlg.t.communautes", ("liste", string.Join(", ", communautes))));
        foreach (var r in precedentes.TakeLast(4))
        {
            d.AppendLine($"{L.T("dlg.t.entite")} : « {ClientLlm.Couper(r.Entite, 300)} »");
            if (r.Mots.Length > 0) d.AppendLine($"{h.Nom} : « {ClientLlm.Couper(r.Mots, 300)} »");
        }
        d.AppendLine();
        d.AppendLine(L.T("dlg.t.dit", ("texte", texte)));
        var messages = new List<(string, string)> { ("user", d.ToString()) };
        var valides = new List<ActeLlm>();
        for (var essai = 0; essai < 2; essai++)
        {
            using var delai = CancellationTokenSource.CreateLinkedTokenSource(arret);
            delai.CancelAfter(DelaiReponse);
            var reponse = await client.Demander(connexion, consigne, messages, SchemaTraduction(), delai.Token);
            brut.AppendLine($"---- {L.T("dlg.trace.traduction")} ({essai + 1}) ----").AppendLine(reponse);
            var fautes = new List<string>();
            valides = LireTraduction(reponse, c.Humain, c.Mode, humains, fautes);
            if (fautes.Count == 0 || essai == 1) break;
            messages.Add(("assistant", reponse));
            messages.Add(("user", L.T("llm.d.corriger", ("erreurs", string.Join(" ", fautes)))));
        }
        return valides;
    }

    /// <summary>Lit la traduction : les paroles valides (au plus <see cref="ParolesMax"/>), les autres dans <paramref name="fautes"/>.</summary>
    public static List<ActeLlm> LireTraduction(string reponse, int humain, ModeParole mode, IReadOnlyList<Humain> humains, List<string> fautes)
    {
        var valides = new List<ActeLlm>();
        JsonObject? o = null;
        try
        {
            o = JsonNode.Parse(ClientLlm.Nettoyer(reponse), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject;
        }
        catch (JsonException e)
        {
            fautes.Add(L.T("llm.erreur.json", ("detail", e.Message)));
            return valides;
        }
        if (o?["paroles"] is not JsonArray liste) return valides;
        foreach (var n in liste)
        {
            if (n is not JsonObject x || valides.Count >= ParolesMax) continue;
            var nom = x["parole"] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";
            int? Entier(string cle) => x[cle] is JsonValue j && (j.TryGetValue<int>(out var i) || j.TryGetValue<string>(out var t) && int.TryParse(t, out i)) ? i : null;
            if (!Enum.TryParse<Parole>(nom, true, out var p) || !Enum.IsDefined(p) || p == Parole.Penchant || Paroles.Collective(p))
            {
                if (nom.Length > 0 && !nom.Equals("aucune", StringComparison.OrdinalIgnoreCase) && !nom.Equals("none", StringComparison.OrdinalIgnoreCase))
                    fautes.Add(L.T("dlg.erreur.paroleInconnue", ("parole", nom)));
                continue;
            }
            var cible = Entier("cible") ?? 0;
            if (Paroles.VeutCible(p) && (cible < 1 || cible > humains.Count || cible == humain || !humains[cible - 1].EstVivant))
            {
                fautes.Add(L.T("dlg.erreur.cible", ("parole", p)));
                continue;
            }
            var a = new ActeLlm
            {
                Type = Actes.Parler, Humains = [humain], Parole = p.ToString(), Mode = mode.ToString(), Cible = Paroles.VeutCible(p) ? cible : 0, Param = Entier("param") ?? 0,
            };
            try
            {
                Actes.Verifier(a, FamillesLlm.Paroles);
                if (!valides.Any(y => y.Parole == a.Parole)) valides.Add(a);
            }
            catch (ArgumentException e)
            {
                fautes.Add(e.Message);
            }
        }
        return valides;
    }

    // ───────────── Faire parler l'humain ─────────────

    private const string FormeHumain = """{"pensee":"…","mots":"…"}""";

    private const string FormeHumainLibre = """{"pensee":"…","mots":"…","changements":[{"quoi":"…","humain":n°,"valeur":x}]}""";

    private static JsonObject Schema(bool libre)
    {
        var proprietes = new JsonObject { ["pensee"] = new JsonObject { ["type"] = "string" }, ["mots"] = new JsonObject { ["type"] = "string" } };
        if (libre) proprietes["changements"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        return new JsonObject { ["type"] = "object", ["properties"] = proprietes, ["required"] = new JsonArray("pensee", "mots") };
    }

    private string Consigne(Humain h, EnCours c, bool libre)
    {
        var langage = LangageDto.Depuis(h, c.Monde.InstantaneHumains().Humains, c.Monde.Communautes(), c.Monde.Flore);
        var s = new StringBuilder(L.T("dlg.humain.consigne", ("nom", h.Nom), ("voie", L.T($"modeParole.{c.Mode}")), ("langue", L.Courante.Nom),
            ("stade", langage.Stade), ("n", langage.NombreDeMots), ("forme", libre ? FormeHumainLibre : FormeHumain)));
        if (libre)
        {
            s.AppendLine().AppendLine();
            s.AppendLine(L.T("dlg.humain.libre", ("id", h.Id), ("liste", string.Join(", ", SortesLibres))));
        }
        return s.ToString();
    }

    private string Demande(EnCours c, Humain h, string texte, List<RepliqueEntite> precedentes, List<(ActeLlm Acte, Reponse? Reponse, string Texte)> issues,
        RegleReplique regle)
    {
        var monde = c.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var d = new StringBuilder();
        d.AppendLine(L.T("llm.d.date", ("date", L.Date(maintenant))));
        d.AppendLine(L.T("dlg.d.toi", ("nom", h.Nom), ("id", h.Id)));
        Contexte.Humain(d, service, h.Id);
        var langage = LangageDto.Depuis(h, humains, monde.Communautes(), monde.Flore);
        if (langage.Mots.Count > 0)
            d.AppendLine(L.T("dlg.d.mots", ("liste", string.Join(", ", langage.Mots.Take(30).Select(m => $"{m.Forme} = {m.Sens}")))));
        if (c.Priere != 0 && Religions.Prieres(service, enAttente: false).FirstOrDefault(p => p.Id == c.Priere) is { } pr)
            d.AppendLine(L.T("dlg.d.priere", ("texte", pr.Texte)));
        // Ses dialogues passés avec l'Entité (les trois derniers, en quelques mots).
        foreach (var x in service.Depot.Dialogues(h.Id, statut: 1, limite: 3))
        {
            var r = service.Depot.Repliques(x.Id);
            if (r.Count == 0) continue;
            d.AppendLine(L.T("dlg.d.passe", ("date", L.Date(x.Debut)), ("voie", L.T($"modeParole.{(ModeParole)x.Mode}")),
                ("resume", string.Join(" / ", r.Take(3).Select(y => ClientLlm.Couper(y.Entite, 100) + (y.Mots.Length > 0 ? " — " + ClientLlm.Couper(y.Mots, 100) : ""))))));
        }
        d.AppendLine();
        d.AppendLine(L.T("dlg.d.conversation", ("voie", L.T($"modeParole.{c.Mode}"))));
        foreach (var r in precedentes.TakeLast(8))
        {
            d.AppendLine($"{L.T("dlg.t.entite")} : « {r.Entite} »");
            d.AppendLine($"{L.T("dlg.d.toiCourt")} : « {r.Mots} »");
        }
        d.AppendLine($"{L.T("dlg.t.entite")} : « {texte} »");
        d.AppendLine();
        d.AppendLine(L.T("dlg.d.regles"));
        d.AppendLine("- " + L.T(!regle.Comprise ? "dlg.h.incomprise" : regle.Croit ? "dlg.h.croit" : "dlg.h.doute", ("voie", L.T($"modeParole.{c.Mode}"))));
        foreach (var (a, r, t) in issues)
            d.AppendLine("- " + L.T("dlg.h.parole", ("parole", Libelle(a)), ("issue", L.T($"dlg.h.issue.{(r ?? Reponse.Incomprise)}")), ("detail", t)));
        if (regle.Fin != FinDialogue.Aucune) d.AppendLine("- " + L.T($"dlg.h.fin.{regle.Fin}"));
        d.AppendLine();
        d.AppendLine(L.T("dlg.d.fin", ("nom", h.Nom)));
        return d.ToString();
    }

    /// <summary>Lit la réponse de l'humain ; avec l'Entité libre, ses changements deviennent un acte libre (null : aucun).</summary>
    public static bool Lire(string reponse, out string mots, out string pensee, out ActeLlm? acte, out string faute)
    {
        (mots, pensee, acte, faute) = ("", "", null, "");
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(ClientLlm.Nettoyer(reponse), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject;
        }
        catch (JsonException e)
        {
            faute = L.T("llm.erreur.json", ("detail", e.Message));
            return false;
        }
        if (o is null)
        {
            faute = L.T("llm.erreur.json", ("detail", "{}"));
            return false;
        }
        string Texte(string cle) => o[cle] is JsonValue v && v.TryGetValue<string>(out var s) ? ClientLlm.Couper(s.Trim(), 1500) : "";
        (mots, pensee) = (Texte("mots"), Texte("pensee"));
        if (o["changements"] is JsonArray ch && ch.Count > 0)
        {
            try
            {
                var liste = ch.Deserialize<List<ChangementLibre>>(Actes.Json) ?? [];
                if (liste.Count > 0) acte = new ActeLlm { Type = Actes.Libre, Changements = liste, Bienfait = true, Visible = false };
            }
            catch (JsonException e)
            {
                faute = L.T("llm.erreur.json", ("detail", e.Message));
                return false;
            }
        }
        return true;
    }

    /// <summary>Sa réaction libre ne touche que lui (ses liens vers d'autres compris), et seulement son cœur, sa croyance, ses liens, ses projets.</summary>
    public static string? VerifierLibre(ActeLlm acte, int humain)
    {
        foreach (var c in acte.Changements ?? [])
        {
            if (Array.IndexOf(SortesLibres, c.Quoi) < 0) return L.T("dlg.erreur.libreSorte", ("quoi", c.Quoi), ("liste", string.Join(", ", SortesLibres)));
            if (c.Humain != humain) return L.T("dlg.erreur.libreHumain", ("id", humain));
        }
        return null;
    }

    // ───────────── Pour le navigateur ─────────────

    private string CroitTexte(int croit, bool comprise) => L.T(croit == 1 ? "dlg.croit.oui" : comprise ? "dlg.croit.doute" : "dlg.croit.incompris");

    public DialogueDto Dto(long id)
    {
        var d = service.Depot.Dialogue(id) ?? throw new ArgumentException(L.T("dlg.erreur.fini"));
        var (_, humains) = service.Monde.InstantaneHumains();
        EnCours? c;
        lock (_verrou) c = _courant is { } x && x.Id == id ? x : null;
        var nom = d.Humain >= 1 && d.Humain <= humains.Count ? humains[d.Humain - 1].Nom : "?";
        var mode = (ModeParole)d.Mode;
        string? textePriere = d.Priere != 0 ? Religions.Prieres(service, enAttente: false).FirstOrDefault(p => p.Id == d.Priere)?.Texte : null;
        var repliques = service.Depot.Repliques(id).Select(Dto).ToList();
        var croit = c is not null ? (c.Croit is { } b ? b ? 1 : 0 : -1) : d.Croit;
        return new DialogueDto(d.Id, d.Humain, nom, mode.ToString(), L.T($"modeParole.{mode}"), d.Priere, textePriere, L.Date(d.Debut),
            d.Statut == 1 ? L.Date(d.Fin) : null, d.Statut == 1 ? L.T($"dlg.fin.court.{(FinDialogue)d.FinRaison}") : null, c is not null, c?.Attend,
            repliques.Count, CroitTexte(croit, c?.Comprise ?? d.Comprise), repliques, c?.Erreur);
    }

    private RepliqueDto Dto(RepliqueEntite r)
    {
        List<string> regles, libre = [];
        List<ActeLlm> paroles;
        try
        {
            regles = JsonSerializer.Deserialize<List<string>>(r.Regles, Actes.Json) ?? [];
            paroles = JsonSerializer.Deserialize<List<ActeLlm>>(r.Traduction, Actes.Json) ?? [];
            if (r.Libre.Length > 0 && JsonNode.Parse(r.Libre)?["resultats"] is JsonArray a) libre = a.Select(x => x?.GetValue<string>() ?? "").ToList();
        }
        catch (JsonException)
        {
            (regles, paroles) = ([], []);
        }
        return new RepliqueDto(r.Rang, L.Date(r.Le), r.Entite, paroles.Select(Libelle).ToList(), regles, r.Mots, r.Pensee, libre,
            r.Juge.Length > 0 ? r.Juge : null, r.Erreur, Math.Round(r.DureeMs / 1000.0, 1), r.JournalPremier);
    }

    public List<DialogueResumeDto> Liste(int humain)
    {
        var (_, humains) = service.Monde.InstantaneHumains();
        long? courant;
        lock (_verrou) courant = _courant?.Id;
        return service.Depot.Dialogues(humain).Select(d => new DialogueResumeDto(d.Id, d.Humain,
            d.Humain >= 1 && d.Humain <= humains.Count ? humains[d.Humain - 1].Nom : "?", L.T($"modeParole.{(ModeParole)d.Mode}"), L.Date(d.Debut), d.Repliques,
            d.Statut == 1 ? L.T($"dlg.fin.court.{(FinDialogue)d.FinRaison}") : null, d.Id == courant, CroitTexte(d.Croit, d.Comprise))).ToList();
    }

    private async Task Diffuser()
    {
        long? id;
        lock (_verrou) id = _courant?.Id;
        try
        {
            await hub.Clients.Diffuser("Dialogue", () => id is { } i ? Dto(i) : null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            journal.LogDebug(e, "Dialogue : diffusion impossible");
        }
    }

    // ───────────── Veille : silence, changement de monde, appels ─────────────

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await minuteur.WaitForNextTickAsync(arret))
            {
                try
                {
                    await Veiller();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    journal.LogWarning(e, "Dialogue : erreur imprévue");
                }
            }
        }
        catch (OperationCanceledException) when (arret.IsCancellationRequested)
        {
        }
    }

    private async Task Veiller()
    {
        var monde = service.Monde;
        var change = false;
        lock (_verrou)
        {
            if (_courant is { } c && c.Attend is null && (!ReferenceEquals(c.Monde, monde) || DateTime.UtcNow - c.Contact > Silence))
            {
                Finir(c, FinDialogue.Silence);
                change = true;
            }
        }
        if (change) await Diffuser();

        // « X t'appelle » : les prières fortes nouvelles d'un humain seul (pas collectives), si un LLM peut le faire parler.
        var prieres = monde.Prieres();
        if (!ReferenceEquals(monde, _monde))
        {
            _monde = monde;
            _dernierePriere = prieres.Select(p => p.Id).DefaultIfEmpty(0).Max();
            return;
        }
        var nouvelles = prieres.Where(p => p.Id > _dernierePriere).OrderBy(p => p.Id).ToList();
        if (nouvelles.Count == 0) return;
        _dernierePriere = nouvelles[^1].Id;
        if (string.IsNullOrEmpty(entite.Connexion.Modele)) return;
        foreach (var p in nouvelles.Where(p => p.Forte && p.CommunauteId == 0 && p.Issue == IssuePriere.EnAttente && p.Sujet != SujetPriere.Remerciement).TakeLast(3))
        {
            var id = p.Id;
            await hub.Clients.Diffuser("Appel", () => Religions.Prieres(service, enAttente: true).FirstOrDefault(x => x.Id == id) is { } dto
                ? new AppelDto(dto.Id, dto.Humain, dto.Nom, dto.Texte) : null);
        }
    }
}
