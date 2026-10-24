using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>Roche qui affleure sous le sol d'une case.</summary>
public enum Roche : byte
{
    /// <summary>Granites et gneiss du vieux socle : roche dure, sols acides, galets durs dans les rivières.</summary>
    Granite = 0,
    /// <summary>Coulées de basalte (rifts, points chauds) : sols riches, cuivre natif par endroits.</summary>
    Basalte = 1,
    /// <summary>Andésites, rhyolites et tufs des volcans récents : obsidienne, soufre.</summary>
    Volcanique = 2,
    /// <summary>Calcaire : falaises, grottes et sources (karst).</summary>
    Calcaire = 3,
    /// <summary>Craie : roche tendre déposée dans les mers chaudes du Crétacé, avec ses rognons de silex et de marcassite.</summary>
    Craie = 4,
    /// <summary>Grès : parois et abris sous roche, sols sableux, terres rouges (ocre).</summary>
    Gres = 5,
    /// <summary>Schistes et marnes : argiles, pyrite des schistes noirs, charbon.</summary>
    Schiste = 6,
    /// <summary>Alluvions récentes des vallées, des deltas, des marais et des plages.</summary>
    Alluvions = 7,
    /// <summary>Évaporites des cuvettes sèches : sel, gypse.</summary>
    Evaporites = 8,
}

/// <summary>Grand type de sol, selon la roche, le climat, la pente et l'eau.</summary>
public enum Sol : byte
{
    Aucun = 0,
    /// <summary>Limons des vallées, renouvelés par les crues : les plus riches.</summary>
    Alluvial = 1,
    /// <summary>Sol de cendres et de basaltes altérés : très fertile.</summary>
    Volcanique = 2,
    /// <summary>Sol brun profond des schistes et des marnes.</summary>
    Brun = 3,
    /// <summary>Sol brun acide des granites et des forêts de conifères.</summary>
    Acide = 4,
    /// <summary>Sol mince sur calcaire ou craie (rendzine).</summary>
    Calcaire = 5,
    /// <summary>Sol sableux des grès : il ne garde pas l'eau.</summary>
    Sableux = 6,
    /// <summary>Latérite des forêts chaudes : rouge et pauvre sous une végétation luxuriante.</summary>
    Lateritique = 7,
    /// <summary>Tourbe et vase des marais : acide, gorgée d'eau.</summary>
    Tourbeux = 8,
    /// <summary>Roche presque nue des pentes fortes.</summary>
    Lithosol = 9,
    /// <summary>Sol des déserts.</summary>
    Desertique = 10,
    /// <summary>Croûte salée des cuvettes sèches.</summary>
    Salin = 11,
}

/// <summary>Ce qu'un gisement contient.</summary>
public enum Gite : byte
{
    Sel = 0,
    Soufre = 1,
    CuivreNatif = 2,
    /// <summary>Malachite : la roche verte des affleurements de cuivre.</summary>
    Malachite = 3,
    /// <summary>Cassitérite : minerai d'étain, en graviers noirs et lourds.</summary>
    Cassiterite = 4,
    /// <summary>Fer des marais (limonite).</summary>
    FerDesMarais = 5,
    /// <summary>Or des graviers de rivière.</summary>
    Or = 6,
    Charbon = 7,
    Bitume = 8,
    /// <summary>Argile blanche et fine (kaolin) des granites altérés.</summary>
    Kaolin = 9,
}

/// <summary>Profondeur d'un gisement : à l'affleurement, on peut le voir ; juste dessous, une crue ou un terrier le découvre.</summary>
public enum Enfouissement : byte
{
    Affleurant = 0,
    SousLaSurface = 1,
    Profond = 2,
}

public sealed record Gisement(int Numero, Gite Type, int X, int Y, Enfouissement Profondeur);

/// <summary>Une grotte (dans le calcaire, ou un tunnel de lave près d'un volcan) ou un abri sous roche au pied d'une paroi.</summary>
/// <param name="Metres">Profondeur jusqu'où l'on peut entrer.</param>
/// <param name="Source">Une source en sort.</param>
public sealed record Grotte(int Numero, int X, int Y, bool SousRoche, int Metres, bool Source);

