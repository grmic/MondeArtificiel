using Monde.Simulation;
using Monde.Simulation.Chimie;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.32.5 : la pierre verte donne le cuivre (réduction sans air, puis fusion au creuset).</summary>
public sealed class CuivreTests(ITestOutputHelper sortie)
{
    private static readonly Alea Hasard = new(7);

    /// <summary>Un galet de rivière pour frapper (quartzite).</summary>
    private const double Galet = 0.65;

    private static Substance Malachite => Matieres.Minerai(Gite.Malachite);

    /// <summary>La pierre verte frappée jusqu'à tomber en poudre.</summary>
    private static Substance Broyee()
    {
        var s = Malachite;
        for (var i = 0; i < 4 && s.Structure != Structure.Grains; i++) s = Reactions.Marteler(s, Galet, 0.5, Hasard).Produit;
        Assert.Equal(Structure.Grains, s.Structure);
        return s;
    }

    private static Substance AvecCharbon(Substance poudre) =>
        Reactions.Melanger(poudre with { Masse = 0.3 }, Thermique.Charbon with { Structure = Structure.Grains, Masse = 0.15 }, 0).Produit;

    [Fact]
    public void Foyers_DuCreuset()
    {
        var ouvert = Thermique.Temperature(FormeFoyer.Ouvert, 0.2, 0, 4, false, 15, 2);
        var creuset = Thermique.Temperature(FormeFoyer.Enceinte, 0.2, 1, 4, false, 15, 2);
        var souffle = Thermique.Temperature(FormeFoyer.Enceinte, 0.2, 1, 4, true, 15, 2);
        sortie.WriteLine($"ouvert {ouvert:F0} °C, creuset {creuset:F0} °C, creuset soufflé {souffle:F0} °C");
        // Sans souffle, le charbon en fosse n'atteint pas la fusion du cuivre ; soufflé, si (Rehren, 2003 : plus de 1 100 °C).
        Assert.InRange(creuset * 0.95, 850, 1084);
        Assert.InRange(souffle * 0.95, 1100, 1350);
    }

    [Fact]
    public void PierreVerte_SeBroie_EnDeuxOuTroisCoups()
    {
        var s = Malachite;
        var coups = 0;
        while (s.Structure != Structure.Grains && coups < 5)
        {
            var t = Reactions.Marteler(s, Galet, 0.5, Hasard);
            s = t.Produit;
            coups++;
            sortie.WriteLine($"coup {coups} : dureté {s.Durete:F2}, {string.Join(", ", Perception.Changements(Malachite, t))}");
        }
        Assert.InRange(coups, 1, 3);
    }

    [Fact]
    public void AuCreusetSouffle_LaPoudreEtLeCharbon_DonnentUneGoutteDeCuivre()
    {
        var melange = AvecCharbon(Broyee());
        var t = Reactions.Chauffer(melange, new Chauffe(1180, 1, 10, Atmosphere.Reductrice), Hasard);
        Assert.True(t.Fondue);
        Assert.Equal(Structure.Metal, t.Produit.Structure);
        Assert.True(t.Produit.Malleable);
        var changements = Perception.Changements(melange, t);
        Assert.Contains("goutte", changements);
        sortie.WriteLine($"goutte de {t.Produit.Masse * 1000:F0} g pour {melange.Masse * 1000:F0} g de mélange ; {string.Join(", ", changements)}");
        // De 0,3 kg de minerai à 55 % de malachite (32 % de cuivre) : quelques dizaines de grammes.
        Assert.InRange(t.Produit.Masse, 0.01, 0.1);
        // La goutte se martèle comme le cuivre natif.
        Assert.Contains("plat", Perception.Changements(t.Produit, Reactions.Marteler(t.Produit, Galet, 0.5, Hasard)));
    }

