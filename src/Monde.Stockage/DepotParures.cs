using Microsoft.Data.Sqlite;

namespace Monde.Stockage;

/// <summary>Se couvrir, se protéger, se parer (0.42.6) : signe du corps peint et pudeur des communautés.</summary>
public sealed partial class DepotMonde
{
    /// <summary>
    /// Migration vers la version 61 : depuis quand le corps peint est le signe d'une communauté, et depuis quand elle tient la
    /// nudité pour inconvenante. Les communautés existantes n'ont ni l'un ni l'autre. Ce que vit le corps de chacun (pieds,
    /// soleil, piqûres, enduit, chaussures) est gardé dans la colonne humains.corps, en JSON (voir DepotCorps).
    /// </summary>
    private static void MigrerVersion61(SqliteConnection connexion, SqliteTransaction transaction)
    {
        var communautes = Colonnes(connexion, transaction, "communautes");
        if (!communautes.Contains("signe_corps_le")) Executer(connexion, transaction, "ALTER TABLE communautes ADD COLUMN signe_corps_le INTEGER NOT NULL DEFAULT 0;");
        if (!communautes.Contains("pudeur_le")) Executer(connexion, transaction, "ALTER TABLE communautes ADD COLUMN pudeur_le INTEGER NOT NULL DEFAULT 0;");
    }
}
