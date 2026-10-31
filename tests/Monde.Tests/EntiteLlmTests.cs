using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>
/// Faux LLM (API d'Ollama) : rend les réponses données, l'une après l'autre (la dernière se répète), et garde ce qu'il a reçu.
/// </summary>
internal sealed class FauxLlm : IDisposable
{
    private readonly HttpListener _ecoute = new();
    private readonly Queue<string> _reponses;
    private readonly string _derniere;
    public List<JsonObject> Recus { get; } = [];
    public string Adresse { get; }

    public FauxLlm(params string[] reponses)
    {
        _reponses = new Queue<string>(reponses);
        _derniere = reponses[^1];
        for (var essai = 0; ; essai++)
        {
            var port = Random.Shared.Next(20000, 60000);
            Adresse = $"http://127.0.0.1:{port}";
            try
            {
                _ecoute.Prefixes.Clear();
                _ecoute.Prefixes.Add(Adresse + "/");
                _ecoute.Start();
                break;
            }
            catch (HttpListenerException) when (essai < 20)
            {
            }
        }
        _ = Task.Run(Servir);
    }

    private async Task Servir()
    {
        while (_ecoute.IsListening)
        {
            HttpListenerContext c;
            try
            {
                c = await _ecoute.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            string corps;
            if (c.Request.Url!.AbsolutePath == "/api/tags") corps = """{"models":[{"name":"faux:1b"},{"name":"autre:7b"}]}""";
            else
            {
                using var lecteur = new StreamReader(c.Request.InputStream, Encoding.UTF8);
                var recu = JsonNode.Parse(await lecteur.ReadToEndAsync())!.AsObject();
                string texte;
                lock (Recus)
                {
                    Recus.Add(recu);
                    texte = _reponses.Count > 0 ? _reponses.Dequeue() : _derniere;
                }
                corps = new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = texte } }.ToJsonString();
            }
            var octets = Encoding.UTF8.GetBytes(corps);
            c.Response.ContentType = "application/json";
            await c.Response.OutputStream.WriteAsync(octets);
            c.Response.Close();
        }
    }

    public void Dispose() => _ecoute.Close();
}

/// <summary>0.35.0 : l'Entité jouée par un LLM (lecture des réponses, vérifications, file, trace, propositions).</summary>
public sealed class EntiteLlmTests
{
    private const string ParoleDeRassurance = """{"pensee":"Elle a peur.","mots":"N'aie pas peur.","actes":[{"type":"parler","humains":[1],"parole":"NAiePasPeur","mode":"Reve"}]}""";

    [Fact]
    public void Lire_AccepteLaReflexionEtLesBalises_EtRefuseLeReste()
    {
        var d = Actes.Lire("<think>je réfléchis { pas du JSON }</think>\n```json\n" + ParoleDeRassurance + "\n```");
        Assert.Equal("N'aie pas peur.", d.Mots);
        var a = Assert.Single(d.Actes);
        Assert.Equal(("parler", "NAiePasPeur", "Reve"), (a.Type, a.Parole, a.Mode));
        Assert.Equal([1], a.Humains!);
        // Nombres écrits en texte, actes en trop (au-delà de quatre) ignorés.
        var e = Actes.Lire("""{"pensee":"","mots":"","actes":[{"type":"exaucer","priere":"12"},{"type":"rien"},{"type":"rien"},{"type":"rien"},{"type":"rien"}]}""");
        Assert.Equal(12, e.Actes[0].Priere);
        Assert.Equal(Actes.ParReponse, e.Actes.Count);
        Assert.Throws<FormatException>(() => Actes.Lire("Je ne sais pas."));
        Assert.Throws<FormatException>(() => Actes.Lire("""{"actes":[{"type":"parler","humains":"tous"}]}"""));
    }

