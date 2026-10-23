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
    double Reserves,
    double Peur,
    bool Contagieux,
    string? NomLangue = null)
{



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
            L.T($"activite.{h.Activite}", ("g", Lib.G(h))), h.EstVivant, h.EstVivant && h.EstMalade(maintenant),
            Math.Round(h.Faim), Math.Round(h.Soif), Math.Round(h.Fatigue), Math.Round(h.Froid), Math.Round(h.Sante, 1),
            h.EstVivant ? null : Lib.Cause(h.CauseDeMort),
            h.MereId, h.PereId, h.EstEnceinte,
            h.EstBebe(instant) ? "bébé" : h.EstEnfant(instant) ? "enfant" : "adulte",
            h.EstVivant && h.Memoire.Pensees.Count > 0 ? L.Ecrire(h.Memoire.Pensees[^1]) : null,
            h.EstVivant && !h.EstBebe(maintenant) ? Lib.Humeur(h) : null,
            h.EstVivant ? h.CommunauteId : 0,
            Math.Round(h.Reserves),
            h.EstVivant ? Math.Round(h.Peur) : 0,
            // Porteur d'une maladie : contagieux dès la veille des premiers signes (voir Catastrophes.Contagion).
            h.EstVivant && h.InfectionId != 0 && maintenant >= h.SymptomesLe - Simulation.Temps.Calendrier.SecondesParJour,
            // Son nom dans la langue des siens, à côté de celui que lui donne l'observateur.
            h.NomLangue);
    }
}
