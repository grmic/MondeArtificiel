using System.Collections.Concurrent;
using System.Globalization;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'on fait subir à une seule pièce. Ce sont des gestes de la main, pas des inventions : aucun ne nomme l'objet qui en
/// sortira. Leur suite, d'une pièce à l'autre, forme une chaîne opératoire de longueur libre (Leroi-Gourhan, 1964).
/// </summary>
public enum Facon : byte
{
    /// <summary>Rendre pointu (le bois, en le raclant d'un tranchant ou sur une pierre rugueuse).</summary>
    Aiguiser = 1,
    /// <summary>Fendre dans le sens du fil : une pièce plus mince, plus légère, plus souple.</summary>
    Fendre = 2,
    /// <summary>Creuser l'intérieur (au tranchant, ou en laissant ronger la braise).</summary>
    Evider = 3,
    /// <summary>Plier une pièce souple et la laisser prendre sa forme près du feu ou dans l'eau.</summary>
    Courber = 4,
    /// <summary>Entrecroiser des brins souples : une natte, une paroi tressée.</summary>
    Tresser = 5,
    /// <summary>Frotter une pierre sur une pierre rugueuse ou dans le sable mouillé : un fil plus net et plus solide.</summary>
    Polir = 6,
    /// <summary>Tailler une encoche ou un ergot (le crochet d'un propulseur, le talon d'une hampe).</summary>
    Encocher = 7,
    /// <summary>Durcir au feu (le bois sèche et durcit, mais devient cassant).</summary>
    Durcir = 8,
    /// <summary>Percer de part en part avec une pointe dure qu'on fait tourner.</summary>
    Percer = 9,
}

/// <summary>Comment deux pièces tiennent ensemble.</summary>
public enum Jointure : byte
{
    /// <summary>La seconde au bout de la première (dans l'axe).</summary>
    Bout = 1,
    /// <summary>La seconde en travers de la première, comme une tête sur un manche.</summary>
    Travers = 2,
    /// <summary>La seconde (un lien) attachée aux deux bouts de la première, qu'elle tient pliée.</summary>
    Bouts = 3,
    /// <summary>La première pousse la seconde (ce qui revient d'un coup, ou un levier qui allonge le bras).</summary>
    Propulse = 4,
    /// <summary>La seconde étalée sur la première (une terre, une résine chauffée, une poix).</summary>
    Enduire = 5,
    /// <summary>La première, percée, cousue avec la seconde.</summary>
    Coudre = 6,
    /// <summary>La première, petite et crochue, pendue au bout de la seconde, longue.</summary>
    Ligne = 7,
}

/// <summary>À quoi sert une chose. Ce sont des besoins du corps (manger, boire, couper), pas des objets.</summary>
public enum Fonction : byte
{
    Aucune = 0,
    /// <summary>Atteindre et percer une bête de loin ou de près (voir <see cref="Lois.Chasse"/>).</summary>
    Chasser = 1,
    /// <summary>Trancher la viande, le bois, une peau (voir <see cref="Lois.Coupe"/>).</summary>
    Couper = 2,
    /// <summary>Garder de l'eau sur soi.</summary>
    Contenir = 3,
    /// <summary>Prendre du poisson.</summary>
    Pecher = 4,
}

/// <summary>
/// Les propriétés d'une chose, qu'elle soit brute ou faite de main d'homme : ce que les lois regardent pour dire si elle coupe,
/// lance, contient ou pêche. Les unités sont physiques quand c'est possible (kg, m, litres, joules, m/s).
/// </summary>
/// <param name="Souplesse">0 : raide et cassant ; 1 : se plie sans résister (une corde).</param>
/// <param name="Fil">Qualité d'un tranchant, de 0 (aucun) à 1 (l'obsidienne fraîche).</param>
/// <param name="Etanche">Part de l'eau qu'une paroi retient (0 à 1).</param>
/// <param name="Contenance">Volume qu'on peut y mettre, en litres.</param>
/// <param name="Tension">Énergie gardée dans une pièce tenue pliée, en joules.</param>
/// <param name="Vitesse">Vitesse que la chose donne à ce qu'elle lance, en m/s (0 : rien).</param>
public readonly record struct Traits(Forme Forme, double Durete, double Masse, double Densite, double Lien, double Resistance,
    double Longueur, double Souplesse, double Fil, double Etanche, double Contenance, double Tension = 0, double Vitesse = 0)
{
    public bool A(Forme f) => (Forme & f) != 0;
}

