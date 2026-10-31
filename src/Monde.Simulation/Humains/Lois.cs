using System.Globalization;
using Monde.Simulation.Monde;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Formes qu'un humain reconnaît à une chose : c'est d'elles, et non d'un nom d'objet, que dépend ce qu'elle peut faire.</summary>
[Flags]
public enum Forme : byte
{
    Aucune = 0,
    Rond = 1,
    Long = 2,
    Plat = 4,
    Creux = 8,
    Pointu = 16,
    Tranchant = 32,
    /// <summary>Percé de part en part : on peut y passer un bâton.</summary>
    Perce = 64,
    /// <summary>Un crochet, un ergot, une encoche qui retient.</summary>
    Crochet = 128,
}

/// <summary>
/// Propriétés physiques d'une matière, valables pour tous.
/// </summary>
/// <param name="Durete">Dureté de 0 à 1 (échelle de Mohs divisée par 10 : silex ~0,7, bois ~0,2 à 0,3).</param>
/// <param name="Masse">Masse d'une pièce, en kg.</param>
/// <param name="Densite">Densité par rapport à l'eau (moins de 1 : flotte). Une chose creuse compte avec l'air qu'elle enferme.</param>
/// <param name="Lien">Solidité comme lien, de 0 (ne lie rien) à 1.</param>
/// <param name="Resistance">Charge qu'une pièce supporte posée en travers, en kg.</param>
public readonly record struct Proprietes(Forme Forme, double Durete, double Masse, double Densite, double Lien = 0, double Resistance = 0);

/// <summary>Comment les pièces d'un objet sont mises ensemble.</summary>
public enum Montage : byte
{
    /// <summary>La seconde pièce liée au bout de la première, dans son prolongement.</summary>
    Bout = 0,
    /// <summary>La seconde pièce liée en travers, au bout de la première.</summary>
    Travers = 1,
    /// <summary>Les pièces liées côte à côte : une plate-forme.</summary>
    Cote = 2,
    /// <summary>La charge posée sur des pièces libres, couchées dessous.</summary>
    Dessous = 3,
    /// <summary>Les secondes pièces enfilées sur un bâton fixé sous les premières, liées côte à côte.</summary>
    Axe = 4,
    /// <summary>Une pièce unique évidée (au feu et à l'outil tranchant) : une coque.</summary>
    Creuse = 5,
}

/// <summary>
/// Un objet fait de plusieurs pièces : un corps (une ou plusieurs pièces d'une matière), une tête (d'autres pièces, ou
/// aucune), un lien et une façon de les monter. Rien n'y désigne un objet connu d'avance : ce qu'il peut faire se
/// déduit des propriétés de ses pièces (voir <see cref="Lois"/>).
/// </summary>
public sealed record Assemblage(Matiere Corps, int NCorps, Matiere Tete, int NTete, Matiere Lien, Montage Montage)
{
    /// <summary>Forme écrite, gardée avec le savoir ou l'objet : « corps x n + tête x k / lien / montage ».</summary>
    public string Signature => string.Create(CultureInfo.InvariantCulture, $"{(int)Corps}x{NCorps}+{(int)Tete}x{NTete}/{(int)Lien}/{(int)Montage}");

    /// <summary>Relit une signature ; null si elle est vide ou mal formée.</summary>
    public static Assemblage? Lire(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 64) return null;
        var parties = s.Split('/');
        if (parties.Length != 3) return null;
        var pieces = parties[0].Split('+');
        if (pieces.Length != 2 || !Piece(pieces[0], out var corps, out var nc) || !Piece(pieces[1], out var tete, out var nt)) return null;
        if (!int.TryParse(parties[1], NumberStyles.None, CultureInfo.InvariantCulture, out var lien) || !Enum.IsDefined((Matiere)lien)) return null;
        if (!int.TryParse(parties[2], NumberStyles.None, CultureInfo.InvariantCulture, out var montage) || !Enum.IsDefined((Montage)montage)) return null;
        if (nc < 1) return null;
        return new Assemblage(corps, nc, tete, nt, (Matiere)lien, (Montage)montage);

