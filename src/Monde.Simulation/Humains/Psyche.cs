using Monde.Simulation.Monde;

using Monde.Simulation.Recits;

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

/// <summary>Épreuve de la vie qui peut laisser une trace durable sur le caractère.</summary>
public enum Epreuve
{
    /// <summary>Mort d'un enfant, d'un compagnon, d'un parent ou d'un ami très proche.</summary>
    Deuil,
    /// <summary>Combat ou raid où des siens sont morts.</summary>
    Guerre,
    /// <summary>Chassé de sa communauté ou renié par les siens.</summary>
    Exil,
    /// <summary>Trahi par son compagnon (infidélité découverte).</summary>
    Trahison,
}

/// <summary>
/// Tirage, héritage et évolution du caractère et de l'intelligence.
/// <para>
/// Les traits vont de 0 à 1, autour de 0,5 (écart-type 0,15). Ils ne sont pas tirés indépendamment : un même tirage
/// multivarié respecte les corrélations établies par la recherche.
/// <list type="bullet">
/// <item>Big Five entre eux : méta-analyse de van der Linden, te Nijenhuis et Bakker (2010, 212 échantillons, N = 144 117),
/// valeurs corrigées : agréabilité, conscience et stabilité émotionnelle vont ensemble (« stabilité »), extraversion et ouverture
/// aussi (« plasticité »).</item>
/// <item>Ouverture et intelligence : r ≈ 0,3 (Ackerman et Heggestad, 1997).</item>
/// <item>Honnêteté-humilité et Big Five : surtout l'agréabilité (≈ 0,3) et la conscience (≈ 0,2), rien avec le névrosisme
/// (Lee et Ashton, 2005).</item>
/// <item>Empathie émotionnelle (souci empathique) et cognitive (prise de perspective) : liées surtout à l'agréabilité (≈ 0,35),
/// la cognitive aussi à la conscience et à l'ouverture, un peu moins anxieuse (Melchers et al., 2016, échantillon groupé de
/// quatre pays) ; entre elles r ≈ 0,33 (Davis, 1983) ; la cognitive un peu liée à l'intelligence (≈ 0,2, ordre de grandeur).</item>
/// <item>Compassion : aspect de l'agréabilité (DeYoung, Quilty et Peterson, 2007), nourrie par l'empathie émotionnelle.</item>
/// <item>Psychopathie : modèle triarchique (Patrick, Fowles et Krueger, 2009) ; très liée à une faible honnêteté-humilité
/// (r = −0,72 ; Lee et Ashton, 2005), à une faible agréabilité (−0,39), à peu d'empathie et de compassion, un peu à une faible
/// conscience et à une faible anxiété (audace).</item>
/// <item>Attachement adulte : l'anxiété va avec le névrosisme (0,42), l'évitement avec peu d'extraversion et d'agréabilité ;
/// anxiété et évitement r = 0,22 (Noftle et Shaver, 2006, étude 1).</item>
/// <item>Fondements moraux (Graham et al., 2011) : le soin est lié au souci empathique (r = 0,59) ; les fondements liants
/// (loyauté, autorité, pureté) vont ensemble et vont avec la conscience et une faible ouverture (Hirsh et al., 2010) ;
/// la pureté avec la transcendance (ordre de grandeur).</item>
/// </list>
/// Les valeurs données comme « ordre de grandeur » sont des choix prudents, faute de méta-analyse ; la matrice est vérifiée
/// définie positive par les tests.
/// </para>
/// <para>
/// Héritage selon le modèle de Falconer : écart à la moyenne de l'enfant = h² × écart moyen des parents,
/// plus une part propre d'écart-type √(1 − h⁴/2) tirée avec les mêmes corrélations, ce qui garde la variété et la cohérence
/// de la population d'une génération à l'autre.
/// </para>
/// </summary>
public static class Psyche
{
    private const double EcartTrait = 0.15;

