using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>
/// Faux service du cloud (0.38.0) : garde chaque demande (adresse, en-têtes, corps) et rend les réponses données, l'une après l'autre
/// (la dernière se répète). Rien ne sort de la machine.
/// </summary>
internal sealed class FauxCloud : HttpMessageHandler
{
    public sealed record Recu(HttpMethod Methode, Uri Adresse, Dictionary<string, string> EnTetes, JsonObject? Corps);

    private readonly Queue<(HttpStatusCode Code, string Corps, TimeSpan? Attendre)> _reponses;
    private readonly (HttpStatusCode, string, TimeSpan?) _derniere;
    public List<Recu> Recus { get; } = [];

    public FauxCloud(params (HttpStatusCode Code, string Corps, TimeSpan? Attendre)[] reponses)
    {
        _reponses = new(reponses);
        _derniere = reponses[^1];
    }

    public static (HttpStatusCode, string, TimeSpan?) Ok(string corps) => (HttpStatusCode.OK, corps, null);

    /// <summary>Une réponse au format OpenAI : le texte et les jetons facturés.</summary>
    public static (HttpStatusCode, string, TimeSpan?) OpenAI(string texte, long lus = 1000, long ecrits = 100) => Ok(new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = texte } }),
        ["usage"] = new JsonObject { ["prompt_tokens"] = lus, ["completion_tokens"] = ecrits },
    }.ToJsonString());

    /// <summary>Une réponse de l'API Messages d'Anthropic.</summary>
    public static (HttpStatusCode, string, TimeSpan?) Claude(string texte, long lus = 2000, long ecrits = 300) => Ok(new JsonObject
    {
        ["id"] = "msg_test", ["type"] = "message", ["role"] = "assistant", ["model"] = "claude-haiku-5-5",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texte }),
        ["stop_reason"] = "end_turn", ["stop_sequence"] = null,
        ["usage"] = new JsonObject { ["input_tokens"] = lus, ["output_tokens"] = ecrits },
    }.ToJsonString());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage demande, CancellationToken arret)
    {
        var entetes = demande.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
        var texte = demande.Content is null ? null : await demande.Content.ReadAsStringAsync(arret);
        (HttpStatusCode Code, string Corps, TimeSpan? Attendre) r;
        lock (Recus)
        {
            Recus.Add(new Recu(demande.Method, demande.RequestUri!, entetes, string.IsNullOrEmpty(texte) ? null : JsonNode.Parse(texte)?.AsObject()));
            r = _reponses.Count > 0 ? _reponses.Dequeue() : _derniere;
        }
        var reponse = new HttpResponseMessage(r.Code) { Content = new StringContent(r.Corps, Encoding.UTF8, "application/json") };
        if (r.Attendre is { } a) reponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(a);
        reponse.Headers.Add("request-id", "req_test");
        return reponse;
    }
}

public sealed class LlmCloudTests : IDisposable
{
    private const string Decision = """{"mots":"Je vous entends.","pensee":"Rien.","actes":[]}""";
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-cloud-" + Guid.NewGuid().ToString("N"));

