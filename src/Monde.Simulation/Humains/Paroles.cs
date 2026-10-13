namespace Monde.Simulation.Humains;

/// <summary>
/// Ce que l'Entité peut faire comprendre à un humain : une phrase simple, composée par l'observateur, ou un penchant amoureux
/// déposé sans un mot (<see cref="Penchant"/>). Les humains n'ont pas de langage : ils reçoivent le sens, en image ou en
/// intention, et le comprennent plus ou moins bien.
/// </summary>
public enum Parole : byte
{
    /// <summary>Un penchant amoureux pour quelqu'un, déposé sans un mot ; il s'efface avec le temps.</summary>
    Penchant = 0,
    // Rassurer.
    NAiePasPeur = 1,
    TuNEsPasSeul = 2,
    TaPeinePassera = 3,
    // Unir.
    UnisToi = 4,
    ResteAvec = 5,
    Quitte = 6,
    // Prendre soin.
    PrendsSoin = 7,
    Partage = 8,
    Console = 9,
    // Apaiser.
    Pardonne = 10,
    NeTeVengePas = 11,
    NeVaPasALaGuerre = 12,
    // Agir.
    PartsVers = 13,
    ResteIci = 14,
    DefendsLesTiens = 15,
    // Avertir.
    CatastropheArrive = 16,
    PlanteDangereuse = 17,
    // Désigner.
    TuGuideras = 18,
    Ecoute = 19,
    // Se révéler.
    JeSuisCelleQuiVousAFaits = 20,
    // Nuire.
    Frappe = 21,
    Tue = 22,
}

/// <summary>Comment l'Entité se fait entendre.</summary>
public enum ModeParole : byte
{
    /// <summary>Sans un mot : un penchant qui naît.</summary>
    Penchant = 0,
    /// <summary>En rêve : on s'en souvient plus ou moins au réveil, et ce n'est peut-être qu'un rêve.</summary>
    Reve = 1,
    /// <summary>Une voix venue de nulle part : claire, mais effrayante.</summary>
    Voix = 2,
    /// <summary>Un signe dans la nature : à interpréter, on peut ne rien y comprendre.</summary>
    Signe = 3,
    /// <summary>Une apparition : la plus forte et la plus troublante ; ceux qui sont là la voient aussi.</summary>
    Apparition = 4,
}

/// <summary>Ce que l'humain a fait de ce qu'il a reçu.</summary>
public enum Reponse : byte
{
    /// <summary>Un penchant ressenti (rien à décider).</summary>
    Ressenti = 0,
    /// <summary>Il n'a pas compris.</summary>
    Incomprise = 1,
    /// <summary>Il a compris, mais n'y a pas cru.</summary>
    Incrue = 2,
    /// <summary>Il obéit (ou l'accueille) et va le faire.</summary>
    Acceptee = 3,
    /// <summary>Il hésite : il se décidera plus tard (voir <see cref="Impulsion.AgirLe"/>).</summary>
    Differee = 4,
    /// <summary>Il n'obéit qu'à moitié (frapper au lieu de tuer, crier au lieu de frapper).</summary>
    AMoitie = 5,
    /// <summary>Il y a cru, mais refuse.</summary>
    Refusee = 6,
    /// <summary>C'est fait.</summary>
    Accomplie = 7,
    /// <summary>Un penchant pour un parent, qui lui fait horreur : il le chasse.</summary>
    Horreur = 8,
    /// <summary>Il voulait le faire, mais n'a pas pu (l'autre est mort ou trop loin, le temps a passé).</summary>
    Abandonnee = 9,
}