    // Variables liées par la matrice de corrélations.
    private const int G = 0, O = 1, C = 2, E = 3, A = 4, N = 5, T = 6, H = 7, EE = 8, EC = 9, Cp = 10, Psy = 11,
        AnxA = 12, EvA = 13, Soin = 14, Equ = 15, Loy = 16, Aut = 17, Pur = 18, NbLies = 19;
    // Variables connues dans les mondes enregistrés avant la version 18 : les autres en sont déduites (voir Completer).
    private const int NbAnciennes = 7;

    /// <summary>
    /// Héritabilités : Big Five ≈ 0,4 (Vukasović et Bratko, 2015) ; intelligence générale ≈ 0,5 à l'âge adulte (Plomin et Deary, 2015) ;
    /// honnêteté-humilité ≈ 0,46 (Kandler et al., 2019) ; empathie émotionnelle 0,48 et cognitive 0,27 (Abramson et al., 2020) ;
    /// anxiété d'attachement 0,45 et évitement 0,36 (Picardi et al., 2011) ; psychopathie : modèle génétique additif sans
    /// environnement partagé à l'âge adulte (Slawinski et al., méta-analyse), environ la moitié ; fondements moraux : 0,49 pour
    /// les individualisants, davantage pour les liants mais surtout l'environnement selon d'autres études (Zakharin et Bates, 2022 ;
    /// Kandler et al., 2019) : 0,4.
    /// </summary>
    private static readonly double[] Heritabilites =
        [0.5, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.46, 0.48, 0.27, 0.4, 0.5, 0.45, 0.36, 0.4, 0.4, 0.4, 0.4, 0.4];
    private const double HeritabiliteTraits = 0.4;
    private const double HeritabiliteIntelligence = 0.5;
    // Part de chaque aptitude expliquée par g (saturations typiques des batteries de tests).
    private const double SaturationAptitudes = 0.7;
    private const double SaturationMemoire = 0.6;
    // Rares talents artistiques hors du commun : environ 1 humain sur 200 reçoit un élan supplémentaire.
    private const double ChanceTalentExceptionnel = 0.005;

    /// <summary>
    /// Les hommes sont plus souvent psychopathes que les femmes : 7,9 % contre 2,9 % selon la méta-analyse de Sanz-García et al. (2021),
    /// rapport ≈ 2,7. Un décalage de ±0,2 écart-type autour de la moyenne donne un rapport ≈ 2,9.
    /// </summary>
    private const double EcartSexePsychopathie = 0.2;

    /// <summary>
    /// Seuil (en écarts-types) au-delà duquel un adulte est psychopathe : ≈ 1,2 % de la population, la prévalence mesurée
    /// avec l'échelle de Hare (PCL-R) dans la population générale (Sanz-García et al., 2021).
    /// </summary>
    internal const double SeuilPsychopathe = 2.30;

    /// <summary>Âge à partir duquel on parle de psychopathie (la PCL-R est un instrument pour adultes).</summary>
    private const double AgePsychopathie = 18;

    // Seuils d'anxiété et d'évitement (écarts-types) qui donnent 56 % de sûrs, 25 % d'évitants, 19 % d'anxieux (Hazan et Shaver, 1987).
    private const double SeuilAnxiete = 0.70;
    private const double SeuilEvitement = 0.55;

    private static readonly double[,] Correlations = Construire();
    private static readonly double[,] Facteur = Cholesky(Correlations);
    private static readonly (double[,] Regression, double[,] Facteur) Conditionnel = Conditionner(Correlations);

