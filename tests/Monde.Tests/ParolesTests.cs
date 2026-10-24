using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Amour inspiré et parole de l'Entité : comprendre, croire, obéir ou refuser, agir ; penchants et tabou de la parenté.</summary>
public sealed class ParolesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;

    private static readonly (int X, int Y) Terre = TrouverTerre();

    private static (int, int) TrouverTerre()
    {
        for (var y = 0; y < Carte.Hauteur; y++)
            for (var x = 0; x < Carte.Largeur; x++)
                if (Climat.EstPraticable(Carte.BiomeEn(x, y)) && Climat.EstPraticable(Carte.BiomeEn(x + 1, y))) return (x, y);
        throw new InvalidOperationException("Pas de terre.");
    }

    private static Personnalite Caractere(double transcendance = 0.5, double autorite = 0.5, double compassion = 0.5, double psychopathie = 0.5,
        double purete = 0.5, double agreabilite = 0.5, double generale = 100) =>
        new Personnalite(0.5, 0.5, 0.5, agreabilite, 0.5, transcendance, 0.5, 0.5, Intelligence.Moyenne with { Generale = generale })
        {
            Moral = TraitsMoraux.Moyens with
            {
                Compassion = compassion, EmpathieEmotionnelle = compassion, Psychopathie = psychopathie,
                Fondements = FondementsMoraux.Moyens with { Autorite = autorite, Purete = purete, Soin = compassion },
            },
        };

    private static Humain Humain(int id, Sexe sexe, Personnalite? p = null, double age = 25, int mere = 0) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        MereId = mere,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = p ?? Caractere(),
        // Depuis la 0.20.4, la foi suit la croyance en l'invisible : celle d'un esprit qui y croit depuis longtemps.
        Croyance = 100 * (p ?? Caractere()).Transcendance,
        Biologie = Biologie.ParDefaut(sexe),
        X = Terre.X,
        Y = Terre.Y,
    };

    private static Comportement Comportement(int graine) => new(Carte, new Ecosysteme(Carte), new Alea((ulong)graine)) { Social = new VieSociale(new Alea((ulong)graine)) };

    [Fact]
    public void Penchant_FaitDesirer_PuisSEfface_EtLeJournalLeMarqueCommeVenuDeLEntite()
    {
        var c = Comportement(1);
        var h = Humain(1, Sexe.Homme);
        var f = Humain(2, Sexe.Femme);
        List<Humain> tous = [h, f];
        var avant = Attirance.Desir(h, f, IdealBeaute.ParDefaut, Maintenant);
        var texte = c.Inspirer(h, f, passion: true, orientation: false, tous, Maintenant);
        Assert.Contains("passion", texte.Texte);
        Assert.True(Attirance.Desir(h, f, IdealBeaute.ParDefaut, Maintenant) > avant + 0.1 || Attirance.Desir(h, f, IdealBeaute.ParDefaut, Maintenant) >= 0.99);
        Assert.Contains(c.Journal, e => e.Origine == Origine.Entite && e.Texte.Contains("passion pour H2"));
        Assert.Contains(h.Memoire.Jardin, x => x.Type == TypeIntime.Amour && x.SujetId == 2);
        var fin = h.Impulsions[0].Jusqua;
        Assert.Equal(0, h.Penchant(2, fin));
    }

    [Fact]
    public void Penchant_EnversUnParent_FaitPresqueToujoursHorreur()
    {
        var horreur = 0;
        const int Essais = 200;
        for (var g = 0; g < Essais; g++)
        {
            var c = Comportement(g + 1);
            var frere = Humain(2, Sexe.Homme, Caractere(purete: 0.6), mere: 9);
            var soeur = Humain(3, Sexe.Femme, mere: 9);
            c.Inspirer(frere, soeur, passion: false, orientation: false, [frere, soeur], Maintenant);
            if (frere.Impulsions[^1].Reponse == Reponse.Horreur)
            {
                horreur++;
                Assert.Equal(0, frere.Penchant(3, Maintenant));
                Assert.False(frere.CedeA(3, Maintenant));
            }
            else Assert.True(frere.CedeA(3, Maintenant));
        }
        Assert.InRange(horreur, (int)(0.9 * Essais), Essais - 1);
    }

    [Fact]
    public void Orientation_LEntitePeutChangerUnCoeur_SinonElleLeRespecte()
    {
        var c = Comportement(3);
        var a = Humain(1, Sexe.Homme);
        var b = Humain(2, Sexe.Homme);
        c.Inspirer(a, b, passion: true, orientation: false, [a, b], Maintenant);
        Assert.Equal(0, a.Orientation.VersHommes);
        Assert.Equal(0, Attirance.Desir(a, b, IdealBeaute.ParDefaut, Maintenant));
        c.Inspirer(a, b, passion: true, orientation: true, [a, b], Maintenant);
        Assert.True(a.Orientation.VersHommes >= 0.6);
        Assert.True(Attirance.Desir(a, b, IdealBeaute.ParDefaut, Maintenant) > 0.3);
        Assert.Contains(c.Journal, e => e.Texte.Contains("se découvre attiré par les hommes"));
    }

    [Fact]
    public void NAiePasPeur_ApaiseLeCroyant_LeSceptiqueNYCroitGuere()
    {
        int apaises = 0, incredules = 0;
        for (var g = 0; g < 100; g++)
        {
            var croyant = Humain(1, Sexe.Femme, Caractere(transcendance: 0.95));
            croyant.Peur = 80;
            Comportement(g + 1).Parler(croyant, Parole.NAiePasPeur, ModeParole.Voix, null, 0, [croyant], Maintenant);
            if (croyant.Peur < 60) apaises++;
            var sceptique = Humain(2, Sexe.Homme, Caractere(transcendance: 0.05, generale: 140));
            Comportement(g + 1000).Parler(sceptique, Parole.NAiePasPeur, ModeParole.Reve, null, 0, [sceptique], Maintenant);
            if (sceptique.Impulsions[^1].Reponse is Reponse.Incrue or Reponse.Incomprise) incredules++;
        }
        Assert.True(apaises >= 70, $"apaisés : {apaises}");
        Assert.True(incredules >= 80, $"incrédules : {incredules}");
    }

    [Fact]
    public void Tue_LeCompatissantRefuse_LePsychopatheObeitPlusSouvent()
    {
        int doux = 0, froid = 0;
        for (var g = 0; g < 200; g++)
        {
            var victime = Humain(3, Sexe.Homme);
            var bon = Humain(1, Sexe.Femme, Caractere(transcendance: 0.9, autorite: 0.8, compassion: 0.9, psychopathie: 0.1, agreabilite: 0.8));
            Comportement(g + 1).Parler(bon, Parole.Tue, ModeParole.Apparition, victime, 0, [bon, victime], Maintenant);
            if (bon.Impulsions.Exists(i => i.Parole == Parole.Tue && i.Reponse == Reponse.Acceptee)) doux++;
            var psychopathe = Humain(2, Sexe.Homme, Caractere(transcendance: 0.9, autorite: 0.8, compassion: 0.1, psychopathie: 0.99, agreabilite: 0.2));
            Comportement(g + 5000).Parler(psychopathe, Parole.Tue, ModeParole.Apparition, victime, 0, [psychopathe, victime], Maintenant);
            if (psychopathe.Impulsions.Exists(i => i.Parole == Parole.Tue && i.Reponse == Reponse.Acceptee)) froid++;
        }
        Assert.True(doux <= 20, $"compatissants qui obéissent : {doux}");
        Assert.True(froid > doux, $"psychopathes {froid}, compatissants {doux}");
    }

    [Fact]
    public void Quitte_UnCroyantObeissantQuitteSonCompagnon_EtLeDelaisseEnSouffre()
    {
        for (var g = 1; g < 400; g++)
        {
            var c = Comportement(g);
            var h = Humain(1, Sexe.Femme, Caractere(transcendance: 0.95, autorite: 0.95));
            var x = Humain(2, Sexe.Homme);
            (h.PartenaireId, x.PartenaireId) = (2, 1);
            List<Humain> tous = [h, x];
            c.Parler(h, Parole.Quitte, ModeParole.Apparition, x, 0, tous, Maintenant);
            var i = h.Impulsions.Find(k => k.Parole == Parole.Quitte);
            if (i is null || i.Reponse is not (Reponse.Acceptee or Reponse.Differee)) continue;
            c.SuivreLaParole(h, tous, i.AgirLe);
            if (h.PartenaireId != 0) continue;
            Assert.Equal(0, x.PartenaireId);
            Assert.True(x.Chagrin > 0);
            Assert.Contains(c.Journal, e => e.Texte.Contains("quitte H2, comme une voix le lui a ordonné") && e.Origine == Origine.Entite && e.Significatif);
            return;
        }
        Assert.Fail("Personne n'a jamais obéi.");
    }

    [Fact]
    public void Frappe_CeluiQuiObeitFrappe_EtLaVictimeLuiEnVeut()
    {
        for (var g = 1; g < 400; g++)
        {
            var c = Comportement(g);
            var h = Humain(1, Sexe.Homme, Caractere(transcendance: 0.95, autorite: 0.95, compassion: 0.2, agreabilite: 0.2));
            var x = Humain(2, Sexe.Homme);
            List<Humain> tous = [h, x];
            c.Parler(h, Parole.Frappe, ModeParole.Voix, x, 0, tous, Maintenant);
            var i = h.Impulsions.Find(k => k.Parole == Parole.Frappe);
            if (i is not { Reponse: Reponse.Acceptee }) continue;
            c.SuivreLaParole(h, tous, i.AgirLe);
            Assert.Contains(x.Griefs, gr => gr.ContreId == 1 && gr.Motif == MotifGrief.Coups);
            Assert.True(x.Sante < 100 || h.Sante < 100);
            Assert.Equal(Reponse.Accomplie, h.Impulsions.Find(k => k.Parole == Parole.Frappe)!.Reponse);
            return;
        }
        Assert.Fail("Personne n'a jamais obéi.");
    }

    [Fact]
    public void Impulsions_SontEnregistrees_EtUnMondeDeLaVersion20SeRelit()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-paroles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
            var h = etat.Humains![0];
            h.Impulsions.Add(new Impulsion(Parole.Penchant, ModeParole.Penchant, 2, 0, 0.6, 100, 900, 100, Reponse.Ressenti, 7, true));
            h.Impulsions.Add(new Impulsion(Parole.PartsVers, ModeParole.Reve, 0, 1234, 0.4, 200, 800, 300, Reponse.Differee, 8));
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(h.Impulsions, relu.Humains![0].Impulsions);

            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion20);
            var v20 = new DepotMonde(fichier).Charger()!;
            Assert.All(v20.Humains!, x => Assert.Empty(x.Impulsions));
            new DepotMonde(fichier).Enregistrer(v20, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void MondeSimule_ParlerATouteUneCommunaute_ChacunRepondASaManiere()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var (_, humains) = monde.InstantaneHumains();
        var ids = humains.Where(h => h.EstVivant).Select(h => h.Id).Take(20).ToArray();
        // Un signe s'interprète : tous ne le comprennent pas, tous n'y croient pas.
        var r = monde.Parler(ids, [], Parole.JeSuisCelleQuiVousAFaits, ModeParole.Signe, 0, 0);
        Assert.Equal(20, r.Count);
        Assert.True(r.Select(x => x.Resultat).Distinct().Count() >= 2, "Tous ont répondu de la même façon.");
        var (_, apres) = monde.InstantaneHumains();
        Assert.Contains(apres, h => h.Memoire.Jardin.Exists(x => x.Cle == "croyance:entite"));
    }
}
