using Monde.Simulation.Humains;
using Monde.Simulation.Monde;

namespace Monde.Simulation.Chimie;

/// <summary>Ce que l'air autour de la chose apporte : de l'oxygène en abondance (un feu ouvert), ou presque pas (sous la terre, sous la braise).</summary>
public enum Atmosphere : byte
{
    Oxydante = 0,
    Reductrice = 1,
}

/// <summary>Ce qu'une chose a subi au feu : la température atteinte, combien de temps, à quelle vitesse elle est montée, et l'air autour.</summary>
/// <param name="Temperature">Température atteinte au cœur de la pièce, en °C.</param>
/// <param name="Heures">Temps passé à cette température.</param>
/// <param name="Montee">Vitesse de montée, en °C par minute (une pièce jetée dans les flammes : 10 à 30 ; enfouie sous la braise : 1 à 3).</param>
public readonly record struct Chauffe(double Temperature, double Heures, double Montee, Atmosphere Air);

/// <summary>
/// Une transformation d'une phase par la chaleur, telle qu'on la mesure au laboratoire : elle commence vers <paramref name="Debut"/>,
/// est complète vers <paramref name="Fin"/>, et demande de l'ordre de <paramref name="Tau"/> heures à ces températures (cinétique
/// simplifiée : part convertie = avancée en température × (1 − e^(−t/τ))). Les produits sont donnés en kilos par kilo de la phase
/// de départ : pour une réaction minérale, ils sortent de la stœchiométrie (masses molaires) ; pour la carbonisation du vivant,
/// des rendements mesurés.
/// </summary>
/// <param name="Air">Air qu'il faut (null : peu importe).</param>
public sealed record Reaction(int Id, Phase Source, double Debut, double Fin, double Tau, (Phase P, double Rendement)[] Produits, Atmosphere? Air = null)
{
    /// <summary>Part de la phase convertie par cette chauffe (0 à 1).</summary>
    public double Avancee(Chauffe c) =>
        Math.Clamp((c.Temperature - Debut) / (Fin - Debut), 0, 1) * (1 - Math.Exp(-Math.Max(0, c.Heures) / Tau));
}

/// <summary>Ce qu'une chose est devenue après un geste, et ce qui lui est arrivé.</summary>
/// <param name="Eclate">Elle s'est brisée (vapeur, retrait, choc thermique).</param>
/// <param name="Fondue">Elle a fondu et coulé, puis s'est figée sans sa forme.</param>
/// <param name="Brulee">Elle a brûlé, il n'en reste presque que des cendres.</param>
/// <param name="Exotherme">Elle a chauffé d'elle-même (chaux vive mouillée).</param>
public readonly record struct Transformation(Substance Produit, bool Eclate = false, bool Fondue = false, bool Brulee = false, bool Exotherme = false);

/// <summary>
/// Les lois qui changent la matière : la chaleur (déshydratation des argiles, calcination du calcaire, carbonisation du bois,
/// combustion, fusion, recuit), les coups, l'eau. Elles s'appliquent à toute substance, quelle qu'elle soit : une terre jamais vue
/// cuira selon ses minéraux, un mélange que personne n'a fait donnera ce que ses phases donnent.
/// <para>
/// Températures (laboratoire, sous air) : la kaolinite perd son eau de constitution entre 450 et 600 °C, et c'est ce qui fait
/// d'une terre une céramique (Grim, 1968 ; les figurines de Dolní Věstonice, il y a 26 000 ans, ont été cuites vers 500-800 °C :
/// Vandiver et al., 1989) ; l'illite et la smectite vers 550-750 °C ; le métakaolin se change en mullite vers 980 °C. La goethite
/// (ocre jaune) devient hématite (rouge) vers 250-400 °C, ce que savaient les peintres du Paléolithique (Pomiès et al., 1999).
/// La calcite donne la chaux vive vers 750-900 °C, lentement (Boynton, 1980). Le bois se carbonise sans air entre 280 et 500 °C
/// et donne 25 à 35 % de sa masse en charbon (Antal et Grønli, 2003) ; l'écorce de bouleau donne son brai vers 350-400 °C
/// (Schmidt et al., 2019 : la colle des Néandertaliens). Le silex chauffé lentement vers 250-350 °C se taille mieux
/// (Domanski et Webb, 1992 ; Brown et al., 2009), mais éclate s'il chauffe trop vite.
/// </para>
/// </summary>
public static class Reactions
{
    private static double Stoechio(Phase source, double nSource, Phase produit, double nProduit) =>
        nProduit * Phases.De(produit).MasseMolaire / (nSource * Phases.De(source).MasseMolaire);

