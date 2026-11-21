using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>Ce qui tombe du ciel pendant une averse.</summary>
public enum SorteAverse : byte
{
    Pluie = 1,
    Neige = 2,
    Orage = 3,
}

/// <summary>Une averse d'un jour (heures depuis minuit de ce jour), de force 0 à 1.</summary>
public readonly record struct Averse(double Debut, double Fin, double Force, SorteAverse Sorte, int X = -1, int Y = -1);

/// <summary>
/// Les averses d'un jour de pluie (0.43.0, dans la simulation depuis la 0.45.26). La simulation dit s'il pleut tel jour sur telle
/// région (<see cref="Ecosysteme.Pleut"/>) ; ce jour est détaillé ici en averses (heure de début, de fin, force), tirées de la
/// graine, du jour et de la région, sans toucher au hasard de la simulation : le manteau neigeux (<see cref="Neige"/>), la carte et
/// la vue 3D voient le même ciel. Ordres de grandeur : un jour de pluie sous un climat tempéré compte en moyenne 3 à 6 heures de
/// pluie, en une à trois averses ; une année très arrosée en donne de plus longues et de plus fortes ; les orages d'été éclatent
/// surtout l'après-midi, sont brefs (une demi-heure à deux heures) et violents.
/// </summary>
public static class Averses
{
    /// <summary>Tirage reproductible dans [0, 1) pour une région, un jour et un usage.</summary>
    public static double Tirage(ulong graine, int region, long jour, int usage)
    {
        var etat = graine ^ 0x54454D5053UL ^ ((ulong)region * 0x9E3779B97F4A7C15UL) ^ ((ulong)jour * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)usage * 0x165667B19E3779F9UL);
        return (Alea.SplitMix64(ref etat) >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// Les averses d'un jour de pluie sur une région. <paramref name="pluie"/> : pluies de l'année (1 : normale). Quand il a plu
    /// la veille (ou qu'il pleuvra le lendemain), la pluie a une chance sur deux de continuer depuis minuit (ou jusqu'à minuit) :
    /// les épisodes de deux ou trois jours restent d'un seul tenant. Une averse est dite de neige quand, au cœur de la région et au
    /// milieu de l'averse, il tombe plus de neige que de pluie (<see cref="PartDeNeige"/>) ; sur les pentes, chaque lieu a sa part.
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
        var seuil = SeuilDeNeige(pluie);
        return fondues.Select(a =>
        {
            var milieu = jour * Calendrier.SecondesParJour + (long)((a.Debut + a.Fin) / 2 * Calendrier.SecondesParHeure);
            return PartDeNeige(temperature(milieu), seuil) >= 0.5 ? a with { Sorte = SorteAverse.Neige } : a;
        }).ToList();
    }

    /// <summary>
    /// Température de l'air (°C) à laquelle il tombe autant de neige que de pluie. Elle n'est pas la même partout : sur 17 millions
    /// d'observations de l'hémisphère nord, elle va d'environ −0,4 °C en air humide à 2,4 °C en air sec, pour une moyenne voisine
    /// de 1 °C (Jennings et al., 2018) : en air sec, le flocon qui commence à fondre se refroidit en s'évaporant. Les pluies de
    /// l'année de la région (1 : normales) tiennent lieu d'humidité de l'air.
    /// </summary>
    public static double SeuilDeNeige(double pluies) => Math.Clamp(1.0 + 1.4 * (1 - pluies), -0.4, 2.4);

    /// <summary>
    /// Part de la précipitation qui tombe en neige (0 à 1) à cette température : la neige l'emporte sous le seuil, la pluie au-dessus,
    /// avec entre les deux, sur 2 à 3 °C, de la neige fondue (courbe logistique ajustée sur les mesures de Jennings et al., 2018).
    /// </summary>
    public static double PartDeNeige(double temperature, double seuil) => 1 / (1 + Math.Exp((temperature - seuil) / 0.6));

    /// <summary>
    /// Eau qui tombe en une heure (mm) pour une averse de cette force (0 à 1) : environ 0,5 mm/h pour une petite pluie, 1 à 4 mm/h
    /// pour une pluie modérée, plus pour une averse forte (classes de l'Organisation météorologique mondiale).
    /// </summary>
    public static double EauParHeure(double force) => 0.3 + 4.5 * force * force;
}