    [Fact]
    public void SansFondre_LeCuivreResteEnGrains_QuOnLibereEnEcrasant()
    {
        var melange = AvecCharbon(Broyee());
        var t = Reactions.Chauffer(melange, new Chauffe(950, 1.5, 10, Atmosphere.Reductrice), Hasard);
        Assert.False(t.Fondue);
        Assert.Contains("points", Perception.Changements(melange, t));
        var ecrase = Reactions.Marteler(t.Produit, Galet, 0.5, Hasard);
        Assert.Equal(Structure.Metal, ecrase.Produit.Structure);
        Assert.Contains("grains", Perception.Changements(t.Produit, ecrase));
    }

    [Fact]
    public void ALAir_IlNyAQueLOxydeNoir()
    {
        var t = Reactions.Chauffer(AvecCharbon(Broyee()), new Chauffe(1180, 1, 10, Atmosphere.Oxydante), Hasard);
        Assert.Equal(0, t.Produit.Part(Phase.Cuivre));
        Assert.True(t.Produit.Part(Phase.Tenorite) > 0.1);
    }

    [Fact]
    public void SansCharbonMele_LeGazReduitPlusLentement()
    {
        var c = new Chauffe(1000, 1, 10, Atmosphere.Reductrice);
        var seule = Reactions.Chauffer(Broyee(), c, Hasard).Produit.Part(Phase.Cuivre);
        var melee = Reactions.Chauffer(AvecCharbon(Broyee()), c, Hasard).Produit.Part(Phase.Cuivre);
        sortie.WriteLine($"cuivre : poudre seule {seule:P1}, mêlée de charbon {melee:P1}");
        Assert.True(seule > 0);
        Assert.True(melee > 1.5 * seule);
    }

