using Monde.Simulation.Nature;
using Monde.Simulation.Recits;

namespace Monde.Simulation;

/// <summary>
/// Les premières fois de l'histoire du monde ou d'une communauté. Certaines se reconnaissent à leur récit seul : la première
/// découverte de chaque savoir au monde, les premiers mots et la première grammaire, le premier rassemblement de deux
/// communautés, la première transe, le premier mot d'une communauté, le premier rite d'une religion, et outre-mer la première
/// terre vue, le premier pied posé et le premier contact de chaque groupe. Les autres (naissance, mort, meurtre, pardon, guerre,
/// religion, Entité, catastrophes…) ont un récit qui revient sans cesse : le monde se souvient de ce qui lui est déjà arrivé
/// (<see cref="RegistrePremieres"/>) et marque la ligne qui le raconte pour la première fois (<see cref="EvenementJournal.Premiere"/>).
/// Elles ne sont jamais effacées ni rangées dans les archives du journal.
/// </summary>
public static class PremieresFois
{
    /// <summary>Récits qui sont toujours une première fois.</summary>
    public static readonly IReadOnlyList<string> Toujours =
        ["ing.premiere", "lang.deuxMots", "lang.grammaire", "lang.premierMot.monde", "renc.debut.premier", "med.transe.monde", "lang.premierMot", "rel.rite.premier"];

    /// <summary>Récits qui ne sont une première fois que marqués significatifs (la mer : seule la première fois l'est).</summary>
    public static readonly IReadOnlyList<string> SiMarquants = ["mer.voit.terre", "mer.voit.feux", "mer.signe.objet", "mer.pied", "mer.contact"];

    /// <summary>Marque d'une ligne : première fois au monde.</summary>
    public const byte AuMonde = 1;

    /// <summary>Marque d'une ligne : première fois pour sa communauté (ou l'autre communauté, pour une guerre ou une paix).</summary>
    public const byte PourLaCommunaute = 2;

    /// <summary>Cet événement est-il une première fois ?</summary>
    public static bool Est(EvenementJournal e) =>
        e.Premiere != 0 || e.Recit is { } r && (Toujours.Contains(r.Cle) || e.Significatif && SiMarquants.Contains(r.Cle));

    /// <summary>Types d'événements qui peuvent porter une première fois du registre (pour relire un ancien journal).</summary>
    public static readonly IReadOnlyList<TypeEvenement> Types =
    [
        TypeEvenement.Naissance, TypeEvenement.Mort, TypeEvenement.Conflit, TypeEvenement.Accouplement, TypeEvenement.Attirance,
        TypeEvenement.Union, TypeEvenement.Decouverte, TypeEvenement.Guerre, TypeEvenement.Religion, TypeEvenement.Parole,
        TypeEvenement.Geste, TypeEvenement.Catastrophe, TypeEvenement.Communaute, TypeEvenement.Medecine,
    ];

