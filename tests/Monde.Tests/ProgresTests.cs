using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.29.1 : copier le plus habile, enseigner exprès, la cause des savoirs perdus et la courbe des savoirs vivants.</summary>
public sealed class ProgresTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, double age, int x, int y, int mere = 0, Sexe sexe = Sexe.Femme) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        MereId = mere,
        Emplacement = 1,
        Naissance = Maintenant - (long)(age * An),
        Personnalite = new Personnalite(0.7, 0.8, 0.5, 0.8, 0.4, 0.5, 0.5, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
        Humeur = 80,
    };

    private static Savoir Eclat(int succes) =>
        new(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, succes + 1, succes, 0, Maintenant, OrigineSavoir.Essai, 0, 0);

    [Fact]
    public void Enseigner_UneMereMontreSonGesteASonEnfant_QuiLApprendDElle_EtLEntiteLeVoit()
    {
        var e = Carte.Emplacements[0];
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(3)) { Social = new VieSociale(new Alea(3)) };
        var mere = Humain(1, 30, e.X, e.Y);
        mere.Savoirs.Add(Eclat(20));
        mere.Objets.Add(new Objet(Matiere.Silex, 1, 1));
        mere.Objets.Add(new Objet(Matiere.Galet, 1, 1));
        var enfant = Humain(2, 9, e.X, e.Y, mere: 1);
        mere.Liens.Add(new Lien(2, 90, 90, Maintenant));
        enfant.Liens.Add(new Lien(1, 90, 90, Maintenant));
        var tous = new List<Humain> { mere, enfant };
        var fois = 0;
        for (var pas = 0; pas < 200_000 && !enfant.Savoirs.Exists(s => s.Effet == Effet.Eclat); pas++)
        {
            if (c.Enseigner(mere, tous, Maintenant + pas * MondeSimule.SecondesParPas)) fois++;
        }
        var appris = enfant.Savoirs.Find(s => s.Effet == Effet.Eclat);
        Assert.NotNull(appris);
        Assert.InRange(fois, 1, 20);
        Assert.Equal((OrigineSavoir.Imitation, 1), (appris.Origine, appris.MaitreId));
        Assert.Equal(Activite.Enseigner, mere.Activite);
        var lecon = Assert.Single(c.Journal, j => j.Texte.Contains("H1 guide la main de H2"));
        Assert.Equal((2, 1, Regard.Enseigner), (lecon.HumainId, lecon.AutreId, lecon.KarmaAutre));
        Assert.Equal(Regard.Enseigner, mere.Karma);
    }

    [Fact]
    public void Enseigner_UnPetitNApprendPasATailler_EtOnNeMontrePasCeQuOnNAPasSousLaMain()
    {
        var e = Carte.Emplacements[0];
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(4)) { Social = new VieSociale(new Alea(4)) };
        var mere = Humain(1, 30, e.X, e.Y);
        // Une pointe emmanchée qu'elle ne porte pas : rien à montrer.
        var lance = new Assemblage(Matiere.Branche, 1, Matiere.Eclat, 1, Matiere.Corde, Montage.Bout).Signature;
        mere.Savoirs.Add(new Savoir(Geste.Assembler, Matiere.Branche, Matiere.Eclat, Effet.PointeEmmanchee, 0, Symptome.Aucun, 10, 9, 0, Maintenant,
            OrigineSavoir.Essai, 0, 0, lance));
        var petit = Humain(2, 2.5, e.X, e.Y, mere: 1);
        var grand = Humain(3, 10, e.X, e.Y, mere: 1);
        var tous = new List<Humain> { mere, petit, grand };
        for (var pas = 0; pas < 50_000; pas++) Assert.False(c.Enseigner(mere, tous, Maintenant + pas * MondeSimule.SecondesParPas));
        // Avec la lance en main, elle la montre au grand, jamais au petit.
        mere.Objets.Add(new Objet(Matiere.Assemblage, 0.8, 1, Pieces: lance));
        for (var pas = 0; pas < 200_000 && !grand.Savoirs.Exists(s => s.Effet == Effet.PointeEmmanchee); pas++)
            c.Enseigner(mere, tous, Maintenant + pas * MondeSimule.SecondesParPas);
        Assert.Contains(grand.Savoirs, s => s.Effet == Effet.PointeEmmanchee && s.MaitreId == 1);
        Assert.DoesNotContain(petit.Savoirs, s => s.Effet == Effet.PointeEmmanchee);
    }

    [Fact]
    public void Heritage_LaCopieEstLePlusSouventMoinsBonne_ParfoisMeilleure_EtMieuxQuandOnEnseigne()
    {
        var c = new Comportement(Carte, new Ecosysteme(Carte), new Alea(7));
        var eleve = Humain(2, 12, 0, 0);
        var modele = Eclat(21);
        var vus = Enumerable.Range(0, 4000).Select(_ => c.Heritage(eleve, modele, Effet.Eclat, "", false)).ToList();
        var montres = Enumerable.Range(0, 4000).Select(_ => c.Heritage(eleve, modele, Effet.Eclat, "", true)).ToList();
        Assert.True(montres.Average() > vus.Average() + 3, $"{montres.Average()} contre {vus.Average()}");
        Assert.True(vus.Count(v => v < 20) > 0.9 * vus.Count);
        Assert.Contains(montres, v => v >= 20);
        Assert.True(montres.Count(v => v >= 20) < 0.25 * montres.Count);
        // Un geste difficile se copie moins bien ; d'un débutant, on ne tient rien.
        Assert.True(Enumerable.Range(0, 4000).Average(_ => c.Heritage(eleve, modele, Effet.Flamme, "", false)) < vus.Average());
        Assert.Equal(0, c.Heritage(eleve, Eclat(1), Effet.Eclat, "", true));
        Assert.Equal(0, c.Heritage(eleve, null, Effet.Eclat, "", true));
    }

    [Fact]
    public void Perte_LeJournalDitPourquoi_EtLaCourbeMesureLesSavoirsVivants()
    {
        var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
        var h = etat.Humains![0];
        // Il ne s'en est pas servi depuis dix ans : il l'oubliera au premier jour.
        h.Savoirs.Add(new Savoir(Geste.Tordre, Matiere.Fibres, Matiere.Aucune, Effet.Corde, 0, Symptome.Aucun, 1, 1, -10 * An, -10 * An,
            OrigineSavoir.Essai, 0, 0));
        h.Savoirs.Add(Eclat(30) with { UtiliseLe = 0 });
        var monde = new MondeSimule(Carte, etat);
        monde.AvancerDe(2 * Calendrier.SecondesParJour);
        var journal = monde.Etat.JournalNonEnregistre!;
        var perte = Assert.Single(journal, j => j.Texte.StartsWith("Plus personne ne sait", StringComparison.Ordinal));
        Assert.Contains($"{h.Nom}, {(h.Sexe == Sexe.Femme ? "la dernière" : "le dernier")} qui savait, l'a oublié faute de s'en servir.", perte.Texte);
        var oubli = Assert.Single(journal, j => j.Id == perte.CauseId);
        Assert.Equal(TypeEvenement.Souvenir, oubli.Type);
        // La mesure de l'an 1 compte l'éclat, plus ce que d'autres ont pu trouver le premier jour, mais plus la corde.
        var mesure = Assert.Single(monde.Progres(), m => m.CommunauteId == 0);
        Assert.Equal(1, mesure.Annee);
        var vivants = monde.Etat.Humains!.Where(x => x.EstVivant).SelectMany(x => x.Savoirs).Where(s => s.Espece == 0 && s.Effet != Effet.Rien)
            .Select(s => s.Effet).Distinct().Count();
        Assert.InRange(mesure.Savoirs, 1, vivants);
        Assert.Equal(vivants, monde.SavoirsVivantsMaintenant(0));
    }

    [Fact]
    public void Courbe_SEnregistre_EtSeReconstitueDepuisLeJournalPourUnMondePlusAncien()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-progres-" + Guid.NewGuid().ToString("N"));
        try
        {
            var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat with
            {
                Progres = [new MesureSavoirs(4, 0, 3), new MesureSavoirs(4, 2, 2), new MesureSavoirs(5, 0, 2)],
                Decouvertes = [new Decouverte(Effet.Eclat, 1, An / 2, 10, OrigineSavoir.Essai), new Decouverte(Effet.Corde, 1, An + 5, 11, OrigineSavoir.Essai),
                    new Decouverte(Effet.Poison, 1, An + 6, 12, OrigineSavoir.Essai), new Decouverte(Effet.Abri, 2, 2 * An + 7, 13, OrigineSavoir.Essai)],
                JournalNonEnregistre =
                [
                    new EvenementJournal(2 * An + 100, TypeEvenement.Decouverte, 0, 0, "Plus personne ne sait : « x » Ce savoir est perdu.") { Id = 20, Significatif = true },
                    new EvenementJournal(2 * An + 200, TypeEvenement.Decouverte, 3, 0, "Savoir retrouvé (plus personne ne le savait) : y") { Id = 21, Significatif = true },
                    new EvenementJournal(2 * An + 300, TypeEvenement.Decouverte, 0, 0, "Plus personne ne sait : « z » Ce savoir est perdu.") { Id = 22, Significatif = true },
                ],
            };
            var fichier = Path.Combine(dossier, "monde.db");
            var depot = new DepotMonde(fichier);
            depot.Enregistrer(etat);
            Assert.Equal(etat.Progres, depot.Charger()!.Progres);
            var marques = depot.MarquesProgres();
            Assert.Equal([(20L, true), (21L, false), (22L, true)], marques.Select(m => (m.Evenement, m.Perdu)));
            // An 1 : rien ; an 2 : l'éclat ; an 3 : éclat et corde (le poison des plantes ne compte pas) ; an 4 : + abri, −1, +1, −1.
            Assert.Equal([0, 1, 2, 2], depot.ReconstituerProgres(5, marques).Select(m => m.Savoirs));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
