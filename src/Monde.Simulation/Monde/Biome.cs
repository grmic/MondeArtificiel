namespace Monde.Simulation.Monde;

/// <summary>Type de terrain d'une tuile. Les valeurs sont stockées sur un octet.</summary>
public enum Biome : byte
{
    Ocean = 0,
    EauPeuProfonde = 1,
    Lac = 2,
    Riviere = 3,
    Plage = 4,
    Marais = 5,
    PlaineDeFougeres = 6,
    ForetDeConiferes = 7,
    ForetTropicale = 8,
    Desert = 9,
    Montagne = 10,
    SommetEnneige = 11,
}

public static class Biomes
{
    /// <summary>Noms affichés, dans l'ordre des valeurs de l'énumération.</summary>
    public static IReadOnlyList<string> Noms { get; } =
    [
        "Océan", "Eau peu profonde", "Lac", "Rivière", "Plage", "Marais",
        "Plaine de fougères", "Forêt de conifères", "Forêt tropicale", "Désert",
        "Montagne", "Sommet enneigé",
    ];

    public static bool EstEauSalee(Biome b) => b is Biome.Ocean or Biome.EauPeuProfonde;

    public static bool EstEauDouce(Biome b) => b is Biome.Lac or Biome.Riviere;

    /// <summary>Terrain où un couple peut s'installer au départ.</summary>
    public static bool EstHabitable(Biome b) =>
        b is Biome.PlaineDeFougeres or Biome.ForetDeConiferes or Biome.ForetTropicale;
}
