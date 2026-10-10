namespace Monde.Simulation.Temps;

/// <summary>Allure de l'horloge : combien de secondes du monde s'écoulent par seconde réelle.</summary>
public sealed record Vitesse(int Niveau, string Nom, long Facteur)
{
    /// <summary>Vitesses proposées, de la pause à « 5 minutes réelles = 5 ans ».</summary>
    public static IReadOnlyList<Vitesse> Toutes { get; } =
    [
        new(0, "Pause", 0),
        new(1, "Temps réel", 1),
        new(2, "1 min = 1 heure", 60),
        new(3, "1 min = 1 jour", 1_440),
        new(4, "1 min = 1 saison", 129_600),
        new(5, "5 min = 5 ans", 518_400),
    ];

    public static Vitesse Pause => Toutes[0];

    public static bool Existe(int niveau) => niveau >= 0 && niveau < Toutes.Count;
}
