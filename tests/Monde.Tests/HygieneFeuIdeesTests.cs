using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.25 : bébés portés, hygiène, leçons du feu, idées spontanées.</summary>
public sealed class HygieneFeuIdeesTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static Humain Humain(int id, double age = 25, Sexe sexe = Sexe.Femme, double attachement = 0.9, int mere = 0,
        double nevrosisme = 0.5, double ouverture = 0.5, int communaute = 0) => new()
    {
        Id = id, Nom = "H" + id, Sexe = sexe, PartenaireId = 0, Emplacement = 1, Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(ouverture, 0.5, 0.5, 0.5, nevrosisme, 0.5, attachement, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe), Reserves = 50, Sante = 100, MereId = mere, CommunauteId = communaute,
        X = Carte.Emplacements[0].X, Y = Carte.Emplacements[0].Y,
    };

    private static Comportement Comportement(ulong graine = 1) =>
        new(Carte, new Ecosysteme(Carte), new Alea(graine)) { Social = new VieSociale(new Alea(graine)) };

    private static Savoir Sait(Geste g, Matiere a, Matiere b, Effet e) =>
        new(g, a, b, e, 0, Symptome.Aucun, 10, 9, Maintenant, Maintenant, OrigineSavoir.Essai, 0, 0);

    [Fact]
    public void Bebe_UneMerePeuAttacheeLePorte_SaufALaLimite_EtRevientLeChercher()
    {
        var mere = Humain(1, attachement: 0.05);
        mere.Faim = 50;
        Assert.False(Comportement_Delaisse(mere));
        mere.Faim = 95;
        Assert.True(Comportement_Delaisse(mere));

        // Posé à trois cases, il est repris, même par une mère peu attachée qui n'est pas à bout.
        mere.Faim = 30;
        mere.DernierAccouchement = Maintenant - 30 * Jour;
        var bebe = Humain(2, age: 30.0 / 360, sexe: Sexe.Homme, mere: 1);
        bebe.X += 3;
        Assert.Same(bebe, Monde.Simulation.Humains.Comportement.EnfantAVeiller(mere, [mere, bebe], Maintenant));
        // À bout de forces, elle ne revient pas.
        mere.Faim = 95;
        Assert.Null(Monde.Simulation.Humains.Comportement.EnfantAVeiller(mere, [mere, bebe], Maintenant));

        static bool Comportement_Delaisse(Humain m) => Monde.Simulation.Humains.Comportement.Delaisse(m);
    }

    [Fact]
    public void Hygiene_ProtegeDuVentreSeulement_EtLePetitDeCeuxQuiLeNourrissent()
    {
        var sale = Humain(1);
        var propre = Humain(2);
        propre.Savoirs.Add(Sait(Geste.Laver, Matiere.Eau, Matiere.Aucune, Effet.MainsPropres));
        Assert.Equal(0, Hygiene.Protection(sale, null, Maladie.Diarrhee, 0, false));
        var mains = Hygiene.Protection(propre, null, Maladie.Diarrhee, 0, false);
        Assert.InRange(mains, 0.3, 0.47);
        Assert.Equal(0, Hygiene.Protection(propre, null, Maladie.Pneumonie, 0, false));

        // Un petit dont la mère se lave les mains en profite.
        var petit = Humain(3, age: 2, mere: 2);
        Assert.Equal(mains, Hygiene.Protection(petit, propre, Maladie.Diarrhee, 0, false), 6);

        // Les excréments écartés protègent surtout quand toute la communauté le fait.
        var seul = Hygiene.Protection(sale, null, Maladie.Dysenterie, 0.1, false);
        var tous = Hygiene.Protection(sale, null, Maladie.Dysenterie, 1, false);
        Assert.True(tous > seul + 0.15, $"{tous:0.00} contre {seul:0.00}");

        // L'eau qui court n'aide que celui qui buvait à une eau qui dort.
        propre.Savoirs.Add(Sait(Geste.Puiser, Matiere.Eau, Matiere.Aucune, Effet.EauVive));
        Assert.True(Hygiene.Protection(propre, null, Maladie.Diarrhee, 0, true) > Hygiene.Protection(propre, null, Maladie.Diarrhee, 0, false));
    }

    [Fact]
    public void Hygiene_ApresDesMauxDeVentre_OnCherchePuisOnTrouveUneCause_JusteOuFausse()
    {
        var c = Comportement(3);
        var h = Humain(1, nevrosisme: 0.9, ouverture: 0.9);
        var tous = new List<Humain> { h };
        var trouve = false;
        for (var jour = 0; jour < 400 && !trouve; jour++)
        {
            var t = Maintenant + jour * Jour;
            h.Maladie = Maladie.Diarrhee;
            h.MaladeJusqua = t - Jour / 2;
            h.Savoirs.Clear();
            c.Hygienes(tous, t);
            trouve = h.Savoirs.Count > 0;
        }
        Assert.True(trouve);
        var s = Assert.Single(h.Savoirs);
        Assert.True(Hygiene.EstPratique(s.Effet) || s.Effet == Effet.Poison && s.Espece > 0, s.Effet.ToString());
        Assert.Equal(OrigineSavoir.Reflexion, s.Origine);
        Assert.Contains(c.Journal, e => e.Type == TypeEvenement.Decouverte && e.Texte.Contains("maux de ventre"));
    }

    [Fact]
    public void Feu_UneViandeFumeeSeGardeUnMois_UneOutreGardeLEau()
    {
        var alea = new Alea(5);
        var contexte = new ContexteGeste(false, true, 0.5, Biome.PlaineDeFougeres, 0.8, 0.5);
        var remplie = 0;
        for (var i = 0; i < 200; i++)
            if (Physique.Tenter(Geste.Remplir, Matiere.Peau, Matiere.Eau, contexte, alea).Effet == Effet.EauPortee) remplie++;
        Assert.InRange(remplie, 60, 199);
        Assert.Equal(Effet.Rien, Physique.Tenter(Geste.Remplir, Matiere.Peau, Matiere.Eau, contexte with { EauProche = false }, alea).Effet);
        Assert.Contains("fumée", Physique.Decrire(Sait(Geste.Fumer, Matiere.Carcasse, Matiere.Feu, Effet.Fumage)));
    }

    [Fact]
    public void Feu_MettreLeFeuAuxHerbes_AllumeUnIncendieQuiCiteSaCause()
    {
        var eco = new Ecosysteme(Carte);
        var catastrophes = new Catastrophes(Carte, eco, new Alea(7));
        var humains = new List<Humain>();
        var (x, y) = Enumerable.Range(0, Carte.Largeur * Carte.Hauteur).Select(i => (X: i % Carte.Largeur, Y: i / Carte.Largeur))
            .First(p => Carte.BiomeEn(p.X, p.Y) == Biome.PlaineDeFougeres);
        catastrophes.MettreLeFeu(x, y, humains, Maintenant, 1234);
        var feu = Assert.Single(catastrophes.EnMemoire, k => k.Type == TypeCatastrophe.Incendie);
        Assert.True(catastrophes.BruleAutour(x, y));
        Assert.Contains(catastrophes.Journal, e => e.CauseId == 1234);
        // Une terre qui brûle déjà ne s'allume pas une seconde fois.
        catastrophes.MettreLeFeu(x, y, humains, Maintenant, 1235);
        Assert.Single(catastrophes.EnMemoire, k => k.Type == TypeCatastrophe.Incendie);
        Assert.Equal(feu.Id, catastrophes.EnMemoire.Single(k => k.Type == TypeCatastrophe.Incendie).Id);
    }

    [Fact]
    public void Idee_ParAnalogie_UnEssaiEtUneConclusion()
    {
        var c = Comportement(11);
        var h = Humain(1, ouverture: 1);
        h.Personnalite = h.Personnalite with { Intelligence = Intelligence.Moyenne with { Generale = 140 } };
        // Il sait tirer des étincelles d'un silex contre une pyrite ; il a un galet et de l'obsidienne sous la main.
        h.Savoirs.Add(Sait(Geste.Frapper, Matiere.Silex, Matiere.Pyrite, Effet.Etincelle));
        h.Objets.AddRange([new Objet(Matiere.Silex, 1, 1), new Objet(Matiere.Pyrite, 1, 1), new Objet(Matiere.Galet, 1, 1), new Objet(Matiere.Obsidienne, 1, 1)]);
        var tous = new List<Humain> { h };
        c.Hygienes(tous, Maintenant); // fixe l'instant du comportement
        var essais = 0;
        for (var i = 0; i < 20_000 && essais == 0; i++)
            if (c.AvoirUneIdee(h, tous)) essais++;
        Assert.Equal(1, essais);
        var idee = Assert.Single(c.Journal, e => e.Texte.Contains("se demande : si frapper"));
        // Juste (un nouveau savoir, venu de la réflexion) ou fausse (il en garde l'idée que ce n'est pas possible).
        Assert.True(h.Savoirs.Exists(s => s.Origine == OrigineSavoir.Reflexion) || h.Memoire.Jardin.Exists(j => j.Cle.StartsWith("idee:")), idee.Texte);
    }

    [Fact]
    public void Monde_Avec_LesNouveautes_Avance_SansErreur()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        monde.AvancerDe(120 * Jour);
        Assert.Contains(monde.Etat.Humains!, x => x.EstVivant);
    }
}
