using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Une langue de l'interface, lue dans <c>langues/xx.json</c>. Chaque clé donne un texte, ou ses formes selon le
/// pluriel (one, other…, choisies par le paramètre <c>n</c>) ou le genre (m, f, choisies par le paramètre <c>g</c>).
/// L'entrée <c>_langue</c> donne le code, le nom de la langue dans sa propre langue et l'écriture des nombres.
/// </summary>
public sealed partial class Langue
{
    public required string Code { get; init; }
    public required string Nom { get; init; }
    public required string Locale { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Entrees { get; init; }
    public required NumberFormatInfo Nombres { get; init; }

    /// <summary>Texte brut du fichier, servi tel quel au navigateur.</summary>
    public required string Json { get; init; }

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$")]
    public static partial Regex CodeValide();

    public static Langue Lire(string chemin)
    {
        var json = File.ReadAllText(chemin);
        using var document = JsonDocument.Parse(json);
        var entrees = new Dictionary<string, JsonElement>();
        foreach (var p in document.RootElement.EnumerateObject()) entrees[p.Name] = p.Value.Clone();
        var code = Path.GetFileNameWithoutExtension(chemin);
        if (!CodeValide().IsMatch(code)) throw new InvalidDataException($"Nom de fichier de langue invalide : {chemin}");
        var entete = entrees.GetValueOrDefault("_langue");
        string Champ(string nom, string defaut) =>
            entete.ValueKind == JsonValueKind.Object && entete.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()! : defaut;
        return new Langue
        {
            Code = code,
            Nom = Champ("nom", code),
            Locale = Champ("locale", code),
            Entrees = entrees,
            // Écriture des nombres donnée par le fichier : elle ne dépend pas des cultures installées sur la machine.
            Nombres = new NumberFormatInfo
            {
                NumberDecimalSeparator = Champ("decimale", code == "fr" ? "," : "."),
                NumberGroupSeparator = Champ("milliers", code == "fr" ? " " : ","),
            },
            Json = json,
        };
    }

    /// <summary>Forme du pluriel (règles CLDR des langues courantes) : en français, 0 et 1 prennent le singulier.</summary>
    public string FormePluriel(double n)
    {
        var langue = Code.Split('-')[0];
        return langue switch
        {
            "fr" or "pt" => n is >= 0 and < 2 ? "one" : "other",
            "ja" or "zh" or "ko" or "vi" or "th" or "id" => "other",
            _ => n == 1 ? "one" : "other",
        };
    }

    /// <summary>Culture des tris alphabétiques (celle de la locale du fichier, ou la culture neutre si la machine ne la connaît pas).</summary>
    public CultureInfo Culture => culture ??= TrouverCulture(Locale);

    private CultureInfo? culture;

    private static CultureInfo TrouverCulture(string locale)
    {
        try { return CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    public string Nombre(double v, int decimales = 1) =>
        v.ToString(decimales > 0 ? "#,##0." + new string('#', decimales) : "#,##0", Nombres);
}

/// <summary>
/// Textes du serveur dans la langue du navigateur qui les demande. La langue courante suit l'appel en cours
/// (elle est posée pour chaque appel du hub, chaque requête HTTP et chaque diffusion) ; par défaut, le français.
/// Une clé absente de la langue courante prend le texte français.
/// </summary>
public static class L
{
    public const string Francais = "fr";
    private static readonly AsyncLocal<Langue?> Actuelle = new();
    private static readonly Lock Verrou = new();
    private static IReadOnlyDictionary<string, Langue>? _langues;

    /// <summary>Navigateurs connectés par langue : la diffusion n'écrit que dans les langues lues en ce moment.</summary>
    private static readonly ConcurrentDictionary<string, int> Connectes = new();

    public static IReadOnlyDictionary<string, Langue> Toutes
    {
        get
        {
            if (_langues is { } l) return l;
            lock (Verrou) return _langues ??= Charger(TrouverDossier());
        }
    }

    public static Langue Courante => Actuelle.Value ?? Toutes[Francais];

    /// <summary>Dossier des langues : à côté de l'exécutable, ou, en développement, dans le dépôt.</summary>
    private static string TrouverDossier()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var dossier = Path.Combine(d.FullName, "langues");
            if (File.Exists(Path.Combine(dossier, "fr.json"))) return dossier;
        }
        throw new FileNotFoundException("Dossier « langues » introuvable (il doit contenir fr.json).");
    }

    private static Dictionary<string, Langue> Charger(string dossier)
    {
        var langues = new Dictionary<string, Langue>();
        foreach (var fichier in Directory.EnumerateFiles(dossier, "*.json").Order(StringComparer.Ordinal))
        {
            var code = Path.GetFileNameWithoutExtension(fichier);
            if (!Langue.CodeValide().IsMatch(code)) continue;
            langues[code] = Langue.Lire(fichier);
        }
        if (!langues.ContainsKey(Francais)) throw new FileNotFoundException("Le fichier de langue fr.json manque.", Path.Combine(dossier, "fr.json"));
        return langues;
    }

    /// <summary>Langue reconnue pour ce code, ou le français.</summary>
    public static string Reconnaitre(string? code) =>
        code is not null && Toutes.ContainsKey(code) ? code : Francais;

    /// <summary>Pose la langue de l'appel en cours (et de ce qu'il attend).</summary>
    public static void Definir(string? code) => Actuelle.Value = Toutes[Reconnaitre(code)];

    /// <summary>Part des clés françaises présentes dans cette langue (1 : complète).</summary>
    public static double Part(Langue langue)
    {
        var francais = Toutes[Francais].Entrees.Keys.Where(k => !k.StartsWith('_')).ToList();
        return francais.Count == 0 ? 1 : francais.Count(langue.Entrees.ContainsKey) / (double)francais.Count;
    }

    public static string Groupe(string code) => $"langue:{code}";

    public static void Connecter(string code) => Connectes.AddOrUpdate(code, 1, (_, n) => n + 1);

    public static void Deconnecter(string code) => Connectes.AddOrUpdate(code, 0, (_, n) => Math.Max(0, n - 1));

    /// <summary>Langues lues en ce moment par au moins un navigateur.</summary>
    public static IEnumerable<string> Actives => Connectes.Where(c => c.Value > 0).Select(c => c.Key).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Texte de la clé dans la langue courante, ses accolades remplacées par les paramètres (les nombres sont
    /// écrits selon la langue). <c>n</c> choisit la forme du pluriel, <c>g</c> (« m », « f ») celle du genre.
    /// </summary>
    public static string T(string cle, params ReadOnlySpan<(string Nom, object? Valeur)> parametres)
    {
        var langue = Courante;
        var modele = Choisir(langue, cle, parametres);
        if (modele is null && langue.Code != Francais) modele = Choisir(Toutes[Francais], cle, parametres);
        if (modele is null) return cle;
        if (parametres.Length == 0 || modele.IndexOf('{') < 0) return modele;
        var texte = new System.Text.StringBuilder(modele.Length + 16);
        var i = 0;
        while (i < modele.Length)
        {
            var debut = modele.IndexOf('{', i);
            var fin = debut < 0 ? -1 : modele.IndexOf('}', debut + 1);
            if (debut < 0 || fin < 0)
            {
                texte.Append(modele, i, modele.Length - i);
                break;
            }
            texte.Append(modele, i, debut - i);
            var nom = modele.AsSpan(debut + 1, fin - debut - 1);
            var trouve = false;
            foreach (var (n, v) in parametres)
            {
                if (!nom.SequenceEqual(n)) continue;
                texte.Append(Ecrire(langue, v));
                trouve = true;
                break;
            }
            if (!trouve) texte.Append(modele, debut, fin - debut + 1);
            i = fin + 1;
        }
        return texte.ToString();
    }

    private static string Ecrire(Langue langue, object? v) => v switch
    {
        null => "",
        string s => s,
        int or long or short or byte => langue.Nombre(Convert.ToDouble(v, CultureInfo.InvariantCulture), 0),
        double d => langue.Nombre(d, 2),
        float f => langue.Nombre(f, 2),
        decimal m => langue.Nombre((double)m, 2),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private static string? Choisir(Langue langue, string cle, ReadOnlySpan<(string Nom, object? Valeur)> parametres)
    {
        if (!langue.Entrees.TryGetValue(cle, out var e)) return null;
        for (var profondeur = 0; profondeur < 3 && e.ValueKind == JsonValueKind.Object; profondeur++)
        {
            var pluriel = e.TryGetProperty("other", out _) || e.TryGetProperty("one", out _);
            string forme;
            if (pluriel)
            {
                var n = 0.0;
                foreach (var (nom, v) in parametres)
                    if (nom == "n" && v is not null) n = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                // « zero », s'il est écrit, vaut pour 0 dans toutes les langues (« Aucun volcan actif. »).
                forme = n == 0 && e.TryGetProperty("zero", out _) ? "zero" : langue.FormePluriel(n);
                if (!e.TryGetProperty(forme, out _)) forme = "other";
            }
            else
            {
                forme = "m";
                foreach (var (nom, v) in parametres)
                    if (nom == "g" && v is string g) forme = g;
                if (!e.TryGetProperty(forme, out _)) forme = "m";
            }
            if (!e.TryGetProperty(forme, out var suivante)) return null;
            e = suivante;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }

    /// <summary>Le texte existe-t-il (dans la langue courante ou en français) ?</summary>
    public static bool Existe(string cle) => Courante.Entrees.ContainsKey(cle) || Toutes[Francais].Entrees.ContainsKey(cle);

    /// <summary>Nombre écrit selon la langue courante.</summary>
    public static string Nombre(double v, int decimales = 1) => Courante.Nombre(v, decimales);

    /// <summary>Distance en mots : « 600 m », « 3,2 km », « 48 km ».</summary>
    public static string Distance(double kilometres) => kilometres switch
    {
        < 1 => $"{Math.Round(kilometres * 1000 / 50) * 50:0} m",
        < 10 => $"{Nombre(kilometres)} km",
        _ => $"{Nombre(kilometres, 0)} km",
    };

    /// <summary>Surface en mots : « 40 hectares », « 3,8 km² », « 1 250 km² ».</summary>
    public static string Surface(double km2) => km2 switch
    {
        < 1 => T("mesure.hectares", ("n", (int)Math.Round(km2 * 100))),
        < 100 => $"{Nombre(km2)} km²",
        _ => $"{Nombre(km2, 0)} km²",
    };

    /// <summary>Une durée du monde en mots : « 3 semaines », « 8 mois », « un an », « 12 ans ».</summary>
    public static string Duree(long secondes)
    {
        var jours = secondes / (double)Calendrier.SecondesParJour;
        var mois = jours / 30;
        return jours switch
        {
            < 10 => T("duree.quelquesJours"),
            < 45 => T("duree.semaines", ("n", (int)Math.Max(2, Math.Round(jours / 7)))),
            _ when mois < 11.5 => T("duree.mois", ("n", (int)Math.Round(mois))),
            _ when mois < 18 => T("duree.unAn"),
            _ => T("duree.ans", ("n", (int)Math.Round(mois / 12))),
        };
    }

    /// <summary>Date du monde : « An 12, jour 30 du printemps, 08:15 ».</summary>
    public static string Date(long secondes) => Date(Calendrier.Decomposer(secondes));

    public static string Date(DateMonde d) => T("date.complete", ("an", d.Annee.ToString(CultureInfo.InvariantCulture)),
        ("jour", d.JourDeSaison.ToString(CultureInfo.InvariantCulture)), ("saisonDe", T($"saison.de.{d.Saison}")),
        ("saison", T($"saison.{d.Saison}")), ("heure", $"{d.Heure:00}:{d.Minute:00}"));

    /// <summary>Nom de la saison : « printemps ».</summary>
    public static string Saison(Saison s) => T($"saison.{s}");
}

/// <summary>
/// Langue de chaque navigateur : donnée à la connexion (<c>/hub/monde?langue=en</c>), gardée avec la connexion,
/// posée pour chacun de ses appels. Chaque connexion rejoint le groupe de sa langue, pour la diffusion.
/// </summary>
public sealed class FiltreLangue : IHubFilter
{
    private const string Cle = "langue";

    private static string LangueDe(HubCallerContext contexte) => contexte.Items.TryGetValue(Cle, out var v) && v is string s ? s : L.Francais;

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext contexte, Func<HubInvocationContext, ValueTask<object?>> suite)
    {
        L.Definir(LangueDe(contexte.Context));
        return await suite(contexte);
    }

    public async Task OnConnectedAsync(HubLifetimeContext contexte, Func<HubLifetimeContext, Task> suite)
    {
        var demandee = contexte.Context.GetHttpContext()?.Request.Query["langue"].ToString();
        var code = L.Reconnaitre(demandee);
        contexte.Context.Items[Cle] = code;
        L.Definir(code);
        L.Connecter(code);
        await contexte.Hub.Groups.AddToGroupAsync(contexte.Context.ConnectionId, L.Groupe(code));
        await suite(contexte);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext contexte, Exception? exception, Func<HubLifetimeContext, Exception?, Task> suite)
    {
        L.Deconnecter(LangueDe(contexte.Context));
        await suite(contexte, exception);
    }
}

public static class Diffusion
{
    /// <summary>
    /// Envoie à tous les navigateurs, chacun dans sa langue : le contenu est fabriqué une fois par langue lue.
    /// </summary>
    public static async Task Diffuser(this IHubClients<IClientProxy> clients, string methode, Func<object?> contenu, CancellationToken arret = default)
    {
        foreach (var code in L.Actives)
        {
            L.Definir(code);
            await clients.Group(L.Groupe(code)).SendAsync(methode, contenu(), arret);
        }
    }
}
