using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Caractère, intelligence, liens, humeur, deuil et communautés.</summary>
public sealed class PsycheEtSocialTests
{
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;

    private static double Ecart(IEnumerable<double> v)
    {
        var l = v.ToList();
        var m = l.Average();
        return Math.Sqrt(l.Sum(x => (x - m) * (x - m)) / l.Count);
    }

    private static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double ma = a.Average(), mb = b.Average();
        var cov = a.Zip(b, (x, y) => (x - ma) * (y - mb)).Sum();
        return cov / Math.Sqrt(a.Sum(x => (x - ma) * (x - ma)) * b.Sum(y => (y - mb) * (y - mb)));
    }

    private static Humain Humain(int id, Personnalite p, Sexe sexe = Sexe.Femme, int x = 10, int y = 10, double age = 25,
        int mere = 0) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        MereId = mere,
        Naissance = 10 * An - (long)(age * An),
        Personnalite = p,
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
    };

    private static Personnalite Caractere(double agreabilite = 0.5, double nevrosisme = 0.5, double extraversion = 0.5,
        double attachement = 0.9) =>
        new(0.5, 0.5, extraversion, agreabilite, nevrosisme, 0.5, attachement, 0.5, Intelligence.Moyenne);

    [Fact]
    public void Population_IntelligenceEnCloche_EtTraitsAutourDeLaMoyenne()
    {
        var alea = new Alea(11);
        var gens = Enumerable.Range(0, 5000).Select(_ => Psyche.Tirer(alea, 0.8)).ToList();
        var qi = gens.Select(p => p.Intelligence.Generale).ToList();

        Assert.InRange(qi.Average(), 98.5, 101.5);
        Assert.InRange(Ecart(qi), 13.5, 16.5);
        Assert.InRange(qi.Count(q => q < 70) / 5000.0, 0.01, 0.04);   // théorie : 2,3 %
        Assert.InRange(qi.Count(q => q >= 130) / 5000.0, 0.01, 0.04); // théorie : 2,3 %
        Assert.InRange(gens.Average(p => p.Extraversion), 0.48, 0.52);
        Assert.InRange(Ecart(gens.Select(p => p.Nevrosisme)), 0.13, 0.17);
        // L'ouverture est un peu liée à l'intelligence ; les aptitudes beaucoup.
        Assert.InRange(Correlation(qi, gens.Select(p => p.Ouverture).ToList()), 0.2, 0.4);
        Assert.InRange(Correlation(qi, gens.Select(p => p.Intelligence.Manuelle).ToList()), 0.6, 0.8);
        // Quelques talents artistiques exceptionnels, pas davantage.
        Assert.InRange(gens.Count(p => p.Intelligence.Artistique >= 145), 3, 60);
        Assert.All(gens, p => Assert.InRange(p.Charisme, 0, 1));
    }

    [Fact]
    public void Heritage_EnfantsDeParentsBrillants_RegressentVersLaMoyenne()
    {
        var alea = new Alea(3);
        Personnalite Brillant() => Psyche.Tirer(new Alea(99), 0.8) with
        {
            Intelligence = new Intelligence(130, 130, 130, 130, 130, 100),
            Agreabilite = 0.8,
        };
        var mere = Humain(1, Brillant());
        var pere = Humain(2, Brillant(), Sexe.Homme);
        var enfants = Enumerable.Range(0, 3000).Select(_ => Psyche.Heriter(mere, pere, alea, 0.8)).ToList();

        // Falconer : 100 + h² × 30 = 115 pour l'intelligence (h² = 0,5) ; 0,5 + 0,4 × 0,3 = 0,62 pour l'agréabilité.
        Assert.InRange(enfants.Average(p => p.Intelligence.Generale), 113, 117);
        Assert.InRange(Ecart(enfants.Select(p => p.Intelligence.Generale)), 12.5, 15.5);
        Assert.InRange(enfants.Average(p => p.Agreabilite), 0.6, 0.64);
    }

    [Fact]
    public void Liens_DesCompatiblesDeviennentAmis_DesIncompatiblesSeDetestent()
    {
        var social = new VieSociale(new Alea(5));
        var a = Humain(1, Caractere(agreabilite: 0.85, nevrosisme: 0.3));
        var b = Humain(2, Caractere(agreabilite: 0.8, nevrosisme: 0.35));
        var c = Humain(3, Caractere(agreabilite: 0.1, nevrosisme: 0.9, extraversion: 0.1), x: 40);
        var d = Humain(4, Caractere(agreabilite: 0.15, nevrosisme: 0.85, extraversion: 0.9), x: 41);
        var tous = new List<Humain> { a, b, c, d };

        for (var j = 1; j <= 60; j++) social.Journee(tous, 10 * An + j * Jour);

        Assert.True(a.LienAvec(2)!.Amis);
        Assert.Equal(a.LienAvec(2)!.Affection, b.LienAvec(1)!.Affection); // lien symétrique
        Assert.True(c.LienAvec(4)!.Rivaux);
        Assert.Null(a.LienAvec(3)); // trop loin pour se connaître
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Lien && e.Recit?.Cle == "j.lien.amis");
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Lien && e.Recit?.Cle == "j.lien.rivaux");

        // Séparés, ils s'oublient lentement.
        b.X = 80;
        var avant = a.LienAvec(2)!;
        for (var j = 61; j <= 160; j++) social.Journee(tous, 10 * An + j * Jour);
        Assert.InRange(a.LienAvec(2)!.Familiarite, avant.Familiarite - 10.5, avant.Familiarite - 9.5);
        Assert.True(a.LienAvec(2)!.Affection < avant.Affection);
    }

    [Fact]
    public void Deuil_UneMereAttacheePleureSonEnfant_LesSensiblesPlusLongtemps()
    {
        var social = new VieSociale(new Alea(1));
        var resiliente = Humain(1, Caractere(nevrosisme: 0.3, attachement: 0.9));
        var sensible = Humain(2, Caractere(nevrosisme: 0.85, attachement: 0.9));
        var detachee = Humain(3, Caractere(nevrosisme: 0.5, attachement: 0.1));
        var e1 = Humain(4, Caractere(), age: 5, mere: 1);
        var e2 = Humain(5, Caractere(), age: 5, mere: 2);
        var e3 = Humain(6, Caractere(), age: 5, mere: 3);
        var tous = new List<Humain> { resiliente, sensible, detachee, e1, e2, e3 };
        var maintenant = 10 * An;
        foreach (var e in new[] { e1, e2, e3 })
        {
            e.MortLe = maintenant;
            social.Endeuiller(e, tous, maintenant);
        }

        Assert.InRange(resiliente.Chagrin, 60, 80);
        Assert.True(sensible.Chagrin > resiliente.Chagrin);
        Assert.True(detachee.Chagrin < 20);
        Assert.Equal(4, resiliente.DeuilDeId);
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Humeur && e.Texte is "H1 pleure son fils H4." or "H1 pleure sa fille H4.");

        for (var j = 1; j <= 90; j++) Journee(social, tous, maintenant + j * Jour);
        // Trois mois plus tard : la résiliente a presque surmonté ; la sensible souffre encore beaucoup.
        Assert.True(resiliente.Chagrin < 10);
        Assert.True(sensible.Chagrin > 35);
        Assert.False(resiliente.EstDeprime);
        Assert.True(sensible.EstDeprime);
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Humeur
            && e.Texte.StartsWith("H2 sombre dans une profonde tristesse : elle pleure", StringComparison.Ordinal));

        // Le chagrin finit par s'apaiser et la dépression par passer.
        for (var j = 91; j <= 3 * 365; j++) Journee(social, tous, maintenant + j * Jour);
        Assert.False(sensible.EstDeprime);
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Humeur && e.Texte == "H2 reprend goût à la vie.");
    }

    /// <summary>Une journée d'humeur pour des humains qui vivent entourés d'au moins un proche (ils ne souffrent pas de solitude).</summary>
    private static void Journee(VieSociale social, List<Humain> tous, long maintenant)
    {
        foreach (var h in tous) h.CompagnieDuJour = 1;
        social.Humeurs(tous, maintenant);
    }

    [Fact]
    public void Depression_DureAuMoinsDeuxSemaines_SansAllerRetourChaqueJour()
    {
        var social = new VieSociale(new Alea(3));
        var h = Humain(1, Caractere());
        var tous = new List<Humain> { h };
        var maintenant = 10 * An;
        h.DeprimeDepuis = maintenant;
        h.JoursSombres = 21;
        // Une embellie passagère ne suffit pas : l'épisode tient au moins deux semaines.
        for (var j = 1; j <= 10; j++)
        {
            h.Humeur = 60;
            social.Humeurs(tous, maintenant + j * Jour);
        }
        Assert.True(h.EstDeprime);
        for (var j = 11; j <= 20; j++)
        {
            h.Humeur = 60;
            social.Humeurs(tous, maintenant + j * Jour);
        }
        Assert.False(h.EstDeprime);
        // Une seule mauvaise journée après la guérison ne replonge pas : il faut de nouveau trois semaines sombres.
        h.Humeur = 10;
        social.Humeurs(tous, maintenant + 21 * Jour);
        Assert.False(h.EstDeprime);
        Assert.Single(social.Journal, e => e.Texte.EndsWith("reprend goût à la vie."));
        Assert.DoesNotContain(social.Journal, e => e.Texte.EndsWith("sombre dans une profonde tristesse."));
    }

    [Fact]
    public void Communaute_SeFormeEntreAmisQuiViventEnsemble_PuisSeDisperse()
    {
        var social = new VieSociale(new Alea(2));
        var tous = Enumerable.Range(1, 6).Select(i => Humain(i, Caractere(extraversion: i == 3 ? 0.95 : 0.4), x: 10 + i % 3)).ToList();
        var loin = Humain(7, Caractere(), x: 60);
        tous.Add(loin);
        var maintenant = 10 * An;
        foreach (var a in tous)
        {
            foreach (var b in tous)
            {
                if (a.Id != b.Id) a.Liens.Add(new Lien(b.Id, 60, 60, maintenant, Amis: true));
            }
        }
        var communautes = new List<Communaute>();

        social.Regrouper(tous, communautes, maintenant, new HashSet<string>());

        var c = Assert.Single(communautes);
        Assert.All(tous.Take(6), h => Assert.Equal(c.Id, h.CommunauteId));
        Assert.Equal(0, loin.CommunauteId); // ami, mais il vit trop loin
        Assert.Equal(3, c.FigureId);        // le plus charismatique
        Assert.Equal("Communauté de H3", c.Nom);
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Communaute && e.Texte.StartsWith("Une communauté se forme autour de H3"));

        // Il rejoint le groupe en venant vivre près d'eux.
        loin.X = 12;
        social.Regrouper(tous, communautes, maintenant + 7 * Jour, new HashSet<string>());
        Assert.Equal(c.Id, loin.CommunauteId);
        Assert.Contains(social.Journal, e => e.Texte == "H7 rejoint la communauté de H3.");

        // Les liens se défont : le groupe se disperse.
        foreach (var h in tous) h.Liens.Clear();
        social.Regrouper(tous, communautes, maintenant + 14 * Jour, new HashSet<string>());
        Assert.False(c.Active);
        Assert.All(tous, h => Assert.Equal(0, h.CommunauteId));
        Assert.Contains(social.Journal, e => e.Texte == "La communauté de H3 se disperse.");
    }

    [Fact]
    public void DeuxAns_LaVieSocialeEstJournalisee_EtToutEstRelu()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-social-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42)) { JournalQuotidien = true };
            monde.AvancerDe(2 * An);
            var etat = monde.Etat;
            // Une captive, pour vérifier aussi sa relecture.
            etat.Humains![1].CaptifDe = 1;
            etat.Humains![1].CaptifLe = 5;
            // Une communauté dispersée en plus, pour vérifier aussi sa relecture.
            etat = etat with
            {
                Communautes = [.. etat.Communautes!, new Communaute { Id = etat.Communautes!.Count + 1, Nom = "Communauté de X", FondeeLe = 5, DisperseeLe = 99, FigureId = 1 }],
            };
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(etat);
            var relu = depot.Charger()!;

            Assert.Equivalent(etat.Communautes, relu.Communautes, strict: true);
            Assert.Equal(etat.Pluies, relu.Pluies);
            Assert.Equal(etat.Relations ?? [], relu.Relations ?? []);
            for (var i = 0; i < etat.Humains!.Count; i++) Assert.Equivalent(etat.Humains[i], relu.Humains![i], strict: true);
            var journal = etat.JournalNonEnregistre!;
            foreach (var type in new[] { TypeEvenement.Lien, TypeEvenement.Pensee, TypeEvenement.Journee, TypeEvenement.Souvenir,
                         TypeEvenement.Partage })
            {
                Assert.Contains(journal, e => e.Type == type);
            }
            Assert.Contains(etat.Humains, h => h.Liens.Any(l => l.Amis));
            // Le journal reste raisonnable : quelques centaines de lignes par humain et par an.
            var humainsAns = etat.Humains.Sum(h => ((h.MortLe ?? etat.Secondes) - Math.Max(h.Naissance, 0)) / (double)An);
            Assert.InRange(journal.Count / humainsAns, 100, 800);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
