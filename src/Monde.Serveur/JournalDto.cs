using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un événement du journal tel qu'envoyé au navigateur.</summary>
/// <param name="Cause">Numéro de l'événement qui l'a provoqué (0 : aucune cause notée).</param>
/// <param name="Significatif">Un tournant (projet de guerre, renoncement, raid, paix, catastrophe…).</param>
public sealed record EvenementDto(long Id, long Secondes, string Date, string Type, int Humain, int Autre, string Texte, string Origine,
    long Cause, int Communaute, int AutreCommunaute, bool Significatif)
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
    };


    public static EvenementDto Depuis(EvenementJournal e) => new(e.Id, e.Secondes, Calendrier.Decomposer(e.Secondes).ToString(),
        Cles.GetValueOrDefault(e.Type, "autre"), e.HumainId, e.AutreId, e.Texte,
        e.Origine == Simulation.Origine.Entite ? "entite" : "naturelle", e.CauseId, e.CommunauteId, e.AutreCommunauteId, e.Significatif);

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
    bool Significatifs = false);

public sealed record LieuDto(string Type, int X, int Y, string VuLe, int Visites);

public sealed record PenseeDto(string Date, string Texte);

/// <summary>
/// Une chose du jardin secret : sa nature (intention, peur, espoir…), qui elle concerne (0 : personne), ce qu'il se dit,
/// sa force (0 à 100) et l'événement qui l'a fait naître.
/// </summary>
public sealed record IntimeDto(string Date, string Type, int Sujet, string? NomSujet, string Texte, int Intensite, long Cause);

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
    EpreuvesDto Epreuves)
{
    private static string Date(long s) => Calendrier.Decomposer(s).ToString();

    private static TraitDto Trait(string nom, double v, string aide) => new(nom, Math.Round(v, 2), Psyche.Niveau(v), aide);
    private static TraitDto Qi(string nom, double v, string aide) => new(nom, Math.Round(v), Monde.Simulation.Humains.Intelligence.Qualifier(v), aide);

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
                Trait("ouverture", p.Ouverture, "curiosité, imagination, goût du nouveau"),
                Trait("conscience", p.Conscience, "application, prévoyance"),
                Trait("extraversion", p.Extraversion, "besoin des autres ; bas : replié sur soi"),
                Trait("agréabilité", p.Agreabilite, "bienveillance, partage ; bas : méfiant, querelleur"),
                Trait("névrosisme", p.Nevrosisme, "sensibilité à l'anxiété et à la tristesse"),
                Trait("transcendance", p.Transcendance, "penchant spirituel et contemplatif ; bas : rationnel, terre à terre"),
                Trait("charisme", p.Charisme, "déduit : extraversion, calme, chaleur, aisance à communiquer"),
                Trait("attachement", p.Attachement, "lien avec ses enfants"),
                Trait("endurance", p.Endurance, "résistance à la fatigue et aux maladies"),
            ],
            [
                Qi("générale", i.Generale, "comprendre, raisonner, apprendre"),
                Qi("verbale", i.Verbale, "communiquer, imiter sons et gestes"),
                Qi("spatiale", i.Spatiale, "s'orienter, se représenter les lieux"),
                Qi("manuelle", i.Manuelle, "adresse des mains et du corps"),
                Qi("mémoire", i.Memoire, "retenir les lieux, oublier moins vite"),
                Qi("artistique", i.Artistique, "sens des formes, des rythmes, des couleurs"),
            ],
            new Dictionary<string, double>
            {
                ["puberté (ans)"] = Math.Round(b.AgePuberte, 1),
                ["fin de fertilité (ans)"] = Math.Round(b.AgeFinFertilite, 1),
                ["fertilité"] = Math.Round(b.Fertilite, 2),
                ["métabolisme"] = Math.Round(b.Metabolisme, 2),
                ["allure"] = Math.Round(b.Marche, 2),
                ["marche (km/h)"] = h.EstVivant && !h.EstBebe(maintenant) ? Math.Round(Marche.Vitesse(h, maintenant) * 3.6, 1) : 0,
            },
            h.EstVivant && !h.EstBebe(maintenant)
                ? new AmeDto(Libelles.Humeur(h), (int)Math.Round(h.Humeur), (int)Math.Round(h.Chagrin), h.DeuilDeId,
                    h.DeprimeDepuis is { } d ? Date(d) : null, VieSociale.Phrases(h, humains, maintenant),
                    (int)Math.Round(h.Colere),
                    h.Griefs.OrderByDescending(g => g.Intensite)
                        .Select(g => new RancuneDto(g.ContreId, Nom(g.ContreId), Motif(g, Nom), (int)Math.Round(g.Intensite), g.EvenementId))
                        .ToList(),
                    h.DepressionEvenementId)
                : null,
            communaute is null ? null : new CommunauteRefDto(communaute.Id, communaute.Nom),
            humains.Where(e => e.MereId == id || e.PereId == id).Select(e => e.Id).ToList(),
            h.Memoire.Pensees.AsEnumerable().Reverse().Select(x => new PenseeDto(Date(x.Secondes), x.Texte)).ToList(),
            h.Memoire.Jardin.OrderBy(x => x.Type).ThenByDescending(x => x.Intensite)
                .Select(x => new IntimeDto(Date(x.Secondes), TypeIntime(x.Type), x.SujetId, x.SujetId > 0 && x.SujetId != id ? Nom(x.SujetId) : null,
                    x.Texte, (int)Math.Round(x.Intensite), x.CauseId)).ToList(),
            h.Memoire.Lieux.OrderBy(l => l.Type).ThenByDescending(l => l.Rappels).ThenByDescending(l => l.VuLe)
                .Select(l => new LieuDto(l.Type switch { TypeLieu.Eau => "eau", TypeLieu.Nourriture => "nourriture", _ => "abri" },
                    l.X, l.Y, Date(l.VuLe), l.Rappels + 1)).ToList(),
            h.Liens.OrderByDescending(l => Math.Abs(l.Affection)).ThenByDescending(l => l.Familiarite)
                .Select(l => new LienDto(l.Id, Nom(l.Id), (int)Math.Round(l.Familiarite), (int)Math.Round(l.Affection),
                    Statut(l), humains[l.Id - 1].EstVivant)).ToList(),
            Journal(service, new FiltreJournal(Humain: id, Limite: 200), types).Select(EvenementDto.Depuis).ToList(),
            LireEpreuves(h, monde.Pathogenes(), maintenant));
    }

    private static EpreuvesDto LireEpreuves(Humain h, List<Simulation.Nature.Pathogene> maladies, long maintenant)
    {
        string NomMaladie(int id) => id >= 1 && id <= maladies.Count ? maladies[id - 1].Nom : "une maladie";
        var porteur = h.EstVivant && h.InfectionId != 0;
        return new EpreuvesDto(
            porteur ? NomMaladie(h.InfectionId) + (maintenant < h.SymptomesLe ? " (pas encore de signes)" : "") : null,
            porteur ? Date(h.InfecteLe) : null, porteur ? h.InfectionEvenementId : 0,
            h.Immunites.Where(i => i.Jusqua is null || i.Jusqua > maintenant)
                .Select(i => $"{NomMaladie(i.PathogeneId)} ({(i.Jusqua is { } j ? $"jusqu'à l'an {Calendrier.Decomposer(j).Annee}" : "à vie")})")
                .ToList(),
            h.EstVivant ? (int)Math.Round(h.Peur) : 0,
            h.EstVivant && h.Peur >= 1 ? Simulation.Nature.TypesCatastrophe.Nom(h.PeurType) : null,
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
        _ => "question",
    };

    private static string Motif(Grief g, Func<int, string> nom) => g.Motif switch
    {
        MotifGrief.Infidelite => "trahison",
        MotifGrief.Rival => "rivalité amoureuse",
        MotifGrief.Coups => "coups",
        _ => g.VictimeId > 0 ? $"a tué {nom(g.VictimeId)}" : "meurtre d'un proche",
    };

    private static string Statut(Lien l) => l switch
    {
        { Rivaux: true } => "hostile",
        { Affection: >= 70 } => "très proche",
        { Amis: true } => "ami",
        { Affection: <= -10 } => "méfiance",
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
    IReadOnlyList<string> Allies, long Evenement);

/// <summary>Un nom qu'a porté une communauté, depuis quand, et l'événement qui le lui a donné.</summary>
public sealed record NomDto(string Nom, string? Sens, string Depuis, long Evenement);

/// <summary>Ce qu'une autre communauté est pour celle-ci ; Evenement : ce qui a fait naître la relation ou le pacte.</summary>
public sealed record RelationDto(int Id, string Nom, string Etat, long Evenement);

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
public sealed record CommunauteDto(int Id, string Nom, string FondeeLe, string? DisperseeLe, int Figure, string NomFigure,
    IReadOnlyList<MembreDto> Membres, int X, int Y, int Rayon, int Abondance, bool Disette, string? Destination,
    int Faim, int Reserves, IReadOnlyList<RelationDto> Relations, string Usage, string? NomPropre, string? Sens,
    IReadOnlyList<NomDto> Noms, IReadOnlyList<string> Mots, int Terres, IReadOnlyList<string> Histoire,
    string? Temperament, IReadOnlyList<string> Guerre, IReadOnlyList<GuerreDto> Guerres, ProjetDto? Projet)
{
    /// <summary>Les idées des mots, en français, pour l'observateur.</summary>
    private static readonly Dictionary<string, string> Idees = new()
    {
        ["ceux"] = "ceux (qui sont…)", ["gens"] = "les humains", ["enfants"] = "enfants de", ["riviere"] = "rivière", ["lac"] = "lac",
        ["rivage"] = "rivage", ["montagnes"] = "montagnes", ["marais"] = "marais", ["sables"] = "sables", ["foret"] = "forêt",
        ["fougeres"] = "fougères", ["soleil"] = "soleil", ["etoiles"] = "étoiles", ["faim"] = "faim vaincue", ["marche"] = "marcher",
        ["partis"] = "partir",
    };

    private static List<string> Raconter(Communaute c, List<Communaute> toutes)
    {
        var histoire = new List<string>();
        string Ref(int id) => toutes[id - 1].Nom;
        if (c.OrigineId > 0) histoire.Add($"Née d'une scission de la {Minuscule(Ref(c.OrigineId))}.");
        foreach (var f in toutes.Where(x => x.FusionneeDansId == c.Id)) histoire.Add($"A accueilli la {Minuscule(f.Nom)}, qui s'est fondue en elle.");
        if (c.FusionneeDansId > 0) histoire.Add($"S'est fondue dans la {Minuscule(Ref(c.FusionneeDansId))}.");
        if (c.Active && c.FusionVersId > 0) histoire.Add($"En route pour rejoindre la {Minuscule(Ref(c.FusionVersId))}.");
        if (c.Migrations > 0) histoire.Add(c.Migrations == 1 ? "A migré une fois vers de nouvelles terres." : $"A migré {c.Migrations} fois vers de nouvelles terres.");
        if (c.DisettesSurmontees > 0) histoire.Add(c.DisettesSurmontees == 1 ? "A surmonté une disette." : $"A surmonté {c.DisettesSurmontees} disettes.");
        foreach (var p in toutes.Where(x => x.HeritiereId == c.Id)) histoire.Add($"Continue la {Minuscule(p.Nom)}, dont elle a gardé les querelles.");
        if (!c.Active && c.HeritiereId > 0) histoire.Add($"Ses membres ont formé la {Minuscule(Ref(c.HeritiereId))}.");
        return histoire;
    }

    private static string? DireTemperament(double bellicisme) => bellicisme switch
    {
        < 0 => null,
        < 0.15 => "pacifique",
        < 0.35 => "peu portée à la violence",
        < 0.6 => "prompte à se battre",
        _ => "guerrière",
    };

    private static List<string> SituationDeGuerre(Communaute c, List<Communaute> toutes, List<Humain> humains)
    {
        var lignes = new List<string>();
        if (!c.Active) return lignes;
        string Ref(int id) => Minuscule(toutes[id - 1].Nom);
        if (c.Raid is { } raid)
        {
            var n = raid.Guerriers.Count;
            lignes.Add(raid.Retour
                ? $"{(n == 1 ? "Un guerrier rentre" : $"{n} guerriers rentrent")} d'un raid contre la {Ref(raid.CibleId)}."
                : $"{(n == 1 ? "Un guerrier est parti" : $"{n} guerriers sont partis")} en raid contre la {Ref(raid.CibleId)}.");
        }
        if (c.SoumiseAId > 0)
        {
            lignes.Add($"Soumise à la {Ref(c.SoumiseAId)} depuis {Calendrier.Decomposer(c.SoumiseLe)} : elle lui livre une part de sa nourriture.");
        }
        var vassaux = toutes.Where(x => x.Active && x.SoumiseAId == c.Id).Select(x => "la " + Minuscule(x.Nom)).ToList();
        if (vassaux.Count > 0) lignes.Add($"Reçoit le tribut de {string.Join(", ", vassaux)}.");
        return lignes;
    }

    private static string IssueVue(Guerre g, int id) => g.Issue switch
    {
        IssueGuerre.EnCours => "en cours",
        IssueGuerre.Paix => "paix conclue",
        IssueGuerre.Soumission => g.VainqueurId == id ? "victoire : l'ennemi s'est soumis" : "défaite : soumission",
        IssueGuerre.Fuite => g.VainqueurId == id ? "victoire : l'ennemi a fui" : "défaite : fuite au loin",
        IssueGuerre.Absorption => g.VainqueurId == id ? "victoire : les derniers ennemis ont rejoint le groupe" : "défaite : les derniers sont allés vivre chez le vainqueur",
        _ => g.VainqueurId == id ? "l'ennemi s'est dispersé" : "le groupe s'est dispersé",
    };

    private static string Minuscule(string nom) => char.ToLowerInvariant(nom[0]) + nom[1..];

    private static string Etat(EtatRelation e, bool pacte) => e switch
    {
        EtatRelation.Alliees => pacte ? "alliée par un pacte" : "alliée",
        EtatRelation.EnGuerre => "en guerre",
        EtatRelation.Rivales => "rivale pour les terres",
        EtatRelation.Hostiles => "hostile",
        _ => "indifférente",
    };

    public static List<CommunauteDto> Toutes(ServiceMonde service)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var parCommunaute = humains.Where(h => h.EstVivant && h.CommunauteId != 0).ToLookup(h => h.CommunauteId);
        var toutes = monde.Communautes();
        var relations = monde.Relations();
        var guerres = monde.Guerres();
        return toutes
            .OrderBy(c => c.DisperseeLe is null ? 0 : 1).ThenByDescending(c => parCommunaute[c.Id].Count()).ThenBy(c => c.Id)
            .Select(c =>
            {
                var membres = parCommunaute[c.Id].ToList();
                var x = membres.Count > 0 ? (int)Math.Round(membres.Average(m => m.X)) : 0;
                var y = membres.Count > 0 ? (int)Math.Round(membres.Average(m => m.Y)) : 0;
                var adultes = membres.Where(m => !m.EstEnfant(maintenant)).ToList();
                return new CommunauteDto(c.Id, c.Nom, Calendrier.Decomposer(c.FondeeLe).ToString(),
                    c.DisperseeLe is { } d ? Calendrier.Decomposer(d).ToString() : null,
                    c.FigureId, humains[c.FigureId - 1].Nom,
                    membres.Select(m => new MembreDto(m.Id, m.Nom, m.EstBebe(maintenant) ? "bébé" : m.EstEnfant(maintenant) ? "enfant" : "adulte")).ToList(),
                    x, y, c.Rayon, (int)Math.Round(c.Abondance * 100), c.Active && c.Disette,
                    c.Active && c.DestinationX >= 0 ? Lieux.Vers(c.CentreX, c.CentreY, c.DestinationX, c.DestinationY) : null,
                    adultes.Count == 0 ? 0 : (int)Math.Round(adultes.Average(m => m.Faim)),
                    adultes.Count == 0 ? 0 : (int)Math.Round(adultes.Average(m => m.Reserves)),
                    relations.Where(r => r.A == c.Id || r.B == c.Id)
                        .Select(r => (Autre: r.A == c.Id ? r.B : r.A, r.Etat, r.EvenementId, r.PacteEvenementId))
                        .Select(r => new RelationDto(r.Autre, toutes[r.Autre - 1].Nom, Etat(r.Etat, r.PacteEvenementId > 0),
                            r.PacteEvenementId > 0 ? r.PacteEvenementId : r.EvenementId))
                        .ToList(),
                    c.Usage switch
                    {
                        UsageFidelite.MalVu => "s'unir hors de son couple y est mal vu",
                        UsageFidelite.Indifferent => "nul ne s'y offusque qu'on s'unisse hors de son couple",
                        _ => "pas d'usage établi sur la fidélité",
                    },
                    c.NomPropre, c.Sens,
                    c.Noms.Select(n => new NomDto(n.Nom, n.Sens, Calendrier.Decomposer(n.Depuis).ToString(), n.EvenementId)).ToList(),
                    c.Lexique.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => $"{Idees.GetValueOrDefault(m.Key, m.Key)} : {m.Value}").ToList(),
                    c.Active ? VieSociale.Etendue(VieSociale.Frequentees(c.Territoire), monde.Carte).Length : 0,
                    Raconter(c, toutes),
                    DireTemperament(c.Bellicisme),
                    SituationDeGuerre(c, toutes, humains),
                    guerres.Where(g => g.A == c.Id || g.B == c.Id).Reverse()
                        .Select(g =>
                        {
                            var (contre, siens, ennemis) = g.A == c.Id ? (g.B, g.MortsA, g.MortsB) : (g.A, g.MortsB, g.MortsA);
                            return new GuerreDto(contre, toutes[contre - 1].Nom, Calendrier.Decomposer(g.Debut).ToString(),
                                g.Fin is { } f ? Calendrier.Decomposer(f).ToString() : null, g.Raids, siens, ennemis,
                                g.Issue == IssueGuerre.EnCours ? null : IssueVue(g, c.Id), g.EvenementId, g.FinEvenementId);
                        }).ToList(),
                    c.Active && c.Projet is { } projet
                        ? new ProjetDto(projet.InstigateurId, humains[projet.InstigateurId - 1].Nom, projet.CibleId, toutes[projet.CibleId - 1].Nom,
                            projet.Raison, Calendrier.Decomposer(projet.Depuis).ToString(),
                            projet.AlliesSollicites.Select(a => toutes[a - 1].Nom).ToList(), projet.EvenementId)
                        : null);
            }).ToList();
    }
}
