using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Ce qui tombe du ciel pendant une averse.</summary>
public enum SorteAverse : byte
{
    Pluie = 1,
    Neige = 2,
    Orage = 3,
}

/// <summary>
/// Le temps qu'il fait (0.43.0, proposition A1), pour la carte et la vue 3D. La simulation dit seulement s'il pleut tel jour sur
/// telle région (<see cref="Ecosysteme.Pleut"/>) ; ici, ce jour de pluie est détaillé en averses (heure de début, de fin, force),
/// tirées de la graine, du jour et de la région, sans toucher au hasard de la simulation : tous les navigateurs voient le même ciel.
/// Ordres de grandeur : un jour de pluie sous un climat tempéré compte en moyenne 3 à 6 heures de pluie, en une à trois
/// averses ; une année très arrosée en donne de plus longues et de plus fortes ; les orages d'été éclatent surtout l'après-midi,
/// sont brefs (une demi-heure à deux heures) et violents. Sous 0,5 °C, il neige.
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

    /// <summary>Une averse d'un jour (heures depuis minuit de ce jour).</summary>
    public readonly record struct Averse(double Debut, double Fin, double Force, SorteAverse Sorte, int X = -1, int Y = -1);

    private static void Ajouter(List<double> liste, int region, Averse a, double decalage) =>
        liste.AddRange([region, Math.Round(a.Debut + decalage, 3), Math.Round(a.Fin + decalage, 3), Math.Round(a.Force, 3), (double)a.Sorte, a.X, a.Y]);

    /// <summary>Tirage reproductible dans [0, 1) pour une région, un jour et un usage.</summary>
    public static double Tirage(ulong graine, int region, long jour, int usage)
    {
        var etat = graine ^ 0x54454D5053UL ^ ((ulong)region * 0x9E3779B97F4A7C15UL) ^ ((ulong)jour * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)usage * 0x165667B19E3779F9UL);
        return (Alea.SplitMix64(ref etat) >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// Les averses d'un jour de pluie sur une région. <paramref name="pluie"/> : pluies de l'année (1 : normale). Quand il a plu
    /// la veille (ou qu'il pleuvra le lendemain), la pluie a une chance sur deux de continuer depuis minuit (ou jusqu'à minuit) :
    /// les épisodes de deux ou trois jours restent d'un seul tenant.
    /// </summary>
    public static List<Averse> Detailler(ulong graine, int region, long jour, double pluie, bool veille, bool lendemain, Func<long, double> temperature)
    {
        var saison = (Saison)(jour % Calendrier.JoursParAn / Calendrier.JoursParSaison);
        double T(int usage) => Tirage(graine, region, jour, usage);
        var arrose = Math.Clamp(pluie, 0.5, 1.8);
        var midi = temperature(jour * Calendrier.SecondesParJour + 12 * Calendrier.SecondesParHeure);
        var averses = new List<Averse>();
        // Été chaud : la pluie du jour tombe en orages l'après-midi, le plus souvent.
        if (saison == Saison.Ete && midi >= 22 && T(1) < 0.65)
        {
            var n = 1 + (T(2) < 0.3 ? 1 : 0);
            for (var k = 0; k < n; k++)
            {
                var debut = 13 + 6 * T(10 + k) + 2 * k;
                var duree = 0.5 + 1.5 * T(20 + k);
                averses.Add(new Averse(debut, Math.Min(24, debut + duree), Math.Clamp(0.6 + 0.4 * T(30 + k) * arrose, 0, 1), SorteAverse.Orage));
            }
            return averses;
        }
        // Heures de pluie du jour : 1,5 à 9 h, davantage les années arrosées ; en une à trois averses.
        var total = (1.5 + 7.5 * T(3) * T(3)) * arrose;
        var nombre = 1 + (T(4) < 0.45 ? 1 : 0) + (T(5) < 0.2 ? 1 : 0);
        var force = Math.Clamp((0.2 + 0.6 * T(6)) * Math.Sqrt(arrose), 0.1, 1);
        var bornes = Enumerable.Range(0, nombre).Select(k => 24 * T(40 + k)).Order().ToList();
        for (var k = 0; k < nombre; k++)
        {
            var duree = total / nombre * (0.6 + 0.8 * T(50 + k));
            var debut = Math.Clamp(bornes[k], 0, 24 - Math.Min(duree, 23.5));
            if (k == 0 && veille && T(7) < 0.5) debut = 0;
            var fin = Math.Min(24, debut + duree);
            if (k == nombre - 1 && lendemain && T(8) < 0.5) fin = 24;
            averses.Add(new Averse(debut, fin, Math.Clamp(force * (0.7 + 0.6 * T(60 + k)), 0.08, 1), SorteAverse.Pluie));
        }
        // Les averses qui se chevauchent n'en font qu'une.
        averses.Sort((a, b) => a.Debut.CompareTo(b.Debut));
        var fondues = new List<Averse>();
        foreach (var a in averses)
        {
            if (fondues.Count > 0 && a.Debut <= fondues[^1].Fin) fondues[^1] = fondues[^1] with { Fin = Math.Max(fondues[^1].Fin, a.Fin), Force = Math.Max(fondues[^1].Force, a.Force) };
            else fondues.Add(a);
        }
        // Sous le gel, il neige (température au milieu de l'averse).
        return fondues.Select(a =>
        {
            var milieu = jour * Calendrier.SecondesParJour + (long)((a.Debut + a.Fin) / 2 * Calendrier.SecondesParHeure);
            return temperature(milieu) <= 0.5 ? a with { Sorte = SorteAverse.Neige } : a;
        }).ToList();
    }

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
