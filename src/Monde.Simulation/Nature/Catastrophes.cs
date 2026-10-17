using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>Une alerte pour l'observateur : une catastrophe touche des humains, ou un phénomène frappe les esprits.</summary>
/// <param name="Grave">Vrai pour une catastrophe qui menace des vies (le monde peut alors se mettre en pause).</param>
public sealed record AlerteCatastrophe(long CatastropheId, TypeCatastrophe Type, string Texte, bool Grave);

/// <summary>
/// Les catastrophes naturelles et les phénomènes du ciel : leur survenue spontanée, leur déroulement, ce qu'elles font
/// aux humains (morts, blessures, fuite, peur, émerveillement), aux communautés et à la nature (végétation et gibier
/// détruits, climat refroidi). Chaque conséquence cite la catastrophe dans le journal.
/// <para>
/// Les fréquences et les ordres de grandeur viennent d'observations réelles, citées à chaque endroit ; rien n'est
/// figé : chaque catastrophe a sa propre intensité, et chaque humain réagit selon son caractère, son âge et sa santé.
/// L'Entité pourra en déclencher (<see cref="Declencher"/>) : le journal en garde alors l'origine.
/// </para>
/// </summary>
public sealed partial class Catastrophes
{
    private readonly Carte _carte;
    private readonly Ecosysteme _ecosysteme;
    private readonly Alea _alea;
    private readonly List<Catastrophe> _liste;
    private readonly List<Pathogene> _pathogenes;

    /// <summary>Danger de chaque case touchée : 1 = menace (on s'éloigne), 2 = mortel (on y risque sa vie).</summary>
    private readonly Dictionary<int, (byte Niveau, Catastrophe Source)> _danger = [];

    public Catastrophes(Carte carte, Ecosysteme ecosysteme, Alea alea, IEnumerable<Catastrophe>? catastrophes = null,
        IEnumerable<Pathogene>? pathogenes = null)
    {
        _carte = carte;
        _ecosysteme = ecosysteme;
        _alea = alea;
        Geologie = Geologie.Generer(carte);
        _liste = (catastrophes ?? []).Select(c => c.Copier()).OrderBy(c => c.Id).ToList();
        _pathogenes = (pathogenes ?? []).OrderBy(p => p.Id).ToList();
        CalculerClimat();
        CalculerDangers();
    }

    public Geologie Geologie { get; }

    /// <summary>Événements survenus : le monde les verse au journal après le pas.</summary>
    public List<EvenementJournal> Journal { get; } = [];

    /// <summary>Alertes à transmettre à l'observateur (le monde les retire après le pas).</summary>
    public List<AlerteCatastrophe> Alertes { get; } = [];

    /// <summary>Numéros des événements (partagés avec tout le monde simulé).</summary>
    public Numeroteur Numeros { get; set; } = new();

    /// <summary>Pour noter les morts comme les autres (deuil, journal) ; fourni par le monde.</summary>
    public Comportement? Comportement { get; set; }

    /// <summary>Communautés (fourni par le monde), pour nommer celles qui sont touchées.</summary>
    public IReadOnlyList<Communaute> Communautes { get; set; } = [];

    /// <summary>Catastrophes en cours, et celles terminées mais pas encore enregistrées (copies).</summary>
    public List<Catastrophe> EnMemoire => _liste.Select(c => c.Copier()).ToList();

    public IReadOnlyList<Pathogene> Pathogenes => _pathogenes;

    /// <summary>
    /// Retire de la mémoire les catastrophes terminées qui viennent d'être enregistrées, sauf celles dont les suites
    /// durent encore (elles comptent leurs morts jusqu'au bout et seront réenregistrées). Elles restent un jour de plus,
    /// le temps que <see cref="FinDesSuites"/> en dresse le bilan.
    /// </summary>
    public void Oublier(IReadOnlySet<long> terminees, long maintenant) =>
        _liste.RemoveAll(c => !c.Active && !c.EnSuites(maintenant - Calendrier.SecondesParJour) && terminees.Contains(c.Id));

    // ───────────── Climat ─────────────

    /// <summary>Écart de température (°C) dû aux vagues de froid, canicules et hivers volcaniques en cours.</summary>
    public double Anomalie { get; private set; }

    private void CalculerClimat()
    {
        double anomalie = 0, vegetation = 1;
        foreach (var c in _liste)
        {
            if (!c.Active) continue;
            switch (c.Type)
            {
                case TypeCatastrophe.VagueDeFroid or TypeCatastrophe.Canicule:
                    anomalie += c.Intensite;
                    break;
                case TypeCatastrophe.HiverVolcanique:
                    anomalie += c.Intensite;
                    // Des étés plus frais et moins ensoleillés : les récoltes de 1816 ont chuté de 10 à 30 % en Europe.
                    vegetation *= Math.Clamp(1 + 0.08 * c.Intensite, 0.6, 1);
                    break;
                case TypeCatastrophe.HiverDImpact:
                    anomalie += c.Intensite;
                    // Poussières et suies coupent la lumière : la photosynthèse s'effondre pendant des mois (Toon et al. 1997 ;
                    // Brugger et al. 2017, pour l'impact de la fin du Crétacé).
                    vegetation *= Math.Clamp(1 + 0.04 * c.Intensite, 0.05, 1);
                    break;
            }
        }
        Anomalie = anomalie;
        _ecosysteme.FacteurClimat = vegetation;
    }

