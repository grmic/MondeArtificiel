using System.Globalization;
using System.Text.Json;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Serveur.Entite;

/// <summary>Filtre de la fenêtre « Actes de l'Entité » (vide ou 0 : tout). Années du monde, comptées depuis 1, bornes comprises.</summary>
public sealed record FiltreActesDto(string? Auteur = null, string? Sorte = null, int Humain = 0, int Communaute = 0, int DepuisAn = 0, int JusquAn = 0,
    long Avant = 0);

/// <summary>Un acte de l'Entité pour la fenêtre : quand, par qui, quoi, sur qui, ce que les règles en ont fait, et où le retrouver.</summary>
public sealed record ActeDto(long Id, string Date, string Auteur, string Sorte, string Quoi, int Humain, string? NomHumain, int Communaute, string? NomCommunaute,
    int Priere, int Nombre, IReadOnlyList<string> Resultats, string Issue, long JournalPremier, long JournalDernier, long Echange, bool Repris,
    bool Defaire = false, bool Defait = false, string? Recit = null);

/// <summary>Un décret de l'Entité libre, pour la fenêtre : la loi, le facteur, où, depuis quand, jusqu'à quand, pourquoi.</summary>
public sealed record DecretDto(int Id, string Loi, string NomLoi, double Facteur, int Communaute, string? NomCommunaute, string Debut, string? Fin, bool EnVigueur,
    string Raison);

/// <summary>Nombre d'actes une année du monde (comptée depuis 1), par le joueur et par le LLM.</summary>
public sealed record AnActesDto(int An, int Joueur, int Llm);

/// <summary>En-tête de la fenêtre : comptes et courbe par année.</summary>
public sealed record ComptesActesDto(int Total, int ParLlm, int Exaucees, int Refusees, int Morts, int Bienfaits, int Chatiments, int Paroles, int Souffles,
    int SoufflesReussis, int Catastrophes, IReadOnlyList<AnActesDto> ParAn);

/// <summary>Une page d'actes et les comptes du filtre.</summary>
/// <param name="LibreDepuis">Date du premier acte libre (null : aucun).</param>
public sealed record ActesEntiteDto(ComptesActesDto Comptes, IReadOnlyList<ActeDto> Actes, bool Plus, string? LibreDepuis = null, IReadOnlyList<DecretDto>? Decrets = null);

/// <summary>La fenêtre « Actes de l'Entité » : chronologie de tout ce qu'elle a fait, par le joueur ou par le LLM.</summary>
public static class ActesEntite
{
    /// <summary>Sortes proposées au filtre (codes de la table, plus les gestes par ce qu'ils font).</summary>
    public static readonly string[] Sortes =
    [
        "parler", "parler_communaute", "parler_monde", "penchant", "geste.bienfait", "geste.chatiment", "catastrophe", "exaucer", "refuser", "exaucer_toutes", "refuser_toutes",
        "souffler_geste", "souffler_combinaison", "reglage", "libre", "lever_decret",
    ];

    public const int ParPage = 150;

    private static FiltreActes Lire(FiltreActesDto f, int limite)
    {
        int? auteur = f.Auteur switch { "joueur" => Registre.Joueur, "llm" => Registre.Llm, _ => null };
        var sorte = string.IsNullOrEmpty(f.Sorte) ? null : Array.IndexOf(Sortes, f.Sorte) >= 0 ? f.Sorte : throw new ArgumentException(L.T("actes.erreur.sorte"));
        return new FiltreActes(auteur, sorte, Math.Max(0, f.Humain), Math.Max(0, f.Communaute),
            f.DepuisAn > 0 ? (f.DepuisAn - 1) * Calendrier.SecondesParAn : 0, f.JusquAn > 0 ? f.JusquAn * Calendrier.SecondesParAn : 0, Math.Max(0, f.Avant), limite);
    }