public enum TypeSource : byte
{
    Douce = 0,
    /// <summary>Source chaude, près d'un volcan.</summary>
    Chaude = 1,
    /// <summary>Source salée : on ne peut pas en boire.</summary>
    Salee = 2,
}

public sealed record Source(int Numero, int X, int Y, TypeSource Type);

/// <summary>
/// Le terrain sous la carte : roches, sols, relief, rivières qui coulent, grottes, sources et gisements cachés.
/// <para>
/// Tout est tiré une fois pour toutes de la graine et de la carte (la carte ne change pas) : un monde ancien reçoit
/// le même terrain qu'un monde neuf. Les roches suivent le contexte tectonique (<see cref="Geologie"/>) ; les sols
/// découlent des roches, du climat, de la pente et de l'eau ; les gisements, des roches.
/// </para>
/// </summary>
public sealed class Terrain
{
    /// <summary>
    /// Écoulement moyen d'une case : environ 35 cm de pluie par an partent vers les rivières sous un climat chaud et
    /// humide (le reste s'évapore ou nourrit les plantes) ; une case fait 0,64 km². Débit en m³/s par « pluie de case ».
    /// </summary>
    private const double DebitParCase = 0.35 * Echelle.MetresParCase * Echelle.MetresParCase / (365.25 * 86_400);

    /// <summary>Débit d'une rivière selon la saison (printemps, été, automne, hiver), relatif à son débit moyen : hautes eaux au printemps.</summary>
    private static readonly double[] DebitSelonSaison = [1.5, 0.8, 0.9, 0.8];

    private static readonly (int Dx, int Dy)[] Voisins8 = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    private readonly int _largeur;

    private Terrain(int largeur) => _largeur = largeur;

    public required Roche[] Roches { get; init; }
    public required Sol[] Sols { get; init; }

    /// <summary>Fertilité du sol, de 0 (roche nue, sel) à 1 (limon des vallées).</summary>
    public required float[] Fertilite { get; init; }

    /// <summary>
    /// Ce que le sol change aux plantes de la case, comparé aux autres cases du même terrain (1 : comme la moyenne) :
    /// le terrain dit déjà le climat, le sol dit ce qui varie d'un lieu à l'autre.
    /// </summary>
    public required float[] FacteurSol { get; init; }

    /// <summary>Plus fort dénivelé vers une case voisine, en unités d'altitude (40 m).</summary>
    public required byte[] Pente { get; init; }

    /// <summary>La case porte une falaise ou un escarpement de roche dure.</summary>
    public required bool[] Falaise { get; init; }

    /// <summary>Case vers laquelle l'eau s'écoule (−1 : la mer).</summary>
    public required int[] Aval { get; init; }

    /// <summary>Débit moyen de la rivière de la case, en m³/s (0 hors des rivières).</summary>
    public required float[] Debit { get; init; }

    /// <summary>
    /// Danger du courant à débit moyen, en m²/s (profondeur × vitesse) : on traverse à gué en dessous de 0,5 à 1 selon
    /// sa taille et sa hardiesse. Un fond plus haut (seuil, banc de graviers) fait un gué ; une fosse, un passage impossible.
    /// </summary>
    public required float[] Courant { get; init; }

    /// <summary>
    /// Pierres qu'on trouve sur chaque case, selon la roche : galets durs (bit 1), silex (bit 2), pyrite (bit 4),
    /// roche à obsidienne (bit 8, à condition d'être près d'un volcan).
    /// </summary>
    public required byte[] Pierres { get; init; }

    public required IReadOnlyList<Gisement> Gisements { get; init; }
    public required IReadOnlyList<Grotte> Grottes { get; init; }
    public required IReadOnlyList<Source> Sources { get; init; }

    /// <summary>Numéro du gisement de chaque case (0 : aucun).</summary>
    public required short[] GisementEn { get; init; }

