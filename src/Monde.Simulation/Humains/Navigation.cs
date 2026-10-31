using System.Runtime.CompilerServices;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce que les humains savent de la mer qui borde leur terre, calculé une fois par carte : la distance de chaque case de
/// terre à la mer, les cases de rivage de chaque terre, les terres voisines par la mer et leur plus haut sommet.
/// </summary>
internal sealed class AbordsDeLaMer
{
    private static readonly ConditionalWeakTable<Carte, AbordsDeLaMer> Cache = [];

    /// <summary>Au-delà (en cases), on est « loin de la mer ».</summary>
    public const int DistanceMax = 60;

    /// <summary>Terre : distance en cases jusqu'à l'eau salée (DistanceMax + 1 au-delà) ; mer : 0.</summary>
    public required short[] DistanceMer { get; init; }

    /// <summary>Cases de terre au bord de la mer, par terre (numéro de masse).</summary>
    public required int[][] Cotes { get; init; }

    /// <summary>Terres voisines par la mer (passage de moins de 300 km), par terre.</summary>
    public required int[][] Voisines { get; init; }

    /// <summary>Case la plus haute de chaque terre (−1 pour la mer).</summary>
    public required int[] Sommets { get; init; }

    public static AbordsDeLaMer De(Carte carte) => Cache.GetValue(carte, Calculer);

    private static AbordsDeLaMer Calculer(Carte carte)
    {
        var mer = carte.Mer;
        int l = carte.Largeur, h = carte.Hauteur, n = l * h;
        var distance = new short[n];
        Array.Fill(distance, (short)(DistanceMax + 1));
        var file = new Queue<int>();
        for (var i = 0; i < n; i++)
        {
            if (!Biomes.EstEauSalee((Biome)carte.Biomes[i])) continue;
            distance[i] = 0;
            file.Enqueue(i);
        }
        while (file.Count > 0)
        {
            var i = file.Dequeue();
            if (distance[i] >= DistanceMax) continue;
            int x = i % l, y = i / l;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= l || ny >= h) continue;
                var j = ny * l + nx;
                if (distance[j] <= distance[i] + 1) continue;
                distance[j] = (short)(distance[i] + 1);
                file.Enqueue(j);
            }
        }
        var masses = mer.Masse.Length == 0 ? 1 : mer.Masse.Max() + 1;
        var cotes = Enumerable.Range(0, masses).Select(_ => new List<int>()).ToArray();
        var sommets = new int[masses];
        Array.Fill(sommets, -1);
        for (var i = 0; i < n; i++)
        {
            var m = mer.Masse[i];
            if (m == 0) continue;
            if (mer.Rivages[i] != Rivage.Aucun) cotes[m].Add(i);
            if (sommets[m] < 0 || carte.Altitudes[i] > carte.Altitudes[sommets[m]]) sommets[m] = i;
        }
        var voisines = Enumerable.Range(0, masses).Select(_ => new List<int>()).ToArray();
        foreach (var p in mer.Passages)
        {
            voisines[p.MasseA].Add(p.MasseB);
            voisines[p.MasseB].Add(p.MasseA);
        }
        return new AbordsDeLaMer
        {
            DistanceMer = distance, Cotes = cotes.Select(c => c.ToArray()).ToArray(), Voisines = voisines.Select(v => v.ToArray()).ToArray(),
            Sommets = sommets,
        };
    }

    /// <summary>
    /// Rivage où embarquer pour aller de (x, y) vers (cx, cy) : une case de la côte de sa terre, ni falaise ni mangrove, qui
    /// raccourcit la traversée sans trop allonger la marche (le trajet en mer compte une fois et demie). (−1, −1) si aucune.
    /// </summary>
    public (int X, int Y) RiveVers(Carte carte, int x, int y, int cx, int cy)
    {
        var masse = carte.Mer.Masse[carte.Indice(x, y)];
        if (masse <= 0 || masse >= Cotes.Length) return (-1, -1);
        var meilleur = (X: -1, Y: -1);
        var cout = double.MaxValue;
        foreach (var c in Cotes[masse])
        {
            if (carte.Mer.Rivages[c] is Rivage.Falaise or Rivage.Mangrove) continue;
            int px = c % carte.Largeur, py = c / carte.Largeur;
            var k = Echelle.Kilometres(x, y, px, py) + 1.5 * Echelle.Kilometres(px, py, cx, cy);
            if (k < cout) (cout, meilleur) = (k, (px, py));
        }
        return meilleur;
    }
}

