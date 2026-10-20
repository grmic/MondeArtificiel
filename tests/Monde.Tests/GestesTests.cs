using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.27.1 : gestes de l'Entité sur un humain ou une communauté, et regard sur l'invisible (amour, crainte, révolte).</summary>
public sealed class GestesTests : IDisposable
{
    private const long Jour = Calendrier.SecondesParJour;
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-gestes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    /// <summary>Un monde de six mois, où deux communautés se sont formées : enregistré une fois, relu par chaque test.</summary>
    private static readonly Lazy<string> Base = new(() =>
    {
        var fichier = Path.Combine(Path.GetTempPath(), "monde-gestes-base-" + Guid.NewGuid().ToString("N") + ".db");
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(180 * Jour);
        new DepotMonde(fichier).Enregistrer(monde.Etat);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(fichier);
        return fichier;
    });

    /// <summary>Ce monde, où chacun croit à moitié et aime l'invisible à 50.</summary>
    private static (MondeSimule Monde, EtatMonde Etat) Monde(Action<EtatMonde>? preparer = null)
    {
        var etat = new DepotMonde(Base.Value).Charger()!;
        foreach (var h in etat.Humains!)
        {
            h.Croyance = 50;
            h.Amour = 50;
        }
        preparer?.Invoke(etat);
        return (new MondeSimule(MondeSimuleTests.Carte42, etat), etat);
    }

    private static Humain Adulte(EtatMonde etat) =>
        etat.Humains!.Where(h => h.EstVivant && !h.EstEnfant(etat.Secondes) && h.CommunauteId != 0).OrderBy(h => h.Id).First();

    private static Humain Vivant(MondeSimule monde, int id) => monde.InstantaneHumains().Humains[id - 1];

    [Fact]
    public void LaFoudre_Tue_SeVoit_EtNeChangeAucunKarma()
    {
        var (monde, etat) = Monde();
        var cible = Adulte(etat);
        var karmas = etat.Humains!.ToDictionary(h => h.Id, h => h.Karma);

        var texte = monde.Agir(GesteEntite.FrapperDeMort, cible.Id, ManiereGeste.Cachee, (int)FormeMort.Foudre, 0, 0);

        Assert.Contains("mort", texte);
        var (_, humains) = monde.InstantaneHumains();
        Assert.Equal(CauseDeMort.Foudre, humains[cible.Id - 1].CauseDeMort);
        Assert.All(humains, h => Assert.Equal(karmas[h.Id], h.Karma));
        var journal = monde.Etat.JournalNonEnregistre!;
        Assert.Contains(journal, e => e.Type == TypeEvenement.Geste && e.Origine == Origine.Entite && e.HumainId == cible.Id && e.Significatif);
        // La foudre se voit toujours, même demandée cachée : la communauté s'en souvient, ses membres l'ont appris.
        var (_, souvenirs) = monde.GestesEnCours();
        Assert.Contains(souvenirs, s => s.CommunauteId == cible.CommunauteId && !s.Bienfait);
        Assert.Contains(humains, h => h.EstVivant && h.CommunauteId == cible.CommunauteId && (h.Crainte > 0 || h.Revolte > 0));
    }

    [Fact]
    public void UnMortDansSonSommeil_PassePourNaturel()
    {
        var (monde, etat) = Monde();
        var cible = Adulte(etat);
        monde.Agir(GesteEntite.FrapperDeMort, cible.Id, ManiereGeste.Apparition, (int)FormeMort.Sommeil, 0, 0);
        var (_, humains) = monde.InstantaneHumains();
        Assert.Equal(CauseDeMort.Sommeil, humains[cible.Id - 1].CauseDeMort);
        Assert.All(humains, h => Assert.Equal(0, h.Crainte));
        Assert.Empty(monde.GestesEnCours().Souvenirs);
    }

