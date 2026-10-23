using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Règles de vie d'un humain, appliquées à chaque pas de 10 minutes :
/// les besoins évoluent, la santé en dépend, puis l'humain choisit une action.
/// Les seuils sont des réglages de départ, à ajuster en observant le monde.
/// </summary>
public sealed partial class Comportement(Carte carte, Ecosysteme ecosysteme, Alea alea)
{
    // Évolution des besoins par pas de 10 minutes.
    private const double FaimParPas = 0.23;     // affamé en 3 jours environ
    private const double SoifParPas = 0.5;      // assoiffé en un peu plus d'un jour
    private const double FatigueParPas = 0.55;  // épuisé après une longue journée sans dormir
    private const double RecuperationSommeil = 1.2;
    private const double TemperatureConfort = 15;
    private const double ChaleurCorporelle = 0.5;

    /// <summary>Humains vivants dans le monde (mis à jour chaque jour) : certaines maladies n'existent que dans les grandes populations.</summary>
    public int Population { get; set; }

    /// <summary>« de » devant le nom d'une maladie : « d'une pneumonie », « du paludisme », « des vers intestinaux ».</summary>
    internal static string De(string nom) => nom switch
    {
        _ when nom.StartsWith("le ", StringComparison.Ordinal) => "du " + nom[3..],
        _ when nom.StartsWith("les ", StringComparison.Ordinal) => "des " + nom[4..],
        _ when nom.StartsWith("des ", StringComparison.Ordinal) => "de " + nom[4..],
        _ when nom.StartsWith("une ", StringComparison.Ordinal) || nom.StartsWith("un ", StringComparison.Ordinal) => "d'" + nom,
        _ => "de " + nom,
    };

    /// <summary>
    /// Santé perdue par pas pour une maladie de gravité 1. Réglé pour qu'une maladie moyenne (gravité × durée) coûte
    /// autant de santé qu'avant que les maladies soient distinguées : la mortalité d'ensemble reste calée.
    /// </summary>
    private const double DegatsMaladie = 0.021;

    private const double ProbabiliteMaladieDeBase = 0.000012; // environ une maladie tous les deux ans

    private static readonly (int Dx, int Dy)[] Voisins8 =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    // État du calcul en cours, propre à chaque fil de calcul : les humains d'un pas simultané sont calculés en même temps.

    /// <summary>Instant du pas en cours.</summary>
    [ThreadStatic] private static long _maintenant;

    // Raison de la décision en cours et humain concerné, pour la pensée qui l'accompagne.
    [ThreadStatic] private static Motif _motif;
    [ThreadStatic] private static Humain? _autre;

    /// <summary>Événements survenus pendant le pas : le monde les verse au journal après le pas.</summary>
    public List<EvenementJournal> Journal => Tour.Courant?.Journal ?? _journal;

    private readonly List<EvenementJournal> _journal = [];

    /// <summary>Morts du pas : le monde endeuille leurs proches.</summary>
    public List<Humain> Deces { get; } = [];

    /// <summary>Liens, humeur et partages (fourni par le monde).</summary>
    public VieSociale? Social { get; set; }

    /// <summary>Numéros des événements (partagés avec tout le monde simulé).</summary>
    public Numeroteur Numeros { get; set; } = new();

    /// <summary>Catastrophes en cours (dangers à fuir, écart de température) ; fourni par le monde.</summary>
    public Catastrophes? Catastrophes { get; set; }

    /// <summary>Température de la case, avec l'écart dû aux vagues de froid, canicules et hivers volcaniques.</summary>
    private double Temperature(int x, int y, long maintenant) => Climat.Temperature(carte, x, y, maintenant) + (Catastrophes?.Anomalie ?? 0);

    /// <summary>Température ressentie par l'humain en cours, pour le coup de chaleur.</summary>
    [ThreadStatic] private static double _temperature;

    /// <summary>
    /// Note un événement et renvoie son numéro, pour que ses suites puissent le citer comme cause. <paramref name="karma"/> et
    /// <paramref name="karmaAutre"/> : ce que vaut l'acte, aux yeux de l'Entité, pour <paramref name="h"/> et pour
    /// <paramref name="autre"/> (voir <see cref="Regard"/>).
    /// </summary>
    private long Noter(long maintenant, TypeEvenement type, Humain h, Humain? autre, string texte, long cause = 0, bool significatif = false,
        double karma = 0, double karmaAutre = 0, int autreCommunaute = 0)
    {
        var e = new EvenementJournal(maintenant, type, h.Id, autre?.Id ?? 0, texte)
        {
            Id = Numeros.Suivant(), CauseId = cause, Significatif = significatif, AutreCommunauteId = autreCommunaute,
            Karma = Regard.Juger(h, karma, maintenant, autre), KarmaAutre = Regard.Juger(autre, karmaAutre, maintenant, h),
        };
        Journal.Add(e);
        return e.Id;
    }

