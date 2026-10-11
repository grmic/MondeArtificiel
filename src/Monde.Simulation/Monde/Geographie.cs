using Monde.Simulation.Humains;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Monde;

/// <summary>Surface d'un type de terrain.</summary>
public sealed record SurfaceBiome(Biome Biome, int Cases, double Km2);

/// <summary>Températures de midi au milieu d'une saison : la plus basse et la plus haute des terres.</summary>
public sealed record TemperaturesSaison(Saison Saison, double Min, double Max);

/// <summary>Un trajet à pied : les cases traversées, sa longueur et le temps qu'il faut à un adulte.</summary>
/// <param name="Effort">Longueur « en terrain plat » (m) : la longueur réelle allongée par le terrain et les pentes.</param>
public sealed record Trajet(IReadOnlyList<int> Cases, double Kilometres, double Effort, double Heures);

/// <summary>
/// Mesures du continent (le plus grand, celui des couples de départ), en kilomètres réels : surface, dimensions, côtes, relief, eaux, terrains et climat.
/// Elles ne dépendent que de la carte : on les calcule une fois par monde.
/// </summary>
public sealed record Geographie(
    double LargeurCarteKm, double HauteurCarteKm, double SurfaceCarteKm2,
    double ContinentKm2, double TerresKm2, double LacsKm2, int Lacs, double RivieresKm,
    int Iles, double IlesKm2, IReadOnlyList<double> AutresContinentsKm2,
    double EtendueEstOuestKm, double EtendueNordSudKm, double PlusGrandeLongueurKm, int LongueurDe, int LongueurA,
    double CotesKm, double PlusLoinDeLaMerKm, int PlusLoinDeLaMer,
    int AltitudeMaxM, int Sommet, int AltitudeMoyenneM, IReadOnlyList<(string Tranche, double Km2)> Altitudes,
    IReadOnlyList<SurfaceBiome> Biomes, IReadOnlyList<TemperaturesSaison> Temperatures, Trajet? Traversee)
{
    private static readonly (int Dx, int Dy)[] Voisins4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int Dx, int Dy)[] Voisins8 =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    public static Geographie Calculer(Carte carte)
    {
        int l = carte.Largeur, n = carte.Largeur * carte.Hauteur;
        var km2 = Echelle.KilometresCarres(1);
        bool Sale(int i) => Simulation.Monde.Biomes.EstEauSalee((Biome)carte.Biomes[i]);

        // Masses de terre (lacs et rivières compris), reliées par les côtés des cases : la plus grande est le continent.
        var masse = new int[n];
        var tailles = new List<int> { 0 };
        for (var i = 0; i < n; i++)
        {
            if (Sale(i) || masse[i] != 0) continue;
            var numero = tailles.Count;
            tailles.Add(Remplir(carte, i, masse, numero, j => !Sale(j)));
        }
        var continent = tailles.Count > 1 ? tailles.IndexOf(tailles.Skip(1).Max()) : 0;

        int terres = 0, lacs = 0, rivieres = 0, cotes = 0, sommet = -1, minX = l, maxX = -1, minY = carte.Hauteur, maxY = -1;
        long sommeAltitudes = 0;
        var bords = new List<int>();
        var tranches = new int[4];
        for (var i = 0; i < n; i++)
        {
            if (masse[i] != continent || continent == 0) continue;
            int x = i % l, y = i / l;
            (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
            var b = (Biome)carte.Biomes[i];
            if (b == Biome.Lac) lacs++;
            else
            {
                terres++;
                if (b == Biome.Riviere) rivieres++;
                var m = Echelle.Metres(carte.Altitudes[i]);
                sommeAltitudes += Math.Max(0, m);
                tranches[m switch { < 200 => 0, < 1000 => 1, < 2000 => 2, _ => 3 }]++;
                if (sommet < 0 || carte.Altitudes[i] > carte.Altitudes[sommet]) sommet = i;
            }
            var cote = false;
            foreach (var (dx, dy) in Voisins4)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= l || ny >= carte.Hauteur || Sale(ny * l + nx)) cote = true;
                if (nx >= 0 && ny >= 0 && nx < l && ny < carte.Hauteur && Sale(ny * l + nx)) cotes++;
            }
            if (cote) bords.Add(i);
        }

        // Plus grande longueur : les deux points de la côte les plus éloignés l'un de l'autre.
        var (de, a, longueur2) = (-1, -1, -1L);
        for (var p = 0; p < bords.Count; p++)
        {
            int x1 = bords[p] % l, y1 = bords[p] / l;
            for (var q = p + 1; q < bords.Count; q++)
            {
                int dx = bords[q] % l - x1, dy = bords[q] / l - y1;
                var d2 = (long)dx * dx + (long)dy * dy;
                if (d2 > longueur2) (de, a, longueur2) = (bords[p], bords[q], d2);
            }
        }

        var lacsSepares = 0;
        var vus = new int[n];
        for (var i = 0; i < n; i++)
        {
            if (masse[i] != continent || continent == 0 || carte.Biomes[i] != (byte)Biome.Lac || vus[i] != 0) continue;
            lacsSepares++;
            Remplir(carte, i, vus, 1, j => carte.Biomes[j] == (byte)Biome.Lac);
        }

        var (loin, loinKm) = PlusLoinDeLaMerCalcul(carte, masse, continent);

        var biomes = Enumerable.Range(0, Simulation.Monde.Biomes.Noms.Count)
            .Select(b => (Biome)b)
            .Select(b => { var c = carte.Biomes.Count(v => v == (byte)b); return new SurfaceBiome(b, c, c * km2); })
            .Where(s => s.Cases > 0).OrderByDescending(s => s.Cases).ToList();

        string[] nomsTranches = ["moins de 200 m", "200 à 1 000 m", "1 000 à 2 000 m", "plus de 2 000 m"];
        // Les autres masses de terre : un continent si elle fait au moins le quart du plus grand (et 200 km²), une île sinon.
        // Repère : le Groenland, plus grande île (2,2 millions de km²), fait un peu plus du quart de l'Australie (7,7).
        var autresMasses = tailles.Skip(1).Where((_, k) => k + 1 != continent).ToList();
        var seuilContinent = Math.Max(200 / km2, continent == 0 ? 0 : tailles[continent] * 0.25);
        var autresContinents = autresMasses.Where(t => t >= seuilContinent).OrderDescending().Select(t => t * km2).ToList();
        var ilesListe = autresMasses.Where(t => t < seuilContinent).ToList();
        return new Geographie(
            Echelle.Kilometres(carte.Largeur), Echelle.Kilometres(carte.Hauteur), Echelle.KilometresCarres(n),
            (terres + lacs) * km2, terres * km2, lacs * km2, lacsSepares, Echelle.Kilometres(rivieres),
            ilesListe.Count, ilesListe.Sum() * km2, autresContinents,
            maxX < 0 ? 0 : Echelle.Kilometres(maxX - minX + 1), maxY < 0 ? 0 : Echelle.Kilometres(maxY - minY + 1),
            longueur2 < 0 ? 0 : Echelle.Kilometres(Math.Sqrt(longueur2)), de, a,
            Echelle.Kilometres(cotes), loinKm, loin,
            sommet < 0 ? 0 : Echelle.Metres(carte.Altitudes[sommet]), sommet, terres == 0 ? 0 : (int)(sommeAltitudes / terres),
            [.. nomsTranches.Select((t, k) => (t, tranches[k] * km2))],
            biomes, TemperaturesDesSaisons(carte, masse, continent),
            de >= 0 && a >= 0 ? CalculerTrajet(carte, de, a) : null);
    }

    /// <summary>
    /// Continents et îles, en cases, du plus grand au plus petit : une masse de terre est un continent si elle fait
    /// au moins le quart du plus grand et 200 km² (voir <see cref="Calculer"/>). Calcul rapide, pour un aperçu.
    /// </summary>
    public static (IReadOnlyList<int> Continents, IReadOnlyList<int> Iles) MassesDeTerre(Carte carte)
    {
        var masse = new int[carte.Largeur * carte.Hauteur];
        var tailles = new List<int>();
        for (var i = 0; i < masse.Length; i++)
        {
            if (masse[i] != 0 || Simulation.Monde.Biomes.EstEauSalee((Biome)carte.Biomes[i])) continue;
            tailles.Add(Remplir(carte, i, masse, tailles.Count + 1, j => !Simulation.Monde.Biomes.EstEauSalee((Biome)carte.Biomes[j])));
        }
        tailles.Sort((a, b) => b.CompareTo(a));
        var seuil = tailles.Count == 0 ? 0 : Math.Max(200 / Echelle.KilometresCarres(1), tailles[0] * 0.25);
        return (tailles.Where((t, k) => k == 0 || t >= seuil).ToList(), tailles.Where((t, k) => k > 0 && t < seuil).ToList());
    }

    /// <summary>Remplit une zone (voisins par les côtés) et renvoie son nombre de cases.</summary>
    private static int Remplir(Carte carte, int depart, int[] marques, int numero, Func<int, bool> dedans)
    {
        var pile = new Stack<int>();
        pile.Push(depart);
        marques[depart] = numero;
        var taille = 0;
        while (pile.Count > 0)
        {
            var i = pile.Pop();
            taille++;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            foreach (var (dx, dy) in Voisins4)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = ny * carte.Largeur + nx;
                if (marques[j] != 0 || !dedans(j)) continue;
                marques[j] = numero;
                pile.Push(j);
            }
        }
        return taille;
    }

    /// <summary>Le point du continent le plus éloigné de la mer (distance à vol d'oiseau, en km).</summary>
    private static (int Case, double Km) PlusLoinDeLaMerCalcul(Carte carte, int[] masse, int continent)
    {
        var mer = new List<(int X, int Y)>();
        for (var i = 0; i < masse.Length; i++)
        {
            if (!Simulation.Monde.Biomes.EstEauSalee((Biome)carte.Biomes[i])) continue;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            // Seules les cases de mer qui touchent une terre comptent : le reste est plus loin.
            if (Voisins4.Any(v => x + v.Dx >= 0 && y + v.Dy >= 0 && x + v.Dx < carte.Largeur && y + v.Dy < carte.Hauteur
                                   && !Simulation.Monde.Biomes.EstEauSalee(carte.BiomeEn(x + v.Dx, y + v.Dy))))
            {
                mer.Add((x, y));
            }
        }
        var (meilleur, meilleurD2) = (-1, -1L);
        if (mer.Count == 0) return (-1, 0);
        for (var i = 0; i < masse.Length; i++)
        {
            if (masse[i] != continent || continent == 0) continue;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            var d2 = long.MaxValue;
            foreach (var (mx, my) in mer)
            {
                var d = (long)(mx - x) * (mx - x) + (long)(my - y) * (my - y);
                if (d < d2) d2 = d;
                if (d2 <= meilleurD2) break; // ne battra pas le record
            }
            if (d2 > meilleurD2) (meilleur, meilleurD2) = (i, d2);
        }
        return (meilleur, meilleur < 0 ? 0 : Echelle.Kilometres(Math.Sqrt(meilleurD2)));
    }

    private static List<TemperaturesSaison> TemperaturesDesSaisons(Carte carte, int[] masse, int continent)
    {
        var resultat = new List<TemperaturesSaison>();
        foreach (var saison in Enum.GetValues<Saison>())
        {
            var midi = (long)saison * Calendrier.JoursParSaison * Calendrier.SecondesParJour
                       + Calendrier.JoursParSaison / 2 * Calendrier.SecondesParJour + 12 * Calendrier.SecondesParHeure;
            var (min, max) = (double.MaxValue, double.MinValue);
            for (var i = 0; i < masse.Length; i++)
            {
                if (masse[i] != continent || continent == 0 || !Climat.EstPraticable((Biome)carte.Biomes[i])) continue;
                var t = Climat.Temperature(carte, i % carte.Largeur, i / carte.Largeur, midi);
                (min, max) = (Math.Min(min, t), Math.Max(max, t));
            }
            if (max >= min) resultat.Add(new TemperaturesSaison(saison, min, max));
        }
        return resultat;
    }

    /// <summary>
    /// Le trajet à pied le moins pénible entre deux cases (algorithme de Dijkstra sur l'effort de chaque pas, voir
    /// <see cref="Marche.Effort"/>), et le temps qu'il faut à un adulte en forme qui marche à son allure ordinaire.
    /// Null si l'arrivée est inaccessible à pied (île, eau).
    /// </summary>
    public static Trajet? CalculerTrajet(Carte carte, int depart, int arrivee)
    {
        int n = carte.Largeur * carte.Hauteur;
        if (depart < 0 || arrivee < 0 || depart >= n || arrivee >= n) return null;
        if (!Climat.EstPraticable((Biome)carte.Biomes[depart]) || !Climat.EstPraticable((Biome)carte.Biomes[arrivee])) return null;
        var effort = new double[n];
        Array.Fill(effort, double.PositiveInfinity);
        var precedent = new int[n];
        var file = new PriorityQueue<int, double>();
        effort[depart] = 0;
        precedent[depart] = -1;
        file.Enqueue(depart, 0);
        while (file.TryDequeue(out var i, out var e))
        {
            if (e > effort[i]) continue;
            if (i == arrivee) break;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = ny * carte.Largeur + nx;
                if (!Climat.EstPraticable((Biome)carte.Biomes[j])) continue;
                var total = e + Marche.Effort(carte, x, y, nx, ny);
                if (total >= effort[j]) continue;
                effort[j] = total;
                precedent[j] = i;
                file.Enqueue(j, total);
            }
        }
        if (double.IsPositiveInfinity(effort[arrivee])) return null;
        var cases = new List<int>();
        var metres = 0.0;
        for (var c = arrivee; c >= 0; c = precedent[c])
        {
            cases.Add(c);
            if (precedent[c] >= 0) metres += Marche.Longueur(c % carte.Largeur - precedent[c] % carte.Largeur, c / carte.Largeur - precedent[c] / carte.Largeur);
        }
        cases.Reverse();
        return new Trajet(cases, metres / 1000, effort[arrivee], effort[arrivee] / Marche.VitesseAdulte / Calendrier.SecondesParHeure);
    }
}
