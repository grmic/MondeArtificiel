namespace Monde.Simulation.Humains;

/// <summary>Un feu entretenu par des humains : il brûle tant qu'on l'alimente.</summary>
/// <param name="Heures">Heures de combustible qui restent.</param>
/// <param name="AllumeurId">Humain qui l'a allumé.</param>
/// <param name="EvenementId">Événement du journal qui en rapporte l'allumage.</param>
/// <param name="Couvert">Couvert de cendre pour la nuit : quand le bois est consumé, sa braise couve jusqu'au matin.</param>
/// <param name="Degage">L'herbe a été écartée autour et des pierres l'entourent : il gagne rarement les herbes.</param>
/// <param name="EntretenuParId">Dernier humain qui l'a nourri (0 : son allumeur) ; c'est à lui qu'on s'en prend s'il s'échappe.</param>
public sealed record Foyer(int Id, int X, int Y, double Heures, long AllumeLe, int AllumeurId, long EvenementId, bool Couvert = false,
    bool Degage = false, int EntretenuParId = 0)
{
    /// <summary>Humain qui en a la garde : le dernier qui l'a nourri, sinon celui qui l'a allumé.</summary>
    public int GardienId => EntretenuParId != 0 ? EntretenuParId : AllumeurId;
}

/// <summary>
/// La braise d'un feu dont le bois est consumé : elle couve encore quelques heures, une nuit entière sous la cendre, et un peu
/// de bois la ranime. Sans personne pour la nourrir, elle s'éteint.
/// </summary>
/// <param name="Heures">Heures pendant lesquelles elle couve encore.</param>
public sealed record Braise(int Id, int X, int Y, double Heures, bool Couvert, long AllumeLe, int AllumeurId, long EvenementId);

/// <summary>Ce qu'on met en réserve.</summary>
public enum SorteReserve : byte
{
    /// <summary>Une calebasse ou une outre bouchée, enterrée pleine (en litres).</summary>
    Eau = 0,
    /// <summary>Viande coupée en lanières et séchée au soleil.</summary>
    ViandeSechee = 1,
    /// <summary>Fruits séchés au soleil.</summary>
    FruitsSeches = 2,
    /// <summary>Noix et graines amassées en automne.</summary>
    NoixGraines = 3,
    /// <summary>Viande cachée dans le froid, sous des pierres ou dans une fosse.</summary>
    ViandeCachee = 4,
}

/// <summary>
/// Une réserve posée quelque part : ce qu'elle contient, qui l'a faite et pour quelle communauté. Elle se gâte plus ou moins vite
/// selon ce qu'elle est, le climat et l'humidité ; les bêtes et les voleurs y prennent leur part.
/// </summary>
/// <param name="Kg">Ce qui reste, en kilos (en litres pour l'eau).</param>
/// <param name="Etat">Fraîcheur, de 1 (intacte) à 0 (gâtée).</param>
/// <param name="Qualite">Soin de la cache, de 0 à 1 : bouchon d'argile ou de résine, pierres lourdes, lanières fines.</param>
/// <param name="CommunauteId">Communauté de celui qui l'a faite, au moment où il l'a faite (0 : aucune).</param>
public sealed record Reserve(int Id, int X, int Y, SorteReserve Sorte, double Kg, double Etat, double Qualite, long FaiteLe, int AuteurId,
    int CommunauteId, long EvenementId)
{
    /// <summary>Faim apaisée par un kilo (un kilo de viande fraîche en apaise 10).</summary>
    public static double FaimParKg(SorteReserve s) => s switch
    {
        // La viande séchée a perdu les deux tiers de son eau ; les noix sont les plus riches (environ 6 kcal par gramme).
        SorteReserve.ViandeSechee => 30,
        SorteReserve.FruitsSeches => 25,
        SorteReserve.NoixGraines => 50,
        SorteReserve.ViandeCachee => 10,
        _ => 0,
    };

    /// <summary>De quoi apaiser la faim (0 pour l'eau).</summary>
    public double Vivres => Kg * FaimParKg(Sorte);
}

/// <summary>Un abri de branches et de feuilles : il protège de la pluie et du froid, puis s'affaisse en quelques mois.</summary>
/// <param name="Solidite">De 1 (neuf, bien fait) à 0 (effondré).</param>
public sealed record Hutte(int Id, int X, int Y, double Solidite, long BatieLe, int BatisseurId, long EvenementId);

/// <summary>Savoirs vivants au début d'une année : effets distincts que savent obtenir les vivants du monde (communauté 0) ou d'une communauté.</summary>
public sealed record MesureSavoirs(int Annee, int CommunauteId, int Savoirs);