/// <summary>Ce dont dispose celui qui façonne : ses outils, le feu, l'eau.</summary>
/// <param name="DureteOutil">Dureté de la pierre la plus dure qu'il a sous la main (0 si aucune).</param>
/// <param name="FilOutil">Le meilleur tranchant qu'il a sous la main.</param>
/// <param name="PointeDure">Une pointe dure pour percer (éclat, obsidienne, alêne).</param>
/// <param name="Abrasif">Du sable, une pierre rugueuse pour frotter.</param>
public readonly record struct Atelier(double DureteOutil, double FilOutil, bool PointeDure, bool Feu, bool Eau, bool Abrasif)
{
    /// <summary>Tout ce qu'il faut (pour juger une chose déjà faite).</summary>
    public static Atelier Complet { get; } = new(1, 1, true, true, true, true);
}

/// <summary>
/// Une chose faite de main d'homme, décrite par la façon dont elle a été faite : une matière brute, une pièce façonnée, ou deux
/// pièces jointes. Rien ici ne nomme l'arc, le panier ni l'hameçon : seulement des pièces, des gestes et des liens.
/// <para>
/// Écriture (gardée avec le savoir et l'objet, et lisible de l'extérieur, par exemple par l'Entité) : une matière brute est son
/// numéro (<c>5</c>) ; une façon est une majuscule suivie de la pièce entre parenthèses (<c>A(5)</c> : une branche aiguisée) ;
/// une jointure est une minuscule, le numéro du lien, puis les deux pièces (<c>b19(A(F(5)),17)</c>).
/// </para>
/// </summary>
public abstract record Facture
{
    public const int ProfondeurMax = 7;
    public const int PiecesMax = 5;
    public const int LongueurMax = 120;

    public abstract string Ecrire();

    /// <summary>Profondeur de la chaîne (1 pour une matière brute).</summary>
    public abstract int Profondeur { get; }

    /// <summary>Matières brutes qu'il faut, avec leur nombre.</summary>
    public abstract void Compter(Dictionary<Matiere, int> n);

    /// <summary>Les façons et jointures employées.</summary>
    public abstract void Gestes(HashSet<Facon> facons, HashSet<Jointure> jointures);

    public IReadOnlyDictionary<Matiere, int> Matieres()
    {
        var n = new Dictionary<Matiere, int>();
        Compter(n);
        return n;
    }

    public int NombreDePieces => Matieres().Values.Sum();

    /// <summary>La matière principale (la plus à gauche), pour ranger le savoir.</summary>
    public abstract Matiere Principale { get; }

    public static Facture? Lire(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > LongueurMax) return null;
        var i = 0;
        var f = Lire(s, ref i, 0);
        return f is null || i != s.Length || f.NombreDePieces > PiecesMax ? null : f;
    }

    private static Facture? Lire(string s, ref int i, int profondeur)
    {
        if (profondeur >= ProfondeurMax || i >= s.Length) return null;
        var c = s[i];
        if (char.IsAsciiDigit(c))
        {
            var debut = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            if (!int.TryParse(s.AsSpan(debut, i - debut), NumberStyles.None, CultureInfo.InvariantCulture, out var m) || !EstBrute((Matiere)m)) return null;
            return new Brute((Matiere)m);
        }
        if (char.IsAsciiLetterUpper(c))
        {
            var f = FaconDe(c);
            if (f is null) return null;
            i++;
            if (i >= s.Length || s[i] != '(') return null;
            i++;
            var x = Lire(s, ref i, profondeur + 1);
            if (x is null || i >= s.Length || s[i] != ')') return null;
            i++;
            return new Faconnee(f.Value, x);
        }
        if (char.IsAsciiLetterLower(c))
        {
            var j = JointureDe(c);
            if (j is null) return null;
            i++;
            var debut = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            if (i == debut || !int.TryParse(s.AsSpan(debut, i - debut), NumberStyles.None, CultureInfo.InvariantCulture, out var lien)
                || !(lien == 0 || EstBrute((Matiere)lien))) return null;
            if (i >= s.Length || s[i] != '(') return null;
            i++;
            var a = Lire(s, ref i, profondeur + 1);
            if (a is null || i >= s.Length || s[i] != ',') return null;
            i++;
            var b = Lire(s, ref i, profondeur + 1);
            if (b is null || i >= s.Length || s[i] != ')') return null;
            i++;
            return new Jointe(j.Value, (Matiere)lien, a, b);
        }
        return null;
    }

    /// <summary>Matières qui peuvent entrer dans une chose façonnée (pas l'eau, le feu ni une bête entière).</summary>
    public static bool EstBrute(Matiere m) => Enum.IsDefined(m) && m is not (Matiere.Aucune or Matiere.Eau or Matiere.Feu or Matiere.Carcasse
        or Matiere.Plante or Matiere.Assemblage or Matiere.Facture or Matiere.Brandon or Matiere.PorteFeu or Matiere.Minerai);

    private const string LettresFacons = "AFECTPNDR";
    private const string LettresJointures = "btkpeol";

    public static char Lettre(Facon f) => LettresFacons[(int)f - 1];
    public static char Lettre(Jointure j) => LettresJointures[(int)j - 1];

    private static Facon? FaconDe(char c) => LettresFacons.IndexOf(c) is >= 0 and var k ? (Facon)(k + 1) : null;
    private static Jointure? JointureDe(char c) => LettresJointures.IndexOf(c) is >= 0 and var k ? (Jointure)(k + 1) : null;

    public override string ToString() => Ecrire();
}