    private static (Phase, double)[] P(params (Phase, double)[] p) => p;

    private static readonly Atmosphere Ox = Atmosphere.Oxydante;
    private static readonly Atmosphere Red = Atmosphere.Reductrice;

    /// <summary>Les réactions, dans l'ordre où on les applique (l'aragonite devient calcite avant que la calcite ne se calcine).</summary>
    public static IReadOnlyList<Reaction> Toutes { get; } =
    [
        new(0, Phase.Eau, 60, 110, 0.15, []),
        new(1, Phase.Aragonite, 380, 470, 0.5, P((Phase.Calcite, 1))),
        new(2, Phase.Kaolinite, 420, 600, 0.5, P((Phase.Metakaolin, Stoechio(Phase.Kaolinite, 1, Phase.Metakaolin, 1)))),
        new(3, Phase.Illite, 500, 750, 0.5, P((Phase.IlliteCuite, Stoechio(Phase.Illite, 1, Phase.IlliteCuite, 1)))),
        new(4, Phase.Smectite, 550, 750, 0.5, P((Phase.SmectiteCuite, Stoechio(Phase.Smectite, 1, Phase.SmectiteCuite, 1)))),
        // 3 Al2Si2O7 → Al6Si2O13 + 4 SiO2
        new(5, Phase.Metakaolin, 950, 1150, 2, P((Phase.Mullite, Stoechio(Phase.Metakaolin, 3, Phase.Mullite, 1)),
            (Phase.SiliceAmorphe, Stoechio(Phase.Metakaolin, 3, Phase.SiliceAmorphe, 4)))),
        // 2 FeO(OH) → Fe2O3 + H2O
        new(6, Phase.Goethite, 250, 400, 0.3, P((Phase.Hematite, Stoechio(Phase.Goethite, 2, Phase.Hematite, 1)))),
        // CaCO3 → CaO + CO2
        new(7, Phase.Calcite, 650, 900, 2, P((Phase.Chaux, Stoechio(Phase.Calcite, 1, Phase.Chaux, 1)))),
        new(8, Phase.Portlandite, 400, 550, 0.5, P((Phase.Chaux, Stoechio(Phase.Portlandite, 1, Phase.Chaux, 1)))),
        // Cu2CO3(OH)2 → 2 CuO + CO2 + H2O
        new(9, Phase.Malachite, 250, 380, 0.3, P((Phase.Tenorite, Stoechio(Phase.Malachite, 1, Phase.Tenorite, 2)))),
        // 4 FeS2 + 11 O2 → 2 Fe2O3 + 8 SO2 : il y faut l'air.
        new(10, Phase.Pyrite, 400, 600, 0.5, P((Phase.Hematite, Stoechio(Phase.Pyrite, 2, Phase.Hematite, 1))), Ox),
        new(11, Phase.Calcedoine, 220, 330, 1, P((Phase.CalcedoineChauffee, 1))),
        // Sans air, le vivant se carbonise (rendements en charbon de la pyrolyse lente).
        new(12, Phase.Hemicellulose, 200, 320, 0.5, P((Phase.Carbone, 0.22)), Red),
        new(13, Phase.Cellulose, 280, 400, 0.5, P((Phase.Carbone, 0.12)), Red),
        new(14, Phase.Lignine, 250, 500, 0.7, P((Phase.Carbone, 0.42)), Red),
        new(15, Phase.Terpenes, 300, 420, 0.5, P((Phase.Brai, 0.45)), Red),
        new(16, Phase.Brai, 450, 600, 0.5, P((Phase.Carbone, 0.3)), Red),
        new(17, Phase.Collagene, 250, 500, 0.7, P((Phase.Carbone, 0.25)), Red),
        new(18, Phase.Bitume, 450, 600, 0.7, P((Phase.Carbone, 0.2)), Red),
        new(19, Phase.Houille, 500, 900, 2, P((Phase.Carbone, 0.65)), Red),
        // À l'air, il brûle (ce qu'il reste : ses minéraux, la cendre).
        new(20, Phase.Hemicellulose, 250, 400, 0.25, [], Ox),
        new(21, Phase.Cellulose, 300, 450, 0.25, [], Ox),
        new(22, Phase.Lignine, 330, 480, 0.3, [], Ox),
        new(23, Phase.Terpenes, 250, 400, 0.2, [], Ox),
        new(24, Phase.Brai, 300, 450, 0.3, [], Ox),
        new(25, Phase.Collagene, 300, 500, 0.4, [], Ox),
        new(26, Phase.Bitume, 300, 450, 0.4, [], Ox),
        new(27, Phase.Carbone, 380, 600, 1.2, [], Ox),
        new(28, Phase.Houille, 450, 650, 2, [], Ox),
        new(29, Phase.Soufre, 230, 300, 0.2, [], Ox),
    ];

