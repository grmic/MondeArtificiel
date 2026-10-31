using Monde.Simulation.Monde;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'un humain peut tenir en main, trouver autour de lui ou fabriquer. Les matières brutes dépendent du terrain et
/// des roches de la région ; les produits naissent des gestes (voir <see cref="Physique"/>).
/// </summary>
public enum Matiere : byte
{
    Aucune = 0,
    /// <summary>Galet dur (quartzite, basalte) : il se casse mal, mais donne parfois un éclat.</summary>
    Galet = 1,
    /// <summary>Silex : se casse en éclats tranchants (fracture conchoïdale) et fait jaillir des étincelles contre la pyrite.</summary>
    Silex = 2,
    /// <summary>Pyrite (ou marcassite) : frappée par une pierre dure, elle donne des étincelles.</summary>
    Pyrite = 3,
    /// <summary>Verre volcanique : les éclats les plus tranchants qui soient.</summary>
    Obsidienne = 4,
    BoisSec = 5,
    Branche = 6,
    GrandesFeuilles = 7,
    /// <summary>Fibres végétales (lianes, roseaux, écorce).</summary>
    Fibres = 8,
    /// <summary>Herbe sèche, mousse, écorce fine : ce qui s'enflamme d'une étincelle.</summary>
    HerbeSeche = 9,
    /// <summary>Calebasse séchée, creuse.</summary>
    Calebasse = 10,
    Argile = 11,
    /// <summary>Une bête qu'on vient de tuer.</summary>
    Carcasse = 12,
    /// <summary>Une plante d'une espèce précise (voir <see cref="Flore"/>).</summary>
    Plante = 13,
    /// <summary>De l'eau, là où l'on peut en puiser.</summary>
    Eau = 14,
    /// <summary>Le feu : flammes d'un incendie ou d'un foyer, braise.</summary>
    Feu = 15,
    /// <summary>Un tronc tombé, ébranché : long, rond, lourd, et il flotte.</summary>
    Tronc = 16,
    /// <summary>Résine des conifères : chauffée, elle colle et durcit en refroidissant.</summary>
    Resine = 17,
    /// <summary>Terre rouge riche en fer (ocre) : écrasée, elle colore la peau et la roche.</summary>
    Ocre = 18,

    // Produits.
    Eclat = 20,
    BatonPointu = 21,
    /// <summary>Épieu : bâton pointu durci au feu.</summary>
    Epieu = 22,
    Corde = 23,
    Peau = 24,
    /// <summary>Brandon : branche dont le bout brûle, pour porter le feu.</summary>
    Brandon = 25,
    /// <summary>Rondelle tranchée dans un tronc : ronde et plate.</summary>
    Rondelle = 26,
    /// <summary>Rondelle percée en son centre.</summary>
    RondellePercee = 27,
    /// <summary>Objet fait de plusieurs pièces tenues ensemble (voir <see cref="Assemblage"/>).</summary>
    Assemblage = 28,
    /// <summary>Porte-feu : corde ou mèche de fibres qui couve sans flamber, et garde le feu des heures durant.</summary>
    PorteFeu = 29,
    /// <summary>Botte de roseaux (ou de joncs, de papyrus) serrés : légère, elle flotte longtemps avant de s'imbiber.</summary>
    Roseau = 30,

    // Mondes neufs depuis la 0.32.0 (voir Chimie).
    /// <summary>Un bloc de calcaire ou de craie.</summary>
    Calcaire = 31,
    Sable = 32,
    /// <summary>Coquilles de moules, d'huîtres, d'escargots.</summary>
    Coquillage = 33,
    /// <summary>Écorce arrachée à un arbre (bouleau, pin) : riche en bétuline ou en résine.</summary>
    Ecorce = 34,
    /// <summary>Un morceau détaché d'un gisement (sa composition est gardée avec l'objet).</summary>
    Minerai = 35,
    /// <summary>Une chose faite de main d'homme dont la matière a changé (pâte modelée, terre cuite, charbon, poix…) : sa composition est gardée avec l'objet.</summary>
    Substance = 36,
    /// <summary>Une chose faite de main d'homme par façons et jointures (voir <see cref="Facture"/>) : sa recette est gardée avec l'objet.</summary>
    Facture = 37,
}

/// <summary>Gestes à la portée de tout humain. Ce qu'ils produisent dépend de la matière, pas d'une découverte programmée.</summary>
public enum Geste : byte
{
    Frapper = 0,
    Frotter = 1,
    /// <summary>Racler, tailler, découper : la seconde matière sert d'outil.</summary>
    Gratter = 2,
    Tordre = 3,
    /// <summary>Entasser une matière sur une autre (bois sur le feu, feuilles sur des branches).</summary>
    Entasser = 4,
    /// <summary>Approcher une matière du feu.</summary>
    Approcher = 5,
    /// <summary>S'envelopper le corps d'une matière.</summary>
    SeCouvrir = 6,
    /// <summary>Remplir un objet creux d'eau.</summary>
    Remplir = 7,
    /// <summary>Creuser la terre avec un objet.</summary>
    Fouir = 8,
    Manger = 9,
    /// <summary>Appliquer une plante écrasée sur une plaie ou la peau.</summary>
    Appliquer = 10,
    /// <summary>Entrer dans l'eau profonde, s'y débattre, flotter.</summary>
    Nager = 11,
    /// <summary>Tenir plusieurs pièces ensemble par un lien (voir <see cref="Lois"/>).</summary>
    Assembler = 12,
    /// <summary>Pousser une chose sur le sol ou sur l'eau, et regarder ce qu'elle fait.</summary>
    Pousser = 13,
    /// <summary>Faire tourner une pointe dure contre du bois pour y faire un trou.</summary>
    Percer = 14,
    /// <summary>Entailler à grands coups (un tronc) avec un outil tranchant.</summary>
    Trancher = 15,
    /// <summary>Laver une plaie à l'eau, la presser et la couvrir de feuilles.</summary>
    Panser = 16,
    /// <summary>Laisser la voix monter et descendre : chanter.</summary>
    Chanter = 17,
    /// <summary>Bouger le corps en cadence.</summary>
    Danser = 18,
    /// <summary>Poser de l'ocre sur une paroi de roche.</summary>
    Peindre = 19,
    /// <summary>Se laver les mains à l'eau.</summary>
    Laver = 20,
    /// <summary>S'écarter du camp et de l'eau pour ses besoins, et les couvrir de terre.</summary>
    Enfouir = 21,
    /// <summary>Boire à l'eau qui court plutôt qu'à l'eau qui dort.</summary>
    Puiser = 22,
    /// <summary>Flairer la viande et jeter celle qui sent mauvais.</summary>
    Flairer = 23,
    /// <summary>Suspendre la viande dans la fumée d'un feu.</summary>
    Fumer = 24,
    /// <summary>Mettre le feu aux herbes sèches.</summary>
    Incendier = 25,
    /// <summary>Couvrir le feu de cendre pour la nuit.</summary>
    Couvrir = 26,
    /// <summary>Écarter l'herbe autour du foyer et l'entourer de pierres.</summary>
    Degager = 27,
    /// <summary>Éteindre le feu avec de l'eau ou de la terre.</summary>
    Eteindre = 28,
    /// <summary>Attendre que le vent tombe avant d'allumer.</summary>
    Patienter = 29,
    /// <summary>Cacher une réserve dans la terre ou sous des pierres.</summary>
    Cacher = 30,
    /// <summary>Mettre à sécher au soleil.</summary>
    Secher = 31,
    /// <summary>Amasser plus qu'on ne mange, pour plus tard.</summary>
    Amasser = 32,

