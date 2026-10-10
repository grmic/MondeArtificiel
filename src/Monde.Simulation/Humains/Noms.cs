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
        var noms = new List<string>(nombre);
        var deja = new HashSet<string>();
        while (noms.Count < nombre)
        {
            var syllabes = 2 + alea.Entier(2);
            var nom = "";
            for (var i = 0; i < syllabes; i++)
            {
                nom += Attaques[alea.Entier(Attaques.Length)] + Voyelles[alea.Entier(Voyelles.Length)];
            }
            nom += Finales[alea.Entier(Finales.Length)];
            nom = char.ToUpperInvariant(nom[0]) + nom[1..];
            if (deja.Add(nom)) noms.Add(nom);
        }
        return noms;
    }
}
