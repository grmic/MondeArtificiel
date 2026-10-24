using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// La nature vivante : ressources actuelles de chaque tuile, en centièmes (0 à 10 000), entières pour rester
/// exactement reproductibles, et pluies de l'année dans chaque région.
/// <para>
/// Les plantes repoussent vers un niveau qui dépend de la saison et des pluies ; trop cueillies, elles repartent
/// plus lentement. Le gibier est une population vivante : il se reproduit selon une croissance logistique
/// (Verhulst), d'autant moins vite qu'il reste peu de bêtes, s'éteint localement si on le chasse trop
/// (effet Allee), et revient peu à peu depuis les terres voisines (les bêtes se déplacent vers les zones moins peuplées).
/// </para>
/// </summary>
public sealed class Ecosysteme
{
    /// <summary>Côté d'une région climatique, en cases : les pluies varient d'une région à l'autre.</summary>
    public const int TailleRegion = 32;

    /// <summary>
    /// Croissance maximale du gibier : environ 35 % par an pour de grands herbivores de 50 à 100 kg
    /// (r_max ≈ 1,5 × masse^−0,36 ; Caughley et Krebs, 1983).
    /// </summary>
    private const double CroissanceGibierParAn = 0.35;

    /// <summary>Part de l'écart de densité qui passe chaque jour d'une case à chacune de ses voisines (dispersion des bêtes).</summary>
    private const double DispersionGibier = 0.02;

    /// <summary>Sous 5 % de son niveau naturel, le gibier ne se reproduit plus sur place (trop peu de bêtes : effet Allee).</summary>
    private const double SeuilAllee = 0.05;

    /// <summary>Repousse des plantes : environ 5 % de l'écart avec leur niveau du moment par jour, au printemps.</summary>
    private const double RepoussePlantesParJour = 0.05;

    /// <summary>
    /// Persistance des pluies d'une année sur l'autre et dispersion autour de la normale (écart-type du logarithme).
    /// Les pluies annuelles varient de 20 à 30 % d'une année à l'autre sous les climats tempérés et tropicaux secs.
    /// </summary>
    private const double PersistancePluies = 0.4;
    private const double DispersionPluies = 0.25;

    /// <summary>Abondance des plantes selon la saison (printemps, été, automne, hiver) : l'hiver est maigre.</summary>
    private static readonly double[] PlantesSelonSaison = [1.0, 1.0, 0.8, 0.45];

    private readonly Carte _carte;
    private readonly int _regionsX;
    private readonly int _regionsY;

    public Ecosysteme(Carte carte, ushort[]? gibier = null, ushort[]? plantes = null, double[]? pluies = null, ushort[]? degats = null)
    {
        _carte = carte;
        _regionsX = (carte.Largeur + TailleRegion - 1) / TailleRegion;
        _regionsY = (carte.Hauteur + TailleRegion - 1) / TailleRegion;
        // Un monde neuf part de ce que porte le sol de chaque case ; un monde ancien y converge saison après saison.
        Gibier = gibier ?? carte.Gibier.Select((v, i) => Arrondir(v * 100 * (0.6 + 0.4 * Sol(i)))).ToArray();
        Plantes = plantes ?? carte.Plantes.Select((v, i) => Arrondir(v * 100 * Sol(i))).ToArray();
        Pluies = pluies ?? Enumerable.Repeat(1.0, _regionsX * _regionsY).ToArray();
        Degats = degats ?? new ushort[carte.Plantes.Length];
        if (Gibier.Length != carte.Gibier.Length || Plantes.Length != carte.Plantes.Length || Pluies.Length != _regionsX * _regionsY
            || Degats.Length != carte.Plantes.Length)
        {
            throw new ArgumentException("Les ressources sauvegardées ne correspondent pas à la taille de la carte.");
        }
        if (Pluies.Any(p => !double.IsFinite(p) || p <= 0))
        {
            throw new ArgumentException("Pluies sauvegardées invalides.");
        }
    }

