using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Suivis ★ et alertes gardés dans la base du monde (0.40.0).</summary>
public sealed class DepotSuivisTests
{
    [Fact]
    public void SuivisEtAlertes_SontGardes()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-suivis-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
            monde.AvancerDe(2 * Calendrier.SecondesParJour);
            var depot = new DepotMonde(Path.Combine(dossier, "m.db"));
            depot.Enregistrer(monde.Etat);
            var h = monde.Etat.Humains![0];

            depot.ChangerSuivisComplets([new Suivi(false, h.Id, h.Nom, true), new Suivi(true, 3, "Groupe", false)], []);
            depot.ChangerSuivisComplets([new Suivi(false, h.Id, h.Nom, false)], [(true, 3)]);
            var suivis = depot.LireSuivisComplets();
            Assert.Equal([new Suivi(false, h.Id, h.Nom, false)], suivis);
            Assert.Contains(h.Id, depot.LireSuivis());

            depot.EnregistrerAlertes(
            [
                new AlerteSuivi(100, SorteAlerte.Maladie, h.Id, 0, 7, new Recit("alerte.libre", ("h", h)), false),
                new AlerteSuivi(200, SorteAlerte.Disette, 0, 2, 0, new Recit("alerte.heritier", ("h", "A"), ("e", "B")), true),
            ]);
            Assert.Equal(2, depot.AlertesNonLues());
            var lues = depot.LireAlertes(10);
            Assert.Equal(2, lues.Count);
            Assert.Equal(SorteAlerte.Disette, lues[0].Sorte);
            Assert.Equal("alerte.heritier", lues[0].Texte!.Cle);
            Assert.Equal(h.Id, lues[1].HumainId);
            Assert.Equal(7, lues[1].EvenementId);
            Assert.Single(depot.LireAlertes(10, lues[0].Id));
            depot.MarquerAlertesLues();
            Assert.Equal(0, depot.AlertesNonLues());

            depot.ChangerSuivisComplets([], [(false, h.Id)]);
            Assert.Empty(depot.LireSuivisComplets());
            Assert.DoesNotContain(h.Id, depot.LireSuivis());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
