using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.42.8 : demander un geste (s'asseoir, prier…), la volonté du moment, et la réponse adressée à l'Entité.</summary>
public sealed class DemandesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An + 10 * 3600;

    private static Humain Humain(int id, Alea a)
    {
        var e = Carte.Emplacements[0];
        double T() => 0.2 + 0.6 * a.Reel();
        return new Humain
        {
            Id = id, Nom = "H" + id, Sexe = id % 2 == 0 ? Sexe.Femme : Sexe.Homme, PartenaireId = 0, Emplacement = 1,
            Naissance = Maintenant - 30 * An,
            Personnalite = new Personnalite(T(), T(), T(), T(), T(), T(), T(), T(), Intelligence.Moyenne),
            Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Femme : Sexe.Homme), X = e.X, Y = e.Y, Humeur = 60,
            Croyance = 80, Amour = 60,
        };
    }

    private static (Comportement C, List<Humain> Tous) Monde(int n, ulong graine, Action<Humain>? etat = null)
    {
        var a = new Alea(graine);
        var tous = Enumerable.Range(1, n).Select(i => Humain(i, a)).ToList();
        foreach (var h in tous) etat?.Invoke(h);
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(graine)) { Social = new VieSociale(new Alea(graine)), Cultes = new Cultes() };
        return (c, tous);
    }

    [Fact]
    public void Prie_CeluiQuiObeitSAgenouilleEtPrieVraiment_ToutDeSuite()
    {
        var (c, tous) = Monde(60, 7);
        foreach (var h in tous) c.Parler(h, Parole.Prie, ModeParole.Voix, null, 0, tous, Maintenant, dialogue: 1, croyait: true);
        var prient = tous.Count(h => h.Activite == Activite.Prier);
        // Des croyants qui aiment l'Entité : la plupart obéissent, et chacun de ceux-là a déposé une vraie prière.
        Assert.True(prient >= 30, $"{prient} prient");
        Assert.True(prient < 60, "tous obéissent : la volonté ne compte pas");
        Assert.Equal(prient, c.Cultes!.Prieres.Count);
        Assert.All(c.Cultes.Prieres, p => Assert.Equal(Activite.Prier, tous[p.HumainId - 1].Activite));
        // La décision et ses raisons vont au journal.
        Assert.Contains(c.Journal, j => j.Recit?.Cle == "ent.obeitPour");
        Assert.Contains(c.Journal, j => j.Recit?.Cle == "dem.fait.prie");
    }

    [Fact]
    public void EnReve_IlLeFeraAuReveil_PasToutDeSuite()
    {
        var (c, tous) = Monde(30, 11);
        foreach (var h in tous)
        {
            h.Activite = Activite.Dormir;
            c.Parler(h, Parole.Prie, ModeParole.Reve, null, 0, tous, Maintenant, dialogue: 1, croyait: true);
        }
        Assert.All(tous, h => Assert.Equal(Activite.Dormir, h.Activite));
        Assert.Empty(c.Cultes!.Prieres);
        Assert.Contains(tous, h => h.Impulsions.Exists(i => i.Parole == Parole.Prie && i.Reponse == Reponse.Acceptee && i.Param == 0));
    }

    [Fact]
    public void LaVolonteDuMoment_LaColereLaFaimEtLaFatigueRetiennent()
    {
        static int Obeissent(List<Humain> tous) =>
            tous.Count(h => h.Impulsions.Exists(i => i.Reponse is Reponse.Acceptee or Reponse.AMoitie or Reponse.Differee));
        var (calmes, tousCalmes) = Monde(80, 21);
        var (epuises, tousEpuises) = Monde(80, 21, h =>
        {
            h.Colere = 85;
            h.Faim = 80;
            h.Fatigue = 90;
        });
        foreach (var h in tousCalmes) calmes.Parler(h, Parole.VaChercherAManger, ModeParole.Voix, null, 0, tousCalmes, Maintenant, dialogue: 1, croyait: true);
        foreach (var h in tousEpuises) epuises.Parler(h, Parole.VaChercherAManger, ModeParole.Voix, null, 0, tousEpuises, Maintenant, dialogue: 1, croyait: true);
        var (a, b) = (Obeissent(tousCalmes), Obeissent(tousEpuises));
        Assert.True(a > b + 10, $"calmes {a}, épuisés et en colère {b}");
        // Les refus disent leurs raisons : sa colère, sa fatigue…
        var refus = epuises.Journal.Where(j => j.Recit?.Cle == "ent.refusePour").ToList();
        Assert.NotEmpty(refus);
        Assert.Contains(refus, j => j.Texte.Contains("colère") || j.Texte.Contains("fatigue"));
    }

    [Fact]
    public void CeQuIlNeSaitPasFaire_IlNeLePeutPas()
    {
        var (c, tous) = Monde(20, 31);
        foreach (var h in tous) c.Parler(h, Parole.VeilleLeFeu, ModeParole.Apparition, null, 0, tous, Maintenant, dialogue: 1, croyait: true);
        // Sans feu ni savoir du feu : personne ne le fait.
        Assert.DoesNotContain(tous, h => h.Activite == Activite.Veiller);
        Assert.All(tous, h => Assert.Equal(Reponse.Refusee, h.Impulsions[^1].Reponse));
    }

    [Fact]
    public void LaReponse_EstCeQuIlDitALEntite_SesMotsDansSaLangueAPart()
    {
        Assert.True(Dialogues.Lire("""{"reponse":"Je t'entends. Je m'assois.","mots":"*s'assoit*","pensee":"Elle est revenue."}""",
            out var mots, out var pensee, out var sens, out _, out _));
        Assert.Equal(("*s'assoit*", "Elle est revenue.", "Je t'entends. Je m'assois."), (mots, pensee, sens));
        // Les réponses d'avant la 0.42.8 (« sens ») se lisent encore.
        Assert.True(Dialogues.Lire("""{"pensee":"…","mots":"*gémit*","sens":"J'ai peur."}""", out _, out _, out var ancien, out _, out _));
        Assert.Equal("J'ai peur.", ancien);
    }

    [Theory]
    [InlineData("Assieds-toi et prie.", "AssiedsToi,Prie")]
    [InlineData("Ana, je suis là. Assois-toi, puis prie pour les tiens !", "AssiedsToi,Prie")]
    [InlineData("Sit down and pray.", "AssiedsToi,Prie")]
    [InlineData("Voici ce que tu dois faire : Enseigne le son sacré à ta communauté.", "Transmets")]
    [InlineData("Tell your people what I said.", "Transmets")]
    [InlineData("Va chercher à manger, puis dors.", "VaChercherAManger,Dors")]
    [InlineData("Je t'en prie, écoute-moi.", "")]
    [InlineData("Veux-tu prier avec moi ?", "")]
    [InlineData("Ne prie pas maintenant.", "")]
    [InlineData("Ramasse du bois sec.", "")]
    [InlineData("Bois à la rivière, puis chante.", "Bois,Chante")]
    [InlineData("Jeûne jusqu'à la lune nouvelle. Tu es jeune encore.", "Jeune")]
    public void LesGestesDitsEnToutesLettres_SontLus(string texte, string attendus)
    {
        Assert.Equal(attendus, string.Join(",", GestesLus.Lire(texte)));
    }
}
