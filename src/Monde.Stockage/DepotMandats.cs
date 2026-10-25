using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Les paroles de l'Entité adressées à des communautés (0.31.6) et ce que les groupes en ont fait.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Migration vers la version 40 : paroles adressées aux communautés. Les mondes existants n'en ont reçu aucune.</summary>
    private static void MigrerVersion40(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS mandats (
                id          INTEGER PRIMARY KEY,
                communaute  INTEGER NOT NULL,
                cible       INTEGER NOT NULL,
                parole      INTEGER NOT NULL,
                etat        INTEGER NOT NULL,
                secondes    INTEGER NOT NULL,
                donnees     TEXT NOT NULL
            );
            """);
    }

    private static List<Mandat> LireMandats(SqliteCommand commande)
    {
        var liste = new List<Mandat>();
        commande.CommandText = "SELECT donnees FROM mandats ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            try
            {
                if (JsonSerializer.Deserialize<Mandat>(l.GetString(0)) is { } m && Enum.IsDefined(m.Parole) && Paroles.Collective(m.Parole) && Enum.IsDefined(m.Etat))
                    liste.Add(m);
            }
            catch (JsonException)
            {
                // Un mandat illisible est oublié.
            }
        }
        // Les numéros se suivent à partir de 1 : un trou (mandat illisible) les décalerait.
        return liste.Where((m, i) => m.Id == i + 1).ToList();
    }

    private static void EnregistrerMandats(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Mandat> mandats)
    {
        Executer(connexion, transaction, "DELETE FROM mandats;");
        using var c = Preparer(connexion, transaction, """
            INSERT INTO mandats (id, communaute, cible, parole, etat, secondes, donnees) VALUES ($i, $c, $b, $p, $e, $s, $d);
            """, "$i", "$c", "$b", "$p", "$e", "$s", "$d");
        foreach (var m in mandats)
            Lancer(c, m.Id, m.CommunauteId, m.CibleId, (int)m.Parole, (int)m.Etat, m.Le, JsonSerializer.Serialize(m));
    }
}
