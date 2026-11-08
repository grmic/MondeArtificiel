using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Monde.Serveur.Entite;

/// <summary>
/// Le LLM qui joue l'Entité : local (Ollama avec son API à lui, LM Studio ou llama.cpp au format OpenAI) ou dans le cloud (0.38.0 :
/// OpenAI, Anthropic, Mistral, Infomaniak, ou un autre service au format OpenAI). Réglage gardé pour l'installation, dans
/// <c>entite-llm.json</c> à côté du dossier des mondes ; la clé d'API n'y est jamais (voir <see cref="ClesLlm"/>).
/// <see cref="Contexte"/> : taille de la fenêtre demandée (jetons). <see cref="PrixLu"/>, <see cref="PrixEcrit"/> : prix du modèle en
/// dollars par million de jetons, pour compter la dépense (0 : inconnu).
/// </summary>
public sealed partial record ConnexionLlm(string Fournisseur, string Adresse, string Modele, int Contexte = 16384, double PrixLu = 0, double PrixEcrit = 0)
{
    public const string Ollama = "ollama";
    public const string LMStudio = "lmstudio";
    public const string LlamaCpp = "llamacpp";
    public const string OpenAI = "openai";
    public const string Anthropic = "anthropic";
    public const string Mistral = "mistral";
    public const string Infomaniak = "infomaniak";
    public const string Autre = "autre";
    public static readonly string[] Locaux = [Ollama, LMStudio, LlamaCpp];
    public static readonly string[] Clouds = [OpenAI, Anthropic, Mistral, Infomaniak, Autre];
    public static readonly string[] Fournisseurs = [.. Locaux, .. Clouds];

    /// <summary>Prix le plus haut accepté (dollars par million de jetons) : au-delà, c'est une faute de frappe.</summary>
    public const double PrixMax = 1000;

    public static string AdresseParDefaut(string fournisseur) => fournisseur switch
    {
        LMStudio => "http://localhost:1234",
        LlamaCpp => "http://localhost:8080",
        OpenAI => "https://api.openai.com",
        Anthropic => "https://api.anthropic.com",
        Mistral => "https://api.mistral.ai",
        Infomaniak => "https://api.infomaniak.com/2/ai/NUMERO/openai",
        Autre => "https://",
        _ => "http://localhost:11434",
    };

    /// <summary>
    /// Hôte imposé (null : libre). La clé d'un fournisseur ne part que vers cet hôte : un appareil du réseau ne peut pas la détourner en
    /// changeant l'adresse.
    /// </summary>
    public static string? HoteImpose(string fournisseur) => fournisseur switch
    {
        OpenAI => "api.openai.com",
        Anthropic => "api.anthropic.com",
        Mistral => "api.mistral.ai",
        Infomaniak => "api.infomaniak.com",
        _ => null,
    };

    public static ConnexionLlm ParDefaut => new(Ollama, AdresseParDefaut(Ollama), "");

    /// <summary>Vrai pour un service sur Internet : payant, avec une clé d'API.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstCloud => Array.IndexOf(Clouds, Fournisseur) >= 0;

    /// <summary>Vrai quand le prix est connu (la dépense peut être comptée).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PrixConnu => PrixLu > 0 || PrixEcrit > 0;

    /// <summary>La même connexion, adresse remise à celle du fournisseur quand elle est imposée (sans « / » final).</summary>
    public ConnexionLlm Normalisee()
    {
        var adresse = (Adresse ?? "").Trim().TrimEnd('/');
        if (Fournisseur is OpenAI or Anthropic or Mistral) adresse = AdresseParDefaut(Fournisseur);
        return this with { Adresse = adresse, Modele = (Modele ?? "").Trim() };
    }

