using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Une prière, telle que l'Entité la voit ; <see cref="Exaucable"/> : en attente, elle peut l'exaucer.</summary>
public sealed record PriereDto(int Id, string Date, int Humain, string Nom, string? Communaute, string Sujet, int Cible, string? NomCible,
    int Ferveur, bool Forte, string? Religion, string Texte, string Issue, string? IssueDate, string? Conclusion, long Evenement, bool Exaucable,
    int CommunauteId = 0, string? Collective = null, int CollectiveId = 0, string? Refus = null);

/// <summary>
/// Bilan des prières (toutes, depuis le début du monde) : exaucées par l'Entité, arrivées d'elles-mêmes, refusées (en secret ou
/// par un signe), pas entendues, en attente ; les remerciements à part.
/// </summary>
public sealed record BilanPrieresDto(int Total, int ExauceesEntite, int ExauceesHasard, int Refusees, int RefuseesEnSecret, int PasEntendues,
    int EnAttente, int Remerciements, int Collectives);

/// <summary>Un précepte d'une religion : ce que disent les fidèles, et d'où il vient (connu de l'Entité seule).</summary>
public sealed record DogmeDto(string Texte, string Origine, bool DeLEntite, string Date, long Evenement);

/// <summary>Un fidèle qui porte un rôle : prophète ou prêtre.</summary>
public sealed record PorteurDto(int Id, string Nom, string Role, string? Source, int Miracles, string? Communaute, bool Vivant, int CommunauteId = 0);

/// <summary>Une religion, ses préceptes, ses fidèles et ceux qui la portent.</summary>
public sealed record ReligionDto(int Id, string Nom, string Sens, string Naissance, int Fondateur, string NomFondateur, string FondeeLe,
    string? Parent, string? Communaute, int Fideles, int FidelesMax, int Ferveur, string? EteinteLe, string? Gestes,
    IReadOnlyList<DogmeDto> Dogmes, IReadOnlyList<PorteurDto> Porteurs, IReadOnlyList<string> Communautes, string? LieuSacre, long Evenement);

/// <summary>Un lieu sacré, pour la carte.</summary>
public sealed record LieuSacreDto(int Id, int X, int Y, string Raison, string? Religion);

/// <summary>La foi d'un humain, pour sa fiche : sa religion, sa ferveur, son rôle, ses prières récentes.</summary>
public sealed record FoiDto(int Religion, string? NomReligion, int Ferveur, string? Role, string? Source, int Miracles, IReadOnlyList<PriereDto> Prieres,
    BilanPrieresDto? Bilan = null);

public static class Religions
{
    private static string Date(long s) => Calendrier.Decomposer(s).ToString();

    private static string? Role(Humain h) => h.Role switch
    {
        RoleReligieux.Prophete => h.Sexe == Sexe.Femme ? "prophétesse" : "prophète",
        RoleReligieux.Pretre => h.Sexe == Sexe.Femme ? "gardienne des rites" : "gardien des rites",
        _ => null,
    };

    private static string Titre(Religion r) => $"{r.Nom} (« {r.Sens} »)";

