using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Serveur.Entite;

/// <summary>Qui joue l'Entité.</summary>
public enum ModeEntite
{
    /// <summary>Le joueur seul (comme avant la 0.35).</summary>
    Joueur = 0,
    /// <summary>Le LLM décide et agit seul ; le joueur garde tous ses pouvoirs.</summary>
    Agit = 1,
    /// <summary>Le LLM propose ; ses décisions attendent l'accord du joueur.</summary>
    Propose = 2,
}

/// <summary>Réglages de l'Entité-LLM gardés avec le monde.</summary>
/// <param name="PartOrdinaires">Part des prières ordinaires soumises au LLM (0 à 100 %).</param>
/// <param name="Attendre">Arrêter l'horloge du monde pendant que le LLM réfléchit.</param>
/// <param name="Regard">Son regard périodique sur le monde.</param>
/// <param name="RegardJours">Jours du monde entre deux regards.</param>
/// <param name="Caractere">Son caractère, écrit par le joueur (vide : elle se fait le sien).</param>
/// <param name="Libre">Entité libre : elle peut changer l'état du monde et décréter, hors des règles (0.35.2).</param>
/// <param name="Juge">Juge de cohérence : un second appel relit chaque acte libre.</param>
/// <param name="Monde">« Permettre au LLM de parler au Monde » (0.37.1), décoché par défaut : une erreur toucherait tout le monde à la fois.</param>
public sealed record ReglagesEntiteLlm(string Mode, bool Prieres, int PartOrdinaires, bool Attendre, bool Paroles, bool Bienfaits, bool Chatiments,
    bool Catastrophes, bool Souffles, bool Regard = true, int RegardJours = EntiteLlm.RegardParDefaut, string Caractere = "", bool Libre = false, bool Juge = true,
    bool Monde = false, bool Trace = false)
{
    public ModeEntite ModeLu => Enum.TryParse<ModeEntite>(Mode, out var m) && Enum.IsDefined(m) ? m : ModeEntite.Joueur;

    public FamillesLlm Familles =>
        (Paroles ? FamillesLlm.Paroles : 0) | (Bienfaits ? FamillesLlm.Bienfaits : 0) | (Chatiments ? FamillesLlm.Chatiments : 0)
        | (Catastrophes ? FamillesLlm.Catastrophes : 0) | (Souffles ? FamillesLlm.Souffles : 0);
}

/// <summary>Où en est l'Entité-LLM : réglages, file d'attente, ce qu'elle fait en ce moment, dernière erreur.</summary>
/// <param name="CaractereLlm">Le caractère que le LLM s'est donné (vide : aucun encore).</param>
/// <param name="ProchainRegard">Date du monde de son prochain regard (null : pas de regard prévu).</param>
/// <param name="LibreDepuis">Date du premier acte libre : depuis, le monde n'est plus le fruit des seules règles (null : aucun).</param>
public sealed record EtatEntiteLlmDto(ConnexionLlm Connexion, ReglagesEntiteLlm Reglages, int EnFile, string? EnCours, string? DerniereErreur, int Propositions,
    string CaractereLlm = "", string? ProchainRegard = null, string? LibreDepuis = null, string TraceChemin = "");

/// <summary>Un échange, pour la liste : date, occasion, destinataire, mots, résultat.</summary>
public sealed record EchangeDto(long Id, string Date, string Quand, string Occasion, int Humain, string? NomHumain, int Communaute, string? NomCommunaute,
    int Priere, string Modele, double Secondes, string Mots, string Pensee, IReadOnlyList<string> Actes, IReadOnlyList<string> Resultats, string Statut,
    string StatutCode, string? Erreur, long JournalPremier, string? Lu = null, string? Reponse = null);

/// <summary>Les lignes du journal écrites par les actes du LLM (pour les marquer « LLM »). Rechargées à chaque changement de monde.</summary>
public static class MarquesLlm
{
    private static readonly Lock Verrou = new();
    private static List<(long Premier, long Dernier)> _plages = [];

    public static void Remplacer(List<(long, long)> plages)
    {
        lock (Verrou) _plages = [.. plages.OrderBy(p => p.Item1)];
    }

    public static void Ajouter(long premier, long dernier)
    {
        if (dernier < premier || premier <= 0) return;
        lock (Verrou) _plages = [.. _plages.Append((Premier: premier, Dernier: dernier)).OrderBy(p => p.Premier)];
    }

    public static bool Contient(long id)
    {
        List<(long Premier, long Dernier)> plages;
        lock (Verrou) plages = _plages;
        if (plages.Count == 0) return false;
        // Plages triées par début et disjointes (chaque acte écrit des numéros neufs) : recherche dichotomique.
        int bas = 0, haut = plages.Count - 1;
        while (bas <= haut)
        {
            var m = (bas + haut) / 2;
            if (id < plages[m].Premier) haut = m - 1;
            else if (id > plages[m].Dernier) bas = m + 1;
            else return true;
        }
        return false;
    }
}

