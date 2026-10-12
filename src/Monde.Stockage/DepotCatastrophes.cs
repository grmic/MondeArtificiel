using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Nature;

namespace Monde.Stockage;

/// <summary>Une catastrophe réduite à ce qu'en montrent les courbes démographiques.</summary>
public sealed record RepereCatastrophe(long Id, TypeCatastrophe Type, Origine Origine, long Debut, string Nom, int Morts, int MortsSuites);

/// <summary>Catastrophes naturelles, maladies, peur et infections des humains, dégâts sur la nature.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Plus grand nombre de témoins ou de communautés qu'une catastrophe enregistrée peut compter.</summary>
    private const int EntitesMax = 10_000_000;

    private const string ColonnesCatastrophe =
        "id, type, origine, debut, fin, x, y, rayon, intensite, nom, morts, blesses, cases, front, direction, parametre, " +
        "prochaine, phase, duree, signalee, pertes, communautes, temoins, suites_jusqua, morts_suites";

    /// <summary>Migration vers la version 11 : catastrophes naturelles et maladies.</summary>
    private static void MigrerVersion11(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            ALTER TABLE humains ADD COLUMN peur REAL NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN peur_evenement INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN peur_type INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN infection INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN infecte_le INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN symptomes_le INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN guerison_le INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN infection_evenement INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE humains ADD COLUMN immunites TEXT NOT NULL DEFAULT '';
            ALTER TABLE ressources ADD COLUMN degats BLOB;
            CREATE TABLE IF NOT EXISTS catastrophes (
                id          INTEGER PRIMARY KEY,
                type        INTEGER NOT NULL,
                origine     INTEGER NOT NULL,
                debut       INTEGER NOT NULL,
                fin         INTEGER,
                x           INTEGER NOT NULL,
                y           INTEGER NOT NULL,
                rayon       INTEGER NOT NULL,
                intensite   REAL    NOT NULL,
                nom         TEXT    NOT NULL,
                morts       INTEGER NOT NULL,
                blesses     INTEGER NOT NULL,
                cases       BLOB    NOT NULL,
                front       BLOB    NOT NULL,
                direction   REAL    NOT NULL,
                parametre   INTEGER NOT NULL,
                prochaine   INTEGER NOT NULL,
                phase       INTEGER NOT NULL,
                duree       INTEGER NOT NULL,
                signalee    INTEGER NOT NULL,
                pertes      TEXT    NOT NULL,
                communautes TEXT    NOT NULL,
                temoins     BLOB    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS catastrophes_debut ON catastrophes (debut);
            CREATE TABLE IF NOT EXISTS pathogenes (
                id           INTEGER PRIMARY KEY,
                nom          TEXT    NOT NULL,
                transmission REAL    NOT NULL,
                incubation   REAL    NOT NULL,
                duree        REAL    NOT NULL,
                letalite     REAL    NOT NULL,
                immunite     REAL    NOT NULL,
                apparu_le    INTEGER NOT NULL,
                evenement    INTEGER NOT NULL
            );
            """);
    }

    /// <summary>Peur, infection et immunités de chaque humain (lues après les autres colonnes).</summary>
    private static void LireSanteNature(SqliteCommand commande, List<Humain> humains)
    {
        commande.CommandText = """
            SELECT id, peur, peur_evenement, peur_type, infection, infecte_le, symptomes_le, guerison_le, infection_evenement, immunites
            FROM humains ORDER BY id;
            """;
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var id = l.GetInt32(0);
            if (id < 1 || id > humains.Count) continue;
            var h = humains[id - 1];
            h.Peur = l.GetDouble(1);
            h.PeurEvenementId = l.GetInt64(2);
            h.PeurType = (TypeCatastrophe)l.GetByte(3);
            h.InfectionId = l.GetInt32(4);
            h.InfecteLe = l.GetInt64(5);
            h.SymptomesLe = l.GetInt64(6);
            h.GuerisonLe = l.GetInt64(7);
            h.InfectionEvenementId = l.GetInt64(8);
            h.Immunites = LireImmunites(l.GetString(9));
        }
    }

    private static void EnregistrerSanteNature(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Humain> humains)
    {
        using var commande = Preparer(connexion, transaction, """
            UPDATE humains SET peur = $p, peur_evenement = $pe, peur_type = $pt, infection = $i, infecte_le = $il,
                symptomes_le = $s, guerison_le = $g, infection_evenement = $ie, immunites = $im
            WHERE id = $id;
            """, "$p", "$pe", "$pt", "$i", "$il", "$s", "$g", "$ie", "$im", "$id");
        foreach (var h in humains)
        {
            Lancer(commande, h.Peur, h.PeurEvenementId, (int)h.PeurType, h.InfectionId, h.InfecteLe, h.SymptomesLe, h.GuerisonLe,
                h.InfectionEvenementId, EcrireImmunites(h.Immunites), h.Id);
        }
    }

    /// <summary>Immunités : « maladie:jusqu'à » séparées par « ; » (« maladie: » pour une immunité à vie).</summary>
    private static string EcrireImmunites(List<Immunite> immunites) =>
        string.Join(';', immunites.Select(i => $"{i.PathogeneId.ToString(CultureInfo.InvariantCulture)}:{i.Jusqua?.ToString(CultureInfo.InvariantCulture)}"));

    private static List<Immunite> LireImmunites(string texte)
    {
        var resultat = new List<Immunite>();
        foreach (var morceau in texte.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parties = morceau.Split(':');
            if (parties.Length != 2 || !int.TryParse(parties[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new InvalidDataException("Immunités corrompues dans la sauvegarde.");
            }
            long? jusqua = null;
            if (parties[1].Length > 0)
            {
                if (!long.TryParse(parties[1], NumberStyles.None, CultureInfo.InvariantCulture, out var j)) throw new InvalidDataException("Immunités corrompues dans la sauvegarde.");
                jusqua = j;
            }
            resultat.Add(new Immunite(id, jusqua));
        }
        return resultat;
    }

    /// <summary>Catastrophes en cours, ou dans leurs suites, pour reprendre la simulation.</summary>
    private static List<Catastrophe> LireCatastrophesEnCours(SqliteCommand commande)
    {
        commande.CommandText = $"SELECT {ColonnesCatastrophe} FROM catastrophes " +
            $"WHERE fin IS NULL OR suites_jusqua > 0 AND suites_jusqua > (SELECT secondes FROM etat WHERE id = 1) - {Monde.Simulation.Temps.Calendrier.SecondesParJour} ORDER BY id;";
        return LireCatastrophes(commande);
    }

    private static List<Pathogene> LirePathogenes(SqliteCommand commande)
    {
        commande.CommandText = "SELECT id, nom, transmission, incubation, duree, letalite, immunite, apparu_le, evenement FROM pathogenes ORDER BY id;";
        var resultat = new List<Pathogene>();
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            resultat.Add(new Pathogene(l.GetInt32(0), l.GetString(1), l.GetDouble(2), l.GetDouble(3), l.GetDouble(4), l.GetDouble(5),
                l.GetDouble(6), l.GetInt64(7), l.GetInt64(8)));
        }
        for (var i = 0; i < resultat.Count; i++)
        {
            if (resultat[i].Id != i + 1) throw new InvalidDataException("Maladies corrompues dans la sauvegarde.");
        }
        return resultat;
    }

    private static void EnregistrerCatastrophes(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Catastrophe> catastrophes)
    {
        // Une catastrophe en cours est réécrite à chaque sauvegarde ; une fois terminée, elle reste dans l'histoire du monde.
        using var commande = Preparer(connexion, transaction,
            $"INSERT OR REPLACE INTO catastrophes ({ColonnesCatastrophe}) VALUES " +
            "($i, $t, $o, $d, $f, $x, $y, $r, $in, $n, $m, $b, $c, $fr, $di, $p, $pr, $ph, $du, $s, $pe, $co, $te, $sj, $ms);",
            "$i", "$t", "$o", "$d", "$f", "$x", "$y", "$r", "$in", "$n", "$m", "$b", "$c", "$fr", "$di", "$p", "$pr", "$ph", "$du",
            "$s", "$pe", "$co", "$te", "$sj", "$ms");
        foreach (var c in catastrophes)
        {
            Lancer(commande, c.Id, (int)c.Type, (int)c.Origine, c.Debut, c.Fin is { } f ? f : DBNull.Value, c.X, c.Y, c.Rayon, c.Intensite,
                c.Nom, c.Morts, c.Blesses, EncoderCases([.. c.Cases.Order()]), EcrireTerritoire(c.Front), c.Direction, c.Parametre,
                c.Prochaine, c.Phase, c.Duree, c.Signalee, EcrirePertes(c.PertesParCommunaute), string.Join(',', c.Communautes),
                EcrireEntiers(c.Temoins.Order()), c.SuitesJusqua, c.MortsSuites);
        }
    }

    private static void EnregistrerPathogenes(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Pathogene> pathogenes)
    {
        using var commande = Preparer(connexion, transaction, """
            INSERT OR REPLACE INTO pathogenes (id, nom, transmission, incubation, duree, letalite, immunite, apparu_le, evenement)
            VALUES ($i, $n, $t, $in, $d, $l, $im, $a, $e);
            """, "$i", "$n", "$t", "$in", "$d", "$l", "$im", "$a", "$e");
        foreach (var p in pathogenes)
        {
            Lancer(commande, p.Id, p.Nom, p.Transmission, p.IncubationJours, p.DureeJours, p.Letalite, p.ImmuniteAns, p.ApparuLe, p.EvenementId);
        }
    }

    /// <summary>
    /// Catastrophes qui ont tué (directement ou dans leurs suites) ou que l'Entité a provoquées, sans leurs cases :
    /// repères des courbes démographiques.
    /// </summary>
    public List<RepereCatastrophe> LireReperesCatastrophes()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = """
            SELECT id, type, origine, debut, nom, morts, morts_suites FROM catastrophes
            WHERE morts > 0 OR morts_suites > 0 OR origine = $entite ORDER BY debut;
            """;
        commande.Parameters.AddWithValue("$entite", (int)Origine.Entite);
        var resultat = new List<RepereCatastrophe>();
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            resultat.Add(new RepereCatastrophe(l.GetInt64(0), (TypeCatastrophe)l.GetInt32(1), (Origine)l.GetInt32(2), l.GetInt64(3),
                l.GetString(4), l.GetInt32(5), l.GetInt32(6)));
        }
        return resultat;
    }

    /// <summary>
    /// Catastrophes enregistrées, de la plus récente à la plus ancienne (avec leurs cases), éventuellement celles qui ont touché
    /// une communauté. Les catastrophes encore en mémoire ne sont pas toutes enregistrées : l'appelant les ajoute.
    /// </summary>
    public List<Catastrophe> LireCatastrophes(int limite = 200, int? communaute = null, long? avantId = null)
    {
        limite = Math.Clamp(limite, 1, LimiteJournal);
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        var conditions = new List<string>();
        if (communaute is { } co)
        {
            // Les communautés touchées sont notées « 1,4,12 » : on cherche « ,4, » dans « ,1,4,12, ».
            conditions.Add("(',' || communautes || ',') LIKE $co");
            commande.Parameters.AddWithValue("$co", $"%,{co.ToString(CultureInfo.InvariantCulture)},%");
        }
        if (avantId is { } a)
        {
            conditions.Add("id < $a");
            commande.Parameters.AddWithValue("$a", a);
        }
        commande.Parameters.AddWithValue("$limite", limite);
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        commande.CommandText = $"SELECT {ColonnesCatastrophe} FROM catastrophes {where} ORDER BY id DESC LIMIT $limite;";
        return LireCatastrophes(commande);
    }

    /// <summary>Une catastrophe enregistrée par son numéro (null si elle n'existe pas).</summary>
    public Catastrophe? LireCatastrophe(long id)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = $"SELECT {ColonnesCatastrophe} FROM catastrophes WHERE id = $id;";
        commande.Parameters.AddWithValue("$id", id);
        return LireCatastrophes(commande).FirstOrDefault();
    }

    /// <summary>Maladies apparues depuis le début du monde.</summary>
    public List<Pathogene> LirePathogenes()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        return LirePathogenes(commande);
    }

    private static List<Catastrophe> LireCatastrophes(SqliteCommand commande)
    {
        var resultat = new List<Catastrophe>();
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            resultat.Add(new Catastrophe
            {
                Id = l.GetInt64(0), Type = (TypeCatastrophe)l.GetByte(1), Origine = (Origine)l.GetByte(2), Debut = l.GetInt64(3),
                Fin = l.IsDBNull(4) ? null : l.GetInt64(4), X = l.GetInt32(5), Y = l.GetInt32(6), Rayon = l.GetInt32(7),
                Intensite = l.GetDouble(8), Nom = l.GetString(9), Morts = l.GetInt32(10), Blesses = l.GetInt32(11),
                Cases = [.. DecoderCases((byte[])l[12])], Front = LireTerritoire((byte[])l[13]), Direction = l.GetDouble(14),
                Parametre = l.GetInt32(15), Prochaine = l.GetInt64(16), Phase = l.GetInt32(17), Duree = l.GetInt64(18),
                Signalee = l.GetBoolean(19), PertesParCommunaute = LirePertes(l.GetString(20)),
                Communautes = [.. LireListe(l.GetString(21))], Temoins = [.. LireEntiers((byte[])l[22])],
                SuitesJusqua = l.GetInt64(23), MortsSuites = l.GetInt32(24),
            });
        }
        return resultat;
    }

    /// <summary>Morts par communauté : « communauté:morts » séparés par « ; ».</summary>
    private static string EcrirePertes(SortedDictionary<int, int> pertes) =>
        string.Join(';', pertes.Select(p => $"{p.Key.ToString(CultureInfo.InvariantCulture)}:{p.Value.ToString(CultureInfo.InvariantCulture)}"));

    private static SortedDictionary<int, int> LirePertes(string texte)
    {
        var resultat = new SortedDictionary<int, int>();
        foreach (var morceau in texte.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parties = morceau.Split(':');
            if (parties.Length != 2
                || !int.TryParse(parties[0], NumberStyles.None, CultureInfo.InvariantCulture, out var c)
                || !int.TryParse(parties[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                throw new InvalidDataException("Pertes corrompues dans la sauvegarde.");
            }
            resultat[c] = n;
        }
        return resultat;
    }

    private static IEnumerable<int> LireListe(string texte)
    {
        foreach (var morceau in texte.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(morceau, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) throw new InvalidDataException("Liste corrompue dans la sauvegarde.");
            yield return n;
        }
    }

    /// <summary>Entiers positifs triés : écarts successifs en entiers à longueur variable.</summary>
    private static byte[] EcrireEntiers(IEnumerable<int> valeurs)
    {
        using var flux = new MemoryStream();
        using var ecrivain = new BinaryWriter(flux, Encoding.UTF8);
        var precedent = 0;
        foreach (var v in valeurs)
        {
            if (v < precedent) throw new ArgumentException("Les entiers doivent être triés et positifs.", nameof(valeurs));
            ecrivain.Write7BitEncodedInt(v - precedent);
            precedent = v;
        }
        ecrivain.Flush();
        return flux.ToArray();
    }

    private static List<int> LireEntiers(byte[] octets)
    {
        var resultat = new List<int>();
        using var lecteur = new BinaryReader(new MemoryStream(octets));
        long valeur = 0;
        while (lecteur.BaseStream.Position < octets.Length)
        {
            var ecart = lecteur.Read7BitEncodedInt();
            valeur += ecart;
            if (ecart < 0 || valeur > int.MaxValue || resultat.Count >= EntitesMax) throw new InvalidDataException("Liste corrompue dans la sauvegarde.");
            resultat.Add((int)valeur);
        }
        return resultat;
    }

}
