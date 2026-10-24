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
    private void Remarquer(Humain h)
    {
        if (h.Activite == Activite.Dormir || h.CaptifDe != 0) return;
        if (carte.Terrain.GisementA(h.X, h.Y) is not { } g || g.Profondeur == Enfouissement.Profond) return;
        if (h.AgeEnAnnees(_maintenant) < h.AgeAutonomie) return;
        if (h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.Gisement && Math.Max(Math.Abs(l.X - g.X), Math.Abs(l.Y - g.Y)) < 3)) return;
        var heure = VisibiliteParHeure(g.Type) * (g.Profondeur == Enfouissement.Affleurant ? 1 : 0.1)
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
    }
}
