using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.45.26 : le manteau neigeux de la simulation, et ce qu'il fait à la marche, aux pieds et au froid des humains et des bêtes.</summary>
public sealed class NeigeTests : IDisposable
{
    private const long Jour = Calendrier.SecondesParJour;
    private const long Heure = Calendrier.SecondesParHeure;

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-neige-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static Carte Carte => MondeSimuleTests.Carte42;

    /// <summary>Une case de terre ferme (plaine) de la carte.</summary>
    private static int Plaine() => Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).First(i => Carte.BiomeEn(i % Carte.Largeur, i / Carte.Largeur) == Biome.PlaineDeFougeres);

    /// <summary>Compte la neige pendant des jours, avec une température donnée partout.</summary>
    private static Neige Compter(Func<int, int, long, double> temperature, long depuis, int jours, Neige? neige = null)
    {
        var n = neige ?? new Neige(Carte, new Ecosysteme(Carte));
        for (var s = depuis + Heure; s <= depuis + jours * Jour; s += Heure) n.Heure(s, temperature);
        return n;
    }

    [Fact]
    public void PartDeNeige_SuitLaTemperature_EtLAirSec()
    {
        Assert.True(Averses.PartDeNeige(-3, 1) > 0.95);
        Assert.True(Averses.PartDeNeige(5, 1) < 0.01);
        Assert.Equal(0.5, Averses.PartDeNeige(1, 1), 3);
        // L'air sec laisse tomber la neige plus haut en température que l'air humide (Jennings et al., 2018).
        Assert.True(Averses.SeuilDeNeige(0.4) > Averses.SeuilDeNeige(1.8));
        Assert.InRange(Averses.SeuilDeNeige(0.1), -0.4, 2.4);
        Assert.InRange(Averses.SeuilDeNeige(3), -0.4, 2.4);
    }

    [Fact]
    public void SousLeGel_LaNeigeSEntasse_PuisFondQuandIlFaitDoux()
    {
        var hiver = 270 * Jour;
        var neige = Compter((_, _, _) => -6, hiver, 20);
        var terre = Enumerable.Range(0, neige.Eau.Length).Where(i => neige.Eau[i] > 0).ToList();
        Assert.NotEmpty(terre);
        // Rien sur l'eau libre.
        foreach (var i in terre) Assert.False(Carte.BiomeEn(i % Carte.Largeur, i / Carte.Largeur) is Biome.Ocean or Biome.Lac or Biome.Riviere);
        var max = terre.Max(i => neige.Epaisseur[i]);
        Assert.InRange(max, 5, 400);
        // Tassée : entre la neige fraîche et la neige regelée.
        foreach (var i in terre) Assert.InRange(neige.DensiteEn(i), 50, 460);
        Assert.True(neige.Quelque);

        // Dix jours à 8 °C : tout fond.
        Compter((_, _, _) => 8, hiver + 20 * Jour, 10, neige);
        Assert.All(neige.Eau, e => Assert.Equal(0, e));
        Assert.False(neige.Quelque);
    }

    [Fact]
    public void ADixDegres_IlNeNeigeJamais()
    {
        var neige = Compter((_, _, _) => 10, 270 * Jour, 30);
        Assert.False(neige.Quelque);
    }

    [Fact]
    public void LaNeigeFondMoinsVite_SousLesArbres()
    {
        var neige = new Neige(Carte, new Ecosysteme(Carte));
        var plaine = Plaine();
        var foret = Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).First(i => Carte.BiomeEn(i % Carte.Largeur, i / Carte.Largeur) == Biome.ForetDeConiferes);
        foreach (var i in new[] { plaine, foret }) (neige.Eau[i], neige.Epaisseur[i]) = (60, 30);
        neige.Reprendre(270 * Jour);
        Compter((_, _, _) => 2, 270 * Jour, 2, neige);
        Assert.True(neige.Eau[foret] > neige.Eau[plaine]);
    }

    [Fact]
    public void LaNeigeFreine_DavantageLesJambesCourtes()
    {
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1));
        var neige = new Neige(Carte, new Ecosysteme(Carte));
        var i = Plaine();
        var maintenant = 270 * Jour;
        Humain Age(double ans) => new()
        {
            Id = 1, Nom = "A", Sexe = Sexe.Homme, Naissance = maintenant - (long)(ans * Calendrier.SecondesParAn), Emplacement = 1, PartenaireId = 0,
            Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, new Intelligence(100, 100, 100, 100, 100, 100)),
            Biologie = Biologie.ParDefaut(Sexe.Homme),
        };
        var adulte = Age(30);
        var enfant = Age(5);
        // Sans neige, rien ne change.
        c.Neige = neige;
        Assert.Equal(1, c.FacteurNeige(adulte, i, maintenant));
        // 45 cm de neige fraîche (100 kg/m³) : jusqu'au genou d'un adulte, qui marche deux à trois fois moins vite ; l'enfant bien moins vite encore.
        (neige.Eau[i], neige.Epaisseur[i]) = (45, 45);
        var fa = c.FacteurNeige(adulte, i, maintenant);
        var fe = c.FacteurNeige(enfant, i, maintenant);
        Assert.InRange(fa, 0.25, 0.5);
        Assert.True(fe < fa * 0.75);
        // La même neige tassée (300 kg/m³, 15 cm) gêne bien moins.
        (neige.Eau[i], neige.Epaisseur[i]) = (45, 15);
        Assert.InRange(c.FacteurNeige(adulte, i, maintenant), 0.7, 1);
        // Une petite poudrée ne gêne presque pas.
        (neige.Eau[i], neige.Epaisseur[i]) = (2, 2);
        Assert.InRange(c.FacteurNeige(adulte, i, maintenant), 0.92, 1);
    }

    [Fact]
    public void LesGrandesBetes_PassentMieux_QueLesPetites()
    {
        Bete B(int forme, double poids) => new(1, Biome.PlaineDeFougeres, forme, 0, RegimeBete.Herbivore, poids, 2, 1, false, 0.5, 0.5, false, false, 0);
        Assert.Equal(1, Comportement.MobiliteBete(B(1, 70), 0, 0));
        var cerf = Comportement.MobiliteBete(B(1, 70), 50, 120);
        var petit = Comportement.MobiliteBete(B(1, 10), 50, 120);
        var geant = Comportement.MobiliteBete(B(0, 5000), 50, 120);
        Assert.True(petit < cerf && cerf < geant);
        // Un rôdeur de même poids enfonce moins qu'une bête à sabots.
        Assert.True(Comportement.MobiliteBete(B(4, 30), 40, 150) > Comportement.MobiliteBete(B(1, 30), 40, 150));
    }

    [Fact]
    public void DansLaNeige_LesHumainsOntPlusFroid_EtMalAuxPieds()
    {
        // Même monde, même hiver, sous un grand froid : avec ou sans 30 cm de neige au sol.
        (double Froid, double Pieds) Vivre(bool avecNeige)
        {
            var depart = MondeSimule.Nouveau(ConfigurationMonde.Nouvelle(42)) with { Secondes = 275 * Jour + 9 * Heure };
            var n = Carte.Largeur * Carte.Hauteur;
            if (avecNeige)
            {
                depart = depart with { NeigeEau = Enumerable.Repeat(60f, n).ToArray(), NeigeEpaisseur = Enumerable.Repeat(30f, n).ToArray() };
            }
            var monde = new MondeSimule(Carte, depart);
            monde.Declencher(TypeCatastrophe.VagueDeFroid, 0, 0, -20);
            monde.AvancerDe(6 * Heure);
            var vivants = monde.Etat.Humains!.Where(h => h.EstVivant).ToList();
            return (vivants.Average(h => h.Froid), vivants.Average(h => h.Pieds));
        }
        var sans = Vivre(false);
        var avec = Vivre(true);
        Assert.True(avec.Froid > sans.Froid, $"froid {avec.Froid} contre {sans.Froid}");
        Assert.True(avec.Pieds > sans.Pieds + 5, $"pieds {avec.Pieds} contre {sans.Pieds}");
    }

    [Fact]
    public void LaNeige_EstGardee_AvecLeMonde()
    {
        var n = Carte.Largeur * Carte.Hauteur;
        var eau = new float[n];
        var epaisseur = new float[n];
        var i = Plaine();
        (eau[i], epaisseur[i]) = (12.5f, 9.25f);
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42) with { NeigeEau = eau, NeigeEpaisseur = epaisseur });
        var fichier = Path.Combine(_dossier, "monde.db");
        new DepotMonde(fichier).Enregistrer(monde.Etat);
        var relu = new DepotMonde(fichier).Charger()!;
        Assert.Equal(12.5f, relu.NeigeEau![i]);
        Assert.Equal(9.25f, relu.NeigeEpaisseur![i]);
        var dto = NeigeDto.Depuis(new MondeSimule(Carte, relu));
        Assert.Equal(n, dto.Epaisseur.Length);
        // v = 16 √cm.
        Assert.InRange(Math.Pow(dto.Epaisseur[i] / 16.0, 2), 9, 10);
    }
}
