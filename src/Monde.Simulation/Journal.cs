namespace Monde.Simulation;

public enum TypeEvenement : byte
{
    Naissance = 0,
    Mort = 1,
    Maladie = 2,
    Union = 3,
    Accouplement = 4,
    Grossesse = 5,
    FausseCouche = 6,
    Delaissement = 7,
    /// <summary>Réservé aux découvertes (feu, outils…), à venir.</summary>
    Decouverte = 8,
    MortNe = 9,
}

/// <summary>
/// D'où vient un événement : du monde lui-même, ou d'une intervention de l'Entité (intuition soufflée, rêve, signe…).
/// Le journal les distingue toujours.
/// </summary>
public enum Origine : byte
{
    Naturelle = 0,
    Entite = 1,
}

/// <summary>Un événement du journal. <see cref="Id"/> vaut 0 tant qu'il n'est pas enregistré.</summary>
public sealed record EvenementJournal(long Secondes, TypeEvenement Type, int HumainId, int AutreId, string Texte,
    Origine Origine = Origine.Naturelle)
{
    public long Id { get; init; }
}

/// <summary>Formulations partagées par le journal et le serveur.</summary>
public static class Libelles
{
    public static string Cause(Humains.CauseDeMort cause) => cause switch
    {
        Humains.CauseDeMort.Faim => "de faim",
        Humains.CauseDeMort.Soif => "de soif",
        Humains.CauseDeMort.Froid => "de froid",
        Humains.CauseDeMort.Maladie => "de maladie",
        Humains.CauseDeMort.Epuisement => "d'épuisement",
        Humains.CauseDeMort.Vieillesse => "de vieillesse",
        Humains.CauseDeMort.Accouchement => "en couches",
        _ => "",
    };
}
