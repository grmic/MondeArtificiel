using System.Text.Json;
using System.Text.RegularExpressions;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>
/// 0.30.0 : fichiers de langue complets et cohérents, clés utilisées présentes et clés présentes utilisées, pas de français
/// écrit en dur dans le navigateur, et en français les libellés traduits écrivent exactement ce qu'écrivait la simulation.
/// </summary>
public sealed partial class LanguesTests
{
    private static readonly string Racine = TrouverRacine();

    private static string TrouverRacine()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "langues", "fr.json")) && Directory.Exists(Path.Combine(d.FullName, "client", "src")))
                return d.FullName;
        throw new DirectoryNotFoundException("Racine du dépôt introuvable.");
    }

    private static Dictionary<string, JsonElement> Lire(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Racine, "langues", $"{code}.json")));
        return doc.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_')).ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static readonly string[] Langues = Directory.EnumerateFiles(Path.Combine(Racine, "langues"), "*.json")
        .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    /// <summary>Textes d'une entrée (toutes ses formes de pluriel et de genre).</summary>
    private static IEnumerable<string> Textes(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Textes(p.Value)),
        _ => [],
    };

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Accolade();

    /// <summary>
    /// Paramètres d'une entrée. « nomMin » (le nom sans majuscule, après « de la » en français) et « nom » comptent pour un :
    /// chaque langue prend la forme qui lui convient.
    /// </summary>
    private static SortedSet<string> Parametres(JsonElement e) =>
        new(Textes(e).SelectMany(t => Accolade().Matches(t).Select(m => m.Groups[1].Value))
            .Select(p => p.EndsWith("Min", StringComparison.Ordinal) ? p[..^3] : p), StringComparer.Ordinal);

    [Fact]
    public void ChaqueLangue_AToutesLesClesDuFrancaisEtAucuneDePlus()
    {
        var fr = Lire("fr");
        Assert.True(fr.Count > 1500);
        foreach (var code in Langues.Where(c => c != "fr"))
        {
            var autre = Lire(code);
            Assert.Empty(fr.Keys.Except(autre.Keys).Select(k => $"{code} : manque {k}"));
            Assert.Empty(autre.Keys.Except(fr.Keys).Select(k => $"{code} : en trop {k}"));
            Assert.Equal(1.0, L.Part(L.Toutes[code]));
        }
    }

    [Fact]
    public void ChaqueTexte_ALesMemesParametresDansToutesLesLangues()
    {
        var fr = Lire("fr");
        foreach (var code in Langues.Where(c => c != "fr"))
        {
            var autre = Lire(code);
            var ecarts = fr.Where(p => autre.ContainsKey(p.Key) && !Parametres(p.Value).SetEquals(Parametres(autre[p.Key])))
                .Select(p => $"{code} {p.Key} : {string.Join(",", Parametres(p.Value))} / {string.Join(",", Parametres(autre[p.Key]))}");
            Assert.Empty(ecarts);
        }
    }

    [Fact]
    public void FormesDePlurielEtDeGenre_SontCompletes()
    {
        string[] pluriel = ["zero", "one", "two", "few", "many", "other"];
        foreach (var code in Langues)
        {
            var erreurs = new List<string>();
            void Verifier(string cle, JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.String) return;
                if (e.ValueKind != JsonValueKind.Object) { erreurs.Add($"{code} {cle} : ni texte ni formes"); return; }
                var formes = e.EnumerateObject().Select(p => p.Name).ToList();
                if (formes.Any(pluriel.Contains))
                {
                    if (!formes.Contains("other")) erreurs.Add($"{code} {cle} : pluriel sans « other »");
                    if (formes.Any(f => !pluriel.Contains(f))) erreurs.Add($"{code} {cle} : formes mêlées");
                }
                else if (!formes.Contains("m")) erreurs.Add($"{code} {cle} : genre sans « m »");
                foreach (var p in e.EnumerateObject()) Verifier(cle, p.Value);
            }
            foreach (var (cle, e) in Lire(code)) Verifier(cle, e);
            Assert.Empty(erreurs);
        }
    }

    // ───────────── Clés utilisées par le code ─────────────

    private static IEnumerable<(string Fichier, string Texte)> Sources()
    {
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Racine, "client", "src"), "*.ts"))
            yield return (f, File.ReadAllText(f));
        yield return (Path.Combine(Racine, "client", "index.html"), File.ReadAllText(Path.Combine(Racine, "client", "index.html")));
        var serveur = Path.Combine(Racine, "src", "Monde.Serveur");
        foreach (var f in Directory.EnumerateFiles(serveur, "*.cs", SearchOption.AllDirectories))
        {
            var dossier = Path.GetRelativePath(serveur, f).Split(Path.DirectorySeparatorChar)[0];
            if (dossier is not ("bin" or "obj")) yield return (f, File.ReadAllText(f));
        }
    }

    [GeneratedRegex(@"(?<![\w.])(?:t|traduire|L\.T)\(""([A-Za-z][\w.-]*)""")]
    private static partial Regex AppelLitteral();

    [GeneratedRegex(@"data-t(?:-[a-z-]+)?=""([A-Za-z][\w.-]*)""")]
    private static partial Regex AttributHtml();

    /// <summary>Préfixe d'une clé fabriquée : <c>`journal.type.${cle}`</c>, <c>$"cause.{cause}"</c>.</summary>
    [GeneratedRegex(@"(?:`|\$"")([A-Za-z][\w]*(?:\.[\w]+)*\.)(?:\$\{|\{)")]
    private static partial Regex PrefixeDynamique();

    [Fact]
    public void ClesAppelees_ExistentEnFrancais()
    {
        var fr = Lire("fr");
        var absentes = Sources()
            .SelectMany(s => AppelLitteral().Matches(s.Texte).Concat(AttributHtml().Matches(s.Texte))
                .Select(m => (Fichier: Path.GetFileName(s.Fichier), Cle: m.Groups[1].Value)))
            .Where(x => !fr.ContainsKey(x.Cle)).Select(x => $"{x.Fichier} : {x.Cle}").Distinct().ToList();
        Assert.Empty(absentes);
    }

    [Fact]
    public void ChaqueCle_EstUtilisee()
    {
        var sources = Sources().Select(s => s.Texte).ToList();
        var prefixes = sources.SelectMany(s => PrefixeDynamique().Matches(s).Select(m => m.Groups[1].Value)).ToHashSet();
        var citees = sources.SelectMany(s => Regex.Matches(s, @"""([A-Za-z][\w.-]*)""").Select(m => m.Groups[1].Value)).ToHashSet();
        var inutiles = Lire("fr").Keys.Where(k => !citees.Contains(k) && !prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).ToList();
        Assert.Empty(inutiles);
    }

    [Fact]
    public void ChaqueValeurDesEnumsAffichees_ASonTexte()
    {
        var fr = Lire("fr");
        var manque = new List<string>();
        void Enum<T>(string prefixe, params T[] sauf) where T : struct, System.Enum
        {
            foreach (var v in System.Enum.GetValues<T>().Except(sauf))
                if (!fr.ContainsKey($"{prefixe}.{v}")) manque.Add($"{prefixe}.{v}");
        }
        Enum("cause", CauseDeMort.Aucune);
        Enum<TypeCatastrophe>("catastropheNom");
        Enum<TypeCatastrophe>("catastropheDe");
        Enum<Biome>("biome");
        Enum("metier", Ouvrage.Aucun);
        Enum("maladieOrdinaire", Maladie.Aucune);
        Enum("phrase", Parole.Penchant);
        Enum<Parole>("parole");
        Enum<Saison>("saison");
        Enum<SorteReserve>("reserve");
        Enum<GesteEntite>("gesteEntite");
        Enum<SujetPriere>("sujetPriere");
        Enum<IssuePriere>("issuePriere");
        Enum<NaissanceReligion>("naissanceReligion");
        Enum<OrigineDogme>("origineDogme");
        // Meurtre et incendie se disent avec la victime (motif.aTue, motif.meurtre).
        Enum("motif", MotifGrief.Meurtre, MotifGrief.Incendie);
        Enum<Activite>("activite");
        Enum<OrdreMots>("ordreMots");
        Enum<ModeParole>("modeParole");
        Enum<MotifPeint>("style.motif");
        Enum<Effet>("effet");
        Enum<Matiere>("matiere");
        // Seuls les gestes de la fenêtre « Gestes » (Physique.Formes) ont un nom de forme.
        foreach (var f in Physique.Formes)
        {
            if (!fr.ContainsKey($"forme.{f.Geste}")) manque.Add($"forme.{f.Geste}");
            if (f.Liaison is not null && !fr.ContainsKey($"forme.{f.Geste}.liaison")) manque.Add($"forme.{f.Geste}.liaison");
        }
        Enum<ManiereGeste>("maniere");
        Enum<Maladie>("maladie");
        Enum<FormeMort>("formeMort");
        Enum<Reponse>("reponse");
        Enum<SourceProphete>("sourceProphete");
        Enum<OrigineSavoir>("origineSavoir");
        Assert.True(manque.Count == 0, string.Join(", ", manque));
    }

    // ───────────── Pas de français écrit en dur dans le navigateur ─────────────

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentaireBloc();

    [GeneratedRegex(@"(?m)^\s*//.*$|(?<=[;,{(\s])//[^\n""`]*$")]
    private static partial Regex CommentaireLigne();

    [GeneratedRegex(@"""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'|`(?:[^`\\]|\\.)*`")]
    private static partial Regex Chaine();

    [GeneratedRegex(@"[àâäçéèêëîïôöùûüÿœæÀÂÄÇÉÈÊËÎÏÔÖÙÛÜŸŒÆ]|\b(?:le|la|les|des|une|pour|dans|avec|sans|aucun|aucune)\b ")]
    private static partial Regex Francais();

    /// <summary>Codes échangés avec le serveur (jamais affichés tels quels).</summary>
    private static readonly HashSet<string> Codes = ["\"bébé\"", "\"renié\"", "\"exilé\"", "\"bébé\" | \"enfant\" | \"adulte\""];

    [Fact]
    public void Navigateur_NEcritPasDeFrancaisEnDur()
    {
        var trouves = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Racine, "client", "src"), "*.ts"))
        {
            if (Path.GetFileName(f) == "langue.ts") continue;
            var texte = CommentaireLigne().Replace(CommentaireBloc().Replace(File.ReadAllText(f), ""), "");
            foreach (Match m in Chaine().Matches(texte))
            {
                // Dans un gabarit, seul le texte hors des ${…} compte.
                var brut = m.Value.StartsWith('`') ? Regex.Replace(m.Value, @"\$\{[^}]*\}", "") : m.Value;
                if (Francais().IsMatch(brut) && !Codes.Contains(m.Value)) trouves.Add($"{Path.GetFileName(f)} : {m.Value}");
            }
        }
        Assert.Empty(trouves);
    }

    [Fact]
    public void ChaqueLangue_EstServieAuNavigateur()
    {
        foreach (var code in Langues)
        {
            Assert.True(L.Toutes.ContainsKey(code));
            Assert.False(string.IsNullOrWhiteSpace(L.Toutes[code].Nom));
        }
    }

    // ───────────── En français, rien ne change ─────────────

    private static IEnumerable<double> Balayage(double de, double a, double pas)
    {
        for (var v = de; v <= a + 1e-9; v += pas) yield return Math.Round(v, 6);
    }

    [Fact]
    public void EnFrancais_LesNiveauxEcriventCeQueDisaitLaSimulation()
    {
        L.Definir("fr");
        foreach (var v in Balayage(0, 1, 0.005)) Assert.Equal(Psyche.Niveau(v), Lib.Niveau(v));
        foreach (var v in Balayage(40, 170, 0.5)) Assert.Equal(Intelligence.Qualifier(v), Lib.Qi(v));
        foreach (var v in Balayage(-120, 200, 0.5)) Assert.Equal(Regard.Dire(v), Lib.Karma(v));
        foreach (var v in Balayage(0, 100, 0.5)) Assert.Equal(Croyances.Dire(v), Lib.Croyance(v));
        var homme = VitesseTests.Humain(2);
        Assert.Equal(Sexe.Homme, homme.Sexe);
        foreach (var s in System.Enum.GetValues<SentimentInvisible>())
            foreach (var v in Balayage(0, 100, 0.5)) Assert.Equal(Regards.Dire(s, v), Lib.Sentiment(s, v, homme));
        foreach (var t in System.Enum.GetValues<TypeCatastrophe>()) Assert.Equal(TypesCatastrophe.Nom(t), Lib.Catastrophe(t));
        for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                if (x != 0 || y != 0) Assert.Equal(Lieux.Vers(0, 0, x, y), Lib.Vers(0, 0, x, y));
        foreach (var taille in new[] { -1, -0.5, 0, 0.51 })
            foreach (var teint in new[] { 0.3, 0.38, 0.5, 0.63 })
                foreach (var corps in new[] { 0.3, 0.38, 0.5, 0.63 })
                {
                    var ideal = new IdealBeaute(taille, teint, corps);
                    Assert.Equal(Attirance.DireIdeal(ideal), Lib.Ideal(ideal));
                }
    }

    [Fact]
    public void EnFrancais_LesFichesEcriventCeQueDisaitLaSimulation()
    {
        L.Definir("fr");
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(3 * Calendrier.SecondesParAn);
        var (maintenant, humains) = monde.InstantaneHumains();
        var comparees = 0;
        foreach (var h in humains)
        {
            Assert.Equal(Attirance.Orienter(h), Lib.Orientation(h));
            foreach (var tolerance in new[] { 0.2, 0.8 }) Assert.Equal(Attirance.Conscience(h, tolerance), Lib.Conscience(h, tolerance));
            Assert.Equal(Comportement.Metier(h), Lib.Metier(h));
            Assert.Equal(Simulation.Humains.Medecine.Role(h), Lib.Soignant(h));
            if (!h.EstVivant) continue;
            Assert.Equal(VieSociale.Phrases(h, humains, maintenant), Lib.Raisons(h, humains, maintenant));
            foreach (var autre in humains.Where(a => a.Id != h.Id && (a.MereId == h.Id || a.PereId == h.Id || h.MereId == a.Id
                         || h.PereId == a.Id || h.PartenaireId == a.Id || (h.MereId != 0 && h.MereId == a.MereId))))
            {
                Assert.Equal(VieSociale.Parente(h, autre), Lib.Parente(h, autre));
                comparees++;
            }
        }
        Assert.True(comparees > 20);
        foreach (var c in monde.Communautes())
        {
            var membres = humains.Where(h => h.EstVivant && h.CommunauteId == c.Id && !h.EstBebe(maintenant)).ToList();
            Assert.Equal(Arts.Style(membres, []), Lib.Style(membres, []));
        }
    }

    [Fact]
    public void EnAnglais_LesMemesFonctionsEcriventEnAnglais()
    {
        L.Definir("en");
        try
        {
            Assert.Equal("the earthquake", Lib.Catastrophe(TypeCatastrophe.Seisme));
            Assert.DoesNotContain("é", Lib.Karma(0) + Lib.Croyance(50) + Lib.Niveau(0.5) + Lib.Qi(100));
            Assert.Equal("1.5", L.Nombre(1.5));
            Assert.Equal("12,500", L.Nombre(12500, 0));
        }
        finally
        {
            L.Definir("fr");
        }
        Assert.Equal("1,5", L.Nombre(1.5));
    }
}
