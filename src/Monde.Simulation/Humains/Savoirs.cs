using Monde.Simulation.Monde;

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
/// <param name="Plein">Pour une calebasse : remplie d'eau.</param>
/// <param name="Pieces">Pour un objet assemblé : ses pièces et son lien (voir <see cref="Assemblage"/>).</param>
public sealed record Objet(Matiere Matiere, double Qualite, double Usure, bool Plein = false, string? Pieces = null);

/// <summary>Ce qui accompagne un geste : le feu, l'eau, la saison sèche, l'adresse et l'intelligence de celui qui le fait.</summary>
/// <param name="Sec">Herbe et bois assez secs pour prendre feu (0 à 1).</param>
/// <param name="FeuxReussis">Feux que celui qui le fait a déjà obtenus par ce geste : on refait bien mieux ce qu'on sait faire.</param>
/// <param name="Coupe">Force à la coupe du meilleur outil qu'il a en main (voir <see cref="Lois.Coupe"/>).</param>
public readonly record struct ContexteGeste(bool FeuProche, bool EauProche, double Sec, Biome Biome, double Adresse, double Ingeniosite,
    int FeuxReussis = 0, double Coupe = 0);

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
            [Matiere.Feu, Matiere.Brandon, Matiere.Branche, Matiere.Galet]),
        new(Geste.Approcher, "approcher", "de", [Matiere.Aucune, Matiere.BoisSec, Matiere.Branche, Matiere.BatonPointu, Matiere.Carcasse, Matiere.Argile],
            [Matiere.Feu]),
        new(Geste.SeCouvrir, "se couvrir de", null, [Matiere.Peau, Matiere.GrandesFeuilles, Matiere.HerbeSeche, Matiere.Fibres], []),
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

    /// <summary>Peut-on porter cette matière sur soi ?</summary>
    public static bool Portable(Matiere m) => m is not (Matiere.Aucune or Matiere.Eau or Matiere.Feu or Matiere.Carcasse or Matiere.Plante or Matiere.Argile
        or Matiere.Tronc);

    public static Resultat Tenter(Geste g, Matiere a, Matiere b, ContexteGeste c, Alea alea)
    {
        var h = c.Adresse;
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
                    if (!Chance(p)) return default;
                    var qualite = Math.Clamp((fine ? 0.5 : 0.25) + 0.4 * h * alea.Reel() + (a == Matiere.Obsidienne || b == Matiere.Obsidienne ? 0.2 : 0), 0, 1);
                    return new(Effet.Eclat, Matiere.Eclat, qualite);
                }
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
                if (EstBois(a) && b is Matiere.Feu or Matiere.Brandon && c.FeuProche)
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
                    _ => default,
                };

            case Geste.Remplir:
                return a == Matiere.Calebasse && c.EauProche ? new(Effet.EauPortee, Matiere.Calebasse, 1) : default;

            case Geste.Fouir:
                if (!(EstBois(a) || a is Matiere.BatonPointu or Matiere.Epieu)) return default;
                var terre = c.Biome switch
                {
                    Biome.PlaineDeFougeres or Biome.ForetTropicale or Biome.Marais => 1.0,
                    Biome.ForetDeConiferes or Biome.Plage => 0.5,
                    Biome.Desert => 0.4,
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
        _ => false,
    };

    /// <summary>Nom court d'un effet, pour la chronologie des découvertes.</summary>
    public static string NomEffet(Effet e) => e switch
    {
        Effet.Eclat => "tailler un éclat de pierre",
        Effet.Etincelle => "faire jaillir des étincelles",
        Effet.Flamme => "allumer le feu",
        Effet.Foyer => "entretenir un feu",
        Effet.Chaleur => "se chauffer au feu",
        Effet.Tiedeur => "échauffer le bois en le frottant",
        Effet.Pointe => "tailler une pointe",
        Effet.Durci => "durcir une pointe au feu",
        Effet.Corde => "tordre des fibres en corde",
        Effet.Couvert => "se vêtir",
        Effet.Abri => "bâtir une hutte",
        Effet.EauPortee => "porter l'eau dans une calebasse",
        Effet.Tubercules => "fouir la terre pour des racines",
        Effet.Cuisson => "cuire la viande",
        Effet.Decoupe => "découper le gibier, garder sa peau",
        Effet.Nage => "nager",
        Effet.Soulagement => "soigner par les plantes",
        Effet.Poison => "reconnaître une plante qui rend malade",
        Effet.Visions => "les plantes à visions",
        Effet.Brandon => "emporter le feu avec un brandon",
        Effet.Chasse => "chasser avec une arme taillée",
        Effet.Roule => "voir rouler ce qui est rond",
        Effet.Flotte => "voir flotter le bois",
        Effet.PointeEmmanchee => "emmancher une pointe de pierre",
        Effet.TranchantEmmanche => "emmancher une pierre tranchante",
        Effet.Glisser => "traîner une charge sur des branches liées",
        Effet.Rouleaux => "faire rouler une charge sur des troncs",
        Effet.Rondelle => "trancher un tronc en rondelles",
        Effet.Percage => "percer le bois",
        Effet.RouleSurAxe => "faire tourner des rondelles autour d'un axe",
        Effet.Flottaison => "aller sur l'eau sur du bois lié",
        Effet.Pansement => "soigner une plaie",
        _ => "rien",
    };

    /// <summary>Nom d'une découverte dans la chronologie : une seule roue et plusieurs roues sont deux trouvailles.</summary>
    public static string NomDecouverte(Effet e, string? pieces) => e == Effet.RouleSurAxe && Lois.Structure(pieces) == "axe:1"
        ? "pousser une charge sur une seule rondelle qui tourne, tenue par deux bras"
        : NomEffet(e);

    /// <summary>Le geste et ses matières, en mots (« pousser un tronc sur l'eau »).</summary>
    public static string Phrase(Geste g, Matiere a, Matiere b)
    {
        var f = Formes.FirstOrDefault(x => x.Geste == g);
        var verbe = f?.Verbe ?? NomGeste(g);
        if (b == Matiere.Aucune) return SansSeconde(g) is { } sol ? $"{verbe} {Nom(a)} sur {sol}" : $"{verbe} {Nom(a)}";
        var premiere = a == Matiere.Aucune ? "ses mains" : Nom(a);
        return $"{verbe} {premiere} {f?.Liaison ?? "et"} {(b == Matiere.Assemblage ? "son outil emmanché" : Nom(b))}"
            .Replace(" de le ", " du ", StringComparison.Ordinal).Replace(" de les ", " des ", StringComparison.Ordinal);
    }

    public static string NomGeste(Geste g) => g switch
    {
        Geste.Frapper => "frapper",
        Geste.Frotter => "frotter",
        Geste.Gratter => "racler",
        Geste.Tordre => "tordre",
        Geste.Entasser => "entasser",
        Geste.Approcher => "approcher du feu",
        Geste.SeCouvrir => "se couvrir de",
        Geste.Remplir => "remplir",
        Geste.Fouir => "fouir la terre avec",
        Geste.Manger => "manger",
        Geste.Appliquer => "appliquer",
        Geste.Assembler => "assembler",
        Geste.Pousser => "pousser",
        Geste.Percer => "percer",
        Geste.Trancher => "trancher",
        Geste.Panser => "panser une plaie avec",
        _ => "nager",
    };

    public static string Nom(Matiere m) => m switch
    {
        Matiere.Galet => "un galet",
        Matiere.Silex => "un silex",
        Matiere.Pyrite => "une pierre brillante (pyrite)",
        Matiere.Obsidienne => "une pierre noire et luisante (obsidienne)",
        Matiere.BoisSec => "du bois sec",
        Matiere.Branche => "une branche",
        Matiere.GrandesFeuilles => "de grandes feuilles",
        Matiere.Fibres => "des fibres",
        Matiere.HerbeSeche => "de l'herbe sèche",
        Matiere.Calebasse => "une calebasse",
        Matiere.Argile => "de l'argile",
        Matiere.Carcasse => "une bête tuée",
        Matiere.Plante => "une plante",
        Matiere.Eau => "l'eau",
        Matiere.Feu => "le feu",
        Matiere.Eclat => "un éclat de pierre",
        Matiere.BatonPointu => "un bâton pointu",
        Matiere.Epieu => "un épieu",
        Matiere.Corde => "une corde",
        Matiere.Peau => "une peau",
        Matiere.Brandon => "un brandon",
        Matiere.Tronc => "un tronc",
        Matiere.Resine => "de la résine",
        Matiere.Rondelle => "une rondelle de tronc",
        Matiere.RondellePercee => "une rondelle percée",
        Matiere.Assemblage => "un objet assemblé",
        _ => "rien",
    };

    /// <summary>Nom d'un objet porté : un objet assemblé est décrit par ses pièces.</summary>
    public static string Nom(Objet o) => o.Matiere == Matiere.Assemblage && Assemblage.Lire(o.Pieces) is { } a ? Lois.Decrire(a) : Nom(o.Matiere);

    /// <summary>Le savoir en quelques mots, tel que l'humain se le dit.</summary>
    public static string Decrire(Savoir s, string? plante = null) => s.Effet switch
    {
        Effet.Eclat => $"Frapper {Nom(s.A)} contre {Nom(s.B)} en détache un éclat tranchant.",
        Effet.Etincelle => $"Frapper {Nom(s.A)} contre {Nom(s.B)} fait jaillir des étincelles.",
        Effet.Flamme when s.Geste == Geste.Frotter => "Frotter du bois sec contre du bois sec, longtemps, fait naître le feu.",
        Effet.Flamme => $"Frapper {Nom(s.A)} contre {Nom(s.B)} au-dessus d'herbe sèche fait naître le feu.",
        Effet.Foyer => "Mettre du bois sur le feu le fait durer.",
        Effet.Chaleur => "Le feu chauffe.",
        Effet.Tiedeur => "Le bois frotté devient chaud.",
        Effet.Pointe => $"Racler une branche avec {Nom(s.B)} la rend pointue.",
        Effet.Durci => "Une pointe passée au feu devient dure.",
        Effet.Corde => "Des fibres tordues deviennent une corde.",
        Effet.Couvert => $"Se couvrir {(s.A == Matiere.Peau ? "d'une peau" : "de feuilles")} protège du froid.",
        Effet.Abri => "Des feuilles posées sur des branches font un abri.",
        Effet.EauPortee => "Une calebasse garde l'eau pour la route.",
        Effet.Tubercules => $"Fouir la terre avec {Nom(s.A)} donne des racines à manger.",
        Effet.Cuisson => "La viande passée au feu est meilleure.",
        Effet.Decoupe => "Un éclat tranchant découpe la bête et lui ôte sa peau.",
        Effet.Nage => "On peut flotter et avancer dans l'eau.",
        Effet.Soulagement => $"{plante ?? "Cette plante"} soulage {Mal(s.Symptome)}.",
        Effet.Poison => $"{plante ?? "Cette plante"} rend malade.",
        Effet.Visions => $"{plante ?? "Cette plante"} fait voir d'autres mondes.",
        Effet.Brandon => "Une branche approchée du feu emporte le feu avec elle.",
        Effet.Chasse => $"Avec {Nom(s.A)}, on tue plus de bêtes.",
        Effet.Roule => $"Poussé sur un sol dur, {Nom(s.A)} roule.",
        Effet.Flotte => $"Posé sur l'eau, {Nom(s.A)} flotte.",
        Effet.Percage => "Une pointe dure qu'on fait tourner longtemps perce le bois.",
        Effet.Rondelle => "Un tronc tranché à grands coups donne des rondelles.",
        Effet.Pansement when s.A == Matiere.Eau => "Une plaie lavée à l'eau guérit mieux.",
        Effet.Pansement => "Une plaie pressée et couverte de feuilles guérit mieux.",
        _ when Assemblage.Lire(s.Pieces) is { } a => Lois.Savoir(s.Effet, a),
        _ => "Rien.",
    };

    public static string Mal(Symptome s) => s switch
    {
        Symptome.Ventre => "les maux de ventre",
        Symptome.Fievre => "la fièvre",
        Symptome.Plaie => "les plaies",
        Symptome.Peau => "les maux de peau",
        _ => "le mal",
    };

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
    public static IEnumerable<Matiere> Disponibles(Carte carte, int x, int y, bool saisonSeche, bool volcanProche)
    {
        var b = carte.BiomeEn(x, y);
        var roches = Roches(carte, x, y);
        if ((roches & 1) != 0 || b is Biome.Riviere or Biome.Plage or Biome.Montagne) yield return Matiere.Galet;
        if ((roches & 2) != 0) yield return Matiere.Silex;
        if ((roches & 4) != 0) yield return Matiere.Pyrite;
        if (volcanProche && (roches & 8) != 0) yield return Matiere.Obsidienne;
        if (b is Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Marais or Biome.Montagne)
        {
            yield return Matiere.BoisSec;
            yield return Matiere.Branche;
        }
        else if (b is Biome.Desert or Biome.Plage) yield return Matiere.BoisSec;
        if (b is Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Marais) yield return Matiere.GrandesFeuilles;
        if (b is Biome.ForetTropicale or Biome.Marais or Biome.Riviere) yield return Matiere.Fibres;
        if (saisonSeche && b is Biome.PlaineDeFougeres or Biome.ForetDeConiferes or Biome.Desert or Biome.Montagne) yield return Matiere.HerbeSeche;
        if (b is Biome.ForetTropicale or Biome.Marais) yield return Matiere.Calebasse;
        if (b is Biome.Riviere or Biome.Marais) yield return Matiere.Argile;
        // Arbres tombés : là où il y a des arbres (les forêts, les fougères arborescentes, les rives).
        if (b is Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Riviere or Biome.Marais) yield return Matiere.Tronc;
        if (b is Biome.ForetDeConiferes) yield return Matiere.Resine;
    }

    /// <summary>
    /// Roches d'une zone de 4 × 4 cases (3,2 km de côté), stables pour une graine : galets (bit 1), silex (bit 2), pyrite (bit 4,
    /// presque toujours avec le silex, comme les nodules de marcassite de la craie), obsidienne (bit 8, près des volcans).
    /// </summary>
    public static int Roches(Carte carte, int x, int y)
    {
        var etat = carte.Graine ^ 0x524F43484553UL ^ ((ulong)(x / 4) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(y / 4) * 0xC2B2AE3D27D4EB4FUL);
        var z = Alea.SplitMix64(ref etat);
        var u1 = (z & 0xFFFF) / 65536.0;
        var u2 = ((z >> 16) & 0xFFFF) / 65536.0;
        var u3 = ((z >> 32) & 0xFFFF) / 65536.0;
        var u4 = ((z >> 48) & 0xFFFF) / 65536.0;
        var r = 0;
        if (u1 < 0.35) r |= 1;
        if (u2 < 0.18) r |= 2;
        if ((r & 2) != 0 ? u3 < 0.35 : u3 < 0.03) r |= 4;
        if (u4 < 0.5) r |= 8;
        return r;
    }
}