    /// <summary>Réactions de la pyrolyse (carbonisation sans air).</summary>
    private static bool Pyrolyse(int id) => id is >= 12 and <= 19;

    private static bool Combustion(int id) => id >= 20;

    /// <summary>
    /// Ce que devient une chose chauffée ainsi. Le hasard (<paramref name="alea"/>) ne décide que des accidents : une pièce qui
    /// éclate. Les lois des fissures : l'eau restée dans une pâte se change en vapeur et la fait éclater si l'on chauffe vite
    /// (Rice, 1987) ; une argile qui se rétracte beaucoup (smectite) se fend si rien ne la dégraisse (sable, coquillage broyé,
    /// fibres : Rye, 1981) ; une pierre riche en quartz éclate vers 573 °C, quand le quartz change de forme et gonfle ; un silex
    /// jeté dans les flammes se couvre de cupules et se brise.
    /// </summary>
    public static Transformation Chauffer(Substance s, Chauffe c, Alea alea)
    {
        var parts = new double[Phases.Nombre];
        foreach (var (p, v) in s.Composants) parts[(int)p] = v;
        var volume = s.Volume;
        var pyrolyse = 0.0;
        var brule = 0.0;
        var silexAvant = parts[(int)Phase.Calcedoine];
        foreach (var r in Toutes)
        {
            if (r.Air is { } air && air != c.Air) continue;
            var source = parts[(int)r.Source];
            if (source <= 0) continue;
            // Le silex ne s'améliore que chauffé lentement ; jeté dans les flammes, il se fend.
            if (r.Id == 11 && c.Montee > 6) continue;
            var x = r.Avancee(c);
            if (x <= 0) continue;
            var converti = source * x;
            parts[(int)r.Source] -= converti;
            foreach (var (p, rendement) in r.Produits) parts[(int)p] += converti * rendement;
            if (Pyrolyse(r.Id)) pyrolyse += converti;
            if (Combustion(r.Id)) brule += converti;
        }
        Reduire(parts, c);
        var masseRelative = parts.Sum();
        var organiqueAvant = s.Composants.Where(x => Phases.De(x.Phase).Organique).Sum(x => x.Part);
        var composants = new List<(Phase, double)>();
        for (var i = 0; i < parts.Length; i++) if (parts[i] > 0) composants.Add(((Phase)i, parts[i]));
        if (composants.Count == 0 || masseRelative <= 1e-6)
            return new(Substance.De(Structure.Grains, 0.5, Math.Max(0.001, s.Masse * 0.001), (Phase.Potasse, 1)) with { Forme = Forme.Aucune }, Brulee: true);

        var masse = s.Masse * masseRelative;
        // Un objet carbonisé se rétracte d'environ un tiers ; le reste garde son volume (les vides prennent la place de ce qui est parti).
        if (organiqueAvant > 0) volume *= 1 - 0.35 * Math.Clamp(pyrolyse / organiqueAvant, 0, 1);
        var structure = s.Structure;
        var forme = s.Forme;
        var brulee = organiqueAvant >= 0.5 && brule >= 0.6 * organiqueAvant;
        if (brulee)
        {
            structure = Structure.Grains;
            forme = Forme.Aucune;
        }
        var produit = Substance.De(structure, 0, masse, [.. composants]) with
        {
            Forme = forme, Frittage = s.Frittage, Fissures = s.Fissures, Ecrouissage = s.Ecrouissage, FaconneLe = s.FaconneLe,
        };
        var porosite = brulee ? 0.5 : Math.Clamp(1 - masse / produit.DensiteSquelette / Math.Max(1e-6, volume), 0, 0.95);
        produit = produit with { Porosite = porosite };

        // Une terre séchée ou mouillée devient céramique quand la moitié de son argile a perdu son eau de constitution.
        if (produit.Structure == Structure.Pate && produit.Part(Phase.Eau) < 0.05)
            produit = produit with { Structure = produit.Argile >= 0.2 ? Structure.Crue : Structure.Grains };
        if (produit.Structure is Structure.Pate or Structure.Crue && produit.Argile >= 0.2 && produit.ArgileCuite >= 0.5)
            produit = produit with { Structure = Structure.Cuite };
        if (produit.Structure == Structure.Cuite)
        {
            // Les fondants (potassium, sodium, calcium, fer) font commencer la soudure des grains plus bas.
            var fondants = produit.PartElement(Element.K) + produit.PartElement(Element.Na) + produit.PartElement(Element.Ca) + produit.PartElement(Element.Fe);
            var debut = 700 - 400 * fondants;
            var frittage = Math.Clamp((c.Temperature - debut) / (1150 - debut), 0, 1) * (1 - Math.Exp(-c.Heures / 1.5));
            if (frittage > produit.Frittage)
                produit = produit with { Frittage = frittage, Porosite = Math.Max(0.02, produit.Porosite - 0.25 * (frittage - produit.Frittage)) };
        }
        // Un métal martelé redevient doux au feu (recristallisation du cuivre écroui vers 250-450 °C).
        if (produit.Ecrouissage > 0 && c.Temperature > 250)
            produit = produit with { Ecrouissage = produit.Ecrouissage * (1 - Math.Clamp((c.Temperature - 250) / 200, 0, 1) * (1 - Math.Exp(-c.Heures / 0.2))) };

        var fondue = Fondre(ref produit, c.Temperature) || Rassembler(ref produit, c);

        // Accidents.
        var vite = c.Montee >= 10 ? 1.3 : c.Montee >= 4 ? 0.7 : 0.35;
        var risque = 0.0;
        if (s.Structure is Structure.Pate or Structure.Crue && s.Argile >= 0.2)
        {
            var vapeur = Math.Clamp((s.Part(Phase.Eau) - 0.03) * 10, 0, 1) * (c.Temperature > 100 ? vite : 0);
            risque = 1 - (1 - vapeur) * (1 - Math.Clamp((Retrait(s) - 0.02) * 15, 0, 1) * Math.Min(1, vite) * (c.Temperature > 450 ? 1 : 0.3));
        }
        else if (s.Structure == Structure.Roche && c.Temperature > 300)
        {
            if (silexAvant > 0.5) risque = (c.Montee > 6 ? 0.5 : 0.05) * Math.Min(1, vite) + Math.Clamp((c.Temperature - 450) / 300, 0, 0.5);
            else if (s.Part(Phase.Quartz) + s.Part(Phase.Calcedoine) >= 0.5 && c.Temperature >= 573) risque = 0.35 * Math.Min(1, vite);
        }
        var eclate = risque > 0 && alea.Reel() < risque;
        if (eclate) produit = produit with { Fissures = 1 };
        else if (risque > 0) produit = produit with { Fissures = Math.Min(1, produit.Fissures + 0.3 * risque) };
        return new(produit, eclate, fondue, brulee);
    }

