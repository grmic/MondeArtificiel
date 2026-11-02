using System.Text.Json;
using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.35.3 : les inventions de l'Entité libre (plante, bête, savoir jugé par les lois, mot), gardées et défaites.</summary>
public sealed class InventionsTests
{
    private static MondeSimule Monde(int jours = 30)
    {
        var conf = new ConfigurationMonde(7, 20, 64, 64, 1);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf));
        monde.AvancerDe(jours * Calendrier.SecondesParJour);
        return monde;
    }

    private static Humain H(MondeSimule m, int id) => m.InstantaneHumains().Humains[id - 1];

    private static DefaireLibre Relu(ResultatLibre r) =>
        JsonSerializer.Deserialize<DefaireLibre>(JsonSerializer.Serialize(new DefaireLibre([.. r.Faits], [.. r.Decrets.Select(d => d.Id)]), Actes.Json), Actes.Json)!;

    [Fact]
    public void PlanteEtBete_EntrentDansLaNature_SontGardees_PuisDisparaissent()
    {
        var monde = Monde();
        var a = monde.InstantaneHumains().Humains.First(h => h.EstVivant);
        var biome = monde.Carte.BiomeEn(a.X, a.Y);
        var plantes = monde.Flore.Especes.Count;
        var betes = monde.Faune.Betes.Count;
        var acte = new ActeLibre("Une herbe bleue et une bête douce apparaissent.", false, true, [],
        [
            new ChangementLibre("plante_nouvelle", X: a.X, Y: a.Y, Nom: "l'herbe aux perles bleues", Forme: "herbe", Aliment: "graines",
                Proprietes: new() { ["febrifuge"] = 0.8 }),
            new ChangementLibre("bete_nouvelle", X: a.X, Y: a.Y, Nom: "le brouteur aux yeux d'or", Forme: "brouteur",
                Proprietes: new() { ["agressivite"] = 0, ["lait"] = 0.9, ["captive"] = 1 }),
        ], []);
        var (r, _, _) = monde.Executer(m => m.Libre(acte));
        var plante = monde.Flore.Get(plantes + 1);
        Assert.Equal(0.8, plante.Febrifuge);
        Assert.Equal(SorteAliment.Graines, plante.Aliment!.Sorte);
        Assert.Contains(monde.Flore.Sur(biome), e => e.Numero == plante.Numero);
        Assert.Equal("l'herbe aux perles bleues", plante.Dit);
        var bete = monde.Faune.Get(betes + 1);
        Assert.Equal((0.0, 0.9, true), (bete.Agressivite, bete.Lait, bete.Captive));
        Assert.Contains(monde.Faune.Sur(biome), b => b.Numero == bete.Numero);

        // Gardées avec le monde, et reconstruites à l'identique.
        var dossier = Path.Combine(Path.GetTempPath(), "monde-inventions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(monde.Etat, remplacer: true);
            var lu = depot.Charger()!;
            Assert.Equal(2, lu.Inventions!.Count);
            var repris = new MondeSimule(monde.Carte, lu);
            var relue = repris.Flore.Get(plante.Numero);
            Assert.Equal((plante.Nom, plante.Biomes[0], plante.Febrifuge, plante.Forme, plante.Aliment), (relue.Nom, relue.Biomes[0], relue.Febrifuge, relue.Forme, relue.Aliment));
            Assert.Equal(bete, repris.Faune.Get(bete.Numero));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dossier, true);
        }

        // Défaites : elles ne poussent ni ne vivent plus nulle part, leur numéro reste.
        var d = Relu(r);
        monde.Executer(m => m.Defaire(d.Faits, d.Decrets, r.Annonce));
        Assert.DoesNotContain(monde.Flore.Sur(biome), e => e.Numero == plante.Numero);
        Assert.DoesNotContain(monde.Faune.Sur(biome), b => b.Numero == bete.Numero);
        Assert.Equal(plantes + 1, monde.Flore.Especes.Count);
        Assert.All(monde.InventionsDuMonde(), i => Assert.True(i.Disparue));
    }

    [Fact]
    public void SavoirNouveau_JugeParLesLois()
    {
        var monde = Monde();
        var a = monde.InstantaneHumains().Humains.First(h => h.EstVivant && !h.EstEnfant(monde.Secondes));
        // Tordre un galet ne donne jamais rien : refusé, rien n'est fait.
        var refus = Assert.Throws<ArgumentException>(() => monde.Libre(new ActeLibre("r", false, true, [],
            [new ChangementLibre("savoir_nouveau", a.Id, Geste: "Tordre", A: "Galet")], [])));
        Assert.StartsWith("libre.erreur.lois|", refus.Message);
        // Tordre des fibres donne une corde : il le sait désormais, venu de l'Entité.
        var (r, _, _) = monde.Executer(m => m.Libre(new ActeLibre("Ses doigts savent.", false, true, [],
            [new ChangementLibre("savoir_nouveau", a.Id, Geste: "Tordre", A: "Fibres")], [])));
        Assert.Contains(H(monde, a.Id).Savoirs, s => s.Effet == Effet.Corde && s.Origine == OrigineSavoir.Entite);
        var d = Relu(r);
        monde.Executer(m => m.Defaire(d.Faits, d.Decrets, r.Annonce));
        Assert.DoesNotContain(H(monde, a.Id).Savoirs, s => s.Effet == Effet.Corde && s.Origine == OrigineSavoir.Entite);
    }

    [Fact]
    public void Mot_DonneAUnHumain_PuisRetire()
    {
        var monde = Monde();
        var a = monde.InstantaneHumains().Humains.First(h => h.EstVivant);
        var avant = H(monde, a.Id).Vocabulaire.Count(m => m.Idee == "feu");
        var (r, _, _) = monde.Executer(m => m.Libre(new ActeLibre("Un mot en rêve.", false, true, [],
            [new ChangementLibre("mot", a.Id, Texte: "feu", Nom: "Ka-rou!")], [])));
        Assert.Contains(H(monde, a.Id).Vocabulaire, m => m.Idee == "feu" && m.Forme == "karou");
        Assert.Throws<ArgumentException>(() => monde.Libre(new ActeLibre("r", false, true, [], [new ChangementLibre("mot", a.Id, Texte: "pas-une-idee")], [])));
        var d = Relu(r);
        monde.Executer(m => m.Defaire(d.Faits, d.Decrets, r.Annonce));
        Assert.DoesNotContain(H(monde, a.Id).Vocabulaire, m => m.Forme == "karou");
        Assert.Equal(avant, H(monde, a.Id).Vocabulaire.Count(m => m.Idee == "feu"));
    }

    [Fact]
    public void DefaireUnSavoirDonne_EtUnOubli()
    {
        // Correctif de la 0.35.2 : ces deux changements ne se relisaient pas pour être défaits.
        var monde = Monde(90);
        var humains = monde.InstantaneHumains().Humains;
        var a = humains.First(h => h.EstVivant && h.Memoire.Jardin.Count > 0);
        var connu = humains.SelectMany(h => h.Savoirs).First(s => s.Effet != Effet.Rien && !a.Savoirs.Exists(x => x.Effet == s.Effet)).Effet;
        var chose = a.Memoire.Jardin[0];
        var (r, _, _) = monde.Executer(m => m.Libre(new ActeLibre("r", false, true, [],
        [
            new ChangementLibre("savoir", a.Id, Texte: connu.ToString()),
            new ChangementLibre("oublier", a.Id, Texte: $"{chose.Secondes}|{chose.Cle}"),
        ], [])));
        Assert.Contains(H(monde, a.Id).Savoirs, s => s.Effet == connu);
        var d = Relu(r);
        monde.Executer(m => m.Defaire(d.Faits, d.Decrets, r.Annonce));
        Assert.DoesNotContain(H(monde, a.Id).Savoirs, s => s.Effet == connu);
        Assert.Contains(H(monde, a.Id).Memoire.Jardin, j => j.Cle == chose.Cle && j.Secondes == chose.Secondes);
    }

    [Fact]
    public void Fautes_DesInventions()
    {
        var monde = Monde(1);
        var a = monde.InstantaneHumains().Humains.First(h => h.EstVivant);
        string Faute(ChangementLibre c) => Assert.Throws<ArgumentException>(() => monde.Libre(new ActeLibre("r", false, true, [], [c], []))).Message.Split('|')[0];
        Assert.Equal("libre.erreur.forme", Faute(new ChangementLibre("plante_nouvelle", X: a.X, Y: a.Y, Nom: "x", Forme: "dragon")));
        Assert.Equal("libre.erreur.nom", Faute(new ChangementLibre("bete_nouvelle", X: a.X, Y: a.Y, Forme: "rodeur")));
        Assert.Equal("libre.erreur.valeur", Faute(new ChangementLibre("plante_nouvelle", X: a.X, Y: a.Y, Nom: "x", Forme: "herbe", Proprietes: new() { ["toxicite"] = 3 })));
        Assert.Equal("libre.erreur.geste", Faute(new ChangementLibre("savoir_nouveau", a.Id, Geste: "Voler")));
        Assert.Empty(monde.InventionsDuMonde());
    }

    [Fact]
    public void Schema_ProposeLesInventions()
    {
        var schema = Actes.Schema(libre: true).ToJsonString();
        Assert.Contains("plante_nouvelle", schema);
        Assert.Contains("\"proprietes\"", schema);
        var d = Actes.Lire("""
            {"pensee":"p","mots":"","actes":[{"type":"libre","recit":"r","changements":[{"quoi":"plante_nouvelle","x":3,"y":4,"forme":"herbe","nom":"n",
            "aliment":"fruits","proprietes":{"toxicite":0.5}},{"quoi":"savoir_nouveau","humain":1,"geste":"Tordre","a":"Fibres"}]}]}
            """);
        var c = d.Actes[0].Changements!;
        Assert.Equal(("herbe", "fruits", 0.5), (c[0].Forme, c[0].Aliment, c[0].Proprietes!["toxicite"]));
        Assert.Equal(("Tordre", "Fibres"), (c[1].Geste, c[1].A));
    }
}
