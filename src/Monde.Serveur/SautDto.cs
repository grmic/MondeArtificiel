using Monde.Simulation;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un saut dans le futur en cours : part faite (0 à 1), années par minute, durée restante en secondes (null : pas encore mesurée).</summary>
public sealed record SautDto(string Cible, double Avancement, double AnsParMinute, double? ResteSecondes)
{
    public static SautDto? Depuis(SautEnCours? s) =>
        s is null ? null : new(L.Date(s.Cible), s.Avancement, s.AnsParMinute, s.Reste?.TotalSeconds);
}

/// <summary>Ce que coûterait un saut de <c>Ans</c> années à la base, au rythme mesuré du journal (null : pas encore mesuré).</summary>
public sealed record EstimationSautDto(int Ans, long Fichier, long? OctetsParAn, long? Ajout, long? LibreDisque, bool Attention)
{
    public static EstimationSautDto Depuis(ServiceMonde service, int ans)
    {
        var fichier = new FileInfo(service.Depot.CheminFichier) is { Exists: true } f ? f.Length : 0;
        var parAn = service.MesureJournal?.OctetsParAn(service.PeriodeMesure);
        var r = service.ReglagesJournal;
        long? ajout = null;
        if (parAn is { } p)
        {
            // Le journal gardé ne dépasse pas la durée de conservation ; au-delà du seuil de compression, il pèse environ dix fois moins (mesuré en 0.31.4).
            double annees = r.ConserverAns > 0 ? Math.Min(ans, r.ConserverAns) : ans;
            var pleines = r.Compresser ? Math.Min(annees, r.CompresserAns) : annees;
            ajout = (long)(p * (pleines + (annees - pleines) / 10));
        }
        long? libre = null;
        try
        {
            libre = new DriveInfo(Path.GetDirectoryName(Path.GetFullPath(service.Depot.CheminFichier))!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Disque illisible : on ne dit rien de la place libre.
        }
        return new(ans, fichier, parAn, ajout, libre, ajout is { } a && libre is { } l && a > l / 2);
    }
}

/// <summary>Une cause de mort et son nombre.</summary>
public sealed record CauseDto(string Cause, int Nombre);

/// <summary>Bilan d'un saut dans le futur, pour la fenêtre qui s'ouvre à l'arrivée ; Premier et Dernier bornent ses lignes du journal.</summary>
public sealed record BilanSautDto(string Debut, string Fin, bool Interrompu, double Ans, double Minutes, long Premier, long Dernier,
    int VivantsAvant, int VivantsApres, int Naissances, List<CauseDto> Morts, int MortsNes, int Decouvertes, int Premieres,
    int CommunautesNees, int CommunautesDisparues, int Raids, int ReligionsNees)
{
    public static BilanSautDto Depuis(BilanSaut b) => new(L.Date(b.Debut), L.Date(b.Fin), b.Interrompu,
        (b.Fin - b.Debut) / (double)Calendrier.SecondesParAn, b.Duree.TotalMinutes, b.PremierEvenement, b.DernierEvenement,
        b.VivantsAvant, b.VivantsApres, b.Naissances,
        b.Morts.OrderByDescending(m => m.Value).Select(m => new CauseDto(m.Key == Simulation.Humains.CauseDeMort.Aucune ? "" : Lib.Cause(m.Key), m.Value)).ToList(),
        b.MortsNes, b.Decouvertes, b.PremieresAuMonde, b.CommunautesNees, b.CommunautesDisparues, b.Raids, b.ReligionsNees);
}
