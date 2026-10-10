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
    /// Mortalité annuelle « de fond » selon l'âge (accidents, maladies, usure), en plus des besoins.
    /// Bornes d'âge en années et taux annuel, du plus jeune au plus âgé.
    /// </summary>
    private static readonly (double AgeMax, double TauxAnnuel)[] MortaliteParAge =
    [
        (1, 0.10), (5, 0.03), (15, 0.01), (30, 0.005), (40, 0.01),
        (50, 0.03), (60, 0.07), (70, 0.15), (80, 0.30), (double.MaxValue, 0.50),
    ];

    /// <summary>Naissances survenues pendant le pas : le monde crée les nouveau-nés après le pas.</summary>
    public List<(Humain Mere, int PereId)> Accouchements { get; } = [];

    private bool ChercherAccouplement(Humain h, Humain? partenaire, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstFecond(maintenant)) return false;

        // Partenaires possibles à portée immédiate : de l'autre sexe, adultes, éveillés et disposés.
        Humain? choisi = null;
        if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) <= 1 && EstDispose(partenaire, maintenant))
        {
            choisi = partenaire;
        }
        else
        {
            // Avec un humain d'un autre couple : plus probable si son propre compagnon est loin ou mort.
            var ouverture = partenaire is null || Distance(h.X, h.Y, partenaire.X, partenaire.Y) > 8 ? 0.6 : 0.1;
            foreach (var autre in tous)
            {
                if (autre.Id == h.Id || autre.Sexe == h.Sexe || !autre.EstVivant) continue;
                if (Distance(h.X, h.Y, autre.X, autre.Y) > 1 || !EstDispose(autre, maintenant) || SontParents(h, autre)) continue;
                if (alea.Reel() < ouverture) choisi = autre;
                break;
            }
        }

        if (choisi is not null)
        {
            SAccoupler(h, choisi, tous, maintenant);
            return true;
        }

        // Désir fort : aller vers son compagnon, ou vers l'humain disposé le plus proche si l'on est seul.
        if (h.Desir < 70) return false;
        var cible = partenaire;
        if (cible is null)
        {
            var rayon = RayonPerception(h);
            cible = tous
                .Where(a => a.EstVivant && a.Sexe != h.Sexe && a.EstFecond(maintenant) && !SontParents(h, a)
                            && Distance(h.X, h.Y, a.X, a.Y) <= rayon)
                .OrderBy(a => Distance(h.X, h.Y, a.X, a.Y)).ThenBy(a => a.Id)
                .FirstOrDefault();
        }
        if (cible is null || Distance(h.X, h.Y, cible.X, cible.Y) == 0) return false;
        Avancer(h, cible.X, cible.Y, Activite.RejoindrePartenaire);
        return true;
    }

    private static bool EstDispose(Humain a, long maintenant) =>
        a.EstVivant && a.EstFecond(maintenant) && a.Activite != Activite.Dormir && a.Desir >= 25
        && a.Faim < 80 && a.Soif < 80;

    /// <summary>Parents et enfants, frères et sœurs : élevés ensemble, ils ne s'attirent pas (instinct).</summary>
    private static bool SontParents(Humain a, Humain b) =>
        a.MereId == b.Id || a.PereId == b.Id || b.MereId == a.Id || b.PereId == a.Id
        || (a.MereId != 0 && a.MereId == b.MereId)
        || (a.PereId != 0 && a.PereId == b.PereId);

    private void SAccoupler(Humain a, Humain b, IReadOnlyList<Humain> tous, long maintenant)
    {
        a.Activite = b.Activite = Activite.SAccoupler;
        a.CibleX = a.CibleY = b.CibleX = b.CibleY = -1;
        a.Desir = b.Desir = 0;

        var femme = a.Sexe == Sexe.Femme ? a : b;
        var homme = a.Sexe == Sexe.Femme ? b : a;
        if (alea.Reel() < ChanceConception(femme, homme, tous, maintenant))
        {
            var jours = Math.Clamp(JoursDeGrossesseMoyens + Biologie.Normale(alea) * EcartJoursDeGrossesse, 230, 300);
            femme.EnceinteDepuis = maintenant;
            femme.TermeGrossesse = maintenant + (long)(jours * Calendrier.SecondesParJour);
            femme.PereEnfantId = homme.Id;
        }

        // Deux humains seuls qui s'accouplent forment un couple.
        if (Vivant(tous, a.PartenaireId) is null && Vivant(tous, b.PartenaireId) is null)
        {
            a.PartenaireId = b.Id;
            b.PartenaireId = a.Id;
        }
    }

    /// <summary>
    /// Probabilité qu'un accouplement mène à une grossesse : fécondité des deux selon l'âge et la biologie,
    /// réduite par la faim, la maladie et l'allaitement.
    /// </summary>
    internal double ChanceConception(Humain femme, Humain homme, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (femme.EstEnceinte) return 0;
        // Un manque d'énergie prolongé bloque l'ovulation (Ellison 2001).
        var nutrition = Math.Clamp(1 - Math.Max(0, femme.Faim - 50) / 50, 0, 1) * Math.Clamp(femme.Sante / 100, 0, 1);
        return ConceptionParAccouplement * femme.Fecondite(maintenant) * homme.Fecondite(maintenant)
               * nutrition * Allaitement(femme, tous, maintenant);
    }

    /// <summary>
    /// L'allaitement à la demande, jusque vers 3 ans, rend presque infertile la première année, puis de moins en moins :
    /// chez les chasseurs-cueilleurs, les naissances sont espacées de 3 à 4 ans (Konner et Worthman 1980).
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
            return age < 1 ? 0.03 : 0.03 + 0.3 * (age - 1) / 2;
        }
        return 1;
    }

    /// <summary>Faim et mauvaise santé de la mère aggravent les risques de la grossesse et de l'accouchement.</summary>
    private static double Detresse(Humain mere) =>
        1 + Math.Max(0, mere.Faim - 60) / 40 + Math.Max(0, 60 - mere.Sante) / 40;

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
            if (alea.Reel() < risqueParPas) TerminerGrossesse(mere, maintenant);
            return;
        }

        var terme = mere.TermeGrossesse ?? debut + (long)(JoursDeGrossesseMoyens * Calendrier.SecondesParJour);
        if (maintenant < terme) return;

        var premature = terme - debut < JoursAvantPrematurite * Calendrier.SecondesParJour;
        var parite = NombreDEnfants(mere, tous);
        var detresse = Detresse(mere);
        // Jumeaux : environ 1 naissance sur 80, un peu plus vers 35 ans.
        var jumeaux = alea.Reel() < 0.004 + 0.008 * (age is >= 30 and < 40 ? 1.5 : 1);
        var risqueMortNe = RisqueMortNe * FacteurAgeMere(age) * detresse * (premature ? 2 : 1) * (jumeaux ? 2 : 1);
        for (var n = jumeaux ? 2 : 1; n > 0; n--)
        {
            if (alea.Reel() >= risqueMortNe) Accouchements.Add((mere, mere.PereEnfantId));
        }

        TerminerGrossesse(mere, maintenant);
        mere.DernierAccouchement = maintenant;
        mere.Fatigue = 100;
        var risqueMere = RisqueMortAccouchement * FacteurAgeMere(age) * detresse
                         * (parite == 0 ? 1.5 : parite >= 5 ? 1.3 : 1) * (jumeaux ? 1.5 : 1);
        if (alea.Reel() < risqueMere) Mourir(mere, maintenant, CauseDeMort.Accouchement);
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
    /// s'éloigner, et en dessous de 0,3 il ne s'en soucie pas.
    /// </summary>
    private static Humain? EnfantAVeiller(Humain parent, IReadOnlyList<Humain> tous, long maintenant)
    {
        var attachement = parent.Personnalite.Attachement;
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
    /// Une mère qui souffre trop (faim, soif, froid) cesse de porter son bébé si elle y est peu attachée.
    /// Le seuil dépasse 100, donc n'est jamais atteint, dès que l'attachement vaut plus de 0,5.
    /// </summary>
    internal static bool Delaisse(Humain mere) =>
        Math.Max(mere.Faim, Math.Max(mere.Soif, mere.Froid)) > 60 + 80 * mere.Personnalite.Attachement;

    /// <summary>
    /// Un enfant sevré ne sait pas encore se nourrir seul : un parent proche partage avec lui nourriture et eau,
    /// d'autant plus qu'il y est attaché, tant qu'il n'est pas lui-même affamé ou assoiffé.
    /// (Chez les chasseurs-cueilleurs, les enfants dépendent des adultes jusqu'à l'adolescence ; Kaplan et al. 2000.)
    /// </summary>
    private static void NourrirEnfant(Humain enfant, Humain? mere, Humain? pere)
    {
        foreach (var parent in new[] { mere, pere })
        {
            if (parent is null || Distance(enfant.X, enfant.Y, parent.X, parent.Y) > 1) continue;
            var part = 0.5 * parent.Personnalite.Attachement;
            if (parent.Faim < 75)
            {
                var donne = Math.Min(enfant.Faim, part);
                enfant.Faim -= donne;
                parent.Faim = Math.Min(100, parent.Faim + 0.6 * donne);
            }
            if (parent.Soif < 75)
            {
                var donne = Math.Min(enfant.Soif, part);
                enfant.Soif -= donne;
                parent.Soif = Math.Min(100, parent.Soif + 0.6 * donne);
            }
        }
    }

    /// <summary>Un bébé est porté par sa mère, qui l'allaite. Sans elle, il ne peut pas survivre longtemps.</summary>
    private void VivreBebe(Humain b, Humain? mere, long maintenant)
    {
        b.Fatigue = 0;
        b.Desir = 0;
        var porte = mere is not null && Distance(b.X, b.Y, mere.X, mere.Y) <= 1 && !Delaisse(mere);
        if (mere is not null && porte)
        {
            b.X = mere.X;
            b.Y = mere.Y;
        }

        // L'allaitement nourrit le bébé et coûte à la mère ; le lait se tarit quand elle manque elle-même de tout.
        var lait = porte ? Math.Clamp((100 - Math.Max(mere!.Faim, mere.Soif)) / 30, 0, 1) : 0;
        b.Faim = Math.Clamp(b.Faim + 0.4 - 1.4 * lait, 0, 100);
        b.Soif = Math.Clamp(b.Soif + 0.6 - 1.6 * lait, 0, 100);
        if (lait > 0)
        {
            mere!.Faim = Math.Min(100, mere.Faim + 0.06 * lait);
            mere.Soif = Math.Min(100, mere.Soif + 0.08 * lait);
        }

        if (porte)
        {
            // Porté peau contre peau, le bébé a aussi chaud que sa mère, ou un peu plus.
            b.Froid = Math.Max(0, mere!.Froid - 5);
        }
        else
        {
            var temperature = Climat.Temperature(carte, b.X, b.Y, maintenant);
            var perte = Math.Max(0, TemperatureConfort - temperature) * 0.065 * Math.Max(0, 1 - Climat.Abri(carte.BiomeEn(b.X, b.Y)));
            b.Froid = Math.Clamp(b.Froid + perte - 0.4, 0, 100);
        }

        b.Activite = porte ? (mere!.Activite == Activite.Dormir ? Activite.Dormir : Activite.PorteParSaMere) : Activite.Repos;
        Souffrir(b, maintenant, fragilite: 1.5);
    }

    /// <summary>Mortalité de fond, tirée une fois par jour du monde.</summary>
    public void Vieillir(Humain h, long maintenant)
    {
        if (!h.EstVivant) return;
        var age = h.AgeEnAnnees(maintenant);
        var taux = MortaliteParAge.First(t => age < t.AgeMax).TauxAnnuel;
        if (alea.Reel() < taux / Calendrier.JoursParAn)
        {
            Mourir(h, maintenant, age >= 50 ? CauseDeMort.Vieillesse : CauseDeMort.Maladie);
        }
    }
}