public sealed record Brute(Matiere M) : Facture
{
    public override string Ecrire() => ((int)M).ToString(CultureInfo.InvariantCulture);
    public override int Profondeur => 1;
    public override void Compter(Dictionary<Matiere, int> n) => n[M] = n.GetValueOrDefault(M) + 1;
    public override void Gestes(HashSet<Facon> facons, HashSet<Jointure> jointures) { }
    public override Matiere Principale => M;
}

public sealed record Faconnee(Facon F, Facture X) : Facture
{
    public override string Ecrire() => $"{Lettre(F)}({X.Ecrire()})";
    public override int Profondeur => 1 + X.Profondeur;
    public override void Compter(Dictionary<Matiere, int> n) => X.Compter(n);
    public override void Gestes(HashSet<Facon> facons, HashSet<Jointure> jointures)
    {
        facons.Add(F);
        X.Gestes(facons, jointures);
    }
    public override Matiere Principale => X.Principale;
}

public sealed record Jointe(Jointure J, Matiere Lien, Facture A, Facture B) : Facture
{
    public override string Ecrire() => string.Create(CultureInfo.InvariantCulture, $"{Lettre(J)}{(int)Lien}({A.Ecrire()},{B.Ecrire()})");
    public override int Profondeur => 1 + Math.Max(A.Profondeur, B.Profondeur);
    public override void Compter(Dictionary<Matiere, int> n)
    {
        A.Compter(n);
        B.Compter(n);
        if (Lien != Matiere.Aucune) n[Lien] = n.GetValueOrDefault(Lien) + 1;
    }
    public override void Gestes(HashSet<Facon> facons, HashSet<Jointure> jointures)
    {
        jointures.Add(J);
        A.Gestes(facons, jointures);
        B.Gestes(facons, jointures);
    }
    public override Matiere Principale => A.Principale;
}

/// <summary>Ce que donne une chose façonnée : ses propriétés, ou ce qui a empêché de la faire.</summary>
public readonly record struct Construction(Traits Traits, Recit? Echec)
{
    public bool Reussie => Echec is null;
}

/// <summary>
/// Les lois qui disent ce que devient une pièce qu'on façonne ou qu'on joint à une autre, et ce que vaut le résultat pour
/// chaque besoin. Elles ne connaissent que des propriétés : l'arc n'y figure pas, mais une branche souple tenue pliée par
/// une corde garde de l'énergie, et cette énergie pousse d'autant plus vite une hampe qu'elle est légère.
/// <para>
/// Ordres de grandeur. Un arc de bois simple stocke de 30 à 60 J et lance une flèche de 20 à 40 g vers 40 à 50 m/s ; un
/// propulseur porte la vitesse d'une sagaie de 20 à 30 m/s (Whittaker, 2010 ; Churchill, 1993 : les armes lancées tuent mieux
/// de loin). Une lance jetée à la main va à 15-20 m/s. Une flèche doit porter une dizaine de joules au moins pour traverser
/// la peau d'un cerf ; une lance d'épaule en porte bien plus. Un panier tressé ne garde l'eau que si on l'enduit
/// (argile, résine, bitume) ; une outre cousue d'une peau en garde une dizaine de litres.
/// </para>
/// </summary>
public static class Facons
{
    /// <summary>Propriétés d'une matière brute (s'ajoutent à <see cref="Lois.De"/> : longueur, souplesse, fil, étanchéité, contenance).</summary>
    public static Traits De(Matiere m)
    {
        var p = Lois.De(m);
        var (longueur, souplesse, fil, etanche, contenance) = m switch
        {
            Matiere.Galet => (0.08, 0.0, 0.0, 0.0, 0.0),
            Matiere.Silex => (0.1, 0, 0.3, 0, 0),
            Matiere.Pyrite => (0.06, 0, 0, 0, 0),
            Matiere.Obsidienne => (0.08, 0, 1, 0, 0),
            Matiere.BoisSec => (0.5, 0.15, 0, 0, 0),
            // Une branche fraîche plie et revient.
            Matiere.Branche => (1.5, 0.55, 0, 0, 0),
            Matiere.GrandesFeuilles => (0.4, 0.9, 0, 0.35, 0),
            Matiere.Fibres => (1, 0.95, 0, 0, 0),
            Matiere.HerbeSeche => (0.3, 0.6, 0, 0, 0),
            Matiere.Calebasse => (0.25, 0, 0, 0.95, 2),
            Matiere.Tronc => (4, 0.05, 0, 0, 0),
            Matiere.Eclat => (0.05, 0, 0.85, 0, 0),
            Matiere.BatonPointu => (1.2, 0.3, 0, 0, 0),
            Matiere.Epieu => (1.8, 0.3, 0, 0, 0),
            Matiere.Corde => (2, 1, 0, 0, 0),
            Matiere.Peau => (1, 0.9, 0, 0.7, 0),
            Matiere.Rondelle or Matiere.RondellePercee => (0.4, 0, 0, 0, 0),
            Matiere.Roseau => (2, 0.35, 0, 0, 0),
            Matiere.Calcaire => (0.1, 0, 0, 0, 0),
            Matiere.Coquillage => (0.05, 0, 0.4, 0.9, 0.05),
            Matiere.Ecorce => (0.5, 0.5, 0, 0.5, 0),
            _ => (0.0, 0.0, 0.0, 0.0, 0.0),
        };
        return new Traits(p.Forme, p.Durete, p.Masse, p.Densite, p.Lien, p.Resistance, longueur, souplesse, fil, etanche, contenance);
    }

