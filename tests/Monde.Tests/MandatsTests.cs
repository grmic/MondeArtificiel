using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>
/// Paroles de l'Entité à une communauté entière : chacun la reçoit, le groupe en débat (une figure très respectée ou crainte
/// tranche seule), puis il obéit, à moitié, refuse, ou ses fidèles agissent seuls ; chaque parole et ses effets.
/// </summary>
public sealed class MandatsTests
{
    private static readonly Carte Carte = MondeSimuleTests.Carte42;
    private const long Jour = Calendrier.SecondesParJour;
    private const long An = Calendrier.SecondesParAn;
    private const long Maintenant = 10 * An;

    private static Humain Humain(int id, int x, int y, Sexe sexe, double agreabilite) => new()
    {
        Id = id,
        Nom = "H" + id,
        Sexe = sexe,
        PartenaireId = 0,
        Emplacement = 1,
        Naissance = Maintenant - 25 * An,
        Personnalite = new Personnalite(0.5, 0.5, 0.5, agreabilite, 0.5, 0.5, 0.9, 0.5, Intelligence.Moyenne),
        Biologie = Biologie.ParDefaut(sexe),
        X = x,
        Y = y,
        Reserves = 60,
    };

    private static void Lier(IEnumerable<Humain> a, IEnumerable<Humain> b, double affection)
    {
        foreach (var x in a)
        {
            foreach (var y in b)
            {
                if (x.Id == y.Id) continue;
                x.Liens.RemoveAll(l => l.Id == y.Id);
                x.Liens.Add(new Lien(y.Id, 60, affection, Maintenant, Amis: affection >= 40));
            }
        }
    }

    private sealed record Scene(VieSociale Social, List<Humain> Tous, List<Communaute> Communautes, Ecosysteme Nature)
    {
        public Communaute A => Communautes[0];
        public Communaute B => Communautes[1];
    }

    /// <summary>
    /// Deux groupes voisins : A (ids 1 à 8, six hommes) et B (ids 9 à 14), à <paramref name="ecart"/> cases l'un de l'autre,
    /// liés par <paramref name="entre"/> d'affection (négative : ils se haïssent).
    /// </summary>
    private static Scene DeuxGroupes(double entre, int graine = 5, int ecart = 10)
    {
        var e = Carte.Emplacements[0];
        var a = Enumerable.Range(1, 8).Select(i => Humain(i, e.X + i % 2, e.Y, i <= 6 ? Sexe.Homme : Sexe.Femme, entre < 0 ? 0.2 : 0.7)).ToList();
        var b = Enumerable.Range(9, 6).Select(i => Humain(i, e.X + ecart + i % 2, e.Y, i == 9 ? Sexe.Homme : Sexe.Femme, 0.8)).ToList();
        Lier(a, a, 60);
        Lier(b, b, 60);
        Lier(a, b, entre);
        Lier(b, a, entre);
        if (entre < 0)
            foreach (var h in a) h.Griefs.Add(new Grief(9, MotifGrief.Meurtre, 100, 100, 5));
        var tous = a.Concat(b).ToList();
        var social = new VieSociale(new Alea((ulong)graine)) { Carte = Carte };
        var communautes = new List<Communaute>();
        social.Communautes = communautes;
        social.Regrouper(tous, communautes, Maintenant, new HashSet<string>());
        Assert.Equal(2, communautes.Count(c => c.Active));
        var scene = new Scene(social, tous, communautes, new Ecosysteme(Carte));
        // Une semaine pour que les relations entre les groupes se forment.
        social.Territoires(tous, communautes, scene.Nature, Carte, Maintenant);
        return scene;
    }

    /// <summary>
    /// L'Entité parle au groupe A : chaque adulte a reçu la parole et en pense <paramref name="reponse"/> (la figure,
    /// <paramref name="figure"/> si donné) ; puis la parole parvient au groupe.
    /// </summary>
    private static Mandat Parler(Scene s, Parole parole, Reponse reponse, Reponse? figure = null, int direction = 0)
    {
        var param = parole == Parole.PartezVers ? direction : s.B.Id;
        foreach (var h in s.Tous.Where(h => h.CommunauteId == s.A.Id))
        {
            var r = h.Id == s.A.FigureId ? figure ?? reponse : reponse;
            h.Impulsions.Add(new Impulsion(parole, ModeParole.Voix, 0, param, 0.9, Maintenant, Maintenant + 2 * An, long.MaxValue, r, 0));
        }
        var cibles = parole == Parole.PartezVers ? [] : new List<Communaute> { s.B };
        return Assert.Single(s.Social.Recevoir(s.A, parole, ModeParole.Voix, cibles, direction, s.Tous, Maintenant));
    }

