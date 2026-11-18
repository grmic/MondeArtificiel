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
    string Pensee, IReadOnlyList<string> Libre, string? Juge, string? Erreur, double Secondes, long JournalPremier, string Sens = "",
    IReadOnlyList<string>? Suivi = null, ReactionDto? Reaction = null);

/// <summary>Un témoin d'une apparition (0.45.11) : il l'a vue, et y a cru ou non.</summary>
public sealed record TemoinDto(int Humain, bool Croit);

/// <summary>
/// Ce que les règles ont décidé à cette réplique, pour que la vue 3D montre sa réaction (0.45.11) ; seulement pour les répliques dites
/// depuis le lancement du serveur. <paramref name="Emotion"/> : « incomprise », « doute », « effroi » ou « merveille » ;
/// <paramref name="Fin"/> : nom de <see cref="FinDialogue"/> ; <paramref name="Issues"/> : ce qu'il a fait de chaque parole (noms de
/// <see cref="Reponse"/>) ; traits de 0 à 1 pour nuancer le geste ; <paramref name="Temoins"/> : à la première réplique d'une apparition.
/// </summary>
public sealed record ReactionDto(bool Comprise, bool Croit, string Emotion, string Fin, IReadOnlyList<string> Issues, double Peur, double Nevrosisme,
    double Transcendance, double Extraversion, double Agreabilite, IReadOnlyList<TemoinDto> Temoins, string Geste = "", double Conscience = 0.5);

/// <summary>
/// Un dialogue pour le navigateur. <paramref name="Attend"/> : les mots de l'Entité en cours de traitement (null : rien en cours) ;
/// <paramref name="Croit"/> : ce qu'il croit en ce moment (« incompris », « doute », « croit »).
/// </summary>
public sealed record DialogueDto(long Id, int Humain, string Nom, string Mode, string Voie, int Priere, string? TextePriere, string Debut, string? Fin,
    string? FinTexte, bool EnCours, string? Attend, int Repliques, string Croit, IReadOnlyList<RepliqueDto> Liste, string? Erreur);

/// <summary>Un dialogue dans une liste.</summary>
public sealed record DialogueResumeDto(long Id, int Humain, string Nom, string Voie, string Debut, int Repliques, string? FinTexte, bool EnCours, string Croit);

/// <summary>Un rêve attendu : le dialogue s'ouvrira quand cet humain s'endormira.</summary>
public sealed record AttenteDto(int Humain, string Nom);

/// <summary>« X t'appelle » : une prière forte, à laquelle le joueur peut répondre par un dialogue.</summary>
public sealed record AppelDto(int Priere, int Humain, string Nom, string Texte);

