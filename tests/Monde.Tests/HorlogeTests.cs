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
}
