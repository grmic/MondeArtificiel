using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Reproduction, grossesse, bébés et vieillesse. La reproduction est un instinct : les humains
/// ne savent pas que l'accouplement mène à une naissance neuf mois plus tard.
/// </summary>
public sealed partial class Comportement
{
    private const double DesirParPas = 0.05;          // le désir culmine en deux semaines environ
    private const double SeuilDesir = 50;
    private const double ProbabiliteConception = 0.12; // par accouplement d'une femme féconde
    private const int JoursDeGrossesse = 270;
    private const double RisqueMortAccouchement = 0.015;
    private const double RisqueMortNe = 0.04;

    /// <summary>
    /// Mortalité annuelle « de fond » selon l'âge (accidents, maladies, usure), en plus des besoins.
    /// Bornes d'âge en années et taux annuel, du plus jeune au plus âgé.
    /// </summary>
    private static readonly (double AgeMax, double TauxAnnuel)[] MortaliteParAge =
    [
        (1, 0.10), (5, 0.03), (15, 0.01), (30, 0.005), (40, 0.01),
        (50, 0.03), (60, 0.07), (70, 0.15), (80, 0.30), (double.MaxValue, 0.50),
    ];

    /// <summary>Naissances survenues pendant le pas : le monde crée les nouveau-nés après le pas.</summary>
    public List<(Humain Mere, int PereId)> Accouchements { get; } = [];

    private bool ChercherAccouplement(Humain h, Humain? partenaire, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstFecond(maintenant)) return false;

