using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Le temps qu'il fait (0.43.0, proposition A1), pour la carte et la vue 3D. La simulation dit seulement s'il pleut tel jour sur
/// telle région (<see cref="Ecosysteme.Pleut"/>) ; ici, ce jour de pluie est détaillé en averses (heure de début, de fin, force),
/// tirées de la graine, du jour et de la région, sans toucher au hasard de la simulation : tous les navigateurs voient le même ciel.
/// Ordres de grandeur : un jour de pluie sous un climat tempéré compte en moyenne 3 à 6 heures de pluie, en une à trois
/// averses ; une année très arrosée en donne de plus longues et de plus fortes ; les orages d'été éclatent surtout l'après-midi,
/// sont brefs (une demi-heure à deux heures) et violents. Neige ou pluie : voir <see cref="Simulation.Nature.Averses.PartDeNeige"/>.
/// </summary>
/// <param name="Jour">Jour du monde (depuis le début).</param>
/// <param name="Averses">Par 7 nombres : région, début et fin (heures depuis minuit du jour, négatives pour la veille), force
/// (0 à 1), sorte (<see cref="SorteAverse"/>), case x et y du cœur de l'orage (−1 : nulle part en particulier).</param>
public sealed record TempsDto(int Jour, int RegionsX, int RegionsY, int TailleRegion, double[] Averses)
{
    public static TempsDto Depuis(MondeSimule monde)
    {
        var secondes = monde.Secondes;
        var jour = secondes / Calendrier.SecondesParJour;
        var carte = monde.Carte;
        var (pluies, rx, ry) = monde.Pluies();
        var avant = monde.JoursDePluie(jour - 2);
        var hier = monde.JoursDePluie(jour - 1);
        var aujourdhui = monde.JoursDePluie(jour);
        var demain = monde.JoursDePluie(jour + 1);
        var anomalie = monde.AnomalieTemperature;
        var foudre = Foudre(monde, jour);
        var averses = new List<double>();
        for (var r = 0; r < aujourdhui.Length; r++)
        {
            var x = Math.Min(carte.Largeur - 1, r % rx * Ecosysteme.TailleRegion + Ecosysteme.TailleRegion / 2);
            var y = Math.Min(carte.Hauteur - 1, r / rx * Ecosysteme.TailleRegion + Ecosysteme.TailleRegion / 2);
            double Temperature(long s) => Climat.Temperature(carte, x, y, Math.Max(0, s)) + anomalie;
            if (hier[r])
            {
                foreach (var a in Detailler(carte.Graine, r, jour - 1, pluies[r], avant[r], aujourdhui[r], Temperature)) Ajouter(averses, r, a, -24);
            }
            if (aujourdhui[r])
            {
                foreach (var a in Detailler(carte.Graine, r, jour, pluies[r], hier[r], demain[r], Temperature)) Ajouter(averses, r, a, 0);
            }
        }
        // Un feu allumé par la foudre a son orage, là et à cette heure-là.
        foreach (var (fx, fy, debut) in foudre)
        {
            var r = fy / Ecosysteme.TailleRegion * rx + fx / Ecosysteme.TailleRegion;
            var heure = (debut - jour * Calendrier.SecondesParJour) / (double)Calendrier.SecondesParHeure;
            var u = Tirage(carte.Graine, r, jour, 97);
            Ajouter(averses, r, new Averse(heure - 0.3 - 0.4 * u, heure + 0.4 + 0.8 * u, 0.55 + 0.35 * u, SorteAverse.Orage, fx, fy), 0);
        }
        return new TempsDto((int)jour, rx, ry, Ecosysteme.TailleRegion, [.. averses]);
    }

    private static void Ajouter(List<double> liste, int region, Averse a, double decalage) =>
        liste.AddRange([region, Math.Round(a.Debut + decalage, 3), Math.Round(a.Fin + decalage, 3), Math.Round(a.Force, 3), (double)a.Sorte, a.X, a.Y]);

    /// <summary>Tirage reproductible dans [0, 1) pour une région, un jour et un usage (voir <see cref="Simulation.Nature.Averses.Tirage"/>).</summary>
    public static double Tirage(ulong graine, int region, long jour, int usage) => Simulation.Nature.Averses.Tirage(graine, region, jour, usage);

    /// <summary>Les averses d'un jour de pluie sur une région (voir <see cref="Simulation.Nature.Averses.Detailler"/>).</summary>
    public static List<Averse> Detailler(ulong graine, int region, long jour, double pluie, bool veille, bool lendemain, Func<long, double> temperature) =>
        Simulation.Nature.Averses.Detailler(graine, region, jour, pluie, veille, lendemain, temperature);

    /// <summary>Feux allumés par la foudre aujourd'hui (case et instant du départ).</summary>
    private static List<(int X, int Y, long Debut)> Foudre(MondeSimule monde, long jour)
    {
        var depuis = jour * Calendrier.SecondesParJour;
        var liste = new List<(int, int, long)>();
        foreach (var c in monde.CatastrophesEnMemoire())
        {
            if (c.Type != TypeCatastrophe.Incendie || c.Debut < depuis || c.Origine != Origine.Naturelle) continue;
            if (monde.EvenementEnMemoire(c.Id)?.Recit?.Cle == "cat.feu.foudre") liste.Add((c.X, c.Y, c.Debut));
        }
        return liste;
    }
}

/// <summary>
/// La neige (0.45.26) pour la carte et la vue 3D, une valeur par case : <paramref name="Epaisseur"/> au sol, codée v = 16 √cm
/// (0 à 255, soit jusqu'à 2,5 m, finement pour un simple saupoudrage) ; <paramref name="Densite"/> en kg/m³ divisés par deux ;
/// <paramref name="Part"/> de ce qui tomberait en neige à cet instant (0 : pluie, 255 : neige) ; <paramref name="Air"/> : température
/// de l'air à cet instant (°C, un octet signé par case, en Base64).
/// </summary>
public sealed record NeigeDto(long Secondes, int Largeur, int Hauteur, byte[] Epaisseur, byte[] Densite, byte[] Part, byte[] Air)
{
    public static NeigeDto Depuis(MondeSimule monde)
    {
        var (epaisseur, eau, part, air, secondes) = monde.CarteNeige();
        var e = new byte[epaisseur.Length];
        var d = new byte[epaisseur.Length];
        for (var i = 0; i < e.Length; i++)
        {
            if (epaisseur[i] <= 0) continue;
            e[i] = (byte)Math.Clamp(Math.Ceiling(16 * Math.Sqrt(epaisseur[i])), 0, 255);
            d[i] = (byte)Math.Clamp(Math.Round(eau[i] / epaisseur[i] * 50), 0, 255);
        }
        return new NeigeDto(secondes, monde.Carte.Largeur, monde.Carte.Hauteur, e, d, part, [.. air.Select(a => unchecked((byte)a))]);
    }
}
