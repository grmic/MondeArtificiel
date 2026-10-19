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
            h.Objets.Select(o => o.Matiere == Matiere.Carcasse
                ? $"de la viande{(o.Plein ? " fumée" : "")} ({Math.Max(1, Math.Round(100 * o.Qualite))} kg){(o.Usure <= 0.4 ? ", qui commence à se gâter" : "")}"
                : Physique.Nom(o) + (o.Plein ? o.Matiere switch { Matiere.Calebasse => " pleine d'eau", Matiere.Peau => " nouée en outre, pleine d'eau", _ => "" } : "")
                  + Etat(o)).ToList(),
            h.Vetement is { } v ? (v.Matiere == Matiere.Peau ? "une peau de bête" : "des feuilles") + Etat(v) : null,
            (int)Math.Round(100 * h.Nage));
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>Une découverte dans la chronologie du monde : la première fois qu'un effet a été obtenu.</summary>
public sealed record DecouverteDto(string Date, long Secondes, string Savoir, int Humain, string Nom, string Origine, long Evenement, int Detenteurs);

/// <summary>Un choix proposé à l'Entité (geste ou matière) : clé et libellé.</summary>
public sealed record ChoixDto(string Cle, string Libelle);

/// <summary>Un geste que l'Entité peut souffler : son verbe, le mot qui relie les matières, et les matières qui lui conviennent.</summary>
public sealed record FormeGesteDto(string Cle, string Verbe, string? Liaison, IReadOnlyList<ChoixDto> A, IReadOnlyList<ChoixDto> B);

/// <summary>Ce que l'Entité peut souffler à un humain : les gestes, et les plantes qui poussent là où il se trouve.</summary>
public sealed record ChoixIntuitionDto(IReadOnlyList<FormeGesteDto> Gestes, IReadOnlyList<ChoixDto> Plantes);

/// <summary>Demande de l'Entité : un geste avec une ou deux matières, ou une plante (numéro d'espèce) à manger ou appliquer.</summary>
public sealed record DemandeIntuition(int Humain, string Geste, string A, string? B, int Espece = 0);

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
            Physique.NomDecouverte(d.Effet, d.Pieces), d.HumainId, d.HumainId >= 1 && d.HumainId <= humains.Count ? humains[d.HumainId - 1].Nom : "?",
            SavoirsDto.Origine(d.Origine), d.EvenementId, detenteurs.GetValueOrDefault(d.Effet))).ToList();
    }

    /// <summary>
    /// Gestes que l'Entité peut souffler à cet humain, chacun avec les seules matières qui ont un sens pour lui, et les
    /// plantes qui poussent là où il se trouve (pour manger ou appliquer).
    /// </summary>
    public static ChoixIntuitionDto Choix(ServiceMonde service, int humain)
    {
        var monde = service.Monde;
        var (_, humains) = monde.InstantaneHumains();
        static ChoixDto Option(Geste g, Matiere m) => new(m.ToString(), m switch
        {
            Matiere.Aucune when g == Geste.Approcher => "ses mains",
            Matiere.Eau when g == Geste.Nager => "l'eau profonde",
            Matiere.Aucune when Physique.SansSeconde(g) is { } sol => sol,
            Matiere.Assemblage => "son outil emmanché",
            Matiere.Plante => "une plante",
            _ => Physique.Nom(m),
        });
        var gestes = Physique.Formes.Select(f => new FormeGesteDto(f.Geste.ToString(), f.Verbe, f.Liaison,
            f.A.Select(m => Option(f.Geste, m)).ToList(), f.B.Select(m => Option(f.Geste, m)).ToList())).ToList();
        var plantes = humain >= 1 && humain <= humains.Count
            ? monde.Flore.Sur(monde.Carte.BiomeEn(humains[humain - 1].X, humains[humain - 1].Y))
                .Select(e => new ChoixDto(e.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Nom)).ToList()
            : [];
        return new ChoixIntuitionDto(gestes, plantes);
    }

    /// <summary>Savoirs partagés dans une communauté : chaque effet connu, et par combien de ses membres.</summary>
    public static List<string> DeCommunaute(IReadOnlyList<Humain> membres) =>
        membres.SelectMany(m => m.Savoirs.Where(s => s.Confiance >= 0.4 && s.Effet is not (Effet.Rien or Effet.Tiedeur)).Select(s => s.Effet).Distinct())
            .GroupBy(e => e).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => $"{Physique.NomEffet(g.Key)} ({g.Count()} sur {membres.Count})").ToList();
}
