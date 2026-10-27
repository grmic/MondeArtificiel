namespace Monde.Simulation.Chimie;

/// <summary>
/// Les corps purs (minéraux, matières du vivant, métaux) dont les choses sont des mélanges. Ce ne sont pas des objets : un
/// galet, un bois, une argile ou un pot sont des proportions de ces phases, et c'est d'elles que se calculent leurs
/// propriétés et ce qu'elles deviennent au feu (voir <see cref="Reactions"/>).
/// </summary>
public enum Phase : byte
{
    Eau = 0,
    Quartz = 1,
    /// <summary>Calcédoine : la silice en cristaux invisibles du silex et du chaille, avec un peu d'eau.</summary>
    Calcedoine = 2,
    /// <summary>Calcédoine chauffée lentement vers 250-350 °C : plus lisse, elle se taille mieux.</summary>
    CalcedoineChauffee = 3,
    SiliceAmorphe = 4,
    /// <summary>Feldspath potassique (orthose), l'essentiel des granites et des sables arkosiques.</summary>
    Feldspath = 5,
    /// <summary>Pyroxène (augite, simplifiée en diopside) : le minéral sombre des basaltes.</summary>
    Pyroxene = 6,
    Kaolinite = 7,
    /// <summary>Illite (simplifiée en muscovite) : l'argile la plus commune des sols et des schistes.</summary>
    Illite = 8,
    /// <summary>Smectite (montmorillonite, simplifiée en pyrophyllite) : l'argile qui gonfle, des marais et des vallées.</summary>
    Smectite = 9,
    /// <summary>Kaolinite qui a perdu son eau de constitution : la terre devenue céramique.</summary>
    Metakaolin = 10,
    IlliteCuite = 11,
    SmectiteCuite = 12,
    Mullite = 13,
    /// <summary>Verre sodocalcique.</summary>
    Verre = 14,
    /// <summary>Verre volcanique riche en silice (obsidienne, rhyolite).</summary>
    VerreVolcanique = 15,
    Calcite = 16,
    /// <summary>Aragonite : le carbonate de calcium des coquillages et de la nacre.</summary>
    Aragonite = 17,
    /// <summary>Chaux vive (oxyde de calcium).</summary>
    Chaux = 18,
    /// <summary>Chaux éteinte (portlandite).</summary>
    Portlandite = 19,
    /// <summary>Hydroxyapatite : la part minérale des os et des dents.</summary>
    Apatite = 20,
    Hematite = 21,
    Goethite = 22,
    Magnetite = 23,
    Pyrite = 24,
    Malachite = 25,
    Tenorite = 26,
    Cuivre = 27,
    Cassiterite = 28,
    Etain = 29,
    Or = 30,
    Argent = 31,
    /// <summary>Sel gemme.</summary>
    Halite = 32,
    Soufre = 33,
    /// <summary>Carbonate de potassium : le sel des cendres de bois.</summary>
    Potasse = 34,
    /// <summary>Carbone presque pur : le charbon de bois.</summary>
    Carbone = 35,
    /// <summary>Houille, charbon de terre.</summary>
    Houille = 36,
    Bitume = 37,
    Cellulose = 38,
    Hemicellulose = 39,
    Lignine = 40,
    /// <summary>Terpènes et triterpènes : résines des conifères, bétuline de l'écorce de bouleau.</summary>
    Terpenes = 41,
    /// <summary>Brai : goudron noir que donnent les résines et les écorces chauffées sans air.</summary>
    Brai = 42,
    /// <summary>Collagène : la part vivante des os, des peaux et des tendons.</summary>
    Collagene = 43,
}

/// <summary>Grandes familles de phases : elles décident de ce que le feu, l'eau et les coups leur font.</summary>
public enum Classe : byte
{
    Eau = 0,
    Silicate = 1,
    /// <summary>Minéraux argileux, plastiques une fois mouillés, qui perdent leur eau de constitution au feu.</summary>
    Argile = 2,
    /// <summary>Argile qui a perdu cette eau : elle ne se défait plus dans l'eau.</summary>
    ArgileCuite = 3,
    Carbonate = 4,
    Oxyde = 5,
    Sulfure = 6,
    Metal = 7,
    Sel = 8,
    Phosphate = 9,
    Verre = 10,
    /// <summary>Matière du vivant : elle brûle à l'air et se carbonise sans air.</summary>
    Organique = 11,
    Carbone = 12,
}

