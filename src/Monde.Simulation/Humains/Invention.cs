using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Comment naissent les objets faits de plusieurs pièces : un besoin (rapporter une bête trop lourde, traverser un lac,
/// chasser mieux, couper du bois) fait chercher dans ce qu'on sait déjà une chose qui ressemble à la solution ; on
/// l'essaie, et les lois de la matière (<see cref="Lois"/>) disent si cela marche, et pourquoi. Rien ici ne nomme le
/// traîneau, la roue ni le radeau : seulement des besoins, des propriétés et des rapprochements.
/// <para>
/// Ce que dit la recherche : l'invention procède surtout par analogie, en transposant une relation connue à un problème
/// nouveau (Gentner, 1983) ; les plus jeunes explorent davantage, les plus âgés exploitent ce qu'ils savent (Gopnik et al.,
/// 2017) ; les objets s'améliorent par petites retouches transmises et conservées, l'« effet cliquet » propre à la culture
/// humaine (Tennie, Call et Tomasello, 2009). La pointe de pierre emmanchée apparaît vers 500 000 ans (Wilkins et al., 2012),
/// la roue bien plus tard, vers 3 500 av. J.-C., et longtemps sans brouette.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    internal enum Besoin : byte
    {
        Chasser = 0,
        Couper = 1,
        Rapporter = 2,
        Traverser = 3,
        Naviguer = 4,
    }

    private static Effet[] EffetsDe(Besoin b) => b switch
    {
        Besoin.Chasser => [Effet.PointeEmmanchee],
        Besoin.Couper => [Effet.TranchantEmmanche],
        Besoin.Rapporter => [Effet.RouleSurAxe, Effet.Glisser, Effet.Rouleaux],
        Besoin.Naviguer => [Effet.Navigation],
        _ => [Effet.Flottaison, Effet.Navigation],
    };

    /// <summary>
    /// Goût et talent pour inventer (0 à 1) : l'intelligence pour rapprocher, l'ouverture pour oser, l'adresse pour réaliser.
    /// Les jeunes adultes explorent le plus ; les enfants n'ont pas encore les savoirs à rapprocher.
    /// </summary>
    private double Inventivite(Humain h)
    {
        var p = h.Personnalite;
        var age = h.AgeEnAnnees(_maintenant);
        var jeunesse = age < 12 ? 0.5 : age < 30 ? 1.15 : age < 50 ? 1 : 0.8;
        return Math.Clamp((0.5 * Ingeniosite(h) + 0.3 * p.Ouverture + 0.2 * p.Adresse) * jeunesse, 0, 1);
    }

    /// <summary>Masse d'un corps humain adulte, en kg (charge d'un radeau).</summary>
    private static double MasseCorps(Humain h) => (h.Sexe == Sexe.Homme ? 62 : 52) * (0.85 + 0.3 * Math.Clamp(h.Reserves / 100, 0, 1));

    /// <summary>Pièces d'une matière sous la main : ce que la case offre ne manque pas ; ce qu'on porte se compte.</summary>
    private int Combien(Humain h, Matiere m)
    {
        if (m == Matiere.Aucune) return 99;
        if (_autour is { } autour && autour.Contains(m)) return 99;
        if (Disponibles(h).Contains(m)) return 99;
        return h.Objets.Count(o => o.Matiere == m);
    }

    private bool APiecesPour(Humain h, Assemblage a) =>
        Combien(h, a.Corps) >= a.NCorps && (a.NTete == 0 || Combien(h, a.Tete) >= a.NTete) && Combien(h, a.Lien) >= 1
        && (a.Lien is not (Matiere.Resine or Matiere.Substance) || FeuIci(h)) && (a.Lien != Matiere.Substance || PorteUnePoix(h))
        // Évider un tronc : le feu pour ronger le bois, ou du moins un outil qui tranche bien.
        && (a.Montage != Montage.Creuse || (FeuIci(h) || h.Sait(Effet.Foyer)) && h.Objets.Exists(o => Lois.Coupe(o) > 0.05)
            || h.Objets.Exists(o => Lois.Coupe(o) >= 0.3));

    /// <summary>
    /// Un lien auquel il pense : la corde qu'il porte, des lianes s'il sait en tordre une, la résine chauffée près du feu.
    /// Sans en avoir l'idée, il peut aussi penser aux lianes qui s'enroulent autour des arbres.
    /// </summary>
    private Matiere ChoisirLien(Humain h)
    {
        if (h.Porte(Matiere.Corde)) return Matiere.Corde;
        // La poix chauffée qu'il porte (brai d'écorce, bitume) : elle tient mieux que tout.
        if (PorteUnePoix(h) && FeuIci(h) && alea.Reel() < 0.6) return Matiere.Substance;
        var fibres = Combien(h, Matiere.Fibres) > 0;
        if (fibres && (h.Sait(Effet.Corde) || alea.Reel() < 0.3 * Inventivite(h))) return Matiere.Fibres;
        if (Combien(h, Matiere.Resine) > 0 && FeuIci(h) && alea.Reel() < 0.4 * Inventivite(h)) return Matiere.Resine;
        if (h.Porte(Matiere.Peau) && h.Sait(Effet.Decoupe) && alea.Reel() < 0.2 * Inventivite(h)) return Matiere.Peau;
        return Matiere.Aucune;
    }

    /// <summary>Ce que donne cet objet pour ce besoin, selon les lois et le terrain.</summary>
    private Verdict Juger(Humain h, Besoin besoin, Assemblage a, double charge, double km = 0)
    {
        var vigueur = Force(h, _maintenant);
        return besoin switch
        {
            Besoin.Chasser or Besoin.Couper => Lois.Emmancher(a, FeuIci(h), AdressePour(h, null), alea),
            Besoin.Rapporter => Lois.Transporter(a, charge, carte.BiomeEn(h.X, h.Y), Lois.Traction(vigueur), Lois.Soulever(vigueur), FeuIci(h), km),
            Besoin.Naviguer => Lois.TenirLaMer(a, 0.5 + 0.5 * h.Personnalite.Adresse, charge, FeuIci(h)),
            _ => Lois.Flotter(a, charge, FeuIci(h)),
        };
    }

    // ------------------------------------------------------------------ Imaginer

    /// <summary>
    /// Une idée pour répondre au besoin, tirée de ce qu'il sait : la pierre qui perce mieux que le bois, la chose ronde qui
    /// roule, le bois qui flotte. Renvoie l'objet imaginé et la pensée qui y a mené, ou null s'il ne voit rien.
    /// </summary>
    private (Assemblage Objet, Recit Pensee)? Imaginer(Humain h, Besoin besoin, double charge)
    {
        switch (besoin)
        {
            case Besoin.Chasser:
            {
                if (!(h.Sait(Effet.Pointe) || h.Sait(Effet.Chasse) || h.Sait(Effet.Durci))) return null;
                var tete = new[] { Matiere.Obsidienne, Matiere.Eclat }.FirstOrDefault(m => Combien(h, m) > 0);
                if (tete == Matiere.Aucune) return null;
                var corps = new[] { Matiere.Epieu, Matiere.BatonPointu, Matiere.Branche }.FirstOrDefault(m => Combien(h, m) > 0);
                if (corps == Matiere.Aucune) return null;
                var lien = ChoisirLien(h);
                var montage = alea.Reel() < 0.75 ? Montage.Bout : Montage.Travers;
                return (new Assemblage(corps, 1, tete, 1, lien, montage),
                    new Recit(montage == Montage.Bout ? "inv.chasser.bout" : "inv.chasser.travers", ("h", h), ("tete", Physique.Nom(tete)), ("corps", Physique.Nom(corps)),
                        ("fin", lien == Matiere.Aucune ? Recits.Recit.Seul("inv.serrant") : AvecLien(lien))));
            }
            case Besoin.Couper:
            {
                if (!(h.Sait(Effet.Eclat) || h.Sait(Effet.Decoupe) || h.Sait(Effet.Chasse))) return null;
                var tete = new[] { Matiere.Silex, Matiere.Galet, Matiere.Obsidienne, Matiere.Eclat }.Where(m => Combien(h, m) > 0).ToList();
                if (tete.Count == 0 || Combien(h, Matiere.Branche) == 0) return null;
                var t = tete[alea.Entier(tete.Count)];
                var lien = ChoisirLien(h);
                var montage = alea.Reel() < 0.6 ? Montage.Travers : Montage.Bout;
                return (new Assemblage(Matiere.Branche, 1, t, 1, lien, montage),
                    new Recit(montage == Montage.Travers ? "inv.couper.travers" : "inv.couper.bout", ("h", h),
                        ("fin", lien == Matiere.Aucune ? Recits.Recit.Seul("inv.sansRien") : AvecLien(lien))));
            }
            case Besoin.Rapporter:
                return ImaginerTransport(h, charge);
            case Besoin.Naviguer:
                return ImaginerBateau(h, charge);
            default:
                return ImaginerRadeau(h, charge);
        }
    }

    /// <summary>
    /// Rapporter une charge trop lourde pour les bras : poser la charge sur des branches liées (comme le toit d'une hutte),
    /// sur des troncs qui roulent, ou sur des choses rondes qui tournent ; chaque idée vient d'un savoir précis.
    /// </summary>
    private (Assemblage, Recit)? ImaginerTransport(Humain h, double charge)
    {
        var idees = new List<(Assemblage, Recit, double)>();
        var lien = ChoisirLien(h);
        var lieAvant = lien != Matiere.Aucune;
        // Des rondelles percées, et l'idée de les enfiler sur un bâton : la plus élaborée, faite de deux savoirs.
        var percees = Combien(h, Matiere.RondellePercee);
        if (percees >= 1 && h.Sait(Effet.Percage) && h.Savoirs.Exists(s => s.Effet == Effet.Roule) && Combien(h, Matiere.Branche) > 0)
        {
            var k = Math.Min(percees, 1 + alea.Entier(4));
            idees.Add((new Assemblage(Matiere.Branche, 3 + alea.Entier(3), Matiere.RondellePercee, k, lien, Montage.Axe),
                new Recit("inv.axe", ("h", h), ("n", k)),
                3));
        }
        // Des troncs qui roulent : y poser la charge.
        if (h.Savoirs.Exists(s => s.Effet == Effet.Roule && s.A == Matiere.Tronc) && Combien(h, Matiere.Tronc) > 0)
            idees.Add((new Assemblage(Matiere.Tronc, 2 + alea.Entier(3), Matiere.Aucune, 0, Matiere.Aucune, Montage.Dessous),
                new Recit("inv.rouleaux", ("h", h)), 1.5));
        // Des choses rondes et courtes (rondelles, galets) glissées dessous : l'idée est bonne, la forme ne l'est pas.
        foreach (var rond in new[] { Matiere.Rondelle, Matiere.Galet })
        {
            if (!h.Savoirs.Exists(s => s.Effet == Effet.Roule && s.A == rond) || Combien(h, rond) < 1) continue;
            var n = Math.Min(Combien(h, rond), 2 + alea.Entier(3));
            idees.Add((new Assemblage(rond, Math.Max(1, n), Matiere.Aucune, 0, Matiere.Aucune, Montage.Dessous),
                new Recit("inv.dessous", ("h", h), ("m", QueNom(rond))), 1.5));
        }
        // Des branches liées côte à côte, comme le toit d'une hutte ou un lit de feuilles.
        if (Combien(h, Matiere.Branche) > 0 && (lieAvant || h.Sait(Effet.Abri)))
            idees.Add((new Assemblage(Matiere.Branche, 3 + alea.Entier(4), Matiere.Aucune, 0, lien, Montage.Cote),
                h.Sait(Effet.Abri)
                    ? new Recit("inv.traineau.hutte", ("h", h))
                    : new Recit("inv.traineau", ("h", h)), 1));
        if (idees.Count == 0) return null;
        var total = idees.Sum(i => i.Item3);
        var x = alea.Reel() * total;
        foreach (var (a, pensee, poids) in idees)
        {
            if ((x -= poids) <= 0) return (a, pensee);
        }
        return (idees[^1].Item1, idees[^1].Item2);
    }

    /// <summary>Traverser l'eau sans nager : lier côte à côte ce qu'il a vu flotter.</summary>
    private (Assemblage, Recit)? ImaginerRadeau(Humain h, double charge)
    {
        var flottants = h.Savoirs.Where(s => s.Effet == Effet.Flotte && Combien(h, s.A) > 0).Select(s => s.A).Distinct().ToList();
        if (flottants.Count == 0) return null;
        var m = flottants[alea.Entier(flottants.Count)];
        var n = m switch
        {
            Matiere.Tronc => 2 + alea.Entier(3),
            Matiere.Calebasse => 4 + alea.Entier(7),
            Matiere.Rondelle => 3 + alea.Entier(4),
            _ => 4 + alea.Entier(9),
        };
        n = Math.Min(n, Combien(h, m));
        var lien = ChoisirLien(h);
        if (lien == Matiere.Aucune)
            return (new Assemblage(m, 1, Matiere.Aucune, 0, Matiere.Aucune, Montage.Dessous),
                new Recit("inv.agrippe", ("h", h), ("m", QueNom(m))));
        return (new Assemblage(m, n, Matiere.Aucune, 0, lien, Montage.Cote),
            new Recit("inv.radeau", ("h", h), ("m", QueNom(m)), ("lien", Physique.Nom(lien))));
    }

    /// <summary>
    /// Un bateau pour la mer : un radeau plus grand et mieux lié de ce qu'il a vu flotter (troncs, roseaux, calebasses) ;
    /// ou, à qui sait que le tronc flotte, que la calebasse creuse garde l'eau dehors comme dedans et que le feu ronge le bois,
    /// l'idée d'évider un tronc.
    /// </summary>
    private (Assemblage, Recit)? ImaginerBateau(Humain h, double charge)
    {
        var idees = new List<(Assemblage, Recit, double)>();
        var lien = ChoisirLien(h);
        foreach (var m in h.Savoirs.Where(s => s.Effet == Effet.Flotte && Combien(h, s.A) > 0).Select(s => s.A).Distinct().OrderBy(m => m))
        {
            if (lien == Matiere.Aucune) break;
            var n = m switch
            {
                Matiere.Tronc => 4 + alea.Entier(5),
                Matiere.Roseau => 10 + alea.Entier(11),
                Matiere.Calebasse => 12 + alea.Entier(9),
                _ => 8 + alea.Entier(6),
            };
            n = Math.Min(n, Combien(h, m));
            idees.Add((new Assemblage(m, n, Matiere.Aucune, 0, lien, Montage.Cote),
                new Recit("inv.bateau.radeau", ("h", h), ("m", QueNom(m)), ("lien", Physique.Nom(lien))), h.Sait(Effet.Flottaison) ? 2 : 1));
        }
        if (h.Savoirs.Exists(s => s.Effet == Effet.Flotte && s.A == Matiere.Tronc) && Combien(h, Matiere.Tronc) > 0
            && (h.Sait(Effet.EauPortee) || h.Savoirs.Exists(s => s.A == Matiere.Calebasse)) && (h.Sait(Effet.Foyer) || h.Sait(Effet.Flamme)))
            idees.Add((new Assemblage(Matiere.Tronc, 1, Matiere.Aucune, 0, Matiere.Aucune, Montage.Creuse), new Recit("inv.bateau.coque", ("h", h)), 1.2));
        if (idees.Count == 0) return null;
        var x = alea.Reel() * idees.Sum(i => i.Item3);
        foreach (var (a, pensee, poids) in idees)
        {
            if ((x -= poids) <= 0) return (a, pensee);
        }
        return (idees[^1].Item1, idees[^1].Item2);
    }

    // ------------------------------------------------------------------ Essayer, retenir, transmettre

    /// <summary>
    /// Assemble l'objet et l'essaie. Réussi, il est retenu (savoir et objet), et ceux qui regardent peuvent l'imiter ;
    /// manqué, la raison est notée au journal quand c'était une idée nouvelle, et la confiance baisse sinon.
    /// </summary>
    private (Verdict Verdict, Objet? Objet) Essayer(Humain h, Besoin besoin, Assemblage a, double charge, OrigineSavoir origine, Recit? pensee,
        IReadOnlyList<Humain> tous, double km = 0)
    {
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        var v = Juger(h, besoin, a, charge, km);
        var connu = h.Savoirs.FindIndex(s => s.Geste == Geste.Assembler && s.Pieces == a.Signature && s.Effet != Effet.Rien);
        if (!v.Reussi)
        {
            if (connu >= 0) h.Savoirs[connu] = h.Savoirs[connu] with { Essais = h.Savoirs[connu].Essais + 1 };
            if (origine != OrigineSavoir.Essai || connu < 0)
            {
                var id = Noter(_maintenant, TypeEvenement.Decouverte, h, null,
                    new Recit("inv.mais", ("pensee", pensee ?? new Recit("inv.assemble", ("h", h), ("objet", Lois.Decrire(a)))), ("raison", v.Raison)));
                // Il retient que cela ne marche pas : il ne refera pas la même chose (tant qu'il s'en souvient).
                if (!h.Savoirs.Exists(s => s.Effet == Effet.Rien && s.Pieces == a.Signature))
                    h.Savoirs.Add(new Savoir(Geste.Assembler, a.Corps, a.Tete, Effet.Rien, 0, Symptome.Aucun, 1, 0, _maintenant, _maintenant,
                        origine, 0, id, a.Signature));
                if (v.Raison.Cle == "lois.r.echappent")
                    Jardin.Confier(h, TypeIntime.Question, 0, Recits.Recit.Seul("js.idee.tourner"),
                        40 + 30 * h.Personnalite.Ouverture, id, _maintenant, "idee:tourner");
            }
            return (v, null);
        }
        // Les pièces portées sont prises (le lien, la pierre, les rondelles) ; ce que la case offre ne manque pas.
        Consommer(h, a.Corps, a.NCorps);
        Consommer(h, a.Tete, a.NTete);
        Consommer(h, a.Lien, 1);
        var evenement = Retenir(h, Geste.Assembler, a.Corps, a.Tete, v.Effet, 0, Symptome.Aucun, origine, 0, tous, pieces: a.Signature,
            recit: pensee is null ? null : new Recit("inv.marche", ("pensee", pensee), ("raison", v.Raison)));
        if (evenement > 0) Montrer(h, Geste.Assembler, a.Corps, a.Tete, v.Effet, 0, Symptome.Aucun, tous, evenement, a.Signature);
        else Montrer(h, Geste.Assembler, a.Corps, a.Tete, v.Effet, 0, Symptome.Aucun, tous, 0, a.Signature);
        // Des troncs couchés sous la charge restent sur place ; le reste s'emporte.
        if (a.Montage == Montage.Dessous) return (v, null);
        var objet = new Objet(Matiere.Assemblage, v.Qualite, 1, Pieces: a.Signature);
        Prendre(h, objet);
        return (v, h.Objets.Contains(objet) ? objet : null);
    }

    private void Consommer(Humain h, Matiere m, int n)
    {
        if (m == Matiere.Aucune || n <= 0) return;
        if (Disponibles(h).Contains(m)) return;
        for (var k = 0; k < n; k++)
        {
            // Une poix : la chose qui colle, pas le pot ou le charbon qu'on porte aussi.
            var o = m == Matiere.Substance ? h.Objets.Find(x => x.Matiere == m && Chimie.Substance.Lire(x.Pieces) is { Collante: true })
                : h.Objets.Find(x => x.Matiere == m);
            if (o is null) return;
            h.Objets.Remove(o);
        }
    }

    /// <summary>L'objet assemblé a servi : le savoir qui l'a fait gagne en confiance.</summary>
    private void RenforcerAssemblage(Humain h, Objet o)
    {
        var i = h.Savoirs.FindIndex(s => s.Geste == Geste.Assembler && s.Pieces == o.Pieces);
        if (i >= 0) h.Savoirs[i] = h.Savoirs[i] with { Essais = h.Savoirs[i].Essais + 1, Succes = h.Savoirs[i].Succes + 1, UtiliseLe = _maintenant };
    }

    /// <summary>Deux objets assemblés qui servent à la même chose (on n'en garde qu'un).</summary>
    private static bool MemeUsage(Objet a, Objet b) =>
        Assemblage.Lire(a.Pieces) is { } x && Assemblage.Lire(b.Pieces) is { } y && Usage(x) == Usage(y);

    private static int Usage(Assemblage a) => a.Montage switch
    {
        Montage.Bout => 0,
        Montage.Travers => 1,
        Montage.Axe => 2,
        Montage.Cote when Lois.Portance(a) > 40 => 3,
        Montage.Creuse => 3,
        _ => 2,
    };

    /// <summary>
    /// Répondre à un besoin par un objet assemblé : refaire celui qu'on sait faire (en le retouchant parfois, c'est l'effet
    /// cliquet), sinon, parfois, en imaginer un. Renvoie le verdict de l'essai, ou null s'il n'a rien tenté.
    /// </summary>
    /// <summary>Répondre à un besoin à cet instant (pour les essais).</summary>
    internal (Verdict Verdict, Objet? Objet)? RepondreAuBesoin(Humain h, Besoin besoin, double charge, double envieDIdee, IReadOnlyList<Humain> tous,
        long maintenant)
    {
        _maintenant = maintenant;
        _temperature = Temperature(h.X, h.Y, maintenant);
        return Repondre(h, besoin, charge, envieDIdee, tous);
    }

    /// <summary>Recevoir la viande d'un proche à cet instant (pour les essais).</summary>
    internal void RecevoirViande(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        RecevoirViande(h, tous);
    }

    private (Verdict Verdict, Objet? Objet)? Repondre(Humain h, Besoin besoin, double charge, double envieDIdee, IReadOnlyList<Humain> tous,
        double km = 0)
    {
        var effets = EffetsDe(besoin);
        foreach (var s in h.Savoirs.Where(x => x.Geste == Geste.Assembler && effets.Contains(x.Effet) && x.Confiance >= 0.3)
                     .OrderByDescending(x => Array.IndexOf(effets, x.Effet) == 0 ? 1 : 0).ThenByDescending(x => x.Confiance).ToList())
        {
            if (Assemblage.Lire(s.Pieces) is not { } a || !APiecesPour(h, a)) continue;
            // Une retouche : un lien plus solide, une pièce de plus ou de moins. Gardée si elle fait mieux.
            if (alea.Reel() < 0.15 * Inventivite(h) && Retoucher(h, a) is { } variante && variante != a)
            {
                var essai = Juger(h, besoin, variante, charge, km);
                var base_ = Juger(h, besoin, a, charge, km);
                if (essai.Reussi && essai.Qualite > base_.Qualite + 0.02 && APiecesPour(h, variante))
                    return Essayer(h, besoin, variante, charge, OrigineSavoir.Reflexion,
                        new Recit("inv.refait", ("h", h), ("a", Lois.Decrire(a)), ("b", Lois.Decrire(variante))), tous, km);
            }
            return Essayer(h, besoin, a, charge, OrigineSavoir.Essai, null, tous, km);
        }
        if (alea.Reel() >= envieDIdee * Inventivite(h)) return null;
        if (Imaginer(h, besoin, charge) is not { } idee) return null;
        // Une idée qu'il a déjà vue échouer lui revient : il la rejette.
        if (h.Savoirs.Exists(s => s.Effet == Effet.Rien && s.Pieces == idee.Objet.Signature)) return null;
        // Des rondelles qui manquent : il en taille et les perce sur place, s'il sait le faire.
        return Essayer(h, besoin, idee.Objet, charge, OrigineSavoir.Reflexion, idee.Pensee, tous, km);
    }

    private Assemblage? Retoucher(Humain h, Assemblage a)
    {
        switch (alea.Entier(3))
        {
            case 0:
                var lien = ChoisirLien(h);
                return lien != Matiere.Aucune && Lois.De(lien).Lien > Lois.De(a.Lien).Lien ? a with { Lien = lien } : null;
            case 1:
                return a with { NCorps = Math.Clamp(a.NCorps + (alea.Reel() < 0.5 ? -1 : 1), 1, 10) };
            default:
                if (a.Montage != Montage.Axe) return null;
                var k = Math.Clamp(a.NTete + (alea.Reel() < 0.5 ? -1 : 1), 1, 4);
                return Combien(h, a.Tete) >= k ? a with { NTete = k } : null;
        }
    }

    // ------------------------------------------------------------------ Les besoins

    /// <summary>Après une chasse manquée : l'idée d'une meilleure arme peut venir à qui sait déjà tailler une pointe.</summary>
    private void ApresUneChasseManquee(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.Objets.Exists(o => Lois.Chasse(o) > 0.8)) return;
        Repondre(h, Besoin.Chasser, 0, 0.002, tous);
    }

    /// <summary>
    /// Après une prise : s'il a des proches qui ont faim, non loin, il voudrait leur rapporter la bête. Dans les bras, on
    /// n'emporte que ce qu'on a découpé ; une bête entière demande un moyen de la traîner. Ce qu'il emporte se gâte en
    /// quelques jours.
    /// </summary>
    private void Rapporter(Humain h, double mange, IReadOnlyList<Humain> tous)
    {
        if (h.Porte(Matiere.Carcasse)) return;
        Humain? affame = null;
        var km = 0.0;
        foreach (var l in h.Liens)
        {
            if (l.Affection < 10) continue;
            var d = tous[l.Id - 1];
            // Le compatissant n'attend pas que celui qui boite soit affamé pour lui rapporter sa part.
            var seuil = d.Sequelle.Membre != Membre.Aucun && Sequelles.Gene(d, _maintenant) >= Sequelles.Visible && h.Personnalite.Moral.Compassion > 0.5 ? 30 : 45;
            if (!d.EstVivant || d.Faim < seuil || d.EstBebe(_maintenant)) continue;
            var dist = Distance(h.X, h.Y, d.X, d.Y);
            if (dist <= 1 || dist > 8) continue;
            affame = d;
            km = dist * Nature.Echelle.MetresParCase / 1000.0;
            break;
        }
        if (affame is null) return;
        // Une bête de taille moyenne pour ce gibier (de 25 à 75 kg) ; la moitié environ se mange.
        var carcasse = 25 + 50 * alea.Reel();
        var reste = Math.Max(0, carcasse - mange);
        var viande = Math.Max(0, 0.5 * carcasse - mange);
        if (viande < 2) return;
        var vigueur = Force(h, _maintenant);
        var bras = Lois.PorterABras(vigueur);
        double emporte;
        Recit comment;
        var vehicule = h.Objets.FirstOrDefault(o => o.Matiere == Matiere.Assemblage && Assemblage.Lire(o.Pieces) is { } a
                                                     && a.Montage is Montage.Cote or Montage.Axe && Lois.Portance(a) <= 40
                                                     && Juger(h, Besoin.Rapporter, a, reste, km).Reussi);
        if (vehicule is not null)
        {
            UserObjet(h, vehicule, 0.04);
            RenforcerAssemblage(h, vehicule);
            emporte = viande;
            comment = new Recit("inv.sur", ("objet", Lois.Decrire(Assemblage.Lire(vehicule.Pieces)!)));
        }
        else
        {
            var lame = h.Porte(Matiere.Eclat) || h.Porte(Matiere.Obsidienne) || h.Objets.Exists(o => Lois.Coupe(o) > 0.3);
            var r = reste > bras ? Repondre(h, Besoin.Rapporter, reste, 0.04, tous, km) : null;
            if (r is { Verdict.Reussi: true } essai)
            {
                emporte = viande;
                comment = essai.Objet is { } o ? new Recit("inv.sur", ("objet", Lois.Decrire(Assemblage.Lire(o.Pieces)!))) : Recits.Recit.Seul("inv.surTroncs");
            }
            else if (reste <= bras)
            {
                emporte = viande;
                comment = Recits.Recit.Seul("inv.epaules");
            }
            else if (lame && h.Sait(Effet.Decoupe))
            {
                emporte = Math.Min(viande, bras);
                comment = Recits.Recit.Seul("inv.quartiers");
            }
            else return;
        }
        // Ce qu'il emporte en plus de ce qu'il a mangé est pris au gibier de la case.
        var unites = Ecosysteme.Prelever(ecosysteme.Gibier, carte.Indice(h.X, h.Y), Math.Clamp((int)Math.Ceiling(emporte / KgParPart), 1, 30));
        emporte = Math.Min(emporte, unites * KgParPart);
        if (emporte < 1) return;
        Prendre(h, new Objet(Matiere.Carcasse, Math.Clamp(emporte / 100, 0, 1), 1));
        Noter(_maintenant, TypeEvenement.Entraide, h, affame,
            new Recit("inv.emporte", ("h", h), ("kg", (long)Math.Round(emporte)), ("comment", comment), ("a", affame)), karma: Regard.NourrirLesAffames);
    }

    /// <summary>Viande d'une part de gibier, en kg.</summary>
    private const double KgParPart = 1.2;

    /// <summary>Faim apaisée par un kilo de viande.</summary>
    private const double FaimParKg = ValeurGibier / KgParPart;

    /// <summary>Celui qui porte de la viande va vers ses proches affamés (vrai s'il se met en route).</summary>
    private bool ApporterViande(Humain h, IReadOnlyList<Humain> tous)
    {
        var viande = h.Objets.Find(o => o.Matiere == Matiere.Carcasse);
        if (viande is null) return false;
        Humain? cible = null;
        var meilleure = int.MaxValue;
        foreach (var l in h.Liens)
        {
            if (l.Affection < 10) continue;
            var d = tous[l.Id - 1];
            if (!d.EstVivant || d.Faim < 40) continue;
            var dist = Distance(h.X, h.Y, d.X, d.Y);
            if (dist <= 1 || dist > 10 || dist >= meilleure) continue;
            (cible, meilleure) = (d, dist);
        }
        if (cible is null) return false;
        _autre = cible;
        Avancer(h, cible.X, cible.Y, Activite.RejoindreProche);
        return h.CibleX >= 0;
    }

    /// <summary>Un affamé reçoit de la viande d'un proche qui en porte, à côté de lui.</summary>
    private void RecevoirViande(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim < 30 || h.EstBebe(_maintenant)) return;
        foreach (var l in h.Liens)
        {
            var d = tous[l.Id - 1];
            if (!d.EstVivant || Distance(h.X, h.Y, d.X, d.Y) > 1) continue;
            var viande = d.Objets.Find(o => o.Matiere == Matiere.Carcasse);
            if (viande is null || d.AffectionPour(h.Id) < 0) continue;
            var kg = Math.Min(100 * viande.Qualite, h.Faim / FaimParKg);
            h.Faim = Math.Max(0, h.Faim - kg * FaimParKg);
            Entamer(d, kg);
            VieSociale.Gratitude(h, d, 1);
            Social?.Partager(d, h);
            return;
        }
    }

    // À part : la fermeture n'est créée que si la viande est vraiment donnée.
    private static void Entamer(Humain d, double kg) =>
        // Ceux qui en reçoivent dans le même pas se partagent ce qui reste.
        Tour.Changer(d, x =>
        {
            var i = x.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse);
            if (i < 0) return;
            var reste = x.Objets[i].Qualite - kg / 100;
            if (reste <= 0.005) x.Objets.RemoveAt(i);
            else x.Objets[i] = x.Objets[i] with { Qualite = reste };
        });

    /// <summary>Celui qui porte de la viande en mange quand il a faim.</summary>
    private bool MangerSaViande(Humain h)
    {
        var viande = h.Objets.Find(o => o.Matiere == Matiere.Carcasse);
        if (viande is null) return false;
        var kg = Math.Min(100 * viande.Qualite, h.Faim / FaimParKg);
        h.Faim = Math.Max(0, h.Faim - kg * FaimParKg);
        var reste = viande.Qualite - kg / 100;
        if (reste <= 0.005) h.Objets.Remove(viande);
        else h.Objets[h.Objets.IndexOf(viande)] = viande with { Qualite = reste };
        h.Activite = Activite.Cueillir;
        h.CibleX = h.CibleY = -1;
        return true;
    }

    /// <summary>
    /// L'eau barre la route et il ne sait pas nager : au bord, il refait le radeau qu'il connaît, ou, s'il a vu flotter le
    /// bois, il en a l'idée. Vrai s'il a passé ce moment à assembler.
    /// </summary>
    private bool Traverser(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || Radeau(h) is not null || !EauProfondeAPortee(h.X, h.Y)) return false;
        var r = Repondre(h, Besoin.Traverser, MasseCorps(h), 0.01, tous);
        return r is not null;
    }

    /// <summary>Le radeau qu'il a assemblé (et pas encore laissé sur la rive).</summary>
    private static Objet? Radeau(Humain h) =>
        h.Objets.Find(o => o.Matiere == Matiere.Assemblage && Assemblage.Lire(o.Pieces) is { } a
                           && (a.Montage == Montage.Cote && Lois.Portance(a) > 40 || a.Montage == Montage.Creuse));

    /// <summary>
    /// Le radeau reste au bord tant qu'on n'est pas parti ; une fois l'eau traversée, on le laisse sur la rive ; la viande
    /// emportée se gâte en trois jours environ.
    /// </summary>
    private void SuivreSesObjets(Humain h)
    {
        for (var i = h.Objets.Count - 1; i >= 0; i--)
        {
            var o = h.Objets[i];
            if (o.Matiere == Matiere.Carcasse)
            {
                // Fumée, elle se garde un mois environ (estimation) ; fraîche, trois jours par un temps doux. La chaleur hâte la
                // pourriture (elle double environ tous les 10 °C) ; gelée, la viande ne pourrit presque plus.
                var chaleur = _temperature < 0 ? 0.03 : Math.Pow(2, (Math.Min(_temperature, 40) - 20) / 10);
                var usure = chaleur * MondeSimule.SecondesParPas / ((o.Plein ? 30.0 : 3.0) * Calendrier.SecondesParJour);
                if (o.Usure - usure <= 0) h.Objets.RemoveAt(i);
                else h.Objets[i] = o with { Usure = o.Usure - usure };
                continue;
            }
            if (Radeau(h) != o) continue;
            var surLEau = !Climat.EstPraticable(carte.BiomeEn(h.X, h.Y));
            if (surLEau) h.Objets[i] = o with { Plein = true };
            else if (TientLaMer(o) && (h.Mer.Active || DistanceMer(h.X, h.Y) <= 3))
            {
                // Un bateau de mer reste tiré sur le rivage où l'on vit : il s'use en un an ou deux.
                var usure = o.Usure - MondeSimule.SecondesParPas / (1.5 * Calendrier.SecondesParAn);
                if (usure <= 0) h.Objets.RemoveAt(i);
                else h.Objets[i] = o with { Usure = usure, Plein = false };
            }
            else if (o.Plein) h.Objets.RemoveAt(i);
            else if (o.Usure <= 0.03) h.Objets.RemoveAt(i);
            else h.Objets[i] = o with { Usure = o.Usure - 0.03 };
        }
    }

    /// <summary>
    /// Au calme : refaire l'arme ou la hache qu'on sait assembler, en avoir l'idée quand on se blesse les mains à casser
    /// du bois, tailler une rondelle dans un tronc pour le feu, et, à qui a vu les rondelles s'échapper, percer un trou
    /// en y faisant tourner une pointe, comme on fait tourner le bâton à feu.
    /// </summary>
    private bool Assembler(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant)) return false;
        // Refaire l'arme qu'on sait faire.
        if (!h.Objets.Exists(o => Lois.Chasse(o) > 0.8) && h.Savoirs.Exists(s => s.Effet == Effet.PointeEmmanchee && s.Geste == Geste.Assembler)
            && Repondre(h, Besoin.Chasser, 0, 0, tous) is not null) return true;
        var hache = h.Objets.Find(o => Lois.Coupe(o) > 0.3);
        if (hache is null && (h.Sait(Effet.Abri) || h.Sait(Effet.Foyer)) && Repondre(h, Besoin.Couper, 0, 0.0004, tous) is not null) return true;
        // Une hache en main, un tronc à terre : on en tranche une rondelle (du bois pour le feu, ou pour autre chose).
        if (hache is not null && !h.Porte(Matiere.Rondelle) && Combien(h, Matiere.Tronc) > 0
            && (FeuIci(h) || h.Sait(Effet.Rondelle)) && alea.Reel() < 0.02)
        {
            var effet = Faire(h, Geste.Trancher, Matiere.Tronc, Matiere.Assemblage, OrigineSavoir.Essai, tous);
            if (effet == Effet.Rondelle && Installations.FoyerEn(h.X, h.Y) is not null)
            {
                var (fx, fy, bois) = (h.X, h.Y, 2 + 2 * alea.Reel());
                Tour.Differer(() =>
                {
                    if (Installations.FoyerEn(fx, fy) is { } f) Installations.Alimenter(f, bois);
                });
            }
            h.Activite = Activite.Fabriquer;
            h.CibleX = h.CibleY = -1;
            return true;
        }
        // Percer une rondelle : il faut l'avoir vue rouler, et savoir qu'on use le bois en y faisant tourner quelque chose.
        if (h.Porte(Matiere.Rondelle) && !h.Sait(Effet.Percage) && h.Savoirs.Exists(s => s.Effet == Effet.Roule)
            && (h.Sait(Effet.Tiedeur) || h.Savoirs.Exists(s => s.Effet == Effet.Flamme && s.Geste == Geste.Frotter) || h.Sait(Effet.Pointe))
            && new[] { Matiere.Eclat, Matiere.Obsidienne }.FirstOrDefault(m => Combien(h, m) > 0) is var meche && meche != Matiere.Aucune
            && alea.Reel() < 0.005 * Inventivite(h))
        {
            h.Activite = Activite.Fabriquer;
            h.CibleX = h.CibleY = -1;
            var r = Physique.Tenter(Geste.Percer, Matiere.Rondelle, meche, Contexte(h), alea);
            var pensee = new Recit("inv.percer", ("h", h), ("meche", Physique.Nom(meche)));
            if (r.Effet == Effet.Percage)
            {
                var id = Retenir(h, Geste.Percer, Matiere.Rondelle, meche, Effet.Percage, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
                    recit: new Recit("inv.percer.reussi", ("pensee", pensee)));
                Produire(h, Geste.Percer, Matiere.Rondelle, meche, r, id);
                Montrer(h, Geste.Percer, Matiere.Rondelle, meche, Effet.Percage, 0, Symptome.Aucun, tous, id);
            }
            else Noter(_maintenant, TypeEvenement.Decouverte, h, null, new Recit("inv.percer.rate", ("pensee", pensee)));
            return true;
        }
        // Qui sait percer en refait, pour la rondelle qu'il porte.
        if (h.Porte(Matiere.Rondelle) && h.Sait(Effet.Percage) && h.Savoirs.Exists(s => s.Geste == Geste.Assembler && s.Effet == Effet.RouleSurAxe)
            && h.Objets.Count(o => o.Matiere == Matiere.RondellePercee) < 2
            && h.Savoirs.Where(s => s.Effet == Effet.Percage && s.A == Matiere.Rondelle && APortee(h, s.B)).MaxBy(s => s.Confiance) is { } percer)
            return Fabriquer(h, Geste.Percer, Matiere.Rondelle, percer.B, tous);
        return false;
    }

    /// <summary>« avec une corde. »</summary>
    private static Recit AvecLien(Matiere lien) => new("inv.avecLien", ("lien", Physique.Nom(lien)));

    /// <summary>La matière dite après « se souvient que » : « le bois sec » (et non « du bois sec »), « une calebasse ».</summary>
    private static Recit QueNom(Matiere m) => m == Matiere.BoisSec ? Recits.Recit.Seul("inv.leBoisSec") : Physique.Nom(m);
}
