using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Pourquoi des avances sont repoussées.</summary>
internal enum Refus : byte
{
    Aucun = 0,
    /// <summary>Celui qui les fait ne plaît pas.</summary>
    NePlaitPas = 1,
    /// <summary>On tient à son compagnon.</summary>
    Fidelite = 2,
    /// <summary>Le désir est là, mais on n'ose pas (même sexe, là où c'est mal vu).</summary>
    NOsePas = 3,
    /// <summary>Celui qui les fait est un parent : on ne s'unit pas entre parents.</summary>
    Parente = 4,
}

/// <summary>
/// Désirs, avances, refus et désirs refoulés. Le désir va d'abord à qui plaît (l'attrait physique pèse plus que le reste
/// dans le choix d'un partenaire : Eastwick et Finkel, 2008) ; être repoussé blesse, d'autant plus qu'on est anxieux
/// (Downey et Feldman, 1996), et l'acceptation réjouit. Celui qui est attiré par son propre sexe ne le sait pas toujours :
/// la prise de conscience vient par étapes, d'un trouble mal compris à la certitude, souvent des années plus tard
/// (Calzo et al., 2011). Là où ces amours sont mal vues, on les cache ou on les refoule (Meyer, 2003 : le stress de
/// la minorité).
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Chance, à chaque occasion, d'oser aller vers quelqu'un de son sexe : il faut le savoir, que l'entourage le tolère,
    /// et un esprit ouvert, peu soucieux des règles. Réglée pour qu'on ose en quelques jours là où c'est accepté, en
    /// plusieurs semaines ou jamais là où c'est réprouvé.
    /// </summary>
    private double Ose(Humain h)
    {
        var tolerance = Social?.ToleranceMoeursDe(h) ?? VieSociale.TemperamentMoeurs(h.Personnalite);
        return 0.06 * h.Lucidite * h.Lucidite * (0.05 + tolerance * tolerance)
               * (0.5 + 0.5 * h.Personnalite.Ouverture) * (1.2 - 0.6 * h.Personnalite.Conscience);
    }

    /// <summary>Chance de répondre à des avances de quelqu'un de son sexe qui nous plaît : il faut le savoir un peu, et oser.</summary>
    private double OseAccepter(Humain h)
    {
        var tolerance = Social?.ToleranceMoeursDe(h) ?? VieSociale.TemperamentMoeurs(h.Personnalite);
        return Math.Clamp(h.Lucidite * (0.2 + 0.8 * tolerance) * (0.6 + 0.6 * h.Personnalite.Ouverture), 0, 1);
    }

    /// <summary>Vrai si le jardin garde une chose de cette clé née il y a moins de <paramref name="jours"/> jours (pour ne pas tout redire).</summary>
    private static bool Recent(Humain h, string cle, double jours, long maintenant) =>
        h.Memoire.Jardin.Exists(x => x.Cle == cle && maintenant - x.Secondes < jours * Calendrier.SecondesParJour);

    private static void Confier(Humain h, TypeIntime type, int sujet, string texte, double intensite, long cause, long maintenant, string cle)
    {
        Jardin.Retirer(h, cle);
        Jardin.Confier(h, type, sujet, texte, intensite, cause, maintenant, cle);
    }

    private static string Il(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "il";
    private static string Les(Sexe s) => s == Sexe.Femme ? "les femmes" : "les hommes";

    /// <summary>
    /// <paramref name="h"/> fait des avances à <paramref name="autre"/>, qui lui plaît (<paramref name="desir"/>). Il peut
    /// se retenir (désir refoulé), puis l'autre accepte ou repousse, selon ce que <paramref name="h"/> lui inspire, sa
    /// fidélité et, s'ils sont du même sexe, ce qu'il ose. Renvoie faux si le désir a été refoulé (rien ne s'est passé).
    /// </summary>
    internal bool Proposer(Humain h, Humain autre, double desir, IReadOnlyList<Humain> tous, long maintenant)
    {
        var meme = autre.Sexe == h.Sexe;
        var compagnon = Vivant(tous, h.PartenaireId);
        if (meme)
        {
            Eveiller(h, desir, maintenant);
            if (alea.Reel() >= Ose(h))
            {
                Refouler(h, autre, desir, null, maintenant);
                return false;
            }
        }
        if (compagnon is not null && compagnon.Id != autre.Id && alea.Reel() >= Ouverture(h, autre, desir, tous))
        {
            Refouler(h, autre, desir, compagnon, maintenant);
            return false;
        }

        var idealAutre = Social?.IdealDe(autre) ?? IdealBeaute.ParDefaut;
        var retour = Attirance.Desir(autre, h, idealAutre, maintenant);
        var plait = Math.Clamp(0.5 + (retour - Attirance.Exigence(autre, idealAutre, maintenant)) / 0.2, 0, 1);
        var refus = Refus.Aucun;
        if (Interdit(autre, h, maintenant)) refus = Refus.Parente;
        else if (alea.Reel() >= plait) refus = Refus.NePlaitPas;
        else if (meme)
        {
            Eveiller(autre, retour, maintenant);
            if (alea.Reel() >= OseAccepter(autre)) refus = Refus.NOsePas;
        }
        if (refus == Refus.Aucun && alea.Reel() >= Ouverture(autre, h, retour, tous)) refus = Refus.Fidelite;

        if (refus != Refus.Aucun)
        {
            Repousser(h, autre, desir, refus, tous, maintenant);
            return true;
        }
        // Être accepté réjouit, surtout la première fois.
        if (!Recent(h, $"accepte:{autre.Id}", 60, maintenant))
        {
            VieSociale.Rejouir(h, 3 + 6 * desir);
            VieSociale.Rejouir(autre, 2 + 4 * retour);
            Confier(h, TypeIntime.Joie, autre.Id, $"{autre.Nom} a bien voulu de moi.", 15 + 30 * desir, 0, maintenant, $"accepte:{autre.Id}");
        }
        SAccoupler(h, autre, tous, maintenant);
        return true;
    }

    /// <summary>Des avances repoussées : la blessure, plus vive chez les anxieux et quand on désirait fort, et parfois le doute de soi.</summary>
    private void Repousser(Humain h, Humain autre, double desir, Refus refus, IReadOnlyList<Humain> tous, long maintenant)
    {
        h.Desir = Math.Max(0, h.Desir - 20);
        var cle = $"refus:{autre.Id}";
        if (Recent(h, cle, refus == Refus.Fidelite ? 120 : 20, maintenant)) return;
        var texte = refus switch
        {
            Refus.Fidelite when Vivant(tous, autre.PartenaireId) is { } c =>
                $"{autre.Nom} repousse les avances de {h.Nom} : {Il(autre)} tient à {VieSociale.Parente(autre, c)}.",
            Refus.NOsePas => $"{autre.Nom} repousse les avances de {h.Nom}, malgré son trouble.",
            Refus.Parente => $"{autre.Nom} repousse avec horreur les avances de {h.Nom} : on ne s'unit pas entre parents.",
            _ => $"{autre.Nom} repousse les avances de {h.Nom}, qui ne lui plaît pas.",
        };
        var id = Noter(maintenant, TypeEvenement.Attirance, h, autre, texte);
        var blessure = (4 + 12 * desir) * (0.6 + 0.8 * h.Personnalite.Nevrosisme) * (refus == Refus.NePlaitPas ? 1 : 0.5);
        h.Humeur = Math.Max(0, h.Humeur - blessure);
        Confier(h, TypeIntime.Peine, autre.Id, refus == Refus.NePlaitPas ? $"{autre.Nom} ne veut pas de moi." : $"{autre.Nom} m'a repoussé{E(h)}.",
            15 + 50 * desir, id, maintenant, cle);
        if (refus == Refus.NePlaitPas && Attirance.Attractivite(h, Social?.IdealDe(h) ?? IdealBeaute.ParDefaut, maintenant) < 45
            && h.Memoire.Jardin.Count(x => x.Cle.StartsWith("refus:", StringComparison.Ordinal)) >= 2)
        {
            Confier(h, TypeIntime.ImageDeSoi, 0, "Personne ne veut de moi.", 20 + 40 * h.Personnalite.Nevrosisme, id, maintenant, "image:attrait");
        }
        if (refus == Refus.NOsePas)
        {
            Confier(autre, TypeIntime.Question, h.Id, $"Pourquoi {h.Nom} me trouble-t-{Il(h)} ainsi ?", 30, id, maintenant, $"trouble:{h.Id}");
        }
        // Des avances de quelqu'un de son sexe, repoussées : là où c'est mal vu, on peut le dire autour de soi.
        if (autre.Sexe == h.Sexe && refus == Refus.NePlaitPas && Social is not null
            && alea.Reel() < 0.5 * (1 - Social.ToleranceMoeursDe(autre)))
        {
            Social.DecouvrirMoeurs(autre, h, null, id, tous, maintenant);
        }
    }

    /// <summary>
    /// Un désir retenu : par fidélité, parce que l'usage le réprouve, ou, pour quelqu'un de son sexe, faute d'oser ou même de
    /// comprendre ce qu'on ressent. Noté au journal et au jardin secret, sans le redire chaque jour.
    /// </summary>
    private void Refouler(Humain h, Humain autre, double desir, Humain? compagnon, long maintenant)
    {
        var meme = compagnon is null;
        if (!meme && desir < 0.7) return;
        var cle = $"refoule:{autre.Id}";
        if (Recent(h, cle, meme ? 60 : 180, maintenant)) return;
        string raison, pensee;
        var type = TypeIntime.Peine;
        if (meme)
        {
            var tolerance = Social?.ToleranceMoeursDe(h) ?? 0.5;
            var p = h.Personnalite;
            var x = autre.Nom;
            (raison, pensee) = h.Lucidite < 0.3
                ? ("sans comprendre ce trouble", Jardin.Varier(h, cle, 0, x,
                    new Jardin.V($"{x} me trouble. Je ne comprends pas pourquoi.", 0.5),
                    new Jardin.V($"Pourquoi mon cœur bat-il ainsi près de {x} ?", 0.3, 2),
                    new Jardin.V($"{x} me trouble.", 0.3, 0))!)
                : tolerance < 0.4
                    ? ("car les siens réprouvent ces amours", Jardin.Varier(h, cle, 1, x,
                        new Jardin.V($"J'aime {x}, mais personne ne doit le savoir.", 0.5),
                        new Jardin.V($"Ce que je ressens pour {x} doit rester caché.", 0.3),
                        new Jardin.V($"Si les miens savaient ce que j'éprouve pour {x}…", 0.1 + Jardin.S(p.Nevrosisme), 2))!)
                    : ("sans oser le dire", Jardin.Varier(h, cle, 2, x,
                        new Jardin.V($"Je n'ose pas dire à {x} ce que je ressens.", 0.4),
                        new Jardin.V($"Un jour, je dirai à {x} ce que je ressens.", 0.1 + Jardin.S(p.Extraversion)),
                        new Jardin.V($"{x} ne sait pas ce que j'éprouve.", 0.3),
                        new Jardin.V($"Je garde pour moi ce que je ressens pour {x}.", 0.1 + Jardin.S(p.Moral.EvitementAffectif)))!);
            if (h.Lucidite < 0.3) type = TypeIntime.Question;
        }
        else
        {
            raison = (Social?.ToleranceDe(h) ?? 0.5) < 0.4
                ? "car les siens réprouvent qu'on s'unisse hors de son couple"
                : $"par attachement à {VieSociale.Parente(h, compagnon!)}";
            var p = h.Personnalite;
            pensee = Jardin.Varier(h, cle, 3, autre.Nom,
                new Jardin.V($"{autre.Nom} me plaît, mais je reste avec {compagnon!.Nom}.", 0.4),
                new Jardin.V($"Je pense à {autre.Nom}, mais c'est {compagnon.Nom} que j'ai choisi{E(h)}.", 0.2 + Jardin.S(p.Conscience)),
                new Jardin.V($"{autre.Nom} me trouble ; je ne trahirai pas {compagnon.Nom}.", 0.1 + Jardin.S(p.Moral.Honnetete) + Jardin.S(p.Moral.Fondements.Loyaute)),
                new Jardin.V($"{autre.Nom}… non. Il y a {compagnon.Nom}.", 0.2, 0))!;
            type = TypeIntime.Intention;
        }
        var id = Noter(maintenant, TypeEvenement.Attirance, h, autre, $"{h.Nom} désire {autre.Nom}, mais refoule ce désir {raison}.");
        Confier(h, type, autre.Id, pensee, 20 + 50 * desir, id, maintenant, cle);
    }

    /// <summary>
    /// Chaque désir pour quelqu'un de son sexe éclaire un peu l'humain sur lui-même, plus vite s'il a l'esprit ouvert :
    /// d'abord un trouble, puis la certitude.
    /// </summary>
    private void Eveiller(Humain h, double desir, long maintenant)
    {
        if (h.Lucidite >= 1) return;
        var avant = h.Lucidite;
        h.Lucidite = Math.Min(1, h.Lucidite + 0.00005 * desir * (0.5 + h.Personnalite.Ouverture));
        var autres = Les(h.Sexe);
        if (avant < 0.3 && h.Lucidite >= 0.3)
        {
            var id = Noter(maintenant, TypeEvenement.Attirance, h, null, $"{h.Nom} se sent troublé{E(h)} par {autres}, sans comprendre pourquoi.");
            Confier(h, TypeIntime.Question, 0, $"Pourquoi {autres} me troublent-{(h.Sexe == Sexe.Femme ? "elles" : "ils")} ?", 40, id, maintenant, "orientation");
        }
        else if (avant < 0.6 && h.Lucidite >= 0.6)
        {
            var aussi = h.Orientation.Vers(h.Sexe == Sexe.Homme ? Sexe.Femme : Sexe.Homme) >= 0.5 ? "aussi " : "";
            var id = Noter(maintenant, TypeEvenement.Attirance, h, null, $"{h.Nom} comprend que ce sont {aussi}{autres} qui l'attirent.");
            Confier(h, TypeIntime.ImageDeSoi, 0, $"Ce sont {aussi}{autres} que je désire.", 50, id, maintenant, "orientation");
        }
    }

    /// <summary>
    /// Deux personnes de même sexe s'aiment : en secret là où c'est mal vu. Un témoin peut les surprendre. Deux qui préfèrent
    /// leur propre sexe, seuls l'un et l'autre, peuvent former un couple si leur communauté l'admet.
    /// </summary>
    private void SAimer(Humain a, Humain b, IReadOnlyList<Humain> tous, long maintenant)
    {
        var tolerance = Social?.ToleranceMoeursDe(a) ?? 0.5;
        var secret = tolerance < 0.5;
        var infidele = Vivant(tous, a.PartenaireId) is { } pa && pa.Id != b.Id || Vivant(tous, b.PartenaireId) is { } pb && pb.Id != a.Id;
        var hors = infidele ? " (hors de leur couple)" : "";
        // Comme pour les accouplements, on ne note pas chaque fois : la première, puis au plus une fois par saison.
        var nouveau = !Recent(a, $"amour:{b.Id}", Calendrier.JoursParSaison, maintenant);
        var id = nouveau
            ? Noter(maintenant, TypeEvenement.Attirance, a, b, secret ? $"{a.Nom} et {b.Nom} s'aiment en secret{hors}." : $"{a.Nom} et {b.Nom} s'aiment{hors}.")
            : Noter(maintenant, TypeEvenement.Accouplement, a, b, $"{a.Nom} et {b.Nom} s'aiment{hors}.");
        foreach (var (x, y) in new[] { (a, b), (b, a) })
        {
            VieSociale.Rejouir(x, 8);
            x.Lucidite = Math.Max(x.Lucidite, 0.6);
            if (!nouveau) continue;
            Confier(x, TypeIntime.Joie, y.Id, $"Avec {y.Nom}, je me sens enfin moi-même.", 50, id, maintenant, $"amour:{y.Id}");
            if (secret) Confier(x, TypeIntime.Peur, y.Id, $"Si les miens savaient, pour {y.Nom}…", 30 + 50 * (0.5 - tolerance), id, maintenant, "moeurs-secret");
        }
        if (infidele) Ebruiter(a, b, id, tous, maintenant);

        // Un témoin, là où l'on cache ces amours, peut les surprendre.
        if (Social is not null)
        {
            foreach (var t in tous)
            {
                if (!t.EstVivant || t.Id == a.Id || t.Id == b.Id || t.EstEnfant(maintenant) || t.Activite == Activite.Dormir
                    || Distance(t.X, t.Y, a.X, a.Y) > 2) continue;
                if (alea.Reel() < 0.4 * (1 - tolerance)) Social.DecouvrirMoeurs(t, a, b, id, tous, maintenant);
                break;
            }
        }

        if (Vivant(tous, a.PartenaireId) is null && Vivant(tous, b.PartenaireId) is null && tolerance >= 0.45
            && Attirance.PourSonSexe(a) > a.Orientation.Vers(a.Sexe == Sexe.Homme ? Sexe.Femme : Sexe.Homme)
            && Attirance.PourSonSexe(b) > b.Orientation.Vers(b.Sexe == Sexe.Homme ? Sexe.Femme : Sexe.Homme))
        {
            a.PartenaireId = b.Id;
            b.PartenaireId = a.Id;
            Noter(maintenant, TypeEvenement.Union, a, b, $"{a.Nom} et {b.Nom} forment un couple.", id);
            VieSociale.Rejouir(a, 10);
            VieSociale.Rejouir(b, 10);
        }
    }
}
