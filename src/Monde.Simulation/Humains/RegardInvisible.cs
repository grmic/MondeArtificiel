using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Ce qu'un humain éprouve envers l'invisible, en plus d'y croire.</summary>
public enum SentimentInvisible : byte
{
    Amour = 0,
    Crainte = 1,
    Revolte = 2,
}

/// <summary>
/// Regard sur l'invisible : amour, crainte et révolte, de 0 à 100 chacun, à côté de la croyance.
/// <para>
/// On peut aimer et craindre à la fois un dieu : ce sont deux dimensions distinctes des représentations de Dieu
/// (Benson et Spilka, 1973 ; Johnson et al., 2013, dieu bienveillant et dieu punitif). Un bienfait fait monter l'amour, plus chez
/// les esprits agréables. Un châtiment jugé mérité fait monter la crainte sans faire baisser l'amour, plus chez les anxieux et
/// ceux qui respectent l'autorité ; jugé injuste, il fait monter la crainte, baisser l'amour, et peut faire naître la révolte,
/// plus chez les proches de la victime et chez les esprits peu agréables et peu soumis à l'autorité (Exline et al., 2011, colère
/// envers Dieu). Le mérite se juge sur ce que l'on sait de la victime (ses torts envers soi, ce qu'on en dit) ; sans rien savoir,
/// on croit volontiers que la victime l'a cherché (Lerner, 1980, croyance en un monde juste).
/// </para>
/// Sans signe, amour et crainte retombent lentement ; la révolte s'efface comme une rancune. Ils se transmettent comme la
/// croyance, et les récits d'une communauté s'effacent en trois générations environ (Vansina, 1985).
/// </summary>
public static class Regards
{
    /// <summary>Demi-vie d'un souvenir de la communauté, en années : un peu plus d'une génération (25 ans environ).</summary>
    public const double DemiVieSouvenir = 25;

    /// <summary>
    /// Ce qui retient de voler ou de frapper (facteur, 1 : rien) : craindre un invisible qui punit rend un peu plus honnête
    /// (Shariff et Norenzayan, 2011 ; Purzycki et al., 2016) ; l'effet reste modeste, au plus 30 % de moins.
    /// </summary>
    public static double Retenue(Humain h) => 1 - 0.3 * h.Crainte / 100 * h.Croyance / 100;

    public static double Valeur(Humain h, SentimentInvisible s) => s switch
    {
        SentimentInvisible.Amour => h.Amour,
        SentimentInvisible.Crainte => h.Crainte,
        _ => h.Revolte,
    };

    private static void Fixer(Humain h, SentimentInvisible s, double v)
    {
        switch (s)
        {
            case SentimentInvisible.Amour: h.Amour = v; break;
            case SentimentInvisible.Crainte: h.Crainte = v; break;
            default: h.Revolte = v; break;
        }
    }

    /// <summary>
    /// Fait bouger un sentiment de <paramref name="h"/> envers l'invisible. Une hausse compte moins quand il est déjà fort.
    /// Renvoie le changement réel.
    /// </summary>
    public static double Ajouter(Humain h, SentimentInvisible s, double delta, Recit raison, long cause, long maintenant, bool signe = false)
    {
        if (Tour.Autrui(h))
        {
            Tour.Differer(h, x => Ajouter(x, s, delta, raison, cause, maintenant, signe));
            return 0;
        }
        if (!h.EstVivant || h.EstBebe(maintenant)) return 0;
        if (signe) h.RegardSigneLe = maintenant;
        if (delta == 0 || !double.IsFinite(delta)) return 0;
        var avant = Valeur(h, s);
        var d = delta > 0 ? delta * (1 - avant / 100) : delta;
        var apres = Math.Clamp(avant + d, 0, 100);
        Fixer(h, s, apres);
        d = apres - avant;
        Retenir(h, d, raison, cause);
        return d;
    }

    /// <summary>Garde ce qui a le plus fait bouger le regard, pour le dire quand il change d'une dizaine.</summary>
    private static void Retenir(Humain h, double d, Recit raison, long cause)
    {
        if (Math.Abs(d) > h.RegardPoussee) (h.RegardPoussee, h.RegardRaison, h.RegardCause) = (Math.Abs(d), raison, cause);
    }

