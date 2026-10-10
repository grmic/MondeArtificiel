using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Ce qu'un humain reproche à un autre.</summary>
public enum MotifGrief : byte
{
    /// <summary>Son compagnon s'est uni à un autre.</summary>
    Infidelite = 0,
    /// <summary>Celui ou celle qui s'est uni à son compagnon.</summary>
    Rival = 1,
    /// <summary>Il l'a frappé.</summary>
    Coups = 2,
    /// <summary>Il a tué un de ses proches (<see cref="Grief.VictimeId"/>).</summary>
    Meurtre = 3,
}

/// <summary>
/// Une rancune : contre qui, pourquoi, avec quelle force (0 à 100), et l'événement du journal qui l'a fait naître.
/// Elle s'efface avec le temps, plus ou moins vite selon le caractère : le pardon vient plus facilement aux
/// personnes bienveillantes et émotionnellement stables (McCullough et Hoyt, 2002).
/// </summary>
public sealed record Grief(int ContreId, MotifGrief Motif, double Intensite, double Initiale, long EvenementId, int VictimeId = 0);

/// <summary>Ce qui pèse sur l'humeur d'un humain.</summary>
public enum TypeRaison : byte
{
    Deuil = 0,
    FaimSoif = 1,
    Froid = 2,
    Maladie = 3,
    Solitude = 4,
    Rejet = 5,
    Trahison = 6,
    Colere = 7,
    /// <summary>Un naturel anxieux ou sombre.</summary>
    Temperament = 8,
}

/// <summary>Une raison de l'humeur du jour, avec l'humain concerné (0 si aucun) et son poids en points d'humeur.</summary>
public sealed record Raison(TypeRaison Type, int AutreId, double Poids);

/// <summary>Ce qu'une communauté pense de ceux qui s'unissent hors de leur couple.</summary>
public enum UsageFidelite : sbyte
{
    MalVu = -1,
    Neutre = 0,
    Indifferent = 1,
}

/// <summary>Offense entre deux communautés (<see cref="A"/> &lt; <see cref="B"/>) : l'événement le plus récent qui les a dressées l'une contre l'autre.</summary>
public sealed record OffenseCommunautes(int A, int B, long EvenementId);