/// <summary>
/// Le dialogue direct entre l'Entité (le joueur) et un humain joué par le LLM local (0.36.2). Chaque réplique : le LLM traduit les
/// mots de l'Entité en paroles codées ; les règles décident (comprendre, croire, obéir, hésiter, refuser, partir) ; le LLM fait
/// parler l'humain d'après ce résultat, et le juge de cohérence relit ses mots. Avec l'Entité libre, l'humain peut aussi changer de
/// lui-même son état (émotions, croyance, liens, projets), dans les limites de la 0.35.2. L'horloge du monde est arrêtée pendant le
/// dialogue. Un seul dialogue à la fois.
/// </summary>
public sealed class Dialogues(ServiceMonde service, ClientLlm client, EntiteLlm entite, IHubContext<MondeHub> hub, ILogger<Dialogues> journal, TraceLlm trace,
    DepensesLlm depenses) : BackgroundService
{
    /// <summary>Voies possibles : le signe ne se prête pas à une conversation.</summary>
    public static readonly ModeParole[] Voies = [ModeParole.Reve, ModeParole.Voix, ModeParole.Apparition];

    /// <summary>Longueur maximale d'une réplique de l'Entité.</summary>
    public const int ReplicaMax = 1200;

    /// <summary>Paroles codées au plus par réplique.</summary>
    public const int ParolesMax = 3;

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
        /// <summary>La ligne du journal de la manifestation qui a ouvert le dialogue (0 : pas encore).</summary>
        public long Reception;
        public bool Comprise;
        public DateTime Contact = DateTime.UtcNow;
        public string? Attend;
        public string? Erreur;
        public required MondeSimule Monde;
        /// <summary>La base de son monde : si l'on change de monde, le dialogue se clôt dans la sienne, pas dans celle du nouveau.</summary>
        public required DepotMonde Depot;
    }

    private readonly Lock _verrou = new();
    private EnCours? _courant;
    /// <summary>Un rêve demandé pendant qu'il veille : le dialogue s'ouvrira quand il s'endormira (le monde continue d'ici là).</summary>
    private (int Humain, int Priere, MondeSimule Monde)? _attente;
    private MondeSimule? _monde;
    private int _dernierePriere = -1;

    // ───────────── Ouvrir, dire, finir ─────────────

    /// <summary>Ouvre un dialogue avec un humain vivant (ou rend celui qui est ouvert avec lui).</summary>
    public DialogueDto? Ouvrir(int humain, string mode, int priere, bool attendre = false)
    {
        if (!entite.PourConversations) throw new ArgumentException(L.T("llm.erreur.usageConversations"));
        if (string.IsNullOrEmpty(entite.Connexion.Modele)) throw new ArgumentException(L.T("llm.erreur.pasDeModele"));
        // Plafond de dépense du mois atteint (0.38.0) : pas de dialogue avec un LLM du cloud.
        if (entite.Suspendu(entite.Connexion) || entite.Suspendu(entite.ConnexionPourJuge)) throw new ArgumentException(depenses.MessagePlafond());
        if (!Enum.TryParse<ModeParole>(mode, out var voie) || Array.IndexOf(Voies, voie) < 0) throw new ArgumentException(L.T("dlg.erreur.voie"));
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        if (humain < 1 || humain > humains.Count || !humains[humain - 1].EstVivant) throw new ArgumentException(L.T("erreur.humainVivant"));
        if (humains[humain - 1].EstBebe(maintenant)) throw new ArgumentException(L.T("dlg.erreur.bebe"));
        // On ne parle en rêve qu'à un dormeur : sinon, on attend qu'il s'endorme (0.36.4).
        if (voie == ModeParole.Reve && !monde.Dort(humain) && !(_courant is { } deja && deja.Humain == humain && ReferenceEquals(deja.Monde, monde)))
        {
            if (!attendre) throw new ArgumentException(L.T("dlg.erreur.eveille", ("nom", humains[humain - 1].Nom)));
            lock (_verrou) _attente = (humain, priere, monde);
            _ = hub.Clients.Diffuser("DialogueAttente", () => Attente());
            return null;
        }
        lock (_verrou)
        {
            _attente = null;
            if (_courant is { } c)
            {
                if (c.Humain == humain && ReferenceEquals(c.Monde, monde)) return Dto(c.Id);
                if (c.Attend is not null) throw new ArgumentException(L.T("dlg.erreur.occupe"));
                Finir(c, FinDialogue.Joueur);
            }
            if (priere != 0 && monde.Prieres().FirstOrDefault(p => p.Id == priere)?.HumainId != humain) priere = 0;
            var id = service.Depot.AjouterDialogue(new DialogueEntite(0, humain, (int)voie, priere, maintenant, 0, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                entite.Connexion.Libelle, 0, 0, 0, -1, false, 0));
            _courant = new EnCours { Id = id, Humain = humain, Mode = voie, Priere = priere, Monde = monde, Depot = service.Depot };
            monde.EnDialogue = true;
        }
        _ = Diffuser();
        return Dto(_courant!.Id);
    }

    /// <summary>Le rêve attendu (humain, nom), ou null.</summary>
    public AttenteDto? Attente()
    {
        (int Humain, int Priere, MondeSimule Monde)? a;
        lock (_verrou) a = _attente;
        if (a is not { } x) return null;
        var (_, humains) = x.Monde.InstantaneHumains();
        return new AttenteDto(x.Humain, x.Humain <= humains.Count ? humains[x.Humain - 1].Nom : "?");
    }

    /// <summary>Le joueur renonce à attendre son sommeil.</summary>
    public void AnnulerAttente()
    {
        lock (_verrou) _attente = null;
        _ = hub.Clients.Diffuser("DialogueAttente", () => (AttenteDto?)null);
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
        var d = c.Depot.Dialogue(c.Id);
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
            c.Depot.MettreAJourDialogue(d with { Fin = c.Monde.Secondes, Statut = 1, FinRaison = (int)fin, Repliques = c.Rang, Croit = c.Croit is { } b ? b ? 1 : 0 : -1,
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
        var precedentes = c.Depot.Repliques(c.Id);
        var brut = new StringBuilder();

        // 1. Traduire ses mots en paroles codées.
        List<ActeLlm> paroles;
        TonParole ton;
        string revelation, impossible;
        try
        {
            (paroles, ton, revelation, impossible) = await Traduire(c, texte, precedentes, entite.ConnexionPourJuge, brut, arret);
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
        List<TemoinDto> temoins = [];
        // Le dialogue est une seule manifestation : la première réplique l'ouvre, les suivantes et leurs paroles n'en sont pas de nouvelles.
        if (c.Reception == 0)
        {
            var (rec, pm, dm) = monde.Executer(m => m.Manifester(c.Humain, c.Mode, ClientLlm.Couper(texte, 240)));
            c.Reception = rec;
            Plage(pm, dm);
            if (c.Mode == ModeParole.Apparition) temoins = [.. monde.DerniersTemoins().Select(x => new TemoinDto(x.Humain, x.Croit))];
        }
        foreach (var a in paroles)
        {
            try
            {
                var parole = Enum.Parse<Parole>(a.Parole ?? "", true);
                var croyait = c.Croit;
                var (recit, p, d) = monde.Executer(m => m.ParlerEnDialogue(c.Humain, parole, c.Mode, a.Cible ?? 0, a.Param ?? 0, c.Reception, croyait));
                var fait = Registre.Chacun(monde, [(c.Humain, recit)]);
                Registre.Noter(service, Registre.Joueur, a, fait, p, d);
                Plage(p, d);
                var r = monde.DerniereReponse(c.Humain);
                if (r is { } rr) reponses.Add(rr);
                var t = string.Join(" ", fait.Resultats);
                issues.Add((a, r, t));
                regles.Add($"{Libelle(a)} → {t}");
                // Ce qu'il a fait de cette parole dit s'il y croit, pour les suivantes.
                if (r is { } x && x != Reponse.Incomprise) c.Croit = x != Reponse.Incrue;
            }
            catch (ArgumentException e)
            {
                regles.Add($"{Libelle(a)} → {L.T("llm.resultat.refuse", ("raison", e.Message))}");
            }
        }
        // Rien à faire dans ses mots : on le dit, pour que le joueur sache que l'humain n'a reçu aucun ordre (0.44.4).
        // Un ordre qu'aucun geste ne dit n'est pas tu (0.44.11) : le joueur sait qu'il ne sera pas exécuté.
        if (impossible.Length > 0) regles.Add(L.T("dlg.r.impossible", ("t", impossible)));
        else if (paroles.Count == 0) regles.Add(L.T("dlg.r.aucuneParole"));
        var (regle, p2, d2) = monde.Executer(m => m.Ecouter(c.Humain, c.Mode, rang, c.Croit, reponses, c.Reception));
        Plage(p2, d2);
        c.Comprise |= regle.Comprise;
        if (regle.Comprise || c.Croit is not null) c.Croit = regle.Croit;
        regles.Add(L.T(!regle.Comprise ? "dlg.r.incomprise" : regle.Croit ? "dlg.r.croit" : "dlg.r.doute"));
        // Ce que ses mots lui font ressentir, et ce qu'elle lui révèle, s'il a compris (0.42.9).
        if (regle.Comprise && regle.Fin != FinDialogue.Absent)
        {
            var croit = regle.Croit;
            var (ressenti, p3, d3) = monde.Executer(m => m.RessentirTon(c.Humain, ton, c.Mode, croit, c.Reception));
            Plage(p3, d3);
            if (ressenti is not null) regles.Add(L.Rediger(ressenti));
            if (revelation.Length > 0)
            {
                try
                {
                    var (_, p4, d4) = monde.Executer(m => m.RevelerEnDialogue(c.Humain, revelation, c.Mode, croit, c.Reception));
                    Plage(p4, d4);
                    regles.Add(L.T("dlg.r.revelation", ("t", ClientLlm.Couper(revelation, 200))));
                }
                catch (ArgumentException e)
                {
                    regles.Add(L.T("llm.resultat.refuse", ("raison", e.Message)));
                }
            }
        }
        if (regle.Fin != FinDialogue.Aucune) regles.Add(L.T($"dlg.r.fin.{regle.Fin}"));
        Reagir(c, rang, regle, reponses, temoins);
        service.Sauvegarder();

        // 3. L'humain parle (sauf s'il n'est plus là pour répondre).
        string mots = "", pensee = "", sens = "", juge = "", lu = "";
        string? erreur = null;
        var libreTextes = new List<string>();
        string libreJson = "";
        var (_, humains) = monde.InstantaneHumains();
        var h = humains[c.Humain - 1];
        if (regle.Fin != FinDialogue.Absent && h.EstVivant)
        {
            var libre = reglages.Libre;
            var permis = Permis(h, humains);
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
                    var reponse = await client.Demander(connexion, consigne, messages, Schema(libre), delai.Token, reprise: essai > 0);
                    brut.AppendLine($"---- {L.T("dlg.trace.humain")} ({essai + 1}) ----").AppendLine(reponse);
                    if (!Lire(reponse, out var m, out var pe, out var se, out var acte, out var faute))
                    {
                        erreur = faute;
                        // Une réponse coupée en pleine boucle n'est pas renvoyée en entier : le modèle y repartirait.
                        messages.Add(("assistant", ClientLlm.Couper(reponse, 800)));
                        messages.Add(("user", L.T("llm.d.corriger", ("erreurs", faute))));
                        continue;
                    }
                    // En rêve, il répond en esprit : pas de mots dits tout haut (0.42.8).
                    if (c.Mode == ModeParole.Reve) m = "";
                    // Il parle sa langue : seulement ses mots, des noms qu'il connaît, des gestes et des cris (0.36.5).
                    var inconnus = Inconnus(m, permis);
                    // Il répond à l'Entité (0.42.8) : une réponse vide, ou qui n'est que sa pensée, ne convient pas.
                    var sansReponse = se.Length == 0 || Normaliser(se) == Normaliser(pe);
                    // Il ne redit pas mot pour mot une réponse déjà faite, et parle à l'Entité, pas d'elle (0.44.10) : un petit modèle
                    // redisait « C'est Faepe, mon enfant… » à « Mon Monde s'appelle LA TERRE », et « elle se nomme Michel ».
                    var repete = !sansReponse && precedentes.Exists(r => r.Sens.Length > 0 && Normaliser(r.Sens) == Normaliser(se));
                    var tiers = !sansReponse && ParleDElle(se);
                    if ((inconnus.Count > 0 || sansReponse || repete || tiers) && essai == 0)
                    {
                        messages.Add(("assistant", reponse));
                        messages.Add(("user", inconnus.Count > 0
                            ? L.T("dlg.erreur.motsInconnus2", ("liste", string.Join(", ", inconnus.Take(20))), ("langue", L.Courante.Nom))
                            : sansReponse ? L.T("dlg.erreur.reponseVide", ("langue", L.Courante.Nom))
                            : repete ? L.T("dlg.erreur.repete", ("texte", texte))
                            : L.T("dlg.erreur.tiers", ("texte", texte))));
                        if (inconnus.Count == 0 && !sansReponse) brut.AppendLine($"---- {L.T(repete ? "dlg.trace.repete" : "dlg.trace.tiers")} ----");
                        continue;
                    }
                    if (inconnus.Count > 0) m = Effacer(m, permis);
                    (mots, pensee, sens) = (m, pe, se);
                    if (acte is not null && !libre) acte = null;
                    string? fauteLibre = null;
                    if (acte is not null) acte = Completer(acte, c.Humain);
                    if (acte is not null) fauteLibre = VerifierLibre(acte, c.Humain, humains) ?? Contredit(acte, h, regle.Croit);
                    // Une parole ne change pas un cœur d'un coup : chaque changement est borné selon son caractère (0.36.4).
                    if (acte is not null && fauteLibre is null) acte = Borner(acte, h, humains);
                    // Puis les règles des actes libres, sans rien changer : une faute donne un second essai, pas un refus.
                    if (acte is not null && fauteLibre is null)
                    {
                        try
                        {
                            Libres.Verifier(monde, acte);
                        }
                        catch (ArgumentException e)
                        {
                            fauteLibre = e.Message;
                        }
                    }
                    // Le juge relit ses mots (et sa réaction libre) au regard de sa fiche et de ce que les règles ont décidé.
                    if (fauteLibre is null && reglages.Juge)
                    {
                        using var delaiJuge = CancellationTokenSource.CreateLinkedTokenSource(arret);
                        delaiJuge.CancelAfter(DelaiReponse);
                        var avis = await client.Demander(entite.ConnexionPourJuge, L.T("dlg.juge.consigne", ("langue", L.Courante.Nom)),
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
                    // Rejoindre une communauté est une intention : il part à pied vers son camp, les règles du monde font le reste.
                    var rejoindre = acte?.Changements?.Where(x => x.Quoi == "communaute").ToList() ?? [];
                    if (rejoindre.Count > 0 && ReferenceEquals(monde, service.Monde))
                    {
                        acte = acte!.Changements!.Count == rejoindre.Count ? null : acte with { Changements = [.. acte.Changements!.Where(x => x.Quoi != "communaute")] };
                        foreach (var x in rejoindre.Take(1))
                        {
                            if (x.Communaute == h.CommunauteId) continue;
                            try
                            {
                                var (_, p, d) = monde.Executer(m => m.PartirRejoindre(c.Humain, x.Communaute, c.Reception));
                                Plage(p, d);
                                var nom = monde.Communautes().FirstOrDefault(k => k.Id == x.Communaute) is { } k ? L.Communaute(k.Nom) : "?";
                                libreTextes.Add(L.T("dlg.libre.rejoindre", ("nom", h.Nom), ("c", nom)));
                            }
                            catch (ArgumentException)
                            {
                                libreTextes.Add(L.T("dlg.libre.rejoindreVide"));
                            }
                        }
                    }
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
                        catch (ArgumentException e) when (essai == 0)
                        {
                            // Refusé par les règles : il a un second essai, avec la raison.
                            libreJson = "";
                            messages.Add(("assistant", reponse));
                            messages.Add(("user", L.T("dlg.corriger", ("raison", e.Message))));
                            continue;
                        }
                        catch (ArgumentException e)
                        {
                            libreTextes.Add(L.T("llm.resultat.refuse", ("raison", e.Message)));
                        }
                    }
                    // Ce qu'il demande de lui-même à l'Entité devient une prière ; ce qu'il lui promet, il le tiendra ou non (0.42.9).
                    LireEngagements(reponse, out var demandeLui, out var pour, out var promesse);
                    // Ce que son corps fait (0.45.19) : les gestes de sa réponse, sinon ceux que son texte dit faire (« je ferme les yeux »).
                    var gestes = LireGestes(reponse);
                    if (gestes.Count == 0) gestes = GestesDits.Lire(se);
                    Gesticuler(c.Id, rang, string.Join(",", gestes));
                    if (gestes.Count > 0 && c.Mode != ModeParole.Reve && ReferenceEquals(monde, service.Monde) && h.EstVivant)
                    {
                        var (_, p, d) = monde.Executer(x => x.NoterGestes(c.Humain, gestes, c.Reception));
                        Plage(p, d);
                    }
                    if (ReferenceEquals(monde, service.Monde) && h.EstVivant)
                    {
                        if (demandeLui is { } sujet)
                        {
                            var (ev, p, d) = monde.Executer(x => x.DemanderEnDialogue(c.Humain, sujet, pour, c.Reception));
                            Plage(p, d);
                            if (ev != 0) libreTextes.Add(L.T("dlg.r.demande", ("nom", h.Nom), ("sujet", L.T($"sujetPriere.{sujet}"))));
                        }
                        if (promesse is { } geste)
                        {
                            var (ev, p, d) = monde.Executer(x => x.Promettre(c.Humain, geste, c.Mode, c.Reception));
                            Plage(p, d);
                            if (ev != 0) libreTextes.Add(L.T("dlg.r.promesse", ("nom", h.Nom), ("geste", L.T($"parole.{geste}"))));
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
        c.Depot.AjouterReplique(new RepliqueEntite(0, c.Id, rang, monde.Secondes, texte, JsonSerializer.Serialize(paroles, Actes.Json),
            JsonSerializer.Serialize(regles, Actes.Json), mots, pensee, JsonSerializer.Serialize(new { acte = libreJson, resultats = libreTextes }, Actes.Json), juge, lu,
            brut.ToString(), chrono.ElapsedMilliseconds, premier, dernier, erreur, sens));
        if (trace.Active)
            trace.Replique(c.Id, rang, h.Nom, texte, mots, sens, pensee, regles.Concat(libreTextes), lu, brut.ToString(), chrono.ElapsedMilliseconds, erreur);
        var d0 = c.Depot.Dialogue(c.Id);
        if (d0 is not null)
            c.Depot.MettreAJourDialogue(d0 with { Repliques = rang, Croit = c.Croit is { } b ? b ? 1 : 0 : -1, Comprise = c.Comprise });
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
            ["ton"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Enum.GetNames<TonParole>().Select(x => (JsonNode)x)]) },
            ["revelation"] = new JsonObject { ["type"] = "string", ["maxLength"] = MondeSimule.RevelationMax },
            ["impossible"] = new JsonObject { ["type"] = "string", ["maxLength"] = 300 },
            ["paroles"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["parole"] = new JsonObject { ["type"] = "string" },
                        ["mots"] = new JsonObject { ["type"] = "string" },
                        ["cible"] = new JsonObject { ["type"] = "integer" },
                        ["direction"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 7 },
                        ["param"] = new JsonObject { ["type"] = "integer" },
                        // Ces mots sont-ils une question plutôt qu'un ordre ou un conseil (0.39.0) ?
                        ["question"] = new JsonObject { ["type"] = "boolean" },
                    },
                    ["required"] = new JsonArray("parole", "mots", "question"),
                },
            },
        },
        // Le ton de ses mots et ce qu'elle révèle (0.42.9).
        ["required"] = new JsonArray("paroles", "ton", "revelation"),
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

    private async Task<(List<ActeLlm> Paroles, TonParole Ton, string Revelation, string Impossible)> Traduire(EnCours c, string texte, List<RepliqueEntite> precedentes, ConnexionLlm connexion, StringBuilder brut,
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
            if (r.Mots.Length > 0 || r.Sens.Length > 0) d.AppendLine($"{h.Nom} : « {ClientLlm.Couper(r.Sens.Length > 0 ? r.Sens : r.Mots, 300)} »");
        }
        d.AppendLine();
        d.AppendLine(L.T("dlg.t.dit", ("texte", texte)));
        var messages = new List<(string, string)> { ("user", d.ToString()) };
        var valides = new List<ActeLlm>();
        var (ton, revelation, impossible) = (TonParole.Aucun, "", "");
        for (var essai = 0; essai < 2; essai++)
        {
            using var delai = CancellationTokenSource.CreateLinkedTokenSource(arret);
            delai.CancelAfter(DelaiReponse);
            var reponse = await client.Demander(connexion, consigne, messages, SchemaTraduction(), delai.Token);
            brut.AppendLine($"---- {L.T("dlg.trace.traduction")} ({essai + 1}) ----").AppendLine(reponse);
            var fautes = new List<string>();
            valides = LireTraduction(reponse, texte, c.Humain, c.Mode, humains, fautes, monde);
            LireTon(reponse, out ton, out revelation);
            impossible = LireImpossible(reponse);
            // Le nom qu'elle se donne est toujours une révélation (0.44.10) : la traduction l'avait laissé tomber.
            if (NomDonne(texte) is { } nomEntite && !revelation.Contains(nomEntite, StringComparison.OrdinalIgnoreCase))
                revelation = (revelation.Length > 0 ? revelation + " " : "") + L.T("dlg.r.nomEntite", ("nom", nomEntite));
            if (fautes.Count == 0 || essai == 1) break;
            messages.Add(("assistant", reponse));
            messages.Add(("user", L.T("llm.d.corriger", ("erreurs", string.Join(" ", fautes)))));
        }
        // Une question n'apprend rien (0.44.8) : « qui est le père de Thounouvial ? » était devenu une révélation.
        if (revelation.Length > 0 && SeulementDesQuestions(texte))
        {
            brut.AppendLine($"---- {L.T("dlg.trace.revelationQuestion")} ----").AppendLine(revelation);
            revelation = "";
        }
        // Les gestes demandés en toutes lettres que la traduction a manqués (0.44.4) : sans eux, « assieds-toi et prie » restait sans effet.
        foreach (var g in GestesLus.Lire(texte))
        {
            if (valides.Count >= ParolesMax) break;
            if (valides.Exists(v => v.Parole == g.ToString())) continue;
            valides.Add(new ActeLlm { Type = Actes.Parler, Humains = [c.Humain], Parole = g.ToString(), Mode = c.Mode.ToString(), Cible = 0 });
            brut.AppendLine($"---- {L.T("dlg.trace.lu")} ----").AppendLine(g.ToString());
        }
        // Un ordre sans geste correspondant (0.44.11) : seulement si rien d'autre n'est demandé et que ce n'est pas une question,
        // pour ne jamais dire « pas exécuté » d'un ordre qui l'est.
        if (valides.Count > 0 || SeulementDesQuestions(texte)) impossible = "";
        if (impossible.Length > 0) brut.AppendLine($"---- {L.T("dlg.trace.impossible")} ----").AppendLine(impossible);
        return (valides, ton, revelation, impossible);
    }

    private static readonly System.Text.RegularExpressions.Regex MotifNom = new(
        @"\b(?:mon\s+nom\s+est|je\s+m['’]appelle|je\s+me\s+nomme|on\s+m['’]appelle|appelle[-\s]moi|my\s+name\s+is|i\s+am\s+called|call\s+me)\s+([\p{L}][\p{L}'’-]*)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex MotifTiers = new(
        @"\b(?:elle|il)\s+(?:dit|a\s+dit|disait|se\s+nomme|s['’]appelle|me\s+parle|m['’]a\s+parlé|demande|me\s+demande|a\s+demandé)\b|\b(?:une|cette|la)\s+(?:présence|entité)(?:\s+\p{L}+)?\s+(?:qui|m['’]|me|dit|parle)|\b(?:she|it)\s+(?:says|said|is\s+called|asks|asked|speaks\s+to\s+me|spoke\s+to\s+me)\b|\bher\s+name\s+is\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Le nom que l'Entité se donne dans ses mots (« Mon nom est MICHEL »), ou null (0.44.10).</summary>
    public static string? NomDonne(string texte)
    {
        if (MotifNom.Match(texte) is not { Success: true } m) return null;
        var nom = m.Groups[1].Value.Trim('\'', '’', '-');
        return nom.Length >= 2 ? nom : null;
    }

    /// <summary>Vrai s'il parle de l'Entité comme d'une autre au lieu de lui répondre (« elle se nomme Michel », 0.44.10).</summary>
    public static bool ParleDElle(string reponse) => MotifTiers.IsMatch(reponse.Replace('’', '\''));

    /// <summary>Ce qu'elle lui demande et qu'aucune parole ne dit, selon la traduction ("" : rien), 0.44.11.</summary>
    public static string LireImpossible(string reponse)
    {
        try
        {
            if (JsonNode.Parse(ClientLlm.Nettoyer(reponse), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) is JsonObject o
                && o["impossible"] is JsonValue v && v.TryGetValue<string>(out var s))
            {
                s = ClientLlm.Couper(s.Trim(), 300);
                // « aucun », « rien », « - » : rien d'impossible.
                return s.Length >= 6 ? s : "";
            }
        }
        catch (JsonException)
        {
            // Illisible : rien.
        }
        return "";
    }

    /// <summary>Vrai si les mots de l'Entité ne sont que des questions : chaque phrase finit par « ? » (0.44.8).</summary>
    public static bool SeulementDesQuestions(string texte)
    {
        var phrases = System.Text.RegularExpressions.Regex.Matches(texte, @"[^.!?…\n]+[.!?…]*").Select(m => m.Value.Trim())
            .Where(p => p.Any(char.IsLetter)).ToList();
        return phrases.Count > 0 && phrases.All(p => p.EndsWith('?'));
    }

    /// <summary>
    /// Le ton des mots de l'Entité et ce qu'elle révèle, tels que la traduction les a entendus (0.42.9). Un ton inconnu vaut
    /// « aucun » ; une révélation est vide si elle ne révèle rien.
    /// </summary>
    public static void LireTon(string reponse, out TonParole ton, out string revelation)
    {
        (ton, revelation) = (TonParole.Aucun, "");
        try
        {
            if (JsonNode.Parse(ClientLlm.Nettoyer(reponse), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) is not JsonObject o) return;
            if (o["ton"] is JsonValue t && t.TryGetValue<string>(out var s) && Enum.TryParse<TonParole>(s.Trim(), true, out var x) && Enum.IsDefined(x)) ton = x;
            if (o["revelation"] is JsonValue r && r.TryGetValue<string>(out var v))
            {
                v = ClientLlm.Resserrer(v.Trim(), MondeSimule.RevelationMax);
                // « aucune », « rien », « - » : il n'y a rien de révélé.
                if (v.Length >= 8) revelation = v;
            }
        }
        catch (JsonException)
        {
            // Illisible : ni ton, ni révélation.
        }
    }

    /// <summary>Lit la traduction : les paroles valides (au plus <see cref="ParolesMax"/>), les autres dans <paramref name="fautes"/>.</summary>
    public static List<ActeLlm> LireTraduction(string reponse, string texte, int humain, ModeParole mode, IReadOnlyList<Humain> humains, List<string> fautes,
        MondeSimule? monde = null)
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
            // L'extrait de ses mots qui porte la parole : il doit s'y trouver tel quel, et ne pas être une question.
            var extrait = x["mots"] is JsonValue ve && ve.TryGetValue<string>(out var se) ? se.Trim() : "";
            // Le LLM dit lui-même si ces mots sont une question (0.39.0) : une question, même sans « ? », n'est pas un ordre.
            bool? question = x["question"] is JsonValue vq && vq.TryGetValue<bool>(out var q) ? q : null;
            if (question == true) continue;
            switch (Extrait(texte, extrait, question == false))
            {
                case 1:
                    fautes.Add(L.T("dlg.erreur.extrait", ("parole", p)));
                    continue;
                case 2:
                    fautes.Add(L.T("dlg.erreur.question", ("parole", p), ("mots", extrait)));
                    continue;
            }
            var cible = Entier("cible") ?? 0;
            if (Paroles.VeutCible(p) && (cible < 1 || cible > humains.Count || cible == humain || !humains[cible - 1].EstVivant))
            {
                fautes.Add(L.T("dlg.erreur.cible", ("parole", p)));
                continue;
            }
            // Celui qu'elle désigne doit être nommé dans ses mots : sinon le LLM a deviné un numéro parmi ses proches.
            if (Paroles.VeutCible(p) && !(" " + Normaliser(texte) + " ").Contains(" " + Normaliser(humains[cible - 1].Nom) + " ", StringComparison.Ordinal))
            {
                fautes.Add(L.T("dlg.erreur.cibleNom", ("parole", p), ("nom", humains[cible - 1].Nom)));
                continue;
            }
            var a = new ActeLlm
            {
                Type = Actes.Parler, Humains = [humain], Parole = p.ToString(), Mode = mode.ToString(), Cible = Paroles.VeutCible(p) ? cible : 0,
                Param = (p == Parole.PartsVers ? Entier("direction") ?? Entier("param") : Entier("param") ?? Entier("direction")),
            };
            try
            {
                Actes.Verifier(a, FamillesLlm.Paroles, monde: monde);
                if (!valides.Any(y => y.Parole == a.Parole)) valides.Add(a);
            }
            catch (ArgumentException e)
            {
                fautes.Add(e.Message);
            }
        }
        return valides;
    }

    /// <summary>Pour comparer des mots : minuscules, sans accents, sans ponctuation, espaces simples.</summary>
    private static string Normaliser(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var b = new StringBuilder(d.Length);
        var espace = true;
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                b.Append(char.ToLowerInvariant(ch));
                espace = false;
            }
            else if (!espace)
            {
                b.Append(' ');
                espace = true;
            }
        }
        return b.ToString().Trim();
    }

    /// <summary>
    /// 0 : l'extrait est bien dans ses mots ; 1 : il n'y est pas (ou il est vide) ; 2 : il est dans une question (une phrase qui finit par
    /// « ? ») : on ne traduit pas une question en ordre.
    /// </summary>
    /// <param name="ordreSelonLeLlm">
    /// Le LLM dit que ce n'est pas une question : une phrase qui finit par « ? » compte alors si ce qui reste autour de l'ordre n'est
    /// qu'une courte queue de question (« Va vers l'est, veux-tu ? », « Pars demain, d'accord ? ») ; trois mots au plus.
    /// </param>
    public static int Extrait(string texte, string extrait, bool ordreSelonLeLlm = false)
    {
        var e = Normaliser(extrait);
        if (e.Length == 0) return 1;
        // Les phrases de ses mots, chacune avec son point final.
        var phrases = System.Text.RegularExpressions.Regex.Matches(texte, @"[^.!?…]+[.!?…]*").Select(m => m.Value).ToList();
        var trouve = false;
        foreach (var ph in phrases)
        {
            if (!(" " + Normaliser(ph) + " ").Contains(" " + e + " ", StringComparison.Ordinal)) continue;
            trouve = true;
            if (!ph.TrimEnd().EndsWith('?')) return 0;
            if (ordreSelonLeLlm && QueueDeQuestion(Normaliser(ph), e)) return 0;
        }
        if (!trouve && (" " + Normaliser(texte) + " ").Contains(" " + e + " ", StringComparison.Ordinal)) return 0;
        return trouve ? 2 : 1;
    }

    /// <summary>Ce qui reste de la phrase autour de l'ordre est une queue de question courte (trois mots au plus), après l'ordre.</summary>
    private static bool QueueDeQuestion(string phrase, string ordre)
    {
        var i = (" " + phrase + " ").IndexOf(" " + ordre + " ", StringComparison.Ordinal);
        if (i < 0) return false;
        var avant = phrase[..Math.Min(i, phrase.Length)].Trim();
        var apres = phrase[Math.Min(phrase.Length, i + ordre.Length)..].Trim();
        return avant.Length == 0 && apres.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 1 and <= 3;
    }

    // ───────────── Faire parler l'humain ─────────────

    private const string FormeHumain = """{"reponse":"…","mots":"…","pensee":"…","gestes":[],"demande":"","pour":0,"promesse":""}""";

    private const string FormeHumainLibre = """{"reponse":"…","mots":"…","pensee":"…","gestes":[],"demande":"","pour":0,"promesse":"","changements":[{"quoi":"…","humain":n°,"valeur":x},{"quoi":"lien","humain":n°,"autre":n°,"valeur":x}]}""";

    /// <summary>Longueur maximale (caractères) des mots d'une réplique dans le schéma imposé au LLM.</summary>
    public const int MotsMax = 600;

    private static JsonObject Schema(bool libre)
    {
        var proprietes = new JsonObject
        {
            // Ce qu'il répond à l'Entité, en clair (0.42.8) ; ce qu'il dit à voix haute dans sa langue ; ce qu'il pense sans le dire.
            ["reponse"] = Chaine(MotsMax), ["mots"] = Chaine(MotsMax), ["pensee"] = Chaine(MotsMax * 2),
            // Ce qu'il demande de lui-même à l'Entité, pour qui, et ce qu'il lui promet (0.42.9) ; "" : rien.
            ["demande"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Enum.GetNames<SujetPriere>().Prepend("").Select(x => (JsonNode)x)]) },
            ["pour"] = new JsonObject { ["type"] = "integer" },
            // Ce que fait son corps en répondant (0.45.11), jusqu'à trois gestes à la suite (0.45.19) : le Monde 3D le montre.
            ["gestes"] = new JsonObject
            {
                ["type"] = "array", ["maxItems"] = GestesMax,
                ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Gestes.Select(x => (JsonNode)x)]) },
            },
            ["promesse"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Promesses.Select(x => x.ToString()).Prepend("").Select(x => (JsonNode)x)]) },
        };
        // Longueur bornée dans la grammaire imposée (0.37.6) : vu le 8/11, gemma3:4b répétait « shuhei, » dans « mots » jusqu'à la limite
        // de jetons, deux essais de suite ; bornée, la chaîne se ferme et la réponse reste lisible (les répétitions sont resserrées après).
        static JsonObject Chaine(int max) => new() { ["type"] = "string", ["maxLength"] = max };
        // La forme complète de chaque changement, sortes en liste fermée (0.36.3) : sans elle, le LLM devinait les champs (« autre »).
        if (libre)
            proprietes["changements"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["quoi"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. SortesLibres.Select(x => (JsonNode)x)]) },
                        ["humain"] = new JsonObject { ["type"] = "integer" },
                        ["autre"] = new JsonObject { ["type"] = "integer" },
                        ["communaute"] = new JsonObject { ["type"] = "integer" },
                        ["valeur"] = new JsonObject { ["type"] = "number" },
                        ["texte"] = new JsonObject { ["type"] = "string" },
                        ["genre"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Enum.GetNames<TypeIntime>().Select(x => (JsonNode)x)]) },
                    },
                    ["required"] = new JsonArray("quoi"),
                },
            };
        return new JsonObject { ["type"] = "object", ["properties"] = proprietes, ["required"] = new JsonArray("reponse", "mots", "pensee") };
    }

    /// <summary>
    /// Ce que son corps peut faire en répondant, montré dans le Monde 3D (0.45.11) : vu le 17/11, « Je me mets à genou, Entité » et
    /// l'humain restait debout, le geste n'étant tiré que de son caractère. Fuir ou se détourner restent décidés par les règles.
    /// </summary>
    /// <remarks>
    /// 0.45.19 : vu le 18/11, « Je ferme les yeux » et les yeux de Meisen restaient ouverts : une trentaine d'actions du visage, du
    /// corps et des déplacements, jusqu'à <see cref="GestesMax"/> à la suite.
    /// </remarks>
    public static readonly string[] Gestes =
    [
        // Visage.
        "FermerYeux", "OuvrirYeux", "CacherYeux", "Couvrir", "RegarderCiel", "RegarderSol", "Sourire", "Crier", "Pleurer",
        // Corps, sur place.
        "Debout", "Asseoir", "Genoux", "Prosterne", "CoucherDos", "CoucherVentre", "Recroqueviller", "Sauter", "TournerSurSoi", "Danser",
        "LeverBras", "Tendre", "Montrer", "FrapperSol", "TournerDos", "Hausser", "Refuser", "Reculer",
        // Se déplacer.
        "MarcherVers", "CourirVers", "Eloigner", "Fuir", "TournerAutour", "Rejoindre", "Revenir",
    ];

    /// <summary>Gestes d'une même réponse, au plus.</summary>
    public const int GestesMax = 3;

    /// <summary>
    /// Les gestes qu'il fait en répondant (de <see cref="Gestes"/>, dans l'ordre, au plus <see cref="GestesMax"/>) ; le champ
    /// « geste » des versions d'avant 0.45.19 est lu aussi.
    /// </summary>
    public static List<string> LireGestes(string reponse)
    {
        var liste = new List<string>();
        try
        {
            if (JsonNode.Parse(ClientLlm.Nettoyer(reponse), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) is not JsonObject o)
                return liste;
            IEnumerable<JsonNode?> noeuds = o["gestes"] is JsonArray a ? a : o["gestes"] is JsonValue v1 ? [v1] : [];
            if (o["geste"] is JsonValue v2) noeuds = noeuds.Append(v2);
            foreach (var n in noeuds)
                if (n is JsonValue v && v.TryGetValue<string>(out var g) && Array.Find(Gestes, x => string.Equals(x, g.Trim(), StringComparison.OrdinalIgnoreCase)) is { } x
                    && !liste.Contains(x) && liste.Count < GestesMax)
                    liste.Add(x);
        }
        catch (JsonException)
        {
            // Illisible : aucun geste.
        }
        return liste;
    }

    /// <summary>Les gestes qu'il peut promettre à l'Entité : ceux de tous les jours, sans personne à désigner (0.42.9).</summary>
    public static readonly Parole[] Promesses = [.. Enum.GetValues<Parole>().Where(p => Paroles.Demande(p) && !Paroles.VeutCible(p))];

    /// <summary>
    /// Ce qu'il demande de lui-même à l'Entité (une prière : guérison, nourriture…), pour qui (0 : lui-même ou les siens), et le
    /// geste qu'il lui promet ; null : rien (0.42.9).
    /// </summary>
    public static void LireEngagements(string reponse, out SujetPriere? demande, out int pour, out Parole? promesse)
    {
        (demande, pour, promesse) = (null, 0, null);
        try
        {
            if (JsonNode.Parse(ClientLlm.Nettoyer(reponse), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) is not JsonObject o) return;
            if (o["demande"] is JsonValue d && d.TryGetValue<string>(out var s) && s.Trim().Length > 0 && Enum.TryParse<SujetPriere>(s.Trim(), true, out var x)
                && Enum.IsDefined(x))
                demande = x;
            if (o["pour"] is JsonValue v && v.TryGetValue<int>(out var n)) pour = n;
            if (o["promesse"] is JsonValue q && q.TryGetValue<string>(out var t) && t.Trim().Length > 0 && Enum.TryParse<Parole>(t.Trim(), true, out var g)
                && Promesses.Contains(g))
                promesse = g;
        }
        catch (JsonException)
        {
            // Illisible : ni demande, ni promesse.
        }
    }

    private string Consigne(Humain h, EnCours c, bool libre)
    {
        var langage = LangageDto.Depuis(h, c.Monde.InstantaneHumains().Humains, c.Monde.Communautes(), c.Monde.Flore);
        // En rêve, il répond en esprit ; par la voix ou devant une apparition, il peut aussi parler tout haut, dans sa langue (0.42.8).
        var voix = L.T(c.Mode == ModeParole.Reve ? "dlg.humain.esprit" : "dlg.humain.aVoixHaute", ("langue", L.Courante.Nom), ("stade", langage.Stade),
            ("n", langage.NombreDeMots));
        var s = new StringBuilder(L.T("dlg.humain.consigne2", ("nom", h.Nom), ("voie", L.T($"modeParole.{c.Mode}")), ("langue", L.Courante.Nom),
            ("voix", voix), ("forme", libre ? FormeHumainLibre : FormeHumain)));
        s.AppendLine().AppendLine();
        s.AppendLine(L.T("dlg.humain.geste", ("gestes", string.Join(", ", Gestes.Select(x => $"{x} = {L.T($"dlg.geste.{x}")}")))));
        s.AppendLine(L.T("dlg.humain.engagements", ("sujets", string.Join(", ", Enum.GetValues<SujetPriere>().Select(x => $"{x} = {L.T($"sujetPriere.{x}")}"))),
            ("gestes", string.Join(", ", Promesses.Select(x => $"{x} = {L.T($"parole.{x}")}")))));
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
        // Ce qu'il porte dans ses bras, en tête (0.44.8) : perdu au milieu de la fiche, le LLM faisait dire à Kelou qu'elle ne
        // voyait pas d'enfant près d'elle.
        if (Contexte.CeQuIlPorte(humains, maintenant, h.Id) is { } porte) d.AppendLine(L.T("dlg.d.vrai", ("t", porte)));
        Contexte.Humain(d, service, h.Id, soiMeme: true);
        // Ses mots, tous (les plus sûrs d'abord) : ce sont les seuls qu'il peut dire.
        var vocabulaire = h.Vocabulaire.Where(m => !m.Signe).OrderByDescending(m => m.Force).Take(200)
            .Select(m => $"{m.Forme} = {Lib.Idee(m.Idee, monde.Flore)}").ToList();
        d.AppendLine(vocabulaire.Count > 0 ? L.T("dlg.d.mots", ("liste", string.Join(", ", vocabulaire))) : L.T("dlg.d.aucunMot"));
        d.AppendLine(L.T("dlg.d.noms", ("liste", string.Join(", ", Noms(h, humains)))));
        if (c.Priere != 0 && Religions.Prieres(service, enAttente: false).FirstOrDefault(p => p.Id == c.Priere) is { } pr)
            d.AppendLine(L.T("dlg.d.priere", ("texte", pr.Texte)));
        // Ce qu'il retient de ses dialogues passés avec l'Entité (0.44.10) : les mots exacts des plus récents, le sens des plus
        // anciens, ou seulement qu'elle lui a parlé ; le reste est oublié, selon sa mémoire, le temps passé et la voie.
        Souvenirs(d, h, c.Id, maintenant);
        d.AppendLine();
        d.AppendLine(L.T("dlg.d.conversation", ("voie", L.T($"modeParole.{c.Mode}"))));
        foreach (var r in precedentes.TakeLast(20))
        {
            d.AppendLine($"{L.T("dlg.t.entite")} : « {r.Entite} »");
            // Ce qu'il a répondu (en clair), et ses mots dits tout haut s'il en a dit (0.42.8).
            d.AppendLine($"{L.T("dlg.d.toiCourt")} : « {(r.Sens.Length > 0 ? r.Sens : r.Mots)} »{(r.Sens.Length > 0 && r.Mots.Length > 0 ? $" [{r.Mots}]" : "")}");
        }
        d.AppendLine($"{L.T("dlg.t.entite")} : « {texte} »");
        d.AppendLine();
        d.AppendLine(L.T("dlg.d.regles"));
        d.AppendLine("- " + L.T(!regle.Comprise ? "dlg.h.incomprise" : regle.Croit ? "dlg.h.croit" : "dlg.h.doute", ("voie", L.T($"modeParole.{c.Mode}"))));
        foreach (var (a, r, t) in issues)
            d.AppendLine("- " + L.T("dlg.h.parole", ("parole", Libelle(a)), ("issue", L.T($"dlg.h.issue.{(r ?? Reponse.Incomprise)}")), ("detail", t)));
        if (regle.Fin != FinDialogue.Aucune) d.AppendLine("- " + L.T($"dlg.h.fin.{regle.Fin}"));
        d.AppendLine();
        d.AppendLine(L.T("dlg.d.ficheVraie"));
        // Ses derniers mots redits juste avant la question (0.44.10) : un petit modèle répondait à une réplique plus ancienne
        // (« qui est ton enfant ? », « assieds-toi et prie ») au lieu de « te souviens-tu de mon nom ? ».
        d.AppendLine(L.T("dlg.d.aRepondre", ("texte", texte)));
        d.AppendLine(L.T("dlg.d.fin", ("nom", h.Nom)));
        return d.ToString();
    }

    /// <summary>Le plus de texte donné au LLM pour ses souvenirs des dialogues passés (environ 1 500 jetons).</summary>
    private const int SouvenirsMax = 6000;

    /// <summary>
    /// Ses souvenirs des dialogues passés avec l'Entité, du plus ancien au plus récent (0.44.10), tels que <see cref="SouvenirDialogue"/>
    /// les garde : chaque réplique mot pour mot, à peu près, ou plus du tout. Il reconnaît l'Entité s'il se souvient d'au moins une fois.
    /// </summary>
    private void Souvenirs(StringBuilder d, Humain h, long dialogueEnCours, long maintenant)
    {
        var memoire = SouvenirDialogue.MemoireDe(h);
        var blocs = new List<string>();
        var taille = 0;
        long? derniere = null;
        foreach (var x in service.Depot.Dialogues(h.Id, limite: 60).Where(x => x.Id != dialogueEnCours))
        {
            var repliques = service.Depot.Repliques(x.Id);
            if (repliques.Count == 0) continue;
            var mode = (ModeParole)x.Mode;
            var retenues = repliques.Select(r => (R: r, T: SouvenirDialogue.Retenu(maintenant - r.Le, memoire, mode, r.Id * 7919 + h.Id))).ToList();
            var meilleure = retenues.Max(y => y.T);
            if (meilleure == SouvenirDialogue.Trace.Oubli) continue;
            derniere ??= x.Debut;
            var b = new StringBuilder();
            var (date, voie) = (L.Date(x.Debut), L.T($"modeParole.{mode}"));
            if (meilleure == SouvenirDialogue.Trace.Vague) b.AppendLine(L.T("dlg.d.souvenirVague", ("date", date), ("voie", voie)));
            else
            {
                b.AppendLine(L.T("dlg.d.souvenir", ("date", date), ("voie", voie)));
                foreach (var (r, t) in retenues.Where(y => y.T >= SouvenirDialogue.Trace.Sens))
                {
                    var lui = r.Sens.Length > 0 ? r.Sens : r.Mots;
                    b.AppendLine(t == SouvenirDialogue.Trace.MotPourMot
                        ? L.T("dlg.d.motPourMot", ("e", ClientLlm.Couper(r.Entite, 300)), ("t", ClientLlm.Couper(lui, 300)))
                        : L.T("dlg.d.aPeuPres", ("e", ClientLlm.Couper(r.Entite, 70)), ("t", ClientLlm.Couper(lui, 70))));
                }
            }
            if (taille + b.Length > SouvenirsMax) break;
            taille += b.Length;
            blocs.Add(b.ToString().TrimEnd());
        }
        if (blocs.Count == 0) return;
        d.AppendLine(L.T("dlg.d.reconnais", ("n", blocs.Count), ("date", L.Date(derniere!.Value))));
        d.AppendLine(L.T("dlg.d.souvenirs"));
        foreach (var b in Enumerable.Reverse(blocs)) d.AppendLine(b);
    }

    /// <summary>Les noms qu'il peut dire : le sien (et son nom dans sa langue), ceux de ses proches.</summary>
    private static List<string> Noms(Humain h, IReadOnlyList<Humain> humains)
    {
        var noms = new List<string> { h.Nom };
        if (!string.IsNullOrWhiteSpace(h.NomLangue)) noms.Add(h.NomLangue!);
        noms.AddRange(h.Liens.Where(l => l.Id >= 1 && l.Id <= humains.Count).OrderByDescending(l => Math.Abs(l.Affection)).Take(40)
            .Select(l => humains[l.Id - 1].Nom));
        return noms.Distinct().ToList();
    }

    /// <summary>Ce qu'il peut dire, mot à mot (normalisé) : les formes de ses mots parlés, les noms qu'il connaît.</summary>
    public static HashSet<string> Permis(Humain h, IReadOnlyList<Humain> humains)
    {
        var permis = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in h.Vocabulaire.Where(m => !m.Signe).Select(m => m.Forme).Concat(Noms(h, humains)))
            foreach (var t in Normaliser(x).Split(' ', StringSplitOptions.RemoveEmptyEntries)) permis.Add(t);
        return permis;
    }

    /// <summary>Ses mots sans les gestes et cris décrits entre astérisques.</summary>
    private static string SansGestes(string mots) => System.Text.RegularExpressions.Regex.Replace(mots, @"\*[^*]*\*", " ");

    /// <summary>Les mots qu'il a dits hors de sa langue (ni ses mots, ni des noms connus, hors gestes entre astérisques).</summary>
    public static List<string> Inconnus(string mots, HashSet<string> permis) =>
        Normaliser(SansGestes(mots)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !permis.Contains(t)).Distinct().ToList();

    /// <summary>Au second essai encore fautif : les mots hors de sa langue deviennent « … » (il bute, ne trouve pas le mot).</summary>
    private static string Effacer(string mots, HashSet<string> permis)
    {
        var morceaux = System.Text.RegularExpressions.Regex.Split(mots, @"(\*[^*]*\*)");
        for (var i = 0; i < morceaux.Length; i++)
        {
            if (morceaux[i].StartsWith('*')) continue;
            morceaux[i] = System.Text.RegularExpressions.Regex.Replace(morceaux[i], @"[\p{L}\p{M}\p{Nd}'’-]+",
                x => Normaliser(x.Value).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(permis.Contains) ? x.Value : "…");
        }
        return string.Concat(morceaux);
    }

    /// <summary>Lit la réponse de l'humain ; avec l'Entité libre, ses changements deviennent un acte libre (null : aucun).</summary>
    public static bool Lire(string reponse, out string mots, out string pensee, out ActeLlm? acte, out string faute) =>
        Lire(reponse, out mots, out pensee, out _, out acte, out faute);

    /// <summary>Lit la réponse de l'humain, avec <paramref name="sens"/> : ce que veulent dire ses mots, dans la langue du joueur.</summary>
    public static bool Lire(string reponse, out string mots, out string pensee, out string sens, out ActeLlm? acte, out string faute)
    {
        (mots, pensee, sens, acte, faute) = ("", "", "", null, "");
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
        string Texte(string cle) => o[cle] is JsonValue v && v.TryGetValue<string>(out var s) ? ClientLlm.Resserrer(s, 1500) : "";
        // « reponse » depuis la 0.42.8 ; « sens » avant.
        (mots, pensee, sens) = (Texte("mots"), Texte("pensee"), Texte("reponse") is { Length: > 0 } r ? r : Texte("sens"));
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

    /// <summary>Un changement sans numéro d'humain est le sien : c'est de lui-même qu'il change.</summary>
    public static ActeLlm Completer(ActeLlm acte, int humain) =>
        acte with { Changements = [.. (acte.Changements ?? []).Select(c => c.Humain == 0 ? c with { Humain = humain } : c)] };

    /// <summary>
    /// Sa réaction libre ne touche que lui (ses liens vers d'autres compris), et seulement son cœur, sa croyance, ses liens, ses projets.
    /// Un lien désigne un autre humain vivant ; une pensée a un texte.
    /// </summary>
    public static string? VerifierLibre(ActeLlm acte, int humain, IReadOnlyList<Humain> humains)
    {
        foreach (var c in acte.Changements ?? [])
        {
            if (Array.IndexOf(SortesLibres, c.Quoi) < 0) return L.T("dlg.erreur.libreSorte", ("quoi", c.Quoi), ("liste", string.Join(", ", SortesLibres)));
            if (c.Humain != humain) return L.T("dlg.erreur.libreHumain", ("id", humain));
            if (c.Quoi == "lien" && (c.Autre < 1 || c.Autre > humains.Count || c.Autre == humain || !humains[c.Autre - 1].EstVivant || c.Valeur is null))
                return L.T("dlg.erreur.libreLien", ("id", humain));
            if (c.Quoi == "pensee" && string.IsNullOrWhiteSpace(c.Texte)) return L.T("dlg.erreur.librePensee");
            if (c.Quoi == "communaute" && c.Communaute < 1) return L.T("dlg.erreur.libreSeul");
        }
        return null;
    }

    /// <summary>
    /// Sa réaction libre ne peut pas aller contre ce que les règles ont décidé dans cette réplique (0.36.3) : s'il n'y croit pas, sa
    /// croyance et sa ferveur ne montent pas ; s'il y croit, sa croyance ne baisse pas. Rend la faute, ou null.
    /// </summary>
    public static string? Contredit(ActeLlm acte, Humain h, bool croit)
    {
        foreach (var c in acte.Changements ?? [])
        {
            if (c.Valeur is not { } v) continue;
            var contraire = c.Quoi switch
            {
                "croyance" => croit ? v < h.Croyance : v > h.Croyance,
                "ferveur" => !croit && v > h.Ferveur,
                _ => false,
            };
            if (contraire) return L.T(croit ? "dlg.erreur.contraireCroit" : "dlg.erreur.contraireDoute", ("quoi", c.Quoi), ("nom", h.Nom));
        }
        return null;
    }

    /// <summary>
    /// Une réplique ne change pas un cœur d'un coup (0.36.4) : chaque changement libre est ramené à un écart borné par son caractère.
    /// Émotions : la réactivité émotionnelle suit le névrosisme ; désir : l'extraversion ; croyance, ferveur et regard sur l'invisible :
    /// l'ouverture (changer d'idée sur le sacré est rare et lent) ; affection : l'agréabilité. Écart : de 10 à 50 points selon le trait
    /// (0,1 à 0,5 pour la ferveur), jamais au-delà des bornes.
    /// </summary>
    public static ActeLlm Borner(ActeLlm acte, Humain h, IReadOnlyList<Humain> humains)
    {
        var p = h.Personnalite;
        var liste = new List<ChangementLibre>();
        foreach (var c in acte.Changements ?? [])
        {
            if (c.Valeur is not { } v)
            {
                liste.Add(c);
                continue;
            }
            (double Actuel, double Trait, double Echelle)? b = c.Quoi switch
            {
                "humeur" => (h.Humeur, p.Nevrosisme, 1),
                "peur" => (h.Peur, p.Nevrosisme, 1),
                "chagrin" => (h.Chagrin, p.Nevrosisme, 1),
                "colere" => (h.Colere, p.Nevrosisme, 1),
                "desir" => (h.Desir, p.Extraversion, 1),
                "croyance" => (h.Croyance, p.Ouverture, 1),
                "amour" => (h.Amour, p.Ouverture, 1),
                "crainte" => (h.Crainte, p.Ouverture, 1),
                "revolte" => (h.Revolte, p.Ouverture, 1),
                "ferveur" => (h.Ferveur, p.Ouverture, 0.01),
                "lien" when c.Autre >= 1 && c.Autre <= humains.Count => (h.LienAvec(c.Autre)?.Affection ?? 0, p.Agreabilite, 1),
                _ => null,
            };
            if (b is not { } x)
            {
                liste.Add(c);
                continue;
            }
            var ecart = (10 + 40 * Math.Clamp(x.Trait, 0, 1)) * x.Echelle;
            liste.Add(c with { Valeur = Math.Round(Math.Clamp(v, x.Actuel - ecart, x.Actuel + ecart), 2) });
        }
        return acte with { Changements = liste };
    }

    // ───────────── Pour le navigateur ─────────────

    private string CroitTexte(int croit, bool comprise) => L.T(croit == 1 ? "dlg.croit.oui" : comprise ? "dlg.croit.doute" : "dlg.croit.incompris");

    /// <summary>Réactions des répliques récentes, par dialogue puis par rang (0.45.11) ; seuls les derniers dialogues sont gardés.</summary>
    private readonly Dictionary<long, Dictionary<int, ReactionDto>> _reactions = [];

    /// <summary>Note ce que les règles viennent de décider, avec ce qui nuance son geste (peur du moment, caractère).</summary>
    private void Reagir(EnCours c, int rang, RegleReplique regle, List<Reponse> reponses, List<TemoinDto> temoins)
    {
        var (_, humains) = c.Monde.InstantaneHumains();
        if (c.Humain < 1 || c.Humain > humains.Count) return;
        var h = humains[c.Humain - 1];
        var p = h.Personnalite;
        // Comme Comportement.Eprouver : l'anxieux qui croit à une voix ou une apparition en a peur, les autres s'en émerveillent.
        var emotion = !regle.Comprise ? "incomprise" : !regle.Croit ? "doute"
            : c.Mode is ModeParole.Voix or ModeParole.Apparition && p.Nevrosisme > 0.55 ? "effroi" : "merveille";
        var r = new ReactionDto(regle.Comprise, regle.Croit, emotion, regle.Fin.ToString(), reponses.Select(x => x.ToString()).ToList(),
            Math.Round(h.Peur / 100.0, 2), Math.Round(p.Nevrosisme, 2), Math.Round(p.Transcendance, 2), Math.Round(p.Extraversion, 2),
            Math.Round(p.Agreabilite, 2), temoins, Conscience: Math.Round(p.Conscience, 2));
        lock (_reactions)
        {
            if (!_reactions.TryGetValue(c.Id, out var parRang))
            {
                if (_reactions.Count >= 20) _reactions.Remove(_reactions.Keys.Min());
                _reactions[c.Id] = parRang = [];
            }
            parRang[rang] = r;
        }
    }

    /// <summary>Le geste qu'il fait en répondant, ajouté à la réaction de cette réplique.</summary>
    private void Gesticuler(long dialogue, int rang, string geste)
    {
        if (geste.Length == 0) return;
        lock (_reactions)
            if (_reactions.TryGetValue(dialogue, out var parRang) && parRang.TryGetValue(rang, out var r)) parRang[rang] = r with { Geste = geste };
    }

    private ReactionDto? Reaction(long dialogue, int rang)
    {
        lock (_reactions)
            return _reactions.TryGetValue(dialogue, out var parRang) && parRang.TryGetValue(rang, out var r) ? r : null;
    }

    public DialogueDto Dto(long id)
    {
        var d = service.Depot.Dialogue(id) ?? throw new ArgumentException(L.T("dlg.erreur.fini"));
        var (_, humains) = service.Monde.InstantaneHumains();
        EnCours? c;
        lock (_verrou) c = _courant is { } x && x.Id == id ? x : null;
        var nom = d.Humain >= 1 && d.Humain <= humains.Count ? humains[d.Humain - 1].Nom : "?";
        var mode = (ModeParole)d.Mode;
        string? textePriere = d.Priere != 0 ? Religions.Prieres(service, enAttente: false).FirstOrDefault(p => p.Id == d.Priere)?.Texte : null;
        var (instant, _) = service.Monde.InstantaneHumains();
        var lui = d.Humain >= 1 && d.Humain <= humains.Count ? humains[d.Humain - 1] : null;
        var repliques = service.Depot.Repliques(id).Select(r => Dto(r, lui, instant)).ToList();
        var croit = c is not null ? (c.Croit is { } b ? b ? 1 : 0 : -1) : d.Croit;
        return new DialogueDto(d.Id, d.Humain, nom, mode.ToString(), L.T($"modeParole.{mode}"), d.Priere, textePriere, L.Date(d.Debut),
            d.Statut == 1 ? L.Date(d.Fin) : null, d.Statut == 1 ? L.T($"dlg.fin.court.{(FinDialogue)d.FinRaison}") : null, c is not null, c?.Attend,
            repliques.Count, CroitTexte(croit, c?.Comprise ?? d.Comprise), repliques, c?.Erreur);
    }

    private RepliqueDto Dto(RepliqueEntite r, Humain? lui, long maintenant)
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
            r.Juge.Length > 0 ? r.Juge : null, r.Erreur, Math.Round(r.DureeMs / 1000.0, 1), r.JournalPremier, r.Sens,
            lui is null ? null : Suivi(paroles, r.Le, lui, maintenant), Reaction(r.Dialogue, r.Rang));
    }

    /// <summary>
    /// Ce que sont devenus les ordres d'une réplique (0.44.11) : pour chaque parole, l'impulsion qu'elle a laissée chez lui (même
    /// parole, reçue à ce moment-là), et son état aujourd'hui.
    /// </summary>
    private List<string> Suivi(List<ActeLlm> paroles, long le, Humain lui, long maintenant)
    {
        var lignes = new List<string>();
        foreach (var a in paroles)
        {
            if (!Enum.TryParse<Parole>(a.Parole, true, out var p)) continue;
            var i = lui.Impulsions.LastOrDefault(x => x.Parole == p && Math.Abs(x.Le - le) <= MondeSimule.SecondesParPas);
            lignes.Add(L.T("ordre.ligne", ("parole", Libelle(a)), ("etat", Ordres.Etat(i, maintenant))));
        }
        return lignes;
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

    /// <summary>
    /// Les dialogues restés ouverts quand le serveur s'est arrêté (0.36.3) : ils se closent comme un silence, avec leur ligne au journal
    /// et le souvenir de l'humain.
    /// </summary>
    private void Orphelins(MondeSimule monde)
    {
        long? courant;
        lock (_verrou) courant = _courant?.Id;
        var depot = service.Depot;
        var clos = false;
        foreach (var d in depot.Dialogues(statut: 0))
        {
            if (d.Id == courant) continue;
            long ligne = 0;
            try
            {
                ligne = monde.FinirDialogue(d.Humain, (ModeParole)d.Mode, d.Repliques, d.Croit == 1, d.Comprise, FinDialogue.Silence, 0);
            }
            catch (ArgumentException)
            {
            }
            depot.MettreAJourDialogue(d with { Fin = monde.Secondes, Statut = 1, FinRaison = (int)FinDialogue.Silence, JournalFin = ligne });
            clos = true;
        }
        if (clos && ReferenceEquals(monde, service.Monde)) service.Sauvegarder();
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

        // Le rêve attendu : il s'est endormi, le dialogue s'ouvre ; il est mort ou le monde a changé, on n'attend plus.
        (int Humain, int Priere, MondeSimule Monde)? attente;
        lock (_verrou) attente = _attente;
        if (attente is { } a)
        {
            var (_, hs) = monde.InstantaneHumains();
            if (!ReferenceEquals(a.Monde, monde) || a.Humain > hs.Count || !hs[a.Humain - 1].EstVivant) AnnulerAttente();
            else if (monde.Dort(a.Humain))
            {
                try
                {
                    Ouvrir(a.Humain, nameof(ModeParole.Reve), a.Priere);
                }
                catch (ArgumentException e)
                {
                    journal.LogDebug(e, "Dialogue : rêve attendu impossible");
                }
                lock (_verrou) _attente = null;
                await hub.Clients.Diffuser("DialogueAttente", () => (AttenteDto?)null);
                await Diffuser();
            }
        }

        // « X t'appelle » : les prières fortes nouvelles d'un humain seul (pas collectives), si un LLM peut le faire parler.
        var prieres = monde.Prieres();
        if (!ReferenceEquals(monde, _monde))
        {
            Orphelins(monde);
            _monde = monde;
            _dernierePriere = prieres.Select(p => p.Id).DefaultIfEmpty(0).Max();
            return;
        }
        var nouvelles = prieres.Where(p => p.Id > _dernierePriere).OrderBy(p => p.Id).ToList();
        if (nouvelles.Count == 0) return;
        _dernierePriere = nouvelles[^1].Id;
        if (string.IsNullOrEmpty(entite.Connexion.Modele) || !entite.PourConversations) return;
        foreach (var p in nouvelles.Where(p => p.Forte && p.CommunauteId == 0 && p.Issue == IssuePriere.EnAttente && p.Sujet != SujetPriere.Remerciement).TakeLast(3))
        {
            var id = p.Id;
            await hub.Clients.Diffuser("Appel", () => Religions.Prieres(service, enAttente: true).FirstOrDefault(x => x.Id == id) is { } dto
                ? new AppelDto(dto.Id, dto.Humain, dto.Nom, dto.Texte) : null);
        }
    }
}
