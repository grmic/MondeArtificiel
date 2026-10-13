using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Traits physiques innés, en partie hérités. Symétrie et harmonie des traits (proches de la moyenne) plaisent dans toutes
/// les cultures ; taille et teint plaisent selon l'idéal de chaque communauté ; l'allure (voix, démarche, regard) est un
/// charme propre à chacun.
/// </summary>
/// <param name="Symetrie">Symétrie du corps et du visage, de 0 à 1.</param>
/// <param name="Harmonie">Harmonie des traits (proches des traits moyens), de 0 à 1.</param>
/// <param name="Taille">Taille en écarts-types autour de la moyenne de son sexe (−3 à 3).</param>
/// <param name="Teint">Teint de la peau, de 0 (clair) à 1 (sombre).</param>
/// <param name="Allure">Charme de la voix, de la démarche, du regard, de 0 à 1.</param>
public sealed record Apparence(double Symetrie, double Harmonie, double Taille, double Teint, double Allure);

/// <summary>
/// Orientation sexuelle : deux attirances, de 0 à 1, l'une vers les hommes, l'autre vers les femmes. Ce n'est pas un
/// choix entre deux cases : la plupart sont attirés presque seulement par l'autre sexe, beaucoup un peu par les deux,
/// quelques-uns surtout par leur propre sexe.
/// </summary>
public sealed record Orientation(double VersHommes, double VersFemmes)
{
    public double Vers(Sexe sexe) => sexe == Sexe.Homme ? VersHommes : VersFemmes;
}

/// <summary>
/// Idéal de beauté d'une communauté : la taille et le teint qu'on y trouve beaux, et la corpulence (0 : maigre, 1 : très en chair).
/// </summary>
public sealed record IdealBeaute(double Taille, double Teint, double Corpulence)
{
    /// <summary>Idéal de celui qui ne vit dans aucune communauté : des traits moyens, un corps ni maigre ni gras.</summary>
    public static readonly IdealBeaute ParDefaut = new(0, 0.5, 0.45);
}

/// <summary>
/// Beauté, attirance et orientation. L'apparence pèse plus que tout le reste dans l'attirance première (Eastwick et Finkel,
/// 2008 ; Luo et Zhang, 2009). La beauté a une part universelle : symétrie, traits proches de la moyenne, signes de santé
/// (Langlois et al., 2000, méta-analyse : on s'accorde sur qui est beau, dans une culture et d'une culture à l'autre), et une
/// part propre à chaque groupe, qui change : on préfère ce qu'on voit souvent (Langlois et Roggman, 1990) et l'apparence de
/// ceux qu'on admire (Henrich et Gil-White, 2001), et là où la nourriture manque on préfère les corps en chair (Nelson et
/// Morrison, 2005 ; Swami et Tovée, 2005). Les hommes accordent plus de poids à la jeunesse de leur partenaire, les femmes
/// un peu plus à la vigueur et au statut (Buss, 1989, 37 cultures).
/// </summary>
public static class Attirance
{
    // ───────────── Tirage et hérédité ─────────────

    /// <summary>Héritabilités (part de la variation due aux parents) : taille 0,8 (Silventoinen et al., 2003), teint 0,8,
    /// beauté du visage environ 0,6 (Mitchem et al., 2014), symétrie environ 0,3, allure 0,4.</summary>
    private const double H2Taille = 0.8, H2Teint = 0.8, H2Harmonie = 0.6, H2Symetrie = 0.3, H2Allure = 0.4;

    public static Apparence TirerApparence(Alea alea) => new(
        Symetrie: Math.Clamp(0.5 + 0.15 * Biologie.Normale(alea), 0, 1),
        Harmonie: Math.Clamp(0.5 + 0.15 * Biologie.Normale(alea), 0, 1),
        Taille: Math.Clamp(Biologie.Normale(alea), -3, 3),
        Teint: Math.Clamp(0.5 + 0.2 * Biologie.Normale(alea), 0, 1),
        Allure: Math.Clamp(0.5 + 0.15 * Biologie.Normale(alea), 0, 1));

