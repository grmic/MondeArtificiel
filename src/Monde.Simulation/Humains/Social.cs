using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Lien d'un humain avec un autre. Il est symétrique : les deux humains portent la même valeur.
/// </summary>
/// <param name="Familiarite">De 0 (inconnu) à 100 : monte avec le temps passé ensemble, baisse lentement sans se voir.</param>
/// <param name="Affection">De −100 (hostilité) à 100 (attachement profond).</param>
/// <param name="Derniere">Dernière journée passée ensemble.</param>
/// <param name="Amis">Vrai une fois l'amitié née (affection ≥ 40), jusqu'à ce qu'elle retombe sous 20.</param>
/// <param name="Rivaux">Vrai une fois l'hostilité née (affection ≤ −30), jusqu'à ce qu'elle remonte au-dessus de −10.</param>
public sealed record Lien(int Id, double Familiarite, double Affection, long Derniere, bool Amis = false, bool Rivaux = false);

/// <summary>
/// Un groupe d'humains liés entre eux qui vivent ensemble. Il n'est pas décrété : il est constaté
/// chaque semaine à partir des liens (voir <see cref="VieSociale.Regrouper"/>).
/// </summary>
public sealed class Communaute
{
    public required int Id { get; init; }

    /// <summary>Nom donné pour l'observateur, d'après sa figure centrale à sa naissance.</summary>
    public required string Nom { get; init; }

    public required long FondeeLe { get; init; }
    public long? DisperseeLe { get; set; }

    /// <summary>Humain le plus influent du groupe : charismatique et aimé. Il n'a aucun titre, c'est un constat.</summary>
    public int FigureId { get; set; }

    public bool Active => DisperseeLe is null;

    /// <summary>Centre du groupe (position moyenne des adultes) et rayon de son territoire, en cases (mis à jour chaque semaine).</summary>
    public int CentreX { get; set; }
    public int CentreY { get; set; }
    public int Rayon { get; set; }

    /// <summary>Ressources autour du groupe rapportées à leur niveau naturel (1 : intactes, 0 : épuisées).</summary>
    public double Abondance { get; set; } = 1;

    /// <summary>Vrai tant que le groupe manque de nourriture (réserves de ses membres basses ou terres épuisées).</summary>
    public bool Disette { get; set; }

    /// <summary>Terres vers lesquelles le groupe se déplace (−1 : il reste où il est).</summary>
    public int DestinationX { get; set; } = -1;
    public int DestinationY { get; set; } = -1;

    public Communaute Copier() => (Communaute)MemberwiseClone();
}

/// <summary>
/// Liens, humeur, deuil et regroupements. Appelé une fois par jour par le monde (une fois par semaine pour les groupes).
/// Les valeurs de départ s'appuient sur la psychologie sociale : l'exposition répétée rend plus familier et plus
/// aimable (Zajonc, 1968), on s'attache davantage à qui nous ressemble (homophilie) ; l'agréabilité favorise les liens,
/// le névrosisme les conflits. Le nombre de liens qu'un humain entretient dépend de son extraversion
/// (la limite cognitive d'environ 150 relations de Dunbar n'est pas atteinte dans un si petit monde).
/// </summary>
public sealed partial class VieSociale(Alea alea)
{
    public const double SeuilAmitie = 40;
    public const double FinAmitie = 20;
    public const double SeuilHostilite = -30;
    public const double FinHostilite = -10;
    public const int JoursEntreRegroupements = 7;

    /// <summary>Au-delà de cette distance (en cases, une demi-journée de marche environ), deux humains ne vivent pas ensemble.</summary>
    public const int DistanceCommunaute = 6;

    /// <summary>Événements à verser au journal.</summary>
    public List<EvenementJournal> Journal { get; } = [];

    private readonly Dictionary<int, int> _voisins = [];

    /// <summary>Partages de nourriture du jour (donneur, receveur), notés une fois par jour au journal.</summary>
    public HashSet<(int Donneur, int Receveur)> PartagesDuJour { get; } = [];

    private void Noter(long maintenant, TypeEvenement type, Humain h, Humain? autre, string texte) =>
        Journal.Add(new EvenementJournal(maintenant, type, h.Id, autre?.Id ?? 0, texte));