/// <summary>
/// La mer, pour les humains (0.31.5) : voir une terre au large (ou la deviner à des signes), y penser, le raconter ; prendre
/// la mer pour pêcher, par curiosité, pour aller voir une terre ou y vivre ; construire un bateau qui tienne les vagues ;
/// la traversée elle-même (cap, courant, vent, vagues, fatigue, soif) ; aborder une terre nouvelle, en rencontrer le peuple.
/// Rien n'y est écrit d'avance : on prend la mer parce qu'on a vu, qu'on a faim, qu'on est curieux ou qu'on n'a plus de
/// quoi vivre, et l'on revient, ou non, selon la mer et le bateau.
/// <para>
/// Ordres de grandeur. L'horizon : 3,57 × (√h₁ + √h₂) km (hauteurs en mètres) ; une côte boisée se voit d'une vingtaine de
/// kilomètres depuis le rivage, une montagne de bien plus loin, si l'air est clair. Les vagues que lève le vent :
/// Hs ≈ 0,21 U²/g en mer ouverte (Pierson et Moskowitz, 1964), bien moins près des côtes (le vent n'a pas la place de les
/// lever). Les hommes ont traversé des bras de mer de plusieurs dizaines de kilomètres il y a au moins 50 000 ans
/// (Sahul ; O'Connell et al., 2010), sans voile ; les oiseaux de terre qui gagnent le large le soir, les feux sur une
/// côte, les objets flottés guidaient les navigateurs du Pacifique (Lewis, We, the Navigators, 1972).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Les premières des groupes avec les terres d'outre-mer (terres vues, abordées, peuples rencontrés).</summary>
    public Outremer Outremer { get; set; } = new();

    /// <summary>Matières qu'il trouve en faisant quelques pas autour de lui, pendant qu'il construit un bateau au rivage.</summary>
    [ThreadStatic] private static HashSet<Matiere>? _autour;

    private AbordsDeLaMer Abords => AbordsDeLaMer.De(carte);

    private int MasseEn(int x, int y) => carte.Mer.Masse[carte.Indice(x, y)];

    private int DistanceMer(int x, int y) => Abords.DistanceMer[carte.Indice(x, y)];

    private static double Hasard(ulong graine, ulong a, ulong b = 0)
    {
        var etat = graine ^ (a * 0x9E3779B97F4A7C15UL) ^ (b * 0xD6E8FEB86659FD93UL);
        return (Alea.SplitMix64(ref etat) >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// Clarté de l'air du jour, en km (la même pour tous) : souvent 10 à 30 km dans l'air chaud et humide de ce monde,
    /// parfois 60 à 120 km par temps très clair (estimation, d'après les visibilités météorologiques sous les tropiques).
    /// </summary>
    internal double Visibilite(long maintenant)
    {
        var u = Hasard(carte.Graine ^ 0x5649534942UL, (ulong)(maintenant / Calendrier.SecondesParJour));
        return 8 * Math.Pow(Nature.Mer.VueMaxKm / 8, Math.Pow(u, 1.5));
    }

    /// <summary>
    /// Force du vent en ce moment (m/s), la même sur toute la mer : tirée d'une loi de Weibull autour du vent de la saison,
    /// une valeur par jour, et l'on passe de l'une à l'autre au fil des heures (un temps calme dure un jour ou deux, un coup
    /// de vent aussi).
    /// </summary>
    internal double Vent(long maintenant, out double depuis)
    {
        var v = carte.Mer.Vents[(int)Calendrier.Decomposer(maintenant).Saison];
        var k = (ulong)(maintenant / Calendrier.SecondesParJour);
        var f = maintenant % Calendrier.SecondesParJour / (double)Calendrier.SecondesParJour;
        double Force(ulong jour) => v.Vitesse / 0.886 * Math.Sqrt(-Math.Log(1 - 0.999 * Hasard(carte.Graine ^ 0x484F554C45UL, jour)));
        double Ecart(ulong jour) => 40 * (Hasard(carte.Graine ^ 0x4341505643UL, jour) - 0.5);
        depuis = v.Depuis + Ecart(k) + (Ecart(k + 1) - Ecart(k)) * f;
        return Force(k) + (Force(k + 1) - Force(k)) * f;
    }

    /// <summary>
    /// Hauteur des vagues (m) sur une case de mer en ce moment : levées par le vent du moment, d'autant moins hautes qu'on est
    /// près d'une côte ; une tempête les porte à plusieurs mètres.
    /// </summary>
    internal double Houle(int x, int y, long maintenant)
    {
        var vent = Vent(maintenant, out _);
        var km = Echelle.Kilometres(carte.Mer.DistanceCote[carte.Indice(x, y)]);
        var hs = 0.21 * vent * vent / Lois.Gravite * Math.Clamp(0.3 + 0.7 * km / 40, 0.3, 1);
        return Math.Max(hs, Catastrophes?.TempeteEn(x, y) ?? 0);
    }

    // ------------------------------------------------------------------ Regarder au large

    /// <summary>
    /// Une fois par heure, celui qui est sur le rivage, sur une hauteur ou en mer regarde au large : le jour, une côte ou un
    /// sommet d'une autre terre qui dépasse l'horizon par l'air du jour ; la nuit, des feux. Au crépuscule, sur le rivage,
    /// des signes : des oiseaux qui partent vers le large, un objet que la mer a apporté.
    /// </summary>
    private void RegarderLeLarge(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstPetitEnfant(_maintenant) || h.Activite == Activite.Dormir) return;
        if ((_maintenant / MondeSimule.SecondesParPas + h.Id) % 6 != 0) return;
        var abords = Abords;
        var i = carte.Indice(h.X, h.Y);
        var ici = carte.Mer.Masse[i];
        double yeux;
        int depart;
        if (ici == 0)
        {
            if (!h.Mer.EnMer) return;
            (yeux, depart) = (Nature.Mer.HauteurDesYeux, h.Mer.Masse);
        }
        else
        {
            var altitude = Math.Max(0, Echelle.Metres(carte.Altitudes[i]));
            if (abords.DistanceMer[i] > 1 && altitude < 150) return;
            (yeux, depart) = (Nature.Mer.HauteurDesYeux + altitude, ici);
        }
        if (depart <= 0 || depart >= abords.Voisines.Length) return;
        var date = Calendrier.Decomposer(_maintenant);
        // Une terre qu'on connaît déjà, on la regarde une fois par jour (le souvenir s'en ravive).
        var matin = date.Heure == 10;
        var visibilite = Visibilite(_maintenant);
        foreach (var autre in abords.Voisines[depart])
        {
            if (autre == ici || autre == h.Continent && ici != 0 && ici != h.Continent) continue;
            var connue = TerreEnMemoire(h, autre) is not null;
            if (connue && !matin) continue;
            if (date.EstJour && VoirTerre(h.X, h.Y, yeux, autre, visibilite) is { } vu)
            {
                Apercevoir(h, autre, vu.X, vu.Y, "terre", connue, tous);
                return;
            }
            if (!date.EstJour && FeuAuLoin(h.X, h.Y, yeux, autre, visibilite) is { } feu)
            {
                Apercevoir(h, autre, feu.X, feu.Y, "feux", connue, tous);
                return;
            }
        }
        if (ici != 0 && date.Heure == 18 && abords.DistanceMer[i] <= 1) Signes(h, ici, tous);
        // Le soir, au camp, ceux qui savent une terre au loin en parlent.
        if (ici != 0 && date.Heure == 20 && alea.Reel() < 0.25 && h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.TerreAuLoin)) RaconterLaMer(h, tous);
    }

    /// <summary>Le souvenir d'une terre au loin sur cette terre-là (la plus marquante), ou null.</summary>
    private LieuConnu? TerreEnMemoire(Humain h, int masse)
    {
        LieuConnu? meilleur = null;
        foreach (var l in h.Memoire.Lieux)
            if (l.Type == TypeLieu.TerreAuLoin && MasseEn(l.X, l.Y) == masse && (meilleur is null || l.Rappels > meilleur.Rappels)) meilleur = l;
        return meilleur;
    }

    /// <summary>Hauteur (m) de ce qui dépasse sur une case de terre : le sol, et les arbres d'une forêt.</summary>
    private double HauteurVue(int i) =>
        Math.Max(0, Echelle.Metres(carte.Altitudes[i])) + (Biomes.EstForet((Biome)carte.Biomes[i]) || carte.Biomes[i] == (byte)Biome.Mangrove ? 25 : 3);

    /// <summary>Le point le plus proche de cette terre qu'on voit d'ici (côte ou sommet), ou null.</summary>
    private (int X, int Y)? VoirTerre(int x, int y, double yeux, int masse, double visibilite)
    {
        var abords = Abords;
        (int X, int Y)? vu = null;
        var plusPres = double.MaxValue;
        // Au plus loin, un sommet de 4 000 m : au-delà, rien ne peut dépasser l'horizon.
        var portee = Math.Min(visibilite, Nature.Mer.Horizon(yeux, 4000));
        foreach (var c in abords.Cotes[masse])
        {
            int cx = c % carte.Largeur, cy = c / carte.Largeur;
            if (Distance(x, y, cx, cy) * Echelle.KilometresParCase > portee) continue;
            var km = Echelle.Kilometres(x, y, cx, cy);
            if (km < plusPres && km <= Math.Min(visibilite, Nature.Mer.Horizon(yeux, HauteurVue(c)))) (plusPres, vu) = (km, (cx, cy));
        }
        if (vu is not null) return vu;
        var s = abords.Sommets[masse];
        if (s < 0) return null;
        int sx = s % carte.Largeur, sy = s / carte.Largeur;
        var d = Echelle.Kilometres(x, y, sx, sy);
        return d <= Math.Min(visibilite, Nature.Mer.Horizon(yeux, HauteurVue(s))) ? (sx, sy) : null;
    }

    /// <summary>
    /// Un feu de camp de cette terre qu'on voit briller la nuit : quelques mètres de flammes, visibles d'une vingtaine de
    /// kilomètres au plus, s'il dépasse l'horizon (estimation).
    /// </summary>
    private (int X, int Y)? FeuAuLoin(int x, int y, double yeux, int masse, double visibilite)
    {
        (int X, int Y)? vu = null;
        var plusPres = double.MaxValue;
        foreach (var f in Installations.Foyers)
        {
            if (MasseEn(f.X, f.Y) != masse) continue;
            var km = Echelle.Kilometres(x, y, f.X, f.Y);
            var hauteur = Math.Max(0, Echelle.Metres(carte.Altitudes[carte.Indice(f.X, f.Y)])) + 3;
            if (km < plusPres && km <= Math.Min(Math.Min(visibilite, 25), Nature.Mer.Horizon(yeux, hauteur))) (plusPres, vu) = (km, (f.X, f.Y));
        }
        return vu;
    }

    /// <summary>
    /// Il voit une terre (ou des feux) au loin : il s'en souvient longtemps, il y pense, il le dit aux siens. La première fois
    /// pour sa communauté, c'est un fait marquant.
    /// </summary>
    private void Apercevoir(Humain h, int masse, int x, int y, string comment, bool connue, IReadOnlyList<Humain> tous)
    {
        h.Memoire.Retenir(TypeLieu.TerreAuLoin, x, y, _maintenant, Aptitude(h), rappels: 4);
        if (connue) return;
        var km = Math.Round(Echelle.Kilometres(h.X, h.Y, x, y));
        var iles = carte.Mer.Iles.Any(i => i.Masse == masse);
        var recit = new Recit($"mer.voit.{comment}", ("h", h), ("terre", Recit.Seul(iles ? "mer.uneIle" : "mer.uneTerre")),
            ("vers", Lieux.VersR(h.X, h.Y, x, y)), ("km", (long)Math.Max(1, km)), ("enMer", h.Mer.EnMer ? Recit.Seul("mer.depuisLaMer") : ""));
        h.Memoire.Penser(_maintenant, new Recit("p.mer.voit", ("vers", Lieux.VersRecit(h.X, h.Y, x, y))));
        var maintenant = _maintenant;
        Tour.Differer(h, q =>
        {
            // Au journal, la première fois pour les siens ; ensuite, il s'en souvient et y pense, sans plus.
            var groupe = Outremer.GroupeDe(q);
            if (Outremer.Connait(groupe, masse, SorteOutremer.Vue)) return;
            var id = Noter(maintenant, TypeEvenement.Mer, q, null, recit, significatif: true);
            Outremer.Ajouter(new DecouverteOutremer(groupe, masse, SorteOutremer.Vue, maintenant, x, y, q.Id, id));
        });
        if (h.Mer is { EnMer: true } m)
        {
            // En mer par curiosité : le plus hardi pousse jusqu'à cette terre si elle n'est pas trop loin ; les autres rentrent
            // le raconter.
            var audace = Audace(h);
            if (m.Motif == MotifMer.Curiosite && audace > 0.3 && km <= 10 + 40 * audace)
                h.Mer = m with { Motif = MotifMer.Expedition, CibleX = x, CibleY = y, Cap = 0, Vu = true };
            else if (m.Motif is MotifMer.Curiosite or MotifMer.Peche)
                h.Mer = m with { Motif = MotifMer.Retour, CibleX = m.RiveX, CibleY = m.RiveY, Vu = true };
            else h.Mer = m with { Vu = true };
        }
        else RaconterLaMer(h, tous);
    }

    /// <summary>
    /// Des signes d'une terre qu'on ne voit pas : des oiseaux de terre qui gagnent le large au crépuscule (ils regagnent leur
    /// gîte à quelques dizaines de kilomètres), un tronc ou un objet que la mer a apporté de là-bas. Il en garde l'idée
    /// d'une terre, dans cette direction, sans savoir à quelle distance.
    /// </summary>
    private void Signes(Humain h, int ici, IReadOnlyList<Humain> tous)
    {
        var abords = Abords;
        var curieux = 0.3 + h.Personnalite.Curiosite;
        foreach (var p in carte.Mer.Passages)
        {
            if (p.MasseA != ici && p.MasseB != ici) continue;
            var autre = p.MasseA == ici ? p.MasseB : p.MasseA;
            if (autre == h.Continent && ici != h.Continent || TerreEnMemoire(h, autre) is not null) continue;
            var (depuis, vers) = p.MasseA == ici ? (p.DepuisA, p.VersB) : (p.VersB, p.DepuisA);
            int dx = depuis % carte.Largeur, dy = depuis / carte.Largeur, vx = vers % carte.Largeur, vy = vers / carte.Largeur;
            if (Echelle.Kilometres(h.X, h.Y, dx, dy) > 30) continue;
            // Des oiseaux qui partent vers le large au soir : leur gîte est à 30 à 100 km selon l'espèce.
            if (p.Km <= 30 + 70 * alea.Reel() && alea.Reel() < 0.02 * curieux)
            {
                Deviner(h, autre, vx, vy, "oiseaux");
                return;
            }
            // Un objet flotté sur une plage, quand le courant vient de l'autre terre.
            if (p.Km > 200 || carte.Mer.RivageA(h.X, h.Y) != Rivage.Plage) continue;
            var (cx, cy) = carte.Mer.Courant(carte.Indice(dx, dy), Calendrier.Decomposer(_maintenant).Saison);
            double ox = dx - vx, oy = dy - vy;
            var norme = Math.Sqrt(ox * ox + oy * oy) * Math.Sqrt(cx * cx + cy * cy);
            if (norme <= 0 || (ox * cx + oy * cy) / norme < 0.3 || alea.Reel() >= 0.003 * curieux) continue;
            var habitee = tous.Vivants().Any(o => o.EstVivant && o.Continent == autre && MasseEn(o.X, o.Y) == autre);
            Deviner(h, autre, vx, vy, habitee && alea.Reel() < 0.5 ? "objet" : "bois");
            return;
        }
    }

    private void Deviner(Humain h, int masse, int x, int y, string signe)
    {
        h.Memoire.Retenir(TypeLieu.TerreAuLoin, x, y, _maintenant, Aptitude(h), rappels: signe == "objet" ? 3 : 1);
        var recit = new Recit($"mer.signe.{signe}", ("h", h), ("vers", Lieux.VersR(h.X, h.Y, x, y)));
        // Un objet travaillé par d'autres mains : il sait qu'une terre habitée est là-bas. C'est une découverte pour les siens.
        if (signe == "objet")
        {
            var maintenant = _maintenant;
            Tour.Differer(h, q =>
            {
                var groupe = Outremer.GroupeDe(q);
                var premiere = !Outremer.Connait(groupe, masse, SorteOutremer.Vue);
                var id = Noter(maintenant, TypeEvenement.Mer, q, null, recit, significatif: premiere);
                if (premiere) Outremer.Ajouter(new DecouverteOutremer(groupe, masse, SorteOutremer.Vue, maintenant, x, y, q.Id, id));
            });
        }
        else Noter(_maintenant, TypeEvenement.Mer, h, null, recit);
        RaconterLaMer(h, _tous);
    }

    /// <summary>Il dit à ceux qui l'entourent (au camp, à quelques cases) les terres au loin qu'il connaît : ils s'en souviennent.</summary>
    private void RaconterLaMer(Humain h, IReadOnlyList<Humain> tous)
    {
        var terres = h.Memoire.Lieux.Where(l => l.Type == TypeLieu.TerreAuLoin).Select(l => (l.X, l.Y, l.Rappels)).ToList();
        if (terres.Count == 0) return;
        var maintenant = _maintenant;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.EstPetitEnfant(maintenant) || Distance(h.X, h.Y, o.X, o.Y) > 3) continue;
            if (o.CommunauteId != h.CommunauteId && o.AffectionPour(h.Id) <= 0) continue;
            Tour.Changer(o, x =>
            {
                foreach (var (tx, ty, rappels) in terres)
                    if (!x.Memoire.Lieux.Exists(l => l.Type == TypeLieu.TerreAuLoin && Math.Max(Math.Abs(l.X - tx), Math.Abs(l.Y - ty)) < 3))
                        x.Memoire.Retenir(TypeLieu.TerreAuLoin, tx, ty, maintenant, Aptitude(x), rappels: Math.Min(2, rappels));
            });
        }
    }

    // ------------------------------------------------------------------ Décider de prendre la mer

    /// <summary>Hardiesse devant l'inconnu (0 à 1) : la curiosité sans la prudence.</summary>
    private static double Audace(Humain h) => h.Personnalite.Curiosite * (1 - h.Personnalite.Prudence);

    /// <summary>Le bateau qu'il a (radeau, botte de roseaux, coque creusée), ou null.</summary>
    private static Objet? Bateau(Humain h) => Radeau(h);

    /// <summary>Le bateau tient la mer (et pas seulement un lac).</summary>
    private static bool TientLaMer(Objet o) => Assemblage.Lire(o.Pieces) is { } a && Lois.HouleMax(a, o.Qualite) >= Lois.HouleDeMer;

    /// <summary>A-t-il un petit enfant (ou un bébé) qui dépend de lui ? On ne part pas alors au large pour rien.</summary>
    private bool AUnPetit(Humain h, IReadOnlyList<Humain> tous)
    {
        foreach (var l in h.Liens)
        {
            if (l.Id <= 0 || l.Id > tous.Count) continue;
            var e = tous[l.Id - 1];
            if (e.EstVivant && (e.MereId == h.Id || e.PereId == h.Id) && e.EstPetitEnfant(_maintenant)) return true;
        }
        return false;
    }

    /// <summary>
    /// Au calme, de jour : prendre la mer. Pour pêcher, s'il a un bateau qui la tient et des eaux riches à portée ; pour voir
    /// une terre dont il se souvient (aperçue, devinée, racontée) ; ou, rarement, par pure curiosité, pour voir ce qu'il y a
    /// au large. Vrai s'il s'y met.
    /// </summary>
    private bool PrendreLaMer(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.Mer.Active || !Calendrier.Decomposer(_maintenant).EstJour) return false;
        var abords = Abords;
        var i = carte.Indice(h.X, h.Y);
        var ici = carte.Mer.Masse[i];
        if (ici == 0 || abords.DistanceMer[i] > AbordsDeLaMer.DistanceMax) return false;
        var p = h.Personnalite;
        var audace = Audace(h);
        var bateau = Bateau(h) is { } b && TientLaMer(b) ? b : null;
        var sait = bateau is not null || h.Savoirs.Exists(s => s.Effet == Effet.Navigation && s.Confiance >= 0.3);

        // Une terre au loin dont il se souvient : y aller voir, d'autant plus volontiers qu'il l'a vue de ses yeux.
        LieuConnu? terre = null;
        foreach (var l in h.Memoire.Lieux)
            if (l.Type == TypeLieu.TerreAuLoin && MasseEn(l.X, l.Y) is var m && m != ici && m != 0
                && (terre is null || Echelle.Kilometres(h.X, h.Y, l.X, l.Y) < Echelle.Kilometres(h.X, h.Y, terre.X, terre.Y)))
                terre = l;
        if (terre is not null && (sait || h.Sait(Effet.Flotte)) && !AUnPetit(h, tous)
            && alea.Reel() < 0.00005 * audace * (terre.Rappels >= 3 ? 1 : 0.4) * (h.Activite == Activite.Repos ? 1.5 : 1))
            return Projeter(h, MotifMer.Expedition, terre.X, terre.Y, tous);

        if (abords.DistanceMer[i] > 3 || AUnPetit(h, tous) && h.Sexe == Sexe.Femme) return false;
        // Pêcher : le ventre qui se creuse, des eaux riches (récifs, hauts-fonds) à portée de pagaie.
        if (sait && h.Faim >= 20 && alea.Reel() < 0.015 * (0.4 + p.Conscience) && CoinDePeche(h.X, h.Y) is { } coin)
            return Projeter(h, MotifMer.Peche, coin.X, coin.Y, tous);
        // Par curiosité : voir ce qu'il y a au large, même sans rien voir d'ici.
        if ((sait || h.Sait(Effet.Flottaison)) && abords.DistanceMer[i] <= 2 && alea.Reel() < 0.0003 * audace)
        {
            var rive = RiveProche(h.X, h.Y);
            if (rive.X < 0) return false;
            var mer = PlusProcheMer(rive.X, rive.Y);
            if (mer.X < 0) return false;
            var angle = Math.Atan2(mer.Y - rive.Y, mer.X - rive.X) + (alea.Reel() - 0.5) * Math.PI * 2 / 3;
            var cases = Echelle.Cases(5 + 45 * audace + 10 * alea.Reel());
            var cx = Math.Clamp(rive.X + (int)Math.Round(Math.Cos(angle) * cases), 0, carte.Largeur - 1);
            var cy = Math.Clamp(rive.Y + (int)Math.Round(Math.Sin(angle) * cases), 0, carte.Hauteur - 1);
            return Projeter(h, MotifMer.Curiosite, cx, cy, tous);
        }
        return false;
    }

    /// <summary>Le voyage est décidé : choisir le rivage où embarquer. Vrai s'il en trouve un.</summary>
    private bool Projeter(Humain h, MotifMer motif, int cx, int cy, IReadOnlyList<Humain> tous)
    {
        var rive = motif is MotifMer.Peche or MotifMer.Curiosite ? RiveProche(h.X, h.Y) : Abords.RiveVers(carte, h.X, h.Y, cx, cy);
        if (rive.X < 0) return false;
        h.Mer = new Traversee(motif, false, rive.X, rive.Y, cx, cy, _maintenant, Masse: MasseEn(h.X, h.Y));
        if (motif != MotifMer.Peche)
            Noter(_maintenant, TypeEvenement.Mer, h, null, new Recit($"mer.projet.{motif}", ("h", h), ("vers", Lieux.VersR(h.X, h.Y, cx, cy))));
        return PreparerLeDepart(h, tous);
    }

    /// <summary>Une case de rivage d'où l'on peut embarquer, à quelques cases (ni falaise ni mangrove), ou (−1, −1).</summary>
    private (int X, int Y) RiveProche(int x, int y)
    {
        var meilleur = (X: -1, Y: -1);
        var d = int.MaxValue;
        for (var dy = -4; dy <= 4; dy++)
        for (var dx = -4; dx <= 4; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            if (carte.Mer.RivageA(nx, ny) is Rivage.Aucun or Rivage.Falaise or Rivage.Mangrove || Abords.DistanceMer[carte.Indice(nx, ny)] != 1) continue;
            var k = Distance2(x, y, nx, ny);
            if (k < d) (d, meilleur) = (k, (nx, ny));
        }
        return meilleur;
    }

    /// <summary>La case de mer voisine la plus proche d'une case de rivage, ou (−1, −1).</summary>
    private (int X, int Y) PlusProcheMer(int x, int y)
    {
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < carte.Largeur && ny < carte.Hauteur && Biomes.EstEauSalee(carte.BiomeEn(nx, ny))) return (nx, ny);
        }
        return (-1, -1);
    }

    /// <summary>Richesse en poissons d'une case de mer (0 : la terre) : les récifs d'abord, puis les hauts-fonds, puis le plateau.</summary>
    private double Poissons(int i)
    {
        var mer = carte.Mer;
        if (mer.Fonds[i] == Fond.Terre) return 0;
        return mer.Recif[i] ? 1 : mer.HautFond[i] ? 0.8 : mer.Fonds[i] == Fond.Plateau ? 0.35 : 0.1;
    }

    /// <summary>Le coin de mer le plus poissonneux à une dizaine de kilomètres au plus du rivage, ou null.</summary>
    private (int X, int Y)? CoinDePeche(int x, int y)
    {
        (int X, int Y)? meilleur = null;
        var score = 0.0;
        const int Rayon = 12;
        for (var dy = -Rayon; dy <= Rayon; dy++)
        for (var dx = -Rayon; dx <= Rayon; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            var i = carte.Indice(nx, ny);
            var s = Poissons(i) - 0.02 * Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (Poissons(i) >= 0.8 && s > score) (score, meilleur) = (s, (nx, ny));
        }
        return meilleur;
    }

    // ------------------------------------------------------------------ Préparer le départ

    /// <summary>
    /// Le voyage est décidé : aller au rivage, y construire un bateau s'il n'en a pas, attendre une mer assez calme et le jour,
    /// puis embarquer (avec ses enfants, s'il part pour vivre ailleurs). Un séjour sur une terre lointaine qui s'achève
    /// devient un retour. Il renonce si les jours passent sans qu'il puisse partir. Vrai s'il y a passé ce moment.
    /// </summary>
    private bool PreparerLeDepart(Humain h, IReadOnlyList<Humain> tous)
    {
        var m = h.Mer;
        if (!m.Active || m.EnMer || m.Motif == MotifMer.Passager) return false;
        if (m.Motif == MotifMer.Sejour)
        {
            if (_maintenant < m.Depuis) return false;
            // Avant de rentrer, il se grave dans la tête le rivage où il a abordé.
            h.Memoire.Retenir(TypeLieu.TerreAuLoin, m.RiveX, m.RiveY, _maintenant, Aptitude(h), rappels: 6);
            h.Mer = m = m with { Motif = MotifMer.Retour, Depuis = _maintenant, Masse = MasseEn(h.X, h.Y) };
        }
        var patience = m.Motif switch
        {
            MotifMer.Peche or MotifMer.Curiosite => Calendrier.SecondesParJour,
            MotifMer.Colon or MotifMer.Retour => 20 * Calendrier.SecondesParJour,
            _ => 15 * Calendrier.SecondesParJour,
        };
        if (_maintenant - m.Depuis > patience)
        {
            Renoncer(h, "attente");
            return false;
        }
        if (Distance(h.X, h.Y, m.RiveX, m.RiveY) > 0)
        {
            Avancer(h, m.RiveX, m.RiveY, Activite.Naviguer);
            if (h.CibleX < 0)
            {
                Renoncer(h, "chemin");
                return false;
            }
            return true;
        }
        // Au rivage. Ce qu'il emmène : lui, ses enfants s'il part pour de bon, de l'eau et quelques outils.
        var passagers = m.Motif is MotifMer.Colon || m.Motif == MotifMer.Retour && h.Liens.Count > 0 ? Enfants(h, tous) : [];
        var charge = MasseCorps(h) + passagers.Sum(e => MasseEnfant(e)) + 10;
        var bateau = Bateau(h) is { } b && TientLaMer(b) && Assemblage.Lire(b.Pieces) is { } ab && Lois.Portance(ab) >= charge ? b : null;
        if (bateau is null) return Construire(h, charge, tous);
        var date = Calendrier.Decomposer(_maintenant);
        var a = Assemblage.Lire(bateau.Pieces)!;
        var mer = PlusProcheMer(h.X, h.Y);
        var houle = mer.X < 0 ? 0 : Houle(mer.X, mer.Y, _maintenant);
        // On part au matin, par une mer que le bateau tient bien ; le prudent attend plus longtemps.
        var tolere = (0.3 + 0.45 * (1 - h.Personnalite.Prudence)) * Lois.HouleMax(a, bateau.Qualite);
        // Et par un vent qui le laisse avancer vers son but (un vent arrière aide, un vent de travers se compense en pagayant
        // de biais) et revenir s'il le faut : on sent bien, sur le rivage, d'où il souffle et combien.
        var (wx, wy) = mer.X < 0 ? (0, 0) : Derive(carte.Indice(mer.X, mer.Y), a, date.Saison);
        // Ce qu'il pagaiera, tel qu'il est (sa force, sa fatigue), avec une marge : on se sait moins vaillant au bout de dix heures.
        var pagaie = 0.8 * Lois.Vitesse(a, h.Sait(Effet.Pagaie)) * (0.6 + 0.4 * Force(h, _maintenant)) * (1 - h.Fatigue / 200)
                     * MondeSimule.SecondesParPas / Calendrier.SecondesParHeure / Echelle.KilometresParCase;
        var prudence = h.Personnalite.Prudence;
        var force = Math.Sqrt(wx * wx + wy * wy);
        bool tropDeVent;
        if (m.Motif is MotifMer.Peche or MotifMer.Curiosite) tropDeVent = force > (0.3 + 0.3 * (1 - prudence)) * pagaie || date.Heure >= 13;
        else
        {
            var progres = Progres(wx, wy, pagaie, h.X, h.Y, m.CibleX, m.CibleY);
            // Sans eau à emporter, on ne part que pour une traversée qui touche terre avant la nuit.
            var heuresDeRoute = Echelle.Kilometres(h.X, h.Y, m.CibleX, m.CibleY) / Math.Max(0.01, progres * Echelle.KilometresParCase
                * Calendrier.SecondesParHeure / MondeSimule.SecondesParPas);
            var eau = h.Objets.Exists(o => o.Matiere is Matiere.Calebasse or Matiere.Peau or Matiere.Substance && o.Plein);
            tropDeVent = progres < (0.25 + 0.35 * prudence) * pagaie || force > 0.95 * pagaie
                         || (eau ? heuresDeRoute > 30 : date.Heure + heuresDeRoute > 20);
        }
        if (!date.EstJour || date.Heure >= 15 && m.Motif is not (MotifMer.Peche or MotifMer.Curiosite) || houle > tolere || tropDeVent)
        {
            // À attendre au bord, on essaie son bateau près du rivage : l'idée de la pagaie peut venir.
            if (tropDeVent && date.EstJour) Pagayer(h, tous, 0.002);
            h.Activite = Activite.Naviguer;
            h.CibleX = h.CibleY = -1;
            _motif = Motif.Aucun;
            return true;
        }
        Embarquer(h, bateau, passagers, tous);
        return true;
    }

    /// <summary>Ses enfants qu'il emmène (pas les bébés, portés par leur mère) : ceux qui ont besoin de lui et sont tout près.</summary>
    private List<Humain> Enfants(Humain h, IReadOnlyList<Humain> tous)
    {
        var enfants = new List<Humain>();
        foreach (var l in h.Liens)
        {
            if (l.Id <= 0 || l.Id > tous.Count) continue;
            var e = tous[l.Id - 1];
            if (!e.EstVivant || e.EstBebe(_maintenant) || !e.EstEnfant(_maintenant) || e.Mer.Active || Distance(h.X, h.Y, e.X, e.Y) > 2) continue;
            if (Gardien(e, tous, _maintenant)?.Id == h.Id) enfants.Add(e);
        }
        return enfants;
    }

    private double MasseEnfant(Humain e) => 8 + 3 * e.AgeEnAnnees(_maintenant);

    private void Renoncer(Humain h, string pourquoi)
    {
        var m = h.Mer;
        h.Mer = default;
        // Ceux qui devaient partir avec les siens par la mer y renoncent : le groupe cesse de vouloir y aller.
        if (m.Motif == MotifMer.Colon && Social?.CommunauteDe(h) is { DestinationX: >= 0 } groupe && MasseEn(groupe.DestinationX, groupe.DestinationY) != MasseEn(h.X, h.Y))
            Tour.Differer(() =>
            {
                if (groupe.DestinationX >= 0 && MasseEn(groupe.DestinationX, groupe.DestinationY) != m.Masse) groupe.DestinationX = groupe.DestinationY = -1;
            });
        if (m.Motif != MotifMer.Peche) Noter(_maintenant, TypeEvenement.Mer, h, null, new Recit($"mer.renonce.{pourquoi}", ("h", h)));
    }

    /// <summary>Durée du travail (en pas de dix minutes) pour faire ce bateau, à deux mains.</summary>
    private double TravailPour(Assemblage a, Humain h)
    {
        if (a.Montage == Montage.Creuse)
        {
            // Évider un tronc au feu et à l'herminette de pierre : quelques jours de travail (estimation).
            var outil = h.Objets.Exists(o => Lois.Coupe(o) >= 0.3);
            return 300 * (FeuIci(h) || h.Sait(Effet.Foyer) ? 1 : 2.5) * (outil ? 1 : 1.8);
        }
        var parPiece = a.Corps switch { Matiere.Tronc => 6, Matiere.Roseau => 2, Matiere.Calebasse => 0.5, _ => 1.5 };
        return a.NCorps * parPiece + 6;
    }

    /// <summary>
    /// Au rivage, sans bateau qui tienne la mer : refaire celui qu'il sait faire, sinon en imaginer un (un radeau plus grand
    /// et mieux lié, une botte de roseaux, une coque creusée), puis y travailler, pas après pas. Fini, il le met à l'eau et les
    /// lois disent s'il tient. Vrai s'il a passé ce moment à y travailler.
    /// </summary>
    private bool Construire(Humain h, double charge, IReadOnlyList<Humain> tous)
    {
        // On va chercher troncs, roseaux et lianes à quelques kilomètres, et on les traîne jusqu'au rivage.
        _autour = MatieresAutour(h, 6);
        try
        {
            var m = h.Mer;
            var projet = Assemblage.Lire(m.Projet);
            if (projet is null || !APiecesPour(h, projet))
            {
                projet = h.Savoirs.Where(s => s.Geste == Geste.Assembler && s.Effet == Effet.Navigation && s.Confiance >= 0.3)
                    .OrderByDescending(s => s.Confiance).Select(s => Assemblage.Lire(s.Pieces))
                    .FirstOrDefault(a => a is not null && APiecesPour(h, a) && Lois.Portance(a) >= charge);
                if (projet is null)
                {
                    if (alea.Reel() >= 0.05 * (0.3 + Inventivite(h)) || Imaginer(h, Besoin.Naviguer, charge) is not { } idee
                        || h.Savoirs.Exists(s => s.Effet == Effet.Rien && s.Pieces == idee.Objet.Signature))
                    {
                        h.Activite = Activite.Naviguer;
                        h.CibleX = h.CibleY = -1;
                        return true;
                    }
                    projet = idee.Objet;
                    if (!APiecesPour(h, projet)) return false;
                    Noter(_maintenant, TypeEvenement.Mer, h, null, new Recit("mer.idee", ("pensee", idee.Pensee)));
                }
                h.Mer = m = m with { Projet = projet.Signature, Ouvrage = 0 };
            }
            h.Activite = Activite.Fabriquer;
            h.CibleX = h.CibleY = -1;
            var ouvrage = m.Ouvrage + (0.6 + 0.8 * h.Personnalite.Adresse) / TravailPour(projet, h);
            if (ouvrage < 1)
            {
                h.Mer = m with { Ouvrage = ouvrage };
                return true;
            }
            h.Mer = m with { Ouvrage = 0, Projet = null };
            var connu = h.Savoirs.Exists(s => s.Geste == Geste.Assembler && s.Pieces == projet.Signature && s.Effet == Effet.Navigation);
            Essayer(h, Besoin.Naviguer, projet, charge, connu ? OrigineSavoir.Essai : OrigineSavoir.Reflexion,
                connu ? null : new Recit("inv.bateau.fini", ("h", h), ("objet", Lois.Decrire(projet))), tous);
            h.Activite = Activite.Fabriquer;
            return true;
        }
        finally
        {
            _autour = null;
        }
    }

    /// <summary>Ce qu'on trouve à quelques cases autour de soi (bois, roseaux, calebasses, lianes).</summary>
    private HashSet<Matiere> MatieresAutour(Humain h, int rayon)
    {
        var set = new HashSet<Matiere>();
        var sec = Sec(h) > 0.55;
        for (var dy = -rayon; dy <= rayon; dy++)
        for (var dx = -rayon; dx <= rayon; dx++)
        {
            int nx = h.X + dx, ny = h.Y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            foreach (var m in Physique.Disponibles(carte, nx, ny, sec, false)) set.Add(m);
        }
        return set;
    }

    /// <summary>Monter dans le bateau et pousser au large : l'erreur de cap qu'il fera, ses enfants à bord.</summary>
    private void Embarquer(Humain h, Objet bateau, List<Humain> passagers, IReadOnlyList<Humain> tous)
    {
        var m = h.Mer;
        // Sans repère, on dévie : d'autant moins qu'on a le sens de l'espace (le soleil, la houle, les étoiles).
        var spatial = Math.Clamp((h.Personnalite.Intelligence.Spatiale - 55) / 90, 0, 1);
        var cap = (alea.Reel() + alea.Reel() + alea.Reel() - 1.5) * 2 * (25 - 18 * spatial);
        // Qui sait que les étoiles tournent autour d'un point fixe tient mieux son cap, la nuit surtout (depuis la 0.34.1).
        cap *= 1 - 0.6 * ConfianceDans(h, Serie.Etoiles);
        // Au retour d'une autre terre, il rapporte ce qu'il y a vu.
        h.Mer = m with { EnMer = true, Depuis = _maintenant, Dx = 0, Dy = 0, Cap = cap, Vu = m.Vu && m.Motif == MotifMer.Retour, Masse = MasseEn(h.X, h.Y) };
        h.Objets[h.Objets.IndexOf(bateau)] = bateau with { Plein = true };
        h.Activite = Activite.Naviguer;
        h.CibleX = h.CibleY = -1;
        foreach (var e in passagers)
        {
            var (id, x, y, debut) = (h.Id, h.X, h.Y, _maintenant);
            Tour.Changer(e, c => c.Mer = new Traversee(MotifMer.Passager, true, x, y, x, y, debut, PiloteId: id, Masse: MasseEn(x, y)));
        }
        Pagayer(h, tous, 0.03);
        if (m.Motif != MotifMer.Peche)
            Noter(_maintenant, TypeEvenement.Mer, h, null, new Recit($"mer.depart.{m.Motif}", ("h", h), ("bateau", Lois.Decrire(Assemblage.Lire(bateau.Pieces)!)),
                ("vers", Lieux.VersR(h.X, h.Y, m.CibleX, m.CibleY)), ("n", passagers.Count)));
    }

    /// <summary>
    /// À pousser l'eau de ses mains, l'idée vient de la pousser avec une branche plate : une pagaie. Il la montre à qui le voit.
    /// </summary>
    private void Pagayer(Humain h, IReadOnlyList<Humain> tous, double chance)
    {
        if (h.Sait(Effet.Pagaie) || alea.Reel() >= chance * Inventivite(h)) return;
        var id = Retenir(h, Geste.Pousser, Matiere.Branche, Matiere.Eau, Effet.Pagaie, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
            recit: new Recit("inv.pagaie", ("h", h)));
        Montrer(h, Geste.Pousser, Matiere.Branche, Matiere.Eau, Effet.Pagaie, 0, Symptome.Aucun, tous, id);
    }

    // ------------------------------------------------------------------ En mer

    /// <summary>
    /// Un pas en mer : boire à sa calebasse, pêcher, dormir à bord quand on n'en peut plus ; décider de rentrer (soif,
    /// fatigue, mer qui grossit, rien en vue) ; pagayer vers le but (avec l'erreur de cap tant qu'il n'est pas en vue),
    /// dériver avec le courant et le vent ; aborder ; risquer de chavirer selon les vagues et le bateau.
    /// </summary>
    private void Voguer(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Mer.Motif == MotifMer.Passager)
        {
            Passager(h, tous);
            return;
        }
        var bateau = Bateau(h);
        if (bateau is null || Assemblage.Lire(bateau.Pieces) is not { } a)
        {
            // Plus de bateau : à la nage vers la rive la plus proche, s'il y en a une.
            h.Mer = default;
            if (!Climat.EstPraticable(carte.BiomeEn(h.X, h.Y)) && !RegagnerLaRive(h))
                Tuer(h, _maintenant, CauseDeMort.Noyade, new Recit("mer.noye.seul", ("h", h)), 0);
            return;
        }
        var m = h.Mer;
        var date = Calendrier.Decomposer(_maintenant);
        var i = carte.Indice(h.X, h.Y);
        var p = h.Personnalite;
        var heures = (_maintenant - m.Depuis) / (double)Calendrier.SecondesParHeure;
        var houleMax = Lois.HouleMax(a, bateau.Qualite);
        // Les roseaux se gorgent d'eau au fil des semaines.
        if (a.Corps == Matiere.Roseau) houleMax *= Math.Max(0.15, 1 - heures / 24 / (20 + 15 * bateau.Qualite));
        var houle = Houle(h.X, h.Y, _maintenant);
        var eau = h.Objets.Exists(o => o.Matiere is Matiere.Calebasse or Matiere.Peau or Matiere.Substance && o.Plein);
        if (h.Soif >= 50) BoireALaCalebasse(h);

        // Rentrer ?
        var versLeBut = Echelle.Kilometres(h.X, h.Y, m.CibleX, m.CibleY);
        var versLaRive = Echelle.Kilometres(h.X, h.Y, m.RiveX, m.RiveY);
        var rentrer = m.Motif switch
        {
            MotifMer.Peche => heures > 3 + 6 * p.Conscience || h.Fatigue > 70 || h.Soif > 60 && !eau || houle > 0.6 * houleMax,
            MotifMer.Curiosite => versLeBut < 1 || heures > 6 + 30 * Audace(h) || h.Soif > 55 && !eau || h.Fatigue > 75
                                  || houle > (0.4 + 0.4 * (1 - p.Prudence)) * houleMax,
            MotifMer.Expedition => (h.Soif > 70 && !eau || houle > 0.9 * houleMax && p.Prudence > 0.5) && versLaRive < versLeBut,
            MotifMer.Colon => h.Soif > 85 && !eau && versLaRive < 0.5 * versLeBut,
            _ => false,
        };
        // Ce que la dérive lui laisse : s'il voit qu'il n'avance plus vers son but (ou, pour la pêche et la curiosité, qu'il
        // ne pourrait plus revenir), il fait demi-tour tant que le rivage est proche.
        var pas = Lois.Vitesse(a, h.Sait(Effet.Pagaie)) * (0.6 + 0.4 * Force(h, _maintenant)) * (1 - h.Fatigue / 200)
                  * MondeSimule.SecondesParPas / Calendrier.SecondesParHeure / Echelle.KilometresParCase;
        var (wx, wy) = Derive(i, a, date.Saison);
        if (m.Motif == MotifMer.Expedition && heures > 1 && Progres(wx, wy, pas, h.X, h.Y, m.CibleX, m.CibleY) < 0.2 * pas
            && Progres(wx, wy, pas, h.X, h.Y, m.RiveX, m.RiveY) > 0) rentrer = true;
        // Pêche et curiosité : on rentre avant la nuit (le temps de revenir, d'après ce qu'on avance vers la rive).
        if (m.Motif is MotifMer.Peche or MotifMer.Curiosite)
        {
            var retour = Progres(wx, wy, pas, h.X, h.Y, m.RiveX, m.RiveY);
            var heuresDeRetour = versLaRive / Math.Max(0.01, retour * Echelle.KilometresParCase * Calendrier.SecondesParHeure / MondeSimule.SecondesParPas);
            if (retour < 0.3 * pas || date.Heure + heuresDeRetour + 1 >= 18) rentrer = true;
        }
        if (rentrer)
        {
            var spatial = Math.Clamp((p.Intelligence.Spatiale - 55) / 90, 0, 1);
            h.Mer = m = m with { Motif = MotifMer.Retour, CibleX = m.RiveX, CibleY = m.RiveY, Cap = (alea.Reel() - 0.5) * (30 - 20 * spatial) };
            versLeBut = versLaRive;
        }
        // La mer l'emporte loin de son but, ou la soif le presse sans eau à bord : gagner la terre la plus proche qu'on voit
        // et qu'on peut atteindre, quelle qu'elle soit.
        if ((_maintenant / MondeSimule.SecondesParPas + h.Id) % 6 == 0
            && (h.Soif > 60 && !eau || Progres(wx, wy, pas, h.X, h.Y, m.CibleX, m.CibleY) < 0.15 * pas)
            && TerreEnVue(h.X, h.Y, Visibilite(_maintenant), (tx, ty) => Progres(wx, wy, pas, h.X, h.Y, tx, ty) >= 0.2 * pas) is { } terre
            && (terre.X, terre.Y) != (m.CibleX, m.CibleY))
        {
            h.Mer = m = m with { Motif = m.Motif == MotifMer.Colon ? MotifMer.Colon : MotifMer.Retour, CibleX = terre.X, CibleY = terre.Y, Cap = 0 };
            versLeBut = Echelle.Kilometres(h.X, h.Y, terre.X, terre.Y);
        }

        var poissons = Poissons(i);
        var auCoin = m.Motif == MotifMer.Peche && Distance(h.X, h.Y, m.CibleX, m.CibleY) <= 1;
        // La terre toute proche, on ne s'endort pas : on pousse jusqu'au rivage tant qu'on tient.
        var terreProche = versLeBut <= 5 && Climat.EstPraticable(carte.BiomeEn(m.CibleX, m.CibleY));
        var dort = terreProche ? h.Fatigue > 92 : h.Fatigue > 80 || h.Activite == Activite.Dormir && h.Fatigue > 15 || !date.EstJour && h.Fatigue > 45;
        if (dort)
        {
            h.Activite = Activite.Dormir;
        }
        // On pêche au coin choisi ; en route, seulement quand la faim tenaille et qu'on n'a pas à lutter pour rentrer.
        else if ((auCoin || h.Faim > 70 && m.Motif != MotifMer.Retour) && poissons > 0.2 && h.Faim > 8)
        {
            h.Activite = Activite.Pecher;
            // Une prise par heure ou deux sur un récif, pour un pêcheur adroit (ligne, harpon, mains) ; un poisson d'un ou deux kilos.
            var ligne = 0.0;
            foreach (var o in h.Objets) if (o.Matiere == Matiere.Facture) ligne = Math.Max(ligne, Facons.Valeur(Fonction.Pecher, o));
            if (alea.Reel() < 0.12 * poissons * (0.5 + p.Adresse) * (1 + ligne)) h.Faim = Math.Max(0, h.Faim - (12 + 18 * alea.Reel()));
        }
        else h.Activite = Activite.Naviguer;
        h.CibleX = h.CibleY = -1;

        // Avancer : pagayer vers le but, et dériver avec le courant et le vent.
        double vx = wx, vy = wy;
        if (h.Activite == Activite.Naviguer)
        {
            var visibilite = Visibilite(_maintenant);
            var cibleEnVue = versLeBut <= Math.Min(visibilite, Nature.Mer.Horizon(Nature.Mer.HauteurDesYeux,
                Climat.EstPraticable(carte.BiomeEn(m.CibleX, m.CibleY)) ? HauteurVue(carte.Indice(m.CibleX, m.CibleY)) : 1));
            var cap = Math.Atan2(m.CibleY - h.Y - m.Dy, m.CibleX - h.X - m.Dx) + (cibleEnVue ? 0 : m.Cap * Math.PI / 180)
                      + (alea.Reel() - 0.5) * 0.15;
            var cases = pas;
            double ux = Math.Cos(cap), uy = Math.Sin(cap);
            // La terre en vue (le but ou le rivage quitté), il voit qu'il dérive et pagaie de biais pour tenir sa route.
            if (cibleEnVue || versLaRive <= visibilite)
            {
                var le = wx * ux + wy * uy;
                double tx = wx - le * ux, ty = wy - le * uy;
                var travers = Math.Sqrt(tx * tx + ty * ty);
                var reste = Math.Sqrt(Math.Max(0, cases * cases - travers * travers));
                (vx, vy) = travers >= cases ? (wx - tx / travers * cases, wy - ty / travers * cases) : (wx - tx + reste * ux, wy - ty + reste * uy);
            }
            else (vx, vy) = (wx + ux * cases, wy + uy * cases);
            if (heures > 1) Pagayer(h, tous, 0.002);
        }
        double dx = m.Dx + vx, dy = m.Dy + vy;
        while (Math.Abs(dx) >= 0.5 || Math.Abs(dy) >= 0.5)
        {
            int sx = Math.Abs(dx) >= 0.5 ? Math.Sign(dx) : 0, sy = Math.Abs(dy) >= 0.5 ? Math.Sign(dy) : 0;
            int nx = h.X + sx, ny = h.Y + sy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur)
            {
                // Le bord du monde connu : on fait demi-tour.
                (dx, dy) = (0, 0);
                if (h.Mer.Motif != MotifMer.Retour) h.Mer = h.Mer with { Motif = MotifMer.Retour, CibleX = m.RiveX, CibleY = m.RiveY };
                break;
            }
            (dx, dy) = (dx - sx, dy - sy);
            (h.X, h.Y) = (nx, ny);
            if (Climat.EstPraticable(carte.BiomeEn(nx, ny)))
            {
                Aborder(h, bateau, tous);
                return;
            }
        }
        h.Mer = h.Mer with { Dx = dx, Dy = dy };
        h.Fatigue = Math.Min(100, h.Fatigue + (h.Activite == Activite.Naviguer ? 0.2 : 0));

        // Les vagues.
        if (alea.Reel() < Lois.RisqueParHeure(houle, houleMax) * MondeSimule.SecondesParPas / Calendrier.SecondesParHeure)
            Chavirer(h, bateau, a, houle, tous);
    }

    /// <summary>
    /// Dérive d'un bateau en un pas (en cases) : le courant de surface, et le vent qui le pousse, environ 1,5 % de sa vitesse
    /// pour un radeau bas sur l'eau et moins pour une pirogue (ordres de grandeur des coefficients de dérive au vent).
    /// </summary>
    private (double X, double Y) Derive(int i, Assemblage a, Saison saison)
    {
        var (cx, cy) = carte.Mer.Courant(i, saison);
        var vent = Vent(_maintenant, out var depuis);
        var derive = (a.Montage == Montage.Creuse ? 0.008 : 0.015) * vent;
        var versOu = (depuis + 180) * Math.PI / 180;
        var parSeconde = MondeSimule.SecondesParPas / (Echelle.KilometresParCase * 1000);
        return ((cx + Math.Sin(versOu) * derive) * parSeconde, (cy - Math.Cos(versOu) * derive) * parSeconde);
    }

    /// <summary>
    /// Ce qu'on avance vers un point en un pas (en cases), en pagayant de biais contre la dérive : négatif si la dérive
    /// l'en éloigne malgré tout.
    /// </summary>
    private static double Progres(double wx, double wy, double pas, int x, int y, int cx, int cy)
    {
        double ux = cx - x, uy = cy - y;
        var n = Math.Sqrt(ux * ux + uy * uy);
        if (n < 1e-9) return pas;
        (ux, uy) = (ux / n, uy / n);
        var le = wx * ux + wy * uy;
        var travers = Math.Sqrt(Math.Max(0, wx * wx + wy * wy - le * le));
        return travers >= pas ? le : Math.Sqrt(pas * pas - travers * travers) + le;
    }

    /// <summary>La case de terre la plus proche qu'on voit d'un bateau (et qu'on peut atteindre, si un filtre est donné), ou null.</summary>
    private (int X, int Y)? TerreEnVue(int x, int y, double visibilite, Func<int, int, bool>? atteignable = null)
    {
        var rayon = Math.Min(60, Echelle.Cases(Math.Min(visibilite, 40)));
        for (var r = 1; r <= rayon; r++)
        {
            (int X, int Y)? meilleur = null;
            var plusPres = double.MaxValue;
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur || !Climat.EstPraticable(carte.BiomeEn(nx, ny))) continue;
                var km = Echelle.Kilometres(x, y, nx, ny);
                if (km < plusPres && km <= Math.Min(visibilite, Nature.Mer.Horizon(Nature.Mer.HauteurDesYeux, HauteurVue(carte.Indice(nx, ny))))
                    && (atteignable is null || atteignable(nx, ny)))
                    (plusPres, meilleur) = (km, (nx, ny));
            }
            if (meilleur is not null) return meilleur;
        }
        return null;
    }

    /// <summary>
    /// Une vague le renverse (ou remplit la coque) : il perd ce qui n'est pas attaché ; il remonte sur le radeau, ou vide la
    /// coque, s'il en a la force et sait nager ; sinon il se noie. Ses enfants à bord aussi risquent leur vie.
    /// </summary>
    private void Chavirer(Humain h, Objet bateau, Assemblage a, double houle, IReadOnlyList<Humain> tous)
    {
        for (var k = h.Objets.Count - 1; k >= 0; k--)
            if (h.Objets[k] != bateau && alea.Reel() < 0.7) h.Objets.RemoveAt(k);
        var remonte = (a.Montage == Montage.Creuse ? 0.45 : 0.6) + 0.35 * h.Nage - h.Fatigue / 250 - h.Froid / 250;
        var id = 0L;
        if (alea.Reel() >= remonte)
        {
            Tuer(h, _maintenant, CauseDeMort.Noyade, new Recit("mer.noye", ("h", h), ("bateau", Lois.Decrire(a)), ("houle", Math.Round(houle, 1))), 0);
        }
        else
        {
            h.Fatigue = Math.Min(100, h.Fatigue + 20);
            h.Froid = Math.Min(100, h.Froid + 10);
            h.Objets[h.Objets.IndexOf(bateau)] = bateau with { Qualite = Math.Max(0.05, bateau.Qualite - 0.1) };
            id = Noter(_maintenant, TypeEvenement.Mer, h, null, new Recit("mer.chavire", ("h", h), ("bateau", Lois.Decrire(a)), ("houle", Math.Round(houle, 1))));
        }
        var (pilote, maintenant) = (h.Id, _maintenant);
        Tour.Differer(tous, t =>
        {
            foreach (var e in t.Vivants())
            {
                if (!e.EstVivant || e.Mer.Motif != MotifMer.Passager || e.Mer.PiloteId != pilote) continue;
                if (alea.Reel() < 0.3 + 0.5 * e.Nage) continue;
                Tuer(e, maintenant, CauseDeMort.Noyade, new Recit("mer.noye.enfant", ("h", e)), id);
            }
        });
    }

    /// <summary>Un enfant à bord suit le bateau de celui qui le mène, et descend à terre avec lui.</summary>
    private void Passager(Humain h, IReadOnlyList<Humain> tous)
    {
        var pilote = h.Mer.PiloteId > 0 && h.Mer.PiloteId <= tous.Count ? tous[h.Mer.PiloteId - 1] : null;
        h.Activite = Activite.Naviguer;
        h.CibleX = h.CibleY = -1;
        if (pilote is null || !pilote.EstVivant || !pilote.Mer.EnMer)
        {
            // Arrivé (ou seul) : il descend là où est celui qui le menait, s'il est à terre ; sinon il nage.
            h.Mer = default;
            if (pilote is { EstVivant: true } && Climat.EstPraticable(carte.BiomeEn(pilote.X, pilote.Y))) (h.X, h.Y) = (pilote.X, pilote.Y);
            return;
        }
        (h.X, h.Y) = (pilote.X, pilote.Y);
        Tour.Differer(h, pilote, (e, q) =>
        {
            if (e.EstVivant && q.EstVivant && e.Mer.Motif == MotifMer.Passager) (e.X, e.Y) = (q.X, q.Y);
        });
    }

    /// <summary>
    /// Toucher terre. Sur sa propre terre : le voyage est fini, il raconte ce qu'il a vu. Sur une autre : la première fois pour
    /// les siens, c'est un fait marquant ; il retient le rivage d'où il est parti (le chemin du retour), puis s'installe ou
    /// reste quelques jours avant de rentrer.
    /// </summary>
    private void Aborder(Humain h, Objet bateau, IReadOnlyList<Humain> tous)
    {
        var m = h.Mer;
        var ici = MasseEn(h.X, h.Y);
        var but = MasseEn(m.CibleX, m.CibleY);
        h.Objets[h.Objets.IndexOf(bateau)] = bateau with { Plein = false };
        h.Activite = Activite.Naviguer;
        var chezSoi = m.Motif == MotifMer.Retour && ici == but || ici == m.Masse;
        if (chezSoi)
        {
            h.Mer = default;
            if (m.Motif != MotifMer.Peche || m.Vu)
            {
                Noter(_maintenant, TypeEvenement.Mer, h, null, new Recit(m.Vu ? "mer.rentre.vu" : "mer.rentre", ("h", h)));
                RaconterLaMer(h, tous);
            }
            return;
        }
        // Une autre terre.
        h.Memoire.Retenir(TypeLieu.TerreAuLoin, m.RiveX, m.RiveY, _maintenant, Aptitude(h), rappels: 6);
        var (x, y, maintenant) = (h.X, h.Y, _maintenant);
        var recit = new Recit("mer.pied", ("h", h), ("terre", Recit.Seul(carte.Mer.Iles.Any(i => i.Masse == ici) ? "mer.uneIle" : "mer.uneTerre")),
            ("km", (long)Math.Round(Echelle.Kilometres(m.RiveX, m.RiveY, x, y))), ("heures", (long)Math.Round((maintenant - m.Depuis) / 3600.0)));
        Tour.Differer(h, q =>
        {
            var groupe = Outremer.GroupeDe(q);
            var premiere = !Outremer.Connait(groupe, ici, SorteOutremer.Pied);
            var id = Noter(maintenant, TypeEvenement.Mer, q, null, recit, significatif: premiere);
            if (premiere) Outremer.Ajouter(new DecouverteOutremer(groupe, ici, SorteOutremer.Pied, maintenant, x, y, q.Id, id));
            // Y avoir posé le pied vaut bien l'avoir vue.
            if (!Outremer.Connait(groupe, ici, SorteOutremer.Vue)) Outremer.Ajouter(new DecouverteOutremer(groupe, ici, SorteOutremer.Vue, maintenant, x, y, q.Id, id));
        });
        if (m.Motif == MotifMer.Colon || m.Motif == MotifMer.Retour)
        {
            h.Mer = default;
            return;
        }
        // Quelques jours pour voir cette terre, puis le retour.
        var sejour = (long)((2 + 8 * h.Personnalite.Curiosite * alea.Reel()) * Calendrier.SecondesParJour);
        h.Mer = new Traversee(MotifMer.Sejour, false, x, y, m.RiveX, m.RiveY, _maintenant + sejour, Masse: ici, Vu: true);
    }

    /// <summary>
    /// Sur une terre qui n'est pas celle de son peuple : rencontrer quelqu'un qui en est. La première rencontre entre deux
    /// groupes de terres différentes est un fait marquant, pour les uns comme pour les autres.
    /// </summary>
    private void RencontrerOutremer(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Continent <= 0 || h.EstPetitEnfant(_maintenant) || (_maintenant / MondeSimule.SecondesParPas + h.Id) % 6 != 3) return;
        var ici = MasseEn(h.X, h.Y);
        if (ici == 0 || ici == h.Continent || Outremer.Connait(Outremer.GroupeDe(h), ici, SorteOutremer.Contact)) return;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.Continent != ici || o.EstPetitEnfant(_maintenant) || Distance(h.X, h.Y, o.X, o.Y) > 2) continue;
            var (maintenant, origine) = (_maintenant, h.Continent);
            Tour.Differer(h, o, (a, b) =>
            {
                var ga = Outremer.GroupeDe(a);
                var gb = Outremer.GroupeDe(b);
                if (Outremer.Connait(ga, ici, SorteOutremer.Contact)) return;
                var id = Noter(maintenant, TypeEvenement.Mer, a, b, new Recit("mer.contact", ("h", a), ("a", b)), significatif: true,
                    autreCommunaute: b.CommunauteId);
                Outremer.Ajouter(new DecouverteOutremer(ga, ici, SorteOutremer.Contact, maintenant, a.X, a.Y, a.Id, id));
                Outremer.Ajouter(new DecouverteOutremer(gb, origine, SorteOutremer.Contact, maintenant, a.X, a.Y, b.Id, id));
            });
            return;
        }
    }

    /// <summary>Le peuple d'un humain : celui de sa mère, sinon la terre où il vit.</summary>
    private void EtablirContinent(Humain h, IReadOnlyList<Humain> tous)
    {
        var mere = h.MereId > 0 && h.MereId <= tous.Count ? tous[h.MereId - 1] : null;
        h.Continent = mere is { Continent: > 0 } ? mere.Continent : MasseEn(h.X, h.Y);
    }

    // ------------------------------------------------------------------ Partir par la mer avec les siens

    /// <summary>
    /// Le groupe part vers une terre de l'autre côté de la mer : arrivé au bord, chacun prépare son départ (le voyage d'un
    /// colon). Vrai s'il s'y met.
    /// </summary>
    private bool PartirParLaMer(Humain h, int cibleX, int cibleY)
    {
        if (h.EstEnfant(_maintenant) || h.Mer.Active) return false;
        // Seul celui qui sait faire un bateau de mer (ou en a un) s'y risque ; les autres restent.
        if (!(Bateau(h) is { } b && TientLaMer(b)) && !h.Savoirs.Exists(s => s.Effet == Effet.Navigation && s.Confiance >= 0.3)) return false;
        var ici = MasseEn(h.X, h.Y);
        var la = MasseEn(cibleX, cibleY);
        if (ici == 0 || la == 0 || ici == la) return false;
        var rive = Abords.RiveVers(carte, h.X, h.Y, cibleX, cibleY);
        if (rive.X < 0) return false;
        h.Mer = new Traversee(MotifMer.Colon, false, rive.X, rive.Y, cibleX, cibleY, _maintenant, Masse: ici);
        return true;
    }

    /// <summary>Choisir le rivage d'un départ décidé par les siens (expédition, colons) : voir <see cref="VieSociale"/>.</summary>
    internal static Traversee? Voyage(Carte carte, Humain h, MotifMer motif, int cibleX, int cibleY, long maintenant)
    {
        var rive = AbordsDeLaMer.De(carte).RiveVers(carte, h.X, h.Y, cibleX, cibleY);
        if (rive.X < 0) return null;
        return new Traversee(motif, false, rive.X, rive.Y, cibleX, cibleY, maintenant, Masse: carte.Mer.Masse[carte.Indice(h.X, h.Y)]);
    }

    /// <summary>Calcule d'avance ce que les humains liront de la mer pendant un pas simultané.</summary>
    private void PreparerLaMer() => _ = Abords;
}
