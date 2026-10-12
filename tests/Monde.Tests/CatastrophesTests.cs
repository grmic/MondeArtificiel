using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Serveur.Hubs;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Catastrophes naturelles : géologie, séismes, feux, crues, vagues géantes, froid, maladies, sauvegarde, pause et hub.</summary>
public sealed class CatastrophesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Heure = Calendrier.SecondesParHeure;
    private const long Jour = Calendrier.SecondesParJour;

    /// <summary>
    /// Monde neuf de la graine 42, avec tous les humains déplacés en (x, y) s'il est donné. <paramref name="anneeNormale"/> :
    /// des pluies ordinaires partout (les feux se propagent mal pendant une année très arrosée).
    /// </summary>
    private static MondeSimule Monde((int X, int Y)? lieu = null, bool anneeNormale = false)
    {
        var etat = MondeSimule.Nouveau(42);
        if (anneeNormale) etat = etat with { Pluies = new MondeSimule(Carte, etat).Etat.Pluies!.Select(_ => 1.0).ToArray() };
        if (lieu is not { } l) return new MondeSimule(Carte, etat);
        var humains = new MondeSimule(Carte, etat).Etat.Humains!.ToList();
        foreach (var h in humains) (h.X, h.Y) = (l.X, l.Y);
        return new MondeSimule(Carte, etat with { Humains = humains });
    }

    /// <summary>Première case de terre (en partant du centre) qui satisfait la condition.</summary>
    private static (int X, int Y) Case(Func<int, int, bool> condition)
    {
        int cx = Carte.Largeur / 2, cy = Carte.Hauteur / 2;
        for (var r = 0; r < Carte.Largeur; r++)
        {
            for (var y = cy - r; y <= cy + r; y++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != r || x < 0 || y < 0 || x >= Carte.Largeur || y >= Carte.Hauteur) continue;
                    if (condition(x, y)) return (x, y);
                }
            }
        }
        throw new InvalidOperationException("Aucune case ne convient.");
    }

    private static bool PresDe(int x, int y, Func<Biome, bool> condition)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < Carte.Largeur && ny < Carte.Hauteur && condition(Carte.BiomeEn(nx, ny))) return true;
            }
        }
        return false;
    }

    // ───────────── Géologie ─────────────

    [Fact]
    public void Geologie_TireeDeLaGraine_SansChangerLaCarte_EtVolcansSurLaTerre()
    {
        var a = Geologie.Generer(Carte);
        var b = Geologie.Generer(GenerateurContinent.Generer(42));
        Assert.Equal(a.Contexte, b.Contexte);
        Assert.Equal(a.Failles, b.Failles);
        Assert.Equal(a.Volcans, b.Volcans);
        Assert.Equal(GenerateurContinent.Generer(42).Biomes, Carte.Biomes);

        var contextes = new HashSet<ContexteTectonique>();
        for (ulong graine = 1; graine <= 15; graine++)
        {
            var carte = GenerateurContinent.Generer(graine);
            var g = Geologie.Generer(carte);
            contextes.Add(g.Contexte);
            Assert.All(g.Volcans, v => Assert.True(carte.Altitudes[carte.Indice(v.X, v.Y)] >= Carte.NiveauMer, "volcan dans la mer"));
            Assert.InRange(g.MagnitudeMax, 6, 9.5);
            if (g.Contexte != ContexteTectonique.Stable) Assert.NotEmpty(g.Failles);
        }
        Assert.Equal(3, contextes.Count);
    }

    // ───────────── Séisme ─────────────

    [Fact]
    public void Seisme_ProvoqueParLEntite_EffraieLesHumains_EtSeJournaliseAvecSonOrigine()
    {
        var lieu = Case((x, y) => Climat.EstPraticable(Carte.BiomeEn(x, y)));
        var monde = Monde(lieu);

        var id = monde.Declencher(TypeCatastrophe.Seisme, lieu.X, lieu.Y, 7.2);

        Assert.True(id > 0);
        var annonce = monde.EvenementEnMemoire(id)!;
        Assert.Equal(TypeEvenement.Catastrophe, annonce.Type);
        Assert.Equal(Origine.Entite, annonce.Origine);
        var seisme = Assert.Single(monde.CatastrophesEnMemoire());
        Assert.Equal(id, seisme.Id);
        Assert.Equal(TypeCatastrophe.Seisme, seisme.Type);
        Assert.Equal(Origine.Entite, seisme.Origine);

        // Tous sont à l'épicentre : chacun a eu peur (ou pire), et sa peur cite le séisme.
        var (_, humains) = monde.InstantaneHumains();
        Assert.All(humains, h => Assert.True(h.Peur > 0 || !h.EstVivant || h.EstBebe(monde.Secondes)));
        Assert.Contains(humains, h => h.PeurEvenementId == id && h.PeurType == TypeCatastrophe.Seisme);
        Assert.Contains(monde.PrendreAlertes(), a => a.CatastropheId == id && a.Grave);

        // La peur s'apaise de jour en jour ; la secousse est finie au plus tard après la dernière réplique (un mois au plus).
        // (Une forte réplique peut raviver la peur entre-temps.)
        var peurAvant = monde.InstantaneHumains().Humains.Where(h => h.EstVivant).Sum(h => h.Peur);
        monde.AvancerDe(31 * Jour);
        Assert.True(monde.InstantaneHumains().Humains.Where(h => h.EstVivant).Sum(h => h.Peur) < peurAvant / 2);
        Assert.NotNull(monde.CatastrophesEnMemoire().Single(c => c.Id == id).Fin);
    }

    [Fact]
    public void Morts_DUneCatastrophe_CitentLaCatastrophe_EtSontEndeuilles()
    {
        // Un séisme très fort à l'épicentre tue presque à coup sûr quelques-uns des 66 humains.
        var lieu = Case((x, y) => Climat.EstPraticable(Carte.BiomeEn(x, y)) && Carte.BiomeEn(x, y) == Biome.Montagne);
        var monde = Monde(lieu);
        var id = monde.Declencher(TypeCatastrophe.Seisme, lieu.X, lieu.Y, 9);
        var morts = monde.InstantaneHumains().Humains.Where(h => !h.EstVivant).ToList();
        Assert.NotEmpty(morts);
        Assert.All(morts, m =>
        {
            Assert.Contains(m.CauseDeMort, new[] { CauseDeMort.Eboulement, CauseDeMort.Accident });
            Assert.Equal(id, monde.EvenementEnMemoire(m.MortEvenementId)!.CauseId);
        });
        Assert.True(monde.CatastrophesEnMemoire().Single(c => c.Id == id).Morts == morts.Count);
    }

    // ───────────── Feu ─────────────

    [Fact]
    public void Incendie_SePropage_AbimeLaNature_EtFaitFuirCeuxQuiSontDessus()
    {
        var foyer = Case((x, y) => Carte.BiomeEn(x, y) is Biome.ForetDeConiferes or Biome.PlaineDeFougeres
                                   && Carte.BiomeEn(x + 1, y) == Carte.BiomeEn(x, y) && Carte.BiomeEn(x, y + 1) == Carte.BiomeEn(x, y));
        var monde = Monde(foyer, anneeNormale: true);
        var id = monde.Declencher(TypeCatastrophe.Incendie, foyer.X, foyer.Y, 0);
        Assert.True(id > 0);
        Assert.True(monde.Etat.DegatsNature![Carte.Indice(foyer.X, foyer.Y)] > 0);

        monde.AvancerDe(Heure);
        // Ceux qui étaient au cœur du feu l'ont fui, ou y sont morts brûlés (et leur mort cite l'incendie).
        foreach (var h in monde.InstantaneHumains().Humains)
        {
            if (h.EstVivant) Assert.True(h.Activite == Activite.Fuir || (h.X, h.Y) != foyer || h.EstBebe(monde.Secondes), $"{h.Nom} reste dans le feu");
            else Assert.Equal(CauseDeMort.Brulure, h.CauseDeMort);
        }

        // Un feu ne prend pas toujours (vent, humidité, nuit, printemps) : en plein été, par une année ordinaire, on allume
        // quarante foyers ; au moins un se propage, et toutes les terres brûlées sont abîmées.
        var ete = Monde(anneeNormale: true);
        ete.AvancerDe(Calendrier.JoursParSaison * Jour + 12 * Heure);
        var feux = new List<long>();
        for (var essai = 0; essai < 40; essai++)
        {
            var f = ete.Declencher(TypeCatastrophe.Incendie, foyer.X + 6 * (essai % 16), foyer.Y + 6 * (essai / 16), 0);
            if (f > 0) feux.Add(f);
        }
        Assert.NotEmpty(feux);
        ete.AvancerDe(Jour);
        var propage = false;
        foreach (var f in feux)
        {
            var feu = ete.CatastrophesEnMemoire().Single(c => c.Id == f);
            propage |= feu.Cases.Count + feu.Front.Count > 1;
            Assert.All(feu.Cases, i => Assert.True(ete.Etat.DegatsNature![i] > 0));
        }
        Assert.True(propage, "aucun feu ne s'est propagé");
    }

    [Fact]
    public void Incendie_SurLEauOuLeDesertNu_NeProvoqueRien()
    {
        var monde = Monde();
        var mer = Case((x, y) => Carte.BiomeEn(x, y) == Biome.Ocean);
        Assert.Equal(0, monde.Declencher(TypeCatastrophe.Incendie, mer.X, mer.Y, 0));
        Assert.Empty(monde.CatastrophesEnMemoire());
    }

    // ───────────── Eau ─────────────

    [Fact]
    public void Tsunami_NoieLesTerresBassesDuLittoral_PuisLesHumainsPresents()
    {
        var cote = Case((x, y) => Climat.EstPraticable(Carte.BiomeEn(x, y)) && PresDe(x, y, b => b == Biome.Ocean)
                                  && Carte.Altitudes[Carte.Indice(x, y)] <= Carte.NiveauMer + 1);
        var monde = Monde(cote);
        var id = monde.Declencher(TypeCatastrophe.Tsunami, cote.X, cote.Y, 10);
        var vague = monde.CatastrophesEnMemoire().Single(c => c.Id == id);
        Assert.Contains(Carte.Indice(cote.X, cote.Y), vague.Cases);
        Assert.All(vague.Cases, i => Assert.True(Carte.Altitudes[i] <= Carte.NiveauMer + 1, "case trop haute pour une vague de 10 m"));

        monde.AvancerDe(2 * Heure);
        vague = monde.CatastrophesEnMemoire().Single(c => c.Id == id);
        Assert.Equal(1, vague.Phase);
        Assert.Equal(vague.Morts, monde.InstantaneHumains().Humains.Count(h => h.CauseDeMort == CauseDeMort.Noyade));
        Assert.True(vague.Temoins.Count > 0);
    }

    [Fact]
    public void Crue_CouvreLesRivesDesRivieres()
    {
        var rive = Case((x, y) => Climat.EstPraticable(Carte.BiomeEn(x, y)) && PresDe(x, y, Biomes.EstEauDouce));
        var monde = Monde();
        var id = monde.Declencher(TypeCatastrophe.Crue, rive.X, rive.Y, 2);
        Assert.True(id > 0);
        var crue = monde.CatastrophesEnMemoire().Single(c => c.Id == id);
        Assert.NotEmpty(crue.Cases);
        Assert.All(crue.Cases, i => Assert.True(Climat.EstPraticable((Biome)Carte.Biomes[i])));
        Assert.True(crue.Duree >= 2 * Jour);
    }

    // ───────────── Climat ─────────────

    [Fact]
    public void VagueDeFroid_RefroiditToutLeContinent_PuisPrendFin()
    {
        var monde = Monde();
        var id = monde.Declencher(TypeCatastrophe.VagueDeFroid, 0, 0, 8);
        Assert.Equal(-8, monde.AnomalieTemperature, 3);
        monde.AvancerDe(8 * Jour);
        Assert.Equal(0, monde.AnomalieTemperature, 3);
        Assert.NotNull(monde.CatastrophesEnMemoire().Single(c => c.Id == id).Fin);
    }

    // ───────────── Maladies ─────────────

    [Fact]
    public void MaladieNouvelle_CommenceParLePlusProche_PuisOnEnGueritImmuniseOuOnEnMeurt()
    {
        var monde = Monde();
        var (_, humains) = monde.InstantaneHumains();
        var premier = humains[0];
        var id = monde.Declencher(TypeCatastrophe.Epidemie, premier.X, premier.Y, 0);

        var maladie = Assert.Single(monde.Pathogenes());
        Assert.Equal(id, maladie.EvenementId);
        Assert.Equal(Origine.Entite, monde.EvenementEnMemoire(id)!.Origine);
        var malade = monde.InstantaneHumains().Humains.Single(h => h.InfectionId == maladie.Id);
        Assert.InRange(maladie.Letalite, 0.002, 0.7);

        monde.AvancerDe(80 * Jour);
        malade = monde.InstantaneHumains().Humains[malade.Id - 1];
        if (malade.EstVivant)
        {
            Assert.Equal(0, malade.InfectionId);
            Assert.Contains(malade.Immunites, i => i.PathogeneId == maladie.Id);
        }
        else
        {
            Assert.Equal(CauseDeMort.Maladie, malade.CauseDeMort);
        }
    }

    // ───────────── Sauvegarde ─────────────

    [Fact]
    public void Sauvegarde_GardeCatastrophesMaladiesPeurEtDegats_EtLeMondeRechargeContinueALIdentique()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-catastrophes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var foyer = Case((x, y) => Carte.BiomeEn(x, y) is Biome.ForetDeConiferes or Biome.PlaineDeFougeres);
            var monde = Monde();
            monde.Declencher(TypeCatastrophe.Incendie, foyer.X, foyer.Y, 0);
            var premier = monde.InstantaneHumains().Humains[0];
            monde.Declencher(TypeCatastrophe.Epidemie, premier.X, premier.Y, 0);
            // Sous la magnitude 5,8, pas de forte réplique : le séisme est fini au bout d'une heure.
            monde.Declencher(TypeCatastrophe.Seisme, premier.X, premier.Y, 5.5);
            monde.Declencher(TypeCatastrophe.VagueDeFroid, 0, 0, 6);
            monde.AvancerDe(3 * Heure);

            var depot = new DepotMonde(Path.Combine(dossier, "monde.db"));
            var etat = monde.Etat;
            depot.Enregistrer(etat);
            var relu = depot.Charger()!;
            var recharge = new MondeSimule(Carte, relu);

            Assert.Equal(etat.DegatsNature, relu.DegatsNature);
            Assert.Equal(etat.Pathogenes, relu.Pathogenes);
            // Les catastrophes finies dont les suites durent encore (terres dévastées) sont rechargées aussi.
            Assert.Equal(etat.Catastrophes!.Count(c => c.Active || c.EnSuites(etat.Secondes)), relu.Catastrophes!.Count);
            for (var i = 0; i < etat.Humains!.Count; i++)
            {
                var (a, b) = (etat.Humains[i], relu.Humains![i]);
                Assert.Equal((a.Peur, a.PeurEvenementId, a.PeurType, a.InfectionId, a.SymptomesLe, a.GuerisonLe, a.InfectionEvenementId),
                    (b.Peur, b.PeurEvenementId, b.PeurType, b.InfectionId, b.SymptomesLe, b.GuerisonLe, b.InfectionEvenementId));
                Assert.Equal(a.Immunites, b.Immunites);
            }
            var feu = etat.Catastrophes!.Single(c => c.Type == TypeCatastrophe.Incendie);
            var feuRelu = relu.Catastrophes!.Single(c => c.Id == feu.Id);
            Assert.Equal(feu.Cases.Order(), feuRelu.Cases.Order());
            Assert.Equal(feu.Front.OrderBy(f => f.Key), feuRelu.Front.OrderBy(f => f.Key));
            Assert.Equal(feu.Temoins.Order(), feuRelu.Temoins.Order());
            Assert.Equal(-6, recharge.AnomalieTemperature, 3);

            // Le monde rechargé continue exactement comme l'original.
            monde.AvancerDe(2 * Jour);
            recharge.AvancerDe(2 * Jour);
            var (x, y) = (monde.Etat, recharge.Etat);
            Assert.Equal(x.Humains!.Select(h => (h.X, h.Y, h.Sante, h.Peur, h.InfectionId)), y.Humains!.Select(h => (h.X, h.Y, h.Sante, h.Peur, h.InfectionId)));
            Assert.Equal(x.DegatsNature, y.DegatsNature);
            Assert.Equal(x.Catastrophes!.Where(c => c.Active).Select(c => (c.Id, c.Cases.Count, c.Front.Count)),
                y.Catastrophes!.Where(c => c.Active).Select(c => (c.Id, c.Cases.Count, c.Front.Count)));

            // Les catastrophes terminées restent dans l'histoire enregistrée.
            depot.Enregistrer(monde.Etat);
            var histoire = depot.LireCatastrophes();
            Assert.Contains(histoire, c => c.Type == TypeCatastrophe.Seisme && c.Fin is not null);
            Assert.Equal(feu.Id, depot.LireCatastrophe(feu.Id)!.Id);
            Assert.Single(depot.LirePathogenes());
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void FichierVersion10_EstMigre_SansCatastrophes()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-v10-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fichier = Path.Combine(dossier, "monde.db");
            var monde = Monde();
            monde.AvancerDe(Jour);
            new DepotMonde(fichier).Enregistrer(monde.Etat);
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion10);

            var etat = new DepotMonde(fichier).Charger()!;
            Assert.Null(etat.Catastrophes);
            Assert.Null(etat.Pathogenes);
            Assert.Null(etat.DegatsNature);
            Assert.All(etat.Humains!, h => Assert.Equal(0, h.Peur));
            var recharge = new MondeSimule(Carte, etat);
            recharge.AvancerDe(Jour);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    // ───────────── Pause ─────────────

    [Fact]
    public void CatastropheQuiMenaceDesHumains_MetLeMondeEnPause_SiLOptionEstChoisie()
    {
        var lieu = Case((x, y) => Climat.EstPraticable(Carte.BiomeEn(x, y)));
        foreach (var pause in new[] { true, false })
        {
            var monde = Monde(lieu);
            monde.PauseSurCatastrophe = pause;
            monde.ChangerFacteur(1000);
            monde.Declencher(TypeCatastrophe.Seisme, lieu.X, lieu.Y, 6);
            monde.Avancer(TimeSpan.FromSeconds(1));
            Assert.Equal(pause ? 0 : 1000, monde.Facteur);
        }
    }

    // ───────────── Hub ─────────────

    [Fact]
    public async Task Hub_ListeDetailGeologieMaladies_EtLEntitePeutProvoquer()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var horloge = await connexion.InvokeAsync<HorlogeDto>("Horloge");
        Assert.True(horloge.PauseCatastrophe);
        horloge = await connexion.InvokeAsync<HorlogeDto>("ChoisirPauseCatastrophe", false);
        Assert.False(horloge.PauseCatastrophe);

        var geologie = await connexion.InvokeAsync<GeologieDto>("Geologie");
        Assert.False(string.IsNullOrWhiteSpace(geologie.Description));

        var humains = await connexion.InvokeAsync<List<HumainDto>>("Humains");
        var h = humains[0];
        var id = await connexion.InvokeAsync<long>("Declencher", new DemandeCatastrophe("seisme", h.X, h.Y, 6.8));
        Assert.True(id > 0);
        var liste = await connexion.InvokeAsync<List<CatastropheDto>>("Catastrophes", null, 50);
        var seisme = Assert.Single(liste);
        Assert.Equal("seisme", seisme.Type);
        Assert.Equal("entite", seisme.Origine);
        Assert.Contains("6,8", seisme.Intensite);
        var detail = await connexion.InvokeAsync<DetailCatastropheDto?>("Catastrophe", id);
        Assert.Equal(id, detail!.Catastrophe.Id);
        Assert.Null(await connexion.InvokeAsync<DetailCatastropheDto?>("Catastrophe", 999_999L));

        await connexion.InvokeAsync<long>("Declencher", new DemandeCatastrophe("epidemie", h.X, h.Y, 0));
        var maladies = await connexion.InvokeAsync<List<MaladieDto>>("Maladies");
        Assert.Equal(1, Assert.Single(maladies).Malades);

        // Le journal sait filtrer les catastrophes.
        var journal = await connexion.InvokeAsync<List<EvenementDto>>("Journal", new FiltreJournal(["catastrophe"]));
        Assert.Contains(journal, e => e.Id == id);

        // Entrées hostiles : type inconnu, lieu hors carte, intensité non finie.
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<long>("Declencher", new DemandeCatastrophe("'; DROP TABLE journal;--", 1, 1, 1)));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<long>("Declencher", new DemandeCatastrophe("seisme", -1, 5, 6)));
        await Assert.ThrowsAsync<HubException>(() => connexion.InvokeAsync<List<CatastropheDto>>("Catastrophes", null, 100_000));
    }
}
