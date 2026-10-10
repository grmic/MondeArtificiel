namespace Monde.Simulation;

/// <summary>
/// Informations générales sur le monde, renvoyées au client.
/// Le moteur de simulation viendra s'ajouter à ce projet dans les étapes suivantes.
/// </summary>
public sealed record InfosMonde(string Nom, string VersionMoteur)
{
    public static InfosMonde ParDefaut { get; } = new("Monde artificiel", "0.1.0");
}
