using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Un nom porté par une communauté : le nom affiché, ce qu'il veut dire, depuis quand, et l'événement qui l'a donné.</summary>
public sealed record NomCommunaute(string Nom, string? Sens, long Depuis, long EvenementId);

/// <summary>
/// Ce qu'était une communauté au début d'une saison : sa taille et les terres qu'elle parcourait
/// (indices des cases, triés). La suite des archives raconte comment le groupe a grandi et s'est déplacé.
/// </summary>
public sealed record ArchiveCommunaute(int CommunauteId, long Secondes, int Membres, int Adultes, int[] Cases);

/// <summary>Ce qu'un nom de groupe veut dire : l'idée (clé), la traduction pour l'observateur, et l'humain qu'il honore.</summary>
public sealed record SensDuNom(string Cle, Recit Recit, int HumainId = 0)
{
    public SensDuNom(string cle, int humainId = 0, params (string, object?)[] args) : this(cle, new Recit($"sens.{cle}", args), humainId) { }

    /// <summary>Le sens en français.</summary>
    public string Texte => Recit.Texte;
}

/// <summary>
/// Identité et histoire des communautés : les terres qu'elles parcourent, le nom qu'elles se donnent et qu'elles
/// peuvent changer, et les fusions qu'elles décident.
/// <para>
/// Se nommer est une invention : personne ne l'a fait avant. L'idée naît chez un humain doué pour la parole
/// et curieux, et ne prend que si le groupe le suit. Une fois qu'un groupe porte un nom, ceux qui le côtoient
/// en ont l'idée bien plus facilement. Les noms viennent de ce qui frappe les membres : leur paysage, un ancêtre,
/// une figure respectée, ce qu'ils ont traversé, ou simplement « les humains », le sens de nombreux ethnonymes
/// réels (Inuit, Diné, Khoïkhoï).
/// </para>
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>
    /// Des terres où personne n'est passé depuis un an ne sont plus celles du groupe : les chasseurs-cueilleurs
    /// parcourent leur domaine au fil des saisons et le revisitent chaque année (Kelly, 2013).
    /// </summary>
    public const long MemoireTerritoire = Calendrier.SecondesParAn;

    /// <summary>Un groupe ne songe à se nommer qu'après avoir vécu quelques semaines ensemble.</summary>
    internal const long AgeAvantDeSeNommer = 8 * 7 * Calendrier.SecondesParJour;

    /// <summary>Chance par semaine qu'un adulte aux aptitudes moyennes ait l'idée de nommer son groupe.</summary>
    private const double EnvieDeNommer = 0.003;

    /// <summary>Quand un groupe nommé est connu, l'idée vient bien plus facilement.</summary>
    private const double IdeeConnue = 4;

    /// <summary>
    /// Au-delà d'une cinquantaine de personnes, une bande de chasseurs-cueilleurs n'a plus intérêt à grossir
    /// (Marlowe, 2005 : 28 personnes en moyenne, rarement plus de 50) : plus elles sont petites, plus deux bandes
    /// amies ont intérêt à s'unir.
    /// </summary>
    private const int TailleBandeMax = 50;

    /// <summary>Chance par semaine que deux alliées décident de s'unir, pour une motivation et un accord pleins.</summary>
    private const double ChanceFusionParSemaine = 0.15;

    /// <summary>Un groupe en route pour en rejoindre un autre y renonce s'il n'y est pas arrivé au bout d'une demi-année.</summary>
    private const long PatienceFusion = 180 * Calendrier.SecondesParJour;

    /// <summary>Après une fusion, il faut environ un an pour que les membres des deux groupes se connaissent.</summary>
    private const long DureeAccueil = Calendrier.SecondesParAn;

    /// <summary>Instantanés de saison à enregistrer (vidés par le monde).</summary>
    public List<ArchiveCommunaute> Archives { get; } = [];

    // ───────────── Territoire ─────────────

    /// <summary>Toutes les heures : chaque membre marque la case où il se trouve comme terre de son groupe.</summary>
    public void Parcourir(List<Humain> humains, Carte carte, long maintenant)
    {
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || CommunauteDe(h) is not { } c) continue;
            var i = carte.Indice(h.X, h.Y);
            c.Territoire[i] = Passage(c.Territoire.GetValueOrDefault(i), maintenant);
        }
    }

    // Une case des terres retient le jour du dernier passage et le nombre de jours où quelqu'un y est passé.
    private const int BitsJours = 16;

    /// <summary>Note un passage sur une case : nouveau jour de passage, ou même jour qu'avant.</summary>
    public static long Passage(long ancien, long maintenant)
    {
        var jour = maintenant / Calendrier.SecondesParJour;
        if (ancien == 0) return (jour << BitsJours) | 1;
        var jours = JoursDePassage(ancien);
        return (jour << BitsJours) | (long)(ancien >> BitsJours == jour ? jours : Math.Min(jours + 1, (1 << BitsJours) - 1));
    }

    /// <summary>Début du jour du dernier passage (secondes).</summary>
    public static long DernierPassage(long valeur) => (valeur >> BitsJours) * Calendrier.SecondesParJour;

    public static int JoursDePassage(long valeur) => (int)(valeur & ((1 << BitsJours) - 1));

    /// <summary>
    /// Terres fréquentées : les cases où des membres sont passés au moins deux jours différents. Une excursion
    /// isolée loin du camp ne fait pas des terres du groupe.
    /// </summary>
    public static IEnumerable<int> Frequentees(Dictionary<int, long> territoire) =>
        territoire.Where(t => JoursDePassage(t.Value) >= 2).Select(t => t.Key);

    /// <summary>
    /// Terres d'un groupe telles qu'on les voit : les cases parcourues et leurs abords immédiats (on chasse et cueille
    /// un peu autour de l'endroit où l'on passe), sans la mer. Indices triés.
    /// </summary>
    public static int[] Etendue(IEnumerable<int> cases, Carte carte)
    {
        var resultat = new HashSet<int>();
        foreach (var i in cases)
        {
            int x = i % carte.Largeur, y = i / carte.Largeur;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                    var j = carte.Indice(nx, ny);
                    if ((Biome)carte.Biomes[j] is not (Biome.Ocean or Biome.EauPeuProfonde)) resultat.Add(j);
                }
            }
        }
        return [.. resultat.Order()];
    }

    /// <summary>Au début de chaque saison : taille et terres de chaque communauté active, pour son histoire.</summary>
    public void Archiver(List<Humain> humains, List<Communaute> communautes, Carte carte, long maintenant)
    {
        var membres = humains.Vivants().Where(h => h.EstVivant && h.CommunauteId != 0).ToLookup(h => h.CommunauteId);
        foreach (var c in communautes.Where(c => c.Active))
        {
            var groupe = membres[c.Id].ToList();
            Archives.Add(new ArchiveCommunaute(c.Id, maintenant, groupe.Count, groupe.Count(h => !h.EstEnfant(maintenant)),
                Etendue(Frequentees(c.Territoire), carte)));
        }
    }

    // ───────────── Chaque semaine ─────────────

    /// <summary>Après les territoires et les relations : terres oubliées, fusions, noms.</summary>
    private void Identites(List<Humain> humains, List<Communaute> communautes, Dictionary<int, List<Humain>> membres, Carte carte,
        long maintenant)
    {
        var actives = communautes.Where(c => c.Active).ToList();
        foreach (var c in actives)
        {
            foreach (var cle in c.Territoire.Where(t => maintenant - DernierPassage(t.Value) > MemoireTerritoire).Select(t => t.Key).ToList())
            {
                c.Territoire.Remove(cle);
            }
        }
        Fusions(humains, communautes, actives, membres, maintenant);
        foreach (var c in actives)
        {
            if (membres.TryGetValue(c.Id, out var groupe)) Nommer(c, groupe, humains, communautes, carte, maintenant);
        }
    }

    // ───────────── Fusions ─────────────

    /// <summary>
    /// Deux communautés alliées et voisines peuvent décider de n'en faire plus qu'une : parce qu'elles sont trop petites,
    /// pour traverser ensemble une disette, ou pour faire front contre un ennemi commun. Il faut que leurs figures
    /// s'entendent. La plus petite part rejoindre la plus grande ; la fusion se constate quand elles vivent ensemble.
    /// </summary>
    private void Fusions(List<Humain> humains, List<Communaute> communautes, List<Communaute> actives,
        Dictionary<int, List<Humain>> membres, long maintenant)
    {
        foreach (var c in actives.Where(c => c.FusionVersId != 0))
        {
            var cible = communautes[c.FusionVersId - 1];
            if (cible.Active && RelationEntre(c.Id, cible.Id) is not (EtatRelation.Hostiles or EtatRelation.EnGuerre) && maintenant - c.FusionDecideeLe <= PatienceFusion)
            {
                (c.DestinationX, c.DestinationY) = (cible.CentreX, cible.CentreY);
                continue;
            }
            Noter(maintenant, TypeEvenement.Communaute, humains[c.FigureId - 1], null,
                cible.Active
                    ? new Recit("j.fusion.renonce", ("c", c), ("d", cible))
                    : new Recit("j.fusion.plusPersonne", ("c", c), ("d", cible)),
                c.FusionEvenementId, communaute: c.Id, autreCommunaute: cible.Id);
            c.FusionVersId = 0;
            c.DestinationX = c.DestinationY = -1;
        }

        for (var i = 0; i < actives.Count; i++)
        {
            for (var j = i + 1; j < actives.Count; j++)
            {
                var (a, b) = (actives[i], actives[j]);
                if (a.FusionVersId != 0 || b.FusionVersId != 0 || RelationEntre(a.Id, b.Id) != EtatRelation.Alliees) continue;
                if (maintenant - a.FondeeLe < AgeAvantDeSeNommer || maintenant - b.FondeeLe < AgeAvantDeSeNommer) continue;
                if (Math.Max(Math.Abs(a.CentreX - b.CentreX), Math.Abs(a.CentreY - b.CentreY)) > a.Rayon + b.Rayon + 12) continue;
                if (!membres.TryGetValue(a.Id, out var ma) || !membres.TryGetValue(b.Id, out var mb)) continue;

                var petites = Math.Clamp((TailleBandeMax - (ma.Count + mb.Count)) / (double)TailleBandeMax, 0, 1);
                var disette = a.Disette || b.Disette;
                var ennemie = actives.FirstOrDefault(k => k.Id != a.Id && k.Id != b.Id
                    && RelationEntre(a.Id, k.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre
                    && RelationEntre(b.Id, k.Id) is EtatRelation.Hostiles or EtatRelation.EnGuerre);
                // Un groupe né de l'autre retrouve les siens : chez les chasseurs-cueilleurs, les bandes se séparent et se
                // réunissent au fil des saisons et des affinités (sociétés de fission-fusion ; Marlowe, 2010).
                var parentes = a.OrigineId == b.Id || b.OrigineId == a.Id;
                var motivation = petites + (disette ? 0.3 : 0) + (ennemie is null ? 0 : 0.4) + (parentes ? 0.3 : 0);
                if (motivation <= 0) continue;

                var fa = humains[a.FigureId - 1];
                var fb = humains[b.FigureId - 1];
                // Des figures qui se connaissent décident selon ce qu'elles pensent l'une de l'autre ; sinon, chacune
                // s'en remet à l'amitié entre les membres des deux groupes (l'alliance suppose une affection moyenne d'au moins 20).
                var entente = fa.LienAvec(fb.Id) is { } la && fb.LienAvec(fa.Id) is { } lb ? (la.Affection + lb.Affection) / 2 : 20;
                if (entente < 10) continue;
                var accord = Math.Clamp(entente / 60, 0, 1) * (0.5 + (fa.Personnalite.Agreabilite + fb.Personnalite.Agreabilite) / 2);
                if (alea.Reel() >= ChanceFusionParSemaine * motivation * accord) continue;

                var (petite, grande) = ma.Count < mb.Count || (ma.Count == mb.Count && a.Id > b.Id) ? (a, b) : (b, a);
                petite.FusionVersId = grande.Id;
                petite.FusionDecideeLe = maintenant;
                (petite.DestinationX, petite.DestinationY) = (grande.CentreX, grande.CentreY);
                var (pourquoi, cause) = ennemie is not null
                    ? (new Recit("j.fusion.pourquoi.front", ("e", ennemie)), _offenses.GetValueOrDefault(Paire(petite.Id, ennemie.Id)))
                    : disette
                        ? (new Recit("j.fusion.pourquoi.disette"), a.Disette ? a.DisetteEvenementId : b.DisetteEvenementId)
                        : parentes
                            ? (new Recit("j.fusion.pourquoi.retrouver"), 0L)
                            : (new Recit("j.fusion.pourquoi.nombre"), 0L);
                if (cause == 0) cause = _debutsRelations.GetValueOrDefault(Paire(a.Id, b.Id));
                petite.FusionEvenementId = Noter(maintenant, TypeEvenement.Communaute, fa, fb,
                    new Recit("j.fusion.decident", ("a", fa), ("b", fb), ("pourquoi", pourquoi), ("p", petite), ("g", grande)),
                    cause, communaute: petite.Id, autreCommunaute: grande.Id);
            }
        }
    }

    /// <summary>
    /// Avant la constitution des groupes : ceux qui ont décidé de s'unir et sont arrivés près de l'autre groupe le
    /// rejoignent ; après une fusion, les nouveaux restent dans le groupe même s'ils n'y ont pas encore d'amis.
    /// </summary>
    private void Rassembler(List<Humain> humains, List<Humain> vivants, List<Communaute> communautes, long maintenant, Action<int, int> unir)
    {
        foreach (var c in communautes.Where(c => c.Active))
        {
            var figure = humains[c.FigureId - 1];
            if (c.FusionVersId != 0 && communautes[c.FusionVersId - 1] is { Active: true } cible
                && humains[cible.FigureId - 1] is { EstVivant: true } figureCible && figure.EstVivant
                && Math.Max(Math.Abs(figure.X - figureCible.X), Math.Abs(figure.Y - figureCible.Y)) <= 2 * DistanceCommunaute)
            {
                foreach (var h in vivants.Where(h => h.CommunauteId == c.Id && !Bannissement.ExcluDe(h, c.Id, maintenant))) unir(h.Id, figureCible.Id);
            }
            if (c.AccueilLe is { } accueil && maintenant - accueil < DureeAccueil && figure.EstVivant)
            {
                foreach (var h in vivants.Where(h => h.CommunauteId == c.Id && !Bannissement.ExcluDe(h, c.Id, maintenant)
                             && Math.Max(Math.Abs(h.X - figure.X), Math.Abs(h.Y - figure.Y)) <= 2 * DistanceCommunaute))
                {
                    unir(h.Id, figure.Id);
                }
            }
        }
    }

    /// <summary>La communauté <paramref name="c"/> s'est fondue dans <paramref name="cible"/> : ses mots, ses terres et parfois son nom passent à l'autre.</summary>
    private void Fondre(Communaute c, Communaute cible, Humain figure, int nombre, long maintenant)
    {
        c.FusionneeDansId = cible.Id;
        c.FusionVersId = 0;
        c.DestinationX = c.DestinationY = -1;
        cible.AccueilLe = maintenant;
        // Seuls passent les mots nés en nommant le groupe ; les autres restent ceux des nouveaux venus, qui apprendront ceux de leurs hôtes.
        foreach (var (idee, mot) in c.Lexique) if (Idees.DeNom(idee) && !c.HistoireLangue.Exists(x => x.Idee == idee)) cible.Lexique.TryAdd(idee, mot);
        foreach (var (indice, vu) in c.Territoire)
        {
            if (!cible.Territoire.TryGetValue(indice, out var deja) || DernierPassage(deja) < DernierPassage(vu)) cible.Territoire[indice] = vu;
        }
        // Un groupe sans nom adopte celui du groupe qu'il accueille.
        var prend = cible.NomPropre is null && c.NomPropre is not null;
        var id = Noter(maintenant, TypeEvenement.Communaute, figure, null,
            prend ? new Recit("j.fusion.fondNom", ("c", c), ("d", cible), ("n", nombre), ("mot", c.NomPropre))
                : new Recit("j.fusion.fond", ("c", c), ("d", cible), ("n", nombre)),
            c.FusionEvenementId, communaute: cible.Id, autreCommunaute: c.Id);
        if (!prend) return;
        (cible.NomPropre, cible.Sens, cible.SensCle, cible.EponymeId, cible.Nom) = (c.NomPropre, c.Sens, c.SensCle, c.EponymeId, c.Nom);
        cible.Noms.Add(new NomCommunaute(cible.Nom, cible.Sens, maintenant, id));
    }

    // ───────────── Noms ─────────────

    private void Nommer(Communaute c, List<Humain> groupe, List<Humain> humains, List<Communaute> communautes, Carte carte, long maintenant)
    {
        var adultes = groupe.Where(h => !h.EstEnfant(maintenant)).OrderBy(h => h.Id).ToList();
        if (adultes.Count == 0) return;
        if (c.NomPropre is null)
        {
            if (maintenant - c.FondeeLe >= AgeAvantDeSeNommer) ProposerNom(c, groupe, adultes, humains, communautes, carte, maintenant);
            return;
        }

        // Un nom qui ne convient plus : le paysage qu'il évoque est loin, ou l'humain qu'il honore est devenu détesté.
        Recit? raison = null;
        long cause = 0;
        // Le paysage ne compte que si le groupe s'est installé ailleurs depuis qu'il porte ce nom (pas au gré d'une excursion).
        if (EstPaysage(c.SensCle) && c.InstallationEvenementId > (c.Noms.LastOrDefault()?.EvenementId ?? 0) && Paysage(c, carte).All(p => p.Cle != c.SensCle))
        {
            if (alea.Reel() < 0.03) (raison, cause) = (new Recit("j.nom.raison.loin"), c.InstallationEvenementId);
        }
        else if (c.EponymeId > 0 && humains[c.EponymeId - 1] is { EstVivant: true } eponyme
                 && AffectionMoyenne(adultes, eponyme) <= -15 && alea.Reel() < 0.1)
        {
            raison = new Recit("j.nom.raison.eponyme", ("e", eponyme));
        }
        if (raison is null) return;
        var porteParole = adultes.MaxBy(h => h.Personnalite.Charisme * h.Personnalite.Intelligence.Verbale)!;
        var sens = ChoisirSens(c, porteParole, groupe, humains, carte, maintenant);
        var (mot, nouveaux) = Forger(c, sens, humains, communautes);
        Adopter(c, mot, sens, nouveaux, porteParole, maintenant, cause,
            ancien => new Recit("j.nom.renomme", ("ancien", ancien), ("mot", mot), ("sens", sens.Recit), ("raison", raison)));
    }

    private void ProposerNom(Communaute c, List<Humain> groupe, List<Humain> adultes, List<Humain> humains, List<Communaute> communautes,
        Carte carte, long maintenant)
    {
        var connue = CommunauteNommeeConnue(c, adultes, humains, communautes);
        var cause = connue?.Noms.LastOrDefault(n => n.Sens is not null)?.EvenementId ?? 0;
        foreach (var h in adultes)
        {
            var p = h.Personnalite;
            var envie = EnvieDeNommer * Math.Pow(p.Intelligence.Verbale / 100, 3) * (0.5 + p.Ouverture) * (0.5 + p.Extraversion)
                        * (connue is null ? 1 : IdeeConnue);
            if (alea.Reel() >= envie) continue;

            var sens = ChoisirSens(c, h, groupe, humains, carte, maintenant);
            var (mot, nouveaux) = Forger(c, sens, humains, communautes);
            // Le groupe suit qui il estime : un humain charismatique et aimé est écouté.
            var accord = Math.Clamp(0.15 + 0.6 * p.Charisme + AffectionMoyenne(adultes, h) / 100, 0.05, 0.95);
            if (alea.Reel() >= accord)
            {
                Noter(maintenant, TypeEvenement.Communaute, h, null,
                    new Recit("j.nom.refuse", ("h", h), ("mot", mot), ("sens", sens.Recit)),
                    cause, communaute: c.Id);
                return;
            }
            var premier = communautes.All(x => x.NomPropre is null);
            object inspiration = connue is null || premier ? "" : new Recit("j.nom.exemple", ("c", connue));
            Adopter(c, mot, sens, nouveaux, h, maintenant, cause,
                ancien => new Recit("j.nom.nomme", ("h", h), ("ancien", ancien), ("insp", inspiration), ("mot", mot), ("sens", sens.Recit), ("premier", premier)));
            return;
        }
    }

    private void Adopter(Communaute c, string mot, SensDuNom sens, Dictionary<string, string> nouveaux, Humain auteur, long maintenant,
        long cause, Func<RefCommunaute, Recit> texte)
    {
        foreach (var (idee, m) in nouveaux) c.Lexique[idee] = m;
        var id = Noter(maintenant, TypeEvenement.Communaute, auteur, null, texte(new RefCommunaute(c)), cause, communaute: c.Id);
        (c.NomPropre, c.Sens, c.SensCle, c.EponymeId) = (mot, sens.Recit.Encoder(), sens.Cle, sens.HumainId);
        c.Nom = $"Communauté {mot}";
        c.Noms.Add(new NomCommunaute(c.Nom, c.Sens, maintenant, id));
    }

    /// <summary>
    /// Le sens d'un nom, tiré selon ce qui entoure le groupe et le caractère de celui qui le propose : les curieux
    /// regardent le paysage, les bienveillants honorent un ancien ou la figure du groupe, les vaniteux se mettent en avant,
    /// les spirituels lèvent les yeux au ciel.
    /// </summary>
    private SensDuNom ChoisirSens(Communaute c, Humain auteur, List<Humain> groupe, List<Humain> humains, Carte carte, long maintenant)
    {
        var p = auteur.Personnalite;
        var candidats = new List<(SensDuNom Sens, double Poids)>();
        foreach (var lieu in Paysage(c, carte)) candidats.Add((lieu, 0.6 + p.Ouverture));
        var figure = humains[c.FigureId - 1];
        if (figure.Id != auteur.Id) candidats.Add((new SensDuNom("eponyme", figure.Id, ("h", figure)), 0.4 + p.Agreabilite));
        candidats.Add((new SensDuNom("eponyme", auteur.Id, ("h", auteur)), 1.5 * (1 - p.Agreabilite) * p.Extraversion));
        if (Ancetre(groupe, humains) is { } ancetre && ancetre.Id != figure.Id)
        {
            candidats.Add((new SensDuNom("ancetre", ancetre.Id, ("h", ancetre)), 1.2));
        }
        candidats.Add((new SensDuNom("gens"), 0.8));
        candidats.Add((new SensDuNom("soleil"), 1.5 * p.Transcendance * p.Transcendance));
        candidats.Add((new SensDuNom("etoiles"), 1.5 * p.Transcendance * p.Transcendance));
        if (c.DisettesSurmontees > 0) candidats.Add((new SensDuNom("faim"), 0.8));
        if (c.Migrations > 0) candidats.Add((new SensDuNom("marche"), 0.8));
        if (c.OrigineId != 0) candidats.Add((new SensDuNom("partis"), 0.7));

        // On ne se renomme pas pour reprendre le même sens.
        candidats.RemoveAll(x => x.Sens.Cle == c.SensCle && x.Sens.HumainId == c.EponymeId);
        var total = candidats.Sum(x => x.Poids);
        var tirage = alea.Reel() * total;
        foreach (var (sens, poids) in candidats)
        {
            tirage -= poids;
            if (tirage < 0) return sens;
        }
        return candidats[^1].Sens;
    }

    /// <summary>Le mot du nom, fait des mots du groupe (créés au besoin) ; deux groupes ne portent jamais le même nom.</summary>
    private (string Mot, Dictionary<string, string> Nouveaux) Forger(Communaute c, SensDuNom sens, List<Humain> humains, List<Communaute> communautes)
    {
        var nouveaux = new Dictionary<string, string>();
        string Mot(string idee, int syllabes)
        {
            if (c.Lexique.TryGetValue(idee, out var m) || nouveaux.TryGetValue(idee, out m)) return m;
            return nouveaux[idee] = Langues.Forger(SonsDe(c), alea, syllabes);
        }
        var ceux = Mot("ceux", 1);
        var mot = sens.Cle switch
        {
            "eponyme" => humains[sens.HumainId - 1].Nom.ToLowerInvariant() + ceux,
            "ancetre" => Mot("enfants", 1) + humains[sens.HumainId - 1].Nom.ToLowerInvariant(),
            "gens" => Mot("gens", 2),
            _ => Mot(sens.Cle, 1 + alea.Entier(2)) + ceux,
        };
        mot = char.ToUpperInvariant(mot[0]) + mot[1..];
        while (communautes.Any(x => x.NomPropre == mot)) mot += Langues.Forger(SonsDe(c), alea, 1);
        return (mot, nouveaux);
    }

    private static readonly HashSet<string> ClesPaysage = ["riviere", "lac", "rivage", "montagnes", "marais", "sables", "foret", "fougeres"];

    private static bool EstPaysage(string? cle) => cle is not null && ClesPaysage.Contains(cle);

    /// <summary>Ce qui frappe dans le paysage où vit le groupe : l'eau, les montagnes, la forêt…</summary>
    public static List<SensDuNom> Paysage(Communaute c, Carte carte)
    {
        var compte = new int[Enum.GetValues<Biome>().Length];
        var r = c.Rayon + 2;
        for (var y = Math.Max(0, c.CentreY - r); y <= Math.Min(carte.Hauteur - 1, c.CentreY + r); y++)
        {
            for (var x = Math.Max(0, c.CentreX - r); x <= Math.Min(carte.Largeur - 1, c.CentreX + r); x++) compte[carte.Biomes[carte.Indice(x, y)]]++;
        }
        int N(Biome b) => compte[(int)b];
        var resultat = new List<SensDuNom>();
        if (N(Biome.Riviere) >= 2) resultat.Add(new("riviere"));
        if (N(Biome.Lac) >= 2) resultat.Add(new("lac"));
        if (N(Biome.Plage) + N(Biome.EauPeuProfonde) >= 3) resultat.Add(new("rivage"));
        if (N(Biome.Montagne) + N(Biome.SommetEnneige) >= 4) resultat.Add(new("montagnes"));
        if (N(Biome.Marais) >= 4) resultat.Add(new("marais"));
        if (N(Biome.Desert) >= 4) resultat.Add(new("sables"));
        var forets = N(Biome.ForetDeConiferes) + N(Biome.ForetTropicale);
        if (forets > 0 && forets >= N(Biome.PlaineDeFougeres)) resultat.Add(new("foret"));
        else if (N(Biome.PlaineDeFougeres) > 0) resultat.Add(new("fougeres"));
        return resultat;
    }

    /// <summary>L'ancêtre (vivant ou mort) dont descendent au moins 40 % des membres du groupe, s'il y en a un.</summary>
    private static Humain? Ancetre(List<Humain> groupe, List<Humain> humains)
    {
        if (groupe.Count < 6) return null;
        var descendants = new Dictionary<int, int>();
        foreach (var h in groupe)
        {
            var vus = new HashSet<int>();
            var pile = new Stack<int>([h.MereId, h.PereId]);
            while (pile.Count > 0)
            {
                var id = pile.Pop();
                if (id <= 0 || !vus.Add(id)) continue;
                descendants[id] = descendants.GetValueOrDefault(id) + 1;
                var a = humains[id - 1];
                pile.Push(a.MereId);
                pile.Push(a.PereId);
            }
        }
        var meilleur = descendants.Where(d => d.Value * 10 >= groupe.Count * 4)
            .OrderByDescending(d => d.Value).ThenBy(d => d.Key).Select(d => d.Key).FirstOrDefault();
        return meilleur > 0 ? humains[meilleur - 1] : null;
    }

    /// <summary>Un groupe nommé que connaissent des membres : celui qu'ils ont quitté, ou celui d'un humain qu'ils fréquentent.</summary>
    private static Communaute? CommunauteNommeeConnue(Communaute c, List<Humain> adultes, List<Humain> humains, List<Communaute> communautes)
    {
        if (c.OrigineId > 0 && communautes[c.OrigineId - 1].NomPropre is not null) return communautes[c.OrigineId - 1];
        foreach (var h in adultes)
        {
            foreach (var l in h.Liens)
            {
                if (l.Familiarite < 20) continue;
                var autre = humains[l.Id - 1];
                if (autre.EstVivant && autre.CommunauteId != 0 && autre.CommunauteId != c.Id
                    && communautes[autre.CommunauteId - 1] is { Active: true, NomPropre: not null } k) return k;
            }
        }
        return null;
    }

    private static double AffectionMoyenne(List<Humain> adultes, Humain cible)
    {
        var autres = adultes.Where(a => a.Id != cible.Id).ToList();
        return autres.Count == 0 ? 0 : autres.Average(a => a.LienAvec(cible.Id)?.Affection ?? 0);
    }
}
