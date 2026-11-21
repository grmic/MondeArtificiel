using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>
/// Une révélation qu'un humain garde en mémoire (0.42.9) : le texte, la fidélité de son souvenir (en %, et en mots), qui la lui a
/// racontée (null : l'Entité elle-même), depuis quand, et s'il y croit.
/// </summary>
public sealed record RevelationConnueDto(int Id, string Texte, int Fidelite, string Souvenir, string? DeQui, int DeQuiId, string Date, bool Croit);

/// <summary>
/// Une révélation de l'Entité, et ce qu'elle est devenue : combien la savent et y croient aujourd'hui, la fidélité moyenne de leurs
/// souvenirs, les communautés où elle s'est répandue, et quand elle s'est perdue (null : on s'en souvient).
/// </summary>
public sealed record RevelationDto(int Id, string Date, string Texte, string A, string Mode, string Auteur, int Savent, int Croient, int Fidelite,
    IReadOnlyList<string> Repandue, string? Perdue);

/// <summary>Ce que l'Entité révèle à une communauté (<see cref="Communaute"/> &gt; 0) ou au Monde entier (0), et comment.</summary>
public sealed record DemandeRevelation(string Texte, string Mode, int Communaute = 0);

/// <summary>Ce qu'il est advenu d'une révélation : combien l'ont entendue, comprise, crue.</summary>
public sealed record ResultatRevelationDto(RevelationDto Revelation, int Entendu, int Compris, int Crus, string Texte);

public static class RevelationsDto
{
    /// <summary>La fidélité d'un souvenir, en mots.</summary>
    public static string Souvenir(double f) => L.T(f switch
    {
        >= 0.85 => "rev.fid.fidele",
        >= 0.6 => "rev.fid.peu",
        >= 0.35 => "rev.fid.deforme",
        _ => "rev.fid.vague",
    });

    /// <summary>Ce que <paramref name="h"/> sait de ce que l'Entité a révélé, du plus récent au plus ancien.</summary>
    public static List<RevelationConnueDto> Connues(MondeSimule monde, Humain h, IReadOnlyList<Humain> humains)
    {
        var revelations = monde.Revelations();
        var liste = new List<RevelationConnueDto>();
        foreach (var c in h.Revelations.OrderByDescending(c => c.Le))
        {
            if (c.Id < 1 || c.Id > revelations.Count) continue;
            var de = c.DeQui >= 1 && c.DeQui <= humains.Count ? humains[c.DeQui - 1].Nom : null;
            liste.Add(new RevelationConnueDto(c.Id, revelations[c.Id - 1].Texte, (int)Math.Round(100 * c.Fidelite), Souvenir(c.Fidelite), de, c.DeQui,
                L.Date(c.Le), c.Croit));
        }
        return liste;
    }

    /// <summary>Toutes les révélations, de la plus récente à la plus ancienne.</summary>
    public static List<RevelationDto> Liste(ServiceMonde service)
    {
        var monde = service.Monde;
        var savoirs = monde.SavoirsDesRevelations();
        return monde.Revelations().AsEnumerable().Reverse().Select(r => Dto(monde, r, savoirs)).ToList();
    }

    public static RevelationDto Dto(MondeSimule monde, Revelation r, Dictionary<int, (int Savent, int Croient, double Fidelite)>? savoirs = null)
    {
        savoirs ??= monde.SavoirsDesRevelations();
        var (savent, croient, fidelite) = savoirs.GetValueOrDefault(r.Id);
        var communautes = monde.Communautes();
        string NomDe(int id) => communautes.FirstOrDefault(k => k.Id == id) is { } k ? L.Communaute(k.Nom) : "?";
        var (_, humains) = monde.InstantaneHumains();
        var a = r.Portee switch
        {
            PorteeRevelation.Humain => r.Destinataire >= 1 && r.Destinataire <= humains.Count ? humains[r.Destinataire - 1].Nom : "?",
            PorteeRevelation.Communaute => NomDe(r.Destinataire),
            _ => L.T("rev.a.monde"),
        };
        return new RevelationDto(r.Id, L.Date(r.Le), r.Texte, a, L.T($"modeParole.{r.Mode}"), L.T(r.Auteur == 1 ? "rev.auteur.llm" : "rev.auteur.joueur"),
            savent, croient, (int)Math.Round(100 * fidelite), r.Repandue.Select(NomDe).ToList(), r.PerdueLe == 0 ? null : L.Date(r.PerdueLe));
    }

    /// <summary>L'Entité révèle quelque chose à une communauté ou au Monde.</summary>
    public static ResultatRevelationDto Reveler(MondeSimule monde, DemandeRevelation d, int auteur = 0)
    {
        if (!Enum.TryParse<ModeParole>(d.Mode, true, out var mode)) throw new ArgumentException(L.T("rev.erreur.mode"));
        if (string.IsNullOrWhiteSpace(d.Texte)) throw new ArgumentException(L.T("rev.erreur.vide"));
        (Revelation r, int entendu, int compris, int crus, OrdreRevele? ordre) x;
        try
        {
            x = monde.Reveler(d.Communaute, d.Texte, mode, auteur);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException(L.T(e.Message == "mode" ? "rev.erreur.mode" : "rev.erreur.personne"));
        }
        var (r, entendu, compris, crus, ordre) = x;
        var texte = L.T("rev.resultat", ("n", entendu), ("compris", compris), ("crus", crus));
        if (ordre is not null)
            texte += " " + L.T("rev.ordre", ("x", ordre.Nom), ("v", ordre.Veulent), ("hes", ordre.Hesitent), ("r", ordre.Refusent));
        return new ResultatRevelationDto(Dto(monde, r), entendu, compris, crus, texte);
    }
}