        static bool Piece(string p, out Matiere m, out int n)
        {
            (m, n) = (Matiere.Aucune, 0);
            var x = p.Split('x');
            if (x.Length != 2 || !int.TryParse(x[0], NumberStyles.None, CultureInfo.InvariantCulture, out var v) || !Enum.IsDefined((Matiere)v)
                || !int.TryParse(x[1], NumberStyles.None, CultureInfo.InvariantCulture, out n) || n > 12) return false;
            m = (Matiere)v;
            return true;
        }
    }

    /// <summary>Masse totale, en kg ; une coque creusée ne garde qu'environ un tiers du bois.</summary>
    public double Masse => Montage == Montage.Creuse
        ? NCorps * Lois.De(Corps).Masse * Lois.PartGardeeCreusee
        : NCorps * Lois.De(Corps).Masse + NTete * Lois.De(Tete).Masse + Lois.De(Lien).Masse;
}

/// <summary>Ce qu'un objet assemblé a donné à l'essai, et pourquoi.</summary>
/// <param name="Raison">Ce que l'humain a vu arriver, dit simplement (pour le journal).</param>
public readonly record struct Verdict(Effet Effet, double Qualite, Recit Raison)
{
    public bool Reussi => Effet != Effet.Rien;
}

/// <summary>
/// Les lois de la matière qui jugent un objet assemblé : force qu'il faut pour traîner une charge, ce qui flotte, ce
/// qu'un lien retient. Elles ne connaissent ni le traîneau, ni la roue, ni le radeau : elles connaissent des frottements,
/// des densités et des formes. Un objet « marche » si les lois le permettent, quel que soit le nom qu'on lui donnerait.
/// <para>
/// Ordres de grandeur. Un adulte tire durablement environ 250 N et soulève une trentaine de kilos à bout de bras
/// (normes d'ergonomie, Snook et Ciriello, 1991). Traîner sur le sol coûte un coefficient de frottement de 0,3 à 0,6
/// selon le terrain (bois sur fougères ou terre), bien moins sur la neige (~0,1). Des rouleaux de bois réduisent la résistance
/// à quelques centièmes sur sol ferme ; une roue sur un essieu de bois, à ~0,05 sur sol dur, bien plus dans le sable ou la
/// boue (résistance au roulement). Un tronc de conifère (densité ~0,5 à 0,6) de 0,15 m³ porte une soixantaine de kilos de
/// plus que son poids (Archimède).
/// </para>
/// </summary>
public static class Lois
{
    public const double Gravite = 9.81;

    /// <summary>Traction soutenue d'un adulte vigoureux, en newtons (multipliée par sa vigueur).</summary>
    public static double Traction(double vigueur) => 250 * vigueur;

    /// <summary>Ce qu'il soulève à bout de bras en marchant, en kg (multiplié par sa vigueur).</summary>
    public static double Soulever(double vigueur) => 30 * vigueur;

    /// <summary>Ce qu'il porte dans les bras ou sur l'épaule sur une longue distance, en kg.</summary>
    public static double PorterABras(double vigueur) => 25 * vigueur;

