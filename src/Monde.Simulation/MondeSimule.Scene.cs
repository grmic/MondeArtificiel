using Monde.Simulation.Monde;
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
/// <param name="Etat">Bits : 1 malade, 2 enceinte, 4 porte un bébé, 8 boite, 16 en mer, 32 porté par sa mère, 64 allaite,
/// 128 chasse (porte un épieu), 256 travaille aux champs (plus d'un tiers de ce qu'il mange en vient).</param>
/// <param name="Humeur">0 abattu, 1 triste, 2 serein, 3 content, 4 joyeux, 5 en deuil, 6 furieux, 7 déprimé.</param>
/// <param name="Reserves">Réserves du corps (graisse), 0 à 100 (0.41.1 : maigreur ou embonpoint à l'affichage).</param>
/// <param name="Endurance">Trait Endurance, 0 à 1 (0.41.1 : musculature à l'affichage).</param>
/// <param name="Taille">Taille propre (écart à la moyenne, −3 à 3, <see cref="Apparence.Taille"/>).</param>
/// <param name="Enfants">Enfants qu'elle a mis au monde (0.41.1 : forme de la poitrine et du ventre).</param>
/// <param name="Vetement">Matière de ce qui le couvre (<see cref="Matiere.Aucune"/> : nu).</param>
/// <param name="Usure">État de ce qui le couvre (1 neuf, 0 en lambeaux).</param>
public readonly record struct HumainScene(int Id, int X, int Y, int VersX, int VersY, double Avance, double ParSeconde,
    Activite Activite, int Etat, double Age, bool Femme, double Teint, int Humeur, double Peur, int Communaute, int Mere,
    double Reserves = 50, double Endurance = 0.5, double Taille = 0, int Enfants = 0, Matiere Vetement = Matiere.Aucune, double Usure = 0);

/// <summary>Une hutte vue en 3D (0.41.1).</summary>
public readonly record struct HutteScene(int Id, int X, int Y, double Solidite, bool Enduite);

/// <summary>Un champ vu en 3D : surface semée, et où il en est (0 semé, 1 mûr).</summary>
public readonly record struct ChampScene(int Id, int X, int Y, double Ares, int Etat, double Croissance);

/// <summary>Des bêtes vues en 3D : gardées (près de leur gardien) ou sauvages (selon le gibier de la case).</summary>
public readonly record struct BetesScene(int Id, int X, int Y, int Forme, double Poids, int Nombre, bool Gardees);

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
            // Enfants nés de chaque mère, et l'âge du plus jeune vivant (elle allaite tant qu'il a moins de deux ans et demi).
            var enfants = new Dictionary<int, int>();
            var plusJeune = new Dictionary<int, double>();
            foreach (var e in _humains)
            {
                if (e.MereId <= 0) continue;
                enfants[e.MereId] = enfants.GetValueOrDefault(e.MereId) + 1;
                if (!e.EstVivant) continue;
                var age = e.AgeEnAnnees(maintenant);
                if (!plusJeune.TryGetValue(e.MereId, out var a) || age < a) plusJeune[e.MereId] = age;
            }
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
                    | (h.Activite == Activite.PorteParSaMere ? 32 : 0)
                    | (h.Sexe == Sexe.Femme && plusJeune.TryGetValue(h.Id, out var cadet) && cadet < 2.5 ? 64 : 0)
                    | (h.Objets.Any(o => o.Matiere == Matiere.Epieu) ? 128 : 0)
                    | (h.PartDesChamps > 1 / 3.0 ? 256 : 0);
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
                    h.AgeEnAnnees(maintenant), h.Sexe == Sexe.Femme, h.Apparence.Teint, humeur, h.Peur, h.CommunauteId, h.MereId,
                    h.Reserves, h.Personnalite.Endurance, h.Apparence.Taille, enfants.GetValueOrDefault(h.Id),
                    h.Vetement?.Matiere ?? Matiere.Aucune, h.Vetement?.Usure ?? 0));
            }
            return (maintenant, liste);
        }
    }

    /// <summary>
    /// Ce qui entoure les humains dans la vue 3D (0.41.1) : huttes, champs, bêtes gardées (sur la case de leur gardien) et bêtes
    /// sauvages (quelques-unes par case, selon le gibier et la faune du terrain). Lu sans rien changer à la simulation.
    /// </summary>
    public (List<HutteScene> Huttes, List<ChampScene> Champs, List<BetesScene> Betes) Camp(int x, int y, int rayon)
    {
        lock (_verrou)
        {
            bool Pres(int cx, int cy) => Math.Abs(cx - x) <= rayon && Math.Abs(cy - y) <= rayon;
            var huttes = _installations.Huttes.Where(h => Pres(h.X, h.Y))
                .Select(h => new HutteScene(h.Id, h.X, h.Y, h.Solidite, h.EnduiteLe > 0)).ToList();
            var champs = _champs.Parcelles.Where(p => Pres(p.X, p.Y))
                .Select(p => new ChampScene(p.Id, p.X, p.Y, p.Ares, (int)p.Etat, p.Croissance)).ToList();
            var betes = new List<BetesScene>();
            foreach (var t in _champs.Troupeaux)
            {
                if (t.Abattue || t.Nombre < 0.5 || t.Espece < 1 || t.Espece > Faune.Betes.Count) continue;
                var g = t.GardienId > 0 && t.GardienId <= _humains.Count ? _humains[t.GardienId - 1] : null;
                if (g is not { EstVivant: true } || !Pres(g.X, g.Y)) continue;
                var b = Faune.Betes[t.Espece - 1];
                betes.Add(new BetesScene(t.Id, g.X, g.Y, b.Forme, b.Poids * Math.Clamp(t.Taille, 0.3, 2), (int)Math.Round(t.Nombre), true));
            }
            var carte = Carte;
            for (var cy = Math.Max(0, y - rayon); cy <= Math.Min(carte.Hauteur - 1, y + rayon); cy++)
            {
                for (var cx = Math.Max(0, x - rayon); cx <= Math.Min(carte.Largeur - 1, x + rayon); cx++)
                {
                    var i = cy * carte.Largeur + cx;
                    var gibier = _ecosysteme.Gibier[i];
                    if (gibier < 500) continue;
                    var especes = Faune.Sur((Biome)carte.Biomes[i]);
                    if (especes.Count == 0) continue;
                    // Une espèce par case, toujours la même (tirée de la case) : quelques bêtes, plus nombreuses où le gibier abonde.
                    var b = especes[(int)((uint)(cx * 73856093 ^ cy * 19349663) % (uint)especes.Count)];
                    var nombre = (int)Math.Clamp(Math.Round(gibier / 1500.0 * (b.Poids < 30 ? 2 : 1)), 1, 8);
                    betes.Add(new BetesScene(-(i + 1), cx, cy, b.Forme, b.Poids, nombre, false));
                }
            }
            return (huttes, champs, betes);
        }
    }
}
