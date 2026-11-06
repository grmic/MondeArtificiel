using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Les paroles de l'Entité au Monde entier ou à une terre (0.37.1), et ce que chaque communauté en a fait.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 57 : paroles au Monde. Les mondes existants n'en ont reçu aucune.</summary>
    private static void MigrerVersion57(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS paroles_monde (
                id          INTEGER PRIMARY KEY,
                secondes    INTEGER NOT NULL,
                parole      INTEGER NOT NULL,
                terre       INTEGER NOT NULL,
                evenement   INTEGER NOT NULL,
                donnees     TEXT NOT NULL
            );
            """);
    }

    private static List<ParoleMonde> LireParolesMonde(SqliteCommand commande)
    {
        var liste = new List<ParoleMonde>();
        commande.CommandText = "SELECT donnees FROM paroles_monde ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            try
            {
                if (JsonSerializer.Deserialize<ParoleMonde>(l.GetString(0)) is { } p && Enum.IsDefined(p.Parole) && Paroles.AuMonde(p.Parole))
                    liste.Add(p with { Bilans = p.Bilans ?? [] });
            }
            catch (JsonException)
            {
                // Une parole illisible est oubliée.
            }
        }
        return liste.Where((p, i) => p.Id == i + 1).ToList();
    }

    private static void EnregistrerParolesMonde(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<ParoleMonde> paroles)
    {
        Executer(connexion, transaction, "DELETE FROM paroles_monde;");
        using var c = Preparer(connexion, transaction, """
            INSERT INTO paroles_monde (id, secondes, parole, terre, evenement, donnees) VALUES ($i, $s, $p, $t, $e, $d);
            """, "$i", "$s", "$p", "$t", "$e", "$d");
        foreach (var p in paroles)
            Lancer(c, p.Id, p.Le, (int)p.Parole, p.Terre, p.EvenementId, JsonSerializer.Serialize(p));
    }
}
