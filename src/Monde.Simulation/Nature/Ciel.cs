using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Nature;

/// <summary>Vagues de froid, canicules et phénomènes du ciel (éclipses, comètes, bolides, aurores, impacts).</summary>
public sealed partial class Catastrophes
{
    // ───────────── Froid et chaleur ─────────────

    /// <summary>
    /// Vagues de froid en hiver et canicules en été, sur tout le continent (une masse d'air couvre bien plus
    /// que ses 200 km) : environ une tous les trois ans chacune, de 5 à 12 °C sous ou de 4 à 10 °C au-dessus
    /// des normales pendant quelques jours à deux semaines (ordres de grandeur des vagues de froid et de chaleur
    /// observées sous les climats tempérés chauds).
    /// </summary>
    private void Temperatures(List<Humain> humains, long maintenant, Saison saison)
    {
        if (_liste.Any(c => c.Active && c.Type is TypeCatastrophe.VagueDeFroid or TypeCatastrophe.Canicule)) return;
        if (saison == Saison.Hiver && _alea.Reel() < 0.35 / Calendrier.JoursParSaison)
        {
            VagueDeFroid(-(5 + 7 * _alea.Reel()), 3 + _alea.Reel() * 7, humains, maintenant, Origine.Naturelle);
        }
        else if (saison == Saison.Ete && _alea.Reel() < 0.35 / Calendrier.JoursParSaison)
        {
            Canicule(4 + 6 * _alea.Reel(), 4 + _alea.Reel() * 8, humains, maintenant, Origine.Naturelle);
        }
    }

    private Catastrophe VagueDeFroid(double ecart, double jours, List<Humain> humains, long maintenant, Origine origine) =>
        Commencer(TypeCatastrophe.VagueDeFroid, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, ecart,
            new Recit("cat.nom.froid", ("t", DecimalR(ecart))),
            new Recit("cat.froid", ("t", DecimalR(-ecart))),
            humains, maintenant, origine, duree: (long)(jours * Calendrier.SecondesParJour));

    private Catastrophe Canicule(double ecart, double jours, List<Humain> humains, long maintenant, Origine origine) =>
        Commencer(TypeCatastrophe.Canicule, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, ecart,
            new Recit("cat.nom.canicule", ("t", DecimalR(ecart))),
            new Recit("cat.canicule", ("t", DecimalR(ecart))),
            humains, maintenant, origine, duree: (long)(jours * Calendrier.SecondesParJour));

    // ───────────── Phénomènes du ciel ─────────────

    /// <summary>
    /// Chaque heure, le ciel peut offrir un spectacle. Fréquences pour une région de cette taille :
    /// une éclipse totale de soleil tous les deux siècles environ (une même ville en voit une tous les 375 ans en moyenne),
    /// une grande éclipse partielle tous les dix à quinze ans, une grande comète tous les quinze ans, une boule de feu
    /// remarquable chaque année, une aurore visible si loin des pôles une ou deux fois par siècle (orages magnétiques
    /// comme celui de 1859). Un impact destructeur (comme Toungouska, 1908) n'arrive ici qu'une fois en une dizaine de
    /// millions d'années (voir <see cref="ImpactsParAn"/>).
    /// </summary>
    private void Ciel(List<Humain> humains, long maintenant, DateMonde date)
    {
        const double heuresParAn = Calendrier.JoursParAn * 24;
        if (date.EstJour && !Actif(TypeCatastrophe.Eclipse))
        {
            var partielle = 1 / 12.0;
            var totale = 1 / 190.0;
            // Le jour dure environ douze heures : la probabilité horaire tient compte des seules heures de jour.
            var tirage = _alea.Reel() * heuresParAn / 2;
            if (tirage < totale) Eclipse(1, humains, maintenant, Origine.Naturelle);
            else if (tirage < totale + partielle) Eclipse(0.8 + 0.15 * _alea.Reel(), humains, maintenant, Origine.Naturelle);
        }
        if (!date.EstJour)
        {
            var tirage = _alea.Reel() * heuresParAn / 2;
            if (tirage < 1.0 && !Actif(TypeCatastrophe.Bolide)) Bolide(humains, maintenant, Origine.Naturelle);
            else if (tirage < 1 + 1 / 15.0 && !Actif(TypeCatastrophe.Comete)) Comete(humains, maintenant, Origine.Naturelle);
            else if (tirage < 1 + 1 / 15.0 + 1 / 70.0 && !Actif(TypeCatastrophe.Aurore)) Aurore(humains, maintenant, Origine.Naturelle);
        }
        if (_alea.Reel() * heuresParAn < ImpactsParAn(ImpactMin))
        {
            var i = TerreAuHasard(0, 0, _carte.Largeur - 1, _carte.Hauteur - 1, _ => true);
            if (i >= 0) Impact(i % _carte.Largeur, i / _carte.Largeur, EnergieImpact(), humains, maintenant, Origine.Naturelle);
        }
    }