    // Mondes neufs depuis la 0.32.0.
    /// <summary>Pétrir une pâte et lui donner une forme.</summary>
    Modeler = 33,
    /// <summary>Laisser longtemps une chose dans le feu, au bord, sous la braise ou dans une fosse sous le bois.</summary>
    Cuire = 34,
    /// <summary>Couvrir de terre un feu bien nourri : il couve sans air.</summary>
    Etouffer = 35,
    /// <summary>Souffler sur le feu, à la bouche ou par un roseau creux.</summary>
    Souffler = 36,
    /// <summary>Mêler deux matières (de la terre et de l'eau, du sable…).</summary>
    Melanger = 37,
    /// <summary>Jeter une pierre chauffée dans l'eau.</summary>
    Plonger = 38,
    /// <summary>Façonner une chose par une suite de gestes et de jointures (voir <see cref="Facture"/>).</summary>
    Faconner = 39,
    /// <summary>Regarder revenir une chose (la lune, les saisons, les étoiles) ou faire correspondre des choses une à une (depuis la 0.34.1).</summary>
    Observer = 40,
}

/// <summary>Ce que l'humain constate après un geste.</summary>
public enum Effet : byte
{
    Rien = 0,
    /// <summary>Un éclat tranchant se détache.</summary>
    Eclat = 1,
    Etincelle = 2,
    /// <summary>Une flamme naît.</summary>
    Flamme = 3,
    /// <summary>Le feu dure : on a un foyer.</summary>
    Foyer = 4,
    /// <summary>Le feu chauffe.</summary>
    Chaleur = 5,
    /// <summary>Le bois s'échauffe sous le frottement.</summary>
    Tiedeur = 6,
    Pointe = 7,
    /// <summary>La pointe passée au feu devient dure.</summary>
    Durci = 8,
    Corde = 9,
    /// <summary>Le corps couvert a moins froid.</summary>
    Couvert = 10,
    /// <summary>Un abri de branches et de feuilles protège.</summary>
    Abri = 11,
    /// <summary>L'eau voyage avec soi.</summary>
    EauPortee = 12,
    /// <summary>La terre fouillée donne des racines et des tubercules.</summary>
    Tubercules = 13,
    /// <summary>La viande passée au feu est meilleure.</summary>
    Cuisson = 14,
    /// <summary>L'éclat découpe la bête : plus de viande, et sa peau.</summary>
    Decoupe = 15,
    /// <summary>On flotte, on avance dans l'eau.</summary>
    Nage = 16,
    /// <summary>Le mal recule (ventre, fièvre, plaie, selon la croyance).</summary>
    Soulagement = 17,
    /// <summary>La plante rend malade.</summary>
    Poison = 18,
    /// <summary>La plante fait voir des choses étranges.</summary>
    Visions = 19,
    /// <summary>Le brandon porte le feu.</summary>
    Brandon = 20,
    /// <summary>La pointe ou l'épieu fait mieux chasser.</summary>
    Chasse = 21,
    /// <summary>Ce qui est rond roule quand on le pousse sur un sol dur.</summary>
    Roule = 22,
    /// <summary>Ce qui est plus léger que l'eau flotte.</summary>
    Flotte = 23,
    /// <summary>Une pierre taillée liée au bout d'un manche : elle perce plus loin et plus fort.</summary>
    PointeEmmanchee = 24,
    /// <summary>Une pierre tranchante liée en travers d'un manche : le coup porte plus fort.</summary>
    TranchantEmmanche = 25,
    /// <summary>Une charge posée sur des branches liées glisse quand on la tire.</summary>
    Glisser = 26,
    /// <summary>Une charge posée sur des troncs roule sur eux.</summary>
    Rouleaux = 27,
    /// <summary>Un tronc tranché donne des rondelles.</summary>
    Rondelle = 28,
    /// <summary>Une pointe dure qu'on fait tourner perce le bois.</summary>
    Percage = 29,
    /// <summary>Des rondelles percées tournent autour d'un bâton fixé sous la charge.</summary>
    RouleSurAxe = 30,
    /// <summary>Des pièces de bois liées portent un humain sur l'eau.</summary>
    Flottaison = 31,
    /// <summary>Une plaie lavée ou couverte guérit mieux et pourrit moins.</summary>
    Pansement = 32,
    /// <summary>Deux choses frappées en cadence font un rythme qui plaît.</summary>
    Rythme = 33,
    /// <summary>La voix qui monte et descend fait un chant.</summary>
    Chant = 34,
    /// <summary>Le corps qui bouge en cadence, avec d'autres : la danse.</summary>
    Danse = 35,
    /// <summary>L'ocre écrasée colore la peau : on s'en pare.</summary>
    Ocre = 36,
    /// <summary>L'ocre posée sur la roche y reste : une image.</summary>
    Peinture = 37,
    /// <summary>Des mains lavées avant de manger donnent moins de maux de ventre.</summary>
    MainsPropres = 38,
    /// <summary>L'eau qui court rend moins malade que l'eau qui dort.</summary>
    EauVive = 39,
    /// <summary>Faire ses besoins loin du camp et les couvrir épargne les maux de ventre à tous.</summary>
    Latrines = 40,
    /// <summary>La viande qui sent mauvais rend malade : on la jette.</summary>
    ViandeSaine = 41,
    /// <summary>La viande passée longtemps dans la fumée se garde des semaines.</summary>
    Fumage = 42,
    /// <summary>La fumée du feu chasse les moustiques.</summary>
    Fumee = 43,
    /// <summary>Devant le feu, les bêtes fuient et se découvrent : on les chasse mieux là où l'herbe brûle.</summary>
    Brulis = 44,
    /// <summary>Le feu couvert de cendre garde sa braise jusqu'au matin.</summary>
    Braises = 45,
    /// <summary>Une corde approchée du feu couve sans flamber : on emporte le feu pour la journée.</summary>
    PorteFeu = 46,
    /// <summary>Un foyer dégagé et entouré de pierres ne gagne pas les herbes.</summary>
    FoyerDegage = 47,
    /// <summary>Le feu allumé dans le vent, sur l'herbe sèche, s'échappe.</summary>
    PasDansLeVent = 48,
    /// <summary>Un feu qu'on laisse vif en partant peut gagner les herbes : on l'éteint.</summary>
    EteindreEnPartant = 49,
    /// <summary>Brûler un peu autour du camp à la saison humide arrête les grands feux de la saison sèche.</summary>
    BrulisProtecteur = 50,
    /// <summary>Une calebasse ou une outre bouchée, enterrée pleine, garde l'eau pour la saison sèche.</summary>
    CacheDEau = 51,
    /// <summary>La viande en lanières et les fruits séchés au soleil se gardent des mois.</summary>
    Sechage = 52,
    /// <summary>Les noix et les graines amassées en automne se mangent encore en hiver.</summary>
    Provisions = 53,
    /// <summary>Dans le froid, sous des pierres ou dans une fosse, la viande ne pourrit pas.</summary>
    CacheFroide = 54,
    /// <summary>Un bateau qui tient la mer : il porte sa charge et ne se défait pas dans les vagues.</summary>
    Navigation = 55,
    /// <summary>Une branche plate poussée contre l'eau fait avancer plus vite que les mains.</summary>
    Pagaie = 56,
    /// <summary>La matière a changé (au feu, sous les coups, dans l'eau) : ce qu'elle est devenue est dit par ses pièces.</summary>
    Transformation = 57,
    /// <summary>Une pâte prend la forme qu'on lui donne.</summary>
    Modelage = 58,
    /// <summary>Là où l'eau d'une crue est passée, tout repousse plus fort (depuis la 0.32.7).</summary>
    Limon = 59,
    /// <summary>Ce qu'on jette ou qu'on enfouit (graines, noyaux, bouts de racine) repousse près du camp (depuis la 0.32.8).</summary>
    Repousse = 60,
    /// <summary>Des graines ou des racines de cette espèce, mises en terre, lèvent et donnent la plante (depuis la 0.33.0).</summary>
    Semis = 61,
    /// <summary>Arracher les herbes autour des semis les fait mieux pousser.</summary>
    Sarclage = 62,
    /// <summary>Une façon de récolter les graines : secouer les épis, les arracher, couper les tiges.</summary>
    Moisson = 63,
    /// <summary>Garder une part de la récolte pour la semer l'an prochain.</summary>
    Semence = 64,
    /// <summary>Ce qu'on sème sur une terre brûlée pousse plus fort, quelques saisons.</summary>
    Essart = 65,
    /// <summary>Le petit d'une bête, nourri au camp, grandit sans fuir l'homme (depuis la 0.33.1).</summary>
    Apprivoiser = 66,
    /// <summary>Les femelles qui allaitent se laissent prendre leur lait.</summary>
    Traite = 67,
    /// <summary>Une chose façonnée qui sert à un besoin (sa recette est dans le savoir : voir <see cref="Facture"/>).</summary>
    Facture = 68,
    /// <summary>Une propriété remarquée d'une matière (« la branche verte plie et revient ») : elle donne des idées.</summary>
    Remarque = 69,
    /// <summary>
    /// Une régularité lue dans une série (la lune revient pleine, les saisons reviennent, les étoiles tournent, les choses se
    /// comptent) : de quoi prévoir. Sa série et ce qu'il en estime sont dans ses pièces (voir <see cref="Comportement.LireRegularite"/>).
    /// </summary>
    Regularite = 70,
}

