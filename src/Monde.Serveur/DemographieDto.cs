using Monde.Simulation;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Population d'une communauté au début de chaque saison (et maintenant, si elle vit encore).</summary>
/// <param name="Pic">Plus grand nombre de membres atteint.</param>
public sealed record SerieCommunauteDto(int Id, string Nom, bool Active, int Pic, long[] Secondes, int[] Membres);

/// <summary>Événement marqué sur les courbes : catastrophe meurtrière ou provoquée par l'Entité, guerre sanglante.</summary>
/// <param name="Type">Clé du type de catastrophe, ou « guerre ».</param>
/// <param name="MortsSuites">Morts dans les terres dévastées, après la catastrophe (0 pour une guerre).</param>
public sealed record RepereDto(long Id, string Type, long Secondes, string Date, string Nom, int Morts, int MortsSuites, bool Entite);

/// <summary>
/// Courbes démographiques : population du monde à intervalles réguliers (avec naissances et morts de chaque intervalle),
/// communautés les plus peuplées, et repères des événements qui ont pu peser sur elles.
/// </summary>
/// <param name="Pas">Intervalle entre deux points de la courbe du monde, en secondes (un mois au moins).</param>
/// <param name="Monde">Vivants à 0, Pas, 2 × Pas… et maintenant (dernier point).</param>
/// <param name="Naissances">Naissances pendant l'intervalle qui finit à ce point.</param>
public sealed record DemographieDto(long Maintenant, string Date, long SecondesParAn, long Pas, long[] Secondes, int[] Monde,
    int[] Naissances, int[] Morts, IReadOnlyList<SerieCommunauteDto> Communautes, int CommunautesEnTout, IReadOnlyList<RepereDto> Reperes)
{
    /// <summary>Points au plus sur la courbe du monde : au-delà, l'intervalle s'allonge (un mois, puis une saison, une année…).</summary>
    private const int PointsMax = 1500;

    public static DemographieDto Calculer(ServiceMonde service, int limite)
    {
        limite = Math.Clamp(limite, 1, 30);
        var monde = service.Monde;
        var (maintenant, vies) = monde.Vies();

        // Monde : vivants à chaque date, par balayage des naissances et des morts triées.
        var pas = Math.Max(30 * Calendrier.SecondesParJour, maintenant / PointsMax);
        pas = (pas + Calendrier.SecondesParJour - 1) / Calendrier.SecondesParJour * Calendrier.SecondesParJour;
        var dates = new List<long>();
        for (long t = 0; t < maintenant; t += pas) dates.Add(t);
        dates.Add(maintenant);
        var naissances = vies.Select(v => v.Naissance).Order().ToArray();
        var morts = vies.Where(v => v.Mort is not null).Select(v => v.Mort!.Value).Order().ToArray();
        var vivants = new int[dates.Count];
        var nees = new int[dates.Count];
        var mortes = new int[dates.Count];
        int iN = 0, iM = 0;
        for (var k = 0; k < dates.Count; k++)
        {
            var (n0, m0) = (iN, iM);
            while (iN < naissances.Length && naissances[iN] <= dates[k]) iN++;
            while (iM < morts.Length && morts[iM] <= dates[k]) iM++;
            vivants[k] = iN - iM;
            // Au premier point, les fondateurs (nés avant le début du monde) ne sont pas des naissances.
            nees[k] = k == 0 ? naissances.Take(iN).Count(n => n > 0) : iN - n0;
            mortes[k] = iM - m0;
        }

        // Communautés : instantanés de saison (enregistrés et en attente), plus la taille actuelle des communautés actives.
        var communautes = monde.Communautes();
        var actuels = vies.Where(v => v.Mort is null && v.Communaute != 0).GroupBy(v => v.Communaute).ToDictionary(g => g.Key, g => g.Count());
        var points = service.Depot.LireToutesTailles()
            .Concat(monde.ArchivesEnMemoire().Select(a => (Communaute: a.CommunauteId, a.Secondes, a.Membres)))
            .DistinctBy(p => (p.Communaute, p.Secondes))
            .Concat(communautes.Where(c => c.Active).Select(c => (Communaute: c.Id, Secondes: maintenant, Membres: actuels.GetValueOrDefault(c.Id))))
            .GroupBy(p => p.Communaute)
            .ToDictionary(g => g.Key, g => g.DistinctBy(p => p.Secondes).OrderBy(p => p.Secondes).ToList());
        var series = points.Where(p => p.Key >= 1 && p.Key <= communautes.Count)
            .Select(p => (Communaute: communautes[p.Key - 1], Points: p.Value, Pic: p.Value.Max(x => x.Membres)))
            .OrderByDescending(s => s.Pic).ThenBy(s => s.Communaute.Id).Take(limite)
            .Select(s => new SerieCommunauteDto(s.Communaute.Id, L.Communaute(s.Communaute.Nom), s.Communaute.Active, s.Pic,
                [.. s.Points.Select(x => x.Secondes)], [.. s.Points.Select(x => x.Membres)]))
            .ToList();

        // Repères : catastrophes meurtrières ou de l'Entité (en mémoire d'abord : leur bilan y est le plus frais), guerres sanglantes.
        var enMemoire = monde.CatastrophesEnMemoire()
            .Where(c => c.Morts > 0 || c.MortsSuites > 0 || c.Origine == Origine.Entite)
            .Select(c => new Stockage.RepereCatastrophe(c.Id, c.Type, c.Origine, c.Debut, c.Nom, c.Morts, c.MortsSuites));
        var reperes = enMemoire.Concat(service.Depot.LireReperesCatastrophes()).DistinctBy(c => c.Id)
            .Select(c => new RepereDto(c.Id, CatastropheDto.Types.GetValueOrDefault(c.Type, "autre"), c.Debut,
                L.Date(c.Debut), c.Nom, c.Morts, c.MortsSuites, c.Origine == Origine.Entite))
            .Concat(monde.Guerres().Where(g => g.MortsA + g.MortsB > 0).Select(g => new RepereDto(g.Id, "guerre", g.Debut,
                L.Date(g.Debut), L.T("demographie.guerre", ("a", NomDe(communautes, g.A)), ("b", NomDe(communautes, g.B))),
                g.MortsA + g.MortsB, 0, false)))
            .OrderBy(r => r.Secondes).ToList();

        return new DemographieDto(maintenant, L.Date(maintenant), Calendrier.SecondesParAn, pas,
            [.. dates], vivants, nees, mortes, series, points.Count, reperes);
    }

    private static string NomDe(IReadOnlyList<Simulation.Humains.Communaute> communautes, int id) =>
        id >= 1 && id <= communautes.Count ? communautes[id - 1].Nom : "?";
}
