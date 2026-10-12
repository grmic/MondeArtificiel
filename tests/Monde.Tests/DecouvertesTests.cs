using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Lois du monde, apprentissage, imitation, oubli, intuition de l'Entité et enregistrement des savoirs.</summary>
public sealed class DecouvertesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 10 * An;

    private static ContexteGeste Contexte(double sec = 0.8, bool feu = false, bool eau = false, Biome biome = Biome.PlaineDeFougeres,
        double adresse = 0.5) => new(feu, eau, sec, biome, adresse, 0.5);

    private static int Compter(Geste g, Matiere a, Matiere b, ContexteGeste c, Effet effet, int essais = 2000, ulong graine = 1)
    {
        var alea = new Alea(graine);
        var n = 0;
        for (var i = 0; i < essais; i++) if (Physique.Tenter(g, a, b, c, alea).Effet == effet) n++;
        return n;
    }

    private static Humain Humain(int id, double age = 25, Sexe sexe = Sexe.Femme, int x = 10, int y = 10, double ouverture = 0.5) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, 0.5, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
    };

    [Fact]
    public void Lois_LeSilexDonneDesEclatsBienPlusSouventQueLeGalet()
    {
        var silex = Compter(Geste.Frapper, Matiere.Silex, Matiere.Galet, Contexte(), Effet.Eclat);
        var galets = Compter(Geste.Frapper, Matiere.Galet, Matiere.Galet, Contexte(), Effet.Eclat);
        Assert.InRange(silex, 900, 1500);
        Assert.InRange(galets, 100, 500);
        Assert.Equal(0, Compter(Geste.Frapper, Matiere.Branche, Matiere.Galet, Contexte(), Effet.Eclat));
    }

    [Fact]
    public void Lois_LeFeuNaitRarement_JamaisDuBoisMouille_EtLaPyriteDonneDesEtincelles()
    {
        Assert.Equal(0, Compter(Geste.Frotter, Matiere.BoisSec, Matiere.BoisSec, Contexte(sec: 0.2, adresse: 1), Effet.Flamme, 20_000));
        var friction = Compter(Geste.Frotter, Matiere.BoisSec, Matiere.BoisSec, Contexte(sec: 1, adresse: 1), Effet.Flamme, 20_000);
        Assert.InRange(friction, 20, 200);
        // Celui qui a déjà allumé un feu ainsi y arrive bien plus souvent.
        var exerce = Compter(Geste.Frotter, Matiere.BoisSec, Matiere.BoisSec, Contexte(sec: 1, adresse: 1) with { FeuxReussis = 3 }, Effet.Flamme, 20_000);
        Assert.True(exerce > 10 * friction);
        Assert.True(Compter(Geste.Frapper, Matiere.Silex, Matiere.Pyrite, Contexte(), Effet.Etincelle) > 800);
        Assert.Equal(0, Compter(Geste.Frapper, Matiere.Silex, Matiere.Galet, Contexte(), Effet.Etincelle));
    }

    [Fact]
    public void Lois_OnNeRemplitPasUneCalebasseLoinDeLEau_EtLaTerreDuDesertDonnePeuDeRacines()
    {
        Assert.Equal(0, Compter(Geste.Remplir, Matiere.Calebasse, Matiere.Aucune, Contexte(eau: false), Effet.EauPortee));
        Assert.Equal(2000, Compter(Geste.Remplir, Matiere.Calebasse, Matiere.Aucune, Contexte(eau: true), Effet.EauPortee));
        var foret = Compter(Geste.Fouir, Matiere.Branche, Matiere.Aucune, Contexte(biome: Biome.ForetTropicale), Effet.Tubercules);
        var desert = Compter(Geste.Fouir, Matiere.Branche, Matiere.Aucune, Contexte(biome: Biome.Desert), Effet.Tubercules);
        Assert.True(foret > 2 * desert);
        Assert.Equal(0, Compter(Geste.Approcher, Matiere.BatonPointu, Matiere.Feu, Contexte(feu: false), Effet.Durci));
    }

    [Fact]
    public void Confiance_UneSeuleReussiteNeProuvePasGrandChose()
    {
        var s = new Savoir(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, 1, 1, 0, 0, OrigineSavoir.Essai, 0, 0);
        Assert.Equal(2 / 3.0, s.Confiance, 6);
        Assert.Equal(11 / 12.0, (s with { Essais = 10, Succes = 10 }).Confiance, 6);
        Assert.Equal(1 / 12.0, (s with { Essais = 10, Succes = 0 }).Confiance, 6);
    }

    [Fact]
    public void Flore_MemeGraineMemesPlantes_EtLesVertusAgissentSurLeBonMal()
    {
        var a = new Flore(7);
        var b = new Flore(7);
        Assert.Equal(a.Especes.Select(e => e.Nom), b.Especes.Select(e => e.Nom));
        Assert.Contains(a.Especes, e => e.Toxicite > 0);
        var astringente = a.Especes.First(e => e.Astringente > 0);
        Assert.True(astringente.Efficacite(Maladie.Diarrhee, Geste.Manger) > 0);
        Assert.Equal(0, astringente.Efficacite(Maladie.Diarrhee, Geste.Appliquer));
        Assert.Equal(0, astringente.Efficacite(Maladie.Pneumonie, Geste.Manger));
    }

    [Fact]
    public void Imitation_UnEnfantQuiRegardeFaireApprendLeGeste_EtSaitDeQui()
    {
        var e = Carte.Emplacements[0];
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(5)) { Social = new VieSociale(new Alea(5)) };
        var tailleur = Humain(1, sexe: Sexe.Homme, x: e.X, y: e.Y);
        tailleur.Personnalite = tailleur.Personnalite with { Conscience = 1 };
        tailleur.Savoirs.Add(new Savoir(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, 8, 8, 0, Maintenant,
            OrigineSavoir.Essai, 0, 0));
        tailleur.Savoirs.Add(new Savoir(Geste.Gratter, Matiere.Carcasse, Matiere.Eclat, Effet.Decoupe, 0, Symptome.Aucun, 8, 8, 0, Maintenant,
            OrigineSavoir.Essai, 0, 0));
        var enfant = Humain(2, age: 9, x: e.X, y: e.Y, ouverture: 0.9);
        tailleur.Liens.Add(new Lien(2, 80, 70, Maintenant, Amis: true, Rivaux: false));
        enfant.Liens.Add(new Lien(1, 80, 70, Maintenant, Amis: true, Rivaux: false));
        var tous = new List<Humain> { tailleur, enfant };
        for (var pas = 0; pas < 20_000 && !enfant.Sait(Effet.Eclat) && !enfant.Savoirs.Exists(s => s.Effet == Effet.Eclat); pas++)
        {
            (tailleur.X, tailleur.Y, enfant.X, enfant.Y) = (e.X, e.Y, e.X, e.Y);
            tailleur.Faim = tailleur.Soif = tailleur.Fatigue = enfant.Faim = enfant.Soif = enfant.Fatigue = 0;
            tailleur.Sante = enfant.Sante = 100;
            tailleur.Desir = 0;
            // Il garde son silex et son galet, mais l'éclat qu'il taille lui est pris : il en refera un.
            tailleur.Objets.RemoveAll(o => o.Matiere == Matiere.Eclat);
            if (!tailleur.Porte(Matiere.Silex)) tailleur.Objets.Add(new Objet(Matiere.Silex, 1, 1));
            if (!tailleur.Porte(Matiere.Galet)) tailleur.Objets.Add(new Objet(Matiere.Galet, 1, 1));
            c.Vivre(tailleur, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        }
        var appris = enfant.Savoirs.Find(s => s.Effet == Effet.Eclat);
        Assert.NotNull(appris);
        Assert.Equal(OrigineSavoir.Imitation, appris.Origine);
        Assert.Equal(1, appris.MaitreId);
        Assert.Contains(c.Journal, j => j.Type == TypeEvenement.Decouverte && j.HumainId == 2 && j.Texte.Contains("regardé faire H1"));
    }

    [Fact]
    public void Oubli_UnGesteJamaisRefaitSOublie_UnPoisonNon()
    {
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1));
        var h = Humain(1);
        h.Savoirs.Add(new Savoir(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde, 0, Symptome.Aucun, 1, 1, 0, Maintenant - 3 * An,
            OrigineSavoir.Essai, 0, 0));
        h.Savoirs.Add(new Savoir(Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Poison, 3, Symptome.Aucun, 1, 1, 0, Maintenant - 3 * An,
            OrigineSavoir.Essai, 0, 0));
        h.Savoirs.Add(new Savoir(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, 1, 1, 0, Maintenant - 10 * Jour,
            OrigineSavoir.Essai, 0, 0));
        var oublies = c.UserEtOublier([h], Maintenant);
        Assert.Equal([Effet.Corde], oublies.Select(o => o.Savoir.Effet));
        Assert.Equal([Effet.Poison, Effet.Eclat], h.Savoirs.Select(s => s.Effet));
    }

    [Fact]
    public void Entite_UneIntuitionSansLaMatiereSEfface_AvecElleLeGesteEstEssayeEtMarqueCommeVenuDeLEntite()
    {
        var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
        var h = etat.Humains![0];
        h.Objets.Add(new Objet(Matiere.Silex, 1, 1));
        h.Objets.Add(new Objet(Matiere.Galet, 1, 1));
        var monde = new MondeSimule(Carte, etat);

        Assert.Equal(Effet.Rien, monde.Souffler(1, Geste.Frapper, Matiere.Obsidienne, Matiere.Galet));
        var effet = Effet.Rien;
        for (var i = 0; i < 40 && effet != Effet.Eclat; i++) effet = monde.Souffler(1, Geste.Frapper, Matiere.Silex, Matiere.Galet);
        Assert.Equal(Effet.Eclat, effet);
        Assert.Equal(Effet.Rien, monde.Souffler(9999, Geste.Frapper, Matiere.Silex, Matiere.Galet));

        var journal = monde.Etat.JournalNonEnregistre!;
        Assert.Contains(journal, j => j.Texte.Contains("sous la main") && j.Origine == Origine.Entite);
        Assert.Contains(journal, j => j.Significatif && j.Texte.Contains("Première fois au monde") && j.Origine == Origine.Entite);
        var (_, _, decouvertes) = monde.Installations();
        Assert.Contains(decouvertes, d => d.Effet == Effet.Eclat && d.HumainId == 1 && d.Origine == OrigineSavoir.Entite);
        Assert.Contains(monde.Etat.Humains![0].Savoirs, s => s.Effet == Effet.Eclat && s.Origine == OrigineSavoir.Entite);
    }

    [Fact]
    public void Enregistrement_SavoirsObjetsFeuxHuttesEtDecouvertes_SontRelusALIdentique()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-savoirs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat with
            {
                Foyers = [new Foyer(1, 5, 6, 7.5, 100, 1, 11)],
                Huttes = [new Hutte(2, 8, 9, 0.75, 200, 2, 12)],
                Decouvertes = [new Decouverte(Effet.Eclat, 1, 50, 10, OrigineSavoir.Essai), new Decouverte(Effet.Abri, 2, 200, 12, OrigineSavoir.Entite)],
            };
            var h = etat.Humains![0];
            h.Savoirs.Add(new Savoir(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, 4, 3, 50, 60, OrigineSavoir.Essai, 0, 10));
            h.Savoirs.Add(new Savoir(Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Soulagement, 3, Symptome.Ventre, 2, 2, 70, 80,
                OrigineSavoir.Imitation, 2, 13));
            h.Objets.Add(new Objet(Matiere.Calebasse, 1, 0.8, Plein: true));
            h.Objets.Add(new Objet(Matiere.Epieu, 0.7, 0.5));
            h.Vetement = new Objet(Matiere.Peau, 0.6, 0.9);
            h.Nage = 0.45;
            h.CuitJusqua = 123;
            (h.RemedeEspece, h.RemedeGeste, h.RemedeSymptome, h.RemedeLe) = (3, Geste.Manger, Symptome.Ventre, 77);

            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            var r = relu.Humains![0];
            Assert.Equal(h.Savoirs, r.Savoirs);
            Assert.Equal(h.Objets, r.Objets);
            Assert.Equal(h.Vetement, r.Vetement);
            Assert.Equal((0.45, 123L, 3, Geste.Manger, Symptome.Ventre, 77L), (r.Nage, r.CuitJusqua, r.RemedeEspece, r.RemedeGeste, r.RemedeSymptome, r.RemedeLe));
            Assert.Null(relu.Humains[1].Vetement);
            Assert.Equal(etat.Foyers, relu.Foyers);
            Assert.Equal(etat.Huttes, relu.Huttes);
            Assert.Equal(etat.Decouvertes, relu.Decouvertes);

            // Un monde de la version 18 se relit, sans savoirs.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion18);
            var ancien = new DepotMonde(fichier).Charger()!;
            Assert.All(ancien.Humains!, x => Assert.Empty(x.Savoirs));
            Assert.Null(ancien.Foyers);
            new DepotMonde(fichier).Enregistrer(ancien, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
