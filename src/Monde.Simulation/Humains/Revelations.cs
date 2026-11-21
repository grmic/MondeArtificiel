using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>À qui l'Entité a révélé quelque chose : un humain (dans un dialogue), une communauté, ou le Monde entier.</summary>
public enum PorteeRevelation : byte
{
    Humain = 0,
    Communaute = 1,
    Monde = 2,
}

/// <summary>
/// Ce que l'Entité a révélé (0.42.9) : l'origine du Monde, ce qu'elle est, ce qui viendra… <see cref="Destinataire"/> : l'humain
/// ou la communauté (0 : le Monde). <see cref="Repandue"/> : les communautés où plus de la moitié des membres la savent ;
/// <see cref="PerdueLe"/> : quand plus personne ne s'en est souvenu (0 : on s'en souvient encore). <see cref="Auteur"/> : 0 le
/// joueur, 1 l'Entité jouée par le LLM.
/// </summary>
public sealed class Revelation
{
    public int Id { get; set; }
    public long Le { get; set; }
    public string Texte { get; set; } = "";
    public PorteeRevelation Portee { get; set; }
    public int Destinataire { get; set; }
    public ModeParole Mode { get; set; }
    public long EvenementId { get; set; }
    public int Auteur { get; set; }
    public List<int> Repandue { get; set; } = [];
    public long PerdueLe { get; set; }

    public Revelation Copier() => new()
    {
        Id = Id, Le = Le, Texte = Texte, Portee = Portee, Destinataire = Destinataire, Mode = Mode, EvenementId = EvenementId, Auteur = Auteur,
        Repandue = [.. Repandue], PerdueLe = PerdueLe,
    };
}

/// <summary>
/// Une révélation qu'un humain garde en mémoire (0.42.9). <see cref="Fidelite"/> : de 1 (mot pour mot) à 0 (presque rien) ;
/// <see cref="DeQui"/> : celui qui la lui a racontée (0 : l'Entité elle-même) ; <see cref="RaconteLe"/> : la dernière fois qu'il
/// l'a dite ou entendue ; <see cref="Croit"/> : il la tient pour vraie.
/// </summary>
public sealed record Connue(int Id, double Fidelite, int DeQui, long Le, long RaconteLe, bool Croit);

