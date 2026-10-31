using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un savoir d'un humain : ce qu'il croit, à quel point il y croit, d'où il le tient, et à qui il l'a appris.</summary>
/// <param name="Eleves">Vivants qui le tiennent de lui (en le regardant faire ou parce qu'il le leur a montré).</param>
public sealed record SavoirDto(string Texte, int Confiance, string Origine, int Maitre, string? NomMaitre, string AppriLe, int Essais, long Evenement,
    IReadOnlyList<NommeDto>? Eleves = null);

/// <summary>Savoirs, objets portés, vêtement et aisance dans l'eau d'un humain.</summary>
public sealed record SavoirsDto(IReadOnlyList<SavoirDto> Savoirs, IReadOnlyList<string> Objets, string? Vetement, int Nage)
{
    public static string Origine(OrigineSavoir o) => o switch
    {
        OrigineSavoir.Observation or OrigineSavoir.Reflexion or OrigineSavoir.Imitation or OrigineSavoir.Entite => L.T($"origineSavoir.{o}"),
        _ => L.T("origineSavoir.Essai"),
    };

    private static string Etat(Objet o) => o.Usure <= 0.4 ? L.T("objet.use") : o.Qualite >= 0.7 ? L.T("objet.bienFait") : "";

    public static SavoirsDto Depuis(Humain h, IReadOnlyList<Humain> humains, Flore flore)
    {
        string? Nom(int i) => i >= 1 && i <= humains.Count ? humains[i - 1].Nom : null;
        List<NommeDto> Eleves(Savoir s) => humains
            .Where(o => o.EstVivant && o.Id != h.Id && o.Savoirs.Exists(x => x.MaitreId == h.Id && x.Meme(s.Geste, s.A, s.B, s.Effet, s.Espece, s.Symptome) && x.Pieces == s.Pieces))
            .Select(o => new NommeDto(o.Id, o.Nom, 0)).ToList();
        return new SavoirsDto(
            h.Savoirs.OrderByDescending(s => s.Confiance).ThenBy(s => s.AppriLe)
                .Select(s => new SavoirDto(L.Rediger(Physique.Decrire(s, s.Espece > 0 ? flore.Get(s.Espece).Dit : null)),
                    (int)Math.Round(100 * s.Confiance), Origine(s.Origine), s.MaitreId, Nom(s.MaitreId),
                    L.Date(s.AppriLe), s.Essais, s.EvenementId, Eleves(s)))
                .ToList(),
            h.Objets.Select(o => o.Matiere == Matiere.Carcasse
                ? L.T(o.Plein ? "objet.viandeFumee" : "objet.viande", ("n", Math.Max(1, Math.Round(100 * o.Qualite)))) + (o.Usure <= 0.4 ? L.T("objet.seGate") : "")
                : NomObjet(o) + (o.Plein ? o.Matiere switch { Matiere.Calebasse or Matiere.Facture => L.T("objet.pleineDEau"), Matiere.Peau => L.T("objet.outrePleine"), _ => "" } : "")
                  + Etat(o)).ToList(),
            h.Vetement is { } v ? (v.Matiere == Matiere.Peau ? L.T("vetement.peau") : L.T("vetement.feuilles")) + Etat(v) : null,
            (int)Math.Round(100 * h.Nage));
    }

    /// <summary>Nom d'un objet porté ; un objet assemblé est décrit par ses pièces.</summary>
    public static string NomObjet(Objet o) => o.Matiere is Matiere.Assemblage or Matiere.Facture ? L.Rediger(Physique.Nom(o)) : L.T($"matiere.{o.Matiere}");
}

/// <summary>Une découverte dans la chronologie du monde : la première fois qu'un effet a été obtenu.</summary>
public sealed record DecouverteDto(string Date, long Secondes, string Savoir, int Humain, string Nom, string Origine, long Evenement, int Detenteurs);