/// <summary>Première fois qu'un effet a été obtenu dans le monde : par qui, quand, comment.</summary>
/// <param name="Pieces">Pour un objet assemblé : ses pièces (voir <see cref="Assemblage"/>).</param>
/// <param name="CommunauteId">Communauté de l'inventeur au moment de la découverte (0 : aucune).</param>
public sealed record Decouverte(Effet Effet, int HumainId, long Le, long EvenementId, OrigineSavoir Origine, string Pieces = "", int CommunauteId = 0);

/// <summary>Ce que les humains ont bâti et allumé dans le monde, et la chronologie de leurs découvertes.</summary>
public sealed class Installations
{
    public List<Foyer> Foyers { get; } = [];
    public List<Hutte> Huttes { get; } = [];
    public List<Decouverte> Decouvertes { get; } = [];
    public List<Braise> Braises { get; } = [];
    public List<Reserve> Reserves { get; } = [];
    /// <summary>Images peintes sur la roche : elles restent.</summary>
    public List<Oeuvre> Oeuvres { get; } = [];
    private int _prochainId = 1;
    private int _prochaineOeuvre = 1;
    private int _prochaineReserve = 1;
    // Nombre de feux et de huttes par case : la plupart des cases n'en ont pas, et on les interroge très souvent.
    private readonly Dictionary<long, int> _casesFoyers = [];
    private readonly Dictionary<long, int> _casesHuttes = [];
    private readonly Dictionary<long, int> _casesBraises = [];
    private readonly Dictionary<long, int> _casesReserves = [];

    private static long Case(int x, int y) => ((long)x << 32) | (uint)y;

    private static void Compter(Dictionary<long, int> cases, int x, int y, int n)
    {
        var c = Case(x, y);
        var total = cases.GetValueOrDefault(c) + n;
        if (total > 0) cases[c] = total;
        else cases.Remove(c);
    }

    public Installations(IEnumerable<Foyer>? foyers = null, IEnumerable<Hutte>? huttes = null, IEnumerable<Decouverte>? decouvertes = null,
        IEnumerable<Oeuvre>? oeuvres = null, IEnumerable<Braise>? braises = null, IEnumerable<Reserve>? reserves = null)
    {
        Foyers.AddRange(foyers ?? []);
        Huttes.AddRange(huttes ?? []);
        Decouvertes.AddRange(decouvertes ?? []);
        Oeuvres.AddRange(oeuvres ?? []);
        Braises.AddRange(braises ?? []);
        Reserves.AddRange(reserves ?? []);
        foreach (var f in Foyers) Compter(_casesFoyers, f.X, f.Y, 1);
        foreach (var h in Huttes) Compter(_casesHuttes, h.X, h.Y, 1);
        foreach (var b in Braises) Compter(_casesBraises, b.X, b.Y, 1);
        foreach (var r in Reserves) Compter(_casesReserves, r.X, r.Y, 1);
        _prochainId = 1 + new[]
        {
            Foyers.Select(f => f.Id).DefaultIfEmpty(0).Max(), Huttes.Select(h => h.Id).DefaultIfEmpty(0).Max(), Braises.Select(b => b.Id).DefaultIfEmpty(0).Max(),
        }.Max();
        _prochaineOeuvre = 1 + Oeuvres.Select(o => o.Id).DefaultIfEmpty(0).Max();
        _prochaineReserve = 1 + Reserves.Select(r => r.Id).DefaultIfEmpty(0).Max();
    }

    /// <summary>Ajoute une image sur la paroi de cette case.</summary>
    public Oeuvre Peindre(int x, int y, int auteurId, long maintenant, MotifPeint motif, string description, long evenementId, int communauteId)
    {
        var o = new Oeuvre(_prochaineOeuvre++, x, y, auteurId, maintenant, motif, description, evenementId, communauteId);
        Oeuvres.Add(o);
        return o;
    }

    public Foyer? FoyerEn(int x, int y)
    {
        if (!_casesFoyers.ContainsKey(Case(x, y))) return null;
        foreach (var f in Foyers) if (f.X == x && f.Y == y) return f;
        return null;
    }

    /// <summary>Braise qui couve sur cette case (un feu sans flamme), ou null.</summary>
    public Braise? BraiseEn(int x, int y)
    {
        if (!_casesBraises.ContainsKey(Case(x, y))) return null;
        foreach (var b in Braises) if (b.X == x && b.Y == y) return b;
        return null;
    }

    /// <summary>Y a-t-il une réserve sur cette case (quelle qu'elle soit) ?</summary>
    public bool ReserveEn(int x, int y) => _casesReserves.ContainsKey(Case(x, y));

