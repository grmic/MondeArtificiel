using System.Globalization;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>Séismes, tsunamis et éruptions volcaniques.</summary>
public sealed partial class Catastrophes
{
    /// <summary>Un nombre décimal à la française (« 6,4 »).</summary>
    internal static string Decimal(double v) => v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

    // ───────────── Séismes ─────────────

    private void Seismes(List<Humain> humains, long maintenant)
    {
        if (_alea.Reel() >= Geologie.TauxM5ParAn / Calendrier.JoursParAn) return;
        var (x, y) = Epicentre();
        Seisme(x, y, MagnitudeGutenbergRichter(5, Geologie.MagnitudeMax), humains, maintenant, Origine.Naturelle);
    }

    /// <summary>
    /// Magnitude tirée selon la loi de Gutenberg-Richter (b = 1) : chaque degré de plus est dix fois plus rare,
    /// jusqu'à la magnitude maximale des failles.
    /// </summary>
    private double MagnitudeGutenbergRichter(double min, double max)
    {
        var u = _alea.Reel();
        return min - Math.Log10(1 - u * (1 - Math.Pow(10, -(max - min))));
    }

    /// <summary>Épicentre : le long des failles le plus souvent (à quelques kilomètres près), parfois n'importe où.</summary>
    private (int X, int Y) Epicentre()
    {
        var failles = Geologie.Failles;
        if (failles.Count > 0 && _alea.Reel() < 0.85)
        {
            var longueurs = failles.Select(f => Math.Sqrt(Math.Pow(f.X2 - f.X1, 2) + Math.Pow(f.Y2 - f.Y1, 2))).ToArray();
            var tirage = _alea.Reel() * longueurs.Sum();
            var k = 0;
            while (k < failles.Count - 1 && tirage > longueurs[k]) tirage -= longueurs[k++];
            var f = failles[k];
            var t = _alea.Reel();
            var l = Math.Max(1, longueurs[k]);
            var ecart = 6 * Biologie.Normale(_alea);
            var x = f.X1 + t * (f.X2 - f.X1) - ecart * (f.Y2 - f.Y1) / l;
            var y = f.Y1 + t * (f.Y2 - f.Y1) + ecart * (f.X2 - f.X1) / l;
            return (Math.Clamp((int)Math.Round(x), 0, _carte.Largeur - 1), Math.Clamp((int)Math.Round(y), 0, _carte.Hauteur - 1));
        }
        var i = TerreAuHasard(0, 0, _carte.Largeur - 1, _carte.Hauteur - 1, Climat.EstPraticable);
        return i < 0 ? (_carte.Largeur / 2, _carte.Hauteur / 2) : (i % _carte.Largeur, i / _carte.Largeur);
    }

    /// <summary>
    /// Intensité ressentie (échelle de Mercalli, de I à XII) à une distance donnée de l'épicentre, selon une loi
    /// d'atténuation simple : I ≈ 1,5 M − 3,5 log₁₀(R) + 2,5, R étant la distance au foyer en km (ordres de grandeur
    /// de Bakun et Wentworth, 1997). Un séisme de magnitude 6 à 10 km donne environ VIII ; on le sent à une centaine de km.
    /// </summary>
    internal static double Mercalli(double magnitude, double distanceKm, double profondeurKm)
    {
        var r = Math.Max(1, Math.Sqrt(distanceKm * distanceKm + profondeurKm * profondeurKm));
        return Math.Clamp(1.5 * magnitude - 3.5 * Math.Log10(r) + 2.5, 1, 12);
    }