    /// <summary>Numéro de la grotte ou de l'abri de chaque case (0 : aucun).</summary>
    public required short[] GrotteEn { get; init; }

    /// <summary>Numéro de la source de chaque case (0 : aucune).</summary>
    public required short[] SourceEn { get; init; }

    public Gisement? GisementA(int x, int y) => GisementEn[y * _largeur + x] is > 0 and var n ? Gisements[n - 1] : null;

    public Grotte? GrotteA(int x, int y) => GrotteEn[y * _largeur + x] is > 0 and var n ? Grottes[n - 1] : null;

    public Source? SourceA(int x, int y) => SourceEn[y * _largeur + x] is > 0 and var n ? Sources[n - 1] : null;

    /// <summary>Débit de la saison relatif au débit moyen, interpolé d'une saison à l'autre.</summary>
    public static double FacteurSaison(long secondes)
    {
        var jour = secondes % Calendrier.SecondesParAn / (double)Calendrier.SecondesParJour;
        var position = (jour - Calendrier.JoursParSaison / 2.0) / Calendrier.JoursParSaison;
        if (position < 0) position += 4;
        var s = (int)position % 4;
        var t = position - Math.Floor(position);
        return DebitSelonSaison[s] + (DebitSelonSaison[(s + 1) % 4] - DebitSelonSaison[s]) * t;
    }

    /// <summary>
    /// Danger du courant d'une case aujourd'hui (m²/s) : le débit suit la saison et les pluies de l'année (<paramref name="crue"/>
    /// = facteur de saison × pluies), et profondeur × vitesse croît comme la racine du débit (géométrie hydraulique des
    /// rivières : profondeur ∝ Q^0,4, vitesse ∝ Q^0,1 ; Leopold et Maddock, 1953).
    /// </summary>
    public double CourantDuJour(int i, double crue) => Courant[i] == 0 ? 0 : Courant[i] * Math.Sqrt(crue);