    private static string E(Humain h) => h.Sexe == Sexe.Femme ? "e" : "";

    public static int LiensMax(Humain h) => 10 + (int)(40 * h.Personnalite.Extraversion);

    // ───────────── Liens ─────────────

    /// <summary>Une journée : ceux qui sont proches l'un de l'autre se lient ; les autres liens s'estompent.</summary>
    public void Journee(List<Humain> humains, long maintenant)
    {
        var presents = humains.Where(h => h.EstVivant && !h.EstBebe(maintenant)).ToList();
        // Nombre d'humains à moins de 4 cases de chacun : la foule crée des frictions.
        _voisins.Clear();
        foreach (var h in presents)
        {
            h.CompagnieDuJour = 0;
            _voisins[h.Id] = 0;
        }
        for (var i = 0; i < presents.Count; i++)
        {
            for (var j = i + 1; j < presents.Count; j++)
            {
                if (Math.Abs(presents[i].X - presents[j].X) > 4 || Math.Abs(presents[i].Y - presents[j].Y) > 4) continue;
                _voisins[presents[i].Id]++;
                _voisins[presents[j].Id]++;
            }
        }

        for (var i = 0; i < presents.Count; i++)
        {
            var a = presents[i];
            for (var j = i + 1; j < presents.Count; j++)
            {
                var b = presents[j];
                if (Math.Abs(a.X - b.X) > 2 || Math.Abs(a.Y - b.Y) > 2) continue;
                Rapprocher(a, b, maintenant);
            }
        }

        foreach (var h in presents)
        {
            Estomper(h, maintenant);
            Limiter(h);
        }

        foreach (var (donneur, receveur) in PartagesDuJour.OrderBy(p => p.Donneur).ThenBy(p => p.Receveur))
        {
            var d = humains[donneur - 1];
            var r = humains[receveur - 1];
            Noter(maintenant, TypeEvenement.Partage, d, r, $"{d.Nom} partage sa nourriture avec {r.Nom}.");
        }
        PartagesDuJour.Clear();
    }

    private void Rapprocher(Humain a, Humain b, long maintenant)
    {
        var la = a.LienAvec(b.Id);
        var pa = a.Personnalite;
        var pb = b.Personnalite;

        // Compatibilité : bienveillance des deux, ressemblance, et peu d'humeurs à vif.
        var bienveillance = (pa.Agreabilite + pb.Agreabilite) / 2 - 0.5;
        // Écart moyen entre deux humains pris au hasard : environ 0,17 par trait ; plus proches que ça, ils se ressemblent.
        var ressemblance = 0.17 - (Math.Abs(pa.Extraversion - pb.Extraversion) + Math.Abs(pa.Ouverture - pb.Ouverture)
                                   + Math.Abs(pa.Transcendance - pb.Transcendance)) / 3;
        var tension = (pa.Nevrosisme + pb.Nevrosisme) / 2 - 0.5;
        // Trop nombreux au même endroit, les frictions s'accumulent (les bandes de chasseurs-cueilleurs comptent
        // en général 20 à 50 personnes et se scindent au-delà ; Marlowe, 2005).
        var foule = Math.Max(0, Math.Max(_voisins[a.Id], _voisins[b.Id]) - 25) * 0.04;
        var delta = 0.3 + 3 * bienveillance + 6 * ressemblance - 2 * tension - foule + 0.6 * Biologie.Normale(alea);

        // La famille et le couple soudent ; la faim partagée attise les querelles.
        if (Parents(a, b)) delta += 1.0;
        if (a.PartenaireId == b.Id) delta += 1.5;
        if (a.Faim > 60 && b.Faim > 60) delta -= 1.5 * (1 - (pa.Agreabilite + pb.Agreabilite) / 2);
        // Quand les réserves s'épuisent, chacun voit en l'autre un rival pour la nourriture.
        if (a.Reserves < 25 && b.Reserves < 25) delta -= 0.7;
        // Entre membres de deux communautés, la relation entre les groupes colore la rencontre.
        if (a.CommunauteId != 0 && b.CommunauteId != 0 && a.CommunauteId != b.CommunauteId)
        {
            delta += RelationEntre(a.CommunauteId, b.CommunauteId) switch
            {
                EtatRelation.Alliees => 0.5,
                EtatRelation.Rivales => -1.0,
                EtatRelation.Hostiles => -1.5,
                _ => 0,
            };
        }

        var avant = la ?? new Lien(b.Id, 0, 0, maintenant);
        var apres = avant with
        {
            Familiarite = Math.Min(100, avant.Familiarite + 1),
            Affection = Math.Clamp(avant.Affection + delta, -100, 100),
            Derniere = maintenant,
        };
        apres = Seuils(a, b, avant, apres, maintenant);
        Poser(a, apres);
        Poser(b, apres with { Id = a.Id });

        if (apres.Affection >= 20)
        {
            a.CompagnieDuJour++;
            b.CompagnieDuJour++;
        }
    }