    private Catastrophe Seisme(int x, int y, double magnitude, List<Humain> humains, long maintenant, Origine origine)
    {
        var profondeur = 8 + 25 * _alea.Reel();
        var sousMarin = Biomes.EstEauSalee(_carte.BiomeEn(x, y));
        // Rayon où on le sent (intensité III) : au-delà, presque personne ne le remarque.
        var portee = Math.Pow(10, (1.5 * magnitude + 2.5 - 3) / 3.5);
        var rayon = Echelle.Cases(Math.Sqrt(Math.Max(0, portee * portee - profondeur * profondeur)));
        var m = Decimal(magnitude);
        var ou = Ou(x, y);
        var texte = sousMarin
            ? $"La terre tremble : un séisme de magnitude {m} secoue les côtes {LieuDe(x, y)}, depuis le fond de la mer."
            : $"La terre tremble : un séisme de magnitude {m} secoue les terres {LieuDe(x, y)}.";
        var c = Commencer(TypeCatastrophe.Seisme, x, y, rayon, magnitude, $"Séisme de magnitude {m} {ou}", texte, humains, maintenant,
            origine, direction: profondeur, duree: Calendrier.SecondesParHeure);
        Secouer(c, magnitude, humains, maintenant);

        // Une faille sous la mer qui joue d'un coup soulève l'eau : les grands séismes de subduction lèvent des tsunamis.
        if (sousMarin && magnitude >= 7.3 && _alea.Reel() < Math.Clamp((magnitude - 7.3) / 1.2, 0.1, 0.95))
        {
            var hauteur = LogNormale(Math.Pow(10, 0.5 * magnitude - 3.3), 0.3);
            Tsunami(x, y, hauteur, c.Id, humains, maintenant, origine);
        }
        // Loi de Båth : la plus forte réplique a environ 1,2 de magnitude de moins ; loi d'Omori : elle vient
        // le plus souvent dans les heures ou les jours qui suivent.
        if (magnitude >= 5.8 && _alea.Reel() < 0.7)
        {
            var heures = Math.Exp(_alea.Reel() * Math.Log(24 * 30));
            c.Prochaine = maintenant + (long)(heures * Calendrier.SecondesParHeure);
        }
        return c;
    }

