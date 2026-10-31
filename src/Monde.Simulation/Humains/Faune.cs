using Monde.Simulation.Monde;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Ce que mange une bête.</summary>
public enum RegimeBete : byte
{
    /// <summary>Herbe, feuilles, graines : elle broute ou picore.</summary>
    Herbivore = 0,
    /// <summary>De tout, restes compris.</summary>
    Omnivore = 1,
    /// <summary>De la viande : proies et charognes.</summary>
    Carnivore = 2,
}

/// <summary>
/// Une espèce de bête du monde (génération 2, depuis la 0.33.1), décrite par sa forme et un trait (« la bête à cornes au pelage
/// roux »). Ses traits sont ceux par lesquels Diamond (1997) explique que si peu de grands mammifères aient été domestiqués :
/// ce qu'elle mange, le temps qu'elle met à grandir, si elle se reproduit en captivité, son humeur, sa panique, et si elle vit
/// en troupeau hiérarchisé. Rien n'en décide d'avance : une bête qui panique s'échappe, une bête agressive blesse, une bête qui
/// ne se reproduit pas captive ne fait jamais de troupeau.
/// </summary>
/// <param name="Poids">Poids d'un adulte sauvage, en kilos.</param>
/// <param name="AnsMaturite">Années avant qu'un petit puisse se reproduire.</param>
/// <param name="Portee">Petits par femelle et par an.</param>
/// <param name="Captive">Elle se reproduit en captivité.</param>
/// <param name="Agressivite">0 : paisible ; 1 : charge et blesse.</param>
/// <param name="Panique">0 : reste calme ; 1 : s'enfuit au moindre bruit et se blesse aux clôtures.</param>
/// <param name="Hierarchie">Vit en groupe qui suit un meneur (elle suivra l'homme qui prend sa place).</param>
/// <param name="Charognarde">Rôde autour des camps pour les restes (le chemin du commensal : Zeder, 2012).</param>
/// <param name="Lait">Lait qu'une femelle laisse prendre, de 0 (aucun) à 1.</param>
public sealed record Bete(int Numero, Biome Biome, int Forme, int Trait, RegimeBete Regime, double Poids, double AnsMaturite, double Portee,
    bool Captive, double Agressivite, double Panique, bool Hierarchie, bool Charognarde, double Lait)
{
    public object Dit => _dit ??= new Recit("bete", ("forme", Recit.Seul($"bete.forme.{Forme}")), ("trait", Recit.Seul($"bete.trait.{Trait}")));

    private Recit? _dit;

    /// <summary>Docilité d'une bête sauvage prise petite : celles qui ne paniquent ni ne chargent se laissent mieux approcher.</summary>
    public double DociliteSauvage => Math.Clamp(0.25 * (1 - Agressivite) * (1 - 0.5 * Panique) + (Charognarde ? 0.1 : 0), 0.02, 0.4);
}

/// <summary>
/// La faune du monde : quelques espèces de bêtes par terrain, tirées de la graine (un hasard à part : la flore et la carte ne
/// changent pas). Il n'y a ni chien, ni chèvre, ni bœuf écrits d'avance : seulement des formes et des traits. Les fourchettes
/// suivent les grands mammifères et oiseaux des régions où l'on a domestiqué (Diamond, 1997 ; Zeder, 2012).
/// </summary>
public sealed class Faune
{
    /// <summary>
    /// Formes : la bête à cornes, la bête à sabots, le petit brouteur, le fouisseur, le rôdeur, le grand chasseur, l'oiseau coureur,
    /// le grimpeur.
    /// </summary>
    public const int NombreDeFormes = 8;

    public const int NombreDeTraits = 12;

    public IReadOnlyList<Bete> Betes { get; }

    private readonly Dictionary<Biome, Bete[]> _parBiome;

