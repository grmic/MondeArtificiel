using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Ce qu'une communauté pense des amours entre personnes de même sexe.</summary>
public enum UsageMoeurs : sbyte
{
    Reprouvees = -1,
    Neutre = 0,
    Acceptees = 1,
}

/// <summary>
/// Mœurs des communautés : leur regard sur les amours entre personnes de même sexe, et leur idéal de beauté.
/// Ni l'un ni l'autre n'est fixé : ils naissent du tempérament des membres et changent avec ce que le groupe vit.
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>
    /// Tolérance d'un humain envers ces amours, d'après son caractère : surtout l'ouverture d'esprit, puis la bienveillance,
    /// et un peu le goût de l'ordre (Shackelford et Besser, 2007 ; revue de Herek et McLemore, 2013) ; le sens de la pureté
    /// (sensibilité au dégoût) les fait réprouver (Inbar et al., 2009).
    /// </summary>
    public static double TemperamentMoeurs(Personnalite p) =>
        Math.Clamp(0.5 + 1.0 * (p.Ouverture - 0.5) + 0.6 * (p.Agreabilite - 0.5) - 0.4 * (p.Conscience - 0.5)
                   - 0.5 * (p.Moral.Fondements.Purete - 0.5), 0, 1);

    /// <summary>Tolérance de l'entourage d'un humain : l'usage de sa communauté, sinon son propre tempérament.</summary>
    public double ToleranceMoeursDe(Humain h) =>
        CommunauteDe(h) is { ToleranceMoeurs: >= 0 } c ? c.ToleranceMoeurs : TemperamentMoeurs(h.Personnalite);

    /// <summary>Idéal de beauté d'un humain : celui de sa communauté, sinon celui de la communauté qu'il a quittée, sinon l'idéal commun.</summary>
    public IdealBeaute IdealDe(Humain h) =>
        CommunauteDe(h)?.Ideal
        ?? (h.CommunauteId > 0 && h.CommunauteId <= Communautes.Count ? Communautes[h.CommunauteId - 1].Ideal : IdealBeaute.ParDefaut);

    /// <summary>Une communauté qui naît d'une autre en garde les mœurs ; sinon elles viennent du tempérament de ses adultes.</summary>
    private (double Tolerance, IdealBeaute Ideal) MoeursInitiales(List<Humain> groupe, List<Communaute> communautes, long maintenant)
    {
        var origine = groupe.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId)
            .Select(c => (Id: c.Key, Nombre: c.Count())).OrderByDescending(c => c.Nombre).ThenBy(c => c.Id).FirstOrDefault();
        var adultes = groupe.Where(h => !h.EstEnfant(maintenant)).ToList();
        if (origine.Id != 0 && origine.Nombre * 2 > groupe.Count && communautes[origine.Id - 1] is { ToleranceMoeurs: >= 0 } c)
        {
            return (c.ToleranceMoeurs, c.Ideal);
        }
        return (adultes.Count == 0 ? 0.5 : adultes.Average(h => TemperamentMoeurs(h.Personnalite)), Attirance.IdealInitial(adultes, null));
    }

    /// <summary>Usage selon la tolérance, avec un écart entre l'entrée et la sortie pour éviter les allers-retours.</summary>
    private static UsageMoeurs ClasserMoeurs(double tolerance, UsageMoeurs avant) => avant switch
    {
        UsageMoeurs.Reprouvees when tolerance <= 0.42 => UsageMoeurs.Reprouvees,
        UsageMoeurs.Acceptees when tolerance >= 0.58 => UsageMoeurs.Acceptees,
        _ when tolerance < 0.35 => UsageMoeurs.Reprouvees,
        _ when tolerance > 0.65 => UsageMoeurs.Acceptees,
        _ => UsageMoeurs.Neutre,
    };

    /// <summary>Corpulence admirée en trois classes, avec le même écart entre l'entrée et la sortie.</summary>
    private static int NoteIdeal(double corpulence, int avant) => avant switch
    {
        1 when corpulence >= 0.55 => 1,
        -1 when corpulence <= 0.45 => -1,
        _ when corpulence > 0.6 => 1,
        _ when corpulence < 0.4 => -1,
        _ => 0,
    };

    /// <summary>
    /// Chaque semaine : la tolérance de chaque communauté glisse vers le tempérament de ses adultes ; ceux qui y vivent leurs
    /// amours sans drame l'habituent un peu (Herek et Capitanio, 1996) ; son idéal de beauté glisse vers l'apparence de ses
    /// membres et de sa figure, et vers les corps en chair quand la nourriture manque. Les changements sont notés.
    /// </summary>
    private void Moeurs(List<Humain> humains, List<Communaute> communautes, long maintenant)
    {
        var groupes = humains.Vivants().Where(h => h.EstVivant && h.CommunauteId != 0 && !h.EstEnfant(maintenant))
            .GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in communautes)
        {
            if (!c.Active || !groupes.TryGetValue(c.Id, out var adultes)) continue;
            var temperament = adultes.Average(h => TemperamentMoeurs(h.Personnalite));
            if (c.ToleranceMoeurs < 0)
            {
                // Communauté enregistrée avant que ses mœurs existent : elles naissent du tempérament de ses adultes.
                c.ToleranceMoeurs = temperament;
                c.UsageMoeurs = ClasserMoeurs(temperament, UsageMoeurs.Neutre);
                c.Ideal = Attirance.IdealInitial(adultes, null);
                c.IdealNote = NoteIdeal(c.Ideal.Corpulence, 0);
            }
            // Couples de même sexe connus de tous, qui vivent en paix : le groupe s'y habitue.
            var couples = adultes.Count(h => h.PartenaireId > h.Id && humains[h.PartenaireId - 1] is { EstVivant: true } p && p.Sexe == h.Sexe);
            c.ToleranceMoeurs = Math.Clamp(c.ToleranceMoeurs + 0.015 * (temperament - c.ToleranceMoeurs) + 0.002 * couples, 0, 1);
            var usage = ClasserMoeurs(c.ToleranceMoeurs, c.UsageMoeurs);
            var figure = humains[c.FigureId - 1];
            if (usage != c.UsageMoeurs)
            {
                var texte = (c.UsageMoeurs, usage) switch
                {
                    (_, UsageMoeurs.Reprouvees) => new Recit("j.moeurs.reprouvees", ("c", c)),
                    (_, UsageMoeurs.Acceptees) => new Recit("j.moeurs.acceptees", ("c", c)),
                    (UsageMoeurs.Reprouvees, _) => new Recit("j.moeurs.finBlame", ("c", c)),
                    _ => new Recit("j.moeurs.debutMal", ("c", c)),
                };
                c.UsageMoeurs = usage;
                Noter(maintenant, TypeEvenement.Communaute, figure, null, texte, c.AffaireMoeursId, communaute: c.Id);
            }

            var abondance = c.Abondance;
            c.Ideal = Attirance.Deriver(c.Ideal, adultes, adultes.Contains(figure) ? figure : null, c.Disette, abondance);
            var note = NoteIdeal(c.Ideal.Corpulence, c.IdealNote);
            if (note == c.IdealNote) continue;
            Noter(maintenant, TypeEvenement.Communaute, figure, null, note switch
            {
                1 when c.Disette => new Recit("j.ideal.chairDisette", ("c", c)),
                1 => new Recit("j.ideal.chair", ("c", c)),
                -1 => new Recit("j.ideal.mince", ("c", c)),
                _ => new Recit("j.ideal.moyen", ("c", c)),
            }, c.Disette ? c.DisetteEvenementId : 0, communaute: c.Id);
            c.IdealNote = note;
        }
    }

    /// <summary>
    /// <paramref name="temoin"/> surprend <paramref name="a"/> avec <paramref name="b"/> (ou repousse les avances de
    /// <paramref name="a"/> quand <paramref name="b"/> est null). Sa réaction dépend de son caractère et de l'usage de sa
    /// communauté : de l'indifférence au rejet. Une affaire qui fait scandale durcit le groupe ; une affaire sans suite l'habitue.
    /// </summary>
    public void DecouvrirMoeurs(Humain temoin, Humain a, Humain? b, long cause, IReadOnlyList<Humain> humains, long maintenant)
    {
        if (Tour.EnCours)
        {
            var (it, ia, ib) = (temoin.Id, a.Id, b?.Id ?? 0);
            Tour.Differer(humains, t => DecouvrirMoeurs(t[it - 1], t[ia - 1], ib > 0 ? t[ib - 1] : null, cause, t, maintenant));
            return;
        }
        var tolerance = 0.5 * ToleranceMoeursDe(temoin) + 0.5 * TemperamentMoeurs(temoin.Personnalite);
        var amitie = Math.Max(0, temoin.AffectionPour(a.Id)) / 100;
        var reprobation = 100 * Math.Clamp((0.6 - tolerance) * 2 * (1.2 - 0.6 * amitie), 0, 1);
        var vus = new Recit("j.moeurs.vus", ("a", a), ("b", b));
        var communaute = CommunauteDe(temoin) ?? CommunauteDe(a);
        long id;
        if (reprobation < 15)
        {
            id = Noter(maintenant, TypeEvenement.Attirance, temoin, a, b is null
                ? new Recit("j.moeurs.avanceIndifferent", ("t", temoin), ("a", a))
                : new Recit("j.moeurs.vusIndifferent", ("t", temoin), ("vus", vus)), cause);
        }
        else
        {
            id = Noter(maintenant, TypeEvenement.Attirance, temoin, a, (b, reprobation >= 60) switch
            {
                (null, true) => new Recit("j.moeurs.avanceScandale", ("t", temoin), ("a", a)),
                (null, false) => new Recit("j.moeurs.avanceReproche", ("t", temoin), ("a", a)),
                (_, true) => new Recit("j.moeurs.vusScandale", ("t", temoin), ("vus", vus)),
                _ => new Recit("j.moeurs.vusReproche", ("t", temoin), ("vus", vus)),
            }, cause);
            foreach (var x in b is null ? [a] : new[] { a, b })
            {
                AjouterGrief(temoin, new Grief(x.Id, MotifGrief.Moeurs, reprobation, reprobation, id));
                ChangerAffection(temoin, x, -reprobation / 3, maintenant);
                x.Humeur = Math.Max(0, x.Humeur - reprobation / 8);
                Jardin.Confier(x, TypeIntime.Peur, temoin.Id, new Recit("js.moeurs.su", ("t", temoin)),
                    20 + reprobation / 2, id, maintenant, $"moeurs-su:{temoin.Id}");
            }
            Jardin.Confier(temoin, TypeIntime.Opinion, a.Id, new Recit("js.moeurs.mal", ("a", a)), reprobation, id, maintenant, $"opinion:{a.Id}");
        }
        if (communaute is null) return;
        communaute.AffaireMoeursId = id;
        communaute.ToleranceMoeurs = Math.Clamp(communaute.ToleranceMoeurs + 0.08 * (0.3 - reprobation / 100), 0, 1);
        if (reprobation >= 40 && communaute.ToleranceMoeurs < 0.4) ReprouverMoeurs(a, b, communaute, id, humains, maintenant);
    }

    /// <summary>Là où ces amours sont mal vues, la nouvelle se répand et la communauté se détourne de ceux qui les vivent.</summary>
    private void ReprouverMoeurs(Humain a, Humain? b, Communaute c, long cause, IReadOnlyList<Humain> humains, long maintenant)
    {
        var force = (0.4 - c.ToleranceMoeurs) * 30;
        var id = Noter(maintenant, TypeEvenement.Attirance, a, b, b is null
            ? new Recit("j.moeurs.reprouve1", ("c", c), ("a", a))
            : new Recit("j.moeurs.reprouve2", ("c", c), ("a", a), ("b", b)), cause, communaute: c.Id);
        foreach (var x in b is null ? [a] : new[] { a, b })
        {
            foreach (var m in humains.Vivants())
            {
                if (!m.EstVivant || m.Id == x.Id || m.CommunauteId != c.Id || m.EstEnfant(maintenant) || (b is not null && m.Id == b.Id)) continue;
                var rigidite = 1 - TemperamentMoeurs(m.Personnalite);
                ChangerAffection(m, x, -force * rigidite, maintenant);
                if (rigidite > 0.55) AjouterGrief(m, new Grief(x.Id, MotifGrief.Moeurs, 40 * rigidite, 40 * rigidite, id));
            }
            Jardin.Confier(x, TypeIntime.ImageDeSoi, 0, Recit.Seul("js.moeurs.honte"), 30 + force, id, maintenant, "image:moeurs");
        }
        c.AffaireMoeursId = id;
    }
}