    public static Proprietes De(Matiere m) => m switch
    {
        Matiere.Galet => new(Forme.Rond, 0.65, 0.6, 2.6),
        Matiere.Silex => new(Forme.Aucune, 0.7, 0.5, 2.6),
        Matiere.Pyrite => new(Forme.Aucune, 0.62, 0.4, 5.0),
        Matiere.Obsidienne => new(Forme.Tranchant | Forme.Pointu, 0.55, 0.3, 2.4),
        Matiere.BoisSec => new(Forme.Long, 0.2, 1, 0.5, Resistance: 8),
        Matiere.Branche => new(Forme.Long, 0.25, 2.5, 0.7, Resistance: 25),
        Matiere.GrandesFeuilles => new(Forme.Plat, 0.01, 0.1, 0.6),
        Matiere.Fibres => new(Forme.Long, 0.05, 0.2, 0.8, Lien: 0.35),
        Matiere.HerbeSeche => new(Forme.Aucune, 0.01, 0.1, 0.3),
        // Une calebasse sèche enferme de l'air : elle flotte comme un bouchon.
        Matiere.Calebasse => new(Forme.Rond | Forme.Creux, 0.15, 0.5, 0.1),
        Matiere.Argile => new(Forme.Aucune, 0.05, 2, 1.8),
        Matiere.Carcasse => new(Forme.Aucune, 0.05, 45, 1.05),
        Matiere.Plante => new(Forme.Aucune, 0.02, 0.1, 0.9),
        Matiere.Tronc => new(Forme.Long | Forme.Rond, 0.3, 90, 0.6, Resistance: 300),
        // Chauffée, la résine colle et durcit en refroidissant : un lien solide, mais seulement près du feu.
        Matiere.Resine => new(Forme.Aucune, 0.1, 0.2, 1.05, Lien: 0.55),
        Matiere.Eclat => new(Forme.Tranchant | Forme.Pointu, 0.7, 0.05, 2.6),
        Matiere.BatonPointu => new(Forme.Long | Forme.Pointu, 0.25, 1.2, 0.7, Resistance: 10),
        Matiere.Epieu => new(Forme.Long | Forme.Pointu, 0.35, 1.3, 0.7, Resistance: 12),
        Matiere.Corde => new(Forme.Long, 0.05, 0.3, 0.9, Lien: 0.85),
        Matiere.Peau => new(Forme.Plat, 0.05, 3, 1.0, Lien: 0.3),
        Matiere.Brandon => new(Forme.Long, 0.2, 1.5, 0.6),
        Matiere.PorteFeu => new(Forme.Long, 0.05, 0.3, 0.9),
        Matiere.Rondelle => new(Forme.Rond | Forme.Plat, 0.3, 9, 0.6, Resistance: 80),
        Matiere.RondellePercee => new(Forme.Rond | Forme.Plat | Forme.Perce, 0.3, 8.5, 0.6, Resistance: 70),
        // Une botte de roseaux serrés, de la taille d'un bras : tiges creuses, densité apparente vers 0,3 tant qu'elles ne
        // sont pas gorgées d'eau (Heyerdahl, Tigris, 1979 : les bottes de papyrus ou de berdi tiennent des semaines).
        Matiere.Roseau => new(Forme.Long, 0.02, 6, 0.3, Resistance: 15),
        Matiere.Calcaire => new(Forme.Aucune, 0.3, 1.5, 2.5),
        Matiere.Sable => new(Forme.Aucune, 0.05, 2, 1.6),
        Matiere.Coquillage => new(Forme.Creux, 0.35, 0.05, 2.7),
        Matiere.Ecorce => new(Forme.Plat, 0.1, 0.3, 0.6, Lien: 0.15),
        Matiere.Minerai => new(Forme.Aucune, 0.4, 0.5, 4),
        _ => new(Forme.Aucune, 0, 0, 1),
    };

    /// <summary>Frottement d'une charge traînée sur ce terrain (coefficient, sans unité).</summary>
    public static double Frottement(Biome b) => b switch
    {
        Biome.SommetEnneige => 0.1,
        Biome.PlaineDeFougeres => 0.35,
        Biome.Desert or Biome.Plage => 0.55,
        Biome.Marais => 0.55,
        Biome.Mangrove => 0.65,
        Biome.Montagne => 0.5,
        _ => 0.45,
    };

    /// <summary>Résistance au roulement d'une chose ronde sur ce terrain : faible sur sol dur, forte dans le sable ou la boue.</summary>
    public static double Sol(Biome b) => b switch
    {
        Biome.PlaineDeFougeres => 0.05,
        Biome.Plage or Biome.Desert => 0.25,
        Biome.Marais or Biome.Mangrove => 0.35,
        Biome.Montagne or Biome.SommetEnneige => 0.3,
        Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.ForetDeFeuillus or Biome.Riviere => 0.15,
        _ => 0.1,
    };

