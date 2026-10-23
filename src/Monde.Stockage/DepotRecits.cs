using Microsoft.Data.Sqlite;
using Monde.Simulation.Humains;
using Monde.Simulation.Recits;

namespace Monde.Stockage;

/// <summary>
/// Journal et pensées enregistrés comme récits (0.30.1), pour que la phrase s'écrive à l'affichage dans la langue de
/// l'observateur. Chaque ligne garde le numéro de sa « forme » (clé du fichier de langue et noms des paramètres, écrits
/// une seule fois dans <c>recits_formes</c>) et ses valeurs en tableau JSON. Une ligne d'avant garde sa phrase française
/// dans <c>texte</c> et n'a pas de forme.
/// </summary>
public sealed partial class DepotMonde
{
    private static void MigrerVersion35(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS recits_formes (
                id   INTEGER PRIMARY KEY,
                cle  TEXT NOT NULL,
                noms TEXT NOT NULL
            );
            """);
        foreach (var table in new[] { "journal", "pensees" })
        {
            var colonnes = Colonnes(connexion, transaction, table);
            if (!colonnes.Contains("forme")) Executer(connexion, transaction, $"ALTER TABLE {table} ADD COLUMN forme INTEGER;");
            if (!colonnes.Contains("valeurs")) Executer(connexion, transaction, $"ALTER TABLE {table} ADD COLUMN valeurs TEXT;");
        }
    }

    /// <summary>Les formes de récit d'un monde, lues d'un coup ; celles qu'un enregistrement ajoute sont écrites à la fin.</summary>
    private sealed class FormesRecits(SqliteConnection connexion, SqliteTransaction? transaction)
    {
        private readonly Dictionary<(string Cle, string Noms), int> _numeros = [];
        private Dictionary<int, Personne>? _humains;
        private readonly List<(string Cle, string Noms)> _parNumero = [("", "")];
        private int _dejaEnregistrees = 1;

        public static FormesRecits Lire(SqliteConnection connexion, SqliteTransaction? transaction)
        {
            var formes = new FormesRecits(connexion, transaction);
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = "SELECT id, cle, noms FROM recits_formes ORDER BY id;";
            using var l = commande.ExecuteReader();
            while (l.Read())
            {
                var id = l.GetInt32(0);
                while (formes._parNumero.Count < id) formes._parNumero.Add(("", ""));
                var forme = (l.GetString(1), l.GetString(2));
                formes._parNumero.Add(forme);
                formes._numeros[forme] = id;
            }
            formes._dejaEnregistrees = formes._parNumero.Count;
            return formes;
        }

        public int Numero(Recit recit)
        {
            var forme = (recit.Cle, recit.Noms);
            if (_numeros.TryGetValue(forme, out var id)) return id;
            id = _parNumero.Count;
            _parNumero.Add(forme);
            _numeros[forme] = id;
            return id;
        }

        public (string Cle, string Noms) Forme(int id) => id > 0 && id < _parNumero.Count ? _parNumero[id] : ("?", "");

        /// <summary>Un humain cité, par son numéro : nom et sexe lus dans la table des humains (ils ne changent jamais).</summary>
        public Personne Humain(int id)
        {
            if (_humains is null)
            {
                _humains = [];
                using var commande = connexion.CreateCommand();
                commande.Transaction = transaction;
                commande.CommandText = "SELECT id, nom, sexe FROM humains;";
                using var l = commande.ExecuteReader();
                while (l.Read()) _humains[l.GetInt32(0)] = new Personne(l.GetInt32(0), l.GetString(1), l.GetByte(2) == (byte)Sexe.Femme);
            }
            return _humains.TryGetValue(id, out var p) ? p : new Personne(id, "?", false);
        }

        public void Enregistrer(SqliteConnection connexion, SqliteTransaction transaction)
        {
            if (_dejaEnregistrees >= _parNumero.Count) return;
            using var commande = Preparer(connexion, transaction, "INSERT INTO recits_formes (id, cle, noms) VALUES ($i, $c, $n);", "$i", "$c", "$n");
            for (var i = _dejaEnregistrees; i < _parNumero.Count; i++) Lancer(commande, i, _parNumero[i].Cle, _parNumero[i].Noms);
            _dejaEnregistrees = _parNumero.Count;
        }

        /// <summary>Les trois colonnes d'un texte : la phrase ancienne (vide pour un récit), la forme et les valeurs.</summary>
        public (object Texte, object Forme, object Valeurs) Colonnes(Recit? recit, string? brut) =>
            recit is null
                ? (brut ?? "", DBNull.Value, DBNull.Value)
                : ("", Numero(recit), recit.Valeurs(Numero) is { Length: > 0 } v ? v : DBNull.Value);

        /// <summary>Le récit d'une ligne (colonnes forme et valeurs), ou null pour une phrase d'avant la 0.30.1.</summary>
        public Recit? Lire(SqliteDataReader l, int forme, int valeurs)
        {
            if (l.IsDBNull(forme)) return null;
            var (cle, noms) = Forme(l.GetInt32(forme));
            return Recit.LireValeurs(cle, noms, l.IsDBNull(valeurs) ? null : l.GetString(valeurs), Forme, Humain);
        }
    }
}
