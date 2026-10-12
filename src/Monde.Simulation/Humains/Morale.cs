namespace Monde.Simulation.Humains;

/// <summary>
/// Les cinq fondements moraux de Haidt et Graham (Graham et al., 2011), de 0 à 1 autour de 0,5 :
/// ce à quoi chacun est sensible quand il juge ce qui est bien ou mal.
/// </summary>
/// <param name="Soin">Souffrir de la souffrance d'autrui, protéger les faibles.</param>
/// <param name="Equite">Justice, réciprocité : chacun doit recevoir sa part, le tricheur est puni.</param>
/// <param name="Loyaute">Fidélité au groupe : ne pas l'abandonner, le défendre, punir le traître.</param>
/// <param name="Autorite">Respect de ceux qui mènent et des usages établis.</param>
/// <param name="Purete">Dégoût de ce qui souille le corps et des actes jugés contre-nature ; sens du sacré.</param>
public sealed record FondementsMoraux(double Soin, double Equite, double Loyaute, double Autorite, double Purete)
{
    public static readonly FondementsMoraux Moyens = new(0.5, 0.5, 0.5, 0.5, 0.5);

    /// <summary>Fondements « individualisants » (soin, équité) : protéger chacun.</summary>
    public double Individualisants => (Soin + Equite) / 2;

    /// <summary>Fondements « liants » (loyauté, autorité, pureté) : tenir le groupe ensemble.</summary>
    public double Liants => (Loyaute + Autorite + Purete) / 3;
}

/// <summary>Style d'attachement adulte (Hazan et Shaver, 1987), tiré des deux dimensions d'anxiété et d'évitement.</summary>
public enum StyleAttachement
{
    /// <summary>À l'aise dans la proximité, confiant envers ses proches.</summary>
    Secure,
    /// <summary>Se méfie de la proximité, garde ses distances, souffre peu des séparations en apparence.</summary>
    Evitant,
    /// <summary>Craint d'être abandonné, jaloux, s'accroche.</summary>
    Anxieux,
}

/// <summary>
/// Traits du cœur et de la conscience morale, de 0 à 1 autour de 0,5, liés aux grands traits comme le montre la recherche
/// (voir <see cref="Psyche"/>).
/// </summary>
/// <param name="Honnetete">Honnêteté-humilité (modèle HEXACO, Ashton et Lee, 2007) ; bas : manipulateur, avide, vaniteux.</param>
/// <param name="EmpathieEmotionnelle">Ressentir ce que ressent l'autre (empathie affective).</param>
/// <param name="EmpathieCognitive">Comprendre ce que l'autre pense et ressent, deviner ses intentions.</param>
/// <param name="Compassion">Vouloir soulager la peine d'autrui (aspect de l'agréabilité, DeYoung et al., 2007).</param>
/// <param name="Psychopathie">
/// Continuum du modèle triarchique (Patrick et al., 2009) : audace, méchanceté, désinhibition. Le haut de l'échelle seulement,
/// à l'âge adulte, fait un psychopathe (voir <see cref="Psyche.EstPsychopathe"/>).
/// </param>
/// <param name="AnxieteAffective">Peur d'être abandonné, besoin d'être rassuré (attachement adulte).</param>
/// <param name="EvitementAffectif">Malaise dans la proximité, indépendance farouche (attachement adulte).</param>
/// <param name="Fondements">Fondements moraux.</param>
public sealed record TraitsMoraux(double Honnetete, double EmpathieEmotionnelle, double EmpathieCognitive, double Compassion,
    double Psychopathie, double AnxieteAffective, double EvitementAffectif, FondementsMoraux Fondements)
{
    public static readonly TraitsMoraux Moyens = new(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, FondementsMoraux.Moyens);

    /// <summary>Style d'attachement : sûr si ni l'anxiété ni l'évitement ne dépassent leur seuil, sinon celui qui dépasse le plus.</summary>
    public StyleAttachement Style => Psyche.Style(AnxieteAffective, EvitementAffectif);
}
