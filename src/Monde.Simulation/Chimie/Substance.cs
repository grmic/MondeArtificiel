using System.Globalization;
using System.Text;
using Monde.Simulation.Humains;

namespace Monde.Simulation.Chimie;

/// <summary>Comment les grains d'une chose tiennent ensemble : c'est autant que sa composition ce qui décide de ce qu'elle peut faire.</summary>
public enum Structure : byte
{
    /// <summary>Grains libres : sable, poudre, cendre.</summary>
    Grains = 0,
    /// <summary>Pâte mouillée, qu'on peut modeler.</summary>
    Pate = 1,
    /// <summary>Pâte séchée : elle tient tant qu'on ne la mouille pas.</summary>
    Crue = 2,
    /// <summary>Terre passée au feu : les grains sont soudés.</summary>
    Cuite = 3,
    /// <summary>Roche ou minéral compact.</summary>
    Roche = 4,
    /// <summary>Tissu du vivant (bois, écorce, os, coquille), ou ce qu'il en reste après le feu.</summary>
    Fibreuse = 5,
    Metal = 6,
    Vitreuse = 7,
}

/// <summary>
/// Une matière telle qu'elle est faite : les phases qui la composent (en parts de masse), la façon dont elles tiennent
/// ensemble, sa porosité, et ce qu'elle a subi (frittage au feu, fissures, écrouissage sous les coups). Toutes ses propriétés
/// se calculent de là : rien n'y dit « pot », « chaux » ni « charbon ».
/// </summary>
/// <param name="Composants">Phases et parts de masse (leur somme vaut 1), triées par phase.</param>
/// <param name="Porosite">Part du volume occupée par des vides (0 à 0,95).</param>
/// <param name="Frittage">Soudure des grains par le feu, de 0 (aucune) à 1 (grès vitrifié).</param>
/// <param name="Fissures">De 0 (intacte) à 1 (en morceaux).</param>
/// <param name="Ecrouissage">Durcissement d'un métal martelé à froid, de 0 (recuit, doux) à 1 (dur et cassant).</param>
/// <param name="Forme">Forme de la pièce (creuse, plate, pointue…).</param>
/// <param name="Masse">Masse de la pièce, en kg.</param>
/// <param name="FaconneLe">Pour une pâte : quand elle a été mouillée ou modelée (elle sèche ensuite à l'air).</param>
public sealed record Substance((Phase Phase, double Part)[] Composants, Structure Structure, double Porosite, double Masse,
    Forme Forme = Forme.Aucune, double Frittage = 0, double Fissures = 0, double Ecrouissage = 0, long FaconneLe = 0)
{
    /// <summary>Une matière faite de ces phases, en proportions normalisées et triées (les traces de moins de 1/10 000 tombent).</summary>
    public static Substance De(Structure structure, double porosite, double masse, params (Phase Phase, double Part)[] parts)
    {
        var total = parts.Where(p => p.Part > 0).Sum(p => p.Part);
        var l = parts.Where(p => p.Part > 0).GroupBy(p => p.Phase).Select(g => (g.Key, g.Sum(x => x.Part) / total))
            .Where(p => p.Item2 >= 1e-4).OrderBy(p => p.Key).ToArray();
        var reste = l.Sum(p => p.Item2);
        for (var i = 0; i < l.Length; i++) l[i].Item2 /= reste;
        return new Substance(l, structure, Math.Clamp(porosite, 0, 0.95), masse);
    }

    public double Part(Phase p)
    {
        foreach (var (x, v) in Composants) if (x == p) return v;
        return 0;
    }

    public double PartClasse(Classe c)
    {
        var s = 0.0;
        foreach (var (x, v) in Composants) if (Phases.De(x).Classe == c) s += v;
        return s;
    }

    /// <summary>Part de la masse qui revient à cet élément (toutes phases confondues).</summary>
    public double PartElement(Element e)
    {
        var s = 0.0;
        foreach (var (x, v) in Composants) s += v * Phases.De(x).Part(e);
        return s;
    }

    /// <summary>La phase qui pèse le plus.</summary>
    public Phase Dominante => Composants.Length == 0 ? Phase.Eau : Composants.MaxBy(c => c.Part).Phase;

    /// <summary>Masse volumique des grains eux-mêmes (sans les vides), en g/cm³.</summary>
    public double DensiteSquelette
    {
        get
        {
            var inverse = 0.0;
            foreach (var (x, v) in Composants) inverse += v / Phases.De(x).Densite;
            return inverse <= 0 ? 1 : 1 / inverse;
        }
    }

    /// <summary>Masse volumique apparente, vides compris (moins de 1 : elle flotte, tant que l'eau n'y entre pas).</summary>
    public double Densite => DensiteSquelette * (1 - Porosite);

    /// <summary>Volume de la pièce, en litres.</summary>
    public double Volume => Masse / Math.Max(0.01, Densite);

    /// <summary>Part de la masse faite d'argile (crue ou cuite).</summary>
    public double Argile => PartClasse(Classe.Argile) + PartClasse(Classe.ArgileCuite);

    /// <summary>Part de l'argile qui a perdu son eau de constitution au feu (0 : crue, 1 : toute cuite).</summary>
    public double ArgileCuite => Argile <= 0 ? 0 : PartClasse(Classe.ArgileCuite) / Argile;

    /// <summary>
    /// Dureté (Mohs divisé par 10). Une pâte ou de la poudre s'écrase entre les doigts quelle que soit sa composition ; une
    /// terre séchée se raye à l'ongle ; une terre cuite dure d'autant plus qu'elle est frittée (une faïence vers 3, un grès vers 6) ;
    /// une roche ou un métal a la dureté de ses minéraux, en moyenne pondérée par leur volume. Les fissures l'affaiblissent.
    /// </summary>
    public double Durete
    {
        get
        {
            var moyenne = MohsMoyen / 10;
            var d = Structure switch
            {
                Structure.Grains or Structure.Pate => 0.02,
                Structure.Crue => 0.12,
                Structure.Cuite => Math.Min(moyenne, 0.25 + 0.4 * Frittage),
                Structure.Metal => Math.Min(0.5, moyenne + 0.12 * Ecrouissage),
                // Le charbon de bois garde la forme du bois mais s'écrase sous les doigts (Mohs 1 à 2).
                Structure.Fibreuse => moyenne * (1 - 0.6 * Math.Max(0, Porosite - 0.6) / 0.35),
                _ => moyenne * (1 - 0.5 * Porosite),
            };
            return d * (1 - 0.5 * Fissures);
        }
    }

    private double MohsMoyen
    {
        get
        {
            double m = 0, v = 0;
            foreach (var (x, p) in Composants)
            {
                if (x == Phase.Eau) continue;
                var i = Phases.De(x);
                m += p / i.Densite * i.Mohs;
                v += p / i.Densite;
            }
            return v <= 0 ? 0 : m / v;
        }
    }

    /// <summary>
    /// Couleur vue : celle de la phase qui colore le plus, une phase forte (hématite, malachite, charbon) suffisant en petite
    /// quantité ; un métal se voit à son éclat dès qu'il fait l'essentiel de la pièce.
    /// </summary>
    public Teinte Teinte
    {
        get
        {
            if (Structure == Structure.Metal) return Phases.De(Dominante).Teinte;
            var meilleure = Teinte.Aucune;
            var force = 0.0;
            foreach (var (x, p) in Composants)
            {
                var i = Phases.De(x);
                if (i.Teinte == Teinte.Aucune) continue;
                var f = p / i.Pouvoir;
                if (f > force) (force, meilleure) = (f, i.Teinte);
            }
            return meilleure;
        }
    }

    /// <summary>Elle sèche, s'émiette ou se dissout dans l'eau (pâte, terre crue, poudre, sel, chaux vive dans un pot).</summary>
    public bool SeDelite =>
        Structure is Structure.Grains or Structure.Pate
        || Structure == Structure.Crue && ArgileCuite < 0.5
        || PartSoluble > 0.3
        // La chaux vive gonfle et éclate en s'éteignant : un pot fait d'une terre pleine de coquillages cuite trop chaud s'effrite
        // quand on le mouille (Rye, 1976).
        || Part(Phase.Chaux) > 0.04 && Structure is Structure.Cuite or Structure.Roche or Structure.Grains;

    private double PartSoluble
    {
        get
        {
            var s = 0.0;
            foreach (var (x, p) in Composants) if (Phases.De(x).Soluble) s += p;
            return s;
        }
    }

    /// <summary>Une pièce creuse qui garde l'eau : ni pâte ni terre crue, assez peu fendue, et dont l'eau ne traverse pas les vides en grand.</summary>
    public bool TientLEau =>
        (Forme & Forme.Creux) != 0 && !SeDelite && Fissures < 0.3
        && Structure is Structure.Cuite or Structure.Roche or Structure.Metal or Structure.Vitreuse or Structure.Fibreuse
        && Porosite < 0.6;

    /// <summary>Elle sonne quand on la frappe (terre bien cuite, roche, métal, verre), si elle n'est pas fendue.</summary>
    public bool Sonore => Fissures < 0.3 && (Structure is Structure.Roche or Structure.Metal or Structure.Vitreuse
        || Structure == Structure.Cuite && Frittage >= 0.1);

    /// <summary>Elle se plie et s'étire sous les coups au lieu de casser.</summary>
    public bool Malleable => Structure == Structure.Metal && PartClasse(Classe.Metal) >= 0.8;

    /// <summary>Elle luit : métal, verre, silex chauffé.</summary>
    public bool Brillante => Structure is Structure.Metal or Structure.Vitreuse || Part(Phase.CalcedoineChauffee) > 0.5;

    /// <summary>
    /// Elle colle quand on la chauffe et durcit en refroidissant (résine, brai, bitume). Le brai suinte de l'écorce qui le porte :
    /// un dixième de la masse suffit pour qu'on le voie perler et qu'on le recueille (Schmidt et al., 2019).
    /// </summary>
    public bool Collante => Part(Phase.Brai) + Part(Phase.Bitume) >= 0.08 || Part(Phase.Brai) + Part(Phase.Terpenes) + Part(Phase.Bitume) >= 0.4;

    /// <summary>Elle se modèle sous les doigts : une pâte d'argile assez mouillée, pas trop.</summary>
    public bool Plastique => Structure == Structure.Pate && PartClasse(Classe.Argile) >= 0.2 && Part(Phase.Eau) is >= 0.12 and <= 0.4;

    /// <summary>Elle pèse lourd dans la main pour sa taille.</summary>
    public bool Lourde => Densite >= 4;

    /// <summary>Elle chauffe et fume quand on la mouille (chaux vive).</summary>
    public bool ChauffeDansLEau => Part(Phase.Chaux) >= 0.2;

    /// <summary>
    /// Pouvoir calorifique inférieur, en MJ/kg (formule de Dulong sur la part combustible : 33,9 C + 141,9 (H − O/8) + 9,3 S,
    /// moins la chaleur qu'il faut pour vaporiser l'eau qu'elle contient et celle que donne son hydrogène). Seuls comptent les
    /// éléments des phases qui brûlent : l'oxygène d'un carbonate ne retire rien.
    /// </summary>
    public double PouvoirCalorifique
    {
        get
        {
            double c = 0, h = 0, o = 0, s = 0;
            foreach (var (x, p) in Composants)
            {
                var i = Phases.De(x);
                if (!i.Organique) continue;
                c += p * i.Part(Element.C);
                h += p * i.Part(Element.H);
                o += p * i.Part(Element.O);
                s += p * i.Part(Element.S);
            }
            var pcs = 33.9 * c + 141.9 * Math.Max(0, h - o / 8) + 9.3 * s;
            return Math.Max(0, pcs - 2.442 * (9 * h + Part(Phase.Eau)));
        }
    }

    /// <summary>
    /// Air qu'il faut pour brûler un kilo de cette matière, en kg (combustion complète : 2,664 kg d'oxygène par kg de carbone,
    /// 7,937 par kg d'hydrogène, 0,998 par kg de soufre, moins l'oxygène qu'elle porte ; l'air en contient 23,2 %).
    /// </summary>
    public double AirStoechiometrique
    {
        get
        {
            double c = 0, h = 0, o = 0, s = 0;
            foreach (var (x, p) in Composants)
            {
                var i = Phases.De(x);
                if (!i.Organique) continue;
                c += p * i.Part(Element.C);
                h += p * i.Part(Element.H);
                o += p * i.Part(Element.O);
                s += p * i.Part(Element.S);
            }
            return Math.Max(0, (2.664 * c + 7.937 * h + 0.998 * s - o) / 0.232);
        }
    }

    /// <summary>Elle prend feu : assez de pouvoir calorifique pour entretenir sa flamme.</summary>
    public bool Combustible => PouvoirCalorifique >= 8;

    /// <summary>Elle brûle en braise, sans flamme ni fumée (charbon de bois).</summary>
    public bool BrulesSansFumee => Combustible && Part(Phase.Carbone) >= 0.6;

    /// <summary>Capacité thermique de la pièce entière, en kJ/K.</summary>
    public double Capacite
    {
        get
        {
            var cp = 0.0;
            foreach (var (x, p) in Composants) cp += p * Phases.De(x).ChaleurMassique;
            return cp * Masse;
        }
    }

    // ------------------------------------------------------------------ Écriture

    /// <summary>
    /// Forme écrite, gardée avec l'objet : « S|phase:part‱,…|structure|porosité %|masse g|forme|frittage %|fissures %|écrouissage %|façonnée le ».
    /// </summary>
    public string Signature
    {
        get
        {
            var b = new StringBuilder("S|");
            for (var i = 0; i < Composants.Length; i++)
            {
                if (i > 0) b.Append(',');
                b.Append(CultureInfo.InvariantCulture, $"{(int)Composants[i].Phase}:{Math.Max(1, (int)Math.Round(Composants[i].Part * 10000))}");
            }
            b.Append(CultureInfo.InvariantCulture,
                $"|{(int)Structure}|{Pc(Porosite)}|{Math.Max(1, (long)Math.Round(Masse * 1000))}|{(int)Forme}|{Pc(Frittage)}|{Pc(Fissures)}|{Pc(Ecrouissage)}|{FaconneLe}");
            return b.ToString();

            static int Pc(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 100);
        }
    }

    /// <summary>La matière telle qu'elle est écrite (arrondie au dix-millième et au pour cent) : relire sa signature redonne la même.</summary>
    public Substance Arrondie => Lire(Signature)!;

    /// <summary>Relit une signature ; null si elle n'en est pas une.</summary>
    public static Substance? Lire(string? s)
    {
        if (s is null || !s.StartsWith("S|", StringComparison.Ordinal) || s.Length > 400) return null;
        var p = s.Split('|');
        if (p.Length != 10) return null;
        var composants = new List<(Phase, double)>();
        foreach (var c in p[1].Split(','))
        {
            var x = c.Split(':');
            if (x.Length != 2 || !int.TryParse(x[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ph) || ph >= Phases.Nombre
                || !int.TryParse(x[1], NumberStyles.None, CultureInfo.InvariantCulture, out var part)) return null;
            composants.Add(((Phase)ph, part / 10000.0));
        }
        if (!int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var st) || !Enum.IsDefined((Structure)st)) return null;
        if (!int.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out var poro)) return null;
        if (!long.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out var g)) return null;
        if (!int.TryParse(p[5], NumberStyles.None, CultureInfo.InvariantCulture, out var forme)) return null;
        if (!int.TryParse(p[6], NumberStyles.None, CultureInfo.InvariantCulture, out var fr)) return null;
        if (!int.TryParse(p[7], NumberStyles.None, CultureInfo.InvariantCulture, out var fi)) return null;
        if (!int.TryParse(p[8], NumberStyles.None, CultureInfo.InvariantCulture, out var ec)) return null;
        if (!long.TryParse(p[9], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var le)) return null;
        var total = composants.Sum(c => c.Item2);
        if (total <= 0) return null;
        return new Substance(composants.Select(c => (c.Item1, c.Item2 / total)).ToArray(), (Structure)st, poro / 100.0, g / 1000.0, (Forme)forme,
            fr / 100.0, fi / 100.0, ec / 100.0, le);
    }

    public override string ToString() => Signature;
}
