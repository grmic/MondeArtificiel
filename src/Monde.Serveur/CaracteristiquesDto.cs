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

    private static string N(double v, int decimales = 1) => L.Nombre(v, decimales);

    private static string Km(double km) => L.Distance(km);

    private static string Km2(double km2) => L.Surface(km2);

    private static string Pourcent(double part) => L.T("commun.pourcent", ("v", N(part * 100, part < 0.1 ? 1 : 0)));

    private static string Duree(double heures)
    {
        if (heures < 1) return L.T("carac.marche.minutes", ("n", Math.Round(heures * 60)));
        var h = (int)heures;
        var min = (int)Math.Round((heures - h) * 60);
        return min == 0 ? L.T("carac.marche.heures", ("n", h)) : L.T("carac.marche.heuresMinutes", ("n", h), ("min", min.ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
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
            new(T("reglages"), [
                new(T("couples"), N(monde.Configuration.Couples, 0), T("couples.aide", ("n", N(monde.Configuration.Couples * 2, 0)))),
                new(T("continents"), N(monde.Configuration.Continents, 0)),
                new(T("depart"), Lib.Placement(monde.Configuration)),
                new(T("graine"), monde.Graine.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    T(monde.Configuration == ConfigurationMonde.ParDefaut(monde.Graine) ? "graine.origine" : "graine.choisie")),
            ]),
            new(T("echelle"), [
                new(T("carte"), T("carte.valeur", ("l", Km(g.LargeurCarteKm)), ("h", Km(g.HauteurCarteKm))),
                    T("carte.aide", ("l", carte.Largeur), ("h", carte.Hauteur), ("surface", Km2(g.SurfaceCarteKm2)))),
                new(T("case"), T("case.valeur", ("v", N(Echelle.KilometresParCase * 1000, 0))), T("case.aide", ("surface", Km2(Echelle.KilometresCarres(1))))),
                new(T("relief"), T("relief.valeur", ("v", N(Echelle.MetresParUnite, 0))), T("relief.aide")),
            ]),
            new(T(g.AutresContinentsKm2.Count == 0 ? "continent" : monde.Configuration.Placement == 1 ? "continentDepart" : "plusGrandContinent"), [
                new(T("surface"), Km2(g.ContinentKm2), T("surface.aide", ("terres", Km2(g.TerresKm2)), ("lacs", Km2(g.LacsKm2)))),
                new(T("estOuest"), Km(g.EtendueEstOuestKm)),
                new(T("nordSud"), Km(g.EtendueNordSudKm)),
                new(T("longueur"), Km(g.PlusGrandeLongueurKm), T("longueur.aide"), g.LongueurDe),
                new(T("largeur"), Km(g.PlusGrandeLongueurKm > 0 ? g.ContinentKm2 / g.PlusGrandeLongueurKm : 0), T("largeur.aide")),
                new(T("cotes"), T("cotes.valeur", ("v", Km(g.CotesKm))), T("cotes.aide", ("v", N(Echelle.KilometresParCase * 1000, 0)))),
                new(T("loinMer"), Km(g.PlusLoinDeLaMerKm), null, g.PlusLoinDeLaMer),
                new(T("iles"), g.Iles == 0 ? T("iles.aucune") : T("compte", ("n", g.Iles), ("surface", Km2(g.IlesKm2))), T("inaccessibles")),
                .. g.AutresContinentsKm2.Count == 0 ? Array.Empty<MesureDto>() : [
                    new(T("autresContinents"), T("compteListe", ("n", g.AutresContinentsKm2.Count), ("liste", string.Join(L.T("commun.virgule"), g.AutresContinentsKm2.Select(Km2)))),
                        T("autresContinents.aide")),
                ],
            ]),
            new(T("relief"), [
                new(T("culminant"), T("metres", ("v", N(g.AltitudeMaxM, 0))), null, g.Sommet),
                new(T("altitudeMoyenne"), T("metres", ("v", N(g.AltitudeMoyenneM, 0)))),
                .. g.Altitudes.Select((t, k) => new MesureDto(T($"tranche.{k}"), Km2(t.Km2), Pourcent(g.TerresKm2 > 0 ? t.Km2 / g.TerresKm2 : 0))),
            ]),
            new(T("eaux"), [
                new(T("lacs"), g.Lacs == 0 ? T("lacs.aucun") : T("compte", ("n", g.Lacs), ("surface", Km2(g.LacsKm2)))),
                new(T("rivieres"), T("rivieres.valeur", ("v", Km(g.RivieresKm)))),
            ]),
            new(T("terrains"), [.. g.Biomes.Select(b => new MesureDto(L.T($"biome.{b.Biome}"), Km2(b.Km2), Pourcent(b.Km2 / g.SurfaceCarteKm2)))]),
            new(T("climat"), [
                .. g.Temperatures.Select(t => new MesureDto(Majuscule(L.Saison(Calendrier.Decomposer(
                        (long)t.Saison * Calendrier.JoursParSaison * Calendrier.SecondesParJour).Saison)),
                    T("temperatures", ("min", N(t.Min, 0)), ("max", N(t.Max, 0))), T("temperatures.aide"))),
                new(T("ecart"), monde.AnomalieTemperature == 0 ? T("ecart.aucun") : T("degres", ("v", Lib.Signe(monde.AnomalieTemperature))), T("ecart.aide")),
                new(T("sousSol"), GeologieDto.Decrire(monde.Geologie)),
            ]),
            Population(monde, g, humains, maintenant),
            Marche(g, humains, maintenant),
        };
        rubriques.InsertRange(rubriques.FindIndex(r => r.Titre == T("terrains")) + 1, [.. SousSol(carte), Cotes(carte)]);
        return new CaracteristiquesDto(InfosMonde.ParDefaut.Nom, (uint)monde.Graine, L.Date(date), Echelle.KilometresParCase,
            Echelle.MetresParUnite, rubriques, g.Traversee?.Cases ?? []);
    }

    /// <summary>
    /// Ce que porte le terrain (voir <see cref="Terrain"/>) : roches et sols des terres émergées, falaises, grottes, sources,
    /// rivières, et les gisements, que l'Entité voit tous même quand nul humain ne les connaît.
    /// </summary>
    private static RubriqueDto[] SousSol(Carte carte)
    {
        var t = carte.Terrain;
        var terres = Enumerable.Range(0, carte.Biomes.Length).Where(i => carte.Altitudes[i] >= Carte.NiveauMer && Climat.EstPraticable((Biome)carte.Biomes[i])
                                                                          || (Biome)carte.Biomes[i] is Biome.Montagne or Biome.SommetEnneige).ToList();
        MesureDto[] Repartition<TCle>(Func<int, TCle> cle, Func<TCle, string> nom) where TCle : notnull =>
        [
            .. terres.GroupBy(cle).OrderByDescending(x => x.Count()).Select(x =>
                new MesureDto(nom(x.Key), Km2(Echelle.KilometresCarres(x.Count())), Pourcent(x.Count() / (double)terres.Count))),
        ];
        var grottes = t.Grottes.Where(x => !x.SousRoche).ToList();
        var abris = t.Grottes.Count - grottes.Count;
        var plusProfonde = grottes.MaxBy(x => x.Metres);
        var embouchure = Enumerable.Range(0, t.Debit.Length).MaxBy(i => t.Debit[i]);
        var infranchissables = Enumerable.Range(0, t.Courant.Length).Count(i => t.Debit[i] > 0 && t.Courant[i] > 1);
        return
        [
            new(T("roches"), Repartition(i => t.Roches[i], r => L.T($"roche.{r}"))),
            new(T("sols"), Repartition(i => t.Sols[i], s => L.T($"sol.{s}"))),
            new(T("cavites"), [
                new(T("falaises"), N(t.Falaise.Count(f => f), 0), T("falaises.aide")),
                new(T("grottes"), N(grottes.Count, 0), plusProfonde is null ? null : T("grottes.aide", ("v", N(plusProfonde.Metres, 0))),
                    plusProfonde is null ? -1 : carte.Indice(plusProfonde.X, plusProfonde.Y)),
                new(T("abrisSousRoche"), N(abris, 0)),
                .. Enum.GetValues<TypeSource>().Select(s => new MesureDto(T($"sources.{s}"), N(t.Sources.Count(x => x.Type == s), 0))),
                new(T("debitMax"), T("debit", ("v", N(t.Debit[embouchure], 0))), T("debitMax.aide"), embouchure),
                new(T("infranchissables"), T("infranchissables.valeur", ("v", Km(Echelle.Kilometres(infranchissables)))), T("infranchissables.aide")),
            ]),
            new(T("gisements"), [
                .. t.Gisements.GroupBy(x => x.Type).OrderBy(x => x.Key).Select((x, k) => new MesureDto(L.T($"gite.{x.Key}"),
                    T("gisements.valeur", ("n", x.Count()), ("a", x.Count(y => y.Profondeur == Enfouissement.Affleurant))), k == 0 ? T("gisements.aide") : null)),
            ]),
        ];
    }

    /// <summary>
    /// Côtes et mer (voir <see cref="Mer"/>) : rivages, fonds, récifs, passages vers les autres terres et ce qu'on en voit,
    /// vents dominants. Rien ne s'en sert encore pour naviguer.
    /// </summary>
    private static RubriqueDto Cotes(Carte carte)
    {
        var m = carte.Mer;
        var kmCase = Echelle.KilometresParCase;
        var rivages = m.Rivages.Where(r => r != Rivage.Aucun).GroupBy(r => r).OrderByDescending(x => x.Count())
            .Select(x => new MesureDto(T($"rivage.{x.Key}"), Km(x.Count() * kmCase))).ToList();
        int Rang(int masse) => m.Continents is var c && c.Contains(masse) ? c.ToList().IndexOf(masse) + 1 : 0;
        var passages = m.Passages.Where(p => Rang(p.MasseA) > 0 && Rang(p.MasseB) > 0)
            .Select(p => new MesureDto(T("passage", ("a", Rang(p.MasseA)), ("b", Rang(p.MasseB))), T("passage.valeur", ("v", Km(p.Km))),
                T(p.VueDuRivage ? "passage.vueRivage" : p.VueDuSommet ? "passage.vueSommet" : "passage.invisible"), p.DepuisA)).ToList();
        var ile = m.Iles.Where(i => !double.IsNaN(i.DistanceKm)).MaxBy(i => i.DistanceKm);
        return new(T("cotes.titre"), [
            .. rivages,
            new(T("plateau"), Km2(Echelle.KilometresCarres(m.Fonds.Count(f => f == Fond.Plateau))), T("plateau.aide")),
            new(T("profondeurMax"), T("metres", ("v", N(m.Profondeur.Max(), 0)))),
            new(T("recifs"), Km2(Echelle.KilometresCarres(m.Recif.Count(r => r))), T("recifs.aide")),
            new(T("hautsFonds"), N(m.HautFond.Count(r => r), 0), T("hautsFonds.aide")),
            .. ile is null ? Array.Empty<MesureDto>() : [new MesureDto(T("ileLointaine"), T("passage.valeur", ("v", Km(ile.DistanceKm))), null, carte.Indice(ile.X, ile.Y))],
            .. passages,
            .. m.Vents.Select((v, s) => new MesureDto(T("vent", ("saison", L.Saison((Saison)s))),
                T("vent.valeur", ("de", T($"rose.{(int)Math.Round(v.Depuis / 45) % 8}")), ("v", N(v.Vitesse, 0))), s == 0 ? T("vent.aide") : null)),
        ]);
    }

    private static RubriqueDto Population(MondeSimule monde, Geographie g, List<Humain> humains, long maintenant)
    {
        var vivants = humains.Count(h => h.EstVivant);
        var communautes = monde.Communautes().Where(c => c.Active).ToList();
        var terres = communautes.SelectMany(c => VieSociale.Frequentees(c.Territoire)).Distinct().Count();
        var densite = g.TerresKm2 > 0 ? vivants / g.TerresKm2 : 0;
        return new(T("population"), [
            new(T("age"), Age(maintenant)),
            new(T("vivants"), N(vivants, 0)),
            new(T("densite"), T("densite.valeur", ("v", N(densite * 100, densite * 100 < 1 ? 2 : 1))), T("densite.aide")),
            new(T("communautes"), N(communautes.Count, 0)),
            terres == 0
                ? new(T("terres"), T("terres.aucune"), T("terres.aucune.aide"))
                : new(T("terres"), Km2(Echelle.KilometresCarres(terres)), T("terres.aide", ("part", Pourcent(g.TerresKm2 > 0 ? Echelle.KilometresCarres(terres) / g.TerresKm2 : 0)))),
        ]);
    }

    private static RubriqueDto Marche(Geographie g, List<Humain> humains, long maintenant)
    {
        var adultes = humains.Where(h => h.EstVivant && !h.EstEnfant(maintenant)).Select(h => Simulation.Humains.Marche.Vitesse(h, maintenant) * 3.6).ToList();
        var mesures = new List<MesureDto>
        {
            new(T("allure"), T("kmh", ("v", N(Simulation.Humains.Marche.VitesseAdulte * 3.6))), T("allure.aide")),
        };
        if (adultes.Count > 0)
        {
            mesures.Add(new(T("adultes"), T("adultes.valeur", ("min", N(adultes.Min())), ("max", N(adultes.Max()))),
                T("adultes.aide", ("v", N(adultes.Average())))));
        }
        mesures.Add(new(T("terrains"), T("terrains.marche"), T("terrains.marche.aide")));
        if (g.Traversee is { } t)
        {
            mesures.Add(new(T("traverser"), T("traverser.valeur", ("km", Km(t.Kilometres)), ("duree", Duree(t.Heures))),
                T("traverser.aide", ("n", Math.Ceiling(t.Heures / HeuresDeMarcheParJour))), t.Cases[0]));
        }
        return new(T("marche"), mesures);
    }

    private static string Age(long secondes)
    {
        var ans = (int)(secondes / Calendrier.SecondesParAn);
        var saisons = (int)(secondes % Calendrier.SecondesParAn / (Calendrier.JoursParSaison * Calendrier.SecondesParJour));
        var texte = ans == 0 ? "" : L.T("duree.ans", ("n", ans));
        if (saisons > 0) texte = texte.Length > 0 ? L.T("carac.ageEt", ("ans", texte), ("saisons", L.T("carac.saisons", ("n", saisons)))) : L.T("carac.saisons", ("n", saisons));
        return texte.Length == 0 ? L.T("carac.moinsDUneSaison") : texte;
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string T(string cle, params ReadOnlySpan<(string Nom, object? Valeur)> p) => L.T($"carac.{cle}", p);
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
        var duree = heures < 1 ? L.T("carac.marche.minutes", ("n", Math.Round(heures * 60)))
            : L.T("carac.marche.heuresMinutes", ("n", (int)heures), ("min", ((int)Math.Round((heures - (int)heures) * 60)).ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
        var jours = heures > 6 ? L.T("trajet.jours", ("n", Math.Ceiling(heures / 6))) : "";
        return new TrajetDto(Math.Round(t.Kilometres, 2), Math.Round(vol, 2), Math.Round(heures, 2),
            L.T("trajet.phrase", ("km", L.Distance(t.Kilometres)), ("vol", L.Distance(vol)), ("duree", duree), ("jours", jours)),
            t.Cases);
    }
}
