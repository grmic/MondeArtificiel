using Monde.Simulation.Monde;

namespace Monde.Simulation.Humains;

/// <summary>
/// Intelligence, sur l'échelle des tests de QI : moyenne 100, écart-type 15 dans la population de départ.
/// Un facteur général (g) et des aptitudes qui lui sont liées sans s'y réduire (modèle de Cattell-Horn-Carroll).
/// </summary>
/// <param name="Generale">Intelligence générale (g) : comprendre, raisonner, apprendre.</param>
/// <param name="Verbale">Communiquer, nommer, imiter des sons ou des gestes (utile quand un langage naîtra).</param>
/// <param name="Spatiale">S'orienter, se représenter les lieux et les formes.</param>
/// <param name="Manuelle">Adresse des mains et du corps : chasser, plus tard façonner.</param>
/// <param name="Memoire">Retenir : nombre de lieux gardés en tête et vitesse d'oubli.</param>
/// <param name="Artistique">Sens artistique (formes, rythmes, couleurs) ; quelques rares individus sont exceptionnels.</param>
public sealed record Intelligence(double Generale, double Verbale, double Spatiale, double Manuelle, double Memoire,
    double Artistique)
{
    public static readonly Intelligence Moyenne = new(100, 100, 100, 100, 100, 100);

    /// <summary>Classification de Wechsler (WAIS-IV), utilisée pour décrire un score à l'observateur.</summary>
    public static string Qualifier(double score) => score switch
    {
        < 70 => "très faible",
        < 80 => "limite",
        < 90 => "moyenne faible",
        < 110 => "moyenne",
        < 120 => "moyenne forte",
        < 130 => "supérieure",
        < 145 => "très supérieure",
        _ => "exceptionnelle",
    };
}

/// <summary>
/// Tirage et héritage du caractère et de l'intelligence.
/// <para>
/// Les cinq grands traits (modèle des Big Five, Costa et McCrae) et la transcendance de soi (Cloninger)
/// vont de 0 à 1, autour de 0,5 (écart-type 0,15). Ils sont transmis à environ 40 %
/// (méta-analyse de Vukasović et Bratko, 2015) ; l'intelligence générale à environ 50 % à l'âge adulte
/// (Plomin et Deary, 2015). L'ouverture est un peu liée à l'intelligence (r ≈ 0,3 ; Ackerman et Heggestad, 1997).
/// </para>
/// <para>
/// Héritage selon le modèle de Falconer : écart à la moyenne de l'enfant = h² × écart moyen des parents,
/// plus une part propre d'écart-type √(1 − h⁴/2), ce qui garde la variété de la population d'une génération à l'autre.
/// </para>
/// </summary>
public static class Psyche
{
    private const double EcartTrait = 0.15;
    private const double HeritabiliteTraits = 0.4;
    private const double HeritabiliteIntelligence = 0.5;
    private const double LienOuvertureIntelligence = 0.3;
    // Part de chaque aptitude expliquée par g (saturations typiques des batteries de tests).
    private const double SaturationAptitudes = 0.7;
    private const double SaturationMemoire = 0.6;
    // Rares talents artistiques hors du commun : environ 1 humain sur 200 reçoit un élan supplémentaire.
    private const double ChanceTalentExceptionnel = 0.005;

    /// <summary>Caractère d'un humain de départ, créé par l'Entité.</summary>
    public static Personnalite Tirer(Alea alea, double attachement)
    {
        var z = new Ecarts(
            G: Biologie.Normale(alea),
            ResteOuverture: Biologie.Normale(alea),
            Conscience: Biologie.Normale(alea),
            Extraversion: Biologie.Normale(alea),
            Agreabilite: Biologie.Normale(alea),
            Nevrosisme: Biologie.Normale(alea),
            Transcendance: Biologie.Normale(alea),
            Endurance: Biologie.Normale(alea),
            ResteVerbale: Biologie.Normale(alea),
            ResteSpatiale: Biologie.Normale(alea),
            ResteManuelle: Biologie.Normale(alea),
            ResteMemoire: Biologie.Normale(alea),
            Art: Biologie.Normale(alea) + Talent(alea));
        return Composer(z, attachement);
    }

