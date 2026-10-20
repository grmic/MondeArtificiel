using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Gestes de l'Entité qui durent, souvenirs des communautés, et regard de chacun sur l'invisible.</summary>
public sealed partial class DepotMonde
{
    private const string EffacerGestes = "DELETE FROM effets_entite; DELETE FROM souvenirs_entite;";

    /// <summary>
    /// Migration vers la version 30 (gestes de l'Entité, regard sur l'invisible). Le regard passé est estimé : l'amour d'après
    /// les prières que chacun a crues entendues (par l'Entité ou par hasard), la crainte d'après les catastrophes meurtrières
    /// envoyées par l'Entité près de lui depuis sa naissance, l'un et l'autre selon sa croyance ; la révolte part de zéro.
    /// </summary>
    private static void MigrerVersion30(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var humains = Colonnes(connexion, transaction, "humains");
        foreach (var (colonne, type, defaut) in new[]
                 {
                     ("amour", "REAL", "0"), ("crainte", "REAL", "0"), ("revolte", "REAL", "0"), ("regard_signe", "INTEGER", "0"),
                     ("fecondite_entite", "REAL", "1"), ("fecondite_jusqua", "INTEGER", "0"),
                 })
        {
            if (!humains.Contains(colonne)) Executer(connexion, transaction, $"ALTER TABLE humains ADD COLUMN {colonne} {type} NOT NULL DEFAULT {defaut};");
        }
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS effets_entite (
                rang       INTEGER PRIMARY KEY,
                geste      INTEGER NOT NULL,
                communaute INTEGER NOT NULL,
                debut      INTEGER NOT NULL,
                fin        INTEGER NOT NULL,
                force      REAL    NOT NULL,
                x          INTEGER NOT NULL,
                y          INTEGER NOT NULL,
                rayon      INTEGER NOT NULL,
                evenement  INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS souvenirs_entite (
                rang       INTEGER PRIMARY KEY,
                communaute INTEGER NOT NULL,
                texte      TEXT    NOT NULL,
                le         INTEGER NOT NULL,
                bienfait   INTEGER NOT NULL,
                force      REAL    NOT NULL,
                evenement  INTEGER NOT NULL,
                faute      INTEGER NOT NULL
            );
            """);
        if (Colonnes(connexion, transaction, "prieres").Count > 0)
        {
            Executer(connexion, transaction, """
                UPDATE humains SET amour = ROUND(MIN(60,
                        20 * (SELECT COUNT(*) FROM prieres p WHERE p.humain = humains.id AND p.issue = 1)
                      + 8 * (SELECT COUNT(*) FROM prieres p WHERE p.humain = humains.id AND p.issue = 2 AND p.sujet <> 6))
                    * (0.4 + 0.6 * COALESCE(croyance, 0) / 100), 1)
                WHERE mort_le IS NULL;
                """);
        }
        if (Colonnes(connexion, transaction, "catastrophes").Count > 0)
        {
            Executer(connexion, transaction, """
                UPDATE humains SET crainte = ROUND(MIN(50, 10 * (SELECT COUNT(*) FROM catastrophes c
                        WHERE c.origine = 1 AND c.morts > 0 AND c.debut >= humains.naissance
                          AND ABS(c.x - humains.x) <= c.rayon + 5 AND ABS(c.y - humains.y) <= c.rayon + 5))
                    * (0.4 + 0.6 * COALESCE(croyance, 0) / 100), 1)
                WHERE mort_le IS NULL;
                """);
        }
    }

    /// <summary>Gestes qui durent et souvenirs des communautés.</summary>
    private static EtatMonde LireGestes(SqliteCommand commande, EtatMonde etat)
    {
        commande.Parameters.Clear();
        var effets = new List<EffetEntite>();
        commande.CommandText = "SELECT geste, communaute, debut, fin, force, x, y, rayon, evenement FROM effets_entite ORDER BY rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                effets.Add(new EffetEntite((GesteEntite)l.GetInt32(0), l.GetInt32(1), l.GetInt64(2), l.GetInt64(3), l.GetDouble(4), l.GetInt32(5),
                    l.GetInt32(6), l.GetInt32(7), l.GetInt64(8)));
            }
        }
        var souvenirs = new List<SouvenirEntite>();
        commande.CommandText = "SELECT communaute, texte, le, bienfait, force, evenement, faute FROM souvenirs_entite ORDER BY rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
                souvenirs.Add(new SouvenirEntite(l.GetInt32(0), l.GetString(1), l.GetInt64(2), l.GetInt32(3) != 0, l.GetDouble(4), l.GetInt64(5), l.GetInt32(6)));
        }
        if (effets.Count > 0) etat = etat with { EffetsEntite = effets };
        if (souvenirs.Count > 0) etat = etat with { SouvenirsEntite = souvenirs };
        return etat;
    }

    /// <summary>Remplace les gestes qui durent et les souvenirs des communautés.</summary>
    private static void EnregistrerGestes(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, EffacerGestes);
        using var effets = Preparer(connexion, transaction,
            "INSERT INTO effets_entite (rang, geste, communaute, debut, fin, force, x, y, rayon, evenement) VALUES ($k, $g, $c, $d, $f, $fo, $x, $y, $r, $e);",
            "$k", "$g", "$c", "$d", "$f", "$fo", "$x", "$y", "$r", "$e");
        var k = 0;
        foreach (var e in etat.EffetsEntite ?? [])
            Lancer(effets, k++, (int)e.Geste, e.CommunauteId, e.Debut, e.Fin, e.Force, e.X, e.Y, e.Rayon, e.EvenementId);
        using var souvenirs = Preparer(connexion, transaction,
            "INSERT INTO souvenirs_entite (rang, communaute, texte, le, bienfait, force, evenement, faute) VALUES ($k, $c, $t, $l, $b, $f, $e, $fa);",
            "$k", "$c", "$t", "$l", "$b", "$f", "$e", "$fa");
        k = 0;
        foreach (var s in etat.SouvenirsEntite ?? [])
            Lancer(souvenirs, k++, s.CommunauteId, s.Texte, s.Le, s.Bienfait ? 1 : 0, s.Force, s.EvenementId, s.Faute);
    }
}
