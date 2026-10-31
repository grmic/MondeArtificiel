using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Ce qui a blessé (pour dire d'où vient une séquelle).</summary>
public enum OrigineBlessure : byte
{
    Aucune = 0,
    Chute = 1,
    Coups = 2,
    Guerre = 3,
    Catastrophe = 4,
}

/// <summary>Le membre qu'une blessure a abîmé.</summary>
public enum Membre : byte
{
    Aucun = 0,
    /// <summary>Jambe, genou, cheville, hanche : il boite.</summary>
    Jambe = 1,
    /// <summary>Bras, épaule, main : il porte, lance et grimpe mal.</summary>
    Bras = 2,
}

/// <summary>
/// Séquelle d'une blessure grave : le membre, la gêne quand la plaie s'est refermée (<paramref name="Debut"/>, 0 à 1) et celle qui
/// restera (<paramref name="Fixe"/>), depuis quand (<paramref name="Le"/>) et ce qui l'a causée. La gêne descend de l'une à l'autre
/// en quelques mois ; <paramref name="Debut"/> égal à <paramref name="Fixe"/> : elle ne bougera plus.
/// </summary>
public readonly record struct Sequelle(Membre Membre, double Debut, double Fixe, long Le, OrigineBlessure Origine);

/// <summary>
/// Séquelles des blessures graves (mondes de la génération 2, depuis la 0.32.8). Une fracture mal réduite, une articulation
/// démise, un tendon tranché laissent souvent une gêne durable chez les chasseurs-cueilleurs : chutes d'arbre et accidents de chasse
/// chez les Aché (Hill et Hurtado, 1996) et les Tsimane (Stieglitz et al., 2014), où les blessés produisent moins et reçoivent
/// davantage des leurs ; Shanidar 1, Néandertalien au bras atrophié qui boitait, a vécu des années ainsi, nourri par son groupe
/// (Trinkaus, 1983). La gêne décroît pendant la consolidation, de six semaines à quelques mois, puis se fixe ; elle est moindre
/// chez l'enfant, dont les os se refont mieux, et chez celui dont la plaie a été soignée, pire chez l'âgé.
/// </summary>
public static class Sequelles
{
    /// <summary>Temps caractéristique de la récupération (jours) : l'essentiel est fait en trois mois, presque tout en six.</summary>
    private const double JoursDeRecuperation = 60;

    /// <summary>Après ce temps, la gêne est fixée pour de bon (jours).</summary>
    public const int JoursAvantDeSeFixer = 240;

    /// <summary>En dessous, la gêne ne se voit plus.</summary>
    public const double Imperceptible = 0.05;

    /// <summary>À partir de là, tout le monde le voit boiter ou épargner son bras.</summary>
    public const double Visible = 0.25;

    /// <summary>Gêne de la séquelle de ce membre aujourd'hui (0 : aucune ; 1 : membre presque inutilisable).</summary>
    public static double Gene(Humain h, Membre m, long maintenant)
    {
        var s = h.Sequelle;
        if (s.Membre != m || m == Membre.Aucun) return 0;
        if (s.Debut <= s.Fixe) return s.Fixe;
        var jours = Math.Max(0, maintenant - s.Le) / (double)Calendrier.SecondesParJour;
        return s.Fixe + (s.Debut - s.Fixe) * Math.Exp(-jours / JoursDeRecuperation);
    }

    /// <summary>La plus forte gêne, quel que soit le membre.</summary>
    public static double Gene(Humain h, long maintenant) => Gene(h, h.Sequelle.Membre, maintenant);

    /// <summary>
    /// Probabilité qu'une blessure de cette gravité (dégâts sur 100 de santé) laisse une séquelle : rare pour une entaille, une chance
    /// sur trois environ pour une chute de dix mètres qu'on survit, presque toujours pour une blessure qui a failli tuer.
    /// </summary>
    public static double Probabilite(double gravite) => 1 / (1 + Math.Exp(-(gravite - 55) / 10));

    /// <summary>Part des séquelles qui touchent la jambe selon la cause : on tombe sur les jambes, on pare les coups des bras.</summary>
    public static double PartJambe(OrigineBlessure o) => o switch
    {
        OrigineBlessure.Chute => 0.7,
        OrigineBlessure.Catastrophe => 0.6,
        _ => 0.45,
    };
}

public sealed partial class Comportement
{
    /// <summary>
    /// Sa plaie grave s'est refermée : laisse-t-elle une séquelle ? Puis, chaque jour, la gêne se fixe au bout de quelques mois : il
    /// ne boite plus, ou il boitera toujours, et les siens le voient selon leur cœur.
    /// </summary>
    internal void Remettre(Humain h)
    {
        if (h.BlessureGrave > 0 && !Medecine.PlaieOuverte(h, _maintenant))
        {
            var (gravite, origine) = (h.BlessureGrave, h.BlessureOrigine);
            (h.BlessureGrave, h.BlessureOrigine) = (0, OrigineBlessure.Aucune);
            if (alea.Reel() < Sequelles.Probabilite(gravite)) Abimer(h, gravite, origine);
            return;
        }
        var s = h.Sequelle;
        if (s.Membre == Membre.Aucun || s.Debut <= s.Fixe || _maintenant - s.Le < Sequelles.JoursAvantDeSeFixer * Calendrier.SecondesParJour) return;
        if (s.Fixe < Sequelles.Imperceptible)
        {
            h.Sequelle = default;
            Noter(_maintenant, TypeEvenement.Medecine, h, null, new Recit($"seq.remis.{s.Membre}", ("h", h)));
            return;
        }
        h.Sequelle = s with { Debut = s.Fixe };
        Noter(_maintenant, TypeEvenement.Medecine, h, null, new Recit($"seq.fixe.{s.Membre}", ("h", h), ("p", Libelles.Pourcent(s.Fixe))));
        if (s.Fixe >= Sequelles.Visible) Regarder(h, s);
    }

    /// <summary>Une séquelle : le membre touché, la gêne au sortir de la plaie et celle qui restera.</summary>
    private void Abimer(Humain h, double gravite, OrigineBlessure origine)
    {
        var membre = alea.Reel() < Sequelles.PartJambe(origine) ? Membre.Jambe : Membre.Bras;
        var debut = Math.Clamp(0.3 + 0.7 * (gravite - 30) / 70 + 0.2 * (alea.Reel() - 0.5), 0.2, 1);
        var age = h.AgeEnAnnees(_maintenant);
        var fixe = debut * Math.Pow(alea.Reel(), 1.5) * (1 - 0.3 * h.PlaieSoin) * (age < 16 ? 0.5 : age >= 50 ? 1.3 : 1);
        // Un membre déjà abîmé l'est davantage ; une séquelle plus légère d'un autre membre s'efface devant la pire.
        var avant = h.Sequelle;
        if (avant.Membre == membre)
        {
            debut = Math.Min(1, debut + 0.5 * avant.Fixe);
            fixe += avant.Fixe;
        }
        else if (avant.Membre != Membre.Aucun && Sequelles.Gene(h, _maintenant) >= debut)
        {
            return;
        }
        h.Sequelle = new Sequelle(membre, debut, Math.Clamp(fixe, 0, 0.9), _maintenant, origine);
        Noter(_maintenant, TypeEvenement.Medecine, h, null, new Recit($"seq.{membre}", ("h", h), ("de", new Recit($"seq.origine.{origine}"))));
    }

    /// <summary>
    /// Il boitera toujours : il le sait, et les siens qui le connaissent le regardent selon leur cœur. Les compatissants s'attachent
    /// à lui et le nourrissent plus tôt (voir <see cref="Rapporter"/>) ; les durs le tiennent pour un poids.
    /// </summary>
    private void Regarder(Humain h, Sequelle s)
    {
        var p = h.Personnalite;
        Jardin.Confier(h, TypeIntime.ImageDeSoi, 0, new Recit(p.Nevrosisme > 0.6 ? $"js.seq.honte.{s.Membre}" : $"js.seq.accepte.{s.Membre}"),
            30 + 50 * s.Fixe, 0, _maintenant, "image:sequelle");
        foreach (var l in h.Liens.ToList())
        {
            if (l.Familiarite < 20) continue;
            var o = _tous[l.Id - 1];
            if (!o.EstVivant || o.EstEnfant(_maintenant)) continue;
            var m = o.Personnalite.Moral;
            var ecart = 6 * (m.Compassion - 0.5) - 6 * Math.Max(0, m.Psychopathie - 0.5);
            if (Math.Abs(ecart) >= 1) Social?.ChangerAffection(o, h, ecart, _maintenant);
        }
    }
}
