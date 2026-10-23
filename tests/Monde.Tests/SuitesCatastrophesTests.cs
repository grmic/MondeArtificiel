using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Effets durables des catastrophes : terres dévastées qui reverdissent lentement, morts des suites, hiver d'impact.</summary>
public sealed class SuitesCatastrophesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Heure = Calendrier.SecondesParHeure;
    private const long Jour = Calendrier.SecondesParJour;

    private static (int X, int Y) Centre()
    {
        for (var r = 0; r < Carte.Largeur; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    int x = Carte.Largeur / 2 + dx, y = Carte.Hauteur / 2 + dy;
                    if (x >= 0 && y >= 0 && x < Carte.Largeur && y < Carte.Hauteur && Carte.BiomeEn(x, y) > Biome.Riviere) return (x, y);
                }
            }
        }
        throw new InvalidOperationException("Pas de terre.");
    }

    [Fact]
    public void Guerison_PlusLenteDansLesConiferesQueDansLesFougeres_EtBienPlusLenteSurUneTerreSterilisee()
    {
        Assert.True(Ecosysteme.AnneesDeRetour(Biome.ForetDeConiferes) > Ecosysteme.AnneesDeRetour(Biome.PlaineDeFougeres));
        var conifere = Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).First(i => Carte.BiomeEn(i % Carte.Largeur, i / Carte.Largeur) == Biome.ForetDeConiferes);
        var fougere = Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).First(i => Carte.BiomeEn(i % Carte.Largeur, i / Carte.Largeur) == Biome.PlaineDeFougeres);
        var degats = new ushort[Carte.Largeur * Carte.Hauteur];
        degats[conifere] = 5_000;
        degats[fougere] = 5_000;
        var eco = new Ecosysteme(Carte, degats: degats);
        Assert.True(eco.GuerisonParJour(conifere) < eco.GuerisonParJour(fougere));
        var brule = eco.GuerisonParJour(conifere);
        eco.Degats[conifere] = 10_000;
        Assert.True(eco.GuerisonParJour(conifere) < brule * 0.2, "une terre stérilisée devrait se refaire bien plus lentement");

        // Une terre de fougères à moitié brûlée a reverdi en moins de deux ans, pas en quelques semaines.
        var jours = 0;
        while (eco.Degats[fougere] > 0 && jours < 2_000)
        {
            eco.Degats[fougere] = (ushort)Math.Max(0, eco.Degats[fougere] - Math.Max(1, (int)Math.Round(eco.GuerisonParJour(fougere))));
            jours++;
        }
        Assert.InRange(jours, 180, 2 * Calendrier.JoursParAn);
    }

    [Fact]
    public void GrandImpact_DevasteLesTerresPourLongtemps_PlongeLeMondeDansLaLongueNuit_EtLeBilanSurvitALaSauvegarde()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-suites-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (x, y) = Centre();
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            var id = monde.Declencher(TypeCatastrophe.Impact, x, y, 100_000);
            Assert.True(id > 0);
            monde.AvancerDe(Jour + Heure);

            var catastrophes = monde.CatastrophesEnMemoire();
            var impact = catastrophes.Single(c => c.Type == TypeCatastrophe.Impact);
            Assert.False(impact.Active);
            // Une terre soufflée et brûlée par 100 000 Mt (le cratère de Chicxulub en valait environ 1 000 fois plus) met des années à revivre.
            Assert.True(impact.SuitesJusqua > monde.Secondes + Calendrier.SecondesParAn, "les suites devraient durer plus d'un an");
            Assert.True(impact.EnSuites(monde.Secondes));
            var hiver = catastrophes.Single(c => c.Type == TypeCatastrophe.HiverDImpact);
            Assert.True(hiver.Active);
            Assert.InRange(monde.AnomalieTemperature, -6, -1);

            // L'impact fini reste en mémoire tant que ses suites durent, même une fois enregistré.
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(monde.Etat);
            monde.MarquerJournalEnregistre(0, 0, new HashSet<long> { impact.Id });
            Assert.Contains(monde.CatastrophesEnMemoire(), c => c.Id == impact.Id);

            // Rechargé, le monde retrouve l'impact et ses suites (et l'hiver), avec le même bilan.
            var relu = depot.Charger()!;
            var impactRelu = relu.Catastrophes!.Single(c => c.Id == impact.Id);
            Assert.Equal((impact.SuitesJusqua, impact.MortsSuites, impact.Morts), (impactRelu.SuitesJusqua, impactRelu.MortsSuites, impactRelu.Morts));
            Assert.Contains(relu.Catastrophes!, c => c.Type == TypeCatastrophe.HiverDImpact && c.Active);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void PetitImpact_PasDHiver()
    {
        var (x, y) = Centre();
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        Assert.True(monde.Declencher(TypeCatastrophe.Impact, x, y, 15) > 0);
        monde.AvancerDe(Jour);
        Assert.DoesNotContain(monde.CatastrophesEnMemoire(), c => c.Type == TypeCatastrophe.HiverDImpact);
    }

    [Fact]
    public void MortDeFaimDansLesTerresDevastees_CompteeDansLesSuites_EtCiteeAvecSaCause()
    {
        var (x, y) = Centre();
        var eco = new Ecosysteme(Carte);
        var catastrophes = new Catastrophes(Carte, eco, new Alea(7));
        var humains = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat.Humains!.ToList();
        // Loin de tous, pour que personne ne soit frappé : seule la nature est touchée.
        foreach (var h in humains) (h.X, h.Y) = (0, 0);
        var id = catastrophes.Declencher(TypeCatastrophe.Impact, x, y, 50, humains, 0, Origine.Naturelle);
        Assert.True(id > 0);
        for (long t = Heure; t <= 2 * Heure; t += Heure) catastrophes.Heure(humains, t);
        catastrophes.Jour(humains, Jour);
        var impact = catastrophes.EnMemoire.Single(c => c.Id == id);
        Assert.True(impact.EnSuites(Jour));

        var victime = humains[0];
        (victime.X, victime.Y) = (x, y);
        var (cause, contexte) = catastrophes.Attribuer(victime, CauseDeMort.Faim, Jour + Heure);
        Assert.Equal(id, cause);
        Assert.Contains("terres dévastées", contexte!.Texte);
        Assert.Equal(1, catastrophes.EnMemoire.Single(c => c.Id == id).MortsSuites);

        // Ailleurs, ou d'une autre cause, la mort ne lui doit rien.
        var autre = humains[1];
        (autre.X, autre.Y) = (0, 0);
        Assert.Equal(0, catastrophes.Attribuer(autre, CauseDeMort.Faim, Jour + Heure).Id);
        (autre.X, autre.Y) = (x, y);
        Assert.Equal(0, catastrophes.Attribuer(autre, CauseDeMort.Vieillesse, Jour + Heure).Id);
    }

    [Fact]
    public void Migration16_LesCatastrophesAnciennesNOntPasDeSuites()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-v16-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (x, y) = Centre();
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.Declencher(TypeCatastrophe.Seisme, x, y, 5.5);
            monde.AvancerDe(3 * Heure);
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(monde.Etat);
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion16);

            var depot = new DepotMonde(fichier);
            Assert.NotNull(depot.Charger());
            var seisme = Assert.Single(depot.LireCatastrophes(), c => c.Type == TypeCatastrophe.Seisme);
            Assert.Equal((0L, 0), (seisme.SuitesJusqua, seisme.MortsSuites));
            Assert.DoesNotContain(depot.LireReperesCatastrophes(), r => r.Origine != Origine.Entite);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
