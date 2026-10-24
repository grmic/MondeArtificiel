using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Nature;

/// <summary>Incendies : allumés par la foudre, poussés par le vent, éteints par la pluie ou faute de combustible.</summary>
public sealed partial class Catastrophes
{
    /// <summary>
    /// Probabilité par heure qu'une case en feu enflamme une case voisine de ce terrain, par temps normal :
    /// la lande sèche, les fougères et les conifères brûlent vite, les feuillus
    /// et la forêt tropicale humide mal, le marais et la mangrove presque pas.
    /// </summary>
    private static double Inflammabilite(Biome b) => b switch
    {
        Biome.Lande => 0.06,
        Biome.PlaineDeFougeres => 0.05,
        Biome.ForetDeConiferes => 0.045,
        Biome.ForetDeFeuillus => 0.02,
        Biome.ForetTropicale => 0.012,
        Biome.Montagne => 0.01,
        Biome.Marais or Biome.Desert or Biome.Mangrove => 0.004,
        _ => 0,
    };

    /// <summary>Saison des feux : la plupart des feux de foudre éclatent en été, quand la végétation est sèche.</summary>
    private static double SaisonDesFeux(Saison s) => s switch
    {
        Saison.Printemps => 0.5,
        Saison.Ete => 1.3,
        Saison.Automne => 1.0,
        _ => 0.25,
    };

    /// <summary>Sécheresse de la case : 1 par une année normale, jusqu'à 2 pendant une sécheresse, 0,15 par une année très arrosée.</summary>
    private double Secheresse(int i) => Math.Clamp((1.35 - _ecosysteme.PluieEn(i)) / 0.35, 0.15, 2);

    /// <summary>Combustible : la végétation qui reste (une terre déjà brûlée ne brûle plus).</summary>
    private double Combustible(int i)
    {
        var naturel = _carte.Plantes[i] * 100.0;
        var plein = naturel <= 0 ? 0.3 : Math.Min(1, _ecosysteme.Plantes[i] / (0.5 * naturel));
        return plein * (1 - _ecosysteme.Degats[i] / 10_000.0);
    }

    /// <summary>
    /// Chaque jour, la foudre peut allumer un feu dans chaque région : surtout en été, bien plus pendant une sécheresse.
    /// Réglé pour qu'une forêt brûle en moyenne une fois par siècle ou deux, comme dans les forêts sèches et les savanes
    /// non entretenues par l'homme (intervalles de retour du feu de 50 à 300 ans).
    /// </summary>
    private void Feux(List<Humain> humains, long maintenant, Saison saison)
    {
        var taille = Ecosysteme.TailleRegion;
        for (var r = 0; r < _ecosysteme.Pluies.Length; r++)
        {
            var x0 = r % _ecosysteme.RegionsX * taille;
            var y0 = r / _ecosysteme.RegionsX * taille;
            var centre = _carte.Indice(Math.Min(_carte.Largeur - 1, x0 + taille / 2), Math.Min(_carte.Hauteur - 1, y0 + taille / 2));
            if (_alea.Reel() >= 0.0003 * SaisonDesFeux(saison) * Secheresse(centre)) continue;
            var i = TerreAuHasard(x0, y0, x0 + taille - 1, y0 + taille - 1, b => Inflammabilite(b) >= 0.01);
            if (i < 0 || EstDangereuse(i)) continue;
            Allumer(i % _carte.Largeur, i / _carte.Largeur, humains, maintenant, Origine.Naturelle, 0);
        }
    }

    /// <summary>
    /// Un humain met le feu aux herbes sèches (<paramref name="cause"/> : l'événement qui le rapporte). Le feu ne prend que
    /// sur une terre qui peut brûler et qui ne brûle pas déjà ; ensuite, il va où le vent et la sécheresse le poussent.
    /// </summary>
    public void MettreLeFeu(int x, int y, List<Humain> humains, long maintenant, long cause)
    {
        var i = _carte.Indice(x, y);
        if (Inflammabilite(_carte.BiomeEn(x, y)) < 0.01 || EstDangereuse(i) || BruleAutour(x, y)) return;
        Allumer(x, y, humains, maintenant, Origine.Naturelle, cause);
        CalculerDangers();
    }

