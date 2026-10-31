using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;
using Monde.Stockage;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.32.8 : séquelles des blessures graves, groupe qui campe sur la plaine de crue, savoir du limon, pousses des camps.</summary>
public sealed class SequellesCrueRejetsTests(ITestOutputHelper sortie)
{
    private static readonly Carte Carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));

    /// <summary>Midi du deuxième jour de l'été de l'an 10.</summary>
    private static readonly long Midi = 10 * Calendrier.SecondesParAn + (Calendrier.JoursParSaison + 1) * Calendrier.SecondesParJour
                                        + 12 * Calendrier.SecondesParHeure;

    private const long Jour = Calendrier.SecondesParJour;

    private static Humain Adulte(int id, int x, int y, double age = 30, double ouverture = 0.5, double intelligence = 100, int communaute = 0) => new()
    {
        Id = id,
        Nom = $"H{id}",
        PartenaireId = 0,
        Sexe = Sexe.Femme,
        Emplacement = id,
        Naissance = Midi - (long)(age * Calendrier.SecondesParAn),
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, new Intelligence(intelligence, 100, 100, 100, 100, 100)),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        X = x,
        Y = y,
        CommunauteId = communaute,
    };

    private static Comportement Comportement(ulong graine, Ecosysteme? e = null, VieSociale? social = null) =>
        new(Carte, e ?? new Ecosysteme(Carte), new Alea(graine))
        {
            Social = social ?? new VieSociale(new Alea(1)) { Carte = Carte, Generation = 2 }, Generation = 2, Installations = new Installations(),
        };

    private static Savoir Croit(Effet effet) => effet == Effet.Limon
        ? new Savoir(Geste.Patienter, Matiere.Eau, Matiere.Plante, Effet.Limon, 0, Symptome.Aucun, 4, 4, 0, Midi, OrigineSavoir.Observation, 0, 0)
        : new Savoir(Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Repousse, 0, Symptome.Aucun, 4, 4, 0, Midi, OrigineSavoir.Observation, 0, 0);

    /// <summary>Une case de plaine praticable et riche en plantes, loin des bords.</summary>
    private static (int X, int Y) Plaine(int depart = 128)
    {
        for (var r = 0; r < 100; r++)
        for (var dx = -r; dx <= r; dx++)
        {
            int x = depart + dx, y = depart + r;
            if (Climat.EstPraticable(Carte.BiomeEn(x, y)) && Carte.Plantes[Carte.Indice(x, y)] >= 40 && Carte.Plantes[Carte.Indice(x + 8, y)] >= 40
                && Climat.EstPraticable(Carte.BiomeEn(x + 8, y))) return (x, y);
        }
        throw new InvalidOperationException("pas de plaine");
    }

    /// <summary>
    /// Une chute de 12 m qu'on survit laisse souvent une séquelle, rarement une chute de 4 m ; la gêne décroît en quelques mois puis
    /// se fixe, et l'enfant s'en remet mieux que l'adulte.
    /// </summary>
    [Fact]
    public void Chute_LaisseUneSequelle_QuiDecroitPuisSeFixe()
    {
        (double Part, double Fixe) Sequelles(double metres, double age)
        {
            int survivants = 0, abimes = 0;
            double fixe = 0;
            for (var k = 0; k < 400; k++)
            {
                var c = Comportement((ulong)k + 1);
                var h = Adulte(1, 100, 100, age: age);
                c.Reprendre([h], Midi);
                c.Tomber(h, metres, "paroi", false, false, h.X, h.Y);
                if (!h.EstVivant) continue;
                survivants++;
                Assert.True(h.BlessureGrave > 0 || metres < 5);
                c.Reprendre([h], Midi + 30 * Jour);
                c.Remettre(h);
                Assert.Equal(0, h.BlessureGrave);
                if (h.Sequelle.Membre == Membre.Aucun) continue;
                abimes++;
                var tot = Monde.Simulation.Humains.Sequelles.Gene(h, Midi + 40 * Jour);
                var tard = Monde.Simulation.Humains.Sequelles.Gene(h, Midi + 200 * Jour);
                Assert.True(tard <= tot);
                c.Reprendre([h], Midi + 300 * Jour);
                c.Remettre(h);
                Assert.True(h.Sequelle.Membre == Membre.Aucun || h.Sequelle.Debut == h.Sequelle.Fixe);
                fixe += h.Sequelle.Fixe;
            }
            return (abimes / (double)survivants, abimes == 0 ? 0 : fixe / abimes);
        }
        var (douze, quatre, enfant) = (Sequelles(12, 30), Sequelles(4, 30), Sequelles(12, 8));
        sortie.WriteLine($"12 m : {douze.Part:P0} gardent une séquelle (gêne fixée moyenne {douze.Fixe:P0}) ; 4 m : {quatre.Part:P0} ; enfant à 12 m : {enfant.Part:P0}, gêne {enfant.Fixe:P0}");
        Assert.InRange(douze.Part, 0.15, 0.85);
        Assert.True(quatre.Part < 0.1);
        Assert.True(enfant.Fixe < douze.Fixe);
    }

    /// <summary>Celui qui boite marche moins vite, chasse moins bien, et tombe plus facilement ; le compatissant le nourrit plus tôt.</summary>
    [Fact]
    public void Boiteux_MarcheMoinsVite_EtTombePlus()
    {
        var c = Comportement(1);
        var sain = Adulte(1, 100, 100);
        var boiteux = Adulte(2, 100, 100);
        boiteux.Sequelle = new Sequelle(Membre.Jambe, 0.6, 0.6, Midi - 400 * Jour, OrigineBlessure.Chute);
        c.Reprendre([sain, boiteux], Midi);
        Assert.Equal(0.6, Monde.Simulation.Humains.Sequelles.Gene(boiteux, Midi), 6);
        Assert.True(Marche.Vitesse(boiteux, Midi) < 0.75 * Marche.Vitesse(sain, Midi));
        Assert.True(c.FacteurDeChute(boiteux, 0, false, false) > 1.5 * c.FacteurDeChute(sain, 0, false, false));
    }

    /// <summary>Hors de la génération 2, une blessure grave ne laisse rien : les anciens mondes ne changent pas.</summary>
    [Fact]
    public void Generation1_SansSequelle()
    {
        var conf = ConfigurationMonde.ParDefaut(7);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Coeurs = 1 };
        monde.AvancerDe(400 * Jour);
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains, h => Assert.Equal(Membre.Aucun, h.Sequelle.Membre));
        Assert.All(monde.CarteRejets(), v => Assert.Equal(0, v));
    }

    /// <summary>
    /// Des croyants du limon dans un groupe qui ne manque de rien : il peut décider d'aller camper sur la plaine riche dont l'un se
    /// souvient ; sans croyants, jamais.
    /// </summary>
    [Fact]
    public void Groupe_VaCamperSurLaPlaineDeCrue()
    {
        var (x, y) = Plaine();
        var (lx, ly) = (x + 8, y);
        int Partent(int croyants)
        {
            var n = 0;
            for (var k = 0; k < 200; k++)
            {
                var e = new Ecosysteme(Carte);
                for (var dy = -3; dy <= 3; dy++)
                for (var dx = -3; dx <= 3; dx++)
                {
                    var j = Carte.Indice(lx + dx, ly + dy);
                    e.Deposer(j, 0.5, 200);
                    e.Plantes[j] = (ushort)Math.Min(ushort.MaxValue, Carte.Plantes[j] * 100 * 1.5);
                }
                var social = new VieSociale(new Alea((ulong)k + 1)) { Carte = Carte, Generation = 2 };
                var c = new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1, CentreX = x, CentreY = y, Rayon = 3, Abondance = 0.7 };
                var groupe = Enumerable.Range(1, 6).Select(id => Adulte(id, x, y, communaute: 1)).ToList();
                foreach (var h in groupe.Take(croyants))
                {
                    h.Savoirs.Add(Croit(Effet.Limon));
                    h.Memoire.Retenir(TypeLieu.Crue, lx, ly, Midi - 60 * Jour);
                }
                if (social.SuivreLaCrue(c, groupe, e, Carte, Midi))
                {
                    n++;
                    Assert.Equal((lx, ly), (c.DestinationX, c.DestinationY));
                    Assert.Contains(social.Journal, j => j.Recit?.Cle == "limon.part");
                }
            }
            return n;
        }
        var (quatre, aucun) = (Partent(4), Partent(0));
        sortie.WriteLine($"{quatre} groupes sur 200 partent (une semaine, 4 croyants sur 6), {aucun} sans croyant");
        Assert.InRange(quatre, 10, 150);
        Assert.Equal(0, aucun);
        var installe = new EvenementJournal(Midi, TypeEvenement.Communaute, 1, 0, new Recit("limon.installe")) { CommunauteId = 1 };
        Assert.Contains(PremieresFois.Sortes(installe, _ => null), s => s.Sorte == "campCrue");
    }

    /// <summary>L'enfant qui cueille avec un croyant sur la plaine que la crue a rendue riche peut apprendre le limon.</summary>
    [Fact]
    public void Limon_ApprisParLesEnfants()
    {
        var (x, y) = Plaine();
        var i = Carte.Indice(x, y);
        var appris = 0;
        for (var k = 0; k < 200; k++)
        {
            var e = new Ecosysteme(Carte);
            e.Deposer(i, 0.5, 200);
            var c = Comportement((ulong)k + 1, e);
            var adulte = Adulte(1, x, y, communaute: 1);
            adulte.Savoirs.Add(Croit(Effet.Limon));
            var enfant = Adulte(2, x, y, age: 9, communaute: 1);
            c.Reprendre([adulte, enfant], Midi);
            for (var fois = 0; fois < 20; fois++) c.ConstaterLeLimon(enfant, i, true);
            if (enfant.Savoirs.Exists(s => s.Effet == Effet.Limon && s.Origine == OrigineSavoir.Imitation && s.MaitreId == 1)) appris++;
        }
        sortie.WriteLine($"{appris} enfants sur 200 retiennent le limon en cueillant avec un croyant");
        Assert.InRange(appris, 20, 199);
    }

    /// <summary>
    /// Autour du camp, à la belle saison, ce qu'on jette germe ; sans personne, les pousses s'effacent en un an ou deux. Un
    /// observateur peut faire le lien, puis enfouir lui-même des graines.
    /// </summary>
    [Fact]
    public void Rejets_GermentAuCamp_SontRemarques_PuisEnfouis()
    {
        var (x, y) = Plaine();
        var i = Carte.Indice(x, y);
        var e = new Ecosysteme(Carte);
        var social = new VieSociale(new Alea(1)) { Carte = Carte, Generation = 2 };
        var c = new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1, CentreX = x, CentreY = y, Rayon = 3 };
        for (var semaine = 0; semaine < 10; semaine++) social.Jeter(c, 30, e, Carte, Midi + semaine * 7 * Jour);
        sortie.WriteLine($"Pousses au camp après dix semaines d'été : +{e.Rejets[i] / 100.0:0} %");
        Assert.InRange(e.Rejets[i], 3_000, Ecosysteme.RejetsMax);
        var avant = e.NiveauPlantes(i, 1);
        Assert.True(avant > new Ecosysteme(Carte).NiveauPlantes(i, 1) * 1.25);
        var debut = e.Rejets[i];
        for (var jour = 0; jour < 365; jour++) e.Journee(Midi + jour * Jour);
        Assert.InRange(e.Rejets[i], debut / 4, debut * 3 / 4);

        int Remarquent(double intelligence)
        {
            var n = 0;
            for (var k = 0; k < 300; k++)
            {
                var eco = new Ecosysteme(Carte);
                eco.Germer(i, 0.4);
                var comportement = Comportement((ulong)k + 1, eco);
                var h = Adulte(1, x, y, ouverture: 0.8, intelligence: intelligence);
                comportement.Reprendre([h], Midi);
                for (var fois = 0; fois < 50; fois++) comportement.ConstaterLesRejets(h, i);
                if (h.Savoirs.Exists(s => s.Effet == Effet.Repousse)) n++;
            }
            return n;
        }
        var (vifs, lents) = (Remarquent(135), Remarquent(75));
        sortie.WriteLine($"{vifs} esprits vifs sur 300 remarquent que ce qu'on jette repousse, contre {lents} esprits lents");
        Assert.InRange(vifs, 10, 200);
        Assert.True(vifs > lents);

        // Qui y croit enfouit des graines près du camp.
        var eco2 = new Ecosysteme(Carte);
        var social2 = new VieSociale(new Alea(1)) { Carte = Carte, Generation = 2, Communautes = [c] };
        var semeur = Adulte(3, x, y, communaute: 1);
        semeur.Savoirs.Add(Croit(Effet.Repousse) with { Essais = 1, Succes = 1 });
        var comp = Comportement(5, eco2, social2);
        comp.Reprendre([semeur], Midi);
        for (var fois = 0; fois < 400; fois++) comp.ConstaterLesRejets(semeur, i);
        Assert.True(eco2.Rejets[i] > 0);
        Assert.Contains(comp.Journal, j => j.Recit?.Cle == "rejets.enfouit");
    }

    /// <summary>La séquelle, la blessure en train de guérir et les pousses des camps sont relues à l'identique.</summary>
    [Fact]
    public void Corps_EtRejets_SontRelus()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-corps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var conf = ConfigurationMonde.Nouvelle(42);
            var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Coeurs = 1 };
            monde.AvancerDe(Jour);
            var etat = monde.Etat;
            var humains = etat.Humains!.Select(h => h.Copier()).ToList();
            humains[0].Sequelle = new Sequelle(Membre.Bras, 0.7, 0.3, 1234, OrigineBlessure.Guerre);
            (humains[1].BlessureGrave, humains[1].BlessureOrigine) = (62.5, OrigineBlessure.Chute);
            var rejets = new ushort[Carte.Largeur * Carte.Hauteur];
            rejets[777] = 2_500;
            etat = etat with { Humains = humains, Rejets = rejets };
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(humains[0].Sequelle, relu.Humains![0].Sequelle);
            Assert.Equal((62.5, OrigineBlessure.Chute), (relu.Humains[1].BlessureGrave, relu.Humains[1].BlessureOrigine));
            Assert.Equal(Membre.Aucun, relu.Humains[2].Sequelle.Membre);
            Assert.Equal(rejets, relu.Rejets);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
