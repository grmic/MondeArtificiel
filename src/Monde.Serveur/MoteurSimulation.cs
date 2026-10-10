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
    // Temps de calcul accordé au monde à chaque tour : le reste sert à diffuser et à répondre aux navigateurs.
    private static readonly TimeSpan BudgetCalcul = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan FenetreMesure = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Cadence);
        var chrono = Stopwatch.StartNew();
        var precedent = chrono.Elapsed;
        var derniereDiffusion = TimeSpan.Zero;
        var derniereSauvegarde = TimeSpan.Zero;
        var debutMesure = TimeSpan.Zero;
        var mondeMesure = service.Monde;
        var secondesMesure = mondeMesure.Secondes;
        long dernierEvenement = mondeMesure.JournalRecent().LastOrDefault()?.Id ?? 0;

        try
        {
            while (await minuteur.WaitForNextTickAsync(arret))
            {
                var maintenant = chrono.Elapsed;
                var monde = service.Monde;
                monde.Avancer(maintenant - precedent, BudgetCalcul);
                precedent = maintenant;

                // Vitesse réellement atteinte, mesurée sur environ une seconde.
                if (!ReferenceEquals(monde, mondeMesure))
                {
                    (mondeMesure, secondesMesure, debutMesure) = (monde, monde.Secondes, maintenant);
                    dernierEvenement = 0; // monde réinitialisé : son journal repart de zéro
                }
                else if (maintenant - debutMesure >= FenetreMesure)
                {
                    var secondes = monde.Secondes;
                    service.FacteurAtteint = (secondes - secondesMesure) / (maintenant - debutMesure).TotalSeconds;
                    (secondesMesure, debutMesure) = (secondes, maintenant);
                }

                if (monde.PrendreObjectifAtteint() is { } objectif)
                {
                    service.FacteurAtteint = 0;
                    service.Sauvegarder();
                    await hub.Clients.All.SendAsync("ObjectifAtteint", objectif, arret);
                    await hub.Clients.All.SendAsync("Horloge", HorlogeDto.Depuis(service), arret);
                }

                if (maintenant - derniereDiffusion >= IntervalleDiffusion)
                {
                    derniereDiffusion = maintenant;
                    await hub.Clients.All.SendAsync("Horloge", HorlogeDto.Depuis(service), arret);
                    await hub.Clients.All.SendAsync("Humains", HumainDto.Depuis(monde), arret);
                    // Nouveaux événements du journal (les 50 derniers au plus, l'historique complet reste consultable).
                    var nouveaux = monde.JournalRecent(dernierEvenement);
                    if (nouveaux.Count > 0)
                    {
                        dernierEvenement = nouveaux[^1].Id;
                        await hub.Clients.All.SendAsync("Journal", nouveaux.TakeLast(50).Select(EvenementDto.Depuis).ToList(), arret);
                    }
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
