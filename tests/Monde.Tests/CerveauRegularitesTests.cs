using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>Le cerveau collectif et les régularités (0.34.1, génération 2).</summary>
public sealed class CerveauRegularitesTests(ITestOutputHelper sortie)
{
    private static readonly Carte Carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));

    private static readonly long Debut = 10 * Calendrier.SecondesParAn + 12 * Calendrier.SecondesParHeure;

    private static Humain Adulte(int id, int x, int y, double ouverture = 0.8) => new()
    {
        Id = id,
        Nom = $"H{id}",
        PartenaireId = 0,
        Sexe = id % 2 == 0 ? Sexe.Femme : Sexe.Homme,
        Emplacement = id,
        Naissance = Debut - 25 * Calendrier.SecondesParAn,
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, 0.5, 0.5, 0.8, 0.5, 0.5, new Intelligence(120, 120, 120, 120, 120, 120)),
        Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Femme : Sexe.Homme),
        X = x,
        Y = y,
    };

    private static (int X, int Y) Foret()
    {
        for (var y = 10; y < Carte.Hauteur - 10; y++)
        for (var x = 10; x < Carte.Largeur - 10; x++)
            if (Carte.BiomeEn(x, y) is Biome.ForetDeFeuillus or Biome.ForetDeConiferes) return (x, y);
        throw new InvalidOperationException("pas de forêt");
    }

    private static Comportement Monde(int generation = 2, int graine = 3) => new(Carte, new Ecosysteme(Carte), new Alea((ulong)graine))
    {
        Social = new VieSociale(new Alea(1)) { Carte = Carte, Generation = generation }, Generation = generation, Installations = new Installations(),
    };

    private static Savoir S(Geste g, Matiere a, Matiere b, Effet e, string p = "") => new(g, a, b, e, 0, Symptome.Aucun, 5, 5, 0, 0, OrigineSavoir.Essai, 0, 0, p);

    /// <summary>
    /// L'un sait tordre une corde et tailler une pointe, l'autre a remarqué que la branche verte revient et sait découper : ensemble, ils
    /// trouvent des choses qu'aucun n'aurait trouvées avec ses seuls savoirs. L'idée cite les deux, et tous deux la retiennent.
    /// </summary>
    [Fact]
    public void IdeeNeeADeux_CiteLesDeuxEtLesDeuxLaRetiennent()
    {
        var (x, y) = Foret();
        var h = Adulte(1, x, y);
        var o = Adulte(2, x, y);
        h.Objets.Add(new Objet(Matiere.Eclat, 0.8, 1));
        h.Objets.Add(new Objet(Matiere.Corde, 0.8, 1));
        h.Savoirs.Add(S(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde));
        h.Savoirs.Add(S(Geste.Gratter, Matiere.Branche, Matiere.Eclat, Effet.Pointe));
        o.Savoirs.Add(S(Geste.Faconner, Matiere.Branche, Matiere.Aucune, Effet.Remarque, "R|ressort"));
        o.Savoirs.Add(S(Geste.Gratter, Matiere.Carcasse, Matiere.Eclat, Effet.Decoupe));
        var c = Monde();
        c.EspritsFertiles = 30;
        var tous = new List<Humain> { h, o };
        for (var k = 0; k < 20000 && !c.Installations.Decouvertes.Exists(d => d.CoauteurId == 2); k++)
        {
            h.Faim = h.Soif = o.Faim = o.Soif = 0;
            c.Reprendre(tous, Debut + k * 600L);
            c.IdeeAPlusieursPourTest(h, tous, Debut + k * 600L, 10);
        }
        foreach (var d in c.Installations.Decouvertes) sortie.WriteLine($"{d.Pieces} ← {d.Parents} (avec {d.CoauteurId})");
        var a2 = c.Installations.Decouvertes.Find(d => d.CoauteurId == 2);
        Assert.NotNull(a2);
        Assert.Equal(1, a2.HumainId);
        Assert.Contains(o.Savoirs, s => s.Pieces == a2.Pieces);
        Assert.Contains(h.Savoirs, s => s.Pieces == a2.Pieces);
        // Au moins un parent vient de l'autre.
        var deO = o.Savoirs.Where(s => s.Effet != Effet.Facture).Select(s => Comportement.CleDeSavoir(s.Effet, s.Pieces)).ToHashSet();
        Assert.Contains(a2.Parents.Split(';'), deO.Contains);
        Assert.Contains(c.Journal, j => j.HumainId == 1 && j.AutreId == 2 && j.Texte.Contains("H1 et H2"));
    }

    /// <summary>Seul sur sa case, ou avec quelqu'un qui ne sait rien de plus, pas d'idée à deux ; et rien en génération 1.</summary>
    [Fact]
    public void PasDIdeeADeux_SansAutreSavoirOuEnGeneration1()
    {
        var (x, y) = Foret();
        foreach (var generation in new[] { 1, 2 })
        {
            var h = Adulte(1, x, y);
            var o = Adulte(2, x, y);
            h.Savoirs.Add(S(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde));
            o.Savoirs.Add(S(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde));
            if (generation == 1) o.Savoirs.Add(S(Geste.Gratter, Matiere.Carcasse, Matiere.Eclat, Effet.Decoupe));
            var c = Monde(generation);
            c.EspritsFertiles = 30;
            var tous = new List<Humain> { h, o };
            for (var k = 0; k < 2000; k++)
            {
                c.Reprendre(tous, Debut + k * 600L);
                Assert.False(c.IdeeAPlusieursPourTest(h, tous, Debut + k * 600L, 10));
            }
        }
    }

    [Fact]
    public void Regularite_SEcritEtSeLit()
    {
        var p = Comportement.EcrireRegularite(Serie.Lune, 29.53);
        Assert.Equal("G|1|295", p);
        Assert.Equal((Serie.Lune, 29.5), Comportement.LireRegularite(p));
        Assert.Null(Comportement.LireRegularite("G|9|3"));
        Assert.Equal("G|1", Lois.Structure(p));
        Assert.Equal(Comportement.CleDeSavoir(Effet.Regularite, "G|1|310"), Comportement.CleDeSavoir(Effet.Regularite, p));
    }

    /// <summary>
    /// Un camp de douze adultes attentifs, trois ans de nuits : quelqu'un finit par voir que la lune revient pleine, l'estime à peu
    /// près, et le dit aux autres. En génération 1, rien.
    /// </summary>
    [Fact]
    public void Lune_RemarqueeConfirmeeEtDite()
    {
        var (x, y) = Foret();
        foreach (var generation in new[] { 1, 2 })
        {
            var tous = Enumerable.Range(1, 12).Select(i => Adulte(i, x, y)).ToList();
            var c = Monde(generation);
            c.EspritsFertiles = 10;
            for (var j = 0; j < 3 * Calendrier.JoursParAn; j++) c.RegularitesPourTest(tous, Debut + j * Calendrier.SecondesParJour);
            var lunes = tous.SelectMany(h => h.Savoirs).Where(s => Comportement.LireRegularite(s.Pieces) is { Serie: Serie.Lune }).ToList();
            if (generation == 1)
            {
                Assert.Empty(lunes);
                continue;
            }
            foreach (var s in lunes) sortie.WriteLine($"{s.Pieces} {s.Origine} {s.Succes}/{s.Essais}");
            Assert.True(lunes.Count >= 2, $"{lunes.Count}");
            Assert.Contains(lunes, s => s.Origine == OrigineSavoir.Imitation);
            Assert.All(lunes, s => Assert.InRange(Comportement.LireRegularite(s.Pieces)!.Value.Jours, 15, 60));
            Assert.Contains(c.Installations.Decouvertes, d => d.Effet == Effet.Regularite && d.Pieces.StartsWith("G|1|"));
        }
    }

    /// <summary>
    /// Qui sait compter et croyait la lune revenir toutes les quarante nuits corrige son estimation en quelques mois et la tient à
    /// une nuit ou deux près ; sans compter, l'estimation reste plus grossière.
    /// </summary>
    [Fact]
    public void Compter_RendLaLunePlusJuste()
    {
        var (x, y) = Foret();
        double Erreur(bool compte)
        {
            var tous = Enumerable.Range(1, 8).Select(i => Adulte(i, x + i, y, 0)).ToList();
            foreach (var h in tous)
            {
                h.Savoirs.Add(new Savoir(Geste.Observer, Matiere.Aucune, Matiere.Aucune, Effet.Regularite, 0, Symptome.Aucun, 4, 4, Debut, Debut,
                    OrigineSavoir.Observation, 0, 0, Comportement.EcrireRegularite(Serie.Lune, 40)));
                if (compte)
                    h.Savoirs.Add(new Savoir(Geste.Observer, Matiere.Aucune, Matiere.Aucune, Effet.Regularite, 0, Symptome.Aucun, 9, 9, Debut, Debut,
                        OrigineSavoir.Observation, 0, 0, Comportement.EcrireRegularite(Serie.Nombre, 0)));
            }
            var c = Monde();
            // La dernière lune vue : la veille de la pleine lune qui précède le début.
            for (var j = 1; j < 2 * Calendrier.JoursParAn; j++) c.RegularitesPourTest(tous, Debut + j * Calendrier.SecondesParJour);
            foreach (var h in tous) sortie.WriteLine(string.Join(" ", h.Savoirs.Select(s => $"{s.Pieces}:{s.Succes}/{s.Essais}")));
            return tous.Average(h => Math.Abs(Comportement.LireRegularite(h.Savoirs.First(s => s.Pieces.StartsWith("G|1|")).Pieces)!.Value.Jours - 29.53));
        }
        var avec = Erreur(true);
        var sans = Erreur(false);
        sortie.WriteLine($"avec {avec:F2}, sans {sans:F2}");
        Assert.True(avec < 2, $"{avec:F2}");
        Assert.True(avec < sans, $"{avec:F2} / {sans:F2}");
    }
}