/// <summary>Le mal tel que l'humain le ressent : il ne connaît pas les maladies, il en voit les signes.</summary>
public enum Symptome : byte
{
    Aucun = 0,
    Ventre = 1,
    Fievre = 2,
    Plaie = 3,
    Peau = 4,
}

/// <summary>D'où vient un savoir.</summary>
public enum OrigineSavoir : byte
{
    /// <summary>Un essai fait par jeu ou par curiosité.</summary>
    Essai = 0,
    /// <summary>Ce qu'il a vu faire la nature (un incendie, une bête brûlée).</summary>
    Observation = 1,
    /// <summary>Un rapprochement entre deux choses qu'il savait déjà.</summary>
    Reflexion = 2,
    /// <summary>Appris en regardant quelqu'un faire.</summary>
    Imitation = 3,
    /// <summary>Une intuition soufflée par l'Entité.</summary>
    Entite = 4,
}

/// <summary>
/// Un savoir : « ce geste, avec ces matières, produit cet effet ». C'est une croyance, juste ou fausse :
/// elle se renforce quand le geste réussit et s'affaiblit quand il échoue. Une plante crue soulager la fièvre
/// peut n'y être pour rien (le malade aurait guéri seul) : c'est ainsi que naissent les remèdes, vrais ou faux.
/// </summary>
/// <param name="Espece">Espèce de plante concernée (0 si aucune).</param>
/// <param name="Symptome">Mal que la plante est censée soulager (remèdes).</param>
/// <param name="Essais">Fois où il a tenté le geste en en attendant l'effet.</param>
/// <param name="Succes">Fois où l'effet est venu.</param>
/// <param name="MaitreId">Humain qui le lui a appris (imitation), 0 sinon.</param>
/// <param name="EvenementId">Événement du journal où il l'a appris.</param>
/// <param name="Pieces">Pour un objet assemblé : ses pièces et son lien (voir <see cref="Assemblage"/>), vide sinon.</param>
public sealed record Savoir(Geste Geste, Matiere A, Matiere B, Effet Effet, int Espece, Symptome Symptome, int Essais, int Succes,
    long AppriLe, long UtiliseLe, OrigineSavoir Origine, int MaitreId, long EvenementId, string Pieces = "")
{
    /// <summary>Confiance de l'humain dans ce savoir (règle de succession de Laplace) : une seule réussite ne prouve pas grand-chose.</summary>
    public double Confiance => (Succes + 1.0) / (Essais + 2.0);

    /// <summary>Même savoir (même geste, mêmes matières, même effet attendu).</summary>
    public bool Meme(Geste g, Matiere a, Matiere b, Effet e, int espece, Symptome s) =>
        Geste == g && A == a && B == b && Effet == e && Espece == espece && Symptome == s;
}

/// <summary>Un objet porté : sa matière, sa qualité (0 à 1) et son usure (1 : neuf, 0 : hors d'usage).</summary>
/// <param name="Plein">Pour une calebasse : remplie d'eau. Pour de la viande : fumée, elle se garde des semaines.</param>
/// <param name="Pieces">Pour un objet assemblé : ses pièces et son lien (voir <see cref="Assemblage"/>).</param>
public sealed record Objet(Matiere Matiere, double Qualite, double Usure, bool Plein = false, string? Pieces = null);

