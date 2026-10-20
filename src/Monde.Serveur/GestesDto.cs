using System.Globalization;
using Monde.Simulation.Humains;
using Monde.Simulation.Nature;

namespace Monde.Serveur;

/// <summary>
/// Un geste que l'Entité peut faire : sur un humain ou une communauté, bienfait ou châtiment, bornes de durée (jours) et de
/// force (0 : sans objet), et ce qu'il faut préciser (« forme », « maladie », « calamite » ou null).
/// </summary>
public sealed record GesteDto(string Cle, string Libelle, bool SurHumain, bool Bienfait, double JoursMin, double JoursMax, double ForceMin, double ForceMax,
    string? Precision);

/// <summary>Tout ce qu'il faut pour composer un geste : gestes, manières, formes de mort et maladies (ordinaires, puis contagieuses).</summary>
public sealed record ChoixGestesDto(IReadOnlyList<GesteDto> Gestes, IReadOnlyList<ChoixDto> Manieres, IReadOnlyList<ChoixDto> Formes, IReadOnlyList<ChoixDto> Maladies);

/// <summary>
/// Un geste de l'Entité sur <see cref="Cible"/> (un humain, ou une communauté selon le geste). <see cref="Param"/> : forme de la
/// mort, ou maladie (voir <see cref="Gestes.Contagieuse"/>) ; <see cref="Calamite"/> : clé de la catastrophe ; <see cref="Force"/>
/// et <see cref="Jours"/> : ramenées dans les bornes du geste.
/// </summary>
public sealed record DemandeGeste(string Geste, int Cible, string Maniere, int Param = 0, string? Calamite = null, double Force = 0, double Jours = 0);

public static class GestesEntite
{
    public static ChoixGestesDto Choix(ServiceMonde service)
    {
        var gestes = Enum.GetValues<GesteEntite>().Select(g =>
        {
            var (jmin, jmax) = Gestes.Jours(g);
            var (fmin, fmax) = Gestes.Force(g);
            return new GesteDto(g.ToString(), Gestes.Nom(g), Gestes.SurHumain(g), Gestes.EstBienfait(g), jmin, jmax, fmin, fmax, g switch
            {
                GesteEntite.FrapperDeMort => "forme",
                GesteEntite.RendreMalade or GesteEntite.Epidemie => "maladie",
                GesteEntite.Calamite => "calamite",
                _ => null,
            });
        }).ToList();
        var manieres = Enum.GetValues<ManiereGeste>().Select(m => new ChoixDto(m.ToString(), m == ManiereGeste.Cachee ? "Caché (passe pour naturel)" : Majuscule(Gestes.NomManiere(m))))
            .ToList();
        var formes = Enum.GetValues<FormeMort>().Select(f => new ChoixDto(((int)f).ToString(CultureInfo.InvariantCulture), Majuscule(Gestes.NomForme(f)))).ToList();
        var maladies = Enum.GetValues<Maladie>().Where(m => m != Maladie.Aucune)
            .Select(m => new ChoixDto(((int)m).ToString(CultureInfo.InvariantCulture), Maladies.Fiche(m).Nom))
            .Concat(service.Monde.Pathogenes().Select(p => new ChoixDto((Gestes.Contagieuse + p.Id).ToString(CultureInfo.InvariantCulture), $"{p.Nom} (contagieuse)")))
            .Append(new ChoixDto("0", "Une maladie nouvelle (contagieuse)"))
            .ToList();
        return new ChoixGestesDto(gestes, manieres, formes, maladies);
    }

    /// <summary>
    /// Vérifie la demande et la traduit. Lève <see cref="ArgumentException"/> (message pour l'utilisateur) si elle est invalide.
    /// </summary>
    public static (GesteEntite Geste, ManiereGeste Maniere, int Param) Lire(DemandeGeste d, IReadOnlyCollection<int> pathogenes)
    {
        if (!Enum.TryParse<GesteEntite>(d.Geste, out var geste) || !Enum.IsDefined(geste)
            || !Enum.TryParse<ManiereGeste>(d.Maniere, out var maniere) || !Enum.IsDefined(maniere))
            throw new ArgumentException("Geste ou manière inconnus.");
        if (!double.IsFinite(d.Force) || !double.IsFinite(d.Jours)) throw new ArgumentException("Force ou durée invalides.");
        var param = geste switch
        {
            GesteEntite.FrapperDeMort when d.Param is >= 0 and <= (int)FormeMort.Sommeil => d.Param,
            GesteEntite.RendreMalade or GesteEntite.Epidemie when d.Param == 0
                || d.Param is >= 1 and < Gestes.Contagieuse && Enum.IsDefined((Maladie)d.Param)
                || d.Param > Gestes.Contagieuse && pathogenes.Contains(d.Param - Gestes.Contagieuse) => d.Param,
            GesteEntite.Calamite when CatastropheDto.TypeDepuis(d.Calamite) is { } t && t is not (TypeCatastrophe.HiverVolcanique or TypeCatastrophe.HiverDImpact) => (int)t,
            GesteEntite.FrapperDeMort or GesteEntite.RendreMalade or GesteEntite.Epidemie or GesteEntite.Calamite => throw new ArgumentException("Précision invalide."),
            _ => 0,
        };
        return (geste, maniere, param);
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
