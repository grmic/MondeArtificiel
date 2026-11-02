using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>
/// De quoi dessiner le portrait d'un humain (0.36.1) : ses traits hérités (en écarts-types), ceux que la simulation connaît
/// déjà (teint, symétrie, harmonie, corpulence) et ce que la vie y met en ce moment (âge, faim, maladie, blessure, humeur).
/// </summary>
/// <param name="Traits">Traits du visage, dans l'ordre de <see cref="TraitVisage"/>, en écarts-types.</param>
/// <param name="Teint">Teint, de 0 (clair) à 1 (sombre).</param>
/// <param name="Maigreur">Joues creusées par la faim, de 0 à 1.</param>
/// <param name="Paleur">Pâleur de la maladie ou d'une santé mauvaise, de 0 à 1.</param>
/// <param name="Humeur">neutre, joie, triste, colere ou peur.</param>
/// <param name="Coupe">Longueur des cheveux, de 0 à 1 (propre à chacun).</param>
public sealed record VisageDto(int Id, double[] Traits, string Sexe, double Age, double Teint, double Symetrie, double Harmonie,
    double Corpulence, double Maigreur, double Paleur, bool Fievre, string Humeur, bool Cicatrice, bool Plaie, bool Mort,
    double Gris, double Calvitie, double Coupe)
{
    public static VisageDto De(MondeSimule monde, Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        var traits = Visages.Traits(monde, h, humains);
        var age = Visages.Age(h, maintenant);
        var vivant = h.EstVivant;
        var corpulence = Attirance.Corpulence(h);
        var maigreur = vivant ? Math.Clamp(Math.Max((0.3 - corpulence) / 0.3, (h.Faim - 70) / 30), 0, 1) : 0;
        var malade = vivant && (h.EstMalade(maintenant) || h.InfectionId != 0 && h.SymptomesLe <= maintenant);
        var paleur = vivant ? Math.Clamp((60 - h.Sante) / 60 + (malade ? 0.3 : 0), 0, 1) : 0;
        var humeur = !vivant ? "neutre"
            : h.Peur >= 50 ? "peur"
            : h.Colere >= 50 ? "colere"
            : h.Chagrin >= 40 || h.EstDeprime || h.Humeur <= 30 ? "triste"
            : h.Humeur >= 65 ? "joie"
            : "neutre";
        var cicatrice = h.Sequelle.Membre != Membre.Aucun && h.Sequelle.Origine is OrigineBlessure.Coups or OrigineBlessure.Guerre;
        return new VisageDto(h.Id, traits.Select(v => Math.Round(Math.Clamp(v, -3, 3), 3)).ToArray(),
            h.Sexe == Monde.Simulation.Humains.Sexe.Femme ? "femme" : "homme", Math.Round(age, 2), Math.Round(h.Apparence.Teint, 3),
            Math.Round(h.Apparence.Symetrie, 3), Math.Round(h.Apparence.Harmonie, 3), Math.Round(corpulence, 3), Math.Round(maigreur, 3),
            Math.Round(paleur, 3), malade, humeur, cicatrice, vivant && Medecine.PlaieOuverte(h, maintenant), !vivant,
            Math.Round(Visages.Gris(traits, age), 3), Math.Round(Visages.Calvitie(traits, h.Sexe, age), 3),
            Math.Round(Visages.Propre(monde, h.Id, 0), 3));
    }

    /// <summary>Les visages de plusieurs humains (vignettes des listes) ; les numéros inconnus sont ignorés.</summary>
    public static List<VisageDto> De(MondeSimule monde, IEnumerable<int> ids)
    {
        var (maintenant, humains) = monde.InstantaneHumains();
        return ids.Distinct().Where(i => i >= 1 && i <= humains.Count).Take(200)
            .Select(i => De(monde, humains[i - 1], humains, maintenant)).ToList();
    }
}
