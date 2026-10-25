using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>La mer (0.31.5) : voyage en cours de chacun, terre de son peuple, premières des groupes avec les terres d'outre-mer.</summary>
public sealed partial class DepotMonde
{
    /// <summary>
    /// Migration vers la version 39 : voyage par mer et terre du peuple de chaque humain, premières avec les terres d'outre-mer.
    /// Les mondes existants n'en ont pas : personne n'est en mer, la terre de chacun sera établie au premier pas.
    /// </summary>
    private static void MigrerVersion39(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var humains = Colonnes(connexion, transaction, "humains");
        if (!humains.Contains("continent")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN continent INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("mer")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN mer TEXT;");
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS outremer (
                rang        INTEGER PRIMARY KEY,
                groupe      INTEGER NOT NULL,
                masse       INTEGER NOT NULL,
                sorte       INTEGER NOT NULL,
                secondes    INTEGER NOT NULL,
                x           INTEGER NOT NULL,
                y           INTEGER NOT NULL,
                humain      INTEGER NOT NULL,
                evenement   INTEGER NOT NULL
            );
            """);
    }

    private static void LireMer(SqliteCommand commande, List<Humain> humains)
    {
        commande.CommandText = "SELECT id, continent, mer FROM humains ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var id = l.GetInt64(0);
            if (id < 1 || id > humains.Count) continue;
            var h = humains[(int)id - 1];
            h.Continent = l.GetInt32(1);
            if (!l.IsDBNull(2) && LireTraversee(l.GetString(2)) is { } t) h.Mer = t;
        }
    }

    /// <summary>Relit un voyage enregistré ; null s'il est illisible ou hors des valeurs connues.</summary>
    internal static Traversee? LireTraversee(string json)
    {
        try
        {
            var t = JsonSerializer.Deserialize<Traversee>(json);
            return Enum.IsDefined(t.Motif) && t.Motif != MotifMer.Aucun ? t : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnregistrerMer(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Humain> humains)
    {
        using var c = Preparer(connexion, transaction, "UPDATE humains SET continent = $c, mer = $m WHERE id = $id;", "$c", "$m", "$id");
        foreach (var h in humains)
            Lancer(c, h.Continent, h.Mer.Active ? JsonSerializer.Serialize(h.Mer) : DBNull.Value, h.Id);
    }

    private static List<DecouverteOutremer> LireOutremer(SqliteCommand commande)
    {
        var liste = new List<DecouverteOutremer>();
        commande.CommandText = "SELECT groupe, masse, sorte, secondes, x, y, humain, evenement FROM outremer ORDER BY rang;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var sorte = (SorteOutremer)l.GetInt32(2);
            if (!Enum.IsDefined(sorte)) continue;
            liste.Add(new DecouverteOutremer(l.GetInt32(0), l.GetInt32(1), sorte, l.GetInt64(3), l.GetInt32(4), l.GetInt32(5), l.GetInt32(6), l.GetInt64(7)));
        }
        return liste;
    }

    private static void EnregistrerOutremer(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<DecouverteOutremer> liste)
    {
        Executer(connexion, transaction, "DELETE FROM outremer;");
        using var c = Preparer(connexion, transaction, """
            INSERT INTO outremer (rang, groupe, masse, sorte, secondes, x, y, humain, evenement) VALUES ($r, $g, $m, $s, $t, $x, $y, $h, $e);
            """, "$r", "$g", "$m", "$s", "$t", "$x", "$y", "$h", "$e");
        for (var r = 0; r < liste.Count; r++)
        {
            var d = liste[r];
            Lancer(c, r, d.Groupe, d.Masse, (int)d.Sorte, d.Secondes, d.X, d.Y, d.HumainId, d.EvenementId);
        }
    }
}
