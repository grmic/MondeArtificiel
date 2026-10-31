using Monde.Simulation.Monde;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Une espèce de plante, avec ses vertus et ses dangers. Les humains n'en savent rien au départ : ils ne les apprennent
/// qu'en goûtant, en appliquant, en observant les autres.
/// </summary>
/// <param name="Numero">Numéro (1 à n) dans la flore du monde.</param>
/// <param name="Nom">Description pour l'observateur (les humains n'ont pas encore de mots).</param>
/// <param name="Toxicite">0 : inoffensive ; 1 : mortelle à forte dose.</param>
/// <param name="Astringente">Tanins : resserrent l'intestin, abrègent les diarrhées (0 à 1).</param>
/// <param name="Febrifuge">Fait tomber la fièvre et calme la douleur, sans guérir (0 à 1).</param>
/// <param name="Antipaludique">Abrège vraiment le paludisme (alcaloïdes comme ceux du quinquina ou de l'armoise annuelle) (0 à 1).</param>
/// <param name="Antiseptique">Appliquée, elle empêche une plaie ou la peau de s'infecter davantage (0 à 1).</param>
/// <param name="Vermifuge">Chasse les vers (0 à 1).</param>
/// <param name="Psychotrope">Fait voir et entendre des choses étranges (0 à 1).</param>
public sealed record Espece(int Numero, string Nom, Biome[] Biomes, double Toxicite, double Astringente, double Febrifuge,
    double Antipaludique, double Antiseptique, double Vermifuge, double Psychotrope, int Forme = -1, int Trait = -1)
{
    /// <summary>
    /// Le nom à écrire dans la langue de l'observateur (« la fougère aux baies rouges », « the fern with red berries ») :
    /// un récit fait de sa forme et de son trait, ou le nom français d'une espèce faite sans eux.
    /// </summary>
    public object Dit => Forme < 0 || Trait < 0 ? Nom : _dit ??= new Recit("plante", ("forme", Recit.Seul($"plante.forme.{Forme}")), ("trait", Recit.Seul($"plante.trait.{Trait}")));

    private Recit? _dit;

    /// <summary>Ce que l'espèce donne à manger, et comment elle se reproduit (null : flore d'avant la 0.33, ou rien à manger).</summary>
    public Aliment? Aliment { get; init; }

    /// <summary>Effet réel de la plante, mangée (ou appliquée), sur un mal donné : 0 si elle n'y fait rien.</summary>
    public double Efficacite(Maladie m, Geste geste) => (m, geste) switch
    {
        (Maladie.Diarrhee or Maladie.Dysenterie, Geste.Manger) => Astringente,
        (Maladie.VersIntestinaux, Geste.Manger) => Vermifuge,
        (Maladie.Paludisme, Geste.Manger) => Antipaludique,
        (Maladie.PlaieInfectee or Maladie.InfectionDeLaPeau, Geste.Appliquer) => Antiseptique,
        _ => 0,
    };
}

/// <summary>
/// La flore du monde : quelques espèces par terrain, tirées de la graine. Une petite part est toxique, beaucoup
/// contiennent des tanins, quelques-unes soignent vraiment un mal précis. Les proportions sont des réglages de départ :
/// en ethnobotanique, une part notable des plantes d'une région sert de remède dans la culture locale, mais
/// l'efficacité réelle de beaucoup d'entre elles n'est pas démontrée.
/// </summary>
public sealed class Flore
{
    private static readonly string[] Formes = ["l'arbuste", "la liane", "l'herbe", "la fougère", "l'arbre", "le buisson", "la racine", "la plante"];
    private static readonly string[] Traits =
    [
        "aux baies rouges", "aux baies noires", "aux feuilles velues", "à l'écorce amère", "aux fleurs blanches", "aux fleurs jaunes",
        "à la sève laiteuse", "aux feuilles larges", "aux épines", "à l'odeur forte", "aux graines dures", "aux feuilles argentées",
        "à la tige rouge", "aux fleurs bleues", "aux fruits velus", "à l'écorce rouge", "aux feuilles en étoile", "à la racine jaune",
    ];

    public IReadOnlyList<Espece> Especes { get; }

    private readonly Dictionary<Biome, Espece[]> _parBiome;

