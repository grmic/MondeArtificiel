using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Stockage;

/// <summary>Les prédateurs (0.45.0) : carnassiers connus des humains, cases où l'on en a tué, et le compte des attaques.</summary>
public sealed partial class DepotMonde
{
    /// <summary>Ce qu'on garde des carnassiers du monde en dehors des bêtes connues.</summary>
    private sealed record EtatFauves(int[] Cases, double[] Manques, int Attaques, int Morts, AttaqueDeBete[]? Recentes = null);

    /// <summary>Migration vers la version 63 : carnassiers connus (une ligne chacun, en JSON) et état des prédateurs. Les mondes existants n'en ont pas.</summary>
    private static void MigrerVersion63(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS fauves (
                id          INTEGER PRIMARY KEY,
                espece      INTEGER NOT NULL,
                donnees     TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS fauves_etat (
                id          INTEGER PRIMARY KEY CHECK (id = 1),
                donnees     TEXT NOT NULL
            );
            """);
    }

    private static EtatMonde LireFauves(SqliteCommand commande, EtatMonde etat)
    {
        var fauves = LireJson<Fauve>(commande, "SELECT donnees FROM fauves ORDER BY id;",
            f => f.Id > 0 && f.Espece > 0 && double.IsFinite(f.Age) && double.IsFinite(f.Blessure) && double.IsFinite(f.Audace) && double.IsFinite(f.Habitude));
        if (fauves.Count > 0) etat = etat with { Fauves = fauves };
        var e = LireJson<EtatFauves>(commande, "SELECT donnees FROM fauves_etat WHERE id = 1;",
            x => x.Cases is not null && x.Manques is not null && x.Cases.Length == x.Manques.Length);
        if (e.Count == 0) return etat;
        var manquants = new List<KeyValuePair<int, double>>();
        for (var k = 0; k < e[0].Cases.Length; k++)
            if (double.IsFinite(e[0].Manques[k]) && e[0].Cases[k] >= 0) manquants.Add(new(e[0].Cases[k], Math.Clamp(e[0].Manques[k], 0, 0.9)));
        var recentes = (e[0].Recentes ?? []).Where(a => a is not null && a.Espece > 0 && a.X >= 0 && a.Y >= 0).ToList();
        return etat with
        {
            FauvesManquants = manquants, AttaquesDeBetes = Math.Max(0, e[0].Attaques), MortsDeBetes = Math.Max(0, e[0].Morts),
            AttaquesRecentes = recentes.Count == 0 ? null : recentes,
        };
    }

    private static void EnregistrerFauves(SqliteConnection connexion, SqliteTransaction transaction, EtatMonde etat)
    {
        Executer(connexion, transaction, "DELETE FROM fauves; DELETE FROM fauves_etat;");
        using (var c = Preparer(connexion, transaction, "INSERT INTO fauves (id, espece, donnees) VALUES ($i, $e, $d);", "$i", "$e", "$d"))
            foreach (var f in etat.Fauves ?? []) Lancer(c, f.Id, f.Espece, JsonSerializer.Serialize(f));
        var manquants = etat.FauvesManquants ?? [];
        if (manquants.Count == 0 && etat.AttaquesDeBetes == 0 && etat.MortsDeBetes == 0 && (etat.AttaquesRecentes?.Count ?? 0) == 0) return;
        using (var c = Preparer(connexion, transaction, "INSERT INTO fauves_etat (id, donnees) VALUES (1, $d);", "$d"))
            Lancer(c, JsonSerializer.Serialize(new EtatFauves([.. manquants.Select(m => m.Key)], [.. manquants.Select(m => m.Value)], etat.AttaquesDeBetes,
                etat.MortsDeBetes, etat.AttaquesRecentes is { Count: > 0 } r ? [.. r] : null)));
    }
}
