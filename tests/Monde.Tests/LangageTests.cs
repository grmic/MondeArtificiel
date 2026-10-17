using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;
using Monde.Stockage;

namespace Monde.Tests;

/// <summary>0.21 : le langage (mots, diffusion, déformation, stades, ordre des mots, noms, interprètes, sauvegarde).</summary>
public sealed class LangageTests
{
    private const long An = Calendrier.SecondesParAn;
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * An;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    private static Humain Humain(int id, double age = 25, int communaute = 1, double verbale = 100, int mere = 0)
    {
        var (x, y) = (Carte.Emplacements[0].X, Carte.Emplacements[0].Y);
        return new Humain
        {
            Id = id, Nom = "H" + id, Sexe = id % 2 == 0 ? Sexe.Homme : Sexe.Femme, PartenaireId = 0, Emplacement = 1, MereId = mere,
            Naissance = Maintenant - (long)(age * An),
            Personnalite = new Personnalite(0.6, 0.5, 0.6, 0.5, 0.5, 0.5, 0.5, 0.5, Intelligence.Moyenne with { Verbale = verbale }),
            Biologie = Biologie.ParDefaut(id % 2 == 0 ? Sexe.Homme : Sexe.Femme), Reserves = 50, Sante = 100, CommunauteId = communaute,
            X = x, Y = y, Humeur = 50,
        };
    }

    private static Communaute Communaute(int id = 1) =>
        new() { Id = id, Nom = $"Communauté de H{id}", FondeeLe = 0, FigureId = 1, CentreX = Carte.Emplacements[0].X, CentreY = Carte.Emplacements[0].Y, Rayon = 6 };

    private static Mot Mot(string idee, string forme, int auteur = 0, int langue = 0) => new(idee, forme, false, 0.5, Maintenant, auteur, langue);

    [Fact]
    public void JeuDeDenomination_DeuxMotsPourUneChose_LeGroupeFinitParNenGarderUn()
    {
        var social = new VieSociale(new Alea(5));
        var c = Communaute();
        var communautes = new List<Communaute> { c };
        social.Communautes = communautes;
        var tous = Enumerable.Range(1, 10).Select(i => Humain(i)).ToList();
        tous[0].Vocabulaire.Add(Mot("eau", "kalu", 1));
        tous[1].Vocabulaire.Add(Mot("eau", "tepi", 2));
        foreach (var h in tous.Take(2)) h.StadeParole = 1;
        var alea = new Alea(9);
        for (var i = 0; i < 6000; i++)
        {
            var s = tous[alea.Entier(tous.Count)];
            var l = tous[alea.Entier(tous.Count)];
            if (s.Id == l.Id || Langues.MotDe(s, "eau") is null) continue;
            social.Echanger(s, l, "eau", c, communautes, tous, Maintenant + i * 600);
        }
        var formes = tous.Select(h => Langues.MotDe(h, "eau")?.Forme).ToList();
        Assert.All(formes, f => Assert.NotNull(f));
        // Un seul mot l'emporte (à une déformation près, les enfants n'étant pas de la partie ici).
        Assert.Single(formes.Distinct());
        Assert.All(tous, h => Assert.True(h.StadeParole >= 1));
    }

    [Fact]
    public void Enfants_DeformentParfoisLeMotQuIlsApprennent()
    {
        var social = new VieSociale(new Alea(6));
        var c = Communaute();
        var communautes = new List<Communaute> { c };
        social.Communautes = communautes;
        var maitre = Humain(1, verbale: 145);
        maitre.Vocabulaire.Add(Mot("pierre", "dakuno", 1));
        maitre.StadeParole = 1;
        int appris = 0, deformes = 0;
        for (var i = 0; i < 4000; i++)
        {
            var enfant = Humain(100 + i, age: 5, verbale: 145);
            social.Echanger(maitre, enfant, "pierre", c, communautes, [maitre, enfant], Maintenant);
            if (Langues.MotDe(enfant, "pierre") is not { } m) continue;
            appris++;
            if (m.Forme != "dakuno")
            {
                deformes++;
                Assert.Equal(enfant.Id, m.AuteurId);
                Assert.True(Langues.Parentes("dakuno", m.Forme, false), m.Forme);
            }
        }
        Assert.True(appris > 1000, $"{appris}");
        var part = deformes / (double)appris;
        Assert.InRange(part, 0.01, 0.06);
    }

