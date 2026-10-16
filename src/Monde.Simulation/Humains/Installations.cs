namespace Monde.Simulation.Humains;

/// <summary>Un feu entretenu par des humains : il brûle tant qu'on l'alimente.</summary>
/// <param name="Heures">Heures de combustible qui restent.</param>
/// <param name="AllumeurId">Humain qui l'a allumé.</param>
/// <param name="EvenementId">Événement du journal qui en rapporte l'allumage.</param>
public sealed record Foyer(int Id, int X, int Y, double Heures, long AllumeLe, int AllumeurId, long EvenementId);

/// <summary>Un abri de branches et de feuilles : il protège de la pluie et du froid, puis s'affaisse en quelques mois.</summary>
/// <param name="Solidite">De 1 (neuf, bien fait) à 0 (effondré).</param>
public sealed record Hutte(int Id, int X, int Y, double Solidite, long BatieLe, int BatisseurId, long EvenementId);

/// <summary>Première fois qu'un effet a été obtenu dans le monde : par qui, quand, comment.</summary>
/// <param name="Pieces">Pour un objet assemblé : ses pièces (voir <see cref="Assemblage"/>).</param>
public sealed record Decouverte(Effet Effet, int HumainId, long Le, long EvenementId, OrigineSavoir Origine, string Pieces = "");

/// <summary>Ce que les humains ont bâti et allumé dans le monde, et la chronologie de leurs découvertes.</summary>
public sealed class Installations
{
    public List<Foyer> Foyers { get; } = [];
    public List<Hutte> Huttes { get; } = [];
    public List<Decouverte> Decouvertes { get; } = [];
    /// <summary>Images peintes sur la roche : elles restent.</summary>
    public List<Oeuvre> Oeuvres { get; } = [];
    private int _prochainId = 1;
    private int _prochaineOeuvre = 1;

    public Installations(IEnumerable<Foyer>? foyers = null, IEnumerable<Hutte>? huttes = null, IEnumerable<Decouverte>? decouvertes = null,
        IEnumerable<Oeuvre>? oeuvres = null)
    {
        Foyers.AddRange(foyers ?? []);
        Huttes.AddRange(huttes ?? []);
        Decouvertes.AddRange(decouvertes ?? []);
        Oeuvres.AddRange(oeuvres ?? []);
        _prochainId = 1 + Math.Max(Foyers.Select(f => f.Id).DefaultIfEmpty(0).Max(), Huttes.Select(h => h.Id).DefaultIfEmpty(0).Max());
        _prochaineOeuvre = 1 + Oeuvres.Select(o => o.Id).DefaultIfEmpty(0).Max();
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
        foreach (var f in Foyers) if (f.X == x && f.Y == y) return f;
        return null;
    }

    public Hutte? HutteEn(int x, int y)
    {
        foreach (var h in Huttes) if (h.X == x && h.Y == y) return h;
        return null;
    }

    /// <summary>Protection qu'offre une hutte sur cette case (0 sans hutte).</summary>
    public double AbriEn(int x, int y) => HutteEn(x, y) is { } h ? 0.3 + 0.4 * h.Solidite : 0;

    /// <summary>Allume (ou ranime) un feu sur une case.</summary>
    public Foyer Allumer(int x, int y, double heures, long maintenant, int humainId, long evenementId)
    {
        if (FoyerEn(x, y) is { } f)
        {
            var ranime = f with { Heures = Math.Min(24, f.Heures + heures) };
            Foyers[Foyers.IndexOf(f)] = ranime;
            return ranime;
        }
        var nouveau = new Foyer(_prochainId++, x, y, heures, maintenant, humainId, evenementId);
        Foyers.Add(nouveau);
        return nouveau;
    }

    /// <summary>Ajoute du bois au feu de la case (au plus une journée de combustible d'avance).</summary>
    public void Alimenter(Foyer f, double heures) => Foyers[Foyers.IndexOf(f)] = f with { Heures = Math.Min(24, f.Heures + heures) };

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
        return nouvelle;
    }

    /// <summary>Les feux brûlent leur bois ; ceux qui n'en ont plus s'éteignent.</summary>
    public void Bruler(double heures)
    {
        for (var i = Foyers.Count - 1; i >= 0; i--)
        {
            var f = Foyers[i];
            if (f.Heures <= heures) Foyers.RemoveAt(i);
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
            if (reste <= 0) Huttes.RemoveAt(i);
            else Huttes[i] = h with { Solidite = reste };
        }
    }

    /// <summary>Éteint les feux d'une case (pluie torrentielle, eau qui monte).</summary>
    public void Eteindre(int x, int y) => Foyers.RemoveAll(f => f.X == x && f.Y == y);

    public Installations Copier() => new(Foyers, Huttes, Decouvertes, Oeuvres);
}