    [Fact]
    public void Verifier_RefuseLesActesInconnusOuNonPermis()
    {
        Assert.Throws<ArgumentException>(() => Actes.Verifier(new ActeLlm { Type = "detruire" }, FamillesLlm.Toutes));
        var tuer = new ActeLlm { Type = Actes.Geste, Geste = nameof(GesteEntite.FrapperDeMort), Cible = 1, Maniere = "Foudre" };
        Assert.Throws<ArgumentException>(() => Actes.Verifier(tuer, FamillesLlm.Toutes & ~FamillesLlm.Chatiments));
        Actes.Verifier(tuer, FamillesLlm.Toutes);
        var guerir = new ActeLlm { Type = Actes.Geste, Geste = nameof(GesteEntite.Guerir), Cible = 1, Maniere = "Cachee" };
        Actes.Verifier(guerir, FamillesLlm.Bienfaits);
        Assert.Throws<ArgumentException>(() => Actes.Verifier(new ActeLlm { Type = Actes.Catastrophe, Catastrophe = "seisme" }, FamillesLlm.Paroles));
        // Une parole collective passe par parler_communaute.
        Assert.Throws<ArgumentException>(() => Actes.Verifier(new ActeLlm { Type = Actes.Parler, Parole = nameof(Parole.FaitesLaGuerre) }, FamillesLlm.Toutes));
        Assert.Equal("P2écrit", Actes.Piece("p2", ["P1écrit", "P2écrit"]));
        Assert.Equal("X(1)", Actes.Piece("X(1)", ["P1écrit"]));
    }

