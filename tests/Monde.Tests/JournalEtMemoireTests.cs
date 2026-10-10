using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Étape 5 : mémoire des humains, pensées, journal du monde.</summary>
public sealed class JournalEtMemoireTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-journal-" + Guid.NewGuid().ToString("N"));
    private string Fichier => Path.Combine(_dossier, "monde.db");

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    private static MondeSimule NouveauMonde() => new(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));

    // Un an de simulation partagé par plusieurs tests (calculé une seule fois).
    private static readonly Lazy<MondeSimule> UnAn = new(() =>
    {
        var monde = NouveauMonde();
        monde.AvancerDe(Calendrier.SecondesParAn);
        return monde;
    });

    [Fact]
    public void Memoire_UnLieuProcheEstLeMeme_EtLaPlaceEstLimitee()
    {
        var m = new Memoire();
        m.Retenir(TypeLieu.Eau, 10, 10, 0);
        m.Retenir(TypeLieu.Eau, 12, 11, 100); // à 2 cases : même endroit, rafraîchi
        Assert.Single(m.Lieux);
        Assert.Equal(new LieuConnu(TypeLieu.Eau, 12, 11, 100), m.Lieux[0]);

        for (var i = 1; i <= Memoire.LieuxParType; i++) m.Retenir(TypeLieu.Eau, 10 + 10 * i, 10, 100 + i);
        Assert.Equal(Memoire.LieuxParType, m.Lieux.Count);
        Assert.DoesNotContain(m.Lieux, l => l.X == 12); // le plus ancien est oublié

        m.Retenir(TypeLieu.Abri, 0, 0, 200);
        Assert.Equal(Memoire.LieuxParType + 1, m.Lieux.Count); // chaque type a sa propre place
    }

    [Fact]
    public void Memoire_UnLieuTropAncienNEstPlusUtilise()
    {
        var m = new Memoire();
        m.Retenir(TypeLieu.Nourriture, 5, 5, 0);
        m.Retenir(TypeLieu.Nourriture, 50, 50, Memoire.DureeSouvenirLieu);

        Assert.Equal(5, m.PlusProche(TypeLieu.Nourriture, 0, 0, Memoire.DureeSouvenirLieu)!.X);
        Assert.Equal(50, m.PlusProche(TypeLieu.Nourriture, 0, 0, Memoire.DureeSouvenirLieu + 1)!.X);
        Assert.Null(m.PlusProche(TypeLieu.Eau, 0, 0, 0));
    }

    [Fact]
    public void Memoire_ProchesEtPenseesSontBornes()
    {
        var m = new Memoire();
        m.Cotoyer(1, 0);
        m.Cotoyer(1, 10);
        Assert.Equal(new Proche(1, 2, 10), m.Proches.Single());
        for (var id = 2; id <= Memoire.ProchesMax + 5; id++) m.Cotoyer(id, id);
        Assert.Equal(Memoire.ProchesMax, m.Proches.Count);
        Assert.Contains(m.Proches, p => p.Id == 1); // le lien le plus fort reste

        m.Penser(0, "J'ai soif.");
        m.Penser(1, "J'ai soif.");
        Assert.Single(m.Pensees); // pas de répétition immédiate
        for (var i = 0; i < Memoire.PenseesMax + 3; i++) m.Penser(i, $"pensée {i}");
        Assert.Equal(Memoire.PenseesMax, m.Pensees.Count);
        Assert.Equal($"pensée {Memoire.PenseesMax + 2}", m.Pensees[^1].Texte);

        var copie = m.Copier();
        copie.Penser(99, "autre");
        Assert.DoesNotContain(m.Pensees, p => p.Texte == "autre"); // copie indépendante
    }

    [Fact]
    public void UnAn_LesHumainsPensent_SeSouviennent_EtLeJournalEstNumerote()
    {
        var monde = UnAn.Value;
        var (_, humains) = monde.InstantaneHumains();
        var adultes = humains.Where(h => h.EstVivant && h.AgeEnAnnees(monde.Secondes) >= 15).ToList();

        Assert.NotEmpty(adultes);
        Assert.All(adultes, h => Assert.NotEmpty(h.Memoire.Pensees));
        Assert.True(adultes.Count(h => h.Memoire.Lieux.Count > 0) > adultes.Count * 0.9);
        Assert.True(adultes.Count(h => h.Memoire.Proches.Count > 0) > adultes.Count * 0.9);
        Assert.All(humains.Where(h => h.AgeEnAnnees(monde.Secondes) < 1), h => Assert.Empty(h.Memoire.Pensees));

        var journal = monde.Etat.JournalNonEnregistre!;
        Assert.Contains(journal, e => e.Type == TypeEvenement.Naissance);
        Assert.Contains(journal, e => e.Type == TypeEvenement.Grossesse);
        Assert.Equal(Enumerable.Range(1, journal.Count).Select(i => (long)i), journal.Select(e => e.Id));
        Assert.All(journal, e => Assert.Equal(Origine.Naturelle, e.Origine));
        // Chaque naissance du journal correspond à un humain né, et inversement.
        var nes = humains.Where(h => h.MereId != 0).Select(h => h.Id).Order();
        Assert.Equal(nes, journal.Where(e => e.Type == TypeEvenement.Naissance).Select(e => e.HumainId).Order());
        // Chaque mort a sa ligne de journal.
        var morts = humains.Where(h => !h.EstVivant).Select(h => h.Id).Order();
        Assert.Equal(morts, journal.Where(e => e.Type == TypeEvenement.Mort).Select(e => e.HumainId).Order());
    }

    [Fact]
    public void Journal_EstEnregistreUneSeuleFois_EtLuParFiltreEtParPage()
    {
        var monde = NouveauMonde();
        monde.AvancerDe(Calendrier.SecondesParAn / 2);
        var depot = new DepotMonde(Fichier);
        var etat = monde.Etat;
        depot.Enregistrer(etat);
        depot.Enregistrer(etat); // une deuxième sauvegarde du même état ne duplique rien
        monde.MarquerJournalEnregistre(etat.JournalNonEnregistre!.Count);
        Assert.Empty(monde.Etat.JournalNonEnregistre!);

        var tout = depot.LireJournal(limite: DepotMonde.LimiteJournal);
        Assert.Equal(Math.Min(etat.JournalNonEnregistre.Count, DepotMonde.LimiteJournal), tout.Count);
        Assert.Equal(tout.Select(e => e.Id).OrderDescending(), tout.Select(e => e.Id)); // du plus récent au plus ancien
        Assert.Equal(etat.JournalNonEnregistre.OrderByDescending(e => e.Id).Take(tout.Count), tout);

        var grossesses = depot.LireJournal([TypeEvenement.Grossesse]);
        Assert.NotEmpty(grossesses);
        Assert.All(grossesses, e => Assert.Equal(TypeEvenement.Grossesse, e.Type));

        var qui = tout.First(e => e.AutreId != 0).AutreId;
        Assert.All(depot.LireJournal(humain: qui), e => Assert.True(e.HumainId == qui || e.AutreId == qui));

        var page1 = depot.LireJournal(limite: 5);
        var page2 = depot.LireJournal(avantId: page1[^1].Id, limite: 5);
        Assert.Equal(5, page1.Count);
        Assert.True(page2[0].Id < page1[^1].Id);
        Assert.Equal(tout.Skip(5).Take(5), page2);

        // La numérotation reprend après le dernier événement enregistré.
        Assert.Equal(tout[0].Id + 1, new DepotMonde(Fichier).Charger()!.ProchainIdJournal);
    }

    [Fact]
    public void Journal_LectureAvecDesTypesHostiles_NeCassePasLaRequete()
    {
        var depot = new DepotMonde(Fichier);
        depot.Enregistrer(NouveauMonde().Etat);
        // Valeurs hors énumération : paramètres SQL, jamais concaténées ; rien ne correspond.
        Assert.Empty(depot.LireJournal([(TypeEvenement)200, (TypeEvenement)201], humain: -1, avantId: long.MaxValue, limite: 100_000));
    }

    [Fact]
    public void Fichier_Version5_PasseEnVersion6_SansPerdreSesHumains()
    {
        var depot = new DepotMonde(Fichier);
        depot.Enregistrer(NouveauMonde().Etat);
        using (var connexion = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Fichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "DROP TABLE journal; DROP TABLE memoire_lieux; DROP TABLE memoire_proches; DROP TABLE pensees; PRAGMA user_version = 5;";
            commande.ExecuteNonQuery();
        }

        var relu = new DepotMonde(Fichier).Charger()!;

        Assert.Equal(66, relu.Humains!.Count);
        Assert.All(relu.Humains, h => Assert.Empty(h.Memoire.Lieux));
        Assert.Equal(1, relu.ProchainIdJournal);
        Assert.Empty(new DepotMonde(Fichier).LireJournal());
    }

    [Fact]
    public async Task Hub_FicheEtJournal_EtFiltresRefuses()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        await connexion.InvokeAsync<HorlogeDto>("AvancerDe", "jour");
        var fiche = await connexion.InvokeAsync<FicheDto?>("Fiche", 1);
        Assert.NotNull(fiche);
        Assert.Equal(1, fiche.Humain.Id);
        Assert.NotEmpty(fiche.Caractere);
        Assert.NotEmpty(fiche.Pensees);
        Assert.Null(await connexion.InvokeAsync<FicheDto?>("Fiche", 999_999));

        var journal = await connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(Limite: 10));
        Assert.True(journal.Count <= 10);

        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(["inconnu"])));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(Limite: 0)));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(Limite: 501)));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<EvenementDto>>("Journal",
            new FiltreJournal(Enumerable.Repeat("mort", 21).ToArray())));
    }
}
