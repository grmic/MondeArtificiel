using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Falaises, cols et chutes (mondes de la génération 2, depuis la 0.32.7). On contourne une paroi, on passe par ses couloirs et
/// ses vires, on franchit une crête par son col ; un téméraire pressé grimpe ou désescalade là où il n'y a pas de passage. Une
/// chute dépend de la hauteur exposée, de l'âge, de la nuit, de la pluie, de la fatigue, de l'adresse et de la prudence ; on
/// tombe aussi des arbres, en cueillant fruits et miel.
/// <para>
/// Repères : les accidents font de l'ordre de 5 à 15 % des morts chez les chasseurs-cueilleurs, et les chutes (de rochers,
/// d'arbres) y tiennent une bonne place (Hill et Hurtado, 1996, chez les Aché ; Gurven et Kaplan, 2007). Chez l'adulte, la
/// moitié des chutes d'environ 14 m tuent (Warner et Demling, 1986) ; un enfant, léger, survit mieux à la même hauteur, un
/// vieillard bien moins. Les autres valeurs sont des ordres de grandeur.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    private bool AvecLeRelief => Generation >= 2;

    /// <summary>Le chemin cherché pour l'humain en cours peut passer par une paroi sans passage (il ose grimper).</summary>
    [ThreadStatic] private static bool _grimpe;

    /// <summary>L'humain dont on cherche le chemin (pour les passages qu'il craint) ; null hors de son tour.</summary>
    [ThreadStatic] private static Humain? _marcheur;

    [ThreadStatic] private static int[]? _couts;
    [ThreadStatic] private static PriorityQueue<int, int>? _tas;

    /// <summary>
    /// Effort estimé pour un pas droit restant (un pas en diagonale compte √2) : la moitié de plus que sur le plat, ce qui guide
    /// la recherche vers la cible bien plus vite, pour un chemin à peine plus long que le meilleur (on ne connaît pas le meilleur
    /// chemin, on suit celui qui semble le bon).
    /// </summary>
    private const int EffortEstime = 1500;

    /// <summary>Au-delà de ce nombre de cases examinées, on va vers la plus proche de la cible et on reprend de là.</summary>
    private const int ExamensMax = 400;

    /// <summary>L'effort estimé de (x, y) jusqu'à (cx, cy), ou jusqu'à côté si <paramref name="arrivee"/> vaut 1.</summary>
    private static int Estimer(int x, int y, int cx, int cy, int arrivee)
    {
        int a = Math.Abs(x - cx), b = Math.Abs(y - cy), mn = Math.Min(a, b), mx = Math.Max(a, b);
        return Math.Max(0, (mx - mn - arrivee) * EffortEstime + mn * (EffortEstime * 1414 / 1000));
    }

    /// <summary>On passe de la case i à sa voisine j (pour le relief : paroi, passage craint).</summary>
    private bool Franchissable(int i, int j)
    {
        if (!AvecLeRelief) return true;
        return carte.Relief.Entre(i, j) switch
        {
            Franchissement.Paroi => _grimpe,
            Franchissement.Passage => _grimpe || _marcheur is null || !Craint(_marcheur, i, j),
            _ => true,
        };
    }

    /// <summary>A-t-il vu tomber quelqu'un (ou est-il tombé) à ce passage ces derniers temps ? Il l'évite alors.</summary>
    private bool Craint(Humain h, int i, int j)
    {
        foreach (var l in h.Memoire.Lieux)
        {
            if (l.Type != TypeLieu.Passage || l.DecuLe is not { } vu) continue;
            // La peur s'éteint en un à cinq ans, plus lentement chez les anxieux.
            if (_maintenant - vu > (1 + 4 * h.Personnalite.Nevrosisme) * Calendrier.SecondesParAn) continue;
            var (x, y) = (l.X, l.Y);
            if (Distance(x, y, i % carte.Largeur, i / carte.Largeur) <= 1 || Distance(x, y, j % carte.Largeur, j / carte.Largeur) <= 1) return true;
        }
        return false;
    }

    /// <summary>
    /// Ose-t-il grimper une paroi plutôt que de faire le détour ? Le téméraire adroit et pressé, oui ; le prudent presque jamais ;
    /// jamais de nuit, sauf pour fuir. Sans hasard : le même humain dans la même situation décide de même à chaque pas.
    /// </summary>
    internal bool OseGrimper(Humain h)
    {
        if (h.Fatigue >= 80 || h.Activite == Activite.PorteParSaMere) return false;
        var p = h.Personnalite;
        var fuite = h.Activite is Activite.Fuir or Activite.SuivreRavisseur or Activite.Raid;
        if (!fuite && !Calendrier.Decomposer(_maintenant).EstJour) return false;
        var audace = 0.6 * (1 - p.Prudence) + 0.2 * p.Ouverture + 0.2 * p.Extraversion;
        var urgence = Math.Max(h.Faim, h.Soif) / 100 * 0.4 + (fuite ? 0.4 : 0);
        var age = h.AgeEnAnnees(_maintenant);
        var seuil = age < 14 ? 0.95 : age >= 60 ? 0.9 : 0.75;
        return audace * (0.5 + p.Adresse) * (1 - 0.7 * Sequelles.Gene(h, _maintenant)) + urgence > seuil;
    }

    /// <summary>
    /// Effort pour aller de i à sa voisine j, en mètres de plat : la marche (terrain et pente, Tobler), plus le temps et la peur
    /// d'un passage, ou l'escalade d'une paroi (de l'ordre d'une heure pour 100 m, avec les arrêts pour chercher les prises).
    /// </summary>
    private int Effort(int i, int j)
    {
        int l = carte.Largeur;
        var relief = carte.Relief;
        var k = i * 8 + Relief.Direction(j % l - i % l, j / l - i / l);
        var connu = relief.Efforts[k];
        if (connu > 0) return connu - 1;
        var e = Marche.Effort(carte, i % l, i / l, j % l, j / l);
        switch (relief.Entre(i, j))
        {
            case Franchissement.Passage: e += 1500; break;
            case Franchissement.Paroi: e += 30 * relief.Hauteur(i, j); break;
        }
        var effort = (int)Math.Min(int.MaxValue / 4, e);
        relief.Efforts[k] = effort + 1;
        return effort;
    }

    /// <summary>
    /// Le chemin de moindre effort (A*, 8 directions), dans un carré de 40 cases autour du départ, vers la cible ou une case voisine
    /// d'elle : il passe par les cols plutôt que par les sommets, et contourne les parois. Renvoie la première case et l'effort, et
    /// met dans <paramref name="chemin"/> (s'il est donné) les cases à suivre, la dernière en tête et la première en fin de liste.
    /// Si la recherche s'étend trop, il prend le chemin vers la case examinée la plus proche de la cible (on reprendra de là),
    /// avec un effort estimé ; s'il faudrait d'abord s'en éloigner, le plus court chemin en cases (sur 20 cases) prend le relais.
    /// </summary>
    internal int PremierPasPondere(int depart, int cible, out int effort, List<int>? chemin = null)
    {
        const int RayonMax = 40;
        int l = carte.Largeur, cx = cible % l, cy = cible / l, x0 = depart % l, y0 = depart / l;
        var arrivee = Praticable(cible) ? 0 : 1;
        var generation = NouveauParcours();
        if (_couts is null || _couts.Length != _vus!.Length) _couts = new int[_vus!.Length];
        _tas ??= new PriorityQueue<int, int>();
        var (vus, parent, couts, tas) = (_vus!, _parents!, _couts, _tas);
        tas.Clear();
        vus[depart] = generation;
        parent[depart] = depart;
        couts[depart] = 0;
        tas.Enqueue(depart, 0);
        var examens = 0;
        effort = int.MaxValue;
        int meilleur = depart, reste = Distance(x0, y0, cx, cy);
        while (tas.TryDequeue(out var i, out var priorite))
        {
            int x = i % l, y = i / l;
            if (priorite > couts[i] + Estimer(x, y, cx, cy, arrivee)) continue; // déjà mieux atteinte
            if (++examens > ExamensMax)
            {
                if (meilleur == depart) return PremierPasDuChemin(depart, cible, chemin, 20); // il faudrait d'abord s'éloigner : le plus court en cases, plus près
                effort = couts[meilleur] + Estimer(meilleur % l, meilleur / l, cx, cy, arrivee);
                return Remonter(meilleur, depart, parent, chemin);
            }
            var d = Distance(x, y, cx, cy);
            if (d < reste || d == reste && meilleur != depart && couts[i] < couts[meilleur]) (meilleur, reste) = (i, d);
            if (i != depart && d <= arrivee)
            {
                effort = couts[i];
                return Remonter(i, depart, parent, chemin);
            }
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= l || ny >= carte.Hauteur || Math.Abs(nx - x0) > RayonMax || Math.Abs(ny - y0) > RayonMax) continue;
                var j = ny * l + nx;
                if (!Praticable(j) || !Franchissable(i, j)) continue;
                var c = couts[i] + Effort(i, j);
                if (vus[j] == generation && c >= couts[j]) continue;
                vus[j] = generation;
                couts[j] = c;
                parent[j] = i;
                tas.Enqueue(j, c + Estimer(nx, ny, cx, cy, arrivee));
            }
        }
        return -1;
    }

    /// <summary>Remonte de l'arrivée au départ : la première case du chemin, et le chemin entier si on le demande.</summary>
    private static int Remonter(int i, int depart, int[] parent, List<int>? chemin)
    {
        chemin?.Clear();
        while (true)
        {
            chemin?.Add(i);
            if (parent[i] == depart) return i;
            i = parent[i];
        }
    }

    /// <summary>
    /// Le chemin d'un humain dans un monde qui a du relief. Il suit celui qu'il a choisi tant que sa cible ne change pas et que le
    /// pas suivant reste possible ; sinon il en cherche un : d'abord sans paroi, puis, si le détour est introuvable ou plusieurs
    /// fois plus long que la ligne droite et qu'il ose, aussi par les parois, et il prend le plus court.
    /// </summary>
    internal int CheminDansLeRelief(Humain h, int depart, int cible)
    {
        _marcheur = h;
        try
        {
            if (h.Chemin is { } suivi && h.CheminCible == cible)
            {
                while (suivi.Count > 0 && suivi[^1] == depart) suivi.RemoveAt(suivi.Count - 1);
                if (suivi.Count > 0)
                {
                    var prochain = suivi[^1];
                    int l = carte.Largeur;
                    var voisin = Distance(depart % l, depart / l, prochain % l, prochain / l) == 1;
                    _grimpe = voisin && carte.Relief.Paroi(depart, prochain) && OseGrimper(h);
                    if (voisin && Praticable(prochain) && Franchissable(depart, prochain)) return prochain;
                }
            }
            _grimpe = false;
            var chemin = h.Chemin ?? [];
            var pas = PremierPasPondere(depart, cible, out var effort, chemin);
            var droit = Distance(depart % carte.Largeur, depart / carte.Largeur, cible % carte.Largeur, cible / carte.Largeur) * 1000;
            if ((pas < 0 || effort > 3 * droit + 4000) && OseGrimper(h))
            {
                _grimpe = true;
                var parLaParoi = new List<int>();
                var autre = PremierPasPondere(depart, cible, out var effortAutre, parLaParoi);
                if (autre >= 0 && effortAutre < effort) (pas, chemin) = (autre, parLaParoi);
            }
            (h.Chemin, h.CheminCible) = pas >= 0 ? (chemin, cible) : (null, -1);
            return pas;
        }
        finally
        {
            _grimpe = false;
            _marcheur = null;
        }
    }

    // ───────────── Les chutes ─────────────

    /// <summary>
    /// Éclairement de la lune, de 0 (nouvelle lune) à 1 (pleine lune) : un mois lunaire de 29,53 jours, le premier jour du monde
    /// à la nouvelle lune.
    /// </summary>
    internal static double Lune(long maintenant)
    {
        var phase = maintenant / (double)Calendrier.SecondesParJour / 29.53 % 1;
        return (1 - Math.Cos(2 * Math.PI * phase)) / 2;
    }

    /// <summary>Elle porte un nourrisson sur elle.</summary>
    private static bool PorteUnBebe(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (h.Sexe != Sexe.Femme) return false;
        var debut = maintenant - 2 * Calendrier.SecondesParAn;
        for (var i = tous.Count - 1; i >= 0; i--)
        {
            var e = tous[i];
            if (e.Naissance < debut) break;
            if (e.MereId == h.Id && e.EstVivant && e.Activite == Activite.PorteParSaMere) return true;
        }
        return false;
    }

    /// <summary>
    /// Ce qui multiplie le risque de glisser ou de lâcher prise : l'âge, la nuit (moins sous la pleine lune, sauf s'il pleut), la
    /// pluie sur le rocher, la fatigue, la faim, la maladie, l'adresse et la prudence.
    /// </summary>
    internal double FacteurDeChute(Humain h, int i, bool pluie, bool nuit)
    {
        var p = h.Personnalite;
        var age = h.AgeEnAnnees(_maintenant);
        var f = age switch { < 4 => 3, < 10 => 1.8, < 15 => 1.2, < 50 => 1, < 60 => 1.3, < 70 => 2, _ => 3 };
        if (nuit) f *= 6 * (1 - (pluie ? 0 : 0.5 * Lune(_maintenant)));
        if (pluie) f *= 2.5;
        f *= 1 + h.Fatigue / 100 + (h.Faim > 70 ? 0.3 : 0) + (h.EstMalade(_maintenant) ? 0.5 : 0) + (h.Sante < 50 ? 0.5 : 0);
        f *= (1.6 - p.Adresse) * (1.3 - 0.6 * p.Prudence);
        // Une jambe qui lâche, un bras qui ne tient plus la prise.
        if (h.Sequelle.Membre != Membre.Aucun) f *= 1 + Sequelles.Gene(h, Membre.Jambe, _maintenant) + 0.7 * Sequelles.Gene(h, Membre.Bras, _maintenant);
        return f;
    }

    /// <summary>
    /// Il vient de passer de la case <paramref name="depuis"/> à sa voisine <paramref name="vers"/> : une pente raide, un passage ou
    /// une paroi l'exposent à une chute. Il retient les passages et les cols qu'il a franchis.
    /// </summary>
    internal void Franchir(Humain h, int depuis, int vers)
    {
        if (carte.Relief.Cols[vers]) h.Memoire.Retenir(TypeLieu.Passage, vers % carte.Largeur, vers / carte.Largeur, _maintenant, Aptitude(h));
        var sorte = carte.Relief.Entre(depuis, vers);
        if (sorte == Franchissement.Marche || h.Activite == Activite.PorteParSaMere) return;
        var hauteur = carte.Relief.Hauteur(depuis, vers);
        var date = Calendrier.Decomposer(_maintenant);
        var pluie = ecosysteme.Pleut(vers, _maintenant);
        var facteur = FacteurDeChute(h, vers, pluie, !date.EstJour) * (PorteUnBebe(h, _tous, _maintenant) ? 1.4 : 1);
        var (x, y) = (vers % carte.Largeur, vers / carte.Largeur);
        var habitude = 1.0;
        if (sorte is Franchissement.Passage or Franchissement.Paroi
            && h.Memoire.Lieux.Find(l => l.Type == TypeLieu.Passage && Distance(l.X, l.Y, x, y) <= 1) is { } connu)
            habitude = 1 / (1 + 0.3 * connu.Rappels);
        var risque = sorte switch
        {
            // Une paroi : chaque longueur de 5 m est une chance de lâcher prise.
            Franchissement.Paroi => 1 - Math.Pow(1 - Math.Min(0.5, 0.006 * facteur * habitude), hauteur / 5),
            Franchissement.Passage => Math.Min(0.5, 0.0015 * facteur * habitude),
            // Une pente raide : une glissade de temps en temps, rarement grave.
            _ => Math.Min(0.5, 0.00005 * facteur),
        };
        if (alea.Reel() < risque)
        {
            var tombe = sorte switch
            {
                Franchissement.Paroi => hauteur * (0.2 + 0.8 * alea.Reel()),
                Franchissement.Passage => hauteur * (0.3 + 0.7 * alea.Reel()),
                _ => 0.5 + hauteur * alea.Reel(),
            };
            Tomber(h, tombe, sorte == Franchissement.Paroi ? "paroi" : sorte == Franchissement.Passage ? "passage" : "pente", pluie, !date.EstJour, x, y);
            return;
        }
        if (sorte == Franchissement.PenteRaide) return;
        h.Memoire.Retenir(TypeLieu.Passage, x, y, _maintenant, Aptitude(h));
        if (sorte == Franchissement.Paroi)
            Noter(_maintenant, TypeEvenement.Nature, h, null, new Recit(carte.Altitudes[vers] > carte.Altitudes[depuis] ? "relief.grimpe" : "relief.desescalade",
                ("h", h), ("m", (int)Math.Round(hauteur))));
    }

    /// <summary>
    /// Grimper à un arbre pour ses fruits ou son miel, en forêt : les enfants et les jeunes grimpent souvent, les vieux rarement ;
    /// une chute de quelques mètres à une vingtaine (Aché, Hadza, Hiwi).
    /// </summary>
    internal void GrimperAUnArbre(Humain h)
    {
        if (!AvecLeRelief || h.Fatigue >= 80) return;
        var foret = carte.BiomeEn(h.X, h.Y) switch
        {
            Biome.ForetTropicale or Biome.ForetDeFeuillus => 1,
            Biome.Mangrove => 0.6,
            Biome.ForetDeConiferes => 0.5,
            _ => 0.0,
        };
        // On ne grimpe pas aux arbres dans le noir.
        if (foret == 0 || !Calendrier.Decomposer(_maintenant).EstJour) return;
        var age = h.AgeEnAnnees(_maintenant);
        var envie = age switch { < 4 => 0, < 15 => 1.5, < 45 => 1, < 60 => 0.5, _ => 0.2 } * (0.6 + 0.8 * (1 - h.Personnalite.Prudence))
                    * (1 - 0.8 * Sequelles.Gene(h, _maintenant));
        // Environ un pas de cueillette sur deux cents en forêt : une montée par semaine ou deux.
        if (alea.Reel() >= 0.005 * foret * envie) return;
        var hauteur = 3 + 17 * Math.Pow(alea.Reel(), 1.5);
        var i = carte.Indice(h.X, h.Y);
        var pluie = ecosysteme.Pleut(i, _maintenant);
        if (alea.Reel() >= Math.Min(0.5, 0.0001 * FacteurDeChute(h, i, pluie, false) * (1 + hauteur / 10))) return;
        Tomber(h, hauteur * (0.3 + 0.7 * alea.Reel()), "arbre", pluie, false, h.X, h.Y);
    }

    /// <summary>
    /// Une chute de <paramref name="metres"/> : la mort, plus probable à mesure que la hauteur approche et dépasse celle qui tue un
    /// adulte sur deux (environ 14 m, selon sa robustesse et son âge), ou une blessure qui saigne et peut s'infecter. Ceux qui l'ont
    /// vue, et lui s'il survit, craignent ensuite ce passage.
    /// </summary>
    internal void Tomber(Humain h, double metres, string ou, bool pluie, bool nuit, int x, int y)
    {
        var age = h.AgeEnAnnees(_maintenant);
        var robustesse = (age < 12 ? 1.3 : age >= 60 ? Math.Max(0.45, 1 - 0.02 * (age - 60)) : 1) * (0.85 + 0.3 * h.Personnalite.Endurance);
        // On dévale une pente en roulant et en se retenant : le choc est bien moindre qu'une chute libre de la même hauteur.
        var choc = ou == "pente" ? 0.5 * metres : metres;
        var mortelle = 1 / (1 + Math.Exp(-(choc - 14 * robustesse) / 3.5));
        var circonstance = pluie && nuit ? "nuitPluie" : nuit ? "nuit" : pluie ? "pluie" : "jour";
        var recit = new Recit($"chute.{ou}", ("h", h), ("m", (int)Math.Max(1, Math.Round(metres))), ("quand", new Recit($"chute.quand.{circonstance}")));
        var temoins = Temoins(h, x, y);
        if (alea.Reel() < mortelle)
        {
            Mourir(h, _maintenant, CauseDeMort.Chute, recit: recit);
        }
        else
        {
            var gravite = Math.Clamp(4 + 3.5 * choc + 10 * alea.Reel(), 3, 90);
            h.Sante -= gravite;
            if (h.Sante <= 0)
            {
                Mourir(h, _maintenant, CauseDeMort.Chute, recit: recit);
            }
            else
            {
                Medecine.Blesser(h, gravite, _maintenant, OrigineBlessure.Chute);
                Noter(_maintenant, TypeEvenement.Nature, h, null, new Recit("chute.blesse", ("chute", recit), ("h", h)));
                if (ou != "arbre") Craindre(h, x, y);
            }
        }
        if (ou == "arbre") return;
        foreach (var id in temoins)
        {
            var (le, cx, cy) = (_maintenant, x, y);
            Tour.Differer(_tous, t => { if (t[id - 1].EstVivant) t[id - 1].Memoire.Craindre(cx, cy, le); });
        }
    }

    private void Craindre(Humain h, int x, int y) => h.Memoire.Craindre(x, y, _maintenant);

    /// <summary>Ceux qui sont tout près, éveillés, et voient tomber (identifiants).</summary>
    private List<int> Temoins(Humain h, int x, int y)
    {
        var l = new List<int>();
        foreach (var o in _tous)
        {
            if (o.Id == h.Id || !o.EstVivant || o.Activite == Activite.Dormir || Distance(o.X, o.Y, x, y) > 2) continue;
            l.Add(o.Id);
        }
        return l;
    }
}
