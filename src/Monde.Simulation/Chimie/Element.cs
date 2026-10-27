namespace Monde.Simulation.Chimie;

/// <summary>
/// Les éléments dont la matière du monde est faite : une vingtaine, ceux qui comptent pour les roches, les sols, le vivant
/// et les premiers métaux. Les humains n'en savent rien : ils voient des couleurs, des duretés, des poids. Mais c'est de ces
/// éléments que la simulation déduit ce que deviendra une matière qu'on chauffe, qu'on frappe ou qu'on mouille.
/// </summary>
public enum Element : byte
{
    H = 0,
    C = 1,
    N = 2,
    O = 3,
    Na = 4,
    Mg = 5,
    Al = 6,
    Si = 7,
    P = 8,
    S = 9,
    Cl = 10,
    K = 11,
    Ca = 12,
    Mn = 13,
    Fe = 14,
    Cu = 15,
    Zn = 16,
    Ag = 17,
    Sn = 18,
    Au = 19,
    Pb = 20,
}

/// <summary>Données des éléments (masses atomiques standard de l'IUPAC, 2021, arrondies au centième).</summary>
public static class Elements
{
    public const int Nombre = 21;

    private static readonly double[] Masses =
    [
        1.01, 12.01, 14.01, 16.00, 22.99, 24.31, 26.98, 28.09, 30.97, 32.06, 35.45, 39.10, 40.08, 54.94, 55.85, 63.55, 65.38,
        107.87, 118.71, 196.97, 207.2,
    ];

    /// <summary>Masse atomique, en g/mol.</summary>
    public static double Masse(Element e) => Masses[(int)e];
}
