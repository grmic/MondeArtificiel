using Monde.Simulation;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Une catastrophe ou un phénomène, pour la liste du navigateur.</summary>
/// <param name="Type">Clé du type (« seisme », « incendie »…).</param>
/// <param name="Intensite">En mots : « magnitude 6,4 », « indice d'explosivité 3 », « catégorie 2 »…</param>
/// <param name="Surface">Terres touchées, en mots (null si la catastrophe ne touche pas de cases précises).</param>
/// <param name="MortsSuites">Morts de faim, de soif, de froid, d'épuisement ou de maladie dans les terres dévastées, après la catastrophe.</param>
/// <param name="SuitesJusqua">Date où les terres dévastées auront (ou ont) reverdi ; null si la catastrophe n'a pas laissé de dégâts durables.</param>
/// <param name="EnSuites">Finie, mais ses terres ne sont pas encore remises.</param>
public sealed record CatastropheDto(long Id, string Type, string Libelle, string Nom, string Origine, long Secondes, string Debut,
    string? Fin, bool Active, int X, int Y, int Rayon, string Intensite, int Morts, int Blesses, string? Surface,
    IReadOnlyList<CommunauteRefDto> Communautes, int Temoins, bool Phenomene, int MortsSuites, string? SuitesJusqua, bool EnSuites)
{
    public static readonly IReadOnlyDictionary<TypeCatastrophe, (string Cle, string Libelle)> Types =
        new Dictionary<TypeCatastrophe, (string, string)>
        {
            [TypeCatastrophe.Seisme] = ("seisme", "Séisme"),
            [TypeCatastrophe.Tsunami] = ("tsunami", "Vague géante"),
            [TypeCatastrophe.Eruption] = ("eruption", "Éruption"),
            [TypeCatastrophe.Crue] = ("crue", "Crue"),
            [TypeCatastrophe.Incendie] = ("incendie", "Incendie"),
            [TypeCatastrophe.Cyclone] = ("cyclone", "Tempête"),
            [TypeCatastrophe.VagueDeFroid] = ("froid", "Vague de froid"),
            [TypeCatastrophe.Canicule] = ("canicule", "Canicule"),
            [TypeCatastrophe.Epidemie] = ("epidemie", "Épidémie"),
            [TypeCatastrophe.Eclipse] = ("eclipse", "Éclipse"),
            [TypeCatastrophe.Comete] = ("comete", "Comète"),
            [TypeCatastrophe.Bolide] = ("bolide", "Boule de feu"),
            [TypeCatastrophe.Impact] = ("impact", "Astéroïde"),
            [TypeCatastrophe.Aurore] = ("aurore", "Aurore"),
            [TypeCatastrophe.HiverVolcanique] = ("hiver-volcanique", "Hiver volcanique"),
            [TypeCatastrophe.HiverDImpact] = ("hiver-impact", "Longue nuit"),
        };

    public static TypeCatastrophe? TypeDepuis(string? cle) =>
        Types.FirstOrDefault(t => t.Value.Cle == cle) is { Value.Cle: not null } t ? t.Key : null;

    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    private static string Decimal(double v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    private static string DecrireIntensite(Catastrophe c) => c.Type switch
    {
        TypeCatastrophe.Seisme => $"magnitude {Decimal(c.Intensite)}",
        TypeCatastrophe.Tsunami => $"vague de {c.Intensite:0} m",
        TypeCatastrophe.Eruption => $"indice d'explosivité {c.Intensite:0}",
        TypeCatastrophe.Crue => $"ampleur {c.Intensite:0} sur 3",
        TypeCatastrophe.Cyclone => $"catégorie {c.Intensite:0}",
        TypeCatastrophe.VagueDeFroid or TypeCatastrophe.Canicule or TypeCatastrophe.HiverVolcanique or TypeCatastrophe.HiverDImpact =>
            $"{(c.Intensite > 0 ? "+" : "")}{Decimal(c.Intensite)} °C",
        TypeCatastrophe.Epidemie => $"{c.Intensite:0} malades au début",
        TypeCatastrophe.Impact => c.Intensite < 10
            ? $"{c.Intensite.ToString("0.#", Fr)} mégatonnes"
            : $"{c.Intensite.ToString("N0", Fr)} mégatonnes",
        TypeCatastrophe.Eclipse => c.Intensite >= 0.999 ? "totale" : $"{c.Intensite:P0} du soleil caché",
        TypeCatastrophe.Bolide => c.Intensite > 0 ? "une pierre est tombée" : "",
        _ => "",
    };

    public static CatastropheDto Depuis(Catastrophe c, IReadOnlyList<Simulation.Humains.Communaute> communautes, long maintenant)
    {
        var (cle, libelle) = Types.TryGetValue(c.Type, out var t) ? t : ("autre", "Catastrophe");
        var cases = c.Cases.Count + c.Front.Count;
        return new CatastropheDto(c.Id, cle, libelle, c.Nom, c.Origine == Simulation.Origine.Entite ? "entite" : "naturelle", c.Debut,
            Calendrier.Decomposer(c.Debut).ToString(), c.Fin is { } f ? Calendrier.Decomposer(f).ToString() : null, c.Active,
            c.X, c.Y, c.Rayon, DecrireIntensite(c), c.Morts, c.Blesses, cases == 0 ? null : Catastrophes.Surface(cases),
            c.Communautes.Where(id => id >= 1 && id <= communautes.Count).Select(id => new CommunauteRefDto(id, communautes[id - 1].Nom)).ToList(),
            c.Temoins.Count, TypesCatastrophe.EstPhenomene(c.Type), c.MortsSuites,
            c.SuitesJusqua > 0 ? Calendrier.Decomposer(c.SuitesJusqua).ToString() : null, c.EnSuites(maintenant));
    }

    /// <summary>Catastrophes en mémoire et enregistrées, fusionnées, de la plus récente à la plus ancienne.</summary>
    public static List<CatastropheDto> Liste(ServiceMonde service, int? communaute, int limite)
    {
        var monde = service.Monde;
        var communautes = monde.Communautes();
        var enMemoire = monde.CatastrophesEnMemoire().Where(c => communaute is not { } co || c.Communautes.Contains(co));
        var enregistrees = service.Depot.LireCatastrophes(limite, communaute);
        return enMemoire.Concat(enregistrees).DistinctBy(c => c.Id).OrderByDescending(c => c.Id).Take(limite)
            .Select(c => Depuis(c, communautes, monde.Secondes)).ToList();
    }

    public static Catastrophe? Trouver(ServiceMonde service, long id) =>
        service.Monde.CatastrophesEnMemoire().FirstOrDefault(c => c.Id == id) ?? service.Depot.LireCatastrophe(id);
}

/// <summary>Une catastrophe avec les cases qu'elle a touchées (pour la carte) et les événements qu'elle a causés.</summary>
/// <param name="Cases">Cases touchées (indices y × largeur + x).</param>
/// <param name="Front">Cases en feu en ce moment.</param>
public sealed record DetailCatastropheDto(CatastropheDto Catastrophe, IReadOnlyList<int> Cases, IReadOnlyList<int> Front);

/// <summary>Faille de la carte (segment), volcan, et sismicité du continent.</summary>
public sealed record FailleDto(int X1, int Y1, int X2, int Y2, bool SousMarine);

public sealed record VolcanDto(int Numero, string Nom, int X, int Y, int ReposMoyenAns, bool EnEruption);

/// <param name="Anomalie">Écart de température du moment (°C) : vagues de froid, canicules, hiver volcanique.</param>
public sealed record GeologieDto(string Contexte, string Description, double SeismesFortsParSiecle, double MagnitudeMax,
    IReadOnlyList<FailleDto> Failles, IReadOnlyList<VolcanDto> Volcans, double Anomalie)
{
    public static GeologieDto Depuis(MondeSimule monde)
    {
        var g = monde.Geologie;
        var eruptions = monde.CatastrophesEnMemoire().Where(c => c.Active && c.Type == TypeCatastrophe.Eruption).Select(c => c.Parametre).ToHashSet();
        return new GeologieDto(g.Contexte switch
            {
                ContexteTectonique.Stable => "stable",
                ContexteTectonique.Rift => "rift",
                _ => "marge-active",
            },
            g.Description, Math.Round(g.TauxM5ParAn * 100, 1), Math.Round(g.MagnitudeMax, 1),
            g.Failles.Select(f => new FailleDto(f.X1, f.Y1, f.X2, f.Y2, f.SousMarine)).ToList(),
            g.Volcans.Select(v => new VolcanDto(v.Numero, v.Nom, v.X, v.Y, (int)Math.Round(v.ReposMoyenAns), eruptions.Contains(v.Numero))).ToList(),
            Math.Round(monde.AnomalieTemperature, 1));
    }
}

/// <summary>Une maladie : sa contagion (R0 : nombre moyen de personnes qu'un malade contamine), sa létalité, ceux qu'elle touche.</summary>
public sealed record MaladieDto(int Id, string Nom, string ApparueLe, double R0, double Letalite, int IncubationJours, int DureeJours,
    string Immunite, int Malades, int Immunises, int Morts, long Evenement)
{
    public static List<MaladieDto> Toutes(MondeSimule monde)
    {
        var (maintenant, humains) = monde.InstantaneHumains();
        var vivants = humains.Where(h => h.EstVivant).ToList();
        return monde.Pathogenes().OrderByDescending(p => p.Id).Select(p => new MaladieDto(p.Id, p.Nom, Calendrier.Decomposer(p.ApparuLe).ToString(),
            // Transmission = R0 / (contacts × 24 h × durée) : voir Catastrophes.NouveauPathogene.
            Math.Round(p.Transmission * 2.5 * 24 * p.DureeJours, 2), Math.Round(p.Letalite * 100, 1),
            (int)Math.Round(p.IncubationJours), (int)Math.Round(p.DureeJours),
            p.ImmuniteAns <= 0 ? "à vie" : $"environ {p.ImmuniteAns:0.#} ans",
            vivants.Count(h => h.InfectionId == p.Id),
            vivants.Count(h => h.Immunites.Any(i => i.PathogeneId == p.Id && (i.Jusqua is null || i.Jusqua > maintenant))),
            humains.Count(h => !h.EstVivant && h.CauseDeMort == Simulation.Humains.CauseDeMort.Maladie && h.InfectionId == p.Id),
            p.EvenementId)).ToList();
    }
}

/// <summary>Une alerte envoyée aux navigateurs.</summary>
/// <param name="Grave">Des humains sont menacés (le monde a pu se mettre en pause).</param>
public sealed record AlerteDto(long Catastrophe, string Type, string Texte, bool Grave)
{
    public static AlerteDto Depuis(AlerteCatastrophe a) =>
        new(a.CatastropheId, CatastropheDto.Types.TryGetValue(a.Type, out var t) ? t.Cle : "autre", a.Texte, a.Grave);
}

/// <summary>Demande de catastrophe (provoquée par l'Entité).</summary>
public sealed record DemandeCatastrophe(string Type, int X, int Y, double Intensite);
