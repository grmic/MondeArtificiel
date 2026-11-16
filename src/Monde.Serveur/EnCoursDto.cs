using Monde.Simulation;
using Monde.Simulation.Nature;

namespace Monde.Serveur;

/// <summary>
/// Une catastrophe en cours, ou dont les terres ne sont pas encore remises, pour le calque de la carte et la vue 3D (0.43.0).
/// </summary>
/// <param name="Type">Clé du type (voir <see cref="CatastropheDto.Types"/>).</param>
/// <param name="Direction">Direction du vent d'un incendie (radians, 0 : vers l'est, π/2 : vers le sud de la carte).</param>
/// <param name="Debut">Instant du début (secondes du monde).</param>
/// <param name="Cases">Cases touchées (y × largeur + x).</param>
/// <param name="Degats">Dégâts qui restent sur chacune de ces cases (0 : remise, 255 : ravagée), dans le même ordre.</param>
/// <param name="Front">Cases en feu en ce moment.</param>
public sealed record ZoneDto(long Id, string Type, string Nom, bool Active, int X, int Y, int Rayon, double Intensite, int Phase,
    double Direction, long Debut, int[] Cases, byte[] Degats, int[] Front);

/// <summary>Ce qui couvre tout le continent (froid, canicule, phénomènes du ciel, hivers) : un bandeau sur la carte.</summary>
public sealed record ContinentDto(long Id, string Type, string Nom);

/// <summary>Les catastrophes à montrer sur la carte et dans la vue 3D, relues toutes les quelques secondes.</summary>
public sealed record EnCoursDto(long Secondes, IReadOnlyList<ZoneDto> Zones, IReadOnlyList<ContinentDto> Continent)
{
    /// <summary>Types dont les terres touchées restent visibles tant qu'elles ne sont pas remises.</summary>
    private static readonly HashSet<TypeCatastrophe> Marquantes =
        [TypeCatastrophe.Incendie, TypeCatastrophe.Eruption, TypeCatastrophe.Crue, TypeCatastrophe.Tsunami, TypeCatastrophe.Impact];

    public static EnCoursDto Depuis(MondeSimule monde)
    {
        var maintenant = monde.Secondes;
        var largeur = monde.Carte.Largeur;
        var degats = monde.CarteDegats();
        var zones = new List<ZoneDto>();
        var continent = new List<ContinentDto>();
        foreach (var c in monde.CatastrophesEnMemoire().OrderBy(c => c.Debut))
        {
            var cle = CatastropheDto.Types.GetValueOrDefault(c.Type, "autre");
            // Ce qui couvre tout le continent n'a pas de zone : un bandeau suffit.
            if (c.Rayon >= largeur || c.Type is TypeCatastrophe.HiverDImpact or TypeCatastrophe.HiverVolcanique
                || (TypesCatastrophe.EstPhenomene(c.Type) && c.Type != TypeCatastrophe.Bolide))
            {
                if (c.Active) continent.Add(new ContinentDto(c.Id, cle, L.Garde(c.Nom)));
                continue;
            }
            if (c.Type == TypeCatastrophe.Epidemie) continue;
            if (!c.Active && !(c.EnSuites(maintenant) && Marquantes.Contains(c.Type))) continue;
            // Une catastrophe finie dont les cases sont toutes remises ne se montre plus.
            var cases = c.Cases.Order().ToArray();
            var restes = cases.Select(i => (byte)Math.Clamp(Math.Round(degats[i] / 10_000.0 * 255), 0, 255)).ToArray();
            if (!c.Active && restes.All(d => d < 8)) continue;
            zones.Add(new ZoneDto(c.Id, cle, L.Garde(c.Nom), c.Active, c.X, c.Y, c.Rayon, Math.Round(c.Intensite, 2), c.Phase,
                Math.Round(c.Direction, 3), c.Debut, cases, restes, [.. c.Front.Keys.Order()]));
        }
        return new EnCoursDto(maintenant, zones, continent);
    }
}