    /// <summary>
    /// Ce dont la ligne est peut-être la première fois : la sorte (« naissance », « mort.Faim », « cat.Seisme »…), et si elle compte
    /// pour le monde, pour la communauté de la ligne, et pour l'autre communauté. <paramref name="catastrophe"/> : le type de la
    /// catastrophe qui porte ce numéro d'événement (celui qui l'annonce), s'il y en a une.
    /// </summary>
    public static IEnumerable<(string Sorte, bool Monde, bool Communaute, bool Autre)> Sortes(EvenementJournal e,
        Func<long, TypeCatastrophe?> catastrophe)
    {
        if (e.Recit is not { } r) yield break;
        var cle = r.Cle;
        switch (e.Type)
        {
            case TypeEvenement.Naissance when cle == "j.naissance":
                yield return ("naissance", true, true, false);
                break;
            case TypeEvenement.Mort when cle != "extinction.annonce":
                yield return ("mort", true, true, false);
                yield return ($"mort.{CauseDeMort(r)}", true, false, false);
                if (cle == "j.mort.meurtre") yield return ("meurtre", true, true, false);
                break;
            case TypeEvenement.Conflit when cle.StartsWith("j.pardon.", StringComparison.Ordinal):
                yield return ("pardon", true, true, false);
                break;
            case TypeEvenement.Union when cle == "j.couple.memeSexe":
                yield return ("memeSexe", true, true, false);
                break;
            case TypeEvenement.Decouverte when r["savoir"] is Recit { Cle: var s } && (s.StartsWith("savoir.Flamme", StringComparison.Ordinal) || s == "savoir.Foyer"):
                // La découverte du feu au monde est déjà « ing.premiere » : ici, le premier de la communauté qui sait faire ou garder le feu.
                yield return ("feu", false, true, false);
                break;
            case TypeEvenement.Guerre when cle == "guerre.raid":
                yield return ("guerre", true, true, true);
                break;
            case TypeEvenement.Guerre when cle == "guerre.paix":
                yield return ("paix", true, true, true);
                break;
            case TypeEvenement.Religion:
                if (cle == "rel.naissance") yield return ("religion", true, true, false);
                else if (cle == "rel.convertit") yield return ("religion", false, true, false);
                else if (cle.StartsWith("rel.prophete.", StringComparison.Ordinal)) yield return ("prophete", true, true, false);
                else if (cle == "pri.exaucee" || cle == "pric.exauce") yield return ("priere.exaucee", true, false, false);
                else if (EstPriere(cle)) yield return ("priere", true, true, false);
                break;
            case TypeEvenement.Parole:
                if (cle.StartsWith("ent.recoit.", StringComparison.Ordinal)) yield return ("entite.parole", true, true, false);
                else if (cle is "ent.obeit" or "ent.moitie" or "ent.hesite" or "ent.refuse") yield return ("entite.crue", true, false, false);
                else if (cle == "mandat.recu") yield return ("entite.groupe", true, true, false);
                break;
            case TypeEvenement.Geste:
                yield return ("entite.geste", true, false, false);
                break;
            case TypeEvenement.Decouverte when cle.StartsWith("champ.", StringComparison.Ordinal):
                // Les champs (0.33.0) : premier semis, première récolte, premier semis sur le limon ou la cendre, première lignée domestiquée.
                if (cle is "champ.seme" or "champ.seme.limon" or "champ.seme.cendres") yield return ("semis", true, true, false);
                if (cle == "champ.seme.limon") yield return ("decrue", true, true, false);
                else if (cle == "champ.seme.cendres") yield return ("essart", true, true, false);
                else if (cle == "champ.recolte") yield return ("recolte", true, true, false);
                else if (cle.StartsWith("champ.lignee.", StringComparison.Ordinal)) yield return ("domestication", true, true, false);
                break;
            case TypeEvenement.Decouverte when cle.StartsWith("bete.", StringComparison.Ordinal):
                // Les bêtes (0.33.1) : premier petit élevé, premiers chiots de rôdeurs, première naissance au camp, première traite,
                // première bête domestiquée.
                if (cle is "bete.petit" or "bete.chiots") yield return ("apprivoise", true, true, false);
                else if (cle == "bete.naissance") yield return ("naissanceCaptive", true, true, false);
                else if (cle == "bete.traite") yield return ("traite", true, true, false);
                else if (cle == "bete.domestique") yield return ("domestiqueBete", true, true, false);
                break;
            case TypeEvenement.Communaute when cle == "limon.installe":
                yield return ("campCrue", true, true, false);
                break;
            case TypeEvenement.Medecine when cle == "seq.fixe.Jambe":
                yield return ("boiteux", true, false, false);
                break;
            case TypeEvenement.Nature when cle is "relief.grimpe" or "relief.desescalade":
                yield return ("paroi", true, true, false);
                break;
            case TypeEvenement.Catastrophe:
                // L'annonce d'une catastrophe porte son numéro ; ce qui en découle (morts, blessés, frayeur) la cite comme cause.
                if (catastrophe(e.Id) is { } annonce) yield return ($"cat.{annonce}", true, false, false);
                else if (e.CommunauteId != 0 && e.CauseId != 0 && catastrophe(e.CauseId) is { } t) yield return ($"cat.{t}", false, true, false);
                break;
        }
        // L'union hors du couple se dit dans une phrase d'accouplement ou d'amour (« (hors de leur couple) »).
        if (e.Type is TypeEvenement.Accouplement or TypeEvenement.Attirance && r["hors"] is Recit { Cle: "j.horsCouple" })
            yield return ("horsCouple", true, true, false);
    }

