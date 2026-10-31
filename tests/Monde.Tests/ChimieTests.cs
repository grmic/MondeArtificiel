using Monde.Simulation.Chimie;
using Monde.Simulation.Humains;
using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.32.0 : la matière et la chaleur. Les lois donnent les ordres de grandeur mesurés, sans recette écrite d'avance.</summary>
public sealed class ChimieTests
{
    private static readonly Element[] Volatils = [Element.H, Element.C, Element.N, Element.O, Element.S, Element.Cl];

    /// <summary>Une terre à pot ordinaire : kaolinite et illite, du quartz, un peu de fer, mouillée.</summary>
    private static Substance Terre(double eau, double sable = 0) => Substance.De(Structure.Pate, 0.3, 1,
        (Phase.Kaolinite, 0.3 * (1 - sable)), (Phase.Illite, 0.15 * (1 - sable)), (Phase.Quartz, 0.25 * (1 - sable) + sable), (Phase.Feldspath, 0.05),
        (Phase.Goethite, 0.03), (Phase.Eau, eau));

    [Fact]
    public void Reactions_GardentLesMetaux()
    {
        // Ce qui ne s'envole pas (calcium, silicium, aluminium, fer, cuivre…) se retrouve tout entier dans les produits.
        foreach (var r in Reactions.Toutes.Where(r => !Phases.De(r.Source).Organique && r.Source != Phase.Eau))
        {
            var source = Phases.De(r.Source);
            foreach (var e in Enum.GetValues<Element>().Except(Volatils))
            {
                var avant = source.Part(e);
                var apres = r.Produits.Sum(p => p.Rendement * Phases.De(p.P).Part(e));
                Assert.True(Math.Abs(avant - apres) < 0.01, $"réaction {r.Id} ({r.Source}) : {e} {avant:F3} → {apres:F3}");
            }
        }
    }

    [Fact]
    public void PouvoirCalorifique_OrdresDeGrandeur()
    {
        // Bois anhydre : 17 à 20 MJ/kg ; charbon de bois : 28 à 33 ; le bois vert brûle mal (Rowell, 2005 ; Antal et Grønli, 2003).
        Assert.InRange(Thermique.BoisSec.PouvoirCalorifique, 16.5, 20);
        Assert.InRange(Thermique.Charbon.PouvoirCalorifique, 28, 33);
        Assert.True(Thermique.Bois(0.45).PouvoirCalorifique < 0.6 * Thermique.BoisSec.PouvoirCalorifique);
        Assert.True(Thermique.Charbon.BrulesSansFumee);
        Assert.False(Thermique.BoisSec.BrulesSansFumee);
    }

    [Fact]
    public void Foyers_OrdresDeGrandeur()
    {
        // Feu ouvert : 600-800 °C ; fosse : 700-900 ; meule couverte : 400-600 ; charbon soufflé en enceinte : plus de 1 200.
        Assert.InRange(Thermique.Temperature(FormeFoyer.Ouvert, 0.15, 0, 1, false, 20, 3), 600, 800);
        Assert.InRange(Thermique.Temperature(FormeFoyer.Fosse, 0.15, 0, 1, false, 20, 3), 750, 950);
        Assert.InRange(Thermique.Temperature(FormeFoyer.Couvert, 0.15, 0, 0, false, 20, 3), 400, 650);
        Assert.True(Thermique.Temperature(FormeFoyer.Enceinte, 0.15, 1, 1, true, 20, 3) > 1200);
        // Le bois mouillé chauffe moins, souffler attise.
        Assert.True(Thermique.Temperature(FormeFoyer.Ouvert, 0.4, 0, 1, false, 20, 3) < Thermique.Temperature(FormeFoyer.Ouvert, 0.15, 0, 1, false, 20, 3));
        Assert.True(Thermique.Temperature(FormeFoyer.Ouvert, 0.15, 0, 1, true, 20, 3) > Thermique.Temperature(FormeFoyer.Ouvert, 0.15, 0, 1, false, 20, 3));
    }

    [Fact]
    public void Terre_SecheeCuiteDevientCeramique()
    {
        // Une terre modelée, séchée quelques jours puis cuite doucement vers 700 °C : elle durcit et ne se défait plus dans l'eau.
        var pot = Reactions.Secher(Terre(0.22, 0.2) with { Forme = Forme.Creux | Forme.Rond }, 6, 0.8);
        Assert.Equal(Structure.Crue, pot.Structure);
        Assert.True(pot.SeDelite);
        var cuits = 0;
        var alea = new Alea(5);
        for (var i = 0; i < 40; i++)
        {
            var t = Reactions.Chauffer(pot, new Chauffe(720, 2, 4, Atmosphere.Oxydante), alea);
            if (t.Eclate) continue;
            cuits++;
            Assert.Equal(Structure.Cuite, t.Produit.Structure);
            Assert.False(t.Produit.SeDelite);
            Assert.True(t.Produit.TientLEau);
            Assert.Contains("eau", Perception.Changements(pot, t));
            Assert.Equal(Teinte.Rouge, t.Produit.Teinte);
        }
        Assert.True(cuits >= 30, $"{cuits} pots sur 40 ont tenu");
    }