    /// <summary>
    /// Une journée : amour et crainte se règlent sur ceux des parents (enfant), des proches et de la communauté (et de ce qu'elle
    /// se raconte) ; sans signe depuis plus d'un an, ils retombent ; la révolte s'efface comme une rancune. Le révolté détourne
    /// ses proches : il compte pour eux comme quelqu'un qui n'aime pas l'invisible.
    /// </summary>
    internal static void Journee(Humain h, IReadOnlyList<Humain> tous, IReadOnlyDictionary<int, (double Amour, double Crainte)> communautes,
        IReadOnlyDictionary<int, (double Bienfaits, double Chatiments)> souvenirs, long maintenant)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return;
        var p = h.Personnalite;
        var enfant = h.EstEnfant(maintenant);
        double amour = 0, crainte = 0, poids = 0;
        void Compter(Humain o, double w)
        {
            amour += w * o.Amour * (1 - o.Revolte / 100);
            crainte += w * o.Crainte;
            poids += w;
        }
        foreach (var id in new[] { h.MereId, h.PereId })
            if (id >= 1 && id <= tous.Count && tous[id - 1].EstVivant) Compter(tous[id - 1], enfant ? 2 : 0.4);
        foreach (var l in h.Liens)
            if (l.Affection >= 20 && l.Id >= 1 && l.Id <= tous.Count && tous[l.Id - 1].EstVivant) Compter(tous[l.Id - 1], 0.5 * l.Affection / 100);
        if (h.CommunauteId != 0)
        {
            if (communautes.TryGetValue(h.CommunauteId, out var m))
            {
                amour += 0.5 * m.Amour;
                crainte += 0.5 * m.Crainte;
                poids += 0.5;
            }
            // Ce que la communauté se raconte des gestes passés tire vers lui.
            if (souvenirs.TryGetValue(h.CommunauteId, out var s))
            {
                amour += 0.3 * Math.Min(100, s.Bienfaits);
                crainte += 0.3 * Math.Min(100, s.Chatiments);
                poids += 0.3;
            }
        }
        double a = h.Amour, c = h.Crainte;
        if (poids > 0)
        {
            // Même vitesse que la croyance : quelques mois chez l'enfant, deux ans environ chez l'adulte.
            var f = 1 - Math.Pow(0.5, 1 / ((enfant ? 120 : 600) / (0.5 + p.Moral.Fondements.Autorite)));
            var da = (amour / poids - a) * f;
            var dc = (crainte / poids - c) * f;
            a += da;
            c += dc;
            Retenir(h, da, Recit.Seul(da > 0 ? "reg.siensAiment" : "reg.siensAimentGuere"), 0);
            Retenir(h, dc, Recit.Seul(dc > 0 ? "reg.siensCraignent" : "reg.siensCraignentGuere"), 0);
        }
        if (maintenant - h.RegardSigneLe > Calendrier.SecondesParAn)
        {
            // Sans signe, la crainte tombe plus vite que l'amour (demi-vies de quelques années, plus longues chez qui est porté à la transcendance).
            var t = 0.6 + 0.8 * p.Transcendance;
            var da = -a * (1 - Math.Pow(0.5, 1 / (360 * 5 * t)));
            var dc = -c * (1 - Math.Pow(0.5, 1 / (360 * 3 * t * (0.6 + 0.8 * p.Nevrosisme))));
            a += da;
            c += dc;
            Retenir(h, Math.Min(da, dc), Recit.Seul("cro.aucunSigne"), 0);
        }
        h.Amour = Math.Clamp(a, 0, 100);
        h.Crainte = Math.Clamp(c, 0, 100);
        if (h.Revolte > 0)
        {
            // Comme la rancune envers qui a tué un proche (voir VieSociale) : plus longue chez l'anxieux et le peu agréable.
            var demiVie = 180 * Math.Exp(1.5 * (p.Nevrosisme - 0.5) - 2 * (p.Agreabilite - 0.5));
            var dr = -h.Revolte * (1 - Math.Pow(0.5, 1 / demiVie));
            h.Revolte = Math.Max(0, h.Revolte + dr);
            Retenir(h, dr, Recit.Seul("reg.apaise"), 0);
            if (h.Revolte < 0.5) h.Revolte = 0;
        }
    }

    /// <summary>Textes des changements d'une dizaine (amour, crainte, révolte), ou rien.</summary>
    internal static IEnumerable<(SentimentInvisible Sentiment, Recit Texte)> Changements(Humain h)
    {
        foreach (var s in new[] { SentimentInvisible.Amour, SentimentInvisible.Crainte, SentimentInvisible.Revolte })
        {
            var note = s switch { SentimentInvisible.Amour => h.AmourNote, SentimentInvisible.Crainte => h.CrainteNote, _ => h.RevolteNote };
            var apres = Valeur(h, s);
            if (double.IsNaN(note))
            {
                Noter(h, s, apres);
                continue;
            }
            if (Math.Floor(note / 10) == Math.Floor(apres / 10) || Math.Abs(apres - note) < 3) continue;
            var monte = apres > note;
            var n = (int)Math.Round(apres);
            var (cle, cleRaison) = s switch
            {
                SentimentInvisible.Amour => monte ? ("reg.aimePlus", "reg.aimePlus.r") : ("reg.aimeMoins", "reg.aimeMoins.r"),
                SentimentInvisible.Crainte => monte ? ("reg.craintPlus", "reg.craintPlus.r") : ("reg.craintMoins", "reg.craintMoins.r"),
                _ when monte && apres >= 50 && note < 50 => ("reg.revolte", "reg.revolte.r"),
                _ => monte ? ("reg.enVeutPlus", "reg.enVeutPlus.r") : ("reg.enVeutMoins", "reg.enVeutMoins.r"),
            };
            var texte = h.RegardRaison is { } r ? new Recit(cleRaison, ("h", h), ("n", n), ("r", r)) : new Recit(cle, ("h", h), ("n", n));
            Noter(h, s, apres);
            yield return (s, texte);
        }
    }

    private static void Noter(Humain h, SentimentInvisible s, double v)
    {
        switch (s)
        {
            case SentimentInvisible.Amour: h.AmourNote = v; break;
            case SentimentInvisible.Crainte: h.CrainteNote = v; break;
            default: h.RevolteNote = v; break;
        }
    }

    /// <summary>Un sentiment en mots, pour l'observateur.</summary>
    public static string Dire(SentimentInvisible s, double v) => (s, v) switch
    {
        (_, < 5) => s switch { SentimentInvisible.Amour => "aucun amour", SentimentInvisible.Crainte => "aucune crainte", _ => "aucune révolte" },
        (_, < 25) => "un peu",
        (_, < 50) => "assez",
        (_, < 75) => "beaucoup",
        _ => s switch { SentimentInvisible.Amour => "adore", SentimentInvisible.Crainte => "terrifié", _ => "le maudit" },
    };
}

