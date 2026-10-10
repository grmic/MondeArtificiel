using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un événement du journal tel qu'envoyé au navigateur.</summary>
public sealed record EvenementDto(long Id, long Secondes, string Date, string Type, int Humain, int Autre, string Texte, string Origine)
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
    };

    public static EvenementDto Depuis(EvenementJournal e) => new(e.Id, e.Secondes, Calendrier.Decomposer(e.Secondes).ToString(),
        Cles.GetValueOrDefault(e.Type, "autre"), e.HumainId, e.AutreId, e.Texte,
        e.Origine == Simulation.Origine.Entite ? "entite" : "naturelle");

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

public sealed record LieuDto(string Type, int X, int Y, string VuLe);

public sealed record ProcheDto(int Id, string Nom, int Jours, string Derniere);

public sealed record PenseeDto(string Date, string Texte);

/// <summary>Fiche complète d'un humain : état, caractère, biologie, famille, mémoire, pensées et histoire.</summary>
public sealed record FicheDto(
    HumainDto Humain,
    IReadOnlyDictionary<string, double> Caractere,
    IReadOnlyDictionary<string, double> Biologie,
    IReadOnlyList<int> Enfants,
    IReadOnlyList<PenseeDto> Pensees,
    IReadOnlyList<LieuDto> Lieux,
    IReadOnlyList<ProcheDto> Proches,
    IReadOnlyList<EvenementDto> Histoire)
{
    private static string Date(long s) => Calendrier.Decomposer(s).ToString();

    public static FicheDto? Depuis(ServiceMonde service, int id)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        if (id < 1 || id > humains.Count) return null;
        var h = humains[id - 1];
        string Nom(int i) => i >= 1 && i <= humains.Count ? humains[i - 1].Nom : "?";
        var p = h.Personnalite;
        var b = h.Biologie;
        return new FicheDto(
            HumainDto.Depuis(h, maintenant),
            new Dictionary<string, double>
            {
                ["curiosité"] = Math.Round(p.Curiosite, 2),
                ["prudence"] = Math.Round(p.Prudence, 2),
                ["sociabilité"] = Math.Round(p.Sociabilite, 2),
                ["endurance"] = Math.Round(p.Endurance, 2),
                ["adresse"] = Math.Round(p.Adresse, 2),
                ["attachement"] = Math.Round(p.Attachement, 2),
            },
            new Dictionary<string, double>
            {
                ["puberté (ans)"] = Math.Round(b.AgePuberte, 1),
                ["fin de fertilité (ans)"] = Math.Round(b.AgeFinFertilite, 1),
                ["fertilité"] = Math.Round(b.Fertilite, 2),
            },
            humains.Where(e => e.MereId == id || e.PereId == id).Select(e => e.Id).ToList(),
            h.Memoire.Pensees.AsEnumerable().Reverse().Select(x => new PenseeDto(Date(x.Secondes), x.Texte)).ToList(),
            h.Memoire.Lieux.OrderBy(l => l.Type).ThenByDescending(l => l.VuLe)
                .Select(l => new LieuDto(l.Type switch { TypeLieu.Eau => "eau", TypeLieu.Nourriture => "nourriture", _ => "abri" },
                    l.X, l.Y, Date(l.VuLe))).ToList(),
            h.Memoire.Proches.OrderByDescending(x => x.Rencontres)
                .Select(x => new ProcheDto(x.Id, Nom(x.Id), x.Rencontres, Date(x.Derniere))).ToList(),
            Journal(service, new FiltreJournal(Humain: id, Limite: 200), []).Select(EvenementDto.Depuis).ToList());
    }

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
