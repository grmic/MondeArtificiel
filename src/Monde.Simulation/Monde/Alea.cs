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

    [ThreadStatic] private static Alea? _detour;

    /// <summary>
    /// Générateur commun du monde : pendant le tour d'un humain (pas simultané), ses tirages sont pris dans le hasard
    /// propre à ce tour (voir <see cref="Detourner"/>), pour que le résultat ne dépende pas de l'ordre des cœurs.
    /// </summary>
    public bool Commun { get; init; }

    /// <summary>Sur ce fil de calcul, envoie les tirages des générateurs communs vers <paramref name="alea"/> (null : plus de détour).</summary>
    public static void Detourner(Alea? alea) => _detour = alea;

    public Alea(ulong graine) => Reinitialiser(graine);

    /// <summary>Repart de la graine <paramref name="graine"/>, comme un générateur neuf.</summary>
    public void Reinitialiser(ulong graine)
    {
        var x = graine;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>Reprend un générateur à partir d'un état sauvegardé (voir <see cref="Etat"/>).</summary>
    public static Alea Depuis(IReadOnlyList<ulong> etat, bool commun = false)
    {
        if (etat.Count != 4 || etat.All(v => v == 0))
        {
            throw new ArgumentException("L'état doit contenir 4 valeurs, pas toutes nulles.", nameof(etat));
        }
        var alea = new Alea(0) { Commun = commun };
        (alea._s0, alea._s1, alea._s2, alea._s3) = (etat[0], etat[1], etat[2], etat[3]);
        return alea;
    }

    /// <summary>État interne, à sauvegarder pour reprendre exactement la même suite.</summary>
    public ulong[] Etat => [_s0, _s1, _s2, _s3];

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
        if (Commun && _detour is { } d) return d.Suivant();
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
