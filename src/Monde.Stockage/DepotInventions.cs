using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;

namespace Monde.Stockage;

/// <summary>Les inventions de l'Entité libre (0.35.3) : plantes et bêtes que le monde n'avait jamais connues.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 53 : inventions. Les mondes existants n'en ont aucune.</summary>
    private static void MigrerVersion53(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS inventions (
                id          INTEGER PRIMARY KEY,
                sorte       TEXT    NOT NULL,
                numero      INTEGER NOT NULL,
                biome       INTEGER NOT NULL,
                forme       INTEGER NOT NULL,
                nom         TEXT    NOT NULL,
                proprietes  TEXT    NOT NULL,
                aliment     INTEGER NOT NULL,
                le          INTEGER NOT NULL,
                evenement   INTEGER NOT NULL,
                disparue    INTEGER NOT NULL
            );
            """);
    }

    private static List<Invention> LireInventions(SqliteCommand commande)
    {
        var liste = new List<Invention>();
        commande.CommandText = "SELECT id, sorte, numero, biome, forme, nom, proprietes, aliment, le, evenement, disparue FROM inventions ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var sorte = l.GetString(1);
            if (sorte is not ("plante" or "bete")) continue;
            var proprietes = JsonSerializer.Deserialize<Dictionary<string, double>>(l.GetString(6)) ?? [];
            liste.Add(new Invention(l.GetInt32(0), sorte, l.GetInt32(2), (Biome)l.GetInt32(3), l.GetInt32(4), l.GetString(5), proprietes,
                (SorteAliment)l.GetInt32(7), l.GetInt64(8), l.GetInt64(9), l.GetInt32(10) != 0));
        }
        return liste;
    }

    private static void EnregistrerInventions(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Invention> inventions)
    {
        Executer(connexion, transaction, "DELETE FROM inventions;");
        if (inventions.Count == 0) return;
        using var c = Preparer(connexion, transaction, """
            INSERT INTO inventions (id, sorte, numero, biome, forme, nom, proprietes, aliment, le, evenement, disparue)
            VALUES ($i, $s, $n, $b, $f, $nom, $p, $a, $le, $e, $d);
            """, "$i", "$s", "$n", "$b", "$f", "$nom", "$p", "$a", "$le", "$e", "$d");
        foreach (var i in inventions)
            Lancer(c, i.Id, i.Sorte, i.Numero, (int)i.Biome, i.Forme, i.Nom, JsonSerializer.Serialize(i.Proprietes), (int)i.Aliment, i.Le, i.Evenement, i.Disparue ? 1 : 0);
    }
}
