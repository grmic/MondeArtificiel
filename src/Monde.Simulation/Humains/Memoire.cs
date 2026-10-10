namespace Monde.Simulation.Humains;

public enum TypeLieu : byte
{
    Eau = 0,
    Nourriture = 1,
    Abri = 2,
}

/// <summary>Un lieu dont l'humain se souvient, et quand il l'a vu pour la dernière fois.</summary>
public sealed record LieuConnu(TypeLieu Type, int X, int Y, long VuLe);

/// <summary>Un humain déjà côtoyé : combien de jours passés près de lui, et la dernière fois.</summary>
public sealed record Proche(int Id, int Rencontres, long Derniere);

/// <summary>Une pensée, écrite pour l'observateur : les humains n'ont pas encore de langage.</summary>
public sealed record Pensee(long Secondes, string Texte);

/// <summary>
/// Mémoire d'un humain : les lieux utiles qu'il a trouvés, les humains qu'il côtoie et ses dernières pensées.
/// Elle est bornée (on oublie les lieux anciens, les liens les plus faibles et les pensées les plus vieilles).
/// </summary>
public sealed class Memoire
{
    public const int LieuxParType = 5;
    public const int ProchesMax = 30;
    public const int PenseesMax = 20;

    /// <summary>Un lieu qui n'a pas été revu depuis deux ans est oublié.</summary>
    public const long DureeSouvenirLieu = 2 * Temps.Calendrier.SecondesParAn;

    public List<LieuConnu> Lieux { get; init; } = [];
    public List<Proche> Proches { get; init; } = [];
    public List<Pensee> Pensees { get; init; } = [];

    public Memoire Copier() => new() { Lieux = [.. Lieux], Proches = [.. Proches], Pensees = [.. Pensees] };

    /// <summary>Retient (ou rafraîchit) un lieu. Si la place manque, le plus ancien de ce type est oublié.</summary>
    public void Retenir(TypeLieu type, int x, int y, long maintenant)
    {
        // Deux lieux à moins de 3 cases l'un de l'autre sont le même endroit.
        var i = Lieux.FindIndex(l => l.Type == type && Math.Max(Math.Abs(l.X - x), Math.Abs(l.Y - y)) < 3);
        if (i >= 0)
        {
            Lieux[i] = new LieuConnu(type, x, y, maintenant);
            return;
        }
        var memeType = Lieux.Where(l => l.Type == type).ToList();
        if (memeType.Count >= LieuxParType) Lieux.Remove(memeType.MinBy(l => l.VuLe)!);
        Lieux.Add(new LieuConnu(type, x, y, maintenant));
    }

    public void Oublier(LieuConnu lieu) => Lieux.Remove(lieu);

    /// <summary>Lieu connu le plus proche d'un type, encore en mémoire, ou null.</summary>
    public LieuConnu? PlusProche(TypeLieu type, int x, int y, long maintenant)
    {
        LieuConnu? meilleur = null;
        var meilleure = int.MaxValue;
        foreach (var l in Lieux)
        {
            if (l.Type != type || maintenant - l.VuLe > DureeSouvenirLieu) continue;
            var d = Math.Max(Math.Abs(l.X - x), Math.Abs(l.Y - y));
            if (d < meilleure) (meilleure, meilleur) = (d, l);
        }
        return meilleur;
    }

    /// <summary>Note une journée passée près d'un autre humain.</summary>
    public void Cotoyer(int id, long maintenant)
    {
        var i = Proches.FindIndex(p => p.Id == id);
        if (i >= 0)
        {
            Proches[i] = Proches[i] with { Rencontres = Proches[i].Rencontres + 1, Derniere = maintenant };
            return;
        }
        if (Proches.Count >= ProchesMax)
        {
            // On oublie le lien le plus faible (le plus ancien à égalité).
            Proches.Remove(Proches.OrderBy(p => p.Rencontres).ThenBy(p => p.Derniere).First());
        }
        Proches.Add(new Proche(id, 1, maintenant));
    }

    public void Penser(long maintenant, string texte)
    {
        if (Pensees.Count > 0 && Pensees[^1].Texte == texte) return; // pas de répétition immédiate
        if (Pensees.Count >= PenseesMax) Pensees.RemoveAt(0);
        Pensees.Add(new Pensee(maintenant, texte));
    }
}