    private static readonly ConcurrentDictionary<string, Construction> Cache = new(StringComparer.Ordinal);

    /// <summary>Propriétés d'une chose faite (gardées en cache : les lois ne changent pas).</summary>
    public static Construction Calculer(Facture f) =>
        Cache.Count < 50_000 ? Cache.GetOrAdd(f.Ecrire(), _ => Construire(f, Atelier.Complet)) : Construire(f, Atelier.Complet);

    /// <summary>Faire la chose avec ce qu'on a : la matière s'y prête-t-elle, et a-t-on l'outil qu'il faut ?</summary>
    public static Construction Construire(Facture f, Atelier at) => f switch
    {
        Brute b => new(De(b.M), null),
        Faconnee x => Construire(x.X, at) is var c && c.Reussie ? Faconner(x.F, c.Traits, at) : c,
        Jointe j => Joindre(j, at),
        _ => Rate("fac.r.rien"),
    };

    private static Construction Joindre(Jointe j, Atelier at)
    {
        var ca = Construire(j.A, at);
        if (!ca.Reussie) return ca;
        var cb = Construire(j.B, at);
        return cb.Reussie ? Joindre(j.J, j.Lien, ca.Traits, cb.Traits, at) : cb;
    }

    private static Construction Rate(string cle) => new(default, Recit.Seul(cle));

