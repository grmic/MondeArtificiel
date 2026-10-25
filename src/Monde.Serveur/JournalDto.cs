using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un événement du journal tel qu'envoyé au navigateur.</summary>
/// <param name="Cause">Numéro de l'événement qui l'a provoqué (0 : aucune cause notée).</param>
/// <param name="Significatif">Un tournant (projet de guerre, renoncement, raid, paix, catastrophe…).</param>
/// <param name="Karma">Karma gagné ou perdu par l'humain par cet acte (regard de l'Entité) ; <paramref name="KarmaAutre"/> : par l'autre.</param>
public sealed record EvenementDto(long Id, long Secondes, string Date, string Type, int Humain, int Autre, string Texte, string Origine,
    long Cause, int Communaute, int AutreCommunaute, bool Significatif, double Karma = 0, double KarmaAutre = 0,
    bool Ancien = false)
{
    /// <summary>Clés des types, dans l'ordre de l'énumération (le navigateur filtre avec ces clés).</summary>
    public static readonly IReadOnlyDictionary<TypeEvenement, string> Cles = new Dictionary<TypeEvenement, string>
    {
        [TypeEvenement.Naissance] = "naissance",
        [TypeEvenement.Mort] = "mort",
        [TypeEvenement.Maladie] = "maladie",
        [TypeEvenement.Union] = "union",
        [TypeEvenement.Accouplement] = "accouplement",
        [TypeEvenement.Grossesse] = "grossesse",
        [TypeEvenement.FausseCouche] = "fausse-couche",
        [TypeEvenement.Delaissement] = "delaissement",
        [TypeEvenement.Decouverte] = "decouverte",
        [TypeEvenement.MortNe] = "mort-ne",
        [TypeEvenement.Pensee] = "pensee",
        [TypeEvenement.Lien] = "lien",
        [TypeEvenement.Humeur] = "humeur",
        [TypeEvenement.Communaute] = "communaute",
        [TypeEvenement.Souvenir] = "souvenir",
        [TypeEvenement.Partage] = "partage",
        [TypeEvenement.Journee] = "journee",
        [TypeEvenement.Nature] = "nature",
        [TypeEvenement.Conflit] = "conflit",
        [TypeEvenement.Catastrophe] = "catastrophe",
        [TypeEvenement.Guerre] = "guerre",
        [TypeEvenement.Attirance] = "attirance",
        [TypeEvenement.Exclusion] = "exclusion",
        [TypeEvenement.Entraide] = "entraide",
        [TypeEvenement.Vol] = "vol",
        [TypeEvenement.Parole] = "parole",
        [TypeEvenement.Croyance] = "croyance",
        [TypeEvenement.Medecine] = "medecine",
        [TypeEvenement.Art] = "art",
        [TypeEvenement.Langage] = "langage",
        [TypeEvenement.Mots] = "mots",
        [TypeEvenement.Religion] = "religion",
        [TypeEvenement.Geste] = "geste",
        [TypeEvenement.Mer] = "mer",
    };


    public static EvenementDto Depuis(EvenementJournal e) => new(e.Id, e.Secondes, L.Date(e.Secondes),
        Cles.GetValueOrDefault(e.Type, "autre"), e.HumainId, e.AutreId, L.Ecrire(e),
        e.Origine == Simulation.Origine.Entite ? "entite" : "naturelle", e.CauseId, e.CommunauteId, e.AutreCommunauteId, e.Significatif,
        e.Karma, e.KarmaAutre, L.Ancien(e.Recit));

    /// <summary>Traduit les clés reçues ; une clé inconnue rend la demande invalide (null).</summary>
    public static List<TypeEvenement>? TypesDepuis(IEnumerable<string>? cles)
    {
        var types = new List<TypeEvenement>();
        foreach (var cle in cles ?? [])
        {
            var trouve = Cles.FirstOrDefault(c => c.Value == cle);
            if (trouve.Value is null) return null;
            types.Add(trouve.Key);
        }
        return types;
    }
}

/// <summary>Filtre du journal demandé par le navigateur.</summary>
/// <param name="Communaute">Chronologie d'une communauté : les événements qui la concernent.</param>
/// <param name="Significatifs">Seulement les événements significatifs.</param>
public sealed record FiltreJournal(string[]? Types = null, int? Humain = null, long? AvantId = null, int Limite = 100, int? Communaute = null,
    bool Significatifs = false)
{
    /// <summary>Au plus un exemplaire de chaque type connu, et une limite entre 1 et la limite du dépôt.</summary>
    public bool EstValide => (Types is not { } t || t.Length <= EvenementDto.Cles.Count)
        && Limite is >= 1 and <= Stockage.DepotMonde.LimiteJournal;
}

public sealed record LieuDto(string Type, int X, int Y, string VuLe, int Visites);

public sealed record PenseeDto(string Date, string Texte, bool Ancien = false);

/// <summary>
/// Une chose du jardin secret : sa nature (intention, peur, espoir…), qui elle concerne (0 : personne), ce qu'il se dit,
/// sa force (0 à 100) et l'événement qui l'a fait naître.
/// </summary>
public sealed record IntimeDto(string Date, string Type, int Sujet, string? NomSujet, string Texte, int Intensite, long Cause, bool Ancien = false);

/// <summary>Un trait affiché : nom, valeur (0 à 1, ou QI) et qualification en mots.</summary>
public sealed record TraitDto(string Nom, double Valeur, string Mots, string Aide);

public sealed record LienDto(int Id, string Nom, int Familiarite, int Affection, string Statut, bool Vivant);

/// <summary>Une rancune : contre qui, pourquoi, avec quelle force (0 à 100), et l'événement qui l'a fait naître.</summary>
public sealed record RancuneDto(int Id, string Nom, string Motif, int Intensite, long Evenement);

/// <param name="Raisons">Ce qui pèse sur son humeur, en mots, du plus lourd au plus léger.</param>
public sealed record AmeDto(string Etat, int Humeur, int Chagrin, int DeuilDe, string? DeprimeDepuis, IReadOnlyList<string> Raisons,
    int Colere, IReadOnlyList<RancuneDto> Rancunes, long DepressionEvenement);

public sealed record CommunauteRefDto(int Id, string Nom);