    private static double[,] Construire()
    {
        var r = new double[NbLies, NbLies];
        for (var i = 0; i < NbLies; i++) r[i, i] = 1;
        void L(int a, int b, double v) => r[a, b] = r[b, a] = v;
        // Big Five : van der Linden et al. (2010), corrélations corrigées.
        L(N, E, -.36); L(N, O, -.17); L(N, A, -.36); L(N, C, -.43);
        L(E, O, .43); L(E, A, .26); L(E, C, .29); L(O, A, .21); L(O, C, .20); L(A, C, .43);
        L(G, O, .30);
        // Transcendance et ouverture (ordre de grandeur).
        L(T, O, .25);
        // Honnêteté-humilité (Lee et Ashton, 2005).
        L(H, A, .30); L(H, C, .20);
        // Empathies (Melchers et al., 2016 ; Davis, 1983).
        L(EE, A, .35); L(EE, E, .15); L(EE, C, .14); L(EE, O, .09); L(EE, N, .07);
        L(EC, A, .33); L(EC, E, .12); L(EC, O, .20); L(EC, C, .24); L(EC, N, -.15); L(EC, G, .20);
        L(EC, EE, .33); L(H, EE, .20); L(H, EC, .10);
        // Compassion, aspect de l'agréabilité (DeYoung et al., 2007).
        L(Cp, A, .80); L(Cp, EE, .60); L(Cp, EC, .35); L(Cp, E, .25); L(Cp, H, .30); L(Cp, C, .20); L(Cp, O, .15);
        // Psychopathie (Lee et Ashton, 2005 ; Patrick et al., 2009) : l'empathie cognitive est conservée.
        L(Psy, H, -.65); L(Psy, A, -.40); L(Psy, Cp, -.50); L(Psy, EE, -.45); L(Psy, C, -.25); L(Psy, N, -.20); L(Psy, E, .10);
        // Attachement adulte (Noftle et Shaver, 2006, étude 1).
        L(AnxA, N, .42); L(AnxA, E, -.15); L(AnxA, O, -.07); L(AnxA, A, -.19); L(AnxA, C, -.23);
        L(EvA, N, .14); L(EvA, E, -.21); L(EvA, O, -.09); L(EvA, A, -.22); L(EvA, C, -.20);
        L(AnxA, EvA, .22); L(EvA, Cp, -.20); L(EvA, EE, -.15); L(Psy, EvA, .15);
        // Fondements moraux (Graham et al., 2011 ; Hirsh et al., 2010).
        L(Soin, EE, .59); L(Soin, Cp, .55); L(Soin, A, .35); L(Soin, H, .20); L(Soin, Psy, -.40); L(Soin, EC, .25);
        L(Equ, Soin, .55); L(Equ, H, .25); L(Equ, A, .25); L(Equ, O, .15); L(Equ, Psy, -.30); L(Equ, EE, .35); L(Equ, Cp, .35);
        L(Loy, Aut, .60); L(Loy, Pur, .50); L(Aut, Pur, .60);
        L(Loy, Soin, .25); L(Loy, Equ, .15); L(Aut, Soin, .20); L(Aut, Equ, .10); L(Pur, Soin, .25); L(Pur, Equ, .15);
        L(Loy, C, .15); L(Aut, C, .20); L(Pur, C, .15); L(Loy, O, -.20); L(Aut, O, -.25); L(Pur, O, -.25);
        L(Loy, A, .15); L(Aut, A, .15); L(Pur, T, .30);
        return r;
    }

    /// <summary>Corrélation entre deux variables latentes (pour les tests).</summary>
    internal static double[,] MatriceCorrelations => (double[,])Correlations.Clone();

    /// <summary>Caractère d'un humain de départ, créé par l'Entité.</summary>
    public static Personnalite Tirer(Alea alea, double attachement, Sexe? sexe = null)
    {
        var z = Correles(alea);
        var propres = Propres(alea);
        propres[^1] += Talent(alea);
        return Composer(z, propres, attachement, sexe);
    }

    /// <summary>
    /// Caractère d'un nouveau-né : une part héritée de ses parents, une part propre. <paramref name="maintenant"/> permet
    /// d'ôter aux parents ce que l'âge a changé en eux (la maturité ne se transmet pas).
    /// </summary>
    public static Personnalite Heriter(Humain mere, Humain? pere, Alea alea, double attachement, Sexe? sexe = null, long? maintenant = null)
    {
        var (m, mp) = Decomposer(mere, maintenant);
        var (p, pp) = pere is null ? (m, mp) : Decomposer(pere, maintenant);
        var part = Correles(alea);
        var z = new double[NbLies];
        for (var i = 0; i < NbLies; i++)
        {
            var h2 = Heritabilites[i];
            z[i] = h2 * (m[i] + p[i]) / 2 + Math.Sqrt(1 - h2 * h2 / 2) * part[i];
        }
        var propres = Propres(alea);
        for (var i = 0; i < propres.Length; i++)
        {
            // L'endurance et le sens artistique comme les traits, les aptitudes comme l'intelligence.
            var h2 = i is 0 or 5 ? HeritabiliteTraits : HeritabiliteIntelligence;
            propres[i] = h2 * (mp[i] + pp[i]) / 2 + Math.Sqrt(1 - h2 * h2 / 2) * propres[i];
        }
        propres[^1] += Talent(alea);
        return Composer(z, propres, attachement, sexe);
    }