/// <summary>Un point de la courbe du progrès : savoirs vivants au début d'une année (ou maintenant, pour le dernier).</summary>
/// <param name="Reconstitue">Vrai pour une année d'avant la 0.29.1, reconstituée depuis le journal (monde entier seulement).</param>
/// <param name="ParHumain">Savoirs que sait en moyenne chaque vivant (−1 : pas mesuré).</param>
public sealed record PointProgresDto(double Annee, int Savoirs, bool Reconstitue, double ParHumain = -1);

/// <summary>Un savoir perdu (ou retrouvé) sur la courbe : l'événement du journal qui le rapporte.</summary>
public sealed record MarqueProgresDto(double Annee, string Date, long Evenement, bool Perdu, string Texte, long Cause);

/// <summary>Une communauté qu'on peut choisir pour la courbe.</summary>
public sealed record CommunauteProgresDto(int Id, string Nom, bool Active);

/// <summary>La courbe du progrès d'un monde ou d'une communauté, ses pertes et retrouvailles, et les communautés mesurées.</summary>
public sealed record ProgresDto(IReadOnlyList<PointProgresDto> Points, IReadOnlyList<MarqueProgresDto> Marques, IReadOnlyList<CommunauteProgresDto> Communautes);

/// <summary>Un choix proposé à l'Entité (geste ou matière) : clé et libellé.</summary>
public sealed record ChoixDto(string Cle, string Libelle);

/// <summary>Un geste que l'Entité peut souffler : son verbe, le mot qui relie les matières, et les matières qui lui conviennent.</summary>
public sealed record FormeGesteDto(string Cle, string Verbe, string? Liaison, IReadOnlyList<ChoixDto> A, IReadOnlyList<ChoixDto> B);

/// <summary>Ce que l'Entité peut souffler à un humain : les gestes, et les plantes qui poussent là où il se trouve.</summary>
public sealed record ChoixIntuitionDto(IReadOnlyList<FormeGesteDto> Gestes, IReadOnlyList<ChoixDto> Plantes);

/// <summary>Demande de l'Entité : un geste avec une ou deux matières, ou une plante (numéro d'espèce) à manger ou appliquer.</summary>
public sealed record DemandeIntuition(int Humain, string Geste, string A, string? B, int Espece = 0);

public static class Decouvertes
{
    /// <summary>Nom d'une découverte dans la chronologie, comme <see cref="Physique.NomDecouverte"/> : une seule roue et plusieurs roues sont deux trouvailles.</summary>
    public static string NomDecouverte(Effet e, string? pieces) => e == Effet.RouleSurAxe && Lois.Structure(pieces) == "axe:1"
        ? L.T("decouverte.brouette") : e is Effet.Facture or Effet.Remarque ? L.Rediger(Physique.NomDecouverte(e, pieces)) : L.T($"effet.{e}");

    public static List<DecouverteDto> Chronologie(ServiceMonde service)
    {
        var monde = service.Monde;
        var (_, humains) = monde.InstantaneHumains();
        var (_, _, decouvertes) = monde.Installations();
        var detenteurs = humains.Where(h => h.EstVivant).SelectMany(h => h.Savoirs.Select(s => s.Effet).Distinct())
            .GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
        return decouvertes.OrderBy(d => d.Le).Select(d => new DecouverteDto(L.Date(d.Le), d.Le,
            NomDecouverte(d.Effet, d.Pieces), d.HumainId, d.HumainId >= 1 && d.HumainId <= humains.Count ? humains[d.HumainId - 1].Nom : "?",
            SavoirsDto.Origine(d.Origine), d.EvenementId, detenteurs.GetValueOrDefault(d.Effet))).ToList();
    }

