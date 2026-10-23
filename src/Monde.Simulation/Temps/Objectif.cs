namespace Monde.Simulation.Temps;

public enum TypeObjectif : byte
{
    /// <summary>Atteindre une date précise.</summary>
    Date = 0,
    /// <summary>La prochaine naissance.</summary>
    Naissance = 1,
    /// <summary>La prochaine mort.</summary>
    Mort = 2,
}

/// <summary>
/// « Avancer jusqu'à » : le monde file à la vitesse maximale, puis se met en pause dès que l'objectif est atteint.
/// Un objectif d'événement s'arrête aussi à sa date limite si l'événement ne survient pas.
/// </summary>
public sealed record Objectif(TypeObjectif Type, long Limite, string Description)
{
    /// <summary>Un événement est attendu au plus cent ans.</summary>
    public const long AttenteMaximale = 100 * Calendrier.SecondesParAn;

    /// <summary>Une date au plus mille ans dans le futur.</summary>
    public const long PorteeMaximale = 1000 * Calendrier.SecondesParAn;

    public static Objectif Date(long cible, long maintenant)
    {
        if (cible <= maintenant) throw new ArgumentOutOfRangeException(nameof(cible), "La date doit être dans le futur.");
        if (cible - maintenant > PorteeMaximale) throw new ArgumentOutOfRangeException(nameof(cible), "La date est trop lointaine (mille ans au plus).");
        return new Objectif(TypeObjectif.Date, cible, Calendrier.Decomposer(cible).ToString());
    }

    public static Objectif Evenement(TypeObjectif type, long maintenant)
    {
        if (type == TypeObjectif.Date) throw new ArgumentException("Utiliser Objectif.Date.", nameof(type));
        return new Objectif(type, maintenant + AttenteMaximale, type == TypeObjectif.Naissance ? "prochaine naissance" : "prochaine mort");
    }

    /// <summary>Secondes du monde correspondant à une date du calendrier (année ≥ 1, saison 1 à 4, jour 1 à 90, heure 0 à 23).</summary>
    public static long Secondes(long annee, int saison, int jour, int heure)
    {
        if (annee < 1 || annee > 1_000_000) throw new ArgumentOutOfRangeException(nameof(annee));
        if (saison is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(saison));
        if (jour < 1 || jour > Calendrier.JoursParSaison) throw new ArgumentOutOfRangeException(nameof(jour));
        if (heure is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(heure));
        return (annee - 1) * Calendrier.SecondesParAn + ((saison - 1) * Calendrier.JoursParSaison + jour - 1) * Calendrier.SecondesParJour
               + heure * Calendrier.SecondesParHeure;
    }

    public static long ProchainMatin(long maintenant)
    {
        var jour = maintenant / Calendrier.SecondesParJour * Calendrier.SecondesParJour;
        var matin = jour + 6 * Calendrier.SecondesParHeure;
        return matin > maintenant ? matin : matin + Calendrier.SecondesParJour;
    }

    public static long ProchaineSaison(long maintenant)
    {
        const long saison = Calendrier.JoursParSaison * Calendrier.SecondesParJour;
        return (maintenant / saison + 1) * saison;
    }

    public static long ProchaineAnnee(long maintenant) => (maintenant / Calendrier.SecondesParAn + 1) * Calendrier.SecondesParAn;
}

/// <summary>Un objectif « avancer jusqu'à » terminé : atteint, ou abandonné quand l'événement attendu n'est pas venu en cent ans.</summary>
public sealed record ObjectifAtteint(Objectif Objectif, bool RienEnCentAns, string Description);