        // Partenaires possibles à portée immédiate : de l'autre sexe, adultes, éveillés et disposés.
        Humain? choisi = null;
        if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) <= 1 && EstDispose(partenaire, maintenant))
        {
            choisi = partenaire;
        }
        else
        {
            // Avec un humain d'un autre couple : plus probable si son propre compagnon est loin ou mort.
            var ouverture = partenaire is null || Distance(h.X, h.Y, partenaire.X, partenaire.Y) > 8 ? 0.6 : 0.1;
            foreach (var autre in tous)
            {
                if (autre.Id == h.Id || autre.Sexe == h.Sexe || !autre.EstVivant) continue;
                if (Distance(h.X, h.Y, autre.X, autre.Y) > 1 || !EstDispose(autre, maintenant) || SontParents(h, autre)) continue;
                if (alea.Reel() < ouverture) choisi = autre;
                break;
            }
        }

        if (choisi is not null)
        {
            SAccoupler(h, choisi, tous, maintenant);
            return true;
        }

        // Désir fort : aller vers son compagnon, ou vers l'humain disposé le plus proche si l'on est seul.
        if (h.Desir < 70) return false;
        var cible = partenaire;
        if (cible is null)
        {
            var rayon = RayonPerception(h);
            cible = tous
                .Where(a => a.EstVivant && a.Sexe != h.Sexe && a.EstFecond(maintenant) && !SontParents(h, a)
                            && Distance(h.X, h.Y, a.X, a.Y) <= rayon)
                .OrderBy(a => Distance(h.X, h.Y, a.X, a.Y)).ThenBy(a => a.Id)
                .FirstOrDefault();
        }
        if (cible is null || Distance(h.X, h.Y, cible.X, cible.Y) == 0) return false;
        Avancer(h, cible.X, cible.Y, Activite.RejoindrePartenaire);
        return true;
    }

    private static bool EstDispose(Humain a, long maintenant) =>
        a.EstVivant && a.EstFecond(maintenant) && a.Activite != Activite.Dormir && a.Desir >= 25
        && a.Faim < 80 && a.Soif < 80;

    /// <summary>Parents et enfants, frères et sœurs : élevés ensemble, ils ne s'attirent pas (instinct).</summary>
    private static bool SontParents(Humain a, Humain b) =>
        a.MereId == b.Id || a.PereId == b.Id || b.MereId == a.Id || b.PereId == a.Id
        || (a.MereId != 0 && a.MereId == b.MereId)
        || (a.PereId != 0 && a.PereId == b.PereId);

    private void SAccoupler(Humain a, Humain b, IReadOnlyList<Humain> tous, long maintenant)
    {
        a.Activite = b.Activite = Activite.SAccoupler;
        a.CibleX = a.CibleY = b.CibleX = b.CibleY = -1;
        a.Desir = b.Desir = 0;

        var femme = a.Sexe == Sexe.Femme ? a : b;
        var homme = a.Sexe == Sexe.Femme ? b : a;
        var allaite = femme.DernierAccouchement is { } d && maintenant - d < Calendrier.SecondesParAn;
        if (!femme.EstEnceinte && !allaite && femme.EstFecond(maintenant) && alea.Reel() < ProbabiliteConception)
        {
            femme.EnceinteDepuis = maintenant;
            femme.PereEnfantId = homme.Id;
        }

        // Deux humains seuls qui s'accouplent forment un couple.
        if (Vivant(tous, a.PartenaireId) is null && Vivant(tous, b.PartenaireId) is null)
        {
            a.PartenaireId = b.Id;
            b.PartenaireId = a.Id;
        }
    }

    private void Grossesse(Humain mere, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (mere.EnceinteDepuis is not { } debut || maintenant - debut < JoursDeGrossesse * Calendrier.SecondesParJour) return;

        mere.EnceinteDepuis = null;
        mere.DernierAccouchement = maintenant;
        if (alea.Reel() >= RisqueMortNe) Accouchements.Add((mere, mere.PereEnfantId));
        mere.PereEnfantId = 0;
        mere.Fatigue = 100;
        if (alea.Reel() < RisqueMortAccouchement) Mourir(mere, maintenant, CauseDeMort.Accouchement);
    }

    /// <summary>Un bébé est porté par sa mère, qui l'allaite. Sans elle, il ne peut pas survivre longtemps.</summary>
    private void VivreBebe(Humain b, Humain? mere, long maintenant)
    {
        b.Fatigue = 0;
        b.Desir = 0;
        var porte = mere is not null && Distance(b.X, b.Y, mere.X, mere.Y) <= 1;
        if (mere is not null && porte)
        {
            b.X = mere.X;
            b.Y = mere.Y;
        }

        if (porte && mere!.Faim < 85 && mere.Soif < 85)
        {
            // L'allaitement nourrit le bébé et coûte à la mère.
            b.Faim = Math.Max(0, b.Faim - 1);
            b.Soif = Math.Max(0, b.Soif - 1);
            mere.Faim = Math.Min(100, mere.Faim + 0.06);
            mere.Soif = Math.Min(100, mere.Soif + 0.08);
        }
        else
        {
            b.Faim = Math.Min(100, b.Faim + 0.4);
            b.Soif = Math.Min(100, b.Soif + 0.6);
        }

        var temperature = Climat.Temperature(carte, b.X, b.Y, maintenant);
        var abri = Climat.Abri(carte.BiomeEn(b.X, b.Y)) + (porte ? 0.4 : 0);
        var perte = Math.Max(0, TemperatureConfort - temperature) * 0.065 * Math.Max(0, 1 - abri);
        b.Froid = Math.Clamp(b.Froid + perte - 0.4, 0, 100);

        b.Activite = porte ? (mere!.Activite == Activite.Dormir ? Activite.Dormir : Activite.PorteParSaMere) : Activite.Repos;
        Souffrir(b, maintenant, fragilite: 1.5);
    }

    /// <summary>Mortalité de fond, tirée une fois par jour du monde.</summary>
    public void Vieillir(Humain h, long maintenant)
    {
        if (!h.EstVivant) return;
        var age = h.AgeEnAnnees(maintenant);
        var taux = MortaliteParAge.First(t => age < t.AgeMax).TauxAnnuel;
        if (alea.Reel() < taux / Calendrier.JoursParAn)
        {
            Mourir(h, maintenant, age >= 50 ? CauseDeMort.Vieillesse : CauseDeMort.Maladie);
        }
    }
}
