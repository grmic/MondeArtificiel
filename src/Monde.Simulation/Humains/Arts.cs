using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Ce qu'une image peinte sur la roche représente.</summary>
public enum MotifPeint : byte
{
    /// <summary>Une main posée sur la paroi, l'ocre soufflée autour (les plus anciennes ont plus de 40 000 ans : Aubert et al., 2014).</summary>
    Main = 0,
    Bete = 1,
    /// <summary>Pour un mort que l'on pleure.</summary>
    Morts = 2,
    /// <summary>L'invisible, tel qu'on le rêve.</summary>
    Entite = 3,
    Danse = 4,
    /// <summary>Des points et des lignes dont seul l'auteur sait le sens.</summary>
    Signes = 5,
}

/// <summary>Une image d'ocre sur une paroi : elle reste longtemps après son auteur.</summary>
/// <param name="CommunauteId">Communauté de l'auteur quand il l'a peinte (0 : aucune).</param>
public sealed record Oeuvre(int Id, int X, int Y, int AuteurId, long Le, MotifPeint Motif, string Description, long EvenementId, int CommunauteId);

/// <summary>
/// Les arts : personne ne les apporte, les humains les trouvent eux-mêmes. Le rythme naît de deux choses frappées en cadence
/// (voir <see cref="Physique.Tenter"/>), la parure de l'ocre écrasée sur la peau, le chant de la voix qui joue, la danse du corps
/// qui suit le chant ou le rythme, la peinture de l'ocre posée sur la roche. Le chant et la danse se retrouvent dans toutes les
/// sociétés connues (berceuses, chants de guérison, de danse, d'amour, lamentations : Mehr et al., 2019) ; chanter et danser
/// ensemble, en cadence, rapproche (Tarr et al., 2016) et apaise le chagrin.
/// </summary>
public static class Arts
{
    /// <summary>Le sens artistique (l'oreille, l'œil), de 0 à 1 : l'aptitude artistique de l'humain.</summary>
    public static double Aptitude(Humain h) => Math.Clamp((h.Personnalite.Intelligence.Artistique - 55) / 90, 0, 1);

    /// <summary>Le savoir est-il assez sûr pour qu'on s'en serve ?</summary>
    public static bool Connait(Humain h, Effet e) => h.Savoirs.Exists(s => s.Effet == e && s.Confiance >= 0.3);

    /// <summary>Plus d'œuvres que cela sur une même paroi (une case), on n'en ajoute plus : la paroi est couverte.</summary>
    public const int OeuvresParCase = 10;

    /// <summary>
    /// Le style d'une communauté, tel que l'observateur le décrit : il naît du caractère de ses membres (des gens expansifs chantent
    /// vif, des gens inquiets chantent plaintif, des gens rudes dansent la guerre) et de ce qu'ils peignent le plus. Null s'ils
    /// ne pratiquent aucun art.
    /// </summary>
    public static string? Style(IReadOnlyList<Humain> membres, IEnumerable<Oeuvre> oeuvres)
    {
        if (membres.Count == 0) return null;
        var parties = new List<string>();
        var chantent = membres.Count(h => Connait(h, Effet.Chant));
        if (chantent > 0)
        {
            var e = membres.Average(h => h.Personnalite.Extraversion);
            var n = membres.Average(h => h.Personnalite.Nevrosisme);
            var tempo = e > 0.55 ? "vifs" : e < 0.45 ? "lents" : "posés";
            var ton = n > 0.55 ? " et plaintifs" : n < 0.45 ? " et joyeux" : "";
            // Quand la plupart des chanteurs ont des mots, les chants ont des paroles.
            var paroles = membres.Count(h => Connait(h, Effet.Chant) && h.StadeParole >= 1) * 2 > chantent ? " avec des paroles" : "";
            parties.Add($"chants {tempo}{ton}{paroles}");
        }
        var rythmes = membres.SelectMany(h => h.Savoirs).Where(s => s.Effet == Effet.Rythme && s.Confiance >= 0.3).ToList();
        if (rythmes.Count > 0)
        {
            var bois = rythmes.Count(s => Physique.EstBois(s.A) || Physique.EstBois(s.B) || s.A == Matiere.Tronc || s.B == Matiere.Tronc);
            var calebasse = rythmes.Count(s => s.A == Matiere.Calebasse || s.B == Matiere.Calebasse);
            parties.Add(calebasse * 2 > rythmes.Count ? "rythmes frappés sur des calebasses"
                : bois * 2 > rythmes.Count ? "rythmes frappés sur le bois" : "rythmes frappés sur la pierre");
        }
        if (membres.Any(h => Connait(h, Effet.Danse)))
        {
            var a = membres.Average(h => h.Personnalite.Agreabilite);
            parties.Add(a < 0.45 ? "danses guerrières" : "danses en cercle");
        }
        if (membres.Any(h => Connait(h, Effet.Ocre))) parties.Add("corps parés d'ocre");
        var motif = oeuvres.GroupBy(o => o.Motif).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (motif is not null)
        {
            parties.Add("on peint surtout " + motif.Key switch
            {
                MotifPeint.Main => "des mains",
                MotifPeint.Bete => "des bêtes",
                MotifPeint.Morts => "les morts",
                MotifPeint.Entite => "l'invisible",
                MotifPeint.Danse => "des danseurs",
                _ => "des signes",
            });
        }
        if (parties.Count == 0) return null;
        var texte = string.Join(", ", parties);
        return char.ToUpperInvariant(texte[0]) + texte[1..] + ".";
    }
}