/// <summary>Ce qui accompagne un geste : le feu, l'eau, la saison sèche, l'adresse et l'intelligence de celui qui le fait.</summary>
/// <param name="Sec">Herbe et bois assez secs pour prendre feu (0 à 1).</param>
/// <param name="FeuxReussis">Feux que celui qui le fait a déjà obtenus par ce geste : on refait bien mieux ce qu'on sait faire.</param>
/// <param name="Coupe">Force à la coupe du meilleur outil qu'il a en main (voir <see cref="Lois.Coupe"/>).</param>
/// <param name="Art">Aptitude artistique de celui qui fait le geste (0 à 1) : l'oreille, l'œil.</param>
public readonly record struct ContexteGeste(bool FeuProche, bool EauProche, double Sec, Biome Biome, double Adresse, double Ingeniosite,
    int FeuxReussis = 0, double Coupe = 0, double Art = 0);

/// <summary>Résultat d'un geste : l'effet constaté, le produit éventuel et sa qualité.</summary>
public readonly record struct Resultat(Effet Effet, Matiere Produit, double Qualite);

/// <summary>
/// Les lois de la matière, valables pour tous : ce que donne chaque geste sur chaque matière, et avec quelle chance
/// selon l'adresse. Ce n'est pas une liste de découvertes : rien n'oblige un humain à faire tel geste, à le refaire,
/// ni à comprendre ce qu'il a vu. Une découverte naît quand quelqu'un fait le bon geste avec les bonnes matières
/// au bon moment, s'en aperçoit, et en a l'usage.
/// <para>
/// Ordres de grandeur. Le silex taillé donne des éclats à presque chaque coup d'une main exercée ; un galet de quartzite,
/// bien plus rarement. Silex contre pyrite fait jaillir une étincelle à presque chaque coup, mais l'amadou ou l'herbe
/// sèche ne s'enflamme qu'à quelques coups sur cent (la percussion est attestée au Paléolithique moyen ; Sorensen et al.,
/// 2018). Faire du feu par friction demande une grande adresse et beaucoup d'efforts : un débutant n'y arrive presque
/// jamais.
/// </para>
/// </summary>
/// <summary>
/// Forme d'un geste que l'Entité peut souffler : le verbe, le mot qui relie les deux matières (null : une seule), et les
/// matières qui ont un sens pour ce geste (rien n'y garantit un résultat : les lois en décident).
/// </summary>
public sealed record FormeGeste(Geste Geste, string Verbe, string? Liaison, Matiere[] A, Matiere[] B);

public static class Physique
{
    private static readonly Matiere[] Pierres = [Matiere.Galet, Matiere.Silex, Matiere.Pyrite, Matiere.Obsidienne];

    /// <summary>Gestes que l'Entité peut souffler, avec les matières qui leur conviennent.</summary>
    public static IReadOnlyList<FormeGeste> Formes { get; } =
    [
        new(Geste.Frapper, "frapper", "contre", [.. Pierres, Matiere.BoisSec, Matiere.Branche], [.. Pierres, Matiere.BoisSec, Matiere.Branche]),
        new(Geste.Frotter, "frotter", "contre", [Matiere.BoisSec, Matiere.Branche, .. Pierres], [Matiere.BoisSec, Matiere.Branche, .. Pierres]),
        new(Geste.Gratter, "racler", "avec", [Matiere.Branche, Matiere.BoisSec, Matiere.Carcasse, Matiere.Peau, Matiere.Calebasse],
            [Matiere.Eclat, Matiere.Obsidienne, Matiere.Silex, Matiere.Galet]),
        new(Geste.Tordre, "tordre", null, [Matiere.Fibres, Matiere.HerbeSeche, Matiere.GrandesFeuilles, Matiere.Branche, Matiere.Peau], []),
        new(Geste.Entasser, "entasser", "sur", [Matiere.BoisSec, Matiere.Branche, Matiere.GrandesFeuilles, Matiere.HerbeSeche],
            [Matiere.Feu, Matiere.Brandon, Matiere.PorteFeu, Matiere.Branche, Matiere.Galet]),
        new(Geste.Approcher, "approcher", "de", [Matiere.Aucune, Matiere.BoisSec, Matiere.Branche, Matiere.BatonPointu, Matiere.Carcasse, Matiere.Argile,
            Matiere.Fibres, Matiere.Corde], [Matiere.Feu]),
        new(Geste.SeCouvrir, "se couvrir de", null, [Matiere.Peau, Matiere.GrandesFeuilles, Matiere.HerbeSeche, Matiere.Fibres, Matiere.Ocre], []),
        new(Geste.Remplir, "remplir", "de", [Matiere.Calebasse, Matiere.Peau, Matiere.Argile], [Matiere.Eau]),
        new(Geste.Fouir, "fouir la terre avec", null, [Matiere.Branche, Matiere.BoisSec, Matiere.BatonPointu, Matiere.Epieu, Matiere.Galet, Matiere.Eclat], []),
        new(Geste.Manger, "manger", null, [Matiere.Plante], []),
        new(Geste.Appliquer, "appliquer sur son mal", null, [Matiere.Plante], []),
        new(Geste.Nager, "nager dans", null, [Matiere.Eau], []),
        new(Geste.Pousser, "pousser", "sur", [Matiere.Tronc, Matiere.Galet, Matiere.Branche, Matiere.Rondelle, Matiere.BoisSec],
            [Matiere.Aucune, Matiere.Eau]),
        new(Geste.Percer, "percer", "en y faisant tourner", [Matiere.Rondelle, Matiere.Branche, Matiere.BoisSec, Matiere.Calebasse],
            [Matiere.Eclat, Matiere.Silex, Matiere.Obsidienne, Matiere.BatonPointu, Matiere.BoisSec]),
        new(Geste.Trancher, "trancher", "à grands coups de", [Matiere.Tronc, Matiere.Branche],
            [Matiere.Assemblage, Matiere.Eclat, Matiere.Obsidienne, Matiere.Silex, Matiere.Galet]),
    ];

    /// <summary>Vrai si ces matières ont un sens pour ce geste (une seule matière : la seconde est « rien »).</summary>
    public static bool Convient(Geste g, Matiere a, Matiere b) =>
        Formes.FirstOrDefault(f => f.Geste == g) is { } f && f.A.Contains(a) && (f.B.Length == 0 ? b == Matiere.Aucune : f.B.Contains(b));

    /// <summary>Libellé de la seconde matière quand elle est « rien » mais a un sens (pousser… sur le sol).</summary>
    public static string? SansSeconde(Geste g) => g == Geste.Pousser ? "le sol" : null;

    public static bool EstDure(Matiere m) => m is Matiere.Galet or Matiere.Silex or Matiere.Obsidienne;
    public static bool EstTranchante(Matiere m) => m is Matiere.Eclat or Matiere.Obsidienne;
    public static bool EstBois(Matiere m) => m is Matiere.BoisSec or Matiere.Branche;

    /// <summary>Ce qui sonne quand on le frappe : bois, pierre, calebasse.</summary>
    public static bool Sonore(Matiere m) => EstBois(m) || EstDure(m) || m is Matiere.Calebasse or Matiere.Tronc;