    private static Construction Faconner(Facon f, Traits t, Atelier at)
    {
        var bois = t.Durete < 0.45 && t.Densite < 1.2;
        switch (f)
        {
            case Facon.Aiguiser:
                if (t.A(Forme.Pointu) && t.Durete < 0.45) return Rate("fac.r.dejaPointu");
                if (!t.A(Forme.Long) && t.Durete < 0.45) return Rate("fac.r.pasLong");
                if (t.Souplesse > 0.9) return Rate("fac.r.tropMou");
                if (t.Durete >= 0.45)
                {
                    // Une pierre ne s'aiguise qu'en frottant sur plus dur ou dans du sable : elle prend un fil, pas une pointe.
                    if (!at.Abrasif || at.DureteOutil + 0.05 < t.Durete) return Rate("fac.r.rienDePlusDur");
                    return Ok(t with { Forme = t.Forme | Forme.Tranchant, Fil = Math.Min(1, t.Fil + 0.3) });
                }
                if (at.FilOutil < 0.25 && !at.Abrasif) return Rate("fac.r.rienPourRacler");
                return Ok(t with { Forme = t.Forme | Forme.Pointu, Masse = t.Masse * 0.95 });
            case Facon.Fendre:
                if (!bois || !t.A(Forme.Long)) return Rate("fac.r.neSeFendPas");
                if (t.Souplesse > 0.9) return Rate("fac.r.tropMou");
                if (t.Masse < 0.02) return Rate("fac.r.tropMince");
                if (at.FilOutil < 0.25 && at.DureteOutil < 0.6) return Rate("fac.r.rienPourFendre");
                return Ok(t with
                {
                    Forme = (t.Forme | Forme.Plat) & ~Forme.Rond, Masse = t.Masse * 0.3, Resistance = t.Resistance * 0.3,
                    Souplesse = Math.Min(0.9, t.Souplesse + 0.1),
                });
            case Facon.Evider:
                if (t.A(Forme.Creux)) return Rate("fac.r.dejaCreux");
                if (!bois || t.Masse < 0.3) return Rate("fac.r.rienACreuser");
                if (t.Souplesse > 0.6) return Rate("fac.r.tropMou");
                if (at.FilOutil < 0.25 && !(at.Feu && at.FilOutil > 0.05)) return Rate("fac.r.rienPourCreuser");
                var volume = t.Masse / Math.Max(0.1, t.Densite);
                return Ok(t with
                {
                    Forme = t.Forme | Forme.Creux, Masse = t.Masse * 0.4, Contenance = 0.6 * volume, Etanche = Math.Max(t.Etanche, 0.85),
                    Resistance = t.Resistance * 0.5,
                });
            case Facon.Courber:
                if (!t.A(Forme.Long)) return Rate("fac.r.pasLong");
                if (t.Souplesse < 0.3) return Rate("fac.r.casse");
                if (t.Souplesse > 0.9) return Rate("fac.r.tropMou");
                if (!at.Feu && !at.Eau) return Rate("fac.r.reprendSaForme");
                // Courbée court, elle fait un crochet ; longue, un arceau.
                return Ok(t.Longueur <= 0.4 ? t with { Forme = t.Forme | Forme.Crochet } : t with { Forme = t.Forme | Forme.Rond });
            case Facon.Tresser:
                if (!t.A(Forme.Long) || t.Souplesse < 0.5) return Rate("fac.r.pasDesBrins");
                if (t.A(Forme.Creux)) return Rate("fac.r.dejaCreux");
                return Ok(t with
                {
                    Forme = (t.Forme | Forme.Plat | Forme.Creux) & ~Forme.Long, Contenance = 15 * Math.Max(0.2, t.Masse), Etanche = 0.1,
                    Resistance = t.Resistance + 10, Souplesse = 0.6, Longueur = 0.4,
                });
            case Facon.Polir:
                if (t.Durete < 0.45) return Rate("fac.r.pasUnePierre");
                if (!at.Abrasif) return Rate("fac.r.rienPourFrotter");
                return Ok(t with { Forme = t.Forme | Forme.Tranchant, Fil = Math.Min(1, Math.Max(t.Fil, 0.2) + 0.15), Durete = Math.Min(1, t.Durete + 0.02) });
            case Facon.Encocher:
                if (t.A(Forme.Crochet)) return Rate("fac.r.dejaCrochu");
                if (!bois || !t.A(Forme.Long)) return Rate("fac.r.rienAEncocher");
                if (at.FilOutil < 0.25) return Rate("fac.r.rienPourEntailler");
                return Ok(t with { Forme = t.Forme | Forme.Crochet, Resistance = t.Resistance * 0.9 });
            case Facon.Durcir:
                if (!bois) return Rate("fac.r.neDurcitPas");
                if (t.Durete >= 0.4) return Rate("fac.r.dejaDur");
                if (!at.Feu) return Rate("fac.r.sansFeu");
                return Ok(t with { Durete = t.Durete + 0.12, Souplesse = Math.Max(0, t.Souplesse - 0.1) });
            case Facon.Percer:
                if (t.A(Forme.Perce)) return Rate("fac.r.dejaPerce");
                if (!at.PointeDure) return Rate("fac.r.rienPourPercer");
                if (t.Durete > 0.6) return Rate("fac.r.tropDur");
                if (t.Masse > 30) return Rate("fac.r.tropGros");
                return Ok(t with { Forme = t.Forme | Forme.Perce, Masse = t.Masse * 0.97 });
            default:
                return Rate("fac.r.rien");
        }
    }

    private static Construction Ok(Traits t) => new(t, null);

