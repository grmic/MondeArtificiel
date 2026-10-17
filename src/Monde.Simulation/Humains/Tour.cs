using Monde.Simulation.Monde;

namespace Monde.Simulation.Humains;

/// <summary>
/// Le tour d'un humain pendant un pas simultané (0.22.1). Tous les humains décident en même temps, chacun sur son propre
/// fil de calcul : chacun agit sur une copie de lui-même et voit les autres tels qu'ils étaient au début du pas.
/// Ce qu'il fait aux autres (et au monde commun : feux, huttes, plantes, communautés…) est mis de côté, puis appliqué
/// après le pas, humain après humain dans l'ordre des identifiants. Chacun tire son propre hasard, tiré de la graine,
/// du pas et de son identifiant : une même graine redonne le même monde, quel que soit le nombre de cœurs.
/// </summary>
public sealed class Tour
{
    [ThreadStatic] private static Tour? _courant;

    /// <summary>Le tour en cours sur ce fil de calcul, ou null hors de la phase simultanée.</summary>
    public static Tour? Courant => _courant;

    private readonly List<Action<IReadOnlyList<Humain>>> _effets = [];
    private long _base;
    private int _rang;
    private int _nombre;
    private int _numeros;

    // Un tour sert de pas en pas (rien n'est alloué) : Commencer le remet à neuf.
    internal Tour()
    {
    }

    internal Tour(Humain original, Humain soi, ulong graine, long baseNumeros, int rang, int nombre) =>
        Commencer(original, soi, graine, baseNumeros, rang, nombre);

    internal void Commencer(Humain original, Humain soi, ulong graine, long baseNumeros, int rang, int nombre)
    {
        Original = original;
        Soi = soi;
        Alea.Reinitialiser(graine);
        _base = baseNumeros;
        _rang = rang;
        _nombre = nombre;
        _numeros = 0;
        Journal.Clear();
        _effets.Clear();
    }

    /// <summary>L'humain tel qu'il était au début du pas (les autres le voient ainsi).</summary>
    public Humain Original { get; private set; } = null!;

    /// <summary>La copie sur laquelle il agit pendant son tour.</summary>
    public Humain Soi { get; private set; } = null!;

    /// <summary>Son hasard pour ce pas.</summary>
    public Alea Alea { get; } = new(0);

    /// <summary>Événements notés pendant son tour, versés au journal après le pas.</summary>
    public List<EvenementJournal> Journal { get; } = [];

    /// <summary>Nombre d'événements notés pendant ce tour.</summary>
    public int NumerosPris => _numeros;

    /// <summary>
    /// Numéro du prochain événement de ce tour. Les numéros des humains d'un même pas sont entrelacés
    /// (le n-ième événement de l'humain de rang r porte base + n × nombre + r) : ils ne se chevauchent jamais
    /// et ne dépendent pas de l'ordre dans lequel les cœurs finissent.
    /// </summary>
    public long NouveauNumero() => _base + (long)_numeros++ * _nombre + _rang;

    /// <summary>Vrai si <paramref name="x"/> est un autre humain que celui dont c'est le tour (ses changements attendent la fin du pas).</summary>
    public static bool Autrui(Humain? x) => _courant is { } t && x is not null && !ReferenceEquals(x, t.Soi);

    /// <summary>Vrai pendant le tour d'un humain.</summary>
    public static bool EnCours => _courant is not null;

    // Les effets mis de côté ne gardent pas les humains eux-mêmes mais leurs identifiants : après le pas, chacun est
    // retrouvé dans sa version à jour.

    /// <summary>Pendant un tour, met l'effet de côté pour l'appliquer après le pas ; hors d'un tour, l'applique tout de suite.</summary>
    public static void Differer(Action effet)
    {
        if (_courant is { } t) t._effets.Add(_ => effet());
        else effet();
    }

    /// <summary>
    /// Comme <see cref="Differer(Action)"/>, pour un effet qui parcourt la population : il reçoit <paramref name="tous"/>
    /// hors d'un tour, la population à jour après le pas (les humains s'y retrouvent par leur identifiant).
    /// </summary>
    public static void Differer(IReadOnlyList<Humain> tous, Action<IReadOnlyList<Humain>> effet)
    {
        if (_courant is { } t) t._effets.Add(effet);
        else effet(tous);
    }

    /// <summary>Comme <see cref="Differer(Action)"/>, pour un effet sur <paramref name="a"/> (même soi).</summary>
    public static void Differer(Humain a, Action<Humain> effet)
    {
        if (_courant is { } t) { var ia = a.Id; t._effets.Add(tous => effet(tous[ia - 1])); }
        else effet(a);
    }

    /// <summary>Comme <see cref="Differer(Action)"/>, pour un effet sur deux humains.</summary>
    public static void Differer(Humain a, Humain b, Action<Humain, Humain> effet)
    {
        if (_courant is { } t) { var (ia, ib) = (a.Id, b.Id); t._effets.Add(tous => effet(tous[ia - 1], tous[ib - 1])); }
        else effet(a, b);
    }

    /// <summary>Comme <see cref="Differer(Action)"/>, pour un effet sur deux ou trois humains qui parcourt aussi la population.</summary>
    public static void Differer(IReadOnlyList<Humain> tous, Humain a, Humain b, Humain? c, Action<IReadOnlyList<Humain>, Humain, Humain, Humain?> effet)
    {
        if (_courant is { } t)
        {
            var (ia, ib, ic) = (a.Id, b.Id, c?.Id ?? 0);
            t._effets.Add(p => effet(p, p[ia - 1], p[ib - 1], ic > 0 ? p[ic - 1] : null));
        }
        else effet(tous, a, b, c);
    }

    /// <summary>Comme <see cref="Differer(Action)"/>, pour un effet sur trois humains (le troisième peut manquer).</summary>
    public static void Differer(Humain a, Humain b, Humain? c, Action<Humain, Humain, Humain?> effet)
    {
        if (_courant is { } t)
        {
            var (ia, ib, ic) = (a.Id, b.Id, c?.Id ?? 0);
            t._effets.Add(tous => effet(tous[ia - 1], tous[ib - 1], ic > 0 ? tous[ic - 1] : null));
        }
        else effet(a, b, c);
    }

    /// <summary>Change <paramref name="x"/> : tout de suite si c'est soi (ou hors d'un tour), après le pas si c'est un autre.</summary>
    public static void Changer(Humain x, Action<Humain> effet)
    {
        if (Autrui(x)) Differer(x, effet);
        else effet(x);
    }

    internal void Entrer() => _courant = this;

    internal static void Sortir() => _courant = null;

    /// <summary>Applique les effets mis de côté, dans l'ordre où ils ont été produits, sur les humains à jour.</summary>
    internal void Appliquer(IReadOnlyList<Humain> tous)
    {
        foreach (var e in _effets) e(tous);
        _effets.Clear();
    }
}
