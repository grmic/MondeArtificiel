using System.Text;
using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Langage : parole et mots de chaque humain, son nom dans sa langue ; sons, signes, stade et histoire de chaque langue.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 26 : le langage. Les mondes plus anciens repartent des gestes.</summary>
    private static void MigrerVersion26(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS langage_humains (
                humain        INTEGER PRIMARY KEY,
                parole        INTEGER NOT NULL,
                parole_enfant INTEGER NOT NULL,
                parole_native INTEGER NOT NULL,
                ordre         INTEGER NOT NULL,
                nom           TEXT,
                sens          TEXT,
                nom_le        INTEGER NOT NULL,
                nom_par       INTEGER NOT NULL,
                nom_evenement INTEGER NOT NULL,
                seconde       INTEGER NOT NULL,
                compagnons    INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mots (
                humain INTEGER NOT NULL,
                rang   INTEGER NOT NULL,
                idee   TEXT    NOT NULL,
                forme  TEXT    NOT NULL,
                signe  INTEGER NOT NULL,
                force  REAL    NOT NULL,
                le     INTEGER NOT NULL,
                auteur INTEGER NOT NULL,
                langue INTEGER NOT NULL,
                PRIMARY KEY (humain, rang)
            );
            CREATE TABLE IF NOT EXISTS langues (
                communaute     INTEGER PRIMARY KEY,
                sons           TEXT,
                signes         TEXT    NOT NULL,
                stade          INTEGER NOT NULL,
                ordre          INTEGER NOT NULL,
                parole_le      INTEGER NOT NULL,
                combinaison_le INTEGER NOT NULL,
                ordre_le       INTEGER NOT NULL,
                palier         INTEGER NOT NULL,
                langue         INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS langue_histoire (
                communaute INTEGER NOT NULL,
                rang       INTEGER NOT NULL,
                idee       TEXT    NOT NULL,
                forme      TEXT    NOT NULL,
                ancienne   TEXT,
                signe      INTEGER NOT NULL,
                le         INTEGER NOT NULL,
                auteur     INTEGER NOT NULL,
                type       INTEGER NOT NULL,
                source     INTEGER NOT NULL,
                evenement  INTEGER NOT NULL,
                PRIMARY KEY (communaute, rang)
            );
            """);
    }

    private const string EffacerLangage = "DELETE FROM langage_humains; DELETE FROM mots; DELETE FROM langues; DELETE FROM langue_histoire;";

    /// <summary>Une idée : lettres, chiffres et « : » (« savoir:12 », « plante:3 »).</summary>
    private static bool EstIdee(string s) => s.Length is > 0 and <= 40 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == ':');

    /// <summary>Un signe : trois nombres séparés par des points (main, mouvement, lieu).</summary>
    private static bool EstSigne(string s) => s.Length is > 0 and <= 20 && s.Split('.') is { Length: 3 } p && p.All(x => x.Length is > 0 and <= 2 && x.All(char.IsAsciiDigit));

    private static bool EstForme(string forme, bool signe) => signe ? EstSigne(forme) : EstMot(forme);

    private static void LireLangage(SqliteCommand commande, List<Humain> humains)
    {
        Humain? Trouver(long id) => id >= 1 && id <= humains.Count ? humains[(int)id - 1] : null;
        commande.CommandText = """
            SELECT humain, parole, parole_enfant, parole_native, ordre, nom, sens, nom_le, nom_par, nom_evenement, seconde, compagnons
            FROM langage_humains ORDER BY humain;
            """;
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                if (Trouver(l.GetInt64(0)) is not { } h) continue;
                var ordre = (OrdreMots)l.GetInt32(4);
                var nom = l.IsDBNull(5) ? null : l.GetString(5);
                if (!Enum.IsDefined(ordre) || nom is not null && !EstMot(nom)) throw new InvalidDataException("Langage corrompu dans la sauvegarde.");
                h.StadeParole = Math.Clamp(l.GetInt32(1), 0, 3);
                h.ParoleEnfant = l.GetBoolean(2);
                h.ParoleNative = l.GetBoolean(3);
                h.Ordre = ordre;
                h.NomLangue = nom;
                h.NomSens = l.IsDBNull(6) ? null : l.GetString(6);
                h.NomLangueLe = l.GetInt64(7);
                h.NomParId = l.GetInt32(8);
                h.NomEvenementId = l.GetInt64(9);
                h.LangueSecondeId = l.GetInt32(10);
                h.CompagnonsDeLangue = Math.Clamp(l.GetInt32(11), 0, 3);
            }
        }
        commande.CommandText = "SELECT humain, idee, forme, signe, force, le, auteur, langue FROM mots ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                var (idee, forme, signe) = (l.GetString(1), l.GetString(2), l.GetBoolean(3));
                if (!EstIdee(idee) || !EstForme(forme, signe)) throw new InvalidDataException("Mot corrompu dans la sauvegarde.");
                Trouver(l.GetInt64(0))?.Vocabulaire.Add(new Mot(idee, forme, signe, Math.Clamp(l.GetDouble(4), 0, 1), l.GetInt64(5), l.GetInt32(6), l.GetInt32(7)));
            }
        }
    }

    private static void EnregistrerLangage(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Humain> humains)
    {
        Executer(connexion, transaction, "DELETE FROM langage_humains; DELETE FROM mots;");
        using var langage = Preparer(connexion, transaction, """
            INSERT INTO langage_humains (humain, parole, parole_enfant, parole_native, ordre, nom, sens, nom_le, nom_par, nom_evenement, seconde, compagnons)
            VALUES ($h, $p, $pe, $pn, $o, $n, $s, $nl, $np, $ne, $se, $co);
            """, "$h", "$p", "$pe", "$pn", "$o", "$n", "$s", "$nl", "$np", "$ne", "$se", "$co");
        using var mots = Preparer(connexion, transaction,
            "INSERT INTO mots (humain, rang, idee, forme, signe, force, le, auteur, langue) VALUES ($h, $r, $i, $f, $s, $fo, $l, $a, $la);",
            "$h", "$r", "$i", "$f", "$s", "$fo", "$l", "$a", "$la");
        foreach (var h in humains)
        {
            if (h.StadeParole == 0 && h.NomLangue is null && h.Vocabulaire.Count == 0) continue;
            if (h.NomLangue is { } nom && !EstMot(nom)) throw new ArgumentException($"Nom invalide : {nom}.");
            Lancer(langage, h.Id, h.StadeParole, h.ParoleEnfant, h.ParoleNative, (int)h.Ordre, (object?)h.NomLangue ?? DBNull.Value,
                (object?)h.NomSens ?? DBNull.Value, h.NomLangueLe, h.NomParId, h.NomEvenementId, h.LangueSecondeId, h.CompagnonsDeLangue);
            for (var r = 0; r < h.Vocabulaire.Count; r++)
            {
                var m = h.Vocabulaire[r];
                if (!EstIdee(m.Idee) || !EstForme(m.Forme, m.Signe)) throw new ArgumentException($"Mot invalide : {m.Idee}={m.Forme}.");
                Lancer(mots, h.Id, r, m.Idee, m.Forme, m.Signe, m.Force, m.Le, m.AuteurId, m.Langue);
            }
        }
    }

    /// <summary>Signes d'un groupe : « idée=signe » séparés par « ; ».</summary>
    private static string EcrireSignes(Dictionary<string, string> signes)
    {
        var texte = new StringBuilder();
        foreach (var (idee, signe) in signes.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!EstIdee(idee) || !EstSigne(signe)) throw new ArgumentException($"Signe invalide : {idee}={signe}.");
            if (texte.Length > 0) texte.Append(';');
            texte.Append(idee).Append('=').Append(signe);
        }
        return texte.ToString();
    }

    private static Dictionary<string, string> LireSignes(string texte)
    {
        var signes = new Dictionary<string, string>();
        foreach (var paire in texte.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var morceaux = paire.Split('=');
            if (morceaux.Length != 2 || !EstIdee(morceaux[0]) || !EstSigne(morceaux[1])) throw new InvalidDataException("Signes corrompus dans la sauvegarde.");
            signes[morceaux[0]] = morceaux[1];
        }
        return signes;
    }

    private static void LireLangues(SqliteCommand commande, List<Communaute> communautes)
    {
        Communaute? Trouver(long id) => id >= 1 && id <= communautes.Count ? communautes[(int)id - 1] : null;
        commande.CommandText = "SELECT communaute, sons, signes, stade, ordre, parole_le, combinaison_le, ordre_le, palier, langue FROM langues ORDER BY communaute;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                if (Trouver(l.GetInt64(0)) is not { } c) continue;
                var ordre = (OrdreMots)l.GetInt32(4);
                if (!Enum.IsDefined(ordre)) throw new InvalidDataException("Langue corrompue dans la sauvegarde.");
                c.Sons = Sons.Lire(l.IsDBNull(1) ? null : l.GetString(1));
                c.Signes = LireSignes(l.GetString(2));
                c.Stade = (byte)Math.Clamp(l.GetInt32(3), 0, 3);
                c.Ordre = ordre;
                c.ParoleLe = l.GetInt64(5);
                c.CombinaisonLe = l.GetInt64(6);
                c.OrdreLe = l.GetInt64(7);
                c.PalierMots = l.GetInt32(8);
                c.LangueId = l.GetInt32(9);
            }
        }
        commande.CommandText = """
            SELECT communaute, idee, forme, ancienne, signe, le, auteur, type, source, evenement
            FROM langue_histoire ORDER BY communaute, rang;
            """;
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                var (idee, forme, signe, type) = (l.GetString(1), l.GetString(2), l.GetBoolean(4), (ChangementMot)l.GetInt32(7));
                var ancienne = l.IsDBNull(3) ? null : l.GetString(3);
                if (!EstIdee(idee) || !EstForme(forme, signe) || ancienne is not null && !EstForme(ancienne, signe) || !Enum.IsDefined(type))
                    throw new InvalidDataException("Histoire d'une langue corrompue dans la sauvegarde.");
                Trouver(l.GetInt64(0))?.HistoireLangue.Add(new HistoireMot(idee, forme, ancienne, signe, l.GetInt64(5), l.GetInt32(6), type,
                    l.GetInt32(8), l.GetInt64(9)));
            }
        }
    }

    private static void EnregistrerLangues(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Communaute> communautes)
    {
        Executer(connexion, transaction, "DELETE FROM langues; DELETE FROM langue_histoire;");
        using var langues = Preparer(connexion, transaction, """
            INSERT INTO langues (communaute, sons, signes, stade, ordre, parole_le, combinaison_le, ordre_le, palier, langue)
            VALUES ($c, $so, $si, $st, $o, $pl, $cl, $ol, $pa, $la);
            """, "$c", "$so", "$si", "$st", "$o", "$pl", "$cl", "$ol", "$pa", "$la");
        using var histoire = Preparer(connexion, transaction, """
            INSERT INTO langue_histoire (communaute, rang, idee, forme, ancienne, signe, le, auteur, type, source, evenement)
            VALUES ($c, $r, $i, $f, $an, $s, $l, $a, $t, $so, $e);
            """, "$c", "$r", "$i", "$f", "$an", "$s", "$l", "$a", "$t", "$so", "$e");
        foreach (var c in communautes)
        {
            if (c.Sons is null && c.Signes.Count == 0 && c.HistoireLangue.Count == 0 && c.Stade == 0 && c.LangueId == 0) continue;
            Lancer(langues, c.Id, (object?)c.Sons?.Ecrire() ?? DBNull.Value, EcrireSignes(c.Signes), (int)c.Stade, (int)c.Ordre,
                c.ParoleLe, c.CombinaisonLe, c.OrdreLe, c.PalierMots, c.LangueId);
            for (var r = 0; r < c.HistoireLangue.Count; r++)
            {
                var x = c.HistoireLangue[r];
                if (!EstIdee(x.Idee) || !EstForme(x.Forme, x.Signe)) throw new ArgumentException($"Mot invalide : {x.Idee}={x.Forme}.");
                Lancer(histoire, c.Id, r, x.Idee, x.Forme, (object?)x.Ancienne ?? DBNull.Value, x.Signe, x.Le, x.AuteurId, (int)x.Type, x.SourceId,
                    x.EvenementId);
            }
        }
    }
}