    /// <summary>Message lisible si le réglage est invalide, sinon null.</summary>
    public string? Erreur()
    {
        if (Array.IndexOf(Fournisseurs, Fournisseur) < 0) return L.T("llm.erreur.fournisseur");
        if (!Uri.TryCreate(Adresse, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(u.UserInfo))
            return L.T("llm.erreur.adresse");
        if (Modele is null || Modele.Length > 200 || Contexte is < 2048 or > 1_048_576) return L.T("llm.erreur.modele");
        if (double.IsNaN(PrixLu) || double.IsNaN(PrixEcrit) || PrixLu is < 0 or > PrixMax || PrixEcrit is < 0 or > PrixMax) return L.T("llm.erreur.prix");
        if (Fournisseur == Infomaniak && !AdresseInfomaniak().IsMatch(Adresse)) return L.T("llm.erreur.infomaniak");
        // Les services payants passent par https ; « Autre » peut être un serveur du réseau local (http).
        if (EstCloud && Fournisseur != Autre && u.Scheme != "https") return L.T("llm.erreur.adresse");
        if (HoteImpose(Fournisseur) is { } hote && !string.Equals(u.Host, hote, StringComparison.OrdinalIgnoreCase)) return L.T("llm.erreur.adresse");
        return null;
    }

    public string Libelle => $"{Fournisseur}:{Modele}";

    [GeneratedRegex(@"^https://api\.infomaniak\.com/2/ai/\d{1,12}/openai/?$", RegexOptions.IgnoreCase)]
    private static partial Regex AdresseInfomaniak();
}

/// <summary>Le LLM cloud ne sera plus appelé ce mois-ci : le plafond de dépense est atteint.</summary>
public sealed class LlmSuspenduException(string message) : InvalidOperationException(message);

/// <summary>
/// Appels au LLM. Une réponse est demandée au format imposé par un schéma JSON. Les services du cloud passent par un client HTTP qui
/// suit le proxy du système ; les jetons qu'ils facturent sont notés dans <see cref="DepensesLlm"/>.
/// </summary>
public sealed partial class ClientLlm(HttpClient http, HttpClient cloud, ClesLlm cles, DepensesLlm depenses)
{
    /// <summary>Client sans cloud (tests, outils) : les clés et la dépense sont alors absentes.</summary>
    public ClientLlm(HttpClient http) : this(http, http, ClesLlm.Aucune, DepensesLlm.Aucune)
    {
    }