    /// <summary>Réserves posées sur cette case.</summary>
    public IEnumerable<Reserve> ReservesEn(int x, int y)
    {
        if (!_casesReserves.ContainsKey(Case(x, y))) yield break;
        foreach (var r in Reserves) if (r.X == x && r.Y == y) yield return r;
    }

    public Reserve? ReserveParId(int id)
    {
        foreach (var r in Reserves) if (r.Id == id) return r;
        return null;
    }

    /// <summary>Pose une réserve, ou ajoute à celle du même auteur et de la même sorte sur cette case.</summary>
    public Reserve Reserver(int x, int y, SorteReserve sorte, double kg, double qualite, long maintenant, int auteurId, int communauteId, long evenementId)
    {
        for (var i = 0; i < Reserves.Count; i++)
        {
            var r = Reserves[i];
            if (r.X != x || r.Y != y || r.Sorte != sorte || r.AuteurId != auteurId) continue;
            // Le neuf se mêle au vieux : la fraîcheur est celle du mélange.
            var total = r.Kg + kg;
            var etat = total <= 0 ? 1 : (r.Kg * r.Etat + kg) / total;
            return Reserves[i] = r with { Kg = total, Etat = etat, Qualite = (r.Kg * r.Qualite + kg * qualite) / Math.Max(1e-9, total) };
        }
        var nouvelle = new Reserve(_prochaineReserve++, x, y, sorte, kg, 1, qualite, maintenant, auteurId, communauteId, evenementId);
        Reserves.Add(nouvelle);
        Compter(_casesReserves, x, y, 1);
        return nouvelle;
    }

    /// <summary>Prend dans une réserve (au plus ce qui reste) et renvoie ce qui a été pris ; vide, elle disparaît.</summary>
    public double Puiser(int reserveId, double kg)
    {
        var i = Reserves.FindIndex(r => r.Id == reserveId);
        if (i < 0 || kg <= 0) return 0;
        var r = Reserves[i];
        var pris = Math.Min(kg, r.Kg);
        if (r.Kg - pris <= 0.05) Retirer(i);
        else Reserves[i] = r with { Kg = r.Kg - pris };
        return pris;
    }

    /// <summary>Change une réserve (elle se gâte, des bêtes la pillent) ; null la retire.</summary>
    public void Changer(int reserveId, Reserve? r)
    {
        var i = Reserves.FindIndex(x => x.Id == reserveId);
        if (i < 0) return;
        if (r is null || r.Kg <= 0.05 || r.Etat <= 0) Retirer(i);
        else Reserves[i] = r;
    }

    private void Retirer(int i)
    {
        var r = Reserves[i];
        Reserves.RemoveAt(i);
        Compter(_casesReserves, r.X, r.Y, -1);
    }

    public Hutte? HutteEn(int x, int y)
    {
        if (!_casesHuttes.ContainsKey(Case(x, y))) return null;
        foreach (var h in Huttes) if (h.X == x && h.Y == y) return h;
        return null;
    }

    /// <summary>Protection qu'offre une hutte sur cette case (0 sans hutte).</summary>
    public double AbriEn(int x, int y) => HutteEn(x, y) is { } h ? 0.3 + 0.4 * h.Solidite : 0;

    /// <summary>
    /// Allume (ou ranime) un feu sur une case. Une braise qui couvait sur la case reprend flamme : le foyer garde son histoire.
    /// <paramref name="degage"/> : celui qui l'allume a écarté l'herbe et posé des pierres autour.
    /// </summary>
    public Foyer Allumer(int x, int y, double heures, long maintenant, int humainId, long evenementId, bool degage = false)
    {
        if (FoyerEn(x, y) is { } f)
        {
            var ranime = f with { Heures = Math.Min(24, f.Heures + heures), Degage = f.Degage || degage, EntretenuParId = humainId, Couvert = false };
            Foyers[Foyers.IndexOf(f)] = ranime;
            return ranime;
        }
        Foyer nouveau;
        if (BraiseEn(x, y) is { } b)
        {
            Braises.Remove(b);
            Compter(_casesBraises, x, y, -1);
            nouveau = new Foyer(b.Id, x, y, heures, b.AllumeLe, b.AllumeurId, b.EvenementId, Degage: degage, EntretenuParId: humainId);
        }
        else nouveau = new Foyer(_prochainId++, x, y, heures, maintenant, humainId, evenementId, Degage: degage);
        Foyers.Add(nouveau);
        Compter(_casesFoyers, x, y, 1);
        return nouveau;
    }

