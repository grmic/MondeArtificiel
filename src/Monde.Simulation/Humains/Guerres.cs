using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'un combattant a en main. Personne ne sait encore fabriquer d'arme : on frappe des poings, on lance des pierres
/// ramassées, on cogne avec une branche (les chimpanzés font déjà de même : Goodall, 1986). Les vraies armes viendront des découvertes.
/// </summary>
public enum Arme : byte
{
    Poings = 0,
    Pierre = 1,
    Baton = 2,
}

/// <summary>Comment une guerre s'est terminée.</summary>
public enum IssueGuerre : byte
{
    EnCours = 0,
    /// <summary>Les deux groupes font la paix.</summary>
    Paix = 1,
    /// <summary>Le vaincu se soumet et livre une part de sa nourriture.</summary>
    Soumission = 2,
    /// <summary>Le vaincu part au loin.</summary>
    Fuite = 3,
    /// <summary>Les survivants du vaincu rejoignent le vainqueur.</summary>
    Absorption = 4,
    /// <summary>L'un des deux groupes a disparu (dispersé, fondu dans un autre).</summary>
    Extinction = 5,
}

/// <summary>
/// Une guerre entre deux communautés : elle commence au premier sang versé lors d'un raid entre groupes hostiles
/// et se termine par la paix, la soumission, la fuite ou l'absorption du vaincu. Les morts sont comptés de chaque côté.
/// </summary>
public sealed class Guerre
{
    public required int Id { get; init; }
    /// <summary>Communauté qui a porté le premier coup, et celle qui l'a reçu.</summary>
    public required int A { get; set; }
    public required int B { get; set; }
    public required long Debut { get; init; }
    public long? Fin { get; set; }
    public int MortsA { get; set; }
    public int MortsB { get; set; }
    public int Raids { get; set; }
    public IssueGuerre Issue { get; set; }
    /// <summary>Dernier raid où le sang a coulé.</summary>
    public long DernierSangLe { get; set; }
    /// <summary>Premier sang (cause de la guerre) et événement qui l'a terminée.</summary>
    public long EvenementId { get; set; }
    public long FinEvenementId { get; set; }
    /// <summary>Vainqueur à l'issue (0 : aucun, par exemple une paix).</summary>
    public int VainqueurId { get; set; }

    public Guerre Copier() => (Guerre)MemberwiseClone();
}

/// <summary>
/// Un raid en cours : quelques guerriers partent surprendre le camp d'un groupe ennemi, puis rentrent.
/// <see cref="X"/>, <see cref="Y"/> : camp visé (suivi tant que l'ennemi se déplace) ; <see cref="RetourX"/>,
/// <see cref="RetourY"/> : camp d'où ils sont partis.
/// </summary>
public sealed class Raid
{
    public required int CibleId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int RetourX { get; set; }
    public int RetourY { get; set; }
    public required long Depuis { get; init; }
    public required long EvenementId { get; init; }
    public List<int> Guerriers { get; set; } = [];
    public bool Retour { get; set; }
    public long RetourDepuis { get; set; }

    public Raid Copier()
    {
        var copie = (Raid)MemberwiseClone();
        copie.Guerriers = [.. Guerriers];
        return copie;
    }
}

/// <summary>
/// Une guerre en préparation : qui la veut (l'instigateur), contre qui, pourquoi, et quels alliés il compte entraîner.
/// Quelques jours ou quelques semaines plus tard (<see cref="DebatLe"/>), le groupe en débat : chacun suit ou non, les
/// alliés répondent, et la guerre est lancée ou abandonnée.
/// </summary>
public sealed class ProjetGuerre
{
    public required int CibleId { get; set; }
    public required int InstigateurId { get; init; }
    public required long Depuis { get; init; }
    /// <summary>Événement du journal qui annonce le projet.</summary>
    public required long EvenementId { get; init; }
    /// <summary>La raison avancée, en mots (« pour venger Ako, tué par Tem »).</summary>
    public required string Raison { get; init; }
    /// <summary>Communautés alliées sollicitées.</summary>
    public List<int> AlliesSollicites { get; set; } = [];

    /// <summary>Jour du débat (0 : projet enregistré avant que la date varie, débattu six jours après son annonce).</summary>
    public long DebatLe { get; set; }

    public long Debat => DebatLe > 0 ? DebatLe : Depuis + 6 * Temps.Calendrier.SecondesParJour;

    public ProjetGuerre Copier()
    {
        var copie = (ProjetGuerre)MemberwiseClone();
        copie.AlliesSollicites = [.. AlliesSollicites];
        return copie;
    }
}

