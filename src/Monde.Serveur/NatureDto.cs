using Monde.Simulation;

namespace Monde.Serveur;

/// <summary>
/// État de la nature pour le navigateur : indicateurs du monde, abondance de chaque tuile (0 à 255, 128 = niveau naturel)
/// et pluies de l'année par région.
/// </summary>
public sealed record NatureDto(Indicateurs Indicateurs, int Largeur, int Hauteur, byte[] Abondance,
    int RegionsX, int RegionsY, int TailleRegion, IReadOnlyList<double> Pluies)
{
    public static NatureDto Depuis(MondeSimule monde)
    {
        var (pluies, rx, ry) = monde.Pluies();
        return new NatureDto(monde.Indicateurs(), monde.Carte.Largeur, monde.Carte.Hauteur, monde.CarteAbondance(),
            rx, ry, Simulation.Humains.Ecosysteme.TailleRegion, pluies.Select(p => Math.Round(p, 2)).ToList());
    }
}
