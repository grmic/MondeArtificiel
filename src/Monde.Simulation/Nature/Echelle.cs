namespace Monde.Simulation.Nature;

/// <summary>
/// Échelle du monde, déduite du pas de marche : un humain avance d'une case en 10 minutes, soit environ 800 m
/// au pas d'un marcheur chargé (4 à 5 km/h). Les catastrophes s'étendent en kilomètres réels, convertis en cases.
/// Le lot « distances et surface » s'appuiera sur ces mêmes valeurs.
/// </summary>
public static class Echelle
{
    /// <summary>Côté d'une case, en kilomètres.</summary>
    public const double KilometresParCase = 0.8;

    /// <summary>
    /// Dénivelé représenté par une unité d'altitude (0 à 255, mer à 128) : les montagnes commencent vers 1 400 m
    /// et les plus hauts sommets atteignent environ 5 000 m.
    /// </summary>
    public const double MetresParUnite = 40;

    public static int Cases(double kilometres) => (int)Math.Round(kilometres / KilometresParCase);

    public static double Kilometres(double cases) => cases * KilometresParCase;
}
