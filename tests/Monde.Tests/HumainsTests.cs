using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

public sealed class HumainsTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static MondeSimule NouveauMonde() => new(Carte, MondeSimule.Nouveau(42));

    /// <summary>Résumé texte de tout l'état, pour comparer deux mondes exactement.</summary>
    private static string Empreinte(MondeSimule monde)
    {
        var etat = monde.Etat;
        var humains = string.Join("\n", etat.Humains!.Select(h =>
            $"{h.Id} {h.X} {h.Y} {h.Faim:R} {h.Soif:R} {h.Fatigue:R} {h.Froid:R} {h.Sante:R} {h.Activite} {h.CibleX} {h.CibleY} {h.Detour} {h.MaladeJusqua} {h.MortLe} {h.CauseDeMort}"));
        return $"{etat.Secondes} {etat.Pas} {string.Join(",", etat.Alea!)}\n{string.Join(",", etat.Gibier!)}\n{string.Join(",", etat.Plantes!)}\n{humains}";
    }

    [Fact]
    public void Population_Compte33CouplesSurLeursEmplacements()
    {
        var humains = NouveauMonde().Etat.Humains!;

        Assert.Equal(66, humains.Count);
        Assert.Equal(66, humains.Select(h => h.Nom).Distinct().Count());
        Assert.All(humains, h =>
        {
            var partenaire = humains[h.PartenaireId - 1];
            Assert.Equal(h.Id, partenaire.PartenaireId);
            Assert.NotEqual(h.Sexe, partenaire.Sexe);
            Assert.Equal(h.Emplacement, partenaire.Emplacement);
            var emplacement = Carte.Emplacements[h.Emplacement - 1];
            Assert.Equal((emplacement.X, emplacement.Y), (h.X, h.Y));
            Assert.InRange(h.AgeEnAnnees(6 * Calendrier.SecondesParHeure), 18, 26);
            Assert.True(h.EstVivant);
        });
    }

    [Fact]
    public void MemeGraine_MemeHistoire()
    {
        var a = NouveauMonde();
        var b = NouveauMonde();
        a.AvancerDe(30 * Calendrier.SecondesParJour);
        b.AvancerDe(30 * Calendrier.SecondesParJour);
        Assert.Equal(Empreinte(a), Empreinte(b));
    }

    [Fact]
    public void SauvegardeEtRechargement_NeChangentPasLaSuite()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-suite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var continu = NouveauMonde();
            var interrompu = NouveauMonde();
            continu.AvancerDe(10 * Calendrier.SecondesParJour);
            interrompu.AvancerDe(10 * Calendrier.SecondesParJour);

            // Arrêt et reprise du monde « interrompu » via un fichier SQLite.
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(interrompu.Etat);
            var repris = new MondeSimule(Carte, new DepotMonde(Path.Combine(dossier, "monde.db")).Charger()!);

            continu.AvancerDe(20 * Calendrier.SecondesParJour);
            repris.AvancerDe(20 * Calendrier.SecondesParJour);
            Assert.Equal(Empreinte(continu), Empreinte(repris));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void SurDeuxAns_CertainsSurviventEtDAutresMeurent()
    {
        var monde = NouveauMonde();
        for (var jour = 0; jour < 2 * Calendrier.JoursParAn; jour++)
        {
            monde.AvancerDe(Calendrier.SecondesParJour);
            var (_, humains) = monde.InstantaneHumains();
            Assert.All(humains, h =>
            {
                Assert.True(Climat.EstPraticable(Carte.BiomeEn(h.X, h.Y)), $"{h.Nom} est dans l'eau en ({h.X}, {h.Y})");
                Assert.InRange(h.Sante, 0, 100);
                Assert.InRange(h.Faim, 0, 100);
            });
        }

        var final = monde.Etat.Humains!;
        var vivants = final.Count(h => h.EstVivant);
        Assert.InRange(vivants, 1, 65);
        Assert.All(final.Where(h => !h.EstVivant), h =>
        {
            Assert.NotEqual(CauseDeMort.Aucune, h.CauseDeMort);
            Assert.Equal(Activite.Mort, h.Activite);
            Assert.Equal(0, h.Sante);
        });
    }

    [Fact]
    public void Climat_HiverPlusFroidQueLEte_EtNuitPlusFroideQueLeJour()
    {
        var e = Carte.Emplacements[0];
        double T(int jour, int heure) => Climat.Temperature(Carte, e.X, e.Y, jour * Calendrier.SecondesParJour + heure * Calendrier.SecondesParHeure);
        Assert.True(T(315, 14) < T(135, 14)); // milieu de l'hiver contre milieu de l'été
        Assert.True(T(135, 2) < T(135, 14));
    }

    [Fact]
    public void Ressources_ConsommeesPuisRepoussent()
    {
        var eco = new Ecosysteme(Carte);
        var i = Array.FindIndex(Carte.Plantes, p => p >= 50);
        var initial = eco.Plantes[i];

        Assert.Equal(8, Ecosysteme.Prelever(eco.Plantes, i, 8));
        Assert.Equal(initial - 800, eco.Plantes[i]);
        for (var heure = 0; heure < 24 * 60; heure++) eco.Repousser();
        Assert.True(eco.Plantes[i] > initial - 800);
        Assert.True(eco.Plantes[i] <= initial);
    }
}
