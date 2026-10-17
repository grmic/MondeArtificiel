using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un mot, avec sa traduction : la forme (ou le signe décrit), sa force chez qui le connaît (0 à 100), quand et de qui il vient.</summary>
public sealed record MotDto(string Sens, string Forme, bool Signe, int Force, string Depuis, int Auteur, string? NomAuteur);

/// <summary>Une langue que l'humain comprend en partie (part de ses mots qui s'y disent pareil).</summary>
public sealed record LangueCompriseDto(int Id, string Nom, int Pourcent);

/// <summary>
/// La parole d'un humain : son stade, son nom dans sa langue (qui le lui a donné, quand, ce qu'il veut dire), ses mots les plus sûrs,
/// l'ordre des mots qu'il suit, et les autres langues qu'il comprend.
/// </summary>
public sealed record LangageDto(string Stade, string? Nom, string? Sens, string? NommeLe, int NommePar, string? NomPar, long NomEvenement,
    int NombreDeMots, IReadOnlyList<MotDto> Mots, string? Ordre, IReadOnlyList<LangueCompriseDto> Comprises)
{
    public static LangageDto Depuis(Humain h, IReadOnlyList<Humain> humains, IReadOnlyList<Communaute> communautes, Flore flore)
    {
        string? Nom(int id) => id >= 1 && id <= humains.Count ? humains[id - 1].Nom : null;
        var comprises = new List<LangueCompriseDto>();
        if (h.Vocabulaire.Count > 0)
        {
            foreach (var c in communautes)
            {
                if (!c.Active || c.Id == h.CommunauteId || c.Lexique.Count + c.Signes.Count < 10) continue;
                var pareils = h.Vocabulaire.Count(m => (m.Signe ? c.Signes : c.Lexique).TryGetValue(m.Idee, out var f) && f == m.Forme);
                var part = (int)Math.Round(100.0 * pareils / Math.Max(10, c.Lexique.Count + c.Signes.Count));
                if (part >= 20) comprises.Add(new LangueCompriseDto(c.Id, c.Nom, part));
            }
        }
        return new LangageDto(Langues.DireStade(h.StadeParole), h.NomLangue, h.NomSens,
            h.NomLangueLe > 0 ? Calendrier.Decomposer(h.NomLangueLe).ToString() : null, h.NomParId, Nom(h.NomParId), h.NomEvenementId,
            h.Vocabulaire.Count,
            h.Vocabulaire.OrderByDescending(m => m.Force).Take(40)
                .Select(m => new MotDto(Idees.Libelle(m.Idee, flore), Langues.Dire(m.Forme, m.Signe), m.Signe, (int)Math.Round(100 * m.Force),
                    Calendrier.Decomposer(m.Le).ToString(), m.AuteurId, Nom(m.AuteurId)))
                .ToList(),
            h.StadeParole >= 3 ? Langues.DireOrdre(h.Ordre) : null,
            comprises.OrderByDescending(x => x.Pourcent).ToList());
    }
}

/// <summary>Un changement dans l'histoire d'une langue, en phrase, avec l'événement du journal.</summary>
public sealed record ChangementLangueDto(string Date, string Texte, long Evenement);

/// <summary>Un membre qui parle aussi une autre langue : il peut servir d'interprète.</summary>
public sealed record InterpreteDto(int Id, string Nom, int Langue, string NomLangue);

