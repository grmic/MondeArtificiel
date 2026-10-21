using System.Globalization;
using Monde.Simulation.Monde;

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

    /// <summary>Masse totale, en kg.</summary>
    public double Masse => NCorps * Lois.De(Corps).Masse + NTete * Lois.De(Tete).Masse + Lois.De(Lien).Masse;
}

/// <summary>Ce qu'un objet assemblé a donné à l'essai, et pourquoi.</summary>
/// <param name="Raison">Ce que l'humain a vu arriver, dit simplement (pour le journal).</param>
public readonly record struct Verdict(Effet Effet, double Qualite, string Raison)
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
        _ => new(Forme.Aucune, 0, 0, 1),
    };

    /// <summary>Frottement d'une charge traînée sur ce terrain (coefficient, sans unité).</summary>
    public static double Frottement(Biome b) => b switch
    {
        Biome.SommetEnneige => 0.1,
        Biome.PlaineDeFougeres => 0.35,
        Biome.Desert or Biome.Plage => 0.55,
        Biome.Marais => 0.55,
        Biome.Montagne => 0.5,
        _ => 0.45,
    };

    /// <summary>Résistance au roulement d'une chose ronde sur ce terrain : faible sur sol dur, forte dans le sable ou la boue.</summary>
    public static double Sol(Biome b) => b switch
    {
        Biome.PlaineDeFougeres => 0.05,
        Biome.Plage or Biome.Desert => 0.25,
        Biome.Marais => 0.35,
        Biome.Montagne or Biome.SommetEnneige => 0.3,
        Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.Riviere => 0.15,
        _ => 0.1,
    };

    /// <summary>Force d'un lien entre les pièces (0 sans lien) ; la résine ne colle que chauffée.</summary>
    public static double ForceDuLien(Matiere lien, bool feu) => lien == Matiere.Resine && !feu ? 0 : De(lien).Lien;

    // ------------------------------------------------------------------ Juger un objet assemblé

    /// <summary>L'objet en main pour frapper, percer ou chasser : la pièce dure tient-elle au manche, et dans quel sens ?</summary>
    public static Verdict Emmancher(Assemblage a, bool feu, double adresse, Alea alea)
    {
        var corps = De(a.Corps);
        var tete = De(a.Tete);
        if (a.Montage is not (Montage.Bout or Montage.Travers) || a.NTete != 1 || (corps.Forme & Forme.Long) == 0)
            return Echec("les pièces ne tiennent pas comme une arme dans la main");
        var lien = ForceDuLien(a.Lien, feu);
        if (lien <= 0) return Echec(a.Lien == Matiere.Resine ? "la résine froide ne colle pas" : "rien ne retient la pierre contre le bois");
        if (tete.Durete < corps.Durete + 0.2) return Echec("la pièce du bout n'est pas plus dure que le manche : elle s'écrase au premier coup");
        // Le lien lâche d'autant plus vite que la main est maladroite et le lien faible.
        if (alea.Reel() > 0.25 + 0.5 * lien + 0.3 * adresse) return Echec("au premier coup, le lien glisse et la pierre tombe");
        var qualite = Math.Clamp(0.3 + 0.4 * lien + 0.3 * adresse * alea.Reel(), 0, 1);
        if (a.Montage == Montage.Bout && (tete.Forme & Forme.Pointu) != 0) return new(Effet.PointeEmmanchee, qualite, "la pointe de pierre entre loin dans le bois");
        if (a.Montage == Montage.Travers && tete.Masse >= 0.05 && (tete.Forme & Forme.Tranchant) != 0 || a.Montage == Montage.Travers && tete.Masse >= 0.4)
            return new(Effet.TranchantEmmanche, qualite * (tete.Masse >= 0.4 ? 1 : 0.8), "le coup porté au bout du manche frappe bien plus fort qu'à main nue");
        return Echec(a.Montage == Montage.Bout ? "la pierre au bout du bâton n'a pas de pointe : elle ne perce rien" : "la pierre en travers est trop légère pour frapper fort");
    }

    /// <summary>Force à la coupe d'un outil tenu en main (0 à 1) : une pierre tranchante seule, ou emmanchée en travers.</summary>
    public static double Coupe(Objet o)
    {
        if (o.Matiere == Matiere.Assemblage && Assemblage.Lire(o.Pieces) is { Montage: Montage.Travers } a)
            return Math.Clamp((0.45 + 0.4 * o.Qualite) * Math.Max(0.2, o.Usure) * (De(a.Tete).Masse >= 0.4 ? 1 : 0.85), 0, 1);
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
            if ((corps.Forme & Forme.Rond) == 0) return Echec("la charge posée sur ces pièces ne glisse pas mieux que sur le sol");
            if ((corps.Forme & Forme.Long) == 0)
                return Echec("les pièces rondes roulent un peu, puis s'échappent de sous la charge, chacune de son côté");
            if (a.NCorps < 2) return Echec("sur un seul tronc, la charge bascule et retombe");
            var roule = 0.4 * Sol(sol) + 0.02;
            if (Sol(sol) >= 0.25) return Echec("les troncs s'enfoncent dans le sol mou et ne roulent plus");
            // Quelques centaines de mètres à l'heure : bon pour hisser une lourde charge, pas pour la porter au loin.
            if (km / 0.4 > 3) return Echec("les troncs roulent, mais il faut sans cesse reporter celui de derrière devant : la route est bien trop longue ainsi");
            return Force(roule * charge * Gravite, traction) ? new(Effet.Rouleaux, Math.Clamp(1 - roule * 5, 0.2, 1), "la charge avance sur les troncs qui roulent dessous")
                : Echec("même sur les troncs, la charge est trop lourde à pousser");
        }
        if (a.Montage is not (Montage.Cote or Montage.Axe) || (corps.Forme & Forme.Long) == 0)
            return Echec("on ne peut rien poser sur cet objet");
        if (lien <= 0) return Echec(a.Lien == Matiere.Resine ? "la résine froide ne colle pas : les pièces se séparent" : "les pièces non liées se séparent dès qu'on tire");
        if (a.NCorps < 2) return Echec("une seule pièce ne fait pas une plate-forme : la charge glisse de côté");
        var porte = a.NCorps * corps.Resistance * (0.5 + 0.5 * lien);
        if (charge > porte) return Echec("les pièces plient et cèdent sous la charge");

        if (a.Montage == Montage.Axe)
        {
            if ((tete.Forme & Forme.Rond) == 0) return Echec("les pièces enfilées ne sont pas rondes : elles ne tournent pas");
            if ((tete.Forme & Forme.Perce) == 0) return Echec("les rondelles ne tiennent pas sur le bâton : il faudrait qu'il les traverse");
            if (a.NTete < 1) return Echec("rien ne tourne sous la charge");
            var c = Sol(sol);
            if (!Force(c * total * Gravite, traction)) return Echec("les rondelles s'enfoncent : il faut trop de force pour avancer");
            if (a.NTete == 1)
            {
                // Une seule roue : celui qui pousse soulève les deux bras et porte une part de la charge.
                if (a.NCorps < 2) return Echec("sur une seule roue, la charge verse de côté");
                if (0.3 * total > soulever) return Echec("sur une seule roue, il faut soulever trop lourd pour garder l'équilibre");
                return new(Effet.RouleSurAxe, Math.Clamp(1 - 2.5 * c, 0.2, 1), "sur sa rondelle qui tourne, la charge avance, tenue par les deux bras");
            }
            return new(Effet.RouleSurAxe, Math.Clamp(1 - 2.5 * c, 0.2, 1), "les rondelles tournent sous la charge, qui avance presque seule");
        }

        // Plate-forme traînée.
        var frottement = Frottement(sol);
        if (!Force(frottement * total * Gravite, traction)) return Echec("la charge est trop lourde à traîner sur ce sol");
        return new(Effet.Glisser, Math.Clamp(1.2 - 1.5 * frottement, 0.2, 1), "les branches liées glissent sur le sol avec leur charge");
    }

    /// <summary>Monter sur l'eau sur cet objet : il faut qu'il porte la charge (Archimède) et qu'il ne chavire pas.</summary>
    public static Verdict Flotter(Assemblage a, double charge, bool feu)
    {
        var corps = De(a.Corps);
        if (a.Montage is not (Montage.Cote or Montage.Axe))
            return a.NCorps == 1 && corps.Densite < 1 && (corps.Forme & Forme.Rond) != 0
                ? Echec("le tronc flotte, mais roule sur lui-même et renverse qui s'y tient")
                : Echec("ces pièces ne font pas un plancher sur l'eau");
        if (ForceDuLien(a.Lien, feu) <= 0) return Echec("les pièces non liées s'écartent dans le courant");
        if (a.NCorps < 2) return Echec("une seule pièce roule sur elle-même et renverse qui monte dessus");
        var portance = Portance(a);
        if (portance < charge) return Echec(portance <= 0 ? "le bois coule" : "le plancher s'enfonce sous son poids");
        return new(Effet.Flottaison, Math.Clamp(portance / (2 * charge), 0.2, 1), "le bois lié le porte au-dessus de l'eau");
    }

    /// <summary>Ce que porte un objet sur l'eau en plus de son propre poids, en kg (poussée d'Archimède).</summary>
    public static double Portance(Assemblage a)
    {
        double Net(Matiere m, int n) => n * De(m).Masse * (1 / Math.Max(0.05, De(m).Densite) - 1);
        return Net(a.Corps, a.NCorps) + Net(a.Tete, a.NTete);
    }

    private static bool Force(double necessaire, double disponible) => necessaire <= disponible;

    private static Verdict Echec(string raison) => new(Effet.Rien, 0, raison);

    // ------------------------------------------------------------------ Dire

    /// <summary>
    /// L'objet décrit par ce qu'on en voit (pour le journal et la fiche), suivi d'un nom qu'un observateur d'aujourd'hui lui
    /// donnerait : ce nom est déduit de sa forme, il ne guide rien.
    /// </summary>
    public static string Decrire(Assemblage a)
    {
        var avec = a.Lien == Matiere.Aucune ? "" : $" avec {Physique.Nom(a.Lien)}";
        var corps = Nombre(a.Corps, a.NCorps);
        var (texte, nom) = a.Montage switch
        {
            Montage.Bout => ($"{Physique.Nom(a.Tete)} {Lie(a.Tete, 1)}{avec} au bout {De(Physique.Nom(a.Corps))}", (De(a.Tete).Forme & Forme.Pointu) != 0 ? "une lance" : null),
            Montage.Travers => ($"{Physique.Nom(a.Tete)} {Lie(a.Tete, 1)}{avec} en travers {De(Physique.Nom(a.Corps))}", "une hache"),
            Montage.Cote => ($"{corps} {Lie(a.Corps, a.NCorps)} côte à côte{avec}", a.Corps == Matiere.Tronc || Portance(a) > 40 ? "un radeau" : "un traîneau"),
            Montage.Dessous => ($"une charge posée sur {corps}", (De(a.Corps).Forme & Forme.Long) != 0 && a.NCorps > 1 ? "des rouleaux" : null),
            Montage.Axe => ($"{corps} {Lie(a.Corps, a.NCorps)} côte à côte{avec}, sur {Nombre(a.Tete, a.NTete)} enfilée{(a.NTete > 1 ? "s" : "")} sur un bâton",
                a.NTete == 1 ? "une brouette" : "un chariot"),
            _ => ("un objet", null),
        };
        return nom is null ? texte : $"{texte} ({nom})";
    }

    /// <summary>Le savoir sur un objet assemblé, tel que l'humain se le dit.</summary>
    public static string Savoir(Effet e, Assemblage a) => e switch
    {
        Effet.PointeEmmanchee => $"{Majuscule(Physique.Nom(a.Tete))} {Lie(a.Tete)} au bout d'un bâton perce plus loin et plus fort.",
        Effet.TranchantEmmanche => $"{Majuscule(Physique.Nom(a.Tete))} {Lie(a.Tete)} en travers d'un manche frappe bien plus fort.",
        Effet.Glisser => $"{Majuscule(Nombre(a.Corps, a.NCorps))} {Lie(a.Corps, a.NCorps)} côte à côte font glisser une lourde charge.",
        Effet.Rouleaux => $"Une charge posée sur {Nombre(a.Corps, a.NCorps)} avance quand ils roulent dessous.",
        Effet.RouleSurAxe => a.NTete == 1
            ? "Une charge posée sur une rondelle percée qui tourne autour d'un bâton avance en la tenant par deux bras."
            : $"Une charge posée sur {Nombre(a.Tete, a.NTete)} qui tournent autour d'un bâton avance presque seule.",
        Effet.Flottaison => $"{Majuscule(Nombre(a.Corps, a.NCorps))} {Lie(a.Corps, a.NCorps)} côte à côte portent un humain sur l'eau.",
        Effet.Rien => $"{Majuscule(Decrire(a))} : cela ne marche pas.",
        _ => Decrire(a),
    };

    /// <summary>Famille d'un objet assemblé pour la chronologie : une roue seule et plusieurs roues sont deux trouvailles.</summary>
    public static string Structure(string? pieces) => Assemblage.Lire(pieces) is { Montage: Montage.Axe } a ? (a.NTete == 1 ? "axe:1" : "axe:2") : "";

    private static string Nombre(Matiere m, int n)
    {
        if (n <= 1) return Physique.Nom(m);
        var pluriel = m switch
        {
            Matiere.BoisSec => "bouts de bois sec",
            Matiere.Rondelle => "rondelles de tronc",
            Matiere.RondellePercee => "rondelles percées",
            Matiere.BatonPointu => "bâtons pointus",
            Matiere.Fibres or Matiere.GrandesFeuilles => Sans(Physique.Nom(m)),
            _ => Sans(Physique.Nom(m)) + "s",
        };
        return $"{Chiffre(n)} {pluriel}";
    }

    /// <summary>« d'une branche », « de bois sec ».</summary>
    private static string De(string nom) => nom.StartsWith("du ", StringComparison.Ordinal) ? "de " + nom[3..]
        : nom.StartsWith("des ", StringComparison.Ordinal) ? "de " + nom[4..] : "d'" + nom;

    /// <summary>Accord de « lié » avec le nom de la pièce.</summary>
    private static string Lie(Matiere m, int n = 1) =>
        (Physique.Nom(m).StartsWith("une ", StringComparison.Ordinal) ? "liée" : "lié") + (n > 1 ? "s" : "");

    private static string Chiffre(int n) => n switch
    {
        2 => "deux", 3 => "trois", 4 => "quatre", 5 => "cinq", 6 => "six", 7 => "sept", 8 => "huit", 9 => "neuf", 10 => "dix",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Le nom sans son article (« une branche » → « branche »).</summary>
    private static string Sans(string nom)
    {
        foreach (var article in new[] { "un ", "une ", "du ", "de la ", "de l'", "des ", "de " })
            if (nom.StartsWith(article, StringComparison.Ordinal)) return nom[article.Length..];
        return nom;
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
