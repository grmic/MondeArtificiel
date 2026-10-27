using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// La médecine des premiers humains : ce qu'ils font pour soigner, et ce qu'ils en croient.
/// <para>
/// Soignants. Chacun garde, aux yeux des siens, le compte de ceux qu'il a soignés, de ceux qui ont guéri et de ceux qui sont
/// morts. Comme la plupart des maladies passent d'elles-mêmes, un soignant reçoit le crédit de guérisons auxquelles il n'est
/// pour rien (corrélation illusoire : Chapman et Chapman, 1967). Celui qui en a guéri nettement plus que les autres est tenu
/// pour guérisseur : on l'appelle au chevet des malades, et les enfants qui le regardent faire apprennent ses remèdes plus vite
/// (chez les Tsimane et les Aka, le savoir des plantes passe par ceux qu'on dit savoir soigner : McDade et al., 2007 ;
/// Salali et al., 2016). S'il en perd trop, on cesse de croire en lui.
/// </para>
/// <para>
/// Plaies. Laver une plaie, la presser, la couvrir de feuilles, y écraser une plante qui empêche de pourrir : des gestes que
/// les humains trouvent eux-mêmes, par essai et par imitation. Une plaie soignée s'infecte moins et se referme plus vite.
/// </para>
/// <para>
/// Accouchement. Chez presque tous les peuples, la mère accouche entourée de femmes de son entourage qui ont déjà enfanté ou
/// aidé (Rosenberg et Trevathan, 2002) : leur expérience réduit le risque de mourir en couches, d'une part qui reste estimée.
/// </para>
/// <para>
/// Transe. Les esprits portés à la transcendance, qui croient fermement à l'invisible et l'ont déjà vu (une plante à visions),
/// entrent en transe au chevet des malades, par le jeûne, la veille ou une plante : le chamanisme naît ainsi dans les sociétés
/// de chasseurs-cueilleurs (Winkelman, 1990 ; Singh, 2018). Ceux qui les voient croient davantage.
/// </para>
/// <para>
/// Placebo et nocebo. Le soin apaise la peur, la douleur, le chagrin et la fatigue, d'autant plus que le malade croit en
/// l'invisible et en son soignant (Benedetti, 2008) ; il n'a jamais guéri une infection. À l'inverse, celui qui croit fermement
/// et qu'un chaman maudit peut dépérir de peur (« mort vaudou » : Cannon, 1942), rarement jusqu'à en mourir.
/// </para>
/// <para>Charlatans. Un manipulateur peut se dire guérisseur pour être nourri et respecté ; il est démasqué si trop de ses malades meurent.</para>
/// </summary>
public static class Medecine
{
    private const long Jour = Calendrier.SecondesParJour;

    /// <summary>Temps qu'une plaie met à se refermer : deux semaines environ, moins chez les endurants et si elle est soignée.</summary>
    public static long DureePlaie(Humain h) =>
        (long)(14 * (1.3 - 0.6 * h.Personnalite.Endurance) * (1 - 0.3 * h.PlaieSoin) * Jour);

    public static bool PlaieOuverte(Humain h, long maintenant) => h.PlaieLe > 0 && maintenant - h.PlaieLe < DureePlaie(h);

    /// <summary>Une blessure (coup, combat, chute de rocher) ouvre une plaie, pas encore soignée.</summary>
    public static void Blesser(Humain h, double degats, long maintenant)
    {
        if (Tour.Autrui(h))
        {
            Tour.Differer(h, x => Blesser(x, degats, maintenant));
            return;
        }
        if (!h.EstVivant || degats < 5) return;
        h.PlaieLe = Math.Max(1, maintenant);
        h.PlaieSoin = 0;
    }

    /// <summary>
    /// Part des infections d'une plaie qu'évitent les soins qu'elle a reçus : jusqu'à 70 % pour la plaie qui pourrit,
    /// moitié moins pour le tétanos, dont les spores résistent mieux au lavage (ordres de grandeur estimés, à mesurer).
    /// </summary>
    public static double Protection(Humain h, Maladie m, long maintenant) => !PlaieOuverte(h, maintenant) ? 0 : m switch
    {
        Maladie.PlaieInfectee => 0.7 * h.PlaieSoin,
        Maladie.Tetanos => 0.35 * h.PlaieSoin,
        _ => 0,
    };

    /// <summary>Soin qu'apporte une technique : laver la plaie compte plus que la couvrir.</summary>
    public static double Qualite(Matiere technique) => technique == Matiere.Eau ? 0.35 : 0.25;

