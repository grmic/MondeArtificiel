using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Un mot que connaît un humain : l'idée, sa forme (sons ou signe), sa force dans sa mémoire, et qui l'a inventé.</summary>
/// <param name="Forme">Pour un mot parlé : ses sons ; pour un signe : trois chiffres « main.mouvement.lieu » (voir <see cref="Langues.DireSigne"/>).</param>
/// <param name="Force">De 0 (oublié) à 1 (employé sans cesse).</param>
public sealed record Mot(string Idee, string Forme, bool Signe, double Force, long Le, int AuteurId, int Langue = 0);

/// <summary>Ce qui arrive à un mot dans la langue d'une communauté.</summary>
public enum ChangementMot : byte
{
    Adopte = 0,
    /// <summary>La forme a dérivé (les enfants l'ont dite autrement).</summary>
    Deforme = 1,
    /// <summary>Le mot d'une autre communauté a pris la place.</summary>
    Emprunte = 2,
    /// <summary>Un autre mot, sans parenté, a pris la place.</summary>
    Remplace = 3,
    Oublie = 4,
}

/// <summary>Une étape de l'histoire d'un mot dans une communauté.</summary>
/// <param name="Ancienne">Forme précédente (changement) ou oubliée.</param>
/// <param name="SourceId">Communauté prêteuse (emprunt), 0 sinon.</param>
public sealed record HistoireMot(string Idee, string Forme, string? Ancienne, bool Signe, long Le, int AuteurId, ChangementMot Type, int SourceId,
    long EvenementId);

/// <summary>
/// Ordre des mots (sujet, objet, verbe) fixé dans une langue. Fréquences dans les langues du monde (Dryer, 2013, WALS ch. 81) :
/// SOV 45 %, SVO 42 %, VSO 9 %, VOS 3 %, OVS et OSV rares.
/// </summary>
public enum OrdreMots : byte { Aucun = 0, SOV = 1, SVO = 2, VSO = 3, VOS = 4, OVS = 5, OSV = 6 }

/// <summary>
/// Les sons d'une langue : ceux que son premier inventeur a choisis, et que les mots suivants reprennent. <paramref name="Signes"/> :
/// penchant de la communauté pour les signes plutôt que la voix (0 à 1).
/// </summary>
public sealed record Sons(string[] Consonnes, string[] Voyelles, string[] Finales, double Signes)
{
    private static readonly string[] ToutesConsonnes = ["k", "t", "m", "n", "r", "s", "l", "v", "d", "b", "g", "h", "z", "j", "p", "f", "sh", "th"];
    private static readonly string[] ToutesVoyelles = ["a", "e", "i", "o", "u", "ai", "ei", "ou", "ia", "ae"];
    private static readonly string[] ToutesFinales = ["n", "r", "l", "k", "s", "m"];

    /// <summary>Un répertoire tiré au hasard : de 6 à 12 consonnes, 3 à 7 voyelles, 0 à 3 finales.</summary>
    public static Sons Tirer(Alea alea) => new(
        Choisir(ToutesConsonnes, 6 + alea.Entier(7), alea), Choisir(ToutesVoyelles, 3 + alea.Entier(5), alea),
        Choisir(ToutesFinales, alea.Entier(4), alea), 0.05 + 0.45 * alea.Reel() * alea.Reel());

    /// <summary>Le répertoire de mots déjà existants (ceux du nom d'un groupe), complété au besoin.</summary>
    public static Sons Depuis(IEnumerable<string> mots, Alea alea)
    {
        var c = new HashSet<string>();
        var v = new HashSet<string>();
        var f = new HashSet<string>();
        foreach (var mot in mots)
        foreach (var (s, type) in Decouper(mot.ToLowerInvariant()))
            (type == 0 ? c : type == 1 ? v : f).Add(s);
        var tire = Tirer(alea);
        foreach (var x in tire.Consonnes) if (c.Count < 6) c.Add(x);
        foreach (var x in tire.Voyelles) if (v.Count < 3) v.Add(x);
        return new([.. c.Order(StringComparer.Ordinal)], [.. v.Order(StringComparer.Ordinal)], [.. f.Order(StringComparer.Ordinal)], tire.Signes);
    }

    private static string[] Choisir(string[] tous, int n, Alea alea)
    {
        var l = tous.ToList();
        var r = new List<string>();
        while (r.Count < n && l.Count > 0)
        {
            var i = alea.Entier(l.Count);
            r.Add(l[i]);
            l.RemoveAt(i);
        }
        return [.. r.Order(StringComparer.Ordinal)];
    }

    /// <summary>Découpe un mot en sons : (son, 0 consonne | 1 voyelle | 2 finale).</summary>
    public static List<(string Son, int Type)> Decouper(string mot)
    {
        var r = new List<(string, int)>();
        var i = 0;
        while (i < mot.Length)
        {
            if (i + 1 < mot.Length && Array.IndexOf(ToutesVoyelles, mot.Substring(i, 2)) >= 0) { r.Add((mot.Substring(i, 2), 1)); i += 2; continue; }
            if (Array.IndexOf(ToutesVoyelles, mot.Substring(i, 1)) >= 0) { r.Add((mot.Substring(i, 1), 1)); i++; continue; }
            if (i + 1 < mot.Length && Array.IndexOf(ToutesConsonnes, mot.Substring(i, 2)) >= 0) { r.Add((mot.Substring(i, 2), 0)); i += 2; continue; }
            // Une consonne en fin de mot (après une voyelle) est une finale.
            r.Add((mot.Substring(i, 1), i == mot.Length - 1 && r.Count > 0 && r[^1].Item2 == 1 ? 2 : 0));
            i++;
        }
        return r;
    }

    /// <summary>« k.t.m|a.e.i|n.s|0.23 »</summary>
    public string Ecrire() => FormattableString.Invariant(
        $"{string.Join('.', Consonnes)}|{string.Join('.', Voyelles)}|{string.Join('.', Finales)}|{Signes:R}");

    public static Sons? Lire(string? texte)
    {
        if (string.IsNullOrEmpty(texte)) return null;
        var p = texte.Split('|');
        if (p.Length != 4) throw new InvalidDataException("Sons d'une langue corrompus dans la sauvegarde.");
        static string[] L(string s) => s.Length == 0 ? [] : s.Split('.');
        if (!double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var signes))
            throw new InvalidDataException("Sons d'une langue corrompus dans la sauvegarde.");
        var sons = new Sons(L(p[0]), L(p[1]), L(p[2]), signes);
        if (sons.Consonnes.Length == 0 || sons.Voyelles.Length == 0 || sons.Consonnes.Concat(sons.Voyelles).Concat(sons.Finales).Any(x => x.Length is 0 or > 2 || !x.All(char.IsLetter)))
            throw new InvalidDataException("Sons d'une langue corrompus dans la sauvegarde.");
        return sons;
    }
}

