using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Les champs (0.33.0) : parcelles semées, lignées cultivées, et ce que chaque communauté pense des champs ; les bêtes gardées (0.33.1).</summary>
public sealed partial class DepotMonde
{
    /// <summary>Ce qu'une communauté pense des champs, tel qu'on l'enregistre.</summary>
    private sealed record IdeeEnregistree(IdeeReserve Idee, long Le, long EvenementId, double Partages, double Vols, long VolEvenementId,
        double Rodeurs = 0, long RodeursEvenementId = 0);

    /// <summary>Migration vers la version 46 : champs. Les mondes existants n'en ont aucun.</summary>
    private static void MigrerVersion46(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS parcelles (
                id          INTEGER PRIMARY KEY,
                x           INTEGER NOT NULL,
                y           INTEGER NOT NULL,
                espece      INTEGER NOT NULL,
                communaute  INTEGER NOT NULL,
                donnees     TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS lignees (
                id          INTEGER PRIMARY KEY,
                espece      INTEGER NOT NULL,
                communaute  INTEGER NOT NULL,
                donnees     TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS champs_idees (
                communaute  INTEGER PRIMARY KEY,
                donnees     TEXT NOT NULL
            );
            """);
    }

    /// <summary>Migration vers la version 47 : bêtes gardées, et diffusion des savoirs dans la courbe du progrès (0.33.1).</summary>
    private static void MigrerVersion47(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS troupeaux (
                id          INTEGER PRIMARY KEY,
                espece      INTEGER NOT NULL,
                communaute  INTEGER NOT NULL,
                donnees     TEXT NOT NULL
            );
            """);
        if (!Colonnes(connexion, transaction, "progres").Contains("par_humain"))
            Executer(connexion, transaction, "ALTER TABLE progres ADD COLUMN par_humain REAL NOT NULL DEFAULT -1;");
    }

    private static List<Troupeau> LireTroupeaux(SqliteCommand commande) =>
        LireJson<Troupeau>(commande, "SELECT donnees FROM troupeaux ORDER BY id;",
            t => t.Id > 0 && t.Espece > 0 && double.IsFinite(t.Adultes) && double.IsFinite(t.Petits) && double.IsFinite(t.Docilite));

    private static List<T> LireJson<T>(SqliteCommand commande, string requete, Func<T, bool> valide)
    {
        var liste = new List<T>();
        commande.CommandText = requete;
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(l.GetString(0)) is { } x && valide(x)) liste.Add(x);
            }
            catch (JsonException)
            {
                // Une ligne illisible est oubliée.
            }
        }
        return liste;
    }

    private static (List<Parcelle> Parcelles, List<Lignee> Lignees, List<(int, IdeeDesChamps)> Idees) LireChamps(SqliteCommand commande)
    {
        var lignees = LireJson<Lignee>(commande, "SELECT donnees FROM lignees ORDER BY id;",
            l => l.Id > 0 && l.Saisons is { Length: 4 } && double.IsFinite(l.Semence) && double.IsFinite(l.Tenaces));
        var ids = lignees.Select(l => l.Id).ToHashSet();
        var parcelles = LireJson<Parcelle>(commande, "SELECT donnees FROM parcelles ORDER BY id;",
            p => p.Id > 0 && Enum.IsDefined(p.Etat) && ids.Contains(p.LigneeId) && double.IsFinite(p.Ares));
        var idees = new List<(int, IdeeDesChamps)>();
        commande.CommandText = "SELECT communaute, donnees FROM champs_idees ORDER BY communaute;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                try
                {
                    if (JsonSerializer.Deserialize<IdeeEnregistree>(l.GetString(1)) is { } i && Enum.IsDefined(i.Idee))
                        idees.Add((l.GetInt32(0), new IdeeDesChamps
                        {
                            Idee = i.Idee, Le = i.Le, EvenementId = i.EvenementId, Partages = i.Partages, Vols = i.Vols, VolEvenementId = i.VolEvenementId,
                            Rodeurs = i.Rodeurs, RodeursEvenementId = i.RodeursEvenementId,
                        }));
                }
                catch (JsonException)
                {
                    // Oubliée.
                }
            }
        }
        return (parcelles, lignees, idees);
    }

    private static void EnregistrerChamps(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, "DELETE FROM parcelles; DELETE FROM lignees; DELETE FROM champs_idees; DELETE FROM troupeaux;");
        using (var c = Preparer(connexion, transaction, "INSERT INTO lignees (id, espece, communaute, donnees) VALUES ($i, $e, $c, $d);", "$i", "$e", "$c", "$d"))
            foreach (var l in etat.Lignees ?? []) Lancer(c, l.Id, l.Espece, l.CommunauteId, JsonSerializer.Serialize(l));
        using (var c = Preparer(connexion, transaction, "INSERT INTO parcelles (id, x, y, espece, communaute, donnees) VALUES ($i, $x, $y, $e, $c, $d);",
                   "$i", "$x", "$y", "$e", "$c", "$d"))
            foreach (var p in etat.Parcelles ?? []) Lancer(c, p.Id, p.X, p.Y, p.Espece, p.CommunauteId, JsonSerializer.Serialize(p));
        using (var c = Preparer(connexion, transaction, "INSERT INTO champs_idees (communaute, donnees) VALUES ($c, $d);", "$c", "$d"))
            foreach (var (id, i) in etat.IdeesChamps ?? [])
                Lancer(c, id, JsonSerializer.Serialize(new IdeeEnregistree(i.Idee, i.Le, i.EvenementId, i.Partages, i.Vols, i.VolEvenementId, i.Rodeurs, i.RodeursEvenementId)));
        using (var c = Preparer(connexion, transaction, "INSERT INTO troupeaux (id, espece, communaute, donnees) VALUES ($i, $e, $c, $d);", "$i", "$e", "$c", "$d"))
            foreach (var t in etat.Troupeaux ?? []) Lancer(c, t.Id, t.Espece, t.CommunauteId, JsonSerializer.Serialize(t));
    }
}
