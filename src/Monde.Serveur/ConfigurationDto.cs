using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Serveur;

/// <summary>Réglages demandés par le navigateur pour un nouveau monde (taille en cases ; graine absente : au hasard).</summary>
public sealed record DemandeConfiguration(long? Graine, int Couples, int Largeur, int Hauteur, int Continents)
{
    /// <summary>Réglages validés ; la graine absente est tirée au hasard.</summary>
    /// <exception cref="ArgumentException">Graine ou réglages hors limites (message lisible).</exception>
    public ConfigurationMonde Valider()
    {
        if (Graine is < 0 or > uint.MaxValue) throw new ArgumentException("La graine doit être un entier entre 0 et 4294967295.");
        var graine = Graine is { } g ? (uint)g : (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        var configuration = new ConfigurationMonde(graine, Couples, Largeur, Hauteur, Continents);
        return configuration.Erreur() is { } erreur ? throw new ArgumentException(erreur) : configuration;
    }
}

/// <summary>Réglages du monde courant et limites acceptées, pour la fenêtre « Configuration ».</summary>
public sealed record ConfigurationDto(
    uint Graine, int Couples, int Largeur, int Hauteur, int Continents, bool Origine, double KilometresParCase,
    int CouplesMin, int CouplesMax, int TailleMin, int TailleMax, int ContinentsMin, int ContinentsMax, string Resume)
{
    public static ConfigurationDto Depuis(ServiceMonde service)
    {
        var monde = service.Monde;
        var c = monde.Configuration;
        return new((uint)c.Graine, c.Couples, c.Largeur, c.Hauteur, c.Continents, c == ConfigurationMonde.ParDefaut(c.Graine),
            Echelle.KilometresParCase, ConfigurationMonde.CouplesMin, ConfigurationMonde.CouplesMax,
            ConfigurationMonde.TailleMin, ConfigurationMonde.TailleMax, ConfigurationMonde.ContinentsMin, ConfigurationMonde.ContinentsMax,
            Resumer(c, monde.Carte));
    }

    /// <summary>Résumé d'une carte : dimensions, continents et îles, en mots.</summary>
    public static string Resumer(ConfigurationMonde c, Carte carte)
    {
        var (continents, iles) = Geographie.MassesDeTerre(carte);
        var surfaces = string.Join(", ", continents.Select(Echelle.Surface));
        var nombre = continents.Count == 1 ? "1 continent" : $"{continents.Count} continents";
        var ilesTexte = iles.Count switch { 0 => "", 1 => " ; 1 île", _ => $" ; {iles.Count} îles" };
        var couples = c.Couples == 1 ? "1 couple" : $"{c.Couples} couples";
        return $"Carte de {Echelle.Distance(Echelle.Kilometres(c.Largeur))} × {Echelle.Distance(Echelle.Kilometres(c.Hauteur))}, "
            + $"{nombre} ({surfaces}){ilesTexte}. Les {couples} ({c.Couples * 2} humains) partent du plus grand continent.";
    }
}

/// <summary>Résultat d'un aperçu : le résumé de la carte, ou la raison pour laquelle ces réglages sont impossibles.</summary>
public sealed record ApercuDto(bool Possible, string Texte);