    /// <summary>
    /// Humain d'un monde enregistré avant ces traits : ils sont tirés selon la loi conditionnelle aux traits qu'il a déjà
    /// (un humain très agréable reçoit le plus souvent une compassion élevée, un humain très anxieux un attachement anxieux…).
    /// </summary>
    public static Personnalite Completer(Personnalite p, Sexe sexe, Alea alea)
    {
        var connus = new double[NbAnciennes];
        connus[G] = VersZQi(p.Intelligence.Generale);
        connus[O] = VersZ(p.Ouverture);
        connus[C] = VersZ(p.Conscience);
        connus[E] = VersZ(p.Extraversion);
        connus[A] = VersZ(p.Agreabilite);
        connus[N] = VersZ(p.Nevrosisme);
        connus[T] = VersZ(p.Transcendance);
        var (b, f) = Conditionnel;
        var n = NbLies - NbAnciennes;
        var eps = new double[n];
        for (var i = 0; i < n; i++) eps[i] = Biologie.Normale(alea);
        var z = new double[NbLies];
        Array.Copy(connus, z, NbAnciennes);
        for (var i = 0; i < n; i++)
        {
            var v = 0.0;
            for (var j = 0; j < NbAnciennes; j++) v += b[i, j] * connus[j];
            for (var j = 0; j <= i; j++) v += f[i, j] * eps[j];
            z[NbAnciennes + i] = v;
        }
        return p with { Moral = Moraux(z, sexe) };
    }

    private static double Talent(Alea alea) => alea.Reel() < ChanceTalentExceptionnel ? 1.5 + Math.Abs(Biologie.Normale(alea)) : 0;

    /// <summary>Tirage normal multivarié : L × ε, où L est le facteur de Cholesky de la matrice de corrélations.</summary>
    private static double[] Correles(Alea alea)
    {
        var eps = new double[NbLies];
        for (var i = 0; i < NbLies; i++) eps[i] = Biologie.Normale(alea);
        var z = new double[NbLies];
        for (var i = 0; i < NbLies; i++)
        {
            var v = 0.0;
            for (var j = 0; j <= i; j++) v += Facteur[i, j] * eps[j];
            z[i] = v;
        }
        return z;
    }

    /// <summary>Parts indépendantes : endurance, restes des aptitudes verbale, spatiale, manuelle, de mémoire, sens artistique.</summary>
    private static double[] Propres(Alea alea) => [.. Enumerable.Range(0, 6).Select(_ => Biologie.Normale(alea))];

    private static double Trait(double z) => Math.Clamp(0.5 + EcartTrait * z, 0, 1);
    internal static double VersZ(double trait) => (trait - 0.5) / EcartTrait;
    private static double Qi(double z) => Math.Clamp(100 + 15 * z, 40, 175);
    private static double VersZQi(double qi) => (qi - 100) / 15;
    private static double Lie(double g, double reste, double saturation) => saturation * g + Math.Sqrt(1 - saturation * saturation) * reste;
    private static double Reste(double total, double g, double saturation) => (total - saturation * g) / Math.Sqrt(1 - saturation * saturation);
    private static double DecalagePsychopathie(Sexe? sexe) => sexe switch
    {
        Sexe.Homme => EcartSexePsychopathie,
        Sexe.Femme => -EcartSexePsychopathie,
        _ => 0,
    };

