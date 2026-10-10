using Microsoft.AspNetCore.SignalR;
using Monde.Simulation;
using Monde.Simulation.Temps;

namespace Monde.Serveur.Hubs;

/// <summary>
/// Canal temps réel entre le serveur et les navigateurs.
/// Le serveur diffuse le message « Horloge » plusieurs fois par seconde.
/// </summary>
public sealed class MondeHub(ServiceMonde service) : Hub
{
    public const string Route = "/hub/monde";

    /// <summary>Permet au client de vérifier la liaison et d'obtenir l'heure du serveur.</summary>
    public ReponsePing Ping() => new(DateTimeOffset.UtcNow, InfosMonde.ParDefaut.Nom);

    public HorlogeDto Horloge() => HorlogeDto.Depuis(service.Monde);

    public IReadOnlyList<Vitesse> Vitesses() => Vitesse.Toutes;

    public List<HumainDto> Humains() => HumainDto.Depuis(service.Monde);

    public async Task<HorlogeDto> ChoisirVitesse(int niveau)
    {
        if (!Vitesse.Existe(niveau))
        {
            // Message volontairement simple : il est renvoyé tel quel au navigateur.
            throw new HubException("Vitesse inconnue.");
        }
        service.Monde.ChangerVitesse(niveau);
        service.Sauvegarder();
        var horloge = HorlogeDto.Depuis(service.Monde);
        await Clients.All.SendAsync("Horloge", horloge);
        return horloge;
    }

    /// <summary>
    /// Recommence le monde à l'an 1 avec la graine donnée (ou une graine au hasard si elle est absente).
    /// Tous les navigateurs connectés sont prévenus pour recharger la carte.
    /// </summary>
    public async Task<HorlogeDto> Reinitialiser(long? graine)
    {
        if (graine is < 0 or > uint.MaxValue)
        {
            throw new HubException("La graine doit être un entier entre 0 et 4294967295.");
        }
        var valeur = graine is { } g ? (uint)g : (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        var monde = service.Reinitialiser(valeur);
        var horloge = HorlogeDto.Depuis(monde);
        await Clients.All.SendAsync("MondeReinitialise", valeur);
        await Clients.All.SendAsync("Horloge", horloge);
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return horloge;
    }
}

public sealed record ReponsePing(DateTimeOffset HeureServeur, string Monde);

/// <summary>État de l'horloge envoyé au navigateur.</summary>
public sealed record HorlogeDto(
    long Secondes,
    long Pas,
    string Texte,
    long Annee,
    string Saison,
    int Jour,
    int Heure,
    int Minute,
    bool EstJour,
    double Lumiere,
    int NiveauVitesse)
{
    public static HorlogeDto Depuis(MondeSimule monde)
    {
        var etat = monde.Etat;
        var date = Calendrier.Decomposer(etat.Secondes);
        return new HorlogeDto(etat.Secondes, etat.Pas, date.ToString(), date.Annee, date.NomSaison,
            date.JourDeSaison, date.Heure, date.Minute, date.EstJour, date.Lumiere, etat.NiveauVitesse);
    }
}
