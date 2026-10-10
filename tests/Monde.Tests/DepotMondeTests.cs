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

    private static void ExecuterSql(string fichier, string sql)
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
