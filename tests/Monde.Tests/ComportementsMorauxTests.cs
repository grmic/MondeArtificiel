using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>Ce que les nouveaux traits changent aux gestes : vol, soins, consolation, peur partagée, deuil, jalousie, trahison.</summary>
public sealed class ComportementsMorauxTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static TraitsMoraux Moral(double compassion = 0.5, double ee = 0.5, double ec = 0.5, double honnetete = 0.5,
        double psychopathie = 0.5, double anxiete = 0.5, double evitement = 0.5, double loyaute = 0.5) =>
        new(honnetete, ee, ec, compassion, psychopathie, anxiete, evitement, FondementsMoraux.Moyens with { Loyaute = loyaute });

    private static Humain Humain(int id, TraitsMoraux? moral = null, Sexe sexe = Sexe.Femme, int x = 10, int y = 10, double age = 25,
        double nevrosisme = 0.5, double agreabilite = 0.5) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.5, 0.5, 0.5, agreabilite, nevrosisme, 0.5, 0.9, 0.5, Intelligence.Moyenne) { Moral = moral ?? TraitsMoraux.Moyens },
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
    };

    private static void Lier(Humain a, Humain b, double affection)
    {
        a.Liens.Add(new Lien(b.Id, 60, affection, Maintenant, Amis: affection >= 40, Rivaux: affection <= -30));
        b.Liens.Add(new Lien(a.Id, 60, affection, Maintenant, Amis: affection >= 40, Rivaux: affection <= -30));
    }

    /// <summary>Un psychopathe affamé (au-dessus du seuil, adulte) à côté d'un inconnu rassasié.</summary>
    private static Humain Psychopathe(int id, int x, int y) =>
        Humain(id, Moral(compassion: 0.2, ee: 0.2, honnetete: 0.15, psychopathie: 0.5 + 0.15 * 2.6), Sexe.Homme, x, y, agreabilite: 0.3);

    [Fact]
    public void Vol_LePeuHonneteAffameVoleUnInconnu_LeVoleurHonneteNeVolePas()
    {
        var e = Carte.Emplacements[0];
        int Vols(TraitsMoraux moral, int graine)
        {
            var social = new VieSociale(new Alea((ulong)graine));
            var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea((ulong)graine)) { Social = social };
            var voleur = Humain(1, moral, Sexe.Homme, e.X, e.Y);
            var victime = Humain(2, x: e.X, y: e.Y);
            var tous = new List<Humain> { voleur, victime };
            for (var pas = 0; pas < 3000; pas++)
            {
                (voleur.X, voleur.Y, victime.X, victime.Y) = (e.X, e.Y, e.X, e.Y);
                voleur.Faim = 95;
                voleur.Soif = voleur.Fatigue = 0;
                victime.Faim = victime.Soif = victime.Fatigue = 0;
                voleur.Sante = victime.Sante = 100;
                voleur.Memoire.Jardin.RemoveAll(j => j.Cle == "vol");
                c.Vivre(voleur, tous, Maintenant + pas * MondeSimule.SecondesParPas);
            }
            return c.Journal.Count(j => j.Type == TypeEvenement.Vol);
        }
        Assert.True(Vols(Moral(honnetete: 0.15), 3) > 0);
        Assert.Equal(0, Vols(Moral(honnetete: 0.7), 3));
    }

    [Fact]
    public void Vol_PrisSurLeFait_LaVictimeEnVeutAuVoleur_EtLePsychopatheDemasqueEstConnu()
    {
        var e = Carte.Emplacements[0];
        var social = new VieSociale(new Alea(4));
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(4)) { Social = social };
        var voleur = Psychopathe(1, e.X, e.Y);
        // Une victime très perspicace, éveillée : elle finira par le surprendre.
        var victime = Humain(2, Moral(ec: 0.95), x: e.X, y: e.Y);
        var tous = new List<Humain> { voleur, victime };
        Assert.True(Psyche.EstPsychopathe(voleur, Maintenant));
        for (var pas = 0; pas < 20_000 && !victime.Griefs.Any(g => g.Motif == MotifGrief.Vol); pas++)
        {
            (voleur.X, voleur.Y, victime.X, victime.Y) = (e.X, e.Y, e.X, e.Y);
            voleur.Faim = 95;
            voleur.Soif = voleur.Fatigue = victime.Faim = victime.Soif = victime.Fatigue = 0;
            voleur.Sante = victime.Sante = 100;
            victime.Activite = Activite.Repos;
            voleur.Memoire.Jardin.RemoveAll(j => j.Cle == "vol");
            c.Vivre(voleur, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        }
        var grief = Assert.Single(victime.Griefs, g => g.Motif == MotifGrief.Vol);
        var vol = Assert.Single(c.Journal, j => j.Id == grief.EvenementId);
        Assert.Equal(TypeEvenement.Vol, vol.Type);
        Assert.Contains("surprend", vol.Texte);
        Assert.Contains(victime.Memoire.Jardin, j => j.Cle == "opinion:1" && j.Texte.Contains("sans honte"));
    }

    [Fact]
    public void Soins_LeCompatissantVeilleSurUnMaladeQuIlConnaitPeu_LeFroidNon()
    {
        int Soins(TraitsMoraux moral)
        {
            var social = new VieSociale(new Alea(2));
            var malade = Humain(1, x: 20, y: 20);
            var voisin = Humain(2, moral, x: 20, y: 20);
            Lier(malade, voisin, 5);
            var tous = new List<Humain> { malade, voisin };
            var n = 0;
            for (var j = 0; j < 30; j++)
            {
                malade.MaladeJusqua = Maintenant + 100 * Jour;
                (malade.Faim, malade.Soif) = (60, 70);
                // Le soignant mange et se repose entre deux visites.
                (voisin.Faim, voisin.Fatigue) = (20, 20);
                social.Coeur(tous, Maintenant + j * Jour);
                if (malade.Soif < 70) n++;
            }
            Assert.All(social.Journal.Where(e => e.Type == TypeEvenement.Entraide), e => Assert.Contains("veille sur H1", e.Texte));
            return n;
        }
        Assert.True(Soins(Moral(compassion: 0.9)) >= 15);
        Assert.True(Soins(Moral(compassion: 0.2, psychopathie: 0.95)) == 0);
    }

    [Fact]
    public void Peur_GagneLeVoisinEmpathique_EtCiteLaCatastrophe()
    {
        var social = new VieSociale(new Alea(1));
        var terrifie = Humain(1, x: 30, y: 30);
        var empathique = Humain(2, Moral(ee: 0.9), x: 31, y: 30, nevrosisme: 0.7);
        var froid = Humain(3, Moral(ee: 0.1, psychopathie: 0.9), x: 30, y: 31, nevrosisme: 0.2);
        terrifie.Peur = 90;
        terrifie.PeurEvenementId = 555;
        social.Coeur([terrifie, empathique, froid], Maintenant);
        Assert.True(empathique.Peur >= 30);
        Assert.True(froid.Peur < empathique.Peur / 3);
        var e = Assert.Single(social.Journal, x => x.HumainId == 2);
        Assert.Equal(555, e.CauseId);
    }

    [Fact]
    public void Deuil_LePsychopatheNePleurePresquePas_LAnxieuxPlusQueLEvitant()
    {
        double Chagrin(TraitsMoraux moral)
        {
            var social = new VieSociale(new Alea(1));
            var mort = Humain(1);
            var proche = Humain(2, moral, Sexe.Homme);
            mort.PartenaireId = 2;
            proche.PartenaireId = 1;
            mort.MortLe = Maintenant;
            social.Endeuiller(mort, [mort, proche], Maintenant);
            return proche.Chagrin;
        }
        var secure = Chagrin(Moral(anxiete: 0.5, evitement: 0.5));
        Assert.True(Chagrin(Moral(anxiete: 0.85)) > secure);
        Assert.True(Chagrin(Moral(evitement: 0.85)) < secure);
        Assert.True(Chagrin(Moral(ee: 0.2, psychopathie: 0.95)) < secure / 2);
    }

    [Fact]
    public void Deuil_UnePerteLourdeLaisseUneTraceDurable()
    {
        var social = new VieSociale(new Alea(3));
        var enfant = Humain(1, age: 6);
        var mere = Humain(2, Moral(ee: 0.8, anxiete: 0.6), nevrosisme: 0.7);
        var avant = mere.Personnalite.Nevrosisme;
        var mereEnfant = new Humain
        {
            Id = 1, Nom = "H1", Sexe = Sexe.Femme, PartenaireId = 0, Emplacement = 1, MereId = 2, Naissance = enfant.Naissance,
            Personnalite = enfant.Personnalite, Biologie = enfant.Biologie, X = 10, Y = 10, MortLe = Maintenant, MortEvenementId = 42,
        };
        social.Endeuiller(mereEnfant, [mereEnfant, mere], Maintenant);
        Assert.True(mere.Personnalite.Nevrosisme > avant);
        Assert.Contains(mere.Memoire.Jardin, j => j.Cle == "epreuve:Deuil" && j.CauseId == 42);
    }

    [Fact]
    public void Jalousie_PlusForteChezLAnxieuxDansSesLiens()
    {
        var p = new Personnalite(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne);
        Assert.True(VieSociale.Jalousie(p with { Moral = Moral(anxiete: 0.85) }) > VieSociale.Jalousie(p) + 0.15);
    }

    [Fact]
    public void Orphelin_LeCompatissantDeSaCommunauteLeRecueille_EtCEstNote()
    {
        var social = new VieSociale(new Alea(1));
        var mere = Humain(1);
        mere.MortLe = Maintenant - Jour;
        mere.MortEvenementId = 9;
        var orphelin = new Humain
        {
            Id = 2, Nom = "H2", Sexe = Sexe.Homme, PartenaireId = 0, Emplacement = 1, MereId = 1, Naissance = Maintenant - 5 * An,
            Personnalite = mere.Personnalite, Biologie = Biologie.ParDefaut(Sexe.Homme), X = 10, Y = 10, CommunauteId = 1,
        };
        var froid = Humain(3, Moral(compassion: 0.2, psychopathie: 0.95));
        var bon = Humain(4, Moral(compassion: 0.9));
        froid.CommunauteId = bon.CommunauteId = 1;
        Lier(orphelin, froid, 30);
        Lier(orphelin, bon, 15);
        var tous = new List<Humain> { mere, orphelin, froid, bon };
        Assert.Equal(4, Comportement.Gardien(orphelin, tous, Maintenant)!.Id);
        social.Coeur(tous, Maintenant);
        var e = Assert.Single(social.Journal, x => x.Type == TypeEvenement.Entraide);
        Assert.Equal((4, 2, 9L), (e.HumainId, e.AutreId, e.CauseId));
        Assert.Contains("orphelin", e.Texte);
    }

    [Fact]
    public void Psychopathe_GardeSonMasqueAuJardinSecret_DansUnMondeEnMarche()
    {
        var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
        var h = etat.Humains![0];
        h.Personnalite = h.Personnalite with { Moral = h.Personnalite.Moral with { Psychopathie = 0.95 } };
        var monde = new MondeSimule(Carte, etat);
        monde.AvancerDe(Jour + 600);
        var (_, humains) = monde.InstantaneHumains();
        Assert.Contains(humains[0].Memoire.Jardin, j => j.Cle == "masque");
        Assert.DoesNotContain(humains.Skip(1).Where(x => !Psyche.EstPsychopathe(x, monde.Secondes)), x => x.Memoire.Jardin.Any(j => j.Cle == "masque"));
    }
}
