using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>
/// Un vrai monde de deux ans, vie quotidienne notée, enregistré comme le fait le serveur : assez de journal pour
/// plusieurs paquets d'archives. Fait une fois pour tous les tests de <see cref="JournalBaseTests"/>.
/// </summary>
public sealed class MondeAvecJournal : IDisposable
{
    public string Dossier { get; } = Path.Combine(Path.GetTempPath(), "monde-journal-" + Guid.NewGuid().ToString("N"));

    public string Fichier => Path.Combine(Dossier, "monde.db");

    public long Secondes { get; }

    public MondeAvecJournal()
    {
        Directory.CreateDirectory(Dossier);
        var conf = new ConfigurationMonde(11, 12, 64, 64, 1);
        var carte = GenerateurContinent.Generer(conf);
        var monde = new MondeSimule(carte, new MondeSimule(carte, MondeSimule.Nouveau(conf)).Etat) { JournalQuotidien = true };
        var depot = new DepotMonde(Fichier);
        for (var mois = 0; mois < 24; mois++)
        {
            monde.AvancerDe(Calendrier.SecondesParAn / 12);
            var etat = monde.Etat;
            depot.Enregistrer(etat);
            monde.MarquerJournalEnregistre(etat.JournalNonEnregistre?.Count ?? 0, etat.ArchivesNonEnregistrees?.Count ?? 0,
                (etat.Catastrophes ?? []).Where(c => !c.Active).Select(c => c.Id).ToHashSet());
        }
        Secondes = monde.Secondes;
    }

    /// <summary>Une copie du monde, à modifier sans toucher aux autres tests.</summary>
    public DepotMonde Copie(string nom)
    {
        var chemin = Path.Combine(Dossier, nom + ".db");
        new DepotMonde(Fichier).CopierVers(chemin);
        return new DepotMonde(chemin);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Dossier)) Directory.Delete(Dossier, recursive: true);
    }
}

/// <summary>0.31.4 : archives compressées du journal, effacement du journal ancien, copie pour analyse.</summary>
public sealed class JournalBaseTests(MondeAvecJournal monde) : IClassFixture<MondeAvecJournal>
{
    private static string Ligne(EvenementJournal e) =>
        $"{e.Id}|{e.Secondes}|{e.Type}|{e.HumainId}|{e.AutreId}|{e.Brut}|{e.Recit?.Encoder()}|{e.Origine}|{e.CauseId}|{e.CommunauteId}|" +
        $"{e.AutreCommunauteId}|{e.Significatif}|{e.Karma}|{e.KarmaAutre}";

    /// <summary>Tout le journal par pages de <paramref name="page"/>, comme le fait la fenêtre Journal en défilant.</summary>
    private static List<string> ToutLire(DepotMonde depot, int page, IReadOnlyCollection<TypeEvenement>? types = null, int? humain = null,
        int? communaute = null, bool significatifs = false)
    {
        var lignes = new List<string>();
        long? avant = null;
        while (true)
        {
            var lus = depot.LireJournal(types, humain, avant, page, communaute, significatifs);
            lignes.AddRange(lus.Select(Ligne));
            if (lus.Count < page) return lignes;
            avant = lus[^1].Id;
        }
    }

    private static int Archiver(DepotMonde depot, long avant)
    {
        var total = 0;
        for (int n; (n = depot.ArchiverJournal(avant)) > 0;) total += n;
        return total;
    }

    private static long Purger(DepotMonde depot, ReglesPurge regles)
    {
        long total = 0;
        while (true)
        {
            var (n, pas) = depot.PurgerJournal(regles);
            total += n;
            if (!pas) return total;
        }
    }

