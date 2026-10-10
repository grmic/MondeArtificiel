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
    string Etape)
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
    };

    private static readonly Dictionary<CauseDeMort, string> Causes = new()
    {
        [Simulation.Humains.CauseDeMort.Faim] = "de faim",
        [Simulation.Humains.CauseDeMort.Soif] = "de soif",
        [Simulation.Humains.CauseDeMort.Froid] = "de froid",
        [Simulation.Humains.CauseDeMort.Maladie] = "de maladie",
        [Simulation.Humains.CauseDeMort.Epuisement] = "d'épuisement",
        [Simulation.Humains.CauseDeMort.Vieillesse] = "de vieillesse",
        [Simulation.Humains.CauseDeMort.Accouchement] = "en couches",
    };

    public static List<HumainDto> Depuis(MondeSimule monde)
    {
        var (maintenant, humains) = monde.InstantaneHumains();
        // Les morts de plus d'un an ne sont plus affichés (la liste grandirait sans fin).
        return humains.Where(h => h.MortLe is not { } m || maintenant - m < Simulation.Temps.Calendrier.SecondesParAn)
            .Select(h => new HumainDto(
            h.Id, h.Nom, h.Sexe == Simulation.Humains.Sexe.Femme ? "femme" : "homme", h.PartenaireId, h.Emplacement,
            Math.Round(h.AgeEnAnnees(h.MortLe ?? maintenant), 1), h.X, h.Y,
            Activites.GetValueOrDefault(h.Activite, "?"), h.EstVivant, h.EstVivant && h.EstMalade(maintenant),
            Math.Round(h.Faim), Math.Round(h.Soif), Math.Round(h.Fatigue), Math.Round(h.Froid), Math.Round(h.Sante, 1),
            h.EstVivant ? null : Causes.GetValueOrDefault(h.CauseDeMort),
            h.MereId, h.PereId, h.EstEnceinte,
            h.EstBebe(h.MortLe ?? maintenant) ? "bébé" : h.EstEnfant(h.MortLe ?? maintenant) ? "enfant" : "adulte")).ToList();
    }
}
