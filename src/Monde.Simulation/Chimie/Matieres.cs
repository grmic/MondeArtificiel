using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Simulation.Chimie;

/// <summary>
/// De quoi sont faites les matières que les humains trouvent : leur composition moyenne (tables de minéralogie et de chimie du
/// bois), et pour les terres et les pierres celle de la région, tirée de la roche du lieu, de son sol et de la graine. Deux
/// argiles de deux vallées ne cuisent donc pas pareil, et une communauté sans calcaire ne verra jamais la chaux.
/// </summary>
public static class Matieres
{
    /// <summary>Un nombre de 0 à 1 propre à une zone de 8 × 8 cases et à un usage, stable pour une graine.</summary>
    private static double Zone(Carte carte, int x, int y, ulong sel)
    {
        var etat = carte.Graine ^ sel ^ ((ulong)(x / 8) * 0xD6E8FEB86659FD93UL) ^ ((ulong)(y / 8) * 0x9E3779B97F4A7C15UL);
        return (Alea.SplitMix64(ref etat) & 0xFFFF) / 65536.0;
    }

    /// <summary>Ce que contient la matière brute trouvée sur cette case, ou null si elle n'a pas de composition (le feu, une plante…).</summary>
    public static Substance? De(Matiere m, Carte carte, int x, int y)
    {
        var i = carte.Indice(x, y);
        var roche = carte.Terrain.Roches[i];
        var sol = carte.Terrain.Sols[i];
        var masse = Lois.De(m).Masse;
        var forme = Lois.De(m).Forme;
        Substance? s = m switch
        {
            Matiere.Argile => Argile(carte, x, y, roche, sol),
            Matiere.Galet => Galet(roche, Zone(carte, x, y, 0x47414C45UL)),
            Matiere.Silex => Substance.De(Structure.Roche, 0.01, masse, (Phase.Calcedoine, 0.985), (Phase.Eau, 0.01), (Phase.Calcite, 0.005)),
            Matiere.Pyrite => Substance.De(Structure.Roche, 0.01, masse, (Phase.Pyrite, 0.95), (Phase.Quartz, 0.05)),
            Matiere.Obsidienne => Substance.De(Structure.Vitreuse, 0.005, masse, (Phase.VerreVolcanique, 0.98), (Phase.Magnetite, 0.02)),
            Matiere.BoisSec or Matiere.Rondelle or Matiere.RondellePercee or Matiere.BatonPointu => Thermique.Bois(0.15),
            Matiere.Branche or Matiere.Tronc => Thermique.Bois(0.4),
            Matiere.Epieu => Thermique.Bois(0.1),
            Matiere.GrandesFeuilles => Substance.De(Structure.Fibreuse, 0.5, masse, (Phase.Cellulose, 0.18), (Phase.Hemicellulose, 0.1),
                (Phase.Lignine, 0.04), (Phase.Eau, 0.66), (Phase.Potasse, 0.02)),
            Matiere.Fibres or Matiere.Corde or Matiere.PorteFeu => Substance.De(Structure.Fibreuse, 0.4, masse, (Phase.Cellulose, 0.6),
                (Phase.Hemicellulose, 0.15), (Phase.Lignine, 0.1), (Phase.Eau, 0.14), (Phase.Potasse, 0.01)),
            // L'herbe sèche porte de la silice (phytolithes) : c'est elle qui reste dans sa cendre.
            Matiere.HerbeSeche => Substance.De(Structure.Fibreuse, 0.7, masse, (Phase.Cellulose, 0.4), (Phase.Hemicellulose, 0.3),
                (Phase.Lignine, 0.13), (Phase.SiliceAmorphe, 0.05), (Phase.Eau, 0.1), (Phase.Potasse, 0.02)),
            Matiere.Calebasse => Substance.De(Structure.Fibreuse, 0.55, masse, (Phase.Cellulose, 0.45), (Phase.Hemicellulose, 0.2),
                (Phase.Lignine, 0.25), (Phase.Eau, 0.09), (Phase.Potasse, 0.01)),
            Matiere.Resine => Substance.De(Structure.Roche, 0.02, masse, (Phase.Terpenes, 0.9), (Phase.Eau, 0.05), (Phase.Cellulose, 0.05)),
            Matiere.Ocre => Ocre(carte, x, y, sol, roche),
            Matiere.Peau => Substance.De(Structure.Fibreuse, 0.3, masse, (Phase.Collagene, 0.65), (Phase.Eau, 0.3), (Phase.Potasse, 0.05)),
            Matiere.Eclat => Substance.De(Structure.Roche, 0.01, masse, (Phase.Calcedoine, 0.985), (Phase.Eau, 0.01), (Phase.Calcite, 0.005)),
            Matiere.Roseau => Substance.De(Structure.Fibreuse, 0.75, masse, (Phase.Cellulose, 0.42), (Phase.Hemicellulose, 0.28),
                (Phase.Lignine, 0.2), (Phase.SiliceAmorphe, 0.03), (Phase.Eau, 0.07)),
            Matiere.Calcaire => Substance.De(Structure.Roche, roche == Roche.Craie ? 0.3 : 0.08, masse, (Phase.Calcite, roche == Roche.Craie ? 0.96 : 0.9),
                (Phase.Quartz, 0.03), (Phase.Kaolinite, roche == Roche.Craie ? 0.005 : 0.04), (Phase.Goethite, 0.01), (Phase.Eau, 0.015)),
            Matiere.Sable => Sable(carte, x, y),
            Matiere.Coquillage => Substance.De(Structure.Fibreuse, 0.05, masse, (Phase.Aragonite, 0.95), (Phase.Collagene, 0.03), (Phase.Eau, 0.02)),
            // L'écorce de bouleau porte jusqu'à un tiers de bétuline ; celle des conifères, de la résine (Rowell, 2005 ; Schmidt et al., 2019).
            Matiere.Ecorce => Substance.De(Structure.Fibreuse, 0.5, masse, (Phase.Lignine, 0.34), (Phase.Cellulose, 0.2),
                (Phase.Hemicellulose, 0.12), (Phase.Terpenes, 0.15 + 0.15 * Zone(carte, x, y, 0x45434F52UL)), (Phase.Eau, 0.12), (Phase.Potasse, 0.02)),
            _ => null,
        };
        return s is null ? null : s with { Masse = masse, Forme = s.Forme == Forme.Aucune ? forme : s.Forme };
    }

