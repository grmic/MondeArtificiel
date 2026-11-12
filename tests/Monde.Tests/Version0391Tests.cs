using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.39.1 : bandes de fondateurs, partage à la demande, le faible qui fuit, le goût de la guerre qui s'apaise.</summary>
public sealed class Version0391Tests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, int x, int y, double agreabilite = 0.5, double compassion = 0.5, double age = 30) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = id % 2 == 0 ? Sexe.Homme : Sexe.Femme,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, agreabilite, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne)
        {
            Moral = TraitsMoraux.Moyens with { Compassion = compassion },
        },
        Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Homme : Sexe.Femme),
        X = x,
        Y = y,
    };

    // ───────────── A1 : fondateurs en bandes ─────────────

    [Fact]
    public void TaillesDesBandes_DeCinqADouzeCouples_QuiFontTousLesCouples()
    {
        var alea = new Alea(3);
        foreach (var couples in new[] { 1, 4, 12, 13, 17, 33, 100, 200 })
        {
            var t = GenerateurContinent.TaillesDesBandes(alea, couples);
            Assert.Equal(couples, t.Sum());
            if (couples > GenerateurContinent.BandeMax) Assert.All(t, n => Assert.InRange(n, GenerateurContinent.BandeMin, GenerateurContinent.BandeMax));
        }
    }

    [Fact]
    public void Generation5_LesFondateursArriventEnBandes_QuiFormentDesCommunautes()
    {
        var conf = new ConfigurationMonde(42, 33, 128, 128, 1, Generation: 5);
        var carte = GenerateurContinent.Generer(conf);
        Assert.Equal(33, carte.Emplacements.Count);
        Assert.All(carte.Emplacements, e => Assert.True(e.Bande > 0));
        var bandes = carte.Emplacements.GroupBy(e => e.Bande).ToList();
        Assert.All(bandes, b => Assert.InRange(b.Count(), GenerateurContinent.BandeMin, GenerateurContinent.BandeMax));
        // Une bande campe sur quelques cases.
        Assert.All(bandes, b => Assert.True(b.Max(e => e.X) - b.Min(e => e.X) <= 2 && b.Max(e => e.Y) - b.Min(e => e.Y) <= 2));

        var humains = GenerateurPopulation.Generer(carte, 42, 0, 5);
        var h = humains[0];
        var bande = carte.Emplacements[h.Emplacement - 1].Bande;
        var siens = humains.Where(o => o.Id != h.Id && o.Id != h.PartenaireId && carte.Emplacements[o.Emplacement - 1].Bande == bande).ToList();
        Assert.All(siens, o => Assert.NotNull(h.LienAvec(o.Id)));
        Assert.Contains(h.Liens, l => l.Amis);

        // Dès la première semaine, la plupart des fondateurs vivent dans une communauté.
        var monde = new MondeSimule(carte, MondeSimule.Nouveau(conf));
        monde.AvancerDe(8 * Jour);
        var (_, apres) = monde.InstantaneHumains();
        Assert.True(apres.Count(o => o.EstVivant && o.CommunauteId != 0) > 0.7 * apres.Count(o => o.EstVivant));
    }

    [Fact]
    public void AvantLaGeneration5_LesCouplesRestentIsoles()
    {
        var carte = GenerateurContinent.Generer(new ConfigurationMonde(42, 33, 128, 128, 1, Generation: 4));
        Assert.All(carte.Emplacements, e => Assert.Equal(0, e.Bande));
        var humains = GenerateurPopulation.Generer(carte, 42, 0, 4);
        Assert.All(humains, h => Assert.Empty(h.Liens));
    }

    // ───────────── B1 : partage à la demande ─────────────

    /// <summary>Un affamé reçoit à manger d'un inconnu moins affamé qui campe près de lui ; d'un aussi affamé que lui, rien.</summary>
    [Fact]
    public void PartageALaDemande_UnVoisinMoinsAffameDonne_SansLienNiSatiete()
    {
        var e = Carte.Emplacements[0];
        double Apres(double faimDuVoisin)
        {
            var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(5)) { Social = new VieSociale(new Alea(5)) };
            var affame = Humain(1, e.X, e.Y);
            var voisin = Humain(2, Math.Min(Carte.Largeur - 1, e.X + 2), e.Y, agreabilite: 0.8, compassion: 0.8);
            affame.Faim = 92;
            voisin.Faim = faimDuVoisin;
            var tous = new List<Humain> { affame, voisin };
            c.RecenserLesCamps(tous);
            c.Vivre(affame, tous, Maintenant);
            return affame.Faim;
        }
        Assert.True(Apres(50) < Apres(85) - 0.05);
    }

    // ───────────── D3 : le goût de la guerre s'apaise ─────────────

    [Fact]
    public void Bellicisme_RedescendApresDesAnneesDePaix_PasPendantLaGuerre()
    {
        var adultes = Enumerable.Range(1, 6).Select(i => Humain(i, 10, 10, agreabilite: 0.6)).ToList();
        double Apres(long dernierRaid, int semaines = 52)
        {
            var social = new VieSociale(new Alea(1));
            var c = new Communaute { Id = 1, Nom = "Gens", FondeeLe = 0, FigureId = 1, Bellicisme = 0.92, DernierRaidLe = dernierRaid };
            for (var semaine = 0; semaine < semaines; semaine++) social.Apaiser(c, adultes, adultes, Maintenant + semaine * 7 * Jour);
            return c.Bellicisme;
        }
        var paix = Apres(Maintenant - 3 * An);
        Assert.InRange(paix, 0.5, 0.88);
        Assert.Equal(0.92, Apres(Maintenant, 40));
    }
}
