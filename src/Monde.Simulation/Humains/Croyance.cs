using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Croyance en l'invisible, de 0 à 100. Au départ faible (0 à 10), un peu plus chez les esprits portés à la transcendance.
/// <para>
/// Elle monte quand l'invisible semble se montrer : une parole de l'Entité que l'on croit (une apparition plus qu'une voix,
/// une voix plus qu'un rêve), une vision après une plante, le rêve d'un mort qui revient (Tylor, 1871), une catastrophe
/// qui terrifie, la mort d'un proche (la pensée de la mort pousse à croire : Norenzayan et Hansen, 2006 ; Jong et al., 2012).
/// Elle se transmet : les enfants prennent la croyance de leurs parents, les adultes se règlent sur leurs proches et leur
/// communauté, d'autant plus qu'ils respectent l'ordre établi (Henrich, 2009 ; Gervais et Najle, 2015).
/// Elle décline lentement quand les années passent sans aucun signe, un peu plus vite chez les esprits analytiques
/// (lien faible, de l'ordre de r = −0,2 : Zuckerman, Silberman et Hall, 2013 ; Pennycook et al., 2016).
/// </para>
/// Chaque changement d'une dizaine est noté au journal avec ce qui l'a le plus causé.
/// </summary>
public static class Croyances
{
    /// <summary>Croyance de départ, de 0 à 10 : tirée du numéro (un flux à part : le reste du monde n'en dépend pas).</summary>
    public static double Initiale(int id, double transcendance)
    {
        var alea = new Alea(0x63726F79616E6365UL ^ ((ulong)id * 0x9E3779B97F4A7C15UL));
        return Math.Round(10 * Math.Clamp(0.6 * transcendance + 0.4 * alea.Reel(), 0, 1), 1);
    }

    /// <summary>Niveau vers lequel la croyance retombe quand rien ne vient la nourrir : celui du tempérament.</summary>
    public static double Repos(Humain h) => 10 * h.Personnalite.Transcendance;