    /// <summary>Prières en mémoire : en attente d'abord (les plus fortes en tête), puis les plus récentes.</summary>
    public static List<PriereDto> Prieres(ServiceMonde service, bool enAttente, int limite = 300)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var religions = monde.Religions().Religions;
        var communautes = monde.Communautes().ToDictionary(c => c.Id, c => c.Nom);
        return monde.Prieres().Where(p => !enAttente || p.Issue == IssuePriere.EnAttente)
            .OrderBy(p => p.Issue == IssuePriere.EnAttente ? 0 : 1).ThenByDescending(p => p.Issue == IssuePriere.EnAttente && p.Forte)
            .ThenByDescending(p => p.Issue == IssuePriere.EnAttente ? p.Le : p.IssueLe)
            .Take(Math.Clamp(limite, 1, 1000)).Select(p => Depuis(p, humains, religions, communautes)).ToList();
    }

    private static PriereDto Depuis(Priere p, IReadOnlyList<Humain> humains, IReadOnlyList<Religion> religions, IReadOnlyDictionary<int, string> communautes)
    {
        var h = humains[p.HumainId - 1];
        var x = p.CibleId >= 1 && p.CibleId <= humains.Count ? humains[p.CibleId - 1] : null;
        var r = p.ReligionId >= 1 && p.ReligionId <= religions.Count ? religions[p.ReligionId - 1] : null;
        var texte = p.Sujet switch
        {
            SujetPriere.Guerison => x is null ? "guérir" : $"que {x.Nom} guérisse",
            SujetPriere.Nourriture => "avoir à manger",
            SujetPriere.Pluie => "que la pluie revienne",
            SujetPriere.Accalmie => "que le malheur cesse",
            SujetPriere.Enfant => "avoir un enfant",
            SujetPriere.Protection => "que les siens soient protégés de la guerre",
            SujetPriere.Vengeance => $"que {x?.Nom} soit frappé{(x?.Sexe == Sexe.Femme ? "e" : "")}",
            SujetPriere.Amour => $"que {x?.Nom} l'aime",
            _ => "rendre grâce",
        };
        var collective = p.CommunauteId != 0;
        return new PriereDto(p.Id, Date(p.Le), h.Id, h.Nom, communautes.GetValueOrDefault(h.CommunauteId), Cultes.Dire(p.Sujet), p.CibleId, x?.Nom,
            (int)Math.Round(100 * p.Ferveur), p.Forte, r is null ? null : r.Nom, texte,
            p.Refus == RefusPriere.Signe ? "refusée par un signe" : Cultes.Dire(p.Issue), p.IssueLe > 0 ? Date(p.IssueLe) : null,
            p.Conclusion, p.EvenementId,
            p.Issue == IssuePriere.EnAttente && p.Refus == RefusPriere.Aucun && (collective || h.EstVivant) && p.Sujet != SujetPriere.Remerciement,
            h.CommunauteId, collective ? communautes.GetValueOrDefault(p.CommunauteId, "?") : null, p.CommunauteId, p.Refus == RefusPriere.Cache ? "refusée en secret" : null);
    }

    /// <summary>Toutes les religions, les vivantes d'abord (les plus suivies en tête).</summary>
    public static List<ReligionDto> Toutes(ServiceMonde service)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var (religions, lieux, _) = monde.Religions();
        var communautes = monde.Communautes().ToDictionary(c => c.Id, c => c.Nom);
        var fideles = humains.Where(h => h.EstVivant && h.ReligionId != 0).GroupBy(h => h.ReligionId).ToDictionary(g => g.Key, g => g.ToList());
        var flore = monde.Flore;
        return religions.Select(r =>
        {
            var f = fideles.GetValueOrDefault(r.Id) ?? [];
            var porteurs = humains.Where(h => h.ReligionId == r.Id && h.Role != RoleReligieux.Aucun)
                .Select(h => new PorteurDto(h.Id, h.Nom, Role(h)!, h.Role == RoleReligieux.Prophete ? Cultes.Dire(h.SourceProphete) : null, h.Miracles,
                    communautes.GetValueOrDefault(h.CommunauteId), h.EstVivant, h.CommunauteId)).ToList();
            var parCommunaute = f.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId).OrderByDescending(g => g.Count())
                .Select(g => $"{communautes.GetValueOrDefault(g.Key, "?")} ({g.Count()})").ToList();
            var sansGroupe = f.Count(h => h.CommunauteId == 0);
            if (sansGroupe > 0) parCommunaute.Add($"sans communauté ({sansGroupe})");
            var lieu = lieux.Find(l => l.ReligionId == r.Id);
            var fondateur = humains[r.FondateurId - 1];
            return new ReligionDto(r.Id, r.Nom, r.Sens, Cultes.Dire(r.Naissance), r.FondateurId, fondateur.Nom, Date(r.FondeeLe),
                r.ParentId >= 1 && r.ParentId <= religions.Count ? religions[r.ParentId - 1].Nom : null, communautes.GetValueOrDefault(r.CommunauteId),
                f.Count, r.FidelesMax, f.Count == 0 ? 0 : (int)Math.Round(100 * f.Average(h => h.Ferveur)), r.EteinteLe > 0 ? Date(r.EteinteLe) : null,
                r.Gestes, r.Dogmes.Select(d => new DogmeDto(d.Texte, Cultes.Dire(d.Origine), d.Origine is OrigineDogme.Entite, Date(d.Le), d.EvenementId)).ToList(),
                porteurs, parCommunaute, lieu?.Raison, r.EvenementId);
        }).OrderBy(r => r.EteinteLe is null ? 0 : 1).ThenByDescending(r => r.Fideles).ThenBy(r => r.Id).ToList();
    }

    /// <summary>Lieux sacrés, pour la carte.</summary>
    public static List<LieuSacreDto> Lieux(ServiceMonde service)
    {
        var (religions, lieux, _) = service.Monde.Religions();
        return lieux.Select(l => new LieuSacreDto(l.Id, l.X, l.Y, l.Raison,
            l.ReligionId >= 1 && l.ReligionId <= religions.Count ? religions[l.ReligionId - 1].Nom : null)).ToList();
    }

    /// <summary>La foi d'un humain, pour sa fiche.</summary>
    public static FoiDto Foi(ServiceMonde service, Humain h, IReadOnlyList<Humain> humains)
    {
        var monde = service.Monde;
        var religions = monde.Religions().Religions;
        var communautes = monde.Communautes().ToDictionary(c => c.Id, c => c.Nom);
        var r = h.ReligionId >= 1 && h.ReligionId <= religions.Count ? religions[h.ReligionId - 1] : null;
        var prieres = monde.Prieres().Where(p => p.HumainId == h.Id).OrderByDescending(p => p.Id).Take(12)
            .Select(p => Depuis(p, humains, religions, communautes)).ToList();
        return new FoiDto(h.ReligionId, r is null ? null : Titre(r), (int)Math.Round(100 * h.Ferveur), Role(h),
            h.Role == RoleReligieux.Prophete ? Cultes.Dire(h.SourceProphete) : null, h.Miracles, prieres, Bilan(service, [h.Id], 0));
    }

    /// <summary>
    /// Bilan des prières d'humains (prières seuls) et, si <paramref name="communaute"/> n'est pas 0, des prières collectives de
    /// cette communauté : tout ce qui est en base, remplacé par ce qui est en mémoire (plus récent).
    /// </summary>
    public static BilanPrieresDto Bilan(ServiceMonde service, IReadOnlyCollection<int> humains, int communaute)
    {
        var issues = new Dictionary<int, (bool Collective, IssuePriere Issue, RefusPriere Refus, SujetPriere Sujet)>();
        foreach (var (id, collective, issue, refus, sujet) in service.Depot.IssuesPrieres(humains, communaute))
            issues[id] = (collective, issue, refus, sujet);
        var ensemble = humains as IReadOnlySet<int> ?? humains.ToHashSet();
        foreach (var p in service.Monde.Prieres())
        {
            if (p.CommunauteId != 0 ? communaute != 0 && p.CommunauteId == communaute : ensemble.Contains(p.HumainId))
                issues[p.Id] = (p.CommunauteId != 0, p.Issue, p.Refus, p.Sujet);
        }
        int exEntite = 0, exHasard = 0, refusees = 0, enSecret = 0, pasEntendues = 0, enAttente = 0, merci = 0, collectives = 0;
        foreach (var (collective, issue, refus, sujet) in issues.Values)
        {
            if (collective) collectives++;
            if (sujet == SujetPriere.Remerciement) { merci++; continue; }
            if (refus != RefusPriere.Aucun)
            {
                refusees++;
                if (refus == RefusPriere.Cache) enSecret++;
                continue;
            }
            switch (issue)
            {
                case IssuePriere.ExauceeParEntite: exEntite++; break;
                case IssuePriere.ExauceeParHasard: exHasard++; break;
                case IssuePriere.SansReponse: pasEntendues++; break;
                case IssuePriere.EnAttente: enAttente++; break;
            }
        }
        return new BilanPrieresDto(issues.Count - merci, exEntite, exHasard, refusees, enSecret, pasEntendues, enAttente, merci, collectives);
    }
}