    public static Apparence HeriterApparence(Apparence mere, Apparence? pere, Alea alea)
    {
        double Moyenne(Func<Apparence, double> f) => pere is null ? f(mere) : (f(mere) + f(pere)) / 2;
        double Trait(double moyenne, double parents, double h2, double ecart, double min, double max) =>
            Math.Clamp(moyenne + h2 * (parents - moyenne) + Math.Sqrt(1 - h2 * h2 / 2) * ecart * Biologie.Normale(alea), min, max);
        return new Apparence(
            Trait(0.5, Moyenne(a => a.Symetrie), H2Symetrie, 0.15, 0, 1),
            Trait(0.5, Moyenne(a => a.Harmonie), H2Harmonie, 0.15, 0, 1),
            Trait(0, Moyenne(a => a.Taille), H2Taille, 1, -3, 3),
            Trait(0.5, Moyenne(a => a.Teint), H2Teint, 0.2, 0, 1),
            Trait(0.5, Moyenne(a => a.Allure), H2Allure, 0.15, 0, 1));
    }

    /// <summary>
    /// Orientation d'un humain. Proportions d'après les grandes enquêtes (Copen et al., 2016 ; revue de Bailey et al., 2016) :
    /// chez les hommes, environ 2 % homosexuels, 2 % bisexuels et 6 % « surtout hétérosexuels » ; chez les femmes, environ
    /// 1,5 % homosexuelles, 5,5 % bisexuelles et 13 % « surtout hétérosexuelles ». L'orientation est en partie héritée
    /// (environ un tiers chez les hommes, un cinquième chez les femmes : Långström et al., 2010) ; chez les garçons, chaque
    /// frère aîné de la même mère la rend un peu plus probablement homosexuelle (Blanchard et Bogaert, 1996).
    /// </summary>
    /// <param name="penchant">Attirance des parents pour leur propre sexe (0 : aucune), qui se transmet en partie.</param>
    /// <param name="freresAines">Frères aînés de même mère (garçons seulement).</param>
    public static Orientation TirerOrientation(Sexe sexe, Alea alea, double penchant = 0, int freresAines = 0)
    {
        var homme = sexe == Sexe.Homme;
        var h2 = homme ? 0.35 : 0.2;
        var transmis = 1 + 4 * h2 * Math.Clamp(penchant, 0, 1);
        var homo = (homme ? 0.02 : 0.015) * transmis * (homme ? Math.Pow(1.33, freresAines) : 1);
        var bi = (homme ? 0.02 : 0.055) * transmis;
        var plutot = (homme ? 0.06 : 0.13) * transmis;
        var u = alea.Reel();
        double meme, autre;
        if (u < homo) (meme, autre) = (0.75 + 0.25 * alea.Reel(), 0.3 * alea.Reel());
        else if (u < homo + bi) (meme, autre) = (0.5 + 0.5 * alea.Reel(), 0.5 + 0.5 * alea.Reel());
        else if (u < homo + bi + plutot) (meme, autre) = (0.15 + 0.3 * alea.Reel(), 0.6 + 0.4 * alea.Reel());
        else (meme, autre) = (alea.Reel() < 0.3 ? 0.1 * alea.Reel() : 0, 0.75 + 0.25 * alea.Reel());
        return homme ? new Orientation(meme, autre) : new Orientation(autre, meme);
    }

    /// <summary>Attirance d'un humain pour son propre sexe.</summary>
    public static double PourSonSexe(Humain h) => h.Orientation.Vers(h.Sexe);

    /// <summary>Traits d'un humain enregistré avant qu'ils existent : tirés de son numéro, pour qu'un même monde les retrouve.</summary>
    public static (Apparence Apparence, Orientation Orientation, double Lucidite) De(int id, Sexe sexe, double ouverture)
    {
        var alea = new Alea(0x617474697261UL ^ ((ulong)id * 0x9E3779B97F4A7C15UL));
        var orientation = TirerOrientation(sexe, alea);
        return (TirerApparence(alea), orientation, LuciditeInitiale(orientation.Vers(sexe), ouverture, alea));
    }

    /// <summary>
    /// Ce qu'un adulte sait de son attirance pour son propre sexe : rien à découvrir s'il n'en a guère ; sinon il peut la
    /// connaître, la deviner ou l'ignorer, d'autant mieux qu'il est ouvert.
    /// </summary>
    public static double LuciditeInitiale(double pourSonSexe, double ouverture, Alea alea) =>
        pourSonSexe < 0.15 ? 1 : Math.Clamp(0.1 + 0.4 * ouverture + 0.5 * alea.Reel() * pourSonSexe, 0, 1);

    // ───────────── Beauté et attractivité ─────────────

