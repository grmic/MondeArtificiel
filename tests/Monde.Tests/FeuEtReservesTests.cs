using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.29.0 : garder le feu, feux de camp qui s'échappent, leçons du feu, réserves d'eau et de nourriture.</summary>
public sealed class FeuEtReservesTests : IDisposable
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-feu-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static Humain Humain(int id, double age = 25, Sexe sexe = Sexe.Femme, int communaute = 0, double agreabilite = 0.5,
        double honnetete = 0.5) => new()
    {
        Id = id, Nom = "H" + id, Sexe = sexe, PartenaireId = 0, Emplacement = 1, Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, agreabilite, 0.5, 0.5, 0.8, 0.5, Intelligence.Moyenne)
        {
            Moral = TraitsMoraux.Moyens with { Honnetete = honnetete },
        },
        Biologie = Biologie.ParDefaut(sexe), Reserves = 50, Sante = 100, CommunauteId = communaute,
        X = Carte.Emplacements[0].X, Y = Carte.Emplacements[0].Y,
    };

    private static Comportement Comportement(ulong graine = 1) =>
        new(Carte, new Ecosysteme(Carte), new Alea(graine)) { Social = new VieSociale(new Alea(graine)) };

    [Fact]
    public void Braise_UnFeuConsumeLaisseUneBraise_CouverteElleTientLaNuit_EtLeFeuRepartAvecSonHistoire()
    {
        var installations = new Installations();
        var nu = installations.Allumer(10, 10, 1, Maintenant, 1, 100);
        var couvert = installations.Allumer(20, 20, 1, Maintenant, 2, 200);
        installations.Couvrir(20, 20);
        installations.Bruler(1.5);
        Assert.Empty(installations.Foyers);
        var b1 = installations.BraiseEn(10, 10)!;
        var b2 = installations.BraiseEn(20, 20)!;
        Assert.False(b1.Couvert);
        Assert.True(b2.Couvert);
        Assert.InRange(b1.Heures, 0.5, 2);
        Assert.InRange(b2.Heures, 6, 16);
        // Trois heures plus tard, la braise à l'air libre est morte ; l'autre couve encore.
        installations.Bruler(3);
        Assert.Null(installations.BraiseEn(10, 10));
        Assert.NotNull(installations.BraiseEn(20, 20));
        // Ranimée au matin, elle redevient le même foyer : son allumeur et sa cause restent.
        var ranime = installations.Allumer(20, 20, 2, Maintenant + Jour, 3, 300);
        Assert.Equal(couvert.Id, ranime.Id);
        Assert.Equal(2, ranime.AllumeurId);
        Assert.Equal(200, ranime.EvenementId);
        Assert.Equal(3, ranime.GardienId);
        Assert.Null(installations.BraiseEn(20, 20));
        Assert.NotEqual(nu.Id, ranime.Id);
    }

    [Fact]
    public void Foyer_DuBoisAjouteDecouvreLeFeu_EtLeDegagerResteAcquis()
    {
        var installations = new Installations();
        installations.Allumer(5, 5, 2, Maintenant, 1, 1, degage: true);
        installations.Couvrir(5, 5);
        Assert.True(installations.FoyerEn(5, 5)!.Couvert);
        installations.Nourrir(5, 5, 2, Maintenant, 4, 2);
        var f = installations.FoyerEn(5, 5)!;
        Assert.False(f.Couvert);
        Assert.True(f.Degage);
        Assert.Equal(4, f.GardienId);
    }

    [Fact]
    public void PorteFeu_UneCordeApprocheeDuFeuCouve_ParfoisSeulement()
    {
        var alea = new Alea(3);
        var contexte = new ContexteGeste(true, true, 0.5, Biome.PlaineDeFougeres, 0.8, 0.5);
        var meches = 0;
        for (var i = 0; i < 400; i++)
            if (Physique.Tenter(Geste.Approcher, Matiere.Corde, Matiere.Feu, contexte, alea).Effet == Effet.PorteFeu) meches++;
        Assert.InRange(meches, 40, 360);
        Assert.Contains("couve", Physique.Nom(Matiere.PorteFeu).Texte);
    }

    [Fact]
    public void Imprudence_EstUnePetiteFaute_JamaisUnMeurtre_PlusLourdeSiElleTue()
    {
        for (var n = 0.0; n <= 1; n += 0.1)
        {
            Assert.InRange(Monde.Simulation.Humains.Comportement.PointsImprudence(n, true), -3, -0.5);
            Assert.InRange(Monde.Simulation.Humains.Comportement.PointsImprudence(n, false), -3, -0.5);
            Assert.True(Monde.Simulation.Humains.Comportement.PointsImprudence(n, true) <= Monde.Simulation.Humains.Comportement.PointsImprudence(n, false));
        }
        Assert.True(Monde.Simulation.Humains.Comportement.PointsImprudence(1, true) < Monde.Simulation.Humains.Comportement.PointsImprudence(0, true));
    }

    [Fact]
    public void FeuDeCamp_LeVentDuJourVarieSelonLaRegion_SansHasardPartage()
    {
        var eco = new Ecosysteme(Carte);
        var a = new Catastrophes(Carte, eco, new Alea(1));
        var b = new Catastrophes(Carte, eco, new Alea(99));
        var vents = new HashSet<double>();
        for (var j = 0; j < 60; j++)
        {
            var v = a.VentEn(40, 40, Maintenant + j * Jour);
            Assert.InRange(v, 0.3, 2);
            Assert.Equal(v, b.VentEn(40, 40, Maintenant + j * Jour));
            vents.Add(Math.Round(v, 2));
        }
        Assert.True(vents.Count > 20);
    }

    [Fact]
    public void FeuDeCamp_SansPersonnePourLeGarder_FinitParGagnerLesHerbes_EtNommeSonGardien()
    {
        // Un foyer laissé seul au milieu des herbes, chaque heure pendant des années : il finit par s'échapper.
        var (x, y) = CaseInflammable();
        var eco = new Ecosysteme(Carte);
        var catastrophes = new Catastrophes(Carte, eco, new Alea(11));
        var gardien = Humain(1);
        gardien.X = x + 6;
        gardien.Y = y;
        var humains = new List<Humain> { gardien };
        long? parti = null;
        for (var heure = 0; heure < 24 * 360 * 6 && parti is null; heure++)
        {
            var t = Maintenant + heure * Calendrier.SecondesParHeure;
            var installations = new Installations([new Foyer(1, x, y, 5, t, 1, 0)]);
            catastrophes.FeuxDeCamp(installations, humains, t);
            if (catastrophes.EnMemoire.Any(c => c.Type == TypeCatastrophe.Incendie)) parti = t;
        }
        Assert.NotNull(parti);
        var incendie = catastrophes.EnMemoire.First(c => c.Type == TypeCatastrophe.Incendie);
        Assert.Equal(1, incendie.ResponsableId);
        Assert.Equal(Effet.EteindreEnPartant, incendie.Lecon);
        Assert.True(incendie.Negligence >= 0.4);
        Assert.Contains(catastrophes.Journal, e => e.Texte.Contains("sans personne pour le garder"));
    }

    [Fact]
    public void Reserves_SeMelentChezLeMemeAuteur_EtSePuisentJusquaDisparaitre()
    {
        var installations = new Installations();
        var r = installations.Reserver(3, 3, SorteReserve.ViandeSechee, 4, 0.6, Maintenant, 1, 2, 10);
        installations.Changer(r.Id, r with { Etat = 0.5 });
        var melange = installations.Reserver(3, 3, SorteReserve.ViandeSechee, 4, 0.8, Maintenant + Jour, 1, 2, 11);
        Assert.Equal(r.Id, melange.Id);
        Assert.Equal(8, melange.Kg, 6);
        Assert.Equal(0.75, melange.Etat, 6);
        Assert.Single(installations.Reserves);
        installations.Reserver(3, 3, SorteReserve.ViandeSechee, 1, 0.5, Maintenant, 2, 2, 12);
        Assert.Equal(2, installations.ReservesEn(3, 3).Count());
        Assert.Equal(8, installations.Puiser(r.Id, 20), 6);
        Assert.Single(installations.Reserves);
        Assert.True(installations.ReserveEn(3, 3));
    }

    [Fact]
    public void Reserves_JoursDeVivres_ComptentLaNourritureDuGroupeSeulement()
    {
        Reserve[] reserves =
        [
            new(1, 0, 0, SorteReserve.NoixGraines, 10, 1, 0.5, 0, 1, 7, 0),
            new(2, 0, 0, SorteReserve.Eau, 50, 1, 0.5, 0, 1, 7, 0),
            new(3, 0, 0, SorteReserve.ViandeSechee, 100, 1, 0.5, 0, 2, 8, 0),
        ];
        // 10 kg de noix nourrissent 500 de faim ; une personne en dépense environ 33 par jour.
        var jours = Monde.Simulation.Humains.Comportement.JoursDeVivres(reserves, 7, 1);
        Assert.InRange(jours, 13, 17);
        Assert.Equal(jours / 5, Monde.Simulation.Humains.Comportement.JoursDeVivres(reserves, 7, 5), 6);
        Assert.Equal(0, Monde.Simulation.Humains.Comportement.JoursDeVivres(reserves, 9, 3));
    }

    [Fact]
    public void Reserves_LaViandeCacheeNeTientQueDansLeFroid_LaViandeSecheeDure()
    {
        var c = Comportement(4);
        var (x, y) = CaseInflammable();
        c.Installations.Reserver(x, y, SorteReserve.ViandeSechee, 10, 0.8, Maintenant, 0, 0, 0);
        c.Installations.Reserver(x, y, SorteReserve.ViandeCachee, 10, 0.8, Maintenant, 0, 0, 0);
        // En été, vingt jours.
        var ete = Maintenant + 2 * Calendrier.JoursParSaison * Jour;
        for (var j = 0; j < 20; j++) c.GaterLesReserves([], ete + j * Jour);
        Assert.DoesNotContain(c.Installations.Reserves, r => r.Sorte == SorteReserve.ViandeCachee);
        var sechee = Assert.Single(c.Installations.Reserves);
        Assert.Equal(SorteReserve.ViandeSechee, sechee.Sorte);
        Assert.True(sechee.Etat > 0.5, $"{sechee.Etat:0.00}");
    }

    [Fact]
    public void Reserves_ApresDesPartages_UnGroupeGenereuxPeutLesDireATous_ApresDesVolsUnGroupeMefiantASoi()
    {
        var genereux = Groupe(1, agreabilite: 0.9, honnetete: 0.9, partages: 30, vols: 0);
        Assert.Equal(IdeeReserve.Commune, genereux);
        var mefiant = Groupe(2, agreabilite: 0.1, honnetete: 0.1, partages: 0, vols: 10);
        Assert.Equal(IdeeReserve.ASoi, mefiant);

        static IdeeReserve Groupe(ulong graine, double agreabilite, double honnetete, double partages, double vols)
        {
            var c = Comportement(graine);
            var co = new Communaute { Id = 1, Nom = "Gens du gué", FondeeLe = 0, PartagesReserve = partages, VolsReserve = vols };
            c.Social!.Communautes = [co];
            var membres = Enumerable.Range(1, 6).Select(i => Humain(i, communaute: 1, agreabilite: agreabilite, honnetete: honnetete)).ToList();
            c.Installations.Reserver(1, 1, SorteReserve.NoixGraines, 10, 0.5, Maintenant, 1, 1, 0);
            for (var j = 0; j < 3000 && co.IdeeReserve == IdeeReserve.Aucune; j++)
            {
                co.PartagesReserve = partages;
                co.VolsReserve = vols;
                c.IdeesDesReserves(membres, Maintenant + j * Jour);
            }
            if (co.IdeeReserve != IdeeReserve.Aucune) Assert.Contains(c.Journal, e => e.CommunauteId == 1 && e.Significatif);
            return co.IdeeReserve;
        }
    }

    [Fact]
    public void Feu_DemandeAUnVoisin_UnCoeurGenereuxLeDonne_UnCoeurFroidLeRefuseSouvent()
    {
        static (int Dons, int Refus) Demander(double agreabilite, double honnetete)
        {
            var c = Comportement(8);
            int dons = 0, refus = 0;
            for (var i = 0; i < 200; i++)
            {
                var h = Humain(1);
                var hote = Humain(2, agreabilite: agreabilite, honnetete: honnetete);
                c.Journal.Clear();
                c.DemanderLeFeu(h, hote, Maintenant);
                if (h.Objets.Exists(o => o.Matiere is Matiere.Brandon or Matiere.PorteFeu)) dons++;
                else refus++;
            }
            Assert.All(c.Journal, e => Assert.False(string.IsNullOrEmpty(e.Texte)));
            return (dons, refus);
        }
        var genereux = Demander(0.95, 0.95);
        var froid = Demander(0.05, 0.05);
        Assert.True(genereux.Dons > 130, $"{genereux}");
        Assert.True(froid.Refus > genereux.Refus + 30, $"{froid} contre {genereux}");
    }

    [Fact]
    public void Reserves_ChezSoiOnPuise_ChezAutruiSeulementSiLeGroupeLesDitATous()
    {
        var c = Comportement(2);
        var co = new Communaute { Id = 1, Nom = "Gens du gué", FondeeLe = 0 };
        c.Social!.Communautes = [co];
        var auteur = Humain(1, communaute: 1);
        var autre = Humain(2, communaute: 1);
        var etranger = Humain(3, communaute: 0);
        List<Humain> tous = [auteur, autre, etranger];
        var r = new Reserve(1, 0, 0, SorteReserve.NoixGraines, 5, 1, 0.5, 0, 1, 1, 0);
        Assert.True(c.Permis(auteur, r, tous));
        Assert.False(c.Permis(etranger, r, tous));
        co.IdeeReserve = IdeeReserve.ASoi;
        Assert.False(c.Permis(autre, r, tous));
        co.IdeeReserve = IdeeReserve.Commune;
        Assert.True(c.Permis(autre, r, tous));
        // L'auteur mort : ce qu'il a laissé est à qui le trouve.
        co.IdeeReserve = IdeeReserve.ASoi;
        auteur.MortLe = Maintenant;
        Assert.True(c.Permis(autre, r, tous));
    }

    [Fact]
    public void Sauvegarde_BraisesReservesEtCeQuOnSaitDuFeu_SontRelus()
    {
        var fichier = Path.Combine(_dossier, "monde.db");
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var etat = monde.Etat;
        var h = etat.Humains![0];
        h.FeuxPortes = 4;
        h.PorteurDuFeuDepuis = 1234;
        h.FeuDemandeLe = 5678;
        var co = new Communaute
        {
            Id = 1, Nom = "Gens du gué", FondeeLe = 0, IncendiesSouvenus = [12, 40], IdeeReserve = IdeeReserve.ASoi, IdeeReserveLe = 99,
            IdeeReserveEvenementId = 7, PartagesReserve = 1.5, VolsReserve = 2.5, VolReserveEvenementId = 8,
        };
        etat = etat with
        {
            Foyers = [new Foyer(3, 1, 2, 4.5, 10, 1, 20, Couvert: true, Degage: true, EntretenuParId: 2)],
            Braises = [new Braise(4, 5, 6, 7.5, true, 11, 2, 21)],
            Reserves = [new Reserve(1, 8, 9, SorteReserve.FruitsSeches, 3.5, 0.9, 0.7, 12, 1, 1, 22)],
            Communautes = [co],
        };
        new DepotMonde(fichier).Enregistrer(etat);
        var relu = new DepotMonde(fichier).Charger()!;
        Assert.Equal(etat.Foyers, relu.Foyers);
        Assert.Equal(etat.Braises, relu.Braises);
        Assert.Equal(etat.Reserves, relu.Reserves);
        var rh = relu.Humains![0];
        Assert.Equal((4, 1234L, 5678L), (rh.FeuxPortes, rh.PorteurDuFeuDepuis, rh.FeuDemandeLe));
        var rc = Assert.Single(relu.Communautes!);
        Assert.Equal([12L, 40L], rc.IncendiesSouvenus);
        Assert.Equal((IdeeReserve.ASoi, 99L, 7L, 1.5, 2.5, 8L),
            (rc.IdeeReserve, rc.IdeeReserveLe, rc.IdeeReserveEvenementId, rc.PartagesReserve, rc.VolsReserve, rc.VolReserveEvenementId));
    }

    [Fact]
    public void Sauvegarde_UnMondeDeLaVersion31_SOuvreSansPerte()
    {
        var fichier = Path.Combine(_dossier, "ancien.db");
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        new DepotMonde(fichier).Enregistrer(monde.Etat);
        DepotMondeTests.ExecuterSql(fichier, """
            DROP TABLE braises;
            DROP TABLE reserves;
            ALTER TABLE foyers DROP COLUMN couvert;
            ALTER TABLE foyers DROP COLUMN degage;
            ALTER TABLE foyers DROP COLUMN entretenu;
            ALTER TABLE humains DROP COLUMN feux_portes;
            ALTER TABLE humains DROP COLUMN porteur_du_feu_depuis;
            ALTER TABLE humains DROP COLUMN feu_demande_le;
            ALTER TABLE communautes DROP COLUMN incendies_souvenus;
            ALTER TABLE communautes DROP COLUMN idee_reserve;
            ALTER TABLE catastrophes DROP COLUMN victimes;
            ALTER TABLE catastrophes DROP COLUMN lecon;
            PRAGMA user_version = 31;
            """);
        var relu = new DepotMonde(fichier).Charger()!;
        Assert.Equal(monde.Etat.Humains!.Count, relu.Humains!.Count);
        Assert.All(relu.Humains, x => Assert.Equal(0, x.FeuxPortes));
        Assert.Null(relu.Braises);
        Assert.Null(relu.Reserves);
    }

    /// <summary>Une case de plaine herbeuse, loin de l'eau et des bords.</summary>
    private static (int X, int Y) CaseInflammable()
    {
        for (var y = 20; y < Carte.Hauteur - 20; y++)
        for (var x = 20; x < Carte.Largeur - 20; x++)
        {
            var b = Carte.BiomeEn(x, y);
            if (b is Biome.PlaineDeFougeres) return (x, y);
        }
        throw new InvalidOperationException("Aucune plaine sur la carte de test.");
    }
}
