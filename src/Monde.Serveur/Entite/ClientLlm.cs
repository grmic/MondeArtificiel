using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Monde.Serveur.Entite;

/// <summary>
/// Le LLM local : Ollama (son API à lui), LM Studio ou llama.cpp (API compatible OpenAI). Réglage gardé pour l'installation, dans
/// <c>entite-llm.json</c> à côté du dossier des mondes. <see cref="Contexte"/> : taille de la fenêtre demandée à Ollama (jetons).
/// </summary>
public sealed record ConnexionLlm(string Fournisseur, string Adresse, string Modele, int Contexte = 16384)
{
    public const string Ollama = "ollama";
    public const string LMStudio = "lmstudio";
    public const string LlamaCpp = "llamacpp";
    public static readonly string[] Fournisseurs = [Ollama, LMStudio, LlamaCpp];

    public static string AdresseParDefaut(string fournisseur) => fournisseur switch
    {
        LMStudio => "http://localhost:1234",
        LlamaCpp => "http://localhost:8080",
        _ => "http://localhost:11434",
    };

    public static ConnexionLlm ParDefaut => new(Ollama, AdresseParDefaut(Ollama), "");

    /// <summary>Message lisible si le réglage est invalide, sinon null.</summary>
    public string? Erreur()
    {
        if (Array.IndexOf(Fournisseurs, Fournisseur) < 0) return L.T("llm.erreur.fournisseur");
        if (!Uri.TryCreate(Adresse, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(u.UserInfo))
            return L.T("llm.erreur.adresse");
        if (Modele.Length > 200 || Contexte is < 2048 or > 1_048_576) return L.T("llm.erreur.modele");
        return null;
    }

    public string Libelle => $"{Fournisseur}:{Modele}";
}

/// <summary>Appels au LLM local. Une réponse est demandée au format imposé par un schéma JSON.</summary>
public sealed partial class ClientLlm(HttpClient http)
{
    /// <summary>Modèles installés (noms à donner tels quels).</summary>
    public async Task<List<string>> Modeles(ConnexionLlm c, CancellationToken arret)
    {
        var baseUri = c.Adresse.TrimEnd('/');
        if (c.Fournisseur == ConnexionLlm.Ollama)
        {
            var r = await http.GetFromJsonAsync<JsonObject>($"{baseUri}/api/tags", arret);
            return r?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().Order(StringComparer.Ordinal).ToList() ?? [];
        }
        var o = await http.GetFromJsonAsync<JsonObject>($"{baseUri}/v1/models", arret);
        return o?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>()).OfType<string>().Order(StringComparer.Ordinal).ToList() ?? [];
    }

    /// <summary>
    /// Une conversation (consigne, puis messages utilisateur et assistant alternés) ; rend le texte de la réponse, débarrassé
    /// d'une éventuelle réflexion (&lt;think&gt;…&lt;/think&gt;) et des balises de code autour du JSON.
    /// </summary>
    /// <summary>
    /// Longueur maximale d'une réponse (jetons, 0.37.5). Une réponse normale en fait 200 à 1 500 ; sans borne, un petit modèle qui
    /// s'emballe écrit jusqu'à remplir tout le contexte (vu le 8/11 : 28 700 jetons en 6 min 30 avec gemma3:4b, et Ollama continue
    /// même après l'abandon du serveur, ce qui bloque les demandes suivantes). Coupée, la réponse est illisible et le LLM est repris.
    /// </summary>
    public const int ReponseMax = 2048;

    public async Task<string> Demander(ConnexionLlm c, string consigne, IReadOnlyList<(string Role, string Texte)> messages, JsonObject schema,
        CancellationToken arret)
    {
        var baseUri = c.Adresse.TrimEnd('/');
        var liste = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = consigne } };
        foreach (var (role, contenuMessage) in messages) liste.Add(new JsonObject { ["role"] = role, ["content"] = contenuMessage });
        JsonObject corps;
        string url;
        if (c.Fournisseur == ConnexionLlm.Ollama)
        {
            url = $"{baseUri}/api/chat";
            corps = new JsonObject
            {
                ["model"] = c.Modele, ["messages"] = liste, ["stream"] = false, ["format"] = schema.DeepClone(),
                ["options"] = new JsonObject { ["num_ctx"] = c.Contexte, ["temperature"] = 0.8, ["num_predict"] = ReponseMax },
            };
        }
        else
        {
            url = $"{baseUri}/v1/chat/completions";
            corps = new JsonObject
            {
                ["model"] = c.Modele, ["messages"] = liste, ["stream"] = false, ["temperature"] = 0.8, ["max_tokens"] = ReponseMax,
                ["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "decision", ["strict"] = false, ["schema"] = schema.DeepClone() },
                },
            };
        }
        using var reponse = await http.PostAsJsonAsync(url, corps, arret);
        var texte = await reponse.Content.ReadAsStringAsync(arret);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException(L.T("llm.erreur.http", ("code", (int)reponse.StatusCode), ("texte", Couper(texte, 300))));
        var json = JsonNode.Parse(texte);
        var contenu = c.Fournisseur == ConnexionLlm.Ollama
            ? json?["message"]?["content"]?.GetValue<string>()
            : json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        return Nettoyer(contenu ?? "");
    }

    /// <summary>Retire une réflexion &lt;think&gt; et les balises ```json ; garde l'objet JSON du premier « { » au dernier « } ».</summary>
    public static string Nettoyer(string texte)
    {
        texte = Reflexion().Replace(texte, "").Trim();
        var debut = texte.IndexOf('{');
        var fin = texte.LastIndexOf('}');
        return debut >= 0 && fin > debut ? texte[debut..(fin + 1)] : texte;
    }

    public static string Couper(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>
    /// Une sortie qui s'emballe ramenée à ce qu'elle dit (0.36.4) : un même mot répété plus de deux fois de suite, ou un même membre de
    /// phrase revenu plus de deux fois (« Fécondité, Fécondité, Fécondité… »), n'est gardé que deux fois ; puis le texte est coupé à
    /// <paramref name="n"/> caractères.
    /// </summary>
    public static string Resserrer(string s, int n)
    {
        s = s.Trim();
        if (s.Length == 0) return s;
        // Mots répétés de suite.
        s = MotsRepetes().Replace(s, m => m.Groups[1].Value + " " + m.Groups[1].Value);
        // Membres de phrase revenus trop souvent.
        var morceaux = Membres().Split(s);
        var vus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var garde = new System.Text.StringBuilder();
        foreach (var m in morceaux)
        {
            var cle = m.Trim().TrimEnd(',', ';', '.', '!', '?', '…', ' ').Trim();
            if (cle.Length > 0)
            {
                vus[cle] = vus.GetValueOrDefault(cle) + 1;
                if (vus[cle] > 2) continue;
            }
            garde.Append(m);
        }
        return Couper(garde.ToString().Trim().TrimEnd(','), n);
    }

    [GeneratedRegex(@"\b(\w+)(?:[\s,;]+\1\b){2,}", RegexOptions.IgnoreCase)]
    private static partial Regex MotsRepetes();

    [GeneratedRegex(@"(?<=[,;.!?…\n])")]
    private static partial Regex Membres();

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex Reflexion();
}
