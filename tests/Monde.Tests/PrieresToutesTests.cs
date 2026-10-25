using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.31.4 : exaucer ou refuser d'un coup toutes les prières en attente.</summary>
public sealed class PrieresToutesTests
{
    private static bool EnAttente(Priere p) => p.Issue == IssuePriere.EnAttente && p.Refus == RefusPriere.Aucun && p.Sujet != SujetPriere.Remerciement;

    /// <summary>Des croyants affamés, jusqu'à ce qu'au moins trois prières attendent une réponse.</summary>
    private static MondeSimule Croyants()
    {
        var conf = new ConfigurationMonde(5, 20, 64, 64, 1);
        var etat = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)).Etat;
        foreach (var h in etat.Humains!)
        {
            h.Croyance = 90;
            h.Faim = 95;
        }
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), etat);
        for (var j = 0; j < 60 && monde.Prieres().Count(EnAttente) < 3; j++) monde.AvancerDe(Calendrier.SecondesParJour);
        Assert.True(monde.Prieres().Count(EnAttente) >= 3);
        return monde;
    }

    [Fact]
    public void ExaucerToutes_RepondATouteLaFile()
    {
        var monde = Croyants();
        var avant = monde.Prieres().Where(EnAttente).Select(p => p.Id).ToList();
        var bilan = monde.ExaucerToutes();

        Assert.Equal("pri.r.toutesExaucees", bilan.Cle);
        var apres = monde.Prieres().Where(p => avant.Contains(p.Id)).ToList();
        // Chacune est exaucée par l'Entité, sauf celles devenues impossibles en chemin (toujours en attente).
        Assert.Contains(apres, p => p.Issue == IssuePriere.ExauceeParEntite);
        Assert.StartsWith($"Prières exaucées : {apres.Count(p => p.Issue == IssuePriere.ExauceeParEntite)}.", bilan.Texte);
        // Plus rien à exaucer ensuite.
        Assert.Equal("pri.r.aucuneEnAttente", monde.ExaucerToutes().Cle);
    }

    [Fact]
    public void RefuserToutes_EnSecretOuParUnSigne()
    {
        var monde = Croyants();
        var avant = monde.Prieres().Where(EnAttente).Select(p => p.Id).ToList();
        var bilan = monde.RefuserToutes(signe: false);

        Assert.Equal($"Prières refusées en secret : {avant.Count}. Ceux qui prient attendront, puis concluront que rien n'est venu.", bilan.Texte);
        Assert.All(monde.Prieres().Where(p => avant.Contains(p.Id)), p => Assert.Equal((IssuePriere.EnAttente, RefusPriere.Cache), (p.Issue, p.Refus)));
        // Déjà refusées en secret : elles n'attendent plus de réponse de l'Entité.
        Assert.Equal("pri.r.aucuneEnAttente", monde.RefuserToutes(signe: true).Cle);

        var autre = Croyants();
        var ids = autre.Prieres().Where(EnAttente).Select(p => p.Id).ToList();
        Assert.Equal("pri.r.toutesRefuseesSigne", autre.RefuserToutes(signe: true).Cle);
        Assert.All(autre.Prieres().Where(p => ids.Contains(p.Id)), p => Assert.Equal((IssuePriere.Refusee, RefusPriere.Signe), (p.Issue, p.Refus)));
    }
}
