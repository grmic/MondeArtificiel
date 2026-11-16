using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur.Entite;

/// <summary>
/// L'état d'un ordre de l'Entité chez celui qui l'a reçu (0.44.11), tel qu'il le garde (son impulsion) : pas compris, pas cru,
/// refusé, remis à plus tard, en cours, fait, fait à moitié, abandonné, ou pas fait faute de temps. Une impulsion s'efface deux ans
/// après : l'ordre n'est alors plus suivi.
/// </summary>
public static class Ordres
{
    public static string Etat(Impulsion? i, long maintenant)
    {
        if (i is null) return L.T("ordre.etat.inconnu");
        var transmis = i.Parole == Parole.Transmets && i.Param > 0 ? " — " + L.T("ordre.etat.transmis", ("n", i.Param)) : "";
        var fin = L.Date(Math.Min(i.Jusqua, maintenant));
        // Un geste tenu un moment (s'asseoir, prier, jeûner…) : Param garde sa durée dès qu'il l'a commencé.
        var commence = (Paroles.Occupe(i.Parole) || i.Parole == Parole.Jeune) && i.Param > 0;
        var etat = i.Reponse switch
        {
            Reponse.Acceptee or Reponse.AMoitie when commence && maintenant < i.Jusqua => L.T("ordre.etat.enCours"),
            Reponse.Acceptee when commence => L.T("ordre.etat.fait", ("date", fin)),
            Reponse.AMoitie when commence => L.T("ordre.etat.moitie", ("date", fin)),
            Reponse.Incomprise => L.T("ordre.etat.incompris"),
            Reponse.Incrue => L.T("ordre.etat.incru"),
            Reponse.Refusee => L.T("ordre.etat.refuse"),
            Reponse.Abandonnee => L.T("ordre.etat.abandonne", ("date", fin)),
            Reponse.Accomplie when i.AgirLe == long.MaxValue => L.T("ordre.etat.fait", ("date", fin)),
            Reponse.AMoitie when i.AgirLe == long.MaxValue => L.T("ordre.etat.moitie", ("date", fin)),
            Reponse.Accomplie or Reponse.Ressenti or Reponse.Horreur => L.T("ordre.etat.recu"),
            Reponse.Differee when i.AgirLe > maintenant => L.T("ordre.etat.plusTard", ("date", L.Date(i.AgirLe))),
            Reponse.Acceptee or Reponse.AMoitie or Reponse.Differee when maintenant < i.Jusqua => L.T("ordre.etat.enCours"),
            _ => L.T("ordre.etat.oublie"),
        };
        return etat + transmis;
    }
}