    public ushort[] Gibier { get; }
    public ushort[] Plantes { get; }

    /// <summary>
    /// Dégâts laissés par les catastrophes sur chaque tuile, en centièmes de pour cent (10 000 : terre dévastée) :
    /// cendres, terre brûlée, sol arraché. La végétation et le gibier n'y reviennent qu'à mesure que la terre guérit.
    /// </summary>
    public ushort[] Degats { get; }

    /// <summary>
    /// Effet du climat du moment sur la végétation de tout le continent (1 : normal ; moins après une grande éruption,
    /// quand les cendres voilent le soleil : « hiver volcanique »).
    /// </summary>
    public double FacteurClimat { get; set; } = 1;

    /// <summary>
    /// Années qu'il faut à une terre ravagée (brûlée, noyée, ensevelie sous la cendre) pour retrouver ses plantes et son gibier,
    /// selon le terrain : un ou deux ans pour les fougères et les marais, quelques années pour la forêt tropicale qui repousse
    /// vite, bien plus pour la forêt de conifères, la montagne et le désert, où tout pousse lentement (ordres de grandeur des
    /// successions après les feux de Yellowstone en 1988, Turner et al. 2003, et après l'éruption du mont Saint Helens en 1980,
    /// del Moral et Wood 1993).
    /// </summary>
    public static double AnneesDeRetour(Biome b) => b switch
    {
        Biome.PlaineDeFougeres or Biome.Marais or Biome.Plage => 1.5,
        Biome.ForetTropicale => 3,
        Biome.Lande => 3,
        Biome.ForetDeFeuillus => 5,
        Biome.Mangrove => 6,
        Biome.ForetDeConiferes => 8,
        Biome.Desert or Biome.Montagne or Biome.SommetEnneige => 10,
        _ => 2,
    };

    /// <summary>
    /// Dégâts effacés en un jour sur une case (sur 10 000). Une terre stérilisée (lave, nuées brûlantes, cratère) se
    /// recolonise bien plus lentement qu'une terre simplement brûlée : il y faut des décennies.
    /// </summary>
    public double GuerisonParJour(int i)
    {
        var vitesse = 10_000 / (AnneesDeRetour(_carte.BiomeEn(i % _carte.Largeur, i / _carte.Largeur)) * Calendrier.JoursParAn);
        var sterile = Math.Clamp((Degats[i] / 10_000.0 - 0.8) / 0.2, 0, 1);
        return vitesse * (1 - 0.85 * sterile);
    }

    /// <summary>Pluies de l'année par région (1 : année normale ; 0,6 : sécheresse ; 1,4 : année très arrosée).</summary>
    public double[] Pluies { get; }

    public int RegionsX => _regionsX;
    public int RegionsY => _regionsY;

    public int Region(int x, int y) => y / TailleRegion * _regionsX + x / TailleRegion;

    public double PluieEn(int indice) => Pluies[Region(indice % _carte.Largeur, indice / _carte.Largeur)];

    /// <summary>Effet des pluies sur la végétation : proportionnel, borné (un sol garde de quoi survivre, un excès d'eau ne sert plus).</summary>
    private static double EffetPluie(double pluie) => Math.Clamp(pluie, 0.3, 1.3);

    /// <summary>Niveau des plantes vers lequel la tuile repousse aujourd'hui (centièmes).</summary>
    public double NiveauPlantes(int i, double saison) =>
        _carte.Plantes[i] * 100 * saison * EffetPluie(PluieEn(i)) * (1 - Degats[i] / 10_000.0) * FacteurClimat * Epargne(i) * Sol(i);

    /// <summary>Capacité d'accueil du gibier sur la tuile cette année (centièmes) : il dépend moins des pluies que les plantes.</summary>
    public double NiveauGibier(int i) => _carte.Gibier[i] * 100 * (0.7 + 0.3 * EffetPluie(PluieEn(i))) * (1 - Degats[i] / 20_000.0) * Epargne(i) * (0.6 + 0.4 * Sol(i));