    [Fact]
    public void Archives_LeJournalSeLitALIdentique()
    {
        var clair = monde.Copie("clair-lecture");
        var archive = monde.Copie("archive-lecture");
        var rangees = Archiver(archive, monde.Secondes - Calendrier.SecondesParAn);
        Assert.True(rangees >= 2 * DepotMonde.LignesParBloc, $"seulement {rangees} lignes rangées");
        Assert.Equal(clair.CompterJournal().Table, archive.CompterJournal().Table + archive.CompterJournal().Archivees);

        var tous = ToutLire(clair, 500);
        Assert.Equal(tous, ToutLire(archive, 500));
        Assert.Equal(tous, ToutLire(archive, 37)); // pages qui ne tombent pas sur les bords des paquets
        Assert.Equal(ToutLire(clair, 100, significatifs: true), ToutLire(archive, 100, significatifs: true));
        TypeEvenement[] types = [TypeEvenement.Pensee, TypeEvenement.Naissance];
        Assert.Equal(ToutLire(clair, 200, types), ToutLire(archive, 200, types));
        foreach (var h in new[] { 1, 2, 7, 15 })
        {
            Assert.Equal(ToutLire(clair, 150, humain: h), ToutLire(archive, 150, humain: h));
        }
        var communaute = clair.LireJournal(limite: 500).Select(e => e.CommunauteId).FirstOrDefault(c => c != 0);
        if (communaute != 0) Assert.Equal(ToutLire(clair, 150, communaute: communaute), ToutLire(archive, 150, communaute: communaute));

        // Un événement par son numéro (chaîne des causes), qu'il soit dans un paquet ou dans la table.
        var ids = clair.LireJournal(limite: 500, avantId: clair.LireJournal(limite: 1)[0].Id - 20_000).Select(e => e.Id).Take(50).ToList();
        Assert.NotEmpty(ids);
        foreach (var id in ids) Assert.Equal(Ligne(clair.LireEvenement(id)!), Ligne(archive.LireEvenement(id)!));
    }

    [Fact]
    public void Effacer_GardeSuivisEtFaitsMarquants_AvecOuSansArchives()
    {
        var clair = monde.Copie("clair-purge");
        var archive = monde.Copie("archive-purge");
        Archiver(archive, monde.Secondes - Calendrier.SecondesParAn / 2);
        // Un humain suivi : celui qui apparaît le plus au journal.
        var suivi = clair.LireJournal(limite: 500).GroupBy(e => e.HumainId).Where(g => g.Key != 0).MaxBy(g => g.Count())!.Key;
        foreach (var d in new[] { clair, archive }) Assert.True(d.ChangerSuivis([suivi], []));

        var date = monde.Secondes - Calendrier.SecondesParAn;
        var regles = new ReglesPurge(date, null, Marquants: false, Suivis: false);
        var avant = ToutLire(clair, 500);
        var effacees = Purger(clair, regles);
        Assert.True(effacees > 0);
        Assert.Equal(effacees, Purger(archive, regles));
        var apres = ToutLire(clair, 500);
        Assert.Equal(apres, ToutLire(archive, 500));

        // Ce qui reste d'avant la date : faits marquants et lignes de l'humain suivi seulement.
        var tout = new List<EvenementJournal>();
        for (long? a = null; ;)
        {
            var page = clair.LireJournal(avantId: a, limite: 500);
            tout.AddRange(page);
            if (page.Count < 500) break;
            a = page[^1].Id;
        }
        var anciens = tout.Where(e => e.Secondes < date - Calendrier.SecondesParJour).ToList();
        Assert.NotEmpty(anciens);
        Assert.All(anciens, e => Assert.True(e.Significatif || PremieresFois.Est(e) || e.HumainId == suivi || e.AutreId == suivi));
        Assert.Contains(anciens, e => e.Significatif);
        Assert.Contains(anciens, e => !e.Significatif && (e.HumainId == suivi || e.AutreId == suivi));
        // Rien de récent n'est touché.
        Assert.Equal(avant.Count(l => long.Parse(l.Split('|')[1]) >= date + Calendrier.SecondesParJour),
            apres.Count(l => long.Parse(l.Split('|')[1]) >= date + Calendrier.SecondesParJour));
        // Une seconde passe ne trouve plus rien.
        Assert.Equal(0, Purger(clair, regles));

        // Cases cochées : le suivi et les faits marquants partent aussi (cases changées : tout est reparcouru).
        Purger(clair, regles with { Marquants = true, Suivis = true });
        Assert.DoesNotContain(clair.LireJournal(limite: 500, avantId: clair.LireJournal(limite: 1)[0].Id),
            e => e.Secondes < date - Calendrier.SecondesParJour && !PremieresFois.Est(e));
    }