/// <summary>Couleur qu'un œil humain donne à une chose.</summary>
public enum Teinte : byte
{
    Aucune = 0,
    Blanc = 1,
    Gris = 2,
    Noir = 3,
    Rouge = 4,
    Jaune = 5,
    Brun = 6,
    Vert = 7,
    /// <summary>L'éclat rouge du cuivre.</summary>
    Cuivre = 8,
    /// <summary>L'éclat jaune de l'or ou de la pyrite.</summary>
    Dore = 9,
    /// <summary>L'éclat blanc de l'étain ou de l'argent.</summary>
    Argente = 10,
    Clair = 11,
}

/// <summary>
/// Propriétés d'une phase, tirées des tables (CRC Handbook of Chemistry and Physics ; Mindat et Deer, Howie et Zussman pour
/// les minéraux ; Rowell, 2005, pour le bois).
/// </summary>
/// <param name="Formule">Atomes par unité de formule (fractionnaires pour les matières du vivant, de composition moyenne).</param>
/// <param name="Densite">Masse volumique du corps compact, en g/cm³.</param>
/// <param name="Mohs">Dureté sur l'échelle de Mohs (pour les matières du vivant : celle du bois ou de l'os qu'elles font).</param>
/// <param name="Fusion">Température de fusion en °C (0 si la phase se décompose ou brûle avant).</param>
/// <param name="ChaleurMassique">Capacité thermique massique près de 25 °C, en kJ/(kg·K).</param>
/// <param name="Teinte">Couleur qu'elle donne à un mélange.</param>
/// <param name="Pouvoir">Force colorante : la part de la masse qu'il lui faut pour donner sa couleur au mélange.</param>
/// <param name="Retrait">Retrait au séchage d'une pâte faite de cette seule argile (fraction de longueur).</param>
public sealed record InfoPhase(Phase Phase, Classe Classe, (Element E, double N)[] Formule, double Densite, double Mohs, double Fusion,
    double ChaleurMassique, Teinte Teinte, double Pouvoir, double Retrait = 0, bool Soluble = false, bool Ductile = false)
{
    /// <summary>Masse molaire d'une unité de formule, en g/mol.</summary>
    public double MasseMolaire { get; } = Formule.Sum(a => a.N * Elements.Masse(a.E));

    /// <summary>Part de la masse de la phase qui revient à cet élément.</summary>
    public double Part(Element e)
    {
        var m = 0.0;
        foreach (var (x, n) in Formule) if (x == e) m += n * Elements.Masse(x);
        return m / MasseMolaire;
    }

    public bool Organique => Classe is Classe.Organique or Classe.Carbone || Phase is Phase.Houille or Phase.Bitume or Phase.Soufre;
}

public static class Phases
{
    public const int Nombre = 44;

    private static (Element, double)[] F(params (Element, double)[] f) => f;

