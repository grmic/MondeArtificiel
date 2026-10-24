using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Serveur;

/// <summary>
/// Détient le monde courant et sa sauvegarde. Au démarrage, le monde reprend à la date enregistrée,
/// en pause par défaut, ou à sa dernière vitesse si ce réglage est choisi.
/// </summary>
public sealed class ServiceMonde
{
    private readonly Lock _verrou = new();
    private readonly ILogger<ServiceMonde> _journal;
    private const string CleRedemarrage = "redemarrage";
    private const string ClePauseCatastrophe = "pause_catastrophe";
    private const string CleJournalQuotidien = "journal_quotidien";
    private const string CleCoeurs = "coeurs";
    private const string CleClemence = "clemence";
    private volatile MondeSimule _monde;
    private volatile bool _reprendreVitesse;
    private volatile bool _pauseSurCatastrophe;
    private volatile bool _journalQuotidien;
    private volatile int _coeurs;
    private volatile int _clemence;
    private double _facteurAtteint;

    public ServiceMonde(IConfiguration configuration, ILogger<ServiceMonde> journal)
    {
        _journal = journal;
        var dossier = configuration["Monde:Dossier"];
        if (string.IsNullOrWhiteSpace(dossier))
        {
            // Dossier de données propre à chaque OS (macOS : ~/Library/Application Support).
            dossier = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                "MondeArtificiel");
        }

        Depot = new DepotMonde(Path.Combine(dossier, "mondes", "monde.db"));
        var etat = Depot.Charger();
        if (etat is null)
        {
            var graine = configuration.GetValue<uint>("Monde:Graine", 42);
            etat = MondeSimule.Nouveau(ConfigurationMonde.Nouvelle(graine));
            journal.LogInformation("Nouveau monde créé (graine {Graine}) dans {Fichier}", graine, Depot.CheminFichier);
        }
        else
        {
            journal.LogInformation("Monde repris dans {Fichier}", Depot.CheminFichier);
        }

