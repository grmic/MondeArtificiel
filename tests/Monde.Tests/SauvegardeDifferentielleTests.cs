using System.Text;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>La sauvegarde différentielle (0.40.0) écrit exactement ce qu'écrirait une sauvegarde complète.</summary>
public sealed class SauvegardeDifferentielleTests
{
    private static readonly string[] Tables =
    [
        "humains", "memoire_lieux", "liens", "pensees", "jardin", "griefs", "raisons", "exclusions",
        "langage_humains", "mots", "savoirs", "objets", "impulsions",
    ];

    /// <summary>Contenu des tables des humains, ligne par ligne, dans un ordre fixe.</summary>
    private static string Contenu(string fichier)
    {
        var texte = new StringBuilder();
        using var connexion = new SqliteConnection($"Data Source={fichier};Pooling=False");
        connexion.Open();
        foreach (var table in Tables)
        {
            using var commande = connexion.CreateCommand();
            commande.CommandText = $"SELECT * FROM {table} ORDER BY 1, 2;";
            using var lecteur = commande.ExecuteReader();
            texte.Append('#').Append(table).Append('\n');
            while (lecteur.Read())
            {
                for (var i = 0; i < lecteur.FieldCount; i++)
                {
                    texte.Append(lecteur.IsDBNull(i) ? "∅" : Convert.ToString(lecteur.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                }
                texte.Append('\n');
            }
        }
        return texte.ToString();
    }

    [Fact]
    public void SauvegardesSuccessives_CommeUneSauvegardeComplete()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-diff-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
            var differentiel = new DepotMonde(Path.Combine(dossier, "diff.db"));
            var reecrits = new List<int>();
            for (var i = 0; i < 6; i++)
            {
                monde.AvancerDe((i == 3 ? 40 : 3) * Calendrier.SecondesParJour);
                var etat = monde.Etat;
                differentiel.Enregistrer(etat);
                monde.MarquerJournalEnregistre(etat.JournalNonEnregistre?.Count ?? 0, etat.ArchivesNonEnregistrees?.Count ?? 0,
                    null, etat.PremieresNonEnregistrees?.Count ?? 0);
                reecrits.Add(differentiel.HumainsReecrits);
            }
            // Même base, réécrite en entier par un dépôt neuf (qui ne sait rien de ce qu'elle contient).
            SqliteConnection.ClearAllPools();
            File.Copy(Path.Combine(dossier, "diff.db"), Path.Combine(dossier, "complet.db"));
            new DepotMonde(Path.Combine(dossier, "complet.db")).Enregistrer(monde.Etat);
            SqliteConnection.ClearAllPools();

            Assert.Equal(Contenu(Path.Combine(dossier, "complet.db")), Contenu(Path.Combine(dossier, "diff.db")));
            // Les morts ne sont plus réécrits : après la première, chaque sauvegarde réécrit au plus les vivants.
            Assert.All(reecrits.Skip(1), n => Assert.True(n <= monde.Etat.Humains!.Count(h => h.EstVivant) + 5));

            // Et le monde repris de la sauvegarde différentielle continue comme l'original.
            var repris = new MondeSimule(MondeSimuleTests.Carte42, new DepotMonde(Path.Combine(dossier, "diff.db")).Charger()!);
            monde.AvancerDe(5 * Calendrier.SecondesParJour);
            repris.AvancerDe(5 * Calendrier.SecondesParJour);
            Assert.Equal(HumainsTests.Empreinte(monde), HumainsTests.Empreinte(repris));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
