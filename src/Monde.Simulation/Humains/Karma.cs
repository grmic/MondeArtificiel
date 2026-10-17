namespace Monde.Simulation.Humains;

/// <summary>
/// Le regard de l'Entité : un karma que les humains ignorent, qui monte quand ils font du bien (veiller sur un malade,
/// partager, consoler, recueillir un orphelin, l'allaiter, pardonner) et baisse quand ils font du mal (tuer, voler,
/// trahir les siens, enlever, être infidèle là où c'est mal vu). Chaque changement est porté par l'événement du journal
/// qui le cause.
/// <para>
/// Barème (choix de zem, 16/10) : un meurtre pèse bien plus qu'un vol, tuer pour se défendre bien moins que tuer pour
/// prendre. Un meurtre efface ainsi des années de petits gestes (une saison de partage avec une personne vaut 0,5).
/// Un enfant ne mesure pas encore la portée de ce qu'il fait : ses fautes comptent moins, de plus en plus à
/// mesure qu'il grandit (l'âge de raison des sociétés humaines, vers 7 ans, et la responsabilité adulte vers 14).
/// Le psychopathe fait le bien par calcul plus que par compassion (Hare, 1993) : ses bonnes actions comptent moitié.
/// </para>
/// </summary>
public static class Regard
{
    // Barème des actes, en points de karma (avant les nuances d'âge et de caractère).
    public const double Partage = 0.5;
    public const double Remede = 0.5;
    public const double Consoler = 1;
    public const double Veiller = 1;
    public const double NourrirLesAffames = 1;
    public const double Pardonner = 2;
    public const double NourrirSansLait = 4;
    public const double Allaiter = 5;
    public const double Recueillir = 5;
    public const double Panser = 1;
    public const double AiderAccoucher = 2;

    public const double Vol = -3;
    public const double Coups = -2;
    public const double Trahir = -3;
    public const double Infidelite = -4;
    public const double LegitimeDefense = -5;
    public const double Enlever = -15;
    public const double TuerAuCombat = -25;
    public const double Meurtre = -50;
    /// <summary>Se faire nourrir en prétendant guérir.</summary>
    public const double Tromper = -2;
    /// <summary>Maudire quelqu'un pour qu'il dépérisse ; s'il en meurt, c'est presque un meurtre.</summary>
    public const double Maudire = -10;
    public const double MortMaudit = -25;

    /// <summary>Un petit geste (partager, veiller, consoler, donner un remède) ne compte qu'une fois par saison envers une même personne.</summary>
    public const double PetitGeste = 3;
    public const long DelaiPetitsGestes = Temps.Calendrier.SecondesParJour * Temps.Calendrier.JoursParSaison;

    /// <summary>
    /// Juge un acte de <paramref name="auteur"/> valant <paramref name="points"/> : applique les nuances d'âge et de caractère,
    /// ajoute le résultat à son karma et le renvoie (à porter sur l'événement du journal). Les petits gestes répétés envers
    /// une même personne (<paramref name="envers"/>) ne comptent qu'une fois par saison : l'Entité voit une bonté qui dure,
    /// pas chaque bouchée partagée.
    /// </summary>
    public static double Juger(Humain? auteur, double points, long maintenant, Humain? envers = null)
    {
        if (auteur is null || points == 0) return 0;
        var bienfait = false;
        if (points > 0 && points < PetitGeste && envers is not null && envers.Id != auteur.Id)
        {
            var i = auteur.Liens.FindIndex(l => l.Id == envers.Id);
            if (i >= 0)
            {
                var b = auteur.Liens[i].Bienfait;
                if (b > 0 && maintenant - b < DelaiPetitsGestes) return 0;
                bienfait = true;
            }
        }
        var d = points;
        if (points < 0)
        {
            var age = auteur.AgeEnAnnees(maintenant);
            if (age < 14) d *= Math.Clamp((age - 3) / 11, 0.1, 1);
        }
        else if (Psyche.EstPsychopathe(auteur, maintenant)) d *= 0.5;
        d = Math.Round(d, 2);
        // L'auteur peut être un autre que celui dont c'est le tour : son karma change alors après le pas.
        var enversId = envers?.Id ?? 0;
        Tour.Changer(auteur, a =>
        {
            if (bienfait && a.Liens.FindIndex(l => l.Id == enversId) is var i and >= 0) a.Liens[i] = a.Liens[i] with { Bienfait = Math.Max(1, maintenant) };
            a.Karma = Math.Round(a.Karma + d, 2);
        });
        return d;
    }

    /// <summary>
    /// Points d'une union hors du couple pour <paramref name="h"/> : rien s'il n'a pas de compagnon (ou si c'est avec lui),
    /// sinon d'autant plus que les siens réprouvent l'infidélité (<paramref name="tolerance"/> : 0 la réprouve, 1 l'admet).
    /// </summary>
    public static double PointsInfidelite(Humain h, Humain avec, IReadOnlyList<Humain> tous, double tolerance)
    {
        if (h.PartenaireId == 0 || h.PartenaireId == avec.Id) return 0;
        if (h.PartenaireId > tous.Count || !tous[h.PartenaireId - 1].EstVivant) return 0;
        return Infidelite * Math.Clamp(1 - tolerance, 0, 1);
    }

    /// <summary>Le karma en mots, pour l'observateur.</summary>
    public static string Dire(double karma) => karma switch
    {
        <= -80 => "lourdement chargé",
        <= -20 => "chargé",
        <= -5 => "un peu chargé",
        < 5 => "neutre",
        < 50 => "bienveillant",
        < 150 => "lumineux",
        _ => "très lumineux",
    };
}

public sealed partial class Comportement
{
    /// <summary>Points d'infidélité de <paramref name="h"/> avec <paramref name="avec"/>, selon l'usage des siens.</summary>
    private double PointsInfidelite(Humain h, Humain avec, IReadOnlyList<Humain> tous) =>
        Regard.PointsInfidelite(h, avec, tous, Social?.ToleranceDe(h) ?? VieSociale.TemperamentFidelite(h.Personnalite));
}
