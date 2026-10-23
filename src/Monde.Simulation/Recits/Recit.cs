using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Monde.Simulation.Humains;

namespace Monde.Simulation.Recits;

/// <summary>Un humain cité dans un récit : son numéro (pour le lien vers sa fiche), son nom et son genre (pour l'accord).</summary>
public readonly record struct Personne(int Id, string Nom, bool Femme)
{
    public Personne(Humain h) : this(h.Id, h.Nom, h.Sexe == Sexe.Femme) { }
}

/// <summary>Une communauté citée : son numéro et son nom français au moment des faits (le nom peut changer ensuite).</summary>
public readonly record struct RefCommunaute(int Id, string Nom)
{
    public RefCommunaute(Communaute c) : this(c.Id, c.Nom) { }
}

/// <summary>Plusieurs humains cités ensemble : « Ana et Bor ». Accordé au féminin s'il n'y a que des femmes.</summary>
public sealed class Groupe(IReadOnlyList<Personne> membres) : IEquatable<Groupe>
{
    public IReadOnlyList<Personne> Membres { get; } = membres;

    public Groupe(IEnumerable<Humain> humains) : this(humains.Select(h => new Personne(h)).ToList()) { }

    public bool Feminin => Membres.Count > 0 && Membres.All(m => m.Femme);

    public bool Equals(Groupe? autre) => autre is not null && Membres.SequenceEqual(autre.Membres);

    public override bool Equals(object? obj) => Equals(obj as Groupe);

    public override int GetHashCode() => Membres.Count;
}

/// <summary>Une énumération : « a », « a et b », « a, b et c » (chaque élément est écrit à son tour).</summary>
/// <remarks><paramref name="virgules"/> : seulement des virgules, sans « et » (« a bu, a dormi »).</remarks>
public sealed class Liste(IReadOnlyList<object?> elements, bool virgules = false) : IEquatable<Liste>
{
    public IReadOnlyList<object?> Elements { get; } = elements;
    public bool Virgules { get; } = virgules;

    public bool Equals(Liste? autre) => autre is not null && Virgules == autre.Virgules && Elements.SequenceEqual(autre.Elements);

    public override bool Equals(object? obj) => Equals(obj as Liste);

    public override int GetHashCode() => Elements.Count;
}

/// <summary>
/// Ce qui est raconté, sans être encore écrit : une clé du fichier de langue et ses paramètres (humains, communautés,
/// nombres, mots inventés, autres récits). La phrase s'écrit au moment de l'affichage, dans la langue de l'observateur.
/// <see cref="Texte"/> l'écrit en français. Deux récits sont égaux s'ils ont la même clé et les mêmes paramètres.
/// </summary>
public sealed class Recit : IEquatable<Recit>
{
    public string Cle { get; }
    public (string Nom, object? Valeur)[] Args { get; }

    public Recit(string cle, params (string Nom, object? Valeur)[] args)
    {
        Cle = cle;
        for (var i = 0; i < args.Length; i++) args[i].Valeur = Normaliser(args[i].Valeur);
        Args = args;
    }

    /// <summary>Un humain devient une <see cref="Personne"/>, une communauté un <see cref="RefCommunaute"/>, un entier un long.</summary>
    private static object? Normaliser(object? v) => v switch
    {
        Humain h => new Personne(h),
        Communaute c => new RefCommunaute(c),
        int i => (long)i,
        short s => (long)s,
        byte b => (long)b,
        float f => (double)f,
        _ => v,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Recit> Seuls = new(StringComparer.Ordinal);

    /// <summary>Le récit sans paramètre de cette clé, créé une seule fois (les pensées courantes en font sans cesse).</summary>
    public static Recit Seul(string cle) => Seuls.GetOrAdd(cle, static c => new Recit(c));

    /// <summary>Le récit écrit en français.</summary>
    public string Texte => Catalogues.Fr.Rediger(this);

    public override string ToString() => Texte;

    public object? this[string nom]
    {
        get
        {
            foreach (var (n, v) in Args) if (n == nom) return v;
            return null;
        }
    }

    /// <summary>Même clé et mêmes paramètres, sauf <paramref name="sauf"/> (« vers » : la même pensée, dans une autre direction).</summary>
    public bool MemeSens(Recit autre, string sauf)
    {
        if (Cle != autre.Cle) return false;
        int i = 0, j = 0;
        while (true)
        {
            while (i < Args.Length && Args[i].Nom == sauf) i++;
            while (j < autre.Args.Length && autre.Args[j].Nom == sauf) j++;
            if (i == Args.Length || j == autre.Args.Length) return i == Args.Length && j == autre.Args.Length;
            if (Args[i].Nom != autre.Args[j].Nom || !Equals(Args[i].Valeur, autre.Args[j].Valeur)) return false;
            i++;
            j++;
        }
    }

    public bool Equals(Recit? autre)
    {
        if (autre is null) return false;
        if (ReferenceEquals(this, autre)) return true;
        if (Cle != autre.Cle || Args.Length != autre.Args.Length) return false;
        for (var i = 0; i < Args.Length; i++)
            if (Args[i].Nom != autre.Args[i].Nom || !Equals(Args[i].Valeur, autre.Args[i].Valeur)) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Recit);

