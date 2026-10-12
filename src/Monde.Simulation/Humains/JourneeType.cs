namespace Monde.Simulation.Humains;

/// <summary>
/// Compte les gestes quotidiens de chaque humain pendant la journée, pour en faire un résumé au journal
/// (une ligne par humain et par jour au lieu de dizaines). Ces compteurs ne servent qu'au journal :
/// ils ne sont pas sauvegardés, et un monde rechargé en milieu de journée résume une journée partielle.
/// </summary>
public sealed class JourneeType
{
    private readonly Dictionary<int, Dictionary<Activite, int>> _compteurs = [];

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
            var e = h.Sexe == Sexe.Femme ? "e" : "";
            var parties = new List<string>();
            void Ajouter(Activite a, string verbe)
            {
                var n = c.GetValueOrDefault(a);
                if (n > 0) parties.Add(n == 1 ? verbe : $"{verbe} {n} fois");
            }
            Ajouter(Activite.Boire, "a bu");
            Ajouter(Activite.Cueillir, "a cueilli");
            Ajouter(Activite.Chasser, "a chassé");
            Ajouter(Activite.ChercherEau, "est allé" + e + " vers l'eau");
            Ajouter(Activite.ChercherNourriture, "est allé" + e + " vers de quoi manger");
            Ajouter(Activite.ChercherAbri, "a cherché un abri");
            Ajouter(Activite.SAbriter, "s'est recroquevillé" + e + " de froid");
            Ajouter(Activite.Explorer, "a exploré");
            Ajouter(Activite.Migrer, "a marché avec les siens vers de nouvelles terres");
            Ajouter(Activite.RejoindrePartenaire, "a rejoint son compagnon".Replace("son compagnon", h.Sexe == Sexe.Femme ? "son compagnon" : "sa compagne"));
            Ajouter(Activite.RejoindreProche, "est allé" + e + " retrouver un proche");
            Ajouter(Activite.SuivreParent, "a suivi ses parents");
            Ajouter(Activite.VeillerEnfant, "est allé" + e + " chercher son enfant");
            Ajouter(Activite.Experimenter, "a essayé des choses de ses mains");
            Ajouter(Activite.Fabriquer, "a fabriqué ou fait ce qu'il sait faire".Replace("qu'il", h.Sexe == Sexe.Femme ? "qu'elle" : "qu'il"));
            Ajouter(Activite.Veiller, "a entretenu le feu");
            Ajouter(Activite.Nager, "a nagé");
            Ajouter(Activite.Repos, "s'est reposé" + e);
            Ajouter(Activite.Dormir, "a dormi");
            if (parties.Count == 0) continue;
            resumes.Add(new EvenementJournal(maintenant, TypeEvenement.Journee, id, 0, $"Journée de {h.Nom} : {string.Join(", ", parties)}."));
        }
        _compteurs.Clear();
        return resumes;
    }
}
