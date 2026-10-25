using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Rubrique « Journal et taille de la base » de la Configuration : les réglages, la taille du monde et son rythme de
/// croissance mesurés (null avant la première mesure), les lignes du journal dans la table et dans les archives compressées.
/// </summary>
public sealed record JournalBaseDto(
    int ConserverAns, int QuotidienAns, bool EffacerMarquants, bool EffacerSuivis, bool Compresser, int CompresserAns, int AnsMax,
    long? Octets, long? OctetsParAn, long? Table, long? Archivees, int Suivis, long AnActuel, string DossierAnalyses)
{
    public static JournalBaseDto Depuis(ServiceMonde service)
    {
        var r = service.ReglagesJournal;
        var m = service.MesureJournal;
        return new(r.ConserverAns, r.QuotidienAns, r.EffacerMarquants, r.EffacerSuivis, r.Compresser, r.CompresserAns, ReglagesJournal.AnsMax,
            m?.Octets, m?.OctetsParAn(service.PeriodeMesure), m?.Table, m?.Archivees, service.Depot.LireSuivis().Count,
            Calendrier.Decomposer(service.Monde.Secondes).Annee, service.DossierAnalyses);
    }
}