    /// <summary>
    /// Courbe du progrès : savoirs vivants au début de chaque année pour le monde (communauté 0) ou une communauté, plus le
    /// point d'aujourd'hui. Pour un monde d'avant la 0.29.1, les années du monde entier qui précèdent la première mesure sont
    /// reconstituées depuis le journal ; celles des communautés commencent à la première mesure.
    /// </summary>
    public static ProgresDto Progres(ServiceMonde service, int communaute)
    {
        var monde = service.Monde;
        var mesures = monde.Progres();
        var points = mesures.Where(m => m.CommunauteId == communaute).Select(m => new PointProgresDto(m.Annee, m.Savoirs, false, m.ParHumain)).ToList();
        var marques = new List<MarqueProgresDto>();
        if (communaute == 0)
        {
            var brutes = service.Depot.MarquesProgres();
            var premiere = points.Count == 0 ? (int)(monde.Secondes / Calendrier.SecondesParAn + 1) : (int)points[0].Annee;
            if (premiere > 1)
                points.InsertRange(0, service.Depot.ReconstituerProgres(premiere, brutes).Select(m => new PointProgresDto(m.Annee, m.Savoirs, true)));
            marques = brutes.Select(m => new MarqueProgresDto(1 + m.Secondes / (double)Calendrier.SecondesParAn, L.Date(m.Secondes),
                m.Evenement, m.Perdu, m.Recit is { } r ? L.Rediger(r) : m.Texte, m.Cause)).ToList();
        }
        var maintenant = monde.Secondes;
        var (vivants, parHumain) = monde.SavoirsVivantsMaintenant(communaute);
        points.Add(new PointProgresDto(1 + maintenant / (double)Calendrier.SecondesParAn, vivants, false, parHumain));
        var mesurees = mesures.Select(m => m.CommunauteId).ToHashSet();
        var communautes = monde.Communautes().Where(c => c.Active || mesurees.Contains(c.Id))
            .OrderByDescending(c => c.Active).ThenBy(c => c.Nom).Select(c => new CommunauteProgresDto(c.Id, L.Communaute(c.Nom), c.Active)).ToList();
        return new ProgresDto(points, marques, communautes);
    }

    /// <summary>
    /// Gestes que l'Entité peut souffler à cet humain, chacun avec les seules matières qui ont un sens pour lui, et les
    /// plantes qui poussent là où il se trouve (pour manger ou appliquer).
    /// </summary>
    public static ChoixIntuitionDto Choix(ServiceMonde service, int humain)
    {
        var monde = service.Monde;
        var (_, humains) = monde.InstantaneHumains();
        static ChoixDto Option(Geste g, Matiere m) => new(m.ToString(), m switch
        {
            Matiere.Aucune when g == Geste.Approcher => L.T("intuition.sesMains"),
            Matiere.Eau when g == Geste.Nager => L.T("intuition.eauProfonde"),
            Matiere.Aucune when Physique.SansSeconde(g) is not null => L.T("intuition.leSol"),
            Matiere.Assemblage => L.T("intuition.outilEmmanche"),
            _ => L.T($"matiere.{m}"),
        });
        var gestes = Physique.Formes.Select(f => new FormeGesteDto(f.Geste.ToString(), L.T($"forme.{f.Geste}"), f.Liaison is null ? null : L.T($"forme.{f.Geste}.liaison"),
            f.A.Select(m => Option(f.Geste, m)).ToList(), f.B.Select(m => Option(f.Geste, m)).ToList())).ToList();
        var plantes = humain >= 1 && humain <= humains.Count
            ? monde.Flore.Sur(monde.Carte.BiomeEn(humains[humain - 1].X, humains[humain - 1].Y))
                .Select(e => new ChoixDto(e.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Nom)).ToList()
            : [];
        return new ChoixIntuitionDto(gestes, plantes);
    }

    /// <summary>Savoirs partagés dans une communauté : chaque effet connu, et par combien de ses membres.</summary>
    public static List<string> DeCommunaute(IReadOnlyList<Humain> membres) =>
        membres.SelectMany(m => m.Savoirs.Where(s => s.Confiance >= 0.4 && s.Effet is not (Effet.Rien or Effet.Tiedeur)).Select(s => s.Effet).Distinct())
            .GroupBy(e => e).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => L.T("savoirs.partage", ("effet", L.T($"effet.{g.Key}")), ("n", g.Count()), ("total", membres.Count))).ToList();
}
