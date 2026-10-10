namespace Monde.Simulation.Monde;

/// <summary>
/// Bruit de valeur fractal en 2D. N'utilise que des opérations arithmétiques de base,
/// dont le résultat est identique sur toutes les plateformes (pas de sin, exp, pow).
/// </summary>
public sealed class Bruit(ulong graine)
{
    /// <summary>Valeur dans [0, 1) attachée à un point entier de la grille.</summary>
    private double ValeurGrille(long ix, long iy)
    {
        var etat = graine ^ ((ulong)ix * 0x9E3779B97F4A7C15UL) ^ ((ulong)iy * 0xC2B2AE3D27D4EB4FUL);
        return (Alea.SplitMix64(ref etat) >> 11) * (1.0 / (1UL << 53));
    }

    private static double Lisser(double t) => t * t * (3 - 2 * t);

    /// <summary>Bruit lissé dans [0, 1).</summary>
    public double Valeur(double x, double y)
    {
        var x0 = (long)Math.Floor(x);
        var y0 = (long)Math.Floor(y);
        var tx = Lisser(x - x0);
        var ty = Lisser(y - y0);

        var a = ValeurGrille(x0, y0);
        var b = ValeurGrille(x0 + 1, y0);
        var c = ValeurGrille(x0, y0 + 1);
        var d = ValeurGrille(x0 + 1, y0 + 1);

        var haut = a + (b - a) * tx;
        var bas = c + (d - c) * tx;
        return haut + (bas - haut) * ty;
    }

    /// <summary>Somme de plusieurs octaves, normalisée dans [0, 1).</summary>
    public double Fractal(double x, double y, int octaves)
    {
        double somme = 0, amplitude = 1, total = 0, frequence = 1;
        for (var i = 0; i < octaves; i++)
        {
            somme += Valeur(x * frequence, y * frequence) * amplitude;
            total += amplitude;
            amplitude *= 0.5;
            frequence *= 2;
        }
        return somme / total;
    }
}