/// <summary>
/// Guerres, raids, pactes, soumissions et paix entre communautés. Rien n'est décrété : un raid se décide quand un groupe
/// hostile a une raison de frapper (venger les siens, disputer des terres affamées), une figure peu conciliante et
/// l'avantage du nombre ; la paix, quand le sang ne coule plus et que les figures s'y prêtent.
/// <para>
/// Ordres de grandeur. Chez les chasseurs-cueilleurs nomades, la plupart des morts violentes viennent de querelles
/// personnelles et de vengeances (Fry et Söderberg, 2013) ; la guerre entre groupes y prend la forme de raids : une petite
/// troupe d'hommes surprend un camp, souvent à l'aube, quand elle a nettement l'avantage du nombre, et se retire
/// (Keeley, 1996 ; Wrangham et Glowacki, 2012, hypothèse du déséquilibre des forces). La vengeance appelle la vengeance
/// (Chagnon, 1988) et la crainte de manquer de nourriture attise les conflits (Ember et Ember, 1992). Selon les sites et
/// les peuples, de 0 à 30 % des morts d'adultes sont dues à la guerre, 14 % en moyenne (Bowles, 2009).
/// </para>
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>Un raid va rarement au-delà d'une journée de marche : environ 30 km.</summary>
    public static readonly int DistanceRaidMax = Echelle.Cases(30);

    /// <summary>
    /// Probabilité qu'un groupe très motivé, mené par une figure querelleuse et en nette supériorité, se mette à préparer
    /// un raid dans la semaine (une partie de ces projets sera abandonnée).
    /// </summary>
    private const double RaidParSemaine = 0.12;

    /// <summary>Un groupe ne repart en raid qu'après au moins quatre semaines.</summary>
    private const long RepitEntreRaids = 28 * Calendrier.SecondesParJour;

    /// <summary>Six mois sans raid sanglant avant qu'une paix soit envisagée.</summary>
    private const long CalmeAvantPaix = 180 * Calendrier.SecondesParJour;

    private const double PaixParSemaine = 0.04;

    private readonly List<Guerre> _guerres = [];
    private readonly Dictionary<(int A, int B), long> _pactes = [];

    /// <summary>Toutes les guerres, en cours ou finies, dans l'ordre où elles ont commencé.</summary>
    public IReadOnlyList<Guerre> Guerres => _guerres;

    public void ChargerGuerres(IEnumerable<Guerre> guerres)
    {
        _guerres.Clear();
        _guerres.AddRange(guerres.Select(g => g.Copier()).OrderBy(g => g.Id));
    }

    /// <summary>Guerre en cours entre deux communautés, ou null.</summary>
    public Guerre? GuerreEntre(int a, int b) =>
        _guerres.LastOrDefault(g => g.Issue == IssueGuerre.EnCours && ((g.A == a && g.B == b) || (g.A == b && g.B == a)));

    /// <summary>Les deux communautés ont juré de s'entraider contre un ennemi commun.</summary>
    public bool Pacte(int a, int b) => _pactes.ContainsKey(Paire(a, b));

    /// <summary>Hommes en état de se battre : de 16 à 50 ans, en assez bonne santé.</summary>
    public static bool EstGuerrier(Humain h, long maintenant)
    {
        if (!h.EstVivant || h.Sexe != Sexe.Homme || h.Sante < 50) return false;
        var age = h.AgeEnAnnees(maintenant);
        return age is >= 16 and < 50;
    }

    /// <summary>
    /// Ardeur d'un guerrier contre une communauté : colère, soif de vengeance contre ses membres, tempérament querelleur et
    /// peu prudent. La loyauté envers les siens pousse à se battre pour eux (les fondements liants vont avec l'hostilité
    /// envers les groupes rivaux ; Graham et al., 2009) ; la compassion retient le bras ; le psychopathe se bat sans haine,
    /// pour ce qu'il y gagne.
    /// </summary>
    private static double Ardeur(Humain h, int ennemie, IReadOnlyList<Humain> humains)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var vengeance = h.Griefs.Where(g => humains[g.ContreId - 1].CommunauteId == ennemie).Sum(g => g.Intensite) / 100;
        return h.Colere / 100 + Math.Min(1.5, vengeance) + (1 - p.Agreabilite) + 0.5 * (1 - p.Prudence)
               + 0.6 * (m.Fondements.Loyaute - 0.5) - 0.8 * (m.Compassion - 0.5) + 0.4 * Psyche.Froideur(p);
    }

    /// <summary>
    /// Soif de vengeance d'un groupe contre un autre (0 à 1) : les rancunes de ses adultes contre les membres de l'autre,
    /// surtout pour des meurtres.
    /// </summary>
    private static double Vengeance(List<Humain> adultes, int ennemie, IReadOnlyList<Humain> humains)
    {
        if (adultes.Count == 0) return 0;
        double somme = 0;
        foreach (var h in adultes)
        {
            foreach (var g in h.Griefs)
            {
                if (humains[g.ContreId - 1].CommunauteId != ennemie) continue;
                somme += g.Intensite / 100 * (g.Motif == MotifGrief.Meurtre ? 1 : 0.3);
            }
        }
        return Math.Min(1, somme / adultes.Count * 2);
    }

    /// <summary>
    /// Tempérament guerrier d'un groupe neuf : celui du groupe dont il est issu, sinon celui de ses adultes (peu
    /// bienveillants et colériques : plus prompts à la violence).
    /// </summary>
    private static double BellicismeInitial(Communaute c, List<Humain> adultes, IReadOnlyList<Communaute> communautes)
    {
        if (c.OrigineId > 0 && communautes[c.OrigineId - 1].Bellicisme >= 0) return communautes[c.OrigineId - 1].Bellicisme;
        if (adultes.Count == 0) return 0.3;
        return Math.Clamp(adultes.Average(h => 0.6 * (1 - h.Personnalite.Agreabilite) + 0.4 * h.Personnalite.Nevrosisme) - 0.2, 0, 1);
    }

    /// <summary>Le tempérament guerrier d'un groupe se forge avec ce qu'il vit.</summary>
    private static void Aguerrir(Communaute c, double ecart) => c.Bellicisme = Math.Clamp(c.Bellicisme + ecart, 0, 1);

    /// <summary>
    /// Une fois par semaine, après les relations : pactes, défaites et paix, tributs, puis raids.
    /// </summary>
    private void Guerroyer(List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> membres, long maintenant)
    {
        var adultes = actives.ToDictionary(c => c.Id, c => membres.TryGetValue(c.Id, out var g)
            ? g.Where(h => !h.EstEnfant(maintenant)).ToList() : []);
        var guerriers = adultes.ToDictionary(p => p.Key, p => p.Value.Where(h => EstGuerrier(h, maintenant)).ToList());
        Heriter(humains, maintenant);
        foreach (var c in actives.Where(c => c.Bellicisme < 0)) c.Bellicisme = BellicismeInitial(c, adultes[c.Id], Communautes);

        FinirGuerresSansAdversaire(humains, maintenant);
        Pactes(humains, actives, maintenant);
        Soumissions(humains, actives, adultes, guerriers, maintenant);
        IssuesDesGuerres(humains, actives, adultes, guerriers, maintenant);

        foreach (var a in actives)
        {
            // Un projet en cours se débat le jour venu (voir Debattre).
            if (a.Projet is not null) continue;
            if (a.Raid is not null || maintenant - a.DernierRaidLe < RepitEntreRaids || guerriers[a.Id].Count < 2 || a.FusionVersId != 0) continue;
            Communaute? cible = null;
            var meilleure = 0.0;
            foreach (var b in actives)
            {
                if (b.Id == a.Id || a.SoumiseAId == b.Id || b.SoumiseAId == a.Id) continue;
                var relation = RelationEntre(a.Id, b.Id);
                if (relation is not (EtatRelation.Hostiles or EtatRelation.EnGuerre or EtatRelation.Rivales)) continue;
                var distance = Math.Max(Math.Abs(a.CentreX - b.CentreX), Math.Abs(a.CentreY - b.CentreY));
                if (distance > DistanceRaidMax) continue;

                // Raisons de frapper : venger les siens, disputer des terres qui ne nourrissent plus tout le monde,
                // et le goût du groupe pour la violence.
                var vengeance = Vengeance(adultes[a.Id], b.Id, humains);
                var faim = a.Disette && distance <= a.Rayon + b.Rayon + 8 ? 0.5 : 0;
                var motivation = vengeance + faim + 0.4 * a.Bellicisme + (relation == EtatRelation.EnGuerre ? 0.2 : 0);
                // De simples rivaux ne se battent que poussés par la faim ou la vengeance.
                if (relation == EtatRelation.Rivales) motivation -= 0.4;
                if (motivation <= 0) continue;

                // L'avantage du nombre, alliés compris : on ne frappe que si l'on est nettement plus fort.
                var force = Forces(a, guerriers, actives, maintenant);
                var defense = Forces(b, guerriers, actives, maintenant);
                var avantage = Math.Clamp((force / Math.Max(1, defense) - 0.7) / 0.8, 0, 1.5);
                var figure = humains[a.FigureId - 1];
                var p = RaidParSemaine * motivation * avantage * (1.2 - figure.Personnalite.Agreabilite);
                if (p > meilleure) (meilleure, cible) = (p, b);
            }
            if (cible is null || alea.Reel() >= meilleure) continue;
            Projeter(a, cible, humains, guerriers, actives, maintenant);
        }
    }

    // ───────────── Projet de guerre ─────────────

    /// <summary>
    /// Délai avant le débat, de deux jours à quatre semaines : le temps de rassembler tout le monde et de parler, plus long
    /// dans un grand groupe ; une vengeance brûlante et un instigateur charismatique le pressent. Chez les peuples sans chef,
    /// les décisions se prennent en discutant jusqu'à ce qu'un avis l'emporte (Boehm, 1999) ; aucune étude ne mesure ce
    /// délai : ces valeurs sont des ordres de grandeur.
    /// </summary>
    internal long DelaiDebat(int adultes, bool vengeanceBrulante, Humain instigateur)
    {
        var jours = (3 + 0.4 * adultes) * (vengeanceBrulante ? 0.6 : 1) * (1.4 - 0.8 * instigateur.Personnalite.Charisme)
                    * Math.Exp(0.4 * Biologie.Normale(alea));
        return (long)(Math.Clamp(jours, 2, 28) * Calendrier.SecondesParJour);
    }

    /// <summary>Chaque jour : les groupes dont le jour du débat est venu débattent de leur projet de guerre.</summary>
    public void Debattre(List<Humain> humains, List<Communaute> communautes, long maintenant)
    {
        var dus = communautes.Where(c => c.Active && c.Projet is { } p && p.Debat <= maintenant).ToList();
        if (dus.Count == 0) return;
        var actives = communautes.Where(c => c.Active).ToList();
        var guerriers = actives.ToDictionary(c => c.Id, _ => new List<Humain>());
        foreach (var h in humains)
        {
            if (h.EstVivant && guerriers.TryGetValue(h.CommunauteId, out var liste) && EstGuerrier(h, maintenant)) liste.Add(h);
        }
        foreach (var a in dus)
        {
            if (a.Projet is not null && a.Active) Deliberer(a, humains, actives, guerriers, maintenant);
        }
    }

    /// <summary>
    /// Un guerrier ardent pousse son groupe à frapper : le projet est annoncé avec sa raison, sa cible et les alliés sollicités.
    /// </summary>
    private void Projeter(Communaute a, Communaute b, List<Humain> humains, Dictionary<int, List<Humain>> guerriers,
        List<Communaute> actives, long maintenant)
    {
        var instigateur = guerriers[a.Id].OrderByDescending(h => Ardeur(h, b.Id, humains)).ThenBy(h => h.Id).First();
        var relation = RelationEntre(a.Id, b.Id);
        var (na, nb) = (Minuscule(a.Nom), Minuscule(b.Nom));

        // La raison avancée : d'abord un mort à venger, puis la faim, puis la guerre déjà ouverte, enfin le goût de la violence.
        var meurtre = adultesDe(a).SelectMany(h => h.Griefs)
            .Where(g => g.Motif == MotifGrief.Meurtre && g.VictimeId > 0 && humains[g.ContreId - 1].CommunauteId == b.Id)
            .MaxBy(g => g.Intensite);
        string raison;
        long cause;
        if (meurtre is not null && Vengeance(adultesDe(a), b.Id, humains) > 0.3)
        {
            var victime = humains[meurtre.VictimeId - 1];
            raison = $"pour venger {victime.Nom}, tué{E(victime)} par {humains[meurtre.ContreId - 1].Nom}";
            cause = victime.MortEvenementId;
        }
        else if (a.Disette)
        {
            raison = "pour chasser l'ennemi des terres où la nourriture manque";
            cause = a.DisetteEvenementId;
        }
        else if (relation == EtatRelation.EnGuerre)
        {
            raison = "pour poursuivre la guerre";
            cause = GuerreEntre(a.Id, b.Id)?.EvenementId ?? 0;
        }
        else
        {
            raison = $"parce que la {nb} est une menace qu'il faut abattre";
            cause = _offenses.GetValueOrDefault(Paire(a.Id, b.Id));
        }
        if (cause == 0) cause = _debutsRelations.GetValueOrDefault(Paire(a.Id, b.Id));

        // Les alliés à portée qui n'ont rien à voir avec l'ennemi sont sollicités.
        var allies = actives.Where(c => c.Id != a.Id && c.Id != b.Id && c.Raid is null && c.SoumiseAId != b.Id
                && (Pacte(a.Id, c.Id) || RelationEntre(a.Id, c.Id) == EtatRelation.Alliees)
                && !Pacte(b.Id, c.Id) && RelationEntre(b.Id, c.Id) != EtatRelation.Alliees
                && Math.Max(Math.Abs(a.CentreX - c.CentreX), Math.Abs(a.CentreY - c.CentreY)) <= DistanceRaidMax
                && guerriers[c.Id].Count > 0)
            .Select(c => c.Id).ToList();
        var appel = allies.Count switch
        {
            0 => "",
            1 => $" Il compte sur la {Minuscule(Communautes[allies[0] - 1].Nom)} pour se joindre à eux.",
            _ => $" Il compte sur {string.Join(", ", allies.Take(allies.Count - 1).Select(id => "la " + Minuscule(Communautes[id - 1].Nom)))} et la {Minuscule(Communautes[allies[^1] - 1].Nom)} pour se joindre à eux.",
        };
        var nouveau = relation == EtatRelation.EnGuerre ? "un nouveau raid" : "la guerre";
        var id = Noter(maintenant, TypeEvenement.Guerre, instigateur, humains[b.FigureId - 1],
            $"{instigateur.Nom} pousse la {na} à préparer {nouveau} contre la {nb}, {raison}.{appel}",
            cause, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
        a.Projet = new ProjetGuerre
        {
            CibleId = b.Id, InstigateurId = instigateur.Id, Depuis = maintenant, EvenementId = id, Raison = raison, AlliesSollicites = allies,
            DebatLe = maintenant + DelaiDebat(adultesDe(a).Count, meurtre is not null && Vengeance(adultesDe(a), b.Id, humains) > 0.6, instigateur),
        };
        Jardin.Confier(instigateur, TypeIntime.Intention, b.FigureId, $"Frapper la {nb}, {raison}.", 50 + 25 * Math.Min(2, Ardeur(instigateur, b.Id, humains)),
            id, maintenant, $"guerre:{b.Id}");

        List<Humain> adultesDe(Communaute c) => humains.Where(h => h.EstVivant && h.CommunauteId == c.Id && !h.EstEnfant(maintenant)).ToList();
    }

    /// <summary>
    /// Le groupe débat du projet. Chaque guerrier pèse son ardeur, sa fidélité à l'instigateur, l'avis de la figure du
    /// groupe, le danger (les prudents comptent les forces de l'ennemi) et sa faim ; les alliés sollicités acceptent ou
    /// refusent. Le projet est abandonné si presque personne ne suit, si l'ennemi est trop fort, ou si la faim a ôté
    /// toute force au groupe ; sinon les volontaires partent. Chez les peuples sans chef, nul n'est tenu de suivre celui
    /// qui veut la guerre : chacun décide d'y aller ou non (Boehm, 1999 ; Glowacki et Wrangham, 2013).
    /// </summary>
    private void Deliberer(Communaute a, List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> guerriers, long maintenant)
    {
        var projet = a.Projet!;
        var na = Minuscule(a.Nom);
        var instigateur = humains[projet.InstigateurId - 1];
        var b = Heritiere(Communautes[projet.CibleId - 1]);
        if (b is not null && b.Id != projet.CibleId) projet.CibleId = b.Id;

        // Le projet n'a plus d'objet.
        string? caduc = null;
        if (b is null || b.Id == a.Id) caduc = $"la {Minuscule(Communautes[projet.CibleId - 1].Nom)} n'existe plus";
        else if (RelationEntre(a.Id, b.Id) is not (EtatRelation.Hostiles or EtatRelation.EnGuerre or EtatRelation.Rivales)) caduc = $"la {na} n'a plus de querelle avec la {Minuscule(b.Nom)}";
        else if (a.SoumiseAId == b.Id || b.SoumiseAId == a.Id) caduc = "l'un des deux groupes s'est soumis à l'autre";
        else if (!instigateur.EstVivant || instigateur.CommunauteId != a.Id) caduc = $"sans {instigateur.Nom}, personne ne le reprend";
        else if (a.Raid is not null) caduc = "les guerriers sont déjà partis en raid";
        if (caduc is not null)
        {
            Renoncer(a, humains, $"Le projet de guerre de la {na} est abandonné : {caduc}.", null, maintenant);
            return;
        }
        var nb = Minuscule(b!.Nom);
        var figure = humains[a.FigureId - 1];
        var defense = Forces(b, guerriers, actives, maintenant);
        var force = Forces(a, guerriers, actives, maintenant);
        var danger = Math.Clamp(defense / Math.Max(1, force), 0, 2);

        // L'avis de chacun.
        // Des terres qui ne nourrissent plus le groupe poussent chacun à les disputer ; une faim extrême ôte l'envie de marcher au combat.
        var terres = a.Disette ? 0.3 : 0;
        // L'instigateur persuade d'autant mieux qu'il a du charisme et sait ce que chacun veut entendre (empathie cognitive).
        var persuasion = 0.3 * (instigateur.Personnalite.Charisme - 0.5) + 0.2 * (instigateur.Personnalite.Moral.EmpathieCognitive - 0.5);
        double Avis(Humain h) => Ardeur(h, b.Id, humains) + terres + 0.5 * h.AffectionPour(instigateur.Id) / 100 + persuasion
            - h.Personnalite.Prudence * danger - 0.5 * Math.Max(0, h.Faim - 60) / 40;
        const double Suivre = 0.6;
        // La figure du groupe donne son avis, et beaucoup s'y rangent.
        var figureContre = figure.Id != instigateur.Id && Avis(figure) < Suivre - 0.25;
        var figurePour = figure.Id == instigateur.Id || (!figureContre && Avis(figure) >= Suivre);
        // La figure n'a d'autorité que sur ceux qui l'estiment, et davantage sur ceux qui respectent l'autorité.
        double Influence(Humain h) => (figureContre ? -0.3 : figurePour && h.AffectionPour(figure.Id) > 0 ? 0.2 : 0)
            * (0.5 + h.Personnalite.Moral.Fondements.Autorite);
        var avis = guerriers[a.Id].Select(h => (h, Score: h.Id == instigateur.Id ? 10 : Avis(h) + Influence(h)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.h.Id).ToList();
        var volontaires = avis.Where(x => x.Score >= Suivre).Select(x => x.h).ToList();
        var refus = avis.Where(x => x.Score < Suivre).Select(x => x.h).ToList();
        foreach (var h in volontaires.Where(h => h.Id != instigateur.Id))
            Jardin.Confier(h, TypeIntime.Intention, b.FigureId, $"Je suivrai {instigateur.Nom} contre la {nb}.", 40, projet.EvenementId, maintenant, $"guerre:{b.Id}");
        foreach (var h in refus)
            Jardin.Confier(h, TypeIntime.Intention, instigateur.Id, $"Je ne suivrai pas {instigateur.Nom} dans cette guerre contre la {nb}.", 35, projet.EvenementId, maintenant, $"guerre:{b.Id}");

        // Les alliés répondent.
        var renforts = new List<Humain>();
        foreach (var id in projet.AlliesSollicites)
        {
            var allie = Heritiere(Communautes[id - 1]);
            if (allie is null || allie.Id == a.Id || allie.Id == b.Id || allie.Raid is not null || !guerriers.TryGetValue(allie.Id, out var siens) || siens.Count == 0) continue;
            var fa = humains[allie.FigureId - 1];
            var vengeance = Vengeance(siens, b.Id, humains);
            // Un pacte engage d'autant plus que la figure de l'allié est loyale.
            var p = (Pacte(a.Id, allie.Id) ? 0.55 + 0.4 * (fa.Personnalite.Moral.Fondements.Loyaute - 0.5) : 0.25) + 0.5 * vengeance
                    + 0.3 * allie.Bellicisme - 0.3 * fa.Personnalite.Agreabilite
                    - (RelationEntre(allie.Id, b.Id) == EtatRelation.Indifferentes ? 0.15 : 0) - (allie.Disette ? 0.2 : 0);
            var nallie = Minuscule(allie.Nom);
            if (alea.Reel() < p)
            {
                var envoyes = siens.OrderByDescending(h => Ardeur(h, b.Id, humains)).ThenBy(h => h.Id).Take(2).ToList();
                renforts.AddRange(envoyes);
                Noter(maintenant, TypeEvenement.Guerre, fa, instigateur,
                    $"{fa.Nom} accepte : la {nallie} enverra {(envoyes.Count > 1 ? "des guerriers" : "un guerrier")} avec la {na} contre la {nb}.",
                    projet.EvenementId, communaute: allie.Id, autreCommunaute: a.Id, significatif: true);
            }
            else
            {
                var pourquoi = allie.Disette ? "les siens ont trop faim pour se battre"
                    : RelationEntre(allie.Id, b.Id) == EtatRelation.Indifferentes ? $"la {nallie} n'a rien contre la {nb}"
                    : fa.Personnalite.Agreabilite > 0.6 ? "il ne veut pas faire couler le sang" : "ce combat n'est pas le sien";
                Noter(maintenant, TypeEvenement.Guerre, fa, instigateur,
                    $"{fa.Nom} refuse de suivre la {na} contre la {nb} : {pourquoi}.",
                    projet.EvenementId, communaute: allie.Id, autreCommunaute: a.Id, significatif: true);
            }
        }

        // La décision.
        var troupe = volontaires.Take(Math.Clamp((int)Math.Ceiling(guerriers[a.Id].Count * 0.6), 2, 10)).ToList();
        var forceTroupe = troupe.Concat(renforts).Sum(h => h.Sante / 100);
        var defenseurs = guerriers[b.Id].Sum(h => h.Sante / 100);
        var faimMoyenne = guerriers[a.Id].Average(h => h.Faim);
        string? renoncement = null;
        if (volontaires.Count < 2)
        {
            renoncement = figureContre
                ? $"{figure.Nom} s'y oppose et presque personne ne suit {instigateur.Nom}"
                : $"presque personne ne suit {instigateur.Nom}";
        }
        else if (forceTroupe < 0.9 * defenseurs)
        {
            renoncement = $"la {nb} est trop forte : {troupe.Count + renforts.Count} hommes seulement contre {guerriers[b.Id].Count}";
        }
        else if (faimMoyenne >= 65)
        {
            renoncement = "la faim a ôté toute force aux guerriers";
        }
        if (renoncement is not null)
        {
            Renoncer(a, humains, $"La {na} renonce à la guerre contre la {nb} : {renoncement}.", instigateur, maintenant);
            return;
        }

        a.Projet = null;
        LancerRaid(a, b, humains, [.. troupe, .. renforts], projet.Raison, projet.EvenementId, maintenant);
    }

    /// <summary>Le projet est abandonné : c'est noté, et l'instigateur le garde sur le cœur.</summary>
    private void Renoncer(Communaute a, List<Humain> humains, string texte, Humain? instigateur, long maintenant)
    {
        var projet = a.Projet!;
        var id = Noter(maintenant, TypeEvenement.Guerre, instigateur ?? humains[a.FigureId - 1], null, texte, projet.EvenementId,
            communaute: a.Id, autreCommunaute: projet.CibleId, significatif: true);
        var cle = $"guerre:{projet.CibleId}";
        foreach (var h in humains.Where(h => h.EstVivant && h.CommunauteId == a.Id))
        {
            // Ceux qui voulaient y aller oublient leur intention ; ceux qui ont refusé gardent la leur en mémoire.
            if (h.Id == projet.InstigateurId || h.Memoire.Jardin.Any(x => x.Cle == cle && x.Texte.StartsWith("Je suivrai", StringComparison.Ordinal))) Jardin.Retirer(h, cle);
        }
        if (instigateur is { EstVivant: true })
        {
            Jardin.Confier(instigateur, TypeIntime.Opinion, instigateur.Id, "Les miens ne m'ont pas suivi. Ils le regretteront.",
                30 + 40 * (1 - instigateur.Personnalite.Agreabilite), id, maintenant, $"renoncement:{projet.CibleId}");
        }
        a.Projet = null;
        a.DernierRaidLe = maintenant;
    }

    /// <summary>Guerriers d'un groupe, plus la moitié de ceux de ses alliés jurés qui vivent assez près.</summary>
    private double Forces(Communaute c, Dictionary<int, List<Humain>> guerriers, List<Communaute> actives, long maintenant)
    {
        double force = guerriers[c.Id].Sum(h => h.Sante / 100);
        foreach (var allie in actives)
        {
            if (allie.Id == c.Id || !Pacte(c.Id, allie.Id)) continue;
            if (Math.Max(Math.Abs(c.CentreX - allie.CentreX), Math.Abs(c.CentreY - allie.CentreY)) > DistanceRaidMax) continue;
            force += 0.5 * guerriers[allie.Id].Sum(h => h.Sante / 100);
        }
        return force;
    }

    /// <summary>La troupe part : chef en tête, vers le camp ennemi.</summary>
    private void LancerRaid(Communaute a, Communaute b, List<Humain> humains, List<Humain> troupe, string raison, long cause, long maintenant)
    {
        var chef = troupe[0];
        var noms = string.Join(", ", troupe.Skip(1).Take(4).Select(h => h.Nom)) + (troupe.Count > 5 ? $" et {troupe.Count - 5} autres" : "");
        var id = Noter(maintenant, TypeEvenement.Guerre, chef, humains[b.FigureId - 1],
            $"{chef.Nom} entraîne {(troupe.Count == 2 ? "un homme" : $"{troupe.Count - 1} hommes")} ({noms}) vers le camp de la {Minuscule(b.Nom)}, "
            + $"à {Lieux.Distance(a.CentreX, a.CentreY, b.CentreX, b.CentreY)} {Lieux.Vers(a.CentreX, a.CentreY, b.CentreX, b.CentreY) switch { var v when v.StartsWith("l'", StringComparison.Ordinal) => "à " + v, var v => "au " + v[3..] }}, {raison}.",
            cause, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
        a.Raid = new Raid
        {
            CibleId = b.Id, X = b.CentreX, Y = b.CentreY, RetourX = a.CentreX, RetourY = a.CentreY, Depuis = maintenant, EvenementId = id,
            Guerriers = [.. troupe.Select(h => h.Id)],
        };
        a.DernierRaidLe = maintenant;
    }

    /// <summary>
    /// Des groupes alliés qui ont un ennemi commun jurent de s'entraider : chacun vient au secours de l'autre et ses
    /// guerriers se joignent à ses raids. Le pacte se rompt quand l'amitié s'éteint.
    /// </summary>
    private void Pactes(List<Humain> humains, List<Communaute> actives, long maintenant)
    {
        foreach (var cle in _pactes.Keys.ToList())
        {
            if (RelationEntre(cle.A, cle.B) == EtatRelation.Alliees && Communautes[cle.A - 1].Active && Communautes[cle.B - 1].Active) continue;
            var (a, b) = (Communautes[cle.A - 1], Communautes[cle.B - 1]);
            if (a.Active && b.Active)
            {
                Noter(maintenant, TypeEvenement.Guerre, humains[a.FigureId - 1], humains[b.FigureId - 1],
                    $"Le pacte entre la {Minuscule(a.Nom)} et la {Minuscule(b.Nom)} est rompu : l'amitié n'y est plus.", _pactes[cle],
                    communaute: a.Id, autreCommunaute: b.Id, significatif: true);
            }
            _pactes.Remove(cle);
        }

        for (var i = 0; i < actives.Count; i++)
        {
            for (var j = i + 1; j < actives.Count; j++)
            {
                var (a, b) = (actives[i], actives[j]);
                if (RelationEntre(a.Id, b.Id) != EtatRelation.Alliees || Pacte(a.Id, b.Id)) continue;
                var ennemie = actives.FirstOrDefault(k => k.Id != a.Id && k.Id != b.Id
                    && RelationEntre(a.Id, k.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre
                    && RelationEntre(b.Id, k.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre);
                if (ennemie is null) continue;
                var (fa, fb) = (humains[a.FigureId - 1], humains[b.FigureId - 1]);
                var accord = (fa.Personnalite.Agreabilite + fb.Personnalite.Agreabilite) / 2;
                if (alea.Reel() >= 0.15 * (0.5 + accord)) continue;
                var cause = GuerreEntre(a.Id, ennemie.Id)?.EvenementId ?? GuerreEntre(b.Id, ennemie.Id)?.EvenementId
                    ?? _offenses.GetValueOrDefault(Paire(a.Id, ennemie.Id));
                _pactes[Paire(a.Id, b.Id)] = Noter(maintenant, TypeEvenement.Guerre, fa, fb,
                    $"{fa.Nom} et {fb.Nom} scellent un pacte : la {Minuscule(a.Nom)} et la {Minuscule(b.Nom)} feront front ensemble contre la {Minuscule(ennemie.Nom)}.",
                    cause, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
            }
        }
    }

    /// <summary>Communauté active qui continue celle-ci (elle-même si elle est active), ou null.</summary>
    public Communaute? Heritiere(Communaute c)
    {
        for (var etapes = 0; !c.Active && c.HeritiereId != 0 && etapes < 50; etapes++) c = Communautes[c.HeritiereId - 1];
        return c.Active ? c : null;
    }

    /// <summary>
    /// Un groupe qui se recompose sous un autre nom garde ses querelles : la communauté qui le continue hérite de ses guerres,
    /// de sa soumission et de son goût pour la violence.
    /// </summary>
    private void Heriter(List<Humain> humains, long maintenant)
    {
        foreach (var g in _guerres.Where(g => g.Issue == IssueGuerre.EnCours))
        {
            var (a, b) = (Communautes[g.A - 1], Communautes[g.B - 1]);
            if (a.Active && b.Active) continue;
            var (ha, hb) = (Heritiere(a), Heritiere(b));
            if (ha is null || hb is null || ha.Id == hb.Id) continue;
            if (GuerreEntre(ha.Id, hb.Id) is { } deja && deja != g) continue;
            foreach (var (avant, apres, ennemie) in new[] { (a, ha, hb), (b, hb, ha) })
            {
                if (avant == apres) continue;
                Noter(maintenant, TypeEvenement.Guerre, humains[apres.FigureId - 1], humains[ennemie.FigureId - 1],
                    $"Les survivants de la {Minuscule(avant.Nom)}, désormais la {Minuscule(apres.Nom)}, poursuivent la guerre contre la {Minuscule(ennemie.Nom)}.",
                    g.EvenementId, communaute: apres.Id, autreCommunaute: ennemie.Id, significatif: true);
            }
            var offense = _offenses.GetValueOrDefault(Paire(g.A, g.B), g.EvenementId);
            (g.A, g.B) = (ha.Id, hb.Id);
            var cle = Paire(ha.Id, hb.Id);
            _relations[cle] = EtatRelation.EnGuerre;
            _debutsRelations[cle] = g.EvenementId;
            _offenses[cle] = offense;
        }

        foreach (var c in Communautes)
        {
            if (c.Active) continue;
            // Le projet de guerre d'un groupe dispersé passe à celui qui le continue, s'il n'en a pas déjà un.
            if (c.Projet is { } projet && Heritiere(c) is { Projet: null } suite && Heritiere(Communautes[projet.CibleId - 1])?.Id != suite.Id)
            {
                suite.Projet = projet;
            }
            c.Projet = null;
            if (c.HeritiereId == 0 || Heritiere(c) is not { } h) continue;
            if (h.Bellicisme < 0 && c.Bellicisme >= 0) h.Bellicisme = c.Bellicisme;
            if (c.SoumiseAId != 0 && h.SoumiseAId == 0 && Heritiere(Communautes[c.SoumiseAId - 1]) is { } maitre && maitre.Id != h.Id)
            {
                (h.SoumiseAId, h.SoumiseLe, h.SoumissionEvenementId) = (maitre.Id, c.SoumiseLe, c.SoumissionEvenementId);
            }
            c.SoumiseAId = 0;
        }
        // Le maître recomposé garde ses vassaux.
        foreach (var c in Communautes)
        {
            if (!c.Active || c.SoumiseAId == 0 || Communautes[c.SoumiseAId - 1].Active) continue;
            if (Heritiere(Communautes[c.SoumiseAId - 1]) is { } maitre && maitre.Id != c.Id) c.SoumiseAId = maitre.Id;
        }
    }

    /// <summary>Une guerre dont l'un des groupes n'existe plus, sans personne pour la continuer, est finie.</summary>
    private void FinirGuerresSansAdversaire(List<Humain> humains, long maintenant)
    {
        foreach (var g in _guerres.Where(g => g.Issue == IssueGuerre.EnCours))
        {
            var (a, b) = (Communautes[g.A - 1], Communautes[g.B - 1]);
            if (a.Active && b.Active) continue;
            var (reste, disparue) = a.Active ? (a, b) : (b, a);
            g.Issue = IssueGuerre.Extinction;
            g.Fin = maintenant;
            g.VainqueurId = reste.Active ? reste.Id : 0;
            if (reste.Active)
            {
                g.FinEvenementId = Noter(maintenant, TypeEvenement.Guerre, humains[reste.FigureId - 1], null,
                    disparue.FusionneeDansId == reste.Id
                        ? $"La guerre entre la {Minuscule(a.Nom)} et la {Minuscule(b.Nom)} s'achève : les survivants de la {Minuscule(disparue.Nom)} vivent désormais parmi leurs ennemis."
                        : $"La guerre entre la {Minuscule(a.Nom)} et la {Minuscule(b.Nom)} s'achève avec la dispersion de la {Minuscule(disparue.Nom)} ; les rancunes demeurent.",
                    g.EvenementId, communaute: reste.Id, autreCommunaute: disparue.Id, significatif: true);
            }
        }
    }

    /// <summary>
    /// Une guerre s'achève. Le vaincu, très affaibli, se rend : trop peu nombreux, ses survivants rejoignent le vainqueur ;
    /// sinon sa figure choisit de se soumettre (prudente ou conciliante) ou de fuir au loin (fière). Sans vaincu, la paix vient
    /// quand le sang ne coule plus depuis des mois et que les figures y sont prêtes, d'autant plus vite que les pertes sont lourdes.
    /// </summary>
    private void IssuesDesGuerres(List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> adultes,
        Dictionary<int, List<Humain>> guerriers, long maintenant)
    {
        foreach (var g in _guerres.Where(g => g.Issue == IssueGuerre.EnCours).ToList())
        {
            var (a, b) = (Communautes[g.A - 1], Communautes[g.B - 1]);
            if (!a.Active || !b.Active) continue;
            var (fa, fb) = (humains[a.FigureId - 1], humains[b.FigureId - 1]);
            var forceA = Forces(a, guerriers, actives, maintenant);
            var forceB = Forces(b, guerriers, actives, maintenant);

            // Un vaincu : bien moins fort que l'autre, et il a perdu des siens dans cette guerre.
            var (vaincu, vainqueur, pertes) = forceA < forceB ? (a, b, g.MortsA) : (b, a, g.MortsB);
            var rapport = Math.Min(forceA, forceB) / Math.Max(1, Math.Max(forceA, forceB));
            if (pertes >= 2 && rapport < 0.4 && alea.Reel() < 0.25)
            {
                Rendre(g, vaincu, vainqueur, humains, adultes[vaincu.Id], maintenant);
                continue;
            }

            if (maintenant - g.DernierSangLe < CalmeAvantPaix || a.Raid?.CibleId == b.Id || b.Raid?.CibleId == a.Id) continue;
            var lassitude = Math.Min(1, (g.MortsA + g.MortsB) / 4.0);
            var vengeance = Math.Max(Vengeance(adultes[a.Id], b.Id, humains), Vengeance(adultes[b.Id], a.Id, humains));
            var p = PaixParSemaine * (fa.Personnalite.Agreabilite + fb.Personnalite.Agreabilite) * (0.5 + lassitude) * (1 - 0.7 * vengeance);
            if (alea.Reel() >= p) continue;

            g.Issue = IssueGuerre.Paix;
            g.Fin = maintenant;
            g.FinEvenementId = Noter(maintenant, TypeEvenement.Guerre, fa, fb,
                $"{fa.Nom} et {fb.Nom} se rencontrent et font la paix : la guerre entre la {Minuscule(a.Nom)} et la {Minuscule(b.Nom)} "
                + $"s'achève ({Pertes(g)}). Pourquoi : {RaisonsPaix(g, fa, fb, lassitude, vengeance, maintenant)}.",
                g.EvenementId, communaute: a.Id, autreCommunaute: b.Id, significatif: true);
            FaireLaPaix(a, b, humains, g.FinEvenementId);
        }
    }

    /// <summary>Ce qui a rendu la paix possible : le temps sans combat, la lassitude des deuils, des figures conciliantes, des rancunes apaisées.</summary>
    private static string RaisonsPaix(Guerre g, Humain fa, Humain fb, double lassitude, double vengeance, long maintenant)
    {
        var raisons = new List<string>();
        var mois = (int)((maintenant - g.DernierSangLe) / (30 * Calendrier.SecondesParJour));
        raisons.Add(g.MortsA + g.MortsB == 0 ? $"aucun combat sanglant en {mois} mois de guerre" : $"aucun sang n'a coulé depuis {mois} mois");
        if (lassitude >= 0.5) raisons.Add($"les deux camps sont las de pleurer leurs morts ({Pertes(g)})");
        var conciliant = fa.Personnalite.Agreabilite >= fb.Personnalite.Agreabilite ? fa : fb;
        if (conciliant.Personnalite.Agreabilite >= 0.6)
            raisons.Add($"{conciliant.Nom} est d'un naturel conciliant{(Math.Min(fa.Personnalite.Agreabilite, fb.Personnalite.Agreabilite) >= 0.6 ? $", comme {(conciliant == fa ? fb : fa).Nom}" : "")}");
        raisons.Add(vengeance < 0.2 ? "la soif de vengeance s'est apaisée" : "malgré la rancune de certains, qui n'oublient pas leurs morts");
        return string.Join(", ", raisons.Take(raisons.Count - 1)) + (raisons.Count > 1 ? " et " : "") + raisons[^1];
    }

    private static string Pertes(Guerre g) => (g.MortsA + g.MortsB) switch
    {
        0 => "sans mort",
        1 => "un mort",
        var n => $"{n} morts",
    };

    /// <summary>La paix apaise les rancunes contre l'autre groupe et rend les deux groupes moins belliqueux.</summary>
    private void FaireLaPaix(Communaute a, Communaute b, List<Humain> humains, long evenement)
    {
        _relations.Remove(Paire(a.Id, b.Id));
        _debutsRelations.Remove(Paire(a.Id, b.Id));
        _offenses.Remove(Paire(a.Id, b.Id));
        foreach (var h in humains)
        {
            if (!h.EstVivant || (h.CommunauteId != a.Id && h.CommunauteId != b.Id)) continue;
            var autre = h.CommunauteId == a.Id ? b.Id : a.Id;
            for (var k = 0; k < h.Griefs.Count; k++)
            {
                var gr = h.Griefs[k];
                if (humains[gr.ContreId - 1].CommunauteId == autre) h.Griefs[k] = gr with { Intensite = gr.Intensite * 0.5 };
            }
            for (var k = 0; k < h.Liens.Count; k++)
            {
                var l = h.Liens[k];
                if (humains[l.Id - 1].CommunauteId == autre && l.Affection < 0) h.Liens[k] = l with { Affection = l.Affection * 0.5 };
            }
        }
        Aguerrir(a, -0.05);
        Aguerrir(b, -0.05);
        a.PaixEvenementId = b.PaixEvenementId = evenement;
    }

    private void Rendre(Guerre g, Communaute vaincu, Communaute vainqueur, List<Humain> humains, List<Humain> adultes, long maintenant)
    {
        var figure = humains[vaincu.FigureId - 1];
        var autre = humains[vainqueur.FigureId - 1];
        var (nv, nw) = (Minuscule(vaincu.Nom), Minuscule(vainqueur.Nom));
        g.Fin = maintenant;
        g.VainqueurId = vainqueur.Id;
        if (adultes.Count <= 3)
        {
            g.Issue = IssueGuerre.Absorption;
            g.FinEvenementId = Noter(maintenant, TypeEvenement.Guerre, figure, autre,
                $"Trop peu nombreux pour survivre seuls, les derniers de la {nv} vont vivre parmi leurs vainqueurs, la {nw} ({Pertes(g)}).",
                g.EvenementId, communaute: vaincu.Id, autreCommunaute: vainqueur.Id, significatif: true);
            FaireLaPaix(vaincu, vainqueur, humains, g.FinEvenementId);
            vaincu.FusionVersId = vainqueur.Id;
            vaincu.FusionDecideeLe = maintenant;
            vaincu.FusionEvenementId = g.FinEvenementId;
            (vaincu.DestinationX, vaincu.DestinationY) = (vainqueur.CentreX, vainqueur.CentreY);
            return;
        }
        var p = figure.Personnalite;
        if (p.Prudence + p.Agreabilite > 1 + alea.Reel() * 0.4)
        {
            g.Issue = IssueGuerre.Soumission;
            vaincu.SoumiseAId = vainqueur.Id;
            vaincu.SoumiseLe = maintenant;
            g.FinEvenementId = vaincu.SoumissionEvenementId = Noter(maintenant, TypeEvenement.Guerre, figure, autre,
                $"Vaincue, la {nv} se soumet à la {nw} : {figure.Nom} promet à {autre.Nom} une part de ce que les siens chassent et cueillent ({Pertes(g)}).",
                g.EvenementId, communaute: vaincu.Id, autreCommunaute: vainqueur.Id, significatif: true);
            FaireLaPaix(vaincu, vainqueur, humains, g.FinEvenementId);
            Aguerrir(vainqueur, 0.05);
            return;
        }
        // Fuir loin du vainqueur : au moins 25 km dans la direction opposée, sur des terres praticables.
        g.Issue = IssueGuerre.Fuite;
        var dx = vaincu.CentreX - vainqueur.CentreX;
        var dy = vaincu.CentreY - vainqueur.CentreY;
        var longueur = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var loin = Echelle.Cases(25);
        var cx = Math.Clamp(vaincu.CentreX + (int)Math.Round(dx / longueur * loin), 0, Carte!.Largeur - 1);
        var cy = Math.Clamp(vaincu.CentreY + (int)Math.Round(dy / longueur * loin), 0, Carte.Hauteur - 1);
        (vaincu.DestinationX, vaincu.DestinationY) = Climat.EstPraticable(Carte.BiomeEn(cx, cy)) ? (cx, cy) : (-1, -1);
        g.FinEvenementId = Noter(maintenant, TypeEvenement.Guerre, figure, autre,
            vaincu.DestinationX >= 0
                ? $"Plutôt que de se soumettre à la {nw}, {figure.Nom} emmène les siens loin d'elle, vers {Lieux.Vers(vainqueur.CentreX, vainqueur.CentreY, cx, cy)} ({Pertes(g)})."
                : $"Plutôt que de se soumettre à la {nw}, la {nv} se retire et cesse le combat ({Pertes(g)}).",
            g.EvenementId, communaute: vaincu.Id, autreCommunaute: vainqueur.Id, significatif: true);
        // L'inimitié demeure ; la guerre cesse faute de combattants.
        _relations[Paire(vaincu.Id, vainqueur.Id)] = EtatRelation.Hostiles;
        Aguerrir(vaincu, 0.05);
    }

    /// <summary>
    /// Chaque semaine, un groupe soumis livre une part de sa nourriture à son maître. Il reprend sa liberté quand il est
    /// redevenu aussi fort, ou quand son maître n'existe plus.
    /// </summary>
    private void Soumissions(List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> adultes,
        Dictionary<int, List<Humain>> guerriers, long maintenant)
    {
        foreach (var c in actives.Where(c => c.SoumiseAId != 0))
        {
            var maitre = Communautes[c.SoumiseAId - 1];
            var figure = humains[c.FigureId - 1];
            if (!maitre.Active)
            {
                Noter(maintenant, TypeEvenement.Guerre, figure, null,
                    $"La {Minuscule(c.Nom)} ne doit plus rien à personne : la {Minuscule(maitre.Nom)} n'existe plus.", c.SoumissionEvenementId,
                    communaute: c.Id, autreCommunaute: maitre.Id, significatif: true);
                c.SoumiseAId = 0;
                continue;
            }
            var force = Forces(c, guerriers, actives, maintenant);
            var forceMaitre = Forces(maitre, guerriers, actives, maintenant);
            if (force >= 0.9 * forceMaitre && maintenant - c.SoumiseLe > 180 * Calendrier.SecondesParJour
                && alea.Reel() < 0.1 * (1.2 - figure.Personnalite.Agreabilite))
            {
                var id = Noter(maintenant, TypeEvenement.Guerre, figure, humains[maitre.FigureId - 1],
                    $"Redevenue forte, la {Minuscule(c.Nom)} cesse de livrer sa nourriture à la {Minuscule(maitre.Nom)} : {figure.Nom} refuse de s'incliner plus longtemps.",
                    c.SoumissionEvenementId, communaute: c.Id, autreCommunaute: maitre.Id, significatif: true);
                c.SoumiseAId = 0;
                _relations[Paire(c.Id, maitre.Id)] = EtatRelation.Hostiles;
                _debutsRelations[Paire(c.Id, maitre.Id)] = id;
                _offenses[Paire(c.Id, maitre.Id)] = id;
                continue;
            }
            // Le tribut : environ 5 % des réserves de chaque adulte soumis, partagés entre les adultes du maître.
            if (!adultes.TryGetValue(maitre.Id, out var receveurs) || receveurs.Count == 0) continue;
            double tribut = 0;
            foreach (var h in adultes[c.Id])
            {
                var part = Math.Min(2, h.Reserves * 0.05);
                h.Reserves -= part;
                tribut += part;
            }
            foreach (var h in receveurs) h.Reserves = Math.Min(100, h.Reserves + tribut / receveurs.Count);
        }
    }

    /// <summary>Une communauté déclare la guerre à une autre (premier sang) ou la poursuit ; la guerre en cours est renvoyée.</summary>
    internal Guerre Ensanglanter(Communaute assaillant, Communaute cible, int mortsAssaillant, int mortsCible, long evenement, long maintenant)
    {
        var g = GuerreEntre(assaillant.Id, cible.Id);
        if (g is null)
        {
            g = new Guerre { Id = _guerres.Count + 1, A = assaillant.Id, B = cible.Id, Debut = maintenant, EvenementId = evenement };
            _guerres.Add(g);
        }
        g.Raids++;
        if (mortsAssaillant + mortsCible > 0) g.DernierSangLe = maintenant;
        if (g.A == assaillant.Id)
        {
            g.MortsA += mortsAssaillant;
            g.MortsB += mortsCible;
        }
        else
        {
            g.MortsB += mortsAssaillant;
            g.MortsA += mortsCible;
        }
        var cle = Paire(assaillant.Id, cible.Id);
        if (_relations.GetValueOrDefault(cle) != EtatRelation.EnGuerre)
        {
            _relations[cle] = EtatRelation.EnGuerre;
            _debutsRelations[cle] = evenement;
        }
        _offenses[cle] = evenement;
        // Le sang versé rend les victimes avides de vengeance ; un raid heureux enhardit, un raid coûteux refroidit.
        if (mortsCible > 0) Aguerrir(cible, 0.06);
        Aguerrir(assaillant, mortsAssaillant > mortsCible ? -0.04 : mortsCible > 0 ? 0.03 : 0);
        return g;
    }

    /// <summary>Carte du monde, pour choisir où fuir (fournie par le monde).</summary>
    public Monde.Carte? Carte { get; set; }
}

/// <summary>Raids : la troupe marche vers le camp ennemi, attaque à l'aube, puis rentre.</summary>
public sealed partial class Comportement
{
    private readonly Dictionary<int, (Communaute Groupe, Raid Raid)> _raids = [];

    private static string Minuscule(string nom) => char.ToLowerInvariant(nom[0]) + nom[1..];

    /// <summary>
    /// Chaque heure : suivre le camp visé, attaquer à l'aube quand la troupe est arrivée, renoncer après six jours,
    /// et clore le raid quand tous sont rentrés.
    /// </summary>
    public void Raids(List<Humain> humains, long maintenant)
    {
        _raids.Clear();
        if (Social is null) return;
        var heure = Calendrier.Decomposer(maintenant).Heure;
        foreach (var c in Social.Communautes)
        {
            if (c.Raid is not { } raid) continue;
            raid.Guerriers.RemoveAll(id => !humains[id - 1].EstVivant);
            var cible = Social.Communautes[raid.CibleId - 1];
            // L'ennemi s'est recomposé sous un autre nom : c'est lui qu'on vise.
            if (!cible.Active && Social.Heritiere(cible) is { } suite && suite.Id != c.Id)
            {
                raid.CibleId = suite.Id;
                cible = suite;
            }
            if (raid.Guerriers.Count == 0 || !c.Active)
            {
                c.Raid = null;
                continue;
            }
            if (raid.Retour)
            {
                // Chacun rentre ; le raid se clôt quand tous sont près du camp, ou au bout de quatre jours.
                raid.Guerriers.RemoveAll(id => Distance(humains[id - 1].X, humains[id - 1].Y, raid.RetourX, raid.RetourY) <= 3);
                if (raid.Guerriers.Count == 0 || maintenant - raid.RetourDepuis > 4 * Calendrier.SecondesParJour)
                {
                    c.Raid = null;
                    continue;
                }
            }
            else if (!cible.Active)
            {
                Rentrer(c, raid, maintenant);
            }
            else
            {
                // Le camp visé : là où vit le membre le plus proche du centre du groupe ennemi.
                var camp = humains.Where(h => h.EstVivant && h.CommunauteId == cible.Id && !h.EstEnfant(maintenant))
                    .MinBy(h => (Distance(h.X, h.Y, cible.CentreX, cible.CentreY), h.Id));
                if (camp is not null) (raid.X, raid.Y) = (camp.X, camp.Y);
                var arrives = raid.Guerriers.Count(id => Distance(humains[id - 1].X, humains[id - 1].Y, raid.X, raid.Y) <= 3);
                if (arrives >= Math.Max(2, (int)Math.Ceiling(raid.Guerriers.Count * 0.6)) && heure is >= 4 and <= 6)
                {
                    Assaut(c, cible, raid, humains, maintenant);
                }
                else if (maintenant - raid.Depuis > 6 * Calendrier.SecondesParJour)
                {
                    Noter(maintenant, TypeEvenement.Guerre, humains[raid.Guerriers[0] - 1], null,
                        $"Les guerriers de la {Minuscule(c.Nom)} n'ont pas pu surprendre la {Minuscule(cible.Nom)} : ils rentrent.", raid.EvenementId);
                    Rentrer(c, raid, maintenant);
                }
            }
            foreach (var id in raid.Guerriers) _raids[id] = (c, raid);
        }
    }

    private static void Rentrer(Communaute c, Raid raid, long maintenant)
    {
        raid.Retour = true;
        raid.RetourDepuis = maintenant;
        // Le camp a pu bouger pendant le raid : on rentre là où vit le groupe.
        if (c.CentreX > 0 || c.CentreY > 0) (raid.RetourX, raid.RetourY) = (c.CentreX, c.CentreY);
    }

    /// <summary>
    /// Un guerrier en raid marche vers le camp ennemi (ou rentre), à moins qu'un besoin pressant ne l'arrête.
    /// Rend vrai si cela occupe son pas.
    /// </summary>
    private bool MenerRaid(Humain h)
    {
        if (!_raids.TryGetValue(h.Id, out var r)) return false;
        var raid = r.Raid;
        if (raid.Retour)
        {
            Avancer(h, raid.RetourX, raid.RetourY, Activite.RentrerDuRaid);
            return h.CibleX >= 0;
        }
        _autre = null;
        if (Distance(h.X, h.Y, raid.X, raid.Y) <= 2)
        {
            // Arrivé : on se cache et on attend l'aube.
            h.Activite = Activite.Raid;
            h.CibleX = h.CibleY = -1;
            return true;
        }
        Avancer(h, raid.X, raid.Y, Activite.Raid);
        return h.CibleX >= 0;
    }

    /// <summary>Ce qu'ont en main les assaillants, en mots (« armés de branches et de pierres »).</summary>
    private static string Armement(IEnumerable<Arme> armes)
    {
        var liste = armes.ToList();
        var mots = new List<string>();
        if (liste.Contains(Arme.Baton)) mots.Add("de branches");
        if (liste.Contains(Arme.Pierre)) mots.Add("de pierres");
        return mots.Count == 0 ? ", à mains nues" : $", armés {string.Join(" et ", mots)}";
    }

    /// <summary>
    /// Efficacité d'un moyen de frapper, relative (1 : pierres). Une branche assomme et brise mieux que des pierres lancées
    /// au jugé ; les poings tuent rarement.
    /// </summary>
    internal static double Puissance(Arme arme) => arme switch { Arme.Baton => 1.25, Arme.Pierre => 1, _ => 0.45 };

    /// <summary>
    /// Ce qu'un combattant a en main, selon ce que le terrain offre (branches en forêt, pierres en montagne, au désert et
    /// au bord de l'eau) et s'il a eu le temps de s'en saisir (<paramref name="prepare"/> : chance de ne pas être pris les mains vides).
    /// </summary>
    private Arme ChoisirArme(Humain h, double prepare)
    {
        if (alea.Reel() >= prepare) return Arme.Poings;
        var biome = carte.BiomeEn(h.X, h.Y);
        var branches = biome switch
        {
            Monde.Biome.ForetDeConiferes or Monde.Biome.ForetTropicale => 0.8,
            Monde.Biome.PlaineDeFougeres or Monde.Biome.Marais => 0.5,
            Monde.Biome.Desert or Monde.Biome.SommetEnneige => 0.1,
            _ => 0.3,
        };
        // L'adroit préfère la branche, qu'il manie mieux ; les autres ramassent des pierres.
        return alea.Reel() < branches * (0.7 + 0.6 * h.Personnalite.Adresse) ? Arme.Baton : Arme.Pierre;
    }

    /// <summary>
    /// L'attaque à l'aube : chaque guerrier s'en prend à un adulte du camp (d'abord aux hommes). La surprise avantage
    /// les assaillants, d'autant que le camp dort ; le nombre des défenseurs et la force de chacun décident du reste.
    /// </summary>
    private void Assaut(Communaute c, Communaute cible, Raid raid, List<Humain> humains, long maintenant)
    {
        var assaillants = raid.Guerriers.Select(id => humains[id - 1]).Where(h => h.EstVivant).ToList();
        var defenseurs = humains.Where(h => h.EstVivant && h.CommunauteId == cible.Id && !h.EstEnfant(maintenant)
                                            && Distance(h.X, h.Y, raid.X, raid.Y) <= 6).ToList();
        if (defenseurs.Count == 0)
        {
            Noter(maintenant, TypeEvenement.Guerre, assaillants[0], null,
                $"Les guerriers de la {Minuscule(c.Nom)} trouvent le camp de la {Minuscule(cible.Nom)} vide : ils rentrent.", raid.EvenementId);
            Rentrer(c, raid, maintenant);
            return;
        }
        var endormis = defenseurs.Count(h => h.Activite == Activite.Dormir) / (double)defenseurs.Count;
        var surprise = 1 + 0.8 * endormis;
        var nombre = Math.Clamp(defenseurs.Count / (double)assaillants.Count, 0.5, 2);
        var tues = new List<(Humain Victime, Humain Tueur, Arme Arme)>();
        var armes = new Dictionary<int, Arme>();
        // Les assaillants se sont préparés en chemin ; les défenseurs saisissent ce qu'ils trouvent, s'ils en ont le temps.
        foreach (var a in assaillants) armes[a.Id] = ChoisirArme(a, prepare: 1);
        foreach (var d in defenseurs) armes[d.Id] = ChoisirArme(d, prepare: d.Activite == Activite.Dormir ? 0.3 : 0.7);
        var blesses = new List<Humain>();
        foreach (var a in assaillants.OrderBy(_ => alea.Reel()).ToList())
        {
            var debout = defenseurs.Where(d => d.EstVivant && tues.All(t => t.Victime != d)).ToList();
            if (debout.Count == 0) break;
            // Les hommes font face les premiers ; les femmes se défendent aussi.
            var poids = debout.Select(d => d.Sexe == Sexe.Homme ? 3.0 : 1.0).ToList();
            var tirage = alea.Reel() * poids.Sum();
            var k = 0;
            while (k < debout.Count - 1 && (tirage -= poids[k]) > 0) k++;
            var d = debout[k];
            double fa = Force(a, maintenant), fd = Force(d, maintenant);
            var pTuer = Math.Min(0.6, 0.22 * surprise * 2 * fa / (fa + fd) * Puissance(armes[a.Id]));
            var pMourir = Math.Min(0.5, 0.12 / surprise * nombre * 2 * fd / (fa + fd) * Puissance(armes[d.Id]));
            var u = alea.Reel();
            if (u < pTuer) tues.Add((d, a, armes[a.Id]));
            else if (u < pTuer + pMourir) tues.Add((a, d, armes[d.Id]));
            else if (u < pTuer + pMourir + 0.3)
            {
                var blesse = alea.Reel() < fa / (fa + fd) ? d : a;
                var frappeur = blesse == d ? a : d;
                blesse.Sante -= (15 + 25 * alea.Reel()) * (0.6 + 0.4 * Puissance(armes[frappeur.Id]));
                blesses.Add(blesse);
            }
        }

        var mortsAssaillants = tues.Count(t => t.Victime.CommunauteId != cible.Id);
        var mortsDefenseurs = tues.Count - mortsAssaillants;
        var (nc, ncible) = (Minuscule(c.Nom), Minuscule(cible.Nom));
        var aube = endormis > 0.5 ? "À l'aube, " : "Au petit jour, ";
        var surpris = endormis > 0.5 ? "surprennent dans son sommeil" : "attaquent";
        string Liste(IEnumerable<Humain> hs) => string.Join(", ", hs.Select(h => h.Nom));
        var bilan = new List<string>();
        if (mortsDefenseurs > 0) bilan.Add($"{Liste(tues.Where(t => t.Victime.CommunauteId == cible.Id).Select(t => t.Victime))} {(mortsDefenseurs > 1 ? "sont tués" : "est tué" + E(tues.First(t => t.Victime.CommunauteId == cible.Id).Victime))}");
        if (mortsAssaillants > 0) bilan.Add($"parmi les assaillants, {Liste(tues.Where(t => t.Victime.CommunauteId != cible.Id).Select(t => t.Victime))} {(mortsAssaillants > 1 ? "tombent" : "tombe")}");
        var touches = blesses.Where(b => tues.All(t => t.Victime != b)).Distinct().ToList();
        if (touches.Count > 0) bilan.Add($"{Liste(touches)} {(touches.Count > 1 ? "sont blessés" : "est blessé" + E(touches[0]))}");
        if (bilan.Count == 0) bilan.Add("les défenseurs repoussent l'attaque sans perte de part et d'autre");
        var id = Noter(maintenant, TypeEvenement.Guerre, assaillants[0], humains[cible.FigureId - 1],
            $"{aube}les guerriers de la {nc} {surpris} le camp de la {ncible}{Armement(assaillants.Select(a => armes[a.Id]))} : {string.Join(" ; ", bilan)}.", raid.EvenementId, significatif: true);

        foreach (var (victime, tueur, arme) in tues)
        {
            if (!victime.EstVivant) continue;
            var coup = arme switch
            {
                Arme.Baton => $"tombe sous les coups de branche de {tueur.Nom}",
                Arme.Pierre => $"est abattu{E(victime)} à coups de pierres par {tueur.Nom}",
                _ => $"meurt sous les coups de poing de {tueur.Nom}",
            };
            Tuer(victime, maintenant, CauseDeMort.Guerre,
                $"{victime.Nom} {coup}, à {AgeEnMots(victime, maintenant)}, lors du raid de la {nc} contre la {ncible}.", id);
            victime.MeurtrierId = tueur.Id;
            Social!.Offenser(tueur, victime, 15, victime.MortEvenementId, humains, maintenant);
        }
        foreach (var b in touches.Where(b => b.Sante <= 0 && b.EstVivant))
        {
            Tuer(b, maintenant, CauseDeMort.Guerre, $"{b.Nom} meurt de ses blessures, à {AgeEnMots(b, maintenant)}, après le raid de la {nc} contre la {ncible}.", id);
        }
        // Ce que chacun en garde au fond de lui : le tueur, sa fierté ou son remords ; les attaqués, la peur qu'ils reviennent.
        // Le psychopathe ne garde ni remords ni fierté pour les siens : seulement ce qu'il y a gagné.
        foreach (var (victime, tueur, _) in tues.Where(t => t.Tueur.EstVivant))
        {
            var tp = tueur.Personnalite;
            if (Psyche.EstPsychopathe(tueur, maintenant))
                Jardin.Confier(tueur, TypeIntime.ImageDeSoi, victime.Id, $"J'ai tué {victime.Nom}. Je n'ai rien senti.", 40, id, maintenant, $"tue:{victime.Id}");
            else if (tp.Agreabilite >= 0.6 || tp.Moral.EmpathieEmotionnelle >= 0.65)
                Jardin.Confier(tueur, TypeIntime.Peine, victime.Id, $"J'ai tué {victime.Nom}. Son visage me hante.",
                    40 + 20 * tp.Agreabilite + 20 * tp.Moral.EmpathieEmotionnelle, id, maintenant, $"tue:{victime.Id}");
            else
                Jardin.Confier(tueur, TypeIntime.Joie, victime.Id, $"J'ai abattu {victime.Nom} : les miens sont plus forts.", 30 + 40 * (1 - tp.Agreabilite), id, maintenant, $"tue:{victime.Id}");
        }
        // Un combat où des siens sont tombés laisse une trace durable à ceux qui l'ont vécu (peur, sursauts : Jeronimus et al., 2014).
        foreach (var h in assaillants.Concat(defenseurs).Where(h => h.EstVivant))
        {
            var siens = tues.Count(t => t.Victime.CommunauteId == h.CommunauteId) + touches.Count(t => !t.EstVivant && t.CommunauteId == h.CommunauteId);
            if (siens > 0) Jardin.Marquer(h, Epreuve.Guerre, Math.Min(1, 0.3 + 0.2 * siens), id, maintenant, alea);
        }
        foreach (var d in defenseurs.Where(d => d.EstVivant))
        {
            Jardin.Confier(d, TypeIntime.Peur, c.FigureId, $"J'ai peur que les guerriers de la {nc} reviennent nous surprendre.",
                30 + 50 * d.Personnalite.Nevrosisme + 10 * mortsDefenseurs, id, maintenant, $"peur:raid:{c.Id}");
        }
        // Même sans mort, l'attaque offense tout le groupe attaqué.
        if (tues.Count == 0) Social!.Offenser(assaillants[0], defenseurs[0], 5, id, humains, maintenant);
        Social!.Ensanglanter(c, cible, mortsAssaillants, mortsDefenseurs, id, maintenant);
        Rentrer(c, raid, maintenant);
    }
}