    [Fact]
    public void UnChatiment_EstJugeMeriteParQuiAEnduréLaVictime_EtInjusteParQuiLAime()
    {
        int cibleId = 0, rancunier = 0, ami = 0;
        var (monde, _) = Monde(etat =>
        {
            var cible = Adulte(etat);
            var autres = etat.Humains!.Where(h => h.EstVivant && !h.EstEnfant(etat.Secondes) && h.Id != cible.Id && h.MereId != cible.Id && h.PereId != cible.Id
                && cible.MereId != h.Id && cible.PereId != h.Id && h.PartenaireId != cible.Id).Take(2).ToList();
            cibleId = cible.Id;
            (rancunier, ami) = (autres[0].Id, autres[1].Id);
            foreach (var o in autres) (o.X, o.Y) = (cible.X, cible.Y);
            autres[0].Liens.RemoveAll(l => l.Id == cible.Id);
            autres[0].Griefs.Add(new Grief(cible.Id, MotifGrief.Meurtre, 90, 90, 0));
            autres[1].Liens.RemoveAll(l => l.Id == cible.Id);
            autres[1].Liens.Add(new Lien(cible.Id, 80, 85, etat.Secondes, Amis: true));
            autres[1].Griefs.RemoveAll(g => g.ContreId == cible.Id);
        });

        monde.Agir(GesteEntite.RendreMalade, cibleId, ManiereGeste.Voix, (int)Maladie.Pneumonie, 0, 0);

        var r = Vivant(monde, rancunier);
        var a = Vivant(monde, ami);
        Assert.True(r.Crainte > 0);
        Assert.Equal(0, r.Revolte);
        Assert.Equal(50, r.Amour);
        Assert.True(a.Revolte > 0);
        Assert.True(a.Amour < 50);
        Assert.True(r.RegardSigneLe > 0 && r.CroyanceSigneLe > 0);
    }

    [Fact]
    public void UneGuerisonCachee_Guerit_SansQuePersonneNeVoieDeSigne()
    {
        int id = 0;
        var (monde, etat) = Monde(e =>
        {
            var h = Adulte(e);
            id = h.Id;
            h.Maladie = Maladie.Pneumonie;
            h.MaladeJusqua = e.Secondes + 30 * Jour;
        });
        var texte = monde.Agir(GesteEntite.Guerir, id, ManiereGeste.Cachee, 0, 0, 0);
        Assert.Contains("guérit", texte);
        var (maintenant, humains) = monde.InstantaneHumains();
        Assert.False(humains[id - 1].EstMalade(maintenant));
        Assert.All(humains, h => Assert.Equal(0, h.RegardSigneLe));
        Assert.All(humains, h => Assert.Equal(50, h.Amour));
        // Rien à guérir : le geste n'a pas lieu.
        Assert.Contains("ni maladie", monde.Agir(GesteEntite.Guerir, id, ManiereGeste.Voix, 0, 0, 0));
    }

