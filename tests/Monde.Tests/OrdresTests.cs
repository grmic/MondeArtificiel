using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.44.11 : transmettre une révélation sur l'ordre de l'Entité, et l'état de chaque ordre.</summary>
public sealed class OrdresTests
{
    private const long Jour = Calendrier.SecondesParJour;

    [Fact]
    public void TransmetsMaRevelation_IlLaRaconteAuxSiens_EtLOrdreFinit()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(Jour);
        var adultes = monde.InstantaneHumains().Humains.Where(h => h.EstVivant && !h.EstEnfant(monde.Secondes)).Take(30).ToList();
        foreach (var h in adultes)
        {
            monde.RevelerEnDialogue(h.Id, "Le son sacré est peace and love.", ModeParole.Apparition, croit: true, cause: 0);
            monde.ParlerEnDialogue(h.Id, Parole.Transmets, ModeParole.Apparition, 0, 0, 0, true);
        }
        var debut = monde.Secondes;
        var acceptes = monde.InstantaneHumains().Humains.Where(h => h.Impulsions.Exists(i => i.Parole == Parole.Transmets
            && i.Reponse is Reponse.Acceptee or Reponse.AMoitie or Reponse.Differee)).Select(h => h.Id).ToList();
        Assert.NotEmpty(acceptes);
        monde.AvancerDe(20 * Jour);
        var (maintenant, humains) = monde.InstantaneHumains();
        var racontee = 0;
        foreach (var id in acceptes)
        {
            var i = humains[id - 1].Impulsions.Last(x => x.Parole == Parole.Transmets);
            if (!humains[id - 1].EstVivant) continue;
            // Au bout de vingt jours, l'ordre est fini : fait, fait en partie, ou pas fait (refusé après hésitation, abandonné).
            Assert.True(i.AgirLe == long.MaxValue || i.Reponse is Reponse.Refusee or Reponse.Abandonnee || i.Jusqua < maintenant, $"{i}");
            racontee += i.Param;
            Assert.DoesNotContain("plus suivi", Ordres.Etat(i, maintenant));
        }
        Assert.True(racontee > 0);
        Assert.True(monde.Secondes > debut);
    }

    [Fact]
    public void EtatDUnOrdre_EnMots()
    {
        const long t = 100 * Jour;
        var recu = new Impulsion(Parole.Prie, ModeParole.Voix, 0, 0, 1, t, t + Jour, t, Reponse.Acceptee, 1);
        Assert.Equal("en cours", Ordres.Etat(recu with { Param = 30, Jusqua = t + 3600 }, t + 60));
        Assert.StartsWith("fait", Ordres.Etat(recu with { Param = 30, Jusqua = t + 3600 }, t + Jour));
        Assert.Equal("refusé", Ordres.Etat(recu with { Reponse = Reponse.Refusee }, t + Jour));
        Assert.StartsWith("remis à plus tard", Ordres.Etat(recu with { Reponse = Reponse.Differee, AgirLe = t + Jour }, t + 60));
        Assert.Equal("pas fait : le temps est passé", Ordres.Etat(recu with { Parole = Parole.VaVers }, t + 2 * Jour));
        Assert.Equal("plus suivi (trop ancien)", Ordres.Etat(null, t));
    }
}
