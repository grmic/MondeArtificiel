using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Monde;

var builder = WebApplication.CreateBuilder(args);

// Serveur HTTP Kestrel intégré : l'adresse d'écoute vient de appsettings.json (section Kestrel).
builder.Services.AddSignalR(options =>
{
    // Limite la taille des messages reçus du navigateur (32 Ko, valeur par défaut explicite).
    options.MaximumReceiveMessageSize = 32 * 1024;
});

builder.Services.AddSingleton<ServiceMonde>();
builder.Services.AddSingleton<ServiceCarte>();
builder.Services.AddHostedService<MoteurSimulation>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(gestion => gestion.Run(contexte =>
    {
        // Message générique : aucun détail interne n'est renvoyé au navigateur.
        contexte.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return contexte.Response.WriteAsync("Erreur interne du serveur.");
    }));
}

app.Use(async (contexte, suite) =>
{
    // En-têtes de sécurité de base pour les pages servies.
    contexte.Response.Headers.XContentTypeOptions = "nosniff";
    contexte.Response.Headers.XFrameOptions = "DENY";
    contexte.Response.Headers["Referrer-Policy"] = "no-referrer";
    await suite(contexte);
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
// (0 : sur tous les continents, n : sur le n-ième par la taille). Une valeur mal formée
// est refusée par la liaison de paramètres (400) ; des réglages hors limites aussi (400) ; un continent trop petit
// pour les couples demandés donne 422 avec la raison.
app.MapGet("/api/carte", (uint? graine, int? couples, int? largeur, int? hauteur, int? continents, int? placement,
    ServiceMonde service, ServiceCarte cartes) =>
{
    var actuelle = service.Monde.Configuration;
    var nombre = continents ?? actuelle.Continents;
    var configuration = new ConfigurationMonde(graine ?? (uint)actuelle.Graine, couples ?? actuelle.Couples,
        largeur ?? actuelle.Largeur, hauteur ?? actuelle.Hauteur, nombre, placement ?? Math.Min(actuelle.Placement, nombre));
    if (configuration.Erreur() is { } erreur) return Results.Problem(erreur, statusCode: StatusCodes.Status400BadRequest);
    try
    {
        return Results.Ok(CarteDto.Depuis(cartes.Obtenir(configuration)));
    }
    catch (InvalidOperationException e)
    {
        return Results.Problem(e.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

app.MapHub<MondeHub>(MondeHub.Route);

app.Run();

// Rend la classe Program visible pour les tests d'intégration.
public partial class Program;
