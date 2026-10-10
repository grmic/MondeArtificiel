using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;

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
app.UseStaticFiles();

app.MapGet("/api/etat", () => Results.Ok(new
{
    InfosMonde.ParDefaut.Nom,
    InfosMonde.ParDefaut.VersionMoteur,
    HeureServeur = DateTimeOffset.UtcNow,
}));

// Carte du continent. Le paramètre « graine » (entier de 0 à 4 294 967 295) est facultatif ;
// une valeur invalide est refusée par la liaison de paramètres (réponse 400).
app.MapGet("/api/carte", (uint? graine, ServiceCarte cartes) =>
    Results.Ok(CarteDto.Depuis(cartes.Obtenir(graine))));

app.MapHub<MondeHub>(MondeHub.Route);

app.Run();

// Rend la classe Program visible pour les tests d'intégration.
public partial class Program;