    /// <summary>
    /// Argile du lieu : l'illite domine les sols et les schistes ; les granites altérés donnent de la kaolinite, les marais et les
    /// cendres volcaniques de la smectite qui gonfle ; les latérites sont pleines d'oxydes de fer ; près du calcaire, la terre est une
    /// marne. Elle se ramasse humide, prête à modeler, sur les berges et dans les marais (20 à 30 % d'eau).
    /// </summary>
    private static Substance Argile(Carte carte, int x, int y, Roche roche, Sol sol)
    {
        var u = Zone(carte, x, y, 0x4152474CUL);
        double kaolinite = 0.12, illite = 0.3, smectite = 0.08, quartz = 0.24, feldspath = 0.05, goethite = 0.035, hematite = 0.005, calcite = 0.01,
            matiere = 0.01;
        switch (roche)
        {
            case Roche.Granite: kaolinite = 0.3; illite = 0.15; smectite = 0.02; quartz = 0.3; feldspath = 0.1; break;
            case Roche.Basalte or Roche.Volcanique: smectite = 0.25; illite = 0.12; kaolinite = 0.08; quartz = 0.1; feldspath = 0.12; goethite = 0.06; break;
            case Roche.Calcaire or Roche.Craie: calcite = 0.18; quartz = 0.18; break;
            case Roche.Gres: quartz = 0.38; illite = 0.22; hematite = 0.02; break;
        }
        switch (sol)
        {
            case Sol.Lateritique: kaolinite = 0.35; illite = 0.08; smectite = 0.02; hematite = 0.07; goethite = 0.05; break;
            case Sol.Tourbeux: smectite += 0.12; matiere = 0.06; goethite = 0.02; break;
            case Sol.Alluvial: smectite += 0.05; quartz += 0.05; break;
        }
        // D'une vallée à l'autre, la terre n'est pas la même.
        var argiles = (kaolinite + illite + smectite) * (0.8 + 0.4 * u);
        var facteur = argiles / (kaolinite + illite + smectite);
        return Substance.De(Structure.Pate, 0.3, Lois.De(Matiere.Argile).Masse,
            (Phase.Kaolinite, kaolinite * facteur), (Phase.Illite, illite * facteur), (Phase.Smectite, smectite * facteur), (Phase.Quartz, quartz),
            (Phase.Feldspath, feldspath), (Phase.Goethite, goethite), (Phase.Hematite, hematite), (Phase.Calcite, calcite),
            (Phase.Lignine, matiere), (Phase.Eau, 0.22 + 0.06 * u));
    }