    public static ActesEntiteDto Lister(ServiceMonde service, FiltreActesDto f)
    {
        f ??= new FiltreActesDto();
        var depot = service.Depot;
        var filtre = Lire(f, ParPage + 1);
        var actes = depot.Actes(filtre);
        var c = depot.CompterActes(filtre with { Avant = 0 }, Calendrier.SecondesParAn);
        var comptes = new ComptesActesDto(c.Total, c.ParLlm, c.Exaucees, c.Refusees, c.Morts, c.Bienfaits, c.Chatiments, c.Paroles, c.Souffles, c.SoufflesReussis,
            c.Catastrophes, c.ParAn.Select(a => new AnActesDto(a.An + 1, a.Joueur, a.Llm)).ToList());
        var (_, humains) = service.Monde.InstantaneHumains();
        var communautes = service.Monde.Communautes();
        var liste = actes.Take(ParPage).Select(a => Dto(service, a, humains, communautes)).ToList();
        return new ActesEntiteDto(comptes, liste, actes.Count > ParPage, depot.PremierActeLibre() is { } l ? L.Date(l) : null, Decrets(service));
    }

    /// <summary>Les décrets de l'Entité libre : en vigueur d'abord, puis les plus récents.</summary>
    public static List<DecretDto> Decrets(ServiceMonde service)
    {
        var monde = service.Monde;
        var maintenant = monde.Secondes;
        var communautes = monde.Communautes();
        return [.. monde.Decrets().OrderByDescending(d => d.EnVigueur(maintenant)).ThenByDescending(d => d.Id).Select(d => new DecretDto(d.Id, d.Loi.ToString(),
            L.T($"loi.{d.Loi}"), d.Facteur, d.CommunauteId,
            d.CommunauteId >= 1 && d.CommunauteId <= communautes.Count ? L.Communaute(communautes[d.CommunauteId - 1].Nom) : null,
            L.Date(d.Debut), d.Fin > 0 ? L.Date(d.Fin) : null, d.EnVigueur(maintenant), d.Raison))];
    }

    private static ActeDto Dto(ServiceMonde service, ActeEntite a, IReadOnlyList<Simulation.Humains.Humain> humains, IReadOnlyList<Simulation.Humains.Communaute> communautes)
    {
        ActeLlm? acte = null;
        List<string> resultats = [];
        try
        {
            if (a.Acte.Length > 0) acte = JsonSerializer.Deserialize<ActeLlm>(a.Acte, Actes.Json);
            resultats = JsonSerializer.Deserialize<List<string>>(a.Resultats, Actes.Json) ?? [];
        }
        catch (JsonException)
        {
        }
        // Un acte repris du journal : ses lignes (celles de l'Entité, au même instant) disent ce qui s'est passé.
        if (a.Repris && a.JournalDernier >= a.JournalPremier && a.JournalPremier > 0)
        {
            var lignes = service.Depot.LireJournalPlage(a.JournalPremier, a.JournalDernier, entite: true, limite: 6);
            resultats = [.. lignes.Take(5).Select(L.Ecrire)];
            if (lignes.Count > 5) resultats.Add("…");
        }
        return new ActeDto(a.Id, L.Date(a.Le), a.Auteur == Registre.Llm ? "llm" : "joueur", a.Sorte, Quoi(a, acte), a.Humain,
            a.Humain >= 1 && a.Humain <= humains.Count ? humains[a.Humain - 1].Nom : null, a.Communaute,
            a.Communaute >= 1 && a.Communaute <= communautes.Count ? L.Communaute(communautes[a.Communaute - 1].Nom) : null,
            a.Priere, a.Nombre, resultats, a.Issue switch { 1 => "effet", -1 => "refuse", _ => "rien" }, a.JournalPremier, a.JournalDernier, a.Echange, a.Repris,
            a.Sorte == Actes.Libre && a.Defaire.Length > 0 && !a.Defait, a.Defait, a.Sorte == Actes.Libre ? acte?.Recit : null);
    }

    private static string Nom(string cle, string repli) => L.Existe(cle) ? L.T(cle) : repli;

