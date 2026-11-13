using Microsoft.Data.Sqlite;

namespace Monde.Stockage;

/// <summary>
/// Sauvegarde différentielle des humains (0.40.0). Avant, chaque sauvegarde effaçait puis réécrivait tous les humains nés depuis
/// le début, morts compris, avec leurs souvenirs, leurs savoirs et leurs mots : sa durée grandissait avec l'âge du monde.
/// Désormais, le dépôt garde pour chaque humain et chaque table une empreinte de ce qu'il y a écrit ; il ne réécrit que ce qui
/// a changé. Ce qui est écrit est exactement ce qu'aurait écrit la sauvegarde complète.
/// </summary>
public sealed partial class DepotMonde
{
    // Empreinte de ce qui est dans la base, par table (ou groupe de colonnes) et par humain. Vide : on ne sait rien, tout est réécrit.
    private readonly Dictionary<string, Dictionary<int, ulong>> _empreintes = new(StringComparer.Ordinal);

    /// <summary>La base a été remplacée ou rechargée : les empreintes ne disent plus ce qu'elle contient.</summary>
    private void OublierEmpreintes()
    {
        lock (_empreintes) _empreintes.Clear();
    }

    /// <summary>Humains réécrits par la dernière sauvegarde (pour les mesures et les tests).</summary>
    public int HumainsReecrits { get; private set; }

    /// <summary>
    /// Ce qu'une sauvegarde en cours a écrit, gardé à part : les empreintes ne sont retenues qu'une fois la transaction validée.
    /// </summary>
    private sealed class Ecritures(DepotMonde depot)
    {
        private readonly Dictionary<string, Dictionary<int, ulong>> _nouvelles = new(StringComparer.Ordinal);
        private readonly HashSet<string> _tablesInconnues = [];

        /// <summary>Humains dont la ligne de <c>humains</c> a été remplacée : leurs autres colonnes sont à réécrire aussi.</summary>
        public HashSet<int> Remplaces { get; } = [];

        /// <summary>Vrai si l'on ne sait pas ce que contient cette table : elle est effacée et réécrite en entier.</summary>
        public bool Inconnue(string cle)
        {
            lock (depot._empreintes)
            {
                if (depot._empreintes.ContainsKey(cle)) return false;
            }
            _tablesInconnues.Add(cle);
            return true;
        }

        /// <summary>Faut-il écrire ce que cet humain a dans cette table ? Retient l'empreinte, à valider avec la transaction.</summary>
        public bool AEcrire(string cle, int humain, ulong empreinte, bool forcer)
        {
            if (!_nouvelles.TryGetValue(cle, out var table)) _nouvelles[cle] = table = [];
            table[humain] = empreinte;
            if (forcer || _tablesInconnues.Contains(cle)) return true;
            lock (depot._empreintes)
            {
                return !depot._empreintes.TryGetValue(cle, out var connues) || !connues.TryGetValue(humain, out var avant) || avant != empreinte;
            }
        }

        /// <summary>La transaction est validée : la base contient désormais ce qui a été écrit.</summary>
        public void Valider()
        {
            lock (depot._empreintes)
            {
                foreach (var (cle, table) in _nouvelles)
                {
                    if (!depot._empreintes.TryGetValue(cle, out var connues)) depot._empreintes[cle] = connues = [];
                    foreach (var (humain, empreinte) in table) connues[humain] = empreinte;
                }
            }
        }
    }

    /// <summary>
    /// Les lignes d'une table qui appartiennent à un humain (ses mots, ses savoirs, ses souvenirs…), ou un groupe de colonnes de
    /// sa ligne dans <c>humains</c> (<paramref name="effacer"/> nul). On donne les lignes d'un humain entre <see cref="Debut"/> et
    /// <see cref="Fin"/> ; elles ne sont écrites que si elles diffèrent de celles de la sauvegarde précédente.
    /// </summary>
    private sealed class ParHumain : IDisposable
    {
        private readonly Ecritures _ecritures;
        private readonly string _cle;
        private readonly SqliteCommand _inserer;
        private readonly SqliteCommand? _effacer;
        private readonly bool _inconnue;
        private readonly bool _suitLaLigne;
        private readonly List<object[]> _lignes = [];
        private int _humain;
        private ulong _empreinte;