public sealed partial class Comportement
{
    /// <summary>Dernière fête notée au journal, par communauté (une par saison au plus, pour ne pas noyer le journal).</summary>
    private readonly Dictionary<int, long> _fetes = [];

    /// <summary>Morts déjà pleurés en chantant, par chanteur (le jardin en garde aussi la trace).</summary>
    private readonly HashSet<(int Chanteur, int Mort)> _lamentations = [];

    /// <summary>Souvenirs déjà racontés, par conteur (chacun raconte une même chose une fois).</summary>
    private readonly HashSet<(int Conteur, string Cle)> _racontes = [];

    /// <summary>Œuvres déjà vues, par humain.</summary>
    private readonly HashSet<(int Humain, int Oeuvre)> _vues = [];

    /// <summary>
    /// Une journée d'arts, le soir venu : on invente un chant, on pleure un mort en chantant, on chante et frappe des rythmes pour
    /// ceux qui sont là, et les autres se mettent à danser ; certains peignent sur la roche, d'autres regardent ce qui y est peint.
    /// </summary>
    public void Arts(IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        var presents = tous.Where(h => h.EstVivant && !h.EstBebe(maintenant) && h.CaptifDe == 0).ToList();
        foreach (var h in presents) InventerChant(h, tous);
        foreach (var h in presents) Lamenter(h, tous);
        Veillees(presents, tous);
        Raconter(presents, tous);
        foreach (var h in presents) Peindre(h, tous);
        Contempler(presents, tous);
    }

