using Monde.Simulation;
using Monde.Stockage;

namespace Monde.Serveur;

/// <summary>
/// Détient le monde courant et sa sauvegarde. Au démarrage, le monde reprend à la date enregistrée,
/// toujours en pause : rien ne s'écoule tant que l'on n'a pas choisi une vitesse.
/// </summary>
public sealed class ServiceMonde
{
    public ServiceMonde(IConfiguration configuration, ILogger<ServiceMonde> journal)
    {
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
            Depot.Enregistrer(etat);
            journal.LogInformation("Nouveau monde créé (graine {Graine}) dans {Fichier}", graine, Depot.CheminFichier);
        }
        else
        {
            journal.LogInformation("Monde repris dans {Fichier}", Depot.CheminFichier);
        }

        Monde = new MondeSimule(etat with { NiveauVitesse = 0 });
    }

    public MondeSimule Monde { get; }

    public DepotMonde Depot { get; }

    public void Sauvegarder() => Depot.Enregistrer(Monde.Etat);
}