/// <summary>
/// L'Entité jouée par un LLM local : une file de demandes (prières, « Que ferait l'Entité ? »), traitées une à une sur un fil de
/// fond. Chaque réponse est vérifiée, appliquée par les mêmes pouvoirs que le joueur (ou proposée au joueur), et gardée dans la
/// trace <c>entite_echanges</c>. En mode « Joueur seul », rien n'est appelé et le monde est exactement celui d'avant.
/// </summary>
public sealed class EntiteLlm(ServiceMonde service, ClientLlm client, IHubContext<MondeHub> hub, ILogger<EntiteLlm> journal, TraceLlm trace) : BackgroundService
{
    private const string CleMode = "llm_mode", ClePrieres = "llm_prieres", ClePart = "llm_part", CleAttendre = "llm_attendre", CleFamilles = "llm_familles",
        CleLangue = "llm_langue", CleRegard = "llm_regard", CleRegardJours = "llm_regard_jours", CleDernierRegard = "llm_dernier_regard",
        CleCaractere = "llm_caractere", CleCaractereLlm = "llm_caractere_llm", CleLibre = "llm_libre", CleJuge = "llm_juge", CleMonde = "llm_monde";

    /// <summary>Jours entre deux regards, par défaut : une saison.</summary>
    public const int RegardParDefaut = (int)Calendrier.JoursParSaison;

    /// <summary>Longueur maximale du caractère écrit par le joueur (ou choisi par le LLM).</summary>
    public const int CaractereMax = 2000;

    /// <summary>Demandes en attente au plus ; au-delà, la moins urgente (la plus récente à urgence égale) est abandonnée.</summary>
    public const int FileMax = 40;

    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DelaiReponse = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReposApresPanne = TimeSpan.FromSeconds(30);

    /// <param name="Depuis">Pour un regard : date du monde du regard précédent.</param>
    private sealed record Demande(OccasionLlm Occasion, int Humain, int Communaute, int Priere, long Rang, long Depuis = 0);

    private readonly Lock _verrou = new();
    private readonly List<Demande> _file = [];
    private long _rang;
    private int _dernierePriere = -1;
    private MondeSimule? _monde;
    private volatile string? _enCours;
    private volatile string? _derniereErreur;
    private readonly Random _hasard = new();
    private ConnexionLlm? _connexion;

