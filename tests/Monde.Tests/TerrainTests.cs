using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.31.0 et 0.31.1 : roches, sols, gisements, relief, grottes et eaux.</summary>
public sealed class TerrainTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    [Fact]
    public void Terrain_MemeCarte_MemeTerrain()
    {
        var a = Carte.Terrain;
        var b = Terrain.Calculer(GenerateurContinent.Generer(42));
        Assert.Equal(a.Roches, b.Roches);
        Assert.Equal(a.Sols, b.Sols);
        Assert.Equal(a.FacteurSol, b.FacteurSol);
        Assert.Equal(a.Debit, b.Debit);
        Assert.Equal(a.Courant, b.Courant);
        Assert.Equal(a.Pierres, b.Pierres);
        Assert.Equal(a.Gisements, b.Gisements);
        Assert.Equal(a.Grottes, b.Grottes);
        Assert.Equal(a.Sources, b.Sources);
    }

    [Fact]
    public void FacteurSol_EnMoyenneUnSurChaqueTerrain()
    {
        var t = Carte.Terrain;
        foreach (var groupe in Enumerable.Range(0, Carte.Biomes.Length).Where(i => Carte.Plantes[i] > 0).GroupBy(i => Carte.Biomes[i]))
        {
            if (groupe.Count() < 50) continue;
            var moyenne = groupe.Average(i => t.FacteurSol[i]);
            Assert.InRange(moyenne, 0.9, 1.1);
            Assert.All(groupe, i => Assert.InRange(t.FacteurSol[i], 0.4f, 1.8f));
        }
    }

    [Fact]
    public void GrottesSourcesGisements_PresentsEtRepertories()
    {
        var t = Carte.Terrain;
        Assert.NotEmpty(t.Grottes);
        Assert.NotEmpty(t.Sources);
        Assert.NotEmpty(t.Gisements);
        Assert.All(t.Grottes, g => Assert.Same(g, t.GrotteA(g.X, g.Y)));
        Assert.All(t.Sources, s => Assert.Same(s, t.SourceA(s.X, s.Y)));
        Assert.All(t.Gisements, g => Assert.Same(g, t.GisementA(g.X, g.Y)));
        // Une grotte abrite mieux qu'une forêt ; un abri sous roche, moins qu'une grotte.
        Assert.All(t.Grottes, g => Assert.True(Climat.Abri(g) > (g.SousRoche ? 0.4 : 0.6)));
        Assert.Equal(0, Climat.Abri((Grotte?)null));
    }

    [Fact]
    public void Courant_GrossitAvecLaCrue_NulHorsDesRivieres()
    {
        var t = Carte.Terrain;
        var rivieres = Enumerable.Range(0, t.Debit.Length).Where(i => t.Debit[i] > 0).ToList();
        Assert.NotEmpty(rivieres);
        Assert.All(rivieres, i => Assert.True(t.CourantDuJour(i, 2) > t.CourantDuJour(i, 1)));
        Assert.All(Enumerable.Range(0, t.Debit.Length).Where(i => t.Debit[i] == 0), i => Assert.Equal(0, t.CourantDuJour(i, 2)));
        // Hautes eaux au printemps, basses eaux en été.
        Assert.True(Terrain.FacteurSaison(30 * Calendrier.SecondesParJour) > Terrain.FacteurSaison(120 * Calendrier.SecondesParJour));
    }

    [Fact]
    public void Gisements_RemarquesEnPassant_RetenusEtNotesUneFoisCommePremiers()
    {
        // Tout le monde campe au bord d'un gisement bien visible (sel, soufre ou bitume à fleur de sol).
        var gisement = Carte.Terrain.Gisements.First(g => g.Profondeur == Enfouissement.Affleurant && g.Type is Gite.Sel or Gite.Soufre or Gite.Bitume
                                                         && Climat.EstPraticable(Carte.BiomeEn(g.X, g.Y)));
        var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
        foreach (var h in etat.Humains!) (h.X, h.Y) = (gisement.X, gisement.Y);
        var monde = new MondeSimule(Carte, etat) { Coeurs = 1 };
        monde.AvancerDe(10 * Calendrier.SecondesParJour);
        var vus = monde.Etat.JournalNonEnregistre!.Where(e => e.Recit?.Cle.StartsWith("gis.") == true).ToList();
        Assert.NotEmpty(vus);
        var gisements = Carte.Terrain.Gisements;
        Assert.All(vus, e => Assert.Equal(TypeEvenement.Decouverte, e.Type));
        // Le premier à voir un gisement est noté comme un fait marquant ; tant qu'il s'en souvient, ceux qui le revoient non.
        Assert.True(vus.Count(e => e.Significatif) <= gisements.Count(g => g.Profondeur != Enfouissement.Profond));
        Assert.Contains(vus, e => e.Significatif);
        Assert.Contains(vus, e => !e.Significatif);
        Assert.Contains(monde.Etat.Humains!, h => h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.Gisement));
    }

    [Fact]
    public void Mer_MemeCarte_MemeMer_EtFondsCoherents()
    {
        var a = Carte.Mer;
        var b = Mer.Calculer(GenerateurContinent.Generer(42));
        Assert.Equal(a.Profondeur, b.Profondeur);
        Assert.Equal(a.Rivages, b.Rivages);
        Assert.Equal(a.Recif, b.Recif);
        Assert.Equal(a.Iles, b.Iles);
        Assert.Equal(a.Vents, b.Vents);
        for (var i = 0; i < Carte.Biomes.Length; i++)
        {
            var sale = Biomes.EstEauSalee((Biome)Carte.Biomes[i]);
            Assert.Equal(sale, a.Profondeur[i] > 0);
            Assert.Equal(sale, a.Fonds[i] != Fond.Terre);
            if (a.Rivages[i] != Rivage.Aucun) Assert.False(sale);
            if (a.Recif[i]) Assert.InRange(a.Profondeur[i], 1, 30);
        }
        Assert.Single(a.Continents);
        Assert.Contains(a.Rivages, r => r == Rivage.Plage);
        Assert.Contains(a.Rivages, r => r == Rivage.Falaise);
        Assert.Equal(4, a.Vents.Length);
        Assert.All(a.Vents, v => Assert.InRange(v.Depuis, 0, 360));
    }

    [Fact]
    public void Horizon_SuitLaCourbureDeLaPlanete()
    {
        // Debout sur la plage, l'horizon est à moins de 5 km ; le sommet d'une montagne de 1 000 m se voit de plus de 110 km.
        Assert.InRange(Mer.Horizon(Mer.HauteurDesYeux, 0), 4.5, 4.8);
        Assert.InRange(Mer.Horizon(Mer.HauteurDesYeux, 1000), 115, 118);
    }

    [Fact]
    public void Passages_EntreContinents_MesuresEtVus()
    {
        var carte = GenerateurContinent.Generer(new ConfigurationMonde(4, 33, 256, 256, 3));
        var m = carte.Mer;
        Assert.Equal(3, m.Continents.Count);
        var entre = m.Passages.Where(p => m.Continents.Contains(p.MasseA) && m.Continents.Contains(p.MasseB)).ToList();
        Assert.NotEmpty(entre);
        Assert.All(entre, p =>
        {
            Assert.InRange(p.Km, 0.1, 300);
            Assert.Equal(p.MasseA, m.Masse[p.DepuisA]);
            Assert.Equal(p.MasseB, m.Masse[p.VersB]);
            if (p.VueDuRivage) Assert.True(p.VueDuSommet);
        });
        // Les courants longent les côtes et suivent le vent au large.
        var cote = Enumerable.Range(0, m.Profondeur.Length).First(i => m.DistanceCote[i] == 1);
        var (x, y) = m.Courant(cote, Monde.Simulation.Temps.Saison.Ete);
        Assert.True(Math.Sqrt(x * x + y * y) < 0.5);
        Assert.Equal((0.0, 0.0), m.Courant(carte.Indice(carte.Emplacements[0].X, carte.Emplacements[0].Y), Monde.Simulation.Temps.Saison.Ete));
    }
}