/// <summary>Maladie en cours, immunités et peur laissée par une catastrophe.</summary>
/// <param name="Maladie">Maladie qu'il porte (null s'il n'en porte pas), et depuis quand.</param>
/// <param name="Immunites">« fièvre des marais (à vie) », « toux rouge (jusqu'à l'an 12) ».</param>
public sealed record EpreuvesDto(string? Maladie, string? MaladieDepuis, long MaladieEvenement, IReadOnlyList<string> Immunites,
    int Peur, string? PeurDe, long PeurEvenement);

/// <summary>Une exclusion : par qui, depuis quand, jusqu'à quand (null : pour toujours), pourquoi, et si elle dure encore.</summary>
public sealed record ExclusionDto(string Type, int Par, string NomPar, string Le, string? Jusqua, string Raison, bool EnCours, long Evenement);

/// <summary>
/// Attraits et orientation. Beauté et attractivité de 0 à 100, aux yeux de sa communauté ; orientation de 0 à 100 vers
/// chaque sexe ; ce qu'il en sait (null s'il n'y a rien à savoir) ; idéal de beauté de son groupe. Avant la puberté,
/// l'attirance et l'orientation ne se sont pas encore éveillées : Impubere vaut vrai et elles ne sont pas affichées.
/// </summary>
public sealed record AttraitsDto(int Beaute, int Attractivite, string Orientation, int VersHommes, int VersFemmes, string? Conscience,
    string Ideal, IReadOnlyList<ExclusionDto> Exclusions, bool Impubere);

/// <summary>
/// Le regard de l'Entité, que l'humain ignore : son karma et sa croyance en l'invisible (0 à 100), en chiffres et en mots,
/// et le dernier signe de l'invisible qu'il a reçu (null : aucun).
/// </summary>
/// <param name="Amour">Regard sur l'invisible (0 à 100) : amour, crainte et révolte, avec leurs mots ; <paramref name="DernierGeste"/> : date du dernier signe vu.</param>
public sealed record RegardDto(double Karma, string KarmaMots, int Croyance, string CroyanceMots, string? DernierSigne,
    int Amour = 0, string AmourMots = "", int Crainte = 0, string CrainteMots = "", int Revolte = 0, string RevolteMots = "", string? DernierGeste = null);

/// <summary>
/// Médecine : ce qu'il a fait comme soignant (soignés, guéris, perdus, accouchements aidés), son rôle et sa renommée
/// (0 à 100), sa plaie et la malédiction qui le ronge éventuellement.
/// </summary>
public sealed record MedecineDto(int Soignes, int Gueris, int Perdus, int AccouchementsAides, string? Role, int Renommee,
    string? GuerisseurDepuis, string? ChamanDepuis, string? CharlatanDepuis, string? DesavoueLe, string? Plaie, string? Malediction);

/// <summary>
/// Le feu et les réserves d'un humain : le feu qu'il porte (null : aucun), depuis quand il est le porteur du feu de son groupe et
/// combien de fois il l'a porté, ses caches d'eau et de nourriture, et ce qu'il sait du feu.
/// </summary>
public sealed record FeuDto(string? Porte, string? PorteurDepuis, int FeuxPortes, IReadOnlyList<string> Caches, IReadOnlyList<string> Lecons);

/// <summary>
/// Ses rencontres et ses ouvrages : la visite en cours (ou le rassemblement), la communauté d'où il est venu, ce que les siens disent
/// qu'il fait pour eux, les ouvrages qu'il a faits pour d'autres, et celui à qui il a demandé un ouvrage.
/// </summary>
public sealed record RencontresDto(string? Visite, string? Rassemblement, string? VenuDe, string? Metier, string? MetierDepuis,
    int ServisTaille, int ServisAssemblage, int ServisPeau, string? Attend);

/// <summary>Un soignant d'une communauté : guérisseur, chaman ou charlatan.</summary>
public sealed record SoignantDto(int Id, string Nom, string Role, int Gueris, int Perdus);

/// <summary>Une image peinte sur la roche par un membre de la communauté.</summary>
public sealed record OeuvreDto(int Id, int AuteurId, string Auteur, string Le, string Description, long EvenementId);

