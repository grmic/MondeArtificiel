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
            while (fin == FinDialogue.Aucune && rang < 40)
            {
                rang++;
                var r = monde.Ecouter(h.Id, ModeParole.Reve, rang, croit, [], "Qui es-tu ?");
                comprise |= r.Comprise;
                if (r.Comprise || croit is not null) croit = r.Croit;
                fin = r.Fin;
                if (rang == 1) Assert.True(r.Reception > 0);
                else Assert.Equal(0, r.Reception);
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
        Assert.Throws<ArgumentException>(() => monde.Ecouter(99999, ModeParole.Voix, 1, null, [], "?"));
    }

    [Fact]
    public void LireTraduction_GardeLesParolesValides_EtNommeLesFautes()
    {
        var config = ConfigurationMonde.Nouvelle(5);
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config));
        var (_, humains) = monde.InstantaneHumains();
        var fautes = new List<string>();
        var r = Dialogues.LireTraduction("""{"paroles":[{"parole":"naiepaspeur"},{"parole":"FaitesLaGuerre"},{"parole":"UnisToi","cible":999999},{"parole":"Inventee"}]}""",
            1, ModeParole.Voix, humains, fautes);
        var a = Assert.Single(r);
        Assert.Equal((Actes.Parler, nameof(Parole.NAiePasPeur), nameof(ModeParole.Voix)), (a.Type, a.Parole, a.Mode));
        Assert.Equal([1], a.Humains!);
        Assert.Equal(3, fautes.Count);
        // Une simple conversation : rien à traduire, aucune faute.
        fautes.Clear();
        Assert.Empty(Dialogues.LireTraduction("""{"paroles":[]}""", 1, ModeParole.Reve, humains, fautes));
        Assert.Empty(fautes);
        // Au plus deux paroles.
        Assert.Equal(Dialogues.ParolesMax, Dialogues.LireTraduction(
            """{"paroles":[{"parole":"NAiePasPeur"},{"parole":"TuNEsPasSeul"},{"parole":"TaPeinePassera"}]}""", 1, ModeParole.Reve, humains, fautes).Count);
    }

    [Fact]
    public void ReactionLibre_NeToucheQueLuiEtSonCoeur()
    {
        Assert.True(Dialogues.Lire("""{"pensee":"Je tremble.","mots":"Qui es-tu ?","changements":[{"quoi":"peur","humain":4,"valeur":70}]}""",
            out var mots, out var pensee, out var acte, out _));
        Assert.Equal(("Qui es-tu ?", "Je tremble."), (mots, pensee));
        Assert.NotNull(acte);
        Assert.Null(Dialogues.VerifierLibre(acte!, 4));
        Assert.NotNull(Dialogues.VerifierLibre(acte!, 5));
        Assert.NotNull(Dialogues.VerifierLibre(acte! with { Changements = [new ChangementLibre("mourir", 4)] }, 4));
        Assert.False(Dialogues.Lire("pas du JSON", out _, out _, out _, out _));
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
            """{"paroles":[{"parole":"NAiePasPeur"}]}""",
            """{"pensee":"Cette voix me connaît.","mots":"Qui es-tu ? *regarde autour*"}""",
            """{"coherent":true,"raison":""}""",
            """{"paroles":[]}""",
            """{"pensee":"Je veux rentrer.","mots":"Laisse-moi."}""",
            """{"coherent":true,"raison":""}""");
        var c = usine.CreerConnexion();
        await using var _ = c;
        await c.StartAsync();
        var service = usine.Services.GetRequiredService<ServiceMonde>();
        var humain = service.Monde.InstantaneHumains().Humains.First(h => h.EstVivant && h.AgeEnAnnees(service.Monde.Secondes) >= 20).Id;

        // Sans modèle, pas de dialogue (le LLM joue l'humain).
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<DialogueLu>("OuvrirDialogue", humain, "Voix", 0));
        await c.InvokeAsync<EtatLu>("ReglerConnexionLlm", new { fournisseur = "ollama", adresse = llm.Adresse, modele = "faux:1b", contexte = 16384 });
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync<DialogueLu>("OuvrirDialogue", humain, "Signe", 0));

        var d = await c.InvokeAsync<DialogueLu>("OuvrirDialogue", humain, "Voix", 0);
        Assert.True(d.EnCours);
        Assert.True(service.Monde.EnDialogue);
        d = await c.InvokeAsync<DialogueLu>("DireAuDialogue", d.Id, "N'aie pas peur, je suis là.");
        d = await Attendre(c, d.Id, x => x.Attend is null && x.Repliques >= 1);
        var r = Assert.Single(d.Liste);
        Assert.Null(r.Erreur);
        Assert.Equal("Qui es-tu ? *regarde autour*", r.Mots);
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