/// <summary>
/// Les révélations de l'Entité restent dans les mémoires (0.42.9). Celui qui l'a reçue la garde ; il la raconte à ses enfants et à
/// ses proches, plus volontiers s'il est expansif et s'il y croit ; chaque récit la déforme un peu, d'autant plus que la mémoire
/// de l'un et de l'autre est faible : c'est la reproduction en série de Bartlett (1932), où un récit se simplifie et se transforme
/// de bouche en bouche. Ce qu'on ne redit à personne s'efface peu à peu (Ebbinghaus, 1885 ; le rappel répété fixe le souvenir :
/// Roediger et Karpicke, 2006) ; une tradition orale ne dure que si on la raconte (Vansina, 1985).
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Sa mémoire, de 0 à 1 (0,5 pour un score de 100).</summary>
    private static double MemoireDe(Humain h) => Math.Clamp((h.Personnalite.Intelligence.Memoire - 55) / 90, 0, 1);

    /// <summary>
    /// Fidélité de ce qu'il retient d'une révélation reçue de l'Entité elle-même : une apparition marque, une voix aussi, un rêve
    /// s'efface au réveil (on oublie l'essentiel d'un rêve en quelques minutes : Goodenough, 1991) ; la bonne mémoire y aide.
    /// </summary>
    private static double FideliteRecue(Humain h, ModeParole mode)
    {
        var m = MemoireDe(h);
        var f = mode switch
        {
            ModeParole.Apparition => 0.9 + 0.1 * m,
            ModeParole.Voix => 0.8 + 0.15 * m,
            ModeParole.Reve => 0.45 + 0.35 * m,
            _ => 0.6 + 0.3 * m,
        };
        return Math.Round(Math.Clamp(f, 0.2, 1), 3);
    }

    /// <summary>
    /// Il apprend (ou entend de nouveau) la révélation <paramref name="r"/> : il la garde, ou ravive ce qu'il en savait. Une
    /// pensée lui en reste au jardin secret. Vrai s'il ne la savait pas.
    /// </summary>
    internal bool Apprendre(Humain h, Revelation r, bool croit, double fidelite, int deQui, long cause, long maintenant)
    {
        var k = h.Revelations.FindIndex(c => c.Id == r.Id);
        if (k >= 0)
        {
            var c = h.Revelations[k];
            h.Revelations[k] = c with { Fidelite = Math.Max(c.Fidelite, fidelite), RaconteLe = maintenant, Croit = c.Croit || croit };
            return false;
        }
        h.Revelations.Add(new Connue(r.Id, fidelite, deQui, maintenant, maintenant, croit));
        var p = h.Personnalite;
        Confier(h, croit ? TypeIntime.Croyance : TypeIntime.Question, 0,
            new Recit(croit ? "js.revelation.croit" : "js.revelation.doute", ("t", Court(r.Texte))),
            (croit ? 35 + 40 * p.Transcendance : 20 + 30 * p.Ouverture) * (0.5 + 0.5 * fidelite), cause, maintenant, $"revelation:{r.Id}");
        return true;
    }

    /// <summary>Le début d'une révélation, pour une pensée ou une ligne du journal.</summary>
    internal static string Court(string texte) => texte.Length <= 160 ? texte : texte[..157].TrimEnd() + "…";

    /// <summary>
    /// L'Entité révèle <paramref name="r"/> à <paramref name="h"/> au cours d'un dialogue : il la garde s'il l'a comprise, plus
    /// fidèlement qu'en rêve s'il l'a entendue ou vue. Rend la ligne du journal.
    /// </summary>
    public long Reveler(Humain h, Revelation r, bool croit, long cause, long maintenant)
    {
        _maintenant = maintenant;
        var f = FideliteRecue(h, r.Mode);
        var id = NoterEntite(maintenant, TypeEvenement.Parole, h, null,
            new Recit(croit ? "rev.recue" : "rev.recueDoute", ("h", h), ("t", Court(r.Texte)), ("m", Recit.Seul($"modeParole.{r.Mode}"))), cause, significatif: true);
        r.EvenementId = r.EvenementId == 0 ? id : r.EvenementId;
        Apprendre(h, r, croit, f, 0, id, maintenant);
        return id;
    }

    /// <summary>
    /// L'Entité révèle <paramref name="r"/> à plusieurs à la fois (une communauté, le Monde) : chacun la comprend ou non, y croit ou
    /// non (voir <see cref="Comprehension"/> et <see cref="Foi"/>), et garde ce qu'il a compris. Rend combien l'ont comprise, et
    /// combien y croient.
    /// </summary>
    /// <summary>Tous les humains (morts compris, rangés par Id), le temps de vivre les révélations : pour l'ordre qu'elles contiennent.</summary>
    private IReadOnlyList<Humain>? _tousRevelations;

    /// <summary>
    /// L'ordre que contient la révélation <paramref name="r"/> (0.45.21) : ceux qui l'ont comprise et y croient l'entendent aussi
    /// comme « Allez vers <paramref name="vers"/> », et chacun décide d'y aller, d'hésiter ou de refuser. Rend ce que chacun en a fait.
    /// </summary>
    public List<(int Humain, Recit Resultat)> OrdonnerRevele(IReadOnlyList<Humain> ecoutent, Revelation r, Humain vers, IReadOnlyList<Humain> tous,
        long maintenant)
    {
        _maintenant = maintenant;
        var resultats = new List<(int, Recit)>();
        foreach (var h in ecoutent)
        {
            if (!h.EstVivant || h.Id == vers.Id || h.Revelations.Find(c => c.Id == r.Id) is not { Croit: true }) continue;
            resultats.Add((h.Id, Parler(h, Parole.VaVers, r.Mode, vers, 0, tous, maintenant, ensemble: true, dialogue: r.EvenementId,
                croyait: true, monde: true, dejaCompris: true)));
        }
        return resultats;
    }

    public (int Compris, int Crus) RevelerA(IReadOnlyList<Humain> ecoutent, Revelation r, long cause, long maintenant)
    {
        _maintenant = maintenant;
        int compris = 0, crus = 0;
        foreach (var h in ecoutent)
        {
            if (!h.EstVivant || h.EstBebe(maintenant)) continue;
            if (alea.Reel() >= Comprehension(h, r.Mode, null, maintenant)) continue;
            compris++;
            var croit = alea.Reel() < Foi(h, r.Mode, ensemble: ecoutent.Count > 1, maintenant);
            if (croit)
            {
                crus++;
                Eprouver(h, r.Mode, cause, maintenant);
            }
            Apprendre(h, r, croit, FideliteRecue(h, r.Mode), 0, cause, maintenant);
        }
        return (compris, crus);
    }

    /// <summary>
    /// Chaque jour : ceux qui savent une révélation la racontent parfois (à un enfant d'abord, sinon à un proche tout près), elle
    /// se déforme en passant, et s'efface chez qui ne l'a ni dite ni entendue depuis longtemps. Quand plus de la moitié d'une
    /// communauté la sait, c'est noté ; quand plus personne ne s'en souvient, aussi. Rien ne se passe (ni hasard tiré) tant que
    /// l'Entité n'a rien révélé.
    /// </summary>
    public void VivreLesRevelations(IReadOnlyList<Humain> humains, IReadOnlyList<Communaute> communautes, long maintenant)
    {
        if (Cultes is not { Revelations.Count: > 0 } cultes) return;
        _maintenant = maintenant;
        _tousRevelations = humains;
        var vivants = humains.Vivants().Where(h => h.EstVivant).ToList();
        foreach (var h in vivants)
        {
            if (h.Revelations.Count == 0) continue;
            Transmettre(h, cultes, vivants, maintenant);
            for (var k = h.Revelations.Count - 1; k >= 0; k--)
            {
                var c = h.Revelations[k];
                if (cultes.Revelation(c.Id) is not { } r)
                {
                    h.Revelations.RemoveAt(k);
                    continue;
                }
                // L'oubli : ce qu'on ne s'est pas redit depuis longtemps pâlit, plus vite chez qui a peu de mémoire ; ce qu'on croit
                // et qui touche l'âme tient mieux.
                var m = MemoireDe(h);
                var tenue = 40 + 160 * m + (c.Croit ? 120 * h.Personnalite.Transcendance : 0);
                if ((maintenant - c.RaconteLe) / (double)Calendrier.SecondesParJour > tenue)
                {
                    var f = c.Fidelite * (1 - 0.006 * (1.4 - m));
                    if (f < 0.2)
                    {
                        h.Revelations.RemoveAt(k);
                        Jardin.Retirer(h, $"revelation:{r.Id}");
                        continue;
                    }
                    h.Revelations[k] = c = c with { Fidelite = Math.Round(f, 4) };
                }
                Raconter(h, c, r, vivants, maintenant);
            }
        }
        Repandre(cultes, vivants, communautes, maintenant);
    }

    /// <summary>
    /// Il la raconte, parfois : l'expansif plus que le réservé, celui qui y croit bien plus que celui qui en doute. L'auditeur est
    /// d'abord un de ses enfants (4 à 15 ans) tout près, sinon un proche de sa communauté qui l'aime et ne la sait pas encore (ou
    /// moins bien). Ce que l'auditeur en retient dépend de la mémoire de l'un et de l'autre ; il y croit selon sa croyance et
    /// l'estime qu'il porte au conteur. Le dire l'entretient chez celui qui raconte.
    /// </summary>
    private void Raconter(Humain h, Connue c, Revelation r, IReadOnlyList<Humain> vivants, long maintenant)
    {
        if (h.EstEnfant(maintenant) && h.AgeEnAnnees(maintenant) < 8) return;
        var p = h.Personnalite;
        var envie = (0.03 + 0.09 * p.Extraversion) * (c.Croit ? 0.6 + 0.8 * h.Croyance / 100 : 0.35) * (0.5 + 0.5 * c.Fidelite);
        if (alea.Reel() >= envie) return;
        Humain? enfant = null, proche = null;
        var (pire, pireProche) = (2.0, 2.0);
        foreach (var o in vivants)
        {
            if (o.Id == h.Id || Distance(o.X, o.Y, h.X, h.Y) > 2) continue;
            var sait = o.Revelations.Find(x => x.Id == r.Id);
            var manque = sait is null ? 0 : sait.Fidelite;
            if (sait is not null && sait.Fidelite >= c.Fidelite) continue;
            if ((o.MereId == h.Id || o.PereId == h.Id) && o.AgeEnAnnees(maintenant) is >= 4 and < 16)
            {
                if (manque < pire) (enfant, pire) = (o, manque);
            }
            else if (!o.EstEnfant(maintenant) && (h.CommunauteId > 0 && o.CommunauteId == h.CommunauteId || o.Id == h.PartenaireId)
                     && o.AffectionPour(h.Id) >= 20 && manque < pireProche)
                (proche, pireProche) = (o, manque);
        }
        var auditeur = enfant ?? proche;
        if (auditeur is null) return;
        RaconterA(h, c, r, auditeur, maintenant);
    }

    /// <summary>Il la raconte à <paramref name="auditeur"/> : ce qu'il en garde, s'il y croit, la ligne du journal (Bartlett, 1932).</summary>
    private void RaconterA(Humain h, Connue c, Revelation r, Humain auditeur, long maintenant)
    {
        var (mh, ma) = (MemoireDe(h), MemoireDe(auditeur));
        // De bouche en bouche : à chaque récit, une part se perd ou se transforme (Bartlett, 1932).
        var garde = Math.Clamp(0.7 + 0.12 * mh + 0.1 * ma + 0.08 * (alea.Reel() - 0.5), 0.55, 0.97);
        var fidelite = Math.Round(c.Fidelite * garde, 4);
        var estime = Math.Clamp(0.5 + auditeur.AffectionPour(h.Id) / 200, 0.2, 1);
        var enfance = 1 - Math.Clamp((auditeur.AgeEnAnnees(maintenant) - 6) / 10, 0, 1);
        var croit = alea.Reel() < Math.Clamp((c.Croit ? 0.25 : 0.05) + 0.5 * auditeur.Croyance / 100 * estime + 0.3 * enfance
                                             - 0.25 * (Croyances.Analytique(auditeur) - 0.5), 0.02, 0.97);
        var id = Noter(maintenant, TypeEvenement.Parole, h, auditeur, new Recit(croit ? "rev.raconte" : "rev.raconteDoute", ("h", h), ("x", auditeur),
            ("t", Court(r.Texte))), r.EvenementId);
        var nouvelle = Apprendre(auditeur, r, croit, fidelite, h.Id, id, maintenant);
        // L'ordre qu'elle contient (0.45.21), entendu de bouche en bouche : on y obéit moins volontiers qu'à l'Entité elle-même,
        // d'autant moins qu'on estime peu le conteur et que le récit s'est déformé.
        if (nouvelle && croit && _tousRevelations is { } tous && OrdresReveles.VaVers(r.Texte, tous, auditeur.CommunauteId) is { } vers
            && vers.Id != auditeur.Id && alea.Reel() < (0.25 + 0.45 * estime) * fidelite)
            Parler(auditeur, Parole.VaVers, r.Mode, vers, 0, tous, maintenant, ensemble: true, dialogue: id, croyait: true, dejaCompris: true);
        var k = h.Revelations.FindIndex(x => x.Id == r.Id);
        if (k >= 0) h.Revelations[k] = h.Revelations[k] with { RaconteLe = maintenant };
    }

    /// <summary>
    /// L'Entité lui a demandé de transmettre ce qu'elle lui a révélé (0.44.11) : chaque jour où il s'y tient, il la raconte, l'un
    /// après l'autre, aux membres de sa communauté qui sont près du camp et ne la savent pas (ou moins bien), à ses proches s'il n'a
    /// pas de communauté. Combien par jour : selon son entrain (extraversion) et sa persévérance (conscience, endurance), moitié
    /// moins s'il n'y obéit qu'à moitié. C'est fait quand il n'y a plus personne à qui la dire ; le temps passé, c'est fait en partie
    /// ou pas du tout. Il transmet la dernière révélation qu'il a reçue, celle que l'Entité vient de lui confier.
    /// </summary>
    private void Transmettre(Humain h, Cultes cultes, IReadOnlyList<Humain> vivants, long maintenant)
    {
        for (var k = 0; k < h.Impulsions.Count; k++)
        {
            var i = h.Impulsions[k];
            if (i.Parole != Parole.Transmets || i.Reponse is not (Reponse.Acceptee or Reponse.AMoitie) || i.AgirLe > maintenant
                || i.AgirLe == long.MaxValue)
                continue;
            var c = h.Revelations.OrderByDescending(x => x.Le).First();
            if (cultes.Revelation(c.Id) is not { } r) continue;
            if (maintenant > i.Jusqua)
            {
                // Le temps est passé : il en a parlé à quelques-uns, ou à personne.
                h.Impulsions[k] = i with { Reponse = i.Param > 0 ? Reponse.AMoitie : Reponse.Abandonnee, AgirLe = long.MaxValue };
                NoterEntite(maintenant, TypeEvenement.Parole, h, null, i.Param > 0
                    ? new Recit("dem.fait.transmisEnPartie", ("h", h), ("n", i.Param), ("t", Court(r.Texte)))
                    : new Recit("dem.fait.pasTransmis", ("h", h), ("t", Court(r.Texte))), i.EvenementId);
                Jardin.Retirer(h, $"entite:{(int)i.Parole}");
                continue;
            }
            var p = h.Personnalite;
            var perseverance = 0.6 * p.Conscience + 0.4 * p.Endurance;
            var parJour = Math.Max(1, (int)Math.Round((1 + 5 * p.Extraversion) * (0.5 + perseverance) * (i.Reponse == Reponse.AMoitie ? 0.5 : 1)
                                                      * (0.7 + 0.6 * alea.Reel())));
            var auditeurs = vivants.Where(o => o.Id != h.Id && o.AgeEnAnnees(maintenant) >= 4 && Distance(o.X, o.Y, h.X, h.Y) <= 6
                                               && (h.CommunauteId > 0 ? o.CommunauteId == h.CommunauteId
                                                   : o.Id == h.PartenaireId || o.MereId == h.Id || o.PereId == h.Id || h.AffectionPour(o.Id) >= 20)
                                               && (o.Revelations.Find(x => x.Id == r.Id) is not { } sait || sait.Fidelite < c.Fidelite))
                .OrderBy(o => Distance(o.X, o.Y, h.X, h.Y)).ThenBy(o => o.Id).ToList();
            if (auditeurs.Count == 0)
            {
                h.Impulsions[k] = i with { Reponse = i.Reponse == Reponse.AMoitie ? Reponse.AMoitie : Reponse.Accomplie, AgirLe = long.MaxValue,
                    Jusqua = Math.Min(i.Jusqua, maintenant) };
                NoterEntite(maintenant, TypeEvenement.Parole, h, null, i.Param > 0
                    ? new Recit("dem.fait.transmis", ("h", h), ("n", i.Param), ("t", Court(r.Texte)))
                    : new Recit("dem.fait.personneATransmettre", ("h", h), ("t", Court(r.Texte))), i.EvenementId);
                Jardin.Retirer(h, $"entite:{(int)i.Parole}");
                continue;
            }
            foreach (var o in auditeurs.Take(parJour))
            {
                c = h.Revelations.Find(x => x.Id == r.Id)!;
                RaconterA(h, c, r, o, maintenant);
            }
            h.Impulsions[k] = i with { Param = i.Param + Math.Min(parJour, auditeurs.Count) };
        }
    }

    /// <summary>Note quand une révélation est sue de plus de la moitié d'une communauté, et quand plus personne ne s'en souvient.</summary>
    private void Repandre(Cultes cultes, IReadOnlyList<Humain> vivants, IReadOnlyList<Communaute> communautes, long maintenant)
    {
        foreach (var r in cultes.Revelations)
        {
            if (r.PerdueLe != 0) continue;
            var savent = 0;
            Dictionary<int, (int Savent, int Membres)>? parCommunaute = null;
            foreach (var h in vivants)
            {
                var sait = h.Revelations.Exists(c => c.Id == r.Id);
                if (sait) savent++;
                if (h.CommunauteId <= 0 || h.EstBebe(maintenant)) continue;
                parCommunaute ??= [];
                var (s, m) = parCommunaute.GetValueOrDefault(h.CommunauteId);
                parCommunaute[h.CommunauteId] = (s + (sait ? 1 : 0), m + 1);
            }
            if (savent == 0)
            {
                r.PerdueLe = maintenant;
                NoterAuMonde(maintenant, new Recit("rev.perdue", ("t", Court(r.Texte))), null, r.EvenementId);
                continue;
            }
            foreach (var (communaute, (s, m)) in (parCommunaute ?? []).OrderBy(x => x.Key))
            {
                if (m < 3 || s * 2 <= m || r.Repandue.Contains(communaute)) continue;
                r.Repandue.Add(communaute);
                if (communaute > communautes.Count) continue;
                NoterAuMonde(maintenant, new Recit("rev.repandue", ("c", communautes[communaute - 1]), ("t", Court(r.Texte))), null, r.EvenementId, communaute);
            }
        }
    }
}