    /// <summary>Encore souffrant : malade, fiévreux, blessé, très affaibli ou rongé par une malédiction.</summary>
    public static bool Souffrant(Humain h, long maintenant) =>
        h.EstMalade(maintenant) || h.InfectionId != 0 && h.SymptomesLe <= maintenant && h.GuerisonLe > maintenant
        || PlaieOuverte(h, maintenant) || h.Sante < 50 || h.MauditLe > 0;

    /// <summary>Part de ses malades qui ont guéri, telle que les siens la jugent (une seule guérison ne prouve rien).</summary>
    public static double Taux(Humain h) => (h.Gueris + 1.0) / (h.Gueris + h.Perdus + 2.0);

    /// <summary>Renommée de soignant, de 0 à 1 : des guérisons nombreuses, peu de morts, un titre de guérisseur ou de chaman.</summary>
    public static double Renommee(Humain h) =>
        Math.Clamp(Taux(h) * (1 - Math.Exp(-h.Gueris / 5.0)) + (h.GuerisseurDepuis > 0 ? 0.2 : 0) + (h.ChamanDepuis > 0 ? 0.1 : 0), 0, 1);

    /// <summary>Ce que le malade attend du soin (0 à 1) : sa croyance en l'invisible, la renommée du soignant, la transe, l'affection.</summary>
    public static double Attente(Humain patient, Humain soigneur, bool transe) =>
        Math.Clamp(0.5 * patient.Croyance / 100 + 0.4 * Renommee(soigneur) + (transe ? 0.25 : 0)
                   + 0.1 * Math.Max(0, patient.AffectionPour(soigneur.Id)) / 100, 0, 1);

    /// <summary>
    /// Effet placebo d'un soin : la peur, la douleur (fatigue), le chagrin reculent, l'humeur et un peu la santé remontent,
    /// selon ce que le malade en attend. La maladie, elle, suit son cours. Renvoie l'attente.
    /// </summary>
    public static double Placebo(Humain patient, Humain soigneur, bool transe)
    {
        var a = Attente(patient, soigneur, transe);
        patient.Peur = Math.Max(0, patient.Peur - 25 * a);
        patient.Chagrin = Math.Max(0, patient.Chagrin - 8 * a);
        patient.Fatigue = Math.Max(0, patient.Fatigue - 10 * a);
        patient.Humeur = Math.Min(100, patient.Humeur + 8 * a);
        patient.Sante = Math.Min(100, patient.Sante + 2 * a);
        return a;
    }

    /// <summary>Peut entrer en transe : adulte, porté à la transcendance, croyant fermement, et qui a déjà vu l'invisible.</summary>
    public static bool PeutEntrerEnTranse(Humain h, long maintenant) =>
        !h.EstEnfant(maintenant) && h.Personnalite.Transcendance >= 0.6 && h.Croyance >= 50
        && (h.ChamanDepuis > 0 || h.Savoirs.Exists(s => s.Effet == Effet.Visions)
            || h.Memoire.Jardin.Exists(x => x.Cle.StartsWith("visions:", StringComparison.Ordinal)));

    /// <summary>Tenté de se dire guérisseur sans rien savoir : un manipulateur (psychopathe ou peu honnête) qui sait séduire.</summary>
    public static bool PorteAuCharlatanisme(Humain h, long maintenant) =>
        (Psyche.EstPsychopathe(h, maintenant) || h.Personnalite.Moral.Honnetete < 0.25) && h.Personnalite.Charisme >= 0.45;

    /// <summary>Sait soigner une plaie (lavage ou feuilles), assez pour le faire.</summary>
    public static bool SaitPanser(Humain h) => h.Savoirs.Exists(s => s.Effet == Effet.Pansement && s.Confiance >= 0.4);

}

public sealed partial class Comportement
{
    private const long JourDeSoin = Calendrier.SecondesParJour;

    /// <summary>Pansements dont on ne sait pas encore s'ils ont réussi : blessé → techniques employées, panseur, événement.</summary>
    private readonly Dictionary<int, (Matiere[] Techniques, int PanseurId, long EvenementId)> _pansements = [];

    /// <summary>
    /// Le soignant prend en charge un malade. Il le compte parmi ses soignés quand l'issue est connue : guéri, mort, ou toujours
    /// souffrant deux mois après ses derniers soins. S'il passe à un autre soignant avant, c'est ce dernier qui en répond.
    /// </summary>
    internal void MarquerSoin(Humain soigneur, Humain patient)
    {
        if (soigneur.Id == patient.Id) return;
        patient.SoigneurId = soigneur.Id;
        patient.SoigneurLe = Math.Max(1, _maintenant);
    }