    public override int GetHashCode() => HashCode.Combine(Cle, Args.Length);

    // ───────────── Enregistrement ─────────────

    private const char Marque = '\u0001';

    /// <summary>
    /// Le récit dans une seule chaîne (clé et paramètres), pour un texte gardé dans l'état du monde (raison d'un exil…)
    /// qui doit s'écrire plus tard dans la langue de l'observateur. Voir <see cref="Decoder"/>.
    /// </summary>
    public string Encoder() => $"{Marque}{Cle}{Marque}{ParametresJson()}";

    /// <summary>Le récit d'une chaîne faite par <see cref="Encoder"/>, ou null si c'est une phrase déjà écrite.</summary>
    public static Recit? Decoder(string? s)
    {
        if (string.IsNullOrEmpty(s) || s[0] != Marque) return null;
        var i = s.IndexOf(Marque, 1);
        return i < 0 ? null : Lire(s[1..i], s[(i + 1)..]);
    }

    /// <summary>Une chaîne gardée (récit encodé ou phrase déjà écrite), en français.</summary>
    public static string EnFrancais(string? s) => Decoder(s)?.Texte ?? s ?? "";

    /// <summary>
    /// Les paramètres en JSON, pour la sauvegarde : un humain s'écrit [numéro, "nom", 0|1], une communauté
    /// {"c": numéro, "n": "nom"}, un récit {"k": "clé", "a": {…}}, un groupe {"g": [humains]}, un réel {"r": valeur}.
    /// </summary>
    public string ParametresJson()
    {
        if (Args.Length == 0) return "";
        using var flux = new MemoryStream();
        using (var w = new Utf8JsonWriter(flux, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            EcrireArgs(w, Args);
        return Encoding.UTF8.GetString(flux.ToArray());
    }

    private static void EcrireArgs(Utf8JsonWriter w, (string Nom, object? Valeur)[] args)
    {
        w.WriteStartObject();
        foreach (var (nom, v) in args)
        {
            w.WritePropertyName(nom);
            EcrireValeur(w, v);
        }
        w.WriteEndObject();
    }

    private static void EcrireValeur(Utf8JsonWriter w, object? v, Func<Recit, int>? forme = null)
    {
        switch (v)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case long l: w.WriteNumberValue(l); break;
            case double d:
                w.WriteStartObject();
                w.WriteNumber("r", d);
                w.WriteEndObject();
                break;
            case Personne p: EcrirePersonne(w, p, forme is not null); break;
            case RefCommunaute c:
                w.WriteStartObject();
                w.WriteNumber("c", c.Id);
                w.WriteString("n", c.Nom);
                w.WriteEndObject();
                break;
            case Groupe g:
                w.WriteStartObject();
                w.WriteStartArray("g");
                foreach (var p in g.Membres) EcrirePersonne(w, p, forme is not null);
                w.WriteEndArray();
                w.WriteEndObject();
                break;
            case Liste l:
                w.WriteStartObject();
                w.WriteStartArray("l");
                foreach (var x in l.Elements) EcrireValeur(w, x, forme);
                w.WriteEndArray();
                if (l.Virgules) w.WriteBoolean("v", true);
                w.WriteEndObject();
                break;
            case Recit r when forme is not null:
                w.WriteStartObject();
                w.WriteNumber("f", forme(r));
                if (r.Args.Length > 0)
                {
                    w.WritePropertyName("a");
                    EcrireValeurs(w, r.Args, forme);
                }
                w.WriteEndObject();
                break;
            case Recit r:
                w.WriteStartObject();
                w.WriteString("k", r.Cle);
                if (r.Args.Length > 0)
                {
                    w.WritePropertyName("a");
                    EcrireArgs(w, r.Args);
                }
                w.WriteEndObject();
                break;
            default: w.WriteStringValue(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
        }
    }

    private static void EcrirePersonne(Utf8JsonWriter w, Personne p, bool compact)
    {
        w.WriteStartArray();
        w.WriteNumberValue(p.Id);
        if (!compact)
        {
            w.WriteStringValue(p.Nom);
            w.WriteNumberValue(p.Femme ? 1 : 0);
        }
        w.WriteEndArray();
    }

    // ───────────── Enregistrement compact (journal, pensées) ─────────────

    /// <summary>Les noms des paramètres, dans l'ordre (« h,quoi,x,y »).</summary>
    public string Noms
    {
        get
        {
            if (Args.Length == 0) return "";
            // Une clé garde presque toujours les mêmes noms : la chaîne déjà faite resert, sans rien allouer.
            if (NomsParCle.TryGetValue(Cle, out var connus) && connus.Noms.Length == Args.Length)
            {
                var memes = true;
                for (var i = 0; i < Args.Length && memes; i++) memes = connus.Noms[i] == Args[i].Nom;
                if (memes) return connus.Texte;
            }
            var noms = Args.Select(a => a.Nom).ToArray();
            var texte = string.Join(',', noms);
            NomsParCle[Cle] = (noms, texte);
            return texte;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string[] Noms, string Texte)> NomsParCle = new(StringComparer.Ordinal);

    /// <summary>
    /// Les valeurs seules, dans l'ordre, en tableau JSON (vide s'il n'y en a pas) : la clé et les noms des paramètres
    /// sont gardés une fois pour toutes dans une « forme », dont <paramref name="forme"/> donne le numéro. Un récit cité
    /// s'écrit {"f": forme, "a": [valeurs]} ; les autres valeurs comme dans <see cref="ParametresJson"/>.
    /// </summary>
    /// <remarks>Un humain s'y écrit par son seul numéro, [12] : son nom et son sexe ne changent jamais, la base les a.</remarks>
    public string Valeurs(Func<Recit, int> forme)
    {
        if (Args.Length == 0) return "";
        var tampon = _tampon ??= new ArrayBufferWriter<byte>(256);
        tampon.ResetWrittenCount();
        var w = _ecrivain ??= new Utf8JsonWriter(tampon, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.Reset(tampon);
        EcrireValeurs(w, Args, forme);
        w.Flush();
        return Encoding.UTF8.GetString(tampon.WrittenSpan);
    }

    // Un tampon et un écrivain par fil, réutilisés d'une ligne à l'autre : l'enregistrement en écrit des centaines de milliers.
    [ThreadStatic] private static ArrayBufferWriter<byte>? _tampon;
    [ThreadStatic] private static Utf8JsonWriter? _ecrivain;

    private static void EcrireValeurs(Utf8JsonWriter w, (string Nom, object? Valeur)[] args, Func<Recit, int> forme)
    {
        w.WriteStartArray();
        foreach (var (_, v) in args) EcrireValeur(w, v, forme);
        w.WriteEndArray();
    }

    /// <summary>Relit un récit enregistré par <see cref="Valeurs"/> : sa clé et ses noms (sa forme), ses valeurs.</summary>
    public static Recit LireValeurs(string cle, string noms, string? json, Func<int, (string Cle, string Noms)> formes, Func<int, Personne> humain)
    {
        if (string.IsNullOrEmpty(json)) return new Recit(cle);
        using var doc = JsonDocument.Parse(json);
        return Assembler(cle, noms, doc.RootElement, new Compact(formes, humain));
    }

    /// <summary>Pour relire l'enregistrement compact : les formes par numéro, les humains par numéro.</summary>
    private sealed record Compact(Func<int, (string Cle, string Noms)> Formes, Func<int, Personne> Humain);

    private static Recit Assembler(string cle, string noms, JsonElement valeurs, Compact formes)
    {
        var n = noms.Length == 0 ? [] : noms.Split(',');
        var args = new (string, object?)[n.Length];
        var i = 0;
        foreach (var v in valeurs.EnumerateArray())
        {
            if (i >= n.Length) break;
            args[i] = (n[i], LireValeur(v, formes));
            i++;
        }
        return new Recit(cle, i == n.Length ? args : args[..i]);
    }

    /// <summary>Relit un récit enregistré (clé et paramètres en JSON).</summary>
    public static Recit Lire(string cle, string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Recit(cle);
        using var doc = JsonDocument.Parse(json);
        return new Recit(cle, LireArgs(doc.RootElement));
    }

    private static (string, object?)[] LireArgs(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Select(p => (p.Name, LireValeur(p.Value))).ToArray() : [];

    private static object? LireValeur(JsonElement e, Compact? formes = null)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: return e.GetString();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return e.TryGetInt64(out var l) ? (object)l : e.GetDouble();
            case JsonValueKind.Array: return LirePersonne(e, formes);
            case JsonValueKind.Object:
                if (formes is not null && e.TryGetProperty("f", out var f))
                {
                    var (cle, noms) = formes.Formes(f.GetInt32());
                    return e.TryGetProperty("a", out var va) ? Assembler(cle, noms, va, formes) : new Recit(cle);
                }
                if (e.TryGetProperty("k", out var k)) return new Recit(k.GetString() ?? "", e.TryGetProperty("a", out var a) ? LireArgs(a) : []);
                if (e.TryGetProperty("r", out var r)) return r.GetDouble();
                if (e.TryGetProperty("c", out var c)) return new RefCommunaute(c.GetInt32(), e.TryGetProperty("n", out var n) ? n.GetString() ?? "" : "");
                if (e.TryGetProperty("l", out var li)) return new Liste(li.EnumerateArray().Select(x => LireValeur(x, formes)).ToList(), e.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.True);
                if (e.TryGetProperty("g", out var g)) return new Groupe(g.EnumerateArray().Select(x => LirePersonne(x, formes)).ToList());
                return null;
            default: return null;
        }
    }

    private static Personne LirePersonne(JsonElement e, Compact? formes)
    {
        var x = e.EnumerateArray().ToArray();
        if (x.Length == 1 && formes is not null) return formes.Humain(x[0].GetInt32());
        return new Personne(x.Length > 0 ? x[0].GetInt32() : 0, x.Length > 1 ? x[1].GetString() ?? "" : "", x.Length > 2 && x[2].GetInt32() == 1);
    }
}
