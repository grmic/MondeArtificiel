using System.Runtime.CompilerServices;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Traits du visage, chacun en écarts-types autour de la moyenne des humains (0.36.1).</summary>
public enum TraitVisage : byte
{
    Largeur, Longueur, Machoire, Pommettes, EcartYeux, TailleYeux, FormeYeux, CouleurYeux, LongueurNez, LargeurNez,
    Levres, Sourcils, Oreilles, CouleurCheveux, TextureCheveux, Calvitie, Barbe, Grisonnement,
}

/// <summary>
/// Le visage d'un humain (0.36.1) : une douzaine de traits hérités, lus seulement pour dessiner son portrait. Rien n'est
/// enregistré ni tiré du hasard de la simulation : chaque trait se calcule de la graine du monde, du numéro de l'humain et
/// des traits de ses parents, si bien qu'un monde déjà commencé a ses visages et que l'histoire du monde ne change pas.
/// <para>
/// Modèle de génétique quantitative : chaque trait a une part héritée (valeur génétique) et une part propre à chacun.
/// L'enfant reçoit la moyenne des valeurs génétiques de ses parents, plus l'écart dû au brassage des gènes (la moitié de la
/// variance génétique) ; un groupe isolé dérive donc peu à peu vers un « air de famille ». Les fondateurs viennent d'une même
/// population : leurs valeurs tombent autour d'une origine propre à chaque monde. Héritabilités d'après les études de jumeaux :
/// forme du visage 0,6 à 0,8 (Weinberg et al., 2013 ; Cole et al., 2017), couleur des yeux et des cheveux 0,9 environ
/// (Lin et al., 2015), texture des cheveux 0,85, calvitie 0,8 (Rexbye et al., 2005), âge des cheveux gris 0,6.
/// Le teint, la symétrie, l'harmonie et la corpulence sont ceux que la simulation connaît déjà (voir <see cref="Apparence"/>).
/// </para>
/// </summary>
public static class Visages
{
    public static readonly int Nombre = Enum.GetValues<TraitVisage>().Length;

    private static readonly double[] H2 = [0.7, 0.7, 0.7, 0.65, 0.7, 0.6, 0.7, 0.9, 0.7, 0.7, 0.65, 0.6, 0.6, 0.9, 0.85, 0.8, 0.8, 0.6];

    /// <summary>Écart de l'origine d'un monde à la moyenne des humains, en écarts-types : les peuples diffèrent un peu.</summary>
    private const double EcartOrigine = 0.6;

    /// <summary>Valeurs génétiques déjà calculées, par monde (oubliées avec lui).</summary>
    private static readonly ConditionalWeakTable<MondeSimule, Dictionary<int, double[]>> Caches = new();

    /// <summary>Les traits visibles de <paramref name="h"/>, en écarts-types (sans tenir compte de l'âge ni de l'état du moment).</summary>
    public static double[] Traits(MondeSimule monde, Humain h, IReadOnlyList<Humain> humains)
    {
        var graine = monde.Configuration.Graine;
        var cache = Caches.GetValue(monde, _ => []);
        double[] genes;
        lock (cache) genes = Genes(h.Id, humains, graine, cache);
        var traits = new double[Nombre];
        for (var k = 0; k < Nombre; k++)
            traits[k] = genes[k] + Math.Sqrt(1 - H2[k]) * Normale(graine, h.Id, k, 1);
        return traits;
    }

    /// <summary>Un nombre de 0 à 1 propre à l'humain (coupe de cheveux, détails du dessin), tiré de son numéro.</summary>
    public static double Propre(MondeSimule monde, int id, int quoi) => Uniforme(monde.Configuration.Graine, id, quoi, 7);

    private static double[] Genes(int id, IReadOnlyList<Humain> humains, ulong graine, Dictionary<int, double[]> cache)
    {
        if (cache.TryGetValue(id, out var connu)) return connu;
        var h = id >= 1 && id <= humains.Count ? humains[id - 1] : null;
        // Les parents ont toujours un numéro plus petit : on les calcule d'abord (pas de boucle possible).
        double[]? Parent(int p) => p >= 1 && p < id && p <= humains.Count ? Genes(p, humains, graine, cache) : null;
        var mere = h is null ? null : Parent(h.MereId);
        var pere = h is null ? null : Parent(h.PereId);
        var genes = new double[Nombre];
        for (var k = 0; k < Nombre; k++)
        {
            var fondateur = EcartOrigine * Normale(graine, 0, k, 2) + Math.Sqrt(H2[k]) * Normale(graine, id, k, 3);
            genes[k] = (mere, pere) switch
            {
                (null, null) => fondateur,
                // Un seul parent connu : l'autre est un inconnu de la même population.
                ({ } m, null) => (m[k] + fondateur) / 2 + Math.Sqrt(H2[k] / 2) * Normale(graine, id, k, 4),
                (null, { } p) => (p[k] + fondateur) / 2 + Math.Sqrt(H2[k] / 2) * Normale(graine, id, k, 4),
                ({ } m, { } p) => (m[k] + p[k]) / 2 + Math.Sqrt(H2[k] / 2) * Normale(graine, id, k, 4),
            };
        }
        cache[id] = genes;
        return genes;
    }

    // Un hasard à part, tiré de la graine, du numéro, du trait et de l'usage : il ne touche jamais celui de la simulation.
    private static ulong Melanger(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static double Uniforme(ulong graine, int id, int k, int usage) =>
        ((Melanger(graine ^ Melanger((ulong)id * 0x100000001B3UL ^ ((ulong)k << 40) ^ ((ulong)usage << 56))) >> 11) + 0.5) / (1UL << 53);

    private static double Normale(ulong graine, int id, int k, int usage)
    {
        var u1 = Uniforme(graine, id, k, usage);
        var u2 = Uniforme(graine, id, k, usage + 16);
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>Âge, en années, où les cheveux d'un humain commencent à grisonner : 30 à 55 ans environ.</summary>
    public static double DebutDuGris(double[] traits) => 42 + 6 * traits[(int)TraitVisage.Grisonnement];

    /// <summary>Part de cheveux gris, de 0 à 1.</summary>
    public static double Gris(double[] traits, double age) => Math.Clamp((age - DebutDuGris(traits)) / 25, 0, 1);

    /// <summary>
    /// Calvitie d'un homme, de 0 à 1 : elle commence entre 20 et 60 ans selon sa prédisposition, et avance plus vite chez
    /// ceux qui y sont prédisposés (environ la moitié des hommes ont une calvitie visible à 50 ans : Rhodes et al., 1998).
    /// </summary>
    public static double Calvitie(double[] traits, Sexe sexe, double age)
    {
        if (sexe != Sexe.Homme) return 0;
        var z = traits[(int)TraitVisage.Calvitie];
        var debut = 50 - 12 * z;
        return Math.Clamp((age - debut) / (20 - 4 * Math.Clamp(z, -1, 2)), 0, 1);
    }

    /// <summary>Années écoulées, pour qui veut dessiner un humain mort à l'âge qu'il avait.</summary>
    public static double Age(Humain h, long maintenant) =>
        (h.MortLe is { } m ? m - h.Naissance : maintenant - h.Naissance) / (double)Calendrier.SecondesParAn;
}
