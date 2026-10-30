using Monde.Simulation.Chimie;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Remarquer un gisement en passant. Les chasseurs-cueilleurs connaissaient intimement le sous-sol qu'ils parcouraient :
/// croûtes de sel, suintements de bitume, terres colorées, pierres vertes ou lourdes. Rien n'en est encore tiré ici :
/// le gisement est vu, retenu comme un lieu, et le journal dit qui l'a vu le premier. Ce qu'on en fera viendra des humains.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Chance, en une heure passée sur la case (800 m de côté, dont le gisement n'occupe qu'une partie), de remarquer un
    /// gisement qui affleure, pour un esprit moyen : une croûte de sel ou une mare de bitume sautent aux yeux (et au nez),
    /// quelques pépites de cuivre ou des grains d'étain beaucoup moins. Sous la surface, on ne voit que des indices (une
    /// source salée, une terre teintée de vert) : dix fois moins souvent.
    /// </summary>
    private static double VisibiliteParHeure(Gite type) => type switch
    {
        Gite.Sel => 0.5,
        Gite.Soufre => 0.5,
        Gite.Bitume => 0.4,
        Gite.Malachite => 0.15,
        Gite.Kaolin => 0.15,
        Gite.Charbon => 0.1,
        Gite.FerDesMarais => 0.08,
        Gite.CuivreNatif => 0.03,
        Gite.Or => 0.03,
        Gite.Cassiterite => 0.01,
        _ => 0.03,
    };

    /// <summary>Gisements dont au moins un vivant se souvient, au début du pas (lus pendant le pas, jamais modifiés).</summary>
    private readonly HashSet<int> _gisementsConnus = [];

    /// <summary>Avant chaque pas : quels gisements les vivants connaissent-ils ?</summary>
    internal void RecenserLesGisements(IReadOnlyList<Humain> tous)
    {
        _gisementsConnus.Clear();
        foreach (var h in tous)
        {
            if (!h.EstVivant) continue;
            foreach (var l in h.Memoire.Lieux)
            {
                if (l.Type == TypeLieu.Gisement && carte.Terrain.GisementA(l.X, l.Y) is { } g) _gisementsConnus.Add(g.Numero);
            }
        }
    }

    /// <summary>
    /// L'humain en cours, éveillé, remarque peut-être le gisement de sa case. Un esprit curieux et vif le voit plus souvent ;
    /// un enfant aussi le voit, mais seulement passé l'âge de courir seul.
    /// </summary>
    internal void RemarquerLeGisement(Humain h) => Remarquer(h);

    private void Remarquer(Humain h)
    {
        if (h.Activite == Activite.Dormir || h.CaptifDe != 0) return;
        if (carte.Terrain.GisementA(h.X, h.Y) is not { } g || g.Profondeur == Enfouissement.Profond) return;
        if (h.AgeEnAnnees(_maintenant) < h.AgeAutonomie) return;
        if (h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.Gisement && Math.Max(Math.Abs(l.X - g.X), Math.Abs(l.Y - g.Y)) < 3)) return;
        var heure = (TachesVertes(g) ? 0.5 * VisibiliteParHeure(Gite.Malachite) : VisibiliteParHeure(g.Type) * (g.Profondeur == Enfouissement.Affleurant ? 1 : 0.1))
                    * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h));
        var pas = MondeSimule.SecondesParPas / (double)Calendrier.SecondesParHeure;
        if (alea.Reel() >= 1 - Math.Pow(1 - Math.Min(0.95, heure), pas)) return;

        // Premier à le voir, ou premier depuis que tous ceux qui le connaissaient l'ont oublié ou sont morts.
        var connu = _gisementsConnus.Contains(g.Numero);
        h.Memoire.Retenir(TypeLieu.Gisement, g.X, g.Y, _maintenant, Aptitude(h));
        var quoi = g.Type.ToString();
        var cle = g.Profondeur == Enfouissement.Affleurant ? $"gis.vu.{quoi}" : $"gis.indice.{quoi}";
        Noter(_maintenant, TypeEvenement.Decouverte, h, null, new Recit(cle, ("h", h), ("x", g.X), ("y", g.Y)), significatif: !connu);
        h.Memoire.Penser(_maintenant, new Recit($"gis.pense.{quoi}"));
        // Une pierre qui attire l'œil, on la ramasse souvent sur-le-champ.
        if (AvecLaMatiere && APorteeDeMain(h, g) && Attrait(g.Type) is var attrait && attrait > 0 && PeutEmporter(h, g)
            && alea.Reel() < attrait * (0.2 + h.Personnalite.Ouverture) * 0.6)
            Emporter(h, g);
    }

    /// <summary>
    /// Ce qui attire la main dans un gisement qui affleure : la pierre verte et le cuivre qui luit, l'or, plus que les graviers
    /// noirs de l'étain. On ramassait les pierres colorées pour le pigment et la parure bien avant d'en tirer le métal : perles
    /// de malachite de Shanidar, vers 10 000 ans (Solecki, 1969), pigments verts du Néolithique.
    /// </summary>
    private static double Attrait(Gite type) => type switch
    {
        Gite.Malachite => 0.8,
        Gite.CuivreNatif => 0.9,
        Gite.Or => 0.9,
        Gite.Cassiterite => 0.3,
        _ => 0,
    };

    /// <summary>
    /// Sur un gisement qu'il connaît et qui affleure, un curieux ramasse parfois un morceau de la pierre et l'emporte (mondes où
    /// la matière a sa chimie). C'est elle qu'il frappera, mêlera, mettra au feu, loin du gisement.
    /// </summary>
    internal void RamasserUnePierre(Humain h)
    {
        if (!AvecLaMatiere || h.Activite == Activite.Dormir || h.CaptifDe != 0) return;
        if (carte.Terrain.GisementA(h.X, h.Y) is not { } g || !APorteeDeMain(h, g) || Attrait(g.Type) is var attrait && attrait <= 0) return;
        if (h.AgeEnAnnees(_maintenant) < h.AgeAutonomie || !h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.Gisement && Math.Max(Math.Abs(l.X - g.X), Math.Abs(l.Y - g.Y)) < 3)) return;
        if (!PeutEmporter(h, g)) return;
        // Une chance sur vingt environ par heure passée là, pour un esprit ouvert ; plus pour qui peint et cherche des couleurs.
        var heure = 0.05 * attrait * (0.2 + h.Personnalite.Ouverture) * (g.Type == Gite.Malachite && Humains.Arts.Connait(h, Effet.Ocre) ? 1.5 : 1);
        var pas = MondeSimule.SecondesParPas / (double)Calendrier.SecondesParHeure;
        if (alea.Reel() >= 1 - Math.Pow(1 - heure, pas)) return;
        Emporter(h, g);
    }

    /// <summary>
    /// Un gisement de cuivre sous la surface se trahit par des taches vertes sur la roche : la malachite et l'azurite du
    /// « chapeau » oxydé, que les prospecteurs suivent depuis toujours (Guilbert et Park, 1986). On les voit bien plus souvent
    /// que le gisement lui-même (mondes où la matière a sa chimie).
    /// </summary>
    private bool TachesVertes(Gisement g) =>
        AvecLaMatiere && g.Profondeur == Enfouissement.SousLaSurface && g.Type is Gite.Malachite or Gite.CuivreNatif;

    /// <summary>
    /// La pierre est-elle à portée de main : elle affleure, ou ce sont des taches vertes et l'humain sait creuser le sol (bâton à
    /// fouir) — sous les taches, à quelques pieds, la pierre verte.
    /// </summary>
    internal bool APorteeDeMain(Humain h, Gisement g) =>
        g.Profondeur == Enfouissement.Affleurant || TachesVertes(g) && h.Sait(Effet.Tubercules);

    /// <summary>A-t-il la place, et pas déjà un morceau de cette pierre ?</summary>
    private static bool PeutEmporter(Humain h, Gisement g)
    {
        if (h.Objets.Count >= ObjetsMax) return false;
        var phase = g.Type is Gite.CuivreNatif ? Phase.Cuivre : g.Type is Gite.Malachite ? Phase.Malachite : (Phase?)null;
        return !h.Objets.Exists(o => o.Matiere == Matiere.Substance && Substance.Lire(o.Pieces) is { } x
                                     && (phase is { } p ? x.Part(p) > 0.1 : x.Structure == Structure.Roche));
    }

    private void Emporter(Humain h, Gisement g)
    {
        var morceau = Matieres.Minerai(g.Type) with { Masse = 0.1 + 0.3 * alea.Reel() };
        Prendre(h, new Objet(Matiere.Substance, 0.5, 1, Pieces: morceau.Arrondie.Signature));
        if (JournalQuotidien) Noter(_maintenant, TypeEvenement.Pensee, h, null, new Recit("gis.ramasse", ("h", h), ("a", Perception.Nom(morceau))));
    }
}
