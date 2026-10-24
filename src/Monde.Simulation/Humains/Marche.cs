using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Simulation.Humains;

/// <summary>
/// Vitesse de marche, en mètres par seconde : propre à chacun (allure innée, âge, santé, fatigue, charge),
/// puis ralentie ou accélérée par le terrain et la pente de la case où l'on entre.
/// </summary>
public static class Marche
{
    /// <summary>
    /// Vitesse de marche spontanée d'un adulte en terrain plat et dégagé : environ 1,36 m/s, soit 4,9 km/h
    /// (méta-analyse de Bohannon et Andrews, 2011, sur des adultes de 20 à 59 ans).
    /// </summary>
    public const double VitesseAdulte = 1.36;

    /// <summary>
    /// Vitesse relative selon l'âge. Enfants : la vitesse croît jusque vers 7 ans puis s'approche de celle des adultes
    /// (Sutherland et al., 1980 : environ 0,6 m/s à 1 an, 0,9 à 3 ans, 1,1 à 7 ans). Âgés : environ 1,3 m/s
    /// à 60-69 ans, 1,2 à 70-79 ans, 0,95 au-delà de 80 ans (Bohannon et Andrews, 2011).
    /// </summary>
    private static readonly (double Age, double Facteur)[] ParAge =
        [(1, 0.45), (3, 0.65), (7, 0.82), (12, 0.95), (16, 1), (60, 1), (65, 0.97), (75, 0.88), (85, 0.7), (100, 0.55)];

    public static double FacteurAge(double age)
    {
        if (age <= ParAge[0].Age) return age < 1 ? 0 : ParAge[0].Facteur;
        for (var i = 1; i < ParAge.Length; i++)
        {
            var (a, f) = ParAge[i];
            if (age > a) continue;
            var (a0, f0) = ParAge[i - 1];
            return f0 + (f - f0) * (age - a0) / (a - a0);
        }
        return ParAge[^1].Facteur;
    }

    /// <summary>
    /// Vitesse à laquelle l'humain marche en ce moment en terrain plat et dégagé (m/s) : son allure, son âge, puis
    /// son état. Une santé dégradée, l'épuisement, la faim ou la soif extrêmes ralentissent ; une grossesse avancée
    /// et un bébé porté aussi (une charge de 10 % du poids du corps réduit la vitesse spontanée de quelques %).
    /// La fuite fait presser le pas, jusqu'au petit trot.
    /// </summary>
    public static double Vitesse(Humain h, long maintenant)
    {
        var age = FacteurAge(h.AgeEnAnnees(maintenant));
        // Un petit qui suit ses parents est pris sur les épaules ou presse le pas : chez les !Kung, les enfants sont
        // portés lors des déplacements jusque vers 4 ans (Lee, 1979), et les adultes règlent leur pas sur les petits.
        if (h.Activite == Activite.SuivreParent) age = Math.Max(age, 0.85);
        var v = VitesseAdulte * h.Biologie.Marche * age;
        if (h.Sante < 60) v *= 0.4 + 0.6 * h.Sante / 60;
        if (h.Fatigue > 60) v *= 1 - (h.Fatigue - 60) / 100;
        if (Math.Max(h.Faim, h.Soif) > 85) v *= 0.9;
        if (h.EnceinteDepuis is { } debut && maintenant - debut > 180 * Temps.Calendrier.SecondesParJour) v *= 0.9;
        if (h.BebePorteLe >= maintenant - MondeSimule.SecondesParPas) v *= 0.93;
        if (h.Activite == Activite.Fuir) v *= 1.3 + 0.4 * Math.Min(1, h.Sante / 100);
        return v;
    }

    /// <summary>
    /// Facilité de la marche selon le terrain (1 : sentier plat et dégagé). Ordres de grandeur tirés des coefficients
    /// de terrain de Soule et Goldman (1972 : broussailles 1,2 à 1,5, marécage 1,8, sable mou 2,1, neige plus encore ; une mangrove, enchevêtrement
    /// de racines dans la vase, est pire qu'un marais)
    /// et du facteur « hors sentier » de Tobler (1993 : 0,6) ; traverser une rivière à gué est lent.
    /// </summary>
    public static double FacteurTerrain(Biome b) => b switch
    {
        Biome.PlaineDeFougeres => 0.85,
        Biome.ForetDeConiferes => 0.8,
        Biome.ForetTropicale => 0.6,
        Biome.Desert => 0.85,
        Biome.Plage => 0.75,
        Biome.Marais => 0.5,
        Biome.Mangrove => 0.35,
        Biome.ForetDeFeuillus => 0.8,
        Biome.Lande => 0.8,
        Biome.Montagne => 0.75,
        Biome.SommetEnneige => 0.45,
        Biome.Riviere => 0.4,
        _ => 0.8,
    };

    /// <summary>
    /// Effet de la pente (fonction de randonnée de Tobler, 1993) : la marche est la plus rapide en légère descente
    /// (−5 %), deux fois plus lente vers +20 % de montée, et ralentit aussi dans les fortes descentes.
    /// </summary>
    public static double FacteurPente(double deniveleMetres, double distanceMetres)
    {
        var pente = deniveleMetres / distanceMetres;
        return Math.Exp(-3.5 * Math.Abs(pente + 0.05)) / Math.Exp(-3.5 * 0.05);
    }

    /// <summary>Longueur d'un pas d'une case à sa voisine, en mètres (droite ou en diagonale).</summary>
    public static double Longueur(int dx, int dy) => dx != 0 && dy != 0 ? Echelle.MetresParCase * Math.Sqrt(2) : Echelle.MetresParCase;

    /// <summary>
    /// Effort pour entrer dans une case voisine, en mètres « de terrain plat » : la longueur du pas, allongée par le
    /// terrain d'arrivée et la pente.
    /// </summary>
    public static double Effort(Carte carte, int x, int y, int nx, int ny)
    {
        var longueur = Longueur(nx - x, ny - y);
        var denivele = (carte.Altitudes[carte.Indice(nx, ny)] - carte.Altitudes[carte.Indice(x, y)]) * Echelle.MetresParUnite;
        return longueur / (FacteurTerrain(carte.BiomeEn(nx, ny)) * FacteurPente(denivele, longueur));
    }
}
