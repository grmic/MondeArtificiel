using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Savoirs, objets et vêtements des humains ; feux, huttes et chronologie des découvertes.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 19 : savoirs et techniques.</summary>
    private static void MigrerVersion19(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            ALTER TABLE humains ADD COLUMN nage REAL NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN cuit_jusqua INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN remede_espece INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN remede_geste INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN remede_symptome INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN remede_le INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN vetement_matiere INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN vetement_qualite REAL NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN vetement_usure REAL NOT NULL DEFAULT 0;
            CREATE TABLE IF NOT EXISTS savoirs (
                humain    INTEGER NOT NULL,
                rang      INTEGER NOT NULL,
                geste     INTEGER NOT NULL,
                a         INTEGER NOT NULL,
                b         INTEGER NOT NULL,
                effet     INTEGER NOT NULL,
                espece    INTEGER NOT NULL,
                symptome  INTEGER NOT NULL,
                essais    INTEGER NOT NULL,
                succes    INTEGER NOT NULL,
                appris_le INTEGER NOT NULL,
                utilise_le INTEGER NOT NULL,
                origine   INTEGER NOT NULL,
                maitre    INTEGER NOT NULL,
                evenement INTEGER NOT NULL,
                PRIMARY KEY (humain, rang)
            );
            CREATE TABLE IF NOT EXISTS objets (
                humain  INTEGER NOT NULL,
                rang    INTEGER NOT NULL,
                matiere INTEGER NOT NULL,
                qualite REAL    NOT NULL,
                usure   REAL    NOT NULL,
                plein   INTEGER NOT NULL,
                PRIMARY KEY (humain, rang)
            );
            CREATE TABLE IF NOT EXISTS foyers (
                id        INTEGER PRIMARY KEY,
                x         INTEGER NOT NULL,
                y         INTEGER NOT NULL,
                heures    REAL    NOT NULL,
                allume_le INTEGER NOT NULL,
                allumeur  INTEGER NOT NULL,
                evenement INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS huttes (
                id        INTEGER PRIMARY KEY,
                x         INTEGER NOT NULL,
                y         INTEGER NOT NULL,
                solidite  REAL    NOT NULL,
                batie_le  INTEGER NOT NULL,
                batisseur INTEGER NOT NULL,
                evenement INTEGER NOT NULL,
                enduite   INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS decouvertes (
                rang      INTEGER PRIMARY KEY,
                effet     INTEGER NOT NULL,
                humain    INTEGER NOT NULL,
                le        INTEGER NOT NULL,
                evenement INTEGER NOT NULL,
                origine   INTEGER NOT NULL
            );
            """);
    }

    /// <summary>
    /// Migration vers la version 29 (fiche Communauté) : communauté de naissance de chaque humain et communauté de l'inventeur
    /// de chaque découverte, retrouvées dans le journal (la naissance et la découverte y sont notées avec la communauté du moment).
    /// </summary>
    private static void MigrerVersion29(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "humains").Contains("communaute_naissance"))
        {
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN communaute_naissance INTEGER NOT NULL DEFAULT 0;
                UPDATE humains SET communaute_naissance = COALESCE(
                    (SELECT j.communaute FROM journal j WHERE j.humain = humains.id AND j.type = 0 ORDER BY j.id LIMIT 1), 0);
                """);
        }
        if (!Colonnes(connexion, transaction, "decouvertes").Contains("communaute"))
        {
            Executer(connexion, transaction, """
                ALTER TABLE decouvertes ADD COLUMN communaute INTEGER NOT NULL DEFAULT 0;
                UPDATE decouvertes SET communaute = COALESCE((SELECT j.communaute FROM journal j WHERE j.id = decouvertes.evenement), 0);
                """);
        }
    }

    /// <summary>Migration vers la version 48 : la filiation des découvertes (de quels savoirs chacune est née).</summary>
    private static void MigrerVersion48(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "decouvertes").Contains("parents"))
            Executer(connexion, transaction, "ALTER TABLE decouvertes ADD COLUMN parents TEXT NOT NULL DEFAULT '';");
    }

    /// <summary>Migration vers la version 49 : celui avec qui une idée est née (le cerveau collectif, 0.34.1).</summary>
    private static void MigrerVersion49(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "decouvertes").Contains("coauteur"))
            Executer(connexion, transaction, "ALTER TABLE decouvertes ADD COLUMN coauteur INTEGER NOT NULL DEFAULT 0;");
    }

    /// <summary>Migration vers la version 20 : objets faits de plusieurs pièces.</summary>
    private static void MigrerVersion20(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            ALTER TABLE savoirs ADD COLUMN pieces TEXT NOT NULL DEFAULT '';
            ALTER TABLE objets ADD COLUMN pieces TEXT NOT NULL DEFAULT '';
            ALTER TABLE decouvertes ADD COLUMN pieces TEXT NOT NULL DEFAULT '';
            """);
    }

    /// <summary>Migration vers la version 21 : paroles et penchants venus de l'Entité.</summary>
    private static void MigrerVersion21(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS impulsions (
                humain      INTEGER NOT NULL,
                rang        INTEGER NOT NULL,
                parole      INTEGER NOT NULL,
                mode        INTEGER NOT NULL,
                cible       INTEGER NOT NULL,
                param       INTEGER NOT NULL,
                force       REAL    NOT NULL,
                le          INTEGER NOT NULL,
                jusqua      INTEGER NOT NULL,
                agir_le     INTEGER NOT NULL,
                reponse     INTEGER NOT NULL,
                evenement   INTEGER NOT NULL,
                orientation INTEGER NOT NULL,
                PRIMARY KEY (humain, rang)
            );
            """);
    }

    private const string EffacerSavoirs = "DELETE FROM savoirs; DELETE FROM objets; DELETE FROM foyers; DELETE FROM huttes; DELETE FROM decouvertes; DELETE FROM impulsions; DELETE FROM oeuvres; DELETE FROM braises; DELETE FROM reserves; DELETE FROM progres;";

    /// <summary>Nage, remède en cours, vêtement, savoirs et objets de chaque humain.</summary>
    private static void LireSavoirs(SqliteCommand commande, List<Humain> humains)
    {
        Humain? Trouver(long id) => id >= 1 && id <= humains.Count ? humains[(int)id - 1] : null;
        commande.CommandText = """
            SELECT id, nage, cuit_jusqua, remede_espece, remede_geste, remede_symptome, remede_le, vetement_matiere, vetement_qualite, vetement_usure
            FROM humains ORDER BY id;
            """;
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                if (Trouver(l.GetInt64(0)) is not { } h) continue;
                h.Nage = l.GetDouble(1);
                h.CuitJusqua = l.GetInt64(2);
                h.RemedeEspece = l.GetInt32(3);
                h.RemedeGeste = (Geste)l.GetInt32(4);
                h.RemedeSymptome = (Symptome)l.GetInt32(5);
                h.RemedeLe = l.GetInt64(6);
                var matiere = (Matiere)l.GetInt32(7);
                h.Vetement = matiere == 0 ? null : new Objet(matiere, l.GetDouble(8), l.GetDouble(9));
            }
        }
        commande.CommandText = """
            SELECT humain, geste, a, b, effet, espece, symptome, essais, succes, appris_le, utilise_le, origine, maitre, evenement, pieces
            FROM savoirs ORDER BY humain, rang;
            """;
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                Trouver(l.GetInt64(0))?.Savoirs.Add(new Savoir((Geste)l.GetInt32(1), (Matiere)l.GetInt32(2), (Matiere)l.GetInt32(3),
                    (Effet)l.GetInt32(4), l.GetInt32(5), (Symptome)l.GetInt32(6), l.GetInt32(7), l.GetInt32(8), l.GetInt64(9), l.GetInt64(10),
                    (OrigineSavoir)l.GetInt32(11), l.GetInt32(12), l.GetInt64(13), l.GetString(14)));
            }
        }
        commande.CommandText = "SELECT humain, matiere, qualite, usure, plein, pieces FROM objets ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                Trouver(l.GetInt64(0))?.Objets.Add(new Objet((Matiere)l.GetInt32(1), l.GetDouble(2), l.GetDouble(3), l.GetBoolean(4),
                    l.GetString(5) is { Length: > 0 } p ? p : null));
            }
        }
        commande.CommandText = """
            SELECT humain, parole, mode, cible, param, force, le, jusqua, agir_le, reponse, evenement, orientation
            FROM impulsions ORDER BY humain, rang;
            """;
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                var parole = (Parole)l.GetInt32(1);
                var mode = (ModeParole)l.GetInt32(2);
                var reponse = (Reponse)l.GetInt32(9);
                if (!Enum.IsDefined(parole) || !Enum.IsDefined(mode) || !Enum.IsDefined(reponse)) continue;
                Trouver(l.GetInt64(0))?.Impulsions.Add(new Impulsion(parole, mode, l.GetInt32(3), l.GetInt32(4), l.GetDouble(5), l.GetInt64(6),
                    l.GetInt64(7), l.GetInt64(8), reponse, l.GetInt64(10), l.GetBoolean(11)));
            }
        }
    }

    private static void EnregistrerSavoirs(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Humain> humains)
    {
        Executer(connexion, transaction, "DELETE FROM savoirs; DELETE FROM objets; DELETE FROM impulsions;");
        using var etat = Preparer(connexion, transaction, """
            UPDATE humains SET nage = $n, cuit_jusqua = $c, remede_espece = $re, remede_geste = $rg, remede_symptome = $rs, remede_le = $rl,
                vetement_matiere = $vm, vetement_qualite = $vq, vetement_usure = $vu
            WHERE id = $id;
            """, "$n", "$c", "$re", "$rg", "$rs", "$rl", "$vm", "$vq", "$vu", "$id");
        using var savoirs = Preparer(connexion, transaction, """
            INSERT INTO savoirs (humain, rang, geste, a, b, effet, espece, symptome, essais, succes, appris_le, utilise_le, origine, maitre, evenement, pieces)
            VALUES ($h, $r, $g, $a, $b, $e, $es, $sy, $n, $s, $ap, $u, $o, $m, $ev, $pi);
            """, "$h", "$r", "$g", "$a", "$b", "$e", "$es", "$sy", "$n", "$s", "$ap", "$u", "$o", "$m", "$ev", "$pi");
        using var objets = Preparer(connexion, transaction,
            "INSERT INTO objets (humain, rang, matiere, qualite, usure, plein, pieces) VALUES ($h, $r, $m, $q, $u, $p, $pi);",
            "$h", "$r", "$m", "$q", "$u", "$p", "$pi");
        using var impulsions = Preparer(connexion, transaction, """
            INSERT INTO impulsions (humain, rang, parole, mode, cible, param, force, le, jusqua, agir_le, reponse, evenement, orientation)
            VALUES ($h, $r, $p, $m, $c, $pa, $f, $le, $j, $a, $re, $ev, $o);
            """, "$h", "$r", "$p", "$m", "$c", "$pa", "$f", "$le", "$j", "$a", "$re", "$ev", "$o");
        foreach (var h in humains)
        {
            var v = h.Vetement;
            Lancer(etat, h.Nage, h.CuitJusqua, h.RemedeEspece, (int)h.RemedeGeste, (int)h.RemedeSymptome, h.RemedeLe,
                (int)(v?.Matiere ?? 0), v?.Qualite ?? 0, v?.Usure ?? 0, h.Id);
            for (var r = 0; r < h.Savoirs.Count; r++)
            {
                var s = h.Savoirs[r];
                Lancer(savoirs, h.Id, r, (int)s.Geste, (int)s.A, (int)s.B, (int)s.Effet, s.Espece, (int)s.Symptome, s.Essais, s.Succes,
                    s.AppriLe, s.UtiliseLe, (int)s.Origine, s.MaitreId, s.EvenementId, s.Pieces);
            }
            for (var r = 0; r < h.Objets.Count; r++)
            {
                var o = h.Objets[r];
                Lancer(objets, h.Id, r, (int)o.Matiere, o.Qualite, o.Usure, o.Plein, o.Pieces ?? "");
            }
            for (var r = 0; r < h.Impulsions.Count; r++)
            {
                var i = h.Impulsions[r];
                Lancer(impulsions, h.Id, r, (int)i.Parole, (int)i.Mode, i.CibleId, i.Param, i.Force, i.Le, i.Jusqua, i.AgirLe, (int)i.Reponse,
                    i.EvenementId, i.Orientation);
            }
        }
    }

    /// <summary>Migration vers la version 25 : images peintes sur la roche.</summary>
    private static void MigrerVersion25(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS oeuvres (
                id          INTEGER PRIMARY KEY,
                x           INTEGER NOT NULL,
                y           INTEGER NOT NULL,
                auteur      INTEGER NOT NULL,
                le          INTEGER NOT NULL,
                motif       INTEGER NOT NULL,
                description TEXT    NOT NULL,
                evenement   INTEGER NOT NULL,
                communaute  INTEGER NOT NULL
            );
            """);
    }

    private static (List<Foyer>, List<Hutte>, List<Decouverte>, List<Oeuvre>) LireInstallations(SqliteCommand commande)
    {
        var foyers = new List<Foyer>();
        commande.CommandText = "SELECT id, x, y, heures, allume_le, allumeur, evenement, couvert, degage, entretenu, charbon FROM foyers ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                foyers.Add(new Foyer(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), l.GetDouble(3), l.GetInt64(4), l.GetInt32(5), l.GetInt64(6),
                    l.GetInt32(7) != 0, l.GetInt32(8) != 0, l.GetInt32(9), l.GetDouble(10)));
            }
        }
        var huttes = new List<Hutte>();
        commande.CommandText = "SELECT id, x, y, solidite, batie_le, batisseur, evenement, enduite FROM huttes ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) huttes.Add(new Hutte(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), l.GetDouble(3), l.GetInt64(4), l.GetInt32(5), l.GetInt64(6), l.GetInt64(7)));
        }
        var decouvertes = new List<Decouverte>();
        commande.CommandText = "SELECT effet, humain, le, evenement, origine, pieces, communaute, parents, coauteur FROM decouvertes ORDER BY rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                decouvertes.Add(new Decouverte((Effet)l.GetInt32(0), l.GetInt32(1), l.GetInt64(2), l.GetInt64(3), (OrigineSavoir)l.GetInt32(4), l.GetString(5), l.GetInt32(6),
                    l.GetString(7), l.GetInt32(8)));
            }
        }
        var oeuvres = new List<Oeuvre>();
        commande.CommandText = "SELECT id, x, y, auteur, le, motif, description, evenement, communaute FROM oeuvres ORDER BY id;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                oeuvres.Add(new Oeuvre(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2), l.GetInt32(3), l.GetInt64(4), (MotifPeint)l.GetInt32(5), l.GetString(6),
                    l.GetInt64(7), l.GetInt32(8)));
            }
        }
        return (foyers, huttes, decouvertes, oeuvres);
    }

    private static void EnregistrerInstallations(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, "DELETE FROM foyers; DELETE FROM huttes; DELETE FROM decouvertes; DELETE FROM oeuvres;");
        using var foyers = Preparer(connexion, transaction,
            "INSERT INTO foyers (id, x, y, heures, allume_le, allumeur, evenement, couvert, degage, entretenu, charbon) VALUES ($i, $x, $y, $h, $a, $p, $e, $c, $d, $n, $k);",
            "$i", "$x", "$y", "$h", "$a", "$p", "$e", "$c", "$d", "$n", "$k");
        foreach (var f in etat.Foyers ?? [])
            Lancer(foyers, f.Id, f.X, f.Y, f.Heures, f.AllumeLe, f.AllumeurId, f.EvenementId, f.Couvert ? 1 : 0, f.Degage ? 1 : 0, f.EntretenuParId, f.Charbon);
        using var huttes = Preparer(connexion, transaction,
            "INSERT INTO huttes (id, x, y, solidite, batie_le, batisseur, evenement, enduite) VALUES ($i, $x, $y, $s, $b, $p, $e, $n);",
            "$i", "$x", "$y", "$s", "$b", "$p", "$e", "$n");
        foreach (var h in etat.Huttes ?? []) Lancer(huttes, h.Id, h.X, h.Y, h.Solidite, h.BatieLe, h.BatisseurId, h.EvenementId, h.EnduiteLe);
        using var decouvertes = Preparer(connexion, transaction,
            "INSERT INTO decouvertes (rang, effet, humain, le, evenement, origine, pieces, communaute, parents, coauteur) VALUES ($r, $e, $h, $l, $ev, $o, $p, $c, $pa, $co);",
            "$r", "$e", "$h", "$l", "$ev", "$o", "$p", "$c", "$pa", "$co");
        var liste = etat.Decouvertes ?? [];
        for (var r = 0; r < liste.Count; r++)
        {
            var d = liste[r];
            Lancer(decouvertes, r, (int)d.Effet, d.HumainId, d.Le, d.EvenementId, (int)d.Origine, d.Pieces, d.CommunauteId, d.Parents, d.CoauteurId);
        }
        using var oeuvres = Preparer(connexion, transaction,
            "INSERT INTO oeuvres (id, x, y, auteur, le, motif, description, evenement, communaute) VALUES ($i, $x, $y, $a, $l, $m, $d, $e, $c);",
            "$i", "$x", "$y", "$a", "$l", "$m", "$d", "$e", "$c");
        foreach (var o in etat.Oeuvres ?? [])
            Lancer(oeuvres, o.Id, o.X, o.Y, o.AuteurId, o.Le, (int)o.Motif, o.Description, o.EvenementId, o.CommunauteId);
    }
}
