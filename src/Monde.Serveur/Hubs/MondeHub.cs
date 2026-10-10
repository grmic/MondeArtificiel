using Microsoft.AspNetCore.SignalR;
using Monde.Simulation;

namespace Monde.Serveur.Hubs;

/// <summary>
/// Canal temps réel entre le serveur et les navigateurs.
/// </summary>
public sealed class MondeHub : Hub
{
    public const string Route = "/hub/monde";

    /// <summary>Permet au client de vérifier la liaison et d'obtenir l'heure du serveur.</summary>
    public ReponsePing Ping() => new(DateTimeOffset.UtcNow, InfosMonde.ParDefaut.Nom);
}

public sealed record ReponsePing(DateTimeOffset HeureServeur, string Monde);
