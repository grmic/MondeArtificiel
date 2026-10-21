using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Lien d'un humain avec un autre. Il est symétrique : les deux humains portent la même valeur.
/// </summary>
/// <param name="Familiarite">De 0 (inconnu) à 100 : monte avec le temps passé ensemble, baisse lentement sans se voir.</param>
/// <param name="Affection">De −100 (hostilité) à 100 (attachement profond).</param>
/// <param name="Derniere">Dernière journée passée ensemble.</param>
/// <param name="Amis">Vrai une fois l'amitié née (affection ≥ 40), jusqu'à ce qu'elle retombe sous 20.</param>
/// <param name="Rivaux">Vrai une fois l'hostilité née (affection ≤ −30), jusqu'à ce qu'elle remonte au-dessus de −10.</param>
/// <param name="Bienfait">Dernier geste de bonté de l'humain envers l'autre qui lui a valu du karma (0 : aucun). Voir <see cref="Regard"/>.</param>
public sealed record Lien(int Id, double Familiarite, double Affection, long Derniere, bool Amis = false, bool Rivaux = false, long Bienfait = 0);

/// <summary>
/// Un groupe d'humains liés entre eux qui vivent ensemble. Il n'est pas décrété : il est constaté
/// chaque semaine à partir des liens (voir <see cref="VieSociale.Regrouper"/>).
/// </summary>
/// <summary>Ce qu'une communauté pense des réserves.</summary>
public enum IdeeReserve : byte
{
    /// <summary>Pas d'idée établie : chacun puise dans la sienne et partage comme il partage le reste.</summary>
    Aucune = 0,
    /// <summary>La réserve est à tous : chacun y puise selon sa faim.</summary>
    Commune = 1,
    /// <summary>La réserve est à celui qui l'a faite : y toucher sans lui demander, c'est voler.</summary>
    ASoi = 2,
}

public sealed class Communaute
{
    public required int Id { get; init; }

    /// <summary>
    /// Nom affiché : d'abord celui que lui donne l'observateur (« Communauté de … », d'après sa figure centrale à sa naissance),
    /// puis « Communauté » suivi du nom que ses membres se donnent, quand ils en ont trouvé un.
    /// </summary>
    public required string Nom { get; set; }

    /// <summary>Nom que les membres se donnent (null tant qu'ils ne se sont pas nommés), et ce qu'il veut dire.</summary>
    public string? NomPropre { get; set; }
    public string? Sens { get; set; }

    /// <summary>Idée d'où vient le nom (« riviere », « eponyme », « gens »…), pour savoir quand il ne convient plus.</summary>
    public string? SensCle { get; set; }

    /// <summary>Humain dont le groupe porte le nom (0 : aucun).</summary>
    public int EponymeId { get; set; }

    /// <summary>
    /// Premiers mots du groupe (idée → mot) : ceux qui ont servi à le nommer. Une communauté née d'une scission garde
    /// les mots de celle qu'elle quitte ; c'est le germe de sa langue.
    /// </summary>
    public Dictionary<string, string> Lexique { get; set; } = [];

    /// <summary>Signes du groupe (idée → geste), pour qui parle avec les mains.</summary>
    public Dictionary<string, string> Signes { get; set; } = [];

    /// <summary>Sons de sa langue (consonnes, voyelles, finales, part des signes) ; tirés au premier mot.</summary>
    public Sons? Sons { get; set; }

    /// <summary>Stade de la langue (0 gestes, 1 mots isolés, 2 mots assemblés, 3 ordre fixe) et ordre des mots.</summary>
    public byte Stade { get; set; }
    public OrdreMots Ordre { get; set; }

    /// <summary>Quand la langue a eu son premier mot courant, ses premiers mots assemblés, son ordre des mots (0 : pas encore).</summary>
    public long ParoleLe { get; set; }
    public long CombinaisonLe { get; set; }
    public long OrdreLe { get; set; }

    /// <summary>Plus haut palier de mots déjà noté au journal (10, 25, 50, 100, 200).</summary>
    public int PalierMots { get; set; }

    /// <summary>Langue qu'elle parle : 0, la sienne ; sinon celle de la communauté dont elle est issue, tant que son parler n'en est qu'un dialecte.</summary>
    public int LangueId { get; set; }

    /// <summary>Histoire de la langue : chaque mot adopté, déformé, emprunté, remplacé ou oublié.</summary>
    public List<HistoireMot> HistoireLangue { get; set; } = [];

    /// <summary>Noms successifs, du premier au dernier.</summary>
    public List<NomCommunaute> Noms { get; set; } = [];

    /// <summary>Communauté d'où vient le groupe, s'il est né d'une scission (0 sinon).</summary>
    public int OrigineId { get; set; }

    /// <summary>Communauté que le groupe a décidé de rejoindre (0 : aucune), depuis quand, et l'événement de la décision.</summary>
    public int FusionVersId { get; set; }
    public long FusionDecideeLe { get; set; }
    public long FusionEvenementId { get; set; }

    /// <summary>Communauté dans laquelle le groupe s'est fondu (0 : il ne s'est fondu dans aucune).</summary>
    public int FusionneeDansId { get; set; }

    /// <summary>Dernière fois que le groupe a accueilli une autre communauté : ses nouveaux membres ne se connaissent pas encore.</summary>
    public long? AccueilLe { get; set; }

    /// <summary>Migrations menées à leur terme et disettes surmontées : ce que le groupe a traversé.</summary>
    public int Migrations { get; set; }
    public int DisettesSurmontees { get; set; }

    /// <summary>Dernière installation sur de nouvelles terres (événement du journal).</summary>
    public long InstallationEvenementId { get; set; }

    /// <summary>
    /// Terres parcourues : pour chaque case où un membre est passé, le jour du dernier passage et le nombre de jours
    /// de passage (voir <see cref="VieSociale.Passage"/>). Tant que personne n'a
    /// inventé la frontière, le territoire d'un groupe n'est rien d'autre que les terres qu'il fréquente.
    /// </summary>
    public Dictionary<int, long> Territoire { get; set; } = [];

    public required long FondeeLe { get; init; }
    public long? DisperseeLe { get; set; }

    /// <summary>Humain le plus influent du groupe : charismatique et aimé. Il n'a aucun titre, c'est un constat.</summary>
    public int FigureId { get; set; }

    public bool Active => DisperseeLe is null;

    /// <summary>Centre du groupe (position moyenne des adultes) et rayon de son territoire, en cases (mis à jour chaque semaine).</summary>
    public int CentreX { get; set; }
    public int CentreY { get; set; }
    public int Rayon { get; set; }

    /// <summary>Ressources autour du groupe rapportées à leur niveau naturel (1 : intactes, 0 : épuisées).</summary>
    public double Abondance { get; set; } = 1;

    /// <summary>Vrai tant que le groupe manque de nourriture (réserves de ses membres basses ou terres épuisées).</summary>
    public bool Disette { get; set; }

    /// <summary>Événement du journal qui a marqué le début de la disette en cours (cause des départs et des rivalités).</summary>
    public long DisetteEvenementId { get; set; }

