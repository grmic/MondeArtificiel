using Monde.Simulation.Monde;

namespace Monde.Tests;

public sealed class AleaTests
{
    [Fact]
    public void SplitMix64_DonneLaValeurDeReference()
    {
        // Première sortie de SplitMix64 pour un état initial nul (implémentation de référence de S. Vigna).
        ulong etat = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, Alea.SplitMix64(ref etat));
    }

    [Fact]
    public void MemeGraine_MemeSuite()
    {
        var a = new Alea(123);
        var b = new Alea(123);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(a.Suivant(), b.Suivant());
        }
    }

    [Fact]
    public void Entier_ResteDansLIntervalle()
    {
        var alea = new Alea(7);
        for (var i = 0; i < 10_000; i++)
        {
            Assert.InRange(alea.Entier(33), 0, 32);
            Assert.InRange(alea.Reel(), 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void Entier_RefuseUneBorneNulle()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Alea(1).Entier(0));
    }
}
