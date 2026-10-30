using Monde.Simulation;
using Monde.Simulation.Chimie;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.32.6 : cuire dans l'eau, les usages de la chaux, les taches vertes du cuivre.</summary>
public sealed class CuisineChauxTests(ITestOutputHelper sortie)
{
    private static readonly Alea Hasard = new(11);

    private static Humain Femme(int id, int x, int y, double ouverture = 0.8, long naissance = -25 * Calendrier.SecondesParAn, int mereId = 0) => new()
    {
        Id = id,
        MereId = mereId,
        Nom = $"F{id}",
        PartenaireId = 0,
        Sexe = Sexe.Femme,
        Emplacement = id,
        Naissance = naissance,
        Personnalite = new Personnalite(ouverture, 0.6, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        X = x,
        Y = y,
    };

    /// <summary>Un pot de terre cuite, bien cuit, plein d'eau.</summary>
    private static Objet PotPlein => new(Matiere.Substance, 0.8, 1, Plein: true, Pieces: (Substance.De(Structure.Cuite, 0.25, 1, (Phase.Metakaolin, 0.4),
        (Phase.IlliteCuite, 0.2), (Phase.Quartz, 0.35), (Phase.Hematite, 0.05)) with { Forme = Forme.Creux | Forme.Rond, Frittage = 0.6 }).Signature);

    private static Savoir Sait(Geste g, Matiere a, Matiere b, int mode, string[] changements, string famille = "", long utiliseLe = 0) =>
        new(g, a, b, Effet.Transformation, 0, Symptome.Aucun, 6, 6, 0, utiliseLe, OrigineSavoir.Essai, 0, 0, Comportement.Recette(g, a, b, mode, changements, famille));

    private static (Comportement C, Ecosysteme E) Monde(Carte carte, int x, int y, ulong graine, int plantes = 600, bool hutte = false)
    {
        var e = new Ecosysteme(carte);
        e.Plantes[carte.Indice(x, y)] = (ushort)plantes;
        var c = new Comportement(carte, e, new Alea(graine))
        {
            Social = new VieSociale(new Alea(1)), Generation = 2,
            Installations = new Installations([new Foyer(1, x, y, 5, 0, 1, 0)], hutte ? [new Hutte(2, x, y, 0.5, 0, 1, 0)] : null),
        };
        return (c, e);
    }

    /// <summary>
    /// Une femme affamée pose son pot plein sur le feu, avec des racines d'une terre que le cueilleur cru a laissée (600, sous le
    /// seuil de 1 000) : elle mange, souvent retient que « ce qu'on y met s'attendrit », et son pot se fend parfois.
    /// </summary>
    [Fact]
    public void Scenario_PotSurLeFeu_RacinesBouillies()
    {
        var carte = MondeSimuleTests.Carte42;
        var (x, y) = (10, 10);
        int mange = 0, savent = 0, fendus = 0;
        for (var essai = 0; essai < 40; essai++)
        {
            var (c, _) = Monde(carte, x, y, (ulong)essai + 1);
            // Toutes les cases voisines dépouillées, sauf la sienne.
            var h = Femme(1, x, y);
            c.Reprendre([h], 0);
            h.Faim = 70;
            h.Objets.Add(PotPlein);
            var effet = c.Bouillir(h, false, OrigineSavoir.Essai, [h]);
            if (effet == Effet.Transformation) mange++;
            if (h.Faim < 70 && effet == Effet.Transformation) Assert.True(h.Faim < 70 - 2.5 * 1.1);
            if (h.Savoirs.Exists(s => Comportement.LireRecette(s.Pieces) is { } r && r.Changements.Contains("tendre"))) savent++;
            if (!h.Objets.Exists(o => o.Matiere == Matiere.Substance)) fendus++;
        }
        sortie.WriteLine($"{mange} repas, {savent} l'ont retenu, {fendus} pots fendus sur 40");
        Assert.InRange(mange, 25, 39);
        Assert.InRange(savent, 15, 39);
        Assert.InRange(fendus, 1, 15);
    }

    /// <summary>Sans feu, ou sans rien à bouillir autour, on ne cuit pas.</summary>
    [Fact]
    public void SansFeuNiRacines_RienNeCuit()
    {
        var carte = MondeSimuleTests.Carte42;
        var (x, y) = (10, 10);
        var e = new Ecosysteme(carte);
        for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++) e.Plantes[carte.Indice(x + dx, y + dy)] = 100;
        var c = new Comportement(carte, e, new Alea(1)) { Social = new VieSociale(new Alea(1)), Generation = 2, Installations = new Installations([new Foyer(1, x, y, 5, 0, 1, 0)]) };
        var h = Femme(1, x, y);
        h.Faim = 70;
        h.Objets.Add(PotPlein);
        Assert.Equal(Effet.Rien, c.Bouillir(h, false, OrigineSavoir.Essai, [h]));
        Assert.Equal(70, h.Faim);
        var (sansFeu, _) = Monde(carte, x, y, 1);
        sansFeu.Installations = new Installations();
        Assert.Equal(Effet.Rien, sansFeu.Bouillir(h, false, OrigineSavoir.Essai, [h]));
    }

