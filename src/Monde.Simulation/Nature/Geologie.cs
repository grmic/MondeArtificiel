using Monde.Simulation.Humains;
using Monde.Simulation.Monde;

namespace Monde.Simulation.Nature;

/// <summary>Cadre géologique du continent : il décide où la terre tremble et où sont les volcans.</summary>
public enum ContexteTectonique : byte
{
    /// <summary>Vieux socle continental (craton) : séismes rares et modérés, parfois un volcan de point chaud.</summary>
    Stable = 0,

    /// <summary>Le continent se fend le long d'un rift (comme l'Afrique de l'Est) : séismes moyens, quelques volcans.</summary>
    Rift = 1,

    /// <summary>Une plaque océanique plonge sous une côte (comme les Andes ou le Japon) : grands séismes, tsunamis, arc volcanique.</summary>
    MargeActive = 2,
}

/// <summary>Un segment de faille, en cases. Une faille sous-marine peut lever un tsunami.</summary>
public sealed record Faille(int X1, int Y1, int X2, int Y2, bool SousMarine);

/// <summary>Un volcan actif et l'intervalle moyen entre deux éruptions.</summary>
public sealed record Volcan(int Numero, int X, int Y, double ReposMoyenAns, string Nom);

/// <summary>
/// Géologie du continent, tirée de la graine une fois pour toutes (elle ne change pas la carte) :
/// contexte tectonique, failles, volcans, activité sismique.
/// <para>
/// Ordres de grandeur pour une région de la taille du continent (environ 200 km de côté) : au moins un séisme
/// de magnitude 5 tous les 20 à 50 ans sur un vieux socle, plusieurs par décennie le long d'un rift, un ou deux
/// par an sur une marge active (catalogues USGS ; loi de Gutenberg-Richter, b ≈ 1). Un volcan actif entre
/// en éruption tous les quelques décennies à quelques siècles (Smithsonian, Global Volcanism Program).
/// </para>
/// </summary>
public sealed class Geologie
{
    public required ContexteTectonique Contexte { get; init; }
    public required IReadOnlyList<Faille> Failles { get; init; }
    public required IReadOnlyList<Volcan> Volcans { get; init; }

    /// <summary>Nombre moyen de séismes de magnitude 5 ou plus par an, sur tout le continent et ses côtes.</summary>
    public required double TauxM5ParAn { get; init; }

    /// <summary>Plus forte magnitude que les failles peuvent produire.</summary>
    public required double MagnitudeMax { get; init; }

    /// <summary>Côte où plonge la plaque (« l'ouest »…), pour une marge active ; null sinon.</summary>
    public string? CoteActive { get; init; }

    public string Description => Contexte switch
    {
        ContexteTectonique.Stable => "Vieux socle stable : la terre tremble rarement.",
        ContexteTectonique.Rift => "Un rift fend le continent : la terre tremble le long de la fracture.",
        _ => $"Une plaque océanique plonge sous la côte de {CoteActive} : grands séismes et tsunamis possibles.",
    } + (Volcans.Count switch { 0 => " Aucun volcan actif.", 1 => " Un volcan actif.", var n => $" {n} volcans actifs." });

    public static Geologie Generer(Carte carte)
    {
        // Générateur propre à la géologie : la carte et la simulation ne changent pas.
        var alea = new Alea(carte.Graine ^ 0x47454F4C4F474945UL);
        var tirage = alea.Reel();
        var contexte = tirage < 0.35 ? ContexteTectonique.Stable : tirage < 0.70 ? ContexteTectonique.Rift : ContexteTectonique.MargeActive;
        return contexte switch
        {
            ContexteTectonique.MargeActive => MargeActive(carte, alea),
            ContexteTectonique.Rift => Rift(carte, alea),
            _ => Stable(carte, alea),
        };
    }

    private static double LogNormale(Alea alea, double mediane, double sigma) => mediane * Math.Exp(sigma * Biologie.Normale(alea));

    private static Geologie Stable(Carte carte, Alea alea)
    {
        var volcans = new List<Volcan>();
        // Un point chaud sous un vieux socle est rare (Hoggar, Tibesti).
        if (alea.Reel() < 0.25 && Sommets(carte, _ => true, 1, 0, alea) is [var (x, y)])
        {
            volcans.Add(new Volcan(1, x, y, LogNormale(alea, 150, 0.6), ""));
        }
        return new Geologie
        {
            Contexte = ContexteTectonique.Stable, Failles = [], Volcans = Nommer(carte, volcans),
            TauxM5ParAn = LogNormale(alea, 0.03, 0.4), MagnitudeMax = 6.6 + 0.5 * alea.Reel(),
        };
    }