    public void Vivre(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstVivant) return;
        _maintenant = maintenant;
        _tous = tous;
        _nageur = false;
        if (h.EstBebe(maintenant))
        {
            // Sans sa mère, le bébé est porté par une femme qui allaite, ou, sevrable, par celui qui veille sur lui.
            var mere = Vivant(tous, h.MereId);
            VivreBebe(h, mere ?? Nourrice(h, tous, maintenant), maintenant, mere is null);
            return;
        }
        // Pour un enfant, le « compagnon » à rejoindre (pour dormir, se tenir chaud) est celui qui veille sur lui.
        var partenaire = h.EstEnfant(maintenant) ? Gardien(h, tous, maintenant) : Vivant(tous, h.PartenaireId);
        var chaleurPartagee = AuMemeEndroit(h, partenaire) || AuMemeEndroit(h, Vivant(tous, h.MereId)) || AuMemeEndroit(h, Vivant(tous, h.PereId))
                              || AmiAuMemeEndroit(h, tous);
        Metaboliser(h, chaleurPartagee, maintenant);
        if (!h.EstVivant) return;
        // La soif fait nager même épuisé, au risque de se noyer : on ne reste pas mourir sur une rive sans eau douce.
        SuivreSesObjets(h);
        _nageur = PeutNager(h) || h.Nage >= 0.4 && h.Soif >= 60 && h.Froid < 50 || Radeau(h) is not null;
        Ramasser(h);
        ObserverLeFeu(h, tous);
        DecouvrirLaBraise(h, tous);
        SentirLaFumee(h, tous);
        if (h.EstEnfant(maintenant))
        {
            // Un orphelin est nourri par celui qui l'a pris sous son aile.
            var pere = Vivant(tous, h.PereId);
            NourrirEnfant(h, Vivant(tous, h.MereId) ?? (partenaire != pere ? partenaire : null), pere, tous);
        }
        PartagerAvecProches(h, tous);
        NourrirLaMere(h, tous);
        RecevoirViande(h, tous);
        Voler(h, tous, maintenant);
        if (!h.EstVivant) return;
        Grossesse(h, tous, maintenant);
        if (!h.EstVivant) return;
        if (h.Activite != Activite.Dormir) Affronter(h, tous, maintenant);
        if (!h.EstVivant) return;
        if (h.Impulsions.Count > 0) Ressentir(h, tous, maintenant);
        var avant = h.Activite;
        var (x0, y0) = (h.X, h.Y);
        (_motif, _autre) = (Motif.Aucun, null);
        Decider(h, partenaire, tous, maintenant);
        _nageur = false;
        if (!h.EstVivant) return;
        EteindreEnPartant(h, x0, y0, tous);
        if (h.Activite != avant && Penseur.Penser(h, _motif, _autre, maintenant) is { } pensee)
        {
            var routine = EstRoutine(h.Activite, _motif);
            // Au journal : les gestes de tous les jours sont résumés une fois par jour ; une autre pensée est notée,
            // sauf si une pensée de même sens figure déjà parmi les dernières (activités qui alternent).
            // Sauf choix contraire dans la configuration, la vie quotidienne n'est pas suivie : ni résumé de la journée,
            // ni pensées sur l'eau, la nourriture, l'abri, le sommeil ou les allées et venues.
            if (routine && JournalQuotidien) CompterRoutine(h.Id, h.Activite);
            var nouvelle = !routine && (JournalQuotidien || !EstQuotidien(h.Activite, _motif))
                           && !h.Memoire.PenseeRecente(pensee, Memoire.PenseesMax);
            if (h.Memoire.Penser(maintenant, pensee) && nouvelle) Noter(maintenant, TypeEvenement.Pensee, h, _autre, $"{h.Nom} : « {pensee} »");
        }
    }

    // À part : la fermeture n'est créée que si la routine est comptée.
    private void CompterRoutine(int id, Activite a) => Tour.Differer(() => Routine.Compter(id, a));

    /// <summary>Gestes quotidiens (boire, manger, dormir, se reposer, aller vers ce qu'on voit ou vers les siens) : résumés chaque jour.</summary>
    public JourneeType Routine { get; } = new();

    /// <summary>
    /// Journaliser la vie quotidienne (résumés de la journée et pensées liées aux besoins et aux allées et venues).
    /// Non par défaut : le journal garde ce qui touche aux autres, aux découvertes et aux événements.
    /// </summary>
    public bool JournalQuotidien { get; set; }

    /// <summary>Activités de la vie quotidienne : besoins du corps, gestes habituels, allées et venues sans enjeu.</summary>
    private static bool EstQuotidien(Activite a, Motif motif) => a switch
    {
        Activite.Boire or Activite.ChercherEau or Activite.Cueillir or Activite.Chasser or Activite.ChercherNourriture
            or Activite.SAbriter or Activite.ChercherAbri or Activite.Dormir or Activite.Repos or Activite.Explorer
            or Activite.Nager or Activite.Veiller or Activite.Fabriquer or Activite.Experimenter
            or Activite.RejoindreProche or Activite.SuivreParent => true,
        Activite.RejoindrePartenaire => motif != Motif.Desir,
        Activite.Migrer => motif != Motif.Guide,
        _ => false,
    };

    private static bool EstRoutine(Activite a, Motif motif) => a switch
    {
        Activite.Boire or Activite.Cueillir or Activite.Chasser or Activite.Dormir or Activite.Repos or Activite.Explorer
            or Activite.SAbriter or Activite.Experimenter or Activite.Fabriquer or Activite.Veiller or Activite.Nager => true,
        Activite.ChercherEau or Activite.ChercherNourriture or Activite.ChercherAbri => motif is Motif.Vu or Motif.Aucun,
        Activite.RejoindrePartenaire => motif != Motif.Desir,
        Activite.RejoindreProche or Activite.SuivreParent or Activite.VeillerEnfant => true,
        Activite.Migrer => motif != Motif.Guide,
        _ => false,
    };

    /// <summary>Se serrer contre quelqu'un qu'on aime réchauffe autant que contre son compagnon.</summary>
    private static bool AmiAuMemeEndroit(Humain h, IReadOnlyList<Humain> tous)
    {
        foreach (var l in h.Liens)
        {
            if (l.Affection < 20) continue;
            var a = tous[l.Id - 1];
            if (a.EstVivant && a.X == h.X && a.Y == h.Y) return true;
        }
        return false;
    }

    /// <summary>
    /// Un humain affamé reçoit un peu de nourriture d'un proche rassasié à côté de lui : d'autant plus que celui-ci
    /// est bienveillant et l'aime, ou vit dans la même communauté. Le partage de nourriture hors de la famille est
    /// universel chez les chasseurs-cueilleurs (Kaplan et Hill, 1985) et nourrit la reconnaissance.
    /// </summary>
    private void PartagerAvecProches(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim < 60 || h.EstBebe(_maintenant)) return;
        foreach (var l in h.Liens)
        {
            var d = tous[l.Id - 1];
            if (!d.EstVivant || d.Faim >= 40 || d.EstPetitEnfant(_maintenant) || d.EstDeprime) continue;
            if (Distance(h.X, h.Y, d.X, d.Y) > 1) continue;
            var lien = d.LienAvec(h.Id);
            if (lien is null) continue;
            // La loyauté envers le groupe pousse à nourrir les siens ; la compassion, à nourrir quiconque a faim, même sans l'aimer.
            var dm = d.Personnalite.Moral;
            var communaute = d.CommunauteId != 0 && d.CommunauteId == h.CommunauteId ? 0.3 * (0.5 + dm.Fondements.Loyaute) : 0;
            var detresse = Math.Max(0, dm.Compassion - 0.5) * (h.Faim - 60) / 40;
            // Partager équitablement est le propre des honnêtes ; l'avide garde tout, le psychopathe ne donne que par calcul.
            var generosite = (0.5 * d.Personnalite.Agreabilite + 0.25 * dm.Compassion + 0.25 * dm.Honnetete) * (1 - 0.8 * Psyche.Froideur(d.Personnalite));
            var envie = Math.Clamp(lien.Affection / 100 + communaute + detresse, 0, 1) * generosite;
            if (envie <= 0.05) continue;
            var donne = Math.Min(h.Faim - 40, 0.6 * envie);
            h.Faim -= donne;
            Tour.Changer(d, x => x.Faim = Math.Min(100, x.Faim + 0.6 * donne));
            VieSociale.Gratitude(d, h, 0.05);
            Social?.Partager(d, h);
            return;
        }
    }

    /// <summary>
    /// Celui qui veille sur un enfant : sa mère, sinon son père, sinon l'adulte qui l'aime le plus (un aîné, un oncle,
    /// une amie de la famille). Chez les chasseurs-cueilleurs, les orphelins sont recueillis par des proches
    /// (Hrdy, 2009), même si leur survie reste plus fragile. Un adulte compatissant de sa communauté le recueille
    /// plus volontiers, même s'il le connaît moins ; un cœur froid ne recueille personne.
    /// </summary>
    public static Humain? Gardien(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if ((Vivant(tous, h.MereId) ?? Vivant(tous, h.PereId)) is { } parent) return parent;
        Humain? choisi = null;
        var meilleur = 20.0;
        foreach (var l in h.Liens)
        {
            var a = tous[l.Id - 1];
            if (!a.EstVivant || a.EstEnfant(maintenant)) continue;
            var m = a.Personnalite.Moral;
            var score = (l.Affection + (a.CommunauteId != 0 && a.CommunauteId == h.CommunauteId ? 40 * (m.Compassion - 0.5) : 0))
                        * (1 - Psyche.Froideur(a.Personnalite));
            if (score < meilleur) continue;
            (choisi, meilleur) = (a, score);
        }
        return choisi;
    }

    private static bool AuMemeEndroit(Humain h, Humain? autre) => autre is not null && autre.X == h.X && autre.Y == h.Y;

    private static Humain? Vivant(IReadOnlyList<Humain> tous, int id) =>
        id > 0 && id <= tous.Count && tous[id - 1].EstVivant ? tous[id - 1] : null;

    private void Metaboliser(Humain h, bool chaleurPartagee, long maintenant)
    {
        var p = h.Personnalite;
        var enceinte = h.EstEnceinte;
        Depenser(h, FaimParPas * BesoinEnergie(h, maintenant));
        h.Soif += SoifParPas;
        h.Fatigue += h.Activite == Activite.Dormir
            ? -RecuperationSommeil
            : FatigueParPas * (1.2 - 0.4 * p.Endurance) * (h.Activite == Activite.Repos ? 0.5 : 1) * (enceinte ? 1.2 : 1);

        // Froid : la perte de chaleur dépend de l'écart au confort et de l'abri ; le corps en produit un peu.
        var temperature = Temperature(h.X, h.Y, maintenant);
        _temperature = temperature;
        // Au-delà de 28 °C, on transpire : la soif vient bien plus vite (jusqu'à un litre d'eau par heure d'effort).
        if (temperature > 28) h.Soif += SoifParPas * (temperature - 28) / 8 * (h.Activite is Activite.Repos or Activite.Dormir ? 0.5 : 1);
        var abri = AbriEn(h.X, h.Y) + Couverture(h);
        if (Installations.FoyerEn(h.X, h.Y) is not null) abri += ChaleurDuFeu;
        if (chaleurPartagee) abri += 0.3; // se tenir chaud avec son compagnon ou sa famille
        var perte = Math.Max(0, TemperatureConfort - temperature) * 0.05 * Math.Max(0, 1 - abri);
        h.Froid += perte - ChaleurCorporelle;

        // Le désir monte chez les adultes en âge d'avoir des enfants, s'ils ne sont pas en détresse.
        // Une maladie légère (des vers, une infection de la peau) ne l'éteint pas.
        if (h.EstFecond(maintenant) && !(h.EstMalade(maintenant) && Maladies.Fiche(h.Maladie).Gravite >= 0.5) && h.Faim < 70 && h.Soif < 70)
        {
            h.Desir = Math.Min(100, h.Desir + DesirParPas);
        }

        h.Faim = Math.Clamp(h.Faim, 0, 100);
        h.Soif = Math.Clamp(h.Soif, 0, 100);
        h.Fatigue = Math.Clamp(h.Fatigue, 0, 100);
        h.Froid = Math.Clamp(h.Froid, 0, 100);
        var faimSoif = (Math.Max(0, h.Faim - 50) + Math.Max(0, h.Soif - 50)) / 50;
        var froid = Math.Max(0, h.Froid - 40) / 50;
        h.FaimSoifDuJour += faimSoif;
        h.FroidDuJour += froid;
        h.InconfortDuJour += faimSoif + froid + (h.EstMalade(maintenant) ? 0.6 : 0);

        Souffrir(h, maintenant, 1);
    }

    /// <summary>Points de faim que représente un point de réserves (100 points de réserves : environ six semaines de nourriture).</summary>
    internal const double FaimParPointDeReserve = 15;

    /// <summary>
    /// Points de faim apaisés par une unité de gibier : la viande est bien plus nourrissante que les plantes (2,5 par unité).
    /// Réglé pour que le gibier d'un continent intact puisse nourrir de l'ordre d'une centaine de chasseurs à lui seul,
    /// les plantes faisant le reste, comme chez la plupart des chasseurs-cueilleurs des climats chauds (Lee, 1968).
    /// </summary>
    private const double ValeurGibier = 12;

    /// <summary>Niveau de réserves vers lequel tend un humain bien nourri (environ dix kilos de graisse).</summary>
    internal const double ReservesDeConfort = 65;

    /// <summary>Clémence du monde, de 0 à 1 (voir <see cref="MondeSimule.Clemence"/>) : part des risques visés que l'Entité écarte.</summary>
    public double Clemence { get; set; }

    /// <summary>Facteur appliqué aux risques visés par la clémence.</summary>
    private double Adouci => 1 - Clemence;

    /// <summary>
    /// Dépense d'énergie du pas, relative à celle d'un adulte moyen : métabolisme propre à chacun, effort du moment
    /// (dormir coûte moins que marcher ou chasser) et grossesse. Une grossesse demande environ 4 %, 12 % puis 21 %
    /// d'énergie en plus selon le trimestre (FAO/OMS/UNU, 2004) ; l'allaitement est compté dans <see cref="VivreBebe"/>.
    /// </summary>
    internal static double BesoinEnergie(Humain h, long maintenant)
    {
        var effort = h.Activite switch
        {
            Activite.Dormir => 0.8,
            Activite.Repos or Activite.SAbriter => 0.95,
            _ => 1.2,
        };
        var grossesse = h.EnceinteDepuis is { } debut
            ? ((maintenant - debut) / (double)Calendrier.SecondesParJour) switch { < 90 => 1.04, < 180 => 1.12, _ => 1.21 }
            : 1;
        return h.Biologie.Metabolisme * effort * grossesse;
    }

    /// <summary>
    /// Ajoute une dépense (ou un repas, si elle est négative) à la faim. Le corps puise dans ses réserves quand on a faim,
    /// ce qui ralentit la faim tant qu'il en reste ; quand on mange à sa faim, il en remet de côté, ce qui demande
    /// de manger un peu plus.
    /// </summary>
    internal static void Depenser(Humain h, double besoin)
    {
        if (besoin > 0 && h.Faim > 50 && h.Reserves > 0)
        {
            var part = 0.7 * Math.Min(1, h.Reserves / 20);
            h.Reserves = Math.Max(0, h.Reserves - besoin * part / FaimParPointDeReserve);
            besoin *= 1 - part;
        }
        else if (h.Faim < 30 && h.Reserves < ReservesDeConfort)
        {
            // Le corps régule son poids : il stocke de moins en moins en approchant de son niveau habituel
            // (chez les chasseurs-cueilleurs, la masse grasse varie de quelques kilos selon les saisons ; Jenike, 2001).
            var stocke = 0.004 * (1 - h.Reserves / ReservesDeConfort);
            h.Reserves += stocke;
            besoin += stocke * FaimParPointDeReserve;
        }
        h.Faim = Math.Clamp(h.Faim + besoin, 0, 100);
    }

    /// <summary>Dégâts sur la santé selon les besoins, mort éventuelle, puis risque de tomber malade.</summary>
    /// <summary>Nombre de causes de mort (taille du tableau des dégâts) : suit l'énumération quand elle grandit.</summary>
    private static readonly int NombreDeCauses = Enum.GetValues<CauseDeMort>().Max(c => (int)c) + 1;

    /// <param name="sevre">Un tout-petit qui ne reçoit pas de lait maternel ce pas-ci (voir <see cref="Sevre"/>).</param>
    private void Souffrir(Humain h, long maintenant, double fragilite, bool sevre = false)
    {
        var p = h.Personnalite;
        Span<double> degats = stackalloc double[NombreDeCauses];
        if (h.Faim >= 85) degats[(int)CauseDeMort.Faim] = 0.06;
        if (h.Soif >= 85) degats[(int)CauseDeMort.Soif] = 0.25;
        if (h.Froid >= 70) degats[(int)CauseDeMort.Froid] = 0.05 + 0.15 * (h.Froid - 70) / 30;
        if (h.Fatigue >= 95) degats[(int)CauseDeMort.Epuisement] = 0.05;
        if (h.EstMalade(maintenant))
            degats[(int)CauseDeMort.Maladie] = DegatsMaladie * Maladies.Gravite(h.Maladie, h.AgeEnAnnees(maintenant)) * (1.3 - 0.6 * p.Endurance)
                                               * (sevre && Sevre.Concerne(h.Maladie) ? Sevre.Gravite(h.AgeEnAnnees(maintenant)) : 1);
        // Coup de chaleur : au-delà de 36 °C, le corps ne se refroidit plus assez, surtout s'il manque d'eau.
        if (_temperature >= 36) degats[(int)CauseDeMort.Chaleur] = 0.01 * (_temperature - 35) * (h.Soif > 60 ? 2 : 1);
        // Une malédiction crue ronge (voir Medecine) : lentement, d'autant plus que la peur est forte.
        if (h.MauditLe > 0) degats[(int)CauseDeMort.Envoutement] = 0.006 * h.MauditForce * h.MauditForce;

        double total = 0, pire = 0;
        var cause = CauseDeMort.Aucune;
        for (var i = 1; i < degats.Length; i++)
        {
            total += degats[i] * fragilite;
            if (degats[i] > pire) (pire, cause) = (degats[i], (CauseDeMort)i);
        }

        // Une plaie soignée se refait plus vite.
        if (total == 0 && h.Faim < 60 && h.Soif < 60)
            h.Sante = Math.Min(100, h.Sante + 0.03 * (h.PlaieSoin > 0 && Medecine.PlaieOuverte(h, maintenant) ? 1 + 0.5 * h.PlaieSoin : 1));
        h.Sante -= total;
        if (h.Sante <= 0)
        {
            Mourir(h, maintenant, cause);
            return;
        }

        // Tomber malade : plus probable quand on est affaibli, affamé ou transi.
        if (!h.EstMalade(maintenant))
        {
            // La détresse affaiblit les défenses du corps.
            // La dénutrition (réserves épuisées) affaiblit l'immunité (Schaible et Kaufmann, 2007).
            var risque = ProbabiliteMaladieDeBase * (1 + (100 - h.Sante) / 25 + h.Faim / 50 + h.Froid / 50) * (1.3 - 0.6 * p.Endurance)
                         * (h.EstDeprime ? 1.3 : 1) * (1 + Math.Max(0, 25 - h.Reserves) / 25);
            var surcroit = sevre ? Sevre.Risque(h.AgeEnAnnees(maintenant)) : 1;
            if (alea.Reel() < risque * fragilite * AdouciPour(h, maintenant) * surcroit)
            {
                var maladie = Maladies.Choisir(h, carte.BiomeEn(h.X, h.Y), Calendrier.Decomposer(maintenant).Saison, Population, maintenant, alea, FumeeInterieure(h));
                // Le surcroît du tout-petit sans lait ne porte que sur les poumons et le ventre.
                if (surcroit > 1 && !Sevre.Concerne(maladie) && alea.Reel() >= 1 / surcroit) return;
                // Une plaie lavée et couverte pourrit moins ; des mains lavées, des excréments écartés, une eau vive gardent du ventre.
                if (alea.Reel() < Medecine.Protection(h, maladie, maintenant)) return;
                if (alea.Reel() < ProtectionFumee(h, maladie)) return;
                if (Hygiene.DuVentre(maladie))
                {
                    if (alea.Reel() < ProtectionHygiene(h, maladie, _tous)) return;
                    DouterHygiene(h);
                }
                h.Maladie = maladie;
                var fiche = Maladies.Fiche(h.Maladie);
                h.MaladeJusqua = maintenant + Maladies.Duree(fiche, h, alea);
                Noter(maintenant, TypeEvenement.Maladie, h, null, $"{h.Nom} tombe malade : {fiche.Nom} (personne ne sait la nommer).");
            }
        }
    }

    /// <summary>
    /// Mort d'un humain. Le suicide a pour cause la dépression ; un meurtre, les coups qui l'ont précédé
    /// (<paramref name="causeId"/>) et son auteur (<paramref name="meurtrier"/>).
    /// </summary>
    private void Mourir(Humain h, long maintenant, CauseDeMort cause, long causeId = 0, Humain? meurtrier = null, double karmaMeurtrier = Regard.Meurtre)
    {
        RemedeEchoue(h);
        PerduPourSonSoignant(h, maintenant);
        if (cause == CauseDeMort.Maladie && Hygiene.DuVentre(h.Maladie) && h.AgeEnAnnees(maintenant) < 15)
        {
            var mort = h.Id;
            Tour.Differer(() => _mortsDuVentre.Add(mort));
        }
        var maudisseur = h.MauditParId >= 1 && h.MauditParId <= _tous.Count ? _tous[h.MauditParId - 1] : null;
        // Mort de froid pendant une vague de froid, de chaleur pendant une canicule, d'une maladie contagieuse, de faim dans une
        // terre dévastée qui n'a pas reverdi : la cause est notée, et dite.
        string? contexte = null;
        if (causeId == 0 && Catastrophes is not null) (causeId, contexte) = Catastrophes.Attribuer(h, cause, maintenant);
        var suite = contexte is null ? "" : $", {contexte}";
        h.MortEvenementId = cause switch
        {
            CauseDeMort.Desespoir => Noter(maintenant, TypeEvenement.Mort, h, null,
                $"{h.Nom}, accablé{(h.Sexe == Sexe.Femme ? "e" : "")} de tristesse, met fin à ses jours, à {AgeEnMots(h, maintenant)}.",
                h.DepressionEvenementId),
            CauseDeMort.Meurtre when meurtrier is not null => Noter(maintenant, TypeEvenement.Mort, h, meurtrier,
                $"{h.Nom} meurt sous les coups de {meurtrier.Nom}, à {AgeEnMots(h, maintenant)}.", causeId, karmaAutre: karmaMeurtrier),
            CauseDeMort.Envoutement when maudisseur is not null => Noter(maintenant, TypeEvenement.Mort, h, maudisseur,
                $"{h.Nom} dépérit et meurt, persuadé{(h.Sexe == Sexe.Femme ? "e" : "")} que la malédiction de {maudisseur.Nom} {(h.Sexe == Sexe.Femme ? "la" : "le")} tue, à {AgeEnMots(h, maintenant)}.",
                h.MauditEvenementId, karmaAutre: Regard.MortMaudit),
            CauseDeMort.Empoisonnement => Noter(maintenant, TypeEvenement.Mort, h, null,
                $"{h.Nom} meurt empoisonné{(h.Sexe == Sexe.Femme ? "e" : "")} par la plante qu'{(h.Sexe == Sexe.Femme ? "elle" : "il")} a mangée, à {AgeEnMots(h, maintenant)}.", causeId),
            CauseDeMort.Noyade when causeId == 0 => Noter(maintenant, TypeEvenement.Mort, h, null,
                $"{h.Nom} se noie en nageant, à {AgeEnMots(h, maintenant)}.", causeId),
            CauseDeMort.Maladie when h.Maladie != Maladie.Aucune && h.EstMalade(maintenant) => Noter(maintenant, TypeEvenement.Mort, h, null,
                $"{h.Nom} meurt {De(Maladies.Fiche(h.Maladie).Nom)}{suite}, à {AgeEnMots(h, maintenant)}.", causeId),
            _ => Noter(maintenant, TypeEvenement.Mort, h, null, $"{h.Nom} meurt {Libelles.Cause(cause)}{suite}, à {AgeEnMots(h, maintenant)}.", causeId),
        };
        h.MeurtrierId = meurtrier?.Id ?? 0;
        Tour.Differer(h, x => Deces.Add(x));
        h.Sante = 0;
        h.MortLe = maintenant;
        h.CauseDeMort = cause;
        h.Activite = Activite.Mort;
        h.EnceinteDepuis = null;
    }

    /// <summary>
    /// Mort causée par une catastrophe ou une épidémie, avec son récit : notée comme les autres (journal, deuil des proches),
    /// et rattachée à sa cause.
    /// </summary>
    internal void Tuer(Humain h, long maintenant, CauseDeMort cause, string texte, long causeId, Humain? tueur = null, double karmaTueur = 0)
    {
        if (!h.EstVivant) return;
        PerduPourSonSoignant(h, maintenant);
        h.MortEvenementId = Noter(maintenant, TypeEvenement.Mort, h, tueur, texte, causeId, karmaAutre: karmaTueur);
        h.MeurtrierId = tueur?.Id ?? 0;
        Tour.Differer(h, x => Deces.Add(x));
        h.Sante = 0;
        h.MortLe = maintenant;
        h.CauseDeMort = cause;
        h.Activite = Activite.Mort;
        h.EnceinteDepuis = null;
    }

    private void Decider(Humain h, Humain? partenaire, IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var date = Calendrier.Decomposer(maintenant);
        var danger = Catastrophes?.DangerEn(h.X, h.Y);

        // Dans l'eau, qui ne peut plus nager ou s'y endort regagne la rive la plus proche, même à bout de forces ; un nageur
        // en état de le faire poursuit sa traversée.
        if (!Climat.EstPraticable(carte.BiomeEn(h.X, h.Y)) && (!_nageur || h.Activite == Activite.Dormir) && RegagnerLaRive(h)) return;

        // Un dormeur ne se réveille que si un besoin devient pressant ou s'il est reposé, ou si le danger le réveille
        // (la fumée, l'eau qui monte, la terre qui gronde).
        if (h.Activite == Activite.Dormir)
        {
            var reveil = h.Soif > 80 || h.Faim > 90 || h.Froid > 80 || h.Fatigue < 5 || (date.EstJour && h.Fatigue < 35)
                         || (danger is { } d && alea.Reel() < (d.Niveau == 2 ? 0.6 : 0.1));
            if (!reveil) return;
        }

        // Dans une crue qui ne fait que monter, l'assoiffé boit d'abord : l'eau est à ses pieds.
        if (danger is { Niveau: 1, Source.Type: TypeCatastrophe.Crue } && h.Soif > 50)
        {
            AllerBoire(h);
            return;
        }

        // Le danger passe avant tout le reste.
        if (danger is { } menace && Fuir(h, menace.Niveau, menace.Source)) return;

        // Urgence de chaque besoin (0 à 1 environ). Les prudents réagissent plus tôt ; le désespoir fait négliger ses besoins.
        var abattu = h.EstDeprime || h.Chagrin > 50;
        var seuil = 0.45 - 0.2 * p.Prudence + (abattu ? 0.1 : 0);
        // De l'eau à ses pieds : on boit en passant, quoi qu'on fasse (même transi ou épuisé, on ne meurt pas de soif au bord
        // d'une rivière).
        if (h.Soif >= 25 && EauDouceAPortee(h.X, h.Y))
        {
            h.Soif = 0;
            h.Memoire.Retenir(TypeLieu.Eau, h.X, h.Y, maintenant, Aptitude(h));
        }
        // Le besoin le plus pressant (à égalité, le premier de la liste).
        var (valeur, besoin) = (Urgence(h.Soif) * 1.2, Activite.Boire);
        Plus(Urgence(h.Faim), Activite.Cueillir, ref valeur, ref besoin);
        Plus(Urgence(h.Fatigue) * 0.9 + (date.EstJour ? 0 : 0.35), Activite.Dormir, ref valeur, ref besoin);
        Plus(Urgence(h.Froid) * (0.8 + p.Prudence * 0.4), Activite.SAbriter, ref valeur, ref besoin);

        // Un malade éveillé cherche à se soulager.
        if (valeur < 0.8 && (SoignerPlaie(h, tous) || SeSoigner(h, tous))) return;

        // Un petit enfant ne part pas seul chercher de l'eau, de quoi manger ou un abri : il reste près de celui qui
        // veille sur lui, qui le nourrit et l'abreuve, et ne prend que ce qui se trouve à ses pieds.
        if (h.EstEnfant(maintenant) && h.EstPetitEnfant(maintenant) && partenaire is not null)
        {
            ResterPres(h, partenaire, valeur >= seuil ? besoin : null);
            return;
        }

        // En raid, seul un besoin très pressant arrête la marche.
        if (valeur < 0.8 && MenerRaid(h)) return;

        // Ce que l'Entité lui a demandé, s'il a décidé d'obéir.
        if (valeur < 0.8 && SuivreLaParole(h, tous, maintenant)) return;

        // Celui qui veille sur un petit enfant assoiffé, affamé ou transi l'emmène vers l'eau, la nourriture ou un abri,
        // d'autant plus tôt qu'il y est attaché ; l'enfant le suit.
        if (!h.EstEnfant(maintenant) && valeur < 0.8 && PetitDansLeBesoin(h, tous, maintenant) is var (petit, pourLui))
        {
            _autre = petit;
            switch (pourLui)
            {
                case Activite.Boire: AllerBoire(h); return;
                case Activite.Cueillir: AllerManger(h); return;
                case Activite.SAbriter: AllerSAbriter(h, null, maintenant); return;
            }
        }

        // Ce qu'il sait faire peut répondre au besoin : le feu, une peau, une hutte, un bâton à fouir.
        if (!h.EstPetitEnfant(maintenant) && UtiliserSavoirs(h, besoin, valeur, seuil, tous)) return;

        // Au calme, le soir : fumer la viande qu'on porte, allumer le feu du camp.
        if (valeur < seuil && (FumerLaViande(h, tous) || GarderLeFeu(h, tous) || FeuDuSoir(h, tous) || FaireDesReserves(h, tous))) return;

        if (valeur >= seuil)
        {
            // Un enfant qui ne sait pas où trouver ce qu'il lui faut retourne auprès des siens plutôt que de se perdre.
            var recours = h.EstEnfant(maintenant) ? partenaire : null;
            switch (besoin)
            {
                case Activite.Boire: AllerBoire(h, recours); return;
                case Activite.Cueillir: AllerManger(h, recours); return;
                case Activite.Dormir: AllerDormir(h, partenaire); return;
                case Activite.SAbriter: AllerSAbriter(h, partenaire, maintenant); return;
            }
        }

        // Une captive reste près de celui qui l'a enlevée, tant qu'elle ne s'est pas enfuie.
        if (h.CaptifDe > 0 && Vivant(tous, h.CaptifDe) is { } ravisseur)
        {
            _autre = ravisseur;
            if (Distance(h.X, h.Y, ravisseur.X, ravisseur.Y) > 1) Avancer(h, ravisseur.X, ravisseur.Y, Activite.SuivreRavisseur);
            if (h.CibleX < 0)
            {
                h.Activite = Activite.Repos;
                h.CibleX = h.CibleY = -1;
            }
            return;
        }

        // Aucun besoin pressant. Un enfant suit ses parents, ou celui qui l'a recueilli.
        if (h.EstEnfant(maintenant))
        {
            var parent = partenaire;
            _autre = parent;
            if (parent is not null && Distance(h.X, h.Y, parent.X, parent.Y) > 1) Avancer(h, parent.X, parent.Y, Activite.SuivreParent);
            else if (!Enseigner(h, tous) && !Experimenter(h, tous))
            {
                h.Activite = Activite.Repos;
                h.CibleX = h.CibleY = -1;
            }
            return;
        }

        // Un adulte : veiller sur ses enfants, l'instinct de reproduction, puis rester près de l'autre, explorer ou se reposer.
        if (EnfantAVeiller(h, tous, maintenant) is { } enfant)
        {
            _autre = enfant;
            Avancer(h, enfant.X, enfant.Y, Activite.VeillerEnfant);
            return;
        }
        // En visite chez un proche, au camp d'un rassemblement, ou sur le chemin du retour.
        if (AllerAilleurs(h, tous, out var ailleurs)) return;
        // Un ouvrage demandé à plus habile que soi, qu'on attend ; un ouvrage qu'on vous demande.
        if (AttendreSonOuvrage(h, tous) || Servir(h, tous)) return;
        if (h.Desir >= SeuilDesir && ChercherAccouplement(h, partenaire, tous, maintenant)) return;

        // Un exilé quitte les terres de ceux qui l'ont chassé.
        if (Bannissement.Exil(h, maintenant) is > 0 and var exil && Social is not null && exil <= Social.Communautes.Count
            && Social.Communautes[exil - 1] is { Active: true } chasse
            && Distance(h.X, h.Y, chasse.CentreX, chasse.CentreY) <= VieSociale.DistanceCommunaute + 4)
        {
            int dx = Math.Sign(h.X - chasse.CentreX), dy = Math.Sign(h.Y - chasse.CentreY);
            if (dx == 0 && dy == 0) dx = h.Id % 2 == 0 ? 1 : -1;
            Avancer(h, Math.Clamp(h.X + 10 * dx, 0, carte.Largeur - 1), Math.Clamp(h.Y + 10 * dy, 0, carte.Hauteur - 1), Activite.PartirEnExil);
            if (h.CibleX >= 0) return;
        }
        if (!ailleurs && partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) > 2 + (1 - p.Sociabilite) * 8
            && !Bannissement.Separes(h, partenaire, maintenant))
        {
            _autre = partenaire;
            Avancer(h, partenaire.X, partenaire.Y, Activite.RejoindrePartenaire);
            return;
        }
        // On s'écarte de ceux qu'on ne supporte plus : c'est ainsi que les groupes trop tendus se scindent.
        if (RivalProche(h, tous) is { } rival && (h.Activite == Activite.EviterRival || alea.Reel() < 0.6 - 0.4 * p.Agreabilite))
        {
            _autre = rival;
            int dx = Math.Sign(h.X - rival.X), dy = Math.Sign(h.Y - rival.Y);
            if (dx == 0 && dy == 0) dx = 1;
            Avancer(h, Math.Clamp(h.X + 8 * dx, 0, carte.Largeur - 1), Math.Clamp(h.Y + 8 * dy, 0, carte.Hauteur - 1), Activite.EviterRival);
            if (h.CibleX >= 0) return;
        }
        // Une communauté en disette part vers de meilleures terres : sa figure et la plupart des membres suivent le mouvement.
        if (!ailleurs && Social?.CommunauteDe(h) is { DestinationX: >= 0 } groupe
            && Distance(h.X, h.Y, groupe.DestinationX, groupe.DestinationY) > 2 && h.Consigne(Parole.ResteIci, maintenant) is null
            && (h.Activite == Activite.Migrer || alea.Reel() < (h.Id == groupe.FigureId ? 0.8 : 0.5)))
        {
            if (h.Id == groupe.FigureId) _motif = Motif.Guide;
            Avancer(h, groupe.DestinationX, groupe.DestinationY, Activite.Migrer);
            if (h.CibleX >= 0) return;
        }
        // Les sociables vont retrouver ceux qu'ils aiment ; c'est ainsi que les groupes se forment.
        if (!abattu && !ailleurs && (h.Activite == Activite.RejoindreProche || alea.Reel() < 0.1 + 0.4 * p.Extraversion)
            && ProcheARejoindre(h, tous) is { } proche && !Bannissement.Separes(h, proche, maintenant))
        {
            _autre = proche;
            Avancer(h, proche.X, proche.Y, Activite.RejoindreProche);
            return;
        }
        if (!abattu && !ailleurs && AllerVoirLeFeu(h)) return;
        if (!ailleurs && ApporterViande(h, tous)) return;
        if (DemanderUnOuvrage(h, tous)) return;
        if (Outiller(h, tous)) return;
        if (Assembler(h, tous)) return;
        if (AvoirUneIdee(h, tous)) return;
        if (Enseigner(h, tous)) return;
        if (Experimenter(h, tous)) return;
        if (!ailleurs && alea.Reel() < (0.15 + 0.5 * p.Curiosite) * (abattu ? 0.3 : 1))
        {
            Explorer(h);
        }
        else
        {
            h.Activite = Activite.Repos;
            h.CibleX = h.CibleY = -1;
            // Au repos, l'endeuillé pense à celui qu'il a perdu.
            if (h.Chagrin > 30 && h.DeuilDeId > 0) _autre = tous[h.DeuilDeId - 1];
        }
    }

    /// <summary>
    /// Fuir un danger qu'on a remarqué : vers le lieu sûr le plus proche (en hauteur s'il s'agit d'eau), sinon droit
    /// devant, loin de la catastrophe. Contre la tempête, on cherche un abri. Les prudents réagissent les premiers ;
    /// devant une menace sourde (un volcan qui gronde, la mer qui se retire), beaucoup hésitent ou ne comprennent pas.
    /// </summary>
    private bool Fuir(Humain h, byte niveau, Catastrophe source)
    {
        var prudence = h.Personnalite.Prudence;
        if (h.Activite != Activite.Fuir)
        {
            var vigilance = source.Type switch
            {
                TypeCatastrophe.Tsunami when source.Phase == 0 => 0.03 + 0.25 * prudence,
                TypeCatastrophe.Eruption when source.Phase == 0 => 0.02 + 0.15 * prudence,
                _ => niveau == 2 ? 0.6 + 0.4 * prudence : 0.25 + 0.6 * prudence,
            };
            if (h.EstEnfant(_maintenant)) vigilance *= 0.7;
            if (alea.Reel() >= vigilance) return false;
        }
        _motif = source.Type switch
        {
            TypeCatastrophe.Incendie or TypeCatastrophe.Impact => Motif.Feu,
            TypeCatastrophe.Crue or TypeCatastrophe.Tsunami => Motif.Eau,
            TypeCatastrophe.Eruption => Motif.Volcan,
            TypeCatastrophe.Cyclone => Motif.Tempete,
            _ => Motif.Aucun,
        };
        if (source.Type == TypeCatastrophe.Cyclone)
        {
            if (AbriEn(h.X, h.Y) >= 0.4)
            {
                h.Activite = Activite.SAbriter;
                h.CibleX = h.CibleY = -1;
                return true;
            }
            var abri = Chercher(h, j => AbriEn(j) >= 0.4, _ => 0, rayonMax: 8, scoreMax: 0);
            if (abri is not { } a) return false;
            Avancer(h, a.X, a.Y, Activite.Fuir);
            return h.CibleX >= 0;
        }
        var eau = source.Type is TypeCatastrophe.Crue or TypeCatastrophe.Tsunami;
        var refuge = Chercher(h, j => !Catastrophes!.EstDangereuse(j), j => eau ? carte.Altitudes[j] * 50 : 0, rayonMax: 12);
        if (refuge is { } r)
        {
            Avancer(h, r.X, r.Y, Activite.Fuir);
            if (h.CibleX >= 0) return true;
        }
        // Rien de sûr en vue : s'éloigner de la catastrophe.
        int dx = Math.Sign(h.X - source.X), dy = Math.Sign(h.Y - source.Y);
        if (dx == 0 && dy == 0) (dx, dy) = (alea.Entier(2) * 2 - 1, alea.Entier(2) * 2 - 1);
        Avancer(h, Math.Clamp(h.X + 12 * dx, 0, carte.Largeur - 1), Math.Clamp(h.Y + 12 * dy, 0, carte.Hauteur - 1), Activite.Fuir);
        return h.CibleX >= 0;
    }

    /// <summary>Un humain hostile à moins de 3 cases, ou null.</summary>
    private Humain? RivalProche(Humain h, IReadOnlyList<Humain> tous)
    {
        foreach (var l in h.Liens)
        {
            if (!l.Rivaux) continue;
            var a = tous[l.Id - 1];
            // On ne quitte pas les siens à cause d'un enfant.
            if (a.EstVivant && !a.EstEnfant(_maintenant) && Distance(h.X, h.Y, a.X, a.Y) <= 3) return a;
        }
        return null;
    }

    /// <summary>L'humain aimé le plus attirant à rejoindre : affection forte, pas trop loin, mais pas déjà à côté.</summary>
    private Humain? ProcheARejoindre(Humain h, IReadOnlyList<Humain> tous)
    {
        Humain? choisi = null;
        var meilleur = double.MinValue;
        foreach (var l in h.Liens)
        {
            if (l.Affection < 30) continue;
            var a = tous[l.Id - 1];
            if (!a.EstVivant || a.EstBebe(_maintenant)) continue;
            var d = Distance(h.X, h.Y, a.X, a.Y);
            if (d <= 3 || d > 25) continue;
            var score = l.Affection - d;
            if (score > meilleur) (meilleur, choisi) = (score, a);
        }
        return choisi;
    }

    internal static string AgeEnMots(Humain h, long maintenant)
    {
        var age = h.AgeEnAnnees(maintenant);
        return age < 1 ? $"{Math.Max(0, (int)(age * 12))} mois" : $"{(int)age} an{(age >= 2 ? "s" : "")}";
    }

    /// <summary>Urgence d'un besoin : croît avec lui, et beaucoup plus vite au-delà de 70 (danger).</summary>
    private static void Plus(double u, Activite a, ref double valeur, ref Activite besoin)
    {
        if (u.CompareTo(valeur) > 0) (valeur, besoin) = (u, a);
    }

    private static double Urgence(double besoin) => besoin / 100 + (besoin > 70 ? (besoin - 70) / 30 * 0.8 : 0);

    private int RayonPerception(Humain h) =>
        h.EstEnfant(_maintenant) ? 5 : 6 + (int)(h.Personnalite.Curiosite * 8);

    private void AllerBoire(Humain h, Humain? recours = null)
    {
        if (EauDouceAPortee(h.X, h.Y) || Catastrophes?.DangerEn(h.X, h.Y) is { Source.Type: TypeCatastrophe.Crue })
        {
            h.Soif = 0;
            h.Activite = Activite.Boire;
            h.CibleX = h.CibleY = -1;
            h.Memoire.Retenir(TypeLieu.Eau, h.X, h.Y, _maintenant, Aptitude(h));
            if (EauDouceAPortee(h.X, h.Y)) Remplir(h, _tous);
            return;
        }
        if (BoireALaCache(h, _tous) || BoireALaCalebasse(h)) return;
        PenserALaCacheDEau(h, _tous);
        var cible = DistanceEau(h.X, h.Y) > RayonPerception(h) ? null
            : Chercher(h, i => EauDouceAPortee(i % carte.Largeur, i / carte.Largeur), _ => 0, scoreMax: 0);
        if (cible is { } c)
        {
            _motif = Motif.Vu;
            Avancer(h, c.X, c.Y, Activite.ChercherEau);
        }
        else if (!AllerALaReserve(h, true, _tous) && !AllerAuSouvenir(h, TypeLieu.Eau, Activite.ChercherEau) && !Retourner(h, recours))
        {
            _motif = Motif.Inconnu;
            Explorer(h, Activite.ChercherEau);
        }
    }

    /// <summary>
    /// Rien d'utile en vue : l'humain se rend au lieu de ce type le plus proche dont il se souvient.
    /// Arrivé sur place sans rien trouver, il est déçu : un souvenir faible s'efface, un souvenir solide s'affaiblit.
    /// </summary>
    private bool AllerAuSouvenir(Humain h, TypeLieu type, Activite activite)
    {
        while (h.Memoire.PlusProche(type, h.X, h.Y, _maintenant) is { } lieu)
        {
            if (Distance(h.X, h.Y, lieu.X, lieu.Y) <= 1)
            {
                h.Memoire.Decevoir(lieu, _maintenant); // déjà là, et rien : le souvenir s'affaiblit
                continue;
            }
            _motif = Motif.Souvenir;
            Avancer(h, lieu.X, lieu.Y, activite);
            if (h.CibleX < 0) h.Memoire.Oublier(lieu); // devenu inaccessible
            return h.CibleX >= 0;
        }
        return false;
    }

    private static double Aptitude(Humain h) => h.Personnalite.Intelligence.Memoire;

    private void AllerManger(Humain h, Humain? recours = null)
    {
        if (MangerSaViande(h)) return;
        var i = carte.Indice(h.X, h.Y);
        if (ecosysteme.Plantes[i] >= 1000)
        {
            // Le rendement de la cueillette suit la densité de ce qui pousse : une tuile riche nourrit vite,
            // une tuile déjà dépouillée demande bien plus de temps (théorie de l'approvisionnement optimal).
            var pris = Ecosysteme.Prelever(ecosysteme.Plantes, i, Math.Clamp(ecosysteme.Plantes[i] / 1000, 1, 8));
            h.Faim = Math.Max(0, h.Faim - pris * 2.5);
            h.Activite = Activite.Cueillir;
            h.CibleX = h.CibleY = -1;
            h.Memoire.Retenir(TypeLieu.Nourriture, h.X, h.Y, _maintenant, Aptitude(h));
            return;
        }
        if (ecosysteme.Gibier[i] >= 2000 && !h.EstEnfant(_maintenant))
        {
            // Chasse à mains nues : réussite rare, meilleure pour les adroits et là où le gibier abonde.
            var (arme, gain) = ArmeDeChasse(h);
            // Des compagnons qui parlent la même langue se coordonnent mieux (rabattre, encercler).
            // Sur une terre qui brûle, les bêtes débusquées fuient à découvert (Bird et al., 2005).
            var auFeu = TerreEnFeu(h);
            var chance = (0.1 + 0.3 * h.Personnalite.Adresse) * (1 + gain) * ecosysteme.Gibier[i] / 10_000.0 * (1 + 0.05 * h.CompagnonsDeLangue)
                         * (auFeu ? 2 : 1);
            h.Activite = Activite.Chasser;
            h.CibleX = h.CibleY = -1;
            if (alea.Reel() < chance)
            {
                // Une prise nourrit bien plus qu'une cueillette ; on n'en prélève que ce qu'on peut manger.
                var pris = Ecosysteme.Prelever(ecosysteme.Gibier, i, Math.Clamp((int)Math.Ceiling(h.Faim / ValeurGibier), 1, 5));
                var facteur = pris > 0 ? ApresLaChasse(h, arme, _tous) : 1;
                h.Faim = Math.Max(0, h.Faim - pris * ValeurGibier * facteur);
                h.Memoire.Retenir(TypeLieu.Nourriture, h.X, h.Y, _maintenant, Aptitude(h));
                // Une maladie de la bête (peut-être nouvelle) passe à l'humain après le pas.
                if (pris > 0 && Catastrophes is { } catastrophes) { var le = _maintenant; Tour.Differer(h, x => catastrophes.Prise(x, le)); }
                if (pris > 0) Rapporter(h, pris * KgParPart * facteur, _tous);
                if (pris > 0) GarderDeLaViande(h, pris * KgParPart * facteur);
                if (pris > 0 && auFeu) ChasseAuFeu(h, _tous);
            }
            else
            {
                h.Fatigue = Math.Min(100, h.Fatigue + 1);
                ApresUneChasseManquee(h, _tous);
            }
            return;
        }
        if (MangerDansLaReserve(h, _tous)) return;
        PenserAuxProvisions(h, _tous);
        if (Incendier(h, _tous)) return;
        var cible = Chercher(h,
            j => ecosysteme.Plantes[j] >= 1000 || ecosysteme.Gibier[j] >= 2000,
            j => ecosysteme.Plantes[j] + ecosysteme.Gibier[j] / 2);
        if (cible is { } c)
        {
            _motif = Motif.Vu;
            Avancer(h, c.X, c.Y, Activite.ChercherNourriture);
        }
        else if (!AllerALaReserve(h, false, _tous) && !AllerAuSouvenir(h, TypeLieu.Nourriture, Activite.ChercherNourriture) && !Retourner(h, recours))
        {
            _motif = Motif.Inconnu;
            Explorer(h, Activite.ChercherNourriture);
        }
    }

    /// <summary>Un enfant perdu revient vers celui qui veille sur lui (vrai s'il se met en route).</summary>
    private bool Retourner(Humain h, Humain? gardien)
    {
        if (gardien is null || Distance(h.X, h.Y, gardien.X, gardien.Y) <= 1) return false;
        _autre = gardien;
        Avancer(h, gardien.X, gardien.Y, Activite.SuivreParent);
        return h.CibleX >= 0;
    }

    /// <summary>Un petit enfant, près de celui qui veille sur lui, qui a besoin de boire, de manger ou de se réchauffer.</summary>
    private (Humain Petit, Activite Besoin)? PetitDansLeBesoin(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        var seuil = 0.3 + 0.4 * (1 - h.Personnalite.Attachement);
        var debut = maintenant - 7 * Calendrier.SecondesParAn;
        for (var i = tous.Count - 1; i >= 0; i--)
        {
            var e = tous[i];
            if (e.Naissance < debut) break;
            if (!e.EstVivant || e.EstBebe(maintenant) || !e.EstPetitEnfant(maintenant) || Distance(h.X, h.Y, e.X, e.Y) > 2) continue;
            if (e.MereId != h.Id && e.PereId != h.Id && Gardien(e, tous, maintenant)?.Id != h.Id) continue;
            // Le besoin le plus pressant (à égalité, le premier de la liste).
            var (valeur, besoin) = (Urgence(e.Soif) * 1.2, Activite.Boire);
            Plus(Urgence(e.Faim), Activite.Cueillir, ref valeur, ref besoin);
            Plus(Urgence(e.Froid), Activite.SAbriter, ref valeur, ref besoin);
            if (valeur >= seuil) return (e, besoin);
        }
        return null;
    }

    private void ResterPres(Humain h, Humain gardien, Activite? besoin)
    {
        _autre = gardien;
        // Assoiffé ou affamé, il se serre contre l'adulte, là où celui-ci boit ou cueille ; transi ou fatigué, il se blottit
        // contre lui pour se tenir chaud et dormir.
        var manque = besoin switch
        {
            Activite.Boire => !EauDouceAPortee(h.X, h.Y),
            Activite.Cueillir => ecosysteme.Plantes[carte.Indice(h.X, h.Y)] < 1000,
            Activite.SAbriter or Activite.Dormir => true,
            _ => false,
        };
        if (Distance(h.X, h.Y, gardien.X, gardien.Y) > (manque ? 0 : 1))
        {
            Avancer(h, gardien.X, gardien.Y, Activite.SuivreParent);
            if (h.CibleX >= 0) return;
        }
        h.CibleX = h.CibleY = -1;
        // En regardant l'adulte boire, cueillir ou s'abriter, l'enfant apprend où sont l'eau, la nourriture et les abris.
        // L'adulte qui comprend ce que l'enfant sait et ignore lui montre mieux (pédagogie naturelle ; Csibra et Gergely, 2009) :
        // l'enfant retient mieux.
        var apprend = Aptitude(h) + 20 * (gardien.Personnalite.Moral.EmpathieCognitive - 0.5);
        switch (gardien.Activite)
        {
            case Activite.Boire when EauDouceAPortee(h.X, h.Y): h.Memoire.Retenir(TypeLieu.Eau, h.X, h.Y, _maintenant, apprend); break;
            case Activite.Cueillir when ecosysteme.Plantes[carte.Indice(h.X, h.Y)] >= 1000:
                h.Memoire.Retenir(TypeLieu.Nourriture, h.X, h.Y, _maintenant, apprend); break;
            case Activite.Dormir or Activite.SAbriter when AbriEn(h.X, h.Y) >= 0.4:
                h.Memoire.Retenir(TypeLieu.Abri, h.X, h.Y, _maintenant, apprend); break;
        }
        switch (besoin)
        {
            case Activite.Boire when EauDouceAPortee(h.X, h.Y): AllerBoire(h); return;
            case Activite.Cueillir when ecosysteme.Plantes[carte.Indice(h.X, h.Y)] >= 1000: AllerManger(h); return;
            case Activite.Dormir: h.Activite = Activite.Dormir; return;
            case Activite.SAbriter: h.Activite = Activite.SAbriter; return;
        }
        h.Activite = Activite.Repos;
    }

    private void AllerDormir(Humain h, Humain? partenaire)
    {
        // Les prudents cherchent d'abord un abri, ou rejoignent l'autre pour dormir ensemble.
        if (AbriEn(h.X, h.Y) < 0.3 && Installations.FoyerEn(h.X, h.Y) is null && h.Fatigue < 85 && h.Personnalite.Prudence > 0.4)
        {
            if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) is > 0 and <= 4)
            {
                _autre = partenaire;
                Avancer(h, partenaire.X, partenaire.Y, h.EstEnfant(_maintenant) ? Activite.SuivreParent : Activite.RejoindrePartenaire);
                return;
            }
            var abri = Chercher(h, j => AbriEn(j) >= 0.4, _ => 0, rayonMax: 4, scoreMax: 0);
            if (abri is { } a)
            {
                Avancer(h, a.X, a.Y, Activite.ChercherAbri);
                return;
            }
        }
        h.Activite = Activite.Dormir;
        h.CibleX = h.CibleY = -1;
        if (AbriEn(h.X, h.Y) >= 0.4) h.Memoire.Retenir(TypeLieu.Abri, h.X, h.Y, _maintenant, Aptitude(h));
    }

    private void AllerSAbriter(Humain h, Humain? partenaire, long maintenant)
    {
        if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) is > 0 and <= 6)
        {
            _autre = partenaire;
            Avancer(h, partenaire.X, partenaire.Y, h.EstEnfant(maintenant) ? Activite.SuivreParent : Activite.RejoindrePartenaire);
            return;
        }
        // Chercher l'endroit le plus chaud à portée : plus bas, mieux abrité.
        int Confort(int i) => (int)((Temperature(i % carte.Largeur, i / carte.Largeur, maintenant)
                                     + AbriEn(i) * 12) * 1000);
        var ici = Confort(carte.Indice(h.X, h.Y));
        var mieux = Chercher(h, j => Confort(j) > ici + 1500, Confort);
        if (mieux is { } m) Avancer(h, m.X, m.Y, Activite.ChercherAbri);
        else
        {
            // Un abri connu pas trop loin (une demi-journée de marche) vaut le déplacement.
            var abri = h.Memoire.PlusProche(TypeLieu.Abri, h.X, h.Y, maintenant);
            if (abri is not null && Distance(h.X, h.Y, abri.X, abri.Y) is > 1 and <= 12
                && AbriEn(h.X, h.Y) < 0.4)
            {
                _motif = Motif.Souvenir;
                Avancer(h, abri.X, abri.Y, Activite.ChercherAbri);
                if (h.CibleX >= 0) return;
            }
            h.Activite = Activite.SAbriter;
            h.CibleX = h.CibleY = -1;
        }
    }

    private void Explorer(Humain h, Activite activite = Activite.Explorer)
    {
        // Garde la même direction tant que la destination d'exploration n'est pas atteinte.
        if (h.Activite != activite || h.CibleX < 0 || EstArrive(h))
        {
            var rayon = RayonPerception(h);
            h.CibleX = Math.Clamp(h.X + alea.Entier(2 * rayon + 1) - rayon, 0, carte.Largeur - 1);
            h.CibleY = Math.Clamp(h.Y + alea.Entier(2 * rayon + 1) - rayon, 0, carte.Hauteur - 1);
        }
        Avancer(h, h.CibleX, h.CibleY, activite);
    }

    /// <summary>Au plus deux cases (1,6 à 2,3 km) en dix minutes : au-delà, il faudrait courir longtemps.</summary>
    internal const int CasesParPasMax = 2;

    /// <summary>
    /// Marche vers la cible pendant le pas : l'humain dispose de dix minutes à sa vitesse du moment
    /// (<see cref="Marche.Vitesse"/>), et chaque case coûte sa longueur réelle allongée par le terrain et la pente.
    /// Ce qui ne suffit pas pour la case suivante est gardé pour le pas d'après. En terrain dégagé, on prend la case
    /// voisine qui rapproche le plus du but ; si un obstacle (côte, lac) bloque, on passe en mode détour : un chemin
    /// est calculé à chaque case jusqu'à l'arrivée.
    /// </summary>
    /// <summary>
    /// Nage d'une case vers la terre ferme la plus proche (dans un rayon de dix cases). L'épuisement et le froid rendent
    /// chaque brasse plus risquée. Renvoie faux s'il n'y a aucune rive en vue.
    /// </summary>
    private bool RegagnerLaRive(Humain h)
    {
        var depart = carte.Indice(h.X, h.Y);
        var parent = new Dictionary<int, int> { [depart] = depart };
        var file = new Queue<int>();
        file.Enqueue(depart);
        while (file.Count > 0)
        {
            var i = file.Dequeue();
            int x = i % carte.Largeur, y = i / carte.Largeur;
            if (i != depart && Climat.EstPraticable((Biome)carte.Biomes[i]))
            {
                while (parent[i] != depart) i = parent[i];
                h.X = i % carte.Largeur;
                h.Y = i / carte.Largeur;
                h.Activite = Activite.Nager;
                h.CibleX = h.CibleY = -1;
                h.Fatigue = Math.Min(100, h.Fatigue + 3);
                Baignade(h, false);
                return true;
            }
            if (Distance(x, y, h.X, h.Y) >= 10) continue;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = carte.Indice(nx, ny);
                var b = carte.BiomeEn(nx, ny);
                if (parent.ContainsKey(j) || b == Biome.Ocean) continue;
                parent[j] = i;
                file.Enqueue(j);
            }
        }
        return false;
    }

    private void Avancer(Humain h, int cibleX, int cibleY, Activite activite)
    {
        if (h.CibleX != cibleX || h.CibleY != cibleY) h.Detour = false;
        h.Activite = activite;
        h.CibleX = cibleX;
        h.CibleY = cibleY;
        var budget = h.Progression + Marche.Vitesse(h, _maintenant) * MondeSimule.SecondesParPas;
        h.Progression = 0;
        for (var n = 0; n < CasesParPasMax; n++)
        {
            if (EstArrive(h))
            {
                h.Detour = false;
                return;
            }

            var (bx, by) = h.Detour ? (-1, -1) : PasDirect(h.X, h.Y, cibleX, cibleY);
            if (bx < 0)
            {
                var pas = PremierPasDuChemin(carte.Indice(h.X, h.Y), carte.Indice(cibleX, cibleY));
                if (pas < 0)
                {
                    // L'eau barre la route : au bord, on peut avoir l'idée (ou refaire) de quoi la traverser.
                    if (!_nageur && Traverser(h, _tous)) return;
                    // Inaccessible : on abandonne cette destination.
                    h.CibleX = h.CibleY = -1;
                    h.Detour = false;
                    return;
                }
                h.Detour = true;
                (bx, by) = (pas % carte.Largeur, pas / carte.Largeur);
            }

            var effort = Marche.Effort(carte, h.X, h.Y, bx, by);
            if (budget < effort)
            {
                h.Progression = budget;
                return;
            }
            budget -= effort;
            h.X = bx;
            h.Y = by;
            if (!Climat.EstPraticable(carte.BiomeEn(bx, by)) && Radeau(h) is { } radeau)
            {
                // Il traverse sur son radeau.
                h.Objets[h.Objets.IndexOf(radeau)] = radeau with { Plein = true };
                h.Fatigue = Math.Min(100, h.Fatigue + 1);
            }
            else if (!Climat.EstPraticable(carte.BiomeEn(bx, by)))
            {
                // Il traverse à la nage.
                Baignade(h, true);
                h.Fatigue = Math.Min(100, h.Fatigue + 3);
                if (!h.EstVivant) return;
            }
            if (carte.BiomeEn(bx, by) is Biome.Montagne or Biome.SommetEnneige) h.Fatigue = Math.Min(100, h.Fatigue + 0.8);
        }
    }

    /// <summary>Arrivé : sur la cible, ou à côté si la cible elle-même est de l'eau.</summary>
    private bool EstArrive(Humain h) =>
        (h.X == h.CibleX && h.Y == h.CibleY)
        || (!Climat.EstPraticable(carte.BiomeEn(h.CibleX, h.CibleY)) && Distance(h.X, h.Y, h.CibleX, h.CibleY) <= 1);

    private (int X, int Y) PasDirect(int x, int y, int cibleX, int cibleY)
    {
        var meilleure = Distance2(x, y, cibleX, cibleY);
        var (bx, by) = (-1, -1);
        var egalites = 0;
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            if (!Praticable(carte.BiomeEn(nx, ny))) continue;
            var d = Distance2(nx, ny, cibleX, cibleY);
            if (d < meilleure) (meilleure, bx, by, egalites) = (d, nx, ny, 1);
            else if (d == meilleure && bx >= 0 && alea.Entier(++egalites) == 0) (bx, by) = (nx, ny);
        }
        return (bx, by);
    }

    /// <summary>
    /// Plus court chemin (parcours en largeur, 8 directions) vers la cible ou une case voisine d'elle ;
    /// renvoie la première case du chemin, ou -1 si la cible est hors de portée.
    /// </summary>
    // Tampons des parcours en largeur, réutilisés : une case est « vue » si elle porte le numéro du parcours en cours.
    [ThreadStatic] private static int[]? _vus;
    [ThreadStatic] private static int[]? _parents;
    [ThreadStatic] private static int _generation;
    [ThreadStatic] private static Queue<(int Indice, int Distance)>? _file;

    private int NouveauParcours()
    {
        var n = carte.Largeur * carte.Hauteur;
        if (_vus is null || _vus.Length != n) { _vus = new int[n]; _parents = new int[n]; _generation = 0; _file ??= new(); }
        if (++_generation == int.MaxValue) { Array.Clear(_vus!); _generation = 1; }
        return _generation;
    }

    private int PremierPasDuChemin(int depart, int cible)
    {
        const int RayonMax = 40;
        int cx = cible % carte.Largeur, cy = cible / carte.Largeur;
        var generation = NouveauParcours();
        var (vus, parent, file) = (_vus!, _parents!, _file!);
        vus[depart] = generation;
        parent[depart] = depart;
        file.Clear();
        file.Enqueue((depart, 0));
        while (file.Count > 0)
        {
            var (i, d) = file.Dequeue();
            int x = i % carte.Largeur, y = i / carte.Largeur;
            if (i != depart && Distance(x, y, cx, cy) <= (Praticable((Biome)carte.Biomes[cible]) ? 0 : 1))
            {
                while (parent[i] != depart) i = parent[i];
                return i;
            }
            if (d >= RayonMax) continue;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = carte.Indice(nx, ny);
                if (vus[j] == generation || !Praticable((Biome)carte.Biomes[j])) continue;
                vus[j] = generation;
                parent[j] = i;
                file.Enqueue((j, d + 1));
            }
        }
        return -1;
    }

    /// <summary>
    /// Parcours en largeur autour de l'humain (dans son rayon de perception) ; renvoie la case
    /// qui satisfait <paramref name="convient"/> avec le meilleur score, en pénalisant la distance.
    /// </summary>
    private (int X, int Y)? Chercher(Humain h, Func<int, bool> convient, Func<int, int> score, int? rayonMax = null, int? scoreMax = null)
    {
        var rayon = rayonMax ?? RayonPerception(h);
        var debut = carte.Indice(h.X, h.Y);
        var generation = NouveauParcours();
        var (vus, file) = (_vus!, _file!);
        vus[debut] = generation;
        file.Clear();
        file.Enqueue((debut, 0));
        (int X, int Y)? meilleur = null;
        var meilleurScore = int.MinValue;
        var egalites = 0;

        while (file.Count > 0)
        {
            var (i, d) = file.Dequeue();
            // Plus loin, aucune case ne peut plus égaler la meilleure : le parcours s'arrête (même résultat, tirages compris).
            if (scoreMax is { } plafond && meilleur is not null && plafond - d * 300 < meilleurScore) break;
            if (i != debut && convient(i))
            {
                var s = score(i) - d * 300;
                if (s > meilleurScore)
                {
                    (meilleurScore, meilleur, egalites) = (s, (i % carte.Largeur, i / carte.Largeur), 1);
                }
                else if (s == meilleurScore && alea.Entier(++egalites) == 0)
                {
                    // Égalité : tirage équitable, sinon le sens du parcours favoriserait toujours le nord-ouest.
                    meilleur = (i % carte.Largeur, i / carte.Largeur);
                }
            }
            if (d >= rayon) continue;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = carte.Indice(nx, ny);
                if (vus[j] == generation) continue;
                vus[j] = generation;
                // L'eau non praticable peut être une cible (on boit depuis la rive) mais on ne la traverse pas.
                if (Praticable((Biome)carte.Biomes[j])) file.Enqueue((j, d + 1));
            }
        }
        return meilleur;
    }

    /// <summary>Reprend le calcul hors des tours (après un pas simultané) : les effets mis de côté voient la population à jour.</summary>
    internal void Reprendre(IReadOnlyList<Humain> tous, long maintenant)
    {
        _tous = tous;
        _maintenant = maintenant;
        _nageur = false;
    }

    /// <summary>Calcule d'avance ce que les humains liront en même temps pendant un pas simultané.</summary>
    internal void Preparer()
    {
        EauDouceAPortee(0, 0);
        DistanceEau(0, 0);
    }

    // Les biomes ne changent pas : l'eau douce à portée de chaque case est calculée une fois.
    private bool[]? _eauAPortee;

    private bool EauDouceAPortee(int x, int y)
    {
        if (_eauAPortee is null)
        {
            var eau = new bool[carte.Largeur * carte.Hauteur];
            for (var j = 0; j < eau.Length; j++) eau[j] = CalculerEauDouceAPortee(j % carte.Largeur, j / carte.Largeur);
            _eauAPortee = eau;
        }
        return _eauAPortee[carte.Indice(x, y)];
    }

    // Distance (en cases, à vol d'oiseau) de chaque case à la plus proche où l'on peut boire : au-delà du regard, inutile de chercher.
    private int[]? _distanceEau;

    private int DistanceEau(int x, int y)
    {
        if (_distanceEau is null)
        {
            int l = carte.Largeur, n = l * carte.Hauteur;
            var dist = new int[n];
            Array.Fill(dist, int.MaxValue);
            var file = new Queue<int>();
            for (var j = 0; j < n; j++)
            {
                if (EauDouceAPortee(j % l, j / l)) { dist[j] = 0; file.Enqueue(j); }
            }
            while (file.Count > 0)
            {
                var i = file.Dequeue();
                int ix = i % l, iy = i / l;
                foreach (var (dx, dy) in Voisins8)
                {
                    int nx = ix + dx, ny = iy + dy;
                    if (nx < 0 || ny < 0 || nx >= l || ny >= carte.Hauteur) continue;
                    var j = carte.Indice(nx, ny);
                    if (dist[j] != int.MaxValue) continue;
                    dist[j] = dist[i] + 1;
                    file.Enqueue(j);
                }
            }
            _distanceEau = dist;
        }
        return _distanceEau[carte.Indice(x, y)];
    }

    private bool CalculerEauDouceAPortee(int x, int y)
    {
        if (Biomes.EstEauDouce(carte.BiomeEn(x, y))) return true;
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < carte.Largeur && ny < carte.Hauteur && Biomes.EstEauDouce(carte.BiomeEn(nx, ny)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Humains du pas en cours (pour l'imitation pendant les gestes).</summary>
    [ThreadStatic] private static IReadOnlyList<Humain>? _tousDuFil;

    private static IReadOnlyList<Humain> _tous { get => _tousDuFil ?? []; set => _tousDuFil = value; }

    /// <summary>L'humain en cours sait nager et en a la force : l'eau des lacs et des côtes ne l'arrête plus.</summary>
    [ThreadStatic] private static bool _nageur;

    private bool Praticable(Biome b) => Climat.EstPraticable(b) || _nageur && b is Biome.Lac or Biome.EauPeuProfonde;

    /// <summary>Un feu réchauffe autant qu'un bon abri : on dort à côté sans geler.</summary>
    internal const double ChaleurDuFeu = 0.6;

    /// <summary>Protection d'une case contre le froid : le terrain, ou une hutte.</summary>
    private double AbriEn(int x, int y) => Math.Max(Climat.Abri(carte.BiomeEn(x, y)), Installations.AbriEn(x, y));

    private double AbriEn(int i) => AbriEn(i % carte.Largeur, i / carte.Largeur);

    /// <summary>Ce que le vêtement retient de chaleur : une peau bien préparée beaucoup, des feuilles peu.</summary>
    private static double Couverture(Humain h) => h.Vetement is { } v ? v.Qualite * (0.5 + 0.5 * v.Usure) * (v.Matiere == Matiere.Peau ? 0.75 : 0.5) : 0;

    private static int Distance(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    private static int Distance2(int x1, int y1, int x2, int y2) => (x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2);
}
