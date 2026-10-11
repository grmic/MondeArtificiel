using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Stockage;

namespace Monde.Tests;

public sealed class DepotMondeTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "monde-depot-" + Guid.NewGuid().ToString("N"));

    private string Fichier => Path.Combine(_dossier, "sous-dossier", "monde.db");

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    /// <summary>Ramène un fichier au schéma 7 (avant les réserves, les pluies et les territoires).</summary>
    /// <summary>Ramène un fichier au schéma 8 (avant les causes, les rancunes et les usages).</summary>
    /// <summary>Ramène un fichier au schéma 9 (avant l'identité et l'histoire des communautés).</summary>
    internal const string RetourVersion9 = """
        DROP INDEX journal_communaute_id;
        DROP INDEX journal_autre_communaute_id;
        ALTER TABLE journal DROP COLUMN communaute;
        ALTER TABLE journal DROP COLUMN autre_communaute;
        ALTER TABLE communautes DROP COLUMN nom_propre;
        ALTER TABLE communautes DROP COLUMN sens;
        ALTER TABLE communautes DROP COLUMN sens_cle;
        ALTER TABLE communautes DROP COLUMN eponyme;
        ALTER TABLE communautes DROP COLUMN lexique;
        ALTER TABLE communautes DROP COLUMN origine;
        ALTER TABLE communautes DROP COLUMN fusion_vers;
        ALTER TABLE communautes DROP COLUMN fusion_decidee_le;
        ALTER TABLE communautes DROP COLUMN fusion_evenement;
        ALTER TABLE communautes DROP COLUMN fusionnee_dans;
        ALTER TABLE communautes DROP COLUMN accueil_le;
        ALTER TABLE communautes DROP COLUMN migrations;
        ALTER TABLE communautes DROP COLUMN disettes_surmontees;
        ALTER TABLE communautes DROP COLUMN installation_evenement;
        ALTER TABLE communautes DROP COLUMN territoire;
        ALTER TABLE relations_communautes DROP COLUMN evenement;
        DROP TABLE noms_communautes;
        DROP TABLE archives_communautes;
        PRAGMA user_version = 9;
        """;

    internal const string RetourVersion8 = RetourVersion9 + """
        ALTER TABLE journal DROP COLUMN cause;
        ALTER TABLE humains DROP COLUMN faim_soif_jour;
        ALTER TABLE humains DROP COLUMN froid_jour;
        ALTER TABLE humains DROP COLUMN jours_seul;
        ALTER TABLE humains DROP COLUMN colere;
        ALTER TABLE humains DROP COLUMN deuil_evenement;
        ALTER TABLE humains DROP COLUMN depression_evenement;
        ALTER TABLE humains DROP COLUMN mort_evenement;
        ALTER TABLE humains DROP COLUMN meurtrier;
        ALTER TABLE communautes DROP COLUMN tolerance;
        ALTER TABLE communautes DROP COLUMN usage;
        ALTER TABLE communautes DROP COLUMN derniere_affaire;
        ALTER TABLE communautes DROP COLUMN disette_evenement;
        DROP TABLE griefs;
        DROP TABLE raisons;
        DROP TABLE offenses_communautes;
        PRAGMA user_version = 8;
        """;

    internal const string RetourVersion7 = RetourVersion8 + """
        ALTER TABLE humains DROP COLUMN reserves;
        ALTER TABLE humains DROP COLUMN metabolisme;
        ALTER TABLE communautes DROP COLUMN centre_x;
        ALTER TABLE communautes DROP COLUMN centre_y;
        ALTER TABLE communautes DROP COLUMN rayon;
        ALTER TABLE communautes DROP COLUMN abondance;
        ALTER TABLE communautes DROP COLUMN disette;
        ALTER TABLE communautes DROP COLUMN destination_x;
        ALTER TABLE communautes DROP COLUMN destination_y;
        DROP TABLE climat;
        DROP TABLE relations_communautes;
        PRAGMA user_version = 7;
        """;

    /// <summary>Ramène un fichier au schéma 6 (avant le caractère complet, les liens et les communautés).</summary>
    internal const string RetourVersion6 = RetourVersion7 + """
        DROP INDEX journal_humain_id;
        DROP INDEX journal_autre_id;
        DROP INDEX journal_type_id;
        CREATE INDEX journal_humain ON journal (humain);
        CREATE INDEX journal_autre ON journal (autre);
        ALTER TABLE humains DROP COLUMN agreabilite;
        ALTER TABLE humains DROP COLUMN nevrosisme;
        ALTER TABLE humains DROP COLUMN transcendance;
        ALTER TABLE humains DROP COLUMN qi;
        ALTER TABLE humains DROP COLUMN qi_verbal;
        ALTER TABLE humains DROP COLUMN qi_spatial;
        ALTER TABLE humains DROP COLUMN qi_manuel;
        ALTER TABLE humains DROP COLUMN qi_memoire;
        ALTER TABLE humains DROP COLUMN qi_art;
        ALTER TABLE humains DROP COLUMN humeur;
        ALTER TABLE humains DROP COLUMN chagrin;
        ALTER TABLE humains DROP COLUMN deuil_de;
        ALTER TABLE humains DROP COLUMN jours_sombres;
        ALTER TABLE humains DROP COLUMN deprime_depuis;
        ALTER TABLE humains DROP COLUMN communaute;
        ALTER TABLE humains DROP COLUMN inconfort;
        ALTER TABLE humains DROP COLUMN compagnie;
        ALTER TABLE memoire_lieux DROP COLUMN rappels;
        ALTER TABLE memoire_lieux DROP COLUMN decu_le;
        DROP TABLE liens;
        DROP TABLE communautes;
        CREATE TABLE memoire_proches (humain INTEGER NOT NULL, rang INTEGER NOT NULL, autre INTEGER NOT NULL,
            rencontres INTEGER NOT NULL, derniere INTEGER NOT NULL, PRIMARY KEY (humain, rang));
        PRAGMA user_version = 6;
        """;

    internal static void ExecuterSql(string fichier, string sql)
    {
        using var connexion = new SqliteConnection($"Data Source={fichier};Pooling=False");
        connexion.Open();
        using var commande = connexion.CreateCommand();
        commande.CommandText = sql;
        commande.ExecuteNonQuery();
    }

    [Fact]
    public void NouveauFichier_NeContientAucunEtat()
    {
        Assert.Null(new DepotMonde(Fichier).Charger());
        Assert.True(File.Exists(Fichier));
    }

    [Fact]
    public void EtatSimple_EstRelu_ApresReouverture()
    {
        var etat = new EtatMonde(uint.MaxValue, 123_456_789, 205_761, 3);
        new DepotMonde(Fichier).Enregistrer(etat);

        Assert.Equal(etat, new DepotMonde(Fichier).Charger());
    }

    [Fact]
    public void EtatComplet_EstReluALIdentique()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(3 * 86_400);
        var etat = monde.Etat;
        new DepotMonde(Fichier).Enregistrer(etat);

        var relu = new DepotMonde(Fichier).Charger()!;

        Assert.Equal(etat.Alea, relu.Alea);
        Assert.Equal(etat.Gibier, relu.Gibier);
        Assert.Equal(etat.Plantes, relu.Plantes);
        Assert.Equal(etat.Humains!.Count, relu.Humains!.Count);
        for (var i = 0; i < etat.Humains.Count; i++)
        {
            Assert.Equivalent(etat.Humains[i], relu.Humains[i], strict: true);
        }
    }

    [Fact]
    public void Enregistrer_RemplaceLEtatPrecedent()
    {
        var depot = new DepotMonde(Fichier);
        depot.Enregistrer(new EtatMonde(1, 100, 1, 0));
        depot.Enregistrer(new EtatMonde(1, 200, 2, 1));

        Assert.Equal(200, depot.Charger()!.Secondes);
    }

    [Fact]
    public void FichierVersion1_EstMisAJour_SansPerdreLaDate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Fichier)!);
        ExecuterSql(Fichier, """
            CREATE TABLE etat (id INTEGER PRIMARY KEY CHECK (id = 1), graine INTEGER NOT NULL,
                secondes INTEGER NOT NULL CHECK (secondes >= 0), pas INTEGER NOT NULL CHECK (pas >= 0),
                vitesse INTEGER NOT NULL, modifie_le TEXT NOT NULL);
            INSERT INTO etat VALUES (1, 42, 99000, 165, 0, '2026-10-10');
            PRAGMA user_version = 1;
            """);

        var etat = new DepotMonde(Fichier).Charger()!;

        Assert.Equal(99_000, etat.Secondes);
        Assert.Null(etat.Humains); // seront créés par le monde à partir de la graine
        Assert.Null(etat.Alea);
    }

    [Fact]
    public void EnfantEtGrossesse_SontRelus()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        var etat = monde.Etat;
        var mere = etat.Humains![0];
        mere.EnceinteDepuis = 12_345;
        mere.PereEnfantId = 4;
        mere.Desir = 42.5;
        var bebe = Simulation.Humains.GenerateurPopulation.Naissance(67, mere, etat.Humains[1], 20_000,
            new Simulation.Monde.Alea(1), new HashSet<string>());
        var avecBebe = etat with { Humains = [.. etat.Humains, bebe] };
        new DepotMonde(Fichier).Enregistrer(avecBebe);

        var relu = new DepotMonde(Fichier).Charger()!.Humains!;

        Assert.Equal(67, relu.Count);
        Assert.Equivalent(mere, relu[0], strict: true);
        Assert.Equivalent(bebe, relu[66], strict: true);
    }

    [Fact]
    public void FichierVersion3_GardeSesHumains_AvecDesTraitsParDefaut()
    {
        // Un monde enregistré avant la version 4 : on retire les nouvelles colonnes en recréant la table.
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        new DepotMonde(Fichier).Enregistrer(monde.Etat);
        ExecuterSql(Fichier, RetourVersion6);
        ExecuterSql(Fichier, """
            UPDATE humains SET attachement = NULL, puberte = NULL, fin_fertilite = NULL, fertilite = NULL, terme_grossesse = NULL;
            ALTER TABLE humains DROP COLUMN attachement;
            ALTER TABLE humains DROP COLUMN puberte;
            ALTER TABLE humains DROP COLUMN fin_fertilite;
            ALTER TABLE humains DROP COLUMN fertilite;
            ALTER TABLE humains DROP COLUMN terme_grossesse;
            PRAGMA user_version = 3;
            """);

        var relu = new DepotMonde(Fichier).Charger()!.Humains!;

        Assert.Equal(66, relu.Count);
        Assert.All(relu, h =>
        {
            Assert.Equal(0.8, h.Personnalite.Attachement);
            Assert.Equal(Simulation.Humains.Biologie.ParDefaut(h.Sexe), h.Biologie);
            Assert.Null(h.TermeGrossesse);
        });
    }

    [Fact]
    public void Remplacer_EffaceLAncienMonde_EtCopieVers_LeGarde()
    {
        var depot = new DepotMonde(Fichier);
        var ancien = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        ancien.AvancerDe(86_400);
        var etatAncien = ancien.Etat;
        var bebe = Simulation.Humains.GenerateurPopulation.Naissance(67, etatAncien.Humains![0], etatAncien.Humains[1], 1,
            new Simulation.Monde.Alea(1), new HashSet<string>());
        depot.Enregistrer(etatAncien with { Humains = [.. etatAncien.Humains, bebe] });
        var copie = Path.Combine(_dossier, "copie.db");
        depot.CopierVers(copie);
        depot.CopierVers(copie); // une copie existante est remplacée

        var neuf = MondeSimule.Nouveau(7);
        depot.Enregistrer(neuf, remplacer: true);

        Assert.Equal(neuf, depot.Charger());              // plus d'humains ni de ressources de l'ancien monde
        Assert.Equal(67, new DepotMonde(copie).Charger()!.Humains!.Count);
        Assert.Throws<ArgumentException>(() => depot.CopierVers(Fichier));
    }

    [Fact]
    public void RecommencerApresCopie_DeplaceLAncienMonde_EtRepartDUnFichierNeuf()
    {
        var depot = new DepotMonde(Fichier);
        var ancien = MondeSimule.Nouveau(42);
        depot.Enregistrer(ancien with { Secondes = 5_000_000 });
        depot.EcrireReglage("redemarrage", "vitesse");
        ExecuterSql(Fichier, "CREATE TABLE temoin (x);");
        var copie = Path.Combine(Path.GetDirectoryName(Fichier)!, "avant.db");
        File.WriteAllText(copie, "ancienne copie");
        File.WriteAllText(copie + "-wal", "reste d'une copie précédente");

        var neuf = MondeSimule.Nouveau(7);
        depot.RecommencerApresCopie(copie, neuf);

        Assert.Equal(neuf, depot.Charger());
        Assert.Equal("vitesse", depot.LireReglage("redemarrage"));
        Assert.Empty(depot.LireJournal(null, null, null, 10));
        Assert.False(TableExiste(Fichier, "temoin"));   // fichier neuf : la table témoin est partie avec la copie
        Assert.True(TableExiste(copie, "temoin"));
        Assert.False(File.Exists(copie + "-wal"));
        var garde = new DepotMonde(copie).Charger()!;
        Assert.Equal(42UL, garde.Graine);
        Assert.Equal(5_000_000, garde.Secondes);
        Assert.Throws<ArgumentException>(() => depot.RecommencerApresCopie(Fichier, neuf));
        Assert.Throws<ArgumentException>(() => depot.RecommencerApresCopie(Path.Combine(_dossier, "ailleurs.db"), neuf));
    }

    [Fact]
    public void RecommencerApresCopie_FichierOccupe_CopieEtEfface()
    {
        var depot = new DepotMonde(Fichier);
        depot.Enregistrer(MondeSimule.Nouveau(42) with { Secondes = 9_000 });
        depot.EcrireReglage("redemarrage", "vitesse");
        ExecuterSql(Fichier, "CREATE TABLE temoin (x);");
        var copie = Path.Combine(Path.GetDirectoryName(Fichier)!, "avant.db");

        // Une lecture en cours empêche de vider le journal WAL (comme un fichier ouvert empêche de le déplacer sous Windows).
        using var lecteur = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Fichier, Pooling = false }.ToString());
        lecteur.Open();
        using var transaction = lecteur.BeginTransaction(deferred: true);
        using (var lecture = lecteur.CreateCommand())
        {
            lecture.Transaction = transaction;
            lecture.CommandText = "SELECT COUNT(*) FROM humains;";
            lecture.ExecuteScalar();
        }
        depot.EcrireReglage("langue", "fr"); // écrit après le début de la lecture : le journal WAL ne peut pas être vidé
        var neuf = MondeSimule.Nouveau(7);
        depot.RecommencerApresCopie(copie, neuf);

        Assert.Equal(neuf, depot.Charger());
        Assert.Equal("vitesse", depot.LireReglage("redemarrage"));
        Assert.Equal("fr", depot.LireReglage("langue"));
        Assert.Equal(9_000, new DepotMonde(copie).Charger()!.Secondes);
        Assert.True(TableExiste(Fichier, "temoin"));    // même fichier, vidé : le monde n'a pas été déplacé
    }

    private static bool TableExiste(string fichier, string table)
    {
        using var connexion = new SqliteConnection($"Data Source={fichier};Pooling=False");
        connexion.Open();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $t;";
        commande.Parameters.AddWithValue("$t", table);
        return Convert.ToInt32(commande.ExecuteScalar()) == 1;
    }

    [Fact]
    public void FichierVersion4_NiveauDeVitesseConvertiEnFacteur()
    {
        new DepotMonde(Fichier).Enregistrer(new EtatMonde(42, 1000, 1, 0));
        ExecuterSql(Fichier, RetourVersion6);
        ExecuterSql(Fichier, "DROP TABLE reglages; UPDATE etat SET vitesse = 3; PRAGMA user_version = 4;");

        var depot = new DepotMonde(Fichier);

        Assert.Equal(1_440, depot.Charger()!.Facteur);
        Assert.Null(depot.LireReglage("redemarrage"));
        depot.EcrireReglage("redemarrage", "vitesse");
        depot.EcrireReglage("redemarrage", "pause");
        Assert.Equal("pause", depot.LireReglage("redemarrage"));
    }

    [Fact]
    public void VitesseLibre_EstRelue()
    {
        new DepotMonde(Fichier).Enregistrer(new EtatMonde(42, 1000, 1, 0.5));
        Assert.Equal(0.5, new DepotMonde(Fichier).Charger()!.Facteur);
    }

    [Fact]
    public void SchemaPlusRecent_EstRefuse()
    {
        new DepotMonde(Fichier);
        ExecuterSql(Fichier, "PRAGMA user_version = 99;");

        Assert.Throws<InvalidOperationException>(() => new DepotMonde(Fichier));
    }

    [Fact]
    public void ValeurNegative_EstRefuseeParLaBase()
    {
        var depot = new DepotMonde(Fichier);
        Assert.Throws<SqliteException>(() => depot.Enregistrer(new EtatMonde(1, -1, 0, 0)));
    }
}
