using Microsoft.Data.Sqlite;
using Monde.Simulation;

namespace Monde.Stockage;

/// <summary>L'Entité libre (0.35.2) : décrets, actes à défaire, lignes du journal qu'elle a écrites.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 52 : décrets ; de quoi défaire un acte libre. Les mondes existants n'en ont aucun.</summary>
    private static void MigrerVersion52(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS decrets (
                id          INTEGER PRIMARY KEY,
                loi         INTEGER NOT NULL,
                communaute  INTEGER NOT NULL,
                facteur     REAL    NOT NULL,
                debut       INTEGER NOT NULL,
                fin         INTEGER NOT NULL,
                raison      TEXT    NOT NULL,
                acte        INTEGER NOT NULL
            );
            """);
        var colonnes = Colonnes(connexion, transaction, "entite_actes");
        if (!colonnes.Contains("defaire")) Executer(connexion, transaction, "ALTER TABLE entite_actes ADD COLUMN defaire TEXT NOT NULL DEFAULT '';");
        if (!colonnes.Contains("defait")) Executer(connexion, transaction, "ALTER TABLE entite_actes ADD COLUMN defait INTEGER NOT NULL DEFAULT 0;");
    }

    private static List<Decret> LireDecrets(SqliteCommand commande)
    {
        var liste = new List<Decret>();
        commande.CommandText = "SELECT id, loi, communaute, facteur, debut, fin, raison, acte FROM decrets ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var loi = (LoiDecret)l.GetInt32(1);
            if (!Enum.IsDefined(loi)) continue;
            liste.Add(new Decret(l.GetInt32(0), loi, l.GetInt32(2), Math.Clamp(l.GetDouble(3), Decrets.FacteurMin, Decrets.FacteurMax), l.GetInt64(4), l.GetInt64(5),
                l.GetString(6), l.GetInt64(7)));
        }
        return liste;
    }

    private static void EnregistrerDecrets(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Decret> decrets)
    {
        Executer(connexion, transaction, "DELETE FROM decrets;");
        if (decrets.Count == 0) return;
        using var c = Preparer(connexion, transaction, """
            INSERT INTO decrets (id, loi, communaute, facteur, debut, fin, raison, acte) VALUES ($i, $l, $c, $f, $d, $n, $r, $a);
            """, "$i", "$l", "$c", "$f", "$d", "$n", "$r", "$a");
        foreach (var d in decrets) Lancer(c, d.Id, (int)d.Loi, d.CommunauteId, d.Facteur, d.Debut, d.Fin, d.Raison, d.ActeId);
    }

    /// <summary>Note qu'un acte libre a été défait, et les lignes du journal que le retour a écrites.</summary>
    public void MarquerDefait(long id, string resultats)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "UPDATE entite_actes SET defait = 1, resultats = $r WHERE id = $id;";
        commande.Parameters.AddWithValue("$id", id);
        commande.Parameters.AddWithValue("$r", resultats);
        commande.ExecuteNonQuery();
    }

    /// <summary>Date du monde du premier acte libre (null : aucun) : depuis, le monde n'est plus le fruit des seules règles.</summary>
    public long? PremierActeLibre()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT min(le) FROM entite_actes WHERE sorte = 'libre' AND issue = 1;";
        return commande.ExecuteScalar() is long l ? l : null;
    }

    /// <summary>Plages du journal écrites par les actes libres (pour marquer ces lignes « LLM libre »).</summary>
    public List<(long Premier, long Dernier)> PlagesJournalLibre()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT journal_premier, journal_dernier FROM entite_actes WHERE sorte = 'libre' AND journal_dernier >= journal_premier AND journal_premier > 0 ORDER BY journal_premier;";
        using var l = commande.ExecuteReader();
        var liste = new List<(long, long)>();
        while (l.Read()) liste.Add((l.GetInt64(0), l.GetInt64(1)));
        return liste;
    }
}
