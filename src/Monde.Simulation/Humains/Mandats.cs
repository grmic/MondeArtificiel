using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Où en est une parole de l'Entité adressée à une communauté.</summary>
public enum EtatMandat : byte
{
    /// <summary>Le groupe en débattra (voir <see cref="Mandat.DebatLe"/>).</summary>
    Debat = 0,
    /// <summary>Le groupe se rapproche de la communauté visée.</summary>
    EnRoute = 1,
    /// <summary>La chose se fait : guerre menée, délégation partie, serment ou trêve tenus, union ou départ en cours.</summary>
    EnCours = 2,
    /// <summary>C'est fait.</summary>
    Accompli = 3,
    /// <summary>Le groupe a refusé.</summary>
    Refuse = 4,
    /// <summary>L'autre communauté a refusé.</summary>
    Repousse = 5,
    /// <summary>Le groupe ne pouvait pas obéir (trop loin, la mer, personne pour le faire, rien à apaiser).</summary>
    Impossible = 6,
    /// <summary>Abandonné en route, ou sans plus d'objet.</summary>
    Caduc = 7,
}

/// <summary>
/// Une parole de l'Entité adressée à une communauté, envers une autre (ou vers une direction), et ce que le groupe en fait.
/// Les numéros se suivent à partir de 1 (<see cref="Id"/> = rang dans <see cref="VieSociale.Mandats"/>).
/// </summary>
public sealed class Mandat
{
    public required int Id { get; init; }

    /// <summary>Communauté qui a reçu la parole (celle qui la continue, si elle s'est recomposée).</summary>
    public required int CommunauteId { get; set; }

    public required Parole Parole { get; init; }
    public required ModeParole Mode { get; init; }

    /// <summary>Communauté visée (0 : aucune, pour « Partez vers… »), et direction de « Partez vers… » (0 à 7).</summary>
    public int CibleId { get; set; }
    public int Direction { get; init; }

    /// <summary>Quand la parole a été reçue, jour du débat, et terme (délai pour agir, ou fin d'un serment ou d'une trêve).</summary>
    public required long Le { get; init; }
    public long DebatLe { get; set; }
    public long Jusqua { get; set; }

    /// <summary>Événement qui rapporte la réception par le groupe : la cause de tout ce qui suit.</summary>
    public long EvenementId { get; set; }

    /// <summary>À la réception : adultes qui veulent obéir, qui hésitent, qui refusent, qui n'y ont pas cru, qui n'ont pas compris.</summary>
    public int Veulent { get; set; }
    public int Hesitent { get; set; }
    public int Refusent { get; set; }
    public int Incredules { get; set; }
    public int Incompris { get; set; }

    public EtatMandat Etat { get; set; }
    public long EtatLe { get; set; }
    public long EtatEvenementId { get; set; }

    /// <summary>Débats déjà tenus (on remet à plus tard deux fois au plus).</summary>
    public int Debats { get; set; }

    /// <summary>Le groupe n'obéit qu'à moitié ; seuls les fidèles agissent ; la figure a imposé son avis.</summary>
    public bool Moitie { get; set; }
    public bool Fideles { get; set; }
    public bool Impose { get; set; }

    /// <summary>Ceux qui agissent : guerriers, délégués, fidèles.</summary>
    public List<int> Envoyes { get; set; } = [];

    /// <summary>Rassemblement qui porte la rencontre ou la délégation (0 : aucun).</summary>
    public int RassemblementId { get; set; }

    /// <summary>Celui qui a porté la parole à l'autre groupe, et s'il a dit que l'Entité les envoyait.</summary>
    public int MessagerId { get; set; }
    public bool Annonce { get; set; }

    /// <summary>Ce qui en est sorti : conversions, part de nourriture donnée, distance parcourue…</summary>
    public double Bilan { get; set; }

    public bool Ouvert => Etat is EtatMandat.Debat or EtatMandat.EnRoute or EtatMandat.EnCours;

    public Mandat Copier()
    {
        var copie = (Mandat)MemberwiseClone();
        copie.Envoyes = [.. Envoyes];
        return copie;
    }
}

/// <summary>
/// Ce qu'une communauté fait d'une parole de l'Entité qui lui est adressée : elle en débat, puis obéit, n'obéit qu'à moitié,
/// remet à plus tard, refuse, ou laisse ses seuls fidèles agir ; une figure très respectée ou crainte peut imposer son avis
/// (voir <see cref="Emprise"/>). Ce qu'elle fait ensuite passe par les règles du monde : projets de guerre et raids, paix,
/// pactes, rassemblements, enseignement, prédication, dons, fusions, migrations ; et l'autre communauté répond selon les siennes.
/// <para>
/// Chez les peuples sans chef, les décisions qui engagent le groupe se prennent en discutant jusqu'à ce qu'un avis l'emporte,
/// et nul n'est tenu de suivre (Boehm, 1999) ; dans les sociétés plus hiérarchisées, celui qui commande peut imposer sa
/// volonté. Les poids et les délais sont des estimations.
/// </para>
/// </summary>
public sealed partial class VieSociale
{
    private readonly List<Mandat> _mandats = [];

    /// <summary>Toutes les paroles adressées à des communautés, de la première à la dernière.</summary>
    public IReadOnlyList<Mandat> Mandats => _mandats;

    public void ChargerMandats(IEnumerable<Mandat> mandats)
    {
        _mandats.Clear();
        _mandats.AddRange(mandats.OrderBy(m => m.Id).Select(m => m.Copier()));
    }

    /// <summary>Avis à partir duquel on est pour (comme pour un projet de guerre).</summary>
    private const double AvisPour = 0.6;

    // ───────────── Ce que la parole coûte et ce qu'elle rejoint ─────────────

    /// <summary>Affection moyenne de <paramref name="h"/> pour les membres de <paramref name="b"/> qu'il connaît (−1 à 1 ; 0 s'il n'en connaît aucun).</summary>
    private static double AffectionPourGroupe(Humain h, Communaute b, IReadOnlyList<Humain> tous)
    {
        double somme = 0;
        var n = 0;
        foreach (var l in h.Liens)
        {
            if (l.Id < 1 || l.Id > tous.Count || tous[l.Id - 1].CommunauteId != b.Id) continue;
            somme += l.Affection;
            n++;
        }
        return n == 0 ? 0 : somme / n / 100;
    }

    /// <summary>Rancune de <paramref name="h"/> envers les membres de <paramref name="b"/> (0 à 1), surtout pour des meurtres.</summary>
    private static double RancuneEnvers(Humain h, Communaute b, IReadOnlyList<Humain> tous)
    {
        var r = 0.0;
        foreach (var g in h.Griefs)
            if (g.ContreId >= 1 && g.ContreId <= tous.Count && tous[g.ContreId - 1].CommunauteId == b.Id) r += g.Intensite / 100 * (g.Motif == MotifGrief.Meurtre ? 1 : 0.4);
        return Math.Min(1, r);
    }

