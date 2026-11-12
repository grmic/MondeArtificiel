using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.39 : fondateurs plus jeunes, l'Entité-LLM et l'état périmé, les questions, les récits nés des catastrophes.</summary>
public sealed class Version039Tests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;

    // ───────────── Fondateurs (0.39.0) ─────────────

    /// <summary>Âges des fondateurs : peu de couples à la limite des 40 ans ; une femme sur deux a moins de 27 ans environ.</summary>
    [Fact]
    public void AgesDuCouple_SuiventLaPyramideDesChasseursCueilleurs()
    {
        var alea = new Alea(7);
        var femmes = new List<double>();
        var hommes = new List<double>();
        for (var i = 0; i < 20_000; i++)
        {
            var (f, h) = GenerateurPopulation.AgesDuCouple(alea);
            femmes.Add(f);
            hommes.Add(h);
        }
        Assert.All(femmes.Concat(hommes), a => Assert.InRange(a, 14, 40.0001));
        femmes.Sort();
        Assert.InRange(femmes[femmes.Count / 2], 23, 28);
        // Avant la 0.39.0, l'âge de l'homme était coupé à 40 ans : un pic de trentenaires sans enfant.
        Assert.True(hommes.Count(a => a >= 35) < 0.25 * hommes.Count);
        Assert.True(hommes.Count(a => a >= 39) < 0.06 * hommes.Count);
    }

    [Fact]
    public void Fondateurs_PlusJeunesEnGeneration4_InchangesAvant()
    {
        static double Age(Humain h) => -h.Naissance / (double)An;
        var g3 = GenerateurPopulation.Generer(Carte, 42, 0, 3);
        var g0 = GenerateurPopulation.Generer(Carte, 42, 0, 0);
        Assert.Equal(g0.Select(Age), g3.Select(Age));
        var g4 = GenerateurPopulation.Generer(Carte, 42, 0, 4);
        Assert.Equal(g3.Count, g4.Count);
        Assert.True(g4.Average(Age) < g3.Average(Age));
        Assert.True(g4.Count(h => Age(h) >= 35) < g3.Count(h => Age(h) >= 35));
    }

    // ───────────── L'Entité-LLM décide sur un état qui a changé (0.39.0) ─────────────

    [Fact]
    public void Perime_CommunauteMaladieEtDirection()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var (maintenant, humains) = monde.InstantaneHumains();
        var h = humains.First(x => x.EstVivant && !x.EstEnfant(maintenant));
        var vu = new EntiteLlm.VuHumain(h.X, h.Y, true, h.CommunauteId, h.EstMalade(maintenant));
        var parler = new ActeLlm { Type = Actes.Parler, Humains = [h.Id], Parole = nameof(Parole.PartsVers), Param = 2 };
        Assert.Null(EntiteLlm.Perime(parler, new Dictionary<int, EntiteLlm.VuHumain> { [h.Id] = vu }, monde));

        var ailleurs = vu with { Communaute = h.CommunauteId + 1 };
        Assert.Equal(L.T("llm.perime.communaute", ("nom", h.Nom)),
            EntiteLlm.Perime(parler, new Dictionary<int, EntiteLlm.VuHumain> { [h.Id] = ailleurs }, monde));

        var loin = vu with { X = h.X + 40 };
        Assert.Equal(L.T("llm.perime.parti", ("nom", h.Nom)), EntiteLlm.Perime(parler, new Dictionary<int, EntiteLlm.VuHumain> { [h.Id] = loin }, monde));
        // Sans direction, peu importe où il est.
        Assert.Null(EntiteLlm.Perime(parler with { Parole = nameof(Parole.NAiePasPeur), Param = null },
            new Dictionary<int, EntiteLlm.VuHumain> { [h.Id] = loin }, monde));

        var geste = new ActeLlm { Type = Actes.Geste, Humain = h.Id, Geste = "Guerir" };
        var autreEtat = vu with { Malade = !vu.Malade };
        Assert.NotNull(EntiteLlm.Perime(geste, new Dictionary<int, EntiteLlm.VuHumain> { [h.Id] = autreEtat }, monde));
        Assert.Null(EntiteLlm.Perime(geste, new Dictionary<int, EntiteLlm.VuHumain> { [h.Id] = vu }, monde));
    }

    // ───────────── Les questions (0.39.0) ─────────────

    [Fact]
    public void Extrait_UnOrdreSuiviDUneCourteQueueDeQuestion()
    {
        Assert.Equal(2, Dialogues.Extrait("Va vers l'est, veux-tu ?", "Va vers l'est"));
        Assert.Equal(0, Dialogues.Extrait("Va vers l'est, veux-tu ?", "Va vers l'est", ordreSelonLeLlm: true));
        // Une vraie question reste une question, même si le LLM s'y trompe.
        Assert.Equal(2, Dialogues.Extrait("Est-ce que tu vas vers l'est ?", "tu vas vers l'est", ordreSelonLeLlm: true));
        Assert.Equal(2, Dialogues.Extrait("Va vers l'est ou reste ici avec les tiens, qu'en penses-tu ?", "Va vers l'est", ordreSelonLeLlm: true));
    }

    [Fact]
    public void LireTraduction_IgnoreUneQuestionSansPointDInterrogation()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var (_, humains) = monde.InstantaneHumains();
        var fautes = new List<string>();
        Assert.Empty(Dialogues.LireTraduction("""{"paroles":[{"parole":"NAiePasPeur","mots":"tu n'as pas peur","question":true}]}""",
            "Alors tu n'as pas peur.", 1, ModeParole.Reve, humains, fautes));
        Assert.Empty(fautes);
        Assert.Single(Dialogues.LireTraduction("""{"paroles":[{"parole":"NAiePasPeur","mots":"N'aie pas peur","question":false}]}""",
            "N'aie pas peur, d'accord ?", 1, ModeParole.Reve, humains, fautes));
    }

    // ───────────── Récits nés des catastrophes (0.39.0) ─────────────

    private static Humain Humain(int id, double transcendance = 0.5, double nevrosisme = 0.5, double ouverture = 0.5, double crainte = 0) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = Sexe.Femme,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = -30 * An,
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, 0.5, nevrosisme, transcendance, 0.5, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        Sante = 100,
        Crainte = crainte,
    };

    private static Catastrophe Cat(TypeCatastrophe type) => new() { Id = 1, Type = type, Debut = 0, Nom = "x", X = 20, Y = 20, Rayon = 6 };

    [Fact]
    public void Interpreter_SelonLeCaractere()
    {
        // L'anxieux qui craint l'invisible voit une faute après des morts.
        Assert.Equal(Explication.Colere, Comportement.Interpreter(Humain(1, transcendance: 0.9, nevrosisme: 0.9, ouverture: 0.2, crainte: 60), Cat(TypeCatastrophe.Crue), 3).E);
        // Une éclipse est un présage pour qui a le sens de l'invisible.
        Assert.Equal(Explication.Presage, Comportement.Interpreter(Humain(2, transcendance: 0.9, nevrosisme: 0.6), Cat(TypeCatastrophe.Eclipse), 0).E);
        // Le curieux et serein voit un esprit à apaiser.
        Assert.Equal(Explication.Esprit, Comportement.Interpreter(Humain(3, transcendance: 0.9, nevrosisme: 0.1, ouverture: 0.95), Cat(TypeCatastrophe.Seisme), 0).E);
        // On croit plus volontiers ce qu'on a vécu.
        var porteur = Humain(9);
        var h = Humain(4);
        Assert.True(Comportement.ChanceDeCroire(h, Explication.Colere, Cat(TypeCatastrophe.Crue), 2, true, true, porteur)
                    > Comportement.ChanceDeCroire(h, Explication.Colere, Cat(TypeCatastrophe.Crue), 2, false, false, porteur));
    }

    [Fact]
    public void Registre_SignesTabouEtLieuInterdit()
    {
        var a = Humain(1);
        var b = Humain(2);
        var c = Humain(3);
        var signes = new RecitCatastrophe
        {
            Id = 1, CommunauteId = 5, CatastropheId = 10, Type = TypeCatastrophe.Tsunami, Explication = Explication.Esprit, NeLe = 0,
            X = 20, Y = 20, Rayon = 4, Lieu = true, Signes = true, Croyants = [1, 2],
        };
        var tabou = new RecitCatastrophe
        {
            Id = 2, CommunauteId = 5, CatastropheId = 11, Type = TypeCatastrophe.Epidemie, Explication = Explication.Colere, NeLe = 0,
            X = 50, Y = 50, Rayon = 3, Tabou = true, Croyants = [3],
        };
        var registre = new RecitsCatastrophes([signes, tabou]);
        Assert.True(registre.Averti(1, TypeCatastrophe.Tsunami));
        Assert.False(registre.Averti(1, TypeCatastrophe.Crue));
        Assert.False(registre.Averti(3, TypeCatastrophe.Tsunami));
        Assert.True(registre.Tabou(3, 52, 49));
        Assert.False(registre.Tabou(3, 60, 49));
        Assert.False(registre.Tabou(1, 52, 49));
        // La moitié des membres y croient : on n'y campe plus ; un seul sur trois : on y campe encore.
        Assert.Same(signes, registre.Interdit(5, 22, 18, [a, b, c]));
        Assert.Null(registre.Interdit(5, 22, 18, [a, c, Humain(4)]));
        Assert.Null(registre.Interdit(5, 30, 18, [a, b, c]));
        Assert.Null(registre.Interdit(6, 22, 18, [a, b, c]));
        // Oublié : plus rien.
        signes.OublieLe = 5;
        registre.Indexer();
        Assert.False(registre.Averti(1, TypeCatastrophe.Tsunami));
        Assert.Null(registre.Interdit(5, 22, 18, [a, b, c]));
    }

    /// <summary>
    /// Des séismes au cœur des camps : des récits naissent, avec au moins deux croyants et leur ligne au journal, et survivent à la
    /// sauvegarde.
    /// </summary>
    [Fact]
    public void Catastrophes_FontNaitreDesRecits_QuiSurviventALaSauvegarde()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-recits-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(An);
            foreach (var c in monde.Communautes().Where(c => c.Active))
                monde.Declencher(TypeCatastrophe.Seisme, c.CentreX, c.CentreY, 7.5, Origine.Naturelle);
            monde.AvancerDe(30 * Jour);
            var recits = monde.RecitsCatastrophes();
            var diag = string.Join(" | ", monde.CatastrophesEnMemoire().Select(c => $"{c.Id} {c.Type} fin={c.Fin} morts={c.Morts} temoins={c.Temoins.Count} comms={string.Join(",", c.Communautes)} pertes={string.Join(",", c.PertesParCommunaute)}"))
                + " || " + string.Join(" ", monde.Communautes().Where(c => c.Active).Select(c => $"{c.Id}@{c.CentreX},{c.CentreY}"));
            Assert.True(recits.Count > 0, diag);
            Assert.All(recits, r =>
            {
                Assert.True(r.Croyants.Count >= 2);
                Assert.True(r.EvenementId > 0);
                Assert.True(r.Signes);
            });
            var journal = monde.JournalRecent();
            Assert.Contains(journal, e => e.Recit?.Cle.StartsWith("recit.ne.", StringComparison.Ordinal) == true);

            Directory.CreateDirectory(dossier);
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(monde.Etat);
            var relu = depot.Charger()!.RecitsCatastrophes!;
            Assert.Equal(recits.Select(r => (r.Id, r.Explication, r.Croyants.Count, r.Signes, r.PorteurId)),
                relu.Select(r => (r.Id, r.Explication, r.Croyants.Count, r.Signes, r.PorteurId)));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
