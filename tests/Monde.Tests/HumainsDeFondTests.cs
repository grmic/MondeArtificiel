using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.36.0 : l'option « humains de fond simulés en groupe ».</summary>
public sealed class HumainsDeFondTests
{
    private static readonly ConfigurationMonde Config = ConfigurationMonde.Nouvelle(42);
    private static readonly Carte Carte = GenerateurContinent.Generer(Config);

    private static MondeSimule NouveauMonde(int coeurs = 1) => new(Carte, MondeSimule.Nouveau(Config)) { Coeurs = coeurs };

    private static string Journal(MondeSimule monde) =>
        string.Join("\n", monde.Etat.JournalNonEnregistre!.Select(e => $"{e.Id} {e.CauseId} {e.HumainId} {e.AutreId} {e.Karma:R} {e.Texte}"));

    [Fact]
    public void SousLeSeuil_MemeHistoireQueSansLOption()
    {
        var sans = NouveauMonde();
        var seuil = NouveauMonde();
        seuil.HumainsDeFond = true;
        seuil.SeuilFond = 1_000_000;
        sans.AvancerDe(20 * Calendrier.SecondesParJour);
        seuil.AvancerDe(20 * Calendrier.SecondesParJour);
        Assert.Equal(0, seuil.DeFond);
        Assert.Equal(HumainsTests.Empreinte(sans), HumainsTests.Empreinte(seuil));
        Assert.Equal(Journal(sans), Journal(seuil));
    }

    [Fact]
    public void Cochee_LaPlupartVivantDeFond_SaufLesGardesEtLesBebes()
    {
        var monde = NouveauMonde();
        monde.HumainsDeFond = true;
        monde.GardesEnIndividu = new HashSet<int> { 1, 2 };
        monde.AvancerDe(30 * Calendrier.SecondesParJour);
        var (_, humains) = monde.InstantaneHumains();
        var vivants = humains.Where(h => h.EstVivant).ToList();
        Assert.True(monde.DeFond > vivants.Count / 2, $"{monde.DeFond} de fond sur {vivants.Count}");
        Assert.False(monde.EstDeFond(1));
        Assert.False(monde.EstDeFond(2));
        Assert.All(vivants.Where(h => h.EstBebe(monde.Secondes)), b => Assert.False(monde.EstDeFond(b.Id)));
        // Personne ne meurt de soif au calme : celui qui a soif vit le pas en entier et va boire.
        Assert.DoesNotContain(humains, h => !h.EstVivant && h.CauseDeMort == CauseDeMort.Soif);

        // Décochée en cours de partie : tout le monde revient en individu au pas suivant.
        monde.HumainsDeFond = false;
        monde.AvancerDe(Calendrier.SecondesParHeure);
        Assert.Equal(0, monde.DeFond);
    }

    [Fact]
    public void SurPlusieursCoeurs_MemeHistoireQuelQueSoitLeNombre()
    {
        var deux = NouveauMonde(2);
        var quatre = NouveauMonde(4);
        deux.HumainsDeFond = quatre.HumainsDeFond = true;
        deux.AvancerDe(10 * Calendrier.SecondesParJour);
        quatre.AvancerDe(10 * Calendrier.SecondesParJour);
        Assert.True(deux.DeFond > 0);
        Assert.Equal(HumainsTests.Empreinte(deux), HumainsTests.Empreinte(quatre));
        Assert.Equal(Journal(deux), Journal(quatre));
    }
}
