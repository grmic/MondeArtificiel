using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.43.0 : averses détaillées à partir des jours de pluie de la simulation, et catastrophes en cours pour la carte et la 3D.</summary>
public sealed class TempsTests
{
    private const long Jour = Calendrier.SecondesParJour;

    [Fact]
    public void Averses_SontReproductibles_EtTiennentDansLaJournee()
    {
        for (var jour = 0L; jour < 360; jour += 7)
        {
            var a = TempsDto.Detailler(42, 3, jour, 1.2, true, true, _ => 15);
            var b = TempsDto.Detailler(42, 3, jour, 1.2, true, true, _ => 15);
            Assert.Equal(a, b);
            Assert.NotEmpty(a);
            foreach (var x in a)
            {
                Assert.InRange(x.Debut, 0, 24);
                Assert.InRange(x.Fin, x.Debut, 24);
                Assert.InRange(x.Force, 0.05, 1);
            }
            // Les averses ne se chevauchent pas.
            for (var k = 1; k < a.Count; k++) Assert.True(a[k].Debut > a[k - 1].Fin);
        }
    }

    [Fact]
    public void SousLeGel_IlNeige_EtLEteChaud_DonneDesOrages()
    {
        var hiver = 3 * Calendrier.JoursParSaison + 10;
        Assert.All(TempsDto.Detailler(42, 1, hiver, 1, false, false, _ => -4), a => Assert.Equal(SorteAverse.Neige, a.Sorte));
        var ete = Calendrier.JoursParSaison + 10;
        var orages = Enumerable.Range(0, 40).Count(k => TempsDto.Detailler(42, k, ete, 1, false, false, _ => 30).Any(a => a.Sorte == SorteAverse.Orage));
        // Environ deux jours de pluie d'été chauds sur trois tournent à l'orage.
        Assert.InRange(orages, 16, 36);
        // Une année très arrosée donne plus d'heures de pluie qu'une année sèche (en moyenne).
        double Heures(double pluie) => Enumerable.Range(0, 200).Sum(k => TempsDto.Detailler(7, k, 5, pluie, false, false, _ => 15).Sum(a => a.Fin - a.Debut));
        Assert.True(Heures(1.6) > Heures(0.6) * 1.5);
    }

    [Fact]
    public void Temps_SuitLesJoursDePluieDeLaSimulation()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(40 * Jour);
        var t = TempsDto.Depuis(monde);
        var pleut = monde.JoursDePluie(t.Jour);
        var regions = new HashSet<int>();
        for (var k = 0; k + 6 < t.Averses.Length; k += 7)
        {
            if (t.Averses[k + 1] >= 0 && t.Averses[k + 4] != (double)SorteAverse.Orage) regions.Add((int)t.Averses[k]);
        }
        // Chaque région où la simulation fait pleuvoir aujourd'hui a ses averses, et seulement celles-là (hors orage de foudre).
        for (var r = 0; r < pleut.Length; r++) Assert.Equal(pleut[r], regions.Contains(r));
        Assert.Equal(t.RegionsX * t.RegionsY, pleut.Length);
    }

    [Fact]
    public void Temps_FonctionneDesLePremierJour()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var t = TempsDto.Depuis(monde);
        Assert.Equal(0, t.Jour);
        Assert.Empty(monde.JoursDePluie(-1).Where(p => p));
    }

    [Fact]
    public void EnCours_MontreUnIncendie_EtSonFront()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var carte = monde.Carte;
        var (x, y) = Enumerable.Range(0, carte.Largeur * carte.Hauteur).Select(i => (i % carte.Largeur, i / carte.Largeur))
            .First(p => carte.BiomeEn(p.Item1, p.Item2) is Simulation.Monde.Biome.ForetDeConiferes or Simulation.Monde.Biome.Lande);
        monde.Declencher(TypeCatastrophe.Incendie, x, y, 0);
        var en = EnCoursDto.Depuis(monde);
        var feu = Assert.Single(en.Zones, z => z.Type == "incendie");
        Assert.True(feu.Active);
        Assert.NotEmpty(feu.Front);
        Assert.Equal(feu.Cases.Length, feu.Degats.Length);
    }
}