        /// <param name="effacer">Requête « DELETE … WHERE humain = $h », ou null pour des colonnes de la ligne de l'humain.</param>
        /// <param name="toutEffacer">Requête qui vide la table, lancée si l'on ne sait pas ce qu'elle contient.</param>
        /// <param name="suitLaLigne">Colonnes mises à jour après la ligne <c>humains</c> : à réécrire quand celle-ci est remplacée.</param>
        public ParHumain(SqliteConnection connexion, SqliteTransaction transaction, Ecritures ecritures, string cle, SqliteCommand inserer,
            string? effacer = null, string? toutEffacer = null, bool suitLaLigne = false)
        {
            _ecritures = ecritures;
            _cle = cle;
            _inserer = inserer;
            _suitLaLigne = suitLaLigne;
            _inconnue = ecritures.Inconnue(cle);
            if (_inconnue && toutEffacer is not null) Executer(connexion, transaction, toutEffacer);
            if (effacer is not null) _effacer = Preparer(connexion, transaction, effacer, "$h");
        }

        public void Debut(int humain)
        {
            _humain = humain;
            _lignes.Clear();
            _empreinte = Empreinte.Depart;
        }

        public void Ligne(params object[] valeurs)
        {
            _lignes.Add(valeurs);
            _empreinte = Empreinte.Ajouter(_empreinte, valeurs);
        }

        /// <summary>Écrit les lignes de l'humain si elles ont changé ; renvoie vrai si elles ont été écrites.</summary>
        public bool Fin()
        {
            var forcer = _suitLaLigne && _ecritures.Remplaces.Contains(_humain);
            if (!_ecritures.AEcrire(_cle, _humain, _empreinte, forcer)) return false;
            if (_effacer is not null && !_inconnue) Lancer(_effacer, _humain);
            foreach (var l in _lignes) Lancer(_inserer, l);
            return true;
        }

        public void Dispose()
        {
            _inserer.Dispose();
            _effacer?.Dispose();
        }
    }

    /// <summary>Empreinte de 64 bits (FNV-1a) des valeurs écrites, avec leur type et leur nombre.</summary>
    private static class Empreinte
    {
        public const ulong Depart = 14695981039346656037UL;
        private const ulong Premier = 1099511628211UL;

        private static ulong Octet(ulong h, byte b) => (h ^ b) * Premier;

        private static ulong Mot(ulong h, ulong v)
        {
            for (var i = 0; i < 8; i++)
            {
                h = Octet(h, (byte)v);
                v >>= 8;
            }
            return h;
        }

        public static ulong Ajouter(ulong h, object[] valeurs)
        {
            h = Mot(h, (ulong)valeurs.Length);
            foreach (var v in valeurs)
            {
                switch (v)
                {
                    case null or DBNull: h = Octet(h, 0); break;
                    case int i: h = Mot(Octet(h, 1), unchecked((ulong)i)); break;
                    case long l: h = Mot(Octet(h, 2), unchecked((ulong)l)); break;
                    case double d: h = Mot(Octet(h, 3), unchecked((ulong)BitConverter.DoubleToInt64Bits(d))); break;
                    case bool b: h = Octet(Octet(h, 4), b ? (byte)1 : (byte)0); break;
                    case string s:
                        h = Mot(Octet(h, 5), (ulong)s.Length);
                        foreach (var c in s) h = Octet(Octet(h, (byte)c), (byte)(c >> 8));
                        break;
                    case byte[] o:
                        h = Mot(Octet(h, 6), (ulong)o.Length);
                        foreach (var x in o) h = Octet(h, x);
                        break;
                    default:
                        // Autre type (énumération, float…) : par son texte invariant, préfixé de son type.
                        var texte = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        h = Mot(Octet(h, 7), (ulong)texte.Length);
                        foreach (var c in v.GetType().FullName ?? "") h = Octet(h, (byte)c);
                        foreach (var c in texte) h = Octet(Octet(h, (byte)c), (byte)(c >> 8));
                        break;
                }
            }
            return h;
        }
    }
}