    /// <summary>Corpulence du moment, de 0 à 1, d'après les réserves du corps.</summary>
    public static double Corpulence(Humain h) => Math.Clamp(h.Reserves / 100, 0, 1);

    /// <summary>
    /// Beauté relative de <paramref name="h"/>, de 0 à 100, selon l'idéal d'une communauté : la part universelle (symétrie,
    /// harmonie), l'écart à l'idéal du groupe (taille, teint, corpulence) et l'éclat de la santé ; elle se fane avec l'âge.
    /// </summary>
    public static double Beaute(Humain h, IdealBeaute ideal, long maintenant)
    {
        var a = h.Apparence;
        var universelle = 0.5 * a.Symetrie + 0.5 * a.Harmonie;
        var ecart = 0.4 * Math.Min(1, Math.Abs(a.Taille - ideal.Taille) / 2.5)
                    + 0.3 * Math.Min(1, Math.Abs(a.Teint - ideal.Teint) * 1.5)
                    + 0.3 * Math.Min(1, Math.Abs(Corpulence(h) - ideal.Corpulence) * 1.5);
        var sante = Math.Clamp(h.Sante / 100, 0, 1);
        var age = h.AgeEnAnnees(maintenant);
        var fane = age <= 35 ? 1 : Math.Max(0.55, 1 - 0.012 * (age - 35));
        return 100 * Math.Clamp(0.5 * universelle + 0.35 * (1 - ecart) + 0.15 * sante, 0, 1) * fane;
    }

    /// <summary>
    /// Attractivité sexuelle de <paramref name="h"/>, de 0 à 100, aux yeux de quelqu'un qui partage l'idéal donné :
    /// la beauté d'abord, puis les signes de jeunesse (plus regardés chez les femmes), l'allure, et la vigueur (plus
    /// regardée chez les hommes).
    /// </summary>
    public static double Attractivite(Humain h, IdealBeaute ideal, long maintenant)
    {
        var age = h.AgeEnAnnees(maintenant);
        if (age < 14) return 0;
        var femme = h.Sexe == Sexe.Femme;
        var jeunesse = femme
            ? age switch { < 18 => 0.8, <= 28 => 1, _ => Math.Max(0, 1 - (age - 28) / 22) }
            : age switch { < 20 => 0.7, <= 38 => 1, _ => Math.Max(0.2, 1 - (age - 38) / 30) };
        var vigueur = femme
            ? Math.Clamp(h.Sante / 100, 0, 1)
            : Math.Clamp(0.4 * (0.5 + h.Apparence.Taille / 4) + 0.3 * h.Personnalite.Endurance + 0.3 * h.Sante / 100, 0, 1);
        return Math.Clamp(0.55 * Beaute(h, ideal, maintenant) + 100 * (0.2 * jeunesse + 0.15 * h.Apparence.Allure + 0.1 * vigueur), 0, 100);
    }

    /// <summary>
    /// Désir de <paramref name="h"/> pour <paramref name="autre"/>, de 0 à 1 : son orientation envers le sexe de l'autre,
    /// puis l'attractivité de l'autre à ses yeux (le poids le plus lourd), l'affection qu'il lui porte et son charisme.
    /// </summary>
    public static double Desir(Humain h, Humain autre, IdealBeaute idealDeH, long maintenant) =>
        h.Orientation.Vers(autre.Sexe)
        * Math.Min(1, 0.65 * Attractivite(autre, idealDeH, maintenant) / 100 + 0.2 * Math.Max(0, h.AffectionPour(autre.Id)) / 100
           + 0.15 * autre.Personnalite.Charisme + (h.Impulsions.Count > 0 ? h.Penchant(autre.Id, maintenant) : 0));

    /// <summary>
    /// Désir en dessous duquel on ne s'intéresse pas à quelqu'un : ceux qui plaisent beaucoup sont plus exigeants
    /// (on s'apparie surtout entre gens de même attrait : Feingold, 1988).
    /// </summary>
    public static double Exigence(Humain h, IdealBeaute ideal, long maintenant) =>
        Math.Clamp(0.3 + 0.3 * (Attractivite(h, ideal, maintenant) / 100 - 0.5), 0.15, 0.5);

    // ───────────── Idéal de beauté des communautés ─────────────