    /// <summary>
    /// À la pierre chaude : au bord de l'eau, une calebasse et un galet. Qui le sait le refait quand la faim presse ; un petit
    /// enfant près d'elle a sa part de bouillie.
    /// </summary>
    [Fact]
    public void Scenario_PierresChaudes_EtBouilliePourLePetit()
    {
        var carte = MondeSimuleTests.Carte42;
        var essai = new Comportement(carte, new Ecosysteme(carte), new Alea(1));
        var (x, y) = Enumerable.Range(0, carte.Largeur * carte.Hauteur).Select(i => (i % carte.Largeur, i / carte.Largeur))
            .First(p => p.Item1 > 2 && p.Item2 > 2 && p.Item1 < carte.Largeur - 3 && p.Item2 < carte.Hauteur - 3 && Climat.EstPraticable(carte.BiomeEn(p.Item1, p.Item2))
                        && essai.EauDouceAPortee(p.Item1, p.Item2));
        var (c, _) = Monde(carte, x, y, 5);
        var h = Femme(1, x, y);
        h.Faim = 70;
        h.Objets.Add(new Objet(Matiere.Calebasse, 1, 1));
        h.Objets.Add(new Objet(Matiere.Galet, 1, 1));
        h.Savoirs.Add(Sait(Geste.Plonger, Matiere.Galet, Matiere.Eau, 0, ["bout", "tendre"]));
        var petit = Femme(2, x, y, naissance: -3 * Calendrier.SecondesParAn, mereId: 1);
        petit.Faim = 50;
        c.Reprendre([h, petit], 0);
        Assert.True(c.Cuisiner(h, [h, petit]));
        sortie.WriteLine($"faim de la mère {h.Faim:F0}, du petit {petit.Faim:F0}");
        Assert.True(h.Faim < 65);
        Assert.True(petit.Faim < 50);
    }

    /// <summary>Une mère qui fait des bouillies sèvre plus tôt : la fécondité revient avant un an et demi.</summary>
    [Fact]
    public void Bouillies_RapprochentLesNaissances()
    {
        var maintenant = 10 * Calendrier.SecondesParAn;
        var mere = Femme(1, 5, 5);
        var enfant = Femme(2, 5, 5, naissance: maintenant - (long)(1.35 * Calendrier.SecondesParAn), mereId: 1);
        enfant.Activite = Activite.PorteParSaMere;
        var sans = Comportement.Allaitement(mere, [mere, enfant], maintenant);
        mere.Savoirs.Add(Sait(Geste.Cuire, Matiere.Plante, Matiere.Eau, Comportement.PotSurLeFeu, ["tendre"], utiliseLe: maintenant - Calendrier.SecondesParJour));
        var avec = Comportement.Allaitement(mere, [mere, enfant], maintenant);
        sortie.WriteLine($"fécondité à 1,35 an : {sans:F2} sans bouillies, {avec:F2} avec");
        Assert.Equal(0.02, sans, 6);
        Assert.True(avec > 0.04);
    }