    private static Geologie Rift(Carte carte, Alea alea)
    {
        // Une fracture qui traverse le continent en passant près du centre.
        var angle = alea.Reel() * Math.PI;
        double cx = carte.Largeur / 2.0 + (alea.Reel() - 0.5) * carte.Largeur * 0.3;
        double cy = carte.Hauteur / 2.0 + (alea.Reel() - 0.5) * carte.Hauteur * 0.3;
        var demi = carte.Largeur * 0.45;
        var points = new List<(int X, int Y)>();
        for (var k = -4; k <= 4; k++)
        {
            var t = demi * k / 4;
            // La fracture n'est pas droite : elle ondule de quelques cases.
            var decalage = (alea.Reel() - 0.5) * 12;
            var x = cx + t * Math.Cos(angle) - decalage * Math.Sin(angle);
            var y = cy + t * Math.Sin(angle) + decalage * Math.Cos(angle);
            points.Add((Math.Clamp((int)x, 0, carte.Largeur - 1), Math.Clamp((int)y, 0, carte.Hauteur - 1)));
        }
        var failles = Segments(carte, points);
        var nombre = alea.Entier(3); // 0 à 2 volcans le long du rift
        var volcans = Sommets(carte, i => DistanceAuxFailles(failles, i % carte.Largeur, i / carte.Largeur) <= 12, nombre, 25, alea)
            .Select((p, n) => new Volcan(n + 1, p.X, p.Y, LogNormale(alea, 80, 0.7), "")).ToList();
        return new Geologie
        {
            Contexte = ContexteTectonique.Rift, Failles = failles, Volcans = Nommer(carte, volcans),
            TauxM5ParAn = LogNormale(alea, 0.2, 0.4), MagnitudeMax = 7.0 + 0.6 * alea.Reel(),
        };
    }

    private static Geologie MargeActive(Carte carte, Alea alea)
    {
        // La plaque plonge sous la côte la plus montagneuse : les cordillères bordent les zones de subduction.
        string[] cotes = ["l'ouest", "l'est", "le nord", "le sud"];
        var meilleure = 0;
        var meilleurRelief = double.MinValue;
        for (var c = 0; c < 4; c++)
        {
            double somme = 0;
            var n = 0;
            for (var i = 0; i < carte.Altitudes.Length; i++)
            {
                if (carte.Altitudes[i] <= Carte.NiveauMer || !DansLaBande(carte, i, c)) continue;
                somme += carte.Altitudes[i];
                n++;
            }
            var relief = n == 0 ? double.MinValue : somme / n;
            if (relief > meilleurRelief) (meilleurRelief, meilleure) = (relief, c);
        }

        // La fosse court au large, parallèle à la côte, à 8 à 14 cases (6 à 11 km) du rivage.
        var points = new List<(int X, int Y)>();
        var pasLong = carte.Largeur / 12;
        for (var k = 1; k < 12; k++)
        {
            var le = k * pasLong;
            var cote = PremiereTerre(carte, meilleure, le);
            if (cote < 0) continue;
            var large = 8 + alea.Entier(7);
            var profondeur = meilleure is 0 or 2 ? Math.Max(1, cote - large) : Math.Min(carte.Largeur - 2, cote + large);
            points.Add(meilleure < 2 ? (profondeur, le) : (le, profondeur));
        }
        var failles = Segments(carte, points);

        // Arc volcanique : les plus hauts sommets entre 15 et 90 cases de la côte active.
        var nombre = 2 + alea.Entier(3);
        var volcans = Sommets(carte, i =>
        {
            var d = DistanceAuxFailles(failles, i % carte.Largeur, i / carte.Largeur);
            return d is >= 20 and <= 100;
        }, nombre, 20, alea).Select((p, n) => new Volcan(n + 1, p.X, p.Y, LogNormale(alea, 60, 0.7), "")).ToList();

        return new Geologie
        {
            Contexte = ContexteTectonique.MargeActive, Failles = failles, Volcans = Nommer(carte, volcans),
            TauxM5ParAn = LogNormale(alea, 1.0, 0.35), MagnitudeMax = 8.4 + 0.8 * alea.Reel(), CoteActive = cotes[meilleure],
        };
    }

