using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Garder l'eau et la nourriture pour plus tard. Les San enterrent sur leurs routes de saison sèche des œufs d'autruche pleins
/// d'eau (Lee, 1979) ; on sèche la viande et les fruits au soleil, on amasse les noix et les graines à l'automne, et dans le grand
/// froid la viande cachée sous des pierres gèle et tient tout l'hiver (Binford, 1978, chez les Nunamiut). Chaque façon se découvre,
/// a ses lois (ce qui sèche bien, ce qui moisit, ce que les bêtes déterrent) et se transmet ou se perd.
/// <para>
/// Une réserve retient sur place et pose la question du partage. Rien n'est décrété : celui qui l'a faite y puise pour lui et les
/// siens ; les autres y prennent comme on partage, ou y volent. Une communauté peut inventer d'elle-même que la réserve est à tous,
/// ou qu'elle est à celui qui l'a faite (voir <see cref="IdeeReserve"/>). Chez les chasseurs-cueilleurs, le stockage va de pair avec
/// un mode de vie plus sédentaire et l'apparition d'inégalités (Testart, 1982 ; Binford, 1980).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Faim d'une journée sans manger.</summary>
    private const double FaimParJour = FaimParPas * (Calendrier.SecondesParJour / MondeSimule.SecondesParPas);

    /// <summary>Soif apaisée par un litre d'eau (une journée de soif demande deux litres et demi environ).</summary>
    private const double SoifParLitre = 30;

    /// <summary>Terres trop humides pour sécher quoi que ce soit : tout y moisit.</summary>
    private static bool Humide(Biome b) => b is Biome.Marais or Biome.ForetTropicale or Biome.Riviere or Biome.Mangrove;

    private static bool EstNourriture(SorteReserve s) => s != SorteReserve.Eau;

    /// <summary>
    /// Au calme : mettre de côté ce qu'on a en trop, si l'on sait le garder, et peut-être découvrir comment. Vrai s'il s'y est mis.
    /// </summary>
    private bool FaireDesReserves(Humain h, IReadOnlyList<Humain> tous)
    {
        // En route (vers la fumée, un proche, de meilleures terres), on ne s'arrête pas pour amasser.
        if (h.EstEnfant(_maintenant) || h.CaptifDe != 0 || h.Activite == Activite.Dormir || h.CibleX >= 0) return false;
        var viande = h.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse && !o.Plein);
        if (viande >= 0)
        {
            DecouvrirLeSechage(h, tous);
            DecouvrirLaCacheFroide(h, tous);
            if (GarderSaViande(h, viande, tous)) return true;
        }
        if (CacherDeLEau(h, tous)) return true;
        return Amasser(h, tous);
    }

    /// <summary>Viande fraîche qu'il porte en plus de ce qu'il mangera aujourd'hui, en kilos.</summary>
    private static double Surplus(Humain h, Objet viande) => 100 * viande.Qualite - (h.Faim + FaimParJour) / FaimParKg;

    /// <summary>Un morceau oublié sur une pierre chaude, par temps sec : il a séché, et il est encore bon des jours plus tard.</summary>
    private void DecouvrirLeSechage(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Sait(Effet.Sechage) || !Calendrier.Decomposer(_maintenant).EstJour || _temperature < 20 || Humide(carte.BiomeEn(h.X, h.Y))) return;
        if (Sec(h) < 0.6 || alea.Reel() >= 0.0005 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h))) return;
        var id = Retenir(h, Geste.Secher, Matiere.Carcasse, Matiere.Aucune, Effet.Sechage, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            recit: new Recit("res.sechage", ("h", h)));
        Montrer(h, Geste.Secher, Matiere.Carcasse, Matiere.Aucune, Effet.Sechage, 0, Symptome.Aucun, tous, id);
    }

    /// <summary>Dans le grand froid, la viande qu'il porte depuis des jours a gelé, et n'a pas pourri.</summary>
    private void DecouvrirLaCacheFroide(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Sait(Effet.CacheFroide) || _temperature >= 0) return;
        if (alea.Reel() >= 0.002 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h))) return;
        var id = Retenir(h, Geste.Cacher, Matiere.Carcasse, Matiere.Aucune, Effet.CacheFroide, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            recit: new Recit("res.gel", ("h", h)));
        Montrer(h, Geste.Cacher, Matiere.Carcasse, Matiere.Aucune, Effet.CacheFroide, 0, Symptome.Aucun, tous, id);
    }

    /// <summary>
    /// Il porte plus de viande qu'il n'en mangera : s'il sait la garder, il la sèche au soleil par temps sec, ou, dans le froid, la
    /// cache sous des pierres. Vrai s'il l'a fait.
    /// </summary>
    private bool GarderSaViande(Humain h, int indice, IReadOnlyList<Humain> tous)
    {
        var viande = h.Objets[indice];
        var surplus = Surplus(h, viande);
        if (surplus < 2) return false;
        var biome = carte.BiomeEn(h.X, h.Y);
        SorteReserve sorte;
        double kg, qualite;
        Geste geste;
        Effet effet;
        if (_temperature < 4 && h.Savoirs.Exists(s => s.Effet == Effet.CacheFroide && s.Confiance >= 0.4))
        {
            (sorte, geste, effet, kg) = (SorteReserve.ViandeCachee, Geste.Cacher, Effet.CacheFroide, surplus);
            // De lourdes pierres empilées gardent mieux des bêtes qu'une fosse vite couverte.
            qualite = Math.Clamp((APortee(h, Matiere.Galet) ? 0.5 : 0.2) + 0.4 * Force(h, _maintenant) * alea.Reel(), 0, 1);
        }
        else if (Calendrier.Decomposer(_maintenant).EstJour && _temperature >= 15 && !Humide(biome) && Sec(h) >= 0.5
                 && h.Savoirs.Exists(s => s.Effet == Effet.Sechage && s.Confiance >= 0.4))
        {
            // Séchée, la viande perd les deux tiers de son poids (son eau), pas ce qu'elle nourrit.
            (sorte, geste, effet, kg) = (SorteReserve.ViandeSechee, Geste.Secher, Effet.Sechage, surplus * FaimParKg / Reserve.FaimParKg(SorteReserve.ViandeSechee));
            // Des lanières fines sèchent mieux et moisissent moins.
            qualite = Math.Clamp(0.3 + 0.6 * h.Personnalite.Adresse * (0.5 + 0.5 * alea.Reel()), 0, 1);
        }
        else return false;
        if (alea.Reel() >= 0.1 * (0.4 + h.Personnalite.Conscience)) return false;
        var reste = viande.Qualite - surplus / 100;
        if (reste <= 0.005) h.Objets.RemoveAt(indice);
        else h.Objets[indice] = viande with { Qualite = reste };
        Poser(h, sorte, kg, qualite, geste, Matiere.Carcasse, effet, tous);
        return true;
    }

    /// <summary>
    /// Pose une réserve là où il est, s'en souvient comme d'un point d'eau ou d'un lieu où manger, et exerce son savoir ; ceux qui
    /// le regardent peuvent l'imiter.
    /// </summary>
    private void Poser(Humain h, SorteReserve sorte, double kg, double qualite, Geste geste, Matiere matiere, Effet effet, IReadOnlyList<Humain> tous)
    {
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        var id = Retenir(h, geste, matiere, Matiere.Aucune, effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        Montrer(h, geste, matiere, Matiere.Aucune, effet, 0, Symptome.Aucun, tous, id);
        // Ce qu'on ramasse en chemin rejoint la cache qu'on a déjà tout près, plutôt qu'une cache de plus à chaque pas.
        var (x, y) = (h.X, h.Y);
        Reserve? proche = null;
        foreach (var r in Installations.Reserves)
        {
            if (r.AuteurId != h.Id || r.Sorte != sorte || Distance(r.X, r.Y, h.X, h.Y) > 6) continue;
            if (proche is null || Distance(r.X, r.Y, h.X, h.Y) < Distance(proche.X, proche.Y, h.X, h.Y)) proche = r;
        }
        if (proche is not null) (x, y) = (proche.X, proche.Y);
        else if (JournalQuotidien)
            id = Noter(_maintenant, TypeEvenement.Pensee, h, null, new Recit("res.metDeCote", ("h", h), ("r", DecrireReserve(sorte, kg))), id);
        h.Memoire.Retenir(sorte == SorteReserve.Eau ? TypeLieu.Eau : TypeLieu.Nourriture, x, y, _maintenant, Aptitude(h));
        var (le, ih, communaute, evenement) = (_maintenant, h.Id, h.CommunauteId, proche?.EvenementId ?? id);
        Tour.Differer(() => Installations.Reserver(x, y, sorte, kg, qualite, le, ih, communaute, evenement));
    }

    /// <summary>« 12 kg de viande séchée », « 4 litres d'eau ».</summary>
    public static Recit DecrireReserve(SorteReserve sorte, double kg) =>
        new(sorte is SorteReserve.Eau or SorteReserve.ViandeSechee or SorteReserve.FruitsSeches or SorteReserve.NoixGraines or SorteReserve.Grains
                or SorteReserve.Tubercules ? $"res.sorte.{sorte}" : "res.sorte.ViandeCachee",
            ("n", (long)Math.Max(1, Math.Round(kg))));

    /// <summary>
    /// Loin de toute eau, une calebasse ou une outre pleine à la main, sans soif : qui sait cacher l'eau l'enterre, bouchée à l'argile
    /// ou à la résine s'il en a, pour la saison sèche. Vrai s'il l'a fait.
    /// </summary>
    private bool CacherDeLEau(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Soif >= 30 || !h.Savoirs.Exists(s => s.Effet == Effet.CacheDEau && s.Confiance >= 0.4)) return false;
        var i = h.Objets.FindIndex(o => o.Plein && o.Matiere is Matiere.Calebasse or Matiere.Peau);
        if (i < 0 || alea.Reel() >= 0.02 * (0.4 + h.Personnalite.Conscience)) return false;
        if (DistanceEau(h.X, h.Y) < 4) return false;
        foreach (var r in Installations.Reserves)
            if (r.Sorte == SorteReserve.Eau && r.AuteurId == h.Id && Distance(r.X, r.Y, h.X, h.Y) <= 3) return false;
        var recipient = h.Objets[i];
        h.Objets.RemoveAt(i);
        var bouchon = APortee(h, Matiere.Argile) || APortee(h, Matiere.Resine) ? 0.6 : 0.3;
        var qualite = Math.Clamp(bouchon + 0.3 * h.Personnalite.Adresse, 0, 1);
        var litres = (recipient.Matiere == Matiere.Peau ? 6 : 4) * Math.Clamp(recipient.Qualite, 0.3, 1);
        Poser(h, SorteReserve.Eau, litres, qualite, Geste.Cacher, recipient.Matiere, Effet.CacheDEau, tous);
        return true;
    }

    /// <summary>Assoiffé loin de toute eau : celui qui sait porter l'eau se dit qu'une calebasse enterrée ici l'aurait sauvé.</summary>
    private void PenserALaCacheDEau(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Soif < 70 || h.EstEnfant(_maintenant) || h.Sait(Effet.CacheDEau) || !h.Sait(Effet.EauPortee)) return;
        if (alea.Reel() >= 0.0002 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h)) || DistanceEau(h.X, h.Y) < 4) return;
        Retenir(h, Geste.Cacher, Matiere.Calebasse, Matiere.Aucune, Effet.CacheDEau, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
            recit: new Recit("res.cacheDEau", ("h", h)));
    }

    /// <summary>L'hiver affame : celui qui se souvient des noix de l'automne se promet d'en garder.</summary>
    private void PenserAuxProvisions(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim < 45 || h.EstEnfant(_maintenant) || h.Sait(Effet.Provisions) || Calendrier.Decomposer(_maintenant).Saison != Saison.Hiver) return;
        if (alea.Reel() >= 0.00002 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h)) * (0.5 + h.Personnalite.Conscience)) return;
        Retenir(h, Geste.Amasser, Matiere.Plante, Matiere.Aucune, Effet.Provisions, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
            recit: new Recit("res.provisions", ("h", h)));
    }

    /// <summary>
    /// Ce qu'il lui faut encore mettre de côté, de 0 (assez) à 1 (rien) : de quoi nourrir les siens trois mois d'hiver, lui, son
    /// compagnon et les petits qu'il nourrit.
    /// </summary>
    private double BesoinDeReserve(Humain h, IReadOnlyList<Humain> tous)
    {
        var bouches = 1.0 + (Vivant(tous, h.PartenaireId) is not null ? 1 : 0);
        foreach (var e in tous.EnfantsDe(h.Id)) if (e.EstVivant && e.EstEnfant(_maintenant)) bouches += 0.6;
        var stock = 0.0;
        foreach (var r in Installations.Reserves) if (r.AuteurId == h.Id && EstNourriture(r.Sorte)) stock += r.Vivres * r.Etat;
        return Math.Clamp(1 - stock / (Calendrier.JoursParSaison * FaimParJour * bouches), 0, 1);
    }

    /// <summary>
    /// Rassasié, là où la cueillette abonde : en automne, qui sait amasser garde des noix et des graines ; en été, qui sait sécher
    /// les fruits les met au soleil, si l'air est sec. Vrai s'il s'y est mis.
    /// </summary>
    private bool Amasser(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim >= 35) return false;
        var saison = Calendrier.Decomposer(_maintenant).Saison;
        if (saison is not (Saison.Ete or Saison.Automne)) return false;
        var i = carte.Indice(h.X, h.Y);
        if (ecosysteme.Plantes[i] < 3000) return false;
        var noix = saison == Saison.Automne && h.Savoirs.Exists(s => s.Effet == Effet.Provisions && s.Confiance >= 0.4);
        var fruits = !noix && Calendrier.Decomposer(_maintenant).EstJour && _temperature >= 15 && !Humide(carte.BiomeEn(h.X, h.Y)) && Sec(h) >= 0.5
                     && h.Savoirs.Exists(s => s.Effet == Effet.Sechage && s.Confiance >= 0.4);
        if (!noix && !fruits || alea.Reel() >= 0.05 * (0.4 + h.Personnalite.Conscience)) return false;
        var besoin = BesoinDeReserve(h, tous);
        if (alea.Reel() >= besoin) return false;
        var pris = Ecosysteme.Prelever(ecosysteme.Plantes, i, Math.Clamp(ecosysteme.Plantes[i] / 1000, 1, 8));
        if (pris <= 0) return false;
        // Une part de cueillette apaise 2,5 de faim : on garde ce qu'on aurait mangé.
        var sorte = noix ? SorteReserve.NoixGraines : SorteReserve.FruitsSeches;
        var kg = pris * 2.5 / Reserve.FaimParKg(sorte);
        var qualite = Math.Clamp(0.4 + 0.5 * h.Personnalite.Conscience * (0.5 + 0.5 * alea.Reel()), 0, 1);
        Poser(h, sorte, kg, qualite, noix ? Geste.Amasser : Geste.Secher, Matiere.Plante, noix ? Effet.Provisions : Effet.Sechage, tous);
        return true;
    }

    /// <summary>
    /// Peut-il y puiser sans voler ? Dans la sienne, dans celle d'un proche ou d'un mort ; dans celle d'un membre de son groupe, si
    /// le groupe tient la réserve pour commune, ou s'il n'en a pas d'idée et que l'auteur ne lui en veut pas. Ailleurs, c'est voler.
    /// </summary>
    internal bool Permis(Humain h, Reserve r, IReadOnlyList<Humain> tous)
    {
        if (r.AuteurId == h.Id || r.AuteurId <= 0 || r.AuteurId > tous.Count) return true;
        var auteur = tous[r.AuteurId - 1];
        if (!auteur.EstVivant || auteur.Id == h.PartenaireId || SontParents(auteur, h) || auteur.AffectionPour(h.Id) >= 30) return true;
        if (h.CommunauteId == 0 || h.CommunauteId != r.CommunauteId) return false;
        var idee = Social?.CommunauteDe(h)?.IdeeReserve ?? IdeeReserve.Aucune;
        return idee == IdeeReserve.Commune || idee == IdeeReserve.Aucune && auteur.AffectionPour(h.Id) >= -10;
    }

    /// <summary>Tenté de voler : la faim, une honnêteté basse, un cœur froid ; l'équité retient (comme pour les autres vols).</summary>
    private double TentationDeVoler(Humain h)
    {
        if (h.Faim < 55 && h.Soif < 60) return 0;
        var p = h.Personnalite;
        var tentation = Math.Clamp((0.45 - p.Moral.Honnetete) / 0.25, 0, 1) + Psyche.Froideur(p);
        return Math.Max(0, tentation * (1.3 - 0.6 * p.Moral.Fondements.Equite) * Regards.Retenue(h));
    }

    /// <summary>
    /// Affamé, sur une réserve : il y mange s'il le peut sans voler, ou, très tenté, en cachette. Vrai s'il a mangé (ou pris).
    /// </summary>
    private bool MangerDansLaReserve(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim < 30 || !Installations.ReserveEn(h.X, h.Y)) return false;
        Reserve? choisie = null;
        var vol = false;
        foreach (var r in Installations.ReservesEn(h.X, h.Y))
        {
            if (!EstNourriture(r.Sorte) || r.Kg <= 0) continue;
            var permis = Permis(h, r, tous);
            if (!permis && (TentationDeVoler(h) <= 0 || alea.Reel() >= 0.05 * TentationDeVoler(h))) continue;
            // La sienne d'abord, puis la plus gâtée (elle ne durera pas).
            if (choisie is null || r.AuteurId == h.Id && choisie.AuteurId != h.Id || permis && vol || r.Etat < choisie.Etat && (permis || vol))
                (choisie, vol) = (r, !permis);
        }
        if (choisie is null) return false;
        h.Activite = Activite.Cueillir;
        h.CibleX = h.CibleY = -1;
        if (choisie.AuteurId == h.Id)
        {
            var (geste, effet) = choisie.Sorte switch
            {
                SorteReserve.NoixGraines or SorteReserve.Grains or SorteReserve.Tubercules => (Geste.Amasser, Effet.Provisions),
                SorteReserve.ViandeCachee => (Geste.Cacher, Effet.CacheFroide),
                _ => (Geste.Secher, Effet.Sechage),
            };
            // Manger en hiver ce qu'on a gardé : le savoir se confirme.
            Retenir(h, geste, choisie.Sorte is SorteReserve.NoixGraines or SorteReserve.FruitsSeches or SorteReserve.Grains or SorteReserve.Tubercules ? Matiere.Plante : Matiere.Carcasse, Matiere.Aucune, effet,
                0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        }
        DiffererPuiser(h.Id, choisie.Id, h.Faim / Reserve.FaimParKg(choisie.Sorte), false, vol, tous, _maintenant);
        return true;
    }

    /// <summary>
    /// Rien à manger ou à boire en vue : il va vers la réserve la plus proche qu'il connaît et où il peut puiser sans voler, la
    /// sienne, celle d'un proche, ou celle du groupe si elle est à tous. Vrai s'il se met en route.
    /// </summary>
    private bool AllerALaReserve(Humain h, bool eau, IReadOnlyList<Humain> tous)
    {
        if (Installations.Reserves.Count == 0) return false;
        var portee = 2 * RayonPerception(h);
        Reserve? choisie = null;
        var meilleure = int.MaxValue;
        foreach (var r in Installations.Reserves)
        {
            if ((r.Sorte == SorteReserve.Eau) != eau || r.Kg <= 0) continue;
            var d = Distance(h.X, h.Y, r.X, r.Y);
            if (d > portee || d >= meilleure || d == 0) continue;
            if (r.AuteurId != h.Id && (r.CommunauteId == 0 || r.CommunauteId != h.CommunauteId) && r.AuteurId != h.PartenaireId) continue;
            if (!Permis(h, r, tous)) continue;
            (choisie, meilleure) = (r, d);
        }
        if (choisie is null) return false;
        _motif = Motif.Souvenir;
        Avancer(h, choisie.X, choisie.Y, eau ? Activite.ChercherEau : Activite.ChercherNourriture);
        return h.CibleX >= 0;
    }

    /// <summary>Assoiffé, sur une cache d'eau : il y boit s'il le peut sans voler, ou, très tenté, en cachette. Vrai s'il a bu.</summary>
    private bool BoireALaCache(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Soif < 25 || !Installations.ReserveEn(h.X, h.Y)) return false;
        foreach (var r in Installations.ReservesEn(h.X, h.Y))
        {
            if (r.Sorte != SorteReserve.Eau || r.Kg <= 0) continue;
            var permis = Permis(h, r, tous);
            // Une cache d'un autre, on ne la connaît pas : il faut tomber dessus.
            if (r.AuteurId != h.Id && !h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.Eau && l.X == r.X && l.Y == r.Y)
                && alea.Reel() >= 0.05 * (0.5 + h.Personnalite.Ouverture)) continue;
            if (!permis && (TentationDeVoler(h) <= 0 || alea.Reel() >= 0.2 * TentationDeVoler(h))) continue;
            h.Activite = Activite.Boire;
            h.CibleX = h.CibleY = -1;
            h.Memoire.Retenir(TypeLieu.Eau, h.X, h.Y, _maintenant, Aptitude(h));
            DiffererPuiser(h.Id, r.Id, h.Soif / SoifParLitre, true, !permis, tous, _maintenant);
            return true;
        }
        return false;
    }

    // À part : la fermeture n'est créée que si l'on puise vraiment.
    private void DiffererPuiser(int ih, int reserve, double voulu, bool eau, bool vol, IReadOnlyList<Humain> tous, long maintenant) =>
        // Ceux qui puisent dans le même pas se partagent ce qui reste ; ce qu'il prend apaise sa faim ou sa soif après le pas.
        Tour.Differer(tous, t => Puiser(t[ih - 1], reserve, voulu, eau, vol, t, maintenant));

    private void Puiser(Humain h, int reserveId, double voulu, bool eau, bool vol, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstVivant || Installations.ReserveParId(reserveId) is not { } r) return;
        var pris = Installations.Puiser(reserveId, voulu);
        if (pris <= 0) return;
        // Une réserve un peu gâtée nourrit moins (ce qui a moisi se jette).
        if (eau) h.Soif = Math.Max(0, h.Soif - pris * SoifParLitre);
        else h.Faim = Math.Max(0, h.Faim - pris * Reserve.FaimParKg(r.Sorte) * (0.5 + 0.5 * r.Etat));
        // Ce qui vient des champs prend une part de plus en plus grande dans ce qu'il mange.
        if (r.Sorte is SorteReserve.Grains or SorteReserve.Tubercules)
            h.PartDesChamps = Math.Min(1, h.PartDesChamps + 0.2 * pris * Reserve.FaimParKg(r.Sorte) / FaimParJour);
        if (r.AuteurId == h.Id || r.AuteurId <= 0 || r.AuteurId > tous.Count) return;
        var auteur = tous[r.AuteurId - 1];
        var communaute = r.CommunauteId > 0 && Social is { } s && r.CommunauteId <= s.Communautes.Count ? s.Communautes[r.CommunauteId - 1] : null;
        if (!vol)
        {
            if (auteur.EstVivant) Social?.Partager(auteur, h);
            if (communaute is not null && h.CommunauteId == communaute.Id) communaute.PartagesReserve++;
            return;
        }
        VolerDansLaReserve(h, auteur, r, communaute, tous, maintenant);
    }

    /// <summary>
    /// Il a pris en cachette dans la réserve d'un autre. L'auteur, s'il est là, ou un membre de son groupe peut le voir ; sinon, le
    /// vol passe inaperçu. Les suites sont celles des autres vols : rancune, estime perdue, offense entre groupes.
    /// </summary>
    private void VolerDansLaReserve(Humain h, Humain auteur, Reserve r, Communaute? communaute, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (communaute is not null) communaute.VolsReserve++;
        var quoi = Recits.Recit.Seul(r.Sorte == SorteReserve.Eau ? "res.quoi.eau" : "res.quoi.reserve");
        var id = Surprendre(h, auteur, r.CommunauteId, quoi, r.EvenementId, tous, maintenant);
        if (communaute is not null) communaute.VolReserveEvenementId = id;
    }

    /// <summary>
    /// Ce qu'il a pris en cachette (<paramref name="quoi"/>) appartenait à <paramref name="auteur"/> : l'auteur, s'il est là, ou un
    /// membre de son groupe peut le voir ; sinon, le vol passe inaperçu. Les suites sont celles des autres vols : rancune, estime
    /// perdue, offense entre groupes. Renvoie la ligne du journal.
    /// </summary>
    private long Surprendre(Humain h, Humain auteur, int communauteId, Recit quoi, long cause, IReadOnlyList<Humain> tous, long maintenant)
    {
        var cache = Dissimulation(h);
        Humain? temoin = null;
        foreach (var t in tous.Vivants())
        {
            if (!t.EstVivant || t.Id == h.Id || t.EstEnfant(maintenant) || t.Activite == Activite.Dormir || Distance(t.X, t.Y, h.X, h.Y) > 1) continue;
            if (t.Id != auteur.Id && (t.CommunauteId == 0 || t.CommunauteId != communauteId)) continue;
            if (alea.Reel() < 0.35 * Perspicacite(t) * cache) { temoin = t; break; }
        }
        if (temoin is null)
            return Noter(maintenant, TypeEvenement.Vol, h, auteur, new Recit("res.vol.cache", ("h", h), ("quoi", quoi), ("a", auteur)), cause, karma: Regard.Vol);
        var texte = temoin.Id == auteur.Id
            ? new Recit("res.vol.surpris", ("a", auteur), ("h", h), ("quoi", quoi))
            : new Recit("res.vol.temoin", ("t", temoin), ("h", h), ("quoi", quoi), ("a", auteur));
        var vol = Noter(maintenant, TypeEvenement.Vol, h, auteur, texte, cause, karma: Regard.Vol);
        if (Social is not { } social) return vol;
        var am = auteur.Personnalite;
        var colere = Math.Min(100, 30 + 40 * (1 - am.Agreabilite) + 30 * (am.Moral.Fondements.Equite - 0.5));
        if (auteur.EstVivant)
        {
            VieSociale.AjouterGrief(auteur, new Grief(h.Id, MotifGrief.Vol, colere, colere, vol));
            social.ChangerAffection(auteur, h, -colere / 4, maintenant);
            social.Offenser(h, auteur, 3, vol, tous, maintenant);
        }
        if (temoin.Id != auteur.Id)
        {
            VieSociale.AjouterGrief(temoin, new Grief(h.Id, MotifGrief.Vol, colere / 2, colere / 2, vol));
            social.ChangerAffection(temoin, h, -colere / 8, maintenant);
        }
        return vol;
    }

    /// <summary>
    /// Une fois par jour, les réserves vieillissent : l'eau suinte d'un bouchon mal fait, ce qui est séché moisit vite là où l'air est
    /// humide, les rongeurs prennent leur part des graines, et une cache de viande ne tient que dans le froid ; les bêtes déterrent
    /// celle qu'on a mal couverte. Les durées sont des ordres de grandeur, différents pour chaque réserve selon son soin et son lieu.
    /// </summary>
    internal void GaterLesReserves(IReadOnlyList<Humain> humains, long maintenant)
    {
        foreach (var r in Installations.Reserves.ToList())
        {
            var i = carte.Indice(r.X, r.Y);
            var biome = carte.BiomeEn(r.X, r.Y);
            var temperature = Temperature(r.X, r.Y, maintenant);
            var pluie = Math.Clamp(ecosysteme.PluieEn(i), 0.3, 2);
            var (kg, etat) = (r.Kg, r.Etat);
            var pillee = false;
            switch (r.Sorte)
            {
                case SorteReserve.Eau:
                    kg *= 1 - (0.002 + 0.01 * (1 - r.Qualite));
                    break;
                case SorteReserve.ViandeSechee:
                case SorteReserve.FruitsSeches:
                {
                    var duree = Humide(biome) ? 15 + 10 * r.Qualite : (r.Sorte == SorteReserve.ViandeSechee ? 120 : 90) + 150 * r.Qualite;
                    etat -= pluie / duree;
                    pillee = alea.Reel() < 0.004 * (1.2 - r.Qualite);
                    break;
                }
                case SorteReserve.NoixGraines:
                    etat -= 1 / (200 + 100 * r.Qualite);
                    kg *= 1 - (0.002 + 0.004 * (1 - r.Qualite));
                    break;
                case SorteReserve.Grains:
                    // Des graines bien sèches se gardent une année et plus ; les rongeurs et l'humidité en prennent leur part.
                    etat -= (Humide(biome) ? 2 : 1) / (300 + 300 * r.Qualite);
                    kg *= 1 - (0.001 + 0.004 * (1 - r.Qualite));
                    break;
                case SorteReserve.Tubercules:
                    // Racines et tubercules tiennent deux à quatre mois au frais, moins par la chaleur.
                    etat -= (temperature > 20 ? 1.5 : 1) / (60 + 60 * r.Qualite);
                    pillee = alea.Reel() < 0.003 * (1.2 - r.Qualite);
                    break;
                case SorteReserve.ViandeCachee:
                    // Gelée, elle ne se gâte presque pas ; au-dessus de zéro, elle pourrit d'autant plus vite qu'il fait doux.
                    etat -= temperature < 0 ? 1 / 365.0 : Math.Min(1, Math.Pow(2, (temperature - 20) / 10)) / (3 * (1 + r.Qualite));
                    pillee = alea.Reel() < 0.01 + 0.03 * (1 - r.Qualite);
                    break;
            }
            if (pillee)
            {
                kg *= 0.2 + 0.3 * alea.Reel();
                if (r.AuteurId > 0 && r.AuteurId <= humains.Count && humains[r.AuteurId - 1] is { EstVivant: true } a)
                    Journal.Add(new EvenementJournal(maintenant, TypeEvenement.Pensee, a.Id, 0, new Recit("res.betes", ("a", a)))
                    {
                        Id = Numeros.Suivant(), CauseId = r.EvenementId,
                    });
            }
            if (etat <= 0 && r.AuteurId > 0 && r.AuteurId <= humains.Count && humains[r.AuteurId - 1] is { EstVivant: true } auteur && r.Kg >= 1)
            {
                Journal.Add(new EvenementJournal(maintenant, TypeEvenement.Pensee, auteur.Id, 0,
                    new Recit(r.Sorte == SorteReserve.ViandeCachee ? "res.pourri" : "res.moisi", ("r", DecrireReserve(r.Sorte, r.Kg)), ("a", auteur)))
                {
                    Id = Numeros.Suivant(), CauseId = r.EvenementId,
                });
                // Ce qui a moisi fait douter : l'air de ces terres était peut-être trop humide.
                if (r.Sorte is SorteReserve.ViandeSechee or SorteReserve.FruitsSeches)
                {
                    var s = auteur.Savoirs.FindIndex(x => x.Effet == Effet.Sechage);
                    if (s >= 0) auteur.Savoirs[s] = auteur.Savoirs[s] with { Essais = auteur.Savoirs[s].Essais + 1 };
                }
            }
            Installations.Changer(r.Id, r with { Kg = kg, Etat = etat });
        }
    }


    /// <summary>
    /// Une fois par jour, ce que chaque communauté pense des réserves. Après des partages, dans un groupe généreux et plus encore
    /// s'il a surmonté une disette, l'idée peut naître que la réserve est à tous ; après des vols, dans un groupe peu confiant, qu'elle
    /// est à celui qui l'a faite. Ce qui a été vécu s'estompe avec les mois.
    /// </summary>
    internal void IdeesDesReserves(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (Social is not { } social) return;
        HashSet<int>? avecReserves = null;
        foreach (var c in social.Communautes)
        {
            if (!c.Active) continue;
            c.PartagesReserve *= 0.99;
            c.VolsReserve *= 0.99;
            if (c.PartagesReserve < 3 && c.VolsReserve < 1) continue;
            avecReserves ??= Installations.Reserves.Select(r => r.CommunauteId).ToHashSet();
            if (!avecReserves.Contains(c.Id)) continue;
            double generosite = 0, confiance = 0;
            var n = 0;
            Humain? figure = null;
            foreach (var h in humains.Vivants())
            {
                if (!h.EstVivant || h.CommunauteId != c.Id || h.EstEnfant(maintenant)) continue;
                var p = h.Personnalite;
                generosite += (0.5 * p.Agreabilite + 0.25 * p.Moral.Compassion + 0.25 * p.Moral.Honnetete) * (1 - 0.8 * Psyche.Froideur(p));
                confiance += 0.5 * (p.Agreabilite + p.Moral.Honnetete);
                n++;
                if (h.Id == c.FigureId) figure = h;
            }
            if (n == 0) continue;
            (generosite, confiance) = (generosite / n, confiance / n);
            var aSoi = c.IdeeReserve != IdeeReserve.ASoi && c.VolsReserve >= 1
                       && alea.Reel() < 0.02 * Math.Min(1, c.VolsReserve / 3) * Math.Max(0, 1 - confiance);
            var commune = !aSoi && c.IdeeReserve != IdeeReserve.Commune && c.PartagesReserve >= 3 && generosite > 0.3
                          && alea.Reel() < 0.01 * (generosite - 0.3) * Math.Min(1, c.PartagesReserve / 10) * (c.DisettesSurmontees > 0 ? 2 : 1);
            if (!aSoi && !commune) continue;
            var avant = c.IdeeReserve;
            c.IdeeReserve = aSoi ? IdeeReserve.ASoi : IdeeReserve.Commune;
            c.IdeeReserveLe = maintenant;
            var texte = new Recit(aSoi ? "res.idee.aSoi" : "res.idee.commune", ("c", c),
                ("fin", avant == IdeeReserve.Aucune ? "" : Recits.Recit.Seul(aSoi ? "res.idee.plusPartage" : "res.idee.plusGarde")));
            var e = new EvenementJournal(maintenant, TypeEvenement.Communaute, figure?.Id ?? 0, 0, texte)
            {
                Id = Numeros.Suivant(), CauseId = aSoi ? c.VolReserveEvenementId : c.DisetteEvenementId, CommunauteId = c.Id, Significatif = true,
            };
            Journal.Add(e);
            c.IdeeReserveEvenementId = e.Id;
        }
    }


    /// <summary>Jours que les réserves d'une communauté peuvent nourrir tous ses membres.</summary>
    public static double JoursDeVivres(IEnumerable<Reserve> reserves, int communaute, int membres)
    {
        if (membres <= 0) return 0;
        var vivres = 0.0;
        foreach (var r in reserves) if (r.CommunauteId == communaute && EstNourriture(r.Sorte)) vivres += r.Vivres * r.Etat;
        return vivres / (FaimParJour * membres);
    }
}