    /// <summary>L'acte en mots : la sorte, puis ce qui le précise (geste, parole, manière, catastrophe, pièces).</summary>
    private static string Quoi(ActeEntite a, ActeLlm? x)
    {
        var sorte = L.T($"actes.sorte.{a.Sorte}");
        var precisions = new List<string>();
        switch (a.Sorte)
        {
            case Actes.Geste when a.Genre.Length > 0:
                precisions.Add(Nom($"gesteEntite.{a.Genre}", a.Genre));
                if (x?.Maniere is { } m && m != "Cachee") precisions.Add(Nom($"maniere.{m}", m));
                break;
            case Actes.ParlerMonde when a.Genre.Length > 0:
                precisions.Add($"« {Nom($"paroleMonde.{a.Genre}", a.Genre)} »");
                if (x?.Mode is { } modeMonde) precisions.Add(Nom($"llm.mode.{modeMonde}", modeMonde));
                if (x?.Terre is { } terre && terre != 0) precisions.Add(L.T("actes.uneTerre"));
                break;
            case Actes.Parler or Actes.ParlerCommunaute when a.Genre.Length > 0:
                precisions.Add($"« {Nom($"parole.{a.Genre}", a.Genre)} »");
                if (x?.Mode is { } mode) precisions.Add(Nom($"llm.mode.{mode}", mode));
                break;
            case Actes.Penchant when x is not null:
                if (x.Passion == true) precisions.Add(L.T("actes.passion"));
                if (x.Mutuel == true) precisions.Add(L.T("actes.mutuel"));
                break;
            case Actes.Catastrophe when a.Genre.Length > 0:
                precisions.Add(Nom($"provoquer.{a.Genre}", a.Genre));
                if (x?.X is { } cx && x.Y is { } cy) precisions.Add($"({cx}, {cy})");
                if (x?.Intensite is { } i && i > 0) precisions.Add(i.ToString("0.#", L.Courante.Nombres));
                break;
            case Actes.Exaucer or Actes.Refuser when a.Priere > 0:
                precisions.Add(L.T("actes.priere", ("n", a.Priere.ToString(CultureInfo.InvariantCulture))));
                if (x?.Signe == true) precisions.Add(L.T("actes.signe"));
                break;
            case Actes.SoufflerGeste when x?.Geste is { } g:
                precisions.Add(string.Join(" ", new[] { Nom($"geste.nom.{g}", g), x.A, x.B }.Where(s => !string.IsNullOrEmpty(s) && s != "Aucune")));
                break;
            case Actes.Libre:
                precisions.Add(L.T(a.Genre == "visible" ? "libre.visible" : "libre.cache"));
                if (x?.Decrets is { Count: > 0 } ds) precisions.Add(L.T("actes.decrets", ("n", ds.Count.ToString(CultureInfo.InvariantCulture))));
                break;
            case Actes.SoufflerCombinaison when x is not null:
                precisions.Add(string.Join(" + ", new[] { x.A, x.B }.Where(s => !string.IsNullOrEmpty(s))));
                break;
        }
        if (a.Nombre > 1 && a.Sorte is not (Actes.Geste or Actes.Catastrophe)) precisions.Add(L.T("actes.nombre", ("n", a.Nombre.ToString(CultureInfo.InvariantCulture))));
        return precisions.Count == 0 ? sorte : $"{sorte} : {string.Join(", ", precisions)}";
    }

    /// <summary>Les lignes du journal écrites par un acte (60 au plus).</summary>
    public static List<EvenementDto> Journal(ServiceMonde service, long id)
    {
        var a = service.Depot.Acte(id);
        if (a is null || a.JournalDernier < a.JournalPremier || a.JournalPremier <= 0) return [];
        // Les lignes pas encore enregistrées sont encore en mémoire.
        var recents = service.Monde.JournalRecent(a.JournalPremier - 1).Where(e => e.Id <= a.JournalDernier && (!a.Repris || e.Origine == Simulation.Origine.Entite));
        var enregistrees = service.Depot.LireJournalPlage(a.JournalPremier, a.JournalDernier, entite: a.Repris, limite: 60);
        return [.. recents.Concat(enregistrees).DistinctBy(e => e.Id).OrderBy(e => e.Id).Take(60).Select(EvenementDto.Depuis)];
    }
}
