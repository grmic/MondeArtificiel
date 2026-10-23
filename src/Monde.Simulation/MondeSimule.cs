using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation;

/// <summary>
/// État sauvegardable du monde. Les champs facultatifs sont absents pour un monde
/// qui n'a pas encore d'humains : ils sont alors créés à partir de la graine.
/// </summary>
/// <param name="Facteur">Vitesse : secondes du monde par seconde réelle (0 = pause).</param>
public sealed record EtatMonde(ulong Graine, long Secondes, long Pas, double Facteur)
{
    public IReadOnlyList<Humain>? Humains { get; init; }
    public ushort[]? Gibier { get; init; }
    public ushort[]? Plantes { get; init; }
    public ulong[]? Alea { get; init; }

    /// <summary>Événements du journal pas encore enregistrés (ils s'ajoutent au journal sauvegardé).</summary>
    public IReadOnlyList<EvenementJournal>? JournalNonEnregistre { get; init; }

    /// <summary>Numéro que portera le prochain événement du journal.</summary>
    public long ProchainIdJournal { get; init; } = 1;

    /// <summary>Communautés, actives ou dispersées (numérotées de 1 à N).</summary>
    public IReadOnlyList<Communaute>? Communautes { get; init; }

    /// <summary>Pluies de l'année par région climatique.</summary>
    public double[]? Pluies { get; init; }

    /// <summary>Relations entre communautés qui ne sont pas indifférentes.</summary>
    public IReadOnlyList<RelationCommunautes>? Relations { get; init; }

    /// <summary>Dernière offense entre deux communautés (cause d'une éventuelle hostilité).</summary>
    public IReadOnlyList<OffenseCommunautes>? Offenses { get; init; }

    /// <summary>Instantanés de saison des communautés pas encore enregistrés (ils s'ajoutent à leur histoire sauvegardée).</summary>
    public IReadOnlyList<ArchiveCommunaute>? ArchivesNonEnregistrees { get; init; }

    /// <summary>Catastrophes en cours, et celles terminées pas encore enregistrées.</summary>
    public IReadOnlyList<Catastrophe>? Catastrophes { get; init; }

    /// <summary>Maladies apparues depuis le début du monde (numérotées de 1 à N).</summary>
    public IReadOnlyList<Pathogene>? Pathogenes { get; init; }

    /// <summary>Dégâts des catastrophes sur la nature, par tuile (voir <see cref="Ecosysteme.Degats"/>).</summary>
    public ushort[]? DegatsNature { get; init; }

    /// <summary>Réglages choisis à la création du monde (absents : réglages d'origine, voir <see cref="ConfigurationMonde.ParDefaut"/>).</summary>
    public ConfigurationMonde? Configuration { get; init; }

    /// <summary>Guerres entre communautés, en cours ou finies.</summary>
    public IReadOnlyList<Guerre>? Guerres { get; init; }

    /// <summary>Feux entretenus par les humains.</summary>
    public IReadOnlyList<Foyer>? Foyers { get; init; }

    /// <summary>Huttes bâties par les humains.</summary>
    public IReadOnlyList<Hutte>? Huttes { get; init; }

    /// <summary>Braises des feux consumés, qui couvent encore.</summary>
    public IReadOnlyList<Braise>? Braises { get; init; }

    /// <summary>Réserves d'eau et de nourriture.</summary>
    public IReadOnlyList<Reserve>? Reserves { get; init; }

    /// <summary>Première fois que chaque effet a été obtenu dans le monde.</summary>
    public IReadOnlyList<Decouverte>? Decouvertes { get; init; }

    /// <summary>Savoirs vivants au début de chaque année, dans le monde et dans chaque communauté.</summary>
    public IReadOnlyList<MesureSavoirs>? Progres { get; init; }

    /// <summary>Rassemblements de deux communautés, du premier au dernier.</summary>
    public IReadOnlyList<Rassemblement>? Rassemblements { get; init; }

    /// <summary>Images peintes sur la roche.</summary>
    public IReadOnlyList<Oeuvre>? Oeuvres { get; init; }

    /// <summary>Religions, prières en mémoire, lieux sacrés et malheurs annoncés (null : aucun).</summary>
    public IReadOnlyList<Religion>? Religions { get; init; }
    public IReadOnlyList<Priere>? Prieres { get; init; }
    public IReadOnlyList<LieuSacre>? LieuxSacres { get; init; }
    public IReadOnlyList<Prophetie>? Propheties { get; init; }

    /// <summary>Gestes de l'Entité qui durent encore, et ce que les communautés se racontent de ses gestes visibles (null : aucun).</summary>
    public IReadOnlyList<EffetEntite>? EffetsEntite { get; init; }
    public IReadOnlyList<SouvenirEntite>? SouvenirsEntite { get; init; }

    /// <summary>Numéro de la prochaine prière (0 : d'après les prières connues).</summary>
    public int ProchainePriere { get; init; }
}

/// <summary>État de la nature et de la population, pour l'observateur.</summary>
/// <param name="Abondance">Ressources des terres habitables rapportées à leur niveau naturel.</param>
/// <param name="RegionsSeches">Régions habitées où les pluies de l'année sont inférieures de 30 % à la normale.</param>
public sealed record Indicateurs(int Vivants, int Enfants, double Abondance, double ReservesMoyennes, int RegionsSeches,
    int RegionsHabitees, int CommunautesEnDisette);

/// <summary>
/// Le monde en cours de simulation. Le temps avance par pas fixes de 10 minutes du monde ;
/// les secondes non encore converties en pas sont conservées pour le prochain appel.
/// Les méthodes sont protégées par un verrou : le moteur et les clients peuvent l'appeler en parallèle.
/// Tout le hasard vient d'un seul générateur sauvegardé : un monde rechargé continue exactement
/// comme s'il n'avait jamais été arrêté.
/// </summary>
public sealed class MondeSimule
{
    public const long SecondesParPas = 600;
    private const long PasParJour = Calendrier.SecondesParJour / SecondesParPas;

    private readonly object _verrou = new();
    private readonly Population _humains;
    private readonly Ecosysteme _ecosysteme;
    private readonly Alea _alea;
    private readonly Comportement _comportement;
    private readonly Catastrophes _catastrophes;
    private readonly List<AlerteCatastrophe> _alertes = [];
    private bool _pauseDemandee;
    // Plus aucun humain vivant : l'extinction n'est annoncée qu'une fois (un monde rechargé éteint ne l'annonce pas de nouveau).
    private bool _eteint;
    private string? _extinction;
    private bool _pauseExtinction;
    private readonly VieSociale _social;
    private readonly Installations _installations;
    private readonly Cultes _cultes;
    // Effets que des vivants savent obtenir : quand le dernier qui savait meurt ou oublie, le savoir est perdu.
    /// <summary>Effets que quelqu'un sait encore obtenir, avec un savoir qui le dit (pour le raconter s'il se perd).</summary>
    private Dictionary<Effet, (Savoir Savoir, List<int> Detenteurs)> _effetsConnus;

    /// <summary>Savoirs vivants au début de chaque année (voir <see cref="MesurerSavoirs"/>).</summary>
    private readonly List<MesureSavoirs> _progres = [];
    private readonly List<Communaute> _communautes;
    private readonly HashSet<string> _noms;
    private long _secondes;
    private long _pas;
    private double _facteur;
    private double _reliquat;
    private Objectif? _objectif;
    private readonly List<EvenementJournal> _journalNonEnregistre = [];
    private readonly List<ArchiveCommunaute> _archivesNonEnregistrees = [];
    private readonly Queue<EvenementJournal> _journalRecent = new();
    private readonly Numeroteur _numeros = new();

    /// <summary>Nombre d'événements récents gardés en mémoire pour les navigateurs (hors événements fréquents).</summary>
    public const int TailleJournalRecent = 500;
    private ObjectifAtteint? _objectifAtteint;

