using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Physique inventive : propriétés des matières, objets assemblés jugés par les lois, invention par besoin et analogie.</summary>
public sealed class InventionTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Assemblage A(Matiere corps, int n, Matiere tete, int k, Matiere lien, Montage m) => new(corps, n, tete, k, lien, m);

    private static Humain Humain(int id, int x, int y, double age = 25) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = Sexe.Homme,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.9, 0.5, 0.5, 0.5, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne with { Generale = 130, Manuelle = 125 }),
        Biologie = Biologie.ParDefaut(Sexe.Homme),
        X = x,
        Y = y,
    };

    private static Savoir Sait(Geste g, Matiere a, Matiere b, Effet e) =>
        new(g, a, b, e, 0, Symptome.Aucun, 6, 6, 0, Maintenant, OrigineSavoir.Essai, 0, 0);

    [Fact]
    public void Assemblage_LaSignatureSeRelit_EtUneFormeMalFaiteEstRefusee()
    {
        var a = A(Matiere.Branche, 4, Matiere.RondellePercee, 2, Matiere.Corde, Montage.Axe);
        Assert.Equal(a, Assemblage.Lire(a.Signature));
        foreach (var faux in new[] { null, "", "6x4", "6x4+27x2/23", "6x4+27x2/23/99", "999x1+0x0/0/0", "6x-1+0x0/0/0", "6x0+0x0/0/0",
                     "6x4+27x2/23/4; DROP TABLE objets", new string('1', 200) })
            Assert.Null(Assemblage.Lire(faux));
    }

    [Fact]
    public void Lois_UnTroncSeulRoule_DeuxTroncsLiesPortentUnHumain_DesBranchesCoulent()
    {
        Assert.Contains("roule sur lui-même", Lois.Flotter(A(Matiere.Tronc, 1, 0, 0, 0, Montage.Dessous), 60, false).Raison.Texte);
        Assert.Equal(Effet.Flottaison, Lois.Flotter(A(Matiere.Tronc, 2, 0, 0, Matiere.Fibres, Montage.Cote), 60, false).Effet);
        Assert.False(Lois.Flotter(A(Matiere.Tronc, 2, 0, 0, Matiere.Aucune, Montage.Cote), 60, false).Reussi);
        Assert.Contains("s'enfonce", Lois.Flotter(A(Matiere.Branche, 8, 0, 0, Matiere.Corde, Montage.Cote), 60, false).Raison.Texte);
        // La résine ne colle que chauffée.
        Assert.False(Lois.Flotter(A(Matiere.Tronc, 3, 0, 0, Matiere.Resine, Montage.Cote), 60, false).Reussi);
        Assert.True(Lois.Flotter(A(Matiere.Tronc, 3, 0, 0, Matiere.Resine, Montage.Cote), 60, true).Reussi);
    }

    [Fact]
    public void Lois_TrainerDependDuSol_LesRondellesLibresSEchappent_LaRoueSurUnAxePasse()
    {
        double t = Lois.Traction(0.8), s = Lois.Soulever(0.8);
        var traineau = A(Matiere.Branche, 4, 0, 0, Matiere.Corde, Montage.Cote);
        Assert.Equal(Effet.Glisser, Lois.Transporter(traineau, 40, Biome.PlaineDeFougeres, t, s, false).Effet);
        Assert.Contains("trop lourde à traîner", Lois.Transporter(traineau with { NCorps = 6 }, 60, Biome.ForetTropicale, t, s, false).Raison.Texte);
        Assert.Contains("cèdent", Lois.Transporter(traineau with { NCorps = 2 }, 70, Biome.PlaineDeFougeres, t, s, false).Raison.Texte);

        var rondelles = A(Matiere.Rondelle, 3, 0, 0, 0, Montage.Dessous);
        Assert.Contains("s'échappent", Lois.Transporter(rondelles, 60, Biome.PlaineDeFougeres, t, s, false).Raison.Texte);
        Assert.Equal(Effet.Rouleaux, Lois.Transporter(A(Matiere.Tronc, 3, 0, 0, 0, Montage.Dessous), 60, Biome.PlaineDeFougeres, t, s, false).Effet);
        Assert.False(Lois.Transporter(A(Matiere.Tronc, 3, 0, 0, 0, Montage.Dessous), 60, Biome.Marais, t, s, false).Reussi);

        var chariot = A(Matiere.Branche, 6, Matiere.RondellePercee, 2, Matiere.Corde, Montage.Axe);
        Assert.Equal(Effet.RouleSurAxe, Lois.Transporter(chariot, 60, Biome.ForetTropicale, t, s, false).Effet);
        Assert.Contains("traverse", Lois.Transporter(chariot with { Tete = Matiere.Rondelle }, 60, Biome.ForetTropicale, t, s, false).Raison.Texte);
        // Une seule roue : on soulève une part de la charge par les deux bras.
        var une = chariot with { NTete = 1 };
        Assert.Equal(Effet.RouleSurAxe, Lois.Transporter(une, 50, Biome.PlaineDeFougeres, t, s, false).Effet);
        Assert.Contains("soulever", Lois.Transporter(une with { NCorps = 10 }, 150, Biome.PlaineDeFougeres, t, s, false).Raison.Texte);
        Assert.Equal("axe:1", Lois.Structure(une.Signature));
        Assert.Equal("axe:2", Lois.Structure(chariot.Signature));
    }

    [Fact]
    public void Lois_UnePierreLieeAuBoutPerce_EnTraversFrappe_SansLienRienNeTient()
    {
        var alea = new Alea(3);
        Assert.Equal(Effet.PointeEmmanchee, Lois.Emmancher(A(Matiere.Branche, 1, Matiere.Eclat, 1, Matiere.Corde, Montage.Bout), false, 1, alea).Effet);
        Assert.Equal(Effet.TranchantEmmanche, Lois.Emmancher(A(Matiere.Branche, 1, Matiere.Galet, 1, Matiere.Corde, Montage.Travers), false, 1, alea).Effet);
        Assert.Contains("rien ne retient", Lois.Emmancher(A(Matiere.Branche, 1, Matiere.Eclat, 1, 0, Montage.Bout), false, 1, alea).Raison.Texte);
        Assert.Contains("pas de pointe", Lois.Emmancher(A(Matiere.Branche, 1, Matiere.Galet, 1, Matiere.Corde, Montage.Bout), false, 1, alea).Raison.Texte);
        Assert.Contains("pas plus dure", Lois.Emmancher(A(Matiere.Branche, 1, Matiere.Branche, 1, Matiere.Corde, Montage.Bout), false, 1, alea).Raison.Texte);
        var lance = new Objet(Matiere.Assemblage, 0.7, 1, Pieces: A(Matiere.Branche, 1, Matiere.Eclat, 1, Matiere.Corde, Montage.Bout).Signature);
        Assert.True(Lois.Chasse(lance) > Lois.Chasse(new Objet(Matiere.Epieu, 0.7, 1)));
        var hache = new Objet(Matiere.Assemblage, 0.7, 1, Pieces: A(Matiere.Branche, 1, Matiere.Galet, 1, Matiere.Corde, Montage.Travers).Signature);
        Assert.True(Lois.Coupe(hache) > 4 * Lois.Coupe(new Objet(Matiere.Eclat, 0.7, 1)));
    }

    [Fact]
    public void Physique_LeRondRouleSurSolDur_LeBoisFlotte_UnTroncSeTrancheMieuxALaHache()
    {
        int Compter(Geste g, Matiere a, Matiere b, ContexteGeste c, Effet e)
        {
            var alea = new Alea(1);
            var n = 0;
            for (var i = 0; i < 2000; i++) if (Physique.Tenter(g, a, b, c, alea).Effet == e) n++;
            return n;
        }
        var plaine = new ContexteGeste(false, true, 0.5, Biome.PlaineDeFougeres, 0.5, 0.5);
        Assert.Equal(2000, Compter(Geste.Pousser, Matiere.Tronc, Matiere.Eau, plaine, Effet.Flotte));
        Assert.Equal(0, Compter(Geste.Pousser, Matiere.Galet, Matiere.Eau, plaine, Effet.Flotte));
        Assert.InRange(Compter(Geste.Pousser, Matiere.Galet, Matiere.Aucune, plaine, Effet.Roule), 1200, 2000);
        Assert.InRange(Compter(Geste.Pousser, Matiere.Galet, Matiere.Aucune, plaine with { Biome = Biome.Marais }, Effet.Roule), 0, 300);
        Assert.Equal(0, Compter(Geste.Pousser, Matiere.Branche, Matiere.Aucune, plaine, Effet.Roule));
        var main = Compter(Geste.Trancher, Matiere.Tronc, Matiere.Eclat, plaine, Effet.Rondelle);
        var hache = Compter(Geste.Trancher, Matiere.Tronc, Matiere.Assemblage, plaine with { Coupe = 0.7 }, Effet.Rondelle);
        Assert.True(hache > 5 * main, $"{hache} contre {main}");
        Assert.True(Compter(Geste.Percer, Matiere.Rondelle, Matiere.Eclat, plaine, Effet.Percage) > 10 * Compter(Geste.Percer, Matiere.Rondelle, Matiere.BatonPointu, plaine, Effet.Percage));
    }

    /// <summary>Une case où poussent des arbres, au bord d'un lac ou d'une eau peu profonde.</summary>
    private static (int X, int Y) Rive()
    {
        for (var y = 1; y < Carte.Hauteur - 1; y++)
        for (var x = 1; x < Carte.Largeur - 1; x++)
        {
            if (Carte.BiomeEn(x, y) is not (Biome.ForetDeConiferes or Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Marais)) continue;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (Carte.BiomeEn(x + dx, y + dy) is Biome.Lac or Biome.EauPeuProfonde) return (x, y);
        }
        throw new InvalidOperationException("pas de rive boisée");
    }

    [Fact]
    public void Invention_QuiAVuFlotterLeBois_ALIdeeDUnRadeau_LeRefait_EtUnAutreLApprendEnRegardant()
    {
        var (x, y) = Rive();
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(11)) { Social = new VieSociale(new Alea(11)) };
        var h = Humain(1, x, y);
        h.Savoirs.Add(Sait(Geste.Pousser, Matiere.Tronc, Matiere.Eau, Effet.Flotte));
        h.Savoirs.Add(Sait(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde));
        var temoin = Humain(2, x, y, age: 16);
        temoin.Personnalite = temoin.Personnalite with { Ouverture = 1 };
        h.Liens.Add(new Lien(2, 90, 80, Maintenant, Amis: true, Rivaux: false));
        temoin.Liens.Add(new Lien(1, 90, 80, Maintenant, Amis: true, Rivaux: false));
        var tous = new List<Humain> { h, temoin };
        h.Objets.Add(new Objet(Matiere.Corde, 1, 1));

        (Verdict Verdict, Objet? Objet)? r = null;
        for (var i = 0; i < 200 && r is not { Verdict.Effet: Effet.Flottaison }; i++)
        {
            if (!h.Porte(Matiere.Corde)) h.Objets.Add(new Objet(Matiere.Corde, 1, 1));
            r = c.RepondreAuBesoin(h, Comportement.Besoin.Traverser, 60, 1, tous, Maintenant + i * 600);
        }
        Assert.Equal(Effet.Flottaison, r!.Value.Verdict.Effet);
        var savoir = Assert.Single(h.Savoirs, s => s.Effet == Effet.Flottaison);
        Assert.Equal(OrigineSavoir.Reflexion, savoir.Origine);
        Assert.Equal(Geste.Assembler, savoir.Geste);
        Assert.NotNull(Assemblage.Lire(savoir.Pieces));
        Assert.Contains(h.Objets, o => o.Matiere == Matiere.Assemblage && o.Pieces == savoir.Pieces);
        Assert.Contains(c.Journal, j => j.Significatif && j.Texte.Contains("Première fois au monde") && j.Texte.Contains("Cela marche"));
        Assert.Contains(c.Installations.Decouvertes, d => d.Effet == Effet.Flottaison && d.Pieces == savoir.Pieces);

        // Il le refait sans avoir à y repenser (au moins une fois), et le temoin finit par l'apprendre en le regardant (parfois dès
        // la première fois).
        for (var i = 0; i < 400 && (i == 0 || !temoin.Savoirs.Exists(s => s.Effet == Effet.Flottaison)); i++)
        {
            h.Objets.RemoveAll(o => o.Matiere == Matiere.Assemblage);
            if (!h.Porte(Matiere.Corde)) h.Objets.Add(new Objet(Matiere.Corde, 1, 1));
            (temoin.X, temoin.Y) = (x, y);
            Assert.NotNull(c.RepondreAuBesoin(h, Comportement.Besoin.Traverser, 60, 0, tous, Maintenant + (1000 + i) * 600));
        }
        var appris = Assert.Single(temoin.Savoirs, s => s.Effet == Effet.Flottaison);
        Assert.Equal(OrigineSavoir.Imitation, appris.Origine);
        Assert.Equal(1, appris.MaitreId);
        Assert.True(h.Savoirs.Find(s => s.Effet == Effet.Flottaison)!.Succes > 1);
    }

    [Fact]
    public void Invention_SansSavoirQueLeBoisFlotte_PersonneNInventeLeRadeau()
    {
        var (x, y) = Rive();
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(4));
        var h = Humain(1, x, y);
        h.Objets.Add(new Objet(Matiere.Corde, 1, 1));
        for (var i = 0; i < 300; i++) Assert.Null(c.RepondreAuBesoin(h, Comportement.Besoin.Traverser, 60, 1, [h], Maintenant + i * 600));
        Assert.DoesNotContain(h.Savoirs, s => s.Effet == Effet.Flottaison);
    }

    [Fact]
    public void Invention_UneIdeeQuiAEchoueEstRetenue_EtNEstPasRefaiteALIdentique()
    {
        var (x, y) = Rive();
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(8));
        var h = Humain(1, x, y);
        // Il a vu flotter le bois, mais n'a ni corde ni idée de lien : il s'agrippe à un tronc, qui roule.
        h.Savoirs.Add(Sait(Geste.Pousser, Matiere.Tronc, Matiere.Eau, Effet.Flotte));
        h.Personnalite = h.Personnalite with { Ouverture = 0, Intelligence = Intelligence.Moyenne with { Generale = 56, Manuelle = 56 } };
        for (var i = 0; i < 300; i++) c.RepondreAuBesoin(h, Comportement.Besoin.Traverser, 60, 1, [h], Maintenant + i * 600);
        var echecs = c.Journal.Where(j => j.Texte.Contains("Mais ")).ToList();
        Assert.NotEmpty(echecs);
        Assert.Equal(echecs.Count, echecs.Select(j => j.Texte).Distinct().Count());
        var retenu = Assert.Single(h.Savoirs, s => s.Effet == Effet.Rien && s.Geste == Geste.Assembler && s.Pieces.Length > 0);
        Assert.Contains("ne marche pas", Physique.Decrire(retenu).Texte);
    }

    [Fact]
    public void Viande_UnAffameRecoitLaViandeQuUnProchePorte()
    {
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(4)) { Social = new VieSociale(new Alea(4)) };
        var chasseur = Humain(1, 10, 10);
        var affame = Humain(2, 10, 11);
        affame.Faim = 70;
        chasseur.Liens.Add(new Lien(2, 60, 50, Maintenant, Amis: true, Rivaux: false));
        affame.Liens.Add(new Lien(1, 60, 50, Maintenant, Amis: true, Rivaux: false));
        chasseur.Objets.Add(new Objet(Matiere.Carcasse, 0.2, 1));
        c.RecevoirViande(affame, [chasseur, affame], Maintenant);
        Assert.True(affame.Faim < 10);
        Assert.True(chasseur.Objets.Single(o => o.Matiere == Matiere.Carcasse).Qualite < 0.2);
    }
}