    // ───────────── Dangers ─────────────

    /// <summary>Danger sur une case (null s'il n'y en a pas).</summary>
    public (byte Niveau, Catastrophe Source)? DangerEn(int x, int y) =>
        _danger.TryGetValue(_carte.Indice(x, y), out var d) ? d : null;

    public bool EstDangereuse(int indice) => _danger.ContainsKey(indice);

    /// <summary>Des flammes brûlent sur cette case ou à côté (front d'un incendie).</summary>
    public bool BruleAutour(int x, int y)
    {
        foreach (var c in _liste)
        {
            if (!c.Active || c.Type != TypeCatastrophe.Incendie || c.Front.Count == 0) continue;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                    if (c.Front.ContainsKey(_carte.Indice(nx, ny))) return true;
                }
        }
        return false;
    }

    /// <summary>
    /// Des braises couvent sur cette case : elle a brûlé pendant un incendie encore en cours, ou éteint depuis peu. Les troncs
    /// et les souches couvent bien plus longtemps que l'herbe (combustion lente : Rein, 2009).
    /// </summary>
    public bool BraisesEn(int x, int y, long maintenant)
    {
        var i = _carte.Indice(x, y);
        foreach (var c in _liste)
        {
            if (c.Type != TypeCatastrophe.Incendie || !c.Cases.Contains(i)) continue;
            if (c.Active || maintenant - c.Fin < Couve(_carte.BiomeEn(x, y)) * Calendrier.SecondesParJour) return true;
        }
        return false;
    }

    /// <summary>Jours pendant lesquels un sol brûlé garde des braises, selon ce qui y a brûlé.</summary>
    private static double Couve(Biome b) => b switch
    {
        Biome.ForetTropicale => 10,
        Biome.ForetDeConiferes => 7,
        Biome.Marais or Biome.PlaineDeFougeres => 2,
        _ => 1,
    };

    /// <summary>
    /// Fumée d'un incendie visible d'ici (à moins de <paramref name="rayon"/> cases) : la case brûlée la plus proche où des
    /// braises couvent encore, ou null.
    /// </summary>
    public (int X, int Y)? FumeeEnVue(int x, int y, int rayon, long maintenant)
    {
        (int X, int Y)? meilleure = null;
        var d2 = long.MaxValue;
        foreach (var c in _liste)
        {
            if (c.Type != TypeCatastrophe.Incendie || c.Cases.Count == 0) continue;
            if ((long)(c.X - x) * (c.X - x) + (long)(c.Y - y) * (c.Y - y) > (long)(rayon + c.Rayon + 1) * (rayon + c.Rayon + 1)) continue;
            foreach (var i in c.Cases)
            {
                int cx = i % _carte.Largeur, cy = i / _carte.Largeur;
                long d = (long)(cx - x) * (cx - x) + (long)(cy - y) * (cy - y);
                if (d >= d2 || d > (long)rayon * rayon || _danger.ContainsKey(i)) continue;
                if (!c.Active && maintenant - c.Fin >= Couve(_carte.BiomeEn(cx, cy)) * Calendrier.SecondesParJour) continue;
                (meilleure, d2) = ((cx, cy), d);
            }
        }
        return meilleure;
    }

    /// <summary>Incendie qui a brûlé cette case (encore actif), ou null : on y trouve des bêtes surprises par le feu.</summary>
    public Catastrophe? BruleeRecemment(int x, int y)
    {
        var i = _carte.Indice(x, y);
        foreach (var c in _liste)
            if (c.Active && c.Type == TypeCatastrophe.Incendie && c.Cases.Contains(i)) return c;
        return null;
    }

    private void CalculerDangers()
    {
        _danger.Clear();
        foreach (var c in _liste)
        {
            if (!c.Active) continue;
            switch (c.Type)
            {
                case TypeCatastrophe.Incendie:
                    foreach (var i in c.Front.Keys) Marquer(i, 2, c, 2);
                    break;
                case TypeCatastrophe.Crue:
                    // Pendant le passage d'une crue subite, le lit et les berges sont mortels ; sinon on évite les terres noyées.
                    foreach (var i in c.Cases) Poser(i, c.Phase == 0 && c.Parametre == 1 && PresDeLEau(i) ? (byte)2 : (byte)1, c);
                    break;
                case TypeCatastrophe.Tsunami:
                    foreach (var i in c.Cases) Poser(i, c.Phase == 1 ? (byte)2 : (byte)1, c);
                    break;
                case TypeCatastrophe.Eruption:
                    if (c.Phase == 0) Disque(c.X, c.Y, RayonMenace(c), 1, c);
                    else if (c.Intensite <= 2) { Disque(c.X, c.Y, RayonLave(c), 2, c); Disque(c.X, c.Y, RayonLave(c) + 3, 1, c); }
                    break;
                case TypeCatastrophe.Cyclone:
                    Disque(c.X, c.Y, c.Rayon, 1, c);
                    break;
            }
        }
    }

    /// <summary>La case est une rivière ou un lac, ou les borde.</summary>
    private bool PresDeLEau(int indice)
    {
        int x = indice % _carte.Largeur, y = indice / _carte.Largeur;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < _carte.Largeur && ny < _carte.Hauteur && Biomes.EstEauDouce(_carte.BiomeEn(nx, ny))) return true;
            }
        }
        return false;
    }

    /// <summary>La case et ses voisines jusqu'à <paramref name="rayon"/> : la case elle-même au niveau donné, le voisinage en menace.</summary>
    private void Marquer(int indice, byte niveau, Catastrophe c, int rayon)
    {
        int x = indice % _carte.Largeur, y = indice / _carte.Largeur;
        for (var dy = -rayon; dy <= rayon; dy++)
        {
            for (var dx = -rayon; dx <= rayon; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                Poser(_carte.Indice(nx, ny), dx == 0 && dy == 0 ? niveau : (byte)1, c);
            }
        }
    }

    private void Disque(int cx, int cy, int rayon, byte niveau, Catastrophe c)
    {
        foreach (var i in DansLeRayon(cx, cy, rayon)) Poser(i, niveau, c);
    }

    private void Poser(int i, byte niveau, Catastrophe c)
    {
        if (!_danger.TryGetValue(i, out var d) || d.Niveau < niveau) _danger[i] = (niveau, c);
    }

    /// <summary>Cases à moins de <paramref name="rayon"/> cases (distance euclidienne) d'un point.</summary>
    private IEnumerable<int> DansLeRayon(int cx, int cy, double rayon)
    {
        var r = (int)Math.Ceiling(rayon);
        var r2 = rayon * rayon;
        for (var y = Math.Max(0, cy - r); y <= Math.Min(_carte.Hauteur - 1, cy + r); y++)
        {
            for (var x = Math.Max(0, cx - r); x <= Math.Min(_carte.Largeur - 1, cx + r); x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r2) yield return _carte.Indice(x, y);
            }
        }
    }

    private static double Distance(int x1, int y1, int x2, int y2) => Math.Sqrt((double)(x1 - x2) * (x1 - x2) + (double)(y1 - y2) * (y1 - y2));

    // ───────────── Déroulement ─────────────

    /// <summary>Une fois par jour : de nouvelles catastrophes peuvent survenir ; les peurs s'apaisent.</summary>
    public void Jour(List<Humain> humains, long maintenant)
    {
        var date = Calendrier.Decomposer(maintenant);
        Seismes(humains, maintenant);
        Volcans(humains, maintenant);
        Crues(humains, maintenant, date.Saison);
        Feux(humains, maintenant, date.Saison);
        Cyclones(humains, maintenant, date.Saison);
        Temperatures(humains, maintenant, date.Saison);
        PluieSurLesFeux(maintenant);
        FinDesSuites(maintenant);
        Lointains(humains, maintenant);
        Apaiser(humains);
        CalculerClimat();
        CalculerDangers();
    }

    /// <summary>
    /// Chaque heure : les catastrophes en cours avancent (le feu gagne, l'eau monte, le volcan se réveille),
    /// ceux qui restent dans une zone mortelle risquent leur vie, les maladies se transmettent.
    /// </summary>
    public void Heure(List<Humain> humains, long maintenant)
    {
        var date = Calendrier.Decomposer(maintenant);
        foreach (var c in _liste.ToList())
        {
            if (!c.Active) continue;
            if (c.Prochaine > 0 && maintenant >= c.Prochaine) Etape(c, humains, maintenant);
            if (c.Type == TypeCatastrophe.Incendie && c.Active) Propager(c, humains, maintenant, date);
            if (c.Type == TypeCatastrophe.Cyclone && c.Active) Souffler(c, humains, maintenant);
            if (c.Type == TypeCatastrophe.Comete && c.Active && date.Heure == 22) Contempler(c, humains, maintenant, nuit: true);
            if (c.Type == TypeCatastrophe.Aurore && c.Active && date.Heure == 23) Contempler(c, humains, maintenant, nuit: true);
            if (c.Active && FinPrevue(c) is { } fin && maintenant >= fin) Terminer(c, humains, maintenant);
        }
        CalculerDangers();
        Exposer(humains, maintenant);
        Ciel(humains, maintenant, date);
        Contagion(humains, maintenant);
        CalculerClimat();
    }

    /// <summary>Date de fin prévue d'une catastrophe qui dure (null si elle finit autrement).</summary>
    private static long? FinPrevue(Catastrophe c) => c.Duree > 0 && c.Prochaine == 0 ? c.Debut + c.Duree : null;

    /// <summary>Une étape prévue : paroxysme d'une éruption, arrivée d'une vague, réplique d'un séisme, fin d'une crue subite.</summary>
    private void Etape(Catastrophe c, List<Humain> humains, long maintenant)
    {
        c.Prochaine = 0;
        switch (c.Type)
        {
            case TypeCatastrophe.Eruption: Paroxysme(c, humains, maintenant); break;
            case TypeCatastrophe.Tsunami: Deferler(c, humains, maintenant); break;
            case TypeCatastrophe.Seisme: Replique(c, humains, maintenant); break;
            case TypeCatastrophe.Crue:
                c.Phase = 1;
                break;
        }
    }

    /// <summary>Une catastrophe prend fin : le journal le note, avec le bilan pour chaque communauté touchée.</summary>
    private void Terminer(Catastrophe c, List<Humain> humains, long maintenant)
    {
        c.Fin = maintenant;
        c.Front.Clear();
        var texte = c.Type switch
        {
            TypeCatastrophe.Incendie => $"L'incendie {Lieu(c)} s'éteint après avoir brûlé {Surface(c.Cases.Count)}.",
            TypeCatastrophe.Crue => $"Les eaux de la crue {Lieu(c)} se retirent.",
            TypeCatastrophe.Cyclone => $"La tempête {Lieu(c)} s'apaise.",
            TypeCatastrophe.VagueDeFroid => "Le grand froid prend fin : la douceur revient.",
            TypeCatastrophe.Canicule => "La canicule prend fin.",
            TypeCatastrophe.Eruption => $"{Majuscule(VolcanDe(c).Nom)} se rendort.",
            TypeCatastrophe.HiverVolcanique => "Le ciel s'éclaircit : le voile de l'éruption s'est dissipé et la chaleur revient.",
            TypeCatastrophe.HiverDImpact => "Les poussières de l'impact sont enfin retombées : le soleil revient, la chaleur aussi, et les plantes repoussent.",
            TypeCatastrophe.Comete => "La comète disparaît du ciel.",
            TypeCatastrophe.Aurore => "Les lueurs du ciel s'éteignent.",
            TypeCatastrophe.Epidemie => $"Plus personne n'est malade de {NomPathogene(c.Parametre)} : l'épidémie est finie.",
            _ => null,
        };
        if (texte is not null) Noter(maintenant, null, texte + Bilan(c), c.Id);
        Pertes(c, maintenant);
        c.SuitesJusqua = DureeDesSuites(c, maintenant);
    }

    // ───────────── Suites ─────────────

    /// <summary>Dégâts (sur 10 000) en dessous desquels une terre a assez reverdi pour nourrir de nouveau ceux qui y vivent.</summary>
    private const int DegatsSensibles = 2_500;

    /// <summary>
    /// Fin des suites d'une catastrophe qui a ravagé la nature : le jour où la tuile qu'elle a le plus abîmée aura assez reverdi,
    /// au rythme propre à son terrain (0 si elle n'a rien ravagé : un séisme, un grand froid, une épidémie). Seuls comptent
    /// les dégâts qu'elle a causés elle-même, pas les cicatrices plus anciennes des terres qu'elle a traversées. Les tuiles
    /// qu'elle a gravement touchées deviennent ses cases : c'est là que l'on meurt de ses suites.
    /// </summary>
    private long DureeDesSuites(Catastrophe c, long maintenant)
    {
        if (TypesCatastrophe.EstPhenomene(c.Type) || c.Type is TypeCatastrophe.Epidemie or TypeCatastrophe.VagueDeFroid
                or TypeCatastrophe.Canicule or TypeCatastrophe.HiverVolcanique or TypeCatastrophe.HiverDImpact) return 0;
        double jours = 0;
        var graves = new List<int>();
        foreach (var (i, d) in c.Ravages)
        {
            // Ce qui a déjà guéri depuis le passage de la catastrophe ne compte plus.
            var reste = Math.Min(d, _ecosysteme.Degats[i]) - DegatsSensibles;
            if (reste <= 0) continue;
            graves.Add(i);
            jours = Math.Max(jours, reste / Math.Max(1, _ecosysteme.GuerisonParJour(i)));
        }
        if (c.Cases.Count == 0) c.Cases.UnionWith(graves);
        return jours <= 0 ? 0 : maintenant + (long)(jours * Calendrier.SecondesParJour);
    }

    /// <summary>
    /// Les suites d'une catastrophe prennent fin : la terre a reverdi. Le journal donne le bilan complet, morts sur le coup
    /// et morts des suites, et chaque communauté note ceux qu'elle a perdus depuis.
    /// </summary>
    private void FinDesSuites(long maintenant)
    {
        foreach (var c in _liste)
        {
            if (c.Active || c.SuitesJusqua <= 0 || c.SuitesJusqua > maintenant || c.SuitesJusqua <= maintenant - Calendrier.SecondesParJour) continue;
            var duree = Echelle.Duree(c.SuitesJusqua - (c.Fin ?? c.Debut));
            var bilan = new List<string>();
            if (c.Morts > 0) bilan.Add(c.Morts == 1 ? "1 mort sur le coup" : $"{c.Morts} morts sur le coup");
            if (c.Blesses > 0) bilan.Add(c.Blesses == 1 ? "1 blessé" : $"{c.Blesses} blessés");
            if (c.MortsSuites > 0) bilan.Add(c.MortsSuites == 1 ? "1 mort de faim, de froid ou de maladie dans les terres dévastées"
                : $"{c.MortsSuites} morts de faim, de froid ou de maladie dans les terres dévastées");
            Noter(maintenant, null, $"Les terres ravagées par {TypesCatastrophe.Nom(c.Type)} {Lieu(c)} ont reverdi, {duree} après. "
                + (bilan.Count == 0 ? "Personne n'en est mort." : $"Bilan : {string.Join(", ", bilan)}."), c.Id,
                significatif: c.Morts + c.MortsSuites > 0);
            Pertes(c, maintenant, suites: true);
        }
    }

    /// <summary>« (3 morts, 2 blessés) », ou rien.</summary>
    private static string Bilan(Catastrophe c)
    {
        if (c.Morts == 0 && c.Blesses == 0) return "";
        var morts = c.Morts switch { 0 => null, 1 => "1 mort", var n => $"{n} morts" };
        var blesses = c.Blesses switch { 0 => null, 1 => "1 blessé", var n => $"{n} blessés" };
        return $" Bilan : {string.Join(", ", new[] { morts, blesses }.Where(s => s is not null))}.";
    }

    /// <summary>Chaque communauté qui a perdu des siens le note dans son histoire.</summary>
    private void Pertes(Catastrophe c, long maintenant, bool suites = false)
    {
        foreach (var (communaute, morts) in c.PertesParCommunaute.OrderBy(p => p.Key))
        {
            if (morts == 0 || communaute <= 0 || communaute > Communautes.Count) continue;
            var nom = Communautes[communaute - 1].Nom;
            var quand = suites ? $"dans les suites {TypesCatastrophe.De(c.Type)}" : $"dans {TypesCatastrophe.Nom(c.Type)}";
            Noter(maintenant, null, $"La {Minuscule(nom)} a perdu {(morts == 1 ? "l'un des siens" : $"{morts} des siens")} {quand}.",
                c.Id, communaute);
        }
        c.PertesParCommunaute.Clear();
    }

    // ───────────── Effets sur les humains ─────────────

    /// <summary>
    /// Ceux qui restent sur une case mortelle risquent leur vie, chaque heure. La plupart ont fui à temps
    /// (voir <see cref="Comportement"/>) : ce sont surtout les endormis, les blessés et les égarés qui sont pris.
    /// </summary>
    private void Exposer(List<Humain> humains, long maintenant)
    {
        if (_danger.Count == 0) return;
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant) continue;
            if (!_danger.TryGetValue(_carte.Indice(h.X, h.Y), out var d) || d.Niveau < 2) continue;
            // La première heure, on entend le grondement, on voit la fumée : chacun a une chance de s'écarter avant d'être pris.
            if (d.Source.Debut >= maintenant) continue;
            var (mort, blessure, cause) = d.Source.Type switch
            {
                // Pris dans les flammes ou la fumée.
                TypeCatastrophe.Incendie => (0.2, 0.3, CauseDeMort.Brulure),
                // Emporté par la crue subite (la plupart des noyades ont lieu dans les premières heures).
                TypeCatastrophe.Crue => (0.05, 0.08, CauseDeMort.Noyade),
                TypeCatastrophe.Eruption => (0.15, 0.25, CauseDeMort.Eruption),
                _ => (0.0, 0.0, CauseDeMort.Aucune),
            };
            if (mort > 0) Frapper(h, d.Source, mort, blessure, cause, humains, maintenant);
        }
    }

    /// <summary>
    /// Un humain pris dans une catastrophe : il peut en mourir ou être blessé, selon sa robustesse et son âge
    /// (les jeunes enfants et les vieillards sont les plus touchés dans toutes les catastrophes ; OMS).
    /// </summary>
    private void Frapper(Humain h, Catastrophe c, double mort, double blessure, CauseDeMort cause, List<Humain> humains, long maintenant)
    {
        var age = h.AgeEnAnnees(maintenant);
        var fragilite = (age < 5 ? 1.8 : age > 60 ? 1.6 : 1) * (1.3 - 0.6 * h.Personnalite.Endurance) * (h.Sante < 50 ? 1.5 : 1);
        c.Temoins.Add(h.Id);
        if (_alea.Reel() < Math.Min(0.95, mort * fragilite))
        {
            Tuer(h, c, cause, humains, maintenant);
            return;
        }
        if (_alea.Reel() < Math.Min(0.95, blessure * fragilite)) Blesser(h, c, 15 + 35 * _alea.Reel(), humains, maintenant);
        Effrayer(h, c, 60, maintenant);
    }

    private void Tuer(Humain h, Catastrophe c, CauseDeMort cause, List<Humain> humains, long maintenant)
    {
        var e = h.Sexe == Sexe.Femme ? "e" : "";
        var comment = cause switch
        {
            CauseDeMort.Noyade => c.Type == TypeCatastrophe.Tsunami ? $"est emporté{e} par la vague géante" : $"se noie, emporté{e} par {TypesCatastrophe.Nom(c.Type)}",
            CauseDeMort.Brulure => c.Type == TypeCatastrophe.Impact ? "périt dans le souffle de feu tombé du ciel" : $"périt dans les flammes de l'incendie",
            CauseDeMort.Eboulement => "meurt écrasé" + e + " sous les rochers que le séisme a fait tomber",
            CauseDeMort.Eruption => "périt dans l'éruption " + VolcanDe(c).Nom[3..],
            CauseDeMort.Accident => $"meurt dans {TypesCatastrophe.Nom(c.Type)}",
            _ => $"meurt dans {TypesCatastrophe.Nom(c.Type)}",
        };
        if (Comportement is null) return;
        Comportement.Tuer(h, maintenant, cause, $"{h.Nom} {comment}, à {Comportement.AgeEnMots(h, maintenant)}.", c.Id);
        c.Morts++;
        if (h.CommunauteId > 0) c.PertesParCommunaute[h.CommunauteId] = c.PertesParCommunaute.GetValueOrDefault(h.CommunauteId) + 1;
        Toucher(c, h);
    }

    private void Blesser(Humain h, Catastrophe c, double gravite, List<Humain> humains, long maintenant)
    {
        h.Sante -= gravite;
        Medecine.Blesser(h, gravite, maintenant);
        if (h.Sante <= 0)
        {
            Tuer(h, c, c.Type switch
            {
                TypeCatastrophe.Seisme => CauseDeMort.Eboulement,
                TypeCatastrophe.Incendie or TypeCatastrophe.Impact => CauseDeMort.Brulure,
                TypeCatastrophe.Crue or TypeCatastrophe.Tsunami => CauseDeMort.Noyade,
                TypeCatastrophe.Eruption => CauseDeMort.Eruption,
                _ => CauseDeMort.Accident,
            }, humains, maintenant);
            return;
        }
        c.Blesses++;
        var e = h.Sexe == Sexe.Femme ? "e" : "";
        var comment = c.Type switch
        {
            TypeCatastrophe.Seisme => $"est blessé{e} par des pierres tombées pendant le séisme",
            TypeCatastrophe.Incendie => $"est brûlé{e} en fuyant l'incendie",
            TypeCatastrophe.Crue => $"réchappe de justesse à la crue, meurtri{e}",
            TypeCatastrophe.Tsunami => $"survit à la vague géante, blessé{e}",
            TypeCatastrophe.Eruption => $"est brûlé{e} par les cendres de l'éruption",
            TypeCatastrophe.Cyclone => $"est blessé{e} par une branche arrachée par la tempête",
            _ => $"est blessé{e} par {TypesCatastrophe.Nom(c.Type)}",
        };
        Noter(maintenant, h, $"{h.Nom} {comment}.", c.Id);
        Toucher(c, h);
    }

    /// <summary>La communauté de l'humain compte parmi celles que la catastrophe a touchées.</summary>
    private static void Toucher(Catastrophe c, Humain h)
    {
        if (h.CommunauteId > 0) c.Communautes.Add(h.CommunauteId);
    }

    /// <summary>
    /// La peur : plus forte chez les anxieux (névrosisme), elle pèse sur l'humeur et s'apaise avec les jours.
    /// Après une catastrophe, 10 à 30 % des personnes exposées gardent longtemps des troubles (Neria et al., 2008) :
    /// chez les plus sensibles, la peur s'éteint bien plus lentement.
    /// </summary>
    private void Effrayer(Humain h, Catastrophe c, double force, long maintenant)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return;
        var gain = force * (0.4 + 1.2 * h.Personnalite.Nevrosisme);
        if (gain < 1) return;
        var avant = h.Peur;
        h.Peur = Math.Min(100, Math.Max(h.Peur, gain) + 0.2 * Math.Min(h.Peur, gain));
        if (h.Peur >= avant + 1 || h.PeurEvenementId == 0)
        {
            // Ce qui terrifie et dépasse l'entendement fait chercher une main derrière le monde (Norenzayan et Hansen, 2006).
            if (h.PeurEvenementId != c.Id)
                Croyances.Ajouter(h, Math.Min(6, gain / 15) * (0.3 + h.Personnalite.Transcendance), $"{TypesCatastrophe.Nom(c.Type)} l'a effrayé{(h.Sexe == Sexe.Femme ? "e" : "")}",
                    c.Id, maintenant);
            h.PeurEvenementId = c.Id;
            h.PeurType = c.Type;
        }
        h.Humeur = Math.Max(0, h.Humeur - gain / 4);
        if (gain >= 60 && avant < 50)
        {
            var e = h.Sexe == Sexe.Femme ? "e" : "";
            Noter(maintenant, h, $"{h.Nom} est terrifié{e} par {TypesCatastrophe.Nom(c.Type)}.", c.Id, type: TypeEvenement.Humeur);
        }
        Toucher(c, h);
        // Une frayeur marquante laisse une croyance ou une question (une seule fois par catastrophe).
        if (gain >= 25 && !h.Memoire.Jardin.Exists(x => x.CauseId == c.Id && x.Type is TypeIntime.Croyance or TypeIntime.Question))
            Jardin.Interpreter(h, c.Type, c.Id, h.Peur, _alea, maintenant);
    }

    /// <summary>
    /// Un phénomène du ciel : selon son tempérament, on en a peur ou on s'en émerveille. Les esprits contemplatifs
    /// (transcendance) y voient quelque chose de grand ; les anxieux et les esprits fermés, une menace.
    /// Ces souvenirs pourront nourrir les premières croyances.
    /// </summary>
    private void Impressionner(Humain h, Catastrophe c, long maintenant)
    {
        if (!h.EstVivant || h.EstBebe(maintenant) || !c.Temoins.Add(h.Id)) return;
        var p = h.Personnalite;
        var peur = Math.Clamp(0.15 + 0.5 * p.Nevrosisme - 0.3 * p.Transcendance - 0.15 * p.Ouverture, 0.02, 0.85);
        Toucher(c, h);
        if (_alea.Reel() < peur)
        {
            Effrayer(h, c, c.Type == TypeCatastrophe.Bolide ? 20 : 35, maintenant);
            return;
        }
        var force = 3 + 10 * p.Transcendance;
        VieSociale.Rejouir(h, force);
        if (p.Transcendance > 0.7 || (p.Transcendance > 0.55 && p.Ouverture > 0.7))
        {
            var e = h.Sexe == Sexe.Femme ? "e" : "";
            var quoi = c.Type switch
            {
                TypeCatastrophe.Eclipse => "le soleil noirci, saisi" + e + " par ce mystère",
                TypeCatastrophe.Comete => "l'étoile chevelue, et ne peut en détacher les yeux",
                TypeCatastrophe.Bolide => "la traînée de feu dans la nuit, émerveillé" + e,
                TypeCatastrophe.Aurore => "les voiles de lumière qui dansent dans le ciel",
                _ => TypesCatastrophe.Nom(c.Type),
            };
            Noter(maintenant, h, $"{h.Nom} contemple {quoi}.", c.Id, type: TypeEvenement.Humeur);
            Jardin.Interpreter(h, c.Type, c.Id, 50, _alea, maintenant);
        }
    }

    /// <summary>Une fois par jour, la peur s'apaise : en quelques jours pour la plupart, en des semaines pour les plus sensibles.</summary>
    private static void Apaiser(List<Humain> humains)
    {
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Peur <= 0) continue;
            var demiVie = 4 * Math.Exp(3.5 * (h.Personnalite.Nevrosisme - 0.5)) * (1 + h.Peur / 50);
            h.Peur *= Math.Pow(0.5, 1 / demiVie);
            if (h.Peur < 1)
            {
                h.Peur = 0;
                h.PeurEvenementId = 0;
            }
        }
    }

    /// <summary>
    /// Mort « ordinaire » qui doit pourtant quelque chose à une catastrophe en cours (le froid pendant une vague de froid
    /// ou un hiver volcanique, la chaleur pendant une canicule, la maladie quand on porte un mal contagieux) :
    /// renvoie l'événement à citer comme cause (0 : aucun), et compte la victime dans le bilan de la catastrophe.
    /// </summary>
    internal (long Id, string? Contexte) Attribuer(Humain h, CauseDeMort cause, long maintenant)
    {
        var type = cause switch
        {
            CauseDeMort.Maladie when h.InfectionId != 0 =>
                _liste.FirstOrDefault(c => c.Active && c.Type == TypeCatastrophe.Epidemie && c.Parametre == h.InfectionId),
            CauseDeMort.Froid => _liste.FirstOrDefault(c => c.Active && c.Type is TypeCatastrophe.VagueDeFroid or TypeCatastrophe.HiverVolcanique
                or TypeCatastrophe.HiverDImpact),
            CauseDeMort.Chaleur => _liste.FirstOrDefault(c => c.Active && c.Type == TypeCatastrophe.Canicule),
            // Les plantes ne poussent plus sous le ciel voilé : la faim vient de l'hiver qui dure.
            CauseDeMort.Faim => _liste.FirstOrDefault(c => c.Active && c.Type is TypeCatastrophe.HiverVolcanique or TypeCatastrophe.HiverDImpact),
            _ => null,
        };
        if (cause == CauseDeMort.Maladie && h.InfectionId != 0)
        {
            if (type is not null) Compter(type, h);
            return (h.InfectionEvenementId, null);
        }
        if (type is not null)
        {
            Compter(type, h);
            return (type.Id, $"pendant {TypesCatastrophe.Nom(type.Type)}");
        }
        // Dans une terre que la catastrophe a dévastée et qui n'a pas encore reverdi, la faim, la soif, le froid et les maladies
        // tuent plus qu'ailleurs : ces morts sont celles de ses suites.
        if (cause is not (CauseDeMort.Faim or CauseDeMort.Soif or CauseDeMort.Froid or CauseDeMort.Epuisement or CauseDeMort.Maladie)) return (0, null);
        if (DegatsAutour(h.X, h.Y) < DegatsSensibles) return (0, null);
        var suites = _liste.Where(c => c.EnSuites(maintenant) && DansLaZone(c, h.X, h.Y)).MaxBy(c => c.Id);
        if (suites is null) return (0, null);
        suites.MortsSuites++;
        suites.Temoins.Add(h.Id);
        Toucher(suites, h);
        if (h.CommunauteId > 0) suites.PertesParCommunaute[h.CommunauteId] = suites.PertesParCommunaute.GetValueOrDefault(h.CommunauteId) + 1;
        return (suites.Id, $"dans les terres dévastées par {TypesCatastrophe.Nom(suites.Type)}");
    }

    /// <summary>Dégâts les plus forts sur la case et ses voisines (là où l'on cherche sa nourriture).</summary>
    private int DegatsAutour(int x, int y)
    {
        var max = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < _carte.Largeur && ny < _carte.Hauteur) max = Math.Max(max, _ecosysteme.Degats[_carte.Indice(nx, ny)]);
            }
        }
        return max;
    }

    private bool DansLaZone(Catastrophe c, int x, int y)
    {
        if (c.Cases.Count == 0) return Distance(x, y, c.X, c.Y) <= c.Rayon + 1;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < _carte.Largeur && ny < _carte.Hauteur && c.Cases.Contains(_carte.Indice(nx, ny))) return true;
            }
        }
        return false;
    }

    private static void Compter(Catastrophe type, Humain h)
    {
        type.Morts++;
        type.Temoins.Add(h.Id);
        Toucher(type, h);
        if (h.CommunauteId > 0) type.PertesParCommunaute[h.CommunauteId] = type.PertesParCommunaute.GetValueOrDefault(h.CommunauteId) + 1;
    }

    // ───────────── Journal et noms ─────────────

    private long Noter(long maintenant, Humain? h, string texte, long cause = 0, int communaute = 0, int autreCommunaute = 0,
        TypeEvenement type = TypeEvenement.Catastrophe, Origine origine = Origine.Naturelle, bool significatif = false)
    {
        var e = new EvenementJournal(maintenant, type, h?.Id ?? 0, 0, texte, origine)
        {
            Id = Numeros.Suivant(), CauseId = cause, CommunauteId = communaute, AutreCommunauteId = autreCommunaute,
            Significatif = significatif,
        };
        Journal.Add(e);
        return e.Id;
    }

    /// <summary>
    /// Une catastrophe commence : le journal l'annonce, et l'observateur est alerté si des humains sont dans la zone
    /// (ou pour un phénomène du ciel que tous peuvent voir).
    /// </summary>
    private Catastrophe Commencer(TypeCatastrophe type, int x, int y, int rayon, double intensite, string nom, string texte,
        List<Humain> humains, long maintenant, Origine origine = Origine.Naturelle, double direction = 0, int parametre = 0, long cause = 0, long duree = 0, bool alerter = true)
    {
        // Significative si elle menace des humains, ou si tous peuvent la voir (phénomène du ciel).
        var menace = humains.Vivants().Any(h => h.EstVivant && Distance(h.X, h.Y, x, y) <= rayon + 3);
        var phenomene = TypesCatastrophe.EstPhenomene(type);
        var id = Noter(maintenant, null, texte, cause, origine: origine, significatif: phenomene || menace);
        var c = new Catastrophe
        {
            Id = id, Type = type, Origine = origine, Debut = maintenant, X = x, Y = y, Rayon = rayon, Intensite = intensite,
            Nom = nom, Direction = direction, Parametre = parametre, Duree = duree,
        };
        _liste.Add(c);
        if (phenomene) Alertes.Add(new AlerteCatastrophe(id, type, texte, Grave: false));
        else if (alerter && menace) Signaler(c, texte);
        return c;
    }

    private void Signaler(Catastrophe c, string texte)
    {
        if (c.Signalee) return;
        c.Signalee = true;
        Alertes.Add(new AlerteCatastrophe(c.Id, c.Type, texte, Grave: true));
    }

    private string Lieu(Catastrophe c) => LieuDe(c.X, c.Y);

    /// <summary>« du nord-ouest », « de l'est », « du centre du continent ».</summary>
    private string LieuDe(int x, int y) => Lieux.Direction(_carte, x, y) switch
    {
        "au centre" => "du centre du continent",
        var d when d.StartsWith("à l'", StringComparison.Ordinal) => "de l'" + d[4..],
        var d => "du " + d[3..],
    };

    /// <summary>« au nord-ouest du continent ».</summary>
    private string Ou(int x, int y) => Lieux.Direction(_carte, x, y) + " du continent";

    /// <summary>Surface en km² d'un nombre de cases, en mots.</summary>
    public static string Surface(int cases) => Echelle.Surface(cases);

    private Volcan VolcanDe(Catastrophe c) =>
        Geologie.Volcans.FirstOrDefault(v => v.Numero == c.Parametre) ?? new Volcan(0, c.X, c.Y, 100, "le volcan");

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Minuscule(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>Le monde tire une valeur dans une loi log-normale (médiane, dispersion du logarithme).</summary>
    private double LogNormale(double mediane, double sigma) => mediane * Math.Exp(sigma * Biologie.Normale(_alea));

    /// <summary>Une case de terre ferme tirée au hasard dans un rectangle, ou −1 après quelques essais infructueux.</summary>
    private int TerreAuHasard(int x0, int y0, int x1, int y1, Func<Biome, bool> convient)
    {
        x0 = Math.Clamp(x0, 0, _carte.Largeur - 1);
        x1 = Math.Clamp(x1, 0, _carte.Largeur - 1);
        y0 = Math.Clamp(y0, 0, _carte.Hauteur - 1);
        y1 = Math.Clamp(y1, 0, _carte.Hauteur - 1);
        for (var essai = 0; essai < 30; essai++)
        {
            var x = x0 + _alea.Entier(x1 - x0 + 1);
            var y = y0 + _alea.Entier(y1 - y0 + 1);
            if (convient(_carte.BiomeEn(x, y))) return _carte.Indice(x, y);
        }
        return -1;
    }
}
