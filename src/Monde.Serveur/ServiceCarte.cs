using System.Collections.Concurrent;
using Monde.Simulation.Monde;

namespace Monde.Serveur;

/// <summary>
/// Fournit les cartes générées : celle du monde courant, ou un aperçu pour d'autres réglages.
/// Une carte est calculée une seule fois par réglages, et le cache est borné pour ne pas grossir indéfiniment.
/// </summary>
public sealed class ServiceCarte(ServiceMonde service)
{
    private const int TailleMaxCache = 8;
    private readonly ConcurrentDictionary<ConfigurationMonde, Lazy<Carte>> _cache = new();

    /// <summary>Graine du monde courant (fixée à sa création, enregistrée dans sa sauvegarde).</summary>
    public uint GraineParDefaut => (uint)service.Monde.Graine;

    /// <summary>Carte du monde courant, ou celle d'une autre graine avec les mêmes réglages.</summary>
    public Carte Obtenir(uint? graine = null) =>
        Obtenir(service.Monde.Configuration with { Graine = graine ?? (uint)service.Monde.Graine });

    /// <exception cref="ArgumentOutOfRangeException">Réglages hors limites.</exception>
    /// <exception cref="InvalidOperationException">Pas assez de place pour les couples.</exception>
    public Carte Obtenir(ConfigurationMonde configuration)
    {
        var monde = service.Monde;
        if (configuration == monde.Configuration) return monde.Carte;
        if (_cache.Count >= TailleMaxCache && !_cache.ContainsKey(configuration))
        {
            _cache.Clear();
        }
        var lazy = _cache.GetOrAdd(configuration, c => new Lazy<Carte>(() => GenerateurContinent.Generer(c)));
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Une génération impossible n'est pas gardée : le cache ne retient que des cartes.
            _cache.TryRemove(configuration, out _);
            throw;
        }
    }
}

/// <summary>Carte telle qu'envoyée au navigateur (les tableaux d'octets partent en Base64).</summary>
public sealed record CarteDto(
    uint Graine,
    int Largeur,
    int Hauteur,
    byte NiveauMer,
    IReadOnlyList<string> NomsBiomes,
    byte[] Biomes,
    byte[] Altitudes,
    byte[] Gibier,
    byte[] Plantes,
    IReadOnlyList<Emplacement> Emplacements,
    double KilometresParCase,
    double MetresParUnite)
{
    public static CarteDto Depuis(Carte carte) => new(
        (uint)carte.Graine, carte.Largeur, carte.Hauteur, Carte.NiveauMer, Simulation.Monde.Biomes.Noms,
        carte.Biomes, carte.Altitudes, carte.Gibier, carte.Plantes, carte.Emplacements,
        Simulation.Nature.Echelle.KilometresParCase, Simulation.Nature.Echelle.MetresParUnite);
}