    /// <summary>Un malade meurt : son dernier soignant l'a perdu (si ses soins sont récents).</summary>
    private void PerduPourSonSoignant(Humain h, long maintenant)
    {
        var mort = h.Id;
        Tour.Differer(() => _pansements.Remove(mort));
        if (h.SoigneurId > 0 && h.SoigneurId <= _tous.Count && maintenant - h.SoigneurLe <= 60 * JourDeSoin)
        {
            Tour.Changer(_tous[h.SoigneurId - 1], s => { s.Soignes++; s.Perdus++; });
        }
        h.SoigneurId = 0;
    }

    private bool EauPourPanser(Humain h) => APortee(h, Matiere.Eau) || h.Objets.Exists(o => o.Matiere is Matiere.Calebasse or Matiere.Substance && o.Plein);

    private bool FeuillesPourPanser(Humain h) =>
        APortee(h, Matiere.GrandesFeuilles) || APortee(h, Matiere.Branche) || APortee(h, Matiere.Fibres);

    /// <summary>
    /// Une plaie ouverte et pas encore soignée : celui qui sait la soigner (le blessé lui-même, un proche à côté, un guérisseur
    /// tout près) la lave et la couvre. Personne ne sait ? Certains, d'instinct, la pressent ou la lavent : c'est un essai, et
    /// la plaie dira s'il était bon. Vérifié une fois par heure.
    /// </summary>
    private bool SoignerPlaie(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.PlaieSoin > 0 || (_maintenant / MondeSimule.SecondesParPas + h.Id) % 6 != 0 || !Medecine.PlaieOuverte(h, _maintenant)) return false;
        if (!h.EstEnfant(_maintenant) && Medecine.SaitPanser(h) && Panser(h, h, tous)) return true;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.EstEnfant(_maintenant) || o.Activite == Activite.Dormir || o.EstMalade(_maintenant) || o.CaptifDe != 0) continue;
            if (Distance(o.X, o.Y, h.X, h.Y) > (o.GuerisseurDepuis > 0 ? 1 : 0) || !Medecine.SaitPanser(o)) continue;
            var affection = o.AffectionPour(h.Id);
            if (affection < -10) continue;
            var p = o.Personnalite;
            var famille = VieSociale.Parents(o, h) || o.PartenaireId == h.Id;
            var envie = Math.Clamp(0.3 + 0.7 * (p.Moral.Compassion - 0.3) + Math.Max(0, affection) / 100 + (famille ? 0.3 : 0)
                                   + (o.GuerisseurDepuis > 0 ? 0.4 : 0), 0, 1) * (1 - Psyche.Froideur(p));
            if (alea.Reel() >= envie) continue;
            if (Panser(o, h, tous)) return true;
        }
        // Un geste d'instinct, rare : presser la plaie, la laver si l'eau est là.
        if (h.AgeEnAnnees(_maintenant) < 8 || h.Savoirs.Exists(s => s.Effet == Effet.Pansement)) return false;
        var pp = h.Personnalite;
        if (alea.Reel() >= 0.0005 * (0.3 + pp.Ouverture) * (0.5 + pp.Conscience)) return false;
        var eau = EauPourPanser(h);
        var feuilles = FeuillesPourPanser(h);
        if (!eau && !feuilles) return false;
        var m = eau && (!feuilles || alea.Reel() < 0.5) ? Matiere.Eau : Matiere.GrandesFeuilles;
        h.PlaieSoin = Medecine.Qualite(m);
        h.Activite = Activite.Soigner;
        h.CibleX = h.CibleY = -1;
        PoserPansement(h.Id, m);
        return true;
    }

    // À part : la fermeture n'est créée que si la plaie est pansée.
    private void PoserPansement(int soi, Matiere m) => Tour.Differer(() => _pansements[soi] = ([m], soi, 0));

    /// <summary>
    /// <paramref name="panseur"/> soigne la plaie de <paramref name="blesse"/> avec ce qu'il sait faire et ce qu'il a sous la
    /// main : de l'eau, des feuilles, une plante qu'il croit bonne pour les plaies (elle agit selon ses vraies vertus).
    /// Ceux qui regardent peuvent apprendre. Renvoie faux s'il n'a rien sous la main.
    /// </summary>
    internal bool Panser(Humain panseur, Humain blesse, IReadOnlyList<Humain> tous)
    {
        var techniques = TechniquesDePansement(panseur);
        if (techniques.Count == 0) return false;
        // Pendant un pas simultané, celui qui soigne un autre le fait après le pas, si tous deux vivent encore.
        if (Tour.Autrui(panseur) || Tour.Autrui(blesse))
        {
            Tour.Differer(tous, panseur, blesse, null, (t, p, b, _) =>
            {
                if (p.EstVivant && b.EstVivant) Panser(p, b, t);
            });
            return true;
        }
        var soin = techniques.Sum(Medecine.Qualite);
        var especes = Flore.Sur(carte.BiomeEn(panseur.X, panseur.Y));
        var plante = panseur.Savoirs.Where(x => x.Effet == Effet.Soulagement && x.Symptome == Symptome.Plaie && x.Geste == Geste.Appliquer
                                                 && x.Confiance >= 0.5 && especes.Any(e => e.Numero == x.Espece)).MaxBy(x => x.Confiance);
        if (plante is not null) soin += 0.5 * Flore.Get(plante.Espece).Antiseptique;
        blesse.PlaieSoin = Math.Clamp(soin, 0.05, 1);
        panseur.Activite = Activite.Soigner;
        panseur.CibleX = panseur.CibleY = -1;
        long id = 0;
        if (panseur.Id != blesse.Id)
        {
            var gestes = new Liste(techniques.Select(t => (object?)Recits.Recit.Seul(t == Matiere.Eau ? "med.panse.lave" : "med.panse.feuilles")).ToList(), virgules: true);
            id = Noter(_maintenant, TypeEvenement.Entraide, panseur, blesse, plante is null
                ? new Recit("med.panse", ("s", panseur), ("b", blesse), ("g", gestes))
                : new Recit("med.panse.plante", ("s", panseur), ("b", blesse), ("g", gestes), ("pl", Flore.Get(plante.Espece).Dit)), karma: Regard.Panser);
            MarquerSoin(panseur, blesse);
            Medecine.Placebo(blesse, panseur, false);
            VieSociale.Gratitude(panseur, blesse, 2);
        }
        var pansement = (techniques.ToArray(), panseur.Id, id);
        var blesseId = blesse.Id;
        Tour.Differer(() => _pansements[blesseId] = pansement);
        foreach (var t in techniques) Montrer(panseur, Geste.Panser, t, Matiere.Aucune, Effet.Pansement, 0, Symptome.Plaie, tous, id);
        if (plante is not null) Montrer(panseur, Geste.Appliquer, Matiere.Plante, Matiere.Aucune, Effet.Soulagement, plante.Espece, Symptome.Plaie, tous, id);
        return true;
    }

    private List<Matiere> TechniquesDePansement(Humain panseur)
    {
        var techniques = new List<Matiere>();
        foreach (var s in panseur.Savoirs)
        {
            if (s.Effet != Effet.Pansement || s.Confiance < 0.4 || techniques.Contains(s.A)) continue;
            if (s.A == Matiere.Eau ? EauPourPanser(panseur) : FeuillesPourPanser(panseur)) techniques.Add(s.A);
        }
        return techniques;
    }

    /// <summary>
    /// Un malade qui ne sait pas se soigner fait appeler le guérisseur, le chaman ou celui qui se prétend tel, s'il est tout près :
    /// d'autant plus volontiers qu'il croit en lui et en l'invisible. Pas plus d'une visite tous les deux jours.
    /// </summary>
    private bool AppelerGuerisseur(Humain h, Symptome mal, IReadOnlyList<Humain> tous)
    {
        if (h.SoigneurLe > 0 && _maintenant - h.SoigneurLe < 2 * JourDeSoin) return false;
        var grave = h.Sante < 60 || h.InfectionId != 0 || h.EstMalade(_maintenant) && Maladies.Fiche(h.Maladie).Gravite >= 1;
        Humain? choisi = null;
        var meilleur = 0.0;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.EstEnfant(_maintenant) || o.Activite == Activite.Dormir || o.EstMalade(_maintenant) || o.CaptifDe != 0) continue;
            if (Distance(o.X, o.Y, h.X, h.Y) > 1) continue;
            var proche = h.CommunauteId != 0 && o.CommunauteId == h.CommunauteId || VieSociale.Parents(o, h) || h.AffectionPour(o.Id) >= 30;
            if (!proche || o.AffectionPour(h.Id) < -10 || h.AffectionPour(o.Id) < -20) continue;
            var role = o.GuerisseurDepuis > 0 || o.CharlatanDepuis > 0
                       || grave && Medecine.PeutEntrerEnTranse(o, _maintenant) && _maintenant - o.TranseLe > 3 * JourDeSoin;
            if (!role) continue;
            var r = Medecine.Renommee(o) + 0.1;
            if (r > meilleur) (choisi, meilleur) = (o, r);
        }
        if (choisi is null || alea.Reel() >= 0.02 * (0.3 + meilleur + h.Croyance / 100)) return false;
        Visiter(choisi, h, mal, grave, tous);
        return true;
    }

    /// <summary>
    /// Le soignant au chevet du malade : un remède qu'il croit bon, sinon le soin de la plaie, sinon la transe si le mal est
    /// grave et qu'il en est capable, sinon des gestes et des paroles. Le charlatan, lui, ne sait que les gestes, et se fait nourrir.
    /// </summary>
    internal void Visiter(Humain s, Humain h, Symptome mal, bool grave, IReadOnlyList<Humain> tous)
    {
        // Pendant un pas simultané, le soignant vient après le pas, si tous deux vivent encore.
        if (Tour.Autrui(s) || Tour.Autrui(h))
        {
            Tour.Differer(tous, s, h, null, (t, x, y, _) =>
            {
                if (x.EstVivant && y.EstVivant) Visiter(x, y, mal, grave, t);
            });
            return;
        }
        s.Activite = Activite.Soigner;
        s.CibleX = s.CibleY = -1;
        MarquerSoin(s, h);
        var especes = Flore.Sur(carte.BiomeEn(h.X, h.Y));
        var remede = s.CharlatanDepuis > 0 ? null
            : s.Savoirs.Where(x => x.Effet == Effet.Soulagement && x.Symptome == mal && x.Confiance >= 0.5 && especes.Any(e => e.Numero == x.Espece)).MaxBy(x => x.Confiance);
        if (remede is not null)
        {
            Noter(_maintenant, TypeEvenement.Entraide, s, h,
                new Recit("med.remede", ("s", s), ("h", h), ("pl", Flore.Get(remede.Espece).Dit), ("mal", Physique.Mal(mal))),
                karma: Regard.Remede);
            Medecine.Placebo(h, s, false);
            PrendreRemede(h, remede.Espece, remede.Geste, mal, s.Id, tous, OrigineSavoir.Imitation);
            return;
        }
        if (mal == Symptome.Plaie && h.PlaieSoin == 0 && Medecine.PlaieOuverte(h, _maintenant) && Medecine.SaitPanser(s) && Panser(s, h, tous)) return;
        if (grave && Medecine.PeutEntrerEnTranse(s, _maintenant) && _maintenant - s.TranseLe > 3 * JourDeSoin)
        {
            Transe(s, h, tous);
            return;
        }
        if (s.CharlatanDepuis > 0)
        {
            var part = Math.Min(20, s.Faim);
            s.Faim -= part;
            h.Faim = Math.Min(100, h.Faim + part / 2);
            Noter(_maintenant, TypeEvenement.Medecine, s, h,
                new Recit("med.charlatan.soin", ("s", s), ("h", h)), karma: Regard.Tromper);
        }
        else
        {
            Noter(_maintenant, TypeEvenement.Medecine, s, h, new Recit("med.mains", ("s", s), ("h", h)), karma: Regard.Remede);
        }
        Medecine.Placebo(h, s, false);
    }

    /// <summary>
    /// La transe au chevet d'un malade : par une plante à visions qu'il connaît, le jeûne ou une nuit de veille. Le malade en
    /// attend beaucoup ; ceux qui la voient croient davantage à l'invisible, et le chaman lui-même aussi.
    /// </summary>
    internal void Transe(Humain s, Humain h, IReadOnlyList<Humain> tous)
    {
        var especes = Flore.Sur(carte.BiomeEn(s.X, s.Y));
        var vision = s.Savoirs.Where(x => x.Effet == Effet.Visions && especes.Any(e => e.Numero == x.Espece)).Select(x => Flore.Get(x.Espece)).FirstOrDefault();
        Recit moyen;
        if (vision is not null) moyen = new Recit("med.transe.plante", ("pl", vision.Dit));
        // Le rythme sans fin et la danse jusqu'à l'épuisement mènent aussi à la transe (Neher, 1962 ; Katz, 1982, chez les San).
        else if (Humains.Arts.Connait(s, Effet.Rythme)) moyen = Recits.Recit.Seul("med.transe.rythme");
        else if (Humains.Arts.Connait(s, Effet.Danse))
        {
            moyen = Recits.Recit.Seul("med.transe.danse");
            s.Fatigue = Math.Min(100, s.Fatigue + 30);
        }
        else if (s.Faim >= 40)
        {
            moyen = Recits.Recit.Seul("med.transe.jeune");
            s.Faim = Math.Min(100, s.Faim + 10);
        }
        else
        {
            moyen = Recits.Recit.Seul("med.transe.veille");
            s.Fatigue = Math.Min(100, s.Fatigue + 40);
        }
        var premiere = s.ChamanDepuis == 0;
        var premiereAuMonde = premiere && tous.All(o => o.ChamanDepuis == 0);
        var id = Noter(_maintenant, TypeEvenement.Medecine, s, h,
            new Recit(premiereAuMonde ? "med.transe.monde" : premiere ? "med.transe.premiere" : "med.transe", ("s", s), ("h", h), ("m", moyen)),
            significatif: premiereAuMonde, karma: Regard.Remede);
        if (premiere) s.ChamanDepuis = Math.Max(1, _maintenant);
        s.TranseLe = Math.Max(1, _maintenant);
        Croyances.Ajouter(s, 4, Recits.Recit.Seul("cro.transe"), id, _maintenant, signe: true);
        foreach (var o in tous.Vivants())
        {
            if (o.Id == s.Id || !o.EstVivant || o.X != s.X || o.Y != s.Y || o.EstBebe(_maintenant)) continue;
            Croyances.Ajouter(o, 3 * (0.3 + o.Personnalite.Transcendance), new Recit("cro.transeVue", ("s", s)), id, _maintenant, signe: true);
        }
        Jardin.Confier(s, TypeIntime.Croyance, h.Id, new Recit("js.transe", ("x", h)),
            40 + 40 * s.Personnalite.Transcendance, id, _maintenant, $"transe:{h.Id}");
        Medecine.Placebo(h, s, true);
    }

    /// <summary>
    /// Un chaman au cœur dur maudit celui qu'il hait. Celui qui croit fermement à l'invisible en est saisi d'effroi et dépérit ;
    /// celui qui n'y croit guère hausse les épaules.
    /// </summary>
    internal void Maudire(Humain s, Humain t)
    {
        var f = Math.Clamp((t.Croyance - 30) / 70 * (0.5 + t.Personnalite.Nevrosisme), 0, 1);
        // Maudire, c'est souhaiter qu'il dépérisse : presque un souhait de mort (gravité 0,7), mais déjà un acte.
        var id = Noter(_maintenant, TypeEvenement.Medecine, s, t, new Recit(f < 0.05 ? "med.maudit.hausse" : "med.maudit.effroi", ("s", s), ("t", t)),
            karma: Regard.Intention(s, t, 0.7, 0, malediction: true, _maintenant));
        Jardin.Confier(s, TypeIntime.Intention, t.Id, new Recit("js.maudire", ("x", t)), 30 + 40 * (1 - s.Personnalite.Moral.Compassion), id, _maintenant, $"maudire:{t.Id}");
        Social?.ChangerAffection(t, s, -15, _maintenant);
        if (f < 0.05) return;
        t.MauditLe = Math.Max(1, _maintenant);
        t.MauditParId = s.Id;
        t.MauditForce = Math.Round(f, 3);
        t.MauditEvenementId = id;
        t.Peur = Math.Max(t.Peur, 40 + 50 * f);
        t.PeurEvenementId = id;
        Jardin.Confier(t, TypeIntime.Peur, s.Id, new Recit("js.maudit", ("x", s), ("g", t)), 40 + 50 * f, id, _maintenant, $"maudit:{s.Id}");
    }

    /// <summary>La malédiction cesse : la peur reflue, ou un soignant l'a chassée.</summary>
    private void Delivrer(Humain t, Humain? soignant, IReadOnlyList<Humain> tous)
    {
        var s = t.MauditParId >= 1 && t.MauditParId <= tous.Count ? tous[t.MauditParId - 1] : null;
        if (soignant is null)
            Noter(_maintenant, TypeEvenement.Medecine, t, s, s is null ? new Recit("med.delivre", ("t", t)) : new Recit("med.delivre.de", ("t", t), ("s", s)), t.MauditEvenementId);
        else
            Noter(_maintenant, TypeEvenement.Medecine, soignant, t, s is null ? new Recit("med.chasse", ("x", soignant), ("t", t)) : new Recit("med.chasse.de", ("x", soignant), ("t", t), ("s", s)), t.MauditEvenementId,
                karma: Regard.Remede);
        t.MauditLe = 0;
        t.MauditParId = 0;
        t.MauditForce = 0;
        t.MauditEvenementId = 0;
        Jardin.Retirer(t, $"maudit:{s?.Id ?? 0}");
    }

    /// <summary>
    /// Chaque jour : qui a guéri ou non après des soins, ce que les pansements ont appris, les malédictions qui rongent ou se
    /// dissipent, les charlatans qui apparaissent, les guérisseurs que les siens reconnaissent ou désavouent, et les chamans
    /// qui maudissent.
    /// </summary>
    public void Soins(IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        // Guérisons : le dernier soignant en reçoit le crédit, mérité ou non.
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.SoigneurId == 0) continue;
            if (!Medecine.Souffrant(h, maintenant))
            {
                if (h.SoigneurId <= tous.Count)
                {
                    var s = tous[h.SoigneurId - 1];
                    s.Soignes++;
                    s.Gueris++;
                    VieSociale.Gratitude(s, h, 3);
                }
                h.SoigneurId = 0;
            }
            else if (maintenant - h.SoigneurLe > 60 * JourDeSoin)
            {
                if (h.SoigneurId <= tous.Count) tous[h.SoigneurId - 1].Soignes++;
                h.SoigneurId = 0;
            }
        }
        BilanDesPansements(tous, maintenant);
        Maledictions(tous, maintenant);
        Charlatans(tous, maintenant);
        Reconnaissance(tous, maintenant);
    }

    /// <summary>Une plaie soignée qui s'est refermée sans pourrir confirme le geste ; une plaie qui pourrit malgré lui le met en doute.</summary>
    private void BilanDesPansements(IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var (id, (techniques, panseurId, evenement)) in _pansements.ToList())
        {
            var h = tous[id - 1];
            var panseur = panseurId >= 1 && panseurId <= tous.Count ? tous[panseurId - 1] : null;
            var infectee = h.EstMalade(maintenant) && h.Maladie is Maladie.PlaieInfectee or Maladie.Tetanos;
            if (!h.EstVivant || panseur is null) _pansements.Remove(id);
            else if (infectee)
            {
                foreach (var t in techniques)
                {
                    foreach (var o in new[] { panseur, h })
                    {
                        var i = o.Savoirs.FindIndex(s => s.Effet == Effet.Pansement && s.A == t);
                        if (i >= 0) o.Savoirs[i] = o.Savoirs[i] with { Essais = o.Savoirs[i].Essais + 1, UtiliseLe = maintenant };
                    }
                }
                _pansements.Remove(id);
            }
            else if (!Medecine.PlaieOuverte(h, maintenant))
            {
                foreach (var t in techniques)
                {
                    if (panseur.EstVivant) Retenir(panseur, Geste.Panser, t, Matiere.Aucune, Effet.Pansement, 0, Symptome.Plaie, OrigineSavoir.Essai, 0, tous);
                    // Le blessé qu'on a soigné a vu faire : il peut retenir le geste.
                    if (h.Id != panseur.Id && !h.EstBebe(maintenant) && alea.Reel() < 0.5)
                        Retenir(h, Geste.Panser, t, Matiere.Aucune, Effet.Pansement, 0, Symptome.Plaie, OrigineSavoir.Imitation, panseur.Id, tous);
                }
                _pansements.Remove(id);
            }
        }
    }

    private void Maledictions(IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var t in tous.Vivants())
        {
            if (!t.EstVivant || t.MauditLe == 0) continue;
            var f = t.MauditForce;
            // Il a peur, mange à peine, s'assombrit (Cannon, 1942) ; la santé décline à chaque pas (voir Souffrir).
            t.Peur = Math.Max(t.Peur, 30 + 50 * f);
            t.Faim = Math.Min(100, t.Faim + 4 * f);
            t.Humeur = Math.Max(0, t.Humeur - 3 * f);
            // Un soignant (autre que celui qui a maudit) peut chasser la malédiction, s'il y croit et si le maudit croit en lui.
            Humain? soignant = null;
            foreach (var o in tous.Vivants())
            {
                if (o.Id == t.Id || o.Id == t.MauditParId || !o.EstVivant || o.EstEnfant(maintenant) || Distance(o.X, o.Y, t.X, t.Y) > 1) continue;
                if (o.GuerisseurDepuis == 0 && o.ChamanDepuis == 0 || o.AffectionPour(t.Id) < 0) continue;
                soignant = o;
                break;
            }
            if (soignant is not null && alea.Reel() < 0.3)
            {
                MarquerSoin(soignant, t);
                if (alea.Reel() < Medecine.Attente(t, soignant, soignant.ChamanDepuis > 0))
                {
                    Delivrer(t, soignant, tous);
                    continue;
                }
            }
            // Le temps fait refluer la peur, plus lentement quand elle est forte.
            if (alea.Reel() < (1.2 - 0.7 * f) / 45) Delivrer(t, null, tous);
        }
        // Les chamans au cœur dur maudissent ceux qu'ils haïssent.
        foreach (var s in tous.Vivants())
        {
            if (!s.EstVivant || s.ChamanDepuis == 0 || s.EstEnfant(maintenant)) continue;
            var p = s.Personnalite;
            if (!Psyche.EstPsychopathe(s, maintenant) && p.Moral.Compassion >= 0.35) continue;
            foreach (var l in s.Liens.ToList())
            {
                if (l.Affection > -40 || l.Id < 1 || l.Id > tous.Count) continue;
                var t = tous[l.Id - 1];
                if (!t.EstVivant || t.MauditLe > 0 || t.EstBebe(maintenant) || Distance(s.X, s.Y, t.X, t.Y) > 3) continue;
                if (alea.Reel() < 0.003 * (1 - p.Moral.Compassion) * (0.5 + s.Colere / 100)) Maudire(s, t);
            }
        }
    }

    /// <summary>Un manipulateur qui ne sait aucun remède se met à prétendre guérir : on nourrit et l'on respecte ceux qui soignent.</summary>
    private void Charlatans(IReadOnlyList<Humain> tous, long maintenant)
    {
        HashSet<int>? avecGuerisseur = null;
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.CommunauteId == 0 || h.CharlatanDepuis > 0 || h.GuerisseurDepuis > 0 || h.EstEnfant(maintenant)) continue;
            if (h.DemasqueLe > 0 && maintenant - h.DemasqueLe < 5 * Calendrier.SecondesParAn || !Medecine.PorteAuCharlatanisme(h, maintenant)) continue;
            if (h.Savoirs.Exists(s => s.Effet == Effet.Soulagement && s.Confiance >= 0.5)) continue;
            // Voir un guérisseur honoré donne l'idée (trois fois plus).
            avecGuerisseur ??= tous.Vivants().Where(o => o.EstVivant && (o.GuerisseurDepuis > 0 || o.ChamanDepuis > 0)).Select(o => o.CommunauteId).ToHashSet();
            var chance = (avecGuerisseur.Contains(h.CommunauteId) ? 3 : 1) / (360.0 * 40);
            if (alea.Reel() >= chance) continue;
            h.CharlatanDepuis = Math.Max(1, maintenant);
            var id = Noter(maintenant, TypeEvenement.Medecine, h, null,
                new Recit("med.charlatan", ("h", h)));
            Jardin.Confier(h, TypeIntime.Intention, 0, new Recit("js.charlatan", ("g", h)),
                30 + 40 * (1 - h.Personnalite.Moral.Honnetete), id, maintenant, "charlatan");
        }
    }

    /// <summary>
    /// Les siens reconnaissent pour guérisseur celui qui a guéri nettement plus de malades que les autres, et en perdu peu ;
    /// ils cessent de croire en lui s'il en perd trop. Le charlatan est démasqué plus vite : on attend des résultats de celui
    /// qui se fait nourrir pour ses soins (seuils estimés).
    /// </summary>
    private void Reconnaissance(IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var g in tous.Vivants().Where(h => h.EstVivant && h.CommunauteId != 0 && !h.EstEnfant(maintenant)).GroupBy(h => h.CommunauteId))
        {
            var membres = g.ToList();
            var moyenne = membres.Average(h => h.Gueris);
            foreach (var o in membres)
            {
                if (o.GuerisseurDepuis == 0 && (o.DemasqueLe == 0 || maintenant - o.DemasqueLe > 3 * Calendrier.SecondesParAn)
                    && o.Gueris >= 4 && Medecine.Taux(o) >= 0.6 && o.Gueris >= 3 * moyenne + 1)
                {
                    var premier = tous.All(x => x.GuerisseurDepuis == 0);
                    o.GuerisseurDepuis = Math.Max(1, maintenant);
                    Noter(maintenant, TypeEvenement.Medecine, o, null,
                        new Recit(premier ? "med.guerisseur.premier" : "med.guerisseur", ("h", o), ("g", o.Gueris), ("m", o.Perdus)), significatif: premier);
                    continue;
                }
                var charlatan = o.CharlatanDepuis > 0;
                var desavoue = charlatan ? o.Perdus >= 2 && o.Perdus * 3 >= o.Gueris : o.GuerisseurDepuis > 0 && o.Perdus >= 3 && Medecine.Taux(o) < 0.5;
                if (!desavoue) continue;
                if (charlatan)
                {
                    Noter(maintenant, TypeEvenement.Medecine, o, null,
                        new Recit("med.demasque", ("h", o), ("m", o.Perdus), ("n", o.Soignes)));
                    foreach (var m in membres)
                        if (m.Id != o.Id) Social?.ChangerAffection(m, o, -8, maintenant);
                    Jardin.Retirer(o, "charlatan");
                }
                else
                {
                    Noter(maintenant, TypeEvenement.Medecine, o, null,
                        new Recit("med.desavoue", ("h", o), ("m", o.Perdus), ("g", o.Gueris)));
                }
                o.CharlatanDepuis = 0;
                o.GuerisseurDepuis = 0;
                o.DemasqueLe = Math.Max(1, maintenant);
            }
        }
    }
}
