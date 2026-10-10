using Microsoft.Data.Sqlite;
using Monde.Simulation;

namespace Monde.Stockage;

/// <summary>
/// Sauvegarde d'un monde dans un fichier SQLite. Un monde = un fichier,
/// ce qui rendra les snapshots et les forks simples (copie du fichier).
/// Toutes les requêtes sont paramétrées.
/// </summary>
public sealed class DepotMonde
{
    /// <summary>Version du schéma, stockée dans PRAGMA user_version.</summary>
    public const int VersionSchema = 1;

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

    private void Initialiser()
    {
        using var connexion = Ouvrir();
        using (var wal = connexion.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }

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
        using var creation = connexion.CreateCommand();
        creation.Transaction = transaction;
        creation.CommandText = """
            CREATE TABLE IF NOT EXISTS etat (
                id          INTEGER PRIMARY KEY CHECK (id = 1),
                graine      INTEGER NOT NULL,
                secondes    INTEGER NOT NULL CHECK (secondes >= 0),
                pas         INTEGER NOT NULL CHECK (pas >= 0),
                vitesse     INTEGER NOT NULL,
                modifie_le  TEXT    NOT NULL
            );
            PRAGMA user_version = 1;
            """;
        creation.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Renvoie l'état enregistré, ou null si le monde n'a jamais été sauvegardé.</summary>
    public EtatMonde? Charger()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT graine, secondes, pas, vitesse FROM etat WHERE id = 1;";
        using var lecteur = commande.ExecuteReader();
        if (!lecteur.Read()) return null;
        return new EtatMonde(
            unchecked((ulong)lecteur.GetInt64(0)),
            lecteur.GetInt64(1),
            lecteur.GetInt64(2),
            lecteur.GetInt32(3));
    }

    public void Enregistrer(EtatMonde etat)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = """
            INSERT INTO etat (id, graine, secondes, pas, vitesse, modifie_le)
            VALUES (1, $graine, $secondes, $pas, $vitesse, $modifie)
            ON CONFLICT (id) DO UPDATE SET
                graine = excluded.graine,
                secondes = excluded.secondes,
                pas = excluded.pas,
                vitesse = excluded.vitesse,
                modifie_le = excluded.modifie_le;
            """;
        commande.Parameters.AddWithValue("$graine", unchecked((long)etat.Graine));
        commande.Parameters.AddWithValue("$secondes", etat.Secondes);
        commande.Parameters.AddWithValue("$pas", etat.Pas);
        commande.Parameters.AddWithValue("$vitesse", etat.NiveauVitesse);
        commande.Parameters.AddWithValue("$modifie", DateTimeOffset.UtcNow.ToString("O"));
        commande.ExecuteNonQuery();
    }
}
