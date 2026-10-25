using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;
using Monde.Stockage;
using Microsoft.Data.Sqlite;

namespace Monde.Tests;

/// <summary>
/// 0.31.5 : traverser la mer. Des scénarios rapides : des humains posés sur un rivage d'où l'on voit une autre terre à une
/// quinzaine de kilomètres, curieux, qui savent ce qui flotte ; on regarde ce que les règles en font en quelques jours.
/// </summary>
public sealed class NavigationTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    /// <summary>Un passage étroit entre deux terres (6 à 25 km), qu'on voit du rivage : le décor des scénarios.</summary>
    private static readonly Passage Detroit = Carte.Mer.Passages
        .Where(p => p.Km is >= 6 and <= 25 && p.VueDuRivage
                    && Climat.EstPraticable(Carte.BiomeEn(p.DepuisA % Carte.Largeur, p.DepuisA / Carte.Largeur))
                    && Climat.EstPraticable(Carte.BiomeEn(p.VersB % Carte.Largeur, p.VersB / Carte.Largeur)))
        .OrderBy(p => Math.Abs(p.Km - 15)).First();

    private static (int X, int Y) Depart => (Detroit.DepuisA % Carte.Largeur, Detroit.DepuisA / Carte.Largeur);
    private static (int X, int Y) Arrivee => (Detroit.VersB % Carte.Largeur, Detroit.VersB / Carte.Largeur);

    private static Humain Humain(int id, (int X, int Y) ou, double curiosite = 0.9, double conscience = 0.2, Sexe sexe = Sexe.Homme,
        int communaute = 1) => new()
    {
        Id = id, Nom = "H" + id, Sexe = sexe, PartenaireId = 0, Emplacement = 1, Naissance = Maintenant - 25 * An,
        Personnalite = new Personnalite(curiosite, conscience, 0.5, 0.5, 0.2, 0.5, 0.8, 0.7,
            Intelligence.Moyenne with { Manuelle = 125, Spatiale = 125 }),
        Biologie = Biologie.ParDefaut(sexe), Reserves = 80, Sante = 100, CommunauteId = communaute, X = ou.X, Y = ou.Y,
    };

    private static Savoir Sait(Geste g, Effet e, Matiere a = Matiere.Aucune, Matiere b = Matiere.Aucune, string pieces = "") =>
        new(g, a, b, e, 0, Symptome.Aucun, 10, 9, Maintenant, Maintenant, OrigineSavoir.Essai, 0, 0, pieces);

    private static Comportement Comportement(ulong graine = 1) =>
        new(Carte, new Ecosysteme(Carte), new Alea(graine)) { Social = new VieSociale(new Alea(graine)) };

    private static readonly Assemblage Pirogue = new(Matiere.Tronc, 1, Matiere.Aucune, 0, Matiere.Aucune, Montage.Creuse);

    private static Objet Bateau(Assemblage a, double qualite = 0.8) => new(Matiere.Assemblage, qualite, 1, Pieces: a.Signature);

    /// <summary>Ses besoins comblés : le scénario ne teste pas la faim ni la soif.</summary>
    private static void Combler(Humain h)
    {
        h.Faim = Math.Min(h.Faim, 10);
        h.Soif = Math.Min(h.Soif, 10);
        h.Froid = 0;
        h.Reserves = Math.Max(h.Reserves, 60);
        h.Sante = 100;
    }

    private static IEnumerable<string> Cles(Comportement c) => c.Journal.Select(e => e.Recit?.Cle ?? "");

    [Fact]
    public void Lois_UnePirogueTientLaMer_UnRadeauDeDeuxBranchesNon_EtLeRisqueMonteAvecLaHoule()
    {
        Assert.True(Lois.HouleMax(Pirogue, 0.8) >= Lois.HouleDeMer);
        var fagot = new Assemblage(Matiere.Branche, 2, Matiere.Aucune, 0, Matiere.Aucune, Montage.Cote);
        Assert.True(Lois.HouleMax(fagot, 0.5) < Lois.HouleDeMer);
        var roseaux = new Assemblage(Matiere.Roseau, 16, Matiere.Aucune, 0, Matiere.Fibres, Montage.Cote);
        Assert.True(Lois.Portance(roseaux) > 80);
        Assert.True(Lois.RisqueParHeure(0.3, 1.2) < 0.001);
        Assert.True(Lois.RisqueParHeure(1.5, 1.2) > Lois.RisqueParHeure(1.0, 1.2));
        Assert.True(Lois.Vitesse(Pirogue, pagaie: true) > Lois.Vitesse(Pirogue, pagaie: false));
    }

    [Fact]
    public void Voir_SurLeRivageParTempsClair_IlApercoitLAutreTerre_UnFaitMarquantPourLesSiens()
    {
        var c = Comportement();
        var guetteur = Humain(1, Depart);
        guetteur.Continent = Detroit.MasseA;
        var voisin = Humain(2, Depart, curiosite: 0.3, conscience: 0.7);
        voisin.Continent = Detroit.MasseA;
        List<Humain> tous = [guetteur, voisin];
        // Quinze jours sur ce rivage, en heures pleines : il suffit d'un jour assez clair.
        for (var t = Maintenant; t < Maintenant + 15 * Jour && c.Outremer.Liste.Count == 0; t += MondeSimule.SecondesParPas)
        {
            foreach (var h in tous)
            {
                (h.X, h.Y) = Depart;
                Combler(h);
                c.Vivre(h, tous, t);
            }
        }
        var vue = Assert.Single(c.Outremer.Liste);
        Assert.Equal(SorteOutremer.Vue, vue.Sorte);
        Assert.Equal(1, vue.Groupe);
        Assert.Equal(Detroit.MasseB, vue.Masse);
        var e = c.Journal.Single(x => x.Id == vue.EvenementId);
        Assert.True(e.Significatif);
        Assert.StartsWith("mer.voit.", e.Recit!.Cle);
        // Il s'en souvient, et l'a dit à son voisin.
        Assert.Contains(guetteur.Memoire.Lieux, l => l.Type == TypeLieu.TerreAuLoin);
        Assert.All(tous, x => Assert.Contains(x.Memoire.Lieux, l => l.Type == TypeLieu.TerreAuLoin));
    }

    [Fact]
    public void Traverser_EnPirogue_IlPoseLePiedSurLAutreTerre_PuisRentreLeRaconter()
    {
        var c = Comportement(3);
        var h = Humain(1, Depart);
        h.Continent = Detroit.MasseA;
        h.Objets.Add(Bateau(Pirogue));
        h.Savoirs.Add(Sait(Geste.Pousser, Effet.Pagaie, Matiere.Branche, Matiere.Eau));
        h.Nage = 0.8;
        var voyage = Comportement_Voyage(h, MotifMer.Expedition, Arrivee);
        h.Mer = voyage;
        List<Humain> tous = [h];
        var t = Maintenant;
        for (; t < Maintenant + 40 * Jour && h.EstVivant; t += MondeSimule.SecondesParPas)
        {
            Combler(h);
            c.Vivre(h, tous, t);
            if (c.Outremer.Connait(1, Detroit.MasseB, SorteOutremer.Pied) && !h.Mer.Active) break;
        }
        Assert.True(h.EstVivant, string.Join(" | ", Cles(c)));
        Assert.Contains("mer.depart.Expedition", Cles(c));
        var pied = Assert.Single(c.Outremer.Liste, d => d.Sorte == SorteOutremer.Pied);
        Assert.Equal(Detroit.MasseB, pied.Masse);
        Assert.True(c.Journal.Single(e => e.Id == pied.EvenementId).Significatif);
        // Y avoir posé le pied vaut bien l'avoir vue.
        Assert.True(c.Outremer.Connait(1, Detroit.MasseB, SorteOutremer.Vue));
        // Le séjour fini, il rentre chez lui avec le chemin en tête.
        Assert.True(Cles(c).Contains("mer.rentre.vu"), $"{h.Mer} {h.Activite} ({h.X},{h.Y}) {string.Join(",", Cles(c).Where(k => k != "j.pensee"))}");
        Assert.Equal(Detroit.MasseA, Carte.Mer.Masse[Carte.Indice(h.X, h.Y)]);
        Assert.Contains(h.Memoire.Lieux, l => l.Type == TypeLieu.TerreAuLoin && Carte.Mer.Masse[Carte.Indice(l.X, l.Y)] == Detroit.MasseB);
    }

    [Fact]
    public void Contact_SurLAutreTerre_IlRencontreQuelquUnDeCePeuple_UnFaitMarquantDesDeuxCotes()
    {
        var c = Comportement(5);
        var visiteur = Humain(1, Arrivee, communaute: 1);
        visiteur.Continent = Detroit.MasseA;
        var natif = Humain(2, Arrivee, communaute: 2, sexe: Sexe.Femme);
        natif.Continent = Detroit.MasseB;
        List<Humain> tous = [visiteur, natif];
        for (var t = Maintenant; t < Maintenant + Jour && c.Outremer.Liste.Count == 0; t += MondeSimule.SecondesParPas)
        {
            foreach (var h in tous)
            {
                (h.X, h.Y) = Arrivee;
                Combler(h);
                c.Vivre(h, tous, t);
            }
        }
        Assert.Equal(2, c.Outremer.Liste.Count);
        Assert.All(c.Outremer.Liste, d => Assert.Equal(SorteOutremer.Contact, d.Sorte));
        Assert.True(c.Outremer.Connait(1, Detroit.MasseB, SorteOutremer.Contact));
        Assert.True(c.Outremer.Connait(2, Detroit.MasseA, SorteOutremer.Contact));
        var e = Assert.Single(c.Journal, x => x.Recit?.Cle == "mer.contact");
        Assert.True(e.Significatif);
        // Un seul fait, pour les deux peuples à la fois.
        Assert.Equal(2, e.AutreCommunauteId);
    }

    [Fact]
    public void Construire_AuRivageSansBateau_IlEnImagineUnEtSyMet()
    {
        var c = Comportement(7);
        var h = Humain(1, Depart);
        h.Continent = Detroit.MasseA;
        foreach (var m in new[] { Matiere.Tronc, Matiere.Branche, Matiere.Roseau, Matiere.BoisSec, Matiere.Calebasse })
            h.Savoirs.Add(Sait(Geste.Pousser, Effet.Flotte, m, Matiere.Eau));
        h.Savoirs.Add(Sait(Geste.Assembler, Effet.Flottaison));
        h.Mer = Comportement_Voyage(h, MotifMer.Expedition, Arrivee);
        List<Humain> tous = [h];
        for (var t = Maintenant; t < Maintenant + 12 * Jour && !h.Mer.EnMer && h.Mer.Active; t += MondeSimule.SecondesParPas)
        {
            Combler(h);
            c.Vivre(h, tous, t);
        }
        var cles = Cles(c).ToList();
        Assert.True(cles.Contains("mer.idee"), $"{h.Mer} {h.Activite} ({h.X},{h.Y}) {string.Join(",", cles)} autour {string.Join(",", Enumerable.Range(-3, 7).SelectMany(dy => Enumerable.Range(-3, 7).SelectMany(dx => Physique.Disponibles(Carte, h.X + dx, h.Y + dy, false, false))).Distinct())} biome {Carte.BiomeEn(h.X, h.Y)}");
        // Il a imaginé un bateau et l'a fini (que les lois disent ensuite s'il tient la mer ou non).
        Assert.Contains("mer.idee", cles);
        Assert.Contains(cles, k => k.StartsWith("inv.bateau.fini", StringComparison.Ordinal) || k.StartsWith("mer.depart.", StringComparison.Ordinal)
                                   || k.StartsWith("inv.", StringComparison.Ordinal));
    }

    [Fact]
    public void Chavirer_UneTempeteSurLaMer_DesBateauxSeRetournent_EtCeuxQuiNeSaventPasNagerSeNoient()
    {
        var morts = 0;
        var chavires = 0;
        for (var k = 0; k < 20; k++)
        {
            var c = Comportement((ulong)(100 + k));
            var mer = PlusProcheMer(Depart.X, Depart.Y);
            // Un cyclone sur le détroit : des vagues de plusieurs mètres.
            var cyclone = new Catastrophe
            {
                Id = 1, Type = TypeCatastrophe.Cyclone, Debut = Maintenant - Jour, X = mer.X, Y = mer.Y, Rayon = 30, Intensite = 1, Nom = "Tempête",
            };
            c.Catastrophes = new Catastrophes(Carte, new Ecosysteme(Carte), new Alea(1), [cyclone]);
            var h = Humain(1, Depart);
            h.Continent = Detroit.MasseA;
            h.Objets.Add(Bateau(Pirogue, 0.5));
            h.Nage = 0;
            h.Mer = Comportement_Voyage(h, MotifMer.Colon, Arrivee) with { EnMer = true, Masse = Detroit.MasseA };
            (h.X, h.Y) = mer;
            List<Humain> tous = [h];
            for (var t = Maintenant; t < Maintenant + Jour && h.EstVivant && h.Mer.EnMer; t += MondeSimule.SecondesParPas)
            {
                Combler(h);
                c.Vivre(h, tous, t);
            }
            if (!h.EstVivant)
            {
                morts++;
                Assert.Equal(CauseDeMort.Noyade, h.CauseDeMort);
            }
            chavires += Cles(c).Count(x => x == "mer.chavire");
        }
        Assert.True(morts > 0 && chavires > 0, $"chavirés {chavires}, noyés {morts}");
        Assert.True(morts < 20, "certains remontent dans leur pirogue");
    }

    [Fact]
    public void Enregistrer_LeVoyageEnCoursEtLesPremieres_SeRelisent_EtUnMondeV38SeMigre()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-mer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var conf = new ConfigurationMonde(11, 12, 64, 64, 1);
            var carte = GenerateurContinent.Generer(conf);
            var etat = new MondeSimule(carte, MondeSimule.Nouveau(conf)).Etat;
            var h = etat.Humains![0];
            h.Continent = 1;
            h.Mer = new Traversee(MotifMer.Expedition, true, 3, 4, 50, 60, 1234, 0.25, -0.5, 12.5, 0.4, Pirogue.Signature, 0, 1, true);
            var outremer = new List<DecouverteOutremer> { new(7, 2, SorteOutremer.Pied, 999, 10, 11, h.Id, 42) };
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat with { Outremer = outremer });
            var relu = new DepotMonde(fichier).Charger()!;
            var r = relu.Humains![0];
            Assert.Equal(1, r.Continent);
            Assert.Equal(h.Mer, r.Mer);
            Assert.Equal(outremer, relu.Outremer);

            // Un monde du schéma 38 : ni peuple, ni voyage, ni premières ; il se relit, et la simulation reprend.
            using (var connexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fichier, Pooling = false }.ToString()))
            {
                connexion.Open();
                using var c = connexion.CreateCommand();
                c.CommandText = "ALTER TABLE humains DROP COLUMN continent; ALTER TABLE humains DROP COLUMN mer; DROP TABLE outremer; PRAGMA user_version = 38;";
                c.ExecuteNonQuery();
            }
            var migre = new DepotMonde(fichier).Charger()!;
            Assert.Equal(0, migre.Humains![0].Continent);
            Assert.False(migre.Humains[0].Mer.Active);
            Assert.True(migre.Outremer is null || migre.Outremer.Count == 0);
            var monde = new MondeSimule(carte, migre);
            monde.AvancerDe(Jour);
            Assert.All(monde.Etat.Humains!.Where(x => x.EstVivant), x => Assert.NotEqual(0, x.Continent));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    private static Traversee Comportement_Voyage(Humain h, MotifMer motif, (int X, int Y) cible) =>
        Simulation.Humains.Comportement.Voyage(Carte, h, motif, cible.X, cible.Y, Maintenant) ?? throw new InvalidOperationException("pas de rive");

    private static (int X, int Y) PlusProcheMer(int x, int y)
    {
        for (var r = 1; r < 10; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
            if (Carte.Mer.Masse[Carte.Indice(x + dx, y + dy)] == 0) return (x + dx, y + dy);
        throw new InvalidOperationException("pas de mer");
    }
}