    /// <summary>Peut-on porter cette matière sur soi ?</summary>
    public static bool Portable(Matiere m) => m is not (Matiere.Aucune or Matiere.Eau or Matiere.Feu or Matiere.Carcasse or Matiere.Plante or Matiere.Argile
        or Matiere.Tronc or Matiere.Sable);

    public static Resultat Tenter(Geste g, Matiere a, Matiere b, ContexteGeste c, Alea alea)
    {
        var h = c.Adresse;
        // Qui sait déjà faire le feu choisit le bois mort sur pied et garde son amadou au sec : il n'attend pas la sécheresse.
        if (c.FeuxReussis > 0) c = c with { Sec = Math.Max(c.Sec, 0.6) };
        bool Chance(double p) => alea.Reel() < p;
        switch (g)
        {
            case Geste.Frapper:
                // Silex contre pyrite (ou l'inverse) : étincelles, et parfois le feu si de l'herbe sèche est là.
                if ((a, b) is (Matiere.Silex or Matiere.Obsidienne, Matiere.Pyrite) or (Matiere.Pyrite, Matiere.Silex or Matiere.Obsidienne or Matiere.Galet)
                    or (Matiere.Galet, Matiere.Pyrite))
                {
                    var etincelle = (a == Matiere.Galet || b == Matiere.Galet ? 0.2 : 0.5) + 0.3 * h;
                    if (!Chance(etincelle)) return default;
                    // Une étincelle n'enflamme que l'amadou bien sec, et seulement si l'on souffle dessus au bon moment.
                    // Qui l'a déjà fait sait préparer l'amadou et souffler : il y arrive bien plus souvent.
                    var amadou = c.FeuxReussis > 0 ? 0.25 : 0.03;
                    if (c.Sec > 0.3 && Chance(amadou * c.Sec * (0.5 + h) * (0.5 + c.Ingeniosite))) return new(Effet.Flamme, Matiere.Feu, 1);
                    return new(Effet.Etincelle, Matiere.Aucune, 0);
                }
                // Pierre dure contre pierre dure : un éclat, s'il s'agit de silex ou d'obsidienne surtout.
                if (EstDure(a) && EstDure(b))
                {
                    var fine = a is Matiere.Silex or Matiere.Obsidienne || b is Matiere.Silex or Matiere.Obsidienne;
                    var p = fine ? 0.3 + 0.45 * h : 0.06 + 0.15 * h;
                    if (Chance(p))
                    {
                        var qualite = Math.Clamp((fine ? 0.5 : 0.25) + 0.4 * h * alea.Reel() + (a == Matiere.Obsidienne || b == Matiere.Obsidienne ? 0.2 : 0), 0, 1);
                        return new(Effet.Eclat, Matiere.Eclat, qualite);
                    }
                }
                // Deux choses dures frappées l'une contre l'autre, encore et encore : certains y entendent une cadence qui plaît,
                // d'autant plus qu'ils ont l'oreille (les plus anciennes flûtes ont 40 000 ans, les percussions sans doute bien plus :
                // Conard et al., 2009).
                if (Sonore(a) && Sonore(b) && Chance(0.001 + 0.02 * c.Art * c.Art)) return new(Effet.Rythme, Matiere.Aucune, c.Art);
                return default;

            case Geste.Frotter:
                if (EstBois(a) && EstBois(b))
                {
                    // Le bois s'échauffe toujours un peu ; la braise ne vient qu'aux plus adroits, par temps sec, au prix de longs efforts.
                    // Le premier feu par friction est une trouvaille rare ; celui qui sait s'y prendre (bois, appui, rythme) l'obtient
                    // le plus souvent en quelques minutes d'effort.
                    var friction = c.FeuxReussis > 0 ? 0.12 * (0.5 + h) * (0.5 + c.Ingeniosite) : 0.004 * h * h * (0.5 + c.Ingeniosite);
                    if (a == Matiere.BoisSec && b == Matiere.BoisSec && c.Sec > 0.4 && Chance(friction * c.Sec))
                        return new(Effet.Flamme, Matiere.Feu, 1);
                    return Chance(0.6) ? new(Effet.Tiedeur, Matiere.Aucune, 0) : default;
                }
                return default;

            case Geste.Gratter:
                if (a == Matiere.Branche && (EstTranchante(b) || b == Matiere.Silex))
                    return Chance(0.35 + 0.45 * h) ? new(Effet.Pointe, Matiere.BatonPointu, Math.Clamp(0.4 + 0.5 * h * alea.Reel(), 0, 1)) : default;
                if (a == Matiere.Carcasse && EstTranchante(b))
                    return Chance(0.3 + 0.5 * h) ? new(Effet.Decoupe, Matiere.Peau, Math.Clamp(0.3 + 0.6 * h * alea.Reel(), 0, 1)) : default;
                return default;

            case Geste.Tordre:
                return a == Matiere.Fibres && Chance(0.2 + 0.5 * h) ? new(Effet.Corde, Matiere.Corde, Math.Clamp(0.3 + 0.6 * h * alea.Reel(), 0, 1)) : default;

            case Geste.Entasser:
                // Du bois sur le feu : le feu dure.
                if (EstBois(a) && b is Matiere.Feu or Matiere.Brandon or Matiere.PorteFeu && c.FeuProche)
                    return Chance(0.6 + 0.3 * h) ? new(Effet.Foyer, Matiere.Feu, 1) : default;
                // Des feuilles sur des branches dressées : un abri.
                if (a == Matiere.GrandesFeuilles && b == Matiere.Branche)
                    return Chance(0.25 + 0.4 * h) ? new(Effet.Abri, Matiere.Aucune, Math.Clamp(0.3 + 0.6 * h, 0, 1)) : default;
                return default;

            case Geste.Approcher:
                if (!c.FeuProche) return default;
                return a switch
                {
                    Matiere.BoisSec or Matiere.Branche => new(Effet.Brandon, Matiere.Brandon, 1),
                    Matiere.BatonPointu => Chance(0.4 + 0.4 * h) ? new(Effet.Durci, Matiere.Epieu, Math.Clamp(0.5 + 0.4 * h, 0, 1)) : default,
                    // Une corde serrée ou des fibres tordues ne flambent pas : leur bout rougit et couve, des heures durant
                    // (mèches à feu des Andamanais et des Aborigènes ; les plus serrées tiennent une journée).
                    Matiere.Corde or Matiere.Fibres => Chance(0.3 + 0.5 * h)
                        ? new(Effet.PorteFeu, Matiere.PorteFeu, Math.Clamp((a == Matiere.Corde ? 0.4 : 0.15) + 0.5 * h * alea.Reel(), 0, 1)) : default,
                    Matiere.Carcasse => new(Effet.Cuisson, Matiere.Aucune, 1),
                    // On sent la chaleur du feu dès qu'on s'en approche.
                    Matiere.Aucune => new(Effet.Chaleur, Matiere.Aucune, 1),
                    _ => default,
                };

            case Geste.SeCouvrir:
                return a switch
                {
                    Matiere.Peau => new(Effet.Couvert, Matiere.Aucune, 0.6),
                    Matiere.GrandesFeuilles => new(Effet.Couvert, Matiere.Aucune, 0.2),
                    // L'ocre écrasée teint la peau de rouge (son usage remonte à 300 000 ans au moins : Watts et al., 2016).
                    Matiere.Ocre => new(Effet.Ocre, Matiere.Aucune, 1),
                    _ => default,
                };

            case Geste.Remplir:
                // Une calebasse garde l'eau ; une peau nouée en outre aussi, si on la tient bien fermée.
                if (!c.EauProche) return default;
                if (a == Matiere.Calebasse) return new(Effet.EauPortee, Matiere.Calebasse, 1);
                return a == Matiere.Peau && Chance(0.3 + 0.5 * h) ? new(Effet.EauPortee, Matiere.Peau, 0.7) : default;

            case Geste.Fouir:
                if (!(EstBois(a) || a is Matiere.BatonPointu or Matiere.Epieu)) return default;
                var terre = c.Biome switch
                {
                    Biome.PlaineDeFougeres or Biome.ForetTropicale or Biome.Marais or Biome.ForetDeFeuillus => 1.0,
                    Biome.ForetDeConiferes or Biome.Plage or Biome.Lande => 0.5,
                    Biome.Desert or Biome.Mangrove => 0.4,
                    _ => 0.1,
                };
                return Chance(terre * (0.25 + 0.35 * h + (a == Matiere.BatonPointu ? 0.1 : 0))) ? new(Effet.Tubercules, Matiere.Aucune, 1) : default;

            case Geste.Nager:
                return Chance(0.15 + 0.4 * h) ? new(Effet.Nage, Matiere.Aucune, 1) : default;

            case Geste.Pousser:
                // Sur l'eau, ce qui est moins dense qu'elle flotte (Archimède) ; sur un sol dur, ce qui est rond roule.
                var pr = Lois.De(a);
                if (b == Matiere.Eau)
                    return c.EauProche && pr.Densite < 1 ? new(Effet.Flotte, Matiere.Aucune, 1) : default;
                var sol = Lois.Sol(c.Biome);
                return (pr.Forme & Forme.Rond) != 0 && Chance(Math.Clamp(1 - 4 * sol, 0.05, 0.9)) ? new(Effet.Roule, Matiere.Aucune, 1) : default;

            case Geste.Percer:
            {
                // Une pointe plus dure que le bois, qu'on fait tourner longtemps, use un trou ; un bâton contre du bois
                // n'y arrive qu'en le brûlant, très lentement (c'est le geste du feu par friction).
                var bois = Lois.De(a);
                var meche = Lois.De(b);
                if (bois.Durete > 0.35 || a is Matiere.Aucune) return default;
                var dur = meche.Durete >= bois.Durete + 0.2 && (meche.Forme & Forme.Pointu) != 0;
                var p = dur ? 0.15 + 0.45 * h : EstBois(b) || b == Matiere.BatonPointu ? 0.02 * h : 0;
                if (!Chance(p * (0.6 + 0.4 * c.Ingeniosite))) return default;
                return new(Effet.Percage, a == Matiere.Rondelle ? Matiere.RondellePercee : Matiere.Aucune, Math.Clamp(0.4 + 0.5 * h * alea.Reel(), 0, 1));
            }

            case Geste.Trancher:
            {
                // Abattre et trancher un tronc demande un outil lourd et tranchant : à la main, un éclat n'en vient à bout
                // qu'au prix d'un jour d'efforts ; une hache de pierre le fait en une heure ou deux.
                if (a != Matiere.Tronc) return default;
                var coupe = b == Matiere.Assemblage ? c.Coupe : (Lois.De(b).Forme & Forme.Tranchant) != 0 ? 0.08 : EstDure(b) ? 0.03 : 0;
                if (!Chance(coupe * (0.4 + 0.6 * h))) return default;
                return new(Effet.Rondelle, Matiere.Rondelle, Math.Clamp(0.3 + 0.4 * coupe + 0.3 * h * alea.Reel(), 0, 1));
            }
        }
        return default;
    }