    /// <summary>Ajoute du bois au feu de la case, ou en allume un s'il n'y en a plus (au plus une journée de combustible d'avance).</summary>
    public void Nourrir(int x, int y, double heures, long maintenant, int humainId, long evenementId, bool degage = false)
    {
        if (FoyerEn(x, y) is { } f) Alimenter(f, heures, humainId, degage);
        else Allumer(x, y, heures, maintenant, humainId, evenementId, degage);
    }

    public void Alimenter(Foyer f, double heures, int humainId = 0, bool degage = false) =>
        Foyers[Foyers.IndexOf(f)] = f with
        {
            // Du bois ajouté découvre le feu : il flambe de nouveau.
            Heures = Math.Min(24, f.Heures + heures), Degage = f.Degage || degage, EntretenuParId = humainId != 0 ? humainId : f.EntretenuParId,
            Couvert = false,
        };

    /// <summary>Couvre de cendre le feu de la case pour la nuit.</summary>
    public void Couvrir(int x, int y)
    {
        if (FoyerEn(x, y) is { Couvert: false } f) Foyers[Foyers.IndexOf(f)] = f with { Couvert = true };
    }

    public Hutte Batir(int x, int y, double solidite, long maintenant, int humainId, long evenementId)
    {
        if (HutteEn(x, y) is { } h)
        {
            var refaite = h with { Solidite = Math.Max(h.Solidite, solidite) };
            Huttes[Huttes.IndexOf(h)] = refaite;
            return refaite;
        }
        var nouvelle = new Hutte(_prochainId++, x, y, solidite, maintenant, humainId, evenementId);
        Huttes.Add(nouvelle);
        Compter(_casesHuttes, x, y, 1);
        return nouvelle;
    }

    /// <summary>
    /// Les feux brûlent leur bois ; ceux qui n'en ont plus laissent une braise, qui couve à son tour puis s'éteint. Une braise
    /// à l'air libre se couvre vite de cendre et tient une heure ou deux ; enfouie sous la cendre exprès, elle passe la nuit
    /// (de six à seize heures ici, selon le bois et le feu : une estimation, faute de mesures sur les foyers de campement).
    /// </summary>
    public void Bruler(double heures)
    {
        for (var i = Braises.Count - 1; i >= 0; i--)
        {
            var b = Braises[i];
            if (b.Heures <= heures)
            {
                Braises.RemoveAt(i);
                Compter(_casesBraises, b.X, b.Y, -1);
            }
            else Braises[i] = b with { Heures = b.Heures - heures };
        }
        for (var i = Foyers.Count - 1; i >= 0; i--)
        {
            var f = Foyers[i];
            if (f.Heures <= heures)
            {
                Foyers.RemoveAt(i);
                Compter(_casesFoyers, f.X, f.Y, -1);
                // Le bois et le feu varient d'un foyer à l'autre : la durée est tirée de son numéro (sans hasard partagé).
                var u = (uint)(f.Id * 2654435761u ^ (uint)f.AllumeLe) / (double)uint.MaxValue;
                var duree = f.Couvert ? 6 + 10 * u : 0.5 + 1.5 * u;
                if (BraiseEn(f.X, f.Y) is null)
                {
                    Braises.Add(new Braise(f.Id, f.X, f.Y, duree, f.Couvert, f.AllumeLe, f.AllumeurId, f.EvenementId));
                    Compter(_casesBraises, f.X, f.Y, 1);
                }
            }
            else Foyers[i] = f with { Heures = f.Heures - heures };
        }
    }

    /// <summary>Une hutte non entretenue s'affaisse en trois à six mois.</summary>
    public void Affaisser(double jours, Func<int, double> usure)
    {
        for (var i = Huttes.Count - 1; i >= 0; i--)
        {
            var h = Huttes[i];
            var reste = h.Solidite - jours * usure(h.Id);
            if (reste <= 0)
            {
                Huttes.RemoveAt(i);
                Compter(_casesHuttes, h.X, h.Y, -1);
            }
            else Huttes[i] = h with { Solidite = reste };
        }
    }

    /// <summary>Éteint les feux d'une case, braise comprise (pluie torrentielle, eau qui monte, terre jetée dessus).</summary>
    public void Eteindre(int x, int y)
    {
        if (Foyers.RemoveAll(f => f.X == x && f.Y == y) > 0) _casesFoyers.Remove(Case(x, y));
        EteindreBraise(x, y);
    }

    /// <summary>Éteint la braise d'une case (la pluie la noie).</summary>
    public void EteindreBraise(int x, int y)
    {
        if (Braises.RemoveAll(b => b.X == x && b.Y == y) > 0) _casesBraises.Remove(Case(x, y));
    }

    public Installations Copier() => new(Foyers, Huttes, Decouvertes, Oeuvres, Braises, Reserves);
}
