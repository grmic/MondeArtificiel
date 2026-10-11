using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Distances réelles : vitesse de marche, terrain et pente, mesures du continent, trajets, sauvegarde, hub.</summary>
public sealed class DistancesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;

    private static Humain Adulte(MondeSimule monde) => monde.Etat.Humains!.First(h => h.EstVivant);

    [Fact]
    public void Echelle_ConvertitEtEcritALaFrancaise()
    {
        Assert.Equal(204.8, Echelle.Kilometres(256), 6);
        Assert.Equal(1, Echelle.Kilometres(0, 0, 0, 0) + 1);
        Assert.Equal(4, Echelle.Kilometres(0, 0, 3, 4), 6); // 5 cases = 4 km
        Assert.Equal("600 m", Echelle.Distance(0.6));
        Assert.Equal("3,2 km", Echelle.Distance(3.2));
        Assert.Equal("1 250 km", Echelle.Distance(1250.4));
        Assert.Equal("64 hectares", Echelle.Surface(1));
        Assert.Equal("12 345 km²", Echelle.Surface(12_345.2));
        Assert.Equal(0, Echelle.Metres(Carte.NiveauMer));
        Assert.Equal(40, Echelle.Metres(Carte.NiveauMer + 1));
    }

    [Fact]
    public void VitesseDeMarche_DependDeLAgeDeLEtatEtDeLAllure()
    {
        Assert.Equal(0, Marche.FacteurAge(0.5));
        Assert.Equal(1, Marche.FacteurAge(30));
        Assert.True(Marche.FacteurAge(4) < Marche.FacteurAge(10));
        Assert.True(Marche.FacteurAge(80) < Marche.FacteurAge(65));
        // La courbe est continue : pas de saut d'un âge à l'autre.
        for (var age = 1.0; age < 100; age += 0.25) Assert.True(Math.Abs(Marche.FacteurAge(age + 0.25) - Marche.FacteurAge(age)) < 0.05);

        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var maintenant = monde.Secondes;
        var h = Adulte(monde);
        h.Activite = Activite.Repos;
        var vitesse = Marche.Vitesse(h, maintenant);
        Assert.InRange(vitesse * 3.6, 3.5, 6.5); // 4,9 km/h ± l'allure propre
        Assert.Equal(Marche.VitesseAdulte * h.Biologie.Marche * Marche.FacteurAge(h.AgeEnAnnees(maintenant)), vitesse, 9);

        h.Fatigue = 95;
        Assert.True(Marche.Vitesse(h, maintenant) < vitesse);
        h.Fatigue = 0;
        h.Sante = 20;
        Assert.True(Marche.Vitesse(h, maintenant) < vitesse);
        h.Sante = 100;
        h.Activite = Activite.Fuir;
        Assert.True(Marche.Vitesse(h, maintenant) > vitesse);

        // Les allures diffèrent d'un humain à l'autre, autour de 1.
        var allures = monde.Etat.Humains!.Select(x => x.Biologie.Marche).ToList();
        Assert.True(allures.Distinct().Count() > 30);
        Assert.InRange(allures.Average(), 0.95, 1.05);
        Assert.All(allures, a => Assert.InRange(a, 0.7, 1.3));
    }

    [Fact]
    public void Effort_DependDeLaDistanceDuTerrainEtDeLaPente()
    {
        // Une case en diagonale est plus longue qu'une case droite.
        Assert.Equal(800 * Math.Sqrt(2), Marche.Longueur(1, 1), 6);
        Assert.Equal(800, Marche.Longueur(1, 0), 6);
        // Tobler : la légère descente est la plus rapide, la montée ralentit, et une forte descente aussi.
        // La vitesse de référence est mesurée sur le plat ; une descente de 5 % va environ 19 % plus vite.
        Assert.Equal(1, Marche.FacteurPente(0, 800), 6);
        Assert.Equal(Math.Exp(0.175), Marche.FacteurPente(-40, 800), 6);
        Assert.True(Marche.FacteurPente(160, 800) < 0.55);
        Assert.True(Marche.FacteurPente(-400, 800) < Marche.FacteurPente(-40, 800));
        Assert.True(Marche.FacteurTerrain(Biome.Marais) < Marche.FacteurTerrain(Biome.PlaineDeFougeres));
        Assert.True(Marche.FacteurTerrain(Biome.Riviere) < Marche.FacteurTerrain(Biome.ForetDeConiferes));
    }

    [Fact]
    public void Humains_NeFontPasPlusDeDeuxCasesParPas_EtAvancentAVitesseHumaine()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var avant = monde.Etat.Humains!.ToDictionary(h => h.Id, h => (h.X, h.Y));
        var parcouru = new Dictionary<int, double>();
        for (var pas = 0; pas < 144; pas++)
        {
            monde.AvancerDe(MondeSimule.SecondesParPas);
            foreach (var h in monde.Etat.Humains!)
            {
                if (!avant.TryGetValue(h.Id, out var p)) continue;
                Assert.True(Math.Max(Math.Abs(h.X - p.X), Math.Abs(h.Y - p.Y)) <= 2, $"{h.Nom} a sauté de {p} à ({h.X}, {h.Y})");
                parcouru[h.Id] = parcouru.GetValueOrDefault(h.Id) + Echelle.Kilometres(p.X, p.Y, h.X, h.Y);
                avant[h.Id] = (h.X, h.Y);
            }
        }
        // En une journée, personne ne marche plus de 24 h à 7 km/h ; et les humains bougent (ils cherchent à boire, à manger).
        Assert.All(parcouru.Values, km => Assert.True(km < 24 * 7));
        Assert.True(parcouru.Values.Average() is > 0.5 and < 60, $"moyenne {parcouru.Values.Average():0.0} km");
    }

    [Fact]
    public void Geographie_MesureLeContinentEnKilometres()
    {
        var g = Geographie.Calculer(Carte);
        Assert.Equal(204.8, g.LargeurCarteKm, 6);
        Assert.Equal(204.8 * 204.8, g.SurfaceCarteKm2, 3);
        Assert.InRange(g.ContinentKm2, 2_000, g.SurfaceCarteKm2);
        Assert.Equal(g.ContinentKm2, g.TerresKm2 + g.LacsKm2, 6);
        Assert.InRange(g.EtendueEstOuestKm, 20, 204.8);
        Assert.InRange(g.EtendueNordSudKm, 20, 204.8);
        // La plus grande longueur dépasse chaque étendue et ne dépasse pas la diagonale de la carte.
        Assert.True(g.PlusGrandeLongueurKm >= Math.Max(g.EtendueEstOuestKm, g.EtendueNordSudKm) - 2);
        Assert.True(g.PlusGrandeLongueurKm <= 204.8 * Math.Sqrt(2));
        Assert.True(g.CotesKm > g.PlusGrandeLongueurKm);
        Assert.InRange(g.AltitudeMaxM, 1, 127 * 40);
        Assert.Equal(g.SurfaceCarteKm2, g.Biomes.Sum(b => b.Km2), 3);
        Assert.Equal(g.TerresKm2, g.Altitudes.Sum(a => a.Km2), 3);
        Assert.Equal(4, g.Temperatures.Count);
        var ete = g.Temperatures.Single(t => t.Saison == Saison.Ete);
        var hiver = g.Temperatures.Single(t => t.Saison == Saison.Hiver);
        Assert.True(ete.Max > hiver.Max && ete.Min > hiver.Min);
        Assert.True(g.PlusLoinDeLaMerKm > 0);

        // Traverser le continent : plus long qu'à vol d'oiseau, et plus long à marcher qu'en terrain plat.
        var t = g.Traversee!;
        Assert.True(t.Kilometres >= g.PlusGrandeLongueurKm - 1);
        Assert.True(t.Heures > t.Kilometres / (Marche.VitesseAdulte * 3.6));
        Assert.Equal(g.LongueurDe, t.Cases[0]);
        Assert.Equal(g.LongueurA, t.Cases[^1]);
    }

    [Fact]
    public void Trajet_EvitLEauEtSuitLeTerrain()
    {
        var terre = Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).Where(i => Climat.EstPraticable((Biome)Carte.Biomes[i])).ToList();
        var a = terre[terre.Count / 3];
        var b = terre[2 * terre.Count / 3];
        var t = Geographie.CalculerTrajet(Carte, a, b);
        Assert.NotNull(t);
        Assert.All(t!.Cases, i => Assert.True(Climat.EstPraticable((Biome)Carte.Biomes[i])));
        // Chaque case du trajet touche la suivante.
        for (var k = 1; k < t.Cases.Count; k++)
        {
            int dx = Math.Abs(t.Cases[k] % Carte.Largeur - t.Cases[k - 1] % Carte.Largeur), dy = Math.Abs(t.Cases[k] / Carte.Largeur - t.Cases[k - 1] / Carte.Largeur);
            Assert.True(Math.Max(dx, dy) == 1);
        }
        var vol = Echelle.Kilometres(a % Carte.Largeur, a / Carte.Largeur, b % Carte.Largeur, b / Carte.Largeur);
        Assert.True(t.Kilometres >= vol - 0.01);
        Assert.Equal(0, Geographie.CalculerTrajet(Carte, a, a)!.Kilometres);

        var eau = Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).First(i => !Climat.EstPraticable((Biome)Carte.Biomes[i]));
        Assert.Null(Geographie.CalculerTrajet(Carte, a, eau));
        Assert.Null(Geographie.CalculerTrajet(Carte, a, -1));
    }

    [Fact]
    public void AllureEtProgression_SontEnregistrees_EtUnFichierVersion11EstMigre()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-v11-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fichier = Path.Combine(dossier, "monde.db");
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(Jour / 3);
            var depot = new DepotMonde(fichier);
            depot.Enregistrer(monde.Etat);
            var relu = depot.Charger()!;
            Assert.Equal(monde.Etat.Humains!.Select(h => (h.Biologie.Marche, h.Progression)), relu.Humains!.Select(h => (h.Biologie.Marche, h.Progression)));
            Assert.Contains(relu.Humains!, h => h.Progression > 0);

            // Le monde rechargé continue exactement comme l'original.
            var recharge = new MondeSimule(Carte, relu);
            monde.AvancerDe(Jour);
            recharge.AvancerDe(Jour);
            Assert.Equal(monde.Etat.Humains!.Select(h => (h.X, h.Y, h.Progression)), recharge.Etat.Humains!.Select(h => (h.X, h.Y, h.Progression)));

            // Un monde d'avant les distances : chacun reçoit une allure tirée de son numéro, la même à chaque lecture.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion11);
            var migre = new DepotMonde(fichier).Charger()!;
            Assert.All(migre.Humains!, h => Assert.Equal(Biologie.MarcheDe(h.Id), h.Biologie.Marche));
            Assert.All(migre.Humains!, h => Assert.Equal(0, h.Progression));
            Assert.True(migre.Humains!.Select(h => h.Biologie.Marche).Distinct().Count() > 30);
            new MondeSimule(Carte, migre).AvancerDe(Jour / 4);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Hub_CaracteristiquesEtTrajet()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var c = await connexion.InvokeAsync<CaracteristiquesDto>("Caracteristiques");
        Assert.Equal(0.8, c.KilometresParCase);
        string[] titres = ["Échelle", "Continent", "Relief", "Eaux", "Terrains", "Population", "Marche"];
        Assert.All(titres, t => Assert.Contains(c.Rubriques, r => r.Titre == t));
        var continent = c.Rubriques.Single(r => r.Titre == "Continent");
        Assert.EndsWith("km²", continent.Mesures.Single(m => m.Libelle == "Surface").Valeur);
        Assert.NotEmpty(c.Traversee);

        var humains = await connexion.InvokeAsync<List<HumainDto>>("Humains");
        var (a, b) = (humains[0], humains[^1]);
        var trajet = await connexion.InvokeAsync<TrajetDto?>("Trajet", a.X, a.Y, b.X, b.Y);
        Assert.NotNull(trajet);
        Assert.True(trajet!.Kilometres >= trajet.VolDOiseau - 0.01);
        Assert.Contains("à pied", trajet.Texte);

        // Entrées hostiles : hors de la carte.
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<TrajetDto?>("Trajet", -1, 0, 5, 5));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<TrajetDto?>("Trajet", 0, 0, 5, int.MaxValue));
    }
}