    private static TraitsMoraux Moraux(double[] z, Sexe? sexe) => new(
        Honnetete: Trait(z[H]),
        EmpathieEmotionnelle: Trait(z[EE]),
        EmpathieCognitive: Trait(z[EC]),
        Compassion: Trait(z[Cp]),
        Psychopathie: Trait(z[Psy] + DecalagePsychopathie(sexe)),
        AnxieteAffective: Trait(z[AnxA]),
        EvitementAffectif: Trait(z[EvA]),
        Fondements: new FondementsMoraux(Trait(z[Soin]), Trait(z[Equ]), Trait(z[Loy]), Trait(z[Aut]), Trait(z[Pur])));

    private static Personnalite Composer(double[] z, double[] propres, double attachement, Sexe? sexe)
    {
        var g = z[G];
        var ouverture = Trait(z[O]);
        return new Personnalite(
            Ouverture: ouverture,
            Conscience: Trait(z[C]),
            Extraversion: Trait(z[E]),
            Agreabilite: Trait(z[A]),
            Nevrosisme: Trait(z[N]),
            Transcendance: Trait(z[T]),
            Attachement: attachement,
            Endurance: Trait(propres[0]),
            Intelligence: new Intelligence(
                Generale: Qi(g),
                Verbale: Qi(Lie(g, propres[1], SaturationAptitudes)),
                Spatiale: Qi(Lie(g, propres[2], SaturationAptitudes)),
                Manuelle: Qi(Lie(g, propres[3], SaturationAptitudes)),
                Memoire: Qi(Lie(g, propres[4], SaturationMemoire)),
                // Le sens artistique doit un peu à l'ouverture et à g, beaucoup à lui-même.
                Artistique: Qi(0.35 * VersZ(ouverture) + 0.2 * g + 0.91 * propres[5])))
        {
            Moral = Moraux(z, sexe),
        };
    }

    /// <summary>Écarts à la moyenne (en écarts-types) d'un parent : la forme sous laquelle les traits se transmettent.</summary>
    private static (double[] Lies, double[] Propres) Decomposer(Humain parent, long? maintenant)
    {
        var p = parent.Personnalite;
        var i = p.Intelligence;
        var m = p.Moral;
        var g = VersZQi(i.Generale);
        var z = new double[NbLies];
        z[G] = g;
        z[O] = VersZ(p.Ouverture);
        z[C] = VersZ(p.Conscience);
        z[E] = VersZ(p.Extraversion);
        z[A] = VersZ(p.Agreabilite);
        z[N] = VersZ(p.Nevrosisme);
        z[T] = VersZ(p.Transcendance);
        z[H] = VersZ(m.Honnetete);
        z[EE] = VersZ(m.EmpathieEmotionnelle);
        z[EC] = VersZ(m.EmpathieCognitive);
        z[Cp] = VersZ(m.Compassion);
        z[Psy] = VersZ(m.Psychopathie) - DecalagePsychopathie(parent.Sexe);
        z[AnxA] = VersZ(m.AnxieteAffective);
        z[EvA] = VersZ(m.EvitementAffectif);
        z[Soin] = VersZ(m.Fondements.Soin);
        z[Equ] = VersZ(m.Fondements.Equite);
        z[Loy] = VersZ(m.Fondements.Loyaute);
        z[Aut] = VersZ(m.Fondements.Autorite);
        z[Pur] = VersZ(m.Fondements.Purete);
        if (maintenant is { } t)
        {
            var (dc, dn, da) = MaturationAttendue(parent.AgeEnAnnees(t));
            z[C] -= dc;
            z[N] -= dn;
            z[A] -= da;
        }
        var zArt = (VersZQi(i.Artistique) - 0.35 * z[O] - 0.2 * g) / 0.91;
        // Le talent hors norme d'un parent n'est transmis qu'en partie (il est compté dans l'écart, puis réduit par h²).
        double[] propres = [VersZ(p.Endurance), Reste(VersZQi(i.Verbale), g, SaturationAptitudes), Reste(VersZQi(i.Spatiale), g, SaturationAptitudes),
            Reste(VersZQi(i.Manuelle), g, SaturationAptitudes), Reste(VersZQi(i.Memoire), g, SaturationMemoire), zArt];
        return (z, propres);
    }

