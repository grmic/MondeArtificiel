using System.Security.Cryptography;
using System.Text;
using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>
/// 0.30.1 : le journal et les pensées sont des récits (clé et paramètres) écrits à l'affichage. En français, la phrase
/// est celle d'avant ; en anglais, elle s'accorde au genre et au nombre ; enregistrés, les récits se relisent à l'identique.
/// </summary>
public sealed class RecitsTests
{
    private static readonly Langue Fr = Catalogues.Fr;
    private static readonly Langue En = Catalogues.Toutes["en"];

    private static readonly Personne Ana = new(1, "Ana", true);
    private static readonly Personne Bor = new(2, "Bor", false);
    private static readonly Personne Eli = new(3, "Eli", true);

    [Fact]
    public void Francais_EliseEtAccorde()
    {
        Assert.Equal("Eli naît, fille d'Ana et de Bor.",
            Fr.Rediger(new Recit("j.naissance", ("b", Eli), ("m", Ana), ("p", new Recit("j.naissance.pere", ("p", Bor))))));
        Assert.Equal("Bor naît, fils d'Ana.", Fr.Rediger(new Recit("j.naissance", ("b", Bor), ("m", Ana), ("p", ""))));
        Assert.Equal("Ana pardonne à Bor de l'avoir volée.", Fr.Rediger(new Recit("j.pardon.vol", ("h", Ana), ("a", Bor))));
        Assert.Equal("Ana et Eli se sont attachées l'une à l'autre.",
            Fr.Rediger(new Recit("j.lien.amis", ("a", Ana), ("b", Eli), ("ab", new Groupe([Ana, Eli])))));
        Assert.Equal("Ana et Bor se sont attachés l'un à l'autre.",
            Fr.Rediger(new Recit("j.lien.amis", ("a", Ana), ("b", Bor), ("ab", new Groupe([Ana, Bor])))));
    }

    [Fact]
    public void Anglais_AccordeAuGenreEtAuNombre()
    {
        Assert.Equal("Eli is born, daughter of Ana and Bor.",
            En.Rediger(new Recit("j.naissance", ("b", Eli), ("m", Ana), ("p", new Recit("j.naissance.pere", ("p", Bor))))));
        Assert.Equal("Ana forgives Bor for stealing from her.", En.Rediger(new Recit("j.pardon.vol", ("h", Ana), ("a", Bor))));
        Assert.Equal("Bor forgives Ana for stealing from him.", En.Rediger(new Recit("j.pardon.vol", ("h", Bor), ("a", Ana))));

        var tako = new RefCommunaute(4, "Communauté Tako");
        var deBor = new RefCommunaute(5, "Communauté de Bor (2)");
        Assert.Equal("The Tako people merges into Bor's community (2): one of its members now lives among them.",
            En.Rediger(new Recit("j.fusion.fond", ("c", tako), ("d", deBor), ("n", 1L))));
        Assert.Equal("The Tako people merges into Bor's community (2): 3 of its members now live among them.",
            En.Rediger(new Recit("j.fusion.fond", ("c", tako), ("d", deBor), ("n", 3L))));
        Assert.Equal("La communauté Tako se fond dans la communauté de Bor (2) : 3 de ses membres vivent désormais avec eux.",
            Fr.Rediger(new Recit("j.fusion.fond", ("c", tako), ("d", deBor), ("n", 3L))));
    }

    [Fact]
    public void Communautes_NommeesSelonLaLangue()
    {
        Assert.Equal("Communauté d'Ana (3)", Fr.Communaute("Communauté de Ana (3)", phrase: false));
        Assert.Equal("la communauté Tako", Fr.Communaute("Communauté Tako", phrase: true));
        Assert.Equal("Ana's community (3)", En.Communaute("Communauté de Ana (3)", phrase: false));
        Assert.Equal("Tako people", En.Communaute("Communauté Tako", phrase: false));
        Assert.Equal("the Tako people", En.Communaute("Communauté Tako", phrase: true));
        Assert.Equal("Among the Tako people, people now admire well-fleshed bodies.",
            En.Rediger(new Recit("j.ideal.chair", ("c", new RefCommunaute(4, "Communauté Tako")))));
        Assert.Equal("In Ana's community, people now admire well-fleshed bodies.",
            En.Rediger(new Recit("j.ideal.chair", ("c", new RefCommunaute(4, "Communauté de Ana")))));
    }

    [Fact]
    public void Enregistre_SeRelitALIdentique()
    {
        var recit = new Recit("j.essai", ("h", Ana), ("c", new RefCommunaute(7, "Communauté Tako")), ("n", 12L), ("x", 0.25),
            ("mot", "Gounrae"), ("oui", true), ("g", new Groupe([Ana, Bor])),
            ("l", new Liste([new Recit("p.boire.1"), "eau", 3L], virgules: true)),
            ("r", new Recit("j.naissance.pere", ("p", Bor))));
        var relu = Recit.Lire(recit.Cle, recit.ParametresJson());
        Assert.Equal(recit, relu);
        Assert.Equal(recit, Recit.Decoder(recit.Encoder()));

        // Enregistrement compact : la clé et les noms une fois par forme, les valeurs seules sur chaque ligne.
        var formes = new List<(string Cle, string Noms)> { ("", "") };
        int Numero(Recit r)
        {
            var i = formes.IndexOf((r.Cle, r.Noms));
            if (i > 0) return i;
            formes.Add((r.Cle, r.Noms));
            return formes.Count - 1;
        }
        var valeurs = recit.Valeurs(Numero);
        Assert.StartsWith("[[1],", valeurs); // un humain par son numéro
        Personne[] humains = [Ana, Bor];
        Assert.Equal(recit, Recit.LireValeurs(recit.Cle, recit.Noms, valeurs, i => formes[i], id => humains[id - 1]));
        Assert.Null(Recit.Decoder("ceux du soleil"));
        Assert.Equal("ceux du soleil", Recit.EnFrancais("ceux du soleil"));
        Assert.Equal("ceux du soleil", Recit.EnFrancais(new Recit("sens.soleil").Encoder()));
        Assert.Equal("", new Recit("p.boire.1").ParametresJson());
    }

