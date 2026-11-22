using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Simulation.Humains;

/// <summary>
/// La neige au sol pour les humains et les bêtes (0.45.26) : elle freine la marche, d'autant plus qu'on y enfonce et qu'on a les
/// jambes courtes ; elle gèle les pieds nus ; elle refroidit le corps mal couvert qui s'y tient ou s'y couche. Rien n'y oblige
/// personne : chacun la subit selon son corps, ce qu'il porte et ce qu'il fait, et peut en tirer l'idée de se couvrir ou de se
/// chausser (voir <see cref="Ingenium"/>).
/// <para>
/// Ce que dit la recherche. Le pied s'enfonce presque jusqu'au sol dans la neige fraîche (60 à 120 kg/m³), bien moins dans une
/// neige tassée ou regelée (300 à 450 kg/m³). L'effort de la marche croît vite avec l'enfoncement (coefficient de terrain de 1,3
/// dans une neige peu profonde à plus de 4 quand on enfonce au-dessus du genou : Pandolf et al., 1976 ; Richmond et al., 2015) ;
/// on marche alors à 3 km/h dans 20 cm de neige fraîche, vers 1,5 à 2 km/h quand elle arrive au genou, à peine 1 km/h à mi-cuisse.
/// Pour les ongulés, la neige gêne quand elle atteint la moitié de la hauteur du poitrail et devient critique vers les trois
/// quarts (Telfer et Kelsall, 1984) ; les bêtes à larges pattes et à faible charge (canidés, félins à pattes velues) enfoncent
/// moins que les bêtes à sabots, et le chasseur qui va mieux que sa proie dans la neige profonde la rattrape. Un pied nu sur la
/// neige perd sa chaleur en quelques minutes, s'engourdit et risque la gelure en moins d'une heure sous quelques degrés de gel ;
/// une peau mouillée de neige perd sa chaleur bien plus vite qu'à l'air sec (conduction par l'eau).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>La neige au sol (fournie par le monde) ; null : aucune.</summary>
    public Neige? Neige { get; set; }

    /// <summary>Température de l'air sur une case à un instant (°C), celle que ressentent les humains (vagues de froid et décrets compris).</summary>
    public double TemperatureEn(int x, int y, long secondes) => Temperature(x, y, secondes);

    /// <summary>
    /// Taille debout (cm) : adulte autour de 167 cm pour un homme et 156 cm pour une femme (squelettes du Paléolithique supérieur et
    /// du Néolithique, écart-type de 6 à 7 cm), et part de la taille adulte atteinte avec l'âge (courbes de croissance de l'OMS :
    /// environ 30 % à la naissance, 45 % à un an, 65 % à cinq ans, 83 % à dix ans).
    /// </summary>
    internal static double Stature(Humain h, long maintenant)
    {
        var adulte = h.Sexe == Sexe.Homme ? 167 + 7 * h.Apparence.Taille : 156 + 6.5 * h.Apparence.Taille;
        var age = h.AgeEnAnnees(maintenant);
        (double Age, double Part)[] croissance = [(0, 0.30), (1, 0.45), (2, 0.51), (5, 0.65), (10, 0.83), (14, h.Sexe == Sexe.Homme ? 0.93 : 0.98), (18, 1)];
        if (age >= 18) return adulte;
        for (var k = 1; k < croissance.Length; k++)
        {
            if (age > croissance[k].Age) continue;
            var (a0, p0) = croissance[k - 1];
            var (a1, p1) = croissance[k];
            return adulte * (p0 + (p1 - p0) * (age - a0) / (a1 - a0));
        }
        return adulte;
    }

    /// <summary>
    /// Part de l'épaisseur où s'enfonce un pied d'humain (charge d'environ 20 à 30 kPa) : presque toute dans la neige fraîche,
    /// moins de la moitié dans une neige tassée, peu dans une neige regelée.
    /// </summary>
    internal static double Enfoncement(double densite) => Math.Clamp(1.1 - 0.0021 * densite, 0.15, 0.9);

    /// <summary>Gêne de la marche selon l'enfoncement rapporté à la hauteur des jambes (1 : aucune neige ; vers 0,3 au genou).</summary>
    internal static double FacteurEnfoncement(double enfoncement, double jambe) =>
        enfoncement <= 0 || jambe <= 0 ? 1 : 1 / (1 + 2 * Math.Pow(enfoncement / jambe, 1.3));

    /// <summary>Neige au sol sur la case (cm) ; 0 sans neige.</summary>
    internal double NeigeSur(int i) => Neige is { } n ? n.EpaisseurEn(i) : 0;

    /// <summary>
    /// Ce que la neige laisse de la vitesse de marche de cet humain sur la case (1 : rien ne change). Le genou est à environ 28,5 %
    /// de la taille debout : un enfant enfonce jusqu'à mi-cuisse là où un adulte enfonce au genou.
    /// </summary>
    public double FacteurNeige(Humain h, int i, long maintenant)
    {
        if (Neige is not { } n || n.EpaisseurEn(i) < 0.5) return 1;
        var enfonce = n.EpaisseurEn(i) * Enfoncement(n.DensiteEn(i));
        return FacteurEnfoncement(enfonce, 0.285 * Stature(h, maintenant));
    }

    private double FacteurNeige(Humain h, int i) => FacteurNeige(h, i, _maintenant);

    /// <summary>Effort pour entrer dans une case voisine (mètres « de terrain plat »), neige comprise.</summary>
    internal double EffortDe(Humain h, int x, int y, int nx, int ny, long maintenant) =>
        Marche.Effort(carte, x, y, nx, ny) / FacteurNeige(h, carte.Indice(nx, ny), maintenant);

    /// <summary>
    /// Ce que la neige laisse de l'allure d'une bête (1 : rien ne change). Hauteur du poitrail tirée du poids (environ 0,17 m × kg^⅓ :
    /// 0,7 m pour un cerf de 70 kg, 0,35 m pour une bête de 10 kg, près de 3 m pour un géant de 5 t) ; les oiseaux coureurs ont les
    /// pattes longues. Les sabots portent beaucoup sur peu de surface et enfoncent davantage ; les larges pattes des rôdeurs et des
    /// félins, moins.
    /// </summary>
    public static double MobiliteBete(Bete b, double epaisseur, double densite)
    {
        if (epaisseur < 0.5) return 1;
        var poitrail = 17 * Math.Cbrt(Math.Max(0.5, b.Poids)) * (b.Forme == 6 ? 1.3 : 1);
        var charge = b.Forme switch { 0 or 1 or 2 => 1.25, 3 => 1.1, 4 => 0.8, 5 => 0.7, 6 => 0.9, _ => 1.0 };
        var enfonce = epaisseur * Math.Clamp(Enfoncement(densite) * charge, 0.1, 1);
        return 1 / (1 + 2.5 * Math.Pow(enfonce / poitrail, 1.5));
    }

    /// <summary>Allure d'une bête sur la case (1 : rien ne change).</summary>
    internal double MobiliteBeteSur(Bete b, int i) => Neige is { } n ? MobiliteBete(b, n.EpaisseurEn(i), n.DensiteEn(i)) : 1;

    /// <summary>
    /// À la chasse dans la neige : celui qui va mieux que sa proie la rattrape (l'élan enlisé dans la neige profonde), celui qui
    /// enfonce plus qu'elle la perd. Sans neige, rien ne change.
    /// </summary>
    private double ChasseDansLaNeige(Humain h, int i)
    {
        if (NeigeSur(i) < 0.5 || Faune.VueSur(carte.BiomeEn(i % carte.Largeur, i / carte.Largeur), i % carte.Largeur, i / carte.Largeur) is not { } proie)
            return 1;
        return Math.Clamp(FacteurNeige(h, i) / MobiliteBeteSur(proie, i), 0.3, 3);
    }

    /// <summary>Hors de tout abri bâti ou creusé : on a les pieds dans la neige.</summary>
    private bool PiedsDansLaNeige(Humain h, out double epaisseur)
    {
        epaisseur = NeigeSur(carte.Indice(h.X, h.Y));
        if (epaisseur < 0.5 || h.Mer.EnMer) return false;
        if (Installations.AbriEn(h.X, h.Y) > 0 && !AuPas(h)) return false;
        return Climat.Abri(carte.Terrain.GrotteA(h.X, h.Y)) < 0.5 || AuPas(h);
    }

    /// <summary>
    /// Ce que la neige retire à la température ressentie (°C) par l'humain qui s'y tient : de la neige qui fond sur la peau et sur
    /// ce qu'il porte, et le sol froid sous lui ; davantage couché ou assis dans la neige, à peine s'il est bien couvert.
    /// </summary>
    private double FroidDeLaNeige(Humain h, long maintenant)
    {
        if (!PiedsDansLaNeige(h, out var epaisseur)) return 0;
        var contact = Math.Min(1, epaisseur / 5);
        var couche = h.Activite is Activite.Dormir or Activite.Repos or Activite.Veiller or Activite.SAbriter ? 1.6 : 1;
        // Assis ou couché sur une litière (0.45.32), on ne touche presque plus la neige.
        if (couche > 1) contact *= 1 - 0.85 * LitiereSous(h, maintenant);
        return contact * couche * (3 + 2 * (1 - h.Personnalite.Endurance)) * (1 - 0.7 * Couverture(h));
    }

    /// <summary>Ce que des chaussures retiennent de la neige : une peau bien liée beaucoup, une natte de fibres un peu.</summary>
    private static double ContreLaNeige(Humain h)
    {
        if (h.Chaussures is not { } c) return 0;
        var matiere = Facons.LireObjet(c.Pieces) is { } x ? x.Facture.Principale : Matiere.Aucune;
        return Facons.Valeur(Fonction.Chausser, c) * (matiere == Matiere.Peau ? 1 : matiere is Matiere.Fibres or Matiere.Corde ? 0.6 : 0.5);
    }

    /// <summary>
    /// Pieds dans la neige : sans rien aux pieds, ils s'engourdissent, se meurtrissent et gèlent, d'autant plus vite qu'il gèle fort
    /// et qu'on marche ; la corne protège à peine ; le corps se refroidit par les pieds. Appelé à chaque pas de vie.
    /// </summary>
    private void PiedsGeles(Humain h, int pas, double temperature)
    {
        if (!PiedsDansLaNeige(h, out var epaisseur)) return;
        if (h.EstPetitEnfant(_maintenant) && h.Activite is Activite.PorteParSaMere) return;
        var contact = Math.Min(1, epaisseur / 5);
        var gel = 0.5 + Math.Max(0, -temperature) / 6;
        var protege = Math.Clamp(ContreLaNeige(h) + 0.15 * h.Cal * (1 - ContreLaNeige(h)), 0, 0.97);
        var marche = AuPas(h) ? 1 : 0.4;
        var mal = pas * 3 * contact * gel * marche * (1 - protege) * (1.2 - 0.4 * h.Personnalite.Endurance);
        if (mal <= 0) return;
        // Sans le monde de la matière (mondes d'avant la 0.42.6), les pieds ne se soignent pas : seul le froid compte.
        if (AvecLaMatiere) h.Pieds = Math.Min(100, h.Pieds + mal);
        h.Froid = Math.Min(100, h.Froid + 0.3 * mal);
    }
}
