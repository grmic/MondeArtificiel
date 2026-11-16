using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Population animale par espèce (0.44.2) : une estimation tirée du gibier, lue sans rien changer.</summary>
public sealed class PopulationFauneTests
{
    private static MondeSimule NouveauMonde() => new(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));

    [Fact]
    public void Population_PartageLeGibierEntreLesEspecesDuTerrain()
    {
        var monde = NouveauMonde();
        var p = monde.Population();
        Assert.Equal(monde.Faune.Betes.Count, p.Especes.Count);
        // Le poids vivant de toutes les bêtes estimées est celui du gibier des cases qui portent des espèces.
        var attendu = 0.0;
        for (var i = 0; i < p.Gibier.Length; i++)
            if (monde.Faune.Sur((Monde.Simulation.Monde.Biome)monde.Carte.Biomes[i]).Count > 0) attendu += p.Gibier[i] * MondeSimule.KgVifParPartDeGibier;
        var estime = p.Especes.Sum(e => e.Sauvages * monde.Faune.Get(e.Numero).Poids);
        Assert.True(attendu > 0);
        Assert.Equal(attendu, estime, attendu * 1e-9);
        Assert.Contains(p.Especes, e => e.Sauvages > 0);
        Assert.All(p.Especes, e => Assert.Null(e.SauvagesAvant));
    }

    [Fact]
    public void Population_DonneLaTendanceApresUnMois_EtNeChangePasLaSimulation()
    {
        var lu = NouveauMonde();
        var temoin = NouveauMonde();
        for (var j = 0; j < 4; j++)
        {
            lu.AvancerDe(10 * Calendrier.SecondesParJour);
            temoin.AvancerDe(10 * Calendrier.SecondesParJour);
            lu.Population();
        }
        Assert.Equal(HumainsTests.Empreinte(temoin), HumainsTests.Empreinte(lu));
        var p = lu.Population();
        Assert.All(p.Especes, e => Assert.NotNull(e.SauvagesAvant));
        Assert.All(p.Especes, e => Assert.True(e.Mois >= 1));
    }

    [Fact]
    public void Population_CompteLesTroupeauxGardes()
    {
        var monde = NouveauMonde();
        var h = monde.InstantaneHumains().Humains.First(x => x.EstVivant);
        var t = new Champs().NouveauTroupeau(monde.Faune.Betes[0], 0, h.Id, 4, 2, 0.45, 0, 0);
        var avec = new MondeSimule(MondeSimuleTests.Carte42, monde.Etat with { Troupeaux = [t] });
        var p = avec.Population();
        var e = p.Especes.Single(x => x.Numero == monde.Faune.Betes[0].Numero);
        Assert.Equal((4.0, 2.0, 1), (e.GardeesAdultes, e.GardeesPetits, e.Troupeaux));
        var (troupeau, (x, y)) = Assert.Single(p.Troupeaux);
        Assert.Equal((t.Id, h.X, h.Y), (troupeau.Id, x, y));
    }
}