    // ── Maturité et épreuves ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Changement moyen par an (en écarts-types) de la conscience, du névrosisme et de l'agréabilité à un âge donné.
    /// Roberts, Walton et Viechtbauer (2006, 92 échantillons longitudinaux) : la conscience monte surtout entre 20 et 40 ans
    /// (d ≈ 0,22 de 20 à 30 ans, 0,26 de 30 à 40), plus lentement ensuite ; la stabilité émotionnelle progresse par petits pas
    /// de l'adolescence à la trentaine ; l'agréabilité change peu jusqu'à la vieillesse, où elle monte.
    /// Les rythmes hors de ces repères chiffrés sont des ordres de grandeur.
    /// </summary>
    private static (double C, double N, double A) MaturationParAn(double age) => (
        C: age switch { < 18 => 0, < 30 => 0.022, < 40 => 0.026, < 60 => 0.01, _ => 0 },
        N: age is >= 15 and < 40 ? -0.015 : 0,
        A: age >= 50 ? 0.01 : 0);

    /// <summary>Changement moyen cumulé depuis la naissance jusqu'à cet âge.</summary>
    internal static (double C, double N, double A) MaturationAttendue(double age)
    {
        double c = 0, n = 0, a = 0;
        for (var annee = 0; annee < Math.Floor(age); annee++)
        {
            var (dc, dn, da) = MaturationParAn(annee);
            (c, n, a) = (c + dc, n + dn, a + da);
        }
        return (c, n, a);
    }

    /// <summary>
    /// Une année de plus : la maturité avance, d'un pas propre à chacun cette année-là (en moyenne celui de la recherche).
    /// </summary>
    public static Personnalite Murir(Personnalite p, double age, Alea alea)
    {
        var (dc, dn, da) = MaturationParAn(age);
        if (dc == 0 && dn == 0 && da == 0) return p;
        var rythme = Math.Max(0, 1 + 0.5 * Biologie.Normale(alea));
        return p with
        {
            Conscience = Trait(VersZ(p.Conscience) + dc * rythme),
            Nevrosisme = Trait(VersZ(p.Nevrosisme) + dn * rythme),
            Agreabilite = Trait(VersZ(p.Agreabilite) + da * rythme),
        };
    }

    /// <summary>
    /// Trace durable d'une épreuve (0 &lt; intensité ≤ 1) : les événements de vie pénibles augmentent le névrosisme, d'autant plus
    /// qu'on y était déjà sensible (Jeronimus et al., 2014) ; la perte et la trahison rendent plus anxieux dans les liens,
    /// l'exil plus méfiant de la proximité. Rend la nouvelle personnalité et la clé d'une phrase pour le jardin secret, ou null si
    /// rien n'a bougé.
    /// </summary>
    public static (Personnalite Personnalite, string? Trace) Eprouver(Personnalite p, Epreuve epreuve, double intensite, Alea alea)
    {
        intensite = Math.Clamp(intensite, 0, 1);
        if (intensite <= 0) return (p, null);
        // Sensibilité propre à cette épreuve : névrosisme, et une part de hasard (la même perte n'atteint pas tous pareil).
        var choc = intensite * (0.5 + p.Nevrosisme) * Math.Max(0.2, 1 + 0.4 * Biologie.Normale(alea));
        var m = p.Moral;
        var (dn, dAnx, dEv) = epreuve switch
        {
            Epreuve.Deuil => (0.12, 0.08, 0.0),
            Epreuve.Guerre => (0.10, 0.0, 0.05),
            Epreuve.Exil => (0.15, 0.05, 0.12),
            _ => (0.08, 0.10, 0.06),
        };
        var nouvelle = p with
        {
            Nevrosisme = Trait(VersZ(p.Nevrosisme) + dn * choc),
            Moral = m with
            {
                AnxieteAffective = Trait(VersZ(m.AnxieteAffective) + dAnx * choc),
                EvitementAffectif = Trait(VersZ(m.EvitementAffectif) + dEv * choc),
            },
        };
        if (choc < 0.4) return (nouvelle, null);
        return (nouvelle, $"js.epreuve.{(epreuve is Epreuve.Deuil or Epreuve.Guerre or Epreuve.Exil ? epreuve : Epreuve.Trahison)}");
    }

