using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Causes des événements et des humeurs, jalousie, rancunes, pardon, coups, vengeance et usages des communautés.</summary>
public sealed class CausaliteTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, Sexe sexe = Sexe.Femme, double nevrosisme = 0.5, double agreabilite = 0.5,
        int x = 10, int y = 10, int mere = 0, double age = 25, double extraversion = 0.5) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        MereId = mere,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, extraversion, agreabilite, nevrosisme, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
    };

    private static void Lier(Humain a, Humain b, double affection, long derniere = Maintenant, bool amis = false)
    {
        a.Liens.Add(new Lien(b.Id, 60, affection, derniere, Amis: amis, Rivaux: affection <= -30));
        b.Liens.Add(new Lien(a.Id, 60, affection, derniere, Amis: amis, Rivaux: affection <= -30));
    }

    private static void Couple(Humain a, Humain b, double affection = 70)
    {
        a.PartenaireId = b.Id;
        b.PartenaireId = a.Id;
        Lier(a, b, affection, amis: true);
    }

    // ───────────── Raisons de l'humeur ─────────────

    [Fact]
    public void Depression_DitPourquoi_EtCiteLaMortQuiEnEstLaCause()
    {
        var social = new VieSociale(new Alea(1));
        var mere = Humain(1, nevrosisme: 0.85);
        var fille = Humain(2, mere: 1, age: 6);
        var hostile = Humain(3);
        Lier(mere, hostile, -50, derniere: Maintenant + 60 * Jour); // ils se croisent encore
        var tous = new List<Humain> { mere, fille, hostile };
        fille.MortLe = Maintenant;
        fille.MortEvenementId = 77;

        social.Endeuiller(fille, tous, Maintenant);
        var pleure = Assert.Single(social.Journal, e => e.Texte == "H1 pleure sa fille H2.");
        Assert.Equal(77, pleure.CauseId);
        Assert.True(pleure.Id > 0);

        for (var j = 1; j <= 60 && !mere.EstDeprime; j++) social.Humeurs(tous, Maintenant + j * Jour);

        Assert.True(mere.EstDeprime);
        Assert.Equal(TypeRaison.Deuil, mere.Raisons[0].Type);
        Assert.Equal(2, mere.Raisons[0].AutreId);
        Assert.Contains(mere.Raisons, r => r.Type == TypeRaison.Rejet && r.AutreId == 3);
        var sombre = Assert.Single(social.Journal, e => e.Texte.StartsWith("H1 sombre", StringComparison.Ordinal));
        Assert.StartsWith("H1 sombre dans une profonde tristesse : elle pleure sa fille H2", sombre.Texte);
        Assert.Contains("H3 lui est hostile", VieSociale.Phrases(mere, tous, Maintenant + 30 * Jour));
        Assert.Equal(77, sombre.CauseId);
        Assert.Equal(sombre.Id, mere.DepressionEvenementId);
    }

    [Fact]
    public void Humeur_LaSolitudePeseSurtoutChezLesExtravertis()
    {
        var social = new VieSociale(new Alea(1));
        var extraverti = Humain(1, Sexe.Homme, extraversion: 0.9);
        var introverti = Humain(2, x: 40, extraversion: 0.1);
        var tous = new List<Humain> { extraverti, introverti };

        for (var j = 1; j <= 60; j++) social.Humeurs(tous, Maintenant + j * Jour);

        var seul1 = extraverti.Raisons.Single(r => r.Type == TypeRaison.Solitude).Poids;
        var seul2 = introverti.Raisons.FirstOrDefault(r => r.Type == TypeRaison.Solitude)?.Poids ?? 0;
        Assert.True(seul1 > 2 * seul2);
        Assert.Contains("il se sent seul", VieSociale.Phrases(extraverti, tous, Maintenant + 60 * Jour));
    }

    // ───────────── Jalousie ─────────────

    [Fact]
    public void Infidelite_Decouverte_JalousieColereEtRancunes_AvecPourCauseLAccouplement()
    {
        var social = new VieSociale(new Alea(2));
        var trompe = Humain(1, Sexe.Homme, nevrosisme: 0.8, agreabilite: 0.3);
        var infidele = Humain(2);
        var complice = Humain(3, Sexe.Homme);
        Couple(trompe, infidele);
        var tous = new List<Humain> { trompe, infidele, complice };

        social.Decouvrir(trompe, infidele, complice, null, 55, tous, Maintenant);

        var e = Assert.Single(social.Journal, j => j.Type == TypeEvenement.Conflit);
        Assert.Equal("H1 découvre que sa compagne H2 s'est unie à H3. Une jalousie furieuse l'envahit.", e.Texte);
        Assert.Equal(55, e.CauseId);
        Assert.True(trompe.Colere > 60);
        Assert.Contains(trompe.Griefs, g => g.ContreId == 2 && g.Motif == MotifGrief.Infidelite && g.EvenementId == e.Id);
        Assert.Contains(trompe.Griefs, g => g.ContreId == 3 && g.Motif == MotifGrief.Rival);
        Assert.True(trompe.AffectionPour(3) < 0);
        Assert.True(trompe.AffectionPour(2) < 70);

        // La récidive est connue : elle blesse, mais moins.
        var colere = trompe.Colere = 0;
        social.Decouvrir(trompe, infidele, complice, null, 56, tous, Maintenant + Jour);
        Assert.Contains(social.Journal, j => j.Texte.StartsWith("H1 apprend que sa compagne H2 recommence avec H3", StringComparison.Ordinal));
        Assert.True(trompe.Colere > colere);
    }

    [Fact]
    public void Infidelite_UnTemperamentSereinDansUnGroupeTolerant_NeSEnEmeutGuere()
    {
        var social = new VieSociale(new Alea(2));
        var trompe = Humain(1, Sexe.Homme, nevrosisme: 0.15, agreabilite: 0.9);
        var infidele = Humain(2);
        var complice = Humain(3, Sexe.Homme);
        Couple(trompe, infidele, affection: 20);
        var tous = new List<Humain> { trompe, infidele, complice };

        social.Decouvrir(trompe, infidele, complice, Humain(4), 55, tous, Maintenant);

        var e = Assert.Single(social.Journal);
        Assert.Equal("H1 apprend que sa compagne H2 s'est unie à H3 (H4 les a vus et le lui a dit), et ne s'en émeut guère.", e.Texte);
        Assert.Empty(trompe.Griefs);
        Assert.Equal(0, trompe.Colere);
    }

    [Fact]
    public void Usage_UnGroupeSevere_ReprouveLInfidele_EtSonUsageEstNote()
    {
        var social = new VieSociale(new Alea(3));
        var tous = Enumerable.Range(1, 8).Select(i => Humain(i, i % 2 == 0 ? Sexe.Homme : Sexe.Femme, x: 10 + i % 2)).ToList();
        foreach (var a in tous)
        {
            foreach (var b in tous.Where(b => b.Id > a.Id)) Lier(a, b, 50, amis: true);
        }
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        var c = Assert.Single(communautes);
        c.Tolerance = 0.2;

        // H2 trompe H1 avec H8 ; H1 le découvre : le groupe réprouve H2.
        tous[0].PartenaireId = 2;
        tous[1].PartenaireId = 1;
        var avant = tous[2].AffectionPour(2);
        social.Decouvrir(tous[0], tous[1], tous[7], null, 9, tous, Maintenant);
        var reprobation = Assert.Single(social.Journal, j => j.Texte.Contains("réprouve H2"));
        Assert.True(tous[2].AffectionPour(2) < avant);
        Assert.Equal(reprobation.Id, c.DerniereAffaireId);

        social.Journal.Clear();
        social.Regrouper(tous, communautes, Maintenant + 7 * Jour, new HashSet<string>());
        Assert.Equal(UsageFidelite.MalVu, c.Usage);
        var usage = Assert.Single(social.Journal, j => j.Texte.EndsWith("s'unir hors de son couple devient mal vu."));
        Assert.Equal(reprobation.Id, usage.CauseId);
    }

    // ───────────── Rancunes, pardon, séparation ─────────────

    [Fact]
    public void Rancune_SEfface_EnPardonOuEnSeparation_QuiCitentLaTrahison()
    {
        var pardons = 0;
        var separations = 0;
        for (var graine = 1; graine <= 20; graine++)
        {
            var social = new VieSociale(new Alea((ulong)graine));
            var h = Humain(1, Sexe.Homme);
            var autre = Humain(2, x: 30);
            Couple(h, autre, affection: 10);
            h.Griefs.Add(new Grief(2, MotifGrief.Infidelite, 70, 70, 123));
            var tous = new List<Humain> { h, autre };
            for (var j = 1; j <= 3 * 365 && h.Griefs.Count > 0; j++)
            {
                h.CompagnieDuJour = 1;
                social.Humeurs(tous, Maintenant + j * Jour);
            }
            Assert.Empty(h.Griefs);
            Assert.Contains(social.Journal, e => e.Texte == "H1 pardonne à H2 sa trahison." && e.CauseId == 123);
            pardons++;
            if (social.Journal.Any(e => e.Texte == "H1 quitte H2, qui l'a trahi." && e.CauseId == 123))
            {
                separations++;
                Assert.Equal(0, h.PartenaireId);
                Assert.Equal(0, autre.PartenaireId);
            }
        }
        Assert.Equal(20, pardons);
        Assert.InRange(separations, 2, 18);
    }

    // ───────────── Coups, meurtre, vengeance ─────────────

    [Fact]
    public void Colere_PousseAAffronterLOffenseur_EnCitantLOffense()
    {
        var e = Carte.Emplacements[0];
        var social = new VieSociale(new Alea(5));
        var comportement = new Comportement(Carte, new Ecosysteme(Carte), new Alea(5)) { Social = social };
        var furieux = Humain(1, Sexe.Homme, agreabilite: 0.1, x: e.X, y: e.Y);
        var cible = Humain(2, Sexe.Homme, x: e.X, y: e.Y);
        Lier(furieux, cible, -40);
        var tous = new List<Humain> { furieux, cible };
        furieux.Griefs.Add(new Grief(2, MotifGrief.Rival, 90, 90, 321));

        for (var pas = 0; pas < 2000 && !comportement.Journal.Any(j => j.Type == TypeEvenement.Conflit); pas++)
        {
            furieux.Colere = 100;
            (furieux.X, furieux.Y, cible.X, cible.Y) = (e.X, e.Y, e.X, e.Y);
            furieux.Faim = furieux.Soif = furieux.Fatigue = cible.Faim = cible.Soif = cible.Fatigue = 0;
            furieux.Sante = cible.Sante = 100;
            comportement.Vivre(furieux, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        }

        var conflit = Assert.Single(comportement.Journal, j => j.Type == TypeEvenement.Conflit);
        Assert.Equal(321, conflit.CauseId);
        Assert.Contains("H1", conflit.Texte);
        Assert.Contains("qui s'est uni à sa compagne", conflit.Texte);
    }

    [Fact]
    public void Meurtre_EndeuilleEtAppelleVengeance_EtDresseLesCommunautesLUneContreLAutre()
    {
        var social = new VieSociale(new Alea(6));
        var groupeA = Enumerable.Range(1, 6).Select(i => Humain(i, i % 2 == 0 ? Sexe.Homme : Sexe.Femme, x: 10 + i % 2)).ToList();
        var groupeB = Enumerable.Range(7, 6).Select(i => Humain(i, i % 2 == 0 ? Sexe.Homme : Sexe.Femme, x: 14 + i % 2)).ToList();
        var tous = groupeA.Concat(groupeB).ToList();
        foreach (var g in new[] { groupeA, groupeB })
        {
            foreach (var a in g)
            {
                foreach (var b in g.Where(b => b.Id > a.Id)) Lier(a, b, 60, amis: true);
            }
        }
        foreach (var a in groupeA)
        {
            foreach (var b in groupeB) Lier(a, b, -10);
        }
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        Assert.Equal(2, communautes.Count);

        // H8 (groupe B) a tué H1 (groupe A).
        var victime = tous[0];
        var meurtrier = tous[7];
        victime.MortLe = Maintenant;
        victime.CauseDeMort = CauseDeMort.Meurtre;
        victime.MeurtrierId = meurtrier.Id;
        victime.MortEvenementId = 99;
        social.Offenser(meurtrier, victime, 15, 99, tous, Maintenant);
        social.Endeuiller(victime, tous, Maintenant);

        Assert.Contains(tous[2].Griefs, g => g.ContreId == 8 && g.Motif == MotifGrief.Meurtre && g.VictimeId == 1 && g.EvenementId == 99);
        Assert.True(tous[2].Colere > 20);

        social.Journal.Clear();
        social.Territoires(tous, communautes, new Ecosysteme(Carte), Carte, Maintenant);
        Assert.Equal(EtatRelation.Hostiles, social.RelationEntre(1, 2));
        var hostilite = Assert.Single(social.Journal, j => j.Texte.Contains("hostiles l'une à l'autre"));
        Assert.Equal(99, hostilite.CauseId);
    }

    // ───────────── Monde entier, sauvegarde et serveur ─────────────

    [Fact]
    public void Monde_ChaqueCauseEstUnEvenementAnterieur_EtLesNumerosSuiventLesFaits()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(3 * An);
        var journal = monde.Etat.JournalNonEnregistre!;
        var ids = journal.Select(e => e.Id).ToHashSet();

        Assert.Equal(journal.Count, ids.Count);
        Assert.Equal(journal.Select(e => e.Id).Order(), journal.Select(e => e.Id));
        var avecCause = journal.Where(e => e.CauseId != 0).ToList();
        Assert.NotEmpty(avecCause);
        Assert.All(avecCause, e =>
        {
            Assert.True(e.CauseId < e.Id);
            Assert.Contains(e.CauseId, ids);
        });
        Assert.Contains(journal, e => e.Type == TypeEvenement.Grossesse && e.CauseId != 0);
    }

    [Fact]
    public void Sauvegarde_GardeCausesRancunesRaisonsEtUsages_EtUnFichierVersion8Migre()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-v9-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fichier = Path.Combine(dossier, "monde.db");
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(Jour);
            var etat = monde.Etat;
            var h = etat.Humains![0];
            h.Griefs.Add(new Grief(2, MotifGrief.Meurtre, 55.5, 80, 12, 3));
            h.Raisons.Add(new Raison(TypeRaison.Solitude, 0, 4.5));
            h.Colere = 42;
            h.MeurtrierId = 5;
            h.DeuilEvenementId = 6;
            var journal = new List<EvenementJournal>(etat.JournalNonEnregistre!)
            {
                new(etat.Secondes, TypeEvenement.Conflit, 1, 2, "test") { Id = etat.ProchainIdJournal, CauseId = 4 },
            };
            etat = etat with
            {
                JournalNonEnregistre = journal,
                ProchainIdJournal = etat.ProchainIdJournal + 1,
                Offenses = [new OffenseCommunautes(1, 2, 7)],
                Communautes = [new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1, Tolerance = 0.3,
                    Usage = UsageFidelite.MalVu, DerniereAffaireId = 8, DisetteEvenementId = 9 }],
            };
            var depot = new DepotMonde(fichier);
            depot.Enregistrer(etat);

            var relu = depot.Charger()!;
            var r = relu.Humains![0];
            Assert.Equal(h.Griefs, r.Griefs);
            Assert.Equal(h.Raisons, r.Raisons);
            Assert.Equal((42.0, 5, 6L), (r.Colere, r.MeurtrierId, r.DeuilEvenementId));
            Assert.Equal(etat.Offenses, relu.Offenses);
            var c = Assert.Single(relu.Communautes!);
            Assert.Equal((0.3, UsageFidelite.MalVu, 8L, 9L), (c.Tolerance, c.Usage, c.DerniereAffaireId, c.DisetteEvenementId));
            Assert.Equal(4, depot.LireEvenement(journal[^1].Id)!.CauseId);
            Assert.Null(depot.LireEvenement(999_999));

            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion8);
            var migre = new DepotMonde(fichier).Charger()!;
            Assert.All(migre.Humains!, x => Assert.Empty(x.Griefs));
            Assert.Equal(0.5, Assert.Single(migre.Communautes!).Tolerance);
            Assert.Equal(0, new DepotMonde(fichier).LireEvenement(journal[^1].Id)!.CauseId);
            new MondeSimule(Carte, migre).AvancerDe(Jour);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Hub_Evenement_RenvoieLaCauseDUnEvenement()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();
        for (var i = 0; i < 40; i++) await connexion.InvokeAsync<Monde.Serveur.Hubs.HorlogeDto>("AvancerDe", "jour");

        var journal = await connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(Types: ["grossesse"], Limite: 10));
        var grossesse = journal.FirstOrDefault(e => e.Cause > 0);
        Assert.NotNull(grossesse);
        var cause = await connexion.InvokeAsync<EvenementDto?>("Evenement", grossesse.Cause);
        Assert.NotNull(cause);
        Assert.Equal("accouplement", cause.Type);
        Assert.Null(await connexion.InvokeAsync<EvenementDto?>("Evenement", 0L));
        Assert.Null(await connexion.InvokeAsync<EvenementDto?>("Evenement", 999_999_999L));
    }
}