    private static readonly InfoPhase[] Table =
    [
        new(Phase.Eau, Classe.Eau, F((Element.H, 2), (Element.O, 1)), 1.00, 0, 0, 4.18, Teinte.Aucune, 1),
        new(Phase.Quartz, Classe.Silicate, F((Element.Si, 1), (Element.O, 2)), 2.65, 7, 1713, 0.74, Teinte.Clair, 0.9),
        new(Phase.Calcedoine, Classe.Silicate, F((Element.Si, 1), (Element.O, 2)), 2.60, 6.5, 1713, 0.74, Teinte.Gris, 0.5),
        new(Phase.CalcedoineChauffee, Classe.Silicate, F((Element.Si, 1), (Element.O, 2)), 2.60, 7, 1713, 0.74, Teinte.Gris, 0.5),
        new(Phase.SiliceAmorphe, Classe.Verre, F((Element.Si, 1), (Element.O, 2)), 2.20, 6, 1713, 0.75, Teinte.Clair, 0.9),
        new(Phase.Feldspath, Classe.Silicate, F((Element.K, 1), (Element.Al, 1), (Element.Si, 3), (Element.O, 8)), 2.56, 6, 1150, 0.73, Teinte.Blanc, 0.6),
        new(Phase.Pyroxene, Classe.Silicate, F((Element.Ca, 1), (Element.Mg, 1), (Element.Si, 2), (Element.O, 6)), 3.30, 6, 1390, 0.70, Teinte.Noir, 0.35),
        new(Phase.Kaolinite, Classe.Argile, F((Element.Al, 2), (Element.Si, 2), (Element.O, 9), (Element.H, 4)), 2.60, 2.2, 0, 0.94, Teinte.Blanc, 0.6,
            Retrait: 0.05),
        new(Phase.Illite, Classe.Argile, F((Element.K, 1), (Element.Al, 3), (Element.Si, 3), (Element.O, 12), (Element.H, 2)), 2.80, 2.5, 0, 0.85, Teinte.Gris, 0.6,
            Retrait: 0.07),
        // L'eau entre les feuillets de la smectite compte avec l'eau libre de la pâte : d'où son grand retrait.
        new(Phase.Smectite, Classe.Argile, F((Element.Al, 2), (Element.Si, 4), (Element.O, 12), (Element.H, 2)), 2.35, 1.5, 0, 0.90, Teinte.Gris, 0.6,
            Retrait: 0.13),
        new(Phase.Metakaolin, Classe.ArgileCuite, F((Element.Al, 2), (Element.Si, 2), (Element.O, 7)), 2.50, 4, 0, 0.90, Teinte.Blanc, 0.6),
        new(Phase.IlliteCuite, Classe.ArgileCuite, F((Element.K, 1), (Element.Al, 3), (Element.Si, 3), (Element.O, 11)), 2.80, 4, 1200, 0.85, Teinte.Gris, 0.6),
        new(Phase.SmectiteCuite, Classe.ArgileCuite, F((Element.Al, 2), (Element.Si, 4), (Element.O, 11)), 2.50, 4, 0, 0.88, Teinte.Gris, 0.6),
        new(Phase.Mullite, Classe.Silicate, F((Element.Al, 6), (Element.Si, 2), (Element.O, 13)), 3.16, 7, 1840, 0.80, Teinte.Blanc, 0.6),
        new(Phase.Verre, Classe.Verre, F((Element.Na, 2), (Element.Ca, 1), (Element.Si, 6), (Element.O, 14)), 2.50, 5.5, 1000, 0.84, Teinte.Clair, 0.9),
        new(Phase.VerreVolcanique, Classe.Verre, F((Element.Na, 1), (Element.K, 1), (Element.Al, 2), (Element.Si, 10), (Element.O, 24)), 2.40, 5.5, 900,
            0.80, Teinte.Noir, 0.5),
        new(Phase.Calcite, Classe.Carbonate, F((Element.Ca, 1), (Element.C, 1), (Element.O, 3)), 2.71, 3, 0, 0.82, Teinte.Blanc, 0.6),
        new(Phase.Aragonite, Classe.Carbonate, F((Element.Ca, 1), (Element.C, 1), (Element.O, 3)), 2.93, 3.7, 0, 0.79, Teinte.Blanc, 0.6),
        new(Phase.Chaux, Classe.Oxyde, F((Element.Ca, 1), (Element.O, 1)), 3.34, 3.5, 2613, 0.75, Teinte.Blanc, 0.5),
        new(Phase.Portlandite, Classe.Oxyde, F((Element.Ca, 1), (Element.O, 2), (Element.H, 2)), 2.21, 2.5, 0, 1.18, Teinte.Blanc, 0.5),
        new(Phase.Apatite, Classe.Phosphate, F((Element.Ca, 5), (Element.P, 3), (Element.O, 13), (Element.H, 1)), 3.16, 5, 1670, 0.70, Teinte.Blanc, 0.5),
        new(Phase.Hematite, Classe.Oxyde, F((Element.Fe, 2), (Element.O, 3)), 5.26, 6, 1565, 0.65, Teinte.Rouge, 0.03),
        new(Phase.Goethite, Classe.Oxyde, F((Element.Fe, 1), (Element.O, 2), (Element.H, 1)), 4.27, 5, 0, 0.75, Teinte.Jaune, 0.06),
        new(Phase.Magnetite, Classe.Oxyde, F((Element.Fe, 3), (Element.O, 4)), 5.17, 6, 1597, 0.65, Teinte.Noir, 0.15),
        new(Phase.Pyrite, Classe.Sulfure, F((Element.Fe, 1), (Element.S, 2)), 5.01, 6.3, 0, 0.52, Teinte.Dore, 0.4),
        new(Phase.Malachite, Classe.Carbonate, F((Element.Cu, 2), (Element.C, 1), (Element.O, 5), (Element.H, 2)), 3.90, 3.8, 0, 0.75, Teinte.Vert, 0.05),
        new(Phase.Tenorite, Classe.Oxyde, F((Element.Cu, 1), (Element.O, 1)), 6.31, 3.8, 1326, 0.53, Teinte.Noir, 0.08),
        new(Phase.Cuivre, Classe.Metal, F((Element.Cu, 1)), 8.96, 3, 1085, 0.385, Teinte.Cuivre, 0.3, Ductile: true),
        new(Phase.Cassiterite, Classe.Oxyde, F((Element.Sn, 1), (Element.O, 2)), 6.99, 6.5, 1630, 0.35, Teinte.Brun, 0.3),
        new(Phase.Etain, Classe.Metal, F((Element.Sn, 1)), 7.29, 1.5, 232, 0.228, Teinte.Argente, 0.3, Ductile: true),
        new(Phase.Or, Classe.Metal, F((Element.Au, 1)), 19.3, 2.75, 1064, 0.129, Teinte.Dore, 0.3, Ductile: true),
        new(Phase.Argent, Classe.Metal, F((Element.Ag, 1)), 10.49, 2.7, 962, 0.235, Teinte.Argente, 0.3, Ductile: true),
        new(Phase.Halite, Classe.Sel, F((Element.Na, 1), (Element.Cl, 1)), 2.17, 2.3, 801, 0.86, Teinte.Blanc, 0.6, Soluble: true),
        new(Phase.Soufre, Classe.Sel, F((Element.S, 1)), 2.07, 2, 115, 0.71, Teinte.Jaune, 0.3),
        new(Phase.Potasse, Classe.Sel, F((Element.K, 2), (Element.C, 1), (Element.O, 3)), 2.43, 2.5, 891, 0.83, Teinte.Blanc, 0.6, Soluble: true),
        new(Phase.Carbone, Classe.Carbone, F((Element.C, 1)), 1.90, 1.5, 0, 0.71, Teinte.Noir, 0.04),
        new(Phase.Houille, Classe.Carbone, F((Element.C, 1), (Element.H, 0.75), (Element.O, 0.08)), 1.30, 2, 0, 1.3, Teinte.Noir, 0.05),
        new(Phase.Bitume, Classe.Organique, F((Element.C, 1), (Element.H, 1.5), (Element.S, 0.02)), 1.03, 1, 90, 1.7, Teinte.Noir, 0.05),
        new(Phase.Cellulose, Classe.Organique, F((Element.C, 6), (Element.H, 10), (Element.O, 5)), 1.50, 2.5, 0, 1.4, Teinte.Clair, 0.8),
        new(Phase.Hemicellulose, Classe.Organique, F((Element.C, 5), (Element.H, 8), (Element.O, 4)), 1.50, 2, 0, 1.4, Teinte.Clair, 0.8),
        new(Phase.Lignine, Classe.Organique, F((Element.C, 10), (Element.H, 12), (Element.O, 3)), 1.35, 2.5, 0, 1.3, Teinte.Brun, 0.4),
        new(Phase.Terpenes, Classe.Organique, F((Element.C, 20), (Element.H, 30), (Element.O, 2)), 1.07, 1.5, 100, 1.6, Teinte.Jaune, 0.3),
        new(Phase.Brai, Classe.Organique, F((Element.C, 20), (Element.H, 26), (Element.O, 1)), 1.10, 1.5, 90, 1.5, Teinte.Noir, 0.08),
        new(Phase.Collagene, Classe.Organique, F((Element.C, 1), (Element.H, 1.6), (Element.N, 0.3), (Element.O, 0.37)), 1.35, 2, 0, 1.5, Teinte.Clair, 0.8),
    ];

    public static InfoPhase De(Phase p) => Table[(int)p];

    public static IReadOnlyList<InfoPhase> Toutes => Table;
}
