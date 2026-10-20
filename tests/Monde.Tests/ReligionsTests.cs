using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.25.1 et 0.26 : petits sevrés, gravités par âge ; prières, prophètes, religions.</summary>
public sealed class ReligionsTests : IDisposable
{
    private const long Jour = Calendrier.SecondesParJour;
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-religions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static MondeSimule Monde(ulong graine = 5, int couples = 20, int taille = 64)
    {
        var conf = new ConfigurationMonde(graine, couples, taille, taille, 1);
        return new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf));
    }

    [Fact]
    public void UnPetitSevre_TombePlusMaladeEtPlusGravement_DuVentreEtDesPoumons()
    {
        Assert.True(Sevre.Risque(0.3) > Sevre.Risque(1.5) && Sevre.Risque(1.5) > 1);
        Assert.True(Sevre.Gravite(0.3) > Sevre.Gravite(1.5) && Sevre.Gravite(1.5) > 1);
        Assert.True(Sevre.Concerne(Maladie.Pneumonie) && Sevre.Concerne(Maladie.Diarrhee));
        Assert.False(Sevre.Concerne(Maladie.Paludisme));
    }

    [Fact]
    public void ChezLesTout_Petits_LaDiarrheeEstPlusGraveEtLaPneumonieUnPeuMoins()
    {
        Assert.True(Maladies.Gravite(Maladie.Diarrhee, 1) > Maladies.Gravite(Maladie.Diarrhee, 3));
        Assert.True(Maladies.Gravite(Maladie.Diarrhee, 3) > Maladies.Gravite(Maladie.Diarrhee, 30));
        Assert.True(Maladies.Gravite(Maladie.Pneumonie, 1) < Maladies.Gravite(Maladie.Pneumonie, 30));
        Assert.Equal(Maladies.Gravite(Maladie.Paludisme, 1), Maladies.Gravite(Maladie.Paludisme, 30));
    }

    /// <summary>Fait de tous des croyants affamés, puis avance jour après jour jusqu'à ce que quelqu'un prie (au plus un mois).</summary>
    private static (MondeSimule Monde, Priere Priere) UnePriere()
    {
        var etat = Monde().Etat;
        foreach (var h in etat.Humains!)
        {
            h.Croyance = 90;
            h.Faim = 95;
        }
        var monde = new MondeSimule(GenerateurContinent.Generer(etat.Configuration!), etat);
        for (var j = 0; j < 30; j++)
        {
            monde.AvancerDe(Jour);
            if (monde.Prieres().FirstOrDefault(p => p.Issue == IssuePriere.EnAttente && p.Sujet != SujetPriere.Remerciement) is { } p) return (monde, p);
        }
        throw new InvalidOperationException("Personne n'a prié.");
    }

    [Fact]
    public void UnCroyantDansLaDetresse_Prie_EtLaPriereForteVaAuJournal()
    {
        var (monde, _) = UnePriere();
        var prieres = monde.Prieres();
        Assert.NotEmpty(prieres);
        var journal = monde.Etat.JournalNonEnregistre!.Where(e => e.Type == TypeEvenement.Religion).ToList();
        foreach (var p in prieres.Where(p => p.Forte)) Assert.Contains(journal, e => e.Id == p.EvenementId && e.Significatif);
        // Les prières discrètes ne sont pas au journal.
        Assert.All(prieres.Where(p => !p.Forte && p.Sujet != SujetPriere.Remerciement), p => Assert.Equal(0, p.EvenementId));
    }

    [Fact]
    public void LEntiteExauce_UnePriereEnAttente_EtCelaEstNoteCommeSonActe()
    {
        var (monde, p) = UnePriere();
        var texte = monde.Exaucer(p.Id);

        Assert.StartsWith("Exaucée", texte);
        var apres = monde.Prieres().First(x => x.Id == p.Id);
        Assert.Equal(IssuePriere.ExauceeParEntite, apres.Issue);
        Assert.Contains(monde.Etat.JournalNonEnregistre!, e => e.Origine == Origine.Entite && e.Type == TypeEvenement.Religion && e.Texte.Contains("exauce"));
        // Une seconde fois : elle n'attend plus rien.
        Assert.Equal("Cette prière n'attend plus de réponse.", monde.Exaucer(p.Id));
        Assert.Equal("Cette prière n'existe pas (ou plus).", monde.Exaucer(-3));
    }

    /// <summary>L'Entité désigne un prophète : apparition à des adultes croyants, jusqu'à ce que l'un accepte.</summary>
    private static (MondeSimule Monde, Humain Prophete) UnProphete()
    {
        var monde = Monde();
        monde.AvancerDe(10 * Jour);
        var t = monde.Secondes;
        foreach (var c in monde.Etat.Humains!.Where(h => h.EstVivant && h.AgeEnAnnees(t) is > 18 and < 40).OrderByDescending(h => h.Personnalite.Extraversion))
        {
            monde.Parler([c.Id], [], Parole.Partage, ModeParole.Apparition, monde.Etat.Humains!.First(h => h.EstVivant && h.Id != c.Id).Id, 0);
            monde.Parler([c.Id], [], Parole.PorteMaParole, ModeParole.Apparition, 0, 0);
            var h = monde.Etat.Humains![c.Id - 1];
            if (h.Role == RoleReligieux.Prophete) return (monde, h);
        }
        throw new InvalidOperationException("Personne n'a accepté.");
    }

    [Fact]
    public void PorteMaParole_FaitUnProphete_QuiFondeUneReligionAvecLesParolesCrues()
    {
        var (monde, prophete) = UnProphete();
        var (religions, lieux, _) = monde.Religions();
        var r = Assert.Single(religions);

        Assert.Equal(SourceProphete.Entite, prophete.SourceProphete);
        Assert.Equal(r.Id, prophete.ReligionId);
        Assert.Equal(NaissanceReligion.Prophete, r.Naissance);
        Assert.Equal($"la parole de {prophete.Nom}", r.Sens);
        Assert.Contains(r.Dogmes, d => d.Precepte == Precepte.Createur && d.Origine == OrigineDogme.Entite);
        // « Partage avec… », si le prophète l'a crue, devient un précepte (peut-être changé en offrandes).
        var partage = prophete.Impulsions.Where(i => i.Parole == Parole.Partage).ToList();
        if (partage.Exists(i => i.Crue && i.Reponse != Reponse.Refusee))
            Assert.Contains(r.Dogmes, d => d.Precepte is Precepte.Partager or Precepte.Offrandes);
        else
            Assert.DoesNotContain(r.Dogmes, d => d.Precepte == Precepte.Partager);
        // L'apparition laisse un lieu sacré.
        Assert.Contains(lieux, l => l.X == prophete.X && l.Y == prophete.Y || Math.Max(Math.Abs(l.X - prophete.X), Math.Abs(l.Y - prophete.Y)) <= 2);
        Assert.Contains(monde.Etat.JournalNonEnregistre!, e => e.Type == TypeEvenement.Religion && e.Significatif && e.Texte.Contains("Ainsi naît"));
    }

    [Fact]
    public void LaReligion_SeTransmet_EtSePerpetueAuFilDesSemaines()
    {
        var (monde, prophete) = UnProphete();
        monde.AvancerDe(120 * Jour);
        var e = monde.Etat;
        var r = monde.Religions().Religions[0];
        Assert.True(r.FidelesMax >= 1);
        // Chaque semaine, les fidèles d'une même communauté se rassemblent : la première fois est notée.
        if (e.Humains!.Count(h => h.EstVivant && h.ReligionId == r.Id && h.CommunauteId != 0) >= 2) Assert.NotNull(r.Gestes);
        Assert.True(e.Humains![prophete.Id - 1].Ferveur >= 0.5 || !e.Humains[prophete.Id - 1].EstVivant);
    }

    [Fact]
    public void ReligionsEtPrieres_SontRelues_ALIdentique()
    {
        var (monde, _) = UnProphete();
        monde.AvancerDe(30 * Jour);
        var etat = monde.Etat;
        var fichier = Path.Combine(_dossier, "monde.db");
        new DepotMonde(fichier).Enregistrer(etat);
        var relu = new DepotMonde(fichier).Charger()!;

        Assert.Equal(etat.Religions!.Count, relu.Religions!.Count);
        for (var i = 0; i < etat.Religions.Count; i++)
        {
            var (a, b) = (etat.Religions[i], relu.Religions[i]);
            Assert.Equal((a.Nom, a.Sens, a.FondateurId, a.Naissance, a.Gestes, a.FidelesMax), (b.Nom, b.Sens, b.FondateurId, b.Naissance, b.Gestes, b.FidelesMax));
            Assert.Equal(a.Dogmes, b.Dogmes);
        }
        Assert.Equal(etat.LieuxSacres, relu.LieuxSacres);
        Assert.Equal((etat.Prieres ?? []).Select(p => (p.Id, p.Issue, p.Sujet, p.Forte)), (relu.Prieres ?? []).Select(p => (p.Id, p.Issue, p.Sujet, p.Forte)));
        for (var i = 0; i < etat.Humains!.Count; i++)
        {
            var (a, b) = (etat.Humains[i], relu.Humains![i]);
            Assert.Equal((a.ReligionId, a.Ferveur, a.Role, a.SourceProphete, a.Miracles, a.PrieLe), (b.ReligionId, b.Ferveur, b.Role, b.SourceProphete, b.Miracles, b.PrieLe));
        }
        // Le monde relu continue sans heurt.
        var suite = new MondeSimule(GenerateurContinent.Generer(relu.Configuration!), relu);
        suite.AvancerDe(10 * Jour);
    }

    [Fact]
    public void PrierPourQuOnSoitFrappe_PeseUnPeuSurLeKarma()
    {
        Assert.True(Regard.PrierContre < 0 && Regard.PrierContre > Regard.Vol);
        Assert.Equal(Regard.Meurtre, Regard.Sacrifier);
    }
}