    public MondeSimule(Carte carte, EtatMonde etat)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(etat.Secondes);
        ArgumentOutOfRangeException.ThrowIfNegative(etat.Pas);
        if (carte.Graine != etat.Graine)
        {
            throw new ArgumentException("La carte ne correspond pas à la graine du monde.", nameof(carte));
        }
        var configuration = etat.Configuration ?? ConfigurationMonde.ParDefaut(etat.Graine);
        if (configuration.Graine != etat.Graine || configuration.Largeur != carte.Largeur || configuration.Hauteur != carte.Hauteur
            || configuration.Couples != carte.Emplacements.Count)
        {
            throw new ArgumentException("La carte ne correspond pas aux réglages du monde.", nameof(carte));
        }

        Carte = carte;
        Configuration = configuration;
        Graine = etat.Graine;
        _secondes = etat.Secondes;
        _pas = etat.Pas;
        _facteur = Vitesse.FacteurValide(etat.Facteur) ? etat.Facteur : 0;
        _humains = new Population(etat.Humains is { Count: > 0 } h
            ? h.Select(x => x.Copier()).OrderBy(x => x.Id)
            : GenerateurPopulation.Generer(carte, etat.Graine, etat.Secondes));
        _ecosysteme = new Ecosysteme(carte, etat.Gibier?.ToArray(), etat.Plantes?.ToArray(), etat.Pluies?.ToArray(), etat.DegatsNature?.ToArray());
        _alea = etat.Alea is { } a ? Alea.Depuis(a, commun: true) : new Alea(etat.Graine ^ 0x534D554C4154UL) { Commun = true };
        _communautes = (etat.Communautes ?? []).Select(c => c.Copier()).OrderBy(c => c.Id).ToList();
        _numeros.Prochain = Math.Max(1, etat.ProchainIdJournal);
        _social = new VieSociale(_alea) { Communautes = _communautes, Numeros = _numeros, Carte = carte };
        _social.ChargerGuerres(etat.Guerres ?? []);
        _social.ChargerRelations(etat.Relations ?? []);
        _social.ChargerRassemblements(etat.Rassemblements ?? []);
        _social.ChargerOffenses(etat.Offenses ?? []);
        Flore = new Flore(etat.Graine);
        _social.Flore = Flore;
        _installations = new Installations(etat.Foyers, etat.Huttes, etat.Decouvertes, etat.Oeuvres, etat.Braises, etat.Reserves);
        _social.Installations = _installations;
        _comportement = new Comportement(carte, _ecosysteme, _alea)
        {
            Social = _social, Numeros = _numeros, Flore = Flore, Installations = _installations,
        };
        _catastrophes = new Catastrophes(carte, _ecosysteme, _alea, etat.Catastrophes, etat.Pathogenes)
        {
            Numeros = _numeros, Comportement = _comportement, Communautes = _communautes,
        };
        _comportement.Catastrophes = _catastrophes;
        _cultes = new Cultes(etat.Religions, etat.Prieres, etat.LieuxSacres, etat.Propheties, etat.EffetsEntite, etat.SouvenirsEntite);
        if (etat.ProchainePriere > _cultes.ProchainePriere) _cultes.ProchainePriere = etat.ProchainePriere;
        _comportement.Cultes = _social.Cultes = _cultes;
        // Une famine envoyée par l'Entité pèse encore sur les terres.
        _comportement.EffetsDuJour(etat.Secondes);
        _noms = _humains.Select(x => x.Nom).ToHashSet();
        _eteint = !_humains.Vivants().Any(x => x.EstVivant);
        _effetsConnus = EffetsConnus();
        _progres.AddRange(etat.Progres ?? []);
        foreach (var e in etat.JournalNonEnregistre ?? []) Ajouter(e);
        _archivesNonEnregistrees.AddRange(etat.ArchivesNonEnregistrees ?? []);

