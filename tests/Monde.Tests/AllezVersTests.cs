using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.45.21 : « Allez vers… » une personne, dit au Monde ou contenu dans une révélation.</summary>
public sealed class AllezVersTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;

    private static (MondeSimule Monde, Humain Vers) Monde()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(Jour);
        var vers = monde.InstantaneHumains().Humains.First(h => h.EstVivant && !h.EstEnfant(monde.Secondes));
        return (monde, vers);
    }

    [Theory]
    [InlineData("Allez tous à la rencontre de {0}.")]
    [InlineData("Je vous le dis : allez vers {0}, il vous attend.")]
    [InlineData("Partez dès l'aube rejoindre {0}.")]
    [InlineData("All of you, go and meet {0}.")]
    public void UneRevelation_QuiDitDAllerVersQuelquUn_LeNomme(string modele)
    {
        var (monde, vers) = Monde();
        var tous = monde.InstantaneHumains().Humains;
        Assert.Equal(vers.Id, OrdresReveles.VaVers(string.Format(modele, vers.Nom), tous)?.Id);
        Assert.Equal(vers.Id, OrdresReveles.VaVers(string.Format(modele, vers.Nom.ToLowerInvariant()), tous)?.Id);
    }

    [Theory]
    [InlineData("Je suis celle qui vous a faits.")]
    [InlineData("Allez vers la mer.")]
    [InlineData("{0} est né de la pluie.")]
    public void UneRevelation_SansOrdre_NeNommePersonne(string modele)
    {
        var (monde, vers) = Monde();
        Assert.Null(OrdresReveles.VaVers(string.Format(modele, vers.Nom), monde.InstantaneHumains().Humains));
    }

    [Fact]
    public void AuMonde_AllezVers_ChacunDecide_EtCeuxQuiPartentMarchentVersLui()
    {
        var (monde, vers) = Monde();
        Assert.True(Paroles.AuMonde(Parole.VaVers));
        Assert.Null(monde.ParlerAuMonde(Parole.VaVers, ModeParole.Voix, 0, 0, 0, 0));
        var r = monde.ParlerAuMonde(Parole.VaVers, ModeParole.Apparition, 0, vers.Id, 0, 0);
        Assert.NotNull(r);
        var bilans = r.Value.Parole.Bilans;
        Assert.True(bilans.Sum(b => b.Veulent) > 0, string.Join(" | ", r.Value.Resultats.Select(x => x.Resultat.Cle)));
        Assert.True(bilans.Sum(b => b.Veulent + b.Hesitent + b.Refusent) <= bilans.Sum(b => b.Crus));
        Assert.DoesNotContain(r.Value.Resultats, x => x.Humain == vers.Id);
    }

    [Fact]
    public void UneRevelation_QuiContientLOrdre_DevientCetOrdre()
    {
        var (monde, vers) = Monde();
        var (_, _, _, crus, ordre) = monde.Reveler(0, $"Allez tous à la rencontre de {vers.Nom}.", ModeParole.Apparition);
        Assert.NotNull(ordre);
        Assert.Equal(vers.Id, ordre.Vers);
        Assert.True(ordre.Veulent + ordre.Hesitent + ordre.Refusent > 0);
        Assert.True(ordre.Veulent + ordre.Hesitent + ordre.Refusent <= crus);
        var (_, _, _, _, rien) = monde.Reveler(0, "Je suis celle qui vous a faits.", ModeParole.Voix);
        Assert.Null(rien);
    }
}
