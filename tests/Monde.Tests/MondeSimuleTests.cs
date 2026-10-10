using Monde.Simulation;
using Monde.Simulation.Temps;

namespace Monde.Tests;

public sealed class MondeSimuleTests
{
    [Fact]
    public void NouveauMonde_CommenceALAubeEnPause()
    {
        var monde = new MondeSimule(MondeSimule.Nouveau(42));
        Assert.Equal(6, monde.Date.Heure);
        Assert.Equal(0, monde.Vitesse.Facteur);
        Assert.Equal(0, monde.Avancer(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AvancerDe_ExecuteUnPasToutesLes10Minutes()
    {
        var monde = new MondeSimule(new EtatMonde(1, 0, 0, 0));
        Assert.Equal(0, monde.AvancerDe(599));
        Assert.Equal(1, monde.AvancerDe(1));
        Assert.Equal(144, monde.AvancerDe(Calendrier.SecondesParJour));
        Assert.Equal(145, monde.Etat.Pas);
    }

    [Fact]
    public void Avancer_RespecteLaVitesse()
    {
        var monde = new MondeSimule(new EtatMonde(1, 0, 0, 0));
        monde.ChangerVitesse(3); // 1 min réelle = 1 jour

        for (var i = 0; i < 600; i++) monde.Avancer(TimeSpan.FromMilliseconds(100)); // 60 s réelles

        Assert.Equal(Calendrier.SecondesParJour, monde.Etat.Secondes, tolerance: 1);
    }

    [Fact]
    public void Accelere_5MinutesFont5Ans()
    {
        var monde = new MondeSimule(new EtatMonde(1, 0, 0, 0));
        monde.ChangerVitesse(5);

        for (var i = 0; i < 3000; i++) monde.Avancer(TimeSpan.FromMilliseconds(100)); // 300 s réelles

        Assert.Equal(6, monde.Date.Annee); // de l'an 1 au début de l'an 6
        Assert.Equal(5 * Calendrier.SecondesParAn / MondeSimule.SecondesParPas, monde.Etat.Pas);
    }

    [Fact]
    public void Avancer_PlafonneUnGrandEcart()
    {
        var monde = new MondeSimule(new EtatMonde(1, 0, 0, 0));
        monde.ChangerVitesse(1);
        monde.Avancer(TimeSpan.FromHours(3)); // ex. sortie de veille
        Assert.Equal(1, monde.Etat.Secondes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void VitesseInconnue_EstRefusee(int niveau)
    {
        var monde = new MondeSimule(MondeSimule.Nouveau(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => monde.ChangerVitesse(niveau));
    }

    [Fact]
    public void EtatInvalide_EstRefuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MondeSimule(new EtatMonde(1, -5, 0, 0)));
    }
}
