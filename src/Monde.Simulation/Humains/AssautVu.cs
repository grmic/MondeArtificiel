namespace Monde.Simulation.Humains;

/// <summary>Ce qui arrive dans un duel de l'assaut (0.42.0).</summary>
public enum IssueDuel : byte
{
    /// <summary>Coups échangés, personne n'est touché.</summary>
    Rien = 0,
    /// <summary>L'assaillant tue le défenseur.</summary>
    DefenseurTue = 1,
    /// <summary>Le défenseur tue l'assaillant.</summary>
    AssaillantTue = 2,
    /// <summary>Le défenseur est blessé.</summary>
    DefenseurBlesse = 3,
    /// <summary>L'assaillant est blessé.</summary>
    AssaillantBlesse = 4,
    /// <summary>L'assaillant enlève la défenseure.</summary>
    Enlevee = 5,
}

/// <summary>Un duel de l'assaut, dans l'ordre où il a eu lieu.</summary>
public readonly record struct DuelAssaut(int Assaillant, int Defenseur, Arme ArmeAssaillant, Arme ArmeDefenseur, IssueDuel Issue);

/// <summary>
/// Le déroulé d'un assaut, gardé pour le montrer dans la vue 3D (0.42.0) : qui a frappé qui, dans quel ordre, avec quoi, et ce qui
/// est arrivé. L'assaut se calcule toujours d'un coup ; le déroulé n'en est que la trace, il ne change rien à l'histoire et n'est
/// gardé qu'en mémoire, quelques jours.
/// </summary>
public sealed class AssautVu
{
    /// <summary>Ligne du journal qui raconte l'assaut.</summary>
    public long EvenementId { get; set; }
    public required long Secondes { get; init; }
    /// <summary>Camp attaqué.</summary>
    public required int X { get; init; }
    public required int Y { get; init; }
    /// <summary>Communauté assaillante et communauté attaquée.</summary>
    public required int Assaillants { get; init; }
    public required int Cible { get; init; }
    /// <summary>Tous les assaillants, et tous les adultes du camp qui pouvaient se défendre.</summary>
    public required int[] Troupe { get; init; }
    public required int[] Defenseurs { get; init; }
    /// <summary>Ceux qui dormaient quand l'assaut a commencé.</summary>
    public required int[] Endormis { get; init; }
    public List<DuelAssaut> Duels { get; } = [];
    /// <summary>Blessés morts de leurs blessures juste après l'assaut.</summary>
    public List<int> MortsDeBlessures { get; } = [];

    /// <summary>Combien de temps un déroulé reste visible : deux jours.</summary>
    public const long Duree = 2 * Temps.Calendrier.SecondesParJour;

    /// <summary>Morts de l'assaut (tués et morts de leurs blessures).</summary>
    public IEnumerable<int> Morts() =>
        Duels.Where(d => d.Issue == IssueDuel.DefenseurTue).Select(d => d.Defenseur)
            .Concat(Duels.Where(d => d.Issue == IssueDuel.AssaillantTue).Select(d => d.Assaillant))
            .Concat(MortsDeBlessures).Distinct();
}

/// <summary>Une troupe qui part en raid, ou qui arrive près du camp visé (0.42.0, pour les alertes).</summary>
public readonly record struct TroupeSignalee(bool AuxPortes, int Communaute, int Cible, int Chef, int Guerriers, long EvenementId, long Secondes);