    /// <param name="nouveauxTerrains">La carte porte des terrains de la génération 1 (feuillus, mangrove, lande) : leurs
    /// espèces sont tirées après toutes les autres, qui restent donc les mêmes que sur une carte plus ancienne.</param>
    public Flore(ulong graine, bool nouveauxTerrains = false)
    {
        var alea = new Alea(graine ^ 0x464C4F5245UL);
        var especes = new List<Espece>();
        var noms = new HashSet<string>();
        var terres = new[] { Biome.Marais, Biome.PlaineDeFougeres, Biome.ForetDeConiferes, Biome.ForetTropicale, Biome.Desert, Biome.Montagne, Biome.Plage, Biome.Riviere };
        if (nouveauxTerrains) terres = [.. terres, Biome.ForetDeFeuillus, Biome.Mangrove, Biome.Lande];
        foreach (var b in terres)
        {
            var n = b is Biome.ForetTropicale ? 9 : b is Biome.ForetDeFeuillus ? 8 : b is Biome.Plage or Biome.Desert or Biome.Riviere or Biome.Mangrove ? 4 : b is Biome.Lande ? 5 : 6;
            for (var k = 0; k < n; k++)
            {
                string nom;
                int forme, trait;
                do
                {
                    forme = alea.Entier(Formes.Length);
                    trait = alea.Entier(Traits.Length);
                    nom = $"{Formes[forme]} {Traits[trait]}";
                }
                while (!noms.Add(nom));
                double Vertu(double p) => alea.Reel() < p ? 0.3 + 0.6 * alea.Reel() : 0;
                var humide = b is Biome.ForetTropicale or Biome.Marais or Biome.Mangrove;
                especes.Add(new Espece(especes.Count + 1, nom, [b],
                    Toxicite: Vertu(0.12), Astringente: Vertu(0.25), Febrifuge: Vertu(0.1), Antipaludique: humide ? Vertu(0.08) : 0,
                    Antiseptique: Vertu(0.15), Vermifuge: Vertu(0.1), Psychotrope: Vertu(0.05), Forme: forme, Trait: trait));
            }
        }
        // Ce que chaque espèce donne à manger, tiré d'un hasard à part : les vertus et les noms ci-dessus ne changent pas.
        var nourrir = new Alea(graine ^ 0x4E4F555252495245UL);
        for (var k = 0; k < especes.Count; k++) especes[k] = especes[k] with { Aliment = Aliment.Tirer(especes[k], nourrir) };
        Especes = especes;
        _parBiome = especes.GroupBy(e => e.Biomes[0]).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public Espece Get(int numero) => Especes[numero - 1];

    /// <summary>Espèces de ce terrain qui donnent à manger.</summary>
    public IReadOnlyList<Espece> NourricieresSur(Biome b)
    {
        if (_nourricieres.TryGetValue(b, out var l)) return l;
        lock (_nourricieres) return _nourricieres[b] = Sur(b).Where(e => e.Aliment is { Sorte: not SorteAliment.Aucune }).ToArray();
    }

    private readonly Dictionary<Biome, Espece[]> _nourricieres = [];

    /// <summary>Espèces qui poussent sur ce terrain.</summary>
    public IReadOnlyList<Espece> Sur(Biome b) => _parBiome.TryGetValue(b, out var l) ? l : [];
}

/// <summary>Ce qu'une plante donne à manger.</summary>
public enum SorteAliment : byte
{
    Aucune = 0,
    /// <summary>Graines d'une herbe (comme les céréales sauvages) : petites, riches, qui tombent d'elles-mêmes à maturité.</summary>
    Graines = 1,
    /// <summary>Graines dans des gousses (comme les légumineuses) : la gousse s'ouvre et les projette.</summary>
    Gousses = 2,
    /// <summary>Racines et tubercules : lourds, pauvres en eau, souvent amers crus ; un morceau remis en terre repousse.</summary>
    Tubercules = 3,
    /// <summary>Fruits charnus et baies.</summary>
    Fruits = 4,
    /// <summary>Noix et amandes : très riches, mais l'arbre met des années à porter.</summary>
    Noix = 5,
}

/// <summary>
/// Ce qu'une espèce donne à manger, et ses traits de plante sauvage : ce sont eux que la culture changera, sans que personne le
/// veuille, par la façon de semer et de récolter (Fuller, 2007 ; Zohary et Hopf, 2012).
/// </summary>
/// <param name="Egrenage">Part de ce qui est mûr qui tombe chaque semaine chez la plante sauvage (épi qui se brise, gousse qui
/// éclate) ; 0 pour ce qui ne se disperse pas ainsi (racines, fruits, noix).</param>
/// <param name="Tenaces">Part des plantes sauvages, mutantes, dont les graines restent sur la tige (de l'ordre d'une sur mille).</param>
/// <param name="Dormance">Part des graines sauvages qui ne germent qu'un an plus tard.</param>
/// <param name="Vivace">Elle vit plusieurs années (racines, arbres) ; sinon elle meurt après avoir donné ses graines.</param>
/// <param name="Bouture">Un morceau (racine, tige) remis en terre redonne la même plante : la sélection va bien plus vite.</param>
/// <param name="Rendement">Récolte sauvage, en kilos à l'hectare et par an, sur un sol moyen.</param>
/// <param name="Amertume">Amertume ou poison de la plante crue (0 : rien) : on la mange cuite, trempée, ou pas du tout.</param>
/// <param name="Maturite">Jours entre la levée et la récolte.</param>
/// <param name="AnsAvantDeDonner">Années avant la première récolte (arbres, arbustes) ; 0 pour une plante de l'année.</param>
public sealed record Aliment(SorteAliment Sorte, double Egrenage, double Tenaces, double Dormance, bool Vivace, bool Bouture, double Rendement,
    double Amertume, int Maturite, int AnsAvantDeDonner)
{
    /// <summary>Faim apaisée par un kilo (un kilo de viande fraîche en apaise 10) : les graines sèches sont trois fois plus riches.</summary>
    public double FaimParKg => Sorte switch
    {
        SorteAliment.Graines or SorteAliment.Gousses => 27,
        SorteAliment.Tubercules => 8,
        SorteAliment.Fruits => 5,
        SorteAliment.Noix => 50,
        _ => 0,
    };

    /// <summary>Semée d'une graine (pas de bouture) et récoltée l'année même : la seule qui s'égrène.</summary>
    public bool SeDisperse => Egrenage > 0;

    private static readonly SorteAliment[] SelonLaForme =
    [
        // l'arbuste, la liane, l'herbe, la fougère, l'arbre, le buisson, la racine, la plante
        SorteAliment.Fruits, SorteAliment.Gousses, SorteAliment.Graines, SorteAliment.Tubercules, SorteAliment.Noix, SorteAliment.Fruits,
        SorteAliment.Tubercules, SorteAliment.Gousses,
    ];

    /// <summary>
    /// Tire ce que l'espèce donne à manger d'après sa forme. Un peu plus de la moitié des espèces nourrissent ; la plante très toxique
    /// ne nourrit qu'au prix d'une longue préparation (amertume élevée). Les fourchettes sont celles des plantes sauvages
    /// apparentées : blés et orges sauvages (300 à 800 kg/ha), lentilles et pois, ignames et taros (1 à 5 t/ha), fruits, noix.
    /// </summary>
    internal static Aliment? Tirer(Espece e, Alea alea)
    {
        var u = alea.Reel();
        var sorte = e.Forme >= 0 ? SelonLaForme[e.Forme] : SorteAliment.Aucune;
        // La moitié des herbes et des fougères ne donnent rien de bon ; une liane sur trois porte des fruits plutôt que des gousses.
        sorte = sorte switch
        {
            SorteAliment.Graines when u < 0.35 => SorteAliment.Aucune,
            SorteAliment.Tubercules when e.Forme == 3 && u < 0.6 => SorteAliment.Aucune,
            SorteAliment.Gousses when e.Forme == 1 && u < 0.35 => SorteAliment.Fruits,
            SorteAliment.Gousses when u > 0.8 => SorteAliment.Aucune,
            SorteAliment.Noix when u < 0.45 => SorteAliment.Fruits,
            SorteAliment.Fruits when u > 0.85 => SorteAliment.Aucune,
            _ => sorte,
        };
        double R(double a, double b) => a + (b - a) * alea.Reel();
        var amertume = Math.Clamp(Math.Max(e.Toxicite, alea.Reel() < 0.3 ? R(0.2, 0.8) : R(0, 0.15)), 0, 1);
        return sorte switch
        {
            SorteAliment.Aucune => null,
            SorteAliment.Graines => new Aliment(sorte, Egrenage: R(0.3, 0.7), Tenaces: R(0.0005, 0.005), Dormance: R(0.2, 0.7), Vivace: alea.Reel() < 0.15,
                Bouture: false, Rendement: R(300, 800), Amertume: Math.Min(amertume, 0.3), Maturite: (int)R(90, 160), AnsAvantDeDonner: 0),
            SorteAliment.Gousses => new Aliment(sorte, Egrenage: R(0.25, 0.6), Tenaces: R(0.001, 0.008), Dormance: R(0.4, 0.85), Vivace: false,
                Bouture: false, Rendement: R(200, 600), Amertume: amertume, Maturite: (int)R(80, 140), AnsAvantDeDonner: 0),
            SorteAliment.Tubercules => new Aliment(sorte, 0, 1, 0, Vivace: true, Bouture: true, Rendement: R(1000, 5000), Amertume: amertume,
                Maturite: (int)R(150, 300), AnsAvantDeDonner: 0),
            SorteAliment.Fruits => new Aliment(sorte, 0, 1, R(0.2, 0.6), Vivace: e.Forme != 1, Bouture: alea.Reel() < 0.5, Rendement: R(1000, 4000),
                Amertume: Math.Min(amertume, 0.5), Maturite: (int)R(90, 150), AnsAvantDeDonner: e.Forme == 1 ? 0 : (int)R(2, 6)),
            _ => new Aliment(sorte, 0, 1, R(0.3, 0.8), Vivace: true, Bouture: false, Rendement: R(300, 1500), Amertume: amertume,
                Maturite: (int)R(150, 200), AnsAvantDeDonner: (int)R(5, 13)),
        };
    }
}
