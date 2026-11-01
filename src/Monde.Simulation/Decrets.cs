namespace Monde.Simulation;

/// <summary>Les lois chiffrées du monde qu'un décret de l'Entité libre peut changer.</summary>
public enum LoiDecret : byte
{
    /// <summary>Chances de concevoir.</summary>
    Fecondite = 0,
    /// <summary>Longévité : la mortalité de fond est divisée par le facteur.</summary>
    Longevite = 1,
    /// <summary>Force des maladies : chances de tomber malade et de prendre un mal contagieux.</summary>
    Maladies = 2,
    /// <summary>Rendement des plantes : le niveau vers lequel elles repoussent (partout).</summary>
    Plantes = 3,
    /// <summary>Rendement du gibier : ce qu'une case peut nourrir de bêtes (partout).</summary>
    Gibier = 4,
    /// <summary>Rigueur du climat : l'écart de température à 18 °C (froids et chaleurs) est multiplié.</summary>
    Climat = 5,
    /// <summary>Vitesse d'apprentissage : chances de comprendre ce qu'on vous montre.</summary>
    Apprentissage = 6,
    /// <summary>Fréquence des idées (intuitions et combinaisons nées d'elles-mêmes).</summary>
    Idees = 7,
}

/// <summary>
/// Un décret de l'Entité libre : une loi multipliée par <see cref="Facteur"/> (0,1 à 10), pour tout le monde
/// (<see cref="CommunauteId"/> = 0) ou pour une communauté, de <see cref="Debut"/> à <see cref="Fin"/> (0 : pour toujours).
/// </summary>
/// <param name="ActeId">Acte de l'Entité qui l'a décrété (registre des actes ; 0 : inconnu).</param>
public sealed record Decret(int Id, LoiDecret Loi, int CommunauteId, double Facteur, long Debut, long Fin, string Raison, long ActeId = 0)
{
    public bool EnVigueur(long maintenant) => Debut <= maintenant && (Fin == 0 || maintenant < Fin);
}

/// <summary>
/// Les décrets du monde. Sans décret (le cas de tout monde où l'Entité libre n'a rien décrété), chaque facteur vaut exactement 1 :
/// la simulation reste identique au bit près.
/// </summary>
public sealed class Decrets
{
    public const double FacteurMin = 0.1, FacteurMax = 10;

    // Remplacée d'un bloc à chaque changement (sous le verrou du monde) : les pas simultanés la lisent sans verrou.
    private Decret[] _liste;

    public Decrets(IEnumerable<Decret>? decrets = null) => _liste = [.. decrets ?? []];

    public IReadOnlyList<Decret> Liste => _liste;

    /// <summary>Facteur d'une loi pour une communauté (ceux de tout le monde compris) ; exactement 1 sans décret en vigueur.</summary>
    public double Facteur(LoiDecret loi, int communaute, long maintenant)
    {
        var liste = _liste;
        if (liste.Length == 0) return 1;
        var f = 1.0;
        foreach (var d in liste)
            if (d.Loi == loi && (d.CommunauteId == 0 || d.CommunauteId == communaute) && d.EnVigueur(maintenant)) f *= d.Facteur;
        return Math.Clamp(f, FacteurMin, FacteurMax);
    }

    /// <summary>Facteur d'une loi qui vaut pour tout le monde (plantes, gibier, climat).</summary>
    public double Facteur(LoiDecret loi, long maintenant) => Facteur(loi, 0, maintenant);

    internal Decret Ajouter(LoiDecret loi, int communaute, double facteur, long debut, long fin, string raison, long acte)
    {
        var d = new Decret(_liste.Length == 0 ? 1 : _liste.Max(x => x.Id) + 1, loi, communaute, Math.Clamp(facteur, FacteurMin, FacteurMax), debut, fin, raison, acte);
        _liste = [.. _liste, d];
        return d;
    }

    /// <summary>Lève un décret (il cesse maintenant) ; rend l'ancien, ou null s'il n'existe pas ou n'est plus en vigueur.</summary>
    internal Decret? Lever(int id, long maintenant)
    {
        var i = Array.FindIndex(_liste, d => d.Id == id);
        if (i < 0 || !_liste[i].EnVigueur(maintenant)) return null;
        var avant = _liste[i];
        var copie = _liste.ToArray();
        copie[i] = avant with { Fin = Math.Max(avant.Debut, maintenant) };
        _liste = copie;
        return avant;
    }
}