    /// <summary>Le terrain d'une carte, tiré de sa graine.</summary>
    public static Terrain Calculer(Carte carte)
    {
        int l = carte.Largeur, h = carte.Hauteur, n = l * h;
        var geologie = Geologie.Generer(carte);
        var alea = new Alea(carte.Graine ^ 0x5445525241494EUL);
        var province = Rangs(carte, new Bruit(alea.Suivant()), 40, 4);
        var sediments = Rangs(carte, new Bruit(alea.Suivant()), 20, 3);
        var detail = Rangs(carte, new Bruit(alea.Suivant()), 8, 2);
        var graineCase = alea.Suivant();
        double Hasard(int i, ulong sel)
        {
            var etat = graineCase ^ sel ^ ((ulong)i * 0x9E3779B97F4A7C15UL);
            return (Alea.SplitMix64(ref etat) >> 11) * (1.0 / (1UL << 53));
        }

        // Relief : plus fort dénivelé vers une voisine.
        var pente = new byte[n];
        for (var i = 0; i < n; i++)
        {
            int x = i % l, y = i / l, max = 0;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= l || ny >= h) continue;
                max = Math.Max(max, Math.Abs(carte.Altitudes[ny * l + nx] - carte.Altitudes[i]));
            }
            pente[i] = (byte)Math.Min(255, max);
        }

        // Eau : la même pente d'écoulement que celle qui a tracé les rivières.
        var humidite = GenerateurContinent.Humidite(carte.Graine, l, h);
        var (_, aval, pluie) = GenerateurContinent.Ecouler(carte.Altitudes, carte.Biomes, humidite, l, h);
        var debit = new float[n];
        var courant = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (carte.Biomes[i] != (byte)Biome.Riviere) continue;
            var q = pluie[i] * DebitParCase;
            debit[i] = (float)q;
            // Profondeur ≈ 0,27 Q^0,4 m, vitesse ≈ 0,82 Q^0,1 m/s : leur produit ≈ 0,22 √Q. Le fond varie d'un tronçon à l'autre
            // (seuils et mouilles) : de 0,4 à 1,6 fois la moyenne.
            courant[i] = (float)(0.22 * Math.Sqrt(q) * (0.4 + 1.2 * Hasard(i, 0x47554531UL)));
        }

        // Roches.
        var roches = new Roche[n];
        var volcans = geologie.Volcans;
        for (var i = 0; i < n; i++)
        {
            int x = i % l, y = i / l;
            var b = (Biome)carte.Biomes[i];
            var alt = (carte.Altitudes[i] - Carte.NiveauMer) / 127.0;
            var dVolcan = volcans.Count == 0 ? double.MaxValue : volcans.Min(v => Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y)));
            var p = province[i];
            var s = sediments[i];
            var socle = geologie.Contexte switch { ContexteTectonique.Stable => 0.3, ContexteTectonique.Rift => 0.22, _ => 0.18 };
            roches[i] = true switch
            {
                _ when Biomes.EstEauSalee(b) => Roche.Alluvions,
                _ when dVolcan <= 6 + 10 * detail[i] => geologie.Contexte == ContexteTectonique.Rift ? Roche.Basalte : Roche.Volcanique,
                _ when geologie.Contexte == ContexteTectonique.Rift && alt > 0.02
                       && Geologie.DistanceAuxFailles(geologie.Failles, x, y) <= 10 + 14 * detail[i] => Roche.Basalte,
                _ when b is Biome.Marais or Biome.Plage => Roche.Alluvions,
                _ when b == Biome.Riviere || alt < 0.12 && pente[i] <= 1 && Vallee(carte, debit, x, y) => Roche.Alluvions,
                _ when b == Biome.Desert && Cuvette(carte, x, y) => Roche.Evaporites,
                _ when alt > 0.28 => geologie.Contexte == ContexteTectonique.MargeActive && s < 0.35 ? Roche.Volcanique
                    : p < 0.65 ? Roche.Granite : s < 0.5 ? Roche.Calcaire : Roche.Gres,
                _ when p < socle => Roche.Granite,
                _ when s < 0.18 => alt < 0.12 ? Roche.Craie : Roche.Calcaire,
                _ when s < 0.42 => Roche.Calcaire,
                _ when s < 0.67 => Roche.Gres,
                _ => Roche.Schiste,
            };
        }

        // Falaises : un fort dénivelé dans une roche dure, ou une côte haute.
        var falaise = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (!Climat.EstPraticable((Biome)carte.Biomes[i]) || roches[i] is Roche.Alluvions or Roche.Evaporites or Roche.Schiste) continue;
            // Un dénivelé de 200 m sur 800 (25 %) ne se fait pas sans parois dans une roche dure ; sur la côte, 80 m de haut suffisent.
            falaise[i] = pente[i] >= 5 || pente[i] >= 2 && carte.Altitudes[i] >= Carte.NiveauMer + 2 && BordDeMer(carte, i % l, i / l);
        }

        // Pierres : le silex vient de la craie et d'une partie des calcaires (en rognons), et les rivières et les plages
        // en roulent les galets jusqu'à quelques kilomètres en aval ; la marcassite (pyrite) l'accompagne souvent dans la
        // craie, et se trouve aussi dans les schistes noirs. Zones de 4 × 4 cases (3,2 km), comme avant la 0.31.
        var silexEnPlace = new bool[n];
        var pierres = new byte[n];
        for (var i = 0; i < n; i++)
        {
            var z = Zone(carte, i % l, i / l);
            silexEnPlace[i] = roches[i] == Roche.Craie || roches[i] == Roche.Calcaire && z.U2 < 0.5;
        }
        var silexRoule = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (!silexEnPlace[i]) continue;
            // Le long de la pente d'écoulement, jusqu'à 8 cases (6 km) en aval.
            for (int j = aval[i], k = 0; j >= 0 && k < 8; j = aval[j], k++) silexRoule[j] = true;
        }
        for (var i = 0; i < n; i++)
        {
            var b = (Biome)carte.Biomes[i];
            int x = i % l, y = i / l;
            var z = Zone(carte, x, y);
            var r = 0;
            if (roches[i] is Roche.Granite or Roche.Basalte or Roche.Volcanique or Roche.Gres || z.U1 < 0.15) r |= 1;
            var silex = silexEnPlace[i] || silexRoule[i] && b is Biome.Riviere or Biome.Plage or Biome.Marais;
            if (silex) r |= 2;
            if (silex ? z.U3 < 0.35 : roches[i] == Roche.Schiste ? z.U3 < 0.15 : z.U3 < 0.02) r |= 4;
            if (roches[i] == Roche.Volcanique || roches[i] == Roche.Basalte && volcans.Any(v => Math.Abs(v.X - x) <= 16 && Math.Abs(v.Y - y) <= 16)) r |= 8;
            pierres[i] = (byte)r;
        }

        // Sols.
        var sols = new Sol[n];
        var fertilite = new float[n];
        for (var i = 0; i < n; i++)
        {
            var b = (Biome)carte.Biomes[i];
            if (!Climat.EstPraticable(b) && b != Biome.Lac) continue;
            var (sol, f) = true switch
            {
                // Neige et glace : pas de sol (ni plantes ni gibier n'y vivent).
                _ when b == Biome.SommetEnneige => (Sol.Aucun, 0),
                _ when b is Biome.Montagne or Biome.SommetEnneige && pente[i] >= 3 => (Sol.Lithosol, 0.15),
                _ when roches[i] == Roche.Evaporites => (Sol.Salin, 0.06),
                _ when b == Biome.Desert => (Sol.Desertique, 0.12),
                _ when b == Biome.Marais => (Sol.Tourbeux, 0.35),
                _ when roches[i] == Roche.Alluvions => (Sol.Alluvial, 0.9),
                _ when roches[i] is Roche.Basalte or Roche.Volcanique => (Sol.Volcanique, 0.85),
                _ when b == Biome.ForetTropicale => (Sol.Lateritique, 0.3),
                _ when roches[i] is Roche.Calcaire or Roche.Craie => (Sol.Calcaire, 0.55),
                _ when roches[i] == Roche.Gres => (Sol.Sableux, 0.35),
                _ when roches[i] == Roche.Schiste => (Sol.Brun, 0.7),
                _ => (Sol.Acide, 0.45),
            };
            // Sur les pentes, le sol est plus mince ; d'un champ à l'autre, il varie.
            if (b is Biome.Montagne or Biome.SommetEnneige) f *= 0.6;
            sols[i] = sol;
            fertilite[i] = (float)Math.Clamp(f * (0.85 + 0.3 * Hasard(i, 0x534F4CUL)), 0, 1);
        }
        var facteur = new float[n];
        var somme = new double[256];
        var nombre = new int[256];
        for (var i = 0; i < n; i++)
        {
            if (sols[i] == Sol.Aucun) continue;
            somme[carte.Biomes[i]] += fertilite[i];
            nombre[carte.Biomes[i]]++;
        }
        for (var i = 0; i < n; i++)
        {
            var b = carte.Biomes[i];
            facteur[i] = sols[i] == Sol.Aucun || somme[b] <= 0 ? 1 : (float)Math.Clamp(fertilite[i] / (somme[b] / nombre[b]), 0.5, 1.6);
        }
        // Les bornes déplacent un peu la moyenne : on la ramène à 1 sur chaque terrain, pour que le sol répartisse
        // l'abondance sans la changer en tout.
        Array.Clear(somme);
        for (var i = 0; i < n; i++) if (sols[i] != Sol.Aucun) somme[carte.Biomes[i]] += facteur[i];
        for (var i = 0; i < n; i++)
        {
            var b = carte.Biomes[i];
            if (sols[i] != Sol.Aucun && somme[b] > 0) facteur[i] = (float)(facteur[i] / (somme[b] / nombre[b]));
        }

        // Grottes et abris sous roche, puis sources.
        var grottes = new List<Grotte>();
        var grotteEn = new short[n];
        var sources = new List<Source>();
        var sourceEn = new short[n];
        void AjouterSource(int i, TypeSource type)
        {
            if (sourceEn[i] != 0 || sources.Count >= short.MaxValue) return;
            sources.Add(new Source(sources.Count + 1, i % l, i / l, type));
            sourceEn[i] = (short)sources.Count;
        }
        for (var i = 0; i < n; i++)
        {
            if (!Climat.EstPraticable((Biome)carte.Biomes[i]) || grottes.Count >= short.MaxValue) continue;
            int x = i % l, y = i / l;
            var u = Hasard(i, 0x47524F54UL);
            var karst = roches[i] is Roche.Calcaire && pente[i] >= 1 || roches[i] == Roche.Craie && pente[i] >= 2;
            var lave = roches[i] is Roche.Volcanique or Roche.Basalte && volcans.Any(v => Math.Abs(v.X - x) <= 12 && Math.Abs(v.Y - y) <= 12);
            bool sousRoche;
            if (karst && u < 0.04 || lave && u < 0.01) sousRoche = false;
            else if (falaise[i] && roches[i] is Roche.Calcaire or Roche.Gres or Roche.Craie or Roche.Granite or Roche.Basalte && u < 0.06) sousRoche = true;
            else continue;
            var v2 = Hasard(i, 0x50524F46UL);
            // Profondeur : quelques mètres pour un abri, de dix mètres à plusieurs centaines pour une grotte (loi log-normale).
            var metres = sousRoche ? 3 + (int)(12 * v2) : (int)Math.Round(Math.Exp(Math.Log(40) + 1.2 * Normale(v2, Hasard(i, 0x4E4F524DUL))));
            var source = !sousRoche && roches[i] is Roche.Calcaire or Roche.Craie && Hasard(i, 0x52455355UL) < 0.3;
            grottes.Add(new Grotte(grottes.Count + 1, x, y, sousRoche, Math.Clamp(metres, 3, 2000), source));
            grotteEn[i] = (short)grottes.Count;
            if (source) AjouterSource(i, TypeSource.Douce);
        }
        for (var i = 0; i < n; i++)
        {
            if (!falaise[i] || roches[i] is not (Roche.Calcaire or Roche.Gres or Roche.Craie) || Hasard(i, 0x50494544UL) >= 0.08) continue;
            // Au pied de la paroi : la voisine la plus basse.
            int x = i % l, y = i / l, pied = -1;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= l || ny >= h) continue;
                var j = ny * l + nx;
                if (Climat.EstPraticable((Biome)carte.Biomes[j]) && (pied < 0 || carte.Altitudes[j] < carte.Altitudes[pied])) pied = j;
            }
            if (pied >= 0) AjouterSource(pied, TypeSource.Douce);
        }
        foreach (var v in volcans)
        {
            // Sources chaudes : une à trois en moyenne autour d'un volcan actif, à quelques kilomètres.
            var combien = Poisson(alea, 1.5);
            for (var k = 0; k < combien; k++)
            {
                for (var essai = 0; essai < 20; essai++)
                {
                    int x = v.X + alea.Entier(25) - 12, y = v.Y + alea.Entier(25) - 12;
                    if (x < 0 || y < 0 || x >= l || y >= h || !Climat.EstPraticable(carte.BiomeEn(x, y)) || (x, y) == (v.X, v.Y)) continue;
                    AjouterSource(y * l + x, TypeSource.Chaude);
                    break;
                }
            }
        }

        // Gisements.
        var gisements = new List<Gisement>();
        var gisementEn = new short[n];
        var parType = new List<int>[Enum.GetValues<Gite>().Length];
        for (var t = 0; t < parType.Length; t++) parType[t] = [];
        bool PresDUnVolcan(int i, int r) => volcans.Any(v => Math.Abs(v.X - i % l) <= r && Math.Abs(v.Y - i / l) <= r);
        bool DescendDuSocle(int i)
        {
            // Une rivière dont une case d'amont proche coule sur le granite : ses graviers en charrient les minéraux lourds.
            int x = i % l, y = i / l;
            for (var dy = -3; dy <= 3; dy++)
            {
                for (var dx = -3; dx <= 3; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < l && ny < h && roches[ny * l + nx] == Roche.Granite && carte.Altitudes[ny * l + nx] > carte.Altitudes[i]) return true;
                }
            }
            return false;
        }
        for (var i = 0; i < n; i++)
        {
            var b = (Biome)carte.Biomes[i];
            if (!Climat.EstPraticable(b)) continue;
            var r = roches[i];
            if (r == Roche.Evaporites) parType[(int)Gite.Sel].Add(i);
            if (r is Roche.Volcanique or Roche.Basalte && PresDUnVolcan(i, 6)) parType[(int)Gite.Soufre].Add(i);
            if (r == Roche.Basalte) parType[(int)Gite.CuivreNatif].Add(i);
            if (r is Roche.Volcanique || r == Roche.Granite && PresDUnVolcan(i, 30)) parType[(int)Gite.Malachite].Add(i);
            if (r == Roche.Granite || b == Biome.Riviere && DescendDuSocle(i)) parType[(int)Gite.Cassiterite].Add(i);
            if (b == Biome.Marais) parType[(int)Gite.FerDesMarais].Add(i);
            if (b == Biome.Riviere && DescendDuSocle(i)) parType[(int)Gite.Or].Add(i);
            if (r is Roche.Schiste or Roche.Gres) parType[(int)Gite.Charbon].Add(i);
            if (r is Roche.Calcaire or Roche.Schiste) parType[(int)Gite.Bitume].Add(i);
            if (r == Roche.Granite && pente[i] <= 1) parType[(int)Gite.Kaolin].Add(i);
        }
        // Gisements pour mille cases hôtes (640 km²), ordres de grandeur des cartes géologiques : le fer des marais est
        // commun, l'or et l'étain rares ; un volcan a presque toujours ses fumerolles soufrées.
        var densites = new Dictionary<Gite, double>
        {
            [Gite.Sel] = 6, [Gite.Soufre] = 0, [Gite.CuivreNatif] = 0.8, [Gite.Malachite] = 0.6, [Gite.Cassiterite] = 0.3,
            [Gite.FerDesMarais] = 20, [Gite.Or] = 1.5, [Gite.Charbon] = 0.4, [Gite.Bitume] = 0.2, [Gite.Kaolin] = 1,
        };
        var affleure = new Dictionary<Gite, double>
        {
            [Gite.Sel] = 0.9, [Gite.Soufre] = 0.95, [Gite.CuivreNatif] = 0.5, [Gite.Malachite] = 0.6, [Gite.Cassiterite] = 0.5,
            [Gite.FerDesMarais] = 0.9, [Gite.Or] = 0.9, [Gite.Charbon] = 0.5, [Gite.Bitume] = 0.9, [Gite.Kaolin] = 0.7,
        };
        foreach (var type in Enum.GetValues<Gite>())
        {
            var hotes = parType[(int)type];
            if (hotes.Count == 0) continue;
            var lambda = type == Gite.Soufre ? 1.2 * volcans.Count : densites[type] * hotes.Count / 1000.0;
            var combien = Poisson(alea, lambda);
            for (var k = 0; k < combien && gisements.Count < short.MaxValue; k++)
            {
                for (var essai = 0; essai < 10; essai++)
                {
                    var i = hotes[alea.Entier(hotes.Count)];
                    if (gisementEn[i] != 0 || gisements.Exists(g => Math.Max(Math.Abs(g.X - i % l), Math.Abs(g.Y - i / l)) < 3)) continue;
                    var u = alea.Reel();
                    var profondeur = u < affleure[type] ? Enfouissement.Affleurant : u < affleure[type] + 0.6 * (1 - affleure[type]) ? Enfouissement.SousLaSurface : Enfouissement.Profond;
                    gisements.Add(new Gisement(gisements.Count + 1, type, i % l, i / l, profondeur));
                    gisementEn[i] = (short)gisements.Count;
                    if (type == Gite.Sel && Hasard(i, 0x53414CUL) < 0.3) AjouterSource(i, TypeSource.Salee);
                    break;
                }
            }
        }

        return new Terrain(l)
        {
            Roches = roches, Pierres = pierres, Sols = sols, Fertilite = fertilite, FacteurSol = facteur, Pente = pente, Falaise = falaise,
            Aval = aval, Debit = debit, Courant = courant,
            Gisements = gisements, Grottes = grottes, Sources = sources, GisementEn = gisementEn, GrotteEn = grotteEn, SourceEn = sourceEn,
        };
    }

    /// <summary>Quatre tirages uniformes attachés à la zone de 4 × 4 cases de (x, y), stables pour une graine.</summary>
    private static (double U1, double U2, double U3, double U4) Zone(Carte carte, int x, int y)
    {
        var etat = carte.Graine ^ 0x524F43484553UL ^ ((ulong)(x / 4) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(y / 4) * 0xC2B2AE3D27D4EB4FUL);
        var z = Alea.SplitMix64(ref etat);
        return ((z & 0xFFFF) / 65536.0, ((z >> 16) & 0xFFFF) / 65536.0, ((z >> 32) & 0xFFFF) / 65536.0, ((z >> 48) & 0xFFFF) / 65536.0);
    }

    /// <summary>Loi normale centrée réduite à partir de deux tirages uniformes (Box-Muller).</summary>
    private static double Normale(double u1, double u2) => Math.Sqrt(-2 * Math.Log(1 - u1)) * Math.Cos(2 * Math.PI * u2);

    /// <summary>Tirage d'une loi de Poisson (méthode de Knuth ; les moyennes restent petites).</summary>
    private static int Poisson(Alea alea, double lambda)
    {
        if (lambda <= 0) return 0;
        var limite = Math.Exp(-Math.Min(lambda, 500));
        var k = 0;
        var p = alea.Reel();
        while (p > limite && k < 10_000)
        {
            k++;
            p *= alea.Reel();
        }
        return k;
    }

    /// <summary>
    /// Bruit lissé ramené à son rang parmi les cases (0 à 1) : les proportions de roches ne dépendent plus de la forme
    /// du bruit, seulement des seuils.
    /// </summary>
    private static double[] Rangs(Carte carte, Bruit bruit, double echelle, int octaves)
    {
        var n = carte.Largeur * carte.Hauteur;
        var valeurs = new double[n];
        for (var i = 0; i < n; i++) valeurs[i] = bruit.Fractal(i % carte.Largeur / echelle, i / carte.Largeur / echelle, octaves);
        var ordre = Enumerable.Range(0, n).ToArray();
        Array.Sort((double[])valeurs.Clone(), ordre);
        var rangs = new double[n];
        for (var k = 0; k < n; k++) rangs[ordre[k]] = (k + 0.5) / n;
        return rangs;
    }

    /// <summary>
    /// Plaine alluviale : au bord d'un lac, ou d'une rivière assez grosse pour s'étaler en crue (débit moyen d'au moins
    /// 2 m³/s ; un ruisseau n'a pas de plaine).
    /// </summary>
    private static bool Vallee(Carte carte, float[] debit, int x, int y)
    {
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            var j = ny * carte.Largeur + nx;
            if (carte.Biomes[j] == (byte)Biome.Lac || debit[j] >= 2) return true;
        }
        return false;
    }

    private static bool BordDeMer(Carte carte, int x, int y)
    {
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < carte.Largeur && ny < carte.Hauteur && Biomes.EstEauSalee(carte.BiomeEn(nx, ny))) return true;
        }
        return false;
    }

    /// <summary>Une cuvette : la case est plus basse que la moyenne des terres alentour (3 cases, 2,4 km).</summary>
    private static bool Cuvette(Carte carte, int x, int y)
    {
        double somme = 0;
        var nombre = 0;
        for (var dy = -3; dy <= 3; dy++)
        {
            for (var dx = -3; dx <= 3; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                somme += carte.Altitudes[ny * carte.Largeur + nx];
                nombre++;
            }
        }
        return carte.Altitudes[y * carte.Largeur + x] < somme / nombre - 0.5;
    }
}
