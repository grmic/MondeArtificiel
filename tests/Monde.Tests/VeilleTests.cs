using Monde.Simulation;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>La veille sur les suivis ★ (0.40.0) prévient sans rien changer à l'histoire du monde.</summary>
public sealed class VeilleTests
{
    private static ReglesAlertes SansPause()
    {
        var r = ReglesAlertes.ParDefaut();
        Array.Fill(r.Pause, false);
        return r;
    }

    [Fact]
    public void Veille_NeChangeRienAuMonde_EtPrevient()
    {
        var temoin = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var veille = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        temoin.AvancerDe(20 * Calendrier.SecondesParJour);
        veille.AvancerDe(20 * Calendrier.SecondesParJour);
        var humains = veille.Etat.Humains!.Where(h => h.EstVivant).Select(h => h.Id).ToList();
        var communautes = veille.Etat.Communautes!.Select(c => c.Id).ToList();
        veille.DefinirVeille(humains, communautes, SansPause());

        temoin.AvancerDe(200 * Calendrier.SecondesParJour);
        veille.AvancerDe(200 * Calendrier.SecondesParJour);

        Assert.Equal(HumainsTests.Empreinte(temoin), HumainsTests.Empreinte(veille));
        var alertes = veille.PrendreAlertesSuivis();
        Assert.NotEmpty(alertes);
        Assert.All(alertes, a => Assert.True(a.HumainId != 0 || a.CommunauteId != 0));
        Assert.All(alertes.Where(a => ReglesAlertes.HumainSeul(a.Sorte)), a => Assert.Contains(a.HumainId, humains));
        // Chaque alerte a un texte rédigeable dans les deux langues.
        Assert.All(alertes, a => Assert.False(string.IsNullOrWhiteSpace(a.Texte.Texte)));
        Assert.Empty(veille.PrendreAlertesSuivis());
    }

    [Fact]
    public void SorteNonCochee_NePrevientPas()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(20 * Calendrier.SecondesParJour);
        var regles = SansPause();
        Array.Fill(regles.Prevenir, false);
        regles.Prevenir[(int)SorteAlerte.Maladie] = true;
        monde.DefinirVeille(monde.Etat.Humains!.Where(h => h.EstVivant).Select(h => h.Id), [], regles);
        monde.AvancerDe(200 * Calendrier.SecondesParJour);
        Assert.All(monde.PrendreAlertesSuivis(), a => Assert.Equal(SorteAlerte.Maladie, a.Sorte));
    }

    [Fact]
    public void AlerteQuiMetEnPause_ArreteLeMonde()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(20 * Calendrier.SecondesParJour);
        var regles = ReglesAlertes.ParDefaut();
        Array.Fill(regles.Pause, true);
        monde.DefinirVeille(monde.Etat.Humains!.Where(h => h.EstVivant).Select(h => h.Id), [], regles);
        var avant = monde.Secondes;
        monde.AvancerDe(400 * Calendrier.SecondesParJour);
        var alertes = monde.PrendreAlertesSuivis();
        Assert.NotEmpty(alertes);
        Assert.True(monde.Secondes < avant + 400 * Calendrier.SecondesParJour);
        Assert.Contains(alertes, a => a.Pause);
    }

    [Fact]
    public void ObjectifProchaineAlerte_EstAtteint()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(20 * Calendrier.SecondesParJour);
        monde.DefinirVeille(monde.Etat.Humains!.Where(h => h.EstVivant).Select(h => h.Id), [], SansPause());
        monde.FixerObjectif(Objectif.Evenement(TypeObjectif.AlerteSuivi, monde.Secondes));
        for (var i = 0; i < 2000 && monde.Objectif is not null; i++) monde.Avancer(TimeSpan.FromSeconds(1));
        Assert.Null(monde.Objectif);
        Assert.NotEmpty(monde.PrendreAlertesSuivis());
    }
}
