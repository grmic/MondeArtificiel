using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Entite;
using Monde.Simulation.Humains;
using Xunit;

namespace Monde.Tests;

/// <summary>0.38.1 : la vraie cause des morts d'hiver, et « Utiliser le LLM ».</summary>
public class SoifEtUsageLlmTests
{
    /// <summary>Un corps décharné qui meurt aussi assoiffé est noté mort de faim ; un corps encore nourri, de soif.</summary>
    [Fact]
    public void CauseRetenue_FaimSurUnCorpsDecharne()
    {
        Assert.Equal(CauseDeMort.Faim, Comportement.CauseRetenue(CauseDeMort.Soif, affame: true, reserves: 5));
        Assert.Equal(CauseDeMort.Soif, Comportement.CauseRetenue(CauseDeMort.Soif, affame: true, reserves: 44));
        Assert.Equal(CauseDeMort.Soif, Comportement.CauseRetenue(CauseDeMort.Soif, affame: false, reserves: 0));
        Assert.Equal(CauseDeMort.Froid, Comportement.CauseRetenue(CauseDeMort.Froid, affame: true, reserves: 0));
    }

    /// <summary>
    /// Sans modèle, le monde ne se sert pas du LLM ; « Que ferait l'Entité ? » et les conversations suivent « Utiliser le LLM ».
    /// </summary>
    [Fact]
    public async Task Hub_UtiliserLeLlm_BorneLEntiteEtLesConversations()
    {
        using var usine = new UsineTest();
        await using var hub = usine.CreerConnexion();
        await hub.StartAsync();
        var etat = await hub.InvokeAsync<EtatEntiteLlmDto>("EtatEntiteLlm");
        Assert.Equal(nameof(UsageLlm.Jamais), etat.Usage);
        await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerConnexionLlm", new ConnexionLlm(ConnexionLlm.Ollama, "http://localhost:9", "gemma3:4b"));

        var refus = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<int>("QueFeraitLEntite", 1, 0));
        Assert.Contains(L.T("llm.erreur.usageEntite"), refus.Message);
        refus = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<DialogueDto?>("OuvrirDialogue", 1, "Voix", 0, false));
        Assert.Contains(L.T("llm.erreur.usageConversations"), refus.Message);

        etat = await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerUsageLlm", nameof(UsageLlm.Entite));
        Assert.Equal(nameof(UsageLlm.Entite), etat.Usage);
        Assert.True(await hub.InvokeAsync<int>("QueFeraitLEntite", 1, 0) >= 1);
        refus = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<DialogueDto?>("OuvrirDialogue", 1, "Voix", 0, false));
        Assert.Contains(L.T("llm.erreur.usageConversations"), refus.Message);

        etat = await hub.InvokeAsync<EtatEntiteLlmDto>("ReglerUsageLlm", nameof(UsageLlm.Conversations));
        refus = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<int>("QueFeraitLEntite", 1, 0));
        Assert.Contains(L.T("llm.erreur.usageEntite"), refus.Message);

        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<EtatEntiteLlmDto>("ReglerUsageLlm", "3"));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<EtatEntiteLlmDto>("ReglerUsageLlm", "Parfois"));
        Assert.Equal(nameof(UsageLlm.Conversations), (await hub.InvokeAsync<EtatEntiteLlmDto>("EtatEntiteLlm")).Usage);
    }
}
