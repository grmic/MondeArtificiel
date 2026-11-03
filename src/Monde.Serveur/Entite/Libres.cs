using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur.Entite;

/// <summary>De quoi défaire un acte libre : ses changements et les décrets qu'il a pris.</summary>
public sealed record DefaireLibre(List<ChangementFait> Faits, List<int> Decrets);

/// <summary>Ce que répond le juge de cohérence.</summary>
public sealed record VerdictJuge(bool Coherent, string Raison);

/// <summary>
/// L'Entité libre (0.35.2) côté serveur : ce que lit le LLM (vocabulaire des changements, état chiffré), l'application d'un acte
/// libre, le juge de cohérence et le bouton « Défaire ».
/// </summary>
public static class Libres
{
    private static string N(double v, int d = 0) => L.Nombre(v, d);

    /// <summary>Une faute de la simulation (« libre.erreur.x|a|b… ») en mots, pour le LLM et le joueur.</summary>
    public static string Message(string code)
    {
        var p = code.Split('|');
        if (!p[0].StartsWith("libre.", StringComparison.Ordinal) || !L.Existe(p[0])) return code;
        string A(int i) => i < p.Length ? p[i] : "";
        return L.T(p[0], ("quoi", A(1)), ("a", A(2)), ("b", A(3)), ("c", A(4)));
    }

    /// <summary>Applique un acte libre ; lève <see cref="ArgumentException"/> (en mots) si un changement est impossible : rien n'est fait.</summary>
    public static FaitEntite Appliquer(MondeSimule monde, ActeLlm a)
    {
        var acte = Lire(a);
        ResultatLibre r;
        try
        {
            r = monde.Libre(acte);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException(Message(e.Message), e);
        }
        var lignes = r.Lignes.Select(L.Rediger).ToList();
        if (r.Savent > 0) lignes.Add(L.T("libre.resultat.savent", ("n", r.Savent)));
        var defaire = JsonSerializer.Serialize(new DefaireLibre([.. r.Faits], [.. r.Decrets.Select(d => d.Id)]), Actes.Json);
        return new FaitEntite(lignes, 1, Math.Max(1, r.Faits.Select(f => f.Humain).Where(h => h > 0).Distinct().Count()), defaire);
    }

    private static ActeLibre Lire(ActeLlm a) =>
        new(a.Recit ?? "", a.Visible ?? false, a.Bienfait ?? true, a.Temoins ?? [], a.Changements ?? [], a.Decrets ?? []);

    /// <summary>Vérifie un acte libre par les règles du monde, sans rien changer (0.36.3) ; lève <see cref="ArgumentException"/> avec la raison en mots.</summary>
    public static void Verifier(MondeSimule monde, ActeLlm a)
    {
        try
        {
            monde.VerifierLibre(Lire(a));
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException(Message(e.Message), e);
        }
    }

    /// <summary>Défait un acte libre noté au registre ; rend ce qui a été défait, en mots.</summary>
    public static List<string> Defaire(ServiceMonde service, long id)
    {
        var a = service.Depot.Acte(id) ?? throw new ArgumentException(L.T("libre.erreur.acte"));
        if (a.Sorte != Actes.Libre || a.Defaire.Length == 0) throw new ArgumentException(L.T("libre.erreur.pasLibre"));
        if (a.Defait) throw new ArgumentException(L.T("libre.erreur.dejaDefait"));
        var d = JsonSerializer.Deserialize<DefaireLibre>(a.Defaire, Actes.Json) ?? throw new ArgumentException(L.T("libre.erreur.acte"));
        var (lignes, premier, dernier) = service.Monde.Executer(m => m.Defaire(d.Faits, d.Decrets, a.JournalPremier));
        if (dernier >= premier) MarquesLibre.Ajouter(premier, dernier);
        var textes = lignes.Select(L.Rediger).ToList();
        var resultats = JsonSerializer.Deserialize<List<string>>(a.Resultats, Actes.Json) ?? [];
        resultats.Add(L.T("libre.resultat.defait", ("date", L.Date(service.Monde.Secondes))));
        resultats.AddRange(textes);
        service.Depot.MarquerDefait(id, JsonSerializer.Serialize(resultats, Actes.Json));
        service.Sauvegarder();
        return textes.Count == 0 ? [L.T("libre.resultat.rienADefaire")] : textes;
    }