    public LlmCloudTests() => Directory.CreateDirectory(_dossier);

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["mots"] = new JsonObject { ["type"] = "string" },
            ["actes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["type"] = new JsonObject { ["type"] = "string" } } } },
        },
    };

    private (ClientLlm Client, ClesLlm Cles, DepensesLlm Depenses) Client(FauxCloud faux)
    {
        var cles = ClesLlm.Dans(_dossier);
        var depenses = DepensesLlm.Dans(_dossier);
        return (new ClientLlm(new HttpClient(new FauxCloud(FauxCloud.Ok("{}"))), new HttpClient(faux), cles, depenses), cles, depenses);
    }

    [Fact]
    public async Task OpenAI_ModeleQuiReflechit_SansTemperature_AvecCleEtSchema_JetonsComptes()
    {
        var faux = new FauxCloud(FauxCloud.OpenAI(Decision, 8000, 800));
        var (client, cles, depenses) = Client(faux);
        cles.Enregistrer(ConnexionLlm.OpenAI, "sk-test-0123456789abcd", "");
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "https://ailleurs.example", "gpt-5.4-mini"));
        Assert.Equal("https://api.openai.com", c.Adresse);
        Assert.Equal(0.75, c.PrixLu);

        var mesure = DepensesLlm.Mesurer();
        var texte = await client.Demander(c, "Consigne", [("user", "Demande")], Schema, CancellationToken.None);

        Assert.Equal(Decision, texte);
        var r = Assert.Single(faux.Recus);
        Assert.Equal("https://api.openai.com/v1/chat/completions", r.Adresse.ToString());
        Assert.Equal("Bearer sk-test-0123456789abcd", r.EnTetes["authorization"]);
        Assert.False(r.Corps!.ContainsKey("temperature"));
        Assert.False(r.Corps.ContainsKey("max_tokens"));
        Assert.Equal(ClientLlm.ReponseMaxCloud, r.Corps["max_completion_tokens"]!.GetValue<int>());
        Assert.Equal("low", r.Corps["reasoning_effort"]!.GetValue<string>());
        Assert.Equal("json_schema", r.Corps["response_format"]!["type"]!.GetValue<string>());
        // 8 000 × 0,75 + 800 × 4,50 = 9 600 millionièmes de dollar.
        Assert.Equal(0.0096, mesure.Dollars, 6);
        Assert.Equal(8000, mesure.JetonsLus);
        var d = depenses.Etat();
        Assert.Equal(0.0096, d.Dollars, 6);
        Assert.Equal(1, d.Appels);
    }

    [Fact]
    public async Task Mistral_FormatOpenAI_AvecTemperature()
    {
        var faux = new FauxCloud(FauxCloud.OpenAI(Decision));
        var (client, cles, _) = Client(faux);
        cles.Enregistrer(ConnexionLlm.Mistral, "cle-mistral-123456", "");
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.Mistral, "", "mistral-large-latest"));
        await client.Demander(c, "Consigne", [("user", "Demande")], Schema, CancellationToken.None);
        var r = Assert.Single(faux.Recus);
        Assert.Equal("https://api.mistral.ai/v1/chat/completions", r.Adresse.ToString());
        Assert.Equal(ClientLlm.ReponseMaxCloud, r.Corps!["max_tokens"]!.GetValue<int>());
        Assert.True(r.Corps.ContainsKey("temperature"));
        Assert.Equal("system", r.Corps["messages"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Infomaniak_SchemaRefuse_RepriseSansSchema_PuisPlusJamais()
    {
        var faux = new FauxCloud((HttpStatusCode.BadRequest, """{"error":"response_format json_schema not supported"}""", null), FauxCloud.OpenAI(Decision));
        var (client, cles, _) = Client(faux);
        cles.Enregistrer(ConnexionLlm.Infomaniak, "cle-infomaniak-123456", "");
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.Infomaniak, "https://api.infomaniak.com/2/ai/4242/openai", "mistralai/Mistral-Small-4-119B-2603"));
        Assert.Equal(Decision, await client.Demander(c, "Consigne", [("user", "Demande")], Schema, CancellationToken.None));
        await client.Demander(c, "Consigne", [("user", "Demande")], Schema, CancellationToken.None);
        Assert.Equal(3, faux.Recus.Count);
        Assert.Equal("https://api.infomaniak.com/2/ai/4242/openai/v1/chat/completions", faux.Recus[0].Adresse.ToString());
        Assert.True(faux.Recus[0].Corps!.ContainsKey("response_format"));
        Assert.False(faux.Recus[1].Corps!.ContainsKey("response_format"));
        Assert.False(faux.Recus[2].Corps!.ContainsKey("response_format"));
    }

    [Fact]
    public void Infomaniak_SansNumeroDeProduit_Refuse()
    {
        Assert.NotNull(new ConnexionLlm(ConnexionLlm.Infomaniak, ConnexionLlm.AdresseParDefaut(ConnexionLlm.Infomaniak), "x").Erreur());
        Assert.NotNull(new ConnexionLlm(ConnexionLlm.Infomaniak, "https://evil.example/2/ai/12/openai", "x").Erreur());
        Assert.Null(new ConnexionLlm(ConnexionLlm.Infomaniak, "https://api.infomaniak.com/2/ai/12/openai", "x").Erreur());
    }

    [Fact]
    public async Task ServiceOccupe_429_AttendPuisReprend()
    {
        var faux = new FauxCloud((HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", TimeSpan.FromSeconds(1)), FauxCloud.OpenAI(Decision));
        var (client, cles, _) = Client(faux);
        cles.Enregistrer(ConnexionLlm.OpenAI, "sk-test-0123456789abcd", "");
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "", "gpt-5.4-mini"));
        Assert.Equal(Decision, await client.Demander(c, "Consigne", [("user", "Demande")], Schema, CancellationToken.None));
        Assert.Equal(2, faux.Recus.Count);
    }

    [Fact]
    public async Task Erreur_NeMontreJamaisLaCle()
    {
        const string cle = "sk-secret-abcdefghijkl";
        var faux = new FauxCloud((HttpStatusCode.Unauthorized, $$"""{"error":"Incorrect API key provided: {{cle}}"}""", null));
        var (client, cles, _) = Client(faux);
        cles.Enregistrer(ConnexionLlm.OpenAI, cle, "");
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "", "gpt-5.4-mini"));
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => client.Demander(c, "C", [("user", "D")], Schema, CancellationToken.None));
        Assert.DoesNotContain(cle, e.Message);
        Assert.Contains("401", e.Message);
    }

    [Fact]
    public async Task Claude_ParLeSdk_EnTetes_SchemaFerme_SansTemperature_JetonsComptes()
    {
        var faux = new FauxCloud(FauxCloud.Claude(Decision, 2000, 300));
        var (client, cles, depenses) = Client(faux);
        cles.Enregistrer(ConnexionLlm.Anthropic, "sk-ant-test-0123456789", "");
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.Anthropic, "", "claude-haiku-5-5"));
        var texte = await client.Demander(c, "Consigne de l'Entité", [("user", "Demande"), ("assistant", "{}"), ("user", "Corrige")], Schema, CancellationToken.None);

        Assert.Equal(Decision, texte);
        var r = Assert.Single(faux.Recus);
        Assert.Equal("https://api.anthropic.com/v1/messages", r.Adresse.ToString());
        // Le SDK (13.x) envoie la clé en « Authorization: Bearer ».
        Assert.Equal("Bearer sk-ant-test-0123456789", r.EnTetes["authorization"]);
        Assert.True(r.EnTetes.ContainsKey("anthropic-version"));
        Assert.False(r.Corps!.ContainsKey("temperature"));
        Assert.Equal("Consigne de l'Entité", r.Corps["system"]!.GetValue<string>());
        Assert.Equal(3, r.Corps["messages"]!.AsArray().Count);
        Assert.Equal("assistant", r.Corps["messages"]![1]!["role"]!.GetValue<string>());
        var format = r.Corps["output_config"]!["format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.False(format["schema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(format["schema"]!["properties"]!["actes"]!["items"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("low", r.Corps["output_config"]!["effort"]!.GetValue<string>());
        // 2 000 × 0,10 + 300 × 0,50 = 350 millionièmes de dollar.
        Assert.Equal(0.00035, depenses.Etat().Dollars, 8);
    }

    [Fact]
    public async Task Plafond_Atteint_RienNePart()
    {
        var faux = new FauxCloud(FauxCloud.OpenAI(Decision, 1_000_000, 0));
        var (client, cles, depenses) = Client(faux);
        cles.Enregistrer(ConnexionLlm.OpenAI, "sk-test-0123456789abcd", "");
        depenses.Regler(0.5);
        var c = EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "", "gpt-5.4-mini"));
        await client.Demander(c, "C", [("user", "D")], Schema, CancellationToken.None);
        Assert.True(depenses.Atteint);
        Assert.True(depenses.Etat().Alerte);
        await Assert.ThrowsAsync<LlmSuspenduException>(() => client.Demander(c, "C", [("user", "D")], Schema, CancellationToken.None));
        Assert.Single(faux.Recus);
        // Plafond relevé : ça repart.
        depenses.Regler(10);
        await client.Demander(c, "C", [("user", "D")], Schema, CancellationToken.None);
        Assert.Equal(2, faux.Recus.Count);
    }

    [Fact]
    public void Depenses_NouveauMois_RepartDeZero_GardeLePlafond()
    {
        var d = DepensesLlm.Dans(_dossier);
        d.MoisCourant = () => "2026-11";
        d.Regler(7);
        d.Noter(new ConnexionLlm(ConnexionLlm.OpenAI, "https://api.openai.com", "gpt-5.4-mini", PrixLu: 1, PrixEcrit: 1), 1_000_000, 0);
        Assert.Equal(1, d.Etat().Dollars, 6);
        var relu = DepensesLlm.Dans(_dossier);
        relu.MoisCourant = () => "2026-11";
        Assert.Equal(1, relu.Etat().Dollars, 6);
        relu.MoisCourant = () => "2026-12";
        Assert.Equal(0, relu.Etat().Dollars);
        Assert.Equal(7, relu.Etat().Plafond);
    }

    [Fact]
    public void Cles_ChiffreesSurLeDisque_LieesALeurHote()
    {
        const string cle = "sk-clair-jamais-0123456789";
        var cles = ClesLlm.Dans(_dossier);
        cles.Enregistrer(ConnexionLlm.OpenAI, cle, "");
        cles.Enregistrer(ConnexionLlm.Autre, "cle-autre-0123456789", "https://llm.chez-moi.example/v1");

        // Rien en clair dans le dossier.
        foreach (var f in Directory.EnumerateFiles(_dossier, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(cle, File.ReadAllText(f));

        var relues = ClesLlm.Dans(_dossier);
        Assert.Equal(cle, relues.Pour(new ConnexionLlm(ConnexionLlm.OpenAI, "https://api.openai.com", "m")));
        // Une autre adresse n'obtient pas la clé, même si elle se dit OpenAI ou « Autre ».
        Assert.Null(relues.Pour(new ConnexionLlm(ConnexionLlm.OpenAI, "https://evil.example", "m")));
        Assert.Null(relues.Pour(new ConnexionLlm(ConnexionLlm.Autre, "https://evil.example", "m")));
        Assert.Equal("cle-autre-0123456789", relues.Pour(new ConnexionLlm(ConnexionLlm.Autre, "https://llm.chez-moi.example", "m")));
        Assert.Null(relues.Pour(new ConnexionLlm(ConnexionLlm.Ollama, "http://localhost:11434", "m")));

        var resume = relues.Resume();
        Assert.Equal("••••6789", resume[ConnexionLlm.OpenAI]);
        Assert.DoesNotContain(cle, string.Join(" ", resume.Values));

        relues.Effacer(ConnexionLlm.OpenAI);
        Assert.Null(ClesLlm.Dans(_dossier).Pour(new ConnexionLlm(ConnexionLlm.OpenAI, "https://api.openai.com", "m")));
    }

    [Fact]
    public void Preparer_ServicePayantSansPrix_Refuse_PrixConnuRepris()
    {
        Assert.Throws<ArgumentException>(() => EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "", "modele-inconnu")));
        Assert.Equal(1.5, EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "", "modele-inconnu", PrixLu: 1.5, PrixEcrit: 3)).PrixLu);
        // « Autre » peut être gratuit (serveur du réseau) ; un LLM local n'a pas de prix.
        Assert.Equal(0, EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.Autre, "http://192.168.1.10:8000", "qwen")).PrixLu);
        Assert.Equal(0, EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.Ollama, "http://localhost:11434", "gemma3:4b")).PrixLu);
        Assert.Throws<ArgumentException>(() => EntiteLlm.Preparer(new ConnexionLlm(ConnexionLlm.OpenAI, "", "x", PrixLu: -1)));
    }

    [Fact]
    public void Echange_JetonsEtCout_GardesDansLaBase()
    {
        var fichier = Path.Combine(_dossier, "monde.db");
        var depot = new DepotMonde(fichier);
        var id = depot.AjouterEchange(new EchangeEntite(0, 10, "2026-11-08T10:00:00Z", 0, 1, 0, 0, "openai:gpt-5.4-mini", 1200, "lu", "{}", "", "", "[]", "[]",
            1, 0, -1, null, 0, 8000, 800, 0.0096));
        var e = depot.Echange(id)!;
        Assert.Equal(8000, e.JetonsLus);
        Assert.Equal(800, e.JetonsEcrits);
        Assert.Equal(0.0096, e.Cout, 6);
    }

    [Fact]
    public async Task Hub_AvertissementPuisCle_JamaisEnClair_NiRenvoyee()
    {
        const string cle = "sk-hub-test-0123456789";
        using var usine = new UsineTest();
        await using var hub = usine.CreerConnexion();
        await hub.StartAsync();
        var openai = new ConnexionLlm(ConnexionLlm.OpenAI, "", "gpt-5.4-mini");

        // Sans avoir lu l'avertissement : refusé.
        var refus = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<EtatEntiteLlmDto>("ReglerConnexionLlm", openai));
        Assert.Contains(L.T("llm.erreur.pasCompris"), refus.Message);

        Assert.True(await hub.InvokeAsync<bool>("ServeurLocalLlm"));
        await hub.InvokeAsync<EtatEntiteLlmDto>("ComprendreCloudLlm");
        var etat = await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerConnexionLlm", openai);
        Assert.Equal("https://api.openai.com", etat.Connexion.Adresse);
        Assert.Equal(4.5, etat.Connexion.PrixEcrit);
        Assert.True(etat.Depenses!.Compris);

        etat = await hub.InvokeAsync<EtatEntiteLlmDto>("EnregistrerCleLlm", ConnexionLlm.OpenAI, cle, "");
        Assert.Equal("••••6789", etat.Cles![ConnexionLlm.OpenAI]);

        // Le juge à part : un LLM local, puis de nouveau le même que l'Entité.
        etat = await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerConnexionJugeLlm", new ConnexionLlm(ConnexionLlm.Ollama, "http://localhost:11434", "gemma3:4b"));
        Assert.Equal("gemma3:4b", etat.Juge!.Modele);
        etat = await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerConnexionJugeLlm", (ConnexionLlm?)null);
        Assert.Null(etat.Juge);

        etat = await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerPlafondLlm", 12.5);
        Assert.Equal(12.5, etat.Depenses!.Plafond);

        foreach (var f in Directory.EnumerateFiles(usine.Dossier, "*", SearchOption.AllDirectories))
            if (!f.EndsWith(".verrou", StringComparison.Ordinal)) Assert.DoesNotContain(cle, File.ReadAllText(f));
    }
}
