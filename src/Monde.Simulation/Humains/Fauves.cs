using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Un carnassier que les humains ont appris à connaître (0.45.0) : il s'est approché d'un camp, a attaqué, a été blessé ou tué.
/// Les autres bêtes ne sont comptées qu'en nombre, case par case (voir <see cref="Comportement.Carnassiers"/>).
/// </summary>
/// <param name="Espece">Numéro de son espèce (<see cref="Faune.Get"/>).</param>
/// <param name="X">Case autour de laquelle il chasse : son domaine bouge avec les proies.</param>
/// <param name="Age">Âge en années.</param>
/// <param name="Blessure">0 : en pleine force ; 1 : très diminué (vieille blessure, dents usées). Les bêtes diminuées s'en prennent
/// plus volontiers aux humains, proies lentes (les lions mangeurs d'hommes de Tsavo avaient la mâchoire abîmée).</param>
/// <param name="Audace">0 : farouche ; 1 : ne craint plus rien.</param>
/// <param name="Habitude">Habitude des humains comme proies, de 0 à 1 : elle grandit à chaque attaque réussie.</param>
/// <param name="EvenementId">Événement du journal où on l'a connu (sa première attaque).</param>
public sealed record Fauve(int Id, int Espece, int X, int Y, double Age, double Blessure, double Audace, double Habitude, int Attaques, int Victimes,
    long ConnuLe, long VuLe, long EvenementId, long MortLe = 0, int TueParId = 0, long MortEvenementId = 0)
{
    public bool EnVie => MortLe == 0;
}

/// <summary>
/// Une attaque de bête gardée un an pour la carte et la vue 3D (0.45.1) : où, quand, l'espèce, la bête connue, la victime, et si elle
/// en est morte. <paramref name="EvenementId"/> : la ligne du journal qui la raconte.
/// </summary>
public sealed record AttaqueDeBete(long EvenementId, long Le, int X, int Y, int Espece, int FauveId, int VictimeId, bool Mort)
{
    /// <summary>
    /// 0.45.25 : le déroulé est noté (les attaques d'avant n'ont que le lieu, la victime et l'issue). Tout ce qui suit est la trace de
    /// ce que la simulation a tiré, pour le rejouer dans la vue 3D ; rien n'y est décidé.
    /// </summary>
    public bool Deroule { get; init; }
    /// <summary>Celui que la bête visait, quand elle a pris le tout-petit qui dormait contre lui (sinon 0).</summary>
    public int CibleId { get; init; }
    public bool Dort { get; init; }
    public bool AuCamp { get; init; }
    public bool Eau { get; init; }
    public ReactionAttaque Reaction { get; init; }
    /// <summary>Arme de la victime (<see cref="Arme"/>).</summary>
    public int ArmeVictime { get; init; }
    /// <summary>Ceux qui sont accourus, dans l'ordre, et ce qu'ils tenaient : une arme (<see cref="Arme"/>) ou −1 pour un tison du feu.</summary>
    public int[]? Aides { get; init; }
    public int[]? ArmesAides { get; init; }
    /// <summary>Ceux qui ont pris un coup de griffes.</summary>
    public int[]? Griffes { get; init; }
    public IssueAttaque Issue { get; init; }
    public bool BeteBlessee { get; init; }
    /// <summary>Celui qui a tué la bête (0 : elle a survécu).</summary>
    public int TueurId { get; init; }
}

/// <summary>Ce que fait la victime quand la bête bondit (0.45.25).</summary>
public enum ReactionAttaque : byte
{
    SeFige = 0,
    SeDefend = 1,
    Fuit = 2,
}

/// <summary>Comment finit l'attaque (0.45.25).</summary>
public enum IssueAttaque : byte
{
    /// <summary>La victime meurt sur place.</summary>
    Tuee = 0,
    /// <summary>La bête l'emporte (ou l'entraîne sous l'eau).</summary>
    Emportee = 1,
    /// <summary>La bête lâche prise d'elle-même.</summary>
    Lachee = 2,
    /// <summary>La victime ou ceux qui accourent la font lâcher.</summary>
    Repoussee = 3,
    /// <summary>La victime s'échappe, blessée.</summary>
    Echappee = 4,
}