    /// <summary>Chacun ressent la secousse selon sa distance ; en montagne, les éboulements tuent ; partout, la peur.</summary>
    private void Secouer(Catastrophe c, double magnitude, List<Humain> humains, long maintenant)
    {
        var profondeur = c.Direction;
        var ressentie = new SortedSet<int>();
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            var intensite = Mercalli(magnitude, Echelle.Kilometres(Distance(h.X, h.Y, c.X, c.Y)), profondeur);
            if (intensite < 3) continue;
            c.Temoins.Add(h.Id);
            if (h.CommunauteId > 0) ressentie.Add(h.CommunauteId);
            // Sans maisons, on ne meurt pas écrasé sous un toit : les morts viennent des éboulements en terrain pentu
            // et, plus rarement, des arbres qui tombent.
            var pente = EstPentu(h.X, h.Y);
            var foret = _carte.BiomeEn(h.X, h.Y) is Biome.ForetDeConiferes or Biome.ForetTropicale;
            var mort = pente && intensite >= 7 ? 0.01 * Math.Pow(intensite - 6, 2) : foret && intensite >= 8 ? 0.003 * (intensite - 7) : 0;
            var blessure = pente && intensite >= 6.5 ? 0.04 * (intensite - 6) : intensite >= 8 ? 0.02 * (intensite - 7) : 0;
            if (mort > 0 || blessure > 0) Frapper(h, c, mort, blessure, CauseDeMort.Eboulement, humains, maintenant);
            else Effrayer(h, c, 10 * (intensite - 3), maintenant);
        }
        foreach (var communaute in ressentie)
        {
            if (communaute > Communautes.Count) continue;
            Noter(maintenant, null, $"Toute la {Minuscule(Communautes[communaute - 1].Nom)} a senti la terre trembler.", c.Id, communaute);
            c.Communautes.Add(communaute);
        }
    }

    private bool EstPentu(int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                if (_carte.BiomeEn(nx, ny) is Biome.Montagne or Biome.SommetEnneige) return true;
            }
        }
        return false;
    }

    private void Replique(Catastrophe c, List<Humain> humains, long maintenant)
    {
        var magnitude = c.Intensite - 1.2 + 0.25 * Biologie.Normale(_alea);
        c.Duree = maintenant - c.Debut + Calendrier.SecondesParHeure;
        if (magnitude < 4.5) return;
        Noter(maintenant, null, $"Une forte réplique (magnitude {Decimal(magnitude)}) secoue de nouveau les terres {LieuDe(c.X, c.Y)}.", c.Id);
        Secouer(c, magnitude, humains, maintenant);
    }

    // ───────────── Tsunamis ─────────────

    private int[]? _distanceMer;

    /// <summary>Distance (en cases) de chaque case à la mer, calculée une fois.</summary>
    private int[] DistanceMer()
    {
        if (_distanceMer is not null) return _distanceMer;
        var d = new int[_carte.Biomes.Length];
        Array.Fill(d, int.MaxValue);
        var file = new Queue<int>();
        for (var i = 0; i < d.Length; i++)
        {
            if (!Biomes.EstEauSalee((Biome)_carte.Biomes[i])) continue;
            d[i] = 0;
            file.Enqueue(i);
        }
        while (file.Count > 0)
        {
            var i = file.Dequeue();
            int x = i % _carte.Largeur, y = i / _carte.Largeur;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                    var j = _carte.Indice(nx, ny);
                    if (d[j] <= d[i] + 1) continue;
                    d[j] = d[i] + 1;
                    file.Enqueue(j);
                }
            }
        }
        return _distanceMer = d;
    }

    /// <summary>
    /// Une vague géante : elle noie les terres basses du littoral, jusqu'à quelques kilomètres à l'intérieur selon sa
    /// hauteur (en 2011 au Japon, des vagues de 10 à 20 m ont pénétré de 2 à 5 km). Elle arrive quelques minutes à
    /// une heure après le séisme ; la mer se retire d'abord, et ceux qui comprennent fuient vers les hauteurs.
    /// </summary>
    private Catastrophe? Tsunami(int x, int y, double hauteur, long cause, List<Humain> humains, long maintenant, Origine origine)
    {
        var mer = DistanceMer();
        var profondeur = Math.Clamp(hauteur * 0.25, 1, 8);              // cases noyées depuis le rivage
        var altitudeMax = Carte.NiveauMer + Math.Max(1, (int)Math.Ceiling(hauteur / Echelle.MetresParUnite));
        var portee = Math.Clamp(60 * hauteur / 5, 60, 400);              // côtes atteintes autour de la source
        var cases = new HashSet<int>();
        var plusProche = double.MaxValue;
        foreach (var i in DansLeRayon(x, y, portee))
        {
            if (mer[i] == 0 || mer[i] > profondeur || _carte.Altitudes[i] > altitudeMax) continue;
            if (!Climat.EstPraticable((Biome)_carte.Biomes[i]) && !Biomes.EstEauDouce((Biome)_carte.Biomes[i])) continue;
            cases.Add(i);
            plusProche = Math.Min(plusProche, Distance(i % _carte.Largeur, i / _carte.Largeur, x, y));
        }
        if (cases.Count == 0) return null;
        // L'onde file à 400 à 700 km/h au large : de quelques minutes à une heure avant d'atteindre la côte.
        var delai = 600 + (long)(Echelle.Kilometres(plusProche) / 500 * Calendrier.SecondesParHeure);
        var c = Commencer(TypeCatastrophe.Tsunami, x, y, (int)portee, hauteur, $"Vague géante de {hauteur:0} m {Ou(x, y)}",
            $"Au large, la mer se soulève : une vague géante d'environ {hauteur:0} m court vers les côtes.", humains, maintenant, origine,
            cause: cause, alerter: false);
        c.Cases = cases;
        c.Prochaine = maintenant + delai;
        // Le danger n'existe que sur les côtes noyées : l'alerte vaut s'il y a quelqu'un là.
        if (!c.Signalee && humains.Any(h => h.EstVivant && cases.Contains(_carte.Indice(h.X, h.Y)))) Signaler(c, Journal[^1].Texte);
        return c;
    }

    private void Deferler(Catastrophe c, List<Humain> humains, long maintenant)
    {
        c.Phase = 1;
        c.Duree = maintenant - c.Debut + 6 * Calendrier.SecondesParHeure;
        Noter(maintenant, null, $"La vague géante déferle sur les côtes et noie {Surface(c.Cases.Count)} de terres basses.", c.Id);
        foreach (var h in humains)
        {
            if (h.EstVivant && c.Cases.Contains(_carte.Indice(h.X, h.Y))) Frapper(h, c, 0.5, 0.3, CauseDeMort.Noyade, humains, maintenant);
        }
        // L'eau salée tue la végétation des terres noyées pour des mois ; le gibier se noie ou fuit.
        foreach (var i in c.Cases) Ravager(c, i, plantes: 0.2, gibier: 0.4, degats: 0.8);
        foreach (var h in humains)
        {
            if (h.EstVivant && !c.Temoins.Contains(h.Id) && Distance(h.X, h.Y, c.X, c.Y) <= c.Rayon
                && DistanceMer()[_carte.Indice(h.X, h.Y)] <= 15) Effrayer(h, c, 30, maintenant);
        }
    }

    /// <summary>Dégâts sur la nature d'une case : part des plantes et du gibier qui survivent, dégâts durables sur la végétation.</summary>
    private void Ravager(Catastrophe c, int i, double plantes, double gibier, double degats)
    {
        _ecosysteme.Plantes[i] = (ushort)(_ecosysteme.Plantes[i] * plantes);
        _ecosysteme.Gibier[i] = (ushort)(_ecosysteme.Gibier[i] * gibier);
        var d = (ushort)(Math.Clamp(degats, 0, 1) * 10_000);
        _ecosysteme.Degats[i] = Math.Max(_ecosysteme.Degats[i], d);
        if (d > c.Ravages.GetValueOrDefault(i)) c.Ravages[i] = d;
    }

    // ───────────── Volcans ─────────────

    /// <summary>
    /// Probabilité qu'une éruption atteigne chaque indice d'explosivité (VEI 1 à 7) : chaque degré est environ
    /// trois à dix fois plus rare que le précédent (Newhall et Self, 1982 ; Siebert et al., 2010).
    /// </summary>
    private static readonly double[] ProbabiliteVei = [0, 0.35, 0.35, 0.18, 0.08, 0.03, 0.009, 0.001];

    /// <summary>Rayon (cases) des nuées brûlantes selon l'indice d'explosivité : 2 km pour un VEI 3, 20 km pour un VEI 6.</summary>
    private static readonly int[] RayonsNuees = [0, 0, 0, 3, 8, 15, 25, 40, 60];

    /// <summary>Rayon (cases) des retombées de cendres qui étouffent la végétation : de quelques km à tout le continent.</summary>
    private static readonly int[] RayonsCendres = [1, 3, 8, 20, 50, 120, 300, 600, 900];

    private static int Vei(Catastrophe c) => Math.Clamp((int)c.Intensite, 0, 8);

    private static int RayonNuees(Catastrophe c) => RayonsNuees[Vei(c)];

    private static int RayonLave(Catastrophe c) => 2 + Vei(c);

    private static int RayonMenace(Catastrophe c) => Vei(c) <= 2 ? RayonLave(c) + 3 : RayonNuees(c) + 5;

    private void Volcans(List<Humain> humains, long maintenant)
    {
        foreach (var v in Geologie.Volcans)
        {
            if (_liste.Any(c => c.Active && c.Type == TypeCatastrophe.Eruption && c.Parametre == v.Numero)) continue;
            if (_alea.Reel() >= 1 / (v.ReposMoyenAns * Calendrier.JoursParAn)) continue;
            var tirage = _alea.Reel();
            var vei = 1;
            while (vei < 7 && tirage > ProbabiliteVei[vei]) tirage -= ProbabiliteVei[vei++];
            Eruption(v, vei, humains, maintenant, Origine.Naturelle);
        }
    }

    /// <summary>
    /// Un volcan se réveille : des jours ou des semaines de grondements et de fumées (le Pinatubo a grondé deux mois
    /// avant d'exploser en 1991), puis l'éruption. Ceux qui vivent au pied du volcan peuvent fuir à temps.
    /// </summary>
    private Catastrophe Eruption(Volcan v, int vei, List<Humain> humains, long maintenant, Origine origine)
    {
        var avant = vei <= 2 ? 1 + _alea.Reel() * 6 : 5 + _alea.Reel() * 55;
        var activite = vei <= 2 ? 10 + _alea.Reel() * 80 : 3 + _alea.Reel() * 12;
        var c = Commencer(TypeCatastrophe.Eruption, v.X, v.Y, RayonsCendres[vei], vei, $"Éruption {De(v.Nom)} (indice d'explosivité {vei})",
            $"{Majuscule(v.Nom)} gronde : la terre tremble à ses pieds et une fumée s'élève de son sommet.", humains, maintenant, origine,
            parametre: v.Numero, duree: (long)((avant + activite) * Calendrier.SecondesParJour));
        c.Prochaine = maintenant + (long)(avant * Calendrier.SecondesParJour);
        foreach (var h in humains)
        {
            if (h.EstVivant && Distance(h.X, h.Y, v.X, v.Y) <= RayonMenace(c) * 3) Effrayer(h, c, 20, maintenant);
        }
        return c;
    }

    /// <summary>« du volcan du nord », « de l'… ».</summary>
    private static string De(string nom) => nom.StartsWith("le ", StringComparison.Ordinal) ? "du " + nom[3..] : "de " + nom;

    private void Paroxysme(Catastrophe c, List<Humain> humains, long maintenant)
    {
        c.Phase = 1;
        var v = VolcanDe(c);
        var vei = Vei(c);
        var texte = vei switch
        {
            <= 2 => $"{Majuscule(v.Nom)} entre en éruption : des coulées de lave descendent ses pentes.",
            <= 4 => $"{Majuscule(v.Nom)} explose : une colonne de cendres monte jusqu'au ciel et des nuées brûlantes dévalent ses flancs.",
            _ => $"{Majuscule(v.Nom)} explose avec une violence inouïe : le fracas s'entend dans tout le continent, des nuées brûlantes "
                 + "rasent tout alentour et le jour devient nuit sous les cendres.",
        };
        Noter(maintenant, null, texte, c.Id);
        Signaler(c, texte);

        // Nuées ardentes : presque personne n'y survit (Saint-Pierre, 1902 ; Pompéi, 79).
        var nuees = RayonNuees(c);
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            var d = Distance(h.X, h.Y, c.X, c.Y);
            if (nuees > 0 && d <= nuees) Frapper(h, c, 0.85, 0.5, CauseDeMort.Eruption, humains, maintenant);
            else if (nuees > 0 && d <= nuees * 1.6) Frapper(h, c, 0.08, 0.3, CauseDeMort.Eruption, humains, maintenant);
        }

        // Lave : la végétation brûle sur son passage.
        if (vei <= 2)
        {
            foreach (var i in DansLeRayon(c.X, c.Y, RayonLave(c))) Ravager(c, i, 0, 0.2, 1);
        }

        // Cendres : elles étouffent la végétation, d'autant plus qu'on est près ; on les respire.
        var rayon = RayonsCendres[vei];
        foreach (var i in DansLeRayon(c.X, c.Y, rayon))
        {
            var f = 1 - Distance(i % _carte.Largeur, i / _carte.Largeur, c.X, c.Y) / rayon;
            if (f <= 0.05) continue;
            Ravager(c, i, 1 - 0.9 * f, 1 - 0.6 * f, Math.Pow(f, 0.7));
        }
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            var d = Distance(h.X, h.Y, c.X, c.Y);
            if (d > rayon) continue;
            var f = 1 - d / rayon;
            h.Sante = Math.Max(1, h.Sante - 15 * f);
            Effrayer(h, c, 30 + 50 * f, maintenant);
        }

        // Après un VEI 6 ou 7, le soufre lancé dans la haute atmosphère voile le soleil pendant deux ou trois ans :
        // −0,5 °C dans le monde après le Pinatubo (VEI 6), « l'année sans été » de 1816 après le Tambora (VEI 7).
        if (vei >= 6)
        {
            var ecart = vei == 6 ? -0.8 - 0.4 * _alea.Reel() : -2 - _alea.Reel();
            var annees = vei == 6 ? 2 : 3;
            var hiver = Commencer(TypeCatastrophe.HiverVolcanique, c.X, c.Y, 900, ecart, $"Hiver volcanique après l'éruption {De(v.Nom)}",
                $"Un voile de cendres et de soufre obscurcit le ciel : le soleil pâlit, et le froid gagne tout le continent ({Decimal(ecart)} °C).",
                humains, maintenant, c.Origine, cause: c.Id, duree: annees * Calendrier.SecondesParAn);
            hiver.Signalee = true;
            // Un volcan proche de la mer peut s'effondrer en partie dans l'eau : une vague géante s'ensuit (Krakatoa, 1883).
            if (DistanceMer()[_carte.Indice(c.X, c.Y)] <= 25 && _alea.Reel() < 0.4)
            {
                var (mx, my) = MerLaPlusProche(c.X, c.Y);
                Tsunami(mx, my, 5 + 10 * _alea.Reel(), c.Id, humains, maintenant, c.Origine);
            }
        }
    }

    // ───────────── Au-delà des mers ─────────────

    /// <summary>
    /// Le monde ne s'arrête pas aux rivages de la carte. Ailleurs sur la planète, un grand volcan explose environ une fois
    /// par siècle à l'indice 6 (Krakatoa 1883, Pinatubo 1991) et une fois par millénaire à l'indice 7 (Tambora 1815)
    /// (Newhall et Self 1982 ; Siebert et al. 2010). Son voile de soufre refroidit aussi ce continent : environ −0,5 °C
    /// pendant un à deux ans après le Pinatubo (Self et al. 1996), l'« année sans été » après le Tambora (−0,4 à −0,7 °C
    /// en moyenne sur le globe, bien plus en certaines régions ; Oppenheimer 2003). Ici, personne ne sait d'où vient le froid.
    /// </summary>
    private void Lointains(List<Humain> humains, long maintenant)
    {
        var tirage = _alea.Reel() * Calendrier.JoursParAn;
        if (tirage < 1 / 1000.0) HiverLointain(-0.7 - 0.8 * _alea.Reel(), 2 + _alea.Reel(), grave: true, humains, maintenant);
        else if (tirage < 1 / 1000.0 + 1 / 100.0) HiverLointain(-0.3 - 0.3 * _alea.Reel(), 1 + _alea.Reel(), grave: false, humains, maintenant);
        ImpactLointain(humains, maintenant);
    }

    private void HiverLointain(double ecart, double annees, bool grave, List<Humain> humains, long maintenant)
    {
        var hiver = Commencer(TypeCatastrophe.HiverVolcanique, _carte.Largeur / 2, _carte.Hauteur / 2, 900, ecart,
            $"Hiver volcanique venu d'une éruption lointaine ({Decimal(ecart)} °C)",
            $"Le soleil pâlit derrière un voile laiteux, les couchers de soleil virent au rouge sang et un froid inhabituel s'installe ({Decimal(ecart)} °C). "
            + "Loin au-delà des mers, un volcan géant a explosé : personne ici ne peut le savoir.",
            humains, maintenant, alerter: grave, duree: (long)(annees * Calendrier.SecondesParAn));
        hiver.Signalee = true;
    }

    private (int X, int Y) MerLaPlusProche(int x, int y)
    {
        var mer = DistanceMer();
        for (var r = 1; r < Math.Max(_carte.Largeur, _carte.Hauteur); r++)
        {
            foreach (var i in DansLeRayon(x, y, r))
            {
                if (mer[i] == 0) return (i % _carte.Largeur, i / _carte.Largeur);
            }
        }
        return (x, y);
    }
}