    [Fact]
    public void Serveur_EcritDansLaLangueDuNavigateur_EtSignaleLesPhrasesAnciennes()
    {
        var neuf = new EvenementJournal(0, TypeEvenement.Naissance, 3, 1,
            new Recit("j.naissance", ("b", Eli), ("m", Ana), ("p", ""))) { Id = 1 };
        var ancien = new EvenementJournal(0, TypeEvenement.Naissance, 3, 1, "Eli naît, fille de Ana.") { Id = 2 };
        try
        {
            Monde.Serveur.L.Definir("en");
            Assert.Equal(("Eli is born, daughter of Ana.", false), (Monde.Serveur.EvenementDto.Depuis(neuf).Texte, Monde.Serveur.EvenementDto.Depuis(neuf).Ancien));
            Assert.Equal(("Eli naît, fille de Ana.", true), (Monde.Serveur.EvenementDto.Depuis(ancien).Texte, Monde.Serveur.EvenementDto.Depuis(ancien).Ancien));
            Assert.Equal(("Tako people", "the Tako people"),
                (Monde.Serveur.L.Communaute("Communauté Tako"), Monde.Serveur.L.CommunautePhrase("Communauté Tako")));
            Assert.Equal("the river people", Monde.Serveur.L.Garde(new Recit("sens.riviere").Encoder()));
            Assert.Equal("ceux de la rivière", Monde.Serveur.L.Garde("ceux de la rivière"));
            Monde.Serveur.L.Definir("fr");
            Assert.Equal(("Eli naît, fille d'Ana.", false), (Monde.Serveur.EvenementDto.Depuis(neuf).Texte, Monde.Serveur.EvenementDto.Depuis(neuf).Ancien));
            Assert.False(Monde.Serveur.EvenementDto.Depuis(ancien).Ancien);
            Assert.Equal("communauté d'Ana (2)", Monde.Serveur.L.CommunauteMin("Communauté de Ana (2)"));
        }
        finally
        {
            Monde.Serveur.L.Definir("fr");
        }
    }

    // ───────────── Le français ne change pas ─────────────

    private const ulong Graine = 5;
    private const int Jours = 240;

    private static string FichierReference => Path.Combine(TrouverDossierTests(), "Donnees", "francais-reference.txt");

    private static string TrouverDossierTests()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var dossier = Path.Combine(d.FullName, "tests", "Monde.Tests");
            if (Directory.Exists(dossier)) return dossier;
        }
        throw new DirectoryNotFoundException("Dossier des tests introuvable.");
    }

    /// <summary>
    /// Pour chaque type d'événement (et pour les pensées) : nombre de lignes et empreinte de leurs phrases françaises,
    /// dans l'ordre du journal, après <see cref="Jours"/> jours du monde de graine <see cref="Graine"/>, vie quotidienne comprise.
    /// </summary>
    private static SortedDictionary<string, (int Lignes, string Empreinte, string Exemple)> Empreintes()
    {
        var monde = new MondeSimule(GenerateurContinent.Generer(Graine), MondeSimule.Nouveau(Graine)) { Coeurs = 1, JournalQuotidien = true };
        var lignes = new Dictionary<string, List<string>>();
        void Ajouter(string type, string texte)
        {
            if (!lignes.TryGetValue(type, out var l)) lignes[type] = l = [];
            l.Add(texte);
        }
        for (var j = 0; j < Jours; j += 30)
        {
            monde.AvancerDe(Math.Min(30, Jours - j) * Calendrier.SecondesParJour);
            var journal = monde.Etat.JournalNonEnregistre ?? [];
            foreach (var e in journal) Ajouter(e.Type.ToString(), e.Texte);
            monde.MarquerJournalEnregistre(journal.Count);
        }
        var (_, humains) = monde.InstantaneHumains();
        foreach (var h in humains) foreach (var p in h.Memoire.Pensees) Ajouter("Pensees", p.Texte);
        var resultat = new SortedDictionary<string, (int, string, string)>(StringComparer.Ordinal);
        foreach (var (type, l) in lignes)
        {
            var empreinte = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', l))))[..16];
            resultat[type] = (l.Count, empreinte, l[0]);
        }
        return resultat;
    }

    /// <summary>
    /// Le monde de référence écrit, en français, exactement les phrases vérifiées lors de la bascule (comparées une à une
    /// à celles de la 0.30.0 : seules diffèrent l'élision « d'Ana » et quelques accords au féminin, corrigés exprès).
    /// Si la simulation change volontairement, régénérer avec la variable MONDE_REGENERER_FRANCAIS=1.
    /// </summary>
    [Fact]
    public void Francais_IdentiqueAuMondeDeReference()
    {
        var actuel = Empreintes();
        var lignes = actuel.Select(x => $"{x.Key}\t{x.Value.Lignes}\t{x.Value.Empreinte}\t{x.Value.Exemple}").ToList();
        if (Environment.GetEnvironmentVariable("MONDE_REGENERER_FRANCAIS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FichierReference)!);
            File.WriteAllLines(FichierReference, lignes);
            return;
        }
        var attendu = File.ReadAllLines(FichierReference);
        Assert.Equal(attendu, lignes);
    }
}
