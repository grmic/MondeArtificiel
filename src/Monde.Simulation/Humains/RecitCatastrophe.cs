using Monde.Simulation.Nature;

namespace Monde.Simulation.Humains;

/// <summary>Ce qu'une communauté se raconte d'une catastrophe (0.39.0).</summary>
public enum Explication : byte
{
    /// <summary>On a fâché l'invisible : une faute, peut-être un coupable.</summary>
    Colere = 0,
    /// <summary>Un esprit du phénomène (« celui qui fait trembler la terre »), qu'on apaise par des offrandes.</summary>
    Esprit = 1,
    /// <summary>Un présage : le phénomène annonçait un malheur.</summary>
    Presage = 2,
    /// <summary>Une cause dans la nature : la chose revient, on sait quand ; une prévision, pas une croyance.</summary>
    Nature = 3,
}

/// <summary>
/// Un récit né d'une catastrophe dans une communauté : l'explication que le groupe s'est donnée, ceux qui y croient, et ce qu'elle
/// change dans leur vie (lieu interdit, signes qui font fuir, rite, bouc émissaire, interdit de chasse). Il passe aux enfants, grossit
/// en passant, et s'oublie quand plus personne ne le raconte (Vansina, 1985). Les numéros se suivent à partir de 1.
/// </summary>
public sealed class RecitCatastrophe
{
    public required int Id { get; init; }

    /// <summary>Communauté où le récit est né.</summary>
    public required int CommunauteId { get; init; }

    /// <summary>Numéro de la catastrophe (celui de l'événement qui l'annonce).</summary>
    public required long CatastropheId { get; init; }

    public required TypeCatastrophe Type { get; init; }
    public required Explication Explication { get; init; }
    public required long NeLe { get; init; }

    /// <summary>Celui dont l'explication s'est imposée.</summary>
    public int PorteurId { get; init; }

    /// <summary>Ligne du journal qui raconte sa naissance (cause de tout ce qui en découle).</summary>
    public long EvenementId { get; set; }

    /// <summary>Celui qu'on accuse d'avoir attiré le malheur (0 : personne).</summary>
    public int AccuseId { get; set; }

    /// <summary>Lieu où la chose est arrivée (rayon 0 : aucun) : on n'y campe plus si <see cref="Lieu"/>, on n'y chasse plus si <see cref="Tabou"/>.</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int Rayon { get; init; }

    /// <summary>On ne campe plus sur ce lieu.</summary>
    public bool Lieu { get; init; }

    /// <summary>Les signes qui l'ont précédée font fuir ceux qui y croient (la mer qui se retire, la terre qui gronde).</summary>
    public bool Signes { get; init; }

    /// <summary>Un rite l'apaise chaque année, à la saison du malheur.</summary>
    public bool Rite { get; init; }

    /// <summary>La bête du lieu est maudite : on n'y chasse plus (épidémie venue d'une bête).</summary>
    public bool Tabou { get; init; }

    /// <summary>Morts réellement causés, et morts tels qu'on les raconte (le récit grossit d'une génération à l'autre).</summary>
    public int Morts { get; init; }
    public int MortsRacontes { get; set; }

    /// <summary>Dernier rite (secondes du monde ; 0 : jamais).</summary>
    public long DernierRiteLe { get; set; }

    /// <summary>Le présage s'est accompli : une mort marquante l'a suivi (0 : pas encore).</summary>
    public long AccompliLe { get; set; }

    /// <summary>Oublié : plus personne ne le raconte (0 : vivant).</summary>
    public long OublieLe { get; set; }

    /// <summary>Ceux qui y croient.</summary>
    public HashSet<int> Croyants { get; set; } = [];

    /// <summary>Ceux qui l'ont vécue : ils ne l'oublient pas.</summary>
    public HashSet<int> Temoins { get; set; } = [];

    /// <summary>Ceux qui l'ont oublié ou ne l'ont pas cru quand on le leur a raconté : on ne le leur raconte plus.</summary>
    public HashSet<int> Detaches { get; set; } = [];