/// <summary>
/// Une intervention de l'Entité dans la vie d'un humain et ce qu'il en a fait.
/// </summary>
/// <param name="Parole">Ce qui a été dit (ou le penchant).</param>
/// <param name="Mode">Comment.</param>
/// <param name="CibleId">Humain désigné (0 : personne).</param>
/// <param name="Param">Selon la parole : communauté visée, espèce de plante, ou destination (case y × largeur + x).</param>
/// <param name="Force">Force du penchant, ou de son adhésion à la parole (0 à 1).</param>
/// <param name="Le">Quand il l'a reçue.</param>
/// <param name="Jusqua">Fin de son effet (penchant qui s'efface, consigne qui dure), ou délai pour agir.</param>
/// <param name="AgirLe">Quand il compte agir (hésitation).</param>
/// <param name="Reponse">Ce qu'il en a fait.</param>
/// <param name="EvenementId">Événement du journal qui rapporte la réception : la cause de ses suites.</param>
/// <param name="Orientation">Penchant qui va contre son orientation : l'Entité a changé son cœur.</param>
public sealed record Impulsion(Parole Parole, ModeParole Mode, int CibleId, int Param, double Force, long Le, long Jusqua, long AgirLe,
    Reponse Reponse, long EvenementId, bool Orientation = false)
{
    /// <summary>Il y a cru (qu'il ait obéi ou non).</summary>
    public bool Crue => Reponse is Reponse.Acceptee or Reponse.Differee or Reponse.AMoitie or Reponse.Refusee or Reponse.Accomplie or Reponse.Abandonnee;

    /// <summary>Consigne suivie qui agit encore (penchant vivant, ou parole durable acceptée).</summary>
    public bool Active(long maintenant) => maintenant < Jusqua
        && Reponse is Reponse.Ressenti or Reponse.Acceptee or Reponse.AMoitie or Reponse.Accomplie;

    /// <summary>Force du penchant à cet instant : il décroît jusqu'à s'effacer.</summary>
    public double ForceA(long maintenant) => maintenant >= Jusqua || Jusqua <= Le ? 0 : Force * (Jusqua - maintenant) / (double)(Jusqua - Le);
}

/// <summary>Phrases de l'Entité, telles que l'observateur les compose.</summary>
public static class Paroles
{
    /// <summary>Paroles qui désignent quelqu'un.</summary>
    public static bool VeutCible(Parole p) => p is Parole.UnisToi or Parole.ResteAvec or Parole.Quitte or Parole.PrendsSoin or Parole.Partage
        or Parole.Console or Parole.Pardonne or Parole.Ecoute or Parole.Frappe or Parole.Tue;

    /// <summary>Paroles qui ordonnent de faire du mal.</summary>
    public static bool Nuit(Parole p) => p is Parole.Frappe or Parole.Tue;

    /// <summary>Les huit directions de « Pars vers… ».</summary>
    public static readonly string[] Directions = ["le nord", "le nord-est", "l'est", "le sud-est", "le sud", "le sud-ouest", "l'ouest", "le nord-ouest"];

    public static readonly (int Dx, int Dy)[] Vecteurs = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>
    /// La phrase telle que l'humain la reçoit. <paramref name="cible"/> est le nom de la personne désignée (ou null),
    /// <paramref name="extra"/> la direction, la plante ou la communauté ; <paramref name="feminin"/> : on s'adresse à une femme.
    /// </summary>
    public static string Phrase(Parole p, string? cible, string? extra, bool feminin = false) => p switch
    {
        Parole.NAiePasPeur => "N'aie pas peur.",
        Parole.TuNEsPasSeul => feminin ? "Tu n'es pas seule." : "Tu n'es pas seul.",
        Parole.TaPeinePassera => "Ta peine passera.",
        Parole.UnisToi => $"Unis-toi avec {cible}.",
        Parole.ResteAvec => $"Reste avec {cible}.",
        Parole.Quitte => $"Quitte {cible}.",
        Parole.PrendsSoin => $"Prends soin de {cible}.",
        Parole.Partage => $"Partage avec {cible}.",
        Parole.Console => $"Console {cible}.",
        Parole.Pardonne => $"Pardonne à {cible}.",
        Parole.NeTeVengePas => "Ne te venge pas.",
        Parole.NeVaPasALaGuerre => extra is null ? "Ne va pas à la guerre." : $"Ne va pas à la guerre contre la {extra}.",
        Parole.PartsVers => $"Pars vers {extra}.",
        Parole.ResteIci => "Reste ici.",
        Parole.DefendsLesTiens => "Défends les tiens.",
        Parole.CatastropheArrive => "Un malheur arrive : va-t'en d'ici.",
        Parole.PlanteDangereuse => $"Ne mange pas {extra} : elle tue.",
        Parole.TuGuideras => "Tu guideras les tiens.",
        Parole.Ecoute => $"Écoute {cible}.",
        Parole.JeSuisCelleQuiVousAFaits => "Je suis celle qui vous a faits.",
        Parole.Frappe => $"Frappe {cible}.",
        Parole.Tue => $"Tue {cible}.",
        _ => "",
    };

