using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Une parole proposée à l'Entité : sa clé, son libellé, sa rubrique, ce qu'elle demande de préciser et son modèle de phrase.</summary>
/// <param name="Precision">Ce qu'il faut choisir en plus : « direction », « plante », « communaute » ou null.</param>
/// <param name="Modele">La phrase, avec {cible} et {extra} à remplacer.</param>
public sealed record ParoleDto(string Cle, string Libelle, string Rubrique, bool Cible, string? Precision, string Modele, bool Nuit);

/// <summary>Tout ce qu'il faut pour composer une parole de l'Entité.</summary>
public sealed record ChoixParolesDto(IReadOnlyList<ParoleDto> Paroles, IReadOnlyList<ChoixDto> Modes, IReadOnlyList<ChoixDto> Directions,
    IReadOnlyList<ChoixDto> Plantes, IReadOnlyList<ChoixDto> Communautes);

/// <summary>
/// Une parole de l'Entité, adressée à des humains et/ou à tous les membres de communautés. <see cref="Cible"/> : la personne
/// désignée (0 : aucune) ; <see cref="Param"/> : direction (0 à 7), espèce de plante, ou communauté (0 : toute guerre).
/// </summary>
public sealed record DemandeParole(int[]? Humains, int[]? Communautes, string Parole, string Mode, int Cible = 0, int Param = 0);

/// <summary>Un penchant amoureux inspiré par l'Entité : pour <see cref="Cible"/>, ou pour qui le monde choisit (0).</summary>
public sealed record DemandePenchant(int[]? Humains, int[]? Communautes, int Cible = 0, bool Mutuel = false, bool Passion = false, bool Orientation = false);

/// <summary>Ce qu'un humain a fait de ce qu'il a reçu.</summary>
public sealed record ResultatParoleDto(int Humain, string Nom, string Resultat);

/// <summary>Critères de recherche d'un humain à désigner.</summary>
/// <param name="Texte">Début ou partie du nom (40 caractères au plus).</param>
/// <param name="Communaute">Communauté (0 : toutes).</param>
/// <param name="Pres">Humain auprès de qui chercher (0 : partout).</param>
/// <param name="KarmaMin">Karma au moins égal (null : sans limite) ; <paramref name="KarmaMax"/> : au plus égal.</param>
public sealed record FiltreHumains(string? Texte, int Communaute = 0, bool Seuls = false, bool Adultes = false, string? Sexe = null, int Pres = 0,
    double? KarmaMin = null, double? KarmaMax = null);

/// <summary>Un humain trouvé.</summary>
public sealed record HumainTrouveDto(int Id, string Nom, string Sexe, int Age, string? Communaute, bool Seul, int DistanceKm, double Karma = 0);

/// <summary>Une parole ou un penchant reçus, pour la fiche.</summary>
public sealed record ParoleRecueDto(string Date, string Mode, string Texte, string Reponse, int Cible, long Evenement);

public static class ParolesEntite
{
    /// <summary>Recherche limitée aux 40 premiers résultats.</summary>
    public const int Resultats = 40;

    public static ChoixParolesDto Choix(ServiceMonde service)
    {
        var monde = service.Monde;
        var paroles = Enum.GetValues<Parole>().Where(p => p != Parole.Penchant).Select(p => new ParoleDto(p.ToString(), Paroles.Libelle(p),
            Paroles.Rubrique(p), Paroles.VeutCible(p), p switch
            {
                Parole.PartsVers => "direction",
                Parole.PlanteDangereuse => "plante",
                Parole.NeVaPasALaGuerre => "communaute",
                _ => null,
            }, Paroles.Phrase(p, "{cible}", "{extra}"), Paroles.Nuit(p))).ToList();
        var modes = new[] { ModeParole.Reve, ModeParole.Voix, ModeParole.Signe, ModeParole.Apparition }
            .Select(m => new ChoixDto(m.ToString(), Paroles.NomMode(m))).ToList();
        var directions = Paroles.Directions.Select((d, i) => new ChoixDto(i.ToString(System.Globalization.CultureInfo.InvariantCulture), d)).ToList();
        var plantes = monde.Flore.Especes.Select(e => new ChoixDto(e.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Nom)).ToList();
        var communautes = monde.Communautes().Where(c => c.Active)
            .Select(c => new ChoixDto(c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Nom)).ToList();
        return new ChoixParolesDto(paroles, modes, directions, plantes, communautes);
    }

