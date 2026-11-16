using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Ce que l'Entité a révélé, et ce que chacun en garde (0.42.9) ; les promesses faites à l'Entité.</summary>
public sealed partial class DepotMonde
{
    /// <summary>
    /// Migration vers la version 62 : les révélations de l'Entité (une ligne chacune, en JSON), ce que chaque humain en sait (en JSON
    /// dans la colonne humains.revelations) et les promesses faites à l'Entité (impulsions.promesse). Les mondes existants n'en ont pas.
    /// </summary>
    private static void MigrerVersion62(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS revelations (
                id          INTEGER PRIMARY KEY,
                secondes    INTEGER NOT NULL,
                portee      INTEGER NOT NULL,
                destinataire INTEGER NOT NULL,
                texte       TEXT NOT NULL,
                donnees     TEXT NOT NULL
            );
            """);
        if (!Colonnes(connexion, transaction, "humains").Contains("revelations")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN revelations TEXT;");
        if (!Colonnes(connexion, transaction, "impulsions").Contains("promesse"))
            Executer(connexion, transaction, "ALTER TABLE impulsions ADD COLUMN promesse INTEGER NOT NULL DEFAULT 0;");
    }

    private static List<Revelation> LireRevelations(SqliteCommand commande)
    {
        var liste = new List<Revelation>();
        commande.CommandText = "SELECT donnees FROM revelations ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            try
            {
                if (JsonSerializer.Deserialize<Revelation>(l.GetString(0)) is { } r && Enum.IsDefined(r.Portee) && Enum.IsDefined(r.Mode))
                    liste.Add(r);
            }
            catch (JsonException)
            {
                // Une révélation illisible est oubliée.
            }
        }
        // Le numéro d'une révélation est son rang : la liste s'arrête au premier trou.
        return liste.TakeWhile((r, i) => r.Id == i + 1).ToList();
    }

    private static void EnregistrerRevelations(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Revelation> revelations)
    {
        Executer(connexion, transaction, "DELETE FROM revelations;");
        using var c = Preparer(connexion, transaction, """
            INSERT INTO revelations (id, secondes, portee, destinataire, texte, donnees) VALUES ($i, $s, $p, $d, $t, $j);
            """, "$i", "$s", "$p", "$d", "$t", "$j");
        foreach (var r in revelations)
            Lancer(c, r.Id, r.Le, (int)r.Portee, r.Destinataire, r.Texte, JsonSerializer.Serialize(r));
    }

    private static void LireRevelationsHumains(SqliteCommand commande, List<Humain> humains)
    {
        commande.CommandText = "SELECT id, revelations FROM humains WHERE revelations IS NOT NULL ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var id = l.GetInt64(0);
            if (id < 1 || id > humains.Count) continue;
            try
            {
                if (JsonSerializer.Deserialize<List<Connue>>(l.GetString(1)) is { } connues)
                    humains[(int)id - 1].Revelations = connues.Where(c => c.Id > 0 && double.IsFinite(c.Fidelite))
                        .Select(c => c with { Fidelite = Math.Clamp(c.Fidelite, 0, 1) }).ToList();
            }
            catch (JsonException)
            {
                // Illisible : il ne s'en souvient plus.
            }
        }
    }

    private static void EnregistrerRevelationsHumains(SqliteConnection connexion, SqliteTransaction transaction, Ecritures ecritures, IReadOnlyList<Humain> humains)
    {
        using var c = new ParHumain(connexion, transaction, ecritures, "humains.revelations",
            Preparer(connexion, transaction, "UPDATE humains SET revelations = $r WHERE id = $id;", "$r", "$id"), suitLaLigne: true);
        foreach (var h in humains)
        {
            c.Debut(h.Id);
            c.Ligne(h.Revelations.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(h.Revelations), h.Id);
            c.Fin();
        }
    }
}
