using Monde.Simulation.Chimie;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Cuire dans l'eau (mondes de la génération 2, depuis la 0.32.6) : des racines et des graines mises dans un pot d'eau posé sur
/// le feu, ou dans une outre, une calebasse, un creux garni de peau où l'on jette des pierres chaudes. Personne ne le sait au
/// départ : on le trouve en laissant un pot plein près des braises, ou en jetant des pierres chaudes dans l'eau où trempent des
/// racines, et l'on n'en retient que ce qu'on voit (« ce qu'on y a mis s'attendrit et se mange »).
/// <para>
/// Ce que la cuisson change, et que la simulation suit : l'amidon cuit (vers 60-70 °C) se digère bien mieux, et une racine dure,
/// une graine, une plante amère qu'on laissait deviennent un repas (Carmody et Wrangham, 2009 ; Carmody et al., 2011) ; l'eau
/// jetée emporte une part des poisons solubles (glands, racines amères : Johns et Kubo, 1988) ; les os bouillis rendent leur
/// graisse (Paléolithique supérieur : Manne et al., 2012) ; le bouillon se boit ; une bouillie nourrit un petit enfant, et la mère
/// qui en fait sèvre plus tôt, ce qui rapproche les naissances (Bocquet-Appel, 2011). Les plus anciens pots, en Asie de l'Est
/// vers 20 000 ans, ont servi à cuire (Craig et al., 2013). La pierre chaude jetée dans l'eau est bien plus ancienne.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Façon de cuire d'une recette : dans un creux de terre cuite plein d'eau, posé sur le feu (voir <see cref="ChoisirMode"/>).</summary>
    internal const int PotSurLeFeu = 5;

    /// <summary>
    /// Ce qu'il reste sur une case quand le cueilleur qui mange cru la laisse (moins de 1 000) : racines dures, graines, plantes
    /// amères. Bouillies, elles nourrissent encore, jusque vers 300.
    /// </summary>
    private const int PlantesABouillir = 300;

    /// <summary>La case (celle-ci ou une voisine) où il reste le plus de quoi bouillir (−1 : rien).</summary>
    private int CaseABouillir(Humain h)
    {
        var (meilleure, plus) = (-1, PlantesABouillir - 1);
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var (x, y) = (h.X + dx, h.Y + dy);
            if (x < 0 || y < 0 || x >= carte.Largeur || y >= carte.Hauteur) continue;
            var i = carte.Indice(x, y);
            if (ecosysteme.Plantes[i] > plus) (meilleure, plus) = (i, ecosysteme.Plantes[i]);
        }
        return meilleure;
    }

    /// <summary>Le savoir le plus sûr qui fait cuire dans l'eau (au pot ou à la pierre chaude).</summary>
    private static Savoir? SavoirDeLaCuisine(Humain h, bool? pierres = null) =>
        h.Savoirs.Where(s => s.Effet == Effet.Transformation && s.Confiance >= 0.3 && LireRecette(s.Pieces) is { } r && r.Changements.Contains("tendre")
            && (pierres is null || pierres == (r.Geste == Geste.Plonger))).MaxBy(s => s.Confiance);

    /// <summary>
    /// Une femme qui fait bouillir (elle le sait, et l'a fait ces deux derniers mois) a de quoi nourrir son petit d'une bouillie :
    /// elle le sèvre plus tôt (voir <see cref="Allaitement"/>).
    /// </summary>
    internal static bool FaitDesBouillies(Humain h, long maintenant) =>
        h.Savoirs.Exists(s => s.Effet == Effet.Transformation && s.Confiance >= 0.3 && s.UtiliseLe >= maintenant - 60 * Calendrier.SecondesParJour
            && LireRecette(s.Pieces) is { } r && r.Changements.Contains("tendre"));

    /// <summary>Un pot de terre cuite qui garde l'eau et n'est pas fendu (−1 : aucun).</summary>
    private int PotPourBouillir(Humain h) =>
        h.Objets.FindIndex(o => PotEtanche(o) && Substance.Lire(o.Pieces) is { Fissures: < 1 });

    /// <summary>
    /// Pour bouillir à la pierre chaude : un récipient (calebasse, outre, pot) ou une peau, ou de grandes feuilles pour garnir un
    /// creux du sol ; et la pierre qu'on chauffe (Matiere.Aucune : il n'en a pas).
    /// </summary>
    private Matiere PierreABouillir(Humain h)
    {
        if (!h.Objets.Exists(o => Recipient(o) || o.Matiere == Matiere.Peau) && !APortee(h, Matiere.GrandesFeuilles)) return Matiere.Aucune;
        foreach (var m in (ReadOnlySpan<Matiere>)[Matiere.Galet, Matiere.Calcaire, Matiere.Silex])
            if (APortee(h, m)) return m;
        return Matiere.Aucune;
    }

    /// <summary>Le feu de la case est-il là, et de quoi bouillir à portée ?</summary>
    private bool PeutBouillir(Humain h, bool pierres)
    {
        if (Installations.FoyerEn(h.X, h.Y) is null && Installations.BraiseEn(h.X, h.Y) is null) return false;
        if (CaseABouillir(h) < 0) return false;
        if (pierres) return EauDouceAPortee(h.X, h.Y) && PierreABouillir(h) != Matiere.Aucune;
        var pot = PotPourBouillir(h);
        return pot >= 0 && (h.Objets[pot].Plein || EauDouceAPortee(h.X, h.Y));
    }

    /// <summary>
    /// Faire bouillir des racines et des graines, au pot sur le feu ou à la pierre chaude, et les manger. Le savoir naît de ce
    /// qu'on voit ; un pot mal cuit ou fêlé peut se fendre sur le feu (choc thermique : Rice, 1987). Renvoie Transformation si
    /// l'on a mangé.
    /// </summary>
    internal Effet Bouillir(Humain h, bool pierres, OrigineSavoir origine, IReadOnlyList<Humain> tous, Humain? maitre = null)
    {
        var pierre = pierres ? PierreABouillir(h) : Matiere.Aucune;
        var (g, a, mode) = pierres ? (Geste.Plonger, pierre, 0) : (Geste.Cuire, Matiere.Plante, PotSurLeFeu);
        if (!PeutBouillir(h, pierres)) return Rien(h, g, a, Matiere.Eau);
        var j = CaseABouillir(h);
        if (!pierres)
        {
            var i = PotPourBouillir(h);
            var pot = Substance.Lire(h.Objets[i].Pieces)!;
            // Une terre peu frittée, poreuse comme il faut, supporte mieux le feu qu'une terre trop dense ; une fêlure s'ouvre.
            if (alea.Reel() < 0.02 + 0.15 * (1 - pot.Frittage) + 0.4 * pot.Fissures + 0.1 * Math.Max(0, 0.15 - pot.Porosite) / 0.15)
            {
                h.Objets.RemoveAt(i);
                if (JournalQuotidien) Noter(_maintenant, TypeEvenement.Journee, h, null, new Recit("cuis.fendu", ("h", h)));
                return Rien(h, g, a, Matiere.Eau);
            }
            h.Objets[i] = h.Objets[i] with { Plein = false };
        }
        // Le gain de la cuisson varie avec ce qu'on met dans l'eau : un peu pour des feuilles, beaucoup pour une racine farineuse.
        var gain = Math.Clamp(1.35 * Math.Exp(0.15 * Biologie.Normale(alea)), 1.1, 1.9);
        var petits = PetitsAuFeu(h, tous);
        var faim = h.Faim;
        var parts = Math.Clamp((int)Math.Ceiling(h.Faim / (2.5 * gain)), 1, 6) + petits.Count;
        var pris = Ecosysteme.Prelever(ecosysteme.Plantes, j, parts);
        if (pris == 0) return Rien(h, g, a, Matiere.Eau);
        var pourLui = Math.Max(1, pris - petits.Count);
        h.Faim = Math.Max(0, h.Faim - pourLui * 2.5 * gain);
        // Le bouillon se boit : de l'eau qui a bouilli.
        h.Soif = Math.Max(0, h.Soif - (4 + 8 * alea.Reel()));
        // Les os d'une bête qu'on porte rendent leur graisse dans l'eau qui bout (une fois par carcasse).
        var os = h.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse && o.Pieces != "os");
        if (os >= 0)
        {
            h.Objets[os] = h.Objets[os] with { Pieces = "os" };
            h.Reserves = Math.Min(100, h.Reserves + 2 + 3 * alea.Reel());
            h.Faim = Math.Max(0, h.Faim - 5);
        }
        // Une bouillie pour les petits qui sont là.
        for (var k = 0; k < Math.Min(petits.Count, pris - pourLui); k++)
        {
            var e = petits[k];
            var donne = Math.Min(e.Faim - 5, 2.5 * gain);
            if (donne <= 0) continue;
            Tour.Changer(e, x => x.Faim = Math.Max(0, x.Faim - donne));
        }
        h.Activite = Activite.Cueillir;
        h.CibleX = h.CibleY = -1;
        var changements = pierres ? new[] { "bout", "tendre" } : ["tendre"];
        var pieces = Recette(g, a, Matiere.Eau, mode, changements);
        var recit = pierres ? new Recit("cuis.pierres", ("h", h), ("a", Physique.Nom(pierre))) : new Recit("cuis.pot", ("h", h));
        if (Remarquer(h, origine, g, a, Matiere.Eau, Effet.Transformation, pieces, faim > 30))
        {
            var ev = Retenir(h, g, a, Matiere.Eau, Effet.Transformation, 0, Symptome.Aucun, origine, maitre?.Id ?? 0, tous, pieces: pieces, recit: recit);
            if (origine != OrigineSavoir.Imitation) Montrer(h, g, a, Matiere.Eau, Effet.Transformation, 0, Symptome.Aucun, tous, ev, pieces);
        }
        if (JournalQuotidien) Noter(_maintenant, TypeEvenement.Journee, h, null, new Recit("cuis.mange", ("h", h)));
        return Effet.Transformation;
    }

    /// <summary>Ses petits enfants (sevrés, pas encore autonomes) qui ont faim, près du feu.</summary>
    private List<Humain> PetitsAuFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        var l = new List<Humain>();
        var debut = _maintenant - 7 * Calendrier.SecondesParAn;
        for (var i = tous.Count - 1; i >= 0 && l.Count < 2; i--)
        {
            var e = tous[i];
            if (e.Naissance < debut) break;
            if (!e.EstVivant || e.Faim <= 15 || e.AgeEnAnnees(_maintenant) < 0.5 || !e.EstPetitEnfant(_maintenant) || Distance(h.X, h.Y, e.X, e.Y) > 1) continue;
            if (e.MereId == h.Id || e.PereId == h.Id) l.Add(e);
        }
        return l;
    }

    /// <summary>
    /// La faim presse, et l'on est au feu : qui sait cuire dans l'eau fait bouillir des racines, au pot s'il en a un et le sait,
    /// sinon à la pierre chaude. Vrai s'il l'a fait.
    /// </summary>
    internal bool Cuisiner(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!AvecLaMatiere || SavoirDeLaCuisine(h) is null) return false;
        var pot = SavoirDeLaCuisine(h, false) is not null && PeutBouillir(h, false);
        if (!pot && (SavoirDeLaCuisine(h, true) is null || !PeutBouillir(h, true))) return false;
        Bouillir(h, !pot, OrigineSavoir.Essai, tous);
        return true;
    }
}