    /// <summary>Modèles disponibles (noms à donner tels quels).</summary>
    public async Task<List<string>> Modeles(ConnexionLlm c, CancellationToken arret)
    {
        c = c.Normalisee();
        var baseUri = c.Adresse;
        if (c.Fournisseur == ConnexionLlm.Ollama)
        {
            var r = await http.GetFromJsonAsync<JsonObject>($"{baseUri}/api/tags", arret);
            return r?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().Order(StringComparer.Ordinal).ToList() ?? [];
        }
        var cle = c.EstCloud ? cles.Pour(c) ?? throw new InvalidOperationException(L.T("llm.erreur.pasDeCle")) : null;
        if (c.Fournisseur == ConnexionLlm.Anthropic)
        {
            var client = Claude(c, cle!);
            var page = await client.Models.List(cancellationToken: arret);
            return page.Items.Select(m => m.ID).Order(StringComparer.Ordinal).ToList();
        }
        using var demande = new HttpRequestMessage(HttpMethod.Get, $"{baseUri}/v1/models");
        if (cle is not null) demande.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cle);
        using var reponse = await (c.EstCloud ? cloud : http).SendAsync(demande, arret);
        var texte = await reponse.Content.ReadAsStringAsync(arret);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException(L.T("llm.erreur.http", ("code", (int)reponse.StatusCode), ("texte", Masquer(Couper(texte, 300), cle))));
        var o = JsonNode.Parse(texte)?.AsObject();
        return o?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>()).OfType<string>().Order(StringComparer.Ordinal).ToList() ?? [];
    }

    /// <summary>
    /// Longueur maximale d'une réponse (jetons, 0.37.5). Une réponse normale en fait 200 à 1 500 ; sans borne, un petit modèle qui
    /// s'emballe écrit jusqu'à remplir tout le contexte (vu le 8/11 : 28 700 jetons en 6 min 30 avec gemma3:4b, et Ollama continue
    /// même après l'abandon du serveur, ce qui bloque les demandes suivantes). Coupée, la réponse est illisible et le LLM est repris.
    /// </summary>
    public const int ReponseMax = 2048;

    /// <summary>
    /// Longueur maximale d'une réponse d'un LLM du cloud (0.38.0) : les modèles qui réfléchissent comptent leur réflexion dans cette
    /// borne ; 2 048 la couperait souvent avant la réponse. Seuls les jetons écrits sont facturés.
    /// </summary>
    public const int ReponseMaxCloud = 8192;

    /// <summary>
    /// Au second essai (0.37.6), le modèle est retenu de se répéter : pénalité de répétition sur les 256 derniers jetons et température
    /// plus basse, car un petit modèle qui a bouclé une fois repart souvent dans la même boucle.
    /// </summary>
    public const double PenaliteReprise = 1.15, TemperatureReprise = 0.6, Temperature = 0.8;

    /// <summary>Services qui ont refusé le schéma JSON imposé : on leur demande ensuite du JSON par la consigne seule.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _sansSchema = new();

    /// <summary>
    /// Une conversation (consigne, puis messages utilisateur et assistant alternés) ; rend le texte de la réponse, débarrassé
    /// d'une éventuelle réflexion (&lt;think&gt;…&lt;/think&gt;) et des balises de code autour du JSON.
    /// </summary>
    public async Task<string> Demander(ConnexionLlm c, string consigne, IReadOnlyList<(string Role, string Texte)> messages, JsonObject schema,
        CancellationToken arret, bool reprise = false)
    {
        c = c.Normalisee();
        string? cle = null;
        if (c.EstCloud)
        {
            depenses.Verifier();
            cle = cles.Pour(c) ?? throw new InvalidOperationException(L.T("llm.erreur.pasDeCle"));
        }
        if (c.Fournisseur == ConnexionLlm.Anthropic) return Nettoyer(await DemanderClaude(c, cle!, consigne, messages, schema, arret));
        var baseUri = c.Adresse;
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
                ["options"] = new JsonObject { ["num_ctx"] = c.Contexte, ["temperature"] = reprise ? TemperatureReprise : Temperature, ["num_predict"] = ReponseMax },
            };
            if (reprise)
            {
                corps["options"]!["repeat_penalty"] = PenaliteReprise;
                corps["options"]!["repeat_last_n"] = 256;
            }
        }
        else
        {
            url = $"{baseUri}/v1/chat/completions";
            corps = new JsonObject { ["model"] = c.Modele, ["messages"] = liste, ["stream"] = false };
            if (ReflechitAvant(c))
            {
                // Modèles d'OpenAI qui réfléchissent (GPT-5, o3…) : ni température ni pénalité ; la borne compte aussi la réflexion,
                // qu'on garde courte (elle est facturée).
                corps["max_completion_tokens"] = ReponseMaxCloud;
                corps["reasoning_effort"] = "low";
            }
            else
            {
                corps["temperature"] = reprise ? TemperatureReprise : Temperature;
                corps["max_tokens"] = c.EstCloud ? ReponseMaxCloud : ReponseMax;
                // Style OpenAI : la pénalité de fréquence (0 à 2) tient le rôle de repeat_penalty.
                if (reprise) corps["frequency_penalty"] = 0.4;
            }
            if (!_sansSchema.ContainsKey(CleService(c)))
                corps["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "decision", ["strict"] = false, ["schema"] = schema.DeepClone() },
                };
        }
        var json = await Envoyer(c, cle, url, corps, arret);
        string? contenu;
        if (c.Fournisseur == ConnexionLlm.Ollama) contenu = json?["message"]?["content"]?.GetValue<string>();
        else
        {
            contenu = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            if (c.EstCloud && json?["usage"] is JsonObject u)
                depenses.Noter(c, Entier(u["prompt_tokens"]), Entier(u["completion_tokens"]));
        }
        return Nettoyer(contenu ?? "");
    }

    /// <summary>Envoie une demande ; un service du cloud occupé (429, 503) est réessayé deux fois, après l'attente qu'il demande.</summary>
    private async Task<JsonNode?> Envoyer(ConnexionLlm c, string? cle, string url, JsonObject corps, CancellationToken arret)
    {
        var client = c.EstCloud ? cloud : http;
        for (var essai = 0; ; essai++)
        {
            using var demande = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corps) };
            if (cle is not null) demande.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cle);
            using var reponse = await client.SendAsync(demande, arret);
            var texte = await reponse.Content.ReadAsStringAsync(arret);
            if (reponse.IsSuccessStatusCode) return JsonNode.Parse(texte);
            var code = (int)reponse.StatusCode;
            if (c.EstCloud && code is 429 or 503 && essai < 2)
            {
                var attente = reponse.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(essai == 0 ? 2 : 6);
                await Task.Delay(attente < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : attente > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : attente, arret);
                continue;
            }
            // Un service qui ne connaît pas le schéma imposé (format OpenAI incomplet) : une fois sans, la consigne demande déjà du JSON.
            if (code is 400 or 422 && corps.ContainsKey("response_format") && c.Fournisseur != ConnexionLlm.Ollama
                && texte.Contains("response_format", StringComparison.OrdinalIgnoreCase) | texte.Contains("json_schema", StringComparison.OrdinalIgnoreCase))
            {
                _sansSchema[CleService(c)] = true;
                corps.Remove("response_format");
                continue;
            }
            throw new HttpRequestException(L.T("llm.erreur.http", ("code", code), ("texte", Masquer(Couper(texte, 300), cle))));
        }
    }

    private static string CleService(ConnexionLlm c) => c.Adresse + "|" + c.Modele;

    private static long Entier(JsonNode? n) => n is JsonValue v && v.TryGetValue<long>(out var x) ? Math.Max(0, x) : 0;

    /// <summary>Modèles d'OpenAI qui réfléchissent avant de répondre (GPT-5.x, série o) : leurs réglages diffèrent.</summary>
    public static bool ReflechitAvant(ConnexionLlm c) => c.Fournisseur == ConnexionLlm.OpenAI
        && (c.Modele.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) || ModeleO().IsMatch(c.Modele));

    [GeneratedRegex(@"^o\d", RegexOptions.IgnoreCase)]
    private static partial Regex ModeleO();

    /// <summary>Une clé qui reviendrait dans un message d'erreur du service n'est jamais montrée.</summary>
    public static string Masquer(string texte, string? cle) =>
        string.IsNullOrEmpty(cle) || cle.Length < 8 ? texte : texte.Replace(cle, "••••", StringComparison.Ordinal);

    // ───────────── Claude (API Anthropic, par le SDK officiel) ─────────────

    private global::Anthropic.AnthropicClient Claude(ConnexionLlm c, string cle) => new()
    {
        ApiKey = cle, BaseUrl = c.Adresse, HttpClient = cloud, MaxRetries = 2, Timeout = TimeSpan.FromMinutes(5),
    };

    private async Task<string> DemanderClaude(ConnexionLlm c, string cle, string consigne, IReadOnlyList<(string Role, string Texte)> messages, JsonObject schema,
        CancellationToken arret)
    {
        var client = Claude(c, cle);
        var liste = messages.Select(m => m.Role == "assistant"
            ? (global::Anthropic.Models.Messages.MessageParam)new global::Anthropic.Models.Messages.AssistantMessageParam { Content = m.Texte }
            : new global::Anthropic.Models.Messages.UserMessageParam { Content = m.Texte }).ToList();
        var avecSchema = !_sansSchema.ContainsKey(CleService(c));
        for (var essai = 0; ; essai++)
        {
            var p = new global::Anthropic.Models.Messages.MessageCreateParams
            {
                Model = c.Modele,
                MaxTokens = ReponseMaxCloud,
                System = consigne,
                Messages = liste,
                // Réflexion au plus court (elle est facturée) ; pas de température : refusée depuis la version d'API du 21/10/2026.
                OutputConfig = new global::Anthropic.Models.Messages.OutputConfig
                {
                    Effort = global::Anthropic.Models.Messages.Effort.Low,
                    Format = avecSchema ? new global::Anthropic.Models.Messages.JsonOutputFormat { Schema = SchemaClaude(schema) } : null,
                },
            };
            try
            {
                var r = await client.Messages.Create(p, arret);
                depenses.Noter(c, r.Usage.InputTokens + (r.Usage.CacheCreationInputTokens ?? 0) + (r.Usage.CacheReadInputTokens ?? 0), r.Usage.OutputTokens);
                if (r.StopReason == global::Anthropic.Models.Messages.StopReason.Refusal)
                    throw new InvalidOperationException(L.T("llm.erreur.refus"));
                return string.Concat(r.Content.Select(b => b.Value).OfType<global::Anthropic.Models.Messages.TextBlock>().Select(t => t.Text));
            }
            catch (global::Anthropic.Exceptions.AnthropicBadRequestException e) when (avecSchema && essai == 0
                && ((e.ApiMessage ?? e.Message).Contains("schema", StringComparison.OrdinalIgnoreCase) || (e.ApiMessage ?? e.Message).Contains("output_config", StringComparison.OrdinalIgnoreCase)))
            {
                // Schéma refusé (mot-clé non pris en charge) : une fois sans, la consigne demande déjà du JSON.
                avecSchema = false;
                _sansSchema[CleService(c)] = true;
            }
            catch (global::Anthropic.Exceptions.AnthropicApiException e)
            {
                throw new HttpRequestException(L.T("llm.erreur.http", ("code", (int)e.StatusCode), ("texte", Masquer(Couper(e.ApiMessage ?? e.Message, 300), cle))));
            }
            catch (global::Anthropic.Exceptions.AnthropicException e) when (e is not global::Anthropic.Exceptions.AnthropicApiException)
            {
                throw new HttpRequestException(Masquer(e.Message, cle), e);
            }
        }
    }

    /// <summary>Le schéma au goût de l'API d'Anthropic : objets fermés (additionalProperties = false).</summary>
    public static Dictionary<string, JsonElement> SchemaClaude(JsonObject schema)
    {
        var copie = schema.DeepClone().AsObject();
        Fermer(copie);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(copie.ToJsonString())!;

        static void Fermer(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                if (o["type"]?.GetValueKind() == JsonValueKind.String && o["type"]!.GetValue<string>() == "object" && !o.ContainsKey("additionalProperties"))
                    o["additionalProperties"] = false;
                foreach (var (_, v) in o.ToList()) Fermer(v);
            }
            else if (n is JsonArray a)
                foreach (var v in a) Fermer(v);
        }
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
