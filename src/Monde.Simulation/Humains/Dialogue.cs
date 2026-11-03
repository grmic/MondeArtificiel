using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Comment un dialogue avec l'Entité a pris fin (0.36.2).</summary>
public enum FinDialogue : byte
{
    /// <summary>Il continue.</summary>
    Aucune = 0,
    /// <summary>L'Entité s'est tue (le joueur a fini).</summary>
    Joueur = 1,
    /// <summary>Il s'est réveillé : le rêve s'est arrêté.</summary>
    Reveil = 2,
    /// <summary>La peur l'a fait fuir.</summary>
    Fuite = 3,
    /// <summary>Il n'y croit pas et se détourne (folie, illusion).</summary>
    Detourne = 4,
    /// <summary>Plus personne n'a parlé depuis longtemps.</summary>
    Silence = 5,
    /// <summary>Il est mort pendant le dialogue (ou ne peut plus entendre).</summary>
    Absent = 6,
}

/// <summary>
/// Ce que les règles ont fait d'une réplique de l'Entité : l'a-t-il comprise, croit-il parler à l'invisible, et le dialogue
/// continue-t-il. <paramref name="Reception"/> : la ligne du journal de la manifestation (0 : aucune nouvelle).
/// </summary>
public sealed record RegleReplique(bool Comprise, bool Croit, FinDialogue Fin, long Reception);

