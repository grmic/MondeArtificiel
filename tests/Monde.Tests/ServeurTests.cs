using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur.Hubs;

namespace Monde.Tests;

public sealed class ServeurTests(UsineTest usine) : IClassFixture<UsineTest>
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
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;

        await connexion.StartAsync();
        var reponse = await connexion.InvokeAsync<ReponsePing>("Ping");

        Assert.Equal("Monde artificiel", reponse.Monde);
        Assert.True(reponse.HeureServeur > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private sealed record JournalLu(bool JournalQuotidien);

    [Fact]
    public async Task HubSignalR_JournalQuotidien_NonParDefaut_EtSeChoisit()
    {
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        Assert.False((await connexion.InvokeAsync<JournalLu>("Configuration")).JournalQuotidien);
        Assert.True((await connexion.InvokeAsync<JournalLu>("ChoisirJournalQuotidien", true)).JournalQuotidien);
        Assert.True((await connexion.InvokeAsync<JournalLu>("Configuration")).JournalQuotidien);
        Assert.False((await connexion.InvokeAsync<JournalLu>("ChoisirJournalQuotidien", false)).JournalQuotidien);
    }

    private sealed record ResultatLu(int Humain, string Nom, string Resultat);

    private sealed record TrouveLu(int Id, string Nom);

    [Fact]
    public async Task HubSignalR_ParolesDeLEntite_RefusentLesDemandesInvalides()
    {
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var trouves = await connexion.InvokeAsync<List<TrouveLu>>("ChercherHumains",
            new { texte = new string('a', 500) + "'; DROP TABLE humains; --", communaute = 0, seuls = false, adultes = false, sexe = (string?)null, pres = 0 });
        Assert.Empty(trouves);
        var tous = await connexion.InvokeAsync<List<TrouveLu>>("ChercherHumains", new { texte = "", communaute = 0, seuls = false, adultes = true, sexe = "femme", pres = 1 });
        Assert.InRange(tous.Count, 1, 40);

        foreach (var demande in new object[]
                 {
                     new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "DROP", mode = "Voix", cible = 0, param = 0 },
                     new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "Penchant", mode = "Voix", cible = 0, param = 0 },
                     new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "NAiePasPeur", mode = "99", cible = 0, param = 0 },
                     new { humains = Array.Empty<int>(), communautes = Array.Empty<int>(), parole = "NAiePasPeur", mode = "Voix", cible = 0, param = 0 },
                     new { humains = Enumerable.Range(1, 600).ToArray(), communautes = Array.Empty<int>(), parole = "NAiePasPeur", mode = "Voix", cible = 0, param = 0 },
                     new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "PartsVers", mode = "Voix", cible = 0, param = 99 },
                     new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "Tue", mode = "Voix", cible = 0, param = 0 },
                     new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "Tue", mode = "Voix", cible = 100000, param = 0 },
                 })
        {
            await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<ResultatLu>>("Parler", demande));
        }
        var r = await connexion.InvokeAsync<List<ResultatLu>>("Parler",
            new { humains = new[] { 1 }, communautes = Array.Empty<int>(), parole = "NAiePasPeur", mode = "Voix", cible = 0, param = 0 });
        Assert.Single(r);
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<ResultatLu>>("Inspirer",
            new { humains = new[] { 1 }, communautes = Array.Empty<int>(), cible = -5, mutuel = false, passion = false, orientation = false }));
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
