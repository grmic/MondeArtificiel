namespace Monde.Simulation.Humains;

/// <summary>
/// L'emprise de la figure d'une communauté sur les siens : l'autorité qu'on lui reconnaît (prestige) et la crainte qu'elle
/// inspire (domination). Rien n'est décrété : les deux se constatent à partir de son caractère, de ce qu'elle a fait et de
/// ce que les siens pensent d'elle.
/// <para>
/// Ce que dit la recherche : on gagne de l'influence dans un groupe par deux voies, le prestige (on suit volontiers celui
/// qu'on admire pour ses talents et sa bienveillance) et la domination (on cède à celui qu'on craint) ; les deux mènent à
/// l'influence (Henrich et Gil-White, 2001 ; Cheng et al., 2013). La domination va avec une faible agréabilité, l'agressivité
/// et le narcissisme (Cheng et al., 2010). Chez les chasseurs-cueilleurs, le groupe tient en respect celui qui veut
/// commander (Boehm, 1999) ; quand il n'y parvient plus, la peur fait taire les autres avis. Les poids sont des estimations.
/// </para>
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>
    /// Penchant d'un humain à s'imposer par la peur : peu agréable, froid (psychopathie), sûr de lui devant les autres et
    /// physiquement redoutable.
    /// </summary>
    public static double Domination(Humain f)
    {
        var p = f.Personnalite;
        var corps = (f.Sexe == Sexe.Homme ? 1 : 0.75) * (0.6 + 0.4 * p.Endurance);
        return Math.Clamp(0.35 * (1 - p.Agreabilite) + 0.25 * Psyche.Froideur(p) + 0.15 * p.Extraversion + 0.25 * corps, 0, 1);
    }

    /// <summary>Adultes d'une communauté, sans les captifs.</summary>
    private static List<Humain> AdultesDe(Communaute c, IReadOnlyList<Humain> humains, long maintenant) =>
        humains.Vivants().Where(h => h.EstVivant && h.CommunauteId == c.Id && !h.EstEnfant(maintenant) && h.CaptifDe == 0).ToList();

    /// <summary>
    /// Crainte qu'inspire la figure aux siens (0 à 1) : son penchant à dominer, et la violence qu'elle a montrée envers eux
    /// (ceux qu'elle a frappés, ceux dont elle a tué un proche). Un dominateur qui n'a jamais levé la main inspire déjà un peu de
    /// crainte ; celui qui a frappé beaucoup des siens tient le groupe par la peur.
    /// </summary>
    public static double Crainte(Communaute c, IReadOnlyList<Humain> humains, long maintenant)
    {
        if (c.FigureId < 1 || c.FigureId > humains.Count) return 0;
        var f = humains[c.FigureId - 1];
        var adultes = AdultesDe(c, humains, maintenant).Where(h => h.Id != f.Id).ToList();
        if (adultes.Count == 0) return 0;
        var violence = 0.0;
        foreach (var h in adultes)
        {
            foreach (var g in h.Griefs)
            {
                if (g.ContreId != f.Id || g.Motif is not (MotifGrief.Coups or MotifGrief.Meurtre)) continue;
                violence += (g.Motif == MotifGrief.Meurtre ? 1 : 0.5) * Math.Min(1, g.Initiale / 100);
            }
        }
        return Math.Clamp(Domination(f) * (0.2 + 2.5 * violence / adultes.Count), 0, 1);
    }

    /// <summary>
    /// Autorité reconnue à la figure (0 à 1) : son prestige (charisme, âge, talent reconnu, rôle religieux, désignée par
    /// l'Entité), l'affection que lui portent les siens, et leur respect pour ceux qui mènent.
    /// </summary>
    public static double Autorite(Communaute c, IReadOnlyList<Humain> humains, long maintenant)
    {
        if (c.FigureId < 1 || c.FigureId > humains.Count) return 0;
        var f = humains[c.FigureId - 1];
        var adultes = AdultesDe(c, humains, maintenant).Where(h => h.Id != f.Id).ToList();
        if (adultes.Count == 0) return 0;
        var age = Math.Clamp((f.AgeEnAnnees(maintenant) - 25) / 25.0, 0, 1);
        var designe = f.Impulsions.Count > 0 && f.Consigne(Parole.TuGuideras, maintenant) is not null;
        var prestige = 0.5 * f.Personnalite.Charisme + 0.2 * age + 0.15 * (f.Specialite != Ouvrage.Aucun ? 1 : 0)
                       + 0.15 * (f.Role != RoleReligieux.Aucun || designe ? 1 : 0);
        var affection = Math.Clamp(adultes.Average(h => h.AffectionPour(f.Id)) / 100, 0, 1);
        var respect = adultes.Average(h => h.Personnalite.Moral.Fondements.Autorite);
        return Math.Clamp(0.4 * prestige + 0.3 * affection + 0.3 * respect, 0, 1);
    }

    /// <summary>
    /// Chance que l'avis de la figure l'emporte sur le débat : une autorité très forte, ou une crainte qui fait taire les autres.
    /// Une autorité ordinaire ou une crainte légère n'y suffisent presque jamais.
    /// </summary>
    public static double Emprise(Communaute c, IReadOnlyList<Humain> humains, long maintenant)
    {
        var autorite = Autorite(c, humains, maintenant);
        var crainte = Crainte(c, humains, maintenant);
        return Math.Clamp(1 - (1 - Math.Pow(autorite, 4)) * (1 - Math.Pow(crainte, 2)), 0, 1);
    }
}
