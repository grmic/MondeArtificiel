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

    [Fact]
    public void NouveauFichier_NeContientAucunEtat()
    {
        Assert.Null(new DepotMonde(Fichier).Charger());
        Assert.True(File.Exists(Fichier));
    }

    [Fact]
    public void EtatEnregistre_EstRelu_ApresReouverture()
    {
        var etat = new EtatMonde(uint.MaxValue, 123_456_789, 205_761, 3);
        new DepotMonde(Fichier).Enregistrer(etat);

        Assert.Equal(etat, new DepotMonde(Fichier).Charger());
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
    public void SchemaPlusRecent_EstRefuse()
    {
        new DepotMonde(Fichier);
        using (var connexion = new SqliteConnection($"Data Source={Fichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA user_version = 99;";
            commande.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => new DepotMonde(Fichier));
    }

    [Fact]
    public void ValeurNegative_EstRefuseeParLaBase()
    {
        var depot = new DepotMonde(Fichier);
        Assert.Throws<SqliteException>(() => depot.Enregistrer(new EtatMonde(1, -1, 0, 0)));
    }
}
