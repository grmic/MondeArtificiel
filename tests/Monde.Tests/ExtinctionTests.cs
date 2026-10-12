using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Mort du dernier humain : tournant noté au journal, pause forcée, annonce unique, nature qui continue.</summary>
public sealed class ExtinctionTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    /// <summary>Un monde dont tous les humains sont à l'agonie, assoiffés : ils meurent au premier pas.</summary>
    private static MondeSimule Mourant()
    {
        var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
        foreach (var h in etat.Humains!)
        {
            h.Sante = -1;
            h.Soif = 100;
        }
        return new MondeSimule(Carte, etat);
    }

    [Fact]
    public void DernierMort_EvenementSignificatifCitantSaMort_PauseForcee_ObjectifAnnule_AnnonceUnique()
    {
        var monde = Mourant();
        monde.FixerObjectif(Objectif.Evenement(TypeObjectif.Naissance, monde.Secondes));
        monde.Avancer(TimeSpan.FromSeconds(1));

        Assert.True(monde.Eteint);
        Assert.Equal(0, monde.Facteur);
        Assert.Null(monde.Objectif);
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains, h => Assert.False(h.EstVivant));
        // La pause tombe au pas de la dernière mort, pas des jours plus tard.
        Assert.InRange(monde.Secondes - humains.Max(h => h.MortLe!.Value), 0, Calendrier.SecondesParHeure);

        var journal = monde.JournalRecent();
        var fin = Assert.Single(journal, e => e.Texte.Contains("l'humanité s'est éteinte"));
        var dernier = humains.OrderBy(h => h.MortLe!.Value).ThenBy(h => h.Id).Last();
        Assert.True(fin.Significatif);
        Assert.Equal(dernier.Id, fin.HumainId);
        Assert.Equal(dernier.MortEvenementId, fin.CauseId);
        Assert.Contains($"{humains.Count} humains auront vécu ici", fin.Texte);

        var annonce = monde.PrendreExtinction();
        Assert.Equal(fin.Texte, annonce);
        Assert.Null(monde.PrendreExtinction());

        // Relancé, le temps passe : la nature continue seule, sans nouvelle annonce ni nouvelle pause.
        monde.ChangerFacteur(10_000);
        var avant = monde.Secondes;
        monde.Avancer(TimeSpan.FromSeconds(1));
        Assert.True(monde.Secondes > avant);
        Assert.Equal(10_000, monde.Facteur);
        Assert.Null(monde.PrendreExtinction());
        Assert.Single(monde.JournalRecent(), e => e.Texte.Contains("l'humanité s'est éteinte"));
    }

    [Fact]
    public void MondeEteintRecharge_NAnnoncePasUneSecondeFois()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-eteint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = Mourant();
            monde.AvancerDe(Calendrier.SecondesParJour);
            Assert.True(monde.Eteint);
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(monde.Etat);

            var relu = new MondeSimule(Carte, depot.Charger()!);
            Assert.True(relu.Eteint);
            relu.ChangerFacteur(100_000);
            relu.Avancer(TimeSpan.FromSeconds(1));
            Assert.Null(relu.PrendreExtinction());
            Assert.Equal(100_000, relu.Facteur);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void MondeVivant_PasEteint()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(Calendrier.SecondesParJour);
        Assert.False(monde.Eteint);
        Assert.Null(monde.PrendreExtinction());
    }
}