    /// <summary>Terres vers lesquelles le groupe se déplace (−1 : il reste où il est).</summary>
    public int DestinationX { get; set; } = -1;
    public int DestinationY { get; set; } = -1;

    /// <summary>
    /// Tolérance du groupe envers ceux qui s'unissent hors de leur couple, de 0 (inacceptable) à 1 (indifférent).
    /// Elle naît du tempérament de ses membres et se forge avec les affaires qui le traversent : une trahison qui finit
    /// en drame rend le groupe plus sévère, une infidélité sans suite l'habitue (Buunk et Hupka, 1987 : la jalousie
    /// est universelle, mais ce qui la déclenche et ce qu'on en pense varient d'une société à l'autre).
    /// </summary>
    public double Tolerance { get; set; } = 0.5;

    /// <summary>Usage constaté : l'infidélité est mal vue, sans usage établi, ou indifférente.</summary>
    public UsageFidelite Usage { get; set; }

    /// <summary>Dernière affaire d'infidélité qui a marqué le groupe (événement du journal), cause des changements d'usage.</summary>
    public long DerniereAffaireId { get; set; }

    /// <summary>
    /// Tolérance du groupe envers les amours entre personnes de même sexe, de 0 (réprouvées) à 1 (acceptées). Elle naît du
    /// tempérament de ses membres (l'ouverture d'esprit en est le meilleur prédicteur : Shackelford et Besser, 2007) et
    /// change avec ce que le groupe vit : côtoyer ceux qui les vivent sans drame l'habitue (Herek et Capitanio, 1996), un
    /// scandale le durcit. Les sociétés humaines vont de l'acceptation au rejet (Ford et Beach, 1951 : la majorité des
    /// sociétés étudiées en admettaient certaines formes).
    /// </summary>
    public double ToleranceMoeurs { get; set; } = 0.5;

    /// <summary>Usage constaté envers ces amours, et la dernière affaire qui a marqué le groupe.</summary>
    public UsageMoeurs UsageMoeurs { get; set; }
    public long AffaireMoeursId { get; set; }

    /// <summary>Ce que le groupe trouve beau (taille, teint, corpulence) : il change avec les générations et les disettes.</summary>
    public IdealBeaute Ideal { get; set; } = IdealBeaute.ParDefaut;

    /// <summary>Corpulence admirée, telle qu'elle a été notée au journal (−1 : minces, 0 : ni l'un ni l'autre, 1 : en chair).</summary>
    public int IdealNote { get; set; }

    /// <summary>
    /// Goût du groupe pour la violence, de 0 (pacifique) à 1 (guerrier) ; −1 tant qu'il n'est pas établi. Il naît du
    /// tempérament de ses membres et se forge avec les raids subis, menés, gagnés ou payés cher.
    /// </summary>
    public double Bellicisme { get; set; } = -1;

    /// <summary>Raid en cours (null : aucun), et date du dernier raid lancé.</summary>
    public Raid? Raid { get; set; }
    public long DernierRaidLe { get; set; }

    /// <summary>Guerre que le groupe prépare (null : aucune) : elle sera débattue, puis lancée ou abandonnée.</summary>
    public ProjetGuerre? Projet { get; set; }

    /// <summary>Communauté à laquelle le groupe est soumis (0 : libre), depuis quand, et l'événement de la soumission.</summary>
    public int SoumiseAId { get; set; }
    public long SoumiseLe { get; set; }
    public long SoumissionEvenementId { get; set; }

    /// <summary>Dernière paix conclue (événement du journal).</summary>
    public long PaixEvenementId { get; set; }

    /// <summary>
    /// Après sa dispersion : la communauté qui la continue (0 : aucune), celle où vit désormais la plupart de ses membres
    /// et dont ils forment la majorité. Elle hérite de ses guerres et de sa soumission.
    /// </summary>
    public int HeritiereId { get; set; }

    /// <summary>Incendies partis d'un camp qui ont tué des siens : le groupe s'en souvient, et craint le feu près des herbes.</summary>
    public List<long> IncendiesSouvenus { get; set; } = [];

    /// <summary>
    /// Ce que le groupe pense des réserves, s'il en a une idée : à tous, ou à celui qui les a faites. Rien n'est décrété : l'idée
    /// naît de ce que le groupe vit (une disette surmontée en partageant, des vols), et peut changer.
    /// </summary>
    public IdeeReserve IdeeReserve { get; set; }
    public long IdeeReserveLe { get; set; }
    public long IdeeReserveEvenementId { get; set; }

    /// <summary>Repas pris dans la réserve d'un autre, sans vol, et réserves volées, depuis le début de la saison (ils s'estompent).</summary>
    public double PartagesReserve { get; set; }
    public double VolsReserve { get; set; }

    /// <summary>Dernier vol dans une réserve (événement du journal), cause possible de l'idée de la réserve à soi.</summary>
    public long VolReserveEvenementId { get; set; }

    public Communaute Copier()
    {
        var copie = (Communaute)MemberwiseClone();
        copie.IncendiesSouvenus = [.. IncendiesSouvenus];
        copie.Lexique = new Dictionary<string, string>(Lexique);
        copie.Signes = new Dictionary<string, string>(Signes);
        copie.HistoireLangue = [.. HistoireLangue];
        copie.Noms = [.. Noms];
        copie.Territoire = new Dictionary<int, long>(Territoire);
        copie.Raid = Raid?.Copier();
        copie.Projet = Projet?.Copier();
        return copie;
    }
}

/// <summary>
/// Liens, humeur, deuil et regroupements. Appelé une fois par jour par le monde (une fois par semaine pour les groupes).
/// Les valeurs de départ s'appuient sur la psychologie sociale : l'exposition répétée rend plus familier et plus
/// aimable (Zajonc, 1968), on s'attache davantage à qui nous ressemble (homophilie) ; l'agréabilité favorise les liens,
/// le névrosisme les conflits. Le nombre de liens qu'un humain entretient dépend de son extraversion
/// (la limite cognitive d'environ 150 relations de Dunbar n'est pas atteinte dans un si petit monde).
/// </summary>
public sealed partial class VieSociale(Alea alea)
{
    public const double SeuilAmitie = 40;
    public const double FinAmitie = 20;
    public const double SeuilHostilite = -30;
    public const double FinHostilite = -10;
    public const int JoursEntreRegroupements = 7;

    /// <summary>Au-delà de cette distance (en cases, une demi-journée de marche environ), deux humains ne vivent pas ensemble.</summary>
    public const int DistanceCommunaute = 6;

    /// <summary>Événements à verser au journal.</summary>
    public List<EvenementJournal> Journal => Tour.Courant?.Journal ?? _journal;

    private readonly List<EvenementJournal> _journal = [];

    private readonly Dictionary<int, int> _voisins = [];

    /// <summary>Partages de nourriture du jour (donneur, receveur), notés une fois par jour au journal.</summary>
    public HashSet<(int Donneur, int Receveur)> PartagesDuJour { get; } = [];

    /// <summary>Note un partage du jour (après le pas, pendant un pas simultané).</summary>
    public void Partager(Humain donneur, Humain receveur)
    {
        var partage = (donneur.Id, receveur.Id);
        Tour.Differer(() => PartagesDuJour.Add(partage));
    }

