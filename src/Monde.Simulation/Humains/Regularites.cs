using System.Globalization;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Les séries que le monde fait passer sous les yeux de chacun (depuis la 0.34.1).</summary>
public enum Serie : byte
{
    Aucune = 0,
    /// <summary>La lune revient pleine.</summary>
    Lune = 1,
    /// <summary>Les saisons reviennent : le froid, puis le chaud.</summary>
    Saisons = 2,
    /// <summary>Les étoiles tournent toutes ensemble autour d'un point qui ne bouge pas.</summary>
    Etoiles = 3,
    /// <summary>Les choses se comptent : faire correspondre des doigts ou des encoches aux choses, une à une.</summary>
    Nombre = 4,
}

/// <summary>
/// Les régularités : chacun voit passer des séries (la lune, les saisons, les étoiles, des choses en nombre). Un esprit attentif peut y
/// lire une règle et s'en servir pour prévoir ; la suite des faits la confirme ou la dément ; confirmée, elle se dit aux autres. Rien
/// n'y nomme d'avance un calendrier : chacun garde sa propre estimation de la période, aussi juste que sa façon de compter le permet,
/// et la corrige quand le fait ne vient pas quand il l'attendait.
/// <para>
/// Ce que dit la recherche : sans compter, on juge une durée ou une quantité avec une erreur proportionnelle à sa grandeur (loi de
/// Weber ; fraction de Weber de l'adulte pour les quantités entre 0,1 et 0,4 environ : Halberda et Feigenson, 2008 ; pour les durées
/// longues, plus encore : Gibbon, 1977). Compter (faire correspondre une à une des choses à des doigts ou des encoches) rend
/// l'estimation presque exacte. Des os encochés sont connus depuis plus de 40 000 ans (Lebombo, Ishango), et l'on y a vu des comptes
/// de lunes (Marshack, 1972). Presque tous les peuples sans écriture ont un calendrier des lunes, recalé sur les signes des
/// saisons (Nilsson, 1920) ; les navigateurs du Pacifique tenaient leur cap la nuit aux levers et couchers des étoiles (Lewis, 1972).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Mois lunaire, en jours (voir aussi <c>Lune</c>, l'éclairement de la nuit).</summary>
    private const double MoisLunaire = 29.53;

    // ------------------------------------------------------------------ Lire et écrire une régularité

    /// <summary>« G|série|jours en dixièmes » : la série et la période qu'il estime (0 : sans période).</summary>
    public static (Serie Serie, double Jours)? LireRegularite(string? pieces)
    {
        if (pieces is null || !pieces.StartsWith("G|", StringComparison.Ordinal)) return null;
        var p = pieces.Split('|');
        if (p.Length != 3 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) || s is < 1 or > 4 || d < 0) return null;
        return ((Serie)s, d / 10.0);
    }

    public static string EcrireRegularite(Serie s, double jours) =>
        string.Create(CultureInfo.InvariantCulture, $"G|{(int)s}|{(int)Math.Round(Math.Max(0, jours) * 10)}");

    /// <summary>Ce qu'il en dit (« La lune revient pleine toutes les 30 nuits environ »).</summary>
    public static Recit DireRegularite(Serie s, double jours) => s switch
    {
        Serie.Lune or Serie.Saisons => new Recit($"savoir.Regularite.{s}", ("n", ((int)Math.Round(jours)).ToString(CultureInfo.InvariantCulture))),
        _ => Recit.Seul($"savoir.Regularite.{s}"),
    };

    private static Savoir? Regularite(Humain h, Serie s)
    {
        foreach (var x in h.Savoirs)
            if (x.Effet == Effet.Regularite && LireRegularite(x.Pieces) is { } r && r.Serie == s) return x;
        return null;
    }

    /// <summary>Confiance qu'il a dans cette régularité (0 s'il ne la connaît pas).</summary>
    internal static double ConfianceDans(Humain h, Serie s) => Regularite(h, s)?.Confiance ?? 0;

    /// <summary>
    /// Il sait d'avance quand reviendra le froid : une régularité des saisons à laquelle il croit (depuis la 0.34.1, génération 2).
    /// </summary>
    internal static bool PrevoitLesSaisons(Humain h) => ConfianceDans(h, Serie.Saisons) >= 0.5;

    // ------------------------------------------------------------------ Chaque jour

    /// <summary>
    /// Une fois par jour (génération 2) : la lune pleine revient, une saison commence, la nuit est claire. Ceux qui connaissent une
    /// régularité vérifient leur prévision ; les autres peuvent en remarquer une ; celui dont la prévision tombe juste le dit à ceux
    /// qui sont avec lui.
    /// </summary>
    public void Regularites(IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!AvecLaMatiere) return;
        _maintenant = maintenant;
        _tous = tous;
        var jour = maintenant / Calendrier.SecondesParJour;
        // La lune est pleine la nuit où sa phase passe la moitié du mois.
        var pleine = Math.Floor(jour / MoisLunaire - 0.5) != Math.Floor((jour - 1) / MoisLunaire - 0.5);
        var saison = jour % Calendrier.JoursParSaison == 0;
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.EstBebe(maintenant) || h.AgeEnAnnees(maintenant) < 4) continue;
            // Une nuit claire : d'autant plus rare que la région est arrosée.
            var clair = alea.Reel() >= 0.4 * Math.Min(1.5, ecosysteme.PluieEn(carte.Indice(h.X, h.Y)));
            if (pleine && clair) Voir(h, Serie.Lune, MoisLunaire, 1, tous);
            if (saison) Voir(h, Serie.Saisons, Calendrier.JoursParAn, 4, tous);
            if (clair) RegarderLeCiel(h, tous);
            Compter(h, tous);
        }
    }

    /// <summary>
    /// Son attention aux choses qui reviennent : la curiosité (ouverture) et l'intelligence générale. De 0,15 à 1,8 environ.
    /// </summary>
    private static double Attention(Humain h) => (0.5 + h.Personnalite.Ouverture) * (0.3 + Ingeniosite(h));

    /// <summary>
    /// Erreur relative de son estimation d'une durée de cette série (écart-type du logarithme). Sans compter, la fraction de Weber
    /// (0,1 à 0,3 selon l'intelligence) ; en comptant les nuits, une nuit sur trente environ, et pour l'année, une lune sur douze s'il
    /// compte les lunes. Le fait lui-même est flou : la lune paraît pleine trois nuits, une saison s'annonce sur des jours.
    /// </summary>
    private double Precision(Humain h, Serie s)
    {
        var i = Ingeniosite(h);
        var flou = s == Serie.Saisons ? 0.08 : 0.03;
        if (ConfianceDans(h, Serie.Nombre) < 0.5) return Math.Max(flou, 0.1 + 0.2 * (1 - i));
        var compte = 0.02 + 0.03 * (1 - i);
        // Compter jusqu'à quatre-vingt-dix jours, sans compter les lunes, c'est se tromper souvent d'une poignée de jours.
        if (s == Serie.Saisons && ConfianceDans(h, Serie.Lune) < 0.5) compte *= 2;
        return Math.Max(flou, compte);
    }

    /// <summary>
    /// Le fait revient (la lune pleine, le début d'une saison) : celui qui en connaît la régularité compare au moment où il l'attendait.
    /// <paramref name="periode"/> : la vraie période, en jours ; <paramref name="parts"/> : faits par période (quatre saisons par an).
    /// </summary>
    private void Voir(Humain h, Serie serie, double periode, int parts, IReadOnlyList<Humain> tous)
    {
        var w = Precision(h, serie);
        var i = h.Savoirs.FindIndex(x => x.Effet == Effet.Regularite && LireRegularite(x.Pieces) is { Serie: var se } && se == serie);
        if (i < 0)
        {
            // Remarquer : rarement, et seulement si l'on a déjà vu revenir la chose (assez vécu).
            var age = h.AgeEnAnnees(_maintenant);
            if (age < (serie == Serie.Lune ? 4 : 6)) return;
            var savoirLune = serie == Serie.Saisons && ConfianceDans(h, Serie.Lune) >= 0.5 ? 2 : 1;
            if (alea.Reel() >= 0.01 * Attention(h) * savoirLune * EspritsFertiles) return;
            var estime = periode * Math.Exp(w * Biologie.Normale(alea));
            var pieces = EcrireRegularite(serie, estime);
            Retenir(h, Geste.Observer, Matiere.Aucune, Matiere.Aucune, Effet.Regularite, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
                pieces: pieces, recit: new Recit($"reg.trouve.{serie}", ("h", h), ("n", ((int)Math.Round(estime)).ToString(CultureInfo.InvariantCulture))),
                parents: ParentsDeRegularite(h, serie));
            return;
        }
        var s = h.Savoirs[i];
        var jours = LireRegularite(s.Pieces)!.Value.Jours;
        if (jours <= 0) return;
        var pas = jours / parts;
        var intervalle = (_maintenant - s.UtiliseLe) / (double)Calendrier.SecondesParJour;
        // Il vient de l'apprendre ou de le voir : rien à vérifier encore.
        if (intervalle < pas / 2) return;
        // Les faits manqués (nuits couvertes, oubli) : il compte combien de fois la chose a dû revenir.
        var k = Math.Max(1, Math.Round(intervalle / pas));
        var percu = intervalle / k * Math.Exp(w * Biologie.Normale(alea));
        var juste = Math.Abs(percu - pas) / pas <= 2 * w;
        // Confirmée, l'estimation s'affine (moyenne des dernières fois) ; démentie, il la reprend de ce qu'il vient de voir.
        var n = Math.Min(s.Succes, 5) + 1.0;
        var nouvelle = juste ? (jours * n + percu * parts) / (n + 1) : percu * parts;
        h.Savoirs[i] = s with
        {
            Essais = s.Essais + 1, Succes = s.Succes + (juste ? 1 : 0), UtiliseLe = _maintenant, Pieces = EcrireRegularite(serie, nouvelle),
        };
        Servir(h, Serie.Nombre);
        if (juste && h.Savoirs[i].Succes >= 2) Dire(h, h.Savoirs[i], tous);
    }

    /// <summary>De quoi est née cette régularité : compter, et pour les saisons, les lunes qu'il sait voir revenir.</summary>
    private static string ParentsDeRegularite(Humain h, Serie serie)
    {
        var l = new List<string>();
        if (serie != Serie.Nombre && Regularite(h, Serie.Nombre) is { Confiance: >= 0.5 } c) l.Add(CleDeSavoir(c));
        if (serie == Serie.Saisons && Regularite(h, Serie.Lune) is { Confiance: >= 0.5 } lu) l.Add(CleDeSavoir(lu));
        if (serie == Serie.Nombre && h.Savoirs.Find(x => x.Effet == Effet.Decoupe && x.Confiance >= 0.4) is { } d) l.Add(CleDeSavoir(d));
        return string.Join(';', l);
    }

    /// <summary>Il s'est servi de cette régularité : elle reste en mémoire et se confirme.</summary>
    private void Servir(Humain h, Serie serie)
    {
        var i = h.Savoirs.FindIndex(x => x.Effet == Effet.Regularite && LireRegularite(x.Pieces) is { Serie: var se } && se == serie);
        if (i < 0) return;
        var s = h.Savoirs[i];
        h.Savoirs[i] = s with { Essais = s.Essais + 1, Succes = s.Succes + 1, UtiliseLe = _maintenant };
    }

    /// <summary>
    /// Une nuit claire : les étoiles tournent toutes ensemble autour d'un point qui ne bouge pas. Il faut le sens de l'espace pour le
    /// voir ; qui le sait le revoit et s'en sert pour garder un cap.
    /// </summary>
    private void RegarderLeCiel(Humain h, IReadOnlyList<Humain> tous)
    {
        var spatial = Math.Clamp((h.Personnalite.Intelligence.Spatiale - 55) / 90, 0, 1);
        if (Regularite(h, Serie.Etoiles) is { } s)
        {
            // Il ne lève pas les yeux chaque nuit.
            if (alea.Reel() >= 0.05 + 0.2 * h.Personnalite.Conscience) return;
            Servir(h, Serie.Etoiles);
            if (s.Succes >= 2 && alea.Reel() < 0.2) Dire(h, Regularite(h, Serie.Etoiles)!, tous);
            return;
        }
        if (h.AgeEnAnnees(_maintenant) < 10 || alea.Reel() >= 0.00005 * Attention(h) * (0.5 + spatial) * EspritsFertiles) return;
        Retenir(h, Geste.Observer, Matiere.Aucune, Matiere.Aucune, Effet.Regularite, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            pieces: EcrireRegularite(Serie.Etoiles, 1), recit: new Recit("reg.trouve.Etoiles", ("h", h)), parents: ParentsDeRegularite(h, Serie.Etoiles));
    }

    /// <summary>
    /// Compter : faire correspondre des doigts ou des encoches aux choses, une à une, et voir que le compte ne change pas quand on
    /// recommence. Plus souvent chez qui a l'esprit des mots, qui sait entailler (des encoches) ou veut compter les nuits d'une lune.
    /// </summary>
    private void Compter(Humain h, IReadOnlyList<Humain> tous)
    {
        if (Regularite(h, Serie.Nombre) is not null || h.AgeEnAnnees(_maintenant) < 8) return;
        var verbal = Math.Clamp((h.Personnalite.Intelligence.Verbale - 55) / 90, 0, 1);
        var encoches = h.Savoirs.Exists(x => x.Effet == Effet.Decoupe && x.Confiance >= 0.4) ? 2 : 1;
        var lunes = ConfianceDans(h, Serie.Lune) >= 0.5 ? 2 : 1;
        if (alea.Reel() >= 0.00002 * Attention(h) * (0.5 + verbal) * encoches * lunes * EspritsFertiles) return;
        Retenir(h, Geste.Observer, Matiere.Aucune, Matiere.Aucune, Effet.Regularite, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            pieces: EcrireRegularite(Serie.Nombre, 0), recit: new Recit(encoches > 1 ? "reg.trouve.Nombre.encoches" : "reg.trouve.Nombre", ("h", h)),
            parents: ParentsDeRegularite(h, Serie.Nombre));
    }

    /// <summary>
    /// Sa prévision est tombée juste : il le dit à ceux qui sont avec lui (« la lune sera pleine dans trois nuits »). Ce qui se dit
    /// passe par les mots : sans mot commun, on comprend mal ; et l'on écoute moins volontiers un étranger.
    /// </summary>
    private void Dire(Humain h, Savoir s, IReadOnlyList<Humain> tous)
    {
        var serie = LireRegularite(s.Pieces)!.Value.Serie;
        var idee = Idees.Savoir(Effet.Regularite);
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.EstBebe(_maintenant) || o.AgeEnAnnees(_maintenant) < 4) continue;
            if (Regularite(o, serie) is not null) continue;
            var affection = h.AffectionPour(o.Id);
            if (affection < -30) continue;
            var ecoute = 0.15 * (0.2 + 0.6 * o.Personnalite.Ouverture) * (o.EstEnfant(_maintenant) ? 1.8 : 1) * (0.5 + Math.Max(-30, affection) / 100.0)
                         * (Langues.Partagent(h, o, idee) ? 1.4 : 0.6) * Mefiance(o, h);
            if (alea.Reel() >= ecoute) continue;
            Retenir(o, Geste.Observer, Matiere.Aucune, Matiere.Aucune, Effet.Regularite, 0, Symptome.Aucun, OrigineSavoir.Imitation, h.Id, tous, pieces: s.Pieces);
        }
    }
}
