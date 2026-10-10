using System.Numerics;

namespace Monde.Simulation.Monde;

/// <summary>
/// Générateur pseudo-aléatoire déterministe (xoshiro256**, initialisé par SplitMix64).
/// Contrairement à System.Random, son algorithme est fixé : une même graine donne
/// la même suite sur toutes les plateformes et toutes les versions de .NET.
/// Usage : simulation uniquement, ce n'est pas un générateur cryptographique.
/// </summary>
public sealed class Alea
{
    private ulong _s0, _s1, _s2, _s3;

    public Alea(ulong graine)
    {
        var x = graine;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>Étape de SplitMix64 : fait avancer l'état et renvoie une valeur mélangée.</summary>
    public static ulong SplitMix64(ref ulong etat)
    {
        etat += 0x9E3779B97F4A7C15UL;
        var z = etat;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong Suivant()
    {
        var resultat = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return resultat;
    }

    /// <summary>Réel uniforme dans [0, 1).</summary>
    public double Reel() => (Suivant() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Entier uniforme dans [0, maxExclu).</summary>
    public int Entier(int maxExclu)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclu);
        // Méthode multiplication-décalage : biais inférieur à 2^-32, négligeable ici.
        return (int)(((Suivant() >> 32) * (ulong)maxExclu) >> 32);
    }

    /// <summary>Mélange de Fisher-Yates, en place.</summary>
    public void Melanger<T>(IList<T> liste)
    {
        for (var i = liste.Count - 1; i > 0; i--)
        {
            var j = Entier(i + 1);
            (liste[i], liste[j]) = (liste[j], liste[i]);
        }
    }
}
