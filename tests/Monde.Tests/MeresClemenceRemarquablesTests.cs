using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.23 et 0.24 : nourrir les mères, clémence du monde, humains remarquables.</summary>
public sealed class MeresClemenceRemarquablesTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 30 * An;

    /// <summary>Une mère enceinte (id 1) et, à côté d'elle, des proches de plus en plus lointains.</summary>
    private static (Humain Mere, Humain Compagnon, Humain SaMere, Humain Voisine, Humain Inconnu) Famille()
    {
        var grandMere = VitesseTests.Humain(3);
        var mere = VitesseTests.Humain(1, mere: 3);
        var compagnon = VitesseTests.Humain(2);
        var voisine = VitesseTests.Humain(5);
        var inconnu = VitesseTests.Humain(7);
        (mere.PartenaireId, compagnon.PartenaireId) = (2, 1);
        mere.EnceinteDepuis = Maintenant - 30 * Calendrier.SecondesParJour;
        foreach (var h in new[] { mere, compagnon, grandMere, voisine, inconnu })
        {
            (h.X, h.Y) = (40, 40);
            h.Faim = 20;
        }
        mere.Faim = 45;
        mere.CommunauteId = voisine.CommunauteId = 4;
        return (mere, compagnon, grandMere, voisine, inconnu);
    }

    [Fact]
    public void LesProchesNourrissentLaMere_LaFamilleDAbordEtPasLesInconnus()
    {
        var (mere, compagnon, saMere, voisine, inconnu) = Famille();
        var deCompagnon = Comportement.EnvieDeNourrir(compagnon, mere, Maintenant);
        var deSaMere = Comportement.EnvieDeNourrir(saMere, mere, Maintenant);
        var deVoisine = Comportement.EnvieDeNourrir(voisine, mere, Maintenant);

        Assert.True(deSaMere > deVoisine && deCompagnon > deVoisine && deVoisine > 0);
        Assert.Equal(0, Comportement.EnvieDeNourrir(inconnu, mere, Maintenant));

        // Loin d'elle, ou ayant lui-même faim, on ne donne rien.
        compagnon.X += 3;
        Assert.Equal(0, Comportement.EnvieDeNourrir(compagnon, mere, Maintenant));
        saMere.Faim = 50;
        Assert.Equal(0, Comportement.EnvieDeNourrir(saMere, mere, Maintenant));
    }

    [Fact]
    public void ClemenceTotale_PlusDeMortDeMaladieNiEnCouchesNiDEnfantMortNe()
    {
        var conf = new ConfigurationMonde(3, 33, 96, 96, 1);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Clemence = 100 };
        monde.AvancerDe(3 * An);
        var e = monde.Etat;

        Assert.DoesNotContain(e.Humains!, h => h.CauseDeMort is CauseDeMort.Maladie or CauseDeMort.Accouchement);
        // Une plante toxique goûtée reste un empoisonnement : seules les maladies sont écartées.
        Assert.Empty(e.JournalNonEnregistre!.Where(j => j.Type == TypeEvenement.MortNe || j.Type == TypeEvenement.Maladie && j.Texte.Contains("tombe malade"))
            .Select(j => j.Texte).Take(5));
    }

    [Fact]
    public void ChangerLaClemence_EstNoteCommeUnActeDeLEntite()
    {
        var conf = new ConfigurationMonde(1, 4, 64, 64, 1);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf));
        monde.ChoisirClemence(30);
        monde.ChoisirClemence(30);
        monde.ChoisirClemence(0);

        Assert.Equal(0, monde.Clemence);
        var notes = monde.Etat.JournalNonEnregistre!.Where(j => j.Origine == Origine.Entite).ToList();
        Assert.Equal(2, notes.Count);
        Assert.Contains("30 %", notes[0].Texte);
        Assert.Contains("cesse", notes[1].Texte);
        Assert.All(notes, n => Assert.True(n.Significatif));
        Assert.Throws<ArgumentOutOfRangeException>(() => monde.Clemence = 101);
    }

    [Fact]
    public async Task ClemenceEtRemarquables_ParLeServeur()
    {
        using var usine = new UsineTest();
        await using var connexion = usine.CreerConnexion();
        await connexion.StartAsync();

        var c = await connexion.InvokeAsync<ConfigurationDto>("ChoisirClemence", 40);
        Assert.Equal(40, c.Clemence);
        Assert.Equal(40, usine.Services.GetRequiredService<ServiceMonde>().Monde.Clemence);
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<ConfigurationDto>("ChoisirClemence", -1));

        var criteres = await connexion.InvokeAsync<List<CritereRemarquableDto>>("CriteresRemarquables");
        Assert.Contains(criteres, k => k.Cle == "qi" && k.Bas);
        Assert.Contains(criteres, k => k.Cle == "descendance" && !k.Bas);

        var (_, humains) = usine.Services.GetRequiredService<ServiceMonde>().Monde.InstantaneHumains();
        var hauts = await connexion.InvokeAsync<List<RemarquableDto>>("Remarquables", "qi", false, false);
        var bas = await connexion.InvokeAsync<List<RemarquableDto>>("Remarquables", "qi", true, false);
        Assert.Equal(Remarquables.Nombre, hauts.Count);
        Assert.Equal(humains.Where(h => h.EstVivant).MaxBy(h => h.Personnalite.Intelligence.Generale)!.Id, hauts[0].Id);
        Assert.Equal(humains.Where(h => h.EstVivant).MinBy(h => h.Personnalite.Intelligence.Generale)!.Id, bas[0].Id);
        Assert.Equal(Enumerable.Range(1, Remarquables.Nombre), hauts.Select(r => r.Rang));

        // Au départ, personne n'a d'enfant ni de victime : un compte nul n'est pas remarquable. Clé inconnue : rien.
        Assert.Empty(await connexion.InvokeAsync<List<RemarquableDto>>("Remarquables", "victimes", false, true));
        Assert.Empty(await connexion.InvokeAsync<List<RemarquableDto>>("Remarquables", "<script>", false, true));
    }
}
