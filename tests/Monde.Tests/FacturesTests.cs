using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.34.0 : choses façonnées par façons et jointures, jugées par les lois ; idées par combinaison.</summary>
public sealed class FacturesTests(ITestOutputHelper sortie)
{
    private static string M(Matiere m) => ((int)m).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Facture F(string s) => Facture.Lire(s) ?? throw new InvalidOperationException($"illisible : {s}");

    /// <summary>Une branche courbée, une corde aux deux bouts : ce qui revient d'un coup.</summary>
    private static readonly string ArcNu = $"k0({M(Matiere.Branche)},{M(Matiere.Corde)})";

    /// <summary>Une branche fendue trois fois, aiguisée : une hampe légère.</summary>
    private static readonly string Hampe = $"A(F(F(F({M(Matiere.Branche)}))))";

    [Fact]
    public void Ecriture_SeRelitALIdentique()
    {
        foreach (var s in new[] { "5", "A(5)", ArcNu, $"p0({ArcNu},{Hampe})", $"b{M(Matiere.Corde)}(A(5),{M(Matiere.Eclat)})", "e0(T(8),20)" })
            Assert.Equal(s, F(s).Ecrire());
        foreach (var s in new[] { "", "Z(5)", "A(5", "A5)", "b(5,5)", "999", "A(A(A(A(A(A(A(5)))))))", "x0(5,5)", $"A({M(Matiere.Eau)})" })
            Assert.Null(Facture.Lire(s));
        var objet = Facons.Ecrire(Fonction.Chasser, F(ArcNu));
        Assert.Equal((Fonction.Chasser, ArcNu), (Facons.LireObjet(objet)!.Value.Fonction, Facons.LireObjet(objet)!.Value.Facture.Ecrire()));
        Assert.Equal("F|" + ArcNu, Lois.Structure(objet));
    }

    /// <summary>
    /// Les lois de l'arc : une branche verte tenue pliée par une corde garde quelques dizaines de joules ; une hampe légère part
    /// vers 30-60 m/s et chasse mieux qu'une pointe de pierre emmanchée jetée à la main. Le bois sec casse, les fibres lâchent.
    /// </summary>
    [Fact]
    public void Arc_LesLoisDecidentSansLeNommer()
    {
        var arc = Facons.Calculer(F(ArcNu));
        Assert.True(arc.Reussie);
        Assert.InRange(arc.Traits.Tension, 30, 70);
        var tir = Facons.Calculer(F($"p0({ArcNu},b{M(Matiere.Corde)}({Hampe},{M(Matiere.Eclat)}))"));
        Assert.True(tir.Reussie);
        sortie.WriteLine($"arc {arc.Traits.Tension:F0} J, flèche {tir.Traits.Masse * 1000:F0} g à {tir.Traits.Vitesse:F0} m/s, chasse {Facons.Chasse(tir.Traits):F2}");
        Assert.InRange(tir.Traits.Vitesse, 20, 60);
        var pointe = Lois.Chasse(new Objet(Matiere.Assemblage, 0.5, 1, Pieces: new Assemblage(Matiere.Epieu, 1, Matiere.Eclat, 1, Matiere.Corde, Montage.Bout).Signature));
        Assert.True(Facons.Chasse(tir.Traits) > pointe, $"{Facons.Chasse(tir.Traits):F2} contre {pointe:F2}");
        // Une hampe trop lourde ne part pas ; un tronc casse, un bout de bois sec est trop court ; une poignée de fibres ne fait pas le tour.
        Assert.False(Facons.Calculer(F($"p0({ArcNu},{M(Matiere.Tronc)})")).Reussie);
        Assert.Equal("fac.r.casse", Facons.Calculer(F($"k0({M(Matiere.Tronc)},{M(Matiere.Corde)})")).Echec!.Cle);
        Assert.Equal("fac.r.tropCourt", Facons.Calculer(F($"k0({M(Matiere.BoisSec)},{M(Matiere.Corde)})")).Echec!.Cle);
        Assert.Equal("fac.r.lienTropCourt", Facons.Calculer(F($"k0({M(Matiere.Branche)},{M(Matiere.Fibres)})")).Echec!.Cle);
        Assert.Equal("arc", Facons.NomActuel(F($"p0({ArcNu},{Hampe})")));
    }

    /// <summary>Un propulseur (un bâton encoché) lance une sagaie plus vite que la main ; une branche aiguisée jetée vaut un bâton pointu.</summary>
    [Fact]
    public void Propulseur_EtBatonJete()
    {
        var sagaie = $"A(F({M(Matiere.Branche)}))";
        var main = Facons.Chasse(Facons.Calculer(F(sagaie)).Traits);
        var propulsee = Facons.Calculer(F($"p0(N(F({M(Matiere.Branche)})),{sagaie})"));
        Assert.True(propulsee.Reussie);
        sortie.WriteLine($"à la main {main:F2}, au propulseur {Facons.Chasse(propulsee.Traits):F2} ({propulsee.Traits.Vitesse:F0} m/s)");
        Assert.True(Facons.Chasse(propulsee.Traits) > main);
        Assert.InRange(Facons.Chasse(Facons.Calculer(F($"A({M(Matiere.Branche)})")).Traits), 0.2, 0.8);
    }

