using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Braises qui couvent, réserves d'eau et de nourriture, et ce que les humains et les communautés savent du feu.</summary>
public sealed partial class DepotMonde
{
    /// <summary>
    /// Migration vers la version 32 : feux couverts et dégagés, braises, réserves, porteurs du feu, idées des réserves, incendies
    /// dont on se souvient et responsables des feux de camp échappés. Les mondes existants n'en ont pas encore : tout part de zéro.
    /// </summary>
    private static void MigrerVersion32(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var foyers = Colonnes(connexion, transaction, "foyers");
        if (!foyers.Contains("couvert")) Executer(connexion, transaction, "ALTER TABLE foyers ADD COLUMN couvert INTEGER NOT NULL DEFAULT 0;");
        if (!foyers.Contains("degage")) Executer(connexion, transaction, "ALTER TABLE foyers ADD COLUMN degage INTEGER NOT NULL DEFAULT 0;");
        if (!foyers.Contains("entretenu")) Executer(connexion, transaction, "ALTER TABLE foyers ADD COLUMN entretenu INTEGER NOT NULL DEFAULT 0;");
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS braises (
                id        INTEGER PRIMARY KEY,
                x         INTEGER NOT NULL,
                y         INTEGER NOT NULL,
                heures    REAL    NOT NULL,
                couvert   INTEGER NOT NULL,
                allume_le INTEGER NOT NULL,
                allumeur  INTEGER NOT NULL,
                evenement INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS reserves (
                id         INTEGER PRIMARY KEY,
                x          INTEGER NOT NULL,
                y          INTEGER NOT NULL,
                sorte      INTEGER NOT NULL,
                kg         REAL    NOT NULL,
                etat       REAL    NOT NULL,
                qualite    REAL    NOT NULL,
                faite_le   INTEGER NOT NULL,
                auteur     INTEGER NOT NULL,
                communaute INTEGER NOT NULL,
                evenement  INTEGER NOT NULL
            );
            """);
        var humains = Colonnes(connexion, transaction, "humains");
        if (!humains.Contains("feux_portes")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN feux_portes INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("porteur_du_feu_depuis")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN porteur_du_feu_depuis INTEGER NOT NULL DEFAULT 0;");
        if (!humains.Contains("feu_demande_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN feu_demande_le INTEGER NOT NULL DEFAULT 0;");
        var communautes = Colonnes(connexion, transaction, "communautes");
        foreach (var (nom, type) in new[]
                 {
                     ("incendies_souvenus", "TEXT NOT NULL DEFAULT ''"), ("idee_reserve", "INTEGER NOT NULL DEFAULT 0"),
                     ("idee_reserve_le", "INTEGER NOT NULL DEFAULT 0"), ("idee_reserve_evenement", "INTEGER NOT NULL DEFAULT 0"),
                     ("partages_reserve", "REAL NOT NULL DEFAULT 0"), ("vols_reserve", "REAL NOT NULL DEFAULT 0"),
                     ("vol_reserve_evenement", "INTEGER NOT NULL DEFAULT 0"),
                 })
        {
            if (!communautes.Contains(nom)) Executer(connexion, transaction, $"ALTER TABLE communautes ADD COLUMN {nom} {type};");
        }
        var catastrophes = Colonnes(connexion, transaction, "catastrophes");
        if (!catastrophes.Contains("responsable")) Executer(connexion, transaction, "ALTER TABLE catastrophes ADD COLUMN responsable INTEGER NOT NULL DEFAULT 0;");
        if (!catastrophes.Contains("negligence")) Executer(connexion, transaction, "ALTER TABLE catastrophes ADD COLUMN negligence REAL NOT NULL DEFAULT 0;");
        if (!catastrophes.Contains("lecon")) Executer(connexion, transaction, "ALTER TABLE catastrophes ADD COLUMN lecon INTEGER NOT NULL DEFAULT 0;");
        if (!catastrophes.Contains("victimes")) Executer(connexion, transaction, "ALTER TABLE catastrophes ADD COLUMN victimes BLOB NOT NULL DEFAULT x'';");
    }

    /// <summary>Numéros séparés par des virgules (« 12,40 ») ; vide pour aucun.</summary>
    private static List<long> LireNumeros(string texte) =>
        texte.Length == 0 ? [] : [.. texte.Split(',').Select(t => long.Parse(t, System.Globalization.CultureInfo.InvariantCulture))];

    private static (List<Braise>, List<Reserve>) LireFeuxEtReserves(SqliteCommand commande)
    {
        var braises = new List<Braise>();
        commande.CommandText = "SELECT id, x, y, heures, couvert, allume_le, allumeur, evenement FROM braises ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
                braises.Add(new Braise(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), l.GetDouble(3), l.GetInt32(4) != 0, l.GetInt64(5), l.GetInt32(6), l.GetInt64(7)));
        }
        var reserves = new List<Reserve>();
        commande.CommandText = "SELECT id, x, y, sorte, kg, etat, qualite, faite_le, auteur, communaute, evenement FROM reserves ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                reserves.Add(new Reserve(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), (SorteReserve)l.GetInt32(3), l.GetDouble(4), l.GetDouble(5),
                    l.GetDouble(6), l.GetInt64(7), l.GetInt32(8), l.GetInt32(9), l.GetInt64(10)));
            }
        }
        return (braises, reserves);
    }

    private static void EnregistrerFeuxEtReserves(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, "DELETE FROM braises; DELETE FROM reserves;");
        using var braises = Preparer(connexion, transaction,
            "INSERT INTO braises (id, x, y, heures, couvert, allume_le, allumeur, evenement) VALUES ($i, $x, $y, $h, $c, $a, $p, $e);",
            "$i", "$x", "$y", "$h", "$c", "$a", "$p", "$e");
        foreach (var b in etat.Braises ?? []) Lancer(braises, b.Id, b.X, b.Y, b.Heures, b.Couvert ? 1 : 0, b.AllumeLe, b.AllumeurId, b.EvenementId);
        using var reserves = Preparer(connexion, transaction,
            "INSERT INTO reserves (id, x, y, sorte, kg, etat, qualite, faite_le, auteur, communaute, evenement) VALUES ($i, $x, $y, $s, $k, $t, $q, $f, $a, $c, $e);",
            "$i", "$x", "$y", "$s", "$k", "$t", "$q", "$f", "$a", "$c", "$e");
        foreach (var r in etat.Reserves ?? [])
            Lancer(reserves, r.Id, r.X, r.Y, (int)r.Sorte, r.Kg, r.Etat, r.Qualite, r.FaiteLe, r.AuteurId, r.CommunauteId, r.EvenementId);
    }
}
