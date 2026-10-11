using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Jardin secret : ce qu'on y confie, ce qui s'efface, ce que le deuil, les naissances et les catastrophes y laissent.</summary>
public sealed class JardinTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, Sexe sexe = Sexe.Femme, double transcendance = 0.5, double ouverture = 0.5, int mere = 0) => new()
    {
        Id = id,
        MereId = mere,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - 25 * An,
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, 0.5, 0.5, transcendance, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        Reserves = 60,
    };

    [Fact]
    public void Confier_UneMemeCleRemplace_EtLeJardinPleinOublieLePlusFaible()
    {
        var h = Humain(1);
        Jardin.Confier(h, TypeIntime.Opinion, 2, "Je peux compter sur H2.", 45, 0, Maintenant, "opinion:2");
        Jardin.Confier(h, TypeIntime.Opinion, 2, "Je me méfie de H2.", 60, 7, Maintenant + 1, "opinion:2");
        var opinion = Assert.Single(h.Memoire.Jardin);
        Assert.Equal(("Je me méfie de H2.", 7L), (opinion.Texte, opinion.CauseId));

        for (var i = 0; i < Jardin.Taille + 5; i++) Jardin.Confier(h, TypeIntime.Peur, 0, $"peur {i}", 10 + i, 0, Maintenant, $"peur:{i}");
        Assert.Equal(Jardin.Taille, h.Memoire.Jardin.Count);
        Assert.Contains(h.Memoire.Jardin, x => x.Cle == "opinion:2");
        Assert.DoesNotContain(h.Memoire.Jardin, x => x.Cle == "peur:0");
        // Trop faible pour être retenu.
        Jardin.Confier(h, TypeIntime.Joie, 0, "à peine", 2, 0, Maintenant, "joie:rien");
        Assert.DoesNotContain(h.Memoire.Jardin, x => x.Cle == "joie:rien");
    }

    [Fact]
    public void Estomper_LesCroyancesDurentPlusQueLesPeurs_EtLesMortsGardentLeurPlusFortesPensees()
    {
        var h = Humain(1);
        Jardin.Confier(h, TypeIntime.Croyance, 0, "La montagne est vivante.", 60, 0, Maintenant, "croyance");
        Jardin.Confier(h, TypeIntime.Peur, 0, "J'ai peur.", 60, 0, Maintenant, "peur");
        for (var semaine = 0; semaine < 10; semaine++) Jardin.Estomper(h);
        var croyance = h.Memoire.Jardin.Single(x => x.Cle == "croyance").Intensite;
        var peur = h.Memoire.Jardin.Single(x => x.Cle == "peur").Intensite;
        Assert.InRange(peur, 5, 45);
        Assert.True(croyance > 55 && croyance < 60, $"croyance {croyance}");

        for (var i = 0; i < 30; i++) Jardin.Confier(h, TypeIntime.Espoir, 0, $"espoir {i}", 20 + i, 0, Maintenant, $"espoir:{i}");
        h.MortLe = Maintenant;
        Jardin.Estomper(h);
        Assert.Equal(Jardin.TailleApresLaMort, h.Memoire.Jardin.Count);
        Assert.Contains(h.Memoire.Jardin, x => x.Cle == "croyance");
    }

    [Fact]
    public void Deuil_LaissePeineEtSoifDeVengeanceContreLeMeurtrier()
    {
        var mere = Humain(1);
        var fils = Humain(2, Sexe.Homme, mere: 1);
        var meurtrier = Humain(3, Sexe.Homme);
        fils.MortLe = Maintenant;
        fils.CauseDeMort = CauseDeMort.Meurtre;
        fils.MeurtrierId = 3;
        fils.MortEvenementId = 42;
        var tous = new List<Humain> { mere, fils, meurtrier };
        new VieSociale(new Alea(1)).Endeuiller(fils, tous, Maintenant);

        var peine = Assert.Single(mere.Memoire.Jardin, x => x.Type == TypeIntime.Peine);
        Assert.Equal((2, 42L, "deuil:2"), (peine.SujetId, peine.CauseId, peine.Cle));
        var vengeance = Assert.Single(mere.Memoire.Jardin, x => x.Type == TypeIntime.Intention);
        Assert.Equal((3, "Je vengerai H2 : H3 paiera."), (vengeance.SujetId, vengeance.Texte));
        Assert.Empty(meurtrier.Memoire.Jardin);
    }

    [Fact]
    public void Catastrophe_LesEspritsSpirituelsYVoientUneVolonte_LesCurieuxSInterrogent()
    {
        var alea = new Alea(3);
        var spirituels = Enumerable.Range(1, 50).Select(i => Humain(i, transcendance: 1, ouverture: 0)).ToList();
        var curieux = Enumerable.Range(51, 50).Select(i => Humain(i, transcendance: 0, ouverture: 1)).ToList();
        foreach (var h in spirituels.Concat(curieux)) Jardin.Interpreter(h, TypeCatastrophe.Seisme, 77, 60, alea, Maintenant);

        Assert.InRange(spirituels.Count(h => h.Memoire.Jardin.Any(x => x.Type == TypeIntime.Croyance)), 20, 45);
        Assert.DoesNotContain(curieux, h => h.Memoire.Jardin.Any(x => x.Type == TypeIntime.Croyance));
        Assert.InRange(curieux.Count(h => h.Memoire.Jardin.Any(x => x.Type == TypeIntime.Question)), 15, 40);
        Assert.All(spirituels.Concat(curieux).SelectMany(h => h.Memoire.Jardin), x => Assert.Equal(77, x.CauseId));
    }

    [Fact]
    public void Monde_SurUnAn_RemplitLesJardins()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(An);
        var vivants = monde.Etat.Humains!.Where(h => h.EstVivant && !h.EstEnfant(monde.Etat.Secondes)).ToList();
        Assert.All(vivants, h => Assert.Contains(h.Memoire.Jardin, x => x.Type == TypeIntime.ImageDeSoi));
        Assert.Contains(vivants, h => h.Memoire.Jardin.Any(x => x.Type == TypeIntime.Opinion));
        Assert.All(vivants, h => Assert.InRange(h.Memoire.Jardin.Count, 1, Jardin.Taille));
    }
}