/// <summary>
/// Le manteau neigeux de chaque case (0.45.26) : la neige qui tombe vraiment (<see cref="Averses"/>) s'y entasse, se tasse et fond.
/// Rien n'y est écrit d'avance : là où il ne neige pas, il n'y a pas de neige au sol, et une neige tombée tient tant qu'il fait
/// assez froid.
/// <para>
/// Ce que dit la recherche. La neige fraîche pèse de 60 à 120 kg/m³ selon le froid (Hedstrom et Pomeroy, 1998) : un millimètre
/// d'eau en donne environ un centimètre. Elle se tasse en quelques jours vers 250 à 350 kg/m³, davantage quand elle fond et
/// regèle (Anderson, 1976). Elle fond d'environ 3 à 6 mm d'eau par degré au-dessus de zéro et par jour à découvert, deux fois
/// moins sous les arbres (facteur degré-jour : Hock, 2003) ; une pluie tiède la fait fondre en plus (chaleur de l'eau, 80 mm
/// de pluie à 1 °C fondant 1 mm de neige).
/// </para>
/// </summary>
public sealed class Neige
{
    private readonly Carte _carte;
    private readonly Ecosysteme _ecosysteme;

    /// <summary>Équivalent en eau de la neige au sol (mm, soit kg/m²).</summary>
    public float[] Eau { get; }

    /// <summary>Épaisseur de la neige au sol (cm).</summary>
    public float[] Epaisseur { get; }

    /// <summary>Jour dont les averses sont détaillées dans <see cref="_averses"/>.</summary>
    private long _jourAverses = -1;

    private List<Averse>[] _averses = [];

    /// <summary>Dernière heure (secondes) du monde dont la neige a été comptée.</summary>
    private long _compteJusqua = -1;

    public Neige(Carte carte, Ecosysteme ecosysteme, float[]? eau = null, float[]? epaisseur = null)
    {
        _carte = carte;
        _ecosysteme = ecosysteme;
        var n = carte.Largeur * carte.Hauteur;
        Eau = eau is { Length: var a } && a == n ? eau : new float[n];
        Epaisseur = epaisseur is { Length: var b } && b == n ? epaisseur : new float[n];
        for (var i = 0; i < n; i++)
        {
            if (!float.IsFinite(Eau[i]) || !float.IsFinite(Epaisseur[i]) || Eau[i] <= 0 || Epaisseur[i] <= 0) Eau[i] = Epaisseur[i] = 0;
        }
    }

    /// <summary>Y a-t-il de la neige quelque part ?</summary>
    public bool Quelque { get; private set; }

    /// <summary>Épaisseur de neige sur la case (cm).</summary>
    public double EpaisseurEn(int i) => Epaisseur[i];

    /// <summary>Densité de la neige de la case (kg/m³), 0 sans neige.</summary>
    public double DensiteEn(int i) => Epaisseur[i] > 0 ? Eau[i] / Epaisseur[i] * 100 : 0;

    /// <summary>Une case où la neige peut tenir (pas sur l'eau libre).</summary>
    private bool Tient(int i) => (Biome)_carte.Biomes[i] is not (Biome.Ocean or Biome.EauPeuProfonde or Biome.Lac or Biome.Riviere);

    /// <summary>Les averses de la région ce jour-là (avec la règle de continuité d'un jour à l'autre).</summary>
    private List<Averse> AversesDe(int region, long jour, Func<int, int, long, double> temperature)
    {
        if (jour != _jourAverses)
        {
            _averses = new List<Averse>[_ecosysteme.Pluies.Length];
            _jourAverses = jour;
        }
        if (region < 0 || region >= _averses.Length) return [];
        if (_averses[region] is { } deja) return deja;
        var t = Ecosysteme.TailleRegion;
        var rx = _ecosysteme.RegionsX;
        var x = Math.Min(_carte.Largeur - 1, region % rx * t + t / 2);
        var y = Math.Min(_carte.Hauteur - 1, region / rx * t + t / 2);
        var i = _carte.Indice(x, y);
        bool Pleut(long j) => j >= 0 && _ecosysteme.Pleut(i, j * Calendrier.SecondesParJour);
        var liste = Pleut(jour)
            ? Averses.Detailler(_carte.Graine, region, jour, _ecosysteme.Pluies[region], Pleut(jour - 1), Pleut(jour + 1), s => temperature(x, y, Math.Max(0, s)))
            : [];
        return _averses[region] = liste;
    }

