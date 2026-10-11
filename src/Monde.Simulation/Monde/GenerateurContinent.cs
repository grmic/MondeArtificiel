using Monde.Simulation.Nature;

namespace Monde.Simulation.Monde;

/// <summary>
/// Génère un ou plusieurs continents entourés d'océan à partir d'une graine et des réglages du monde.
/// Tout le calcul est déterministe : même graine, mêmes réglages, même carte.
/// </summary>
public static class GenerateurContinent
{
    public const int NombreEmplacements = ConfigurationMonde.CouplesParDefaut;
    public const int TailleParDefaut = ConfigurationMonde.TailleParDefaut;

    private const int DistanceMaxEauDouce = 4;
    private const int ProfondeurMinLac = 16;

    private static readonly (int Dx, int Dy)[] Voisins8 =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    private static readonly (int Dx, int Dy)[] Voisins4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    public static Carte Generer(ulong graine, int largeur = TailleParDefaut, int hauteur = TailleParDefaut) =>
        Generer(new ConfigurationMonde(graine, NombreEmplacements, largeur, hauteur, 1));

    /// <exception cref="ArgumentOutOfRangeException">Réglages hors limites.</exception>
    /// <exception cref="InvalidOperationException">Le continent est trop petit pour le nombre de couples demandé.</exception>
    public static Carte Generer(ConfigurationMonde configuration)
    {
        var (graine, couples, largeur, hauteur, continents, placement) = configuration;
        ArgumentOutOfRangeException.ThrowIfLessThan(largeur, ConfigurationMonde.TailleMin);
        ArgumentOutOfRangeException.ThrowIfLessThan(hauteur, ConfigurationMonde.TailleMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(largeur, ConfigurationMonde.TailleMax);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hauteur, ConfigurationMonde.TailleMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(couples, ConfigurationMonde.CouplesMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(couples, ConfigurationMonde.CouplesMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(continents, ConfigurationMonde.ContinentsMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(continents, ConfigurationMonde.ContinentsMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(placement, ConfigurationMonde.PlacementTous);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(placement, continents);

        var n = largeur * hauteur;
        var alea = new Alea(graine);
        var relief = new Bruit(alea.Suivant());
        var humidite = new Bruit(alea.Suivant());
        var abondance = new Bruit(alea.Suivant());

        // Un hasard à part pour placer les continents : le monde d'origine (un seul continent) reste identique.
        var centres = continents == 1 ? null : PlacerContinents(new Alea(graine ^ 0x434F4E54494EUL), continents, largeur, hauteur);
        var altitudes = CalculerAltitudes(relief, largeur, hauteur, centres);
        var biomes = new byte[n];
        var climat = new double[n];

        // 1. Mer, puis terres selon l'altitude, la latitude et l'humidité.
        var ocean = MarquerOcean(altitudes, largeur, hauteur);
        for (var y = 0; y < hauteur; y++)
        {
            // Climat chaud de l'ère des dinosaures : plus chaud au centre, plus tempéré aux bords nord et sud.
            var latitude = Math.Abs((y + 0.5) / hauteur - 0.5) * 2;
            for (var x = 0; x < largeur; x++)
            {
                var i = y * largeur + x;
                var h = Hauteur(altitudes[i]);
                var humide = humidite.Fractal(x / 48.0, y / 48.0, 4);
                var temperature = 1 - latitude * 0.6 - Math.Max(0, h) * 0.8;
                climat[i] = humide;

                if (h < 0)
                {
                    biomes[i] = (byte)(ocean[i]
                        ? (h > -0.06 ? Biome.EauPeuProfonde : Biome.Ocean)
                        : Biome.Lac);
                    continue;
                }
                biomes[i] = (byte)ChoisirBiome(h, temperature, humide);
            }
        }

        // 2. Rivières : de sources en altitude jusqu'à la mer ou un lac.
        TracerRivieres(altitudes, biomes, climat, largeur, hauteur);

        // 3. Ressources selon le biome, avec une variation locale.
        var gibier = new byte[n];
        var plantes = new byte[n];
        for (var y = 0; y < hauteur; y++)
        {
            for (var x = 0; x < largeur; x++)
            {
                var i = y * largeur + x;
                var (g, p) = RessourcesDeBase((Biome)biomes[i]);
                var variation = 0.6 + abondance.Fractal(x / 16.0, y / 16.0, 3) * 0.8;
                gibier[i] = (byte)Math.Clamp((int)(g * variation), 0, 100);
                plantes[i] = (byte)Math.Clamp((int)(p * variation), 0, 100);
            }
        }

        // 4. Les emplacements de départ (un par couple), sur le continent choisi ou répartis sur tous.
        var emplacements = PlacerEmplacements(alea, biomes, gibier, plantes, largeur, hauteur, couples, continents, placement);

        return new Carte
        {
            Graine = graine,
            Largeur = largeur,
            Hauteur = hauteur,
            Biomes = biomes,
            Altitudes = altitudes,
            Gibier = gibier,
            Plantes = plantes,
            Emplacements = emplacements,
        };
    }

    /// <summary>Altitude relative au niveau de la mer, de -1 à environ 1.</summary>
    private static double Hauteur(byte altitude) => (altitude - Carte.NiveauMer) / 127.0;

    /// <summary>
    /// Centre et demi-taille (en cases) de chaque continent : la carte est découpée en une grille de cellules à peu près
    /// carrées, au moins une par continent ; chaque continent occupe une cellule tirée au hasard, son centre légèrement décalé.
    /// </summary>
    private static (double X, double Y, double Rx, double Ry)[] PlacerContinents(Alea alea, int nombre, int largeur, int hauteur)
    {
        var (colonnes, lignes) = (nombre, 1);
        var meilleur = double.MaxValue;
        for (var c = 1; c <= nombre + 1; c++)
        {
            for (var r = 1; r <= nombre + 1; r++)
            {
                if (c * r < nombre || c * r > nombre + 1) continue;
                // Cellules les plus carrées possible, puis le moins de cellules vides.
                var ecart = Math.Abs(Math.Log(largeur / (double)c / (hauteur / (double)r))) + (c * r - nombre) * 0.3;
                if (ecart < meilleur) (meilleur, colonnes, lignes) = (ecart, c, r);
            }
        }
        var cellules = Enumerable.Range(0, colonnes * lignes).ToList();
        alea.Melanger(cellules);
        double cw = largeur / (double)colonnes, ch = hauteur / (double)lignes;
        return [.. cellules.Take(nombre).Order().Select(k =>
        {
            var x = (k % colonnes + 0.5 + (alea.Reel() - 0.5) * 0.2) * cw;
            var y = (k / colonnes + 0.5 + (alea.Reel() - 0.5) * 0.2) * ch;
            return (x, y, cw / 2, ch / 2);
        })];
    }

    private static byte[] CalculerAltitudes(Bruit relief, int largeur, int hauteur, (double X, double Y, double Rx, double Ry)[]? centres)
    {
        var altitudes = new byte[largeur * hauteur];
        for (var y = 0; y < hauteur; y++)
        {
            for (var x = 0; x < largeur; x++)
            {
                double d2, detroit = 0, relief1 = 1;
                if (centres is null)
                {
                    // Distance au centre, normalisée : 0 au centre, 1 au bord.
                    var dx = (x + 0.5) / largeur * 2 - 1;
                    var dy = (y + 0.5) / hauteur * 2 - 1;
                    d2 = dx * dx + dy * dy;
                }
                else
                {
                    // Distance normalisée au centre du continent le plus proche, et au deuxième : entre deux continents,
                    // le terrain plonge pour qu'un bras de mer les sépare.
                    double p1 = double.MaxValue, p2 = double.MaxValue;
                    foreach (var (cx, cy, rx, ry) in centres)
                    {
                        var dx = (x + 0.5 - cx) / rx;
                        var dy = (y + 0.5 - cy) / ry;
                        var d = dx * dx + dy * dy;
                        if (d < p1) (p1, p2) = (d, p1);
                        else if (d < p2) p2 = d;
                    }
                    d2 = p1;
                    // Relief moins heurté au cœur : un continent plus petit ne se brise pas en archipel.
                    relief1 = Math.Min(1, 0.45 + p1);
                    var marge = Math.Sqrt(p2) - Math.Sqrt(p1);
                    if (marge < 0.35) detroit += (0.35 - marge) * 2.5;
                    // Les bords de la carte restent en mer.
                    var bx = Math.Abs((x + 0.5) / largeur * 2 - 1);
                    var by = Math.Abs((y + 0.5) / hauteur * 2 - 1);
                    var bord = Math.Max(bx, by);
                    if (bord > 0.9) detroit += (bord - 0.9) * 8;
                }

                var bruit = relief.Fractal(x / 64.0, y / 64.0, 6);
                // Le terrain s'abaisse vers les bords pour former un continent entouré d'océan.
                var h = 0.28 + (bruit - 0.5) * 1.8 * relief1 - d2 * 0.55 - Math.Max(0, d2 - 0.6) * 1.2 - detroit;
                // Les terres basses sont étendues, les reliefs plus rares (courbe quadratique).
                if (h > 0) h = Math.Min(1, h * h * 1.6);
                var valeur = (int)(Carte.NiveauMer + h * 127);
                altitudes[y * largeur + x] = (byte)Math.Clamp(valeur, 0, 255);
            }
        }
        return altitudes;
    }

    /// <summary>L'océan est l'eau reliée au bord de la carte ; le reste de l'eau forme des lacs.</summary>
    private static bool[] MarquerOcean(byte[] altitudes, int largeur, int hauteur)
    {
        var ocean = new bool[largeur * hauteur];
        var file = new Queue<int>();
        for (var x = 0; x < largeur; x++)
        {
            Ajouter(x, 0);
            Ajouter(x, hauteur - 1);
        }
        for (var y = 0; y < hauteur; y++)
        {
            Ajouter(0, y);
            Ajouter(largeur - 1, y);
        }
        while (file.Count > 0)
        {
            var i = file.Dequeue();
            int x = i % largeur, y = i / largeur;
            foreach (var (dx, dy) in Voisins4)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < largeur && ny < hauteur)
                {
                    Ajouter(nx, ny);
                }
            }
        }
        return ocean;

        void Ajouter(int x, int y)
        {
            var i = y * largeur + x;
            if (!ocean[i] && altitudes[i] < Carte.NiveauMer)
            {
                ocean[i] = true;
                file.Enqueue(i);
            }
        }
    }

    private static Biome ChoisirBiome(double h, double temperature, double humide)
    {
        if (h > 0.6) return temperature < 0.4 ? Biome.SommetEnneige : Biome.Montagne;
        if (h > 0.28) return Biome.Montagne;
        if (h < 0.006) return humide > 0.62 ? Biome.Marais : Biome.Plage;
        if (h < 0.04 && humide > 0.62) return Biome.Marais;
        if (humide < 0.36) return Biome.Desert;
        if (temperature > 0.72) return humide > 0.52 ? Biome.ForetTropicale : Biome.PlaineDeFougeres;
        return humide > 0.5 ? Biome.ForetDeConiferes : Biome.PlaineDeFougeres;
    }

    /// <summary>
    /// Écoulement de l'eau par « remplissage prioritaire » : on part de la mer et on remonte les terres
    /// de la plus basse à la plus haute. Chaque tuile s'écoule vers celle qui l'a atteinte, ce qui
    /// garantit un chemin jusqu'à la mer. Les cuvettes remplies deviennent des lacs, et les tuiles qui
    /// reçoivent beaucoup d'eau deviennent des rivières.
    /// </summary>
    private static void TracerRivieres(byte[] altitudes, byte[] biomes, double[] climat, int largeur, int hauteur)
    {
        var n = biomes.Length;
        var niveau = new int[n];   // altitude après remplissage, en huitièmes d'unité
        var aval = new int[n];
        var ferme = new bool[n];
        var ordre = new List<int>(n);
        var file = new PriorityQueue<int, long>();
        Array.Fill(aval, -1);

        for (var i = 0; i < n; i++)
        {
            if (Biomes.EstEauSalee((Biome)biomes[i]))
            {
                niveau[i] = altitudes[i] * 8;
                ferme[i] = true;
                // La priorité inclut l'indice : l'ordre de sortie ne dépend jamais des égalités.
                file.Enqueue(i, niveau[i] * (long)n + i);
            }
        }

        while (file.TryDequeue(out var c, out _))
        {
            ordre.Add(c);
            int x = c % largeur, y = c / largeur;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= largeur || ny >= hauteur) continue;
                var j = ny * largeur + nx;
                if (ferme[j]) continue;
                ferme[j] = true;
                niveau[j] = Math.Max(altitudes[j] * 8, niveau[c] + 1);
                aval[j] = c;
                file.Enqueue(j, niveau[j] * (long)n + j);
            }
        }

        // Débit : chaque tuile reçoit la pluie (selon son humidité) et la transmet vers l'aval.
        var debit = new double[n];
        for (var i = 0; i < n; i++)
        {
            debit[i] = Biomes.EstEauSalee((Biome)biomes[i]) ? 0 : 0.5 + climat[i];
        }
        for (var k = ordre.Count - 1; k >= 0; k--)
        {
            var c = ordre[k];
            if (aval[c] >= 0 && !Biomes.EstEauSalee((Biome)biomes[c]))
            {
                debit[aval[c]] += debit[c];
            }
        }

        var seuilRiviere = n / 400.0;
        for (var i = 0; i < n; i++)
        {
            var biome = (Biome)biomes[i];
            if (Biomes.EstEauSalee(biome) || biome == Biome.Lac) continue;
            var profondeur = niveau[i] - altitudes[i] * 8;
            if (profondeur >= ProfondeurMinLac)
            {
                biomes[i] = (byte)Biome.Lac;
            }
            else if (debit[i] >= seuilRiviere)
            {
                biomes[i] = (byte)Biome.Riviere;
            }
        }
    }

    private static (int Gibier, int Plantes) RessourcesDeBase(Biome biome) => biome switch
    {
        Biome.PlaineDeFougeres => (60, 55),
        Biome.ForetDeConiferes => (55, 40),
        Biome.ForetTropicale => (70, 80),
        Biome.Marais => (45, 50),
        Biome.Plage => (15, 15),
        Biome.Desert => (8, 5),
        Biome.Montagne => (20, 10),
        Biome.Riviere or Biome.Lac => (40, 20),
        Biome.EauPeuProfonde => (30, 10),
        _ => (0, 0),
    };

    private static List<Emplacement> PlacerEmplacements(
        Alea alea, byte[] biomes, byte[] gibier, byte[] plantes, int largeur, int hauteur, int couples, int continents, int placement)
    {
        var (composante, rangs) = Terres(biomes, largeur, hauteur);
        var distanceEau = DistanceEauDouce(biomes, largeur, hauteur);
        // Les terres qui reçoivent des couples : le continent choisi (1 : le plus grand), ou tous les continents.
        var visees = placement == ConfigurationMonde.PlacementTous ? rangs.Count : 1;
        if (placement > rangs.Count)
        {
            throw new InvalidOperationException(
                $"Cette carte n'a que {rangs.Count} continent{(rangs.Count > 1 ? "s" : "")} : impossible d'y placer les couples sur le continent n° {placement}.");
        }
        var rangDe = new Dictionary<int, int>();
        for (var r = 0; r < visees; r++) rangDe[rangs[placement == ConfigurationMonde.PlacementTous ? r : placement - 1]] = r;

        // Candidats : terrain habitable des terres visées, proche d'eau douce, assez riche.
        var candidats = new List<int>();
        for (var i = 0; i < biomes.Length; i++)
        {
            if (composante[i] >= 0 && rangDe.ContainsKey(composante[i]) && Biomes.EstHabitable((Biome)biomes[i]))
            {
                candidats.Add(i);
            }
        }
        alea.Melanger(candidats);

        // Écart minimal entre deux couples, réduit progressivement si le continent est petit : 1/12 de la carte pour
        // 33 couples sur un continent, plus serré s'il y a plus de couples ou si le continent est plus petit.
        if (visees == 1)
        {
            var ecartInitial = (int)(Math.Max(largeur, hauteur) / 12.0 * Math.Sqrt((double)NombreEmplacements / couples / continents));
            return Numeroter(Choisir(candidats, distanceEau, gibier, plantes, largeur, couples, ecartInitial)
                ?? throw TropPetit(couples), largeur);
        }

        // Sur tous les continents : des couples sur chacun, en proportion de ses terres habitables (au moins un par continent
        // tant qu'il y a assez de couples), chaque groupe espacé sur son continent.
        var parTerre = Enumerable.Range(0, visees).Select(r => candidats.Where(i => rangDe[composante[i]] == r).ToList()).ToList();
        var parts = Repartir(couples, parTerre.Select(l => l.Count).ToList());
        var tous = new List<int>();
        for (var r = 0; r < visees; r++)
        {
            if (parts[r] == 0) continue;
            var ecart = (int)(Math.Max(largeur, hauteur) / 12.0 * Math.Sqrt((double)NombreEmplacements / parts[r] / continents));
            tous.AddRange(Choisir(parTerre[r], distanceEau, gibier, plantes, largeur, parts[r], ecart) ?? throw TropPetit(couples));
        }
        return Numeroter(tous, largeur);
    }

    private static InvalidOperationException TropPetit(int couples) => new(
        $"Le continent généré est trop petit pour {couples} couples : agrandissez la carte, réduisez le nombre de continents ou de couples.");

    /// <summary>Numérotation d'ouest en est puis du nord au sud, pour une lecture stable.</summary>
    private static List<Emplacement> Numeroter(List<int> choisis, int largeur) => choisis
        .OrderBy(i => i % largeur).ThenBy(i => i / largeur)
        .Select((i, rang) => new Emplacement(rang + 1, i % largeur, i / largeur))
        .ToList();

    /// <summary>
    /// Choisit <paramref name="nombre"/> cases parmi les candidats (dans leur ordre), d'abord près de l'eau douce et riches,
    /// en desserrant l'écart minimal jusqu'à ce qu'elles tiennent ; null si c'est impossible.
    /// </summary>
    private static List<int>? Choisir(List<int> candidats, int[] distanceEau, byte[] gibier, byte[] plantes, int largeur, int nombre, int ecartInitial)
    {
        var avecEau = candidats.Where(i => distanceEau[i] <= DistanceMaxEauDouce && gibier[i] + plantes[i] >= 60).ToList();
        foreach (var liste in new[] { avecEau, candidats })
        {
            for (var ecart = Math.Max(2, ecartInitial); ecart >= 2; ecart--)
            {
                var choisis = new List<int>();
                foreach (var i in liste)
                {
                    int x = i % largeur, y = i / largeur;
                    var assezLoin = choisis.All(c =>
                    {
                        int cx = c % largeur, cy = c / largeur;
                        return (cx - x) * (cx - x) + (cy - y) * (cy - y) >= ecart * ecart;
                    });
                    if (assezLoin)
                    {
                        choisis.Add(i);
                        if (choisis.Count == nombre) return choisis;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Partage des couples entre continents, en proportion de leurs cases habitables (plus forts restes), au moins un
    /// chacun quand il y a assez de couples ; sinon les plus grands d'abord.
    /// </summary>
    internal static int[] Repartir(int couples, IReadOnlyList<int> habitables)
    {
        var parts = new int[habitables.Count];
        var avecPlace = Enumerable.Range(0, habitables.Count).Where(r => habitables[r] > 0).ToList();
        if (avecPlace.Count == 0) return parts;
        if (couples <= avecPlace.Count)
        {
            foreach (var r in avecPlace.Take(couples)) parts[r] = 1;
            return parts;
        }
        foreach (var r in avecPlace) parts[r] = 1;
        var reste = couples - avecPlace.Count;
        double total = avecPlace.Sum(r => (double)habitables[r]);
        var exacts = avecPlace.ToDictionary(r => r, r => reste * habitables[r] / total);
        foreach (var r in avecPlace) parts[r] += (int)exacts[r];
        var manquants = couples - parts.Sum();
        foreach (var r in avecPlace.OrderByDescending(r => exacts[r] - Math.Floor(exacts[r])).ThenBy(r => r).Take(manquants)) parts[r]++;
        return parts;
    }

    /// <summary>
    /// Masses de terre (tout ce qui n'est pas mer, reliée en 4-voisinage) : le numéro de masse de chaque tuile (−1 en mer) et
    /// les continents, du plus grand au plus petit (une masse d'au moins le quart de la plus grande et de 200 km², comme
    /// dans <see cref="Geographie"/>).
    /// </summary>
    private static (int[] Composante, List<int> Continents) Terres(byte[] biomes, int largeur, int hauteur)
    {
        var composante = new int[biomes.Length];
        Array.Fill(composante, -1);
        var tailles = new List<int>();
        var file = new Queue<int>();

        for (var depart = 0; depart < biomes.Length; depart++)
        {
            if (composante[depart] >= 0 || Biomes.EstEauSalee((Biome)biomes[depart])) continue;
            var id = tailles.Count;
            var taille = 0;
            composante[depart] = id;
            file.Enqueue(depart);
            while (file.Count > 0)
            {
                var i = file.Dequeue();
                taille++;
                int x = i % largeur, y = i / largeur;
                foreach (var (dx, dy) in Voisins4)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= largeur || ny >= hauteur) continue;
                    var j = ny * largeur + nx;
                    if (composante[j] < 0 && !Biomes.EstEauSalee((Biome)biomes[j]))
                    {
                        composante[j] = id;
                        file.Enqueue(j);
                    }
                }
            }
            tailles.Add(taille);
        }

        if (tailles.Count == 0) return (composante, []);
        var seuil = Math.Max(200 / Echelle.KilometresCarres(1), tailles.Max() * 0.25);
        // À taille égale, la première trouvée d'abord : le « plus grand continent » reste celui d'avant.
        var plusGrande = tailles.IndexOf(tailles.Max());
        var continents = Enumerable.Range(0, tailles.Count).Where(k => k == plusGrande || tailles[k] >= seuil)
            .OrderByDescending(k => tailles[k]).ThenBy(k => k).ToList();
        return (composante, continents);
    }

    /// <summary>Distance (en tuiles, 4-voisinage) à l'eau douce la plus proche.</summary>
    private static int[] DistanceEauDouce(byte[] biomes, int largeur, int hauteur)
    {
        var distance = new int[biomes.Length];
        Array.Fill(distance, int.MaxValue);
        var file = new Queue<int>();
        for (var i = 0; i < biomes.Length; i++)
        {
            if (Biomes.EstEauDouce((Biome)biomes[i]))
            {
                distance[i] = 0;
                file.Enqueue(i);
            }
        }
        while (file.Count > 0)
        {
            var i = file.Dequeue();
            int x = i % largeur, y = i / largeur;
            foreach (var (dx, dy) in Voisins4)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= largeur || ny >= hauteur) continue;
                var j = ny * largeur + nx;
                if (distance[j] == int.MaxValue)
                {
                    distance[j] = distance[i] + 1;
                    file.Enqueue(j);
                }
            }
        }
        return distance;
    }
}