        for (var i = 0; i < _humains.Count; i++)
        {
            if (_humains[i].Id != i + 1) throw new ArgumentException("Les identifiants des humains doivent aller de 1 à N.");
        }
    }

    /// <summary>Nouveau monde : il commence à l'aube du premier jour, en pause.</summary>
    public static EtatMonde Nouveau(ulong graine) => Nouveau(ConfigurationMonde.ParDefaut(graine));

    /// <summary>Nouveau monde avec ses réglages : il commence à l'aube du premier jour, en pause.</summary>
    public static EtatMonde Nouveau(ConfigurationMonde configuration) =>
        new(configuration.Graine, 6 * Calendrier.SecondesParHeure, 0, 0) { Configuration = configuration };

    public Carte Carte { get; }

    /// <summary>Les plantes du monde, leurs vertus et leurs dangers (tirées de la graine).</summary>
    public Flore Flore { get; }

    /// <summary>Réglages choisis à la création du monde.</summary>
    public ConfigurationMonde Configuration { get; }

    public ulong Graine { get; }

    /// <summary>Vitesse demandée : secondes du monde par seconde réelle (0 = pause).</summary>
    public double Facteur { get { lock (_verrou) return _facteur; } }

    /// <summary>Objectif « avancer jusqu'à » en cours, ou null.</summary>
    public Objectif? Objectif { get { lock (_verrou) return _objectif; } }

    public long Secondes { get { lock (_verrou) return _secondes; } }

    /// <summary>Date, pas, vitesse et objectif lus ensemble, sans copier les humains (diffusion fréquente).</summary>
    public (long Secondes, long Pas, double Facteur, Objectif? Objectif) Horloge
    {
        get { lock (_verrou) return (_secondes, _pas, _facteur, _objectif); }
    }

    public DateMonde Date { get { lock (_verrou) return Calendrier.Decomposer(_secondes); } }

    /// <summary>Copie complète de l'état, pour la sauvegarde.</summary>
    public EtatMonde Etat
    {
        get
        {
            lock (_verrou)
            {
                return new EtatMonde(Graine, _secondes, _pas, _facteur)
                {
                    Humains = _humains.Select(h => h.Copier()).ToList(),
                    Gibier = _ecosysteme.Gibier.ToArray(),
                    Plantes = _ecosysteme.Plantes.ToArray(),
                    Alea = _alea.Etat,
                    JournalNonEnregistre = _journalNonEnregistre.ToList(),
                    ProchainIdJournal = _numeros.Prochain,
                    Communautes = _communautes.Select(c => c.Copier()).ToList(),
                    Pluies = _ecosysteme.Pluies.ToArray(),
                    Relations = _social.Relations,
                    Rassemblements = _social.Rassemblements.Select(r => r.Copier()).ToList(),
                    Offenses = _social.Offenses,
                    ArchivesNonEnregistrees = _archivesNonEnregistrees.ToList(),
                    Catastrophes = _catastrophes.EnMemoire,
                    Pathogenes = _catastrophes.Pathogenes.ToList(),
                    DegatsNature = _ecosysteme.Degats.ToArray(),
                    Configuration = Configuration,
                    Guerres = _social.Guerres.Select(g => g.Copier()).ToList(),
                    Foyers = _installations.Foyers.ToList(),
                    Huttes = _installations.Huttes.ToList(),
                    Braises = _installations.Braises.ToList(),
                    Reserves = _installations.Reserves.ToList(),
                    Decouvertes = _installations.Decouvertes.ToList(),
                    Progres = _progres.ToList(),
                    Oeuvres = _installations.Oeuvres.ToList(),
                    Religions = _cultes.Religions.Select(r => r.Copier()).ToList(),
                    Prieres = _cultes.Prieres.Select(p => p.Copier()).ToList(),
                    LieuxSacres = _cultes.Lieux.ToList(),
                    Propheties = _cultes.Propheties.ToList(),
                    EffetsEntite = _cultes.Effets.ToList(),
                    SouvenirsEntite = _cultes.Souvenirs.ToList(),
                    ProchainePriere = _cultes.ProchainePriere,
                };
            }
        }
    }

    /// <summary>
    /// Après une sauvegarde : les <paramref name="nombre"/> premiers événements et les <paramref name="archives"/>
    /// premiers instantanés en attente sont enregistrés, ainsi que les catastrophes terminées <paramref name="terminees"/>.
    /// </summary>
    public void MarquerJournalEnregistre(int nombre, int archives = 0, IReadOnlySet<long>? terminees = null)
    {
        lock (_verrou)
        {
            _journalNonEnregistre.RemoveRange(0, Math.Min(nombre, _journalNonEnregistre.Count));
            _archivesNonEnregistrees.RemoveRange(0, Math.Min(archives, _archivesNonEnregistrees.Count));
            if (terminees is { Count: > 0 }) _catastrophes.Oublier(terminees, _secondes);
        }
    }

    /// <summary>Contexte géologique du continent : failles et volcans (fixés par la graine).</summary>
    public Geologie Geologie => _catastrophes.Geologie;

    /// <summary>Catastrophes en cours et celles terminées pas encore enregistrées (copies).</summary>
    public List<Catastrophe> CatastrophesEnMemoire()
    {
        lock (_verrou) return _catastrophes.EnMemoire;
    }

    /// <summary>Maladies apparues depuis le début du monde.</summary>
    public List<Pathogene> Pathogenes()
    {
        lock (_verrou) return [.. _catastrophes.Pathogenes];
    }

    /// <summary>Écart de température du moment (°C) dû aux vagues de froid, canicules et hivers volcaniques.</summary>
    public double AnomalieTemperature
    {
        get { lock (_verrou) return _catastrophes.Anomalie; }
    }

    /// <summary>Si vrai, le monde se met en pause quand une catastrophe menace des humains (le serveur la laisse désactivée par défaut, depuis la 0.29.3).</summary>
    public bool PauseSurCatastrophe { get; set; }

    /// <summary>Si vrai, la vie quotidienne de chacun est notée au journal (non par défaut ; voir <see cref="Comportement.JournalQuotidien"/>).</summary>
    public bool JournalQuotidien
    {
        get => _comportement.JournalQuotidien;
        set { lock (_verrou) _comportement.JournalQuotidien = value; }
    }

    /// <summary>
    /// Clémence du monde, de 0 à 100 : la part des risques de maladie, d'infection, de mort en couches, de mort de nourrisson
    /// et de mort dans une catastrophe que l'Entité écarte (0 : le monde tel quel ; 100 : aucune de ces morts). Les écarts entre
    /// individus restent ; les guerres et les meurtres, choix des humains, ne changent pas.
    /// </summary>
    public int Clemence
    {
        get => (int)Math.Round(100 * _comportement.Clemence);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            lock (_verrou) _comportement.Clemence = _catastrophes.Clemence = value / 100.0;
        }
    }

    /// <summary>L'Entité change la clémence du monde : c'est noté au journal comme son acte.</summary>
    public void ChoisirClemence(int clemence)
    {
        lock (_verrou)
        {
            var avant = Clemence;
            Clemence = clemence;
            if (clemence == avant) return;
            var texte = clemence == 0 ? new Recit("j.clemence.fin")
                : new Recit(clemence > avant ? "j.clemence.plus" : "j.clemence.moins", ("n", clemence));
            _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Nature, 0, 0, texte, Origine.Entite)
            {
                Id = _numeros.Suivant(), Significatif = true,
            });
            VerserJournal();
        }
    }

    /// <summary>Alertes survenues depuis le dernier appel (elles ne sont rendues qu'une fois).</summary>
    public List<AlerteCatastrophe> PrendreAlertes()
    {
        lock (_verrou)
        {
            var alertes = _alertes.ToList();
            _alertes.Clear();
            return alertes;
        }
    }

    /// <summary>
    /// Provoque une catastrophe (voir <see cref="Catastrophes.Declencher"/>). Renvoie le numéro de l'événement qui
    /// l'annonce, ou 0 si elle n'a pas pu avoir lieu.
    /// </summary>
    public long Declencher(TypeCatastrophe type, int x, int y, double intensite, Origine origine = Origine.Entite)
    {
        lock (_verrou)
        {
            var id = _catastrophes.Declencher(type, x, y, intensite, _humains, _secondes, origine);
            VerserJournal();
            return id;
        }
    }

    /// <summary>Instantanés de saison pas encore enregistrés (copie).</summary>
    public List<ArchiveCommunaute> ArchivesEnMemoire()
    {
        lock (_verrou) return [.. _archivesNonEnregistrees];
    }

    /// <summary>Événements récents dont le numéro dépasse <paramref name="apresId"/>, du plus ancien au plus récent.</summary>
    public List<EvenementJournal> JournalRecent(long apresId = 0)
    {
        lock (_verrou) return _journalRecent.Where(e => e.Id > apresId).ToList();
    }

    /// <summary>Événement du journal pas encore enregistré ou encore récent, par son numéro (null sinon).</summary>
    public EvenementJournal? EvenementEnMemoire(long id)
    {
        lock (_verrou)
        {
            return _journalNonEnregistre.FirstOrDefault(e => e.Id == id) ?? _journalRecent.FirstOrDefault(e => e.Id == id);
        }
    }

    private void Ajouter(EvenementJournal evenement)
    {
        var numerote = evenement.Id > 0 ? evenement : evenement with { Id = _numeros.Suivant() };
        // Sans communauté notée, l'événement appartient à celle de l'humain (d'un nouveau-né : celle de sa mère).
        if (numerote.CommunauteId == 0 && numerote.HumainId > 0 && numerote.HumainId <= _humains.Count)
        {
            var communaute = _humains[numerote.HumainId - 1].CommunauteId;
            if (communaute == 0 && numerote.Type == TypeEvenement.Naissance && numerote.AutreId > 0 && numerote.AutreId <= _humains.Count)
            {
                communaute = _humains[numerote.AutreId - 1].CommunauteId;
            }
            if (communaute != 0) numerote = numerote with { CommunauteId = communaute };
        }
        _numeros.Prochain = Math.Max(_numeros.Prochain, numerote.Id + 1);
        _journalNonEnregistre.Add(numerote);
        if (TypesEvenement.EstFrequent(numerote.Type)) return;
        _journalRecent.Enqueue(numerote);
        if (_journalRecent.Count > TailleJournalRecent) _journalRecent.Dequeue();
    }

    /// <summary>Copie des communautés (actives et dispersées).</summary>
    public List<Communaute> Communautes()
    {
        lock (_verrou) return _communautes.Select(c => c.Copier()).ToList();
    }

    /// <summary>Rassemblements de communautés, du premier au dernier (copies).</summary>
    public List<Rassemblement> Rassemblements()
    {
        lock (_verrou) return _social.Rassemblements.Select(r => r.Copier()).ToList();
    }

    /// <summary>Relations entre communautés qui ne sont pas indifférentes.</summary>
    public List<RelationCommunautes> Relations()
    {
        lock (_verrou) return _social.Relations;
    }

    /// <summary>Naissance, mort et communauté de chaque humain ayant vécu (pour les courbes démographiques, sans copier les humains).</summary>
    public (long Secondes, List<(long Naissance, long? Mort, int Communaute)> Vies) Vies()
    {
        lock (_verrou) return (_secondes, _humains.Select(h => (h.Naissance, h.MortLe, h.CommunauteId)).ToList());
    }

    /// <summary>Copie des guerres entre communautés, en cours ou finies.</summary>
    public List<Guerre> Guerres()
    {
        lock (_verrou) return _social.Guerres.Select(g => g.Copier()).ToList();
    }

    /// <summary>Abondance de chaque tuile (0 à 255, 128 = ce que la saison et les pluies permettent), pour la carte des ressources.</summary>
    public byte[] CarteAbondance()
    {
        lock (_verrou) return _ecosysteme.CarteAbondance(_secondes);
    }

    /// <summary>Pluies de l'année par région climatique (copie), et nombre de régions en largeur et en hauteur.</summary>
    public (double[] Pluies, int RegionsX, int RegionsY) Pluies()
    {
        lock (_verrou) return (_ecosysteme.Pluies.ToArray(), _ecosysteme.RegionsX, _ecosysteme.RegionsY);
    }

    /// <summary>État de la nature et de la population, calculé à la demande.</summary>
    public Indicateurs Indicateurs()
    {
        lock (_verrou)
        {
            var vivants = _humains.Vivants().Where(h => h.EstVivant).ToList();
            var regions = vivants.Select(h => _ecosysteme.Region(h.X, h.Y)).ToHashSet();
            double actuel = 0, naturel = 0;
            for (var i = 0; i < Carte.Biomes.Length; i++)
            {
                if (!Biomes.EstHabitable((Biome)Carte.Biomes[i])) continue;
                actuel += _ecosysteme.Plantes[i] + _ecosysteme.Gibier[i];
                naturel += (Carte.Plantes[i] + Carte.Gibier[i]) * 100;
            }
            var adultes = vivants.Where(h => !h.EstEnfant(_secondes)).ToList();
            return new Indicateurs(vivants.Count, vivants.Count - adultes.Count, naturel == 0 ? 0 : actuel / naturel,
                adultes.Count == 0 ? 0 : adultes.Average(h => h.Reserves),
                regions.Count(r => _ecosysteme.Pluies[r] < 0.7), regions.Count,
                _communautes.Count(c => c.Active && c.Disette));
        }
    }

    /// <summary>Copie légère des humains seulement (diffusion fréquente vers les navigateurs).</summary>
    public (long Secondes, List<Humain> Humains) InstantaneHumains()
    {
        lock (_verrou) return (_secondes, _humains.Select(h => h.Copier()).ToList());
    }

    /// <summary>Choisit une vitesse préréglée (0 = pause). Annule un objectif en cours.</summary>
    public void ChangerVitesse(int niveau)
    {
        if (!Vitesse.Existe(niveau)) throw new ArgumentOutOfRangeException(nameof(niveau), niveau, "Vitesse inconnue.");
        ChangerFacteur(Vitesse.Toutes[niveau].Facteur);
    }

    /// <summary>Choisit une vitesse libre (0 = pause). Annule un objectif en cours.</summary>
    public void ChangerFacteur(double facteur)
    {
        if (!Vitesse.FacteurValide(facteur)) throw new ArgumentOutOfRangeException(nameof(facteur), facteur, "Vitesse hors limites.");
        lock (_verrou)
        {
            _facteur = facteur;
            _reliquat = 0;
            _objectif = null;
        }
    }

    /// <summary>Lance un « avancer jusqu'à » : le monde file à la vitesse maximale, puis se met en pause.</summary>
    public void FixerObjectif(Objectif objectif)
    {
        lock (_verrou)
        {
            if (objectif.Limite <= _secondes) throw new ArgumentOutOfRangeException(nameof(objectif), "L'objectif est déjà passé.");
            _objectif = objectif;
            _reliquat = 0;
        }
    }

    /// <summary>Renvoie, une seule fois, la description du dernier objectif atteint (null sinon).</summary>
    public string? PrendreObjectifAtteint() => PrendreObjectifAtteintDetaille()?.Description;

    /// <summary>Renvoie, une seule fois, le dernier objectif atteint ou abandonné après cent ans (null sinon).</summary>
    public ObjectifAtteint? PrendreObjectifAtteintDetaille()
    {
        lock (_verrou)
        {
            var atteint = _objectifAtteint;
            _objectifAtteint = null;
            return atteint;
        }
    }

    /// <summary>
    /// Fait avancer le monde d'une durée réelle, selon la vitesse courante (vitesse maximale si un objectif est en cours).
    /// Avec un <paramref name="budget"/>, le calcul s'arrête quand il est épuisé : si la machine ne suit pas,
    /// le retard est abandonné (le monde ralentit au lieu de s'emballer). Renvoie le nombre de pas exécutés.
    /// </summary>
    public int Avancer(TimeSpan dureeReelle, TimeSpan? budget = null)
    {
        if (dureeReelle <= TimeSpan.Zero) return 0;
        lock (_verrou)
        {
            var facteur = _objectif is null ? _facteur : Vitesse.FacteurMax;
            if (facteur == 0) return 0;
            // Une pause du programme (veille de l'ordinateur) ne doit pas faire sauter des années :
            // l'écart pris en compte est plafonné à une seconde réelle.
            var reel = Math.Min(dureeReelle.TotalSeconds, 1.0);
            _reliquat += reel * facteur;
            var secondes = (long)_reliquat;
            _reliquat -= secondes;
            var (pas, complet) = AvancerInterne(secondes, budget);
            if (!complet) _reliquat = 0;
            return pas;
        }
    }

    /// <summary>Avance d'un nombre exact de secondes du monde (pas à pas, tests).</summary>
    public int AvancerDe(long secondesMonde)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(secondesMonde);
        lock (_verrou) return AvancerInterne(secondesMonde, null).Pas;
    }

    private (int Pas, bool Complet) AvancerInterne(long secondesMonde, TimeSpan? budget)
    {
        var chrono = budget is null ? null : System.Diagnostics.Stopwatch.StartNew();
        var fin = _secondes + secondesMonde;
        var objectif = _objectif;
        if (objectif is not null) fin = Math.Min(fin, objectif.Limite);
        var nombreAvant = _humains.Count;
        var vivantsAvant = objectif?.Type == TypeObjectif.Mort ? _humains.Vivants().Count(h => h.EstVivant) : 0;

        var pas = 0;
        var evenement = false;
        while (true)
        {
            // Une catastrophe menace des humains (survenue au pas précédent, ou provoquée par l'Entité) :
            // le monde s'arrête pour que l'observateur voie ce qui se passe.
            if (_pauseDemandee)
            {
                _pauseDemandee = false;
                if (PauseSurCatastrophe)
                {
                    _objectif = null;
                    _facteur = 0;
                    _reliquat = 0;
                    return (pas, false);
                }
            }

            // Le dernier humain vient de mourir : le monde s'arrête, quel que soit le réglage des pauses.
            if (_pauseExtinction)
            {
                _pauseExtinction = false;
                _objectif = null;
                _facteur = 0;
                _reliquat = 0;
                return (pas, false);
            }

            // Chaque pas s'exécute à l'instant exact où il tombe.
            var prochain = (_secondes / SecondesParPas + 1) * SecondesParPas;
            if (prochain > fin)
            {
                _secondes = fin;
                break;
            }
            _secondes = prochain;
            ExecuterPas();
            pas++;

            if (objectif is not null)
            {
                evenement = objectif.Type switch
                {
                    TypeObjectif.Naissance => _humains.Count > nombreAvant,
                    // Les naissances du pas sont vivantes : on compare aux vivants d'avant, nouveau-nés compris.
                    TypeObjectif.Mort => _humains.Vivants().Count(h => h.EstVivant) < vivantsAvant + (_humains.Count - nombreAvant),
                    _ => false,
                };
                if (evenement) break;
            }
            if (chrono is not null && chrono.Elapsed >= budget) return (pas, false);
        }

        if (objectif is not null && (evenement || _secondes >= objectif.Limite))
        {
            var rien = !evenement && objectif.Type != TypeObjectif.Date;
            _objectifAtteint = new ObjectifAtteint(objectif, rien, rien ? $"{objectif.Description} : rien en cent ans" : objectif.Description);
            _objectif = null;
            _facteur = 0;
            _reliquat = 0;
            return (pas, false);
        }
        return (pas, true);
    }

    /// <summary>QI à partir duquel une naissance est signalée à l'Entité (trois écarts-types au-dessus de la moyenne).</summary>
    public const double EspritHorsDuCommun = 145;

    /// <summary>Un pas de simulation (10 minutes du monde).</summary>
    private void ExecuterPas()
    {
        _pas++;
        var nouveauJour = _pas % PasParJour == 0;
        if (nouveauJour)
        {
            if (_secondes % Calendrier.SecondesParAn < Calendrier.SecondesParJour) NouvelleAnnee();
            _ecosysteme.Journee(_secondes);
            _catastrophes.Jour(_humains, _secondes);
        }
        if (_pas % (Calendrier.SecondesParHeure / SecondesParPas) == 0)
        {
            _catastrophes.Heure(_humains, _secondes);
            _catastrophes.FeuxDeCamp(_installations, _humains, _secondes);
            _comportement.Raids(_humains, _secondes);
        }

        // Toutes les heures, chacun marque les terres de son groupe ; au début de chaque saison, l'histoire des groupes est archivée.
        if (_pas % (Calendrier.SecondesParHeure / SecondesParPas) == 0) _social.Parcourir(_humains, Carte, _secondes);
        if (nouveauJour && _secondes % (Calendrier.JoursParSaison * Calendrier.SecondesParJour) < Calendrier.SecondesParJour)
        {
            _social.Archiver(_humains, _communautes, Carte, _secondes);
            _archivesNonEnregistrees.AddRange(_social.Archives);
            _social.Archives.Clear();
        }

        _installations.Bruler(SecondesParPas / (double)Calendrier.SecondesParHeure);

        if (Coeurs == 1) VivreChacunSonTour(nouveauJour);
        else VivreEnsemble(nouveauJour);

        foreach (var (mere, pereId) in _comportement.Accouchements)
        {
            var pere = pereId > 0 ? _humains[pereId - 1] : null;
            var bebe = GenerateurPopulation.Naissance(_humains.Count + 1, mere, pere, _secondes, _alea, _noms, _humains);
            _humains.Ajouter(bebe);
            var naissance = new EvenementJournal(_secondes, TypeEvenement.Naissance, bebe.Id, mere.Id,
                new Recit("j.naissance", ("b", bebe), ("m", mere), ("p", pere is null ? "" : new Recit("j.naissance.pere", ("p", pere)))))
            {
                Id = _numeros.Suivant(),
            };
            _comportement.Journal.Add(naissance);
            // Regard de l'Entité : un esprit hors du commun (QI de 145 ou plus, environ une naissance sur 740) ; nul ne le sait.
            if (bebe.Personnalite.Intelligence.Generale >= EspritHorsDuCommun)
            {
                _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Naissance, bebe.Id, mere.Id,
                    new Recit("j.naissance.genie", ("b", bebe), ("qi", (long)Math.Round(bebe.Personnalite.Intelligence.Generale))))
                {
                    Id = _numeros.Suivant(), CauseId = naissance.Id, Significatif = true,
                });
            }
            Jardin.Confier(mere, TypeIntime.Joie, bebe.Id, $"{bebe.Nom} est né{(bebe.Sexe == Sexe.Femme ? "e" : "")} de moi : quelle joie !",
                30 + 60 * mere.Personnalite.Attachement, naissance.Id, _secondes, $"naissance:{bebe.Id}");
            if (pere is { EstVivant: true } && pere.PartenaireId == mere.Id)
            {
                Jardin.Confier(pere, TypeIntime.Joie, bebe.Id, $"{mere.Nom} m'a donné {(bebe.Sexe == Sexe.Femme ? "une fille" : "un fils")}, {bebe.Nom}.",
                    20 + 50 * pere.Personnalite.Attachement, naissance.Id, _secondes, $"naissance:{bebe.Id}");
            }
            // Une naissance réjouit la mère, et le père s'il est là et attaché à ses enfants.
            VieSociale.Rejouir(mere, 20 * mere.Personnalite.Attachement);
            if (pere is { EstVivant: true }) VieSociale.Rejouir(pere, 15 * pere.Personnalite.Attachement);
        }
        _comportement.Accouchements.Clear();

        foreach (var mort in _comportement.Deces) _social.Endeuiller(mort, _humains, _secondes);
        _comportement.Deces.Clear();

        if (nouveauJour)
        {
            _comportement.Population = _humains.Vivants().Count(h => h.EstVivant);
            _comportement.Journal.AddRange(_comportement.Routine.Resumer(_humains, _secondes).Select(e => e with { Id = _numeros.Suivant() }));
            _social.Journee(_humains, _secondes);
            _social.Coeur(_humains, _secondes);
            _social.Debattre(_humains, _communautes, _secondes);
            Oublier();
            Murir();
            Savoirs();
            _social.Humeurs(_humains, _secondes);
            _comportement.Croire(_humains, _secondes);
            _comportement.Regarder(_humains, _secondes);
            _comportement.EffetsDuJour(_secondes);
            _comportement.Religions(_humains, _secondes);
            _comportement.Soins(_humains, _secondes);
            _comportement.Arts(_humains, _secondes);
            _catastrophes.BrulisProtecteurs(_communautes, _humains, _secondes);
            _comportement.GaterLesReserves(_humains, _secondes);
            _comportement.IdeesDesReserves(_humains, _secondes);
            _social.Parler(_humains, _communautes, Carte, _secondes);
            _social.Visites(_humains, _secondes);
            _social.JourDeRassemblement(_humains, _secondes);
            if (_pas / PasParJour % VieSociale.JoursEntreRegroupements == 0)
            {
                _social.Regrouper(_humains, _communautes, _secondes, _noms);
                _social.ConstaterLangues(_humains, _communautes, _secondes);
                _social.Territoires(_humains, _communautes, _ecosysteme, Carte, _secondes);
                foreach (var h in _humains) if (h.EstVivant || h.Memoire.Jardin.Count > Jardin.TailleApresLaMort) Jardin.Mediter(h, _humains, _communautes, _secondes, Carte);
            }
        }
        VerserJournal();
    }

    /// <summary>
    /// Cœurs du processeur donnés au calcul. 1 : pas classique, chacun vit à son tour, l'un après l'autre (comme jusqu'à la 0.22.0).
    /// 0 (tous) ou plus de 1 : pas simultané, réparti sur les cœurs ; son histoire est la même quel que soit leur nombre,
    /// mais diffère de celle du pas classique. Modifiable en cours de route : pris en compte au pas suivant.
    /// </summary>
    public int Coeurs
    {
        get => Volatile.Read(ref _coeurs);
        set => Volatile.Write(ref _coeurs, Math.Max(0, value));
    }

    private int _coeurs;

    /// <summary>Pas classique : ceux qui vivaient au début du pas vivent l'un après l'autre, et chacun voit ce qu'ont fait ceux d'avant.</summary>
    private void VivreChacunSonTour(bool nouveauJour)
    {
        // La liste ne change pas pendant ce parcours.
        _humains.Recenser();
        var vivants = _humains.Vivants;
        var nombre = vivants.Count;
        for (var i = 0; i < nombre; i++)
        {
            var humain = vivants[i];
            _comportement.Vivre(humain, _humains, _secondes);
            if (nouveauJour) _comportement.Vieillir(humain, _secondes);
        }
    }

    // Les tours des humains, réutilisés de pas en pas.
    private readonly List<Tour> _tours = [];

    /// <summary>Pour les tests : appelé avec vrai avant les tours simultanés d'un pas, avec faux après (avant d'appliquer leurs effets).</summary>
    internal Action<bool>? Surveillant { get; set; }

    /// <summary>
    /// Pas simultané : tous ceux qui vivaient au début du pas vivent ces dix minutes en même temps, répartis sur les cœurs.
    /// Chacun agit sur une copie de lui-même, voit les autres tels qu'ils étaient au début du pas et tire son propre hasard ;
    /// ce qu'il fait aux autres et au monde commun est appliqué ensuite, humain après humain, dans l'ordre des identifiants.
    /// </summary>
    private void VivreEnsemble(bool nouveauJour)
    {
        _humains.Recenser();
        var vivants = _humains.Vivants;
        var nombre = vivants.Count;
        if (nombre == 0) return;
        var cle = _alea.Suivant();
        var baseNumeros = _numeros.Prochain;
        _comportement.Preparer();
        while (_tours.Count < nombre) _tours.Add(new Tour());
        var tours = _tours;
        Surveillant?.Invoke(true);
        var coeurs = Coeurs;
        var options = new ParallelOptions { MaxDegreeOfParallelism = coeurs > 0 ? coeurs : Environment.ProcessorCount };
        try
        {
            Parallel.For(0, nombre, options, i =>
            {
                var original = vivants[i];
                var soi = _humains.CopieDeTravail(original);
                var tour = tours[i];
                tour.Commencer(original, soi, cle ^ (ulong)original.Id * 0x9E3779B97F4A7C15UL, baseNumeros, i, nombre);
                tour.Entrer();
                Alea.Detourner(tour.Alea);
                try
                {
                    _comportement.Vivre(soi, _humains, _secondes);
                    if (nouveauJour) _comportement.Vieillir(soi, _secondes);
                }
                finally
                {
                    Tour.Sortir();
                    Alea.Detourner(null);
                }
            });
        }
        catch (AggregateException e) when (e.InnerExceptions.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
        }

        Surveillant?.Invoke(false);

        // Chacun reprend sa place sous sa nouvelle version ; les numéros d'événements reprennent après ceux du pas.
        var pris = 0;
        for (var i = 0; i < nombre; i++)
        {
            _humains.Remplacer(tours[i].Soi, i);
            pris = Math.Max(pris, tours[i].NumerosPris);
        }
        _numeros.Prochain = baseNumeros + (long)pris * nombre;
        for (var i = 0; i < nombre; i++) _comportement.Journal.AddRange(tours[i].Journal);
        _comportement.Reprendre(_humains, _secondes);
        for (var i = 0; i < nombre; i++) tours[i].Appliquer(_humains);
    }

    /// <summary>
    /// Le dernier humain est mort : le journal le note comme un tournant du monde, en citant cette dernière mort pour cause,
    /// et le monde se met en pause. La nature continue si l'observateur relance le temps.
    /// </summary>
    private void Eteindre()
    {
        _eteint = true;
        var dernier = _humains.OrderBy(h => h.MortLe).ThenBy(h => h.Id).Last();
        var nes = _humains.Count;
        var texte = new Recit("extinction.annonce", ("nom", dernier), ("g", dernier), ("duree", Echelle.DureeR(_secondes)), ("n", nes));
        _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Mort, dernier.Id, 0, texte)
        {
            Id = _numeros.Suivant(), CauseId = dernier.MortEvenementId, Significatif = true,
        });
        _extinction = texte.Texte;
        _pauseExtinction = true;
    }

    /// <summary>Plus aucun humain ne vit dans ce monde.</summary>
    public bool Eteint
    {
        get { lock (_verrou) return _eteint; }
    }

    /// <summary>Annonce de l'extinction, rendue une seule fois.</summary>
    public string? PrendreExtinction()
    {
        lock (_verrou)
        {
            var texte = _extinction;
            _extinction = null;
            return texte;
        }
    }

    /// <summary>
    /// Tous les événements sont numérotés dès leur création : versés dans l'ordre des numéros, le journal suit l'ordre des faits.
    /// </summary>
    private void VerserJournal()
    {
        if (!_eteint && _humains.Count > 0 && !_humains.Vivants().Any(h => h.EstVivant)) Eteindre();
        foreach (var mort in _comportement.Deces) _social.Endeuiller(mort, _humains, _secondes);
        _comportement.Deces.Clear();
        foreach (var e in _comportement.Journal.Concat(_social.Journal).Concat(_catastrophes.Journal).OrderBy(e => e.Id)) Ajouter(e);
        _comportement.Journal.Clear();
        _social.Journal.Clear();
        _catastrophes.Journal.Clear();
        if (_catastrophes.Alertes.Any(a => a.Grave)) _pauseDemandee = true;
        _alertes.AddRange(_catastrophes.Alertes);
        _catastrophes.Alertes.Clear();
    }

    /// <summary>
    /// Premier jour de l'année : les pluies de chaque région sont tirées. Les sécheresses et les années d'abondance
    /// des régions où vivent des humains sont notées au journal (une ligne par région touchée).
    /// </summary>
    private void NouvelleAnnee()
    {
        var avant = _ecosysteme.Pluies.ToArray();
        _ecosysteme.NouvelleAnnee(_alea);
        var habitees = _humains.Vivants().Where(h => h.EstVivant).GroupBy(h => _ecosysteme.Region(h.X, h.Y))
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).First());
        var dejaDits = new HashSet<string>();
        foreach (var (r, temoin) in habitees.OrderBy(p => p.Key))
        {
            var pluie = _ecosysteme.Pluies[r];
            var ou = Lieux.Direction(Carte, temoin.X, temoin.Y);
            string? texte = pluie switch
            {
                < 0.7 when avant[r] < 0.7 => $"La sécheresse se prolonge {ou} du continent : les pluies restent rares ({pluie:P0} de la normale).",
                < 0.7 => $"Une sécheresse frappe les terres {ou} du continent : il ne tombe que {pluie:P0} des pluies habituelles.",
                > 1.35 => $"Une année très arrosée {ou} du continent : les plantes et le gibier abondent.",
                _ when avant[r] < 0.7 => $"Les pluies reviennent {ou} du continent.",
                _ => null,
            };
            // Deux régions dans la même direction donneraient deux fois la même ligne.
            if (texte is not null && dejaDits.Add(texte)) Ajouter(new EvenementJournal(_secondes, TypeEvenement.Nature, 0, 0, texte));
        }
    }

    /// <summary>
    /// À chaque anniversaire, le caractère mûrit d'une année (voir <see cref="Psyche.Murir"/>) : le changement est lent et
    /// n'est pas noté au journal jour après jour ; la fiche montre les traits d'aujourd'hui.
    /// </summary>
    private void Murir()
    {
        foreach (var h in _humains.Vivants())
        {
            if (!h.EstVivant) continue;
            // Ce que cache un psychopathe, lui le sait : il le garde pour lui.
            if (Psyche.EstPsychopathe(h, _secondes) && !h.Memoire.Jardin.Exists(x => x.Cle == "masque"))
            {
                Jardin.Confier(h, TypeIntime.ImageDeSoi, 0,
                    "Les autres sont des outils. Je leur montre le visage qu'ils veulent voir ; leurs peines ne me touchent pas.",
                    60, 0, _secondes, "masque");
            }
            var age = h.AgeEnAnnees(_secondes);
            var hier = h.AgeEnAnnees(_secondes - Calendrier.SecondesParJour);
            if (Math.Floor(age) == Math.Floor(hier) || age < 1) continue;
            h.Personnalite = Psyche.Murir(h.Personnalite, Math.Floor(hier), _alea);
        }
    }

    /// <summary>Une fois par jour, chacun peut oublier des lieux, plus ou moins vite selon sa mémoire.</summary>
    private void Oublier()
    {
        foreach (var h in _humains.Vivants())
        {
            if (!h.EstVivant || h.EstBebe(_secondes)) continue;
            foreach (var l in h.Memoire.OublierAvecLeTemps(h.Personnalite.Intelligence.Memoire, _alea))
            {
                var quoi = l.Type switch
                {
                    TypeLieu.Eau => "eau",
                    TypeLieu.Nourriture => "nourriture",
                    _ => "abri",
                };
                object fois = l.Rappels > 0 ? new Recit("j.oubli.fois", ("h", h), ("n", l.Rappels + 1)) : "";
                _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Souvenir, h.Id, 0,
                    new Recit("j.oubli", ("h", h), ("quoi", new Recit($"j.oubli.{quoi}")), ("x", l.X), ("y", l.Y), ("fois", fois)))
                {
                    Id = _numeros.Suivant(),
                });
            }
        }
    }

    private static string Pronom(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "il";

    /// <summary>Effets que des vivants savent obtenir : le savoir le plus sûr de chacun, et tous ceux qui le détiennent.</summary>
    private Dictionary<Effet, (Savoir Savoir, List<int> Detenteurs)> EffetsConnus()
    {
        var connus = new Dictionary<Effet, (Savoir Savoir, List<int> Detenteurs)>();
        foreach (var h in _humains.Vivants())
        {
            if (!h.EstVivant) continue;
            foreach (var s in h.Savoirs)
            {
                if (s.Espece != 0 || s.Effet == Effet.Rien) continue;
                if (!connus.TryGetValue(s.Effet, out var x)) connus[s.Effet] = (s, [h.Id]);
                else
                {
                    if (x.Detenteurs[^1] != h.Id) x.Detenteurs.Add(h.Id);
                    if (s.Confiance > x.Savoir.Confiance) connus[s.Effet] = (s, x.Detenteurs);
                }
            }
        }
        return connus;
    }

    /// <summary>
    /// Une fois l'an, au premier jour : combien de savoirs sont vivants (sus d'au moins un vivant) dans le monde et dans
    /// chaque communauté. C'est la courbe du progrès de la fenêtre « Découvertes ».
    /// </summary>
    private void MesurerSavoirs(Dictionary<Effet, (Savoir Savoir, List<int> Detenteurs)> connus)
    {
        var annee = (int)(_secondes / Calendrier.SecondesParAn + 1);
        if (_progres.Count > 0 && _progres[^1].Annee >= annee) return;
        foreach (var (communaute, n) in SavoirsVivants(connus)) _progres.Add(new MesureSavoirs(annee, communaute, n));
    }

    /// <summary>Savoirs vivants (effets distincts) du monde (communauté 0) puis de chaque communauté qui a des membres.</summary>
    private List<(int Communaute, int Savoirs)> SavoirsVivants(Dictionary<Effet, (Savoir Savoir, List<int> Detenteurs)> connus)
    {
        var resultat = new List<(int, int)> { (0, connus.Count) };
        var parCommunaute = new SortedDictionary<int, HashSet<Effet>>();
        foreach (var (effet, (_, detenteurs)) in connus)
        {
            foreach (var id in detenteurs)
            {
                var c = _humains[id - 1].CommunauteId;
                if (c <= 0) continue;
                if (!parCommunaute.TryGetValue(c, out var effets)) parCommunaute[c] = effets = [];
                effets.Add(effet);
            }
        }
        foreach (var (c, effets) in parCommunaute) resultat.Add((c, effets.Count));
        return resultat;
    }

    /// <summary>Savoirs vivants en ce moment, pour le dernier point de la courbe (communauté 0 : le monde entier).</summary>
    public int SavoirsVivantsMaintenant(int communaute)
    {
        lock (_verrou)
        {
            foreach (var (c, n) in SavoirsVivants(EffetsConnus())) if (c == communaute) return n;
            return 0;
        }
    }

    /// <summary>Mesures annuelles des savoirs vivants (copie).</summary>
    public List<MesureSavoirs> Progres()
    {
        lock (_verrou) return _progres.ToList();
    }

    /// <summary>
    /// Pourquoi un savoir s'est perdu : ce qui est arrivé aux derniers qui le détenaient (morts, ou oublié faute de s'en
    /// servir). Renvoie la phrase et l'événement à citer comme cause (la mort ou l'oubli du dernier).
    /// </summary>
    private (string Texte, long Cause) PourquoiPerdu(Effet effet, List<int> detenteurs, Dictionary<(int, Effet), long> oublis)
    {
        var faits = new List<(Humain Humain, string Sort)>();
        long cause = 0;
        foreach (var id in detenteurs.Take(3))
        {
            var h = _humains[id - 1];
            var e = h.Sexe == Sexe.Femme ? "e" : "";
            if (!h.EstVivant)
            {
                var comment = Libelles.Cause(h.CauseDeMort);
                faits.Add((h, $"est mort{e}{(comment.Length > 0 ? " " + comment : "")}"));
                if (h.MortEvenementId > 0) cause = h.MortEvenementId;
            }
            else
            {
                faits.Add((h, "l'a oublié faute de s'en servir"));
                if (oublis.TryGetValue((id, effet), out var o) && o > 0) cause = o;
            }
        }
        if (faits.Count == 0) return ("", 0);
        if (detenteurs.Count == 1)
        {
            var (h, sort) = faits[0];
            return ($" {h.Nom}, {(h.Sexe == Sexe.Femme ? "la dernière" : "le dernier")} qui savait, {sort}.", cause);
        }
        return ($" Les derniers qui savaient : {string.Join(" ; ", faits.Select(f => $"{f.Humain.Nom} {f.Sort}"))}{(detenteurs.Count > 3 ? "…" : "")}.", cause);
    }

    /// <summary>
    /// Une fois par jour : les huttes s'affaissent, la pluie éteint les feux, les remèdes font leurs preuves (ou non),
    /// les vêtements s'usent, les savoirs inemployés s'oublient. Quand plus personne ne sait obtenir un effet, le savoir
    /// est perdu pour tous : c'est un tournant (★).
    /// </summary>
    private void Savoirs()
    {
        _installations.Affaisser(1, id => 1 / (90 + id * 7919 % 91.0));
        // La pluie éteint un feu qu'aucune hutte ne protège, et noie la braise ; sous une épaisse couche de cendre, elle résiste mieux.
        foreach (var f in _installations.Foyers.ToList())
        {
            if (_installations.HutteEn(f.X, f.Y) is not null) continue;
            if (_alea.Reel() < 0.12 * _ecosysteme.PluieEn(Carte.Indice(f.X, f.Y))) _installations.Eteindre(f.X, f.Y);
        }
        foreach (var b in _installations.Braises.ToList())
        {
            if (_installations.HutteEn(b.X, b.Y) is not null) continue;
            if (_alea.Reel() < (b.Couvert ? 0.06 : 0.12) * _ecosysteme.PluieEn(Carte.Indice(b.X, b.Y))) _installations.EteindreBraise(b.X, b.Y);
        }
        _comportement.BilanDesRemedes(_humains, _secondes);
        _comportement.Hygienes(_humains, _secondes);
        var oublis = new Dictionary<(int, Effet), long>();
        foreach (var (h, s) in _comportement.UserEtOublier(_humains, _secondes))
        {
            if (s.Effet is Effet.Chaleur or Effet.Tiedeur || s.Confiance < 0.5) continue;
            var plante = s.Espece > 0 ? Flore.Get(s.Espece).Nom : null;
            var oubli = new EvenementJournal(_secondes, TypeEvenement.Souvenir, h.Id, 0,
                $"{h.Nom} ne se souvient plus de ce qu'{Pronom(h)} savait : « {Physique.Decrire(s, plante)} »") { Id = _numeros.Suivant(), CauseId = s.EvenementId };
            _comportement.Journal.Add(oubli);
            if (s.Espece == 0) oublis[(h.Id, s.Effet)] = oubli.Id;
        }
        var connus = EffetsConnus();
        foreach (var (perdu, (dernier, detenteurs)) in _effetsConnus.Where(p => !connus.ContainsKey(p.Key)).OrderBy(p => p.Key))
        {
            var premier = _installations.Decouvertes.Find(d => d.Effet == perdu);
            var (pourquoi, cause) = PourquoiPerdu(perdu, detenteurs, oublis);
            _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Decouverte, 0, 0,
                $"Plus personne ne sait : « {Physique.Decrire(dernier)} » Ce savoir est perdu.{pourquoi}")
            {
                Id = _numeros.Suivant(), CauseId = cause > 0 ? cause : premier?.EvenementId ?? 0, Significatif = true,
            });
        }
        _effetsConnus = connus;
        MesurerSavoirs(connus);
    }

    /// <summary>
    /// L'Entité souffle une intuition à un humain : il fait aussitôt le geste suggéré, avec ce qu'il a sous la main.
    /// Renvoie l'effet obtenu (Rien si le geste n'a rien donné, ou si l'humain n'existe pas ou n'a pas les matières).
    /// </summary>
    public Effet Souffler(int humainId, Geste geste, Matiere a, Matiere b, int espece = 0)
    {
        lock (_verrou)
        {
            if (humainId < 1 || humainId > _humains.Count || !_humains[humainId - 1].EstVivant) return Effet.Rien;
            var effet = _comportement.Souffler(_humains[humainId - 1], geste, a, b, _humains, _secondes, espece);
            VerserJournal();
            return effet;
        }
    }

    /// <summary>
    /// L'Entité fait comprendre une parole à des humains (un seul, plusieurs, ou tous les membres de communautés) :
    /// chacun comprend, croit, obéit ou refuse à sa manière. <paramref name="cibleId"/> : la personne désignée (0 : aucune) ;
    /// <paramref name="param"/> : direction, plante ou communauté. Renvoie, pour chacun, ce qu'il en a fait.
    /// </summary>
    public List<(int Humain, string Resultat)> Parler(IReadOnlyCollection<int> humainsIds, IReadOnlyCollection<int> communautes, Parole parole,
        ModeParole mode, int cibleId, int param)
    {
        lock (_verrou)
        {
            var ids = humainsIds.Where(id => id >= 1 && id <= _humains.Count).ToHashSet();
            foreach (var c in communautes)
                foreach (var h in _humains)
                    if (h.EstVivant && h.CommunauteId == c && !h.EstBebe(_secondes)) ids.Add(h.Id);
            var cible = cibleId >= 1 && cibleId <= _humains.Count ? _humains[cibleId - 1] : null;
            var resultats = new List<(int, string)>();
            foreach (var id in ids.Order())
            {
                var h = _humains[id - 1];
                if (!h.EstVivant || cible?.Id == h.Id) continue;
                resultats.Add((id, _comportement.Parler(h, parole, mode, cible, param, _humains, _secondes, ensemble: ids.Count > 1)));
            }
            VerserJournal();
            return resultats;
        }
    }

    /// <summary>
    /// L'Entité inspire un penchant à un humain (ou à chaque adulte de communautés) : pour <paramref name="cibleId"/>, ou pour
    /// celui que le monde choisit (0). <paramref name="mutuel"/> : l'autre le ressent aussi (coup de foudre).
    /// </summary>
    public List<(int Humain, string Resultat)> Inspirer(IReadOnlyCollection<int> humainsIds, IReadOnlyCollection<int> communautes, int cibleId,
        bool mutuel, bool passion, bool orientation)
    {
        lock (_verrou)
        {
            var ids = humainsIds.Where(id => id >= 1 && id <= _humains.Count).ToHashSet();
            foreach (var c in communautes)
                foreach (var h in _humains)
                    if (h.EstVivant && h.CommunauteId == c && !h.EstEnfant(_secondes)) ids.Add(h.Id);
            var cible = cibleId >= 1 && cibleId <= _humains.Count ? _humains[cibleId - 1] : null;
            var resultats = new List<(int, string)>();
            foreach (var id in ids.Order())
            {
                var h = _humains[id - 1];
                if (cible?.Id == h.Id) continue;
                var avant = h.Impulsions.LastOrDefault();
                resultats.Add((id, _comportement.Inspirer(h, cible, passion, orientation, _humains, _secondes)));
                // Coup de foudre : l'autre ressent le même penchant en retour.
                if (mutuel && h.Impulsions.LastOrDefault() is { Parole: Parole.Penchant, Reponse: Reponse.Ressenti } i && !ReferenceEquals(i, avant))
                    resultats.Add((i.CibleId, _comportement.Inspirer(_humains[i.CibleId - 1], h, passion, orientation, _humains, _secondes)));
            }
            VerserJournal();
            return resultats;
        }
    }

    /// <summary>Feux, huttes et chronologie des découvertes (copies).</summary>
    public (List<Foyer> Foyers, List<Hutte> Huttes, List<Decouverte> Decouvertes) Installations()
    {
        lock (_verrou) return (_installations.Foyers.ToList(), _installations.Huttes.ToList(), _installations.Decouvertes.ToList());
    }

    /// <summary>Braises qui couvent et réserves d'eau et de nourriture (copies).</summary>
    public (List<Braise> Braises, List<Reserve> Reserves) BraisesEtReserves()
    {
        lock (_verrou) return (_installations.Braises.ToList(), _installations.Reserves.ToList());
    }

    /// <summary>
    /// L'Entité exauce une prière en attente. Renvoie, en mots, ce qui s'est passé, ou pourquoi elle ne peut l'être.
    /// </summary>
    public string Exaucer(int priereId)
    {
        lock (_verrou)
        {
            var texte = _comportement.Exaucer(priereId, _humains, _secondes);
            VerserJournal();
            return texte;
        }
    }

    /// <summary>
    /// L'Entité refuse une prière en attente, en secret ou par un signe. Renvoie, en mots, ce qui s'est passé.
    /// </summary>
    public string Refuser(int priereId, bool signe)
    {
        lock (_verrou)
        {
            var texte = _comportement.Refuser(priereId, signe, _humains, _secondes);
            VerserJournal();
            return texte;
        }
    }

    /// <summary>
    /// L'Entité fait un geste sur un humain (<see cref="Gestes.SurHumain"/>) ou sur une communauté. Les bornes de durée et de
    /// force sont imposées. Renvoie, en mots, ce qui s'est passé, ou pourquoi le geste ne peut avoir lieu.
    /// </summary>
    public string Agir(GesteEntite geste, int cibleId, ManiereGeste maniere, int param, double force, double jours)
    {
        if (!Enum.IsDefined(geste) || !Enum.IsDefined(maniere)) return "Geste inconnu.";
        lock (_verrou)
        {
            string texte;
            if (Gestes.SurHumain(geste))
            {
                if (cibleId < 1 || cibleId > _humains.Count) return "Cet humain n'existe pas.";
                texte = _comportement.AgirSur(_humains[cibleId - 1], geste, maniere, param, force, jours, _humains, _secondes);
            }
            else
            {
                if (cibleId < 1 || cibleId > _communautes.Count) return "Cette communauté n'existe pas.";
                texte = _comportement.AgirSur(_communautes[cibleId - 1], geste, maniere, param, force, jours, _humains, _secondes);
            }
            VerserJournal();
            return texte;
        }
    }

    /// <summary>Gestes de l'Entité qui durent encore, et souvenirs des communautés (copies).</summary>
    public (List<EffetEntite> Effets, List<SouvenirEntite> Souvenirs) GestesEnCours()
    {
        lock (_verrou) return (_cultes.Effets.Where(e => e.Fin > _secondes).ToList(), _cultes.Souvenirs.ToList());
    }

    /// <summary>Prières en mémoire : en attente, et celles de l'année écoulée (copies).</summary>
    public List<Priere> Prieres()
    {
        lock (_verrou) return _cultes.Prieres.Select(p => p.Copier()).ToList();
    }

    /// <summary>Religions, lieux sacrés et malheurs annoncés (copies).</summary>
    public (List<Religion> Religions, List<LieuSacre> Lieux, List<Prophetie> Propheties) Religions()
    {
        lock (_verrou) return (_cultes.Religions.Select(r => r.Copier()).ToList(), _cultes.Lieux.ToList(), _cultes.Propheties.ToList());
    }

    /// <summary>Images peintes sur la roche (copie).</summary>
    public List<Oeuvre> Oeuvres()
    {
        lock (_verrou) return _installations.Oeuvres.ToList();
    }
}
