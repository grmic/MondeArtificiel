using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur.Hubs;

namespace Monde.Tests;

public sealed class ServeurTests(WebApplicationFactory<Program> usine) : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed record Etat(string Nom, string VersionMoteur, DateTimeOffset HeureServeur);

    [Fact]
    public async Task ApiEtat_RenvoieLeNomDuMonde()
    {
        using var client = usine.CreateClient();

        var etat = await client.GetFromJsonAsync<Etat>("/api/etat");

        Assert.NotNull(etat);
        Assert.Equal("Monde artificiel", etat.Nom);
    }

    [Fact]
    public async Task PageAccueil_EstServieAvecEnTetesDeSecurite()
    {
        using var client = usine.CreateClient();

        var reponse = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("nosniff", reponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", reponse.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task RouteInconnue_Renvoie404()
    {
        using var client = usine.CreateClient();

        var reponse = await client.GetAsync("/../../etc/passwd");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task HubSignalR_RepondAuPing()
    {
        var connexion = new HubConnectionBuilder()
            .WithUrl(new Uri(usine.Server.BaseAddress, MondeHub.Route), options =>
            {
                options.HttpMessageHandlerFactory = _ => usine.Server.CreateHandler();
            })
            .Build();
        await using var _ = connexion;

        await connexion.StartAsync();
        var reponse = await connexion.InvokeAsync<ReponsePing>("Ping");

        Assert.Equal("Monde artificiel", reponse.Monde);
        Assert.True(reponse.HeureServeur > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private sealed record EmplacementLu(int Numero, int X, int Y);

    private sealed record CarteLue(uint Graine, int Largeur, int Hauteur, string[] NomsBiomes, byte[] Biomes, EmplacementLu[] Emplacements);

    [Fact]
    public async Task ApiCarte_RenvoieLaCarteParDefaut()
    {
        using var client = usine.CreateClient();

        var carte = await client.GetFromJsonAsync<CarteLue>("/api/carte");

        Assert.NotNull(carte);
        Assert.Equal(42u, carte.Graine);
        Assert.Equal(carte.Largeur * carte.Hauteur, carte.Biomes.Length);
        Assert.Equal(33, carte.Emplacements.Length);
        Assert.Equal(12, carte.NomsBiomes.Length);
    }

    [Fact]
    public async Task ApiCarte_AccepteUneAutreGraine()
    {
        using var client = usine.CreateClient();

        var carte = await client.GetFromJsonAsync<CarteLue>("/api/carte?graine=7");

        Assert.Equal(7u, carte!.Graine);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("99999999999")]
    [InlineData("1%27%20OR%201=1")]
    public async Task ApiCarte_RefuseUneGraineInvalide(string graine)
    {
        using var client = usine.CreateClient();

        var reponse = await client.GetAsync($"/api/carte?graine={graine}");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }
}