    private static Construction Joindre(Jointure j, Matiere lien, Traits a, Traits b, Atelier at)
    {
        var force = lien == Matiere.Aucune ? 0 : Lois.ForceDuLien(lien, at.Feu);
        var masse = a.Masse + b.Masse + (lien == Matiere.Aucune ? 0 : Lois.De(lien).Masse);
        switch (j)
        {
            case Jointure.Bout or Jointure.Travers:
                if (!a.A(Forme.Long) || a.Souplesse > 0.9) return Rate("fac.r.pasUnManche");
                // Une ligature ne prend qu'un bout du lien.
                masse = a.Masse + b.Masse + 0.1 * Lois.De(lien).Masse;
                if (lien == Matiere.Aucune || force <= 0) return Rate(lien is Matiere.Resine or Matiere.Substance ? "fac.r.resineFroide" : "fac.r.rienNeTient");
                if (b.Masse > 3 * a.Masse + 1) return Rate("fac.r.teteTropLourde");
                if (j == Jointure.Bout)
                {
                    if (!b.A(Forme.Pointu | Forme.Tranchant)) return Rate("fac.r.pasDePointe");
                    return Ok(a with
                    {
                        Forme = a.Forme | Forme.Pointu | (b.Fil > 0.3 ? Forme.Tranchant : 0), Durete = Math.Max(a.Durete, b.Durete), Fil = b.Fil,
                        Masse = masse, Longueur = a.Longueur + b.Longueur, Lien = force,
                    });
                }
                if (b.Masse < 0.04) return Rate("fac.r.tropLegere");
                return Ok(a with
                {
                    Forme = a.Forme | (b.Fil > 0.2 ? Forme.Tranchant : 0), Durete = Math.Max(a.Durete, b.Durete), Fil = b.Fil, Masse = masse, Lien = force,
                });
            case Jointure.Bouts:
                // La seconde pièce est le lien tendu ; la première, tenue pliée, garde de l'énergie selon sa souplesse.
                if (!a.A(Forme.Long) || a.Longueur < 0.6) return Rate("fac.r.tropCourt");
                if (a.Souplesse < 0.25) return Rate("fac.r.casse");
                if (a.Souplesse > 0.85) return Rate("fac.r.tropMou");
                if (!b.A(Forme.Long) || b.Souplesse < 0.8) return Rate("fac.r.pasUnLien");
                if (b.Longueur < 0.9 * a.Longueur) return Rate("fac.r.lienTropCourt");
                if (b.Lien < 0.5) return Rate("fac.r.lienCasse");
                // Une pièce trop souple ne pousse pas ; trop raide, elle ne plie pas : le meilleur est entre les deux.
                var ressort = Math.Clamp(1 - Math.Abs(a.Souplesse - 0.5) / 0.35, 0, 1) * Math.Clamp(a.Resistance / 20, 0.2, 1.2);
                var energie = 60 * Math.Clamp(a.Longueur / 1.5, 0.3, 1.3) * ressort;
                if (energie < 3) return Rate("fac.r.neRevientPas");
                return Ok(a with { Forme = a.Forme | Forme.Rond, Masse = masse, Tension = energie });
            case Jointure.Propulse:
            {
                if (!b.A(Forme.Long)) return Rate("fac.r.rienALancer");
                if (a.Tension > 0)
                {
                    if (b.Masse > 0.4) return Rate("fac.r.projectileLourd");
                    // Environ sept dixièmes de l'énergie passent à la flèche.
                    var v = Math.Min(70, Math.Sqrt(2 * 0.7 * a.Tension / Math.Max(0.005, b.Masse)));
                    return Ok(b with { Vitesse = v, Masse = b.Masse, Tension = a.Tension });
                }
                if (a.A(Forme.Crochet) && a.A(Forme.Long) && a.Souplesse < 0.7)
                {
                    if (b.Masse > 1.5) return Rate("fac.r.projectileLourd");
                    // Le levier allonge le bras : la sagaie part plus vite qu'à la main.
                    var v = Math.Min(35, Main(b.Masse) * (1 + 0.6 * Math.Min(1, a.Longueur)));
                    return Ok(b with { Vitesse = v });
                }
                return Rate("fac.r.rienNePousse");
            }
            case Jointure.Enduire:
            {
                if (!a.A(Forme.Creux | Forme.Plat)) return Rate("fac.r.rienAEnduire");
                double couche;
                if (b.Durete < 0.1 && b.Densite > 1.5 && b.Lien == 0) couche = 0.55; // une terre (elle s'amollit à la longue)
                else if (b.Lien > 0.4 && b.Densite is > 0.9 and < 1.3) couche = at.Feu ? (b.Lien > 0.7 ? 0.95 : 0.85) : -1; // résine ou poix chauffées
                else return Rate("fac.r.neCollePas");
                if (couche < 0) return Rate("fac.r.resineFroide");
                return Ok(a with { Etanche = Math.Max(a.Etanche, couche), Masse = masse });
            }
            case Jointure.Coudre:
                if (!a.A(Forme.Plat) || a.Souplesse < 0.4) return Rate("fac.r.rienACoudre");
                if (!a.A(Forme.Perce)) return Rate("fac.r.pasPerce");
                if (!b.A(Forme.Long) || b.Souplesse < 0.8 || b.Lien < 0.3) return Rate("fac.r.pasUnFil");
                return Ok(a with
                {
                    Forme = (a.Forme | Forme.Creux) & ~Forme.Plat, Contenance = 4 * a.Masse, Etanche = 0.9 * Math.Max(a.Etanche, 0.3), Masse = masse,
                });
            case Jointure.Ligne:
                if (!a.A(Forme.Crochet) || a.Longueur > 0.4) return Rate("fac.r.pasUnCrochet");
                if (!b.A(Forme.Long) || b.Souplesse < 0.8 || b.Lien < 0.3) return Rate("fac.r.pasUnFil");
                return Ok(a with { Masse = masse, Longueur = b.Longueur, Lien = b.Lien });
            default:
                return Rate("fac.r.rien");
        }
    }

    /// <summary>Vitesse d'une chose lancée à la main : une vingtaine de m/s pour une chose légère, moins pour une lourde (≈150 J au plus).</summary>
    public static double Main(double masse) => Math.Min(20, Math.Sqrt(2 * 150 / Math.Max(0.01, masse)));

