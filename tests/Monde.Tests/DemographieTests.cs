using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Stockage;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.20.3 : orphelins allaités par une autre femme, femmes enlevées dans les raids, journal sans la vie quotidienne.</summary>
public sealed class DemographieTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 20 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static Humain Humain(int id, Sexe sexe, double age, int mere = 0, int pere = 0, double psychopathie = 0.5, double attachement = 0.9)
    {
        var (x, y) = (Carte.Emplacements[0].X, Carte.Emplacements[0].Y);
        return new Humain
        {
            Id = id, Nom = "H" + id, Sexe = sexe, PartenaireId = 0, Emplacement = 1,
            Naissance = Maintenant - (long)(age * An),
            Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, attachement, 0.5, Intelligence.Moyenne)
            {
                Moral = TraitsMoraux.Moyens with { Psychopathie = psychopathie },
            },
            Biologie = Biologie.ParDefaut(sexe), Reserves = 50, Sante = 100, MereId = mere, PereId = pere, X = x, Y = y,
        };
    }

    private static Comportement Comportement() => new(Carte, new Ecosysteme(Carte), new Alea(3)) { Social = new VieSociale(new Alea(3)) };

    [Fact]
    public void Orphelin_UneFemmeQuiAllaite_LePrendEtLeNourrit_UnCoeurFroidNon()
    {
        var mere = Humain(1, Sexe.Femme, 25);
        var froide = Humain(2, Sexe.Femme, 27, psychopathie: 0.999);
        var bonne = Humain(3, Sexe.Femme, 30);
        var sansLait = Humain(4, Sexe.Femme, 50);
        var bebe = Humain(5, Sexe.Homme, 0.2, mere: 1);
        mere.MortLe = Maintenant - Jour;
        froide.DernierAccouchement = bonne.DernierAccouchement = Maintenant - An / 2;
        var tous = new List<Humain> { mere, froide, bonne, sansLait, bebe };
        var c = Comportement();

        Assert.Same(bonne, c.Nourrice(bebe, tous, Maintenant));
        var note = Assert.Single(c.Journal, e => e.Type == TypeEvenement.Entraide);
        Assert.Contains("H3 prend H5", note.Texte);
        Assert.Contains(bonne.Memoire.Jardin, x => x.Cle == "nourrice:5");

        // Porté et allaité pendant deux jours, il ne meurt pas de soif ; la prise n'est notée qu'une fois.
        for (var pas = 1; pas <= 2 * Jour / MondeSimule.SecondesParPas; pas++)
        {
            var t = Maintenant + pas * MondeSimule.SecondesParPas;
            bonne.Faim = bonne.Soif = 10;
            c.Vivre(bebe, tous, t);
        }
        Assert.True(bebe.EstVivant);
        Assert.True(bebe.Soif < 50);
        Assert.Single(c.Journal, e => e.Type == TypeEvenement.Entraide);

        // Sans femme qui allaite, un nourrisson de deux mois n'a personne.
        bonne.DernierAccouchement = froide.DernierAccouchement = null;
        bonne.Memoire.Jardin.Clear();
        Assert.Null(Comportement().Nourrice(bebe, tous, Maintenant));
    }

    [Fact]
    public void Orphelin_SevrableSansNourrice_EstPorteParSonPere()
    {
        var mere = Humain(1, Sexe.Femme, 25);
        var pere = Humain(2, Sexe.Homme, 28);
        var bebe = Humain(3, Sexe.Femme, 0.8, mere: 1, pere: 2);
        mere.MortLe = Maintenant - Jour;
        var c = Comportement();
        Assert.Same(pere, c.Nourrice(bebe, [mere, pere, bebe], Maintenant));
        Assert.Contains(c.Journal, e => e.Texte.Contains("bouchées mâchées"));
    }

    [Fact]
    public void Raid_LesJeunesFemmesSontPlusSouventEnleveesQueTuees()
    {
        var guerrier = Humain(1, Sexe.Homme, 25);
        var froid = Humain(2, Sexe.Homme, 25, psychopathie: 0.999);
        var jeune = Humain(3, Sexe.Femme, 20);
        var agee = Humain(4, Sexe.Femme, 50);
        Assert.True(VieSocialeChance(guerrier, jeune) > 0.5);
        Assert.True(VieSocialeChance(guerrier, agee) < VieSocialeChance(guerrier, jeune));
        Assert.True(VieSocialeChance(froid, jeune) < VieSocialeChance(guerrier, jeune));
        static double VieSocialeChance(Humain a, Humain f) => Monde.Simulation.Humains.Comportement.ChanceDEnlever(a, f, Maintenant);
    }

    [Fact]
    public void Captive_LibreALaMortDuRavisseur_ResteSiElleAEuUnEnfantChezLui()
    {
        var social = new VieSociale(new Alea(1));
        var ravisseur = Humain(1, Sexe.Homme, 30);
        var captive = Humain(2, Sexe.Femme, 20);
        var autre = Humain(3, Sexe.Femme, 22);
        captive.CaptifDe = autre.CaptifDe = 1;
        captive.CaptifLe = autre.CaptifLe = Maintenant - An;
        captive.DernierAccouchement = Maintenant - Jour;
        var tous = new List<Humain> { ravisseur, captive, autre };

        social.Captivites(tous, Maintenant);
        Assert.Equal(0, captive.CaptifDe);
        Assert.Contains(social.Journal, e => e.Texte.StartsWith("H2 ne cherche plus à fuir"));

        ravisseur.MortLe = Maintenant;
        social.Captivites(tous, Maintenant + Jour);
        Assert.Equal(0, autre.CaptifDe);
        Assert.Contains(social.Journal, e => e.Texte.Contains("H3, qu'il avait enlevée, n'est plus captive"));
    }

    [Fact]
    public void Captive_SuitSonRavisseur()
    {
        var ravisseur = Humain(1, Sexe.Homme, 30);
        var captive = Humain(2, Sexe.Femme, 20);
        captive.CaptifDe = 1;
        ravisseur.X += 6;
        var c = Comportement();
        // En plein jour d'été, sans besoin pressant : elle marche vers lui.
        for (var pas = 0; pas < 6; pas++)
        {
            captive.Faim = captive.Soif = captive.Fatigue = captive.Froid = 0;
            c.Vivre(captive, [ravisseur, captive], Maintenant + 120 * Jour + 12 * 3600 + pas * MondeSimule.SecondesParPas);
        }
        Assert.Equal(Activite.SuivreRavisseur, captive.Activite);
        Assert.True(Math.Abs(captive.X - ravisseur.X) < 6);
    }

    [Fact]
    public void JournalQuotidien_NonParDefaut_OuiSurDemande()
    {
        var sans = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        sans.AvancerDe(20 * Jour);
        Assert.DoesNotContain(sans.Etat.JournalNonEnregistre!, e => e.Type == TypeEvenement.Journee);

        var avec = new MondeSimule(Carte, MondeSimule.Nouveau(42)) { JournalQuotidien = true };
        avec.AvancerDe(20 * Jour);
        Assert.Contains(avec.Etat.JournalNonEnregistre!, e => e.Type == TypeEvenement.Journee);
        Assert.True(avec.Etat.JournalNonEnregistre!.Count > 2 * sans.Etat.JournalNonEnregistre!.Count);
    }

    [Fact]
    public void Captive_EstEnregistree_EtUnMondeDeLaVersion21SeRelit()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-captive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
            etat.Humains![1].CaptifDe = 1;
            etat.Humains![1].CaptifLe = 1234;
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal((1, 1234L), (relu.Humains![1].CaptifDe, relu.Humains![1].CaptifLe));

            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion21);
            var v21 = new DepotMonde(fichier).Charger()!;
            Assert.All(v21.Humains!, x => Assert.Equal(0, x.CaptifDe));
            new DepotMonde(fichier).Enregistrer(v21, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
