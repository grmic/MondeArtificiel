using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Qui est séparé de qui : exil hors d'une communauté, rejet par un parent.</summary>
public static class Bannissement
{
    /// <summary>Vrai si <paramref name="h"/> est chassé de la communauté <paramref name="communaute"/> (ou l'a quittée avec un exilé).</summary>
    public static bool ExcluDe(Humain h, int communaute, long maintenant)
    {
        if (communaute == 0 || h.Exclusions.Count == 0) return false;
        foreach (var e in h.Exclusions)
        {
            if (e.Type != TypeExclusion.Famille && e.De == communaute && e.Active(maintenant)) return true;
        }
        return false;
    }

    /// <summary>Vrai si l'un a renié l'autre.</summary>
    public static bool Renie(Humain a, Humain b, long maintenant)
    {
        foreach (var e in a.Exclusions)
        {
            if (e.Type == TypeExclusion.Famille && e.De == b.Id && e.Active(maintenant)) return true;
        }
        foreach (var e in b.Exclusions)
        {
            if (e.Type == TypeExclusion.Famille && e.De == a.Id && e.Active(maintenant)) return true;
        }
        return false;
    }

    /// <summary>Deux humains qui ne peuvent pas vivre ensemble : l'un est chassé de la communauté de l'autre, ou renié par lui.</summary>
    public static bool Separes(Humain a, Humain b, long maintenant) =>
        (a.Exclusions.Count > 0 || b.Exclusions.Count > 0)
        && (ExcluDe(a, b.CommunauteId, maintenant) || ExcluDe(b, a.CommunauteId, maintenant) || Renie(a, b, maintenant));

    /// <summary>Communauté dont l'humain est chassé en ce moment (0 : aucune).</summary>
    public static int Exil(Humain h, long maintenant)
    {
        foreach (var e in h.Exclusions)
        {
            if (e.Type != TypeExclusion.Famille && e.Active(maintenant)) return e.De;
        }
        return 0;
    }
}