    // ------------------------------------------------------------------ Ce que vaut la chose, pour chaque besoin

    /// <summary>
    /// Ce qu'ajoute la chose à la chasse (même échelle que <see cref="Lois.Chasse"/> : une pointe de pierre emmanchée vaut
    /// environ 1). La portée compte (on approche moins la bête), et la pénétration : une pointe dure qui porte assez d'énergie.
    /// </summary>
    public static double Chasse(Traits t)
    {
        if (!t.A(Forme.Long) || t.Masse > 6) return 0;
        var v = t.Vitesse > 0 ? t.Vitesse : Main(t.Masse);
        var energie = 0.5 * t.Masse * v * v;
        var portee = v * v / (2 * Lois.Gravite);
        var pointe = t.A(Forme.Pointu) ? 1 : t.A(Forme.Tranchant) ? 0.6 : 0.3;
        var perce = pointe * Math.Min(1, 0.4 + t.Durete) * Math.Sqrt(Math.Min(1, energie / 30));
        return Math.Min(2.2, perce * (0.6 + 0.5 * Math.Log(1 + portee / 10)));
    }

    /// <summary>Force à la coupe (même échelle que <see cref="Lois.Coupe"/> : un éclat en main ≈ 0,12, une hache emmanchée ≈ 0,6).</summary>
    public static double Coupe(Traits t)
    {
        if (t.Fil <= 0) return 0;
        var durete = 0.5 + 0.5 * Math.Min(1, t.Durete / 0.6);
        return Math.Clamp(t.Fil * durete * (0.15 + 0.85 * Math.Min(1, t.Masse * Math.Max(0.05, t.Longueur) / 0.6)), 0, 1);
    }

    /// <summary>Litres d'eau qu'on emporte (0 si la paroi ne la garde pas).</summary>
    public static double Eau(Traits t) => t.A(Forme.Creux) && t.Etanche >= 0.6 && t.Contenance >= 0.3 ? Math.Min(20, t.Contenance) * t.Etanche : 0;

    /// <summary>Ce qu'ajoute la chose à la pêche (0 : rien) : un crochet dur et pointu au bout d'un fil.</summary>
    public static double Peche(Traits t) =>
        t.A(Forme.Crochet) && t.Lien > 0 && t.Longueur >= 1 ? (0.5 + 0.5 * Math.Min(1, t.Durete / 0.5)) * (t.A(Forme.Pointu) ? 1.3 : 1) : 0;

    /// <summary>Ce que vaut la chose pour ce besoin (0 : elle n'y sert pas).</summary>
    public static double Valeur(Fonction f, Traits t) => f switch
    {
        Fonction.Chasser => Chasse(t),
        Fonction.Couper => Coupe(t),
        Fonction.Contenir => Eau(t) >= 0.5 ? Math.Log(1 + Eau(t)) : 0,
        Fonction.Pecher => Peche(t),
        _ => 0,
    };

    /// <summary>La valeur d'une chose déjà faite, pour ce besoin (avec sa qualité et son usure).</summary>
    public static double Valeur(Fonction f, Objet o) =>
        o.Matiere == Matiere.Facture && LireObjet(o.Pieces) is { } x && Calculer(x.Facture) is { Reussie: true } c
            ? Valeur(f, c.Traits) * (0.6 + 0.4 * o.Qualite) * Math.Max(0.3, o.Usure)
            : 0;

    // ------------------------------------------------------------------ Écrire et lire, avec le savoir et l'objet

    /// <summary>« F|fonction|recette » : gardé avec le savoir et l'objet.</summary>
    public static string Ecrire(Fonction f, Facture x) => string.Create(CultureInfo.InvariantCulture, $"F|{(int)f}|{x.Ecrire()}");

