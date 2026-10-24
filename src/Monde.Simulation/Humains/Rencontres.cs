using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Deux communautés qui campent ensemble quelques jours : chants, danses, cadeaux, rencontres, et beaucoup de gestes vus.
/// Les numéros se suivent à partir de 1 (<see cref="Id"/> = rang dans <see cref="VieSociale.Rassemblements"/>).
/// </summary>
public sealed class Rassemblement
{
    public required int Id { get; init; }

    /// <summary>Communauté qui reçoit (celle dont les terres nourrissent le mieux), et celle qui vient.</summary>
    public required int HoteId { get; init; }
    public required int InviteeId { get; init; }

    /// <summary>Lieu du camp commun : là où se tenait la figure de la communauté qui reçoit.</summary>
    public required int X { get; init; }
    public required int Y { get; init; }

    public required long Debut { get; init; }

    /// <summary>Fin prévue (le rassemblement peut finir plus tôt : disette, querelle, raid).</summary>
    public long Fin { get; set; }

    /// <summary>Fin réelle (0 : il dure encore).</summary>
    public long FiniLe { get; set; }

    /// <summary>Événement qui l'annonce, et celui qui en fait le bilan.</summary>
    public long EvenementId { get; set; }
    public long BilanEvenementId { get; set; }

    /// <summary>Ce qui s'y est passé : participants, cadeaux donnés, gestes appris des autres, couples formés entre les deux groupes.</summary>
    public int Participants { get; set; }
    public int Cadeaux { get; set; }
    public int Appris { get; set; }
    public int Couples { get; set; }

    public bool Actif => FiniLe == 0;

    public Rassemblement Copier() => (Rassemblement)MemberwiseClone();
}

