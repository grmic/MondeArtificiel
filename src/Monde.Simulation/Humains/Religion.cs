namespace Monde.Simulation.Humains;

/// <summary>Ce qu'un humain demande à l'invisible.</summary>
public enum SujetPriere : byte
{
    /// <summary>Qu'un malade ou un blessé guérisse (lui-même ou un proche : voir <see cref="Priere.CibleId"/>).</summary>
    Guerison = 0,
    /// <summary>Avoir à manger.</summary>
    Nourriture = 1,
    /// <summary>Que la pluie revienne sur une terre sèche.</summary>
    Pluie = 2,
    /// <summary>Qu'une catastrophe s'arrête.</summary>
    Accalmie = 3,
    /// <summary>Avoir un enfant.</summary>
    Enfant = 4,
    /// <summary>Que les siens soient protégés de la guerre.</summary>
    Protection = 5,
    /// <summary>Que l'invisible frappe celui qui l'a offensé.</summary>
    Vengeance = 6,
    /// <summary>Être aimé de quelqu'un.</summary>
    Amour = 7,
    /// <summary>Remercier : rien à exaucer.</summary>
    Remerciement = 8,
}

/// <summary>Ce qu'il est advenu d'une prière.</summary>
public enum IssuePriere : byte
{
    EnAttente = 0,
    /// <summary>L'Entité l'a exaucée.</summary>
    ExauceeParEntite = 1,
    /// <summary>Ce qu'il demandait est arrivé de soi-même : il croit avoir été exaucé.</summary>
    ExauceeParHasard = 2,
    /// <summary>Rien n'est venu, ou le contraire est arrivé.</summary>
    SansReponse = 3,
    /// <summary>Un remerciement : il n'attend rien.</summary>
    Rendue = 4,
}

/// <summary>
/// Une prière : qui, quand, pour quoi (et pour qui), avec quelle ferveur. <see cref="Forte"/> : une prière de vie ou de mort,
/// notée au journal (★) ; les autres ne sont visibles que dans la liste des prières.
/// </summary>
public sealed class Priere
{
    public required int Id { get; init; }
    public required int HumainId { get; init; }
    public required long Le { get; init; }
    public required SujetPriere Sujet { get; init; }

    /// <summary>Celui pour qui il prie (malade, aimé) ou contre qui (vengeance) ; 0 : lui-même ou les siens.</summary>
    public int CibleId { get; init; }

    /// <summary>Précision selon le sujet : la catastrophe, la guerre, la communauté.</summary>
    public long Parametre { get; init; }

    /// <summary>De 0 à 1 : la force de sa foi et de sa détresse.</summary>
    public double Ferveur { get; init; }
    public bool Forte { get; init; }
    public int ReligionId { get; init; }

    /// <summary>Événement du journal qui la note (0 : prière discrète).</summary>
    public long EvenementId { get; set; }

    /// <summary>Jusqu'à quand il attend une réponse.</summary>
    public required long Jusqua { get; init; }

    public IssuePriere Issue { get; set; }
    public long IssueLe { get; set; }

    /// <summary>Ce qu'il en a conclu, en mots (null tant qu'il attend).</summary>
    public string? Conclusion { get; set; }

    public Priere Copier() => (Priere)MemberwiseClone();
}

/// <summary>Le rôle d'un humain dans sa religion.</summary>
public enum RoleReligieux : byte
{
    Aucun = 0,
    /// <summary>Il dit parler au nom de l'Entité.</summary>
    Prophete = 1,
    /// <summary>Il garde les récits et conduit les rites.</summary>
    Pretre = 2,
}

/// <summary>D'où le prophète tient sa parole. Seule l'Entité le sait.</summary>
public enum SourceProphete : byte
{
    Aucune = 0,
    /// <summary>Désigné par l'Entité.</summary>
    Entite = 1,
    /// <summary>Il a eu des visions et croit sincèrement parler en son nom.</summary>
    Visionnaire = 2,
    /// <summary>Il prétend parler en son nom pour en tirer pouvoir et nourriture.</summary>
    Imposteur = 3,
}

