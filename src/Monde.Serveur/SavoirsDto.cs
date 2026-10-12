using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un savoir d'un humain : ce qu'il croit, à quel point il y croit, d'où il le tient.</summary>
public sealed record SavoirDto(string Texte, int Confiance, string Origine, int Maitre, string? NomMaitre, string AppriLe, int Essais, long Evenement);

/// <summary>Savoirs, objets portés, vêtement et aisance dans l'eau d'un humain.</summary>
public sealed record SavoirsDto(IReadOnlyList<SavoirDto> Savoirs, IReadOnlyList<string> Objets, string? Vetement, int Nage)
{
    public static string Origine(OrigineSavoir o) => o switch
    {
        OrigineSavoir.Observation => "en observant la nature",
        OrigineSavoir.Reflexion => "en réfléchissant",
        OrigineSavoir.Imitation => "en imitant",
        OrigineSavoir.Entite => "intuition soufflée par l'Entité",
        _ => "en essayant",
    };

    private static string Etat(Objet o) => o.Usure <= 0.4 ? " (usé)" : o.Qualite >= 0.7 ? " (bien fait)" : "";

    public static SavoirsDto Depuis(Humain h, IReadOnlyList<Humain> humains, Flore flore)
    {
        string? Nom(int i) => i >= 1 && i <= humains.Count ? humains[i - 1].Nom : null;
        return new SavoirsDto(
            h.Savoirs.OrderByDescending(s => s.Confiance).ThenBy(s => s.AppriLe)
                .Select(s => new SavoirDto(Physique.Decrire(s, s.Espece > 0 ? Majuscule(flore.Get(s.Espece).Nom) : null),
                    (int)Math.Round(100 * s.Confiance), Origine(s.Origine), s.MaitreId, Nom(s.MaitreId),
                    Calendrier.Decomposer(s.AppriLe).ToString(), s.Essais, s.EvenementId))
                .ToList(),
            h.Objets.Select(o => Physique.Nom(o.Matiere) + (o.Plein ? " pleine d'eau" : "") + Etat(o)).ToList(),
            h.Vetement is { } v ? (v.Matiere == Matiere.Peau ? "une peau de bête" : "des feuilles") + Etat(v) : null,
            (int)Math.Round(100 * h.Nage));
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>Une découverte dans la chronologie du monde : la première fois qu'un effet a été obtenu.</summary>
public sealed record DecouverteDto(string Date, long Secondes, string Savoir, int Humain, string Nom, string Origine, long Evenement, int Detenteurs);

/// <summary>Un choix proposé à l'Entité (geste ou matière) : clé et libellé.</summary>
public sealed record ChoixDto(string Cle, string Libelle);

/// <summary>Ce que l'Entité peut souffler : gestes et matières.</summary>
public sealed record ChoixIntuitionDto(IReadOnlyList<ChoixDto> Gestes, IReadOnlyList<ChoixDto> Matieres);

/// <summary>Demande de l'Entité : souffler à un humain l'idée d'un geste avec une ou deux matières.</summary>
public sealed record DemandeIntuition(int Humain, string Geste, string A, string? B);

public static class Decouvertes
{
    public static List<DecouverteDto> Chronologie(ServiceMonde service)
    {
        var monde = service.Monde;
        var (_, humains) = monde.InstantaneHumains();
        var (_, _, decouvertes) = monde.Installations();
        var detenteurs = humains.Where(h => h.EstVivant).SelectMany(h => h.Savoirs.Select(s => s.Effet).Distinct())
            .GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
        return decouvertes.OrderBy(d => d.Le).Select(d => new DecouverteDto(Calendrier.Decomposer(d.Le).ToString(), d.Le,
            Physique.NomEffet(d.Effet), d.HumainId, d.HumainId >= 1 && d.HumainId <= humains.Count ? humains[d.HumainId - 1].Nom : "?",
            SavoirsDto.Origine(d.Origine), d.EvenementId, detenteurs.GetValueOrDefault(d.Effet))).ToList();
    }

    /// <summary>Gestes et matières que l'Entité peut suggérer (la matière « rien » sert aux gestes à une seule matière).</summary>
    public static ChoixIntuitionDto Choix() => new(
        Enum.GetValues<Geste>().Select(g => new ChoixDto(g.ToString(), Physique.NomGeste(g))).ToList(),
        Enum.GetValues<Matiere>().Select(m => new ChoixDto(m.ToString(), m == Matiere.Aucune ? "(rien)" : Physique.Nom(m))).ToList());

    /// <summary>Savoirs partagés dans une communauté : chaque effet connu, et par combien de ses membres.</summary>
    public static List<string> DeCommunaute(IReadOnlyList<Humain> membres) =>
        membres.SelectMany(m => m.Savoirs.Where(s => s.Confiance >= 0.4 && s.Effet is not (Effet.Rien or Effet.Tiedeur)).Select(s => s.Effet).Distinct())
            .GroupBy(e => e).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => $"{Physique.NomEffet(g.Key)} ({g.Count()} sur {membres.Count})").ToList();
}