    public Faune(ulong graine, bool nouveauxTerrains = false)
    {
        var alea = new Alea(graine ^ 0x4641554E45UL);
        var betes = new List<Bete>();
        var terres = new[] { Biome.Marais, Biome.PlaineDeFougeres, Biome.ForetDeConiferes, Biome.ForetTropicale, Biome.Desert, Biome.Montagne, Biome.Plage, Biome.Riviere };
        if (nouveauxTerrains) terres = [.. terres, Biome.ForetDeFeuillus, Biome.Mangrove, Biome.Lande];
        foreach (var b in terres)
        {
            var n = b is Biome.Plage or Biome.Riviere or Biome.Mangrove or Biome.Desert ? 3 : 5;
            var prises = new HashSet<int>();
            for (var k = 0; k < n; k++)
            {
                int forme;
                do forme = FormeSur(b, alea);
                while (!prises.Add(forme) && prises.Count < NombreDeFormes);
                betes.Add(Tirer(betes.Count + 1, b, forme, alea.Entier(NombreDeTraits), alea));
            }
        }
        Betes = betes;
        _parBiome = betes.GroupBy(e => e.Biome).ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>Une faune donnée (pour les essais).</summary>
    internal Faune(IReadOnlyList<Bete> betes)
    {
        Betes = betes;
        _parBiome = betes.GroupBy(e => e.Biome).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public Bete Get(int numero) => Betes[numero - 1];

    /// <summary>Bêtes de ce terrain.</summary>
    public IReadOnlyList<Bete> Sur(Biome b) => _parBiome.TryGetValue(b, out var l) ? l : [];

    /// <summary>Une forme selon le terrain : peu de grands brouteurs en forêt dense, des rôdeurs presque partout.</summary>
    private static int FormeSur(Biome b, Alea alea)
    {
        double[] poids = b switch
        {
            Biome.PlaineDeFougeres or Biome.Lande => [3, 3, 2, 1, 2, 1, 2, 0.3],
            Biome.Montagne => [2, 1, 4, 0.5, 1.5, 1, 1, 0.5],
            Biome.Desert => [1, 2, 2, 0.5, 2, 1, 2, 0],
            Biome.ForetTropicale => [0.5, 0.5, 1, 3, 1, 1.5, 2, 3],
            Biome.ForetDeConiferes or Biome.ForetDeFeuillus => [1.5, 2, 1, 3, 2, 1, 1.5, 1],
            Biome.Marais or Biome.Mangrove => [1.5, 0.5, 0.5, 2, 1, 1, 3, 1],
            _ => [1, 1, 1, 1, 2, 0.5, 2, 0.5],
        };
        var u = alea.Reel() * poids.Sum();
        for (var k = 0; k < poids.Length; k++)
        {
            u -= poids[k];
            if (u < 0) return k;
        }
        return poids.Length - 1;
    }

    /// <summary>Les traits d'une espèce, d'après sa forme. Peu d'espèces réunissent tout ce qu'il faut pour vivre auprès de l'homme.</summary>
    private static Bete Tirer(int numero, Biome b, int forme, int trait, Alea alea)
    {
        double R(double a, double z) => a + (z - a) * alea.Reel();
        bool P(double p) => alea.Reel() < p;
        return forme switch
        {
            // Grands bovidés : lourds, lents à grandir, souvent irascibles ; quelques-uns suivent un meneur.
            0 => new Bete(numero, b, forme, trait, RegimeBete.Herbivore, R(150, 700), R(2, 4), R(0.6, 0.9), P(0.5), R(0.25, 0.95), R(0.2, 0.7), P(0.6), false, R(0.3, 1)),
            // Ongulés coureurs : paniquent, sautent, se brisent les pattes en enclos (gazelles, cerfs) ; parfois plus calmes (chevaux).
            1 => new Bete(numero, b, forme, trait, RegimeBete.Herbivore, R(40, 450), R(1.5, 3), R(0.7, 1.2), P(0.4), R(0.1, 0.6), R(0.45, 1), P(0.45), false, R(0, 0.6)),
            // Petits brouteurs (chèvres, moutons sauvages) : grandissent vite, vivent en troupe hiérarchisée.
            2 => new Bete(numero, b, forme, trait, RegimeBete.Herbivore, R(20, 70), R(1, 2), R(1, 1.8), P(0.6), R(0.1, 0.6), R(0.25, 0.8), P(0.7), false, R(0.3, 0.9)),
            // Fouisseurs omnivores (sangliers) : portées nombreuses, mangent les restes, peuvent charger.
            3 => new Bete(numero, b, forme, trait, RegimeBete.Omnivore, R(40, 180), R(1, 1.5), R(3, 7), P(0.65), R(0.3, 0.9), R(0.2, 0.6), P(0.5), P(0.3), 0),
            // Rôdeurs (canidés) : meute hiérarchisée, charognards autour des camps.
            4 => new Bete(numero, b, forme, trait, RegimeBete.Carnivore, R(10, 40), R(1, 2), R(3, 6), P(0.75), R(0.2, 0.7), R(0.2, 0.6), P(0.75), P(0.7), 0),
            // Grands chasseurs (félins) : solitaires, dangereux, ne se reproduisent guère captifs.
            5 => new Bete(numero, b, forme, trait, RegimeBete.Carnivore, R(40, 250), R(2, 4), R(1, 3), P(0.12), R(0.7, 1), R(0.2, 0.6), false, P(0.15), 0),
            // Oiseaux coureurs (gallinacés, grands coureurs) : picorent de tout, pondent beaucoup.
            6 => new Bete(numero, b, forme, trait, RegimeBete.Omnivore, R(1, 40), R(0.5, 2), R(4, 12), P(0.65), R(0.05, 0.5), R(0.3, 0.9), P(0.55), P(0.2), 0),
            // Grimpeurs : vivent dans les arbres, ne suivent personne.
            _ => new Bete(numero, b, forme, trait, RegimeBete.Omnivore, R(3, 60), R(3, 7), R(0.3, 1), P(0.2), R(0.3, 0.9), R(0.4, 0.9), P(0.3), P(0.2), 0),
        };
    }
}