/// <summary>Ce qu'enseigne une croyance partagée, et ce qu'elle fait faire.</summary>
public enum Precepte : byte
{
    /// <summary>L'Entité nous a faits et nous parle.</summary>
    Createur = 0,
    Partager = 1,
    /// <summary>Prendre soin des malades et des faibles.</summary>
    Soigner = 2,
    Pardonner = 3,
    /// <summary>Ne pas faire la guerre.</summary>
    Paix = 4,
    /// <summary>Les morts veillent sur nous : on les honore.</summary>
    Ancetres = 5,
    /// <summary>L'Entité est en colère contre nous : il faut l'apaiser.</summary>
    Colere = 6,
    /// <summary>On lui offre de la nourriture.</summary>
    Offrandes = 7,
    /// <summary>On lui offre une bête.</summary>
    SacrificeAnimal = 8,
    /// <summary>On lui offre une vie humaine.</summary>
    SacrificeHumain = 9,
    /// <summary>Un lieu sacré (paramètre : case).</summary>
    LieuSacre = 10,
    /// <summary>Une plante interdite (paramètre : espèce).</summary>
    Interdit = 11,
    /// <summary>Combattre une autre communauté (paramètre : communauté ; 0 : ceux qui ne croient pas comme nous).</summary>
    GuerreSainte = 12,
    /// <summary>Nourrir le prophète (ce qu'enseigne un imposteur).</summary>
    NourrirLeProphete = 13,
    /// <summary>L'Entité voit l'avenir : ce que dit le prophète arrive.</summary>
    Prophetie = 14,
    /// <summary>Prier chaque jour.</summary>
    Priere = 15,
    /// <summary>L'Entité punit le vol (paramètre 0) ou le meurtre (1).</summary>
    Chatiment = 16,
    /// <summary>Défendre les siens : la guerre juste.</summary>
    Defendre = 17,
}

/// <summary>D'où vient un précepte, tel que l'Entité le sait.</summary>
public enum OrigineDogme : byte
{
    /// <summary>Une parole qu'elle a réellement dite.</summary>
    Entite = 0,
    /// <summary>Une parole qu'elle a dite, comprise de travers ou changée en passant de bouche en bouche.</summary>
    Deformee = 1,
    /// <summary>Inventée par un prophète (visions sincères ou mensonge).</summary>
    Inventee = 2,
    /// <summary>Tirée d'une coïncidence prise pour un signe.</summary>
    Coincidence = 3,
    /// <summary>Tirée d'une catastrophe.</summary>
    Catastrophe = 4,
    /// <summary>Tirée des rêves des morts.</summary>
    Reve = 5,
    /// <summary>Tirée d'un lieu où l'invisible s'est montré.</summary>
    Lieu = 6,
}

/// <summary>Une croyance partagée par les fidèles d'une religion.</summary>
public sealed record Dogme(Precepte Precepte, int Parametre, OrigineDogme Origine, string Texte, long Le, long EvenementId);

/// <summary>Comment une religion est née.</summary>
public enum NaissanceReligion : byte
{
    /// <summary>Autour d'un prophète désigné par l'Entité.</summary>
    Prophete = 0,
    /// <summary>Autour d'un prophète venu de lui-même (visionnaire ou imposteur).</summary>
    ProphetSpontane = 1,
    /// <summary>Le culte des morts, né des rêves.</summary>
    Ancetres = 2,
    /// <summary>Une branche qui s'est séparée d'une autre religion.</summary>
    Schisme = 3,
}

/// <summary>
/// Une religion : des croyances partagées, un nom dans la langue de ceux qui l'ont fondée, des rites. Les fidèles se comptent
/// sur les humains (voir <see cref="Humain.ReligionId"/>).
/// </summary>
public sealed class Religion
{
    public required int Id { get; init; }
    public required string Nom { get; set; }

    /// <summary>Le sens du nom, en français (« la parole de Kalu »).</summary>
    public required string Sens { get; set; }
    public required int FondateurId { get; init; }
    public required long FondeeLe { get; init; }
    public required NaissanceReligion Naissance { get; init; }
    public long EvenementId { get; init; }

    /// <summary>Religion dont elle s'est séparée (0 : aucune).</summary>
    public int ParentId { get; init; }

    /// <summary>Communauté où elle est née (0 : aucune).</summary>
    public int CommunauteId { get; init; }

    public List<Dogme> Dogmes { get; set; } = [];

    /// <summary>Comment ses fidèles prient (gestes inventés), en mots ; null tant qu'ils n'ont pas de manière à eux.</summary>
    public string? Gestes { get; set; }

    /// <summary>Plus aucun fidèle vivant (0 : elle vit encore).</summary>
    public long EteinteLe { get; set; }

    /// <summary>Plus grand nombre de fidèles qu'elle ait eu.</summary>
    public int FidelesMax { get; set; }

    public bool Enseigne(Precepte p) => Dogmes.Exists(d => d.Precepte == p);

    public Religion Copier()
    {
        var r = (Religion)MemberwiseClone();
        r.Dogmes = [.. Dogmes];
        return r;
    }
}
