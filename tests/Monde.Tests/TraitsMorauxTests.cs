using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Traits liés entre eux comme le montre la recherche, psychopathie, attachement, maturité et épreuves.</summary>
public sealed class TraitsMorauxTests
{
    private const long An = Calendrier.SecondesParAn;

    private static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double ma = a.Average(), mb = b.Average();
        var cov = a.Zip(b, (x, y) => (x - ma) * (y - mb)).Sum();
        return cov / Math.Sqrt(a.Sum(x => (x - ma) * (x - ma)) * b.Sum(y => (y - mb) * (y - mb)));
    }

    private static double R(List<Personnalite> gens, Func<Personnalite, double> a, Func<Personnalite, double> b) =>
        Correlation(gens.Select(a).ToList(), gens.Select(b).ToList());

    private static Humain Adulte(int id, Personnalite p, Sexe sexe, double age = 30) => new()
    {
        Id = id, Nom = $"H{id}", Sexe = sexe, PartenaireId = 0, Emplacement = 0, Naissance = (long)(-age * An),
        Personnalite = p, Biologie = Biologie.ParDefaut(sexe),
    };

    [Fact]
    public void Matrice_DefiniePositive_EtSymetrique()
    {
        var r = Psyche.MatriceCorrelations;
        for (var i = 0; i < r.GetLength(0); i++)
            for (var j = 0; j < r.GetLength(1); j++) Assert.Equal(r[i, j], r[j, i]);
        // Le tirage échouerait sinon (Cholesky).
        Assert.NotNull(Psyche.Tirer(new Alea(1), 0.8));
    }

    [Fact]
    public void Tirage_RespecteLesCorrelationsDeLaRecherche()
    {
        var alea = new Alea(21);
        var gens = Enumerable.Range(0, 20_000).Select(i => Psyche.Tirer(alea, 0.8, i % 2 == 0 ? Sexe.Homme : Sexe.Femme)).ToList();

        // Big Five : van der Linden et al. (2010). Les bornes tolèrent l'écrêtage à [0, 1].
        Assert.InRange(R(gens, p => p.Agreabilite, p => p.Conscience), 0.38, 0.48);
        Assert.InRange(R(gens, p => p.Nevrosisme, p => p.Conscience), -0.48, -0.38);
        Assert.InRange(R(gens, p => p.Extraversion, p => p.Ouverture), 0.38, 0.48);
        Assert.InRange(R(gens, p => p.Intelligence.Generale, p => p.Ouverture), 0.25, 0.35);
        // Compassion, aspect de l'agréabilité ; soin et empathie émotionnelle (Graham et al., 2011).
        Assert.InRange(R(gens, p => p.Agreabilite, p => p.Moral.Compassion), 0.75, 0.85);
        Assert.InRange(R(gens, p => p.Moral.EmpathieEmotionnelle, p => p.Moral.Fondements.Soin), 0.54, 0.64);
        // Psychopathie : faible honnêteté-humilité (Lee et Ashton, 2005).
        Assert.InRange(R(gens, p => p.Moral.Honnetete, p => p.Moral.Psychopathie), -0.7, -0.58);
        // La loyauté n'a presque rien à voir avec l'empathie.
        Assert.InRange(R(gens, p => p.Moral.EmpathieEmotionnelle, p => p.Moral.Fondements.Loyaute), -0.1, 0.1);
        // Attachement anxieux et névrosisme (Noftle et Shaver, 2006).
        Assert.InRange(R(gens, p => p.Nevrosisme, p => p.Moral.AnxieteAffective), 0.37, 0.47);
    }

    [Fact]
    public void Psychopathes_UnPeuPlusDUnPourCent_DeuxFoisPlusDHommes_EtAdultesSeulement()
    {
        var alea = new Alea(5);
        var hommes = Enumerable.Range(0, 30_000).Select(i => Adulte(i + 1, Psyche.Tirer(alea, 0.8, Sexe.Homme), Sexe.Homme)).ToList();
        var femmes = Enumerable.Range(0, 30_000).Select(i => Adulte(i + 1, Psyche.Tirer(alea, 0.8, Sexe.Femme), Sexe.Femme)).ToList();
        double ph = hommes.Count(h => Psyche.EstPsychopathe(h, 0)) / 30_000.0, pf = femmes.Count(h => Psyche.EstPsychopathe(h, 0)) / 30_000.0;
        Assert.InRange((ph + pf) / 2, 0.009, 0.016);
        Assert.True(ph > 2 * pf, $"hommes {ph:P2}, femmes {pf:P2}");

        var enfant = Adulte(1, hommes.First(h => Psyche.EstPsychopathe(h, 0)).Personnalite, Sexe.Homme, age: 12);
        Assert.False(Psyche.EstPsychopathe(enfant, 0));
    }

    [Fact]
    public void Attachement_56PourCentSurs_25Evitants_19Anxieux()
    {
        var alea = new Alea(8);
        var gens = Enumerable.Range(0, 20_000).Select(_ => Psyche.Tirer(alea, 0.8)).ToList();
        double Part(StyleAttachement s) => gens.Count(p => p.Moral.Style == s) / 20_000.0;
        Assert.InRange(Part(StyleAttachement.Secure), 0.53, 0.59);
        Assert.InRange(Part(StyleAttachement.Evitant), 0.22, 0.28);
        Assert.InRange(Part(StyleAttachement.Anxieux), 0.16, 0.22);
    }

    [Fact]
    public void Heritage_EmpathieEmotionnelleTransmiseAPresQueMoitie_LaCognitiveMoins()
    {
        var alea = new Alea(3);
        var base_ = Psyche.Tirer(new Alea(99), 0.8);
        var chaleureux = base_ with { Moral = base_.Moral with { EmpathieEmotionnelle = 0.8, EmpathieCognitive = 0.8 } };
        var mere = Adulte(1, chaleureux, Sexe.Femme);
        var pere = Adulte(2, chaleureux, Sexe.Homme);
        var enfants = Enumerable.Range(0, 4000).Select(_ => Psyche.Heriter(mere, pere, alea, 0.8)).ToList();
        // Falconer : 0,5 + h² × 0,3 → 0,644 (h² = 0,48) et 0,581 (h² = 0,27).
        Assert.InRange(enfants.Average(p => p.Moral.EmpathieEmotionnelle), 0.625, 0.665);
        Assert.InRange(enfants.Average(p => p.Moral.EmpathieCognitive), 0.56, 0.60);
    }

    [Fact]
    public void Heritage_LaMaturiteDesParentsNeSeTransmetPas()
    {
        var alea = new Alea(4);
        var moyen = Psyche.Tirer(new Alea(1), 0.8) with { Conscience = 0.5, Nevrosisme = 0.5, Agreabilite = 0.5 };
        var (dc, dn, da) = Psyche.MaturationAttendue(60);
        Assert.True(dc > 0.4 && dn < -0.2 && da > 0, $"{dc} {dn} {da}");
        // Parents de 60 ans dont la conscience a suivi la maturité moyenne : leurs enfants partent de la moyenne.
        var murs = moyen with { Conscience = 0.5 + 0.15 * dc, Nevrosisme = 0.5 + 0.15 * dn, Agreabilite = 0.5 + 0.15 * da };
        var mere = Adulte(1, murs, Sexe.Femme, age: 60);
        var pere = Adulte(2, murs, Sexe.Homme, age: 60);
        var enfants = Enumerable.Range(0, 4000).Select(_ => Psyche.Heriter(mere, pere, alea, 0.8, maintenant: 0)).ToList();
        Assert.InRange(enfants.Average(p => p.Conscience), 0.49, 0.51);
        Assert.InRange(enfants.Average(p => p.Nevrosisme), 0.49, 0.51);
    }

    [Fact]
    public void Maturite_ConscienceMonteEtNevrosismeBaisseEntre20Et40Ans()
    {
        var alea = new Alea(6);
        var gens = Enumerable.Range(0, 2000).Select(_ => Psyche.Tirer(alea, 0.8)).ToList();
        var murs = gens.Select(p =>
        {
            for (var age = 20; age < 40; age++) p = Psyche.Murir(p, age, alea);
            return p;
        }).ToList();
        // d ≈ 0,22 + 0,26 pour la conscience (Roberts et al., 2006) : 0,48 écart-type = 0,072 sur l'échelle des traits.
        Assert.InRange(murs.Average(p => p.Conscience) - gens.Average(p => p.Conscience), 0.055, 0.085);
        Assert.True(murs.Average(p => p.Nevrosisme) < gens.Average(p => p.Nevrosisme) - 0.03);
    }

    [Fact]
    public void Epreuve_DeuilRendPlusSensibleEtPlusAnxieuxDansLesLiens_SurtoutLesSensibles()
    {
        var alea = new Alea(2);
        var p = Psyche.Tirer(new Alea(7), 0.8);
        var serein = p with { Nevrosisme = 0.2 };
        var sensible = p with { Nevrosisme = 0.8 };
        double Choc(Personnalite x)
        {
            var total = 0.0;
            for (var i = 0; i < 500; i++) total += Psyche.Eprouver(x, Epreuve.Deuil, 1, alea).Personnalite.Nevrosisme - x.Nevrosisme;
            return total / 500;
        }
        Assert.True(Choc(sensible) > Choc(serein) * 1.5);
        var (apres, trace) = Psyche.Eprouver(sensible, Epreuve.Deuil, 1, new Alea(1));
        Assert.True(apres.Moral.AnxieteAffective > sensible.Moral.AnxieteAffective);
        Assert.Equal(sensible.Agreabilite, apres.Agreabilite);
        Assert.Equal((p, (string?)null), Psyche.Eprouver(p, Epreuve.Exil, 0, alea));
        _ = trace;
    }

    [Fact]
    public void MondeAncien_NouveauxTraitsTiresDapresLesAnciens_EtStablesALaRelecture()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-v17-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42));
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(monde.Etat);
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion17);

            var avant = monde.Etat.Humains!;
            var relu = new DepotMonde(fichier).Charger()!.Humains!;
            var relu2 = new DepotMonde(fichier).Charger()!.Humains!;
            Assert.Equal(avant.Select(h => h.Personnalite.Agreabilite), relu.Select(h => h.Personnalite.Agreabilite));
            // Tirés, pas moyens, et identiques d'une lecture à l'autre.
            Assert.Contains(relu, h => Math.Abs(h.Personnalite.Moral.Compassion - 0.5) > 0.05);
            Assert.Equal(relu.Select(h => h.Personnalite.Moral), relu2.Select(h => h.Personnalite.Moral));

            // Sur une grande population, la loi conditionnelle garde les liens avec les anciens traits.
            var alea = new Alea(12);
            var anciens = Enumerable.Range(0, 10_000).Select(i =>
            {
                var s = i % 2 == 0 ? Sexe.Homme : Sexe.Femme;
                return Psyche.Completer(Psyche.Tirer(alea, 0.8, s) with { Moral = TraitsMoraux.Moyens }, s, alea);
            }).ToList();
            Assert.InRange(R(anciens, p => p.Agreabilite, p => p.Moral.Compassion), 0.75, 0.85);
            Assert.InRange(R(anciens, p => p.Moral.Honnetete, p => p.Moral.Psychopathie), -0.7, -0.58);
            Assert.InRange(R(anciens, p => p.Nevrosisme, p => p.Moral.AnxieteAffective), 0.37, 0.47);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
