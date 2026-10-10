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
        Assert.Equal(0, monde.Facteur);
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

    [Theory]
    [InlineData(0.5)]
    [InlineData(3_000)]
    [InlineData(Vitesse.FacteurMax)]
    public void VitesseLibre_EstRespectee(double facteur)
    {
        var monde = Monde();
        monde.ChangerFacteur(facteur);
        for (var i = 0; i < 20; i++) monde.Avancer(TimeSpan.FromMilliseconds(100)); // 2 s réelles
        Assert.Equal(2 * facteur, monde.Etat.Secondes, tolerance: 1);
        Assert.Equal(facteur, monde.Etat.Facteur);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.05)]
    [InlineData(Vitesse.FacteurMax * 2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void VitesseLibreHorsLimites_EstRefusee(double facteur)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Monde().ChangerFacteur(facteur));
    }

    [Fact]
    public void BudgetEpuise_LeRetardEstAbandonne()
    {
        var monde = new MondeSimule(Carte42, MondeSimule.Nouveau(42));
        monde.ChangerFacteur(Vitesse.FacteurMax);
        // Un dixième de seconde réelle demanderait 1 000 000 s du monde (11 jours) : avec 1 ms de calcul, on en fait bien moins.
        monde.Avancer(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1));
        var apres = monde.Etat.Secondes;
        Assert.InRange(apres, 6 * 3600 + 600, 6 * 3600 + 1_000_000 - 1);
        Assert.Equal(0, apres % MondeSimule.SecondesParPas); // arrêté sur un pas, sans reliquat
    }

    [Fact]
    public void AllerJusquaUneDate_SArreteDessusEtMetEnPause()
    {
        var monde = new MondeSimule(Carte42, MondeSimule.Nouveau(42));
        var cible = Objectif.Secondes(1, 2, 10, 12); // an 1, jour 10 de l'été, 12 h
        monde.FixerObjectif(Objectif.Date(cible, monde.Secondes));

        while (monde.Objectif is not null) monde.Avancer(TimeSpan.FromMilliseconds(100));

        Assert.Equal(cible, monde.Secondes);
        Assert.Equal(0, monde.Facteur);
        Assert.Equal("An 1, jour 10 de l'été, 12:00", monde.PrendreObjectifAtteint());
        Assert.Null(monde.PrendreObjectifAtteint()); // annoncé une seule fois
        Assert.Equal(0, monde.Avancer(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(TypeObjectif.Naissance)]
    [InlineData(TypeObjectif.Mort)]
    public void AllerJusquaUnEvenement_SArreteAuPasOuIlSurvient(TypeObjectif type)
    {
        var monde = new MondeSimule(Carte42, MondeSimule.Nouveau(42));
        var avant = monde.Etat.Humains!;
        monde.FixerObjectif(Objectif.Evenement(type, monde.Secondes));

        while (monde.Objectif is not null) monde.Avancer(TimeSpan.FromSeconds(1));

        var apres = monde.Etat.Humains!;
        if (type == TypeObjectif.Naissance) Assert.True(apres.Count > avant.Count);
        else Assert.Contains(apres, h => h.MortLe == monde.Secondes);
        Assert.Equal(0, monde.Facteur);
        Assert.NotNull(monde.PrendreObjectifAtteint());
    }

    [Fact]
    public void ChangerDeVitesse_AnnuleLObjectif()
    {
        var monde = Monde();
        monde.FixerObjectif(Objectif.Date(Objectif.ProchaineAnnee(0), 0));
        monde.ChangerVitesse(0);
        Assert.Null(monde.Objectif);
        Assert.Throws<ArgumentOutOfRangeException>(() => monde.FixerObjectif(new Objectif(TypeObjectif.Date, 0, "passé")));
    }

    [Fact]
    public void Calendrier_DesObjectifs()
    {
        Assert.Equal(0, Objectif.Secondes(1, 1, 1, 0));
        Assert.Equal(Calendrier.SecondesParAn + 90 * Calendrier.SecondesParJour + 6 * 3600, Objectif.Secondes(2, 2, 1, 6));
        Assert.Equal(6 * 3600, Objectif.ProchainMatin(0));
        Assert.Equal(Calendrier.SecondesParJour + 6 * 3600, Objectif.ProchainMatin(6 * 3600));
        Assert.Equal(90 * Calendrier.SecondesParJour, Objectif.ProchaineSaison(5));
        Assert.Equal(Calendrier.SecondesParAn, Objectif.ProchaineAnnee(Calendrier.SecondesParAn - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Objectif.Secondes(1, 5, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Objectif.Secondes(1, 1, 91, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Objectif.Secondes(0, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Objectif.Date(10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Objectif.Date(Objectif.PorteeMaximale + 1, 0));
    }
}
