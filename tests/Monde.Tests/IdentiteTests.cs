using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Identité et histoire des communautés : noms, mots, scissions, fusions, terres parcourues, archives et chronologie.</summary>
public sealed class IdentiteTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Semaine = 7 * Jour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    /// <summary>Un point de départ sur la terre ferme.</summary>
    private static readonly (int X, int Y) Terre = (Carte.Emplacements[0].X, Carte.Emplacements[0].Y);

    private static Humain Humain(int id, int x, int y, double verbale = 100, double agreabilite = 0.6, int mere = 0, double age = 25) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = id % 2 == 0 ? Sexe.Homme : Sexe.Femme,
        PartenaireId = 0,
        Emplacement = 1,
        MereId = mere,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.8, 0.5, 0.8, agreabilite, 0.3, 0.5, 0.9, 0.5, new Intelligence(100, verbale, 100, 100, 100, 100)),
        Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Homme : Sexe.Femme),
        X = x,
        Y = y,
    };

    private static void Lier(Humain a, Humain b, double affection, bool amis)
    {
        a.Liens.Add(new Lien(b.Id, 60, affection, Maintenant, Amis: amis, Rivaux: false));
        b.Liens.Add(new Lien(a.Id, 60, affection, Maintenant, Amis: amis, Rivaux: false));
    }

    /// <summary>Un groupe d'amis rassemblés autour d'un point.</summary>
    private static List<Humain> Groupe(int premier, int nombre, int x, int y, double verbale = 100)
    {
        var groupe = Enumerable.Range(premier, nombre).Select(i => Humain(i, x + i % 3, y + i % 2, verbale)).ToList();
        foreach (var a in groupe)
        {
            foreach (var b in groupe.Where(b => b.Id > a.Id)) Lier(a, b, 60, amis: true);
        }
        return groupe;
    }

    /// <summary>Une semaine de vie de groupe : regroupements, puis terres, disettes, relations, fusions et noms.</summary>
    private static void Semaines(VieSociale social, List<Humain> tous, List<Communaute> communautes, long debut, int nombre,
        Func<bool>? arret = null)
    {
        var nature = new Ecosysteme(Carte);
        for (var s = 0; s < nombre && arret?.Invoke() != true; s++)
        {
            var t = debut + s * Semaine;
            social.Parcourir(tous, Carte, t);
            social.Regrouper(tous, communautes, t, new HashSet<string>());
            social.Territoires(tous, communautes, nature, Carte, t);
        }
    }

    // ───────────── Noms ─────────────

    [Fact]
    public void Communaute_FinitParSeDonnerUnNom_QuiVeutDireQuelqueChose()
    {
        var social = new VieSociale(new Alea(3));
        var tous = Groupe(1, 8, Terre.X, Terre.Y, verbale: 145);
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        var c = Assert.Single(communautes);
        Assert.Null(c.NomPropre);
        Assert.Single(c.Noms);

        // Pas de nom les premières semaines : le groupe doit d'abord vivre ensemble.
        Semaines(social, tous, communautes, Maintenant + Semaine, 6);
        Assert.Null(c.NomPropre);

        Semaines(social, tous, communautes, Maintenant + 8 * Semaine, 150, () => c.NomPropre is not null);
        Assert.NotNull(c.NomPropre);
        Assert.Equal($"Communauté {c.NomPropre}", c.Nom);
        Assert.NotNull(c.Sens);
        Assert.Equal(2, c.Noms.Count);
        Assert.NotEmpty(c.Lexique);
        var bapteme = Assert.Single(social.Journal, e => e.Texte.Contains("se donnent un nom", StringComparison.Ordinal));
        Assert.Contains($"« {c.NomPropre} », qui veut dire « {Recit.EnFrancais(c.Sens)} »", bapteme.Texte);
        Assert.Contains("C'est la première fois que des humains nomment leur groupe.", bapteme.Texte);
        Assert.Equal(c.Id, bapteme.CommunauteId);
        Assert.Equal(bapteme.Id, c.Noms[^1].EvenementId);
    }

    [Fact]
    public void Paysage_DecritLeauLesMontagnesEtLaForet()
    {
        var c = new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, CentreX = Terre.X, CentreY = Terre.Y, Rayon = 6 };
        var paysage = VieSociale.Paysage(c, Carte);
        Assert.NotEmpty(paysage);
        Assert.All(paysage, p => Assert.StartsWith("ceux ", p.Texte));
        Assert.Equal(paysage.Count, paysage.Select(p => p.Cle).Distinct().Count());
    }

    [Fact]
    public void Scission_EmporteLesMotsDuGroupeQuElleQuitte()
    {
        var social = new VieSociale(new Alea(4));
        var restent = Groupe(1, 6, Terre.X, Terre.Y);
        var partent = Groupe(7, 5, Terre.X + 30, Terre.Y);
        var tous = restent.Concat(partent).ToList();
        foreach (var h in tous) h.CommunauteId = 1;
        var origine = new Communaute
        {
            Id = 1, Nom = "Communauté Taluri", FondeeLe = Maintenant - An, FigureId = 1, NomPropre = "Taluri", Sens = "ceux de la rivière",
            SensCle = "riviere", Lexique = new() { ["ceux"] = "ri", ["riviere"] = "talu" },
        };
        var communautes = new List<Communaute> { origine };
        social.Communautes = communautes;

        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());

        Assert.Equal(2, communautes.Count);
        var nouvelle = communautes[1];
        Assert.Equal(1, nouvelle.OrigineId);
        Assert.Equal(origine.Lexique, nouvelle.Lexique);
        var depart = Assert.Single(social.Journal, e => e.Texte.Contains("quittent la communauté Taluri", StringComparison.Ordinal));
        Assert.Equal((nouvelle.Id, 1), (depart.CommunauteId, depart.AutreCommunauteId));
    }

    // ───────────── Fusions ─────────────

    [Fact]
    public void DeuxPetitesAlliees_DecidentDeSUnir_EtNEnFontPlusQuUne()
    {
        var social = new VieSociale(new Alea(5));
        var a = Groupe(1, 6, Terre.X, Terre.Y);
        var b = Groupe(7, 5, Terre.X + 9, Terre.Y);
        // Quelques amitiés d'un groupe à l'autre, et des figures qui s'entendent.
        foreach (var (x, y) in new[] { (0, 0), (1, 1), (2, 2), (3, 3) }) Lier(a[x], b[y], 45, amis: false);
        var tous = a.Concat(b).ToList();
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        Assert.Equal(2, communautes.Count);
        foreach (var c in communautes)
        {
            var figure = tous[c.FigureId - 1];
            var autre = tous[communautes.First(x => x.Id != c.Id).FigureId - 1];
            if (figure.LienAvec(autre.Id) is null) Lier(figure, autre, 45, amis: false);
        }
        social.ChargerRelations([new RelationCommunautes(1, 2, EtatRelation.Alliees, EvenementId: 999)]);

        Semaines(social, tous, communautes, Maintenant + 8 * Semaine, 260, () => communautes.Any(c => c.FusionneeDansId != 0));

        var fondue = Assert.Single(communautes, c => c.FusionneeDansId != 0);
        var accueil = communautes[fondue.FusionneeDansId - 1];
        Assert.True(accueil.Active);
        Assert.False(fondue.Active);
        Assert.All(tous, h => Assert.Equal(accueil.Id, h.CommunauteId));
        var decision = Assert.Single(social.Journal, e => e.Texte.Contains("décident d'unir leurs groupes", StringComparison.Ordinal));
        Assert.Equal(999, decision.CauseId);
        var fusion = Assert.Single(social.Journal, e => e.Texte.Contains("se fond dans", StringComparison.Ordinal));
        Assert.Equal(decision.Id, fusion.CauseId);
        Assert.Equal((accueil.Id, fondue.Id), (fusion.CommunauteId, fusion.AutreCommunauteId));
        Assert.DoesNotContain(social.Journal, e => e.Texte.Contains("se disperse", StringComparison.Ordinal));

        // Les nouveaux venus ne connaissent encore personne : ils restent pourtant dans le groupe.
        Semaines(social, tous, communautes, fusion.Secondes + Semaine, 8);
        Assert.All(tous, h => Assert.Equal(accueil.Id, h.CommunauteId));
    }

    // ───────────── Terres ─────────────

    [Fact]
    public void Terres_SuiventLesPassages_EtOublientCeQuiNEstPlusParcouruDepuisUnAn()
    {
        var social = new VieSociale(new Alea(6));
        var tous = Groupe(1, 6, Terre.X, Terre.Y);
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        var c = Assert.Single(communautes);

        social.Parcourir(tous, Carte, Maintenant);
        var avant = c.Territoire.Keys.ToHashSet();
        Assert.Contains(Carte.Indice(Terre.X, Terre.Y), avant);
        var etendue = VieSociale.Etendue(avant, Carte);
        Assert.True(etendue.Length > avant.Count);
        Assert.Equal(etendue.Order(), etendue);
        Assert.All(etendue, i => Assert.True((Biome)Carte.Biomes[i] is not (Biome.Ocean or Biome.EauPeuProfonde)));

        // Le groupe part plus loin ; un an plus tard, ses anciennes terres ne sont plus les siennes.
        foreach (var h in tous) h.X += 12;
        social.Parcourir(tous, Carte, Maintenant + An / 2);
        Assert.Superset(avant, c.Territoire.Keys.ToHashSet());
        social.Territoires(tous, communautes, new Ecosysteme(Carte), Carte, Maintenant + An + Jour);
        Assert.Empty(avant.Intersect(c.Territoire.Keys));
        Assert.NotEmpty(c.Territoire);
    }

    [Fact]
    public void Cases_EncodeesEnPlages_SeRelisent_EtUnFichierCorrompuEstRefuse()
    {
        int[] cases = [0, 1, 2, 3, 10, 11, 500, 65_535];
        Assert.Equal(cases, DepotMonde.DecoderCases(DepotMonde.EncoderCases(cases)));
        Assert.Empty(DepotMonde.DecoderCases(DepotMonde.EncoderCases([])));
        Assert.Throws<ArgumentException>(() => DepotMonde.EncoderCases([5, 3]));
        // Une plage de longueur nulle, ou qui déborde de toute carte possible.
        Assert.Throws<InvalidDataException>(() => DepotMonde.DecoderCases([1, 0]));
        Assert.Throws<InvalidDataException>(() => DepotMonde.DecoderCases([0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x05]));
        Assert.Equal([0, 4, 10, 2], Territoires.Plages([0, 1, 2, 3, 10, 11]));
    }

    // ───────────── Monde entier, sauvegarde et serveur ─────────────

    [Fact]
    public void Monde_ArchiveChaqueSaison_EtRattacheLesEvenementsAuxCommunautes()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(2 * An);
        var etat = monde.Etat;
        var communautes = etat.Communautes!;
        Assert.NotEmpty(communautes);
        var archives = etat.ArchivesNonEnregistrees!;
        Assert.NotEmpty(archives);
        // Le premier jour de chaque saison (les jours du monde commencent à 6 h).
        Assert.All(archives, a => Assert.InRange(a.Secondes % (Calendrier.JoursParSaison * Jour), 0, Jour - 1));
        Assert.Contains(archives, a => a.Cases.Length > 0 && a.Membres >= 5);
        Assert.All(communautes.Where(c => c.Active), c => Assert.NotEmpty(c.Territoire));
        Assert.All(communautes, c => Assert.NotEmpty(c.Noms));

        var journal = etat.JournalNonEnregistre!;
        var formations = journal.Where(e => e.Type == TypeEvenement.Communaute && e.Texte.StartsWith("Une communauté se forme", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(formations);
        Assert.All(formations, e => Assert.NotEqual(0, e.CommunauteId));
        // Les naissances appartiennent à la communauté de la mère.
        Assert.Contains(journal, e => e.Type == TypeEvenement.Naissance && e.CommunauteId != 0);
    }

    [Fact]
    public void Sauvegarde_GardeNomsMotsTerresEtArchives_EtUnFichierVersion9Migre()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-identite-" + Guid.NewGuid().ToString("N"));
        var fichier = Path.Combine(dossier, "monde.db");
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(An);
            var etat = monde.Etat;
            var c = etat.Communautes![0];
            c.NomPropre = "Kaluri";
            c.Sens = "ceux de la rivière";
            c.SensCle = "riviere";
            c.Lexique = new() { ["ceux"] = "ri", ["riviere"] = "kalu" };
            c.Noms.Add(new NomCommunaute("Communauté Kaluri", "ceux de la rivière", etat.Secondes, 5));
            c.Nom = "Communauté Kaluri";
            (c.Migrations, c.DisettesSurmontees, c.AccueilLe, c.FusionVersId) = (2, 1, 123, 0);

            var depot = new DepotMonde(fichier);
            depot.Enregistrer(etat);
            var relu = depot.Charger()!;
            var r = relu.Communautes![0];
            Assert.Equal((c.Nom, c.NomPropre, c.Sens, c.SensCle), (r.Nom, r.NomPropre, r.Sens, r.SensCle));
            Assert.Equal(c.Lexique, r.Lexique);
            Assert.Equal(c.Noms, r.Noms);
            Assert.Equal(c.Territoire, r.Territoire);
            Assert.Equal((2, 1, 123L), (r.Migrations, r.DisettesSurmontees, r.AccueilLe!.Value));
            Assert.Equal(etat.Relations ?? [], relu.Relations ?? []);

            var dates = depot.LireDatesArchives();
            Assert.Equal(etat.ArchivesNonEnregistrees!.Select(a => a.Secondes).Distinct().Order(), dates);
            var archive = etat.ArchivesNonEnregistrees![0];
            var relue = Assert.Single(depot.LireArchives(archive.Secondes), a => a.CommunauteId == archive.CommunauteId);
            Assert.Equal(archive.Cases, relue.Cases);
            Assert.Equal(archive.Membres, depot.LireTailles(archive.CommunauteId)[0].Membres);
            var chronologie = depot.LireJournal(communaute: c.Id, limite: 500);
            Assert.NotEmpty(chronologie);
            Assert.All(chronologie, e => Assert.True(e.CommunauteId == c.Id || e.AutreCommunauteId == c.Id));

            // Un fichier de la version 9 : les événements de communauté sont rattachés d'après leur texte.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion9);
            var migre = new DepotMonde(fichier);
            var etatMigre = migre.Charger()!;
            var m = etatMigre.Communautes![0];
            Assert.Null(m.NomPropre);
            Assert.Equal("Communauté Kaluri", Assert.Single(m.Noms).Nom);
            Assert.Empty(m.Territoire);
            var formation = etat.JournalNonEnregistre!.First(e => e.Type == TypeEvenement.Communaute
                && e.Texte.StartsWith("Une communauté se forme", StringComparison.Ordinal));
            Assert.Equal(0, migre.LireEvenement(formation.Id)!.CommunauteId); // le texte ne cite pas de nom : rien à rattacher
            // (la communauté renommée plus haut ne porte plus le nom cité par le texte : on en prend une autre)
            var dispersion = etat.JournalNonEnregistre!.FirstOrDefault(e => e.Texte.EndsWith("se disperse.", StringComparison.Ordinal) && e.CommunauteId != c.Id);
            if (dispersion is not null) Assert.Equal(dispersion.CommunauteId, migre.LireEvenement(dispersion.Id)!.CommunauteId);
            new MondeSimule(Carte, etatMigre).AvancerDe(Jour);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Hub_TerritoiresDatesTaillesEtChronologie()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();
        for (var i = 0; i < 400; i++) await connexion.InvokeAsync<Monde.Serveur.Hubs.HorlogeDto>("AvancerDe", "jour");

        var actuels = await connexion.InvokeAsync<TerritoiresDto>("Territoires", (long?)null);
        Assert.True(actuels.Actuel);
        Assert.NotEmpty(actuels.Communautes);
        Assert.All(actuels.Communautes, t => Assert.True(t.Plages.Length % 2 == 0 && t.Plages.Length > 0));

        var dates = await connexion.InvokeAsync<List<DateArchiveDto>>("DatesTerritoires");
        Assert.NotEmpty(dates);
        var passe = await connexion.InvokeAsync<TerritoiresDto?>("Territoires", (long?)dates[^1].Secondes);
        Assert.NotNull(passe);
        Assert.False(passe.Actuel);
        Assert.Null(await connexion.InvokeAsync<TerritoiresDto?>("Territoires", (long?)-5));
        Assert.Null(await connexion.InvokeAsync<TerritoiresDto?>("Territoires", (long?)7));

        var id = actuels.Communautes[0].Id;
        Assert.NotEmpty(await connexion.InvokeAsync<List<TailleDto>>("TaillesCommunaute", id));
        Assert.Empty(await connexion.InvokeAsync<List<TailleDto>>("TaillesCommunaute", 0));
        var chronologie = await connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(Types: ["communaute"], Communaute: id));
        Assert.NotEmpty(chronologie);
        Assert.All(chronologie, e => Assert.True(e.Communaute == id || e.AutreCommunaute == id));
        var communaute = (await connexion.InvokeAsync<List<CommunauteDto>>("Communautes")).First(c => c.Id == id);
        Assert.True(communaute.Terres > 0);
        Assert.NotEmpty(communaute.Noms);

        // Courbes démographiques : le monde de mois en mois, cohérent avec les naissances et les morts ; les communautés les plus peuplées.
        var demo = await connexion.InvokeAsync<DemographieDto>("Demographie", 3);
        Assert.Equal(demo.Secondes.Length, demo.Monde.Length);
        Assert.Equal(demo.Maintenant, demo.Secondes[^1]);
        Assert.Equal(66, demo.Monde[0]);
        for (var k = 1; k < demo.Monde.Length; k++) Assert.Equal(demo.Monde[k - 1] + demo.Naissances[k] - demo.Morts[k], demo.Monde[k]);
        Assert.InRange(demo.Communautes.Count, 1, 3);
        Assert.True(demo.Communautes.Zip(demo.Communautes.Skip(1)).All(p => p.First.Pic >= p.Second.Pic));
        Assert.All(demo.Communautes, c => Assert.Equal(c.Secondes.Length, c.Membres.Length));
        Assert.Contains(demo.Communautes, c => c.Active && c.Secondes[^1] == demo.Maintenant);
        Assert.InRange((await connexion.InvokeAsync<DemographieDto>("Demographie", 500)).Communautes.Count, 1, 30);
    }
}