    /// <summary>Énergie (mégatonnes) en dessous de laquelle un objet venu du ciel explose trop haut pour ravager le sol.</summary>
    private const double ImpactMin = 10;

    /// <summary>
    /// Impacts d'au moins <paramref name="megatonnes"/> par an sur cette carte : sur toute la Terre, N(&gt;E) = 3,7 E^−0,9 par an
    /// (E en kilotonnes ; Brown et al. 2002, d'après les explosions de bolides observées par satellite), soit un Toungouska
    /// (10 Mt) tous les mille ans environ, réparti sur les 510 millions de km² du globe.
    /// </summary>
    private double ImpactsParAn(double megatonnes) =>
        3.7 * Math.Pow(megatonnes * 1000, -0.9) * Echelle.KilometresCarres(_carte.Largeur * _carte.Hauteur) / 510e6;

    /// <summary>Énergie d'un impact naturel, tirée selon la même loi : les gros sont d'autant plus rares.</summary>
    private double EnergieImpact() => Math.Min(EnergieMax, ImpactMin * Math.Pow(1 - _alea.Reel(), -1 / 0.9));

    /// <summary>Le plus gros impact connu, celui de la fin du Crétacé (Chicxulub) : de l'ordre de 100 millions de mégatonnes.</summary>
    public const double EnergieMax = 1e8;

    /// <summary>
    /// Hiver d'impact : écart de température (°C) et durée (années) selon l'énergie. Jusqu'à 10 000 Mt, les effets restent
    /// régionaux ; vers 100 000 Mt à un million (astéroïde d'un à deux kilomètres), poussières et suies assombrissent
    /// le ciel du monde entier pendant des mois (Toon et al. 1997) ; après Chicxulub, le froid a duré des années
    /// (−26 °C en moyenne sur les terres, trois ans sous le gel ; Brugger et al. 2017). Interpolé entre ces repères.
    /// </summary>
    private static readonly (double Megatonnes, double Ecart, double Annees)[] HiversDImpact =
        [(1e4, -0.5, 0.5), (1e5, -3, 1), (1e6, -8, 2), (1e7, -15, 3), (1e8, -26, 5)];

    private static (double Ecart, double Annees)? HiverDImpact(double megatonnes)
    {
        if (megatonnes < HiversDImpact[0].Megatonnes) return null;
        var l = Math.Log10(megatonnes);
        for (var k = 1; k < HiversDImpact.Length; k++)
        {
            var (m1, e1, a1) = HiversDImpact[k - 1];
            var (m2, e2, a2) = HiversDImpact[k];
            if (l > Math.Log10(m2)) continue;
            var t = (l - Math.Log10(m1)) / (Math.Log10(m2) - Math.Log10(m1));
            return (e1 + t * (e2 - e1), a1 + t * (a2 - a1));
        }
        return (HiversDImpact[^1].Ecart, HiversDImpact[^1].Annees);
    }

