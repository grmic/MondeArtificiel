using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Monde.Simulation.Recits;

/// <summary>
/// Une langue de l'observateur, lue dans <c>langues/xx.json</c>. Chaque clé donne un texte, ou ses formes selon le
/// pluriel (one, other…, choisies par le paramètre <c>n</c>) ou le genre (m, f, choisies par le paramètre <c>g</c>).
/// L'entrée <c>_langue</c> donne le code, le nom de la langue dans sa propre langue et l'écriture des nombres ;
/// l'entrée <c>_grammaire</c>, les élisions (« de Ana » devient « d'Ana »), les contractions (« de le feu » devient
/// « du feu ») et la façon de lier une liste de noms.
/// <para>
/// Dans un texte, <c>{x}</c> écrit le paramètre x, <c>{^x}</c> l'écrit avec une majuscule, <c>{_x}</c> avec une minuscule, et <c>{x:a|b}</c> choisit
/// une forme d'après x : selon son genre si x est un humain ou un groupe (masculin|féminin), selon le pluriel si x est
/// un nombre (dans l'ordre des formes de la langue, « # » valant le nombre), selon sa valeur si x est vrai ou faux
/// (faux|vrai), selon le genre du nom qu'il écrit si x est un récit (entrée <c>clé.g</c> : « f » pour le féminin).
/// </para>
/// </summary>
public sealed partial class Langue
{
    public required string Code { get; init; }
    public required string Nom { get; init; }
    public required string Locale { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Entrees { get; init; }
    public required NumberFormatInfo Nombres { get; init; }

    /// <summary>Texte brut du fichier, servi tel quel au navigateur.</summary>
    public required string Json { get; init; }

    /// <summary>Langue dont on prend le texte d'une clé absente (le français), ou null pour le français lui-même.</summary>
    public Langue? Repli { get; set; }

    /// <summary>Formes du pluriel, dans l'ordre où les écrit un choix <c>{n:…|…}</c>.</summary>
    public IReadOnlyList<string> Pluriels { get; init; } = ["one", "other"];

    /// <summary>Mot qui s'élide devant une voyelle → sa forme élidée (« de » → « d' »).</summary>
    public IReadOnlyDictionary<string, string> Elisions { get; init; } = new Dictionary<string, string>();

    /// <summary>Deux mots qui se contractent quand une valeur suit le premier : « de le » → « du », « de des » → « de ».</summary>
    public IReadOnlyDictionary<string, string> Contractions { get; init; } = new Dictionary<string, string>();

    /// <summary>Liaison des deux derniers noms d'une liste (« et », « and »).</summary>
    public string Et { get; init; } = "et";

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$")]
    public static partial Regex CodeValide();

    public static Langue Lire(string chemin)
    {
        var json = File.ReadAllText(chemin);
        using var document = JsonDocument.Parse(json);
        var entrees = new Dictionary<string, JsonElement>();
        foreach (var p in document.RootElement.EnumerateObject()) entrees[p.Name] = p.Value.Clone();
        var code = Path.GetFileNameWithoutExtension(chemin);
        if (!CodeValide().IsMatch(code)) throw new InvalidDataException($"Nom de fichier de langue invalide : {chemin}");
        var entete = entrees.GetValueOrDefault("_langue");
        string Champ(string nom, string defaut) =>
            entete.ValueKind == JsonValueKind.Object && entete.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()! : defaut;
        var grammaire = entrees.GetValueOrDefault("_grammaire");
        var elisions = new Dictionary<string, string>(StringComparer.Ordinal);
        var contractions = new Dictionary<string, string>(StringComparer.Ordinal);
        var pluriels = new List<string>();
        var et = code == "fr" ? "et" : "and";
        if (grammaire.ValueKind == JsonValueKind.Object)
        {
            if (grammaire.TryGetProperty("elisions", out var e) && e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) elisions[p.Name] = p.Value.GetString()!;
            if (grammaire.TryGetProperty("contractions", out var c) && c.ValueKind == JsonValueKind.Object)
                foreach (var p in c.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) contractions[p.Name] = p.Value.GetString()!;
            if (grammaire.TryGetProperty("pluriels", out var pl) && pl.ValueKind == JsonValueKind.Array)
                foreach (var x in pl.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String) pluriels.Add(x.GetString()!);
            if (grammaire.TryGetProperty("et", out var l) && l.ValueKind == JsonValueKind.String) et = l.GetString()!;
        }
        return new Langue
        {
            Code = code,
            Nom = Champ("nom", code),
            Locale = Champ("locale", code),
            Entrees = entrees,
            // Écriture des nombres donnée par le fichier : elle ne dépend pas des cultures installées sur la machine.
            Nombres = new NumberFormatInfo
            {
                NumberDecimalSeparator = Champ("decimale", code == "fr" ? "," : "."),
                NumberGroupSeparator = Champ("milliers", code == "fr" ? " " : ","),
            },
            Json = json,
            Elisions = elisions,
            Contractions = contractions,
            Pluriels = pluriels.Count > 0 ? pluriels : ["one", "other"],
            Et = et,
        };
    }

    /// <summary>Forme du pluriel (règles CLDR des langues courantes) : en français, 0 et 1 prennent le singulier.</summary>
    public string FormePluriel(double n)
    {
        var langue = Code.Split('-')[0];
        return langue switch
        {
            "fr" or "pt" => n is >= 0 and < 2 ? "one" : "other",
            "ja" or "zh" or "ko" or "vi" or "th" or "id" => "other",
            _ => n == 1 ? "one" : "other",
        };
    }

    /// <summary>Culture des tris alphabétiques (celle de la locale du fichier, ou la culture neutre si la machine ne la connaît pas).</summary>
    public CultureInfo Culture => culture ??= TrouverCulture(Locale);

    private CultureInfo? culture;

    private static CultureInfo TrouverCulture(string locale)
    {
        try { return CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    public string Nombre(double v, int decimales = 1) =>
        v.ToString(decimales > 0 ? "#,##0." + new string('#', decimales) : "#,##0", Nombres);

    /// <summary>Le récit écrit dans cette langue.</summary>
    public string Rediger(Recit recit) => Texte(recit.Cle, recit.Args);

    /// <summary>
    /// Texte de la clé, ses accolades remplacées par les paramètres (voir le résumé de la classe). Une clé absente
    /// prend le texte de la langue de repli ; absente partout, la clé elle-même s'écrit.
    /// </summary>
    public string Texte(string cle, ReadOnlySpan<(string Nom, object? Valeur)> parametres)
    {
        var modele = Choisir(this, cle, parametres);
        if (modele is null && Repli is { } repli) modele = Choisir(repli, cle, parametres);
        if (modele is null) return cle;
        if (modele.IndexOf('{') < 0) return modele;
        return Remplir(modele, parametres);
    }

    private string Remplir(string modele, ReadOnlySpan<(string Nom, object? Valeur)> parametres)
    {
        var texte = new StringBuilder(modele.Length + 16);
        var i = 0;
        while (i < modele.Length)
        {
            var debut = modele.IndexOf('{', i);
            var fin = debut < 0 ? -1 : modele.IndexOf('}', debut + 1);
            if (debut < 0 || fin < 0)
            {
                texte.Append(modele, i, modele.Length - i);
                break;
            }
            texte.Append(modele, i, debut - i);
            var dedans = modele.AsSpan(debut + 1, fin - debut - 1);
            var majuscule = dedans.Length > 0 && dedans[0] == '^';
            var minuscule = dedans.Length > 0 && dedans[0] == '_';
            if (majuscule || minuscule) dedans = dedans[1..];
            var deuxPoints = dedans.IndexOf(':');
            var nom = deuxPoints < 0 ? dedans : dedans[..deuxPoints];
            var trouve = false;
            foreach (var (n, v) in parametres)
            {
                if (!nom.SequenceEqual(n)) continue;
                trouve = true;
                var morceau = deuxPoints < 0 ? Ecrire(v) : Forme(v, dedans[(deuxPoints + 1)..]);
                if (majuscule) morceau = Majuscule(morceau);
                else if (minuscule) morceau = Minuscule(morceau);
                if (deuxPoints < 0)
                {
                    morceau = Contracter(texte, morceau);
                    Elider(texte, morceau);
                }
                texte.Append(morceau);
                break;
            }
            if (!trouve) texte.Append(modele, debut, fin - debut + 1);
            i = fin + 1;
        }
        return texte.ToString();
    }

    /// <summary>Forme choisie dans « a|b|… » d'après la valeur (genre, pluriel ou vrai/faux).</summary>
    private string Forme(object? v, ReadOnlySpan<char> formes)
    {
        var liste = formes.ToString().Split('|');
        int k;
        switch (v)
        {
            // Une communauté avec un seul mot : une forme de son nom (« {c:dans} » : « dans la communauté d'Ana », « among the Tako people »).
            case RefCommunaute c when liste.Length == 1: return Communaute(c.Nom, liste[0]);
            case Personne p: k = p.Femme ? 1 : 0; break;
            case Groupe g: k = g.Feminin ? 1 : 0; break;
            case bool b: k = b ? 1 : 0; break;
            case Recit r: k = Texte(r.Cle + ".g", []) == "f" ? 1 : 0; break;
            case string s: k = s == "f" ? 1 : 0; break;
            case long or int or double:
                {
                    var n = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                    var cat = FormePluriel(n);
                    k = 0;
                    var trouve = false;
                    for (var j = 0; j < Pluriels.Count; j++) if (Pluriels[j] == cat) { k = j; trouve = true; break; }
                    if (!trouve) k = Pluriels.Count - 1;
                    var forme = liste[Math.Min(k, liste.Length - 1)];
                    return forme.Contains('#') ? forme.Replace("#", Ecrire(v), StringComparison.Ordinal) : forme;
                }
            default: k = 0; break;
        }
        return liste[Math.Min(k, liste.Length - 1)];
    }

    /// <summary>Élision devant une valeur qui commence par une voyelle : « de Ana » devient « d'Ana ».</summary>
    private void Elider(StringBuilder texte, string morceau)
    {
        if (Elisions.Count == 0 || morceau.Length == 0 || !Voyelle(morceau[0]) || texte.Length < 2 || texte[^1] != ' ') return;
        var fin = texte.Length - 1;
        var debut = fin;
        while (debut > 0 && char.IsLetter(texte[debut - 1])) debut--;
        if (debut == fin) return;
        var mot = texte.ToString(debut, fin - debut);
        if (!Elisions.TryGetValue(mot, out var elide)) return;
        texte.Remove(debut, fin - debut + 1).Append(elide);
    }

    /// <summary>
    /// Contraction du mot qui précède avec le premier mot de la valeur : « de » suivi de « le feu » donne « du feu »,
    /// « de » suivi de « des fibres » donne « de fibres ». Le texte est raccourci du mot, la valeur rendue changée.
    /// </summary>
    private string Contracter(StringBuilder texte, string morceau)
    {
        if (Contractions.Count == 0 || morceau.Length == 0 || texte.Length < 2 || texte[^1] != ' ') return morceau;
        var espace = morceau.IndexOf(' ');
        if (espace <= 0) return morceau;
        var fin = texte.Length - 1;
        var debut = fin;
        while (debut > 0 && char.IsLetter(texte[debut - 1])) debut--;
        if (debut == fin) return morceau;
        if (!Contractions.TryGetValue(string.Concat(texte.ToString(debut, fin - debut), " ", morceau.AsSpan(0, espace)), out var c)) return morceau;
        texte.Remove(debut, fin - debut + 1);
        return string.Concat(c, morceau.AsSpan(espace));
    }

    private static bool Voyelle(char c) => "AEIOUÀÂÉÈÊËÎÏÔÛÙaeiouàâéèêëîïôûù".Contains(c);

    public static string Majuscule(string s) => s.Length == 0 || char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Minuscule(string s) => s.Length == 0 || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>Une valeur écrite dans cette langue.</summary>
    public string Ecrire(object? v) => v switch
    {
        null => "",
        string s => s,
        Personne p => p.Nom,
        Recit r => Rediger(r),
        RefCommunaute c => Communaute(c.Nom, phrase: true),
        Groupe g => Lister(g.Membres.Select(m => m.Nom).ToList()),
        Liste l => l.Virgules ? string.Join(", ", l.Elements.Select(Ecrire)) : Lister(l.Elements.Select(Ecrire).ToList()),
        int or long or short or byte => Nombre(Convert.ToDouble(v, CultureInfo.InvariantCulture), 0),
        Decimale x => x.Valeur.ToString(x.Chiffres > 0 ? "0." + new string('0', x.Chiffres) : "0", Nombres),
        double d => Nombre(d, 2),
        float f => Nombre(f, 2),
        decimal m => Nombre((double)m, 2),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>« Ana », « Ana et Bor », « Ana, Bor et Eli ».</summary>
    public string Lister(IReadOnlyList<string> noms) => noms.Count switch
    {
        0 => "",
        1 => noms[0],
        _ => string.Join(", ", noms.Take(noms.Count - 1)) + $" {Et} " + noms[^1],
    };

    [GeneratedRegex(@"^Communauté de (.+?)(?: \((\d+)\))?$")]
    private static partial Regex NomDe();

    [GeneratedRegex(@"^Communauté (.+)$")]
    private static partial Regex NomPropre();

    /// <summary>
    /// Nom d'une communauté dans cette langue, d'après son nom français (« Communauté de Ana (2) », « Communauté Tako ») :
    /// pour une liste (« Communauté d'Ana », « Tako people ») ou dans une phrase (« la communauté d'Ana », « the Tako people »).
    /// </summary>
    public string Communaute(string nomFrancais, bool phrase) => Communaute(nomFrancais, phrase ? "phrase" : "nom");

    /// <summary>Une forme du nom d'une communauté (clés <c>communaute.{forme}.de</c> et <c>communaute.{forme}.propre</c>).</summary>
    public string Communaute(string nomFrancais, string forme)
    {
        if (NomDe().Match(nomFrancais) is { Success: true } de)
        {
            var texte = Texte($"communaute.{forme}.de", [("figure", de.Groups[1].Value)]);
            return de.Groups[2].Success ? $"{texte} ({de.Groups[2].Value})" : texte;
        }
        if (NomPropre().Match(nomFrancais) is { Success: true } p) return Texte($"communaute.{forme}.propre", [("mot", p.Groups[1].Value)]);
        return nomFrancais;
    }

    private static string? Choisir(Langue langue, string cle, ReadOnlySpan<(string Nom, object? Valeur)> parametres)
    {
        if (!langue.Entrees.TryGetValue(cle, out var e)) return null;
        for (var profondeur = 0; profondeur < 3 && e.ValueKind == JsonValueKind.Object; profondeur++)
        {
            var pluriel = e.TryGetProperty("other", out _) || e.TryGetProperty("one", out _);
            string forme;
            if (pluriel)
            {
                var n = 0.0;
                foreach (var (nom, v) in parametres)
                    if (nom == "n" && v is not null) n = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                // « zero », s'il est écrit, vaut pour 0 dans toutes les langues (« Aucun volcan actif. »).
                forme = n == 0 && e.TryGetProperty("zero", out _) ? "zero" : langue.FormePluriel(n);
                if (!e.TryGetProperty(forme, out _)) forme = "other";
            }
            else
            {
                forme = "m";
                foreach (var (nom, v) in parametres)
                {
                    if (nom != "g") continue;
                    forme = v switch { string g => g, Personne p => p.Femme ? "f" : "m", Groupe gr => gr.Feminin ? "f" : "m", _ => "m" };
                }
                if (!e.TryGetProperty(forme, out _)) forme = "m";
            }
            if (!e.TryGetProperty(forme, out var suivante)) return null;
            e = suivante;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }

    /// <summary>Le texte existe-t-il (dans cette langue ou dans sa langue de repli) ?</summary>
    public bool Existe(string cle) => Entrees.ContainsKey(cle) || (Repli?.Entrees.ContainsKey(cle) ?? false);
}

/// <summary>
/// Les langues du dossier <c>langues</c> (à côté de l'exécutable, ou, en développement, dans le dépôt ; la variable
/// d'environnement <c>MONDE_LANGUES</c> peut le désigner). Le français est toujours là : c'est la langue de repli.
/// </summary>
public static class Catalogues
{
    public const string Francais = "fr";
    private static readonly Lock Verrou = new();
    private static IReadOnlyDictionary<string, Langue>? _langues;

    public static IReadOnlyDictionary<string, Langue> Toutes
    {
        get
        {
            if (_langues is { } l) return l;
            lock (Verrou) return _langues ??= Charger(TrouverDossier());
        }
    }

    public static Langue Fr => Toutes[Francais];

    private static string TrouverDossier()
    {
        if (Environment.GetEnvironmentVariable("MONDE_LANGUES") is { Length: > 0 } impose && File.Exists(Path.Combine(impose, "fr.json"))) return impose;
        foreach (var depart in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var d = new DirectoryInfo(depart); d is not null; d = d.Parent)
            {
                var dossier = Path.Combine(d.FullName, "langues");
                if (File.Exists(Path.Combine(dossier, "fr.json"))) return dossier;
            }
        }
        throw new FileNotFoundException("Dossier « langues » introuvable (il doit contenir fr.json).");
    }

    private static Dictionary<string, Langue> Charger(string dossier)
    {
        var langues = new Dictionary<string, Langue>();
        foreach (var fichier in Directory.EnumerateFiles(dossier, "*.json").Order(StringComparer.Ordinal))
        {
            var code = Path.GetFileNameWithoutExtension(fichier);
            if (!Langue.CodeValide().IsMatch(code)) continue;
            langues[code] = Langue.Lire(fichier);
        }
        if (!langues.TryGetValue(Francais, out var fr)) throw new FileNotFoundException("Le fichier de langue fr.json manque.", Path.Combine(dossier, "fr.json"));
        foreach (var l in langues.Values) if (l != fr) l.Repli = fr;
        return langues;
    }
}