    public static (Fonction Fonction, Facture Facture)? LireObjet(string? pieces)
    {
        if (pieces is null || pieces.Length < 5 || !pieces.StartsWith("F|", StringComparison.Ordinal)) return null;
        var barre = pieces.IndexOf('|', 2);
        if (barre < 0 || !byte.TryParse(pieces.AsSpan(2, barre - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var f)
            || !Enum.IsDefined((Fonction)f)) return null;
        return Facture.Lire(pieces[(barre + 1)..]) is { } x ? ((Fonction)f, x) : null;
    }

    /// <summary>Identité d'une trouvaille dans la chronologie : la recette, quel que soit l'usage.</summary>
    public static string Structure(string pieces) => LireObjet(pieces) is { } x ? "F|" + x.Facture.Ecrire() : pieces;

    // ------------------------------------------------------------------ Dire

    /// <summary>La chose décrite par la façon dont elle est faite (« une branche fendue, aiguisée »), avec le nom qu'un observateur d'aujourd'hui lui donnerait.</summary>
    public static Recit Decrire(Facture f)
    {
        var texte = Dire(f);
        return NomActuel(f) is { } nom ? new Recit("fac.nomme", ("objet", texte), ("nom", Recit.Seul($"fac.nom.{nom}"))) : texte;
    }

    private static Recit Dire(Facture f) => f switch
    {
        Brute b => Physique.Nom(b.M),
        Faconnee x => DireFacon(x),
        Jointe j => new($"fac.j.{j.J}", ArgumentsJointe(j)),
        _ => Recit.Seul("fac.objet"),
    };

    /// <summary>Une façon, avec la pièce qui la reçoit ; la même façon répétée se dit une fois, avec le nombre (« fendue trois fois »).</summary>
    private static Recit DireFacon(Faconnee x)
    {
        var n = 1;
        var dedans = x.X;
        while (dedans is Faconnee y && y.F == x.F)
        {
            n++;
            dedans = y.X;
        }
        var m = Physique.Nom(x.Principale);
        // Après une autre façon, une virgule : « une branche fendue 3 fois, aiguisée ».
        var v = dedans is Brute ? "" : ",";
        return n == 1 ? new Recit($"fac.f.{x.F}", ("x", Dire(dedans)), ("m", m), ("v", v))
            : new Recit($"fac.f.{x.F}.n", ("x", Dire(dedans)), ("m", m), ("n", n), ("v", v));
    }

    private static (string, object?)[] ArgumentsJointe(Jointe j) =>
    [
        ("a", Dire(j.A)), ("b", Dire(j.B)), ("ma", Physique.Nom(j.A.Principale)), ("mb", Physique.Nom(j.B.Principale)),
        ("lien", j.Lien == Matiere.Aucune ? "" : new Recit("fac.avec", ("lien", Physique.Nom(j.Lien)))), ("v", j.A is Brute ? "" : ","),
    ];

    /// <summary>Un nom d'aujourd'hui, déduit seulement des propriétés : il ne guide rien.</summary>
    public static string? NomActuel(Facture f)
    {
        if (Calculer(f) is not { Reussie: true } c) return null;
        var t = c.Traits;
        return f switch
        {
            Jointe { J: Jointure.Propulse } j when Calculer(j.A).Traits.Tension > 0 => "arc",
            Jointe { J: Jointure.Propulse } => "propulseur",
            Jointe { J: Jointure.Bouts } => "arcNu",
            Jointe { J: Jointure.Ligne } => "ligne",
            Jointe { J: Jointure.Coudre } => "outre",
            _ when Eau(t) > 0 && Tresse(f) => "panierEnduit",
            _ when t.A(Forme.Creux) && t.Etanche < 0.3 && t.Contenance > 1 => "panier",
            _ when Eau(t) > 0 => "recipient",
            Jointe { J: Jointure.Travers } => "hache",
            Jointe { J: Jointure.Bout } when t.A(Forme.Pointu) => "lance",
            _ when t.A(Forme.Crochet) && t.Longueur <= 0.4 => "hamecon",
            _ => null,
        };
    }

    private static bool Tresse(Facture f)
    {
        var facons = new HashSet<Facon>();
        f.Gestes(facons, []);
        return facons.Contains(Facon.Tresser);
    }

    /// <summary>« ce que l'on fait » en une phrase, pour la question d'une idée (« et si je fendais une branche ? »).</summary>
    public static Recit DireGeste(Facture f) => f switch
    {
        Faconnee x => new Recit($"fac.g.{x.F}", ("x", Dire(x.X)), ("m", Physique.Nom(x.Principale))),
        Jointe j => new($"fac.gj.{j.J}", ArgumentsJointe(j)),
        _ => Dire(f),
    };

    /// <summary>Ce qu'une chose a de remarquable (une pièce qui plie et revient, une paroi qui garde l'eau…), pour les remarques.</summary>
    public static IEnumerable<string> Proprietes(Traits t)
    {
        if (t.A(Forme.Long) && t.Souplesse is >= 0.4 and <= 0.75) yield return "ressort";
        if (t.Fil >= 0.5) yield return "fil";
        if (t.A(Forme.Creux) && t.Etanche >= 0.6) yield return "etanche";
        if (t.A(Forme.Long) && t.Souplesse >= 0.85) yield return "souple";
        if (t.Durete >= 0.6) yield return "dur";
    }

    /// <summary>Lit une remarque : « R|propriete ».</summary>
    public static string? LireRemarque(string? pieces) =>
        pieces is not null && pieces.StartsWith("R|", StringComparison.Ordinal) && pieces.Length > 2 ? pieces[2..] : null;

    /// <summary>Description d'une recette pour l'interface (« une branche fendue »), ou null si elle est illisible.</summary>
    public static Recit? DecrireTexte(string recette) => Facture.Lire(recette) is { } f ? Decrire(f) : null;

}
