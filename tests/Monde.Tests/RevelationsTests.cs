using Monde.Serveur.Entite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.42.9 : les révélations gardées en mémoire, le ton des mots de l'Entité, ce qu'on lui demande et lui promet.</summary>
public sealed class RevelationsTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const string Origine = "Je vous ai faits de la poussière des étoiles, au commencement du Monde.";

    [Fact]
    public void UneRevelation_SeRaconte_SeDeforme_EtResteApresLaSauvegarde()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(Jour);
        var premier = monde.InstantaneHumains().Humains.First(h => h.EstVivant && !h.EstEnfant(monde.Secondes) && h.PartenaireId != 0);
        monde.RevelerEnDialogue(premier.Id, Origine, ModeParole.Apparition, croit: true, cause: 0);
        var connue = monde.InstantaneHumains().Humains[premier.Id - 1].Revelations.Single();
        Assert.Equal((1, 0, true), (connue.Id, connue.DeQui, connue.Croit));
        Assert.InRange(connue.Fidelite, 0.9, 1);

        monde.AvancerDe(240 * Jour);
        var (_, humains) = monde.InstantaneHumains();
        var entendue = humains.Where(h => h.Revelations.Exists(c => c.DeQui != 0)).ToList();
        // Il l'a racontée : d'autres la savent de bouche à oreille, moins fidèlement que lui.
        Assert.NotEmpty(entendue);
        Assert.All(entendue, h => Assert.True(h.Revelations[0].Fidelite < 0.98, $"{h.Nom} : {h.Revelations[0].Fidelite}"));

        var dossier = Path.Combine(Path.GetTempPath(), "monde-rev-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, "monde.db");
            var etat = monde.Etat;
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(Origine, relu.Revelations!.Single().Texte);
            Assert.Equal(etat.Humains!.Select(h => h.Revelations.Count).Sum(), relu.Humains!.Select(h => h.Revelations.Count).Sum());
            Assert.Equal(etat.Humains![premier.Id - 1].Revelations, relu.Humains![premier.Id - 1].Revelations);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void AuMonde_ChacunLaComprendEtYCroitOuNon()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(Jour);
        var (r, entendu, compris, crus) = monde.Reveler(0, Origine, ModeParole.Voix);
        Assert.Equal(PorteeRevelation.Monde, r.Portee);
        Assert.True(compris > entendu / 2, $"{compris} sur {entendu}");
        Assert.InRange(crus, 1, compris);
        var savent = monde.InstantaneHumains().Humains.Count(h => h.Revelations.Count > 0);
        Assert.Equal(compris, savent);
        Assert.Contains(monde.JournalRecent(), j => j.Recit?.Cle == "rev.monde");
        Assert.Throws<ArgumentException>(() => monde.Reveler(0, "   ", ModeParole.Voix));
        Assert.Throws<ArgumentException>(() => monde.Reveler(0, Origine, ModeParole.Signe));
    }

    [Fact]
    public void SansPersonneAQuiLaDire_ElleSOublie()
    {
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(5)) { Cultes = new Cultes() };
        var h = new Humain
        {
            Id = 1, Nom = "Seul", Sexe = Sexe.Homme, PartenaireId = 0, Emplacement = 1, Naissance = 10 * Calendrier.SecondesParAn - 30 * Calendrier.SecondesParAn,
            Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, Intelligence.Moyenne), Biologie = Biologie.ParDefaut(Sexe.Homme),
            X = Carte.Emplacements[0].X, Y = Carte.Emplacements[0].Y, Croyance = 10,
        };
        var r = new Revelation { Id = 1, Texte = Origine, Mode = ModeParole.Reve };
        c.Cultes.Revelations.Add(r);
        var t0 = 10 * Calendrier.SecondesParAn;
        c.Reveler(h, r, croit: false, 0, t0);
        var avant = h.Revelations[0].Fidelite;
        Assert.True(avant < 0.85, "un rêve s'efface au réveil");
        for (var j = 1; j <= 900 && h.Revelations.Count > 0; j++) c.VivreLesRevelations([h], [], t0 + j * Jour);
        Assert.Empty(h.Revelations);
        Assert.NotEqual(0, r.PerdueLe);
        Assert.Contains(c.Journal, e => e.Recit?.Cle == "rev.perdue");
    }

    private static Humain Un(int id, double n, double a, double conscience, double honnetete = 0.5)
    {
        var e = Carte.Emplacements[0];
        var p = new Personnalite(0.5, conscience, 0.5, a, n, 0.5, 0.5, 0.5, Intelligence.Moyenne);
        p = p with { Moral = p.Moral with { Honnetete = honnetete } };
        return new Humain
        {
            Id = id, Nom = "H" + id, Sexe = Sexe.Femme, PartenaireId = 0, Emplacement = 1, Naissance = -20 * Calendrier.SecondesParAn, Personnalite = p,
            Biologie = Biologie.ParDefaut(Sexe.Femme), X = e.X, Y = e.Y, Humeur = 50, Croyance = 80, Peur = 40, Amour = 50,
        };
    }

    [Fact]
    public void LeTon_ToucheChacunSelonSonCaractere()
    {
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(3));
        var anxieux = Un(1, n: 0.9, a: 0.5, conscience: 0.5);
        var serein = Un(2, n: 0.1, a: 0.5, conscience: 0.5);
        c.RessentirTon(anxieux, TonParole.Menace, ModeParole.Voix, true, 0, 0);
        c.RessentirTon(serein, TonParole.Menace, ModeParole.Voix, true, 0, 0);
        Assert.True(anxieux.Peur > serein.Peur + 10, $"{anxieux.Peur} / {serein.Peur}");
        var rassure = Un(3, n: 0.5, a: 0.8, conscience: 0.5);
        c.RessentirTon(rassure, TonParole.Reconfort, ModeParole.Apparition, true, 0, 0);
        Assert.True(rassure.Peur < 30 && rassure.Amour > 50);
        // Le peu accommodant se rebiffe sous la menace ; l'accommodant non.
        var indocile = Un(4, n: 0.2, a: 0.1, conscience: 0.5);
        c.RessentirTon(indocile, TonParole.Menace, ModeParole.Voix, true, 0, 0);
        Assert.True(indocile.Revolte > 5);
        Assert.Contains(c.Journal, e => e.Recit?.Cle == "ton.Menace");
        Assert.Null(c.RessentirTon(Un(5, 0.5, 0.5, 0.5), TonParole.Aucun, ModeParole.Voix, true, 0, 0));
    }

    [Fact]
    public void UnePromesse_LeConsciencieuxLaTient()
    {
        static int Tenues(double conscience, double honnetete)
        {
            var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(9)) { Cultes = new Cultes() };
            var tous = Enumerable.Range(1, 60).Select(i => Un(i, 0.5, 0.5, conscience, honnetete)).ToList();
            foreach (var h in tous) c.Promettre(h, Parole.AssiedsToi, ModeParole.Voix, 0, 0);
            foreach (var h in tous) c.SuivreLaParole(h, tous, 8 * 3600);
            return c.Journal.Count(e => e.Recit?.Cle == "dem.promesseTenue");
        }
        var (fiables, legers) = (Tenues(0.9, 0.9), Tenues(0.1, 0.1));
        Assert.True(fiables > legers + 15, $"consciencieux {fiables}, légers {legers}");
    }

    [Fact]
    public void LaTraduction_DitLeTonEtLaRevelation_LaReponse_LaDemandeEtLaPromesse()
    {
        Dialogues.LireTon("""{"paroles":[],"ton":"Menace","revelation":"Le Monde est né d'un feu venu du ciel."}""", out var ton, out var rev);
        Assert.Equal((TonParole.Menace, "Le Monde est né d'un feu venu du ciel."), (ton, rev));
        Dialogues.LireTon("""{"paroles":[],"ton":"inconnu","revelation":"rien"}""", out ton, out rev);
        Assert.Equal((TonParole.Aucun, ""), (ton, rev));
        Dialogues.LireEngagements("""{"reponse":"Guéris ma fille, je prierai.","demande":"Guerison","pour":4,"promesse":"Prie"}""",
            out var demande, out var pour, out var promesse);
        Assert.Equal((SujetPriere.Guerison, 4, Parole.Prie), (demande, pour, promesse));
        // On ne promet pas d'aller vers quelqu'un : seulement un geste sans personne à désigner.
        Dialogues.LireEngagements("""{"demande":"","promesse":"VaVers"}""", out demande, out _, out promesse);
        Assert.Null(demande);
        Assert.Null(promesse);
    }
}