    private string CheminConnexion => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(service.Depot.CheminFichier)!)!, "entite-llm.json");

    /// <summary>Réglage de la connexion (fichier de l'installation ; par défaut Ollama sur cette machine, sans modèle choisi).</summary>
    public ConnexionLlm Connexion
    {
        get
        {
            if (_connexion is { } c) return c;
            try
            {
                if (File.Exists(CheminConnexion) && JsonSerializer.Deserialize<ConnexionLlm>(File.ReadAllText(CheminConnexion), Actes.Json) is { } lu && lu.Erreur() is null)
                    return _connexion = lu;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                journal.LogWarning(e, "Réglage du LLM illisible : {Fichier}", CheminConnexion);
            }
            return _connexion = ConnexionLlm.ParDefaut;
        }
        set
        {
            if (value.Erreur() is { } erreur) throw new ArgumentException(erreur);
            Directory.CreateDirectory(Path.GetDirectoryName(CheminConnexion)!);
            File.WriteAllText(CheminConnexion, JsonSerializer.Serialize(value, Actes.Json));
            _connexion = value;
            _derniereErreur = null;
        }
    }

    public ReglagesEntiteLlm Reglages
    {
        get
        {
            var d = service.Depot;
            var familles = int.TryParse(d.LireReglage(CleFamilles), NumberStyles.None, CultureInfo.InvariantCulture, out var f) ? (FamillesLlm)f & FamillesLlm.Toutes : FamillesLlm.Toutes;
            var part = int.TryParse(d.LireReglage(ClePart), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p <= 100 ? p : 10;
            var jours = int.TryParse(d.LireReglage(CleRegardJours), NumberStyles.None, CultureInfo.InvariantCulture, out var j) && j is >= 1 and <= 3650 ? j : RegardParDefaut;
            return new ReglagesEntiteLlm(d.LireReglage(CleMode) is { } m && Enum.TryParse<ModeEntite>(m, out var mm) && Enum.IsDefined(mm) ? mm.ToString() : nameof(ModeEntite.Joueur),
                d.LireReglage(ClePrieres) != "non", part, d.LireReglage(CleAttendre) == "oui",
                familles.HasFlag(FamillesLlm.Paroles), familles.HasFlag(FamillesLlm.Bienfaits), familles.HasFlag(FamillesLlm.Chatiments),
                familles.HasFlag(FamillesLlm.Catastrophes), familles.HasFlag(FamillesLlm.Souffles), d.LireReglage(CleRegard) != "non", jours,
                d.LireReglage(CleCaractere) ?? "", d.LireReglage(CleLibre) == "oui", d.LireReglage(CleJuge) != "non", d.LireReglage(CleMonde) == "oui",
                d.LireReglage(TraceLlm.Cle) == "oui");
        }
    }

    /// <summary>Change les réglages ; <paramref name="langue"/> : celle du navigateur, que le LLM parlera.</summary>
    public void Regler(ReglagesEntiteLlm r, string langue)
    {
        if (!Enum.TryParse<ModeEntite>(r.Mode, out var mode) || !Enum.IsDefined(mode) || r.PartOrdinaires is < 0 or > 100 || r.RegardJours is < 1 or > 3650
            || (r.Caractere?.Length ?? 0) > CaractereMax)
            throw new ArgumentException(L.T("llm.erreur.reglages"));
        var d = service.Depot;
        // Le regard reprend : le premier viendra après une période entière, sans tout ce qui s'est passé pendant qu'il dormait.
        var avant = Reglages;
        if (mode != ModeEntite.Joueur && r.Regard && (avant.ModeLu == ModeEntite.Joueur || !avant.Regard || avant.RegardJours != r.RegardJours))
            d.EcrireReglage(CleDernierRegard, service.Monde.Secondes.ToString(CultureInfo.InvariantCulture));
        d.EcrireReglage(CleMode, mode.ToString());
        d.EcrireReglage(ClePrieres, r.Prieres ? "oui" : "non");
        d.EcrireReglage(ClePart, r.PartOrdinaires.ToString(CultureInfo.InvariantCulture));
        d.EcrireReglage(CleAttendre, r.Attendre ? "oui" : "non");
        d.EcrireReglage(CleFamilles, ((int)r.Familles).ToString(CultureInfo.InvariantCulture));
        d.EcrireReglage(CleRegard, r.Regard ? "oui" : "non");
        d.EcrireReglage(CleRegardJours, r.RegardJours.ToString(CultureInfo.InvariantCulture));
        d.EcrireReglage(CleCaractere, (r.Caractere ?? "").Trim());
        d.EcrireReglage(CleLibre, r.Libre ? "oui" : "non");
        d.EcrireReglage(CleJuge, r.Juge ? "oui" : "non");
        d.EcrireReglage(CleMonde, r.Monde ? "oui" : "non");
        d.EcrireReglage(TraceLlm.Cle, r.Trace ? "oui" : "non");
        if (L.Toutes.ContainsKey(langue)) d.EcrireReglage(CleLangue, langue);
        if (mode == ModeEntite.Joueur)
            lock (_verrou) _file.RemoveAll(x => x.Occasion is not (OccasionLlm.DemandeHumain or OccasionLlm.DemandeCommunaute));
    }

    public EtatEntiteLlmDto Etat()
    {
        int n;
        lock (_verrou) n = _file.Count;
        var r = Reglages;
        string? prochain = null;
        if (r.ModeLu != ModeEntite.Joueur && r.Regard && !string.IsNullOrEmpty(Connexion.Modele))
            prochain = L.Date((DernierRegard ?? service.Monde.Secondes) + r.RegardJours * Calendrier.SecondesParJour);
        var libre = service.Depot.PremierActeLibre();
        return new EtatEntiteLlmDto(Connexion, r, n, _enCours, _derniereErreur, service.Depot.Echanges(statut: 1, limite: 1000).Count,
            service.Depot.LireReglage(CleCaractereLlm) ?? "", prochain, libre is { } l ? L.Date(l) : null, trace.Chemin);
    }

    /// <summary>Date du monde de son dernier regard (null : jamais).</summary>
    private long? DernierRegard =>
        long.TryParse(service.Depot.LireReglage(CleDernierRegard), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;

    /// <summary>Oublie le caractère que le LLM s'était donné : il s'en choisira un autre au prochain appel.</summary>
    public void OublierCaractere() => service.Depot.EcrireReglage(CleCaractereLlm, "");

    /// <summary>Met en file son regard sur le monde quand le moment est venu (un seul à la fois).</summary>
    private void Regarder(MondeSimule monde, ReglagesEntiteLlm r)
    {
        var maintenant = monde.Secondes;
        var dernier = DernierRegard;
        if (dernier is null || dernier > maintenant)
        {
            // Premier tour (ou monde revenu en arrière) : le premier regard viendra après une période entière.
            service.Depot.EcrireReglage(CleDernierRegard, maintenant.ToString(CultureInfo.InvariantCulture));
            return;
        }
        if (maintenant - dernier.Value < r.RegardJours * Calendrier.SecondesParJour) return;
        lock (_verrou)
        {
            if (_file.Any(x => x.Occasion == OccasionLlm.Regard)) return;
        }
        service.Depot.EcrireReglage(CleDernierRegard, maintenant.ToString(CultureInfo.InvariantCulture));
        Mettre(new Demande(OccasionLlm.Regard, 0, 0, 0, 0, dernier.Value));
    }

    /// <summary>Le joueur demande « Que ferait l'Entité ? » pour un humain ou une communauté ; rend la place dans la file.</summary>
    public int Demander(int humain, int communaute)
    {
        if (string.IsNullOrEmpty(Connexion.Modele)) throw new ArgumentException(L.T("llm.erreur.pasDeModele"));
        var (_, humains) = service.Monde.InstantaneHumains();
        var communautes = service.Monde.Communautes();
        if (humain != 0 && (humain < 1 || humain > humains.Count || !humains[humain - 1].EstVivant)) throw new ArgumentException(L.T("erreur.humainVivant"));
        if (humain == 0 && (communaute < 1 || communaute > communautes.Count || !communautes[communaute - 1].Active)) throw new ArgumentException(L.T("erreur.communaute"));
        return Mettre(new Demande(humain != 0 ? OccasionLlm.DemandeHumain : OccasionLlm.DemandeCommunaute, humain, humain != 0 ? 0 : communaute, 0, 0));
    }

    private static int Urgence(OccasionLlm o) => o switch
    {
        OccasionLlm.DemandeHumain or OccasionLlm.DemandeCommunaute => 0,
        OccasionLlm.PriereCollective => 1,
        OccasionLlm.PriereForte => 2,
        OccasionLlm.Regard => 3,
        _ => 4,
    };

    private int Mettre(Demande d)
    {
        lock (_verrou)
        {
            _file.Add(d with { Rang = ++_rang });
            _file.Sort((a, b) => Urgence(a.Occasion) != Urgence(b.Occasion) ? Urgence(a.Occasion).CompareTo(Urgence(b.Occasion)) : a.Rang.CompareTo(b.Rang));
            if (_file.Count > FileMax) _file.RemoveAt(_file.Count - 1);
            return _file.FindIndex(x => x.Rang == _rang) + 1;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Cadence);
        try
        {
            while (await minuteur.WaitForNextTickAsync(arret))
            {
                try
                {
                    await Tour(arret);
                }
                catch (Exception e) when (e is not OperationCanceledException || !arret.IsCancellationRequested)
                {
                    journal.LogWarning(e, "Entité-LLM : erreur imprévue");
                    _derniereErreur = e.Message;
                }
            }
        }
        catch (OperationCanceledException) when (arret.IsCancellationRequested)
        {
        }
        finally
        {
            service.Monde.Retenu = false;
        }
    }

    private async Task Tour(CancellationToken arret)
    {
        var monde = service.Monde;
        if (!ReferenceEquals(monde, _monde))
        {
            // Nouveau monde (démarrage, réinitialisation, restauration) : on repart des prières d'aujourd'hui, et des marques de sa trace.
            // Au démarrage, la file garde ce qui a pu être demandé avant ce premier tour ; un autre monde la vide.
            if (_monde is not null) lock (_verrou) _file.Clear();
            _monde = monde;
            _dernierePriere = monde.Prieres().Select(p => p.Id).DefaultIfEmpty(0).Max();
            MarquesLlm.Remplacer(service.Depot.PlagesJournalLlm());
            MarquesLibre.Remplacer(service.Depot.PlagesJournalLibre());
        }
        var reglages = Reglages;
        var mode = reglages.ModeLu;
        if (mode != ModeEntite.Joueur && reglages.Prieres) Recenser(monde, reglages);
        else _dernierePriere = Math.Max(_dernierePriere, monde.Prieres().Select(p => p.Id).DefaultIfEmpty(0).Max());

        if (string.IsNullOrEmpty(Connexion.Modele)) return;
        if (mode != ModeEntite.Joueur && reglages.Regard) Regarder(monde, reglages);
        // Pendant un dialogue, elle attend : elle ne touche pas à celui à qui l'on parle, et laisse le LLM au dialogue (0.36.3).
        if (monde.EnDialogue) return;
        Demande? d;
        lock (_verrou)
        {
            d = _file.Count > 0 ? _file[0] : null;
            if (d is not null) _file.RemoveAt(0);
        }
        if (d is null) return;
        var langue = service.Depot.LireReglage(CleLangue) ?? L.Francais;
        L.Definir(langue);
        await Traiter(monde, d, reglages, arret);
    }

    /// <summary>Met en file les prières nouvelles : fortes et collectives toujours, ordinaires selon la part choisie.</summary>
    private void Recenser(MondeSimule monde, ReglagesEntiteLlm r)
    {
        foreach (var p in monde.Prieres().Where(p => p.Id > _dernierePriere).OrderBy(p => p.Id))
        {
            _dernierePriere = p.Id;
            if (p.Issue != IssuePriere.EnAttente || p.Sujet == SujetPriere.Remerciement) continue;
            var occasion = p.CommunauteId != 0 ? OccasionLlm.PriereCollective : p.Forte ? OccasionLlm.PriereForte : OccasionLlm.PriereOrdinaire;
            if (occasion == OccasionLlm.PriereOrdinaire && _hasard.Next(100) >= r.PartOrdinaires) continue;
            Mettre(new Demande(occasion, p.HumainId, p.CommunauteId, p.Id, 0));
        }
    }

    private async Task Traiter(MondeSimule monde, Demande d, ReglagesEntiteLlm reglages, CancellationToken arret)
    {
        Priere? priere = null;
        if (d.Priere != 0)
        {
            priere = monde.Prieres().FirstOrDefault(p => p.Id == d.Priere);
            // Déjà close (exaucée par le joueur, arrivée d'elle-même, expirée) : rien à demander.
            if (priere is null || priere.Issue != IssuePriere.EnAttente || priere.Refus != RefusPriere.Aucun) return;
            // Le joueur lui a répondu par un dialogue : c'est sa réponse.
            if (service.Depot.PriereEnDialogue(d.Priere)) return;
        }
        var connexion = Connexion;
        var permises = reglages.Familles;
        var memoire = service.Depot.Echanges(limite: 8).Where(e => e.Statut is 0 or 1).Reverse().ToList();
        var caractereLlm = service.Depot.LireReglage(CleCaractereLlm) ?? "";
        var libre = reglages.Libre;
        var auMonde = reglages.Monde && permises.HasFlag(FamillesLlm.Paroles);
        var lecture = d.Occasion == OccasionLlm.Regard
            ? Contexte.LireRegard(service, permises, d.Depuis, memoire, reglages.Caractere, caractereLlm, libre, auMonde)
            : Contexte.Lire(service, permises, d.Occasion, d.Humain, d.Communaute, priere, memoire, reglages.Caractere, caractereLlm, libre, auMonde);
        var schema = Actes.Schema(libre, auMonde);
        var traceJuge = new System.Text.StringBuilder();
        var (maintenant, humains) = monde.InstantaneHumains();
        _enCours = d.Occasion == OccasionLlm.Regard ? L.T("llm.etat.regard")
            : d.Humain >= 1 && d.Humain <= humains.Count ? humains[d.Humain - 1].Nom : L.T("llm.etat.communaute");
        var chrono = Stopwatch.StartNew();
        // Une demande plus longue que le contexte serait coupée en silence par Ollama, consigne comprise : on la raccourcit.
        var (demande, raccourcie, jetons) = BudgetLlm.Ajuster(lecture.Consigne, lecture.Demande, schema.ToJsonString().Length, connexion.Contexte);
        if (raccourcie) lecture = lecture with { Demande = demande };
        var messages = new List<(string, string)> { ("user", lecture.Demande) };
        string reponse = "";
        DecisionLlm? decision = null;
        string? erreur = null;
        var avertissement = raccourcie ? L.T("llm.erreur.raccourcie", ("jetons", jetons), ("contexte", connexion.Contexte)) : null;
        if (reglages.Attendre) monde.Retenu = true;
        try
        {
            for (var essai = 0; essai < 2 && decision is null; essai++)
            {
                using var delai = CancellationTokenSource.CreateLinkedTokenSource(arret);
                delai.CancelAfter(DelaiReponse);
                reponse = await client.Demander(connexion, lecture.Consigne, messages, schema, delai.Token, reprise: essai > 0);
                try
                {
                    var lu = Actes.Lire(reponse);
                    var fautes = new List<string>();
                    // Plus d'actes que permis : au premier essai, il choisit lui-même ceux qui comptent (0.36.4).
                    if (lu.Ignores > 0 && essai == 0) fautes.Add(L.T("llm.erreur.tropDActes", ("max", Actes.ParReponse), ("n", Actes.ParReponse + lu.Ignores)));
                    var fautifs = new HashSet<ActeLlm>(ReferenceEqualityComparer.Instance);
                    foreach (var a in lu.Actes)
                    {
                        try
                        {
                            Actes.Verifier(a, permises, libre, lecture.Pieces, monde, auMonde);
                        }
                        catch (ArgumentException e)
                        {
                            fautes.Add(e.Message);
                            fautifs.Add(a);
                        }
                    }
                    // Le juge de cohérence relit un acte libre : incohérent, la décision revient une fois à l'Entité avec la raison.
                    if (fautes.Count == 0 && libre && reglages.Juge && lu.Actes.Any(a => a.Type == Actes.Libre))
                    {
                        using var delaiJuge = CancellationTokenSource.CreateLinkedTokenSource(arret);
                        delaiJuge.CancelAfter(DelaiReponse);
                        var brut = await client.Demander(connexion, L.T("libre.juge.consigne", ("langue", L.Courante.Nom)), [("user", Libres.DemandeJuge(lecture, reponse))],
                            Libres.SchemaJuge(), delaiJuge.Token);
                        var verdict = Libres.LireVerdict(brut);
                        traceJuge.AppendLine($"---- {L.T("libre.juge.nom")} ({essai + 1}) ----").AppendLine(brut);
                        if (!verdict.Coherent)
                        {
                            var raison = L.T("libre.juge.incoherent", ("raison", verdict.Raison));
                            if (essai == 1)
                            {
                                erreur = L.T("libre.juge.abandon", ("raison", verdict.Raison));
                                decision = null;
                                break;
                            }
                            erreur = raison;
                            messages.Add(("assistant", reponse));
                            messages.Add(("user", L.T("libre.juge.corriger", ("raison", verdict.Raison))));
                            continue;
                        }
                    }
                    if (fautes.Count == 0)
                    {
                        // Corrigé au second essai : l'erreur notée est celle du premier, dite comme telle.
                        if (essai == 1 && erreur is not null) erreur = L.T("llm.erreur.premierEssai", ("erreurs", erreur));
                        decision = lu;
                    }
                    else if (essai == 1)
                    {
                        // Encore fautif au second essai : seuls les actes valides sont gardés (proposés ou appliqués), les autres notés.
                        erreur = L.T("llm.erreur.secondEssai", ("erreurs", string.Join(" ", fautes)));
                        decision = lu with { Actes = [.. lu.Actes.Where(a => !fautifs.Contains(a))] };
                    }
                    else
                    {
                        erreur = string.Join(" ", fautes);
                        messages.Add(("assistant", reponse));
                        messages.Add(("user", L.T("llm.d.corriger", ("erreurs", erreur))));
                    }
                }
                catch (FormatException e)
                {
                    erreur = e.Message;
                    if (essai == 0)
                    {
                        messages.Add(("assistant", ClientLlm.Couper(reponse, 800)));
                        messages.Add(("user", L.T("llm.d.corriger", ("erreurs", e.Message))));
                    }
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException && !arret.IsCancellationRequested)
        {
            _derniereErreur = e is TaskCanceledException ? L.T("llm.erreur.delai") : L.T("llm.erreur.connexion", ("detail", e.Message));
            journal.LogWarning("Entité-LLM : {Erreur}", _derniereErreur);
            Enregistrer(new EchangeEntite(0, maintenant, Maintenant(), (int)d.Occasion, d.Humain, d.Communaute, d.Priere, connexion.Libelle, chrono.ElapsedMilliseconds,
                Lu(lecture), reponse, "", "", "[]", "[]", 3, 0, -1, _derniereErreur, 0));
            _enCours = null;
            monde.Retenu = false;
            await Task.Delay(ReposApresPanne, arret);
            return;
        }
        finally
        {
            monde.Retenu = false;
        }
        _enCours = null;
        _derniereErreur = null;
        var duree = chrono.ElapsedMilliseconds;
        if (decision is null)
        {
            Enregistrer(new EchangeEntite(0, maintenant, Maintenant(), (int)d.Occasion, d.Humain, d.Communaute, d.Priere, connexion.Libelle, duree,
                Lu(lecture), Trace(reponse, traceJuge), "", "", "[]", "[]", 3, 0, -1, Joindre(avertissement, erreur), 0));
            return;
        }
        // Sans caractère écrit par le joueur, elle garde celui qu'elle s'est donné la première fois.
        if (string.IsNullOrWhiteSpace(reglages.Caractere) && string.IsNullOrWhiteSpace(caractereLlm) && !string.IsNullOrWhiteSpace(decision.Caractere))
            service.Depot.EcrireReglage(CleCaractereLlm, ClientLlm.Couper(decision.Caractere.Trim(), CaractereMax));
        // Les pièces P1, P2… sont remplacées par leur écriture : une proposition acceptée plus tard reste lisible.
        var actes = decision.Actes.Select(a => a.Type == Actes.SoufflerCombinaison ? a with { A = Actes.Piece(a.A, lecture.Pieces), B = Actes.Piece(a.B, lecture.Pieces) }
            : Libres.Resoudre(a, lecture.Jardin)).ToList();
        var echange = new EchangeEntite(0, maintenant, Maintenant(), (int)d.Occasion, d.Humain, d.Communaute, d.Priere, connexion.Libelle, duree,
            Lu(lecture), Trace(reponse, traceJuge), decision.Mots, decision.Pensee, JsonSerializer.Serialize(actes, Actes.Json), "[]", 1, 0, -1, Joindre(avertissement, erreur), 0);
        var proposer = reglages.ModeLu != ModeEntite.Agit;
        if (actes.All(a => a.Type == Actes.Rien)) echange = echange with { Statut = 5 };
        var id = Enregistrer(echange);
        if (!proposer && echange.Statut != 5) await Appliquer(id, permises);
    }

    private static string? Joindre(string? a, string? b) => a is null ? b : b is null ? a : a + " " + b;

    private static string Lu(LectureLlm l) => l.Consigne + "\n\n----\n\n" + l.Demande;

    /// <summary>La réponse brute, suivie de ce qu'a dit le juge de cohérence (s'il a été appelé).</summary>
    private static string Trace(string reponse, System.Text.StringBuilder juge) => juge.Length == 0 ? reponse : reponse + "\n\n" + juge;

    private static string Maintenant() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private long Enregistrer(EchangeEntite e)
    {
        var id = service.Depot.AjouterEchange(e);
        _ = hub.Clients.Diffuser("EntiteLlm", () => Etat());
        Tracer(id, complet: true);
        return id;
    }

    /// <summary>
    /// Applique les actes d'un échange (proposé ou décidé) : chacun par les pouvoirs du joueur, après vérification ; un acte refusé par
    /// les règles est noté avec sa raison. Rend ce que les règles en ont fait.
    /// </summary>
    public async Task<List<string>> Appliquer(long id, FamillesLlm? permises = null)
    {
        var e = service.Depot.Echange(id) ?? throw new ArgumentException(L.T("llm.erreur.echange"));
        if (e.Statut is not (1 or 5)) throw new ArgumentException(L.T("llm.erreur.dejaTraite"));
        var familles = permises ?? Reglages.Familles;
        var libre = Reglages.Libre;
        var auMonde = Reglages.Monde;
        var actes = JsonSerializer.Deserialize<List<ActeLlm>>(e.Actes, Actes.Json) ?? [];
        var monde = service.Monde;
        // Une prière déjà close : l'échange arrive trop tard.
        if (e.Priere != 0 && actes.All(a => a.Type is Actes.Exaucer or Actes.Refuser or Actes.Rien)
            && (monde.Prieres().FirstOrDefault(p => p.Id == e.Priere) is not { Issue: IssuePriere.EnAttente } || service.Depot.PriereEnDialogue(e.Priere)))
        {
            service.Depot.MettreAJourEchange(e with { Statut = 4, Resultats = JsonSerializer.Serialize(new[] { L.T("llm.resultat.tropTard") }, Actes.Json) });
            Tracer(e.Id, complet: false);
            await hub.Clients.Diffuser("EntiteLlm", () => Etat());
            return [L.T("llm.resultat.tropTard")];
        }
        // Chaque acte d'un bloc (ses lignes du journal sont les siennes), noté au registre ; un acte refusé par les règles y est noté aussi.
        var resultats = new List<string>();
        long premier = 0, dernier = -1;
        foreach (var a in actes)
        {
            if (a.Type == Actes.Rien)
            {
                resultats.Add(L.T("llm.resultat.rien"));
                continue;
            }
            // Proposé puis accepté plus tard : le monde a pu changer (mort, communauté dispersée, prière close). Ce n'est pas un refus
            // des règles, c'est trop tard (0.36.4).
            if (monde.Secondes > e.Le)
            {
                string? tard = null;
                try
                {
                    Essais.Essayer(monde, a, []);
                }
                catch (ArgumentException x)
                {
                    tard = L.T("llm.resultat.tropTardRaison", ("raison", x.Message));
                }
                if (tard is not null)
                {
                    Registre.Noter(service, Registre.Llm, a, new FaitEntite([tard], -1, 0), 1, 0, e.Id);
                    resultats.Add(tard);
                    continue;
                }
            }
            try
            {
                var (fait, p, d) = monde.Executer(m => Actes.Appliquer(m, a, familles, [], libre, auMonde));
                Registre.Noter(service, Registre.Llm, a, fait, p, d, e.Id);
                resultats.Add(string.Join(" ", fait.Resultats));
                if (d >= p)
                {
                    if (premier == 0) premier = p;
                    dernier = d;
                }
            }
            catch (ArgumentException x)
            {
                var refus = L.T("llm.resultat.refuse", ("raison", x.Message));
                Registre.Noter(service, Registre.Llm, a, new FaitEntite([refus], -1, 0), 1, 0, e.Id);
                resultats.Add(refus);
            }
        }
        if (resultats.Count == 0) resultats.Add(L.T("llm.resultat.rien"));
        service.Depot.MettreAJourEchange(e with
        {
            Statut = 0, Resultats = JsonSerializer.Serialize(resultats, Actes.Json), JournalPremier = dernier >= premier ? premier : 0,
            JournalDernier = dernier >= premier ? dernier : -1, AppliqueLe = monde.Secondes,
        });
        // Un échange appliqué aussitôt est tracé une seconde fois, court : ce que les règles en ont fait.
        Tracer(e.Id, complet: false);
        service.Sauvegarder();
        await hub.Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        await hub.Clients.Diffuser("EntiteLlm", () => Etat());
        return resultats;
    }

    /// <summary>Le joueur rejette une proposition.</summary>
    public void Rejeter(long id)
    {
        var e = service.Depot.Echange(id) ?? throw new ArgumentException(L.T("llm.erreur.echange"));
        if (e.Statut is not (1 or 5)) throw new ArgumentException(L.T("llm.erreur.dejaTraite"));
        service.Depot.MettreAJourEchange(e with { Statut = 2 });
        Tracer(id, complet: false);
    }

    /// <summary>L'échange dans le fichier de trace et la console (seulement si la case est cochée).</summary>
    private void Tracer(long id, bool complet)
    {
        if (!trace.Active || service.Depot.Echange(id) is not { } e) return;
        trace.Echange(Dto(e, complet), complet);
    }

    private static readonly string[] Statuts = ["applique", "propose", "rejete", "erreur", "tropTard", "rien"];

    /// <summary>Un échange pour le navigateur ; <paramref name="complet"/> : avec ce qu'a lu le LLM et sa réponse brute.</summary>
    public EchangeDto Dto(EchangeEntite e, bool complet)
    {
        var (_, humains) = service.Monde.InstantaneHumains();
        var communautes = service.Monde.Communautes();
        List<ActeLlm> actes;
        List<string> resultats;
        try
        {
            actes = JsonSerializer.Deserialize<List<ActeLlm>>(e.Actes, Actes.Json) ?? [];
            resultats = JsonSerializer.Deserialize<List<string>>(e.Resultats, Actes.Json) ?? [];
        }
        catch (JsonException)
        {
            (actes, resultats) = ([], []);
        }
        var code = e.Statut >= 0 && e.Statut < Statuts.Length ? Statuts[e.Statut] : "erreur";
        return new EchangeDto(e.Id, L.Date(e.Le), e.Quand, L.T($"llm.occasion.court.{(OccasionLlm)e.Occasion}"), e.Humain,
            e.Humain >= 1 && e.Humain <= humains.Count ? humains[e.Humain - 1].Nom : null, e.Communaute,
            e.Communaute >= 1 && e.Communaute <= communautes.Count ? L.Communaute(communautes[e.Communaute - 1].Nom) : null,
            e.Priere, e.Modele, Math.Round(e.DureeMs / 1000.0, 1), e.Mots, e.Pensee, Decrire(actes).ToList(), resultats,
            L.T($"llm.statut.{code}"), code, e.Erreur, e.JournalPremier, complet ? e.Lu : null, complet ? e.Reponse : null);
    }

    private static IEnumerable<string> Decrire(IEnumerable<ActeLlm> actes)
    {
        foreach (var a in actes)
        {
            if (a.Type == Actes.Libre) yield return string.Join("\n", Libres.Decrire(a));
            else yield return Decrire(a);
        }
    }

    /// <summary>Un acte en mots courts (pour la liste des échanges).</summary>
    private static string Decrire(ActeLlm a)
    {
        var morceaux = new List<string> { a.Type };
        void Ajouter(string nom, object? v)
        {
            if (v is null) return;
            morceaux.Add(v is int[] t ? $"{nom}=[{string.Join(",", t)}]" : string.Create(CultureInfo.InvariantCulture, $"{nom}={v}"));
        }
        Ajouter("humain", a.Humain); Ajouter("humains", a.Humains); Ajouter("communaute", a.Communaute); Ajouter("communautes", a.Communautes);
        Ajouter("cible", a.Cible); Ajouter("cibles", a.Cibles); Ajouter("parole", a.Parole); Ajouter("mode", a.Mode); Ajouter("geste", a.Geste);
        Ajouter("maniere", a.Maniere); Ajouter("param", a.Param); Ajouter("direction", a.Direction); Ajouter("calamite", a.Calamite);
        Ajouter("force", a.Force); Ajouter("jours", a.Jours); Ajouter("priere", a.Priere); Ajouter("signe", a.Signe); Ajouter("catastrophe", a.Catastrophe);
        Ajouter("x", a.X); Ajouter("y", a.Y); Ajouter("intensite", a.Intensite); Ajouter("operation", a.Operation); Ajouter("a", a.A); Ajouter("b", a.B);
        Ajouter("lien", a.Lien); Ajouter("espece", a.Espece); Ajouter("mutuel", a.Mutuel); Ajouter("passion", a.Passion);
        return string.Join(" ", morceaux);
    }

    /// <summary>Teste la connexion : rend les modèles installés.</summary>
    public Task<List<string>> Modeles(ConnexionLlm c, CancellationToken arret) => client.Modeles(c, arret);
}