    /// <summary>La chaux vive mouillée fait une pâte ; sèche, elle prend en masse, et un enduit mince durcit en calcaire en quelques semaines.</summary>
    [Fact]
    public void PateDeChaux_PrendEtDurcit()
    {
        var calcaire = Substance.De(Structure.Roche, 0.1, 1, (Phase.Calcite, 0.95), (Phase.Quartz, 0.05));
        var vive = Reactions.Chauffer(calcaire, new Chauffe(900, 6, 5, Atmosphere.Oxydante), Hasard).Produit;
        Assert.True(vive.Part(Phase.Chaux) > 0.3);
        var pate = Reactions.Melanger(vive with { Structure = Structure.Grains, Masse = 1 }, Substance.De(Structure.Grains, 0, 0.6, (Phase.Eau, 1)), 0);
        Assert.True(pate.Exotherme);
        Assert.Equal(Structure.Pate, pate.Produit.Structure);
        var seche = Reactions.Secher(pate.Produit, 15, 0.7);
        Assert.Equal(Structure.Roche, seche.Structure);
        var enduit = Reactions.Carbonater(seche, 30, 0.5);
        sortie.WriteLine($"portlandite {seche.Part(Phase.Portlandite):P0} → {enduit.Part(Phase.Portlandite):P0}, calcite {enduit.Part(Phase.Calcite):P0}");
        Assert.True(enduit.Part(Phase.Calcite) > 0.5);
        Assert.True(enduit.Durete > seche.Durete);
        // En boule de 3 cm, un mois ne suffit pas.
        Assert.True(Reactions.Carbonater(seche, 30, 3).Part(Phase.Portlandite) > 0.2);
    }

    /// <summary>
    /// Une femme enduit sa hutte de pâte blanche : elle se raffermit et s'affaisse bien moins vite ; revenue quelques jours plus
    /// tard, elle finit par voir que la pâte a durci, et le retient.
    /// </summary>
    [Fact]
    public void Scenario_HutteEnduite()
    {
        var carte = MondeSimuleTests.Carte42;
        var (x, y) = (10, 10);
        var (c, _) = Monde(carte, x, y, 3, hutte: true);
        var h = Femme(1, x, y);
        c.Reprendre([h], Calendrier.SecondesParJour);
        var pate = Reactions.Melanger(Substance.De(Structure.Grains, 0.4, 1, (Phase.Chaux, 0.95), (Phase.Quartz, 0.05)),
            Substance.De(Structure.Grains, 0, 0.6, (Phase.Eau, 1)), 0).Produit;
        h.Objets.Add(new Objet(Matiere.Substance, 0.5, 1, Pieces: pate.Arrondie.Signature));
        Assert.True(c.Enduire(h, 0));
        var hutte = c.Installations.HutteEn(x, y)!;
        Assert.True(hutte.Solidite > 0.55);
        Assert.Equal(Calendrier.SecondesParJour, hutte.EnduiteLe);
        Assert.Empty(h.Objets);
        // Une hutte nue et une hutte enduite, cent cinquante jours plus tard.
        var i = new Installations(null, [new Hutte(1, 0, 0, 1, 0, 1, 0), new Hutte(2, 1, 0, 1, 0, 1, 0, EnduiteLe: 1)]);
        i.Affaisser(150, _ => 1 / 135.0);
        Assert.Null(i.HutteEn(0, 0));
        Assert.NotNull(i.HutteEn(1, 0));
        c.Reprendre([h], 11 * Calendrier.SecondesParJour);
        for (var pas = 0; pas < 500 && !h.Savoirs.Exists(s => s.Geste == Geste.Couvrir); pas++) c.VoirLEnduit(h, [h]);
        var savoir = Assert.Single(h.Savoirs);
        Assert.Contains("durcit", Comportement.LireRecette(savoir.Pieces)!.Value.Changements);
    }

