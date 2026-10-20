namespace Monde.Simulation.Humains;

/// <summary>
/// Le lait maternel protège le tout-petit des infections des poumons et du ventre. Un nourrisson qui n'est pas allaité en
/// tombe malade bien plus souvent, et en meurt bien plus : avant six mois, environ deux fois et demie plus de pneumonies
/// et de diarrhées, et beaucoup plus de morts ; de six mois à deux ans, l'écart est moindre (Lamberti et al., 2011 et
/// 2013). Le lait d'une autre femme protège comme celui de la mère ; des bouchées mâchées, non.
/// </summary>
public static class Sevre
{
    /// <summary>Les maladies contre lesquelles le lait protège.</summary>
    public static bool Concerne(Maladie m) => m == Maladie.Pneumonie || Hygiene.DuVentre(m);

    /// <summary>Surcroît de risque de tomber malade (des poumons ou du ventre) sans lait, selon l'âge en années.</summary>
    public static double Risque(double age) => age < 0.5 ? 2.3 : 1.3;

    /// <summary>
    /// Surcroît de gravité sans lait : les morts croissent bien plus que les cas dans ces études ; une partie de l'écart
    /// tient à la maladie elle-même (estimation).
    /// </summary>
    public static double Gravite(double age) => age < 0.5 ? 1.6 : 1.25;
}
