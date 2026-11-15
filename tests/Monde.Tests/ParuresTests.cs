using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.42.6 : se couvrir, se protéger, se parer (semelles nouées, enduit de la peau, corne des pieds, signe et pudeur).</summary>
public sealed class ParuresTests
{
    private const long Jour = Calendrier.SecondesParJour;

    private static string M(Matiere m) => ((int)m).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Facture F(string s) => Facture.Lire(s) ?? throw new InvalidOperationException($"illisible : {s}");

    /// <summary>
    /// Une peau ou une natte tressée, nouée au pied par une corde, protège bien la plante ; de l'écorce moins, une feuille presque
    /// pas. Ce qui est raide, ce qui n'est pas plat, ce qui n'est pas un lien ne tient pas. Les lois ne nomment pas la sandale.
    /// </summary>
    [Fact]
    public void Semelle_LesLoisDecidentSansLaNommer()
    {
        var peau = $"n0({M(Matiere.Peau)},{M(Matiere.Corde)})";
        Assert.Equal(peau, F(peau).Ecrire());
        var cPeau = Facons.Calculer(F(peau));
        Assert.True(cPeau.Reussie);
        var natte = Facons.Calculer(F($"n0(T({M(Matiere.Fibres)}),{M(Matiere.Corde)})"));
        var ecorce = Facons.Calculer(F($"n0({M(Matiere.Ecorce)},{M(Matiere.Fibres)})"));
        var feuille = Facons.Calculer(F($"n0({M(Matiere.GrandesFeuilles)},{M(Matiere.Fibres)})"));
        Assert.True(natte.Reussie && ecorce.Reussie && feuille.Reussie);
        Assert.InRange(Facons.Chaussure(cPeau.Traits), 0.6, 0.95);
        Assert.InRange(Facons.Chaussure(natte.Traits), 0.6, 0.95);
        Assert.True(Facons.Chaussure(ecorce.Traits) < Facons.Chaussure(cPeau.Traits));
        Assert.True(Facons.Chaussure(feuille.Traits) < 0.35);
        Assert.Equal(0, Facons.Valeur(Fonction.Chausser, Facons.Calculer(F(M(Matiere.Peau))).Traits));
        Assert.Equal("sandale", Facons.NomActuel(F(peau)));
        Assert.Equal("fac.r.rienALacer", Facons.Calculer(F($"n0({M(Matiere.Branche)},{M(Matiere.Corde)})")).Echec!.Cle);
        Assert.Equal("fac.r.pasUnLien", Facons.Calculer(F($"n0({M(Matiere.Peau)},{M(Matiere.Peau)})")).Echec!.Cle);
        Assert.True(Facons.JoursDeMarche(Matiere.Peau) > Facons.JoursDeMarche(Matiere.Ecorce));
    }

    /// <summary>Les mondes d'avant la matière (génération 0) n'ont ni corne, ni gênes, ni enduit : leur histoire ne change pas.</summary>
    [Fact]
    public void VieuxMonde_NeConnaitPasLesGenesDuCorps()
    {
        var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
        monde.AvancerDe(20 * Jour);
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains, h =>
        {
            Assert.Equal(-1, h.Cal);
            Assert.Equal(0, h.Pieds);
            Assert.Equal(0, h.CoupDeSoleil);
            Assert.Null(h.Chaussures);
            Assert.Equal(Matiere.Aucune, h.Enduit);
        });
    }

    /// <summary>
    /// Dans un monde neuf, la plante des pieds est estimée dès le premier pas (endurcie chez l'adulte, tendre chez le petit), et
    /// tout ce que vit le corps (corne, gênes, enduit, chaussures), comme le signe et la pudeur d'une communauté, se relit à l'identique.
    /// </summary>
    [Fact]
    public void MondeNeuf_CorpsEtParuresSeRelisent()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-parures-" + Guid.NewGuid().ToString("N"));
        try
        {
            var conf = ConfigurationMonde.Nouvelle(42);
            var monde = new MondeSimule(GenerateurContinent.Generer(conf), MondeSimule.Nouveau(conf)) { Coeurs = 1 };
            monde.AvancerDe(Jour);
            var etat = monde.Etat;
            var vivants = etat.Humains!.Where(h => h.EstVivant).ToList();
            Assert.All(vivants, h => Assert.InRange(h.Cal, 0, 1));
            Assert.Contains(vivants, h => h.Cal > 0.4);
            var h = vivants[0];
            var semelle = Facons.Ecrire(Fonction.Chausser, F($"n0({M(Matiere.Peau)},{M(Matiere.Corde)})"));
            h.Chaussures = new Objet(Matiere.Facture, 0.7, 0.6, Pieces: semelle);
            (h.Cal, h.Pieds, h.CoupDeSoleil, h.Piqures) = (0.42, 55, 31, 12);
            (h.Enduit, h.EnduitJusqua, h.EnduitPare) = (Matiere.Ocre, etat.Secondes + 2 * Jour, true);
            var communautes = new List<Communaute> { new() { Id = 1, Nom = "Les Rouges", FondeeLe = 0, FigureId = h.Id, SigneDuCorpsLe = 1234, PudeurLe = 5678 } };
            etat = etat with { Communautes = communautes };
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            var r = relu.Humains![h.Id - 1];
            Assert.Equal((0.42, 55.0, 31.0, 12.0), (r.Cal, r.Pieds, r.CoupDeSoleil, r.Piqures));
            Assert.Equal((Matiere.Ocre, h.EnduitJusqua, true), (r.Enduit, r.EnduitJusqua, r.EnduitPare));
            Assert.Equal(h.Chaussures, r.Chaussures);
            var c = relu.Communautes!.Single();
            Assert.Equal((1234L, 5678L), (c.SigneDuCorpsLe, c.PudeurLe));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