    [Fact]
    public void Langue_ConstateLesMotsDuGroupe_SonStade_EtSonOrdre()
    {
        var social = new VieSociale(new Alea(7));
        var c = Communaute();
        var communautes = new List<Communaute> { c };
        social.Communautes = communautes;
        var tous = Enumerable.Range(1, 8).Select(i => Humain(i, age: i <= 3 ? 9 : 30)).ToList();
        var idees = new[] { "eau", "feu", "pierre", "arbre", "soleil", "lune", "pluie", "vent", "oiseau", "poisson", "nuit", "jour" };
        foreach (var h in tous)
        {
            foreach (var (idee, k) in idees.Select((x, k) => (x, k))) h.Vocabulaire.Add(Mot(idee, "ka" + new string('l', 1 + k % 3) + (char)('a' + k), auteur: 1));
            h.StadeParole = 2;
        }
        foreach (var h in tous.Take(3)) (h.StadeParole, h.Ordre) = (3, OrdreMots.SOV);
        social.ConstaterLangues(tous, communautes, Maintenant);
        Assert.Equal(idees.Length, c.Lexique.Count);
        Assert.Equal(3, c.Stade);
        Assert.Equal(OrdreMots.SOV, c.Ordre);
        Assert.True(c.ParoleLe > 0 && c.CombinaisonLe > 0 && c.OrdreLe > 0);
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Langage && e.Texte.Contains("ordre des mots", StringComparison.Ordinal));
        Assert.Contains(social.Journal, e => e.Type == TypeEvenement.Langage && e.Texte.Contains("10 mots", StringComparison.Ordinal));
        Assert.Equal(idees.Length, c.HistoireLangue.Count(x => x.Type == ChangementMot.Adopte));
        // Un palier n'est noté qu'une fois, même si la langue perd puis regagne des mots.
        var avant = social.Journal.Count(e => e.Texte.Contains("10 mots", StringComparison.Ordinal));
        c.Lexique.Remove("eau");
        social.ConstaterLangues(tous, communautes, Maintenant + 7 * Jour);
        Assert.Equal(avant, social.Journal.Count(e => e.Texte.Contains("10 mots", StringComparison.Ordinal)));
    }

    [Fact]
    public void Ordre_UnAdulteLePrendRarement_UnEnfantNéDansLesPhrasesSouvent()
    {
        var social = new VieSociale(new Alea(8));
        var c = Communaute();
        var communautes = new List<Communaute> { c };
        social.Communautes = communautes;
        var locuteur = Humain(1, age: 12);
        locuteur.Vocabulaire.Add(Mot("eau", "kalu"));
        (locuteur.StadeParole, locuteur.Ordre) = (3, OrdreMots.SVO);
        int Prennent(double age, bool natif)
        {
            var n = 0;
            for (var i = 0; i < 2000; i++)
            {
                var l = Humain(10 + i, age: age);
                l.Vocabulaire.Add(Mot("eau", "kalu"));
                (l.StadeParole, l.ParoleNative) = (2, natif);
                social.Echanger(locuteur, l, "eau", c, communautes, [locuteur, l], Maintenant);
                if (l.StadeParole == 3) { n++; Assert.Equal(OrdreMots.SVO, l.Ordre); }
            }
            return n;
        }
        var adultes = Prennent(30, false);
        var natifs = Prennent(6, true);
        Assert.True(natifs > 10 * Math.Max(1, adultes), $"{natifs} enfants contre {adultes} adultes");
    }

    [Fact]
    public void Nom_LeBebeRecoitUnNomDeSaMere_QuiVeutDireQuelqueChose()
    {
        var social = new VieSociale(new Alea(9));
        var c = Communaute();
        c.Stade = 1;
        var communautes = new List<Communaute> { c };
        social.Communautes = communautes;
        foreach (var (idee, k) in new[] { "soleil", "lune", "etoiles", "pluie", "vent", "riviere", "pierre", "arbre", "oiseau", "poisson", "herbe",
                     "feu", "joie", "gibier", "nuit", "eau" }.Select((x, k) => (x, k)))
            c.Lexique[idee] = "ta" + (char)('a' + k) + "lo";
        var mere = Humain(1, age: 24);
        mere.StadeParole = 1;
        var bebe = Humain(2, age: 0.2, mere: 1);
        var tous = new List<Humain> { mere, bebe };
        for (var j = 0; j < 400 && bebe.NomLangue is null; j++) social.Parler(tous, communautes, Carte, Maintenant + j * Jour);
        Assert.NotNull(bebe.NomLangue);
        Assert.True(char.IsUpper(bebe.NomLangue![0]));
        Assert.NotNull(bebe.NomSens);
        Assert.Equal(1, bebe.NomParId);
        var e = Assert.Single(social.Journal, x => x.Id == bebe.NomEvenementId);
        Assert.Equal(TypeEvenement.Langage, e.Type);
        Assert.Contains($"« {bebe.NomLangue} », qui veut dire « {bebe.NomSens} »", e.Texte);
    }

    [Fact]
    public void Interprete_QuiParleAussiLAutreLangue_EstNoteUneFois()
    {
        var social = new VieSociale(new Alea(10));
        var a = Communaute(1);
        var b = Communaute(2);
        var communautes = new List<Communaute> { a, b };
        social.Communautes = communautes;
        var idees = new[] { "eau", "feu", "pierre", "arbre", "soleil", "lune", "pluie", "vent", "oiseau", "poisson", "nuit", "jour", "terre", "ciel", "herbe" };
        var groupeA = Enumerable.Range(1, 4).Select(i => Humain(i, communaute: 1)).ToList();
        var groupeB = Enumerable.Range(5, 4).Select(i => Humain(i, communaute: 2)).ToList();
        foreach (var h in groupeA) foreach (var x in idees) h.Vocabulaire.Add(Mot(x, "ko" + x, langue: 1));
        foreach (var h in groupeB) foreach (var x in idees) h.Vocabulaire.Add(Mot(x, "zu" + x, langue: 2));
        // Une captive venue de B, qui a appris la langue de A sans oublier la sienne.
        var captive = Humain(9, communaute: 1);
        foreach (var x in idees) captive.Vocabulaire.Add(Mot(x, "ko" + x, langue: 1));
        foreach (var x in idees.Take(10)) captive.Vocabulaire.Add(Mot(x, "zu" + x, langue: 2));
        var tous = groupeA.Concat(groupeB).Append(captive).ToList();
        social.ConstaterLangues(tous, communautes, Maintenant);
        Assert.Equal(2, captive.LangueSecondeId);
        Assert.All(groupeA, h => Assert.Equal(0, h.LangueSecondeId));
        social.ConstaterLangues(tous, communautes, Maintenant + 7 * Jour);
        Assert.Single(social.Journal, e => e.HumainId == 9 && e.Texte.Contains("interprète", StringComparison.Ordinal));
    }

    [Fact]
    public void Dialecte_QuandLesMotsDivergent_DevientUneLangueAPart()
    {
        var social = new VieSociale(new Alea(12));
        var mere = Communaute(1);
        var fille = Communaute(2);
        fille.LangueId = 1;
        var communautes = new List<Communaute> { mere, fille };
        social.Communautes = communautes;
        var idees = new[] { "eau", "feu", "pierre", "arbre", "soleil", "lune", "pluie", "vent", "oiseau", "poisson", "nuit", "jour", "terre", "ciel", "herbe", "vent" }.Distinct().ToArray();
        var a = Enumerable.Range(1, 4).Select(i => Humain(i, communaute: 1)).ToList();
        var b = Enumerable.Range(5, 4).Select(i => Humain(i, communaute: 2)).ToList();
        foreach (var h in a) foreach (var x in idees) h.Vocabulaire.Add(Mot(x, "ko" + x, langue: 1));
        // Encore parents : un dialecte.
        foreach (var h in b) foreach (var (x, k) in idees.Select((x, k) => (x, k))) h.Vocabulaire.Add(Mot(x, k < 4 ? "zu" + x : "ko" + x, langue: 1));
        var tous = a.Concat(b).ToList();
        social.ConstaterLangues(tous, communautes, Maintenant);
        Assert.Equal(1, fille.LangueId);
        Assert.Equal(1, Langues.LangueDe(fille));
        // Les mots ont dérivé : plus de la moitié ne se reconnaissent plus.
        foreach (var h in b) { h.Vocabulaire.Clear(); foreach (var x in idees) h.Vocabulaire.Add(Mot(x, "zu" + x, langue: 1)); }
        social.ConstaterLangues(tous, communautes, Maintenant + 7 * Jour);
        Assert.Equal(2, fille.LangueId);
        Assert.All(b, h => Assert.All(h.Vocabulaire, m => Assert.Equal(2, m.Langue)));
        Assert.Single(social.Journal, e => e.Texte.Contains("langue à part", StringComparison.Ordinal));
    }

    [Fact]
    public void Etranger_SesMotsNeFontPasLaLangueDuGroupe_EtOnLuiParleDansLaSienne()
    {
        var social = new VieSociale(new Alea(13));
        var a = Communaute(1);
        var b = Communaute(2);
        var communautes = new List<Communaute> { a, b };
        social.Communautes = communautes;
        var hote = Humain(1, communaute: 1);
        hote.Vocabulaire.Add(Mot("eau", "kalu", langue: 1));
        hote.Vocabulaire.Add(Mot("eau", "zimo", langue: 2));
        var venu = Humain(2, communaute: 2);
        venu.Vocabulaire.Add(Mot("eau", "zimo", langue: 2));
        hote.StadeParole = venu.StadeParole = 1;
        // L'hôte qui connaît le mot de l'étranger le lui dit dans sa langue : ils se comprennent.
        social.Echanger(hote, venu, "eau", a, communautes, [hote, venu], Maintenant);
        Assert.Equal(0.6, Langues.MotDe(venu, "eau")!.Force, 3);
        // Un étranger accueilli : son mot ne compte pas dans la langue du groupe.
        venu.CommunauteId = 1;
        var groupe = new List<Humain> { hote, venu, Humain(3, communaute: 1) };
        groupe[2].Vocabulaire.Add(Mot("eau", "kalu", langue: 1));
        social.ConstaterLangues(groupe, communautes, Maintenant);
        Assert.Equal("kalu", a.Lexique["eau"]);
    }

    [Fact]
    public void Sons_SEcriventEtSeRelisent_EtLesMotsForgesLesEmploient()
    {
        var alea = new Alea(11);
        var sons = Sons.Tirer(alea);
        var relu = Sons.Lire(sons.Ecrire())!;
        Assert.Equal(sons.Consonnes, relu.Consonnes);
        Assert.Equal(sons.Voyelles, relu.Voyelles);
        for (var i = 0; i < 200; i++)
        {
            var mot = Langues.Forger(sons, alea, 2);
            Assert.All(Sons.Decouper(mot), s => Assert.Contains(s.Son, sons.Consonnes.Concat(sons.Voyelles).Concat(sons.Finales)));
        }
        Assert.Throws<InvalidDataException>(() => Sons.Lire("k.t|a|n"));
        Assert.Throws<InvalidDataException>(() => Sons.Lire("k;--|a|n|0.1"));
    }

    [Fact]
    public void Langage_SeRelit_EtUnMondeV25SeCharge()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "monde-langage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var monde = new MondeSimule(Carte, MondeSimule.Nouveau(42));
            monde.AvancerDe(Jour);
            var etat = monde.Etat;
            var h = etat.Humains![0];
            h.Vocabulaire.Add(new Mot("eau", "kalu", false, 0.7, 5, 1, 1));
            h.Vocabulaire.Add(new Mot("savoir:3", "1.2.3", true, 0.4, 6, 2, 3));
            (h.StadeParole, h.ParoleEnfant, h.Ordre, h.NomLangue, h.NomSens, h.NomLangueLe, h.NomParId, h.NomEvenementId, h.LangueSecondeId) =
                (3, true, OrdreMots.VSO, "Kaluti", "rivière", 7, 2, 8, 2);
            var c = Communaute();
            c.Lexique["eau"] = "kalu";
            c.Lexique["plante:4"] = "maso";
            c.Signes["savoir:3"] = "1.2.3";
            (c.Sons, c.Stade, c.Ordre, c.ParoleLe, c.CombinaisonLe, c.OrdreLe, c.PalierMots, c.LangueId) =
                (Sons.Tirer(new Alea(1)), 3, OrdreMots.VSO, 10, 20, 30, 10, 2);
            c.HistoireLangue.Add(new HistoireMot("eau", "kalu", "kalo", false, 9, 1, ChangementMot.Deforme, 0, 4));
            etat = etat with { Communautes = [c] };
            var fichier = Path.Combine(dossier, "monde.db");
            Directory.CreateDirectory(dossier);
            new DepotMonde(fichier).Enregistrer(etat);
            var relu = new DepotMonde(fichier).Charger()!;
            var r = relu.Humains![0];
            Assert.Equal(h.Vocabulaire, r.Vocabulaire);
            Assert.Equal((3, true, OrdreMots.VSO, "Kaluti", "rivière", 7L, 2, 8L, 2),
                (r.StadeParole, r.ParoleEnfant, r.Ordre, r.NomLangue, r.NomSens, r.NomLangueLe, r.NomParId, r.NomEvenementId, r.LangueSecondeId));
            var rc = Assert.Single(relu.Communautes!);
            Assert.Equal(c.Lexique, rc.Lexique);
            Assert.Equal(c.Signes, rc.Signes);
            Assert.Equal(c.Sons!.Ecrire(), rc.Sons!.Ecrire());
            Assert.Equal((c.Stade, c.Ordre, c.ParoleLe, c.CombinaisonLe, c.OrdreLe, c.PalierMots, c.LangueId),
                (rc.Stade, rc.Ordre, rc.ParoleLe, rc.CombinaisonLe, rc.OrdreLe, rc.PalierMots, rc.LangueId));
            Assert.Equal(c.HistoireLangue, rc.HistoireLangue);

            DepotMondeTests.ExecuterSql(fichier, DepotMondeTests.RetourVersion25);
            var v25 = new DepotMonde(fichier).Charger()!;
            Assert.Empty(v25.Humains![0].Vocabulaire);
            Assert.Equal(0, v25.Humains[0].StadeParole);
            Assert.Null(v25.Communautes![0].Sons);
            new DepotMonde(fichier).Enregistrer(v25, remplacer: true);
            Assert.Equal(DepotMonde.VersionSchema, DepotMonde.Decrire(fichier)!.Version);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        }
    }
}
