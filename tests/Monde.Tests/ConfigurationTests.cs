using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

public sealed class ConfigurationTests
{
    private const long Jour = Calendrier.SecondesParJour;

    [Fact]
    public void ReglagesDOrigine_RedonnentExactementLaMemeCarte()
    {
        var defaut = ConfigurationMonde.ParDefaut(42);
        Assert.Equal(new ConfigurationMonde(42, 33, 256, 256, 1), defaut);
        Assert.Null(defaut.Erreur());
        Assert.Equal(MondeSimuleTests.Carte42.Empreinte(), GenerateurContinent.Generer(defaut).Empreinte());
    }

    [Theory]
    [InlineData(0, 256, 256, 1)]
    [InlineData(201, 256, 256, 1)]
    [InlineData(33, 63, 256, 1)]
    [InlineData(33, 256, 1025, 1)]
    [InlineData(33, 256, 256, 0)]
    [InlineData(33, 256, 256, 7)]
    public void ReglagesHorsLimites_SontRefuses(int couples, int largeur, int hauteur, int continents)
    {
        var c = new ConfigurationMonde(1, couples, largeur, hauteur, continents);
        Assert.NotNull(c.Erreur());
        Assert.Throws<ArgumentOutOfRangeException>(() => GenerateurContinent.Generer(c));
    }

    [Theory]
    [InlineData(42UL, 2)]
    [InlineData(42UL, 3)]
    [InlineData(7UL, 4)]
    [InlineData(1UL, 5)]
    [InlineData(2UL, 6)]
    public void PlusieursContinents_SeparesParLaMer_LesCouplesSurLePlusGrand(ulong graine, int continents)
    {
        var c = new ConfigurationMonde(graine, 20, 384, 384, continents);
        var carte = GenerateurContinent.Generer(c);
        var (masses, _) = Geographie.MassesDeTerre(carte);

        Assert.Equal(continents, masses.Count);
        Assert.Equal(20, carte.Emplacements.Count);
        // Tous les emplacements sont sur la même terre, la plus grande : le trajet à pied existe de l'un à l'autre.
        var premier = carte.Emplacements[0];
        foreach (var e in carte.Emplacements.Skip(1).Take(5))
        {
            Assert.NotNull(Geographie.CalculerTrajet(carte, carte.Indice(premier.X, premier.Y), carte.Indice(e.X, e.Y)));
        }
        var g = Geographie.Calculer(carte);
        Assert.Equal(continents - 1, g.AutresContinentsKm2.Count);
        Assert.All(g.AutresContinentsKm2, km2 => Assert.True(km2 <= g.ContinentKm2));
        // Même graine et mêmes réglages : même monde.
        Assert.Equal(carte.Empreinte(), GenerateurContinent.Generer(c).Empreinte());
    }