    private static Humain Fondeur(int x, int y) => new()
    {
        Id = 1,
        Nom = "F",
        PartenaireId = 0,
        Sexe = Sexe.Femme,
        Emplacement = 1,
        Naissance = -25 * Calendrier.SecondesParAn,
        Personnalite = new Personnalite(0.6, 0.8, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(Sexe.Femme),
        X = x,
        Y = y,
    };

    /// <summary>Un pot de terre cuite, bien cuit.</summary>
    private static Objet Pot => new(Matiere.Substance, 0.8, 1, Pieces: (Substance.De(Structure.Cuite, 0.25, 1, (Phase.Metakaolin, 0.4), (Phase.IlliteCuite, 0.2),
        (Phase.Quartz, 0.35), (Phase.Hematite, 0.05)) with { Forme = Forme.Creux | Forme.Rond, Frittage = 0.6 }).Signature);

    /// <summary>
    /// Une femme qui sait souffler sur la braise, au bord d'un feu de charbon, avec un pot et de la pierre verte broyée mêlée
    /// de charbon : au creuset, elle en tire une goutte de cuivre (le plus souvent), et son pot se fend parfois.
    /// </summary>
    [Fact]
    public void Scenario_AuCreuset_UneGoutteDeCuivre()
    {
        var carte = MondeSimuleTests.Carte42;
        var (x, y) = (10, 10);
        int gouttes = 0, potsFendus = 0;
        for (var essai = 0; essai < 30; essai++)
        {
            var c = new Comportement(carte, new Ecosysteme(carte), new Alea((ulong)essai + 1))
            {
                Social = new VieSociale(new Alea(1)), Generation = 2,
                Installations = new Installations([new Foyer(1, x, y, 5, 0, 1, 0, Charbon: 1)]),
            };
            var h = Fondeur(x, y);
            h.Savoirs.Add(new Savoir(Geste.Souffler, Matiere.Feu, Matiere.Aucune, Effet.Transformation, 0, Symptome.Aucun, 6, 6, 0, 0, OrigineSavoir.Essai, 0, 0,
                Comportement.Recette(Geste.Souffler, Matiere.Feu, Matiere.Aucune, 0, ["attise"])));
            h.Objets.Add(Pot);
            h.Objets.Add(new Objet(Matiere.Substance, 0.5, 1, Pieces: AvecCharbon(Broyee()).Arrondie.Signature));
            c.FaireMatiere(h, Geste.Cuire, Matiere.Substance, Matiere.Feu, OrigineSavoir.Essai, [h], mode: 4, objet: 1);
            if (h.Objets.Exists(o => Substance.Lire(o.Pieces) is { Structure: Structure.Metal, Malleable: true })) gouttes++;
            if (!h.Objets.Exists(o => Substance.Lire(o.Pieces) is { Structure: Structure.Cuite })) potsFendus++;
        }
        sortie.WriteLine($"{gouttes} gouttes, {potsFendus} pots fendus sur 30");
        Assert.InRange(gouttes, 20, 30);
        Assert.InRange(potsFendus, 1, 15);
    }

    [Fact]
    public void Parure_EtAlene()
    {
        var goutte = Reactions.Chauffer(AvecCharbon(Broyee()), new Chauffe(1180, 1, 10, Atmosphere.Reductrice), Hasard).Produit;
        var plaque = Reactions.Marteler(goutte, Galet, 0.5, Hasard).Produit;
        var pointe = Reactions.Marteler(plaque, Galet, 0.5, Hasard).Produit;
        var o = (Substance s) => new Objet(Matiere.Substance, 0.5, 1, Pieces: s.Arrondie.Signature);
        Assert.False(Attirance.Parure(o(goutte)));
        Assert.True(Attirance.Parure(o(plaque)));
        Assert.False(Attirance.Alene(o(plaque)));
        Assert.True(Attirance.Alene(o(pointe)));
        var h = Fondeur(0, 0);
        var sans = Attirance.Attractivite(h, IdealBeaute.ParDefaut, 0);
        h.Objets.Add(o(plaque));
        Assert.Equal(sans + 5, Attirance.Attractivite(h, IdealBeaute.ParDefaut, 0), 6);
    }

    [Fact]
    public void CuivreNatif_FondAuCreuset()
    {
        var t = Reactions.Chauffer(Matieres.Minerai(Gite.CuivreNatif), new Chauffe(1150, 1, 10, Atmosphere.Reductrice), Hasard);
        Assert.True(t.Fondue);
        Assert.Equal(Structure.Metal, t.Produit.Structure);
    }

    /// <summary>
    /// Sur un gisement de pierre verte qui affleure et qu'elle connaît, une curieuse finit par en ramasser un morceau, une seule
    /// fois ; celle qui ne le connaît pas n'y touche pas.
    /// </summary>
    [Fact]
    public void PierreVerte_Ramassee_SurUnGisementConnu()
    {
        var (carte, g) = Enumerable.Range(1, 20).Select(graine => GenerateurContinent.Generer((ulong)graine))
            .SelectMany(m => m.Terrain.Gisements.Where(x => x.Profondeur == Enfouissement.Affleurant && x.Type == Gite.Malachite).Select(x => (m, x)))
            .First();
        var c = new Comportement(carte, new Ecosysteme(carte), new Alea(3)) { Social = new VieSociale(new Alea(1)), Generation = 2 };
        var inconnue = Fondeur(g.X, g.Y);
        var h = Fondeur(g.X, g.Y);
        h.Memoire.Retenir(TypeLieu.Gisement, g.X, g.Y, 0);
        var pas = 0;
        while (h.Objets.Count == 0 && pas++ < 2000) c.RamasserUnePierre(h);
        for (var i = 0; i < 2000; i++) { c.RamasserUnePierre(h); c.RamasserUnePierre(inconnue); }
        sortie.WriteLine($"ramassée après {pas} pas");
        var morceau = Assert.Single(h.Objets);
        Assert.True(Substance.Lire(morceau.Pieces)!.Part(Phase.Malachite) > 0.1);
        Assert.Empty(inconnue.Objets);
    }
}