    /// <summary>Caractère d'un nouveau-né : une part héritée de ses parents, une part propre.</summary>
    public static Personnalite Heriter(Humain mere, Humain? pere, Alea alea, double attachement)
    {
        var m = Decomposer(mere.Personnalite);
        var p = pere is null ? m : Decomposer(pere.Personnalite);
        double Transmettre(double a, double b, double h2) => h2 * (a + b) / 2 + Math.Sqrt(1 - h2 * h2 / 2) * Biologie.Normale(alea);
        double T(double a, double b) => Transmettre(a, b, HeritabiliteTraits);
        double I(double a, double b) => Transmettre(a, b, HeritabiliteIntelligence);
        var z = new Ecarts(
            G: I(m.G, p.G),
            ResteOuverture: T(m.ResteOuverture, p.ResteOuverture),
            Conscience: T(m.Conscience, p.Conscience),
            Extraversion: T(m.Extraversion, p.Extraversion),
            Agreabilite: T(m.Agreabilite, p.Agreabilite),
            Nevrosisme: T(m.Nevrosisme, p.Nevrosisme),
            Transcendance: T(m.Transcendance, p.Transcendance),
            Endurance: T(m.Endurance, p.Endurance),
            ResteVerbale: I(m.ResteVerbale, p.ResteVerbale),
            ResteSpatiale: I(m.ResteSpatiale, p.ResteSpatiale),
            ResteManuelle: I(m.ResteManuelle, p.ResteManuelle),
            ResteMemoire: I(m.ResteMemoire, p.ResteMemoire),
            Art: T(m.Art, p.Art) + Talent(alea));
        return Composer(z, attachement);
    }

    private static double Talent(Alea alea) => alea.Reel() < ChanceTalentExceptionnel ? 1.5 + Math.Abs(Biologie.Normale(alea)) : 0;

    /// <summary>Écarts à la moyenne (en écarts-types) : la forme sous laquelle les traits se transmettent.</summary>
    private sealed record Ecarts(double G, double ResteOuverture, double Conscience, double Extraversion, double Agreabilite,
        double Nevrosisme, double Transcendance, double Endurance, double ResteVerbale, double ResteSpatiale,
        double ResteManuelle, double ResteMemoire, double Art);

    private static double Trait(double z) => Math.Clamp(0.5 + EcartTrait * z, 0, 1);
    private static double VersZ(double trait) => (trait - 0.5) / EcartTrait;
    private static double Qi(double z) => Math.Clamp(100 + 15 * z, 40, 175);
    private static double VersZQi(double qi) => (qi - 100) / 15;
    private static double Lie(double g, double reste, double saturation) => saturation * g + Math.Sqrt(1 - saturation * saturation) * reste;
    private static double Reste(double total, double g, double saturation) => (total - saturation * g) / Math.Sqrt(1 - saturation * saturation);

    private static Personnalite Composer(Ecarts z, double attachement) => new(
        Ouverture: Trait(Lie(z.G, z.ResteOuverture, LienOuvertureIntelligence)),
        Conscience: Trait(z.Conscience),
        Extraversion: Trait(z.Extraversion),
        Agreabilite: Trait(z.Agreabilite),
        Nevrosisme: Trait(z.Nevrosisme),
        Transcendance: Trait(z.Transcendance),
        Attachement: attachement,
        Endurance: Trait(z.Endurance),
        Intelligence: new Intelligence(
            Generale: Qi(z.G),
            Verbale: Qi(Lie(z.G, z.ResteVerbale, SaturationAptitudes)),
            Spatiale: Qi(Lie(z.G, z.ResteSpatiale, SaturationAptitudes)),
            Manuelle: Qi(Lie(z.G, z.ResteManuelle, SaturationAptitudes)),
            Memoire: Qi(Lie(z.G, z.ResteMemoire, SaturationMemoire)),
            // Le sens artistique doit un peu à l'ouverture et à g, beaucoup à lui-même.
            Artistique: Qi(0.35 * VersZ(Trait(Lie(z.G, z.ResteOuverture, LienOuvertureIntelligence))) + 0.2 * z.G + 0.91 * z.Art)));

    private static Ecarts Decomposer(Personnalite p)
    {
        var i = p.Intelligence;
        var g = VersZQi(i.Generale);
        var zOuverture = VersZ(p.Ouverture);
        var zArt = (VersZQi(i.Artistique) - 0.35 * zOuverture - 0.2 * g) / 0.91;
        return new Ecarts(g, Reste(zOuverture, g, LienOuvertureIntelligence), VersZ(p.Conscience), VersZ(p.Extraversion),
            VersZ(p.Agreabilite), VersZ(p.Nevrosisme), VersZ(p.Transcendance), VersZ(p.Endurance),
            Reste(VersZQi(i.Verbale), g, SaturationAptitudes), Reste(VersZQi(i.Spatiale), g, SaturationAptitudes),
            Reste(VersZQi(i.Manuelle), g, SaturationAptitudes), Reste(VersZQi(i.Memoire), g, SaturationMemoire),
            // Le talent hors norme d'un parent n'est transmis qu'en partie (il est compté dans l'écart, puis réduit par h²).
            zArt);
    }

    /// <summary>Description courte d'un trait de 0 à 1 pour l'observateur.</summary>
    public static string Niveau(double trait) => trait switch
    {
        < 0.25 => "très bas",
        < 0.4 => "bas",
        < 0.6 => "moyen",
        < 0.75 => "élevé",
        _ => "très élevé",
    };
}