    [Fact]
    public void Terre_MouilleeJeteeAuFeu_EclateSouvent()
    {
        // La vapeur d'une pâte encore mouillée la fait éclater, d'autant plus que la chauffe est brutale.
        var alea = new Alea(9);
        var brutale = Enumerable.Range(0, 200).Count(_ => Reactions.Chauffer(Terre(0.22), new Chauffe(700, 1, 15, Atmosphere.Oxydante), alea).Eclate);
        var douce = Enumerable.Range(0, 200).Count(_ => Reactions.Chauffer(Reactions.Secher(Terre(0.22, 0.2), 6, 0.8), new Chauffe(700, 1, 2, Atmosphere.Oxydante), alea).Eclate);
        Assert.True(brutale > 120, $"{brutale} sur 200");
        Assert.True(douce < brutale / 3, $"{douce} contre {brutale}");
    }

    [Fact]
    public void Calcaire_DonneLaChaux_QuiChauffeDansLEau()
    {
        var calcaire = Substance.De(Structure.Roche, 0.05, 1, (Phase.Calcite, 0.95), (Phase.Quartz, 0.05));
        var cuite = Reactions.Chauffer(calcaire, new Chauffe(900, 6, 4, Atmosphere.Oxydante), new Alea(1)).Produit;
        Assert.True(cuite.Part(Phase.Chaux) > 0.6);
        // Elle a perdu son gaz carbonique : 44 % de la masse de la calcite (CaCO3 → CaO + CO2).
        Assert.InRange(cuite.Masse, 0.55, 0.85);
        Assert.Equal("chaux", Perception.Famille(cuite));
        var eau = Substance.De(Structure.Grains, 0, 0.5, (Phase.Eau, 1));
        var eteinte = Reactions.Melanger(cuite, eau, 0);
        Assert.True(eteinte.Exotherme);
        Assert.True(eteinte.Produit.Part(Phase.Portlandite) > 0.3);
        // Sous 650 °C, le calcaire reste ce qu'il est.
        Assert.True(Reactions.Chauffer(calcaire, new Chauffe(600, 6, 4, Atmosphere.Oxydante), new Alea(1)).Produit.Part(Phase.Chaux) < 0.01);
    }

    [Fact]
    public void Bois_SansAir_DevientCharbon_EtBruleALAir()
    {
        var bois = Thermique.Bois(0.15) with { Masse = 5 };
        var meule = Reactions.Chauffer(bois, new Chauffe(500, 8, 2, Atmosphere.Reductrice), new Alea(1));
        Assert.False(meule.Brulee);
        Assert.True(meule.Produit.BrulesSansFumee);
        // 25 à 35 % de la masse du bois sec (Antal et Grønli, 2003).
        Assert.InRange(meule.Produit.Masse / (5 * 0.85), 0.2, 0.4);
        Assert.Contains("braise", Perception.Changements(bois, meule));
        Assert.True(Reactions.Chauffer(bois, new Chauffe(700, 3, 15, Atmosphere.Oxydante), new Alea(1)).Brulee);
    }

    [Fact]
    public void Ecorce_SansAir_DonneUnePoix()
    {
        // L'écorce de bouleau sous la braise donne un brai collant (Schmidt et al., 2019).
        var ecorce = Substance.De(Structure.Fibreuse, 0.5, 0.3, (Phase.Lignine, 0.34), (Phase.Cellulose, 0.2), (Phase.Hemicellulose, 0.1),
            (Phase.Terpenes, 0.25), (Phase.Eau, 0.1), (Phase.Potasse, 0.01));
        var t = Reactions.Chauffer(ecorce, new Chauffe(380, 3, 2, Atmosphere.Reductrice), new Alea(1));
        Assert.True(t.Produit.Collante);
        Assert.Contains("colle", Perception.Changements(ecorce, t));
    }

    [Fact]
    public void Silex_ChauffeLentement_SeTailleMieux()
    {
        var silex = Substance.De(Structure.Roche, 0.01, 0.5, (Phase.Calcedoine, 0.97), (Phase.Eau, 0.01), (Phase.Goethite, 0.02));
        var lent = Reactions.Chauffer(silex, new Chauffe(320, 4, 2, Atmosphere.Reductrice), new Alea(1)).Produit;
        var vite = Reactions.Chauffer(silex, new Chauffe(320, 4, 15, Atmosphere.Oxydante), new Alea(1)).Produit;
        Assert.True(lent.Part(Phase.CalcedoineChauffee) > 0.5);
        Assert.True(vite.Part(Phase.CalcedoineChauffee) < 0.01);
    }

