using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// La spécialisation, telle qu'elle naît : personne ne décrète un métier. Quand quelqu'un fait une chose nettement mieux que
/// les autres, on vient lui demander de la faire (une pointe, une hache, un vêtement de peau) ; il la fait, contre de la viande,
/// un objet ou simplement de l'estime. À force, les siens voient en lui « celui qui taille », comme ils voient déjà le porteur
/// du feu et le guérisseur. Il pratique davantage, donc il s'améliore, donc on lui demande davantage ; les autres pratiquent
/// moins ce geste, et s'il meurt sans élève, le groupe peut le perdre d'un coup.
/// <para>
/// Ce que dit la recherche : même chez les chasseurs-cueilleurs, certains sont connus pour mieux faire tel objet, et on le leur
/// demande (Stout, 2002, les tailleurs d'herminettes de Langda, en Nouvelle-Guinée, et leurs années d'apprentissage) ; la
/// division du travail grandit avec la taille et la densité du groupe, et le spécialiste en tire du prestige, des biens si
/// l'on admet que les choses sont à quelqu'un (Henrich et Boyd, 2008 ; Kuhn et Stiner, 2006).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Savoirs qui font chaque ouvrage.</summary>
    private static bool Fait(Ouvrage o, Savoir s) => o switch
    {
        Ouvrage.Taille => s.Espece == 0 && s.Effet is Effet.Eclat or Effet.Pointe,
        Ouvrage.Assemblage => s.Geste == Geste.Assembler && s.Effet is Effet.PointeEmmanchee or Effet.TranchantEmmanche,
        Ouvrage.Peau => s.Effet == Effet.Couvert && s.A == Matiere.Peau,
        _ => false,
    };

    /// <summary>Ce qui lui manque et qu'un autre pourrait faire pour lui (Aucun : rien).</summary>
    private Ouvrage Manque(Humain h)
    {
        if (h.Porte(Matiere.Peau) && h.Vetement is not { Usure: > 0.2 } && _temperature < 16) return Ouvrage.Peau;
        var arme = h.Porte(Matiere.Epieu) || h.Porte(Matiere.BatonPointu) || h.Objets.Exists(o => Lois.Chasse(o) > 0.8);
        var lame = h.Porte(Matiere.Eclat) || h.Porte(Matiere.Obsidienne);
        if (!arme && (h.Sait(Effet.Chasse) || h.Sait(Effet.Pointe) || h.Sait(Effet.PointeEmmanchee))) return h.Sait(Effet.PointeEmmanchee) ? Ouvrage.Assemblage : Ouvrage.Taille;
        if (!lame && (h.Sait(Effet.Decoupe) || h.Sait(Effet.Eclat) || h.Sait(Effet.Pointe))) return Ouvrage.Taille;
        if (!h.Objets.Exists(o => Lois.Coupe(o) > 0.3) && h.Sait(Effet.TranchantEmmanche)) return Ouvrage.Assemblage;
        return Ouvrage.Aucun;
    }

    /// <summary>Son adresse au meilleur savoir qu'il a pour cet ouvrage (0 s'il ne sait pas le faire).</summary>
    private static double Habilete(Humain h, Ouvrage o, double confiance)
    {
        var mieux = 0.0;
        foreach (var s in h.Savoirs)
            if (Fait(o, s) && s.Confiance >= confiance) mieux = Math.Max(mieux, AdressePour(h, s));
        return mieux;
    }

    /// <summary>Demander un ouvrage à cet instant (pour les essais).</summary>
    internal bool DemanderUnOuvrage(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _temperature = Temperature(h.X, h.Y, maintenant);
        return DemanderUnOuvrage(h, tous);
    }

    /// <summary>Faire l'ouvrage qu'on vous demande, à cet instant (pour les essais).</summary>
    internal bool Servir(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _temperature = Temperature(h.X, h.Y, maintenant);
        return Servir(h, tous);
    }

    /// <summary>
    /// Au calme, il lui manque une pointe, une hache, un vêtement, et quelqu'un des siens, tout près, le fait nettement mieux
    /// que lui (ou lui ne sait pas le faire) : il va le lui demander. Celui que les siens disent spécialiste attire les demandes ;
    /// les timides et les fiers demandent moins. Vrai s'il se met en route.
    /// </summary>
    private bool DemanderUnOuvrage(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.CommunauteId == 0 || h.DemandeAId != 0) return false;
        var p = h.Personnalite;
        if (alea.Reel() >= 0.03 * (0.5 + p.Conscience)) return false;
        var ouvrage = Manque(h);
        if (ouvrage == Ouvrage.Aucun) return false;
        var sienne = Habilete(h, ouvrage, 0.4);
        var rayon = Math.Min(RayonPerception(h), VieSociale.DistanceCommunaute);
        Humain? artisan = null;
        var meilleur = 0.0;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.Id == h.Id || o.CommunauteId != h.CommunauteId || o.EstEnfant(_maintenant) || o.Activite == Activite.Dormir) continue;
            if (o.DemandeAId != 0 || o.VisiteChezId != 0 || o.CaptifDe > 0 || Distance(o.X, o.Y, h.X, h.Y) > rayon) continue;
            var affection = h.AffectionPour(o.Id);
            if (affection < -10) continue;
            var sa = 0.0;
            foreach (var s in o.Savoirs)
                if (Fait(ouvrage, s) && s.Confiance >= 0.6 && s.Succes >= 3) sa = Math.Max(sa, AdressePour(o, s));
            // Il faut que l'écart se voie : quelques coups de plus réussis ne suffisent pas.
            var ecart = Math.Clamp((sa - sienne - 0.05) / 0.25, 0, 1);
            if (ecart <= 0) continue;
            var poids = ecart * (o.Specialite == ouvrage ? 1.5 : 1) * (0.5 + affection / 100);
            if (poids <= meilleur) continue;
            (artisan, meilleur) = (o, poids);
        }
        if (artisan is null || alea.Reel() >= meilleur * (0.3 + 0.4 * p.Extraversion + 0.3 * p.Agreabilite)) return false;
        h.DemandeAId = artisan.Id;
        h.DemandeOuvrage = ouvrage;
        h.DemandeLe = _maintenant;
        _autre = artisan;
        if (Distance(h.X, h.Y, artisan.X, artisan.Y) > 1) Avancer(h, artisan.X, artisan.Y, Activite.Demander);
        else
        {
            h.Activite = Activite.Demander;
            h.CibleX = h.CibleY = -1;
        }
        return true;
    }

    /// <summary>
    /// Il a demandé un ouvrage : il va vers l'artisan et attend près de lui, et le regarde faire. Il se lasse au bout d'un jour ou
    /// deux selon sa patience, ou quand l'artisan est parti. Vrai s'il passe ce moment à attendre.
    /// </summary>
    private bool AttendreSonOuvrage(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.DemandeAId == 0) return false;
        var artisan = tous[h.DemandeAId - 1];
        var p = h.Personnalite;
        var patience = (0.5 + p.Conscience + p.Agreabilite) * Calendrier.SecondesParJour;
        if (!artisan.EstVivant || artisan.VisiteChezId != 0 || _maintenant - h.DemandeLe > patience || Manque(h) != h.DemandeOuvrage)
        {
            h.DemandeAId = 0;
            return false;
        }
        _autre = artisan;
        if (Distance(h.X, h.Y, artisan.X, artisan.Y) > 1)
        {
            Avancer(h, artisan.X, artisan.Y, Activite.Demander);
            if (h.CibleX >= 0) return true;
            h.DemandeAId = 0;
            return false;
        }
        h.Activite = Activite.Demander;
        h.CibleX = h.CibleY = -1;
        return true;
    }

    /// <summary>
    /// Quelqu'un attend près de lui l'ouvrage qu'il lui a demandé : il s'y met, le refuse, ou le fait attendre. L'envie vient de
    /// la bonté et du sérieux, de l'affection pour celui qui demande, et de la fierté de celui que les siens disent spécialiste ;
    /// la fatigue la retient. Le geste est fait sous les yeux de celui qui attend, qui peut l'apprendre. Vrai s'il y a passé ce moment.
    /// </summary>
    private bool Servir(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant)) return false;
        Humain? demandeur = null;
        foreach (var o in tous.Vivants())
        {
            if (o.DemandeAId != h.Id || !o.EstVivant || Distance(o.X, o.Y, h.X, h.Y) > 1) continue;
            demandeur = o;
            break;
        }
        if (demandeur is null || alea.Reel() >= 0.25) return false;
        var p = h.Personnalite;
        var ouvrage = demandeur.DemandeOuvrage;
        var envie = Math.Clamp((0.3 + 0.4 * p.Agreabilite + 0.3 * p.Conscience) * (1 - 0.7 * Psyche.Froideur(p)) + h.AffectionPour(demandeur.Id) / 150
                               + (h.Specialite == ouvrage ? 0.2 : 0) - h.Fatigue / 200, 0.02, 0.98);
        _autre = demandeur;
        var (hi, di, le) = (h.Id, demandeur.Id, _maintenant);
        if (alea.Reel() >= envie)
        {
            Noter(_maintenant, TypeEvenement.Conflit, h, demandeur, new Recit("ouv.refus", ("d", demandeur), ("h", h), ("quoi", Quoi(ouvrage))));
            Tour.Differer(tous, t =>
            {
                if (t[di - 1].DemandeAId == hi) t[di - 1].DemandeAId = 0;
                Social?.ChangerAffection(t[di - 1], t[hi - 1], -2, le);
            });
            return true;
        }
        // Sans de quoi le faire maintenant, il n'y passe pas son temps : celui qui attend attendra.
        return Faconner(h, demandeur, ouvrage, tous) is not null;
    }

    /// <summary>« de lui tailler une pointe », pour le journal.</summary>
    private static Recit Quoi(Ouvrage o) => Recit.Seul(o switch
    {
        Ouvrage.Taille => "ouv.quoi.Taille",
        Ouvrage.Assemblage => "ouv.quoi.Assemblage",
        _ => "ouv.quoi.Peau",
    });

    /// <summary>
    /// Fait l'ouvrage demandé avec ce qu'il a sous la main, et le remet après le pas. Null s'il n'a pas de quoi le faire maintenant
    /// (celui qui attend attendra) ; sinon vrai, que le geste ait réussi ou non.
    /// </summary>
    private bool? Faconner(Humain h, Humain demandeur, Ouvrage ouvrage, IReadOnlyList<Humain> tous)
    {
        var arme = demandeur.Porte(Matiere.Epieu) || demandeur.Porte(Matiere.BatonPointu) || demandeur.Objets.Exists(o => Lois.Chasse(o) > 0.8);
        Objet? produit = null;
        var vetement = false;
        Recit fait;
        switch (ouvrage)
        {
            case Ouvrage.Taille:
            {
                var voulu = !arme ? Effet.Pointe : Effet.Eclat;
                var s = h.Savoirs.Where(x => x.Effet == voulu && x.Espece == 0 && x.Confiance >= 0.4 && APortee(h, x.A) && APortee(h, x.B)).MaxBy(x => x.Succes)
                        ?? h.Savoirs.Where(x => x.Effet == Effet.Eclat && x.Espece == 0 && x.Confiance >= 0.4 && APortee(h, x.A) && APortee(h, x.B)).MaxBy(x => x.Succes);
                if (s is null) return null;
                Ouvrer(h);
                var r = Physique.Tenter(s.Geste, s.A, s.B, Contexte(h, s), alea);
                if (r.Effet != s.Effet)
                {
                    Retenir(h, s.Geste, s.A, s.B, s.Effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous, succes: false);
                    return true;
                }
                if (s.Effet == Effet.Pointe) User(h, s.B, 0.1);
                Retenir(h, s.Geste, s.A, s.B, s.Effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
                Montrer(h, s.Geste, s.A, s.B, s.Effet, 0, Symptome.Aucun, tous, 0);
                produit = new Objet(r.Produit, r.Qualite, 1);
                fait = new Recit("ouv.fait.taille", ("o", Physique.Nom(r.Produit)));
                break;
            }
            case Ouvrage.Assemblage:
            {
                var voulu = !arme ? Effet.PointeEmmanchee : Effet.TranchantEmmanche;
                var besoin = !arme ? Besoin.Chasser : Besoin.Couper;
                Assemblage? a = null;
                Savoir? s = null;
                foreach (var x in h.Savoirs.Where(x => x.Geste == Geste.Assembler && x.Effet == voulu && x.Confiance >= 0.3).OrderByDescending(x => x.Succes))
                {
                    if (Assemblage.Lire(x.Pieces) is not { } l || !APiecesPour(h, l)) continue;
                    (a, s) = (l, x);
                    break;
                }
                if (a is null || s is null) return null;
                Ouvrer(h);
                var v = Juger(h, besoin, a, 0);
                if (!v.Reussi)
                {
                    var i = h.Savoirs.IndexOf(s);
                    h.Savoirs[i] = s with { Essais = s.Essais + 1 };
                    return true;
                }
                Consommer(h, a.Corps, a.NCorps);
                Consommer(h, a.Tete, a.NTete);
                Consommer(h, a.Lien, 1);
                Retenir(h, Geste.Assembler, a.Corps, a.Tete, v.Effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous, pieces: a.Signature);
                Montrer(h, Geste.Assembler, a.Corps, a.Tete, v.Effet, 0, Symptome.Aucun, tous, 0, a.Signature);
                produit = new Objet(Matiere.Assemblage, v.Qualite, 1, Pieces: a.Signature);
                fait = new Recit("ouv.fait.assemble", ("o", Lois.Decrire(a)));
                break;
            }
            default:
            {
                var s = h.Savoirs.Where(x => Fait(Ouvrage.Peau, x) && x.Confiance >= 0.4).MaxBy(x => x.Succes);
                var peau = demandeur.Objets.Find(o => o.Matiere == Matiere.Peau);
                if (s is null || peau is null) return null;
                Ouvrer(h);
                var r = Physique.Tenter(Geste.SeCouvrir, Matiere.Peau, Matiere.Aucune, Contexte(h, s), alea);
                if (r.Effet != Effet.Couvert)
                {
                    Retenir(h, s.Geste, s.A, s.B, s.Effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous, succes: false);
                    return true;
                }
                Retenir(h, s.Geste, s.A, s.B, s.Effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
                Montrer(h, s.Geste, s.A, s.B, s.Effet, 0, Symptome.Aucun, tous, 0);
                produit = new Objet(Matiere.Peau, Math.Min(1, peau.Qualite * r.Qualite / 0.6), 1);
                vetement = true;
                fait = new Recit("ouv.fait.vetement", ("d", demandeur));
                break;
            }
        }
        var (hi, di, le, objet, habit) = (h.Id, demandeur.Id, _maintenant, produit, vetement);
        Tour.Differer(tous, t => Livrer(t[hi - 1], t[di - 1], ouvrage, objet, habit, fait, t, le));
        return true;
    }

    private static void Ouvrer(Humain h)
    {
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
    }

    /// <summary>
    /// Après le pas : l'ouvrage est remis, et celui qui l'a demandé le paie. Là où l'on tient que les choses sont à quelqu'un, il le
    /// paie d'une part de viande ou d'un objet ; ailleurs, l'artisan n'y gagne que de l'estime, et un généreux lui donne parfois
    /// quelque chose en retour. Celui qui ne l'attendait plus ne le reçoit pas : l'artisan le garde.
    /// </summary>
    internal void Livrer(Humain artisan, Humain demandeur, Ouvrage ouvrage, Objet objet, bool vetement, Recit fait, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!demandeur.EstVivant || demandeur.DemandeAId != artisan.Id)
        {
            if (!vetement) Prendre(artisan, objet);
            return;
        }
        demandeur.DemandeAId = 0;
        if (vetement)
        {
            var peau = demandeur.Objets.Find(o => o.Matiere == Matiere.Peau);
            if (peau is not null) demandeur.Objets.Remove(peau);
            demandeur.Vetement = objet;
        }
        else Prendre(demandeur, objet);

        var p = demandeur.Personnalite;
        var asoi = Social?.CommunauteDe(demandeur) is { IdeeReserve: IdeeReserve.ASoi };
        var generosite = (0.5 * p.Agreabilite + 0.3 * p.Moral.Compassion + 0.2 * p.Moral.Fondements.Equite) * (1 - 0.8 * Psyche.Froideur(p));
        object contre = "";
        var redevable = false;
        if (asoi || alea.Reel() < 0.35 * generosite)
        {
            var iv = demandeur.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse);
            var troc = demandeur.Objets.Find(o => o.Matiere is not (Matiere.Carcasse or Matiere.Brandon or Matiere.PorteFeu) && o != objet
                                                  && !artisan.Objets.Exists(x => x.Matiere == o.Matiere && x.Pieces == o.Pieces));
            if (iv >= 0)
            {
                var kg = Math.Min(100 * demandeur.Objets[iv].Qualite, 2 + 2 * alea.Reel());
                var reste = demandeur.Objets[iv].Qualite - kg / 100;
                if (reste <= 0.005) demandeur.Objets.RemoveAt(iv);
                else demandeur.Objets[iv] = demandeur.Objets[iv] with { Qualite = reste };
                var sienne = artisan.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse);
                if (sienne >= 0) artisan.Objets[sienne] = artisan.Objets[sienne] with { Qualite = Math.Min(1, artisan.Objets[sienne].Qualite + kg / 100) };
                else Prendre(artisan, new Objet(Matiere.Carcasse, kg / 100, 1));
                contre = Recit.Seul(asoi ? "ouv.contre.viande" : "ouv.retour.viande");
            }
            else if (troc is not null && artisan.Objets.Count < ObjetsMax)
            {
                demandeur.Objets.Remove(troc);
                artisan.Objets.Add(troc);
                contre = new Recit(asoi ? "ouv.contre" : "ouv.retour", ("o", Physique.Nom(troc.Matiere)));
            }
            else if (asoi)
            {
                contre = Recit.Seul("ouv.redevable");
                redevable = true;
            }
        }
        switch (ouvrage)
        {
            case Ouvrage.Taille: artisan.ServisTaille++; break;
            case Ouvrage.Assemblage: artisan.ServisAssemblage++; break;
            default: artisan.ServisPeau++; break;
        }
        VieSociale.Gratitude(artisan, demandeur, asoi ? 2 : 4);
        var id = Noter(maintenant, TypeEvenement.Entraide, artisan, demandeur, new Recit("ouv.service", ("d", demandeur), ("a", artisan), ("fait", fait), ("contre", contre)),
            karma: asoi && contre is Recit && !redevable ? 0 : Regard.Partage);
        Reconnaitre(artisan, ouvrage, tous, id, maintenant);
    }

    /// <summary>
    /// Celui qui a fait cet ouvrage pour d'autres plus souvent que tous les siens, et plus de deux fois, devient pour eux celui qui
    /// le fait : ce n'est pas un titre, c'est ce qu'on dit de lui. S'il l'était pour un autre ouvrage plus demandé, il le reste.
    /// </summary>
    private void Reconnaitre(Humain h, Ouvrage ouvrage, IReadOnlyList<Humain> tous, long cause, long maintenant)
    {
        var n = h.Servis(ouvrage);
        if (n < 3 || h.Specialite == ouvrage || h.CommunauteId == 0) return;
        if (h.Specialite != Ouvrage.Aucun && h.Servis(h.Specialite) >= n) return;
        Humain? ancien = null;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.Id == h.Id || o.CommunauteId != h.CommunauteId) continue;
            if (o.Servis(ouvrage) >= n) return;
            if (o.Specialite == ouvrage) ancien = o;
        }
        h.Specialite = ouvrage;
        h.SpecialiteDepuis = maintenant;
        if (ancien is not null) ancien.Specialite = Ouvrage.Aucun;
        var role = Recit.Seul(ouvrage switch
        {
            Ouvrage.Taille => "ouv.role.Taille",
            Ouvrage.Assemblage => "ouv.role.Assemblage",
            _ => "ouv.role.Peau",
        });
        Noter(maintenant, TypeEvenement.Decouverte, h, ancien,
            ancien is null ? new Recit("ouv.reconnu", ("h", h), ("role", role)) : new Recit("ouv.reconnu.ancien", ("h", h), ("role", role), ("a", ancien)),
            cause);
    }

    /// <summary>Ce que les siens disent qu'il fait pour eux, pour l'observateur (null : rien).</summary>
    public static string? Metier(Humain h)
    {
        var f = h.Sexe == Sexe.Femme;
        return h.Specialite switch
        {
            Ouvrage.Taille => f ? "tailleuse de pierre" : "tailleur de pierre",
            Ouvrage.Assemblage => f ? "faiseuse d'armes et de haches" : "faiseur d'armes et de haches",
            Ouvrage.Peau => f ? "faiseuse de vêtements de peau" : "faiseur de vêtements de peau",
            _ => null,
        };
    }
}
