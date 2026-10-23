using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Monde.Serveur.Hubs;

namespace Monde.Serveur;

/// <summary>
/// Fait avancer le monde sur un fil à lui, à plein temps ; une boucle de fond diffuse l'horloge aux navigateurs
/// et sauvegarde régulièrement l'état, sans arrêter le calcul.
/// </summary>
public sealed class MoteurSimulation(
    ServiceMonde service,
    IHubContext<MondeHub> hub,
    ILogger<MoteurSimulation> journal) : BackgroundService
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan IntervalleDiffusion = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IntervalleSauvegarde = TimeSpan.FromSeconds(10);
    // Le calcul se fait par tranches : entre deux, le monde est libre pour être lu (diffusion, fiches, sauvegarde).
    private static readonly TimeSpan Tranche = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FenetreMesure = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Cadence);
        var chrono = Stopwatch.StartNew();
        using var finCalcul = CancellationTokenSource.CreateLinkedTokenSource(arret);
        var calcul = new Thread(() => Calculer(chrono, finCalcul.Token)) { IsBackground = true, Name = "Monde" };
        calcul.Start();
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
                // Une erreur du calcul arrête le serveur, comme avant (elle est transmise ici).
                _panne?.Throw();
                var maintenant = chrono.Elapsed;
                var monde = service.Monde;

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

                // Catastrophes et phénomènes : alerte aux navigateurs ; une catastrophe qui menace des humains a pu mettre le monde en pause.
                if (monde.PrendreAlertes() is { Count: > 0 } alertes)
                {
                    if (monde.Facteur == 0 && monde.Objectif is null) service.FacteurAtteint = 0;
                    if (alertes.Any(a => a.Grave)) service.Sauvegarder();
                    await hub.Clients.Diffuser("Alertes", () => alertes.Select(AlerteDto.Depuis).ToList(), arret);
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                }

                if (monde.PrendreObjectifAtteintDetaille() is { } objectif)
                {
                    service.FacteurAtteint = 0;
                    service.Sauvegarder();
                    await hub.Clients.Diffuser("ObjectifAtteint", () => Lib.Objectif(objectif), arret);
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                }

                // Le dernier humain est mort : le monde s'est mis en pause, quel que soit le réglage.
                if (monde.PrendreExtinction() is not null)
                {
                    service.FacteurAtteint = 0;
                    service.Sauvegarder();
                    await hub.Clients.Diffuser("Extinction", () => Lib.Extinction(monde), arret);
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                }

                if (maintenant - derniereDiffusion >= IntervalleDiffusion)
                {
                    derniereDiffusion = maintenant;
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                    await hub.Clients.Diffuser("Humains", () => HumainDto.Depuis(monde), arret);
                    // Nouveaux événements du journal (les 50 derniers au plus, l'historique complet reste consultable).
                    var nouveaux = monde.JournalRecent(dernierEvenement);
                    if (nouveaux.Count > 0)
                    {
                        dernierEvenement = nouveaux[^1].Id;
                        await hub.Clients.Diffuser("Journal", () => nouveaux.TakeLast(50).Select(EvenementDto.Depuis).ToList(), arret);
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
            finCalcul.Cancel();
            calcul.Join();
            service.Sauvegarder();
            journal.LogInformation("Monde sauvegardé à l'arrêt ({Date}).", service.Monde.Date);
        }
    }

    private volatile System.Runtime.ExceptionServices.ExceptionDispatchInfo? _panne;

    /// <summary>
    /// Fil du calcul : avance le monde selon le temps réel écoulé, par tranches. En retard (vitesse au-delà de ce que la
    /// machine suit), il enchaîne les tranches et ne s'interrompt qu'un instant pour laisser lire le monde ; à l'heure, il attend.
    /// </summary>
    private void Calculer(Stopwatch chrono, CancellationToken fin)
    {
        try
        {
            var precedent = chrono.Elapsed;
            while (!fin.IsCancellationRequested)
            {
                var debut = chrono.Elapsed;
                service.Monde.Avancer(debut - precedent, Tranche);
                precedent = debut;
                Thread.Sleep(chrono.Elapsed - debut >= Tranche ? 1 : 10);
            }
        }
        catch (Exception e)
        {
            journal.LogCritical(e, "Le calcul du monde s'est arrêté sur une erreur.");
            _panne = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
        }
    }
}
