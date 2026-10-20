using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>Un critère de la fenêtre « Remarquables » ; <see cref="Bas"/> : on peut aussi demander les plus bas.</summary>
public sealed record CritereRemarquableDto(string Cle, string Libelle, bool Bas);

/// <summary>Un humain remarquable selon un critère : son rang, sa valeur en mots, et un détail éventuel.</summary>
public sealed record RemarquableDto(int Rang, int Id, string Nom, string Sexe, int Age, string? Communaute, bool Vivant, string Valeur, string? Detail, int CommunauteId = 0);

/// <summary>
/// Les humains hors du commun, selon un critère : intelligence et aptitudes (échelle des QI), karma, croyance, découvertes,
/// soins, œuvres, descendance, longévité, victimes. Ce classement est le regard de l'Entité : les humains n'en savent rien.
/// </summary>
public static class Remarquables
{
    /// <summary>Taille du classement.</summary>
    public const int Nombre = 25;

    private sealed record Critere(string Cle, string Libelle, bool Bas, Func<Humain, Comptes, double> Valeur, Func<Humain, Comptes, double, string> Texte,
        Func<Humain, Comptes, string?>? Detail = null);

    /// <summary>Ce que l'on compte une fois pour tous : enfants, petits-enfants, victimes, œuvres et premières découvertes de chacun.</summary>
    private sealed class Comptes(IReadOnlyList<Humain> humains, IEnumerable<Oeuvre> oeuvres, IEnumerable<Decouverte> decouvertes, long maintenant)
    {
        /// <summary>Âge d'un vivant, ou âge atteint à sa mort.</summary>
        public double Age(Humain h) => ((h.MortLe ?? maintenant) - h.Naissance) / (double)Monde.Simulation.Temps.Calendrier.SecondesParAn;

        public readonly int[] Enfants = new int[humains.Count + 1];
        public readonly int[] PetitsEnfants = new int[humains.Count + 1];
        public readonly int[] Victimes = new int[humains.Count + 1];
        public readonly int[] Oeuvres = new int[humains.Count + 1];
        public readonly int[] Premieres = new int[humains.Count + 1];

        public Comptes Compter()
        {
            foreach (var h in humains)
            {
                foreach (var p in new[] { h.MereId, h.PereId })
                {
                    if (p < 1 || p > humains.Count) continue;
                    Enfants[p]++;
                    var parent = humains[p - 1];
                    if (parent.MereId is > 0 and var gm && gm <= humains.Count) PetitsEnfants[gm]++;
                    if (parent.PereId is > 0 and var gp && gp <= humains.Count) PetitsEnfants[gp]++;
                }
                if (!h.EstVivant && h.MeurtrierId is > 0 and var m && m <= humains.Count) Victimes[m]++;
            }
            foreach (var o in oeuvres) if (o.AuteurId >= 1 && o.AuteurId <= humains.Count) Oeuvres[o.AuteurId]++;
            foreach (var d in decouvertes) if (d.HumainId >= 1 && d.HumainId <= humains.Count) Premieres[d.HumainId]++;
            return this;
        }
    }

    private static string Qi(double v) => $"{Math.Round(v)} ({Intelligence.Qualifier(v)})";
    private static string Pluriel(int n, string un, string plusieurs) => n == 1 ? $"1 {un}" : $"{n} {plusieurs}";

    /// <summary>Savoirs trouvés seul (essai, observation, réflexion) : ni imités, ni soufflés par l'Entité.</summary>
    private static int TrouvesSeul(Humain h) => h.Savoirs.Count(s => s.Origine is OrigineSavoir.Essai or OrigineSavoir.Observation or OrigineSavoir.Reflexion);

