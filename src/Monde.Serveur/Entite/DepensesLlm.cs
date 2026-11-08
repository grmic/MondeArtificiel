using System.Globalization;
using System.Text.Json;

namespace Monde.Serveur.Entite;

/// <summary>Un modèle du cloud dont le prix est connu (dollars par million de jetons lus et écrits), proposé au joueur.</summary>
public sealed record PrixModele(string Fournisseur, string Modele, double Lu, double Ecrit, bool Conseille);

/// <summary>État de la dépense du mois, pour l'affichage.</summary>
/// <param name="Alerte">Vrai à partir de 80 % du plafond.</param>
public sealed record DepensesDto(string Mois, double Dollars, long Appels, long JetonsLus, long JetonsEcrits, long SansPrix, double Plafond, bool Atteint,
    bool Alerte, bool Compris);

/// <summary>
/// Ce que coûtent les LLM du cloud (0.38.0) : les jetons réellement facturés (renvoyés par le service) sont comptés pour le mois en
/// cours, dans <c>depenses-llm.json</c> à côté du dossier des mondes (tous les mondes de l'installation ensemble, comme la facture).
/// Un plafond mensuel suspend les appels quand il est atteint. Les prix viennent du réglage de la connexion.
/// </summary>
public class DepensesLlm
{
    /// <summary>Sans fichier (tests, outils) : rien n'est compté, rien n'est suspendu.</summary>
    public static readonly DepensesLlm Aucune = new(() => null);

    /// <summary>Plafond par défaut : 5 dollars par mois.</summary>
    public const double PlafondParDefaut = 5;

    public const double PlafondMax = 10_000;

    public const string NomFichier = "depenses-llm.json";

    /// <summary>
    /// Prix relevés le 8/11/2026 (Anthropic : tarif officiel ; les autres : relevés publics, à vérifier chez le fournisseur). Le premier
    /// conseillé de chaque fournisseur est pré-rempli : bon marché, assez bon pour jouer l'Entité.
    /// </summary>
    public static readonly PrixModele[] Connus =
    [
        new(ConnexionLlm.OpenAI, "gpt-5.4-mini", 0.75, 4.50, true),
        new(ConnexionLlm.OpenAI, "gpt-5.4-nano", 0.20, 1.25, false),
        new(ConnexionLlm.OpenAI, "gpt-5.4", 2.50, 15.00, false),
        new(ConnexionLlm.OpenAI, "gpt-5.5", 5.00, 30.00, false),
        new(ConnexionLlm.Anthropic, "claude-haiku-5-5", 0.10, 0.50, true),
        new(ConnexionLlm.Anthropic, "claude-sonnet-5-5", 2.00, 10.00, false),
        new(ConnexionLlm.Anthropic, "claude-opus-5-5", 4.00, 20.00, false),
        new(ConnexionLlm.Mistral, "mistral-large-latest", 0.50, 2.00, true),
        new(ConnexionLlm.Mistral, "mistral-small-latest", 0.15, 0.60, false),
        new(ConnexionLlm.Mistral, "mistral-medium-latest", 2.00, 8.00, false),
        new(ConnexionLlm.Infomaniak, "mistralai/Mistral-Small-4-119B-2603", 0.25, 0.93, true),
        new(ConnexionLlm.Infomaniak, "google/gemma-4-31B-it", 0.25, 0.50, false),
        new(ConnexionLlm.Infomaniak, "Qwen/Qwen3.5-397B-A17B-FP8", 0.99, 4.00, false),
        new(ConnexionLlm.Infomaniak, "swiss-ai/Apertus-v1.5-70B", 0.87, 3.00, false),
    ];

    /// <summary>Le prix connu d'un modèle (null : inconnu).</summary>
    public static PrixModele? Prix(string fournisseur, string modele) =>
        Connus.FirstOrDefault(p => p.Fournisseur == fournisseur && string.Equals(p.Modele, modele, StringComparison.OrdinalIgnoreCase));

    /// <summary>La dépense comptée pendant un échange (voir <see cref="Mesurer"/>).</summary>
    public sealed class Mesure
    {
        public long JetonsLus;
        public long JetonsEcrits;
        public double Dollars;
    }

    private static readonly AsyncLocal<Mesure?> Courante = new();

    /// <summary>
    /// Commence à mesurer ce que coûtent les appels faits à partir d'ici, dans ce fil d'exécution (les <c>await</c> compris) : pour noter
    /// le coût d'un échange de l'Entité.
    /// </summary>
    public static Mesure Mesurer() => Courante.Value = new Mesure();

    private sealed record Fichier(string Mois, double Dollars, long Appels, long JetonsLus, long JetonsEcrits, long SansPrix, double Plafond, bool Compris);

    private readonly Func<string?> _dossier;
    private readonly Lock _verrou = new();
    private Fichier? _etat;

    public DepensesLlm(ServiceMonde service) : this(() => Path.GetDirectoryName(Path.GetDirectoryName(service.Depot.CheminFichier)!))
    {
    }

