using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

public sealed class MondeSimuleTests
{
    internal static readonly Carte Carte42 = GenerateurContinent.Generer(42);

    private static MondeSimule Monde(long secondes = 0) => new(Carte42, new EtatMonde(42, secondes, 0, 0));

    [Fact]
    public void NouveauMonde_CommenceALAubeEnPause()
    {
        var monde = new MondeSimule(Carte42, MondeSimule.Nouveau(42));
        Assert.Equal(6, monde.Date.Heure);
        Assert.Equal(0, monde.Vitesse.Facteur);
        Assert.Equal(0, monde.Avancer(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AvancerDe_ExecuteUnPasToutesLes10Minutes()
    {
        var monde = Monde();
        Assert.Equal(0, monde.AvancerDe(599));
        Assert.Equal(1, monde.AvancerDe(1));
        Assert.Equal(144, monde.AvancerDe(Calendrier.SecondesParJour));
        Assert.Equal(145, monde.Etat.Pas);
        Assert.Equal(600 + Calendrier.SecondesParJour, monde.Etat.Secondes);
    }

    [Fact]
    public void Avancer_RespecteLaVitesse()
    {
        var monde = Monde();
        monde.ChangerVitesse(3); // 1 min réelle = 1 jour

        for (var i = 0; i < 600; i++) monde.Avancer(TimeSpan.FromMilliseconds(100)); // 60 s réelles

        Assert.Equal(Calendrier.SecondesParJour, monde.Etat.Secondes, tolerance: 1);
    }

    [Fact]
    public void VitesseMaximale_5MinutesFont5Ans()
    {
        // 6 s réelles à la vitesse maximale = 6 × 518 400 s du monde ; 300 s donneraient exactement 5 ans.
        var monde = Monde();
        monde.ChangerVitesse(5);

        for (var i = 0; i < 60; i++) monde.Avancer(TimeSpan.FromMilliseconds(100));

        Assert.Equal(6 * 518_400, monde.Etat.Secondes, tolerance: 1);
        Assert.Equal(5 * Calendrier.SecondesParAn, 300 * Vitesse.Toutes[5].Facteur);
    }

    [Fact]
    public void Avancer_PlafonneUnGrandEcart()
    {
        var monde = Monde();
        monde.ChangerVitesse(1);
        monde.Avancer(TimeSpan.FromHours(3)); // ex. sortie de veille
        Assert.Equal(1, monde.Etat.Secondes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void VitesseInconnue_EstRefusee(int niveau)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Monde().ChangerVitesse(niveau));
    }

    [Fact]
    public void EtatInvalide_EstRefuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MondeSimule(Carte42, new EtatMonde(42, -5, 0, 0)));
    }

    [Fact]
    public void CarteDUneAutreGraine_EstRefusee()
    {
        Assert.Throws<ArgumentException>(() => new MondeSimule(Carte42, new EtatMonde(7, 0, 0, 0)));
    }
}
