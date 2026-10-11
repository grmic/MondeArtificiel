using Monde.Simulation.Nature;

namespace Monde.Simulation.Monde;

/// <summary>
/// Réglages choisis à la création d'un monde. Ils sont enregistrés dans la base du monde : la carte est régénérée
/// à l'identique à chaque reprise. Un monde créé avant la configuration reçoit les réglages d'origine
/// (<see cref="ParDefaut"/>), qui redonnent exactement la même carte.
/// </summary>
/// <param name="Graine">Graine du hasard : même graine et mêmes réglages, même monde de départ.</param>
/// <param name="Couples">Couples de départ, un par emplacement.</param>
/// <param name="Largeur">Largeur de la carte en cases (une case : <see cref="Echelle.KilometresParCase"/> km).</param>
/// <param name="Hauteur">Hauteur de la carte en cases.</param>
/// <param name="Continents">Nombre de continents séparés par la mer.</param>
/// <param name="Placement">
/// Où partent les couples : sur le n-ième continent par la taille (1 : le plus grand, par défaut), ou répartis sur tous
/// (<see cref="PlacementTous"/>).
/// </param>
public sealed record ConfigurationMonde(ulong Graine, int Couples, int Largeur, int Hauteur, int Continents, int Placement = 1)
{
    /// <summary>Couples répartis sur tous les continents, en proportion de leurs terres habitables.</summary>
    public const int PlacementTous = 0;

    public const int CouplesParDefaut = 33;
    public const int TailleParDefaut = 256;
    public const int CouplesMin = 1;
    public const int CouplesMax = 200;
    public const int TailleMin = 64;
    public const int TailleMax = 1024;
    public const int ContinentsMin = 1;
    public const int ContinentsMax = 6;

    /// <summary>Réglages d'origine : 33 couples sur un continent unique, carte de 256 × 256 cases (205 km de côté).</summary>
    public static ConfigurationMonde ParDefaut(ulong graine) =>
        new(graine, CouplesParDefaut, TailleParDefaut, TailleParDefaut, 1);

    /// <summary>Premier défaut des réglages, ou null s'ils sont valides (la place pour les couples est vérifiée à la génération).</summary>
    public string? Erreur() =>
        Couples is < CouplesMin or > CouplesMax ? $"Le nombre de couples doit être compris entre {CouplesMin} et {CouplesMax}."
        : Largeur is < TailleMin or > TailleMax || Hauteur is < TailleMin or > TailleMax
            ? $"La carte doit mesurer entre {Echelle.Distance(Echelle.Kilometres(TailleMin))} et {Echelle.Distance(Echelle.Kilometres(TailleMax))} de côté."
        : Continents is < ContinentsMin or > ContinentsMax ? $"Le nombre de continents doit être compris entre {ContinentsMin} et {ContinentsMax}."
        : Placement < PlacementTous || Placement > Continents ? "Le continent de départ des couples doit être l'un des continents du monde."
        : null;

    /// <summary>Où partent les couples, en mots.</summary>
    public string DecrirePlacement() => Placement switch
    {
        PlacementTous => "répartis sur tous les continents",
        1 => Continents == 1 ? "sur le continent" : "sur le plus grand continent",
        var n => $"sur le {n}e continent par la taille",
    };
}
