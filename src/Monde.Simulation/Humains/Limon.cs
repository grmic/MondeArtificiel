using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// « Là où l'eau est passée, tout repousse » (mondes de la génération 2, depuis la 0.32.7). Personne ne le sait au départ : celui
/// qui a vu une plaine sous l'eau, et qui y cueille plus tard une abondance inhabituelle, peut faire le lien ; une fois ne suffit
/// souvent pas. Il peut aussi en douter, quand il revient sur une terre noyée et la trouve pauvre. Qui le croit retourne cueillir
/// dans la plaine une fois l'eau retirée, au lieu de la fuir ; le savoir se raconte, s'enseigne, et se perd s'il ne sert pas.
/// C'est la porte d'entrée de la culture de décrue, la plus ancienne agriculture des grandes vallées.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Du limon qui se voit à la pousse : au moins 15 % de plus.</summary>
    internal const int LimonVisible = 1_500;

    /// <summary>Le savoir du limon, s'il l'a.</summary>
    internal static Savoir? SavoirDuLimon(Humain h) => h.Savoirs.Find(s => s.Effet == Effet.Limon);

    /// <summary>Un lieu qu'il a vu sous l'eau, tout près de (x, y), il y a entre <paramref name="min"/> et <paramref name="max"/> jours.</summary>
    private LieuConnu? CrueVue(Humain h, int x, int y, int rayon, int min, int max) => CrueVue(h, x, y, rayon, min, max, _maintenant);

    internal static LieuConnu? CrueVue(Humain h, int x, int y, int rayon, int min, int max, long maintenant)
    {
        LieuConnu? meilleur = null;
        foreach (var l in h.Memoire.Lieux)
        {
            if (l.Type != TypeLieu.Crue || Distance(l.X, l.Y, x, y) > rayon) continue;
            var jours = (maintenant - l.VuLe) / Calendrier.SecondesParJour;
            if (jours < min || jours > max) continue;
            if (meilleur is null || Distance(l.X, l.Y, x, y) < Distance(meilleur.X, meilleur.Y, x, y)) meilleur = l;
        }
        return meilleur;
    }

    /// <summary>
    /// Il cueille sur la case <paramref name="i"/> : s'il l'a vue sous l'eau, il peut remarquer que tout y repousse plus fort, ou,
    /// s'il le croyait et la trouve pauvre, en douter. Une fois par mois au plus.
    /// </summary>
    internal void ConstaterLeLimon(Humain h, int i, bool abondant)
    {
        if (h.EstEnfant(_maintenant))
        {
            ApprendreLeLimon(h, i, abondant);
            return;
        }
        if (CrueVue(h, h.X, h.Y, 2, 15, 400) is null) return;
        var sait = SavoirDuLimon(h);
        if (sait is not null && _maintenant - sait.UtiliseLe < 30 * Calendrier.SecondesParJour) return;
        var riche = abondant && ecosysteme.Limon[i] >= LimonVisible;
        if (sait is null)
        {
            if (!riche) return;
            var p = h.Personnalite;
            var crues = h.Memoire.Lieux.Count(l => l.Type == TypeLieu.Crue);
            var chance = 0.004 * (0.3 + Croyances.Analytique(h)) * (0.5 + p.Ouverture) * (1 + 0.5 * (crues - 1));
            if (alea.Reel() >= chance) return;
            Retenir(h, Geste.Patienter, Matiere.Eau, Matiere.Plante, Effet.Limon, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, _tous,
                recit: new Recit("limon.constat", ("h", h)));
            return;
        }
        // Il le savait : la terre noyée confirme, ou le fait douter (une sécheresse, d'autres qui ont tout cueilli avant lui).
        if (riche || ecosysteme.Limon[i] == 0 && ecosysteme.Plantes[i] < 1000)
            Retenir(h, Geste.Patienter, Matiere.Eau, Matiere.Plante, Effet.Limon, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, _tous, succes: riche);
    }

    /// <summary>
    /// Un enfant cueille avec un adulte des siens qui croit au limon, sur une terre que la crue a rendue riche : il l'entend dire
    /// pourquoi tout pousse si bien ici, et peut le retenir.
    /// </summary>
    private void ApprendreLeLimon(Humain h, int i, bool abondant)
    {
        if (!abondant || ecosysteme.Limon[i] < LimonVisible || h.AgeEnAnnees(_maintenant) < 5 || SavoirDuLimon(h) is not null) return;
        if (alea.Reel() >= 0.02 * (0.5 + h.Personnalite.Ouverture)) return;
        Humain? maitre = null;
        foreach (var o in _tous)
        {
            if (o.Id == h.Id || !o.EstVivant || o.CommunauteId != h.CommunauteId || o.CommunauteId == 0 || Distance(o.X, o.Y, h.X, h.Y) > 2
                || o.EstEnfant(_maintenant) || SavoirDuLimon(o) is not { Confiance: >= 0.5 }) continue;
            maitre = o;
            break;
        }
        if (maitre is null) return;
        Retenir(h, Geste.Patienter, Matiere.Eau, Matiere.Plante, Effet.Limon, 0, Symptome.Aucun, OrigineSavoir.Imitation, maitre.Id, _tous);
    }

    /// <summary>
    /// Il a faim et croit que la plaine noyée repousse : il y retourne, une fois l'eau retirée (pas tant qu'elle est dangereuse).
    /// Vrai s'il se met en route.
    /// </summary>
    internal bool AllerAuLimon(Humain h)
    {
        if (SavoirDuLimon(h) is not { Confiance: >= 0.5 }) return false;
        var lieu = CrueVue(h, h.X, h.Y, 25, 10, 270);
        if (lieu is null || Distance(lieu.X, lieu.Y, h.X, h.Y) <= 1 || Catastrophes?.EstDangereuse(carte.Indice(lieu.X, lieu.Y)) == true) return false;
        _motif = Motif.Souvenir;
        Avancer(h, lieu.X, lieu.Y, Activite.ChercherNourriture);
        return h.CibleX >= 0;
    }
}

