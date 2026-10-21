namespace Monde.Simulation.Nature;

/// <summary>Ce que fait la nature d'extraordinaire : catastrophes et phénomènes du ciel.</summary>
public enum TypeCatastrophe : byte
{
    Seisme = 0,
    Tsunami = 1,
    Eruption = 2,
    Crue = 3,
    Incendie = 4,
    Cyclone = 5,
    VagueDeFroid = 6,
    Canicule = 7,
    Epidemie = 8,
    /// <summary>Le soleil s'obscurcit en plein jour.</summary>
    Eclipse = 9,
    Comete = 10,
    /// <summary>Boule de feu dans le ciel ; parfois une pierre tombe au sol (météorite).</summary>
    Bolide = 11,
    /// <summary>Chute d'un astéroïde ou d'une comète : souffle, incendies (de l'ordre d'une fois en un million d'années ici).</summary>
    Impact = 12,
    /// <summary>Aurore visible très loin des pôles, lors d'un grand orage magnétique.</summary>
    Aurore = 13,
    /// <summary>Après une très grande éruption, le voile de cendres et de soufre refroidit le climat pendant des années.</summary>
    HiverVolcanique = 14,
    /// <summary>Après un très gros impact, poussières et suies voilent le soleil : le froid et la nuit durent des mois ou des années.</summary>
    HiverDImpact = 15,
}

public static class TypesCatastrophe
{
    /// <summary>Phénomènes du ciel : ils ne détruisent rien (sauf un impact) mais frappent les esprits.</summary>
    public static bool EstPhenomene(TypeCatastrophe t) =>
        t is TypeCatastrophe.Eclipse or TypeCatastrophe.Comete or TypeCatastrophe.Bolide or TypeCatastrophe.Aurore;

    /// <summary>Nom court, avec article, pour les phrases (« la crue », « le séisme »).</summary>
    public static string Nom(TypeCatastrophe t) => t switch
    {
        TypeCatastrophe.Seisme => "le séisme",
        TypeCatastrophe.Tsunami => "la vague géante",
        TypeCatastrophe.Eruption => "l'éruption",
        TypeCatastrophe.Crue => "la crue",
        TypeCatastrophe.Incendie => "l'incendie",
        TypeCatastrophe.Cyclone => "la tempête",
        TypeCatastrophe.VagueDeFroid => "le grand froid",
        TypeCatastrophe.Canicule => "la canicule",
        TypeCatastrophe.Epidemie => "l'épidémie",
        TypeCatastrophe.Eclipse => "l'éclipse",
        TypeCatastrophe.Comete => "la comète",
        TypeCatastrophe.Bolide => "la boule de feu",
        TypeCatastrophe.Impact => "le feu tombé du ciel",
        TypeCatastrophe.Aurore => "les lueurs du ciel",
        TypeCatastrophe.HiverVolcanique => "l'hiver sans fin",
        TypeCatastrophe.HiverDImpact => "la longue nuit",
        _ => "la catastrophe",
    };

    /// <summary>« du séisme », « de la crue », « de l'éruption ».</summary>
    public static string De(TypeCatastrophe t)
    {
        var nom = Nom(t);
        return nom.StartsWith("le ", StringComparison.Ordinal) ? "du " + nom[3..]
            : nom.StartsWith("les ", StringComparison.Ordinal) ? "des " + nom[4..]
            : "de " + nom;
    }
}

/// <summary>
/// Une catastrophe ou un phénomène, de son début à sa fin. Son numéro est celui de l'événement du journal
/// qui l'annonce : les morts, les blessés et les peurs qu'elle cause le citent comme cause.
/// </summary>
public sealed class Catastrophe
{
    public required long Id { get; init; }
    public required TypeCatastrophe Type { get; init; }
    public Origine Origine { get; init; }
    public required long Debut { get; init; }

    /// <summary>Fin, ou null tant qu'elle dure.</summary>
    public long? Fin { get; set; }

    /// <summary>Centre (épicentre, volcan, départ du feu, point d'arrivée de la tempête) et rayon de la zone touchée, en cases.</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int Rayon { get; set; }

    /// <summary>Magnitude, indice d'explosivité, catégorie, écart de température, pathogène… selon le type.</summary>
    public double Intensite { get; init; }

    /// <summary>Nom pour l'observateur : « Séisme de magnitude 6,4 au nord-ouest ».</summary>
    public required string Nom { get; set; }

    public int Morts { get; set; }
    public int Blesses { get; set; }

    /// <summary>
    /// Fin des suites : tant que la nature touchée n'a pas reverdi, ceux qui y meurent de faim, de soif, de froid,
    /// d'épuisement ou de maladie meurent des suites de la catastrophe (0 : pas de suites, ou pas encore connues).
    /// </summary>
    public long SuitesJusqua { get; set; }

