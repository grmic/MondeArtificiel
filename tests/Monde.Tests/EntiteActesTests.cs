using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.35.1 : registre des actes de l'Entité, regard périodique et caractère de l'Entité-LLM.</summary>
public sealed class EntiteActesTests
{
    private sealed record ActeLu(long Id, string Auteur, string Sorte, string Quoi, int Humain, List<string> Resultats, string Issue, long JournalPremier,
        long Echange, bool Repris);
    private sealed record ComptesLus(int Total, int ParLlm, int Morts, int Paroles, List<AnLu> ParAn);
    private sealed record AnLu(int An, int Joueur, int Llm);
    private sealed record PageLue(ComptesLus Comptes, List<ActeLu> Actes, bool Plus);
    private sealed record LigneLue(long Id, string Texte);
    private sealed record EtatLu(int EnFile, string? DerniereErreur, string CaractereLlm, string? ProchainRegard, string? EnCours = null);
    private sealed record EchangeLu(long Id, string StatutCode, string Occasion, string Mots, List<string> Resultats);

    private static async Task<List<EchangeLu>> Attendre(HubConnection c, Func<List<EchangeLu>, bool> condition)
    {
        for (var i = 0; i < 300; i++)
        {
            var liste = await c.InvokeAsync<List<EchangeLu>>("EchangesLlm", 0, 0, (int?)null, 0L);
            if (condition(liste)) return liste;
            await Task.Delay(100);
        }
        var etat = await c.InvokeAsync<EtatLu>("EtatEntiteLlm");
        throw new TimeoutException($"L'Entité-LLM n'a pas répondu ({etat.EnFile} en file, en cours {etat.EnCours}, erreur : {etat.DerniereErreur}).");
    }

    [Fact]
    public async Task ActesDuJoueur_SontNotes_AvecLeurJournal_EtRepris_Apres_Migration()
    {
        using var usine = new UsineTest();
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        var adultes = service.Monde.InstantaneHumains().Humains.Where(h => h.EstVivant && !h.EstEnfant(service.Monde.Secondes)).Select(h => h.Id).ToList();
        var cible = adultes[0];
        int[] deux = [adultes[1], adultes[2]];

        await c.InvokeAsync<string>("Agir", new { geste = nameof(GesteEntite.FrapperDeMort), cible, maniere = "Foudre", param = 0 });
        await c.InvokeAsync<List<object>>("Parler", new { humains = deux, parole = "NAiePasPeur", mode = "Reve" });
        // Une demande invalide n'est pas notée.
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<string>("Agir", new { geste = "Rien", cible, maniere = "Foudre" }));

        var page = await c.InvokeAsync<PageLue>("ActesEntite", new { });
        Assert.Equal(2, page.Comptes.Total);
        Assert.Equal(0, page.Comptes.ParLlm);
        Assert.Equal(1, page.Comptes.Morts);
        Assert.Equal(1, page.Comptes.Paroles);
        Assert.Equal(2, Assert.Single(page.Comptes.ParAn).Joueur);
        var mort = page.Actes.Single(a => a.Sorte == "geste");
        Assert.Equal(("joueur", cible, "effet", false), (mort.Auteur, mort.Humain, mort.Issue, mort.Repris));
        Assert.True(mort.JournalPremier > 0);
        Assert.NotEmpty(await c.InvokeAsync<List<LigneLue>>("JournalActe", mort.Id));
        var parole = page.Actes.Single(a => a.Sorte == "parler");
        Assert.Equal(2, parole.Resultats.Count);