public sealed partial class VieSociale
{
    /// <summary>Génération du monde (le camp sur la plaine de crue : génération 2, depuis la 0.32.8).</summary>
    public int Generation { get; set; }

    /// <summary>
    /// Plusieurs des siens croient que la plaine noyée repousse plus fort, et l'un d'eux en connaît une à une ou deux journées, dont
    /// l'eau s'est retirée et qui vaut nettement mieux que leurs terres : le groupe peut décider d'aller y camper tant qu'elle est
    /// riche, sans attendre la disette. Il y reste ou en repart ensuite comme de toute terre (voir <see cref="Disette"/>). Sans
    /// calendrier imposé : c'est le savoir des siens, leur nombre et leur faim qui décident. Vrai si le groupe se met en route.
    /// </summary>
    internal bool SuivreLaCrue(Communaute c, List<Humain> groupe, Ecosysteme nature, long maintenant)
    {
        var adultes = groupe.Where(h => !h.EstEnfant(maintenant)).ToList();
        var croyants = adultes.Where(h => Comportement.SavoirDuLimon(h) is { Confiance: >= 0.5 }).OrderBy(h => h.Id).ToList();
        if (croyants.Count < 2) return false;
        var part = croyants.Count / (double)adultes.Count;
        // Une décision qui se discute : d'autant plus vite prise que beaucoup y croient et que les terres d'ici s'appauvrissent.
        if (alea.Reel() >= 0.4 * part * (1.5 - Math.Min(1, c.Abondance))) return false;
        var meilleur = (X: -1, Y: -1, Abondance: c.Abondance + 0.15, Qui: (Humain?)null);
        foreach (var h in croyants)
        {
            var lieu = Comportement.CrueVue(h, c.CentreX, c.CentreY, DistanceMigrationMax, 20, 240, maintenant);
            if (lieu is null) continue;
            var d = Math.Max(Math.Abs(lieu.X - c.CentreX), Math.Abs(lieu.Y - c.CentreY));
            var i = Carte.Indice(lieu.X, lieu.Y);
            if (d <= 4 || nature.Limon[i] < Comportement.LimonVisible) continue;
            var abondance = nature.Abondance(lieu.X, lieu.Y, 3);
            if (abondance > meilleur.Abondance) meilleur = (lieu.X, lieu.Y, abondance, h);
        }
        if (meilleur.Qui is not { } guide) return false;
        (c.DestinationX, c.DestinationY) = (meilleur.X, meilleur.Y);
        Noter(maintenant, TypeEvenement.Communaute, guide, null,
            new Recit("limon.part", ("c", c), ("vers", Lieux.VersR(c.CentreX, c.CentreY, meilleur.X, meilleur.Y)),
                ("d", Lieux.DistanceR(c.CentreX, c.CentreY, meilleur.X, meilleur.Y)), ("g", guide)), communaute: c.Id);
        return true;
    }
}