    // Ce que le sol change aux plantes de la case (voir Terrain.FacteurSol) ; le gibier, qui broute, en dépend moins.
    private float[]? _sol;

    private double Sol(int i) => (_sol ??= _carte.Terrain.FacteurSol)[i];

    /// <summary>
    /// Famine envoyée par l'Entité : part des plantes et du gibier perdue sur chaque tuile (null : aucune). Fixé chaque jour
    /// par le monde d'après les famines en cours.
    /// </summary>
    public double[]? Fleau { get; set; }

    private double Epargne(int i) => Fleau is null ? 1 : 1 - Fleau[i];

    /// <summary>Saison des plantes (0,45 à 1), interpolée pour éviter les à-coups entre saisons.</summary>
    public static double FacteurSaison(long secondes)
    {
        var jour = secondes % Calendrier.SecondesParAn / (double)Calendrier.SecondesParJour;
        var position = (jour - Calendrier.JoursParSaison / 2.0) / Calendrier.JoursParSaison;
        if (position < 0) position += 4;
        var s = (int)position % 4;
        var t = position - Math.Floor(position);
        return PlantesSelonSaison[s] + (PlantesSelonSaison[(s + 1) % 4] - PlantesSelonSaison[s]) * t;
    }

    /// <summary>Une journée de la nature : les plantes repoussent ou se fanent, le gibier se reproduit et se disperse.</summary>
    public void Journee(long maintenant)
    {
        var saison = FacteurSaison(maintenant);
        var croissanceDuJour = CroissanceGibierParAn / Calendrier.JoursParAn;
        for (var i = 0; i < Plantes.Length; i++)
        {
            if (Degats[i] > 0) Degats[i] = (ushort)Math.Max(0, Degats[i] - Math.Max(1, (int)Math.Round(GuerisonParJour(i))));
            if (_carte.Plantes[i] == 0 && _carte.Gibier[i] == 0) continue;

            // Plantes : la repousse suit la saison ; une tuile dépouillée (moins de 20 % de son niveau) repart plus lentement.
            var niveau = NiveauPlantes(i, saison);
            double p = Plantes[i];
            if (p < niveau)
            {
                var vigueur = 0.5 + 0.5 * Math.Min(1, p / Math.Max(1, 0.2 * niveau));
                p += (niveau - p) * RepoussePlantesParJour * saison * vigueur;
            }
            else
            {
                p -= (p - niveau) * 0.03; // fruits et feuilles qui tombent quand la saison tourne
            }
            Plantes[i] = Arrondir(p);

            // Gibier : croissance logistique, nulle sous le seuil d'Allee.
            var capacite = NiveauGibier(i);
            double g = Gibier[i];
            if (capacite > 0 && g >= SeuilAllee * capacite) g += croissanceDuJour * EffetPluie(PluieEn(i)) * g * (1 - g / capacite);
            else if (g > capacite) g = capacite;
            Gibier[i] = Arrondir(g);
        }
        Disperser();
    }

    /// <summary>Plus haut niveau d'une ressource (centièmes) : une case pleine (100) sur le meilleur sol (×1,8 au plus).</summary>
    internal const int Plafond = 18_000;

