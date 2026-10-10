using Microsoft.AspNetCore.SignalR.Client;
using Monde.Serveur;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>Nature vivante, réserves du corps, fécondité selon l'énergie, disettes, migrations et relations entre communautés.</summary>
public sealed class RessourcesTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;

    private static Humain Humain(int id, int x, int y, Sexe sexe = Sexe.Femme, double age = 25, double reserves = 50) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = 10 * An - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
        Reserves = reserves,
    };

    /// <summary>Vide les ressources d'un carré de terres.</summary>
    private static void Epuiser(Ecosysteme eco, int cx, int cy, int rayon)
    {
        for (var y = Math.Max(0, cy - rayon); y <= Math.Min(Carte.Hauteur - 1, cy + rayon); y++)
        {
            for (var x = Math.Max(0, cx - rayon); x <= Math.Min(Carte.Largeur - 1, cx + rayon); x++)
            {
                eco.Plantes[Carte.Indice(x, y)] = 0;
                eco.Gibier[Carte.Indice(x, y)] = 0;
            }
        }
    }

    /// <summary>Tous liés d'amitié deux à deux (dans le groupe seulement).</summary>
    private static void Lier(IReadOnlyList<Humain> groupe, double affection = 60)
    {
        foreach (var a in groupe)
        {
            foreach (var b in groupe)
            {
                if (a.Id != b.Id) a.Liens.Add(new Lien(b.Id, 60, affection, 10 * An, Amis: affection >= 40));
            }
        }
    }

    // ───────────── Nature ─────────────

    [Fact]
    public void Gibier_Epuise_RevientDepuisLesTerresVoisines_EtSansSourceNeRevientPas()
    {
        var e = Carte.Emplacements[0];
        var eco = new Ecosysteme(Carte);
        Epuiser(eco, e.X, e.Y, 10);
        var centre = Carte.Indice(e.X, e.Y);

        for (var jour = 0; jour < An / Jour; jour++) eco.Journee(jour * Jour);
        var bordUnAn = eco.Gibier[Carte.Indice(e.X + 9, e.Y)];
        Assert.True(bordUnAn > eco.Gibier[centre], "les bêtes reviennent d'abord par les bords");
        Assert.True(eco.Abondance(e.X, e.Y, 10) < 0.8, "en un an, le gibier n'est pas revenu partout");

        for (var jour = An / Jour; jour < 15 * An / Jour; jour++) eco.Journee(jour * Jour);
        Assert.True(eco.Gibier[centre] > 0.5 * eco.NiveauGibier(centre), "quinze ans plus tard, il a repeuplé les lieux");

        // Plus aucune bête nulle part : rien ne revient (effet Allee, pas de génération spontanée).
        var desert = new Ecosysteme(Carte, gibier: new ushort[Carte.Gibier.Length]);
        for (var jour = 0; jour < 60; jour++) desert.Journee(jour * Jour);
        Assert.All(desert.Gibier, g => Assert.Equal(0, g));
    }

    [Fact]
    public void Plantes_MaigresEnHiver_EtLesPluiesVarientDUneAnneeALAutre()
    {
        var eco = new Ecosysteme(Carte);
        var i = Array.FindIndex(Carte.Plantes, p => p >= 60);
        var debutHiver = 3 * Calendrier.JoursParSaison;
        for (var jour = 0; jour < 4 * Calendrier.JoursParSaison; jour++)
        {
            eco.Journee(jour * Jour);
            if (jour == debutHiver + 60) Assert.True(eco.Plantes[i] < 0.6 * Carte.Plantes[i] * 100, "l'hiver est maigre");
        }
        Assert.True(Ecosysteme.FacteurSaison(45 * Jour) > Ecosysteme.FacteurSaison(315 * Jour));

        var alea = new Alea(3);
        var annees = new List<double>();
        for (var a = 0; a < 300; a++)
        {
            eco.NouvelleAnnee(alea);
            annees.AddRange(eco.Pluies);
        }
        Assert.All(annees, p => Assert.InRange(p, 0.35, 2.0));
        // Une sécheresse (moins de 70 % des pluies) touche une région environ une année sur dix.
        Assert.InRange(annees.Count(p => p < 0.7) / (double)annees.Count, 0.04, 0.2);
        Assert.InRange(annees.Average(p => Math.Log(p)), -0.05, 0.05);
    }

    [Fact]
    public void NouvelleAnnee_UneSecheresseEstJournalisee()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        var types = new HashSet<TypeEvenement>();
        for (var a = 0; a < 12 && !types.Contains(TypeEvenement.Nature); a++)
        {
            monde.AvancerDe(An);
            types.UnionWith(monde.Etat.JournalNonEnregistre!.Select(e => e.Type));
        }
        var nature = monde.Etat.JournalNonEnregistre!.Where(e => e.Type == TypeEvenement.Nature).ToList();
        Assert.NotEmpty(nature);
        Assert.All(nature, e => Assert.Contains("du continent", e.Texte));
    }

    // ───────────── Réserves et fécondité ─────────────

    [Fact]
    public void Reserves_RalentissentLaFaim_EtSeRemplissentJusquAuNiveauHabituel()
    {
        var gras = Humain(1, 0, 0, reserves: 60);
        var maigre = Humain(2, 0, 0, reserves: 0);
        gras.Faim = maigre.Faim = 60;
        for (var pas = 0; pas < 100; pas++)
        {
            Comportement.Depenser(gras, 0.23);
            Comportement.Depenser(maigre, 0.23);
        }
        Assert.Equal(83, maigre.Faim, precision: 6);
        Assert.InRange(gras.Faim, 60, 70);
        Assert.InRange(gras.Reserves, 58, 60);

        var rassasie = Humain(3, 0, 0, reserves: 10);
        for (var pas = 0; pas < 400 * 144; pas++)
        {
            rassasie.Faim = 10;
            Comportement.Depenser(rassasie, 0);
        }
        Assert.InRange(rassasie.Reserves, 60, Comportement.ReservesDeConfort);
    }

    [Fact]
    public void Conception_BloqueeQuandLesReservesSontEpuisees()
    {
        var comportement = new Comportement(Carte, new Ecosysteme(Carte), new Alea(1));
        var homme = Humain(2, 0, 0, Sexe.Homme);
        var femme = Humain(1, 0, 0, reserves: 60);
        var tous = new List<Humain> { femme, homme };
        var maintenant = 10 * An;

        var bienNourrie = comportement.ChanceConception(femme, homme, tous, maintenant);
        femme.Reserves = 25;
        var maigre = comportement.ChanceConception(femme, homme, tous, maintenant);
        femme.Reserves = 5;
        var affamee = comportement.ChanceConception(femme, homme, tous, maintenant);

        Assert.True(bienNourrie > 0);
        Assert.InRange(maigre, 0.3 * bienNourrie, 0.7 * bienNourrie);
        Assert.Equal(0, affamee);
    }

    [Fact]
    public void Metabolisme_PropreAChacun_EtEnPartieHerite()
    {
        var alea = new Alea(5);
        var parents = Enumerable.Range(0, 2000).Select(_ => Biologie.Tirer(Sexe.Femme, alea)).ToList();
        var m = parents.Average(b => b.Metabolisme);
        Assert.InRange(m, 0.99, 1.01);
        Assert.InRange(Math.Sqrt(parents.Average(b => (b.Metabolisme - m) * (b.Metabolisme - m))), 0.06, 0.08);

        var mere = Humain(1, 0, 0);
        var mereRapide = new Humain
        {
            Id = 1, Nom = "M", Sexe = Sexe.Femme, PartenaireId = 0, Emplacement = 1, Naissance = 0,
            Personnalite = mere.Personnalite, Biologie = mere.Biologie with { Metabolisme = 1.2 },
        };
        var enfants = Enumerable.Range(0, 2000).Select(_ => Biologie.Heriter(Sexe.Homme, mereRapide, mereRapide, alea).Metabolisme).ToList();
        // h² = 0,4 : l'enfant garde 40 % de l'écart de ses parents (1,08 en moyenne).
        Assert.InRange(enfants.Average(), 1.07, 1.09);
    }

    // ───────────── Disette, migration, relations ─────────────

    /// <summary>Six amis sur un même lieu, réunis en communauté.</summary>
    private static (VieSociale Social, List<Humain> Tous, List<Communaute> Communautes) Groupe(int x, int y, int premier = 1,
        List<Humain>? deja = null, VieSociale? social = null)
    {
        social ??= new VieSociale(new Alea(4));
        var tous = deja ?? [];
        var groupe = Enumerable.Range(premier, 6).Select(i => Humain(i, x + i % 2, y)).ToList();
        Lier(groupe);
        tous.AddRange(groupe);
        return (social, tous, []);
    }

    [Fact]
    public void Disette_LeGroupePartVersLesTerresRichesDontSeSouvientUnMembre()
    {
        var e = Carte.Emplacements[0];
        var eco = new Ecosysteme(Carte);
        Epuiser(eco, e.X, e.Y, 12);
        var (social, tous, communautes) = Groupe(e.X, e.Y);
        social.Communautes = communautes;
        var maintenant = 10 * An;
        social.Regrouper(tous, communautes, maintenant, new HashSet<string>());
        var c = Assert.Single(communautes);

        // H4 se souvient d'un coin riche à une vingtaine de cases.
        var riche = Enumerable.Range(0, Carte.Biomes.Length)
            .Select(i => (X: i % Carte.Largeur, Y: i / Carte.Largeur))
            .Where(p => Math.Max(Math.Abs(p.X - e.X), Math.Abs(p.Y - e.Y)) is >= 20 and <= 30)
            .First(p => eco.Abondance(p.X, p.Y, 4) > 0.8 && Climat.EstPraticable(Carte.BiomeEn(p.X, p.Y)));
        tous[3].Memoire.Lieux.Add(new LieuConnu(TypeLieu.Nourriture, riche.X, riche.Y, maintenant));

        social.Territoires(tous, communautes, eco, Carte, maintenant);

        Assert.True(c.Disette);
        Assert.True(c.Abondance < 0.3);
        Assert.Equal((riche.X, riche.Y), (c.DestinationX, c.DestinationY));
        Assert.Contains(social.Journal, j => j.Texte.StartsWith("La disette gagne la communauté de"));
        Assert.Contains(social.Journal, j => j.Texte.Contains("dont se souvient H4") && j.HumainId == 4);

        // Un membre sans besoin pressant suit le mouvement.
        var comportement = new Comportement(Carte, eco, new Alea(9)) { Social = social };
        var marcheur = tous[1];
        marcheur.Activite = Activite.Migrer;
        var depart = Math.Max(Math.Abs(marcheur.X - riche.X), Math.Abs(marcheur.Y - riche.Y));
        comportement.Vivre(marcheur, tous, maintenant + 12 * Calendrier.SecondesParHeure);
        Assert.Equal(Activite.Migrer, marcheur.Activite);
        Assert.True(Math.Max(Math.Abs(marcheur.X - riche.X), Math.Abs(marcheur.Y - riche.Y)) < depart);

        // Arrivés sur place et bien nourris : ils s'installent, la disette prend fin.
        foreach (var h in tous)
        {
            (h.X, h.Y) = (riche.X, riche.Y);
            h.Reserves = 60;
        }
        social.Journal.Clear();
        social.Territoires(tous, communautes, eco, Carte, maintenant + 7 * Jour);
        Assert.False(c.Disette);
        Assert.Equal(-1, c.DestinationX);
        Assert.Contains(social.Journal, j => j.Texte.Contains("La disette prend fin"));
    }

    [Fact]
    public void Relations_HostilesAlliéesOuRivales_SelonLesLiensEtLaPenurie()
    {
        var e = Carte.Emplacements[0];
        var eco = new Ecosysteme(Carte);
        var (social, tous, communautes) = Groupe(e.X, e.Y);
        Groupe(e.X + 5, e.Y, premier: 7, deja: tous, social: social);
        social.Communautes = communautes;
        var maintenant = 10 * An;
        social.Regrouper(tous, communautes, maintenant, new HashSet<string>());
        Assert.Equal(2, communautes.Count(c => c.Active));

        void LiensCroises(double affection)
        {
            foreach (var a in tous.Take(6))
            {
                foreach (var b in tous.Skip(6))
                {
                    a.Liens.RemoveAll(l => l.Id == b.Id);
                    b.Liens.RemoveAll(l => l.Id == a.Id);
                    a.Liens.Add(new Lien(b.Id, 30, affection, maintenant));
                    b.Liens.Add(new Lien(a.Id, 30, affection, maintenant));
                }
            }
        }

        LiensCroises(-40);
        social.Territoires(tous, communautes, eco, Carte, maintenant);
        Assert.Equal(EtatRelation.Hostiles, social.RelationEntre(1, 2));
        Assert.Contains(social.Journal, j => j.Texte.EndsWith("sont désormais hostiles l'une à l'autre."));

        LiensCroises(30);
        social.Regrouper(tous, communautes, maintenant + 7 * Jour, new HashSet<string>());
        social.Territoires(tous, communautes, eco, Carte, maintenant + 7 * Jour);
        Assert.Equal(EtatRelation.Alliees, social.RelationEntre(1, 2));

        // Sans sentiments marqués, mais sur les mêmes terres épuisées : elles se les disputent.
        LiensCroises(0);
        Epuiser(eco, e.X, e.Y, 14);
        social.Regrouper(tous, communautes, maintenant + 14 * Jour, new HashSet<string>());
        social.Territoires(tous, communautes, eco, Carte, maintenant + 14 * Jour);
        Assert.Equal(EtatRelation.Rivales, social.RelationEntre(2, 1));
        Assert.Contains(social.Journal, j => j.Texte.Contains("se disputent les mêmes terres"));
        Assert.Single(social.Relations);
    }

    [Fact]
    public void Scission_UnGroupeQuiQuitteSaCommunauteEstJournaliseCommeTel()
    {
        var e = Carte.Emplacements[0];
        var social = new VieSociale(new Alea(4));
        var tous = Enumerable.Range(1, 12).Select(i => Humain(i, e.X + i % 2, e.Y)).ToList();
        Lier(tous);
        var communautes = new List<Communaute>();
        social.Regrouper(tous, communautes, 10 * An, new HashSet<string>());
        Assert.Single(communautes);

        // Cinq membres partent vivre loin et ne gardent de liens qu'entre eux.
        var partants = tous.Skip(7).ToList();
        foreach (var h in partants) h.X += 20;
        foreach (var h in tous) h.Liens.RemoveAll(l => partants.Any(p => p.Id == l.Id) != partants.Contains(h));
        social.Regrouper(tous, communautes, 10 * An + 7 * Jour, new HashSet<string>());

        Assert.Equal(2, communautes.Count(c => c.Active));
        Assert.Contains(social.Journal, j => j.Texte.StartsWith("5 humains quittent la communauté de"));
    }

    // ───────────── Sauvegarde et serveur ─────────────

    [Fact]
    public void Fichier_Version7_PasseEnVersion8_AvecDesReservesMoyennes()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-v8-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fichier = Path.Combine(dossier, "monde.db");
            var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
            new DepotMonde(fichier).Enregistrer(etat);
            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion7);

            var relu = new DepotMonde(fichier).Charger()!;

            Assert.All(relu.Humains!, h => Assert.Equal(50, h.Reserves));
            Assert.All(relu.Humains!, h => Assert.Equal(1, h.Biologie.Metabolisme));
            Assert.Null(relu.Pluies);
            var monde = new MondeSimule(Carte, relu);
            monde.AvancerDe(Jour);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Hub_Nature_DonneLaCarteDesRessourcesEtLesIndicateurs()
    {
        using var usine = new UsineTest();
        var connexion = usine.CreerConnexion();
        await using var _ = connexion;
        await connexion.StartAsync();

        var nature = await connexion.InvokeAsync<NatureDto>("Nature");

        Assert.Equal(nature.Largeur * nature.Hauteur, nature.Abondance.Length);
        Assert.Equal(nature.RegionsX * nature.RegionsY, nature.Pluies.Count);
        Assert.Equal(66, nature.Indicateurs.Vivants);
        Assert.InRange(nature.Indicateurs.Abondance, 0.9, 1.01);

        var communautes = await connexion.InvokeAsync<List<CommunauteDto>>("Communautes");
        Assert.Empty(communautes);
    }
}