    /// <summary>
    /// La voix qui joue : un enfant qui fredonne, un adulte seul avec sa peine ou sa joie. Les plus doués d'oreille trouvent le chant
    /// bien plus souvent ; la peine et la joie y poussent.
    /// </summary>
    private void InventerChant(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Savoirs.Exists(s => s.Effet == Effet.Chant)) return;
        var art = Humains.Arts.Aptitude(h);
        var emotion = 1 + (h.Chagrin > 40 ? 1.5 : 0) + (h.Humeur > 75 ? 1 : 0);
        // Rare : une fois en quelques années pour toute une petite population de départ.
        var p = 0.00002 * (0.2 + art) * emotion * (h.EstEnfant(_maintenant) ? 1.5 : 1);
        if (alea.Reel() >= p) return;
        var recit = h.EstEnfant(_maintenant) ? $"{h.Nom} joue en laissant sa voix monter et descendre"
            : h.Chagrin > 40 ? $"Seul{E(h)} avec sa peine, {h.Nom} laisse sa voix monter et descendre"
            : h.Humeur > 75 ? $"Dans sa joie, {h.Nom} laisse sa voix monter et descendre"
            : $"{h.Nom} laisse sa voix monter et descendre, pour le plaisir";
        Retenir(h, Geste.Chanter, Matiere.Aucune, Matiere.Aucune, Effet.Chant, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous,
            recit: $"{recit} : c'est un chant.");
    }

    /// <summary>
    /// Celui qui sait chanter et pleure un mort chante pour lui, une fois : ceux qui le pleurent aussi et savent chanter se joignent
    /// à lui. La lamentation soulage, les chanteurs et les proches qui les entourent.
    /// </summary>
    private void Lamenter(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!PeutLamenter(h, tous)) return;
        if (alea.Reel() >= 0.3 * (0.4 + Humains.Arts.Aptitude(h)) * (0.5 + h.Personnalite.Extraversion)) return;
        var mort = tous[h.DeuilDeId - 1];
        var choeur = tous.Where(o => o.Id != h.Id && o.DeuilDeId == mort.Id && Distance(o.X, o.Y, h.X, h.Y) <= 1 && o.CaptifDe == 0
                                     && PeutLamenter(o, tous)).ToList();
        List<Humain> chanteurs = [h, .. choeur];
        var proches = tous.Where(o => !chanteurs.Contains(o) && o.EstVivant && !o.EstBebe(_maintenant) && o.CaptifDe == 0 && Distance(o.X, o.Y, h.X, h.Y) <= 1
                                     && (o.DeuilDeId == mort.Id || o.AffectionPour(mort.Id) >= 30 || o.AffectionPour(h.Id) >= 30)).ToList();
        var avec = choeur.Count == 0 ? "" : choeur.Count == 1 ? $", avec {choeur[0].Nom}"
            : $", avec {string.Join(", ", choeur.Take(choeur.Count - 1).Select(o => o.Nom))} et {choeur[^1].Nom}";
        var autour = proches.Count == 0 ? "" : proches.Count == 1 ? $" {proches[0].Nom} {(choeur.Count == 0 ? "l'écoute" : "les écoute")}."
            : $" {proches.Count} proches {(choeur.Count == 0 ? "l'entourent" : "les entourent")}.";
        var id = Noter(_maintenant, TypeEvenement.Art, h, mort, $"{h.Nom} chante sa peine pour {mort.Nom}{avec}.{autour}", h.DeuilEvenementId);
        foreach (var c in chanteurs)
        {
            _lamentations.Add((c.Id, mort.Id));
            c.Chagrin = Math.Max(0, c.Chagrin - 10);
            Retenir(c, Geste.Chanter, Matiere.Aucune, Matiere.Aucune, Effet.Chant, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
            Jardin.Confier(c, TypeIntime.Peine, mort.Id, $"J'ai chanté pour {mort.Nom}. Ma peine a trouvé une voix.", 30 + 30 * c.Personnalite.Ouverture,
                id, _maintenant, $"lamentation:{mort.Id}");
            foreach (var o in chanteurs) if (o.Id > c.Id) Social?.ChangerAffection(c, o, 1, _maintenant);
        }
        foreach (var o in proches)
        {
            if (o.Chagrin > 0) o.Chagrin = Math.Max(0, o.Chagrin - 5);
            Social?.ChangerAffection(h, o, 1, _maintenant);
        }
    }

    /// <summary>Sait chanter, pleure un mort pour qui il n'a pas encore chanté, et sa peine est vive.</summary>
    private bool PeutLamenter(Humain h, IReadOnlyList<Humain> tous) =>
        h.DeuilDeId > 0 && h.DeuilDeId <= tous.Count && h.Chagrin >= 40 && Humains.Arts.Connait(h, Effet.Chant)
        && !_lamentations.Contains((h.Id, h.DeuilDeId)) && !h.Memoire.Jardin.Exists(x => x.Cle == $"lamentation:{h.DeuilDeId}");

    /// <summary>
    /// Le soir, ici et là, quelqu'un qui sait chanter ou frapper un rythme s'y met pour ceux qui sont autour : l'humeur monte, la
    /// peur et le chagrin reculent, et ceux qui chantent ou dansent ensemble se sentent plus proches. On apprend en écoutant ;
    /// un jour, quelqu'un se met à bouger en cadence : la danse est née.
    /// </summary>
    private void Veillees(List<Humain> presents, IReadOnlyList<Humain> tous)
    {
        var deja = new HashSet<int>();
        var artistes = presents.Where(h => h.Savoirs.Exists(s => s.Effet is Effet.Chant or Effet.Rythme && s.Confiance >= 0.3)).ToList();
        // L'ordre de passage est tiré au sort : personne n'a la priorité d'un jour à l'autre.
        for (var i = artistes.Count - 1; i > 0; i--)
        {
            var j = alea.Entier(i + 1);
            (artistes[i], artistes[j]) = (artistes[j], artistes[i]);
        }
        foreach (var s in artistes)
        {
            if (deja.Contains(s.Id)) continue;
            var art = Humains.Arts.Aptitude(s);
            if (alea.Reel() >= 0.25 * (0.3 + art) * (0.4 + s.Personnalite.Extraversion) * (s.EstMalade(_maintenant) ? 0.3 : 1)) continue;
            var public_ = presents.Where(o => o.Id != s.Id && !deja.Contains(o.Id) && Distance(o.X, o.Y, s.X, s.Y) <= 1
                                              && o.AffectionPour(s.Id) > -30).ToList();
            deja.Add(s.Id);
            foreach (var o in public_) deja.Add(o.Id);
            Veillee(s, public_, art, tous);
        }
    }

    private void Veillee(Humain s, List<Humain> public_, double art, IReadOnlyList<Humain> tous)
    {
        var chante = Humains.Arts.Connait(s, Effet.Chant);
        var rythme = s.Savoirs.Where(x => x.Effet == Effet.Rythme && x.Confiance >= 0.3).MaxBy(x => x.Confiance);
        if (chante) Retenir(s, Geste.Chanter, Matiere.Aucune, Matiere.Aucune, Effet.Chant, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        if (rythme is not null) Retenir(s, rythme.Geste, rythme.A, rythme.B, Effet.Rythme, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        s.Humeur = Math.Min(100, s.Humeur + 3);
        var q = Math.Clamp(art + (chante && rythme is not null ? 0.15 : 0), 0, 1);
        var actifs = new List<Humain> { s };
        var danseurs = new List<Humain>();
        if (Humains.Arts.Connait(s, Effet.Danse)) danseurs.Add(s);
        foreach (var o in public_)
        {
            var p = o.Personnalite;
            o.Humeur = Math.Min(100, o.Humeur + (2 + 4 * q) * (0.5 + p.Ouverture));
            o.Peur = Math.Max(0, o.Peur - 3);
            if (o.Chagrin > 0) o.Chagrin = Math.Max(0, o.Chagrin - (2 + 3 * q));
            var oa = Humains.Arts.Aptitude(o);
            var enfant = o.EstEnfant(_maintenant);
            // Qui sait chanter chante avec : on chante ensemble.
            if (chante && Humains.Arts.Connait(o, Effet.Chant) && alea.Reel() < 0.3 + 0.5 * p.Extraversion) actifs.Add(o);
            // Apprendre en écoutant.
            if (chante && !o.Savoirs.Exists(x => x.Effet == Effet.Chant) && alea.Reel() < 0.05 * (0.3 + oa) * (enfant ? 1.8 : 1))
                Retenir(o, Geste.Chanter, Matiere.Aucune, Matiere.Aucune, Effet.Chant, 0, Symptome.Aucun, OrigineSavoir.Imitation, s.Id, tous);
            if (rythme is not null && !o.Savoirs.Exists(x => x.Effet == Effet.Rythme) && alea.Reel() < 0.05 * (0.3 + oa) * (enfant ? 1.8 : 1))
                Retenir(o, rythme.Geste, rythme.A, rythme.B, Effet.Rythme, 0, Symptome.Aucun, OrigineSavoir.Imitation, s.Id, tous);
            // Danser : ceux qui savent dansent volontiers ; un jour, quelqu'un se met à bouger en cadence pour la première fois.
            if (Humains.Arts.Connait(o, Effet.Danse))
            {
                if (alea.Reel() < (enfant ? 0.8 : 0.3 + 0.5 * p.Extraversion) && o.Fatigue < 80) danseurs.Add(o);
            }
            else if (!o.Savoirs.Exists(x => x.Effet == Effet.Danse) && alea.Reel() < 0.0005 * (0.3 + oa) * (0.4 + p.Extraversion) * (enfant ? 2 : 1))
            {
                var son = chante ? $"au chant de {s.Nom}" : $"au rythme que frappe {s.Nom}";
                Retenir(o, Geste.Danser, Matiere.Aucune, Matiere.Aucune, Effet.Danse, 0, Symptome.Aucun, OrigineSavoir.Essai, s.Id, tous,
                    recit: $"{o.Nom} se met à bouger en cadence {son}.");
                danseurs.Add(o);
            }
        }
        // Voir danser donne envie de danser.
        if (danseurs.Count > 0)
        {
            foreach (var o in public_)
            {
                if (danseurs.Contains(o) || o.Savoirs.Exists(x => x.Effet == Effet.Danse)) continue;
                if (alea.Reel() >= 0.08 * (0.3 + Humains.Arts.Aptitude(o)) * (o.EstEnfant(_maintenant) ? 1.8 : 1)) continue;
                Retenir(o, Geste.Danser, Matiere.Aucune, Matiere.Aucune, Effet.Danse, 0, Symptome.Aucun, OrigineSavoir.Imitation, danseurs[0].Id, tous);
                danseurs.Add(o);
            }
        }
        // On se pare d'ocre pour danser, si l'on en a sous la main ; ceux qui le voient peuvent l'imiter.
        var pare = danseurs.Find(d => Humains.Arts.Connait(d, Effet.Ocre) && APortee(d, Matiere.Ocre));
        foreach (var d in danseurs)
        {
            if (pare is not null && APortee(d, Matiere.Ocre))
            {
                if (Humains.Arts.Connait(d, Effet.Ocre))
                    Retenir(d, Geste.SeCouvrir, Matiere.Ocre, Matiere.Aucune, Effet.Ocre, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
                else if (alea.Reel() < 0.1 * (0.3 + Humains.Arts.Aptitude(d)) * (d.EstEnfant(_maintenant) ? 1.8 : 1))
                    Retenir(d, Geste.SeCouvrir, Matiere.Ocre, Matiere.Aucune, Effet.Ocre, 0, Symptome.Aucun, OrigineSavoir.Imitation, pare.Id, tous);
            }
            d.Peur = Math.Max(0, d.Peur - 5);
            d.Fatigue = Math.Min(100, d.Fatigue + 3);
            d.Humeur = Math.Min(100, d.Humeur + 2);
            if (d.Savoirs.Exists(x => x.Effet == Effet.Danse))
                Retenir(d, Geste.Danser, Matiere.Aucune, Matiere.Aucune, Effet.Danse, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
            if (!actifs.Contains(d)) actifs.Add(d);
        }
        // Chanter et danser ensemble, en cadence, rapproche (Tarr et al., 2016) : un peu, chaque fois.
        var lies = actifs.Count > 12 ? actifs.Take(12).ToList() : actifs;
        for (var i = 0; i < lies.Count; i++)
        for (var j = i + 1; j < lies.Count; j++)
            Social?.ChangerAffection(lies[i], lies[j], danseurs.Contains(lies[i]) && danseurs.Contains(lies[j]) ? 0.3 : 0.2, _maintenant);
        Feter(s, danseurs, chante, rythme);
    }

    /// <summary>
    /// Le soir, qui sait assembler des mots raconte ce qui l'a marqué : une croyance, une peur, une peine, une joie. Ceux qui
    /// l'écoutent dans sa langue en gardent la trace, plus faible ; une croyance racontée fait un peu croire. Ainsi les souvenirs
    /// passent d'une bouche à l'autre, et d'une génération à la suivante (récits du soir : Wiessner, 2014).
    /// </summary>
    private void Raconter(List<Humain> presents, IReadOnlyList<Humain> tous)
    {
        foreach (var s in presents)
        {
            if (s.StadeParole < 2 || s.CommunauteId == 0 || s.Activite == Activite.Dormir && alea.Reel() < 0.5) continue;
            if (alea.Reel() >= 0.01 * (0.4 + s.Personnalite.Extraversion) * (0.3 + Langues.Aptitude(s))) continue;
            var souvenir = s.Memoire.Jardin
                .Where(x => x.Type is TypeIntime.Croyance or TypeIntime.Peine or TypeIntime.Peur or TypeIntime.Joie
                            && x.Intensite >= 40 && x.CauseId > 0 && !_racontes.Contains((s.Id, x.Cle)))
                .MaxBy(x => x.Intensite);
            if (souvenir is null) continue;
            var auditeurs = presents.Where(o => o.Id != s.Id && o.CommunauteId == s.CommunauteId && o.StadeParole >= 1
                                                && Distance(o.X, o.Y, s.X, s.Y) <= 1 && o.AffectionPour(s.Id) > -30).Take(8).ToList();
            if (auditeurs.Count == 0) continue;
            _racontes.Add((s.Id, souvenir.Cle));
            // Un récit déjà entendu se raconte sous le nom de celui qui le dit maintenant.
            var fond = souvenir.Texte.IndexOf(" m'a raconté : ", StringComparison.Ordinal) is >= 0 and var k ? souvenir.Texte[(k + 15)..] : souvenir.Texte;
            var cle = souvenir.Cle.StartsWith("recit:", StringComparison.Ordinal) ? souvenir.Cle : "recit:" + souvenir.Cle;
            var qui = auditeurs.Count == 1 ? auditeurs[0].Nom : $"{auditeurs.Count} des siens";
            var id = Noter(_maintenant, TypeEvenement.Langage, s, auditeurs.Count == 1 ? auditeurs[0] : null,
                $"{s.Nom} raconte à {qui}, à la veillée : « {fond} »", souvenir.CauseId);
            foreach (var o in auditeurs)
            {
                Jardin.Confier(o, souvenir.Type, souvenir.SujetId, $"{s.Nom} m'a raconté : {fond}", souvenir.Intensite * (0.4 + 0.3 * o.Personnalite.Ouverture),
                    id, _maintenant, cle);
                if (souvenir.Type == TypeIntime.Croyance)
                    Croyances.Ajouter(o, 1 + 2 * o.Personnalite.Transcendance, $"ce que {s.Nom} a raconté à la veillée", id, _maintenant);
            }
        }
    }

    /// <summary>Quand beaucoup dansent ensemble, c'est une fête : elle est notée au journal (une par saison et par communauté au plus).</summary>
    private void Feter(Humain s, List<Humain> danseurs, bool chante, Savoir? rythme)
    {
        if (danseurs.Count < 6) return;
        var c = s.CommunauteId;
        if (_fetes.TryGetValue(c, out var derniere) && _maintenant - derniere < 90 * Calendrier.SecondesParJour) return;
        _fetes[c] = _maintenant;
        var groupe = c > 0 && Social is not null && c <= Social.Communautes.Count ? $" chez {Social.Communautes[c - 1].Nom}" : "";
        var son = chante && rythme is not null ? $"au chant et au rythme de {s.Nom}" : chante ? $"au chant de {s.Nom}" : $"au rythme que frappe {s.Nom}";
        var ocre = danseurs.Count(d => Humains.Arts.Connait(d, Effet.Ocre));
        var pare = ocre == 0 ? "" : ocre * 2 >= danseurs.Count ? ", parés d'ocre" : ", certains parés d'ocre";
        var id = Noter(_maintenant, TypeEvenement.Art, s, null, $"Fête{groupe} : {danseurs.Count} dansent {son}{pare}.");
        foreach (var d in danseurs)
            Jardin.Confier(d, TypeIntime.Joie, s.Id, "Nous avons dansé ensemble jusqu'à la nuit.", 25 + 30 * d.Personnalite.Extraversion, id, _maintenant, "fete");
    }

    /// <summary>
    /// Celui qui sait se parer d'ocre, et en a sous la main, devant une paroi de roche (montagne, désert, abri sous roche), y pose parfois une image :
    /// sa main, une bête qu'il chasse, un mort qu'il pleure, l'invisible auquel il croit, des danseurs, des signes. Les plus doués
    /// d'œil le font bien plus souvent.
    /// </summary>
    private void Peindre(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) && h.AgeEnAnnees(_maintenant) < 6) return;
        if (!Humains.Arts.Connait(h, Effet.Ocre) || !Physique.Paroi(carte, h.X, h.Y)) return;
        var art = Humains.Arts.Aptitude(h);
        var deuil = h.DeuilDeId > 0 && h.Chagrin > 20;
        var envie = 1 + (deuil ? 1 : 0) + h.Croyance / 100 + (h.Savoirs.Exists(s => s.Effet == Effet.Peinture) ? 1 : 0);
        if (alea.Reel() >= 0.002 * (0.2 + art) * (0.2 + art) * envie * (0.5 + h.Personnalite.Ouverture)) return;
        if (Installations.Oeuvres.Count(o => o.X == h.X && o.Y == h.Y) >= Humains.Arts.OeuvresParCase || !APortee(h, Matiere.Ocre)) return;

        var poids = new List<(MotifPeint Motif, double Poids)>
        {
            (MotifPeint.Main, 1),
            (MotifPeint.Signes, 0.5 * h.Personnalite.Ouverture),
        };
        if (h.Sait(Effet.Chasse)) poids.Add((MotifPeint.Bete, 1.5));
        if (deuil) poids.Add((MotifPeint.Morts, 2));
        if (h.Croyance >= 30) poids.Add((MotifPeint.Entite, h.Croyance / 40));
        if (Humains.Arts.Connait(h, Effet.Danse)) poids.Add((MotifPeint.Danse, 1));
        var tirage = alea.Reel() * poids.Sum(x => x.Poids);
        var motif = poids[^1].Motif;
        foreach (var (m, w) in poids)
        {
            if (tirage < w) { motif = m; break; }
            tirage -= w;
        }
        var mort = deuil && h.DeuilDeId <= tous.Count ? tous[h.DeuilDeId - 1] : null;
        var description = motif switch
        {
            MotifPeint.Main => "une main rouge, l'ocre soufflée autour de la sienne posée sur la paroi",
            MotifPeint.Bete => "une bête de profil, telle qu'on la voit à la chasse",
            MotifPeint.Morts => $"une silhouette couchée, pour {mort?.Nom ?? "un mort"}",
            MotifPeint.Entite => "une grande forme rayonnante : l'invisible, tel qu'on le rêve",
            MotifPeint.Danse => "des silhouettes qui dansent en ronde",
            _ => "des points et des lignes dont l'auteur seul sait le sens",
        };
        h.Activite = Activite.Fabriquer;
        var savait = h.Savoirs.Exists(s => s.Effet == Effet.Peinture);
        var id = Retenir(h, Geste.Peindre, Matiere.Ocre, Matiere.Aucune, Effet.Peinture, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous,
            recit: $"{h.Nom} écrase de l'ocre et la pose sur la roche : {description}.");
        if (savait || id == 0)
            id = Noter(_maintenant, TypeEvenement.Art, h, mort, $"{h.Nom} peint sur la roche {description}.", motif == MotifPeint.Morts ? h.DeuilEvenementId : 0);
        var oeuvre = Installations.Peindre(h.X, h.Y, h.Id, _maintenant, motif, description, id, h.CommunauteId);
        _vues.Add((h.Id, oeuvre.Id));
        h.Humeur = Math.Min(100, h.Humeur + 5);
        if (deuil) h.Chagrin = Math.Max(0, h.Chagrin - 5);
    }

    /// <summary>
    /// Ceux qui passent devant une paroi peinte la regardent : l'image émeut, interroge ; celle qui montre l'invisible ou les morts
    /// fait croire un peu plus. Qui sait se parer d'ocre peut, en la voyant, comprendre qu'on peut peindre.
    /// </summary>
    private void Contempler(List<Humain> presents, IReadOnlyList<Humain> tous)
    {
        if (Installations.Oeuvres.Count == 0) return;
        var parCase = Installations.Oeuvres.ToLookup(o => (o.X, o.Y));
        foreach (var h in presents)
        {
            if (!parCase.Contains((h.X, h.Y))) continue;
            foreach (var o in parCase[(h.X, h.Y)])
            {
                if (o.AuteurId == h.Id || _vues.Contains((h.Id, o.Id)) || alea.Reel() >= 0.3) continue;
                _vues.Add((h.Id, o.Id));
                var p = h.Personnalite;
                h.Humeur = Math.Min(100, h.Humeur + 2 * (0.5 + p.Ouverture));
                var auteur = o.AuteurId >= 1 && o.AuteurId <= tous.Count ? tous[o.AuteurId - 1] : null;
                var connu = auteur is not null && (h.LienAvec(auteur.Id) is not null || h.CommunauteId != 0 && h.CommunauteId == o.CommunauteId);
                var question = connu ? $"{auteur!.Nom} a laissé sur la roche {o.Description}. Pourquoi a-t-on envie de laisser une trace ?"
                    : $"Qui a laissé sur la roche {o.Description} ?";
                Jardin.Confier(h, TypeIntime.Question, auteur?.Id ?? 0, question, 15 + 30 * p.Ouverture * (0.5 + Humains.Arts.Aptitude(h)),
                    o.EvenementId, _maintenant, $"oeuvre:{o.Id}");
                if (o.Motif is MotifPeint.Entite or MotifPeint.Morts)
                    Croyances.Ajouter(h, 1 + 2 * p.Transcendance, "une image de l'invisible peinte sur la roche", o.EvenementId, _maintenant);
                if (Humains.Arts.Connait(h, Effet.Ocre) && !h.Savoirs.Exists(s => s.Effet == Effet.Peinture)
                    && alea.Reel() < 0.05 * (0.2 + Humains.Arts.Aptitude(h)) * (0.5 + p.Ouverture))
                    Retenir(h, Geste.Peindre, Matiere.Ocre, Matiere.Aucune, Effet.Peinture, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous);
            }
        }
    }
}