/// <summary>Les idées que les humains ont besoin de dire, et leur traduction pour l'observateur.</summary>
public static class Idees
{
    /// <summary>Ce dont on parle tous les jours, et les mots déjà nés en nommant les groupes.</summary>
    public static readonly IReadOnlyDictionary<string, string> Libelles = new Dictionary<string, string>
    {
        ["eau"] = "eau", ["manger"] = "manger", ["boire"] = "boire", ["dormir"] = "dormir", ["venir"] = "viens", ["partir"] = "partir",
        ["donner"] = "donner", ["prendre"] = "prendre", ["aider"] = "aider", ["oui"] = "oui", ["non"] = "non", ["regarder"] = "regarde",
        ["homme"] = "homme", ["femme"] = "femme", ["enfant"] = "enfant", ["bebe"] = "bébé", ["ancien"] = "ancien", ["mere"] = "mère",
        ["pere"] = "père", ["compagnon"] = "compagnon", ["ami"] = "ami", ["ennemi"] = "ennemi", ["etranger"] = "étranger",
        ["mort"] = "mort", ["malade"] = "malade", ["pierre"] = "pierre", ["arbre"] = "arbre", ["bois"] = "bois", ["herbe"] = "herbe",
        ["terre"] = "terre", ["ciel"] = "ciel", ["nuit"] = "nuit", ["jour"] = "jour", ["lune"] = "lune", ["pluie"] = "pluie",
        ["vent"] = "vent", ["froid"] = "froid", ["chaud"] = "chaud", ["gibier"] = "gibier", ["predateur"] = "bête qui tue",
        ["oiseau"] = "oiseau", ["poisson"] = "poisson", ["serpent"] = "serpent", ["chasser"] = "chasser", ["fuir"] = "fuir",
        ["frapper"] = "frapper", ["aimer"] = "aimer", ["peur"] = "peur", ["joie"] = "joie", ["chagrin"] = "chagrin",
        ["colere"] = "colère", ["douleur"] = "douleur", ["faim"] = "faim", ["soif"] = "soif", ["feu"] = "feu", ["reve"] = "rêve",
        ["invisible"] = "l'invisible", ["guerre"] = "guerre", ["gens"] = "les humains", ["soleil"] = "soleil", ["etoiles"] = "étoiles",
        ["riviere"] = "rivière", ["lac"] = "lac", ["rivage"] = "rivage", ["montagnes"] = "montagnes", ["marais"] = "marais",
        ["sables"] = "sables", ["foret"] = "forêt", ["fougeres"] = "fougères",
        // Mots nés en nommant les groupes, dont on ne parle pas autrement.
        ["ceux"] = "ceux (qui sont…)", ["enfants"] = "enfants de", ["marche"] = "marcher", ["partis"] = "partis",
    };

    /// <summary>Idées qui ne vivent que dans les noms de groupes : elles ne s'oublient pas faute d'en parler.</summary>
    public static readonly HashSet<string> NomsSeuls = ["ceux", "enfants", "marche", "partis"];

    /// <summary>Idées qui servent à nommer un groupe (paysage, ciel, ancêtres…) : les mots nés en se nommant.</summary>
    public static bool DeNom(string idee) => NomsSeuls.Contains(idee) || idee is "gens" or "soleil" or "etoiles" or "faim" or "riviere" or "lac"
        or "rivage" or "montagnes" or "marais" or "sables" or "foret" or "fougeres";

    private static readonly string[] Communes = ["eau", "manger", "boire", "dormir", "venir", "partir", "donner", "prendre", "aider", "oui", "non",
        "regarder", "homme", "femme", "enfant", "ancien", "pierre", "arbre", "bois", "herbe", "terre", "ciel", "nuit", "jour", "lune", "soleil",
        "etoiles", "pluie", "vent", "gibier", "predateur", "oiseau", "poisson", "serpent", "chasser", "fuir", "frapper", "aimer", "peur", "joie",
        "colere", "gens"];

    /// <summary>Les idées de tous les jours (mots courts, d'après la loi d'abréviation de Zipf, 1935).</summary>
    public static bool Courante(string idee) => Array.IndexOf(Communes, idee) >= 0 || idee is "mere" or "pere" or "feu" or "faim" or "soif";

    public static string Savoir(Effet e) => $"savoir:{(int)e}";
    public static string Plante(int espece) => $"plante:{espece}";

    /// <summary>L'idée en français.</summary>
    public static string Libelle(string idee, Flore? flore = null)
    {
        if (Libelles.TryGetValue(idee, out var l)) return l;
        if (idee.StartsWith("savoir:", StringComparison.Ordinal) && int.TryParse(idee[7..], out var e)) return Physique.NomEffet((Effet)e);
        if (idee.StartsWith("plante:", StringComparison.Ordinal) && int.TryParse(idee[7..], out var p) && flore is not null) return flore.Get(p).Nom;
        return idee;
    }

    /// <summary>
    /// Ce dont <paramref name="h"/> a l'occasion de parler aujourd'hui : les choses de tous les jours, ses proches, son deuil, ce qu'il
    /// sait faire, les plantes qu'il connaît, le paysage, l'invisible s'il y croit.
    /// </summary>
    public static List<string> Sujets(Humain h, IReadOnlyList<Humain> tous, Carte carte, long maintenant, IReadOnlySet<int> meres)
    {
        var l = new List<string>(Communes);
        if (h.Faim > 40) { l.Add("faim"); l.Add("manger"); }
        if (h.Soif > 40) { l.Add("soif"); l.Add("eau"); }
        if (h.Froid > 20) l.Add("froid");
        if (h.Peur > 40) l.Add("peur");
        if (h.Chagrin > 20) l.Add("chagrin");
        if (h.DeuilDeId > 0) l.Add("mort");
        if (h.EstMalade(maintenant) || h.Sante < 60) { l.Add("malade"); l.Add("douleur"); }
        if (h.MereId > 0 && tous[h.MereId - 1].EstVivant) l.Add("mere");
        if (h.PereId > 0 && tous[h.PereId - 1].EstVivant) l.Add("pere");
        if (h.PartenaireId > 0) l.Add("compagnon");
        if (h.Liens.Exists(x => x.Amis)) l.Add("ami");
        if (h.Liens.Exists(x => x.Rivaux)) l.Add("ennemi");
        if (meres.Contains(h.Id)) l.Add("bebe");
        if (h.Croyance >= 30) { l.Add("invisible"); l.Add("reve"); }
        if (h.Sait(Effet.Flamme) || h.Sait(Effet.Chaleur)) l.Add("feu");
        foreach (var s in h.Savoirs)
        {
            if (s.Confiance < 0.4) continue;
            l.Add(s.Espece > 0 ? Plante(s.Espece) : Savoir(s.Effet));
        }
        l.Add(carte.BiomeEn(h.X, h.Y) switch
        {
            Biome.Riviere => "riviere",
            Biome.Lac => "lac",
            Biome.Plage => "rivage",
            Biome.Montagne or Biome.SommetEnneige => "montagnes",
            Biome.Marais => "marais",
            Biome.Desert => "sables",
            Biome.ForetDeConiferes or Biome.ForetTropicale => "foret",
            _ => "fougeres",
        });
        return l;
    }
}

