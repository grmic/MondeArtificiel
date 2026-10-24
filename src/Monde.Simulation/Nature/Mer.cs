using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>Fond de la mer : plateau continental, talus, puis le large.</summary>
public enum Fond : byte
{
    Terre = 0,

    /// <summary>Jusqu'à 200 m : le plateau qui borde les côtes (le rebord des plateaux actuels est vers 130 à 200 m).</summary>
    Plateau = 1,

    /// <summary>De 200 à 3 000 m : la pente vers les grands fonds.</summary>
    Talus = 2,

    /// <summary>Au-delà de 3 000 m.</summary>
    Large = 3,
}

/// <summary>Nature d'un rivage, côté terre : on n'embarque pas au pied d'une falaise.</summary>
public enum Rivage : byte
{
    Aucun = 0,
    Plage = 1,
    Falaise = 2,
    CoteRocheuse = 3,
    Estuaire = 4,
    Delta = 5,
    Marecage = 6,

    /// <summary>Mangrove : racines dans l'eau, vase et chenaux ; on n'y aborde qu'en suivant un chenal.</summary>
    Mangrove = 7,
}

/// <summary>Une île (terre trop petite pour être un continent) et la mer qui la sépare de la terre la plus proche.</summary>
/// <param name="Masse">Numéro de la masse de terre (voir <see cref="Mer.Masse"/>).</param>
/// <param name="DistanceKm">Mer à traverser jusqu'à la terre la plus proche (continent ou autre île), en km.</param>
public sealed record Ile(int Masse, int Cases, int X, int Y, int AltitudeMaxM, double DistanceKm);

/// <summary>
/// Le plus court passage par mer entre deux masses de terre, et ce qu'on voit de l'autre rive : depuis le rivage (yeux à
/// 1,7 m) ou depuis le plus haut sommet de la sienne.
/// </summary>
public sealed record Passage(int MasseA, int MasseB, double Km, int DepuisA, int VersB, bool VueDuRivage, bool VueDuSommet);

/// <summary>Vent dominant d'une saison : d'où il souffle (degrés, 0 = du nord, 90 = de l'est) et sa force moyenne (m/s).</summary>
public readonly record struct Vent(double Depuis, double Vitesse);

