using Microsoft.Data.Sqlite;

namespace Monde.Stockage;

/// <summary>
/// Un dialogue entre l'Entité (le joueur) et un humain joué par le LLM (0.36.2).
/// </summary>
/// <param name="Id">Numéro (0 avant l'enregistrement).</param>
/// <param name="Humain">L'humain à qui l'Entité parle.</param>
/// <param name="Mode">La voie (<c>ModeParole</c> : rêve, voix, apparition).</param>
/// <param name="Priere">La prière forte par laquelle il l'a appelée (0 : aucune).</param>
/// <param name="Debut">Date du monde (secondes) du début.</param>
/// <param name="Fin">Date du monde de la fin (0 : en cours).</param>
/// <param name="Quand">Heure réelle du début (UTC, ISO 8601).</param>
/// <param name="Modele">Fournisseur et modèle du LLM qui joue l'humain.</param>
/// <param name="Statut">0 en cours, 1 fini.</param>
/// <param name="FinRaison">Comment il a pris fin (<c>FinDialogue</c>).</param>
/// <param name="Repliques">Répliques de l'Entité.</param>
/// <param name="Croit">−1 : il n'a rien compris encore ; 0 : il n'y croit pas ; 1 : il croit parler à l'invisible.</param>
/// <param name="Comprise">Il a compris au moins une réplique.</param>
/// <param name="JournalFin">Ligne du journal de la fin (0 : aucune).</param>
public sealed record DialogueEntite(long Id, int Humain, int Mode, int Priere, long Debut, long Fin, string Quand, string Modele, int Statut, int FinRaison,
    int Repliques, int Croit, bool Comprise, long JournalFin);

/// <summary>
/// Une réplique : ce que l'Entité a dit, comment le LLM l'a traduit en paroles codées, ce que les règles en ont fait, et ce que
/// l'humain a répondu.
/// </summary>
/// <param name="Rang">1 pour la première.</param>
/// <param name="Le">Date du monde.</param>
/// <param name="Entite">Les mots de l'Entité.</param>
/// <param name="Traduction">Paroles codées (JSON : liste d'actes).</param>
/// <param name="Regles">Ce que les règles en ont fait (JSON : liste de textes).</param>
/// <param name="Mots">Les mots de l'humain (vides s'il n'a rien dit).</param>
/// <param name="Pensee">Ce qu'il pense sans le dire, selon le LLM.</param>
/// <param name="Libre">Sa réaction libre (acte libre en JSON, vide sans Entité libre) et ce qu'elle a donné.</param>
/// <param name="Juge">Ce qu'a dit le juge de cohérence (vide : pas appelé ou rien à redire).</param>
/// <param name="Lu">Ce qu'a lu le LLM pour faire parler l'humain.</param>
/// <param name="Reponse">Ses réponses brutes (traduction, puis l'humain).</param>
/// <param name="JournalPremier">Premier numéro du journal écrit par les règles (0 : aucun).</param>
/// <param name="JournalDernier">Dernier numéro (−1 : aucun).</param>
public sealed record RepliqueEntite(long Id, long Dialogue, int Rang, long Le, string Entite, string Traduction, string Regles, string Mots, string Pensee,
    string Libre, string Juge, string Lu, string Reponse, long DureeMs, long JournalPremier, long JournalDernier, string? Erreur);

/// <summary>Dialogues de l'Entité avec les humains (tables <c>dialogues</c> et <c>dialogue_repliques</c>).</summary>
public sealed partial class DepotMonde
{
    private const string ColonnesDialogues = "id, humain, mode, priere, debut, fin, quand, modele, statut, fin_raison, repliques, croit, comprise, journal_fin";

    private const string ColonnesRepliques =
        "id, dialogue, rang, le, entite, traduction, regles, mots, pensee, libre, juge, lu, reponse, duree_ms, journal_premier, journal_dernier, erreur";

