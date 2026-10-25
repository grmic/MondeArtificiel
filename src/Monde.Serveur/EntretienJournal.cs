using System.Diagnostics;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Entretien du journal en arrière-plan (0.31.4) : efface ce qui dépasse la durée de conservation choisie, range le journal
/// ancien dans des paquets compressés si la case est cochée, et mesure la taille du monde pour la Configuration.
/// Le travail se fait par petits pas, entre deux sauvegardes : le monde n'est jamais arrêté longtemps.
/// </summary>
public sealed class EntretienJournal(ServiceMonde service, ILogger<EntretienJournal> journal) : BackgroundService
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IntervalleMesure = TimeSpan.FromMinutes(10);
    // Durée d'une suite de pas sous le verrou de la base, puis une pause pour laisser passer les sauvegardes.
    private static readonly TimeSpan Rafale = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        var derniereMesure = TimeSpan.MinValue;
        var chrono = Stopwatch.StartNew();
        using var minuteur = new PeriodicTimer(Intervalle);
        do
        {
            try
            {
                if (derniereMesure == TimeSpan.MinValue || chrono.Elapsed - derniereMesure >= IntervalleMesure)
                {
                    Mesurer();
                    derniereMesure = chrono.Elapsed;
                }
                await Entretenir(arret);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Un entretien manqué n'empêche pas le monde de tourner : il sera retenté.
                journal.LogWarning(e, "Entretien du journal interrompu");
            }
        }
        while (await minuteur.WaitForNextTickAsync(arret));
    }

    private void Mesurer()
    {
        var maintenant = service.Monde.Secondes;
        var depuis = Math.Max(0, maintenant - Calendrier.SecondesParAn);
        // Lecture seule : elle ne bloque pas les sauvegardes, même longue sur un gros monde.
        var mesure = service.Depot.MesurerJournal(depuis);
        service.MesureJournal = mesure;
        service.PeriodeMesure = maintenant - depuis;
    }

    /// <summary>Efface puis archive, pas à pas, jusqu'à ce qu'il n'y ait plus rien à faire.</summary>
    private async Task Entretenir(CancellationToken arret)
    {
        long effacees = 0, rangees = 0;
        var debut = Stopwatch.StartNew();
        while (!arret.IsCancellationRequested)
        {
            var reglages = service.ReglagesJournal;
            if (!reglages.Regles(0).Active && !reglages.Compresser) break;
            var fini = false;
            var rafale = Stopwatch.StartNew();
            service.SurLaBase((depot, monde) =>
            {
                while (rafale.Elapsed < Rafale)
                {
                    var maintenant = monde.Secondes;
                    var (n, pas) = depot.PurgerJournal(reglages.Regles(maintenant));
                    effacees += n;
                    if (pas) continue;
                    var r = reglages.Compresser ? depot.ArchiverJournal(maintenant - reglages.CompresserAns * Calendrier.SecondesParAn) : 0;
                    rangees += r;
                    if (r == 0)
                    {
                        fini = true;
                        break;
                    }
                }
                return 0;
            });
            if (fini) break;
            await Task.Delay(Pause, arret);
        }
        if (effacees + rangees > 0)
        {
            journal.LogInformation("Journal entretenu en {Duree} : {Effacees} lignes effacées, {Rangees} rangées dans les archives compressées",
                debut.Elapsed, effacees, rangees);
        }
    }
}
