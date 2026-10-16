using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Maladies ordinaires (non contagieuses d'une bande à l'autre), nommées pour l'observateur.</summary>
public enum Maladie : byte
{
    Aucune = 0,
    Pneumonie = 1,
    Diarrhee = 2,
    Dysenterie = 3,
    Paludisme = 4,
    PlaieInfectee = 5,
    Tetanos = 6,
    VersIntestinaux = 7,
    InfectionDeLaPeau = 8,
    Tuberculose = 9,
    Rougeole = 10,
}

/// <summary>
/// Une maladie : son nom, sa gravité (dégâts sur la santé, relatifs), sa durée en jours (médiane), et la population
/// en dessous de laquelle elle ne peut pas exister.
/// </summary>
public sealed record FicheMaladie(Maladie Maladie, string Nom, double Gravite, double Jours, int PopulationMin);

/// <summary>
/// Les maladies des chasseurs-cueilleurs. Les infections causent environ 70 % de leurs morts, surtout celles des poumons
/// et du ventre, qui tuent d'abord les jeunes enfants (Gurven et Kaplan, 2007). Le paludisme frappe près des eaux
/// dormantes et chaudes ; une plaie peut s'infecter, rarement donner le tétanos ; les vers et les infections de la peau
/// affaiblissent plus qu'ils ne tuent. Les maladies des foules ne peuvent pas durer dans de petites populations :
/// la tuberculose s'étend avec les villages, la rougeole demande quelque 250 000 personnes en contact (Black, 1966).
/// La liste s'allongera avec la population.
/// </summary>
public static class Maladies
{
    public static readonly IReadOnlyList<FicheMaladie> Liste =
    [
        new(Maladie.Pneumonie, "une pneumonie", 1.4, 10, 0),
        new(Maladie.Diarrhee, "une diarrhée infectieuse", 1.0, 5, 0),
        new(Maladie.Dysenterie, "une dysenterie", 1.3, 8, 0),
        new(Maladie.Paludisme, "le paludisme", 1.2, 12, 0),
        new(Maladie.PlaieInfectee, "une plaie infectée", 1.0, 9, 0),
        new(Maladie.Tetanos, "le tétanos", 3.0, 14, 0),
        new(Maladie.VersIntestinaux, "des vers intestinaux", 0.3, 20, 0),
        new(Maladie.InfectionDeLaPeau, "une infection de la peau", 0.25, 10, 0),
        new(Maladie.Tuberculose, "la tuberculose", 0.7, 60, 1000),
        new(Maladie.Rougeole, "la rougeole", 1.5, 10, 250_000),
    ];

    /// <summary>Maladie sans nom propre (une maladie contagieuse, qui a sa fiche à part) : gravité ordinaire.</summary>
    private static readonly FicheMaladie Ordinaire = new(Maladie.Aucune, "une maladie", 1, 7, 0);

    public static FicheMaladie Fiche(Maladie m) => m == Maladie.Aucune ? Ordinaire : Liste.FirstOrDefault(f => f.Maladie == m) ?? Ordinaire;

    /// <summary>
    /// Quelle maladie : selon la saison et le froid (poumons), la faim et l'âge (ventre), le pays (paludisme en terre
    /// chaude et humide), une santé entamée par une blessure (plaie), et la taille de la population.
    /// </summary>
    public static Maladie Choisir(Humain h, Biome biome, Saison saison, int population, long maintenant, Alea alea)
    {
        var enfant = h.AgeEnAnnees(maintenant) < 5;
        var humide = biome is Biome.Marais or Biome.ForetTropicale or Biome.PlaineDeFougeres;
        var poids = new double[Liste.Count];
        for (var i = 0; i < Liste.Count; i++)
        {
            var f = Liste[i];
            if (population < f.PopulationMin) continue;
            poids[i] = f.Maladie switch
            {
                Maladie.Pneumonie => 1 + 2 * h.Froid / 100 + (saison == Saison.Hiver ? 1 : 0) + (enfant ? 0.5 : 0),
                Maladie.Diarrhee => 1 + h.Faim / 100 + (enfant ? 1.5 : 0) + (saison == Saison.Ete ? 0.5 : 0),
                Maladie.Dysenterie => 0.4 + (saison == Saison.Ete ? 0.3 : 0) + (enfant ? 0.3 : 0),
                Maladie.Paludisme => humide && saison != Saison.Hiver ? 1.5 : 0,
                Maladie.PlaieInfectee => h.Sante < 70 || Medecine.PlaieOuverte(h, maintenant) ? 2 : 0.2,
                Maladie.Tetanos => h.Sante < 70 || Medecine.PlaieOuverte(h, maintenant) ? 0.08 : 0.01,
                Maladie.VersIntestinaux => 0.5 + h.Faim / 200,
                Maladie.InfectionDeLaPeau => 0.4,
                Maladie.Tuberculose => 0.3,
                Maladie.Rougeole => enfant ? 1 : 0.1,
                _ => 0,
            };
        }
        // Manger cuit tue une partie des germes et des parasites de la viande.
        if (h.CuitJusqua > maintenant)
        {
            for (var i = 0; i < Liste.Count; i++)
                if (Liste[i].Maladie is Maladie.Diarrhee or Maladie.Dysenterie) poids[i] *= 0.7;
                else if (Liste[i].Maladie == Maladie.VersIntestinaux) poids[i] *= 0.5;
        }
        var tirage = alea.Reel() * poids.Sum();
        for (var i = 0; i < poids.Length; i++)
        {
            if ((tirage -= poids[i]) < 0 && poids[i] > 0) return Liste[i].Maladie;
        }
        return Maladie.Diarrhee;
    }

    /// <summary>Durée d'une maladie pour cet humain : autour de sa médiane, plus longue chez les moins endurants.</summary>
    public static long Duree(FicheMaladie f, Humain h, Alea alea) =>
        (long)(f.Jours * (0.5 + alea.Reel()) * (1.3 - 0.6 * h.Personnalite.Endurance) * Calendrier.SecondesParJour);
}