        // Filtres : par sorte, par auteur, par humain.
        Assert.Single((await c.InvokeAsync<PageLue>("ActesEntite", new { sorte = "geste.chatiment" })).Actes);
        Assert.Empty((await c.InvokeAsync<PageLue>("ActesEntite", new { auteur = "llm" })).Actes);
        Assert.Single((await c.InvokeAsync<PageLue>("ActesEntite", new { humain = cible })).Actes);
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<PageLue>("ActesEntite", new { sorte = "inconnue" }));

        // Un monde d'avant la 0.35.1 : ses actes sont reconstitués depuis les lignes « Entité » du journal.
        service.Sauvegarder();
        var chemin = service.Depot.CheminFichier;
        using (var cx = new SqliteConnection($"Data Source={chemin};Pooling=False"))
        {
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText = "DROP TABLE entite_actes; PRAGMA user_version = 50;";
            cmd.ExecuteNonQuery();
        }
        var repris = new DepotMonde(chemin).Actes(new FiltreActes());
        Assert.All(repris, a => Assert.True(a.Repris));
        Assert.Contains(repris, a => a.Sorte == "geste" && a.Genre == nameof(GesteEntite.FrapperDeMort) && a.Humain == cible);
        Assert.Contains(repris, a => a.Sorte == "parler" && a.Nombre == 2);
    }

    [Fact]
    public async Task Regard_Periodique_EtCaractere_GardeDUnAppelALAutre()
    {
        using var usine = new UsineTest();
        var regard = """{"pensee":"Le monde suit son cours.","mots":"","caractere":"Une mère lointaine et patiente.","actes":[{"type":"parler","humains":[1],"parole":"NAiePasPeur","mode":"Reve"}]}""";
        using var llm = new FauxLlm(regard);
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 8192 });
        await c.InvokeAsync<EtatLu>("ReglerUsageLlm", "Tout");
        var etat = await c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Agit", prieres = false, partOrdinaires = 0, attendre = false, paroles = true, bienfaits = true, chatiments = true, catastrophes = true,
            souffles = true, regard = true, regardJours = 2, caractere = "",
        });
        Assert.NotNull(etat.ProchainRegard);
        Assert.Equal("", etat.CaractereLlm);

        // Deux jours passent : elle regarde le monde, se choisit un caractère et agit.
        service.Monde.AvancerDe(3 * Calendrier.SecondesParJour);
        var liste = await Attendre(c, l => l.Count > 0 && l[0].StatutCode == "applique");
        Assert.Contains(liste, e => e.Occasion == L.T("llm.occasion.court.Regard"));
        var lu = llm.Recus[0]["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains(L.T("llm.r.communautes"), lu);
        Assert.Contains(L.T("llm.caractere.choisir"), llm.Recus[0]["messages"]![0]!["content"]!.GetValue<string>());
        etat = await c.InvokeAsync<EtatLu>("EtatEntiteLlm");
        Assert.Equal("Une mère lointaine et patiente.", etat.CaractereLlm);

        // L'acte du LLM est au registre, lié à son échange.
        var page = await c.InvokeAsync<PageLue>("ActesEntite", new { auteur = "llm" });
        var acte = Assert.Single(page.Actes);
        Assert.Equal(liste[0].Id, acte.Echange);
        Assert.Equal(1, page.Comptes.ParLlm);

        // Au regard suivant, elle relit le caractère qu'elle s'est donné.
        service.Monde.AvancerDe(3 * Calendrier.SecondesParJour);
        await Attendre(c, l => l.Count > 1);
        Assert.Contains("Une mère lointaine et patiente.", llm.Recus[^1]["messages"]![0]!["content"]!.GetValue<string>());

        // Le caractère écrit par le joueur l'emporte ; « Oublier » efface le sien.
        await c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Agit", prieres = false, partOrdinaires = 0, attendre = false, paroles = true, bienfaits = true, chatiments = true, catastrophes = true,
            souffles = true, regard = false, regardJours = 2, caractere = "Jalouse et colérique.",
        });
        Assert.Equal("", (await c.InvokeAsync<EtatLu>("OublierCaractereLlm")).CaractereLlm);
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Agit", prieres = false, partOrdinaires = 0, attendre = false, paroles = true, bienfaits = true, chatiments = true, catastrophes = true,
            souffles = true, regard = true, regardJours = 0, caractere = "",
        }));
    }
}