/// <summary>
/// Les rencontres entre communautés : les visites à un proche qui vit ailleurs et les rassemblements de deux groupes amis.
/// <para>
/// Ce que dit la recherche : un chasseur-cueilleur côtoie au fil de sa vie des centaines d'autres humains hors de son camp
/// (Hill et al., 2011 et 2014) ; chez les Agta et les BaYaka, les visites entre camps, surtout chez des parents, répandent
/// les savoirs bien au-delà du groupe (Migliano et al., 2017 et 2020) ; les groupes se rassemblent à la saison où la nourriture
/// abonde, et l'on y échange des cadeaux qui lient les familles pour des années (Wiessner, 1982, le hxaro des Ju/'hoansi) ;
/// un savoir complexe tient mieux dans un réseau de groupes reliés que dans un seul (Kline et Boyd, 2010).
/// </para>
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>
    /// Distance maximale entre les camps de deux groupes qui se rassemblent : environ 30 km, une longue journée de marche avec
    /// les enfants. Celle d'une visite à un proche : environ 40 km, deux journées pour un adulte seul (Kelly, 2013).
    /// </summary>
    private static readonly int DistanceRassemblementMax = Nature.Echelle.Cases(30);
    private static readonly int DistanceVisiteMax = Nature.Echelle.Cases(40);

    /// <summary>Tous les rassemblements, du premier au dernier.</summary>
    public List<Rassemblement> Rassemblements { get; } = [];

    public void ChargerRassemblements(IEnumerable<Rassemblement> rassemblements)
    {
        Rassemblements.Clear();
        Rassemblements.AddRange(rassemblements.OrderBy(r => r.Id).Select(r => r.Copier()));
    }

    /// <summary>Le rassemblement en cours auquel il prend part, ou null.</summary>
    public Rassemblement? RassemblementDe(Humain h) =>
        h.RassemblementId > 0 && h.RassemblementId <= Rassemblements.Count && Rassemblements[h.RassemblementId - 1] is { Actif: true } r ? r : null;

    /// <summary>Des relations qui permettent de se rendre visite : ni rivales, ni hostiles, ni en guerre.</summary>
    private bool EnBonsTermes(int a, int b) => RelationEntre(a, b) is EtatRelation.Indifferentes or EtatRelation.Alliees;

    // ───────────── Visites ─────────────

    /// <summary>
    /// Une fois par jour : qui a un proche (parent, enfant, frère ou sœur, compagnon, ami) dans une autre communauté, pas trop
    /// loin et en bons termes avec la sienne, part parfois le voir quelques jours. L'envie vient de l'attachement, du temps
    /// passé sans le voir et du goût pour la compagnie ; la faim, la maladie, un petit enfant à porter la retiennent.
    /// </summary>
    public void Visites(List<Humain> humains, long maintenant)
    {
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.VisiteChezId != 0 || h.RassemblementId != 0 || h.Rentrer || h.CaptifDe > 0 || h.DemandeAId != 0) continue;
            if (h.AgeEnAnnees(maintenant) < 15 || h.EstMalade(maintenant) || h.Faim > 50 || h.Soif > 50 || h.Fatigue > 60) continue;
            if (CommunauteDe(h) is not { } siens || siens.Disette || siens.DestinationX >= 0 || siens.Raid is not null) continue;
            if (Bannissement.Exil(h, maintenant) > 0) continue;
            var p = h.Personnalite;
            // Sitôt rentré, on ne repart pas : de quelques semaines pour les plus sociables à quelques mois.
            if (h.VisiteLe > 0 && maintenant - h.VisiteLe < (long)((15 + 60 * (1 - p.Extraversion)) * Calendrier.SecondesParJour)) continue;
            Humain? hote = null;
            Lien? lien = null;
            var meilleur = 0.0;
            foreach (var l in h.Liens)
            {
                if (l.Affection < 30) continue;
                var o = humains[l.Id - 1];
                if (!o.EstVivant || o.EstEnfant(maintenant) || o.CaptifDe > 0 || o.VisiteChezId != 0 || o.RassemblementId != 0) continue;
                if (o.CommunauteId == 0 || o.CommunauteId == h.CommunauteId || CommunauteDe(o) is not { } chezLui) continue;
                if (!EnBonsTermes(h.CommunauteId, o.CommunauteId)) continue;
                var d = Math.Max(Math.Abs(h.X - o.X), Math.Abs(h.Y - o.Y));
                if (d <= DistanceCommunaute || d > DistanceVisiteMax) continue;
                var famille = Parents(h, o) || h.PartenaireId == o.Id;
                if (!famille && !l.Amis) continue;
                // Le manque grandit avec le temps passé sans se voir (environ deux mois pour qu'il pèse pleinement).
                var manque = Math.Clamp((maintenant - l.Derniere) / (60.0 * Calendrier.SecondesParJour), 0.2, 2);
                var score = l.Affection / 100 * (famille ? 1.5 : 1) * manque * (1 - 0.5 * d / DistanceVisiteMax) * (chezLui.Disette ? 0.3 : 1);
                if (score <= meilleur) continue;
                (hote, lien, meilleur) = (o, l, score);
            }
            if (hote is null || lien is null) continue;
            // Une mère qui allaite ou un père qui veille sur un tout-petit part moins volontiers : l'enfant fait la route avec lui.
            var petit = humains.Vivants().Any(e => e.EstVivant && (e.MereId == h.Id || e.PereId == h.Id) && e.EstPetitEnfant(maintenant));
            var chance = meilleur * (0.3 + 0.7 * p.Extraversion) * (0.6 + 0.4 * p.Ouverture) * (petit ? 0.5 : 1) / 90;
            if (alea.Reel() >= chance) continue;
            // Quelques jours sur place : davantage quand on est venu de loin, et chez qui l'on aime beaucoup.
            var distance = Math.Max(Math.Abs(h.X - hote.X), Math.Abs(h.Y - hote.Y));
            var jours = Math.Clamp(3 * (1 + distance / (double)DistanceVisiteMax) * (0.6 + lien.Affection / 100) * Math.Exp(0.45 * Biologie.Normale(alea)), 1, 20);
            var chez = Communautes[hote.CommunauteId - 1];
            h.VisiteChezId = hote.Id;
            h.VisiteDepuis = maintenant;
            h.VisiteArriveeLe = 0;
            h.VisiteJusquA = (long)(jours * Calendrier.SecondesParJour);
            h.VisiteEvenementId = Noter(maintenant, TypeEvenement.Lien, h, hote,
                new Recit("renc.visite", ("h", h), ("qui", ParenteR(h, hote)), ("c", chez), ("d", Lieux.DistanceR(h.X, h.Y, hote.X, hote.Y)), ("vers", Lieux.VersR(h.X, h.Y, hote.X, hote.Y))),
                communaute: h.CommunauteId, autreCommunaute: chez.Id);
        }
    }

    /// <summary>Visite finie (ou interrompue) : il rentre chez les siens.</summary>
    internal static void FinirVisite(Humain h, long maintenant)
    {
        h.VisiteChezId = 0;
        h.VisiteArriveeLe = 0;
        h.VisiteJusquA = 0;
        h.VisiteLe = maintenant;
        h.Rentrer = true;
    }

    // ───────────── Rassemblements ─────────────

    /// <summary>
    /// Chaque semaine, après les relations : les rassemblements finissent ou s'interrompent, et deux communautés amies, voisines,
    /// qui mangent à leur faim, décident parfois de camper ensemble. Rien ne les y oblige : il faut l'envie de leurs figures et de
    /// leurs membres, de quoi nourrir tout le monde, et qu'il y ait un moment qu'elles ne se sont pas retrouvées.
    /// </summary>
    private void Rencontres(List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> membres, long maintenant)
    {
        foreach (var r in Rassemblements)
        {
            if (!r.Actif) continue;
            var (a, b) = (Communautes[r.HoteId - 1], Communautes[r.InviteeId - 1]);
            var pourquoi = !a.Active || !b.Active ? Recit.Seul("renc.pourquoi.defait")
                : !EnBonsTermes(a.Id, b.Id) ? Recit.Seul("renc.pourquoi.entente")
                : a.Disette || b.Disette ? Recit.Seul("renc.pourquoi.disette")
                : a.Raid is not null || b.Raid is not null ? Recit.Seul("renc.pourquoi.raid")
                : null;
            if (pourquoi is null && maintenant < r.Fin) continue;
            Finir(r, humains, maintenant, pourquoi);
        }

        var occupees = Rassemblements.Where(r => r.Actif).SelectMany(r => new[] { r.HoteId, r.InviteeId }).ToHashSet();
        for (var i = 0; i < actives.Count; i++)
        {
            for (var j = i + 1; j < actives.Count; j++)
            {
                var (a, b) = (actives[i], actives[j]);
                if (RelationEntre(a.Id, b.Id) != EtatRelation.Alliees || occupees.Contains(a.Id) || occupees.Contains(b.Id)) continue;
                if (a.Disette || b.Disette || a.Raid is not null || b.Raid is not null || a.DestinationX >= 0 || b.DestinationX >= 0) continue;
                if (a.FusionVersId != 0 || b.FusionVersId != 0) continue;
                if (Math.Max(Math.Abs(a.CentreX - b.CentreX), Math.Abs(a.CentreY - b.CentreY)) > DistanceRassemblementMax) continue;
                if (!membres.TryGetValue(a.Id, out var ma) || !membres.TryGetValue(b.Id, out var mb)) continue;
                var adultes = ma.Concat(mb).Where(h => !h.EstEnfant(maintenant)).ToList();
                if (adultes.Count == 0) continue;
                // De quoi nourrir tout le monde : des terres riches là où l'on campera, et des corps qui ne manquent de rien.
                var hote = a.Abondance >= b.Abondance ? a : b;
                var invitee = hote == a ? b : a;
                var nourriture = Math.Clamp((hote.Abondance - 0.5) / 0.4, 0, 1) * Math.Clamp((adultes.Average(h => h.Reserves) - 30) / 40, 0, 1);
                if (nourriture <= 0) continue;
                // Le temps depuis la dernière fois : il faut quelques mois pour en avoir de nouveau envie.
                var derniere = Rassemblements.LastOrDefault(r => r.HoteId == a.Id && r.InviteeId == b.Id || r.HoteId == b.Id && r.InviteeId == a.Id);
                var attente = derniere is null ? 0.75 : Math.Clamp((maintenant - derniere.FiniLe) / (double)Calendrier.SecondesParAn - 0.25, 0, 1.5);
                if (attente <= 0) continue;
                var (fa, fb) = (humains[a.FigureId - 1], humains[b.FigureId - 1]);
                static double Envie(Humain h) => 0.5 * h.Personnalite.Extraversion + 0.3 * h.Personnalite.Agreabilite + 0.2 * h.Personnalite.Ouverture;
                var envie = 0.5 * (Envie(fa) + Envie(fb)) / 2 + 0.5 * adultes.Average(h => h.Personnalite.Extraversion);
                // Environ une fois l'an pour deux groupes amis qui ne manquent de rien.
                if (alea.Reel() >= envie * nourriture * attente / 26) continue;
                Commencer(hote, invitee, ma.Concat(mb).ToList(), humains, nourriture, maintenant);
                occupees.Add(a.Id);
                occupees.Add(b.Id);
            }
        }
    }

    private void Commencer(Communaute hote, Communaute invitee, List<Humain> tous, List<Humain> humains, double nourriture, long maintenant)
    {
        var figure = humains[hote.FigureId - 1];
        var jours = Math.Clamp(7 * (0.6 + 0.8 * nourriture) * Math.Exp(0.35 * Biologie.Normale(alea)), 3, 21);
        var r = new Rassemblement
        {
            Id = Rassemblements.Count + 1, HoteId = hote.Id, InviteeId = invitee.Id, X = figure.X, Y = figure.Y, Debut = maintenant,
            Fin = maintenant + (long)(jours * Calendrier.SecondesParJour),
        };
        // Chacun décide d'y aller : les sociables presque toujours, les malades, les captives et ceux qui sont au loin non.
        // Un enfant va où va celui qui veille sur lui.
        var vont = new HashSet<int>();
        foreach (var h in tous)
        {
            if (h.EstEnfant(maintenant) || h.CaptifDe > 0 || h.VisiteChezId != 0 || h.EstMalade(maintenant)) continue;
            if (Bannissement.Exil(h, maintenant) > 0 || Math.Max(Math.Abs(h.X - r.X), Math.Abs(h.Y - r.Y)) > DistanceRassemblementMax + DistanceCommunaute) continue;
            var p = h.Personnalite;
            if (alea.Reel() >= Math.Clamp(0.5 + 0.4 * (p.Extraversion - 0.5) + 0.3 * (p.Agreabilite - 0.5) + (h.Id == figure.Id ? 1 : 0), 0.1, 0.97)) continue;
            vont.Add(h.Id);
        }
        foreach (var h in tous)
        {
            if (h.EstEnfant(maintenant) && Comportement.Gardien(h, humains, maintenant) is { } g && vont.Contains(g.Id)) vont.Add(h.Id);
        }
        if (vont.Count(id => !humains[id - 1].EstEnfant(maintenant)) < 2) return;
        Rassemblements.Add(r);
        foreach (var id in vont)
        {
            var h = humains[id - 1];
            h.RassemblementId = r.Id;
            h.Rentrer = false;
            h.DemandeAId = 0;
        }
        r.Participants = vont.Count;
        var premier = Rassemblements.Count == 1;
        r.EvenementId = Noter(maintenant, TypeEvenement.Communaute, figure, humains[invitee.FigureId - 1],
            new Recit(premier ? "renc.debut.premier" : "renc.debut", ("ni", invitee), ("nh", hote), ("f", figure), ("n", vont.Count)),
            _debutsRelations.GetValueOrDefault(Paire(hote.Id, invitee.Id)), communaute: hote.Id, autreCommunaute: invitee.Id, significatif: premier);
    }

    private void Finir(Rassemblement r, List<Humain> humains, long maintenant, Recit? pourquoi)
    {
        r.FiniLe = maintenant;
        foreach (var h in humains.Vivants())
        {
            if (h.RassemblementId != r.Id) continue;
            h.RassemblementId = 0;
            h.Rentrer = h.CommunauteId != r.HoteId;
        }
        var (a, b) = (Communautes[r.HoteId - 1], Communautes[r.InviteeId - 1]);
        var jours = Math.Max(1, (int)Math.Round((maintenant - r.Debut) / (double)Calendrier.SecondesParJour));
        var faits = new List<object?>();
        if (r.Cadeaux > 0) faits.Add(new Recit("renc.bilan.cadeaux", ("n", r.Cadeaux)));
        if (r.Appris > 0) faits.Add(new Recit("renc.bilan.appris", ("n", r.Appris)));
        if (r.Couples > 0) faits.Add(new Recit("renc.bilan.couples", ("n", r.Couples)));
        object bilan = faits.Count == 0 ? Recit.Seul("renc.bilan.rien") : new Liste(faits);
        var figure = humains[a.FigureId - 1];
        r.BilanEvenementId = Noter(maintenant, TypeEvenement.Communaute, figure, null,
            pourquoi is null ? new Recit("renc.fin", ("a", a), ("b", b), ("j", jours), ("bilan", bilan))
                : new Recit("renc.fin.precipitee", ("a", a), ("b", b), ("j", jours), ("pourquoi", pourquoi), ("bilan", bilan)),
            r.EvenementId, communaute: a.Id, autreCommunaute: b.Id);
    }

    /// <summary>
    /// Une fois par jour, au camp commun : on offre à quelqu'un de l'autre groupe un objet qu'il n'a pas, ou une part de viande.
    /// Le généreux donne plus souvent, à qui il aime ; le don crée de la gratitude, et un lien qui durera.
    /// </summary>
    public void JourDeRassemblement(List<Humain> humains, long maintenant)
    {
        foreach (var r in Rassemblements)
        {
            if (!r.Actif) continue;
            // Celui du groupe invité qui n'avait pas voulu venir, mais qui a fini par suivre un proche jusqu'au camp, en est aussi :
            // il rentrera avec les autres.
            foreach (var h in humains.Vivants())
            {
                if (!h.EstVivant || h.CommunauteId != r.InviteeId || h.RassemblementId != 0 || h.VisiteChezId != 0 || h.Rentrer || h.CaptifDe > 0
                    || Math.Max(Math.Abs(h.X - r.X), Math.Abs(h.Y - r.Y)) > DistanceCommunaute) continue;
                h.RassemblementId = r.Id;
                r.Participants++;
            }
            var presents = humains.Vivants().Where(h => h.EstVivant && h.RassemblementId == r.Id && !h.EstEnfant(maintenant)
                                                        && Math.Max(Math.Abs(h.X - r.X), Math.Abs(h.Y - r.Y)) <= 2).ToList();
            foreach (var d in presents)
            {
                var p = d.Personnalite;
                var generosite = (0.5 * p.Agreabilite + 0.3 * p.Moral.Compassion + 0.2 * p.Extraversion) * (1 - 0.8 * Psyche.Froideur(p));
                if (alea.Reel() >= 0.2 * generosite) continue;
                Humain? receveur = null;
                var mieux = double.MinValue;
                foreach (var o in presents)
                {
                    if (o.CommunauteId == d.CommunauteId || o.Id == d.Id) continue;
                    var affection = d.AffectionPour(o.Id);
                    if (affection < 0) continue;
                    var poids = affection + 30 * alea.Reel();
                    if (poids <= mieux) continue;
                    (receveur, mieux) = (o, poids);
                }
                if (receveur is null) continue;
                var don = d.Objets.Find(o => o.Matiere is not (Matiere.Carcasse or Matiere.Brandon or Matiere.PorteFeu)
                                             && !receveur.Objets.Exists(x => x.Matiere == o.Matiere && x.Pieces == o.Pieces));
                Recit quoi;
                if (don is not null && receveur.Objets.Count < 4)
                {
                    d.Objets.Remove(don);
                    receveur.Objets.Add(don);
                    quoi = don.Matiere == Matiere.Assemblage && don.Pieces is { } pieces && Assemblage.Lire(pieces) is { } a ? Lois.Decrire(a) : Physique.Nom(don.Matiere);
                }
                else if (d.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse) is var iv and >= 0)
                {
                    // Une part de viande : un à trois kilos, mangés sur place.
                    var kg = Math.Min(100 * d.Objets[iv].Qualite, 1 + 2 * alea.Reel());
                    var reste = d.Objets[iv].Qualite - kg / 100;
                    if (reste <= 0.005) d.Objets.RemoveAt(iv);
                    else d.Objets[iv] = d.Objets[iv] with { Qualite = reste };
                    receveur.Faim = Math.Max(0, receveur.Faim - 10 * kg);
                    quoi = Recit.Seul("renc.cadeau.viande");
                }
                else continue;
                r.Cadeaux++;
                Gratitude(d, receveur, 6);
                ChangerAffection(d, receveur, 3, maintenant);
                var cadeau = Noter(maintenant, TypeEvenement.Partage, d, receveur,
                    new Recit("renc.cadeau", ("d", d), ("quoi", quoi), ("r", receveur), ("c", Communautes[receveur.CommunauteId - 1])),
                    r.EvenementId, karma: Regard.Partage);
                Jardin.Confier(receveur, TypeIntime.Opinion, d.Id, new Recit("js.cadeau", ("x", d)), 25, cadeau, maintenant, $"opinion:{d.Id}");
            }
        }
    }

    /// <summary>Un geste appris d'un membre de l'autre groupe pendant le rassemblement (pour le bilan).</summary>
    internal void ApprisAuRassemblement(int rassemblement) => Tour.Differer(() =>
    {
        if (rassemblement >= 1 && rassemblement <= Rassemblements.Count) Rassemblements[rassemblement - 1].Appris++;
    });

    /// <summary>Un couple formé entre deux membres de groupes différents pendant le rassemblement (pour le bilan).</summary>
    internal void CoupleNe(Humain a, Humain b)
    {
        if (a.RassemblementId == 0 || a.RassemblementId != b.RassemblementId || a.CommunauteId == b.CommunauteId) return;
        var rassemblement = a.RassemblementId;
        Tour.Differer(() =>
        {
            if (rassemblement <= Rassemblements.Count) Rassemblements[rassemblement - 1].Couples++;
        });
    }
}