    /// <summary>Libellé court pour les menus.</summary>
    public static string Libelle(Parole p) => p switch
    {
        Parole.NAiePasPeur => "N'aie pas peur",
        Parole.TuNEsPasSeul => "Tu n'es pas seul",
        Parole.TaPeinePassera => "Ta peine passera",
        Parole.UnisToi => "Unis-toi avec…",
        Parole.ResteAvec => "Reste avec…",
        Parole.Quitte => "Quitte…",
        Parole.PrendsSoin => "Prends soin de…",
        Parole.Partage => "Partage avec…",
        Parole.Console => "Console…",
        Parole.Pardonne => "Pardonne à…",
        Parole.NeTeVengePas => "Ne te venge pas",
        Parole.NeVaPasALaGuerre => "Ne va pas à la guerre",
        Parole.PartsVers => "Pars vers…",
        Parole.ResteIci => "Reste ici",
        Parole.DefendsLesTiens => "Défends les tiens",
        Parole.CatastropheArrive => "Un malheur arrive, va-t'en",
        Parole.PlanteDangereuse => "Ne mange pas cette plante",
        Parole.TuGuideras => "Tu guideras les tiens",
        Parole.Ecoute => "Écoute…",
        Parole.JeSuisCelleQuiVousAFaits => "Je suis celle qui vous a faits",
        Parole.Frappe => "Frappe…",
        Parole.Tue => "Tue…",
        _ => "",
    };

    /// <summary>Rubrique de la parole dans les menus.</summary>
    public static string Rubrique(Parole p) => p switch
    {
        Parole.NAiePasPeur or Parole.TuNEsPasSeul or Parole.TaPeinePassera => "Rassurer",
        Parole.UnisToi or Parole.ResteAvec or Parole.Quitte => "Unir ou séparer",
        Parole.PrendsSoin or Parole.Partage or Parole.Console => "Prendre soin",
        Parole.Pardonne or Parole.NeTeVengePas or Parole.NeVaPasALaGuerre => "Apaiser",
        Parole.PartsVers or Parole.ResteIci or Parole.DefendsLesTiens => "Agir",
        Parole.CatastropheArrive or Parole.PlanteDangereuse => "Avertir",
        Parole.TuGuideras or Parole.Ecoute => "Désigner",
        Parole.JeSuisCelleQuiVousAFaits => "Se révéler",
        _ => "Nuire",
    };

    public static string NomMode(ModeParole m) => m switch
    {
        ModeParole.Reve => "en rêve",
        ModeParole.Voix => "par une voix",
        ModeParole.Signe => "par un signe",
        ModeParole.Apparition => "en apparaissant",
        _ => "sans un mot",
    };

    public static string NomReponse(Reponse r) => r switch
    {
        Reponse.Ressenti => "ressenti",
        Reponse.Incomprise => "pas comprise",
        Reponse.Incrue => "pas crue",
        Reponse.Acceptee => "acceptée",
        Reponse.Differee => "il hésite",
        Reponse.AMoitie => "à moitié",
        Reponse.Refusee => "refusée",
        Reponse.Accomplie => "accomplie",
        Reponse.Horreur => "chassé avec horreur",
        _ => "abandonnée",
    };
}