    private Lien Seuils(Humain a, Humain b, Lien avant, Lien apres, long maintenant)
    {
        if (!avant.Amis && apres.Affection >= SeuilAmitie && apres.Familiarite >= 20)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} se sont attachés l'un à l'autre.");
            return apres with { Amis = true };
        }
        if (avant.Amis && apres.Affection < FinAmitie)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} se sont éloignés.");
            return apres with { Amis = false };
        }
        if (!avant.Rivaux && apres.Affection <= SeuilHostilite)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} ne se supportent plus.");
            return apres with { Rivaux = true };
        }
        if (avant.Rivaux && apres.Affection > FinHostilite)
        {
            Noter(maintenant, TypeEvenement.Lien, a, b, $"{a.Nom} et {b.Nom} ont fait la paix.");
            return apres with { Rivaux = false };
        }
        return apres;
    }

    /// <summary>Sans se voir, on se connaît de moins en moins et les sentiments s'atténuent.</summary>
    private static void Estomper(Humain h, long maintenant)
    {
        for (var i = h.Liens.Count - 1; i >= 0; i--)
        {
            var l = h.Liens[i];
            if (l.Derniere >= maintenant - Calendrier.SecondesParJour / 2) continue;
            var estompe = l with { Familiarite = Math.Max(0, l.Familiarite - 0.1), Affection = l.Affection * 0.997 };
            if (estompe.Familiarite <= 0 && Math.Abs(estompe.Affection) < 5) h.Liens.RemoveAt(i);
            else h.Liens[i] = estompe;
        }
    }

    /// <summary>Au-delà de ce qu'il peut entretenir, l'humain perd le lien le plus faible.</summary>
    private static void Limiter(Humain h)
    {
        var max = LiensMax(h);
        while (h.Liens.Count > max)
        {
            h.Liens.Remove(h.Liens.MinBy(l => (l.Familiarite + Math.Abs(l.Affection), l.Derniere))!);
        }
    }

    private static void Poser(Humain h, Lien lien)
    {
        var i = h.Liens.FindIndex(l => l.Id == lien.Id);
        if (i >= 0) h.Liens[i] = lien;
        else h.Liens.Add(lien);
    }

    /// <summary>Parent et enfant, ou frères et sœurs (même mère).</summary>
    public static bool Parents(Humain a, Humain b) =>
        a.MereId == b.Id || a.PereId == b.Id || b.MereId == a.Id || b.PereId == a.Id
        || (a.MereId != 0 && a.MereId == b.MereId);

    /// <summary>Un geste de bonté rapproche, surtout aux yeux de celui qui reçoit.</summary>
    public static void Gratitude(Humain donneur, Humain receveur, double force)
    {
        var l = receveur.LienAvec(donneur.Id);
        if (l is null) return;
        var affection = Math.Min(100, l.Affection + force);
        Poser(receveur, l with { Affection = affection });
        if (donneur.LienAvec(receveur.Id) is { } d) Poser(donneur, d with { Affection = affection });
    }

    // ───────────── Humeur et deuil ─────────────

    /// <summary>
    /// Humeur du jour : elle tend vers un niveau propre à chacun (plus haut chez les extravertis,
    /// plus bas chez les anxieux), abaissé par l'inconfort et le chagrin, relevé par la compagnie de ceux qu'on aime.
    /// </summary>
    public void Humeurs(List<Humain> humains, long maintenant)
    {
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            var p = h.Personnalite;
            var inconfort = h.InconfortDuJour / (Calendrier.SecondesParJour / MondeSimule.SecondesParPas);
            h.InconfortDuJour = 0;
            if (h.EstBebe(maintenant)) continue;

            var base_ = 50 + 25 * (p.Extraversion - 0.5) - 40 * (p.Nevrosisme - 0.5);
            var compagnie = 3 * Math.Min(4, h.CompagnieDuJour);
            // Le chagrin pèse plus lourd chez les plus sensibles.
            var cible = base_ - 25 * inconfort + compagnie - (0.2 + 0.4 * p.Nevrosisme) * h.Chagrin;
            h.Humeur = Math.Clamp(h.Humeur + (cible - h.Humeur) * 0.3, 0, 100);

            // Le chagrin s'apaise : en un mois environ pour la plupart, en des mois voire des années chez les plus
            // sensibles (trajectoires du deuil de Bonanno, 2004 : résilience majoritaire, deuil chronique pour 10 à 15 %).
            var demiVie = 30 * Math.Exp(5 * (p.Nevrosisme - 0.5));
            h.Chagrin *= Math.Pow(0.5, 1 / demiVie);
            if (h.Chagrin < 1)
            {
                h.Chagrin = 0;
                h.DeuilDeId = 0;
            }

            if (h.Humeur < 20) h.JoursSombres++;
            else if (h.Humeur > 35) h.JoursSombres = 0;

            // Trois semaines au plus mal : une tristesse ordinaire, même un deuil, ne suffit pas d'habitude.
            if (!h.EstDeprime && h.JoursSombres >= 21 && !h.EstEnfant(maintenant))
            {
                h.DeprimeDepuis = maintenant;
                Noter(maintenant, TypeEvenement.Humeur, h, null, $"{h.Nom} sombre dans une profonde tristesse.");
            }
            // Un épisode dépressif dure au moins deux semaines (critère du DSM-5) et ne cède qu'à un vrai mieux.
            else if (h.EstDeprime && h.Humeur > 35 && maintenant - h.DeprimeDepuis >= 14 * Calendrier.SecondesParJour)
            {
                h.DeprimeDepuis = null;
                h.JoursSombres = 0;
                Noter(maintenant, TypeEvenement.Humeur, h, null, $"{h.Nom} reprend goût à la vie.");
            }
        }
    }

    /// <summary>
    /// Risque quotidien de suicide d'un adulte déprimé : environ 0,2 % par an, plus fort avec le chagrin.
    /// (Ordre de grandeur : un taux de 10 à 15 pour 100 000 par an dans la population, très concentré
    /// chez les personnes déprimées.)
    /// </summary>
    public double RisqueDesespoir(Humain h, long maintenant) =>
        h.EstDeprime && h.AgeEnAnnees(maintenant) >= 15
            ? 0.002 / Calendrier.JoursParAn * (1 + h.Chagrin / 50) * (0.6 + 0.8 * h.Personnalite.Nevrosisme)
            : 0;

    /// <summary>
    /// Une mort endeuille ceux qui tenaient au disparu : son compagnon, ses parents (selon leur attachement),
    /// ses enfants, ses frères et sœurs, ses amis, et un peu toute sa communauté.
    /// </summary>
    public void Endeuiller(Humain mort, List<Humain> humains, long maintenant)
    {
        foreach (var h in humains)
        {
            if (!h.EstVivant || h.Id == mort.Id || h.EstBebe(maintenant)) continue;
            double k = 0;
            if (h.PartenaireId == mort.Id || mort.PartenaireId == h.Id) k = 70;
            if (mort.MereId == h.Id || mort.PereId == h.Id)
            {
                k = Math.Max(k, 100 * h.Personnalite.Attachement * (mort.AgeEnAnnees(maintenant) < 1 ? 0.7 : 1));
            }
            if (h.MereId == mort.Id || h.PereId == mort.Id) k = Math.Max(k, h.EstEnfant(maintenant) ? 70 : 45);
            if (h.MereId != 0 && h.MereId == mort.MereId) k = Math.Max(k, 40);
            var affection = h.AffectionPour(mort.Id);
            if (affection > 0) k = Math.Max(k, 0.7 * affection);
            if (k < 8 && h.CommunauteId != 0 && h.CommunauteId == mort.CommunauteId) k = 8;
            if (k <= 0) continue;

            k *= 0.6 + 0.8 * h.Personnalite.Nevrosisme; // les plus sensibles souffrent davantage
            if (k > h.Chagrin) h.DeuilDeId = mort.Id;
            h.Chagrin = Math.Min(100, h.Chagrin + k);
            h.Humeur = Math.Max(0, h.Humeur - k / 2);
            if (k >= 25) Noter(maintenant, TypeEvenement.Humeur, h, mort, $"{h.Nom} pleure {mort.Nom}.");
        }
    }

    /// <summary>Une joie : naissance d'un enfant, nouveau compagnon…</summary>
    public static void Rejouir(Humain h, double force) => h.Humeur = Math.Min(100, h.Humeur + force);

    // ───────────── Communautés ─────────────

    /// <summary>
    /// Une fois par semaine : les humains reliés par des liens d'affection forment des groupes.
    /// Un groupe d'au moins 5 humains dont 3 adultes est une communauté. Elle garde son identité
    /// tant qu'elle conserve au moins la moitié de ses membres ; sinon elle se disperse.
    /// Il faut une amitié pour entrer, un simple lien d'affection pour rester (évite les va-et-vient),
    /// et les deux humains doivent vivre à moins de <see cref="DistanceCommunaute"/> cases l'un de l'autre.
    /// </summary>
    public void Regrouper(List<Humain> humains, List<Communaute> communautes, long maintenant, ISet<string> noms)
    {
        var membres = humains.Where(h => h.EstVivant).ToList();
        var parent = new Dictionary<int, int>();
        int Racine(int id)
        {
            while (parent[id] != id) id = parent[id] = parent[parent[id]];
            return id;
        }
        void Unir(int a, int b) => parent[Racine(a)] = Racine(b);
        foreach (var h in membres) parent[h.Id] = h.Id;

        foreach (var h in membres)
        {
            // Bébés et enfants vont avec leurs parents.
            if (h.EstEnfant(maintenant))
            {
                foreach (var pid in new[] { h.MereId, h.PereId })
                {
                    if (pid > 0 && humains[pid - 1].EstVivant) Unir(h.Id, pid);
                }
                continue;
            }
            foreach (var l in h.Liens)
            {
                if (l.Id < h.Id || !parent.ContainsKey(l.Id)) continue;
                var autre = humains[l.Id - 1];
                if (autre.EstEnfant(maintenant)) continue;
                // Une communauté vit ensemble : des amis qui vivent loin l'un de l'autre n'en font pas une.
                if (Math.Max(Math.Abs(h.X - autre.X), Math.Abs(h.Y - autre.Y)) > DistanceCommunaute) continue;
                // Pour entrer, il faut une amitié ; pour rester, un lien d'affection suffit.
                var dejaEnsemble = h.CommunauteId != 0 && h.CommunauteId == autre.CommunauteId;
                if (dejaEnsemble ? l.Affection >= 15 && l.Familiarite >= 20 : l.Amis) Unir(h.Id, l.Id);
            }
        }

        var groupes = membres.GroupBy(h => Racine(h.Id))
            .Select(g => g.ToList())
            .Where(g => g.Count >= 5 && g.Count(h => !h.EstEnfant(maintenant)) >= 3)
            .OrderByDescending(g => g.Count).ThenBy(g => g.Min(h => h.Id))
            .ToList();

        var anciens = membres.ToDictionary(h => h.Id, h => h.CommunauteId);
        var tailles = membres.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => g.Count());
        var reprises = new HashSet<int>();
        var nouvelles = new Dictionary<int, int>(); // humain → communauté
        foreach (var g in groupes)
        {
            // La communauté d'avant la mieux représentée dans le groupe, si elle y garde au moins la moitié de ses membres.
            var candidate = g.Where(h => h.CommunauteId != 0 && !reprises.Contains(h.CommunauteId))
                .GroupBy(h => h.CommunauteId)
                .Select(c => (Id: c.Key, Nombre: c.Count()))
                .OrderByDescending(c => c.Nombre).ThenBy(c => c.Id)
                .FirstOrDefault();
            Communaute communaute;
            if (candidate.Id != 0 && candidate.Nombre * 2 >= tailles[candidate.Id])
            {
                communaute = communautes.First(c => c.Id == candidate.Id);
                reprises.Add(communaute.Id);
            }
            else
            {
                var figure = Figure(g, maintenant);
                var nom = $"Communauté de {figure.Nom}";
                var homonymes = communautes.Count(c => c.Nom == nom || c.Nom.StartsWith(nom + " (", StringComparison.Ordinal));
                communaute = new Communaute
                {
                    Id = communautes.Count + 1,
                    Nom = homonymes == 0 ? nom : $"{nom} ({homonymes + 1})",
                    FondeeLe = maintenant,
                    FigureId = figure.Id,
                };
                communautes.Add(communaute);
                reprises.Add(communaute.Id);
                // Groupe venu en majorité d'une même communauté : c'est une scission.
                var origine = g.Where(h => h.CommunauteId != 0).GroupBy(h => h.CommunauteId)
                    .Select(c => (Id: c.Key, Nombre: c.Count())).OrderByDescending(c => c.Nombre).ThenBy(c => c.Id).FirstOrDefault();
                Noter(maintenant, TypeEvenement.Communaute, figure, null, origine.Id != 0 && origine.Nombre * 2 > g.Count
                    ? $"{g.Count} humains quittent la {Minuscule(communautes[origine.Id - 1].Nom)} et se rassemblent autour de {figure.Nom}."
                    : $"Une communauté se forme autour de {figure.Nom} : {g.Count} humains vivent désormais ensemble.");
            }
            foreach (var h in g) nouvelles[h.Id] = communaute.Id;

            var nouvelleFigure = Figure(g, maintenant);
            if (nouvelleFigure.Id != communaute.FigureId)
            {
                communaute.FigureId = nouvelleFigure.Id;
                Noter(maintenant, TypeEvenement.Communaute, nouvelleFigure, null,
                    $"{nouvelleFigure.Nom} devient la figure centrale de la {Minuscule(communaute.Nom)}.");
            }
        }

        foreach (var c in communautes.Where(c => c.Active && !reprises.Contains(c.Id)))
        {
            c.DisperseeLe = maintenant;
            var figure = humains[c.FigureId - 1];
            Noter(maintenant, TypeEvenement.Communaute, figure, null, $"La {Minuscule(c.Nom)} se disperse.");
        }

        foreach (var h in membres)
        {
            var avant = anciens[h.Id];
            var apres = nouvelles.GetValueOrDefault(h.Id);
            h.CommunauteId = apres;
            if (avant == apres || h.EstBebe(maintenant)) continue;
            // Les arrivées dans une communauté qui vient de naître sont déjà dans l'annonce de sa formation.
            if (apres != 0 && communautes[apres - 1].FondeeLe != maintenant)
            {
                Noter(maintenant, TypeEvenement.Communaute, h, null, $"{h.Nom} rejoint la {Minuscule(communautes[apres - 1].Nom)}.");
            }
            else if (apres == 0 && communautes[avant - 1].Active)
            {
                Noter(maintenant, TypeEvenement.Communaute, h, null, $"{h.Nom} quitte la {Minuscule(communautes[avant - 1].Nom)}.");
            }
        }
    }

    private static string Minuscule(string nom) => char.ToLowerInvariant(nom[0]) + nom[1..];

    /// <summary>Figure centrale : le plus charismatique, pondéré par l'affection que lui portent les autres membres.</summary>
    private static Humain Figure(List<Humain> groupe, long maintenant)
    {
        var ids = groupe.Select(h => h.Id).ToHashSet();
        return groupe.Where(h => !h.EstEnfant(maintenant))
            .MaxBy(h => h.Personnalite.Charisme * (1 + h.Liens.Where(l => ids.Contains(l.Id) && l.Affection > 0).Sum(l => l.Affection) / 100))!;
    }
}
