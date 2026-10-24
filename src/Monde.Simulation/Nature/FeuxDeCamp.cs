using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Nature;

/// <summary>
/// Les feux de camp qui s'échappent. Un foyer gagne les herbes surtout quand elles sont sèches, que le vent souffle, que personne
/// ne le garde, ou qu'on l'a laissé vif en partant ; un foyer dégagé et entouré de pierres, ou couvert de cendre, ne s'échappe
/// presque jamais. Une fois parti, c'est un incendie comme un autre. Les feux de camp abandonnés comptent aujourd'hui encore
/// parmi les premières causes humaines d'incendie. La fréquence n'est pas fixée : elle découle de ces conditions, et le réglage
/// de départ est une estimation, à vérifier sur les mondes.
/// </summary>
public sealed partial class Catastrophes
{
    /// <summary>Probabilité par heure qu'un foyer gardé, sur des fougères, par un temps et un vent ordinaires, gagne les herbes (estimation).</summary>
    private const double EchappeeParHeure = 1.5e-5;

    /// <summary>
    /// Force du vent du jour dans la région de cette case, de 0,3 (calme) à 2 (grand vent) ; 1,3 et plus, c'est un vent fort.
    /// Il est le même pour tous et ne dépend que du jour et de la région (aucun hasard partagé n'est tiré).
    /// </summary>
    public double VentEn(int x, int y, long maintenant)
    {
        var region = Math.Min(_ecosysteme.RegionsX - 1, x / Ecosysteme.TailleRegion) + _ecosysteme.RegionsX * (y / Ecosysteme.TailleRegion);
        var etat = _carte.Graine ^ 0x56454E54UL ^ ((ulong)(maintenant / Calendrier.SecondesParJour) * 0x9E3779B97F4A7C15UL) ^ ((ulong)region * 0xC2B2AE3D27D4EB4FUL);
        var u = (Alea.SplitMix64(ref etat) & 0xFFFF) / 65536.0;
        return 0.3 + 1.7 * u * u;
    }

