using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>
/// 0.29.2 : les visites, les rassemblements et les ouvrages demandés respectent le calcul sur plusieurs cœurs. Deux groupes amis
/// vivent à une vingtaine de cases l'un de l'autre, avec des proches de part et d'autre ; un rassemblement commence ; tous les
/// adultes savent tailler, plus ou moins bien. La durée se règle par MONDE_GARDE_JOURS (60 jours par défaut).
/// </summary>
public sealed class GardeRencontresTests
{
    private static readonly int Jours = int.TryParse(Environment.GetEnvironmentVariable("MONDE_GARDE_JOURS"), out var j) ? j : 60;
    private static readonly Carte Carte = MondeSimuleTests.Carte42;

    /// <summary>Une case de terre ferme à une vingtaine de cases de <paramref name="x"/>, <paramref name="y"/>.</summary>
    private static (int X, int Y) TerreVoisine(int x, int y)
    {
        for (var d = 20; d < 40; d++)
        {
            foreach (var (dx, dy) in new[] { (d, 0), (-d, 0), (0, d), (0, -d), (d, d), (-d, -d), (d, -d), (-d, d) })
            {
                var (nx, ny) = (x + dx, y + dy);
                if (nx < 0 || ny < 0 || nx >= Carte.Largeur || ny >= Carte.Hauteur) continue;
                if (Climat.EstPraticable(Carte.BiomeEn(nx, ny))) return (nx, ny);
            }
        }
        throw new InvalidOperationException("pas de terre voisine");
    }

    private static MondeSimule NouveauMonde(int coeurs)
    {
        var etat = new MondeSimule(Carte, MondeSimule.Nouveau(42)).Etat;
        var humains = etat.Humains!;
        var p1 = (Carte.Emplacements[0].X, Carte.Emplacements[0].Y);
        var p2 = TerreVoisine(p1.Item1, p1.Item2);
        var groupes = new[] { humains.Take(14).ToList(), humains.Skip(14).Take(14).ToList() };
        void Lier(Humain a, Humain b, double affection)
        {
            a.Liens.RemoveAll(l => l.Id == b.Id);
            b.Liens.RemoveAll(l => l.Id == a.Id);
            a.Liens.Add(new Lien(b.Id, 60, affection, 0, Amis: true));
            b.Liens.Add(new Lien(a.Id, 60, affection, 0, Amis: true));
        }
        for (var g = 0; g < 2; g++)
        {
            var (x, y) = g == 0 ? p1 : p2;
            foreach (var h in groupes[g])
            {
                (h.X, h.Y) = (x + h.Id % 3, y + h.Id % 2);
                h.CommunauteId = g + 1;
                h.Reserves = 80;
                // Tous savent tailler une pointe, plus ou moins sûrement : les maladroits iront demander aux habiles.
                h.Savoirs.Add(new Savoir(Geste.Gratter, Matiere.Branche, Matiere.Eclat, Effet.Pointe, 0, Symptome.Aucun, 4 + h.Id % 20, 2 + h.Id % 19, 0, 0, OrigineSavoir.Essai, 0, 0));
                h.Savoirs.Add(new Savoir(Geste.Frapper, Matiere.Silex, Matiere.Galet, Effet.Eclat, 0, Symptome.Aucun, 10, 8, 0, 0, OrigineSavoir.Essai, 0, 0));
                if (h.Id % 2 == 0) h.Objets.Add(new Objet(Matiere.Eclat, 0.8, 1));
                if (h.Id % 3 == 0) h.Objets.Add(new Objet(Matiere.Branche, 0.8, 1));
            }
            foreach (var a in groupes[g])
                foreach (var b in groupes[g].Where(b => b.Id > a.Id)) Lier(a, b, 60);
        }
        // Des amis de part et d'autre, qui ne se sont pas vus depuis longtemps.
        for (var i = 0; i < 6; i++) Lier(groupes[0][i], groupes[1][i], 75);
        var communautes = new List<Communaute>();
        for (var g = 0; g < 2; g++)
        {
            var (x, y) = g == 0 ? p1 : p2;
            communautes.Add(new Communaute { Id = g + 1, Nom = $"Communauté de {groupes[g][0].Nom}", FondeeLe = 0, FigureId = groupes[g][0].Id, CentreX = x, CentreY = y, Rayon = 3 });
        }
        var rassemblement = new Rassemblement
        {
            Id = 1, HoteId = 1, InviteeId = 2, X = p1.Item1, Y = p1.Item2, Debut = 0, Fin = 10 * Calendrier.SecondesParJour,
        };
        // Comme au début d'un rassemblement : une partie de chaque groupe y prend part.
        foreach (var h in groupes[0].Concat(groupes[1]).Where(h => h.Id % 2 == 1)) h.RassemblementId = 1;
        etat = etat with
        {
            Communautes = communautes,
            Relations = [new RelationCommunautes(1, 2, EtatRelation.Alliees)],
            Rassemblements = [rassemblement],
        };
        return new MondeSimule(Carte, etat) { Coeurs = coeurs };
    }

    private static string Rencontres(MondeSimule monde)
    {
        var etat = monde.Etat;
        return string.Join(";", etat.Humains!.Select(h =>
                   $"{h.Id}:{h.VisiteChezId}:{h.VisiteDepuis}:{h.VisiteArriveeLe}:{h.VisiteJusquA}:{h.VisiteLe}:{h.RassemblementId}:{h.Rentrer}:{h.VenuDeId}"
                   + $":{h.DemandeAId}:{h.DemandeOuvrage}:{h.DemandeLe}:{h.ServisTaille}:{h.Specialite}:{string.Join(",", h.Objets)}:{h.Karma:R}"))
               + "\n" + string.Join(";", etat.Rassemblements ?? []);
    }

    private static string Journal(MondeSimule monde) =>
        string.Join("\n", monde.Etat.JournalNonEnregistre!.Select(e => $"{e.Id} {e.CauseId} {e.HumainId} {e.AutreId} {e.Karma:R} {e.Texte}"));

    [Fact]
    public void Rencontres_DeuxCoeursOuQuatre_MemeHistoire()
    {
        var deux = NouveauMonde(2);
        var quatre = NouveauMonde(4);
        deux.JournalQuotidien = quatre.JournalQuotidien = true;
        deux.AvancerDe(Jours * Calendrier.SecondesParJour);
        quatre.AvancerDe(Jours * Calendrier.SecondesParJour);
        Assert.Equal(HumainsTests.Empreinte(deux), HumainsTests.Empreinte(quatre));
        Assert.Equal(Rencontres(deux), Rencontres(quatre));
        Assert.Equal(Journal(deux), Journal(quatre));
        // Les gestes de la version se sont bien produits.
        var journal = Journal(deux);
        Assert.Contains("Fin du rassemblement", journal);
        Assert.Contains(" pour lui", journal.Replace(" pour elle", " pour lui"));
    }

    [Fact]
    public void Rencontres_PendantLesTours_LeMondeNeChangePas()
    {
        var monde = NouveauMonde(4);
        string? avant = null;
        var ecarts = 0;
        monde.Surveillant = debut =>
        {
            var empreinte = HumainsTests.Empreinte(monde, listesPropres: false) + "\n" + Rencontres(monde) + "\n" + Journal(monde);
            if (debut) avant = empreinte;
            else if (avant != empreinte) ecarts++;
        };
        monde.AvancerDe(Math.Min(Jours, 20) * Calendrier.SecondesParJour);
        Assert.Equal(0, ecarts);
    }
}
