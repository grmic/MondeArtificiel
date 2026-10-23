using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Les rencontres entre communautés (visites, rassemblements) et les ouvrages qu'on demande à plus habile que soi.</summary>
public sealed partial class DepotMonde
{
    /// <summary>
    /// Migration vers la version 34 : visites, rassemblements, communauté d'où vient un humain, ouvrages demandés et spécialistes.
    /// Les mondes existants n'en ont pas encore : tout part de zéro (personne n'est en visite, personne n'est dit spécialiste).
    /// </summary>
    private static void MigrerVersion34(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var humains = Colonnes(connexion, transaction, "humains");
        if (!humains.Contains("visite_chez")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN visite_chez INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("visite_depuis")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN visite_depuis INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("visite_arrivee")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN visite_arrivee INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("visite_jusqua")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN visite_jusqua INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("visite_evenement")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN visite_evenement INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("visite_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN visite_le INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("rassemblement")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN rassemblement INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("rentrer")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN rentrer INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("venu_de")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN venu_de INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("demande_a")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN demande_a INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("demande_ouvrage")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN demande_ouvrage INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("demande_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN demande_le INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("servis_taille")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN servis_taille INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("servis_assemblage")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN servis_assemblage INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("servis_peau")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN servis_peau INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("specialite")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN specialite INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("specialite_depuis")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN specialite_depuis INTEGER NOT NULL DEFAULT 0;");
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS rassemblements (
                id          INTEGER PRIMARY KEY,
                hote        INTEGER NOT NULL,
                invitee     INTEGER NOT NULL,
                x           INTEGER NOT NULL,
                y           INTEGER NOT NULL,
                debut       INTEGER NOT NULL,
                fin         INTEGER NOT NULL,
                fini_le     INTEGER NOT NULL,
                evenement   INTEGER NOT NULL,
                bilan       INTEGER NOT NULL,
                participants INTEGER NOT NULL,
                cadeaux     INTEGER NOT NULL,
                appris      INTEGER NOT NULL,
                couples     INTEGER NOT NULL
            );
            """);
    }

    private static List<Rassemblement> LireRassemblements(SqliteCommand commande)
    {
        var liste = new List<Rassemblement>();
        commande.CommandText = "SELECT id, hote, invitee, x, y, debut, fin, fini_le, evenement, bilan, participants, cadeaux, appris, couples FROM rassemblements ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            liste.Add(new Rassemblement
            {
                Id = l.GetInt32(0), HoteId = l.GetInt32(1), InviteeId = l.GetInt32(2), X = l.GetInt32(3), Y = l.GetInt32(4), Debut = l.GetInt64(5),
                Fin = l.GetInt64(6), FiniLe = l.GetInt64(7), EvenementId = l.GetInt64(8), BilanEvenementId = l.GetInt64(9),
                Participants = l.GetInt32(10), Cadeaux = l.GetInt32(11), Appris = l.GetInt32(12), Couples = l.GetInt32(13),
            });
        }
        return liste;
    }

    private static void EnregistrerRassemblements(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Rassemblement> rassemblements)
    {
        Executer(connexion, transaction, "DELETE FROM rassemblements;");
        using var c = Preparer(connexion, transaction, """
            INSERT INTO rassemblements (id, hote, invitee, x, y, debut, fin, fini_le, evenement, bilan, participants, cadeaux, appris, couples)
            VALUES ($id, $h, $i, $x, $y, $d, $f, $fl, $e, $b, $p, $c, $a, $co);
            """, "$id", "$h", "$i", "$x", "$y", "$d", "$f", "$fl", "$e", "$b", "$p", "$c", "$a", "$co");
        foreach (var r in rassemblements)
            Lancer(c, r.Id, r.HoteId, r.InviteeId, r.X, r.Y, r.Debut, r.Fin, r.FiniLe, r.EvenementId, r.BilanEvenementId, r.Participants, r.Cadeaux, r.Appris, r.Couples);
    }
}