/// <summary>
/// Exils et rejets. Dans les petites sociétés sans chef, le groupe se protège de ceux qui le menacent ou le troublent par
/// la réprobation, puis l'ostracisme, et parfois l'expulsion (Boehm, 1999). Être rejeté blesse profondément : tristesse,
/// colère, sentiment de ne plus valoir rien (Williams, 2007). Rien n'est décrété : un exil naît des rancunes accumulées
/// contre un membre, quand la plupart des adultes et la figure du groupe lui en veulent et que presque personne ne le défend.
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>Poids de chaque reproche dans le désir de chasser quelqu'un : le meurtre d'abord, puis les mœurs réprouvées, les coups…</summary>
    private static double PoidsReproche(MotifGrief motif) => motif switch
    {
        MotifGrief.Meurtre => 1,
        MotifGrief.Moeurs => 0.6,
        MotifGrief.Coups => 0.5,
        // Le voleur pris sur le fait : chez les chasseurs-cueilleurs, le profiteur qui ne partage pas et prend est moqué,
        // puis tenu à l'écart (Boehm, 1999).
        MotifGrief.Vol => 0.45,
        MotifGrief.Trahison => 0.5,
        _ => 0.3,
    };

    /// <summary>Ce que <paramref name="m"/> reproche à <paramref name="x"/> (0 : rien), et le reproche le plus lourd.</summary>
    private static (double Score, Grief? Principal) Reproche(Humain m, Humain x)
    {
        double score = Math.Max(0, -m.AffectionPour(x.Id) - 20) / 100;
        Grief? principal = null;
        foreach (var g in m.Griefs)
        {
            if (g.ContreId != x.Id) continue;
            var poids = PoidsReproche(g.Motif) * g.Intensite / 100;
            score += poids;
            if (principal is null || poids > PoidsReproche(principal.Motif) * principal.Intensite / 100) principal = g;
        }
        return (score, principal);
    }

    private static string Raison(Grief? g, Humain x, IReadOnlyList<Humain> humains) => g?.Motif switch
    {
        MotifGrief.Meurtre when g.VictimeId > 0 => $"pour le meurtre de {humains[g.VictimeId - 1].Nom}",
        MotifGrief.Meurtre => "pour un meurtre",
        MotifGrief.Moeurs => "pour ses amours avec quelqu'un de son sexe",
        MotifGrief.Coups => "pour sa violence",
        MotifGrief.Vol => "pour ses vols",
        MotifGrief.Trahison => "pour avoir trahi les siens",
        MotifGrief.Infidelite or MotifGrief.Rival => "pour avoir semé la discorde en s'unissant hors de son couple",
        _ => $"parce que plus personne ne {(x.Sexe == Sexe.Femme ? "la" : "le")} supporte",
    };

    /// <summary>
    /// Durée d'un exil selon la faute : quelques mois pour la discorde, des années pour un meurtre ; parfois pour toujours.
    /// <paramref name="clemence"/> (0 à 1) : part des accusateurs qu'un plaidoyer a fait revenir sur leur colère ; l'exil en
    /// est raccourci, et moins souvent sans terme.
    /// </summary>
    private long DureeExil(Grief? g, double colere, double clemence = 0)
    {
        var (mediane, toujours) = g?.Motif switch
        {
            MotifGrief.Meurtre => (3.0, 0.4),
            MotifGrief.Moeurs => (1.5, 0.25),
            MotifGrief.Coups => (0.6, 0.1),
            MotifGrief.Vol => (0.5, 0.1),
            MotifGrief.Infidelite or MotifGrief.Rival => (0.4, 0.05),
            _ => (0.8, 0.1),
        };
        var adouci = 1 - 0.5 * clemence;
        if (alea.Reel() < toujours * (0.5 + colere) * adouci) return 0;
        return (long)(mediane * (0.6 + colere) * adouci * Math.Exp(0.6 * Biologie.Normale(alea)) * Calendrier.SecondesParAn);
    }

    /// <summary>
    /// Chaque semaine : une communauté peut chasser un de ses membres ; un parent peut renier un enfant adulte ;
    /// les exils qui arrivent à leur terme prennent fin.
    /// </summary>
    internal void Exiler(List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> membres, long maintenant)
    {
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Exclusions.Count == 0) continue;
            for (var i = 0; i < h.Exclusions.Count; i++)
            {
                var e = h.Exclusions[i];
                if (e.Levee || e.Jusqua == 0 || e.Jusqua > maintenant) continue;
                h.Exclusions[i] = e with { Levee = true };
                Noter(maintenant, TypeEvenement.Exclusion, h, e.Type == TypeExclusion.Famille ? humains[e.De - 1] : null, e.Type switch
                {
                    TypeExclusion.Famille => $"{humains[e.De - 1].Nom} cesse de renier {h.Nom}.",
                    _ when e.De <= Communautes.Count => $"L'exil de {h.Nom} prend fin : {(h.Sexe == Sexe.Femme ? "elle" : "il")} peut revenir parmi la {Minuscule(Communautes[e.De - 1].Nom)}.",
                    _ => $"L'exil de {h.Nom} prend fin.",
                }, e.EvenementId, communaute: e.Type == TypeExclusion.Famille ? -1 : e.De);
            }
        }

        foreach (var c in actives)
        {
            if (!membres.TryGetValue(c.Id, out var groupe)) continue;
            var adultes = groupe.Where(h => !h.EstEnfant(maintenant) && !Bannissement.ExcluDe(h, c.Id, maintenant)).ToList();
            if (adultes.Count < 4) continue;
            Humain? exile = null;
            Grief? motif = null;
            List<Humain> hostiles = [];
            foreach (var x in adultes)
            {
                if (x.Id == c.FigureId) continue;
                var contre = new List<Humain>();
                var defenseurs = 0;
                var poids = new Dictionary<MotifGrief, double>();
                Grief? principal = null;
                foreach (var m in adultes)
                {
                    if (m.Id == x.Id) continue;
                    var (score, g) = Reproche(m, x);
                    if (score >= 0.4) contre.Add(m);
                    else if (m.AffectionPour(x.Id) >= 40) defenseurs++;
                    if (g is null) continue;
                    poids[g.Motif] = poids.GetValueOrDefault(g.Motif) + score;
                    if (principal is null || poids[g.Motif] > poids.GetValueOrDefault(principal.Motif)) principal = g;
                }
                if (contre.Count * 2 <= adultes.Count - 1 || contre.All(m => m.Id != c.FigureId) || defenseurs * 2 > contre.Count) continue;
                if (exile is null || contre.Count > hostiles.Count) (exile, motif, hostiles) = (x, principal, contre);
            }
            if (exile is null) continue;
            // Les cœurs bienveillants hésitent à chasser ; un groupe attaché à la loyauté, à l'autorité et à la pureté
            // punit plus volontiers celui qui trouble l'ordre (Graham et al., 2009).
            var bienveillance = hostiles.Average(m => (m.Personnalite.Agreabilite + m.Personnalite.Moral.Compassion) / 2);
            var severite = hostiles.Average(m => m.Personnalite.Moral.Fondements.Liants);
            if (alea.Reel() >= 0.3 * (1.2 - bienveillance) * (0.7 + 0.6 * severite)) continue;
            var clemence = 0.0;
            if (Plaideur(exile, c, adultes, hostiles, motif, maintenant) is { } plaideur)
            {
                var avant = hostiles.Count;
                if (Plaider(plaideur, exile, c, adultes, hostiles, motif, humains, maintenant)) continue;
                clemence = 1 - hostiles.Count / (double)avant;
            }
            Chasser(exile, c, motif, hostiles, humains, maintenant, clemence);
        }

        // Un parent qui en veut profondément à son enfant adulte peut le renier, d'autant plus qu'il est peu bienveillant et peu attaché.
        foreach (var x in humains.Vivants())
        {
            if (!x.EstVivant || x.EstEnfant(maintenant) || x.AgeEnAnnees(maintenant) < 16) continue;
            foreach (var pid in new[] { x.MereId, x.PereId })
            {
                if (pid == 0 || humains[pid - 1] is not { EstVivant: true } p || Bannissement.Renie(p, x, maintenant)) continue;
                if (p.AffectionPour(x.Id) > -40) continue;
                var g = p.Griefs.Where(g => g.ContreId == x.Id).MaxBy(g => g.Intensite);
                if (g is null || g.Intensite < 50) continue;
                if (alea.Reel() >= 0.25 * (1 - p.Personnalite.Agreabilite) * (1.2 - p.Personnalite.Attachement)) continue;
                Renier(p, x, g, humains, maintenant);
            }
        }
    }

    /// <summary>
    /// Celui qui peut plaider pour <paramref name="x"/> : un membre que les siens tiennent pour bon. Les humains ne voient pas le
    /// karma, mais ils voient ce qui le fait monter (soigner, partager, consoler, recueillir, pardonner) : le plaideur est celui
    /// dont le karma est le plus lumineux du groupe (50 ou plus, voir <see cref="Regard.Dire"/>), compatissant, qui n'en veut
    /// pas lui-même à l'accusé. Il plaide d'autant plus volontiers qu'il l'aime, qu'il ose parler devant tous (extraversion)
    /// et que la faute est légère.
    /// </summary>
    private Humain? Plaideur(Humain x, Communaute c, List<Humain> adultes, List<Humain> hostiles, Grief? motif, long maintenant)
    {
        Humain? p = null;
        foreach (var m in adultes)
        {
            if (m.Id == x.Id || p is not null && m.Karma <= p.Karma) continue;
            p = m;
        }
        if (p is null || p.Karma < 50 || p.Personnalite.Moral.Compassion < 0.5 || Psyche.EstPsychopathe(p, maintenant)) return null;
        if (hostiles.Any(m => m.Id == p.Id) || Reproche(p, x).Score >= 0.2) return null;
        var faute = motif?.Motif switch
        {
            MotifGrief.Meurtre => 0.4,
            MotifGrief.Coups or MotifGrief.Trahison => 0.7,
            _ => 1.0,
        };
        var envie = (0.3 + 0.4 * p.Personnalite.Moral.Compassion) * (0.6 + 0.4 * p.Personnalite.Extraversion)
            * (1 + Math.Max(0, p.AffectionPour(x.Id)) / 100) * faute;
        return alea.Reel() < Math.Min(0.9, envie) ? p : null;
    }

    /// <summary>
    /// Le plaidoyer : chaque accusateur reconsidère selon l'estime qu'il porte au plaideur et sa propre bienveillance ; pour un
    /// meurtre, rarement. Si la majorité n'y est plus, ou si la figure du groupe s'est laissé fléchir, l'exil est abandonné
    /// (renvoie vrai). Chez les peuples sans chef, les figures respectées apaisent les conflits par la parole et non par
    /// l'ordre (Boehm, 1999 ; Wiessner, 2014). Plaider pour un humain très détesté coûte un peu d'estime au plaideur.
    /// </summary>
    private bool Plaider(Humain p, Humain x, Communaute c, List<Humain> adultes, List<Humain> hostiles, Grief? motif, IReadOnlyList<Humain> humains,
        long maintenant)
    {
        var faute = motif?.Motif == MotifGrief.Meurtre ? 0.3 : 1;
        var flechis = new List<Humain>();
        foreach (var m in hostiles)
        {
            var estime = Math.Clamp((m.AffectionPour(p.Id) + 50) / 100, 0, 1);
            var colere = Math.Min(1, Reproche(m, x).Score / 2);
            if (alea.Reel() < 0.6 * estime * (0.4 + 0.6 * m.Personnalite.Agreabilite) * (1 - 0.5 * colere) * faute) flechis.Add(m);
        }
        foreach (var m in flechis) hostiles.Remove(m);
        var renonce = hostiles.Count * 2 <= adultes.Count - 1 || hostiles.All(m => m.Id != c.FigureId);
        var raison = Raison(motif, x, humains);
        var (le, lui, e) = x.Sexe == Sexe.Femme ? ("la", "elle", "e") : ("le", "lui", "");
        var suite = renonce
            ? flechis.Count == 1 ? $" {flechis[0].Nom} renonce à {le} chasser, et l'exil est abandonné." : $" {flechis.Count} des siens renoncent à {le} chasser, et l'exil est abandonné."
            : flechis.Count == 0 ? " Personne ne se laisse fléchir."
            : flechis.Count == 1 ? $" {flechis[0].Nom} se laisse fléchir, pas assez pour l'empêcher." : $" {flechis.Count} des siens se laissent fléchir, pas assez pour l'empêcher.";
        var id = Noter(maintenant, TypeEvenement.Exclusion, p, x, $"La {Minuscule(c.Nom)} veut chasser {x.Nom} {raison} ; {p.Nom} plaide pour {lui}.{suite}",
            motif?.EvenementId ?? 0, communaute: c.Id, significatif: renonce, karma: Regard.Plaider);
        foreach (var m in flechis)
        {
            Jardin.Confier(m, TypeIntime.Opinion, x.Id, $"{p.Nom} m'a convaincu{E(m)} d'épargner {x.Nom}.", 35, id, maintenant, $"opinion:{x.Id}");
            // Convaincu d'épargner, on lui laisse une chance : la rancune baisse et l'aversion retombe (il faudra de nouveaux
            // torts pour qu'on veuille de nouveau le chasser), sinon le groupe remettrait l'exil en question chaque semaine.
            for (var i = 0; i < m.Griefs.Count; i++)
                if (m.Griefs[i].ContreId == x.Id) m.Griefs[i] = m.Griefs[i] with { Intensite = Math.Round(m.Griefs[i].Intensite * 0.7, 1) };
            ChangerAffection(m, x, Math.Max(10, -30 - m.AffectionPour(x.Id)), maintenant);
        }
        // Défendre celui que presque tous détestent fait douter de soi ceux qui ne se sont pas laissé fléchir.
        foreach (var m in hostiles)
        {
            var colere = Reproche(m, x).Score;
            if (colere >= 1) ChangerAffection(m, p, -5 * Math.Min(2, colere), maintenant);
        }
        if (renonce)
        {
            ChangerAffection(x, p, 20, maintenant);
            Jardin.Confier(x, TypeIntime.Joie, p.Id, $"{p.Nom} m'a défendu{e} quand les miens voulaient me chasser.", 60, id, maintenant, $"plaidoyer:{p.Id}");
        }
        Jardin.Confier(p, TypeIntime.Intention, x.Id, renonce ? $"J'ai obtenu qu'on garde {x.Nom} parmi nous." : $"J'ai plaidé pour {x.Nom}, en vain.",
            40, id, maintenant, $"plaider:{x.Id}");
        return renonce;
    }

    private void Chasser(Humain x, Communaute c, Grief? motif, List<Humain> hostiles, IReadOnlyList<Humain> humains, long maintenant, double clemence = 0)
    {
        var colere = hostiles.Average(m => Reproche(m, x).Score);
        var duree = DureeExil(motif, Math.Min(1, colere / 2), clemence);
        var raison = Raison(motif, x, humains);
        var il = x.Sexe == Sexe.Femme ? "elle" : "il";
        var pour = duree == 0 ? "pour toujours" : $"pour {DireDuree(duree)}";
        var id = Noter(maintenant, TypeEvenement.Exclusion, x, humains[c.FigureId - 1],
            $"La {Minuscule(c.Nom)} chasse {x.Nom} {raison}, {pour}. {Majuscule(il)} doit partir.", motif?.EvenementId ?? 0,
            communaute: c.Id, significatif: true);
        var jusqua = duree == 0 ? 0 : maintenant + duree;
        x.Exclusions.Add(new Exclusion(TypeExclusion.Communaute, c.Id, maintenant, jusqua, id, raison));
        x.Humeur = Math.Max(0, x.Humeur - 25 * (0.6 + 0.8 * x.Personnalite.Nevrosisme));
        x.Colere = Math.Min(100, x.Colere + 20);
        Jardin.Confier(x, TypeIntime.Peine, 0, $"Les miens m'ont chassé{E(x)}.", 70, id, maintenant, $"exil:{c.Id}");
        Jardin.Confier(x, TypeIntime.ImageDeSoi, 0, "Je ne vaux plus rien aux yeux des miens.", 30 + 40 * x.Personnalite.Nevrosisme, id, maintenant, "image:exil");
        Jardin.Marquer(x, Epreuve.Exil, duree == 0 ? 1 : Math.Min(1, 0.4 + duree / (double)Calendrier.SecondesParAn / 3), id, maintenant, alea);
        foreach (var m in hostiles) Jardin.Confier(m, TypeIntime.Opinion, x.Id, $"Il fallait chasser {x.Nom}.", 40, id, maintenant, $"opinion:{x.Id}");

        // Le compagnon peut le suivre, s'il l'aime assez et ne lui en veut pas.
        if (humains.ElementAtOrDefault(x.PartenaireId - 1) is { EstVivant: true } p && p.CommunauteId == c.Id
            && Reproche(p, x).Score < 0.3 && alea.Reel() < Math.Max(0, p.AffectionPour(x.Id) - 30) / 70 * (0.4 + 0.6 * p.Personnalite.Attachement))
        {
            var suit = Noter(maintenant, TypeEvenement.Exclusion, p, x, $"{p.Nom} part avec {x.Nom}, plutôt que de {(x.Sexe == Sexe.Femme ? "la" : "le")} perdre.", id,
                communaute: c.Id);
            p.Exclusions.Add(new Exclusion(TypeExclusion.Depart, c.Id, maintenant, jusqua, suit, $"pour suivre {x.Nom} en exil"));
            Jardin.Confier(p, TypeIntime.Intention, x.Id, $"Je reste avec {x.Nom}, où qu'{il} aille.", 60, suit, maintenant, $"exil-suivre:{x.Id}");
        }
    }

    private void Renier(Humain p, Humain x, Grief g, IReadOnlyList<Humain> humains, long maintenant)
    {
        var raison = Raison(g, x, humains);
        var duree = DureeExil(g, g.Intensite / 100);
        var enfant = x.Sexe == Sexe.Femme ? "sa fille" : "son fils";
        var id = Noter(maintenant, TypeEvenement.Exclusion, p, x, $"{p.Nom} renie {enfant} {x.Nom} {raison}.", g.EvenementId, significatif: true);
        x.Exclusions.Add(new Exclusion(TypeExclusion.Famille, p.Id, maintenant, duree == 0 ? 0 : maintenant + duree, id, raison));
        x.Humeur = Math.Max(0, x.Humeur - 15 * (0.6 + 0.8 * x.Personnalite.Nevrosisme));
        var parent = p.Sexe == Sexe.Femme ? "Ma mère" : "Mon père";
        Jardin.Confier(x, TypeIntime.Peine, p.Id, $"{parent} m'a renié{E(x)}.", 60, id, maintenant, $"renie:{p.Id}");
        Jardin.Confier(p, TypeIntime.Opinion, x.Id, $"{x.Nom} n'est plus mon enfant.", 60, id, maintenant, $"opinion:{x.Id}");
    }

    private static string Majuscule(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static string DireDuree(long secondes)
    {
        var mois = secondes / (double)Calendrier.SecondesParAn * 12;
        return mois switch
        {
            < 1.5 => "un mois",
            < 11.5 => $"{Math.Round(mois)} mois",
            < 18 => "un an",
            _ => $"{Math.Round(mois / 12)} ans",
        };
    }
}