    /// <summary>
    /// Réduction de l'oxyde de cuivre (la malachite chauffée devient d'abord cet oxyde noir, vers 250-380 °C) : sans air, le
    /// monoxyde de carbone que dégage le combustible lui prend son oxygène (CuO + CO → Cu + CO₂), à partir de 700 °C environ et
    /// d'autant plus vite que c'est chaud, presque tout vers 1 100 °C (Rehder, 2000 ; Rostoker et al., 1989). Le charbon mêlé au
    /// minerai broyé touche chaque grain et va bien plus vite que le gaz seul ; il s'use (2 CuO + C → 2 Cu + CO₂). Ainsi ont
    /// commencé les premiers fondeurs, à Belovode vers 5000 av. J.-C. : malachite et charbon dans de petits creusets
    /// (Radivojević et al., 2010).
    /// </summary>
    private static void Reduire(double[] parts, Chauffe c)
    {
        var oxyde = parts[(int)Phase.Tenorite];
        if (c.Air != Atmosphere.Reductrice || oxyde <= 0) return;
        var carbone = parts[(int)Phase.Carbone];
        var x = Math.Clamp((c.Temperature - 700) / 400, 0, 1) * (1 - Math.Exp(-Math.Max(0, c.Heures) / 0.7));
        var converti = oxyde * x * (carbone >= 0.02 * oxyde ? 1 : 0.3);
        if (converti <= 0) return;
        parts[(int)Phase.Tenorite] -= converti;
        parts[(int)Phase.Cuivre] += converti * Stoechio(Phase.Tenorite, 1, Phase.Cuivre, 1);
        parts[(int)Phase.Carbone] = Math.Max(0, carbone - converti * Stoechio(Phase.Tenorite, 2, Phase.Carbone, 1));
    }

