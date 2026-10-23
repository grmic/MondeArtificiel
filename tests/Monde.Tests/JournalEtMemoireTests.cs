using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
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
    public void Memoire_UnLieuProcheEstLeMeme_EtLaPlaceDependDeLAptitude()
    {
        var m = new Memoire();
        m.Retenir(TypeLieu.Eau, 10, 10, 0);
        m.Retenir(TypeLieu.Eau, 12, 11, 100); // à 2 cases : même endroit, rafraîchi le même jour (pas de rappel)
        Assert.Single(m.Lieux);
        Assert.Equal(new LieuConnu(TypeLieu.Eau, 12, 11, 100), m.Lieux[0]);
        m.Retenir(TypeLieu.Eau, 12, 11, Calendrier.SecondesParJour); // revenu un autre jour : un rappel
        Assert.Equal(1, m.Lieux[0].Rappels);

        Assert.Equal(3, Memoire.Capacite(70));
        Assert.Equal(5, Memoire.Capacite(100));
        Assert.Equal(10, Memoire.Capacite(130));

        // Mémoire faible (70) : 3 lieux d'eau au plus ; le souvenir le plus faible part (ici, jamais rappelé et le plus ancien).
        for (var i = 1; i <= 5; i++) m.Retenir(TypeLieu.Eau, 10 + 10 * i, 10, Calendrier.SecondesParJour + i, aptitude: 70);
        Assert.Equal(3, m.Lieux.Count);
        Assert.Contains(m.Lieux, l => l.X == 12); // le lieu rappelé reste

        m.Retenir(TypeLieu.Abri, 0, 0, 200, aptitude: 70);
        Assert.Equal(4, m.Lieux.Count); // chaque type a sa propre place
    }

    [Fact]
    public void Memoire_LOubliDependDeLAptitudeEtDesRappels()
    {
        static double Restants(double aptitude, int rappels, int jours)
        {
            var alea = new Alea(7);
            var m = new Memoire();
            for (var i = 0; i < 1000; i++) m.Lieux.Add(new LieuConnu(TypeLieu.Eau, i, 0, 0, rappels));
            for (var j = 0; j < jours; j++) m.OublierAvecLeTemps(aptitude, alea);
            return m.Lieux.Count / 1000.0;
        }

        // Vu une fois, 30 jours plus tard : un « crétin » (70) a presque tout oublié, une très bonne mémoire (130) presque rien.
        Assert.InRange(Restants(70, 0, 30), 0, 0.05);      // théorie : e^(−30/7,5) ≈ 2 %
        Assert.InRange(Restants(100, 0, 30), 0.30, 0.44);  // e^(−1) ≈ 37 %
        Assert.InRange(Restants(130, 0, 30), 0.72, 0.84);  // e^(−0,25) ≈ 78 %
        // Un abri où l'on est revenu 50 fois : presque jamais oublié en un an.
        Assert.InRange(Restants(100, 50, 365), 0.94, 1);
    }

    [Fact]
    public void Memoire_UnLieuDecevantEstEviteOuOublie()
    {
        var m = new Memoire();
        var faible = new LieuConnu(TypeLieu.Nourriture, 5, 5, 0, Rappels: 1);
        var solide = new LieuConnu(TypeLieu.Nourriture, 50, 50, 0, Rappels: 6);
        m.Lieux.AddRange([faible, solide]);

        m.Decevoir(faible, 100);
        Assert.DoesNotContain(faible, m.Lieux);
        m.Decevoir(solide, 100);
        Assert.Equal(4, m.Lieux.Single().Rappels);
        Assert.Null(m.PlusProche(TypeLieu.Nourriture, 0, 0, 100 + Memoire.DureeDeception - 1)); // trop tôt pour y retourner
        Assert.Equal(50, m.PlusProche(TypeLieu.Nourriture, 0, 0, 100 + Memoire.DureeDeception)!.X);
    }

    [Fact]
    public void Memoire_PenseesBornees_SansRepetition()
    {
        var m = new Memoire();
        static Recit Eau(string vers) => new("p.eau.vue", ("vers", new Recit($"p.vers.{vers}")));
        m.Penser(0, new Recit("p.boire.1"));
        m.Penser(1, new Recit("p.boire.1"));
        Assert.Single(m.Pensees); // pas de répétition immédiate
        for (var i = 0; i < Memoire.PenseesMax + 3; i++) m.Penser(i, new Recit("j.duree.mois", ("n", i)));
        Assert.Equal(Memoire.PenseesMax, m.Pensees.Count);
        Assert.Equal($"{Memoire.PenseesMax + 2} mois", m.Pensees[^1].Texte);

        m.Penser(50, Eau("nord"));
        Assert.Equal("J'ai soif. Je vois de l'eau vers le nord.", m.Pensees[^1].Texte);
        Assert.True(m.PenseeRecente(Eau("sud-est"), 3)); // même sens, autre direction
        Assert.False(m.PenseeRecente(new Recit("p.cueillir.1"), 3));

        // Une pensée d'avant la 0.30.1 (phrase française) se compare encore à son sens.
        var ancienne = new Memoire();
        ancienne.Pensees.Add(new Pensee(0, "J'ai soif. Je vois de l'eau vers le nord."));
        Assert.True(ancienne.PenseeRecente(Eau("sud"), 3));

        var copie = m.Copier();
        copie.Penser(99, new Recit("p.boire.2"));
        Assert.DoesNotContain(m.Pensees, p => p.Recit?.Cle == "p.boire.2"); // copie indépendante
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
        Assert.True(adultes.Count(h => h.Liens.Count > 0) > adultes.Count * 0.9);
        Assert.All(humains.Where(h => h.AgeEnAnnees(monde.Secondes) < 1), h => Assert.Empty(h.Memoire.Pensees));

        var journal = monde.Etat.JournalNonEnregistre!;
        Assert.Contains(journal, e => e.Type == TypeEvenement.Naissance);
        Assert.Contains(journal, e => e.Type == TypeEvenement.Grossesse);
        // Numéros croissants et uniques (les humains d'un même pas se partagent des numéros entrelacés).
        Assert.True(journal.Zip(journal.Skip(1)).All(p => p.First.Id < p.Second.Id));
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
    public void Fichier_Version5_PasseEnVersion8_SansPerdreSesHumains()
    {
        var depot = new DepotMonde(Fichier);
        depot.Enregistrer(NouveauMonde().Etat);
        DepotMondeTests.ExecuterSql(Fichier, DepotMondeTests.RetourVersion6);
        DepotMondeTests.ExecuterSql(Fichier,
            "DROP TABLE journal; DROP TABLE memoire_lieux; DROP TABLE memoire_proches; DROP TABLE pensees; PRAGMA user_version = 5;");

        var relu = new DepotMonde(Fichier).Charger()!;

        Assert.Equal(66, relu.Humains!.Count);
        Assert.All(relu.Humains, h => Assert.Empty(h.Memoire.Lieux));
        Assert.Equal(1, relu.ProchainIdJournal);
        Assert.Empty(new DepotMonde(Fichier).LireJournal());
    }

    [Fact]
    public void Fichier_Version6_PasseEnVersion8_AvecUnCaractereNeutre_EtSesProchesDevenusDesLiens()
    {
        var etat = NouveauMonde().Etat;
        var h1 = etat.Humains![0];
        new DepotMonde(Fichier).Enregistrer(etat);
        DepotMondeTests.ExecuterSql(Fichier, DepotMondeTests.RetourVersion6);
        DepotMondeTests.ExecuterSql(Fichier, "INSERT INTO memoire_proches VALUES (1, 0, 2, 40, 9000);");

        var relu = new DepotMonde(Fichier).Charger()!.Humains!;

        var p = relu[0].Personnalite;
        Assert.Equal(h1.Personnalite.Ouverture, p.Ouverture);
        Assert.Equal(h1.Personnalite.Extraversion, p.Extraversion);
        Assert.Equal(0.5, p.Agreabilite);
        Assert.Equal(100, p.Intelligence.Generale);
        Assert.Equal(h1.Personnalite.Adresse, p.Adresse, precision: 9); // l'adresse d'avant devient l'aptitude manuelle
        Assert.Equal(new Lien(2, 40, 0, 9000), relu[0].Liens.Single());
        Assert.Equal(50, relu[0].Humeur);
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
