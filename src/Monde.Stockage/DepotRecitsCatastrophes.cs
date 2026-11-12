using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Les récits nés des catastrophes (0.39.0) : ce que chaque communauté se raconte d'un malheur, et qui y croit.</summary>
public sealed partial class DepotMonde
{
    private static readonly JsonSerializerOptions OptionsRecits = new() { IncludeFields = false };

    /// <summary>Migration vers la version 59 : récits de catastrophe. Les mondes existants n'en ont aucun ; ils naîtront des prochains malheurs.</summary>
    private static void MigrerVersion59(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS recits_catastrophes (
                id          INTEGER PRIMARY KEY,
                communaute  INTEGER NOT NULL,
                donnees     TEXT    NOT NULL
            );
            """);
    }

    private static List<RecitCatastrophe> LireRecitsCatastrophes(SqliteCommand commande)
    {
        var liste = new List<RecitCatastrophe>();
        commande.CommandText = "SELECT donnees FROM recits_catastrophes ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
            if (JsonSerializer.Deserialize<RecitCatastrophe>(l.GetString(0), OptionsRecits) is { } r) liste.Add(r);
        return liste;
    }

    private static void EnregistrerRecitsCatastrophes(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<RecitCatastrophe> recits)
    {
        Executer(connexion, transaction, "DELETE FROM recits_catastrophes;");
        if (recits.Count == 0) return;
        using var c = Preparer(connexion, transaction, "INSERT INTO recits_catastrophes (id, communaute, donnees) VALUES ($i, $c, $d);", "$i", "$c", "$d");
        foreach (var r in recits) Lancer(c, r.Id, r.CommunauteId, JsonSerializer.Serialize(r, OptionsRecits));
    }
}