/// <summary>
/// Le langage : personne ne l'apporte. Un mot naît quand quelqu'un veut dire une chose et n'a pas de mot pour elle ; il se répand
/// si d'autres le reprennent, et le plus entendu l'emporte (le « jeu de dénomination » : Steels, 1995 ; Baronchelli et al., 2006).
/// Les enfants apprennent bien plus vite et déforment parfois ; une communauté qui se scinde voit ses deux langues dériver.
/// Les mots isolés viennent d'abord, puis leur assemblage, puis un ordre fixe, que régularisent surtout les enfants qui ont appris
/// d'autres enfants (la langue des signes du Nicaragua : Senghas et al., 2004).
/// </summary>
public static class Langues
{
    /// <summary>Aisance avec les mots (0 à 1) : l'aptitude verbale.</summary>
    public static double Aptitude(Humain h) => Math.Clamp((h.Personnalite.Intelligence.Verbale - 55) / 90, 0, 1);

    /// <summary>
    /// Le mot de <paramref name="h"/> pour une idée : dans la langue <paramref name="langue"/> (un mot sans langue encore, appris avant
    /// tout groupe, convient), ou dans n'importe laquelle si <paramref name="langue"/> est négative.
    /// </summary>
    public static int Indice(Humain h, string idee, int langue = -1)
    {
        var v = h.Vocabulaire;
        for (var i = 0; i < v.Count; i++) if (v[i].Idee == idee && (langue < 0 || v[i].Langue == langue || v[i].Langue == 0)) return i;
        return -1;
    }

    public static Mot? MotDe(Humain h, string idee, int langue = -1) => Indice(h, idee, langue) is >= 0 and var i ? h.Vocabulaire[i] : null;

    /// <summary>Les deux ont un même mot pour cette idée (dans quelque langue que ce soit).</summary>
    public static bool Partagent(Humain a, Humain b, string idee)
    {
        foreach (var x in a.Vocabulaire)
        {
            if (x.Idee != idee) continue;
            foreach (var y in b.Vocabulaire) if (y.Idee == idee && y.Forme == x.Forme && y.Signe == x.Signe) return true;
        }
        return false;
    }

    /// <summary>La langue que parle une communauté : la sienne, ou celle dont elle est issue tant que son parler n'en est qu'un dialecte.</summary>
    public static int LangueDe(Communaute? c) => c is null ? 0 : c.LangueId > 0 ? c.LangueId : c.Id;

    /// <summary>Ils peuvent s'expliquer : même communauté, et tous deux assemblent des mots.</summary>
    public static bool SExpliquent(Humain a, Humain b) => a.StadeParole >= 2 && b.StadeParole >= 2 && a.CommunauteId != 0 && a.CommunauteId == b.CommunauteId;

    private static readonly string[] Mains = ["la main à plat", "le poing", "l'index tendu", "la main en V", "la main en coupe", "la main aux doigts écartés"];
    private static readonly string[] Mouvements = ["qui ondule", "qui tape", "qui tourne", "qui monte", "qui descend", "qui s'ouvre", "qui se ferme", "qui va et vient"];
    private static readonly string[] Lieux = ["devant la bouche", "sur la poitrine", "au front", "vers le ciel", "vers le sol", "devant soi", "sur le ventre"];

    /// <summary>Un mot neuf dans les sons de la langue : court pour les choses de tous les jours.</summary>
    public static string Forger(Sons s, Alea alea, int syllabes)
    {
        var mot = "";
        for (var i = 0; i < syllabes; i++) mot += s.Consonnes[alea.Entier(s.Consonnes.Length)] + s.Voyelles[alea.Entier(s.Voyelles.Length)];
        if (s.Finales.Length > 0 && alea.Reel() < 0.3) mot += s.Finales[alea.Entier(s.Finales.Length)];
        return mot;
    }

    public static string SigneNeuf(Alea alea) => $"{alea.Entier(Mains.Length)}.{alea.Entier(Mouvements.Length)}.{alea.Entier(Lieux.Length)}";

    /// <summary>« la main à plat qui ondule devant la bouche »</summary>
    public static string DireSigne(string forme)
    {
        var p = forme.Split('.');
        return p.Length == 3 && int.TryParse(p[0], out var m) && int.TryParse(p[1], out var v) && int.TryParse(p[2], out var l)
               && m < Mains.Length && v < Mouvements.Length && l < Lieux.Length
            ? $"{Mains[m]} {Mouvements[v]} {Lieux[l]}" : forme;
    }

    /// <summary>Le mot tel que l'observateur le lit : « kasha », ou [signe : …].</summary>
    public static string Dire(string forme, bool signe) => signe ? $"[signe : {DireSigne(forme)}]" : forme;

    /// <summary>Un enfant qui apprend le mot le dit un peu autrement : un son changé (ou un trait du signe).</summary>
    public static string Deformer(string forme, bool signe, Sons sons, Alea alea)
    {
        if (signe)
        {
            var p = forme.Split('.');
            if (p.Length != 3) return forme;
            var k = alea.Entier(3);
            p[k] = alea.Entier(k == 0 ? Mains.Length : k == 1 ? Mouvements.Length : Lieux.Length).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Join('.', p);
        }
        var sonsDuMot = Sons.Decouper(forme);
        if (sonsDuMot.Count == 0) return forme;
        var j = alea.Entier(sonsDuMot.Count);
        var type = sonsDuMot[j].Type;
        var choix = type == 0 ? sons.Consonnes : type == 1 ? sons.Voyelles : sons.Finales;
        if (choix.Length == 0) return forme;
        sonsDuMot[j] = (choix[alea.Entier(choix.Length)], type);
        return string.Concat(sonsDuMot.Select(x => x.Son));
    }

    /// <summary>Deux formes parentes : un son (ou un trait du signe) de différence au plus.</summary>
    public static bool Parentes(string a, string b, bool signe)
    {
        if (a == b) return true;
        if (signe) return a.Split('.').Zip(b.Split('.')).Count(x => x.First != x.Second) <= 1;
        var x = Sons.Decouper(a);
        var y = Sons.Decouper(b);
        return x.Count == y.Count && x.Zip(y).Count(p => p.First.Son != p.Second.Son) <= 1;
    }

