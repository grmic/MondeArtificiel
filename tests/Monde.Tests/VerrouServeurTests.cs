using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;

namespace Monde.Tests;

/// <summary>Un seul serveur par dossier de mondes (0.37.5) : un second refuse de démarrer, sans toucher au monde.</summary>
public sealed class VerrouServeurTests
{
    [Fact]
    public void SecondServeur_SurLeMemeDossier_RefuseDeDemarrer()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-verrou-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var premier = new UsineTest(dossier, supprimer: false))
            {
                _ = premier.Services.GetRequiredService<ServiceMonde>();
                using var second = new UsineTest(dossier, supprimer: false);
                var e = Assert.ThrowsAny<Exception>(() => second.Services.GetRequiredService<ServiceMonde>());
                Assert.Contains(L.T("serveur.dejaLance", ("dossier", dossier)), e.ToString());
            }
            // Le premier arrêté, le verrou tombe : un nouveau serveur démarre.
            using var apres = new UsineTest(dossier, supprimer: false);
            Assert.NotNull(apres.Services.GetRequiredService<ServiceMonde>().Monde);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