    // ── Lecture des traits ──────────────────────────────────────────────────────────────────

    /// <summary>Style d'attachement d'après l'anxiété et l'évitement (traits de 0 à 1).</summary>
    public static StyleAttachement Style(double anxiete, double evitement)
    {
        var (za, ze) = (VersZ(anxiete) - SeuilAnxiete, VersZ(evitement) - SeuilEvitement);
        if (za < 0 && ze < 0) return StyleAttachement.Secure;
        return ze > za ? StyleAttachement.Evitant : StyleAttachement.Anxieux;
    }

    /// <summary>Adulte au haut du continuum de psychopathie (≈ 1,2 % des adultes).</summary>
    public static bool EstPsychopathe(Humain h, long maintenant) =>
        h.AgeEnAnnees(maintenant) >= AgePsychopathie && VersZ(h.Personnalite.Moral.Psychopathie) >= SeuilPsychopathe;

    /// <summary>Penchant psychopathique de 0 (aucun) à 1 (psychopathe) : ce qui pèse sur les gestes, même sous le seuil.</summary>
    public static double Froideur(Personnalite p) => Math.Clamp((VersZ(p.Moral.Psychopathie) - 0.5) / (SeuilPsychopathe - 0.5), 0, 1);

    /// <summary>Description courte d'un trait de 0 à 1 pour l'observateur.</summary>
    public static string Niveau(double trait) => trait switch
    {
        < 0.25 => "très bas",
        < 0.4 => "bas",
        < 0.6 => "moyen",
        < 0.75 => "élevé",
        _ => "très élevé",
    };

    // ── Algèbre ─────────────────────────────────────────────────────────────────────────────

    private static double[,] Cholesky(double[,] r)
    {
        var n = r.GetLength(0);
        var l = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var s = r[i, j];
                for (var k = 0; k < j; k++) s -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (s <= 0) throw new InvalidOperationException("La matrice de corrélations des traits n'est pas définie positive.");
                    l[i, i] = Math.Sqrt(s);
                }
                else
                {
                    l[i, j] = s / l[j, j];
                }
            }
        }
        return l;
    }

    /// <summary>
    /// Loi des nouveaux traits sachant les anciens : moyenne B × z_connus avec B = Σ_nc Σ_cc⁻¹,
    /// covariance Σ_nn − B Σ_cn (factorisée par Cholesky).
    /// </summary>
    private static (double[,], double[,]) Conditionner(double[,] r)
    {
        const int k = NbAnciennes;
        const int n = NbLies - NbAnciennes;
        // Inverse de Σ_cc par Gauss-Jordan (7 × 7, bien conditionnée).
        var inv = new double[k, k];
        var a = new double[k, 2 * k];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) a[i, j] = r[i, j];
            a[i, k + i] = 1;
        }
        for (var c = 0; c < k; c++)
        {
            var pivot = a[c, c];
            for (var j = 0; j < 2 * k; j++) a[c, j] /= pivot;
            for (var i = 0; i < k; i++)
            {
                if (i == c) continue;
                var f = a[i, c];
                for (var j = 0; j < 2 * k; j++) a[i, j] -= f * a[c, j];
            }
        }
        for (var i = 0; i < k; i++) for (var j = 0; j < k; j++) inv[i, j] = a[i, k + j];

        var b = new double[n, k];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < k; j++)
                for (var m = 0; m < k; m++) b[i, j] += r[k + i, m] * inv[m, j];
        var cov = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                var v = r[k + i, k + j];
                for (var m = 0; m < k; m++) v -= b[i, m] * r[m, k + j];
                cov[i, j] = v;
            }
        return (b, Cholesky(cov));
    }
}
