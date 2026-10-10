using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Fécondité, grossesse et attachement : des valeurs propres à chacun, calées sur des données réelles.</summary>
public sealed class ReproductionTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long An = Calendrier.SecondesParAn;

    private static Comportement NouveauComportement() =>
        new(Carte, new Ecosysteme(Carte), new Alea(1));

    private static Humain Adulte(int id, Sexe sexe, double age, Biologie? biologie = null, double attachement = 0.9) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = 10 * An - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, attachement, 0.5, Intelligence.Moyenne),
        Biologie = biologie ?? new Biologie(16, sexe == Sexe.Femme ? 49 : 90, 1),
    };

    [Theory]
    [InlineData(15.9, 0)]      // avant la puberté
    [InlineData(22, 1.0)]
    [InlineData(32, 0.86)]
    [InlineData(42, 0.36)]
    [InlineData(49.5, 0)]      // après la ménopause
    public void FeconditeFemme_SuitLaCourbeSelonLAge(double age, double attendu)
    {
        var biologie = new Biologie(16, 49, 1);
        Assert.Equal(attendu, biologie.FeconditeFemme(age), precision: 6);
    }

    [Fact]
    public void FeconditeFemme_AdolescenceMoinsFeconde_EtSteriliteNulle()
    {
        var biologie = new Biologie(16, 49, 1);
        Assert.InRange(biologie.FeconditeFemme(17), 0.4, 0.6);
        Assert.Equal(0, (biologie with { Fertilite = 0 }).FeconditeFemme(22));
        Assert.True(new Biologie(16, 90, 1).FeconditeHomme(70) < new Biologie(16, 90, 1).FeconditeHomme(30));
    }

    [Fact]
    public void LoiNormale_CentreeReduite()
    {
        var alea = new Alea(7);
        var tirages = Enumerable.Range(0, 20_000).Select(_ => Biologie.Normale(alea)).ToList();
        var moyenne = tirages.Average();
        var ecart = Math.Sqrt(tirages.Average(x => (x - moyenne) * (x - moyenne)));
        Assert.InRange(moyenne, -0.03, 0.03);
        Assert.InRange(ecart, 0.97, 1.03);
    }

    [Fact]
    public void Population_TraitsVariesEtRealistes()
    {
        // Plusieurs graines pour avoir assez d'individus.
        var humains = new ulong[] { 1, 2, 3, 4, 5, 6 }
            .SelectMany(g => GenerateurPopulation.Generer(Carte, g, 0)).ToList();

        var attaches = humains.Count(h => h.Personnalite.Attachement >= 0.6) / (double)humains.Count;
        Assert.InRange(attaches, 0.75, 0.95); // la grande majorité, pas tous
        Assert.Contains(humains, h => h.Personnalite.Attachement < 0.3);

        Assert.InRange(humains.Average(h => h.Biologie.AgePuberte), 15.5, 16.5);
        Assert.All(humains, h => Assert.InRange(h.Biologie.AgePuberte, 12, 20));
        var femmes = humains.Where(h => h.Sexe == Sexe.Femme).ToList();
        Assert.InRange(femmes.Average(h => h.Biologie.AgeFinFertilite), 48, 50);
        Assert.InRange(humains.Average(h => h.Biologie.Fertilite), 0.9, 1.05);
    }

    [Fact]
    public void Naissances_EnvironAutantDeGarconsQueDeFilles_EtTraitsHerites()
    {
        var mere = Adulte(1, Sexe.Femme, 25, new Biologie(14, 45, 1.4), attachement: 1);
        var pere = Adulte(2, Sexe.Homme, 25, new Biologie(14, 90, 1.4), attachement: 1);
        var alea = new Alea(3);
        var noms = new HashSet<string>();
        var bebes = Enumerable.Range(3, 4000).Select(id => GenerateurPopulation.Naissance(id, mere, pere, 10 * An, alea, noms)).ToList();

        var garcons = bebes.Count(b => b.Sexe == Sexe.Homme) / (double)bebes.Count;
        Assert.InRange(garcons, 0.49, 0.535);
        // Hérité : puberté précoce et grande fertilité des parents, attachement fort.
        Assert.InRange(bebes.Average(b => b.Biologie.AgePuberte), 13.8, 14.4);
        Assert.InRange(bebes.Where(b => b.Biologie.Fertilite > 0).Average(b => b.Biologie.Fertilite), 1.1, 1.3);
        Assert.InRange(bebes.Average(b => b.Personnalite.Attachement), 0.85, 0.95);
        Assert.All(bebes.Where(b => b.Sexe == Sexe.Femme), b => Assert.InRange(b.Biologie.AgeFinFertilite, 40, 58));
    }

    [Fact]
    public void ChanceConception_BaisseAvecFaimAllaitementEtAge()
    {
        var comportement = NouveauComportement();
        var maintenant = 10 * An;
        var femme = Adulte(1, Sexe.Femme, 22);
        var homme = Adulte(2, Sexe.Homme, 25);
        List<Humain> tous = [femme, homme];

        var normale = comportement.ChanceConception(femme, homme, tous, maintenant);
        Assert.Equal(0.12, normale, precision: 6);

        femme.Faim = 90;
        Assert.True(comportement.ChanceConception(femme, homme, tous, maintenant) < normale / 4);
        femme.Faim = 0;

        // Bébé de 3 mois allaité : presque aucune chance.
        var bebe = Adulte(3, Sexe.Femme, 0.25);
        var allaite = new Humain
        {
            Id = 3, Nom = "B", Sexe = Sexe.Femme, PartenaireId = 0, Emplacement = 1, MereId = 1, PereId = 2,
            Naissance = bebe.Naissance, Personnalite = bebe.Personnalite, Biologie = bebe.Biologie,
            Activite = Activite.PorteParSaMere,
        };
        tous.Add(allaite);
        Assert.True(comportement.ChanceConception(femme, homme, tous, maintenant) < normale * 0.1);
        allaite.MortLe = maintenant - 1;
        Assert.Equal(normale, comportement.ChanceConception(femme, homme, tous, maintenant), precision: 6);

        var agee = Adulte(4, Sexe.Femme, 42);
        Assert.True(comportement.ChanceConception(agee, homme, tous, maintenant) < normale / 2);
        femme.EnceinteDepuis = maintenant;
        Assert.Equal(0, comportement.ChanceConception(femme, homme, tous, maintenant));
    }

    [Fact]
    public void MerePeuAttachee_DelaisseSonBebeQuandElleSouffre()
    {
        var attachee = Adulte(1, Sexe.Femme, 25, attachement: 0.9);
        var detachee = Adulte(2, Sexe.Femme, 25, attachement: 0.1);
        attachee.Faim = detachee.Faim = 95;

        Assert.False(Comportement.Delaisse(attachee));
        Assert.True(Comportement.Delaisse(detachee));
        detachee.Faim = 30;
        Assert.False(Comportement.Delaisse(detachee));
    }

    [Fact]
    public void SurSixAns_GrossessesDeDureesVariees_EtNaissancesEspacees()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var durees = new HashSet<long>();
        for (var jour = 0; jour < 6 * Calendrier.JoursParAn; jour++)
        {
            monde.AvancerDe(Calendrier.SecondesParJour);
            if (jour % 20 != 0) continue;
            var (_, humains) = monde.InstantaneHumains();
            foreach (var f in humains.Where(h => h.EnceinteDepuis is not null))
            {
                var jours = (f.TermeGrossesse!.Value - f.EnceinteDepuis!.Value) / Calendrier.SecondesParJour;
                Assert.InRange(jours, 230, 300);
                durees.Add(jours);
            }
        }
        Assert.True(durees.Count >= 10, $"durées de grossesse trop uniformes : {string.Join(", ", durees)}");

        // Écart moyen entre deux naissances d'une même mère (enfants nés vivants) : plusieurs années, comme chez les chasseurs-cueilleurs.
        var final = monde.Etat.Humains!;
        var ecarts = final.Where(h => h.MereId != 0).GroupBy(h => h.MereId)
            .SelectMany(g => g.OrderBy(h => h.Naissance).Zip(g.OrderBy(h => h.Naissance).Skip(1), (a, b) => (b.Naissance - a.Naissance) / (double)An))
            .Where(e => e > 0.1) // jumeaux exclus
            .ToList();
        Assert.NotEmpty(ecarts);
        Assert.InRange(ecarts.Average(), 1.5, 5);
    }
}