    /// <summary>La case est-elle dans le tiers du continent tourné vers la côte <paramref name="cote"/> (0 ouest, 1 est, 2 nord, 3 sud) ?</summary>
    private static bool DansLaBande(Carte carte, int i, int cote)
    {
        int x = i % carte.Largeur, y = i / carte.Largeur;
        return cote switch
        {
            0 => x < carte.Largeur / 3,
            1 => x >= carte.Largeur * 2 / 3,
            2 => y < carte.Hauteur / 3,
            _ => y >= carte.Hauteur * 2 / 3,
        };
    }

    /// <summary>Première terre rencontrée depuis le bord <paramref name="cote"/>, sur la ligne (ou colonne) <paramref name="le"/> ; −1 si aucune.</summary>
    private static int PremiereTerre(Carte carte, int cote, int le)
    {
        var longueur = cote < 2 ? carte.Largeur : carte.Hauteur;
        for (var k = 0; k < longueur; k++)
        {
            var p = cote is 0 or 2 ? k : longueur - 1 - k;
            var (x, y) = cote < 2 ? (p, le) : (le, p);
            if (Climat.EstPraticable(carte.BiomeEn(x, y))) return p;
        }
        return -1;
    }

    private static List<Faille> Segments(Carte carte, List<(int X, int Y)> points)
    {
        var failles = new List<Faille>();
        for (var k = 1; k < points.Count; k++)
        {
            var (a, b) = (points[k - 1], points[k]);
            var milieu = carte.BiomeEn((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            failles.Add(new Faille(a.X, a.Y, b.X, b.Y, Biomes.EstEauSalee(milieu)));
        }
        return failles;
    }

    /// <summary>Distance (en cases) d'un point au plus proche segment de faille ; l'infini s'il n'y a pas de faille.</summary>
    public static double DistanceAuxFailles(IReadOnlyList<Faille> failles, double x, double y)
    {
        var d = double.PositiveInfinity;
        foreach (var f in failles)
        {
            double vx = f.X2 - f.X1, vy = f.Y2 - f.Y1;
            var l2 = vx * vx + vy * vy;
            var t = l2 == 0 ? 0 : Math.Clamp(((x - f.X1) * vx + (y - f.Y1) * vy) / l2, 0, 1);
            double px = f.X1 + t * vx - x, py = f.Y1 + t * vy - y;
            d = Math.Min(d, Math.Sqrt(px * px + py * py));
        }
        return d;
    }

    /// <summary>Les plus hauts sommets qui conviennent, espacés d'au moins <paramref name="ecart"/> cases.</summary>
    private static List<(int X, int Y)> Sommets(Carte carte, Func<int, bool> convient, int nombre, int ecart, Alea alea)
    {
        var candidats = new List<int>();
        for (var i = 0; i < carte.Biomes.Length; i++)
        {
            if ((Biome)carte.Biomes[i] is Biome.Montagne or Biome.SommetEnneige && convient(i)) candidats.Add(i);
        }
        // Les plus hauts d'abord ; à altitude égale, un ordre tiré au hasard mais reproductible.
        var ordre = candidats.Select(i => (Indice: i, Cle: carte.Altitudes[i] * 1000 + alea.Entier(1000))).OrderByDescending(c => c.Cle);
        var choisis = new List<(int X, int Y)>();
        foreach (var (i, _) in ordre)
        {
            if (choisis.Count >= nombre) break;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            if (choisis.All(c => Math.Max(Math.Abs(c.X - x), Math.Abs(c.Y - y)) >= ecart)) choisis.Add((x, y));
        }
        return choisis;
    }

    /// <summary>Noms d'observateur : « le volcan du nord-ouest », numérotés s'ils sont plusieurs dans la même direction.</summary>
    private static List<Volcan> Nommer(Carte carte, List<Volcan> volcans)
    {
        var bases = volcans.Select(v => Lieux.Direction(carte, v.X, v.Y) switch
        {
            "au centre" => "le volcan du centre",
            var d when d.StartsWith("à l'", StringComparison.Ordinal) => "le volcan de l'" + d[4..],
            var d => "le volcan du " + d[3..],
        }).ToList();
        return volcans.Select((v, k) =>
        {
            var memes = bases.Count(b => b == bases[k]);
            var rang = bases.Take(k).Count(b => b == bases[k]) + 1;
            return v with { Nom = memes > 1 ? $"{bases[k]} ({rang})" : bases[k] };
        }).ToList();
    }
}