    /// <summary>
    /// Un astéroïde assez gros pour assombrir le ciel du monde entier (10 000 Mt et plus) peut tomber n'importe où sur la Terre,
    /// le plus souvent loin d'ici : environ une fois en 500 000 ans (même loi que <see cref="ImpactsParAn"/>, pour tout le globe
    /// hors de cette carte). Seul son hiver atteint ce continent. Tiré une fois par jour.
    /// </summary>
    private void ImpactLointain(List<Humain> humains, long maintenant)
    {
        const double seuil = 1e4;
        var ici = ImpactsParAn(seuil);
        var ailleurs = 3.7 * Math.Pow(seuil * 1000, -0.9) - ici;
        if (_alea.Reel() * Calendrier.JoursParAn >= ailleurs) return;
        var megatonnes = Math.Min(EnergieMax, seuil * Math.Pow(1 - _alea.Reel(), -1 / 0.9));
        if (HiverDImpact(megatonnes) is not var (ecart, annees)) return;
        var hiver = Commencer(TypeCatastrophe.HiverDImpact, _carte.Largeur / 2, _carte.Hauteur / 2, 900, ecart,
            new Recit("cat.nom.impactLointain", ("t", DecimalR(ecart))),
            new Recit("cat.impactLointain", ("t", DecimalR(ecart))),
            humains, maintenant, duree: (long)(annees * Calendrier.SecondesParAn));
        hiver.Signalee = true;
    }

    private bool Actif(TypeCatastrophe type) => _liste.Any(c => c.Active && c.Type == type);

    private Catastrophe Eclipse(double part, List<Humain> humains, long maintenant, Origine origine)
    {
        var totale = part >= 0.999;
        var c = Commencer(TypeCatastrophe.Eclipse, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, part,
            totale ? Recit.Seul("cat.nom.eclipseTotale") : new Recit("cat.nom.eclipse", ("p", Libelles.Pourcent(part))),
            Recit.Seul(totale ? "cat.eclipseTotale" : "cat.eclipse"),
            humains, maintenant, origine, duree: Calendrier.SecondesParHeure);
        foreach (var h in humains)
        {
            // Une éclipse partielle passe inaperçue pour ceux qui ne lèvent pas les yeux.
            if (h.EstVivant && h.Activite != Activite.Dormir && (totale || _alea.Reel() < 0.5)) Impressionner(h, c, maintenant);
        }
        return c;
    }

    private Catastrophe Comete(List<Humain> humains, long maintenant, Origine origine)
    {
        var jours = 20 + _alea.Reel() * 40;
        var c = Commencer(TypeCatastrophe.Comete, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, jours, Recit.Seul("cat.nom.comete"),
            Recit.Seul("cat.comete"),
            humains, maintenant, origine, duree: (long)(jours * Calendrier.SecondesParJour));
        Contempler(c, humains, maintenant, nuit: true);
        return c;
    }

    private Catastrophe Aurore(List<Humain> humains, long maintenant, Origine origine)
    {
        var nuits = 1 + _alea.Entier(3);
        var c = Commencer(TypeCatastrophe.Aurore, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, nuits, Recit.Seul("cat.nom.aurore"),
            Recit.Seul("cat.aurore"), humains, maintenant, origine,
            duree: nuits * Calendrier.SecondesParJour);
        Contempler(c, humains, maintenant, nuit: true);
        return c;
    }

    /// <summary>Ceux qui sont éveillés voient le phénomène (la nuit, beaucoup dorment).</summary>
    private void Contempler(Catastrophe c, List<Humain> humains, long maintenant, bool nuit)
    {
        foreach (var h in humains)
        {
            if (h.EstVivant && (!nuit || h.Activite != Activite.Dormir)) Impressionner(h, c, maintenant);
        }
    }

