namespace Monde.Simulation.Temps;

public enum Saison
{
    Printemps = 0,
    Ete = 1,
    Automne = 2,
    Hiver = 3,
}

/// <summary>Date du monde décomposée, prête à afficher.</summary>
public sealed record DateMonde(
    long Annee,
    Saison Saison,
    int JourDeSaison,
    int Heure,
    int Minute,
    bool EstJour,
    double Lumiere)
{
    private static readonly string[] NomsSaisons = ["printemps", "été", "automne", "hiver"];

    public string NomSaison => NomsSaisons[(int)Saison];

    public override string ToString() =>
        $"An {Annee}, jour {JourDeSaison} {(Saison == Saison.Printemps ? "du" : "de l'")}{(Saison == Saison.Printemps ? " " : "")}{NomSaison}, {Heure:00}:{Minute:00}";
}

/// <summary>
/// Calendrier du monde : 4 saisons de 90 jours (360 jours par an).
/// Le temps du monde est compté en secondes depuis l'an 1, 1er jour du printemps, 00:00.
/// </summary>
public static class Calendrier
{
    public const long SecondesParMinute = 60;
    public const long SecondesParHeure = 3600;
    public const long SecondesParJour = 86_400;
    public const int JoursParSaison = 90;
    public const int JoursParAn = 4 * JoursParSaison;
    public const long SecondesParAn = JoursParAn * SecondesParJour;

    /// <summary>Durée du jour (lever au coucher), en heures, pour chaque saison.</summary>
    private static readonly double[] DureeJour = [12, 15, 12, 9];

    // Dernière date décomposée sur ce fil : chacun la redemande plusieurs fois par pas (la date est immuable, on la rend telle quelle).
    [ThreadStatic] private static DateMonde? _derniere;
    [ThreadStatic] private static long _secondesDerniere;

    public static DateMonde Decomposer(long secondes)
    {
        if (_derniere is { } d && _secondesDerniere == secondes) return d;
        d = Calculer(secondes);
        (_derniere, _secondesDerniere) = (d, secondes);
        return d;
    }

    private static DateMonde Calculer(long secondes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(secondes);

        var annee = secondes / SecondesParAn + 1;
        var dansAnnee = secondes % SecondesParAn;
        var jourDansAnnee = (int)(dansAnnee / SecondesParJour);
        var saison = (Saison)(jourDansAnnee / JoursParSaison);
        var dansJour = dansAnnee % SecondesParJour;
        var heure = (int)(dansJour / SecondesParHeure);
        var minute = (int)(dansJour % SecondesParHeure / SecondesParMinute);

        var lumiere = Lumiere(saison, dansJour / (double)SecondesParHeure);
        return new DateMonde(annee, saison, jourDansAnnee % JoursParSaison + 1, heure, minute, lumiere >= 0.5, lumiere);
    }

    /// <summary>
    /// Clarté de 0 (nuit noire) à 1 (plein jour), avec une aube et un crépuscule d'une heure
    /// centrés sur le lever et le coucher du soleil.
    /// </summary>
    public static double Lumiere(Saison saison, double heure)
    {
        var duree = DureeJour[(int)saison];
        var lever = 12 - duree / 2;
        var coucher = 12 + duree / 2;
        if (heure <= lever - 0.5 || heure >= coucher + 0.5) return 0;
        if (heure >= lever + 0.5 && heure <= coucher - 0.5) return 1;
        return heure < 12 ? heure - (lever - 0.5) : (coucher + 0.5) - heure;
    }
}
