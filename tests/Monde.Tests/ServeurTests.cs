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
}
