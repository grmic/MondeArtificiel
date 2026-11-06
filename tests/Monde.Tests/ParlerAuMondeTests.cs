using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>
/// L'Entité parle au Monde entier ou à une terre (0.37.1) : chacun l'entend et la reçoit selon son esprit, le bilan par
/// communauté se note, une parole qui vise une communauté est débattue par les autres ; les commandements se vivent et se
/// transmettent ; la case du LLM.
/// </summary>
public sealed class ParlerAuMondeTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static MondeSimule MondeAvecCommunautes()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        for (var k = 0; k < 24 && monde.Communautes().Count(c => c.Active) < 2; k++) monde.AvancerDe(An / 12);
        Assert.True(monde.Communautes().Count(c => c.Active) >= 2);
        return monde;
    }

    [Fact]
    public void ParlerAuMonde_ChacunLEntend_LeBilanSeNote_EtSeRelit()
    {
        var monde = MondeAvecCommunautes();
        var (_, humains) = monde.InstantaneHumains();
        var r = monde.ParlerAuMonde(Parole.NeTuezPas, ModeParole.Voix, 0, 0, 0, 0);
        Assert.NotNull(r);
        var (pm, resultats, mandats) = r.Value;
        Assert.Empty(mandats);
        var (_, apres) = monde.InstantaneHumains();
        var t = apres.Where(h => h.EstVivant).Max(h => h.Impulsions.Select(i => i.Le).DefaultIfEmpty(0).Max());
        // Une voix : tous les vivants en âge de comprendre l'entendent, endormis ou non.
        var attendus = humains.Count(h => h.EstVivant && !h.EstBebe(t));
        Assert.Equal(attendus, resultats.Count);
        Assert.Equal(resultats.Count, pm.Bilans.Sum(b => b.Entendu));
        Assert.All(pm.Bilans, b => Assert.True(b.Entendu >= b.Compris && b.Compris >= b.Crus && b.Crus >= b.Veulent + b.Hesitent + b.Refusent));
        Assert.Contains(monde.JournalRecent(), j => j.Id == pm.EvenementId);
        Assert.All(pm.Bilans, b => Assert.Contains(monde.JournalRecent(), j => j.Id == b.EvenementId && j.CauseId == pm.EvenementId));
        Assert.Single(monde.ParolesMonde());

        // Une parole faite pour un seul humain, ou une communauté visée absente : rien.
        Assert.Null(monde.ParlerAuMonde(Parole.Tue, ModeParole.Voix, 0, 1, 0, 0));
        Assert.Null(monde.ParlerAuMonde(Parole.FaitesLaGuerre, ModeParole.Voix, 0, 0, 0, 0));
        Assert.Null(monde.ParlerAuMonde(Parole.NeTuezPas, ModeParole.Voix, -7, 0, 0, 0));

        var dossier = Path.Combine(Path.GetTempPath(), "monde-parole-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(monde.Etat);
            var relu = new DepotMonde(fichier).Charger()!;
            var lu = Assert.Single(relu.ParolesMonde!);
            Assert.Equal((pm.Id, pm.Parole, pm.Mode, pm.Terre, pm.EvenementId, pm.Bilans.Count), (lu.Id, lu.Parole, lu.Mode, lu.Terre, lu.EvenementId, lu.Bilans.Count));
            Assert.Equal(pm.Bilans.Sum(b => b.Crus), lu.Bilans.Sum(b => b.Crus));
            Assert.Single(new MondeSimule(Carte, relu).ParolesMonde());

            // Un monde de la version 56 n'a pas de paroles au Monde ; il se relit et passe en version 57.
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fichier};Pooling=False"))
            {
                c.Open();
                using var commande = c.CreateCommand();
                commande.CommandText = "DROP TABLE paroles_monde; PRAGMA user_version = 56;";
                commande.ExecuteNonQuery();
            }
            var v56 = new DepotMonde(fichier).Charger()!;
            Assert.True(v56.ParolesMonde is null or { Count: 0 });
            new DepotMonde(fichier).Enregistrer(v56, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void UneTerre_SeulsSesHabitantsLEntendent()
    {
        var monde = MondeAvecCommunautes();
        var terres = monde.TerresHabitees();
        Assert.NotEmpty(terres);
        var terre = terres[0];
        var (pm, resultats, _) = monde.ParlerAuMonde(Parole.NAiePasPeur, ModeParole.Reve, terre.Masse, 0, 0, 0)!.Value;
        Assert.Equal(terre.Masse, pm.Terre);
        Assert.True(resultats.Count <= terre.Vivants);
        Assert.NotEmpty(resultats);
    }

    [Fact]
    public void ParoleQuiViseUneCommunaute_LesAutresEnDebattent()
    {
        var monde = MondeAvecCommunautes();
        var actives = monde.Communautes().Where(c => c.Active).ToList();
        var visee = actives[^1];
        var (pm, resultats, mandats) = monde.ParlerAuMonde(Parole.FaitesLaPaix, ModeParole.Apparition, 0, 0, visee.Id, 0)!.Value;
        Assert.Equal(visee.Id, pm.CibleCommunaute);
        Assert.NotEmpty(resultats);
        // Chaque autre communauté active qui l'a entendue en débat ; jamais celle qu'elle vise.
        Assert.NotEmpty(mandats);
        Assert.All(mandats, m => Assert.Equal(visee.Id, m.CibleId));
        Assert.DoesNotContain(mandats, m => m.CommunauteId == visee.Id);
        Assert.All(pm.Bilans.Where(b => b.CommunauteId == visee.Id), b => Assert.True(b.Visee));
        Assert.All(pm.Bilans.Where(b => b.MandatId > 0), b => Assert.Contains(mandats, m => m.Id == b.MandatId));
    }

    [Fact]
    public void SansParoleAuMonde_LesCommandementsNeTirentAucunHasard()
    {
        var a = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var b = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        a.AvancerDe(An / 2);
        b.AvancerDe(An / 2);
        Assert.Equal(a.JournalRecent().Count, b.JournalRecent().Count);
        Assert.Empty(a.ParolesMonde());
        Assert.DoesNotContain(a.JournalRecent(), j => j.Recit?.Cle is "cmd.partage" or "cmd.soigne" or "cmd.transmis");
    }

    private static Humain Humain(int id, int x, int y, double age, double faim = 20, int pere = 0) => new()
    {
        PereId = pere,
        Id = id,
        Nom = "H" + id,
        Sexe = id % 2 == 0 ? Sexe.Femme : Sexe.Homme,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.6, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Femme : Sexe.Homme),
        X = x,
        Y = y,
        Faim = faim,
    };

    [Fact]
    public void Partagez_LeFidelePartageAvecQuiAFaim_EtLeRaconteASonEnfant()
    {
        var e = Carte.Emplacements[0];
        var fidele = Humain(1, e.X, e.Y, 30, faim: 10);
        var affame = Humain(2, e.X + 1, e.Y, 40, faim: 85);
        var enfant = Humain(3, e.X, e.Y + 1, 9, pere: 1);
        fidele.Impulsions.Add(new Impulsion(Parole.PartagezTous, ModeParole.Voix, 0, 0, 1, Maintenant, Maintenant + An, Maintenant, Reponse.Acceptee, 99));
        var tous = new List<Humain> { fidele, affame, enfant };
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(3)) { Social = new VieSociale(new Alea(3)) };
        var t = Maintenant;
        for (var k = 0; k < 120 && !(c.Journal.Exists(j => j.Recit?.Cle == "cmd.partage") && enfant.Consigne(Parole.PartagezTous, t) is not null); k++)
        {
            t += Jour;
            fidele.Faim = 10;
            affame.Faim = 85;
            c.VivreLesCommandements(tous, t);
        }
        var partage = Assert.Single(c.Journal, j => j.Recit?.Cle == "cmd.partage" && j.CauseId == 99);
        Assert.Equal((1, 2), (partage.HumainId, partage.AutreId));
        Assert.NotNull(enfant.Consigne(Parole.PartagezTous, t));
        Assert.Contains(c.Journal, j => j.Recit?.Cle == "cmd.transmis");
    }

    [Fact]
    public void Llm_ParlerAuMonde_RefuseSansLaCase()
    {
        var acte = new ActeLlm { Type = Actes.ParlerMonde, Parole = "NeTuezPas", Mode = "Voix" };
        var refus = Assert.Throws<ArgumentException>(() => Actes.Verifier(acte, FamillesLlm.Toutes));
        Assert.DoesNotContain(Actes.ParlerMonde, Actes.TypesPermis(false, false));
        Actes.Verifier(acte, FamillesLlm.Toutes, auMonde: true);
        Assert.Throws<ArgumentException>(() => Actes.Verifier(acte, FamillesLlm.Toutes & ~FamillesLlm.Paroles, auMonde: true));
        Assert.Throws<ArgumentException>(() => Actes.Verifier(acte with { Parole = "Tue" }, FamillesLlm.Toutes, auMonde: true));
        Assert.NotEmpty(refus.Message);
    }
}