/// <summary>
/// Les prédateurs qui attaquent (0.45.0, mondes à relief). Chaque heure, chacun court un risque qui tient à sa situation : la nuit
/// et la lune, être seul ou en groupe, son âge et sa santé, dormir à découvert ou à l'abri, le feu, les veilleurs, les chiens,
/// l'odeur de la viande, l'eau où guette le grand nageur, et la faim des bêtes quand les proies manquent. Une attaque est
/// toujours notée avec ce qui a compté.
/// <para>
/// Ce que dit la recherche. Les grands carnivores attaquent surtout la nuit et au crépuscule ; chez les lions de Tanzanie, surtout
/// en début de nuit dans les jours qui suivent la pleine lune, quand les premières heures sont noires (Packer et al., 2011), et
/// davantage là où leurs proies manquent (Packer et al., 2005). La plupart des victimes sont seules, beaucoup sont des enfants
/// (Treves et Naughton-Treves, 1999 ; Löe et Röskaft, 2004 ; Linnell et al., 2002 pour les loups ; Beier, 1991 pour les pumas).
/// Une hutte mal close n'arrête pas un grand félin ; les enclos d'épines, les chiens et les gardiens réduisent beaucoup les pertes
/// des troupeaux (Ogada et al., 2003). Chez les Hadza, tout le groupe n'a dormi en même temps que 18 minutes en 20 nuits : les
/// rythmes de chacun font des veilleurs sans qu'on le décide (Samson et al., 2017). Se défendre fait souvent lâcher prise (Beier,
/// 1991). Une espèce de carnivore vit à raison d'environ 90 kg pour 10 tonnes de proies (Carbone et Gittleman, 2002). Dans les
/// sociétés de chasseurs-cueilleurs étudiées, les attaques de bêtes font quelques pour cent des morts, davantage dans certains
/// milieux (jaguars chez les Aché : Hill et Hurtado, 1996 ; pythons chez les Agta : Headland et Greene, 2011). Les facteurs
/// ci-dessous suivent ces tendances ; leurs valeurs exactes sont des estimations, et chaque bête a sa propre audace.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Kilos de carnassiers d'une espèce que nourrissent dix tonnes de proies (Carbone et Gittleman, 2002).</summary>
    private const double CarnassiersParKgDeProies = 90.0 / 10_000;

    /// <summary>
    /// Rencontres par heure, pour une personne seule et exposée, d'une bête « menaçante » de la case (estimation réglée pour que
    /// les attaques fassent quelques pour cent des morts, mesurée sur huit graines et vingt ans).
    /// </summary>
    private const double RencontresParHeure = 1.1e-6;

    /// <summary>Les carnassiers connus (vivants et morts), dans l'ordre où on les a connus.</summary>
    public List<Fauve> Fauves { get; private set; } = [];

    /// <summary>Les attaques de l'année écoulée (0.45.1), de la plus ancienne à la plus récente.</summary>
    public List<AttaqueDeBete> AttaquesRecentes { get; private set; } = [];

    /// <summary>Attaques de bêtes notées depuis le lancement (0.45.25, pour « Aller jusqu'à la prochaine attaque de bête »).</summary>
    public long AttaquesLivrees { get; private set; }

    /// <summary>Carnassiers manquants par case (indice → part de la population tuée), qui se comble en quelques mois.</summary>
    public Dictionary<int, double> FauvesManquants { get; private set; } = [];

    /// <summary>Attaques et morts par bête depuis le début du monde (pour la mesure et la fiche du monde).</summary>
    public int AttaquesDeBetes { get; set; }

    public int MortsDeBetes { get; set; }

    /// <summary>Somme des risques d'attaque tirés (pour la mesure).</summary>
    public double RisqueCumule { get; set; }

    /// <summary>Remet les carnassiers connus d'un monde chargé.</summary>
    public void ChargerFauves(IEnumerable<Fauve>? fauves, IEnumerable<KeyValuePair<int, double>>? manquants, int attaques, int morts,
        IEnumerable<AttaqueDeBete>? recentes = null)
    {
        AttaquesRecentes = [.. (recentes ?? []).Where(a => Faune.Existe(a.Espece)).OrderBy(a => a.Le)];
        Fauves = [.. (fauves ?? []).Where(f => Faune.Existe(f.Espece)).OrderBy(f => f.Id)];
        FauvesManquants = new Dictionary<int, double>(manquants ?? []);
        (AttaquesDeBetes, MortsDeBetes) = (attaques, morts);
    }

    /// <summary>Activités qu'on vit au camp, auprès des autres.</summary>
    private static bool AuCamp(Activite a) => a is Activite.Repos or Activite.Dormir or Activite.SAbriter or Activite.VeillerEnfant or Activite.Veiller
        or Activite.Soigner or Activite.Enseigner or Activite.Fabriquer or Activite.Experimenter or Activite.Allaiter or Activite.Prier
        or Activite.Chanter or Activite.SAccoupler or Activite.PorteParSaMere or Activite.Rassembler;

    /// <summary>Activités au bord de l'eau, là où guette le grand nageur.</summary>
    private static bool AlEau(Activite a) => a is Activite.Boire or Activite.ChercherEau or Activite.Pecher or Activite.Nager;

    /// <summary>
    /// Carnassiers de la case, par espèce : leur nombre (d'après les proies que la terre porte, et un peu moins quand elles
    /// manquent), et leur faim (1 : les proies abondent ; jusqu'à 3 : elles ont disparu).
    /// </summary>
    public List<(Bete Bete, double Nombre, double Faim)> Carnassiers(int x, int y)
    {
        var liste = new List<(Bete, double, double)>();
        if (x < 0 || y < 0 || x >= carte.Largeur || y >= carte.Hauteur) return liste;
        var i = carte.Indice(x, y);
        var biome = carte.BiomeEn(x, y);
        var capacite = ecosysteme.NiveauGibier(i);
        var ratio = capacite <= 0 ? 0 : Math.Clamp(ecosysteme.Gibier[i] / capacite, 0, 1);
        // Les carnassiers suivent les proies avec retard : là où on a vidé la terre, ils restent un temps, affamés.
        var kgProies = capacite * MondeSimule.KgVifParPartDeGibier * (0.4 + 0.6 * ratio);
        var manque = FauvesManquants.GetValueOrDefault(i);
        var faim = 1 + 2 * (1 - ratio);
        foreach (var b in Faune.Sur(biome))
        {
            if (b.Regime != RegimeBete.Carnivore) continue;
            liste.Add((b, kgProies * CarnassiersParKgDeProies / Math.Max(1, b.Poids) * (1 - manque), faim));
        }
        if (Faune.NageurDe(biome) is { } n)
        {
            // Le grand nageur vit des poissons, des bêtes qui viennent boire et des charognes : on le compte comme un carnassier
            // de la case.
            liste.Add((n, Math.Max(capacite, 300) * MondeSimule.KgVifParPartDeGibier * CarnassiersParKgDeProies / n.Poids * (1 - manque), 1 + (1 - ratio)));
        }
        return liste;
    }

    /// <summary>Ce qu'une bête de cette espèce vaut de menace pour un humain : son humeur, et sa taille face à la sienne.</summary>
    private static double Menace(Bete b) => b.Agressivite * Math.Clamp(b.Poids / 80, 0.15, 2.2);

    /// <summary>
    /// Chaque heure du monde : les rencontres avec les bêtes. Les dormeurs d'un camp, ceux qui cueillent au loin, celui qui va seul
    /// à l'eau courent chacun leur risque.
    /// </summary>
    internal void HeureDesFauves(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (!AvecLeRelief) return;
        _maintenant = maintenant;
        _tous = humains;
        var date = Calendrier.Decomposer(maintenant);
        var nuit = Obscurite(date, maintenant);

        // Qui est où : au camp (dormeurs et éveillés) ou au loin, case par case.
        var cases = new Dictionary<int, List<Humain>>();
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Mer.EnMer || h.Activite == Activite.Mort) continue;
            var i = carte.Indice(h.X, h.Y);
            if (!cases.TryGetValue(i, out var l)) cases[i] = l = [];
            l.Add(h);
        }

        foreach (var (i, presents) in cases.OrderBy(c => c.Key))
        {
            var x = i % carte.Largeur;
            var y = i / carte.Largeur;
            var carnassiers = Carnassiers(x, y);
            var connus = Fauves.Where(f => f.EnVie && Math.Abs(f.X - x) <= 2 && Math.Abs(f.Y - y) <= 2).ToList();
            if (carnassiers.Count == 0 && connus.Count == 0) continue;
            var camp = new Situation(this, presents, maintenant, x, y, alea.Reel());
            // Le grand nageur est un reptile : actif quand l'eau est chaude, engourdi par le froid ; à la saison sèche, bêtes et
            // humains se pressent aux mêmes points d'eau et les attaques culminent (Leslie, 1997 ; estimations).
            var nageur = Math.Clamp((Temperature(x, y, maintenant) - 10) / 15, 0.1, 1.4) * (date.Saison == Saison.Ete ? 1.3 : 1);
            foreach (var h in presents)
            {
                if (!h.EstVivant) continue;
                var expo = Exposition(h, camp, maintenant);
                if (expo <= 0) continue;
                // Les espèces de la case, et les bêtes connues du voisinage qui ont pris goût aux humains.
                var menaces = new List<(Bete Bete, Fauve? Connu, double Poids)>();
                foreach (var (b, nombre, faim) in carnassiers)
                {
                    if (b.Eau != (AlEau(h.Activite) && !AuCamp(h.Activite))) continue;
                    var moment = b.Eau ? (0.6 + 0.4 * nuit) * nageur : nuit;
                    menaces.Add((b, null, nombre * Menace(b) * faim * moment));
                }
                foreach (var f in connus)
                {
                    var b = Faune.Get(f.Espece);
                    if (b.Eau != (AlEau(h.Activite) && !AuCamp(h.Activite))) continue;
                    menaces.Add((b, f, Menace(b) * (0.5 + f.Audace) * 4 * f.Habitude * (b.Eau ? (0.6 + 0.4 * nuit) * nageur : nuit)));
                }
                var total = menaces.Sum(m => m.Poids);
                if (total <= 0) continue;
                var risque = RencontresParHeure * total * expo;
                RisqueCumule += risque;
                if (alea.Reel() >= risque) continue;
                // Une bête vient. Laquelle ?
                var u = alea.Reel() * total;
                var choix = menaces[^1];
                foreach (var m in menaces)
                {
                    u -= m.Poids;
                    if (u < 0)
                    {
                        choix = m;
                        break;
                    }
                }
                // Un veilleur la voit, un chien aboie, le feu l'arrête, la haie d'épines la tient dehors : elle rôde et s'en va.
                if (Retenue(h, camp, choix.Bete, out var precaution))
                {
                    Rodee(h, camp, precaution, maintenant);
                    continue;
                }
                Attaquer(h, camp, choix.Bete, choix.Connu, nuit, maintenant);
            }
        }
    }

    /// <summary>
    /// Ce qu'il fait de noir, de 0 (plein jour) à 1 (nuit sans lune) : les bêtes chassent surtout au crépuscule et dans le noir.
    /// La lune décroissante se lève tard : après la pleine lune, le début de la nuit est noir (Packer et al., 2011).
    /// </summary>
    private static double Obscurite(DateMonde date, long maintenant)
    {
        if (date.Lumiere >= 0.6) return 0.06;
        // Crépuscule et aube : l'heure des chasseurs.
        if (date.Lumiere > 0.02) return 1.2;
        var phase = maintenant / (double)Calendrier.SecondesParJour / 29.53 % 1;
        // La lune se lève vers 6 h à la nouvelle lune, vers 18 h à la pleine lune, et reste douze heures dans le ciel.
        var lever = (6 + 24 * phase) % 24;
        var depuis = (date.Heure + date.Minute / 60.0 - lever + 24) % 24;
        var lune = depuis < 12 ? Lune(maintenant) : 0;
        return 1 - 0.5 * lune;
    }

    /// <summary>Ce qu'on sait de ceux d'une case, pour une heure : groupe au camp, veilleurs, feu, abri, chiens, précautions.</summary>
    private sealed class Situation
    {
        public readonly List<Humain> AuCamp = [];
        public readonly int Eveilles;
        public readonly bool Veilleur;
        public readonly bool Feu;
        public readonly bool Braise;
        public readonly double Abri;
        public readonly bool Chiens;
        public readonly bool Odeur;
        public readonly bool SaitVeille;
        public readonly bool SaitSerrer;
        public readonly bool SaitRestes;
        public readonly bool SaitEpines;

        public Situation(Comportement c, List<Humain> presents, long maintenant, int x, int y, double tirage)
        {
            foreach (var h in presents)
                if (AuCamp(h.Activite)) AuCamp.Add(h);
            var adultes = AuCamp.Where(h => !h.EstBebe(maintenant) && h.AgeEnAnnees(maintenant) >= 10).ToList();
            Eveilles = adultes.Count(h => h.Activite != Activite.Dormir);
            // Les uns s'endorment tôt, d'autres tard, les vieux s'éveillent avant l'aube : il y a souvent quelqu'un d'éveillé
            // (Samson et al., 2017). Chacun a son rythme (tiré de son numéro), un peu plus de veille avec l'âge.
            var personne = 1.0;
            foreach (var h in adultes)
            {
                if (h.Activite != Activite.Dormir) continue;
                var rythme = ((uint)(h.Id * 2654435761u) >> 8) / (double)(1 << 24);
                personne *= 1 - Math.Clamp(0.05 + 0.15 * rythme + 0.004 * Math.Max(0, h.AgeEnAnnees(maintenant) - 30), 0, 0.4);
            }
            SaitVeille = adultes.Any(h => h.Savoirs.Exists(s => s.Effet == Effet.Veille && s.Confiance >= 0.5));
            Veilleur = Eveilles > 0 || (SaitVeille && adultes.Count >= 2) || tirage >= personne;
            Feu = c.Installations.FoyerEn(x, y) is not null;
            Braise = !Feu && c.Installations.BraiseEn(x, y) is not null;
            Abri = Math.Clamp(c.AbriEn(x, y), 0, 1);
            Chiens = AuCamp.Any(h => c._chiens.Contains(Maisonnee(h)));
            // L'odeur de la viande : une réserve de viande au camp, ou une bête rapportée aujourd'hui.
            Odeur = c.Installations.Reserves.Exists(r => r.X == x && r.Y == y && r.Kg > 0 && r.Sorte is SorteReserve.ViandeSechee or SorteReserve.ViandeCachee)
                    || presents.Exists(h => h.PriseLe > maintenant - Calendrier.SecondesParJour && h.PriseEspece is > 0 and < Humain.PrisePoisson);
            SaitSerrer = adultes.Any(h => h.Savoirs.Exists(s => s.Effet == Effet.Serrer && s.Confiance >= 0.5));
            SaitRestes = adultes.Any(h => h.Savoirs.Exists(s => s.Effet == Effet.Restes && s.Confiance >= 0.5));
            SaitEpines = adultes.Any(h => h.Savoirs.Exists(s => s.Effet == Effet.Epines && s.Confiance >= 0.5));
        }
    }

    /// <summary>Le tout-petit dort avec quelqu'un, ou est porté : c'est à travers lui que la bête l'atteindrait.</summary>
    private static bool Garde(Humain h) => h.Activite == Activite.PorteParSaMere || h.Coucher is Coucher.Contre or Coucher.ACote or Coucher.Confie;

    /// <summary>
    /// Combien il s'offre aux bêtes cette heure-ci (1 : un adulte seul, éveillé, loin du camp ; 0 : à l'abri de tout). Seul ou en
    /// groupe, son âge, sa santé, son sommeil, le feu, l'abri, l'odeur de la viande.
    /// </summary>
    private double Exposition(Humain h, Situation s, long maintenant)
    {
        var age = h.AgeEnAnnees(maintenant);
        // Un tout-petit qu'on porte ou qui dort contre quelqu'un n'est pris qu'avec lui (voir Attaquer).
        if (h.EstBebe(maintenant) && Garde(h)) return 0;
        var auCamp = AuCamp(h.Activite);
        double e;
        if (auCamp && !(h.Coucher == Coucher.AEcart))
        {
            // Au camp : la bête choisit parmi tous, et un groupe la tient à distance.
            var n = s.AuCamp.Count(o => !o.EstBebe(maintenant) || !Garde(o));
            e = n <= 1 ? 1 : Math.Pow(n, -1.4);
            if (h.Activite == Activite.Dormir) e *= s.Abri > 0 ? 1 - 0.5 * s.Abri : 1;
            if (s.Feu) e *= 0.3;
            else if (s.Braise) e *= 0.8;
            if (s.SaitEpines) e *= 0.25;
            // Les enfants au milieu des dormeurs : ils ne sont pas plus exposés que les adultes.
            if (s.SaitSerrer && age < 12 && n > 2) age = 12;
        }
        else
        {
            // Au loin : seul, ou avec ceux qui font la même chose sur la même case.
            var avec = 0;
            foreach (var o in _tous.Vivants())
                if (o.Id != h.Id && o.EstVivant && o.X == h.X && o.Y == h.Y && !AuCamp(o.Activite) && o.Activite == h.Activite) avec++;
            e = avec == 0 ? 1 : Math.Pow(avec + 1, -1.4);
            if (avec == 0 && h.Savoirs.Exists(k => k.Effet == Effet.APlusieurs && k.Confiance >= 0.5)) e *= 0.5;
        }
        // Les enfants d'abord, puis les malades, les blessés, les vieux, ceux qui boitent.
        e *= age < 2 ? 4 : age < 6 ? 3 : age < 12 ? 2 : age >= 60 ? 1.4 : 1;
        if (h.EstMalade(maintenant)) e *= 1.4;
        if (h.Sante < 50) e *= 1.5;
        if (h.BlessureGrave >= 20) e *= 1.6;
        if (Sequelles.Gene(h, Membre.Jambe, maintenant) > 0) e *= 1.3;
        if (h.Activite == Activite.Dormir && !s.Veilleur) e *= 1.6;
        if (s.Odeur && !s.SaitRestes) e *= 1.5;
        // Qui a peur des bêtes ouvre l'œil.
        if (h.PeurDesBetes > 40) e *= 0.85;
        return e;
    }

    /// <summary>La bête est vue, entendue ou arrêtée avant d'attaquer : par un veilleur, des chiens, le feu, la haie d'épines.</summary>
    private bool Retenue(Humain h, Situation s, Bete b, out Effet precaution)
    {
        precaution = Effet.Rien;
        if (!AuCamp(h.Activite) || b.Eau) return false;
        if (s.Veilleur && alea.Reel() < (s.SaitVeille ? 0.6 : 0.35))
        {
            precaution = s.SaitVeille ? Effet.Veille : Effet.Rien;
            return true;
        }
        if (s.Chiens && alea.Reel() < 0.4) return true;
        if (s.SaitEpines && alea.Reel() < 0.3)
        {
            precaution = Effet.Epines;
            return true;
        }
        return false;
    }

    /// <summary>Une bête a rôdé sans attaquer : ceux qui gardent une précaution la tiennent pour bonne (ils ne l'oublient pas).</summary>
    private void Rodee(Humain h, Situation s, Effet precaution, long maintenant)
    {
        if (precaution == Effet.Rien) return;
        foreach (var o in s.AuCamp)
        {
            var k = o.Savoirs.FindIndex(x => x.Effet == precaution);
            if (k >= 0) o.Savoirs[k] = o.Savoirs[k] with { Essais = o.Savoirs[k].Essais + 1, Succes = o.Savoirs[k].Succes + 1, UtiliseLe = maintenant };
        }
    }

    /// <summary>
    /// Une attaque : la victime se débat, fuit ou se fige ; ceux qui sont là accourent ou non ; la bête lâche prise, emporte sa
    /// proie, ou est blessée, tuée. Tout est noté, avec ce qui a compté.
    /// </summary>
    private void Attaquer(Humain cible, Situation s, Bete b, Fauve? connu, double nuit, long maintenant)
    {
        // Un tout-petit qui dort contre celui qui le garde : la bête prend l'adulte, ou l'arrache à ses bras.
        var victime = cible;
        if (!b.Eau)
            foreach (var e in _tous.Vivants())
                if (e.EstVivant && e.X == cible.X && e.Y == cible.Y && e.CoucheAvecId == cible.Id && e.Coucher is Coucher.Contre or Coucher.ACote or Coucher.Confie
                    && alea.Reel() < (e.Coucher == Coucher.ACote ? 0.5 : 0.25))
                {
                    victime = e;
                    break;
                }
        var age = victime.AgeEnAnnees(maintenant);
        var fauve = connu ?? NouveauFauve(b, cible.X, cible.Y, maintenant);
        var p = victime.Personnalite;
        var dort = victime.Activite == Activite.Dormir;
        var auCamp = AuCamp(victime.Activite) && victime.Coucher != Coucher.AEcart;

        // Ce qui a compté : seul, endormi, sans veilleur, sans feu, l'enfant à l'écart, l'eau.
        var circonstances = new List<string>();
        if (b.Eau) circonstances.Add("eau");
        else if (!auCamp) circonstances.Add(victime.Coucher == Coucher.AEcart ? "ecart" : "seul");
        if (dort) circonstances.Add(s.Veilleur ? "endormi" : "sansVeilleur");
        if (auCamp && !b.Eau && !s.Feu) circonstances.Add(s.Braise ? "braise" : "sansFeu");
        if (nuit >= 1) circonstances.Add("nuitNoire");
        if (s.Odeur && !s.SaitRestes) circonstances.Add("viande");
        if (age < 12) circonstances.Add("enfant");
        var circ = circonstances.Count == 0 ? (object)"" : new Recit("fauve.circ", ("liste", Circonstances(circonstances)));
        var attaque = Noter(maintenant, TypeEvenement.Nature, victime, null,
            new Recit(b.Eau ? "fauve.attaque.eau" : "fauve.attaque", ("h", victime), ("bete", b.Dit), ("connue", fauve.Attaques > 0 ? Recit.Seul("fauve.connue") : ""),
                ("circ", circ)), fauve.EvenementId, significatif: true);
        AttaquesDeBetes++;

        // La victime : se défendre, fuir, se figer (Beier, 1991 : se défendre fait souvent lâcher prise).
        var force = age < 12 ? 0.25 + 0.05 * age : age >= 65 ? 0.6 : 1.0;
        var armeVictime = (int)MondeSimule.ArmePortee(victime);
        var arme = armeVictime > 0 ? 1.8 : 1;
        var seDefend = age >= 10 && alea.Reel() < Math.Clamp((0.25 + 0.4 * (1 - p.Nevrosisme) + 0.3 * p.Endurance + (victime.Sexe == Sexe.Homme ? 0.1 : 0)
                                                              - (victime.EstMalade(maintenant) ? 0.3 : 0)) * (dort ? 0.6 : 1), 0.05, 0.9);
        var fuit = !seDefend && age >= 4 && alea.Reel() < 0.3 + 0.5 * p.Nevrosisme;

        // Ceux qui sont là : au camp, les éveillés et les dormeurs que les cris réveillent ; au loin, ceux de la case.
        var aides = new List<Humain>();
        var armesAides = new List<int>();
        var defense = seDefend ? (0.5 + 0.8 * p.Endurance) * force * arme : 0;
        var armes = seDefend && arme > 1 ? 1.0 : 0;
        foreach (var o in _tous.Vivants())
        {
            if (o.Id == victime.Id || !o.EstVivant || o.X != victime.X || o.Y != victime.Y || o.EstBebe(maintenant) || o.Mer.EnMer) continue;
            var oAuCamp = AuCamp(o.Activite);
            if (b.Eau ? !AlEau(o.Activite) && !oAuCamp : oAuCamp != auCamp && !(auCamp == false && victime.Coucher == Coucher.AEcart && oAuCamp)) continue;
            if (o.Activite == Activite.Dormir && alea.Reel() >= 0.75) continue;
            var q = o.Personnalite;
            var oAge = o.AgeEnAnnees(maintenant);
            var parent = victime.MereId == o.Id || victime.PereId == o.Id;
            var courage = 0.3 * (1 - q.Nevrosisme) + 0.25 * q.Extraversion + 0.2 * q.Endurance + 0.25 * (1 - q.Prudence)
                          + (parent ? 0.5 : 0) + (o.PartenaireId == victime.Id ? 0.4 : 0) + Math.Max(0, o.AffectionPour(victime.Id)) / 200;
            if (oAge < 12) courage *= 0.2;
            if (alea.Reel() >= Math.Clamp(courage, 0, 0.95)) continue;
            aides.Add(o);
            var oArme = (int)MondeSimule.ArmePortee(o) > 0 ? 2.2 : s.Feu && oAuCamp ? 1.5 : 1;
            armesAides.Add((int)MondeSimule.ArmePortee(o) > 0 ? (int)MondeSimule.ArmePortee(o) : s.Feu && oAuCamp ? -1 : 0);
            defense += (0.6 + 0.8 * q.Endurance) * (oAge < 15 ? 0.4 : 1) * oArme;
            if (oArme > 1.6) armes += 1;
        }
        var puissance = Math.Pow(b.Poids / 60, 0.6) * (0.7 + 0.6 * fauve.Audace) * (1 - 0.5 * fauve.Blessure) * (b.Eau ? 1.5 : 1);
        if (aides.Count > 0)
        {
            Noter(maintenant, TypeEvenement.Entraide, aides[0], victime, aides.Count == 1
                ? new Recit("fauve.aide.un", ("a", aides[0]), ("h", victime))
                : new Recit("fauve.aide.plusieurs", ("a", aides[0]), ("n", aides.Count - 1), ("h", victime)), attaque);
        }

        // L'issue. La bête frappe d'abord ; elle lâche prise si la défense est assez forte, sinon elle tue ou emporte.
        var lache = defense / (defense + puissance);
        if (fuit && aides.Count == 0) lache *= 0.5;
        var gravite = Math.Clamp((10 + 35 * alea.Reel()) * 2 * puissance / (puissance + force), 5, 95);
        var lacheprise = alea.Reel() < lache;
        var tue = !lacheprise && alea.Reel() < 0.85;
        // La clémence du monde laisse en vie une part de ceux que la bête aurait tués.
        if (tue && Clemence > 0 && alea.Reel() < Clemence) tue = false;
        var victimes = fauve.Victimes;
        var emporte = false;
        if (tue)
        {
            emporte = alea.Reel() < (b.Eau ? 0.7 : age < 12 ? 0.6 : 0.25) * (aides.Count == 0 ? 1 : 0.3);
            var recit = new Recit(emporte ? (b.Eau ? "fauve.mort.eau" : "fauve.mort.emporte") : "fauve.mort", ("h", victime), ("bete", b.Dit),
                ("age", AgeEnMots(victime, maintenant)));
            Tuer(victime, maintenant, CauseDeMort.Bete, recit, attaque);
            MortsDeBetes++;
            victimes++;
            // Un tout-petit qui dormait contre la victime : la bête ne le prend pas, mais il est seul.
        }
        else
        {
            victime.Sante -= gravite * (lacheprise ? 0.6 : 1);
            Medecine.Blesser(victime, gravite * (lacheprise ? 0.6 : 1), maintenant, OrigineBlessure.Morsure);
            if (victime.Sante <= 0) victime.Sante = 1;
            Noter(maintenant, TypeEvenement.Medecine, victime, null,
                new Recit(lacheprise ? (aides.Count > 0 || seDefend ? "fauve.repousse" : "fauve.lache") : "fauve.echappe", ("h", victime), ("bete", b.Dit)), attaque);
        }
        // Les aides qui s'approchent trop prennent des coups de griffes.
        var griffes = new List<int>();
        foreach (var a in aides)
        {
            if (alea.Reel() >= 0.12 * puissance / (puissance + 1)) continue;
            griffes.Add(a.Id);
            var g = 5 + 20 * alea.Reel();
            a.Sante -= g;
            if (a.Sante <= 0) a.Sante = 1;
            Medecine.Blesser(a, g, maintenant, OrigineBlessure.Morsure);
            Noter(maintenant, TypeEvenement.Medecine, a, null, new Recit("fauve.griffe", ("h", a), ("bete", b.Dit)), attaque);
        }

        // La bête : blessée ou tuée par ceux qui se défendent, à l'épieu surtout ; enhardie par ce qu'elle a pris, ou instruite par les coups.
        var blessee = lacheprise && alea.Reel() < 0.5 * lache;
        var tuee = lacheprise && armes > 0 && alea.Reel() < Math.Min(0.6, 0.15 * armes + 0.05 * aides.Count) * (1 - 0.5 * Math.Min(1, b.Poids / 400));
        var tueur = tuee ? aides.Find(a => (int)MondeSimule.ArmePortee(a) > 0) ?? (seDefend ? victime : aides.FirstOrDefault()) : null;
        var habitude = Math.Clamp(fauve.Habitude + (tue ? 0.25 : lacheprise ? -0.1 : 0.05), 0, 1);
        var audace = Math.Clamp(fauve.Audace + (tue ? 0.1 : lacheprise ? -0.1 : 0), 0, 1);
        var nouvelle = fauve with
        {
            EvenementId = fauve.EvenementId == 0 ? attaque : fauve.EvenementId,
            X = cible.X, Y = cible.Y, Attaques = fauve.Attaques + 1, Victimes = victimes, Habitude = habitude, Audace = audace, VuLe = maintenant,
            Blessure = blessee ? Math.Min(1, fauve.Blessure + 0.2 + 0.3 * alea.Reel()) : fauve.Blessure,
        };
        if (tuee && tueur is not null)
        {
            var mort = Noter(maintenant, TypeEvenement.Nature, tueur, victime, new Recit("fauve.tuee", ("a", tueur), ("bete", b.Dit)), attaque, significatif: true);
            nouvelle = nouvelle with { MortLe = maintenant, TueParId = tueur.Id, MortEvenementId = mort };
            Manquer(carte.Indice(cible.X, cible.Y), b);
            // La viande de la bête nourrit ceux qui l'ont tuée.
            var kg = b.Poids * 0.5 / Math.Max(1, aides.Count + 1);
            foreach (var a in aides.Append(victime).Where(a => a.EstVivant)) a.Faim = Math.Max(0, a.Faim - 10 * Math.Min(3, kg));
        }
        else if (blessee) Noter(maintenant, TypeEvenement.Nature, aides.FirstOrDefault() ?? victime, null, new Recit("fauve.blessee", ("bete", b.Dit)), attaque);
        Garder(nouvelle);
        AttaquesRecentes.Add(new AttaqueDeBete(attaque, maintenant, victime.X, victime.Y, b.Numero, nouvelle.Id, victime.Id, tue)
        {
            Deroule = true, CibleId = victime.Id == cible.Id ? 0 : cible.Id, Dort = dort, AuCamp = auCamp, Eau = b.Eau,
            Reaction = seDefend ? ReactionAttaque.SeDefend : fuit ? ReactionAttaque.Fuit : ReactionAttaque.SeFige,
            ArmeVictime = armeVictime, Aides = [.. aides.Select(a => a.Id)], ArmesAides = [.. armesAides],
            Griffes = griffes.Count == 0 ? null : [.. griffes],
            Issue = tue ? emporte ? IssueAttaque.Emportee : IssueAttaque.Tuee
                : lacheprise ? aides.Count > 0 || seDefend ? IssueAttaque.Repoussee : IssueAttaque.Lachee : IssueAttaque.Echappee,
            BeteBlessee = blessee, TueurId = tuee && tueur is not null ? tueur.Id : 0,
        });
        AttaquesLivrees++;

        // La peur, le lieu redouté, et peut-être une idée pour s'en garder.
        Effrayer(victime, aides, b, tue, attaque, maintenant);
        Lecons(victime, aides, s, circonstances, tue, attaque, maintenant);
    }

    /// <summary>« seul, endormi, le feu éteint » : les circonstances, dans l'ordre.</summary>
    private static Recit Circonstances(List<string> c)
    {
        var parties = c.Select(k => Recit.Seul($"fauve.circ.{k}")).ToList();
        if (parties.Count == 1) return parties[0];
        var r = new Recit("fauve.et", ("a", parties[^2]), ("b", parties[^1]));
        for (var k = parties.Count - 3; k >= 0; k--) r = new Recit("fauve.virgule", ("a", parties[k]), ("b", r));
        return r;
    }

    /// <summary>Une bête qu'on ne connaissait pas : son âge, une vieille blessure, son audace (les bêtes diminuées s'en prennent plus aux humains).</summary>
    private Fauve NouveauFauve(Bete b, int x, int y, long maintenant)
    {
        var diminuee = alea.Reel() < 0.3;
        var vie = b.Eau ? 50 : 4 + 4 * b.AnsMaturite;
        var age = b.AnsMaturite + (vie - b.AnsMaturite) * (diminuee ? 0.6 + 0.4 * alea.Reel() : alea.Reel());
        var blessure = diminuee ? 0.3 + 0.6 * alea.Reel() : 0;
        var id = Fauves.Count == 0 ? 1 : Fauves[^1].Id + 1;
        return new Fauve(id, b.Numero, x, y, age, blessure, Math.Clamp(0.2 + 0.5 * alea.Reel() + 0.3 * blessure, 0, 1), 0.1, 0, 0, maintenant, maintenant, 0);
    }

    /// <summary>Garde la bête connue (la remplace si on la connaissait déjà) ; la première fois, son numéro d'événement est celui de l'attaque.</summary>
    private void Garder(Fauve f)
    {
        var k = Fauves.FindIndex(x => x.Id == f.Id);
        if (k >= 0) Fauves[k] = f;
        else Fauves.Add(f);
    }

    /// <summary>Un carnassier tué : sa case en compte un de moins, le temps que d'autres viennent prendre sa place.</summary>
    private void Manquer(int i, Bete b)
    {
        var (x, y) = (i % carte.Largeur, i / carte.Largeur);
        var nombre = Carnassiers(x, y).Where(c => c.Bete.Numero == b.Numero).Sum(c => c.Nombre);
        FauvesManquants[i] = Math.Min(0.9, FauvesManquants.GetValueOrDefault(i) + 1 / Math.Max(1, nombre + 1));
    }

    /// <summary>
    /// La peur reste : forte chez la victime et ceux qui ont vu, plus faible chez les siens qui l'apprennent, plus forte chez les
    /// anxieux. Le lieu devient redouté un temps. Ceux que l'invisible occupe se demandent qui l'a voulu.
    /// </summary>
    private void Effrayer(Humain victime, List<Humain> aides, Bete b, bool tue, long attaque, long maintenant)
    {
        foreach (var o in _tous.Vivants())
        {
            if (!o.EstVivant || o.EstBebe(maintenant)) continue;
            var la = o.X == victime.X && o.Y == victime.Y;
            var siens = victime.CommunauteId != 0 && o.CommunauteId == victime.CommunauteId;
            if (!la && !siens) continue;
            var force = o.Id == victime.Id ? 80 : la ? (tue ? 65 : 45) : tue ? 25 : 10;
            var gain = force * (0.4 + 1.2 * o.Personnalite.Nevrosisme);
            if (gain < 1) continue;
            o.PeurDesBetes = Math.Min(100, Math.Max(o.PeurDesBetes, gain) + 0.2 * Math.Min(o.PeurDesBetes, gain));
            o.PeurDesBetesEvenementId = attaque;
            o.Humeur = Math.Max(0, o.Humeur - gain / 5);
            if (la || o.PeurDesBetes >= 40)
            {
                (o.RedouteX, o.RedouteY) = (victime.X, victime.Y);
                o.RedouteJusqua = maintenant + (long)((20 + 100 * alea.Reel()) * (0.5 + o.Personnalite.Nevrosisme) * Calendrier.SecondesParJour);
            }
            if (gain >= 25)
                Jardin.Confier(o, TypeIntime.Peur, b.Numero, new Recit(o.Id == victime.Id ? "js.fauve.victime" : "js.fauve.peur", ("bete", b.Dit), ("h", victime)),
                    o.PeurDesBetes, attaque, maintenant, "peur:bete");
            // Ce qui frappe dans la nuit fait chercher une main derrière le monde (Norenzayan et Hansen, 2006).
            if (tue && gain >= 30 && o.Personnalite.Transcendance >= 0.55)
                Croyances.Ajouter(o, Math.Min(4, gain / 20) * (0.3 + o.Personnalite.Transcendance), new Recit("cro.fauve", ("bete", b.Dit), ("h", victime)), attaque, maintenant);
        }
    }

    /// <summary>
    /// Après une attaque, quelqu'un de prudent ou d'inventif imagine comment s'en garder, d'après ce qui a compté : veiller,
    /// dormir serrés avec les enfants au milieu, porter les restes loin, ne plus aller seul, une haie d'épines. Il le montre à
    /// ceux qui sont là.
    /// </summary>
    private void Lecons(Humain victime, List<Humain> aides, Situation s, List<string> circonstances, bool tue, long attaque, long maintenant)
    {
        var lecons = new List<Effet>();
        if (circonstances.Contains("sansVeilleur") || circonstances.Contains("endormi")) lecons.Add(Effet.Veille);
        if (circonstances.Contains("enfant") || circonstances.Contains("ecart")) lecons.Add(Effet.Serrer);
        if (circonstances.Contains("viande")) lecons.Add(Effet.Restes);
        if (circonstances.Contains("seul") || circonstances.Contains("eau")) lecons.Add(Effet.APlusieurs);
        if (s.AuCamp.Count > 0 && !circonstances.Contains("seul") && !circonstances.Contains("eau")) lecons.Add(Effet.Epines);
        if (lecons.Count == 0) return;
        foreach (var o in _tous.Vivants())
        {
            if (!o.EstVivant || o.X != victime.X || o.Y != victime.Y || o.AgeEnAnnees(maintenant) < 12 || o.Activite == Activite.Dormir && !aides.Contains(o)) continue;
            var lecon = lecons[alea.Entier(lecons.Count)];
            if (o.Sait(lecon)) continue;
            var q = o.Personnalite;
            if (alea.Reel() >= (tue ? 0.25 : 0.12) * (0.3 + q.Ouverture) * (0.5 + q.Prudence) * (0.4 + Ingeniosite(o))) continue;
            var (geste, a) = lecon switch
            {
                Effet.Veille => (Geste.Patienter, Matiere.Feu),
                Effet.Serrer => (Geste.Entasser, Matiere.Aucune),
                Effet.Restes => (Geste.Pousser, Matiere.Carcasse),
                Effet.APlusieurs => (Geste.Approcher, Matiere.Aucune),
                _ => (Geste.Entasser, Matiere.Branche),
            };
            var ev = Retenir(o, geste, a, Matiere.Aucune, lecon, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, _tous,
                recit: new Recit($"fauve.idee.{lecon}", ("h", o)));
            Montrer(o, geste, a, Matiere.Aucune, lecon, 0, Symptome.Aucun, _tous, ev == 0 ? attaque : ev);
        }
    }

    /// <summary>
    /// Une fois par jour : les bêtes connues vieillissent, suivent les proies, meurent ; les cases vidées se repeuplent ; la peur
    /// s'apaise, et l'on refait chaque soir les précautions tant qu'on a peur ; les troupeaux perdent des bêtes la nuit ; les
    /// proches d'une victime traquent la bête.
    /// </summary>
    internal void JourneeDesFauves(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (!AvecLeRelief) return;
        _maintenant = maintenant;
        _tous = humains;
        const double jour = 1.0 / Calendrier.JoursParAn;
        for (var k = 0; k < Fauves.Count; k++)
        {
            var f = Fauves[k];
            if (!f.EnVie) continue;
            var b = Faune.Get(f.Espece);
            var vie = b.Eau ? 50 : 4 + 4 * b.AnsMaturite;
            // Mourir : de vieillesse, d'une blessure qui s'infecte ou l'empêche de chasser.
            var mort = 0.3 / Calendrier.JoursParAn * (1 + 3 * f.Blessure) * (f.Age > vie ? 10 : 1);
            if (alea.Reel() < mort)
            {
                Fauves[k] = f with { MortLe = maintenant };
                continue;
            }
            // Suivre les proies : un pas vers la case voisine la plus giboyeuse, de temps en temps.
            var (x, y) = (f.X, f.Y);
            if (alea.Reel() < 0.1)
            {
                var (dx, dy) = Voisins8[alea.Entier(Voisins8.Length)];
                if (x + dx >= 0 && y + dy >= 0 && x + dx < carte.Largeur && y + dy < carte.Hauteur
                    && (b.Eau ? Faune.NageurDe(carte.BiomeEn(x + dx, y + dy)) is not null : Faune.Sur(carte.BiomeEn(x + dx, y + dy)).Contains(b))
                    && ecosysteme.Gibier[carte.Indice(x + dx, y + dy)] >= ecosysteme.Gibier[carte.Indice(x, y)] * 0.8)
                    (x, y) = (x + dx, y + dy);
            }
            // L'habitude des humains s'efface lentement quand elle n'attaque plus.
            Fauves[k] = f with { X = x, Y = y, Age = f.Age + jour, Habitude = Math.Max(0, f.Habitude - 0.002), Blessure = Math.Max(0, f.Blessure - 0.001) };
        }
        // On ne garde en mémoire que les bêtes vivantes et les mortes de moins de deux ans.
        Fauves.RemoveAll(f => !f.EnVie && maintenant - f.MortLe > 2 * Calendrier.SecondesParAn);
        AttaquesRecentes.RemoveAll(a => maintenant - a.Le > Calendrier.SecondesParAn);
        foreach (var i in FauvesManquants.Keys.ToList())
        {
            var m = FauvesManquants[i] * 0.99;
            if (m < 0.01) FauvesManquants.Remove(i);
            else FauvesManquants[i] = m;
        }

        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant) continue;
            if (h.PeurDesBetes > 0)
            {
                var demiVie = 4 * Math.Exp(3.5 * (h.Personnalite.Nevrosisme - 0.5)) * (1 + h.PeurDesBetes / 50);
                h.PeurDesBetes *= Math.Pow(0.5, 1 / demiVie);
                if (h.PeurDesBetes < 1) h.PeurDesBetes = 0;
                // Tant qu'on a peur, on refait les précautions qu'on connaît : elles restent en mémoire.
                if (h.PeurDesBetes >= 15)
                    for (var k = 0; k < h.Savoirs.Count; k++)
                        if (h.Savoirs[k].Effet is Effet.Veille or Effet.Serrer or Effet.Restes or Effet.APlusieurs or Effet.Epines)
                            h.Savoirs[k] = h.Savoirs[k] with { UtiliseLe = maintenant };
            }
            if (h.RedouteJusqua > 0 && h.RedouteJusqua < maintenant) (h.RedouteX, h.RedouteY, h.RedouteJusqua) = (-1, -1, 0);
        }

        Troupeaux(humains, maintenant);
        Traquer(humains, maintenant);
    }

    /// <summary>
    /// La nuit, les carnassiers prennent des bêtes des troupeaux : surtout sans chien ni gardien près d'elles, et moins derrière
    /// une haie d'épines (Ogada et al., 2003 : enclos, chiens et gardiens réduisent beaucoup les pertes).
    /// </summary>
    private void Troupeaux(IReadOnlyList<Humain> humains, long maintenant)
    {
        for (var k = 0; k < Champs.Troupeaux.Count; k++)
        {
            var t = Champs.Troupeaux[k];
            if (t.Nombre < 0.5 || !Faune.Existe(t.Espece)) continue;
            var proie = Faune.Get(t.Espece);
            // Les chiens ne se mangent pas entre eux : les carnassiers gardés ne sont pas des proies.
            if (proie.Regime == RegimeBete.Carnivore) continue;
            var (x, y, gardien) = OuEst(t, humains);
            if (x < 0) continue;
            var carnassiers = Carnassiers(x, y);
            var menace = carnassiers.Where(c => !c.Bete.Eau).Sum(c => c.Nombre * c.Bete.Agressivite * c.Faim * Math.Clamp(c.Bete.Poids / proie.Poids, 0.2, 2));
            if (menace <= 0) continue;
            var garde = gardien is not null && Math.Abs(gardien.X - x) <= 1 && Math.Abs(gardien.Y - y) <= 1;
            var chiens = _chiens.Contains(Maisonnee(t.CommunauteId, t.GardienId));
            var epines = humains.Any(h => h.EstVivant && (t.CommunauteId != 0 ? h.CommunauteId == t.CommunauteId : h.Id == t.GardienId)
                                          && h.Savoirs.Exists(s => s.Effet == Effet.Epines && s.Confiance >= 0.5));
            // Quelques bêtes sur cent par an, sans protection (estimation, d'après les pertes des éleveurs d'Afrique de l'Est).
            var risque = 0.0002 * menace * Math.Sqrt(t.Nombre) * (garde ? 0.5 : 1) * (chiens ? 0.4 : 1) * (epines ? 0.3 : 1);
            if (alea.Reel() >= risque) continue;
            var predateur = carnassiers.Where(c => !c.Bete.Eau).OrderByDescending(c => c.Nombre * c.Bete.Agressivite).First().Bete;
            var petit = t.Petits >= 1 && alea.Reel() < 0.6;
            Champs.Troupeaux[k] = petit ? t with { Petits = t.Petits - 1 } : t with { Adultes = Math.Max(0, t.Adultes - 1) };
            var temoin = gardien ?? humains.FirstOrDefault(h => h.EstVivant && h.CommunauteId == t.CommunauteId && t.CommunauteId != 0);
            if (temoin is not null)
                Noter(maintenant, TypeEvenement.Nature, temoin, null, new Recit(petit ? "fauve.troupeau.petit" : "fauve.troupeau", ("bete", predateur.Dit),
                    ("proie", proie.Dit), ("sans", Recit.Seul(chiens ? "fauve.troupeau.chiens" : garde ? "fauve.troupeau.garde" : "fauve.troupeau.seules"))), t.EvenementId);
        }
    }

    /// <summary>
    /// La bête qui a tué rôde encore : les proches de la victime, en colère et courageux, partent la traquer. Avec des épieux et
    /// des chiens, à plusieurs, ils la tuent souvent ; seuls, ils risquent d'y laisser la vie.
    /// </summary>
    private void Traquer(IReadOnlyList<Humain> humains, long maintenant)
    {
        foreach (var f in Fauves.ToList())
        {
            if (!f.EnVie || f.Victimes == 0 || maintenant - f.VuLe > 30 * Calendrier.SecondesParJour) continue;
            var b = Faune.Get(f.Espece);
            var chasseurs = new List<Humain>();
            foreach (var h in humains.Vivants())
            {
                if (!h.EstVivant || h.EstEnfant(maintenant) || Math.Abs(h.X - f.X) > 3 || Math.Abs(h.Y - f.Y) > 3 || h.PeurDesBetesEvenementId == 0) continue;
                var q = h.Personnalite;
                var envie = 0.02 * (h.Colere / 50 + h.Chagrin / 60) * (1.2 - q.Nevrosisme) * (0.5 + q.Endurance) * (1.3 - q.Prudence);
                if (alea.Reel() < envie) chasseurs.Add(h);
            }
            if (chasseurs.Count == 0) continue;
            // On ne part pas seul si d'autres de la case veulent venir.
            var meneur = chasseurs[0];
            foreach (var h in humains.Vivants())
                if (h.EstVivant && !chasseurs.Contains(h) && !h.EstEnfant(maintenant) && h.X == meneur.X && h.Y == meneur.Y && chasseurs.Count < 6
                    && alea.Reel() < 0.3 * (1 - h.Personnalite.Prudence)) chasseurs.Add(h);
            var armes = chasseurs.Count(h => (int)MondeSimule.ArmePortee(h) > 0);
            var chiens = _chiens.Contains(Maisonnee(meneur));
            var force = chasseurs.Count + 1.5 * armes + (chiens ? 2 : 0);
            var puissance = Math.Pow(b.Poids / 60, 0.6) * (0.7 + 0.6 * f.Audace) * (1 - 0.5 * f.Blessure) * (b.Eau ? 2 : 1) * 2;
            var depart = Noter(maintenant, TypeEvenement.Nature, meneur, null, new Recit(chasseurs.Count == 1 ? "fauve.traque.seul" : "fauve.traque",
                ("h", meneur), ("n", chasseurs.Count - 1), ("bete", b.Dit)), f.EvenementId);
            var trouvee = alea.Reel() < 0.5;
            if (!trouvee)
            {
                Noter(maintenant, TypeEvenement.Nature, meneur, null, new Recit("fauve.traque.rien", ("h", meneur), ("bete", b.Dit)), depart);
                continue;
            }
            if (alea.Reel() < force / (force + puissance))
            {
                var tueur = chasseurs.Find(h => (int)MondeSimule.ArmePortee(h) > 0) ?? meneur;
                var ev = Noter(maintenant, TypeEvenement.Nature, tueur, null, new Recit("fauve.traque.tuee", ("h", tueur), ("bete", b.Dit)), depart, significatif: true);
                Garder(f with { MortLe = maintenant, TueParId = tueur.Id, MortEvenementId = ev });
                Manquer(carte.Indice(f.X, f.Y), b);
                foreach (var h in chasseurs)
                {
                    h.PeurDesBetes *= 0.6;
                    h.Colere = Math.Max(0, h.Colere - 30);
                }
            }
            else
            {
                var blesse = chasseurs[alea.Entier(chasseurs.Count)];
                var g = 15 + 40 * alea.Reel();
                blesse.Sante -= g;
                Medecine.Blesser(blesse, g, maintenant, OrigineBlessure.Morsure);
                if (blesse.Sante <= 0 && !(Clemence > 0 && alea.Reel() < Clemence))
                {
                    Tuer(blesse, maintenant, CauseDeMort.Bete, new Recit("fauve.traque.mort", ("h", blesse), ("bete", b.Dit), ("age", AgeEnMots(blesse, maintenant))), depart);
                    MortsDeBetes++;
                }
                else
                {
                    if (blesse.Sante <= 0) blesse.Sante = 1;
                    Noter(maintenant, TypeEvenement.Medecine, blesse, null, new Recit("fauve.traque.blesse", ("h", blesse), ("bete", b.Dit)), depart);
                }
                Garder(f with { Audace = Math.Min(1, f.Audace + 0.05), VuLe = maintenant });
            }
        }
    }
}
