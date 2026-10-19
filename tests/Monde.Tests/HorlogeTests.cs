using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur.Hubs;

namespace Monde.Tests;

public sealed class HorlogeTests
{
    [Fact]
    public async Task ServeurDemarre_EnPause_ALAube()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var horloge = await connexion.InvokeAsync<HorlogeDto>("Horloge");

        Assert.Equal(0, horloge.NiveauVitesse);
        Assert.Equal("An 1, jour 1 du printemps, 06:00", horloge.Texte);
    }

    [Fact]
    public async Task VitesseInconnue_EstRefuseeAvecUnMessageClair()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var erreur = await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<HorlogeDto>("ChoisirVitesse", 42));
        Assert.Contains("Vitesse inconnue.", erreur.Message);
    }

    [Fact]
    public async Task Redemarrage_ReprendALaDateSauvegardee_EnPause()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-redemarrage-" + Guid.NewGuid().ToString("N"));
        try
        {
            long secondesAvantArret;
            using (var usine = new UsineTest(dossier, supprimer: false))
            {
                var connexion = usine.CreerConnexion();
                await using var _ = connexion;
                await connexion.StartAsync();
                var recues = 0;
                connexion.On<HorlogeDto>("Horloge", _ => Interlocked.Increment(ref recues));

                await connexion.InvokeAsync<HorlogeDto>("ChoisirVitesse", 5);
                await Task.Delay(600);
                var horloge = await connexion.InvokeAsync<HorlogeDto>("ChoisirVitesse", 0);
                secondesAvantArret = horloge.Secondes;

                Assert.True(secondesAvantArret > 6 * 3600, "le temps aurait dû avancer en accéléré");
                Assert.True(recues > 0, "le serveur aurait dû diffuser l'horloge");
            }

            using (var usine = new UsineTest(dossier, supprimer: true))
            {
                var connexion = usine.CreerConnexion();
                await using var _ = connexion;
                await connexion.StartAsync();

                var horloge = await connexion.InvokeAsync<HorlogeDto>("Horloge");

                Assert.Equal(secondesAvantArret, horloge.Secondes);
                Assert.Equal(0, horloge.NiveauVitesse);
            }
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task PasAPas_MetEnPauseEtAvanceDUnJour()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();
        var avant = await connexion.InvokeAsync<HorlogeDto>("Horloge");

        var apres = await connexion.InvokeAsync<HorlogeDto>("AvancerDe", "jour");
        var plus10 = await connexion.InvokeAsync<HorlogeDto>("AvancerDe", "pas");

        Assert.Equal(avant.Secondes + 86_400, apres.Secondes);
        Assert.Equal(apres.Secondes + 600, plus10.Secondes);
        Assert.Equal(0, plus10.Facteur);
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<HorlogeDto>("AvancerDe", "siecle"));
    }

    [Fact]
    public async Task VitesseLibre_EstAppliqueeEtMesuree()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var horloge = await connexion.InvokeAsync<HorlogeDto>("ChoisirFacteur", 5_000.0);
        Assert.Equal(5_000, horloge.Facteur);
        Assert.Equal(-1, horloge.NiveauVitesse);
        await Task.Delay(1_600);
        var mesuree = await connexion.InvokeAsync<HorlogeDto>("Horloge");
        Assert.InRange(mesuree.FacteurAtteint, 3_000, 7_000);

        var erreur = await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<HorlogeDto>("ChoisirFacteur", -3.0));
        Assert.Contains("La vitesse doit valoir 0", erreur.Message);
    }

    [Fact]
    public async Task AllerJusqua_LaProchaineSaison_PuisPauseEtAnnonce()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        var annonce = new TaskCompletionSource<string>();
        connexion.On<string>("ObjectifAtteint", t => annonce.TrySetResult(t));
        await connexion.StartAsync();
        // Une catastrophe en route mettrait la course en pause : ce n'est pas ce qu'on vérifie ici.
        await connexion.InvokeAsync<HorlogeDto>("ChoisirPauseCatastrophe", false);

        var enRoute = await connexion.InvokeAsync<HorlogeDto>("AllerJusqua", new DemandeObjectif("saison"));
        Assert.Equal("An 1, jour 1 de l'été, 00:00", enRoute.Objectif);

        Assert.Equal("An 1, jour 1 de l'été, 00:00", await annonce.Task.WaitAsync(TimeSpan.FromSeconds(60)));
        var horloge = await connexion.InvokeAsync<HorlogeDto>("Horloge");
        Assert.Equal(90 * 86_400, horloge.Secondes);
        Assert.Equal(0, horloge.Facteur);
        Assert.Null(horloge.Objectif);
    }

    [Theory]
    [InlineData("date", 1, 1, 1, 0)]     // déjà passée
    [InlineData("date", 2, 5, 1, 0)]     // saison inexistante
    [InlineData("date", 5000, 1, 1, 0)]  // trop lointaine
    [InlineData("demain", 2, 1, 1, 0)]   // type inconnu
    public async Task AllerJusqua_RefuseUneDemandeInvalide(string type, long annee, int saison, int jour, int heure)
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            connexion.InvokeAsync<HorlogeDto>("AllerJusqua", new DemandeObjectif(type, annee, saison, jour, heure)));
        Assert.Null((await connexion.InvokeAsync<HorlogeDto>("Horloge")).Objectif);
    }

    [Fact]
    public async Task Redemarrage_PeutReprendreALaDerniereVitesse()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-reprise-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var usine = new UsineTest(dossier, supprimer: false))
            {
                var connexion = usine.CreerConnexion();
                await using var _ = connexion;
                await connexion.StartAsync();
                var horloge = await connexion.InvokeAsync<HorlogeDto>("ChoisirRedemarrage", true);
                Assert.True(horloge.ReprendreVitesse);
                await connexion.InvokeAsync<HorlogeDto>("ChoisirFacteur", 250.0);
            }

            using (var usine = new UsineTest(dossier, supprimer: true))
            {
                var connexion = usine.CreerConnexion();
                await using var _ = connexion;
                await connexion.StartAsync();

                var horloge = await connexion.InvokeAsync<HorlogeDto>("Horloge");
                Assert.True(horloge.ReprendreVitesse);
                Assert.Equal(250, horloge.Facteur);
            }
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