    /// <summary>
    /// Au-dessus de 1 085 °C, le cuivre fond : ses grains, épars dans la masse, coulent et se rassemblent en une goutte au fond
    /// du creuset ; le reste devient une scorie qu'on jette. Une part du cuivre y reste prise, d'autant moins que c'est chaud et
    /// que cela dure (les scories des premiers fondeurs sont pleines de gouttelettes de cuivre : Rehren, 2003).
    /// </summary>
    private static bool Rassembler(ref Substance s, Chauffe c)
    {
        var cuivre = s.Part(Phase.Cuivre);
        if (s.Structure == Structure.Metal || cuivre < 0.01 || c.Temperature < Phases.De(Phase.Cuivre).Fusion) return false;
        var recueilli = Math.Clamp(0.3 + (c.Temperature - 1085) / 300, 0.3, 0.85) * (1 - Math.Exp(-Math.Max(0, c.Heures) / 0.5));
        var masse = s.Masse * cuivre * recueilli;
        if (masse < 0.002) return false;
        s = Substance.De(Structure.Metal, 0.02, masse, (Phase.Cuivre, 1)) with { Forme = Forme.Aucune, FaconneLe = s.FaconneLe };
        return true;
    }

    /// <summary>
    /// Retrait d'une pâte en séchant : celui de ses argiles, pondéré par leur part ; les grains qui ne sont pas de l'argile
    /// (sable, coquillage broyé, fibres, terre déjà cuite) le diminuent d'autant (c'est le rôle du dégraissant).
    /// </summary>
    public static double Retrait(Substance s)
    {
        var r = 0.0;
        foreach (var (p, v) in s.Composants) r += v * Phases.De(p).Retrait;
        var sec = 1 - s.Part(Phase.Eau);
        return sec <= 0 ? 0 : r / sec;
    }

    /// <summary>
    /// Fusion : un métal qui fait l'essentiel de la pièce fond à sa température et se fige en goutte ; un verre aussi ; une poix,
    /// du soufre ou du sel fondent et coulent. La pièce perd sa forme.
    /// </summary>
    private static bool Fondre(ref Substance s, double t)
    {
        var d = Phases.De(s.Dominante);
        if (d.Fusion <= 0 || t < d.Fusion || s.Part(d.Phase) < 0.5) return false;
        var structure = d.Classe switch
        {
            Classe.Metal => Structure.Metal,
            Classe.Verre or Classe.Silicate => Structure.Vitreuse,
            _ => Structure.Roche,
        };
        s = s with { Structure = structure, Forme = Forme.Aucune, Porosite = 0.02, Ecrouissage = 0 };
        return true;
    }

