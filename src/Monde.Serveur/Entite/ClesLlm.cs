using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Monde.Serveur.Entite;

/// <summary>
/// Clés d'API des LLM du cloud (0.38.0). Jamais dans la base d'un monde, ni dans <c>entite-llm.json</c>, ni renvoyées au navigateur :
/// elles sont gardées chiffrées (Data Protection d'ASP.NET) dans <c>cles-llm.dat</c>, à côté du dossier des mondes ; les clés qui les
/// chiffrent sont dans <c>cles-protection/</c> (protégées par DPAPI sous Windows, lisibles par le seul utilisateur sous macOS et Linux).
/// Une variable d'environnement (<c>MONDE_CLE_OPENAI</c>…) passe avant le fichier : pratique pour un serveur sans écran.
/// <para>Chaque clé est liée à un hôte : elle ne part que vers lui, quelle que soit l'adresse qu'on demande.</para>
/// </summary>
public class ClesLlm
{
    /// <summary>Sans clés (tests, outils).</summary>
    public static readonly ClesLlm Aucune = new(() => null);

    private sealed record Entree(string Cle, string Hote);

    private readonly Func<string?> _dossier;
    private readonly Lock _verrou = new();
    private IDataProtector? _protection;
    private Dictionary<string, Entree>? _cles;

    public ClesLlm(ServiceMonde service) : this(() => Path.GetDirectoryName(Path.GetDirectoryName(service.Depot.CheminFichier)!))
    {
    }

    private ClesLlm(Func<string?> dossier) => _dossier = dossier;

    /// <summary>Gardé dans ce dossier (tests, outils).</summary>
    public static ClesLlm Dans(string dossier) => new(() => dossier);

    public const string NomFichier = "cles-llm.dat";

    /// <summary>Nom de la variable d'environnement d'un fournisseur (et, pour « Autre », de son hôte : <c>MONDE_CLE_AUTRE_HOTE</c>).</summary>
    public static string Variable(string fournisseur) => "MONDE_CLE_" + fournisseur.ToUpperInvariant();

    private string? Fichier => _dossier() is { } d ? Path.Combine(d, NomFichier) : null;

    private IDataProtector Protection()
    {
        if (_protection is not null) return _protection;
        var dossier = Path.Combine(_dossier() ?? throw new InvalidOperationException(L.T("llm.erreur.pasDeCle")), "cles-protection");
        Directory.CreateDirectory(dossier);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dossier, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var fournisseur = DataProtectionProvider.Create(new DirectoryInfo(dossier), config =>
        {
            config.SetApplicationName("MondeArtificiel");
            if (OperatingSystem.IsWindows()) config.ProtectKeysWithDpapi();
        });
        return _protection = fournisseur.CreateProtector("MondeArtificiel.ClesLlm.v1");
    }

    private Dictionary<string, Entree> Lire()
    {
        if (_cles is not null) return _cles;
        var cles = new Dictionary<string, Entree>(StringComparer.Ordinal);
        if (Fichier is { } f && File.Exists(f))
        {
            try
            {
                var lu = JsonSerializer.Deserialize<Dictionary<string, Entree>>(Protection().Unprotect(File.ReadAllText(f)));
                if (lu is not null) foreach (var (k, v) in lu) cles[k] = v;
            }
            catch (Exception e) when (e is IOException or JsonException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
            {
                // Illisible (clés de chiffrement perdues, fichier abîmé) : comme s'il n'y avait pas de clé ; il suffit d'en enregistrer une.
            }
        }
        return _cles = cles;
    }

    private void Ecrire(Dictionary<string, Entree> cles)
    {
        var f = Fichier ?? throw new InvalidOperationException(L.T("llm.erreur.pasDeCle"));
        var texte = Protection().Protect(JsonSerializer.Serialize(cles));
        var temporaire = f + ".tmp";
        File.WriteAllText(temporaire, texte);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporaire, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporaire, f, overwrite: true);
        _cles = cles;
    }

    /// <summary>
    /// Enregistre la clé d'un fournisseur du cloud, liée à l'hôte de <paramref name="adresse"/> (imposé pour OpenAI, Anthropic,
    /// Mistral, Infomaniak).
    /// </summary>
    public void Enregistrer(string fournisseur, string cle, string adresse)
    {
        if (Array.IndexOf(ConnexionLlm.Clouds, fournisseur) < 0) throw new ArgumentException(L.T("llm.erreur.fournisseur"));
        cle = (cle ?? "").Trim();
        if (cle.Length is < 8 or > 500 || cle.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) throw new ArgumentException(L.T("llm.erreur.cle"));
        var hote = ConnexionLlm.HoteImpose(fournisseur);
        if (hote is null)
        {
            if (!Uri.TryCreate((adresse ?? "").Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.Host.Length == 0)
                throw new ArgumentException(L.T("llm.erreur.adresse"));
            hote = u.Host;
        }
        lock (_verrou)
        {
            var cles = new Dictionary<string, Entree>(Lire(), StringComparer.Ordinal) { [fournisseur] = new Entree(cle, hote.ToLowerInvariant()) };
            Ecrire(cles);
        }
    }

    /// <summary>Oublie la clé enregistrée d'un fournisseur (une variable d'environnement reste).</summary>
    public void Effacer(string fournisseur)
    {
        lock (_verrou)
        {
            var cles = new Dictionary<string, Entree>(Lire(), StringComparer.Ordinal);
            if (cles.Remove(fournisseur)) Ecrire(cles);
        }
    }

    private Entree? Trouver(string fournisseur)
    {
        var env = Environment.GetEnvironmentVariable(Variable(fournisseur));
        if (!string.IsNullOrWhiteSpace(env))
        {
            var hote = ConnexionLlm.HoteImpose(fournisseur) ?? Environment.GetEnvironmentVariable(Variable(fournisseur) + "_HOTE");
            if (!string.IsNullOrWhiteSpace(hote)) return new Entree(env.Trim(), hote.Trim().ToLowerInvariant());
        }
        if (_dossier() is null) return null;
        lock (_verrou) return Lire().GetValueOrDefault(fournisseur);
    }

    /// <summary>La clé à envoyer pour cette connexion, ou null : aucune, ou l'adresse ne mène pas à l'hôte auquel elle est liée.</summary>
    public string? Pour(ConnexionLlm c)
    {
        if (!c.EstCloud || Trouver(c.Fournisseur) is not { } e) return null;
        if (!Uri.TryCreate(c.Adresse, UriKind.Absolute, out var u) || !string.Equals(u.Host, e.Hote, StringComparison.OrdinalIgnoreCase)) return null;
        if (u.Scheme != "https" && c.Fournisseur != ConnexionLlm.Autre) return null;
        return e.Cle;
    }

    /// <summary>Ce qu'on peut montrer de chaque clé : « •••• » et ses 4 derniers caractères, ou « variable d'environnement ».</summary>
    public Dictionary<string, string> Resume()
    {
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in ConnexionLlm.Clouds)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable(f))))
                r[f] = L.T("llm.cle.variable", ("nom", Variable(f)));
            else if (Trouver(f) is { } e)
                r[f] = "••••" + e.Cle[^4..] + (ConnexionLlm.HoteImpose(f) is null ? $" ({e.Hote})" : "");
        }
        return r;
    }
}