    /// <summary>Migration vers la version 54 : dialogues avec les humains.</summary>
    private static void MigrerVersion54(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS dialogues (
                id          INTEGER PRIMARY KEY,
                humain      INTEGER NOT NULL,
                mode        INTEGER NOT NULL,
                priere      INTEGER NOT NULL,
                debut       INTEGER NOT NULL,
                fin         INTEGER NOT NULL,
                quand       TEXT    NOT NULL,
                modele      TEXT    NOT NULL,
                statut      INTEGER NOT NULL,
                fin_raison  INTEGER NOT NULL,
                repliques   INTEGER NOT NULL,
                croit       INTEGER NOT NULL,
                comprise    INTEGER NOT NULL,
                journal_fin INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS dialogues_humain ON dialogues (humain);
            CREATE TABLE IF NOT EXISTS dialogue_repliques (
                id              INTEGER PRIMARY KEY,
                dialogue        INTEGER NOT NULL,
                rang            INTEGER NOT NULL,
                le              INTEGER NOT NULL,
                entite          TEXT    NOT NULL,
                traduction      TEXT    NOT NULL,
                regles          TEXT    NOT NULL,
                mots            TEXT    NOT NULL,
                pensee          TEXT    NOT NULL,
                libre           TEXT    NOT NULL,
                juge            TEXT    NOT NULL,
                lu              BLOB    NOT NULL,
                reponse         TEXT    NOT NULL,
                duree_ms        INTEGER NOT NULL,
                journal_premier INTEGER NOT NULL,
                journal_dernier INTEGER NOT NULL,
                erreur          TEXT
            );
            CREATE INDEX IF NOT EXISTS dialogue_repliques_dialogue ON dialogue_repliques (dialogue, rang);
            """);
    }

    /// <summary>Enregistre un nouveau dialogue ; rend son numéro.</summary>
    public long AjouterDialogue(DialogueEntite d)
    {
        using var connexion = Ouvrir();
        using var c = connexion.CreateCommand();
        c.CommandText = $"INSERT INTO dialogues ({ColonnesDialogues[4..]}) VALUES ($h, $m, $p, $d, $f, $q, $mo, $s, $fr, $r, $c, $co, $j) RETURNING id;";
        ParametresDialogue(c, d);
        return Convert.ToInt64(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Met à jour un dialogue (fin, répliques, croyance).</summary>
    public void MettreAJourDialogue(DialogueEntite d)
    {
        using var connexion = Ouvrir();
        using var c = connexion.CreateCommand();
        c.CommandText = "UPDATE dialogues SET fin = $f, statut = $s, fin_raison = $fr, repliques = $r, croit = $c, comprise = $co, journal_fin = $j WHERE id = $id;";
        ParametresDialogue(c, d);
        c.Parameters.AddWithValue("$id", d.Id);
        c.ExecuteNonQuery();
    }

    private static void ParametresDialogue(SqliteCommand c, DialogueEntite d)
    {
        c.Parameters.AddWithValue("$h", d.Humain);
        c.Parameters.AddWithValue("$m", d.Mode);
        c.Parameters.AddWithValue("$p", d.Priere);
        c.Parameters.AddWithValue("$d", d.Debut);
        c.Parameters.AddWithValue("$f", d.Fin);
        c.Parameters.AddWithValue("$q", d.Quand);
        c.Parameters.AddWithValue("$mo", d.Modele);
        c.Parameters.AddWithValue("$s", d.Statut);
        c.Parameters.AddWithValue("$fr", d.FinRaison);
        c.Parameters.AddWithValue("$r", d.Repliques);
        c.Parameters.AddWithValue("$c", d.Croit);
        c.Parameters.AddWithValue("$co", d.Comprise ? 1 : 0);
        c.Parameters.AddWithValue("$j", d.JournalFin);
    }

    private static DialogueEntite LireDialogue(SqliteDataReader l) => new(l.GetInt64(0), l.GetInt32(1), l.GetInt32(2), l.GetInt32(3), l.GetInt64(4), l.GetInt64(5),
        l.GetString(6), l.GetString(7), l.GetInt32(8), l.GetInt32(9), l.GetInt32(10), l.GetInt32(11), l.GetInt32(12) != 0, l.GetInt64(13));

    /// <summary>Un dialogue (null s'il n'existe pas).</summary>
    public DialogueEntite? Dialogue(long id)
    {
        using var connexion = Ouvrir();
        using var c = connexion.CreateCommand();
        c.CommandText = $"SELECT {ColonnesDialogues} FROM dialogues WHERE id = $id;";
        c.Parameters.AddWithValue("$id", id);
        using var l = c.ExecuteReader();
        return l.Read() ? LireDialogue(l) : null;
    }

    /// <summary>Dialogues, du plus récent au plus ancien : tous, ou ceux d'un humain ; <paramref name="statut"/> : seulement ceux-là.</summary>
    public List<DialogueEntite> Dialogues(int humain = 0, int? statut = null, int limite = 200)
    {
        using var connexion = Ouvrir();
        using var c = connexion.CreateCommand();
        c.CommandText = $"SELECT {ColonnesDialogues} FROM dialogues WHERE ($h = 0 OR humain = $h) AND ($s IS NULL OR statut = $s) ORDER BY id DESC LIMIT $limite;";
        c.Parameters.AddWithValue("$h", humain);
        c.Parameters.AddWithValue("$s", (object?)statut ?? DBNull.Value);
        c.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 2000));
        using var l = c.ExecuteReader();
        var liste = new List<DialogueEntite>();
        while (l.Read()) liste.Add(LireDialogue(l));
        return liste;
    }

    /// <summary>Enregistre une réplique ; rend son numéro.</summary>
    public long AjouterReplique(RepliqueEntite r)
    {
        using var connexion = Ouvrir();
        using var c = connexion.CreateCommand();
        c.CommandText = $"""
            INSERT INTO dialogue_repliques ({ColonnesRepliques[4..]})
            VALUES ($d, $rang, $le, $e, $t, $re, $m, $p, $li, $j, $lu, $rep, $duree, $jp, $jd, $err) RETURNING id;
            """;
        c.Parameters.AddWithValue("$d", r.Dialogue);
        c.Parameters.AddWithValue("$rang", r.Rang);
        c.Parameters.AddWithValue("$le", r.Le);
        c.Parameters.AddWithValue("$e", r.Entite);
        c.Parameters.AddWithValue("$t", r.Traduction);
        c.Parameters.AddWithValue("$re", r.Regles);
        c.Parameters.AddWithValue("$m", r.Mots);
        c.Parameters.AddWithValue("$p", r.Pensee);
        c.Parameters.AddWithValue("$li", r.Libre);
        c.Parameters.AddWithValue("$j", r.Juge);
        c.Parameters.AddWithValue("$lu", Compresser(r.Lu));
        c.Parameters.AddWithValue("$rep", r.Reponse);
        c.Parameters.AddWithValue("$duree", r.DureeMs);
        c.Parameters.AddWithValue("$jp", r.JournalPremier);
        c.Parameters.AddWithValue("$jd", r.JournalDernier);
        c.Parameters.AddWithValue("$err", (object?)r.Erreur ?? DBNull.Value);
        return Convert.ToInt64(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Les répliques d'un dialogue, dans l'ordre ; <paramref name="avecLu"/> : avec ce qu'a lu le LLM.</summary>
    public List<RepliqueEntite> Repliques(long dialogue, bool avecLu = false)
    {
        using var connexion = Ouvrir();
        using var c = connexion.CreateCommand();
        c.CommandText = $"SELECT {ColonnesRepliques} FROM dialogue_repliques WHERE dialogue = $d ORDER BY rang, id;";
        c.Parameters.AddWithValue("$d", dialogue);
        using var l = c.ExecuteReader();
        var liste = new List<RepliqueEntite>();
        while (l.Read())
            liste.Add(new RepliqueEntite(l.GetInt64(0), l.GetInt64(1), l.GetInt32(2), l.GetInt64(3), l.GetString(4), l.GetString(5), l.GetString(6), l.GetString(7),
                l.GetString(8), l.GetString(9), l.GetString(10), avecLu ? DecompresserTexte((byte[])l[11]) : "", l.GetString(12), l.GetInt64(13), l.GetInt64(14),
                l.GetInt64(15), l.IsDBNull(16) ? null : l.GetString(16)));
        return liste;
    }

    /// <summary>Copie pour analyse : ce qu'a lu le LLM pour faire parler l'humain, en clair, dans une colonne de plus.</summary>
    private static void RepliquesEnClair(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "dialogue_repliques").Contains("lu_texte"))
            Executer(connexion, transaction, "ALTER TABLE dialogue_repliques ADD COLUMN lu_texte TEXT;");
        var lus = new List<(long, byte[])>();
        using (var lire = connexion.CreateCommand())
        {
            lire.Transaction = transaction;
            lire.CommandText = "SELECT id, lu FROM dialogue_repliques;";
            using var l = lire.ExecuteReader();
            while (l.Read()) lus.Add((l.GetInt64(0), (byte[])l[1]));
        }
        using var ecrire = Preparer(connexion, transaction, "UPDATE dialogue_repliques SET lu_texte = $t WHERE id = $i;", "$t", "$i");
        foreach (var (id, lu) in lus) Lancer(ecrire, DecompresserTexte(lu), id);
    }
}