    private static ushort Arrondir(double v) => (ushort)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), 0, Plafond);

    /// <summary>
    /// Les bêtes passent des cases où elles sont nombreuses (relativement à ce que la case peut nourrir) vers les voisines
    /// moins peuplées. Calculé d'un bloc pour que l'ordre de parcours ne change rien.
    /// </summary>
    private void Disperser()
    {
        var largeur = _carte.Largeur;
        var flux = new int[Gibier.Length];
        for (var i = 0; i < Gibier.Length; i++)
        {
            if (_carte.Gibier[i] == 0) continue;
            var x = i % largeur;
            // Vers la droite et vers le bas seulement : chaque paire de voisines n'est traitée qu'une fois.
            if (x + 1 < largeur) Echanger(i, i + 1, flux);
            if (i + largeur < Gibier.Length) Echanger(i, i + largeur, flux);
        }
        for (var i = 0; i < Gibier.Length; i++) Gibier[i] = (ushort)Math.Clamp(Gibier[i] + flux[i], 0, Plafond);
    }

    private void Echanger(int a, int b, int[] flux)
    {
        if (_carte.Gibier[b] == 0) return;
        // Densité relative : part de la capacité naturelle occupée.
        var da = Gibier[a] / (double)_carte.Gibier[a];
        var db = Gibier[b] / (double)_carte.Gibier[b];
        // Le flux suit l'écart de densité ; la capacité de la plus petite des deux cases le limite.
        var n = (int)((da - db) * Math.Min(_carte.Gibier[a], _carte.Gibier[b]) * DispersionGibier);
        flux[a] -= n;
        flux[b] += n;
    }

    /// <summary>
    /// Nouvelle année : les pluies de chaque région sont tirées. Elles gardent une part de l'année passée
    /// (les sécheresses durent parfois plusieurs années) et une part commune à tout le continent.
    /// </summary>
    public void NouvelleAnnee(Alea alea)
    {
        var commun = Biologie.Normale(alea);
        for (var r = 0; r < Pluies.Length; r++)
        {
            var propre = Biologie.Normale(alea);
            var ecart = DispersionPluies * (0.5 * commun + 0.866 * propre);
            Pluies[r] = Math.Clamp(Math.Exp(PersistancePluies * Math.Log(Pluies[r]) + ecart), 0.35, 2.0);
        }
    }

    /// <summary>
    /// Abondance autour d'un point : ressources actuelles rapportées à leur niveau naturel (1 : intact, 0 : tout est épuisé).
    /// </summary>
    public double Abondance(int cx, int cy, int rayon)
    {
        double actuel = 0, naturel = 0;
        for (var y = Math.Max(0, cy - rayon); y <= Math.Min(_carte.Hauteur - 1, cy + rayon); y++)
        {
            for (var x = Math.Max(0, cx - rayon); x <= Math.Min(_carte.Largeur - 1, cx + rayon); x++)
            {
                var i = _carte.Indice(x, y);
                actuel += Plantes[i] + Gibier[i];
                naturel += (_carte.Plantes[i] + _carte.Gibier[i]) * 100;
            }
        }
        return naturel == 0 ? 0 : actuel / naturel;
    }

    /// <summary>
    /// Abondance de chaque tuile (0 à 255) rapportée à ce que la saison et les pluies permettent aujourd'hui
    /// (128 : la tuile est à son niveau du moment), pour la carte des ressources : l'hiver seul n'y paraît pas épuisé.
    /// </summary>
    public byte[] CarteAbondance(long maintenant)
    {
        var saison = FacteurSaison(maintenant);
        var resultat = new byte[Plantes.Length];
        for (var i = 0; i < resultat.Length; i++)
        {
            var potentiel = NiveauPlantes(i, saison) + NiveauGibier(i);
            resultat[i] = potentiel <= 0 ? (byte)0 : (byte)Math.Clamp(128.0 * (Plantes[i] + Gibier[i]) / potentiel, 1, 255);
        }
        return resultat;
    }

    /// <summary>
    /// Prélève une quantité (en unités 0 à 100) et renvoie ce qui a vraiment été pris. Pendant un pas simultané, chacun voit
    /// la case telle qu'elle était au début du pas ; les prélèvements sont retirés ensuite (sans descendre sous zéro).
    /// </summary>
    public static int Prelever(ushort[] ressource, int indice, int quantite)
    {
        var pris = Math.Min(ressource[indice], quantite * 100);
        Tour.Differer(() => ressource[indice] -= (ushort)Math.Min(ressource[indice], pris));
        return pris / 100;
    }
}