/// <summary>Fiche complète d'un humain : état, caractère, intelligence, humeur, liens, mémoire, pensées et histoire.</summary>
public sealed record FicheDto(
    HumainDto Humain,
    IReadOnlyList<TraitDto> Caractere,
    IReadOnlyList<TraitDto> Intelligence,
    IReadOnlyDictionary<string, double> Biologie,
    AmeDto? Ame,
    CommunauteRefDto? Communaute,
    IReadOnlyList<int> Enfants,
    IReadOnlyList<PenseeDto> Pensees,
    IReadOnlyList<IntimeDto> Jardin,
    IReadOnlyList<LieuDto> Lieux,
    IReadOnlyList<LienDto> Liens,
    IReadOnlyList<EvenementDto> Histoire,
    EpreuvesDto Epreuves,
    AttraitsDto Attraits,
    IReadOnlyList<TraitDto> Coeur,
    IReadOnlyList<TraitDto> Morale,
    IReadOnlyList<string> Portrait,
    SavoirsDto Savoirs,
    IReadOnlyList<ParoleRecueDto> Paroles,
    RegardDto Regard,
    MedecineDto Medecine,
    LangageDto? Langage = null,
    FoiDto? Foi = null,
    FeuDto? Feu = null,
    RencontresDto? Rencontres = null)
{
    private static string Date(long s) => L.Date(s);

    private static TraitDto Trait(string cle, double v) => new(L.T($"trait.{cle}"), Math.Round(v, 2), Lib.Niveau(v), L.T($"trait.{cle}.aide"));
    private static TraitDto Qi(string cle, double v) => new(L.T($"aptitude.{cle}"), Math.Round(v), Lib.Qi(v), L.T($"aptitude.{cle}.aide"));

    public static FicheDto? Depuis(ServiceMonde service, int id)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        if (id < 1 || id > humains.Count) return null;
        var h = humains[id - 1];
        string Nom(int i) => i >= 1 && i <= humains.Count ? humains[i - 1].Nom : "?";
        var p = h.Personnalite;
        var i = p.Intelligence;
        var b = h.Biologie;
        var communaute = h.CommunauteId == 0 ? null : monde.Communautes().FirstOrDefault(c => c.Id == h.CommunauteId);
        // L'histoire ne reprend pas les pensées : elles ont leur propre rubrique.
        var types = EvenementDto.Cles.Keys.Where(t => t != TypeEvenement.Pensee).ToList();
        return new FicheDto(
            HumainDto.Depuis(h, maintenant),
            [
                Trait("ouverture", p.Ouverture),
                Trait("conscience", p.Conscience),
                Trait("extraversion", p.Extraversion),
                Trait("agreabilite", p.Agreabilite),
                Trait("nevrosisme", p.Nevrosisme),
                Trait("transcendance", p.Transcendance),
                Trait("charisme", p.Charisme),
                Trait("attachement", p.Attachement),
                Trait("endurance", p.Endurance),
            ],
            [
                Qi("generale", i.Generale),
                Qi("verbale", i.Verbale),
                Qi("spatiale", i.Spatiale),
                Qi("manuelle", i.Manuelle),
                Qi("memoire", i.Memoire),
                Qi("artistique", i.Artistique),
            ],
            new Dictionary<string, double>
            {
                [L.T("biologie.puberte")] = Math.Round(b.AgePuberte, 1),
                [L.T("biologie.finFertilite")] = Math.Round(b.AgeFinFertilite, 1),
                [L.T("biologie.fertilite")] = Math.Round(b.Fertilite, 2),
                [L.T("biologie.metabolisme")] = Math.Round(b.Metabolisme, 2),
                [L.T("biologie.allure")] = Math.Round(b.Marche, 2),
                [L.T("biologie.marche")] = h.EstVivant && !h.EstBebe(maintenant) ? Math.Round(Marche.Vitesse(h, maintenant) * 3.6, 1) : 0,
            },
            h.EstVivant && !h.EstBebe(maintenant)
                ? new AmeDto(Lib.Humeur(h), (int)Math.Round(h.Humeur), (int)Math.Round(h.Chagrin), h.DeuilDeId,
                    h.DeprimeDepuis is { } d ? Date(d) : null, Lib.Raisons(h, humains, maintenant),
                    (int)Math.Round(h.Colere),
                    h.Griefs.OrderByDescending(g => g.Intensite)
                        .Select(g => new RancuneDto(g.ContreId, Nom(g.ContreId), Motif(g, Nom), (int)Math.Round(g.Intensite), g.EvenementId))
                        .ToList(),
                    h.DepressionEvenementId)
                : null,
            communaute is null ? null : new CommunauteRefDto(communaute.Id, L.Communaute(communaute.Nom)),
            humains.Where(e => e.MereId == id || e.PereId == id).Select(e => e.Id).ToList(),
            h.Memoire.Pensees.AsEnumerable().Reverse().Select(x => new PenseeDto(Date(x.Secondes), L.Ecrire(x), L.Ancien(x.Recit))).ToList(),
            h.Memoire.Jardin.OrderBy(x => x.Type).ThenByDescending(x => x.Intensite)
                .Select(x => new IntimeDto(Date(x.Secondes), TypeIntime(x.Type), x.SujetId, x.SujetId > 0 && x.SujetId != id ? Nom(x.SujetId) : null,
                    x.Recit is { } r ? L.Rediger(r) : x.Brut ?? "", (int)Math.Round(x.Intensite), x.CauseId, L.Ancien(x.Recit))).ToList(),
            h.Memoire.Lieux.OrderBy(l => l.Type).ThenByDescending(l => l.Rappels).ThenByDescending(l => l.VuLe)
                .Select(l => new LieuDto(l.Type switch { TypeLieu.Eau => "eau", TypeLieu.Nourriture => "nourriture", TypeLieu.Gisement => "gisement", TypeLieu.TerreAuLoin => "terre", _ => "abri" },
                    l.X, l.Y, Date(l.VuLe), l.Rappels + 1)).ToList(),
            h.Liens.OrderByDescending(l => Math.Abs(l.Affection)).ThenByDescending(l => l.Familiarite)
                .Select(l => new LienDto(l.Id, Nom(l.Id), (int)Math.Round(l.Familiarite), (int)Math.Round(l.Affection),
                    Statut(l), humains[l.Id - 1].EstVivant)).ToList(),
            Journal(service, new FiltreJournal(Humain: id, Limite: 200), types).Select(EvenementDto.Depuis).ToList(),
            LireEpreuves(h, monde.Pathogenes(), maintenant),
            LireAttraits(h, humains, monde.Communautes(), maintenant),
            [
                Trait("compassion", p.Moral.Compassion),
                Trait("empathieEmotionnelle", p.Moral.EmpathieEmotionnelle),
                Trait("empathieCognitive", p.Moral.EmpathieCognitive),
                Trait("honnetete", p.Moral.Honnetete),
                Trait("psychopathie", p.Moral.Psychopathie),
                Trait("anxieteAffective", p.Moral.AnxieteAffective),
                Trait("evitementAffectif", p.Moral.EvitementAffectif),
            ],
            [
                Trait("soin", p.Moral.Fondements.Soin),
                Trait("equite", p.Moral.Fondements.Equite),
                Trait("loyaute", p.Moral.Fondements.Loyaute),
                Trait("autorite", p.Moral.Fondements.Autorite),
                Trait("purete", p.Moral.Fondements.Purete),
            ],
            Decrire(h, maintenant),
            SavoirsDto.Depuis(h, humains, monde.Flore),
            ParolesEntite.Recues(h, humains, service, maintenant),
            new RegardDto(Math.Round(h.Karma, 1), Lib.Karma(h.Karma), (int)Math.Round(h.Croyance), Lib.Croyance(h.Croyance),
                h.CroyanceSigneLe > 0 ? Date(h.CroyanceSigneLe) : null,
                (int)Math.Round(h.Amour), Lib.Sentiment(SentimentInvisible.Amour, h.Amour, h),
                (int)Math.Round(h.Crainte), Lib.Sentiment(SentimentInvisible.Crainte, h.Crainte, h),
                (int)Math.Round(h.Revolte), Lib.Sentiment(SentimentInvisible.Revolte, h.Revolte, h),
                h.RegardSigneLe > 0 ? Date(h.RegardSigneLe) : null),
            Soins(h, humains, maintenant),
            LangageDto.Depuis(h, humains, monde.Communautes(), monde.Flore),
            Religions.Foi(service, h, humains),
            LireFeu(monde, h),
            LireRencontres(monde, h, humains));
    }

    private static RencontresDto? LireRencontres(MondeSimule monde, Humain h, IReadOnlyList<Humain> humains)
    {
        var communautes = monde.Communautes();
        string Nom(int id) => id >= 1 && id <= communautes.Count ? L.CommunautePhrase(communautes[id - 1].Nom) : "?";
        string NomMin(int id) => id >= 1 && id <= communautes.Count ? L.CommunauteMin(communautes[id - 1].Nom) : "?";
        string De(int id) => id >= 1 && id <= communautes.Count ? L.T("rencontre.de", ("nom", Nom(id)), ("nomMin", NomMin(id))) : L.T("rencontre.dAilleurs");
        string? visite = null;
        if (h.VisiteChezId >= 1 && h.VisiteChezId <= humains.Count)
        {
            var o = humains[h.VisiteChezId - 1];
            visite = h.VisiteArriveeLe == 0
                ? L.T("rencontre.enRoute", ("nom", o.Nom), ("de", De(o.CommunauteId)), ("date", Date(h.VisiteDepuis)), ("g", Lib.G(h)))
                : L.T("rencontre.chez", ("nom", o.Nom), ("de", De(o.CommunauteId)), ("depuis", Date(h.VisiteArriveeLe)), ("jusqua", Date(h.VisiteJusquA)));
        }
        string? rassemblement = null;
        if (h.RassemblementId > 0 && monde.Rassemblements() is var rs && h.RassemblementId <= rs.Count && rs[h.RassemblementId - 1] is { Actif: true } r)
            {
            var avec = r.HoteId == h.CommunauteId ? r.InviteeId : r.HoteId;
            rassemblement = L.T("rencontre.campe", ("nom", Nom(avec)), ("nomMin", NomMin(avec)), ("date", Date(r.Debut)));
        }
        var attend = h.DemandeAId >= 1 && h.DemandeAId <= humains.Count ? L.T("rencontre.demande", ("nom", humains[h.DemandeAId - 1].Nom), ("date", Date(h.DemandeLe))) : null;
        var dto = new RencontresDto(visite, rassemblement, h.VenuDeId != 0 ? L.T("rencontre.venu", ("de", De(h.VenuDeId)), ("g", Lib.G(h))) : null,
            Lib.Metier(h), h.Specialite != Ouvrage.Aucun ? Date(h.SpecialiteDepuis) : null, h.ServisTaille, h.ServisAssemblage, h.ServisPeau, attend);
        return dto is { Visite: null, Rassemblement: null, VenuDe: null, Metier: null, ServisTaille: 0, ServisAssemblage: 0, ServisPeau: 0, Attend: null } ? null : dto;
    }

    private static readonly Effet[] LeconsDuFeu =
        [Effet.Braises, Effet.PorteFeu, Effet.FoyerDegage, Effet.PasDansLeVent, Effet.EteindreEnPartant, Effet.BrulisProtecteur];

    private static FeuDto LireFeu(MondeSimule monde, Humain h)
    {
        var porte = h.Objets.Find(o => o.Matiere is Matiere.Brandon or Matiere.PorteFeu);
        var (_, reserves) = monde.BraisesEtReserves();
        var caches = reserves.Where(r => r.AuteurId == h.Id && r.Kg > 0)
            .Select(r => L.T("cache.phrase", ("reserve", Lib.Reserve(r.Sorte, r.Kg)), ("x", r.X), ("y", r.Y),
                ("etat", L.T(r.Etat >= 0.8 ? "cache.intacte" : r.Etat >= 0.4 ? "cache.entamee" : "cache.gatee"))))
            .ToList();
        var lecons = LeconsDuFeu.Where(e => h.Savoirs.Exists(s => s.Effet == e && s.Confiance >= 0.4)).Select(e => L.T($"effet.{e}")).ToList();
        return new FeuDto(porte is null ? null : L.T($"matiere.{porte.Matiere}"), h.PorteurDuFeuDepuis > 0 ? Date(h.PorteurDuFeuDepuis) : null,
            h.FeuxPortes, caches, lecons);
    }

    private static MedecineDto Soins(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        string? plaie = null;
        if (h.EstVivant && Simulation.Humains.Medecine.PlaieOuverte(h, maintenant))
            plaie = h.PlaieSoin > 0 ? L.T("soin.plaieSoignee", ("soin", h.PlaieSoin.ToString("0.00", L.Courante.Nombres))) : L.T("soin.plaieOuverte");
        string? malediction = null;
        if (h.MauditLe > 0)
        {
            var par = h.MauditParId >= 1 && h.MauditParId <= humains.Count ? humains[h.MauditParId - 1].Nom : "?";
            malediction = L.T("soin.maudit", ("par", par), ("date", Date(h.MauditLe)), ("force", h.MauditForce.ToString("0.00", L.Courante.Nombres)), ("g", Lib.G(h)));
        }
        return new MedecineDto(h.Soignes, h.Gueris, h.Perdus, h.AccouchementsAides, Lib.Soignant(h),
            (int)Math.Round(100 * Simulation.Humains.Medecine.Renommee(h)),
            h.GuerisseurDepuis > 0 ? Date(h.GuerisseurDepuis) : null, h.ChamanDepuis > 0 ? Date(h.ChamanDepuis) : null,
            h.CharlatanDepuis > 0 ? Date(h.CharlatanDepuis) : null, h.DemasqueLe > 0 ? Date(h.DemasqueLe) : null, plaie, malediction);
    }

    /// <summary>Le caractère en quelques phrases : style d'attachement, psychopathie (adulte), morale dominante.</summary>
    private static List<string> Decrire(Humain h, long maintenant)
    {
        var m = h.Personnalite.Moral;
        var g = Lib.G(h);
        var phrases = new List<string>();
        if (h.AgeEnAnnees(maintenant) >= 12)
        {
            phrases.Add(m.Style switch
            {
                StyleAttachement.Secure => L.T("portrait.secure", ("g", g)),
                StyleAttachement.Evitant => L.T("portrait.evitant", ("g", g)),
                _ => L.T("portrait.anxieux", ("g", g)),
            });
        }
        if (Psyche.EstPsychopathe(h, maintenant))
            phrases.Add(L.T("portrait.psychopathe", ("g", g)));
        else if (Psyche.Froideur(h.Personnalite) >= 0.5)
            phrases.Add(L.T("portrait.froid", ("g", g)));
        var f = m.Fondements;
        var ecart = f.Individualisants - f.Liants;
        if (Math.Abs(ecart) >= 0.08)
            phrases.Add(L.T(ecart > 0 ? "portrait.moraleIndividualisante" : "portrait.moraleLiante", ("g", g)));
        return phrases;
    }

    private static AttraitsDto LireAttraits(Humain h, List<Humain> humains, List<Communaute> communautes, long maintenant)
    {
        var groupe = h.CommunauteId > 0 && h.CommunauteId <= communautes.Count ? communautes[h.CommunauteId - 1] : null;
        var ideal = groupe?.Ideal ?? IdealBeaute.ParDefaut;
        var tolerance = groupe is { ToleranceMoeurs: >= 0 } ? groupe.ToleranceMoeurs : VieSociale.TemperamentMoeurs(h.Personnalite);
        var instant = h.MortLe ?? maintenant;
        string Ref(Exclusion e) => e.Type == TypeExclusion.Famille
            ? (e.De >= 1 && e.De <= humains.Count ? humains[e.De - 1].Nom : "?")
            : (e.De >= 1 && e.De <= communautes.Count ? communautes[e.De - 1].Nom : "?");
        return new AttraitsDto(
            (int)Math.Round(Attirance.Beaute(h, ideal, instant)), (int)Math.Round(Attirance.Attractivite(h, ideal, instant)),
            Lib.Orientation(h), (int)Math.Round(100 * h.Orientation.VersHommes), (int)Math.Round(100 * h.Orientation.VersFemmes),
            h.EstEnfant(instant) ? null : Lib.Conscience(h, tolerance),
            groupe is null ? L.T("ideal.sansCommunaute") : Lib.Ideal(ideal),
            h.Exclusions.AsEnumerable().Reverse().Select(e => new ExclusionDto(
                e.Type switch { TypeExclusion.Famille => "renié", TypeExclusion.Depart => "parti avec un exilé", _ => "exilé" },
                e.De, Ref(e), Date(e.Le), e.Jusqua == 0 ? null : Date(e.Jusqua), L.Garde(e.Raison), e.Active(maintenant), e.EvenementId)).ToList(),
            h.AgeEnAnnees(instant) < h.Biologie.AgePuberte);
    }

    private static EpreuvesDto LireEpreuves(Humain h, List<Simulation.Nature.Pathogene> maladies, long maintenant)
    {
        string NomMaladie(int id) => id >= 1 && id <= maladies.Count ? maladies[id - 1].Nom : L.T("maladieOrdinaire.Aucune");
        var porteur = h.EstVivant && h.InfectionId != 0;
        // Maladie ordinaire (non contagieuse) : nommée pour l'observateur, l'humain ne sait pas la nommer.
        var ordinaire = !porteur && h.EstVivant && h.EstMalade(maintenant) && h.Maladie != Maladie.Aucune
            ? L.T("epreuve.sansNom", ("maladie", Lib.Maladie(h.Maladie))) : null;
        return new EpreuvesDto(
            porteur ? maintenant < h.SymptomesLe ? L.T("epreuve.sansSignes", ("maladie", NomMaladie(h.InfectionId))) : NomMaladie(h.InfectionId) : ordinaire,
            porteur ? Date(h.InfecteLe) : null, porteur ? h.InfectionEvenementId : 0,
            h.Immunites.Where(i => i.Jusqua is null || i.Jusqua > maintenant)
                .Select(i => i.Jusqua is { } j ? L.T("epreuve.immuniteJusqua", ("maladie", NomMaladie(i.PathogeneId)), ("an", Calendrier.Decomposer(j).Annee.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    : L.T("epreuve.immuniteAVie", ("maladie", NomMaladie(i.PathogeneId))))
                .ToList(),
            h.EstVivant ? (int)Math.Round(h.Peur) : 0,
            h.EstVivant && h.Peur >= 1 ? Lib.Catastrophe(h.PeurType) : null,
            h.EstVivant && h.Peur >= 1 ? h.PeurEvenementId : 0);
    }

    private static string TypeIntime(TypeIntime t) => t switch
    {
        Simulation.Humains.TypeIntime.Intention => "intention",
        Simulation.Humains.TypeIntime.Peur => "peur",
        Simulation.Humains.TypeIntime.Espoir => "espoir",
        Simulation.Humains.TypeIntime.Joie => "joie",
        Simulation.Humains.TypeIntime.Peine => "peine",
        Simulation.Humains.TypeIntime.Opinion => "opinion",
        Simulation.Humains.TypeIntime.ImageDeSoi => "image-de-soi",
        Simulation.Humains.TypeIntime.Croyance => "croyance",
        Simulation.Humains.TypeIntime.Desir => "desir",
        Simulation.Humains.TypeIntime.Amour => "amour",
        Simulation.Humains.TypeIntime.Jalousie => "jalousie",
        Simulation.Humains.TypeIntime.Manque => "manque",
        Simulation.Humains.TypeIntime.Reve => "reve",
        Simulation.Humains.TypeIntime.Souvenir => "souvenir",
        Simulation.Humains.TypeIntime.Fierte => "fierte",
        Simulation.Humains.TypeIntime.Honte => "honte",
        Simulation.Humains.TypeIntime.Culpabilite => "culpabilite",
        Simulation.Humains.TypeIntime.Envie => "envie",
        Simulation.Humains.TypeIntime.Emerveillement => "emerveillement",
        Simulation.Humains.TypeIntime.Ennui => "ennui",
        _ => "question",
    };

    private static string Motif(Grief g, Func<int, string> nom) => g.Motif switch
    {
        MotifGrief.Infidelite or MotifGrief.Rival or MotifGrief.Coups or MotifGrief.Moeurs or MotifGrief.Vol or MotifGrief.Trahison => L.T($"motif.{g.Motif}"),
        _ => g.VictimeId > 0 ? L.T("motif.aTue", ("nom", nom(g.VictimeId))) : L.T("motif.meurtre"),
    };

    /// <summary>Code du lien (le navigateur l'écrit avec la clé statut.&lt;code&gt; et le colore).</summary>
    private static string Statut(Lien l) => l switch
    {
        { Rivaux: true } => "hostile",
        { Affection: >= 70 } => "tresProche",
        { Amis: true } => "ami",
        { Affection: <= -10 } => "mefiance",
        { Familiarite: >= 30 } => "familier",
        _ => "connaissance",
    };

    /// <summary>Journal enregistré et événements récents pas encore enregistrés, fusionnés, du plus récent au plus ancien.</summary>
    public static List<EvenementJournal> Journal(ServiceMonde service, FiltreJournal filtre, List<TypeEvenement> types)
    {
        var limite = Math.Clamp(filtre.Limite, 1, Stockage.DepotMonde.LimiteJournal);
        bool Convient(EvenementJournal e) =>
            (types.Count == 0 || types.Contains(e.Type))
            && (filtre.Humain is not { } h || e.HumainId == h || e.AutreId == h)
            && (filtre.AvantId is not { } a || e.Id < a)
            && (filtre.Communaute is not { } c || e.CommunauteId == c || e.AutreCommunauteId == c)
            && (!filtre.Significatifs || e.Significatif);
        var recents = service.Monde.JournalRecent().Where(Convient);
        var enregistres = service.Depot.LireJournal(types, filtre.Humain, filtre.AvantId, limite, filtre.Communaute, filtre.Significatifs);
        return recents.Concat(enregistres).DistinctBy(e => e.Id).OrderByDescending(e => e.Id).Take(limite).ToList();
    }
}

public sealed record MembreDto(int Id, string Nom, string Etape);

/// <summary>Une guerre en préparation : qui la veut, contre qui, pourquoi, depuis quand, et les alliés sollicités.</summary>
public sealed record ProjetDto(int Instigateur, string NomInstigateur, int Cible, string NomCible, string Raison, string Depuis,
    IReadOnlyList<string> Allies, long Evenement, string Debat);

/// <summary>Un membre chassé : qui, pourquoi, jusqu'à quand (null : pour toujours), et l'événement de l'exil.</summary>
public sealed record ExileDto(int Id, string Nom, string Raison, string? Jusqua, long Evenement);

/// <summary>Un nom qu'a porté une communauté, depuis quand, et l'événement qui le lui a donné.</summary>
public sealed record NomDto(string Nom, string? Sens, string Depuis, long Evenement);

/// <summary>Ce qu'une autre communauté est pour celle-ci ; Evenement : ce qui a fait naître la relation ou le pacte.</summary>
/// <param name="Code">État sans traduction (« alliee », « guerre », « rivale », « hostile », « indifferente ») : le navigateur colore avec lui.</param>
public sealed record RelationDto(int Id, string Nom, string Etat, string Code, long Evenement);

/// <summary>
/// Une guerre vue par une communauté : contre qui, quand, combien de raids, ses morts et ceux de l'ennemi, et comment elle a
/// fini (null : en cours). Evenement : le premier sang ; Fin : l'événement qui l'a close.
/// </summary>
public sealed record GuerreDto(int Contre, string NomContre, string Debut, string? FinLe, int Raids, int MortsSiens, int MortsEnnemis,
    string? Issue, long Evenement, long Fin);

/// <summary>Une communauté : qui en fait partie, depuis quand, où elle vit, autour de qui, et ce que ses terres lui donnent.</summary>
/// <param name="Abondance">Ressources autour d'elle, en % de leur niveau naturel.</param>
/// <param name="Destination">Direction vers laquelle elle part, ou null si elle reste.</param>
/// <param name="NomPropre">Nom que se donnent ses membres (null tant qu'ils ne se sont pas nommés), et son sens.</param>
/// <param name="Noms">Noms successifs, du premier au dernier.</param>
/// <param name="Mots">Premiers mots du groupe (« rivière : kalu »).</param>
/// <param name="Terres">Nombre de cases de ses terres parcourues.</param>
/// <param name="Histoire">Ce qu'elle a traversé, en phrases (origine, fusions, migrations, disettes).</param>
/// <param name="Temperament">Son goût pour la violence, en mots (null tant qu'il n'est pas établi).</param>
/// <param name="Guerre">Raid en cours, soumission, vassaux, en phrases.</param>
/// <param name="Guerres">Ses guerres, la plus récente d'abord.</param>
/// <param name="Projet">Guerre qu'elle prépare (null : aucune).</param>
/// <param name="Moeurs">Son regard sur les amours entre personnes de même sexe, en mots.</param>
/// <param name="Ideal">Ce qu'elle trouve beau.</param>
/// <param name="Exiles">Ceux qu'elle a chassés et dont l'exil dure encore.</param>
public sealed record CommunauteDto(int Id, string Nom, string FondeeLe, string? DisperseeLe, int Figure, string NomFigure,
    IReadOnlyList<MembreDto> Membres, int X, int Y, int Rayon, int Abondance, bool Disette, string? Destination,
    int Faim, int Reserves, IReadOnlyList<RelationDto> Relations, string Usage, string? NomPropre, string? Sens,
    IReadOnlyList<NomDto> Noms, IReadOnlyList<string> Mots, int Terres, IReadOnlyList<string> Histoire,
    string? Temperament, IReadOnlyList<string> Guerre, IReadOnlyList<GuerreDto> Guerres, ProjetDto? Projet,
    string? Moeurs, string? Ideal, IReadOnlyList<ExileDto> Exiles, string? Morale, IReadOnlyList<string> Savoirs,
    double? Karma = null, int? Croyance = null, IReadOnlyList<SoignantDto>? Soignants = null, string? Style = null,
    IReadOnlyList<OeuvreDto>? Oeuvres = null, LangueDto? Langue = null)
{
    /// <summary>
    /// Morale du groupe en mots : la moyenne des fondements de ses adultes, et ce qui y pèse le plus.
    /// Les écarts entre groupes sont faibles au début (tirés d'une même population) : ils grandissent avec les naissances et les départs.
    /// </summary>
    private static string? DireMorale(List<Humain> adultes)
    {
        if (adultes.Count == 0) return null;
        var f = VieSociale.MoraleDe(adultes);
        string N(double v) => v.ToString("0.00", L.Courante.Nombres);
        var (cle, valeur) = new[] { ("soin", f.Soin), ("equite", f.Equite), ("loyaute", f.Loyaute),
            ("autorite", f.Autorite), ("purete", f.Purete) }.MaxBy(x => x.Item2);
        return L.T("morale.phrase", ("soin", N(f.Soin)), ("equite", N(f.Equite)), ("loyaute", N(f.Loyaute)), ("autorite", N(f.Autorite)),
            ("purete", N(f.Purete)), ("tient", L.T($"morale.tient.{cle}")));
    }

    private static List<string> Raconter(Communaute c, List<Communaute> toutes)
    {
        var histoire = new List<string>();
        string Ref(int id) => toutes[id - 1].Nom;
        string Phrase(string cle, string nom) => L.T($"histoireCommunaute.{cle}", ("nom", L.CommunautePhrase(nom)), ("nomMin", L.CommunauteMin(nom)));
        if (c.OrigineId > 0) histoire.Add(Phrase("scission", Ref(c.OrigineId)));
        foreach (var f in toutes.Where(x => x.FusionneeDansId == c.Id)) histoire.Add(Phrase("accueilli", f.Nom));
        if (c.FusionneeDansId > 0) histoire.Add(Phrase("fondue", Ref(c.FusionneeDansId)));
        if (c.Active && c.FusionVersId > 0) histoire.Add(Phrase("enRoute", Ref(c.FusionVersId)));
        if (c.Migrations > 0) histoire.Add(L.T("histoireCommunaute.migrations", ("n", c.Migrations)));
        if (c.DisettesSurmontees > 0) histoire.Add(L.T("histoireCommunaute.disettes", ("n", c.DisettesSurmontees)));
        foreach (var p in toutes.Where(x => x.HeritiereId == c.Id)) histoire.Add(Phrase("continue", p.Nom));
        if (!c.Active && c.HeritiereId > 0) histoire.Add(Phrase("formee", Ref(c.HeritiereId)));
        return histoire;
    }

    private static string? DireTemperament(double bellicisme) => bellicisme switch
    {
        < 0 => null,
        < 0.15 => L.T("temperament.pacifique"),
        < 0.35 => L.T("temperament.peuViolente"),
        < 0.6 => L.T("temperament.prompte"),
        _ => L.T("temperament.guerriere"),
    };

    private static List<string> SituationDeGuerre(Communaute c, List<Communaute> toutes, List<Humain> humains)
    {
        var lignes = new List<string>();
        if (!c.Active) return lignes;
        string Ref(int id) => toutes[id - 1].Nom;
        if (c.Raid is { } raid)
        {
            var n = raid.Guerriers.Count;
            lignes.Add(L.T(raid.Retour ? "guerreCommunaute.rentrent" : "guerreCommunaute.partis", ("n", n), ("nom", L.CommunautePhrase(Ref(raid.CibleId))),
                ("nomMin", L.CommunauteMin(Ref(raid.CibleId)))));
        }
        if (c.SoumiseAId > 0)
        {
            lignes.Add(L.T("guerreCommunaute.soumise", ("nom", L.CommunautePhrase(Ref(c.SoumiseAId))), ("nomMin", L.CommunauteMin(Ref(c.SoumiseAId))), ("date", L.Date(c.SoumiseLe))));
        }
        var vassaux = toutes.Where(x => x.Active && x.SoumiseAId == c.Id)
            .Select(x => L.T("guerreCommunaute.vassal", ("nom", L.CommunautePhrase(x.Nom)), ("nomMin", L.CommunauteMin(x.Nom)))).ToList();
        if (vassaux.Count > 0) lignes.Add(L.T("guerreCommunaute.tribut", ("vassaux", string.Join(L.T("commun.virgule"), vassaux))));
        return lignes;
    }

    private static string IssueVue(Guerre g, int id) => g.Issue switch
    {
        IssueGuerre.EnCours => L.T("issueGuerre.enCours"),
        IssueGuerre.Paix => L.T("issueGuerre.paix"),
        IssueGuerre.Soumission or IssueGuerre.Fuite or IssueGuerre.Absorption => L.T($"issueGuerre.{g.Issue}.{(g.VainqueurId == id ? "victoire" : "defaite")}"),
        _ => L.T($"issueGuerre.disperse.{(g.VainqueurId == id ? "victoire" : "defaite")}"),
    };


    private static string Etat(EtatRelation e, bool pacte) => e switch
    {
        EtatRelation.Alliees => L.T(pacte ? "relation.pacte" : "relation.alliee"),
        EtatRelation.EnGuerre => L.T("relation.guerre"),
        EtatRelation.Rivales => L.T("relation.rivale"),
        EtatRelation.Hostiles => L.T("relation.hostile"),
        _ => L.T("relation.indifferente"),
    };

    private static string Code(EtatRelation e) => e switch
    {
        EtatRelation.Alliees => "alliee",
        EtatRelation.EnGuerre => "guerre",
        EtatRelation.Rivales => "rivale",
        EtatRelation.Hostiles => "hostile",
        _ => "indifferente",
    };

    public static List<CommunauteDto> Toutes(ServiceMonde service) => Lister(service, null);

    /// <summary>Une seule communauté (null si elle n'existe pas).</summary>
    public static CommunauteDto? Une(ServiceMonde service, int id, (long, List<Humain>)? instantane = null) =>
        Lister(service, id, instantane).FirstOrDefault();

    private static List<CommunauteDto> Lister(ServiceMonde service, int? seule, (long, List<Humain>)? instantane = null)
    {
        var monde = service.Monde;
        var (maintenant, humains) = instantane ?? monde.InstantaneHumains();
        var parCommunaute = humains.Where(h => h.EstVivant && h.CommunauteId != 0).ToLookup(h => h.CommunauteId);
        var toutes = monde.Communautes();
        var relations = monde.Relations();
        var guerres = monde.Guerres();
        var oeuvres = monde.Oeuvres().ToLookup(o => o.CommunauteId);
        return toutes.Where(c => seule is null || c.Id == seule)
            .OrderBy(c => c.DisperseeLe is null ? 0 : 1).ThenByDescending(c => parCommunaute[c.Id].Count()).ThenBy(c => c.Id)
            .Select(c =>
            {
                var membres = parCommunaute[c.Id].ToList();
                var x = membres.Count > 0 ? (int)Math.Round(membres.Average(m => m.X)) : 0;
                var y = membres.Count > 0 ? (int)Math.Round(membres.Average(m => m.Y)) : 0;
                var adultes = membres.Where(m => !m.EstEnfant(maintenant)).ToList();
                return new CommunauteDto(c.Id, L.Communaute(c.Nom), L.Date(c.FondeeLe),
                    c.DisperseeLe is { } d ? L.Date(d) : null,
                    c.FigureId, humains[c.FigureId - 1].Nom,
                    membres.Select(m => new MembreDto(m.Id, m.Nom, m.EstBebe(maintenant) ? "bébé" : m.EstEnfant(maintenant) ? "enfant" : "adulte")).ToList(),
                    x, y, c.Rayon, (int)Math.Round(c.Abondance * 100), c.Active && c.Disette,
                    c.Active && c.DestinationX >= 0 ? Lib.Vers(c.CentreX, c.CentreY, c.DestinationX, c.DestinationY) : null,
                    adultes.Count == 0 ? 0 : (int)Math.Round(adultes.Average(m => m.Faim)),
                    adultes.Count == 0 ? 0 : (int)Math.Round(adultes.Average(m => m.Reserves)),
                    relations.Where(r => r.A == c.Id || r.B == c.Id)
                        .Select(r => (Autre: r.A == c.Id ? r.B : r.A, r.Etat, r.EvenementId, r.PacteEvenementId))
                        .Select(r => new RelationDto(r.Autre, toutes[r.Autre - 1].Nom, Etat(r.Etat, r.PacteEvenementId > 0), Code(r.Etat),
                            r.PacteEvenementId > 0 ? r.PacteEvenementId : r.EvenementId))
                        .ToList(),
                    c.Usage switch
                    {
                        UsageFidelite.MalVu => L.T("usage.fidelite.malVu"),
                        UsageFidelite.Indifferent => L.T("usage.fidelite.indifferent"),
                        _ => L.T("usage.fidelite.aucun"),
                    },
                    c.NomPropre, L.Garde(c.Sens),
                    c.Noms.Select(n => new NomDto(L.Communaute(n.Nom), L.Garde(n.Sens), L.Date(n.Depuis), n.EvenementId)).ToList(),
                    c.Lexique.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => L.T("mots.ligne", ("sens", Lib.Idee(m.Key, monde.Flore)), ("mot", m.Value))).ToList(),
                    c.Active ? VieSociale.Etendue(VieSociale.Frequentees(c.Territoire), monde.Carte).Length : 0,
                    Raconter(c, toutes),
                    DireTemperament(c.Bellicisme),
                    SituationDeGuerre(c, toutes, humains),
                    guerres.Where(g => g.A == c.Id || g.B == c.Id).Reverse()
                        .Select(g =>
                        {
                            var (contre, siens, ennemis) = g.A == c.Id ? (g.B, g.MortsA, g.MortsB) : (g.A, g.MortsB, g.MortsA);
                            return new GuerreDto(contre, toutes[contre - 1].Nom, L.Date(g.Debut),
                                g.Fin is { } f ? L.Date(f) : null, g.Raids, siens, ennemis,
                                g.Issue == IssueGuerre.EnCours ? null : IssueVue(g, c.Id), g.EvenementId, g.FinEvenementId);
                        }).ToList(),
                    c.Active && c.Projet is { } projet
                        ? new ProjetDto(projet.InstigateurId, humains[projet.InstigateurId - 1].Nom, projet.CibleId, toutes[projet.CibleId - 1].Nom,
                            L.Garde(projet.Raison), L.Date(projet.Depuis),
                            projet.AlliesSollicites.Select(a => toutes[a - 1].Nom).ToList(), projet.EvenementId,
                            L.Date(projet.Debat))
                        : null,
                    c.ToleranceMoeurs < 0 ? null : c.UsageMoeurs switch
                    {
                        UsageMoeurs.Reprouvees => L.T("usage.moeurs.reprouvees"),
                        UsageMoeurs.Acceptees => L.T("usage.moeurs.acceptees"),
                        _ => L.T("usage.moeurs.aucun"),
                    },
                    c.ToleranceMoeurs < 0 ? null : Lib.Ideal(c.Ideal),
                    humains.Where(m => m.EstVivant)
                        .SelectMany(m => m.Exclusions.Where(e => e.Type == TypeExclusion.Communaute && e.De == c.Id && e.Active(maintenant))
                            .Select(e => new ExileDto(m.Id, m.Nom, L.Garde(e.Raison), e.Jusqua == 0 ? null : L.Date(e.Jusqua), e.EvenementId)))
                        .ToList(),
                    DireMorale(adultes),
                    Decouvertes.DeCommunaute(membres),
                    // Moyennes des adultes : le karma (regard de l'Entité) et la croyance en l'invisible.
                    adultes.Count == 0 ? null : Math.Round(adultes.Average(m => m.Karma), 1),
                    adultes.Count == 0 ? null : (int)Math.Round(adultes.Average(m => m.Croyance)),
                    adultes.Where(m => Lib.Soignant(m) is not null)
                        .Select(m => new SoignantDto(m.Id, m.Nom, Lib.Soignant(m)!, m.Gueris, m.Perdus)).ToList(),
                    Lib.Style(membres.Where(m => !m.EstBebe(maintenant)).ToList(), oeuvres[c.Id]),
                    oeuvres[c.Id].OrderByDescending(o => o.Le).Take(12)
                        .Select(o => new OeuvreDto(o.Id, o.AuteurId, humains[o.AuteurId - 1].Nom, L.Date(o.Le), L.Garde(o.Description), o.EvenementId))
                        .ToList(),
                    LangueDto.Depuis(c, toutes, humains, membres, monde.Flore));
            }).ToList();
    }
}