    [Fact]
    public void TailleEtCouples_SontRespectes_OuRefusesSiLaPlaceManque()
    {
        var petite = GenerateurContinent.Generer(new ConfigurationMonde(42, 5, 64, 80, 1));
        Assert.Equal((64, 80, 5), (petite.Largeur, petite.Hauteur, petite.Emplacements.Count));
        Assert.Equal(Enumerable.Range(1, 5), petite.Emplacements.Select(e => e.Numero));

        var nombreux = GenerateurContinent.Generer(new ConfigurationMonde(42, 150, 512, 384, 1));
        Assert.Equal(150, nombreux.Emplacements.Select(e => (e.X, e.Y)).Distinct().Count());

        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurContinent.Generer(new ConfigurationMonde(42, 200, 64, 64, 6)));
        Assert.Contains("200 couples", ex.Message);
    }

    [Theory]
    [InlineData(300, 250)]
    [InlineData(96, 512)]
    public void CarteRectangulaire_GeologieEtMondeSansErreur(int largeur, int hauteur)
    {
        // Les côtes actives se cherchent le long du bon côté de la carte, même quand elle n'est pas carrée.
        var contextes = new HashSet<ContexteTectonique>();
        for (ulong graine = 1; graine <= 8; graine++)
        {
            var c = new ConfigurationMonde(graine, 5, largeur, hauteur, 1);
            var carte = GenerateurContinent.Generer(c);
            var geologie = Geologie.Generer(carte);
            contextes.Add(geologie.Contexte);
            Assert.All(geologie.Failles, f => Assert.True(f.X1 < largeur && f.X2 < largeur && f.Y1 < hauteur && f.Y2 < hauteur));
            _ = new MondeSimule(carte, MondeSimule.Nouveau(c));
        }
        Assert.Contains(ContexteTectonique.MargeActive, contextes);
    }

    [Fact]
    public void MondeConfigure_VitEtSeSauvegardeAvecSesReglages()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            var c = new ConfigurationMonde(9, 12, 200, 160, 3);
            var monde = new MondeSimule(GenerateurContinent.Generer(c), MondeSimule.Nouveau(c));
            Assert.Equal(24, monde.Etat.Humains!.Count);
            monde.AvancerDe(3 * Jour);
            Assert.Contains(monde.Etat.Humains!, h => h.EstVivant);

            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(monde.Etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(c, relu.Configuration);

            // Le monde rechargé, sur la carte régénérée depuis ses réglages, continue exactement comme l'original.
            var recharge = new MondeSimule(GenerateurContinent.Generer(relu.Configuration!), relu);
            monde.AvancerDe(Jour);
            recharge.AvancerDe(Jour);
            Assert.Equal(monde.Etat.Humains!.Select(h => (h.X, h.Y, h.Sante)), recharge.Etat.Humains!.Select(h => (h.X, h.Y, h.Sante)));

            // Une carte qui ne correspond pas aux réglages est refusée.
            Assert.Throws<ArgumentException>(() => new MondeSimule(GenerateurContinent.Generer(9), relu));

            // Un monde d'avant la configuration garde les réglages d'origine.
            new DepotMonde(fichier).Enregistrer(MondeSimule.Nouveau(42) with { Configuration = null }, remplacer: true);
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion12);
            var ancien = new DepotMonde(fichier).Charger()!;
            Assert.Null(ancien.Configuration);
            Assert.Equal(ConfigurationMonde.ParDefaut(42), new MondeSimule(MondeSimuleTests.Carte42, ancien).Configuration);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Hub_CreerMonde_AvecSesReglages_GardesAuRedemarrage()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var usine = new UsineTest(dossier, supprimer: false))
            {
                var connexion = usine.CreerConnexion();
                await using var _ = connexion;
                await connexion.StartAsync();

                var actuelle = await connexion.InvokeAsync<ConfigurationDto>("Configuration");
                Assert.Equal((42u, 33, 256, 256, 1, true), (actuelle.Graine, actuelle.Couples, actuelle.Largeur, actuelle.Hauteur, actuelle.Continents, actuelle.Origine));
                Assert.Contains("1 continent", actuelle.Resume);
                Assert.Equal(("0.33.1", "MicG"), (actuelle.Version, actuelle.Auteur));

                // Aperçu : possible ou non, avec la raison ; le monde courant ne change pas.
                var apercu = await connexion.InvokeAsync<ApercuDto>("ApercuConfiguration", new DemandeConfiguration(5, 10, 300, 250, 2));
                Assert.True(apercu.Possible, apercu.Texte);
                Assert.Contains("2 continents", apercu.Texte);
                var impossible = await connexion.InvokeAsync<ApercuDto>("ApercuConfiguration", new DemandeConfiguration(5, 200, 64, 64, 6));
                Assert.False(impossible.Possible);
                var horsLimites = await connexion.InvokeAsync<ApercuDto>("ApercuConfiguration", new DemandeConfiguration(5, 10, 300, 250, 9));
                Assert.False(horsLimites.Possible);
                Assert.Contains("continents", horsLimites.Texte);

                // Des réglages impossibles sont refusés sans toucher au monde.
                var refus = await Assert.ThrowsAsync<HubException>(() =>
                    connexion.InvokeAsync<HorlogeDto>("CreerMonde", new DemandeConfiguration(5, 200, 64, 64, 6)));
                Assert.Contains("200 couples", refus.Message);
                await Assert.ThrowsAsync<HubException>(() =>
                    connexion.InvokeAsync<HorlogeDto>("CreerMonde", new DemandeConfiguration(-1, 10, 300, 250, 2)));
                Assert.Equal(66, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);

                var horloge = await connexion.InvokeAsync<HorlogeDto>("CreerMonde", new DemandeConfiguration(5, 10, 300, 250, 2));
                Assert.Equal("An 1, jour 1 du printemps, 06:00", horloge.Texte);
                Assert.Equal(20, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);

                using var client = usine.CreateClient();
                var carte = await client.GetFromJsonAsync<CarteDto>("/api/carte");
                Assert.Equal((5u, 300, 250, 10), (carte!.Graine, carte.Largeur, carte.Hauteur, carte.Emplacements.Count));
                // Aperçu d'autres réglages par l'API ; refus motivé sinon.
                var autre = await client.GetFromJsonAsync<CarteDto>("/api/carte?graine=8&continents=3&largeur=320");
                Assert.Equal((8u, 320, 250, 10), (autre!.Graine, autre.Largeur, autre.Hauteur, autre.Emplacements.Count));
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/carte?couples=0")).StatusCode);
                var trop = await client.GetAsync("/api/carte?couples=200&largeur=64&hauteur=64&continents=6");
                Assert.Equal(HttpStatusCode.UnprocessableEntity, trop.StatusCode);
                Assert.Contains("200 couples", await trop.Content.ReadAsStringAsync());

                // Une réinitialisation par graine garde les réglages du monde.
                await connexion.InvokeAsync<HorlogeDto>("Reinitialiser", 6L);
                var apres = await connexion.InvokeAsync<ConfigurationDto>("Configuration");
                Assert.Equal((6u, 10, 300, 250, 2, false), (apres.Graine, apres.Couples, apres.Largeur, apres.Hauteur, apres.Continents, apres.Origine));
                var caracteristiques = await connexion.InvokeAsync<CaracteristiquesDto>("Caracteristiques");
                Assert.Contains(caracteristiques.Rubriques, r => r.Titre == "Continent des couples de départ");
            }

            // Au redémarrage du serveur, le monde reprend avec ses réglages, lus dans sa base.
            using (var usine = new UsineTest(dossier, supprimer: false))
            {
                var connexion = usine.CreerConnexion();
                await using var _ = connexion;
                await connexion.StartAsync();
                var reprise = await connexion.InvokeAsync<ConfigurationDto>("Configuration");
                Assert.Equal((6u, 10, 300, 250, 2), (reprise.Graine, reprise.Couples, reprise.Largeur, reprise.Hauteur, reprise.Continents));
                // Un monde créé depuis la 0.31.3 a la carte de la dernière génération, et la garde.
                Assert.Equal((ConfigurationMonde.GenerationActuelle, 0), (reprise.Generation, reprise.Ecart));
                Assert.Equal(20, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);
            }
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