    [Fact]
    public void Cuivre_MarteleDurcit_EtLeFeuLeRendDoux()
    {
        var cuivre = Substance.De(Structure.Metal, 0, 0.2, (Phase.Cuivre, 0.99), (Phase.Tenorite, 0.01));
        Assert.True(cuivre.Malleable);
        var martele = Reactions.Marteler(cuivre, 0.7, 0.8, new Alea(1)).Produit;
        for (var i = 0; i < 4; i++) martele = Reactions.Marteler(martele, 0.7, 0.8, new Alea((ulong)i)).Produit;
        Assert.True(martele.Ecrouissage > 0.3);
        Assert.True(martele.Durete > cuivre.Durete);
        var recuit = Reactions.Chauffer(martele, new Chauffe(550, 0.5, 15, Atmosphere.Oxydante), new Alea(1));
        Assert.True(recuit.Produit.Ecrouissage < 0.1 * martele.Ecrouissage);
        Assert.Contains("doux", Perception.Changements(martele, recuit));
    }

    [Fact]
    public void Ocre_JauneRougitAuFeu()
    {
        var ocre = Substance.De(Structure.Grains, 0.4, 0.1, (Phase.Goethite, 0.5), (Phase.Kaolinite, 0.2), (Phase.Quartz, 0.3));
        var t = Reactions.Chauffer(ocre, new Chauffe(450, 1, 15, Atmosphere.Oxydante), new Alea(1));
        Assert.Equal(Teinte.Jaune, ocre.Teinte);
        Assert.Equal(Teinte.Rouge, t.Produit.Teinte);
        Assert.Contains("t4", Perception.Changements(ocre, t));
    }

    [Fact]
    public void Signature_SeRelitALIdentique()
    {
        var pot = Reactions.Secher(Terre(0.22, 0.2) with { Forme = Forme.Creux | Forme.Rond, FaconneLe = 12345 }, 3, 0.5).Arrondie;
        Assert.Equal(pot.Signature, Substance.Lire(pot.Signature)!.Signature);
        Assert.Null(Substance.Lire("A|n'importe quoi"));
    }

    [Fact]
    public void AnciensMondes_NeVoientPasLesNouvellesMatieres()
    {
        var carte = MondeSimuleTests.Carte42;
        var neuves = new[] { Matiere.Calcaire, Matiere.Sable, Matiere.Coquillage, Matiere.Ecorce, Matiere.Minerai, Matiere.Substance };
        for (var x = 0; x < carte.Largeur; x += 7)
        for (var y = 0; y < carte.Hauteur; y += 7)
        {
            Assert.DoesNotContain(Physique.Disponibles(carte, x, y, true, false, 0), m => neuves.Contains(m));
            Assert.DoesNotContain(Physique.Disponibles(carte, x, y, true, false, 1), m => neuves.Contains(m));
        }
    }

    private static List<Humain> Derouler(int generation, int jours)
    {
        var config = ConfigurationMonde.ParDefaut(7) with { Generation = generation };
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config));
        monde.AvancerDe(jours * Calendrier.SecondesParJour);
        return [.. monde.InstantaneHumains().Humains];
    }

    [Fact]
    public void MondeNeuf_ModeleEtTransformeLaMatiere()
    {
        // En trois ans (deux ne suffisent pas toujours, selon le hasard des premiers essais), des humains ont pétri la terre et changé une matière, et ce qu'ils portent se relit.
        var humains = Derouler(2, 1080);
        Assert.Contains(humains, h => h.Savoirs.Exists(s => s.Effet == Effet.Modelage));
        Assert.Contains(humains, h => h.Savoirs.Exists(s => s.Effet == Effet.Transformation && Comportement.LireRecette(s.Pieces) is not null));
        var choses = humains.SelectMany(h => h.Objets).Where(o => o.Matiere == Matiere.Substance).ToList();
        Assert.NotEmpty(choses);
        Assert.All(choses, o => Assert.NotNull(Substance.Lire(o.Pieces)));
    }

    [Fact]
    public void MondeDeGeneration1_NeConnaitPasLaChimie()
    {
        var humains = Derouler(1, 360);
        Assert.DoesNotContain(humains, h => h.Savoirs.Exists(s => s.Effet is Effet.Modelage or Effet.Transformation));
        Assert.DoesNotContain(humains, h => h.Objets.Exists(o => o.Matiere is Matiere.Substance or Matiere.Minerai));
    }

    [Fact]
    public void Foyer_GardeSonCharbon_EtLaVersion40SeRelit()
    {
        var etat = MondeSimule.Nouveau(42) with { Foyers = [new Foyer(1, 3, 4, 5, 0, 1, 0, Charbon: 0.4)] };
        var dossier = Path.Combine(Path.GetTempPath(), "monde-charbon-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            Assert.Equal(0.4, Assert.Single(new DepotMonde(fichier).Charger()!.Foyers!).Charbon, 6);
            // Une base de la version 40 n'a pas la colonne : ses feux n'ont pas de charbon.
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fichier};Pooling=False"))
            {
                c.Open();
                using var commande = c.CreateCommand();
                commande.CommandText = "ALTER TABLE foyers DROP COLUMN charbon; PRAGMA user_version = 40;";
                commande.ExecuteNonQuery();
            }
            Assert.Equal(0, Assert.Single(new DepotMonde(fichier).Charger()!.Foyers!).Charbon);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
