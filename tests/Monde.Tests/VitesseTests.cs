using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.22 : index qui accélèrent le calcul sans changer ce qui se passe (vivants, enfants, feux et huttes par case).</summary>
public sealed class VitesseTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 30 * An;

    private static Humain Humain(int id, int mere = 0, int pere = 0, bool mort = false) => new()
    {
        Id = id, Nom = "H" + id, Sexe = id % 2 == 0 ? Sexe.Homme : Sexe.Femme, PartenaireId = 0, Emplacement = 1, MereId = mere, PereId = pere,
        Naissance = Maintenant - 20 * An, Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Homme : Sexe.Femme), Reserves = 50, Sante = 100,
        MortLe = mort ? Maintenant - An : null,
    };

    [Fact]
    public void LesVivantsSuiventLesMortsEtLesNaissances()
    {
        var p = new Population([Humain(1), Humain(2, mort: true), Humain(3), Humain(4)]);
        Assert.Equal([1, 3, 4], p.Vivants.Select(h => h.Id));
        Assert.Equal(4, p.Count);
        Assert.Same(p[1], ((IReadOnlyList<Humain>)p)[1]);

        // Celui qui meurt reste dans la liste jusqu'au recensement suivant (les parcours vérifient qu'il vit).
        p[2].MortLe = Maintenant;
        Assert.Equal([1, 3, 4], p.Vivants.Select(h => h.Id));
        p.Recenser();
        Assert.Equal([1, 4], p.Vivants.Select(h => h.Id));

        p.Ajouter(Humain(5, mere: 1, pere: 4));
        Assert.Equal([1, 4, 5], p.Vivants.Select(h => h.Id));
        Assert.Equal([1, 4, 5], ((IReadOnlyList<Humain>)p).Vivants().Select(h => h.Id));
    }

    [Fact]
    public void UneListeQuelconqueEstParcourueEnEntier()
    {
        IReadOnlyList<Humain> liste = [Humain(1), Humain(2, mort: true), Humain(3, mere: 1)];
        Assert.Equal([1, 2, 3], liste.Vivants().Select(h => h.Id));
        Assert.Equal([3], liste.EnfantsDe(1).Select(h => h.Id));
        Assert.Empty(liste.EnfantsDe(2));
    }

    [Fact]
    public void LesEnfantsDeChacunSontIndexesParLaMereEtLePere()
    {
        var p = new Population([Humain(1), Humain(2), Humain(3, mere: 1, pere: 2), Humain(4, mere: 1, mort: true), Humain(5, pere: 2)]);
        IReadOnlyList<Humain> tous = p;
        Assert.Equal([3, 4], tous.EnfantsDe(1).Select(h => h.Id));
        Assert.Equal([3, 5], tous.EnfantsDe(2).Select(h => h.Id));
        Assert.Empty(tous.EnfantsDe(3));
        p.Ajouter(Humain(6, mere: 1, pere: 2));
        Assert.Equal([3, 4, 6], tous.EnfantsDe(1).Select(h => h.Id));
        // Même réponse que le parcours de toute la liste.
        foreach (var h in p)
        {
            Assert.Equal(p.Where(e => e.MereId == h.Id || e.PereId == h.Id).Select(e => e.Id), tous.EnfantsDe(h.Id).Select(e => e.Id));
        }
    }

    [Fact]
    public void FeuxEtHuttesSontRetrouvesParCase()
    {
        var i = new Installations([new Foyer(1, 3, 4, 2, 0, 1, 0)], [new Hutte(2, 5, 6, 0.5, 0, 1, 0)]);
        Assert.NotNull(i.FoyerEn(3, 4));
        Assert.Null(i.FoyerEn(4, 3));
        Assert.NotNull(i.HutteEn(5, 6));
        Assert.Null(i.HutteEn(6, 5));

        i.Allumer(7, 7, 1, 0, 1, 0);
        Assert.NotNull(i.FoyerEn(7, 7));
        i.Bruler(1.5); // le feu d'une heure s'éteint, l'autre brûle encore
        Assert.Null(i.FoyerEn(7, 7));
        Assert.NotNull(i.FoyerEn(3, 4));
        i.Eteindre(3, 4);
        Assert.Null(i.FoyerEn(3, 4));
        i.Allumer(3, 4, 1, 0, 1, 0);
        Assert.NotNull(i.FoyerEn(3, 4));

        i.Batir(1, 1, 0.2, 0, 1, 0);
        i.Batir(5, 6, 0.9, 0, 1, 0); // refaite, pas doublée
        Assert.Equal(2, i.Huttes.Count);
        i.Affaisser(1, _ => 0.3); // la hutte la plus faible s'effondre
        Assert.Null(i.HutteEn(1, 1));
        Assert.Equal(0.6, i.HutteEn(5, 6)!.Solidite, 9);

        var copie = i.Copier();
        Assert.NotNull(copie.HutteEn(5, 6));
        Assert.NotNull(copie.FoyerEn(3, 4));
        Assert.Equal(0.3 + 0.4 * 0.6, copie.AbriEn(5, 6), 9);
        Assert.Equal(0, copie.AbriEn(1, 1));
    }
}
