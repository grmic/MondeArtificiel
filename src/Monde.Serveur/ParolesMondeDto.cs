using System.Globalization;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Recits;

namespace Monde.Serveur;

/// <summary>Une terre où vivent des humains, que l'Entité peut viser seule (0.37.1).</summary>
public sealed record TerreDto(int Masse, string Libelle, int Vivants);

/// <summary>Tout ce qu'il faut pour composer une parole au Monde : paroles, manières, plantes, communautés et terres.</summary>
public sealed record ChoixParolesMondeDto(IReadOnlyList<ParoleDto> Paroles, IReadOnlyList<ChoixDto> Modes, IReadOnlyList<ChoixDto> Plantes,
    IReadOnlyList<ChoixDto> Communautes, IReadOnlyList<TerreDto> Terres, int Vivants);

/// <summary>
/// Une parole de l'Entité au Monde entier (<see cref="Terre"/> 0) ou à une terre. <see cref="Cible"/> : la personne désignée
/// (« Écoutez… ») ; <see cref="Communaute"/> : la communauté visée (guerre, paix, accueil) ; <see cref="Param"/> : la plante.
/// </summary>
public sealed record DemandeParoleMonde(string Parole, string Mode, int Terre = 0, int Cible = 0, int Communaute = 0, int Param = 0);

/// <summary>Ce que le Monde a fait de la parole : une ligne par communauté, et quelques-uns de ceux qui l'ont reçue.</summary>
public sealed record ResultatParoleMondeDto(IReadOnlyList<string> Bilans, IReadOnlyList<ResultatParoleDto> Membres, int Entendue, long Evenement);

/// <summary>Une parole au Monde, pour l'historique et la fiche Communauté.</summary>
public sealed record ParoleMondeDto(int Id, string Le, string Mode, string Phrase, string Portee, string Auteur, IReadOnlyList<string> Bilans,
    long Evenement, bool Visee = false);

public static class ParolesMonde
{
    public static ChoixParolesMondeDto Choix(ServiceMonde service)
    {
        var monde = service.Monde;
        var paroles = Enum.GetValues<Parole>().Where(Paroles.AuMonde).Select(p => new ParoleDto(p.ToString(), L.T($"paroleMonde.{p}"),
            L.T(p switch
            {
                Parole.JeSuisCelleQuiVousAFaits => "rubriqueParole.reveler",
                Parole.CatastropheArrive or Parole.PlanteDangereuse => "rubriqueParole.avertir",
                Parole.NAiePasPeur or Parole.TuNEsPasSeul => "rubriqueParole.rassurer",
                Parole.Ecoute => "rubriqueParole.designer",
                Parole.FaitesLaGuerre or Parole.FaitesLaPaix or Parole.AccueillezLes => "rubriqueParole.monde.communaute",
                _ => "rubriqueParole.monde.commander",
            }), p == Parole.Ecoute, p switch
            {
                Parole.PlanteDangereuse => "plante",
                _ when Paroles.Collective(p) => "communaute",
                _ => null,
            }, L.T($"phrase.monde.{p}", ("cible", "{cible}"), ("extra", "{extra}"), ("c", "{extra}")), Paroles.NuitAuxAutres(p) || p == Parole.FaitesLaGuerre)).ToList();
        var modes = new[] { ModeParole.Reve, ModeParole.Voix, ModeParole.Signe, ModeParole.Apparition }
            .Select(m => new ChoixDto(m.ToString(), L.T($"modeParole.{m}"))).ToList();
        var plantes = monde.Flore.Especes.Select(e => new ChoixDto(e.Numero.ToString(CultureInfo.InvariantCulture), e.Nom)).ToList();
        var communautes = monde.Communautes().Where(c => c.Active)
            .Select(c => new ChoixDto(c.Id.ToString(CultureInfo.InvariantCulture), L.Communaute(c.Nom))).ToList();
        var terres = monde.TerresHabitees().Select(t => new TerreDto(t.Masse, Terre(t.Rang), t.Vivants)).ToList();
        return new ChoixParolesMondeDto(paroles, modes, plantes, communautes, terres, terres.Sum(t => t.Vivants));
    }

    /// <summary>« le plus grand continent », « le 2ᵉ continent », « une île ».</summary>
    public static string Terre(int rang) => rang switch
    {
        0 => L.T("monde.terre.ile"),
        1 => L.T("monde.terre.premier"),
        _ => L.T("monde.terre.continent", ("k", rang)),
    };