    /// <summary>Morts dans les suites, après la fin de la catastrophe elle-même.</summary>
    public int MortsSuites { get; set; }

    /// <summary>Encore en mémoire : en cours, ou dans ses suites.</summary>
    public bool EnSuites(long maintenant) => !Active && SuitesJusqua > 0 && SuitesJusqua > maintenant;

    /// <summary>Cases touchées (brûlées, inondées, ensevelies sous la cendre…), quand la zone n'est pas un simple disque.</summary>
    public HashSet<int> Cases { get; set; } = [];

    /// <summary>
    /// Dégâts (sur 10 000) que cette catastrophe a elle-même causés à chaque tuile, pour mesurer ses suites sans lui imputer
    /// les cicatrices d'une autre. Gardés en mémoire seulement : à sa fin, les tuiles qu'elle a gravement touchées
    /// rejoignent <see cref="Cases"/>, qui est enregistré.
    /// </summary>
    public Dictionary<int, ushort> Ravages { get; set; } = [];

    /// <summary>Front d'un incendie : cases en train de brûler, avec l'heure où chacune s'éteindra.</summary>
    public Dictionary<int, long> Front { get; set; } = [];

    /// <summary>Direction du vent (radians) pour un feu.</summary>
    public double Direction { get; init; }

    /// <summary>Volcan, pathogène ou autre précision selon le type (0 si sans objet).</summary>
    public int Parametre { get; init; }

    /// <summary>Prochaine étape prévue (paroxysme d'une éruption, arrivée d'une vague), 0 si aucune.</summary>
    public long Prochaine { get; set; }

    /// <summary>Phase en cours, propre au type (0 : début).</summary>
    public int Phase { get; set; }

    /// <summary>Durée prévue depuis le début (0 : elle finira autrement, par la pluie, une étape ou la guérison des malades).</summary>
    public long Duree { get; set; }

    /// <summary>L'observateur a-t-il déjà été alerté ?</summary>
    public bool Signalee { get; set; }

    /// <summary>Morts par communauté, pour le bilan de fin.</summary>
    public SortedDictionary<int, int> PertesParCommunaute { get; set; } = [];

    /// <summary>Communautés touchées.</summary>
    public SortedSet<int> Communautes { get; set; } = [];

    /// <summary>Humains qui l'ont vécue (témoins, victimes), pour ne compter chacun qu'une fois.</summary>
    public HashSet<int> Temoins { get; set; } = [];

    /// <summary>Pour un incendie parti d'un camp : celui qui avait la garde du feu (0 : la nature, ou personne).</summary>
    public int ResponsableId { get; set; }

    /// <summary>Négligence de ce gardien, de 0 à 1 : feu laissé sans personne, allumé dans le vent, leçons connues et pas suivies.</summary>
    public double Negligence { get; set; }

    /// <summary>Ce qui a fait partir le feu du camp, et donc la leçon qu'on peut en tirer (<see cref="Humains.Effet.Rien"/> : sans objet).</summary>
    public Humains.Effet Lecon { get; set; }

    /// <summary>Humains tués ou blessés.</summary>
    public HashSet<int> Victimes { get; set; } = [];

    public bool Active => Fin is null;

    public Catastrophe Copier()
    {
        var copie = (Catastrophe)MemberwiseClone();
        copie.Cases = [.. Cases];
        copie.Front = new Dictionary<int, long>(Front);
        copie.Ravages = new Dictionary<int, ushort>(Ravages);
        copie.Communautes = [.. Communautes];
        copie.Temoins = [.. Temoins];
        copie.Victimes = [.. Victimes];
        copie.PertesParCommunaute = new SortedDictionary<int, int>(PertesParCommunaute);
        return copie;
    }
}

/// <summary>
/// Une maladie contagieuse, née d'un contact avec une bête (zoonose). Ses traits sont tirés à sa naissance :
/// la plupart des maladies venues des animaux se transmettent mal entre humains et s'éteignent vite.
/// </summary>
/// <param name="Transmission">Probabilité par heure qu'un malade contamine quelqu'un sur la même case.</param>
/// <param name="IncubationJours">Durée moyenne avant les premiers signes.</param>
/// <param name="DureeJours">Durée moyenne de la maladie (on est contagieux pendant ce temps).</param>
/// <param name="Letalite">Part des malades qui en meurent, pour un adulte en bonne santé.</param>
/// <param name="ImmuniteAns">Durée de l'immunité après guérison (0 : à vie).</param>
public sealed record Pathogene(int Id, string Nom, double Transmission, double IncubationJours, double DureeJours, double Letalite,
    double ImmuniteAns, long ApparuLe, long EvenementId);

/// <summary>Immunité d'un humain contre une maladie, jusqu'à une date (null : à vie).</summary>
public sealed record Immunite(int PathogeneId, long? Jusqua);
