using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Terres d'une communauté sur la carte. <see cref="Plages"/> liste les cases en plages : début, longueur, début, longueur…
/// (indices y × largeur + x, croissants), bien plus court qu'une case par case.
/// </summary>
public sealed record TerritoireDto(int Id, string Nom, int Membres, int[] Plages);

/// <summary>Terres de toutes les communautés à une date (null : maintenant).</summary>
public sealed record TerritoiresDto(long Secondes, string Date, bool Actuel, IReadOnlyList<TerritoireDto> Communautes);

/// <summary>Une date à laquelle les terres des communautés ont été archivées (début d'une saison).</summary>
public sealed record DateArchiveDto(long Secondes, string Date);

/// <summary>Taille d'une communauté au début d'une saison.</summary>
public sealed record TailleDto(long Secondes, string Date, int Membres, int Adultes);

public static class Territoires
{
    /// <summary>Terres actuelles de chaque communauté active.</summary>
    public static TerritoiresDto Actuels(ServiceMonde service)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var membres = humains.Where(h => h.EstVivant && h.CommunauteId != 0).ToLookup(h => h.CommunauteId);
        var liste = monde.Communautes().Where(c => c.Active)
            .Select(c => new TerritoireDto(c.Id, c.Nom, membres[c.Id].Count(), Plages(VieSociale.Etendue(VieSociale.Frequentees(c.Territoire), monde.Carte))))
            .Where(t => t.Plages.Length > 0).ToList();
        return new TerritoiresDto(maintenant, L.Date(maintenant), true, liste);
    }

    /// <summary>Terres archivées à une date (celles en attente d'enregistrement comprises), ou null si rien n'y est archivé.</summary>
    public static TerritoiresDto? Archives(ServiceMonde service, long secondes)
    {
        var archives = service.Monde.ArchivesEnMemoire().Where(a => a.Secondes == secondes).ToList();
        if (archives.Count == 0) archives = service.Depot.LireArchives(secondes);
        if (archives.Count == 0) return null;
        var noms = service.Monde.Communautes().ToDictionary(c => c.Id, c => NomA(c, secondes));
        return new TerritoiresDto(secondes, L.Date(secondes), false,
            archives.Where(a => a.Cases.Length > 0)
                .Select(a => new TerritoireDto(a.CommunauteId, noms.GetValueOrDefault(a.CommunauteId, "?"), a.Membres, Plages(a.Cases))).ToList());
    }

    /// <summary>Dates archivées, de la plus ancienne à la plus récente.</summary>
    public static List<DateArchiveDto> Dates(ServiceMonde service) =>
        service.Depot.LireDatesArchives().Concat(service.Monde.ArchivesEnMemoire().Select(a => a.Secondes))
            .Distinct().Order().Select(s => new DateArchiveDto(s, L.Date(s))).ToList();

    /// <summary>Taille d'une communauté au début de chaque saison.</summary>
    public static List<TailleDto> Tailles(ServiceMonde service, int communaute) =>
        service.Depot.LireTailles(communaute)
            .Select(t => (t.Secondes, t.Membres, t.Adultes))
            .Concat(service.Monde.ArchivesEnMemoire().Where(a => a.CommunauteId == communaute).Select(a => (a.Secondes, a.Membres, a.Adultes)))
            .DistinctBy(t => t.Secondes).OrderBy(t => t.Secondes)
            .Select(t => new TailleDto(t.Secondes, L.Date(t.Secondes), t.Membres, t.Adultes)).ToList();

    /// <summary>Le nom que portait une communauté à une date.</summary>
    private static string NomA(Communaute c, long secondes) => c.Noms.LastOrDefault(n => n.Depuis <= secondes)?.Nom ?? c.Nom;

    /// <summary>Cases triées → plages (début, longueur).</summary>
    public static int[] Plages(IReadOnlyList<int> cases)
    {
        var plages = new List<int>();
        var i = 0;
        while (i < cases.Count)
        {
            var longueur = 1;
            while (i + longueur < cases.Count && cases[i + longueur] == cases[i] + longueur) longueur++;
            plages.Add(cases[i]);
            plages.Add(longueur);
            i += longueur;
        }
        return [.. plages];
    }
}
