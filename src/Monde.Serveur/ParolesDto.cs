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

/// <summary>
/// Une parole de l'Entité adressée à toute une communauté, envers d'autres (<see cref="Cibles"/>) ou vers une direction
/// (<see cref="Direction"/>, de 0 à 7, pour « Partez vers… »).
/// </summary>
public sealed record DemandeParoleCommunaute(int Communaute, string Parole, string Mode, int[]? Cibles, int Direction = 0);

/// <summary>Une communauté que l'on peut viser : distance entre les camps et relation avec celle qui reçoit la parole.</summary>
public sealed record CibleCommunauteDto(string Cle, string Libelle, string Phrase, int Km, string Relation, string Code);

/// <summary>Tout ce qu'il faut pour composer une parole adressée à une communauté.</summary>
public sealed record ChoixParolesCommunauteDto(IReadOnlyList<ParoleDto> Paroles, IReadOnlyList<ChoixDto> Modes, IReadOnlyList<ChoixDto> Directions,
    IReadOnlyList<CibleCommunauteDto> Communautes);

/// <summary>Ce que chaque adulte a fait de la parole, et ce que le groupe en fera (une ligne par communauté visée).</summary>
public sealed record ResultatParoleCommunauteDto(IReadOnlyList<ResultatParoleDto> Membres, IReadOnlyList<string> Groupe);

/// <summary>
/// Une parole adressée à une communauté et ce qu'elle en a fait. <see cref="Visee"/> : la communauté de la fiche est celle
/// que la parole vise (elle a été adressée à <see cref="Communaute"/>).
/// </summary>
public sealed record MandatDto(int Id, string Le, string Mode, string Phrase, int CibleId, bool Visee, int CommunauteId, string? Communaute,
    string Etat, string Code, string Reception, long Evenement);