    public bool Vivant => OublieLe == 0;

    /// <summary>Le lieu interdit couvre cette case.</summary>
    public bool Couvre(int x, int y) => Rayon > 0 && Math.Max(Math.Abs(x - X), Math.Abs(y - Y)) <= Rayon;

    public RecitCatastrophe Copier()
    {
        var c = (RecitCatastrophe)MemberwiseClone();
        c.Croyants = [.. Croyants];
        c.Temoins = [.. Temoins];
        c.Detaches = [.. Detaches];
        return c;
    }
}

/// <summary>
/// Les récits de catastrophe du monde, avec ce que les règles consultent à chaque pas (lu en parallèle, reconstruit seulement pendant
/// les passes séquentielles) : qui guette quels signes, où chacun ne chasse plus, où chaque communauté ne campe plus.
/// </summary>
public sealed class RecitsCatastrophes
{
    private readonly List<RecitCatastrophe> _liste = [];
    private Dictionary<int, int> _signes = [];
    private Dictionary<int, List<RecitCatastrophe>> _tabous = [];
    private Dictionary<int, List<RecitCatastrophe>> _interdits = [];

    public RecitsCatastrophes(IEnumerable<RecitCatastrophe>? recits = null)
    {
        if (recits is not null) _liste.AddRange(recits);
        Indexer();
    }

    public IReadOnlyList<RecitCatastrophe> Liste => _liste;

    internal void Ajouter(RecitCatastrophe r)
    {
        _liste.Add(r);
        Indexer();
    }

    /// <summary>Recalcule ce que les règles consultent (après une naissance, une adhésion, un oubli).</summary>
    internal void Indexer()
    {
        var signes = new Dictionary<int, int>();
        var tabous = new Dictionary<int, List<RecitCatastrophe>>();
        var interdits = new Dictionary<int, List<RecitCatastrophe>>();
        foreach (var r in _liste)
        {
            if (!r.Vivant) continue;
            foreach (var id in r.Croyants)
            {
                if (r.Signes) signes[id] = signes.GetValueOrDefault(id) | 1 << (int)r.Type;
                if (r.Tabou) (tabous.TryGetValue(id, out var l) ? l : tabous[id] = []).Add(r);
            }
            if (r.Lieu && r.Rayon > 0) (interdits.TryGetValue(r.CommunauteId, out var li) ? li : interdits[r.CommunauteId] = []).Add(r);
        }
        (_signes, _tabous, _interdits) = (signes, tabous, interdits);
    }

    /// <summary>Il guette les signes de ce genre de catastrophe : le récit qu'on lui a fait les lui a appris.</summary>
    public bool Averti(int humain, TypeCatastrophe type) => _signes.TryGetValue(humain, out var m) && (m & 1 << (int)type) != 0;

    /// <summary>Il ne chasse pas ici : la bête de ce lieu est maudite.</summary>
    public bool Tabou(int humain, int x, int y) => _tabous.TryGetValue(humain, out var l) && l.Exists(r => r.Couvre(x, y));

    /// <summary>
    /// Un lieu où la communauté ne campe plus : la moitié au moins de ses membres croient au récit du malheur qui y est arrivé
    /// (<paramref name="membres"/> : ses membres vivants).
    /// </summary>
    public RecitCatastrophe? Interdit(int communaute, int x, int y, IReadOnlyCollection<Humain> membres)
    {
        if (membres.Count == 0 || !_interdits.TryGetValue(communaute, out var l)) return null;
        foreach (var r in l)
        {
            if (!r.Couvre(x, y)) continue;
            var croient = 0;
            foreach (var h in membres) if (r.Croyants.Contains(h.Id)) croient++;
            if (2 * croient >= membres.Count) return r;
        }
        return null;
    }

    /// <summary>La communauté a au moins un lieu interdit (pour ne rien calculer sinon).</summary>
    public bool AUnInterdit(int communaute) => _interdits.ContainsKey(communaute);
}
