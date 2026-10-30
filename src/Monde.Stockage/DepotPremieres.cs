using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Nature;

namespace Monde.Stockage;

/// <summary>Premières fois déjà arrivées, au monde et à chaque communauté (voir <see cref="RegistrePremieres"/>).</summary>
public sealed partial class DepotMonde
{
    /// <summary>
    /// Migration vers la version 42 : la marque d'une première fois sur une ligne du journal, et la table de ce qui est déjà arrivé.
    /// Un monde déjà commencé relit une fois son journal (lignes en clair et paquets archivés) : ce qui y est arrivé compte comme
    /// passé, sans que les anciennes lignes soient marquées. Si une partie du journal a été effacée, tout ce qui pouvait y être
    /// compte comme passé, pour le monde et pour chaque communauté : mieux vaut une première fois oubliée qu'une fausse.
    /// </summary>
    private static void MigrerVersion42(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "journal").Contains("premiere"))
            Executer(connexion, transaction, "ALTER TABLE journal ADD COLUMN premiere INTEGER NOT NULL DEFAULT 0;");
        Executer(connexion, transaction, "CREATE TABLE IF NOT EXISTS premieres (cle TEXT PRIMARY KEY) WITHOUT ROWID;");
        if (Convert.ToInt64(Scalaire(connexion, transaction, "SELECT count(*) FROM premieres;")) > 0) return;

        var catastrophes = new Dictionary<long, TypeCatastrophe>();
        using (var c = connexion.CreateCommand())
        {
            c.Transaction = transaction;
            c.CommandText = "SELECT id, type FROM catastrophes;";
            using var l = c.ExecuteReader();
            while (l.Read()) catastrophes[l.GetInt64(0)] = (TypeCatastrophe)l.GetInt32(1);
        }
        TypeCatastrophe? Type(long id) => catastrophes.TryGetValue(id, out var t) ? t : null;

        var registre = new RegistrePremieres();
        var formes = FormesRecits.Lire(connexion, transaction);
        var types = string.Join(", ", PremieresFois.Types.Select(t => (int)t));
        // Les paquets archivés sont plus anciens que les lignes en clair : on les relit d'abord, dans l'ordre.
        using (var blocs = connexion.CreateCommand())
        {
            blocs.Transaction = transaction;
            blocs.CommandText = "SELECT donnees FROM journal_blocs ORDER BY premier;";
            using var l = blocs.ExecuteReader();
            while (l.Read())
            {
                foreach (var x in Decompresser((byte[])l[0]))
                    if (PremieresFois.Types.Contains((TypeEvenement)x.Type)) registre.Relire(VersEvenement(x, formes), Type);
            }
        }
        using (var lire = connexion.CreateCommand())
        {
            lire.Transaction = transaction;
            lire.CommandText = $"SELECT {ColonnesJournal} FROM journal WHERE id > $apres AND type IN ({types}) ORDER BY id LIMIT 50000;";
            var apres = lire.Parameters.AddWithValue("$apres", 0L);
            while (true)
            {
                var lus = new List<LigneJournal>();
                using (var l = lire.ExecuteReader())
                {
                    while (l.Read()) lus.Add(LireLigne(l));
                }
                if (lus.Count == 0) break;
                foreach (var x in lus) registre.Relire(VersEvenement(x, formes), Type);
                apres.Value = lus[^1].Id;
            }
        }
        var cles = new HashSet<string>(registre.Passees, StringComparer.Ordinal);
        // Un journal dont une partie a été effacée (conservation limitée) : tout ce qui pouvait s'y trouver est compté comme passé.
        if (Convert.ToInt64(Scalaire(connexion, transaction, "SELECT coalesce(max(tout), 0) FROM journal_purge;")) > 0)
        {
            var sortes = SortesPossibles(catastrophes.Values);
            foreach (var (s, monde, _) in sortes) if (monde) cles.Add($"m:{s}");
            using var c = connexion.CreateCommand();
            c.Transaction = transaction;
            c.CommandText = "SELECT id FROM communautes;";
            using var l = c.ExecuteReader();
            while (l.Read())
                foreach (var (s, _, communaute) in sortes)
                    if (communaute) cles.Add($"c:{l.GetInt64(0)}:{s}");
        }
        EnregistrerPremieres(connexion, transaction, cles);
    }

    /// <summary>Toutes les sortes de premières fois qu'un journal effacé pouvait contenir (pour le monde, pour une communauté).</summary>
    private static List<(string Sorte, bool Monde, bool Communaute)> SortesPossibles(IEnumerable<TypeCatastrophe> catastrophes)
    {
        List<(string, bool, bool)> l =
        [
            ("naissance", true, true), ("mort", true, true), ("meurtre", true, true), ("pardon", true, true), ("horsCouple", true, true),
            ("memeSexe", true, true), ("feu", false, true), ("guerre", true, true), ("paix", true, true), ("religion", true, true),
            ("prophete", true, true), ("priere", true, true), ("priere.exaucee", true, false), ("entite.parole", true, true),
            ("entite.crue", true, false), ("entite.groupe", true, true), ("entite.geste", true, false),
        ];
        foreach (var c in Enum.GetNames<Simulation.Humains.CauseDeMort>()) l.Add(($"mort.{c}", true, false));
        l.Add(("mort.Catastrophe", true, false));
        l.Add(("mort.Guerre", true, false));
        foreach (var t in Enum.GetValues<TypeCatastrophe>()) l.Add(($"cat.{t}", true, true));
        return l;
    }

    private static void EnregistrerPremieres(SqliteConnection connexion, SqliteTransaction transaction, IEnumerable<string> cles)
    {
        using var commande = Preparer(connexion, transaction, "INSERT OR IGNORE INTO premieres (cle) VALUES ($c);", "$c");
        foreach (var c in cles) Lancer(commande, c);
    }

    private static List<string> LirePremieres(SqliteConnection connexion)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT cle FROM premieres ORDER BY cle;";
        var cles = new List<string>();
        using var l = commande.ExecuteReader();
        while (l.Read()) cles.Add(l.GetString(0));
        return cles;
    }

    private static object? Scalaire(SqliteConnection connexion, SqliteTransaction transaction, string sql)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = sql;
        return commande.ExecuteScalar();
    }
}