/// <summary>Emprise de la figure sur les siens : l'autorité qu'on lui reconnaît, la crainte qu'elle inspire, et la chance qu'elle tranche seule (%).</summary>
public sealed record AutoriteDto(int Figure, string NomFigure, string Autorite, string Crainte, int Emprise, bool Tyran);

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
        var paroles = Enum.GetValues<Parole>().Where(p => p != Parole.Penchant && !Paroles.Collective(p)).Select(p => new ParoleDto(p.ToString(), L.T($"parole.{p}"),
            Lib.RubriqueParole(p), Paroles.VeutCible(p), p switch
            {
                Parole.PartsVers => "direction",
                Parole.PlanteDangereuse => "plante",
                Parole.NeVaPasALaGuerre => "communaute",
                _ => null,
            }, Lib.Phrase(p, "{cible}", "{extra}", "{extra}"), Paroles.Nuit(p))).ToList();
        var modes = new[] { ModeParole.Reve, ModeParole.Voix, ModeParole.Signe, ModeParole.Apparition }
            .Select(m => new ChoixDto(m.ToString(), L.T($"modeParole.{m}"))).ToList();
        var directions = Paroles.Directions.Select((_, i) => new ChoixDto(i.ToString(System.Globalization.CultureInfo.InvariantCulture), L.T($"direction.{i}"))).ToList();
        var plantes = monde.Flore.Especes.Select(e => new ChoixDto(e.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Nom)).ToList();
        var communautes = monde.Communautes().Where(c => c.Active)
            .Select(c => new ChoixDto(c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), L.Communaute(c.Nom))).ToList();
        return new ChoixParolesDto(paroles, modes, directions, plantes, communautes);
    }

    /// <summary>Paroles qu'on peut adresser à la communauté <paramref name="id"/>, et les autres communautés actives qu'elles peuvent viser.</summary>
    public static ChoixParolesCommunauteDto? ChoixCommunaute(ServiceMonde service, int id)
    {
        var monde = service.Monde;
        var communautes = monde.Communautes();
        if (id < 1 || id > communautes.Count || !communautes[id - 1].Active) return null;
        var a = communautes[id - 1];
        var paroles = Enum.GetValues<Parole>().Where(Paroles.Collective).Select(p => new ParoleDto(p.ToString(), L.T($"parole.{p}"),
            L.T(p switch
            {
                Parole.FaitesLaGuerre or Parole.ChassezDeLeursTerres or Parole.Exterminez => "rubriqueParole.groupe.guerre",
                Parole.FaitesLaPaix or Parole.AlliezVous or Parole.UnissezVous => "rubriqueParole.groupe.paix",
                Parole.PartezVers => "rubriqueParole.groupe.partir",
                _ => "rubriqueParole.groupe.donner",
            }), false, p == Parole.PartezVers ? "direction" : "communautes", L.T($"phrase.{p}", ("extra", "{extra}")), Paroles.NuitAuxAutres(p))).ToList();
        var modes = new[] { ModeParole.Reve, ModeParole.Voix, ModeParole.Signe, ModeParole.Apparition }
            .Select(m => new ChoixDto(m.ToString(), L.T($"modeParole.{m}"))).ToList();
        var directions = Paroles.Directions.Select((_, i) => new ChoixDto(i.ToString(System.Globalization.CultureInfo.InvariantCulture), L.T($"direction.{i}"))).ToList();
        var relations = monde.Relations();
        var cibles = communautes.Where(c => c.Active && c.Id != id).Select(c =>
        {
            var r = relations.FirstOrDefault(x => x.A == Math.Min(id, c.Id) && x.B == Math.Max(id, c.Id));
            var etat = r?.Etat ?? EtatRelation.Indifferentes;
            return new CibleCommunauteDto(c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), L.Communaute(c.Nom), L.CommunautePhrase(c.Nom),
                (int)Math.Round(Monde.Simulation.Nature.Echelle.Kilometres(a.CentreX, a.CentreY, c.CentreX, c.CentreY)),
                CommunauteDto.Etat(etat, r is { PacteEvenementId: > 0 }), etat.ToString());
        }).OrderBy(c => c.Km).ToList();
        return new ChoixParolesCommunauteDto(paroles, modes, directions, cibles);
    }

    /// <summary>Paroles adressées à la communauté <paramref name="id"/> ou qui la visent, de la plus récente à la plus ancienne.</summary>
    public static List<MandatDto> Mandats(ServiceMonde service, int id)
    {
        var monde = service.Monde;
        var communautes = monde.Communautes();
        string? Nom(int c) => c >= 1 && c <= communautes.Count ? L.CommunautePhrase(communautes[c - 1].Nom) : null;
        return monde.Mandats().Where(m => m.CommunauteId == id || m.CibleId == id).OrderByDescending(m => m.Id).Take(40).Select(m =>
        {
            var extra = m.Parole == Parole.PartezVers ? L.T($"direction.{m.Direction}")
                : m.CibleId >= 1 && m.CibleId <= communautes.Count ? L.CommunautePhrase(communautes[m.CibleId - 1].Nom) : null;
            var details = new List<string>();
            if (m.Moitie) details.Add(L.T("etatMandat.moitie"));
            if (m.Fideles) details.Add(L.T("etatMandat.fideles"));
            if (m.Impose) details.Add(L.T("etatMandat.impose"));
            var etat = L.T($"etatMandat.{m.Etat}", ("date", L.Date(m.Etat == EtatMandat.Debat ? m.DebatLe : m.EtatLe)))
                       + (details.Count == 0 ? "" : L.T("commun.parentheses", ("texte", string.Join(L.T("commun.virgule"), details))));
            var reception = L.T("mandat.reception", ("v", m.Veulent), ("hes", m.Hesitent), ("r", m.Refusent), ("i", m.Incredules + m.Incompris));
            return new MandatDto(m.Id, L.Date(m.Le), L.T($"modeParole.{m.Mode}"), L.T("commun.guillemets", ("texte", L.T($"phrase.{m.Parole}", ("extra", extra)))),
                m.CibleId, m.CibleId == id && m.CommunauteId != id, m.CommunauteId, Nom(m.CommunauteId), etat, m.Etat.ToString(), reception,
                m.EtatEvenementId != 0 ? m.EtatEvenementId : m.EvenementId);
        }).ToList();
    }

    /// <summary>Autorité et crainte de la figure de la communauté <paramref name="id"/> (null : pas de figure).</summary>
    public static AutoriteDto? Autorite(ServiceMonde service, int id)
    {
        var monde = service.Monde;
        var communautes = monde.Communautes();
        if (id < 1 || id > communautes.Count || !communautes[id - 1].Active) return null;
        var c = communautes[id - 1];
        var (maintenant, humains) = monde.InstantaneHumains();
        if (c.FigureId < 1 || c.FigureId > humains.Count) return null;
        var autorite = VieSociale.Autorite(c, humains, maintenant);
        var crainte = VieSociale.Crainte(c, humains, maintenant);
        var emprise = VieSociale.Emprise(c, humains, maintenant);
        return new AutoriteDto(c.FigureId, humains[c.FigureId - 1].Nom,
            L.T(autorite switch { < 0.35 => "autorite.faible", < 0.6 => "autorite.moyenne", < 0.75 => "autorite.forte", _ => "autorite.tresForte" }),
            L.T(crainte switch { < 0.25 => "crainte.aucune", < 0.5 => "crainte.redoutee", _ => "crainte.tyran" }, ("g", humains[c.FigureId - 1].Sexe == Sexe.Femme ? "f" : "m")),
            (int)Math.Round(100 * emprise), crainte >= 0.5);
    }

    public static List<HumainTrouveDto> Chercher(ServiceMonde service, FiltreHumains f)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var noms = monde.Communautes().ToDictionary(c => c.Id, c => L.Communaute(c.Nom));
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
                texte = L.T(i.Force >= 0.4 ? "penchant.passion" : "penchant.penchant", ("nom", Nom(i.CibleId)))
                        + (i.Orientation ? L.T("penchant.coeurChange") : "")
                        + (i.Reponse == Reponse.Ressenti ? (maintenant < i.Jusqua
                            ? L.T("penchant.sEfface", ("v", (int)Math.Round(100 * i.ForceA(maintenant) / Math.Max(0.01, i.Force))))
                            : L.T("penchant.efface", ("g", i.Force >= 0.4 ? "f" : "m"))) : "");
            }
            else
            {
                string? extra = i.Parole switch
                {
                    Parole.PartsVers => L.T("phrase.labas"),
                    Parole.PlanteDangereuse when i.Param >= 1 && i.Param <= monde.Flore.Especes.Count => monde.Flore.Get(i.Param).Nom,
                    Parole.NeVaPasALaGuerre when i.Param > 0 => communautes.FirstOrDefault(c => c.Id == i.Param)?.Nom,
                    Parole.PartezVers => L.T($"direction.{Math.Clamp(i.Param, 0, 7)}"),
                    _ when Paroles.Collective(i.Parole) => communautes.FirstOrDefault(c => c.Id == i.Param) is { } vc ? L.CommunautePhrase(vc.Nom) : null,
                    _ => null,
                };
                texte = L.T("commun.guillemets", ("texte", Lib.Phrase(i.Parole, Nom(i.CibleId), extra, i.Parole == Parole.NeVaPasALaGuerre ? extra?.ToLowerInvariant() : extra, h.Sexe == Sexe.Femme)));
            }
            return new ParoleRecueDto(L.Date(i.Le), L.T($"modeParole.{i.Mode}"), texte, L.T($"reponse.{i.Reponse}"), i.CibleId, i.EvenementId);
        }).ToList();
    }
}
