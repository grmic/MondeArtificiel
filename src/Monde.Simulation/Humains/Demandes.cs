using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les gestes de tous les jours que l'Entité peut demander (0.42.8) : s'asseoir, prier, dormir, manger, boire, aller chercher à
/// manger, aller vers quelqu'un, veiller le feu, parler à quelqu'un, lui montrer un geste, chanter, se cacher, jeûner. Ils passent
/// par les mêmes règles que les autres paroles (comprendre, croire, vouloir) ; ce qu'il ne sait pas faire, il ne le peut pas.
/// <para>
/// Une fois décidé, il commence tout de suite (sauf en rêve : à son réveil), et tient son geste le temps que décident son caractère
/// et son corps : on persévère davantage quand on est consciencieux et endurant (Roberts et al., 2005), et un besoin pressant
/// (soif, faim, danger) l'interrompt, comme toute autre activité.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Ce que coûte un geste demandé, ce qu'il rejoint de ses envies du moment, la raison d'un refus, et si l'on peut n'y obéir
    /// qu'à moitié. Le coût 1 dit qu'il ne le peut pas.
    /// </summary>
    private (double Cout, double Envie, Recit? Refus, bool Moitie) PeserDemande(Humain h, Parole parole, Humain? x, IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var aff = x is null ? 0 : h.AffectionPour(x.Id) / 100;
        var (faim, soif, fatigue) = (h.Faim / 100, h.Soif / 100, h.Fatigue / 100);
        // Ce qu'il est en train de faire et qu'on ne quitte pas pour rien : fuir, combattre, veiller un petit.
        var occupe = h.Activite switch
        {
            Activite.Fuir or Activite.Raid or Activite.Nager or Activite.Naviguer => 0.6,
            Activite.VeillerEnfant or Activite.Allaiter or Activite.Soigner => 0.25,
            Activite.Chasser or Activite.Pecher => 0.15,
            _ => 0,
        };
        // L'envie d'agir et de bouger : le curieux qui aime la nouveauté tient mal en place (Cloninger : recherche de nouveauté,
        // ici tirée de l'ouverture et du peu de conscience).
        var nouveaute = Math.Clamp(0.6 * p.Ouverture + 0.4 * (1 - p.Conscience), 0, 1);
        var timide = (1 - p.Extraversion) * (0.5 + p.Nevrosisme);
        switch (parole)
        {
            case Parole.AssiedsToi:
                return (Math.Clamp(occupe + 0.25 * nouveaute * (1 - fatigue), 0, 1), 0.7 * fatigue + 0.2 * h.Chagrin / 100 - 0.1,
                    new Recit("dem.refus.assis", ("h", h)), true);
            case Parole.Prie:
            {
                // Prier à quoi bon, pense l'esprit analytique qui croit peu ; le fervent, le contemplatif et l'éprouvé y vont d'eux-mêmes.
                var detresse = Math.Max(Math.Max(faim, soif), Math.Max(h.Chagrin / 100, h.Peur / 100));
                return (Math.Clamp(occupe + 0.4 * (1 - h.Croyance / 100) * Croyances.Analytique(h), 0, 1),
                    0.35 * p.Transcendance + 0.3 * h.Ferveur + 0.25 * detresse - 0.15, new Recit("dem.refus.prier", ("h", h)), true);
            }
            case Parole.Dors:
                return (Math.Clamp(occupe + (fatigue < 0.2 ? 0.5 : 0.1) + 0.3 * h.Peur / 100 * p.Nevrosisme, 0, 1), fatigue - 0.3,
                    new Recit(fatigue < 0.2 ? "dem.refus.sommeil" : "dem.refus.peurDormir", ("h", h)), false);
            case Parole.Mange:
                return (Math.Clamp(occupe + (faim < 0.15 ? 0.6 : 0), 0, 1), faim - 0.2, new Recit("dem.refus.pasFaim", ("h", h)), false);
            case Parole.Bois:
                return (Math.Clamp(occupe + (soif < 0.15 ? 0.5 : 0), 0, 1), soif - 0.2, new Recit("dem.refus.pasSoif", ("h", h)), false);
            case Parole.VaChercherAManger:
            {
                // Pour lui, et pour les petits qu'il nourrit.
                var petits = tous.Count(o => o.EstVivant && (o.MereId == h.Id || o.PereId == h.Id) && o.EstPetitEnfant(maintenant));
                var nuit = !Calendrier.Decomposer(maintenant).EstJour;
                return (Math.Clamp(occupe + 0.4 * fatigue * (1.2 - p.Endurance) + (nuit ? 0.3 * (0.5 + p.Prudence) : 0), 0, 1),
                    0.5 * faim + 0.15 * Math.Min(3, petits) * p.Attachement - 0.05, new Recit(nuit ? "dem.refus.nuit" : "dem.refus.fatigue", ("h", h)), true);
            }
            case Parole.VaVers when x is not null:
                return (Math.Clamp(occupe + 0.6 * Math.Max(0, -aff) + 0.2 * fatigue, 0, 1), 0.5 * aff, new Recit("ent.refus.estime", ("h", h), ("x", x)), false);
            case Parole.VeilleLeFeu:
                if (Installations.FoyerEn(h.X, h.Y) is null && !h.Sait(Effet.Foyer)) return (1, 0, new Recit("dem.refus.pasDeFeu", ("h", h)), false);
                return (Math.Clamp(occupe + (Installations.FoyerEn(h.X, h.Y) is null ? 0.5 : 0) + 0.2 * nouveaute, 0, 1), 0.3 * h.Froid / 100 + 0.2 * p.Conscience - 0.05,
                    new Recit("dem.refus.pasDeFeuIci", ("h", h)), true);
            case Parole.ParleA when x is not null:
                return (Math.Clamp(occupe + 0.35 * timide + 0.6 * Math.Max(0, -aff), 0, 1), 0.3 * p.Extraversion + 0.4 * Math.Max(0, aff) - 0.1,
                    Math.Max(0, -aff) > 0.2 ? new Recit("ent.refus.estime", ("h", h), ("x", x)) : new Recit("dem.refus.timide", ("h", h)), true);
            case Parole.Enseigne when x is not null:
            {
                var lecon = ChoisirLecon(h, x);
                if (lecon is null || h.AgeEnAnnees(maintenant) < 9) return (1, 0, new Recit("dem.refus.rienAMontrer", ("h", h), ("x", x)), false);
                return (Math.Clamp(occupe + 0.5 * Math.Max(0, -aff) + 0.2 * (1 - p.Conscience), 0, 1),
                    0.25 * p.Agreabilite + 0.3 * Math.Max(0, aff) + 0.2 * p.Moral.EmpathieCognitive - 0.1, new Recit("dem.refus.patience", ("h", h)), true);
            }
            case Parole.Chante:
                // Chanter devant les autres : la gêne retient le timide ; la joie, le goût des arts et l'élan y poussent.
                return (Math.Clamp(occupe + 0.45 * timide + 0.3 * h.Chagrin / 100, 0, 1),
                    0.35 * Humains.Arts.Aptitude(h) + 0.25 * p.Extraversion + 0.2 * (h.Humeur - 50) / 50, new Recit("dem.refus.chanter", ("h", h)), true);
            case Parole.CacheToi:
                return (Math.Clamp(0.5 * occupe + 0.2 * nouveaute * (1 - h.Peur / 100), 0, 1), 0.6 * h.Peur / 100 + 0.2 * p.Prudence - 0.1,
                    new Recit("dem.refus.cacher", ("h", h)), false);
            case Parole.Jeune:
                // Le contemplatif fervent jeûne volontiers ; la faim déjà là, et la gourmandise du plaisir immédiat, retiennent.
                return (Math.Clamp(0.15 + 0.8 * faim + 0.2 * nouveaute + (h.EstEnceinte ? 0.4 : 0), 0, 1), 0.35 * p.Transcendance + 0.25 * h.Ferveur - 0.1,
                    new Recit(faim > 0.5 ? "dem.refus.affame" : "dem.refus.jeuner", ("h", h)), true);
            case Parole.Transmets:
            {
                // Il ne peut transmettre que ce qu'il sait. Raconter aux siens ce que l'invisible a dit : la gêne retient le timide
                // et celui qui en doute ; l'expansif, le fervent et celui qui y croit y vont volontiers.
                if (h.Revelations.Count == 0) return (1, 0, new Recit("dem.refus.rienATransmettre", ("h", h)), false);
                var croit = h.Revelations.Exists(c => c.Croit);
                return (Math.Clamp(occupe + 0.35 * timide + (croit ? 0 : 0.3), 0, 1),
                    0.3 * p.Extraversion + 0.25 * p.Transcendance + 0.2 * h.Ferveur + (croit ? 0.1 : -0.1) - 0.1,
                    new Recit(croit ? "dem.refus.transmettre" : "dem.refus.transmettreDoute", ("h", h)), true);
            }
            default:
                return (0, 0, null, false);
        }
    }

    /// <summary>Combien de temps il tient un geste demandé (en secondes du monde) : selon le geste, sa persévérance et un peu de hasard.</summary>
    private long DureeDemande(Humain h, Parole parole, bool moitie)
    {
        var p = h.Personnalite;
        // Persévérance (Cloninger), tirée ici de la conscience et de l'endurance.
        var perseverance = 0.6 * p.Conscience + 0.4 * p.Endurance;
        var minutes = parole switch
        {
            Parole.AssiedsToi => 20 + 70 * perseverance,
            Parole.Prie => 10 + 30 * (0.4 * perseverance + 0.6 * Math.Max(p.Transcendance, h.Ferveur)),
            Parole.Chante => 10 + 30 * (0.5 * perseverance + 0.5 * Humains.Arts.Aptitude(h)),
            Parole.VeilleLeFeu => 60 + 180 * perseverance,
            Parole.CacheToi => 60 + 240 * p.Prudence,
            Parole.Mange or Parole.Bois => 60 + 60 * perseverance,
            Parole.VaChercherAManger => 120 + 240 * perseverance,
            Parole.Jeune => 24 * 60 * (0.5 + 2 * perseverance * (0.5 + 0.5 * Math.Max(p.Transcendance, h.Ferveur))),
            _ => 30,
        };
        minutes *= (0.7 + 0.6 * alea.Reel()) * (moitie ? 0.5 : 1);
        return (long)Math.Max(MondeSimule.SecondesParPas, minutes * 60);
    }

    /// <summary>
    /// Il commence le geste demandé <paramref name="k"/> : il s'y met (posture, prière), le journal le dit, et le temps qu'il le
    /// tiendra est fixé. Vrai s'il y consacre ce pas.
    /// </summary>
    private bool Commencer(Humain h, int k, Humain? x, IReadOnlyList<Humain> tous, long maintenant)
    {
        var i = h.Impulsions[k];
        var moitie = i.Reponse == Reponse.AMoitie;
        var duree = DureeDemande(h, i.Parole, moitie);
        // Param garde la durée (minutes) : un geste commencé a un Param positif.
        h.Impulsions[k] = i = i with { Param = (int)Math.Max(1, duree / 60), Jusqua = maintenant + duree, AgirLe = maintenant };
        string cle;
        switch (i.Parole)
        {
            case Parole.Prie when !moitie:
                PrierSurParole(h, tous, maintenant, i.EvenementId);
                cle = "dem.fait.prie";
                break;
            case Parole.Prie:
                cle = "dem.fait.prieMoitie";
                break;
            case Parole.Chante:
                ChanterSurParole(h, tous, moitie);
                cle = moitie ? "dem.fait.fredonne" : "dem.fait.chante";
                break;
            case Parole.VeilleLeFeu:
                if (Installations.FoyerEn(h.X, h.Y) is not null && h.Sait(Effet.Foyer) && APortee(h, Matiere.BoisSec))
                    Faire(h, Geste.Entasser, Matiere.BoisSec, Matiere.Feu, OrigineSavoir.Essai, tous);
                cle = "dem.fait.veille";
                break;
            case Parole.Dors:
                cle = h.Fatigue >= 20 ? "dem.fait.dort" : "dem.fait.allonge";
                break;
            case Parole.Jeune:
                cle = "dem.fait.jeune";
                break;
            default:
                cle = $"dem.fait.{i.Parole}";
                break;
        }
        NoterEntite(maintenant, TypeEvenement.Parole, h, x, new Recit(cle, ("h", h), ("mode", Recits.Recit.Seul($"modeParole.{i.Mode}"))), i.EvenementId);
        return Tenir(h, i, maintenant);
    }

    /// <summary>Il tient le geste commencé ce pas-ci. Vrai s'il y consacre ce pas.</summary>
    private bool Tenir(Humain h, Impulsion i, long maintenant)
    {
        var moitie = i.Reponse == Reponse.AMoitie;
        switch (i.Parole)
        {
            case Parole.AssiedsToi:
                h.Activite = Activite.Repos;
                break;
            case Parole.Prie:
                h.Activite = moitie ? Activite.Repos : Activite.Prier;
                break;
            case Parole.Chante:
                h.Activite = Activite.Chanter;
                break;
            case Parole.VeilleLeFeu:
                if (Installations.FoyerEn(h.X, h.Y) is null) return false;
                h.Activite = Activite.Veiller;
                break;
            case Parole.Dors:
                h.Activite = h.Fatigue >= 20 ? Activite.Dormir : Activite.Repos;
                break;
            case Parole.CacheToi:
                if (h.Activite != Activite.SAbriter)
                {
                    AllerSAbriter(h, null, maintenant);
                    return true;
                }
                h.Activite = Activite.SAbriter;
                break;
            case Parole.Mange or Parole.VaChercherAManger:
                // On va chercher, puis l'on rapporte : il suit ses propres règles de cueillette, de chasse et de pêche.
                if (i.Parole == Parole.Mange && h.Faim < 5) return false;
                AllerManger(h);
                return true;
            case Parole.Bois:
                if (h.Soif < 5) return false;
                AllerBoire(h);
                return true;
            case Parole.Jeune:
                // Rien à faire : la faim ne le fait plus partir chercher à manger (voir Jeuner).
                return false;
            default:
                return false;
        }
        h.CibleX = h.CibleY = -1;
        return true;
    }

    /// <summary>
    /// Le geste demandé <paramref name="k"/>, ce pas-ci : il le commence, le tient, ou le finit quand le temps est passé. Vrai s'il
    /// y consacre ce pas.
    /// </summary>
    private bool AgirDemande(Humain h, int k, Impulsion i, Humain? x, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Paroles.Occupe(i.Parole) || i.Parole == Parole.Jeune)
        {
            if (i.Param <= 0) return Commencer(h, k, x, tous, maintenant);
            var tient = Tenir(h, i, maintenant);
            // Mangé, bu : c'est fait avant l'heure.
            if (!tient && i.Parole is Parole.Mange or Parole.Bois) Finir(h, k, maintenant);
            return tient;
        }
        if (x is null) return false;
        if (Distance(h.X, h.Y, x.X, x.Y) > 1)
        {
            _autre = x;
            Avancer(h, x.X, x.Y, Activite.RejoindreProche);
            return h.CibleX >= 0;
        }
        var moitie = i.Reponse == Reponse.AMoitie;
        switch (i.Parole)
        {
            case Parole.VaVers:
                h.Activite = Activite.Repos;
                Social?.ChangerAffection(h, x, 2, maintenant);
                Achever(h, k, i, x, new Recit("dem.fait.rejoint", ("h", h), ("x", x)), maintenant);
                return true;
            case Parole.ParleA:
            {
                // Passer un moment ensemble rapproche, plus quand on s'aimait déjà un peu, moins pour qui y va à reculons.
                var gain = (moitie ? 2 : 5) * (0.6 + 0.4 * h.Personnalite.Extraversion);
                Social?.ChangerAffection(h, x, gain, maintenant);
                Social?.ChangerAffection(x, h, gain * (0.5 + 0.5 * x.Personnalite.Agreabilite), maintenant);
                VieSociale.Rejouir(x, 3);
                VieSociale.Rejouir(h, 3);
                h.Activite = Activite.Repos;
                _autre = x;
                Achever(h, k, i, x, new Recit(moitie ? "dem.fait.parlePeu" : "dem.fait.parle", ("h", h), ("x", x)), maintenant);
                return true;
            }
            case Parole.Enseigne:
            {
                if (ChoisirLecon(h, x) is not { } lecon)
                {
                    Achever(h, k, i, x, new Recit("dem.fait.rienAMontrer", ("h", h), ("x", x)), maintenant);
                    return false;
                }
                Achever(h, k, i, x, new Recit("dem.fait.montre", ("h", h), ("x", x)), maintenant);
                Montrer(h, x, lecon, tous);
                return true;
            }
        }
        return false;
    }

    /// <summary>Le geste demandé est fait (une seule ligne au journal).</summary>
    private void Achever(Humain h, int k, Impulsion i, Humain? x, Recit texte, long maintenant)
    {
        h.Impulsions[k] = i with { Reponse = i.Reponse == Reponse.AMoitie ? Reponse.AMoitie : Reponse.Accomplie, Jusqua = Math.Max(i.Jusqua, maintenant), AgirLe = long.MaxValue };
        NoterEntite(maintenant, TypeEvenement.Parole, h, x, texte, i.EvenementId);
        Jardin.Retirer(h, $"entite:{(int)i.Parole}");
    }

    /// <summary>Le temps du geste commencé est passé (ou il a fini avant) : il s'arrête, sans autre ligne au journal.</summary>
    private void Finir(Humain h, int k, long maintenant)
    {
        var i = h.Impulsions[k];
        h.Impulsions[k] = i with { Reponse = i.Reponse == Reponse.AMoitie ? Reponse.AMoitie : Reponse.Accomplie, Jusqua = Math.Min(i.Jusqua, maintenant), AgirLe = long.MaxValue };
        Jardin.Retirer(h, $"entite:{(int)i.Parole}");
    }

    /// <summary>
    /// Il jeûne : la faim ne le fait plus partir chercher à manger, tant qu'elle reste supportable pour lui (plus longtemps s'il est
    /// endurant et fervent). Vrai si sa faim doit être ignorée ce pas-ci.
    /// </summary>
    private bool Jeuner(Humain h, long maintenant)
    {
        if (h.Impulsions.Count == 0) return false;
        var i = h.Consigne(Parole.Jeune, maintenant);
        if (i is null || i.Param <= 0) return false;
        var p = h.Personnalite;
        var limite = 55 + 30 * (0.5 * p.Endurance + 0.5 * Math.Max(p.Transcendance, h.Ferveur)) - (i.Reponse == Reponse.AMoitie ? 15 : 0);
        return h.Faim < limite;
    }

    /// <summary>
    /// Il prie parce que l'Entité le lui a demandé : une vraie prière, qui demande ce dont il a besoin (ou remercie s'il ne manque de
    /// rien), visible parmi les prières et notée au journal.
    /// </summary>
    /// <remarks>
    /// <paramref name="demande"/> (0.42.9) : ce qu'il demande lui-même à l'Entité au cours d'un dialogue, et pour qui
    /// (<paramref name="pour"/>) ; sa détresse est celle de son besoin du moment s'il porte sur la même chose, sinon modérée.
    /// </remarks>
    private long PrierSurParole(Humain h, IReadOnlyList<Humain> tous, long maintenant, long cause, SujetPriere? demande = null, int pour = 0)
    {
        if (Cultes is null) return 0;
        var b = BesoinDePrier(h, tous, maintenant);
        if (demande is { } d && b?.Sujet != d)
            b = (d, pour >= 1 && pour <= tous.Count && pour != h.Id && tous[pour - 1].EstVivant ? pour : 0, 0L, d == SujetPriere.Remerciement ? 0 : 0.5, false);
        var c = h.Croyance / 100;
        var ferveur = Math.Clamp(0.5 * c + 0.4 * Math.Min(1, b?.Detresse ?? 0) + 0.2 * h.Ferveur + 0.1, 0, 1);
        var sujet = b?.Sujet ?? SujetPriere.Remerciement;
        var cible = b?.Cible ?? 0;
        var forte = ferveur >= 0.6 && b is { } v && (v.Vital || v.Detresse >= 1);
        var jusqua = maintenant + (long)(JoursDAttente(sujet) * Patience(h, b?.Vital ?? false) * Calendrier.SecondesParJour);
        Priere Nouvelle(int id, long evenement) => new()
        {
            Id = id, HumainId = h.Id, Le = maintenant, Sujet = sujet, CibleId = cible, Parametre = b?.Param ?? 0, Ferveur = Math.Round(ferveur, 2),
            Forte = forte, ReligionId = h.ReligionId, Jusqua = jusqua, EvenementId = evenement,
        };
        h.PrieLe = maintenant;
        var x = cible > 0 && cible <= tous.Count ? tous[cible - 1] : null;
        var evenement = Noter(maintenant, TypeEvenement.Religion, h, x, TextePriere(h, Nouvelle(0, 0), x), cause, significatif: forte);
        var cultes = Cultes;
        // Son numéro se donne après le pas, dans l'ordre des humains (pas simultané).
        Tour.Differer(() => cultes.Prieres.Add(Nouvelle(cultes.ProchainePriere++, evenement)));
        Confier(h, TypeIntime.Espoir, cible, new Recit("js.priere.entendra", ("qui", InvisibleRecit(h))), 20 + 50 * ferveur, evenement,
            maintenant, "priere");
        return evenement;
    }

    /// <summary>Il chante : sa joie monte, et celle de ceux qui l'entendent sur sa case, d'autant plus qu'il chante bien.</summary>
    private void ChanterSurParole(Humain h, IReadOnlyList<Humain> tous, bool moitie)
    {
        var art = Humains.Arts.Aptitude(h);
        VieSociale.Rejouir(h, (moitie ? 2 : 5) * (0.6 + 0.8 * art));
        if (moitie) return;
        foreach (var o in tous)
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.Activite == Activite.Dormir) continue;
            VieSociale.Rejouir(o, 2 + 4 * art);
        }
    }
}