    /// <summary>Des jours de vie sociale (débats chaque jour, territoires chaque semaine) jusqu'à la condition ; rend la date atteinte.</summary>
    private static long Jours(Scene s, long depuis, Func<bool> fini, int maximum = 200)
    {
        var t = depuis;
        for (var k = 0; k < maximum && !fini(); k++)
        {
            t += Jour;
            s.Social.Debattre(s.Tous, s.Communautes, t);
            if (k % 7 == 6) s.Social.Territoires(s.Tous, s.Communautes, s.Nature, Carte, t);
        }
        return t;
    }

    private static bool Ouvert(Mandat m) => m.Etat is EtatMandat.Debat;

    [Fact]
    public void Recevoir_CompteLesAvis_EtFixeLeJourDuDebat()
    {
        var s = DeuxGroupes(40);
        var m = Parler(s, Parole.AllezALaRencontre, Reponse.Refusee, figure: Reponse.Acceptee);
        Assert.Equal((s.A.Id, s.B.Id, EtatMandat.Debat), (m.CommunauteId, m.CibleId, m.Etat));
        Assert.Equal((1, 7), (m.Veulent, m.Refusent));
        Assert.InRange(m.DebatLe, Maintenant + 1, Maintenant + 60 * Jour);
        Assert.True(m.Jusqua > Maintenant + 30 * Jour);
        var recu = Assert.Single(s.Social.Journal, j => j.Id == m.EvenementId);
        Assert.True(recu.Significatif);
        Assert.Equal("mandat.recu", recu.Recit?.Cle);
        Assert.Equal((s.A.Id, s.B.Id), (recu.CommunauteId, recu.AutreCommunauteId));
    }

    [Fact]
    public void Debat_PresqueTousRefusent_LeGroupeRefuse()
    {
        for (var graine = 1; graine <= 6; graine++)
        {
            var s = DeuxGroupes(40, graine);
            var m = Parler(s, Parole.FaitesLaGuerre, Reponse.Refusee);
            Jours(s, Maintenant, () => !Ouvert(m));
            Assert.Equal(EtatMandat.Refuse, m.Etat);
            Assert.Null(s.A.Raid);
            Assert.Contains(s.Social.Journal, j => j.Id == m.EtatEvenementId && j.Recit?.Cle is "mandat.refuse" or "mandat.refuse.figure" or "mandat.refuse.figureContre");
        }
    }

    [Fact]
    public void Paix_EntreGroupesQuiSAiment_EstImpossible()
    {
        var s = DeuxGroupes(40);
        var m = Parler(s, Parole.FaitesLaPaix, Reponse.Acceptee);
        Jours(s, Maintenant, () => !Ouvert(m));
        Assert.Equal(EtatMandat.Impossible, m.Etat);
        Assert.Equal("mandat.impossible", s.Social.Journal.Single(j => j.Id == m.EtatEvenementId).Recit?.Cle);
    }

