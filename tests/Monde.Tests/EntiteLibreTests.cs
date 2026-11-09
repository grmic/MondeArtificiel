using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.35.2 : l'Entité libre (changements de l'état du monde, décrets, prodiges, juge de cohérence, « Défaire »).</summary>
public sealed class EntiteLibreTests
{
    private static MondeSimule Monde(int jours = 30)
    {
        var conf = new ConfigurationMonde(7, 20, 64, 64, 1);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf));
        monde.AvancerDe(jours * Calendrier.SecondesParJour);
        return monde;
    }

    private static Humain H(MondeSimule m, int id) => m.InstantaneHumains().Humains[id - 1];

    [Fact]
    public void Changements_SontAppliques_NotesAuJournal_PuisDefaits()
    {
        var monde = Monde();
        var (_, humains) = monde.InstantaneHumains();
        var a = humains.First(h => h.EstVivant && !h.EstEnfant(monde.Secondes));
        var b = humains.First(h => h.EstVivant && h.Id != a.Id);
        var savoirAvant = a.Savoirs.Count;
        var acte = new ActeLibre("Une eau chaude jaillit de la roche.", Visible: true, Bienfait: true, Temoins: [],
        [
            new ChangementLibre("sante", a.Id, Valeur: 42),
            new ChangementLibre("croyance", a.Id, Valeur: 95),
            new ChangementLibre("lien", a.Id, Autre: b.Id, Valeur: -70),
            new ChangementLibre("pensee", a.Id, Texte: "La roche a parlé.", Genre: "Emerveillement"),
            new ChangementLibre("age", b.Id, Valeur: 30),
            new ChangementLibre("plantes", X: a.X, Y: a.Y, Rayon: 2, Valeur: 3),
        ],
        [new DecretLibre("Fecondite", 2, Raison: "Qu'ils soient nombreux.")]);
        var plantesAvant = monde.Etat.Plantes![monde.Carte.Indice(a.X, a.Y)];
        var (r, premier, dernier) = monde.Executer(m => m.Libre(acte));
        Assert.True(dernier > premier);
        Assert.Equal(42, H(monde, a.Id).Sante);
        Assert.Equal(-70, H(monde, a.Id).AffectionPour(b.Id));
        Assert.Contains(H(monde, a.Id).Memoire.Jardin, j => j.Texte == "La roche a parlé.");
        Assert.Equal(30, H(monde, b.Id).AgeEnAnnees(monde.Secondes), 3);
        Assert.Single(r.Decrets);
        Assert.Equal(2, monde.Decrets()[0].Facteur);
        Assert.True(r.Savent >= 1);
        // Le témoin (lui-même au moins) garde un prodige dans son jardin secret.
        Assert.Contains(H(monde, a.Id).Memoire.Jardin, j => j.Cle.StartsWith("libre:prodige:", StringComparison.Ordinal));
        var journal = monde.JournalRecent(premier - 1).Where(e => e.Id <= dernier).ToList();
        Assert.Contains(journal, e => e.Recit?.Cle == "libre.recit" && e.Origine == Origine.Entite);
        Assert.Contains(journal, e => e.Recit?.Cle == "libre.ch.nombre");
        Assert.Contains(journal, e => e.Recit?.Cle == "libre.decret");

        // Le registre garde de quoi défaire : sérialisé puis relu comme le fait le serveur.
        var json = JsonSerializer.Serialize(new DefaireLibre([.. r.Faits], [.. r.Decrets.Select(d => d.Id)]), Actes.Json);
        var lu = JsonSerializer.Deserialize<DefaireLibre>(json, Actes.Json)!;
        monde.Executer(m => m.Defaire(lu.Faits, lu.Decrets, r.Annonce));
        Assert.NotEqual(42, H(monde, a.Id).Sante);
        Assert.NotEqual(-70, H(monde, a.Id).AffectionPour(b.Id));
        Assert.DoesNotContain(H(monde, a.Id).Memoire.Jardin, j => j.Texte == "La roche a parlé.");
        Assert.Equal(plantesAvant, monde.Etat.Plantes![monde.Carte.Indice(a.X, a.Y)]);
        Assert.False(monde.Decrets()[0].EnVigueur(monde.Secondes));
        Assert.Equal(savoirAvant, H(monde, a.Id).Savoirs.Count);
    }

    [Fact]
    public void MourirPuisRessusciter_EtDefaire()
    {
        var monde = Monde();
        var a = monde.InstantaneHumains().Humains.First(h => h.EstVivant);
        var (r, _, _) = monde.Executer(m => m.Libre(new ActeLibre("La foudre.", false, false, [], [new ChangementLibre("mourir", a.Id, Texte: "Foudre")], [])));
        Assert.False(H(monde, a.Id).EstVivant);
        Assert.Equal(CauseDeMort.Foudre, H(monde, a.Id).CauseDeMort);
        monde.Executer(m => m.Defaire(r.Faits, [], r.Annonce));
        Assert.True(H(monde, a.Id).EstVivant);
        // Revenu parmi les vivants, il vit avec les autres.
        monde.AvancerDe(2 * Calendrier.SecondesParJour);
        var (r2, _, _) = monde.Executer(m => m.Libre(new ActeLibre("Encore.", false, false, [], [new ChangementLibre("mourir", a.Id)], [])));
        Assert.False(H(monde, a.Id).EstVivant);
        monde.Executer(m => m.Libre(new ActeLibre("Il revient.", true, true, [], [new ChangementLibre("ressusciter", a.Id)], [])));
        Assert.True(H(monde, a.Id).EstVivant);
        Assert.True(H(monde, a.Id).Sante >= 60);
        monde.AvancerDe(Calendrier.SecondesParJour);
    }

    [Fact]
    public void Fautes_NeChangentRien()
    {
        var monde = Monde();
        var a = monde.InstantaneHumains().Humains.First(h => h.EstVivant);
        var sante = a.Sante;
        string Faute(params ChangementLibre[] c) =>
            Assert.Throws<ArgumentException>(() => monde.Executer(m => m.Libre(new ActeLibre("x", false, true, [], c, [])))).Message;
        Assert.StartsWith("libre.erreur.valeur", Faute(new ChangementLibre("sante", a.Id, Valeur: 10), new ChangementLibre("sante", a.Id, Valeur: 300)));
        Assert.StartsWith("libre.erreur.humain", Faute(new ChangementLibre("faim", 99999, Valeur: 10)));
        Assert.StartsWith("libre.erreur.lieu", Faute(new ChangementLibre("plantes", X: 5000, Y: 1, Valeur: 2)));
        Assert.StartsWith("libre.erreur.sorte", Faute(new ChangementLibre("voler")));
        Assert.StartsWith("libre.erreur.inconnu", Faute(new ChangementLibre("savoir", a.Id, Texte: "Regularite")));
        Assert.Equal(sante, H(monde, a.Id).Sante);
        Assert.Throws<ArgumentException>(() => monde.Executer(m => m.Libre(new ActeLibre("x", false, true, [], [], [new DecretLibre("Longevite", 50)]))));
        Assert.Empty(monde.Decrets());
    }

    [Fact]
    public void Decrets_UnSansDecretVautExactementUn_EtSontGardes()
    {
        var d = new Decrets();
        Assert.Equal(1.0, d.Facteur(LoiDecret.Maladies, 3, 0));
        var monde = Monde(1);
        monde.Executer(m => m.Libre(new ActeLibre("Que la vie dure.", false, true, [],
            [], [new DecretLibre("Longevite", 3, 0, 10, "Pour voir."), new DecretLibre("Maladies", 0.5)])));
        Assert.StartsWith("libre.erreur.communaute", Assert.Throws<ArgumentException>(() => monde.Executer(m => m.Libre(new ActeLibre("x", false, true, [], [], [new DecretLibre("Gibier", 2, 999)])))).Message);
        var decrets = monde.Decrets();
        Assert.Equal(2, decrets.Count);
        // Gardés avec le monde (état) ; le premier finit après dix jours.
        var copie = new MondeSimule(monde.Carte, monde.Etat);
        Assert.Equal(2, copie.Decrets().Count);
        copie.AvancerDe(11 * Calendrier.SecondesParJour);
        Assert.False(copie.Decrets()[0].EnVigueur(copie.Secondes));
        Assert.True(copie.Decrets()[1].EnVigueur(copie.Secondes));
        Assert.NotNull(copie.LeverDecret(2));
        Assert.Null(copie.LeverDecret(2));
    }

    [Fact]
    public void DepotGardeLesDecrets()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-libre-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            var monde = Monde(1);
            monde.Executer(m => m.Libre(new ActeLibre("Froid.", false, false, [], [], [new DecretLibre("Climat", 1.5, 0, 0, "Qu'ils apprennent.")])));
            depot.Enregistrer(monde.Etat, remplacer: true);
            var lu = depot.Charger()!;
            var d = Assert.Single(lu.Decrets!);
            Assert.Equal((LoiDecret.Climat, 1.5, "Qu'ils apprennent."), (d.Loi, d.Facteur, d.Raison));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, true);
        }
    }

    [Fact]
    public void LireUnActeLibre_LesValeursParDefautTiennent()
    {
        var d = Actes.Lire("""
            {"pensee":"p","mots":"","actes":[{"type":"libre","recit":"r","visible":true,"changements":[{"quoi":"deplacer","humain":1},{"quoi":"oublier","humain":1,"texte":"J2"}],
            "decrets":[{"loi":"Plantes","facteur":2}]}]}
            """);
        var a = Assert.Single(d.Actes);
        Assert.Equal(-1, a.Changements![0].X);
        Assert.Throws<ArgumentException>(() => Actes.Verifier(a, FamillesLlm.Toutes));
        Actes.Verifier(a, FamillesLlm.Toutes, libre: true);
        var resolu = Libres.Resoudre(a, ["1|a", "2|b"]);
        Assert.Equal("2|b", resolu.Changements![1].Texte);
        Assert.Contains("\"libre\"", Actes.Schema(libre: true).ToJsonString());
        Assert.DoesNotContain("\"libre\"", Actes.Schema().ToJsonString());
    }

    private sealed record EtatLu(int EnFile, string? DerniereErreur, string? LibreDepuis, string? EnCours = null);
    private sealed record EchangeLu(long Id, string StatutCode, List<string> Actes, List<string> Resultats, string? Erreur, string? Reponse);
    private sealed record ActeLu(long Id, string Sorte, bool Defaire, bool Defait, string? Recit);
    private sealed record PageLu(List<ActeLu> Actes, string? LibreDepuis);

    private static async Task<List<EchangeLu>> Attendre(HubConnection c, Func<List<EchangeLu>, bool> condition)
    {
        for (var i = 0; i < 300; i++)
        {
            var liste = await c.InvokeAsync<List<EchangeLu>>("EchangesLlm", 0, 0, (int?)null, 0L);
            if (condition(liste)) return liste;
            await Task.Delay(100);
        }
        var etat = await c.InvokeAsync<EtatLu>("EtatEntiteLlm");
        throw new TimeoutException($"Pas de réponse ({etat.EnFile} en file, erreur : {etat.DerniereErreur}).");
    }

    private const string Guerison = """
        {"pensee":"Elle a assez souffert.","mots":"","actes":[{"type":"libre","recit":"Une eau chaude jaillit ; la fièvre la quitte.","visible":true,"bienfait":true,
        "changements":[{"quoi":"sante","humain":1,"valeur":90},{"quoi":"croyance","humain":1,"valeur":80}]}]}
        """;

    [Fact]
    public async Task JugeDeCoherence_RenvoieUneFois_PuisLActeEstAppliqueEtDefait()
    {
        using var usine = new UsineTest();
        // Réponse de l'Entité, juge « incohérent », nouvelle réponse, juge « cohérent ».
        using var llm = new FauxLlm(Guerison, """{"coherent":false,"raison":"Elle n'était pas malade."}""", Guerison, """{"coherent":true,"raison":"Bien."}""");
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 16384 });
        await c.InvokeAsync<EtatLu>("ReglerUsageLlm", "Tout");
        await c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Agit", prieres = false, partOrdinaires = 0, attendre = true, paroles = true, bienfaits = true, chatiments = true, catastrophes = true,
            souffles = true, regard = false, regardJours = 91, caractere = "", libre = true, juge = true,
        });
        await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0);
        var e = Assert.Single(await Attendre(c, l => l.Count > 0 && l[0].StatutCode != "propose"));
        Assert.Equal("applique", e.StatutCode);
        Assert.Equal(4, llm.Recus.Count);
        // L'Entité a lu le mode libre et l'état chiffré ; le juge, sa décision.
        Assert.Contains("ENTITÉ LIBRE", llm.Recus[0]["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Contains("Elle n'était pas malade.", llm.Recus[2]["messages"]!.AsArray().Last()!["content"]!.GetValue<string>());
        Assert.Contains("Juge de cohérence", e.Reponse ?? (await c.InvokeAsync<EchangeLu>("EchangeLlm", e.Id)).Reponse);
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        Assert.Equal(90, service.Monde.InstantaneHumains().Humains[0].Sante, 0);

        var page = await c.InvokeAsync<PageLu>("ActesEntite", new FiltreActesDto(Sorte: "libre"));
        var acte = Assert.Single(page.Actes);
        Assert.True(acte.Defaire);
        Assert.NotNull(page.LibreDepuis);
        Assert.NotNull((await c.InvokeAsync<EtatLu>("EtatEntiteLlm")).LibreDepuis);
        var journal = await c.InvokeAsync<List<EvenementDto>>("JournalActe", acte.Id);
        Assert.All(journal, l => Assert.True(l.Libre));

        var defait = await c.InvokeAsync<List<string>>("DefaireActe", acte.Id);
        Assert.NotEmpty(defait);
        Assert.NotEqual(90, service.Monde.InstantaneHumains().Humains[0].Sante);
        Assert.True((await c.InvokeAsync<PageLu>("ActesEntite", new FiltreActesDto(Sorte: "libre"))).Actes[0].Defait);
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<List<string>>("DefaireActe", acte.Id));
    }

    [Fact]
    public async Task SansLaCase_LActeLibreEstRefuse()
    {
        using var usine = new UsineTest();
        using var llm = new FauxLlm(Guerison);
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 16384 });
        await c.InvokeAsync<EtatLu>("ReglerUsageLlm", "Tout");
        await c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Agit", prieres = false, partOrdinaires = 0, attendre = true, paroles = true, bienfaits = true, chatiments = true, catastrophes = true,
            souffles = true, regard = false, regardJours = 91, caractere = "",
        });
        await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0);
        var e = Assert.Single(await Attendre(c, l => l.Count > 0 && l[0].StatutCode != "propose"));
        // Refusé aux deux essais : l'acte est écarté et la raison notée en erreur.
        Assert.Equal("rien", e.StatutCode);
        Assert.Contains("Entité libre", e.Erreur);
        Assert.DoesNotContain("ENTITÉ LIBRE", llm.Recus[0]["messages"]![0]!["content"]!.GetValue<string>());
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        Assert.NotEqual(90, service.Monde.InstantaneHumains().Humains[0].Sante);
    }
}
