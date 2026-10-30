using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Monde.Simulation;

namespace Monde.Stockage;

/// <summary>
/// Archives du journal (0.31.4, schéma v38). Le journal fait presque tout le poids d'un monde : passé un an, ses lignes
/// ordinaires sont rangées par paquets de <see cref="LignesParBloc"/>, compressés (Brotli), dans <c>journal_blocs</c>, et
/// <c>journal_blocs_cles</c> dit quels humains, communautés et types d'événements chaque paquet contient, pour n'ouvrir
/// que ceux qu'un filtre demande. Rien n'est perdu : les lectures du journal mêlent la table et les paquets, dans l'ordre
/// des numéros. Les faits marquants restent dans la table, toujours lisibles par un client SQL ; une copie pour analyse
/// remet tout en clair (<see cref="CopierPourAnalyse"/>).
/// </summary>
public sealed partial class DepotMonde
{
    /// <summary>Lignes par paquet : assez pour bien compresser, assez peu pour qu'en ouvrir un reste instantané.</summary>
    public const int LignesParBloc = 4096;

    /// <summary>Format d'un paquet (premier octet), si un jour il doit changer.</summary>
    private const byte FormatBloc = 1;

    // Clés de recherche d'un paquet : la sorte dans les bits hauts, le numéro dans les bas.
    private const long CleHumain = 1L << 40, CleCommunaute = 2L << 40, CleType = 3L << 40;