    [Fact]
    public void LaFamine_VideLesTerres_PuisLEntiteEnSort_EtLesBornesSontImposees()
    {
        var (monde, etat) = Monde();
        var c = monde.Communautes().First(x => x.Active);
        var i = MondeSimuleTests.Carte42.Indice(c.CentreX, c.CentreY);
        var avant = monde.Etat.Plantes![i];

        monde.Agir(GesteEntite.Famine, c.Id, ManiereGeste.Reve, 0, 5, 99_999);

        var famine = Assert.Single(monde.GestesEnCours().Effets);
        Assert.Equal(0.9, famine.Force);
        Assert.Equal(720 * Jour, famine.Fin - famine.Debut);
        Assert.True(monde.Etat.Plantes![i] <= Math.Round(avant * 0.1) + 1);
        // Une semaine plus tard, les terres ne se sont pas remises ; une nouvelle famine remplace l'ancienne.
        monde.AvancerDe(7 * Jour);
        Assert.Single(monde.GestesEnCours().Effets);
        monde.Agir(GesteEntite.Famine, c.Id, ManiereGeste.Cachee, 0, 0.5, 90);
        Assert.Equal(0.5, Assert.Single(monde.GestesEnCours().Effets).Force);

        monde.Agir(GesteEntite.SortirDeFamine, c.Id, ManiereGeste.Voix, 0, 0, 0);
        Assert.Empty(monde.GestesEnCours().Effets);
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains.Where(h => h.EstVivant && h.CommunauteId == c.Id), h => Assert.True(h.Faim <= 10));
        // Un bienfait vu de tous : l'amour de ses membres monte.
        Assert.Contains(humains, h => h.EstVivant && h.CommunauteId == c.Id && h.Amour > 50);
    }

    [Fact]
    public void BenirEtRendreFecond_DurentDansLeursBornes()
    {
        var (monde, etat) = Monde();
        var c = monde.Communautes().First(x => x.Active);
        monde.Agir(GesteEntite.Benir, c.Id, ManiereGeste.Cachee, 0, 50, 10);
        var b = Assert.Single(monde.GestesEnCours().Effets);
        Assert.Equal(0.5, b.Force);
        Assert.Equal(90 * Jour, b.Fin - b.Debut);
        var femme = etat.Humains!.First(h => h.EstVivant && h.Sexe == Sexe.Femme && !h.EstEnfant(etat.Secondes));
        monde.Agir(GesteEntite.RendreFecond, femme.Id, ManiereGeste.Cachee, 0, 9, 200);
        var f = Vivant(monde, femme.Id);
        Assert.Equal(2, f.FeconditeEntite);
        Assert.Equal(etat.Secondes + 200 * Jour, f.FeconditeJusqua);
        // La bénédiction finie s'efface.
        monde.AvancerDe(91 * Jour);
        Assert.Empty(monde.GestesEnCours().Effets);
    }

    [Fact]
    public void Regard_MonteMoinsQuandIlEstFort_EtLaRevolteSApaise()
    {
        var h = VitesseTests.Humain(2);
        Regards.Ajouter(h, SentimentInvisible.Crainte, 20, "test", 0, Calendrier.SecondesParAn * 30);
        Assert.Equal(20, h.Crainte, 6);
        Regards.Ajouter(h, SentimentInvisible.Crainte, 20, "test", 0, Calendrier.SecondesParAn * 30);
        Assert.Equal(36, h.Crainte, 6);
        Assert.Equal(1, Regards.Retenue(h));
        h.Croyance = 100;
        Assert.Equal(1 - 0.3 * 0.36, Regards.Retenue(h), 6);

        h.Revolte = 60;
        var t = Calendrier.SecondesParAn * 30;
        var vide = new Dictionary<int, (double, double)>();
        var sans = new Dictionary<int, (double, double)>();
        for (var j = 0; j < 360; j++) Regards.Journee(h, [h], vide, sans, t + j * Jour);
        Assert.InRange(h.Revolte, 1, 30);
        Assert.True(h.Crainte < 36);
    }

    [Fact]
    public void UnChangementDUneDizaine_VaAuJournalAvecSaRaison()
    {
        var h = VitesseTests.Humain(1);
        Assert.Empty(Regards.Changements(h));
        Regards.Ajouter(h, SentimentInvisible.Revolte, 55, "l'invisible a frappé Ana sans raison", 0, Calendrier.SecondesParAn * 30);
        var (s, texte) = Assert.Single(Regards.Changements(h));
        Assert.Equal(SentimentInvisible.Revolte, s);
        Assert.Contains("se révolte contre l'invisible", texte);
        Assert.Contains("sans raison", texte);
        Assert.Empty(Regards.Changements(h));
    }

    [Fact]
    public void UnMondeV30_GardeGestesEtRegard_EtUnMondeV29_EstimeLeRegardPasse()
    {
        Directory.CreateDirectory(_dossier);
        var fichier = Path.Combine(_dossier, "monde.db");
        var (monde, etat) = Monde();
        var c = monde.Communautes().First(x => x.Active);
        monde.Agir(GesteEntite.Famine, c.Id, ManiereGeste.Voix, 0, 0.6, 200);
        var cible = Adulte(etat);
        monde.Agir(GesteEntite.RendreFecond, cible.Id, ManiereGeste.Apparition, 0, 1.5, 100);
        var avant = monde.Etat;
        new DepotMonde(fichier).Enregistrer(avant);

        var relu = new DepotMonde(fichier).Charger()!;
        Assert.Equal(avant.EffetsEntite, relu.EffetsEntite);
        Assert.Equal(avant.SouvenirsEntite, relu.SouvenirsEntite);
        foreach (var h in avant.Humains!)
        {
            var r = relu.Humains![h.Id - 1];
            Assert.Equal((h.Amour, h.Crainte, h.Revolte, h.RegardSigneLe, h.FeconditeEntite, h.FeconditeJusqua),
                (r.Amour, r.Crainte, r.Revolte, r.RegardSigneLe, r.FeconditeEntite, r.FeconditeJusqua));
        }
        // La famine pèse encore sur les terres après le chargement.
        var recharge = new MondeSimule(MondeSimuleTests.Carte42, relu);
        Assert.Single(recharge.GestesEnCours().Effets);

        // Retour en version 29, avec une prière exaucée par l'Entité pour le premier vivant.
        var vivant = avant.Humains!.First(h => h.EstVivant && !h.EstBebe(avant.Secondes));
        DepotMondeTests.ExecuterSql(fichier, $"""
            ALTER TABLE humains DROP COLUMN amour;
            ALTER TABLE humains DROP COLUMN crainte;
            ALTER TABLE humains DROP COLUMN revolte;
            ALTER TABLE humains DROP COLUMN regard_signe;
            ALTER TABLE humains DROP COLUMN fecondite_entite;
            ALTER TABLE humains DROP COLUMN fecondite_jusqua;
            DROP TABLE effets_entite;
            DROP TABLE souvenirs_entite;
            DELETE FROM prieres;
            INSERT INTO prieres (id, humain, le, sujet, cible, parametre, ferveur, forte, religion, evenement, jusqua, issue, issue_le, conclusion)
                VALUES (1, {vivant.Id}, 0, 0, 0, 0, 0.5, 1, 0, 0, 0, 1, 0, NULL);
            UPDATE humains SET croyance = 100 WHERE id = {vivant.Id};
            PRAGMA user_version = 29;
            """);
        var migre = new DepotMonde(fichier).Charger()!;
        Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        Assert.Equal(20, migre.Humains![vivant.Id - 1].Amour);
        Assert.All(migre.Humains!.Where(h => h.Id != vivant.Id), h => Assert.Equal(0, h.Amour));
        Assert.All(migre.Humains!, h => Assert.Equal(0, h.Revolte));
        Assert.All(migre.Humains!, h => Assert.Equal(1, h.FeconditeEntite));
        Assert.Null(migre.EffetsEntite);
    }

    [Fact]
    public async Task Hub_RefuseUnGesteInvalide_EtAgitSurUnHumain()
    {
        using var usine = new UsineTest();
        await using var connexion = usine.CreerConnexion();
        await connexion.StartAsync();
        var monde = usine.Services.GetRequiredService<ServiceMonde>().Monde;
        monde.ChangerFacteur(0);
        monde.AvancerDe(30 * Jour);

        var choix = await connexion.InvokeAsync<ChoixGestesDto>("ChoixGestes");
        Assert.Equal(11, choix.Gestes.Count);
        Assert.Contains(choix.Maladies, m => m.Cle == "0");

        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<string>("Agir", new DemandeGeste("'; DROP TABLE journal;--", 1, "Voix")));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<string>("Agir", new DemandeGeste("Guerir", 1, "Tonnerre")));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<string>("Agir", new DemandeGeste("RendreMalade", 1, "Voix", 55)));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<string>("Agir", new DemandeGeste("Calamite", 1, "Voix", 0, "hiver-impact")));
        Assert.Contains("n'existe pas", await connexion.InvokeAsync<string>("Agir", new DemandeGeste("Guerir", int.MaxValue, "Voix")));
        Assert.Contains("n'existe pas", await connexion.InvokeAsync<string>("Agir", new DemandeGeste("Benir", -3, "Voix")));

        var (_, humains) = monde.InstantaneHumains();
        var h = humains.First(x => x.EstVivant && !x.EstEnfant(monde.Secondes));
        Assert.Contains("malade", await connexion.InvokeAsync<string>("Agir", new DemandeGeste("RendreMalade", h.Id, "Voix", (int)Maladie.Diarrhee)));
        var fiche = await connexion.InvokeAsync<FicheDto?>("Fiche", h.Id);
        Assert.NotNull(fiche);
        Assert.NotNull(fiche.Regard.DernierGeste);
    }
}
