using Monde.Simulation.Humains;
using Monde.Simulation.Monde;

namespace Monde.Simulation.Nature;

/// <summary>Ce qu'il faut franchir pour passer d'une case à sa voisine, vu par un marcheur.</summary>
public enum Franchissement : byte
{
    /// <summary>Une marche ordinaire, même en montée.</summary>
    Marche = 0,

    /// <summary>Une pente très raide sans paroi (120 m ou plus de dénivelé sur la case) : on peut y glisser.</summary>
    PenteRaide = 1,

    /// <summary>Un couloir, une vire, une cheminée ou un éboulis dans une falaise : quelques mètres de pas exposés.</summary>
    Passage = 2,

    /// <summary>Une paroi sans passage : on ne la franchit qu'en grimpant ou en désescaladant.</summary>
    Paroi = 3,
}

/// <summary>
/// Le relief tel que le vit un marcheur (mondes de la génération 2, depuis la 0.32.7). Une case fait 800 m et l'altitude se
/// compte par marches de 40 m : entre deux cases voisines qui diffèrent de 200 m ou plus, dans une roche dure, il y a souvent
/// une paroi. La paroi est entre les deux cases : on longe sans danger le pied ou le haut d'une falaise, c'est la franchir qui
/// coûte. Certaines parois ont un passage (couloir d'éboulis, vire, cheminée, dalle inclinée), plus souvent dans le calcaire et
/// le grès, qui se creusent et se délitent en gradins, que dans la craie qui s'effrite ou le granite massif.
/// <para>
/// Les cols sont les selles d'une crête : la case est plus basse que deux voisines opposées et plus haute que les deux autres
/// côtés. Les chemins passent par eux d'eux-mêmes, parce qu'ils cherchent l'effort le plus faible (voir <see cref="Marche"/>).
/// </para>
/// Rien n'est enregistré : tout se recalcule de la graine et de la carte.
/// </summary>
public sealed class Relief
{
    /// <summary>Un dénivelé de 120 m (3 marches) entre deux cases sans paroi : une pente où l'on peut glisser.</summary>
    public const int PenteRaide = 3;

    /// <summary>À partir de 200 m (5 marches) entre deux cases, dans une roche dure, une paroi devient possible.</summary>
    public const int DeniveleParoi = 5;

    private readonly int _largeur;
    private readonly int _hauteur;
    private readonly byte[] _altitudes;
    private readonly ulong _graine;

    /// <summary>Pour chaque case, un bit par voisine (voir <see cref="Direction"/>) : une paroi sans passage les sépare.</summary>
    private readonly byte[] _parois;

    /// <summary>Pour chaque case, un bit par voisine : un passage dans une paroi les relie.</summary>
    private readonly byte[] _passages;

    /// <summary>La case est un col (selle d'une crête).</summary>
    public bool[] Cols { get; }

    /// <summary>
    /// L'effort de marche de chaque case vers chacune de ses 8 voisines (plus 1, 0 = pas encore calculé), rempli à mesure que les
    /// humains cherchent leur chemin : la carte ne change pas, et deux fils qui le calculent en même temps trouvent la même valeur.
    /// </summary>
    public int[] Efforts { get; }

    private Relief(int largeur, int hauteur, byte[] altitudes, ulong graine, byte[] parois, byte[] passages, bool[] cols)
    {
        (_largeur, _hauteur, _altitudes, _graine, _parois, _passages, Cols) = (largeur, hauteur, altitudes, graine, parois, passages, cols);
        Efforts = new int[largeur * hauteur * 8];
    }

    /// <summary>Numéro (0 à 7) de la direction de (dx, dy), chacun entre −1 et 1, sans (0, 0).</summary>
    public static int Direction(int dx, int dy)
    {
        var k = (dy + 1) * 3 + dx + 1;
        return k < 4 ? k : k - 1;
    }

    /// <summary>Part des parois qui ont un passage, selon la roche (estimations : gradins du calcaire et du grès, craie qui s'effrite).</summary>
    private static double PartDePassages(Roche r) => r switch
    {
        Roche.Calcaire or Roche.Gres => 0.18,
        Roche.Volcanique => 0.15,
        Roche.Basalte => 0.14,
        Roche.Granite => 0.10,
        Roche.Craie => 0.06,
        _ => 1,
    };

    /// <summary>Penchant d'une roche à faire des parois (0 : jamais, les roches tendres font des pentes et des ravines).</summary>
    private static double Durete(Roche r) => r switch
    {
        Roche.Calcaire or Roche.Granite or Roche.Basalte => 1,
        Roche.Gres => 0.8,
        Roche.Volcanique => 0.7,
        Roche.Craie => 0.6,
        _ => 0,
    };

    /// <summary>Hasard propre à une arête (les deux cases, dans n'importe quel ordre) : le même à chaque calcul.</summary>
    private double Hasard(int i, int j, ulong sel)
    {
        var (a, b) = i < j ? (i, j) : (j, i);
        var etat = _graine ^ sel ^ ((ulong)a * 0x9E3779B97F4A7C15UL) ^ ((ulong)b * 0xC2B2AE3D27D4EB4FUL);
        return (Alea.SplitMix64(ref etat) >> 11) * (1.0 / (1UL << 53));
    }