public sealed partial class Comportement
{
    /// <summary>
    /// Celui qui rend visite à un proche, prend part à un rassemblement ou en revient : il marche vers lui, vers le camp commun ou
    /// vers les siens. Renvoie vrai s'il est en route ; <paramref name="ailleurs"/> dit qu'il est arrivé et reste sur place
    /// (il ne part ni explorer, ni retrouver les siens, ni suivre sa communauté).
    /// </summary>
    private bool AllerAilleurs(Humain h, IReadOnlyList<Humain> tous, out bool ailleurs)
    {
        ailleurs = false;
        if (Social is not { } social) return false;
        if (h.RassemblementId > 0)
        {
            if (social.RassemblementDe(h) is not { } r)
            {
                h.RassemblementId = 0;
                h.Rentrer = true;
            }
            else
            {
                if (Distance(h.X, h.Y, r.X, r.Y) > 1)
                {
                    Avancer(h, r.X, r.Y, Activite.Rassembler);
                    if (h.CibleX >= 0) return true;
                }
                ailleurs = true;
                return false;
            }
        }
        if (h.VisiteChezId > 0)
        {
            var hote = tous[h.VisiteChezId - 1];
            // Il rentre quand le temps prévu est passé, quand son hôte est mort ou parti, quand les deux groupes se brouillent,
            // ou quand, au bout de quatre jours de route, il ne l'a toujours pas trouvé.
            var finie = !hote.EstVivant || hote.CommunauteId == 0 || hote.CommunauteId == h.CommunauteId
                        || h.CommunauteId != 0 && social.RelationEntre(h.CommunauteId, hote.CommunauteId) is EtatRelation.Rivales or EtatRelation.Hostiles or EtatRelation.EnGuerre
                        || h.VisiteArriveeLe > 0 && _maintenant >= h.VisiteJusquA
                        || h.VisiteArriveeLe == 0 && _maintenant - h.VisiteDepuis > 4 * Calendrier.SecondesParJour;
            if (finie) VieSociale.FinirVisite(h, _maintenant);
            else
            {
                _autre = hote;
                if (h.VisiteArriveeLe == 0 && Distance(h.X, h.Y, hote.X, hote.Y) <= 1)
                {
                    h.VisiteArriveeLe = _maintenant;
                    h.VisiteJusquA = _maintenant + h.VisiteJusquA;
                }
                if (Distance(h.X, h.Y, hote.X, hote.Y) > (h.VisiteArriveeLe == 0 ? 1 : 0))
                {
                    Avancer(h, hote.X, hote.Y, Activite.Visiter);
                    if (h.CibleX >= 0) return true;
                }
                ailleurs = true;
                return false;
            }
        }
        if (h.Rentrer)
        {
            if (social.CommunauteDe(h) is not { } siens || Distance(h.X, h.Y, siens.CentreX, siens.CentreY) <= 3)
            {
                h.Rentrer = false;
                return false;
            }
            Avancer(h, siens.CentreX, siens.CentreY, Activite.Rentrer);
            if (h.CibleX >= 0) return true;
            h.Rentrer = false;
        }
        return false;
    }
}
