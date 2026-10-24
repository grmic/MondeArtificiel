using Monde.Simulation.Recits;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.20.4 : karma (regard de l'Entité) et croyance en l'invisible.</summary>
public sealed class RegardTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static Humain Humain(int id, double age = 25, double transcendance = 0.5, double psychopathie = 0.5, double croyance = 5,
        int mere = 0, int pere = 0, double generale = 100) => new()
    {
        Id = id, Nom = "H" + id, Sexe = Sexe.Femme, PartenaireId = 0, Emplacement = 1, Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, transcendance, 0.5, 0.5, Intelligence.Moyenne with { Generale = generale })
        {
            Moral = TraitsMoraux.Moyens with { Psychopathie = psychopathie },
        },
        Biologie = Biologie.ParDefaut(Sexe.Femme), Reserves = 50, Sante = 100, MereId = mere, PereId = pere,
        X = Carte.Emplacements[0].X, Y = Carte.Emplacements[0].Y, Croyance = croyance,
    };

    [Fact]
    public void Juger_UnMeurtrePesePlusQuUnVol_LEnfantEstMoinsCoupable_LePsychopatheGagneMoins()
    {
        var adulte = Humain(1);
        Assert.Equal(Regard.Meurtre, Regard.Juger(adulte, Regard.Meurtre, Maintenant));
        Assert.Equal(Regard.Vol, Regard.Juger(adulte, Regard.Vol, Maintenant));
        Assert.Equal(Regard.Meurtre + Regard.Vol, adulte.Karma);

        var enfant = Humain(2, age: 7);
        var d = Regard.Juger(enfant, Regard.Vol, Maintenant);
        Assert.InRange(d, Regard.Vol, -0.01);
        Assert.True(d > Regard.Vol, $"enfant : {d}");
        Assert.Equal(Regard.Veiller, Regard.Juger(enfant, Regard.Veiller, Maintenant));

        var froid = Humain(3, psychopathie: 0.999);
        Assert.True(Psyche.EstPsychopathe(froid, Maintenant));
        Assert.Equal(Regard.Veiller / 2, Regard.Juger(froid, Regard.Veiller, Maintenant));
        Assert.Equal(Regard.Vol, Regard.Juger(froid, Regard.Vol, Maintenant));
        Assert.Equal(0, Regard.Juger(null, Regard.Meurtre, Maintenant));
    }

    [Fact]
    public void PetitsGestes_EnversUneMemePersonne_NeComptentQuUneFoisParSaison()
    {
        var a = Humain(1);
        var b = Humain(2);
        var c = Humain(3);
        a.Liens.Add(new Lien(2, 10, 30, Maintenant));
        Assert.Equal(Regard.Partage, Regard.Juger(a, Regard.Partage, Maintenant, b));
        Assert.Equal(0, Regard.Juger(a, Regard.Veiller, Maintenant + 30 * Jour, b));
        Assert.Equal(Regard.Partage, Regard.Juger(a, Regard.Partage, Maintenant + 30 * Jour, c)); // une autre personne
        Assert.Equal(Regard.Recueillir, Regard.Juger(a, Regard.Recueillir, Maintenant + 31 * Jour, b)); // un grand geste compte toujours
        Assert.Equal(Regard.Veiller, Regard.Juger(a, Regard.Veiller, Maintenant + 91 * Jour, b));
        Assert.Equal(Regard.Partage * 2 + Regard.Recueillir + Regard.Veiller, a.Karma);
    }

    [Fact]
    public void Infidelite_PeseSelonLUsage_EtRienSansCompagnon()
    {
        var a = Humain(1);
        var compagnon = Humain(2);
        var b = Humain(3);
        a.PartenaireId = 2;
        List<Humain> tous = [a, compagnon, b];
        Assert.Equal(Regard.Infidelite, Regard.PointsInfidelite(a, b, tous, tolerance: 0));
        Assert.Equal(0, Regard.PointsInfidelite(a, b, tous, tolerance: 1));
        Assert.Equal(0, Regard.PointsInfidelite(a, compagnon, tous, tolerance: 0));
        Assert.Equal(0, Regard.PointsInfidelite(b, a, tous, tolerance: 0));
    }

    [Fact]
    public void CroyanceInitiale_EntreZeroEtDix_PlusHauteChezLesEspritsTranscendants()
    {
        var bas = Enumerable.Range(1, 300).Select(i => Croyances.Initiale(i, 0.1)).ToList();
        var haut = Enumerable.Range(1, 300).Select(i => Croyances.Initiale(i, 0.9)).ToList();
        Assert.All(bas.Concat(haut), c => Assert.InRange(c, 0, 10));
        Assert.True(haut.Average() > bas.Average() + 3, $"{bas.Average():0.0} → {haut.Average():0.0}");
        Assert.Equal(Croyances.Initiale(7, 0.5), Croyances.Initiale(7, 0.5));
    }

    [Fact]
    public void Croyance_LEnfantPrendCelleDeSesParents_EtElleRetombeSansSigne()
    {
        var mere = Humain(1, croyance: 80);
        var enfant = Humain(2, age: 6, croyance: 2, mere: 1);
        var seul = Humain(3, croyance: 60, transcendance: 0.2);
        seul.CroyanceSigneLe = Maintenant - 3 * An;
        mere.CroyanceSigneLe = enfant.CroyanceSigneLe = Maintenant;
        List<Humain> tous = [mere, enfant, seul];
        var vide = new Dictionary<int, double>();
        for (var j = 0; j < 360; j++)
            foreach (var h in tous) Croyances.Journee(h, tous, vide, Maintenant + j * Jour);
        Assert.InRange(enfant.Croyance, 55, 80);
        Assert.InRange(seul.Croyance, 40, 58); // demi-vie de quelques années
        Assert.True(seul.Croyance > Croyances.Repos(seul));
    }

    [Fact]
    public void Croyance_UneHausseCompteMoinsQuandOnCroitDeja_EtUnChangementDUneDizaineEstNoteAvecSaCause()
    {
        var h = Humain(1, croyance: 5);
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1)) { Social = new VieSociale(new Alea(1)) };
        c.Croire([h], Maintenant);
        Assert.DoesNotContain(c.Journal, e => e.Type == TypeEvenement.Croyance);
        Croyances.Ajouter(h, 20, Recit.Seul("cro.raconte"), 77, Maintenant, signe: true);
        Assert.Equal(5 + 20 * 0.95, h.Croyance, 6);
        Assert.Equal(Maintenant, h.CroyanceSigneLe);
        c.Croire([h], Maintenant);
        var e = Assert.Single(c.Journal, x => x.Type == TypeEvenement.Croyance);
        Assert.Equal(77, e.CauseId);
        Assert.Contains("croit davantage en l'invisible (24/100) : on lui a raconté le prodige", e.Texte);

        var croyant = Humain(2, croyance: 90);
        Assert.True(Croyances.Ajouter(croyant, 20, Recit.Seul("cro.raconte"), 0, Maintenant) < 3);
    }

    [Fact]
    public void Entite_LaFoiSuitLaCroyance_EtUneVoixCrueLaFaitGrandir()
    {
        int crusCroyants = 0, crusIncroyants = 0;
        double gain = 0;
        for (var g = 0; g < 200; g++)
        {
            var croyant = Humain(1, croyance: 90);
            var incroyant = Humain(2, croyance: 0, generale: 130);
            var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea((ulong)g + 1)) { Social = new VieSociale(new Alea((ulong)g + 1)) };
            c.Parler(croyant, Parole.NAiePasPeur, ModeParole.Voix, null, 0, [croyant], Maintenant);
            c.Parler(incroyant, Parole.NAiePasPeur, ModeParole.Voix, null, 0, [incroyant], Maintenant);
            if (croyant.Impulsions[^1].Crue) crusCroyants++;
            if (incroyant.Impulsions[^1].Crue)
            {
                crusIncroyants++;
                gain += incroyant.Croyance;
            }
        }
        Assert.True(crusCroyants > crusIncroyants + 60, $"{crusCroyants} contre {crusIncroyants}");
        Assert.True(crusIncroyants > 0 && gain / crusIncroyants >= 10, $"gain moyen {gain / Math.Max(1, crusIncroyants):0.0}");
    }

    [Fact]
    public void DeuxAns_ChaqueChangementDeKarmaEstPorteParUnActe_EtToutEstRelu()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-regard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            var depart = monde.Etat.Humains!.ToDictionary(h => h.Id, h => h.Croyance);
            Assert.All(depart.Values, c => Assert.InRange(c, 0, 10));
            monde.AvancerDe(2 * An);
            var etat = monde.Etat;
            var journal = etat.JournalNonEnregistre!;
            Assert.Contains(journal, e => e.Karma > 0);
            Assert.Contains(journal, e => e.Type == TypeEvenement.Partage && e.Karma > 0);
            // Le karma de chacun est exactement la somme de ce que lui ont valu ses actes notés au journal.
            var sommes = new Dictionary<int, double>();
            foreach (var e in journal)
            {
                if (e.Karma != 0) sommes[e.HumainId] = sommes.GetValueOrDefault(e.HumainId) + e.Karma;
                if (e.KarmaAutre != 0) sommes[e.AutreId] = sommes.GetValueOrDefault(e.AutreId) + e.KarmaAutre;
            }
            foreach (var h in etat.Humains!) Assert.Equal(sommes.GetValueOrDefault(h.Id), h.Karma, 1);

            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            for (var i = 0; i < etat.Humains!.Count; i++)
            {
                Assert.Equal(etat.Humains[i].Karma, relu.Humains![i].Karma);
                Assert.Equal(etat.Humains[i].Croyance, relu.Humains[i].Croyance);
                Assert.Equal(etat.Humains[i].CroyanceSigneLe, relu.Humains[i].CroyanceSigneLe);
            }
            var depot = new DepotMonde(fichier);
            var avecKarma = journal.Where(e => e.Karma != 0 && e.Type != TypeEvenement.Partage).MaxBy(e => e.Id)!;
            var lu = depot.LireEvenement(avecKarma.Id)!;
            Assert.Equal((avecKarma.Karma, avecKarma.KarmaAutre), (lu.Karma, lu.KarmaAutre));

            // Un monde de la version 22 se relit : sans karma, et la croyance tirée du numéro.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion22);
            var v22 = new DepotMonde(fichier).Charger()!;
            Assert.All(v22.Humains!, h => Assert.Equal(0, h.Karma));
            Assert.All(v22.Humains!, h => Assert.Equal(Croyances.Initiale(h.Id, h.Personnalite.Transcendance), h.Croyance));
            new DepotMonde(fichier).Enregistrer(v22, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
