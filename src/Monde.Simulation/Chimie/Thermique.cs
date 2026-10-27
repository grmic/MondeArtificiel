using Monde.Simulation.Humains;

namespace Monde.Simulation.Chimie;

/// <summary>Forme d'un foyer : c'est elle, plus que le bois, qui décide de la chaleur qu'il atteint.</summary>
public enum FormeFoyer : byte
{
    /// <summary>Un feu posé sur le sol, à l'air libre.</summary>
    Ouvert = 0,
    /// <summary>Entouré d'un cercle de pierres.</summary>
    Pierres = 1,
    /// <summary>Dans une fosse creusée : l'air n'y entre que par le haut, la terre garde la chaleur.</summary>
    Fosse = 2,
    /// <summary>Couvert de terre : le bois couve sans flamme, privé d'air (c'est ainsi qu'il devient charbon).</summary>
    Couvert = 3,
    /// <summary>Enfermé dans une enceinte de terre ou d'argile, avec une ouverture : un four.</summary>
    Enceinte = 4,
}

/// <summary>
/// La chaleur comme une quantité. La température d'un feu n'est pas un palier donné : elle sort du bilan d'énergie entre ce que
/// le combustible dégage (son pouvoir calorifique, moins l'eau à vaporiser), l'air qu'il réchauffe en brûlant (l'air qu'il faut
/// au strict minimum, multiplié par l'excès d'air λ qu'apporte la forme du foyer et le vent), et ce que le feu perd par
/// rayonnement (en T⁴, loi de Stefan-Boltzmann), d'autant moins que la terre ou l'argile l'enferment.
/// <para>
/// Ordres de grandeur mesurés : un feu de camp ouvert de bois sec, 600 à 800 °C dans les flammes, 900 °C au plus au cœur
/// des braises (Bellomo, 1993 ; Werts et Jahren, 2007) ; une cuisson en meule ou en fosse, 700 à 900 °C (Gosselain, 1992 ;
/// Livingstone Smith, 2001) ; un four à tirage, 1 000 à 1 100 °C ; le charbon de bois soufflé, plus de 1 200 °C, ce qu'il faut
/// pour fondre le cuivre (Rehren, 2003). Une meule ou une fosse couverte de terre couve entre 400 et 600 °C (Antal et Grønli, 2003).
/// La terre sous un feu atteint 300 à 500 °C à quelques centimètres (Werts et Jahren, 2007).
/// </para>
/// </summary>
public static class Thermique
{
    /// <summary>Chaleur massique moyenne des fumées entre 300 et 1 500 K, en kJ/(kg·K).</summary>
    public const double ChaleurFumees = 1.25;

    /// <summary>Le bois de feu : du bois mort et sec à l'air (12 à 25 % d'eau), ou du bois vert (40 à 50 %).</summary>
    public static Substance Bois(double humidite) => Mouiller(BoisSec, humidite);

    /// <summary>Bois anhydre (Rowell, 2005) : cellulose, hémicelluloses, lignine, un peu de résine et ses cendres.</summary>
    public static Substance BoisSec { get; } = Substance.De(Structure.Fibreuse, 0.6, 1,
        (Phase.Cellulose, 0.43), (Phase.Hemicellulose, 0.27), (Phase.Lignine, 0.26), (Phase.Terpenes, 0.03), (Phase.Potasse, 0.004),
        (Phase.Calcite, 0.006));

    /// <summary>Le charbon de bois tel qu'il sort d'une meule : 80 à 90 % de carbone, un peu d'hydrogène et d'oxygène resté, ses cendres.</summary>
    public static Substance Charbon { get; } = Substance.De(Structure.Fibreuse, 0.8, 1, (Phase.Carbone, 0.86), (Phase.Lignine, 0.08),
        (Phase.Potasse, 0.02), (Phase.Calcite, 0.02), (Phase.Eau, 0.02));

