using Monde.Simulation.Recits;

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
    // Désigner (suite).
    PorteMaParole = 23,
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
    /// La phrase telle que l'humain la reçoit (clé <c>phrase.*</c>). <paramref name="cible"/> est la personne désignée (ou null),
    /// <paramref name="extra"/> la direction, la plante ou la communauté ; <paramref name="feminin"/> : on s'adresse à une femme.
    /// </summary>
    public static Recit Phrase(Parole p, Humain? cible, object? extra, bool feminin = false) => p switch
    {
        Parole.NeVaPasALaGuerre when extra is not null => new Recit("phrase.NeVaPasALaGuerre.c", ("c", extra)),
        _ => new Recit($"phrase.{p}", ("cible", cible), ("extra", extra), ("g", feminin ? "f" : "m")),
    };
}
