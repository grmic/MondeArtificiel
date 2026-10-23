using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;

namespace Monde.Tests;

/// <summary>0.29.3 : une sauvegarde supprimée part à la corbeille du système, d'où l'on peut la reprendre.</summary>
public sealed class CorbeilleTests
{
    [Fact]
    public async Task Hub_MettreALaCorbeille_RetireDeLaListe_EtLaCorbeilleLaGarde()
    {
        // Sous macOS et Windows, le test mettrait un fichier dans la vraie corbeille : il ne vérifie que sous Linux,
        // avec une corbeille freedesktop à lui.
        if (!OperatingSystem.IsLinux()) return;
        using var usine = new UsineTest();
        var donnees = Path.Combine(usine.Dossier, "xdg");
        var avant = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", donnees);
        try
        {
            var connexion = usine.CreerConnexion();
            await using var _ = connexion;
            await connexion.StartAsync();

            var sauvegarde = Assert.Single(await connexion.InvokeAsync<List<SauvegardeDto>>("Sauvegarder"));
            var chemin = Path.Combine(usine.Dossier, "mondes", "sauvegardes", sauvegarde.Nom);
            Assert.True(File.Exists(chemin));

            // Seuls les noms de la liste sont acceptés ; le monde en cours n'y est pas.
            foreach (var nom in new[] { "../monde.db", "monde.db", "/etc/passwd", "", "inconnue.db" })
            {
                await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<SauvegardeDto>>("MettreALaCorbeille", nom));
            }

            var liste = await connexion.InvokeAsync<List<SauvegardeDto>>("MettreALaCorbeille", sauvegarde.Nom);
            Assert.Empty(liste);
            Assert.False(File.Exists(chemin));
            var dansLaCorbeille = Path.Combine(donnees, "Trash", "files", sauvegarde.Nom);
            Assert.True(File.Exists(dansLaCorbeille));
            var info = File.ReadAllText(Path.Combine(donnees, "Trash", "info", sauvegarde.Nom + ".trashinfo"));
            Assert.StartsWith("[Trash Info]\nPath=/", info);
            Assert.EndsWith("/sauvegardes/" + sauvegarde.Nom, info.Split('\n')[1]);
            Assert.Matches("DeletionDate=[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}", info);

            // Une seconde sauvegarde du même nom ne remplace pas la première dans la corbeille.
            File.Copy(dansLaCorbeille, chemin);
            await connexion.InvokeAsync<List<SauvegardeDto>>("MettreALaCorbeille", sauvegarde.Nom);
            Assert.True(File.Exists(dansLaCorbeille));
            Assert.True(File.Exists(Path.Combine(donnees, "Trash", "files", Path.GetFileNameWithoutExtension(sauvegarde.Nom) + " 2.db")));

            // Le monde en cours continue.
            Assert.Equal(66, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", avant);
        }
    }
}
