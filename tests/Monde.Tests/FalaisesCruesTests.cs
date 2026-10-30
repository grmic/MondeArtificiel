using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Xunit.Abstractions;

namespace Monde.Tests;

/// <summary>0.32.7 : falaises, cols et chutes ; limon des crues et ce que les humains en comprennent.</summary>
public sealed class FalaisesCruesTests(ITestOutputHelper sortie)
{
    /// <summary>Une carte de génération 2 pleine de montagnes (graine 2 : environ 3 900 parois).</summary>
    private static readonly Carte Montagnes = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2));

    /// <summary>Midi du deuxième jour du printemps de l'an 10.</summary>
    private static readonly long Midi = 10 * Calendrier.SecondesParAn + Calendrier.SecondesParJour + 12 * Calendrier.SecondesParHeure;

    private static Humain Adulte(int id, int x, int y, double prudence, double adresse = 100, double age = 30, double ouverture = 0.5,
        double intelligence = 100) => new()
    {
        Id = id,
        Nom = $"H{id}",
        PartenaireId = 0,
        Sexe = Sexe.Homme,
        Emplacement = id,
        Naissance = Midi - (long)(age * Calendrier.SecondesParAn),
        // Prudence = 0,6 × conscience + 0,4 × névrosisme : les deux suivent la prudence voulue.
        Personnalite = new Personnalite(ouverture, prudence, 1 - prudence, 0.5, prudence, 0.5, 0.5, 0.5,
            new Intelligence(intelligence, 100, 100, adresse, 100, 100)),
        Biologie = Biologie.ParDefaut(Sexe.Homme),
        X = x,
        Y = y,
    };

    private static Comportement Comportement(Carte carte, ulong graine, int generation = 2, Ecosysteme? e = null) =>
        new(carte, e ?? new Ecosysteme(carte), new Alea(graine)) { Social = new VieSociale(new Alea(1)), Generation = generation, Installations = new Installations() };

    /// <summary>Des parois loin des bords de la carte, entre deux cases de terre : (case, voisine).</summary>
    private static IEnumerable<(int I, int J)> Parois(Carte carte)
    {
        var r = carte.Relief;
        for (var i = 0; i < carte.Largeur * carte.Hauteur; i++)
        {
            int x = i % carte.Largeur, y = i / carte.Largeur;
            if (x < 45 || y < 45 || x >= carte.Largeur - 45 || y >= carte.Hauteur - 45) continue;
            foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
            {
                var j = carte.Indice(x + dx, y + dy);
                if (Climat.EstPraticable(carte.BiomeEn(x, y)) && Climat.EstPraticable(carte.BiomeEn(x + dx, y + dy)) && r.Paroi(i, j)) yield return (i, j);
            }
        }
    }

    [Fact]
    public void Relief_Deterministe_Symetrique_EtSeulementSurLesFortsDenivelés()
    {
        var autre = GenerateurContinent.Generer(ConfigurationMonde.Nouvelle(2)).Relief;
        var r = Montagnes.Relief;
        int l = Montagnes.Largeur, parois = 0, passages = 0;
        for (var i = 0; i < l * Montagnes.Hauteur; i++)
        {
            Assert.Equal(r.Cols[i], autre.Cols[i]);
            int x = i % l, y = i / l;
            if (x + 1 >= l || y + 1 >= Montagnes.Hauteur) continue;
            foreach (var j in new[] { i + 1, i + l, i + l + 1 })
            {
                var f = r.Entre(i, j);
                Assert.Equal(f, r.Entre(j, i));
                Assert.Equal(f, autre.Entre(i, j));
                if (f is Franchissement.Paroi or Franchissement.Passage)
                {
                    Assert.True(Math.Abs(Montagnes.Altitudes[i] - Montagnes.Altitudes[j]) >= Relief.DeniveleParoi);
                    if (f == Franchissement.Paroi) parois++;
                    else passages++;
                    Assert.Equal(r.Hauteur(i, j), r.Hauteur(j, i));
                }
            }
        }
        sortie.WriteLine($"{parois} parois, {passages} passages, {r.Cols.Count(c => c)} cols");
        Assert.InRange(passages, parois / 20, parois / 2);
        Assert.True(r.Cols.Count(c => c) >= 5);
    }

    /// <summary>
    /// Un prudent, au pied d'une paroi, veut aller juste de l'autre côté : il ne la franchit jamais, il contourne par un passage ou
    /// renonce. Un monde de génération 0 ignore les parois.
    /// </summary>
    [Fact]
    public void Scenario_Prudent_ContourneLaParoi()
    {
        var (arrives, renonce, essais) = (0, 0, 0);
        foreach (var (i, j) in Parois(Montagnes).Take(25))
        {
            essais++;
            var c = Comportement(Montagnes, (ulong)essais);
            var h = Adulte(1, i % Montagnes.Largeur, i / Montagnes.Largeur, prudence: 0.9);
            c.Reprendre([h], Midi);
            for (var pas = 0; pas < 400 && h.EstVivant; pas++)
            {
                c.Avancer(h, j % Montagnes.Largeur, j / Montagnes.Largeur, Activite.Explorer);
                if (h.CibleX < 0) break;
            }
            Assert.DoesNotContain(c.Journal, e => e.Recit?.Cle is "relief.grimpe" or "relief.desescalade");
            if (h.X == j % Montagnes.Largeur && h.Y == j / Montagnes.Largeur) arrives++;
            else if (h.CibleX < 0) renonce++;
        }
        sortie.WriteLine($"{arrives} arrivés par un détour, {renonce} ont renoncé, sur {essais}");
        Assert.True(arrives > 0);

        // Génération 0 : la paroi n'existe pas pour le marcheur, il passe tout droit.
        var (a, b) = Parois(Montagnes).First();
        var ancien = Comportement(Montagnes, 1, generation: 0);
        var g = Adulte(1, a % Montagnes.Largeur, a / Montagnes.Largeur, prudence: 0.9);
        ancien.Reprendre([g], Midi);
        for (var pas = 0; pas < 5 && (g.X, g.Y) != (b % Montagnes.Largeur, b / Montagnes.Largeur); pas++)
            ancien.Avancer(g, b % Montagnes.Largeur, b / Montagnes.Largeur, Activite.Explorer);
        Assert.Equal((b % Montagnes.Largeur, b / Montagnes.Largeur), (g.X, g.Y));
    }

    /// <summary>
    /// Un téméraire adroit et affamé, devant une paroi dont le détour est long, la grimpe de jour quand elle n'est pas trop haute
    /// (parfois il tombe) ; jamais de nuit ; un prudent dans la même situation n'ose pas.
    /// </summary>
    [Fact]
    public void Scenario_Temeraire_GrimpeDeJour_PasLaNuit()
    {
        var c0 = Comportement(Montagnes, 1);
        var temeraire = Adulte(1, 0, 0, prudence: 0.05, adresse: 145, ouverture: 0.9);
        temeraire.Faim = 85;
        var prudent = Adulte(2, 0, 0, prudence: 0.9);
        prudent.Faim = 85;
        c0.Reprendre([temeraire, prudent], Midi);
        Assert.True(c0.OseGrimper(temeraire));
        Assert.False(c0.OseGrimper(prudent));
        c0.Reprendre([temeraire], Midi - 12 * Calendrier.SecondesParHeure);
        Assert.False(c0.OseGrimper(temeraire));

        // Les parois dont le détour est introuvable ou très long : il passe par la paroi.
        var (grimpe, tombe, essais) = (0, 0, 0);
        foreach (var (i, j) in Parois(Montagnes))
        {
            var c = Comportement(Montagnes, (ulong)essais + 7);
            var h = Adulte(1, i % Montagnes.Largeur, i / Montagnes.Largeur, prudence: 0.05, adresse: 145, ouverture: 0.9);
            h.Faim = 85;
            c.Reprendre([h], Midi);
            var direct = c.PremierPasPondere(i, j, out var effort);
            if (direct >= 0 && effort <= 3 * 1000 + 4000) continue;
            if (++essais > 30) break;
            for (var pas = 0; pas < 80 && h.EstVivant && (h.X, h.Y) != (j % Montagnes.Largeur, j / Montagnes.Largeur); pas++)
                c.Avancer(h, j % Montagnes.Largeur, j / Montagnes.Largeur, Activite.ChercherNourriture);
            if (c.Journal.Exists(e => e.Recit?.Cle is "relief.grimpe" or "relief.desescalade")) grimpe++;
            if (c.Journal.Exists(e => e.Recit?.Cle is "chute.blesse" or "j.mort.chute")) tombe++;
        }
        sortie.WriteLine($"{grimpe} parois franchies, {tombe} chutes, sur {essais - 1}");
        // Une paroi de 200 m ou plus coûte souvent plus que le détour : même le téméraire fait le tour.
        Assert.True(grimpe + tombe >= 3);
    }

    /// <summary>
    /// La gravité d'une chute suit la hauteur : de 3 m on meurt rarement, de 25 m presque toujours ; à 14 m, environ un adulte sur
    /// deux, moins d'enfants, plus de vieillards.
    /// </summary>
    [Fact]
    public void Chutes_LaHauteurTue_SelonLAge()
    {
        double Morts(double metres, double age)
        {
            var morts = 0;
            for (var k = 0; k < 400; k++)
            {
                var c = Comportement(Montagnes, (ulong)k + 1);
                var h = Adulte(1, 100, 100, prudence: 0.5, age: age);
                c.Reprendre([h], Midi);
                c.Tomber(h, metres, "paroi", false, false, h.X, h.Y);
                if (!h.EstVivant) morts++;
                else Assert.True(h.Sante < 100);
            }
            return morts / 400.0;
        }
        var (trois, quatorze, vingtCinq, enfant, vieux) = (Morts(3, 30), Morts(14, 30), Morts(25, 30), Morts(14, 8), Morts(14, 75));
        sortie.WriteLine($"3 m : {trois:P0} ; 14 m : {quatorze:P0} (enfant {enfant:P0}, vieillard {vieux:P0}) ; 25 m : {vingtCinq:P0}");
        Assert.True(trois < 0.1);
        Assert.InRange(quatorze, 0.3, 0.75);
        Assert.True(vingtCinq > 0.9);
        Assert.True(enfant < quatorze);
        Assert.True(vieux > quatorze);
    }

    /// <summary>La nuit sans lune, la pluie, l'âge et la maladresse font glisser ; la pleine lune éclaire un peu.</summary>
    [Fact]
    public void Chutes_NuitPluieAgeMaladresse()
    {
        var c = Comportement(Montagnes, 1);
        var adulte = Adulte(1, 100, 100, prudence: 0.5);
        var maladroit = Adulte(2, 100, 100, prudence: 0.5, adresse: 55);
        var vieux = Adulte(3, 100, 100, prudence: 0.5, age: 72);
        var nouvelleLune = (long)(Math.Round(Midi / (double)Calendrier.SecondesParJour / 29.53) * 29.53 * Calendrier.SecondesParJour);
        c.Reprendre([adulte], nouvelleLune);
        var jour = c.FacteurDeChute(adulte, 0, false, false);
        var nuitNoire = c.FacteurDeChute(adulte, 0, false, true);
        var pluie = c.FacteurDeChute(adulte, 0, true, false);
        c.Reprendre([adulte], nouvelleLune + (long)(14.76 * Calendrier.SecondesParJour)); // pleine lune
        var pleineLune = c.FacteurDeChute(adulte, 0, false, true);
        Assert.True(nuitNoire >= 5 * jour);
        Assert.True(pleineLune < nuitNoire && pleineLune > jour);
        Assert.True(pluie > 2 * jour);
        Assert.True(c.FacteurDeChute(maladroit, 0, false, false) > jour);
        Assert.True(c.FacteurDeChute(vieux, 0, false, false) > 1.8 * c.FacteurDeChute(adulte, 0, false, false));
    }

    /// <summary>Des jours de pluie plus fréquents au printemps et les années arrosées, et toujours les mêmes pour une graine.</summary>
    [Fact]
    public void Pluie_SuitLaSaisonEtLAnnee()
    {
        var e = new Ecosysteme(Montagnes);
        var i = Montagnes.Indice(128, 128);
        int Jours(int saison) => Enumerable.Range(0, Calendrier.JoursParSaison)
            .Count(j => e.Pleut(i, (saison * Calendrier.JoursParSaison + j) * (long)Calendrier.SecondesParJour));
        var (printemps, ete) = (Jours(0), Jours(1));
        e.Pluies[e.Region(128, 128)] = 1.6;
        var printempsArrose = Jours(0);
        sortie.WriteLine($"printemps {printemps}, été {ete}, printemps arrosé {printempsArrose} jours de pluie sur 90");
        Assert.True(printemps > ete);
        Assert.True(printempsArrose > printemps);
        Assert.InRange(printemps, 15, 50);
        Assert.Equal(e.Pleut(i, 5 * Calendrier.SecondesParJour), new Ecosysteme(Montagnes).Pleut(i, 5 * Calendrier.SecondesParJour));
    }

    /// <summary>
    /// Une crue lente dans un monde de génération 2 laisse du limon et une terre qui guérit en quelques semaines ; dans un monde de
    /// génération 0, la cicatrice de toujours (40 %) et pas de limon. Le limon s'éteint en une à trois saisons, et se garde à
    /// l'enregistrement.
    /// </summary>
    [Fact]
    public void Crue_LaisseDuLimon_EnGeneration2()
    {
        var (gen2, gen0) = (MondeAvecCrue(2), MondeAvecCrue(0));
        var limon = gen2.CarteLimon();
        sortie.WriteLine($"génération 2 : {limon.Count(v => v > 0)} cases limoneuses, au plus +{limon.Max() / 100} %");
        Assert.Contains(limon, v => v >= 1_500);
        Assert.All(gen0.CarteLimon(), v => Assert.Equal(0, v));

        // Enregistré puis relu : le même limon.
        var relu = new MondeSimule(gen2.Carte, gen2.Etat);
        Assert.Equal(limon, relu.CarteLimon());

        // Une à trois saisons plus tard, il n'en reste rien (sauf si une autre crue est passée par là).
        gen2.AvancerDe(280L * Calendrier.SecondesParJour);
        var apres = gen2.CarteLimon();
        var limoneuses = Enumerable.Range(0, limon.Length).Where(i => limon[i] > 0).ToList();
        Assert.True(limoneuses.Count(i => apres[i] == 0) >= 0.9 * limoneuses.Count);
    }

    private static MondeSimule MondeAvecCrue(int generation)
    {
        var conf = ConfigurationMonde.ParDefaut(42) with { Generation = generation };
        var carte = GenerateurContinent.Generer(conf);
        var monde = new MondeSimule(carte, MondeSimule.Nouveau(conf)) { Coeurs = 1 };
        // La plus grosse rivière, en plaine.
        var t = carte.Terrain;
        var riviere = Enumerable.Range(0, carte.Largeur * carte.Hauteur).Where(i => carte.Biomes[i] == (byte)Biome.Riviere).MaxBy(i => t.Debit[i]);
        for (var essai = 0; essai < 6 && monde.CarteLimon().All(v => v == 0); essai++)
        {
            if (monde.Declencher(TypeCatastrophe.Crue, riviere % carte.Largeur, riviere / carte.Largeur, 2) == 0) continue;
            if (generation != 2) break;
        }
        return monde;
    }

    /// <summary>
    /// Une curieuse et intelligente qui a vu la plaine sous l'eau y cueille, deux mois plus tard, une abondance : elle finit souvent
    /// par faire le lien, bien plus souvent qu'un esprit lent et fermé. Qui le croit y retourne quand il a faim.
    /// </summary>
    [Fact]
    public void Scenario_LaOuLEauEstPassee_ToutRepousse()
    {
        var carte = Montagnes;
        var (x, y) = (128, 128);
        while (!Climat.EstPraticable(carte.BiomeEn(x, y))) x++;
        var i = carte.Indice(x, y);
        int Comprennent(double intelligence, double ouverture)
        {
            var n = 0;
            for (var k = 0; k < 300; k++)
            {
                var e = new Ecosysteme(carte);
                e.Deposer(i, 0.5, 200);
                e.Plantes[i] = 5_000;
                var c = Comportement(carte, (ulong)k + 1, e: e);
                var h = Adulte(1, x, y, prudence: 0.5, ouverture: ouverture, intelligence: intelligence);
                h.Memoire.Retenir(TypeLieu.Crue, x, y, Midi - 60 * Calendrier.SecondesParJour);
                c.Reprendre([h], Midi);
                for (var fois = 0; fois < 50; fois++) c.ConstaterLeLimon(h, i, true);
                if (h.Savoirs.Exists(s => s.Effet == Effet.Limon)) n++;
            }
            return n;
        }
        var (vif, lent) = (Comprennent(135, 0.9), Comprennent(75, 0.1));
        sortie.WriteLine($"{vif} esprits vifs sur 300 font le lien, contre {lent} esprits lents");
        Assert.InRange(vif, 30, 150);
        Assert.True(vif > 2 * lent);

        // Qui le croit retourne vers la plaine noyée.
        var c2 = Comportement(carte, 3);
        var croyant = Adulte(2, x, y, prudence: 0.5);
        croyant.Savoirs.Add(new Savoir(Geste.Patienter, Matiere.Eau, Matiere.Plante, Effet.Limon, 0, Symptome.Aucun, 4, 4, 0, Midi, OrigineSavoir.Observation, 0, 0));
        var (lx, ly) = (x + 6, y);
        while (!Climat.EstPraticable(carte.BiomeEn(lx, ly))) lx++;
        croyant.Memoire.Retenir(TypeLieu.Crue, lx, ly, Midi - 40 * Calendrier.SecondesParJour);
        c2.Reprendre([croyant], Midi);
        Assert.True(c2.AllerAuLimon(croyant));
        Assert.Equal((lx, ly), (croyant.CibleX, croyant.CibleY));
    }
}