    /// <summary>
    /// Note un événement et renvoie son numéro, pour que ses suites puissent le citer comme cause. Sans communauté
    /// précisée, l'événement appartient à la chronologie de la communauté de <paramref name="h"/>.
    /// </summary>
    private long Noter(long maintenant, TypeEvenement type, Humain h, Humain? autre, string texte, long cause = 0,
        int communaute = -1, int autreCommunaute = 0, bool significatif = false, double karma = 0)
    {
        var e = new EvenementJournal(maintenant, type, h.Id, autre?.Id ?? 0, texte)
        {
            Id = Numeros.Suivant(), CauseId = cause, CommunauteId = communaute >= 0 ? communaute : h.CommunauteId,
            AutreCommunauteId = autreCommunaute, Significatif = significatif, Karma = Regard.Juger(h, karma, maintenant, autre),
        };
        Journal.Add(e);
        return e.Id;
    }

    private static string E(Humain h) => h.Sexe == Sexe.Femme ? "e" : "";

    public static int LiensMax(Humain h) => 10 + (int)(40 * h.Personnalite.Extraversion);

    // ───────────── Liens ─────────────

    /// <summary>Une journée : ceux qui sont proches l'un de l'autre se lient ; les autres liens s'estompent.</summary>
    public void Journee(List<Humain> humains, long maintenant)
    {
        var presents = humains.Vivants().Where(h => h.EstVivant && !h.EstBebe(maintenant)).ToList();
        // Nombre d'humains à moins de 4 cases de chacun : la foule crée des frictions.
        _voisins.Clear();
        foreach (var h in presents)
        {
            h.CompagnieDuJour = 0;
            _voisins[h.Id] = 0;
        }
        for (var i = 0; i < presents.Count; i++)
        {
            for (var j = i + 1; j < presents.Count; j++)
            {
                if (Math.Abs(presents[i].X - presents[j].X) > 4 || Math.Abs(presents[i].Y - presents[j].Y) > 4) continue;
                _voisins[presents[i].Id]++;
                _voisins[presents[j].Id]++;
            }
        }

        for (var i = 0; i < presents.Count; i++)
        {
            var a = presents[i];
            for (var j = i + 1; j < presents.Count; j++)
            {
                var b = presents[j];
                if (Math.Abs(a.X - b.X) > 2 || Math.Abs(a.Y - b.Y) > 2) continue;
                Rapprocher(a, b, maintenant);
            }
        }

        foreach (var h in presents)
        {
            Estomper(h, maintenant);
            Limiter(h);
        }

        foreach (var (donneur, receveur) in PartagesDuJour.OrderBy(p => p.Donneur).ThenBy(p => p.Receveur))
        {
            var d = humains[donneur - 1];
            var r = humains[receveur - 1];
            Noter(maintenant, TypeEvenement.Partage, d, r, $"{d.Nom} partage sa nourriture avec {r.Nom}.", karma: Regard.Partage);
        }
        PartagesDuJour.Clear();
    }

    private void Rapprocher(Humain a, Humain b, long maintenant)
    {
        var la = a.LienAvec(b.Id);
        var pa = a.Personnalite;
        var pb = b.Personnalite;

        // Compatibilité : bienveillance des deux, ressemblance, et peu d'humeurs à vif.
        var bienveillance = (pa.Agreabilite + pb.Agreabilite) / 2 - 0.5;
        // Écart moyen entre deux humains pris au hasard : environ 0,17 par trait ; plus proches que ça, ils se ressemblent.
        var ressemblance = 0.17 - (Math.Abs(pa.Extraversion - pb.Extraversion) + Math.Abs(pa.Ouverture - pb.Ouverture)
                                   + Math.Abs(pa.Transcendance - pb.Transcendance)) / 3;
        var tension = (pa.Nevrosisme + pb.Nevrosisme) / 2 - 0.5;
        // Trop nombreux au même endroit, les frictions s'accumulent (les bandes de chasseurs-cueilleurs comptent
        // en général 20 à 50 personnes et se scindent au-delà ; Marlowe, 2005).
        var foule = Math.Max(0, Math.Max(_voisins[a.Id], _voisins[b.Id]) - 25) * 0.04;
        var delta = 0.3 + 3 * bienveillance + 6 * ressemblance - 2 * tension - foule + 0.6 * Biologie.Normale(alea);

        // La famille et le couple soudent ; la faim partagée attise les querelles.
        if (Parents(a, b)) delta += 1.0;
        if (a.PartenaireId == b.Id) delta += 1.5;
        if (a.Faim > 60 && b.Faim > 60) delta -= 1.5 * (1 - (pa.Agreabilite + pb.Agreabilite) / 2);
        // Quand les réserves s'épuisent, chacun voit en l'autre un rival pour la nourriture.
        if (a.Reserves < 25 && b.Reserves < 25) delta -= 0.7;
        // Entre membres de deux communautés, la relation entre les groupes colore la rencontre.
        if (a.CommunauteId != 0 && b.CommunauteId != 0 && a.CommunauteId != b.CommunauteId)
        {
            delta += RelationEntre(a.CommunauteId, b.CommunauteId) switch
            {
                EtatRelation.Alliees => 0.5,
                EtatRelation.Rivales => -1.0,
                EtatRelation.Hostiles => -1.5,
                EtatRelation.EnGuerre => -2.0,
                _ => 0,
            };
        }

        // Les adultes supportent les enfants, et les enfants entre eux se fâchent et se réconcilient vite :
        // une vraie hostilité ne se forme guère avant l'adolescence.
        var plusJeune = Math.Min(a.AgeEnAnnees(maintenant), b.AgeEnAnnees(maintenant));
        if (plusJeune < 14 && delta < 0) delta *= plusJeune / 14;

        var avant = la ?? new Lien(b.Id, 0, 0, maintenant);
        var apres = avant with
        {
            Familiarite = Math.Min(100, avant.Familiarite + 1),
            Affection = Math.Clamp(avant.Affection + delta, -100, 100),
            Derniere = maintenant,
        };
        apres = Seuils(a, b, avant, apres, maintenant);
        Poser(a, apres);
        Poser(b, apres with { Id = a.Id });

        if (apres.Affection >= 20)
        {
            a.CompagnieDuJour++;
            b.CompagnieDuJour++;
        }
    }

