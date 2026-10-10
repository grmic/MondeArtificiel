using Monde.Simulation.Temps;

namespace Monde.Tests;

public sealed class CalendrierTests
{
    [Fact]
    public void Origine_EstLePremierJourDuPrintempsAMinuit()
    {
        var date = Calendrier.Decomposer(0);
        Assert.Equal((1L, Saison.Printemps, 1, 0, 0), (date.Annee, date.Saison, date.JourDeSaison, date.Heure, date.Minute));
        Assert.False(date.EstJour);
        Assert.Equal("An 1, jour 1 du printemps, 00:00", date.ToString());
    }

    [Theory]
    [InlineData(90, Saison.Ete, "de l'été")]
    [InlineData(180, Saison.Automne, "de l'automne")]
    [InlineData(270, Saison.Hiver, "de l'hiver")]
    public void ChangementDeSaison_TousLes90Jours(int jours, Saison attendue, string texte)
    {
        var date = Calendrier.Decomposer(jours * Calendrier.SecondesParJour);
        Assert.Equal(attendue, date.Saison);
        Assert.Equal(1, date.JourDeSaison);
        Assert.Contains(texte, date.ToString());
    }

    [Fact]
    public void UneAnneeCompte360Jours()
    {
        var finAn1 = Calendrier.Decomposer(Calendrier.SecondesParAn - 1);
        var debutAn2 = Calendrier.Decomposer(Calendrier.SecondesParAn);
        Assert.Equal((1L, Saison.Hiver, 90, 23, 59), (finAn1.Annee, finAn1.Saison, finAn1.JourDeSaison, finAn1.Heure, finAn1.Minute));
        Assert.Equal((2L, Saison.Printemps, 1), (debutAn2.Annee, debutAn2.Saison, debutAn2.JourDeSaison));
    }

    [Theory]
    [InlineData(Saison.Printemps, 12, 1.0)]
    [InlineData(Saison.Printemps, 2, 0.0)]
    [InlineData(Saison.Printemps, 6, 0.5)]   // lever à 6 h : aube à mi-chemin
    [InlineData(Saison.Ete, 19, 1.0)]        // l'été, il fait encore plein jour à 19 h
    [InlineData(Saison.Hiver, 17.5, 0.0)]    // l'hiver, nuit à 17 h 30
    public void Lumiere_SuitLaDureeDuJourSelonLaSaison(Saison saison, double heure, double attendue)
    {
        Assert.Equal(attendue, Calendrier.Lumiere(saison, heure), 6);
    }

    [Fact]
    public void TempsNegatif_EstRefuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendrier.Decomposer(-1));
    }
}
