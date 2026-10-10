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