/// <summary>
/// Émotions et leurs causes : raisons de l'humeur, jalousie, colère, rancunes, pardon, séparation,
/// usages des communautés et offenses entre groupes. Chaque conséquence cite l'événement qui l'a provoquée.
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>Numéros des événements (partagés avec tout le monde simulé).</summary>
    public Numeroteur Numeros { get; set; } = new();

    private readonly Dictionary<(int A, int B), long> _offenses = [];

    /// <summary>Dernière offense entre deux communautés, dans un ordre stable.</summary>
    public List<OffenseCommunautes> Offenses =>
        _offenses.OrderBy(o => o.Key.A).ThenBy(o => o.Key.B).Select(o => new OffenseCommunautes(o.Key.A, o.Key.B, o.Value)).ToList();

    public void ChargerOffenses(IEnumerable<OffenseCommunautes> offenses)
    {
        _offenses.Clear();
        foreach (var o in offenses) _offenses[Paire(o.A, o.B)] = o.EvenementId;
    }

    /// <summary>
    /// Penchant à la jalousie, de 0 à 1 : plus fort chez les anxieux (névrosisme) et les moins bienveillants
    /// (Dijkstra et Barelds, 2008). La jalousie sexuelle existe dans toutes les sociétés étudiées (Buss, 2000).
    /// </summary>
    public static double Jalousie(Personnalite p) =>
        Math.Clamp(0.5 + 0.8 * (p.Nevrosisme - 0.5) - 0.5 * (p.Agreabilite - 0.5), 0, 1);

    /// <summary>Tolérance de l'entourage d'un humain envers l'infidélité : l'usage de sa communauté, sinon son propre tempérament.</summary>
    public double ToleranceDe(Humain h) => CommunauteDe(h)?.Tolerance ?? 1 - Jalousie(h.Personnalite);

    // ───────────── Raisons de l'humeur ─────────────

    /// <summary>Humains qui lui sont hostiles et qu'il a côtoyés ces deux dernières semaines, du plus hostile au moins hostile.</summary>
    public static List<int> Hostiles(Humain h, long maintenant)
    {
        var depuis = maintenant - 14 * Calendrier.SecondesParJour;
        return h.Liens.Where(l => l.Affection <= -10 && l.Derniere >= depuis)
            .OrderBy(l => l.Affection).ThenBy(l => l.Id).Select(l => l.Id).ToList();
    }

    /// <summary>Les raisons de son humeur en mots, de la plus lourde à la plus légère (par exemple « elle pleure sa fille Ana »).</summary>
    public static List<string> Phrases(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        var il = h.Sexe == Sexe.Femme ? "elle" : "il";
        var le = h.Sexe == Sexe.Femme ? "la" : "le";
        var e = E(h);
        string Nom(int id) => id >= 1 && id <= humains.Count ? humains[id - 1].Nom : "?";
        var phrases = new List<string>();
        foreach (var r in h.Raisons)
        {
            string? phrase = r.Type switch
            {
                TypeRaison.Deuil when r.AutreId > 0 => $"{il} pleure {Parente(h, humains[r.AutreId - 1])}",
                TypeRaison.FaimSoif => $"la faim et la soif {le} tenaillent",
                TypeRaison.Froid => $"le froid {le} transit",
                TypeRaison.Maladie => "la maladie l'affaiblit",
                TypeRaison.Solitude => $"{il} se sent seul{e}",
                TypeRaison.Rejet => Hostiles(h, maintenant) switch
                {
                    [var a] => $"{Nom(a)} lui est hostile",
                    [var a, var b] => $"{Nom(a)} et {Nom(b)} lui sont hostiles",
                    [var a, var b, ..] => $"{il} se sent rejeté{e} par {Nom(a)}, {Nom(b)} et d'autres",
                    _ => null,
                },
                TypeRaison.Trahison when r.AutreId > 0 => $"{Parente(h, humains[r.AutreId - 1])} l'a trahi{e}",
                TypeRaison.Colere => $"la colère {le} ronge",
                TypeRaison.Temperament => $"{il} est d'un naturel sombre",
                _ => null,
            };
            if (phrase is not null) phrases.Add(phrase);
        }
        return phrases;
    }

    /// <summary>Les trois premières raisons en une phrase : « a, b et c ».</summary>
    public static string Expliquer(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        var phrases = Phrases(h, humains, maintenant).Take(3).ToList();
        return phrases.Count <= 1 ? string.Concat(phrases) : string.Join(", ", phrases[..^1]) + " et " + phrases[^1];
    }

    /// <summary>Événement du journal à l'origine d'une raison (la mort pleurée, la trahison découverte…), 0 si aucun.</summary>
    private static long CauseDe(Humain h, Raison? raison) => raison?.Type switch
    {
        TypeRaison.Deuil => h.DeuilEvenementId,
        TypeRaison.Trahison => h.Griefs.FirstOrDefault(g => g.Motif == MotifGrief.Infidelite && g.ContreId == raison.AutreId)?.EvenementId ?? 0,
        TypeRaison.Colere => h.Griefs.MaxBy(g => g.Intensite)?.EvenementId ?? 0,
        _ => 0,
    };

    /// <summary>Un proche nommé par son lien avec l'humain : « sa fille Ana », « son compagnon Bo », « son amie Lia », sinon son nom.</summary>
    public static string Parente(Humain h, Humain autre)
    {
        var femme = autre.Sexe == Sexe.Femme;
        var lien = autre switch
        {
            _ when autre.MereId == h.Id || autre.PereId == h.Id => femme ? "sa fille" : "son fils",
            _ when h.MereId == autre.Id => "sa mère",
            _ when h.PereId == autre.Id => "son père",
            _ when h.PartenaireId == autre.Id || autre.PartenaireId == h.Id => femme ? "sa compagne" : "son compagnon",
            _ when h.MereId != 0 && h.MereId == autre.MereId => femme ? "sa sœur" : "son frère",
            _ when h.LienAvec(autre.Id) is { Amis: true } => femme ? "son amie" : "son ami",
            _ => null,
        };
        return lien is null ? autre.Nom : $"{lien} {autre.Nom}";
    }

    // ───────────── Colère et rancunes ─────────────

    /// <summary>Ajoute une rancune, ou ravive celle qui existe déjà contre le même humain pour le même motif.</summary>
    public static void AjouterGrief(Humain h, Grief grief)
    {
        var i = h.Griefs.FindIndex(g => g.ContreId == grief.ContreId && g.Motif == grief.Motif);
        if (i < 0)
        {
            h.Griefs.Add(grief);
            return;
        }
        var g = h.Griefs[i];
        var intensite = Math.Min(100, Math.Max(g.Intensite, grief.Intensite) + 0.3 * Math.Min(g.Intensite, grief.Intensite));
        h.Griefs[i] = grief with { Intensite = intensite, Initiale = Math.Max(g.Initiale, intensite) };
    }

    /// <summary>
    /// Chaque jour : la colère retombe en quelques jours (moins vite chez les anxieux et les querelleurs), les rancunes
    /// s'effacent en quelques mois (beaucoup plus lentement pour un meurtre), et se ravivent à la vue de l'offenseur.
    /// Une rancune éteinte devient un pardon ; une trahison qui ne passe pas peut briser le couple.
    /// L'infidélité est l'une des premières causes de séparation dans les sociétés étudiées (Betzig, 1989).
    /// </summary>
    private void Rancunes(Humain h, List<Humain> humains, long maintenant)
    {
        var p = h.Personnalite;
        var demiVieColere = 2 * Math.Exp(2 * (p.Nevrosisme - p.Agreabilite));
        h.Colere *= Math.Pow(0.5, 1 / demiVieColere);
        if (h.Colere < 1) h.Colere = 0;

        for (var i = h.Griefs.Count - 1; i >= 0; i--)
        {
            var g = h.Griefs[i];
            var autre = humains[g.ContreId - 1];
            if (!autre.EstVivant)
            {
                h.Griefs.RemoveAt(i);
                continue;
            }
            var demiVie = 45 * Math.Exp(1.5 * (p.Nevrosisme - 0.5) - 2 * (p.Agreabilite - 0.5)) * (g.Motif == MotifGrief.Meurtre ? 4 : 1);
            var intensite = g.Intensite * Math.Pow(0.5, 1 / demiVie);
            if (Math.Max(Math.Abs(h.X - autre.X), Math.Abs(h.Y - autre.Y)) <= 2) h.Colere = Math.Max(h.Colere, 0.5 * intensite);

            if (intensite < 8)
            {
                h.Griefs.RemoveAt(i);
                if (g.Initiale < 30) continue;
                Noter(maintenant, TypeEvenement.Conflit, h, autre, g.Motif switch
                {
                    MotifGrief.Infidelite => $"{h.Nom} pardonne à {autre.Nom} sa trahison.",
                    MotifGrief.Rival => $"{h.Nom} ne garde plus rancune à {autre.Nom}.",
                    MotifGrief.Coups => $"{h.Nom} pardonne à {autre.Nom} les coups reçus.",
                    _ => $"{h.Nom} renonce à se venger de {autre.Nom}.",
                }, g.EvenementId);
                continue;
            }
            h.Griefs[i] = g with { Intensite = intensite };

            if (g.Motif == MotifGrief.Infidelite && h.PartenaireId == autre.Id && intensite > 35
                && alea.Reel() < 0.02 * intensite / 100 * (1.5 - Math.Max(0, h.AffectionPour(autre.Id)) / 100))
            {
                h.PartenaireId = 0;
                if (autre.PartenaireId == h.Id) autre.PartenaireId = 0;
                h.Humeur = Math.Max(0, h.Humeur - 5);
                autre.Humeur = Math.Max(0, autre.Humeur - 10 * Math.Max(0, autre.AffectionPour(h.Id)) / 100);
                Noter(maintenant, TypeEvenement.Conflit, h, autre, $"{h.Nom} quitte {autre.Nom}, qui l'a trahi{E(h)}.", g.EvenementId);
            }
        }
    }

    /// <summary>Change l'affection entre deux humains (dans les deux sens), en notant l'hostilité qui naît ou la paix.</summary>
    public void ChangerAffection(Humain a, Humain b, double delta, long maintenant)
    {
        if (a.Id == b.Id) return;
        var avant = a.LienAvec(b.Id) ?? new Lien(b.Id, 0, 0, maintenant);
        var apres = Seuils(a, b, avant, avant with { Affection = Math.Clamp(avant.Affection + delta, -100, 100) }, maintenant);
        Poser(a, apres);
        Poser(b, apres with { Id = a.Id });
    }

    // ───────────── Jalousie ─────────────

    /// <summary>
    /// <paramref name="trompe"/> apprend que son compagnon <paramref name="infidele"/> s'est uni à <paramref name="complice"/>,
    /// en les voyant ou par un <paramref name="temoin"/>. Sa réaction dépend de son penchant à la jalousie, de son
    /// attachement au compagnon et de l'usage de sa communauté : de l'indifférence à la fureur.
    /// </summary>
    public void Decouvrir(Humain trompe, Humain infidele, Humain complice, Humain? temoin, long accouplement,
        IReadOnlyList<Humain> humains, long maintenant)
    {
        var amour = Math.Clamp(trompe.AffectionPour(infidele.Id) / 100, 0, 1);
        var tolerance = ToleranceDe(trompe);
        var intensite = 100 * Math.Clamp(Jalousie(trompe.Personnalite) * (0.5 + amour) * (1.4 - 0.8 * tolerance), 0, 1);
        var appris = temoin is null ? "" : $" ({temoin.Nom} les a vus et le lui a dit)";
        // Une trahison déjà connue blesse moins la seconde fois : la colère est là, mais la surprise est passée.
        var recidive = trompe.Griefs.Any(g => g.ContreId == infidele.Id && g.Motif == MotifGrief.Infidelite);
        if (recidive) intensite *= 0.5;
        var uni = recidive
            ? $"{Parente(trompe, infidele)} recommence avec {complice.Nom}{appris}"
            : $"{Parente(trompe, infidele)} s'est uni{E(infidele)} à {complice.Nom}{appris}";

        var communaute = CommunauteDe(trompe) ?? CommunauteDe(infidele);
        long id;
        if (intensite < 15)
        {
            id = Noter(maintenant, TypeEvenement.Conflit, trompe, infidele, $"{trompe.Nom} apprend que {uni}, et ne s'en émeut guère.", accouplement);
        }
        else
        {
            var sentiment = intensite >= 60 ? "Une jalousie furieuse l'envahit." : "La jalousie et la colère l'envahissent.";
            id = Noter(maintenant, TypeEvenement.Conflit, trompe, infidele, recidive
                ? $"{trompe.Nom} apprend que {uni}. {sentiment}"
                : $"{trompe.Nom} découvre que {uni}. {sentiment}", accouplement);
            trompe.Colere = Math.Min(100, trompe.Colere + intensite);
            trompe.Humeur = Math.Max(0, trompe.Humeur - intensite / 4);
            AjouterGrief(trompe, new Grief(infidele.Id, MotifGrief.Infidelite, intensite, intensite, id));
            AjouterGrief(trompe, new Grief(complice.Id, MotifGrief.Rival, 0.8 * intensite, 0.8 * intensite, id));
            ChangerAffection(trompe, infidele, -intensite / 3, maintenant);
            ChangerAffection(trompe, complice, -intensite / 2, maintenant);
        }
        // L'usage se forge aux affaires : un drame rend le groupe plus sévère, une affaire sans suite l'habitue.
        if (communaute is not null)
        {
            communaute.DerniereAffaireId = id;
            communaute.Tolerance = Math.Clamp(communaute.Tolerance + 0.1 * (0.35 - intensite / 100), 0, 1);
        }
        Reprouver(infidele, complice, id, humains, maintenant);
    }

    /// <summary>
    /// Là où l'infidélité est mal vue, la communauté de l'infidèle le réprouve : ceux qui le connaissent l'apprécient moins,
    /// ce qui peut aller jusqu'au rejet.
    /// </summary>
    private void Reprouver(Humain infidele, Humain complice, long cause, IReadOnlyList<Humain> humains, long maintenant)
    {
        if (CommunauteDe(infidele) is not { Tolerance: < 0.4 } c) return;
        var force = (0.4 - c.Tolerance) * 25;
        var id = Noter(maintenant, TypeEvenement.Conflit, infidele, complice,
            $"La {Minuscule(c.Nom)} réprouve {infidele.Nom}, qui s'est uni{E(infidele)} à {complice.Nom} hors de son couple.", cause);
        foreach (var l in infidele.Liens.ToList())
        {
            var m = humains[l.Id - 1];
            if (!m.EstVivant || m.CommunauteId != c.Id || m.Id == complice.Id) continue;
            ChangerAffection(m, infidele, -force, maintenant);
        }
        c.DerniereAffaireId = id;
    }

    /// <summary>Une affaire violente rend la communauté plus sévère envers l'infidélité.</summary>
    public void Durcir(Humain h, double force, long cause)
    {
        if (CommunauteDe(h) is not { } c) return;
        c.Tolerance = Math.Clamp(c.Tolerance - force, 0, 1);
        c.DerniereAffaireId = cause;
    }

    // ───────────── Offenses ─────────────

    /// <summary>
    /// Une violence de <paramref name="auteur"/> contre <paramref name="victime"/> : les proches de la victime et sa communauté
    /// en veulent à l'auteur. Si les deux appartiennent à des communautés différentes, toute la communauté de la victime en
    /// veut à celle de l'auteur, et l'offense est retenue comme cause d'une éventuelle hostilité entre les deux groupes.
    /// </summary>
    public void Offenser(Humain auteur, Humain victime, double poids, long cause, IReadOnlyList<Humain> humains, long maintenant)
    {
        var ca = CommunauteDe(auteur);
        var cv = CommunauteDe(victime);
        foreach (var l in auteur.Liens.ToList())
        {
            var m = humains[l.Id - 1];
            if (!m.EstVivant || m.Id == victime.Id || cv is null || m.CommunauteId != cv.Id) continue;
            ChangerAffection(m, auteur, -poids, maintenant);
        }
        if (ca is null || cv is null || ca.Id == cv.Id) return;
        foreach (var m in humains)
        {
            if (!m.EstVivant || m.CommunauteId != cv.Id) continue;
            foreach (var l in m.Liens.ToList())
            {
                var autre = humains[l.Id - 1];
                if (autre.EstVivant && autre.CommunauteId == ca.Id && autre.Id != auteur.Id) ChangerAffection(m, autre, -poids / 3, maintenant);
            }
        }
        _offenses[Paire(ca.Id, cv.Id)] = cause;
    }

    // ───────────── Usages des communautés ─────────────

    /// <summary>Une communauté qui naît d'une autre en garde l'usage ; sinon il vient du tempérament de ses adultes.</summary>
    private double ToleranceInitiale(List<Humain> groupe, List<Communaute> communautes, long maintenant)
    {
        var origine = groupe.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId)
            .Select(c => (Id: c.Key, Nombre: c.Count())).OrderByDescending(c => c.Nombre).ThenBy(c => c.Id).FirstOrDefault();
        if (origine.Id != 0 && origine.Nombre * 2 > groupe.Count) return communautes[origine.Id - 1].Tolerance;
        var adultes = groupe.Where(h => !h.EstEnfant(maintenant)).ToList();
        return adultes.Count == 0 ? 0.5 : adultes.Average(h => 1 - Jalousie(h.Personnalite));
    }

    /// <summary>Usage selon la tolérance, avec un écart entre l'entrée et la sortie pour éviter les allers-retours.</summary>
    private static UsageFidelite Classer(double tolerance, UsageFidelite avant) => avant switch
    {
        UsageFidelite.MalVu when tolerance <= 0.42 => UsageFidelite.MalVu,
        UsageFidelite.Indifferent when tolerance >= 0.58 => UsageFidelite.Indifferent,
        _ when tolerance < 0.35 => UsageFidelite.MalVu,
        _ when tolerance > 0.65 => UsageFidelite.Indifferent,
        _ => UsageFidelite.Neutre,
    };

    /// <summary>
    /// Chaque semaine : l'usage de chaque communauté glisse lentement vers le tempérament de ses adultes, et ses changements
    /// sont notés, avec pour cause la dernière affaire qui a marqué le groupe.
    /// </summary>
    private void Usages(List<Humain> humains, List<Communaute> communautes, long maintenant)
    {
        var adultes = humains.Where(h => h.EstVivant && h.CommunauteId != 0 && !h.EstEnfant(maintenant))
            .GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => g.Average(h => 1 - Jalousie(h.Personnalite)));
        foreach (var c in communautes)
        {
            if (!c.Active || !adultes.TryGetValue(c.Id, out var temperament)) continue;
            c.Tolerance += 0.02 * (temperament - c.Tolerance);
            var usage = Classer(c.Tolerance, c.Usage);
            if (usage == c.Usage) continue;
            var nom = Minuscule(c.Nom);
            var texte = (c.Usage, usage) switch
            {
                (_, UsageFidelite.MalVu) => $"Dans la {nom}, s'unir hors de son couple devient mal vu.",
                (_, UsageFidelite.Indifferent) => $"Dans la {nom}, nul ne s'offusque plus qu'on s'unisse hors de son couple.",
                (UsageFidelite.MalVu, _) => $"Dans la {nom}, on cesse de blâmer ceux qui s'unissent hors de leur couple.",
                _ => $"Dans la {nom}, on commence à mal supporter qu'on s'unisse hors de son couple.",
            };
            c.Usage = usage;
            Noter(maintenant, TypeEvenement.Communaute, humains[c.FigureId - 1], null, texte, c.DerniereAffaireId);
        }
    }
}
