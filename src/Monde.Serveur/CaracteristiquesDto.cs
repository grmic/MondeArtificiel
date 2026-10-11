using System.Runtime.CompilerServices;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Une mesure du monde. <paramref name="Case"/> : case à montrer sur la carte (−1 : aucune).</summary>
public sealed record MesureDto(string Libelle, string Valeur, string? Aide = null, int Case = -1);

/// <summary>Une rubrique de la fenêtre « Caractéristiques du Monde ».</summary>
public sealed record RubriqueDto(string Titre, IReadOnlyList<MesureDto> Mesures);

/// <summary>
/// Caractéristiques du Monde : échelle, continent, relief, eaux, terrains, climat, population, marche.
/// <paramref name="Traversee"/> : cases du plus long trajet à pied à travers le continent, pour la carte.
/// </summary>
public sealed record CaracteristiquesDto(string Nom, uint Graine, string Date, double KilometresParCase, double MetresParUnite,
    IReadOnlyList<RubriqueDto> Rubriques, IReadOnlyList<int> Traversee)
{
    /// <summary>Les mesures de la carte ne changent pas : calculées une fois par carte.</summary>
    private static readonly ConditionalWeakTable<Carte, Geographie> Cache = [];

    /// <summary>
    /// Heures de marche par jour lors d'un déplacement : chez les Hadza, les femmes marchent environ 6 km par jour et
    /// les hommes 11 km (Marlowe, 2010), soit 1 h 15 à 2 h 30 de marche ; un camp qui se déplace marche plus longtemps.
    /// Hypothèse retenue pour un long voyage : 6 heures de marche par jour.
    /// </summary>
    private const double HeuresDeMarcheParJour = 6;

    public static Geographie Geographie(Carte carte) => Cache.GetValue(carte, Simulation.Monde.Geographie.Calculer);

    private static string N(double v, int decimales = 1) => Echelle.Nombre(v, decimales);

    private static string Km(double km) => Echelle.Distance(km);

    private static string Km2(double km2) => Echelle.Surface(km2);

    private static string Pourcent(double part) => $"{N(part * 100, part < 0.1 ? 1 : 0)} %";

    private static string Duree(double heures)
    {
        if (heures < 1) return $"{Math.Round(heures * 60)} min";
        var h = (int)heures;
        var min = (int)Math.Round((heures - h) * 60);
        return min == 0 ? $"{h} h" : $"{h} h {min:00}";
    }

    public static CaracteristiquesDto Depuis(ServiceMonde service)
    {
        var monde = service.Monde;
        var carte = monde.Carte;
        var g = Geographie(carte);
        var (maintenant, humains) = monde.InstantaneHumains();
        var date = Calendrier.Decomposer(maintenant);

        var rubriques = new List<RubriqueDto>
        {
            new("Échelle", [
                new("Carte", $"{Km(g.LargeurCarteKm)} × {Km(g.HauteurCarteKm)}", $"{carte.Largeur} × {carte.Hauteur} cases, {Km2(g.SurfaceCarteKm2)}"),
                new("Une case", $"{N(Echelle.KilometresParCase * 1000, 0)} m de côté",
                    $"{Km2(Echelle.KilometresCarres(1))} ; un adulte la traverse en une dizaine de minutes en terrain plat"),
                new("Relief", $"{N(Echelle.MetresParUnite, 0)} m par degré d'altitude", "la carte compte 127 degrés au-dessus de la mer"),
            ]),
            new("Continent", [
                new("Surface", Km2(g.ContinentKm2), $"terres {Km2(g.TerresKm2)}, lacs {Km2(g.LacsKm2)}"),
                new("Étendue est-ouest", Km(g.EtendueEstOuestKm)),
                new("Étendue nord-sud", Km(g.EtendueNordSudKm)),
                new("Plus grande longueur", Km(g.PlusGrandeLongueurKm), "entre les deux points de la côte les plus éloignés", g.LongueurDe),
                new("Largeur moyenne", Km(g.PlusGrandeLongueurKm > 0 ? g.ContinentKm2 / g.PlusGrandeLongueurKm : 0),
                    "surface divisée par la plus grande longueur"),
                new("Côtes", $"environ {Km(g.CotesKm)}", $"mesurées case par case ({N(Echelle.KilometresParCase * 1000, 0)} m) : une côte réelle, plus découpée, serait plus longue"),
                new("Point le plus éloigné de la mer", Km(g.PlusLoinDeLaMerKm), null, g.PlusLoinDeLaMer),
                new("Îles au large", g.Iles == 0 ? "aucune" : $"{g.Iles} ({Km2(g.IlesKm2)})", "inaccessibles à pied"),
            ]),
            new("Relief", [
                new("Point culminant", $"{N(g.AltitudeMaxM, 0)} m", null, g.Sommet),
                new("Altitude moyenne des terres", $"{N(g.AltitudeMoyenneM, 0)} m"),
                .. g.Altitudes.Select(t => new MesureDto($"Terres à {t.Tranche}", Km2(t.Km2), Pourcent(g.TerresKm2 > 0 ? t.Km2 / g.TerresKm2 : 0))),
            ]),
            new("Eaux", [
                new("Lacs", g.Lacs == 0 ? "aucun" : $"{g.Lacs} ({Km2(g.LacsKm2)})"),
                new("Rivières", $"environ {Km(g.RivieresKm)} de cours"),
            ]),
            new("Terrains", [.. g.Biomes.Select(b => new MesureDto(Biomes.Noms[(int)b.Biome], Km2(b.Km2), Pourcent(b.Km2 / g.SurfaceCarteKm2)))]),
            new("Climat (à midi, au milieu de chaque saison)", [
                .. g.Temperatures.Select(t => new MesureDto(Majuscule(Calendrier.Decomposer(
                        (long)t.Saison * Calendrier.JoursParSaison * Calendrier.SecondesParJour).NomSaison),
                    $"{N(t.Min, 0)} à {N(t.Max, 0)} °C", "des sommets aux basses terres du centre")),
                new("Écart du moment", monde.AnomalieTemperature == 0 ? "aucun" : $"{(monde.AnomalieTemperature > 0 ? "+" : "")}{N(monde.AnomalieTemperature)} °C",
                    "vague de froid, canicule ou hiver volcanique"),
                new("Sous-sol", monde.Geologie.Description),
            ]),
            Population(monde, g, humains, maintenant),
            Marche(g, humains, maintenant),
        };
        return new CaracteristiquesDto(InfosMonde.ParDefaut.Nom, (uint)monde.Graine, date.ToString(), Echelle.KilometresParCase,
            Echelle.MetresParUnite, rubriques, g.Traversee?.Cases ?? []);
    }

    private static RubriqueDto Population(MondeSimule monde, Geographie g, List<Humain> humains, long maintenant)
    {
        var vivants = humains.Count(h => h.EstVivant);
        var communautes = monde.Communautes().Where(c => c.Active).ToList();
        var terres = communautes.SelectMany(c => VieSociale.Frequentees(c.Territoire)).Distinct().Count();
        var densite = g.TerresKm2 > 0 ? vivants / g.TerresKm2 : 0;
        return new("Population", [
            new("Âge du monde", Age(maintenant)),
            new("Humains vivants", N(vivants, 0)),
            new("Densité", $"{N(densite * 100, densite * 100 < 1 ? 2 : 1)} pour 100 km²",
                "les chasseurs-cueilleurs vivent d'ordinaire entre 1 et 100 personnes pour 100 km² (Binford, 2001 ; Kelly, 2013)"),
            new("Communautés", N(communautes.Count, 0)),
            terres == 0
                ? new("Terres parcourues", "aucune encore", "elles apparaissent quand des communautés se forment")
                : new("Terres parcourues", Km2(Echelle.KilometresCarres(terres)), $"{Pourcent(g.TerresKm2 > 0 ? Echelle.KilometresCarres(terres) / g.TerresKm2 : 0)} des terres du continent"),
        ]);
    }

    private static RubriqueDto Marche(Geographie g, List<Humain> humains, long maintenant)
    {
        var adultes = humains.Where(h => h.EstVivant && !h.EstEnfant(maintenant)).Select(h => Simulation.Humains.Marche.Vitesse(h, maintenant) * 3.6).ToList();
        var mesures = new List<MesureDto>
        {
            new("Allure d'un adulte", $"{N(Simulation.Humains.Marche.VitesseAdulte * 3.6)} km/h", "en terrain plat et dégagé (Bohannon et Andrews, 2011) ; chacun a son allure"),
        };
        if (adultes.Count > 0)
        {
            mesures.Add(new("Adultes du monde, en ce moment", $"{N(adultes.Min())} à {N(adultes.Max())} km/h",
                $"moyenne {N(adultes.Average())} km/h, selon l'âge, la santé, la fatigue, la grossesse, un bébé porté"));
        }
        mesures.Add(new("Terrains", "fougères ×0,85 · conifères ×0,8 · forêt tropicale ×0,6 · marais ×0,5 · neige ×0,45 · gué ×0,4",
            "facilité de la marche (Soule et Goldman, 1972 ; Tobler, 1993) ; les pentes ralentissent selon la fonction de Tobler"));
        if (g.Traversee is { } t)
        {
            mesures.Add(new("Traverser le continent", $"{Km(t.Kilometres)} à pied, {Duree(t.Heures)} de marche",
                $"le plus long trajet, par le chemin le moins pénible, pour un adulte : environ {N(Math.Ceiling(t.Heures / HeuresDeMarcheParJour), 0)} jours à 6 h de marche par jour",
                t.Cases[0]));
        }
        return new("Marche", mesures);
    }

    private static string Age(long secondes)
    {
        var ans = (int)(secondes / Calendrier.SecondesParAn);
        var saisons = (int)(secondes % Calendrier.SecondesParAn / (Calendrier.JoursParSaison * Calendrier.SecondesParJour));
        var texte = ans switch { 0 => "", 1 => "1 an", _ => $"{ans} ans" };
        if (saisons > 0) texte += (texte.Length > 0 ? " et " : "") + (saisons == 1 ? "1 saison" : $"{saisons} saisons");
        return texte.Length == 0 ? "moins d'une saison" : texte;
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>Un trajet à pied mesuré sur la carte.</summary>
/// <param name="Cases">Cases traversées (indices y × largeur + x).</param>
public sealed record TrajetDto(double Kilometres, double VolDOiseau, double Heures, string Texte, IReadOnlyList<int> Cases)
{
    public static TrajetDto? Depuis(Carte carte, int x1, int y1, int x2, int y2)
    {
        if (Geographie.CalculerTrajet(carte, carte.Indice(x1, y1), carte.Indice(x2, y2)) is not { } t) return null;
        var vol = Echelle.Kilometres(x1, y1, x2, y2);
        var heures = t.Heures;
        var duree = heures < 1 ? $"{Math.Round(heures * 60)} min" : $"{(int)heures} h {(int)Math.Round((heures - (int)heures) * 60):00}";
        var jours = heures > 6 ? $", soit environ {Echelle.Nombre(Math.Ceiling(heures / 6), 0)} jours à 6 h de marche par jour" : "";
        return new TrajetDto(Math.Round(t.Kilometres, 2), Math.Round(vol, 2), Math.Round(heures, 2),
            $"{Echelle.Distance(t.Kilometres)} à pied ({Echelle.Distance(vol)} à vol d'oiseau) : {duree} de marche pour un adulte{jours}.",
            t.Cases);
    }
}
