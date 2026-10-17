namespace Monde.Simulation.Humains;

/// <summary>
/// Tous les humains du monde, morts compris (l'humain d'identifiant n est à l'indice n - 1), avec deux index tenus à jour :
/// les vivants et les enfants de chacun. Dans un vieux monde, les morts sont bien plus nombreux que les vivants ;
/// les parcourir pour les écarter coûtait l'essentiel du calcul.
/// </summary>
public sealed class Population : List<Humain>
{
    private readonly List<Humain> _vivants = [];
    private readonly Dictionary<int, List<Humain>> _enfants = [];

    public Population(IEnumerable<Humain> humains)
    {
        foreach (var h in humains) Ajouter(h);
    }

    /// <summary>Les vivants, dans l'ordre des identifiants. Celui qui vient de mourir y reste jusqu'au prochain recensement.</summary>
    public IReadOnlyList<Humain> Vivants => _vivants;

    /// <summary>Ajoute un humain (né ou chargé), à la suite des autres.</summary>
    public void Ajouter(Humain h)
    {
        Add(h);
        if (h.EstVivant) _vivants.Add(h);
        if (h.MereId > 0) EnfantsDe(h.MereId, creer: true)!.Add(h);
        if (h.PereId > 0 && h.PereId != h.MereId) EnfantsDe(h.PereId, creer: true)!.Add(h);
    }

    /// <summary>Retire des vivants ceux qui sont morts depuis le dernier recensement.</summary>
    public void Recenser() => _vivants.RemoveAll(h => !h.EstVivant);

    /// <summary>Les enfants d'un humain (vivants ou morts), dans l'ordre des naissances.</summary>
    public IReadOnlyList<Humain> Enfants(int parentId) => EnfantsDe(parentId, creer: false) ?? (IReadOnlyList<Humain>)[];

    private List<Humain>? EnfantsDe(int parentId, bool creer)
    {
        if (_enfants.TryGetValue(parentId, out var liste)) return liste;
        if (!creer) return null;
        return _enfants[parentId] = [];
    }
}

/// <summary>Parcours rapides d'une liste d'humains quand c'est la population du monde ; sinon, la liste entière.</summary>
public static class Recensement
{
    /// <summary>
    /// Les humains à parcourir quand on écarte les morts : le même ordre, sans ceux qui étaient déjà morts
    /// (le parcours doit toujours vérifier <see cref="Humain.EstVivant"/>).
    /// </summary>
    public static IReadOnlyList<Humain> Vivants(this IReadOnlyList<Humain> tous) => tous is Population p ? p.Vivants : tous;

    /// <summary>Les enfants de <paramref name="parentId"/> (mère ou père), vivants ou morts, dans l'ordre des identifiants.</summary>
    public static IEnumerable<Humain> EnfantsDe(this IReadOnlyList<Humain> tous, int parentId) =>
        tous is Population p ? p.Enfants(parentId) : tous.Where(e => e.MereId == parentId || e.PereId == parentId);
}