    /// <summary>Idéal d'un groupe neuf : celui du groupe dont il vient, sinon l'apparence moyenne de ses adultes.</summary>
    public static IdealBeaute IdealInitial(IReadOnlyCollection<Humain> adultes, IdealBeaute? origine) =>
        origine ?? (adultes.Count == 0
            ? IdealBeaute.ParDefaut
            : new IdealBeaute(adultes.Average(h => h.Apparence.Taille), adultes.Average(h => h.Apparence.Teint), IdealBeaute.ParDefaut.Corpulence));

    /// <summary>
    /// Une semaine passe : l'idéal glisse vers l'apparence moyenne du groupe et vers celle de sa figure ; la corpulence admirée
    /// monte quand la nourriture manque et redescend dans l'abondance.
    /// </summary>
    public static IdealBeaute Deriver(IdealBeaute ideal, IReadOnlyCollection<Humain> adultes, Humain? figure, bool disette, double abondance)
    {
        if (adultes.Count == 0) return ideal;
        var taille = ideal.Taille + 0.01 * (adultes.Average(h => h.Apparence.Taille) - ideal.Taille)
                     + (figure is null ? 0 : 0.008 * (figure.Apparence.Taille - ideal.Taille));
        var teint = ideal.Teint + 0.01 * (adultes.Average(h => h.Apparence.Teint) - ideal.Teint)
                    + (figure is null ? 0 : 0.008 * (figure.Apparence.Teint - ideal.Teint));
        var cible = disette ? 0.75 : abondance >= 0.9 ? 0.4 : 0.5;
        var corpulence = ideal.Corpulence + 0.02 * (cible - ideal.Corpulence);
        return new IdealBeaute(taille, teint, corpulence);
    }

    // ───────────── Mots pour la fiche ─────────────

    public static string Orienter(Humain h)
    {
        var e = h.Sexe == Sexe.Femme ? "le" : "";
        var meme = h.Orientation.Vers(h.Sexe);
        var autre = h.Orientation.Vers(h.Sexe == Sexe.Homme ? Sexe.Femme : Sexe.Homme);
        return (meme, autre) switch
        {
            ( < 0.12, _) => $"hétérosexuel{e}",
            ( < 0.5, _) when autre >= meme => $"surtout hétérosexuel{e}",
            ( >= 0.5, >= 0.5) => $"bisexuel{e}",
            ( >= 0.5, _) => $"homosexuel{e}",
            _ => "attiré" + (h.Sexe == Sexe.Femme ? "e" : "") + " par les deux sexes",
        };
    }

    /// <summary>Ce que l'humain sait de son attirance pour son propre sexe, en mots (null s'il n'y a rien à savoir).</summary>
    public static string? Conscience(Humain h, double toleranceDuGroupe)
    {
        if (Attirance.PourSonSexe(h) < 0.15) return null;
        var il = h.Sexe == Sexe.Femme ? "elle" : "il";
        return h.Lucidite switch
        {
            < 0.3 => $"{il} ne le sait pas",
            < 0.6 => $"{il} commence à le comprendre",
            _ when toleranceDuGroupe < 0.4 => $"{il} le sait et le cache",
            _ => $"{il} le sait",
        };
    }

    public static string DireIdeal(IdealBeaute i)
    {
        var taille = i.Taille switch { > 0.5 => "les grands", < -0.5 => "les petits", _ => "une taille moyenne" };
        var teint = i.Teint switch { > 0.62 => "la peau sombre", < 0.38 => "la peau claire", _ => "un teint ni clair ni sombre" };
        var corps = i.Corpulence switch { > 0.62 => "les corps bien en chair", < 0.38 => "les corps minces", _ => "les corps ni maigres ni gras" };
        return $"{taille}, {teint}, {corps}";
    }
}

/// <summary>Qui exclut : la communauté (exil), un parent (rejet familial), ou un départ volontaire pour suivre un exilé.</summary>
public enum TypeExclusion : byte
{
    Communaute = 0,
    Famille = 1,
    Depart = 2,
}

/// <summary>
/// Une exclusion : par qui (<paramref name="De"/> = id de la communauté ou du parent), quand, jusqu'à quand
/// (0 = sans terme), l'événement du journal qui la raconte, la raison donnée, et si sa fin a été notée.
/// </summary>
public sealed record Exclusion(TypeExclusion Type, int De, long Le, long Jusqua, long EvenementId, string Raison, bool Levee = false)
{
    public bool Active(long maintenant) => !Levee && (Jusqua == 0 || Jusqua > maintenant);
}
