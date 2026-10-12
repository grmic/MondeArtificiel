using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Petits enfants : âge d'autonomie, celui qui veille sur eux, et pas d'hostilité adulte contre un enfant.</summary>
public sealed class EnfantsTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, double age, double ouverture = 0.5, double conscience = 0.5, int mere = 0, int pere = 0) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = Sexe.Femme,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(ouverture, conscience, 0.5, 0.5, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        Reserves = 50,
        Sante = 100,
        MereId = mere,
        PereId = pere,
    };

    [Fact]
    public void AgeAutonomie_EntreTroisEtSixAns_PlusTotChezLesCurieux()
    {
        var curieux = Humain(1, 3, ouverture: 1, conscience: 0);
        var prudent = Humain(2, 3, ouverture: 0, conscience: 1);
        Assert.InRange(curieux.AgeAutonomie, 3, 6);
        Assert.InRange(prudent.AgeAutonomie, 3, 6);
        Assert.True(curieux.AgeAutonomie < prudent.AgeAutonomie);
        Assert.True(prudent.EstPetitEnfant(Maintenant));
    }

    [Fact]
    public void Gardien_LaMereSinonLAdulteQuiLAimeLePlus()
    {
        var mere = Humain(1, 30);
        var tante = Humain(2, 35);
        var ami = Humain(3, 25);
        var frere = Humain(4, 10);
        var enfant = Humain(5, 3, mere: 1);
        var tous = new List<Humain> { mere, tante, ami, frere, enfant };
        enfant.Liens.Add(new Lien(2, 80, 70, Maintenant));
        enfant.Liens.Add(new Lien(3, 80, 40, Maintenant));
        enfant.Liens.Add(new Lien(4, 90, 95, Maintenant));
        Assert.Same(mere, Comportement.Gardien(enfant, tous, Maintenant));

        mere.MortLe = Maintenant - An;
        // Le frère de 10 ans l'aime plus, mais il est trop jeune pour veiller sur lui : c'est la tante.
        Assert.Same(tante, Comportement.Gardien(enfant, tous, Maintenant));

        tante.MortLe = ami.MortLe = Maintenant - An;
        Assert.Null(Comportement.Gardien(enfant, tous, Maintenant));
    }
}