/// <summary>
/// La mer autour des terres : ses fonds, ses rivages, ses îles et les passages entre les terres, les vents dominants et
/// les courants de surface. Rien ne s'en sert encore pour naviguer (0.32.4) : la mer est décrite une fois pour toutes,
/// à partir de la graine et de la carte, comme le <see cref="Terrain"/>.
/// </summary>
public sealed class Mer
{
    private static readonly (int Dx, int Dy)[] Voisins4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];
    private static readonly (int Dx, int Dy)[] Voisins8 = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary>Hauteur des yeux d'un adulte debout, en mètres.</summary>
    public const double HauteurDesYeux = 1.7;

    /// <summary>
    /// Au-delà, même une haute montagne se perd dans la brume : par air clair et sec on voit parfois 150 km et plus, mais
    /// l'air chaud et humide de ce monde voile bien avant. Hypothèse retenue : 120 km.
    /// </summary>
    public const double VueMaxKm = 120;

    /// <summary>Au-delà, deux terres ne sont plus voisines par la mer : aucun passage n'est noté.</summary>
    public const double DistanceMaxPassageKm = 300;

    private readonly int _largeur;

    // Direction de la côte la plus proche, vue de la mer (vecteur unitaire, x vers l'est, y vers le sud) : les courants
    // côtiers la longent au lieu de s'y jeter.
    private readonly float[] _versLaCoteX;
    private readonly float[] _versLaCoteY;

    private Mer(int largeur, float[] versLaCoteX, float[] versLaCoteY) => (_largeur, _versLaCoteX, _versLaCoteY) = (largeur, versLaCoteX, versLaCoteY);

    /// <summary>Profondeur de la mer en mètres (0 sur les terres, les lacs et les rivières).</summary>
    public required short[] Profondeur { get; init; }

    public required Fond[] Fonds { get; init; }

    /// <summary>Haut-fond au large : moins de 20 m d'eau à plus de 3 km de la côte.</summary>
    public required bool[] HautFond { get; init; }

    /// <summary>Récif de corail (ou de rudistes, au Crétacé) : eau chaude, claire et peu profonde, loin des bouches des rivières.</summary>
    public required bool[] Recif { get; init; }

    /// <summary>Nature du rivage des cases de terre qui touchent la mer.</summary>
    public required Rivage[] Rivages { get; init; }

    /// <summary>Distance à la terre la plus proche, en cases (0 sur les terres).</summary>
    public required short[] DistanceCote { get; init; }

    /// <summary>Masse de terre de chaque case (0 : la mer), reliée par les côtés des cases.</summary>
    public required int[] Masse { get; init; }

    /// <summary>Masses qui sont des continents, de la plus grande à la plus petite.</summary>
    public required IReadOnlyList<int> Continents { get; init; }

    public required IReadOnlyList<Ile> Iles { get; init; }

    /// <summary>Plus courts passages entre deux masses de terre voisines par la mer (moins de 300 km).</summary>
    public required IReadOnlyList<Passage> Passages { get; init; }

    /// <summary>Vents dominants du printemps, de l'été, de l'automne et de l'hiver.</summary>
    public required Vent[] Vents { get; init; }

    /// <summary>
    /// Distance jusqu'à laquelle deux points se voient par-dessus la courbure de la planète (rayon terrestre, réfraction
    /// ordinaire comprise) : 3,57 × (√h₁ + √h₂) km, hauteurs en mètres.
    /// </summary>
    public static double Horizon(double h1, double h2) => 3.57 * (Math.Sqrt(Math.Max(0, h1)) + Math.Sqrt(Math.Max(0, h2)));

    /// <summary>
    /// Courant de surface d'une case de mer en une saison (m/s, x vers l'est, y vers le sud) : le vent entraîne la surface à
    /// environ 3 % de sa vitesse ; près des côtes, le courant les longe.
    /// </summary>
    public (double X, double Y) Courant(int i, Saison saison)
    {
        if (Fonds[i] == Fond.Terre) return (0, 0);
        var v = Vents[(int)saison];
        var vers = (v.Depuis + 180) * Math.PI / 180;
        double x = Math.Sin(vers) * v.Vitesse * 0.03, y = -Math.Cos(vers) * v.Vitesse * 0.03;
        if (DistanceCote[i] <= 3 && (_versLaCoteX[i] != 0 || _versLaCoteY[i] != 0))
        {
            // On retire la part qui pousse vers la côte ou l'en écarte : il reste le long du rivage, un peu renforcé.
            var p = x * _versLaCoteX[i] + y * _versLaCoteY[i];
            (x, y) = ((x - p * _versLaCoteX[i]) * 1.3, (y - p * _versLaCoteY[i]) * 1.3);
        }
        return (x, y);
    }

    public Rivage RivageA(int x, int y) => Rivages[y * _largeur + x];

    /// <summary>La mer d'une carte, tirée de sa graine.</summary>
    public static Mer Calculer(Carte carte)
    {
        int l = carte.Largeur, h = carte.Hauteur, n = l * h;
        var terrain = carte.Terrain;
        bool Sale(int i) => Biomes.EstEauSalee((Biome)carte.Biomes[i]);

        // 1. Masses de terre (4-voisinage, lacs et rivières compris) et continents, comme la génération les compte.
        var masse = new int[n];
        var tailles = new List<int> { 0 };
        var file = new Queue<int>();
        for (var depart = 0; depart < n; depart++)
        {
            if (Sale(depart) || masse[depart] != 0) continue;
            var numero = tailles.Count;
            var taille = 0;
            masse[depart] = numero;
            file.Enqueue(depart);
            while (file.Count > 0)
            {
                var i = file.Dequeue();
                taille++;
                foreach (var j in Voisins(i, l, h, Voisins4))
                {
                    if (masse[j] == 0 && !Sale(j))
                    {
                        masse[j] = numero;
                        file.Enqueue(j);
                    }
                }
            }
            tailles.Add(taille);
        }
        var seuil = tailles.Count > 1 ? Math.Max(200 / Echelle.KilometresCarres(1), tailles.Skip(1).Max() * 0.25) : 0;
        var plusGrande = tailles.Count > 1 ? tailles.IndexOf(tailles.Skip(1).Max()) : 0;
        var continents = Enumerable.Range(1, tailles.Count - 1).Where(k => k == plusGrande || tailles[k] >= seuil)
            .OrderByDescending(k => tailles[k]).ThenBy(k => k).ToList();

        // Plus haut point de chaque masse.
        var sommets = new int[tailles.Count];
        Array.Fill(sommets, -1);
        for (var i = 0; i < n; i++)
        {
            if (masse[i] == 0) continue;
            ref var s = ref sommets[masse[i]];
            if (s < 0 || carte.Altitudes[i] > carte.Altitudes[s]) s = i;
        }

        // 2. Distance à la terre, case de rivage la plus proche et masse dont elle relève (partage de la mer entre les terres).
        var distance = new short[n];
        var origine = new int[n];
        Array.Fill(distance, short.MaxValue);
        Array.Fill(origine, -1);
        for (var i = 0; i < n; i++)
        {
            if (masse[i] == 0) continue;
            distance[i] = 0;
            origine[i] = i;
            if (Voisins(i, l, h, Voisins8).Any(Sale)) file.Enqueue(i);
        }
        var passages = new Dictionary<(int, int), (double Km, int A, int B)>();
        while (file.Count > 0)
        {
            var i = file.Dequeue();
            foreach (var j in Voisins(i, l, h, Voisins8))
            {
                if (masse[j] != 0) continue;
                if (distance[j] == short.MaxValue)
                {
                    distance[j] = (short)(distance[i] + 1);
                    origine[j] = origine[i];
                    file.Enqueue(j);
                    continue;
                }
                // Deux fronts venus de terres différentes se rencontrent : un passage possible entre elles.
                int ma = masse[origine[i]], mb = masse[origine[j]];
                if (ma == mb) continue;
                var (a, b) = ma < mb ? (origine[i], origine[j]) : (origine[j], origine[i]);
                var km = Math.Max(0, Echelle.Kilometres(a % l, a / l, b % l, b / l) - Echelle.KilometresParCase);
                var cle = (Math.Min(ma, mb), Math.Max(ma, mb));
                if (km < DistanceMaxPassageKm && (!passages.TryGetValue(cle, out var p) || km < p.Km)) passages[cle] = (km, a, b);
            }
        }

        // 3. Profondeur : l'altitude sous la mer, adoucie près des côtes (le plateau descend doucement, quelques mètres par km).
        var profondeur = new short[n];
        var fonds = new Fond[n];
        var hautFond = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (!Sale(i)) continue;
            var parAltitude = (Carte.NiveauMer - carte.Altitudes[i]) * Echelle.MetresParUnite - Echelle.MetresParUnite / 2;
            var parCote = 3 + 15 * Echelle.Kilometres(distance[i]);
            var m = Math.Max(2, Math.Min(parAltitude, parCote));
            profondeur[i] = (short)Math.Round(m);
            fonds[i] = m <= 200 ? Fond.Plateau : m <= 3000 ? Fond.Talus : Fond.Large;
            hautFond[i] = m <= 20 && distance[i] >= 4;
        }

        // 4. Rivages.
        var rivages = new Rivage[n];
        var bouches = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (Sale(i) || !Voisins(i, l, h, Voisins4).Any(Sale)) continue;
            var b = (Biome)carte.Biomes[i];
            var dure = terrain.Roches[i] is Roche.Granite or Roche.Basalte or Roche.Volcanique or Roche.Calcaire or Roche.Gres;
            rivages[i] = b switch
            {
                Biome.Riviere or Biome.Lac => terrain.Debit[i] >= 20 && terrain.Pente[i] <= 1 ? Rivage.Delta : Rivage.Estuaire,
                Biome.Marais => Rivage.Marecage,
                Biome.Mangrove => Rivage.Mangrove,
                _ when terrain.Falaise[i] || terrain.Pente[i] >= 3 => Rivage.Falaise,
                Biome.Plage => Rivage.Plage,
                _ when dure && terrain.Pente[i] >= 1 => Rivage.CoteRocheuse,
                _ when terrain.Sols[i] is Sol.Sableux or Sol.Alluvial || terrain.Roches[i] == Roche.Alluvions => Rivage.Plage,
                _ => dure ? Rivage.CoteRocheuse : Rivage.Plage,
            };
            if (rivages[i] is Rivage.Estuaire or Rivage.Delta) bouches.Add(i);
        }

        // 5. Récifs : mer chaude (comme pour les forêts tropicales, loin des bords nord et sud), moins de 30 m d'eau,
        // pas d'eau douce et boueuse à moins de 4 km d'une bouche de rivière, en taches.
        var tache = new Bruit(carte.Graine ^ 0x5245434946UL);
        var pres = new bool[n];
        foreach (var bouche in bouches)
        {
            int bx = bouche % l, by = bouche / l;
            for (var dy = -5; dy <= 5; dy++)
            for (var dx = -5; dx <= 5; dx++)
            {
                int x = bx + dx, y = by + dy;
                if (x >= 0 && y >= 0 && x < l && y < h) pres[y * l + x] = true;
            }
        }
        var recif = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (!Sale(i) || profondeur[i] > 30 || distance[i] > 8 || pres[i]) continue;
            var latitude = Math.Abs((i / l + 0.5) / h - 0.5) * 2;
            if (latitude > 0.5) continue;
            recif[i] = tache.Fractal(i % l / 12.0, i / l / 12.0, 3) > 0.55;
        }

        // 6. Direction de la côte la plus proche, pour les courants côtiers.
        var versX = new float[n];
        var versY = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (masse[i] != 0 || distance[i] > 3 || origine[i] < 0) continue;
            double dx = origine[i] % l - i % l, dy = origine[i] / l - i / l;
            var norme = Math.Sqrt(dx * dx + dy * dy);
            if (norme > 0) (versX[i], versY[i]) = ((float)(dx / norme), (float)(dy / norme));
        }

        // 7. Îles et passages, avec ce qu'on voit de l'autre rive.
        double Metres(int i) => Math.Max(0, Echelle.Metres(carte.Altitudes[i]));
        double Km(int a, int b) => Echelle.Kilometres(a % l, a / l, b % l, b / l);
        bool Voit(double km, double h1, double h2) => km <= Math.Min(VueMaxKm, Horizon(h1, h2));
        var liste = new List<Passage>();
        foreach (var ((ma, mb), (km, a, b)) in passages.OrderBy(p => p.Key))
        {
            // Du rivage : voir le rivage d'en face, ou le sommet de l'autre terre. Du sommet : de même.
            var duRivage = Voit(km, HauteurDesYeux, HauteurDesYeux) || Voit(Km(a, sommets[mb]), HauteurDesYeux, Metres(sommets[mb]))
                           || Voit(Km(b, sommets[ma]), HauteurDesYeux, Metres(sommets[ma]));
            var duSommet = Voit(Km(sommets[ma], b), Metres(sommets[ma]) + HauteurDesYeux, HauteurDesYeux)
                           || Voit(Km(sommets[ma], sommets[mb]), Metres(sommets[ma]) + HauteurDesYeux, Metres(sommets[mb]))
                           || Voit(Km(sommets[mb], a), Metres(sommets[mb]) + HauteurDesYeux, HauteurDesYeux);
            liste.Add(new Passage(ma, mb, Math.Round(km, 1), a, b, duRivage, duRivage || duSommet));
        }
        var iles = new List<Ile>();
        for (var k = 1; k < tailles.Count; k++)
        {
            if (continents.Contains(k)) continue;
            var proche = liste.Where(p => p.MasseA == k || p.MasseB == k).Select(p => p.Km).DefaultIfEmpty(double.NaN).Min();
            var s = sommets[k];
            iles.Add(new Ile(k, tailles[k], s % l, s / l, Echelle.Metres(carte.Altitudes[s]), proche));
        }

        // 8. Vents dominants : sous ce climat chaud, des alizés venus de l'est, que la mousson d'été peut retourner vers la
        // terre ; tirés une fois pour toutes de la graine.
        var alea = new Alea(carte.Graine ^ 0x56454E54UL);
        var base0 = 60 + 60 * alea.Reel();
        var force = 4 + 3 * alea.Reel();
        var mousson = alea.Reel() < 0.5 ? 0 : (90 + 90 * alea.Reel()) * (alea.Reel() < 0.5 ? -1 : 1);
        double Angle(double a) => (a % 360 + 360) % 360;
        Vent[] vents =
        [
            new(Math.Round(Angle(base0 + mousson / 2)), Math.Round(force, 1)),
            new(Math.Round(Angle(base0 + mousson)), Math.Round(force * (mousson == 0 ? 0.9 : 1.1), 1)),
            new(Math.Round(Angle(base0 + mousson / 2)), Math.Round(force, 1)),
            new(Math.Round(Angle(base0)), Math.Round(force * 1.15, 1)),
        ];

        return new Mer(l, versX, versY)
        {
            Profondeur = profondeur, Fonds = fonds, HautFond = hautFond, Recif = recif, Rivages = rivages, DistanceCote = distance,
            Masse = masse, Continents = continents, Iles = iles, Passages = liste, Vents = vents,
        };
    }

    private static IEnumerable<int> Voisins(int i, int l, int h, (int Dx, int Dy)[] voisins)
    {
        int x = i % l, y = i / l;
        foreach (var (dx, dy) in voisins)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < l && ny < h) yield return ny * l + nx;
        }
    }
}
