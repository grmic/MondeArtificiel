namespace Monde.Simulation.Temps;

/// <summary>Allure de l'horloge : combien de secondes du monde s'écoulent par seconde réelle.</summary>
public sealed record Vitesse(int Niveau, string Nom, long Facteur)
{
    /// <summary>Vitesses proposées, de la pause à « 5 minutes réelles = 5 ans ».</summary>
    public static IReadOnlyList<Vitesse> Toutes { get; } =
    [
        new(0, "Pause", 0),
        new(1, "Temps réel", 1),
        new(2, "1 min = 1 heure", 60),
        new(3, "1 min = 1 jour", 1_440),
        new(4, "1 min = 1 saison", 129_600),
        new(5, "5 min = 5 ans", 518_400),
    ];

    public static Vitesse Pause => Toutes[0];

    /// <summary>Ralenti le plus lent : 1 seconde du monde toutes les 10 secondes réelles.</summary>
    public const double FacteurMin = 0.1;

    /// <summary>
    /// Vitesse la plus haute que l'on peut demander (environ 19 ans par minute). Le monde va aussi vite
    /// que la machine le permet : la vitesse réellement atteinte est mesurée et affichée.
    /// </summary>
    public const double FacteurMax = 10_000_000;

    /// <summary>0 (pause) ou une valeur finie entre <see cref="FacteurMin"/> et <see cref="FacteurMax"/>.</summary>
    public static bool FacteurValide(double facteur) =>
        facteur == 0 || double.IsFinite(facteur) && facteur >= FacteurMin && facteur <= FacteurMax;

    /// <summary>Niveau de la vitesse préréglée qui a exactement ce facteur, ou -1 (vitesse libre).</summary>
    public static int NiveauDe(double facteur)
    {
        foreach (var v in Toutes)
        {
            if (v.Facteur == facteur) return v.Niveau;
        }
        return -1;
    }

    public static bool Existe(int niveau) => niveau >= 0 && niveau < Toutes.Count;
}