    public static Relief Calculer(Carte carte)
    {
        int l = carte.Largeur, h = carte.Hauteur, n = l * h;
        var terrain = carte.Terrain;
        var relief = new Relief(l, h, carte.Altitudes, carte.Graine ^ 0x52454C494546UL, new byte[n], new byte[n], new bool[n]);
        for (var i = 0; i < n; i++)
        {
            if (!Climat.EstPraticable((Biome)carte.Biomes[i])) continue;
            int x = i % l, y = i / l;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (dx == 0 && dy == 0 || nx < 0 || ny < 0 || nx >= l || ny >= h) continue;
                var j = ny * l + nx;
                if (!Climat.EstPraticable((Biome)carte.Biomes[j])) continue;
                var d = Math.Abs(carte.Altitudes[i] - carte.Altitudes[j]);
                if (d < DeniveleParoi) continue;
                // La roche de la case haute fait la paroi ; plus le dénivelé est fort, plus il y a de rocher nu (à 400 m, toujours).
                var roche = terrain.Roches[carte.Altitudes[i] >= carte.Altitudes[j] ? i : j];
                var p = Math.Clamp((d - 4) / 6.0, 0, 1) * Durete(roche);
                if (relief.Hasard(i, j, 0x5041524F49UL) >= p) continue;
                var bit = (byte)(1 << Direction(dx, dy));
                if (relief.Hasard(i, j, 0x50415353UL) < PartDePassages(roche)) relief._passages[i] |= bit;
                else relief._parois[i] |= bit;
            }
        }
        // Cols : sur un cercle de 2,4 km autour de la case, le terrain alterne entre plus haut et plus bas (deux crêtes, deux
        // vallées) ; parmi les cases voisines qui le sont aussi, seule la plus basse est le col.
        var anneau = Enumerable.Range(0, 16).Select(k => ((int)Math.Round(3 * Math.Cos(k * Math.PI / 8)), (int)Math.Round(3 * Math.Sin(k * Math.PI / 8)))).ToArray();
        var selle = new bool[n];
        for (var y = 3; y < h - 3; y++)
        for (var x = 3; x < l - 3; x++)
        {
            var i = y * l + x;
            if (!Climat.EstPraticable((Biome)carte.Biomes[i]) || carte.Altitudes[i] < Carte.NiveauMer + 5) continue;
            int a = carte.Altitudes[i], changements = 0, premier = 0, precedent = 0;
            foreach (var (dx, dy) in anneau)
            {
                var v = carte.Altitudes[(y + dy) * l + x + dx] - a;
                var signe = v >= 3 ? 1 : v <= -2 ? -1 : 0;
                if (signe == 0) continue;
                if (premier == 0) premier = signe;
                else if (signe != precedent) changements++;
                precedent = signe;
            }
            if (precedent != 0 && precedent != premier) changements++;
            selle[i] = changements >= 4;
        }
        for (var i = 0; i < n; i++)
        {
            if (!selle[i]) continue;
            int x = i % l, y = i / l;
            var plusBas = true;
            for (var dy = -2; dy <= 2 && plusBas; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var j = (y + dy) * l + x + dx;
                if (j != i && selle[j] && (carte.Altitudes[j] < carte.Altitudes[i] || carte.Altitudes[j] == carte.Altitudes[i] && j < i)) { plusBas = false; break; }
            }
            relief.Cols[i] = plusBas;
        }
        return relief;
    }

    /// <summary>Ce qu'il faut franchir pour aller de la case i à sa voisine j.</summary>
    public Franchissement Entre(int i, int j)
    {
        int dx = j % _largeur - i % _largeur, dy = j / _largeur - i / _largeur;
        var bit = 1 << Direction(dx, dy);
        if ((_parois[i] & bit) != 0 || (_parois[j] & (1 << Direction(-dx, -dy))) != 0) return Franchissement.Paroi;
        if ((_passages[i] & bit) != 0 || (_passages[j] & (1 << Direction(-dx, -dy))) != 0) return Franchissement.Passage;
        return Math.Abs(_altitudes[i] - _altitudes[j]) >= PenteRaide ? Franchissement.PenteRaide : Franchissement.Marche;
    }

    /// <summary>Une paroi sans passage sépare i de sa voisine j.</summary>
    public bool Paroi(int i, int j) => Entre(i, j) == Franchissement.Paroi;

    /// <summary>La case touche-t-elle une paroi ou un passage (pour la carte) ?</summary>
    public (bool Paroi, bool Passage) Bords(int i)
    {
        int x = i % _largeur, y = i / _largeur;
        bool paroi = _parois[i] != 0, passage = _passages[i] != 0;
        for (var dy = -1; dy <= 1 && !(paroi && passage); dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (dx == 0 && dy == 0 || nx < 0 || ny < 0 || nx >= _largeur || ny >= _hauteur) continue;
            var j = ny * _largeur + nx;
            var bit = 1 << Direction(-dx, -dy);
            paroi |= (_parois[j] & bit) != 0;
            passage |= (_passages[j] & bit) != 0;
        }
        return (paroi, passage);
    }

    /// <summary>
    /// Hauteur exposée, en mètres, quand on franchit l'arête de i à j : la partie verticale d'une paroi (15 à 80 % du dénivelé),
    /// les pas délicats d'un passage (2 à 12 m), le ressaut d'une pente raide où l'on glisse (1 à 4 m).
    /// </summary>
    public double Hauteur(int i, int j) => Entre(i, j) switch
    {
        Franchissement.Paroi => Math.Abs(_altitudes[i] - _altitudes[j]) * Echelle.MetresParUnite * (0.15 + 0.65 * Hasard(i, j, 0x48415554UL)),
        Franchissement.Passage => 2 + 10 * Hasard(i, j, 0x45585053UL),
        Franchissement.PenteRaide => 1 + 3 * Hasard(i, j, 0x474C4953UL),
        _ => 0,
    };
}
