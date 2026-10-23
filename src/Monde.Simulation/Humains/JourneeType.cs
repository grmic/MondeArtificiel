using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Compte les gestes quotidiens de chaque humain pendant la journée, pour en faire un résumé au journal
/// (une ligne par humain et par jour au lieu de dizaines). Ces compteurs ne servent qu'au journal :
/// ils ne sont pas sauvegardés, et un monde rechargé en milieu de journée résume une journée partielle.
/// </summary>
public sealed class JourneeType
{
    private readonly Dictionary<int, Dictionary<Activite, int>> _compteurs = [];

    /// <summary>Gestes résumés, dans l'ordre où le résumé les cite.</summary>
    private static readonly Activite[] Ordre =
    [
        Activite.Boire, Activite.Cueillir, Activite.Chasser, Activite.ChercherEau, Activite.ChercherNourriture, Activite.ChercherAbri,
        Activite.SAbriter, Activite.Explorer, Activite.Migrer, Activite.RejoindrePartenaire, Activite.RejoindreProche, Activite.SuivreParent,
        Activite.VeillerEnfant, Activite.Experimenter, Activite.Fabriquer, Activite.Veiller, Activite.Nager, Activite.Repos, Activite.Dormir,
    ];

    public void Compter(int humain, Activite activite)
    {
        if (!_compteurs.TryGetValue(humain, out var c)) _compteurs[humain] = c = [];
        c[activite] = c.GetValueOrDefault(activite) + 1;
    }

    /// <summary>Résumés de la journée écoulée (par humain, dans l'ordre des numéros), puis remise à zéro.</summary>
    public List<EvenementJournal> Resumer(IReadOnlyList<Humain> humains, long maintenant)
    {
        var resumes = new List<EvenementJournal>();
        foreach (var (id, c) in _compteurs.OrderBy(p => p.Key))
        {
            var h = humains[id - 1];
            if (!h.EstVivant) continue;
            var parties = new List<object?>();
            foreach (var a in Ordre)
            {
                var n = c.GetValueOrDefault(a);
                if (n > 0) parties.Add(new Recit("j.jour.fois", ("v", new Recit($"j.jour.{a}", ("h", h))), ("n", n)));
            }
            if (parties.Count == 0) continue;
            resumes.Add(new EvenementJournal(maintenant, TypeEvenement.Journee, id, 0, new Recit("j.journee", ("h", h), ("l", new Liste(parties, virgules: true)))));
        }
        _compteurs.Clear();
        return resumes;
    }
}
