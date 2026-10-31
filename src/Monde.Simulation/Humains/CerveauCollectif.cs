using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Le cerveau collectif (depuis la 0.34.1, génération 2) : deux personnes aux savoirs différents, qui vivent au même camp, se
/// rendent visite ou se retrouvent à un rassemblement, peuvent mettre leurs savoirs ensemble. L'idée naît « entre » elles : une pièce
/// ou un geste de l'une, une pièce ou un geste de l'autre. Les deux sont citées, et toutes deux la retiennent. Rien ne dit quoi
/// inventer : les lois jugent comme pour une idée venue d'un seul.
/// <para>
/// Ce que dit la recherche : l'inventivité d'un peuple tient à la taille et aux liens de son réseau social plus qu'au génie de
/// chacun (Henrich, 2004 ; Muthukrishna et Henrich, 2016) ; la complexité des outils de pêche des îles du Pacifique croît avec la
/// population et les contacts (Kline et Boyd, 2010) ; dans les expériences, des groupes plus nombreux et plus reliés améliorent
/// davantage un outil (Derex et al., 2013 ; Derex et Boyd, 2016, où les idées naissent de la rencontre de lignées différentes). Le
/// revers est déjà dans la transmission (copie imparfaite du plus habile, voir <see cref="Heritage"/>, et oubli de ce qui ne sert
/// plus) : un petit groupe isolé perd ce qu'il savait, comme la Tasmanie séparée du continent.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Au calme, avec quelqu'un qui sait d'autres choses : une idée née de leurs deux savoirs (génération 2). Vrai si l'un d'eux a
    /// essayé quelque chose.
    /// </summary>
    private bool IdeeAPlusieurs(Humain h, IReadOnlyList<Humain> tous, double pousse = 1)
    {
        if (!AvecLaMatiere || h.EstEnfant(_maintenant) || h.Savoirs.Count == 0) return false;
        var p = h.Personnalite;
        var ingenieux = 0.2 + Ingeniosite(h);
        // Un seul tirage : la chance se calcule ensuite avec l'autre, sous un plafond (son ingéniosité, un mot commun, ce qu'ils savent à deux).
        var tirage = alea.Reel();
        const double Plafond = 1.2 * 1.5 * 3 * 1.3;
        var sansAutre = 0.00015 * ingenieux * pousse * EspritsFertiles;
        if (tirage >= sansAutre * Plafond) return false;

        // L'autre : quelqu'un d'éveillé sur la même case, qui n'est pas un enfant, qu'on ne déteste pas, et qui sait des choses qu'on ignore.
        var cles = ClesConnues(h);
        var candidats = new List<Humain>();
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.EstEnfant(_maintenant) || o.Activite == Activite.Dormir) continue;
            if (h.AffectionPour(o.Id) < -30 || o.CaptifDe > 0) continue;
            if (!ClesConnues(o).Exists(k => !cles.Contains(k))) continue;
            candidats.Add(o);
        }
        if (candidats.Count == 0) return false;
        var autre = candidats[alea.Entier(candidats.Count)];

        var materiaux = Materiaux(h);
        var (facons, jointures) = GestesConnus(h);
        var (faconsO, jointuresO) = GestesConnus(autre);
        var apports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (f, k) in faconsO)
            if (facons.TryAdd(f, k) && !cles.Contains(k)) apports.Add(k);
        foreach (var (j, k) in jointuresO)
            if (jointures.TryAdd(j, k) && !cles.Contains(k)) apports.Add(k);
        var pieces = PiecesConnues(h, materiaux);
        var deja = new HashSet<string>(pieces.Select(x => x.Piece.Ecrire()), StringComparer.Ordinal);
        foreach (var (x, k) in PiecesConnues(autre, []))
        {
            if (!deja.Add(x.Ecrire())) continue;
            pieces.Add((x, k));
            if (!cles.Contains(k)) apports.Add(k);
        }
        if (apports.Count == 0 || pieces.Count == 0) return false;

        // Le possible adjacent à deux : tout ce qu'ils savent ensemble.
        var ensemble = new HashSet<string>(cles, StringComparer.Ordinal);
        ensemble.UnionWith(ClesConnues(autre));
        var voisinage = Math.Min(3, Math.Log(1 + ensemble.Count) / Math.Log(10));
        var parler = Langues.Partagent(h, autre, Idees.Savoir(Effet.Facture)) ? 1.3 : 0.7;
        // Comme pour une idée seul (voir InventerParCombinaison), avec l'ingéniosité de l'autre et leur curiosité à tous deux.
        var chance = sansAutre * (0.2 + Ingeniosite(autre)) * (0.5 + (p.Ouverture + autre.Personnalite.Ouverture) / 2) * voisinage * parler * Mefiance(h, autre);
        if (tirage >= chance) return false;

        var idee = Imaginer(h, pieces, facons, jointures, LiensDe(h, materiaux));
        // Une idée où rien ne vient de l'autre n'est pas née à deux.
        if (idee is null || !idee.Value.Parents.Exists(apports.Contains)) return false;
        // Ce que l'autre savait déjà faire, il le lui montre : ce n'est pas une idée nouvelle.
        var recette = idee.Value.Facture.Ecrire();
        if (autre.Savoirs.Exists(s => s.Effet == Effet.Facture && Facons.LireObjet(s.Pieces) is { } x && x.Facture.Ecrire() == recette)) return false;
        _autre = autre;
        var besoin = FonctionsUtiles[alea.Entier(FonctionsUtiles.Length)];
        return Essayer(h, idee.Value.Facture, idee.Value.Parents, besoin, OrigineSavoir.Reflexion, tous, autre);
    }

    /// <summary>Les savoirs qui servent à combiner (ceux auxquels il croit), par leur clé.</summary>
    private static List<string> ClesConnues(Humain h)
    {
        var l = new List<string>();
        foreach (var s in h.Savoirs)
            if (s.Effet != Effet.Rien && s.Confiance >= 0.4) l.Add(CleDeSavoir(s));
        return l;
    }

    /// <summary>Une idée à deux, pour les essais : h avec cet autre, poussés par ce besoin.</summary>
    internal bool IdeeAPlusieursPourTest(Humain h, IReadOnlyList<Humain> tous, long maintenant, double pousse)
    {
        _maintenant = maintenant;
        return IdeeAPlusieurs(h, tous, pousse);
    }

    /// <summary>Les régularités d'un jour, pour les essais.</summary>
    internal void RegularitesPourTest(IReadOnlyList<Humain> tous, long maintenant) => Regularites(tous, maintenant);
}