    /// <summary>
    /// Frapper la chose sur une pierre dure, encore et encore : un métal s'aplatit, s'étire et durcit (il se fend si on le
    /// martèle trop sans le recuire) ; ce qui est plus tendre que la pierre et cassant s'écrase en poudre ; le reste résiste.
    /// <paramref name="marteau"/> : dureté de la pierre qui frappe (0 à 1).
    /// </summary>
    public static Transformation Marteler(Substance s, double marteau, double adresse, Alea alea)
    {
        if (s.Malleable)
        {
            var ecrouissage = Math.Min(1, s.Ecrouissage + 0.2 + 0.1 * alea.Reel());
            var fissures = Math.Min(1, s.Fissures + Math.Max(0, ecrouissage - 0.8) * (1.2 - adresse));
            // Battu, il s'aplatit ; repris par un bord, il s'étire en pointe (alênes et perles du cuivre natif : Ehrhardt, 2009).
            var forme = (s.Forme & Forme.Plat) == 0 ? Forme.Plat : Forme.Long | Forme.Pointu;
            return new(s with { Ecrouissage = ecrouissage, Fissures = fissures, Forme = forme, Porosite = Math.Max(0, s.Porosite - 0.1) });
        }
        if (s.Structure is Structure.Fibreuse && s.PartClasse(Classe.Organique) + s.Part(Phase.Eau) > 0.5) return new(s);
        // Une masse où le cuivre est épars (minerai chauffé sans fondre) : écrasée, elle libère des grains de métal, qui ne
        // s'écrasent pas, eux, et qu'on ramasse.
        if (s.Part(Phase.Cuivre) is var cuivre and >= 0.01 && (s.Durete < marteau || s.Structure == Structure.Grains) && s.Masse * cuivre * 0.8 >= 0.002)
            return new(Substance.De(Structure.Metal, 0.05, s.Masse * cuivre * 0.8, (Phase.Cuivre, 1)) with { Forme = Forme.Aucune }, Eclate: true);
        if (s.Durete < marteau && s.Structure is not Structure.Grains and not Structure.Pate)
        {
            // Une pierre dure se brise en morceaux ; une matière tendre (craie, coquille, terre séchée, ocre) s'écrase en poudre.
            if (s.Durete >= 0.5) return new(s with { Fissures = 1 }, Eclate: true);
            return new(s with { Structure = Structure.Grains, Forme = Forme.Aucune, Porosite = 0.45, Fissures = 0 }, Eclate: true);
        }
        return new(s);
    }

    /// <summary>
    /// Mêler deux matières (et de l'eau, si l'une est de l'eau) : la chaux vive s'éteint en chauffant (CaO + H2O → Ca(OH)2,
    /// 65 kJ par mole) ; une terre argileuse assez mouillée devient une pâte qu'on peut modeler ; un sable ou une poudre mêlé à une
    /// pâte la dégraisse. Les parts sont celles des masses mises ensemble.
    /// </summary>
    public static Transformation Melanger(Substance a, Substance b, long maintenant)
    {
        var parts = new double[Phases.Nombre];
        foreach (var (p, v) in a.Composants) parts[(int)p] += v * a.Masse;
        foreach (var (p, v) in b.Composants) parts[(int)p] += v * b.Masse;
        var exotherme = false;
        // La chaux vive boit l'eau : 1 mole de chaux (56 g) pour 1 mole d'eau (18 g).
        var chaux = parts[(int)Phase.Chaux];
        var eau = parts[(int)Phase.Eau];
        if (chaux > 0 && eau > 0)
        {
            var eteinte = Math.Min(chaux, eau * Phases.De(Phase.Chaux).MasseMolaire / Phases.De(Phase.Eau).MasseMolaire);
            parts[(int)Phase.Chaux] -= eteinte;
            parts[(int)Phase.Eau] -= eteinte * Phases.De(Phase.Eau).MasseMolaire / Phases.De(Phase.Chaux).MasseMolaire;
            parts[(int)Phase.Portlandite] += eteinte * Stoechio(Phase.Chaux, 1, Phase.Portlandite, 1);
            exotherme = eteinte > 0.02 * (a.Masse + b.Masse);
        }
        var masse = parts.Sum();
        var composants = new List<(Phase, double)>();
        for (var i = 0; i < parts.Length; i++) if (parts[i] > 0) composants.Add(((Phase)i, parts[i] / masse));
        var melange = Substance.De(Structure.Grains, 0.4, masse, [.. composants]);
        var argile = melange.PartClasse(Classe.Argile);
        var structure = melange.Part(Phase.Eau) >= 0.12 && (argile >= 0.2 || melange.Part(Phase.Portlandite) >= 0.3) ? Structure.Pate : Structure.Grains;
        return new(melange with { Structure = structure, Porosite = structure == Structure.Pate ? 0.3 : 0.4, FaconneLe = maintenant }, Exotherme: exotherme);
    }

