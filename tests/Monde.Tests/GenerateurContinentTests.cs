using Monde.Simulation.Monde;

namespace Monde.Tests;

public sealed class GenerateurContinentTests
{
    public static TheoryData<ulong> Graines => [0, 1, 2, 3, 7, 42, 1000, 123456789, uint.MaxValue];

    [Fact]
    public void MemeGraine_MemeCarte()
    {
        var a = GenerateurContinent.Generer(42);
        var b = GenerateurContinent.Generer(42);
        Assert.Equal(a.Empreinte(), b.Empreinte());
    }

    [Fact]
    public void GrainesDifferentes_CartesDifferentes()
    {
        Assert.NotEqual(GenerateurContinent.Generer(1).Empreinte(), GenerateurContinent.Generer(2).Empreinte());
    }

    [Theory]
    [MemberData(nameof(Graines))]
    public void Bords_SontToujoursEnMer(ulong graine)
    {
        var carte = GenerateurContinent.Generer(graine);
        for (var x = 0; x < carte.Largeur; x++)
        {
            Assert.True(Biomes.EstEauSalee(carte.BiomeEn(x, 0)));
            Assert.True(Biomes.EstEauSalee(carte.BiomeEn(x, carte.Hauteur - 1)));
        }
    }

    [Theory]
    [MemberData(nameof(Graines))]
    public void Emplacements_SontAuNombreDe33_SurTerreHabitable_EtDistincts(ulong graine)
    {
        var carte = GenerateurContinent.Generer(graine);

        Assert.Equal(GenerateurContinent.NombreEmplacements, carte.Emplacements.Count);
        Assert.Equal(carte.Emplacements.Count, carte.Emplacements.Select(e => (e.X, e.Y)).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 33), carte.Emplacements.Select(e => e.Numero));
        Assert.All(carte.Emplacements, e => Assert.True(Biomes.EstHabitable(carte.BiomeEn(e.X, e.Y))));
    }

    [Theory]
    [MemberData(nameof(Graines))]
    public void Emplacements_SontTousSurLeMemeContinent(ulong graine)
    {
        var carte = GenerateurContinent.Generer(graine);

        // Parcours de la terre (tout sauf la mer) depuis le premier emplacement.
        var atteint = new bool[carte.Biomes.Length];
        var file = new Queue<(int X, int Y)>();
        var depart = carte.Emplacements[0];
        atteint[carte.Indice(depart.X, depart.Y)] = true;
        file.Enqueue((depart.X, depart.Y));
        while (file.Count > 0)
        {
            var (x, y) = file.Dequeue();
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var i = carte.Indice(nx, ny);
                if (atteint[i] || Biomes.EstEauSalee((Biome)carte.Biomes[i])) continue;
                atteint[i] = true;
                file.Enqueue((nx, ny));
            }
        }

        Assert.All(carte.Emplacements, e => Assert.True(atteint[carte.Indice(e.X, e.Y)]));
    }

    [Fact]
    public void Carte_ContientDesRivieresEtDesTerresVariees()
    {
        var carte = GenerateurContinent.Generer(42);
        var presents = carte.Biomes.Select(b => (Biome)b).ToHashSet();

        Assert.Contains(Biome.Riviere, presents);
        Assert.Contains(Biome.PlaineDeFougeres, presents);
        Assert.Contains(Biome.Montagne, presents);
        Assert.All(carte.Gibier, g => Assert.InRange(g, (byte)0, (byte)100));
    }

    [Theory]
    [InlineData(10, 256)]
    [InlineData(256, 5000)]
    public void TailleInvalide_EstRefusee(int largeur, int hauteur)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GenerateurContinent.Generer(1, largeur, hauteur));
    }
}