    /// <summary>
    /// Chaque heure, chaque foyer peut gagner les herbes autour de lui. Celui qui en avait la garde en est tenu pour responsable,
    /// d'autant plus qu'il a été négligent.
    /// </summary>
    public void FeuxDeCamp(Installations installations, List<Humain> humains, long maintenant)
    {
        if (installations.Foyers.Count == 0) return;
        var date = Calendrier.Decomposer(maintenant);
        var saison = SaisonDesFeux(date.Saison);
        var nuit = date.EstJour ? 1 : 0.5;
        foreach (var f in installations.Foyers.ToList())
        {
            // La case la plus inflammable autour du foyer : c'est là que le feu prendrait.
            var depart = -1;
            var flamme = 0.0;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int x = f.X + dx, y = f.Y + dy;
                    if (x < 0 || y < 0 || x >= _carte.Largeur || y >= _carte.Hauteur) continue;
                    var j = _carte.Indice(x, y);
                    var fl = Inflammabilite((Biome)_carte.Biomes[j]) * Combustible(j);
                    if (fl > flamme) (depart, flamme) = (j, fl);
                }
            }
            if (depart < 0 || flamme < 0.005 || installations.HutteEn(f.X, f.Y) is not null && flamme < 0.02) continue;
            var garde = Garde(f.X, f.Y, humains, maintenant);
            var vent = VentEn(f.X, f.Y, maintenant);
            var sec = Secheresse(_carte.Indice(f.X, f.Y));
            var p = EchappeeParHeure * flamme / 0.05 * sec * saison * nuit * vent * (garde ? 1 : 3) * (f.Degage ? 0.15 : 1) * (f.Couvert ? 0.3 : 1);
            if (_alea.Reel() >= p) continue;
            if (EstDangereuse(depart) || BruleAutour(depart % _carte.Largeur, depart / _carte.Largeur)) continue;
            Echapper(f, depart, garde, vent, sec, humains, maintenant);
        }
    }

    /// <summary>Quelqu'un d'éveillé veille-t-il sur ce feu ?</summary>
    private static bool Garde(int x, int y, List<Humain> humains, long maintenant)
    {
        foreach (var h in humains.Vivants())
            if (h.EstVivant && h.X == x && h.Y == y && !h.EstEnfant(maintenant) && h.Activite != Activite.Dormir) return true;
        return false;
    }

    private void Echapper(Foyer f, int depart, bool garde, double vent, double sec, List<Humain> humains, long maintenant)
    {
        var gardien = f.GardienId > 0 && f.GardienId <= humains.Count && humains[f.GardienId - 1] is { EstVivant: true } g ? g : null;
        var ventFort = vent >= 1.3 && sec >= 0.6;
        // Ce qui l'a fait partir dit ce qu'on peut en apprendre : personne pour le garder, le vent, ou l'herbe trop près.
        var lecon = !garde ? Effet.EteindreEnPartant : ventFort ? Effet.PasDansLeVent : Effet.FoyerDegage;
        double negligence = 0;
        if (gardien is not null)
        {
            negligence = (garde ? 0 : 0.4) + (ventFort ? 0.3 : 0)
                         + (!f.Degage && gardien.Savoirs.Exists(s => s.Effet == Effet.FoyerDegage && s.Confiance >= 0.5) ? 0.2 : 0)
                         + (!garde && gardien.Savoirs.Exists(s => s.Effet == Effet.EteindreEnPartant && s.Confiance >= 0.5) ? 0.2 : 0);
            negligence = Math.Clamp(negligence, 0, 1);
        }
        var comment = Recit.Seul(!garde ? "cat.camp.seul" : ventFort ? "cat.camp.vent" : "cat.camp.herbes");
        var texte = gardien is null
            ? new Recit("cat.camp.feu", ("comment", comment))
            : new Recit("cat.camp.feuDe", ("h", gardien), ("comment", comment));
        var id = Noter(maintenant, gardien, texte, f.EvenementId, significatif: true);
        var c = Allumer(depart % _carte.Largeur, depart / _carte.Largeur, humains, maintenant, Origine.Naturelle, id);
        c.ResponsableId = gardien?.Id ?? 0;
        c.Negligence = negligence;
        c.Lecon = lecon;
        CalculerDangers();
    }

    /// <summary>
    /// Un incendie s'éteint. Parti d'un camp, il a ses suites : la faute, la colère des proches des victimes, les leçons, le souvenir.
    /// Quel qu'il soit, ceux qui l'ont vu s'arrêter net au bord d'une terre déjà brûlée peuvent en comprendre la raison.
    /// </summary>
    private void ApresLIncendie(Catastrophe c, List<Humain> humains, long maintenant)
    {
        if (Comportement is not { } comportement) return;
        if (c.Lecon != Effet.Rien) comportement.ApresLeFeuDeCamp(c, humains, maintenant);
        // Les terres brûlées naguère (par un autre feu) au bord de celles-ci : le feu s'y est arrêté.
        var arrets = 0;
        foreach (var i in c.Cases)
        {
            int x = i % _carte.Largeur, y = i / _carte.Largeur;
            foreach (var (dx, dy) in Voisins)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                var j = _carte.Indice(nx, ny);
                if (c.Cases.Contains(j) || c.Ravages.ContainsKey(j)) continue;
                if (Inflammabilite((Biome)_carte.Biomes[j]) >= 0.01 && _ecosysteme.Degats[j] >= 3000) arrets++;
            }
            if (arrets >= 3) break;
        }
        if (arrets >= 3) comportement.VoirLeFeuSArreter(c, humains, maintenant);
    }

    private static readonly (int Dx, int Dy)[] Voisins = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    /// <summary>
    /// Une fois par jour, à la saison humide, une communauté dont un membre sait que le feu arrête le feu peut brûler un peu les
    /// herbes autour de son camp, si elles ne l'ont pas été depuis longtemps : le feu reste petit, et les grands feux de la saison
    /// sèche s'arrêteront à son bord. Il peut lui aussi s'échapper, mais bien plus rarement.
    /// </summary>
    public void BrulisProtecteurs(IReadOnlyList<Communaute> communautes, List<Humain> humains, long maintenant)
    {
        var saison = Calendrier.Decomposer(maintenant).Saison;
        if (saison is not (Saison.Printemps or Saison.Hiver)) return;
        foreach (var co in communautes)
        {
            if (!co.Active || co.CentreX < 0) continue;
            var centre = _carte.Indice(co.CentreX, co.CentreY);
            if (Secheresse(centre) > 0.9 || Inflammabilite(_carte.BiomeEn(co.CentreX, co.CentreY)) < 0.01) continue;
            // Une fois par saison humide environ, quand le savoir est là.
            if (_alea.Reel() >= 1.0 / Calendrier.JoursParSaison) continue;
            Humain? brule = null;
            foreach (var h in humains.Vivants())
            {
                if (!h.EstVivant || h.CommunauteId != co.Id || h.EstEnfant(maintenant) || Distance(h.X, h.Y, co.CentreX, co.CentreY) > 2) continue;
                if (!h.Savoirs.Exists(s => s.Effet == Effet.BrulisProtecteur && s.Confiance >= 0.5)) continue;
                if (brule is null || h.Personnalite.Conscience > brule.Personnalite.Conscience) brule = h;
            }
            if (brule is null) continue;
            // Des terres déjà brûlées autour du camp n'ont pas besoin de l'être encore.
            var cases = new List<int>();
            var dejaBrulees = 0;
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    int x = co.CentreX + dx, y = co.CentreY + dy;
                    if (x < 0 || y < 0 || x >= _carte.Largeur || y >= _carte.Hauteur || Math.Abs(dx) + Math.Abs(dy) < 2) continue;
                    var j = _carte.Indice(x, y);
                    if (Inflammabilite((Biome)_carte.Biomes[j]) < 0.01) continue;
                    cases.Add(j);
                    if (_ecosysteme.Degats[j] >= 3000) dejaBrulees++;
                }
            }
            if (cases.Count == 0 || dejaBrulees * 2 >= cases.Count) continue;
            foreach (var j in cases)
            {
                // Un feu doux de saison humide : il brûle l'herbe sèche et les broussailles, épargne les arbres et le gibier.
                _ecosysteme.Degats[j] = (ushort)Math.Max(_ecosysteme.Degats[j], 3500 + _alea.Entier(2000));
                _ecosysteme.Plantes[j] = (ushort)(_ecosysteme.Plantes[j] * 0.8);
            }
            var id = Noter(maintenant, brule, new Recit("cat.camp.brulis", ("h", brule)),
                communaute: co.Id);
            Comportement?.AvoirBruleAutourDuCamp(brule, humains, maintenant);
            // Même petit, il peut gagner plus loin s'il fait plus sec qu'on ne croyait.
            var vent = VentEn(co.CentreX, co.CentreY, maintenant);
            if (_alea.Reel() < 0.02 * Secheresse(centre) * vent)
            {
                var j = cases[_alea.Entier(cases.Count)];
                var c = Allumer(j % _carte.Largeur, j / _carte.Largeur, humains, maintenant, Origine.Naturelle, id);
                c.ResponsableId = brule.Id;
                c.Negligence = 0.1;
                c.Lecon = vent >= 1.3 ? Effet.PasDansLeVent : Effet.FoyerDegage;
                CalculerDangers();
            }
        }
    }
}