    /// <summary>
    /// Force d'un lien entre les pièces (0 sans lien) ; la résine ne colle que chauffée. Une poix de brai ou de bitume (portée comme
    /// <see cref="Matiere.Substance"/>) colle bien mieux que la résine crue (Kozowyk et al., 2017), mais elle aussi doit être chauffée.
    /// </summary>
    public static double ForceDuLien(Matiere lien, bool feu) => lien is Matiere.Resine or Matiere.Substance && !feu ? 0
        : lien == Matiere.Substance ? 0.8 : De(lien).Lien;

    // ------------------------------------------------------------------ Juger un objet assemblé

    /// <summary>L'objet en main pour frapper, percer ou chasser : la pièce dure tient-elle au manche, et dans quel sens ?</summary>
    public static Verdict Emmancher(Assemblage a, bool feu, double adresse, Alea alea)
    {
        var corps = De(a.Corps);
        var tete = De(a.Tete);
        if (a.Montage is not (Montage.Bout or Montage.Travers) || a.NTete != 1 || (corps.Forme & Forme.Long) == 0)
            return Echec(Seul("lois.r.pasUneArme"));
        var lien = ForceDuLien(a.Lien, feu);
        if (lien <= 0) return Echec(a.Lien == Matiere.Resine ? Seul("lois.r.resineFroide") : Seul("lois.r.rienNeRetient"));
        if (tete.Durete < corps.Durete + 0.2) return Echec(Seul("lois.r.boutPasDur"));
        // Le lien lâche d'autant plus vite que la main est maladroite et le lien faible.
        if (alea.Reel() > 0.25 + 0.5 * lien + 0.3 * adresse) return Echec(Seul("lois.r.lienGlisse"));
        var qualite = Math.Clamp(0.3 + 0.4 * lien + 0.3 * adresse * alea.Reel(), 0, 1);
        if (a.Montage == Montage.Bout && (tete.Forme & Forme.Pointu) != 0) return new(Effet.PointeEmmanchee, qualite, Seul("lois.r.pointeEntre"));
        if (a.Montage == Montage.Travers && tete.Masse >= 0.05 && (tete.Forme & Forme.Tranchant) != 0 || a.Montage == Montage.Travers && tete.Masse >= 0.4)
            return new(Effet.TranchantEmmanche, qualite * (tete.Masse >= 0.4 ? 1 : 0.8), Seul("lois.r.coupFort"));
        return Echec(a.Montage == Montage.Bout ? Seul("lois.r.pasDePointe") : Seul("lois.r.tropLegere"));
    }

    /// <summary>Force à la coupe d'un outil tenu en main (0 à 1) : une pierre tranchante seule, ou emmanchée en travers.</summary>
    public static double Coupe(Objet o)
    {
        if (o.Matiere == Matiere.Assemblage && Assemblage.Lire(o.Pieces) is { Montage: Montage.Travers } a)
            return Math.Clamp((0.45 + 0.4 * o.Qualite) * Math.Max(0.2, o.Usure) * (De(a.Tete).Masse >= 0.4 ? 1 : 0.85), 0, 1);
        if (o.Matiere == Matiere.Facture) return Facons.Valeur(Fonction.Couper, o);
        return (De(o.Matiere).Forme & Forme.Tranchant) != 0 ? 0.12 * (0.5 + o.Qualite) : 0;
    }

