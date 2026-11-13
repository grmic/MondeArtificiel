using Monde.Simulation.Humains;

namespace Monde.Simulation;

/// <summary>La veille sur les suivis ★ (0.40.0) : elle prévient, et peut arrêter le monde ; elle ne change rien à son histoire.</summary>
public sealed partial class MondeSimule
{
    private readonly Veille _veille = new();

    /// <summary>Les suivis (humains et communautés) et, pour chaque sorte d'alerte, s'il faut prévenir et mettre en pause.</summary>
    public void DefinirVeille(IEnumerable<int> humains, IEnumerable<int> communautes, ReglesAlertes regles)
    {
        lock (_verrou) _veille.Definir(humains, communautes, regles, _humains, _communautes);
    }

    /// <summary>Alertes survenues depuis le dernier appel (elles ne sont rendues qu'une fois).</summary>
    public List<AlerteSuivi> PrendreAlertesSuivis()
    {
        lock (_verrou) return _veille.Prendre();
    }

    /// <summary>
    /// Les proches d'un humain (mort, le plus souvent) que l'on peut se mettre à suivre : ses enfants vivants, de l'aîné au
    /// plus jeune, puis son ou sa partenaire vivant ; et son héritier : l'aîné des enfants vivants, sinon l'aîné des petits-enfants
    /// vivants (0 s'il n'y en a pas).
    /// </summary>
    public (List<(int Id, string Nom)> Proches, int Heritier) Proches(int humainId)
    {
        lock (_verrou)
        {
            var proches = new List<(int, string)>();
            if (humainId <= 0 || humainId > _humains.Count) return (proches, 0);
            var h = _humains[humainId - 1];
            var enfants = _humains.Where(x => x.MereId == humainId || x.PereId == humainId).OrderBy(x => x.Naissance).ThenBy(x => x.Id).ToList();
            foreach (var e in enfants.Where(x => x.EstVivant)) proches.Add((e.Id, e.Nom));
            var partenaires = _humains.Where(x => x.EstVivant && x.Id != humainId && (x.PartenaireId == humainId || x.Id == h.PartenaireId));
            foreach (var p in partenaires.OrderBy(x => x.Id)) if (!proches.Any(c => c.Item1 == p.Id)) proches.Add((p.Id, p.Nom));
            var heritier = enfants.FirstOrDefault(x => x.EstVivant)?.Id ?? 0;
            if (heritier == 0)
            {
                var ids = enfants.Select(x => x.Id).ToHashSet();
                heritier = _humains.Where(x => x.EstVivant && (ids.Contains(x.MereId) || ids.Contains(x.PereId)))
                    .OrderBy(x => x.Naissance).ThenBy(x => x.Id).FirstOrDefault()?.Id ?? 0;
            }
            return (proches, heritier);
        }
    }

    /// <summary>Nom d'un humain (vide s'il n'existe pas).</summary>
    public string NomHumain(int humainId)
    {
        lock (_verrou) return humainId > 0 && humainId <= _humains.Count ? _humains[humainId - 1].Nom : "";
    }

    /// <summary>Ce que l'on sait d'un suivi : existe-t-il, son nom, vit-il encore (une communauté : est-elle active) ?</summary>
    public (bool Existe, string Nom, bool EnVie) EtatSuivi(bool communaute, int id)
    {
        lock (_verrou)
        {
            if (!communaute)
                return id > 0 && id <= _humains.Count ? (true, _humains[id - 1].Nom, _humains[id - 1].EstVivant) : (false, "", false);
            var c = _communautes.FirstOrDefault(x => x.Id == id);
            return c is null ? (false, "", false) : (true, c.Nom, c.Active);
        }
    }
}
