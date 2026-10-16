using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.20.6 : arts et musique (rythme, chant, danse, ocre, peinture sur roche, lamentations, fêtes, style).</summary>
public sealed class ArtsTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static (int X, int Y) Case(Biome biome, Func<int, int, bool>? condition = null)
    {
        for (var y = 0; y < Carte.Hauteur; y++)
        for (var x = 0; x < Carte.Largeur; x++)
            if (Carte.BiomeEn(x, y) == biome && (condition is null || condition(x, y)))
                return (x, y);
        throw new InvalidOperationException("biome absent");
    }

    private static Humain Humain(int id, double art = 100, double extraversion = 0.5, double age = 25, (int X, int Y)? lieu = null,
        int communaute = 0)
    {
        var (x, y) = lieu ?? (Carte.Emplacements[0].X, Carte.Emplacements[0].Y);
        return new Humain
        {
            Id = id, Nom = "H" + id, Sexe = id % 2 == 0 ? Sexe.Homme : Sexe.Femme, PartenaireId = 0, Emplacement = 1,
            Naissance = Maintenant - (long)(age * An),
            Personnalite = new Personnalite(0.6, 0.5, extraversion, 0.5, 0.5, 0.5, 0.5, 0.5, Intelligence.Moyenne with { Artistique = art }),
            Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Homme : Sexe.Femme), Reserves = 50, Sante = 100, CommunauteId = communaute,
            X = x, Y = y, Humeur = 40,
        };
    }

    private static Comportement Comportement(ulong graine = 1) =>
        new(Carte, new Ecosysteme(Carte), new Alea(graine)) { Social = new VieSociale(new Alea(graine)) };

    private static Savoir Sait(Geste g, Effet e, Matiere a = Matiere.Aucune, Matiere b = Matiere.Aucune) =>
        new(g, a, b, e, 0, Symptome.Aucun, 10, 9, Maintenant, Maintenant, OrigineSavoir.Essai, 0, 0);

    [Fact]
    public void Rythme_DeuxBoisFrappes_LOreilleFaitEntendreLaCadence()
    {
        var alea = new Alea(3);
        int Rythmes(double art)
        {
            var n = 0;
            for (var i = 0; i < 40000; i++)
            {
                var r = Physique.Tenter(Geste.Frapper, Matiere.Branche, Matiere.BoisSec, new ContexteGeste(false, false, 0.5, Biome.PlaineDeFougeres, 0.5, 0.5, Art: art), alea);
                if (r.Effet == Effet.Rythme) n++;
            }
            return n;
        }
        var sourd = Rythmes(0);
        var doue = Rythmes(0.9);
        Assert.InRange(sourd, 15, 80); // ~0,1 %
        Assert.True(doue > 8 * sourd, $"{doue} contre {sourd}");
        // Ce qui ne sonne pas (une peau, des fibres) ne fait pas de rythme.
        for (var i = 0; i < 500; i++)
            Assert.NotEqual(Effet.Rythme, Physique.Tenter(Geste.Frapper, Matiere.Peau, Matiere.Fibres, new ContexteGeste(false, false, 0.5, Biome.PlaineDeFougeres, 0.5, 0.5, Art: 1), alea).Effet);
    }

    [Fact]
    public void Ocre_SeCouvrirDeTerreRouge_Pare_EtSeTrouveDansLaRoche()
    {
        var r = Physique.Tenter(Geste.SeCouvrir, Matiere.Ocre, Matiere.Aucune, new ContexteGeste(false, false, 0.5, Biome.Montagne, 0.5, 0.5), new Alea(1));
        Assert.Equal(Effet.Ocre, r.Effet);
        var (x, y) = Case(Biome.Montagne);
        Assert.Contains(Matiere.Ocre, Physique.Disponibles(Carte, x, y, false, false));
        var (px, py) = Case(Biome.PlaineDeFougeres, (x, y) => !Physique.Ocreuse(Carte, x, y));
        Assert.DoesNotContain(Matiere.Ocre, Physique.Disponibles(Carte, px, py, false, false));
        var (ox, oy) = Case(Biome.PlaineDeFougeres, (x, y) => Physique.Ocreuse(Carte, x, y));
        Assert.Contains(Matiere.Ocre, Physique.Disponibles(Carte, ox, oy, false, false));
    }

    [Fact]
    public void Veillee_LeChantReleveLHumeur_RapprocheCeuxQuiDansent_EtSApprend()
    {
        var c = Comportement(5);
        var chanteur = Humain(1, art: 140, extraversion: 0.9);
        chanteur.Savoirs.Add(Sait(Geste.Chanter, Effet.Chant));
        List<Humain> tous = [chanteur];
        for (var i = 2; i <= 8; i++)
        {
            var o = Humain(i, art: 130, extraversion: 0.8, age: i < 5 ? 10 : 25);
            if (i >= 5) o.Savoirs.Add(Sait(Geste.Danser, Effet.Danse));
            o.Chagrin = 30;
            tous.Add(o);
        }
        var jour = 0;
        while (tous[1].Humeur <= 40 && jour < 200) c.Arts(tous, Maintenant + jour++ * Jour);
        Assert.True(tous[1].Humeur > 40, "personne n'a chanté en 200 jours");
        Assert.True(tous[1].Chagrin < 30);
        // Ceux qui ont dansé ensemble se sentent plus proches.
        Assert.True(tous[5].AffectionPour(tous[6].Id) > 0 || tous[5].AffectionPour(chanteur.Id) > 0);
        // À force d'écouter, on apprend à chanter ; à force de voir danser, à danser.
        for (; jour < 400; jour++)
        {
            foreach (var h in tous) (h.Humeur, h.Fatigue) = (40, 0);
            c.Arts(tous, Maintenant + jour * Jour);
        }
        Assert.Contains(tous.Skip(1), h => h.Savoirs.Exists(s => s.Effet == Effet.Chant && s.Origine == OrigineSavoir.Imitation && s.MaitreId == 1));
        Assert.Contains(tous.Skip(1).Take(3), h => h.Savoirs.Exists(s => s.Effet == Effet.Danse));
    }

    [Fact]
    public void Lamentation_ChanterSaPeine_UneFoisParMort_CauseeParLeDeuil()
    {
        var c = Comportement(2);
        var mort = Humain(1);
        mort.MortLe = Maintenant - Jour;
        var chanteur = Humain(2, art: 140, extraversion: 0.8);
        chanteur.Savoirs.Add(Sait(Geste.Chanter, Effet.Chant));
        (chanteur.DeuilDeId, chanteur.DeuilEvenementId, chanteur.Chagrin) = (1, 77, 80);
        var proche = Humain(3);
        (proche.DeuilDeId, proche.DeuilEvenementId, proche.Chagrin) = (1, 77, 60);
        List<Humain> tous = [mort, chanteur, proche];
        for (var j = 0; j < 100; j++)
        {
            chanteur.Chagrin = Math.Max(chanteur.Chagrin, 60);
            c.Arts(tous, Maintenant + j * Jour);
        }
        var lamentations = c.Journal.Where(e => e.Type == TypeEvenement.Art && e.Texte.Contains("chante sa peine pour H1")).ToList();
        var l = Assert.Single(lamentations);
        Assert.Equal(77, l.CauseId);
        Assert.Contains("H3 l'écoute", l.Texte);

        // Deux qui pleurent le même mort et savent chanter chantent ensemble : une seule lamentation.
        var c2 = Comportement(4);
        var a = Humain(2, art: 140, extraversion: 0.9);
        var b = Humain(3, art: 140, extraversion: 0.9);
        foreach (var x in new[] { a, b })
        {
            x.Savoirs.Add(Sait(Geste.Chanter, Effet.Chant));
            (x.DeuilDeId, x.DeuilEvenementId, x.Chagrin) = (1, 77, 80);
        }
        for (var j = 0; j < 100; j++) c2.Arts([mort, a, b], Maintenant + j * Jour);
        var choeur = Assert.Single(c2.Journal, e => e.Type == TypeEvenement.Art && e.Texte.Contains("chante sa peine"));
        Assert.Contains(", avec H", choeur.Texte);
        Assert.Contains(chanteur.Memoire.Jardin, x => x.Cle == "lamentation:1");
    }

    [Fact]
    public void Peinture_SurLaRoche_ResteLaEtSeContemple()
    {
        var c = Comportement(9);
        var roche = Case(Biome.Montagne);
        var peintre = Humain(1, art: 145, lieu: roche);
        peintre.Savoirs.Add(Sait(Geste.SeCouvrir, Effet.Ocre, Matiere.Ocre));
        peintre.Croyance = 80;
        var passant = Humain(2, art: 120, lieu: roche);
        List<Humain> tous = [peintre, passant];
        var j = 0;
        while (c.Installations.Oeuvres.Count == 0 && j < 20000) c.Arts(tous, Maintenant + j++ * Jour);
        var o = Assert.Single(c.Installations.Oeuvres);
        Assert.Equal((roche.X, roche.Y, 1), (o.X, o.Y, o.AuteurId));
        var premiere = c.Journal.Single(e => e.Id == o.EvenementId);
        Assert.Contains("Première fois au monde", premiere.Texte);
        Assert.True(premiere.Significatif);
        Assert.Contains(peintre.Savoirs, s => s.Effet == Effet.Peinture);
        for (var k = 0; k < 30; k++) c.Arts(tous, Maintenant + j++ * Jour);
        Assert.Contains(passant.Memoire.Jardin, x => x.Cle.StartsWith("oeuvre:", StringComparison.Ordinal));
        // Hors de la roche, on ne peint pas.
        var c2 = Comportement(9);
        var ailleurs = Humain(1, art: 145, lieu: Case(Biome.PlaineDeFougeres, (x, y) => Physique.Ocreuse(Carte, x, y) && !Physique.Paroi(Carte, x, y)));
        ailleurs.Savoirs.Add(Sait(Geste.SeCouvrir, Effet.Ocre, Matiere.Ocre));
        for (var k = 0; k < 3000; k++) c2.Arts([ailleurs], Maintenant + k * Jour);
        Assert.Empty(c2.Installations.Oeuvres);
    }

    [Fact]
    public void Style_NaitDuCaractereDesMembres()
    {
        Assert.Null(Arts.Style([Humain(1)], []));
        var vifs = Enumerable.Range(1, 4).Select(i => Humain(i, extraversion: 0.8)).ToList();
        vifs[0].Savoirs.Add(Sait(Geste.Chanter, Effet.Chant));
        vifs[1].Savoirs.Add(Sait(Geste.Danser, Effet.Danse));
        vifs[2].Savoirs.Add(Sait(Geste.Frapper, Effet.Rythme, Matiere.Branche, Matiere.BoisSec));
        var style = Arts.Style(vifs, [new Oeuvre(1, 0, 0, 1, 0, MotifPeint.Main, "une main", 0, 1)])!;
        Assert.Contains("Chants vifs", style);
        Assert.Contains("rythmes frappés sur le bois", style);
        Assert.Contains("danses en cercle", style);
        Assert.Contains("des mains", style);
        var lents = Enumerable.Range(1, 4).Select(i => Humain(i, extraversion: 0.2)).ToList();
        lents[0].Savoirs.Add(Sait(Geste.Chanter, Effet.Chant));
        Assert.Contains("Chants lents", Arts.Style(lents, [])!);
    }

    [Fact]
    public void Oeuvres_SeRelisent_EtUnMondeV24SeCharge()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-arts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(Jour);
            var etat = monde.Etat with { Oeuvres = [new Oeuvre(3, 10, 12, 1, 99, MotifPeint.Entite, "une grande forme", 5, 2)] };
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(etat.Oeuvres, relu.Oeuvres);
            // La suite de la numérotation repart après la dernière œuvre.
            Assert.Equal(4, new Installations(oeuvres: relu.Oeuvres).Peindre(0, 0, 1, 0, MotifPeint.Main, "", 0, 0).Id);

            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion24);
            var v24 = new DepotMonde(fichier).Charger()!;
            Assert.Null(v24.Oeuvres);
            new DepotMonde(fichier).Enregistrer(v24, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
