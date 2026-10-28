namespace Monde.Simulation;

/// <summary>
/// Les premières fois de l'histoire du monde ou d'une communauté, reconnues à leur récit : la première découverte de chaque
/// savoir au monde, les premiers mots et la première grammaire, le premier rassemblement de deux communautés, la première
/// transe, le premier mot d'une communauté, le premier rite d'une religion, et outre-mer la première terre vue, le premier
/// pied posé et le premier contact de chaque groupe. Elles ne sont jamais effacées ni rangées dans les archives du journal.
/// </summary>
public static class PremieresFois
{
    /// <summary>Récits qui sont toujours une première fois.</summary>
    public static readonly IReadOnlyList<string> Toujours =
        ["ing.premiere", "lang.deuxMots", "lang.grammaire", "lang.premierMot.monde", "renc.debut.premier", "med.transe.monde", "lang.premierMot", "rel.rite.premier"];

    /// <summary>Récits qui ne sont une première fois que marqués significatifs (la mer : seule la première fois l'est).</summary>
    public static readonly IReadOnlyList<string> SiMarquants = ["mer.voit.terre", "mer.voit.feux", "mer.signe.objet", "mer.pied", "mer.contact"];

    /// <summary>Cet événement est-il une première fois ?</summary>
    public static bool Est(EvenementJournal e) =>
        e.Recit is { } r && (Toujours.Contains(r.Cle) || e.Significatif && SiMarquants.Contains(r.Cle));
}
