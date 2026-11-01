using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur.Entite;

/// <summary>
/// Un acte demandé par le LLM, à plat : <see cref="Type"/> dit lequel, les autres champs servent selon le type (les autres sont
/// ignorés). Les noms suivent le schéma envoyé au LLM.
/// </summary>
public sealed record ActeLlm
{
    public string Type { get; init; } = "";
    public int? Humain { get; init; }
    public int[]? Humains { get; init; }
    public int? Communaute { get; init; }
    public int[]? Communautes { get; init; }
    public int? Cible { get; init; }
    public int[]? Cibles { get; init; }
    public string? Parole { get; init; }
    public string? Mode { get; init; }
    public int? Param { get; init; }
    public int? Direction { get; init; }
    public string? Geste { get; init; }
    public string? Maniere { get; init; }
    public string? Calamite { get; init; }
    public double? Force { get; init; }
    public double? Jours { get; init; }
    public int? Priere { get; init; }
    public bool? Signe { get; init; }
    public string? Catastrophe { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public double? Intensite { get; init; }
    public string? Operation { get; init; }
    public string? A { get; init; }
    public string? B { get; init; }
    public int? Lien { get; init; }
    public int? Espece { get; init; }
    public bool? Mutuel { get; init; }
    public bool? Passion { get; init; }

    // Entité libre (type « libre ») : son récit, s'il se voit et de qui, s'il fait du bien, les changements et les décrets.
    public string? Recit { get; init; }
    public bool? Visible { get; init; }
    public bool? Bienfait { get; init; }
    public int[]? Temoins { get; init; }
    public List<ChangementLibre>? Changements { get; init; }
    public List<DecretLibre>? Decrets { get; init; }
}

/// <summary>La réponse du LLM : pourquoi, les mots dits, et les actes (zéro ou plus).</summary>
/// <param name="Caractere">Le caractère qu'elle se donne (seulement quand elle n'en a pas encore ; vide sinon).</param>
public sealed record DecisionLlm(string Pensee, string Mots, List<ActeLlm> Actes, string Caractere = "");

/// <summary>Familles d'actes que le joueur permet au LLM.</summary>
[Flags]
public enum FamillesLlm
{
    Aucune = 0,
    Paroles = 1,
    Bienfaits = 2,
    Chatiments = 4,
    Catastrophes = 8,
    Souffles = 16,
    Toutes = Paroles | Bienfaits | Chatiments | Catastrophes | Souffles,
}

public static class Actes
{
    public const string Parler = "parler";
    public const string ParlerCommunaute = "parler_communaute";
    public const string Geste = "geste";
    public const string Catastrophe = "catastrophe";
    public const string Exaucer = "exaucer";
    public const string Refuser = "refuser";
    public const string SoufflerGeste = "souffler_geste";
    public const string SoufflerCombinaison = "souffler_combinaison";
    public const string Penchant = "penchant";
    public const string Rien = "rien";
    public const string Libre = "libre";
    public static readonly string[] Types = [Parler, ParlerCommunaute, Geste, Catastrophe, Exaucer, Refuser, SoufflerGeste, SoufflerCombinaison, Penchant, Rien];

    /// <summary>Types permis, avec l'acte libre quand la case « Entité libre » est cochée.</summary>
    public static string[] TypesPermis(bool libre) => libre ? [.. Types, Libre] : Types;

