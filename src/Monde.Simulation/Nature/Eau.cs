using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Nature;

/// <summary>Crues des rivières et tempêtes venues de la mer (cyclones).</summary>
public sealed partial class Catastrophes
{
    // ───────────── Crues ─────────────

    /// <summary>
    /// Chaque jour, les rivières d'une région peuvent déborder : rarement par une année ordinaire (un orage violent),
    /// bien plus souvent par une année très arrosée, surtout à la saison des pluies (printemps).
    /// </summary>
    private void Crues(List<Humain> humains, long maintenant, Saison saison)
    {
        var facteur = saison switch { Saison.Printemps => 1, Saison.Ete => 0.8, Saison.Automne => 0.6, _ => 0.3 };
        for (var r = 0; r < _ecosysteme.Pluies.Length; r++)
        {
            var pluie = _ecosysteme.Pluies[r];
            var p = (0.00005 + 0.002 * Math.Max(0, pluie - 1.1) / 0.3) * facteur;
            if (_alea.Reel() >= p) continue;
            var ampleur = (pluie > 1.5 ? 2 : 1) + (_alea.Reel() < 0.2 ? 1 : 0);
            Crue(r, ampleur, humains, maintenant, Origine.Naturelle, 0);
        }
    }

    /// <summary>
    /// Les rivières d'une région débordent : l'eau couvre les terres basses de leurs rives, sur une ou plusieurs cases
    /// selon l'ampleur, et reste quelques jours. Le plus souvent, elle monte en quelques heures et chacun a le temps de
    /// s'écarter. Parfois, sous un orage violent, c'est une crue subite : un flot dévale le lit de la rivière en moins
    /// d'une heure, et c'est là qu'on se noie (la plupart des morts des crues sont dues aux crues subites ; OMM).
    /// Plus les pluies sont fortes, plus la crue a de chances d'être subite.
    /// </summary>
    private Catastrophe? Crue(int region, int ampleur, List<Humain> humains, long maintenant, Origine origine, long cause)
    {
        var taille = Ecosysteme.TailleRegion;
        int x0 = region % _ecosysteme.RegionsX * taille, y0 = region / _ecosysteme.RegionsX * taille;
        var cases = new HashSet<int>();
        for (var y = Math.Max(0, y0 - 2); y < Math.Min(_carte.Hauteur, y0 + taille + 2); y++)
        {
            for (var x = Math.Max(0, x0 - 2); x < Math.Min(_carte.Largeur, x0 + taille + 2); x++)
            {
                var b = _carte.BiomeEn(x, y);
                if (!Biomes.EstEauDouce(b)) continue;
                var niveau = _carte.Altitudes[_carte.Indice(x, y)] + ampleur;
                for (var dy = -ampleur; dy <= ampleur; dy++)
                {
                    for (var dx = -ampleur; dx <= ampleur; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                        var j = _carte.Indice(nx, ny);
                        var bj = (Biome)_carte.Biomes[j];
                        if (!Climat.EstPraticable(bj) || bj is Biome.Montagne or Biome.SommetEnneige) continue;
                        if (_carte.Altitudes[j] <= niveau) cases.Add(j);
                    }
                }
            }
        }
        if (cases.Count == 0) return null;
        var cx = (int)Math.Round(cases.Average(i => i % _carte.Largeur));
        var cy = (int)Math.Round(cases.Average(i => i / _carte.Largeur));
        var rayon = (int)Math.Ceiling(cases.Max(i => Distance(i % _carte.Largeur, i / _carte.Largeur, cx, cy)));
        var jours = (2 + _alea.Reel() * 5) * ampleur;
        var subite = _alea.Reel() < ampleur switch { 1 => 0.15, 2 => 0.4, _ => 0.7 };
        var texte = new Recit(subite ? "cat.crue.subite" : cause == 0 ? "cat.crue.pluies" : "cat.crue.tempete",
            ("lieu", LieuDe(cx, cy)), ("s", SurfaceR(cases.Count)));
        var c = Commencer(TypeCatastrophe.Crue, cx, cy, rayon, ampleur, new Recit(subite ? "cat.nom.crueSubite" : "cat.nom.crue", ("lieu", LieuDe(cx, cy))),
            texte, humains, maintenant, origine, cause: cause, parametre: subite ? 1 : 0,
            duree: (long)(jours * Calendrier.SecondesParJour), alerter: false);
        c.Cases = cases;
        // Le flot d'une crue subite passe en une à trois heures ; une crue lente monte pendant six à dix-huit heures.
        c.Prochaine = maintenant + (subite ? 1 + _alea.Entier(3) : 6 + _alea.Entier(13)) * Calendrier.SecondesParHeure;
        foreach (var i in cases) Ravager(c, i, 0.5, 0.7, 0.4);
        if (humains.Vivants().Any(h => h.EstVivant && cases.Contains(_carte.Indice(h.X, h.Y)))) Signaler(c, texte);
        return c;
    }