    private Lien Seuils(Humain a, Humain b, Lien avant, Lien apres, long maintenant)
    {
        if (!avant.Amis && apres.Affection >= SeuilAmitie && apres.Familiarite >= 20)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} se sont attachés l'un à l'autre.");
            return apres with { Amis = true };
        }
        if (avant.Amis && apres.Affection < FinAmitie)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} se sont éloignés.");
            return apres with { Amis = false };
        }
        if (!avant.Rivaux && apres.Affection <= SeuilHostilite)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} ne se supportent plus.");
            return apres with { Rivaux = true };
        }
        if (avant.Rivaux && apres.Affection > FinHostilite)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} ont fait la paix.");
            return apres with { Rivaux = false };
        }
        return apres;
    }

    /// <summary>Sans se voir, on se connaît de moins en moins et les sentiments s'atténuent.</summary>
    private static void Estomper(Humain h, long maintenant)
    {
        for (var i = h.Liens.Count - 1; i >= 0; i--)
        {
            var l = h.Liens[i];
            if (l.Derniere >= maintenant - Calendrier.SecondesParJour / 2) continue;
            var estompe = l with { Familiarite = Math.Max(0, l.Familiarite - 0.1), Affection = l.Affection * 0.997 };
            if (estompe.Familiarite <= 0 && Math.Abs(estompe.Affection) < 5) h.Liens.RemoveAt(i);
            else h.Liens[i] = estompe;
        }
    }

    /// <summary>Au-delà de ce qu'il peut entretenir, l'humain perd le lien le plus faible.</summary>
    private static void Limiter(Humain h)
    {
        var max = LiensMax(h);
        while (h.Liens.Count > max)
        {
            h.Liens.Remove(h.Liens.MinBy(l => (l.Familiarite + Math.Abs(l.Affection), l.Derniere))!);
        }
    }

    /// <summary>Pose un lien vu de <paramref name="h"/> : le dernier bienfait reste le sien (il n'est pas copié de l'autre côté).</summary>
    private static void Poser(Humain h, Lien lien)
    {
        var i = h.Liens.FindIndex(l => l.Id == lien.Id);
        if (i >= 0) h.Liens[i] = lien with { Bienfait = h.Liens[i].Bienfait };
        else h.Liens.Add(lien with { Bienfait = 0 });
    }

    /// <summary>Parent et enfant, ou frères et sœurs (même mère).</summary>
    public static bool Parents(Humain a, Humain b) =>
        a.MereId == b.Id || a.PereId == b.Id || b.MereId == a.Id || b.PereId == a.Id
        || (a.MereId != 0 && a.MereId == b.MereId);

    /// <summary>Un geste de bonté rapproche, surtout aux yeux de celui qui reçoit.</summary>
    public static void Gratitude(Humain donneur, Humain receveur, double force)
    {
        if (Tour.Autrui(donneur) || Tour.Autrui(receveur))
        {
            Tour.Differer(donneur, receveur, (d, r) => Gratitude(d, r, force));
            return;
        }
        var l = receveur.LienAvec(donneur.Id);
        if (l is null) return;
        var affection = Math.Min(100, l.Affection + force);
        Poser(receveur, l with { Affection = affection });
        if (donneur.LienAvec(receveur.Id) is { } d) Poser(donneur, d with { Affection = affection });
    }

    // ───────────── Humeur et deuil ─────────────

    /// <summary>
    /// Humeur du jour : elle tend vers un niveau propre à chacun (plus haut chez les extravertis,
    /// plus bas chez les anxieux), abaissé par l'inconfort, le chagrin, la solitude, le rejet, la trahison et la colère,
    /// relevé par la compagnie de ceux qu'on aime. Chaque poids est gardé comme raison de l'humeur : on sait toujours
    /// pourquoi un humain va mal. La solitude ressentie pèse sur l'humeur et mène à la dépression (Cacioppo et al., 2006),
    /// davantage chez les extravertis ; l'exclusion par les autres blesse, surtout les plus sensibles (Williams, 2007).
    /// </summary>
    public void Humeurs(List<Humain> humains, long maintenant)
    {
        const double pasParJour = Calendrier.SecondesParJour / MondeSimule.SecondesParPas;
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant) continue;
            var p = h.Personnalite;
            var inconfort = h.InconfortDuJour / pasParJour;
            var faimSoif = h.FaimSoifDuJour / pasParJour;
            var froid = h.FroidDuJour / pasParJour;
            h.InconfortDuJour = h.FaimSoifDuJour = h.FroidDuJour = 0;
            if (h.EstBebe(maintenant)) continue;

            Rancunes(h, humains, maintenant);
            h.JoursSeul = h.CompagnieDuJour > 0 ? 0 : h.JoursSeul + 1;

            var base_ = 50 + 25 * (p.Extraversion - 0.5) - 40 * (p.Nevrosisme - 0.5);
            var compagnie = 3 * Math.Min(4, h.CompagnieDuJour);
            var hostiles = Hostiles(h, maintenant);
            var trahison = h.Griefs.Where(g => g.Motif == MotifGrief.Infidelite).MaxBy(g => g.Intensite);
            Span<double> poids =
            [
                45 - base_,                                                        // tempérament (déjà dans base_)
                25 * faimSoif,
                25 * froid,
                25 * Math.Max(0, inconfort - faimSoif - froid),
                (0.2 + 0.4 * p.Nevrosisme) * h.Chagrin,                            // le chagrin pèse plus chez les sensibles
                Math.Min(8, 0.25 * h.JoursSeul) * (0.3 + 1.0 * p.Extraversion),
                Math.Min(12, 2.5 * hostiles.Count) * (0.6 + 0.8 * p.Nevrosisme),
                0.25 * (trahison?.Intensite ?? 0),
                0.25 * h.Colere,
                (0.15 + 0.35 * p.Nevrosisme) * h.Peur,                             // la peur d'une catastrophe
            ];
            TypeRaison[] types = [TypeRaison.Temperament, TypeRaison.FaimSoif, TypeRaison.Froid, TypeRaison.Maladie, TypeRaison.Deuil,
                TypeRaison.Solitude, TypeRaison.Rejet, TypeRaison.Trahison, TypeRaison.Colere, TypeRaison.Peur];
            var peines = 0.0;
            for (var i = 1; i < poids.Length; i++) peines += poids[i];
            var cible = base_ + compagnie - peines;
            h.Humeur = Math.Clamp(h.Humeur + (cible - h.Humeur) * 0.3, 0, 100);

            h.Raisons.Clear();
            for (var i = 0; i < poids.Length; i++)
            {
                if (poids[i] < 3) continue;
                var autre = types[i] switch
                {
                    TypeRaison.Deuil => h.DeuilDeId,
                    TypeRaison.Rejet => hostiles[0],
                    TypeRaison.Trahison => trahison!.ContreId,
                    TypeRaison.Peur => (int)h.PeurType,
                    _ => 0,
                };
                h.Raisons.Add(new Raison(types[i], autre, Math.Round(poids[i], 1)));
            }
            h.Raisons.Sort((a, b) => b.Poids.CompareTo(a.Poids));
            if (h.Raisons.Count > 4) h.Raisons.RemoveRange(4, h.Raisons.Count - 4);

            // Le chagrin s'apaise : en un mois environ pour la plupart, en des mois voire des années chez les plus
            // sensibles (trajectoires du deuil de Bonanno, 2004 : résilience majoritaire, deuil chronique pour 10 à 15 %).
            var demiVie = 30 * Math.Exp(5 * (p.Nevrosisme - 0.5));
            h.Chagrin *= Math.Pow(0.5, 1 / demiVie);
            if (h.Chagrin < 1)
            {
                h.Chagrin = 0;
                h.DeuilDeId = 0;
                h.DeuilEvenementId = 0;
            }

            if (h.Humeur < 20) h.JoursSombres++;
            else if (h.Humeur > 35) h.JoursSombres = 0;

            // Trois semaines au plus mal : une tristesse ordinaire, même un deuil, ne suffit pas d'habitude.
            if (!h.EstDeprime && h.JoursSombres >= 21 && !h.EstEnfant(maintenant))
            {
                h.DeprimeDepuis = maintenant;
                var raisons = Expliquer(h, humains, maintenant);
                h.DepressionEvenementId = Noter(maintenant, TypeEvenement.Humeur, h, null,
                    $"{h.Nom} sombre dans une profonde tristesse{(raisons.Length > 0 ? " : " + raisons : "")}.", CauseDe(h, h.Raisons.FirstOrDefault()));
            }
            // Un épisode dépressif dure au moins deux semaines (critère du DSM-5) et ne cède qu'à un vrai mieux.
            else if (h.EstDeprime && h.Humeur > 35 && maintenant - h.DeprimeDepuis >= 14 * Calendrier.SecondesParJour)
            {
                h.DeprimeDepuis = null;
                h.JoursSombres = 0;
                Noter(maintenant, TypeEvenement.Humeur, h, null, $"{h.Nom} reprend goût à la vie.", h.DepressionEvenementId);
                h.DepressionEvenementId = 0;
            }
        }
    }

    /// <summary>
    /// Risque quotidien de suicide d'un adulte déprimé : environ 0,2 % par an, plus fort avec le chagrin.
    /// (Ordre de grandeur : un taux de 10 à 15 pour 100 000 par an dans la population, très concentré
    /// chez les personnes déprimées.)
    /// </summary>
    public double RisqueDesespoir(Humain h, long maintenant) =>
        h.EstDeprime && h.AgeEnAnnees(maintenant) >= 15
            ? 0.002 / Calendrier.JoursParAn * (1 + h.Chagrin / 50) * (0.6 + 0.8 * h.Personnalite.Nevrosisme)
            : 0;

    /// <summary>
    /// Une mort endeuille ceux qui tenaient au disparu : son compagnon, ses parents (selon leur attachement),
    /// ses enfants, ses frères et sœurs, ses amis, et un peu toute sa communauté.
    /// </summary>
    public void Endeuiller(Humain mort, List<Humain> humains, long maintenant)
    {
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Id == mort.Id || h.EstBebe(maintenant)) continue;
            double k = 0;
            if (h.PartenaireId == mort.Id || mort.PartenaireId == h.Id) k = 70;
            if (mort.MereId == h.Id || mort.PereId == h.Id)
            {
                k = Math.Max(k, 100 * h.Personnalite.Attachement * (mort.AgeEnAnnees(maintenant) < 1 ? 0.7 : 1));
            }
            if (h.MereId == mort.Id || h.PereId == mort.Id) k = Math.Max(k, h.EstEnfant(maintenant) ? 70 : 45);
            if (h.MereId != 0 && h.MereId == mort.MereId) k = Math.Max(k, 40);
            var affection = h.AffectionPour(mort.Id);
            if (affection > 0) k = Math.Max(k, 0.7 * affection);
            // Toute la communauté est touchée, les cœurs compatissants davantage.
            var p = h.Personnalite;
            var voisin = 8 * (0.5 + p.Moral.Compassion);
            if (k < voisin && h.CommunauteId != 0 && h.CommunauteId == mort.CommunauteId) k = voisin;
            if (k <= 0) continue;

            k *= 0.6 + 0.8 * p.Nevrosisme; // les plus sensibles souffrent davantage
            // L'empathie émotionnelle fait ressentir la perte ; l'attachement anxieux la rend plus douloureuse, l'évitant la tient
            // à distance (Fraley et Shaver, 1999) ; le psychopathe n'éprouve presque rien (Cleckley, 1941 ; Hare, 1993).
            k *= (0.75 + 0.5 * p.Moral.EmpathieEmotionnelle) * (1 + 0.6 * (p.Moral.AnxieteAffective - 0.5) - 0.6 * (p.Moral.EvitementAffectif - 0.5))
                 * (1 - 0.7 * Psyche.Froideur(p));
            if (k > h.Chagrin)
            {
                h.DeuilDeId = mort.Id;
                h.DeuilEvenementId = mort.MortEvenementId;
            }
            h.Chagrin = Math.Min(100, h.Chagrin + k);
            h.Humeur = Math.Max(0, h.Humeur - k / 2);
            if (k >= 25)
            {
                Noter(maintenant, TypeEvenement.Humeur, h, mort, $"{h.Nom} pleure {Parente(h, mort)}.", mort.MortEvenementId);
                // Penser à la mort pousse à croire qu'il y a autre chose (Jong et al., 2012) : d'autant plus que le deuil est lourd.
                Croyances.Ajouter(h, k / 25 * (0.3 + p.Transcendance), $"la mort de {Parente(h, mort)}", mort.MortEvenementId, maintenant);
                Jardin.Pleurer(h, mort, Parente(h, mort), k, maintenant);
            }
            // La perte d'un très proche peut changer durablement (Jeronimus et al., 2014).
            if (k >= 50) Jardin.Marquer(h, Epreuve.Deuil, (k - 40) / 60, mort.MortEvenementId, maintenant, alea);

            // Un proche tué : la peine se double d'une soif de vengeance contre le meurtrier (Daly et Wilson, 1988).
            if (mort.CauseDeMort is CauseDeMort.Meurtre or CauseDeMort.Guerre && mort.MeurtrierId > 0 && mort.MeurtrierId != h.Id && k >= 20
                && humains[mort.MeurtrierId - 1] is { EstVivant: true } meurtrier)
            {
                var soif = Math.Min(100, 1.2 * k);
                AjouterGrief(h, new Grief(meurtrier.Id, MotifGrief.Meurtre, soif, soif, mort.MortEvenementId, mort.Id));
                h.Colere = Math.Max(h.Colere, k);
                Jardin.Confier(h, TypeIntime.Intention, meurtrier.Id, $"Je vengerai {mort.Nom} : {meurtrier.Nom} paiera.", soif * (0.5 + 0.5 * (1 - h.Personnalite.Agreabilite)),
                    mort.MortEvenementId, maintenant, $"vengeance:{meurtrier.Id}");
            }
        }
    }

    /// <summary>Une joie : naissance d'un enfant, nouveau compagnon…</summary>
    public static void Rejouir(Humain h, double force) => Tour.Changer(h, x => x.Humeur = Math.Min(100, x.Humeur + force));

    // ───────────── Communautés ─────────────

    /// <summary>
    /// Une fois par semaine : les humains reliés par des liens d'affection forment des groupes.
    /// Il faut au moins 5 humains dont 3 adultes pour fonder une communauté ; elle dure ensuite tant que deux de ses adultes
    /// au moins vivent encore ensemble et qu'elle conserve au moins la moitié de ses membres ; sinon elle se disperse,
    /// ou se fond dans celle qui a accueilli la plupart d'entre eux. Le campement change souvent chez les chasseurs-cueilleurs,
    /// l'appartenance au groupe beaucoup moins (Hill et al., 2011).
    /// Il faut une amitié pour entrer, un simple lien d'affection pour rester (évite les va-et-vient),
    /// et les deux humains doivent vivre à moins de <see cref="DistanceCommunaute"/> cases l'un de l'autre ; celui qui
    /// s'éloigne un temps (cueillette, exploration) reste des leurs (voir <see cref="AbsenceToleree"/>).
    /// Des anciens d'une communauté dispersée qui se retrouvent la font renaître.
    /// Deux groupes qui ont décidé de s'unir restent unis le temps que leurs membres apprennent à se connaître.
    /// </summary>
    public void Regrouper(List<Humain> humains, List<Communaute> communautes, long maintenant, ISet<string> noms)
    {
        var membres = humains.Vivants().Where(h => h.EstVivant).ToList();
        var parent = new Dictionary<int, int>();
        int Racine(int id)
        {
            while (parent[id] != id) id = parent[id] = parent[parent[id]];
            return id;
        }
        void Unir(int a, int b) => parent[Racine(a)] = Racine(b);
        foreach (var h in membres) parent[h.Id] = h.Id;
        // Les guerriers partis en raid restent membres de leur groupe, même loin du camp.
        var enRaid = communautes.Where(c => c.Active && c.Raid is not null).SelectMany(c => c.Raid!.Guerriers).ToHashSet();
        // Un groupe en guerre se serre les coudes : la menace commune renforce la cohésion du groupe (Bauer et al., 2016).
        var actifs = communautes.Where(c => c.Active).Select(c => c.Id).ToList();
        var enGuerre = actifs.Where(a => actifs.Any(b => b != a && RelationEntre(a, b) == EtatRelation.EnGuerre)).ToHashSet();

        foreach (var h in membres)
        {
            // Bébés et enfants vont avec leurs parents ; un orphelin, avec celui qui l'a recueilli.
            if (h.EstEnfant(maintenant))
            {
                // Parents séparés par un exil ou un reniement : l'enfant reste avec sa mère.
                var separes = h.MereId > 0 && h.PereId > 0 && Bannissement.Separes(humains[h.MereId - 1], humains[h.PereId - 1], maintenant);
                foreach (var pid in separes && humains[h.MereId - 1].EstVivant ? [h.MereId] : new[] { h.MereId, h.PereId })
                {
                    if (pid > 0 && humains[pid - 1].EstVivant) Unir(h.Id, pid);
                }
                if (Comportement.Gardien(h, humains, maintenant) is { } gardien && gardien.Id != h.MereId && gardien.Id != h.PereId)
                    Unir(h.Id, gardien.Id);
                continue;
            }
            foreach (var l in h.Liens)
            {
                if (l.Id < h.Id || !parent.ContainsKey(l.Id)) continue;
                var autre = humains[l.Id - 1];
                if (autre.EstEnfant(maintenant) || Bannissement.Separes(h, autre, maintenant)) continue;
                var dejaEnsemble = h.CommunauteId != 0 && h.CommunauteId == autre.CommunauteId;
                // Une communauté vit ensemble : des amis qui vivent loin l'un de l'autre n'en font pas une. Mais celui qui
                // s'est éloigné depuis peu des siens reste des leurs.
                if (Math.Max(Math.Abs(h.X - autre.X), Math.Abs(h.Y - autre.Y)) > DistanceCommunaute
                    && !enRaid.Contains(h.Id) && !enRaid.Contains(autre.Id)
                    && !(dejaEnsemble && maintenant - l.Derniere <= AbsenceToleree(h, autre, l))) continue;
                // Tant que la guerre dure, on ne passe pas chez l'ennemi, même par amitié.
                if (h.CommunauteId != autre.CommunauteId && h.CommunauteId != 0 && autre.CommunauteId != 0
                    && RelationEntre(h.CommunauteId, autre.CommunauteId) == EtatRelation.EnGuerre) continue;
                // Pour entrer, il faut une amitié ; pour rester, un lien d'affection suffit (ou, en guerre, de ne pas se détester).
                // Le loyal reste avec les siens même quand les liens s'usent, et plus encore dans la disette ; le peu loyal
                // s'en va quand le groupe ne le nourrit plus.
                var loyaute = (h.Personnalite.Moral.Fondements.Loyaute + autre.Personnalite.Moral.Fondements.Loyaute) / 2;
                var disette = dejaEnsemble && communautes[h.CommunauteId - 1].Disette;
                var attache = enGuerre.Contains(h.CommunauteId) ? 0
                    : Math.Max(0, 15 * (1.5 - loyaute) + (disette ? 20 * (0.5 - loyaute) : 0));
                if (dejaEnsemble ? l.Affection >= attache && l.Familiarite >= 20 : l.Amis) Unir(h.Id, l.Id);
            }
        }
        Rassembler(humains, membres, communautes, maintenant, Unir);

        var groupes = membres.GroupBy(h => Racine(h.Id))
            .Select(g => g.ToList())
            .Where(g => g.Count(h => !h.EstEnfant(maintenant)) >= 2)
            .OrderByDescending(g => g.Count).ThenBy(g => g.Min(h => h.Id))
            .ToList();

        var anciens = membres.ToDictionary(h => h.Id, h => h.CommunauteId);
        var tailles = membres.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => g.Count());
        var reprises = new HashSet<int>();
        var nouvelles = new Dictionary<int, int>(); // humain → communauté
        var renees = new HashSet<int>();
        foreach (var g in groupes)
        {
            // La communauté d'avant la mieux représentée dans le groupe, si elle y garde au moins la moitié de ses membres.
            var candidate = g.Where(h => h.CommunauteId != 0 && !reprises.Contains(h.CommunauteId))
                .GroupBy(h => h.CommunauteId)
                .Select(c => (Id: c.Key, Nombre: c.Count()))
                .OrderByDescending(c => c.Nombre).ThenBy(c => c.Id)
                .FirstOrDefault();
            Communaute communaute;
            var fondatrice = g.Count >= 5 && g.Count(h => !h.EstEnfant(maintenant)) >= 3;
            if (candidate.Id != 0 && candidate.Nombre * 2 >= tailles[candidate.Id])
            {
                communaute = communautes[candidate.Id - 1];
                reprises.Add(communaute.Id);
            }
            else if (!fondatrice)
            {
                // Trop peu nombreux pour fonder une communauté : ils restent sans groupe.
                continue;
            }
            else if (Renaissante(g, communautes, reprises, maintenant) is { } ancienne)
            {
                communaute = ancienne;
                communaute.DisperseeLe = null;
                communaute.HeritiereId = 0;
                reprises.Add(communaute.Id);
                renees.Add(communaute.Id);
                var figure = Figure(g, maintenant);
                Noter(maintenant, TypeEvenement.Communaute, figure, null,
                    $"Les anciens de la {Minuscule(communaute.Nom)} se retrouvent autour de {figure.Nom} : la communauté renaît, {g.Count} humains vivent de nouveau ensemble.",
                    communaute: communaute.Id);
            }
            else
            {
                var figure = Figure(g, maintenant);
                var nom = $"Communauté de {figure.Nom}";
                // Les homonymes se comptent sur le premier nom de chaque communauté : celles qui se sont nommées depuis comptent encore.
                var homonymes = communautes.Select(c => c.Noms.Count > 0 ? c.Noms[0].Nom : c.Nom)
                    .Count(n => n == nom || n.StartsWith(nom + " (", StringComparison.Ordinal));
                var tolerance = ToleranceInitiale(g, communautes, maintenant);
                var (toleranceMoeurs, ideal) = MoeursInitiales(g, communautes, maintenant);
                // Groupe venu en majorité d'une même communauté : c'est une scission.
                var origine = g.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId)
                    .Select(c => (Id: c.Key, Nombre: c.Count())).OrderByDescending(c => c.Nombre).ThenBy(c => c.Id).FirstOrDefault();
                var scission = origine.Id != 0 && origine.Nombre * 2 > g.Count;
                communaute = new Communaute
                {
                    Id = communautes.Count + 1,
                    Nom = homonymes == 0 ? nom : $"{nom} ({homonymes + 1})",
                    FondeeLe = maintenant,
                    FigureId = figure.Id,
                    Tolerance = tolerance,
                    Usage = Classer(tolerance, UsageFidelite.Neutre),
                    ToleranceMoeurs = toleranceMoeurs,
                    UsageMoeurs = ClasserMoeurs(toleranceMoeurs, UsageMoeurs.Neutre),
                    Ideal = ideal,
                    IdealNote = NoteIdeal(ideal.Corpulence, 0),
                    OrigineId = scission ? origine.Id : 0,
                    // Ceux qui partent emportent les mots de leur ancien groupe.
                    Lexique = scission ? new Dictionary<string, string>(communautes[origine.Id - 1].Lexique) : [],
                    Signes = scission ? new Dictionary<string, string>(communautes[origine.Id - 1].Signes) : [],
                    // Même sons, même stade au départ : un dialecte, qui dérivera.
                    Sons = scission ? communautes[origine.Id - 1].Sons : null,
                    Stade = scission ? communautes[origine.Id - 1].Stade : (byte)0,
                    Ordre = scission ? communautes[origine.Id - 1].Ordre : OrdreMots.Aucun,
                    LangueId = scission ? Langues.LangueDe(communautes[origine.Id - 1]) : 0,
                };
                communautes.Add(communaute);
                reprises.Add(communaute.Id);
                var id = Noter(maintenant, TypeEvenement.Communaute, figure, null, scission
                    ? $"{g.Count} humains quittent la {Minuscule(communautes[origine.Id - 1].Nom)} et se rassemblent autour de {figure.Nom}."
                    : $"Une communauté se forme autour de {figure.Nom} : {g.Count} humains vivent désormais ensemble.",
                    communaute: communaute.Id, autreCommunaute: scission ? origine.Id : 0);
                communaute.Noms.Add(new NomCommunaute(communaute.Nom, null, maintenant, id));
            }
            foreach (var h in g) nouvelles[h.Id] = communaute.Id;

            var nouvelleFigure = Figure(g, maintenant);
            if (nouvelleFigure.Id != communaute.FigureId)
            {
                communaute.FigureId = nouvelleFigure.Id;
                Noter(maintenant, TypeEvenement.Communaute, nouvelleFigure, null,
                    $"{nouvelleFigure.Nom} devient la figure centrale de la {Minuscule(communaute.Nom)}.", communaute: communaute.Id);
            }
        }

        var fondues = new Dictionary<int, int>(); // communauté → celle qui a accueilli ses membres
        foreach (var c in communautes.Where(c => c.Active && !reprises.Contains(c.Id)))
        {
            c.DisperseeLe = maintenant;
            var figure = humains[c.FigureId - 1];
            // La plupart de ses membres vivent désormais dans une même communauté : le groupe s'y est fondu.
            var accueil = membres.Where(h => anciens[h.Id] == c.Id && nouvelles.ContainsKey(h.Id))
                .GroupBy(h => nouvelles[h.Id]).Select(x => (Id: x.Key, Nombre: x.Count()))
                .OrderByDescending(x => x.Nombre).ThenBy(x => x.Id).FirstOrDefault();
            // Ses membres forment la majorité d'un groupe : c'est elle, sous un autre nom (souvent après des morts ou des départs).
            if (accueil.Id != 0 && accueil.Nombre * 2 > nouvelles.Values.Count(v => v == accueil.Id)) c.HeritiereId = accueil.Id;
            if (accueil.Id != 0 && accueil.Nombre * 2 >= tailles.GetValueOrDefault(c.Id))
            {
                var cible = communautes[accueil.Id - 1];
                // Un petit groupe parti depuis quelques semaines qui revient d'où il vient n'a pas fait de fusion : il n'a pas duré.
                if (maintenant - c.FondeeLe < AgeAvantDeSeNommer && c.FusionEvenementId == 0)
                {
                    Noter(maintenant, TypeEvenement.Communaute, figure, null,
                        $"La {Minuscule(c.Nom)} ne dure pas : {accueil.Nombre} de ses membres vivent de nouveau avec la {Minuscule(cible.Nom)}.",
                        communaute: cible.Id, autreCommunaute: c.Id);
                }
                else
                {
                    Fondre(c, cible, figure, accueil.Nombre, maintenant);
                }
                fondues[c.Id] = cible.Id;
                c.Territoire.Clear();
                continue;
            }
            Noter(maintenant, TypeEvenement.Communaute, figure, null, $"La {Minuscule(c.Nom)} se disperse.", communaute: c.Id);
            c.Territoire.Clear();
        }

        // Un enfant part ou arrive avec celui qui veille sur lui : il est nommé dans sa ligne, pas dans une ligne à part.
        var suivent = new Dictionary<int, List<Humain>>();
        foreach (var h in membres.Where(h => h.EstEnfant(maintenant) && anciens[h.Id] != nouvelles.GetValueOrDefault(h.Id)))
        {
            if (Comportement.Gardien(h, humains, maintenant) is { } g && anciens.TryGetValue(g.Id, out var gAvant)
                && gAvant == anciens[h.Id] && nouvelles.GetValueOrDefault(g.Id) == nouvelles.GetValueOrDefault(h.Id))
            {
                (suivent.TryGetValue(g.Id, out var l) ? l : suivent[g.Id] = []).Add(h);
            }
        }
        var accompagnes = suivent.Values.SelectMany(l => l).Select(h => h.Id).ToHashSet();
        string Avec(Humain h) => suivent.TryGetValue(h.Id, out var l) ? AvecEnfants(h, l, maintenant) : "";

        foreach (var h in membres)
        {
            var avant = anciens[h.Id];
            var apres = nouvelles.GetValueOrDefault(h.Id);
            h.CommunauteId = apres;
            if (apres != 0) h.AncienneCommunauteId = 0;
            else if (avant != 0) h.AncienneCommunauteId = avant;
            if (avant == apres || accompagnes.Contains(h.Id) || h.EstBebe(maintenant)) continue;
            // Les arrivées dans une communauté qui vient de naître (ou de renaître) sont déjà dans l'annonce de sa formation,
            // celles d'une fusion dans l'annonce de la fusion.
            if (apres != 0 && communautes[apres - 1].FondeeLe != maintenant && !renees.Contains(apres)
                && fondues.GetValueOrDefault(avant) != apres)
            {
                var arrivee = Noter(maintenant, TypeEvenement.Communaute, h, null, $"{h.Nom} rejoint la {Minuscule(communautes[apres - 1].Nom)}{Avec(h)}{AgeSiEnfant(h, maintenant)}.",
                    communaute: apres, autreCommunaute: avant);
                if (avant != 0 && communautes[avant - 1].Active && !h.EstEnfant(maintenant)
                    && RelationEntre(avant, apres) is EtatRelation.Rivales or EtatRelation.Hostiles)
                    Trahir(h, communautes[avant - 1], communautes[apres - 1], membres, arrivee, maintenant);
            }
            else if (apres == 0 && communautes[avant - 1].Active)
            {
                Noter(maintenant, TypeEvenement.Communaute, h, null, $"{h.Nom} quitte la {Minuscule(communautes[avant - 1].Nom)}{Avec(h)}{AgeSiEnfant(h, maintenant)}.",
                    communaute: avant);
            }
        }
        Usages(humains, communautes, maintenant);
        Moeurs(humains, communautes, maintenant);
    }

    /// <summary>
    /// Combien de temps celui qui s'est éloigné des siens (cueillette, chasse, exploration) reste des leurs : d'une semaine
    /// environ pour qui tient peu à eux à quatre mois pour un loyal très attaché. Les chasseurs-cueilleurs partent souvent
    /// quelques jours ou quelques semaines et reviennent (Kelly, 2013) ; aucune étude ne mesure ce délai : ordre de grandeur.
    /// </summary>
    internal static long AbsenceToleree(Humain h, Humain autre, Lien l)
    {
        var loyaute = (h.Personnalite.Moral.Fondements.Loyaute + autre.Personnalite.Moral.Fondements.Loyaute) / 2;
        var semaines = (2 + 10 * loyaute) * Math.Clamp(0.5 + l.Affection / 100, 0.5, 1.5);
        return (long)(semaines * 7 * Calendrier.SecondesParJour);
    }

    /// <summary>
    /// La communauté dispersée que ce groupe fait renaître : la plupart de ses adultes en étaient, et personne d'autre ne la
    /// continue (ni héritière, ni fusion). Null s'il n'y en a pas.
    /// </summary>
    private static Communaute? Renaissante(List<Humain> g, List<Communaute> communautes, HashSet<int> reprises, long maintenant)
    {
        var adultes = g.Where(h => !h.EstEnfant(maintenant)).ToList();
        var (id, nombre) = adultes.Where(h => h.CommunauteId == 0 && h.AncienneCommunauteId != 0)
            .GroupBy(h => h.AncienneCommunauteId).Select(x => (Id: x.Key, Nombre: x.Count()))
            .OrderByDescending(x => x.Nombre).ThenBy(x => x.Id).FirstOrDefault();
        if (id == 0 || nombre * 2 <= adultes.Count || reprises.Contains(id)) return null;
        var c = communautes[id - 1];
        return !c.Active && c.HeritiereId == 0 && c.FusionneeDansId == 0 ? c : null;
    }

    /// <summary>
    /// Passer chez un groupe rival, c'est trahir aux yeux de ceux qui tiennent à la loyauté : ils en gardent rancune,
    /// d'autant plus qu'ils connaissaient le transfuge (le traître est puni plus durement qu'un étranger ; Graham et al., 2011).
    /// </summary>
    private void Trahir(Humain h, Communaute quittee, Communaute rejointe, List<Humain> membres, long arrivee, long maintenant)
    {
        var anciens = membres.Where(m => m.CommunauteId == quittee.Id && !m.EstEnfant(maintenant) && m.LienAvec(h.Id) is not null).ToList();
        long id = 0;
        foreach (var m in anciens)
        {
            var colere = 100 * (m.Personnalite.Moral.Fondements.Loyaute - 0.45) * (0.6 + Math.Max(0, m.AffectionPour(h.Id)) / 100);
            if (colere < 10) continue;
            if (id == 0)
            {
                id = Noter(maintenant, TypeEvenement.Conflit, h, null,
                    $"Pour les siens de la {Minuscule(quittee.Nom)}, {h.Nom} est {(h.Sexe == Sexe.Femme ? "une traîtresse" : "un traître")} : {(h.Sexe == Sexe.Femme ? "elle" : "il")} est passé{E(h)} chez la {Minuscule(rejointe.Nom)}, leur rivale.",
                    arrivee, communaute: quittee.Id, autreCommunaute: rejointe.Id, karma: Regard.Trahir);
            }
            AjouterGrief(m, new Grief(h.Id, MotifGrief.Trahison, Math.Min(100, colere), Math.Min(100, colere), id));
            ChangerAffection(m, h, -colere / 4, maintenant);
        }
    }

    /// <summary>« , avec sa fille X (3 ans) » ou « , avec ses enfants X (5 ans) et Y (8 mois) ».</summary>
    private static string AvecEnfants(Humain h, List<Humain> enfants, long maintenant)
    {
        var noms = enfants.OrderBy(e => e.Naissance).Select(e => $"{e.Nom} ({Comportement.AgeEnMots(e, maintenant)})").ToList();
        var liste = noms.Count == 1 ? noms[0] : $"{string.Join(", ", noms[..^1])} et {noms[^1]}";
        if (enfants.Count > 1) return $", avec {(enfants.All(e => e.MereId == h.Id || e.PereId == h.Id) ? "ses enfants" : "les enfants")} {liste}";
        var e = enfants[0];
        var lien = e.MereId == h.Id || e.PereId == h.Id ? (e.Sexe == Sexe.Femme ? "sa fille" : "son fils") : e.Sexe == Sexe.Femme ? "la petite" : "le petit";
        return $", avec {lien} {liste}";
    }

    /// <summary>Un enfant qui part ou arrive seul : son âge, pour qu'on le comprenne.</summary>
    private static string AgeSiEnfant(Humain h, long maintenant) =>
        h.EstEnfant(maintenant) ? $" ({Comportement.AgeEnMots(h, maintenant)}, sans les siens)" : "";

    private static string Minuscule(string nom) => char.ToLowerInvariant(nom[0]) + nom[1..];

    /// <summary>Figure centrale : le plus charismatique, pondéré par l'affection que lui portent les autres membres.</summary>
    private static Humain Figure(List<Humain> groupe, long maintenant)
    {
        var ids = groupe.Select(h => h.Id).ToHashSet();
        return groupe.Where(h => !h.EstEnfant(maintenant))
            .MaxBy(h => h.Personnalite.Charisme * (1 + h.Liens.Where(l => ids.Contains(l.Id) && l.Affection > 0).Sum(l => l.Affection) / 100)
                        * (h.Impulsions.Count > 0 && h.Consigne(Parole.TuGuideras, maintenant) is { } elu ? 1 + elu.Force : 1))!;
    }
}
