using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.36.2 : le dialogue direct entre l'Entité et un humain joué par le LLM.</summary>
public sealed class DialoguesTests
{
    [Fact]
    public void Ecouter_LesReglesDecident_EtLeDialogueFinitParLesRegles()
    {
        var config = ConfigurationMonde.Nouvelle(3);
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)) { Coeurs = 1 };
        monde.AvancerDe(2 * Calendrier.SecondesParJour);
        var (_, humains) = monde.InstantaneHumains();
        var adultes = humains.Where(h => h.EstVivant && h.AgeEnAnnees(monde.Secondes) >= 18).Take(30).ToList();
        int fins = 0, croient = 0, comprises = 0;
        foreach (var h in adultes)
        {
            bool? croit = null;
            var fin = FinDialogue.Aucune;
            var rang = 0;
            var comprise = false;
            // Le dialogue est une seule manifestation : elle l'ouvre, au journal.
            var reception = monde.Manifester(h.Id, ModeParole.Reve, "Qui es-tu ?");
            Assert.True(reception > 0);
            while (fin == FinDialogue.Aucune && rang < 40)
            {
                rang++;
                var r = monde.Ecouter(h.Id, ModeParole.Reve, rang, croit, [], reception);
                comprise |= r.Comprise;
                if (r.Comprise || croit is not null) croit = r.Croit;
                fin = r.Fin;
            }
            if (fin != FinDialogue.Aucune) fins++;
            if (croit == true) croient++;
            if (comprise) comprises++;
            var ligne = monde.FinirDialogue(h.Id, ModeParole.Reve, rang, croit == true, comprise, fin == FinDialogue.Aucune ? FinDialogue.Joueur : fin, 0);
            Assert.True(ligne > 0);
            Assert.StartsWith("dlg.fin.", monde.EvenementEnMemoire(ligne)!.Recit!.Cle);
            Assert.Contains(monde.InstantaneHumains().Humains[h.Id - 1].Memoire.Jardin, j => j.Cle == "entite:dialogue");
        }
        // Un rêve finit toujours par un réveil (ou un incrédule qui se détourne) ; certains y croient, presque tous comprennent un moment.
        Assert.True(fins >= adultes.Count * 0.9, $"{fins} fins sur {adultes.Count}");
        Assert.True(croient > 0, "personne n'y croit");
        Assert.True(comprises >= adultes.Count * 0.8, $"{comprises} ont compris");
        // Un numéro inconnu est refusé.
        Assert.Throws<ArgumentException>(() => monde.Ecouter(99999, ModeParole.Voix, 1, null, [], 0));
    }

    [Fact]
    public void LireTraduction_GardeLesParolesValides_EtNommeLesFautes()
    {
        var config = ConfigurationMonde.Nouvelle(5);
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config));
        var (_, humains) = monde.InstantaneHumains();
        var fautes = new List<string>();
        const string texte = "N'aie pas peur, je suis là. Fais la guerre ! Unis-toi à lui.";
        var r = Dialogues.LireTraduction("""{"paroles":[{"parole":"naiepaspeur","mots":"N'aie pas peur"},{"parole":"FaitesLaGuerre","mots":"Fais la guerre"},{"parole":"UnisToi","mots":"Unis-toi","cible":999999},{"parole":"Inventee","mots":"je suis là"}]}""",
            texte, 1, ModeParole.Voix, humains, fautes);
        var a = Assert.Single(r);
        Assert.Equal((Actes.Parler, nameof(Parole.NAiePasPeur), nameof(ModeParole.Voix)), (a.Type, a.Parole, a.Mode));
        Assert.Equal([1], a.Humains!);
        Assert.Equal(3, fautes.Count);
        // Une simple conversation : rien à traduire, aucune faute.
        fautes.Clear();
        Assert.Empty(Dialogues.LireTraduction("""{"paroles":[]}""", "Qui aimes-tu ?", 1, ModeParole.Reve, humains, fautes));
        Assert.Empty(fautes);
        // Une question n'est pas un ordre, et une parole doit venir de ses mots.
        Assert.Empty(Dialogues.LireTraduction("""{"paroles":[{"parole":"TuNEsPasSeul","mots":"Qui aimes-tu"},{"parole":"NAiePasPeur","mots":"Tu n'es pas seul"}]}""",
            "Qui aimes-tu ?", 1, ModeParole.Reve, humains, fautes));
        Assert.Equal(2, fautes.Count);
        Assert.Equal(2, Dialogues.Extrait("Qui aimes-tu ?", "qui aimes tu"));
        Assert.Equal(1, Dialogues.Extrait("Qui aimes-tu ?", "Tu n'es pas seul"));
        Assert.Equal(0, Dialogues.Extrait("Qui aimes-tu ? Tu n'es pas seul.", "tu n’es pas seul"));
        // Celui qu'un ordre désigne doit être nommé : sinon le numéro est deviné.
        var autre = humains.First(h => h.Id != 1 && h.EstVivant && h.AgeEnAnnees(monde.Secondes) >= 18);
        fautes.Clear();
        var json = $$"""{"paroles":[{"parole":"UnisToi","mots":"Unis-toi","cible":{{autre.Id}}}]}""";
        Assert.Empty(Dialogues.LireTraduction(json, "Unis-toi à lui.", 1, ModeParole.Voix, humains, fautes, monde));
        Assert.Contains(autre.Nom, Assert.Single(fautes));
        fautes.Clear();
        Assert.Single(Dialogues.LireTraduction(json, $"Unis-toi à {autre.Nom}.", 1, ModeParole.Voix, humains, fautes, monde));
        // Une direction manquante pour « Pars vers » est une faute, pas le nord par défaut.
        Assert.Empty(Dialogues.LireTraduction("""{"paroles":[{"parole":"PartsVers","mots":"Pars"}]}""", "Pars !", 1, ModeParole.Voix, humains, fautes, monde));
        // Au plus deux paroles.
        Assert.Equal(Dialogues.ParolesMax, Dialogues.LireTraduction(
            """{"paroles":[{"parole":"NAiePasPeur","mots":"pas peur"},{"parole":"TuNEsPasSeul","mots":"pas seul"},{"parole":"TaPeinePassera","mots":"passera"}]}""",
            "N'aie pas peur. Tu n'es pas seul. Ta peine passera.", 1, ModeParole.Reve, humains, fautes).Count);
    }

    [Fact]
    public void ReactionLibre_NeToucheQueLuiEtSonCoeur()
    {
        Assert.True(Dialogues.Lire("""{"pensee":"Je tremble.","mots":"Qui es-tu ?","changements":[{"quoi":"peur","humain":4,"valeur":70}]}""",
            out var mots, out var pensee, out var acte, out _));
        Assert.Equal(("Qui es-tu ?", "Je tremble."), (mots, pensee));
        Assert.NotNull(acte);
        var config = ConfigurationMonde.Nouvelle(5);
        var humains = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)).InstantaneHumains().Humains;
        Assert.Null(Dialogues.VerifierLibre(acte!, 4, humains));
        Assert.NotNull(Dialogues.VerifierLibre(acte!, 5, humains));
        Assert.NotNull(Dialogues.VerifierLibre(acte! with { Changements = [new ChangementLibre("mourir", 4)] }, 4, humains));
        // Un lien sans l'autre humain est refusé avant d'arriver aux règles ; un changement sans numéro est le sien.
        Assert.NotNull(Dialogues.VerifierLibre(acte! with { Changements = [new ChangementLibre("lien", 4) { Valeur = 30 }] }, 4, humains));
        Assert.Null(Dialogues.VerifierLibre(acte! with { Changements = [new ChangementLibre("lien", 4) { Autre = 6, Valeur = 30 }] }, 4, humains));
        // Sa réaction libre ne contredit pas les règles : s'il n'y croit pas, sa croyance ne monte pas.
        var h4 = humains[3];
        var monte = acte! with { Changements = [new ChangementLibre("croyance", 4) { Valeur = Math.Min(100, h4.Croyance + 30) }] };
        if (h4.Croyance < 100) Assert.NotNull(Dialogues.Contredit(monte, h4, croit: false));
        Assert.Null(Dialogues.Contredit(monte, h4, croit: true));
        var complete = Dialogues.Completer(acte! with { Changements = [new ChangementLibre("peur", 0) { Valeur = 10 }] }, 4);
        Assert.Equal(4, complete.Changements![0].Humain);
        Assert.False(Dialogues.Lire("pas du JSON", out _, out _, out _, out _));
    }

    [Fact]
    public void DansUnDialogue_LaFoiNEstPasRetiree_EtRejoindreEstUnVoyage()
    {
        var config = ConfigurationMonde.ParDefaut(2);
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)) { Coeurs = 1 };
        monde.AvancerDe(3 * 360 * Calendrier.SecondesParJour);
        var (_, humains) = monde.InstantaneHumains();
        var adultes = humains.Where(h => h.EstVivant && h.AgeEnAnnees(monde.Secondes) >= 18).Take(20).ToList();
        foreach (var h in adultes)
        {
            var reception = monde.Manifester(h.Id, ModeParole.Voix, "Écoute-moi.");
            // Il y croit déjà : aucune parole du dialogue n'est « pas crue ».
            for (var i = 0; i < 3; i++)
                Assert.NotEqual("ent.r.incrue", monde.ParlerEnDialogue(h.Id, Parole.NAiePasPeur, ModeParole.Voix, 0, 0, reception, true).Cle);
        }
        // Rejoindre une autre communauté : il part vers son camp, il n'y est pas posé.
        var communautes = monde.Communautes().Where(c => c.Active).ToList();
        if (communautes.Count >= 2)
        {
            var (_, tous) = monde.InstantaneHumains();
            var h = tous.First(x => x.EstVivant && x.AgeEnAnnees(monde.Secondes) >= 18 && x.CommunauteId == communautes[0].Id);
            var (x0, y0) = (h.X, h.Y);
            Assert.True(monde.PartirRejoindre(h.Id, communautes[1].Id, 0) > 0);
            var apres = monde.InstantaneHumains().Humains[h.Id - 1];
            Assert.Equal((communautes[0].Id, x0, y0), (apres.CommunauteId, apres.X, apres.Y));
            Assert.Contains(apres.Impulsions, i => i.Parole == Parole.PartsVers && i.Reponse == Reponse.Acceptee);
        }
    }

    [Fact]
    public void ReactionLibre_EstBorneeParSonCaractere_EtLaSortieResserree()
    {
        var config = ConfigurationMonde.Nouvelle(5);
        var humains = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)).InstantaneHumains().Humains;
        var h = humains[3];
        var acte = new ActeLlm { Type = Actes.Libre, Changements = [new ChangementLibre("peur", 4) { Valeur = 100 }, new ChangementLibre("croyance", 4) { Valeur = 0 }] };
        var b = Dialogues.Borner(acte, h, humains).Changements!;
        Assert.True(b[0].Valeur <= h.Peur + 50.0001 && b[0].Valeur >= h.Peur);
        Assert.True(b[1].Valeur >= h.Croyance - 50.0001 && b[1].Valeur <= h.Croyance);
        Assert.Equal("Fécondité Fécondité", ClientLlm.Resserrer("Fécondité Fécondité Fécondité Fécondité Fécondité", 2000));
        Assert.Equal("Le feu, le feu", ClientLlm.Resserrer("Le feu, le feu, le feu, le feu,", 2000));
    }

    [Fact]
    public void LHumainParleSaLangue_SansMotInvente()
    {
        var config = ConfigurationMonde.Nouvelle(5);
        var humains = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)).InstantaneHumains().Humains;
        var h = humains[0];
        var permis = Dialogues.Permis(h, humains);
        // Son nom et les gestes entre astérisques sont toujours permis ; un mot inventé ou du français ne l'est pas.
        Assert.Empty(Dialogues.Inconnus($"{h.Nom} ! *montre le feu*", permis));
        Assert.Equal(["shiadiavo", "feu"], Dialogues.Inconnus("shiadiavo feu *gémit*", permis));
        Assert.True(Dialogues.Lire("""{"pensee":"…","mots":"*gémit*","sens":"J'ai peur."}""", out var mots, out _, out var sens, out _, out _));
        Assert.Equal(("*gémit*", "J'ai peur."), (mots, sens));
    }

    private sealed record ConnexionLue(string Modele);
    private sealed record EtatLu(ConnexionLue Connexion);
    private sealed record RepliqueLue(int Rang, string Entite, List<string> Traduction, List<string> Regles, string Mots, string Pensee, string? Erreur);
    private sealed record DialogueLu(long Id, int Humain, string Mode, bool EnCours, string? Attend, int Repliques, List<RepliqueLue> Liste, string? FinTexte, string? Erreur);

    [Fact]
    public async Task Dialogue_TraduitAppliqueFaitRepondre_EtArreteLHorloge()
    {
        using var usine = new UsineTest();
        using var llm = new FauxLlm(
            """{"paroles":[{"parole":"NAiePasPeur","mots":"N'aie pas peur"}]}""",
            """{"pensee":"Cette voix me connaît.","mots":"*regarde autour* *tremble*","sens":"Qui es-tu ?"}""",
            """{"coherent":true,"raison":""}""",
            """{"paroles":[]}""",
            """{"pensee":"Je veux rentrer.","mots":"*recule*","sens":"Laisse-moi."}""",
            """{"coherent":true,"raison":""}""");
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        var humain = service.Monde.InstantaneHumains().Humains.First(h => h.EstVivant && h.AgeEnAnnees(service.Monde.Secondes) >= 20).Id;

        // Sans modèle, pas de dialogue (le LLM joue l'humain).
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<DialogueLu>("OuvrirDialogue", humain, "Voix", 0, false));
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 16384 });
        // Un monde neuf ne se sert pas du LLM tant qu'on ne l'a pas choisi (0.38.1).
        await c.InvokeAsync<EtatLu>("ReglerUsageLlm", "Tout");
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<DialogueLu>("OuvrirDialogue", humain, "Signe", 0, false));

        var d = await c.InvokeAsync<DialogueLu>("OuvrirDialogue", humain, "Voix", 0, false);
        Assert.True(d.EnCours);
        Assert.True(service.Monde.EnDialogue);
        d = await c.InvokeAsync<DialogueLu>("DireAuDialogue", d.Id, "N'aie pas peur, je suis là.");
        d = await Attendre(c, d.Id, x => x.Attend is null && x.Repliques >= 1);
        var r = Assert.Single(d.Liste);
        Assert.Null(r.Erreur);
        Assert.Equal("*regarde autour* *tremble*", r.Mots);
        Assert.Single(r.Traduction);
        Assert.True(r.Regles.Count >= 2, string.Join(" | ", r.Regles));
        // La parole codée est passée par les règles des paroles et notée au registre.
        Assert.Contains(service.Depot.Actes(new Monde.Stockage.FiltreActes(Limite: 20)), a => a.Sorte == Actes.Parler && a.Humain == humain);
        // Le LLM qui fait parler l'humain a lu sa fiche et ce que les règles ont décidé.
        var lu = llm.Recus[1].ToJsonString();
        Assert.Contains("pas peur", lu);

        if (d.EnCours)
        {
            await c.InvokeAsync<DialogueLu>("DireAuDialogue", d.Id, "Comment vont les tiens ?");
            d = await Attendre(c, d.Id, x => x.Attend is null && x.Repliques >= 2);
            Assert.Empty(d.Liste[1].Traduction);
        }
        if (d.EnCours) d = await c.InvokeAsync<DialogueLu>("TerminerDialogue", d.Id);
        Assert.False(d.EnCours);
        Assert.NotNull(d.FinTexte);
        Assert.False(service.Monde.EnDialogue);
        Assert.Contains(service.Monde.InstantaneHumains().Humains[humain - 1].Memoire.Jardin, j => j.Cle == "entite:dialogue");
        var liste = await c.InvokeAsync<List<DialogueLu>>("ListeDialogues", humain);
        Assert.Single(liste);
    }

    private static async Task<DialogueLu> Attendre(HubConnection c, long id, Func<DialogueLu, bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            var d = await c.InvokeAsync<DialogueLu>("LireDialogue", id);
            if (condition(d)) return d;
            await Task.Delay(50);
        }
        throw new TimeoutException("Le dialogue n'a pas avancé.");
    }
}