    private DepensesLlm(Func<string?> dossier) => _dossier = dossier;

    /// <summary>Gardé dans ce dossier (tests, outils).</summary>
    public static DepensesLlm Dans(string dossier) => new(() => dossier);

    /// <summary>Mois en cours (heure de la machine), « 2026-11 ».</summary>
    public Func<string> MoisCourant { get; set; } = () => DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private string? Chemin => _dossier() is { } d ? Path.Combine(d, NomFichier) : null;

    private Fichier Lire()
    {
        var mois = MoisCourant();
        if (_etat is null)
        {
            _etat = new Fichier(mois, 0, 0, 0, 0, 0, PlafondParDefaut, false);
            try
            {
                if (Chemin is { } c && File.Exists(c) && JsonSerializer.Deserialize<Fichier>(File.ReadAllText(c), Actes.Json) is { } lu)
                    _etat = lu with { Plafond = double.IsFinite(lu.Plafond) ? Math.Clamp(lu.Plafond, 0, PlafondMax) : PlafondParDefaut };
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
        // Nouveau mois : la dépense repart de zéro, le plafond et l'avertissement lu restent.
        if (_etat.Mois != mois) _etat = new Fichier(mois, 0, 0, 0, 0, 0, _etat.Plafond, _etat.Compris);
        return _etat;
    }

    private void Ecrire(Fichier f)
    {
        _etat = f;
        if (Chemin is not { } c) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(c)!);
            File.WriteAllText(c, JsonSerializer.Serialize(f, Actes.Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Une panne d'écriture ne gêne pas le jeu ; le compte reste en mémoire.
        }
    }

    /// <summary>Note les jetons facturés par un appel ; rend son coût (0 si le prix n'est pas connu).</summary>
    public double Noter(ConnexionLlm c, long lus, long ecrits)
    {
        lus = Math.Max(0, lus);
        ecrits = Math.Max(0, ecrits);
        var dollars = (lus * c.PrixLu + ecrits * c.PrixEcrit) / 1_000_000.0;
        if (Courante.Value is { } m)
        {
            Interlocked.Add(ref m.JetonsLus, lus);
            Interlocked.Add(ref m.JetonsEcrits, ecrits);
            lock (m) m.Dollars += dollars;
        }
        if (_dossier() is null) return dollars;
        lock (_verrou)
        {
            var f = Lire();
            Ecrire(f with
            {
                Dollars = f.Dollars + dollars, Appels = f.Appels + 1, JetonsLus = f.JetonsLus + lus, JetonsEcrits = f.JetonsEcrits + ecrits,
                SansPrix = f.SansPrix + (c.PrixConnu ? 0 : 1),
            });
        }
        return dollars;
    }

    /// <summary>Vrai quand le plafond du mois est atteint (0 : sans plafond).</summary>
    public bool Atteint
    {
        get
        {
            if (_dossier() is null) return false;
            lock (_verrou)
            {
                var f = Lire();
                return f.Plafond > 0 && f.Dollars >= f.Plafond;
            }
        }
    }

    /// <summary>Lève <see cref="LlmSuspenduException"/> si le plafond est atteint.</summary>
    public void Verifier()
    {
        if (Atteint) throw new LlmSuspenduException(MessagePlafond());
    }

    public string MessagePlafond()
    {
        var e = Etat();
        return L.T("llm.plafond.atteint", ("depense", Dollars(e.Dollars)), ("plafond", Dollars(e.Plafond)));
    }

    /// <summary>Un montant en dollars, lisible (« 0,0123 $ » ou « 1,25 $ »).</summary>
    public static string Dollars(double d) =>
        (d < 0.1 ? d.ToString("0.####", L.Courante.Culture) : d.ToString("0.00", L.Courante.Culture)) + " $";

    public DepensesDto Etat()
    {
        if (_dossier() is null) return new DepensesDto(MoisCourant(), 0, 0, 0, 0, 0, 0, false, false, false);
        lock (_verrou)
        {
            var f = Lire();
            var atteint = f.Plafond > 0 && f.Dollars >= f.Plafond;
            return new DepensesDto(f.Mois, Math.Round(f.Dollars, 6), f.Appels, f.JetonsLus, f.JetonsEcrits, f.SansPrix, f.Plafond, atteint,
                f.Plafond > 0 && f.Dollars >= 0.8 * f.Plafond, f.Compris);
        }
    }

    /// <summary>Change le plafond mensuel (dollars ; 0 : sans plafond).</summary>
    public void Regler(double plafond)
    {
        if (!double.IsFinite(plafond) || plafond is < 0 or > PlafondMax) throw new ArgumentException(L.T("llm.erreur.plafond"));
        lock (_verrou) Ecrire(Lire() with { Plafond = Math.Round(plafond, 2) });
    }

    /// <summary>Le joueur a lu l'avertissement (ce qui part chez le fournisseur, ce que ça coûte).</summary>
    public void Comprendre()
    {
        lock (_verrou) Ecrire(Lire() with { Compris = true });
    }
}
