using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Reproduction, grossesse, bébés et vieillesse. La reproduction est un instinct : les humains
/// ne savent pas que l'accouplement mène à une naissance neuf mois plus tard.
/// </summary>
public sealed partial class Comportement
{
    private const double DesirParPas = 0.05;          // le désir culmine en deux semaines environ
    private const double SeuilDesir = 50;

    // Valeurs de départ tirées des études sur les populations sans médecine moderne. Chaque humain les module
    // par sa biologie propre (héritée), son âge, sa nutrition et sa santé du moment.

    /// <summary>
    /// Conception par accouplement d'une femme de 20-24 ans bien nourrie, de fertilité moyenne : réglée pour
    /// donner environ 20 % de conceptions par cycle (fécondabilité, Wood 1994).
    /// </summary>
    private const double ConceptionParAccouplement = 0.12;

    /// <summary>Durée de grossesse depuis la conception : 268 jours en moyenne, écart-type 10 jours (Jukic et al. 2013).</summary>
    private const double JoursDeGrossesseMoyens = 268;
    private const double EcartJoursDeGrossesse = 10;

    /// <summary>Avant 37 semaines (259 jours), la naissance est prématurée et plus risquée.</summary>
    private const double JoursAvantPrematurite = 259;

    /// <summary>Morts-nés : 30 à 50 pour 1000 dans les sociétés préindustrielles (Woods 2009).</summary>
    private const double RisqueMortNe = 0.035;

    /// <summary>Mort de la mère : environ 1 % par accouchement avant la médecine moderne (Loudon 1992).</summary>
    private const double RisqueMortAccouchement = 0.01;

    /// <summary>Les fausses couches surviennent surtout pendant les 20 premières semaines.</summary>
    private const double JoursRisqueFausseCouche = 140;

    private const long PasParJour = Calendrier.SecondesParJour / MondeSimule.SecondesParPas;

    /// <summary>
    /// Mortalité annuelle « de fond » selon l'âge : ce que la simulation ne fait pas mourir autrement. Chez les petits,
    /// les causes néonatales et ce qui n'est pas modélisé ; chez l'adulte jeune, presque rien, car ses morts (infections,
    /// accouchements, accidents, violences, faim, soif, froid) sont déjà simulées une à une et s'y ajouteraient ; passé
    /// 50 ans, l'usure du corps. Bornes d'âge en années et taux annuel, du plus jeune au plus âgé. Mesuré sur 60 ans, la
    /// mortalité totale approche celle des chasseurs-cueilleurs (Gurven et Kaplan, 2007).
    /// </summary>
    private static readonly (double AgeMax, double TauxAnnuel)[] MortaliteParAge =
    [
        (1, 0.10), (5, 0.03), (15, 0.01), (30, 0.001), (40, 0.002),
        (50, 0.008), (60, 0.04), (70, 0.12), (80, 0.30), (double.MaxValue, 0.50),
    ];

    /// <summary>Naissances survenues pendant le pas : le monde crée les nouveau-nés après le pas.</summary>
    public List<(Humain Mere, int PereId)> Accouchements { get; } = [];

    private bool ChercherAccouplement(Humain h, Humain? partenaire, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstFecond(maintenant)) return false;
        var ideal = Social?.IdealDe(h) ?? IdealBeaute.ParDefaut;
        var exigence = Attirance.Exigence(h, ideal, maintenant);

