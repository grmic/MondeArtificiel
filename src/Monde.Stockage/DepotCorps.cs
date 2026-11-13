using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Le corps (0.32.8) : blessure grave en train de guérir et séquelle de chacun ; pousses des camps.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Ce qu'on garde du corps d'un humain, en JSON dans la colonne humains.corps (null : rien à garder).</summary>
    private sealed record Corps(double Grave, OrigineBlessure Origine, Membre Membre, double Debut, double Fixe, long Le, OrigineBlessure Cause,
        double Champs = 0);

    /// <summary>Migration vers la version 45 : séquelles des blessures graves, pousses des camps. Les mondes existants n'en ont pas.</summary>
    private static void MigrerVersion45(SqliteConnection connexion, SqliteTransaction transaction)
    {
        if (!Colonnes(connexion, transaction, "humains").Contains("corps")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN corps TEXT;");
        if (!Colonnes(connexion, transaction, "ressources").Contains("rejets")) Executer(connexion, transaction, "ALTER TABLE ressources ADD COLUMN rejets BLOB;");
    }

    private static void LireCorps(SqliteCommand commande, List<Humain> humains)
    {
        commande.CommandText = "SELECT id, corps FROM humains WHERE corps IS NOT NULL ORDER BY id;";
        using var l = commande.ExecuteReader();
        while (l.Read())
        {
            var id = l.GetInt64(0);
            if (id < 1 || id > humains.Count || LireCorps(l.GetString(1)) is not { } c) continue;
            var h = humains[(int)id - 1];
            (h.BlessureGrave, h.BlessureOrigine) = (c.Grave, c.Origine);
            h.Sequelle = c.Membre == Membre.Aucun ? default : new Sequelle(c.Membre, c.Debut, c.Fixe, c.Le, c.Cause);
            h.PartDesChamps = Math.Clamp(c.Champs, 0, 1);
        }
    }

    /// <summary>Relit le corps enregistré ; null s'il est illisible ou hors des valeurs connues.</summary>
    private static Corps? LireCorps(string json)
    {
        try
        {
            var c = JsonSerializer.Deserialize<Corps>(json);
            return c is not null && Enum.IsDefined(c.Origine) && Enum.IsDefined(c.Membre) && Enum.IsDefined(c.Cause)
                   && double.IsFinite(c.Grave) && double.IsFinite(c.Debut) && double.IsFinite(c.Fixe) && double.IsFinite(c.Champs) ? c : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnregistrerCorps(SqliteConnection connexion, SqliteTransaction transaction, Ecritures ecritures, IReadOnlyList<Humain> humains)
    {
        using var c = new ParHumain(connexion, transaction, ecritures, "humains.corps",
            Preparer(connexion, transaction, "UPDATE humains SET corps = $c WHERE id = $id;", "$c", "$id"), suitLaLigne: true);
        foreach (var h in humains)
        {
            // Rien à dire du corps : la colonne reste vide (une ligne remplacée l'a déjà vidée ; sinon on la vide, si elle a changé).
            c.Debut(h.Id);
            var s = h.Sequelle;
            var rien = h.BlessureGrave <= 0 && s.Membre == Membre.Aucun && h.PartDesChamps <= 0;
            c.Ligne(rien ? DBNull.Value : JsonSerializer.Serialize(new Corps(h.BlessureGrave, h.BlessureOrigine, s.Membre, s.Debut, s.Fixe, s.Le, s.Origine, h.PartDesChamps)), h.Id);
            c.Fin();
        }
    }
}
