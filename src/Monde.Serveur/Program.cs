using Microsoft.AspNetCore.SignalR;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Monde;

var builder = WebApplication.CreateBuilder(args);

// Serveur HTTP Kestrel intégré : l'adresse d'écoute vient de appsettings.json (section Kestrel).
builder.Services.AddSignalR(options =>
{
    // Langue de chaque navigateur, posée pour chacun de ses appels (voir Langues.cs).
    options.AddFilter<FiltreLangue>();
    // Limite la taille des messages reçus du navigateur (32 Ko, valeur par défaut explicite).
    options.MaximumReceiveMessageSize = 32 * 1024;
});

builder.Services.AddSingleton<FiltreLangue>();
builder.Services.AddSingleton<ServiceMonde>();
builder.Services.AddSingleton<ServiceCarte>();
builder.Services.AddHostedService<MoteurSimulation>();
builder.Services.AddHostedService<EntretienJournal>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(gestion => gestion.Run(contexte =>
    {
        // Message générique : aucun détail interne n'est renvoyé au navigateur.
        contexte.Response.StatusCode = StatusCodes.Status500InternalServerError;
        L.Definir(contexte.Request.Query["langue"].ToString());
        return contexte.Response.WriteAsync(L.T("erreur.interne"));
    }));
}

app.Use(async (contexte, suite) =>
{
    // En-têtes de sécurité de base pour les pages servies.
    contexte.Response.Headers.XContentTypeOptions = "nosniff";
    contexte.Response.Headers.XFrameOptions = "DENY";
    contexte.Response.Headers["Referrer-Policy"] = "no-referrer";
    // Langue des textes renvoyés (aperçu de carte, erreurs) : celle que demande le navigateur, sinon le français.
    L.Definir(contexte.Request.Query["langue"].ToString());
    await suite(contexte);
});

// Langues de l'interface : la liste (chaque langue dans sa propre langue, avec sa part traduite), puis le fichier.
app.MapGet("/api/langues", () => Results.Ok(L.Toutes.Values
    .OrderBy(l => l.Code == L.Francais ? 0 : 1).ThenBy(l => l.Nom, StringComparer.CurrentCulture)
    .Select(l => new { l.Code, l.Nom, Part = Math.Round(L.Part(l), 3) })));
app.MapGet("/api/langues/{code}", (string code, HttpContext contexte) =>
{
    if (!L.Toutes.TryGetValue(code, out var langue)) return Results.NotFound();
    contexte.Response.Headers.CacheControl = "no-cache";
    return Results.Text(langue.Json, "application/json; charset=utf-8");
});

// Guide : le README de la version installée, en français ou en anglais, et ses images (copiés à côté du serveur).
var dossierGuide = Path.Combine(AppContext.BaseDirectory, "guide");
app.MapGet("/api/guide", (string? langue) =>
{
    var chemin = Path.Combine(dossierGuide, langue == "en" ? "README.en.md" : "README.md");
    return File.Exists(chemin) ? Results.Text(File.ReadAllText(chemin), "text/markdown; charset=utf-8") : Results.NotFound();
});
app.MapGet("/api/guide/docs/{nom}", (string nom) =>
{
    // Seulement des images du dossier docs, par leur nom simple.
    if (!System.Text.RegularExpressions.Regex.IsMatch(nom, "^[A-Za-z0-9_-]+\\.png$")) return Results.NotFound();
    var chemin = Path.Combine(dossierGuide, "docs", nom);
    return File.Exists(chemin) ? Results.File(chemin, "image/png") : Results.NotFound();
});

// Client web (fichiers produits par Vite dans wwwroot).
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = fichier =>
    {
        // La page est revalidée à chaque chargement : une nouvelle version s'affiche sans rechargement forcé.
        // Les scripts et styles de Vite portent une empreinte dans leur nom : ils peuvent rester en cache.
        var entetes = fichier.Context.Response.Headers;
        entetes.CacheControl = fichier.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache"
            : fichier.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache";
    },
});

app.MapGet("/api/etat", () => Results.Ok(new
{
    InfosMonde.ParDefaut.Nom,
    InfosMonde.ParDefaut.VersionMoteur,
    HeureServeur = DateTimeOffset.UtcNow,
}));

// Carte du monde courant, ou aperçu d'autres réglages. Les paramètres sont facultatifs (absents : ceux du monde
// courant) : graine (0 à 4 294 967 295), couples, largeur et hauteur en cases, continents, placement des couples
// (0 : sur tous les continents, n : sur le n-ième par la taille), ecart (mer entre les continents : 0 proches, 1 éloignés,
// 2 mélangés). Avec au moins un paramètre, c'est l'aperçu d'un monde neuf, donc de la dernière génération de cartes. Une valeur mal formée
// est refusée par la liaison de paramètres (400) ; des réglages hors limites aussi (400) ; un continent trop petit
// pour les couples demandés donne 422 avec la raison.
app.MapGet("/api/carte", (uint? graine, int? couples, int? largeur, int? hauteur, int? continents, int? placement, int? ecart,
    ServiceMonde service, ServiceCarte cartes) =>
{
    var actuelle = service.Monde.Configuration;
    var nombre = continents ?? actuelle.Continents;
    var configuration = new ConfigurationMonde(graine ?? (uint)actuelle.Graine, couples ?? actuelle.Couples,
        largeur ?? actuelle.Largeur, hauteur ?? actuelle.Hauteur, nombre, placement ?? Math.Min(actuelle.Placement, nombre),
        graine is null && couples is null && largeur is null && hauteur is null && continents is null && placement is null && ecart is null
            ? actuelle.Generation : ConfigurationMonde.GenerationActuelle,
        ecart is { } voulu ? (EcartContinents)voulu : actuelle.Ecart);
    if (Lib.Erreur(configuration) is { } erreur) return Results.Problem(erreur, statusCode: StatusCodes.Status400BadRequest);
    try
    {
        return Results.Ok(CarteDto.Depuis(cartes.Obtenir(configuration)));
    }
    catch (InvalidOperationException e)
    {
        return Results.Problem(Lib.Message(e), statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

app.MapHub<MondeHub>(MondeHub.Route);

app.Run();

// Rend la classe Program visible pour les tests d'intégration.
public partial class Program;
