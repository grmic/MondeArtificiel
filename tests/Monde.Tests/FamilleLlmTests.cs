using System.Text;
using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.44.7 : le LLM qui joue un humain sait qui sont ses enfants, et quel bébé il ou elle porte.</summary>
public sealed class FamilleLlmTests
{
    [Fact]
    public void UneMereQuiPorteSonBebe_LeSait()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var (maintenant, humains) = (0L, (IReadOnlyList<Humain>)[]);
        Humain? bebe = null;
        for (var j = 0; j < 40 && bebe is null; j++)
        {
            monde.AvancerDe(30 * Calendrier.SecondesParJour);
            (maintenant, humains) = monde.InstantaneHumains();
            bebe = humains.FirstOrDefault(b => b.EstVivant && b.EstBebe(maintenant) && b.Activite == Activite.PorteParSaMere && b.MereId > 0
                                               && humains[b.MereId - 1].EstVivant && humains[b.MereId - 1].BebePorteLe >= maintenant - 2 * MondeSimule.SecondesParPas);
        }
        Assert.NotNull(bebe);
        var mere = humains[bebe!.MereId - 1];
        var d = new StringBuilder();
        Contexte.Famille(d, humains, maintenant, mere.Id);
        var texte = d.ToString();
        Assert.Contains($"{bebe.Nom} n°{bebe.Id}", texte);
        Assert.Contains("tu portes contre toi ton bébé " + bebe.Nom, texte);
        Assert.DoesNotContain("aucun enfant", texte);
        // Un homme sans enfant le sait aussi.
        var seul = humains.First(h => h.EstVivant && !humains.Any(e => e.MereId == h.Id || e.PereId == h.Id));
        var s = new StringBuilder();
        Contexte.Famille(s, humains, maintenant, seul.Id);
        Assert.Contains("aucun enfant", s.ToString());
    }
}
