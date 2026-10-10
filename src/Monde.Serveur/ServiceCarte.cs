using System.Collections.Concurrent;
using Monde.Simulation.Monde;

namespace Monde.Serveur;

/// <summary>
/// Fournit les cartes générées. Une carte est calculée une seule fois par graine,
/// et le cache est borné pour ne pas grossir indéfiniment.
/// </summary>
public sealed class ServiceCarte(ServiceMonde service)
{
    private const int TailleMaxCache = 8;
    private readonly ConcurrentDictionary<ulong, Lazy<Carte>> _cache = new();

    /// <summary>Graine du monde courant (fixée à sa création, enregistrée dans sa sauvegarde).</summary>
    public uint GraineParDefaut { get; } = (uint)service.Monde.Graine;

    public Carte Obtenir(uint? graine = null)
    {
        var cle = (ulong)(graine ?? GraineParDefaut);
        if (cle == service.Monde.Graine) return service.Monde.Carte;
        if (_cache.Count >= TailleMaxCache && !_cache.ContainsKey(cle))
        {
            // Garde seulement la carte par défaut quand le cache est plein.
            foreach (var autre in _cache.Keys.Where(k => k != GraineParDefaut))
            {
                _cache.TryRemove(autre, out _);
            }
        }
        return _cache.GetOrAdd(cle, g => new Lazy<Carte>(() => GenerateurContinent.Generer(g))).Value;
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
    IReadOnlyList<Emplacement> Emplacements)
{
    public static CarteDto Depuis(Carte carte) => new(
        (uint)carte.Graine, carte.Largeur, carte.Hauteur, Carte.NiveauMer, Simulation.Monde.Biomes.Noms,
        carte.Biomes, carte.Altitudes, carte.Gibier, carte.Plantes, carte.Emplacements);
}
