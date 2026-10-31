using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Monde.Stockage;

/// <summary>
/// Un échange entre le monde et l'Entité jouée par un LLM : ce qui l'a appelée, ce qu'elle a lu, ce qu'elle a répondu, et ce que
/// les règles du monde en ont fait.
/// </summary>
/// <param name="Id">Numéro (0 avant l'enregistrement).</param>
/// <param name="Le">Date du monde (secondes) où la demande est partie.</param>
/// <param name="Quand">Heure réelle (UTC, ISO 8601).</param>
/// <param name="Occasion">Ce qui a appelé l'Entité (voir <c>OccasionLlm</c> côté serveur).</param>
/// <param name="Humain">Humain concerné (0 : aucun).</param>
/// <param name="Communaute">Communauté concernée (0 : aucune).</param>
/// <param name="Priere">Prière à laquelle elle répond (0 : aucune).</param>
/// <param name="Modele">Fournisseur et modèle.</param>
/// <param name="DureeMs">Temps de réflexion du LLM (millisecondes, essais compris).</param>
/// <param name="Lu">Ce que le LLM a lu (consigne et demande).</param>
/// <param name="Reponse">Sa réponse brute (la dernière, s'il y a eu un second essai).</param>
/// <param name="Mots">Les mots que l'Entité dit (peuvent être vides).</param>
/// <param name="Pensee">Pourquoi elle agit ainsi, selon le LLM.</param>
/// <param name="Actes">Les actes codés (JSON).</param>
/// <param name="Resultats">Ce que les règles en ont fait, une ligne par acte (JSON : liste de textes).</param>
/// <param name="Statut">0 appliqué, 1 proposé (en attente du joueur), 2 rejeté par le joueur, 3 erreur, 4 arrivé trop tard, 5 rien à faire.</param>
/// <param name="JournalPremier">Premier numéro du journal écrit par ses actes (0 : aucun).</param>
/// <param name="JournalDernier">Dernier numéro du journal écrit par ses actes (−1 : aucun).</param>
/// <param name="Erreur">Ce qui n'a pas marché (null : rien).</param>
/// <param name="AppliqueLe">Date du monde où les actes ont été appliqués (0 : jamais).</param>
public sealed record EchangeEntite(long Id, long Le, string Quand, int Occasion, int Humain, int Communaute, int Priere, string Modele, long DureeMs,
    string Lu, string Reponse, string Mots, string Pensee, string Actes, string Resultats, int Statut, long JournalPremier, long JournalDernier,
    string? Erreur, long AppliqueLe);

/// <summary>Trace des conversations entre les humains et l'Entité jouée par un LLM (table <c>entite_echanges</c>).</summary>
public sealed partial class DepotMonde
{
    private const string ColonnesEchanges =
        "id, le, quand, occasion, humain, communaute, priere, modele, duree_ms, lu, reponse, mots, pensee, actes, resultats, statut, journal_premier, journal_dernier, erreur, applique_le";

