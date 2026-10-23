using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.29.2 : visites à un proche d'une autre communauté, rassemblements de groupes amis, savoirs venus d'ailleurs, ouvrages demandés et spécialistes.</summary>
public sealed class RencontresTests : IDisposable
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Semaine = 7 * Jour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;
    private static readonly (int X, int Y) Terre = (Carte.Emplacements[0].X, Carte.Emplacements[0].Y);

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-rencontres-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static Humain Humain(int id, int x, int y, int mere = 0, double age = 25, double adresse = 0.5, double extraversion = 0.8) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = id % 2 == 0 ? Sexe.Homme : Sexe.Femme,
        PartenaireId = 0,
        Emplacement = 1,
        MereId = mere,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.8, 0.6, extraversion, 0.7, 0.3, 0.5, 0.5, 0.5, new Intelligence(100, 100, 100, 55 + 90 * adresse, 100, 100)),
        Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Homme : Sexe.Femme),
        X = x,
        Y = y,
        Humeur = 80,
        Reserves = 80,
    };

    private static void Lier(Humain a, Humain b, double affection, bool amis, long derniere = Maintenant)
    {
        a.Liens.Add(new Lien(b.Id, 60, affection, derniere, Amis: amis));
        b.Liens.Add(new Lien(a.Id, 60, affection, derniere, Amis: amis));
    }

    /// <summary>Un groupe d'amis rassemblés autour d'un point.</summary>
    private static List<Humain> Groupe(int premier, int nombre, int x, int y)
    {
        var groupe = Enumerable.Range(premier, nombre).Select(i => Humain(i, x + i % 3, y + i % 2)).ToList();
        foreach (var a in groupe)
        {
            foreach (var b in groupe.Where(b => b.Id > a.Id)) Lier(a, b, 60, amis: true);
        }
        return groupe;
    }

    private static void Semaines(VieSociale social, List<Humain> tous, List<Communaute> communautes, long debut, int nombre, Func<bool>? arret = null)
    {
        var nature = new Ecosysteme(Carte);
        for (var s = 0; s < nombre && arret?.Invoke() != true; s++)
        {
            var t = debut + s * Semaine;
            social.Parcourir(tous, Carte, t);
            social.Regrouper(tous, communautes, t, new HashSet<string>());
            social.Territoires(tous, communautes, nature, Carte, t);
        }
    }

    // ───────────── Visites ─────────────

    [Fact]
    public void Visite_UneFilleVaVoirSaMereChezUnAutreGroupe_ResteDesSiens_PuisRentre()
    {
        var social = new VieSociale(new Alea(4));
        var fille = Humain(1, Terre.X, Terre.Y, mere: 7);
        var a = Groupe(2, 5, Terre.X, Terre.Y);
        foreach (var o in a) Lier(fille, o, 60, amis: true);
        a.Insert(0, fille);
        var b = Groupe(7, 6, Terre.X + 20, Terre.Y);
        var mere = b[0];
        // Elles ne se sont pas vues depuis trois mois.
        Lier(fille, mere, 80, amis: true, derniere: Maintenant - 90 * Jour);
        var tous = a.Concat(b).ToList();
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        Assert.Equal(2, communautes.Count);
        Assert.NotEqual(fille.CommunauteId, mere.CommunauteId);

        // L'une ou l'autre part voir l'autre.
        var jour = 0;
        for (; jour < 2000 && fille.VisiteChezId == 0 && mere.VisiteChezId == 0; jour++) social.Visites(tous, Maintenant + jour * Jour);
        var (visiteur, hote) = fille.VisiteChezId != 0 ? (fille, mere) : (mere, fille);
        Assert.Equal(hote.Id, visiteur.VisiteChezId);
        var depart = Assert.Single(social.Journal, e => e.Texte.Contains(visiteur == fille ? "H1 part voir sa mère H7" : "H7 part voir sa fille H1", StringComparison.Ordinal));
        Assert.Equal((visiteur.CommunauteId, hote.CommunauteId), (depart.CommunauteId, depart.AutreCommunauteId));
        Assert.Equal(depart.Id, visiteur.VisiteEvenementId);
        Assert.InRange(visiteur.VisiteJusquA, Jour, 20 * Jour);

        // Arrivée : chaque semaine, les groupes se refont, mais le visiteur reste des siens et n'unit pas les deux groupes.
        var sienne = visiteur.CommunauteId;
        visiteur.X = hote.X;
        visiteur.Y = hote.Y;
        visiteur.VisiteArriveeLe = Maintenant + jour * Jour;
        Semaines(social, tous, communautes, Maintenant + jour * Jour, 2);
        Assert.Equal(2, communautes.Count(c => c.Active));
        Assert.Equal(sienne, visiteur.CommunauteId);

        VieSociale.FinirVisite(visiteur, Maintenant + (jour + 14) * Jour);
        Assert.Equal((0, true), (visiteur.VisiteChezId, visiteur.Rentrer));
        Assert.True(visiteur.VisiteLe > 0);
    }

    // ───────────── Rassemblements ─────────────

    [Fact]
    public void Rassemblement_DeuxGroupesAmis_CampentEnsemble_EchangentDesCadeaux_EtEnFontLeBilan()
    {
        var social = new VieSociale(new Alea(7));
        var a = Groupe(1, 8, Terre.X, Terre.Y);
        var b = Groupe(9, 8, Terre.X + 25, Terre.Y);
        // Assez d'amitiés d'un groupe à l'autre pour une alliance.
        for (var i = 0; i < 4; i++) Lier(a[i], b[i], 50, amis: true);
        foreach (var h in a) h.Objets.Add(new Objet(Matiere.Corde, 0.7, 1));
        foreach (var h in b) h.Objets.Add(new Objet(Matiere.Calebasse, 0.8, 1));
        var tous = a.Concat(b).ToList();
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        Semaines(social, tous, communautes, Maintenant, 1500, () => social.Rassemblements.Count > 0);
        var r = Assert.Single(social.Rassemblements);
        Assert.Equal(EtatRelation.Alliees, social.RelationEntre(r.HoteId, r.InviteeId));
        Assert.Contains(social.Journal, e => e.Id == r.EvenementId && e.Texte.Contains("camper avec", StringComparison.Ordinal) && e.Significatif);
        Assert.InRange(r.Participants, 2, 16);
        Assert.InRange(r.Fin - r.Debut, 3 * Jour, 21 * Jour);

        // Tous ceux qui y vont se retrouvent au camp commun ; on s'y offre des cadeaux.
        var presents = tous.Where(h => h.RassemblementId == r.Id).ToList();
        foreach (var h in presents) (h.X, h.Y) = (r.X, r.Y);
        for (var j = 0; j < 30 && r.Cadeaux == 0; j++) social.JourDeRassemblement(tous, r.Debut + j * Jour);
        Assert.True(r.Cadeaux > 0);
        Assert.Contains(social.Journal, e => e.Texte.StartsWith("Au rassemblement, ", StringComparison.Ordinal) && e.Karma > 0 && e.CauseId == r.EvenementId);

        // Les semaines passent : le camp commun ne fait pas un seul groupe, et le rassemblement finit par un bilan.
        var debut = social.Journal.Find(e => e.Id == r.EvenementId)!.Secondes;
        Semaines(social, tous, communautes, debut + Semaine, 5, () => !r.Actif);
        Assert.False(r.Actif);
        Assert.Equal(2, communautes.Count(c => c.Active));
        var bilan = Assert.Single(social.Journal, e => e.Id == r.BilanEvenementId);
        Assert.Contains("Bilan : ", bilan.Texte);
        Assert.Equal(r.EvenementId, bilan.CauseId);
        Assert.All(tous, h => Assert.Equal(0, h.RassemblementId));
        Assert.Contains(presents, h => h.CommunauteId == r.InviteeId && h.Rentrer);
    }

    // ───────────── Savoirs venus d'ailleurs ─────────────

    private static Savoir Eclat(int succes) =>
        new(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, succes + 1, succes, 0, Maintenant, OrigineSavoir.Essai, 0, 0);

    private static (Comportement, VieSociale, List<Communaute>) DeuxCommunautes(List<Humain> tous, int[] premiere, int[] seconde)
    {
        var social = new VieSociale(new Alea(3));
        var communautes = new List<Communaute>
        {
            new() { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = premiere[0] },
            new() { Id = 2, Nom = "Communauté de H2", FondeeLe = 0, FigureId = seconde.Length > 0 ? seconde[0] : premiere[0] },
        };
        social.Communautes = communautes;
        foreach (var id in premiere) tous[id - 1].CommunauteId = 1;
        foreach (var id in seconde) tous[id - 1].CommunauteId = 2;
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(3)) { Social = social };
        // L'éclat est connu au monde depuis longtemps : ce n'est pas une première.
        c.Installations.Decouvertes.Add(new Decouverte(Effet.Eclat, 99, 0, 0, OrigineSavoir.Essai, "", 0));
        return (c, social, communautes);
    }

    [Fact]
    public void SavoirVenuDAilleurs_AppriDUnAutreGroupe_LeJournalDitDOuIlVient()
    {
        var e = Carte.Emplacements[0];
        var maitre = Humain(2, e.X, e.Y);
        var eleve = Humain(1, e.X, e.Y);
        var voisin = Humain(3, e.X + 2, e.Y);
        maitre.Savoirs.Add(Eclat(20));
        maitre.Objets.Add(new Objet(Matiere.Silex, 1, 1));
        maitre.Objets.Add(new Objet(Matiere.Galet, 1, 1));
        Lier(maitre, eleve, 80, amis: true);
        var tous = new List<Humain> { eleve, maitre, voisin };
        var (c, _, _) = DeuxCommunautes(tous, [1, 3], [2]);
        eleve.VisiteChezId = maitre.Id;
        eleve.VisiteArriveeLe = Maintenant;
        for (var pas = 0; pas < 200_000 && !eleve.Savoirs.Exists(s => s.Effet == Effet.Eclat); pas++)
            c.Enseigner(maitre, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        Assert.Contains(eleve.Savoirs, s => s.Effet == Effet.Eclat);
        var ev = Assert.Single(c.Journal, j => j.Texte.StartsWith("La communauté de H1 l'apprend de la communauté de H2 : H1 l'a vu faire à H2, chez qui elle était en visite.", StringComparison.Ordinal));
        Assert.True(ev.Significatif);
        Assert.Equal(2, ev.AutreCommunauteId);

    }

    [Fact]
    public void SavoirVenuDAilleurs_QuelquUnVenuDUnAutreGroupe_ApporteSonSavoir()
    {
        var e = Carte.Emplacements[0];
        var venue = Humain(1, e.X, e.Y);
        var enfant = Humain(3, e.X, e.Y, mere: 1, age: 10);
        venue.Savoirs.Add(Eclat(20));
        venue.Objets.Add(new Objet(Matiere.Silex, 1, 1));
        venue.Objets.Add(new Objet(Matiere.Galet, 1, 1));
        Lier(venue, enfant, 90, amis: true);
        var tous = new List<Humain> { venue, Humain(2, e.X + 30, e.Y), enfant };
        var (c, _, _) = DeuxCommunautes(tous, [1, 3], [2]);
        venue.VenuDeId = 2;
        for (var pas = 0; pas < 200_000 && !enfant.Savoirs.Exists(s => s.Effet == Effet.Eclat); pas++)
            c.Enseigner(venue, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        var ev = Assert.Single(c.Journal, j => j.Texte.StartsWith("H1, venue de la communauté de H2, apporte ce savoir à la communauté de H1.", StringComparison.Ordinal));
        Assert.True(ev.Significatif);
    }

    [Fact]
    public void Regrouper_CeluiQuiRejointUnAutreGroupe_EstDitVenuDuSien()
    {
        var social = new VieSociale(new Alea(5));
        var a = Groupe(1, 6, Terre.X, Terre.Y);
        var b = Groupe(7, 6, Terre.X + 20, Terre.Y);
        var tous = a.Concat(b).ToList();
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        var avant = a[0].CommunauteId;
        // Elle part vivre avec l'autre groupe : elle s'y lie, et perd de vue les siens.
        var partie = a[0];
        (partie.X, partie.Y) = (b[0].X, b[0].Y);
        foreach (var o in b) Lier(partie, o, 60, amis: true);
        partie.Liens.RemoveAll(l => a.Exists(x => x.Id == l.Id));
        foreach (var x in a) x.Liens.RemoveAll(l => l.Id == partie.Id);
        social.Regrouper(tous, communautes, Maintenant + 30 * Semaine, new HashSet<string>());
        Assert.Equal(b[0].CommunauteId, partie.CommunauteId);
        Assert.Equal(avant, partie.VenuDeId);
    }

    // ───────────── Ouvrages et spécialistes ─────────────

    [Fact]
    public void Ouvrage_OnDemandeAuPlusHabile_IlTaillePourLui_EtDevientCeluiQuiTaille()
    {
        var e = Carte.Emplacements[0];
        var artisan = Humain(1, e.X, e.Y, adresse: 0.95);
        // Racler une branche avec un éclat : le bout devient pointu.
        artisan.Savoirs.Add(new Savoir(Geste.Gratter, Matiere.Branche, Matiere.Eclat, Effet.Pointe, 0, Symptome.Aucun, 31, 30, 0, Maintenant, OrigineSavoir.Essai, 0, 0));
        artisan.Objets.Add(new Objet(Matiere.Eclat, 1, 1));
        artisan.Objets.Add(new Objet(Matiere.Branche, 1, 1));
        var demandeurs = Enumerable.Range(2, 4).Select(i => Humain(i, e.X, e.Y, adresse: 0.1)).ToList();
        foreach (var d in demandeurs)
        {
            d.Savoirs.Add(new Savoir(Geste.Gratter, Matiere.Branche, Matiere.Eclat, Effet.Pointe, 0, Symptome.Aucun, 3, 1, 0, Maintenant, OrigineSavoir.Essai, 0, 0));
            Lier(artisan, d, 60, amis: true);
        }
        var tous = new List<Humain> { artisan };
        tous.AddRange(demandeurs);
        var (c, _, communautes) = DeuxCommunautes(tous, [1, 2, 3, 4, 5], []);
        communautes[0].IdeeReserve = IdeeReserve.ASoi;
        demandeurs[0].Objets.Add(new Objet(Matiere.Carcasse, 0.3, 1));

        var t = Maintenant;
        foreach (var d in demandeurs)
        {
            for (var k = 0; k < 100_000 && d.DemandeAId == 0; k++) c.DemanderUnOuvrage(d, tous, t += MondeSimule.SecondesParPas);
            Assert.Equal((artisan.Id, Ouvrage.Taille), (d.DemandeAId, d.DemandeOuvrage));
            Assert.Equal(Activite.Demander, d.Activite);
            for (var k = 0; k < 10_000 && !d.Porte(Matiere.BatonPointu); k++)
            {
                // L'artisan n'a pas toujours de quoi : on lui rend une branche et un éclat.
                if (!artisan.Porte(Matiere.Branche)) artisan.Objets.Add(new Objet(Matiere.Branche, 1, 1));
                if (!artisan.Porte(Matiere.Eclat)) artisan.Objets.Add(new Objet(Matiere.Eclat, 1, 1));
                c.Servir(artisan, tous, t += MondeSimule.SecondesParPas);
            }
            Assert.True(d.Porte(Matiere.BatonPointu));
            Assert.Equal(0, d.DemandeAId);
        }
        Assert.Equal(4, artisan.ServisTaille);
        Assert.Equal(Ouvrage.Taille, artisan.Specialite);
        Assert.Equal("tailleuse de pierre", Comportement.Metier(artisan));
        Assert.Contains(c.Journal, j => j.Texte.StartsWith("Les siens disent de H1 qu'elle taille la pierre pour eux", StringComparison.Ordinal));
        // Là où les choses sont à quelqu'un, on paie : le premier a donné de la viande.
        Assert.Contains(c.Journal, j => j.Texte.Contains("H1 taille un bâton pointu pour lui, contre une part de viande", StringComparison.Ordinal) && j.Karma == 0);
        Assert.True(artisan.Porte(Matiere.Carcasse));
    }

    [Fact]
    public void Ouvrage_PersonneNeFaitMieux_OnNeDemandeRien()
    {
        var e = Carte.Emplacements[0];
        var a = Humain(1, e.X, e.Y, adresse: 0.5);
        var b = Humain(2, e.X, e.Y, adresse: 0.5);
        foreach (var h in new[] { a, b })
            h.Savoirs.Add(new Savoir(Geste.Gratter, Matiere.Branche, Matiere.Eclat, Effet.Pointe, 0, Symptome.Aucun, 11, 10, 0, Maintenant, OrigineSavoir.Essai, 0, 0));
        Lier(a, b, 60, amis: true);
        var tous = new List<Humain> { a, b };
        var (c, _, _) = DeuxCommunautes(tous, [1, 2], []);
        var t = Maintenant;
        for (var k = 0; k < 20_000; k++) Assert.False(c.DemanderUnOuvrage(b, tous, t += MondeSimule.SecondesParPas));
    }

    // ───────────── Sauvegarde ─────────────

    [Fact]
    public void Sauvegarde_VisitesRassemblementsEtOuvrages_SontRelus()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var etat = monde.Etat;
        var h = etat.Humains![0];
        (h.VisiteChezId, h.VisiteDepuis, h.VisiteArriveeLe, h.VisiteJusquA, h.VisiteEvenementId, h.VisiteLe) = (3, 11, 12, 13, 14, 15);
        (h.RassemblementId, h.Rentrer, h.VenuDeId) = (1, true, 2);
        (h.DemandeAId, h.DemandeOuvrage, h.DemandeLe) = (4, Ouvrage.Peau, 16);
        (h.ServisTaille, h.ServisAssemblage, h.ServisPeau, h.Specialite, h.SpecialiteDepuis) = (5, 6, 7, Ouvrage.Assemblage, 17);
        var r = new Rassemblement
        {
            Id = 1, HoteId = 1, InviteeId = 2, X = 10, Y = 20, Debut = 100, Fin = 200, FiniLe = 190, EvenementId = 5, BilanEvenementId = 6,
            Participants = 30, Cadeaux = 4, Appris = 3, Couples = 1,
        };
        etat = etat with { Rassemblements = [r] };
        var fichier = Path.Combine(_dossier, "monde.db");
        new DepotMonde(fichier).Enregistrer(etat);
        var relu = new DepotMonde(fichier).Charger()!;
        Assert.Equivalent(h, relu.Humains![0], strict: true);
        Assert.Equivalent(r, Assert.Single(relu.Rassemblements!), strict: true);
        // Le monde rechargé les reprend.
        var repris = new MondeSimule(Carte, relu);
        Assert.Equivalent(r, Assert.Single(repris.Rassemblements()), strict: true);
    }
}