    /// <summary>
    /// Fait dire la parole au Monde (ou à une terre) ; lève <see cref="ArgumentException"/> si la demande est invalide. Rend la
    /// parole et ce que chacun en a fait.
    /// </summary>
    public static (ParoleMonde Parole, List<(int Humain, Recit Resultat)> Resultats) Parler(MondeSimule monde, DemandeParoleMonde d, int auteur)
    {
        if (d is null) throw new ArgumentException(L.T("erreur.demandeVide"));
        if (!Enum.TryParse<Parole>(d.Parole, true, out var parole) || !Enum.IsDefined(parole) || !Paroles.AuMonde(parole)
            || !Enum.TryParse<ModeParole>(d.Mode, true, out var mode) || !Enum.IsDefined(mode) || mode == ModeParole.Penchant)
            throw new ArgumentException(L.T("erreur.paroleMonde", ("liste", string.Join(", ", Enum.GetValues<Parole>().Where(Paroles.AuMonde)))));
        var (_, humains) = monde.InstantaneHumains();
        if (parole == Parole.Ecoute && (d.Cible < 1 || d.Cible > humains.Count || !humains[d.Cible - 1].EstVivant))
            throw new ArgumentException(L.T("erreur.humainVivant"));
        var communautes = monde.Communautes();
        if (Paroles.Collective(parole) && (d.Communaute < 1 || d.Communaute > communautes.Count || !communautes[d.Communaute - 1].Active))
            throw new ArgumentException(L.T("erreur.communaute"));
        if (parole == Parole.PlanteDangereuse && (d.Param < 1 || d.Param > monde.Flore.Especes.Count)) throw new ArgumentException(L.T("erreur.precision"));
        if (d.Terre != 0 && !monde.TerresHabitees().Any(t => t.Masse == d.Terre)) throw new ArgumentException(L.T("erreur.terre"));
        return monde.ParlerAuMonde(parole, mode, d.Terre, d.Cible, d.Communaute, d.Param, auteur) is { } r
            ? (r.Parole, r.Resultats)
            : throw new ArgumentException(L.T("erreur.paroleMonde", ("liste", string.Join(", ", Enum.GetValues<Parole>().Where(Paroles.AuMonde)))));
    }

    /// <summary>Le bilan d'une communauté (ou des isolés) en une ligne.</summary>
    public static string Bilan(BilanParoleMonde b, IReadOnlyList<Communaute> communautes)
    {
        var qui = b.CommunauteId >= 1 && b.CommunauteId <= communautes.Count ? L.Communaute(communautes[b.CommunauteId - 1].Nom) : L.T("monde.isoles");
        return L.T(b.Visee ? "monde.resultat.vise" : "monde.resultat", ("qui", qui), ("n", b.Entendu), ("compris", b.Compris), ("crus", b.Crus),
            ("v", b.Veulent), ("hes", b.Hesitent), ("r", b.Refusent));
    }

    /// <summary>La phrase dite, telle que l'observateur la lit.</summary>
    public static string Phrase(MondeSimule monde, ParoleMonde p)
    {
        var (_, humains) = monde.InstantaneHumains();
        var communautes = monde.Communautes();
        var cible = p.CibleHumain >= 1 && p.CibleHumain <= humains.Count ? humains[p.CibleHumain - 1].Nom : "…";
        string? extra = p.Parole == Parole.PlanteDangereuse && p.Param >= 1 && p.Param <= monde.Flore.Especes.Count ? monde.Flore.Get(p.Param).Nom
            : p.CibleCommunaute >= 1 && p.CibleCommunaute <= communautes.Count ? L.CommunautePhrase(communautes[p.CibleCommunaute - 1].Nom) : null;
        return L.T("commun.guillemets", ("texte", L.T($"phrase.monde.{p.Parole}", ("cible", cible), ("extra", extra), ("c", extra))));
    }

    public static ResultatParoleMondeDto Resultat(MondeSimule monde, ParoleMonde p, List<(int Humain, Recit Resultat)> r)
    {
        var (_, humains) = monde.InstantaneHumains();
        var communautes = monde.Communautes();
        return new ResultatParoleMondeDto(p.Bilans.Select(b => Bilan(b, communautes)).ToList(),
            r.Take(60).Select(x => new ResultatParoleDto(x.Humain, x.Humain >= 1 && x.Humain <= humains.Count ? humains[x.Humain - 1].Nom : "?", L.Rediger(x.Resultat))).ToList(),
            r.Count, p.EvenementId);
    }

    /// <summary>Les paroles au Monde, de la plus récente à la plus ancienne (40 au plus) ; pour une communauté, celles qui l'ont touchée.</summary>
    public static List<ParoleMondeDto> Liste(ServiceMonde service, int communaute = 0)
    {
        var monde = service.Monde;
        var communautes = monde.Communautes();
        return monde.ParolesMonde().Where(p => communaute == 0 || p.CibleCommunaute == communaute || p.Bilans.Any(b => b.CommunauteId == communaute))
            .OrderByDescending(p => p.Id).Take(40).Select(p => new ParoleMondeDto(p.Id, L.Date(p.Le), L.T($"modeParole.{p.Mode}"), Phrase(monde, p),
                p.Terre == 0 ? L.T("monde.portee.tout") : Terre(monde.RangDeTerre(p.Terre)),
                L.T(p.Auteur == 1 ? "monde.auteur.llm" : "monde.auteur.joueur"),
                p.Bilans.Where(b => communaute == 0 || b.CommunauteId == communaute).Select(b => Bilan(b, communautes)).ToList(), p.EvenementId,
                communaute != 0 && p.CibleCommunaute == communaute)).ToList();
    }
}
