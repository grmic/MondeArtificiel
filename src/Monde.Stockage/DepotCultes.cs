using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Stockage;

/// <summary>Religions et leurs préceptes, prières, lieux sacrés et malheurs annoncés.</summary>
public sealed partial class DepotMonde
{
    private const string EffacerCultes = "DELETE FROM religions; DELETE FROM dogmes; DELETE FROM prieres; DELETE FROM lieux_sacres; DELETE FROM propheties;";

    /// <summary>Migration vers la version 28 : prières, prophètes et religions.</summary>
    private static void MigrerVersion28(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var humains = Colonnes(connexion, transaction, "humains");
        foreach (var (colonne, type) in new[]
                 {
                     ("religion", "INTEGER"), ("ferveur", "REAL"), ("religion_depuis", "INTEGER"), ("role", "INTEGER"),
                     ("source_prophete", "INTEGER"), ("role_depuis", "INTEGER"), ("miracles", "INTEGER"), ("prie_le", "INTEGER"),
                 })
        {
            if (!humains.Contains(colonne)) Executer(connexion, transaction, $"ALTER TABLE humains ADD COLUMN {colonne} {type} NOT NULL DEFAULT 0;");
        }
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS religions (
                id           INTEGER PRIMARY KEY,
                nom          TEXT    NOT NULL,
                sens         TEXT    NOT NULL,
                fondateur    INTEGER NOT NULL,
                fondee_le    INTEGER NOT NULL,
                naissance    INTEGER NOT NULL,
                evenement    INTEGER NOT NULL,
                parent       INTEGER NOT NULL,
                communaute   INTEGER NOT NULL,
                gestes       TEXT,
                eteinte_le   INTEGER NOT NULL,
                fideles_max  INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS dogmes (
                religion   INTEGER NOT NULL,
                rang       INTEGER NOT NULL,
                precepte   INTEGER NOT NULL,
                parametre  INTEGER NOT NULL,
                origine    INTEGER NOT NULL,
                texte      TEXT    NOT NULL,
                le         INTEGER NOT NULL,
                evenement  INTEGER NOT NULL,
                PRIMARY KEY (religion, rang)
            );
            CREATE TABLE IF NOT EXISTS prieres (
                id         INTEGER PRIMARY KEY,
                humain     INTEGER NOT NULL,
                le         INTEGER NOT NULL,
                sujet      INTEGER NOT NULL,
                cible      INTEGER NOT NULL,
                parametre  INTEGER NOT NULL,
                ferveur    REAL    NOT NULL,
                forte      INTEGER NOT NULL,
                religion   INTEGER NOT NULL,
                evenement  INTEGER NOT NULL,
                jusqua     INTEGER NOT NULL,
                issue      INTEGER NOT NULL,
                issue_le   INTEGER NOT NULL,
                conclusion TEXT
            );
            CREATE INDEX IF NOT EXISTS prieres_humain ON prieres (humain);
            CREATE TABLE IF NOT EXISTS lieux_sacres (
                id         INTEGER PRIMARY KEY,
                x          INTEGER NOT NULL,
                y          INTEGER NOT NULL,
                le         INTEGER NOT NULL,
                raison     TEXT    NOT NULL,
                evenement  INTEGER NOT NULL,
                religion   INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS propheties (
                prophete   INTEGER NOT NULL,
                religion   INTEGER NOT NULL,
                x          INTEGER NOT NULL,
                y          INTEGER NOT NULL,
                le         INTEGER NOT NULL,
                jusqua     INTEGER NOT NULL,
                evenement  INTEGER NOT NULL,
                vraie      INTEGER NOT NULL
            );
            """);
    }

    /// <summary>
    /// Lit les religions, les lieux sacrés, les malheurs annoncés et les prières encore utiles (en attente, ou résolues
    /// depuis moins d'un an avant <paramref name="secondes"/>) ; le numéro de la prochaine prière suit la plus haute en base.
    /// </summary>
    private static EtatMonde LireCultes(SqliteCommand commande, EtatMonde etat)
    {
        var religions = new List<Religion>();
        commande.CommandText = "SELECT id, nom, sens, fondateur, fondee_le, naissance, evenement, parent, communaute, gestes, eteinte_le, fideles_max FROM religions ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                religions.Add(new Religion
                {
                    Id = l.GetInt32(0), Nom = l.GetString(1), Sens = l.GetString(2), FondateurId = l.GetInt32(3), FondeeLe = l.GetInt64(4),
                    Naissance = (NaissanceReligion)l.GetInt32(5), EvenementId = l.GetInt64(6), ParentId = l.GetInt32(7), CommunauteId = l.GetInt32(8),
                    Gestes = l.IsDBNull(9) ? null : l.GetString(9), EteinteLe = l.GetInt64(10), FidelesMax = l.GetInt32(11),
                });
            }
        }
        commande.CommandText = "SELECT religion, precepte, parametre, origine, texte, le, evenement FROM dogmes ORDER BY religion, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                var id = l.GetInt32(0);
                if (id < 1 || id > religions.Count) continue;
                religions[id - 1].Dogmes.Add(new Dogme((Precepte)l.GetInt32(1), l.GetInt32(2), (OrigineDogme)l.GetInt32(3), l.GetString(4), l.GetInt64(5), l.GetInt64(6)));
            }
        }
        var prieres = new List<Priere>();
        commande.CommandText = """
            SELECT id, humain, le, sujet, cible, parametre, ferveur, forte, religion, evenement, jusqua, issue, issue_le, conclusion
            FROM prieres WHERE issue = 0 OR issue_le >= $depuis ORDER BY id;
            """;
        commande.Parameters.Clear();
        commande.Parameters.AddWithValue("$depuis", etat.Secondes - Calendrier.SecondesParAn);
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                prieres.Add(new Priere
                {
                    Id = l.GetInt32(0), HumainId = l.GetInt32(1), Le = l.GetInt64(2), Sujet = (SujetPriere)l.GetInt32(3), CibleId = l.GetInt32(4),
                    Parametre = l.GetInt64(5), Ferveur = l.GetDouble(6), Forte = l.GetInt32(7) != 0, ReligionId = l.GetInt32(8), EvenementId = l.GetInt64(9),
                    Jusqua = l.GetInt64(10), Issue = (IssuePriere)l.GetInt32(11), IssueLe = l.GetInt64(12), Conclusion = l.IsDBNull(13) ? null : l.GetString(13),
                });
            }
        }
        commande.Parameters.Clear();
        commande.CommandText = "SELECT COALESCE(MAX(id), 0) FROM prieres;";
        var prochaine = Convert.ToInt32(commande.ExecuteScalar()) + 1;
        var lieux = new List<LieuSacre>();
        commande.CommandText = "SELECT id, x, y, le, raison, evenement, religion FROM lieux_sacres ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) lieux.Add(new LieuSacre(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), l.GetInt64(3), l.GetString(4), l.GetInt64(5), l.GetInt32(6)));
        }
        var propheties = new List<Prophetie>();
        commande.CommandText = "SELECT prophete, religion, x, y, le, jusqua, evenement, vraie FROM propheties ORDER BY rowid;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                propheties.Add(new Prophetie(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), l.GetInt32(3), l.GetInt64(4), l.GetInt64(5), l.GetInt64(6),
                    l.GetInt32(7) != 0));
            }
        }
        // Rien de tout cela (monde neuf, ou d'avant la version 28) : l'état reste tel quel.
        if (religions.Count > 0) etat = etat with { Religions = religions };
        if (prieres.Count > 0) etat = etat with { Prieres = prieres };
        if (lieux.Count > 0) etat = etat with { LieuxSacres = lieux };
        if (propheties.Count > 0) etat = etat with { Propheties = propheties };
        return prochaine > 1 ? etat with { ProchainePriere = prochaine } : etat;
    }

    /// <summary>
    /// Enregistre les religions, lieux sacrés et malheurs annoncés (remplacés), et les prières en mémoire (ajoutées ou mises
    /// à jour : les plus anciennes restent en base).
    /// </summary>
    private static void EnregistrerCultes(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, "DELETE FROM religions; DELETE FROM dogmes; DELETE FROM lieux_sacres; DELETE FROM propheties;");
        using var religions = Preparer(connexion, transaction,
            "INSERT INTO religions (id, nom, sens, fondateur, fondee_le, naissance, evenement, parent, communaute, gestes, eteinte_le, fideles_max) "
            + "VALUES ($i, $n, $s, $f, $fl, $na, $e, $p, $c, $g, $el, $m);",
            "$i", "$n", "$s", "$f", "$fl", "$na", "$e", "$p", "$c", "$g", "$el", "$m");
        using var dogmes = Preparer(connexion, transaction,
            "INSERT INTO dogmes (religion, rang, precepte, parametre, origine, texte, le, evenement) VALUES ($r, $k, $p, $a, $o, $t, $l, $e);",
            "$r", "$k", "$p", "$a", "$o", "$t", "$l", "$e");
        foreach (var r in etat.Religions ?? [])
        {
            Lancer(religions, r.Id, r.Nom, r.Sens, r.FondateurId, r.FondeeLe, (int)r.Naissance, r.EvenementId, r.ParentId, r.CommunauteId,
                r.Gestes is { } g ? g : DBNull.Value, r.EteinteLe, r.FidelesMax);
            for (var k = 0; k < r.Dogmes.Count; k++)
            {
                var d = r.Dogmes[k];
                Lancer(dogmes, r.Id, k, (int)d.Precepte, d.Parametre, (int)d.Origine, d.Texte, d.Le, d.EvenementId);
            }
        }
        using var prieres = Preparer(connexion, transaction, """
            INSERT OR REPLACE INTO prieres (id, humain, le, sujet, cible, parametre, ferveur, forte, religion, evenement, jusqua, issue, issue_le, conclusion)
            VALUES ($i, $h, $l, $s, $c, $p, $f, $fo, $r, $e, $j, $is, $il, $co);
            """, "$i", "$h", "$l", "$s", "$c", "$p", "$f", "$fo", "$r", "$e", "$j", "$is", "$il", "$co");
        foreach (var p in etat.Prieres ?? [])
        {
            Lancer(prieres, p.Id, p.HumainId, p.Le, (int)p.Sujet, p.CibleId, p.Parametre, p.Ferveur, p.Forte ? 1 : 0, p.ReligionId, p.EvenementId,
                p.Jusqua, (int)p.Issue, p.IssueLe, p.Conclusion is { } c ? c : DBNull.Value);
        }
        using var lieux = Preparer(connexion, transaction,
            "INSERT INTO lieux_sacres (id, x, y, le, raison, evenement, religion) VALUES ($i, $x, $y, $l, $r, $e, $re);",
            "$i", "$x", "$y", "$l", "$r", "$e", "$re");
        foreach (var l in etat.LieuxSacres ?? []) Lancer(lieux, l.Id, l.X, l.Y, l.Le, l.Raison, l.EvenementId, l.ReligionId);
        using var propheties = Preparer(connexion, transaction,
            "INSERT INTO propheties (prophete, religion, x, y, le, jusqua, evenement, vraie) VALUES ($p, $r, $x, $y, $l, $j, $e, $v);",
            "$p", "$r", "$x", "$y", "$l", "$j", "$e", "$v");
        foreach (var p in etat.Propheties ?? []) Lancer(propheties, p.ProphetId, p.ReligionId, p.X, p.Y, p.Le, p.Jusqua, p.EvenementId, p.Vraie ? 1 : 0);
    }
}
