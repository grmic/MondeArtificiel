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
        Especes = especes;
        _parBiome = especes.GroupBy(e => e.Biomes[0]).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public Espece Get(int numero) => Especes[numero - 1];

    /// <summary>Espèces qui poussent sur ce terrain.</summary>
    public IReadOnlyList<Espece> Sur(Biome b) => _parBiome.TryGetValue(b, out var l) ? l : [];
}