    /// <summary>Ce qu'une arme ajoute à la chasse (0 : rien) ; une pointe de pierre emmanchée perce plus loin qu'un épieu.</summary>
    public static double Chasse(Objet o) => o.Matiere switch
    {
        Matiere.Epieu => 0.6 + 0.5 * o.Qualite,
        Matiere.BatonPointu => 0.2 + 0.3 * o.Qualite,
        Matiere.Assemblage when Assemblage.Lire(o.Pieces) is { Montage: Montage.Bout } a && (De(a.Tete).Forme & Forme.Pointu) != 0
            => (0.85 + 0.6 * o.Qualite) * Math.Max(0.3, o.Usure),
        Matiere.Assemblage when Assemblage.Lire(o.Pieces) is { Montage: Montage.Travers } => 0.3 + 0.2 * o.Qualite,
        Matiere.Facture => Facons.Valeur(Fonction.Chasser, o),
        _ => 0,
    };

    /// <summary>
    /// Traîner une charge avec cet objet sur ce terrain : il faut que la plate-forme la porte, et que la force pour la
    /// mouvoir reste à la portée de ceux qui tirent.
    /// </summary>
    /// <param name="charge">Masse à transporter, en kg.</param>
    /// <param name="traction">Force que peuvent fournir ceux qui tirent, en newtons.</param>
    /// <param name="soulever">Charge qu'ils soulèvent à bout de bras, en kg.</param>
    /// <param name="km">Distance à parcourir : sur des rouleaux, il faut sans cesse reporter celui de derrière devant.</param>
    public static Verdict Transporter(Assemblage a, double charge, Biome sol, double traction, double soulever, bool feu, double km = 0)
    {
        var corps = De(a.Corps);
        var tete = De(a.Tete);
        var lien = ForceDuLien(a.Lien, feu);
        var total = charge + a.Masse;

        // La charge posée sur des pièces libres, couchées dessous.
        if (a.Montage == Montage.Dessous)
        {
            if ((corps.Forme & Forme.Rond) == 0) return Echec(Seul("lois.r.glissePasMieux"));
            if ((corps.Forme & Forme.Long) == 0)
                return Echec(Seul("lois.r.echappent"));
            if (a.NCorps < 2) return Echec(Seul("lois.r.seulTronc"));
            var roule = 0.4 * Sol(sol) + 0.02;
            if (Sol(sol) >= 0.25) return Echec(Seul("lois.r.solMou"));
            // Quelques centaines de mètres à l'heure : bon pour hisser une lourde charge, pas pour la porter au loin.
            if (km / 0.4 > 3) return Echec(Seul("lois.r.routeLongue"));
            return Force(roule * charge * Gravite, traction) ? new(Effet.Rouleaux, Math.Clamp(1 - roule * 5, 0.2, 1), Seul("lois.r.avanceTroncs"))
                : Echec(Seul("lois.r.tropLourdeTroncs"));
        }
        if (a.Montage is not (Montage.Cote or Montage.Axe) || (corps.Forme & Forme.Long) == 0)
            return Echec(Seul("lois.r.rienPoser"));
        if (lien <= 0) return Echec(a.Lien == Matiere.Resine ? Seul("lois.r.resineSeparent") : Seul("lois.r.nonLiees"));
        if (a.NCorps < 2) return Echec(Seul("lois.r.seulePiece"));
        var porte = a.NCorps * corps.Resistance * (0.5 + 0.5 * lien);
        if (charge > porte) return Echec(Seul("lois.r.cedent"));

        if (a.Montage == Montage.Axe)
        {
            if ((tete.Forme & Forme.Rond) == 0) return Echec(Seul("lois.r.pasRondes"));
            if ((tete.Forme & Forme.Perce) == 0) return Echec(Seul("lois.r.pasPercees"));
            if (a.NTete < 1) return Echec(Seul("lois.r.rienNeTourne"));
            var c = Sol(sol);
            if (!Force(c * total * Gravite, traction)) return Echec(Seul("lois.r.rondellesEnfoncent"));
            if (a.NTete == 1)
            {
                // Une seule roue : celui qui pousse soulève les deux bras et porte une part de la charge.
                if (a.NCorps < 2) return Echec(Seul("lois.r.roueVerse"));
                if (0.3 * total > soulever) return Echec(Seul("lois.r.roueLourd"));
                return new(Effet.RouleSurAxe, Math.Clamp(1 - 2.5 * c, 0.2, 1), Seul("lois.r.brouette"));
            }
            return new(Effet.RouleSurAxe, Math.Clamp(1 - 2.5 * c, 0.2, 1), Seul("lois.r.chariot"));
        }

        // Plate-forme traînée.
        var frottement = Frottement(sol);
        if (!Force(frottement * total * Gravite, traction)) return Echec(Seul("lois.r.tropLourdeSol"));
        return new(Effet.Glisser, Math.Clamp(1.2 - 1.5 * frottement, 0.2, 1), Seul("lois.r.glisse"));
    }

