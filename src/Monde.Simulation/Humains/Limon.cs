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
    private const int LimonVisible = 1_500;

    /// <summary>Le savoir du limon, s'il l'a.</summary>
    private static Savoir? SavoirDuLimon(Humain h) => h.Savoirs.Find(s => s.Effet == Effet.Limon);

    /// <summary>Un lieu qu'il a vu sous l'eau, tout près de (x, y), il y a entre <paramref name="min"/> et <paramref name="max"/> jours.</summary>
    private LieuConnu? CrueVue(Humain h, int x, int y, int rayon, int min, int max)
    {
        LieuConnu? meilleur = null;
        foreach (var l in h.Memoire.Lieux)
        {
            if (l.Type != TypeLieu.Crue || Distance(l.X, l.Y, x, y) > rayon) continue;
            var jours = (_maintenant - l.VuLe) / Calendrier.SecondesParJour;
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
        if (h.EstEnfant(_maintenant) || CrueVue(h, h.X, h.Y, 2, 15, 400) is null) return;
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