public sealed partial class Comportement
{
    // ───────────── Ce que les humains voient d'un geste ─────────────

    /// <summary>
    /// Ceux qui perçoivent un geste visible fait à <paramref name="h"/>, avec la force de ce qu'ils perçoivent : lui-même ; ceux
    /// qui sont là (la foudre se voit de quelques kilomètres, une voix ou une apparition seulement de qui est à quelques centaines
    /// de mètres) ; pour un rêve, ses proches qui le partagent.
    /// </summary>
    private List<(Humain Qui, double Ampleur, bool Direct)> Temoins(Humain h, ManiereGeste maniere, IReadOnlyList<Humain> tous, long maintenant)
    {
        var vus = new List<(Humain, double, bool)>();
        if (maniere == ManiereGeste.Cachee) return vus;
        vus.Add((h, 1, true));
        var portee = maniere == ManiereGeste.Foudre ? 4 : 1;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.EstBebe(maintenant)) continue;
            if (maniere == ManiereGeste.Reve)
            {
                if (Famille(o, h) || o.AffectionPour(h.Id) >= 40) vus.Add((o, 0.8, true));
                continue;
            }
            var d = Distance(o.X, o.Y, h.X, h.Y);
            if (d <= portee) vus.Add((o, d == 0 ? 1 : d <= 1 ? 0.8 : 0.5, true));
        }
        return vus;
    }

    private static bool Famille(Humain o, Humain h) =>
        o.MereId == h.Id || o.PereId == h.Id || h.MereId == o.Id || h.PereId == o.Id || o.PartenaireId == h.Id;

    /// <summary>
    /// Un geste visible se sait : ceux qui l'ont vu (<paramref name="vecu"/>) en sont touchés, puis le récit gagne les proches de
    /// la cible et la communauté, plus faiblement. Chacun juge un châtiment mérité ou non, et en ressent amour, crainte ou révolte.
    /// La communauté en garde le souvenir. Un geste caché ne se sait pas : seul celui qui l'avait demandé croit sa prière
    /// entendue (voir <see cref="Resoudre"/>).
    /// </summary>
    private void FaireSavoir(GesteEntite geste, ManiereGeste maniere, Humain? cible, int communaute,
        List<(Humain Qui, double Ampleur, bool Direct)> vecu, bool bienfait, double ampleur, long annonce, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (maniere == ManiereGeste.Cachee || vecu.Count == 0) return;
        var touches = new Dictionary<int, (Humain Qui, double Ampleur, bool Direct)>();
        foreach (var v in vecu)
            if (!touches.TryGetValue(v.Qui.Id, out var t) || t.Ampleur < v.Ampleur) touches[v.Qui.Id] = v;
        // Le récit : la communauté, et les proches de la cible où qu'ils soient.
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.EstBebe(maintenant) || touches.ContainsKey(o.Id)) continue;
            var a = communaute != 0 && o.CommunauteId == communaute ? 0.4 : 0;
            if (cible is not null && (Famille(o, cible) || o.AffectionPour(cible.Id) >= 40)) a = Math.Max(a, 0.6);
            if (a > 0) touches[o.Id] = (o, a, false);
        }
        var (faute, culpabilite) = bienfait ? (Gestes.FauteAucune, 0.0) : cible is not null ? (FauteDe(cible, tous), 0.0) : Culpabilite(communaute, tous, maintenant);
        var reputation = cible is null || bienfait ? 0 : Reputation(cible, tous, maintenant);
        object nom = cible ?? (object?)Social?.Communautes.FirstOrDefault(c => c.Id == communaute) ?? Recit.Seul("reg.lesSiens");
        // Un bienfait fait à un fidèle, ou à une communauté qui suit une religion, la fait suivre de ceux qui l'ont vu.
        var (porteuse, par) = bienfait ? Porteuse(cible, communaute, tous, maintenant) : (null, null);
        int merite = 0, injuste = 0;
        foreach (var (o, a, direct) in touches.Values.OrderBy(t => t.Qui.Id))
        {
            if (!o.EstVivant) continue;
            var m = bienfait ? 0 : cible is not null ? Merite(o, cible, reputation) : MeriteCommunaute(o, culpabilite);
            if (!bienfait) { if (m >= 0) merite++; else injuste++; }
            Ressentir(o, geste, maniere, cible, nom, bienfait, m, ampleur * a, direct, annonce, maintenant);
            if (porteuse is null || par is null || o.Id == par.Id) continue;
            if (o.ReligionId == porteuse.Id) o.Ferveur = Math.Min(1, o.Ferveur + 0.05 * a);
            else if (direct && alea.Reel() < 0.3 * ampleur * a * o.Croyance / 100) Convertir(o, porteuse, par, tous, maintenant);
        }
        // Ce qu'on en dit, au journal.
        var porteur = cible ?? vecu[0].Qui;
        var vus = vecu.Count(v => v.Qui.Id != cible?.Id);
        var jugement = Recit.Seul(bienfait ? "reg.jugement.bienfait" : merite >= injuste ? "reg.jugement.merite" : "reg.jugement.injuste");
        Noter(maintenant, TypeEvenement.Croyance, porteur, cible is null ? null : porteur,
            new Recit("reg.temoins", ("m", Gestes.NomManiere(maniere)), ("n", vus), ("savent", touches.Count), ("j", jugement)), annonce);
        if (communaute != 0 && Cultes is { } cultes)
        {
            var texte = (geste, cible) switch
            {
                (_, { } x) => new Recit("reg.souvenir", ("g", Gestes.Nom(geste)), ("x", x), ("m", Gestes.NomManiere(maniere))),
                _ => new Recit("reg.souvenir.siens", ("g", Gestes.Nom(geste)), ("m", Gestes.NomManiere(maniere))),
            };
            cultes.Souvenirs.Add(new SouvenirEntite(communaute, texte.Encoder(), maintenant, bienfait, Math.Round(60 * ampleur, 1), annonce, faute));
        }
    }

    /// <summary>La religion à qui l'on attribue un bienfait : celle de la cible, ou celle de la plupart des siens ; et un fidèle qui la porte.</summary>
    private (Religion?, Humain?) Porteuse(Humain? cible, int communaute, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Cultes is not { } cultes) return (null, null);
        if (cible is not null)
            return cible.ReligionId != 0 && cultes.Get(cible.ReligionId) is { EteinteLe: 0 } r ? (r, cible) : (null, null);
        if (communaute == 0) return (null, null);
        var vivants = tous.Vivants().Where(h => h.EstVivant).ToList();
        if (Majorite(vivants, communaute, maintenant) is not (> 0 and var id) || cultes.Get(id) is not { EteinteLe: 0 } religion) return (null, null);
        return (religion, vivants.Where(h => h.CommunauteId == communaute && h.ReligionId == id).MaxBy(h => h.Ferveur));
    }

    /// <summary>
    /// Ce qu'on dit de <paramref name="x"/> (0 à 1) : les rancunes qu'on lui garde, et les mensonges de charlatan découverts.
    /// </summary>
    private static double Reputation(Humain x, IReadOnlyList<Humain> tous, long maintenant)
    {
        double s = 0;
        foreach (var o in tous.Vivants())
            if (o.EstVivant && o.Id != x.Id)
                foreach (var g in o.Griefs)
                    if (g.ContreId == x.Id) s += g.Intensite / 100 * Gravite(g.Motif);
        if (x.DemasqueLe > 0) s += 1;
        return 1 - Math.Exp(-s / 2);
    }

    /// <summary>Le tort qu'on reproche le plus à <paramref name="x"/> : meurtre ou vol (aucun sinon).</summary>
    private static int FauteDe(Humain x, IReadOnlyList<Humain> tous)
    {
        double meurtre = 0, vol = 0;
        foreach (var o in tous.Vivants())
            if (o.EstVivant)
                foreach (var g in o.Griefs)
                    if (g.ContreId == x.Id)
                    {
                        if (g.Motif == MotifGrief.Meurtre) meurtre += g.Intensite;
                        else if (g.Motif == MotifGrief.Vol) vol += g.Intensite;
                    }
        return meurtre >= 20 && meurtre >= vol ? Gestes.FauteMeurtre : vol >= 20 ? Gestes.FauteVol : Gestes.FauteAucune;
    }

    /// <summary>Poids d'un tort dans le jugement : un meurtre pèse plus qu'un vol ou une rivalité.</summary>
    private static double Gravite(MotifGrief m) => m switch
    {
        MotifGrief.Meurtre => 1,
        MotifGrief.Trahison => 0.8,
        MotifGrief.Coups or MotifGrief.Vol or MotifGrief.Infidelite => 0.6,
        MotifGrief.Moeurs => 0.4,
        _ => 0.3,
    };

    /// <summary>
    /// Le châtiment de <paramref name="x"/> est-il mérité aux yeux de <paramref name="o"/> ? De −1 (injuste) à 1 (mérité),
    /// d'après ses propres rancunes, ce qu'on en dit et son attachement ; sans rien savoir, on penche pour le mérité (Lerner, 1980).
    /// </summary>
    private static double Merite(Humain o, Humain x, double reputation)
    {
        var p = o.Personnalite;
        var autorite = p.Moral.Fondements.Autorite;
        if (o.Id == x.Id) return Math.Clamp(-0.4 + 0.8 * (p.Nevrosisme - 0.3) + 0.5 * reputation, -1, 1);
        double grief = 0;
        foreach (var g in o.Griefs)
            if (g.ContreId == x.Id) grief = Math.Max(grief, g.Intensite / 100 * Gravite(g.Motif));
        var affection = o.AffectionPour(x.Id) / 100;
        var proche = Famille(o, x) || affection >= 0.4;
        var connu = proche || o.LienAvec(x.Id) is not null || o.CommunauteId == x.CommunauteId && x.CommunauteId != 0;
        var m = connu
            ? 1.2 * grief + 0.8 * reputation - 0.25 - 1.2 * Math.Max(0, affection) - (proche ? 0.4 : 0)
            : 0.1 + 0.3 * autorite + 0.5 * reputation;
        return Math.Clamp(m, -1, 1);
    }

    /// <summary>Le châtiment de sa communauté, mérité ou non aux yeux de <paramref name="o"/> (−1 à 1), selon la faute qu'on s'y trouve.</summary>
    private static double MeriteCommunaute(Humain o, double culpabilite)
    {
        var p = o.Personnalite;
        return Math.Clamp(1.4 * culpabilite - 0.4 + 0.3 * (p.Moral.Fondements.Autorite - 0.5) + 0.2 * (p.Nevrosisme - 0.5), -1, 1);
    }

    /// <summary>
    /// La faute qu'une communauté frappée se trouve (0 à 1) : les meurtres, vols, coups et trahisons de ses membres (les rancunes
    /// qu'on leur garde), et la guerre qu'elle a commencée.
    /// </summary>
    private (int Faute, double Culpabilite) Culpabilite(int communaute, IReadOnlyList<Humain> tous, long maintenant)
    {
        var membres = new HashSet<int>();
        foreach (var h in tous.Vivants())
            if (h.EstVivant && h.CommunauteId == communaute) membres.Add(h.Id);
        if (membres.Count == 0) return (Gestes.FauteAucune, 0);
        double meurtres = 0, vols = 0;
        foreach (var o in tous.Vivants())
            if (o.EstVivant)
                foreach (var g in o.Griefs)
                    if (membres.Contains(g.ContreId))
                    {
                        if (g.Motif == MotifGrief.Meurtre) meurtres += g.Intensite / 100;
                        else if (g.Motif is MotifGrief.Vol or MotifGrief.Coups or MotifGrief.Trahison) vols += 0.3 * g.Intensite / 100;
                    }
        var guerre = Social?.Guerres.Any(g => g.A == communaute && g.Fin is null) == true ? 0.4 : 0;
        var c = Math.Min(1, (meurtres + vols) / Math.Max(3, 0.3 * membres.Count) + guerre);
        var faute = c < 0.1 ? Gestes.FauteAucune : guerre > 0 && guerre >= meurtres ? Gestes.FauteGuerre : meurtres >= vols ? Gestes.FauteMeurtre : Gestes.FauteVol;
        return (faute, c);
    }

    /// <summary>
    /// Ce que <paramref name="o"/> ressent de ce qu'il a vu ou appris (force <paramref name="a"/>, 0 à 1) : sa croyance monte (même
    /// l'incrédule, un peu, devant un signe), puis amour, crainte ou révolte selon le jugement <paramref name="m"/> et son caractère.
    /// </summary>
    private void Ressentir(Humain o, GesteEntite geste, ManiereGeste maniere, Humain? cible, object nom, bool bienfait, double m, double a, bool direct,
        long cause, long maintenant)
    {
        var p = o.Personnalite;
        var autorite = p.Moral.Fondements.Autorite;
        var soi = cible?.Id == o.Id;
        var proche = cible is not null && !soi && (Famille(o, cible) || o.AffectionPour(cible.Id) >= 40);
        var vu = direct ? new Recit("cro.aVu", ("h", o), ("m", Gestes.NomManiere(maniere))) : Recit.Seul("cro.raconte");
        Croyances.Ajouter(o, (direct ? 10 : 4) * a, vu, cause, maintenant, signe: direct);
        // Le sentiment suit la croyance : qui n'y croit guère ne ressent qu'à moitié.
        a *= 0.4 + 0.6 * o.Croyance / 100;
        if (bienfait)
        {
            Regards.Ajouter(o, SentimentInvisible.Amour, 15 * a * (0.6 + 0.8 * p.Agreabilite), soi ? new Recit("reg.beniSoi", ("h", o)) : new Recit("reg.beni", ("x", nom)), cause, maintenant, direct);
            if (direct && a >= 0.4) Confier(o, TypeIntime.Croyance, cible?.Id ?? 0, Recits.Recit.Seul(soi ? "js.regard.bienfait.soi" : "js.regard.bienfait"), 30 + 40 * a, cause, maintenant, "regard:bienfait");
            return;
        }
        if (m >= 0)
        {
            Regards.Ajouter(o, SentimentInvisible.Crainte, 15 * a * (0.4 + 0.6 * m) * (0.6 + 0.5 * p.Nevrosisme + 0.5 * autorite),
                soi ? new Recit("reg.puniSoi", ("h", o)) : new Recit("reg.puni", ("x", nom)), cause, maintenant, direct);
            if (direct && a >= 0.4)
                Confier(o, TypeIntime.Peur, cible?.Id ?? 0, Recits.Recit.Seul(soi ? "js.regard.chatiment.soi" : "js.regard.chatiment"),
                    30 + 40 * a, cause, maintenant, "regard:chatiment");
            return;
        }
        var injuste = -m;
        Regards.Ajouter(o, SentimentInvisible.Crainte, 8 * a * (0.6 + 0.8 * p.Nevrosisme), new Recit("reg.frappe", ("x", nom)), cause, maintenant, direct);
        Regards.Ajouter(o, SentimentInvisible.Amour, -15 * a * injuste * (1.3 - 0.6 * autorite), new Recit("reg.frappeInjuste", ("x", nom)), cause, maintenant);
        var revolte = 20 * a * injuste * Math.Max(0.1, 1.6 - p.Agreabilite - 0.6 * autorite) * (proche || soi ? 1.5 : 1);
        Regards.Ajouter(o, SentimentInvisible.Revolte, revolte, new Recit("reg.frappeInjuste", ("x", nom)), cause, maintenant);
        if (direct && a >= 0.4 || proche)
            Confier(o, TypeIntime.Question, cible?.Id ?? 0, cible is null || soi
                ? Recits.Recit.Seul("js.regard.injuste.nous")
                : new Recit("js.regard.injuste", ("x", cible)), 30 + 50 * a, cause, maintenant, "regard:injuste");
    }

    /// <summary>
    /// Chaque jour : le regard de chacun sur l'invisible se règle sur les siens et retombe sans signe ; les souvenirs des
    /// communautés s'effacent. Un changement d'une dizaine est noté au journal.
    /// </summary>
    public void Regarder(IReadOnlyList<Humain> humains, long maintenant)
    {
        _maintenant = maintenant;
        var moyennes = humains.Vivants().Where(h => h.EstVivant && h.CommunauteId != 0 && !h.EstEnfant(maintenant))
            .GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => (g.Average(h => h.Amour * (1 - h.Revolte / 100)), g.Average(h => h.Crainte)));
        var souvenirs = new Dictionary<int, (double Bienfaits, double Chatiments)>();
        if (Cultes is { Souvenirs.Count: > 0 } cultes)
        {
            var f = Math.Pow(0.5, 1 / (Regards.DemiVieSouvenir * Calendrier.JoursParAn));
            for (var i = cultes.Souvenirs.Count - 1; i >= 0; i--)
            {
                var s = cultes.Souvenirs[i];
                if (s.Force * f < 5 || !moyennes.ContainsKey(s.CommunauteId))
                {
                    cultes.Souvenirs.RemoveAt(i);
                    continue;
                }
                cultes.Souvenirs[i] = s = s with { Force = s.Force * f };
                var (b, c) = souvenirs.GetValueOrDefault(s.CommunauteId);
                souvenirs[s.CommunauteId] = s.Bienfait ? (b + s.Force, c) : (b, c + s.Force);
            }
        }
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant) continue;
            Regards.Journee(h, humains, moyennes, souvenirs, maintenant);
            foreach (var (_, texte) in Regards.Changements(h))
                Noter(maintenant, TypeEvenement.Croyance, h, null, texte, h.RegardCause);
            (h.RegardPoussee, h.RegardRaison, h.RegardCause) = (0, null, 0);
        }
    }
}
