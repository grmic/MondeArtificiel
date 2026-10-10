using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Monde.Serveur.Hubs;

namespace Monde.Serveur;

/// <summary>
/// Boucle de fond qui fait avancer le monde, diffuse l'horloge aux navigateurs
/// et sauvegarde régulièrement l'état.
/// </summary>
public sealed class MoteurSimulation(
    ServiceMonde service,
    IHubContext<MondeHub> hub,
    ILogger<MoteurSimulation> journal) : BackgroundService
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan IntervalleDiffusion = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IntervalleSauvegarde = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Cadence);
        var chrono = Stopwatch.StartNew();
        var precedent = chrono.Elapsed;
        var derniereDiffusion = TimeSpan.Zero;
        var derniereSauvegarde = TimeSpan.Zero;

        try
        {
            while (await minuteur.WaitForNextTickAsync(arret))
            {
                var maintenant = chrono.Elapsed;
                service.Monde.Avancer(maintenant - precedent);
                precedent = maintenant;

                if (maintenant - derniereDiffusion >= IntervalleDiffusion)
                {
                    derniereDiffusion = maintenant;
                    await hub.Clients.All.SendAsync("Horloge", HorlogeDto.Depuis(service.Monde), arret);
                }

                if (maintenant - derniereSauvegarde >= IntervalleSauvegarde)
                {
                    derniereSauvegarde = maintenant;
                    service.Sauvegarder();
                }
            }
        }
        catch (OperationCanceledException) when (arret.IsCancellationRequested)
        {
            // Arrêt normal du serveur.
        }
        finally
        {
            service.Sauvegarder();
            journal.LogInformation("Monde sauvegardé à l'arrêt ({Date}).", service.Monde.Date);
        }
    }
}
