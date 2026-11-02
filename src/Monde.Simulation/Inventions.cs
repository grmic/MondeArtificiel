using Monde.Simulation.Humains;
using Monde.Simulation.Monde;

namespace Monde.Simulation;

/// <summary>
/// Une chose que le monde n'avait jamais connue, créée par l'Entité libre (0.35.3) : une plante ou une bête. Elle est gardée telle
/// que le LLM l'a voulue (forme, nom, propriétés) ; le reste (rendement, traits non dits) est tiré d'un hasard à part, toujours le
/// même pour une même invention, si bien qu'elle se reconstruit à l'identique au chargement du monde.
/// </summary>
/// <param name="Id">Numéro de l'invention (1, 2…) ; sert aussi de graine au hasard de ses traits.</param>
/// <param name="Sorte">« plante » ou « bete ».</param>
/// <param name="Numero">Son numéro dans la flore ou la faune du monde.</param>
/// <param name="Forme">Indice de la forme (voir <see cref="Inventions.FormesPlante"/> et <see cref="Inventions.FormesBete"/>).</param>
/// <param name="Nom">Le nom que l'Entité lui a donné (écrit par le LLM, dans la langue de l'interface du moment).</param>
/// <param name="Proprietes">Ce que l'Entité a fixé (vertus, poids, docilité…), dans les bornes de <see cref="Inventions"/>.</param>
/// <param name="Aliment">Ce qu'une plante donne à manger.</param>
/// <param name="Disparue">Défaite : elle ne pousse ou ne vit plus nulle part.</param>
public sealed record Invention(int Id, string Sorte, int Numero, Biome Biome, int Forme, string Nom, Dictionary<string, double> Proprietes,
    SorteAliment Aliment, long Le, long Evenement, bool Disparue = false);

/// <summary>Ce que l'Entité libre peut inventer, et comment une invention entre dans la flore ou la faune.</summary>
public static class Inventions
{
    /// <summary>Formes de plantes, dans l'ordre des formes de la flore.</summary>
    public static readonly string[] FormesPlante = ["arbuste", "liane", "herbe", "fougere", "arbre", "buisson", "racine", "plante"];

    /// <summary>Formes de bêtes, dans l'ordre des formes de la faune.</summary>
    public static readonly string[] FormesBete = ["cornes", "sabots", "brouteur", "fouisseur", "rodeur", "chasseur", "coureur", "grimpeur"];

    /// <summary>Vertus d'une plante (0 à 1).</summary>
    public static readonly string[] VertusPlante = ["toxicite", "astringente", "febrifuge", "antipaludique", "antiseptique", "vermifuge", "psychotrope"];

    /// <summary>Traits d'une bête qu'on peut fixer, avec leurs bornes.</summary>
    public static readonly Dictionary<string, (double Min, double Max)> TraitsBete = new()
    {
        ["poids"] = (0.5, 3000), ["maturite"] = (0.2, 15), ["portee"] = (0.2, 15), ["agressivite"] = (0, 1), ["panique"] = (0, 1), ["lait"] = (0, 1),
        ["captive"] = (0, 1), ["hierarchie"] = (0, 1), ["charognarde"] = (0, 1),
    };

    /// <summary>Sortes d'aliments, par leur nom (« aucun » : la plante ne se mange pas).</summary>
    public static readonly string[] Aliments = ["aucun", "graines", "gousses", "tubercules", "fruits", "noix"];

    /// <summary>Plus long nom accepté.</summary>
    public const int NomMax = 80;

    private static Alea AleaDe(ulong graine, int id) => new(graine ^ 0x494E56454E54UL ^ ((ulong)id * 0x9E3779B97F4A7C15UL));

    /// <summary>La plante telle qu'elle entre dans la flore (son numéro est donné par la flore).</summary>
    public static Espece Plante(Invention i, ulong graine)
    {
        double V(string k) => i.Proprietes.TryGetValue(k, out var v) ? Math.Clamp(v, 0, 1) : 0;
        var e = new Espece(0, i.Nom, [i.Biome], V("toxicite"), V("astringente"), V("febrifuge"), V("antipaludique"), V("antiseptique"), V("vermifuge"),
            V("psychotrope"), Forme: i.Forme, Trait: -1);
        return e with { Aliment = Humains.Aliment.Selon(i.Aliment, e, AleaDe(graine, i.Id)) };
    }

    /// <summary>La bête telle qu'elle entre dans la faune : les traits de sa forme, sauf ceux que l'Entité a fixés.</summary>
    public static Bete Bete(Invention i, ulong graine)
    {
        var b = Faune.Modele(i.Biome, i.Forme, AleaDe(graine, i.Id));
        double V(string k, double defaut) => i.Proprietes.TryGetValue(k, out var v) ? Math.Clamp(v, TraitsBete[k].Min, TraitsBete[k].Max) : defaut;
        bool O(string k, bool defaut) => i.Proprietes.TryGetValue(k, out var v) ? v >= 0.5 : defaut;
        return b with
        {
            Poids = V("poids", b.Poids), AnsMaturite = V("maturite", b.AnsMaturite), Portee = V("portee", b.Portee), Agressivite = V("agressivite", b.Agressivite),
            Panique = V("panique", b.Panique), Lait = V("lait", b.Lait), Captive = O("captive", b.Captive), Hierarchie = O("hierarchie", b.Hierarchie),
            Charognarde = O("charognarde", b.Charognarde), Nom = i.Nom,
        };
    }
}