    /// <summary>Un panier ne garde pas l'eau ; enduit de résine chauffée, il la garde ; une peau percée et cousue fait une outre.</summary>
    [Fact]
    public void Contenants()
    {
        var panier = Facons.Calculer(F($"T({M(Matiere.Fibres)})"));
        Assert.True(panier.Reussie);
        Assert.True(panier.Traits.Contenance > 1);
        Assert.Equal(0, Facons.Eau(panier.Traits));
        Assert.True(Facons.Eau(Facons.Calculer(F($"e0(T({M(Matiere.Fibres)}),{M(Matiere.Resine)})")).Traits) > 0);
        var froid = Facons.Construire(F($"e0(T({M(Matiere.Fibres)}),{M(Matiere.Resine)})"), Atelier.Complet with { Feu = false });
        Assert.Equal("fac.r.resineFroide", froid.Echec!.Cle);
        var outre = Facons.Calculer(F($"o0(R({M(Matiere.Peau)}),{M(Matiere.Corde)})"));
        Assert.InRange(Facons.Eau(outre.Traits), 5, 15);
        Assert.Equal("fac.r.pasPerce", Facons.Calculer(F($"o0({M(Matiere.Peau)},{M(Matiere.Corde)})")).Echec!.Cle);
    }

    /// <summary>Une hache de pierre polie coupe bien mieux qu'un éclat tenu en main ; sans outil, on ne fend rien.</summary>
    [Fact]
    public void Coupe_EtOutils()
    {
        var eclat = Facons.Coupe(Facons.De(Matiere.Eclat));
        var hache = Facons.Coupe(Facons.Calculer(F($"t{M(Matiere.Corde)}({M(Matiere.Branche)},P({M(Matiere.Silex)}))")).Traits);
        sortie.WriteLine($"éclat {eclat:F2}, hache polie {hache:F2}");
        Assert.InRange(eclat, 0.08, 0.2);
        Assert.InRange(hache, 0.35, 0.8);
        var rien = new Atelier(0, 0, false, false, false, false);
        Assert.Equal("fac.r.rienPourFendre", Facons.Construire(F($"F({M(Matiere.Branche)})"), rien).Echec!.Cle);
        Assert.Equal("fac.r.rienPourRacler", Facons.Construire(F($"A({M(Matiere.Branche)})"), rien).Echec!.Cle);
    }

    /// <summary>Les choses se disent par leur fabrication, en français accordé et en anglais, avec le nom qu'on leur donnerait aujourd'hui.</summary>
    [Fact]
    public void Description_FrancaisEtAnglais()
    {
        var fr = Catalogues.Fr;
        var en = Catalogues.Toutes["en"];
        var arc = F($"p0({ArcNu},b{M(Matiere.Corde)}({Hampe},{M(Matiere.Eclat)}))");
        var panier = F($"e0(T({M(Matiere.Fibres)}),{M(Matiere.Resine)})");
        foreach (var f in new[] { arc, panier, F($"o0(R({M(Matiere.Peau)}),{M(Matiere.Corde)})") })
        {
            var (tf, te) = (fr.Rediger(Facons.Decrire(f)), en.Rediger(Facons.Decrire(f)));
            sortie.WriteLine(tf);
            sortie.WriteLine(te);
            Assert.DoesNotContain("{", tf + te);
            Assert.DoesNotContain("fac.", tf + te);
        }
        Assert.Contains("fendue 3 fois", fr.Rediger(Facons.Decrire(arc)));
        Assert.Contains("des fibres tressées", fr.Rediger(Facons.Decrire(panier)));
        Assert.Contains("un arc et sa flèche", fr.Rediger(Facons.Decrire(arc)));
        var h = Adulte(1, 0, 0);
        var phrase = fr.Rediger(new Monde.Simulation.Recits.Recit("fac.idee.rate", ("question", new Monde.Simulation.Recits.Recit("fac.idee.question", ("h", h),
            ("geste", Facons.DireGeste(F($"k0({M(Matiere.Tronc)},{M(Matiere.Corde)})"))))), ("h", h), ("raison", Facons.Calculer(F($"k0({M(Matiere.Tronc)},{M(Matiere.Corde)})")).Echec!)));
        sortie.WriteLine(phrase);
        Assert.DoesNotContain("{", phrase);
    }

