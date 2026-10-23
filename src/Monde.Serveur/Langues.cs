using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Textes du serveur dans la langue du navigateur qui les demande. La langue courante suit l'appel en cours
/// (elle est posée pour chaque appel du hub, chaque requête HTTP et chaque diffusion) ; par défaut, le français.
/// Une clé absente de la langue courante prend le texte français.
/// </summary>
public static class L
{
    public const string Francais = "fr";
    private static readonly AsyncLocal<Langue?> Actuelle = new();

    /// <summary>Navigateurs connectés par langue : la diffusion n'écrit que dans les langues lues en ce moment.</summary>
    private static readonly ConcurrentDictionary<string, int> Connectes = new();

    public static IReadOnlyDictionary<string, Langue> Toutes => Catalogues.Toutes;

    public static Langue Courante => Actuelle.Value ?? Toutes[Francais];

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
    public static string T(string cle, params ReadOnlySpan<(string Nom, object? Valeur)> parametres) => Courante.Texte(cle, parametres);

    /// <summary>Le récit écrit dans la langue courante.</summary>
    public static string Rediger(Recit recit) => Courante.Rediger(recit);

    /// <summary>Un événement du journal dans la langue courante ; écrit avant la 0.30.1, il reste tel quel (en français).</summary>
    public static string Ecrire(EvenementJournal e) => e.Recit is { } r ? Rediger(r) : e.Brut ?? "";

    /// <summary>Une pensée dans la langue courante (une pensée ancienne reste en français).</summary>
    public static string Ecrire(Pensee p) => p.Recit is { } r ? Rediger(r) : p.Brut ?? "";

    /// <summary>Un texte gardé dans l'état du monde (récit encodé, ou phrase écrite avant la 0.30.1), dans la langue courante.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(s))]
    public static string? Garde(string? s) => s is null ? null : Recit.Decoder(s) is { } r ? Rediger(r) : s;

    /// <summary>Vrai si ce texte, écrit avant la traduction du journal, s'affiche en français dans une autre langue.</summary>
    public static bool Ancien(Recit? recit) => recit is null && Courante.Code != Catalogues.Francais;

    /// <summary>Nom d'une communauté (d'après son nom français) pour un titre ou une liste : « Communauté d'Ana », « Tako people ».</summary>
    public static string Communaute(string nomFrancais) => Courante.Communaute(nomFrancais, phrase: false);

    /// <summary>Nom d'une communauté au fil d'une phrase : « la communauté d'Ana », « the Tako people ».</summary>
    public static string CommunautePhrase(string nomFrancais) => Courante.Communaute(nomFrancais, phrase: true);

    /// <summary>Nom d'une communauté sans majuscule, pour les phrases qui écrivent l'article elles-mêmes (« de la {nomMin} »).</summary>
    public static string CommunauteMin(string nomFrancais) =>
        Communaute(nomFrancais) is { Length: > 0 } n ? char.ToLower(n[0], Courante.Culture) + n[1..] : "";

    /// <summary>Le texte existe-t-il (dans la langue courante ou en français) ?</summary>
    public static bool Existe(string cle) => Courante.Existe(cle);

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
