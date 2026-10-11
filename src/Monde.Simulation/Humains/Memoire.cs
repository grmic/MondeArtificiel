using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

public enum TypeLieu : byte
{
    Eau = 0,
    Nourriture = 1,
    Abri = 2,
}

/// <summary>Un lieu dont l'humain se souvient.</summary>
/// <param name="VuLe">Dernière visite.</param>
/// <param name="Rappels">Nombre de jours distincts où il y est revenu : plus il y revient, moins il l'oublie.</param>
/// <param name="DecuLe">Dernière fois qu'il y est allé pour rien (ressource épuisée), ou null.</param>
public sealed record LieuConnu(TypeLieu Type, int X, int Y, long VuLe, int Rappels = 0, long? DecuLe = null);

/// <summary>Une pensée, datée.</summary>
public sealed record Pensee(long Secondes, string Texte);

/// <summary>
/// Mémoire d'un humain : lieux utiles et dernières pensées. Elle est propre à chacun :
/// la place et la vitesse d'oubli dépendent de son aptitude à retenir (voir <see cref="Intelligence.Memoire"/>).
/// <para>
/// L'oubli suit la courbe d'Ebbinghaus : un souvenir s'efface avec le temps, de moins en moins vite
/// à chaque fois qu'il est rappelé (effet d'espacement). Un abri où l'on dort chaque saison
/// peut ainsi ne jamais être oublié, un point d'eau vu une seule fois s'oublie en quelques semaines.
/// </para>
/// </summary>
public sealed class Memoire
{
    public const int PenseesMax = 20;

    /// <summary>Stabilité d'un souvenir vu une seule fois, pour une mémoire moyenne (durée de vie moyenne, en jours).</summary>
    public const double StabiliteDeBaseJours = 30;

    /// <summary>Après une déception, le lieu n'est plus visé pendant ce temps (la ressource repousse).</summary>
    public const long DureeDeception = 3 * Calendrier.SecondesParJour;

    public List<LieuConnu> Lieux { get; init; } = [];
    public List<Pensee> Pensees { get; init; } = [];

    /// <summary>Le jardin secret (voir <see cref="Humains.Jardin"/>).</summary>
    public List<Intime> Jardin { get; init; } = [];

    public Memoire Copier() => new() { Lieux = [.. Lieux], Pensees = [.. Pensees], Jardin = [.. Jardin] };

    /// <summary>Facteur de mémoire : ×2 tous les 15 points de QI de mémoire (×0,25 à 70, ×4 à 130).</summary>
    public static double Facteur(double aptitude) => Math.Pow(2, (aptitude - 100) / 15);

    /// <summary>Nombre de lieux de chaque type qu'on peut garder en tête : 5 pour une mémoire moyenne, de 2 à 12 selon l'individu.</summary>
    public static int Capacite(double aptitude) => Math.Clamp((int)Math.Round(5 * Math.Sqrt(Facteur(aptitude)), MidpointRounding.AwayFromZero), 2, 12);

    /// <summary>Durée de vie moyenne d'un souvenir, en jours.</summary>
    public static double Stabilite(LieuConnu lieu, double aptitude) => StabiliteDeBaseJours * Facteur(aptitude) * Math.Pow(1 + lieu.Rappels, 1.5);

    /// <summary>Retient (ou rafraîchit) un lieu. Si la place manque, le souvenir le plus faible de ce type est oublié.</summary>
    public void Retenir(TypeLieu type, int x, int y, long maintenant, double aptitude = 100)
    {
        // Deux lieux à moins de 3 cases l'un de l'autre sont le même endroit.
        var i = Lieux.FindIndex(l => l.Type == type && Math.Max(Math.Abs(l.X - x), Math.Abs(l.Y - y)) < 3);
        if (i >= 0)
        {
            var l = Lieux[i];
            // Un rappel compte une fois par jour au plus : c'est le retour espacé qui renforce le souvenir.
            var rappel = maintenant - l.VuLe >= Calendrier.SecondesParJour / 2 ? 1 : 0;
            Lieux[i] = new LieuConnu(type, x, y, maintenant, l.Rappels + rappel);
            return;
        }
        var memeType = Lieux.Where(l => l.Type == type).ToList();
        if (memeType.Count >= Capacite(aptitude)) Lieux.Remove(memeType.MinBy(l => (l.Rappels, l.VuLe))!);
        Lieux.Add(new LieuConnu(type, x, y, maintenant));
    }

    /// <summary>Arrivé sur un lieu connu sans rien y trouver : un souvenir faible s'efface, un souvenir solide s'affaiblit.</summary>
    public void Decevoir(LieuConnu lieu, long maintenant)
    {
        var i = Lieux.IndexOf(lieu);
        if (i < 0) return;
        if (lieu.Rappels >= 3) Lieux[i] = lieu with { Rappels = lieu.Rappels - 2, DecuLe = maintenant };
        else Lieux.RemoveAt(i);
    }

    public void Oublier(LieuConnu lieu) => Lieux.Remove(lieu);

    /// <summary>Lieu connu le plus proche d'un type, hors des lieux qui viennent de décevoir, ou null.</summary>
    public LieuConnu? PlusProche(TypeLieu type, int x, int y, long maintenant)
    {
        LieuConnu? meilleur = null;
        var meilleure = int.MaxValue;
        foreach (var l in Lieux)
        {
            if (l.Type != type || maintenant - l.DecuLe < DureeDeception) continue;
            var d = Math.Max(Math.Abs(l.X - x), Math.Abs(l.Y - y));
            if (d < meilleure) (meilleure, meilleur) = (d, l);
        }
        return meilleur;
    }

    /// <summary>
    /// Une fois par jour : chaque souvenir peut s'effacer, avec la probabilité 1 − e^(−1/S),
    /// S étant sa stabilité en jours. Renvoie les lieux oubliés (pour le journal).
    /// </summary>
    public List<LieuConnu> OublierAvecLeTemps(double aptitude, Alea alea)
    {
        var oublies = new List<LieuConnu>();
        for (var i = Lieux.Count - 1; i >= 0; i--)
        {
            if (alea.Reel() < 1 - Math.Exp(-1 / Stabilite(Lieux[i], aptitude)))
            {
                oublies.Add(Lieux[i]);
                Lieux.RemoveAt(i);
            }
        }
        return oublies;
    }

    /// <summary>Ajoute une pensée ; renvoie faux si c'est la même que la précédente (pas de répétition immédiate).</summary>
    public bool Penser(long maintenant, string texte)
    {
        if (Pensees.Count > 0 && Pensees[^1].Texte == texte) return false;
        if (Pensees.Count >= PenseesMax) Pensees.RemoveAt(0);
        Pensees.Add(new Pensee(maintenant, texte));
        return true;
    }

    /// <summary>
    /// Vrai si une pensée de même sens figure parmi les <paramref name="n"/> dernières. La direction n'entre pas
    /// dans la comparaison : « Je vois de l'eau vers le nord » et « … vers l'est » disent la même chose.
    /// </summary>
    public bool PenseeRecente(string texte, int n)
    {
        var sens = Sens(texte);
        return Pensees.Skip(Math.Max(0, Pensees.Count - n)).Any(p => Sens(p.Texte) == sens);
    }

    private static string Sens(string texte)
    {
        var i = texte.IndexOf(" vers le ", StringComparison.Ordinal);
        if (i < 0) i = texte.IndexOf(" ici", StringComparison.Ordinal);
        if (i < 0) return texte;
        var fin = texte.IndexOfAny(['.', '?', '!'], i);
        return fin < 0 ? texte[..i] : texte[..i] + texte[fin..];
    }
}
