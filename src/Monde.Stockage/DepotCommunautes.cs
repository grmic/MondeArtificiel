using System.Buffers.Binary;
using System.Text;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Un instantané de saison sans ses terres : la taille d'une communauté à une date (courbe de son histoire).</summary>
public sealed record TailleCommunaute(long Secondes, int Membres, int Adultes);

/// <summary>Identité et histoire des communautés : noms successifs, mots, terres parcourues et archives de saison.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Plus grand nombre de cases qu'un territoire enregistré peut compter (la carte entière, 1024 × 1024 au plus).</summary>
    private const int CasesMax = 1024 * 1024;

    private static void LireNoms(SqliteCommand commande, List<Communaute> communautes)
    {
        commande.CommandText = "SELECT communaute, nom, sens, depuis, evenement FROM noms_communautes ORDER BY communaute, rang;";
        var parId = communautes.ToDictionary(c => c.Id);
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            if (!parId.TryGetValue(l.GetInt32(0), out var c)) continue;
            c.Noms.Add(new NomCommunaute(l.GetString(1), l.IsDBNull(2) ? null : l.GetString(2), l.GetInt64(3), l.GetInt64(4)));
        }
    }

    private static void EnregistrerNoms(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Communaute> communautes)
    {
        Executer(connexion, transaction, "DELETE FROM noms_communautes;");
        using var commande = Preparer(connexion, transaction, """
            INSERT INTO noms_communautes (communaute, rang, nom, sens, depuis, evenement) VALUES ($c, $r, $n, $s, $d, $e);
            """, "$c", "$r", "$n", "$s", "$d", "$e");
        foreach (var c in communautes)
        {
            for (var i = 0; i < c.Noms.Count; i++)
            {
                var n = c.Noms[i];
                Lancer(commande, c.Id, i, n.Nom, (object?)n.Sens ?? DBNull.Value, n.Depuis, n.EvenementId);
            }
        }
    }

    private static void EnregistrerArchives(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<ArchiveCommunaute> archives)
    {
        // INSERT OR IGNORE : un instantané déjà enregistré (même communauté, même date) n'est pas dupliqué.
        using var commande = Preparer(connexion, transaction, """
            INSERT OR IGNORE INTO archives_communautes (communaute, secondes, membres, adultes, cases) VALUES ($c, $s, $m, $a, $x);
            """, "$c", "$s", "$m", "$a", "$x");
        foreach (var a in archives) Lancer(commande, a.CommunauteId, a.Secondes, a.Membres, a.Adultes, EncoderCases(a.Cases));
    }

    /// <summary>Taille d'une communauté au début de chaque saison, de la plus ancienne à la plus récente.</summary>
    public List<TailleCommunaute> LireTailles(int communaute)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT secondes, membres, adultes FROM archives_communautes WHERE communaute = $c ORDER BY secondes;";
        commande.Parameters.AddWithValue("$c", communaute);
        var resultat = new List<TailleCommunaute>();
        using var l = commande.ExecuteReader();
        while (l.Read()) resultat.Add(new TailleCommunaute(l.GetInt64(0), l.GetInt32(1), l.GetInt32(2)));
        return resultat;
    }

    /// <summary>Dates des instantanés enregistrés (début de chaque saison), de la plus ancienne à la plus récente.</summary>
    public List<long> LireDatesArchives()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT DISTINCT secondes FROM archives_communautes ORDER BY secondes;";
        var resultat = new List<long>();
        using var l = commande.ExecuteReader();
        while (l.Read()) resultat.Add(l.GetInt64(0));
        return resultat;
    }

    /// <summary>Instantanés de toutes les communautés à une date donnée.</summary>
    public List<ArchiveCommunaute> LireArchives(long secondes)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT communaute, membres, adultes, cases FROM archives_communautes WHERE secondes = $s ORDER BY communaute;";
        commande.Parameters.AddWithValue("$s", secondes);
        var resultat = new List<ArchiveCommunaute>();
        using var l = commande.ExecuteReader();
        while (l.Read()) resultat.Add(new ArchiveCommunaute(l.GetInt32(0), secondes, l.GetInt32(1), l.GetInt32(2), DecoderCases((byte[])l[3])));
        return resultat;
    }

    // ───────────── Encodages ─────────────

    /// <summary>
    /// Cases d'un territoire (indices triés) en plages : pour chaque plage, l'écart depuis la fin de la précédente
    /// puis sa longueur, en entiers à longueur variable. Un territoire d'un seul tenant tient en quelques centaines d'octets.
    /// </summary>
    public static byte[] EncoderCases(IReadOnlyList<int> cases)
    {
        using var flux = new MemoryStream();
        using var ecrivain = new BinaryWriter(flux);
        var fin = 0;
        var i = 0;
        while (i < cases.Count)
        {
            var debut = cases[i];
            if (debut < fin) throw new ArgumentException("Les cases doivent être triées et distinctes.", nameof(cases));
            var longueur = 1;
            while (i + longueur < cases.Count && cases[i + longueur] == debut + longueur) longueur++;
            ecrivain.Write7BitEncodedInt(debut - fin);
            ecrivain.Write7BitEncodedInt(longueur);
            fin = debut + longueur;
            i += longueur;
        }
        ecrivain.Flush();
        return flux.ToArray();
    }

    public static int[] DecoderCases(byte[] octets)
    {
        var resultat = new List<int>();
        using var lecteur = new BinaryReader(new MemoryStream(octets));
        var fin = 0L;
        while (lecteur.BaseStream.Position < octets.Length)
        {
            var debut = fin + lecteur.Read7BitEncodedInt();
            var longueur = lecteur.Read7BitEncodedInt();
            if (longueur < 1 || debut + longueur > CasesMax || resultat.Count + longueur > CasesMax)
            {
                throw new InvalidDataException("Territoire corrompu dans la sauvegarde.");
            }
            for (var k = 0; k < longueur; k++) resultat.Add((int)debut + k);
            fin = debut + longueur;
        }
        return [.. resultat];
    }

    /// <summary>Terres parcourues : paires (case, passages : jour du dernier et nombre de jours) en petit-boutiste.</summary>
    private static byte[] EcrireTerritoire(Dictionary<int, long> territoire)
    {
        var octets = new byte[territoire.Count * 12];
        var i = 0;
        foreach (var (indice, vu) in territoire.OrderBy(t => t.Key))
        {
            BinaryPrimitives.WriteInt32LittleEndian(octets.AsSpan(i), indice);
            BinaryPrimitives.WriteInt64LittleEndian(octets.AsSpan(i + 4), vu);
            i += 12;
        }
        return octets;
    }

    private static Dictionary<int, long> LireTerritoire(byte[] octets)
    {
        if (octets.Length % 12 != 0 || octets.Length / 12 > CasesMax) throw new InvalidDataException("Territoire corrompu dans la sauvegarde.");
        var territoire = new Dictionary<int, long>(octets.Length / 12);
        for (var i = 0; i < octets.Length; i += 12)
        {
            territoire[BinaryPrimitives.ReadInt32LittleEndian(octets.AsSpan(i))] = BinaryPrimitives.ReadInt64LittleEndian(octets.AsSpan(i + 4));
        }
        return territoire;
    }

    /// <summary>Mots d'un groupe : « idée=mot » séparés par « ; » (idées et mots ne sont faits que de lettres).</summary>
    private static string EcrireLexique(Dictionary<string, string> lexique)
    {
        var texte = new StringBuilder();
        foreach (var (idee, mot) in lexique.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!EstMot(idee) || !EstMot(mot)) throw new ArgumentException($"Mot invalide dans le lexique : {idee}={mot}.");
            if (texte.Length > 0) texte.Append(';');
            texte.Append(idee).Append('=').Append(mot);
        }
        return texte.ToString();
    }

    private static Dictionary<string, string> LireLexique(string texte)
    {
        var lexique = new Dictionary<string, string>();
        foreach (var paire in texte.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var morceaux = paire.Split('=');
            if (morceaux.Length != 2 || !EstMot(morceaux[0]) || !EstMot(morceaux[1])) throw new InvalidDataException("Lexique corrompu dans la sauvegarde.");
            lexique[morceaux[0]] = morceaux[1];
        }
        return lexique;
    }

    private static bool EstMot(string s) => s.Length is > 0 and <= 40 && s.All(char.IsLetter);

    /// <summary>
    /// Migration vers la version 10 : les événements de communauté déjà enregistrés sont rattachés aux communautés
    /// dont leur texte cite le nom (la première citée, puis la seconde pour ce qui se passe entre deux groupes).
    /// Les noms les plus longs sont cherchés d'abord (« communauté de Kaila » avant « communauté de Kai »).
    /// </summary>
    private static void RattacherJournalAuxCommunautes(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var noms = new List<(int Id, string Nom)>();
        using (var commande = connexion.CreateCommand())
        {
            commande.Transaction = transaction;
            commande.CommandText = "SELECT id, nom FROM communautes;";
            using var l = commande.ExecuteReader();
            while (l.Read())
            {
                var nom = l.GetString(1);
                if (nom.Length > 0) noms.Add((l.GetInt32(0), char.ToLowerInvariant(nom[0]) + nom[1..]));
            }
        }
        if (noms.Count == 0) return;
        noms = [.. noms.OrderByDescending(n => n.Nom.Length).ThenBy(n => n.Id)];

        var evenements = new List<(long Id, string Texte)>();
        using (var commande = connexion.CreateCommand())
        {
            commande.Transaction = transaction;
            commande.CommandText = "SELECT id, texte FROM journal WHERE type = $t;";
            commande.Parameters.AddWithValue("$t", (int)TypeEvenement.Communaute);
            using var l = commande.ExecuteReader();
            while (l.Read()) evenements.Add((l.GetInt64(0), l.GetString(1)));
        }
        using var maj = Preparer(connexion, transaction, "UPDATE journal SET communaute = $c, autre_communaute = $a WHERE id = $i;", "$c", "$a", "$i");
        foreach (var (id, texte) in evenements)
        {
            var trouvees = new List<(int Position, int Id)>();
            var pris = new bool[texte.Length];
            foreach (var (cid, nom) in noms)
            {
                for (var p = texte.IndexOf(nom, StringComparison.Ordinal); p >= 0; p = texte.IndexOf(nom, p + 1, StringComparison.Ordinal))
                {
                    var fin = p + nom.Length;
                    // Le nom doit être entier (« Kai » ne doit pas être pris dans « Kaila ») et pas déjà couvert par un nom plus long.
                    if (fin < texte.Length && (char.IsLetter(texte[fin]) || texte[fin] == ' ' && fin + 1 < texte.Length && texte[fin + 1] == '(')) continue;
                    if (pris[p]) continue;
                    for (var k = p; k < fin; k++) pris[k] = true;
                    trouvees.Add((p, cid));
                }
            }
            if (trouvees.Count == 0) continue;
            var ordre = trouvees.OrderBy(t => t.Position).Select(t => t.Id).Distinct().ToList();
            Lancer(maj, ordre[0], ordre.Count > 1 ? ordre[1] : 0, id);
        }
    }
}
