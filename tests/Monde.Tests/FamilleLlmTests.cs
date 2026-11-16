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
        // 0.44.8 : ce qu'elle porte est redit en tête du dialogue, et « Enfant » ne devient pas une prière pour en avoir un.
        Assert.Contains(bebe.Nom, Contexte.CeQuIlPorte(humains, maintenant, mere.Id));
        Assert.Equal(0, monde.DemanderEnDialogue(mere.Id, SujetPriere.Enfant, 0, 0));
        // Un homme sans enfant le sait aussi.
        var seul = humains.First(h => h.EstVivant && !humains.Any(e => e.MereId == h.Id || e.PereId == h.Id));
        var s = new StringBuilder();
        Contexte.Famille(s, humains, maintenant, seul.Id);
        Assert.Contains("aucun enfant", s.ToString());
    }

    [Theory]
    [InlineData("qui est le père de Thounouvial ?", true)]
    [InlineData("Kelou, qui est l'enfant qui se trouve à tes côtés ?", true)]
    [InlineData("Qui es-tu ? D'où viens-tu ?", true)]
    [InlineData("Je suis celle qui vous a créés. Qui es-tu ?", false)]
    [InlineData("Le monde est né du feu.", false)]
    public void UneQuestion_NEstPasUneRevelation(string texte, bool question) =>
        Assert.Equal(question, Dialogues.SeulementDesQuestions(texte));

    [Theory]
    [InlineData("Je suis Celle qui t'a créé. Mon nom est MICHEL.", "MICHEL")]
    [InlineData("Appelle-moi Ona, enfant.", "Ona")]
    [InlineData("My name is Ava.", "Ava")]
    [InlineData("Te souviens-tu de mon nom ?", null)]
    public void LeNomQuElleSeDonne_EstLu(string texte, string? nom) => Assert.Equal(nom, Dialogues.NomDonne(texte));

    [Theory]
    [InlineData("Elle se nomme Michel, elle dit qu'elle nous a faits.", true)]
    [InlineData("C'est une présence invisible qui m'a fait.", true)]
    [InlineData("She says she made us.", true)]
    [InlineData("Tu es Michel, celle qui nous a faits. Je te crois.", false)]
    [InlineData("C'est Faepe, mon enfant. Elle repose contre moi.", false)]
    public void ParlerDeLEntite_AuLieuDeLuiRepondre(string reponse, bool tiers) => Assert.Equal(tiers, Dialogues.ParleDElle(reponse));
}