    /// <summary>
    /// Une pâte qui sèche à l'air : son eau s'en va, plus vite au soleil et au vent sec (de un à quelques jours pour un petit pot :
    /// Rye, 1981). Sous 5 % d'eau, la terre est crue : dure, mais elle se défait si on la mouille.
    /// <paramref name="jours"/> : temps écoulé ; <paramref name="sec"/> : sécheresse de l'air (0 à 1).
    /// </summary>
    public static Substance Secher(Substance s, double jours, double sec)
    {
        var eau = s.Part(Phase.Eau);
        if (eau <= 0 || jours <= 0 || s.Structure is not (Structure.Pate or Structure.Crue or Structure.Grains)) return s;
        var reste = eau * Math.Exp(-jours / (1 + 3 * (1 - Math.Clamp(sec, 0, 1))));
        // L'eau de l'air n'en part jamais tout à fait : 1 à 2 % restent dans une terre « sèche ».
        reste = Math.Max(Math.Min(eau, 0.015), reste);
        var parts = s.Composants.Select(c => c.Phase == Phase.Eau ? (c.Phase, reste) : c).ToArray();
        var sechee = Substance.De(s.Structure, s.Porosite, s.Masse * (1 - eau + reste), parts) with
        {
            Forme = s.Forme, Frittage = s.Frittage, Fissures = s.Fissures, Ecrouissage = s.Ecrouissage, FaconneLe = s.FaconneLe,
        };
        if (sechee.Structure == Structure.Pate && reste < 0.05)
            sechee = sechee with
            {
                // Une pâte de chaux qui sèche ne tombe pas en poudre : elle prend en masse, et durcit en reprenant l'air.
                Structure = sechee.Argile >= 0.2 ? Structure.Crue : sechee.Part(Phase.Portlandite) + sechee.Part(Phase.Calcite) >= 0.3 ? Structure.Roche : Structure.Grains,
            };
        return sechee;
    }

    /// <summary>
    /// La chaux éteinte reprend à l'air son gaz carbonique et redevient du calcaire : Ca(OH)2 + CO2 → CaCO3 + H2O. Une couche
    /// mince d'enduit prend en quelques jours à quelques semaines, une masse bien plus lentement (la carbonatation avance comme
    /// la racine carrée du temps : Lawrence et al., 2006). <paramref name="jours"/> : temps depuis qu'on l'a gâchée ;
    /// <paramref name="epaisseur"/> : en centimètres.
    /// </summary>
    public static Substance Carbonater(Substance s, double jours, double epaisseur)
    {
        var chaux = s.Part(Phase.Portlandite);
        if (chaux <= 0 || jours <= 0) return s;
        // Environ 2 mm la première semaine dans une pâte à l'air libre, puis comme √t (k ≈ 0,8 mm par √jour).
        var profondeur = 0.08 * Math.Sqrt(jours);
        var prise = Math.Min(1, profondeur / Math.Max(0.1, epaisseur / 2));
        var reste = chaux * (1 - prise);
        var convertie = chaux - reste;
        if (convertie <= 1e-6) return s;
        var calcite = convertie * Stoechio(Phase.Portlandite, 1, Phase.Calcite, 1);
        var parts = new Dictionary<Phase, double>();
        foreach (var (p, v) in s.Composants) parts[p] = v;
        parts[Phase.Portlandite] = reste;
        parts[Phase.Calcite] = parts.GetValueOrDefault(Phase.Calcite) + calcite;
        // L'eau rendue s'évapore aussitôt.
        var total = parts.Values.Sum();
        var masse = s.Masse * total;
        var composants = parts.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value / total)).ToArray();
        return Substance.De(s.Structure, s.Porosite, masse, composants) with
        {
            Forme = s.Forme, Frittage = s.Frittage, Fissures = s.Fissures, Ecrouissage = s.Ecrouissage, FaconneLe = s.FaconneLe,
        };
    }

    /// <summary>
    /// Une pierre chauffée plongée dans l'eau : elle lui donne sa chaleur jusqu'à ce que les deux s'égalisent (calorimétrie :
    /// m·c·ΔT de la pierre = m·c·ΔT de l'eau, 4,18 kJ/(kg·K) pour l'eau). Au-delà de 100 °C, l'eau bout et ne monte plus.
    /// Renvoie la température de l'eau.
    /// </summary>
    public static double Plonger(Substance pierre, double temperaturePierre, double litres, double temperatureEau)
    {
        var cPierre = pierre.Capacite;
        var cEau = 4.18 * litres;
        var equilibre = (cPierre * temperaturePierre + cEau * temperatureEau) / Math.Max(1e-6, cPierre + cEau);
        return Math.Min(100, equilibre);
    }
}
