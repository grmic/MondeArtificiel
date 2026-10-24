using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Garder le feu. Allumer un feu est difficile, et le savoir se perd : les peuples qui ne savaient pas l'allumer ne l'ont pas
/// perdu pour autant. Les Andamanais le gardaient au camp et l'emportaient en voyage, dans des braises et des mèches qui couvent
/// (Radcliffe-Brown, 1922). On couvre le feu de cendre pour la nuit, on le veille quand il est précieux, et qui l'a perdu va en
/// demander à des voisins. Rien de tout cela n'est donné : couvrir la braise et porter une mèche se découvrent, se montrent et se
/// perdent, comme les autres savoirs.
/// </summary>
public sealed partial class Comportement
{
    private static string Lui(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "lui";

    /// <summary>Porte-t-il du feu sur lui (un brandon ou une mèche qui couve) ?</summary>
    private static bool PorteLeFeu(Humain h) => h.Porte(Matiere.Brandon) || h.Porte(Matiere.PorteFeu);

    /// <summary>Le feu qu'il porte : le brandon d'abord, qui ne durera pas, puis la mèche (null : aucun).</summary>
    private static Matiere? FeuPorte(Humain h) =>
        h.Porte(Matiere.Brandon) ? Matiere.Brandon : h.Porte(Matiere.PorteFeu) ? Matiere.PorteFeu : null;

    /// <summary>
    /// Ce que vaut le feu pour ceux qui vivent autour de lui, de 0 à 1 : la part des adultes proches (lui compris) qui ne savent
    /// pas le rallumer. Un feu que personne ne sait refaire est précieux.
    /// </summary>
    private double FeuPrecieux(Humain h, IReadOnlyList<Humain> tous)
    {
        int adultes = 0, savent = 0;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.EstEnfant(_maintenant)) continue;
            if (o.Id != h.Id && (h.CommunauteId == 0 || o.CommunauteId != h.CommunauteId)
                && Distance(o.X, o.Y, h.X, h.Y) > VieSociale.DistanceCommunaute) continue;
            adultes++;
            if (o.Savoirs.Exists(s => s.Effet == Effet.Flamme && s.Confiance >= 0.3)) savent++;
        }
        return adultes == 0 ? 1 : 1 - (double)savent / adultes;
    }

    /// <summary>Le groupe garde-t-il le souvenir d'un incendie parti d'un camp, qui a tué des siens ?</summary>
    private bool SeSouvientDUnIncendie(Humain h) => h.CommunauteId != 0 && Social?.CommunauteDe(h) is { IncendiesSouvenus.Count: > 0 };

    /// <summary>
    /// Au matin, une braise qui couve encore sous la cendre : qui sait entretenir un feu peut remarquer qu'elle a passé la nuit.
    /// C'est ainsi qu'on apprend à couvrir le feu pour la garder.
    /// </summary>
    private void DecouvrirLaBraise(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Activite == Activite.Dormir || h.EstEnfant(_maintenant) || Installations.BraiseEn(h.X, h.Y) is null) return;
        if (h.Sait(Effet.Braises) || !h.Sait(Effet.Foyer)) return;
        var heure = Calendrier.Decomposer(_maintenant).Heure;
        if (heure < 5 || heure > 9) return;
        if (alea.Reel() >= 0.02 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h))) return;
        var id = Retenir(h, Geste.Couvrir, Matiere.Aucune, Matiere.Feu, Effet.Braises, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            recit: $"Au matin, {h.Nom} remue la cendre froide du feu de la veille : dessous, une braise rougit encore.");
        Montrer(h, Geste.Couvrir, Matiere.Aucune, Matiere.Feu, Effet.Braises, 0, Symptome.Aucun, tous, id);
    }

    /// <summary>Une braise couve ici : qui sait entretenir un feu la ranime avec un peu de bois. Vrai s'il l'a fait.</summary>
    private bool RanimerLaBraise(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.CaptifDe != 0 || Installations.BraiseEn(h.X, h.Y) is null) return false;
        if (!h.Sait(Effet.Foyer) || !APortee(h, Matiere.BoisSec)) return false;
        h.Activite = Activite.Veiller;
        h.CibleX = h.CibleY = -1;
        Faire(h, Geste.Entasser, Matiere.BoisSec, Matiere.Feu, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>
    /// Le feu au camp : le soir, on le nourrit pour la nuit s'il est précieux, et qui sait le faire le couvre de cendre avant de
    /// dormir ; dans la journée, on y prépare la mèche qui portera le feu. En route, qui porte un feu près de s'éteindre en allume
    /// un pour ne pas le perdre ; et qui n'a plus de feu va en demander. Vrai s'il s'y est mis.
    /// </summary>
    private bool GarderLeFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.CaptifDe != 0) return false;
        var heure = Calendrier.Decomposer(_maintenant).Heure;
        var foyer = Installations.FoyerEn(h.X, h.Y);
        if (foyer is not null)
        {
            if (heure >= 20 && heure <= 23 && NourrirPourLaNuit(h, foyer, tous)) return true;
            if (heure >= 20 && heure <= 23 && CouvrirLeFeu(h, foyer, tous)) return true;
            if (heure >= 6 && heure <= 18 && PreparerLePorteFeu(h, tous)) return true;
            return false;
        }
        // Au matin, la braise de la nuit : si personne autour ne sait refaire le feu, on la ranime aussitôt.
        if (heure >= 5 && heure <= 9 && Installations.BraiseEn(h.X, h.Y) is not null && h.Sait(Effet.Foyer)
            && alea.Reel() < 0.3 * (0.5 + h.Personnalite.Conscience) && FeuPrecieux(h, tous) >= 0.5 && RanimerLaBraise(h, tous))
            return true;
        if (GarderLeFeuEnRoute(h, tous)) return true;
        return ChercherLeFeu(h, tous);
    }

    /// <summary>Le soir, un feu précieux qui faiblit : on y remet de quoi passer la nuit.</summary>
    private bool NourrirPourLaNuit(Humain h, Foyer foyer, IReadOnlyList<Humain> tous)
    {
        if (foyer.Heures >= 6 || !h.Sait(Effet.Foyer) || !APortee(h, Matiere.BoisSec)) return false;
        if (alea.Reel() >= 0.3 * (0.5 + h.Personnalite.Conscience) || FeuPrecieux(h, tous) < 0.5) return false;
        h.Activite = Activite.Veiller;
        h.CibleX = h.CibleY = -1;
        Faire(h, Geste.Entasser, Matiere.BoisSec, Matiere.Feu, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>Avant de dormir, qui sait garder la braise couvre le feu de cendre ; ceux qui le voient faire peuvent l'imiter.</summary>
    private bool CouvrirLeFeu(Humain h, Foyer foyer, IReadOnlyList<Humain> tous)
    {
        if (foyer.Couvert || !h.Savoirs.Exists(s => s.Effet == Effet.Braises && s.Confiance >= 0.4)) return false;
        if (alea.Reel() >= 0.2 * (0.4 + h.Personnalite.Conscience)) return false;
        h.Activite = Activite.Veiller;
        h.CibleX = h.CibleY = -1;
        var (x, y) = (h.X, h.Y);
        Tour.Differer(() => Installations.Couvrir(x, y));
        var id = Retenir(h, Geste.Couvrir, Matiere.Aucune, Matiere.Feu, Effet.Braises, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        if (JournalQuotidien) Noter(_maintenant, TypeEvenement.Pensee, h, null, $"{h.Nom} couvre le feu de cendre pour la nuit.", id != 0 ? id : foyer.EvenementId);
        Montrer(h, Geste.Couvrir, Matiere.Aucune, Matiere.Feu, Effet.Braises, 0, Symptome.Aucun, tous, 0);
        return true;
    }

    /// <summary>
    /// Près du feu, dans la journée : qui sait porter le feu dans une mèche en prépare une s'il n'en a pas, d'autant plus que le
    /// feu est précieux ; qui sait déjà emporter un brandon peut trouver, en approchant une corde du feu, qu'elle couve sans flamber.
    /// </summary>
    private bool PreparerLePorteFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Porte(Matiere.PorteFeu)) return false;
        Matiere? meche = APortee(h, Matiere.Corde) ? Matiere.Corde : APortee(h, Matiere.Fibres) ? Matiere.Fibres : null;
        if (meche is not { } m) return false;
        var sait = h.Savoirs.Exists(s => s.Effet == Effet.PorteFeu && s.Confiance >= 0.4);
        if (!sait && !h.Sait(Effet.Brandon)) return false;
        var p = sait ? 0.03 * (0.4 + h.Personnalite.Conscience) : 0.0003 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h));
        // Le tirage d'abord : compter ceux qui savent refaire le feu coûte plus cher.
        if (alea.Reel() >= 1.5 * p || alea.Reel() >= (0.5 + FeuPrecieux(h, tous)) / 1.5) return false;
        h.Activite = sait ? Activite.Fabriquer : Activite.Experimenter;
        h.CibleX = h.CibleY = -1;
        Faire(h, Geste.Approcher, m, Matiere.Feu, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>En route, le feu qu'il porte va s'éteindre : il en allume un là où il est, pour ne pas le perdre.</summary>
    private bool GarderLeFeuEnRoute(Humain h, IReadOnlyList<Humain> tous)
    {
        var porte = FeuPorte(h);
        if (porte is not { } p || !h.Sait(Effet.Foyer) || !APortee(h, Matiere.BoisSec)) return false;
        if (h.Objets.Find(o => o.Matiere == p) is not { Usure: < 0.25 }) return false;
        if (FoyerProche(h.X, h.Y) || Installations.BraiseEn(h.X, h.Y) is not null) return false;
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        Faire(h, Geste.Entasser, Matiere.BoisSec, p, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>
    /// Il vient d'allumer ailleurs le feu qu'il portait. Celui qui le fait plus souvent que tous les siens, et plus de deux fois,
    /// devient pour eux celui qui porte le feu : ce n'est pas un titre, c'est ce qu'on dit de lui.
    /// </summary>
    private void AvoirPorteLeFeu(Humain h)
    {
        h.FeuxPortes++;
        if (h.PorteurDuFeuDepuis != 0 || h.CommunauteId == 0 || h.FeuxPortes < 3 || _tous is not { } tous) return;
        Humain? ancien = null;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.Id == h.Id || o.CommunauteId != h.CommunauteId) continue;
            if (o.FeuxPortes >= h.FeuxPortes) return;
            if (o.PorteurDuFeuDepuis != 0) ancien = o;
        }
        h.PorteurDuFeuDepuis = _maintenant;
        if (ancien is not null) Tour.Changer(ancien, x => x.PorteurDuFeuDepuis = 0);
        Noter(_maintenant, TypeEvenement.Decouverte, h, ancien,
            $"Les siens disent de {h.Nom} qu'{Il(h)} porte leur feu : à chaque halte, c'est {Lui(h)} qui le rallume{(ancien is null ? "" : $", comme le faisait {ancien.Nom}")}.");
    }

    /// <summary>Allumer un feu neuf : qui sait dégager le foyer écarte l'herbe et l'entoure de pierres (et ceux qui regardent l'imitent).</summary>
    private bool DegagerLeFoyer(Humain h)
    {
        if (Installations.FoyerEn(h.X, h.Y) is { Degage: true }) return false;
        if (!h.Savoirs.Exists(s => s.Effet == Effet.FoyerDegage && s.Confiance >= 0.5)) return false;
        if (alea.Reel() >= Math.Min(1, (0.5 + 0.5 * h.Personnalite.Prudence) * (SeSouvientDUnIncendie(h) ? 1.3 : 1))) return false;
        if (_tous is { } tous)
        {
            Retenir(h, Geste.Degager, Matiere.Aucune, Matiere.Feu, Effet.FoyerDegage, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
            Montrer(h, Geste.Degager, Matiere.Aucune, Matiere.Feu, Effet.FoyerDegage, 0, Symptome.Aucun, tous, 0);
        }
        return true;
    }

    /// <summary>
    /// Il voudrait allumer un feu, mais le vent souffle fort sur l'herbe sèche : qui a appris ce qu'il en coûte attend qu'il tombe,
    /// sauf si le froid le presse. Vrai s'il renonce.
    /// </summary>
    private bool AttendreLeCalme(Humain h, IReadOnlyList<Humain> tous)
    {
        if (Catastrophes is null || h.Froid > 30 || !h.Savoirs.Exists(s => s.Effet == Effet.PasDansLeVent && s.Confiance >= 0.5)) return false;
        if (Catastrophes.VentEn(h.X, h.Y, _maintenant) < 1.3 || Sec(h) < 0.5) return false;
        if (alea.Reel() >= Math.Min(1, (0.5 + 0.5 * h.Personnalite.Prudence) * (SeSouvientDUnIncendie(h) ? 1.3 : 1))) return false;
        Retenir(h, Geste.Patienter, Matiere.Aucune, Matiere.Feu, Effet.PasDansLeVent, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        return true;
    }

    /// <summary>
    /// Il quitte un feu vif sans personne pour le garder, et part loin : qui a appris qu'un feu laissé ainsi gagne les herbes
    /// l'éteint. S'il est précieux, il en emporte d'abord une braise ; s'il ne peut pas l'emporter, il le laisse brûler.
    /// </summary>
    private void EteindreEnPartant(Humain h, int x0, int y0, IReadOnlyList<Humain> tous)
    {
        if (h.X == x0 && h.Y == y0 || h.EstEnfant(_maintenant) || Installations.FoyerEn(x0, y0) is null) return;
        if (!h.Savoirs.Exists(s => s.Effet == Effet.EteindreEnPartant && s.Confiance >= 0.5)) return;
        // On ne part pas loin : on reviendra au feu.
        var loin = h.CibleX >= 0 && Distance(h.CibleX, h.CibleY, x0, y0) >= 3 || h.Activite is Activite.Migrer or Activite.PartirEnExil;
        if (!loin) return;
        foreach (var o in tous.Vivants())
            if (o.EstVivant && o.Id != h.Id && o.X == x0 && o.Y == y0 && !o.EstBebe(_maintenant)) return;
        if (alea.Reel() >= Math.Min(1, (0.4 + 0.6 * h.Personnalite.Prudence) * (SeSouvientDUnIncendie(h) ? 1.3 : 1))) return;
        if (!PorteLeFeu(h) && FeuPrecieux(h, tous) >= 0.5)
        {
            // Un feu que personne ne sait refaire : on l'emporte (une branche y prend feu), sinon on le laisse.
            if (!(h.Sait(Effet.Brandon) && Physique.Disponibles(carte, x0, y0, false, false).Contains(Matiere.Branche))) return;
            Prendre(h, new Objet(Matiere.Brandon, 1, 1));
        }
        Tour.Differer(() => Installations.Eteindre(x0, y0));
        Retenir(h, Geste.Eteindre, Matiere.Aucune, Matiere.Feu, Effet.EteindreEnPartant, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
    }

    /// <summary>
    /// Il n'a plus de feu, ne sait pas le refaire, et le soir ou le froid vient : il va en demander là où monte la fumée d'un feu
    /// de camp, chez des gens qui ne lui sont pas hostiles. Une fois arrivé, il demande ; on lui donne une braise, ou non.
    /// Vrai s'il est en route ou s'il a demandé.
    /// </summary>
    private bool ChercherLeFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!h.Sait(Effet.Foyer) || PorteLeFeu(h) || FoyerProche(h.X, h.Y) || Installations.BraiseEn(h.X, h.Y) is not null) return false;
        if (h.Savoirs.Exists(s => s.Effet == Effet.Flamme && s.Confiance >= 0.3 && APortee(h, s.A) && APortee(h, s.B))) return false;
        var heure = Calendrier.Decomposer(_maintenant).Heure;
        var besoin = h.Froid > 15 || _temperature < 14 || heure >= 15 && heure <= 19;
        var enRoute = h.Activite == Activite.ChercherLeFeu && h.CibleX >= 0;
        if (!besoin && !enRoute) return false;
        if (!enRoute)
        {
            if (h.FeuDemandeLe > 0 && _maintenant - h.FeuDemandeLe < 3 * Calendrier.SecondesParJour) return false;
            if (alea.Reel() >= 0.05 * (0.5 + h.Personnalite.Conscience)) return false;
        }
        // La fumée d'un feu de camp se voit de loin, bien plus loin que ce qu'on distingue d'ordinaire.
        var rayon = 3 * RayonPerception(h);
        Foyer? cible = null;
        Humain? hote = null;
        var meilleure = int.MaxValue;
        foreach (var f in Installations.Foyers)
        {
            var d = Distance(f.X, f.Y, h.X, h.Y);
            if (d > rayon || d >= meilleure) continue;
            if (Hote(h, f.X, f.Y, tous) is not { } o) continue;
            (cible, hote, meilleure) = (f, o, d);
        }
        if (cible is null || hote is null) return false;
        _autre = hote;
        if (meilleure <= 1)
        {
            h.FeuDemandeLe = _maintenant;
            h.Activite = Activite.ChercherLeFeu;
            h.CibleX = h.CibleY = -1;
            var (ih, io, le) = (h.Id, hote.Id, _maintenant);
            Tour.Differer(tous, t =>
            {
                if (t[ih - 1].EstVivant && t[io - 1].EstVivant) DemanderLeFeu(t[ih - 1], t[io - 1], le);
            });
            return true;
        }
        if (!enRoute) h.FeuDemandeLe = _maintenant;
        Avancer(h, cible.X, cible.Y, Activite.ChercherLeFeu);
        return h.CibleX >= 0;
    }

    /// <summary>Quelqu'un d'éveillé près de ce feu, à qui l'on peut demander : pas un ennemi, ni d'un groupe en guerre avec le sien.</summary>
    private Humain? Hote(Humain h, int x, int y, IReadOnlyList<Humain> tous)
    {
        Humain? hote = null;
        var mieux = double.MinValue;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.Id == h.Id || o.EstEnfant(_maintenant) || o.Activite == Activite.Dormir || Distance(o.X, o.Y, x, y) > 1) continue;
            var affection = o.AffectionPour(h.Id);
            if (affection < -30) continue;
            if (o.CommunauteId != 0 && h.CommunauteId != 0 && o.CommunauteId != h.CommunauteId && Social is { } social
                && social.RelationEntre(o.CommunauteId, h.CommunauteId) is EtatRelation.Hostiles or EtatRelation.EnGuerre) continue;
            if (affection <= mieux) continue;
            (hote, mieux) = (o, affection);
        }
        return hote;
    }

    /// <summary>
    /// Il demande le feu à <paramref name="hote"/>. Donner une braise ne coûte presque rien, mais on la refuse à qui l'on n'aime
    /// pas, et un cœur froid la refuse plus souvent. Le don crée de la gratitude, et un lien entre deux groupes.
    /// </summary>
    internal void DemanderLeFeu(Humain h, Humain hote, long maintenant)
    {
        var p = hote.Personnalite;
        var generosite = (0.5 * p.Agreabilite + 0.25 * p.Moral.Compassion + 0.25 * p.Moral.Honnetete) * (1 - 0.8 * Psyche.Froideur(p));
        var memeGroupe = h.CommunauteId != 0 && h.CommunauteId == hote.CommunauteId;
        var accord = Math.Clamp(0.35 + 0.6 * generosite + hote.AffectionPour(h.Id) / 100 + (memeGroupe ? 0.2 : 0), 0.02, 0.98);
        var etranger = h.CommunauteId != hote.CommunauteId && h.CommunauteId != 0 && hote.CommunauteId != 0;
        if (alea.Reel() >= accord)
        {
            var refus = Noter(maintenant, TypeEvenement.Conflit, hote, h, $"{h.Nom} vient demander le feu à {hote.Nom}, qui le lui refuse.");
            Social?.ChangerAffection(h, hote, -5, maintenant);
            Jardin.Confier(h, TypeIntime.Opinion, hote.Id, new Recit("js.feu.refuse", ("x", hote)), 25, refus, maintenant, $"opinion:{hote.Id}");
            return;
        }
        // Qui sait faire une mèche qui couve la donne : elle tiendra jusqu'au camp ; sinon, une branche qui brûle.
        var meche = hote.Savoirs.Exists(s => s.Effet == Effet.PorteFeu && s.Confiance >= 0.4);
        Prendre(h, meche ? new Objet(Matiere.PorteFeu, 0.6, 1) : new Objet(Matiere.Brandon, 1, 1));
        var don = Noter(maintenant, TypeEvenement.Entraide, hote, h,
            $"{h.Nom} est venu{E(h)} demander le feu{(etranger ? " chez des voisins" : "")} : {hote.Nom} lui donne {(meche ? "une mèche qui couve" : "une branche qui brûle")}.",
            significatif: etranger, karma: Regard.Partage);
        VieSociale.Gratitude(hote, h, etranger ? 8 : 4);
        Jardin.Confier(h, TypeIntime.Opinion, hote.Id, new Recit("js.feu.donne", ("x", hote)), 30, don, maintenant, $"opinion:{hote.Id}");
        h.Activite = Activite.Repos;
    }
}
