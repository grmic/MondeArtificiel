using Monde.Simulation.Monde;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'une communauté fait d'une terre au-delà de la mer (0.31.5), une fois par semaine : y envoyer quelques hardis voir
/// ce qu'il en est ; une fois qu'on y a posé le pied, y laisser partir un couple qui veut y vivre ; et, quand la disette ne
/// laisse aucune terre meilleure à portée de marche, y partir tous ensemble. Rien n'oblige à rien : il faut qu'on se
/// souvienne de cette terre, et que quelqu'un ait envie d'y aller.
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>Les premières des groupes avec les terres d'outre-mer (le même registre que celui du comportement).</summary>
    public Outremer? Outremer { get; set; }

    private void PrendreLaMer(List<Communaute> actives, Dictionary<int, List<Humain>> membres, Carte carte, long maintenant)
    {
        if (Outremer is null) return;
        foreach (var c in actives)
        {
            if (!membres.TryGetValue(c.Id, out var groupe)) continue;
            var ici = carte.Mer.Masse[carte.Indice(c.CentreX, c.CentreY)];
            if (ici == 0) continue;
            // Ce que la communauté sait des terres au loin : ce qu'elle en a vu, ce que ses membres en ont entendu dire.
            var terres = Outremer.TerresConnues(c.Id).Where(d => d.Masse != ici)
                .Select(d => (d.Masse, d.X, d.Y, Pied: d.Sorte == SorteOutremer.Pied)).ToList();
            foreach (var h in groupe.Where(h => !h.EstEnfant(maintenant)).OrderBy(h => h.Id))
            {
                foreach (var l in h.Memoire.Lieux)
                {
                    if (l.Type != TypeLieu.TerreAuLoin) continue;
                    var m = carte.Mer.Masse[carte.Indice(l.X, l.Y)];
                    if (m != 0 && m != ici && !terres.Exists(t => t.Masse == m)) terres.Add((m, l.X, l.Y, false));
                }
            }
            if (terres.Count == 0 || groupe.Exists(h => h.Mer.Active)) continue;
            var terre = terres.MinBy(t => Nature.Echelle.Kilometres(c.CentreX, c.CentreY, t.X, t.Y));
            var adultes = groupe.Where(h => !h.EstEnfant(maintenant) && h.AgeEnAnnees(maintenant) < 55).ToList();
            if (adultes.Count == 0) continue;
            var figure = groupe.Find(h => h.Id == c.FigureId) ?? groupe[0];

            // La disette, et rien de mieux à portée de marche : partir tous ensemble par la mer, vers une terre où l'un des siens a
            // déjà posé le pied (on sait qu'on peut l'atteindre).
            if (terre.Pied && c.Disette && c.DestinationX < 0 && c.FusionVersId == 0 && adultes.Exists(h => h.Savoirs.Exists(s => s.Effet == Effet.Navigation && s.Confiance >= 0.3)))
            {
                (c.DestinationX, c.DestinationY) = (terre.X, terre.Y);
                Noter(maintenant, TypeEvenement.Mer, figure, null, new Recit("terr.partMer", ("c", c), ("vers", Lieux.VersR(c.CentreX, c.CentreY, terre.X, terre.Y)),
                    ("d", Lieux.DistanceR(c.CentreX, c.CentreY, terre.X, terre.Y))), c.DisetteEvenementId, communaute: c.Id, significatif: true);
                continue;
            }
            static double Audace(Humain h) => h.Personnalite.Curiosite * (1 - h.Personnalite.Prudence);
            if (!terre.Pied)
            {
                // Une expédition : rare (une entreprise de plusieurs jours, au risque de la vie), plus fréquente s'il y a des hardis parmi les siens.
                if (alea.Reel() >= 0.004 + 0.03 * adultes.Max(Audace)) continue;
                var volontaires = adultes.Where(h => Audace(h) >= 0.2 && !h.EstEnceinte && h.CaptifDe == 0 && h.VisiteChezId == 0)
                    .OrderByDescending(Audace).ThenBy(h => h.Id).Take(1 + alea.Entier(3)).ToList();
                var partis = new List<Humain>();
                foreach (var h in volontaires)
                {
                    if (Comportement.Voyage(carte, h, MotifMer.Expedition, terre.X, terre.Y, maintenant) is not { } voyage) continue;
                    h.Mer = voyage;
                    partis.Add(h);
                }
                if (partis.Count == 0) continue;
                Noter(maintenant, TypeEvenement.Mer, partis[0], partis.Count > 1 ? partis[1] : null, new Recit("terr.expedition", ("c", c), ("h", partis[0]),
                    ("n", partis.Count), ("vers", Lieux.VersR(c.CentreX, c.CentreY, terre.X, terre.Y))), communaute: c.Id);
                continue;
            }
            // On y a posé le pied : un couple peut vouloir y vivre, plus volontiers quand les terres d'ici s'épuisent.
            if (alea.Reel() >= 0.02 * (c.Abondance < 0.8 ? 2 : 1) * (groupe.Count > 25 ? 1.5 : 1)) continue;
            var couple = adultes.Where(h => h.PartenaireId > h.Id && h.AgeEnAnnees(maintenant) < 45)
                .Select(h => (A: h, B: groupe.Find(o => o.Id == h.PartenaireId && o.EstVivant && !o.EstEnfant(maintenant))))
                .Where(p => p.B is not null && (Audace(p.A) + Audace(p.B!)) / 2 >= 0.15)
                .OrderByDescending(p => Audace(p.A) + Audace(p.B!)).FirstOrDefault();
            if (couple.A is null) continue;
            var colons = new List<Humain>();
            foreach (var h in new[] { couple.A, couple.B! })
            {
                if (Comportement.Voyage(carte, h, MotifMer.Colon, terre.X, terre.Y, maintenant) is not { } voyage) continue;
                h.Mer = voyage;
                colons.Add(h);
            }
            if (colons.Count > 0)
                Noter(maintenant, TypeEvenement.Mer, couple.A, couple.B, new Recit("terr.colons", ("c", c), ("a", couple.A), ("b", couple.B!),
                    ("vers", Lieux.VersR(c.CentreX, c.CentreY, terre.X, terre.Y))), communaute: c.Id);
        }
    }
}
