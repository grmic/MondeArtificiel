using System.Globalization;
using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>
/// Un nœud de l'arbre des savoirs : une trouvaille du monde, la première fois qu'elle a été faite, et les savoirs dont elle est née.
/// </summary>
/// <param name="Cle">Identité de la trouvaille (effet et structure).</param>
/// <param name="Profondeur">0 pour une trouvaille sans parents connus ; sinon un de plus que le plus profond de ses parents.</param>
/// <param name="Detenteurs">Vivants qui la savent (dans la communauté choisie) ; 0 : perdue.</param>
public sealed record NoeudArbreDto(string Cle, string Nom, string Date, long Secondes, int Humain, string NomHumain, string Origine, bool Entite,
    int Detenteurs, long Evenement, IReadOnlyList<string> Parents, int Profondeur);

/// <summary>L'arbre des savoirs du monde ou d'une communauté, dessiné après coup, et le possible adjacent.</summary>
/// <param name="Adjacents">Quelques combinaisons à un pas de ce qu'ils savent, que personne n'a encore trouvées (sans dire si elles marcheraient).</param>
/// <param name="TotalAdjacents">Nombre de ces combinaisons.</param>
public sealed record ArbreDto(IReadOnlyList<NoeudArbreDto> Noeuds, IReadOnlyList<string> Adjacents, int TotalAdjacents);

/// <summary>Ce que l'Entité peut proposer de combiner à un humain.</summary>
public sealed record ChoixCombinaisonDto(IReadOnlyList<ChoixDto> Pieces, IReadOnlyList<ChoixDto> Operations, IReadOnlyList<ChoixDto> Liens);

/// <summary>
/// Demande de l'Entité : une opération (la lettre d'une façon ou d'une jointure, voir <see cref="Facture"/>), une pièce, la seconde
/// pièce pour une jointure, et le lien pour une jointure qui en demande un (numéro de matière, 0 : aucun).
/// </summary>
public sealed record DemandeCombinaison(int Humain, string Operation, string A, string? B, int Lien = 0);

public static class Arbre
{
    public static ArbreDto Construire(ServiceMonde service, int communaute)
    {
        var monde = service.Monde;
        var (_, humains) = monde.InstantaneHumains();
        var (_, _, decouvertes) = monde.Installations();
        var portee = humains.Where(h => h.EstVivant && (communaute == 0 || h.CommunauteId == communaute)).ToList();
        var detenteurs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var h in portee)
            foreach (var cle in h.Savoirs.Where(s => s.Effet != Effet.Rien && s.Confiance >= 0.4).Select(s => Comportement.CleDeSavoir(s.Effet, s.Pieces)).Distinct())
                detenteurs[cle] = detenteurs.GetValueOrDefault(cle) + 1;
        var profondeurs = new Dictionary<string, int>(StringComparer.Ordinal);
        var noeuds = new List<NoeudArbreDto>();
        foreach (var d in decouvertes.OrderBy(d => d.Le))
        {
            var cle = Comportement.CleDeSavoir(d.Effet, d.Pieces);
            var n = detenteurs.GetValueOrDefault(cle);
            // Pour une communauté : ce qu'elle a trouvé, et ce que ses membres savent.
            if (communaute != 0 && n == 0 && d.CommunauteId != communaute) continue;
            var parents = d.Parents.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(profondeurs.ContainsKey).Distinct().ToList();
            var profondeur = parents.Count == 0 ? 0 : 1 + parents.Max(p => profondeurs[p]);
            profondeurs.TryAdd(cle, profondeur);
            noeuds.Add(new NoeudArbreDto(cle, L.Rediger(Physique.NomDecouverte(d.Effet, d.Pieces)), L.Date(d.Le), d.Le, d.HumainId,
                d.HumainId >= 1 && d.HumainId <= humains.Count ? humains[d.HumainId - 1].Nom : "?", SavoirsDto.Origine(d.Origine),
                d.Origine == OrigineSavoir.Entite, n, d.EvenementId, parents, profondeur));
        }
        var (total, exemples) = monde.Adjacents(communaute);
        return new ArbreDto(noeuds, exemples.Select(f => L.Rediger(Facons.Decrire(f))).ToList(), total);
    }

    /// <summary>Pièces auxquelles l'humain peut penser, toutes les façons et jointures, et les liens qu'il a sous la main.</summary>
    public static ChoixCombinaisonDto Choix(ServiceMonde service, int humain)
    {
        if (service.Monde.PossiblesPour(humain) is not { } p) return new([], [], []);
        var pieces = p.Pieces.Select(r => Facture.Lire(r)).Where(f => f is not null)
            .Select(f => new ChoixDto(f!.Ecrire(), L.Rediger(Facons.Decrire(f)))).ToList();
        var operations = p.Facons.Select(f => new ChoixDto(Facture.Lettre(f).ToString(), L.T($"fac.op.{f}")))
            .Concat(p.Jointures.Select(j => new ChoixDto(Facture.Lettre(j).ToString(), L.T($"fac.op.{j}")))).ToList();
        var liens = p.Liens.Distinct().Select(m => new ChoixDto(((int)m).ToString(CultureInfo.InvariantCulture), L.T($"matiere.{m}"))).ToList();
        return new ChoixCombinaisonDto(pieces, operations, liens);
    }

    /// <summary>Écrit la recette demandée, ou null si elle est invalide (lettre inconnue, pièce illisible, trop compliquée).</summary>
    public static string? Recette(DemandeCombinaison d)
    {
        if (string.IsNullOrEmpty(d.Operation) || d.Operation.Length != 1 || Facture.Lire(d.A) is not { } a) return null;
        var c = d.Operation[0];
        string recette;
        if (char.IsAsciiLetterUpper(c)) recette = $"{c}({a.Ecrire()})";
        else
        {
            if (Facture.Lire(d.B) is not { } b || d.Lien < 0) return null;
            recette = string.Create(CultureInfo.InvariantCulture, $"{c}{d.Lien}({a.Ecrire()},{b.Ecrire()})");
        }
        return Facture.Lire(recette)?.Ecrire();
    }
}