    private static readonly Carte Carte = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));

    private static readonly long Printemps = 10 * Calendrier.SecondesParAn + 12 * Calendrier.SecondesParHeure;

    private static Humain Adulte(int id, int x, int y) => new()
    {
        Id = id,
        Nom = $"H{id}",
        PartenaireId = 0,
        Sexe = Sexe.Homme,
        Emplacement = id,
        Naissance = Printemps - 25 * Calendrier.SecondesParAn,
        Personnalite = new Personnalite(0.8, 0.5, 0.5, 0.5, 0.5, 0.8, 0.5, 0.5, new Intelligence(120, 120, 120, 120, 120, 120)),
        Biologie = Biologie.ParDefaut(Sexe.Homme),
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

    private static Comportement Monde(int generation = 2) => new(Carte, new Ecosysteme(Carte), new Alea(3))
    {
        Social = new VieSociale(new Alea(1)) { Carte = Carte, Generation = generation }, Generation = generation, Installations = new Installations(),
    };

    /// <summary>
    /// L'Entité souffle une combinaison : l'humain l'essaie avec ce qu'il a ; les lois disent qu'elle marche, il la retient, la
    /// porte, et c'est une première au monde. Une combinaison absurde est essayée, ratée, et la raison est notée.
    /// </summary>
    [Fact]
    public void Entite_SouffleUneCombinaison()
    {
        var (x, y) = Foret();
        var h = Adulte(1, x, y);
        h.Objets.Add(new Objet(Matiere.Corde, 0.8, 1));
        h.Objets.Add(new Objet(Matiere.Corde, 0.8, 1));
        h.Objets.Add(new Objet(Matiere.Eclat, 0.8, 1));
        var c = Monde();
        var tous = new List<Humain> { h };
        var e = c.SoufflerFacture(h, $"p0({ArcNu},b{M(Matiere.Corde)}({Hampe},{M(Matiere.Eclat)}))", tous, Printemps);
        foreach (var ev in c.Journal) sortie.WriteLine(ev.Recit?.Cle + " " + string.Join(",", ev.Recit?.Args.Select(a => (a.Valeur as Monde.Simulation.Recits.Recit)?.Cle ?? a.Valeur?.ToString()) ?? []));
        Assert.Equal(Effet.Facture, e);
        var objet = h.Objets.Find(o => o.Matiere == Matiere.Facture);
        Assert.NotNull(objet);
        Assert.True(Lois.Chasse(objet) > 1, $"{Lois.Chasse(objet):F2}");
        var d = Assert.Single(c.Installations.Decouvertes);
        Assert.Equal(OrigineSavoir.Entite, d.Origine);
        Assert.StartsWith("F|", d.Pieces);
        // Une pierre qu'on voudrait courber : rien.
        Assert.Equal(Effet.Rien, c.SoufflerFacture(h, $"C({M(Matiere.Silex)})", tous, Printemps + 600));
    }

    /// <summary>
    /// Au fil des jours, un humain ingénieux qui sait tordre une corde, tailler une pointe et a remarqué que la branche verte revient,
    /// finit par combiner ces savoirs (avec des esprits fertiles pour un test court) ; chaque trouvaille garde ses parents.
    /// </summary>
    [Fact]
    public void Combinaisons_NaissentDuVoisinage()
    {
        var (x, y) = Foret();
        var h = Adulte(1, x, y);
        h.Objets.Add(new Objet(Matiere.Eclat, 0.8, 1));
        h.Objets.Add(new Objet(Matiere.Corde, 0.8, 1));
        foreach (var (g, a, b, e, p) in new[]
                 {
                     (Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde, ""),
                     (Geste.Gratter, Matiere.Branche, Matiere.Eclat, Effet.Pointe, ""),
                     (Geste.Gratter, Matiere.Carcasse, Matiere.Eclat, Effet.Decoupe, ""),
                     (Geste.Faconner, Matiere.Branche, Matiere.Aucune, Effet.Remarque, "R|ressort"),
                 })
            h.Savoirs.Add(new Savoir(g, a, b, e, 0, Symptome.Aucun, 5, 5, 0, 0, OrigineSavoir.Essai, 0, 0, p));
        var c = Monde();
        c.EspritsFertiles = 200;
        var tous = new List<Humain> { h };
        for (var k = 0; k < 4000 && h.Savoirs.Count(s => s.Effet == Effet.Facture) < 3; k++)
        {
            h.Faim = h.Soif = 0;
            h.Fatigue = 0;
            c.Reprendre(tous, Printemps + k * 600L);
            c.InventerParCombinaisonPourTest(h, Fonction.Chasser, tous);
        }
        var trouvees = h.Savoirs.Where(s => s.Effet == Effet.Facture).ToList();
        foreach (var s in trouvees) sortie.WriteLine($"{s.Pieces}");
        foreach (var d in c.Installations.Decouvertes) sortie.WriteLine($"{d.Pieces} ← {d.Parents}");
        Assert.NotEmpty(trouvees);
        Assert.Contains(c.Installations.Decouvertes, d => d.Parents.Length > 0);
    }

    /// <summary>En génération 1, rien de tout cela : pas d'idée par combinaison.</summary>
    [Fact]
    public void Generation1_SansCombinaisons()
    {
        var (x, y) = Foret();
        var h = Adulte(1, x, y);
        h.Savoirs.Add(new Savoir(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde, 0, Symptome.Aucun, 5, 5, 0, 0, OrigineSavoir.Essai, 0, 0));
        var c = Monde(1);
        c.EspritsFertiles = 200;
        var tous = new List<Humain> { h };
        for (var k = 0; k < 200; k++) c.InventerParCombinaisonPourTest(h, Fonction.Chasser, tous);
        Assert.DoesNotContain(h.Savoirs, s => s.Effet == Effet.Facture);
    }
}
