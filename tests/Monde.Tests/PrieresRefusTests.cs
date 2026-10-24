using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.28.1 : refus des prières (en secret ou par un signe), durée de vie, prières collectives, bilans.</summary>
public sealed class PrieresRefusTests : IDisposable
{
    private const long Jour = Calendrier.SecondesParJour;
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-refus-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    /// <summary>Des croyants affamés ; avance jour après jour jusqu'à ce que <paramref name="trouver"/> trouve une prière en attente.</summary>
    private static (MondeSimule Monde, Priere Priere) Attendre(Func<Priere, bool> trouver, int jours = 60, bool groupes = false)
    {
        var conf = groupes ? ConfigurationMonde.ParDefaut(5) : new ConfigurationMonde(5, 20, 64, 64, 1);
        var depart = new MondeSimule(GenerateurContinent.Generer(conf), groupes ? MondeSimule.Nouveau(5) : MondeSimule.Nouveau(conf));
        // Les communautés naissent au fil des premières années.
        for (var a = 0; groupes && a < 6 && depart.Communautes().Count(c => c.Active) < 2; a++) depart.AvancerDe(Calendrier.SecondesParAn);
        var etat = depart.Etat;
        foreach (var h in etat.Humains!)
        {
            h.Croyance = 90;
            h.Faim = 95;
        }
        var monde = new MondeSimule(GenerateurContinent.Generer(etat.Configuration!), etat);
        for (var j = 0; j < jours; j++)
        {
            if (groupes)
            {
                // Un groupe affamé le reste jusqu'au soir, quand il décide de prier ensemble : sinon une chasse heureuse, de la
                // viande gardée ou une réserve l'apaisent avant.
                monde.AvancerDe(Jour - 2 * MondeSimule.SecondesParPas);
                var suite = monde.Etat;
                foreach (var h in suite.Humains!) if (h.EstVivant) h.Faim = Math.Max(h.Faim, 95);
                monde = new MondeSimule(GenerateurContinent.Generer(suite.Configuration!), suite);
                monde.AvancerDe(2 * MondeSimule.SecondesParPas);
            }
            else monde.AvancerDe(Jour);
            if (monde.Prieres().FirstOrDefault(p => p.Issue == IssuePriere.EnAttente && p.Sujet != SujetPriere.Remerciement && trouver(p)) is { } p)
                return (monde, p);
        }
        throw new InvalidOperationException("Aucune prière de ce genre.");
    }

    [Fact]
    public void UnRefusEnSecret_NeChangeRienPourCeluiQuiPrie_PuisLaPriereNEstPasEntendue()
    {
        var (monde, p) = Attendre(p => p.CommunauteId == 0);
        var texte = monde.Refuser(p.Id, signe: false);

        Assert.StartsWith("Refusée en secret", texte.Texte);
        var apres = monde.Prieres().First(x => x.Id == p.Id);
        Assert.Equal((IssuePriere.EnAttente, RefusPriere.Cache), (apres.Issue, apres.Refus));
        Assert.Equal("Tu as refusé cette prière.", monde.Exaucer(p.Id).Texte);
        Assert.Equal("Tu as déjà refusé cette prière.", monde.Refuser(p.Id, signe: true).Texte);
        Assert.Equal("Cette prière n'existe pas (ou plus).", monde.Refuser(-1, signe: false).Texte);
        // Passé son temps, elle se conclut sans l'Entité (pas entendue, ou arrivée d'elle-même) et reste marquée refusée.
        monde.AvancerDe((p.Jusqua - monde.Etat.Secondes) + 2 * Jour);
        var fin = monde.Prieres().FirstOrDefault(x => x.Id == p.Id);
        if (fin is not null)
        {
            Assert.Contains(fin.Issue, new[] { IssuePriere.SansReponse, IssuePriere.ExauceeParHasard });
            Assert.Equal(RefusPriere.Cache, fin.Refus);
        }
    }

    [Fact]
    public void UnRefusParUnSigne_EstConnuDeCeluiQuiPrie_EtNoteCommeActeDeLEntite()
    {
        var (monde, p) = Attendre(p => p.CommunauteId == 0);
        var texte = monde.Refuser(p.Id, signe: true);

        Assert.StartsWith("Refusée par un signe", texte.Texte);
        var apres = monde.Prieres().First(x => x.Id == p.Id);
        Assert.Equal((IssuePriere.Refusee, RefusPriere.Signe), (apres.Issue, apres.Refus));
        Assert.NotNull(apres.Conclusion);
        Assert.Contains(monde.Etat.JournalNonEnregistre!, e => e.Origine == Origine.Entite && e.Texte.Contains("refuse la prière"));
        Assert.Equal("Cette prière n'attend plus de réponse.", monde.Exaucer(p.Id).Texte);
    }

    [Fact]
    public void LaPatience_DependDuCaractereEtDeLEnjeu_DansDesBornesRaisonnables()
    {
        var conf = new ConfigurationMonde(5, 20, 64, 64, 1);
        var humains = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)).Etat.Humains!;
        foreach (var h in humains)
        {
            var p = Comportement.Patience(h, vital: false);
            Assert.InRange(p, 0.6, 1.4);
            Assert.True(Comportement.Patience(h, vital: true) > p);
        }
        // Un consciencieux fervent attend plus longtemps qu'un impatient sans ferveur.
        Assert.True(humains.Max(h => Comportement.Patience(h, false)) > humains.Min(h => Comportement.Patience(h, false)));
    }

    [Fact]
    public void UnGroupeAffame_PrieEnsemble_EtLEntitePeutLExaucer()
    {
        var (monde, p) = Attendre(p => p.CommunauteId != 0, groupes: true);

        Assert.True(p.Forte);
        Assert.Contains(monde.Etat.JournalNonEnregistre!, e => e.Id == p.EvenementId && e.Significatif && e.Texte.Contains("prie ensemble"));
        var texte = monde.Exaucer(p.Id);
        Assert.StartsWith("Exaucée", texte.Texte);
        Assert.Equal(IssuePriere.ExauceeParEntite, monde.Prieres().First(x => x.Id == p.Id).Issue);
    }

    [Fact]
    public void RefusEtPrieresCollectives_SontRelus_EtCompteParLeDepot()
    {
        var (monde, p) = Attendre(p => p.CommunauteId == 0);
        monde.Refuser(p.Id, signe: false);
        var etat = monde.Etat;
        var fichier = Path.Combine(_dossier, "monde.db");
        var depot = new DepotMonde(fichier);
        depot.Enregistrer(etat);
        var relu = new DepotMonde(fichier).Charger()!;

        Assert.Equal((etat.Prieres ?? []).Select(x => (x.Id, x.Issue, x.Refus, x.CommunauteId)),
            (relu.Prieres ?? []).Select(x => (x.Id, x.Issue, x.Refus, x.CommunauteId)));
        var issues = depot.IssuesPrieres([p.HumainId], 0);
        Assert.Contains(issues, x => x.Id == p.Id && x.Refus == RefusPriere.Cache && !x.Collective);
        Assert.All(issues, x => Assert.False(x.Collective));
        Assert.Empty(depot.IssuesPrieres([], 0));
    }
}