    /// <summary>Monter sur l'eau sur cet objet : il faut qu'il porte la charge (Archimède) et qu'il ne chavire pas.</summary>
    public static Verdict Flotter(Assemblage a, double charge, bool feu)
    {
        var corps = De(a.Corps);
        if (a.Montage == Montage.Creuse)
        {
            if (a.NCorps != 1 || a.Corps != Matiere.Tronc) return Echec(Seul("lois.r.pasUneCoque"));
            var porte = Portance(a);
            return porte < charge ? Echec(Seul("lois.r.coqueEnfonce")) : new(Effet.Flottaison, Math.Clamp(porte / (2 * charge), 0.2, 1), Seul("lois.r.coquePorte"));
        }
        if (a.Montage is not (Montage.Cote or Montage.Axe))
            return a.NCorps == 1 && corps.Densite < 1 && (corps.Forme & Forme.Rond) != 0
                ? Echec(Seul("lois.r.troncRoule"))
                : Echec(Seul("lois.r.pasPlancher"));
        if (ForceDuLien(a.Lien, feu) <= 0) return Echec(Seul("lois.r.courant"));
        if (a.NCorps < 2) return Echec(Seul("lois.r.seulePieceRoule"));
        var portance = Portance(a);
        if (portance < charge) return Echec(portance <= 0 ? Seul("lois.r.coule") : Seul("lois.r.plancherEnfonce"));
        return new(Effet.Flottaison, Math.Clamp(portance / (2 * charge), 0.2, 1), Seul("lois.r.porte"));
    }

    /// <summary>Ce que porte un objet sur l'eau en plus de son propre poids, en kg (poussée d'Archimède).</summary>
    public static double Portance(Assemblage a)
    {
        // Une coque déplace le volume du tronc entier, et ne pèse plus qu'une part de son bois.
        if (a.Montage == Montage.Creuse) return a.NCorps * De(a.Corps).Masse * (1 / Math.Max(0.05, De(a.Corps).Densite) - PartGardeeCreusee);
        double Net(Matiere m, int n) => n * De(m).Masse * (1 / Math.Max(0.05, De(m).Densite) - 1);
        return Net(a.Corps, a.NCorps) + Net(a.Tete, a.NTete);
    }

    // ------------------------------------------------------------------ La mer

    /// <summary>Part du bois qui reste d'un tronc évidé en coque (estimation : parois de quelques centimètres).</summary>
    public const double PartGardeeCreusee = 0.35;

    /// <summary>
    /// Hauteur des vagues (significative, en mètres) que l'objet supporte sans se défaire ni se remplir. Des pièces liées
    /// côte à côte tiennent d'autant mieux que le lien est solide et qu'elles sont nombreuses ; une coque creusée, basse
    /// sur l'eau, se remplit dès que la mer se creuse (estimations : les radeaux de balsa et les bateaux de roseaux ont
    /// traversé des océans, les pirogues monoxyles restent des embarcations côtières ; McGrail, 2001).
    /// </summary>
    public static double HouleMax(Assemblage a, double qualite)
    {
        var q = 0.6 + 0.4 * Math.Clamp(qualite, 0, 1);
        if (a.Montage == Montage.Creuse) return (0.7 + 0.4 * qualite) * (a.NCorps == 1 ? 1 : 0);
        if (a.Montage != Montage.Cote || Portance(a) <= 0) return 0;
        var matiere = a.Corps switch
        {
            Matiere.Tronc => 1.0,
            Matiere.Roseau => 0.9,
            Matiere.Branche => 0.7,
            Matiere.BoisSec => 0.6,
            _ => 0.5,
        };
        return matiere * (0.5 + 2.5 * De(a.Lien).Lien * Math.Min(1, a.NCorps / 4.0)) * q;
    }

