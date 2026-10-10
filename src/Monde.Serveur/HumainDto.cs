using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>Ce que le navigateur reçoit de chaque humain.</summary>
public sealed record HumainDto(
    int Id,
    string Nom,
    string Sexe,
    int Partenaire,
    int Emplacement,
    double Age,
    int X,
    int Y,
    string Activite,
    bool Vivant,
    bool Malade,
    double Faim,
    double Soif,
    double Fatigue,
    double Froid,
    double Sante,
    string? CauseDeMort,
    int Mere,
    int Pere,
    bool Enceinte,
    string Etape,
    string? Pensee,
    string? Humeur,
    int Communaute,
    double Reserves)
{
    private static readonly Dictionary<Activite, string> Activites = new()
    {
        [Simulation.Humains.Activite.Repos] = "se repose",
        [Simulation.Humains.Activite.Dormir] = "dort",
        [Simulation.Humains.Activite.Boire] = "boit",
        [Simulation.Humains.Activite.ChercherEau] = "cherche de l'eau",
        [Simulation.Humains.Activite.Cueillir] = "cueille",
        [Simulation.Humains.Activite.Chasser] = "chasse",
        [Simulation.Humains.Activite.ChercherNourriture] = "cherche à manger",
        [Simulation.Humains.Activite.SAbriter] = "s'abrite",
        [Simulation.Humains.Activite.ChercherAbri] = "cherche un abri",
        [Simulation.Humains.Activite.RejoindrePartenaire] = "rejoint son compagnon",
        [Simulation.Humains.Activite.Explorer] = "explore",
        [Simulation.Humains.Activite.Mort] = "mort",
        [Simulation.Humains.Activite.SAccoupler] = "s'accouple",
        [Simulation.Humains.Activite.SuivreParent] = "suit ses parents",
        [Simulation.Humains.Activite.PorteParSaMere] = "dans les bras de sa mère",
        [Simulation.Humains.Activite.VeillerEnfant] = "rejoint son enfant",
        [Simulation.Humains.Activite.RejoindreProche] = "rejoint un proche",
        [Simulation.Humains.Activite.EviterRival] = "s'éloigne d'un rival",
        [Simulation.Humains.Activite.Migrer] = "part avec les siens",
    };


    public static List<HumainDto> Depuis(MondeSimule monde)
    {
        var (maintenant, humains) = monde.InstantaneHumains();
        // Les morts de plus d'un an ne sont plus affichés (la liste grandirait sans fin).
        return humains.Where(h => h.MortLe is not { } m || maintenant - m < Simulation.Temps.Calendrier.SecondesParAn)
            .Select(h => Depuis(h, maintenant)).ToList();
    }

    public static HumainDto Depuis(Humain h, long maintenant)
    {
        var instant = h.MortLe ?? maintenant;
        return new HumainDto(
            h.Id, h.Nom, h.Sexe == Simulation.Humains.Sexe.Femme ? "femme" : "homme", h.PartenaireId, h.Emplacement,
            Math.Round(h.AgeEnAnnees(instant), 1), h.X, h.Y,
            h.Activite == Simulation.Humains.Activite.RejoindrePartenaire && h.Sexe == Simulation.Humains.Sexe.Homme
                ? "rejoint sa compagne"
                : Activites.GetValueOrDefault(h.Activite, "?"), h.EstVivant, h.EstVivant && h.EstMalade(maintenant),
            Math.Round(h.Faim), Math.Round(h.Soif), Math.Round(h.Fatigue), Math.Round(h.Froid), Math.Round(h.Sante, 1),
            h.EstVivant ? null : Libelles.Cause(h.CauseDeMort),
            h.MereId, h.PereId, h.EstEnceinte,
            h.EstBebe(instant) ? "bébé" : h.EstEnfant(instant) ? "enfant" : "adulte",
            h.EstVivant && h.Memoire.Pensees.Count > 0 ? h.Memoire.Pensees[^1].Texte : null,
            h.EstVivant && !h.EstBebe(maintenant) ? Libelles.Humeur(h) : null,
            h.EstVivant ? h.CommunauteId : 0,
            Math.Round(h.Reserves));
    }
}