    // ───────────── Cyclones ─────────────

    /// <summary>
    /// Tempêtes tropicales : sous ce climat chaud, une tempête venue de la mer touche les côtes du continent environ une
    /// fois tous les trois ans, en été et en automne (ordre de grandeur des côtes tropicales exposées, NOAA).
    /// La catégorie suit l'échelle de Saffir-Simpson ; les plus violentes sont les plus rares.
    /// </summary>
    private void Cyclones(List<Humain> humains, long maintenant, Saison saison)
    {
        if (saison is not (Saison.Ete or Saison.Automne)) return;
        if (_alea.Reel() >= 0.35 / (2 * Calendrier.JoursParSaison)) return;
        var mer = DistanceMer();
        int i = -1;
        for (var essai = 0; essai < 200 && i < 0; essai++)
        {
            var j = _alea.Entier(mer.Length);
            if (mer[j] == 1 && Climat.EstPraticable((Biome)_carte.Biomes[j])) i = j;
        }
        if (i < 0) return;
        double[] repartition = [0.4, 0.25, 0.18, 0.12, 0.05];
        var tirage = _alea.Reel();
        var categorie = 1;
        while (categorie < 5 && tirage > repartition[categorie - 1]) tirage -= repartition[categorie++ - 1];
        Cyclone(i % _carte.Largeur, i / _carte.Largeur, categorie, humains, maintenant, Origine.Naturelle);
    }

    private Catastrophe Cyclone(int x, int y, int categorie, List<Humain> humains, long maintenant, Origine origine)
    {
        var rayon = 30 + 12 * categorie;
        var heures = 18 + _alea.Entier(31);
        var c = Commencer(TypeCatastrophe.Cyclone, x, y, rayon, categorie, new Recit("cat.nom.cyclone", ("n", categorie), ("ou", Ou(x, y))),
            new Recit("cat.cyclone", ("n", categorie), ("lieu", LieuDe(x, y))),
            humains, maintenant, origine, duree: heures * Calendrier.SecondesParHeure);

        // Les arbres cassent, les fruits tombent, des bêtes périssent.
        foreach (var i in DansLeRayon(x, y, rayon))
        {
            var f = 1 - Distance(i % _carte.Largeur, i / _carte.Largeur, x, y) / rayon;
            Ravager(c, i, 1 - 0.08 * categorie * f, 1 - 0.05 * categorie * f, 0.06 * categorie * f);
        }
        // Onde de tempête : à partir de la catégorie 3, la mer envahit les rivages bas.
        if (categorie >= 3)
        {
            var mer = DistanceMer();
            foreach (var h in humains.Vivants())
            {
                if (!h.EstVivant) continue;
                var i = _carte.Indice(h.X, h.Y);
                if (mer[i] <= 1 + categorie / 2 && _carte.Altitudes[i] <= Carte.NiveauMer + 1 && Distance(h.X, h.Y, x, y) <= rayon / 2.0)
                {
                    Frapper(h, c, 0.15, 0.2, CauseDeMort.Noyade, humains, maintenant);
                }
            }
        }
        // Les pluies de la tempête font souvent déborder les rivières.
        if (_alea.Reel() < 0.5) Crue(_ecosysteme.Region(x, y), 1 + categorie / 3, humains, maintenant, origine, c.Id);
        return c;
    }

    /// <summary>
    /// Une heure de tempête : à découvert, le vent et la pluie glacent ; en forêt, des branches et des arbres tombent.
    /// La plupart des humains cherchent un abri (voir <see cref="Comportement"/>).
    /// </summary>
    private void Souffler(Catastrophe c, List<Humain> humains, long maintenant)
    {
        var categorie = c.Intensite;
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || Distance(h.X, h.Y, c.X, c.Y) > c.Rayon) continue;
            var biome = _carte.BiomeEn(h.X, h.Y);
            var abri = Climat.Abri(biome);
            if (c.Temoins.Add(h.Id)) Effrayer(h, c, 15 + 8 * categorie, maintenant);
            if (abri < 0.4) h.Froid = Math.Min(100, h.Froid + 8 * (1 - abri));
            var foret = biome is Biome.ForetDeConiferes or Biome.ForetTropicale;
            var (mort, blessure) = foret ? (0.0003 * categorie, 0.0015 * categorie) : abri < 0.4 ? (0.0002 * categorie, 0.001 * categorie) : (0, 0);
            if (mort > 0 && _alea.Reel() < blessure + mort) Frapper(h, c, mort / (mort + blessure), 1, CauseDeMort.Accident, humains, maintenant);
        }
    }
}
