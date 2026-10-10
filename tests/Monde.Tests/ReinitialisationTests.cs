using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Stockage;

namespace Monde.Tests;

public sealed class ReinitialisationTests
{
    private sealed record CarteLue(uint Graine);

    [Fact]
    public async Task Reinitialiser_RecommenceALAn1_AvecLaGraineChoisie_EtGardeUneCopie()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        var annonce = new TaskCompletionSource<uint>();
        connexion.On<uint>("MondeReinitialise", g => annonce.TrySetResult(g));
        await connexion.StartAsync();

        // Le monde a vécu un peu (accéléré), puis on le réinitialise avec la graine 7.
        await connexion.InvokeAsync<HorlogeDto>("ChoisirVitesse", 5);
        await Task.Delay(500);
        await connexion.InvokeAsync<HorlogeDto>("ChoisirVitesse", 0);
        var avant = await connexion.InvokeAsync<HorlogeDto>("Horloge");
        Assert.True(avant.Secondes > 6 * 3600);

        var horloge = await connexion.InvokeAsync<HorlogeDto>("Reinitialiser", 7L);

        Assert.Equal("An 1, jour 1 du printemps, 06:00", horloge.Texte);
        Assert.Equal(0, horloge.NiveauVitesse);
        Assert.Equal(7u, await annonce.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        using var client = usine.CreateClient();
        Assert.Equal(7u, (await client.GetFromJsonAsync<CarteLue>("/api/carte"))!.Graine);
        Assert.Equal(66, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);

        // Le fichier contient le nouveau monde ; l'ancien est copié à côté.
        var dossier = Path.Combine(usine.Dossier, "mondes");
        var enregistre = new DepotMonde(Path.Combine(dossier, "monde.db")).Charger()!;
        Assert.Equal(7UL, enregistre.Graine);
        Assert.Equal(66, enregistre.Humains!.Count);
        var copie = new DepotMonde(Path.Combine(dossier, "monde-avant-reinitialisation.db")).Charger()!;
        Assert.Equal(42UL, copie.Graine);
        Assert.Equal(avant.Secondes, copie.Secondes);
    }

    [Fact]
    public async Task Reinitialiser_SansGraine_EnTireUneAuHasard()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        await connexion.InvokeAsync<HorlogeDto>("Reinitialiser", (long?)null);

        Assert.Equal(66, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(4_294_967_296L)]
    public async Task Reinitialiser_RefuseUneGraineHorsLimites(long graine)
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var erreur = await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<HorlogeDto>("Reinitialiser", graine));
        Assert.Contains("entre 0 et 4294967295", erreur.Message);
        using var client = usine.CreateClient();
        Assert.Equal(42u, (await client.GetFromJsonAsync<CarteLue>("/api/carte"))!.Graine); // monde inchangé
    }
}
