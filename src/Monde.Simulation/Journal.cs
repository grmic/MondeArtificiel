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
    /// <summary>Une pensée (ce que l'humain fait et pourquoi).</summary>
    Pensee = 10,
    /// <summary>Amitié ou hostilité qui naît ou qui s'éteint.</summary>
    Lien = 11,
    /// <summary>Deuil, tristesse profonde, guérison.</summary>
    Humeur = 12,
    /// <summary>Communauté qui se forme, se disperse, membre qui arrive ou part.</summary>
    Communaute = 13,
    /// <summary>Un lieu oublié.</summary>
    Souvenir = 14,
    /// <summary>Nourriture partagée avec un autre que son enfant.</summary>
    Partage = 15,
    /// <summary>Résumé des gestes quotidiens d'un humain sur une journée.</summary>
    Journee = 16,
    /// <summary>Sécheresse, année d'abondance : ce que fait la nature, région par région.</summary>
    Nature = 17,
    /// <summary>Jalousie, querelle, coups, séparation, pardon, réprobation : les offenses et leurs suites.</summary>
    Conflit = 18,
    /// <summary>Séisme, éruption, crue, incendie, tempête, épidémie, phénomène du ciel : ce que fait la nature d'extraordinaire.</summary>
    Catastrophe = 19,
    /// <summary>Raid, guerre, pacte, soumission, paix : la violence entre communautés et ses suites.</summary>
    Guerre = 20,
    /// <summary>Désirs, avances acceptées ou repoussées, désirs refoulés, amours entre personnes de même sexe.</summary>
    Attirance = 21,
    /// <summary>Exil hors de la communauté, rejet par un parent, retour d'exil.</summary>
    Exclusion = 22,
    /// <summary>Soins à un malade ou un blessé, consolation, enfant recueilli : la compassion en actes.</summary>
    Entraide = 23,
    /// <summary>Vol de nourriture, mensonge démasqué.</summary>
    Vol = 24,
    /// <summary>L'Entité parle : ce que l'humain a compris, cru, décidé et fait, et ce qu'il en a raconté.</summary>
    Parole = 25,
    /// <summary>La croyance d'un humain en l'invisible change d'une dizaine : ce qui l'a fait grandir ou décliner.</summary>
    Croyance = 26,
    /// <summary>Guérisseurs reconnus ou désavoués, transes, charlatans démasqués, malédictions : la médecine et ses croyances.</summary>
    Medecine = 27,
    /// <summary>Chants, rythmes, danses, fêtes, lamentations, peintures sur la roche : les arts.</summary>
    Art = 28,
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

/// <summary>
/// Un événement du journal. <see cref="Id"/> vaut 0 tant qu'il n'est pas numéroté.
/// <see cref="CauseId"/> désigne l'événement qui l'a provoqué (0 : aucune cause notée) : en remontant les causes,
/// on suit l'enchaînement des faits (une trahison, la jalousie, des coups, une mort, un deuil, une vengeance…).
/// </summary>
public sealed record EvenementJournal(long Secondes, TypeEvenement Type, int HumainId, int AutreId, string Texte,
    Origine Origine = Origine.Naturelle)
{
    public long Id { get; init; }
    public long CauseId { get; init; }

    /// <summary>
    /// Communauté concernée (0 : aucune) et, pour ce qui se passe entre deux groupes, l'autre communauté.
    /// Par défaut, c'est la communauté de l'humain au moment des faits : la chronologie d'un groupe s'en déduit.
    /// </summary>
    public int CommunauteId { get; init; }
    public int AutreCommunauteId { get; init; }

    /// <summary>
    /// Événement significatif : un tournant pour un groupe ou pour le monde (projet de guerre, renoncement, raid, début et
    /// fin d'une guerre, pacte, soumission, catastrophe…). L'observateur peut ne suivre que ceux-là.
    /// </summary>
    public bool Significatif { get; init; }

    /// <summary>
    /// Karma (regard de l'Entité, invisible aux humains) gagné ou perdu par l'humain (<see cref="HumainId"/>) à cause de cet acte,
    /// et par l'autre (<see cref="AutreId"/>) : le meurtrier dans une mort, le second dans un accouplement hors du couple.
    /// </summary>
    public double Karma { get; init; }
    public double KarmaAutre { get; init; }
}

/// <summary>
/// Distribue les numéros des événements du journal dès leur création, pour qu'un événement puisse citer sa cause
/// avant même d'être enregistré. Un seul numéroteur est partagé par tout le monde simulé.
/// </summary>
public sealed class Numeroteur
{
    public long Prochain { get; set; } = 1;

    public long Suivant() => Prochain++;
}

public static class TypesEvenement
{
    /// <summary>
    /// Types très fréquents (pensées, souvenirs, partages, accouplements) : ils sont enregistrés comme les autres,
    /// mais ne sont ni gardés dans la file des événements récents ni poussés en direct aux navigateurs.
    /// </summary>
    public static bool EstFrequent(TypeEvenement t) =>
        t is TypeEvenement.Pensee or TypeEvenement.Journee or TypeEvenement.Souvenir or TypeEvenement.Partage or TypeEvenement.Accouplement;
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
        Humains.CauseDeMort.Desespoir => "de sa propre main",
        Humains.CauseDeMort.Meurtre => "sous les coups d'un autre",
        Humains.CauseDeMort.Noyade => "noyé",
        Humains.CauseDeMort.Brulure => "dans les flammes",
        Humains.CauseDeMort.Eboulement => "sous un éboulement",
        Humains.CauseDeMort.Eruption => "dans une éruption",
        Humains.CauseDeMort.Accident => "par accident",
        Humains.CauseDeMort.Chaleur => "de chaleur",
        Humains.CauseDeMort.Guerre => "au combat",
        Humains.CauseDeMort.Empoisonnement => "empoisonné par une plante",
        Humains.CauseDeMort.Envoutement => "rongé par une malédiction",
        _ => "",
    };

    /// <summary>État d'âme en un ou deux mots.</summary>
    public static string Humeur(Humains.Humain h) => h switch
    {
        { EstDeprime: true } => "déprimé" + (h.Sexe == Humains.Sexe.Femme ? "e" : ""),
        { Colere: > 60 } => h.Sexe == Humains.Sexe.Femme ? "furieuse" : "furieux",
        { Chagrin: > 30 } => "en deuil",
        { Humeur: > 75 } => h.Sexe == Humains.Sexe.Femme ? "joyeuse" : "joyeux",
        { Humeur: > 60 } => "content" + (h.Sexe == Humains.Sexe.Femme ? "e" : ""),
        { Humeur: > 40 } => "serein" + (h.Sexe == Humains.Sexe.Femme ? "e" : ""),
        { Humeur: > 25 } => "triste",
        _ => "abattu" + (h.Sexe == Humains.Sexe.Femme ? "e" : ""),
    };
}