    /// <summary>Un acte libre en mots courts (liste des échanges, propositions) : son récit puis chaque changement.</summary>
    public static IEnumerable<string> Decrire(ActeLlm a)
    {
        yield return $"{L.T("libre.nom")}{(a.Visible == true ? " (" + L.T("libre.visible") + ")" : "")} : {ClientLlm.Couper(a.Recit ?? "", 300)}";
        foreach (var c in a.Changements ?? [])
        {
            var morceaux = new List<string> { c.Quoi };
            if (c.Humain != 0) morceaux.Add($"n°{c.Humain}");
            if (c.Communaute != 0) morceaux.Add(L.T("llm.r.communauteNo", ("id", c.Communaute)));
            if (c.Autre != 0) morceaux.Add($"→ {c.Autre}");
            if (c.Valeur is { } v) morceaux.Add($"= {N(v, 2)}");
            if (!string.IsNullOrEmpty(c.Texte)) morceaux.Add($"« {ClientLlm.Couper(c.Texte, 120)} »");
            if (!string.IsNullOrEmpty(c.Nom)) morceaux.Add($"« {ClientLlm.Couper(c.Nom, 80)} »");
            if (!string.IsNullOrEmpty(c.Forme)) morceaux.Add(c.Forme);
            if (!string.IsNullOrEmpty(c.Geste)) morceaux.Add($"{c.Geste} {c.A} {c.B}".Trim());
            if (c.Proprietes is { Count: > 0 } p) morceaux.Add(string.Join(", ", p.Select(x => $"{x.Key} {N(x.Value, 2)}")));
            if (c.X >= 0) morceaux.Add($"({c.X}, {c.Y}{(c.Rayon > 0 ? $", r {c.Rayon}" : "")})");
            yield return "  · " + string.Join(" ", morceaux);
        }
        foreach (var d in a.Decrets ?? [])
            yield return "  · " + L.T("libre.decrire.decret", ("loi", L.Existe($"loi.{d.Loi}") ? L.T($"loi.{d.Loi}") : d.Loi), ("f", N(d.Facteur, 2)),
                ("jours", d.Jours > 0 ? N(d.Jours) : "∞"));
    }

    private const string FormeLibre =
        """{"type":"libre","recit":"…","visible":true|false,"bienfait":true|false,"temoins":[n°],"changements":[{"quoi":"…","humain":n°,"valeur":x}],"decrets":[{"loi":"…","facteur":x,"communaute":0,"jours":0,"raison":"…"}]}""";

    /// <summary>Ce que l'Entité libre peut faire, pour la consigne.</summary>
    public static void Consigne(StringBuilder c)
    {
        c.AppendLine();
        c.AppendLine(L.T("libre.consigne"));
        c.AppendLine($"- {L.T("libre.cat.acte")} : {FormeLibre}");
        var nombres = Simulation.Libre.Sortes.Where(s => Simulation.Libre.Bornes(s) is not null)
            .Select(s => { var (min, max) = Simulation.Libre.Bornes(s)!.Value; return $"{s} ({N(min, max <= 1 ? 1 : 0)}–{N(max, max <= 1 ? 1 : 0)})"; });
        c.AppendLine(L.T("libre.cat.nombres", ("liste", string.Join(", ", nombres))));
        foreach (var s in Simulation.Libre.SortesHumain.Concat(Simulation.Libre.SortesMonde).Concat(Simulation.Libre.SortesInvention))
            c.AppendLine($"  - {s} : {L.T($"libre.cat.{s}")}");
        c.AppendLine(L.T("libre.cat.causes", ("liste", string.Join(", ", Enum.GetNames<CauseDeMort>().Where(n => n != nameof(CauseDeMort.Aucune))))));
        c.AppendLine(L.T("libre.cat.maladies", ("liste", string.Join(", ", Enum.GetNames<Maladie>().Where(n => n != nameof(Maladie.Aucune))))));
        c.AppendLine(L.T("libre.cat.pensees", ("liste", string.Join(", ", Enum.GetNames<TypeIntime>()))));
        c.AppendLine(L.T("libre.cat.relations", ("liste", string.Join(", ", Simulation.Libre.Relations))));
        c.AppendLine(L.T("libre.cat.decrets", ("liste", string.Join(", ", Enum.GetValues<LoiDecret>().Select(l => $"{l} ({L.T($"loi.{l}")})"))),
            ("min", N(Decrets.FacteurMin, 1)), ("max", N(Decrets.FacteurMax))));
        c.AppendLine(L.T("libre.cat.inventions", ("plantes", string.Join(", ", Inventions.FormesPlante)), ("betes", string.Join(", ", Inventions.FormesBete)),
            ("vertus", string.Join(", ", Inventions.VertusPlante)), ("aliments", string.Join(", ", Inventions.Aliments)),
            ("traits", string.Join(", ", Inventions.TraitsBete.Select(t => $"{t.Key} {N(t.Value.Min, t.Value.Min < 1 ? 1 : 0)}–{N(t.Value.Max)}")))));
        c.AppendLine(L.T("libre.cat.gestes", ("gestes", string.Join(", ", Enum.GetNames<Geste>())),
            ("matieres", string.Join(", ", Enum.GetNames<Matiere>().Where(n => n != nameof(Matiere.Aucune))))));
        c.AppendLine(L.T("libre.cat.idees", ("liste", string.Join(", ", Idees.Libelles.Keys))));
        c.AppendLine(L.T("libre.cat.regles"));
    }