    public static string DireOrdre(OrdreMots o) => o switch
    {
        OrdreMots.SOV => "sujet, objet, verbe (« l'homme la bête chasse »)",
        OrdreMots.SVO => "sujet, verbe, objet (« l'homme chasse la bête »)",
        OrdreMots.VSO => "verbe, sujet, objet (« chasse l'homme la bête »)",
        OrdreMots.VOS => "verbe, objet, sujet (« chasse la bête l'homme »)",
        OrdreMots.OVS => "objet, verbe, sujet (« la bête chasse l'homme »)",
        OrdreMots.OSV => "objet, sujet, verbe (« la bête l'homme chasse »)",
        _ => "aucun ordre fixe",
    };

    public static string DireStade(int stade) => stade switch
    {
        0 => "des gestes et des cris",
        1 => "des mots isolés",
        2 => "des mots assemblés, sans ordre fixe",
        _ => "des mots assemblés dans un ordre fixe",
    };

    /// <summary>Tire un ordre selon sa fréquence dans les langues du monde.</summary>
    public static OrdreMots TirerOrdre(Alea alea)
    {
        var r = alea.Reel();
        return r < 0.45 ? OrdreMots.SOV : r < 0.87 ? OrdreMots.SVO : r < 0.96 ? OrdreMots.VSO : r < 0.99 ? OrdreMots.VOS : r < 0.995 ? OrdreMots.OVS : OrdreMots.OSV;
    }
}

public sealed partial class VieSociale
{
    /// <summary>La flore du monde (pour nommer les plantes au journal) ; fournie par le monde.</summary>
    public Flore? Flore { get; set; }

    /// <summary>Part de mots communs entre deux langues (calculée chaque semaine), par paire de communautés (plus petit numéro d'abord).</summary>
    public Dictionary<(int, int), double> Comprehension { get; } = [];

    private static double Age(Humain h, long maintenant) => h.AgeEnAnnees(maintenant);

    private Sons SonsDe(Communaute? c)
    {
        if (c is null) return Sons.Tirer(alea);
        return c.Sons ??= c.Lexique.Count > 0 ? Sons.Depuis(c.Lexique.Values, alea) : Sons.Tirer(alea);
    }

    private Communaute? CommunauteDe(Humain h, List<Communaute> communautes) =>
        h.CommunauteId > 0 && h.CommunauteId <= communautes.Count ? communautes[h.CommunauteId - 1] : null;

