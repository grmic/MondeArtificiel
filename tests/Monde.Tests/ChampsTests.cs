using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.33.0 : espèces nourricières, parcelles, récolte et semence gardée, lignées, idée des champs.</summary>
public sealed class ChampsTests(ITestOutputHelper sortie)
{
    private static readonly Carte Carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));
    private static readonly Flore LaFlore = new(2, true);

    /// <summary>Midi du deuxième jour de l'été de l'an 10.</summary>
    private static readonly long Midi = 10 * Calendrier.SecondesParAn + (Calendrier.JoursParSaison + 1) * Calendrier.SecondesParJour
                                        + 12 * Calendrier.SecondesParHeure;

    private const long Jour = Calendrier.SecondesParJour;

    private static Humain Adulte(int id, int communaute = 1) => new()
    {
        Id = id,
        Nom = $"H{id}",
        PartenaireId = 0,
        Sexe = Sexe.Femme,
        Emplacement = id,
        Naissance = Midi - 30 * Calendrier.SecondesParAn,
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, new Intelligence(100, 100, 100, 100, 100, 100)),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        X = 100,
        Y = 100,
        CommunauteId = communaute,
    };

    private static Comportement Comportement(Champs champs, ulong graine = 1) =>
        new(Carte, new Ecosysteme(Carte), new Alea(graine))
        {
            Social = new VieSociale(new Alea(1)) { Carte = Carte, Generation = 2, Champs = champs }, Generation = 2,
            Installations = new Installations(), Flore = LaFlore, Champs = champs,
        };

    private static Savoir Sait(Effet effet, Geste geste = Geste.Amasser, int espece = 0) =>
        new(geste, Matiere.Plante, Matiere.Aucune, effet, espece, Symptome.Aucun, 8, 8, 0, Midi, OrigineSavoir.Observation, 0, 0);

    private static Espece Graminee() => LaFlore.Especes.First(e => e.Aliment is { Sorte: SorteAliment.Graines, Vivace: false } a && a.SeDisperse);

    private static Espece ABouture() => LaFlore.Especes.First(e => e.Aliment is { Bouture: true });

    /// <summary>Une parcelle mûre : un dixième des graines tient à la tige.</summary>
    private static Parcelle Mure(Champs champs, Lignee l, int semeur, double tenaces = 1, double cassantes = 9)
    {
        var p = champs.Semer(100, 100, l, 1, l.Tenaces, semeur, l.CommunauteId, Midi - 120 * Jour, 0, 0, false, 0);
        p = p with { Etat = EtatParcelle.Mure, SurPiedTenaces = tenaces, SurPiedCassantes = cassantes, Prevue = tenaces + cassantes, MureLe = Midi - Jour };
        champs.Changer(p);
        return p;
    }

    /// <summary>
    /// Arracher les tiges garde la part de graines tenaces qui reste sur pied ; secouer les épis au-dessus d'un panier ne prend
    /// presque que les graines qui se détachent : la semence gardée en porte moins (Hillman et Davies, 1990).
    /// </summary>
    [Fact]
    public void Recolte_ArracherGardeLesTenaces_SecouerLesPerd()
    {
        double PartGardee(Geste facon)
        {
            var champs = new Champs();
            var h = Adulte(1);
            h.Savoirs.Add(Sait(Effet.Moisson, facon));
            h.Savoirs.Add(Sait(Effet.Semence));
            var l = champs.NouvelleLignee(Graminee(), 1, Midi - Calendrier.SecondesParAn, 1, 0);
            var p = Mure(champs, l, 1);
            var c = Comportement(champs);
            c.Reprendre([h], Midi);
            c.Recolter(h, p, [h]);
            return champs.Lignee(l.Id)!.Tenaces;
        }
        var (arracher, secouer) = (PartGardee(Geste.Tordre), PartGardee(Geste.Frapper));
        sortie.WriteLine($"graines tenaces dans la semence : arrachées {arracher:P1}, secouées {secouer:P1} (sur pied : 10 %)");
        Assert.Equal(0.1, arracher, 3);
        Assert.True(secouer < 0.85 * arracher);
    }

    /// <summary>Dix parcelles récoltées la même saison ne font qu'une génération de la lignée.</summary>
    [Fact]
    public void Semence_UneGenerationParAn()
    {
        var champs = new Champs();
        var h = Adulte(1);
        h.Savoirs.Add(Sait(Effet.Semence));
        var e = ABouture();
        var l = champs.NouvelleLignee(e, 1, Midi - Calendrier.SecondesParAn, 1, 0);
        var c = Comportement(champs);
        c.Reprendre([h], Midi);
        for (var k = 0; k < 10; k++)
        {
            var p = Mure(champs, l, 1, tenaces: 20, cassantes: 0);
            c.Recolter(h, p, [h]);
            champs.Changer(champs.Parcelle(p.Id)! with { Etat = EtatParcelle.Finie });
        }
        var apres = champs.Lignee(l.Id)!;
        Assert.Equal(1, apres.Generations);
        Assert.True(apres.Semence > 0);
        Assert.InRange(apres.Vigueur, 1.0001, 1.1);
        // L'an d'après : une génération de plus.
        c.Reprendre([h], Midi + Calendrier.SecondesParAn);
        c.Recolter(h, Mure(champs, l, 1, tenaces: 20, cassantes: 0), [h]);
        Assert.Equal(2, champs.Lignee(l.Id)!.Generations);
    }

    /// <summary>
    /// Celui qui prend dans le champ d'un autre de sa communauté sans en avoir le droit : permis si l'on y pense que les champs sont
    /// à tous, pas s'ils sont à qui les a semés.
    /// </summary>
    [Fact]
    public void ChampDUnAutre_PermisSelonLIdee()
    {
        var champs = new Champs();
        var (semeur, autre) = (Adulte(1), Adulte(2));
        var l = champs.NouvelleLignee(Graminee(), 1, Midi, 1, 0);
        var p = Mure(champs, l, 1);
        var c = Comportement(champs);
        c.Reprendre([semeur, autre], Midi);
        champs.IdeeDe(1).Idee = IdeeReserve.Commune;
        Assert.True(c.PermisAuChamp(autre, p, [semeur, autre]));
        champs.IdeeDe(1).Idee = IdeeReserve.ASoi;
        Assert.False(c.PermisAuChamp(autre, p, [semeur, autre]));
        Assert.True(c.PermisAuChamp(semeur, p, [semeur, autre]));
        // Hors de la communauté : jamais sans lien.
        var etranger = Adulte(3, communaute: 2);
        champs.IdeeDe(1).Idee = IdeeReserve.Commune;
        Assert.False(c.PermisAuChamp(etranger, p, [semeur, autre, etranger]));
    }

    /// <summary>Une parcelle qu'on laisse : elle lève, mûrit ou se perd, mais ne reste jamais en terre pour toujours.</summary>
    [Fact]
    public void Parcelles_LeventEtMurissent()
    {
        var champs = new Champs();
        var h = Adulte(1);
        var e = Graminee();
        var l = champs.NouvelleLignee(e, 1, Midi, 1, 0);
        var printemps = 10 * Calendrier.SecondesParAn + 5 * Jour + 12 * Calendrier.SecondesParHeure;
        var places = 0;
        for (var y = 5; y < Carte.Hauteur - 5 && places < 12; y += 7)
        for (var x = 5; x < Carte.Largeur - 5 && places < 12; x += 13)
        {
            if (!Climat.EstPraticable(Carte.BiomeEn(x, y)) || Carte.Plantes[Carte.Indice(x, y)] < 30) continue;
            champs.Semer(x, y, l, 2, l.Tenaces, 1, 1, printemps, 0, 0, true, 0);
            places++;
        }
        Assert.True(places >= 6);
        var c = Comportement(champs);
        var murs = 0;
        for (var j = 0; j < 300; j++)
        {
            var t = printemps + j * Jour;
            c.Reprendre([h], t);
            c.JourneeDesChamps([h], t);
            for (var k = 0; k < champs.Parcelles.Count; k++) champs.Parcelles[k] = champs.Parcelles[k] with { VueLe = t };
            murs = Math.Max(murs, champs.Parcelles.Count(p => p.Etat == EtatParcelle.Mure));
        }
        sortie.WriteLine($"{e.Nom} : {murs} parcelles mûres sur {places} au mieux ; états finaux {string.Join(", ", champs.Parcelles.Select(p => p.Etat))}");
        Assert.True(murs >= 1);
        Assert.DoesNotContain(champs.Parcelles, p => p.Etat == EtatParcelle.EnTerre);
    }

    /// <summary>Dans un monde de la génération 1, personne ne sème : les anciens mondes ne changent pas.</summary>
    [Fact]
    public void Generation1_SansChamps()
    {
        var conf = ConfigurationMonde.ParDefaut(7);
        var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Coeurs = 1 };
        monde.AvancerDe(400 * Jour);
        var (parcelles, lignees, idees) = monde.Champs();
        Assert.Empty(parcelles);
        Assert.Empty(lignees);
        Assert.Empty(idees);
        Assert.Null(monde.Etat.Parcelles);
        Assert.Empty(monde.Troupeaux());
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains, h => Assert.Equal(0, h.PartDesChamps));
    }

    /// <summary>Parcelles, lignées, idées et part des champs dans le repas survivent à l'enregistrement.</summary>
    [Fact]
    public void Champs_EnregistresEtRelus()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-champs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var conf = ConfigurationMonde.Nouvelle(42);
            var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Coeurs = 1 };
            monde.AvancerDe(Jour);
            var etat = monde.Etat;
            var champs = new Champs();
            var l = champs.NouvelleLignee(Graminee(), 3, Midi, 1, 77) with { Tenaces = 0.42, Semence = 5.5, Generations = 9, Saisons = [1, -2, 0, 3], ChoisieLe = 123 };
            champs.Changer(l);
            var p = champs.Semer(10, 20, l, 1.5, 0.4, 1, 3, Midi, 0.2, 0.1, true, 78) with { Etat = EtatParcelle.Pousse, Croissance = 0.5 };
            champs.Changer(p);
            champs.IdeeDe(3).Idee = IdeeReserve.ASoi;
            champs.IdeeDe(3).Vols = 2;
            var humains = etat.Humains!.Select(h => h.Copier()).ToList();
            humains[0].PartDesChamps = 0.37;
            etat = etat with
            {
                Humains = humains, Parcelles = champs.Parcelles.ToList(), Lignees = champs.Lignees.ToList(),
                IdeesChamps = champs.Idees.Select(x => (x.Key, x.Value)).ToList(),
            };
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(p, relu.Parcelles!.Single());
            var rl = relu.Lignees!.Single();
            Assert.Equal((0.42, 5.5, 9, 123L, 77L), (rl.Tenaces, rl.Semence, rl.Generations, rl.ChoisieLe, rl.EvenementId));
            Assert.Equal([1, -2, 0, 3], rl.Saisons);
            var (cid, idee) = relu.IdeesChamps!.Single();
            Assert.Equal((3, IdeeReserve.ASoi, 2.0), (cid, idee.Idee, idee.Vols));
            Assert.Equal(0.37, relu.Humains![0].PartDesChamps, 6);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    /// <summary>Chaque espèce nourricière a ses traits, tirés de la graine du monde : les mêmes à chaque fois.</summary>
    [Fact]
    public void Flore_EspecesNourricieres_Reproductibles()
    {
        var autre = new Flore(2, true);
        var nourricieres = LaFlore.Especes.Where(e => e.Aliment is { Sorte: not SorteAliment.Aucune }).ToList();
        sortie.WriteLine($"{nourricieres.Count} espèces nourricières sur {LaFlore.Especes.Count} : "
                         + string.Join(", ", nourricieres.GroupBy(e => e.Aliment!.Sorte).Select(g => $"{g.Key} {g.Count()}")));
        Assert.True(nourricieres.Count >= 5);
        Assert.Contains(nourricieres, e => e.Aliment!.SeDisperse);
        for (var k = 0; k < LaFlore.Especes.Count; k++) Assert.Equal(LaFlore.Especes[k].Aliment, autre.Especes[k].Aliment);
    }
}
