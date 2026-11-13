using Monde.Simulation;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>« Avancer de N ans » (0.40.0) : le monde file jusqu'à la date, puis dit ce qui s'est passé.</summary>
public sealed class SautTests
{
    [Fact]
    public void Saut_ArriveALaDate_EtFaitLeBilan()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var temoin = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var debut = monde.Secondes;
        monde.Sauter(1);
        Assert.NotNull(monde.Saut);
        Assert.Null(monde.PrendreBilanSaut());
        monde.AvancerDe(3 * Calendrier.SecondesParAn);
        temoin.AvancerDe(Calendrier.SecondesParAn);

        Assert.Equal(debut + Calendrier.SecondesParAn, monde.Secondes);
        Assert.Null(monde.Saut);
        var bilan = monde.PrendreBilanSaut();
        Assert.NotNull(bilan);
        Assert.Null(monde.PrendreBilanSaut());
        Assert.False(bilan.Interrompu);
        // Même histoire qu'un monde qui a simplement avancé d'un an.
        Assert.Equal(HumainsTests.Empreinte(temoin), HumainsTests.Empreinte(monde));
        // Les comptes tiennent : les vivants d'après sont ceux d'avant, plus les naissances, moins les morts.
        Assert.Equal(bilan.VivantsAvant + bilan.Naissances - bilan.Morts.Values.Sum(), bilan.VivantsApres);
        Assert.True(bilan.Naissances > 0);
        Assert.True(bilan.DernierEvenement >= bilan.PremierEvenement);
    }

    [Fact]
    public void Saut_ArreteParLeJoueur_EstInterrompu()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.Sauter(10);
        monde.AvancerDe(30 * Calendrier.SecondesParJour);
        monde.ChangerVitesse(0);
        var bilan = monde.PrendreBilanSaut();
        Assert.NotNull(bilan);
        Assert.True(bilan.Interrompu);
    }
}