/// <summary>
/// La langue d'une communauté : parlée, signée ou les deux ; son stade, son ordre des mots ; ses mots (avec leur traduction,
/// quand et par qui ils sont venus) ; son histoire ; ce qu'elle partage avec les autres langues ; ses interprètes.
/// </summary>
public sealed record LangueDto(string Mode, string Stade, string? Ordre, string? PremierMot, string? Assemblage, string? OrdreFixe,
    int NombreDeMots, IReadOnlyList<MotDto> Lexique, IReadOnlyList<ChangementLangueDto> Histoire, IReadOnlyList<LangueCompriseDto> Proches,
    IReadOnlyList<InterpreteDto> Interpretes, string? DialecteDe = null)
{
    private static string Date(long s) => Calendrier.Decomposer(s).ToString();

    public static LangueDto Depuis(Communaute c, IReadOnlyList<Communaute> toutes, IReadOnlyList<Humain> humains, IEnumerable<Humain> membres, Flore flore)
    {
        string? Nom(int id) => id >= 1 && id <= humains.Count ? humains[id - 1].Nom : null;
        var total = c.Lexique.Count + c.Signes.Count;
        var mode = total == 0 ? "des gestes et des mimes"
            : c.Signes.Count * 5 >= total * 3 ? "surtout signée, avec les mains"
            : c.Signes.Count * 5 >= total ? "parlée et signée"
            : "parlée";
        // Le dernier changement de chaque mot dit d'où vient sa forme actuelle.
        var origines = new Dictionary<string, HistoireMot>();
        foreach (var x in c.HistoireLangue) origines[x.Idee] = x;
        MotDto Mot(string idee, string forme, bool signe) => origines.TryGetValue(idee, out var o)
            ? new MotDto(Idees.Libelle(idee, flore), Langues.Dire(forme, signe), signe, 100, Date(o.Le), o.AuteurId, Nom(o.AuteurId))
            : new MotDto(Idees.Libelle(idee, flore), Langues.Dire(forme, signe), signe, 100, "", 0, null);
        var lexique = c.Lexique.Select(m => Mot(m.Key, m.Value, false)).Concat(c.Signes.Select(m => Mot(m.Key, m.Value, true)))
            .OrderBy(m => m.Sens, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), true)).ToList();
        var proches = toutes.Where(o => o.Id != c.Id && (o.Active || !c.Active) && o.Lexique.Count + o.Signes.Count > 0)
            .Select(o => new LangueCompriseDto(o.Id, o.Nom, (int)Math.Round(100 * VieSociale.Communs(c, o))))
            .Where(x => x.Pourcent >= 10).OrderByDescending(x => x.Pourcent).Take(8).ToList();
        return new LangueDto(mode, Langues.DireStade(c.Stade), c.Stade >= 3 ? Langues.DireOrdre(c.Ordre) : null,
            c.ParoleLe > 0 ? Date(c.ParoleLe) : null, c.CombinaisonLe > 0 ? Date(c.CombinaisonLe) : null, c.OrdreLe > 0 ? Date(c.OrdreLe) : null,
            total, lexique,
            c.HistoireLangue.AsEnumerable().Reverse().Take(150).Select(x => new ChangementLangueDto(Date(x.Le), Decrire(x, toutes, flore), x.EvenementId)).ToList(),
            proches,
            membres.Where(m => m.EstVivant && m.LangueSecondeId > 0 && m.LangueSecondeId <= toutes.Count)
                .Select(m => new InterpreteDto(m.Id, m.Nom, m.LangueSecondeId, toutes[m.LangueSecondeId - 1].Nom)).ToList(),
            c.LangueId > 0 && c.LangueId != c.Id && c.LangueId <= toutes.Count ? toutes[c.LangueId - 1].Nom : null);
    }

    private static string Decrire(HistoireMot x, IReadOnlyList<Communaute> toutes, Flore flore)
    {
        var sens = Idees.Libelle(x.Idee, flore);
        var dit = Langues.Dire(x.Forme, x.Signe);
        return x.Type switch
        {
            ChangementMot.Deforme => $"« {sens} » se dit désormais « {dit} » (avant : « {Avant(x)} »)",
            ChangementMot.Emprunte when x.SourceId >= 1 && x.SourceId <= toutes.Count => $"« {sens} » : « {dit} », pris à la {Minuscule(toutes[x.SourceId - 1].Nom)}",
            ChangementMot.Emprunte => $"« {sens} » : « {dit} », pris à une autre langue",
            ChangementMot.Remplace => $"« {sens} » : « {dit} » remplace « {Avant(x)} »",
            ChangementMot.Oublie => $"« {sens} » : le mot « {dit} » s'est perdu",
            _ => $"« {sens} » : « {dit} »",
        };
    }

    // La forme remplacée peut être un signe alors que la nouvelle est un mot, ou l'inverse : un signe s'écrit avec des points.
    private static string Avant(HistoireMot x) => x.Ancienne is { } a ? Langues.Dire(a, a.Contains('.')) : "";

    private static string Minuscule(string nom) => char.ToLowerInvariant(nom[0]) + nom[1..];
}