/// <summary>
/// Le dialogue direct entre l'Entité et un humain (0.36.2). Chaque réplique de l'Entité passe par les mêmes règles qu'une parole :
/// la voie compte (on comprend mal un rêve, on croit plus une apparition qu'une voix), chacun y croit ou non selon sa croyance et son
/// esprit. Ce qu'elle demande, traduit en paroles codées, suit les règles des paroles (obéir, hésiter, refuser). Le dialogue peut
/// finir sans que l'Entité le veuille : le rêveur se réveille, l'effrayé fuit, l'incrédule se détourne.
/// <para>
/// Un rêve ne dure que le temps d'une phase de sommeil paradoxal : une dizaine de minutes en début de nuit, jusqu'à près d'une heure
/// au petit matin (Carskadon et Dement, 2011) ; un cauchemar réveille. Entendre une voix ou voir une présence effraie surtout les
/// anxieux (Luhrmann, 2012). Les esprits analytiques qui ne croient pas tiennent ces expériences pour des illusions et s'en
/// détournent (Gervais et Norenzayan, 2012).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Minutes de rêve, en moyenne, que prend une réplique (dire, entendre, répondre).</summary>
    private const double MinutesParReplique = 1.5;

    /// <summary>
    /// Une réplique de l'Entité à <paramref name="h"/>. <paramref name="rang"/> : son rang dans le dialogue (1 : la première) ;
    /// <paramref name="croyait"/> : s'il croyait déjà lui parler (null : il n'a encore rien compris) ; <paramref name="paroles"/> :
    /// ce qu'il a fait des paroles codées de cette réplique (déjà passées par les règles des paroles ; vide : elle ne fait que parler).
    /// <paramref name="mots"/> : ses mots, pour le journal.
    /// </summary>
    public RegleReplique Ecouter(Humain h, ModeParole mode, int rang, bool? croyait, IReadOnlyList<Reponse> paroles, string mots,
        IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        if (!h.EstVivant || h.EstBebe(maintenant)) return new RegleReplique(false, croyait ?? false, FinDialogue.Absent, 0);
        var p = h.Personnalite;
        long reception = 0;
        bool comprise, croit;
        if (paroles.Count > 0)
        {
            // Les règles des paroles ont déjà décidé : il a compris si l'une au moins l'a été, il croit si l'une au moins a été crue.
            comprise = paroles.Any(r => r != Reponse.Incomprise);
            croit = paroles.Any(r => r is not (Reponse.Incomprise or Reponse.Incrue)) || croyait == true && comprise;
        }
        else
        {
            // La première réplique est une manifestation (au journal, et vue des témoins si elle apparaît) ; les suivantes non.
            if (rang <= 1) reception = Recevoir(h, mode, new Recit("dlg.phrase", ("t", mots)), tous, maintenant, false);
            comprise = alea.Reel() < Comprehension(h, mode, null, maintenant);
            if (!comprise) croit = croyait ?? false;
            else if (croyait is { } deja) croit = deja;
            else
            {
                // La première fois qu'il comprend : y croire ou non, comme pour une parole.
                Repetition(h, reception, maintenant);
                croit = alea.Reel() < Foi(h, mode, false, maintenant);
                if (croit) Eprouver(h, mode, reception, maintenant);
                else if (mode is ModeParole.Voix or ModeParole.Apparition && p.Nevrosisme > 0.55)
                {
                    h.Peur = Math.Min(100, h.Peur + 15 * p.Nevrosisme);
                    Confier(h, TypeIntime.Peur, 0, Recit.Seul("js.entite.folie"), 20 + 40 * p.Nevrosisme, reception, maintenant, "entite:folie");
                }
            }
        }
        return new RegleReplique(comprise, croit, Fin(h, mode, comprise, croit, maintenant), reception);
    }

    /// <summary>Le dialogue continue-t-il ? Le rêve s'arrête, la peur fait fuir, l'incrédule se détourne.</summary>
    private FinDialogue Fin(Humain h, ModeParole mode, bool comprise, bool croit, long maintenant)
    {
        var p = h.Personnalite;
        if (mode == ModeParole.Reve)
        {
            // Phases de sommeil paradoxal plus longues à mesure que la nuit avance (de 10 min vers 21 h à près d'une heure au matin).
            var heure = Calendrier.Decomposer(maintenant).Heure;
            var nuit = Math.Clamp(((heure + 24 - 21) % 24) / 9.0, 0, 1);
            var reveil = MinutesParReplique / (10 + 50 * nuit) + 0.3 * h.Peur / 100 * p.Nevrosisme;
            if (alea.Reel() < reveil) return FinDialogue.Reveil;
        }
        else
        {
            var choc = mode == ModeParole.Apparition ? 1.0 : 0.7;
            var fuite = Math.Max(0, (h.Peur - 40) / 60) * (0.3 + 0.7 * p.Nevrosisme) * choc;
            if (alea.Reel() < fuite) return FinDialogue.Fuite;
        }
        if (comprise && !croit && alea.Reel() < 0.2 + 0.5 * Croyances.Analytique(h)) return FinDialogue.Detourne;
        // Un petit enfant ne tient pas longtemps.
        var age = h.AgeEnAnnees(maintenant);
        if (age < 8 && alea.Reel() < 0.25 * (8 - age) / 5) return mode == ModeParole.Reve ? FinDialogue.Reveil : FinDialogue.Detourne;
        return FinDialogue.Aucune;
    }

    /// <summary>
    /// Le dialogue est fini : une ligne au journal (la voie, le nombre de répliques, comment il a pris fin), un souvenir dans son jardin
    /// secret ; s'il y a cru, il peut le raconter aux siens. Rend la ligne du journal.
    /// </summary>
    public long FinirDialogue(Humain h, ModeParole mode, int repliques, bool croit, bool comprise, FinDialogue fin, long cause,
        IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        var m = Recit.Seul($"modeParole.{mode}");
        var id = NoterEntite(maintenant, TypeEvenement.Parole, h, null,
            new Recit($"dlg.fin.{fin}", ("h", h), ("m", m), ("n", repliques)), cause, significatif: true);
        if (!h.EstVivant || repliques == 0) return id;
        var p = h.Personnalite;
        var force = Math.Min(1, repliques / 6.0);
        if (!comprise)
            Confier(h, TypeIntime.Question, 0, new Recit("js.dialogue.incompris", ("m", m)), 20 + 30 * p.Ouverture, id, maintenant, "entite:dialogue");
        else if (croit)
            Confier(h, TypeIntime.Croyance, 0, new Recit("js.dialogue.cru", ("m", m)), 40 + 30 * force + 20 * p.Transcendance, id, maintenant, "entite:dialogue");
        else
            Confier(h, TypeIntime.Question, 0, new Recit("js.dialogue.doute", ("m", m)), 25 + 30 * force, id, maintenant, "entite:dialogue");
        if (croit) Raconter(h, false, mode, id, tous, maintenant);
        return id;
    }
}
