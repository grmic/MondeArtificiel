using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.27.0 : fiche d'une communauté, communauté notée à la naissance et à la découverte.</summary>
public sealed class FicheCommunauteTests
{
    [Fact]
    public void NouveauNe_GardeLaCommunauteDeSaMere()
    {
        var mere = VitesseTests.Humain(1);
        mere.CommunauteId = 7;
        var bebe = GenerateurPopulation.Naissance(2, mere, null, Calendrier.SecondesParAn * 20, new Alea(3), new HashSet<string>());
        Assert.Equal(7, bebe.CommunauteNaissance);
    }

    [Theory]
    [InlineData(Sexe.Homme, 0.0, 0.9, 0)]
    [InlineData(Sexe.Homme, 0.2, 0.8, 1)]
    [InlineData(Sexe.Femme, 0.5, 0.5, 2)]
    [InlineData(Sexe.Femme, 0.1, 0.9, 3)]
    [InlineData(Sexe.Homme, 0.05, 0.1, 4)]
    public void ClasseOrientation_SelonLaPartVersLeMemeSexe(Sexe sexe, double versHommes, double versFemmes, int classe)
    {
        var h = VitesseTests.Humain(sexe == Sexe.Homme ? 2 : 1);
        h.Orientation = new Orientation(versHommes, versFemmes);
        Assert.Equal(classe, FicheCommunauteDto.ClasseOrientation(h));
    }

    [Fact]
    public void MondeV28_RetrouveLesCommunautesDansLeJournal()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-fichec-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
            monde.AvancerDe(2 * Calendrier.SecondesParAn);
            var etat = monde.Etat;
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);

            var nes = etat.Humains!.Where(h => h.CommunauteNaissance != 0).ToDictionary(h => h.Id, h => h.CommunauteNaissance);
            Assert.NotEmpty(nes);

            DepotMondeTests.ExecuterSql(fichier, """
                ALTER TABLE humains DROP COLUMN communaute_naissance;
                ALTER TABLE decouvertes DROP COLUMN communaute;
                PRAGMA user_version = 28;
                """);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
            foreach (var h in relu.Humains!)
                Assert.Equal(nes.GetValueOrDefault(h.Id), h.CommunauteNaissance);
            Assert.Equal(etat.Decouvertes!.Select(d => d.CommunauteId), relu.Decouvertes!.Select(d => d.CommunauteId));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Hub_DonneLaFicheDUneCommunaute_EtRienPourUnIdentifiantInconnu()
    {
        using var usine = new UsineTest();
        await using var connexion = usine.CreerConnexion();
        await connexion.StartAsync();
        var monde = usine.Services.GetRequiredService<ServiceMonde>().Monde;
        monde.ChangerFacteur(0);
        monde.AvancerDe(Calendrier.SecondesParAn);

        var (_, humains) = monde.InstantaneHumains();
        var id = humains.First(h => h.EstVivant && h.CommunauteId != 0).CommunauteId;
        var fiche = await connexion.InvokeAsync<FicheCommunauteDto?>("FicheCommunaute", id);
        Assert.NotNull(fiche);
        Assert.Equal(id, fiche.Base.Id);
        Assert.Equal(humains.Count(h => h.EstVivant && h.CommunauteId == id), fiche.Vivants);
        Assert.Equal(fiche.Vivants, fiche.Membres.Count);
        Assert.Equal(fiche.Vivants, fiche.Sexes.Hommes.Sum() + fiche.Sexes.Femmes.Sum());
        Assert.Equal(fiche.Vivants - fiche.Sexes.Hommes[0] - fiche.Sexes.Femmes[0], fiche.Sexes.Orientations.Sum());
        Assert.Equal(34, fiche.Population.Pyramide.Sum(p => p.Length));

        Assert.Null(await connexion.InvokeAsync<FicheCommunauteDto?>("FicheCommunaute", -1));
        Assert.Null(await connexion.InvokeAsync<FicheCommunauteDto?>("FicheCommunaute", int.MaxValue));
    }
}
