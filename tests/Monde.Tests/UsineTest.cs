using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur.Hubs;

namespace Monde.Tests;

/// <summary>Serveur de test dont les données vont dans un dossier temporaire.</summary>
public sealed class UsineTest : WebApplicationFactory<Program>
{
    public UsineTest() : this(Path.Combine(Path.GetTempPath(), "monde-tests-" + Guid.NewGuid().ToString("N")), supprimer: true)
    {
    }

    internal UsineTest(string dossier, bool supprimer)
    {
        Dossier = dossier;
        _supprimer = supprimer;
    }

    private readonly bool _supprimer;

    public string Dossier { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Monde:Dossier", Dossier);

    public HubConnection CreerConnexion() => new HubConnectionBuilder()
        .WithUrl(new Uri(Server.BaseAddress, MondeHub.Route), options =>
        {
            options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
        })
        .Build();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _supprimer && Directory.Exists(Dossier))
        {
            Directory.Delete(Dossier, recursive: true);
        }
    }
}
