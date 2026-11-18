using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Le ton de ce que l'Entité lui dit (0.42.9), tel que la traduction l'a entendu.</summary>
public enum TonParole : byte
{
    Aucun = 0,
    /// <summary>Elle le rassure, le console.</summary>
    Reconfort = 1,
    /// <summary>Elle le loue, le remercie.</summary>
    Louange = 2,
    /// <summary>Elle lui promet un bien.</summary>
    Promesse = 3,
    /// <summary>Elle le menace.</summary>
    Menace = 4,
    /// <summary>Elle lui reproche ce qu'il a fait.</summary>
    Reproche = 5,
    /// <summary>Elle est en colère contre lui.</summary>
    Courroux = 6,
}

/// <summary>
/// Ce que les mots de l'Entité lui font, et ce qu'il lui demande ou lui promet de lui-même (0.42.9). Le ton touche son cœur, mais
/// chacun selon son caractère : être rassuré apaise d'autant plus que l'on avait peur et que l'on est confiant ; une menace effraie
/// surtout l'anxieux (Lazarus et Folkman, 1984) et pousse l'esprit indocile à résister (la réactance : Brehm, 1966) ; un reproche
/// fait naître la culpabilité chez le consciencieux, la colère chez le peu accommodant (Tangney et Dearing, 2002). Tout reste borné :
/// une parole ne retourne pas une âme d'un coup.
/// <para>
/// Une promesse faite à l'Entité est tenue ou non : le consciencieux et l'honnête tiennent parole (Roberts et al., 2009, sur la
/// conscience et la fiabilité ; Ashton et Lee, 2007, sur l'honnêteté-humilité) ; ce qui lui coûte en ce moment l'en détourne ;
/// celui qui la rompt en garde du remords, selon sa conscience.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Il ressent le ton de ce que l'Entité vient de lui dire. Plus fort s'il croit que c'est elle, et devant une apparition qu'en
    /// rêve. Rend la ligne du journal (null : rien de ressenti).
    /// </summary>
    public Recit? RessentirTon(Humain h, TonParole ton, ModeParole mode, bool croit, long cause, long maintenant)
    {
        if (ton == TonParole.Aucun || !Enum.IsDefined(ton) || !h.EstVivant || h.EstBebe(maintenant)) return null;
        _maintenant = maintenant;
        var p = h.Personnalite;
        var force = (croit ? 1 : 0.35) * mode switch
        {
            ModeParole.Apparition => 1,
            ModeParole.Voix => 0.85,
            ModeParole.Reve => 0.6,
            _ => 0.5,
        };
        var (n, a, c, e) = (p.Nevrosisme, p.Agreabilite, p.Conscience, p.Extraversion);
        static double Borne(double v) => Math.Clamp(v, 0, 100);
        TypeIntime type;
        double intensite;
        switch (ton)
        {
            case TonParole.Reconfort:
                // Être rassuré apaise d'autant plus que l'on avait peur ou du chagrin.
                h.Peur = Borne(h.Peur - h.Peur * 0.5 * force * (0.6 + 0.4 * a));
                h.Chagrin = Borne(h.Chagrin - h.Chagrin * 0.3 * force * (0.5 + 0.5 * p.Attachement));
                h.Humeur = Borne(h.Humeur + 8 * force);
                if (croit) h.Amour = Borne(h.Amour + 6 * force * (0.5 + a));
                (type, intensite) = (TypeIntime.Espoir, 30 + 40 * force);
                break;
            case TonParole.Louange:
                h.Humeur = Borne(h.Humeur + 10 * force * (0.5 + e));
                if (croit) h.Amour = Borne(h.Amour + 5 * force);
                (type, intensite) = (TypeIntime.Fierte, 30 + 40 * force * (0.5 + e));
                break;
            case TonParole.Promesse:
                h.Humeur = Borne(h.Humeur + 7 * force);
                if (croit) h.Amour = Borne(h.Amour + 4 * force);
                (type, intensite) = (TypeIntime.Espoir, 30 + 40 * force * (0.4 + p.Transcendance));
                break;
            case TonParole.Menace:
                h.Peur = Borne(h.Peur + 30 * force * (0.4 + n));
                if (croit) h.Crainte = Borne(h.Crainte + 10 * force * (0.5 + n));
                if (croit) h.Revolte = Borne(h.Revolte + 8 * force * Math.Max(0, 1.2 - a - 0.4 * n));
                if (croit) h.Amour = Borne(h.Amour - 5 * force);
                (type, intensite) = (TypeIntime.Peur, 30 + 50 * force * n);
                break;
            case TonParole.Reproche:
                h.Chagrin = Borne(h.Chagrin + 12 * force * (0.4 + c) * (0.5 + n));
                h.Humeur = Borne(h.Humeur - 8 * force);
                h.Colere = Borne(h.Colere + 10 * force * Math.Max(0, 1 - a));
                if (croit) h.Revolte = Borne(h.Revolte + 6 * force * Math.Max(0, 1.1 - a - 0.3 * c));
                (type, intensite) = (c + n > 1 ? TypeIntime.Culpabilite : TypeIntime.Peine, 25 + 40 * force * (0.4 + c));
                break;
            default:
                h.Peur = Borne(h.Peur + 35 * force * (0.4 + n));
                h.Humeur = Borne(h.Humeur - 10 * force);
                if (croit) h.Crainte = Borne(h.Crainte + 12 * force * (0.5 + n));
                if (croit) h.Revolte = Borne(h.Revolte + 6 * force * Math.Max(0, 1.2 - a - 0.4 * n));
                (type, intensite) = (TypeIntime.Peur, 35 + 50 * force * n);
                break;
        }
        Confier(h, type, 0, Recit.Seul($"js.ton.{ton}"), intensite, cause, maintenant, "entite:ton");
        var r = new Recit($"ton.{ton}", ("h", h));
        NoterEntite(maintenant, TypeEvenement.Parole, h, null, r, cause);
        return r;
    }

    /// <summary>
    /// Au cours du dialogue, il demande lui-même quelque chose à l'Entité : c'est une vraie prière, parmi les autres (voir
    /// <see cref="PrierSurParole"/>). Rend la ligne du journal.
    /// </summary>
    public long DemanderEnDialogue(Humain h, SujetPriere sujet, int pour, IReadOnlyList<Humain> tous, long cause, long maintenant)
    {
        _maintenant = maintenant;
        if (!h.EstVivant || !Enum.IsDefined(sujet)) return 0;
        // « Mon enfant est ici » n'est pas « donne-moi un enfant » (0.44.8) : celle qui est enceinte, ou qui a un tout-petit à
        // porter, ne prie pas pour en avoir un ; le mot « enfant » dans sa réponse avait fait naître une telle prière.
        if (sujet == SujetPriere.Enfant && (h.EstEnceinte || tous.Any(e => e.EstVivant && e.EstBebe(maintenant) && (e.MereId == h.Id || e.PereId == h.Id))))
            return 0;
        return PrierSurParole(h, tous, maintenant, cause, sujet, pour);
    }

    /// <summary>
    /// Il promet à l'Entité un geste de tous les jours (prier, veiller le feu, jeûner…). Il compte le faire bientôt : au moment
    /// venu, il tient parole ou non (voir <see cref="TenirParole"/>). Rend la ligne du journal.
    /// </summary>
    /// <summary>
    /// Ce que son corps a fait en répondant à l'Entité (0.45.19) : « Meisen ferme les yeux, en répondant à l'Entité ». Un acte qu'il
    /// choisit de faire : journalisé avec sa cause, la réplique de l'Entité. Rien d'autre ne change dans le monde.
    /// </summary>
    public long NoterGestes(Humain h, IReadOnlyList<string> gestes, long cause, long maintenant)
    {
        if (!h.EstVivant || gestes.Count == 0) return 0;
        var g = gestes.Take(3).Select(x => new Recit($"gesteFait.{x}")).ToList();
        var texte = g.Count switch
        {
            1 => new Recit("dlg.fait1", ("h", h), ("a", g[0])),
            2 => new Recit("dlg.fait2", ("h", h), ("a", g[0]), ("b", g[1])),
            _ => new Recit("dlg.fait3", ("h", h), ("a", g[0]), ("b", g[1]), ("c", g[2])),
        };
        return NoterEntite(maintenant, TypeEvenement.Parole, h, null, texte, cause);
    }

    public long Promettre(Humain h, Parole parole, ModeParole mode, long cause, long maintenant)
    {
        _maintenant = maintenant;
        if (!h.EstVivant || !Paroles.Demande(parole) || Paroles.VeutCible(parole)) return 0;
        var p = h.Personnalite;
        var id = NoterEntite(maintenant, TypeEvenement.Parole, h, null,
            new Recit("dem.promet", ("h", h), ("ph", Paroles.Phrase(parole, null, null))), cause);
        // Il compte s'y mettre dans les heures qui viennent ; le consciencieux sans tarder.
        var agir = maintenant + (long)((0.5 + 6 * (1 - p.Conscience) * alea.Reel()) * 3600);
        Garder(h, new Impulsion(parole, mode, 0, 0, 0.5 + 0.5 * p.Conscience, maintenant, agir + Calendrier.SecondesParJour, agir,
            Reponse.Differee, id, Promesse: true));
        Confier(h, TypeIntime.Intention, 0, new Recit("js.promesse", ("ph", Paroles.Phrase(parole, null, null))), 30 + 40 * p.Conscience, id,
            maintenant, $"entite:{(int)parole}");
        return id;
    }

    /// <summary>
    /// Le moment de tenir sa promesse : le consciencieux et l'honnête la tiennent, ce qu'elle coûte maintenant (la faim, la fatigue,
    /// l'impossible) l'en détourne. Celui qui la rompt en garde du remords. Vrai s'il la tient.
    /// </summary>
    private bool TenirParole(Humain h, Impulsion i, double cout, double envie, long maintenant)
    {
        var p = h.Personnalite;
        var etat = Math.Max(0, h.Fatigue - 50) / 100 + Math.Max(0, h.Faim - 60) / 100;
        var tenir = Math.Clamp(0.15 + 0.45 * p.Conscience + 0.3 * p.Moral.Honnetete + 0.2 * envie - 0.6 * cout - etat, 0.03, 0.97);
        if (alea.Reel() < tenir)
        {
            NoterEntite(maintenant, TypeEvenement.Parole, h, null, new Recit("dem.promesseTenue", ("h", h), ("ph", Paroles.Phrase(i.Parole, null, null))),
                i.EvenementId);
            return true;
        }
        NoterEntite(maintenant, TypeEvenement.Parole, h, null, new Recit("dem.promesseRompue", ("h", h), ("ph", Paroles.Phrase(i.Parole, null, null))),
            i.EvenementId);
        Jardin.Retirer(h, $"entite:{(int)i.Parole}");
        if (p.Conscience > 0.35)
            Confier(h, TypeIntime.Culpabilite, 0, Recit.Seul("js.promesseRompue"), 20 + 50 * p.Conscience, i.EvenementId, maintenant, "entite:promesse");
        return false;
    }
}
