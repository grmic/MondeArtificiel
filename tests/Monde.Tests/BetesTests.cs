using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.33.1 : faune inventée, petits pris à la chasse, troupeaux, docilité, lait.</summary>
public sealed class BetesTests(ITestOutputHelper sortie)
{
    private static readonly Carte Carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));

    /// <summary>Premier jour du printemps de l'an 10, à midi.</summary>
    private static readonly long Printemps = 10 * Calendrier.SecondesParAn + 12 * Calendrier.SecondesParHeure;

    private const long Jour = Calendrier.SecondesParJour;

    private static Humain Adulte(int id, int x, int y) => new()
    {
        Id = id,
        Nom = $"H{id}",
        PartenaireId = 0,
        Sexe = Sexe.Femme,
        Emplacement = id,
        Naissance = Printemps - 30 * Calendrier.SecondesParAn,
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, new Intelligence(100, 100, 100, 100, 100, 100)),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        X = x,
        Y = y,
    };

    /// <summary>Une case de plaine riche en plantes.</summary>
    private static (int X, int Y) Plaine()
    {
        for (var y = 10; y < Carte.Hauteur - 10; y++)
        for (var x = 10; x < Carte.Largeur - 10; x++)
            if (Carte.BiomeEn(x, y) == Biome.PlaineDeFougeres && Carte.Plantes[Carte.Indice(x, y)] >= 60) return (x, y);
        throw new InvalidOperationException("pas de plaine");
    }

    /// <summary>Un petit brouteur qui réunit tout ce qu'il faut, et une bête coureuse qui panique et ne se reproduit pas captive.</summary>
    private static readonly Bete Brouteur = new(1, Biome.PlaineDeFougeres, 2, 0, RegimeBete.Herbivore, 40, 1, 1.5, true, 0.2, 0.3, true, false, 0.6);

    private static readonly Bete Coureur = new(2, Biome.PlaineDeFougeres, 1, 1, RegimeBete.Herbivore, 60, 1.5, 1, false, 0.2, 0.95, false, false, 0);

    /// <summary>Fait vivre un troupeau d'une seule gardienne pendant des années, et le rend.</summary>
    private static Troupeau Elever(Bete b, double adultes, int annees, ulong graine = 1)
    {
        var (x, y) = Plaine();
        var champs = new Champs();
        var h = Adulte(1, x, y);
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(graine))
        {
            Social = new VieSociale(new Alea(1)) { Carte = Carte, Generation = 2, Champs = champs }, Generation = 2,
            Installations = new Installations(), Champs = champs, Faune = new Faune([Brouteur, Coureur]),
        };
        champs.NouveauTroupeau(b, 0, 1, adultes, 0, b.DociliteSauvage, Printemps, 0);
        var t = champs.Troupeaux[0];
        for (var j = 0; j < annees * Calendrier.JoursParAn; j++)
        {
            var maintenant = Printemps + j * Jour;
            h.Faim = 10;
            c.Reprendre([h], maintenant);
            c.JourneeDesBetes([h], maintenant);
            if (champs.Troupeaux.Count == 0) break;
            t = champs.Troupeaux[0];
        }
        return t;
    }

    /// <summary>
    /// Une bête qui se reproduit captive et ne panique guère : le troupeau croît, et la docilité monte à chaque génération, sans
    /// atteindre la domestication en dix ans.
    /// </summary>
    [Fact]
    public void Troupeau_CroitEtSAdoucit()
    {
        var t = Elever(Brouteur, 2, 10);
        sortie.WriteLine($"après 10 ans : {t.Adultes:F1} adultes, {t.Petits:F1} petits, docilité {Brouteur.DociliteSauvage:P0} → {t.Docilite:P0}, {t.Generations} générations, taille {t.Taille:F3}");
        Assert.True(t.Nombre > 4);
        Assert.InRange(t.Generations, 8, 11);
        Assert.True(t.Docilite > Brouteur.DociliteSauvage + 0.1);
        Assert.True(t.Docilite < 0.6);
        Assert.True(t.Taille < 1);
    }

    /// <summary>Une bête qui panique et ne se reproduit pas captive ne fait jamais de troupeau : elles s'échappent ou meurent.</summary>
    [Fact]
    public void BeteQuiPanique_NeFaitPasDeTroupeau()
    {
        var t = Elever(Coureur, 4, 6);
        sortie.WriteLine($"après 6 ans : {t.Adultes:F1} adultes, {t.Petits:F1} petits, {t.Generations} générations");
        Assert.Equal(0, t.Generations);
        Assert.True(t.Nombre < 2);
    }

    /// <summary>
    /// Diamond (1997) : sur 148 grands mammifères, 14 seulement domestiqués. Peu d'espèces tirées réunissent tout : se reproduire
    /// captive, ne pas charger, ne pas paniquer, suivre un meneur.
    /// </summary>
    [Fact]
    public void Faune_PeuDEspecesDomesticables()
    {
        int grandes = 0, aptes = 0;
        for (ulong g = 1; g <= 40; g++)
        {
            var f = new Faune(g, true);
            Assert.Equal(f.Betes, new Faune(g, true).Betes);
            foreach (var b in f.Betes.Where(b => b.Forme <= 2))
            {
                grandes++;
                if (b.Captive && b.Agressivite < 0.5 && b.Panique < 0.5 && b.Hierarchie) aptes++;
            }
        }
        sortie.WriteLine($"{aptes} brouteurs aptes sur {grandes} ({aptes / (double)grandes:P0})");
        Assert.InRange(aptes / (double)grandes, 0.03, 0.3);
    }

    /// <summary>Bêtes gardées et restes jetés aux rôdeurs survivent à l'enregistrement.</summary>
    [Fact]
    public void Troupeaux_EnregistresEtRelus()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-betes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var conf = ConfigurationMonde.Nouvelle(42);
            var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Coeurs = 1 };
            monde.AvancerDe(Jour);
            var champs = new Champs();
            var t = champs.NouveauTroupeau(monde.Faune.Betes[0], 3, 7, 4.5, 2, 0.31, Printemps, 99) with { Generations = 6, Domestique = true, Abattue = true };
            champs.IdeeDe(3).Rodeurs = 3.5;
            var etat = monde.Etat with { Troupeaux = [t], IdeesChamps = champs.Idees.Select(x => (x.Key, x.Value)).ToList() };
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            Assert.Equal(t, relu.Troupeaux!.Single());
            Assert.Equal(3.5, relu.IdeesChamps!.Single().Idee.Rodeurs);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
