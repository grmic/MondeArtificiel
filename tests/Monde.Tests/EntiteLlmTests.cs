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
    public void Verifier_RefuseUnGesteSurUnNumeroQuiNEstPasUnHumainVivant()
    {
        var config = ConfigurationMonde.Nouvelle(7);
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config));
        var (_, humains) = monde.InstantaneHumains();
        var feconder = new ActeLlm { Type = Actes.Geste, Geste = nameof(GesteEntite.RendreFecond), Maniere = "Cachee" };
        Assert.True(Gestes.SurHumain(GesteEntite.RendreFecond));
        Assert.Throws<ArgumentException>(() => Actes.Verifier(feconder with { Cible = humains.Count + 50 }, FamillesLlm.Toutes, monde: monde));
        Assert.Throws<ArgumentException>(() => Actes.Verifier(feconder, FamillesLlm.Toutes, monde: monde));
        Actes.Verifier(feconder with { Cible = humains.First(h => h.EstVivant).Id }, FamillesLlm.Toutes, monde: monde);
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
        // Une parole à un humain envoyée à une communauté : refusée avant d'appliquer, avec la raison (second essai possible).
        var e = Assert.Throws<ArgumentException>(() => Actes.Verifier(new ActeLlm { Type = Actes.ParlerCommunaute, Parole = nameof(Parole.UnisToi) }, FamillesLlm.Toutes));
        Assert.Contains(nameof(Parole.UnissezVous), e.Message);
        Actes.Verifier(new ActeLlm { Type = Actes.ParlerCommunaute, Parole = nameof(Parole.UnissezVous) }, FamillesLlm.Toutes);
        // Une combinaison de pièces « P1 + P2 » sans pièces montrées (regard sur le monde) ou hors de la liste : refusée avant l'acte.
        var combinaison = new ActeLlm { Type = Actes.SoufflerCombinaison, Humain = 1, Operation = "a", A = "P1", B = "P2" };
        Assert.Throws<ArgumentException>(() => Actes.Verifier(combinaison, FamillesLlm.Toutes, pieces: []));
        Assert.Throws<ArgumentException>(() => Actes.Verifier(combinaison with { B = "P3" }, FamillesLlm.Toutes, pieces: ["x", "y"]));
        Actes.Verifier(combinaison, FamillesLlm.Toutes, pieces: ["x", "y"]);
        // Un geste aux noms devinés (« frapper Galet Pierre ») : refusé avant l'acte ; les clés sans tenir compte des majuscules passent.
        Assert.Throws<ArgumentException>(() => Actes.Verifier(new ActeLlm { Type = Actes.SoufflerGeste, Humain = 1, Geste = "frapper", A = "Galet", B = "Pierre" }, FamillesLlm.Toutes));
        Assert.Throws<ArgumentException>(() => Actes.Verifier(new ActeLlm { Type = Actes.SoufflerGeste, Humain = 1, Geste = "Inexistant", A = "Galet" }, FamillesLlm.Toutes));
        var frapper = Physique.Formes.First(f => f.Geste == Geste.Frapper);
        Actes.Verifier(new ActeLlm { Type = Actes.SoufflerGeste, Humain = 1, Geste = "frapper", A = frapper.A[0].ToString(),
            B = frapper.B.Length > 0 ? frapper.B[0].ToString().ToLowerInvariant() : null }, FamillesLlm.Toutes);
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
        Assert.Equal(ClientLlm.ReponseMax, recu["options"]!["num_predict"]!.GetValue<int>());
        Assert.NotNull(recu["format"]);
        var demande = recu["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("(n°1)", demande);
        Assert.Contains("FrapperDeMort", recu["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(4, llm.Recus[1]["messages"]!.AsArray().Count); // consigne, demande, réponse fautive, correction
        // Au second essai, le modèle est retenu de se répéter ; jamais au premier.
        Assert.Null(recu["options"]!["repeat_penalty"]);
        Assert.Equal(ClientLlm.PenaliteReprise, llm.Recus[1]["options"]!["repeat_penalty"]!.GetValue<double>());
        Assert.Equal(ClientLlm.TemperatureReprise, llm.Recus[1]["options"]!["temperature"]!.GetValue<double>());

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
        // Sans la case « Écrire les échanges dans un fichier texte », rien n'est écrit.
        Assert.False(File.Exists(usine.Services.GetRequiredService<TraceLlm>().Chemin));
    }

    [Fact]
    public void Budget_RaccourcitLesListes_PourTenirDansLeContexte()
    {
        var demande = "Ce qui s'est passé :\n" + string.Join("\n", Enumerable.Range(1, 200).Select(i => "- fait " + i + " " + new string('x', 500)))
            + "\nQue fais-tu ?";
        var (court, raccourcie, jetons) = BudgetLlm.Ajuster("consigne", demande, 100, 8192);
        Assert.True(raccourcie);
        Assert.True(jetons > 8192);
        Assert.True(court.Length + 108 <= 0.7 * 8192 * BudgetLlm.CaracteresParJeton);
        Assert.StartsWith("Ce qui s'est passé :\n- fait 1 ", court);
        Assert.EndsWith("Que fais-tu ?", court);
        Assert.Contains("- …", court);
        // Assez court : rien ne change.
        Assert.Equal(("abc", false), (BudgetLlm.Ajuster("c", "abc", 10, 8192).Demande, BudgetLlm.Ajuster("c", "abc", 10, 8192).Raccourcie));
    }

    [Fact]
    public async Task Trace_EcritChaqueEchangeEnClair_DansLeFichier()
    {
        using var usine = new UsineTest();
        using var llm = new FauxLlm(ParoleDeRassurance);
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 8192 });
        await c.InvokeAsync<EtatLu>("ReglerEntiteLlm", new
        {
            mode = "Joueur", prieres = false, partOrdinaires = 0, attendre = false, paroles = true, bienfaits = true, chatiments = false, catastrophes = false,
            souffles = false, trace = true,
        });
        await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0);
        var e = Assert.Single(await Attendre(c, l => l.Count > 0));
        await c.InvokeAsync<List<string>>("AccepterProposition", e.Id);
        var chemin = usine.Services.GetRequiredService<TraceLlm>().Chemin;
        Assert.Equal(TraceLlm.NomFichier, Path.GetFileName(chemin));
        Assert.StartsWith(usine.Dossier, chemin);
        var texte = File.ReadAllText(chemin);
        // Le bloc complet (ce qu'il a lu, sa réponse, ses mots), puis le bloc court quand la proposition est appliquée.
        Assert.Equal(2, texte.Split("════ ").Length - 1);
        Assert.Contains("(n°1)", texte);
        Assert.Contains("N'aie pas peur.", texte);
        Assert.Contains(ParoleDeRassurance, texte);
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
        // Châtiments interdits : la réponse est refusée deux fois ; au second essai l'acte fautif est écarté (rien à faire) et noté
        // en erreur ; l'humain 1 vit toujours.
        await c.InvokeAsync<int>("QueFeraitLEntite", 1, 0);
        var liste = await Attendre(c, l => l.Count > 0 && l[0].StatutCode != "propose");
        var e = Assert.Single(liste);
        Assert.Equal("rien", e.StatutCode);
        Assert.Empty(e.Actes);
        Assert.StartsWith(L.T("llm.erreur.secondEssai", ("erreurs", "")).TrimEnd(), e.Erreur);
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