    /// <summary>
    /// Fait bouger la croyance de <paramref name="h"/>. Une hausse compte moins quand on croit déjà beaucoup.
    /// <paramref name="signe"/> : l'invisible s'est montré (le déclin sans signe repart de zéro). Renvoie le changement réel.
    /// </summary>
    public static double Ajouter(Humain h, double delta, string raison, long cause, long maintenant, bool signe = false)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return 0;
        if (signe) h.CroyanceSigneLe = maintenant;
        var avant = h.Croyance;
        var d = delta > 0 ? delta * (1 - avant / 100) : delta;
        h.Croyance = Math.Clamp(avant + d, 0, 100);
        d = h.Croyance - avant;
        Retenir(h, d, raison, cause);
        return d;
    }

    /// <summary>Garde ce qui a le plus poussé la croyance dans chaque sens, pour le dire quand elle change d'une dizaine.</summary>
    internal static void Retenir(Humain h, double d, string raison, long cause)
    {
        if (d > h.CroyancePoussee)
            (h.CroyancePoussee, h.CroyanceRaison, h.CroyanceCause) = (d, raison, cause);
        else if (d < h.CroyanceBaisse)
            (h.CroyanceBaisse, h.CroyanceRaisonBaisse, h.CroyanceCauseBaisse) = (d, raison, cause);
    }

    /// <summary>Facteur de l'esprit analytique (0 à 1) : l'intelligence générale.</summary>
    internal static double Analytique(Humain h) => Math.Clamp((h.Personnalite.Intelligence.Generale - 55) / 90, 0, 1);

    /// <summary>
    /// Une journée : la croyance se règle sur celle des parents (enfant), des proches et de la communauté, et retombe vers
    /// son niveau de repos quand aucun signe n'est venu depuis plus d'un an.
    /// </summary>
    internal static void Journee(Humain h, IReadOnlyList<Humain> tous, IReadOnlyDictionary<int, double> communautes, long maintenant)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return;
        var p = h.Personnalite;
        var enfant = h.EstEnfant(maintenant);
        double somme = 0, poids = 0, parents = 0;
        foreach (var id in new[] { h.MereId, h.PereId })
        {
            if (id < 1 || id > tous.Count || !tous[id - 1].EstVivant) continue;
            var w = enfant ? 2 : 0.4;
            somme += w * tous[id - 1].Croyance;
            poids += w;
            parents += w;
        }
        foreach (var l in h.Liens)
        {
            if (l.Affection < 20 || l.Id < 1 || l.Id > tous.Count || !tous[l.Id - 1].EstVivant) continue;
            var w = 0.5 * l.Affection / 100;
            somme += w * tous[l.Id - 1].Croyance;
            poids += w;
        }
        if (h.CommunauteId != 0 && communautes.TryGetValue(h.CommunauteId, out var moyenne))
        {
            somme += 0.5 * moyenne;
            poids += 0.5;
        }
        var c = h.Croyance;
        if (poids > 0)
        {
            // Demi-vie de l'écart : quelques mois chez l'enfant, deux ans environ chez l'adulte (moins chez qui respecte l'ordre établi).
            var demiVie = (enfant ? 120 : 600) / (0.5 + p.Moral.Fondements.Autorite);
            var cible = somme / poids;
            var d = (cible - c) * (1 - Math.Pow(0.5, 1 / demiVie));
            c += d;
            Retenir(h, d, parents >= poids / 2
                ? (d > 0 ? "ses parents y croient" : "ses parents n'y croient guère")
                : (d > 0 ? "les siens y croient" : "les siens n'y croient guère"), 0);
        }
        // Les années sans signe : la croyance retombe vers celle du tempérament (demi-vie de quelques années).
        var repos = Repos(h);
        if (c > repos && maintenant - h.CroyanceSigneLe > Calendrier.SecondesParAn)
        {
            var demiVie = 360 * 6 * (0.6 + 0.8 * p.Transcendance) * (1.15 - 0.3 * Analytique(h));
            var d = -(c - repos) * (1 - Math.Pow(0.5, 1 / demiVie));
            c += d;
            Retenir(h, d, "aucun signe de l'invisible depuis longtemps", 0);
        }
        h.Croyance = Math.Clamp(c, 0, 100);
    }

    /// <summary>Texte du changement d'une dizaine, ou null s'il n'y a rien à noter.</summary>
    internal static string? Changement(Humain h)
    {
        if (double.IsNaN(h.CroyanceNotee)) h.CroyanceNotee = h.Croyance;
        var avant = h.CroyanceNotee;
        var apres = h.Croyance;
        if (Math.Floor(avant / 10) == Math.Floor(apres / 10) || Math.Abs(apres - avant) < 3) return null;
        var monte = apres > avant;
        var raison = monte ? h.CroyanceRaison : h.CroyanceRaisonBaisse;
        var n = (int)Math.Round(apres);
        return monte
            ? $"{h.Nom} croit davantage en l'invisible ({n}/100){(raison is null ? "" : " : " + raison)}."
            : $"{h.Nom} croit moins en l'invisible ({n}/100){(raison is null ? "" : " : " + raison)}.";
    }

    /// <summary>La croyance en mots, pour l'observateur.</summary>
    public static string Dire(double c) => c switch
    {
        < 5 => "n'y pense pas",
        < 20 => "en doute",
        < 40 => "y croit un peu",
        < 60 => "y croit",
        < 80 => "y croit fermement",
        _ => "en est habité",
    };
}

public sealed partial class Comportement
{
    /// <summary>
    /// Chaque jour : la croyance de chacun se règle sur les siens et retombe sans signe ; un changement d'une dizaine est noté
    /// au journal, avec sa raison principale et l'événement qui l'a causée.
    /// </summary>
    public void Croire(IReadOnlyList<Humain> humains, long maintenant)
    {
        _maintenant = maintenant;
        var moyennes = humains.Where(h => h.EstVivant && h.CommunauteId != 0 && !h.EstEnfant(maintenant))
            .GroupBy(h => h.CommunauteId).ToDictionary(g => g.Key, g => g.Average(h => h.Croyance));
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            Croyances.Journee(h, humains, moyennes, maintenant);
            if (Croyances.Changement(h) is not { } texte) continue;
            Noter(maintenant, TypeEvenement.Croyance, h, null, texte, h.Croyance > h.CroyanceNotee ? h.CroyanceCause : h.CroyanceCauseBaisse);
            h.CroyanceNotee = h.Croyance;
            (h.CroyancePoussee, h.CroyanceRaison, h.CroyanceCause) = (0, null, 0);
            (h.CroyanceBaisse, h.CroyanceRaisonBaisse, h.CroyanceCauseBaisse) = (0, null, 0);
        }
    }
}
