using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Paroles de l'Entité adressées à une communauté entière, envers une ou plusieurs autres (ou vers une direction) : chaque
/// adulte la reçoit pour lui-même, la comprend, y croit ou non, et sait ce qu'il en pense ; le groupe en débattra ensuite
/// (voir <see cref="VieSociale.Mandats"/>).
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Un adulte de <paramref name="a"/> reçoit la parole adressée à son groupe. Pour chaque communauté visée (ou pour la
    /// direction de « Partez vers… »), il garde ce qu'il en pense : il veut obéir, il hésite, il ne le ferait qu'à moitié, ou
    /// il refuse. Renvoie, en mots, ce qu'il en a fait.
    /// </summary>
    public Recit RecevoirPourLeGroupe(Humain h, Parole parole, ModeParole mode, Communaute a, IReadOnlyList<Communaute> cibles, int direction,
        IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        if (!h.EstVivant) return new Recit("ent.r.mort", ("h", h));
        var p = h.Personnalite;
        object? extra = parole == Parole.PartezVers ? Recits.Recit.Seul($"direction.{Math.Clamp(direction, 0, 7)}")
            : cibles.Count == 1 ? cibles[0] : new Liste(cibles.Select(c => (object?)c).ToList());
        var phrase = Paroles.Phrase(parole, null, extra, h.Sexe == Sexe.Femme);
        var reception = Recevoir(h, mode, phrase, tous, maintenant, ensemble: true, significatif: false);
        // Une parole adressée au groupe se garde le temps qu'il en débatte et la mène à bien.
        var jusqua = maintenant + 2 * Calendrier.SecondesParAn;
        var parametres = parole == Parole.PartezVers ? [Math.Clamp(direction, 0, 7)] : cibles.Select(c => c.Id).ToList();

        if (alea.Reel() >= Comprehension(h, mode, null, maintenant))
        {
            NoterEntite(maintenant, TypeEvenement.Parole, h, null, mode switch
            {
                ModeParole.Reve => new Recit("ent.incompris.reve", ("h", h)),
                ModeParole.Signe => new Recit("ent.incompris.signe", ("h", h)),
                _ => new Recit("ent.incompris", ("h", h)),
            }, reception);
            foreach (var param in parametres)
                Garder(h, new Impulsion(parole, mode, 0, param, 0, maintenant, jusqua, long.MaxValue, Reponse.Incomprise, reception));
            return Recits.Recit.Seul("ent.r.incomprise");
        }

        var foi = Foi(h, mode, ensemble: true, maintenant);
        Repetition(h, reception, maintenant);
        if (alea.Reel() >= foi)
        {
            NoterEntite(maintenant, TypeEvenement.Parole, h, null, mode switch
            {
                ModeParole.Reve => new Recit("ent.incru.reve", ("h", h)),
                ModeParole.Voix => new Recit("ent.incru.voix", ("h", h)),
                ModeParole.Signe => new Recit("ent.incru.signe", ("h", h)),
                _ => new Recit("ent.incru.apparition", ("h", h)),
            }, reception);
            foreach (var param in parametres)
                Garder(h, new Impulsion(parole, mode, 0, param, 0, maintenant, jusqua, long.MaxValue, Reponse.Incrue, reception));
            return Recits.Recit.Seul("ent.r.incrue");
        }
        Eprouver(h, mode, reception, maintenant);

        // Ce qu'il en pense, pour chaque communauté visée : comme pour une parole à lui seul (voir Obeir), le respect de
        // l'autorité, la foi et l'amour de l'invisible y poussent ; ce que la parole lui coûte retient, ce qu'elle rejoint de
        // ses désirs pousse. Il ne décide pas seul : il dira son avis quand le groupe en débattra.
        var force = Math.Clamp(foi * (0.5 + 0.5 * h.Croyance / 100), 0.1, 1);
        var psychopathe = Psyche.EstPsychopathe(h, maintenant);
        var (amour, crainte) = (h.Amour / 100, h.Crainte / 100);
        var deference = (0.25 + 0.35 * p.Moral.Fondements.Autorite + 0.25 * h.Croyance / 100 + 0.15 * foi + 0.15 * amour + 0.1 * crainte)
            * (psychopathe ? 0.4 : 1) * (1 - 0.8 * h.Revolte / 100);
        var avis = new List<Reponse>();
        for (var k = 0; k < parametres.Count; k++)
        {
            var b = parole == Parole.PartezVers ? null : cibles[k];
            var (cout, envie, raison) = Social!.PeserPourLeGroupe(h, parole, a, b, tous, maintenant);
            var obeir = Math.Clamp(deference * (1 - cout) + 0.4 * envie, 0.01, 0.97);
            Reponse reponse;
            if (alea.Reel() < obeir) reponse = alea.Reel() < 0.5 * cout + 0.3 * crainte * (1 - amour) ? Reponse.AMoitie : Reponse.Acceptee;
            else if (alea.Reel() < 0.5 * (1 - p.Conscience) * (1 - cout)) reponse = Reponse.Differee;
            else reponse = Reponse.Refusee;
            Garder(h, new Impulsion(parole, mode, 0, parametres[k], force, maintenant, jusqua, long.MaxValue, reponse, reception));
            avis.Add(reponse);
            object qui = b is null ? Recits.Recit.Seul($"direction.{parametres[k]}") : b;
            NoterEntite(maintenant, TypeEvenement.Parole, h, null, reponse switch
            {
                Reponse.Refusee => new Recit("ent.groupe.refuse", ("h", h), ("c", qui), ("r", raison)),
                Reponse.Differee => new Recit("ent.groupe.hesite", ("h", h), ("c", qui)),
                Reponse.AMoitie => new Recit("ent.groupe.moitie", ("h", h), ("c", qui)),
                _ => new Recit("ent.groupe.veut", ("h", h), ("c", qui)),
            }, reception, significatif: Paroles.NuitAuxAutres(parole) && reponse == Reponse.Acceptee);
            Confier(h, TypeIntime.Intention, b?.FigureId ?? 0, new Recit(reponse == Reponse.Refusee ? "js.mandat.refuse" : "js.mandat.veut", ("ph", phrase)),
                30 + 40 * foi, reception, maintenant, $"mandat:{(int)parole}:{parametres[k]}");
            if (Paroles.NuitAuxAutres(parole) && reponse == Reponse.Refusee && !psychopathe)
                Confier(h, TypeIntime.Peur, 0, Recits.Recit.Seul("js.entite.mal"), 30 + 40 * p.Moral.EmpathieEmotionnelle, reception, maintenant, "entite:mal");
        }
        if (h.Role == RoleReligieux.Prophete && parametres.Count > 0) Reveler(h, parole, parametres[0], reception, tous, maintenant);
        // Une seule communauté visée : son avis ; plusieurs : le plus engagé de ses avis.
        var resume = avis.Contains(Reponse.Acceptee) ? Reponse.Acceptee : avis.Contains(Reponse.AMoitie) ? Reponse.AMoitie
            : avis.Contains(Reponse.Differee) ? Reponse.Differee : Reponse.Refusee;
        return resume switch
        {
            Reponse.Acceptee => new Recit("ent.r.groupe.veut", ("h", h)),
            Reponse.AMoitie => new Recit("ent.r.groupe.moitie", ("h", h)),
            Reponse.Differee => new Recit("ent.r.groupe.hesite", ("h", h)),
            _ => new Recit("ent.r.groupe.refuse", ("h", h)),
        };
    }
}
