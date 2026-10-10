using Monde.Simulation.Monde;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ressources actuelles de chaque tuile, en centièmes (0 à 10 000), entières pour rester
/// exactement reproductibles. Elles diminuent quand on les consomme et repoussent vers le niveau de la carte.
/// </summary>
public sealed class Ecosysteme
{
    private readonly Carte _carte;

    public Ecosysteme(Carte carte, ushort[]? gibier = null, ushort[]? plantes = null)
    {
        _carte = carte;
        Gibier = gibier ?? carte.Gibier.Select(v => (ushort)(v * 100)).ToArray();
        Plantes = plantes ?? carte.Plantes.Select(v => (ushort)(v * 100)).ToArray();
        if (Gibier.Length != carte.Gibier.Length || Plantes.Length != carte.Plantes.Length)
        {
            throw new ArgumentException("Les ressources sauvegardées ne correspondent pas à la taille de la carte.");
        }
    }

    public ushort[] Gibier { get; }
    public ushort[] Plantes { get; }

    /// <summary>Repousse : chaque tuile regagne 1/500 de l'écart avec son niveau naturel (appelé une fois par heure du monde).</summary>
    public void Repousser()
    {
        for (var i = 0; i < Gibier.Length; i++)
        {
            Gibier[i] = Rapprocher(Gibier[i], _carte.Gibier[i] * 100);
            Plantes[i] = Rapprocher(Plantes[i], _carte.Plantes[i] * 100);
        }
    }

    private static ushort Rapprocher(ushort actuel, int cible)
    {
        if (actuel >= cible) return actuel;
        var pas = Math.Max(1, (cible - actuel) / 500);
        return (ushort)Math.Min(cible, actuel + pas);
    }

    /// <summary>Prélève une quantité (en unités 0 à 100) et renvoie ce qui a vraiment été pris.</summary>
    public static int Prelever(ushort[] ressource, int indice, int quantite)
    {
        var pris = Math.Min(ressource[indice], quantite * 100);
        ressource[indice] -= (ushort)pris;
        return pris / 100;
    }
}
