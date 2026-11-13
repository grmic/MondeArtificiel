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
            for (var k = 0; k < 5; k++)
            {
                lu.Scene(lu.Carte.Largeur / 2, lu.Carte.Hauteur / 2, 400);
                lu.Camp(lu.Carte.Largeur / 2, lu.Carte.Hauteur / 2, 400);
            }
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

    [Fact]
    public void Scene_DonneLeCorps()
    {
        var monde = NouveauMonde();
        monde.AvancerDe(Calendrier.SecondesParJour);
        var (_, vivants) = monde.InstantaneHumains();
        foreach (var p in monde.Scene(0, 0, 10_000).Humains)
        {
            var h = vivants.First(v => v.Id == p.Id);
            Assert.Equal(h.Reserves, p.Reserves, 6);
            Assert.InRange(p.Endurance, 0, 1);
            Assert.Equal(h.Apparence.Taille, p.Taille, 6);
            Assert.Equal(h.Vetement?.Matiere ?? Matiere.Aucune, p.Vetement);
            Assert.True(p.Enfants >= 0);
            // Seule une femme allaite.
            if ((p.Etat & 64) != 0) Assert.True(p.Femme);
        }
    }

    [Fact]
    public void Camp_DonneLesBetesSauvagesDansLeRayon()
    {
        var monde = NouveauMonde();
        monde.AvancerDe(Calendrier.SecondesParJour);
        var (huttes, champs, betes) = monde.Camp(monde.Carte.Largeur / 2, monde.Carte.Hauteur / 2, 6);
        Assert.NotEmpty(betes);
        Assert.All(betes, b =>
        {
            Assert.InRange(b.X, monde.Carte.Largeur / 2 - 6, monde.Carte.Largeur / 2 + 6);
            Assert.InRange(b.Forme, 0, Faune.NombreDeFormes - 1);
            Assert.InRange(b.Nombre, 1, 400);
            Assert.True(b.Poids > 0);
        });
        Assert.All(huttes, h => Assert.InRange(h.Solidite, 0, 1));
        Assert.All(champs, c => Assert.InRange(c.Ares, 0, 10_000));
    }
}
