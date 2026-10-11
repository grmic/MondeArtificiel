using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

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
            $"Vague de froid ({Decimal(ecart)} °C)",
            $"Un froid mordant s'abat sur tout le continent : il fait {Decimal(-ecart)} °C de moins que d'habitude.",
            humains, maintenant, origine, duree: (long)(jours * Calendrier.SecondesParJour));

    private Catastrophe Canicule(double ecart, double jours, List<Humain> humains, long maintenant, Origine origine) =>
        Commencer(TypeCatastrophe.Canicule, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, ecart,
            $"Canicule (+{Decimal(ecart)} °C)",
            $"Une chaleur écrasante s'installe sur tout le continent : il fait {Decimal(ecart)} °C de plus que d'habitude.",
            humains, maintenant, origine, duree: (long)(jours * Calendrier.SecondesParJour));

    // ───────────── Phénomènes du ciel ─────────────

    /// <summary>
    /// Chaque heure, le ciel peut offrir un spectacle. Fréquences pour une région de cette taille :
    /// une éclipse totale de soleil tous les deux siècles environ (une même ville en voit une tous les 375 ans en moyenne),
    /// une grande éclipse partielle tous les dix à quinze ans, une grande comète tous les quinze ans, une boule de feu
    /// remarquable chaque année, une aurore visible si loin des pôles une ou deux fois par siècle (orages magnétiques
    /// comme celui de 1859). Un impact destructeur (comme Toungouska, 1908) n'arrive ici qu'une fois en quelques
    /// millions d'années.
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
        if (_alea.Reel() * heuresParAn < 3e-7)
        {
            var i = TerreAuHasard(0, 0, _carte.Largeur - 1, _carte.Hauteur - 1, _ => true);
            if (i >= 0) Impact(i % _carte.Largeur, i / _carte.Largeur, 10, humains, maintenant, Origine.Naturelle);
        }
    }

    private bool Actif(TypeCatastrophe type) => _liste.Any(c => c.Active && c.Type == type);

    private Catastrophe Eclipse(double part, List<Humain> humains, long maintenant, Origine origine)
    {
        var totale = part >= 0.999;
        var c = Commencer(TypeCatastrophe.Eclipse, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, part,
            totale ? "Éclipse totale de soleil" : $"Grande éclipse partielle de soleil ({part:P0})",
            totale
                ? "En plein jour, le soleil s'éteint : la nuit tombe quelques minutes, les étoiles paraissent, les bêtes se taisent, puis la lumière revient."
                : "Une ombre ronge le soleil : pendant une heure, la lumière baisse étrangement.",
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
        var c = Commencer(TypeCatastrophe.Comete, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, jours, "Grande comète",
            "Une étoile chevelue apparaît dans le ciel nocturne : sa longue traînée pâle brillera chaque nuit pendant des semaines.",
            humains, maintenant, origine, duree: (long)(jours * Calendrier.SecondesParJour));
        Contempler(c, humains, maintenant, nuit: true);
        return c;
    }

    private Catastrophe Aurore(List<Humain> humains, long maintenant, Origine origine)
    {
        var nuits = 1 + _alea.Entier(3);
        var c = Commencer(TypeCatastrophe.Aurore, _carte.Largeur / 2, _carte.Hauteur / 2, _carte.Largeur, nuits, "Aurore",
            "Des voiles de lumière rouge et verte ondulent dans le ciel de la nuit.", humains, maintenant, origine,
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
        var c = Commencer(TypeCatastrophe.Bolide, x, y, 190, chute ? 1 : 0, chute ? $"Chute d'une météorite {Ou(x, y)}" : "Boule de feu",
            chute
                ? $"Une boule de feu raye la nuit dans un grondement de tonnerre ; une pierre fumante s'abat {Ou(x, y)}."
                : "Une boule de feu raye le ciel nocturne et laisse une longue traînée lumineuse.",
            humains, maintenant, origine, duree: Calendrier.SecondesParHeure);
        if (chute) c.Cases.Add(_carte.Indice(x, y));
        foreach (var h in humains)
        {
            if (!h.EstVivant || h.Activite == Activite.Dormir || Distance(h.X, h.Y, x, y) > c.Rayon) continue;
            if (chute && Distance(h.X, h.Y, x, y) <= 6)
            {
                c.Temoins.Add(h.Id);
                Toucher(c, h);
                Noter(maintenant, h, $"{h.Nom} voit une pierre fumante tomber du ciel tout près.", c.Id, type: TypeEvenement.Humeur);
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
        var c = Commencer(TypeCatastrophe.Impact, x, y, rayon, megatonnes, $"Chute d'un astéroïde {Ou(x, y)} ({megatonnes:0} Mt)",
            $"Un second soleil traverse le ciel et explose {Ou(x, y)} : le souffle couche les arbres à perte de vue et le ciel s'embrase.",
            humains, maintenant, origine, duree: Calendrier.SecondesParHeure);
        foreach (var h in humains)
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
            Ravager(i, 1 - f, 1 - 0.8 * f, f);
        }
        var depart = _carte.Indice(x, y);
        if (Inflammabilite(_carte.BiomeEn(x, y)) > 0 && !EstDangereuse(depart)) Allumer(x, y, humains, maintenant, origine, c.Id);
        return c;
    }
}