        var carte = GenerateurContinent.Generer(etat.Configuration ?? ConfigurationMonde.ParDefaut(etat.Graine));
        _reprendreVitesse = Depot.LireReglage(CleRedemarrage) == "vitesse";
        _pauseSurCatastrophe = Depot.LireReglage(ClePauseCatastrophe) == "oui";
        _journalQuotidien = Depot.LireReglage(CleJournalQuotidien) == "oui";
        // Un cœur par défaut (le plus rapide mesuré sur une partie de 400 habitants) ; une valeur illisible ou hors limites aussi.
        _coeurs = int.TryParse(Depot.LireReglage(CleCoeurs), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var c)
                  && c <= CoeursMachine ? c : 1;
        // Clémence du monde : 0 (le monde tel quel) par défaut, et pour une valeur illisible ou hors limites.
        _clemence = int.TryParse(Depot.LireReglage(CleClemence), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cl)
                    && cl <= 100 ? cl : 0;
        _monde = new MondeSimule(carte, _reprendreVitesse ? etat : etat with { Facteur = 0 })
        {
            PauseSurCatastrophe = _pauseSurCatastrophe, JournalQuotidien = _journalQuotidien, Coeurs = _coeurs, Clemence = _clemence,
        };
        // Première sauvegarde complète : les humains d'un monde nouveau (ou d'une ancienne version) sont enregistrés.
        Sauvegarder();
    }

    /// <summary>Monde courant. Il peut être remplacé par une réinitialisation : relire la propriété à chaque usage.</summary>
    public MondeSimule Monde => _monde;

    public DepotMonde Depot { get; }

    /// <summary>Au redémarrage du serveur : vrai = reprendre à la dernière vitesse, faux = en pause.</summary>
    public bool ReprendreVitesse
    {
        get => _reprendreVitesse;
        set
        {
            Depot.EcrireReglage(CleRedemarrage, value ? "vitesse" : "pause");
            _reprendreVitesse = value;
        }
    }

    /// <summary>Mettre le monde en pause quand une catastrophe menace des humains (non par défaut depuis la 0.29.3).</summary>
    public bool PauseSurCatastrophe
    {
        get => _pauseSurCatastrophe;
        set
        {
            Depot.EcrireReglage(ClePauseCatastrophe, value ? "oui" : "non");
            _pauseSurCatastrophe = value;
            _monde.PauseSurCatastrophe = value;
        }
    }

    /// <summary>Noter au journal la vie quotidienne de chacun (non par défaut).</summary>
    public bool JournalQuotidien
    {
        get => _journalQuotidien;
        set
        {
            Depot.EcrireReglage(CleJournalQuotidien, value ? "oui" : "non");
            _journalQuotidien = value;
            _monde.JournalQuotidien = value;
        }
    }

    /// <summary>Cœurs logiques de cette machine.</summary>
    public static int CoeursMachine => Environment.ProcessorCount;

    /// <summary>
    /// Cœurs donnés au calcul : 1 = pas classique, l'un après l'autre (par défaut) ; 0 = tous ; 2 ou plus = pas simultané sur ce nombre.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Hors de 0 à <see cref="CoeursMachine"/>.</exception>
    public int Coeurs
    {
        get => _coeurs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, CoeursMachine);
            Depot.EcrireReglage(CleCoeurs, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _coeurs = value;
            _monde.Coeurs = value;
        }
    }

    /// <summary>
    /// Clémence du monde, de 0 à 100 % : la part des risques de maladie, d'infection, de mort en couches, de mort des tout-petits
    /// et de mort dans les catastrophes que l'Entité écarte. Gardée en base ; un changement est noté au journal du monde.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Hors de 0 à 100.</exception>
    public int Clemence
    {
        get => _clemence;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            Depot.EcrireReglage(CleClemence, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _clemence = value;
            _monde.ChoisirClemence(value);
        }
    }

    /// <summary>Vitesse réellement atteinte, mesurée par le moteur (secondes du monde par seconde réelle).</summary>
    public double FacteurAtteint
    {
        get => Volatile.Read(ref _facteurAtteint);
        set => Volatile.Write(ref _facteurAtteint, value);
    }

    /// <summary>Fichier où l'ancien monde est copié juste avant une réinitialisation.</summary>
    public string CheminCopieAvantReinitialisation =>
        Path.Combine(Path.GetDirectoryName(Depot.CheminFichier)!, "monde-avant-reinitialisation.db");

    public void Sauvegarder()
    {
        lock (_verrou)
        {
            var monde = _monde;
            var etat = monde.Etat;
            Depot.Enregistrer(etat);
            // Les catastrophes terminées et enregistrées quittent la mémoire du monde (elles restent dans la base).
            var terminees = (etat.Catastrophes ?? []).Where(c => !c.Active).Select(c => c.Id).ToHashSet();
            monde.MarquerJournalEnregistre(etat.JournalNonEnregistre?.Count ?? 0, etat.ArchivesNonEnregistrees?.Count ?? 0, terminees);
        }
    }

    /// <summary>Remplace le monde par un monde neuf avec une autre graine et les mêmes réglages.</summary>
    public MondeSimule Reinitialiser(uint graine) => Reinitialiser(_monde.Configuration with { Graine = graine });

    /// <summary>
    /// Remplace le monde par un monde neuf (an 1, deux humains par couple de départ), en pause, avec ses réglages.
    /// L'ancien monde est d'abord enregistré puis gardé (fichier renommé) dans <see cref="CheminCopieAvantReinitialisation"/> (seule la dernière copie est gardée).
    /// </summary>
    /// <exception cref="InvalidOperationException">Les réglages ne laissent pas la place aux couples : rien n'est changé.</exception>
    public MondeSimule Reinitialiser(ConfigurationMonde configuration)
    {
        if (configuration.Erreur() is { } erreur) throw new ArgumentException(erreur, nameof(configuration));
        // La carte est générée avant de toucher à l'ancien monde : un réglage impossible ne détruit rien.
        var etat = MondeSimule.Nouveau(configuration);
        var nouveau = new MondeSimule(GenerateurContinent.Generer(configuration), etat)
        {
            PauseSurCatastrophe = _pauseSurCatastrophe, JournalQuotidien = _journalQuotidien, Coeurs = _coeurs, Clemence = _clemence,
        };
        lock (_verrou)
        {
            Sauvegarder();
            Depot.RecommencerApresCopie(CheminCopieAvantReinitialisation, nouveau.Etat);
            _monde = nouveau;
        }
        _journal.LogInformation("Monde réinitialisé ({Configuration}) ; ancien monde copié dans {Copie}",
            configuration, CheminCopieAvantReinitialisation);
        return nouveau;
    }

    /// <summary>Dossier des sauvegardes faites à la demande (à côté du monde).</summary>
    public string DossierSauvegardes => Path.Combine(Path.GetDirectoryName(Depot.CheminFichier)!, "sauvegardes");

    /// <summary>
    /// Fichiers que l'on peut restaurer : les sauvegardes faites à la demande (et avant chaque restauration), plus la copie
    /// gardée avant la dernière création d'un monde. Clé : le nom affiché et demandé par le navigateur.
    /// </summary>
    public Dictionary<string, string> FichiersRestaurables()
    {
        var fichiers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(DossierSauvegardes))
        {
            foreach (var f in Directory.EnumerateFiles(DossierSauvegardes, "*.db")) fichiers[Path.GetFileName(f)] = f;
        }
        if (File.Exists(CheminCopieAvantReinitialisation)) fichiers[Path.GetFileName(CheminCopieAvantReinitialisation)] = CheminCopieAvantReinitialisation;
        return fichiers;
    }

    /// <summary>Copie cohérente du monde courant dans le dossier des sauvegardes ; rend le chemin du fichier créé.</summary>
    public string SauvegarderMaintenant(string prefixe = "sauvegarde")
    {
        lock (_verrou)
        {
            Sauvegarder();
            Directory.CreateDirectory(DossierSauvegardes);
            var date = Calendrier.Decomposer(_monde.Secondes);
            var nom = $"{prefixe}-an{date.Annee}-{DateTime.Now:yyyyMMdd-HHmmss}";
            var chemin = Path.Combine(DossierSauvegardes, nom + ".db");
            for (var k = 2; File.Exists(chemin); k++) chemin = Path.Combine(DossierSauvegardes, $"{nom}-{k}.db");
            Depot.CopierVers(chemin);
            _journal.LogInformation("Monde sauvegardé dans {Fichier}", chemin);
            return chemin;
        }
    }

    /// <summary>
    /// Met une sauvegarde (nom tiré de la liste) à la corbeille du système, d'où l'on peut encore la reprendre ; rend le nom
    /// de la corbeille. Le monde en cours n'est jamais dans la liste : il ne peut pas être supprimé.
    /// </summary>
    /// <exception cref="ArgumentException">Ce nom n'est pas celui d'une sauvegarde.</exception>
    /// <exception cref="IOException">Le système a refusé : la sauvegarde est toujours là.</exception>
    public string MettreALaCorbeille(string nom)
    {
        // Seuls les noms de la liste sont acceptés : aucun chemin venu du navigateur n'est utilisé tel quel.
        if (!FichiersRestaurables().TryGetValue(nom, out var chemin)) throw new ArgumentException(L.T("sauvegarde.inexistante"), nameof(nom));
        lock (_verrou)
        {
            try
            {
                var corbeille = Corbeille.Jeter(chemin);
                _journal.LogInformation("Sauvegarde {Fichier} mise à {Corbeille}", chemin, corbeille);
                return corbeille;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _journal.LogWarning(e, "Sauvegarde {Fichier} impossible à mettre à la corbeille", chemin);
                throw;
            }
        }
    }

    /// <summary>
    /// Remplace le monde courant par une sauvegarde, en pause. Le monde courant est d'abord sauvegardé
    /// (« avant-restauration-… » dans le dossier des sauvegardes). La sauvegarde restaurée n'est pas modifiée.
    /// </summary>
    /// <exception cref="ArgumentException">Ce nom n'est pas celui d'une sauvegarde.</exception>
    /// <exception cref="InvalidDataException">La sauvegarde est illisible ou d'une version plus récente : rien n'est changé.</exception>
    public MondeSimule Restaurer(string nom)
    {
        // Seuls les noms de la liste sont acceptés : aucun chemin venu du navigateur n'est utilisé tel quel.
        if (!FichiersRestaurables().TryGetValue(nom, out var source)) throw new ArgumentException(L.T("sauvegarde.inexistante"), nameof(nom));
        lock (_verrou)
        {
            var (prepare, etat) = Depot.PreparerRestauration(source);
            MondeSimule restaure;
            try
            {
                var configuration = etat.Configuration ?? ConfigurationMonde.ParDefaut(etat.Graine);
                restaure = new MondeSimule(GenerateurContinent.Generer(configuration), etat with { Facteur = 0 })
                {
                    PauseSurCatastrophe = _pauseSurCatastrophe, JournalQuotidien = _journalQuotidien, Coeurs = _coeurs, Clemence = _clemence,
                };
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                File.Delete(prepare);
                _journal.LogWarning(e, "Sauvegarde {Source} impossible à restaurer", source);
                throw new InvalidDataException(L.T("sauvegarde.impossible"), e);
            }
            var copie = SauvegarderMaintenant("avant-restauration");
            Depot.RemplacerPar(prepare);
            _monde = restaure;
            _journal.LogInformation("Sauvegarde {Source} restaurée ; l'ancien monde est dans {Copie}", source, copie);
            return restaure;
        }
    }
}
