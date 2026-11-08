using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.37.0 : « Langage avancé » (langue mère héritée par terre de départ) et idées nouvelles de la génération 3.</summary>
public sealed class LangageAvanceTests
{
    private const long Jour = Calendrier.SecondesParJour;

    private static MondeSimule Creer(bool langage, ulong graine = 42, int generation = ConfigurationMonde.GenerationActuelle)
    {
        var config = ConfigurationMonde.ParDefaut(graine) with { Generation = generation, LangageAvance = langage };
        return new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)) { Coeurs = 1 };
    }

    [Fact]
    public void LangageAvance_LesFondateursParlentLaMemeLangueDesLePremierJour()
    {
        var monde = Creer(true);
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains, h =>
        {
            Assert.Equal(3, h.StadeParole);
            Assert.NotEqual(OrdreMots.Aucun, h.Ordre);
            Assert.NotNull(h.NomLangue);
            Assert.True(h.Vocabulaire.Count >= 40, $"{h.Nom} : {h.Vocabulaire.Count} mots");
            // Personne ne les a inventés : ils viennent de la langue des fondateurs, sans langue de groupe encore.
            Assert.All(h.Vocabulaire, m => Assert.Equal((0, 0), (m.AuteurId, m.Langue)));
        });
        // Une seule terre de départ : une seule langue, donc une seule forme par idée, et un seul ordre des mots.
        Assert.All(humains.SelectMany(h => h.Vocabulaire).GroupBy(m => m.Idee), g => Assert.Single(g.Select(m => (m.Forme, m.Signe)).Distinct()));
        Assert.Single(humains.Select(h => h.Ordre).Distinct());
        // Chacun la connaît à sa façon : tous ne savent pas autant de mots.
        Assert.True(humains.Max(h => h.Vocabulaire.Count) > humains.Min(h => h.Vocabulaire.Count));
        // Les idées nouvelles de la génération 3 en font partie ; les savoirs et les plantes non.
        Assert.Contains(humains.SelectMany(h => h.Vocabulaire), m => m.Idee == "main");
        Assert.DoesNotContain(humains.SelectMany(h => h.Vocabulaire), m => m.Idee.Contains(':'));
        var e = Assert.Single(monde.Etat.JournalNonEnregistre!, x => x.Recit?.Cle == "lang.herite");
        Assert.All(humains, h => Assert.Equal(e.Id, h.NomEvenementId));
    }

    [Fact]
    public void LangageAvance_LesMotsAppris_AvantLePremierJour_SAffichentSurLaFiche()
    {
        // Les mots de la langue héritée ont été appris dans l'enfance des fondateurs, avant le premier jour du monde (date
        // négative) : la fiche de l'humain les montre sans planter (0.37.3, bug « la fiche ne s'affiche pas »).
        var monde = Creer(true);
        var (_, humains) = monde.InstantaneHumains();
        Assert.Contains(humains.SelectMany(h => h.Vocabulaire), m => m.Le < 0);
        var h = humains.First(x => x.Vocabulaire.Any(m => m.Le < 0));
        var dto = Monde.Serveur.LangageDto.Depuis(h, humains, monde.Communautes(), monde.Flore);
        Assert.Contains(dto.Mots, m => m.Depuis == Monde.Serveur.L.T("date.avant"));
    }

    [Fact]
    public void SansLangageAvance_LesFondateursPartentDesCris()
    {
        var (_, humains) = Creer(false).InstantaneHumains();
        Assert.All(humains, h => Assert.Equal((0, 0, (string?)null), (h.StadeParole, h.Vocabulaire.Count, h.NomLangue)));
    }

    [Fact]
    public void IdeesNouvelles_SeulementDepuisLaGeneration3()
    {
        var monde = Creer(false);
        var (maintenant, humains) = monde.InstantaneHumains();
        var h = humains[0];
        var avant = Idees.Sujets(h, humains, monde.Carte, maintenant, new HashSet<int>(), 2);
        var apres = Idees.Sujets(h, humains, monde.Carte, maintenant, new HashSet<int>(), 3);
        Assert.DoesNotContain(avant, Idees.Nouvelles.ContainsKey);
        Assert.Contains("main", apres);
        Assert.Contains("demain", apres);
        // Pas d'occasion, pas de mot : un fondateur sans religion ne parle pas d'offrande.
        Assert.DoesNotContain("offrande", apres);
        // La langue héritée des mondes plus anciens n'a pas les idées nouvelles.
        Assert.DoesNotContain(Idees.DUneLangueHeritee(2), Idees.Nouvelles.ContainsKey);
        Assert.All(Idees.Nouvelles.Keys, i => Assert.True(Idees.Libelles.ContainsKey(i)));
    }

    [Fact]
    public void UnGroupeQuiSeForme_ParleLaLangueHeritee_SansPremierMotNiLigneParMot()
    {
        var monde = Creer(true);
        var (maintenant, humains) = monde.InstantaneHumains();
        var groupe = humains.Take(8).ToList();
        foreach (var h in groupe) h.CommunauteId = 1;
        var c = new Communaute { Id = 1, Nom = "Communauté de test", FondeeLe = 0, FigureId = groupe[0].Id, CentreX = groupe[0].X, CentreY = groupe[0].Y, Rayon = 6 };
        var communautes = new List<Communaute> { c };
        var social = new VieSociale(new Alea(3)) { Communautes = communautes, LangageAvance = true, Generation = 3 };
        social.ConstaterLangues([.. humains], communautes, maintenant + 7 * Jour);
        Assert.True(c.Lexique.Count + c.Signes.Count >= 40);
        Assert.All(c.HistoireLangue, x => Assert.Equal(ChangementMot.Herite, x.Type));
        Assert.True(c.ParoleLe > 0);
        var cles = social.Journal.Select(x => x.Recit?.Cle).ToList();
        Assert.Single(cles, k => k == "lang.herite.communaute");
        Assert.DoesNotContain(cles, k => k is "lang.mot" or "lang.mot.de" or "lang.premierMot" or "lang.premierMot.monde" or "lang.combinaison" or "lang.ordre");
        // Les sons de la langue du groupe sont ceux de la langue parlée : un mot neuf y ressemble.
        var mots = groupe.SelectMany(h => h.Vocabulaire).Where(m => !m.Signe).Select(m => m.Forme).ToList();
        Assert.All(c.Sons!.Consonnes, s => Assert.Contains(mots, f => f.Contains(s, StringComparison.Ordinal)));
    }

    [Fact]
    public void LeReglageEstGardeDansLaBase()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-langage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, "monde.db");
            var etat = Creer(true).Etat;
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.True(relu.Configuration!.LangageAvance);
            Assert.Equal(ConfigurationMonde.GenerationActuelle, relu.Configuration.Generation);
            // Rechargé, le monde ne redonne pas une langue : il garde celle qu'il a.
            var monde = new MondeSimule(GenerateurContinent.Generer(relu.Configuration), relu);
            Assert.Equal(etat.Humains!.Select(h => h.Vocabulaire.Count), monde.InstantaneHumains().Humains.Select(h => h.Vocabulaire.Count));
            Assert.DoesNotContain(monde.Etat.JournalNonEnregistre ?? [], x => x.Recit?.Cle == "lang.herite" && x.Id > etat.ProchainIdJournal);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