    /// <summary>Hauteur des vagues au-dessous de laquelle un bateau mérite d'aller en mer (et pas seulement sur un lac).</summary>
    public const double HouleDeMer = 0.8;

    /// <summary>
    /// Mettre l'objet à l'eau, au bord de la mer, et le charger : il faut qu'il porte la charge et qu'il tienne dans les
    /// vagues qui viennent mourir sur le rivage.
    /// </summary>
    public static Verdict TenirLaMer(Assemblage a, double qualite, double charge, bool feu)
    {
        var flotte = Flotter(a, charge, feu);
        if (!flotte.Reussi) return flotte;
        var houle = HouleMax(a, qualite);
        if (houle < HouleDeMer) return Echec(a.Montage == Montage.Creuse ? Seul("lois.r.coqueRemplie") : Seul("lois.r.vagueDefait"));
        return new(Effet.Navigation, Math.Clamp(houle / 3, 0.2, 1), a.Montage == Montage.Creuse ? Seul("lois.r.coqueTient") : Seul("lois.r.tientLaMer"));
    }

    /// <summary>
    /// Vitesse de croisière, en km/h, en pagayant : environ 4 à 5 km/h pour une pirogue, 1,5 à 2,8 km/h pour un radeau
    /// (d'autant plus lent qu'il est large) ; à mains nues ou avec une branche, sept dixièmes de cela.
    /// </summary>
    public static double Vitesse(Assemblage a, bool pagaie)
    {
        var v = a.Montage == Montage.Creuse ? 4.5 : Math.Clamp(3.0 - 0.1 * a.NCorps, 1.5, 2.8) * (a.Corps == Matiere.Roseau ? 1.2 : 1);
        // Sans pagaie, à mains nues ou avec une branche : on avance, mais moins vite.
        return pagaie ? v : 0.7 * v;
    }

    /// <summary>
    /// Risque de chavirer ou de se défaire en une heure, selon la hauteur des vagues rapportée à ce que l'objet supporte :
    /// presque nul par mer calme, quelques pour cent par heure quand elle est deux à trois fois trop forte (estimation).
    /// </summary>
    public static double RisqueParHeure(double houle, double houleMax)
    {
        if (houleMax <= 0) return 1;
        var r = houle / houleMax;
        return Math.Min(0.5, 0.0004 * r * r * r * r);
    }

    private static bool Force(double necessaire, double disponible) => necessaire <= disponible;

    private static Verdict Echec(Recit raison) => new(Effet.Rien, 0, raison);

    // ------------------------------------------------------------------ Dire

