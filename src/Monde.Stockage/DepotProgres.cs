using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Stockage;

/// <summary>Le progrès : savoirs vivants année après année, et les savoirs perdus ou retrouvés.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Une perte ou une retrouvaille de savoir, pour les marques de la courbe.</summary>
    public sealed record MarqueProgres(long Secondes, long Evenement, bool Perdu, string Texte, long Cause = 0, Simulation.Recits.Recit? Recit = null);

    /// <summary>Migration vers la version 33 : mesures annuelles des savoirs vivants. Les mondes existants commencent à zéro.</summary>
    private static void MigrerVersion33(SqliteConnection connexion, SqliteTransaction transaction) =>
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS progres (
                annee      INTEGER NOT NULL,
                communaute INTEGER NOT NULL,
                savoirs    INTEGER NOT NULL,
                PRIMARY KEY (annee, communaute)
            );
            """);

    private static List<MesureSavoirs> LireProgres(SqliteCommand commande)
    {
        var liste = new List<MesureSavoirs>();
        commande.CommandText = "SELECT annee, communaute, savoirs FROM progres ORDER BY annee, communaute;";
        using var l = commande.ExecuteReader();
        while (l.Read()) liste.Add(new MesureSavoirs(l.GetInt32(0), l.GetInt32(1), l.GetInt32(2)));
        return liste;
    }

    private static void EnregistrerProgres(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<MesureSavoirs> progres)
    {
        Executer(connexion, transaction, "DELETE FROM progres;");
        using var c = Preparer(connexion, transaction, "INSERT INTO progres (annee, communaute, savoirs) VALUES ($a, $c, $s);", "$a", "$c", "$s");
        foreach (var m in progres) Lancer(c, m.Annee, m.CommunauteId, m.Savoirs);
    }

    /// <summary>Pertes et retrouvailles de savoirs notées au journal, dans l'ordre.</summary>
    public List<MarqueProgres> MarquesProgres()
    {
        var marques = new List<MarqueProgres>();
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = """
            SELECT secondes, id, texte, cause, forme, valeurs FROM journal
            WHERE type = $t AND significatif = 1 AND (texte LIKE 'Plus personne ne sait%' OR texte LIKE 'Savoir retrouvé%'
                OR forme IN (SELECT id FROM recits_formes WHERE cle IN ('perdu.savoir', 'ing.retrouve')))
            ORDER BY secondes, id;
            """;
        commande.Parameters.AddWithValue("$t", (int)TypeEvenement.Decouverte);
        var formes = FormesRecits.Lire(connexion, null);
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var texte = l.GetString(2);
            var recit = formes.Lire(l, 4, 5);
            var perdu = recit is null ? texte.StartsWith("Plus personne", StringComparison.Ordinal) : recit.Cle == "perdu.savoir";
            marques.Add(new MarqueProgres(l.GetInt64(0), l.GetInt64(1), perdu, recit?.Texte ?? texte, l.GetInt64(3), recit));
        }
        return marques;
    }

    /// <summary>
    /// Savoirs vivants du monde entier au début de chaque année avant <paramref name="avant"/>, reconstitués pour un monde
    /// d'avant la 0.29.1 : chaque premier effet obtenu au monde en ajoute un, chaque perte notée au journal en retire un,
    /// chaque retrouvaille en rend un. Les savoirs des plantes (poisons, visions, remèdes) n'y comptent pas, comme dans la mesure.
    /// </summary>
    public List<MesureSavoirs> ReconstituerProgres(int avant, IReadOnlyList<MarqueProgres> marques)
    {
        var pas = new List<(long Secondes, int Delta)>();
        using (var connexion = Ouvrir())
        using (var commande = connexion.CreateCommand())
        {
            commande.CommandText = "SELECT effet, MIN(le) FROM decouvertes GROUP BY effet;";
            using var l = commande.ExecuteReader();
            while (l.Read())
            {
                if ((Effet)l.GetInt32(0) is Effet.Rien or Effet.Poison or Effet.Visions or Effet.Soulagement) continue;
                pas.Add((l.GetInt64(1), 1));
            }
        }
        pas.AddRange(marques.Select(m => (m.Secondes, m.Perdu ? -1 : 1)));
        pas.Sort((a, b) => a.Secondes.CompareTo(b.Secondes));
        var resultat = new List<MesureSavoirs>();
        var i = 0;
        var n = 0;
        for (var annee = 1; annee < avant; annee++)
        {
            var debut = (annee - 1) * Calendrier.SecondesParAn;
            while (i < pas.Count && pas[i].Secondes < debut) n += pas[i++].Delta;
            resultat.Add(new MesureSavoirs(annee, 0, Math.Max(0, n)));
        }
        return resultat;
    }
}
