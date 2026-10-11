using Monde.Simulation;
using Monde.Simulation.Monde;
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
    private volatile MondeSimule _monde;
    private volatile bool _reprendreVitesse;
    private volatile bool _pauseSurCatastrophe;
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
            etat = MondeSimule.Nouveau(graine);
            journal.LogInformation("Nouveau monde créé (graine {Graine}) dans {Fichier}", graine, Depot.CheminFichier);
        }
        else
        {
            journal.LogInformation("Monde repris dans {Fichier}", Depot.CheminFichier);
        }

        var carte = GenerateurContinent.Generer(etat.Graine);
        _reprendreVitesse = Depot.LireReglage(CleRedemarrage) == "vitesse";
        _pauseSurCatastrophe = Depot.LireReglage(ClePauseCatastrophe) != "non";
        _monde = new MondeSimule(carte, _reprendreVitesse ? etat : etat with { Facteur = 0 }) { PauseSurCatastrophe = _pauseSurCatastrophe };
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

    /// <summary>Mettre le monde en pause quand une catastrophe menace des humains (oui par défaut).</summary>
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

    /// <summary>
    /// Remplace le monde par un monde neuf (an 1, 66 humains de départ), en pause.
    /// L'ancien monde est d'abord enregistré puis gardé (fichier renommé) dans <see cref="CheminCopieAvantReinitialisation"/> (seule la dernière copie est gardée).
    /// </summary>
    public MondeSimule Reinitialiser(uint graine)
    {
        var etat = MondeSimule.Nouveau(graine);
        var nouveau = new MondeSimule(GenerateurContinent.Generer(graine), etat) { PauseSurCatastrophe = _pauseSurCatastrophe };
        lock (_verrou)
        {
            Sauvegarder();
            Depot.RecommencerApresCopie(CheminCopieAvantReinitialisation, nouveau.Etat);
            _monde = nouveau;
        }
        _journal.LogInformation("Monde réinitialisé (graine {Graine}) ; ancien monde copié dans {Copie}",
            graine, CheminCopieAvantReinitialisation);
        return nouveau;
    }
}