    /// <summary>
    /// L'objet décrit par ce qu'on en voit (pour le journal et la fiche), suivi d'un nom qu'un observateur d'aujourd'hui lui
    /// donnerait : ce nom est déduit de sa forme, il ne guide rien.
    /// </summary>
    public static Recit Decrire(Assemblage a)
    {
        object avec = a.Lien == Matiere.Aucune ? "" : new Recit("lois.avec", ("lien", Physique.Nom(a.Lien)));
        var corps = Nombre(a.Corps, a.NCorps);
        var (texte, nom) = a.Montage switch
        {
            Montage.Bout => (new Recit("lois.bout", ("tete", Physique.Nom(a.Tete)), ("avec", avec), ("corps", Physique.Nom(a.Corps))),
                (De(a.Tete).Forme & Forme.Pointu) != 0 ? "lance" : null),
            Montage.Travers => (new Recit("lois.travers", ("tete", Physique.Nom(a.Tete)), ("avec", avec), ("corps", Physique.Nom(a.Corps))), "hache"),
            Montage.Cote => (new Recit("lois.cote", ("corps", corps), ("m", Physique.Nom(a.Corps)), ("k", a.NCorps), ("avec", avec)),
                a.Corps == Matiere.Tronc || Portance(a) > 40 ? "radeau" : "traineau"),
            Montage.Dessous => (new Recit("lois.dessous", ("corps", corps)), (De(a.Corps).Forme & Forme.Long) != 0 && a.NCorps > 1 ? "rouleaux" : null),
            Montage.Creuse => (new Recit("lois.creuse", ("corps", corps)), a.Corps == Matiere.Tronc ? "pirogue" : null),
            Montage.Axe => (new Recit("lois.axe", ("corps", corps), ("m", Physique.Nom(a.Corps)), ("k", a.NCorps), ("avec", avec),
                ("tete", Nombre(a.Tete, a.NTete)), ("kt", a.NTete)), a.NTete == 1 ? "brouette" : "chariot"),
            _ => (Seul("lois.objet"), null),
        };
        return nom is null ? texte : new Recit("lois.nomme", ("objet", texte), ("nom", Seul($"lois.nom.{nom}")));
    }

    /// <summary>Le savoir sur un objet assemblé, tel que l'humain se le dit.</summary>
    public static Recit Savoir(Effet e, Assemblage a) => e switch
    {
        Effet.PointeEmmanchee or Effet.TranchantEmmanche => new Recit($"lois.savoir.{e}", ("tete", Physique.Nom(a.Tete))),
        Effet.Glisser or Effet.Flottaison => new Recit($"lois.savoir.{e}", ("corps", Nombre(a.Corps, a.NCorps)), ("m", Physique.Nom(a.Corps)), ("k", a.NCorps)),
        Effet.Rouleaux => new Recit("lois.savoir.Rouleaux", ("corps", Nombre(a.Corps, a.NCorps))),
        Effet.RouleSurAxe => a.NTete == 1 ? Seul("lois.savoir.brouette") : new Recit("lois.savoir.RouleSurAxe", ("tete", Nombre(a.Tete, a.NTete))),
        Effet.Rien => new Recit("lois.savoir.Rien", ("objet", Decrire(a))),
        Effet.Navigation => new Recit("lois.savoir.Navigation", ("objet", Decrire(a))),
        _ => Decrire(a),
    };

    /// <summary>Famille d'un objet assemblé pour la chronologie : une roue seule et plusieurs roues sont deux trouvailles.</summary>
    public static string Structure(string? pieces) =>
        pieces is not null && pieces.StartsWith("T|", StringComparison.Ordinal) && Comportement.LireRecette(pieces) is { } r
            ? string.Create(CultureInfo.InvariantCulture, $"T|{(int)r.A}|{string.Join('+', r.Changements)}|{r.Famille}")
        : pieces is not null && pieces.StartsWith("M|", StringComparison.Ordinal) ? pieces
        : pieces is not null && pieces.StartsWith("F|", StringComparison.Ordinal) ? Facons.Structure(pieces)
        : pieces is not null && pieces.StartsWith("R|", StringComparison.Ordinal) ? pieces
        : Assemblage.Lire(pieces) is { Montage: Montage.Axe } a ? (a.NTete == 1 ? "axe:1" : "axe:2") : "";

    /// <summary>« un galet », « deux galets », « trois bouts de bois sec ».</summary>
    private static Recit Nombre(Matiere m, int n) => n <= 1 ? Physique.Nom(m)
        : new Recit("lois.nombre", ("n", n <= 10 ? Seul($"chiffre.{n}") : n), ("m", Seul($"matiere.{m}.pl")));

    private static Recit Seul(string cle) => Recits.Recit.Seul(cle);
}
