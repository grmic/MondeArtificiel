using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.45.0 : les carnassiers qui attaquent, et où la mère couche son tout-petit la nuit.</summary>
public sealed class PredateursTests : IDisposable
{
    private static readonly Carte Carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));

    /// <summary>Premier jour du printemps de l'an 10, à minuit.</summary>
    private static readonly long Minuit = 10 * Calendrier.SecondesParAn;

    private static readonly Bete Fauve = new(1, Biome.PlaineDeFougeres, 5, 0, RegimeBete.Carnivore, 120, 3, 2, false, 0.8, 0.2, false, false, 0);

    private static readonly Bete Brouteur = new(2, Biome.PlaineDeFougeres, 2, 0, RegimeBete.Herbivore, 40, 1, 1.5, true, 0.2, 0.3, true, false, 0.6);

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-fauves-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static Humain Adulte(int id, int x, int y, Sexe sexe = Sexe.Femme, double age = 28, int mere = 0) => new()
    {
        Id = id,
        PartenaireId = 0,
        MereId = mere,
        Nom = $"H{id}",
        Sexe = sexe,
        Emplacement = id,
        Naissance = Minuit - (long)(age * Calendrier.SecondesParAn),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, new Intelligence(100, 100, 100, 100, 100, 100)),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
        Activite = Activite.Dormir,
    };

    private static (int X, int Y) Plaine()
    {
        for (var y = 10; y < Carte.Hauteur - 10; y++)
        for (var x = 10; x < Carte.Largeur - 10; x++)
            if (Carte.BiomeEn(x, y) == Biome.PlaineDeFougeres && Carte.Plantes[Carte.Indice(x, y)] >= 60) return (x, y);
        throw new InvalidOperationException("pas de plaine");
    }

    private static Comportement Nouveau(ulong graine = 1)
    {
        var champs = new Champs();
        return new Comportement(Carte, new Ecosysteme(Carte), new Alea(graine))
        {
            Social = new VieSociale(new Alea(1)) { Carte = Carte, Generation = 2, Champs = champs }, Generation = 2,
            Installations = new Installations(), Champs = champs, Faune = new Faune([Fauve, Brouteur]),
        };
    }

    [Fact]
    public void Carnassiers_SuiventLesProies_EtLesHerbivoresNEnSontPas()
    {
        var c = Nouveau();
        var (x, y) = Plaine();
        var (b, nombre, faim) = Assert.Single(c.Carnassiers(x, y));
        Assert.Equal(Fauve, b);
        // Environ 90 kg de carnassiers pour 10 tonnes de proies (Carbone et Gittleman, 2002) : une fraction de bête par case.
        Assert.InRange(nombre, 1e-4, 5);
        Assert.InRange(faim, 1, 3);
    }

    /// <summary>Seul, endormi, sans veilleur : bien plus exposé que chacun d'un groupe de huit (la vigilance et le nombre protègent).</summary>
    [Fact]
    public void LeDormeurSeul_EstPlusExposeQueChacunDUnGroupe()
    {
        var (x, y) = Plaine();
        var seul = Nouveau();
        seul.HeureDesFauves([Adulte(1, x, y)], Minuit + Calendrier.SecondesParHeure);
        var groupe = Nouveau();
        var huit = Enumerable.Range(1, 8).Select(i => Adulte(i, x, y, i % 2 == 0 ? Sexe.Homme : Sexe.Femme)).ToList();
        huit[0].Activite = Activite.Veiller;
        groupe.HeureDesFauves(huit, Minuit + Calendrier.SecondesParHeure);
        Assert.True(seul.RisqueCumule > 0);
        Assert.True(seul.RisqueCumule > 4 * groupe.RisqueCumule / huit.Count, $"seul {seul.RisqueCumule}, groupe {groupe.RisqueCumule / huit.Count} chacun");
    }

    /// <summary>Le jour, les bêtes chassent peu : le risque de la nuit est bien plus grand que celui de midi.</summary>
    [Fact]
    public void LaNuit_EstPlusDangereuseQueLeJour()
    {
        var (x, y) = Plaine();
        var nuit = Nouveau();
        nuit.HeureDesFauves([Adulte(1, x, y)], Minuit + Calendrier.SecondesParHeure);
        var jour = Nouveau();
        var h = Adulte(1, x, y);
        h.Activite = Activite.Cueillir;
        jour.HeureDesFauves([h], Minuit + 12 * Calendrier.SecondesParHeure);
        Assert.True(nuit.RisqueCumule > 3 * jour.RisqueCumule, $"nuit {nuit.RisqueCumule}, jour {jour.RisqueCumule}");
    }

    /// <summary>Une attaque finit par arriver ; elle est journalisée avec ses circonstances, et la bête est connue ensuite.</summary>
    [Fact]
    public void UneAttaque_EstJournalisee_EtLaBeteEstConnue()
    {
        var (x, y) = Plaine();
        var c = Nouveau(7);
        var h = Adulte(1, x, y);
        for (var heure = 0; heure < 2_000_000 && c.AttaquesDeBetes == 0; heure++)
        {
            if (!h.EstVivant) h = Adulte(1, x, y);
            c.HeureDesFauves([h], Minuit + (heure / 6 * 24 + heure % 6) * Calendrier.SecondesParHeure);
        }
        Assert.True(c.AttaquesDeBetes > 0);
        var f = Assert.Single(c.Fauves);
        Assert.Equal(Fauve.Numero, f.Espece);
        Assert.True(f.Attaques >= 1);
    }

    [Fact]
    public void LesBetesConnues_SontEnregistreesEtRelues()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(ConfigurationMonde.Nouvelle(2)));
        var f = new Fauve(1, 3, 10, 12, 6.5, 0.4, 0.7, 0.9, 2, 1, 1000, 2000, 33);
        var etat = monde.Etat with { Fauves = [f], FauvesManquants = [new(5, 0.25)], AttaquesDeBetes = 4, MortsDeBetes = 2 };
        var fichier = Path.Combine(_dossier, "monde.db");
        new DepotMonde(fichier).Enregistrer(etat);
        var relu = new DepotMonde(fichier).Charger()!;
        Assert.Equal(f, Assert.Single(relu.Fauves!));
        Assert.Equal(0.25, Assert.Single(relu.FauvesManquants!).Value);
        Assert.Equal((4, 2), (relu.AttaquesDeBetes, relu.MortsDeBetes));
    }

    /// <summary>
    /// Une nuit d'hiver froide, une mère couche presque toujours son tout-petit contre elle ; la décision est prise une fois pour la
    /// nuit, et au lever elle devient l'habitude.
    /// </summary>
    [Fact]
    public void Coucher_LaMereDecide_UneFoisParNuit()
    {
        var (x, y) = Plaine();
        var hiver = Minuit + 3 * 90 * Calendrier.SecondesParJour + 2 * Calendrier.SecondesParHeure;
        var contre = 0;
        const int Meres = 200;
        for (var k = 0; k < Meres; k++)
        {
            var c = Nouveau((ulong)k + 1);
            var mere = Adulte(1, x, y);
            var bebe = Adulte(2, x, y, age: 0.5, mere: 1);
            c.Reprendre([mere, bebe], hiver);
            Assert.Null(c.CoucherLeBebe(bebe, mere, hiver));
            Assert.NotEqual(Coucher.Aucune, bebe.Coucher);
            var choix = bebe.Coucher;
            c.CoucherLeBebe(bebe, mere, hiver + Calendrier.SecondesParHeure);
            Assert.Equal(choix, bebe.Coucher);
            if (choix == Coucher.Contre) contre++;
            Comportement.Lever(bebe);
            Assert.Equal((Coucher.Aucune, choix), (bebe.Coucher, bebe.CoucherPrecedent));
        }
        Assert.True(contre > Meres / 2, $"contre elle : {contre} sur {Meres}");
    }

    /// <summary>Malade, avec sa propre mère à côté : elle lui confie parfois le bébé pour la nuit.</summary>
    [Fact]
    public void Coucher_MaladeAvecSaMere_LeConfieParfois()
    {
        var (x, y) = Plaine();
        var nuit = Minuit + 2 * Calendrier.SecondesParHeure;
        var confie = 0;
        for (var k = 0; k < 200; k++)
        {
            var c = Nouveau((ulong)k + 1);
            var grandMere = Adulte(3, x, y, age: 50);
            var mere = Adulte(1, x, y, mere: 3);
            mere.Sante = 30;
            var bebe = Adulte(2, x, y, age: 0.5, mere: 1);
            c.Reprendre([mere, bebe, grandMere], nuit);
            if (c.CoucherLeBebe(bebe, mere, nuit) is { } g)
            {
                Assert.Equal(grandMere.Id, g.Id);
                Assert.Equal(Coucher.Confie, bebe.Coucher);
                confie++;
            }
        }
        Assert.InRange(confie, 10, 190);
    }
}