    [Fact]
    public void Guerre_LeGroupeObeit_EtLanceUnRaidVoulu()
    {
        var s = DeuxGroupes(-40);
        var m = Parler(s, Parole.FaitesLaGuerre, Reponse.Acceptee);
        Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute));
        Assert.Equal(EtatMandat.Accompli, m.Etat);
        var raid = s.A.Raid!;
        Assert.Equal(s.B.Id, raid.CibleId);
        Assert.Equal(raid.EvenementId, m.EtatEvenementId);
        Assert.Contains("parce que l'Entité l'a voulu", s.Social.Journal.Single(j => j.Id == raid.EvenementId).Texte);
    }

    [Fact]
    public void Exterminer_LaGuerreDure_SansTreveNiPaix()
    {
        var s = DeuxGroupes(-40);
        var m = Parler(s, Parole.Exterminez, Reponse.Acceptee);
        Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute));
        if (m.Moitie)
        {
            // À moitié : on menace sans verser le sang.
            Assert.Contains(s.Social.Journal, j => j.Recit?.Cle == "mandat.guerre.menace");
            return;
        }
        Assert.Equal(EtatMandat.EnCours, m.Etat);
        Assert.True(s.Social.MeneContre(s.A.Id, s.B.Id, Parole.Exterminez));
        Assert.False(s.Social.Treve(s.A.Id, s.B.Id));
        Assert.Contains("exterminer", s.Social.Journal.Single(j => j.Id == s.A.Raid!.EvenementId).Texte);
    }

    [Fact]
    public void Tyran_CraintDesSiens_ImposeSonAvisAuDebat()
    {
        var imposes = 0;
        for (var graine = 1; graine <= 12; graine++)
        {
            var s = DeuxGroupes(-40, graine);
            var figure = s.Tous[s.A.FigureId - 1];
            figure.Personnalite = figure.Personnalite with { Agreabilite = 0, Extraversion = 0.9 };
            // Il a frappé chacun des siens.
            foreach (var h in s.Tous.Where(h => h.CommunauteId == s.A.Id && h.Id != figure.Id))
                h.Griefs.Add(new Grief(figure.Id, MotifGrief.Coups, 100, 100, 1));
            Assert.True(VieSociale.Crainte(s.A, s.Tous, Maintenant) >= 0.5);
            Assert.True(VieSociale.Emprise(s.A, s.Tous, Maintenant) >= 0.25);
            Assert.True(VieSociale.Crainte(s.B, s.Tous, Maintenant) < 0.2);
            // Tous refusent la guerre, sauf lui.
            var m = Parler(s, Parole.FaitesLaGuerre, Reponse.Refusee, figure: Reponse.Acceptee);
            Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute) || m.Impose);
            if (!m.Impose) continue;
            imposes++;
            Assert.Contains(s.Social.Journal, j => j.Recit?.Cle == "mandat.impose.oui");
            Assert.Contains(s.Tous.Where(h => h.CommunauteId == s.A.Id && h.Id != figure.Id), h => h.Memoire.Jardin.Exists(x => x.Texte.Contains(figure.Nom)));
        }
        Assert.InRange(imposes, 1, 12);
    }

    [Fact]
    public void Alliance_SiLAutreAccepte_UnSermentLesTient()
    {
        var acceptees = 0;
        for (var graine = 1; graine <= 8; graine++)
        {
            var s = DeuxGroupes(60, graine);
            var m = Parler(s, Parole.AlliezVous, Reponse.Acceptee);
            Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute));
            Assert.Contains(m.Etat, new[] { EtatMandat.EnCours, EtatMandat.Accompli, EtatMandat.Repousse });
            if (m.Etat != EtatMandat.EnCours || m.Moitie) continue;
            acceptees++;
            Assert.True(s.Social.Serment(s.A.Id, s.B.Id));
            Assert.Equal(EtatRelation.Alliees, s.Social.RelationEntre(s.A.Id, s.B.Id));
            // Le serment tient la semaine suivante, quand les relations se refont.
            Jours(s, s.Social.Journal[^1].Secondes, () => false, maximum: 8);
            Assert.Equal(EtatRelation.Alliees, s.Social.RelationEntre(s.A.Id, s.B.Id));
        }
        Assert.True(acceptees > 0);
    }

    [Fact]
    public void Rencontre_UneDelegationVaChezLAutreGroupe()
    {
        var parties = 0;
        for (var graine = 1; graine <= 8; graine++)
        {
            var s = DeuxGroupes(60, graine);
            var m = Parler(s, Parole.AllezALaRencontre, Reponse.Acceptee);
            Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute));
            if (m.Etat != EtatMandat.EnCours) continue;
            parties++;
            var r = Assert.Single(s.Social.Rassemblements, x => x.Id == m.RassemblementId);
            Assert.Equal(new[] { s.A.Id, s.B.Id }, new[] { r.HoteId, r.InviteeId }.Order());
            // Tout le groupe s'y rend ; à moitié, une délégation seulement.
            if (m.Envoyes.Count > 0)
            {
                Assert.All(m.Envoyes, id => Assert.Equal(r.Id, s.Tous[id - 1].RassemblementId));
                Assert.Contains(s.Social.Journal, j => j.Recit?.Cle == "mandat.delegation.AllezALaRencontre");
            }
        }
        Assert.True(parties > 0);
    }

    [Fact]
    public void Partager_LesReservesPassentALAutreGroupe()
    {
        var faits = 0;
        for (var graine = 1; graine <= 8; graine++)
        {
            var s = DeuxGroupes(60, graine);
            foreach (var h in s.Tous.Where(h => h.CommunauteId == s.B.Id)) h.Reserves = 2;
            s.B.Disette = true;
            var avant = s.Tous.Where(h => h.CommunauteId == s.B.Id).Sum(h => h.Reserves);
            var m = Parler(s, Parole.PartagezAvec, Reponse.Acceptee);
            Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute), maximum: 30);
            if (m.Etat != EtatMandat.Accompli) continue;
            faits++;
            Assert.True(m.Bilan > 0);
            Assert.True(s.Tous.Where(h => h.CommunauteId == s.B.Id).Sum(h => h.Reserves) > avant);
        }
        Assert.True(faits > 0);
    }

    [Fact]
    public void Unir_LePlusPetitGroupeRejointLePlusGrand()
    {
        var unis = 0;
        // Se fondre dans un autre groupe se décide rarement : ici deux groupes très liés, et l'autre a faim.
        for (var graine = 1; graine <= 12; graine++)
        {
            var s = DeuxGroupes(90, graine);
            foreach (var h in s.Tous.Where(h => h.CommunauteId == s.B.Id)) h.Reserves = 2;
            s.B.Disette = true;
            var m = Parler(s, Parole.UnissezVous, Reponse.Acceptee);
            Jours(s, Maintenant, () => m.Etat is not (EtatMandat.Debat or EtatMandat.EnRoute), maximum: 30);
            if (m.Etat != EtatMandat.EnCours) continue;
            unis++;
            Assert.Equal(s.A.Id, s.B.FusionVersId);
            Assert.Equal((s.A.CentreX, s.A.CentreY), (s.B.DestinationX, s.B.DestinationY));
        }
        Assert.True(unis > 0);
    }

    [Fact]
    public void Partir_LeGroupeLeveLeCamp_VersLaDirectionDonnee()
    {
        var s = DeuxGroupes(40);
        var (x0, y0) = (s.A.CentreX, s.A.CentreY);
        Mandat? m = null;
        // Une direction où il y a de la terre.
        foreach (var d in Enumerable.Range(0, 8))
        {
            var (dx, dy) = Paroles.Vecteurs[d];
            if (!Climat.EstPraticable(Carte.BiomeEn(Math.Clamp(x0 + 3 * dx, 0, Carte.Largeur - 1), Math.Clamp(y0 + 3 * dy, 0, Carte.Hauteur - 1)))) continue;
            m = Parler(s, Parole.PartezVers, Reponse.Acceptee, direction: d);
            break;
        }
        Assert.NotNull(m);
        Jours(s, Maintenant, () => !Ouvert(m));
        Assert.Equal(EtatMandat.EnCours, m.Etat);
        Assert.NotEqual(-1, s.A.DestinationX);
        var (dx2, dy2) = Paroles.Vecteurs[m.Direction];
        Assert.True((s.A.DestinationX - x0) * dx2 + (s.A.DestinationY - y0) * dy2 > 0);
    }

    [Fact]
    public void ParLeMonde_ChaqueAdulteRecoit_EtLesParolesSontEnregistrees()
    {
        var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
        for (var k = 0; k < 24 && monde.Communautes().Count(c => c.Active) < 2; k++) monde.AvancerDe(An / 12);
        var actives = monde.Communautes().Where(c => c.Active).ToList();
        Assert.True(actives.Count >= 2);
        var (a, b) = (actives[0], actives[1]);
        var (resultats, mandats) = monde.ParlerACommunaute(a.Id, Parole.FaitesLaPaix, ModeParole.Reve, [b.Id], 0);
        var m = Assert.Single(mandats);
        Assert.Equal((a.Id, b.Id), (m.CommunauteId, m.CibleId));
        Assert.NotEmpty(resultats);
        Assert.Equal(resultats.Count, m.Veulent + m.Hesitent + m.Refusent + m.Incredules + m.Incompris);
        var etat = monde.Etat;
        Assert.All(resultats, r => Assert.Contains(etat.Humains![r.Humain - 1].Impulsions, i => i.Parole == Parole.FaitesLaPaix && i.Param == b.Id));
        // Rien pour une parole faite pour un seul humain, ni sans communauté visée.
        Assert.Empty(monde.ParlerACommunaute(a.Id, Parole.Tue, ModeParole.Reve, [b.Id], 0).Mandats);
        Assert.Empty(monde.ParlerACommunaute(a.Id, Parole.FaitesLaGuerre, ModeParole.Reve, [], 0).Mandats);

        var dossier = Path.Combine(Path.GetTempPath(), "monde-mandats-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, "monde.db");
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            var lu = Assert.Single(relu.Mandats!);
            Assert.Equal((m.Id, m.Parole, m.CibleId, m.DebatLe, m.Veulent, m.EvenementId), (lu.Id, lu.Parole, lu.CibleId, lu.DebatLe, lu.Veulent, lu.EvenementId));
            var repris = new MondeSimule(Carte, relu);
            Assert.Single(repris.Mandats());

            // Un monde de la version 39 n'a pas de paroles aux communautés ; il se relit et passe en version 40.
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fichier};Pooling=False"))
            {
                c.Open();
                using var commande = c.CreateCommand();
                commande.CommandText = "DROP TABLE mandats; PRAGMA user_version = 39;";
                commande.ExecuteNonQuery();
            }
            var v39 = new DepotMonde(fichier).Charger()!;
            Assert.True(v39.Mandats is null or { Count: 0 });
            new DepotMonde(fichier).Enregistrer(v39, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
            Assert.Equal(49, DepotMonde.VersionSchema);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
