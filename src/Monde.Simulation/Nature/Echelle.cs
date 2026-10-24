using System.Globalization;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Nature;

/// <summary>
/// Échelle du monde : une case mesure 800 m de côté (la carte d'origine, 256 cases, fait donc environ 205 km de côté),
/// une unité d'altitude 40 m. Les humains marchent en mètres par seconde (voir <see cref="Humains.Marche"/>) :
/// une case droite fait 800 m, une case en diagonale environ 1 130 m. Les catastrophes s'étendent en kilomètres
/// réels, convertis en cases. La taille de la carte se règle en nombre de cases (voir <see cref="Monde.ConfigurationMonde"/>) ;
/// la finesse d'une case reste fixe, car la marche et les ressources sont calibrées sur elle.
/// </summary>
public static class Echelle
{
    /// <summary>Côté d'une case, en kilomètres.</summary>
    public const double KilometresParCase = 0.8;

    /// <summary>Côté d'une case, en mètres.</summary>
    public const double MetresParCase = KilometresParCase * 1000;

    /// <summary>
    /// Dénivelé représenté par une unité d'altitude (0 à 255, mer à 128) : les montagnes commencent vers 1 400 m
    /// et les plus hauts sommets atteignent environ 5 000 m.
    /// </summary>
    public const double MetresParUnite = 40;

    public static int Cases(double kilometres) => (int)Math.Round(kilometres / KilometresParCase);

    public static double Kilometres(double cases) => cases * KilometresParCase;

    /// <summary>Distance à vol d'oiseau entre deux cases (de centre à centre), en kilomètres.</summary>
    public static double Kilometres(int x1, int y1, int x2, int y2) =>
        Math.Sqrt((double)(x1 - x2) * (x1 - x2) + (double)(y1 - y2) * (y1 - y2)) * KilometresParCase;

    /// <summary>Surface d'un nombre de cases, en km².</summary>
    public static double KilometresCarres(int cases) => cases * KilometresParCase * KilometresParCase;

    /// <summary>Altitude d'une valeur de la carte, en mètres au-dessus (ou au-dessous) du niveau de la mer.</summary>
    public static int Metres(byte altitude) => (int)Math.Round((altitude - Monde.Carte.NiveauMer) * MetresParUnite);

    /// <summary>Format des nombres à la française, sans dépendre des cultures installées sur la machine.</summary>
    private static readonly NumberFormatInfo Francais = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = " " };

    /// <summary>Nombre à la française (espace des milliers, virgule décimale), avec au plus <paramref name="decimales"/> décimales.</summary>
    public static string Nombre(double v, int decimales = 1) =>
        v.ToString(decimales > 0 ? "#,##0." + new string('#', decimales) : "#,##0", Francais);

    /// <summary>Distance en mots : « 600 m », « 3,2 km », « 48 km ».</summary>
    public static string Distance(double kilometres) => kilometres switch
    {
        < 1 => $"{Math.Round(kilometres * 1000 / 50) * 50:0} m",
        < 10 => $"{Nombre(kilometres)} km",
        _ => $"{Nombre(kilometres, 0)} km",
    };

    /// <summary>La même distance, à écrire dans la langue de l'observateur.</summary>
    public static Recit DistanceR(double kilometres) => kilometres switch
    {
        < 1 => new Recit("mesure.m", ("n", (long)(Math.Round(kilometres * 1000 / 50) * 50))),
        < 10 => new Recit("mesure.km", ("n", Math.Round(kilometres, 1, MidpointRounding.AwayFromZero))),
        _ => new Recit("mesure.km", ("n", (long)Math.Round(kilometres, MidpointRounding.AwayFromZero))),
    };

    /// <summary>La même surface, à écrire dans la langue de l'observateur.</summary>
    public static Recit SurfaceR(double km2) => km2 switch
    {
        < 1 => new Recit("mesure.ha", ("n", (long)Math.Round(km2 * 100))),
        < 100 => new Recit("mesure.km2", ("n", Math.Round(km2, 1, MidpointRounding.AwayFromZero))),
        _ => new Recit("mesure.km2", ("n", (long)Math.Round(km2, MidpointRounding.AwayFromZero))),
    };

    /// <summary>Surface en mots : « 40 hectares », « 3,8 km² », « 1 250 km² ».</summary>
    public static string Surface(double km2) => km2 switch
    {
        < 1 => $"{Math.Round(km2 * 100)} hectares",
        < 100 => $"{Nombre(km2)} km²",
        _ => $"{Nombre(km2, 0)} km²",
    };

    /// <summary>Surface d'un nombre de cases, en mots.</summary>
    public static string Surface(int cases) => Surface(KilometresCarres(cases));

    /// <summary>Une durée du monde en mots : « 3 semaines », « 8 mois », « un an », « 12 ans ».</summary>
    public static string Duree(long secondes) => DureeR(secondes).Texte;

    /// <summary>La même durée, à écrire dans la langue de l'observateur.</summary>
    public static Recit DureeR(long secondes)
    {
        var jours = secondes / (double)Temps.Calendrier.SecondesParJour;
        var mois = jours / 30;
        return jours switch
        {
            < 10 => new Recit("duree.quelquesJours"),
            < 45 => new Recit("duree.semaines", ("n", (long)Math.Max(2, Math.Round(jours / 7)))),
            _ when mois < 11.5 => new Recit("duree.mois", ("n", (long)Math.Round(mois))),
            _ when mois < 18 => new Recit("duree.unAn"),
            _ => new Recit("duree.ans", ("n", (long)Math.Round(mois / 12))),
        };
    }
}