        // Son compagnon, s'il est là et disposé : on s'unit à lui d'autant plus volontiers qu'on est attiré par son sexe.
        if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) <= 1 && EstDispose(partenaire, maintenant)
            && alea.Reel() < 0.3 + 0.7 * h.Orientation.Vers(partenaire.Sexe))
        {
            _autre = partenaire;
            SAccoupler(h, partenaire, tous, maintenant);
            return true;
        }

        // Sinon, celui ou celle qui lui plaît le plus parmi ceux qui sont tout près : l'attrait physique pèse plus que tout le reste.
        Humain? prefere = null;
        double meilleur = 0;
        foreach (var autre in tous.Vivants())
        {
            if (autre.Id == h.Id || autre.Id == partenaire?.Id || !autre.EstVivant || autre.EstEnfant(maintenant)) continue;
            if (Distance(h.X, h.Y, autre.X, autre.Y) > 1 || !EstDispose(autre, maintenant) || Interdit(h, autre, maintenant)) continue;
            if (h.AffectionPour(autre.Id) <= VieSociale.SeuilHostilite || Bannissement.Separes(h, autre, maintenant)) continue;
            if (CompagnonVeille(autre, h, tous)) continue;
            var desir = Attirance.Desir(h, autre, ideal, maintenant);
            // Qui nous a repoussé, on ne le sollicite plus de quelque temps.
            if (desir >= exigence && desir > meilleur && !Recent(h, $"refus:{autre.Id}", 90, maintenant)) (prefere, meilleur) = (autre, desir);
        }
        // Un désir refoulé laisse l'humain libre d'aller vers son compagnon.
        if (prefere is not null && Proposer(h, prefere, meilleur, tous, maintenant))
        {
            _autre = prefere;
            return true;
        }

        // Désir fort : aller vers son compagnon, ou vers celui ou celle qui lui plaît le plus alentour si l'on est seul.
        if (h.Desir < 70) return false;
        var cible = partenaire;
        if (cible is null)
        {
            var rayon = RayonPerception(h);
            var ose = Ose(h);
            double score = 0;
            foreach (var a in tous.Vivants())
            {
                if (a.Id == h.Id || !a.EstVivant || !a.EstFecond(maintenant) || Interdit(h, a, maintenant)) continue;
                var d = Distance(h.X, h.Y, a.X, a.Y);
                if (d > rayon || h.AffectionPour(a.Id) <= VieSociale.SeuilHostilite || Bannissement.Separes(h, a, maintenant)) continue;
                if (CompagnonVeille(a, h, tous)) continue;
                var desir = Attirance.Desir(h, a, ideal, maintenant);
                if (desir < exigence || Recent(h, $"refus:{a.Id}", 90, maintenant)) continue;
                // On ne va vers quelqu'un de son sexe que si l'on ose ; plus il est loin, moins on y va.
                var s = desir * (a.Sexe == h.Sexe ? ose : 1) / (1 + 0.1 * d);
                if (s > score) (cible, score) = (a, s);
            }
        }
        if (cible is null || Distance(h.X, h.Y, cible.X, cible.Y) == 0) return false;
        (_autre, _motif) = (cible, Motif.Desir);
        Avancer(h, cible.X, cible.Y, Activite.RejoindrePartenaire);
        return true;
    }

    /// <summary>
    /// On ne fait pas d'avances à quelqu'un dont le compagnon est tout près : partout, les compagnons veillent l'un sur l'autre
    /// et écartent les rivaux (Buss, 2002, sur la surveillance du partenaire).
    /// </summary>
    private static bool CompagnonVeille(Humain autre, Humain h, IReadOnlyList<Humain> tous) =>
        Vivant(tous, autre.PartenaireId) is { } c && c.Id != h.Id && c.Activite != Activite.Dormir && Distance(c.X, c.Y, autre.X, autre.Y) <= 2;

    /// <summary>
    /// Envie de <paramref name="h"/> de s'unir à <paramref name="autre"/>, une fois le désir là. Seul (ou avec son propre
    /// compagnon), sans réserve. En couple avec un autre, rarement : les relations hors du couple restent minoritaires dans
    /// les sociétés étudiées (quelques pour cent des naissances en moyenne ; Anderson, 2006), avec de grands écarts d'une
    /// société à l'autre (Scelza et al., 2020). Elles sont plus probables quand le compagnon est loin, moins fréquentes chez
    /// les consciencieux (Schmitt, 2004), chez ceux qui sont attachés à leur compagnon (Allen et al., 2005) et là où
    /// l'usage les réprouve.
    /// </summary>
    private double Ouverture(Humain h, Humain autre, double desir, IReadOnlyList<Humain> tous)
    {
        var compagnon = Vivant(tous, h.PartenaireId);
        if (compagnon is null || compagnon.Id == autre.Id) return 1;
        // L'Entité peut pousser vers un autre (un penchant, « Unis-toi avec… ») ou retenir (« Reste avec… »).
        var entite = 1.0;
        if (h.Impulsions.Count > 0)
        {
            entite = 1 + 15 * h.Penchant(autre.Id, _maintenant) + (h.Impulsions.Exists(i => i.Parole == Parole.UnisToi && i.CibleId == autre.Id && i.Active(_maintenant)) ? 30 : 0);
            if (h.Consigne(Parole.ResteAvec, _maintenant) is { } reste && reste.CibleId == compagnon.Id) entite *= 1 - 0.8 * reste.Force;
        }
        var occasion = Distance(h.X, h.Y, compagnon.X, compagnon.Y) > 8 ? 0.04 : 0.01;
        // Plus fréquentes aussi chez les évitants (DeWall et al., 2011), les peu honnêtes et les psychopathes (Lee et Ashton, 2012).
        var m = h.Personnalite.Moral;
        return Math.Min(1, entite * (occasion + 0.02 * desir) * (0.4 + 0.6 * (Social?.ToleranceDe(h) ?? 0.5)) * (1.4 - 0.8 * h.Personnalite.Conscience)
               * Math.Clamp(1.2 - h.AffectionPour(compagnon.Id) / 100, 0.2, 1.2)
               * (0.6 + 0.8 * (1 - m.Honnetete)) * (0.8 + 0.4 * m.EvitementAffectif) * (1 + Psyche.Froideur(h.Personnalite)));
    }

    private static bool EstDispose(Humain a, long maintenant) =>
        a.EstVivant && a.EstFecond(maintenant) && a.Activite != Activite.Dormir && a.Desir >= 25
        && a.Faim < 80 && a.Soif < 80;

    /// <summary>Événement de l'Entité qui a poussé l'un vers l'autre (penchant ou « Unis-toi avec… » encore actifs), 0 sinon.</summary>
    private static long CauseEntite(Humain a, Humain b, long maintenant)
    {
        foreach (var (x, y) in new[] { (a, b), (b, a) })
            foreach (var i in x.Impulsions)
                if (i.CibleId == y.Id && i.Parole is Parole.Penchant or Parole.UnisToi && i.Active(maintenant)) return i.EvenementId;
        return 0;
    }

    /// <summary>Un parent, que l'on ne désire pas, sauf si l'Entité a vaincu ce tabou en lui.</summary>
    private static bool Interdit(Humain h, Humain autre, long maintenant) =>
        SontParents(h, autre) && !(h.Impulsions.Count > 0 && h.CedeA(autre.Id, maintenant));

    /// <summary>Parents et enfants, frères et sœurs : élevés ensemble, ils ne s'attirent pas (instinct).</summary>
    private static bool SontParents(Humain a, Humain b) =>
        a.MereId == b.Id || a.PereId == b.Id || b.MereId == a.Id || b.PereId == a.Id
        || (a.MereId != 0 && a.MereId == b.MereId)
        || (a.PereId != 0 && a.PereId == b.PereId);

    private void SAccoupler(Humain a, Humain b, IReadOnlyList<Humain> tous, long maintenant)
    {
        // Pendant un pas simultané, l'union se fait après le pas, si tous deux vivent encore.
        if (Tour.Autrui(b))
        {
            a.Activite = Activite.SAccoupler;
            a.CibleX = a.CibleY = -1;
            a.Desir = 0;
            var (ia, ib) = (a.Id, b.Id);
            Tour.Differer(tous, t =>
            {
                if (t[ia - 1].EstVivant && t[ib - 1].EstVivant) SAccoupler(t[ia - 1], t[ib - 1], t, maintenant);
            });
            return;
        }
        a.Activite = b.Activite = Activite.SAccoupler;
        a.CibleX = a.CibleY = b.CibleX = b.CibleY = -1;
        a.Desir = b.Desir = 0;
        if (a.Sexe == b.Sexe)
        {
            SAimer(a, b, tous, maintenant);
            return;
        }

        var femme = a.Sexe == Sexe.Femme ? a : b;
        var homme = a.Sexe == Sexe.Femme ? b : a;
        var infidele = Vivant(tous, a.PartenaireId) is { } pa && pa.Id != b.Id || Vivant(tous, b.PartenaireId) is { } pb && pb.Id != a.Id;
        var hors = infidele ? " (hors de leur couple)" : "";
        // Un penchant inspiré ou un ordre de l'Entité y a mené : il en est la cause.
        var accouplement = Noter(maintenant, TypeEvenement.Accouplement, femme, homme, $"{femme.Nom} et {homme.Nom} s'accouplent{hors}.",
            CauseEntite(a, b, maintenant), karma: PointsInfidelite(femme, homme, tous), karmaAutre: PointsInfidelite(homme, femme, tous));
        if (infidele)
        {
            Remords(a, b, accouplement, tous, maintenant);
            Remords(b, a, accouplement, tous, maintenant);
            Ebruiter(a, b, accouplement, tous, maintenant);
        }
        if (alea.Reel() < ChanceConception(femme, homme, tous, maintenant))
        {
            var jours = Math.Clamp(JoursDeGrossesseMoyens + Biologie.Normale(alea) * EcartJoursDeGrossesse, 230, 300);
            femme.EnceinteDepuis = maintenant;
            femme.TermeGrossesse = maintenant + (long)(jours * Calendrier.SecondesParJour);
            femme.PereEnfantId = homme.Id;
            Noter(maintenant, TypeEvenement.Grossesse, femme, homme,
                $"{femme.Nom} attend un enfant de {homme.Nom}. Elle ne le sait pas encore.", accouplement);
        }

        // Deux humains seuls qui s'accouplent forment un couple.
        if (Vivant(tous, a.PartenaireId) is null && Vivant(tous, b.PartenaireId) is null)
        {
            a.PartenaireId = b.Id;
            b.PartenaireId = a.Id;
            Noter(maintenant, TypeEvenement.Union, femme, homme, $"{femme.Nom} et {homme.Nom} forment un nouveau couple.", accouplement);
            VieSociale.Rejouir(a, 10);
            VieSociale.Rejouir(b, 10);
        }
    }

    /// <summary>
    /// Celui qui a trompé son compagnon le garde pour lui : la honte chez qui ressent la peine qu'il causerait,
    /// rien qu'un secret bien gardé chez le psychopathe, sans culpabilité (Hare, 1993).
    /// </summary>
    private void Remords(Humain h, Humain complice, long accouplement, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Vivant(tous, h.PartenaireId) is not { } compagnon || compagnon.Id == complice.Id) return;
        var cle = $"infidelite:{compagnon.Id}";
        if (Recent(h, cle, 60, maintenant)) return;
        if (Psyche.EstPsychopathe(h, maintenant))
        {
            Confier(h, TypeIntime.Intention, compagnon.Id, $"{compagnon.Nom} n'en saura rien. Et quand bien même.", 35, accouplement, maintenant, cle);
            return;
        }
        var m = h.Personnalite.Moral;
        var honte = 100 * Math.Clamp(0.25 * m.EmpathieEmotionnelle + 0.25 * m.Honnetete + 0.3 * Math.Max(0, h.AffectionPour(compagnon.Id)) / 100
                                     + 0.2 * m.Fondements.Loyaute, 0, 1) * (1 - Psyche.Froideur(h.Personnalite));
        if (honte < 25) return;
        Confier(h, TypeIntime.Peine, compagnon.Id, $"J'ai trahi {compagnon.Nom} avec {complice.Nom}. J'en ai honte.", honte, accouplement, maintenant, cle);
        Tour.Changer(h, x => x.Humeur = Math.Max(0, x.Humeur - honte / 10));
    }

    /// <summary>
    /// Probabilité qu'un accouplement mène à une grossesse : fécondité des deux selon l'âge et la biologie,
    /// réduite par la faim, la maladie et l'allaitement.
    /// </summary>
    internal double ChanceConception(Humain femme, Humain homme, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (femme.EstEnceinte) return 0;
        // L'ovulation dépend de l'énergie disponible : des réserves basses ou une faim qui dure la bloquent
        // (Ellison, 2001 ; Valeggia et Ellison, 2009). Chez les chasseurs-cueilleurs, les conceptions suivent ainsi
        // les saisons d'abondance, et les naissances s'espacent quand la nourriture manque.
        var reserves = Math.Clamp((femme.Reserves - 10) / 30, 0, 1);
        var nutrition = reserves * Math.Clamp(1 - Math.Max(0, femme.Faim - 50) / 35, 0, 1) * Math.Clamp(femme.Sante / 100, 0, 1);
        return ConceptionParAccouplement * femme.Fecondite(maintenant) * homme.Fecondite(maintenant)
               * nutrition * Allaitement(femme, tous, maintenant);
    }

    /// <summary>
    /// L'allaitement à la demande, jusque vers 3 ans, rend presque infertile pendant un an et demi, puis de moins en moins :
    /// chez les chasseurs-cueilleurs, les naissances sont espacées de 3 à 4 ans (Konner et Worthman 1980 ; Hill et Hurtado 1996).
    /// Si l'enfant meurt ou est éloigné de sa mère, la fécondité revient.
    /// </summary>
    internal static double Allaitement(Humain mere, IReadOnlyList<Humain> tous, long maintenant)
    {
        var debut = maintenant - 3 * Calendrier.SecondesParAn;
        for (var i = tous.Count - 1; i >= 0; i--)
        {
            var e = tous[i];
            if (e.Naissance < debut) break; // les identifiants suivent l'ordre des naissances
            if (e.MereId != mere.Id || !e.EstVivant) continue;
            var age = e.AgeEnAnnees(maintenant);
            var allaite = age < 2
                ? e.Activite is Activite.PorteParSaMere or Activite.Dormir
                : Distance(e.X, e.Y, mere.X, mere.Y) <= 1;
            if (!allaite) continue;
            return age < 1.5 ? 0.02 : 0.02 + 0.4 * (age - 1.5) / 1.5;
        }
        return 1;
    }

    /// <summary>Faim et mauvaise santé de la mère aggravent les risques de la grossesse et de l'accouchement.</summary>
    private static double Detresse(Humain mere) =>
        1 + Math.Max(0, mere.Faim - 60) / 40 + Math.Max(0, 60 - mere.Sante) / 40 + Math.Max(0, 20 - mere.Reserves) / 20;

    private static double FacteurAgeMere(double age) => age switch
    {
        < 18 => 1.5,
        < 35 => 1,
        < 40 => 1.5,
        _ => 2,
    };

    private void Grossesse(Humain mere, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (mere.EnceinteDepuis is not { } debut) return;
        var age = mere.AgeEnAnnees(maintenant);

        // Fausse couche : risque selon l'âge, réparti sur les premières semaines.
        if (maintenant - debut < JoursRisqueFausseCouche * Calendrier.SecondesParJour)
        {
            var risqueParPas = Biologie.RisqueFausseCouche(age) * Detresse(mere) / (JoursRisqueFausseCouche * PasParJour);
            if (alea.Reel() < risqueParPas)
            {
                TerminerGrossesse(mere, maintenant);
                Noter(maintenant, TypeEvenement.FausseCouche, mere, null, $"{mere.Nom} perd l'enfant qu'elle portait.");
            }
            return;
        }

        var terme = mere.TermeGrossesse ?? debut + (long)(JoursDeGrossesseMoyens * Calendrier.SecondesParJour);
        if (maintenant < terme) return;

        var premature = terme - debut < JoursAvantPrematurite * Calendrier.SecondesParJour;
        var parite = NombreDEnfants(mere, tous);
        var detresse = Detresse(mere);
        // Jumeaux : environ 1 naissance sur 80, un peu plus vers 35 ans.
        var jumeaux = alea.Reel() < 0.004 + 0.008 * (age is >= 30 and < 40 ? 1.5 : 1);
        // Des femmes (ou des proches) qui ont l'expérience des naissances aident : moins de risque pour la mère, un peu pour l'enfant.
        var aide = AiderAccouchement(mere, tous, maintenant);
        var risqueMortNe = RisqueMortNe * FacteurAgeMere(age) * detresse * (premature ? 2 : 1) * (jumeaux ? 2 : 1) * (1 - aide / 2) * Adouci;
        for (var n = jumeaux ? 2 : 1; n > 0; n--)
        {
            if (alea.Reel() >= risqueMortNe) { var pere = mere.PereEnfantId; Tour.Differer(mere, m => Accouchements.Add((m, pere))); }
            else Noter(maintenant, TypeEvenement.MortNe, mere, null, $"{mere.Nom} met au monde un enfant mort-né.");
        }

        TerminerGrossesse(mere, maintenant);
        mere.DernierAccouchement = maintenant;
        mere.Fatigue = 100;
        var risqueMere = RisqueMortAccouchement * FacteurAgeMere(age) * detresse
                         * (parite == 0 ? 1.5 : parite >= 5 ? 1.3 : 1) * (jumeaux ? 1.5 : 1) * (1 - aide) * Adouci;
        if (alea.Reel() < risqueMere) Mourir(mere, maintenant, CauseDeMort.Accouchement);
    }

    /// <summary>
    /// Ceux qui sont là au moment de l'accouchement, proches de la mère, l'aident (presque partout, la mère accouche entourée :
    /// Rosenberg et Trevathan, 2002). Les plus expérimentées (enfants mis au monde, accouchements aidés) mènent. Renvoie la part
    /// du risque de mourir en couches qu'elles écartent : de 10 % pour une présence novice à 45 % au plus (estimation, à mesurer).
    /// </summary>
    internal double AiderAccouchement(Humain mere, IReadOnlyList<Humain> tous, long maintenant)
    {
        var aides = new List<(Humain H, double Experience)>();
        foreach (var o in tous.Vivants())
        {
            if (o.Id == mere.Id || !o.EstVivant || o.AgeEnAnnees(maintenant) < 15 || Distance(o.X, o.Y, mere.X, mere.Y) > 1
                || o.EstMalade(maintenant) || o.CaptifDe != 0) continue;
            var affection = o.AffectionPour(mere.Id);
            var famille = o.PartenaireId == mere.Id || VieSociale.Parents(o, mere);
            if (affection < 0 && !famille) continue;
            var p = o.Personnalite;
            var envie = Math.Clamp(0.1 + 0.6 * p.Moral.Compassion + affection / 100 + (famille ? 0.4 : 0)
                                   + (o.CommunauteId != 0 && o.CommunauteId == mere.CommunauteId ? 0.2 : 0), 0, 1) * (1 - Psyche.Froideur(p));
            if (alea.Reel() >= envie) continue;
            var enfants = o.Sexe == Sexe.Femme ? NombreDEnfants(o, tous) : 0;
            aides.Add((o, 1 - Math.Exp(-(enfants + 1.5 * o.AccouchementsAides) / 5.0)));
        }
        if (aides.Count == 0) return 0;
        aides = aides.OrderByDescending(a => a.Experience).ThenBy(a => a.H.Id).Take(3).ToList();
        foreach (var (a, _) in aides)
        {
            Tour.Changer(a, x =>
            {
                x.AccouchementsAides++;
                x.Fatigue = Math.Min(100, x.Fatigue + 10);
            });
            VieSociale.Gratitude(a, mere, 2);
        }
        var noms = aides.Count == 1 ? aides[0].H.Nom
            : string.Join(", ", aides.Take(aides.Count - 1).Select(a => a.H.Nom)) + " et " + aides[^1].H.Nom;
        Noter(maintenant, TypeEvenement.Entraide, aides[0].H, mere,
            $"{noms} {(aides.Count == 1 ? "aide" : "aident")} {mere.Nom} à accoucher.", karma: Regard.AiderAccoucher);
        return Math.Min(0.45, 0.1 + 0.3 * aides[0].Experience + 0.05 * (aides.Count - 1));
    }

    private static void TerminerGrossesse(Humain mere, long maintenant)
    {
        mere.EnceinteDepuis = null;
        mere.TermeGrossesse = null;
        mere.PereEnfantId = 0;
        mere.Fatigue = Math.Min(100, mere.Fatigue + 30);
    }

    private static int NombreDEnfants(Humain mere, IReadOnlyList<Humain> tous)
    {
        var n = 0;
        for (var i = tous.Count - 1; i >= 0 && tous[i].MereId != 0; i--)
        {
            if (tous[i].MereId == mere.Id) n++;
        }
        return n;
    }

    /// <summary>
    /// Un parent attaché retourne vers son enfant éloigné ; plus l'attachement est faible, plus il le laisse
    /// s'éloigner, et en dessous de 0,3 il ne s'en soucie pas. Un bébé, lui, ne peut pas suivre : sa mère qui l'a posé
    /// revient le prendre, quel que soit son attachement, tant qu'elle n'est pas à bout de forces (chez les
    /// chasseurs-cueilleurs, un nourrisson est porté presque sans cesse : Konner, 2005).
    /// </summary>
    internal static Humain? EnfantAVeiller(Humain parent, IReadOnlyList<Humain> tous, long maintenant)
    {
        var attachement = parent.Personnalite.Attachement;
        var debutBebe = maintenant - 2 * Calendrier.SecondesParAn;
        if (parent.Sexe == Sexe.Femme && parent.DernierAccouchement >= debutBebe && !Delaisse(parent))
        {
            for (var i = tous.Count - 1; i >= 0; i--)
            {
                var e = tous[i];
                if (e.Naissance < debutBebe) break;
                if (e.EstVivant && e.MereId == parent.Id && (e.X != parent.X || e.Y != parent.Y)) return e;
            }
        }
        if (attachement < 0.3) return null;
        var limite = 2 + (1 - attachement) * 12;
        var debutEnfance = maintenant - 14 * Calendrier.SecondesParAn;
        Humain? choisi = null;
        for (var i = tous.Count - 1; i >= 0; i--)
        {
            var e = tous[i];
            if (e.Naissance < debutEnfance) break;
            if (!e.EstVivant || e.MereId != parent.Id && e.PereId != parent.Id) continue;
            var d = Distance(parent.X, parent.Y, e.X, e.Y);
            if (d > limite)
            {
                limite = d;
                choisi = e;
            }
        }
        return choisi;
    }

    /// <summary>
    /// Une mère à bout de forces (faim, soif, froid) cesse de porter son bébé si elle y est peu attachée. Même une mère
    /// peu attachée porte son nourrisson tant qu'elle n'est pas dans la détresse : l'abandon d'un enfant déjà allaité est
    /// rare et suit une misère extrême (Hrdy, 1999 ; Daly et Wilson, 1984). Le seuil dépasse 100, donc n'est jamais
    /// atteint, dès que l'attachement vaut plus de 0,4 ; une mère déprimée s'occupe moins de son bébé (le seuil baisse de 10).
    /// </summary>
    internal static bool Delaisse(Humain mere) =>
        Math.Max(mere.Faim, Math.Max(mere.Soif, mere.Froid)) > 80 + 50 * mere.Personnalite.Attachement - (mere.EstDeprime ? 10 : 0);

    /// <summary>
    /// Un enfant sevré ne sait pas encore se nourrir seul : un parent proche partage avec lui nourriture et eau,
    /// d'autant plus qu'il y est attaché, tant qu'il n'est pas lui-même affamé ou assoiffé.
    /// (Chez les chasseurs-cueilleurs, les enfants dépendent des adultes jusqu'à l'adolescence ; Kaplan et al. 2000.)
    /// </summary>
    private static void NourrirEnfant(Humain enfant, Humain? mere, Humain? pere, IReadOnlyList<Humain> tous)
    {
        foreach (var parent in new[] { mere, pere })
        {
            if (parent is null || Distance(enfant.X, enfant.Y, parent.X, parent.Y) > 1) continue;
            var part = 0.5 * parent.Personnalite.Attachement;
            if (parent.Faim < 75)
            {
                var donne = Math.Min(enfant.Faim, part);
                enfant.Faim -= donne;
                Tour.Changer(parent, p => p.Faim = Math.Min(100, p.Faim + 0.6 * donne));
            }
            if (parent.Soif < 75)
            {
                var donne = Math.Min(enfant.Soif, part);
                enfant.Soif -= donne;
                Tour.Changer(parent, p => p.Soif = Math.Min(100, p.Soif + 0.6 * donne));
            }
        }
        // Ses grand-mères, si elles sont là et qu'il a encore faim : chez les Hadza, elles nourrissent les petits sevrés
        // et soulagent leur mère (Hawkes et al., 1998).
        GrandMereNourrit(enfant, enfant.MereId, tous);
        GrandMereNourrit(enfant, enfant.PereId, tous);
    }

    private static void GrandMereNourrit(Humain enfant, int parentId, IReadOnlyList<Humain> tous)
    {
        if (enfant.Faim < 30 || parentId == 0 || Vivant(tous, tous[parentId - 1].MereId) is not { } gm) return;
        if (gm.Faim >= 60 || gm.Activite == Activite.Dormir || Distance(enfant.X, enfant.Y, gm.X, gm.Y) > 1) return;
        var donne = Math.Min(enfant.Faim - 20, 0.35 * gm.Personnalite.Attachement);
        if (donne <= 0) return;
        enfant.Faim -= donne;
        Tour.Changer(gm, g => g.Faim = Math.Min(100, g.Faim + 0.6 * donne));
    }

    /// <summary>
    /// Celle (ou celui) qui prend un bébé dont la mère est morte. Chez les chasseurs-cueilleurs, l'orphelin est d'abord
    /// allaité par une autre femme qui a du lait, souvent une parente de la mère : l'allaitement partagé est courant chez
    /// les Aka et les Efe (Hewlett et Winn, 2014 ; Hrdy, 2009). Le lait dure tant qu'un enfant tète : une femme qui a
    /// accouché depuis moins de trois ans en a encore. Un bébé de plus de six mois peut aussi survivre sans lait, nourri
    /// d'aliments prémâchés et d'eau par celui qui veille sur lui (son père, un proche) ; plus jeune, il en meurt.
    /// La bonté et l'attachement de chacune décident, la parenté et la communauté aussi ; un cœur froid ne prend personne.
    /// </summary>
    internal Humain? Nourrice(Humain b, IReadOnlyList<Humain> tous, long maintenant)
    {
        var cle = $"nourrice:{b.Id}";
        var morte = b.MereId > 0 ? tous[b.MereId - 1] : null;
        Humain? choisie = null;
        var meilleur = 0.0;
        foreach (var f in tous.Vivants())
        {
            if (!f.EstVivant || f.Sexe != Sexe.Femme || f.EstEnfant(maintenant) || f.Id == b.MereId) continue;
            if (f.DernierAccouchement is not { } accouchement || maintenant - accouchement >= 3 * Calendrier.SecondesParAn) continue;
            var d = Distance(b.X, b.Y, f.X, f.Y);
            var dejaElle = f.Memoire.Jardin.Exists(x => x.Cle == cle);
            // Elle l'entend pleurer au camp ; celle qui le porte déjà l'emmène où elle va.
            if (d > (dejaElle ? CasesParPasMax : 3)) continue;
            var m = f.Personnalite.Moral;
            var parente = morte is not null && (f.MereId > 0 && f.MereId == morte.MereId || f.Id == morte.MereId || f.Id == b.PereId);
            var envie = (0.2 + 0.5 * m.Compassion + 0.3 * f.Personnalite.Attachement) * (1 - Psyche.Froideur(f.Personnalite))
                        + (parente ? 0.3 : 0) + (f.CommunauteId != 0 && f.CommunauteId == b.CommunauteId ? 0.1 : 0)
                        + (morte is not null ? f.AffectionPour(morte.Id) / 200 : 0) + (dejaElle ? 1 : 0);
            if (envie < 0.5 || envie <= meilleur || Delaisse(f)) continue;
            (choisie, meilleur) = (f, envie);
        }
        if (choisie is not null)
        {
            if (!choisie.Memoire.Jardin.Exists(x => x.Cle == cle))
            {
                var id = Noter(maintenant, TypeEvenement.Entraide, choisie, b,
                    $"{choisie.Nom} prend {b.Nom} ({AgeEnMots(b, maintenant)}){(morte is null ? "" : $", qui a perdu sa mère {morte.Nom},")} et l'allaite comme son enfant.",
                    morte?.MortEvenementId ?? 0, significatif: true, karma: Regard.Allaiter);
                Jardin.Confier(choisie, TypeIntime.Intention, b.Id, $"Je nourrirai {b.Nom} de mon lait, comme mon enfant.",
                    40 + 40 * choisie.Personnalite.Moral.Compassion, id, maintenant, cle);
            }
            return choisie;
        }
        // Sevrable : celui qui veille sur lui le porte et le nourrit d'aliments prémâchés.
        if (b.AgeEnAnnees(maintenant) < 0.5 || Gardien(b, tous, maintenant) is not { } g || Distance(b.X, b.Y, g.X, g.Y) > CasesParPasMax) return null;
        if (!g.Memoire.Jardin.Exists(x => x.Cle == cle))
        {
            var id = Noter(maintenant, TypeEvenement.Entraide, g, b,
                $"Faute de lait, {g.Nom} porte {b.Nom} ({AgeEnMots(b, maintenant)}) et {(b.Sexe == Sexe.Femme ? "la" : "le")} nourrit de bouchées mâchées.",
                morte?.MortEvenementId ?? 0, significatif: true, karma: Regard.NourrirSansLait);
            Jardin.Confier(g, TypeIntime.Intention, b.Id, $"{b.Nom} n'a plus de mère : c'est moi qui le nourrirai.",
                40 + 40 * g.Personnalite.Moral.Compassion, id, maintenant, cle);
        }
        return g;
    }

    /// <summary>
    /// Un bébé est porté par sa mère, qui l'allaite. Sans elle (<paramref name="orphelin"/>), une autre femme qui a du lait
    /// peut le prendre (<see cref="Nourrice"/>) ; à défaut, celui qui veille sur lui le nourrit moins bien, sans lait.
    /// </summary>
    private void VivreBebe(Humain b, Humain? mere, long maintenant, bool orphelin = false)
    {
        b.Fatigue = 0;
        b.Desir = 0;
        // La mère a pu faire deux cases dans le pas (fuite, descente) avant que son bébé ne soit compté.
        // Une mère qui portait son bébé au pas précédent l'emporte où qu'elle aille (fuite, longue marche) : il ne reste en
        // arrière que si elle le délaisse.
        var portaitIlYAUnInstant = mere is not null && mere.BebePorteLe >= maintenant - 2 * MondeSimule.SecondesParPas;
        var proche = mere is not null && (Distance(b.X, b.Y, mere.X, mere.Y) <= Comportement.CasesParPasMax || portaitIlYAUnInstant);
        var porte = proche && !Delaisse(mere!);
        if (proche && !porte && b.Activite is Activite.PorteParSaMere or Activite.Dormir)
        {
            Noter(maintenant, TypeEvenement.Delaissement, mere!, b, orphelin
                ? $"{mere!.Nom}, à bout de forces, laisse là {b.Nom}, qu'{(mere.Sexe == Sexe.Femme ? "elle" : "il")} portait."
                : $"{mere!.Nom}, à bout de forces, délaisse son bébé {b.Nom}.");
        }
        if (mere is not null && porte)
        {
            b.X = mere.X;
            b.Y = mere.Y;
            Tour.Changer(mere, m => m.BebePorteLe = maintenant);
            // Celle qui le porte a pu bouger pendant ce même pas : il est là où elle est arrivée.
            Tour.Differer(b, mere, (x, m) =>
            {
                if (x.EstVivant && m.EstVivant) (x.X, x.Y) = (m.X, m.Y);
            });
        }

        // L'allaitement nourrit et abreuve le bébé, et coûte à la mère (environ 25 % d'énergie en plus). Le corps de la mère
        // protège le lait : il puise dans ses réserves, et la quantité de lait ne baisse guère chez les mères mal nourries ;
        // elle ne chute qu'en cas de dénutrition grave (Prentice et al., 1986 ; Dewey, 1997). Le lait ne tarit donc que si elle
        // meurt de faim, et baisse d'un tiers au plus quand ses réserves sont épuisées. De même, elle le préserve quand elle
        // boit peu (boire plus n'en donne pas davantage : Dusdieker et al., 1985) : c'est elle qui a soif, et le lait ne
        // baisse que lorsqu'elle se déshydrate gravement.
        // Sans lait (un homme, une femme qui n'allaite plus), des bouchées mâchées et de l'eau : moins que le lait, et seulement
        // si celui qui le porte n'a pas lui-même trop faim ou soif.
        var allaitante = porte && mere!.Sexe == Sexe.Femme && mere.DernierAccouchement is { } dernier
                         && maintenant - dernier < 3 * Calendrier.SecondesParAn;
        var lait = porte
            ? Math.Clamp(Math.Min((100 - mere!.Faim) / 8, (100 - mere.Soif) / 15), 0, 1) * Math.Min(1, 0.65 + mere.Reserves / 30)
              * (allaitante ? 1 : 0.5)
            : 0;
        Depenser(b, (0.4 - 1.4 * lait) * b.Biologie.Metabolisme);
        b.Soif = Math.Clamp(b.Soif + 0.6 - 1.6 * lait, 0, 100);
        if (lait > 0)
        {
            Tour.Changer(mere!, m =>
            {
                Depenser(m, 0.06 * lait);
                m.Soif = Math.Min(100, m.Soif + 0.08 * lait);
            });
        }

        if (porte)
        {
            // Porté peau contre peau, le bébé a aussi chaud que sa mère, ou un peu plus.
            b.Froid = Math.Max(0, mere!.Froid - 5);
        }
        else
        {
            var temperature = Temperature(b.X, b.Y, maintenant);
            var perte = Math.Max(0, TemperatureConfort - temperature) * 0.065 * Math.Max(0, 1 - AbriEn(b.X, b.Y) - (Installations.FoyerEn(b.X, b.Y) is null ? 0 : ChaleurDuFeu));
            b.Froid = Math.Clamp(b.Froid + perte - 0.4, 0, 100);
        }

        b.Activite = porte ? (mere!.Activite == Activite.Dormir ? Activite.Dormir : Activite.PorteParSaMere) : Activite.Repos;
        _temperature = Temperature(b.X, b.Y, maintenant);
        Souffrir(b, maintenant, fragilite: 1.5);
    }

    /// <summary>Mortalité de fond, tirée une fois par jour du monde.</summary>
    public void Vieillir(Humain h, long maintenant)
    {
        if (!h.EstVivant) return;
        var age = h.AgeEnAnnees(maintenant);
        var taux = MortaliteParAge.First(t => age < t.AgeMax).TauxAnnuel;
        // Avant 50 ans, ces morts sont des maladies (voir plus bas) et des morts de tout-petits : la clémence du monde les écarte ;
        // l'usure de l'âge, non.
        if (age < 50) taux *= Adouci;
        if (alea.Reel() < taux / Calendrier.JoursParAn)
        {
            // Avant 50 ans, ces morts sont surtout des infections, des poumons et du ventre (Gurven et Kaplan, 2007).
            if (age < 50 && !h.EstMalade(maintenant))
            {
                var maladie = Maladies.Choisir(h, carte.BiomeEn(h.X, h.Y), Calendrier.Decomposer(maintenant).Saison, Population, maintenant, alea);
                // Les maux de ventre qu'évitent l'hygiène des siens ne tuent pas.
                if (Hygiene.DuVentre(maladie) && alea.Reel() < ProtectionHygiene(h, maladie, _tous)) return;
                h.Maladie = maladie;
                h.MaladeJusqua = maintenant + Calendrier.SecondesParJour;
            }
            Mourir(h, maintenant, age >= 50 ? CauseDeMort.Vieillesse : CauseDeMort.Maladie);
            return;
        }
        var desespoir = Social?.RisqueDesespoir(h, maintenant) ?? 0;
        if (desespoir > 0 && alea.Reel() < desespoir) Mourir(h, maintenant, CauseDeMort.Desespoir);
    }
}
