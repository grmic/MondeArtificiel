using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Température ressentie sur une tuile à un instant donné.</summary>
public static class Climat
{
    /// <summary>Température moyenne (°C) au milieu de chaque saison : climat chaud de l'ère des dinosaures.</summary>
    private static readonly double[] MilieuSaison = [19, 27, 18, 10];

    public static double Temperature(Carte carte, int x, int y, long secondes)
    {
        var date = Calendrier.Decomposer(secondes);

        // Interpolation linéaire entre les milieux de saisons, pour une année sans à-coups.
        var jourDansAnnee = secondes % Calendrier.SecondesParAn / (double)Calendrier.SecondesParJour;
        var position = (jourDansAnnee - Calendrier.JoursParSaison / 2.0) / Calendrier.JoursParSaison;
        if (position < 0) position += 4;
        var saison = (int)position % 4;
        var t = position - Math.Floor(position);
        var moyenne = MilieuSaison[saison] + (MilieuSaison[(saison + 1) % 4] - MilieuSaison[saison]) * t;

        var latitude = Math.Abs((y + 0.5) / carte.Hauteur - 0.5) * 2;
        var altitude = Math.Max(0, carte.Altitudes[carte.Indice(x, y)] - Carte.NiveauMer) / 127.0;
        var nuit = (1 - date.Lumiere) * 5;

        return moyenne - latitude * 6 - altitude * 25 - nuit;
    }

    /// <summary>Protection qu'offre le terrain contre le froid et la pluie (0 à 1).</summary>
    public static double Abri(Biome biome) => biome switch
    {
        Biome.ForetDeConiferes => 0.6,
        Biome.ForetTropicale => 0.5,
        Biome.Montagne => 0.4,
        Biome.Marais => 0.1,
        _ => 0,
    };

    /// <summary>
    /// Protection d'une grotte : à quelques mètres de l'entrée, la température ne suit plus guère celle du dehors et le vent
    /// n'entre pas (une grotte profonde garde la moyenne annuelle du lieu) ; un abri sous roche ne coupe que la pluie et une
    /// partie du vent.
    /// </summary>
    public static double Abri(Nature.Grotte? g) => g switch
    {
        null => 0,
        { SousRoche: true } => 0.5,
        { Metres: < 10 } => 0.7,
        _ => 0.85,
    };

    /// <summary>Un humain peut-il marcher sur ce terrain ? (pas encore de nage ni de bateau)</summary>
    public static bool EstPraticable(Biome biome) =>
        biome is not (Biome.Ocean or Biome.EauPeuProfonde or Biome.Lac);
}
