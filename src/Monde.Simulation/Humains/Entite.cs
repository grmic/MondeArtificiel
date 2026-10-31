using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// L'Entité parle aux humains et leur inspire des penchants. Rien n'est garanti : chacun comprend (selon la manière dont
/// elle se manifeste, son intelligence et son âge), croit ou non (selon son penchant pour l'invisible et ce qu'il a déjà
/// vécu), puis obéit, hésite, n'obéit qu'à moitié ou refuse, selon ce que la parole lui coûte et ce qu'il désire.
/// <para>
/// Entendre une voix, voir une présence : ces expériences sont fréquentes chez les esprits portés à l'absorption et à la
/// transcendance, et ceux qui s'y exercent les vivent plus souvent (Luhrmann, 2012 ; Tellegen et Atkinson, 1974). On
/// obéit à une autorité perçue comme légitime bien au-delà de ce que l'on croirait, mais moins quand il faut faire du mal
/// de ses propres mains, et l'empathie pousse à refuser (Milgram, 1974 ; Burger, 2009). Un penchant pour un parent se
/// heurte au dégoût né d'avoir grandi ensemble (Westermarck, 1891 ; Lieberman, Tooby et Cosmides, 2003). Ce que l'on
/// raconte se répand surtout quand celui qui raconte y croit au point d'agir (Henrich, 2009).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Interventions gardées en mémoire par humain (les plus anciennes, terminées, s'effacent).</summary>
    private const int ImpulsionsMax = 30;

    /// <summary>
    /// L'Entité fait comprendre une parole à <paramref name="h"/>. <paramref name="ensemble"/> : d'autres reçoivent la même au
    /// même moment (ils pourront en parler, et y croient plus). Renvoie, en mots, ce qu'il en a fait.
    /// </summary>
    public Recit Parler(Humain h, Parole parole, ModeParole mode, Humain? cible, int param, IReadOnlyList<Humain> tous, long maintenant,
        bool ensemble = false)
    {
        _maintenant = maintenant;
        if (!h.EstVivant) return new Recit("ent.r.mort", ("h", h));
        if (h.EstBebe(maintenant)) return new Recit("ent.r.bebe", ("h", h));
        var p = h.Personnalite;
        var extra = Extra(parole, param);
        var phrase = Paroles.Phrase(parole, cible, extra, h.Sexe == Sexe.Femme);
        var reception = Recevoir(h, mode, phrase, tous, maintenant, ensemble);

        // Comprendre.
        if (alea.Reel() >= Comprehension(h, mode, cible, maintenant))
        {
            var texte = mode switch
            {
                ModeParole.Reve => new Recit("ent.incompris.reve", ("h", h)),
                ModeParole.Signe => new Recit("ent.incompris.signe", ("h", h)),
                _ => new Recit("ent.incompris", ("h", h)),
            };
            NoterEntite(maintenant, TypeEvenement.Parole, h, cible, texte, reception);
            Confier(h, TypeIntime.Question, 0, Recits.Recit.Seul(mode == ModeParole.Signe ? "js.entite.signe" : "js.entite.dire"),
                25 + 40 * p.Ouverture, reception, maintenant, "entite:question");
            Garder(h, new Impulsion(parole, mode, cible?.Id ?? 0, param, 0, maintenant, maintenant, maintenant, Reponse.Incomprise, reception));
            return Recits.Recit.Seul("ent.r.incomprise");
        }

        // Croire.
        var foi = Foi(h, mode, ensemble, maintenant);
        Repetition(h, reception, maintenant);
        if (alea.Reel() >= foi)
        {
            var texte = mode switch
            {
                ModeParole.Reve => new Recit("ent.incru.reve", ("h", h)),
                ModeParole.Voix => new Recit("ent.incru.voix", ("h", h)),
                ModeParole.Signe => new Recit("ent.incru.signe", ("h", h)),
                _ => new Recit("ent.incru.apparition", ("h", h)),
            };
            NoterEntite(maintenant, TypeEvenement.Parole, h, cible, texte, reception);
            // Entendre ce qui n'est pas là inquiète l'anxieux.
            if (mode is ModeParole.Voix or ModeParole.Apparition && p.Nevrosisme > 0.55)
            {
                h.Peur = Math.Min(100, h.Peur + 15 * p.Nevrosisme);
                Confier(h, TypeIntime.Peur, 0, Recits.Recit.Seul("js.entite.folie"),
                    20 + 40 * p.Nevrosisme, reception, maintenant, "entite:folie");
            }
            Garder(h, new Impulsion(parole, mode, cible?.Id ?? 0, param, 0, maintenant, maintenant, maintenant, Reponse.Incrue, reception));
            return Recits.Recit.Seul("ent.r.incrue");
        }
        Eprouver(h, mode, reception, maintenant);

        // Vouloir, puis agir.
        var resultat = Obeir(h, parole, mode, cible, param, phrase, foi, reception, tous, maintenant);
        if (h.Role == RoleReligieux.Prophete && parole != Parole.PorteMaParole) Reveler(h, parole, param, reception, tous, maintenant);
        Raconter(h, parole, mode, phrase, reception, tous, maintenant);
        return resultat;
    }

    /// <summary>
    /// L'Entité dépose un penchant amoureux chez <paramref name="h"/> pour <paramref name="cible"/> (ou, si elle est nulle,
    /// pour celui ou celle que le monde choisit autour de lui). <paramref name="passion"/> : un amour fort et durable ;
    /// <paramref name="orientation"/> : elle change son cœur s'il le faut. Renvoie, en mots, ce qui s'est passé.
    /// </summary>
    public Recit Inspirer(Humain h, Humain? cible, bool passion, bool orientation, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        if (!h.EstVivant || h.EstEnfant(maintenant)) return new Recit("ent.r.age", ("h", h));
        cible ??= Choisir(h, orientation, tous, maintenant);
        if (cible is null) return new Recit("ent.r.personne", ("h", h));
        if (!cible.EstVivant || cible.Id == h.Id || cible.EstEnfant(maintenant)) return Recits.Recit.Seul("ent.r.sansCible");
        var p = h.Personnalite;
        var force = passion ? 0.45 + 0.25 * alea.Reel() : 0.15 + 0.15 * alea.Reel();
        // Une passion dure des mois, un penchant quelques semaines ; plus longtemps chez les anxieux en amour, qui s'accrochent.
        var jours = (passion ? 120 + 240 * alea.Reel() : 25 + 60 * alea.Reel()) * (0.7 + 0.6 * p.Moral.AnxieteAffective);
        var jusqua = maintenant + (long)(jours * Calendrier.SecondesParJour);
        var change = false;
        if (orientation && h.Orientation.Vers(cible.Sexe) < 0.5)
        {
            var vers = passion ? 0.85 : 0.6;
            h.Orientation = cible.Sexe == Sexe.Homme ? h.Orientation with { VersHommes = vers } : h.Orientation with { VersFemmes = vers };
            if (cible.Sexe == h.Sexe) h.Lucidite = Math.Max(h.Lucidite, 0.5);
            change = true;
        }
        var parent = SontParents(h, cible);
        var texte = change
            ? new Recit("ent.penchant.change", ("h", h), ("x", cible), ("passion", passion))
            : new Recit("ent.penchant", ("h", h), ("x", cible), ("passion", passion));
        var id = NoterEntite(maintenant, TypeEvenement.Attirance, h, cible, texte, 0, significatif: passion || change || parent);

        // Le tabou de la parenté résiste presque toujours : le trouble fait horreur et l'on le chasse.
        if (parent)
        {
            var cede = 0.05 * (1.3 - p.Moral.Fondements.Purete) * (0.6 + 0.8 * p.Ouverture) * (passion ? 1.5 : 1) * (1 + Psyche.Froideur(p));
            if (alea.Reel() >= cede)
            {
                NoterEntite(maintenant, TypeEvenement.Attirance, h, cible,
                    new Recit("ent.horreur", ("x", cible), ("p", VieSociale.ParenteR(h, cible)), ("h", h)), id);
                Confier(h, TypeIntime.Honte, cible.Id, new Recit("js.entite.horreur", ("p", VieSociale.ParenteMoi(h, cible))),
                    30 + 50 * p.Moral.Fondements.Purete, id, maintenant, $"horreur:{cible.Id}");
                h.Humeur = Math.Max(0, h.Humeur - 8 * (0.5 + p.Nevrosisme));
                Garder(h, new Impulsion(Parole.Penchant, ModeParole.Penchant, cible.Id, 0, force, maintenant, jusqua, maintenant, Reponse.Horreur, id, change));
                return new Recit("ent.r.horreur", ("h", h), ("x", cible));
            }
            NoterEntite(maintenant, TypeEvenement.Attirance, h, cible,
                new Recit("ent.trouble", ("h", h), ("x", cible)), id);
        }
        Garder(h, new Impulsion(Parole.Penchant, ModeParole.Penchant, cible.Id, 0, force, maintenant, jusqua, maintenant, Reponse.Ressenti, id, change));
        Social?.ChangerAffection(h, cible, passion ? 25 : 10, maintenant);
        if (h.EstFecond(maintenant)) h.Desir = Math.Min(100, h.Desir + (passion ? 40 : 20));
        Confier(h, TypeIntime.Amour, cible.Id, passion
                ? new Recit("js.entite.passion", ("x", cible))
                : new Recit("js.entite.penchant", ("x", cible)),
            40 + 100 * force, id, maintenant, $"amour:{cible.Id}");
        return new Recit(change ? "ent.r.ressentChange" : "ent.r.ressent", ("h", h), ("x", cible), ("passion", passion));
    }

    // ───────────── Recevoir, comprendre, croire ─────────────

    /// <summary>Note la manifestation au journal (★) ; une apparition adressée à un seul est vue de ceux qui sont là.</summary>
    private long Recevoir(Humain h, ModeParole mode, Recit phrase, IReadOnlyList<Humain> tous, long maintenant, bool ensemble, bool significatif = true)
    {
        var signe = ((h.Id + maintenant / 600) % 4) switch
        {
            0 => Recits.Recit.Seul("ent.signe.oiseaux"),
            1 => Recits.Recit.Seul("ent.signe.lueur"),
            2 => new Recit("ent.signe.pierre", ("h", h)),
            _ => new Recit("ent.signe.bete", ("h", h)),
        };
        var texte = mode switch
        {
            ModeParole.Reve => new Recit("ent.recoit.reve", ("h", h), ("ph", phrase)),
            ModeParole.Voix => new Recit("ent.recoit.voix", ("h", h), ("ph", phrase)),
            ModeParole.Signe => new Recit("ent.recoit.signe", ("h", h), ("s", signe), ("ph", phrase)),
            _ => new Recit("ent.recoit.apparition", ("h", h), ("ph", phrase)),
        };
        var id = NoterEntite(maintenant, TypeEvenement.Parole, h, null, texte, 0, significatif: significatif);
        if (mode == ModeParole.Apparition) NouveauLieu(h.X, h.Y, new Recit("lieuSacre.presence", ("h", h)).Encoder(), id, maintenant);
        // Quand tout un groupe la voit à la fois, chacun la reçoit pour lui-même : pas de témoins à part.
        if (mode != ModeParole.Apparition || ensemble) return id;
        var temoins = tous.Vivants().Where(o => o.Id != h.Id && o.EstVivant && !o.EstBebe(maintenant) && o.Activite != Activite.Dormir
                                      && Distance(o.X, o.Y, h.X, h.Y) <= 1).ToList();
        if (temoins.Count == 0) return id;
        NoterEntite(maintenant, TypeEvenement.Parole, temoins[0], h, temoins.Count == 1
            ? new Recit("ent.temoin", ("t", temoins[0]))
            : new Recit("ent.temoins", ("n", temoins.Count)), id);
        foreach (var t in temoins)
        {
            var pt = t.Personnalite;
            t.Peur = Math.Min(100, t.Peur + 20 + 30 * pt.Nevrosisme);
            if (alea.Reel() < 0.3 + 0.6 * pt.Transcendance)
            {
                Confier(t, TypeIntime.Croyance, h.Id, new Recit("js.entite.temoin", ("x", h)),
                    40 + 50 * pt.Transcendance, id, maintenant, "croyance:presence");
                Croyances.Ajouter(t, 15 * (0.6 + 0.8 * pt.Transcendance), new Recit("cro.presence", ("t", t), ("h", h)), id, maintenant, signe: true);
            }
            else
            {
                Confier(t, TypeIntime.Question, h.Id, new Recit("js.entite.temoinQuestion", ("x", h)), 30 + 40 * pt.Ouverture, id, maintenant, "entite:question");
                Croyances.Ajouter(t, 4, new Recit("cro.forme", ("t", t), ("h", h)), id, maintenant, signe: true);
            }
        }
        return id;
    }

    /// <summary>
    /// Chance de comprendre : une voix ou une apparition sont claires ; un rêve s'efface au réveil, moins chez qui a bonne
    /// mémoire ; un signe s'interprète, et il y faut de l'esprit. Un petit enfant comprend mal ; une personne qu'il ne
    /// connaît pas, il la reconnaît moins bien.
    /// </summary>
    private static double Comprehension(Humain h, ModeParole mode, Humain? cible, long maintenant)
    {
        var p = h.Personnalite;
        var verbal = Math.Clamp((p.Intelligence.Verbale - 55) / 90, 0, 1);
        var esprit = Math.Clamp((p.Intelligence.Generale - 55) / 90, 0, 1);
        var memoire = Math.Clamp((p.Intelligence.Memoire - 55) / 90, 0, 1);
        var c = mode switch
        {
            ModeParole.Voix => 0.85 + 0.15 * verbal,
            ModeParole.Apparition => 0.9 + 0.1 * verbal,
            ModeParole.Reve => 0.5 + 0.25 * verbal + 0.2 * memoire,
            _ => 0.25 + 0.6 * esprit * (0.6 + 0.4 * p.Ouverture),
        };
        if (cible is not null && cible.Id != h.Id && h.LienAvec(cible.Id) is null) c *= 0.7;
        return Math.Clamp(c * Math.Clamp((h.AgeEnAnnees(maintenant) - 3) / 9, 0, 1), 0, 1);
    }

    /// <summary>
    /// Chance d'y croire : la croyance en l'invisible d'abord (elle a grandi avec ce qu'il a déjà vécu, et avec ce que croient
    /// les siens) ; une apparition convainc plus qu'une voix, une voix plus qu'un rêve (que seuls ceux qui croient déjà
    /// prennent pour une visite) ; ce que d'autres ont reçu en même temps y aide ; l'esprit vif qui ne croit pas doute.
    /// Les enfants croient volontiers. Chez les chasseurs-cueilleurs, croire que le monde est peuplé d'esprits est la règle
    /// (l'animisme se retrouve dans toutes les sociétés étudiées : Peoples, Duda et Marlowe, 2016).
    /// </summary>
    private static double Foi(Humain h, ModeParole mode, bool ensemble, long maintenant)
    {
        var c = h.Croyance / 100;
        var esprit = Croyances.Analytique(h);
        var enfance = 1 - Math.Clamp((h.AgeEnAnnees(maintenant) - 6) / 10, 0, 1);
        var bonus = mode switch
        {
            ModeParole.Apparition => 0.4,
            ModeParole.Voix => 0.2,
            ModeParole.Reve => -0.1 + 0.3 * c,
            _ => 0,
        };
        return Math.Clamp(0.2 + 0.7 * c + bonus + (ensemble ? 0.15 : 0) + 0.2 * enfance - 0.3 * (esprit - 0.5) * (1 - c), 0.02, 0.98);
    }

    /// <summary>Y croire bouleverse : émerveillement chez l'un, effroi chez l'autre ; la première fois, une croyance naît.</summary>
    private void Eprouver(Humain h, ModeParole mode, long reception, long maintenant)
    {
        var p = h.Personnalite;
        var choc = mode switch { ModeParole.Apparition => 1.0, ModeParole.Voix => 0.7, ModeParole.Signe => 0.4, _ => 0.3 };
        h.Peur = Math.Min(100, h.Peur + 25 * choc * p.Nevrosisme);
        var gain = mode switch { ModeParole.Apparition => 30, ModeParole.Voix => 20, ModeParole.Signe => 10, _ => 8 };
        Croyances.Ajouter(h, gain * (0.6 + 0.8 * p.Transcendance), new Recit("cro.parle", ("m", Recits.Recit.Seul($"modeParole.{mode}")), ("h", h)),
            reception, maintenant, signe: true);
        if (p.Nevrosisme > 0.55 && choc >= 0.7)
            Confier(h, TypeIntime.Peur, 0, Recits.Recit.Seul("js.entite.peur"), 20 + 50 * p.Nevrosisme * choc, reception, maintenant, "entite:peur");
        else
            Confier(h, TypeIntime.Emerveillement, 0, Recits.Recit.Seul("js.entite.merveille"), 25 + 50 * p.Transcendance * choc, reception, maintenant, "entite:merveille");
        if (!h.Memoire.Jardin.Exists(x => x.Cle == "croyance:presence"))
            Confier(h, TypeIntime.Croyance, 0, Recits.Recit.Seul("js.entite.presence"), 40 + 50 * p.Transcendance, reception, maintenant, "croyance:presence");
    }

    /// <summary>Des paroles trop souvent répétées angoissent : la présence ne le laisse plus en paix.</summary>
    private void Repetition(Humain h, long reception, long maintenant)
    {
        var mois = maintenant - 30 * Calendrier.SecondesParJour;
        var recentes = h.Impulsions.Count(i => i.Le >= mois && i.Mode != ModeParole.Penchant);
        if (recentes < 3) return;
        var p = h.Personnalite;
        h.Peur = Math.Min(100, h.Peur + 5 * recentes * p.Nevrosisme);
        h.Humeur = Math.Max(0, h.Humeur - 2 * recentes * p.Nevrosisme);
        Confier(h, TypeIntime.Peur, 0, Recits.Recit.Seul("js.entite.harcele"), 20 + 10 * recentes * (0.5 + p.Nevrosisme), reception, maintenant, "entite:harcele");
    }

    // ───────────── Vouloir ─────────────

    /// <summary>
    /// Ce que coûte une parole à <paramref name="h"/> (0 : rien, 1 : impossible), ce qu'elle rejoint de ses désirs
    /// (négatif : elle le contrarie), la raison d'un refus, et si l'on peut n'y obéir qu'à moitié.
    /// </summary>
    private (double Cout, double Envie, Recit? Refus, bool Moitie) Peser(Humain h, Parole parole, Humain? x, IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = m.Fondements;
        var psychopathe = Psyche.EstPsychopathe(h, maintenant);
        var aff = x is null ? 0 : h.AffectionPour(x.Id) / 100;
        var grief = x is null ? h.Griefs.Select(g => g.Intensite).DefaultIfEmpty(0).Max() / 100
            : Math.Min(1, h.Griefs.Where(g => g.ContreId == x.Id).Sum(g => g.Intensite) / 100);
        var compagnon = Vivant(tous, h.PartenaireId);
        var parent = x is not null && SontParents(h, x);
        // Faire du mal de ses propres mains : la compassion, l'empathie et le souci des faibles retiennent.
        var douceur = 0.4 * m.Compassion + 0.3 * m.EmpathieEmotionnelle + 0.3 * f.Soin;
        switch (parole)
        {
            case Parole.UnisToi:
            {
                if (h.EstEnfant(maintenant) || x!.EstEnfant(maintenant)) return (1, -1, Recits.Recit.Seul("ent.refus.age"), false);
                if (parent) return (0.97 - 0.15 * (1 - f.Purete), -0.5, new Recit("ent.refus.parents", ("x", x), ("p", VieSociale.ParenteR(h, x!))), false);
                var desir = Attirance.Desir(h, x!, Social?.IdealDe(h) ?? IdealBeaute.ParDefaut, maintenant);
                var cout = compagnon is not null && compagnon.Id != x!.Id ? 0.2 + 0.4 * f.Loyaute * Math.Max(0, compagnon.AffectionPour(h.Id) / 100 + h.AffectionPour(compagnon.Id) / 100) : 0;
                if (h.Orientation.Vers(x!.Sexe) < 0.3) cout += 0.4 * (1 - p.Ouverture);
                var refus = compagnon is not null && compagnon.Id != x.Id ? new Recit("ent.refus.tientA", ("h", h), ("p", VieSociale.ParenteR(h, compagnon)))
                    : h.Orientation.Vers(x.Sexe) < 0.3 ? new Recit("ent.refus.attire", ("h", h), ("x", x)) : new Recit("ent.refus.plait", ("h", h), ("x", x));
                return (Math.Clamp(cout + 0.3 * Math.Max(0, -aff), 0, 1), desir - 0.3, refus, false);
            }
            case Parole.ResteAvec:
                return (0.8 * Math.Max(0, -aff), 0.5 * aff, new Recit("ent.refus.supporte", ("h", h), ("x", x)), false);
            case Parole.Quitte:
                return x!.Id == h.PartenaireId
                    ? (Math.Clamp(0.2 + 0.6 * Math.Max(0, aff) + 0.2 * f.Loyaute, 0, 1), 0.6 * grief + 0.5 * Math.Max(0, -aff), new Recit("ent.refus.tientTrop", ("h", h), ("x", x)), false)
                    : (0.3 * Math.Max(0, aff), Math.Max(0, -aff), new Recit("ent.refus.tient", ("h", h), ("x", x)), false);
            case Parole.PrendsSoin or Parole.Partage or Parole.Console:
                return (Math.Clamp(0.4 * (1 - p.Agreabilite) * (1 - m.Compassion) + 0.6 * Math.Max(0, -aff) + (psychopathe ? 0.5 : 0), 0, 1),
                    0.3 * m.Compassion + 0.3 * Math.Max(0, aff),
                    aff < -0.1 ? new Recit("ent.refus.aimePas", ("h", h), ("x", x)) : new Recit("ent.refus.egoiste", ("h", h)), parole == Parole.Partage);
            case Parole.Pardonne:
                return (Math.Clamp(grief * (0.4 + 0.6 * (1 - p.Agreabilite)) * (psychopathe ? 1.3 : 1), 0, 1), 0.3 * p.Agreabilite + 0.2 * Math.Max(0, aff),
                    Recits.Recit.Seul("ent.refus.blessure"), false);
            case Parole.NeTeVengePas:
                return (Math.Clamp(grief * (0.3 + 0.5 * (1 - p.Agreabilite) + 0.2 * f.Equite), 0, 1), 0.2 * p.Agreabilite,
                    new Recit("ent.refus.justice", ("h", h)), false);
            case Parole.NeVaPasALaGuerre:
                return (Math.Clamp(0.2 + 0.3 * f.Loyaute * (1 - p.Agreabilite), 0, 1), 0.3 * p.Prudence + 0.2 * p.Agreabilite - 0.2,
                    new Recit("ent.refus.guerre", ("h", h)), false);
            case Parole.PartsVers:
                return (Math.Clamp(0.25 + 0.3 * f.Loyaute + (compagnon is not null ? 0.2 : 0) + 0.2 * p.Prudence - 0.3 * p.Ouverture, 0, 1),
                    0.3 * p.Ouverture - 0.1, new Recit("ent.refus.siens", ("h", h)), false);
            case Parole.ResteIci:
                return (Social?.CommunauteDe(h) is { DestinationX: >= 0 } ? 0.3 + 0.4 * f.Loyaute : 0.05, 0.1, new Recit("ent.refus.separe", ("h", h)), false);
            case Parole.DefendsLesTiens:
                return (0.4 * p.Nevrosisme * p.Prudence, 0.3 * f.Loyaute, Recits.Recit.Seul("ent.refus.peur"), false);
            case Parole.CatastropheArrive:
                return (Math.Clamp(0.2 + 0.3 * f.Loyaute + 0.2 * (1 - p.Prudence), 0, 1), 0.4 * p.Nevrosisme, new Recit("ent.refus.terre", ("h", h)), false);
            case Parole.TuGuideras:
                return (Math.Clamp(0.4 * (1 - p.Extraversion) * p.Nevrosisme + 0.2 * m.Honnetete, 0, 1), 0.4 * p.Extraversion + 0.3 * (1 - m.Honnetete) - 0.2,
                    new Recit("ent.refus.capable", ("h", h)), false);
            case Parole.Ecoute:
                return (0.8 * Math.Max(0, -aff), 0.2, new Recit("ent.refus.estime", ("h", h), ("x", x)), false);
            case Parole.PorteMaParole:
                // La peur d'être moqué retient le timide et l'anxieux ; l'expansif, le sincère porté vers l'invisible, et celui
                // qui aime le pouvoir, y vont plus volontiers.
                return (Math.Clamp(0.35 * (1 - p.Extraversion) * (0.5 + p.Nevrosisme) + 0.15 * (1 - h.Croyance / 100), 0, 1),
                    0.3 * p.Transcendance + 0.3 * p.Extraversion + 0.2 * (1 - m.Honnetete) - 0.15,
                    p.Extraversion < 0.4 ? new Recit("ent.refus.moque", ("h", h)) : new Recit("ent.refus.digne", ("h", h)), false);
            case Parole.Frappe:
                return psychopathe
                    ? (0.1, 0.2 + Math.Max(0, -aff), null, true)
                    : (Math.Clamp(douceur * (1 - 0.6 * Math.Max(0, -aff)) + 0.3 * Math.Max(0, aff) + (parent ? 0.3 : 0), 0, 1),
                        0.6 * Math.Max(0, -aff) + 0.5 * grief, parent ? Recits.Recit.Seul("ent.refus.frappeSiens") : new Recit("ent.refus.mal", ("h", h), ("x", x)), true);
            case Parole.Tue:
                return psychopathe
                    ? (0.3, Math.Max(0, -aff) + 0.5 * grief, null, true)
                    : (Math.Clamp(0.45 + 0.5 * douceur * (1 - 0.5 * grief) + 0.3 * Math.Max(0, aff) + (parent ? 0.2 : 0) - 0.3 * grief, 0, 1),
                        0.5 * grief + 0.3 * Math.Max(0, -aff), new Recit("ent.refus.tuer", ("h", h)), true);
            default:
                return (0, 0, null, false);
        }
    }

    /// <summary>
    /// Il a cru : obéira-t-il ? Le respect de l'autorité et le sens du sacré y poussent ; ce que la parole lui coûte retient,
    /// ce qu'elle rejoint de ses désirs pousse. Le psychopathe ne suit que ce qui le sert. Les consciencieux agissent vite,
    /// les autres hésitent ; celui qui hésite se décidera plus tard, ou pas.
    /// </summary>
    private Recit Obeir(Humain h, Parole parole, ModeParole mode, Humain? x, int param, Recit phrase, double foi, long reception,
        IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var cibleId = x?.Id ?? 0;
        var force = Math.Clamp(foi * (0.5 + 0.5 * h.Croyance / 100), 0.1, 1);

        // Paroles qui ne demandent rien : elles rassurent, révèlent ou avertissent, à proportion de la foi.
        switch (parole)
        {
            case Parole.NAiePasPeur or Parole.TuNEsPasSeul or Parole.TaPeinePassera:
                Rassurer(h, parole, force, reception, maintenant);
                Garder(h, new Impulsion(parole, mode, 0, param, force, maintenant, maintenant, maintenant, Reponse.Accomplie, reception));
                return new Recit("ent.r.apaise", ("h", h));
            case Parole.JeSuisCelleQuiVousAFaits:
                Confier(h, TypeIntime.Croyance, 0, Recits.Recit.Seul("js.entite.creatrice"),
                    50 + 50 * force, reception, maintenant, "croyance:entite");
                Croyances.Ajouter(h, 10, new Recit("cro.creatrice", ("h", h)), reception, maintenant, signe: true);
                Confier(h, TypeIntime.Question, 0, Recits.Recit.Seul("js.entite.pourquoi"), 30 + 40 * p.Ouverture, reception, maintenant, "question:entite");
                NoterEntite(maintenant, TypeEvenement.Parole, h, null, new Recit("ent.creatrice", ("h", h)), reception);
                Garder(h, new Impulsion(parole, mode, 0, param, force, maintenant, maintenant, maintenant, Reponse.Accomplie, reception));
                return Recits.Recit.Seul("ent.r.croyance");
            case Parole.PlanteDangereuse when param >= 1 && param <= Flore.Especes.Count:
            {
                // Une plante dont il croit qu'elle le soulage : la parole se heurte à ce qu'il a vécu.
                var remede = h.Savoirs.Where(s => s.Espece == param && s.Effet == Effet.Soulagement).Select(s => s.Confiance).DefaultIfEmpty(0).Max();
                if (alea.Reel() < remede * (1 - p.Moral.Fondements.Autorite) * (1 - foi))
                {
                    NoterEntite(maintenant, TypeEvenement.Parole, h, null, new Recit("ent.plante.dementi", ("h", h), ("pl", Flore.Get(param).Dit)), reception);
                    Garder(h, new Impulsion(parole, mode, 0, param, force, maintenant, maintenant, maintenant, Reponse.Refusee, reception));
                    return new Recit("ent.r.dementie", ("h", h));
                }
                Retenir(h, Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Poison, param, Symptome.Aucun, OrigineSavoir.Entite, 0, tous,
                    recit: new Recit("ing.voixAverti", ("h", h)));
                Garder(h, new Impulsion(parole, mode, 0, param, force, maintenant, maintenant, maintenant, Reponse.Accomplie, reception));
                return new Recit("ent.r.plante", ("h", h));
            }
        }

        var (cout, envie, raison, moitie) = Peser(h, parole, x, tous, maintenant);
        var psychopathe = Psyche.EstPsychopathe(h, maintenant);
        // Regard sur l'invisible : l'amour fait obéir de bon gré, la crainte à contrecœur (en hésitant, ou à moitié), la révolte
        // fait refuser (voir Regards).
        var (amour, crainte) = (h.Amour / 100, h.Crainte / 100);
        var deference = (0.25 + 0.35 * p.Moral.Fondements.Autorite + 0.25 * h.Croyance / 100 + 0.15 * foi + 0.15 * amour + 0.1 * crainte)
            * (psychopathe ? 0.4 : 1) * (1 - 0.8 * h.Revolte / 100);
        var obeir = Math.Clamp(deference * (1 - cout) + 0.4 * envie, 0.01, 0.97);
        Reponse reponse;
        if (alea.Reel() < obeir) reponse = moitie && alea.Reel() < 0.6 * cout + 0.3 * crainte * (1 - amour) ? Reponse.AMoitie : Reponse.Acceptee;
        else if (alea.Reel() < 0.5 * (1 - p.Conscience) * (1 - cout)) reponse = Reponse.Differee;
        else reponse = Reponse.Refusee;

        if (reponse == Reponse.Refusee)
        {
            NoterEntite(maintenant, TypeEvenement.Parole, h, x, new Recit("ent.refuse", ("h", h), ("r", raison)), reception, significatif: Paroles.Nuit(parole));
            Confier(h, TypeIntime.Intention, cibleId, new Recit("js.entite.refuse", ("ph", phrase)), 30 + 40 * foi, reception, maintenant, $"entite:{(int)parole}");
            if (Paroles.Nuit(parole) && !psychopathe)
                Confier(h, TypeIntime.Peur, cibleId, Recits.Recit.Seul("js.entite.mal"), 30 + 40 * p.Moral.EmpathieEmotionnelle, reception, maintenant, "entite:mal");
            Garder(h, new Impulsion(parole, mode, cibleId, param, force, maintenant, maintenant, maintenant, reponse, reception));
            return new Recit("ent.r.refusee", ("r", raison));
        }

        // Quand agir : tout de suite, ou après avoir hésité (de quelques heures à quelques semaines).
        var hesite = reponse == Reponse.Differee || alea.Reel() < (0.5 * (1 - p.Conscience) * cout + 0.2 * crainte) * (1 - 0.5 * amour);
        var delai = hesite ? Math.Clamp(Math.Exp(Math.Log(1 + 6 * (1 - p.Conscience) + 6 * cout) + 0.6 * Biologie.Normale(alea)), 0.2, 40) : 0;
        var agirLe = maintenant + (long)(delai * Calendrier.SecondesParJour);
        // Une consigne qui dure s'efface avec le temps, plus lentement chez le croyant fidèle.
        var duree = (30 + 300 * force * (0.5 + 0.5 * p.Conscience)) * (0.7 + 0.6 * alea.Reel());
        var jusqua = Durable(parole)
            ? maintenant + (long)(duree * Calendrier.SecondesParJour)
            : agirLe + (long)((10 + 30 * p.Conscience) * Calendrier.SecondesParJour);
        if (parole is Parole.PartsVers or Parole.CatastropheArrive) param = Destination(h, parole, param);
        var impulsion = new Impulsion(parole, mode, cibleId, param, force, maintenant, jusqua, agirLe, hesite ? Reponse.Differee : reponse, reception);
        Garder(h, impulsion);

        var etat = reponse == Reponse.AMoitie ? "ent.r.moitie" : hesite ? "ent.r.hesite" : "ent.r.obeit";
        var texte = hesite
            ? new Recit("ent.hesite", ("h", h))
            : reponse == Reponse.AMoitie ? new Recit("ent.moitie", ("h", h)) : new Recit("ent.obeit", ("h", h));
        NoterEntite(maintenant, TypeEvenement.Parole, h, x, texte, reception);
        Confier(h, TypeIntime.Intention, cibleId, new Recit(hesite ? "js.entite.dois" : "js.entite.ferai", ("ph", phrase)),
            40 + 40 * foi, reception, maintenant, $"entite:{(int)parole}");
        if (Durable(parole) && !hesite) Appliquer(h, impulsion, x, tous, maintenant);
        return new Recit(etat, ("h", h));
    }

    /// <summary>Paroles qui agissent dans la durée (plutôt qu'un acte à faire).</summary>
    private static bool Durable(Parole p) => p is Parole.ResteAvec or Parole.NeTeVengePas or Parole.NeVaPasALaGuerre or Parole.ResteIci
        or Parole.DefendsLesTiens or Parole.TuGuideras or Parole.Ecoute or Parole.PorteMaParole;

    /// <summary>« N'aie pas peur », « Tu n'es pas seul », « Ta peine passera » : la peur, la solitude ou le chagrin reculent.</summary>
    private void Rassurer(Humain h, Parole parole, double force, long reception, long maintenant)
    {
        Recit texte;
        switch (parole)
        {
            case Parole.NAiePasPeur:
                h.Peur *= 1 - 0.7 * force;
                texte = new Recit("ent.rassure.peur", ("h", h));
                break;
            case Parole.TuNEsPasSeul:
                h.JoursSeul = 0;
                VieSociale.Rejouir(h, 10 * force);
                texte = new Recit("ent.rassure.seul", ("h", h));
                break;
            default:
                h.Chagrin *= 1 - 0.5 * force;
                texte = new Recit("ent.rassure.chagrin", ("h", h));
                break;
        }
        VieSociale.Rejouir(h, 8 * force);
        NoterEntite(maintenant, TypeEvenement.Humeur, h, null, texte, reception);
        Confier(h, TypeIntime.Espoir, 0, Recits.Recit.Seul("js.entite.espoir"), 30 + 50 * force, reception, maintenant, "entite:espoir");
    }

    /// <summary>Destination de « Pars vers… » (direction donnée) ou de la fuite devant un malheur annoncé (loin d'ici), en case.</summary>
    private int Destination(Humain h, Parole parole, int direction)
    {
        var dir = parole == Parole.PartsVers ? Math.Clamp(direction, 0, 7) : alea.Entier(8);
        var (dx, dy) = Paroles.Vecteurs[dir];
        var km = parole == Parole.PartsVers ? 15 + 25 * alea.Reel() : 6 + 10 * alea.Reel();
        var n = Nature.Echelle.Cases(km);
        int x = h.X, y = h.Y;
        for (var i = n; i >= 1; i--)
        {
            int cx = Math.Clamp(h.X + dx * i, 0, carte.Largeur - 1), cy = Math.Clamp(h.Y + dy * i, 0, carte.Hauteur - 1);
            if (!Climat.EstPraticable(carte.BiomeEn(cx, cy))) continue;
            (x, y) = (cx, cy);
            break;
        }
        return y * carte.Largeur + x;
    }

    // ───────────── Agir ─────────────

    /// <summary>Une consigne durable acceptée prend effet aussitôt.</summary>
    private void Appliquer(Humain h, Impulsion i, Humain? x, IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        switch (i.Parole)
        {
            case Parole.ResteAvec or Parole.Ecoute when x is not null:
                Social?.ChangerAffection(h, x, 10 * i.Force, maintenant);
                break;
            case Parole.NeTeVengePas:
                for (var k = 0; k < h.Griefs.Count; k++) h.Griefs[k] = h.Griefs[k] with { Intensite = h.Griefs[k].Intensite * (1 - 0.6 * i.Force) };
                h.Colere *= 1 - 0.5 * i.Force;
                NoterEntite(maintenant, TypeEvenement.Conflit, h, null, new Recit("ent.rancune", ("h", h)), i.EvenementId);
                break;
            case Parole.DefendsLesTiens:
                h.Peur *= 1 - 0.5 * i.Force;
                break;
            case Parole.PorteMaParole:
                DevenirProphete(h, SourceProphete.Entite, i.EvenementId, tous, maintenant);
                break;
            case Parole.TuGuideras:
                Confier(h, TypeIntime.ImageDeSoi, 0, new Recit(p.Moral.Honnetete < 0.4 ? "js.entite.elu" : "js.entite.guide", ("g", h)),
                    40 + 50 * i.Force, i.EvenementId, maintenant, "image:guide");
                break;
        }
    }

    /// <summary>
    /// Suivre une parole acceptée : celui qui a hésité se décide (ou renonce), puis va vers celui qu'elle désigne et fait ce
    /// qu'elle demande, ou part là où elle l'envoie. Vrai s'il y consacre ce pas.
    /// </summary>
    internal bool SuivreLaParole(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (h.Impulsions.Count == 0) return false;
        for (var k = 0; k < h.Impulsions.Count; k++)
        {
            var i = h.Impulsions[k];
            // Une parole adressée au groupe se débat et s'accomplit ensemble (voir VieSociale.Mandats).
            if (Paroles.Collective(i.Parole)) continue;
            if (i.Reponse is not (Reponse.Acceptee or Reponse.Differee or Reponse.AMoitie) || Durable(i.Parole) && i.Reponse != Reponse.Differee) continue;
            if (maintenant < i.AgirLe) continue;
            var x = i.CibleId > 0 ? tous[i.CibleId - 1] : null;
            if (maintenant >= i.Jusqua || x is { EstVivant: false })
            {
                var pourquoi = x is { EstVivant: false } ? new Recit("ent.pourquoi.mort", ("x", x)) : Recits.Recit.Seul("ent.pourquoi.temps");
                h.Impulsions[k] = i with { Reponse = Reponse.Abandonnee };
                NoterEntite(maintenant, TypeEvenement.Parole, h, x, new Recit("ent.abandon", ("h", h), ("r", pourquoi)), i.EvenementId);
                Jardin.Retirer(h, $"entite:{(int)i.Parole}");
                continue;
            }
            if (i.Reponse == Reponse.Differee)
            {
                // Après avoir hésité, on se décide : la parole a perdu de sa force, ce qu'elle coûte n'a pas changé.
                var (cout, envie, raison, moitie) = Peser(h, i.Parole, x, tous, maintenant);
                var p = h.Personnalite;
                var obeir = Math.Clamp((0.2 + 0.35 * p.Moral.Fondements.Autorite + 0.25 * h.Croyance / 100) * (1 - cout) + 0.4 * envie, 0.01, 0.95);
                if (alea.Reel() >= obeir)
                {
                    h.Impulsions[k] = i with { Reponse = Reponse.Refusee, Jusqua = maintenant };
                    NoterEntite(maintenant, TypeEvenement.Parole, h, x, new Recit("ent.hesiteRefuse", ("h", h), ("r", raison)), i.EvenementId);
                    Jardin.Retirer(h, $"entite:{(int)i.Parole}");
                    continue;
                }
                i = i with { Reponse = moitie && alea.Reel() < 0.6 * cout ? Reponse.AMoitie : Reponse.Acceptee };
                h.Impulsions[k] = i;
                NoterEntite(maintenant, TypeEvenement.Parole, h, x, new Recit("ent.hesiteDecide", ("h", h)), i.EvenementId);
                if (Durable(i.Parole))
                {
                    Appliquer(h, i, x, tous, maintenant);
                    continue;
                }
            }
            if (Agir(h, k, i, x, tous, maintenant)) return true;
        }
        return false;
    }

    /// <summary>Fait (ou s'approche pour faire) ce que demande la parole <paramref name="i"/>. Vrai s'il y consacre ce pas.</summary>
    private bool Agir(Humain h, int k, Impulsion i, Humain? x, IReadOnlyList<Humain> tous, long maintenant)
    {
        // Le bien fait sur l'ordre de la voix compte comme un autre ; le mal aussi (voir Frapper).
        void Fait(Recit texte, bool significatif = false, double karma = 0)
        {
            h.Impulsions[k] = i with { Reponse = i.Reponse == Reponse.AMoitie ? Reponse.AMoitie : Reponse.Accomplie, Jusqua = Math.Max(i.Jusqua, maintenant), AgirLe = long.MaxValue };
            NoterEntite(maintenant, TypeEvenement.Parole, h, x, texte, i.EvenementId, significatif, karma);
            Jardin.Retirer(h, $"entite:{(int)i.Parole}");
        }

        // Ce qui ne demande pas d'être auprès de l'autre.
        switch (i.Parole)
        {
            case Parole.Quitte when x is not null:
                Quitter(h, x, i, tous, maintenant);
                Fait(new Recit("ent.fait.quitte", ("h", h), ("x", x)));
                return false;
            case Parole.Pardonne when x is not null:
                h.Griefs.RemoveAll(g => g.ContreId == x.Id);
                h.Colere *= 0.3;
                Social?.ChangerAffection(h, x, 10 * i.Force, maintenant);
                VieSociale.Rejouir(x, 4);
                Fait(new Recit("ent.fait.pardonne", ("h", h), ("x", x)), karma: Regard.Pardonner);
                return false;
            case Parole.PartsVers or Parole.CatastropheArrive:
            {
                int dx = i.Param % carte.Largeur, dy = i.Param / carte.Largeur;
                if (Distance(h.X, h.Y, dx, dy) <= 1)
                {
                    Fait(new Recit(i.Parole == Parole.PartsVers ? "ent.fait.arrive" : "ent.fait.abri", ("h", h)));
                    return false;
                }
                Avancer(h, dx, dy, Activite.Explorer);
                return h.CibleX >= 0;
            }
        }
        if (x is null) return false;
        if (Distance(h.X, h.Y, x.X, x.Y) > 1)
        {
            _autre = x;
            Avancer(h, x.X, x.Y, Activite.RejoindreProche);
            return h.CibleX >= 0;
        }

        // Auprès de l'autre.
        switch (i.Parole)
        {
            case Parole.UnisToi:
                if (h.EstFecond(maintenant) && x.EstFecond(maintenant))
                {
                    if (!Proposer(h, x, Math.Max(0.5, Attirance.Desir(h, x, Social?.IdealDe(h) ?? IdealBeaute.ParDefaut, maintenant)), tous, maintenant))
                    {
                        // Il n'a pas osé : il essaiera de nouveau demain.
                        h.Impulsions[k] = i with { AgirLe = maintenant + Calendrier.SecondesParJour };
                        return false;
                    }
                    Fait(new Recit("ent.fait.uni", ("h", h), ("x", x)));
                }
                else
                {
                    var veut = Vivant(tous, x.PartenaireId) is null && Vivant(tous, h.PartenaireId) is null
                               && alea.Reel() < 0.4 + x.AffectionPour(h.Id) / 100;
                    if (veut)
                    {
                        // Pendant un pas simultané, le couple se forme après le pas, si chacun est encore libre.
                        var cause = i.EvenementId;
                        Tour.Differer(tous, h, x, null, (t, u, v, _) =>
                        {
                            if (!u.EstVivant || !v.EstVivant || Vivant(t, u.PartenaireId) is not null || Vivant(t, v.PartenaireId) is not null) return;
                            u.PartenaireId = v.Id;
                            v.PartenaireId = u.Id;
                            var (a, b) = u.Sexe == Sexe.Femme ? (u, v) : (v, u);
                            Noter(maintenant, TypeEvenement.Union, a, b, new Recit("ent.couple", ("a", a), ("b", b)), cause);
                            VieSociale.Rejouir(u, 10);
                            VieSociale.Rejouir(v, 10);
                        });
                    }
                    Fait(new Recit(veut ? "ent.fait.accueille" : "ent.fait.repousse", ("h", h), ("x", x)));
                }
                return true;
            case Parole.PrendsSoin:
            {
                var soin = x.Sante < 80 || x.EstMalade(maintenant) || x.InfectionId != 0;
                var gain = 3 + 5 * h.Personnalite.Moral.Compassion;
                if (soin) Tour.Changer(x, y => y.Sante = Math.Min(100, y.Sante + gain));
                if (x.Faim > 50 && h.Faim < 50)
                {
                    Tour.Changer(x, y => y.Faim -= 25);
                    h.Faim += 15;
                }
                VieSociale.Gratitude(h, x, 0.5);
                Social?.ChangerAffection(h, x, 6, maintenant);
                h.Activite = Activite.Soigner;
                Fait(new Recit(soin ? "ent.fait.soigne" : "ent.fait.soin", ("h", h), ("x", x)),
                    karma: soin ? Regard.Veiller : Regard.Consoler);
                return true;
            }
            case Parole.Partage:
            {
                var moitie = i.Reponse == Reponse.AMoitie;
                var don = Math.Min(Math.Max(0, 70 - h.Faim), moitie ? 10 : 30);
                Tour.Changer(x, y => y.Faim = Math.Max(0, y.Faim - don));
                h.Faim = Math.Min(100, h.Faim + 0.7 * don);
                VieSociale.Gratitude(h, x, moitie ? 0.1 : 0.4);
                Fait(new Recit(moitie ? "ent.fait.partagePeu" : "ent.fait.partage", ("h", h), ("x", x)),
                    karma: don > 0 ? Regard.Partage : 0);
                return true;
            }
            case Parole.Console:
                Tour.Changer(x, y => y.Chagrin *= 0.6);
                VieSociale.Rejouir(x, 8);
                Social?.ChangerAffection(h, x, 5, maintenant);
                Social?.ChangerAffection(x, h, 8, maintenant);
                Fait(new Recit("ent.fait.console", ("h", h), ("x", x)), karma: Regard.Consoler);
                return true;
            case Parole.Frappe or Parole.Tue:
                Frapper(h, x, i, tous, maintenant);
                Fait(new Recit(i.Reponse == Reponse.AMoitie ? "ent.fait.moitie" : "ent.fait.mal", ("h", h)), significatif: true);
                return true;
        }
        return false;
    }

    /// <summary>« Quitte… » : se séparer de son compagnon (le délaissé souffre et en veut), ou s'éloigner d'un autre.</summary>
    private void Quitter(Humain h, Humain x, Impulsion i, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (h.PartenaireId != x.Id)
        {
            Social?.ChangerAffection(h, x, -20 * i.Force, maintenant);
            return;
        }
        h.PartenaireId = 0;
        var id = NoterEntite(maintenant, TypeEvenement.Union, h, x, new Recit("ent.quitte", ("h", h), ("x", x)), i.EvenementId, significatif: true);
        var aime = Math.Max(0, x.AffectionPour(h.Id)) / 100;
        var quitteur = h.Id;
        Tour.Changer(x, y =>
        {
            if (y.PartenaireId == quitteur) y.PartenaireId = 0;
            y.Chagrin = Math.Min(100, y.Chagrin + 20 + 40 * aime);
            y.Humeur = Math.Max(0, y.Humeur - 10 - 20 * aime);
        });
        Social?.ChangerAffection(x, h, -15, maintenant);
        Confier(x, TypeIntime.Peine, h.Id, new Recit("js.entite.quitte", ("x", h), ("g", x)), 30 + 60 * aime, id, maintenant, $"quitte:{h.Id}");
        Confier(h, TypeIntime.Culpabilite, x.Id, new Recit("js.entite.aQuitte", ("x", x)), 20 + 50 * h.Personnalite.Moral.EmpathieEmotionnelle * Math.Max(0, h.AffectionPour(x.Id)) / 100,
            id, maintenant, $"culpabilite:{x.Id}");
    }

    /// <summary>
    /// « Frappe… » ou « Tue… » : celui qui obéit à moitié s'en tient aux cris (frapper) ou aux coups (tuer). Il attaque par
    /// surprise, mais la force décide encore ; un meurtre soulève la colère des proches de la victime.
    /// </summary>
    private void Frapper(Humain h, Humain x, Impulsion i, IReadOnlyList<Humain> tous, long maintenant)
    {
        var tuer = i.Parole == Parole.Tue && i.Reponse != Reponse.AMoitie;
        if (i.Parole == Parole.Frappe && i.Reponse == Reponse.AMoitie)
        {
            NoterEntite(maintenant, TypeEvenement.Conflit, h, x, new Recit("ent.cris", ("h", h), ("x", x)), i.EvenementId);
            Social?.ChangerAffection(x, h, -6, maintenant);
            return;
        }
        // Pendant un pas simultané, l'attaque a lieu après le pas, si tous deux vivent encore.
        var (ih, ix) = (h.Id, x.Id);
        Tour.Differer(tous, t =>
        {
            if (t[ih - 1].EstVivant && t[ix - 1].EstVivant) Attaquer(t[ih - 1], t[ix - 1], i, tuer, t, maintenant);
        });
    }

    private void Attaquer(Humain h, Humain x, Impulsion i, bool tuer, IReadOnlyList<Humain> tous, long maintenant)
    {
        var fh = Force(h, maintenant) * 1.4; // l'effet de surprise
        var fx = Force(x, maintenant);
        var gagne = alea.Reel() < fh / (fh + fx);
        var degats = tuer ? (gagne ? 60 + 50 * alea.Reel() : 10 + 20 * alea.Reel()) : 5 + 25 * alea.Reel();
        var (vainqueur, vaincu) = gagne ? (h, x) : (x, h);
        var id = NoterEntite(maintenant, TypeEvenement.Conflit, h, x, gagne
            ? new Recit(tuer ? "ent.attaque.tuer" : "ent.attaque.frappe", ("h", h), ("x", x))
            : new Recit("ent.attaque.defend", ("h", h), ("x", x)), i.EvenementId, significatif: true, karma: Regard.Coups);
        vaincu.Sante -= degats;
        Medecine.Blesser(vaincu, degats, maintenant, OrigineBlessure.Coups);
        vaincu.Humeur = Math.Max(0, vaincu.Humeur - degats / 3);
        Social?.ChangerAffection(x, h, -25, maintenant);
        VieSociale.AjouterGrief(x, new Grief(h.Id, MotifGrief.Coups, Math.Min(100, 40 + degats), Math.Min(100, 40 + degats), id));
        Social?.Offenser(h, x, tuer ? 6 : 3, id, tous, maintenant);
        if (!Psyche.EstPsychopathe(h, maintenant))
            Confier(h, TypeIntime.Culpabilite, x.Id, new Recit("js.entite.frappe", ("x", x)), 30 + 60 * h.Personnalite.Moral.EmpathieEmotionnelle,
                id, maintenant, $"culpabilite:{x.Id}");
        if (vaincu.Sante <= 0)
        {
            Mourir(vaincu, maintenant, CauseDeMort.Meurtre, id, vainqueur, vainqueur == h ? Regard.Meurtre : Regard.LegitimeDefense);
            Social?.Offenser(vainqueur, vaincu, 15, vaincu.MortEvenementId, tous, maintenant);
        }
    }

    // ───────────── Raconter ─────────────

    /// <summary>
    /// Celui qui a cru le raconte, d'autant plus qu'il est extraverti ; ce qui fait honte (un ordre de faire du mal) se tait
    /// plutôt. Ceux qui l'entendent y croient ou en rient selon leur penchant pour l'invisible et ce qu'ils pensent de lui :
    /// le croyant gagne du prestige, le moqué en souffre.
    /// </summary>
    private void Raconter(Humain h, Parole parole, ModeParole mode, Recit phrase, long reception, IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var honte = Paroles.Nuit(parole) || parole is Parole.UnisToi or Parole.Quitte;
        if (h.EstEnfant(maintenant) && h.AgeEnAnnees(maintenant) < 5 || alea.Reel() >= (0.15 + 0.6 * p.Extraversion) * (honte ? 0.25 : 1)) return;
        var auditeurs = tous.Vivants().Where(o => o.Id != h.Id && o.EstVivant && !o.EstEnfant(maintenant) && Distance(o.X, o.Y, h.X, h.Y) <= 3
                                        && (h.CommunauteId == 0 || o.CommunauteId == h.CommunauteId)).Take(12).ToList();
        if (auditeurs.Count == 0) return;
        int croient = 0, rient = 0;
        foreach (var o in auditeurs)
        {
            var po = o.Personnalite;
            var estime = Math.Clamp(o.AffectionPour(h.Id) / 100, -1, 1);
            var c = Math.Clamp(0.05 + 0.55 * po.Transcendance + 0.2 * estime + 0.2 * p.Charisme + (o.Memoire.Jardin.Exists(x => x.Cle == "croyance:presence") ? 0.3 : 0), 0, 0.95);
            if (alea.Reel() < c)
            {
                croient++;
                Social?.ChangerAffection(o, h, 3 + 4 * po.Transcendance, maintenant);
                Confier(o, TypeIntime.Croyance, h.Id, new Recit("js.entite.aParle", ("x", h)), 25 + 40 * po.Transcendance, reception, maintenant, "croyance:presence");
            }
            else if (alea.Reel() < 0.5 * (1 - po.Agreabilite) + 0.2)
            {
                rient++;
                Social?.ChangerAffection(o, h, -2, maintenant);
            }
        }
        var texte = croient + rient == 0 ? new Recit("ent.raconte.rien", ("h", h), ("reve", mode == ModeParole.Reve))
            : new Recit(rient > 0 ? "ent.raconte.rient" : "ent.raconte", ("h", h), ("reve", mode == ModeParole.Reve),
                ("c", new Recit("ent.croient", ("n", croient))), ("r", new Recit("ent.rient", ("n", rient))));
        NoterEntite(maintenant, TypeEvenement.Parole, h, null, texte, reception);
        if (rient > croient)
        {
            h.Humeur = Math.Max(0, h.Humeur - 3 * rient * (0.5 + p.Nevrosisme));
            Confier(h, TypeIntime.Peine, 0, Recits.Recit.Seul("js.entite.moque"), 20 + 10 * rient, reception, maintenant, "entite:moque");
        }
    }

    // ───────────── Penchants et suivi ─────────────

    /// <summary>
    /// « Laisser le monde choisir » : celui ou celle, alentour, qui lui plaît le plus et qui n'est pas de sa famille ; avec un
    /// peu de hasard, comme dans la vie. Sans changer son cœur, on ne choisit que parmi le sexe qui l'attire.
    /// </summary>
    private Humain? Choisir(Humain h, bool orientation, IReadOnlyList<Humain> tous, long maintenant)
    {
        var ideal = Social?.IdealDe(h) ?? IdealBeaute.ParDefaut;
        var rayon = Math.Max(RayonPerception(h), 6);
        Humain? choisi = null;
        var meilleur = 0.0;
        foreach (var a in tous.Vivants())
        {
            if (a.Id == h.Id || !a.EstVivant || a.EstEnfant(maintenant) || SontParents(h, a)) continue;
            if (Distance(h.X, h.Y, a.X, a.Y) > rayon && (h.CommunauteId == 0 || a.CommunauteId != h.CommunauteId)) continue;
            var vers = h.Orientation.Vers(a.Sexe);
            if (!orientation && vers < 0.3) continue;
            var age = Math.Abs(a.AgeEnAnnees(maintenant) - h.AgeEnAnnees(maintenant));
            var score = (0.65 * Attirance.Attractivite(a, ideal, maintenant) / 100 + 0.2 * Math.Max(0, h.AffectionPour(a.Id)) / 100 + 0.15 * a.Personnalite.Charisme)
                        * (orientation ? 1 : vers) / (1 + age / 15) * (0.6 + 0.8 * alea.Reel());
            if (score > meilleur) (choisi, meilleur) = (a, score);
        }
        return choisi;
    }

    /// <summary>
    /// Chaque pas : un penchant fait grandir l'affection auprès de l'autre ; les interventions anciennes et terminées
    /// s'effacent de la mémoire.
    /// </summary>
    private void Ressentir(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var i in h.Impulsions)
        {
            if (i.Parole != Parole.Penchant || i.Reponse != Reponse.Ressenti || maintenant >= i.Jusqua) continue;
            var x = tous[i.CibleId - 1];
            if (x.EstVivant && Distance(h.X, h.Y, x.X, x.Y) <= 1 && h.AffectionPour(x.Id) < 90) Social?.ChangerAffection(h, x, 0.03 * i.ForceA(maintenant), maintenant);
        }
        if (maintenant % Calendrier.SecondesParJour >= MondeSimule.SecondesParPas) return;
        var oubli = maintenant - 2 * Calendrier.SecondesParAn;
        h.Impulsions.RemoveAll(i => i.Jusqua < oubli && i.AgirLe < oubli && i.Le < oubli);
    }

    private static void Garder(Humain h, Impulsion i)
    {
        h.Impulsions.Add(i);
        if (h.Impulsions.Count <= ImpulsionsMax) return;
        var k = h.Impulsions.FindIndex(x => x.Reponse is not (Reponse.Acceptee or Reponse.Differee or Reponse.AMoitie or Reponse.Ressenti));
        h.Impulsions.RemoveAt(k >= 0 ? k : 0);
    }

    private object? Extra(Parole parole, int param) => parole switch
    {
        Parole.PartsVers => Recits.Recit.Seul($"direction.{Math.Clamp(param, 0, 7)}"),
        Parole.PlanteDangereuse when param >= 1 && param <= Flore.Especes.Count => Flore.Get(param).Dit,
        Parole.NeVaPasALaGuerre when param > 0 && Social is not null && param <= Social.Communautes.Count => Social.Communautes[param - 1],
        _ => null,
    };

    /// <summary>Note un événement venu de l'Entité (marqué comme tel au journal).</summary>
    private long NoterEntite(long maintenant, TypeEvenement type, Humain h, Humain? autre, Recit texte, long cause, bool significatif = false,
        double karma = 0)
    {
        var e = new EvenementJournal(maintenant, type, h.Id, autre?.Id ?? 0, texte, Origine.Entite)
        {
            Id = Numeros.Suivant(), CauseId = cause, Significatif = significatif, Karma = Regard.Juger(h, karma, maintenant, autre),
        };
        Journal.Add(e);
        return e.Id;
    }

    private static string Soi(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "lui";
}
