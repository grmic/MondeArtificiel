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
    // Pendant un saut dans le futur (0.40.0) : humains et journal diffusés moins souvent, sauvegarde plus espacée, pour laisser le calcul aller.
    private static readonly TimeSpan IntervalleDiffusionSaut = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IntervalleSauvegardeSaut = TimeSpan.FromSeconds(30);
    // Le calcul se fait par tranches : entre deux, le monde est libre pour être lu (diffusion, fiches, sauvegarde).
    private static readonly TimeSpan Tranche = TimeSpan.FromMilliseconds(50);
    // Vitesse réellement atteinte (0.45.28) : moyenne glissante sur les dix dernières secondes, affichée dès qu'une seconde est mesurée.
    // Sur une seule seconde, elle sautait de ±40 % d'une lecture à l'autre (journées plus lourdes, sauvegardes), sans être fausse.
    private static readonly TimeSpan FenetreMesure = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MesureMinimale = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Cadence);
        var chrono = Stopwatch.StartNew();
        using var finCalcul = CancellationTokenSource.CreateLinkedTokenSource(arret);
        var calcul = new Thread(() => Calculer(chrono, finCalcul.Token)) { IsBackground = true, Name = "Monde" };
        calcul.Start();
        var derniereDiffusion = TimeSpan.Zero;
        var derniereHorloge = TimeSpan.Zero;
        var derniereSauvegarde = TimeSpan.Zero;
        var mondeMesure = service.Monde;
        var mesureRepart = service.MesureRepart;
        // Relevés (instant réel, secondes du monde) des dix dernières secondes.
        var releves = new Queue<(TimeSpan Reel, long Monde)>();
        var derniereVitesseNotee = TimeSpan.Zero;
        long dernierEvenement = mondeMesure.JournalRecent().LastOrDefault()?.Id ?? 0;

        try
        {
            while (await minuteur.WaitForNextTickAsync(arret))
            {
                // Une erreur du calcul arrête le serveur, comme avant (elle est transmise ici).
                _panne?.Throw();
                var maintenant = chrono.Elapsed;
                var monde = service.Monde;

                // Vitesse réellement atteinte : moyenne sur les dix dernières secondes (moins juste après un changement de vitesse).
                if (!ReferenceEquals(monde, mondeMesure))
                {
                    mondeMesure = monde;
                    releves.Clear();
                    dernierEvenement = 0; // monde réinitialisé : son journal repart de zéro
                }
                if (service.MesureRepart != mesureRepart)
                {
                    mesureRepart = service.MesureRepart;
                    releves.Clear();
                }
                var secondes = monde.SecondesSansAttendre;
                releves.Enqueue((maintenant, secondes));
                while (releves.Count > 2 && maintenant - releves.Peek().Reel > FenetreMesure) releves.Dequeue();
                var (reel0, monde0) = releves.Peek();
                if (maintenant - reel0 >= MesureMinimale)
                {
                    var facteur = (secondes - monde0) / (maintenant - reel0).TotalSeconds;
                    service.FacteurAtteint = facteur;
                    if (facteur == 0) mesureRepart = service.MesureRepart; // monde arrêté : ce 0 n'est pas une remise à zéro demandée
                    // Fenêtre pleine et calcul à pleine charge (la machine ne suit pas la vitesse demandée) : c'est la vitesse de ce réglage.
                    if (maintenant - reel0 >= FenetreMesure - Cadence && maintenant - derniereVitesseNotee >= FenetreMesure && !monde.Retenu && !monde.EnDialogue)
                    {
                        derniereVitesseNotee = maintenant;
                        var (_, _, demande, enRoute) = monde.Horloge;
                        if (facteur > 0 && (enRoute is not null || facteur < demande * 0.9)) service.NoterVitesse(facteur, monde.VivantsDuJour);
                    }
                }

                // Catastrophes et phénomènes : alerte aux navigateurs ; une catastrophe qui menace des humains a pu mettre le monde en pause.
                if (monde.PrendreAlertes() is { Count: > 0 } alertes)
                {
                    if (monde.Facteur == 0 && monde.Objectif is null) service.FacteurAtteint = 0;
                    if (alertes.Any(a => a.Grave)) service.Sauvegarder();
                    await hub.Clients.Diffuser("Alertes", () => alertes.Select(AlerteDto.Depuis).ToList(), arret);
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                }

                // Alertes des suivis ★ (0.40.0) : gardées, diffusées ; certaines sortes ont mis le monde en pause.
                if (monde.PrendreAlertesSuivis() is { Count: > 0 } alertesSuivis)
                {
                    var rendues = service.RecevoirAlertes(alertesSuivis);
                    if (rendues.Any(a => a.Pause))
                    {
                        service.FacteurAtteint = 0;
                        service.Sauvegarder();
                    }
                    var suivis = AlerteSuiviDto.HumainsSuivis(service);
                    await hub.Clients.Diffuser("AlertesSuivis", () => rendues.Select(a => AlerteSuiviDto.Depuis(a, service, suivis)).ToList(), arret);
                    await hub.Clients.All.SendAsync("AlertesNonLues", service.AlertesNonLues(), arret);
                    if (rendues.Any(a => a.Sorte == Simulation.SorteAlerte.Mort))
                        await hub.Clients.All.SendAsync("Suivis", SuiviDto.Depuis(service), arret);
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

                // Saut dans le futur fini (ou arrêté avant) : son bilan.
                if (monde.PrendreBilanSaut() is { } bilan)
                {
                    service.Sauvegarder();
                    await hub.Clients.Diffuser("BilanSaut", () => BilanSautDto.Depuis(bilan), arret);
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                }

                var enSaut = monde.Saut is not null;
                if (maintenant - derniereHorloge >= IntervalleDiffusion)
                {
                    derniereHorloge = maintenant;
                    await hub.Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service), arret);
                }
                if (maintenant - derniereDiffusion >= (enSaut ? IntervalleDiffusionSaut : IntervalleDiffusion))
                {
                    derniereDiffusion = maintenant;
                    await hub.Clients.Diffuser("Humains", () => HumainDto.Depuis(monde), arret);
                    // Nouveaux événements du journal (les 50 derniers au plus, l'historique complet reste consultable).
                    var nouveaux = monde.JournalRecent(dernierEvenement);
                    if (nouveaux.Count > 0)
                    {
                        dernierEvenement = nouveaux[^1].Id;
                        await hub.Clients.Diffuser("Journal", () => nouveaux.TakeLast(50).Select(EvenementDto.Depuis).ToList(), arret);
                    }
                }

                if (maintenant - derniereSauvegarde >= (enSaut ? IntervalleSauvegardeSaut : IntervalleSauvegarde))
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
