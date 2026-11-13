using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Recits;

namespace Monde.Stockage;

/// <summary>Un humain ou une communauté suivi ★ ; <paramref name="Heritier"/> : à sa mort, le suivi passe à son héritier.</summary>
public sealed record Suivi(bool Communaute, int Id, string Nom, bool Heritier);

/// <summary>Une alerte gardée dans la base, avec le texte qui la dit.</summary>
public sealed record AlerteEnregistree(long Id, long Secondes, SorteAlerte Sorte, int HumainId, int CommunauteId, long EvenementId,
    Recit? Texte, bool Lue);

/// <summary>Suivis ★ et alertes (0.40.0) : ils vivent dans la base du monde, communs à tous les navigateurs.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 60 : suivis et alertes.</summary>
    private static void MigrerVersion60(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS suivis (
                communaute  INTEGER NOT NULL,
                id          INTEGER NOT NULL,
                nom         TEXT    NOT NULL,
                heritier    INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (communaute, id)
            );
            CREATE TABLE IF NOT EXISTS alertes (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                secondes    INTEGER NOT NULL,
                sorte       INTEGER NOT NULL,
                humain      INTEGER NOT NULL,
                communaute  INTEGER NOT NULL,
                evenement   INTEGER NOT NULL,
                forme       INTEGER,
                valeurs     TEXT,
                lue         INTEGER NOT NULL DEFAULT 0
            );
            """);
    }

    /// <summary>Plus grand nombre d'alertes gardées : les plus anciennes s'effacent au-delà.</summary>
    public const int AlertesMax = 2000;

    public List<Suivi> LireSuivisComplets()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT communaute, id, nom, heritier FROM suivis ORDER BY rowid;";
        using var l = commande.ExecuteReader();
        var liste = new List<Suivi>();
        while (l.Read()) liste.Add(new Suivi(l.GetInt32(0) != 0, l.GetInt32(1), l.GetString(2), l.GetInt32(3) != 0));
        return liste;
    }

    /// <summary>
    /// Ajoute ou met à jour des suivis, et en retire. Le journal des humains suivis est gardé quand le journal ancien est effacé
    /// (table journal_suivis, comme avant la 0.40.0).
    /// </summary>
    public void ChangerSuivisComplets(IEnumerable<Suivi> ajoutes, IEnumerable<(bool Communaute, int Id)> retires)
    {
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        using (var ajout = Preparer(connexion, transaction, """
                   INSERT INTO suivis (communaute, id, nom, heritier) VALUES ($c, $i, $n, $h)
                   ON CONFLICT (communaute, id) DO UPDATE SET nom = excluded.nom, heritier = excluded.heritier;
                   """, "$c", "$i", "$n", "$h"))
        using (var journal = Preparer(connexion, transaction, "INSERT OR IGNORE INTO journal_suivis (humain) VALUES ($h);", "$h"))
        {
            foreach (var s in ajoutes)
            {
                if (s.Id <= 0) continue;
                Lancer(ajout, s.Communaute ? 1 : 0, s.Id, s.Nom.Length > 200 ? s.Nom[..200] : s.Nom, s.Heritier ? 1 : 0);
                if (!s.Communaute) Lancer(journal, s.Id);
            }
        }
        using (var retrait = Preparer(connexion, transaction, "DELETE FROM suivis WHERE communaute = $c AND id = $i;", "$c", "$i"))
        using (var journal = Preparer(connexion, transaction, "DELETE FROM journal_suivis WHERE humain = $h;", "$h"))
        {
            foreach (var (communaute, id) in retires)
            {
                Lancer(retrait, communaute ? 1 : 0, id);
                if (!communaute) Lancer(journal, id);
            }
        }
        transaction.Commit();
    }

    /// <summary>Garde des alertes (après la sauvegarde du monde : les humains qu'elles citent sont déjà dans la base).</summary>
    public void EnregistrerAlertes(IReadOnlyList<AlerteSuivi> alertes)
    {
        if (alertes.Count == 0) return;
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        var formes = FormesRecits.Lire(connexion, transaction);
        using (var commande = Preparer(connexion, transaction, """
                   INSERT INTO alertes (secondes, sorte, humain, communaute, evenement, forme, valeurs) VALUES ($s, $o, $h, $c, $e, $f, $v);
                   """, "$s", "$o", "$h", "$c", "$e", "$f", "$v"))
        {
            foreach (var a in alertes)
            {
                var (_, forme, valeurs) = formes.Colonnes(a.Texte, null);
                Lancer(commande, a.Secondes, (int)a.Sorte, a.HumainId, a.CommunauteId, a.EvenementId, forme, valeurs);
            }
        }
        formes.Enregistrer(connexion, transaction);
        Executer(connexion, transaction, $"DELETE FROM alertes WHERE id <= (SELECT MAX(id) FROM alertes) - {AlertesMax};");
        transaction.Commit();
    }

    /// <summary>Les dernières alertes, des plus récentes aux plus anciennes (avant <paramref name="avant"/> si non nul).</summary>
    public List<AlerteEnregistree> LireAlertes(int nombre, long avant = 0)
    {
        using var connexion = Ouvrir();
        var formes = FormesRecits.Lire(connexion, null);
        using var commande = connexion.CreateCommand();
        commande.CommandText = """
            SELECT id, secondes, sorte, humain, communaute, evenement, forme, valeurs, lue FROM alertes
            WHERE $a = 0 OR id < $a ORDER BY id DESC LIMIT $n;
            """;
        commande.Parameters.AddWithValue("$a", avant);
        commande.Parameters.AddWithValue("$n", Math.Clamp(nombre, 1, AlertesMax));
        using var l = commande.ExecuteReader();
        var liste = new List<AlerteEnregistree>();
        while (l.Read())
        {
            var sorte = (SorteAlerte)l.GetInt32(2);
            if (!Enum.IsDefined(sorte)) continue;
            liste.Add(new AlerteEnregistree(l.GetInt64(0), l.GetInt64(1), sorte, l.GetInt32(3), l.GetInt32(4), l.GetInt64(5),
                formes.Lire(l, 6, 7), l.GetInt32(8) != 0));
        }
        return liste;
    }

    /// <summary>Nombre d'alertes non lues.</summary>
    public int AlertesNonLues()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT COUNT(*) FROM alertes WHERE lue = 0;";
        return Convert.ToInt32(commande.ExecuteScalar());
    }

    public void MarquerAlertesLues()
    {
        using var connexion = Ouvrir();
        Executer(connexion, null, "UPDATE alertes SET lue = 1 WHERE lue = 0;");
    }
}