    /// <summary>Galet de la rivière ou de la pente : quartzite et granite sur les vieux socles, basalte sur les coulées, calcaire ailleurs.</summary>
    private static Substance Galet(Roche roche, double u) => roche switch
    {
        Roche.Basalte or Roche.Volcanique => Substance.De(Structure.Roche, 0.03, 0.6, (Phase.Feldspath, 0.48), (Phase.Pyroxene, 0.4),
            (Phase.Magnetite, 0.06), (Phase.VerreVolcanique, 0.05), (Phase.Eau, 0.01)),
        Roche.Calcaire or Roche.Craie when u < 0.6 => Substance.De(Structure.Roche, 0.06, 0.6, (Phase.Calcite, 0.92), (Phase.Quartz, 0.05),
            (Phase.Goethite, 0.01), (Phase.Eau, 0.02)),
        Roche.Granite => Substance.De(Structure.Roche, 0.01, 0.6, (Phase.Quartz, 0.35), (Phase.Feldspath, 0.55), (Phase.Illite, 0.08),
            (Phase.Magnetite, 0.01), (Phase.Eau, 0.01)),
        _ => Substance.De(Structure.Roche, 0.01, 0.6, (Phase.Quartz, 0.9), (Phase.Feldspath, 0.06), (Phase.Hematite, 0.01), (Phase.Eau, 0.01),
            (Phase.Illite, 0.02)),
    };

    /// <summary>
    /// Ocre : terre d'argile et de quartz teintée d'oxydes de fer. Rouge (hématite) dans les grès et les latérites ; ailleurs
    /// le plus souvent jaune ou brune (goethite), et elle rougit au feu.
    /// </summary>
    private static Substance Ocre(Carte carte, int x, int y, Sol sol, Roche roche)
    {
        var rouge = sol == Sol.Lateritique || roche == Roche.Gres || Zone(carte, x, y, 0x4F435245UL) < 0.35;
        return Substance.De(Structure.Grains, 0.4, Lois.De(Matiere.Ocre).Masse, (rouge ? Phase.Hematite : Phase.Goethite, 0.28),
            (Phase.Kaolinite, 0.36), (Phase.Quartz, 0.3), (Phase.Eau, 0.06));
    }

    /// <summary>Sable : grains de quartz et de feldspath, avec des débris de coquillages sur les plages.</summary>
    private static Substance Sable(Carte carte, int x, int y)
    {
        var plage = carte.BiomeEn(x, y) is Biome.Plage or Biome.Mangrove;
        return Substance.De(Structure.Grains, 0.4, Lois.De(Matiere.Sable).Masse, (Phase.Quartz, plage ? 0.7 : 0.85), (Phase.Feldspath, 0.1),
            (Phase.Magnetite, 0.01), (Phase.Aragonite, plage ? 0.17 : 0.02), (Phase.Eau, 0.02));
    }

    /// <summary>Ce que contient un gisement, tel qu'on en détache un morceau.</summary>
    public static Substance Minerai(Gite g) => g switch
    {
        Gite.Sel => Substance.De(Structure.Roche, 0.02, 0.5, (Phase.Halite, 0.95), (Phase.Quartz, 0.03), (Phase.Eau, 0.02)),
        Gite.Soufre => Substance.De(Structure.Roche, 0.05, 0.5, (Phase.Soufre, 0.85), (Phase.Quartz, 0.1), (Phase.Pyrite, 0.05)),
        // Pépites de cuivre presque pur, avec un peu de malachite autour (lac Supérieur : Martin, 1999).
        Gite.CuivreNatif => Substance.De(Structure.Metal, 0.01, 0.3, (Phase.Cuivre, 0.93), (Phase.Malachite, 0.03), (Phase.Quartz, 0.04)),
        Gite.Malachite => Substance.De(Structure.Roche, 0.05, 0.5, (Phase.Malachite, 0.55), (Phase.Quartz, 0.3), (Phase.Goethite, 0.1),
            (Phase.Calcite, 0.05)),
        Gite.Cassiterite => Substance.De(Structure.Grains, 0.35, 0.5, (Phase.Cassiterite, 0.7), (Phase.Quartz, 0.25), (Phase.Magnetite, 0.05)),
        Gite.FerDesMarais => Substance.De(Structure.Roche, 0.3, 0.5, (Phase.Goethite, 0.6), (Phase.Quartz, 0.15), (Phase.Kaolinite, 0.1),
            (Phase.Lignine, 0.03), (Phase.Eau, 0.12)),
        // L'or des rivières est allié d'argent (souvent 10 à 20 %).
        Gite.Or => Substance.De(Structure.Metal, 0.01, 0.02, (Phase.Or, 0.86), (Phase.Argent, 0.12), (Phase.Quartz, 0.02)),
        Gite.Charbon => Substance.De(Structure.Roche, 0.05, 0.5, (Phase.Houille, 0.82), (Phase.Kaolinite, 0.1), (Phase.Pyrite, 0.03), (Phase.Eau, 0.05)),
        Gite.Bitume => Substance.De(Structure.Roche, 0.1, 0.5, (Phase.Bitume, 0.75), (Phase.Quartz, 0.2), (Phase.Eau, 0.05)),
        _ => Substance.De(Structure.Pate, 0.3, 2, (Phase.Kaolinite, 0.72), (Phase.Quartz, 0.1), (Phase.Illite, 0.05), (Phase.Eau, 0.13)),
    };
}
