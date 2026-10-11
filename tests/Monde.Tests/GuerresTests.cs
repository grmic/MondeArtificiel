using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Raids, guerres, pactes, défaites et soumission, héritage des querelles ; placement des couples ; sauvegardes.</summary>
public sealed class GuerresTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, int x, int y, Sexe sexe, double agreabilite = 0.5, double conscience = 0.5) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - 25 * An,
        Personnalite = new Personnalite(0.5, conscience, 0.5, agreabilite, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
        Reserves = 60,
    };

    private static void Lier(IEnumerable<Humain> a, IEnumerable<Humain> b, double affection)
    {
        foreach (var x in a)
        {
            foreach (var y in b)
            {
                if (x.Id == y.Id) continue;
                x.Liens.RemoveAll(l => l.Id == y.Id);
                x.Liens.Add(new Lien(y.Id, 60, affection, Maintenant, Amis: affection >= 40));
            }
        }
    }

    /// <summary>
    /// Deux groupes voisins qui se haïssent : A (ids 1 à 8, six hommes querelleurs) et B (ids 9 à 14, un seul homme),
    /// à <paramref name="ecart"/> cases l'un de l'autre. Les membres de A ont une rancune de meurtre contre ceux de B.
    /// </summary>
    private static (VieSociale Social, List<Humain> Tous, List<Communaute> Communautes, Ecosysteme Nature) DeuxGroupes(int ecart = 10)
    {
        var e = Carte.Emplacements[0];
        var a = Enumerable.Range(1, 8).Select(i => Humain(i, e.X + i % 2, e.Y, i <= 6 ? Sexe.Homme : Sexe.Femme, agreabilite: 0.2)).ToList();
        var b = Enumerable.Range(9, 6).Select(i => Humain(i, e.X + ecart + i % 2, e.Y, i == 9 ? Sexe.Homme : Sexe.Femme, agreabilite: 0.9, conscience: 0.9)).ToList();
        Lier(a, a, 60);
        Lier(b, b, 60);
        Lier(a, b, -40);
        Lier(b, a, -40);
        foreach (var h in a) h.Griefs.Add(new Grief(9, MotifGrief.Meurtre, 100, 100, 5));
        var tous = a.Concat(b).ToList();
        var social = new VieSociale(new Alea(5)) { Carte = Carte };
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        Assert.Equal(2, communautes.Count(c => c.Active));
        return (social, tous, communautes, new Ecosysteme(Carte));
    }

    /// <summary>Semaines de vie sociale (relations, guerres) jusqu'à ce que la condition soit remplie ; rend la date atteinte.</summary>
    private static long Semaines(VieSociale social, List<Humain> tous, List<Communaute> communautes, Ecosysteme nature, long depuis,
        Func<bool> fini, int maximum = 60)
    {
        var t = depuis;
        for (var k = 0; k < maximum && !fini(); k++)
        {
            t += 7 * Jour;
            social.Territoires(tous, communautes, nature, Carte, t);
        }
        Assert.True(fini(), $"rien après {maximum} semaines ; relation {social.RelationEntre(1, 2)}");
        return t;
    }

    [Fact]
    public void Raid_LanceContreUnEnnemiHaiEtPlusFaible_PuisAssautALAube()
    {
        var (social, tous, communautes, nature) = DeuxGroupes();
        var (a, b) = (communautes[0], communautes[1]);
        Assert.Equal(1, tous[0].CommunauteId);
        var t = Semaines(social, tous, communautes, nature, Maintenant, () => a.Raid is not null);

        var raid = a.Raid!;
        Assert.Equal(b.Id, raid.CibleId);
        Assert.InRange(raid.Guerriers.Count, 2, 6);
        Assert.All(raid.Guerriers, id => Assert.Equal(Sexe.Homme, tous[id - 1].Sexe));
        Assert.Equal(EtatRelation.Hostiles, social.RelationEntre(a.Id, b.Id));
        var depart = Assert.Single(social.Journal, j => j.Type == TypeEvenement.Guerre && j.Texte.Contains("entraîne"));
        Assert.Contains("pour venger les siens", depart.Texte);
        Assert.Equal((a.Id, b.Id), (depart.CommunauteId, depart.AutreCommunauteId));
        Assert.Equal(raid.EvenementId, depart.Id);

        // La troupe arrive au camp ennemi pendant la nuit ; l'assaut part à l'aube.
        foreach (var id in raid.Guerriers) (tous[id - 1].X, tous[id - 1].Y) = (tous[8].X, tous[8].Y);
        var comportement = new Comportement(Carte, nature, new Alea(11)) { Social = social };
        var aube = t - t % Jour + Jour + 5 * Calendrier.SecondesParHeure;
        comportement.Raids(tous, aube);

        var guerre = Assert.Single(social.Guerres);
        Assert.Equal((a.Id, b.Id, 1, IssueGuerre.EnCours), (guerre.A, guerre.B, guerre.Raids, guerre.Issue));
        Assert.Equal(EtatRelation.EnGuerre, social.RelationEntre(a.Id, b.Id));
        var assaut = Assert.Single(comportement.Journal, j => j.Texte.StartsWith("Au petit jour"));
        Assert.Equal(raid.EvenementId, assaut.CauseId);
        Assert.Equal(assaut.Id, guerre.EvenementId);
        Assert.True(raid.Retour);
        // Les morts du combat sont reliés à la guerre et à leur tueur.
        Assert.Equal(guerre.MortsA + guerre.MortsB, tous.Count(h => h.CauseDeMort == CauseDeMort.Guerre));
        Assert.All(tous.Where(h => h.CauseDeMort == CauseDeMort.Guerre), h => Assert.NotEqual(0, h.MeurtrierId));
    }

    [Fact]
    public void Defaite_LeVaincuSeSoumetEtPaieTribut_PuisLaGuerreSeTransmetAuGroupeRecompose()
    {
        var (social, tous, communautes, nature) = DeuxGroupes(ecart: 40);
        var (a, b) = (communautes[0], communautes[1]);
        // Deux raids ont coûté deux morts à B (les liens de A restent haineux).
        var g = social.Ensanglanter(a, b, 0, 2, 77, Maintenant);
        Assert.Equal(EtatRelation.EnGuerre, social.RelationEntre(a.Id, b.Id));
        foreach (var h in tous.Skip(8)) h.Griefs.Clear();
        foreach (var h in tous.Take(8)) h.Griefs.Clear();

        var t = Semaines(social, tous, communautes, nature, Maintenant, () => g.Issue != IssueGuerre.EnCours);
        Assert.Equal(IssueGuerre.Soumission, g.Issue);
        Assert.Equal(a.Id, g.VainqueurId);
        Assert.Equal(a.Id, b.SoumiseAId);
        Assert.Contains(social.Journal, j => j.Id == g.FinEvenementId && j.Texte.Contains("se soumet à la") && j.CauseId == 77);

        // Chaque semaine, B livre une part de ses réserves à A.
        var (avantB, avantA) = (tous.Skip(8).Sum(h => h.Reserves), tous.Take(8).Sum(h => h.Reserves));
        social.Territoires(tous, communautes, nature, Carte, t + 7 * Jour);
        Assert.True(tous.Skip(8).Sum(h => h.Reserves) < avantB);
        Assert.True(tous.Take(8).Sum(h => h.Reserves) > avantA);

        // Une nouvelle guerre ; puis A se recompose sous un autre nom : le groupe qui la continue hérite de la guerre et des vassaux.
        b.SoumiseAId = 0;
        var nouvelle = social.Ensanglanter(b, a, 1, 0, 88, t + 8 * Jour);
        var suite = new Communaute { Id = communautes.Count + 1, Nom = "Communauté de H2", FondeeLe = t + 9 * Jour, FigureId = 2 };
        communautes.Add(suite);
        a.DisperseeLe = t + 9 * Jour;
        a.HeritiereId = suite.Id;
        foreach (var h in tous.Take(8)) h.CommunauteId = suite.Id;
        social.Journal.Clear();
        social.Territoires(tous, communautes, nature, Carte, t + 14 * Jour);
        Assert.Same(suite, social.Heritiere(a));
        Assert.Equal((b.Id, suite.Id, IssueGuerre.EnCours), (nouvelle.A, nouvelle.B, nouvelle.Issue));
        Assert.Equal(EtatRelation.EnGuerre, social.RelationEntre(b.Id, suite.Id));
        Assert.Contains(social.Journal, j => j.Texte.Contains("poursuivent la guerre") && j.CommunauteId == suite.Id && j.CauseId == 88);
        Assert.Equal(a.Bellicisme, suite.Bellicisme);
    }

    [Fact]
    public void GuerresPactesRaidsEtSoumissions_SeSauvegardentEtLeMondeReprendALIdentique()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-guerres-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(2 * Jour);
            var etat = monde.Etat;
            // Deux communautés posées à la main : les deux premiers couples.
            var communautes = new List<Communaute>
            {
                new() { Id = 1, Nom = "Communauté de A", FondeeLe = etat.Secondes, FigureId = 1 },
                new() { Id = 2, Nom = "Communauté de B", FondeeLe = etat.Secondes, FigureId = 3 },
            };
            foreach (var h in etat.Humains!.Take(4)) h.CommunauteId = h.Id <= 2 ? 1 : 2;
            var (a, b) = (communautes[0], communautes[1]);
            var membreA = etat.Humains!.First(h => h.EstVivant && h.CommunauteId == a.Id);
            a.Bellicisme = 0.42;
            a.Raid = new Raid
            {
                CibleId = b.Id, X = b.CentreX, Y = b.CentreY, RetourX = a.CentreX, RetourY = a.CentreY, Depuis = etat.Secondes - Jour,
                EvenementId = 3, Guerriers = [membreA.Id],
            };
            a.DernierRaidLe = etat.Secondes - Jour;
            b.SoumiseAId = a.Id;
            b.SoumiseLe = etat.Secondes - 2 * Jour;
            b.SoumissionEvenementId = 4;
            b.PaixEvenementId = 5;
            b.HeritiereId = 0;
            var guerres = new List<Guerre>
            {
                new() { Id = 1, A = a.Id, B = b.Id, Debut = 100, Fin = 200, MortsA = 1, MortsB = 3, Raids = 2, Issue = IssueGuerre.Soumission,
                    DernierSangLe = 150, EvenementId = 3, FinEvenementId = 4, VainqueurId = a.Id },
            };
            var relations = new List<RelationCommunautes> { new(a.Id, b.Id, EtatRelation.Alliees, 6, 7) };
            etat = etat with { Communautes = communautes, Guerres = guerres, Relations = relations };

            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;

            var g = Assert.Single(relu.Guerres!);
            Assert.Equivalent(guerres[0], g);
            Assert.Equal(relations, relu.Relations);
            var (ra, rb) = (relu.Communautes!.Single(c => c.Id == a.Id), relu.Communautes!.Single(c => c.Id == b.Id));
            Assert.Equal((0.42, a.DernierRaidLe), (ra.Bellicisme, ra.DernierRaidLe));
            Assert.Equivalent(a.Raid, ra.Raid);
            Assert.Equal((a.Id, b.SoumiseLe, 4L, 5L), (rb.SoumiseAId, rb.SoumiseLe, rb.SoumissionEvenementId, rb.PaixEvenementId));

            // Le monde rechargé continue exactement comme l'original.
            var original = new MondeSimule(Carte, etat);
            var recharge = new MondeSimule(Carte, relu);
            original.AvancerDe(10 * Jour);
            recharge.AvancerDe(10 * Jour);
            Assert.Equal(original.Etat.Humains!.Select(h => (h.X, h.Y, h.Sante, h.Activite)), recharge.Etat.Humains!.Select(h => (h.X, h.Y, h.Sante, h.Activite)));
            Assert.Equal(original.Guerres().Count, recharge.Guerres().Count);

            // Un monde de la version 13 se met à jour : aucune guerre, tempéraments à établir.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion13);
            var ancien = new DepotMonde(fichier).Charger()!;
            Assert.Null(ancien.Guerres);
            Assert.All(ancien.Communautes!, c => Assert.Equal((-1.0, 0, (Raid?)null), (c.Bellicisme, c.SoumiseAId, c.Raid)));
            Assert.Equal(1, ancien.Configuration?.Placement ?? 1);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    // ───────────── Placement des couples ─────────────

    /// <summary>Numéro de masse de terre de chaque case (−1 en mer), et taille de chaque masse.</summary>
    private static (int[] Masse, List<int> Tailles) Masses(Carte carte)
    {
        var masse = Enumerable.Repeat(-1, carte.Largeur * carte.Hauteur).ToArray();
        var tailles = new List<int>();
        var file = new Queue<int>();
        for (var depart = 0; depart < masse.Length; depart++)
        {
            if (masse[depart] >= 0 || Biomes.EstEauSalee((Biome)carte.Biomes[depart])) continue;
            masse[depart] = tailles.Count;
            var taille = 0;
            file.Enqueue(depart);
            while (file.Count > 0)
            {
                var i = file.Dequeue();
                taille++;
                int x = i % carte.Largeur, y = i / carte.Largeur;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                    var j = ny * carte.Largeur + nx;
                    if (masse[j] >= 0 || Biomes.EstEauSalee((Biome)carte.Biomes[j])) continue;
                    masse[j] = masse[depart];
                    file.Enqueue(j);
                }
            }
            tailles.Add(taille);
        }
        return (masse, tailles);
    }

    [Fact]
    public void Placement_SurTousLesContinentsOuSurUnContinentChoisi()
    {
        var parTaille = (Carte c) =>
        {
            var (masse, tailles) = Masses(c);
            var rang = tailles.Select((t, k) => (t, k)).OrderByDescending(x => x.t).ThenBy(x => x.k).Select(x => x.k).ToList();
            return c.Emplacements.Select(e => rang.IndexOf(masse[c.Indice(e.X, e.Y)]) + 1).ToList();
        };

        var tous = GenerateurContinent.Generer(new ConfigurationMonde(42, 20, 384, 384, 3, ConfigurationMonde.PlacementTous));
        var rangs = parTaille(tous);
        Assert.Equal(20, rangs.Count);
        Assert.Equal([1, 2, 3], rangs.Distinct().Order());
        // Le plus grand reçoit le plus de couples.
        Assert.True(rangs.Count(r => r == 1) >= rangs.Count(r => r == 3));

        var deuxieme = GenerateurContinent.Generer(new ConfigurationMonde(42, 10, 384, 384, 3, 2));
        Assert.All(parTaille(deuxieme), r => Assert.Equal(2, r));
        // Même carte, seuls les emplacements changent.
        Assert.Equal(tous.Biomes, deuxieme.Biomes);

        Assert.NotNull(new ConfigurationMonde(42, 10, 384, 384, 3, 4).Erreur());
        Assert.NotNull(new ConfigurationMonde(42, 10, 384, 384, 3, -1).Erreur());
        Assert.Throws<ArgumentOutOfRangeException>(() => GenerateurContinent.Generer(new ConfigurationMonde(42, 10, 384, 384, 2, 3)));
        Assert.Equal(ConfigurationMonde.ParDefaut(42), new ConfigurationMonde(42, 33, 256, 256, 1, 1));
    }

    [Theory]
    [InlineData(10, new[] { 100, 50, 0 }, new[] { 6, 4, 0 })]
    [InlineData(2, new[] { 5, 5, 5 }, new[] { 1, 1, 0 })]
    [InlineData(3, new[] { 1000, 10, 10 }, new[] { 1, 1, 1 })]
    [InlineData(4, new[] { 0, 0 }, new[] { 0, 0 })]
    public void Repartition_EnProportionDesTerresHabitables(int couples, int[] habitables, int[] attendu) =>
        Assert.Equal(attendu, GenerateurContinent.Repartir(couples, habitables));

    // ───────────── Sauvegardes ─────────────

    [Fact]
    public async Task Hub_SauvegarderPuisRestaurer_RevientALaDateSauvegardee_EtGardeLeMondeRemplace()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        Assert.Empty(await connexion.InvokeAsync<List<SauvegardeDto>>("Sauvegardes"));
        await connexion.InvokeAsync<HorlogeDto>("AvancerDe", "jour");
        var liste = await connexion.InvokeAsync<List<SauvegardeDto>>("Sauvegarder");
        var sauvegarde = Assert.Single(liste);
        Assert.True(sauvegarde.Possible);
        Assert.Equal(66, sauvegarde.Vivants);
        Assert.StartsWith("An 1, jour 2", sauvegarde.DateMonde);
        Assert.Matches("^sauvegarde-an1-[0-9]{8}-[0-9]{6}\\.db$", sauvegarde.Nom);

        await connexion.InvokeAsync<HorlogeDto>("AvancerDe", "jour");
        await connexion.InvokeAsync<HorlogeDto>("AvancerDe", "jour");
        var horloge = await connexion.InvokeAsync<HorlogeDto>("Restaurer", sauvegarde.Nom);
        Assert.StartsWith("An 1, jour 2", horloge.Texte);
        Assert.Equal(66, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);

        // Le monde remplacé a été sauvegardé avant ; la sauvegarde restaurée n'a pas changé.
        liste = await connexion.InvokeAsync<List<SauvegardeDto>>("Sauvegardes");
        Assert.Equal(2, liste.Count);
        var avant = Assert.Single(liste, s => s.Nom.StartsWith("avant-restauration-", StringComparison.Ordinal));
        Assert.StartsWith("An 1, jour 4", avant.DateMonde);
        Assert.StartsWith("An 1, jour 2", liste.Single(s => s.Nom == sauvegarde.Nom).DateMonde);

        // Un nom qui n'est pas dans la liste est refusé, chemins compris ; rien ne change.
        foreach (var nom in new[] { "../monde.db", "monde.db", "/etc/passwd", "", "inconnue.db" })
        {
            await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<HorlogeDto>("Restaurer", nom));
        }
        Assert.StartsWith("An 1, jour 2", (await connexion.InvokeAsync<HorlogeDto>("Horloge")).Texte);

        // Un fichier illisible est listé mais refusé.
        File.WriteAllText(Path.Combine(usine.Dossier, "mondes", "sauvegardes", "abime.db"), "pas une base");
        var abime = Assert.Single(await connexion.InvokeAsync<List<SauvegardeDto>>("Sauvegardes"), s => s.Nom == "abime.db");
        Assert.False(abime.Possible);
        var refus = await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<HorlogeDto>("Restaurer", "abime.db"));
        Assert.Contains("illisible", refus.Message);
        Assert.DoesNotContain(usine.Dossier, refus.Message);
        Assert.Equal(66, (await connexion.InvokeAsync<List<HumainDto>>("Humains")).Count);
    }
}
