using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce que les communautés se racontent de leurs malheurs (0.39.0). Après une catastrophe, ceux qui l'ont vécue cherchent pourquoi :
/// une faute qui a fâché l'invisible, un esprit à apaiser, un présage, ou une chose de la nature qui revient. L'explication qui
/// s'impose est celle de quelqu'un qu'on écoute ; on la croit surtout si on l'a vécue et si elle répond à sa propre peur.
/// <para>
/// Le récit change la vie de ceux qui y croient. Ils ne campent plus où c'est arrivé ; ils guettent les signes qui l'ont précédée
/// (à Simeulue, le souvenir du tsunami de 1907 a sauvé presque tout le monde en 2004 : McAdoo et al., 2006) ; ils apaisent l'esprit
/// chaque année ; ils accusent parfois quelqu'un d'avoir attiré le malheur, souvent un étranger ou un mal-aimé (Evans-Pritchard,
/// 1937 ; Miguel, 2005) ; ils ne chassent plus les bêtes du lieu d'où l'épidémie est venue. Le récit passe aux enfants et grossit
/// en passant ; sans rite qui le redise, il s'efface en quelques générations (Vansina, 1985).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Délai pendant lequel un présage peut « s'accomplir » : une mort marquante dans la saison qui suit.</summary>
    private static readonly long FenetrePresage = 90 * Calendrier.SecondesParJour;

    private static Recit NomCat(TypeCatastrophe t) => Nature.Catastrophes.NomR(t);

    /// <summary>Chaque semaine : naissance des récits des catastrophes qui viennent de finir, puis leur vie (rites, présages, transmission, oubli).</summary>
    private void RecitsDesMalheurs(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Social is null) return;
        var change = NaitreDesRecits(vivants, tous, maintenant);
        foreach (var r in Malheurs.Liste)
        {
            if (!r.Vivant) continue;
            change |= VivreLeRecit(r, vivants, tous, maintenant);
        }
        if (change) Malheurs.Indexer();
    }

    // ───────────── Naître ─────────────

    private bool NaitreDesRecits(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Catastrophes is null) return false;
        // Une catastrophe ne donne naissance à un récit qu'une fois : la semaine où elle finit.
        var finies = Catastrophes.Recentes(maintenant - 12 * Calendrier.SecondesParAn)
            .Where(c => c.Fin is { } f && f > maintenant - Semaine && f <= maintenant).OrderBy(c => c.Id).ToList();
        if (finies.Count == 0) return false;
        var change = false;
        var parCommunaute = vivants.Where(h => h.CommunauteId != 0 && h.CaptifDe == 0).GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in finies)
        {
            var mortsDe = c.Victimes.Where(id => id >= 1 && id <= tous.Count && tous[id - 1] is { MortLe: { } m } && m >= c.Debut && m <= maintenant)
                .ToHashSet();
            foreach (var (communaute, membres) in parCommunaute.OrderBy(x => x.Key))
            {
                if (Malheurs.Liste.Any(r => r.CommunauteId == communaute && r.CatastropheId == c.Id)) continue;
                // Un malheur que l'Entité a envoyé à la vue de tous est déjà raconté comme son geste (voir les souvenirs de l'Entité).
                if (c.Origine == Origine.Entite && Cultes is { } cultes
                    && cultes.Souvenirs.Any(s => s.CommunauteId == communaute && !s.Bienfait && s.Le >= c.Debut - Calendrier.SecondesParJour)) continue;
                var morts = c.PertesParCommunaute.GetValueOrDefault(communaute);
                var temoins = membres.Where(h => c.Temoins.Contains(h.Id) || c.Victimes.Contains(h.Id)).ToList();
                if (!Marquante(c, morts, temoins.Count, membres.Count)) continue;
                // La chose revient : le groupe n'en fait pas un nouveau récit, il retrouve l'ancien, et ceux qui l'ont vue y croient
                // davantage (on se souvient d'une catastrophe, pas de chaque éclipse).
                if (Malheurs.Liste.FirstOrDefault(r => r.Vivant && r.CommunauteId == communaute && r.Type == c.Type) is { } ancien)
                {
                    foreach (var h in temoins.OrderBy(h => h.Id))
                        if (!ancien.Croyants.Contains(h.Id) && alea.Reel() < 0.5)
                        {
                            ancien.Croyants.Add(h.Id);
                            ancien.Detaches.Remove(h.Id);
                            ancien.Temoins.Add(h.Id);
                            change = true;
                        }
                    continue;
                }
                // Un malheur marque d'autant plus qu'il a tué : presque toujours un récit quand il y a des morts.
                var chance = Math.Min(0.95, 0.25 + 0.2 * morts + (TypesCatastrophe.EstPhenomene(c.Type) ? 0.35 : 0) + 0.3 * temoins.Count / Math.Max(1, membres.Count));
                if (alea.Reel() >= chance) continue;
                if (NaitreUnRecit(c, Social!.Communautes[communaute - 1], membres, temoins, morts, mortsDe, tous, vivants, maintenant)) change = true;
            }
        }
        return change;
    }

    /// <summary>Le malheur a-t-il assez frappé le groupe pour qu'on cherche à l'expliquer ?</summary>
    private static bool Marquante(Catastrophe c, int morts, int temoins, int membres)
    {
        if (morts >= 1) return true;
        if (TypesCatastrophe.EstPhenomene(c.Type)) return temoins >= 3;
        // Sans mort, seuls les malheurs qui ont fait peur à beaucoup restent dans les mémoires.
        return c.Type is TypeCatastrophe.Seisme or TypeCatastrophe.Tsunami or TypeCatastrophe.Eruption or TypeCatastrophe.Crue
                   or TypeCatastrophe.Incendie or TypeCatastrophe.Cyclone or TypeCatastrophe.Impact
               && temoins >= Math.Max(3, (membres + 1) / 2);
    }

    /// <summary>
    /// L'explication que <paramref name="h"/> se donne d'un malheur, et sa force. Le sens de l'invisible (transcendance, croyance)
    /// porte vers la faute, l'esprit ou le présage ; l'anxieux et le craintif voient une faute et une colère (Gray et Wegner, 2010 :
    /// on cherche un responsable à la souffrance) ; celui qui a appris les saisons et les étoiles voit une chose qui revient.
    /// </summary>
    internal static (Explication E, double Force) Interpreter(Humain h, Catastrophe c, int morts)
    {
        var p = h.Personnalite;
        var invisible = 0.3 + 0.7 * Math.Max(p.Transcendance, h.Croyance / 100);
        var phenomene = TypesCatastrophe.EstPhenomene(c.Type);
        var savoir = Math.Max(ConfianceDans(h, Serie.Saisons), ConfianceDans(h, Serie.Etoiles));
        var revient = c.Type is TypeCatastrophe.Crue or TypeCatastrophe.VagueDeFroid or TypeCatastrophe.Canicule or TypeCatastrophe.Cyclone;
        Span<double> f =
        [
            invisible * (0.4 + p.Nevrosisme + h.Crainte / 100) * (morts > 0 ? 1.4 : 0.6),
            invisible * (0.4 + p.Ouverture) * (phenomene ? 0.6 : 1),
            invisible * (0.5 + p.Nevrosisme) * (phenomene ? 1.6 : 0.25),
            (1 - p.Transcendance) * (0.2 + p.Ouverture) * (savoir >= 0.5 ? 2 : 0.4) * (revient ? 1.5 : 1),
        ];
        var meilleur = 0;
        for (var i = 1; i < f.Length; i++) if (f[i] > f[meilleur]) meilleur = i;
        return ((Explication)meilleur, f[meilleur]);
    }

    /// <summary>Poids de la parole de <paramref name="h"/> dans le groupe : charisme, figure du groupe, prêtre ou prophète, ferveur.</summary>
    private static double PoidsDeParole(Humain h, Communaute c) =>
        0.2 + h.Personnalite.Charisme + (h.Id == c.FigureId ? 0.8 : 0) + (h.Role != RoleReligieux.Aucun ? 0.6 : 0) + 0.3 * h.Ferveur;

    private bool NaitreUnRecit(Catastrophe c, Communaute communaute, List<Humain> membres, List<Humain> temoins, int morts, HashSet<int> mortsDe,
        IReadOnlyList<Humain> tous, List<Humain> vivants, long maintenant)
    {
        var adultes = membres.Where(h => !h.EstEnfant(maintenant)).OrderBy(h => h.Id).ToList();
        if (adultes.Count == 0) return false;
        // Celui qu'on écoute impose son explication : tirage selon le poids de chacun et la force de sa conviction.
        var poids = adultes.Select(h => (h, I: Interpreter(h, c, morts))).Select(x => (x.h, x.I.E, W: Math.Pow(PoidsDeParole(x.h, communaute), 2) * x.I.Force)).ToList();
        var total = poids.Sum(x => x.W);
        if (total <= 0) return false;
        var tirage = alea.Reel() * total;
        var (porteur, explication, _) = poids[^1];
        foreach (var x in poids)
        {
            tirage -= x.W;
            if (tirage < 0)
            {
                (porteur, explication) = (x.h, x.E);
                break;
            }
        }

        // Qui y croit : d'abord les adultes, puis les enfants, qui croient ce que croient leurs parents.
        var croyants = new HashSet<int>();
        var detaches = new HashSet<int>();
        var vecus = temoins.Select(h => h.Id).ToHashSet();
        foreach (var h in adultes)
        {
            var chance = h.Id == porteur.Id ? 1 : ChanceDeCroire(h, explication, c, morts, vecus.Contains(h.Id), PerteProche(h, mortsDe), porteur);
            if (alea.Reel() < chance) croyants.Add(h.Id);
            else detaches.Add(h.Id);
        }
        foreach (var h in membres.Where(h => h.EstEnfant(maintenant)).OrderBy(h => h.Id))
        {
            var parent = croyants.Contains(h.MereId) || croyants.Contains(h.PereId);
            if (alea.Reel() < (parent ? 0.9 : 0.3)) croyants.Add(h.Id);
        }
        // Une explication que personne d'autre ne reprend meurt avec celui qui l'a eue.
        if (croyants.Count < 2) return false;

        var lieu = explication != Explication.Nature
                   && (c.Type is TypeCatastrophe.Tsunami or TypeCatastrophe.Crue or TypeCatastrophe.Eruption or TypeCatastrophe.Impact
                       || c.Type == TypeCatastrophe.Incendie && morts >= 2);
        var tabou = c.Type == TypeCatastrophe.Epidemie && explication is Explication.Colere or Explication.Esprit && morts >= 1;
        var rite = explication == Explication.Esprit || explication == Explication.Colere && alea.Reel() < 0.4 + 0.4 * porteur.Personnalite.Transcendance;
        var r = new RecitCatastrophe
        {
            Id = Malheurs.Liste.Count + 1,
            CommunauteId = communaute.Id,
            CatastropheId = c.Id,
            Type = c.Type,
            Explication = explication,
            NeLe = maintenant,
            PorteurId = porteur.Id,
            X = c.X,
            Y = c.Y,
            Rayon = lieu || tabou ? Math.Clamp(c.Rayon / 2, 2, 10) : 0,
            Lieu = lieu,
            Signes = c.Type is TypeCatastrophe.Tsunami or TypeCatastrophe.Eruption or TypeCatastrophe.Crue or TypeCatastrophe.Seisme,
            Rite = rite,
            Tabou = tabou,
            Morts = morts,
            MortsRacontes = morts,
            Croyants = croyants,
            Temoins = [.. vecus.Where(croyants.Contains)],
            Detaches = detaches,
        };
        r.EvenementId = Noter(maintenant, TypeEvenement.Religion, porteur, null,
            new Recit($"recit.ne.{explication.ToString().ToLowerInvariant()}", ("h", porteur), ("c", communaute), ("cat", NomCat(c.Type)),
                ("n", croyants.Count)), c.Id, significatif: true);
        Malheurs.Ajouter(r);

        foreach (var id in croyants)
        {
            var h = tous[id - 1];
            // La faute et le présage font craindre l'invisible ; la cause naturelle rassure un peu.
            if (explication is Explication.Colere or Explication.Presage) h.Crainte = Math.Min(100, h.Crainte + 4 + 6 * h.Personnalite.Nevrosisme);
            else if (explication == Explication.Nature) h.Peur = Math.Max(0, h.Peur - 5);
        }
        if (lieu) Noter(maintenant, TypeEvenement.Religion, porteur, null, new Recit("recit.lieu", ("c", communaute), ("cat", NomCat(c.Type))), r.EvenementId);
        if (r.Signes) Noter(maintenant, TypeEvenement.Religion, porteur, null, new Recit($"recit.signes.{c.Type}", ("c", communaute)), r.EvenementId);
        if (tabou) Noter(maintenant, TypeEvenement.Religion, porteur, null, new Recit("recit.tabou", ("c", communaute)), r.EvenementId);
        if (rite)
        {
            Noter(maintenant, TypeEvenement.Religion, porteur, null, new Recit("recit.rite.institue", ("h", porteur), ("c", communaute), ("cat", NomCat(c.Type))),
                r.EvenementId);
            // La religion du groupe en fait un précepte : on apaise par des offrandes.
            if (Cultes is { } cultes && Majorite(vivants, communaute.Id, maintenant) is > 0 and var religion)
                AjouterDogme(cultes.Religions[religion - 1], Precepte.Offrandes, 0, OrigineDogme.Catastrophe, porteur, r.EvenementId, maintenant);
        }
        if (explication == Explication.Colere && morts >= 1) Accuser(r, communaute, membres, tous, maintenant);
        return true;
    }

    /// <summary>Le malheur lui a pris un proche (compagnon, parent, enfant).</summary>
    private static bool PerteProche(Humain h, HashSet<int> mortsDe) =>
        mortsDe.Contains(h.PartenaireId) || mortsDe.Contains(h.MereId) || mortsDe.Contains(h.PereId)
        || h.Liens.Exists(l => l.Affection >= 60 && mortsDe.Contains(l.Id));

    /// <summary>
    /// Chance que <paramref name="h"/> croie l'explication : plus s'il l'a vécue ou y a perdu un proche, si elle répond à ce qu'il en
    /// pense lui-même, et s'il a confiance en celui qui la porte.
    /// </summary>
    internal static double ChanceDeCroire(Humain h, Explication e, Catastrophe c, int morts, bool temoin, bool perte, Humain porteur)
    {
        var (siens, _) = Interpreter(h, c, morts);
        var accord = siens == e ? 1.4 : e == Explication.Nature ? 0.5 + 0.5 * h.Personnalite.Ouverture : 0.75;
        var confiance = 0.8 + 0.4 * Math.Clamp(h.AffectionPour(porteur.Id) / 100, -1, 1);
        return Math.Clamp((0.3 + (temoin ? 0.3 : 0) + (perte ? 0.2 : 0)) * accord * confiance, 0, 0.97);
    }

    /// <summary>
    /// La colère de l'invisible veut une faute, et souvent un fautif : celui qu'on aimait le moins, l'étranger venu d'ailleurs, celui qui
    /// avait la garde du feu. Les accusations de sorcellerie visent surtout les marginaux et les mal-aimés (Evans-Pritchard, 1937) ;
    /// elles montent avec les malheurs (Miguel, 2005). Les croyants les moins compatissants lui en veulent.
    /// </summary>
    private void Accuser(RecitCatastrophe r, Communaute communaute, List<Humain> membres, IReadOnlyList<Humain> tous, long maintenant)
    {
        var croyants = membres.Where(h => r.Croyants.Contains(h.Id) && !h.EstEnfant(maintenant)).ToList();
        if (croyants.Count < 2) return;
        var compassion = croyants.Average(h => h.Personnalite.Moral.Compassion);
        if (alea.Reel() >= 0.6 * (1 - compassion)) return;
        var c = Catastrophes!.Trouver(r.CatastropheId);
        Humain? accuse = null;
        var pire = 0.8;
        foreach (var x in membres.Where(h => !h.EstEnfant(maintenant)).OrderBy(h => h.Id))
        {
            if (x.Id == r.PorteurId || x.Id == communaute.FigureId) continue;
            double score = 0;
            foreach (var m in croyants)
            {
                if (m.Id == x.Id) continue;
                score += Math.Max(0, -m.AffectionPour(x.Id)) / 100;
                foreach (var g in m.Griefs) if (g.ContreId == x.Id) score += 0.3 * g.Intensite / 100;
            }
            score = score / croyants.Count * 3;
            if (x.CommunauteNaissance != communaute.Id) score += 0.4;
            if (c is { ResponsableId: var resp } && resp == x.Id) score += 1.5;
            if (score > pire) (pire, accuse) = (score, x);
        }
        if (accuse is null) return;
        r.AccuseId = accuse.Id;
        var porteur = tous[r.PorteurId - 1];
        var id = Noter(maintenant, TypeEvenement.Religion, porteur, accuse,
            new Recit("recit.bouc", ("h", porteur), ("x", accuse), ("c", communaute), ("cat", NomCat(r.Type))), r.EvenementId, significatif: true,
            karma: -Regard.Pardonner / 2);
        foreach (var m in croyants)
        {
            if (m.Id == accuse.Id || m.Personnalite.Moral.Compassion >= 0.55) continue;
            var colere = Math.Min(70, 20 + 50 * (1 - m.Personnalite.Moral.Compassion) * (0.5 + m.Personnalite.Nevrosisme));
            VieSociale.AjouterGrief(m, new Grief(accuse.Id, MotifGrief.Malheur, colere, colere, id));
        }
    }

    // ───────────── Vivre, passer, s'oublier ─────────────

    private bool VivreLeRecit(RecitCatastrophe r, List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var change = false;
        // Les morts n'y croient plus.
        if (r.Croyants.RemoveWhere(id => !tous[id - 1].EstVivant) > 0) change = true;
        r.Temoins.RemoveWhere(id => !tous[id - 1].EstVivant);

        var communaute = Social!.Communautes[r.CommunauteId - 1];
        var porteur = tous[r.PorteurId - 1];

        // Le présage s'accomplit si une mort marquante suit : la figure du groupe, un prêtre, ou trois morts dans la saison.
        if (r.Explication == Explication.Presage && r.AccompliLe == 0 && maintenant - r.NeLe <= FenetrePresage)
        {
            var mortsSuivants = tous.Where(h => h.MortLe is { } m && m > r.NeLe && m <= maintenant && h.CommunauteId == r.CommunauteId).ToList();
            var marquante = mortsSuivants.FirstOrDefault(h => h.Id == communaute.FigureId || h.Role != RoleReligieux.Aucun)
                            ?? (mortsSuivants.Count >= 3 ? mortsSuivants.MaxBy(h => h.MortLe) : null);
            if (marquante is not null)
            {
                r.AccompliLe = maintenant;
                Noter(maintenant, TypeEvenement.Religion, marquante, null,
                    new Recit("recit.presage.accompli", ("x", marquante), ("c", communaute), ("cat", NomCat(r.Type))), marquante.MortEvenementId, significatif: true);
                foreach (var id in r.Croyants)
                {
                    var h = tous[id - 1];
                    h.Croyance = Math.Min(100, h.Croyance + 5);
                    h.Crainte = Math.Min(100, h.Crainte + 5);
                }
                // Ceux qui doutaient se laissent convaincre (Festinger : la prophétie confirmée renforce la foi).
                foreach (var h in vivants.Where(h => h.CommunauteId == r.CommunauteId && !r.Croyants.Contains(h.Id)).OrderBy(h => h.Id))
                    if (alea.Reel() < 0.3 * (0.5 + h.Personnalite.Transcendance))
                    {
                        r.Croyants.Add(h.Id);
                        r.Detaches.Remove(h.Id);
                    }
                change = true;
            }
        }

        // Le rite revient chaque année, à la saison du malheur : les croyants l'apaisent ensemble et se redisent le récit.
        var age = maintenant - r.NeLe;
        var rite = r.Rite && age >= Calendrier.SecondesParAn && age % Calendrier.SecondesParAn < Semaine && maintenant - r.DernierRiteLe > 300 * Calendrier.SecondesParJour;
        if (age >= Calendrier.SecondesParAn && age % Calendrier.SecondesParAn < Semaine)
        {
            // En passant de bouche en bouche, le malheur grossit : plus vite quand plus personne ne l'a vécu (Vansina, 1985).
            if (r.Morts > 0 && r.MortsRacontes < 3 * r.Morts)
            {
                var croissance = 1 + (r.Temoins.Count == 0 ? 0.08 : 0.02) * (0.5 + alea.Reel());
                r.MortsRacontes = Math.Min(3 * r.Morts, Math.Max(r.MortsRacontes + (r.Temoins.Count == 0 ? 1 : 0), (int)Math.Round(r.MortsRacontes * croissance)));
            }
        }
        var presents = vivants.Where(h => r.Croyants.Contains(h.Id) && h.CommunauteId == r.CommunauteId).ToList();
        if (rite && presents.Count >= 2)
        {
            r.DernierRiteLe = maintenant;
            var guide = presents.FirstOrDefault(h => h.Id == r.PorteurId) ?? presents.MaxBy(h => PoidsDeParole(h, communaute))!;
            Noter(maintenant, TypeEvenement.Religion, guide, null,
                new Recit($"recit.rite.{(r.Explication == Explication.Esprit ? "esprit" : "colere")}", ("h", guide), ("c", communaute), ("cat", NomCat(r.Type)),
                    ("n", presents.Count)), r.EvenementId);
            foreach (var h in presents)
            {
                // Une offrande coûte : un peu de ce qu'on aurait mangé. Le rite apaise la crainte (Sosis et Bressler, 2003).
                h.Reserves = Math.Max(0, h.Reserves - 1 - alea.Reel());
                h.Crainte = Math.Max(0, h.Crainte - 2 - 3 * h.Personnalite.Transcendance);
            }
            change = true;
        }

        // Les enfants l'apprennent de leurs parents ; les nouveaux venus, des croyants qui les entourent. Chacun ne l'entend qu'une fois.
        foreach (var h in vivants)
        {
            if (r.Croyants.Contains(h.Id) || r.Detaches.Contains(h.Id)) continue;
            if (h.AgeEnAnnees(maintenant) < 4) continue;
            double chance;
            if (r.Croyants.Contains(h.MereId) || r.Croyants.Contains(h.PereId)) chance = r.Rite ? 0.95 : 0.85;
            else if (h.CommunauteId == r.CommunauteId)
            {
                var proches = h.Liens.Count(l => l.Affection >= 30 && r.Croyants.Contains(l.Id));
                if (proches == 0 && presents.Count < 3) continue;
                chance = Math.Min(0.8, (0.15 + 0.15 * proches) * (0.5 + h.Personnalite.Transcendance) * (r.Explication == Explication.Nature ? 0.5 + h.Personnalite.Ouverture : 1));
            }
            else continue;
            if (alea.Reel() < chance) r.Croyants.Add(h.Id);
            else r.Detaches.Add(h.Id);
            change = true;
        }

        // Ceux qui ne l'ont pas vécu l'oublient peu à peu, moins vite quand le rite le redit chaque année.
        foreach (var id in r.Croyants.Where(id => !r.Temoins.Contains(id)).OrderBy(id => id).ToList())
        {
            var h = tous[id - 1];
            var annees = 25 * (r.Rite ? 3 : 1) * (0.5 + h.Personnalite.Transcendance);
            if (alea.Reel() >= 1 / (52 * annees)) continue;
            r.Croyants.Remove(id);
            r.Detaches.Add(id);
            change = true;
        }

        if (r.Croyants.Count == 0)
        {
            r.OublieLe = maintenant;
            Noter(maintenant, TypeEvenement.Religion, porteur, null, new Recit("recit.oublie", ("c", communaute), ("cat", NomCat(r.Type))), r.EvenementId);
            change = true;
        }
        return change;
    }
}
