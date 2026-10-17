namespace Monde.Simulation.Humains;

/// <summary>
/// Conflits entre humains : une infidélité qui s'ébruite, la colère qui pousse à affronter l'offenseur,
/// des querelles et parfois des coups, rarement mortels. Chaque geste cite l'offense qui l'a provoqué.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Probabilité, à chaque pas passé à côté de l'offenseur, d'en venir à l'affronter, pour une colère de 100.</summary>
    private const double AffrontementParPas = 0.01;

    /// <summary>
    /// Une blessure sur 25 est grave (os brisés, plaie profonde) : chez les chasseurs-cueilleurs, la plupart des disputes
    /// ne tuent pas, mais les homicides y comptent pour une part notable des morts d'adultes (Knauft, 1987 ; Hill et al., 2007).
    /// </summary>
    private const double RisqueBlessureGrave = 0.04;

    /// <summary>
    /// Après un accouplement hors du couple, le compagnon trompé peut l'apprendre : s'il est tout près (les amants se cachent :
    /// il ne les surprend qu'une fois sur deux), ou par un de ses amis qui les a vus et le lui dit (une fois sur quatre).
    /// Qui lit bien les autres (empathie cognitive) devine davantage ; un menteur habile (peu honnête, froid, qui comprend
    /// ce que l'autre croit) se trahit moins.
    /// </summary>
    private void Ebruiter(Humain a, Humain b, long accouplement, IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var (infidele, complice) in new[] { (a, b), (b, a) })
        {
            if (Vivant(tous, infidele.PartenaireId) is not { } trompe || trompe.Id == complice.Id) continue;
            Humain? temoin = null;
            var cache = Dissimulation(infidele);
            var voit = trompe.Activite != Activite.Dormir && Distance(trompe.X, trompe.Y, infidele.X, infidele.Y) <= 2
                       && alea.Reel() < 0.5 * Perspicacite(trompe) * cache;
            if (!voit)
            {
                foreach (var t in tous.Vivants())
                {
                    if (!t.EstVivant || t.Id == a.Id || t.Id == b.Id || t.Id == trompe.Id || t.EstEnfant(maintenant)
                        || t.Activite == Activite.Dormir || Distance(t.X, t.Y, infidele.X, infidele.Y) > 2
                        || t.LienAvec(trompe.Id) is not { Amis: true }) continue;
                    if (alea.Reel() < 0.25 * Perspicacite(t) * cache) temoin = t;
                    break;
                }
            }
            if (voit || temoin is not null) Social?.Decouvrir(trompe, infidele, complice, temoin, accouplement, tous, maintenant);
        }
    }

    /// <summary>
    /// Un humain en colère, à côté de celui qui l'a offensé, peut l'affronter : le plus souvent une querelle, parfois des coups,
    /// d'autant plus que la colère est forte et la personne peu bienveillante. Les hommes en viennent aux coups bien plus souvent
    /// que les femmes (Archer, 2004). Celui qui frappe n'a pas toujours le dessus : la force et l'adresse décident.
    /// </summary>
    private void Affronter(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (h.Colere < 35 || h.Griefs.Count == 0 || h.EstEnfant(maintenant) || Social is null) return;
        Grief? grief = null;
        Humain? cible = null;
        foreach (var g in h.Griefs)
        {
            var autre = tous[g.ContreId - 1];
            if (!autre.EstVivant || autre.EstBebe(maintenant) || Distance(h.X, h.Y, autre.X, autre.Y) > 1) continue;
            if (grief is null || g.Intensite > grief.Intensite) (grief, cible) = (g, autre);
        }
        if (grief is null || cible is null || alea.Reel() >= AffrontementParPas * h.Colere / 100) return;
        // « Ne te venge pas » : celui qui y croit se retient, plus ou moins.
        if (h.Impulsions.Count > 0 && h.Consigne(Parole.NeTeVengePas, maintenant) is { } retenue && alea.Reel() < retenue.Force)
        {
            if (!Recent(h, "entite:retenue", 7, maintenant))
            {
                var id0 = Noter(maintenant, TypeEvenement.Conflit, h, cible, $"{h.Nom} sent la colère monter contre {cible.Nom}, mais se retient : la voix le lui a demandé.", retenue.EvenementId);
                Confier(h, TypeIntime.Intention, cible.Id, $"Je ne me vengerai pas de {cible.Nom}. La voix l'a voulu.", 30, id0, maintenant, "entite:retenue");
            }
            h.Colere *= 0.7;
            return;
        }

        var p = h.Personnalite;
        var motif = grief.Motif switch
        {
            MotifGrief.Infidelite => $", qui l'a trahi{E(h)}",
            MotifGrief.Rival => $", qui s'est uni{E(cible)} à {(h.Sexe == Sexe.Femme ? "son compagnon" : "sa compagne")}",
            MotifGrief.Coups => $", qui l'a frappé{E(h)}",
            MotifGrief.Vol => $", qui l'a volé{E(h)}",
            MotifGrief.Trahison => ", qui a trahi les siens",
            MotifGrief.Moeurs => "",
            _ => grief.VictimeId > 0 ? $", qui a tué {VieSociale.Parente(h, tous[grief.VictimeId - 1])}" : "",
        };
        var violence = (1 - p.Agreabilite) * h.Colere / 100 * (h.Sexe == Sexe.Homme ? 1.3 : 0.7)
                       * (grief.Motif == MotifGrief.Meurtre ? 1.5 : 1) * (1 + Psyche.Froideur(p)) * (1.2 - 0.4 * p.Moral.Compassion)
                       // Ceux qui peuvent s'expliquer avec des phrases en viennent moins vite aux mains.
                       * (Langues.SExpliquent(h, cible) ? 0.75 : 1);
        if (alea.Reel() >= 0.5 * violence)
        {
            Noter(maintenant, TypeEvenement.Conflit, h, cible, $"{h.Nom} s'emporte contre {cible.Nom}{motif} : cris, reproches et menaces.",
                grief.EvenementId);
            h.Colere *= 0.6;
            Tour.Changer(cible, c => c.Colere = Math.Min(100, c.Colere + 10 * (1 - c.Personnalite.Agreabilite)));
            Social.ChangerAffection(h, cible, -4, maintenant);
            return;
        }

        // Des coups : l'issue dépend de la force de chacun. Pendant un pas simultané, ils se battent après le pas, s'ils vivent encore.
        var (ih, ic) = (h.Id, cible.Id);
        Tour.Differer(tous, t =>
        {
            if (t[ih - 1].EstVivant && t[ic - 1].EstVivant) Combattre(t[ih - 1], t[ic - 1], grief, motif, t, maintenant);
        });
    }

    private void Combattre(Humain h, Humain cible, Grief grief, string motif, IReadOnlyList<Humain> tous, long maintenant)
    {
        var fh = Force(h, maintenant);
        var fc = Force(cible, maintenant);
        var gagne = alea.Reel() < fh / (fh + fc);
        var (vainqueur, vaincu) = gagne ? (h, cible) : (cible, h);
        var grave = alea.Reel() < RisqueBlessureGrave;
        var degats = grave ? 50 + 50 * alea.Reel() : 5 + 20 * alea.Reel();
        var le = vaincu.Sexe == Sexe.Femme ? "la" : "le";
        var blesse = grave ? $"{le} blesse grièvement" : $"{le} blesse";
        var id = Noter(maintenant, TypeEvenement.Conflit, h, cible, gagne
            ? $"{h.Nom} frappe {cible.Nom}{motif}, et {blesse}."
            : $"{h.Nom} s'en prend à {cible.Nom}{motif}, mais {cible.Nom} se défend et {blesse}.", grief.EvenementId, karma: Regard.Coups);
        h.Colere *= 0.4;
        vaincu.Sante -= degats;
        Medecine.Blesser(vaincu, degats, maintenant);
        vaincu.Humeur = Math.Max(0, vaincu.Humeur - degats / 3);
        Social.ChangerAffection(h, cible, -10, maintenant);
        VieSociale.AjouterGrief(vaincu, new Grief(vainqueur.Id, MotifGrief.Coups, Math.Min(100, 30 + degats), Math.Min(100, 30 + degats), id));
        Social.Offenser(h, cible, 3, id, tous, maintenant);
        // Une jalousie qui tourne aux coups rend le groupe plus sévère envers l'infidélité.
        if (grief.Motif is MotifGrief.Infidelite or MotifGrief.Rival) Social.Durcir(h, grave ? 0.1 : 0.03, id);

        if (vaincu.Sante <= 0)
        {
            // Tuer celui qui vous attaque pèse bien moins que tuer celui que l'on attaque.
            Mourir(vaincu, maintenant, CauseDeMort.Meurtre, id, vainqueur, vainqueur == h ? Regard.Meurtre : Regard.LegitimeDefense);
            Social.Offenser(vainqueur, vaincu, 15, vaincu.MortEvenementId, tous, maintenant);
            if (grief.Motif is MotifGrief.Infidelite or MotifGrief.Rival) Social.Durcir(h, 0.1, vaincu.MortEvenementId);
        }
    }

    /// <summary>Facteur (0,6 à 1,4) de qui perce à jour une ruse ou un mensonge : l'empathie cognitive.</summary>
    internal static double Perspicacite(Humain h) => 0.6 + 0.8 * h.Personnalite.Moral.EmpathieCognitive;

    /// <summary>
    /// Facteur (0,4 à 1,2) de qui se laisse prendre : le menteur peu honnête, sans gêne ni remords, qui sait ce que l'autre
    /// pense, se trahit moins (le psychopathe ment avec aisance ; Hare, 1993).
    /// </summary>
    internal static double Dissimulation(Humain h)
    {
        var m = h.Personnalite.Moral;
        return Math.Clamp(1.2 - 0.5 * (1 - m.Honnetete) * m.EmpathieCognitive * 2 - 0.3 * Psyche.Froideur(h.Personnalite), 0.4, 1.2);
    }

    /// <summary>Force au combat : sexe, endurance, adresse, santé du moment et âge.</summary>
    private static double Force(Humain h, long maintenant)
    {
        var p = h.Personnalite;
        var age = h.AgeEnAnnees(maintenant);
        var vigueur = age switch { < 16 => 0.6, < 45 => 1, < 60 => 0.85, _ => 0.6 };
        return (h.Sexe == Sexe.Homme ? 1 : 0.75) * (0.6 + 0.4 * p.Endurance) * (0.7 + 0.3 * p.Adresse)
               * (0.3 + 0.7 * Math.Clamp(h.Sante, 0, 100) / 100) * vigueur;
    }

    private static string E(Humain h) => h.Sexe == Sexe.Femme ? "e" : "";
}
