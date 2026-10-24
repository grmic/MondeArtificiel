using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.31.3 : forêt de feuillus, mangrove et lande sèche, et mer entre les continents, pour les mondes neufs seulement.</summary>
public sealed class TerrainsNeufsTests
{
    private static readonly Biome[] Neufs = [Biome.ForetDeFeuillus, Biome.Mangrove, Biome.Lande];

    [Fact]
    public void MondeAncien_GardeSaCarte()
    {
        // Génération 0 : aucun des nouveaux terrains, et la même carte qu'avant (même graine, mêmes réglages).
        var ancienne = MondeSimuleTests.Carte42;
        Assert.DoesNotContain(ancienne.Biomes, b => b >= (byte)Biome.ForetDeFeuillus);
        Assert.Equal(ConfigurationMonde.ParDefaut(42), new MondeSimule(ancienne, MondeSimule.Nouveau(42)).Configuration);
        Assert.True(ConfigurationMonde.ParDefaut(42).EstOrigine);
        Assert.True(ConfigurationMonde.Nouvelle(42).EstOrigine);
        Assert.Equal(EcartContinents.Proches, ConfigurationMonde.Nouvelle(42).Ecart);
    }

    [Fact]
    public void MondeNeuf_PorteLesNouveauxTerrains()
    {
        var presents = new HashSet<Biome>();
        foreach (var graine in new ulong[] { 1, 2, 3, 42 })
        {
            var neuve = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(graine));
            var ancienne = GenerateurContinent.Generer(ConfigurationMonde.ParDefaut(graine));
            // Seuls les terrains changent : le relief et les côtes restent ceux de la même graine.
            Assert.Equal(ancienne.Altitudes, neuve.Altitudes);
            foreach (var b in neuve.Biomes) presents.Add((Biome)b);
            for (var i = 0; i < neuve.Biomes.Length; i++)
            {
                var b = (Biome)neuve.Biomes[i];
                if (b == Biome.Mangrove) Assert.True(Biomes.EstEauSalee((Biome)ancienne.Biomes[i]) || ancienne.Biomes[i] is (byte)Biome.Plage or (byte)Biome.Marais);
                if (Neufs.Contains(b)) Assert.InRange(neuve.Plantes[i], 1, 255);
            }
            Assert.All(neuve.Emplacements, e => Assert.True(Biomes.EstHabitable(neuve.BiomeEn(e.X, e.Y))));
        }
        Assert.All(Neufs, b => Assert.Contains(b, presents));
    }

    [Fact]
    public void NouveauxTerrains_OntLeursRegles()
    {
        // Mangrove : pire qu'un marais pour marcher ; feuillus : un abri, comme les autres forêts ; lande : brûle vite.
        Assert.True(Marche.FacteurTerrain(Biome.Mangrove) < Marche.FacteurTerrain(Biome.Marais));
        Assert.True(Climat.Abri(Biome.ForetDeFeuillus) >= 0.5);
        Assert.True(Biomes.EstForet(Biome.ForetDeFeuillus));
        Assert.True(Ecosysteme.AnneesDeRetour(Biome.ForetDeFeuillus) > Ecosysteme.AnneesDeRetour(Biome.Lande));
        var carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(1));
        var t = carte.Terrain;
        var m = carte.Mer;
        for (var i = 0; i < carte.Biomes.Length; i++)
        {
            switch ((Biome)carte.Biomes[i])
            {
                case Biome.Mangrove:
                    Assert.Equal(Roche.Alluvions, t.Roches[i]);
                    Assert.Equal(Sol.Tourbeux, t.Sols[i]);
                    if (m.Rivages[i] != Rivage.Aucun) Assert.Equal(Rivage.Mangrove, m.Rivages[i]);
                    break;
                case Biome.Lande:
                    Assert.NotEqual(Sol.Aucun, t.Sols[i]);
                    break;
            }
        }
    }

    [Fact]
    public void Flore_LesEspecesAnciennesNeChangentPas()
    {
        var ancienne = new Flore(7);
        var neuve = new Flore(7, nouveauxTerrains: true);
        static object Cle(Espece e) => (e.Numero, e.Nom, e.Biomes[0], e.Toxicite, e.Febrifuge, e.Antipaludique, e.Psychotrope, e.Forme, e.Trait);
        Assert.Equal(ancienne.Especes.Select(Cle), neuve.Especes.Take(ancienne.Especes.Count).Select(Cle));
        Assert.All(Neufs, b => Assert.NotEmpty(neuve.Sur(b)));
        Assert.All(Neufs, b => Assert.Empty(ancienne.Sur(b)));
    }

    [Fact]
    public void ContinentsEloignes_PlusDe200KmDeMer()
    {
        var c = ConfigurationMonde.Nouvelle(5) with { Largeur = 1024, Hauteur = 1024, Continents = 3, Ecart = EcartContinents.Eloignes };
        var m = GenerateurContinent.Generer(c).Mer;
        Assert.Equal(3, m.Continents.Count);
        // Aucun passage noté entre deux continents à moins de 200 km (au-delà de 300, il n'y a plus de passage du tout).
        Assert.All(m.Passages.Where(p => m.Continents.Contains(p.MasseA) && m.Continents.Contains(p.MasseB)), p => Assert.True(p.Km >= 200, $"{p.Km} km"));

        var proches = GenerateurContinent.Generer(c with { Ecart = EcartContinents.Proches }).Mer;
        Assert.Contains(proches.Passages, p => proches.Continents.Contains(p.MasseA) && proches.Continents.Contains(p.MasseB) && p.Km < 120);
    }

    [Fact]
    public void ContinentsEloignes_SurUneCarteTropPetite_Refuses()
    {
        var c = ConfigurationMonde.Nouvelle(5) with { Largeur = 256, Hauteur = 256, Continents = 6, Ecart = EcartContinents.Eloignes, Couples = 5 };
        Assert.Throws<CarteTropPetitePourEloignes>(() => GenerateurContinent.Generer(c));
    }

    [Fact]
    public void ContinentsMelanges_TaillesVariees()
    {
        var c = ConfigurationMonde.Nouvelle(11) with { Largeur = 768, Hauteur = 512, Continents = 4, Ecart = EcartContinents.Melanges };
        var carte = GenerateurContinent.Generer(c);
        Assert.Equal(4, carte.Mer.Continents.Count);
        Assert.NotEqual(carte.Biomes, GenerateurContinent.Generer(c with { Ecart = EcartContinents.Proches }).Biomes);
    }

    [Fact]
    public void GenerationEtEcart_SontSauvegardes()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-neuf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var c = ConfigurationMonde.Nouvelle(9) with { Largeur = 600, Hauteur = 400, Continents = 2, Couples = 10, Ecart = EcartContinents.Eloignes };
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(MondeSimule.Nouveau(c));
            Assert.Equal(c, new DepotMonde(fichier).Charger()!.Configuration);
            // Une génération inconnue (monde d'une version plus récente) est refusée.
            Assert.Equal(ProblemeConfiguration.Generation, (c with { Generation = ConfigurationMonde.GenerationActuelle + 1 }).Probleme());
            Assert.Equal(ProblemeConfiguration.Generation, (c with { Ecart = (EcartContinents)9 }).Probleme());
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