    private static readonly Critere[] Criteres =
    [
        new("qi", "Intelligence (QI)", true, (h, _) => h.Personnalite.Intelligence.Generale, (_, _, v) => $"QI {Qi(v)}"),
        new("verbale", "Aptitude verbale", true, (h, _) => h.Personnalite.Intelligence.Verbale, (_, _, v) => Qi(v)),
        new("spatiale", "Aptitude spatiale", true, (h, _) => h.Personnalite.Intelligence.Spatiale, (_, _, v) => Qi(v)),
        new("manuelle", "Adresse manuelle", true, (h, _) => h.Personnalite.Intelligence.Manuelle, (_, _, v) => Qi(v)),
        new("memoire", "Mémoire", true, (h, _) => h.Personnalite.Intelligence.Memoire, (_, _, v) => Qi(v)),
        new("artistique", "Sens artistique", true, (h, _) => h.Personnalite.Intelligence.Artistique, (_, _, v) => Qi(v)),
        new("karma", "Karma", true, (h, _) => h.Karma, (_, _, v) => $"karma {(v > 0 ? "+" : "")}{Math.Round(v, 1).ToString(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))}"),
        new("croyance", "Croyance en l'invisible", true, (h, _) => h.Croyance, (_, _, v) => $"croyance {Math.Round(v)}/100",
            (h, _) => h.ChamanDepuis > 0 ? "chaman" : null),
        new("decouvertes", "Découvertes", false, (h, c) => TrouvesSeul(h) + 0.001 * c.Premieres[h.Id],
            (h, _, _) => Pluriel(TrouvesSeul(h), "savoir trouvé seul", "savoirs trouvés seul"),
            (h, c) => c.Premieres[h.Id] > 0 ? $"dont {Pluriel(c.Premieres[h.Id], "première du monde", "premières du monde")}" : null),
        new("soins", "Soignants", false, (h, _) => h.Gueris + 0.001 * h.Soignes,
            (h, _, _) => Pluriel(h.Gueris, "malade guéri", "malades guéris"),
            (h, _) => string.Join(", ", new[] { $"{h.Soignes} soignés", h.GuerisseurDepuis > 0 ? "guérisseur" : null, h.ChamanDepuis > 0 ? "chaman" : null,
                h.AccouchementsAides > 0 ? Pluriel(h.AccouchementsAides, "accouchement aidé", "accouchements aidés") : null }.Where(x => x is not null))),
        new("oeuvres", "Œuvres peintes", false, (h, c) => c.Oeuvres[h.Id], (_, _, v) => Pluriel((int)v, "œuvre", "œuvres")),
        new("descendance", "Descendance", false, (h, c) => c.Enfants[h.Id] + 0.001 * c.PetitsEnfants[h.Id],
            (h, c, _) => Pluriel(c.Enfants[h.Id], "enfant", "enfants"),
            (h, c) => c.PetitsEnfants[h.Id] > 0 ? Pluriel(c.PetitsEnfants[h.Id], "petit-enfant", "petits-enfants") : null),
        new("longevite", "Longévité", false, (h, c) => c.Age(h), (_, _, v) => $"{(int)v} ans"),
        new("victimes", "Victimes (meurtres, guerres)", false, (h, c) => c.Victimes[h.Id], (_, _, v) => Pluriel((int)v, "victime", "victimes")),
    ];

    public static List<CritereRemarquableDto> ListeCriteres() => Criteres.Select(c => new CritereRemarquableDto(c.Cle, c.Libelle, c.Bas)).ToList();

    /// <summary>
    /// Les <see cref="Nombre"/> premiers selon <paramref name="cle"/> (les plus bas si <paramref name="bas"/> et si le critère le
    /// permet), vivants seulement sauf si <paramref name="morts"/>. Un compte nul n'est jamais remarquable, ni un karma nul. Clé inconnue : liste vide.
    /// </summary>
    public static List<RemarquableDto> Classer(ServiceMonde service, string? cle, bool bas, bool morts)
    {
        if (Criteres.FirstOrDefault(c => c.Cle == cle) is not { } critere) return [];
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var comptes = new Comptes(humains, monde.Oeuvres(), monde.Installations().Decouvertes, maintenant).Compter();
        var noms = monde.Communautes().ToDictionary(c => c.Id, c => c.Nom);
        bas &= critere.Bas;
        var candidats = humains.Where(h => morts || h.EstVivant).Select(h => (H: h, V: critere.Valeur(h, comptes)))
            .Where(x => double.IsFinite(x.V) && (critere.Bas ? critere.Cle != "karma" || x.V != 0 : x.V > 0));
        var tries = bas ? candidats.OrderBy(x => x.V) : candidats.OrderByDescending(x => x.V);
        return tries.ThenBy(x => x.H.Id).Take(Nombre)
            .Select((x, i) => new RemarquableDto(i + 1, x.H.Id, x.H.Nom, x.H.Sexe == Sexe.Femme ? "femme" : "homme", (int)comptes.Age(x.H),
                x.H.CommunauteId != 0 ? noms.GetValueOrDefault(x.H.CommunauteId) : null, x.H.EstVivant,
                critere.Texte(x.H, comptes, x.V), critere.Detail?.Invoke(x.H, comptes) is { Length: > 0 } d ? d : null, x.H.CommunauteId))
            .ToList();
    }
}