    /// <summary>La même matière avec cette part d'eau (en masse).</summary>
    public static Substance Mouiller(Substance s, double eau)
    {
        var sans = s.Composants.Where(c => c.Phase != Phase.Eau).Select(c => (c.Phase, c.Part * (1 - eau))).ToList();
        if (eau > 0) sans.Add((Phase.Eau, eau));
        return Substance.De(s.Structure, s.Porosite, s.Masse, [.. sans]) with
        {
            Forme = s.Forme, Frittage = s.Frittage, Fissures = s.Fissures, Ecrouissage = s.Ecrouissage, FaconneLe = s.FaconneLe,
        };
    }

    /// <summary>
    /// Température de flamme d'un combustible brûlé avec un excès d'air λ, sans perte (température adiabatique), en °C :
    /// ΔT = PCI / ((1 + λ · air stœchiométrique) · c_fumées).
    /// </summary>
    public static double Adiabatique(Substance combustible, double lambda, double ambiante)
    {
        var pci = combustible.PouvoirCalorifique * 1000;
        return ambiante + pci / ((1 + lambda * combustible.AirStoechiometrique) * ChaleurFumees);
    }

    /// <summary>
    /// Ce que fait la forme du foyer : l'excès d'air (λ) d'un feu calme, ce que le vent y ajoute par mètre par seconde, la part
    /// de la chaleur gardée près du feu et la température caractéristique des pertes par rayonnement (plus haute quand la terre
    /// renvoie la chaleur).
    /// </summary>
    private static (double Lambda, double ParVent, double Garde, double Rayonnement) Forme(FormeFoyer f) => f switch
    {
        FormeFoyer.Pierres => (2.3, 0.08, 0.93, 2000),
        FormeFoyer.Fosse => (2.0, 0.03, 0.95, 2200),
        FormeFoyer.Enceinte => (1.7, 0.02, 0.97, 2450),
        _ => (2.5, 0.12, 0.9, 1900),
    };

    /// <summary>
    /// Température dans les flammes d'un foyer, en °C. <paramref name="humidite"/> : eau du bois ; <paramref name="charbon"/> :
    /// part du charbon de bois dans ce qui brûle (0 à 1) ; <paramref name="vent"/> : en m/s ; <paramref name="souffle"/> : quelqu'un
    /// souffle sur le feu (à la bouche, par un roseau creux) ; <paramref name="heures"/> : combustible qui reste (un petit feu
    /// perd plus de sa chaleur qu'un grand).
    /// </summary>
    public static double Temperature(FormeFoyer forme, double humidite, double charbon, double vent, bool souffle, double ambiante, double heures)
    {
        if (forme == FormeFoyer.Couvert)
        {
            // Privé d'air, le bois ne brûle qu'en partie et la terre boit la chaleur : il couve.
            return Math.Max(ambiante, 380 + 220 * (1 - Math.Clamp(humidite, 0, 0.6) / 0.6) + 60 * charbon);
        }
        var (lambda, parVent, garde, rayonnement) = Forme(forme);
        lambda += parVent * Math.Clamp(vent, 0, 20);
        if (souffle)
        {
            // Souffler apporte l'air là où il manque : le feu brûle plus vite pour les mêmes pertes, donc plus chaud.
            lambda = Math.Max(1.2, lambda - 0.6);
            garde = Math.Min(0.99, garde + 0.03);
        }
        var bois = Bois(humidite);
        var tBois = Adiabatique(bois, lambda, ambiante);
        var tCharbon = Adiabatique(Charbon, lambda, ambiante);
        var brute = ambiante + garde * (0.8 + 0.2 * Math.Clamp(heures / 3, 0, 1)) * ((1 - charbon) * tBois + charbon * tCharbon - ambiante);
        var k = brute + 273.15;
        var r = rayonnement + 273.15;
        return Math.Max(ambiante, k / (1 + Math.Pow(k / r, 4)) - 273.15);
    }

    /// <summary>
    /// Couleur de ce qui rougeoie à cette température, telle que l'œil la voit (échelle de Pouillet) : rien sous 500 °C,
    /// rouge sombre vers 550, cerise vers 700, orange vers 900, jaune vers 1 100, blanc au-delà de 1 300.
    /// </summary>
    public static int Incandescence(double t) => t switch
    {
        < 500 => 0,
        < 650 => 1,
        < 800 => 2,
        < 1000 => 3,
        < 1250 => 4,
        _ => 5,
    };
}