    /// <summary>
    /// Une boule de feu traverse la nuit. Une fois sur dix environ, il en tombe une pierre (météorite) : elle restera
    /// là où elle est tombée.
    /// </summary>
    private Catastrophe Bolide(List<Humain> humains, long maintenant, Origine origine)
    {
        var x = _alea.Entier(_carte.Largeur);
        var y = _alea.Entier(_carte.Hauteur);
        var chute = Climat.EstPraticable(_carte.BiomeEn(x, y)) && _alea.Reel() < 0.1;
        var c = Commencer(TypeCatastrophe.Bolide, x, y, 190, chute ? 1 : 0, chute ? new Recit("cat.nom.meteorite", ("ou", Ou(x, y))) : Recit.Seul("cat.nom.bolide"),
            chute ? new Recit("cat.meteorite", ("ou", Ou(x, y))) : Recit.Seul("cat.bolide"),
            humains, maintenant, origine, duree: Calendrier.SecondesParHeure);
        if (chute) c.Cases.Add(_carte.Indice(x, y));
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Activite == Activite.Dormir || Distance(h.X, h.Y, x, y) > c.Rayon) continue;
            if (chute && Distance(h.X, h.Y, x, y) <= 6)
            {
                c.Temoins.Add(h.Id);
                Toucher(c, h);
                Noter(maintenant, h, new Recit("cat.meteorite.vue", ("h", h)), c.Id, type: TypeEvenement.Humeur);
                Effrayer(h, c, 40, maintenant);
            }
            else Impressionner(h, c, maintenant);
        }
        return c;
    }

    /// <summary>
    /// Chute d'un astéroïde qui explose en l'air ou frappe le sol : le souffle rase tout sur des dizaines de km
    /// (Toungouska, 1908 : environ 2 000 km² de forêt couchés pour 10 à 15 mégatonnes), allume des feux et fait trembler la terre.
    /// </summary>
    private Catastrophe Impact(int x, int y, double megatonnes, List<Humain> humains, long maintenant, Origine origine)
    {
        // Le rayon de destruction croît comme la racine cubique de l'énergie : environ 25 km pour 10 Mt.
        var rayon = Echelle.Cases(25 * Math.Cbrt(megatonnes / 10));
        var c = Commencer(TypeCatastrophe.Impact, x, y, rayon, megatonnes, new Recit("cat.nom.impact", ("ou", Ou(x, y)), ("mt", Math.Round(megatonnes, megatonnes < 10 ? 1 : 0, MidpointRounding.AwayFromZero))),
            new Recit("cat.impact", ("ou", Ou(x, y))),
            humains, maintenant, origine, duree: Calendrier.SecondesParHeure);
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant) continue;
            var d = Distance(h.X, h.Y, x, y);
            if (d <= rayon / 3.0) Frapper(h, c, 0.9, 0.5, CauseDeMort.Brulure, humains, maintenant);
            else if (d <= rayon) Frapper(h, c, 0.25, 0.5, CauseDeMort.Accident, humains, maintenant);
            else if (d <= rayon * 4) Effrayer(h, c, 50, maintenant);
            else Impressionner(h, c, maintenant);
        }
        foreach (var i in DansLeRayon(x, y, rayon))
        {
            var f = 1 - Distance(i % _carte.Largeur, i / _carte.Largeur, x, y) / Math.Max(1, rayon);
            Ravager(c, i, 1 - f, 1 - 0.8 * f, f);
        }
        var depart = _carte.Indice(x, y);
        if (Inflammabilite(_carte.BiomeEn(x, y)) > 0 && !EstDangereuse(depart)) Allumer(x, y, humains, maintenant, origine, c.Id);
        foreach (var i in DansLeRayon(x, y, rayon)) c.Cases.Add(i);
        if (HiverDImpact(megatonnes) is var (ecart, annees))
        {
            var hiver = Commencer(TypeCatastrophe.HiverDImpact, x, y, 900, ecart, new Recit("cat.nom.hiverImpact", ("t", DecimalR(ecart))),
                new Recit(ecart > -5 ? "cat.hiverImpact" : "cat.hiverImpact.nuit", ("t", DecimalR(ecart))),
                humains, maintenant, origine, cause: c.Id, duree: (long)(annees * Calendrier.SecondesParAn));
            hiver.Signalee = true;
        }
        return c;
    }
}
