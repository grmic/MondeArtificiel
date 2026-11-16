using System.Text.Json.Nodes;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Xunit;

namespace Monde.Tests;

/// <summary>
/// 0.44.2 : des modèles qui réfléchissent (prism-ml/bonsai-27b servi par LM Studio) rendent un « content » vide et écrivent
/// leur réponse JSON dans « reasoning_content » ; le dialogue affichait alors « Réponse illisible (JSON attendu) ».
/// </summary>
public sealed class ReflexionLlmTests
{
    private const string Reponse = """{"reponse": "Je ne sais pas ce que tu dis, mais je te regarde. Tu es là, je te vois.", "mots": "", "pensee": "Je ne comprends pas ses mots {étranges}.", "demande": "", "pour": 0, "promesse": "", "changements": []}""";

    private static JsonObject OpenAI(JsonNode? contenu, string? champ = null, string? reflexion = null)
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = contenu };
        if (champ is not null) message[champ] = reflexion;
        return new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = message, ["finish_reason"] = "stop" }) };
    }

    [Fact]
    public void LMStudio_ContenuVide_ReponseDansReasoningContent()
    {
        Assert.Equal(Reponse, ClientLlm.Extraire(OpenAI("", "reasoning_content", Reponse), ollama: false));
    }

    [Theory]
    [InlineData("reasoning")]
    [InlineData("reasoning_content")]
    public void ContenuNul_ReflexionAvecTexteAutour_DernierObjetValide(string champ)
    {
        var reflexion = "Je pèse {ses mots} ; un brouillon : {\"reponse\": \"non\"} puis non, mieux :\n```json\n" + Reponse + "\n```\nVoilà.";
        Assert.Equal(Reponse, ClientLlm.Extraire(OpenAI(null, champ, reflexion), ollama: false));
    }

    [Fact]
    public void Ollama_ContenuVide_ReponseDansThinking()
    {
        var json = new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "", ["thinking"] = Reponse } };
        Assert.Equal(Reponse, ClientLlm.Extraire(json, ollama: true));
    }

    [Fact]
    public void ContenuPresent_ReflexionIgnoree()
    {
        const string vrai = """{"reponse":"Oui.","mots":"","pensee":"","demande":"","pour":0,"promesse":"","changements":[]}""";
        Assert.Equal(vrai, ClientLlm.Extraire(OpenAI("<think>{\"brouillon\":1}</think>\n```json\n" + vrai + "\n```", "reasoning_content", Reponse), ollama: false));
    }

    [Fact]
    public void ContenuEnParties_TexteRecolle()
    {
        var parties = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "{\"reponse\":" }, new JsonObject { ["type"] = "text", ["text"] = "\"Oui.\"}" });
        Assert.Equal("{\"reponse\":\"Oui.\"}", ClientLlm.Extraire(OpenAI(parties), ollama: false));
    }

    [Fact]
    public void RienNulle_Part_ResteVide()
    {
        Assert.Equal("", ClientLlm.Extraire(OpenAI(""), ollama: false));
        Assert.Equal("", ClientLlm.Extraire(OpenAI("", "reasoning_content", "Je réfléchis sans conclure {"), ollama: false));
        Assert.Null(ClientLlm.DernierObjet("rien {de} valide"));
    }

    [Fact]
    public async Task LMStudio_DemanderDeboutEnBout()
    {
        var faux = new FauxCloud(FauxCloud.Ok(OpenAI("", "reasoning_content", Reponse).ToJsonString()));
        var dossier = Path.Combine(Path.GetTempPath(), "monde-reflexion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var client = new ClientLlm(new HttpClient(faux), new HttpClient(new FauxCloud(FauxCloud.Ok("{}"))), ClesLlm.Dans(dossier), DepensesLlm.Dans(dossier));
            var c = new ConnexionLlm(ConnexionLlm.LMStudio, ConnexionLlm.AdresseParDefaut(ConnexionLlm.LMStudio), "prism-ml/bonsai-27b");
            var texte = await client.Demander(c, "Consigne", [("user", "Lève toi")], new JsonObject { ["type"] = "object" }, CancellationToken.None);
            Assert.Equal(Reponse, texte);
            Assert.EndsWith("/v1/chat/completions", Assert.Single(faux.Recus).Adresse.ToString());
        }
        finally { Directory.Delete(dossier, recursive: true); }
    }
}
