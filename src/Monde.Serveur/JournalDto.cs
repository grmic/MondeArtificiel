using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un événement du journal tel qu'envoyé au navigateur.</summary>
/// <param name="Cause">Numéro de l'événement qui l'a provoqué (0 : aucune cause notée).</param>
public sealed record EvenementDto(long Id, long Secondes, string Date, string Type, int Humain, int Autre, string Texte, string Origine,
    long Cause)
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
    };


    public static EvenementDto Depuis(EvenementJournal e) => new(e.Id, e.Secondes, Calendrier.Decomposer(e.Secondes).ToString(),
        Cles.GetValueOrDefault(e.Type, "autre"), e.HumainId, e.AutreId, e.Texte,
        e.Origine == Simulation.Origine.Entite ? "entite" : "naturelle", e.CauseId);

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
public sealed record FiltreJournal(string[]? Types = null, int? Humain = null, long? AvantId = null, int Limite = 100);

public sealed record LieuDto(string Type, int X, int Y, string VuLe, int Visites);

public sealed record PenseeDto(string Date, string Texte);

/// <summary>Un trait affiché : nom, valeur (0 à 1, ou QI) et qualification en mots.</summary>
public sealed record TraitDto(string Nom, double Valeur, string Mots, string Aide);

public sealed record LienDto(int Id, string Nom, int Familiarite, int Affection, string Statut, bool Vivant);

/// <summary>Une rancune : contre qui, pourquoi, avec quelle force (0 à 100), et l'événement qui l'a fait naître.</summary>
public sealed record RancuneDto(int Id, string Nom, string Motif, int Intensite, long Evenement);

/// <param name="Raisons">Ce qui pèse sur son humeur, en mots, du plus lourd au plus léger.</param>
public sealed record AmeDto(string Etat, int Humeur, int Chagrin, int DeuilDe, string? DeprimeDepuis, IReadOnlyList<string> Raisons,
    int Colere, IReadOnlyList<RancuneDto> Rancunes, long DepressionEvenement);

public sealed record CommunauteRefDto(int Id, string Nom);

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
    IReadOnlyList<LieuDto> Lieux,
    IReadOnlyList<LienDto> Liens,
    IReadOnlyList<EvenementDto> Histoire)
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
            h.Memoire.Lieux.OrderBy(l => l.Type).ThenByDescending(l => l.Rappels).ThenByDescending(l => l.VuLe)
                .Select(l => new LieuDto(l.Type switch { TypeLieu.Eau => "eau", TypeLieu.Nourriture => "nourriture", _ => "abri" },
                    l.X, l.Y, Date(l.VuLe), l.Rappels + 1)).ToList(),
            h.Liens.OrderByDescending(l => Math.Abs(l.Affection)).ThenByDescending(l => l.Familiarite)
                .Select(l => new LienDto(l.Id, Nom(l.Id), (int)Math.Round(l.Familiarite), (int)Math.Round(l.Affection),
                    Statut(l), humains[l.Id - 1].EstVivant)).ToList(),
            Journal(service, new FiltreJournal(Humain: id, Limite: 200), types).Select(EvenementDto.Depuis).ToList());
    }

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
            && (filtre.AvantId is not { } a || e.Id < a);
        var recents = service.Monde.JournalRecent().Where(Convient);
        var enregistres = service.Depot.LireJournal(types, filtre.Humain, filtre.AvantId, limite);
        return recents.Concat(enregistres).DistinctBy(e => e.Id).OrderByDescending(e => e.Id).Take(limite).ToList();
    }
}

public sealed record MembreDto(int Id, string Nom, string Etape);

/// <summary>Ce qu'une autre communauté est pour celle-ci.</summary>
public sealed record RelationDto(int Id, string Nom, string Etat);

/// <summary>Une communauté : qui en fait partie, depuis quand, où elle vit, autour de qui, et ce que ses terres lui donnent.</summary>
/// <param name="Abondance">Ressources autour d'elle, en % de leur niveau naturel.</param>
/// <param name="Destination">Direction vers laquelle elle part, ou null si elle reste.</param>
public sealed record CommunauteDto(int Id, string Nom, string FondeeLe, string? DisperseeLe, int Figure, string NomFigure,
    IReadOnlyList<MembreDto> Membres, int X, int Y, int Rayon, int Abondance, bool Disette, string? Destination,
    int Faim, int Reserves, IReadOnlyList<RelationDto> Relations, string Usage)
{
    private static string Etat(EtatRelation e) => e switch
    {
        EtatRelation.Alliees => "alliée",
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
                        .Select(r => (Autre: r.A == c.Id ? r.B : r.A, r.Etat))
                        .Select(r => new RelationDto(r.Autre, toutes[r.Autre - 1].Nom, Etat(r.Etat)))
                        .ToList(),
                    c.Usage switch
                    {
                        UsageFidelite.MalVu => "s'unir hors de son couple y est mal vu",
                        UsageFidelite.Indifferent => "nul ne s'y offusque qu'on s'unisse hors de son couple",
                        _ => "pas d'usage établi sur la fidélité",
                    });
            }).ToList();
    }
}
