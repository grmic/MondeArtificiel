using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les religions : des croyances que plusieurs partagent, avec leurs préceptes, leurs rites et ceux qui les portent.
/// Aucune n'est écrite d'avance. Elles naissent :
/// <list type="bullet">
/// <item>autour d'un prophète que l'Entité a désigné (« Porte Ma parole ») : il transmet ce qu'Elle lui a dit, parfois de
/// travers ;</item>
/// <item>autour d'un prophète venu de lui-même : un visionnaire sincère, habité par ce qu'il a vu, ou un imposteur qui y
/// trouve pouvoir et nourriture. Ces mouvements naissent surtout dans les temps de crise (Wallace, 1956) ;</item>
/// <item>du culte des morts, quand plusieurs rêvent de leurs défunts (Tylor, 1871 ; il existe dans une partie seulement
/// des sociétés de chasseurs-cueilleurs : Peoples, Duda et Marlowe, 2016) ;</item>
/// <item>d'une séparation, quand un fidèle charismatique entraîne ceux qui l'aiment.</item>
/// </list>
/// <para>
/// On se convertit surtout par ses proches : on rejoint ceux qu'on aime avant de partager leur foi (Stark et Lofland, 1965).
/// On croit celui qui vit ce qu'il prêche (Henrich, 2009) : l'imposteur convainc moins. Les rites partagés, d'autant plus
/// qu'ils coûtent, soudent un groupe (Sosis et Bressler, 2003). Une prophétie démentie renforce les plus fervents et
/// détourne les autres (Festinger et al., 1956). Les sacrifices humains, rares, frappent surtout les captifs et les
/// faibles, et servent le pouvoir de ceux qui les ordonnent (Watts et al., 2016). Les prêtres, gardiens des rites,
/// n'apparaissent qu'avec des groupes assez grands (Peoples et Marlowe, 2012).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Dernière saison où le rassemblement d'une religion dans une communauté a été noté au journal.</summary>
    private readonly Dictionary<(int Religion, int Communaute), long> _rites = [];

    /// <summary>Adhérents à partir desquels un fidèle devient gardien des rites (prêtre) dans une communauté.</summary>
    private const int FidelesPourUnPretre = 12;

    private static readonly long Semaine = 7 * Calendrier.SecondesParJour;

    /// <summary>
    /// Chaque jour : on prie et l'on voit ce qu'il advient des prières ; la ferveur suit la croyance ; des prophètes se
    /// lèvent et prêchent ; les malheurs annoncés viennent ou non. Chaque semaine : les enfants suivent la foi de leurs
    /// parents, les fidèles se rassemblent, les préceptes changent avec ce que vivent les fidèles, des prêtres apparaissent,
    /// des religions se séparent ou s'éteignent.
    /// </summary>
    public void Religions(IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Cultes is null) return;
        _maintenant = maintenant;
        _tous = tous;
        var vivants = tous.Vivants().Where(h => h.EstVivant && !h.EstBebe(maintenant)).ToList();
        Prier(vivants, tous, maintenant);
        Resoudre(tous, maintenant);
        Ferveurs(vivants, maintenant);
        ProphetesSpontanes(vivants, tous, maintenant);
        Precher(vivants, tous, maintenant);
        Propheties(vivants, tous, maintenant);
        if (maintenant / Calendrier.SecondesParJour % 7 != 0) return;
        Heriter(tous, maintenant);
        CulteDesMorts(vivants, maintenant);
        var groupes = vivants.Where(h => h.ReligionId != 0 && h.CommunauteId != 0 && h.CaptifDe == 0)
            .GroupBy(h => (h.ReligionId, h.CommunauteId)).OrderBy(g => g.Key).ToList();
        foreach (var g in groupes) Rite(Cultes.Religions[g.Key.ReligionId - 1], Social!.Communautes[g.Key.CommunauteId - 1], [.. g], tous, maintenant);
        foreach (var g in groupes) Pretre(Cultes.Religions[g.Key.ReligionId - 1], Social!.Communautes[g.Key.CommunauteId - 1], [.. g], maintenant);
        Opposer(vivants, tous, maintenant);
        Preceptes(vivants, tous, maintenant);
        foreach (var g in groupes) Sacrifier(Cultes.Religions[g.Key.ReligionId - 1], Social!.Communautes[g.Key.CommunauteId - 1], [.. g], vivants, tous, maintenant);
        Schismes(vivants, tous, maintenant);
        Mefiance(vivants, maintenant);
        Bilan(vivants, maintenant);
    }

    // ───────────── Naître, adhérer, se détacher ─────────────

    private string TexteDogme(Precepte p, int param) => p switch
    {
        Precepte.Interdit when param >= 1 && Flore is not null && param <= Flore.Especes.Count => $"Il ne faut pas manger {Flore.Get(param).Nom}.",
        Precepte.GuerreSainte when param > 0 && Social is not null && param <= Social.Communautes.Count
            => $"Il faut combattre la {Social.Communautes[param - 1].Nom.ToLowerInvariant()}.",
        Precepte.Chatiment when param == 1 => "L'invisible punit ceux qui tuent.",
        Precepte.Chatiment => "L'invisible punit ceux qui volent.",
        _ => Cultes.Dire(p),
    };

    /// <summary>Le précepte que tire un prophète d'une parole de l'Entité qu'il a crue (null : elle n'enseigne rien).</summary>
    private static (Precepte P, int Param)? PrecepteDe(Parole parole, int param) => parole switch
    {
        Parole.Partage => (Precepte.Partager, 0),
        Parole.PrendsSoin or Parole.Console => (Precepte.Soigner, 0),
        Parole.Pardonne or Parole.NeTeVengePas => (Precepte.Pardonner, 0),
        Parole.NeVaPasALaGuerre => (Precepte.Paix, 0),
        Parole.DefendsLesTiens => (Precepte.Defendre, 0),
        Parole.CatastropheArrive => (Precepte.Prophetie, 0),
        Parole.PlanteDangereuse => (Precepte.Interdit, param),
        Parole.JeSuisCelleQuiVousAFaits or Parole.PorteMaParole => (Precepte.Createur, 0),
        Parole.Frappe or Parole.Tue => (Precepte.Chatiment, 1),
        _ => null,
    };

    /// <summary>Ce que devient un précepte mal compris ou changé en passant de bouche en bouche.</summary>
    private static (Precepte P, int Param) Deformer(Precepte p, int param) => p switch
    {
        Precepte.Partager => (Precepte.Offrandes, 0),
        Precepte.Soigner => (Precepte.Partager, 0),
        Precepte.Pardonner => (Precepte.Paix, 0),
        Precepte.Paix => (Precepte.Pardonner, 0),
        Precepte.Defendre => (Precepte.GuerreSainte, 0),
        Precepte.Chatiment => (Precepte.Colere, 0),
        _ => (p, param),
    };

    /// <summary>Part des paroles qu'un prophète transmet de travers : moins il a de mots, plus il déforme (estimation).</summary>
    private static double ChanceDeDeformer(Humain h) => 0.35 * (1 - Math.Clamp((h.Personnalite.Intelligence.Verbale - 55) / 90, 0, 1));

    /// <summary>Une religion naît autour de <paramref name="f"/>, avec son nom dans la langue des siens.</summary>
    private Religion Fonder(Humain f, NaissanceReligion naissance, List<Dogme> dogmes, string annonce, long cause, long maintenant, int parent = 0)
    {
        var cultes = Cultes!;
        var c = Social?.CommunauteDe(f);
        var sons = Social?.SonsDe(c) ?? Sons.Tirer(alea);
        var nom = Majuscule(Langues.Forger(sons, alea, 2));
        while (cultes.Religions.Exists(r => r.Nom == nom)) nom += Langues.Forger(sons, alea, 1);
        var sens = naissance switch
        {
            NaissanceReligion.Ancetres => "ceux qui veillent",
            NaissanceReligion.Schisme => $"la voie de {f.Nom}",
            _ => $"la parole de {f.Nom}",
        };
        var id = Noter(maintenant, TypeEvenement.Religion, f, null, $"{annonce} Ainsi naît le {nom} (« {sens} »).", cause, significatif: true);
        var religion = new Religion
        {
            Id = cultes.Religions.Count + 1, Nom = nom, Sens = sens, FondateurId = f.Id, FondeeLe = maintenant, Naissance = naissance,
            EvenementId = id, ParentId = parent, CommunauteId = c?.Id ?? 0,
            Dogmes = [.. dogmes.Select(d => d with { EvenementId = d.EvenementId == 0 ? id : d.EvenementId })],
        };
        cultes.Religions.Add(religion);
        Adherer(f, religion, 0.9, maintenant);
        return religion;
    }

    private static void Adherer(Humain h, Religion r, double ferveur, long maintenant)
    {
        if (h.ReligionId != r.Id && h.Role == RoleReligieux.Pretre) (h.Role, h.RoleDepuis) = (RoleReligieux.Aucun, 0);
        h.ReligionId = r.Id;
        h.Ferveur = Math.Clamp(ferveur, 0.05, 1);
        h.ReligionDepuis = maintenant;
    }

    /// <summary>Ajoute un précepte à une religion (s'il n'y est pas déjà). Vrai s'il est nouveau.</summary>
    private bool AjouterDogme(Religion r, Precepte p, int param, OrigineDogme origine, Humain? auteur, long cause, long maintenant)
    {
        if (r.Dogmes.Exists(d => d.Precepte == p && d.Parametre == param)) return false;
        // Un seul lieu sacré compte : le premier.
        if (p == Precepte.LieuSacre && r.Enseigne(Precepte.LieuSacre)) return false;
        var texte = TexteDogme(p, param);
        var grave = p is Precepte.SacrificeHumain or Precepte.GuerreSainte or Precepte.Colere;
        var id = auteur is null
            ? cause
            : Noter(maintenant, TypeEvenement.Religion, auteur, null, origine switch
            {
                OrigineDogme.Coincidence or OrigineDogme.Catastrophe => $"Les fidèles du {r.Nom} y voient un signe, et croient désormais : « {texte} »",
                _ => $"{auteur.Nom} enseigne aux fidèles du {r.Nom} : « {texte} »",
            }, cause, significatif: grave);
        r.Dogmes.Add(new Dogme(p, param, origine, texte, maintenant, id));
        return true;
    }

    /// <summary>
    /// Chaque jour, la ferveur d'un fidèle se règle sur sa croyance en l'invisible (demi-vie de deux ans environ) ; trop
    /// faible, il se détache de sa religion. Le prophète, lui, ne s'en détache pas.
    /// </summary>
    private void Ferveurs(List<Humain> vivants, long maintenant)
    {
        var pas = 1 - Math.Pow(0.5, 1 / 720.0);
        foreach (var h in vivants)
        {
            if (h.ReligionId == 0) continue;
            var cible = 0.1 + 0.8 * h.Croyance / 100;
            h.Ferveur = Math.Clamp(h.Ferveur + (cible - h.Ferveur) * pas, 0, 1);
            if (h.Role == RoleReligieux.Prophete) h.Ferveur = Math.Max(h.Ferveur, 0.5);
            if (h.Ferveur >= 0.08) continue;
            var r = Cultes!.Religions[h.ReligionId - 1];
            Noter(maintenant, TypeEvenement.Religion, h, null, $"{h.Nom} se détache du {r.Nom} : {Il(h)} n'y croit plus guère.");
            h.ReligionId = 0;
            h.Ferveur = 0;
            if (h.Role == RoleReligieux.Pretre) (h.Role, h.RoleDepuis) = (RoleReligieux.Aucun, 0);
        }
    }

    /// <summary>Les enfants suivent la religion de leur mère, ou à défaut de leur père, avec la moitié de leur ferveur.</summary>
    private void Heriter(IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.ReligionId != 0 || h.AgeEnAnnees(maintenant) >= 14) continue;
            var parent = Vivant(tous, h.MereId) is { ReligionId: > 0 } m ? m : Vivant(tous, h.PereId) is { ReligionId: > 0 } p ? p : null;
            if (parent is null) continue;
            Adherer(h, Cultes!.Religions[parent.ReligionId - 1], 0.5 * parent.Ferveur, maintenant);
        }
    }

    // ───────────── Prophètes ─────────────

    /// <summary>Ce qui pousse un humain à chercher du sens dans l'invisible : la faim des siens, la guerre, la peur, le deuil (Wallace, 1956).</summary>
    private double Crise(Humain h)
    {
        var c = Social?.CommunauteDe(h);
        var guerre = h.CommunauteId != 0 && Social!.Guerres.Any(g => g.Fin is null && (g.A == h.CommunauteId || g.B == h.CommunauteId));
        return 1 + (c?.Disette == true ? 1 : 0) + (guerre ? 0.5 : 0) + (h.Peur > 50 ? 0.5 : 0) + (h.Chagrin > 50 ? 0.5 : 0);
    }

    /// <summary>
    /// Devenir prophète : il fonde une religion avec ce qu'il croit savoir de l'invisible. Le prophète désigné transmet les
    /// paroles de l'Entité qu'il a crues, parfois de travers ; le visionnaire, ce que ses visions et ses peurs lui ont appris ;
    /// l'imposteur, ce qui le sert.
    /// </summary>
    internal void DevenirProphete(Humain h, SourceProphete source, long cause, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Cultes is null || h.Role == RoleReligieux.Prophete || !h.EstVivant) return;
        _maintenant = maintenant;
        _tous = tous;
        var p = h.Personnalite;
        var dogmes = new List<Dogme>();
        void Ajouter(Precepte pr, int param, OrigineDogme o)
        {
            if (!dogmes.Exists(d => d.Precepte == pr && d.Parametre == param)) dogmes.Add(new Dogme(pr, param, o, TexteDogme(pr, param), maintenant, cause));
        }
        void Transmettre()
        {
            foreach (var i in h.Impulsions)
            {
                if (!i.Crue || i.Reponse == Reponse.Refusee || PrecepteDe(i.Parole, i.Param) is not { } pp) continue;
                if (alea.Reel() < ChanceDeDeformer(h))
                {
                    var (dp, dparam) = Deformer(pp.P, pp.Param);
                    Ajouter(dp, dparam, dp == pp.P ? OrigineDogme.Entite : OrigineDogme.Deformee);
                }
                else Ajouter(pp.P, pp.Param, OrigineDogme.Entite);
            }
        }
        var jardin = h.Memoire.Jardin;
        string annonce;
        switch (source)
        {
            case SourceProphete.Entite:
                Ajouter(Precepte.Createur, 0, OrigineDogme.Entite);
                Transmettre();
                annonce = $"{h.Nom} se lève parmi les siens : {Il(h)} dit porter la parole de celle qui les a faits.";
                break;
            case SourceProphete.Visionnaire:
                if (jardin.Exists(x => x.Cle is "croyance:entite" or "croyance:presence")) Ajouter(Precepte.Createur, 0, OrigineDogme.Inventee);
                if (jardin.Exists(x => x.Cle == "croyance:morts")) Ajouter(Precepte.Ancetres, 0, OrigineDogme.Reve);
                Transmettre();
                if (ProcheMortALaGuerre(h, tous, 0) && p.Agreabilite > 0.4) Ajouter(Precepte.Paix, 0, OrigineDogme.Inventee);
                if ((h.Peur > 50 || Crise(h) >= 2) && alea.Reel() < 0.2 + 0.6 * p.Nevrosisme) Ajouter(Precepte.Colere, 0, OrigineDogme.Inventee);
                if (p.Agreabilite > 0.6) Ajouter(alea.Reel() < 0.5 ? Precepte.Partager : Precepte.Pardonner, 0, OrigineDogme.Inventee);
                if (alea.Reel() < 0.5) Ajouter(Precepte.Priere, 0, OrigineDogme.Inventee);
                if (alea.Reel() < 0.4) Ajouter(Precepte.Prophetie, 0, OrigineDogme.Inventee);
                annonce = $"Habité{E(h)} par ses visions, {h.Nom} se met à parler au nom de l'invisible.";
                break;
            default:
                Ajouter(Precepte.Createur, 0, OrigineDogme.Inventee);
                Ajouter(Precepte.NourrirLeProphete, 0, OrigineDogme.Inventee);
                Ajouter(Precepte.Offrandes, 0, OrigineDogme.Inventee);
                Ajouter(Precepte.Prophetie, 0, OrigineDogme.Inventee);
                annonce = $"{h.Nom} prétend parler au nom de l'invisible, pour être écouté{E(h)} et nourri{E(h)}.";
                break;
        }
        if (dogmes.Count == 0) Ajouter(Precepte.Priere, 0, OrigineDogme.Inventee);
        h.Role = RoleReligieux.Prophete;
        h.SourceProphete = source;
        h.RoleDepuis = maintenant;
        var r = Fonder(h, source == SourceProphete.Entite ? NaissanceReligion.Prophete : NaissanceReligion.ProphetSpontane, dogmes, annonce, cause, maintenant,
            h.ReligionId);
        h.Ferveur = 1;
        Confier(h, TypeIntime.ImageDeSoi, 0, source switch
        {
            SourceProphete.Imposteur => "Ils m'écouteront, et ils me nourriront.",
            _ when p.Moral.Honnetete < 0.4 => "L'invisible m'a choisi, moi, entre tous.",
            _ => "L'invisible parle par ma bouche. En serai-je digne ?",
        }, 50 + 40 * p.Extraversion, r.EvenementId, maintenant, "image:prophete");
        // Ses plus proches, s'ils croient déjà un peu, le suivent les premiers.
        foreach (var l in h.Liens.ToArray())
        {
            if (l.Affection < 50 || Vivant(tous, l.Id) is not { } x || x.EstBebe(maintenant) || x.ReligionId == r.Id) continue;
            if (alea.Reel() < l.Affection / 100 * (0.2 + 0.8 * x.Croyance / 100)) Convertir(x, r, h, tous, maintenant);
        }
    }

    /// <summary>
    /// Une parole de l'Entité crue par un prophète devient un précepte de sa religion (ou l'annonce d'un malheur).
    /// </summary>
    internal void Reveler(Humain h, Parole parole, int param, long reception, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (Cultes?.Get(h.ReligionId) is not { } r || h.Role != RoleReligieux.Prophete) return;
        if (h.Impulsions.Count == 0 || h.Impulsions[^1] is not { Crue: true } i || i.Reponse == Reponse.Refusee) return;
        if (parole == Parole.CatastropheArrive)
        {
            Annoncer(h, r, vraie: true, reception, maintenant);
            return;
        }
        if (PrecepteDe(parole, param) is not { } pp) return;
        var (pr, pa, o) = alea.Reel() < ChanceDeDeformer(h) ? (Deformer(pp.P, pp.Param).P, Deformer(pp.P, pp.Param).Param, OrigineDogme.Deformee)
            : (pp.P, pp.Param, OrigineDogme.Entite);
        AjouterDogme(r, pr, pa, o == OrigineDogme.Deformee && pr == pp.P ? OrigineDogme.Entite : o, h, reception, maintenant);
    }

    /// <summary>Un lieu où l'invisible s'est montré (pas deux à moins de deux cases).</summary>
    internal void NouveauLieu(int x, int y, string raison, long cause, long maintenant)
    {
        if (Cultes is null || Cultes.Lieux.Exists(l => Distance(l.X, l.Y, x, y) <= 2)) return;
        Cultes.Lieux.Add(new LieuSacre(Cultes.Lieux.Count + 1, x, y, maintenant, raison, cause, 0));
    }

    /// <summary>
    /// Chaque jour : un croyant habité de visions (rêves des morts, transes, paroles crues), surtout dans les temps durs, peut
    /// se mettre à parler au nom de l'invisible ; un menteur charismatique, devant des gens qui croient, peut le prétendre.
    /// Les taux sont des estimations : quelques prophètes par siècle dans une petite population.
    /// </summary>
    private void ProphetesSpontanes(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        Dictionary<int, double>? auditoires = null;
        foreach (var h in vivants)
        {
            if (h.Role != RoleReligieux.Aucun || h.CaptifDe != 0 || h.AgeEnAnnees(maintenant) < 16) continue;
            var p = h.Personnalite;
            var visionnaire = h.Croyance >= 55;
            var menteur = p.Moral.Honnetete < 0.3 && p.Charisme > 0.5 && h.AgeEnAnnees(maintenant) >= 18;
            if (!visionnaire && !menteur) continue;
            var crise = Crise(h);
            if (visionnaire)
            {
                var signe = h.Impulsions.Exists(i => i.Crue && maintenant - i.Le < Calendrier.SecondesParAn)
                            || h.TranseLe > 0 && maintenant - h.TranseLe < Calendrier.SecondesParAn
                            || h.Memoire.Jardin.Exists(x => x.Cle == "croyance:morts");
                var chance = 4e-5 * p.Transcendance * p.Transcendance * (0.5 + p.Ouverture) * (0.5 + p.Extraversion) * (signe ? 3 : 1) * crise;
                if (alea.Reel() < chance)
                {
                    DevenirProphete(h, SourceProphete.Visionnaire, 0, tous, maintenant);
                    continue;
                }
            }
            if (!menteur || h.CommunauteId == 0) continue;
            auditoires ??= vivants.Where(x => x.CommunauteId != 0 && !x.EstEnfant(maintenant)).GroupBy(x => x.CommunauteId)
                .ToDictionary(g => g.Key, g => g.Average(x => x.Croyance) / 100);
            var auditoire = auditoires.GetValueOrDefault(h.CommunauteId);
            if (alea.Reel() < 1.5e-4 * (0.3 - p.Moral.Honnetete) / 0.3 * p.Charisme * auditoire * crise)
                DevenirProphete(h, SourceProphete.Imposteur, 0, tous, maintenant);
        }
    }

    /// <summary>
    /// Chaque jour : le prophète, les prêtres et les fidèles ardents et expansifs parlent de leur foi à ceux qui les entourent.
    /// On se laisse convaincre par ceux qu'on aime, quand ses proches croient déjà (Stark et Lofland, 1965), par celui à qui
    /// l'on prête des miracles, et par celui qui vit ce qu'il dit (Henrich, 2009).
    /// </summary>
    private void Precher(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var precheurs = vivants.Where(h => h.ReligionId != 0 && h.CaptifDe == 0 && !h.EstEnfant(maintenant)
            && (h.Role != RoleReligieux.Aucun || h.Ferveur >= 0.7 && h.Personnalite.Extraversion >= 0.6)).ToList();
        if (precheurs.Count == 0) return;
        var parCase = new Dictionary<int, List<Humain>>();
        foreach (var h in vivants)
        {
            if (h.AgeEnAnnees(maintenant) < 7 || h.CaptifDe != 0) continue;
            var i = carte.Indice(h.X, h.Y);
            if (!parCase.TryGetValue(i, out var l)) parCase[i] = l = [];
            l.Add(h);
        }
        foreach (var pr in precheurs)
        {
            if (pr.ReligionId == 0) continue;
            var r = Cultes!.Religions[pr.ReligionId - 1];
            var poids = pr.Role switch { RoleReligieux.Prophete => 1, RoleReligieux.Pretre => 0.6, _ => 0.25 };
            var cred = pr.SourceProphete switch { SourceProphete.Imposteur => 0.7, SourceProphete.Entite => 1.2, _ => 1 };
            var prestige = 1 + 0.3 * Math.Min(pr.Miracles, 10);
            var entendus = 0;
            for (var dy = -2; dy <= 2 && entendus < 8; dy++)
                for (var dx = -2; dx <= 2 && entendus < 8; dx++)
                {
                    int x = pr.X + dx, y = pr.Y + dy;
                    if (x < 0 || y < 0 || x >= carte.Largeur || y >= carte.Hauteur || !parCase.TryGetValue(carte.Indice(x, y), out var ici)) continue;
                    foreach (var o in ici)
                    {
                        if (o.ReligionId == r.Id || o.Id == pr.Id || entendus >= 8) continue;
                        entendus++;
                        var po = o.Personnalite;
                        var recept = 0.15 + 0.85 * o.Croyance / 100 * (0.5 + po.Transcendance);
                        var proches = 0;
                        foreach (var l in o.Liens)
                            if (l.Affection >= 40 && l.Id >= 1 && l.Id <= tous.Count && tous[l.Id - 1] is { EstVivant: true } f && f.ReligionId == r.Id && ++proches >= 4) break;
                        var reseau = 1 + 2 * Math.Max(0, o.AffectionPour(pr.Id)) / 100 + 0.5 * proches;
                        var autre = o.ReligionId != 0 ? 0.5 * (1 - o.Ferveur) : 1;
                        var chance = 0.004 * poids * (0.3 + pr.Personnalite.Charisme) * prestige * cred * recept * reseau * autre * Crise(o);
                        if (alea.Reel() < chance) Convertir(o, r, pr, tous, maintenant);
                        else if (pr.SourceProphete == SourceProphete.Imposteur && po.Moral.EmpathieCognitive > 0.7 && Croyances.Analytique(o) > 0.5
                                 && alea.Reel() < 0.002)
                        {
                            Noter(maintenant, TypeEvenement.Religion, o, pr, $"{o.Nom} voit clair dans le jeu de {pr.Nom}, qui parle au nom de l'invisible pour se faire nourrir.");
                            Social?.ChangerAffection(o, pr, -15, maintenant);
                        }
                        else if (alea.Reel() < 0.003 * (1 - recept) * (1 - po.Agreabilite)) Social?.ChangerAffection(o, pr, -2, maintenant);
                    }
                }
        }
    }

    private void Convertir(Humain o, Religion r, Humain par, IReadOnlyList<Humain> tous, long maintenant)
    {
        var avant = Cultes!.Get(o.ReligionId);
        var texte = avant is null
            ? $"{o.Nom} se met à suivre le {r.Nom}, convaincu{E(o)} par {par.Nom}."
            : $"{o.Nom} quitte le {avant.Nom} pour suivre le {r.Nom}, convaincu{E(o)} par {par.Nom}.";
        var id = Noter(maintenant, TypeEvenement.Religion, o, par, texte, r.EvenementId);
        Adherer(o, r, 0.3 + 0.3 * alea.Reel(), maintenant);
        Croyances.Ajouter(o, 5, $"{par.Nom} l'a convaincu{E(o)}", id, maintenant);
        Social?.ChangerAffection(o, par, 5, maintenant);
        if (r.Enseigne(Precepte.Createur))
            Confier(o, TypeIntime.Croyance, par.Id, "Une Entité venue d'ailleurs nous a faits, et elle nous parle.", 30 + 40 * o.Ferveur, id, maintenant, "croyance:entite");
    }

    // ───────────── Prophéties ─────────────

    /// <summary>Le prophète annonce un malheur là où vivent les siens (vrai s'il le tient de l'Entité).</summary>
    private void Annoncer(Humain h, Religion r, bool vraie, long cause, long maintenant)
    {
        if (Cultes!.Propheties.Exists(p => p.ProphetId == h.Id)) return;
        var c = Social?.CommunauteDe(h);
        var (x, y) = c is null ? (h.X, h.Y) : (c.CentreX, c.CentreY);
        var id = Noter(maintenant, TypeEvenement.Religion, h, null, $"{h.Nom} annonce aux siens qu'un grand malheur va frapper.", cause, significatif: true);
        // Un malheur annoncé est attendu bientôt : passé quelques mois, on n'y voit plus la prophétie.
        var duree = vraie ? 90 : 120;
        Cultes.Propheties.Add(new Prophetie(h.Id, r.Id, x, y, maintenant, maintenant + (long)(duree * Calendrier.SecondesParJour), id, vraie));
        AjouterDogme(r, Precepte.Prophetie, 0, vraie ? OrigineDogme.Entite : OrigineDogme.Inventee, null, id, maintenant);
    }

    /// <summary>
    /// Chaque jour : un prophète venu de lui-même annonce parfois un malheur ; un malheur qui frappe là où il était annoncé
    /// accomplit la prophétie (son prestige grandit, des gens se convertissent) ; le délai passé, elle est démentie.
    /// </summary>
    private void Propheties(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var cultes = Cultes!;
        foreach (var h in vivants)
            if (h.Role == RoleReligieux.Prophete && h.SourceProphete != SourceProphete.Entite && h.ReligionId != 0 && alea.Reel() < 0.0015)
                Annoncer(h, cultes.Religions[h.ReligionId - 1], vraie: false, 0, maintenant);
        if (cultes.Propheties.Count == 0) return;
        var recentes = Catastrophes?.Recentes(maintenant - Calendrier.SecondesParJour).ToList() ?? [];
        for (var k = cultes.Propheties.Count - 1; k >= 0; k--)
        {
            var p = cultes.Propheties[k];
            var venue = recentes.FirstOrDefault(c => c.Debut >= p.Le && Distance(c.X, c.Y, p.X, p.Y) <= c.Rayon + 2);
            if (venue is not null) Accomplir(p, venue, vivants, tous, maintenant);
            else if (maintenant >= p.Jusqua) Dementir(p, vivants, tous, maintenant);
            else continue;
            cultes.Propheties.RemoveAt(k);
        }
    }

    private void Accomplir(Prophetie p, Catastrophe venue, List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var prophete = tous[p.ProphetId - 1];
        var r = Cultes!.Religions[p.ReligionId - 1];
        if (prophete.EstVivant) prophete.Miracles++;
        var id = Noter(maintenant, TypeEvenement.Religion, prophete, null,
            $"Le malheur annoncé par {prophete.Nom} est venu ({venue.Nom}) : ceux qui l'ont entendu{E(prophete)} se souviennent de sa parole.",
            p.EvenementId, significatif: true);
        var convertis = 0;
        foreach (var h in vivants)
        {
            if (Distance(h.X, h.Y, p.X, p.Y) > 12) continue;
            if (h.ReligionId == r.Id)
            {
                h.Ferveur = Math.Min(1, h.Ferveur + 0.15);
                Croyances.Ajouter(h, 8, $"le malheur annoncé par {prophete.Nom} est venu", id, maintenant, signe: true);
            }
            else if (!h.EstEnfant(maintenant) && alea.Reel() < 0.25 * (0.15 + 0.85 * h.Croyance / 100))
            {
                Adherer(h, r, 0.4 + 0.3 * alea.Reel(), maintenant);
                Croyances.Ajouter(h, 10, $"le malheur annoncé par {prophete.Nom} est venu", id, maintenant, signe: true);
                convertis++;
            }
        }
        if (convertis > 0)
            Noter(maintenant, TypeEvenement.Religion, prophete, null,
                $"{(convertis == 1 ? "Une personne se met" : $"{convertis} personnes se mettent")} à suivre le {r.Nom}.", id);
    }

    private void Dementir(Prophetie p, List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var prophete = tous[p.ProphetId - 1];
        if (prophete.EstVivant) prophete.Miracles = Math.Max(0, prophete.Miracles - 1);
        int ardents = 0, detournes = 0;
        foreach (var h in vivants)
        {
            if (h.ReligionId != p.ReligionId || h.Id == prophete.Id) continue;
            if (h.Ferveur >= 0.7)
            {
                h.Ferveur = Math.Min(1, h.Ferveur + 0.05);
                ardents++;
            }
            else
            {
                h.Ferveur = Math.Max(0, h.Ferveur - 0.1);
                Social?.ChangerAffection(h, prophete, -5, maintenant);
                detournes++;
            }
        }
        Noter(maintenant, TypeEvenement.Religion, prophete, null, $"Le malheur annoncé par {prophete.Nom} n'est pas venu"
            + (ardents + detournes == 0 ? "." : $" : {ardents} fidèle{(ardents > 1 ? "s" : "")} n'en croi{(ardents > 1 ? "ent" : "t")} que plus fort, {detournes} doute{(detournes > 1 ? "nt" : "")}."),
            p.EvenementId);
    }

    // ───────────── Le culte des morts ─────────────

    /// <summary>
    /// Chaque semaine : dans une communauté où plusieurs rêvent de leurs morts et où l'on croit à l'invisible, et qui n'a pas
    /// encore de religion, on se met à honorer les morts.
    /// </summary>
    private void CulteDesMorts(List<Humain> vivants, long maintenant)
    {
        foreach (var g in vivants.Where(h => h.CommunauteId != 0 && !h.EstEnfant(maintenant)).GroupBy(h => h.CommunauteId).OrderBy(g => g.Key))
        {
            var membres = g.ToList();
            if (membres.Count(h => h.ReligionId != 0) * 2 >= membres.Count) continue;
            var reveurs = membres.Where(h => h.ReligionId == 0 && h.Memoire.Jardin.Exists(x => x.Cle == "croyance:morts")).ToList();
            if (reveurs.Count < 3 || membres.Average(h => h.Croyance) < 20 || alea.Reel() >= 0.1) continue;
            var f = reveurs.MaxBy(h => h.Croyance * (0.5 + h.Personnalite.Charisme))!;
            var c = Social!.Communautes[g.Key - 1];
            var dogmes = new List<Dogme> { new(Precepte.Ancetres, 0, OrigineDogme.Reve, TexteDogme(Precepte.Ancetres, 0), maintenant, 0) };
            if (alea.Reel() < 0.5) dogmes.Add(new Dogme(Precepte.Offrandes, 0, OrigineDogme.Inventee, TexteDogme(Precepte.Offrandes, 0), maintenant, 0));
            var r = Fonder(f, NaissanceReligion.Ancetres, dogmes,
                $"Dans la {c.Nom.ToLowerInvariant()}, on se met à honorer les morts, qui reviennent la nuit veiller sur les vivants.", 0, maintenant);
            foreach (var h in reveurs) if (h.Id != f.Id) Adherer(h, r, 0.35 + 0.35 * h.Croyance / 100, maintenant);
        }
    }

    // ───────────── Rites ─────────────

    /// <summary>Gestes inventés pour prier, selon ce que font les siens (arts, morts, lieux).</summary>
    private string InventerGestes(Religion r, List<Humain> groupe)
    {
        string[] postures = ["à genoux", "debout, les bras levés", "assis en cercle", "prosternés", "en se balançant d'avant en arrière"];
        var vers = r.Enseigne(Precepte.Ancetres) ? "auprès des morts"
            : alea.Reel() < 0.5 ? (alea.Reel() < 0.5 ? "face au soleil levant" : "face au couchant") : "autour du feu";
        var art = groupe.Any(h => Humains.Arts.Connait(h, Effet.Danse)) ? ", en dansant"
            : groupe.Any(h => Humains.Arts.Connait(h, Effet.Chant)) ? ", en chantant"
            : groupe.Any(h => Humains.Arts.Connait(h, Effet.Rythme)) ? ", au son des rythmes" : "";
        var ocre = groupe.Any(h => Humains.Arts.Connait(h, Effet.Ocre)) && alea.Reel() < 0.5 ? ", le visage peint d'ocre" : "";
        return $"{postures[alea.Entier(postures.Length)]} {vers}{art}{ocre}";
    }

    /// <summary>
    /// Chaque semaine, les fidèles d'une religion dans une communauté se rassemblent : la ferveur, l'humeur et les liens
    /// montent, le chagrin s'apaise (Sosis et Bressler, 2003). Les offrandes coûtent un peu de nourriture ; à chaque saison,
    /// la fête est notée, avec la bête offerte si la religion le veut. Leur foi les pousse aussi à partager, à veiller sur
    /// les malades, à pardonner et, parfois, à nourrir leur prophète.
    /// </summary>
    private void Rite(Religion r, Communaute c, List<Humain> groupe, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (groupe.Count < 2) return;
        var meneur = groupe.FirstOrDefault(h => h.Role == RoleReligieux.Prophete) ?? groupe.FirstOrDefault(h => h.Role == RoleReligieux.Pretre)
                     ?? groupe.MaxBy(h => h.Ferveur)!;
        if (r.Gestes is null)
        {
            r.Gestes = InventerGestes(r, groupe);
            Noter(maintenant, TypeEvenement.Religion, meneur, null, $"Pour la première fois, les fidèles du {r.Nom} se rassemblent et prient {r.Gestes}.",
                r.EvenementId, significatif: true);
        }
        var lieu = Cultes!.Lieux.Find(l => l.ReligionId == r.Id && Distance(l.X, l.Y, c.CentreX, c.CentreY) <= c.Rayon + 3);
        var bonus = lieu is null ? 1 : 1.5;
        var offrandes = r.Enseigne(Precepte.Offrandes);
        foreach (var h in groupe)
        {
            h.Ferveur = Math.Min(1, h.Ferveur + 0.03 * (1 - h.Ferveur) * bonus);
            VieSociale.Rejouir(h, 2);
            h.Chagrin *= 0.95;
            if (offrandes) h.Reserves = Math.Max(0, h.Reserves - 0.5);
            for (var k = 0; k < Math.Min(3, groupe.Count - 1); k++)
            {
                var o = groupe[alea.Entier(groupe.Count)];
                if (o.Id != h.Id) Social?.ChangerAffection(h, o, 0.5, maintenant);
            }
        }
        var saison = maintenant / (Calendrier.JoursParSaison * Calendrier.SecondesParJour);
        if (_rites.GetValueOrDefault((r.Id, c.Id), -1) != saison)
        {
            _rites[(r.Id, c.Id)] = saison;
            var bete = "";
            if (r.Enseigne(Precepte.SacrificeAnimal))
            {
                var prises = Ecosysteme.Prelever(ecosysteme.Gibier, carte.Indice(meneur.X, meneur.Y), 1 + alea.Entier(3));
                if (prises > 0)
                {
                    bete = ", et offrent une bête, dont ils partagent la viande";
                    foreach (var h in groupe) h.Faim = Math.Max(0, h.Faim - 10);
                }
            }
            Noter(maintenant, TypeEvenement.Religion, meneur, null,
                $"Les fidèles du {r.Nom} de la {c.Nom.ToLowerInvariant()} ({groupe.Count}) se rassemblent et prient {r.Gestes}"
                + (lieu is not null ? ", au lieu sacré" : "") + (offrandes ? ", et offrent une part de leur nourriture" : "") + bete + ".", r.EvenementId);
            NourrirLeProphete(r, groupe, maintenant);
        }
        Vivre(r, groupe, maintenant);
    }

    /// <summary>Ce que la foi pousse à faire, chaque semaine : partager, veiller sur les malades, pardonner.</summary>
    private void Vivre(Religion r, List<Humain> groupe, long maintenant)
    {
        if (r.Enseigne(Precepte.Pardonner))
            foreach (var h in groupe)
                for (var k = 0; k < h.Griefs.Count; k++)
                    h.Griefs[k] = h.Griefs[k] with { Intensite = h.Griefs[k].Intensite * (1 - 0.1 * h.Ferveur) };
        if (r.Enseigne(Precepte.Partager))
        {
            var affame = groupe.Where(h => h.Faim >= 60).MaxBy(h => h.Faim);
            var donneur = groupe.Where(h => h.Faim < 40 && h.Ferveur >= 0.5 && !h.EstEnfant(maintenant)).MaxBy(h => h.Ferveur);
            if (affame is not null && donneur is not null && affame.Id != donneur.Id && alea.Reel() < donneur.Ferveur)
            {
                var don = Math.Min(30, 70 - donneur.Faim);
                affame.Faim = Math.Max(0, affame.Faim - don);
                donneur.Faim += 0.7 * don;
                VieSociale.Gratitude(donneur, affame, 0.4);
                Noter(maintenant, TypeEvenement.Partage, donneur, affame, $"Comme le veut sa foi, {donneur.Nom} partage sa nourriture avec {affame.Nom}.", karma: Regard.Partage);
            }
        }
        if (r.Enseigne(Precepte.Soigner))
        {
            var malade = groupe.Where(h => (h.EstMalade(maintenant) || h.InfectionId != 0 || h.PlaieLe > 0) && h.Sante < 70).MinBy(h => h.Sante);
            var soignant = groupe.Where(h => h.Ferveur >= 0.5 && !h.EstEnfant(maintenant) && h.Id != malade?.Id && !h.EstMalade(maintenant)).MaxBy(h => h.Ferveur);
            if (malade is not null && soignant is not null && alea.Reel() < soignant.Ferveur)
            {
                malade.Sante = Math.Min(100, malade.Sante + 3 + 4 * soignant.Personnalite.Moral.Compassion);
                Social?.ChangerAffection(malade, soignant, 4, maintenant);
                Noter(maintenant, TypeEvenement.Entraide, soignant, malade, $"Comme le veut sa foi, {soignant.Nom} veille sur {malade.Nom}.", karma: Regard.Veiller);
            }
        }
    }

    /// <summary>Les fidèles nourrissent leur prophète (ce qu'enseigne l'imposteur, et parfois d'autres).</summary>
    private void NourrirLeProphete(Religion r, List<Humain> groupe, long maintenant)
    {
        if (!r.Enseigne(Precepte.NourrirLeProphete) || groupe.FirstOrDefault(h => h.Role == RoleReligieux.Prophete) is not { } prophete) return;
        var donneurs = groupe.Where(h => h.Id != prophete.Id && !h.EstEnfant(maintenant) && h.Faim < 60).Take(4).ToList();
        if (donneurs.Count == 0) return;
        foreach (var d in donneurs) d.Faim = Math.Min(100, d.Faim + 5);
        prophete.Faim = Math.Max(0, prophete.Faim - 8 * donneurs.Count);
        prophete.Reserves = Math.Min(100, prophete.Reserves + 2 * donneurs.Count);
        Noter(maintenant, TypeEvenement.Religion, prophete, null, $"Les fidèles du {r.Nom} nourrissent {prophete.Nom}, qui porte la parole.",
            karma: prophete.SourceProphete == SourceProphete.Imposteur ? Regard.Tromper : 0);
    }

    /// <summary>Un groupe de fidèles assez grand se donne un gardien des rites : le plus fervent, le plus consciencieux, le plus parlant.</summary>
    private void Pretre(Religion r, Communaute c, List<Humain> groupe, long maintenant)
    {
        if (groupe.Count < FidelesPourUnPretre || groupe.Exists(h => h.Role == RoleReligieux.Pretre)) return;
        var choisi = groupe.Where(h => h.Role == RoleReligieux.Aucun && h.AgeEnAnnees(maintenant) >= 18)
            .MaxBy(h => h.Ferveur * (0.5 + h.Personnalite.Conscience) * (0.5 + Math.Clamp((h.Personnalite.Intelligence.Verbale - 55) / 90, 0, 1))
                        * (0.8 + 0.4 * alea.Reel()));
        if (choisi is null) return;
        choisi.Role = RoleReligieux.Pretre;
        choisi.RoleDepuis = maintenant;
        var id = Noter(maintenant, TypeEvenement.Religion, choisi, null,
            $"{choisi.Nom} devient {(choisi.Sexe == Sexe.Femme ? "la gardienne" : "le gardien")} des rites du {r.Nom} dans la {c.Nom.ToLowerInvariant()}.", r.EvenementId);
        Confier(choisi, TypeIntime.Fierte, 0, "Les miens m'ont confié leurs rites.", 40 + 40 * choisi.Ferveur, id, maintenant, "image:pretre");
    }

    // ───────────── Opposition ─────────────

    /// <summary>
    /// Chaque semaine : la figure d'une communauté qui ne partage pas la foi d'un prophète influent peut s'y opposer : le
    /// rejeter, le faire chasser, ou, rarement, le tuer. La persécution soude ses fidèles.
    /// </summary>
    private void Opposer(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var prophete in vivants.Where(h => h.Role == RoleReligieux.Prophete && h.CommunauteId != 0).ToList())
        {
            if (!prophete.EstVivant || Social?.CommunauteDe(prophete) is not { FigureId: > 0 } c || Vivant(tous, c.FigureId) is not { } figure) continue;
            if (figure.Id == prophete.Id || figure.ReligionId == prophete.ReligionId) continue;
            var membres = vivants.Where(h => h.CommunauteId == c.Id && !h.EstEnfant(maintenant)).ToList();
            if (membres.Count == 0) continue;
            var part = membres.Count(h => h.ReligionId == prophete.ReligionId) / (double)membres.Count;
            var pf = figure.Personnalite;
            if (part < 0.2 || alea.Reel() >= 0.08 * part * (1.2 - pf.Agreabilite) * (0.5 + pf.Moral.Fondements.Autorite)) continue;
            var r = Cultes!.Religions[prophete.ReligionId - 1];
            var fideles = membres.Where(h => h.ReligionId == r.Id && h.Id != prophete.Id).ToList();
            var tueur = Psyche.EstPsychopathe(figure, maintenant) || pf.Moral.Compassion < 0.3;
            var tirage = alea.Reel();
            if (tueur && tirage < 0.15)
            {
                var id = Noter(maintenant, TypeEvenement.Religion, figure, prophete, $"{figure.Nom} ne supporte plus que {prophete.Nom} parle au nom de l'invisible.",
                    r.EvenementId, significatif: true);
                Tuer(prophete, maintenant, CauseDeMort.Meurtre, $"{figure.Nom} tue {prophete.Nom}, qui parlait au nom de l'invisible.", id, figure, Regard.Meurtre);
                foreach (var f in fideles)
                {
                    f.Ferveur = Math.Min(1, f.Ferveur + 0.2);
                    VieSociale.AjouterGrief(f, new Grief(figure.Id, MotifGrief.Meurtre, 60, 60, prophete.MortEvenementId, prophete.Id));
                }
            }
            else if (part < 0.5 && tirage < 0.45)
            {
                var duree = (long)((1 + 4 * alea.Reel()) * Calendrier.SecondesParAn);
                var raison = $"parce qu'{Il(prophete)} parle au nom de l'invisible";
                var id = Noter(maintenant, TypeEvenement.Exclusion, prophete, figure,
                    $"Poussée par {figure.Nom}, la {c.Nom.ToLowerInvariant()} chasse {prophete.Nom}, qui parlait au nom de l'invisible.", r.EvenementId, significatif: true);
                prophete.Exclusions.Add(new Exclusion(TypeExclusion.Communaute, c.Id, maintenant, maintenant + duree, id, raison));
                Confier(prophete, TypeIntime.Peine, figure.Id, "Les miens m'ont chassé, et l'invisible n'a rien fait.", 50, id, maintenant, $"exil:{c.Id}");
                foreach (var f in fideles) f.Ferveur = Math.Min(1, f.Ferveur + 0.05);
            }
            else
            {
                Social?.ChangerAffection(figure, prophete, -10, maintenant);
                Social?.ChangerAffection(prophete, figure, -5, maintenant);
                Noter(maintenant, TypeEvenement.Religion, figure, prophete, $"{figure.Nom} s'oppose à {prophete.Nom}, qui parle au nom de l'invisible.", r.EvenementId);
                foreach (var f in fideles) f.Ferveur = Math.Min(1, f.Ferveur + 0.02);
            }
        }
    }

    // ───────────── Préceptes nés de ce qu'on vit ─────────────

    /// <summary>
    /// Chaque semaine, les préceptes d'une religion changent avec ce que vivent ses fidèles : une catastrophe qui tue est une
    /// colère à apaiser ; dans la crise, celui qui mène les rites invente le sacrifice d'une bête ; la mort d'un voleur ou d'un
    /// meurtrier est un châtiment ; un meneur dur appelle à combattre les voisins qui ne croient pas de même, un meneur doux à
    /// la paix ; un lieu où l'invisible s'est montré devient sacré.
    /// </summary>
    private void Preceptes(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var cultes = Cultes!;
        var recentes = Catastrophes?.Recentes(maintenant - 30 * Calendrier.SecondesParJour).Where(c => c.Morts >= 2).ToList() ?? [];
        var morts = tous.Where(h => h.MortLe is { } m && m >= maintenant - Semaine
            && h.CauseDeMort is CauseDeMort.Maladie or CauseDeMort.Accident or CauseDeMort.Noyade or CauseDeMort.Eboulement or CauseDeMort.Brulure
                or CauseDeMort.Foudre or CauseDeMort.MortSubite or CauseDeMort.Sommeil).ToList();
        // Les châtiments visibles de l'Entité de la semaine, tels que les communautés se les racontent.
        var chatiments = cultes.Souvenirs.Where(s => !s.Bienfait && s.Le >= maintenant - Semaine).ToList();
        foreach (var r in cultes.Religions)
        {
            if (r.EteinteLe != 0) continue;
            var fideles = vivants.Where(h => h.ReligionId == r.Id).ToList();
            if (fideles.Count == 0) continue;
            var meneur = fideles.FirstOrDefault(h => h.Role == RoleReligieux.Prophete) ?? fideles.FirstOrDefault(h => h.Role == RoleReligieux.Pretre)
                         ?? fideles.MaxBy(h => h.Ferveur)!;
            var communautes = fideles.Where(h => h.CommunauteId != 0).Select(h => h.CommunauteId).Distinct().ToList();
            var croyance = fideles.Average(h => h.Croyance) / 100;
            // La crainte de l'invisible pousse aux offrandes et à voir sa colère partout (voir Regards).
            var crainte = fideles.Average(h => h.Crainte) / 100;
            var pm = meneur.Personnalite;

            if (!r.Enseigne(Precepte.Colere) && recentes.FirstOrDefault(c => communautes.Any(id => c.PertesParCommunaute.GetValueOrDefault(id) >= 2)) is { } cat
                && alea.Reel() < 0.4 * croyance * (1 + crainte))
                AjouterDogme(r, Precepte.Colere, 0, OrigineDogme.Catastrophe, meneur, r.EvenementId, maintenant);
            // Un châtiment vu de leurs yeux : la faute que le groupe s'est trouvée devient un précepte ; sans faute, l'invisible est en colère.
            foreach (var souvenir in chatiments)
            {
                if (!communautes.Contains(souvenir.CommunauteId) || alea.Reel() >= 0.5 * croyance) continue;
                if (souvenir.Faute is Gestes.FauteVol or Gestes.FauteMeurtre)
                    AjouterDogme(r, Precepte.Chatiment, souvenir.Faute == Gestes.FauteMeurtre ? 1 : 0, OrigineDogme.Entite, meneur, souvenir.EvenementId, maintenant);
                else if (!r.Enseigne(Precepte.Colere)) AjouterDogme(r, Precepte.Colere, 0, OrigineDogme.Entite, meneur, souvenir.EvenementId, maintenant);
            }

            var crise = communautes.Any(id => Social!.Communautes[id - 1].Disette) || recentes.Count > 0;
            if (crise && !r.Enseigne(Precepte.SacrificeAnimal) && (r.Enseigne(Precepte.Colere) || r.Enseigne(Precepte.Offrandes)) && alea.Reel() < 0.02 * (1 + 2 * crainte))
                AjouterDogme(r, Precepte.SacrificeAnimal, 0, OrigineDogme.Inventee, meneur, r.EvenementId, maintenant);

            foreach (var m in morts)
            {
                var motif = fideles.SelectMany(f => f.Griefs).Where(g => g.ContreId == m.Id && g.Motif is MotifGrief.Vol or MotifGrief.Meurtre)
                    .Select(g => (MotifGrief?)g.Motif).FirstOrDefault();
                if (motif is null || alea.Reel() >= 0.3) continue;
                if (AjouterDogme(r, Precepte.Chatiment, motif == MotifGrief.Meurtre ? 1 : 0, OrigineDogme.Coincidence, meneur, m.MortEvenementId, maintenant)) break;
            }

            if (!r.Enseigne(Precepte.Priere) && pm.Conscience > 0.6 && alea.Reel() < 0.01)
                AjouterDogme(r, Precepte.Priere, 0, OrigineDogme.Inventee, meneur, r.EvenementId, maintenant);

            if (meneur.CommunauteId != 0 && Social is { } s)
            {
                var a = s.Communautes[meneur.CommunauteId - 1];
                var membresA = vivants.Count(h => h.CommunauteId == a.Id && !h.EstEnfant(maintenant));
                var part = membresA == 0 ? 0 : fideles.Count(h => h.CommunauteId == a.Id && !h.EstEnfant(maintenant)) / (double)membresA;
                if (part >= 0.5 && pm.Agreabilite < 0.4 && pm.Moral.Fondements.Loyaute > 0.55 && alea.Reel() < 0.03
                    && s.Rivale(a.Id) is { } b && Majorite(vivants, b.Id, maintenant) != r.Id)
                    AjouterDogme(r, Precepte.GuerreSainte, b.Id, OrigineDogme.Inventee, meneur, r.EvenementId, maintenant);
                if (part >= 0.5 && pm.Agreabilite > 0.6 && !r.Enseigne(Precepte.GuerreSainte) && ProcheMortALaGuerre(meneur, tous, maintenant - Calendrier.SecondesParAn)
                    && alea.Reel() < 0.05)
                    AjouterDogme(r, Precepte.Paix, 0, OrigineDogme.Inventee, meneur, r.EvenementId, maintenant);
                // Un lieu où l'invisible s'est montré, sur les terres des fidèles, devient le leur.
                var k = cultes.Lieux.FindIndex(l => l.ReligionId == 0 && Distance(l.X, l.Y, a.CentreX, a.CentreY) <= a.Rayon + 3);
                if (k >= 0 && !r.Enseigne(Precepte.LieuSacre))
                {
                    var l = cultes.Lieux[k];
                    cultes.Lieux[k] = l with { ReligionId = r.Id };
                    AjouterDogme(r, Precepte.LieuSacre, carte.Indice(l.X, l.Y), OrigineDogme.Lieu, meneur, l.EvenementId, maintenant);
                }
            }
        }
    }

    /// <summary>Religion de la plupart des adultes d'une communauté (0 : aucune n'y est majoritaire).</summary>
    private static int Majorite(List<Humain> vivants, int communaute, long maintenant)
    {
        var adultes = vivants.Where(h => h.CommunauteId == communaute && !h.EstEnfant(maintenant)).ToList();
        if (adultes.Count == 0) return 0;
        var g = adultes.Where(h => h.ReligionId != 0).GroupBy(h => h.ReligionId).MaxBy(g => g.Count());
        return g is not null && g.Count() * 2 > adultes.Count ? g.Key : 0;
    }

    // ───────────── Sacrifices humains ─────────────

    /// <summary>
    /// Dans une crise grave, une religion qui croit l'invisible en colère et lui offre déjà des bêtes peut en venir à lui
    /// offrir une vie : celui qui mène les rites, s'il est dur et attaché à l'ordre (ou psychopathe), le propose, et désigne
    /// d'abord un captif, à défaut le plus faible des siens (Watts et al., 2016). Le groupe peut refuser : la compassion et
    /// l'attachement à la victime s'y opposent, l'autorité et la peur y poussent. Rare.
    /// </summary>
    private void Sacrifier(Religion r, Communaute c, List<Humain> groupe, List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        var deja = r.Enseigne(Precepte.SacrificeHumain);
        if (!deja && !(r.Enseigne(Precepte.Colere) && r.Enseigne(Precepte.SacrificeAnimal))) return;
        var recente = Catastrophes?.Recentes(maintenant - 60 * Calendrier.SecondesParJour).Any(k => k.PertesParCommunaute.GetValueOrDefault(c.Id) >= 3) == true;
        var epidemie = vivants.Count(h => h.CommunauteId == c.Id && h.InfectionId != 0) >= 3;
        if (!(c.Disette && (recente || epidemie)) && !(deja && (c.Disette || recente))) return;
        var meneur = groupe.FirstOrDefault(h => h.Role == RoleReligieux.Prophete) ?? groupe.FirstOrDefault(h => h.Role == RoleReligieux.Pretre);
        if (meneur is null) return;
        var pm = meneur.Personnalite;
        if (!(Psyche.EstPsychopathe(meneur, maintenant) || pm.Moral.Fondements.Soin < 0.4 && pm.Moral.Fondements.Autorite > 0.55)) return;
        if (alea.Reel() >= (deja ? 0.1 : 0.03)) return;
        var membres = vivants.Where(h => h.CommunauteId == c.Id).ToList();
        var victime = vivants.Where(h => h.CaptifDe != 0 && membres.Exists(m => m.Id == h.CaptifDe)).MinBy(h => h.Id)
                      ?? membres.Where(h => h.Id != meneur.Id && !SontParents(h, meneur) && h.AgeEnAnnees(maintenant) >= 3 && h.Role == RoleReligieux.Aucun)
                          .MinBy(h => membres.Sum(m => Math.Max(0, m.AffectionPour(h.Id))) + 30 * h.Personnalite.Charisme);
        if (victime is null) return;
        var captif = victime.CaptifDe != 0;
        double pour = 0, contre = 0;
        foreach (var m in membres.Where(m => !m.EstEnfant(maintenant) && m.Id != victime.Id))
        {
            var pf = m.Personnalite.Moral;
            var voix = (m.ReligionId == r.Id ? 0.6 * m.Ferveur : 0) + 0.4 * pf.Fondements.Autorite * m.Croyance / 100 + (captif ? 0.3 : 0)
                       - 0.6 * pf.Fondements.Soin - 0.4 * pf.Compassion - Math.Max(0, m.AffectionPour(victime.Id)) / 100 + 0.3 * m.Peur / 100;
            if (voix > 0) pour += voix; else contre -= voix;
        }
        var proposition = Noter(maintenant, TypeEvenement.Religion, meneur, victime,
            $"Pour apaiser la colère de l'invisible, {meneur.Nom} veut lui offrir la vie de {victime.Nom}{(captif ? ", le captif" + (victime.Sexe == Sexe.Femme ? "e" : "") : "")}.",
            r.EvenementId, significatif: true);
        if (pour <= contre)
        {
            Noter(maintenant, TypeEvenement.Religion, meneur, victime, $"Les siens refusent : {victime.Nom} aura la vie sauve.", proposition, significatif: true);
            foreach (var m in membres) if (m.Id != meneur.Id && m.Personnalite.Moral.Fondements.Soin > 0.5) Social?.ChangerAffection(m, meneur, -8, maintenant);
            Confier(victime, TypeIntime.Peur, meneur.Id, $"{meneur.Nom} a voulu m'offrir à l'invisible.", 70, proposition, maintenant, $"sacrifice:{meneur.Id}");
            return;
        }
        Tuer(victime, maintenant, CauseDeMort.Sacrifice, $"{victime.Nom} est offert{E(victime)} en sacrifice à l'invisible par {meneur.Nom}.", proposition, meneur, Regard.Sacrifier);
        AjouterDogme(r, Precepte.SacrificeHumain, 0, OrigineDogme.Inventee, null, victime.MortEvenementId, maintenant);
        foreach (var m in membres)
        {
            if (m.Id == meneur.Id || m.EstBebe(maintenant)) continue;
            if (m.Personnalite.Moral.EmpathieEmotionnelle > 0.6)
                Confier(m, TypeIntime.Culpabilite, victime.Id, $"Nous avons laissé mourir {victime.Nom} pour l'invisible.", 30 + 50 * m.Personnalite.Moral.EmpathieEmotionnelle,
                    victime.MortEvenementId, maintenant, $"sacrifice:{victime.Id}");
            else if (m.ReligionId == r.Id) m.Ferveur = Math.Min(1, m.Ferveur + 0.05);
            if (m.AffectionPour(victime.Id) >= 40)
                VieSociale.AjouterGrief(m, new Grief(meneur.Id, MotifGrief.Meurtre, 70, 70, victime.MortEvenementId, victime.Id));
        }
    }

    // ───────────── Séparations, méfiance, extinction ─────────────

    /// <summary>
    /// Chaque semaine, dans une religion assez grande : un fidèle ardent et charismatique, en froid avec celui qui mène, ou
    /// dont le prophète est mort, peut entraîner ceux qui l'aiment dans une religion à lui, avec un précepte changé.
    /// </summary>
    private void Schismes(List<Humain> vivants, IReadOnlyList<Humain> tous, long maintenant)
    {
        foreach (var r in Cultes!.Religions.ToList())
        {
            if (r.EteinteLe != 0) continue;
            var fideles = vivants.Where(h => h.ReligionId == r.Id).ToList();
            if (fideles.Count < 10) continue;
            var meneur = fideles.FirstOrDefault(h => h.Role == RoleReligieux.Prophete);
            var fondateurMort = tous[r.FondateurId - 1] is { MortLe: { } m } && maintenant - m < 2 * Calendrier.SecondesParAn;
            var dispersee = fideles.Select(h => h.CommunauteId).Where(id => id != 0).Distinct().Count() >= 2;
            var meneurA = meneur;
            var chef = fideles.Where(h => h.Ferveur >= 0.6 && h.Personnalite.Charisme >= 0.6 && h.Role != RoleReligieux.Prophete && !h.EstEnfant(maintenant)
                                          && (meneurA is not null ? h.AffectionPour(meneurA.Id) <= -10 : fondateurMort && h.Role == RoleReligieux.Pretre))
                .MaxBy(h => h.Personnalite.Charisme);
            if (chef is null || alea.Reel() >= 0.01 * (dispersee ? 2 : 1) * (fondateurMort ? 3 : 1)) continue;
            var dogmes = r.Dogmes.ToList();
            var pc = chef.Personnalite;
            var retirables = dogmes.Where(d => d.Precepte != Precepte.Createur).ToList();
            if (retirables.Count >= 2 && alea.Reel() < 0.5) dogmes.Remove(retirables[alea.Entier(retirables.Count)]);
            var neuf = pc.Agreabilite > 0.6 ? Precepte.Paix : pc.Agreabilite < 0.35 ? Precepte.GuerreSainte : alea.Reel() < 0.5 ? Precepte.Pardonner : Precepte.Priere;
            if (!dogmes.Exists(d => d.Precepte == neuf)) dogmes.Add(new Dogme(neuf, 0, OrigineDogme.Inventee, TexteDogme(neuf, 0), maintenant, 0));
            var ancienneFerveur = chef.Ferveur;
            var branche = Fonder(chef, NaissanceReligion.Schisme, dogmes,
                $"{chef.Nom} se sépare du {r.Nom}{(meneur is not null ? $" et de {meneur.Nom}" : "")} : une partie des fidèles {(chef.Sexe == Sexe.Femme ? "la" : "le")} suit.",
                r.EvenementId, maintenant, r.Id);
            branche.Gestes = r.Gestes;
            chef.Ferveur = Math.Max(ancienneFerveur, 0.8);
            if (chef.Role == RoleReligieux.Pretre) chef.RoleDepuis = maintenant;
            foreach (var f in fideles)
            {
                if (f.Id == chef.Id || f.Role == RoleReligieux.Prophete) continue;
                var pour = f.AffectionPour(chef.Id) - (meneur is null ? 0 : f.AffectionPour(meneur.Id));
                if (pour >= 30 || f.Role == RoleReligieux.Aucun && pour > 0 && alea.Reel() < 0.3) Adherer(f, branche, f.Ferveur, maintenant);
            }
        }
    }

    /// <summary>Les fidèles ardents de religions différentes, quand ils se croisent, se méfient les uns des autres.</summary>
    private void Mefiance(List<Humain> vivants, long maintenant)
    {
        foreach (var g in vivants.Where(h => h.ReligionId != 0 && h.Ferveur >= 0.6 && !h.EstEnfant(maintenant)).GroupBy(h => carte.Indice(h.X, h.Y)))
        {
            var ici = g.ToList();
            for (var i = 0; i < ici.Count; i++)
                for (var j = i + 1; j < ici.Count; j++)
                {
                    if (ici[i].ReligionId == ici[j].ReligionId) continue;
                    Social?.ChangerAffection(ici[i], ici[j], -1, maintenant);
                    Social?.ChangerAffection(ici[j], ici[i], -1, maintenant);
                }
        }
    }

    /// <summary>Chaque semaine : nombre de fidèles ; une religion sans plus aucun fidèle vivant s'éteint.</summary>
    private void Bilan(List<Humain> vivants, long maintenant)
    {
        var comptes = vivants.Where(h => h.ReligionId != 0).GroupBy(h => h.ReligionId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var r in Cultes!.Religions)
        {
            if (r.EteinteLe != 0) continue;
            var n = comptes.GetValueOrDefault(r.Id);
            r.FidelesMax = Math.Max(r.FidelesMax, n);
            if (n > 0) continue;
            r.EteinteLe = maintenant;
            var f = _tous[r.FondateurId - 1];
            Noter(maintenant, TypeEvenement.Religion, f, null, $"Plus personne ne suit le {r.Nom} (« {r.Sens} ») : cette croyance s'éteint.", r.EvenementId, significatif: true);
        }
    }
}