    /// <summary>
    /// L'état chiffré d'un humain et son jardin secret numéroté (J1, J2…), pour qu'elle puisse le changer ; rend les références des
    /// choses du jardin (« secondes|clé ») dans l'ordre des numéros.
    /// </summary>
    public static List<string> Etat(StringBuilder d, MondeSimule monde, int id)
    {
        var (_, humains) = monde.InstantaneHumains();
        if (id < 1 || id > humains.Count) return [];
        var h = humains[id - 1];
        d.AppendLine(L.T("libre.d.valeurs", ("id", h.Id), ("liste", string.Join(", ", Simulation.Libre.Valeurs(h).Select(v => $"{v.Quoi} {N(v.Valeur, Math.Abs(v.Valeur) < 2 ? 2 : 0)}")))));
        var jardin = h.Memoire.Jardin.OrderByDescending(j => j.Intensite).Take(15).ToList();
        if (jardin.Count == 0) return [];
        d.AppendLine(L.T("libre.d.jardin"));
        var refs = new List<string>();
        foreach (var j in jardin)
        {
            refs.Add($"{j.Secondes.ToString(CultureInfo.InvariantCulture)}|{j.Cle}");
            d.AppendLine($"  J{refs.Count} : {(j.Recit is { } r ? L.Rediger(r) : j.Brut)} ({j.Type}, {N(j.Intensite)})");
        }
        return refs;
    }

    /// <summary>Les savoirs qu'on peut donner (connus d'au moins un humain, vivant ou mort), pour la demande.</summary>
    public static void SavoirsDuMonde(StringBuilder d, MondeSimule monde)
    {
        var (_, humains) = monde.InstantaneHumains();
        var effets = humains.SelectMany(h => h.Savoirs).Select(s => s.Effet).Where(e => e != Effet.Rien).Distinct().Order().ToList();
        if (effets.Count > 0) d.AppendLine(L.T("libre.d.savoirs", ("liste", string.Join(", ", effets.Take(120).Select(e => $"{e} ({L.T($"effet.{e}")})")))));
        var decrets = monde.Decrets().Where(x => x.EnVigueur(monde.Secondes)).ToList();
        if (decrets.Count > 0)
            d.AppendLine(L.T("libre.d.decrets", ("liste", string.Join(" ; ", decrets.Select(x => $"{x.Loi} ×{N(x.Facteur, 2)}{(x.CommunauteId > 0 ? $" ({L.T("llm.r.communauteNo", ("id", x.CommunauteId))})" : "")}")))));
    }

    /// <summary>« J3 » → la référence de la troisième chose du jardin montrée ; une référence complète est gardée.</summary>
    public static ActeLlm Resoudre(ActeLlm a, IReadOnlyList<string> jardin)
    {
        if (a.Type != Actes.Libre || a.Changements is null) return a;
        return a with
        {
            Changements = [.. a.Changements.Select(c => c.Quoi == "oublier" && c.Texte is { } t && t.Length > 1 && (t[0] is 'J' or 'j')
                && int.TryParse(t[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 1 && i <= jardin.Count ? c with { Texte = jardin[i - 1] } : c)],
        };
    }

    /// <summary>Schéma de la réponse du juge.</summary>
    public static JsonObject SchemaJuge() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["coherent"] = new JsonObject { ["type"] = "boolean" }, ["raison"] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray("coherent", "raison"),
    };

    /// <summary>Ce que lit le juge : le contexte que l'Entité a lu, puis sa décision.</summary>
    public static string DemandeJuge(LectureLlm lecture, string reponse) =>
        L.T("libre.juge.demande", ("contexte", lecture.Demande), ("decision", reponse));

    /// <summary>Lit le verdict du juge ; illisible, il compte comme cohérent (le juge ne bloque pas sur sa propre faute).</summary>
    public static VerdictJuge LireVerdict(string texte)
    {
        try
        {
            if (JsonNode.Parse(ClientLlm.Nettoyer(texte)) is JsonObject o)
            {
                var coherent = o["coherent"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true;
                var raison = o["raison"] is JsonValue r && r.TryGetValue<string>(out var s) ? ClientLlm.Couper(s.Trim(), 600) : "";
                return new VerdictJuge(coherent, raison);
            }
        }
        catch (JsonException)
        {
        }
        return new VerdictJuge(true, "");
    }
}

/// <summary>Les lignes du journal écrites par les actes libres (pour les marquer « LLM libre »).</summary>
public static class MarquesLibre
{
    private static readonly Lock Verrou = new();
    private static List<(long Premier, long Dernier)> _plages = [];

    public static void Remplacer(List<(long, long)> plages)
    {
        lock (Verrou) _plages = [.. plages.OrderBy(p => p.Item1)];
    }

    public static void Ajouter(long premier, long dernier)
    {
        if (dernier < premier || premier <= 0) return;
        lock (Verrou) _plages = [.. _plages.Append((Premier: premier, Dernier: dernier)).OrderBy(p => p.Premier)];
    }

    public static bool Contient(long id)
    {
        List<(long Premier, long Dernier)> plages;
        lock (Verrou) plages = _plages;
        foreach (var p in plages)
        {
            if (id < p.Premier) return false;
            if (id <= p.Dernier) return true;
        }
        return false;
    }
}