    public static List<HumainTrouveDto> Chercher(ServiceMonde service, FiltreHumains f)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var noms = monde.Communautes().ToDictionary(c => c.Id, c => c.Nom);
        var texte = (f.Texte ?? "").Trim();
        if (texte.Length > 40) texte = texte[..40];
        var pres = f.Pres >= 1 && f.Pres <= humains.Count ? humains[f.Pres - 1] : null;
        int Km(Humain h) => pres is null ? 0 : (int)Math.Round(Monde.Simulation.Nature.Echelle.Kilometres(h.X, h.Y, pres.X, pres.Y));
        return humains.Where(h => h.EstVivant && !h.EstBebe(maintenant)
                && (texte.Length == 0 || h.Nom.Contains(texte, StringComparison.CurrentCultureIgnoreCase))
                && (f.Communaute == 0 || h.CommunauteId == f.Communaute)
                && (!f.Seuls || h.PartenaireId == 0 || !humains[h.PartenaireId - 1].EstVivant)
                && (!f.Adultes || !h.EstEnfant(maintenant))
                && (f.Sexe is not ("femme" or "homme") || (h.Sexe == Sexe.Femme) == (f.Sexe == "femme"))
                && (f.KarmaMin is not { } kmin || !double.IsFinite(kmin) || h.Karma >= kmin)
                && (f.KarmaMax is not { } kmax || !double.IsFinite(kmax) || h.Karma <= kmax))
            .OrderBy(h => pres is null ? 0 : Km(h)).ThenBy(h => h.Nom, StringComparer.CurrentCulture)
            .Take(Resultats)
            .Select(h => new HumainTrouveDto(h.Id, h.Nom, h.Sexe == Sexe.Femme ? "femme" : "homme", (int)h.AgeEnAnnees(maintenant),
                noms.GetValueOrDefault(h.CommunauteId), h.PartenaireId == 0 || !humains[h.PartenaireId - 1].EstVivant, Km(h), Math.Round(h.Karma, 1)))
            .ToList();
    }

    /// <summary>Paroles et penchants reçus par un humain, du plus récent au plus ancien.</summary>
    public static List<ParoleRecueDto> Recues(Humain h, IReadOnlyList<Humain> humains, ServiceMonde service, long maintenant)
    {
        var monde = service.Monde;
        string? Nom(int i) => i >= 1 && i <= humains.Count ? humains[i - 1].Nom : null;
        var communautes = monde.Communautes();
        return h.Impulsions.AsEnumerable().Reverse().Select(i =>
        {
            string texte;
            if (i.Parole == Parole.Penchant)
            {
                var force = i.Force >= 0.4 ? "Une passion" : "Un penchant";
                texte = $"{force} pour {Nom(i.CibleId)}{(i.Orientation ? " (son cœur a changé)" : "")}"
                        + (i.Reponse == Reponse.Ressenti ? (maintenant < i.Jusqua ? $", qui s'efface ({(int)Math.Round(100 * i.ForceA(maintenant) / Math.Max(0.01, i.Force))} % de sa force)" : ", effacé") : "");
            }
            else
            {
                string? extra = i.Parole switch
                {
                    Parole.PartsVers => null,
                    Parole.PlanteDangereuse when i.Param >= 1 && i.Param <= monde.Flore.Especes.Count => monde.Flore.Get(i.Param).Nom,
                    Parole.NeVaPasALaGuerre when i.Param > 0 => communautes.FirstOrDefault(c => c.Id == i.Param)?.Nom.ToLowerInvariant(),
                    _ => null,
                };
                texte = "« " + Paroles.Phrase(i.Parole, Nom(i.CibleId), i.Parole == Parole.PartsVers ? "là-bas" : extra, h.Sexe == Sexe.Femme) + " »";
            }
            return new ParoleRecueDto(Calendrier.Decomposer(i.Le).ToString(), Paroles.NomMode(i.Mode), texte, Paroles.NomReponse(i.Reponse), i.CibleId, i.EvenementId);
        }).ToList();
    }
}
