using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>Un foyer sur la carte : il flambe, ou couve sous la cendre (couvert), ou n'est plus qu'une braise.</summary>
public sealed record FeuVueDto(int X, int Y, bool Couve);

/// <summary>Une réserve sur la carte : de l'eau, ou de la nourriture, et ce que c'est en mots.</summary>
public sealed record ReserveVueDto(int X, int Y, bool Eau, string Texte);

/// <summary>Feux et réserves du monde, pour la carte.</summary>
public sealed record FeuxDto(IReadOnlyList<FeuVueDto> Feux, IReadOnlyList<ReserveVueDto> Reserves)
{
    public static FeuxDto Depuis(MondeSimule monde)
    {
        var (foyers, _, _) = monde.Installations();
        var (braises, reserves) = monde.BraisesEtReserves();
        var feux = foyers.Select(f => new FeuVueDto(f.X, f.Y, f.Couvert))
            .Concat(braises.Select(b => new FeuVueDto(b.X, b.Y, true))).ToList();
        var vues = reserves.Where(r => r.Kg > 0)
            .Select(r => new ReserveVueDto(r.X, r.Y, r.Sorte == SorteReserve.Eau, Comportement.DecrireReserve(r.Sorte, r.Kg))).ToList();
        return new FeuxDto(feux, vues);
    }
}