    private static void MigrerVersion38(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS journal_blocs (
                premier INTEGER PRIMARY KEY,
                dernier INTEGER NOT NULL,
                debut   INTEGER NOT NULL,
                fin     INTEGER NOT NULL,
                lignes  INTEGER NOT NULL,
                donnees BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS journal_blocs_cles (
                cle  INTEGER NOT NULL,
                bloc INTEGER NOT NULL,
                PRIMARY KEY (cle, bloc)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS journal_suivis (humain INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS journal_purge (
                id        INTEGER PRIMARY KEY CHECK (id = 1),
                signature TEXT NOT NULL,
                tout      INTEGER NOT NULL,
                quotidien INTEGER NOT NULL
            );
            """);
    }

    /// <summary>Une ligne du journal telle qu'elle est rangée dans un paquet (le récit se reconstitue à la lecture).</summary>
    private readonly record struct LigneJournal(long Id, long Secondes, byte Type, int Humain, int Autre, string Texte, byte Origine,
        long Cause, int Communaute, int AutreCommunaute, bool Significatif, double Karma, double KarmaAutre, int? Forme, string? Valeurs, byte Premiere = 0);

    /// <summary>
    /// Condition SQL d'une première fois (voir <see cref="PremieresFois"/>) : une ligne qui la remplit n'est jamais effacée ni rangée.
    /// Les clés sont des constantes du code, jamais une saisie.
    /// </summary>
    internal static readonly string PremiereSql =
        $"(premiere > 0 OR coalesce(forme, 0) IN (SELECT id FROM recits_formes WHERE cle IN ({Liste(PremieresFois.Toujours)})) " +
        $"OR significatif = 1 AND coalesce(forme, 0) IN (SELECT id FROM recits_formes WHERE cle IN ({Liste(PremieresFois.SiMarquants)})))";

    private static string Liste(IEnumerable<string> cles) => string.Join(", ", cles.Select(c => $"'{c}'"));

    private const string ColonnesJournal =
        "id, secondes, type, humain, autre, texte, origine, cause, communaute, autre_communaute, significatif, karma, karma_autre, forme, valeurs, premiere";

    private static LigneJournal LireLigne(SqliteDataReader l) => new(l.GetInt64(0), l.GetInt64(1), l.GetByte(2), l.GetInt32(3), l.GetInt32(4),
        l.GetString(5), l.GetByte(6), l.GetInt64(7), l.GetInt32(8), l.GetInt32(9), l.GetBoolean(10), l.GetDouble(11), l.GetDouble(12),
        l.IsDBNull(13) ? null : l.GetInt32(13), l.IsDBNull(14) ? null : l.GetString(14), l.GetByte(15));

    private static EvenementJournal VersEvenement(LigneJournal x, FormesRecits formes)
    {
        var recit = formes.Lire(x.Forme, x.Valeurs);
        return new EvenementJournal(x.Secondes, (TypeEvenement)x.Type, x.Humain, x.Autre, recit is null ? x.Texte : null, (Origine)x.Origine)
        {
            Recit = recit, Id = x.Id, CauseId = x.Cause, CommunauteId = x.Communaute, AutreCommunauteId = x.AutreCommunaute,
            Significatif = x.Significatif, Karma = x.Karma, KarmaAutre = x.KarmaAutre, Premiere = x.Premiere,
        };
    }

    private static byte[] Compresser(IReadOnlyList<LigneJournal> lignes)
    {
        using var brut = new MemoryStream();
        using (var w = new BinaryWriter(brut, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write7BitEncodedInt(lignes.Count);
            long id = 0, secondes = 0;
            foreach (var x in lignes)
            {
                // Numéros et dates par écart avec la ligne d'avant : ils se suivent, et l'écart tient en un ou deux octets.
                w.Write7BitEncodedInt64(x.Id - id);
                w.Write7BitEncodedInt64(Zigzag(x.Secondes - secondes));
                (id, secondes) = (x.Id, x.Secondes);
                w.Write(x.Type);
                w.Write(x.Origine);
                w.Write7BitEncodedInt(x.Humain);
                w.Write7BitEncodedInt(x.Autre);
                w.Write7BitEncodedInt64(x.Cause);
                w.Write7BitEncodedInt(x.Communaute);
                w.Write7BitEncodedInt(x.AutreCommunaute);
                var drapeaux = (byte)((x.Karma != 0 ? 1 : 0) | (x.KarmaAutre != 0 ? 2 : 0) | (x.Forme is null ? 0 : 4) | (x.Valeurs is null ? 0 : 8)
                                      | (x.Texte.Length > 0 ? 16 : 0) | (x.Significatif ? 32 : 0));
                w.Write(drapeaux);
                if (x.Karma != 0) w.Write(x.Karma);
                if (x.KarmaAutre != 0) w.Write(x.KarmaAutre);
                if (x.Forme is { } f) w.Write7BitEncodedInt(f);
                if (x.Valeurs is { } v) w.Write(v);
                if (x.Texte.Length > 0) w.Write(x.Texte);
            }
        }
        var donnees = brut.GetBuffer().AsSpan(0, (int)brut.Length);
        var sortie = new byte[1 + BrotliEncoder.GetMaxCompressedLength(donnees.Length)];
        sortie[0] = FormatBloc;
        // Qualité 5 : presque aussi serré que le maximum, bien plus rapide.
        if (!BrotliEncoder.TryCompress(donnees, sortie.AsSpan(1), out var ecrits, quality: 5, window: 22))
            throw new InvalidOperationException("Compression d'un paquet du journal impossible.");
        return sortie[..(1 + ecrits)];
    }

    private static List<LigneJournal> Decompresser(byte[] donnees)
    {
        if (donnees.Length == 0 || donnees[0] != FormatBloc) throw new InvalidDataException("Paquet du journal d'un format inconnu.");
        using var flux = new BrotliStream(new MemoryStream(donnees, 1, donnees.Length - 1), CompressionMode.Decompress);
        using var r = new BinaryReader(flux, System.Text.Encoding.UTF8);
        var n = r.Read7BitEncodedInt();
        var lignes = new List<LigneJournal>(n);
        long id = 0, secondes = 0;
        for (var k = 0; k < n; k++)
        {
            id += r.Read7BitEncodedInt64();
            secondes += Unzigzag(r.Read7BitEncodedInt64());
            var type = r.ReadByte();
            var origine = r.ReadByte();
            var humain = r.Read7BitEncodedInt();
            var autre = r.Read7BitEncodedInt();
            var cause = r.Read7BitEncodedInt64();
            var communaute = r.Read7BitEncodedInt();
            var autreCommunaute = r.Read7BitEncodedInt();
            var d = r.ReadByte();
            var karma = (d & 1) != 0 ? r.ReadDouble() : 0;
            var karmaAutre = (d & 2) != 0 ? r.ReadDouble() : 0;
            int? forme = (d & 4) != 0 ? r.Read7BitEncodedInt() : null;
            var valeurs = (d & 8) != 0 ? r.ReadString() : null;
            var texte = (d & 16) != 0 ? r.ReadString() : "";
            lignes.Add(new LigneJournal(id, secondes, type, humain, autre, texte, origine, cause, communaute, autreCommunaute, (d & 32) != 0,
                karma, karmaAutre, forme, valeurs));
        }
        return lignes;
    }

    private static long Zigzag(long v) => (v << 1) ^ (v >> 63);

    private static long Unzigzag(long v) => (long)((ulong)v >> 1) ^ -(v & 1);

    /// <summary>
    /// Range dans un paquet compressé les <see cref="LignesParBloc"/> plus anciennes lignes ordinaires du journal, si elles
    /// datent toutes d'avant <paramref name="avant"/> (secondes du monde). Les faits marquants restent dans la table.
    /// Rend le nombre de lignes rangées : 0 s'il n'y a pas encore de quoi faire un paquet entier.
    /// </summary>
    public int ArchiverJournal(long avant)
    {
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = "SELECT coalesce(max(dernier), 0) FROM journal_blocs;";
        var dejaRanges = Convert.ToInt64(commande.ExecuteScalar());
        // Toujours dans l'ordre des numéros, à la suite du dernier paquet : deux paquets ne se chevauchent jamais.
        commande.CommandText = $"SELECT {ColonnesJournal} FROM journal WHERE id > $d AND significatif = 0 AND NOT {PremiereSql} ORDER BY id LIMIT $n;";
        commande.Parameters.AddWithValue("$d", dejaRanges);
        commande.Parameters.AddWithValue("$n", LignesParBloc);
        var lignes = new List<LigneJournal>(LignesParBloc);
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) lignes.Add(LireLigne(l));
        }
        if (lignes.Count < LignesParBloc || lignes.Max(x => x.Secondes) >= avant) return 0;

        var (premier, dernier) = (lignes[0].Id, lignes[^1].Id);
        using (var ajout = Preparer(connexion, transaction,
                   "INSERT INTO journal_blocs (premier, dernier, debut, fin, lignes, donnees) VALUES ($p, $d, $a, $b, $n, $x);",
                   "$p", "$d", "$a", "$b", "$n", "$x"))
        {
            Lancer(ajout, premier, dernier, lignes.Min(x => x.Secondes), lignes.Max(x => x.Secondes), lignes.Count, Compresser(lignes));
        }
        IndexerBloc(connexion, transaction, premier, lignes);
        using (var effacer = Preparer(connexion, transaction,
                   $"DELETE FROM journal WHERE id >= $p AND id <= $d AND significatif = 0 AND NOT {PremiereSql};", "$p", "$d"))
        {
            Lancer(effacer, premier, dernier);
        }
        transaction.Commit();
        return lignes.Count;
    }

    /// <summary>Clés de recherche d'un paquet : ses humains, ses communautés et ses types d'événements.</summary>
    private static void IndexerBloc(SqliteConnection connexion, SqliteTransaction transaction, long premier, IReadOnlyList<LigneJournal> lignes)
    {
        var cles = new HashSet<long>();
        foreach (var x in lignes)
        {
            if (x.Humain != 0) cles.Add(CleHumain + x.Humain);
            if (x.Autre != 0) cles.Add(CleHumain + x.Autre);
            if (x.Communaute != 0) cles.Add(CleCommunaute + x.Communaute);
            if (x.AutreCommunaute != 0) cles.Add(CleCommunaute + x.AutreCommunaute);
            cles.Add(CleType + x.Type);
        }
        using var cle = Preparer(connexion, transaction, "INSERT INTO journal_blocs_cles (cle, bloc) VALUES ($c, $b);", "$c", "$b");
        foreach (var c in cles) Lancer(cle, c, premier);
    }

    /// <summary>
    /// Complète une lecture du journal par les paquets : les lignes rangées qui passent les filtres, du plus récent au plus
    /// ancien, jusqu'à en avoir <paramref name="limite"/> (assez pour que la fusion avec la table soit exacte).
    /// </summary>
    private static List<LigneJournal> LireArchives(SqliteConnection connexion, SqliteTransaction transaction,
        IReadOnlyCollection<TypeEvenement>? types, int? humain, long? avantId, int limite, int? communaute)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        var conditions = new List<string>();
        if (avantId is { } a)
        {
            conditions.Add("premier < $a");
            commande.Parameters.AddWithValue("$a", a);
        }
        if (humain is { } h)
        {
            conditions.Add("premier IN (SELECT bloc FROM journal_blocs_cles WHERE cle = $h)");
            commande.Parameters.AddWithValue("$h", CleHumain + h);
        }
        if (communaute is { } c)
        {
            conditions.Add("premier IN (SELECT bloc FROM journal_blocs_cles WHERE cle = $c)");
            commande.Parameters.AddWithValue("$c", CleCommunaute + c);
        }
        HashSet<byte>? voulus = null;
        if (types is { Count: > 0 })
        {
            voulus = [.. types.Select(t => (byte)t)];
            var noms = voulus.Select((t, i) =>
            {
                commande.Parameters.AddWithValue($"$t{i}", CleType + t);
                return $"$t{i}";
            }).ToList();
            conditions.Add($"premier IN (SELECT bloc FROM journal_blocs_cles WHERE cle IN ({string.Join(", ", noms)}))");
        }
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        commande.CommandText = $"SELECT donnees FROM journal_blocs {where} ORDER BY premier DESC;";
        var trouvees = new List<LigneJournal>();
        using var l = commande.ExecuteReader();
        while (trouvees.Count < limite && l.Read())
        {
            var lignes = Decompresser((byte[])l[0]);
            for (var k = lignes.Count - 1; k >= 0; k--)
            {
                var x = lignes[k];
                if (avantId is { } av && x.Id >= av) continue;
                if (voulus is not null && !voulus.Contains(x.Type)) continue;
                if (humain is { } hu && x.Humain != hu && x.Autre != hu) continue;
                if (communaute is { } co && x.Communaute != co && x.AutreCommunaute != co) continue;
                trouvees.Add(x);
            }
        }
        return trouvees;
    }

    /// <summary>Une ligne rangée dans un paquet, par son numéro (null si aucun paquet ne la contient).</summary>
    private static LigneJournal? LireArchive(SqliteConnection connexion, SqliteTransaction transaction, long id)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = "SELECT donnees FROM journal_blocs WHERE premier <= $id AND dernier >= $id;";
        commande.Parameters.AddWithValue("$id", id);
        if (commande.ExecuteScalar() is not byte[] donnees) return null;
        foreach (var x in Decompresser(donnees))
        {
            if (x.Id == id) return x;
        }
        return null;
    }

    /// <summary>Lignes du journal encore dans la table, et lignes rangées dans les paquets (pour le suivi de l'archivage).</summary>
    public (long Table, long Archivees) CompterJournal()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT (SELECT count(*) FROM journal), (SELECT coalesce(sum(lignes), 0) FROM journal_blocs);";
        using var l = commande.ExecuteReader();
        l.Read();
        return (l.GetInt64(0), l.GetInt64(1));
    }

    /// <summary>
    /// Rend au disque la place libérée (VACUUM) quand plus de la moitié du fichier est vide, ce qui arrive après l'archivage
    /// d'un gros journal. À faire au démarrage, avant que le monde ne tourne : le fichier est bloqué pendant ce temps.
    /// Rend la taille gagnée, en octets (0 si rien n'est fait).
    /// </summary>
    public long CompacterSiUtile(long minimumOctets = 64L << 20)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT page_count, freelist_count, page_size FROM pragma_page_count, pragma_freelist_count, pragma_page_size;";
        long pages, libres, taille;
        using (var l = commande.ExecuteReader())
        {
            l.Read();
            (pages, libres, taille) = (l.GetInt64(0), l.GetInt64(1), l.GetInt64(2));
        }
        if (libres * 2 < pages || libres * taille < minimumOctets) return 0;
        commande.CommandText = "VACUUM;";
        commande.CommandTimeout = 0;
        commande.ExecuteNonQuery();
        commande.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        commande.ExecuteNonQuery();
        return libres * taille;
    }

    /// <summary>
    /// Copie lisible par n'importe quel client SQL, pour analyser le monde : une copie du fichier où tout le journal (table
    /// et paquets) est remis en clair dans la table <c>journal</c>, avec la phrase rédigée par <paramref name="rediger"/>
    /// dans la colonne <c>texte_redige</c>. Avec <paramref name="debut"/> et <paramref name="fin"/> (secondes du monde),
    /// seul le journal de cette période est gardé. Rend le nombre de lignes du journal copié.
    /// </summary>
    public long CopierPourAnalyse(string chemin, Func<EvenementJournal, string> rediger, long? debut = null, long? fin = null)
    {
        CopierVers(chemin);
        var copie = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(chemin), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        long lignes = 0;
        using (var connexion = new SqliteConnection(copie))
        {
            connexion.Open();
            Executer(connexion, null, "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF;");
            using var transaction = connexion.BeginTransaction();
            var formes = FormesRecits.Lire(connexion, transaction);
            if (debut is { } d) Executer(connexion, transaction, $"DELETE FROM journal WHERE secondes < {d};");
            if (fin is { } f) Executer(connexion, transaction, $"DELETE FROM journal WHERE secondes > {f};");
            using (var ajout = Preparer(connexion, transaction, $"INSERT INTO journal ({ColonnesJournal}) VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15);",
                       [.. Enumerable.Range(0, 16).Select(i => $"${i}")]))
            using (var blocs = connexion.CreateCommand())
            {
                blocs.Transaction = transaction;
                blocs.CommandText = "SELECT donnees FROM journal_blocs WHERE ($d IS NULL OR fin >= $d) AND ($f IS NULL OR debut <= $f) ORDER BY premier;";
                blocs.Parameters.AddWithValue("$d", (object?)debut ?? DBNull.Value);
                blocs.Parameters.AddWithValue("$f", (object?)fin ?? DBNull.Value);
                using var l = blocs.ExecuteReader();
                while (l.Read())
                {
                    foreach (var x in Decompresser((byte[])l[0]))
                    {
                        if (x.Secondes < (debut ?? long.MinValue) || x.Secondes > (fin ?? long.MaxValue)) continue;
                        Lancer(ajout, x.Id, x.Secondes, (int)x.Type, x.Humain, x.Autre, x.Texte, (int)x.Origine, x.Cause, x.Communaute, x.AutreCommunaute,
                            x.Significatif, x.Karma, x.KarmaAutre, (object?)x.Forme ?? DBNull.Value, (object?)x.Valeurs ?? DBNull.Value, (int)x.Premiere);
                    }
                }
            }
            Executer(connexion, transaction, "DELETE FROM journal_blocs; DELETE FROM journal_blocs_cles; ALTER TABLE journal ADD COLUMN texte_redige TEXT;");

            // La phrase en clair, ligne par ligne, par tranches (un journal entier ne tient pas toujours en mémoire).
            using (var lire = connexion.CreateCommand())
            using (var ecrire = Preparer(connexion, transaction, "UPDATE journal SET texte_redige = $t WHERE id = $i;", "$t", "$i"))
            {
                lire.Transaction = transaction;
                lire.CommandText = $"SELECT {ColonnesJournal} FROM journal WHERE id > $apres ORDER BY id LIMIT 50000;";
                var apres = lire.Parameters.AddWithValue("$apres", 0L);
                while (true)
                {
                    var lus = new List<LigneJournal>();
                    using (var l = lire.ExecuteReader())
                    {
                        while (l.Read()) lus.Add(LireLigne(l));
                    }
                    if (lus.Count == 0) break;
                    foreach (var x in lus) Lancer(ecrire, rediger(VersEvenement(x, formes)), x.Id);
                    lignes += lus.Count;
                    apres.Value = lus[^1].Id;
                }
            }
            transaction.Commit();
        }
        using (var connexion = new SqliteConnection(copie))
        {
            connexion.Open();
            Executer(connexion, null, "VACUUM;");
        }
        return lignes;
    }
}
