using Monde.Simulation.Humains;

namespace Monde.Simulation;

/// <summary>
/// Un humain tel que le voit la vue 3D (0.41.0) : où il est, vers quelle case il marche et où il en est du chemin, à quelle allure,
/// ce qu'il fait et dans quel état. Lu sans rien changer à la simulation.
/// </summary>
/// <param name="VersX">Case vers laquelle il marche (−1 s'il ne va nulle part).</param>
/// <param name="Avance">Part du chemin vers cette case déjà faite au dernier pas (0 à 1).</param>
/// <param name="ParSeconde">Part du chemin vers cette case qu'il fait par seconde du monde, à son allure du moment.</param>
/// <param name="Etat">Bits : 1 malade, 2 enceinte, 4 porte un bébé, 8 boite, 16 en mer, 32 porté par sa mère.</param>
/// <param name="Humeur">0 abattu, 1 triste, 2 serein, 3 content, 4 joyeux, 5 en deuil, 6 furieux, 7 déprimé.</param>
public readonly record struct HumainScene(int Id, int X, int Y, int VersX, int VersY, double Avance, double ParSeconde,
    Activite Activite, int Etat, double Age, bool Femme, double Teint, int Humeur, double Peur, int Communaute, int Mere);

public sealed partial class MondeSimule
{
    /// <summary>Activités pendant lesquelles on reste sur place : la cible éventuelle est celle d'avant.</summary>
    private static readonly HashSet<Activite> SurPlace =
    [
        Activite.Repos, Activite.Dormir, Activite.Boire, Activite.SAbriter, Activite.Mort, Activite.SAccoupler, Activite.PorteParSaMere,
        Activite.VeillerEnfant, Activite.Experimenter, Activite.Fabriquer, Activite.Veiller, Activite.Soigner, Activite.Enseigner,
        Activite.Pecher, Activite.Cultiver,
    ];

    /// <summary>
    /// Les vivants à moins de <paramref name="rayon"/> cases de (<paramref name="x"/>, <paramref name="y"/>), pour la vue 3D, et l'instant
    /// du dernier pas. Ne copie que ce qu'il faut : appelé quatre fois par seconde.
    /// </summary>
    public (long Secondes, List<HumainScene> Humains) Scene(int x, int y, int rayon)
    {
        lock (_verrou)
        {
            var maintenant = _secondes;
            var liste = new List<HumainScene>();
            foreach (var h in _humains)
            {
                if (!h.EstVivant || Math.Abs(h.X - x) > rayon || Math.Abs(h.Y - y) > rayon) continue;
                int versX = -1, versY = -1;
                double avance = 0, parSeconde = 0;
                if (!SurPlace.Contains(h.Activite))
                {
                    if (h.Chemin is { Count: > 0 } chemin)
                    {
                        versX = chemin[^1] % Carte.Largeur;
                        versY = chemin[^1] / Carte.Largeur;
                    }
                    else if (h.CibleX >= 0 && (h.CibleX != h.X || h.CibleY != h.Y))
                    {
                        versX = h.X + Math.Sign(h.CibleX - h.X);
                        versY = h.Y + Math.Sign(h.CibleY - h.Y);
                    }
                    if (versX >= 0 && versY >= 0 && versX < Carte.Largeur && versY < Carte.Hauteur && (versX != h.X || versY != h.Y)
                        && Math.Abs(versX - h.X) <= 1 && Math.Abs(versY - h.Y) <= 1)
                    {
                        var effort = Marche.Effort(Carte, h.X, h.Y, versX, versY);
                        if (effort > 0)
                        {
                            avance = Math.Clamp(h.Progression / effort, 0, 1);
                            parSeconde = Marche.Vitesse(h, maintenant) / effort;
                        }
                    }
                    else
                    {
                        versX = versY = -1;
                    }
                }
                var etat = (h.EstMalade(maintenant) ? 1 : 0)
                    | (h.EstEnceinte ? 2 : 0)
                    | (h.BebePorteLe >= maintenant - SecondesParPas ? 4 : 0)
                    | (Sequelles.Gene(h, Membre.Jambe, maintenant) > 0 ? 8 : 0)
                    | (h.Mer.EnMer ? 16 : 0)
                    | (h.Activite == Activite.PorteParSaMere ? 32 : 0);
                var humeur = h switch
                {
                    { EstDeprime: true } => 7,
                    { Colere: > 60 } => 6,
                    { Chagrin: > 30 } => 5,
                    { Humeur: > 75 } => 4,
                    { Humeur: > 60 } => 3,
                    { Humeur: > 40 } => 2,
                    { Humeur: > 25 } => 1,
                    _ => 0,
                };
                liste.Add(new HumainScene(h.Id, h.X, h.Y, versX, versY, avance, parSeconde, h.Activite, etat,
                    h.AgeEnAnnees(maintenant), h.Sexe == Sexe.Femme, h.Apparence.Teint, humeur, h.Peur, h.CommunauteId, h.MereId));
            }
            return (maintenant, liste);
        }
    }
}