    /// <summary>Migration vers la version 50 : trace des échanges avec l'Entité jouée par un LLM.</summary>
    private static void MigrerVersion50(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS entite_echanges (
                id              INTEGER PRIMARY KEY,
                le              INTEGER NOT NULL,
                quand           TEXT    NOT NULL,
                occasion        INTEGER NOT NULL,
                humain          INTEGER NOT NULL,
                communaute      INTEGER NOT NULL,
                priere          INTEGER NOT NULL,
                modele          TEXT    NOT NULL,
                duree_ms        INTEGER NOT NULL,
                lu              BLOB    NOT NULL,
                reponse         TEXT    NOT NULL,
                mots            TEXT    NOT NULL,
                pensee          TEXT    NOT NULL,
                actes           TEXT    NOT NULL,
                resultats       TEXT    NOT NULL,
                statut          INTEGER NOT NULL,
                journal_premier INTEGER NOT NULL,
                journal_dernier INTEGER NOT NULL,
                erreur          TEXT,
                applique_le     INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS entite_echanges_humain ON entite_echanges (humain);
            CREATE INDEX IF NOT EXISTS entite_echanges_communaute ON entite_echanges (communaute);
            """);
    }

    /// <summary>Texte compressé (Brotli) : ce que lit le LLM fait quelques milliers de mots, souvent répétés d'un échange à l'autre.</summary>
    internal static byte[] Compresser(string texte)
    {
        using var sortie = new MemoryStream();
        using (var b = new BrotliStream(sortie, CompressionLevel.Optimal, leaveOpen: true))
            b.Write(Encoding.UTF8.GetBytes(texte));
        return sortie.ToArray();
    }

    internal static string DecompresserTexte(byte[] donnees)
    {
        using var entree = new MemoryStream(donnees);
        using var b = new BrotliStream(entree, CompressionMode.Decompress);
        using var lecteur = new StreamReader(b, Encoding.UTF8);
        return lecteur.ReadToEnd();
    }

    /// <summary>Enregistre un nouvel échange ; rend son numéro.</summary>
    public long AjouterEchange(EchangeEntite e)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = $"INSERT INTO entite_echanges ({ColonnesEchanges[4..]}) VALUES ($le, $quand, $occasion, $humain, $communaute, $priere, $modele, $duree, $lu, $reponse, $mots, $pensee, $actes, $resultats, $statut, $jp, $jd, $erreur, $applique) RETURNING id;";
        Parametres(commande, e);
        return Convert.ToInt64(commande.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Met à jour un échange (statut, résultats, numéros du journal) ; ce qu'il a lu ne change pas.</summary>
    public void MettreAJourEchange(EchangeEntite e)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = """
            UPDATE entite_echanges SET resultats = $resultats, statut = $statut, journal_premier = $jp, journal_dernier = $jd, erreur = $erreur,
                applique_le = $applique WHERE id = $id;
            """;
        commande.Parameters.AddWithValue("$id", e.Id);
        commande.Parameters.AddWithValue("$resultats", e.Resultats);
        commande.Parameters.AddWithValue("$statut", e.Statut);
        commande.Parameters.AddWithValue("$jp", e.JournalPremier);
        commande.Parameters.AddWithValue("$jd", e.JournalDernier);
        commande.Parameters.AddWithValue("$erreur", (object?)e.Erreur ?? DBNull.Value);
        commande.Parameters.AddWithValue("$applique", e.AppliqueLe);
        commande.ExecuteNonQuery();
    }

    private static void Parametres(SqliteCommand c, EchangeEntite e)
    {
        c.Parameters.AddWithValue("$le", e.Le);
        c.Parameters.AddWithValue("$quand", e.Quand);
        c.Parameters.AddWithValue("$occasion", e.Occasion);
        c.Parameters.AddWithValue("$humain", e.Humain);
        c.Parameters.AddWithValue("$communaute", e.Communaute);
        c.Parameters.AddWithValue("$priere", e.Priere);
        c.Parameters.AddWithValue("$modele", e.Modele);
        c.Parameters.AddWithValue("$duree", e.DureeMs);
        c.Parameters.AddWithValue("$lu", Compresser(e.Lu));
        c.Parameters.AddWithValue("$reponse", e.Reponse);
        c.Parameters.AddWithValue("$mots", e.Mots);
        c.Parameters.AddWithValue("$pensee", e.Pensee);
        c.Parameters.AddWithValue("$actes", e.Actes);
        c.Parameters.AddWithValue("$resultats", e.Resultats);
        c.Parameters.AddWithValue("$statut", e.Statut);
        c.Parameters.AddWithValue("$jp", e.JournalPremier);
        c.Parameters.AddWithValue("$jd", e.JournalDernier);
        c.Parameters.AddWithValue("$erreur", (object?)e.Erreur ?? DBNull.Value);
        c.Parameters.AddWithValue("$applique", e.AppliqueLe);
    }

    private static EchangeEntite LireEchange(SqliteDataReader l, bool avecLu) => new(
        l.GetInt64(0), l.GetInt64(1), l.GetString(2), l.GetInt32(3), l.GetInt32(4), l.GetInt32(5), l.GetInt32(6), l.GetString(7), l.GetInt64(8),
        avecLu ? DecompresserTexte((byte[])l[9]) : "", l.GetString(10), l.GetString(11), l.GetString(12), l.GetString(13), l.GetString(14), l.GetInt32(15),
        l.GetInt64(16), l.GetInt64(17), l.IsDBNull(18) ? null : l.GetString(18), l.GetInt64(19));

    /// <summary>
    /// Échanges, du plus récent au plus ancien : tous, ou ceux d'un humain, d'une communauté, d'un statut. Sans ce qu'a lu le LLM
    /// (voir <see cref="Echange"/>). <paramref name="avant"/> : numéro à ne pas atteindre (pagination, 0 : depuis le dernier).
    /// </summary>
    public List<EchangeEntite> Echanges(int humain = 0, int communaute = 0, int? statut = null, long avant = 0, int limite = 100)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = $"""
            SELECT {ColonnesEchanges} FROM entite_echanges
            WHERE ($h = 0 OR humain = $h) AND ($c = 0 OR communaute = $c) AND ($s IS NULL OR statut = $s) AND ($avant = 0 OR id < $avant)
            ORDER BY id DESC LIMIT $limite;
            """;
        commande.Parameters.AddWithValue("$h", humain);
        commande.Parameters.AddWithValue("$c", communaute);
        commande.Parameters.AddWithValue("$s", (object?)statut ?? DBNull.Value);
        commande.Parameters.AddWithValue("$avant", avant);
        commande.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 1000));
        using var l = commande.ExecuteReader();
        var liste = new List<EchangeEntite>();
        while (l.Read()) liste.Add(LireEchange(l, avecLu: false));
        return liste;
    }

    /// <summary>Un échange complet, ce qu'a lu le LLM compris (null s'il n'existe pas).</summary>
    public EchangeEntite? Echange(long id)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = $"SELECT {ColonnesEchanges} FROM entite_echanges WHERE id = $id;";
        commande.Parameters.AddWithValue("$id", id);
        using var l = commande.ExecuteReader();
        return l.Read() ? LireEchange(l, avecLu: true) : null;
    }

    /// <summary>Plages du journal écrites par les actes du LLM (pour marquer ces lignes).</summary>
    public List<(long Premier, long Dernier)> PlagesJournalLlm()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT journal_premier, journal_dernier FROM entite_echanges WHERE journal_dernier >= journal_premier AND journal_premier > 0 ORDER BY journal_premier;";
        using var l = commande.ExecuteReader();
        var liste = new List<(long, long)>();
        while (l.Read()) liste.Add((l.GetInt64(0), l.GetInt64(1)));
        return liste;
    }

    /// <summary>Copie pour analyse : ce qu'a lu le LLM, en clair, dans une colonne de plus.</summary>
    private static void EchangesEnClair(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "entite_echanges").Contains("lu_texte"))
            Executer(connexion, transaction, "ALTER TABLE entite_echanges ADD COLUMN lu_texte TEXT;");
        var lus = new List<(long, byte[])>();
        using (var lire = connexion.CreateCommand())
        {
            lire.Transaction = transaction;
            lire.CommandText = "SELECT id, lu FROM entite_echanges;";
            using var l = lire.ExecuteReader();
            while (l.Read()) lus.Add((l.GetInt64(0), (byte[])l[1]));
        }
        using var ecrire = Preparer(connexion, transaction, "UPDATE entite_echanges SET lu_texte = $t WHERE id = $i;", "$t", "$i");
        foreach (var (id, lu) in lus) Lancer(ecrire, DecompresserTexte(lu), id);
    }
}