    /// <summary>Plus d'actes par réponse : au-delà, ils sont ignorés.</summary>
    public const int ParReponse = 4;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Schéma JSON imposé à la réponse (format structuré d'Ollama, response_format des API compatibles OpenAI).</summary>
    public static JsonObject Schema(bool libre = false)
    {
        static JsonObject T(string type) => new() { ["type"] = type };
        static JsonObject Entiers() => new() { ["type"] = "array", ["items"] = T("integer") };
        var acte = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. TypesPermis(libre).Select(t => (JsonNode)t)]) },
                ["humain"] = T("integer"), ["humains"] = Entiers(), ["communaute"] = T("integer"), ["communautes"] = Entiers(),
                ["cible"] = T("integer"), ["cibles"] = Entiers(), ["parole"] = T("string"), ["mode"] = T("string"), ["param"] = T("integer"),
                ["direction"] = T("integer"), ["geste"] = T("string"), ["maniere"] = T("string"), ["calamite"] = T("string"), ["force"] = T("number"),
                ["jours"] = T("number"), ["priere"] = T("integer"), ["signe"] = T("boolean"), ["catastrophe"] = T("string"), ["x"] = T("integer"),
                ["y"] = T("integer"), ["intensite"] = T("number"), ["operation"] = T("string"), ["a"] = T("string"), ["b"] = T("string"),
                ["lien"] = T("integer"), ["espece"] = T("integer"), ["mutuel"] = T("boolean"), ["passion"] = T("boolean"),
            },
            ["required"] = new JsonArray("type"),
        };
        if (libre)
        {
            var proprietes = (JsonObject)acte["properties"]!;
            proprietes["recit"] = T("string");
            proprietes["visible"] = T("boolean");
            proprietes["bienfait"] = T("boolean");
            proprietes["temoins"] = Entiers();
            proprietes["changements"] = new JsonObject
            {
                ["type"] = "array", ["maxItems"] = Simulation.Libre.ChangementsMax,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["quoi"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Simulation.Libre.Sortes.Select(s => (JsonNode)s)]) },
                        ["humain"] = T("integer"), ["communaute"] = T("integer"), ["autre"] = T("integer"), ["valeur"] = T("number"), ["texte"] = T("string"),
                        ["genre"] = T("string"), ["x"] = T("integer"), ["y"] = T("integer"), ["rayon"] = T("integer"),
                    },
                    ["required"] = new JsonArray("quoi"),
                },
            };
            proprietes["decrets"] = new JsonObject
            {
                ["type"] = "array", ["maxItems"] = Simulation.Libre.DecretsMax,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["loi"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Enum.GetNames<LoiDecret>().Select(s => (JsonNode)s)]) },
                        ["facteur"] = T("number"), ["communaute"] = T("integer"), ["jours"] = T("number"), ["raison"] = T("string"),
                    },
                    ["required"] = new JsonArray("loi", "facteur"),
                },
            };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["pensee"] = T("string"),
                ["mots"] = T("string"),
                ["caractere"] = T("string"),
                ["actes"] = new JsonObject { ["type"] = "array", ["items"] = acte, ["maxItems"] = ParReponse },
            },
            ["required"] = new JsonArray("pensee", "mots", "actes"),
        };
    }

    /// <summary>Lit la réponse du LLM ; lève <see cref="FormatException"/> (message pour le LLM) si elle est illisible.</summary>
    public static DecisionLlm Lire(string texte)
    {
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(ClientLlm.Nettoyer(texte), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
        }
        catch (JsonException e)
        {
            throw new FormatException(L.T("llm.erreur.json", ("detail", e.Message)), e);
        }
        if (o is null) throw new FormatException(L.T("llm.erreur.json", ("detail", "{}")));
        var actes = new List<ActeLlm>();
        if (o["actes"] is JsonArray a)
        {
            foreach (var n in a.Take(ParReponse))
            {
                try
                {
                    if (n?.Deserialize<ActeLlm>(Json) is { } x && !string.IsNullOrWhiteSpace(x.Type)) actes.Add(x);
                }
                catch (JsonException e)
                {
                    throw new FormatException(L.T("llm.erreur.json", ("detail", e.Message)), e);
                }
            }
        }
        return new DecisionLlm(Texte(o["pensee"]), Texte(o["mots"]), actes, Texte(o["caractere"]));
    }

    private static string Texte(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? ClientLlm.Couper(s.Trim(), 2000) : "";

    /// <summary>La famille d'un acte (pour les cases du joueur), ou Aucune pour « rien » et un type inconnu.</summary>
    public static FamillesLlm Famille(ActeLlm a) => a.Type switch
    {
        Parler or ParlerCommunaute or Penchant or Refuser => FamillesLlm.Paroles,
        Exaucer => FamillesLlm.Bienfaits,
        Geste => Enum.TryParse<GesteEntite>(a.Geste, out var g) && Enum.IsDefined(g) && Gestes.EstBienfait(g) ? FamillesLlm.Bienfaits : FamillesLlm.Chatiments,
        Catastrophe => FamillesLlm.Catastrophes,
        SoufflerGeste or SoufflerCombinaison => FamillesLlm.Souffles,
        _ => FamillesLlm.Aucune,
    };

    /// <summary>
    /// Vérifie un acte sans rien changer au monde (type connu, famille permise, ce qui ne se voit qu'avant d'appliquer). Lève
    /// <see cref="ArgumentException"/> avec la raison, pour le LLM.
    /// </summary>
    public static void Verifier(ActeLlm a, FamillesLlm permises, bool libre = false)
    {
        if (Array.IndexOf(TypesPermis(libre), a.Type) < 0) throw new ArgumentException(L.T(a.Type == Libre ? "llm.erreur.pasLibre" : "llm.erreur.type", ("type", a.Type)));
        if (a.Type == Libre && (a.Changements?.Count ?? 0) + (a.Decrets?.Count ?? 0) == 0) throw new ArgumentException(L.T("libre.erreur.vide"));
        var f = Famille(a);
        if (f != FamillesLlm.Aucune && (permises & f) == 0) throw new ArgumentException(L.T("llm.erreur.famille", ("type", a.Type)));
        if (a.Type == Parler && Enum.TryParse<Parole>(a.Parole, out var p) && Paroles.Collective(p))
            throw new ArgumentException(L.T("llm.erreur.paroleCollective", ("parole", a.Parole)));
    }

    /// <summary>
    /// Applique un acte par les mêmes pouvoirs que le joueur ; rend ce que les règles en ont fait, en mots. Lève
    /// <see cref="ArgumentException"/> si la demande est invalide (rien n'a changé). <paramref name="pieces"/> : les pièces
    /// numérotées montrées au LLM pour les combinaisons (P1, P2…).
    /// </summary>
    public static FaitEntite Appliquer(MondeSimule monde, ActeLlm a, FamillesLlm permises, IReadOnlyList<string> pieces, bool libre = false)
    {
        Verifier(a, permises, libre);
        switch (a.Type)
        {
            case Libre:
                return Libres.Appliquer(monde, a);
            case Parler:
                return Registre.Chacun(monde, Pouvoirs.Parler(monde, new DemandeParole(Humains(a), a.Communautes, a.Parole ?? "", a.Mode ?? "", a.Cible ?? 0, a.Param ?? a.Direction ?? 0)));
            case ParlerCommunaute:
            {
                var (membres, groupe) = Pouvoirs.ParlerACommunaute(monde,
                    new DemandeParoleCommunaute(a.Communaute ?? 0, a.Parole ?? "", a.Mode ?? "", a.Cibles ?? (a.Cible is { } c ? [c] : null), a.Direction ?? a.Param ?? 0));
                return new FaitEntite([string.Join(" ", groupe.Prepend(L.T("llm.resultat.membres", ("n", membres.Count))))], membres.Count > 0 ? 1 : 0, membres.Count);
            }
            case Geste:
                return Registre.Texte(Pouvoirs.Agir(monde, new DemandeGeste(a.Geste ?? "", a.Cible ?? a.Humain ?? a.Communaute ?? 0, a.Maniere ?? nameof(ManiereGeste.Cachee),
                    a.Param ?? 0, a.Calamite, a.Force ?? 0, a.Jours ?? 0)), true);
            case Catastrophe:
            {
                var id = Pouvoirs.Declencher(monde, new DemandeCatastrophe(a.Catastrophe ?? "", a.X ?? -1, a.Y ?? -1, a.Intensite ?? Intensite(a.Catastrophe)));
                return Registre.Texte(id > 0 ? L.T("llm.resultat.catastrophe") : L.T("llm.resultat.catastropheImpossible"), id > 0);
            }
            case Exaucer:
                return Registre.Texte(Pouvoirs.Exaucer(monde, a.Priere ?? 0), true);
            case Refuser:
                return Registre.Texte(Pouvoirs.Refuser(monde, a.Priere ?? 0, a.Signe ?? false), true);
            case SoufflerGeste:
            {
                var (texte, reussi) = Pouvoirs.SoufflerFait(monde, new DemandeIntuition(a.Humain ?? a.Cible ?? 0, a.Geste ?? "", a.A ?? "", a.B, a.Espece ?? 0));
                return Registre.Texte(texte, reussi);
            }
            case SoufflerCombinaison:
            {
                var (texte, reussi) = Pouvoirs.SoufflerCombinaisonFait(monde, new DemandeCombinaison(a.Humain ?? a.Cible ?? 0, a.Operation ?? "", Piece(a.A, pieces) ?? "",
                    Piece(a.B, pieces), a.Lien ?? 0));
                return Registre.Texte(texte, reussi);
            }
            case Penchant:
                return Registre.Chacun(monde, Pouvoirs.Inspirer(monde, new DemandePenchant(Humains(a), a.Communautes, a.Cible ?? 0, a.Mutuel ?? false, a.Passion ?? false)));
            default:
                return new FaitEntite([L.T("llm.resultat.rien")], 0, 0);
        }
    }

    private static int[]? Humains(ActeLlm a) => a.Humains is { Length: > 0 } h ? h : a.Humain is { } x ? [x] : null;

    /// <summary>« P3 » (ou « 3 ») désigne la troisième pièce montrée ; une écriture de pièce est gardée telle quelle.</summary>
    public static string? Piece(string? s, IReadOnlyList<string> pieces)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.StartsWith('P') || t.StartsWith('p')) t = t[1..];
        return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 1 && i <= pieces.Count ? pieces[i - 1] : s.Trim();
    }

    /// <summary>Catastrophes que l'Entité peut provoquer : clé, intensité proposée (0 : sans mesure).</summary>
    public static readonly (string Cle, double Intensite)[] Provoquables =
    [
        ("seisme", 6.5), ("eruption", 3), ("tsunami", 8), ("crue", 2), ("incendie", 0), ("cyclone", 3), ("froid", 8), ("canicule", 7),
        ("epidemie", 0), ("eclipse", 0), ("comete", 0), ("aurore", 0), ("bolide", 0), ("impact", 10),
    ];

    private static double Intensite(string? cle) => Provoquables.FirstOrDefault(p => p.Cle == cle).Intensite;
}
