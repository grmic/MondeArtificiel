using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Beauté, attirance, refus, orientation, mœurs des communautés, exil, maladies nommées et délai des débats.</summary>
public sealed class AttiranceTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, Sexe sexe = Sexe.Femme, double age = 25, double ouverture = 0.5, double nevrosisme = 0.5,
        double agreabilite = 0.5, int x = 10, int y = 10) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, agreabilite, nevrosisme, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        Reserves = 50,
        Sante = 100,
        X = x,
        Y = y,
    };

    private static void Lier(Humain a, Humain b, double affection)
    {
        a.Liens.Add(new Lien(b.Id, 60, affection, Maintenant));
        b.Liens.Add(new Lien(a.Id, 60, affection, Maintenant));
    }

    // ───────────── Orientation ─────────────

    [Fact]
    public void Orientation_ProportionsDesGrandesEnquetes_EtPenchantTransmis()
    {
        var alea = new Alea(3);
        const int n = 200_000;
        foreach (var (sexe, homo, bi) in new[] { (Sexe.Homme, 0.02, 0.02), (Sexe.Femme, 0.015, 0.055) })
        {
            var h = Humain(1, sexe);
            string Categorie()
            {
                h.Orientation = Attirance.TirerOrientation(sexe, alea);
                return Attirance.Orienter(h).Replace("elle", "el");
            }
            var categories = Enumerable.Range(0, n).Select(_ => Categorie()).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count() / (double)n);
            Assert.InRange(categories.GetValueOrDefault("homosexuel"), homo * 0.85, homo * 1.15);
            Assert.InRange(categories.GetValueOrDefault("bisexuel"), bi * 0.85, bi * 1.15);
            Assert.True(categories.GetValueOrDefault("hétérosexuel") > 0.75);
        }
        // Un parent attiré par son sexe et des frères aînés rendent un fils plus souvent homosexuel.
        var transmis = Enumerable.Range(0, n).Count(_ => Attirance.TirerOrientation(Sexe.Homme, alea, penchant: 1, freresAines: 3).VersHommes >= 0.75);
        Assert.True(transmis / (double)n > 0.05, $"{transmis}");
    }

    [Fact]
    public void Apparence_HeriteeDesParents()
    {
        var alea = new Alea(5);
        var grands = new Apparence(0.5, 0.5, 2, 0.5, 0.5);
        var petits = new Apparence(0.5, 0.5, -2, 0.5, 0.5);
        var enfantsDeGrands = Enumerable.Range(0, 2000).Average(_ => Attirance.HeriterApparence(grands, grands, alea).Taille);
        var enfantsDePetits = Enumerable.Range(0, 2000).Average(_ => Attirance.HeriterApparence(petits, petits, alea).Taille);
        Assert.InRange(enfantsDeGrands, 1.4, 1.8);
        Assert.InRange(enfantsDePetits, -1.8, -1.4);
    }

    // ───────────── Beauté et désir ─────────────

    [Fact]
    public void Beaute_DependDeLIdealDuGroupe_EtLAttractiviteSeFaneAvecLAge()
    {
        var grande = Humain(1);
        grande.Apparence = new Apparence(0.6, 0.6, 1.5, 0.3, 0.5);
        var aimeLesGrands = new IdealBeaute(1.5, 0.3, 0.5);
        var aimeLesPetits = new IdealBeaute(-1.5, 0.8, 0.5);
        Assert.True(Attirance.Beaute(grande, aimeLesGrands, Maintenant) > Attirance.Beaute(grande, aimeLesPetits, Maintenant) + 15);

        var agee = Humain(2, age: 45);
        agee.Apparence = grande.Apparence;
        Assert.True(Attirance.Attractivite(agee, aimeLesGrands, Maintenant) < Attirance.Attractivite(grande, aimeLesGrands, Maintenant) - 15);

        // Le désir suit l'orientation : un homme qui n'est attiré que par les hommes ne désire pas une femme, si belle soit-elle.
        var homme = Humain(3, Sexe.Homme);
        homme.Orientation = new Orientation(0.9, 0);
        Assert.Equal(0, Attirance.Desir(homme, grande, aimeLesGrands, Maintenant));
        Assert.True(Attirance.Desir(homme, Humain(4, Sexe.Homme), aimeLesGrands, Maintenant) > 0.3);
    }

    [Fact]
    public void Avances_RepousseesPourCeQuOnNInspirePas_BlessentEtSontNotees()
    {
        var comportement = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1)) { Social = new VieSociale(new Alea(1)) };
        var lui = Humain(1, Sexe.Homme, nevrosisme: 0.8);
        var elle = Humain(2);
        elle.Orientation = new Orientation(0, 1); // elle n'aime que les femmes
        lui.Humeur = 60;
        var tous = new List<Humain> { lui, elle };

        Assert.True(comportement.Proposer(lui, elle, 0.7, tous, Maintenant));
        var refus = Assert.Single(comportement.Journal, j => j.Type == TypeEvenement.Attirance);
        Assert.Equal("H2 repousse les avances de H1, qui ne lui plaît pas.", refus.Texte);
        Assert.True(lui.Humeur < 50);
        var peine = Assert.Single(lui.Memoire.Jardin, x => x.Cle == "refus:2");
        Assert.Equal((TypeIntime.Peine, refus.Id), (peine.Type, peine.CauseId));
        Assert.Equal(0, lui.PartenaireId);

        // Le même refus ne se redit pas chaque jour.
        comportement.Proposer(lui, elle, 0.7, tous, Maintenant + Jour);
        Assert.Single(comportement.Journal, j => j.Type == TypeEvenement.Attirance);
    }

    [Fact]
    public void Avances_Acceptees_Rejouissent_EtFormentUnCouple()
    {
        var comportement = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1)) { Social = new VieSociale(new Alea(1)) };
        var lui = Humain(1, Sexe.Homme);
        var elle = Humain(2);
        lui.Apparence = elle.Apparence = new Apparence(0.9, 0.9, 0, 0.5, 0.9);
        Lier(lui, elle, 60);
        lui.Humeur = elle.Humeur = 50;
        var tous = new List<Humain> { lui, elle };

        Assert.True(comportement.Proposer(lui, elle, 0.8, tous, Maintenant));
        Assert.Equal((2, 1), (lui.PartenaireId, elle.PartenaireId));
        Assert.True(lui.Humeur > 50);
        Assert.Contains(lui.Memoire.Jardin, x => x.Type == TypeIntime.Joie && x.Cle == "accepte:2");
        Assert.Contains(comportement.Journal, j => j.Type == TypeEvenement.Accouplement);
    }

    [Fact]
    public void DesirPourSonSexe_RefouleSansLeComprendre_PuisLaConscienceVient()
    {
        var comportement = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1)) { Social = new VieSociale(new Alea(1)) };
        var lui = Humain(1, Sexe.Homme, ouverture: 0.8);
        var autre = Humain(2, Sexe.Homme);
        lui.Orientation = new Orientation(0.9, 0.1);
        lui.Lucidite = 0;
        var tous = new List<Humain> { lui, autre };

        Assert.False(comportement.Proposer(lui, autre, 0.8, tous, Maintenant));
        var refoule = Assert.Single(comportement.Journal, j => j.Type == TypeEvenement.Attirance);
        Assert.Equal("H1 désire H2, mais refoule ce désir sans comprendre ce trouble.", refoule.Texte);
        Assert.Contains(lui.Memoire.Jardin, x => x.Type == TypeIntime.Question && x.Cle == "refoule:2");

        // Désir après désir, il finit par comprendre ce qui l'attire.
        for (var i = 1; i < 40_000 && lui.Lucidite < 0.6; i++) comportement.Proposer(lui, autre, 0.8, tous, Maintenant + i * MondeSimule.SecondesParPas);
        Assert.True(lui.Lucidite >= 0.6);
        Assert.Contains(comportement.Journal, j => j.Texte == "H1 se sent troublé par les hommes, sans comprendre pourquoi.");
        Assert.Contains(comportement.Journal, j => j.Texte == "H1 comprend que ce sont les hommes qui l'attirent.");
        Assert.Equal("homosexuel", Attirance.Orienter(lui));
    }

    // ───────────── Mœurs des communautés ─────────────

    [Fact]
    public void Moeurs_UnTemoinIntolerantReprouve_LaCommunauteSeDurcit()
    {
        var social = new VieSociale(new Alea(1));
        var c = new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1, ToleranceMoeurs = 0.3 };
        social.Communautes = [c];
        var temoin = Humain(1, ouverture: 0.1, agreabilite: 0.3);
        var a = Humain(2, Sexe.Homme);
        var b = Humain(3, Sexe.Homme);
        var membre = Humain(4, ouverture: 0.2);
        foreach (var h in new[] { temoin, a, b, membre }) h.CommunauteId = 1;
        Lier(membre, a, 30);
        var tous = new List<Humain> { temoin, a, b, membre };

        social.DecouvrirMoeurs(temoin, a, b, 99, tous, Maintenant);

        var decouverte = social.Journal.First(j => j.Type == TypeEvenement.Attirance);
        Assert.Equal(99, decouverte.CauseId);
        Assert.Contains("H2 et H3 s'aiment", decouverte.Texte);
        Assert.Contains(temoin.Griefs, g => g.ContreId == 2 && g.Motif == MotifGrief.Moeurs);
        Assert.True(c.ToleranceMoeurs < 0.3);
        Assert.Contains(social.Journal, j => j.Texte == "La communauté de H1 réprouve les amours de H2 et H3.");
        Assert.True(membre.AffectionPour(2) < 30);
        Assert.Contains(a.Memoire.Jardin, x => x.Type == TypeIntime.Peur);

        // Un témoin ouvert ne s'en émeut pas.
        var ouvert = Humain(5, ouverture: 0.95, agreabilite: 0.9);
        social.DecouvrirMoeurs(ouvert, a, b, 99, tous, Maintenant);
        Assert.DoesNotContain(ouvert.Griefs, g => g.Motif == MotifGrief.Moeurs);
    }

    // ───────────── Exil ─────────────

    [Fact]
    public void Exil_LaCommunauteChasseUnMeurtrier_AvecSaRaison_PuisLExilPrendFin()
    {
        var social = new VieSociale(new Alea(4));
        var c = new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1 };
        social.Communautes = [c];
        var membres = Enumerable.Range(1, 6).Select(i => Humain(i, i % 2 == 0 ? Sexe.Homme : Sexe.Femme, agreabilite: 0.3)).ToList();
        var victime = Humain(7);
        victime.MortLe = Maintenant - 30 * Jour;
        var meurtrier = membres[5];
        foreach (var m in membres) m.CommunauteId = 1;
        foreach (var m in membres.Take(5))
        {
            m.Griefs.Add(new Grief(meurtrier.Id, MotifGrief.Meurtre, 85, 85, 55, VictimeId: 7));
            Lier(m, meurtrier, -60);
        }
        var tous = new List<Humain>(membres) { victime };
        var groupes = new Dictionary<int, List<Humain>> { [1] = membres };

        var semaine = 0;
        for (; semaine < 200 && meurtrier.Exclusions.Count == 0; semaine++) social.Exiler(tous, [c], groupes, Maintenant + semaine * 7 * Jour);

        var exil = Assert.Single(meurtrier.Exclusions);
        Assert.Equal((TypeExclusion.Communaute, 1, "pour le meurtre de H7"), (exil.Type, exil.De, exil.Raison));
        var evenement = Assert.Single(social.Journal, j => j.Type == TypeEvenement.Exclusion);
        Assert.True(evenement.Significatif);
        Assert.Equal(55, evenement.CauseId);
        Assert.StartsWith("La communauté de H1 chasse H6 pour le meurtre de H7", evenement.Texte);
        var quand = Maintenant + (semaine - 1) * 7 * Jour;
        Assert.True(Bannissement.Separes(meurtrier, membres[0], quand));
        Assert.Equal(1, Bannissement.Exil(meurtrier, quand));
        Assert.Contains(meurtrier.Memoire.Jardin, x => x.Type == TypeIntime.Peine && x.CauseId == evenement.Id);

        if (exil.Jusqua > 0)
        {
            social.Exiler(tous, [c], [], exil.Jusqua + Jour);
            Assert.True(meurtrier.Exclusions[0].Levee);
            Assert.False(Bannissement.Separes(meurtrier, membres[0], exil.Jusqua + Jour));
            Assert.Contains(social.Journal, j => j.Texte.StartsWith("L'exil de H6 prend fin") && j.CauseId == evenement.Id);
        }
    }

    [Fact]
    public void Exil_UnePersonneLumineusePlaide_EtPeutFaireRenoncerLeGroupe()
    {
        var plaidoyers = 0;
        var abandons = 0;
        for (ulong graine = 1; graine <= 30; graine++)
        {
            var social = new VieSociale(new Alea(graine));
            var c = new Communaute { Id = 1, Nom = "Communauté de H1", FondeeLe = 0, FigureId = 1 };
            social.Communautes = [c];
            var membres = Enumerable.Range(1, 7).Select(i => Humain(i, i % 2 == 0 ? Sexe.Homme : Sexe.Femme, agreabilite: 0.7)).ToList();
            var voleur = membres[5];
            var juste = membres[6];
            juste.Karma = 120;
            juste.Personnalite = juste.Personnalite with { Extraversion = 0.8, Moral = TraitsMoraux.Moyens with { Compassion = 0.9 } };
            foreach (var m in membres) m.CommunauteId = 1;
            foreach (var m in membres.Take(5))
            {
                m.Griefs.Add(new Grief(voleur.Id, MotifGrief.Vol, 90, 90, 55));
                Lier(m, voleur, -60);
                Lier(m, juste, 60);
            }
            Lier(juste, voleur, 30);
            var groupes = new Dictionary<int, List<Humain>> { [1] = membres };
            for (var semaine = 0; semaine < 100 && voleur.Exclusions.Count == 0; semaine++)
            {
                social.Exiler(membres, [c], groupes, Maintenant + semaine * 7 * Jour);
                if (social.Journal.Any(j => j.Texte.Contains("l'exil est abandonné"))) break;
            }
            var plaidoyer = social.Journal.Where(j => j.Texte.Contains("H7 plaide pour")).ToList();
            plaidoyers += plaidoyer.Count > 0 ? 1 : 0;
            if (plaidoyer.Any(j => j.Texte.Contains("l'exil est abandonné")))
            {
                abandons++;
                Assert.Empty(voleur.Exclusions);
                Assert.True(plaidoyer.Last().Significatif);
                Assert.Contains(voleur.Memoire.Jardin, x => x.Type == TypeIntime.Joie && x.Texte.Contains("H7 m'a défendu"));
            }
            // Plaider fait monter le karma, une fois par saison envers une même personne.
            if (plaidoyer.Count > 0) Assert.Equal(Regard.Plaider, plaidoyer[0].Karma);
        }
        Assert.True(plaidoyers >= 15, $"{plaidoyers} plaidoyers sur 30");
        Assert.InRange(abandons, 1, plaidoyers - 1);
    }

    // ───────────── Maladies et débat ─────────────

    [Fact]
    public void Maladies_NommeesSelonLePaysLaSaisonEtLaPopulation()
    {
        var alea = new Alea(2);
        var h = Humain(1);
        var marais = Enumerable.Range(0, 5000).Select(_ => Maladies.Choisir(h, Biome.Marais, Saison.Ete, 100, Maintenant, alea)).ToList();
        Assert.Contains(Maladie.Paludisme, marais);
        Assert.DoesNotContain(Maladie.Rougeole, marais);
        Assert.DoesNotContain(Maladie.Tuberculose, marais);
        var hiver = Enumerable.Range(0, 5000).Select(_ => Maladies.Choisir(h, Biome.Desert, Saison.Hiver, 100, Maintenant, alea)).ToList();
        Assert.DoesNotContain(Maladie.Paludisme, hiver);
        Assert.Equal(Maladie.Pneumonie, hiver.GroupBy(m => m).MaxBy(g => g.Count())!.Key);
        Assert.Contains(Maladie.Rougeole, Enumerable.Range(0, 5000).Select(_ => Maladies.Choisir(h, Biome.Desert, Saison.Hiver, 300_000, Maintenant, alea)));

        Assert.Equal("d'une pneumonie", Comportement.De("une pneumonie"));
        Assert.Equal("du paludisme", Comportement.De("le paludisme"));
        Assert.Equal("de vers intestinaux", Comportement.De("des vers intestinaux"));
        Assert.Equal("de la tuberculose", Comportement.De("la tuberculose"));
    }

    [Fact]
    public void Debat_DeuxJoursAQuatreSemaines_PlusLongDansUnGrandGroupe_PlusCourtSousLaColere()
    {
        var social = new VieSociale(new Alea(8));
        var instigateur = Humain(1, Sexe.Homme);
        double Jours(int adultes, bool colere) => social.DelaiDebat(adultes, colere, instigateur) / (double)Jour;
        var petits = Enumerable.Range(0, 2000).Select(_ => Jours(5, false)).ToList();
        var grands = Enumerable.Range(0, 2000).Select(_ => Jours(40, false)).ToList();
        var colere = Enumerable.Range(0, 2000).Select(_ => Jours(40, true)).ToList();
        Assert.All(petits.Concat(grands).Concat(colere), j => Assert.InRange(j, 2, 28));
        Assert.True(petits.Distinct().Count() > 100, "le délai varie");
        Assert.True(grands.Average() > petits.Average() * 2);
        Assert.True(colere.Average() < grands.Average() * 0.75);
    }

    // ───────────── Sauvegarde ─────────────

    [Fact]
    public void AttraitsExclusionsEtMoeurs_SeSauvegardent_EtUnMondeDeLaVersion15LesRetrouve()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-attirance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(Jour);
            var etat = monde.Etat;
            var h = etat.Humains![0];
            h.Apparence = new Apparence(0.1, 0.2, 1.3, 0.4, 0.5);
            h.Orientation = new Orientation(0.6, 0.7);
            h.Lucidite = 0.45;
            h.Maladie = Maladie.Paludisme;
            h.Exclusions.Add(new Exclusion(TypeExclusion.Communaute, 1, 100, 0, 7, "pour ses amours avec « quelqu'un »'); DROP TABLE exclusions;--"));
            h.Exclusions.Add(new Exclusion(TypeExclusion.Famille, 2, 200, 300, 8, "pour sa violence", Levee: true));
            var c = new Communaute
            {
                Id = 1, Nom = "Communauté de A", FondeeLe = etat.Secondes, FigureId = 1, ToleranceMoeurs = 0.23,
                UsageMoeurs = UsageMoeurs.Reprouvees, AffaireMoeursId = 9, Ideal = new IdealBeaute(0.4, 0.6, 0.7), IdealNote = 1,
                Projet = new ProjetGuerre { CibleId = 1, InstigateurId = 1, Depuis = 5, EvenementId = 1, Raison = "r", DebatLe = 5 + 9 * Jour },
            };
            etat = etat with { Communautes = [c] };

            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            var rh = relu.Humains![0];
            Assert.Equal((h.Apparence, h.Orientation, h.Lucidite, h.Maladie), (rh.Apparence, rh.Orientation, rh.Lucidite, rh.Maladie));
            Assert.Equal(h.Exclusions, rh.Exclusions);
            var rc = Assert.Single(relu.Communautes!);
            Assert.Equal((0.23, UsageMoeurs.Reprouvees, 9L, c.Ideal, 1), (rc.ToleranceMoeurs, rc.UsageMoeurs, rc.AffaireMoeursId, rc.Ideal, rc.IdealNote));
            Assert.Equal(c.Projet.DebatLe, rc.Projet!.DebatLe);
            // Les autres humains gardent les traits tirés à leur naissance.
            Assert.Equal(etat.Humains.Select(x => x.Orientation), relu.Humains!.Select(x => x.Orientation));

            // Un monde de la version 15 se met à jour : traits tirés du numéro de chacun, aucune exclusion, mœurs à établir.
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion15);
            var ancien = new DepotMonde(fichier).Charger()!;
            var ah = ancien.Humains![0];
            var attendu = Attirance.De(ah.Id, ah.Sexe, ah.Personnalite.Ouverture);
            Assert.Equal((attendu.Apparence, attendu.Orientation, attendu.Lucidite, Maladie.Aucune), (ah.Apparence, ah.Orientation, ah.Lucidite, ah.Maladie));
            Assert.All(ancien.Humains!, x => Assert.Empty(x.Exclusions));
            Assert.Equal(-1, Assert.Single(ancien.Communautes!).ToleranceMoeurs);
            Assert.Equal(0, ancien.Communautes![0].Projet!.DebatLe - c.Projet.DebatLe); // le projet garde sa date (JSON)
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
