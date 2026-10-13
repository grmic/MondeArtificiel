using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

public sealed class HumainsTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static MondeSimule NouveauMonde() => new(Carte, MondeSimule.Nouveau(42));

    /// <summary>Résumé texte de tout l'état, pour comparer deux mondes exactement.</summary>
    private static string Empreinte(MondeSimule monde)
    {
        var etat = monde.Etat;
        var humains = string.Join("\n", etat.Humains!.Select(h =>
            $"{h.Id} {h.X} {h.Y} {h.Faim:R} {h.Soif:R} {h.Fatigue:R} {h.Froid:R} {h.Sante:R} {h.Activite} {h.CibleX} {h.CibleY} {h.Detour} {h.MaladeJusqua} {h.MortLe} {h.CauseDeMort} "
            + $"{string.Join(";", h.Memoire.Lieux)} {string.Join(";", h.Liens)} {h.Humeur:R} {h.Chagrin:R} {h.CommunauteId} {h.DeprimeDepuis} {string.Join(";", h.Memoire.Pensees)} {h.Reserves:R} {h.Biologie}"));
        return $"{etat.Secondes} {etat.Pas} {string.Join(",", etat.Alea!)}\n{string.Join(",", etat.Gibier!)}\n{string.Join(",", etat.Plantes!)}\n"
               + $"{string.Join(",", etat.Pluies!.Select(p => p.ToString("R")))}\n{string.Join(";", etat.Relations!)}\n{humains}";
    }

    [Fact]
    public void Population_Compte33CouplesSurLeursEmplacements()
    {
        var humains = NouveauMonde().Etat.Humains!;

        Assert.Equal(66, humains.Count);
        Assert.Equal(66, humains.Select(h => h.Nom).Distinct().Count());
        Assert.All(humains, h =>
        {
            var partenaire = humains[h.PartenaireId - 1];
            Assert.Equal(h.Id, partenaire.PartenaireId);
            Assert.NotEqual(h.Sexe, partenaire.Sexe);
            Assert.Equal(h.Emplacement, partenaire.Emplacement);
            var emplacement = Carte.Emplacements[h.Emplacement - 1];
            Assert.Equal((emplacement.X, emplacement.Y), (h.X, h.Y));
            Assert.InRange(h.AgeEnAnnees(6 * Calendrier.SecondesParHeure), GenerateurPopulation.AgeMin, GenerateurPopulation.AgeMax);
            Assert.True(h.EstVivant);
        });
    }

    [Fact]
    public void MemeGraine_MemeHistoire()
    {
        var a = NouveauMonde();
        var b = NouveauMonde();
        a.AvancerDe(30 * Calendrier.SecondesParJour);
        b.AvancerDe(30 * Calendrier.SecondesParJour);
        Assert.Equal(Empreinte(a), Empreinte(b));
    }

    [Fact]
    public void SauvegardeEtRechargement_NeChangentPasLaSuite()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-suite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var continu = NouveauMonde();
            var interrompu = NouveauMonde();
            continu.AvancerDe(10 * Calendrier.SecondesParJour);
            interrompu.AvancerDe(10 * Calendrier.SecondesParJour);

            // Arrêt et reprise du monde « interrompu » via un fichier SQLite.
            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            depot.Enregistrer(interrompu.Etat);
            var repris = new MondeSimule(Carte, new DepotMonde(Path.Combine(dossier, "monde.db")).Charger()!);

            continu.AvancerDe(20 * Calendrier.SecondesParJour);
            repris.AvancerDe(20 * Calendrier.SecondesParJour);
            Assert.Equal(Empreinte(continu), Empreinte(repris));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void SurDeuxAns_NaissancesMortsEtRegles()
    {
        var monde = NouveauMonde();
        for (var jour = 0; jour < 2 * Calendrier.JoursParAn; jour++)
        {
            monde.AvancerDe(Calendrier.SecondesParJour);
            var (maintenant, humains) = monde.InstantaneHumains();
            Assert.All(humains, h =>
            {
                Assert.True(Climat.EstPraticable(Carte.BiomeEn(h.X, h.Y)), $"{h.Nom} est dans l'eau en ({h.X}, {h.Y})");
                Assert.InRange(h.Sante, 0, 100);
                Assert.InRange(h.Faim, 0, 100);
                // Un bébé vivant est dans les bras de sa mère vivante, si elle y est attachée (sinon elle peut le délaisser).
                if (h.EstVivant && h.EstBebe(maintenant) && humains[h.MereId - 1].EstVivant
                    && humains[h.MereId - 1].Personnalite.Attachement > 0.5)
                {
                    var mere = humains[h.MereId - 1];
                    Assert.Equal((mere.X, mere.Y), (h.X, h.Y));
                }
            });
        }

        var final = monde.Etat.Humains!;
        var vivants = final.Count(h => h.EstVivant);
        Assert.InRange(vivants, 1, final.Count - 1);
        Assert.All(final.Where(h => !h.EstVivant), h =>
        {
            Assert.NotEqual(CauseDeMort.Aucune, h.CauseDeMort);
            Assert.Equal(Activite.Mort, h.Activite);
            Assert.Equal(0, h.Sante);
        });

        // Des enfants sont nés, de parents adultes de sexes différents qui ne sont pas apparentés.
        var enfants = final.Where(h => h.MereId != 0).ToList();
        Assert.NotEmpty(enfants);
        Assert.All(enfants, e =>
        {
            var mere = final[e.MereId - 1];
            var pere = final[e.PereId - 1];
            Assert.Equal(Sexe.Femme, mere.Sexe);
            Assert.Equal(Sexe.Homme, pere.Sexe);
            Assert.InRange(mere.AgeEnAnnees(e.Naissance), 15, 46);
            Assert.InRange(e.Personnalite.Curiosite, 0, 1);
            Assert.Same(e, final[e.Id - 1]);
        });
    }

    [Fact]
    public void HumainsDeCouplesDifferents_PeuventAvoirUnEnfant()
    {
        // Règle de zem : une femme et un homme de deux couples différents, dont les compagnons sont morts,
        // se retrouvent au même endroit.
        var depart = NouveauMonde().Etat;
        var e = Carte.Emplacements[0];
        Humain Copie(int index, int x, int y, long? mort = null)
        {
            var h = depart.Humains![index].Copier();
            (h.X, h.Y, h.Desir, h.MortLe) = (x, y, 100, mort);
            // Tous deux attirés par l'autre sexe (l'orientation tirée au hasard pourrait en décider autrement).
            h.Orientation = h.Sexe == Sexe.Femme ? new Orientation(0.9, 0) : new Orientation(0, 0.9);
            if (mort is not null) (h.Sante, h.CauseDeMort, h.Activite) = (0, CauseDeMort.Faim, Activite.Mort);
            return h;
        }
        var femme = Copie(0, e.X, e.Y);        // couple n° 1
        var sonCompagnon = Copie(1, e.X, e.Y, mort: 1);
        var homme = Copie(3, e.X, e.Y);        // couple n° 2
        var saCompagne = Copie(2, e.X, e.Y, mort: 1);
        Assert.NotEqual(femme.Emplacement, homme.Emplacement);

        var monde = new MondeSimule(Carte, depart with { Humains = [femme, sonCompagnon, saCompagne, homme] });
        var enceinte = false;
        for (var jour = 0; jour < 240 && !enceinte; jour++)
        {
            monde.AvancerDe(Calendrier.SecondesParJour);
            var f = monde.Etat.Humains![0];
            enceinte = f.EstEnceinte && f.PereEnfantId == homme.Id;
        }

        Assert.True(enceinte, "la femme aurait dû attendre un enfant de l'homme de l'autre couple");
        var final = monde.Etat.Humains!;
        Assert.Equal(homme.Id, final[0].PartenaireId); // deux humains seuls forment un nouveau couple
    }

    [Fact]
    public void Climat_HiverPlusFroidQueLEte_EtNuitPlusFroideQueLeJour()
    {
        var e = Carte.Emplacements[0];
        double T(int jour, int heure) => Climat.Temperature(Carte, e.X, e.Y, jour * Calendrier.SecondesParJour + heure * Calendrier.SecondesParHeure);
        Assert.True(T(315, 14) < T(135, 14)); // milieu de l'hiver contre milieu de l'été
        Assert.True(T(135, 2) < T(135, 14));
    }

    [Fact]
    public void Ressources_ConsommeesPuisRepoussent()
    {
        var eco = new Ecosysteme(Carte);
        var i = Array.FindIndex(Carte.Plantes, p => p >= 50);
        var initial = eco.Plantes[i];

        Assert.Equal(8, Ecosysteme.Prelever(eco.Plantes, i, 8));
        Assert.Equal(initial - 800, eco.Plantes[i]);
        for (var jour = 0; jour < 60; jour++) eco.Journee(jour * Calendrier.SecondesParJour);
        Assert.True(eco.Plantes[i] > initial - 800);
        Assert.True(eco.Plantes[i] <= initial);
    }
}
