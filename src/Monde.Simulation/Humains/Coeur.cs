using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// La vie du cœur, chaque jour, entre voisins : la peur et la peine se propagent à ceux qui les ressentent (contagion
/// émotionnelle ; Hatfield, Cacioppo et Rapson, 1993), les compatissants consolent ceux qui pleurent et veillent sur les malades
/// et les blessés (chez les Shiwiar, les soins des proches font survivre aux maladies et blessures graves ; Sugiyama, 2004),
/// et les orphelins sont recueillis. Chaque geste est noté avec sa cause.
/// </summary>
public sealed partial class VieSociale
{
    public void Coeur(List<Humain> humains, long maintenant)
    {
        // Voisins : même case ou case voisine.
        var parCase = new Dictionary<(int, int), List<Humain>>();
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.EstBebe(maintenant)) continue;
            (parCase.TryGetValue((h.X, h.Y), out var l) ? l : parCase[(h.X, h.Y)] = []).Add(h);
        }
        IEnumerable<Humain> Voisins(Humain h)
        {
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (parCase.TryGetValue((h.X + dx, h.Y + dy), out var l))
                        foreach (var v in l)
                            if (v.Id != h.Id) yield return v;
        }

        var sources = parCase.Values.SelectMany(l => l).OrderBy(h => h.Id).ToList();
        foreach (var s in sources)
        {
            if (s.Peur >= 40) PropagerPeur(s, Voisins(s), maintenant);
            if (s.Chagrin >= 30) Consoler(s, Voisins(s), humains, maintenant);
            if (s.EstMalade(maintenant) || s.InfectionId != 0 && s.SymptomesLe <= maintenant || s.Sante < 50)
                Soigner(s, Voisins(s), maintenant);
        }
        Recueillir(humains, maintenant);
        Captivites(humains, maintenant);
    }

    /// <summary>La terreur d'un voisin gagne ceux qui la ressentent, les sensibles et les empathiques surtout.</summary>
    private void PropagerPeur(Humain s, IEnumerable<Humain> voisins, long maintenant)
    {
        foreach (var v in voisins)
        {
            if (v.Peur >= s.Peur * 0.8) continue;
            var p = v.Personnalite;
            var gain = 0.35 * s.Peur * (0.3 + 1.4 * p.Moral.EmpathieEmotionnelle) * (0.6 + 0.8 * p.Nevrosisme) * (1 - 0.6 * Psyche.Froideur(p));
            if (gain <= v.Peur + 2) continue;
            var avant = v.Peur;
            v.Peur = Math.Min(100, gain);
            v.PeurEvenementId = s.PeurEvenementId;
            v.PeurType = s.PeurType;
            v.Humeur = Math.Max(0, v.Humeur - (v.Peur - avant) / 6);
            if (v.Peur >= 30 && avant < 20)
            {
                Noter(maintenant, TypeEvenement.Humeur, v, s, new Recit("j.peurGagne", ("v", v), ("s", s)), s.PeurEvenementId);
            }
        }
    }

    /// <summary>
    /// Ceux qui aiment l'endeuillé partagent un peu sa peine ; les plus compatissants le consolent, ce qui l'apaise
    /// (le soutien des proches adoucit le deuil ; Stroebe et al., 2007).
    /// </summary>
    private void Consoler(Humain s, IEnumerable<Humain> voisins, List<Humain> humains, long maintenant)
    {
        foreach (var v in voisins)
        {
            if (v.EstEnfant(maintenant) || v.Chagrin >= s.Chagrin) continue;
            var affection = v.AffectionPour(s.Id);
            if (affection < 0) continue;
            var p = v.Personnalite;
            var froideur = Psyche.Froideur(p);
            // La peine partagée : l'empathie émotionnelle fait souffrir avec l'autre.
            var peine = 0.04 * s.Chagrin * p.Moral.EmpathieEmotionnelle * (0.3 + affection / 100) * (1 - froideur);
            v.Humeur = Math.Max(0, v.Humeur - peine);
            // La consolation : la compassion pousse à agir, l'empathie cognitive trouve les gestes justes.
            var envie = (p.Moral.Compassion - 0.35) * (0.4 + affection / 100) * (1 - froideur);
            if (envie <= 0 || alea.Reel() >= envie) continue;
            var apaise = (3 + 6 * p.Moral.EmpathieCognitive) * (0.5 + affection / 200);
            s.Chagrin = Math.Max(0, s.Chagrin - apaise);
            s.Humeur = Math.Min(100, s.Humeur + apaise / 2);
            Gratitude(v, s, 1);
            var cle = $"console:{s.Id}:{s.DeuilEvenementId}";
            if (v.Memoire.Jardin.Exists(x => x.Cle == cle)) continue;
            var mort = s.DeuilDeId >= 1 && s.DeuilDeId <= humains.Count ? humains[s.DeuilDeId - 1] : null;
            var id = Noter(maintenant, TypeEvenement.Entraide, v, s,
                mort is null ? new Recit("j.console", ("v", v), ("s", s)) : new Recit("j.console.deuil", ("v", v), ("s", s), ("p", ParenteR(s, mort))), s.DeuilEvenementId, karma: Regard.Consoler);
            var (il, e) = s.Sexe == Sexe.Femme ? ("elle", "e") : ("il", "");
            var pensee = Jardin.Varier(v, cle, 0, s.Nom,
                new Jardin.V($"Je reste près de {s.Nom} tant qu'{il} a du chagrin.", 0.4),
                new Jardin.V($"Je partage la peine de {s.Nom}.", 0.2 + Jardin.S(p.Moral.EmpathieEmotionnelle)),
                new Jardin.V($"Je trouverai les gestes pour consoler {s.Nom}.", 0.1 + Jardin.S(p.Moral.EmpathieCognitive)),
                new Jardin.V($"{s.Nom} pleure ; je ne {(e == "e" ? "la" : "le")} laisserai pas seul{e}.", 0.2 + Jardin.S(p.Moral.Compassion)),
                new Jardin.V($"Je console {s.Nom}.", 0.3, 0))!;
            Jardin.Confier(v, TypeIntime.Intention, s.Id, pensee, 20 + 30 * p.Moral.Compassion, id, maintenant, cle);
        }
    }

    /// <summary>
    /// Un malade ou un blessé reçoit de l'eau et de la nourriture de ceux qui veillent sur lui : ses proches d'abord, et les
    /// compatissants même s'ils le connaissent peu.
    /// </summary>
    private void Soigner(Humain s, IEnumerable<Humain> voisins, long maintenant)
    {
        foreach (var v in voisins)
        {
            if (v.EstEnfant(maintenant) || v.EstMalade(maintenant) || v.Faim >= 60 || v.Soif >= 60) continue;
            var affection = v.AffectionPour(s.Id);
            var famille = v.PartenaireId == s.Id || s.MereId == v.Id || s.PereId == v.Id || v.MereId == s.Id || v.PereId == s.Id;
            if (affection < 0 && !famille) continue;
            var p = v.Personnalite;
            var envie = Math.Clamp(0.15 + 1.2 * (p.Moral.Compassion - 0.5) + 0.3 * Math.Max(0, affection) / 100 + (famille ? 0.4 : 0)
                                   + (v.CommunauteId != 0 && v.CommunauteId == s.CommunauteId ? 0.1 : 0), 0, 1)
                        * (1 - Psyche.Froideur(p));
            if (alea.Reel() >= envie) continue;
            var eau = Math.Min(s.Soif, 25);
            var vivres = Math.Min(s.Faim, 20);
            s.Soif -= eau;
            s.Faim -= vivres;
            s.Humeur = Math.Min(100, s.Humeur + 3);
            v.Faim = Math.Min(100, v.Faim + vivres * 0.3);
            v.Fatigue = Math.Min(100, v.Fatigue + 5);
            Gratitude(v, s, 2);
            var cle = $"soin:{s.Id}";
            if (v.Memoire.Jardin.Exists(x => x.Cle == cle && maintenant - x.Secondes < 30 * Temps.Calendrier.SecondesParJour)) return;
            var etat = s.EstMalade(maintenant) || s.InfectionId != 0 ? "malade" : $"blessé{E(s)}";
            var id = Noter(maintenant, TypeEvenement.Entraide, v, s,
                new Recit(s.EstMalade(maintenant) || s.InfectionId != 0 ? "j.veille.malade" : "j.veille.blesse", ("v", v), ("s", s)), s.InfectionId != 0 ? s.InfectionEvenementId : 0, karma: Regard.Veiller);
            Jardin.Retirer(v, cle);
            var lui = s.Sexe == Sexe.Femme ? "elle" : "lui";
            var pensee = Jardin.Varier(v, cle, 0, s.Nom,
                new Jardin.V($"Je prends soin de {s.Nom}.", 0.4),
                new Jardin.V($"{s.Nom} a besoin de moi ; je reste près {(lui == "lui" ? "de lui" : "d'elle")}.", 0.2 + Jardin.S(p.Moral.Compassion)),
                new Jardin.V($"Tant que {s.Nom} sera {etat}, je lui porterai à boire et à manger.", 0.2 + Jardin.S(p.Conscience)),
                new Jardin.V($"Je soigne {s.Nom} ; {(lui == "lui" ? "il" : "elle")} ferait de même pour moi.", 0.1 + Jardin.S(p.Moral.Fondements.Equite)),
                new Jardin.V($"Je veille sur {s.Nom}.", 0.3, 0),
                new Jardin.V($"Le mal de {s.Nom} me fait mal à moi aussi.", 0.05 + 2 * Jardin.S(p.Moral.EmpathieEmotionnelle), 2))!;
            Jardin.Confier(v, TypeIntime.Intention, s.Id, pensee, 20 + 40 * p.Moral.Compassion, id, maintenant, cle);
            return;
        }
    }

    /// <summary>Un enfant qui n'a plus ni père ni mère : celui qui le prend sous son aile le fait savoir au journal.</summary>
    private void Recueillir(List<Humain> humains, long maintenant)
    {
        foreach (var e in humains.Vivants())
        {
            if (!e.EstVivant || !e.EstEnfant(maintenant) || e.MereId == 0 && e.PereId == 0) continue;
            if (e.MereId > 0 && humains[e.MereId - 1].EstVivant || e.PereId > 0 && humains[e.PereId - 1].EstVivant) continue;
            if (Comportement.Gardien(e, humains, maintenant) is not { } g) continue;
            var cle = $"recueil:{e.Id}";
            if (g.Memoire.Jardin.Exists(x => x.Cle == cle)) continue;
            // La mort du dernier parent est la cause.
            var parents = new[] { e.MereId, e.PereId }.Where(id => id > 0).Select(id => humains[id - 1]).ToList();
            var dernier = parents.MaxBy(x => x.MortLe ?? 0);
            var id = Noter(maintenant, TypeEvenement.Entraide, g, e,
                new Recit("j.recueille", ("g", g), ("e", e), ("age", Comportement.AgeEnMots(e, maintenant))),
                dernier?.MortEvenementId ?? 0, karma: Regard.Recueillir);
            Jardin.Confier(g, TypeIntime.Intention, e.Id, $"Je veillerai sur {e.Nom}.", 30 + 40 * g.Personnalite.Moral.Compassion, id, maintenant, cle);
        }
    }
}
