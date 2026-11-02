using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.36.1 : les visages hérités, calculés sans toucher à la simulation.</summary>
public sealed class VisagesTests
{
    [Fact]
    public void Visages_StablesHeritesEtSansEffetSurLHistoire()
    {
        var config = ConfigurationMonde.Nouvelle(11);
        var carte = GenerateurContinent.Generer(config);
        var avec = new MondeSimule(carte, MondeSimule.Nouveau(config)) { Coeurs = 1 };
        var sans = new MondeSimule(carte, MondeSimule.Nouveau(config)) { Coeurs = 1 };
        // On regarde les visages pendant que le monde avance : l'histoire doit rester la même.
        for (var j = 0; j < 10; j++)
        {
            avec.AvancerDe(30 * Calendrier.SecondesParJour);
            sans.AvancerDe(30 * Calendrier.SecondesParJour);
            Assert.NotEmpty(VisageDto.De(avec, Enumerable.Range(1, 400)));
        }
        Assert.Equal(HumainsTests.Empreinte(sans), HumainsTests.Empreinte(avec));

        var (maintenant, humains) = avec.InstantaneHumains();
        // Le même humain a toujours le même visage, et deux humains n'ont pas le même.
        Assert.Equal(Visages.Traits(avec, humains[0], humains), Visages.Traits(avec, humains[0], humains));
        Assert.NotEqual(Visages.Traits(avec, humains[0], humains), Visages.Traits(avec, humains[1], humains));

        // Un enfant ressemble à ses parents plus qu'à un humain pris au hasard.
        var enfants = humains.Where(h => h.MereId > 0 && h.PereId > 0).ToList();
        Assert.True(enfants.Count >= 5, $"{enfants.Count} enfants");
        double Ecart(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Average();
        var auxParents = enfants.Average(e =>
        {
            var t = Visages.Traits(avec, e, humains);
            var m = Visages.Traits(avec, humains[e.MereId - 1], humains);
            var p = Visages.Traits(avec, humains[e.PereId - 1], humains);
            return Ecart(t, m.Zip(p, (x, y) => (x + y) / 2).ToArray());
        });
        var auHasard = enfants.Average(e => Ecart(Visages.Traits(avec, e, humains), Visages.Traits(avec, humains[(e.Id * 7) % 66], humains)));
        Assert.True(auxParents < 0.85 * auHasard, $"aux parents {auxParents:F2}, au hasard {auHasard:F2}");

        // L'âge et l'état du moment se lisent dans le portrait.
        var v = VisageDto.De(avec, humains[0], humains, maintenant);
        Assert.Equal(Visages.Nombre, v.Traits.Length);
        Assert.InRange(v.Teint, 0, 1);
    }
}
