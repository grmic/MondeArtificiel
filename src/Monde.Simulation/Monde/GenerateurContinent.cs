namespace Monde.Simulation.Monde;

/// <summary>
/// Génère un continent entouré d'océan à partir d'une graine.
/// Tout le calcul est déterministe : même graine, même taille, même carte.
/// </summary>
public static class GenerateurContinent
{
    public const int NombreEmplacements = 33;
    public const int TailleParDefaut = 256;

    private const int DistanceMaxEauDouce = 4;
    private const int ProfondeurMinLac = 16;

    private static readonly (int Dx, int Dy)[] Voisins8 =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    private static readonly (int Dx, int Dy)[] Voisins4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    public static Carte Generer(ulong graine, int largeur = TailleParDefaut, int hauteur = TailleParDefaut)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(largeur, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(hauteur, 64);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(largeur, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hauteur, 1024);

        var n = largeur * hauteur;
        var alea = new Alea(graine);
        var relief = new Bruit(alea.Suivant());
        var humidite = new Bruit(alea.Suivant());
        var abondance = new Bruit(alea.Suivant());

        var altitudes = CalculerAltitudes(relief, largeur, hauteur);
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

        // 4. Les 33 emplacements de départ, tous sur la plus grande terre.
        var emplacements = PlacerEmplacements(alea, biomes, gibier, plantes, largeur, hauteur);

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

    private static byte[] CalculerAltitudes(Bruit relief, int largeur, int hauteur)
    {
        var altitudes = new byte[largeur * hauteur];
        for (var y = 0; y < hauteur; y++)
        {
            for (var x = 0; x < largeur; x++)
            {
                // Distance au centre, normalisée : 0 au centre, 1 au bord.
                var dx = (x + 0.5) / largeur * 2 - 1;
                var dy = (y + 0.5) / hauteur * 2 - 1;
                var d2 = dx * dx + dy * dy;

                var bruit = relief.Fractal(x / 64.0, y / 64.0, 6);
                // Le terrain s'abaisse vers les bords pour former un continent entouré d'océan.
                var h = 0.28 + (bruit - 0.5) * 1.8 - d2 * 0.55 - Math.Max(0, d2 - 0.6) * 1.2;
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
        Alea alea, byte[] biomes, byte[] gibier, byte[] plantes, int largeur, int hauteur)
    {
        var continent = PlusGrandeTerre(biomes, largeur, hauteur);
        var distanceEau = DistanceEauDouce(biomes, largeur, hauteur);

        // Candidats : terrain habitable du continent principal, proche d'eau douce, assez riche.
        var candidats = new List<int>();
        for (var i = 0; i < biomes.Length; i++)
        {
            if (continent[i] && Biomes.EstHabitable((Biome)biomes[i]))
            {
                candidats.Add(i);
            }
        }
        alea.Melanger(candidats);

        var avecEau = candidats.Where(i => distanceEau[i] <= DistanceMaxEauDouce && gibier[i] + plantes[i] >= 60).ToList();

        // Écart minimal entre deux couples, réduit progressivement si le continent est petit.
        var ecartInitial = Math.Max(largeur, hauteur) / 12;
        foreach (var liste in new[] { avecEau, candidats })
        {
            for (var ecart = ecartInitial; ecart >= 2; ecart--)
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
                        if (choisis.Count == NombreEmplacements) break;
                    }
                }
                if (choisis.Count == NombreEmplacements)
                {
                    // Numérotation d'ouest en est puis du nord au sud, pour une lecture stable.
                    return choisis
                        .OrderBy(i => i % largeur).ThenBy(i => i / largeur)
                        .Select((i, rang) => new Emplacement(rang + 1, i % largeur, i / largeur))
                        .ToList();
                }
            }
        }
        throw new InvalidOperationException(
            $"Le continent généré est trop petit pour {NombreEmplacements} emplacements.");
    }

    /// <summary>Marque les tuiles de la plus grande terre (tout ce qui n'est pas mer, reliée en 4-voisinage).</summary>
    private static bool[] PlusGrandeTerre(byte[] biomes, int largeur, int hauteur)
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

        var plusGrande = tailles.Count == 0 ? -1 : tailles.IndexOf(tailles.Max());
        return composante.Select(c => c == plusGrande && c >= 0).ToArray();
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
