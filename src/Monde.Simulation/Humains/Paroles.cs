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
    // À une communauté, envers une autre (voir ParolesCollectives) : le groupe en débat avant d'agir.
    FaitesLaGuerre = 24,
    FaitesLaPaix = 25,
    AlliezVous = 26,
    AllezALaRencontre = 27,
    EnseignezVosSavoirs = 28,
    PortezMaParoleA = 29,
    PartagezAvec = 30,
    UnissezVous = 31,
    PartezVers = 32,
    ChassezDeLeursTerres = 33,
    Exterminez = 34,
    // Au Monde entier (0.37.1) : des commandements qui durent chez ceux qui les acceptent, et l'accueil d'une communauté.
    NeTuezPas = 35,
    PartagezTous = 36,
    PrenezSoinDesFaibles = 37,
    /// <summary>« Accueillez… » : une communauté invite l'autre à camper chez elle et ne la frappe pas tant qu'elle l'accueille.</summary>
    AccueillezLes = 38,
    // Demander un geste (0.42.8) : des actes de tous les jours, que l'humain fait tout de suite s'il le veut (voir Demandes).
    /// <summary>« Assieds-toi, repose-toi » : il s'assoit et se tient tranquille un moment.</summary>
    AssiedsToi = 39,
    /// <summary>« Prie » : il s'assoit ou s'agenouille, et prie (une vraie prière, visible parmi les prières).</summary>
    Prie = 40,
    /// <summary>« Dors » : il s'allonge ; il ne dort que s'il en a besoin.</summary>
    Dors = 41,
    /// <summary>« Mange » : il mange ce qu'il trouve autour de lui, ou part en chercher.</summary>
    Mange = 42,
    /// <summary>« Bois » : il va boire.</summary>
    Bois = 43,
    /// <summary>« Va chercher à manger » : cueillir, chasser ou pêcher, à sa manière, pour lui et les siens.</summary>
    VaChercherAManger = 44,
    /// <summary>« Va vers… » : il va rejoindre quelqu'un.</summary>
    VaVers = 45,
    /// <summary>« Veille le feu » : il s'assoit près du feu du camp et l'entretient.</summary>
    VeilleLeFeu = 46,
    /// <summary>« Parle à… » : il va voir quelqu'un et passe un moment avec lui.</summary>
    ParleA = 47,
    /// <summary>« Montre-lui ce que tu sais » : il enseigne un geste à quelqu'un.</summary>
    Enseigne = 48,
    /// <summary>« Chante » : il chante (et danse, s'il en a envie), seul ou devant ceux qui sont là.</summary>
    Chante = 49,
    /// <summary>« Cache-toi » : il va se mettre à l'abri.</summary>
    CacheToi = 50,
    /// <summary>« Jeûne » : il ne part pas chercher à manger pendant un jour ou deux, tant que sa faim le lui permet.</summary>
    Jeune = 51,
    /// <summary>
    /// « Transmets ce que je t'ai révélé à ta communauté » (0.44.11) : il va la raconter, l'un après l'autre, aux membres de sa
    /// communauté (à ses proches s'il n'en a pas) qui ne la savent pas encore.
    /// </summary>
    Transmets = 52,
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
/// <param name="Promesse">Il l'a promis lui-même à l'Entité au cours d'un dialogue (0.42.9) : il le tiendra ou non, selon sa conscience.</param>
public sealed record Impulsion(Parole Parole, ModeParole Mode, int CibleId, int Param, double Force, long Le, long Jusqua, long AgirLe,
    Reponse Reponse, long EvenementId, bool Orientation = false, bool Promesse = false)
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
        or Parole.Console or Parole.Pardonne or Parole.Ecoute or Parole.Frappe or Parole.Tue or Parole.VaVers or Parole.ParleA or Parole.Enseigne;

    /// <summary>Gestes de tous les jours qu'on lui demande de faire (0.42.8).</summary>
    public static bool Demande(Parole p) => p >= Parole.AssiedsToi && p <= Parole.Transmets;

    /// <summary>Gestes demandés qu'il commence à l'instant même s'il le veut, sur place (s'asseoir, prier, dormir, chanter, veiller le feu, jeûner).</summary>
    public static bool Immediat(Parole p) => p is Parole.AssiedsToi or Parole.Prie or Parole.Dors or Parole.Chante or Parole.VeilleLeFeu or Parole.Jeune;

    /// <summary>Gestes demandés qui l'occupent un moment sur place (une activité tenue jusqu'à ce que les règles la finissent).</summary>
    public static bool Occupe(Parole p) => p is Parole.AssiedsToi or Parole.Prie or Parole.Dors or Parole.VeilleLeFeu or Parole.Chante or Parole.CacheToi
        or Parole.Mange or Parole.Bois or Parole.VaChercherAManger;

    /// <summary>Paroles qui ordonnent de faire du mal.</summary>
    public static bool Nuit(Parole p) => p is Parole.Frappe or Parole.Tue;

    /// <summary>Paroles adressées à une communauté entière, que le groupe débat avant d'agir.</summary>
    public static bool Collective(Parole p) => p >= Parole.FaitesLaGuerre && p <= Parole.Exterminez || p == Parole.AccueillezLes;

    /// <summary>
    /// Paroles que l'Entité peut dire au Monde entier (ou à toute une terre, 0.37.1) : se révéler, avertir, rassurer, commander,
    /// désigner, et dresser tous les groupes contre une communauté, leur faire faire la paix avec elle ou l'accueillir.
    /// </summary>
    public static bool AuMonde(Parole p) => p is Parole.JeSuisCelleQuiVousAFaits or Parole.CatastropheArrive or Parole.PlanteDangereuse
        or Parole.NAiePasPeur or Parole.TuNEsPasSeul or Parole.NeTuezPas or Parole.NeTeVengePas or Parole.PartagezTous
        or Parole.NeVaPasALaGuerre or Parole.PrenezSoinDesFaibles or Parole.Ecoute or Parole.FaitesLaGuerre or Parole.FaitesLaPaix
        or Parole.AccueillezLes or Parole.VaVers;

    /// <summary>Commandements au Monde : une règle qui dure chez ceux qui l'acceptent, transmise aux enfants par ceux qui la racontent.</summary>
    public static bool Commandement(Parole p) => p is Parole.NeTuezPas or Parole.PartagezTous or Parole.PrenezSoinDesFaibles;

    /// <summary>Paroles collectives qui visent une autre communauté (toutes sauf « Partez vers… »).</summary>
    public static bool VeutCommunaute(Parole p) => Collective(p) && p != Parole.PartezVers;

    /// <summary>Paroles collectives qui ordonnent de nuire à une autre communauté.</summary>
    public static bool NuitAuxAutres(Parole p) => p is Parole.ChassezDeLeursTerres or Parole.Exterminez;

    /// <summary>Paroles collectives qui obligent le groupe à vivre près de l'autre (à portée de raid ou de rassemblement).</summary>
    public static bool Approcher(Parole p) => p is Parole.FaitesLaGuerre or Parole.AllezALaRencontre or Parole.ChassezDeLeursTerres or Parole.Exterminez;

    /// <summary>Paroles collectives de guerre.</summary>
    public static bool Guerriere(Parole p) => p is Parole.FaitesLaGuerre or Parole.ChassezDeLeursTerres or Parole.Exterminez;

    /// <summary>Les huit directions de « Pars vers… ».</summary>
    public static readonly string[] Directions = ["le nord", "le nord-est", "l'est", "le sud-est", "le sud", "le sud-ouest", "l'ouest", "le nord-ouest"];

    public static readonly (int Dx, int Dy)[] Vecteurs = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>
    /// La phrase telle que l'humain la reçoit (clé <c>phrase.*</c>). <paramref name="cible"/> est la personne désignée (ou null),
    /// <paramref name="extra"/> la direction, la plante ou la communauté ; <paramref name="feminin"/> : on s'adresse à une femme.
    /// </summary>
    /// <param name="monde">Dite au Monde entier : la phrase s'adresse à tous (« N'ayez pas peur », « Tous, faites la guerre à… »).</param>
    public static Recit Phrase(Parole p, Humain? cible, object? extra, bool feminin = false, bool monde = false) => p switch
    {
        _ when monde && Collective(p) => new Recit($"phrase.monde.{p}", ("c", extra)),
        _ when monde => new Recit($"phrase.monde.{p}", ("cible", cible), ("extra", extra)),
        Parole.NeVaPasALaGuerre when extra is not null => new Recit("phrase.NeVaPasALaGuerre.c", ("c", extra)),
        _ when Collective(p) => new Recit($"phrase.{p}.c", ("c", extra)),
        _ => new Recit($"phrase.{p}", ("cible", cible), ("extra", extra), ("g", feminin ? "f" : "m")),
    };
}