    [Fact]
    public void PremieresFois_JamaisEffacees_NiRangees()
    {
        var depot = monde.Copie("premieres");
        var avant = depot.LireJournal(limite: 500, premieres: true);
        Assert.NotEmpty(avant);
        Assert.All(avant, e => Assert.True(PremieresFois.Est(e)));
        Archiver(depot, monde.Secondes);
        Purger(depot, new ReglesPurge(monde.Secondes, null, Marquants: true, Suivis: true));
        Assert.Equal(avant.Select(Ligne), depot.LireJournal(limite: 500, premieres: true).Select(Ligne));
    }

    [Fact]
    public void Effacer_VieQuotidienneSeule()
    {
        var depot = monde.Copie("quotidien");
        var date = monde.Secondes - Calendrier.SecondesParAn;
        Purger(depot, new ReglesPurge(null, date, Marquants: false, Suivis: false));
        var anciens = ToutLire(depot, 500).Select(l => l.Split('|')).Where(c => long.Parse(c[1]) < date - Calendrier.SecondesParJour).ToList();
        Assert.Contains(anciens, c => c[2] == nameof(TypeEvenement.Naissance) || c[2] == nameof(TypeEvenement.Mort));
        Assert.DoesNotContain(anciens, c => c[11] == "False" && ReglesPurge.Quotidien.Select(t => t.ToString()).Contains(c[2]));
        // La vie quotidienne récente est toujours là.
        Assert.NotEmpty(depot.LireJournal(types: ReglesPurge.Quotidien.ToList(), limite: 10));
    }

    [Fact]
    public void CopiePourAnalyse_RemetToutEnClair()
    {
        var archive = monde.Copie("archive-analyse");
        Archiver(archive, monde.Secondes - Calendrier.SecondesParAn / 2);
        var (table, archivees) = archive.CompterJournal();
        Assert.True(archivees > 0);
        var chemin = Path.Combine(monde.Dossier, "analyse.db");
        var lignes = archive.CopierPourAnalyse(chemin, e => e.Recit?.Texte ?? e.Brut ?? "");
        Assert.Equal(table + archivees, lignes);

        using var connexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = chemin, Pooling = false }.ToString());
        connexion.Open();
        using var c = connexion.CreateCommand();
        c.CommandText = "SELECT count(*), count(texte_redige), (SELECT count(*) FROM journal_blocs) FROM journal WHERE texte_redige <> '';";
        using (var l = c.ExecuteReader())
        {
            l.Read();
            Assert.Equal(lignes, l.GetInt64(0));
            Assert.Equal(lignes, l.GetInt64(1));
            Assert.Equal(0, l.GetInt64(2));
        }

        // Une période : seulement ses lignes.
        var an2 = Path.Combine(monde.Dossier, "analyse-an2.db");
        var n = archive.CopierPourAnalyse(an2, e => "", Calendrier.SecondesParAn, 2 * Calendrier.SecondesParAn - 1);
        Assert.InRange(n, 1, lignes - 1);
    }

    [Fact]
    public void Migration_DepuisLaVersion37()
    {
        var depot = monde.Copie("v37");
        using (var connexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = depot.CheminFichier, Pooling = false }.ToString()))
        {
            connexion.Open();
            using var c = connexion.CreateCommand();
            c.CommandText = "DROP TABLE journal_blocs; DROP TABLE journal_blocs_cles; DROP TABLE journal_suivis; DROP TABLE journal_purge; PRAGMA user_version = 37;";
            c.ExecuteNonQuery();
        }
        var repris = new DepotMonde(depot.CheminFichier);
        Assert.NotNull(repris.Charger());
        Assert.True(Archiver(repris, monde.Secondes) > 0);
        Assert.True(repris.ChangerSuivis([3], []));
        Assert.Equal([3], repris.LireSuivis());
    }

    [Fact]
    public void Mesure_TailleEtRythme()
    {
        var depot = monde.Copie("mesure");
        var m = depot.MesurerJournal(monde.Secondes - Calendrier.SecondesParAn);
        Assert.True(m.Octets > 0 && m.Table > 0 && m.Recentes > 0 && m.Recentes < m.Table);
        Assert.InRange(m.OctetsParAn(Calendrier.SecondesParAn), m.Octets / 4, m.Octets);
    }
}