    /// <summary>
    /// Une journée de paroles : chacun parle à ceux qui l'entourent, d'autant plus qu'il est expansif, de ce qui fait sa vie. Les mots
    /// naissent, se répandent ou s'effacent ; les enfants reçoivent un nom dans la langue des leurs.
    /// </summary>
    public void Parler(List<Humain> humains, List<Communaute> communautes, Carte carte, long maintenant)
    {
        var parCase = new Dictionary<int, List<Humain>>();
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || Age(h, maintenant) < 1.5) continue;
            var k = h.Y * carte.Largeur + h.X;
            if (!parCase.TryGetValue(k, out var l)) parCase[k] = l = [];
            l.Add(h);
        }
        var meres = new HashSet<int>();
        foreach (var h in humains) if (h.EstVivant && h.MereId > 0 && h.EstBebe(maintenant)) meres.Add(h.MereId);
        var voisins = new List<Humain>();
        foreach (var s in humains.Vivants())
        {
            if (!s.EstVivant || Age(s, maintenant) < 1.5) continue;
            voisins.Clear();
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var x = s.X + dx;
                var y = s.Y + dy;
                if (x < 0 || y < 0 || x >= carte.Largeur || y >= carte.Hauteur || !parCase.TryGetValue(y * carte.Largeur + x, out var l)) continue;
                foreach (var o in l) if (o.Id != s.Id) voisins.Add(o);
            }
            s.CompagnonsDeLangue = s.StadeParole == 0 ? 0 : Math.Min(3, voisins.Count(o => o.CommunauteId == s.CommunauteId && o.StadeParole >= 1));
            if (voisins.Count == 0) continue;
            var cs = CommunauteDe(s, communautes);
            var p = s.Personnalite;
            var fois = 1 + (alea.Reel() < p.Extraversion ? 1 : 0) + (alea.Reel() < 0.5 * p.Extraversion ? 1 : 0);
            var sujets = Idees.Sujets(s, humains, carte, maintenant, meres);
            for (var i = 0; i < fois; i++)
            {
                var l = voisins[alea.Entier(voisins.Count)];
                if (l.AffectionPour(s.Id) < -40 || s.AffectionPour(l.Id) < -40) continue;
                Echanger(s, l, sujets[alea.Entier(sujets.Count)], cs, communautes, humains, maintenant);
            }
        }
        foreach (var h in humains) if (h.EstVivant) DonnerUnNom(h, humains, communautes, carte, maintenant);
    }

    /// <summary>
    /// <paramref name="s"/> veut dire <paramref name="idee"/> à <paramref name="l"/>. Sans mot, il en invente parfois un (bien plus
    /// volontiers s'il sait déjà que des sons peuvent dire des choses), sinon il montre et mime. L'auditeur reprend le mot, ou garde
    /// le sien et l'entend un peu moins bien. Chaque mot appartient à une langue : à un étranger, on parle sa langue si l'on en connaît
    /// le mot, sinon la sienne ; c'est ainsi qu'un captif ou un voisin finit par parler deux langues.
    /// </summary>
    internal void Echanger(Humain s, Humain l, string idee, Communaute? cs, List<Communaute> communautes, IReadOnlyList<Humain> tous, long maintenant)
    {
        var langueS = Langues.LangueDe(cs);
        var langueL = Langues.LangueDe(CommunauteDe(l, communautes));
        var i = langueL != langueS && Langues.Indice(s, idee, langueL) is >= 0 and var k && s.Vocabulaire[k].Langue == langueL ? k
            : Langues.Indice(s, idee, langueS) is >= 0 and var k2 ? k2 : Langues.Indice(s, idee);
        if (i < 0)
        {
            var aptitude = Langues.Aptitude(s);
            // Avant tout mot, l'idée même qu'un son puisse dire une chose est très rare ; ensuite, on en invente pour ce qui manque.
            var p = (s.Vocabulaire.Count == 0 ? 0.00002 : 0.0015) * (0.3 + aptitude) * (0.5 + s.Personnalite.Extraversion);
            if (alea.Reel() >= p) return;
            var sons = SonsDe(cs);
            var chasse = idee is "gibier" or "predateur" or "chasser" or "fuir" || idee == Idees.Savoir(Effet.Chasse);
            var signe = alea.Reel() < Math.Min(0.9, sons.Signes * (chasse ? 2 : 1));
            // On évite, sans toujours y parvenir, de reprendre une forme qui dit déjà autre chose.
            var neuve = "";
            for (var essai = 0; essai < 4; essai++)
            {
                neuve = signe ? Langues.SigneNeuf(alea) : Langues.Forger(sons, alea, Idees.Courante(idee) ? 1 + alea.Entier(2) : 2 + alea.Entier(2));
                if (!s.Vocabulaire.Exists(x => x.Forme == neuve) && (cs is null || !(signe ? cs.Signes : cs.Lexique).ContainsValue(neuve))) break;
            }
            s.Vocabulaire.Add(new Mot(idee, neuve, signe, 0.5, maintenant, s.Id, langueS));
            if (s.StadeParole == 0) s.StadeParole = 1;
            i = s.Vocabulaire.Count - 1;
        }
        var m = s.Vocabulaire[i];
        var langue = m.Langue > 0 ? m.Langue : langueS;
        var j = Langues.Indice(l, idee, langue);
        // Le même mot, connu dans une autre langue, se comprend aussi.
        if ((j < 0 || l.Vocabulaire[j].Forme != m.Forme) && l.Vocabulaire.FindIndex(x => x.Idee == idee && x.Forme == m.Forme && x.Signe == m.Signe) is >= 0 and var meme)
            j = meme;
        var enfant = Age(l, maintenant) < 14;
        if (j >= 0 && l.Vocabulaire[j].Forme == m.Forme && l.Vocabulaire[j].Signe == m.Signe)
        {
            // Compris : le mot s'ancre chez l'un et l'autre, et l'on se sent un peu plus proches.
            s.Vocabulaire[i] = m with { Force = Math.Min(1, m.Force + 0.1) };
            l.Vocabulaire[j] = l.Vocabulaire[j] with { Force = Math.Min(1, l.Vocabulaire[j].Force + 0.1) };
            ChangerAffection(s, l, 0.03, maintenant);
            Assembler(s, l, communautes, tous, maintenant);
            return;
        }
        var prestige = s.GuerisseurDepuis > 0 || CommunauteDe(s, communautes)?.FigureId == s.Id ? 0.3 : 0;
        var affection = Math.Max(0, l.AffectionPour(s.Id)) / 100;
        var forme = m.Forme;
        var auteur = m.AuteurId;
        // Un enfant qui apprend un mot le dit parfois un peu autrement : la langue dérive au fil des générations.
        if (Age(l, maintenant) < 10 && alea.Reel() < 0.03)
        {
            var deforme = Langues.Deformer(forme, m.Signe, SonsDe(CommunauteDe(l, communautes) ?? cs), alea);
            if (deforme != forme) (forme, auteur) = (deforme, l.Id);
        }
        if (j >= 0)
        {
            // Deux mots pour une même chose : celui qu'on entend le plus finit par l'emporter.
            var mien = l.Vocabulaire[j];
            var force = mien.Force - 0.15 * (0.5 + prestige + affection);
            l.Vocabulaire[j] = force > 0 ? mien with { Force = force } : new Mot(idee, forme, m.Signe, 0.3, maintenant, auteur, langue);
            s.Vocabulaire[i] = m with { Force = Math.Max(0.05, m.Force - 0.02) };
            return;
        }
        var adopter = Math.Min(0.95, 0.4 * (0.4 + Langues.Aptitude(l)) * (enfant ? 1.8 : 1) * (0.6 + affection + prestige));
        if (alea.Reel() >= adopter) return;
        l.Vocabulaire.Add(new Mot(idee, forme, m.Signe, 0.3, maintenant, auteur, langue));
        if (l.StadeParole == 0) l.StadeParole = 1;
        Assembler(s, l, communautes, tous, maintenant);
    }

    /// <summary>
    /// Assembler des mots : un locuteur qui en connaît assez finit par en dire deux d'un coup ; ceux qui l'entendent l'imitent.
    /// L'ordre des mots, lui, se fixe chez les enfants qui ont appris à assembler d'autres enfants (Senghas et al., 2004).
    /// </summary>
    private void Assembler(Humain s, Humain l, List<Communaute> communautes, IReadOnlyList<Humain> tous, long maintenant)
    {
        var aptitude = Langues.Aptitude(s);
        var ageS = Age(s, maintenant);
        if (s.StadeParole == 1 && s.Vocabulaire.Count >= 15 && alea.Reel() < 0.00003 * (0.3 + aptitude) * (ageS < 14 ? 2 : 1))
        {
            s.StadeParole = 2;
            s.ParoleEnfant = ageS < 10;
            if (tous.All(o => o.Id == s.Id || o.StadeParole < 2))
            {
                Noter(maintenant, TypeEvenement.Langage, s, l,
                    $"Première fois au monde : {s.Nom} dit deux mots d'un coup à {l.Nom}, et {l.Nom} comprend. On ne parle plus seulement par mots isolés.",
                    significatif: true);
            }
        }
        else if (s.StadeParole == 2 && s.ParoleNative && ageS is >= 3 and < 14 && alea.Reel() < 0.0003)
        {
            // Un enfant qui a appris à assembler d'autres enfants régularise : il met toujours les mots dans le même ordre.
            s.StadeParole = 3;
            s.Ordre = Langues.TirerOrdre(alea);
            if (tous.All(o => o.Id == s.Id || o.StadeParole < 3))
            {
                Noter(maintenant, TypeEvenement.Langage, s, null,
                    $"Première fois au monde : {s.Nom}, encore enfant, met toujours les mots dans le même ordre : {Langues.DireOrdre(s.Ordre)}. "
                    + "Une grammaire naît.", significatif: true);
            }
        }
        var ageL = Age(l, maintenant);
        var enfant = ageL < 14;
        if (s.StadeParole >= 2 && l.StadeParole == 1 && l.Vocabulaire.Count >= 10 && alea.Reel() < 0.03 * (0.4 + Langues.Aptitude(l)) * (enfant ? 2 : 1))
        {
            l.StadeParole = 2;
            l.ParoleEnfant = ageL < 10;
            l.ParoleNative = ageL < 6 && s.ParoleEnfant;
        }
        // L'ordre des mots se prend surtout dans l'enfance, d'autant mieux qu'on a grandi au milieu des phrases ; un adulte le prend rarement
        // (l'âge d'acquisition compte : Newport, 1990 ; Senghas et al., 2004).
        else if (s.StadeParole == 3 && l.StadeParole == 2 && alea.Reel() < (l.ParoleNative && enfant ? 0.15 : enfant ? 0.03 : 0.0002))
        {
            l.StadeParole = 3;
            l.Ordre = s.Ordre;
        }
    }

    /// <summary>
    /// Une fois que la communauté a des mots, chacun reçoit un nom dans sa langue : le nouveau-né de sa mère, les autres d'un proche
    /// ou de la figure du groupe. Le nom vient d'un mot (le soleil, la rivière, un oiseau…) et veut dire quelque chose.
    /// </summary>
    private void DonnerUnNom(Humain h, List<Humain> humains, List<Communaute> communautes, Carte carte, long maintenant)
    {
        if (h.NomLangue is not null || CommunauteDe(h, communautes) is not { Active: true } c || c.Stade < 1 || c.Lexique.Count < 15) return;
        var bebe = h.EstBebe(maintenant);
        if (alea.Reel() >= (bebe ? 0.02 : 0.003)) return;
        var mere = h.MereId > 0 ? humains[h.MereId - 1] : null;
        var parrain = mere is { EstVivant: true } && mere.CommunauteId == c.Id && mere.StadeParole >= 1 ? mere
            : humains.Vivants().Where(o => o.EstVivant && o.Id != h.Id && o.CommunauteId == c.Id && o.StadeParole >= 1 && !o.EstEnfant(maintenant))
                .OrderByDescending(o => o.AffectionPour(h.Id) + (o.Id == c.FigureId ? 20 : 0)).FirstOrDefault();
        if (parrain is null) return;
        var p = h.Personnalite;
        var candidats = new List<(string Idee, double Poids)>();
        void Si(string idee, double poids)
        {
            if (poids > 0 && c.Lexique.ContainsKey(idee)) candidats.Add((idee, poids));
        }
        foreach (var x in new[] { "soleil", "lune", "etoiles", "pluie", "vent", "riviere", "pierre", "arbre", "oiseau", "poisson", "herbe", "feu" }) Si(x, 1);
        Si("joie", 3 * p.Extraversion * p.Extraversion);
        Si("vent", 2 * p.Ouverture);
        Si("pierre", 2 * p.Conscience);
        Si("gibier", 1.5 * p.Adresse);
        Si("nuit", bebe && Calendrier.Decomposer(maintenant).Saison == Saison.Hiver ? 2 : 0);
        Si("pluie", bebe && Calendrier.Decomposer(maintenant).Saison == Saison.Printemps ? 2 : 0);
        if (candidats.Count == 0) return;
        var tirage = alea.Reel() * candidats.Sum(x => x.Poids);
        var idee = candidats[^1].Idee;
        foreach (var (i, w) in candidats)
        {
            if (tirage < w) { idee = i; break; }
            tirage -= w;
        }
        var sons = SonsDe(c);
        var nom = c.Lexique[idee];
        if (alea.Reel() < 0.5) nom += Langues.Forger(sons, alea, 1);
        var pris = humains.Vivants().Where(o => o.EstVivant && o.CommunauteId == c.Id && o.NomLangue is not null).Select(o => o.NomLangue!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (pris.Contains(nom)) nom += Langues.Forger(sons, alea, 1);
        nom = char.ToUpperInvariant(nom[0]) + nom[1..];
        var sens = Idees.Libelle(idee, Flore);
        var texte = parrain.Id == h.MereId
            ? $"{parrain.Nom} appelle {(bebe ? "son enfant" : $"{(h.Sexe == Sexe.Femme ? "sa fille" : "son fils")}")} {h.Nom} « {nom} », qui veut dire « {sens} »."
            : $"{parrain.Nom} donne un nom à {h.Nom} dans la langue de la {Minuscule(c.Nom)} : « {nom} », qui veut dire « {sens} ».";
        var id = Noter(maintenant, TypeEvenement.Langage, parrain, h, texte);
        (h.NomLangue, h.NomSens, h.NomLangueLe, h.NomParId, h.NomEvenementId) = (nom, sens, maintenant, parrain.Id, id);
    }

    /// <summary>
    /// Chaque semaine, la langue de chaque communauté : les mots que la plupart emploient. Ce qui change (un mot adopté, déformé,
    /// emprunté, remplacé, oublié) s'inscrit dans son histoire ; le stade et l'ordre des mots se constatent ; la part de mots communs
    /// entre langues voisines se mesure. Les mots s'effacent peu à peu chez qui ne les emploie plus.
    /// </summary>
    public void ConstaterLangues(List<Humain> humains, List<Communaute> communautes, long maintenant)
    {
        foreach (var h in humains)
        {
            if (!h.EstVivant)
            {
                if (h.Vocabulaire.Count > 0) h.Vocabulaire.Clear();
                continue;
            }
            for (var i = h.Vocabulaire.Count - 1; i >= 0; i--)
            {
                var m = h.Vocabulaire[i];
                // Les mots des noms de groupes ne s'oublient pas : on les entend chaque fois qu'on dit qui l'on est.
                if (Idees.NomsSeuls.Contains(m.Idee)) continue;
                if (m.Force <= 0.02) h.Vocabulaire.RemoveAt(i);
                else h.Vocabulaire[i] = m with { Force = m.Force - 0.02 };
            }
        }
        var membres = humains.Vivants().Where(h => h.EstVivant && h.CommunauteId > 0 && Age(h, maintenant) >= 3).ToLookup(h => h.CommunauteId);
        foreach (var c in communautes)
        {
            if (!c.Active) continue;
            var groupe = membres[c.Id].ToList();
            if (groupe.Count == 0) continue;
            SonsDe(c);
            var langue = Langues.LangueDe(c);
            // Les mots appris avant d'avoir un groupe deviennent ceux de sa langue.
            foreach (var h in groupe)
                for (var i = 0; i < h.Vocabulaire.Count; i++)
                    if (h.Vocabulaire[i].Langue == 0) h.Vocabulaire[i] = h.Vocabulaire[i] with { Langue = langue };
            MotsDeLaCommunaute(c, groupe, communautes, maintenant);
            Stade(c, groupe, communautes, maintenant);
            Separer(c, groupe, communautes, maintenant);
        }
        Comprehension.Clear();
        var actives = communautes.Where(c => c.Active && c.Lexique.Count + c.Signes.Count > 0).ToList();
        for (var a = 0; a < actives.Count; a++)
        for (var b = a + 1; b < actives.Count; b++)
            Comprehension[(actives[a].Id, actives[b].Id)] = Communs(actives[a], actives[b]);
        Bilingues(humains, actives, maintenant);
    }

    /// <summary>
    /// Qui connaît, en plus de la sienne, au moins la moitié des mots d'une autre langue la parle aussi : un captif, un exilé, un enfant
    /// né de deux groupes. Ces bilingues servent d'interprètes entre les communautés ; c'est noté une fois au journal.
    /// </summary>
    private readonly HashSet<(int Humain, int Langue)> _bilinguesNotes = [];

    private void Bilingues(List<Humain> humains, List<Communaute> actives, long maintenant)
    {
        var grandes = actives.Where(c => c.Lexique.Count + c.Signes.Count >= 15).ToList();
        if (grandes.Count < 2) return;
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Vocabulaire.Count < 10) continue;
            if (h.LangueSecondeId > 0 && (h.LangueSecondeId == h.CommunauteId || !grandes.Exists(c => c.Id == h.LangueSecondeId))) h.LangueSecondeId = 0;
            if (h.LangueSecondeId > 0) continue;
            foreach (var c in grandes)
            {
                if (c.Id == h.CommunauteId) continue;
                var propre = h.CommunauteId > 0 && h.CommunauteId <= Communautes.Count ? Communautes[h.CommunauteId - 1] : null;
                var autre = Langues.LangueDe(c);
                // Deux dialectes d'une même langue se comprennent : pas besoin d'interprète.
                if (propre is null || Langues.LangueDe(propre) == autre || Communs(propre, c) >= 0.5) continue;
                int total = 0, sus = 0;
                foreach (var (idee, forme) in c.Lexique)
                {
                    if (Idees.NomsSeuls.Contains(idee)) continue;
                    total++;
                    if (h.Vocabulaire.Exists(m => m.Idee == idee && !m.Signe && m.Forme == forme)) sus++;
                }
                foreach (var (idee, forme) in c.Signes)
                {
                    total++;
                    if (h.Vocabulaire.Exists(m => m.Idee == idee && m.Signe && m.Forme == forme)) sus++;
                }
                if (total < 10 || sus * 2 < total) continue;
                // Sa propre langue, il faut aussi la parler : sinon ce n'est qu'un changement de langue.
                var siens = Langues.LangueDe(propre);
                if (h.Vocabulaire.Count(m => m.Langue == siens) < 10) continue;
                h.LangueSecondeId = c.Id;
                // Noté une fois par personne et par langue, même s'il la perd de vue puis la retrouve.
                if (!_bilinguesNotes.Add((h.Id, autre))) break;
                Noter(maintenant, TypeEvenement.Langage, h, null,
                    $"{h.Nom} parle aussi la langue de la {Minuscule(c.Nom)} : {(h.Sexe == Sexe.Femme ? "elle" : "il")} peut servir d'interprète entre les siens et eux.",
                    communaute: h.CommunauteId, autreCommunaute: c.Id);
                break;
            }
        }
    }

    /// <summary>
    /// Une communauté née d'une scission parle d'abord le dialecte de celle qu'elle a quittée. Quand moins de la moitié de leurs mots
    /// restent parents, on ne se comprend plus : c'est une langue à part (noté au journal).
    /// </summary>
    private void Separer(Communaute c, List<Humain> groupe, List<Communaute> communautes, long maintenant)
    {
        if (c.LangueId <= 0 || c.LangueId == c.Id || c.Lexique.Count + c.Signes.Count < 15) return;
        var mere = c.LangueId <= communautes.Count ? communautes[c.LangueId - 1] : null;
        // Celle dont elle parlait le dialecte a disparu : sa langue devient la sienne, sans rupture.
        if (mere is not { Active: true }) mere = null;
        else if (Communs(c, mere) >= 0.5) return;
        var ancienne = c.LangueId;
        c.LangueId = c.Id;
        foreach (var h in groupe)
            for (var i = 0; i < h.Vocabulaire.Count; i++)
                if (h.Vocabulaire[i].Langue == ancienne) h.Vocabulaire[i] = h.Vocabulaire[i] with { Langue = c.Id };
        if (mere is null) return;
        var cible = groupe.Find(h => h.Id == c.FigureId) ?? groupe[0];
        Noter(maintenant, TypeEvenement.Langage, cible, null,
            $"Le parler de la {Minuscule(c.Nom)} s'est tant éloigné de celui de la {Minuscule(mere.Nom)} qu'on ne se comprend plus : c'est désormais une langue à part.",
            communaute: c.Id, autreCommunaute: mere.Id);
    }

    /// <summary>Part des idées dites par les deux langues qui se disent de façon parente.</summary>
    public static double Communs(Communaute a, Communaute b)
    {
        int communes = 0, parentes = 0;
        foreach (var (idee, forme) in a.Lexique)
        {
            if (Idees.NomsSeuls.Contains(idee)) continue;
            if (b.Lexique.TryGetValue(idee, out var f)) { communes++; if (Langues.Parentes(forme, f, false)) parentes++; }
            else if (b.Signes.ContainsKey(idee)) communes++;
        }
        foreach (var (idee, forme) in a.Signes)
        {
            if (b.Signes.TryGetValue(idee, out var f)) { communes++; if (Langues.Parentes(forme, f, true)) parentes++; }
            else if (b.Lexique.ContainsKey(idee)) communes++;
        }
        // Peu d'idées en commun ne suffit pas à se comprendre : en dessous de dix, la part est ramenée à dix idées.
        return parentes / Math.Max(communes, 10.0);
    }

    private void MotsDeLaCommunaute(Communaute c, List<Humain> groupe, List<Communaute> communautes, long maintenant)
    {
        var parIdee = new Dictionary<string, Dictionary<(string Forme, bool Signe), (int N, int Auteur)>>();
        var langue = Langues.LangueDe(c);
        foreach (var h in groupe)
        foreach (var m in h.Vocabulaire)
        {
            // Les mots d'une autre langue (d'un captif, d'un nouveau venu) ne font pas la langue du groupe.
            if (m.Langue != langue && m.Langue != 0) continue;
            if (!parIdee.TryGetValue(m.Idee, out var formes)) parIdee[m.Idee] = formes = [];
            var k = (m.Forme, m.Signe);
            formes[k] = formes.TryGetValue(k, out var x) ? (x.N + 1, x.Auteur) : (1, m.AuteurId);
        }
        var seuil = Math.Max(2, (int)Math.Ceiling(0.25 * groupe.Count));
        var courantsAvant = c.HistoireLangue.Count(x => x.Type == ChangementMot.Adopte && x.AuteurId > 0);
        foreach (var idee in parIdee.Keys.Concat(c.Lexique.Keys).Concat(c.Signes.Keys).Distinct().ToList())
        {
            (string Forme, bool Signe)? actuel = c.Lexique.TryGetValue(idee, out var fl) ? (fl, false) : c.Signes.TryGetValue(idee, out var fs) ? (fs, true) : null;
            var meilleur = parIdee.TryGetValue(idee, out var formes) ? formes.MaxBy(x => x.Value.N) : default;
            var tenu = formes is not null && meilleur.Value.N >= seuil;
            if (actuel is { } a && (formes is null || !formes.ContainsKey(a)))
            {
                if (Idees.NomsSeuls.Contains(idee) && formes is null) continue;
                if (Idees.DeNom(idee) && !c.HistoireLangue.Exists(x => x.Idee == idee) && formes is null)
                {
                    // Un mot né en nommant le groupe : tous le connaissent.
                    foreach (var h in groupe) h.Vocabulaire.Add(new Mot(idee, a.Forme, a.Signe, 0.5, maintenant, 0, langue));
                    c.HistoireLangue.Add(new HistoireMot(idee, a.Forme, null, a.Signe, maintenant, 0, ChangementMot.Adopte, 0, 0));
                    continue;
                }
                if (!tenu)
                {
                    Retirer(c, idee);
                    Historiser(c, idee, a.Forme, a.Forme, a.Signe, 0, ChangementMot.Oublie, 0, maintenant, groupe);
                    continue;
                }
            }
            if (!tenu) continue;
            var (forme, signe) = meilleur.Key;
            if (actuel is { } b2 && b2.Forme == forme && b2.Signe == signe) continue;
            var type = ChangementMot.Adopte;
            var source = 0;
            if (actuel is { } prec)
            {
                if (prec.Signe == signe && Langues.Parentes(prec.Forme, forme, signe)) type = ChangementMot.Deforme;
                else if (communautes.FirstOrDefault(o => o.Id != c.Id && o.Active && (signe ? o.Signes : o.Lexique).TryGetValue(idee, out var f) && f == forme) is { } preteuse)
                    (type, source) = (ChangementMot.Emprunte, preteuse.Id);
                else type = ChangementMot.Remplace;
            }
            Retirer(c, idee);
            (signe ? c.Signes : c.Lexique)[idee] = forme;
            Historiser(c, idee, forme, actuel?.Forme, signe, meilleur.Value.Auteur, type, source, maintenant, groupe);
        }
        var apres = c.Lexique.Count + c.Signes.Count;
        var cible = groupe.Find(h => h.Id == c.FigureId) ?? groupe[0];
        if (c.ParoleLe == 0 && c.HistoireLangue.Exists(x => x.Type == ChangementMot.Adopte && x.AuteurId > 0))
        {
            c.ParoleLe = maintenant;
            var premier = c.HistoireLangue.First(x => x.Type == ChangementMot.Adopte && x.AuteurId > 0);
            var mondial = courantsAvant == 0 && communautes.All(o => o.Id == c.Id || o.ParoleLe == 0);
            Noter(maintenant, TypeEvenement.Langage, cible, null,
                (mondial ? "Première fois au monde : des humains ont un mot pour une chose de tous les jours. " : "")
                + $"La {Minuscule(c.Nom)} a son premier mot en usage : « {Langues.Dire(premier.Forme, premier.Signe)} », qui veut dire « {Idees.Libelle(premier.Idee, Flore)} ».",
                premier.EvenementId, communaute: c.Id, significatif: mondial);
        }
        foreach (var palier in new[] { 10, 25, 50, 100, 200 })
        {
            // Chaque palier n'est noté qu'une fois, même si la langue perd des mots puis en regagne.
            if (c.PalierMots < palier && apres >= palier)
            {
                c.PalierMots = palier;
                Noter(maintenant, TypeEvenement.Langage, cible, null, $"La langue de la {Minuscule(c.Nom)} compte désormais {palier} mots.", communaute: c.Id);
            }
        }
    }

    private static void Retirer(Communaute c, string idee)
    {
        c.Lexique.Remove(idee);
        c.Signes.Remove(idee);
    }

    private void Historiser(Communaute c, string idee, string forme, string? ancienne, bool signe, int auteur, ChangementMot type, int source, long maintenant,
        List<Humain> groupe)
    {
        var sens = Idees.Libelle(idee, Flore);
        var dit = Langues.Dire(forme, signe);
        var qui = auteur > 0 && groupe.Find(h => h.Id == auteur) is { } a ? a : groupe.Find(h => h.Id == c.FigureId) ?? groupe[0];
        var nom = $"Les membres de la {Minuscule(c.Nom)}";
        var texte = type switch
        {
            ChangementMot.Adopte => $"{nom} disent désormais « {dit} » pour « {sens} »" + (auteur > 0 && qui.Id == auteur ? $", un mot de {qui.Nom}." : "."),
            ChangementMot.Deforme => $"{nom} ne disent plus « {Langues.Dire(ancienne!, signe)} » mais « {dit} » pour « {sens} » : le mot a changé dans la bouche des enfants.",
            ChangementMot.Emprunte when source > 0 && Communautes.Count >= source => $"{nom} ont pris à la {Minuscule(Communautes[source - 1].Nom)} son mot pour « {sens} » : « {dit} ».",
            ChangementMot.Oublie => $"{nom} n'ont plus de mot pour « {sens} » : « {dit} » est oublié.",
            _ => $"{nom} disent désormais « {dit} » pour « {sens} », à la place de « {Langues.Dire(ancienne!, signe)} ».",
        };
        var id = Noter(maintenant, TypeEvenement.Mots, qui, null, texte, communaute: c.Id);
        c.HistoireLangue.Add(new HistoireMot(idee, forme, ancienne, signe, maintenant, auteur, type, source, id));
    }

    /// <summary>Le stade de la langue (le plus haut que tienne au moins un quart du groupe) et l'ordre des mots, avec leurs tournants.</summary>
    private void Stade(Communaute c, List<Humain> groupe, List<Communaute> communautes, long maintenant)
    {
        var stade = 0;
        for (var s = 3; s >= 1; s--)
        {
            if (groupe.Count(h => h.StadeParole >= s) * 4 < groupe.Count) continue;
            stade = s;
            break;
        }
        var ordre = groupe.Where(h => h.StadeParole >= 3).GroupBy(h => h.Ordre).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? OrdreMots.Aucun;
        var cible = groupe.Find(h => h.Id == c.FigureId) ?? groupe[0];
        if (stade >= 2 && c.CombinaisonLe == 0)
        {
            c.CombinaisonLe = maintenant;
            Noter(maintenant, TypeEvenement.Langage, cible, null, $"Dans la {Minuscule(c.Nom)}, on assemble désormais les mots deux à deux pour dire davantage.",
                communaute: c.Id);
        }
        if (stade >= 3 && c.OrdreLe == 0)
        {
            c.OrdreLe = maintenant;
            Noter(maintenant, TypeEvenement.Langage, cible, null,
                $"La langue de la {Minuscule(c.Nom)} a désormais un ordre des mots, fixé par ses enfants : {Langues.DireOrdre(ordre)}.", communaute: c.Id);
        }
        c.Stade = (byte)stade;
        c.Ordre = stade >= 3 ? ordre : OrdreMots.Aucun;
    }
}
