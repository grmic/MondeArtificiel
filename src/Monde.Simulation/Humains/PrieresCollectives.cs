using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Prières collectives et refus de l'Entité (0.28.1, demandes de zem du 21/10).
/// <para>
/// Quand beaucoup souffrent du même mal (la faim, la sécheresse, la guerre, une épidémie, une catastrophe), celui qui porte
/// la foi du groupe (un prêtre, un prophète, sinon la figure du groupe ou le plus croyant) réunit les siens pour prier
/// ensemble. Dans les sociétés sans écriture, les rites collectifs se multiplient dans l'adversité (Sosis et Handwerker,
/// 2011), et le rite apaise l'anxiété de ceux qui y prennent part, qu'il soit suivi d'effet ou non (Lang et al., 2015).
/// Si ce qu'on demandait arrive, tous croient avoir été entendus ; sinon le doute gagne les tièdes.
/// </para>
/// <para>
/// L'Entité peut refuser une prière : en secret (rien ne change pour celui qui prie, qui attend puis conclut comme si rien
/// n'était venu) ou en le faisant savoir par un rêve ou une voix. Un refus signifié prouve l'invisible ; le fervent
/// s'incline et le craint, les autres s'aigrissent, d'autant plus que l'enjeu était vital. Celui qui priait pour qu'on
/// frappe un autre a honte.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Croyance moyenne des adultes (sur 100) en dessous de laquelle un groupe ne prie pas ensemble.</summary>
    private const double CroyancePourPrierEnsemble = 25;

    /// <summary>Croyance (sur 100) à partir de laquelle on prend part à une prière collective.</summary>
    private const double CroyancePourParticiper = 10;

    /// <summary>
    /// Chaque jour, chaque communauté : si un même mal frappe beaucoup des siens, celui qui porte la foi du groupe peut les
    /// réunir pour prier. Une seule prière collective à la fois par groupe.
    /// </summary>
    private void PrierEnsemble(IReadOnlyList<Humain> vivants, long maintenant)
    {
        if (Social is null || Cultes is null) return;
        var enAttente = Cultes.Prieres.Where(p => p.CommunauteId != 0 && p.Issue == IssuePriere.EnAttente).Select(p => p.CommunauteId).ToHashSet();
        var parGroupe = vivants.Where(h => h.CommunauteId != 0 && h.CaptifDe == 0 && !h.EstEnfant(maintenant)).GroupBy(h => h.CommunauteId).OrderBy(g => g.Key);
        foreach (var g in parGroupe)
        {
            var adultes = g.ToList();
            if (adultes.Count < 3 || enAttente.Contains(g.Key) || g.Key > Social.Communautes.Count) continue;
            var c = Social.Communautes[g.Key - 1];
            if (!c.Active) continue;
            var croyance = adultes.Average(h => h.Croyance);
            if (croyance < CroyancePourPrierEnsemble) continue;
            if (Guide(adultes, c) is not { } guide) continue;
            if (MalCommun(adultes, c, guide, maintenant) is not { } mal) continue;
            // La plupart des jours, on ne se réunit pas : plus souvent si l'on croit fort et si le mal est grand.
            if (alea.Reel() >= 0.15 * croyance / 100 * Math.Min(1.5, mal.Detresse)) continue;
            Reunir(c, guide, adultes, mal.Sujet, mal.Param, maintenant);
        }
    }

    /// <summary>Qui conduit la prière : un prêtre ou un prophète du groupe, sinon sa figure si elle croit, sinon le plus croyant.</summary>
    private static Humain? Guide(List<Humain> adultes, Communaute c)
    {
        var porteur = adultes.Where(h => h.Role is RoleReligieux.Pretre or RoleReligieux.Prophete).MaxBy(h => h.Ferveur);
        if (porteur is not null) return porteur;
        var figure = adultes.FirstOrDefault(h => h.Id == c.FigureId);
        if (figure is { Croyance: >= 40 }) return figure;
        var croyant = adultes.MaxBy(h => h.Croyance);
        return croyant is { Croyance: >= 50 } ? croyant : null;
    }

    /// <summary>Le mal qui frappe le plus le groupe, s'il en frappe assez : la faim, la sécheresse, la guerre, une épidémie, une catastrophe.</summary>
    private (SujetPriere Sujet, long Param, double Detresse)? MalCommun(List<Humain> adultes, Communaute c, Humain guide, long maintenant)
    {
        (SujetPriere, long, double)? meilleur = null;
        void Garder(SujetPriere s, long param, double d)
        {
            if (d > 0 && (meilleur is null || d > meilleur.Value.Item3)) meilleur = (s, param, d);
        }
        var affames = adultes.Count(h => h.Faim >= 60) / (double)adultes.Count;
        if (affames >= 0.5) Garder(SujetPriere.Nourriture, 0, affames);
        var region = ecosysteme.Region(guide.X, guide.Y);
        if (ecosysteme.Pluies[region] < 0.65) Garder(SujetPriere.Pluie, region, 1.2 - ecosysteme.Pluies[region]);
        if (Social?.Guerres.FirstOrDefault(w => w.Fin is null && (w.A == c.Id || w.B == c.Id)) is { } guerre) Garder(SujetPriere.Protection, guerre.Id, 0.7);
        var infectes = adultes.Count(h => h.InfectionId != 0);
        if (infectes >= Math.Max(3, adultes.Count / 5)) Garder(SujetPriere.Guerison, 0, Math.Min(1.5, 2.0 * infectes / adultes.Count));
        if (Catastrophes?.DangerEn(guide.X, guide.Y) is { } danger && danger.Source.Active) Garder(SujetPriere.Accalmie, danger.Source.Id, 1.2);
        return meilleur;
    }

    private void Reunir(Communaute c, Humain guide, List<Humain> adultes, SujetPriere sujet, long param, long maintenant)
    {
        var fideles = adultes.Where(h => h.Croyance >= CroyancePourParticiper).ToList();
        var ferveur = Math.Clamp(fideles.Average(h => h.Ferveur) * 0.5 + fideles.Average(h => h.Croyance) / 200, 0, 1);
        var priere = new Priere
        {
            Id = Cultes!.ProchainePriere++, HumainId = guide.Id, CommunauteId = c.Id, Le = maintenant, Sujet = sujet, Parametre = param,
            Ferveur = Math.Round(ferveur, 2), Forte = true, ReligionId = guide.ReligionId,
            // Une prière commune se garde plus longtemps qu'une prière seule : tous attendent, et le guide y tient.
            Jusqua = maintenant + (long)(JoursDAttente(sujet) * Patience(guide, vital: true) * Calendrier.SecondesParJour),
        };
        var objet = Recits.Recit.Seul(sujet switch
        {
            SujetPriere.Nourriture => "pric.objet.nourriture",
            SujetPriere.Pluie => "pric.objet.pluie",
            SujetPriere.Protection => "pric.objet.protection",
            SujetPriere.Guerison => "pric.objet.guerison",
            SujetPriere.Accalmie => "pric.objet.accalmie",
            _ => "pric.objet.aide",
        });
        priere.EvenementId = Noter(maintenant, TypeEvenement.Religion, guide, null,
            new Recit("pric.prie", ("c", c), ("qui", InvisibleDit(guide)), ("h", guide), ("n", fideles.Count), ("o", objet)), significatif: true);
        Cultes.Prieres.Add(priere);
        foreach (var o in fideles)
        {
            // Le rite apaise la peur de ceux qui y prennent part, même sans réponse (Lang et al., 2015).
            o.Peur *= 0.85;
            o.Ferveur = Math.Min(1, o.Ferveur + 0.02);
            o.PrieLe = maintenant;
        }
    }

    /// <summary>Ce que la communauté demandait est-il arrivé ? Sinon, une fois le délai passé, la prière n'a pas été entendue.</summary>
    private void ResoudreEnsemble(Priere p, IReadOnlyList<Humain> tous, long maintenant)
    {
        var c = Social is { } s && p.CommunauteId <= s.Communautes.Count ? s.Communautes[p.CommunauteId - 1] : null;
        var membres = tous.Vivants().Where(h => h.EstVivant && h.CommunauteId == p.CommunauteId && !h.EstEnfant(maintenant)).ToList();
        if (c is not { Active: true } || membres.Count == 0)
        {
            (p.Issue, p.IssueLe, p.Conclusion) = (IssuePriere.SansReponse, maintenant, Recits.Recit.Seul("pric.fin.disperse").Encoder());
            return;
        }
        var arrive = p.Sujet switch
        {
            SujetPriere.Nourriture => membres.Count(h => h.Faim >= 60) < 0.2 * membres.Count,
            SujetPriere.Pluie => ecosysteme.Pluies[(int)p.Parametre] >= 0.9,
            SujetPriere.Protection => Social!.Guerres.FirstOrDefault(g => g.Id == p.Parametre) is not { Fin: null },
            SujetPriere.Guerison => membres.All(h => h.InfectionId == 0),
            SujetPriere.Accalmie => Catastrophes?.Trouver(p.Parametre) is not { Active: true },
            _ => false,
        };
        if (arrive) ExauceEnsemble(p, c, membres, IssuePriere.ExauceeParHasard, maintenant);
        else if (maintenant >= p.Jusqua) SansReponseEnsemble(p, c, membres, maintenant);
    }

    private void ExauceEnsemble(Priere p, Communaute c, List<Humain> membres, IssuePriere issue, long maintenant, long cause = 0)
    {
        (p.Issue, p.IssueLe, p.Conclusion) = (issue, maintenant, Recits.Recit.Seul("pric.fin.entendue").Encoder());
        var guide = _tous[p.HumainId - 1];
        var id = Noter(maintenant, TypeEvenement.Religion, guide, null,
            new Recit("pric.exauce", ("c", c)),
            p.EvenementId != 0 ? p.EvenementId : cause, significatif: true);
        var gain = issue == IssuePriere.ExauceeParEntite ? 5 + 8 * p.Ferveur : 1.5 + 3 * p.Ferveur;
        foreach (var o in membres)
        {
            if (o.Croyance < CroyancePourParticiper) continue;
            Croyances.Ajouter(o, gain, Recits.Recit.Seul("cro.communeEntendue"), id, maintenant, signe: true);
            Regards.Ajouter(o, SentimentInvisible.Amour, 0.6 * gain * (0.6 + 0.8 * o.Personnalite.Agreabilite), Recits.Recit.Seul("cro.communeEntendue"), id, maintenant, signe: true);
        }
        if (guide.EstVivant && guide.Role == RoleReligieux.Prophete) guide.Miracles++;
    }

    private void SansReponseEnsemble(Priere p, Communaute c, List<Humain> membres, long maintenant)
    {
        (p.Issue, p.IssueLe) = (IssuePriere.SansReponse, maintenant);
        var guide = _tous[p.HumainId - 1];
        var doutent = 0;
        var id = Noter(maintenant, TypeEvenement.Religion, guide, null,
            new Recit("pric.vaine", ("c", c)), p.EvenementId, significatif: true);
        foreach (var o in membres)
        {
            if (o.Croyance < CroyancePourParticiper) continue;
            // Comme pour une prière seule : le fervent se trouve une raison, le tiède doute (Festinger et al., 1956).
            if (alea.Reel() < 0.2 + 0.7 * o.Croyance / 100) continue;
            doutent++;
            Croyances.Ajouter(o, -(1 + 4 * p.Ferveur), Recits.Recit.Seul("cro.communeVaine"), id, maintenant);
            o.Ferveur = Math.Max(0, o.Ferveur - 0.05);
        }
        p.Conclusion = (doutent == 0 ? Recits.Recit.Seul("pric.fin.raisons") : new Recit("pric.fin.doutent", ("n", doutent))).Encoder();
    }

    /// <summary>L'Entité exauce une prière collective : ce qu'elle fait dépend de ce que le groupe demandait.</summary>
    private Recit ExaucerEnsemble(Priere p, IReadOnlyList<Humain> tous, long maintenant)
    {
        var c = Social is { } s && p.CommunauteId <= s.Communautes.Count ? s.Communautes[p.CommunauteId - 1] : null;
        var membres = tous.Vivants().Where(h => h.EstVivant && h.CommunauteId == p.CommunauteId).ToList();
        if (c is not { Active: true } || membres.Count == 0) return Recits.Recit.Seul("pric.r.disparue");
        var guide = tous[p.HumainId - 1];
        Recit texte;
        switch (p.Sujet)
        {
            case SujetPriere.Nourriture:
                foreach (var i in membres.Select(h => carte.Indice(h.X, h.Y)).Distinct())
                {
                    ecosysteme.Gibier[i] = (ushort)Math.Min(ushort.MaxValue, ecosysteme.Gibier[i] + 2000);
                    ecosysteme.Plantes[i] = (ushort)Math.Min(ushort.MaxValue, ecosysteme.Plantes[i] + 2000);
                }
                foreach (var h in membres) h.Faim = Math.Min(h.Faim, 20);
                texte = new Recit("pric.don.manger", ("c", c));
                break;
            case SujetPriere.Pluie:
                ecosysteme.Pluies[(int)p.Parametre] = Math.Max(ecosysteme.Pluies[(int)p.Parametre], 1.15);
                texte = new Recit("pric.don.pluie", ("c", c));
                break;
            case SujetPriere.Accalmie:
                if (Catastrophes is null || !Catastrophes.Apaiser(p.Parametre, tous as List<Humain> ?? [.. tous], maintenant)) return Recits.Recit.Seul("pri.r.malheurFini");
                texte = new Recit("pric.don.accalmie", ("c", c));
                break;
            case SujetPriere.Guerison:
                foreach (var h in membres.Where(h => h.InfectionId != 0)) GuerirDeTout(h, maintenant);
                texte = new Recit("pric.don.guerison", ("c", c));
                break;
            case SujetPriere.Protection:
            {
                var guerre = Social!.Guerres.FirstOrDefault(g => g.Id == p.Parametre);
                if (guerre is not { Fin: null }) return Recits.Recit.Seul("pric.r.paix");
                var ennemi = guerre.A == c.Id ? guerre.B : guerre.A;
                foreach (var o in tous.Vivants())
                {
                    if (!o.EstVivant) continue;
                    if (o.CommunauteId == c.Id)
                    {
                        o.Peur *= 0.3;
                        if (o.PlaieLe > 0) o.Sante = Math.Max(o.Sante, 70);
                    }
                    else if (o.CommunauteId == ennemi) o.Peur = Math.Min(100, o.Peur + 40);
                }
                texte = new Recit("pric.don.protection", ("c", c));
                break;
            }
            default:
                return Recits.Recit.Seul("pric.r.rien");
        }
        var id = NoterEntite(maintenant, TypeEvenement.Religion, guide, null, new Recit("pric.exaucee", ("c", c), ("t", texte)), p.EvenementId, significatif: true);
        ExauceEnsemble(p, c, membres.Where(h => !h.EstEnfant(maintenant)).ToList(), IssuePriere.ExauceeParEntite, maintenant, id);
        return new Recit("pri.r.exaucee", ("t", texte));
    }

    /// <summary>
    /// L'Entité refuse une prière en attente, en secret ou par un signe. Renvoie, en mots, ce qui s'est passé, ou pourquoi
    /// elle ne peut être refusée.
    /// </summary>
    public Recit Refuser(int priereId, bool signe, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        if (Cultes?.Priere(priereId) is not { } p) return Recits.Recit.Seul("pri.r.inconnue");
        if (p.Issue != IssuePriere.EnAttente) return Recits.Recit.Seul("pri.r.close");
        if (p.Refus != RefusPriere.Aucun) return Recits.Recit.Seul("pri.r.dejaRefusee");
        if (!signe)
        {
            p.Refus = RefusPriere.Cache;
            return Recits.Recit.Seul("pri.r.secret");
        }
        p.Refus = RefusPriere.Signe;
        (p.Issue, p.IssueLe) = (IssuePriere.Refusee, maintenant);
        var guide = tous[p.HumainId - 1];
        var x = p.CibleId > 0 ? tous[p.CibleId - 1] : null;
        Jardin.Retirer(guide, "priere");
        if (p.CommunauteId != 0)
        {
            var c = Social!.Communautes[p.CommunauteId - 1];
            var id = NoterEntite(maintenant, TypeEvenement.Religion, guide, null,
                new Recit("pric.refus", ("c", c)), p.EvenementId, significatif: true);
            var membres = tous.Vivants().Where(h => h.EstVivant && h.CommunauteId == p.CommunauteId && !h.EstEnfant(maintenant)).ToList();
            var revoltes = membres.Count(o => o.Croyance >= CroyancePourParticiper && !SIncliner(o, p, id, maintenant));
            p.Conclusion = (revoltes == 0 ? Recits.Recit.Seul("pric.fin.inclinent") : new Recit("pric.fin.aigrissent", ("n", revoltes))).Encoder();
            return new Recit("pric.r.signe", ("c", c));
        }
        var maniere = Gestes.NomManiere(guide.Croyance >= 50 ? ManiereGeste.Voix : ManiereGeste.Reve);
        var evt = NoterEntite(maintenant, TypeEvenement.Religion, guide, x, new Recit("pri.refus", ("h", guide), ("m", maniere)),
            p.EvenementId, significatif: p.Forte);
        var incline = SIncliner(guide, p, evt, maintenant);
        p.Conclusion = new Recit(incline ? "pri.fin.incline" : "pri.fin.aigrit", ("h", guide)).Encoder();
        return new Recit(incline ? "pri.r.incline" : "pri.r.aigrit", ("h", guide));
    }

    /// <summary>
    /// Ce qu'un refus signifié fait à celui qui priait : l'invisible s'est montré, sa croyance monte. Le fervent et le
    /// bienveillant s'inclinent et le craignent ; les autres s'aigrissent, d'autant plus que l'enjeu était vital (la colère
    /// contre Dieu après un malheur : Exline et al., 2011). Renvoie vrai s'il s'incline.
    /// </summary>
    private bool SIncliner(Humain h, Priere p, long cause, long maintenant)
    {
        Croyances.Ajouter(h, 3, Recits.Recit.Seul("cro.refus"), cause, maintenant, signe: true);
        var pe = h.Personnalite;
        var incline = alea.Reel() < (0.2 + 0.6 * h.Croyance / 100) * (0.6 + 0.6 * pe.Agreabilite) * (1.2 - 0.4 * pe.Nevrosisme);
        var enjeu = p.Forte ? 1.5 : 1;
        if (incline)
        {
            Regards.Ajouter(h, SentimentInvisible.Crainte, 6 * enjeu, Recits.Recit.Seul("reg.non"), cause, maintenant, signe: true);
            Confier(h, TypeIntime.Croyance, 0, Recits.Recit.Seul("js.refus.croyance"), 30 + 30 * p.Ferveur, cause, maintenant, "croyance:refus");
        }
        else
        {
            Regards.Ajouter(h, SentimentInvisible.Revolte, 10 * enjeu * (0.5 + pe.Nevrosisme), Recits.Recit.Seul("reg.non"), cause, maintenant, signe: true);
            Confier(h, TypeIntime.Question, 0, Recits.Recit.Seul("js.refus.question"), 30 + 40 * p.Ferveur, cause, maintenant, "question:refus");
        }
        if (p.Sujet == SujetPriere.Vengeance)
            Confier(h, TypeIntime.Honte, p.CibleId, Recits.Recit.Seul("js.refus.honte"), 30 + 30 * pe.Agreabilite, cause, maintenant, "honte:refus");
        return incline;
    }
}
