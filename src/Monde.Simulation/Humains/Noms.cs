using Monde.Simulation.Monde;

namespace Monde.Simulation.Humains;

/// <summary>Fabrique des noms de deux ou trois syllabes, tous différents.</summary>
public static class Noms
{
    private static readonly string[] Attaques = ["k", "t", "m", "n", "r", "s", "l", "v", "d", "b", "g", "h", "z", "j", "p", "f", "sh", "th"];
    private static readonly string[] Voyelles = ["a", "e", "i", "o", "u", "ai", "ei", "ou", "ia", "ae"];
    private static readonly string[] Finales = ["", "", "", "n", "r", "l", "k", "s", "m"];

    public static List<string> Generer(Alea alea, int nombre)
    {
        var deja = new HashSet<string>();
        return Enumerable.Range(0, nombre).Select(_ => Nouveau(alea, deja)).ToList();
    }

    /// <summary>Un nom qui n'est pas encore dans <paramref name="deja"/> (il y est ajouté).</summary>
    public static string Nouveau(Alea alea, ISet<string> deja)
    {
        while (true)
        {
            var syllabes = 2 + alea.Entier(2);
            var nom = "";
            for (var i = 0; i < syllabes; i++)
            {
                nom += Attaques[alea.Entier(Attaques.Length)] + Voyelles[alea.Entier(Voyelles.Length)];
            }
            nom += Finales[alea.Entier(Finales.Length)];
            nom = char.ToUpperInvariant(nom[0]) + nom[1..];
            if (deja.Add(nom)) return nom;
        }
    }

    /// <summary>Un mot court (sons pris dans le même répertoire que les noms), en minuscules.</summary>
    public static string Mot(Alea alea, int syllabes)
    {
        var mot = "";
        for (var i = 0; i < syllabes; i++) mot += Attaques[alea.Entier(Attaques.Length)] + Voyelles[alea.Entier(Voyelles.Length)];
        return mot;
    }
}
