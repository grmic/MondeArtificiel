using Monde.Simulation;
using Monde.Stockage;

namespace Monde.Serveur;

/// <summary>Un suivi ★ : « humain » ou « communaute » ; <c>EnVie</c> faux pour un mort ou une communauté dispersée.</summary>
public sealed record SuiviDto(string Genre, int Id, string Nom, bool Heritier, bool EnVie)
{
    public static List<SuiviDto> Depuis(ServiceMonde service)
    {
        var monde = service.Monde;
        return service.Suivis().Select(s =>
        {
            var (existe, nom, enVie) = monde.EtatSuivi(s.Communaute, s.Id);
            return new SuiviDto(s.Communaute ? "communaute" : "humain", s.Id, existe && nom.Length > 0 ? nom : s.Nom, s.Heritier, existe && enVie);
        }).ToList();
    }
}

/// <summary>Un proche qu'une alerte propose de suivre.</summary>
public sealed record ProcheDto(int Id, string Nom);

/// <summary>Une alerte d'un suivi, rédigée dans la langue de l'observateur. <c>Id</c> 0 : pas encore gardée dans la base.</summary>
public sealed record AlerteSuiviDto(long Id, long Secondes, string Date, string Sorte, int Humain, int Communaute, long Evenement,
    string Texte, bool Lue, bool Pause, List<ProcheDto> Proches)
{
    public static AlerteSuiviDto Depuis(AlerteSuivi a, ServiceMonde service, HashSet<int>? suivis) =>
        new(0, a.Secondes, L.Date(a.Secondes), a.Sorte.ToString(), a.HumainId, a.CommunauteId, a.EvenementId, L.Rediger(a.Texte), false,
            a.Pause, ProchesDe(a.Sorte, a.HumainId, a.EvenementId, service, suivis));

    public static AlerteSuiviDto Depuis(AlerteEnregistree a, ServiceMonde service, HashSet<int>? suivis) =>
        new(a.Id, a.Secondes, L.Date(a.Secondes), a.Sorte.ToString(), a.HumainId, a.CommunauteId, a.EvenementId,
            a.Texte is { } t ? L.Rediger(t) : "", a.Lue, false, ProchesDe(a.Sorte, a.HumainId, a.EvenementId, service, suivis));

    /// <summary>Humains suivis, pour ne proposer que les proches qui ne le sont pas encore.</summary>
    public static HashSet<int> HumainsSuivis(ServiceMonde service) => service.Suivis().Where(s => !s.Communaute).Select(s => s.Id).ToHashSet();

    // À la mort d'un suivi : ses enfants vivants et son ou sa partenaire, que l'on peut suivre d'un clic.
    private static List<ProcheDto> ProchesDe(SorteAlerte sorte, int humain, long evenement, ServiceMonde service, HashSet<int>? suivis)
    {
        if (sorte != SorteAlerte.Mort || humain == 0 || evenement == 0) return [];
        var (proches, _) = service.Monde.Proches(humain);
        return proches.Where(p => suivis is null || !suivis.Contains(p.Id)).Select(p => new ProcheDto(p.Id, p.Nom)).ToList();
    }
}

/// <summary>Les dernières alertes et le nombre de non lues.</summary>
public sealed record AlertesDto(List<AlerteSuiviDto> Alertes, int NonLues);

/// <summary>Pour chaque sorte d'alerte (dans l'ordre de <c>Sortes</c>) : prévenir, mettre en pause. Les <c>Humain</c> premières concernent un humain.</summary>
public sealed record ReglesAlertesDto(string[] Sortes, bool[] Prevenir, bool[] Pause, int Humain)
{
    public static ReglesAlertesDto Depuis(ReglesAlertes r) =>
        new(Enum.GetNames<SorteAlerte>(), r.Prevenir, r.Pause, Enum.GetValues<SorteAlerte>().Count(ReglesAlertes.HumainSeul));
}