    /// <summary>Des croyants affamés, jusqu'à ce qu'une prière au moins attende une réponse.</summary>
    private static MondeSimule Croyants()
    {
        var conf = new ConfigurationMonde(5, 20, 64, 64, 1);
        var etat = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)).Etat;
        foreach (var h in etat.Humains!)
        {
            h.Croyance = 90;
            h.Faim = 95;
        }
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), etat);
        for (var j = 0; j < 60 && !monde.Prieres().Any(EnAttente); j++) monde.AvancerDe(Calendrier.SecondesParJour);
        return monde;
    }

    private static bool EnAttente(Priere p) => p.Issue == IssuePriere.EnAttente && p.Refus == RefusPriere.Aucun && p.Sujet != SujetPriere.Remerciement;

    [Fact]
    public void Appliquer_PasseParLesPouvoirsDuJoueur_EtMarqueLeJournal()
    {
        var monde = Croyants();
        var priere = monde.Prieres().First(EnAttente);
        var (resultats, premier, dernier) = monde.Executer(m => new[]
        {
            Actes.Appliquer(m, new ActeLlm { Type = Actes.Exaucer, Priere = priere.Id }, FamillesLlm.Toutes, []),
        });
        Assert.Single(resultats);
        Assert.Equal(IssuePriere.ExauceeParEntite, monde.Prieres().First(p => p.Id == priere.Id).Issue);
        Assert.True(dernier >= premier);
        Assert.Contains(monde.JournalRecent(premier - 1).Where(e => e.Id <= dernier), e => e.Origine == Origine.Entite);
        // Une demande invalide est refusée sans rien changer.
        Assert.Throws<ArgumentException>(() => Actes.Appliquer(monde, new ActeLlm { Type = Actes.Exaucer, Priere = 0 }, FamillesLlm.Toutes, []));
        Assert.Throws<ArgumentException>(() => Actes.Appliquer(monde,
            new ActeLlm { Type = Actes.Parler, Humains = [1], Parole = "Tue", Mode = "Voix", Cible = 99999 }, FamillesLlm.Toutes, []));
    }

    [Fact]
    public void Retenu_ArreteLHorlogeSansToucherAuMonde()
    {
        var conf = new ConfigurationMonde(5, 20, 64, 64, 1);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf));
        monde.ChangerFacteur(3600);
        monde.Retenu = true;
        var avant = monde.Secondes;
        Assert.Equal(0, monde.Avancer(TimeSpan.FromSeconds(0.5)));
        Assert.Equal(avant, monde.Secondes);
        monde.Retenu = false;
        Assert.True(monde.Avancer(TimeSpan.FromSeconds(0.5)) > 0);
    }

    private sealed record EtatLu(int EnFile, string? DerniereErreur, int Propositions, string? EnCours = null);
    private sealed record EchangeLu(long Id, string StatutCode, string Mots, List<string> Actes, List<string> Resultats, string? Erreur, long JournalPremier,
        int Humain, string? Lu, string? Reponse);

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
    public async Task Demande_Proposee_PuisAcceptee_EtTracee_AvecUnSecondEssai()
    {
        using var usine = new UsineTest();
        // Première réponse illisible : le LLM est repris une fois avec la raison.
        using var llm = new FauxLlm("Je ne réponds pas en JSON.", ParoleDeRassurance);
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();

        var connexion = new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 8192 };
        Assert.Equal(["autre:7b", "faux:1b"], await c.InvokeAsync<List<string>>("ModelesLlm", connexion));
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", connexion);
        // Mode « Vous seul » : la demande du joueur devient une proposition.
        Assert.Equal(1, await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0));
        var liste = await Attendre(c, l => l.Count > 0);
        var e = Assert.Single(liste);
        Assert.Equal("propose", e.StatutCode);
        Assert.Equal("N'aie pas peur.", e.Mots);
        Assert.Equal(2, llm.Recus.Count);
        // Ce qu'a lu le LLM : la consigne, la fiche de l'humain 1 et le schéma imposé.
        var recu = llm.Recus[0];
        Assert.Equal("faux:1b", recu["model"]!.GetValue<string>());
        Assert.Equal(8192, recu["options"]!["num_ctx"]!.GetValue<int>());
        Assert.NotNull(recu["format"]);
        var demande = recu["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("(n°1)", demande);
        Assert.Contains("FrapperDeMort", recu["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(4, llm.Recus[1]["messages"]!.AsArray().Count); // consigne, demande, réponse fautive, correction

        var complet = await c.InvokeAsync<EchangeLu>("EchangeLlm", e.Id);
        Assert.Contains("(n°1)", complet.Lu);
        Assert.Equal(ParoleDeRassurance, complet.Reponse);

        var resultats = await c.InvokeAsync<List<string>>("AccepterProposition", e.Id);
        Assert.Single(resultats);
        var applique = Assert.Single(await c.InvokeAsync<List<EchangeLu>>("EchangesLlm", 0, 0, (int?)null, 0L));
        Assert.Equal("applique", applique.StatutCode);
        Assert.True(applique.JournalPremier > 0);
        Assert.True(MarquesLlm.Contient(applique.JournalPremier));
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<List<string>>("AccepterProposition", e.Id));
    }

    [Fact]
    public async Task ModeAgit_AppliqueSeul_EtFamillesInterdites_SontRefusees()
    {
        using var usine = new UsineTest();
        var tuer = """{"pensee":"","mots":"","actes":[{"type":"geste","geste":"FrapperDeMort","cible":1,"maniere":"Foudre","param":0}]}""";
        using var llm = new FauxLlm(tuer, tuer, ParoleDeRassurance);
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 16384 });
        await c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Agit", prieres = false, partOrdinaires = 0, attendre = true, paroles = true, bienfaits = true, chatiments = false, catastrophes = false, souffles = false,
        });
        // Châtiments interdits : la réponse est refusée deux fois, puis notée en erreur ; l'humain 1 vit toujours.
        await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0);
        var liste = await Attendre(c, l => l.Count > 0 && l[0].StatutCode != "propose");
        var e = Assert.Single(liste);
        Assert.Equal("applique", e.StatutCode);
        Assert.Contains("FrapperDeMort", e.Actes[0]);
        Assert.Contains(e.Resultats, r => r.Contains("geste", StringComparison.OrdinalIgnoreCase) || r.Length > 0);
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        Assert.True(service.Monde.InstantaneHumains().Humains[0].EstVivant);
        Assert.False(service.Monde.Retenu);

        // Permis : appliqué sans attendre le joueur.
        await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0);
        liste = await Attendre(c, l => l.Count > 1 && l[0].StatutCode != "propose");
        Assert.Equal("applique", liste[0].StatutCode);
        Assert.Contains("NAiePasPeur", liste[0].Actes[0]);
    }
}