    /// <summary>Rapprochements que fait un esprit humain : une étincelle ressemble à une petite flamme, une pointe à une dent.</summary>
    public static bool Ressemble(Effet connu, Effet voulu) => (connu, voulu) switch
    {
        (Effet.Etincelle, Effet.Flamme) or (Effet.Tiedeur, Effet.Flamme) => true,
        (Effet.Eclat, Effet.Pointe) or (Effet.Eclat, Effet.Decoupe) => true,
        (Effet.Pointe, Effet.Chasse) or (Effet.Durci, Effet.Chasse) => true,
        (Effet.Chaleur, Effet.Foyer) or (Effet.Brandon, Effet.Foyer) => true,
        (Effet.Brandon, Effet.PorteFeu) or (Effet.Fumage, Effet.Sechage) => true,
        _ => false,
    };

    private static Recit Seul(string cle) => Recits.Recit.Seul(cle);

    private static Recit[] Noms<T>(Func<T, string> cle) where T : struct, Enum
    {
        var valeurs = Enum.GetValues<T>();
        var noms = new Recit[valeurs.Max(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)) + 1];
        foreach (var v in valeurs) noms[Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)] = new Recit(cle(v));
        return noms;
    }

    private static readonly Recit[] NomsEffets = Noms<Effet>(e => $"effet.{e}");
    private static readonly Recit[] NomsGestes = Noms<Geste>(g => $"geste.nom.{g}");
    private static readonly Recit[] NomsMatieres = Noms<Matiere>(m => $"matiere.{m}");
    private static readonly Recit[] Verbes = Noms<Geste>(g => $"forme.{g}");
    private static readonly Recit[] Liaisons = Noms<Geste>(g => $"forme.{g}.liaison");

    /// <summary>Nom court d'un effet, pour la chronologie des découvertes.</summary>
    public static Recit NomEffet(Effet e) => NomsEffets[(int)e];

    /// <summary>Nom d'une découverte dans la chronologie : une seule roue et plusieurs roues sont deux trouvailles.</summary>
    public static Recit NomDecouverte(Effet e, string? pieces) => e == Effet.RouleSurAxe && Lois.Structure(pieces) == "axe:1"
        ? Seul("decouverte.brouette")
        : e == Effet.Transformation && Comportement.LireRecette(pieces) is { } r
            ? new Recit("decouverte.Transformation", ("a", MatiereDeRecette(r.A, r.Famille)), ("chg", Chimie.Perception.Dire(r.Changements)))
        : e == Effet.Facture && Facons.LireObjet(pieces) is { } f ? Facons.Decrire(f.Facture)
        : e == Effet.Remarque && Facons.LireRemarque(pieces) is { } rq ? Seul($"decouverte.Remarque.{rq}")
        : e == Effet.Regularite && Comportement.LireRegularite(pieces) is { } rg ? Seul($"decouverte.Regularite.{rg.Serie}")
        : NomEffet(e);

    /// <summary>Le geste et ses matières, en mots (« pousser un tronc sur l'eau »).</summary>
    public static Recit Phrase(Geste g, Matiere a, Matiere b)
    {
        var f = Formes.FirstOrDefault(x => x.Geste == g);
        var verbe = f is null ? NomGeste(g) : Verbes[(int)g];
        if (b == Matiere.Aucune)
            return SansSeconde(g) is not null
                ? new Recit("geste.phrase.sol", ("verbe", verbe), ("a", Nom(a)), ("sol", Seul("intuition.leSol")))
                : new Recit("geste.phrase.un", ("verbe", verbe), ("a", Nom(a)));
        return new Recit("geste.phrase.deux", ("verbe", verbe), ("a", a == Matiere.Aucune ? Seul("intuition.sesMains") : Nom(a)),
            ("liaison", f?.Liaison is null ? Seul("geste.et") : Liaisons[(int)g]), ("b", b == Matiere.Assemblage ? Seul("intuition.outilEmmanche") : Nom(b)));
    }

    public static Recit NomGeste(Geste g) => NomsGestes[(int)g];

    public static Recit Nom(Matiere m) => NomsMatieres[(int)m];

    /// <summary>Nom d'un objet porté : un objet assemblé est décrit par ses pièces.</summary>
    public static Recit Nom(Objet o) => o.Matiere == Matiere.Assemblage && Assemblage.Lire(o.Pieces) is { } a ? Lois.Decrire(a)
        : o.Matiere is Matiere.Substance or Matiere.Minerai && Chimie.Substance.Lire(o.Pieces) is { } s ? Chimie.Perception.Nom(s)
        : o.Matiere == Matiere.Facture && Facons.LireObjet(o.Pieces) is { } f ? Facons.Decrire(f.Facture)
        : Nom(o.Matiere);

    /// <summary>Le savoir en quelques mots, tel que l'humain se le dit.</summary>
    public static Recit Decrire(Savoir s, object? plante = null) => s.Effet switch
    {
        Effet.Eclat or Effet.Etincelle or Effet.Rythme => new Recit($"savoir.{s.Effet}", ("a", Nom(s.A)), ("b", Nom(s.B))),
        Effet.Flamme when s.Geste == Geste.Frotter => Seul("savoir.Flamme.frotter"),
        Effet.Flamme => new Recit("savoir.Flamme", ("a", Nom(s.A)), ("b", Nom(s.B))),
        Effet.Pointe => new Recit("savoir.Pointe", ("b", Nom(s.B))),
        Effet.Couvert => Seul(s.A == Matiere.Peau ? "savoir.Couvert.peau" : "savoir.Couvert"),
        Effet.EauPortee when s.A == Matiere.Peau => Seul("savoir.EauPortee.peau"),
        Effet.Tubercules or Effet.Chasse or Effet.Roule or Effet.Flotte => new Recit($"savoir.{s.Effet}", ("a", Nom(s.A))),
        Effet.Soulagement => new Recit("savoir.Soulagement", ("plante", plante ?? Seul("savoir.cettePlante")), ("mal", Mal(s.Symptome))),
        Effet.Poison or Effet.Visions => new Recit($"savoir.{s.Effet}", ("plante", plante ?? Seul("savoir.cettePlante"))),
        Effet.Pansement when s.A == Matiere.Eau => Seul("savoir.Pansement.eau"),
        Effet.PorteFeu => Seul(s.A == Matiere.Corde ? "savoir.PorteFeu.corde" : "savoir.PorteFeu"),
        Effet.Foyer or Effet.Chaleur or Effet.Tiedeur or Effet.Durci or Effet.Corde or Effet.Abri or Effet.EauPortee or Effet.Cuisson or Effet.Decoupe
            or Effet.Nage or Effet.Brandon or Effet.Percage or Effet.Rondelle or Effet.Pansement or Effet.Chant or Effet.Danse or Effet.Ocre
            or Effet.Peinture or Effet.MainsPropres or Effet.EauVive or Effet.Latrines or Effet.ViandeSaine or Effet.Fumage or Effet.Fumee
            or Effet.Brulis or Effet.Braises or Effet.FoyerDegage or Effet.PasDansLeVent or Effet.EteindreEnPartant or Effet.BrulisProtecteur
            or Effet.CacheDEau or Effet.Sechage or Effet.Provisions or Effet.CacheFroide or Effet.Pagaie or Effet.Limon or Effet.Repousse or Effet.Sarclage or Effet.Semence or Effet.Essart or Effet.Apprivoiser or Effet.Traite => Seul($"savoir.{s.Effet}"),
        Effet.Semis => new Recit("savoir.Semis", ("plante", plante ?? Seul("savoir.cettePlante"))),
        Effet.Moisson => Seul($"savoir.Moisson.{s.Geste}"),
        Effet.Transformation when Comportement.LireRecette(s.Pieces) is { } r => DecrireRecette(r.Geste, r.A, r.B, r.Mode, r.Changements, r.Famille),
        Effet.Modelage => Seul(s.Pieces == "M|1" ? "savoir.Modelage.creux" : "savoir.Modelage"),
        Effet.Facture when Facons.LireObjet(s.Pieces) is { } f => new Recit($"savoir.Facture.{f.Fonction}", ("objet", Facons.Decrire(f.Facture))),
        Effet.Remarque when Facons.LireRemarque(s.Pieces) is { } r => new Recit($"savoir.Remarque.{r}", ("a", Nom(s.A))),
        Effet.Regularite when Comportement.LireRegularite(s.Pieces) is { } rg => Comportement.DireRegularite(rg.Serie, rg.Jours),
        Effet.Rien when Facons.LireObjet(s.Pieces) is { } fr => new Recit("savoir.Facture.Rien", ("objet", Facons.Decrire(fr.Facture))),
        _ when Assemblage.Lire(s.Pieces) is { } a => Lois.Savoir(s.Effet, a),
        _ => Seul("savoir.Rien"),
    };

    /// <summary>La matière d'une recette : son nom, ou pour une chose façonnée ce qu'elle était (« un objet de terre mouillée »).</summary>
    private static Recit MatiereDeRecette(Matiere a, string famille) =>
        a == Matiere.Substance && famille.Length > 0 ? new Recit("chim.objet", ("fam", Seul($"chim.fam.{famille}"))) : Nom(a);

    /// <summary>Une recette de la matière, telle qu'on se la dit (« l'argile laissée longtemps dans les flammes durcit et ne se défait plus dans l'eau »).</summary>
    public static Recit DecrireRecette(Geste g, Matiere a, Matiere b, int mode, IReadOnlyList<string> changements, string famille) =>
        new($"savoir.Transformation.{g}{(g == Geste.Cuire ? "." + mode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}",
            ("a", MatiereDeRecette(a, famille)), ("b", Nom(b)), ("chg", Chimie.Perception.Dire(changements)));

    /// <summary>Le mal que soulage un remède (« les maux de ventre »).</summary>
    public static Recit Mal(Symptome s) => Seul($"mal.{s}");

    /// <summary>Signe que l'humain perçoit de sa maladie.</summary>
    public static Symptome SymptomeDe(Maladie m) => m switch
    {
        Maladie.Diarrhee or Maladie.Dysenterie or Maladie.VersIntestinaux => Symptome.Ventre,
        Maladie.Pneumonie or Maladie.Paludisme or Maladie.Tuberculose or Maladie.Rougeole or Maladie.Aucune => Symptome.Fievre,
        Maladie.PlaieInfectee or Maladie.Tetanos => Symptome.Plaie,
        Maladie.InfectionDeLaPeau => Symptome.Peau,
        _ => Symptome.Aucun,
    };

    /// <summary>
    /// Matières brutes qu'on trouve sur une case : les roches dépendent de la région (tirées une fois pour toutes de la graine),
    /// le reste du terrain et de la saison.
    /// </summary>
    /// <param name="generation">Génération du monde : à partir de 2, on trouve aussi du calcaire, du sable, des coquillages, de l'écorce
    /// et des morceaux des gisements qui affleurent.</param>
    public static IEnumerable<Matiere> Disponibles(Carte carte, int x, int y, bool saisonSeche, bool volcanProche, int generation = 0)
    {
        var b = carte.BiomeEn(x, y);
        var roches = Roches(carte, x, y);
        if ((roches & 1) != 0 || b is Biome.Riviere or Biome.Plage or Biome.Montagne) yield return Matiere.Galet;
        if ((roches & 2) != 0) yield return Matiere.Silex;
        if ((roches & 4) != 0) yield return Matiere.Pyrite;
        if (volcanProche && (roches & 8) != 0) yield return Matiere.Obsidienne;
        if (b is Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Marais or Biome.Montagne
              or Biome.ForetDeFeuillus or Biome.Mangrove or Biome.Lande)
        {
            yield return Matiere.BoisSec;
            yield return Matiere.Branche;
        }
        else if (b is Biome.Desert or Biome.Plage) yield return Matiere.BoisSec;
        if (b is Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Marais or Biome.ForetDeFeuillus) yield return Matiere.GrandesFeuilles;
        if (b is Biome.ForetTropicale or Biome.Marais or Biome.Riviere or Biome.ForetDeFeuillus or Biome.Mangrove) yield return Matiere.Fibres;
        if (saisonSeche && b is Biome.PlaineDeFougeres or Biome.ForetDeConiferes or Biome.Desert or Biome.Montagne || b == Biome.Lande) yield return Matiere.HerbeSeche;
        if (b is Biome.ForetTropicale or Biome.Marais) yield return Matiere.Calebasse;
        // Roseaux, joncs et papyrus poussent les pieds dans l'eau douce ou saumâtre.
        if (b is Biome.Marais or Biome.Riviere or Biome.Mangrove) yield return Matiere.Roseau;
        if (b is Biome.Riviere or Biome.Marais or Biome.Mangrove || carte.Terrain.Roches[carte.Indice(x, y)] == Nature.Roche.Schiste) yield return Matiere.Argile;
        // Arbres tombés : là où il y a des arbres (les forêts, les fougères arborescentes, les rives).
        if (b is Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Riviere or Biome.Marais
            or Biome.ForetDeFeuillus or Biome.Mangrove) yield return Matiere.Tronc;
        if (b is Biome.ForetDeConiferes) yield return Matiere.Resine;
        // Terre rouge (oxydes de fer) : dans les roches des montagnes et des déserts, les grès rouges et les latérites des
        // forêts chaudes, et par endroits dans les plaines et sur les berges.
        if (b is Biome.Montagne or Biome.Desert || Ocreuse(carte, x, y)) yield return Matiere.Ocre;
        if (generation < 2) yield break;
        var roche = carte.Terrain.Roches[carte.Indice(x, y)];
        if (roche is Nature.Roche.Calcaire or Nature.Roche.Craie && Climat.EstPraticable(b)) yield return Matiere.Calcaire;
        if (b is Biome.Plage or Biome.Desert or Biome.Riviere || roche == Nature.Roche.Gres) yield return Matiere.Sable;
        // Coquilles des plages et des vasières ; moules d'eau douce dans les rivières.
        if (b is Biome.Plage or Biome.Mangrove or Biome.Riviere) yield return Matiere.Coquillage;
        if (b is Biome.ForetDeFeuillus or Biome.ForetDeConiferes) yield return Matiere.Ecorce;
        if (carte.Terrain.GisementA(x, y) is { Profondeur: Nature.Enfouissement.Affleurant }) yield return Matiere.Minerai;
    }

    /// <summary>
    /// Terre rouge ici : grès et latérites en sont faits ; ailleurs, sous les forêts chaudes, les plaines et les berges,
    /// une zone de 4 × 4 cases sur quatre environ, stable pour une graine.
    /// </summary>
    public static bool Ocreuse(Carte carte, int x, int y)
    {
        var i = carte.Indice(x, y);
        if (carte.Terrain.Roches[i] == Nature.Roche.Gres && Climat.EstPraticable(carte.BiomeEn(x, y)) || carte.Terrain.Sols[i] == Nature.Sol.Lateritique) return true;
        if (carte.BiomeEn(x, y) is not (Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Riviere)) return false;
        var etat = carte.Graine ^ 0x4F435245UL ^ ((ulong)(x / 4) * 0xD6E8FEB86659FD93UL) ^ ((ulong)(y / 4) * 0x9E3779B97F4A7C15UL);
        return (Alea.SplitMix64(ref etat) & 0xFFFF) / 65536.0 < 0.25;
    }

    /// <summary>
    /// Y a-t-il ici une paroi de roche où peindre ? Dans les montagnes et les déserts, sur les falaises, et dans les grottes et
    /// les abris sous roche.
    /// </summary>
    public static bool Paroi(Carte carte, int x, int y) =>
        carte.BiomeEn(x, y) is Biome.Montagne or Biome.Desert
        || Climat.EstPraticable(carte.BiomeEn(x, y)) && (carte.Terrain.Falaise[carte.Indice(x, y)] || carte.Terrain.GrotteEn[carte.Indice(x, y)] != 0);

    /// <summary>
    /// Pierres de la case : galets durs (bit 1), silex (bit 2), pyrite (bit 4), obsidienne (bit 8, près des volcans),
    /// selon la roche du lieu (voir <see cref="Nature.Terrain.Pierres"/>).
    /// </summary>
    public static int Roches(Carte carte, int x, int y) => carte.Terrain.Pierres[carte.Indice(x, y)];
}
