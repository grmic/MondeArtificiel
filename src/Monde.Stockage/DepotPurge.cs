using Microsoft.Data.Sqlite;
using Monde.Simulation;

namespace Monde.Stockage;

/// <summary>
/// Ce que l'on efface du journal ancien (0.31.4). <see cref="AvantTout"/> : tout ce qui date d'avant (secondes du monde) ;
/// <see cref="AvantQuotidien"/> : la vie quotidienne seule (gestes de la journée, pensées, partages) d'avant cette date.
/// Null : rien n'est effacé à ce titre. Les faits marquants et les lignes des humains suivis restent, sauf si
/// <see cref="Marquants"/> ou <see cref="Suivis"/> le demandent.
/// </summary>
public sealed record ReglesPurge(long? AvantTout, long? AvantQuotidien, bool Marquants, bool Suivis)
{
    /// <summary>Vie quotidienne : ce que la durée « Garder la vie quotidienne » efface.</summary>
    public static readonly IReadOnlyList<TypeEvenement> Quotidien = [TypeEvenement.Journee, TypeEvenement.Pensee, TypeEvenement.Partage];

    public bool Active => AvantTout is not null || AvantQuotidien is not null;
}

public sealed partial class DepotMonde
{
    /// <summary>Numéros du journal parcourus à chaque pas d'effacement : le fichier n'est jamais bloqué longtemps.</summary>
    public const int NumerosParPasDePurge = 50_000;

