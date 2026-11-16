namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'un humain retient de ce que l'Entité et lui se sont dit (0.44.10). Deux traces, comme pour toute conversation : les mots
/// exacts s'effacent en quelques heures, le sens tient des semaines ou des années (Sachs, 1967 ; Brainerd et Reyna, théorie de la
/// trace floue). Chacune suit une loi de puissance de l'oubli, R = (1 + t / τ)^−β (Wixted et Ebbesen, 1991 ; Wixted, 2004). τ
/// dépend de sa mémoire et de la voie : une apparition marque bien plus qu'une voix, un rêve s'oublie vite au réveil (Goodenough,
/// 1991). Une part propre à chaque réplique (± 25 %) fait que deux humains, ni deux souvenirs, ne s'effacent jamais pareil.
/// </summary>
public static class SouvenirDialogue
{
    /// <summary>Ce qu'il en reste.</summary>
    public enum Trace : byte
    {
        /// <summary>Oublié.</summary>
        Oubli = 0,
        /// <summary>Il sait seulement que l'Entité lui a parlé ce jour-là.</summary>
        Vague = 1,
        /// <summary>Il se souvient du sens, plus des mots.</summary>
        Sens = 2,
        /// <summary>Il se souvient des mots mêmes.</summary>
        MotPourMot = 3,
    }

    /// <summary>Pente de l'oubli : environ 0,5 dans les mesures de rappel sur des jours et des mois (Wixted et Carpenter, 2007).</summary>
    private const double Pente = 0.5;

    /// <summary>Sa mémoire ramenée de 0 à 1 (aptitude Mémoire, moyenne 100 σ 15 : 55 → 0, 145 → 1), comme pour les révélations.</summary>
    public static double MemoireDe(Humain h) => Math.Clamp((h.Personnalite.Intelligence.Memoire - 55) / 90, 0, 1);

    /// <summary>Combien la voie marque : l'apparition le plus, le rêve le moins (estimation).</summary>
    public static double Marque(ModeParole mode) => mode switch
    {
        ModeParole.Apparition => 4,
        ModeParole.Voix => 2,
        ModeParole.Reve => 0.25,
        _ => 1,
    };

    /// <summary>
    /// Ce qu'il reste d'une réplique dite il y a <paramref name="secondes"/> (temps du monde). <paramref name="graine"/> donne la
    /// part propre à ce souvenir, la même à chaque appel.
    /// </summary>
    public static Trace Retenu(double secondes, double memoire, ModeParole mode, long graine)
    {
        var heures = Math.Max(0, secondes) / 3600;
        var force = (0.5 + memoire) * Marque(mode) * (0.75 + 0.5 * Part(graine));
        // Mots exacts : τ d'environ une heure ; le sens : environ dix jours (avant la part de chacun).
        var motPourMot = Math.Pow(1 + heures / (1.0 * force), -Pente);
        var sens = Math.Pow(1 + heures / (240.0 * force), -Pente);
        return motPourMot >= 0.35 ? Trace.MotPourMot : sens >= 0.3 ? Trace.Sens : sens >= 0.12 ? Trace.Vague : Trace.Oubli;
    }

    /// <summary>Un nombre de 0 à 1 tiré de la graine, toujours le même (SplitMix64).</summary>
    private static double Part(long graine)
    {
        var z = unchecked((ulong)graine + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }
}