    /// <summary>La cause d'une mort, telle que le récit la dit.</summary>
    private static string CauseDeMort(Recit r) => r.Cle switch
    {
        "j.mort" when r["cause"] is Recit { Cle: var c } && c.StartsWith("j.cause.", StringComparison.Ordinal) => c["j.cause.".Length..],
        var c when c.StartsWith("j.mort.", StringComparison.Ordinal) => c["j.mort.".Length..],
        var c when c.StartsWith("cat.", StringComparison.Ordinal) => "Catastrophe",
        var c when c.StartsWith("guerre.", StringComparison.Ordinal) => "Guerre",
        var c => c,
    };

    /// <summary>Une prière d'un humain (« pri.guerir », « pri.pluie »…) ou d'une communauté (« pric.prie »).</summary>
    private static bool EstPriere(string cle) => cle == "pric.prie" || cle is "pri.guerir" or "pri.sauver" or "pri.manger" or "pri.pluie"
        or "pri.accalmie" or "pri.enfant" or "pri.protection" or "pri.mourir" or "pri.lesSiens" or "pri.souffrir" or "pri.amour" or "pri.merci";
}

/// <summary>
/// Ce qui est déjà arrivé au monde et à chaque communauté (« m:naissance », « c:12:guerre »). La marque d'une ligne ne dépend que
/// des lignes d'avant, versées dans l'ordre de leurs numéros, et ne tire aucun hasard : la simulation n'en est pas changée.
/// </summary>
public sealed class RegistrePremieres
{
    private readonly HashSet<string> _passees;
    private readonly List<string> _nonEnregistrees = [];

    public RegistrePremieres(IEnumerable<string>? passees = null) => _passees = new HashSet<string>(passees ?? [], StringComparer.Ordinal);

    /// <summary>Tout ce qui est déjà arrivé.</summary>
    public IReadOnlyCollection<string> Passees => _passees;

    /// <summary>Ce qui est arrivé depuis la dernière sauvegarde, dans l'ordre.</summary>
    public IReadOnlyList<string> NonEnregistrees => _nonEnregistrees;

    public void Enregistrees(int nombre) => _nonEnregistrees.RemoveRange(0, Math.Min(nombre, _nonEnregistrees.Count));

    /// <summary>La ligne, avec sa marque si elle raconte une première fois (sinon telle quelle).</summary>
    public EvenementJournal Marquer(EvenementJournal e, Func<long, TypeCatastrophe?> catastrophe)
    {
        byte marque = 0;
        foreach (var (sorte, monde, communaute, autre) in PremieresFois.Sortes(e, catastrophe))
        {
            if (monde && Noter($"m:{sorte}")) marque |= PremieresFois.AuMonde;
            if (communaute && e.CommunauteId != 0 && Noter($"c:{e.CommunauteId}:{sorte}")) marque |= PremieresFois.PourLaCommunaute;
            if (autre && e.AutreCommunauteId != 0 && Noter($"c:{e.AutreCommunauteId}:{sorte}")) marque |= PremieresFois.PourLaCommunaute;
        }
        return marque == 0 ? e : e with { Premiere = (byte)(e.Premiere | marque) };
    }

    /// <summary>Retient ce qui est arrivé sans rien marquer (un ancien journal relu).</summary>
    public void Relire(EvenementJournal e, Func<long, TypeCatastrophe?> catastrophe) => Marquer(e, catastrophe);

    private bool Noter(string cle)
    {
        if (!_passees.Add(cle)) return false;
        _nonEnregistrees.Add(cle);
        return true;
    }
}
