using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Flux « Scène » de la vue 3D (0.41.0) : il lit sans rien changer, et ne donne que les vivants autour de la caméra.</summary>
public sealed class SceneTests
{
    private static MondeSimule NouveauMonde() => new(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));

    [Fact]
    public void Scene_NeChangePasLaSimulation()
    {
        var lu = NouveauMonde();
        var temoin = NouveauMonde();
        for (var j = 0; j < 6; j++)
        {
            lu.AvancerDe(Calendrier.SecondesParJour / 2);
            temoin.AvancerDe(Calendrier.SecondesParJour / 2);
            for (var k = 0; k < 5; k++) lu.Scene(lu.Carte.Largeur / 2, lu.Carte.Hauteur / 2, 400);
        }
        Assert.Equal(HumainsTests.Empreinte(temoin), HumainsTests.Empreinte(lu));
    }

    [Fact]
    public void Scene_DonneLesVivantsDansLeRayon()
    {
        var monde = NouveauMonde();
        monde.AvancerDe(2 * Calendrier.SecondesParJour);
        var (_, vivants) = monde.InstantaneHumains();
        var h = vivants.First(v => v.EstVivant);
        var (secondes, proches) = monde.Scene(h.X, h.Y, 2);
        Assert.Equal(monde.Secondes, secondes);
        Assert.Contains(proches, p => p.Id == h.Id);
        Assert.All(proches, p =>
        {
            Assert.InRange(p.X, h.X - 2, h.X + 2);
            Assert.InRange(p.Y, h.Y - 2, h.Y + 2);
            Assert.InRange(p.Avance, 0, 1);
            Assert.True(p.ParSeconde >= 0);
            // Une case visée est voisine.
            if (p.VersX >= 0) Assert.True(Math.Abs(p.VersX - p.X) <= 1 && Math.Abs(p.VersY - p.Y) <= 1 && (p.VersX, p.VersY) != (p.X, p.Y));
        });
        var tous = monde.Scene(0, 0, 10_000).Humains;
        Assert.Equal(vivants.Count(v => v.EstVivant), tous.Count);
    }

    [Fact]
    public void Scene_MarcheurAvanceVersUneCaseVoisine()
    {
        var monde = NouveauMonde();
        var vus = 0;
        for (var pas = 0; pas < 400 && vus == 0; pas++)
        {
            monde.AvancerDe(MondeSimule.SecondesParPas);
            vus = monde.Scene(0, 0, 10_000).Humains.Count(p => p.VersX >= 0 && p.ParSeconde > 0);
        }
        Assert.True(vus > 0, "personne ne marche vers une case voisine en 400 pas");
    }
}
