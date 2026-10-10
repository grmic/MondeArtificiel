using System.Security.Cryptography;

namespace Monde.Simulation.Monde;

/// <summary>Lieu de départ d'un couple.</summary>
public sealed record Emplacement(int Numero, int X, int Y);

/// <summary>
/// Carte du continent : une grille de tuiles stockée en tableaux à plat (indice = y * Largeur + x).
/// </summary>
public sealed class Carte
{
    public required ulong Graine { get; init; }
    public required int Largeur { get; init; }
    public required int Hauteur { get; init; }

    /// <summary>Biome de chaque tuile (valeur de <see cref="Biome"/>).</summary>
    public required byte[] Biomes { get; init; }

    /// <summary>Altitude de 0 à 255 ; le niveau de la mer vaut <see cref="NiveauMer"/>.</summary>
    public required byte[] Altitudes { get; init; }

    /// <summary>Abondance du gibier, de 0 à 100.</summary>
    public required byte[] Gibier { get; init; }

    /// <summary>Abondance des plantes comestibles, de 0 à 100.</summary>
    public required byte[] Plantes { get; init; }

    public required IReadOnlyList<Emplacement> Emplacements { get; init; }

    public const byte NiveauMer = 128;

    public int Indice(int x, int y) => y * Largeur + x;

    public Biome BiomeEn(int x, int y) => (Biome)Biomes[Indice(x, y)];

    /// <summary>Empreinte SHA-256 du contenu, pour vérifier qu'une graine redonne la même carte.</summary>
    public string Empreinte()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Biomes);
        hash.AppendData(Altitudes);
        hash.AppendData(Gibier);
        hash.AppendData(Plantes);
        foreach (var e in Emplacements)
        {
            hash.AppendData(BitConverter.GetBytes(e.X));
            hash.AppendData(BitConverter.GetBytes(e.Y));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
