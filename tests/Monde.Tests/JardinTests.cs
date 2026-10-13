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

    /// <summary>Deux caractères opposés : chaleureux, anxieux en amour, à l'aise avec les mots ; froid, évitant, taiseux.</summary>
    private static Personnalite Caractere(bool chaleureux) => chaleureux
        ? new Personnalite(0.85, 0.7, 0.8, 0.85, 0.8, 0.8, 0.95, 0.5, new Intelligence(115, 135, 100, 100, 110, 120))
        {
            Moral = new TraitsMoraux(0.85, 0.9, 0.8, 0.9, 0.2, 0.9, 0.1, new FondementsMoraux(0.9, 0.8, 0.8, 0.5, 0.5)),
        }
        : new Personnalite(0.15, 0.2, 0.3, 0.1, 0.15, 0.1, 0.1, 0.5, new Intelligence(90, 70, 100, 100, 90, 80))
        {
            Moral = new TraitsMoraux(0.1, 0.1, 0.6, 0.1, 0.95, 0.1, 0.9, new FondementsMoraux(0.1, 0.2, 0.3, 0.5, 0.2)),
        };

    /// <summary>Un humain, son compagnon, un ami et un mort cher, tous dans la même situation ; seul le caractère change.</summary>
    private static (Humain H, List<Humain> Tous) Situation(bool chaleureux)
    {
        var h = Humain(1);
        h.Personnalite = Caractere(chaleureux);
        h.PartenaireId = 2;
        h.CommunauteId = 1;
        var compagnon = Humain(2, Sexe.Homme);
        compagnon.PartenaireId = 1;
        compagnon.CommunauteId = 1;
        var ami = Humain(3);
        ami.CommunauteId = 1;
        var mort = Humain(4, mere: 1);
        mort.MortLe = Maintenant - 200 * Calendrier.SecondesParJour;
        h.Liens.AddRange([new Lien(2, 90, 85, Maintenant), new Lien(3, 60, 80, Maintenant, Amis: true), new Lien(4, 80, 90, Maintenant - 200 * Calendrier.SecondesParJour)]);
        compagnon.Liens.Add(new Lien(1, 90, 85, Maintenant));
        ami.Liens.Add(new Lien(1, 60, 80, Maintenant, Amis: true));
        // Le mort l'avait frappé : une rancune contre lui reste chez l'ami (pour la culpabilité, la honte ou l'indifférence).
        ami.Griefs.Add(new Grief(1, MotifGrief.Coups, 60, 60, 9));
        return (h, [h, compagnon, ami, mort]);
    }

    [Fact]
    public void Mediter_DeuxCaracteresOpposes_NePensentPasLaMemeChose()
    {
        var communautes = new List<Communaute> { new() { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 3 } };
        var (doux, tousDoux) = Situation(chaleureux: true);
        var (froid, tousFroid) = Situation(chaleureux: false);
        for (var semaine = 0; semaine < 12; semaine++)
        {
            var t = Maintenant + semaine * 7 * Calendrier.SecondesParJour;
            foreach (var h in new[] { doux, froid }) for (var i = 0; i < 2; i++) h.Liens[i] = h.Liens[i] with { Derniere = t };
            Jardin.Mediter(doux, tousDoux, communautes, t);
            Jardin.Mediter(froid, tousFroid, communautes, t);
        }
        var pensees = (Humain h) => h.Memoire.Jardin.Select(x => x.Texte).ToHashSet();
        // Au plus une pensée commune (un simple constat, comme « on me trouve belle »), sur une dizaine chacun.
        Assert.InRange(pensees(doux).Intersect(pensees(froid)).Count(), 0, 1);
        Assert.All(new[] { doux, froid }, h => Assert.True(pensees(h).Count >= 6, string.Join(" / ", pensees(h))));

        // Le cœur chaleureux regrette son geste ; le froid n'en éprouve rien.
        Assert.Contains(doux.Memoire.Jardin, x => x.Type is TypeIntime.Culpabilite or TypeIntime.Honte && x.SujetId == 3);
        Assert.DoesNotContain(froid.Memoire.Jardin, x => x.Type is TypeIntime.Culpabilite or TypeIntime.Honte);
        // L'un et l'autre pensent à leur compagnon et à leur enfant mort, chacun à sa façon ; on ne « compte » pas sur un mort.
        Assert.All(new[] { doux, froid }, h => Assert.Contains(h.Memoire.Jardin, x => x.Type == TypeIntime.Amour && x.SujetId == 2));
        Assert.Contains(doux.Memoire.Jardin, x => x.Type == TypeIntime.Manque && x.SujetId == 4);
        Assert.DoesNotContain(doux.Memoire.Jardin.Concat(froid.Memoire.Jardin), x => x.Type == TypeIntime.Opinion && x.SujetId == 4);
        // L'anxieux en amour craint d'être délaissé, l'évitant tient à ses distances.
        Assert.Contains(doux.Memoire.Jardin, x => x.Cle == "couple-style" && x.Texte.Contains("lasse") || x.Texte.Contains("ne respire plus") || x.Texte.Contains("vraiment"));
        Assert.Contains(froid.Memoire.Jardin, x => x.Cle == "couple-style");
    }

    [Fact]
    public void Mediter_RienNeChange_RienNestReecrit_EtLeDesirNeVaPasAuxProches()
    {
        var communautes = new List<Communaute> { new() { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1 } };
        var h = Humain(1);
        h.CommunauteId = 1;
        h.Desir = 80;
        var frere = Humain(2, Sexe.Homme, mere: 5);
        var voisin = Humain(3, Sexe.Homme);
        foreach (var a in new[] { frere, voisin }) { a.CommunauteId = 1; a.Apparence = new Apparence(0.95, 0.95, 0, 0.5, 0.95); }
        var soeur = new Humain
        {
            Id = 4, MereId = 5, Nom = "H4", Sexe = Sexe.Femme, PartenaireId = 0, Emplacement = 1, Naissance = h.Naissance,
            Personnalite = h.Personnalite, Biologie = h.Biologie, Reserves = 60, CommunauteId = 1, Desir = 80,
        };
        var tous = new List<Humain> { h, frere, voisin, soeur, Humain(5) };
        Jardin.Mediter(h, tous, communautes, Maintenant);
        Jardin.Mediter(soeur, tous, communautes, Maintenant);
        var desir = Assert.Single(h.Memoire.Jardin, x => x.Type == TypeIntime.Desir);
        Assert.Contains(soeur.Memoire.Jardin, x => x.Type == TypeIntime.Desir && x.SujetId == 3);
        Assert.DoesNotContain(soeur.Memoire.Jardin, x => x.SujetId == 2 && x.Type is TypeIntime.Desir or TypeIntime.Amour);

        var avant = h.Memoire.Jardin.Select(x => (x.Cle, x.Texte, x.Secondes)).ToList();
        Jardin.Mediter(h, tous, communautes, Maintenant + 1);
        Assert.Equal(avant.Where(x => x.Cle is "soi" or "desir"), h.Memoire.Jardin.Select(x => (x.Cle, x.Texte, x.Secondes)).Where(x => x.Cle is "soi" or "desir"));
        Assert.True(desir.SujetId is 2 or 3);
    }
}