    private Catastrophe Allumer(int x, int y, List<Humain> humains, long maintenant, Origine origine, long cause)
    {
        var i = _carte.Indice(x, y);
        var ou = Ou(x, y);
        var biome = _carte.BiomeEn(x, y);
        var quoi = Recit.Seul(biome is Biome.PlaineDeFougeres or Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.Montagne or Biome.Marais
                                         or Biome.ForetDeFeuillus or Biome.Mangrove or Biome.Lande
            ? $"cat.feu.quoi.{biome}" : "cat.feu.quoi.broussailles");
        var texte = new Recit(cause == 0 ? "cat.feu.foudre" : "cat.feu.prend", ("quoi", quoi), ("ou", ou));
        var c = Commencer(TypeCatastrophe.Incendie, x, y, 1, 0, new Recit("cat.nom.incendie", ("ou", ou)), texte, humains, maintenant, origine,
            direction: _alea.Reel() * 2 * Math.PI, cause: cause);
        c.Front[i] = maintenant + (2 + _alea.Entier(4)) * Calendrier.SecondesParHeure;
        Ravager(c, i, 0, 0.3, 0.9);
        return c;
    }

    /// <summary>Une heure de feu : chaque case en feu peut enflammer ses voisines (plus vite sous le vent, moins la nuit), puis s'éteint.</summary>
    private void Propager(Catastrophe c, List<Humain> humains, long maintenant, DateMonde date)
    {
        var nuit = date.EstJour ? 1 : 0.5;
        var saison = SaisonDesFeux(date.Saison);
        var nouvelles = new SortedSet<int>();
        foreach (var (i, fin) in c.Front.OrderBy(f => f.Key).ToList())
        {
            int x = i % _carte.Largeur, y = i / _carte.Largeur;
            var sec = Secheresse(i);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                    var j = _carte.Indice(nx, ny);
                    if (c.Front.ContainsKey(j) || c.Cases.Contains(j) || nouvelles.Contains(j)) continue;
                    var flamme = Inflammabilite((Biome)_carte.Biomes[j]);
                    if (flamme == 0) continue;
                    var vent = 1 + 0.7 * Math.Cos(Math.Atan2(dy, dx) - c.Direction);
                    if (_alea.Reel() < flamme * sec * saison * nuit * vent * Combustible(j)) nouvelles.Add(j);
                }
            }
            if (maintenant >= fin)
            {
                c.Front.Remove(i);
                c.Cases.Add(i);
            }
        }
        foreach (var j in nouvelles)
        {
            c.Front[j] = maintenant + (2 + _alea.Entier(4)) * Calendrier.SecondesParHeure;
            Ravager(c, j, 0, 0.3, 0.9);
        }
        c.Rayon = Math.Max(c.Rayon, nouvelles.Select(j => (int)Math.Ceiling(Distance(j % _carte.Largeur, j / _carte.Largeur, c.X, c.Y))).DefaultIfEmpty(0).Max());

        // Ceux qui voient les flammes de près s'en souviendront : la première rencontre avec le feu.
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.EstBebe(maintenant)) continue;
            if (!_danger.TryGetValue(_carte.Indice(h.X, h.Y), out var d) || d.Source != c || !c.Temoins.Add(h.Id)) continue;
            Signaler(c, new Recit("cat.feu.menace", ("lieu", Lieu(c))));
            Noter(maintenant, h, new Recit("cat.feu.voit", ("h", h)), c.Id, type: TypeEvenement.Pensee);
            Effrayer(h, c, 15, maintenant);
        }

        if (c.Front.Count == 0 || maintenant - c.Debut > 60 * Calendrier.SecondesParJour) Terminer(c, humains, maintenant);
    }

    /// <summary>Chaque jour, la pluie peut éteindre les feux en cours (plus souvent là où il pleut beaucoup).</summary>
    private void PluieSurLesFeux(long maintenant)
    {
        foreach (var c in _liste)
        {
            if (!c.Active || c.Type != TypeCatastrophe.Incendie || c.Front.Count == 0) continue;
            if (_alea.Reel() >= 0.22 * _ecosysteme.PluieEn(_carte.Indice(c.X, c.Y))) continue;
            foreach (var i in c.Front.Keys) c.Cases.Add(i);
            c.Front.Clear();
            Noter(maintenant, null, new Recit("cat.feu.pluie", ("lieu", Lieu(c))), c.Id);
        }
    }
}