    /// <summary>Une peau trempée dans l'eau de chaux fait un meilleur vêtement ; la chaux vive brûle parfois les mains.</summary>
    [Fact]
    public void Scenario_PeauEpilee()
    {
        var carte = MondeSimuleTests.Carte42;
        var essai = new Comportement(carte, new Ecosysteme(carte), new Alea(1));
        var (x, y) = Enumerable.Range(0, carte.Largeur * carte.Hauteur).Select(i => (i % carte.Largeur, i / carte.Largeur))
            .First(p => Climat.EstPraticable(carte.BiomeEn(p.Item1, p.Item2)) && essai.EauDouceAPortee(p.Item1, p.Item2));
        int brulees = 0, retenu = 0;
        for (var n = 0; n < 40; n++)
        {
            var (c, _) = Monde(carte, x, y, (ulong)n + 1);
            var h = Femme(1, x, y);
            h.Froid = 30;
            h.Objets.Add(new Objet(Matiere.Peau, 0.6, 1));
            h.Objets.Add(new Objet(Matiere.Substance, 0.5, 1, Pieces: Substance.De(Structure.Grains, 0.4, 0.5, (Phase.Chaux, 0.95), (Phase.Quartz, 0.05)).Arrondie.Signature));
            Assert.True(c.Epiler(h, OrigineSavoir.Essai, [h]));
            var peau = h.Objets.Single(o => o.Matiere == Matiere.Peau);
            Assert.True(peau.Qualite > 0.6 * 1.14);
            if (h.Sante < 100) brulees++;
            if (h.Savoirs.Exists(s => s.Geste == Geste.Laver)) retenu++;
        }
        sortie.WriteLine($"{retenu} l'ont retenu, {brulees} brûlées sur 40");
        Assert.InRange(retenu, 20, 40);
        Assert.InRange(brulees, 1, 20);
    }

    /// <summary>
    /// Des taches vertes au-dessus d'un gisement de cuivre enfoui : on les voit bien plus souvent que dans les mondes d'avant ;
    /// qui sait fouir y trouve la pierre verte.
    /// </summary>
    [Fact]
    public void TachesVertes_SurLeCuivreEnfoui()
    {
        var (carte, g) = Enumerable.Range(1, 30).Select(graine => GenerateurContinent.Generer((ulong)graine))
            .SelectMany(m => m.Terrain.Gisements.Where(x => x.Profondeur == Enfouissement.SousLaSurface && x.Type == Gite.Malachite).Select(x => (m, x)))
            .First();
        var vus = new int[3];
        for (var generation = 0; generation <= 2; generation += 2)
        {
            for (var n = 0; n < 60; n++)
            {
                var c = new Comportement(carte, new Ecosysteme(carte), new Alea((ulong)n + 1)) { Social = new VieSociale(new Alea(1)), Generation = generation };
                var h = Femme(1, g.X, g.Y);
                for (var pas = 0; pas < 50; pas++) c.RemarquerLeGisement(h);
                if (h.Memoire.Lieux.Exists(l => l.Type == TypeLieu.Gisement)) vus[generation]++;
            }
        }
        sortie.WriteLine($"vus sur 60 en 50 pas : génération 0 {vus[0]}, génération 2 {vus[2]}");
        Assert.True(vus[2] > 2 * vus[0]);
        var c2 = new Comportement(carte, new Ecosysteme(carte), new Alea(1)) { Social = new VieSociale(new Alea(1)), Generation = 2 };
        var fouisseuse = Femme(1, g.X, g.Y);
        Assert.False(c2.APorteeDeMain(fouisseuse, g));
        fouisseuse.Savoirs.Add(new Savoir(Geste.Fouir, Matiere.BatonPointu, Matiere.Aucune, Effet.Tubercules, 0, Symptome.Aucun, 6, 6, 0, 0, OrigineSavoir.Essai, 0, 0));
        Assert.True(c2.APorteeDeMain(fouisseuse, g));
    }
}
