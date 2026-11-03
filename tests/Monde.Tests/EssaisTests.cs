using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>
/// 0.36.3 : un acte plausible mais faux est refusé AVANT d'être appliqué, avec une raison précise (le LLM corrige au second essai),
/// sans rien changer au monde ; les noms écrits sans majuscules sont repris tels quels.
/// </summary>
public sealed class EssaisTests
{
    private static (MondeSimule Monde, List<Humain> Humains) Monde()
    {
        var config = ConfigurationMonde.Nouvelle(7);
        var monde = new MondeSimule(GenerateurContinent.Generer(config), MondeSimule.Nouveau(config)) { Coeurs = 1 };
        monde.AvancerDe(Calendrier.SecondesParJour);
        return (monde, [.. monde.InstantaneHumains().Humains]);
    }

    private static string Faute(MondeSimule monde, ActeLlm a) =>
        Assert.Throws<ArgumentException>(() => Actes.Verifier(Actes.Canonique(a), FamillesLlm.Toutes, true, [], monde)).Message;

    [Fact]
    public void ActesPlausiblesMaisFaux_SontRefusesAvantDAgir_AvecLaRaison()
    {
        var (monde, humains) = Monde();
        var adultes = humains.Where(h => h.EstVivant && !h.EstEnfant(monde.Secondes)).ToList();
        var (a, b) = (adultes[0].Id, adultes[1].Id);
        double Empreinte() => monde.InstantaneHumains().Humains.Sum(h => h.Peur + h.Croyance + h.Liens.Count + (h.EstVivant ? 1 : 0));
        var avant = Empreinte();

        // Parole : cible manquante, cible inconnue, direction absente, parole de groupe adressée à un humain, mode inconnu.
        Assert.Contains("cible", Faute(monde, new ActeLlm { Type = "parler", Humains = [a], Parole = "UnisToi", Mode = "Voix" }));
        Assert.Contains("99999", Faute(monde, new ActeLlm { Type = "parler", Humains = [a], Parole = "UnisToi", Mode = "Voix", Cible = 99999 }));
        Assert.Contains("direction", Faute(monde, new ActeLlm { Type = "parler", Humains = [a], Parole = "PartsVers", Mode = "Reve" }));
        Faute(monde, new ActeLlm { Type = "parler", Humains = [a], Parole = "NAiePasPeur", Mode = "Chant" });
        Faute(monde, new ActeLlm { Type = "parler", Humains = [99999], Parole = "NAiePasPeur", Mode = "Reve" });
        // Geste sans cible vivante, catastrophe hors carte, prière inconnue, combinaison illisible, acte libre hors bornes.
        Faute(monde, new ActeLlm { Type = "geste", Geste = "guerir", Cible = 99999, Maniere = "cachee" });
        Faute(monde, new ActeLlm { Type = "catastrophe", Catastrophe = "seisme", X = -5, Y = 3 });
        Faute(monde, new ActeLlm { Type = "exaucer", Priere = 424242 });
        Faute(monde, new ActeLlm { Type = "souffler_combinaison", Humain = a, Operation = "nimporte", A = "x" });
        Assert.Contains("peur", Faute(monde, new ActeLlm { Type = "libre", Changements = [new ChangementLibre("peur", a) { Valeur = 500 }] }));
        Faute(monde, new ActeLlm { Type = "libre", Changements = [new ChangementLibre("lien", a) { Valeur = 30 }] });

        // Rien n'a changé dans le monde.
        Assert.Equal(avant, Empreinte());

        // Des actes justes passent, même écrits sans majuscules ; « direction » vaut pour « Pars vers ».
        Actes.Verifier(Actes.Canonique(new ActeLlm { Type = "parler", Humains = [a], Parole = "unistoi", Mode = "voix", Cible = b }), FamillesLlm.Toutes, true, [], monde);
        var pars = Actes.Canonique(new ActeLlm { Type = "Parler", Humains = [a], Parole = "partsvers", Mode = "reve", Direction = 3 });
        Assert.Equal(("parler", "PartsVers", "Reve", 3), (pars.Type, pars.Parole, pars.Mode, pars.Param));
        Actes.Verifier(pars, FamillesLlm.Toutes, true, [], monde);
        var geste = Actes.Canonique(new ActeLlm { Type = "geste", Geste = "guerir", Cible = a, Maniere = "cachee" });
        Assert.Equal((nameof(GesteEntite.Guerir), nameof(ManiereGeste.Cachee)), (geste.Geste, geste.Maniere));
        Assert.Equal(FamillesLlm.Bienfaits, Actes.Famille(geste));
        Actes.Verifier(geste, FamillesLlm.Bienfaits, false, [], monde);
        Actes.Verifier(new ActeLlm { Type = "libre", Changements = [new ChangementLibre("lien", a) { Autre = b, Valeur = 30 }] }, FamillesLlm.Toutes, true, [], monde);
    }

    [Fact]
    public void LeSchemaDonneLesClesEnListesFermees()
    {
        var acte = Actes.Schema(true)["properties"]!["actes"]!["items"]!["properties"]!;
        Assert.Contains("NAiePasPeur", acte["parole"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("Guerir", acte["geste"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("seisme", acte["catastrophe"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.DoesNotContain("Penchant", acte["mode"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()));
    }
}
