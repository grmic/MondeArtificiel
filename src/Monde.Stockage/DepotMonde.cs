using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>
/// Sauvegarde d'un monde dans un fichier SQLite. Un monde = un fichier,
/// ce qui rendra les snapshots et les forks simples (copie du fichier).
/// Toutes les requêtes sont paramétrées ; une sauvegarde complète tient dans une transaction.
/// </summary>
public sealed class DepotMonde
{
    /// <summary>Version du schéma, stockée dans PRAGMA user_version.</summary>
    public const int VersionSchema = 4;

    private readonly string _connexion;

    public DepotMonde(string cheminFichier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cheminFichier);
        CheminFichier = Path.GetFullPath(cheminFichier);
        Directory.CreateDirectory(Path.GetDirectoryName(CheminFichier)!);
        _connexion = new SqliteConnectionStringBuilder
        {
            DataSource = CheminFichier,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, // le fichier est libéré dès la fermeture (copie, fork)
        }.ToString();
        Initialiser();
    }

    public string CheminFichier { get; }

    private SqliteConnection Ouvrir()
    {
        var connexion = new SqliteConnection(_connexion);
        connexion.Open();
        return connexion;
    }

    private static void Executer(SqliteConnection connexion, SqliteTransaction? transaction, string sql)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = sql;
        commande.ExecuteNonQuery();
    }

    private void Initialiser()
    {
        using var connexion = Ouvrir();
        Executer(connexion, null, "PRAGMA journal_mode = WAL;");

        using var version = connexion.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        var actuelle = Convert.ToInt32(version.ExecuteScalar());
        if (actuelle > VersionSchema)
        {
            throw new InvalidOperationException(
                $"Le fichier {CheminFichier} vient d'une version plus récente de l'application (schéma {actuelle}).");
        }
        if (actuelle == VersionSchema) return;

        using var transaction = connexion.BeginTransaction();
        if (actuelle < 1)
        {
            Executer(connexion, transaction, """
                CREATE TABLE etat (
                    id          INTEGER PRIMARY KEY CHECK (id = 1),
                    graine      INTEGER NOT NULL,
                    secondes    INTEGER NOT NULL CHECK (secondes >= 0),
                    pas         INTEGER NOT NULL CHECK (pas >= 0),
                    vitesse     INTEGER NOT NULL,
                    modifie_le  TEXT    NOT NULL
                );
                """);
        }
        if (actuelle < 2)
        {
            // Version 2 : humains, ressources des tuiles et état du générateur aléatoire.
            Executer(connexion, transaction, """
                ALTER TABLE etat ADD COLUMN alea0 INTEGER;
                ALTER TABLE etat ADD COLUMN alea1 INTEGER;
                ALTER TABLE etat ADD COLUMN alea2 INTEGER;
                ALTER TABLE etat ADD COLUMN alea3 INTEGER;
                CREATE TABLE ressources (
                    id      INTEGER PRIMARY KEY CHECK (id = 1),
                    gibier  BLOB NOT NULL,
                    plantes BLOB NOT NULL
                );
                CREATE TABLE humains (
                    id            INTEGER PRIMARY KEY,
                    nom           TEXT    NOT NULL,
                    sexe          INTEGER NOT NULL,
                    partenaire    INTEGER NOT NULL,
                    emplacement   INTEGER NOT NULL,
                    naissance     INTEGER NOT NULL,
                    curiosite     REAL    NOT NULL,
                    prudence      REAL    NOT NULL,
                    sociabilite   REAL    NOT NULL,
                    endurance     REAL    NOT NULL,
                    adresse       REAL    NOT NULL,
                    x             INTEGER NOT NULL,
                    y             INTEGER NOT NULL,
                    faim          REAL    NOT NULL,
                    soif          REAL    NOT NULL,
                    fatigue       REAL    NOT NULL,
                    froid         REAL    NOT NULL,
                    sante         REAL    NOT NULL,
                    activite      INTEGER NOT NULL,
                    cible_x       INTEGER NOT NULL,
                    cible_y       INTEGER NOT NULL,
                    detour        INTEGER NOT NULL,
                    malade_jusqua INTEGER NOT NULL,
                    mort_le       INTEGER,
                    cause_mort    INTEGER NOT NULL
                );
                """);
        }
        if (actuelle < 3)
        {
            // Version 3 : filiation, désir et grossesse.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN mere INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN pere INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN desir REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN enceinte_depuis INTEGER;
                ALTER TABLE humains ADD COLUMN pere_enfant INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN dernier_accouchement INTEGER;
                """);
        }
        if (actuelle < 4)
        {
            // Version 4 : attachement aux enfants, biologie propre à chacun, terme de la grossesse.
            // Valeurs nulles pour les humains déjà enregistrés : des valeurs par défaut sont prises au chargement.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN attachement REAL;
                ALTER TABLE humains ADD COLUMN puberte REAL;
                ALTER TABLE humains ADD COLUMN fin_fertilite REAL;
                ALTER TABLE humains ADD COLUMN fertilite REAL;
                ALTER TABLE humains ADD COLUMN terme_grossesse INTEGER;
                """);
        }
        Executer(connexion, transaction, $"PRAGMA user_version = {VersionSchema};");
        transaction.Commit();
    }

    /// <summary>Renvoie l'état enregistré, ou null si le monde n'a jamais été sauvegardé.</summary>
    public EtatMonde? Charger()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT graine, secondes, pas, vitesse, alea0, alea1, alea2, alea3 FROM etat WHERE id = 1;";
        EtatMonde etat;
        using (var lecteur = commande.ExecuteReader())
        {
            if (!lecteur.Read()) return null;
            etat = new EtatMonde(
                unchecked((ulong)lecteur.GetInt64(0)),
                lecteur.GetInt64(1),
                lecteur.GetInt64(2),
                lecteur.GetInt32(3))
            {
                Alea = lecteur.IsDBNull(4) ? null : [.. Enumerable.Range(4, 4).Select(i => unchecked((ulong)lecteur.GetInt64(i)))],
            };
        }

        commande.CommandText = "SELECT gibier, plantes FROM ressources WHERE id = 1;";
        using (var lecteur = commande.ExecuteReader())
        {
            if (lecteur.Read())
            {
                etat = etat with { Gibier = VersUShort((byte[])lecteur[0]), Plantes = VersUShort((byte[])lecteur[1]) };
            }
        }

        commande.CommandText = """
            SELECT id, nom, sexe, partenaire, emplacement, naissance,
                   curiosite, prudence, sociabilite, endurance, adresse,
                   x, y, faim, soif, fatigue, froid, sante, activite,
                   cible_x, cible_y, detour, malade_jusqua, mort_le, cause_mort,
                   mere, pere, desir, enceinte_depuis, pere_enfant, dernier_accouchement,
                   attachement, puberte, fin_fertilite, fertilite, terme_grossesse
            FROM humains ORDER BY id;
            """;
        var humains = new List<Humain>();
        using (var lecteur = commande.ExecuteReader())
        {
            while (lecteur.Read())
            {
                var sexe = (Sexe)lecteur.GetByte(2);
                var biologie = lecteur.IsDBNull(32)
                    ? Biologie.ParDefaut(sexe)
                    : new Biologie(lecteur.GetDouble(32), lecteur.GetDouble(33), lecteur.GetDouble(34));
                humains.Add(new Humain
                {
                    Id = lecteur.GetInt32(0),
                    Nom = lecteur.GetString(1),
                    Sexe = sexe,
                    Biologie = biologie,
                    PartenaireId = lecteur.GetInt32(3),
                    Emplacement = lecteur.GetInt32(4),
                    Naissance = lecteur.GetInt64(5),
                    Personnalite = new Personnalite(lecteur.GetDouble(6), lecteur.GetDouble(7), lecteur.GetDouble(8),
                        lecteur.GetDouble(9), lecteur.GetDouble(10), lecteur.IsDBNull(31) ? 0.8 : lecteur.GetDouble(31)),
                    X = lecteur.GetInt32(11),
                    Y = lecteur.GetInt32(12),
                    Faim = lecteur.GetDouble(13),
                    Soif = lecteur.GetDouble(14),
                    Fatigue = lecteur.GetDouble(15),
                    Froid = lecteur.GetDouble(16),
                    Sante = lecteur.GetDouble(17),
                    Activite = (Activite)lecteur.GetByte(18),
                    CibleX = lecteur.GetInt32(19),
                    CibleY = lecteur.GetInt32(20),
                    Detour = lecteur.GetBoolean(21),
                    MaladeJusqua = lecteur.GetInt64(22),
                    MortLe = lecteur.IsDBNull(23) ? null : lecteur.GetInt64(23),
                    CauseDeMort = (CauseDeMort)lecteur.GetByte(24),
                    MereId = lecteur.GetInt32(25),
                    PereId = lecteur.GetInt32(26),
                    Desir = lecteur.GetDouble(27),
                    EnceinteDepuis = lecteur.IsDBNull(28) ? null : lecteur.GetInt64(28),
                    PereEnfantId = lecteur.GetInt32(29),
                    DernierAccouchement = lecteur.IsDBNull(30) ? null : lecteur.GetInt64(30),
                    TermeGrossesse = lecteur.IsDBNull(35) ? null : lecteur.GetInt64(35),
                });
            }
        }
        return humains.Count > 0 ? etat with { Humains = humains } : etat;
    }

    /// <summary>
    /// Enregistre l'état. Avec <paramref name="remplacer"/>, l'ancien monde est d'abord entièrement effacé
    /// (dans la même transaction : en cas d'échec, l'ancien monde reste intact).
    /// </summary>
    public void Enregistrer(EtatMonde etat, bool remplacer = false)
    {
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        if (remplacer) Executer(connexion, transaction, "DELETE FROM humains; DELETE FROM ressources; DELETE FROM etat;");

        using (var commande = connexion.CreateCommand())
        {
            commande.Transaction = transaction;
            commande.CommandText = """
                INSERT INTO etat (id, graine, secondes, pas, vitesse, modifie_le, alea0, alea1, alea2, alea3)
                VALUES (1, $graine, $secondes, $pas, $vitesse, $modifie, $a0, $a1, $a2, $a3)
                ON CONFLICT (id) DO UPDATE SET
                    graine = excluded.graine, secondes = excluded.secondes, pas = excluded.pas,
                    vitesse = excluded.vitesse, modifie_le = excluded.modifie_le,
                    alea0 = excluded.alea0, alea1 = excluded.alea1, alea2 = excluded.alea2, alea3 = excluded.alea3;
                """;
            commande.Parameters.AddWithValue("$graine", unchecked((long)etat.Graine));
            commande.Parameters.AddWithValue("$secondes", etat.Secondes);
            commande.Parameters.AddWithValue("$pas", etat.Pas);
            commande.Parameters.AddWithValue("$vitesse", etat.NiveauVitesse);
            commande.Parameters.AddWithValue("$modifie", DateTimeOffset.UtcNow.ToString("O"));
            for (var i = 0; i < 4; i++)
            {
                commande.Parameters.AddWithValue($"$a{i}", etat.Alea is { } a ? unchecked((long)a[i]) : DBNull.Value);
            }
            commande.ExecuteNonQuery();
        }

        if (etat.Gibier is { } gibier && etat.Plantes is { } plantes)
        {
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = """
                INSERT INTO ressources (id, gibier, plantes) VALUES (1, $gibier, $plantes)
                ON CONFLICT (id) DO UPDATE SET gibier = excluded.gibier, plantes = excluded.plantes;
                """;
            commande.Parameters.AddWithValue("$gibier", VersOctets(gibier));
            commande.Parameters.AddWithValue("$plantes", VersOctets(plantes));
            commande.ExecuteNonQuery();
        }

        if (etat.Humains is { } humains)
        {
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = """
                INSERT OR REPLACE INTO humains (id, nom, sexe, partenaire, emplacement, naissance,
                    curiosite, prudence, sociabilite, endurance, adresse,
                    x, y, faim, soif, fatigue, froid, sante, activite,
                    cible_x, cible_y, detour, malade_jusqua, mort_le, cause_mort,
                    mere, pere, desir, enceinte_depuis, pere_enfant, dernier_accouchement,
                    attachement, puberte, fin_fertilite, fertilite, terme_grossesse)
                VALUES ($id, $nom, $sexe, $partenaire, $emplacement, $naissance,
                    $curiosite, $prudence, $sociabilite, $endurance, $adresse,
                    $x, $y, $faim, $soif, $fatigue, $froid, $sante, $activite,
                    $cible_x, $cible_y, $detour, $malade_jusqua, $mort_le, $cause_mort,
                    $mere, $pere, $desir, $enceinte_depuis, $pere_enfant, $dernier_accouchement,
                    $attachement, $puberte, $fin_fertilite, $fertilite, $terme_grossesse);
                """;
            string[] noms = ["$id", "$nom", "$sexe", "$partenaire", "$emplacement", "$naissance",
                "$curiosite", "$prudence", "$sociabilite", "$endurance", "$adresse",
                "$x", "$y", "$faim", "$soif", "$fatigue", "$froid", "$sante", "$activite",
                "$cible_x", "$cible_y", "$detour", "$malade_jusqua", "$mort_le", "$cause_mort",
                "$mere", "$pere", "$desir", "$enceinte_depuis", "$pere_enfant", "$dernier_accouchement",
                "$attachement", "$puberte", "$fin_fertilite", "$fertilite", "$terme_grossesse"];
            var parametres = noms.Select(n => commande.Parameters.Add(new SqliteParameter { ParameterName = n })).ToArray();
            commande.Prepare();
            foreach (var h in humains)
            {
                object[] valeurs =
                [
                    h.Id, h.Nom, (int)h.Sexe, h.PartenaireId, h.Emplacement, h.Naissance,
                    h.Personnalite.Curiosite, h.Personnalite.Prudence, h.Personnalite.Sociabilite,
                    h.Personnalite.Endurance, h.Personnalite.Adresse,
                    h.X, h.Y, h.Faim, h.Soif, h.Fatigue, h.Froid, h.Sante, (int)h.Activite,
                    h.CibleX, h.CibleY, h.Detour, h.MaladeJusqua, h.MortLe is { } m ? m : DBNull.Value, (int)h.CauseDeMort,
                    h.MereId, h.PereId, h.Desir, h.EnceinteDepuis is { } e ? e : DBNull.Value, h.PereEnfantId,
                    h.DernierAccouchement is { } d ? d : DBNull.Value,
                    h.Personnalite.Attachement, h.Biologie.AgePuberte, h.Biologie.AgeFinFertilite, h.Biologie.Fertilite,
                    h.TermeGrossesse is { } t ? t : DBNull.Value,
                ];
                for (var i = 0; i < valeurs.Length; i++) parametres[i].Value = valeurs[i];
                commande.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <summary>
    /// Copie cohérente du monde dans un autre fichier (VACUUM INTO), même pendant que le serveur écrit.
    /// Un fichier existant à cet emplacement est remplacé.
    /// </summary>
    public void CopierVers(string cheminCopie)
    {
        var complet = Path.GetFullPath(cheminCopie);
        if (string.Equals(complet, Path.GetFullPath(CheminFichier), StringComparison.Ordinal))
        {
            throw new ArgumentException("La copie doit aller dans un autre fichier.", nameof(cheminCopie));
        }
        File.Delete(complet); // VACUUM INTO refuse d'écrire sur un fichier existant
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "VACUUM INTO $chemin;";
        commande.Parameters.AddWithValue("$chemin", complet);
        commande.ExecuteNonQuery();
    }

    // Les ressources sont stockées en petit-boutiste, quel que soit le processeur.
    private static byte[] VersOctets(ushort[] valeurs)
    {
        var octets = new byte[valeurs.Length * 2];
        for (var i = 0; i < valeurs.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(octets.AsSpan(i * 2), valeurs[i]);
        return octets;
    }

    private static ushort[] VersUShort(byte[] octets)
    {
        if (octets.Length % 2 != 0) throw new InvalidDataException("Ressources corrompues dans la sauvegarde.");
        var valeurs = new ushort[octets.Length / 2];
        for (var i = 0; i < valeurs.Length; i++) valeurs[i] = BinaryPrimitives.ReadUInt16LittleEndian(octets.AsSpan(i * 2));
        return valeurs;
    }
}