    /// <summary>
    /// Ce que coûte à <paramref name="h"/> la parole adressée à son groupe <paramref name="a"/> envers <paramref name="b"/>
    /// (0 : rien, 1 : impossible), ce qu'elle rejoint de ses désirs (négatif : elle le contrarie), et la raison qu'il donnerait
    /// d'un refus.
    /// </summary>
    internal (double Cout, double Envie, Recit Refus) PeserPourLeGroupe(Humain h, Parole parole, Communaute a, Communaute? b,
        IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = m.Fondements;
        var aff = b is null ? 0 : AffectionPourGroupe(h, b, tous);
        var rancune = b is null ? 0 : RancuneEnvers(h, b, tous);
        var douceur = 0.4 * m.Compassion + 0.3 * m.EmpathieEmotionnelle + 0.3 * f.Soin;
        var froid = Psyche.Froideur(p);
        var relation = b is null ? EtatRelation.Indifferentes : RelationEntre(a.Id, b.Id);
        var querelle = relation is EtatRelation.Hostiles or EtatRelation.EnGuerre or EtatRelation.Rivales;
        switch (parole)
        {
            case Parole.FaitesLaGuerre or Parole.ChassezDeLeursTerres:
            {
                var terres = parole == Parole.ChassezDeLeursTerres && a.Disette ? 0.3 : 0;
                var envie = 0.6 * rancune + 0.3 * (1 - p.Agreabilite) + 0.3 * (f.Loyaute - 0.5) + 0.2 * froid + terres - 0.3 * (m.Compassion - 0.5);
                var cout = Math.Clamp(0.3 * p.Prudence + 0.3 * douceur * (querelle ? 0.5 : 1) + 0.4 * Math.Max(0, aff) + (parole == Parole.ChassezDeLeursTerres ? 0.1 : 0), 0, 1);
                return (cout, envie, querelle ? new Recit("mandat.refus.sang", ("h", h)) : new Recit("mandat.refus.rienFait", ("h", h), ("c", b!)));
            }
            case Parole.Exterminez:
            {
                if (Psyche.EstPsychopathe(h, maintenant)) return (0.3, 0.5 * rancune + 0.3 * froid, new Recit("mandat.refus.rien", ("h", h)));
                var cout = Math.Clamp(0.45 + 0.5 * douceur * (1 - 0.5 * rancune) + 0.3 * Math.Max(0, aff) - 0.3 * rancune, 0, 1);
                return (cout, 0.6 * rancune + 0.2 * (1 - p.Agreabilite), new Recit("mandat.refus.enfants", ("h", h)));
            }
            case Parole.FaitesLaPaix:
            {
                var guerre = GuerreEntre(a.Id, b!.Id);
                var deuils = guerre is null ? 0 : Math.Min(1, (guerre.MortsA + guerre.MortsB) / 4.0);
                return (Math.Clamp(0.6 * rancune * (1.2 - p.Agreabilite), 0, 1), 0.3 * p.Agreabilite + 0.3 * m.Compassion + 0.3 * deuils - 0.2,
                    new Recit("mandat.refus.vengeance", ("h", h)));
            }
            case Parole.AlliezVous:
            {
                var ennemi = Communautes.Any(k => k.Active && k.Id != a.Id && k.Id != b!.Id
                    && RelationEntre(a.Id, k.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre && RelationEntre(b.Id, k.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre);
                return (Math.Clamp(0.6 * Math.Max(0, -aff) + 0.4 * rancune, 0, 1), 0.5 * Math.Max(0, aff) + (ennemi ? 0.3 : 0) + 0.2 * p.Agreabilite - 0.1,
                    new Recit("mandat.refus.confiance", ("h", h), ("c", b!)));
            }
            case Parole.AllezALaRencontre:
                return (Math.Clamp(0.6 * Math.Max(0, -aff) + 0.4 * rancune + 0.2 * p.Nevrosisme * (1 - p.Ouverture) + (a.Disette ? 0.3 : 0), 0, 1),
                    0.3 * p.Extraversion + 0.2 * p.Ouverture + 0.3 * Math.Max(0, aff) - 0.1, new Recit("mandat.refus.etrangers", ("h", h)));
            case Parole.EnseignezVosSavoirs:
                return (Math.Clamp(0.5 * Math.Max(0, -aff) + 0.4 * rancune + 0.2 * (1 - p.Agreabilite) * (querelle ? 1 : 0.3), 0, 1),
                    0.3 * p.Agreabilite + 0.2 * m.Compassion + 0.2 * p.Extraversion - 0.1, new Recit("mandat.refus.savoirs", ("h", h)));
            case Parole.PortezMaParoleA:
                return (Math.Clamp(0.4 * (1 - h.Croyance / 100) + 0.3 * Math.Max(0, -aff) + 0.3 * (1 - p.Extraversion) * p.Nevrosisme, 0, 1),
                    0.4 * h.Ferveur + 0.3 * p.Transcendance + 0.2 * p.Extraversion - 0.2, new Recit("mandat.refus.moques", ("h", h)));
            case Parole.PartagezAvec:
            {
                var faim = a.Disette ? 0.5 : 0;
                return (Math.Clamp(faim + 0.3 * Math.Max(0, h.Faim - 40) / 60 + 0.5 * Math.Max(0, -aff) + 0.3 * (1 - p.Agreabilite) * (1 - m.Compassion), 0, 1),
                    0.4 * m.Compassion + 0.2 * f.Soin + (b!.Disette ? 0.3 * m.Compassion : 0) - 0.1,
                    a.Disette ? new Recit("mandat.refus.faim", ("h", h)) : new Recit("mandat.refus.garder", ("h", h)));
            }
            case Parole.UnissezVous:
            {
                var petits = Math.Clamp((TailleBandeMax - a.Rayon * 2.0) / TailleBandeMax, 0, 1);
                return (Math.Clamp(0.3 * f.Loyaute + 0.6 * Math.Max(0, -aff) + 0.4 * rancune, 0, 1), 0.4 * Math.Max(0, aff) + 0.2 * petits + (a.Disette ? 0.2 : 0) - 0.1,
                    new Recit("mandat.refus.nous", ("h", h)));
            }
            case Parole.PartezVers:
            {
                var lies = h.Memoire.Lieux.Count(l => l.Type == TypeLieu.Nourriture);
                return (Math.Clamp(0.3 * p.Prudence + 0.2 * p.Conscience * Math.Min(1, lies / 10.0) - 0.2 * p.Ouverture + 0.1, 0, 1),
                    0.4 * p.Ouverture + (a.Disette ? 0.3 : 0) - 0.15, new Recit("mandat.refus.terres", ("h", h)));
            }
            default:
                return (0, 0, Recit.Seul("mandat.refus.rien"));
        }
    }

    // ───────────── La parole parvient au groupe ─────────────

    /// <summary>
    /// La parole est parvenue au groupe <paramref name="a"/> : on note ce que ses adultes en pensent et le jour où il en
    /// débattra (de quelques jours à quelques semaines, comme pour un projet de guerre ; plus tôt après une apparition).
    /// Un mandat par communauté visée.
    /// </summary>
    public List<Mandat> Recevoir(Communaute a, Parole parole, ModeParole mode, IReadOnlyList<Communaute> cibles, int direction,
        IReadOnlyList<Humain> humains, long maintenant)
    {
        var adultes = AdultesDe(a, humains, maintenant);
        var figure = humains[a.FigureId - 1];
        var nouveaux = new List<Mandat>();
        var parametres = parole == Parole.PartezVers ? [Math.Clamp(direction, 0, 7)] : cibles.Select(c => c.Id).ToList();
        foreach (var param in parametres)
        {
            var m = new Mandat
            {
                Id = _mandats.Count + 1, CommunauteId = a.Id, Parole = parole, Mode = mode, CibleId = parole == Parole.PartezVers ? 0 : param,
                Direction = parole == Parole.PartezVers ? param : 0, Le = maintenant, Etat = EtatMandat.Debat, EtatLe = maintenant,
            };
            double forces = 0;
            var croyants = 0;
            foreach (var h in adultes)
            {
                switch (ImpulsionDe(h, m)?.Reponse)
                {
                    case Reponse.Acceptee or Reponse.AMoitie: m.Veulent++; break;
                    case Reponse.Differee: m.Hesitent++; break;
                    case Reponse.Refusee: m.Refusent++; break;
                    case Reponse.Incrue: m.Incredules++; break;
                    case Reponse.Incomprise: m.Incompris++; break;
                }
                if (ImpulsionDe(h, m) is { Crue: true } i) (forces, croyants) = (forces + i.Force, croyants + 1);
            }
            var urgence = mode == ModeParole.Apparition || croyants * 2 > adultes.Count;
            m.DebatLe = maintenant + DelaiDebat(adultes.Count, urgence, figure);
            // Le temps de mener la chose à bien : plus long quand beaucoup y croient fort.
            var foi = croyants == 0 ? 0 : forces / croyants;
            m.Jusqua = maintenant + (long)((60 + 340 * foi * croyants / Math.Max(1, adultes.Count)) * (0.7 + 0.6 * alea.Reel()) * Calendrier.SecondesParJour);
            object cible = m.CibleId > 0 ? Communautes[m.CibleId - 1] : Recit.Seul($"direction.{m.Direction}");
            m.EvenementId = Noter(maintenant, TypeEvenement.Parole, figure, null,
                new Recit("mandat.recu", ("c", a), ("m", Recit.Seul($"modeParole.{mode}")), ("ph", Paroles.Phrase(parole, null, cible)),
                    ("v", m.Veulent), ("hes", m.Hesitent), ("r", m.Refusent), ("i", m.Incredules + m.Incompris)),
                communaute: a.Id, autreCommunaute: m.CibleId, significatif: true);
            _mandats.Add(m);
            nouveaux.Add(m);
        }
        return nouveaux;
    }

    /// <summary>Ce que <paramref name="h"/> a pensé de la parole de ce mandat (null : elle ne lui est pas parvenue).</summary>
    private static Impulsion? ImpulsionDe(Humain h, Mandat m)
    {
        var param = m.Parole == Parole.PartezVers ? m.Direction : m.CibleId;
        for (var k = h.Impulsions.Count - 1; k >= 0; k--)
        {
            var i = h.Impulsions[k];
            if (i.Le == m.Le && i.Parole == m.Parole && i.Param == param) return i;
            if (i.Le < m.Le) break;
        }
        return null;
    }

    // ───────────── Chaque jour ─────────────

    /// <summary>
    /// Chaque jour, après les projets de guerre : les groupes débattent des paroles reçues le jour venu, se rapprochent de
    /// ceux qu'elles visent, et mènent à bien ce qu'ils ont décidé.
    /// </summary>
    private void SuivreLesMandats(List<Humain> humains, long maintenant)
    {
        if (_mandats.Count == 0) return;
        foreach (var m in _mandats)
        {
            if (!m.Ouvert) continue;
            if (Heritiere(Communautes[m.CommunauteId - 1]) is not { } a)
            {
                Clore(m, EtatMandat.Caduc, null, humains, Recit.Seul("mandat.caduc.groupe"), maintenant);
                continue;
            }
            m.CommunauteId = a.Id;
            Communaute? b = null;
            if (m.CibleId > 0)
            {
                b = Heritiere(Communautes[m.CibleId - 1]);
                if (b is null || b.Id == a.Id)
                {
                    var finie = Paroles.Guerriere(m.Parole) && m.Etat == EtatMandat.EnCours;
                    Clore(m, finie ? EtatMandat.Accompli : EtatMandat.Caduc, a, humains,
                        new Recit(finie ? "mandat.fin.plusLa" : "mandat.caduc.cible", ("c", Communautes[m.CibleId - 1])), maintenant);
                    continue;
                }
                m.CibleId = b.Id;
            }
            switch (m.Etat)
            {
                case EtatMandat.Debat when maintenant >= m.DebatLe:
                    Deliberer(m, a, b, humains, maintenant);
                    break;
                case EtatMandat.EnRoute:
                    Approcher(m, a, b!, humains, maintenant);
                    break;
                case EtatMandat.EnCours:
                    Poursuivre(m, a, b, humains, maintenant);
                    break;
            }
        }
    }

    /// <summary>Le mandat prend fin : c'est noté au journal (significatif).</summary>
    private void Clore(Mandat m, EtatMandat etat, Communaute? a, List<Humain> humains, Recit texte, long maintenant)
    {
        m.Etat = etat;
        m.EtatLe = maintenant;
        var c = a ?? Communautes[m.CommunauteId - 1];
        var figure = c.FigureId >= 1 ? humains[c.FigureId - 1] : humains[0];
        m.EtatEvenementId = Noter(maintenant, TypeEvenement.Parole, figure, null, texte, m.EtatEvenementId != 0 ? m.EtatEvenementId : m.EvenementId,
            communaute: c.Id, autreCommunaute: m.CibleId, significatif: true);
    }

    /// <summary>Change l'état du mandat et note ce qui se passe (significatif).</summary>
    private void Passer(Mandat m, EtatMandat etat, Communaute a, List<Humain> humains, Recit texte, long maintenant, Humain? qui = null, Humain? autre = null)
    {
        m.Etat = etat;
        m.EtatLe = maintenant;
        m.EtatEvenementId = Noter(maintenant, Paroles.Guerriere(m.Parole) ? TypeEvenement.Guerre : TypeEvenement.Communaute,
            qui ?? humains[a.FigureId - 1], autre, texte, m.EtatEvenementId != 0 ? m.EtatEvenementId : m.EvenementId,
            communaute: a.Id, autreCommunaute: m.CibleId, significatif: true);
    }

    // ───────────── Le débat ─────────────

    /// <summary>
    /// Le groupe débat. Chacun dit ce qu'il pense : celui qui a cru la parole penche comme il l'a reçue, les autres selon
    /// leurs seuls intérêts ; la figure donne son avis et beaucoup s'y rangent, d'autant plus qu'ils respectent l'autorité.
    /// Une figure très respectée ou crainte peut trancher seule (voir <see cref="Emprise"/>). Ensuite : on obéit, on obéit à
    /// moitié, on remet à plus tard, on refuse, ou les fidèles agissent seuls.
    /// </summary>
    private void Deliberer(Mandat m, Communaute a, Communaute? b, List<Humain> humains, long maintenant)
    {
        var adultes = AdultesDe(a, humains, maintenant);
        if (adultes.Count == 0)
        {
            Clore(m, EtatMandat.Caduc, a, humains, Recit.Seul("mandat.caduc.groupe"), maintenant);
            return;
        }
        if (Impossible(m, a, b, humains, maintenant) is { } impossible)
        {
            Clore(m, EtatMandat.Impossible, a, humains, new Recit("mandat.impossible", ("c", a), ("pourquoi", impossible)), maintenant);
            return;
        }
        m.Debats++;
        var figure = humains[a.FigureId - 1];
        double Avis(Humain h)
        {
            var (cout, envie, _) = PeserPourLeGroupe(h, m.Parole, a, b, humains, maintenant);
            var propre = 0.3 + 0.5 * envie - 0.5 * cout;
            var i = ImpulsionDe(h, m);
            var cru = i?.Reponse switch
            {
                Reponse.Acceptee => 0.8 + 0.2 * i.Force,
                Reponse.AMoitie => 0.62,
                Reponse.Differee => 0.45,
                Reponse.Refusee => 0.15,
                _ => (double?)null,
            };
            // Loin, il faut marcher longtemps : ceux qui doivent y aller tous ensemble y regardent à deux fois.
            var loin = b is null || !Paroles.Approcher(m.Parole) ? 0 : 0.3 * Math.Clamp(Ecart(a, b) / (3.0 * DistanceRaidMax), 0, 1);
            return (cru is { } c ? 0.7 * c + 0.3 * propre : propre) - loin + 0.08 * Biologie.Normale(alea);
        }
        var avisFigure = Avis(figure);
        var figurePour = avisFigure >= AvisPour;
        var figureContre = avisFigure < AvisPour - 0.25;
        var avis = adultes.Select(h => (h, Score: h.Id == figure.Id ? avisFigure
            : Avis(h) + (figureContre ? -0.25 : figurePour && h.AffectionPour(figure.Id) > 0 ? 0.2 : 0) * (0.5 + h.Personnalite.Moral.Fondements.Autorite)))
            .ToList();
        var pour = avis.Where(x => x.Score >= AvisPour).Select(x => x.h).ToList();
        var indecis = avis.Count(x => Math.Abs(x.Score - AvisPour) < 0.12);
        var part = pour.Count / (double)avis.Count;

        // Une figure très respectée ou très crainte tranche seule.
        m.Impose = alea.Reel() < Emprise(a, humains, maintenant);
        var obeir = m.Impose ? figurePour : part >= 0.5;
        var moitie = !m.Impose && obeir && part < 0.6;
        if (m.Impose)
        {
            // Ceux qui pensaient autrement se taisent : de la rancœur, et de la peur si la figure est crainte.
            var crainte = Crainte(a, humains, maintenant);
            foreach (var (h, score) in avis)
            {
                if (h.Id == figure.Id || (score >= AvisPour) == figurePour) continue;
                ChangerAffection(h, figure, -(3 + 6 * crainte) * (1.2 - h.Personnalite.Agreabilite), maintenant);
                Jardin.Confier(h, crainte > 0.4 ? TypeIntime.Peur : TypeIntime.Opinion, figure.Id, new Recit("js.mandat.impose", ("f", figure)),
                    25 + 40 * crainte, m.EvenementId, maintenant, $"impose:{figure.Id}");
            }
            Passer(m, EtatMandat.Debat, a, humains, new Recit(figurePour ? "mandat.impose.oui" : "mandat.impose.non", ("f", figure), ("c", a),
                ("crainte", Recit.Seul(crainte >= 0.5 ? "mandat.parPeur" : "mandat.parRespect"))), maintenant, figure);
        }

        if (obeir)
        {
            m.Moitie = moitie;
            if (!m.Impose)
                Passer(m, EtatMandat.Debat, a, humains, new Recit(moitie ? "mandat.debat.moitie" : "mandat.debat.oui", ("c", a), ("n", pour.Count), ("t", avis.Count)),
                    maintenant);
            Agir(m, a, b, pour.Count > 0 ? pour : [figure], humains, maintenant);
            return;
        }
        // Rien n'est tranché : on en reparlera (deux fois au plus).
        if (!m.Impose && indecis * 4 >= avis.Count && m.Debats < 3)
        {
            m.DebatLe = maintenant + DelaiDebat(avis.Count, false, figure);
            Passer(m, EtatMandat.Debat, a, humains, new Recit("mandat.debat.plusTard", ("c", a)), maintenant);
            return;
        }
        // Le groupe refuse ; les fidèles ardents passent outre et agissent seuls, s'ils sont assez nombreux.
        var fideles = pour.Where(h => ImpulsionDe(h, m) is { Reponse: Reponse.Acceptee }).ToList();
        var ardeur = fideles.Count == 0 ? 0 : avis.Where(x => fideles.Contains(x.h)).Average(x => x.Score);
        var refus = m.Impose ? new Recit("mandat.refuse.figure", ("c", a), ("f", figure))
            : figureContre ? new Recit("mandat.refuse.figureContre", ("c", a), ("f", figure), ("n", pour.Count), ("t", avis.Count))
            : new Recit("mandat.refuse", ("c", a), ("n", pour.Count), ("t", avis.Count));
        if (fideles.Count >= 2 && ardeur >= AvisPour + 0.25 && alea.Reel() < 0.5 + 0.5 * Math.Min(1, fideles.Average(h => h.Personnalite.Extraversion + (1 - h.Personnalite.Prudence)) / 2))
        {
            Passer(m, EtatMandat.Debat, a, humains, new Recit("mandat.fideles", ("refus", refus), ("n", fideles.Count)), maintenant, fideles[0]);
            m.Fideles = true;
            Agir(m, a, b, fideles, humains, maintenant);
            return;
        }
        Clore(m, EtatMandat.Refuse, a, humains, refus, maintenant);
    }

    /// <summary>Écart entre les centres de deux groupes, en cases.</summary>
    private static int Ecart(Communaute a, Communaute b) => Math.Max(Math.Abs(a.CentreX - b.CentreX), Math.Abs(a.CentreY - b.CentreY));

    /// <summary>Quelqu'un du groupe sait-il faire un bateau qui tient la mer ?</summary>
    private static bool SaitNaviguer(IEnumerable<Humain> adultes) => adultes.Any(h => h.Savoirs.Exists(s => s.Effet == Effet.Navigation && s.Confiance >= 0.3));

    /// <summary>Les deux groupes vivent-ils sur des terres séparées par la mer ?</summary>
    private bool SeparesParLaMer(Communaute a, Communaute b, IReadOnlyList<Humain> humains)
    {
        if (Carte is null) return false;
        var (fa, fb) = (humains[a.FigureId - 1], humains[b.FigureId - 1]);
        var (ma, mb) = (Carte.Mer.Masse[Carte.Indice(fa.X, fa.Y)], Carte.Mer.Masse[Carte.Indice(fb.X, fb.Y)]);
        return ma > 0 && mb > 0 && ma != mb;
    }

    /// <summary>Ce qui rend la parole impossible à suivre (null : rien).</summary>
    private Recit? Impossible(Mandat m, Communaute a, Communaute? b, IReadOnlyList<Humain> humains, long maintenant)
    {
        if (b is null) return null;
        var relation = RelationEntre(a.Id, b.Id);
        var mer = SeparesParLaMer(a, b, humains);
        switch (m.Parole)
        {
            case Parole.FaitesLaPaix when relation is not (EtatRelation.Hostiles or EtatRelation.EnGuerre or EtatRelation.Rivales):
                return new Recit("mandat.pourquoi.paix", ("c", b));
            case Parole.AlliezVous when relation == EtatRelation.EnGuerre:
            case Parole.EnseignezVosSavoirs or Parole.PortezMaParoleA or Parole.AllezALaRencontre when relation == EtatRelation.EnGuerre:
                return new Recit("mandat.pourquoi.guerre", ("c", b));
            case Parole.EnseignezVosSavoirs or Parole.PortezMaParoleA or Parole.PartagezAvec when mer:
                return new Recit("mandat.pourquoi.mer", ("c", b));
            case Parole.PartagezAvec when Ecart(a, b) > 2 * DistanceVisiteMax:
                return new Recit("mandat.pourquoi.loin", ("c", b));
            case Parole.PortezMaParoleA when !AdultesDe(a, humains, maintenant).Any(h => h.Croyance >= 50 || h.ReligionId != 0):
                return Recit.Seul("mandat.pourquoi.personne");
            case Parole.FaitesLaGuerre or Parole.ChassezDeLeursTerres or Parole.Exterminez
                when !AdultesDe(a, humains, maintenant).Any(h => EstGuerrier(h, maintenant)):
                return Recit.Seul("mandat.pourquoi.guerriers");
        }
        if (mer && (Paroles.Approcher(m.Parole) || m.Parole == Parole.UnissezVous) && !SaitNaviguer(AdultesDe(a, humains, maintenant)))
            return new Recit("mandat.pourquoi.mer", ("c", b));
        return null;
    }

    // ───────────── Agir ─────────────

    /// <summary>
    /// Le groupe (ou ses fidèles, <paramref name="acteurs"/>) fait ce que la parole demande. Ce qui doit se faire près de
    /// l'autre groupe attend que l'on s'en soit rapproché ; l'autre groupe répond à ce qu'on lui propose.
    /// </summary>
    private void Agir(Mandat m, Communaute a, Communaute? b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        m.Envoyes = acteurs.Select(h => h.Id).ToList();
        // Ce qu'il faut faire près de l'autre groupe : le groupe entier s'en rapproche d'abord (les fidèles seuls ne déplacent pas le camp).
        if (b is not null && Paroles.Approcher(m.Parole) && !m.Moitie)
        {
            var portee = m.Parole == Parole.AllezALaRencontre ? DistanceRassemblementMax : DistanceRaidMax;
            if (Ecart(a, b) > portee)
            {
                if (m.Fideles)
                {
                    Clore(m, EtatMandat.Impossible, a, humains, new Recit("mandat.impossible", ("c", a), ("pourquoi", new Recit("mandat.pourquoi.loin", ("c", b)))), maintenant);
                    return;
                }
                VersLAutre(a, b, portee, humains);
                Passer(m, EtatMandat.EnRoute, a, humains, new Recit("mandat.enRoute", ("c", a), ("b", b),
                    ("d", Lieux.DistanceR(a.CentreX, a.CentreY, b.CentreX, b.CentreY)), ("vers", Lieux.VersR(a.CentreX, a.CentreY, b.CentreX, b.CentreY))), maintenant);
                return;
            }
        }
        Faire(m, a, b, humains, maintenant);
    }

    /// <summary>Le groupe part s'installer à portée de l'autre (aux deux tiers de la portée, de son côté).</summary>
    private void VersLAutre(Communaute a, Communaute b, int portee, IReadOnlyList<Humain> humains)
    {
        var (dx, dy) = (a.CentreX - b.CentreX, a.CentreY - b.CentreY);
        var longueur = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var loin = portee * 2 / 3.0;
        var x = Math.Clamp(b.CentreX + (int)Math.Round(dx / longueur * loin), 0, Carte!.Largeur - 1);
        var y = Math.Clamp(b.CentreY + (int)Math.Round(dy / longueur * loin), 0, Carte.Hauteur - 1);
        // Sur des terres praticables : à défaut, auprès de l'autre groupe.
        if (!Climat.EstPraticable(Carte.BiomeEn(x, y))) (x, y) = (humains[b.FigureId - 1].X, humains[b.FigureId - 1].Y);
        (a.DestinationX, a.DestinationY) = (x, y);
    }

    /// <summary>Chaque jour, en route : on suit l'autre groupe là où il campe ; arrivé à portée, on agit ; le temps passé, on renonce.</summary>
    private void Approcher(Mandat m, Communaute a, Communaute b, List<Humain> humains, long maintenant)
    {
        var portee = m.Parole == Parole.AllezALaRencontre ? DistanceRassemblementMax : DistanceRaidMax;
        if (Ecart(a, b) <= portee)
        {
            a.DestinationX = a.DestinationY = -1;
            Faire(m, a, b, humains, maintenant);
            return;
        }
        if (maintenant >= m.Jusqua)
        {
            a.DestinationX = a.DestinationY = -1;
            Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.caduc.route", ("c", a), ("b", b)), maintenant);
            return;
        }
        VersLAutre(a, b, portee, humains);
    }

    /// <summary>Ce que demande chaque parole, une fois le groupe décidé (et à portée s'il le faut).</summary>
    private void Faire(Mandat m, Communaute a, Communaute? b, List<Humain> humains, long maintenant)
    {
        var acteurs = m.Envoyes.Select(id => humains[id - 1]).Where(h => h.EstVivant && h.CommunauteId == a.Id).ToList();
        switch (m.Parole)
        {
            case Parole.FaitesLaGuerre or Parole.ChassezDeLeursTerres or Parole.Exterminez:
                FaireLaGuerre(m, a, b!, acteurs, humains, maintenant);
                break;
            case Parole.FaitesLaPaix:
                Pacifier(m, a, b!, acteurs, humains, maintenant);
                break;
            case Parole.AlliezVous:
                Allier(m, a, b!, acteurs, humains, maintenant);
                break;
            case Parole.AllezALaRencontre or Parole.EnseignezVosSavoirs or Parole.PortezMaParoleA:
                Deleguer(m, a, b!, acteurs, humains, maintenant);
                break;
            case Parole.PartagezAvec:
                Partager(m, a, b!, acteurs, humains, maintenant);
                break;
            case Parole.UnissezVous:
                Unir(m, a, b!, acteurs, humains, maintenant);
                break;
            case Parole.PartezVers:
                Partir(m, a, acteurs, humains, maintenant);
                break;
        }
    }

    // ───────────── L'autre groupe répond ─────────────

    /// <summary>
    /// Celui qui porte la proposition à l'autre groupe : la figure si elle a cru la parole, sinon le fidèle le plus ardent.
    /// Il dit que l'Entité les envoie s'il y croit et qu'il est franc ; le rusé ou le sceptique le tait.
    /// </summary>
    private Humain Messager(Mandat m, Communaute a, List<Humain> acteurs, List<Humain> humains)
    {
        var figure = humains[a.FigureId - 1];
        var messager = ImpulsionDe(figure, m) is { Crue: true } && figure.CommunauteId == a.Id ? figure
            : acteurs.OrderByDescending(h => ImpulsionDe(h, m)?.Force ?? 0).ThenBy(h => h.Id).FirstOrDefault() ?? figure;
        var mo = messager.Personnalite.Moral;
        var croit = ImpulsionDe(messager, m) is { Crue: true };
        m.MessagerId = messager.Id;
        m.Annonce = croit && alea.Reel() < (0.3 + 0.6 * mo.Honnetete) * (1 - 0.5 * Psyche.Froideur(messager.Personnalite));
        return messager;
    }

    /// <summary>
    /// L'autre groupe accepte-t-il ? Chacun de ses adultes pèse ce qu'on lui propose selon ce qu'il pense des autres et son
    /// caractère ; si l'on dit que l'Entité les envoie, ceux qui croient en l'invisible y sont plus enclins. Une figure très
    /// respectée ou crainte tranche seule.
    /// </summary>
    private bool Accepter(Mandat m, Communaute a, Communaute b, List<Humain> humains, long maintenant)
    {
        var adultes = AdultesDe(b, humains, maintenant);
        if (adultes.Count == 0) return false;
        var relation = RelationEntre(a.Id, b.Id);
        var querelle = relation switch { EtatRelation.EnGuerre => 0.5, EtatRelation.Hostiles => 0.35, EtatRelation.Rivales => 0.15, _ => 0 };
        var guerre = GuerreEntre(a.Id, b.Id);
        var deuils = guerre is null ? 0 : Math.Min(1, (guerre.MortsA + guerre.MortsB) / 4.0);
        var religionA = ReligionDominante(a, humains, maintenant);
        double Avis(Humain o)
        {
            var p = o.Personnalite;
            var aff = AffectionPourGroupe(o, a, humains);
            var rancune = RancuneEnvers(o, a, humains);
            var v = m.Parole switch
            {
                Parole.FaitesLaPaix => 0.25 + 0.3 * p.Agreabilite + 0.3 * deuils + 0.2 * p.Prudence - 0.6 * rancune,
                Parole.AlliezVous => 0.2 + 0.5 * aff + 0.2 * p.Agreabilite - querelle - 0.4 * rancune,
                Parole.AllezALaRencontre => 0.3 + 0.3 * p.Extraversion + 0.2 * p.Agreabilite + 0.3 * aff - querelle - 0.4 * rancune,
                Parole.EnseignezVosSavoirs => 0.35 + 0.4 * p.Ouverture + 0.2 * aff - querelle - 0.3 * rancune,
                Parole.PortezMaParoleA => 0.25 + 0.3 * p.Transcendance + 0.2 * aff - querelle - 0.3 * rancune
                                           - (o.ReligionId != 0 && o.ReligionId != religionA ? 0.4 * o.Ferveur : 0),
                // Un groupe qui a faim prend ; un fier, en querelle, refuse ce qui vient de l'ennemi.
                Parole.PartagezAvec => 0.5 + (b.Disette ? 0.4 : 0) - querelle * (1.2 - p.Agreabilite),
                Parole.UnissezVous => 0.1 + 0.5 * aff + (b.Disette ? 0.2 : 0) - 0.3 * p.Moral.Fondements.Loyaute - querelle - 0.4 * rancune,
                _ => 0.5,
            };
            // « L'Entité nous envoie » : ceux qui croient en l'invisible l'entendent.
            if (m.Annonce) v += 0.3 * (o.Croyance / 100 - 0.3);
            return v;
        }
        var emprise = Emprise(b, humains, maintenant);
        var figureB = humains[b.FigureId - 1];
        var p = emprise * Math.Clamp(Avis(figureB), 0, 1) + (1 - emprise) * Math.Clamp(adultes.Average(Avis), 0, 1);
        return alea.Reel() < Math.Clamp(p, 0.02, 0.98);
    }

    /// <summary>Religion de la plupart des adultes d'une communauté (0 : aucune).</summary>
    private static int ReligionDominante(Communaute c, IReadOnlyList<Humain> humains, long maintenant)
    {
        var adultes = AdultesDe(c, humains, maintenant);
        return adultes.Where(h => h.ReligionId != 0).GroupBy(h => h.ReligionId).MaxBy(g => g.Count()) is { } g && g.Count() * 2 > adultes.Count ? g.Key : 0;
    }

    /// <summary>La proposition est portée à l'autre groupe (noté), qui répond ; vrai s'il accepte.</summary>
    private bool Proposer(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        var messager = Messager(m, a, acteurs, humains);
        var figureB = humains[b.FigureId - 1];
        var oui = Accepter(m, a, b, humains, maintenant);
        var proposition = Recit.Seul($"mandat.propose.{m.Parole}");
        var texte = new Recit(oui ? "mandat.accepte" : "mandat.repousse", ("m", messager), ("b", b), ("fb", figureB), ("quoi", proposition),
            ("entite", m.Annonce ? Recit.Seul("mandat.annonce") : ""));
        if (oui) Passer(m, EtatMandat.Debat, a, humains, texte, maintenant, messager, figureB);
        else Clore(m, EtatMandat.Repousse, a, humains, texte, maintenant);
        return oui;
    }

    // ───────────── Chaque parole ─────────────

    /// <summary>
    /// La guerre : les guerriers les plus ardents (ou les seuls fidèles) partent en raid ; à moitié, le groupe se contente de
    /// menacer. Chasser et exterminer durent jusqu'à ce que l'autre groupe ait fui, ou n'existe plus.
    /// </summary>
    private void FaireLaGuerre(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        if (m.Moitie && !m.Fideles)
        {
            // À moitié : on montre sa force et l'on menace ; l'inimitié est déclarée, le sang n'a pas coulé.
            var cle = Paire(a.Id, b.Id);
            if (RelationEntre(a.Id, b.Id) != EtatRelation.EnGuerre) _relations[cle] = EtatRelation.Hostiles;
            var id = Noter(maintenant, TypeEvenement.Guerre, humains[a.FigureId - 1], humains[b.FigureId - 1],
                new Recit("mandat.guerre.menace", ("c", a), ("b", b)), m.EtatEvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
            _offenses[cle] = id;
            _debutsRelations.TryAdd(cle, id);
            m.Etat = EtatMandat.Accompli;
            m.EtatLe = maintenant;
            m.EtatEvenementId = id;
            return;
        }
        if (a.Raid is not null)
        {
            // Un raid est déjà en route : on attend qu'il soit rentré.
            m.Etat = EtatMandat.EnCours;
            return;
        }
        var guerriers = acteurs.Where(h => EstGuerrier(h, maintenant)).ToList();
        if (guerriers.Count < 2) guerriers = AdultesDe(a, humains, maintenant).Where(h => EstGuerrier(h, maintenant)).OrderByDescending(h => acteurs.Contains(h)).ThenBy(h => h.Id).ToList();
        var troupe = guerriers.Take(Math.Clamp((int)Math.Ceiling(guerriers.Count * 0.6), 2, 10)).ToList();
        if (troupe.Count < 2)
        {
            Clore(m, EtatMandat.Impossible, a, humains, new Recit("mandat.impossible", ("c", a), ("pourquoi", Recit.Seul("mandat.pourquoi.guerriers"))), maintenant);
            return;
        }
        a.Projet = null;
        LancerRaid(a, b, humains, troupe, Recit.Seul(m.Parole switch
        {
            Parole.Exterminez => "guerre.raison.exterminer",
            Parole.ChassezDeLeursTerres => "guerre.raison.chasser",
            _ => "guerre.raison.entite",
        }), m.EtatEvenementId != 0 ? m.EtatEvenementId : m.EvenementId, maintenant);
        m.Envoyes = troupe.Select(h => h.Id).ToList();
        m.EtatEvenementId = a.Raid!.EvenementId;
        m.EtatLe = maintenant;
        // « Faites la guerre » est accompli quand la troupe part ; chasser et exterminer durent (voir Poursuivre).
        m.Etat = m.Parole == Parole.FaitesLaGuerre || m.Moitie ? EtatMandat.Accompli : EtatMandat.EnCours;
    }

    /// <summary>
    /// La paix : on la propose ; si l'autre l'accepte, la guerre s'achève et les rancunes s'apaisent. À moitié : une trêve,
    /// pendant laquelle le groupe ne frappe pas. Les fidèles seuls font la paix pour eux-mêmes.
    /// </summary>
    private void Pacifier(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        if (m.Fideles)
        {
            foreach (var h in acteurs)
            {
                for (var k = 0; k < h.Griefs.Count; k++)
                {
                    var g = h.Griefs[k];
                    if (g.ContreId >= 1 && humains[g.ContreId - 1].CommunauteId == b.Id) h.Griefs[k] = g with { Intensite = g.Intensite * 0.5 };
                }
            }
            Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.paix.fideles", ("n", acteurs.Count), ("b", b)), maintenant);
            return;
        }
        if (m.Moitie)
        {
            m.Jusqua = maintenant + (long)((90 + 270 * alea.Reel()) * Calendrier.SecondesParJour);
            Passer(m, EtatMandat.EnCours, a, humains, new Recit("mandat.treve", ("c", a), ("b", b)), maintenant);
            return;
        }
        if (!Proposer(m, a, b, acteurs, humains, maintenant)) return;
        var (fa, fb) = (humains[a.FigureId - 1], humains[b.FigureId - 1]);
        if (GuerreEntre(a.Id, b.Id) is { } g2)
        {
            g2.Issue = IssueGuerre.Paix;
            g2.Fin = maintenant;
            g2.FinEvenementId = Noter(maintenant, TypeEvenement.Guerre, fa, fb,
                new Recit("mandat.paix.guerre", ("fa", fa), ("fb", fb), ("a", a), ("b", b), ("pertes", Pertes(g2))),
                m.EtatEvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
            FaireLaPaix(a, b, humains, g2.FinEvenementId);
            Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.fait", ("c", a)), maintenant);
            return;
        }
        var id = Noter(maintenant, TypeEvenement.Communaute, fa, fb, new Recit("mandat.paix.querelle", ("a", a), ("b", b)),
            m.EtatEvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
        FaireLaPaix(a, b, humains, id);
        m.Etat = EtatMandat.Accompli;
        m.EtatLe = maintenant;
        m.EtatEvenementId = id;
    }

    /// <summary>
    /// L'alliance : un serment d'entraide que l'autre accepte ou non ; il tient des années, tant que l'amitié entre les deux
    /// groupes ne tourne pas à l'inimitié. À moitié : des cadeaux, qui rapprochent sans engager.
    /// </summary>
    private void Allier(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        if (m.Moitie || m.Fideles)
        {
            Rapprocher(m.Fideles ? acteurs : AdultesDe(a, humains, maintenant), b, 8, humains, maintenant);
            Clore(m, EtatMandat.Accompli, a, humains, new Recit(m.Fideles ? "mandat.cadeaux.fideles" : "mandat.cadeaux", ("c", a), ("b", b), ("n", acteurs.Count)), maintenant);
            return;
        }
        if (!Proposer(m, a, b, acteurs, humains, maintenant)) return;
        var cle = Paire(a.Id, b.Id);
        var (fa, fb) = (humains[a.FigureId - 1], humains[b.FigureId - 1]);
        // Un serment tient d'autant plus longtemps que les figures sont loyales : de deux à dix ans environ.
        var loyaute = (fa.Personnalite.Moral.Fondements.Loyaute + fb.Personnalite.Moral.Fondements.Loyaute) / 2;
        m.Jusqua = maintenant + (long)((2 + 8 * loyaute) * (0.7 + 0.6 * alea.Reel()) * Calendrier.SecondesParAn);
        _relations[cle] = EtatRelation.Alliees;
        Rapprocher(AdultesDe(a, humains, maintenant), b, 10, humains, maintenant);
        var id = Noter(maintenant, TypeEvenement.Communaute, fa, fb, new Recit("mandat.serment", ("fa", fa), ("fb", fb), ("a", a), ("b", b)),
            m.EtatEvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
        _pactes[cle] = id;
        _debutsRelations[cle] = id;
        m.Etat = EtatMandat.EnCours;
        m.EtatLe = maintenant;
        m.EtatEvenementId = id;
    }

    /// <summary>Des gestes d'amitié envers l'autre groupe : chacun des donneurs se rapproche de quelques-uns de ses membres.</summary>
    private void Rapprocher(List<Humain> donneurs, Communaute b, double force, List<Humain> humains, long maintenant)
    {
        var receveurs = humains.Vivants().Where(o => o.EstVivant && o.CommunauteId == b.Id && !o.EstEnfant(maintenant)).ToList();
        if (receveurs.Count == 0) return;
        foreach (var h in donneurs)
        {
            for (var k = 0; k < Math.Min(3, receveurs.Count); k++)
                ChangerAffection(h, receveurs[alea.Entier(receveurs.Count)], force * (0.5 + alea.Reel()), maintenant);
        }
    }

    /// <summary>
    /// La rencontre, l'enseignement et la prédication passent par un camp commun : tout le groupe pour une rencontre (à moitié,
    /// une délégation), quelques délégués pour enseigner ou prêcher. Il faut que l'autre groupe les accueille.
    /// </summary>
    private void Deleguer(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        if (!Proposer(m, a, b, acteurs, humains, maintenant)) return;
        // Une hostilité se suspend le temps d'être reçus.
        if (RelationEntre(a.Id, b.Id) is EtatRelation.Hostiles or EtatRelation.Rivales) _relations.Remove(Paire(a.Id, b.Id));
        var adultes = AdultesDe(a, humains, maintenant);
        List<Humain> delegues;
        if (m.Parole == Parole.AllezALaRencontre && !m.Moitie && !m.Fideles)
        {
            delegues = [];
        }
        else
        {
            var vivier = m.Fideles ? acteurs : adultes;
            var nombre = m.Moitie ? 1 : Math.Clamp((int)Math.Round(vivier.Count * 0.15), 2, 5);
            IEnumerable<Humain> ordre = m.Parole switch
            {
                Parole.EnseignezVosSavoirs => vivier.OrderByDescending(h => h.Savoirs.Count(s => s.Confiance >= 0.6 && s.Succes >= 2)).ThenBy(h => h.Id),
                Parole.PortezMaParoleA => vivier.Where(h => h.Croyance >= 50 || h.ReligionId != 0)
                    .OrderByDescending(h => h.Role != RoleReligieux.Aucun).ThenByDescending(h => h.Ferveur + h.Croyance / 100).ThenBy(h => h.Id),
                _ => vivier.OrderByDescending(h => h.Id == a.FigureId).ThenByDescending(h => h.Personnalite.Extraversion).ThenBy(h => h.Id),
            };
            delegues = ordre.Where(h => h.VisiteChezId == 0 && h.RassemblementId == 0 && !h.EstMalade(maintenant)).Take(nombre).ToList();
            if (delegues.Count == 0)
            {
                Clore(m, EtatMandat.Impossible, a, humains, new Recit("mandat.impossible", ("c", a), ("pourquoi", Recit.Seul("mandat.pourquoi.personne"))), maintenant);
                return;
            }
        }
        var avant = Rassemblements.Count;
        if (delegues.Count == 0)
        {
            var tous = humains.Vivants().Where(h => h.EstVivant && (h.CommunauteId == a.Id || h.CommunauteId == b.Id)).ToList();
            var hote = b.Abondance >= a.Abondance ? b : a;
            Commencer(hote, hote == a ? b : a, tous, humains, Math.Clamp((hote.Abondance - 0.5) / 0.4, 0.3, 1), maintenant);
        }
        else
        {
            Accueillir(m, b, a, delegues, humains, maintenant);
        }
        if (Rassemblements.Count == avant)
        {
            Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.caduc.personne", ("c", a)), maintenant);
            return;
        }
        var r = Rassemblements[^1];
        m.RassemblementId = r.Id;
        m.Envoyes = delegues.Select(h => h.Id).ToList();
        m.Etat = EtatMandat.EnCours;
        m.EtatLe = maintenant;
        m.EtatEvenementId = r.EvenementId;
    }

    /// <summary>
    /// Une délégation de <paramref name="a"/> est reçue chez <paramref name="b"/> : un camp commun autour de la figure de
    /// <paramref name="b"/>, pour deux à six semaines (moins à moitié), où les siens viennent voir et écouter.
    /// </summary>
    private void Accueillir(Mandat m, Communaute b, Communaute a, List<Humain> delegues, List<Humain> humains, long maintenant)
    {
        var figure = humains[b.FigureId - 1];
        var jours = Math.Clamp((m.Moitie ? 10 : 25) * Math.Exp(0.35 * Biologie.Normale(alea)), 5, 45);
        var r = new Rassemblement
        {
            Id = Rassemblements.Count + 1, HoteId = b.Id, InviteeId = a.Id, X = figure.X, Y = figure.Y, Debut = maintenant,
            Fin = maintenant + (long)(jours * Calendrier.SecondesParJour),
        };
        var vont = delegues.Select(h => h.Id).ToHashSet();
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.CommunauteId != b.Id || h.CaptifDe > 0 || h.VisiteChezId != 0 || h.RassemblementId != 0) continue;
            if (Math.Max(Math.Abs(h.X - r.X), Math.Abs(h.Y - r.Y)) > DistanceRassemblementMax) continue;
            vont.Add(h.Id);
        }
        Rassemblements.Add(r);
        foreach (var id in vont)
        {
            var h = humains[id - 1];
            h.RassemblementId = r.Id;
            h.Rentrer = false;
            h.DemandeAId = 0;
        }
        r.Participants = vont.Count;
        var noms = new Liste(delegues.Select(h => (object?)new Personne(h)).ToList(), virgules: true);
        r.EvenementId = Noter(maintenant, TypeEvenement.Communaute, delegues[0], figure,
            new Recit($"mandat.delegation.{m.Parole}", ("noms", noms), ("n", delegues.Count), ("a", a), ("b", b), ("f", figure)),
            m.EtatEvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
    }

    /// <summary>
    /// Partager : chacun de ceux qui donnent cède une part de ses réserves (le généreux davantage), partagée entre les adultes
    /// de l'autre groupe ; ceux qui reçoivent en gardent de la gratitude.
    /// </summary>
    private void Partager(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        if (!Proposer(m, a, b, acteurs, humains, maintenant)) return;
        var donneurs = m.Fideles ? acteurs : AdultesDe(a, humains, maintenant);
        var receveurs = AdultesDe(b, humains, maintenant);
        double total = 0;
        foreach (var h in donneurs)
        {
            var part = h.Reserves * (m.Moitie ? 0.5 : 1) * Math.Clamp(0.08 + 0.25 * h.Personnalite.Moral.Compassion, 0.05, 0.35);
            h.Reserves -= part;
            total += part;
        }
        foreach (var o in receveurs) o.Reserves = Math.Min(100, o.Reserves + total / Math.Max(1, receveurs.Count));
        foreach (var o in receveurs.Take(12))
            ChangerAffection(o, humains[m.MessagerId - 1], 6 + 6 * Math.Min(1, total / Math.Max(1, receveurs.Count) / 20), maintenant);
        m.Bilan = total;
        Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.partage", ("c", a), ("b", b), ("n", donneurs.Count),
            ("jours", (int)Math.Round(total / Math.Max(1, receveurs.Count) / 3))), maintenant);
    }

    /// <summary>
    /// S'unir : si l'autre y consent, le plus petit groupe part rejoindre le plus grand (voir les fusions) ; à moitié, des gestes
    /// d'amitié ; les fidèles seuls partent vivre chez l'autre groupe.
    /// </summary>
    private void Unir(Mandat m, Communaute a, Communaute b, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        if (m.Fideles)
        {
            var figureB = humains[b.FigureId - 1];
            foreach (var h in acteurs)
                Rejoindre(h, m, figureB.X, figureB.Y, maintenant);
            Passer(m, EtatMandat.Accompli, a, humains, new Recit("mandat.union.fideles", ("n", acteurs.Count), ("b", b)), maintenant);
            return;
        }
        if (m.Moitie)
        {
            Rapprocher(AdultesDe(a, humains, maintenant), b, 8, humains, maintenant);
            Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.cadeaux", ("c", a), ("b", b), ("n", acteurs.Count)), maintenant);
            return;
        }
        if (!Proposer(m, a, b, acteurs, humains, maintenant)) return;
        if (RelationEntre(a.Id, b.Id) is EtatRelation.Hostiles or EtatRelation.Rivales) FaireLaPaix(a, b, humains, m.EtatEvenementId);
        var (na, nb) = (AdultesDe(a, humains, maintenant).Count, AdultesDe(b, humains, maintenant).Count);
        var (petite, grande) = na < nb || (na == nb && a.Id > b.Id) ? (a, b) : (b, a);
        petite.FusionVersId = grande.Id;
        petite.FusionDecideeLe = maintenant;
        (petite.DestinationX, petite.DestinationY) = (grande.CentreX, grande.CentreY);
        petite.FusionEvenementId = Noter(maintenant, TypeEvenement.Communaute, humains[petite.FigureId - 1], humains[grande.FigureId - 1],
            new Recit("mandat.union", ("p", petite), ("g", grande)), m.EtatEvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
        m.EtatEvenementId = petite.FusionEvenementId;
        m.Etat = EtatMandat.EnCours;
        m.EtatLe = maintenant;
    }

    /// <summary>Un fidèle part vivre là où l'Entité l'envoie : une consigne « Pars vers… » qu'il suit comme les siennes.</summary>
    private void Rejoindre(Humain h, Mandat m, int x, int y, long maintenant)
    {
        if (Carte is null) return;
        h.Impulsions.Add(new Impulsion(Parole.PartsVers, m.Mode, 0, y * Carte.Largeur + x, ImpulsionDe(h, m)?.Force ?? 0.5, maintenant,
            maintenant + 60 * Calendrier.SecondesParJour, maintenant, Reponse.Acceptee, m.EvenementId));
    }

    /// <summary>
    /// Partir : le groupe part vers la direction donnée, à une ou deux journées de marche (moitié moins à moitié) ;
    /// les fidèles seuls partent sans les autres.
    /// </summary>
    private void Partir(Mandat m, Communaute a, List<Humain> acteurs, List<Humain> humains, long maintenant)
    {
        var (dx, dy) = Paroles.Vecteurs[Math.Clamp(m.Direction, 0, 7)];
        var km = m.Moitie ? 6 + 6 * alea.Reel() : 15 + 25 * alea.Reel();
        var n = Echelle.Cases(km);
        int x = a.CentreX, y = a.CentreY;
        for (var i = n; i >= 1; i--)
        {
            int cx = Math.Clamp(a.CentreX + dx * i, 0, Carte!.Largeur - 1), cy = Math.Clamp(a.CentreY + dy * i, 0, Carte.Hauteur - 1);
            if (!Climat.EstPraticable(Carte.BiomeEn(cx, cy))) continue;
            (x, y) = (cx, cy);
            break;
        }
        if (x == a.CentreX && y == a.CentreY)
        {
            Clore(m, EtatMandat.Impossible, a, humains, new Recit("mandat.impossible", ("c", a), ("pourquoi", Recit.Seul("mandat.pourquoi.terres"))), maintenant);
            return;
        }
        if (m.Fideles)
        {
            foreach (var h in acteurs) Rejoindre(h, m, x, y, maintenant);
            Passer(m, EtatMandat.Accompli, a, humains, new Recit("mandat.partir.fideles", ("n", acteurs.Count), ("vers", Recit.Seul($"direction.{m.Direction}"))), maintenant);
            return;
        }
        (a.DestinationX, a.DestinationY) = (x, y);
        m.Bilan = km;
        Passer(m, EtatMandat.EnCours, a, humains, new Recit("mandat.partir", ("c", a), ("vers", Recit.Seul($"direction.{m.Direction}")),
            ("d", Lieux.DistanceR(a.CentreX, a.CentreY, x, y))), maintenant);
    }

    // ───────────── Ce qui dure ─────────────

    /// <summary>Chaque jour, ce qui est en cours : la guerre continue, la délégation rentre, le serment tient, l'union se fait, on arrive.</summary>
    private void Poursuivre(Mandat m, Communaute a, Communaute? b, List<Humain> humains, long maintenant)
    {
        switch (m.Parole)
        {
            case Parole.FaitesLaGuerre:
                // Le raid précédent n'était pas rentré : on repart dès que possible.
                if (a.Raid is null) FaireLaGuerre(m, a, b!, m.Envoyes.Select(id => humains[id - 1]).Where(h => h.EstVivant).ToList(), humains, maintenant);
                break;
            case Parole.ChassezDeLeursTerres or Parole.Exterminez:
            {
                var fin = _guerres.LastOrDefault(g => (g.A == a.Id && g.B == b!.Id || g.A == b!.Id && g.B == a.Id) && g.Issue == IssueGuerre.Fuite && g.Fin >= m.Le);
                if (m.Parole == Parole.ChassezDeLeursTerres && fin is not null)
                {
                    Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.chasses", ("c", a), ("b", b!)), maintenant);
                    break;
                }
                if (maintenant >= m.Jusqua)
                {
                    Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.caduc.haine", ("c", a), ("b", b!)), maintenant);
                    break;
                }
                // L'ennemi a fui hors de portée : on le poursuit.
                if (Ecart(a, b!) > DistanceRaidMax && a.Raid is null)
                {
                    if (m.Parole == Parole.ChassezDeLeursTerres)
                        Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.chasses", ("c", a), ("b", b!)), maintenant);
                    else
                    {
                        VersLAutre(a, b!, DistanceRaidMax, humains);
                        Passer(m, EtatMandat.EnRoute, a, humains, new Recit("mandat.poursuite", ("c", a), ("b", b!)), maintenant);
                    }
                }
                break;
            }
            case Parole.FaitesLaPaix when maintenant >= m.Jusqua:
                Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.treve.fin", ("c", a), ("b", b!)), maintenant);
                break;
            case Parole.AlliezVous:
                if (RelationEntre(a.Id, b!.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre)
                    Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.serment.rompu", ("a", a), ("b", b)), maintenant);
                else if (maintenant >= m.Jusqua)
                    Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.serment.fin", ("a", a), ("b", b)), maintenant);
                break;
            case Parole.AllezALaRencontre or Parole.EnseignezVosSavoirs or Parole.PortezMaParoleA:
            {
                if (m.RassemblementId < 1 || m.RassemblementId > Rassemblements.Count || Rassemblements[m.RassemblementId - 1] is not { } r)
                {
                    Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.caduc.personne", ("c", a)), maintenant);
                    break;
                }
                if (r.Actif) break;
                var bilan = m.Parole switch
                {
                    Parole.EnseignezVosSavoirs => new Recit("mandat.bilan.appris", ("n", r.Appris)),
                    Parole.PortezMaParoleA => new Recit("mandat.bilan.convertis", ("n", (int)m.Bilan)),
                    _ => new Recit("mandat.bilan.rencontre", ("n", r.Participants), ("cadeaux", r.Cadeaux), ("couples", r.Couples)),
                };
                Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.retour", ("c", a), ("b", b!), ("bilan", bilan)), maintenant);
                break;
            }
            case Parole.UnissezVous:
            {
                var (petite, grande) = a.FusionVersId == b!.Id ? (a, b) : (b, a);
                if (petite.FusionneeDansId == grande.Id || !petite.Active && Heritiere(petite)?.Id == grande.Id)
                    Clore(m, EtatMandat.Accompli, grande, humains, new Recit("mandat.union.faite", ("p", petite), ("g", grande)), maintenant);
                else if (petite.FusionVersId != grande.Id)
                    Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.union.renonce", ("p", petite), ("g", grande)), maintenant);
                break;
            }
            case Parole.PartezVers:
                if (a.DestinationX < 0)
                    Clore(m, EtatMandat.Accompli, a, humains, new Recit("mandat.arrive", ("c", a)), maintenant);
                else if (maintenant >= m.Jusqua)
                {
                    a.DestinationX = a.DestinationY = -1;
                    Clore(m, EtatMandat.Caduc, a, humains, new Recit("mandat.caduc.route2", ("c", a)), maintenant);
                }
                break;
        }
    }

    // ───────────── Ce que les autres règles en savent ─────────────

    /// <summary>Le groupe <paramref name="a"/> mène contre <paramref name="b"/> une guerre voulue par l'Entité de cette sorte.</summary>
    internal bool MeneContre(int a, int b, Parole parole)
    {
        if (_mandats.Count == 0) return false;
        foreach (var m in _mandats)
            if (m.Parole == parole && m.Ouvert && m.Etat != EtatMandat.Debat && m.CommunauteId == a && m.CibleId == b) return true;
        return false;
    }

    /// <summary>Le groupe <paramref name="a"/> a promis une trêve à <paramref name="b"/> et la tient encore.</summary>
    internal bool Treve(int a, int b)
    {
        if (_mandats.Count == 0) return false;
        foreach (var m in _mandats)
            if (m is { Parole: Parole.FaitesLaPaix, Etat: EtatMandat.EnCours } && m.CommunauteId == a && m.CibleId == b) return true;
        return false;
    }

    /// <summary>Les deux groupes ont juré l'alliance sur la parole de l'Entité, et le serment tient encore.</summary>
    internal bool Serment(int a, int b)
    {
        if (_mandats.Count == 0) return false;
        foreach (var m in _mandats)
            if (m is { Parole: Parole.AlliezVous, Etat: EtatMandat.EnCours } && (m.CommunauteId == a && m.CibleId == b || m.CommunauteId == b && m.CibleId == a)) return true;
        return false;
    }

    /// <summary>Ce que l'humain fait chez l'autre groupe sur la parole de l'Entité (enseigner, prêcher), ou null.</summary>
    internal Mandat? Mission(Humain h)
    {
        if (_mandats.Count == 0 || h.RassemblementId == 0) return null;
        foreach (var m in _mandats)
            if (m.RassemblementId == h.RassemblementId && m.Etat == EtatMandat.EnCours && m.Envoyes.Contains(h.Id)) return m;
        return null;
    }

    /// <summary>Un humain de l'autre groupe s'est converti (ou s'est mis à croire) grâce à la délégation.</summary>
    internal void Converti(Mandat m) => Tour.Differer(() => m.Bilan++);
}
