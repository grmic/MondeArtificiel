using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.20.5 : médecine (soignants, plaies, accouchement, transe, placebo, nocebo, charlatans).</summary>
public sealed class MedecineTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static Humain Humain(int id, double age = 25, Sexe sexe = Sexe.Femme, double transcendance = 0.5, double croyance = 5,
        int mere = 0, double compassion = 0.5, int communaute = 0) => new()
    {
        Id = id, Nom = "H" + id, Sexe = sexe, PartenaireId = 0, Emplacement = 1, Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, transcendance, 0.5, 0.5, Intelligence.Moyenne)
        {
            Moral = TraitsMoraux.Moyens with { Compassion = compassion },
        },
        Biologie = Biologie.ParDefaut(sexe), Reserves = 50, Sante = 100, MereId = mere, CommunauteId = communaute,
        X = Carte.Emplacements[0].X, Y = Carte.Emplacements[0].Y, Croyance = croyance,
    };

    private static Comportement Comportement(ulong graine = 1) =>
        new(Carte, new Ecosysteme(Carte), new Alea(graine)) { Social = new VieSociale(new Alea(graine)) };

    private static Savoir Sait(Geste g, Matiere a, Effet e, int espece = 0, Symptome s = Symptome.Aucun) =>
        new(g, a, Matiere.Aucune, e, espece, s, 10, 9, Maintenant, Maintenant, OrigineSavoir.Essai, 0, 0);

    [Fact]
    public void Placebo_SoulageSelonLaCroyanceEtLaRenommee_SansJamaisGuerir()
    {
        var renomme = Humain(1);
        renomme.Gueris = 20;
        renomme.GuerisseurDepuis = Maintenant - An;
        var inconnu = Humain(2);
        var croyant = Humain(3, croyance: 90);
        var incroyant = Humain(4, croyance: 0);
        foreach (var p in new[] { croyant, incroyant })
        {
            p.Peur = 60;
            p.Maladie = Maladie.Pneumonie;
            p.MaladeJusqua = Maintenant + 5 * Jour;
        }
        var fort = Medecine.Placebo(croyant, renomme, transe: true);
        var faible = Medecine.Placebo(incroyant, inconnu, transe: false);
        Assert.True(fort > faible + 0.5, $"{fort:0.00} contre {faible:0.00}");
        Assert.True(croyant.Peur < incroyant.Peur - 10, $"{croyant.Peur:0} contre {incroyant.Peur:0}");
        Assert.Equal(Maintenant + 5 * Jour, croyant.MaladeJusqua);
        Assert.Equal(Maladie.Pneumonie, croyant.Maladie);
    }

    [Fact]
    public void PlaieSoignee_SInfecteMoins_EtSeRefermePlusVite()
    {
        var h = Humain(1);
        Assert.Equal(0, Medecine.Protection(h, Maladie.PlaieInfectee, Maintenant));
        Medecine.Blesser(h, 2, Maintenant);
        Assert.False(Medecine.PlaieOuverte(h, Maintenant + 1)); // une égratignure n'ouvre pas de plaie
        Medecine.Blesser(h, 20, Maintenant);
        Assert.True(Medecine.PlaieOuverte(h, Maintenant + Jour));
        Assert.Equal(0, Medecine.Protection(h, Maladie.PlaieInfectee, Maintenant + Jour));
        var brute = Medecine.DureePlaie(h);
        h.PlaieSoin = 0.6;
        Assert.Equal(0.42, Medecine.Protection(h, Maladie.PlaieInfectee, Maintenant + Jour), 6);
        Assert.Equal(0.21, Medecine.Protection(h, Maladie.Tetanos, Maintenant + Jour), 6);
        Assert.Equal(0, Medecine.Protection(h, Maladie.Pneumonie, Maintenant + Jour));
        Assert.True(Medecine.DureePlaie(h) < brute * 0.85);
        Assert.False(Medecine.PlaieOuverte(h, Maintenant + 40 * Jour));
    }

    [Fact]
    public void Pansement_UnProcheLaveLaPlaie_EstCompteCommeSoignant_EtLaPlaieRefermeeConfirmeLeGeste()
    {
        var c = Comportement();
        var panseur = Humain(1);
        panseur.Savoirs.Add(Sait(Geste.Panser, Matiere.Eau, Effet.Pansement, s: Symptome.Plaie));
        panseur.Objets.Add(new Objet(Matiere.Calebasse, 1, 1, Plein: true));
        var blesse = Humain(2);
        List<Humain> tous = [panseur, blesse];
        c.Soins(tous, Maintenant);
        Medecine.Blesser(blesse, 25, Maintenant);
        Assert.True(c.Panser(panseur, blesse, tous));
        Assert.Equal(Medecine.Qualite(Matiere.Eau), blesse.PlaieSoin, 6);
        Assert.Equal(0, panseur.Soignes); // compté quand l'issue sera connue
        Assert.Equal(1, blesse.SoigneurId);
        var e = Assert.Single(c.Journal, x => x.Type == TypeEvenement.Entraide);
        Assert.Contains("soigne la plaie de H2 : elle la lave", e.Texte);
        Assert.Equal(Regard.Panser, e.Karma);

        // La plaie se referme sans pourrir : le geste est confirmé, le blessé est compté guéri pour son soignant.
        c.Soins(tous, Maintenant + 40 * Jour);
        Assert.Equal(10, panseur.Savoirs[0].Succes);
        Assert.Equal((1, 1), (panseur.Soignes, panseur.Gueris));
        Assert.Equal(0, blesse.SoigneurId);
    }

    [Fact]
    public void Accouchement_DesProchesExperimenteesAident_EtReduisentLeRisque()
    {
        var grandMere = Humain(1, age: 50);
        grandMere.AccouchementsAides = 12;
        var mere = Humain(2, age: 22, mere: 1);
        List<Humain> tous = [grandMere, mere];
        // Aider reste un choix : on essaie quelques tirages jusqu'à ce qu'elle vienne.
        Comportement c;
        double r;
        ulong graine = 0;
        do
        {
            grandMere.AccouchementsAides = 12;
            c = Comportement(++graine);
            c.Soins(tous, Maintenant);
            r = c.AiderAccouchement(mere, tous, Maintenant);
        } while (r == 0 && graine < 20);
        Assert.InRange(r, 0.3, 0.45);
        Assert.Equal(13, grandMere.AccouchementsAides);
        var e = Assert.Single(c.Journal, x => x.Type == TypeEvenement.Entraide);
        Assert.Equal("H1 aide H2 à accoucher.", e.Texte);
        Assert.Equal(Regard.AiderAccoucher, e.Karma);

        // Seule, sans personne autour : aucune aide.
        Assert.Equal(0, Comportement().AiderAccouchement(mere, [mere], Maintenant));
    }

    [Fact]
    public void Transe_LePremierChamanEstNote_EtLesTemoinsCroientDavantage()
    {
        var c = Comportement();
        var chaman = Humain(1, transcendance: 0.8, croyance: 70);
        chaman.Savoirs.Add(Sait(Geste.Manger, Matiere.Plante, Effet.Visions, espece: 1));
        Assert.True(Medecine.PeutEntrerEnTranse(chaman, Maintenant));
        Assert.False(Medecine.PeutEntrerEnTranse(Humain(9, transcendance: 0.8, croyance: 70), Maintenant)); // jamais rien vu
        var malade = Humain(2, croyance: 40);
        malade.Peur = 70;
        var temoin = Humain(3, croyance: 10);
        List<Humain> tous = [chaman, malade, temoin];
        c.Soins(tous, Maintenant);
        c.Transe(chaman, malade, tous);
        Assert.Equal(Maintenant, chaman.ChamanDepuis);
        Assert.Equal(Maintenant, chaman.TranseLe);
        var e = Assert.Single(c.Journal, x => x.Type == TypeEvenement.Medecine);
        Assert.True(e.Significatif);
        Assert.Contains("entre en transe au chevet de H2", e.Texte);
        Assert.True(temoin.Croyance > 11, $"{temoin.Croyance}");
        Assert.Equal(Maintenant, temoin.CroyanceSigneLe);
        Assert.True(malade.Peur < 60, $"{malade.Peur}");
        Assert.Equal("chamane", Medecine.Role(chaman));
    }

    [Fact]
    public void Malediction_RongeCeluiQuiCroit_LIncroyantHausseLesEpaules_EtLaPeurFinitParRefluer()
    {
        var c = Comportement(3);
        var chaman = Humain(1, transcendance: 0.8, croyance: 70, compassion: 0.1);
        chaman.ChamanDepuis = Maintenant - An;
        var croyant = Humain(2, croyance: 90);
        var incroyant = Humain(3, croyance: 5);
        List<Humain> tous = [chaman, croyant, incroyant];
        c.Soins(tous, Maintenant);
        c.Maudire(chaman, croyant);
        c.Maudire(chaman, incroyant);
        Assert.Equal(Maintenant, croyant.MauditLe);
        Assert.InRange(croyant.MauditForce, 0.5, 1);
        Assert.True(croyant.Peur >= 40);
        Assert.Equal(0, incroyant.MauditLe);
        Assert.Contains(c.Journal, e => e.Texte.Contains("hausse les épaules"));
        Assert.All(c.Journal.Where(e => e.Type == TypeEvenement.Medecine), e => Assert.Equal(Regard.Maudire, e.Karma));
        Assert.True(Medecine.Souffrant(croyant, Maintenant));

        for (var j = 1; j <= 2000 && croyant.MauditLe != 0; j++) c.Soins(tous, Maintenant + j * Jour);
        Assert.Equal(0, croyant.MauditLe);
        Assert.Contains(c.Journal, e => e.Texte.Contains("La peur de H2 reflue"));
    }

    [Fact]
    public void Malediction_RongeLaSantePasAPas_SansPlanter()
    {
        var c = Comportement(5);
        var chaman = Humain(1, transcendance: 0.8, croyance: 70, compassion: 0.1);
        chaman.ChamanDepuis = Maintenant - An;
        var croyant = Humain(2, croyance: 95);
        croyant.Personnalite = croyant.Personnalite with { Nevrosisme = 0.9 };
        List<Humain> tous = [chaman, croyant];
        c.Soins(tous, Maintenant);
        c.Maudire(chaman, croyant);
        Assert.True(croyant.MauditForce > 0.9, $"{croyant.MauditForce}");
        croyant.Sante = 80;
        // Une heure de vie : la malédiction ronge la santé, au lieu de laisser le corps se refaire.
        for (var pas = 1; pas <= 6; pas++) c.Vivre(croyant, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        Assert.True(croyant.EstVivant);
        Assert.True(croyant.Sante < 80, $"{croyant.Sante}");
    }

    [Fact]
    public void Reconnaissance_LeMeilleurSoignantDevientGuerisseur_LeCharlatanEstDemasque()
    {
        var c = Comportement();
        var tous = Enumerable.Range(1, 5).Select(i => Humain(i, communaute: 1)).ToList();
        tous[0].Gueris = 8;
        tous[0].Perdus = 1;
        tous[0].Soignes = 9;
        var charlatan = tous[1];
        charlatan.CharlatanDepuis = Maintenant - An;
        charlatan.Soignes = 5;
        charlatan.Gueris = 2;
        charlatan.Perdus = 3;
        Assert.Equal("charlatane (prétend guérir)", Medecine.Role(charlatan));
        c.Soins(tous, Maintenant);
        Assert.Equal(Maintenant, tous[0].GuerisseurDepuis);
        Assert.Contains(c.Journal, e => e.Texte.StartsWith("Les siens tiennent désormais H1 pour une guérisseuse") && e.Significatif);
        Assert.Equal(0, charlatan.CharlatanDepuis);
        Assert.Equal(Maintenant, charlatan.DemasqueLe);
        Assert.Contains(c.Journal, e => e.Texte.StartsWith("Les siens démasquent H2"));
        Assert.True(tous[2].AffectionPour(charlatan.Id) < 0);
    }

    [Fact]
    public void LeDernierSoignant_RecoitLeCreditDUneGuerison_EtPerdCeuxQuiMeurent()
    {
        var c = Comportement();
        var s = Humain(1);
        var gueri = Humain(2);
        var mort = Humain(3);
        List<Humain> tous = [s, gueri, mort];
        foreach (var p in new[] { gueri, mort })
        {
            p.Maladie = Maladie.Diarrhee;
            p.MaladeJusqua = Maintenant + 3 * Jour;
        }
        c.Soins(tous, Maintenant);
        c.MarquerSoin(s, gueri);
        c.MarquerSoin(s, mort);
        c.MarquerSoin(s, mort); // le même mal ne compte qu'une fois
        Assert.Equal(0, s.Soignes);
        c.Tuer(mort, Maintenant + Jour, CauseDeMort.Maladie, "H3 meurt.", 0);
        Assert.Equal((1, 1), (s.Soignes, s.Perdus));
        c.Soins(tous, Maintenant + 2 * Jour);
        Assert.Equal(0, s.Gueris); // encore malade
        c.Soins(tous, Maintenant + 4 * Jour);
        Assert.Equal((2, 1), (s.Soignes, s.Gueris));
    }

    [Fact]
    public void DeuxAns_DesAccouchementsSontAides_EtLaMedecineSeRelit()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-medecine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(2 * An);
            var etat = monde.Etat;
            Assert.Contains(etat.JournalNonEnregistre!, e => e.Type == TypeEvenement.Entraide && e.Texte.Contains("à accoucher"));
            Assert.Contains(etat.Humains!, h => h.AccouchementsAides > 0);

            // Des valeurs de médecine sur un humain : elles se relisent telles quelles.
            var h = etat.Humains![0];
            (h.Soignes, h.Gueris, h.Perdus, h.GuerisseurDepuis, h.ChamanDepuis, h.TranseLe) = (7, 5, 1, 11, 12, 13);
            (h.CharlatanDepuis, h.DemasqueLe, h.SoigneurId, h.SoigneurLe, h.PlaieLe, h.PlaieSoin) = (14, 15, 2, 16, 17, 0.35);
            (h.MauditLe, h.MauditParId, h.MauditForce, h.MauditEvenementId) = (18, 3, 0.5, 19);
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            for (var i = 0; i < etat.Humains.Count; i++)
            {
                var (a, b) = (etat.Humains[i], relu.Humains![i]);
                Assert.Equal((a.Soignes, a.Gueris, a.Perdus, a.AccouchementsAides, a.GuerisseurDepuis, a.ChamanDepuis, a.TranseLe),
                    (b.Soignes, b.Gueris, b.Perdus, b.AccouchementsAides, b.GuerisseurDepuis, b.ChamanDepuis, b.TranseLe));
                Assert.Equal((a.CharlatanDepuis, a.DemasqueLe, a.SoigneurId, a.SoigneurLe, a.PlaieLe, a.PlaieSoin),
                    (b.CharlatanDepuis, b.DemasqueLe, b.SoigneurId, b.SoigneurLe, b.PlaieLe, b.PlaieSoin));
                Assert.Equal((a.MauditLe, a.MauditParId, a.MauditForce, a.MauditEvenementId), (b.MauditLe, b.MauditParId, b.MauditForce, b.MauditEvenementId));
            }

            // Un monde de la version 23 se relit, sans rien de tout cela.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion23);
            var v23 = new DepotMonde(fichier).Charger()!;
            Assert.All(v23.Humains!, x => Assert.Equal((0, 0, 0L, 0L), (x.Soignes, x.AccouchementsAides, x.PlaieLe, x.MauditLe)));
            new DepotMonde(fichier).Enregistrer(v23, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
