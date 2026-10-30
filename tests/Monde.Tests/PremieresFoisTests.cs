using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.32.5 : premières fois du monde et des communautés, tenues par un registre et marquées sur la ligne du journal.</summary>
public sealed class PremieresFoisTests(MondeAvecJournal monde) : IClassFixture<MondeAvecJournal>
{
    private static TypeCatastrophe? Aucune(long id) => null;

    private static EvenementJournal Ligne(long id, TypeEvenement type, Recit recit, int communaute = 0, int autre = 0, long cause = 0) =>
        new(0, type, 1, 2, recit) { Id = id, CommunauteId = communaute, AutreCommunauteId = autre, CauseId = cause };

    [Fact]
    public void Naissance_PremiereAuMonde_PuisParCommunaute()
    {
        var r = new RegistrePremieres();
        var naissance = new Recit("j.naissance", ("b", "B"), ("m", "M"), ("p", ""));
        Assert.Equal(PremieresFois.AuMonde | PremieresFois.PourLaCommunaute, r.Marquer(Ligne(1, TypeEvenement.Naissance, naissance, 3), Aucune).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(2, TypeEvenement.Naissance, naissance, 3), Aucune).Premiere);
        Assert.Equal(PremieresFois.PourLaCommunaute, r.Marquer(Ligne(3, TypeEvenement.Naissance, naissance, 4), Aucune).Premiere);
        // Sans communauté, seule la première au monde compte.
        Assert.Equal(0, r.Marquer(Ligne(4, TypeEvenement.Naissance, naissance), Aucune).Premiere);
        // Le génie qui suit une naissance n'est pas une naissance de plus.
        Assert.Equal(0, new RegistrePremieres().Marquer(Ligne(5, TypeEvenement.Naissance, new Recit("j.naissance.genie", ("b", "B"))), Aucune).Premiere);
        Assert.True(PremieresFois.Est(r.Marquer(Ligne(6, TypeEvenement.Naissance, naissance, 7), Aucune)));
        Assert.Equal(["m:naissance", "c:3:naissance", "c:4:naissance", "c:7:naissance"], r.NonEnregistrees);
        r.Enregistrees(2);
        Assert.Equal(["c:4:naissance", "c:7:naissance"], r.NonEnregistrees);
    }

    [Fact]
    public void Mort_ChaqueCauseUneFoisAuMonde()
    {
        var r = new RegistrePremieres();
        Recit Mort(string cause) => new("j.mort", ("h", "H"), ("cause", new Recit($"j.cause.{cause}", ("h", "H"))), ("suite", ""), ("age", ""));
        Assert.Equal(3, r.Marquer(Ligne(1, TypeEvenement.Mort, Mort("Faim"), 1), Aucune).Premiere);
        // Une autre cause, dans la même communauté : première au monde pour cette cause seulement.
        Assert.Equal(PremieresFois.AuMonde, r.Marquer(Ligne(2, TypeEvenement.Mort, Mort("Froid"), 1), Aucune).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(3, TypeEvenement.Mort, Mort("Froid"), 1), Aucune).Premiere);
        var meurtre = r.Marquer(Ligne(4, TypeEvenement.Mort, new Recit("j.mort.meurtre", ("h", "H"), ("m", "M"), ("age", "")), 1), Aucune);
        Assert.Equal(PremieresFois.AuMonde | PremieresFois.PourLaCommunaute, meurtre.Premiere);
        Assert.Contains("m:mort.meurtre", r.Passees);
        Assert.Contains("c:1:meurtre", r.Passees);
        // L'annonce de l'extinction n'est pas une mort de plus.
        Assert.Equal(0, new RegistrePremieres().Marquer(Ligne(5, TypeEvenement.Mort, new Recit("extinction.annonce")), Aucune).Premiere);
    }

    [Fact]
    public void Guerre_ComptePourLesDeuxCommunautes()
    {
        var r = new RegistrePremieres();
        Assert.Equal(3, r.Marquer(Ligne(1, TypeEvenement.Guerre, new Recit("guerre.raid"), 1, 2), Aucune).Premiere);
        Assert.Contains("c:2:guerre", r.Passees);
        Assert.Equal(PremieresFois.PourLaCommunaute, r.Marquer(Ligne(2, TypeEvenement.Guerre, new Recit("guerre.raid"), 2, 3), Aucune).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(3, TypeEvenement.Guerre, new Recit("guerre.raid"), 1, 3), Aucune).Premiere);
        Assert.Equal(3, r.Marquer(Ligne(4, TypeEvenement.Guerre, new Recit("guerre.paix"), 1, 2), Aucune).Premiere);
    }

    [Fact]
    public void Catastrophe_AnnonceAuMonde_SuitesPourLaCommunaute()
    {
        var r = new RegistrePremieres();
        TypeCatastrophe? Seisme(long id) => id == 10 ? TypeCatastrophe.Seisme : null;
        Assert.Equal(PremieresFois.AuMonde, r.Marquer(Ligne(10, TypeEvenement.Catastrophe, new Recit("cat.seisme")), Seisme).Premiere);
        Assert.Equal(PremieresFois.PourLaCommunaute, r.Marquer(Ligne(11, TypeEvenement.Catastrophe, new Recit("cat.terrifie"), 5, cause: 10), Seisme).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(12, TypeEvenement.Catastrophe, new Recit("cat.blesse"), 5, cause: 10), Seisme).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(13, TypeEvenement.Catastrophe, new Recit("cat.blesse"), 6, cause: 99), Seisme).Premiere);
    }

    [Fact]
    public void HorsCouple_Feu_Entite_Religion()
    {
        var r = new RegistrePremieres();
        var hors = new Recit("j.accouplement", ("f", "F"), ("h", "H"), ("hors", new Recit("j.horsCouple")));
        Assert.Equal(3, r.Marquer(Ligne(1, TypeEvenement.Accouplement, hors, 1), Aucune).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(2, TypeEvenement.Accouplement, new Recit("j.accouplement", ("hors", "")), 2), Aucune).Premiere);
        // Le feu : seulement la première fois pour une communauté (au monde, c'est la première découverte).
        var feu = new Recit("ing.comprend", ("savoir", new Recit("savoir.Flamme.frotter")));
        Assert.Equal(PremieresFois.PourLaCommunaute, r.Marquer(Ligne(3, TypeEvenement.Decouverte, feu, 1), Aucune).Premiere);
        Assert.Equal(0, r.Marquer(Ligne(4, TypeEvenement.Decouverte, new Recit("ing.comprend", ("savoir", new Recit("savoir.Corde"))), 2), Aucune).Premiere);
        Assert.Equal(3, r.Marquer(Ligne(5, TypeEvenement.Parole, new Recit("ent.recoit.reve"), 1), Aucune).Premiere);
        Assert.Equal(PremieresFois.AuMonde, r.Marquer(Ligne(6, TypeEvenement.Parole, new Recit("ent.obeit"), 1), Aucune).Premiere);
        Assert.Equal(PremieresFois.AuMonde, r.Marquer(Ligne(7, TypeEvenement.Geste, new Recit("geste.quelconque"), 1), Aucune).Premiere);
        Assert.Equal(3, r.Marquer(Ligne(8, TypeEvenement.Religion, new Recit("pri.pluie"), 1), Aucune).Premiere);
        Assert.Equal(PremieresFois.PourLaCommunaute, r.Marquer(Ligne(9, TypeEvenement.Religion, new Recit("pric.prie"), 2), Aucune).Premiere);
        Assert.Equal(3, r.Marquer(Ligne(10, TypeEvenement.Religion, new Recit("rel.prophete.visions"), 1), Aucune).Premiere);
        Assert.Equal(3, r.Marquer(Ligne(11, TypeEvenement.Religion, new Recit("rel.naissance"), 1), Aucune).Premiere);
        Assert.Equal(PremieresFois.PourLaCommunaute, r.Marquer(Ligne(12, TypeEvenement.Religion, new Recit("rel.convertit"), 2), Aucune).Premiere);
    }

    /// <summary>Le monde de deux ans : ses premières fois sont marquées, enregistrées, et une seule ligne par sorte au monde.</summary>
    [Fact]
    public void MondeReel_PremieresMarqueesEtEnregistrees()
    {
        var depot = monde.Copie("premieres-reel");
        var marquees = depot.LireJournal(limite: 500, premieres: true).Where(e => e.Premiere != 0).ToList();
        Assert.NotEmpty(marquees);
        Assert.Contains(marquees, e => e.Type == TypeEvenement.Naissance && (e.Premiere & PremieresFois.AuMonde) != 0);
        Assert.Contains(marquees, e => e.Type == TypeEvenement.Mort && (e.Premiere & PremieresFois.AuMonde) != 0);
        Assert.Single(marquees, e => e.Type == TypeEvenement.Naissance && (e.Premiere & PremieresFois.AuMonde) != 0);
        var etat = depot.Charger()!;
        Assert.Contains("m:naissance", etat.Premieres!);
        Assert.Contains("m:mort", etat.Premieres!);
    }

    /// <summary>
    /// Un monde d'avant la 0.32.5 (schéma 41) : à l'ouverture, il relit son journal, sans marquer les anciennes lignes, et
    /// retrouve tout ce que la simulation avait noté (rien ne sera annoncé à tort comme une première fois).
    /// </summary>
    [Fact]
    public void AncienMonde_RelitSonJournal_SansMarquer()
    {
        var chemin = Path.Combine(monde.Dossier, "premieres-ancien.db");
        new DepotMonde(monde.Fichier).CopierVers(chemin);
        var attendues = new DepotMonde(chemin).Charger()!.Premieres!.ToHashSet();
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = chemin, Pooling = false }.ToString()))
        {
            c.Open();
            using var x = c.CreateCommand();
            x.CommandText = "DROP TABLE premieres; ALTER TABLE journal DROP COLUMN premiere; PRAGMA user_version = 41;";
            x.ExecuteNonQuery();
        }
        var depot = new DepotMonde(chemin);
        Assert.DoesNotContain(depot.LireJournal(limite: 500), e => e.Premiere != 0);
        var relues = depot.Charger()!.Premieres!.ToHashSet();
        // Les sortes tirées de lignes enregistrées se retrouvent toutes (les suites d'une catastrophe oubliée exceptées).
        Assert.Empty(attendues.Except(relues).Where(k => !k.Contains(":cat.")));
        Assert.Contains("m:naissance", relues);
    }
}
