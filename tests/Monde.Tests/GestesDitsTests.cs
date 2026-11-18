using Monde.Serveur.Entite;

namespace Monde.Tests;

/// <summary>0.45.19 : ce que l'humain dit faire de son corps en répondant à l'Entité, et les gestes de sa réponse.</summary>
public sealed class GestesDitsTests
{
    [Theory]
    [InlineData("Oui, Entité. Je ferme les yeux.", "FermerYeux")]
    [InlineData("Je m'assieds, puis je ferme les yeux.", "Asseoir,FermerYeux")]
    [InlineData("Je me cache les yeux avec les mains, j'ai peur.", "CacherYeux")]
    [InlineData("Je ne fermerai pas les yeux devant toi.", "")]
    [InlineData("Dois-je fermer les yeux ?", "")]
    [InlineData("Je cours vers toi !", "CourirVers")]
    [InlineData("Je saute, je saute encore, et je danse.", "Sauter,Danser")]
    [InlineData("Je m'allonge sur le ventre, face contre terre.", "CoucherVentre")]
    [InlineData("Je suis dans la forêt, près du feu.", "")]
    [InlineData("I close my eyes and kneel down.", "FermerYeux,Genoux")]
    [InlineData("I won't close my eyes.", "")]
    [InlineData("I raise my arms to you, then I run away.", "LeverBras,Fuir")]
    public void LesGestesDitsDansLaReponse_SontLus(string texte, string attendus)
    {
        Assert.Equal(attendus, string.Join(",", GestesDits.Lire(texte)));
    }

    [Fact]
    public void LesGestesDeLaReponse_SontLus_EnListeOuDansLAncienChamp()
    {
        Assert.Equal(["Asseoir", "FermerYeux"], Dialogues.LireGestes("""{"reponse":"…","gestes":["Asseoir","fermeryeux","Inconnu"]}"""));
        Assert.Equal(["Genoux"], Dialogues.LireGestes("""{"reponse":"…","geste":"Genoux"}"""));
        Assert.Equal(["Sauter", "Danser", "Crier"], Dialogues.LireGestes("""{"gestes":["Sauter","Danser","Crier","Pleurer"]}"""));
        Assert.Empty(Dialogues.LireGestes("pas du JSON"));
    }
}
