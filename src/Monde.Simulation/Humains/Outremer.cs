namespace Monde.Simulation.Humains;

/// <summary>Ce qu'un groupe a fait pour la première fois avec une terre d'outre-mer.</summary>
public enum SorteOutremer : byte
{
    /// <summary>Il l'a aperçue (ou devinée à des signes sûrs : feux, objets travaillés).</summary>
    Vue = 0,
    /// <summary>L'un des siens y a posé le pied.</summary>
    Pied = 1,
    /// <summary>L'un des siens a rencontré quelqu'un de ce peuple.</summary>
    Contact = 2,
}

/// <summary>
/// Une première : une communauté (ou les isolés d'un peuple) a vu une terre d'outre-mer, y a posé le pied, ou en a rencontré le
/// peuple. C'est aussi ce que la communauté garde en mémoire de ces terres : on se raconte d'une génération à l'autre
/// qu'il y a une terre au-delà de la mer, et où.
/// </summary>
/// <param name="Groupe">Communauté (plus de 0) ou isolés sans communauté d'un peuple (le numéro de sa terre d'origine, en négatif).</param>
/// <param name="Masse">Terre concernée (voir <see cref="Nature.Mer.Masse"/>) ; pour un contact, la terre du peuple rencontré.</param>
/// <param name="X">Où la terre a été vue, abordée ou le peuple rencontré.</param>
public sealed record DecouverteOutremer(int Groupe, int Masse, SorteOutremer Sorte, long Secondes, int X, int Y, int HumainId, long EvenementId);

/// <summary>Les premières des groupes avec les terres d'outre-mer, dans l'ordre où elles ont eu lieu.</summary>
public sealed class Outremer
{
    private readonly List<DecouverteOutremer> _liste = [];
    private readonly HashSet<(int, int, SorteOutremer)> _connues = [];

    public Outremer(IEnumerable<DecouverteOutremer>? liste = null)
    {
        foreach (var d in liste ?? []) Ajouter(d);
    }

    public IReadOnlyList<DecouverteOutremer> Liste => _liste;

    /// <summary>
    /// Groupe d'un humain : sa communauté ; sans communauté, les isolés de son peuple (ceux de sa terre d'origine), pour que
    /// les premières des isolés restent des premières et non un fait marquant par personne.
    /// </summary>
    public static int GroupeDe(Humain h) => h.CommunauteId > 0 ? h.CommunauteId : -Math.Max(1, h.Continent);

    public bool Connait(int groupe, int masse, SorteOutremer sorte) => _connues.Contains((groupe, masse, sorte));

    /// <summary>Ajoute une première ; faux si le groupe l'avait déjà faite.</summary>
    public bool Ajouter(DecouverteOutremer d)
    {
        if (!_connues.Add((d.Groupe, d.Masse, d.Sorte))) return false;
        _liste.Add(d);
        return true;
    }

    /// <summary>Terres d'outre-mer dont le groupe a gardé la mémoire (la première fois qu'il les a vues ou abordées), par terre.</summary>
    public List<DecouverteOutremer> TerresConnues(int groupe) =>
        _liste.Where(d => d.Groupe == groupe && d.Sorte != SorteOutremer.Contact).GroupBy(d => d.Masse)
            .Select(g => g.OrderByDescending(d => d.Sorte).First()).ToList();
}