    /// <summary>Humains suivis dans un navigateur : leur journal est gardé (sauf « Effacer aussi le journal des humains suivis »).</summary>
    public IReadOnlyList<int> LireSuivis()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT humain FROM journal_suivis ORDER BY humain;";
        var suivis = new List<int>();
        using var l = commande.ExecuteReader();
        while (l.Read()) suivis.Add(l.GetInt32(0));
        return suivis;
    }

    /// <summary>Ajoute et retire des humains suivis. Rend vrai si la liste a changé.</summary>
    public bool ChangerSuivis(IEnumerable<int> ajoutes, IEnumerable<int> retires)
    {
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        var change = 0;
        using (var ajout = Preparer(connexion, transaction, "INSERT OR IGNORE INTO journal_suivis (humain) VALUES ($h);", "$h"))
        {
            foreach (var h in ajoutes.Where(h => h > 0).Distinct())
            {
                ajout.Parameters[0].Value = h;
                change += ajout.ExecuteNonQuery();
            }
        }
        using (var retrait = Preparer(connexion, transaction, "DELETE FROM journal_suivis WHERE humain = $h;", "$h"))
        {
            foreach (var h in retires.Distinct())
            {
                retrait.Parameters[0].Value = h;
                change += retrait.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        return change > 0;
    }

    /// <summary>
    /// Un pas d'effacement du journal ancien selon <paramref name="regles"/> : au plus <see cref="NumerosParPasDePurge"/> numéros,
    /// dans la table comme dans les paquets compressés. Ce qui a déjà été parcouru n'est pas relu, tant que les règles
    /// (cases et humains suivis) ne changent pas ; une règle changée fait tout reparcourir une fois.
    /// Rend le nombre de lignes effacées, et vrai si un pas a été fait (il peut en rester : rappeler jusqu'à faux).
    /// </summary>
    public (long Effacees, bool Reste) PurgerJournal(ReglesPurge regles)
    {
        if (!regles.Active) return (0, false);
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        var suivis = new HashSet<int>();
        using (var commande = connexion.CreateCommand())
        {
            commande.Transaction = transaction;
            commande.CommandText = "SELECT humain FROM journal_suivis;";
            using var l = commande.ExecuteReader();
            while (l.Read()) suivis.Add(l.GetInt32(0));
        }
        var signature = $"{(regles.Marquants ? 1 : 0)}{(regles.Suivis ? 1 : 0)}:" + (regles.Suivis ? "" : string.Join(",", suivis.Order()));
        long tout = 0, quotidien = 0;
        using (var commande = connexion.CreateCommand())
        {
            commande.Transaction = transaction;
            commande.CommandText = "SELECT signature, tout, quotidien FROM journal_purge WHERE id = 1;";
            using var l = commande.ExecuteReader();
            if (l.Read() && l.GetString(0) == signature) (tout, quotidien) = (l.GetInt64(1), l.GetInt64(2));
        }

        long effacees = 0;
        var pas = false;
        // La vie quotidienne d'abord : sa durée est en général la plus courte. Un seul pas à la fois.
        foreach (var (avant, quotidienSeul) in new[] { (regles.AvantQuotidien, true), (regles.AvantTout, false) })
        {
            if (avant is not { } date) continue;
            var de = quotidienSeul ? quotidien : tout;
            var borne = PremierNumeroDepuis(connexion, transaction, date);
            if (de >= borne) continue;
            var jusqua = Math.Min(borne, de + NumerosParPasDePurge);
            bool Effacer(LigneJournal x) => x.Id >= de && x.Id < jusqua
                && (!quotidienSeul || ReglesPurge.Quotidien.Contains((TypeEvenement)x.Type))
                && (regles.Marquants || !x.Significatif)
                && (regles.Suivis || (!suivis.Contains(x.Humain) && !suivis.Contains(x.Autre)));
            effacees += EffacerDansTable(connexion, transaction, de, jusqua, quotidienSeul, regles);
            effacees += EffacerDansPaquets(connexion, transaction, de, jusqua, Effacer);
            if (quotidienSeul) quotidien = jusqua;
            else tout = jusqua;
            pas = true;
            break;
        }

        if (!pas) return (0, false);
        using (var commande = Preparer(connexion, transaction,
                   "INSERT INTO journal_purge (id, signature, tout, quotidien) VALUES (1, $s, $t, $q) " +
                   "ON CONFLICT (id) DO UPDATE SET signature = excluded.signature, tout = excluded.tout, quotidien = excluded.quotidien;",
                   "$s", "$t", "$q"))
        {
            Lancer(commande, signature, tout, quotidien);
        }
        transaction.Commit();
        return (effacees, true);
    }

    private static long EffacerDansTable(SqliteConnection connexion, SqliteTransaction transaction, long de, long jusqua, bool quotidienSeul, ReglesPurge regles)
    {
        var conditions = new List<string> { "id >= $de", "id < $a", "id < (SELECT max(id) FROM journal)" };
        if (quotidienSeul) conditions.Add($"type IN ({string.Join(", ", ReglesPurge.Quotidien.Select(t => (int)t))})");
        if (!regles.Marquants) conditions.Add("significatif = 0");
        // Les premières fois ne s'effacent jamais.
        conditions.Add($"NOT {PremiereSql}");
        if (!regles.Suivis)
        {
            conditions.Add("humain NOT IN (SELECT humain FROM journal_suivis)");
            conditions.Add("autre NOT IN (SELECT humain FROM journal_suivis)");
        }
        using var commande = Preparer(connexion, transaction, $"DELETE FROM journal WHERE {string.Join(" AND ", conditions)};", "$de", "$a");
        commande.Parameters[0].Value = de;
        commande.Parameters[1].Value = jusqua;
        return commande.ExecuteNonQuery();
    }

    /// <summary>Retire des paquets les lignes à effacer ; un paquet vidé disparaît, les autres sont recompressés.</summary>
    private static long EffacerDansPaquets(SqliteConnection connexion, SqliteTransaction transaction, long de, long jusqua, Func<LigneJournal, bool> effacer)
    {
        var paquets = new List<(long Premier, byte[] Donnees)>();
        using (var commande = Preparer(connexion, transaction, "SELECT premier, donnees FROM journal_blocs WHERE dernier >= $de AND premier < $a;", "$de", "$a"))
        {
            commande.Parameters[0].Value = de;
            commande.Parameters[1].Value = jusqua;
            using var l = commande.ExecuteReader();
            while (l.Read()) paquets.Add((l.GetInt64(0), (byte[])l[1]));
        }
        long effacees = 0;
        foreach (var (premier, donnees) in paquets)
        {
            var lignes = Decompresser(donnees);
            var gardees = lignes.Where(x => !effacer(x)).ToList();
            if (gardees.Count == lignes.Count) continue;
            effacees += lignes.Count - gardees.Count;
            using (var cles = Preparer(connexion, transaction, "DELETE FROM journal_blocs_cles WHERE bloc = $b;", "$b"))
            {
                Lancer(cles, premier);
            }
            if (gardees.Count == 0)
            {
                using var vider = Preparer(connexion, transaction, "DELETE FROM journal_blocs WHERE premier = $b;", "$b");
                Lancer(vider, premier);
                continue;
            }
            // Le paquet garde ses bornes de numéros (premier, dernier) : elles disent seulement où chercher.
            using (var maj = Preparer(connexion, transaction,
                       "UPDATE journal_blocs SET debut = $a, fin = $f, lignes = $n, donnees = $x WHERE premier = $b;", "$a", "$f", "$n", "$x", "$b"))
            {
                Lancer(maj, gardees.Min(x => x.Secondes), gardees.Max(x => x.Secondes), gardees.Count, Compresser(gardees), premier);
            }
            IndexerBloc(connexion, transaction, premier, gardees);
        }
        return effacees;
    }

    /// <summary>
    /// Premier numéro du journal (table ou paquets) daté de <paramref name="secondes"/> ou après : tout ce qui le précède est
    /// plus ancien. Les numéros suivent l'ordre du temps ; la table est parcourue par dichotomie sur sa clé.
    /// </summary>
    private static long PremierNumeroDepuis(SqliteConnection connexion, SqliteTransaction transaction, long secondes)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        // Dans les paquets : le premier qui va jusqu'à cette date, ouvert pour y trouver la ligne exacte.
        commande.CommandText = "SELECT donnees FROM journal_blocs WHERE fin >= $s ORDER BY premier LIMIT 1;";
        commande.Parameters.AddWithValue("$s", secondes);
        var paquets = commande.ExecuteScalar() is byte[] donnees
            ? Decompresser(donnees).First(x => x.Secondes >= secondes).Id
            : long.MaxValue;
        commande.CommandText = "SELECT max(coalesce((SELECT max(id) FROM journal), 0), coalesce((SELECT max(dernier) FROM journal_blocs), 0)) + 1;";
        paquets = Math.Min(paquets, Convert.ToInt64(commande.ExecuteScalar()));
        commande.CommandText = "SELECT coalesce(max(id), 0) + 1 FROM journal;";
        long bas = 0, haut = Convert.ToInt64(commande.ExecuteScalar());
        commande.CommandText = "SELECT id, secondes FROM journal WHERE id >= $m ORDER BY id LIMIT 1;";
        var m = commande.Parameters.AddWithValue("$m", 0L);
        var fin = haut;
        while (bas < haut)
        {
            m.Value = bas + (haut - bas) / 2;
            using var l = commande.ExecuteReader();
            if (!l.Read() || l.GetInt64(1) >= secondes) haut = (long)m.Value;
            else bas = l.GetInt64(0) + 1;
        }
        // La première ligne de la table à cette date ou après (entre deux, les lignes peuvent être dans les paquets).
        m.Value = bas;
        using var premiere = commande.ExecuteReader();
        return Math.Min(premiere.Read() ? premiere.GetInt64(0) : fin, paquets);
    }

    /// <summary>
    /// Taille du monde et rythme du journal : lignes gardées (table et paquets), et lignes écrites depuis
    /// <paramref name="depuis"/> (secondes du monde). Compter une grosse table prend du temps : à faire en arrière-plan.
    /// </summary>
    public MesureJournal MesurerJournal(long depuis)
    {
        long octets = 0;
        foreach (var f in new[] { CheminFichier, CheminFichier + "-wal" })
        {
            if (File.Exists(f)) octets += new FileInfo(f).Length;
        }
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction(deferred: true);
        var borne = PremierNumeroDepuis(connexion, transaction, depuis);
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = """
            SELECT (SELECT count(*) FROM journal), (SELECT coalesce(sum(lignes), 0) FROM journal_blocs),
                   (SELECT count(*) FROM journal WHERE id >= $b) + (SELECT coalesce(sum(lignes), 0) FROM journal_blocs WHERE premier >= $b);
            """;
        commande.Parameters.AddWithValue("$b", borne);
        using var l = commande.ExecuteReader();
        l.Read();
        return new MesureJournal(octets, l.GetInt64(0), l.GetInt64(1), l.GetInt64(2));
    }
}

/// <summary>
/// Taille du fichier d'un monde, lignes du journal dans la table et dans les paquets compressés, et lignes écrites
/// pendant la période mesurée.
/// </summary>
public sealed record MesureJournal(long Octets, long Table, long Archivees, long Recentes)
{
    /// <summary>Octets par an au rythme mesuré (la période mesurée durant <paramref name="secondes"/>).</summary>
    public long OctetsParAn(long secondes) =>
        Table + Archivees == 0 || secondes <= 0 ? 0
            : (long)((double)Octets / (Table + Archivees) * Recentes * Monde.Simulation.Temps.Calendrier.SecondesParAn / secondes);
}
