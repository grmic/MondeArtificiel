using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les prières. Dans la détresse, qui croit en l'invisible lui demande de l'aide : guérir un proche, manger, que la pluie
/// revienne, qu'un malheur cesse, un enfant, la protection des siens, frapper qui l'a offensé, être aimé. La prière de
/// demande est la plus répandue des formes de prière, et la détresse y pousse (Spilka et al., 2003 ; Norenzayan et Hansen,
/// 2006).
/// <para>
/// Personne ne répond, sauf si l'Entité le veut (bouton « Exaucer »). Mais ce qu'on demandait arrive parfois de soi-même :
/// le malade guérit, la pluie revient. On croit alors avoir été entendu, et la croyance monte (la corrélation illusoire :
/// on retient ce qui confirme ; Chapman, 1967). Quand rien ne vient, le croyant fervent trouve une raison (l'invisible avait
/// les siennes, on n'a pas assez prié) plutôt que de douter : la dissonance se réduit en croyant davantage (Festinger,
/// Riecken et Schachter, 1956). Les autres doutent.
/// </para>
/// Seules les prières fortes (une vie en jeu, une détresse extrême, une foi ardente) sont notées au journal ; toutes sont
/// visibles dans la fenêtre des prières.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Croyances partagées, religions et prières (fourni par le monde).</summary>
    public Cultes? Cultes { get; set; }

    /// <summary>Jours qu'on attend une réponse, selon ce qu'on demande.</summary>
    private static double JoursDAttente(SujetPriere s) => s switch
    {
        SujetPriere.Guerison => 30,
        SujetPriere.Nourriture => 10,
        SujetPriere.Pluie => 360,
        SujetPriere.Accalmie => 15,
        SujetPriere.Enfant => 360,
        SujetPriere.Protection => 180,
        SujetPriere.Vengeance => 90,
        SujetPriere.Amour => 120,
        _ => 0,
    };

    /// <summary>Celui que le croyant prie, selon ce que lui ont appris les siens.</summary>
    private string Invisible(Humain h)
    {
        var r = Cultes?.Get(h.ReligionId);
        if (r is not null && r.Enseigne(Precepte.Createur)) return "celle qui les a faits";
        if (r is not null && r.Enseigne(Precepte.Ancetres)) return "les morts";
        return h.Memoire.Jardin.Exists(x => x.Cle == "croyance:entite") ? "celle qui les a faits" : "l'invisible";
    }

    /// <summary>
    /// Ce qui pourrait le faire prier aujourd'hui, et à quel point il est en détresse (0 : rien ; au-delà de 1, une vie en jeu).
    /// </summary>
    private (SujetPriere Sujet, int Cible, long Param, double Detresse, bool Vital)? BesoinDePrier(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        (SujetPriere, int, long, double, bool)? meilleur = null;
        void Garder(SujetPriere s, int cible, long param, double d, bool vital)
        {
            if (d >= 0.25 && (meilleur is null || d > meilleur.Value.Item4)) meilleur = (s, cible, param, d, vital);
        }
        static bool Souffre(Humain x, long maintenant) => x.EstMalade(maintenant) || x.InfectionId != 0 || x.PlaieLe > 0;

        // Guérir : soi-même, ou un proche malade.
        if (Souffre(h, maintenant) && h.Sante < 70) Garder(SujetPriere.Guerison, 0, 0, 1.2 * (100 - h.Sante) / 100, h.Sante < 35);
        foreach (var l in h.Liens)
        {
            if (l.Affection < 50 || l.Id < 1 || l.Id > tous.Count) continue;
            var x = tous[l.Id - 1];
            if (!x.EstVivant || !Souffre(x, maintenant) || x.Sante >= 70) continue;
            var enfant = x.MereId == h.Id || x.PereId == h.Id;
            Garder(SujetPriere.Guerison, x.Id, 0, (100 - x.Sante) / 100 * l.Affection / 100 * (enfant ? 1.5 : 1.1), x.Sante < 35);
        }
        if (h.Faim >= 70) Garder(SujetPriere.Nourriture, 0, 0, (h.Faim - 50) / 40, h.Faim >= 90);
        var region = ecosysteme.Region(h.X, h.Y);
        if (ecosysteme.Pluies[region] < 0.65 && (h.Faim > 40 || h.Soif > 50)) Garder(SujetPriere.Pluie, 0, region, 1.2 - ecosysteme.Pluies[region], false);
        if (Catastrophes?.DangerEn(h.X, h.Y) is { } danger && danger.Source.Active)
            Garder(SujetPriere.Accalmie, 0, danger.Source.Id, danger.Niveau == 2 ? 1.2 : 0.6, danger.Niveau == 2);
        if (h.CommunauteId != 0 && Social?.Guerres.FirstOrDefault(g => g.Fin is null && (g.A == h.CommunauteId || g.B == h.CommunauteId)) is { } guerre)
            Garder(SujetPriere.Protection, 0, guerre.Id, 0.3 + 0.5 * h.Personnalite.Nevrosisme, false);
        // Un enfant qui ne vient pas : on ne s'en inquiète pas chaque jour.
        if (h.Sexe == Sexe.Femme && !h.EstEnceinte && h.AgeEnAnnees(maintenant) is >= 20 and < 42 && Vivant(tous, h.PartenaireId) is not null
            && alea.Reel() < 0.05 && !tous.Vivants().Any(x => x.MereId == h.Id && x.AgeEnAnnees(maintenant) < 5))
            Garder(SujetPriere.Enfant, 0, 0, (0.2 + 0.5 * h.Personnalite.Attachement) * Math.Clamp((h.AgeEnAnnees(maintenant) - 18) / 12, 0.5, 1.5), false);
        if (h.Griefs.Count > 0 && h.Griefs.MaxBy(g => g.Intensite) is { Intensite: >= 60 } grief && Vivant(tous, grief.ContreId) is not null)
        {
            var p = h.Personnalite;
            Garder(SujetPriere.Vengeance, grief.ContreId, 0, grief.Intensite / 100 * (1.2 - p.Agreabilite) * (1 - 0.5 * p.Moral.Compassion), false);
        }
        if (h.PartenaireId == 0 && !h.EstEnfant(maintenant))
        {
            var aime = h.Liens.Where(l => l.Affection >= 70).MaxBy(l => l.Affection);
            if (aime is not null && Vivant(tous, aime.Id) is { } x && !x.EstEnfant(maintenant) && x.AffectionPour(h.Id) < 30
                && h.Orientation.Vers(x.Sexe) >= 0.5 && !SontParents(h, x))
                Garder(SujetPriere.Amour, x.Id, 0, 0.6 * (aime.Affection - 50) / 50, false);
        }
        return meilleur;
    }

    /// <summary>
    /// Chaque jour : celui qui croit, dans la détresse, prie (une prière à la fois ; plus souvent s'il croit fort, si sa
    /// religion le demande, si la détresse est grande).
    /// </summary>
    private void Prier(IReadOnlyList<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var cultes = Cultes!;
        var enAttente = cultes.Prieres.Where(p => p.Issue == IssuePriere.EnAttente).Select(p => p.HumainId).ToHashSet();
        foreach (var h in vivants)
        {
            if (enAttente.Contains(h.Id) || h.AgeEnAnnees(maintenant) < 6 || maintenant - h.PrieLe < 3 * Calendrier.SecondesParJour) continue;
            var c = h.Croyance / 100;
            if (c < 0.05) continue;
            var religion = cultes.Get(h.ReligionId);
            var regle = religion?.Enseigne(Precepte.Priere) == true ? 2.0 : 1;
            // Tirage préalable : la plupart des jours, on ne prie pas (et l'on n'évalue pas ses besoins).
            if (alea.Reel() >= 0.25 * c * regle * (0.6 + 0.8 * h.Ferveur)) continue;
            if (BesoinDePrier(h, tous, maintenant) is not { } b || alea.Reel() >= Math.Min(1, b.Detresse)) continue;
            var ferveur = Math.Clamp(0.5 * c + 0.4 * Math.Min(1, b.Detresse) + 0.2 * h.Ferveur, 0, 1);
            var forte = ferveur >= 0.6 && (b.Vital || b.Detresse >= 1);
            var priere = new Priere
            {
                Id = cultes.ProchainePriere++, HumainId = h.Id, Le = maintenant, Sujet = b.Sujet, CibleId = b.Cible, Parametre = b.Param,
                Ferveur = Math.Round(ferveur, 2), Forte = forte, ReligionId = h.ReligionId,
                Jusqua = maintenant + (long)(JoursDAttente(b.Sujet) * Calendrier.SecondesParJour),
            };
            h.PrieLe = maintenant;
            var x = b.Cible > 0 ? tous[b.Cible - 1] : null;
            if (forte)
            {
                priere.EvenementId = Noter(maintenant, TypeEvenement.Religion, h, x, TextePriere(h, priere, x), significatif: true,
                    karma: b.Sujet == SujetPriere.Vengeance ? Regard.PrierContre : 0);
            }
            else if (b.Sujet == SujetPriere.Vengeance) Regard.Juger(h, Regard.PrierContre, maintenant, x);
            cultes.Prieres.Add(priere);
            Confier(h, TypeIntime.Espoir, b.Cible, b.Sujet switch
            {
                SujetPriere.Guerison when x is not null => $"Pourvu que {x.Nom} guérisse.",
                SujetPriere.Vengeance when x is not null => $"Que {x.Nom} paie pour ce qu'il m'a fait.",
                SujetPriere.Amour when x is not null => $"Pourvu que {x.Nom} m'aime un jour.",
                _ => $"J'ai prié {Invisible(h)}. M'entendra-t-on ?",
            }, 20 + 50 * ferveur, priere.EvenementId, maintenant, "priere");
        }
    }

    private string TextePriere(Humain h, Priere p, Humain? x)
    {
        var qui = Invisible(h);
        var maniere = Cultes?.Get(h.ReligionId)?.Gestes is { } g ? $" (à la façon des fidèles : {g})" : "";
        var malheur = Catastrophes?.Trouver(p.Parametre)?.Nom is { Length: > 0 } nom ? $"ce malheur ({char.ToLowerInvariant(nom[0])}{nom[1..]})" : "le malheur";
        return p.Sujet switch
        {
            SujetPriere.Guerison when x is null => $"{h.Nom} supplie {qui} de {(h.Sexe == Sexe.Femme ? "la" : "le")} guérir{maniere}.",
            SujetPriere.Guerison => $"{h.Nom} supplie {qui} de sauver {x.Nom}{maniere}.",
            SujetPriere.Nourriture => $"Affamé{E(h)}, {h.Nom} supplie {qui} de lui donner à manger{maniere}.",
            SujetPriere.Pluie => $"{h.Nom} prie {qui} de faire revenir la pluie{maniere}.",
            SujetPriere.Accalmie => $"{h.Nom} supplie {qui} d'arrêter {malheur}{maniere}.",
            SujetPriere.Enfant => $"{h.Nom} prie {qui} de lui donner un enfant{maniere}.",
            SujetPriere.Protection => $"{h.Nom} prie {qui} de protéger les siens de la guerre{maniere}.",
            SujetPriere.Vengeance => $"{h.Nom} prie {qui} de frapper {x?.Nom}{maniere}.",
            SujetPriere.Amour => $"{h.Nom} prie {qui} que {x?.Nom} l'aime{maniere}.",
            _ => $"{h.Nom} remercie {qui}{maniere}.",
        };
    }

    /// <summary>
    /// Chaque jour : ce qu'on demandait est-il arrivé ? Arrivé de soi-même, le croyant se croit entendu ; le délai passé ou
    /// l'espoir perdu (le malade est mort), la prière reste sans réponse. Les prières résolues depuis plus d'un an quittent
    /// la mémoire (elles restent en base).
    /// </summary>
    private void Resoudre(IReadOnlyList<Humain> tous, long maintenant)
    {
        var cultes = Cultes!;
        // Une prière entendue peut en ajouter une (le remerciement) : on ne parcourt que celles d'avant.
        for (int k = 0, n = cultes.Prieres.Count; k < n; k++)
        {
            var p = cultes.Prieres[k];
            if (p.Issue != IssuePriere.EnAttente) continue;
            var h = tous[p.HumainId - 1];
            if (!h.EstVivant)
            {
                (p.Issue, p.IssueLe, p.Conclusion) = (IssuePriere.SansReponse, maintenant, "mort avant de voir la réponse");
                continue;
            }
            var x = p.CibleId > 0 ? tous[p.CibleId - 1] : null;
            var (arrive, perdu) = p.Sujet switch
            {
                SujetPriere.Guerison => ((x ?? h) is { EstVivant: true } m && !m.EstMalade(maintenant) && m.InfectionId == 0 && m.PlaieLe == 0 && m.Sante >= 60,
                    x is { EstVivant: false }),
                SujetPriere.Nourriture => (h.Faim < 30, false),
                SujetPriere.Pluie => (ecosysteme.Pluies[(int)p.Parametre] >= 0.9, false),
                SujetPriere.Accalmie => (Catastrophes?.Trouver(p.Parametre) is not { Active: true }, false),
                SujetPriere.Enfant => (h.EstEnceinte, false),
                SujetPriere.Protection => (Social?.Guerres.FirstOrDefault(g => g.Id == p.Parametre) is not { Fin: null }
                    || maintenant >= p.Jusqua && !ProcheMortALaGuerre(h, tous, p.Le), ProcheMortALaGuerre(h, tous, p.Le)),
                SujetPriere.Vengeance => (x is null || !x.EstVivant || x.EstMalade(maintenant) && x.MaladeJusqua - maintenant < 60 * Calendrier.SecondesParJour
                    || x.Sante < 40, false),
                SujetPriere.Amour => (x is { EstVivant: true } && (x.AffectionPour(h.Id) >= 50 || x.PartenaireId == h.Id), x is { EstVivant: false }),
                _ => (false, false),
            };
            if (arrive) Exauce(h, p, x, IssuePriere.ExauceeParHasard, maintenant);
            else if (perdu || maintenant >= p.Jusqua) SansReponse(h, p, x, perdu, maintenant);
        }
        var oubli = maintenant - Calendrier.SecondesParAn;
        cultes.Prieres.RemoveAll(p => p.Issue != IssuePriere.EnAttente && p.IssueLe < oubli);
    }

    /// <summary>Un proche (affection d'au moins 40) est mort à la guerre depuis <paramref name="depuis"/>.</summary>
    private static bool ProcheMortALaGuerre(Humain h, IReadOnlyList<Humain> tous, long depuis)
    {
        foreach (var l in h.Liens)
            if (l.Affection >= 40 && l.Id >= 1 && l.Id <= tous.Count && tous[l.Id - 1] is { MortLe: { } mort, CauseDeMort: CauseDeMort.Guerre } && mort >= depuis)
                return true;
        return false;
    }

    /// <summary>Ce qu'il demandait est venu : il se croit entendu, et en remercie parfois.</summary>
    private void Exauce(Humain h, Priere p, Humain? x, IssuePriere issue, long maintenant, long cause = 0)
    {
        (p.Issue, p.IssueLe) = (issue, maintenant);
        p.Conclusion = p.Sujet == SujetPriere.Vengeance ? "l'invisible a frappé pour lui" : "sa prière a été entendue";
        var pe = h.Personnalite;
        // Une coïncidence conforte un peu ; un vrai prodige (l'Entité) bouleverse.
        var gain = (issue == IssuePriere.ExauceeParEntite ? 6 + 10 * p.Ferveur : 1.5 + 3 * p.Ferveur) * (p.Forte ? 1.5 : 1);
        var id = cause;
        if (p.Forte || issue == IssuePriere.ExauceeParEntite)
        {
            var texte = p.Sujet switch
            {
                SujetPriere.Guerison when x is not null => $"{x.Nom} guérit : {h.Nom} croit que sa prière a été entendue.",
                SujetPriere.Guerison => $"{h.Nom} guérit, et croit que sa prière a été entendue.",
                SujetPriere.Nourriture => $"{h.Nom} a de nouveau à manger, et croit que sa prière a été entendue.",
                SujetPriere.Pluie => $"La pluie revient : {h.Nom} croit que sa prière a été entendue.",
                SujetPriere.Accalmie => $"Le malheur a cessé : {h.Nom} croit que sa prière a été entendue.",
                SujetPriere.Enfant => $"{h.Nom} attend un enfant, et croit que sa prière a été entendue.",
                SujetPriere.Protection => $"Les siens sont saufs : {h.Nom} croit que sa prière a été entendue.",
                SujetPriere.Vengeance when x is { EstVivant: false } => $"{x.Nom} est mort{E(x)} : {h.Nom} croit que l'invisible l'a frappé{E(x)} pour {Soi(h)}.",
                SujetPriere.Vengeance => $"Le malheur frappe {x?.Nom} : {h.Nom} croit que l'invisible l'a fait pour {Soi(h)}.",
                SujetPriere.Amour => $"{x?.Nom} se tourne vers {h.Nom}, qui croit que sa prière a été entendue.",
                _ => $"{h.Nom} croit que sa prière a été entendue.",
            };
            id = Noter(maintenant, TypeEvenement.Religion, h, x, texte, p.EvenementId != 0 ? p.EvenementId : cause);
        }
        Croyances.Ajouter(h, gain, $"{Il(h)} croit que sa prière a été entendue", id, maintenant, signe: true);
        h.Ferveur = Math.Min(1, h.Ferveur + 0.1 * (1 - h.Ferveur));
        Confier(h, TypeIntime.Croyance, p.CibleId, p.Sujet == SujetPriere.Vengeance ? $"L'invisible a frappé {x?.Nom} pour moi." : "On m'a entendu, quand j'ai prié.",
            30 + 50 * p.Ferveur, id, maintenant, "croyance:priere");
        Jardin.Retirer(h, "priere");
        // Celui qui porte la parole et dont la prière a été entendue : on le dit, et son prestige grandit.
        if (h.Role == RoleReligieux.Prophete && p.Forte) h.Miracles++;
        // Une prière forte entendue se raconte : la ferveur des fidèles proches monte.
        if (p.Forte && h.ReligionId != 0)
            foreach (var o in _tous.Vivants())
                if (o.ReligionId == h.ReligionId && o.Id != h.Id && o.EstVivant && Distance(o.X, o.Y, h.X, h.Y) <= 3)
                    o.Ferveur = Math.Min(1, o.Ferveur + 0.03);
        // Remercier, parfois (sans rien attendre).
        if (p.Sujet != SujetPriere.Vengeance && alea.Reel() < 0.2 + 0.5 * h.Ferveur * (0.5 + pe.Agreabilite))
        {
            Cultes!.Prieres.Add(new Priere
            {
                Id = Cultes.ProchainePriere++, HumainId = h.Id, Le = maintenant, Sujet = SujetPriere.Remerciement, CibleId = p.CibleId,
                Ferveur = p.Ferveur, ReligionId = h.ReligionId, Jusqua = maintenant, Issue = IssuePriere.Rendue, IssueLe = maintenant,
                Conclusion = $"{Il(h)} rend grâce",
            });
        }
    }

    /// <summary>
    /// Rien n'est venu. Le croyant fervent trouve une raison (Festinger et al., 1956) ; les autres doutent. Perdre celui pour
    /// qui l'on priait ébranle davantage.
    /// </summary>
    private void SansReponse(Humain h, Priere p, Humain? x, bool perdu, long maintenant)
    {
        (p.Issue, p.IssueLe) = (IssuePriere.SansReponse, maintenant);
        Jardin.Retirer(h, "priere");
        var c = h.Croyance / 100;
        var religion = Cultes!.Get(h.ReligionId);
        if (alea.Reel() < 0.2 + 0.7 * c)
        {
            // Les raisons qu'on se donne : selon ce que croient les siens, et selon son caractère.
            if (religion?.Enseigne(Precepte.Colere) == true)
            {
                p.Conclusion = "l'invisible est en colère contre les siens";
                h.Ferveur = Math.Min(1, h.Ferveur + 0.05);
            }
            else if (h.Personnalite.Nevrosisme > 0.55)
            {
                p.Conclusion = $"{Il(h)} n'a pas assez prié";
                h.Ferveur = Math.Min(1, h.Ferveur + 0.08);
                Confier(h, TypeIntime.Culpabilite, 0, "Je n'ai pas assez prié.", 20 + 40 * h.Personnalite.Nevrosisme, p.EvenementId, maintenant, "priere:faute");
            }
            else p.Conclusion = "l'invisible avait ses raisons";
            if (p.Forte)
                Noter(maintenant, TypeEvenement.Religion, h, x, perdu && x is not null
                    ? $"{x.Nom} est mort{E(x)} malgré les prières de {h.Nom}, qui se dit que {p.Conclusion}."
                    : $"La prière de {h.Nom} est restée sans réponse ; {Il(h)} se dit que {p.Conclusion}.", p.EvenementId);
            return;
        }
        p.Conclusion = $"{Il(h)} doute";
        var perte = (2 + 6 * p.Ferveur) * (p.Forte ? 1.5 : 1) * (perdu ? 1.5 : 1);
        var id = p.EvenementId;
        if (p.Forte)
            id = Noter(maintenant, TypeEvenement.Religion, h, x, perdu && x is not null
                ? $"{x.Nom} est mort{E(x)} malgré les prières de {h.Nom}, qui doute désormais de l'invisible."
                : $"La prière de {h.Nom} est restée sans réponse : {Il(h)} doute.", p.EvenementId);
        Croyances.Ajouter(h, -perte, perdu ? "ses prières n'ont sauvé personne" : "ses prières sont restées sans réponse", id, maintenant);
        h.Ferveur = Math.Max(0, h.Ferveur - 0.1);
        if (perdu && x is not null)
            Confier(h, TypeIntime.Question, x.Id, $"J'ai prié pour {x.Nom}, et {Il(x)} est mort{E(x)}. Personne ne m'écoute donc ?", 30 + 50 * p.Ferveur, id, maintenant, "question:priere");
    }

    /// <summary>
    /// L'Entité exauce une prière en attente : ce qu'elle fait dépend de ce qu'on demandait. Renvoie, en mots, ce qui s'est
    /// passé (ou pourquoi elle ne peut l'être).
    /// </summary>
    public string Exaucer(int priereId, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        if (Cultes?.Priere(priereId) is not { } p) return "Cette prière n'existe pas (ou plus).";
        if (p.Issue != IssuePriere.EnAttente) return "Cette prière n'attend plus de réponse.";
        var h = tous[p.HumainId - 1];
        if (!h.EstVivant) return $"{h.Nom} est mort{E(h)}.";
        var x = p.CibleId > 0 ? tous[p.CibleId - 1] : null;
        string texte;
        switch (p.Sujet)
        {
            case SujetPriere.Guerison:
            {
                var m = x ?? h;
                if (!m.EstVivant) return $"{m.Nom} est mort{E(m)}.";
                m.MaladeJusqua = Math.Min(m.MaladeJusqua, maintenant);
                if (m.InfectionId != 0)
                {
                    m.Immunites.Add(new Immunite(m.InfectionId, maintenant + Calendrier.SecondesParAn));
                    (m.InfectionId, m.InfectionEvenementId) = (0, 0);
                }
                m.PlaieLe = 0;
                m.Sante = Math.Max(m.Sante, 85);
                texte = $"Le mal quitte {m.Nom} d'un coup : {Il(m)} guérit.";
                break;
            }
            case SujetPriere.Nourriture:
            {
                h.Faim = 0;
                h.Reserves = Math.Min(100, h.Reserves + 10);
                var i = carte.Indice(h.X, h.Y);
                ecosysteme.Gibier[i] = (ushort)Math.Min(ushort.MaxValue, ecosysteme.Gibier[i] + 2000);
                ecosysteme.Plantes[i] = (ushort)Math.Min(ushort.MaxValue, ecosysteme.Plantes[i] + 2000);
                texte = $"Des bêtes et des fruits en abondance, là où se tient {h.Nom} : {Il(h)} mange enfin.";
                break;
            }
            case SujetPriere.Pluie:
                ecosysteme.Pluies[(int)p.Parametre] = Math.Max(ecosysteme.Pluies[(int)p.Parametre], 1.15);
                texte = $"La pluie tombe enfin sur les terres de {h.Nom}.";
                break;
            case SujetPriere.Accalmie:
                if (Catastrophes is null || !Catastrophes.Apaiser(p.Parametre, tous as List<Humain> ?? [.. tous], maintenant))
                    return "Ce malheur a déjà cessé.";
                texte = $"Le malheur cesse d'un coup, comme {h.Nom} l'a demandé.";
                break;
            case SujetPriere.Enfant:
            {
                var compagnon = Vivant(tous, h.PartenaireId);
                if (h.Sexe != Sexe.Femme || h.EstEnceinte || compagnon is null || !h.EstFecond(maintenant))
                    return $"{h.Nom} ne peut pas concevoir maintenant.";
                var jours = Math.Clamp(JoursDeGrossesseMoyens + Biologie.Normale(alea) * EcartJoursDeGrossesse, 230, 300);
                h.EnceinteDepuis = maintenant;
                h.TermeGrossesse = maintenant + (long)(jours * Calendrier.SecondesParJour);
                h.PereEnfantId = compagnon.Id;
                texte = $"{h.Nom} porte un enfant de {compagnon.Nom}. Elle ne le sait pas encore.";
                break;
            }
            case SujetPriere.Protection:
            {
                // Les siens prennent courage et leurs blessés guérissent ; l'ennemi est saisi de crainte.
                var guerre = Social?.Guerres.FirstOrDefault(g => g.Id == p.Parametre);
                var ennemi = guerre is null ? 0 : guerre.A == h.CommunauteId ? guerre.B : guerre.A;
                foreach (var o in tous.Vivants())
                {
                    if (!o.EstVivant) continue;
                    if (o.CommunauteId == h.CommunauteId && h.CommunauteId != 0)
                    {
                        o.Peur *= 0.3;
                        if (o.PlaieLe > 0) o.Sante = Math.Max(o.Sante, 70);
                    }
                    else if (ennemi != 0 && o.CommunauteId == ennemi) o.Peur = Math.Min(100, o.Peur + 40);
                }
                texte = $"Une force invisible garde les siens de {h.Nom}, et l'ennemi est saisi de crainte.";
                break;
            }
            case SujetPriere.Vengeance:
            {
                if (x is not { EstVivant: true }) return "Celui qu'il fallait frapper est déjà mort.";
                x.Maladie = Maladie.Dysenterie;
                x.MaladeJusqua = maintenant + (long)((10 + 20 * alea.Reel()) * Calendrier.SecondesParJour);
                x.Sante = Math.Min(x.Sante, 50);
                texte = $"Un mal soudain s'abat sur {x.Nom}.";
                break;
            }
            case SujetPriere.Amour:
                if (x is not { EstVivant: true }) return "Celui qu'il fallait toucher est mort.";
                Inspirer(x, h, passion: true, orientation: false, tous, maintenant);
                texte = $"Le cœur de {x.Nom} se tourne vers {h.Nom}.";
                break;
            default:
                return "Un remerciement n'attend rien.";
        }
        var id = NoterEntite(maintenant, TypeEvenement.Religion, h, x, $"L'Entité exauce la prière de {h.Nom}. {texte}", p.EvenementId, significatif: true);
        Exauce(h, p, x, IssuePriere.ExauceeParEntite, maintenant, id);
        return $"Exaucée : {texte}";
    }
}