    /// <summary>
    /// L'heure qui vient de s'écouler (jusqu'à <paramref name="maintenant"/>) : la neige tombe, se tasse et fond sur chaque case.
    /// <paramref name="temperature"/> : température de l'air sur une case à un instant (°C), celle que ressentent les humains.
    /// </summary>
    public void Heure(long maintenant, Func<int, int, long, double> temperature)
    {
        if (_compteJusqua >= maintenant) return;
        _compteJusqua = maintenant;
        var debut = maintenant - Calendrier.SecondesParHeure;
        if (debut < 0) return;
        var milieu = debut + Calendrier.SecondesParHeure / 2;
        var jour = debut / Calendrier.SecondesParJour;
        var h0 = (debut - jour * Calendrier.SecondesParJour) / (double)Calendrier.SecondesParHeure;
        var h1 = h0 + 1;
        var regions = _ecosysteme.Pluies.Length;
        var l = _carte.Largeur;
        var date = Calendrier.Decomposer(milieu);
        // Le soleil fond davantage au printemps et en été, et de jour (rayonnement), que l'hiver et la nuit.
        var soleil = (date.Saison switch { Saison.Printemps => 1.15, Saison.Ete => 1.3, Saison.Automne => 0.9, _ => 0.8 }) * (0.6 + 0.8 * date.Lumiere);

        // Ce qui tombe, région par région.
        var tombe = new double[regions];
        var tombeQuelquePart = false;
        for (var r = 0; r < regions; r++)
        {
            foreach (var a in AversesDe(r, jour, temperature))
            {
                var recouvre = Math.Min(h1, a.Fin) - Math.Max(h0, a.Debut);
                if (recouvre > 0) tombe[r] += recouvre * Averses.EauParHeure(a.Force);
            }
            tombeQuelquePart |= tombe[r] > 0;
        }
        if (!tombeQuelquePart && !Quelque) return;

        var quelque = false;
        for (var i = 0; i < Eau.Length; i++)
        {
            var r = _ecosysteme.Region(i % l, i / l);
            var eauTombee = r < regions ? tombe[r] : 0;
            if (Eau[i] <= 0 && eauTombee <= 0) continue;
            if (!Tient(i))
            {
                Eau[i] = Epaisseur[i] = 0;
                continue;
            }
            int x = i % l, y = i / l;
            var temp = temperature(x, y, milieu);
            var pluie = 0.0;
            if (eauTombee > 0)
            {
                var part = Averses.PartDeNeige(temp, Averses.SeuilDeNeige(_ecosysteme.Pluies[r]));
                var neige = eauTombee * part;
                pluie = eauTombee - neige;
                if (neige > 0.01)
                {
                    // Neige fraîche : plus légère par grand froid (Hedstrom et Pomeroy, 1998).
                    var densite = 67.92 + 51.25 * Math.Exp(Math.Min(temp, 2) / 2.59);
                    Eau[i] += (float)neige;
                    Epaisseur[i] += (float)(neige / densite * 100);
                }
            }
            if (Eau[i] <= 0) continue;
            // Fonte : degré-heure, moins sous le couvert des arbres, plus au soleil ; la pluie tiède en ajoute.
            if (temp > 0)
            {
                var couvert = Climat.Abri((Biome)_carte.Biomes[i]);
                var facteurJour = (5.5 - 3.5 * couvert) * soleil;
                var fonte = facteurJour / 24 * temp + pluie * temp / 80;
                var reste = Math.Max(0, Eau[i] - fonte);
                Epaisseur[i] = Eau[i] > 0 ? (float)(Epaisseur[i] * reste / Eau[i]) : 0;
                Eau[i] = (float)reste;
            }
            if (Eau[i] < 0.05f)
            {
                Eau[i] = Epaisseur[i] = 0;
                continue;
            }
            // Tassement : vers 300 kg/m³ en quelques jours, vers 450 quand elle fond et regèle.
            var actuelle = Eau[i] / Epaisseur[i] * 100;
            var plafond = temp > -1 ? 450.0 : 300.0;
            if (actuelle < plafond)
            {
                var tassee = plafond - (plafond - actuelle) * Math.Exp(-1.0 / 72);
                Epaisseur[i] = (float)(Eau[i] / tassee * 100);
            }
            quelque = true;
        }
        Quelque = quelque;
    }

    /// <summary>Remet le compte à un instant (monde chargé) : l'heure en cours sera comptée à sa fin.</summary>
    public void Reprendre(long maintenant)
    {
        _compteJusqua = maintenant - maintenant % Calendrier.SecondesParHeure;
        Quelque = Array.Exists(Eau, e => e > 0);
    }
}
