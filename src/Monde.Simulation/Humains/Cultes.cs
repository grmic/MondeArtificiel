namespace Monde.Simulation.Humains;

/// <summary>Un lieu où l'invisible s'est montré : les fidèles qui le connaissent le tiennent pour sacré.</summary>
/// <param name="ReligionId">Religion qui l'a fait sien (0 : aucune encore).</param>
public sealed record LieuSacre(int Id, int X, int Y, long Le, string Raison, long EvenementId, int ReligionId);

/// <summary>
/// Un malheur annoncé par un prophète, là où il vit, avant <paramref name="Jusqua"/>. <paramref name="Vraie"/> : l'Entité
/// le lui a fait savoir (sinon il l'a imaginé, ou inventé).
/// </summary>
public sealed record Prophetie(int ProphetId, int ReligionId, int X, int Y, long Le, long Jusqua, long EvenementId, bool Vraie);

/// <summary>
/// Ce que les humains croient ensemble : les religions nées parmi eux, leurs prières, leurs lieux sacrés et les malheurs
/// annoncés par leurs prophètes. Partagé par le comportement des humains et leur vie sociale ; enregistré avec le monde.
/// </summary>
public sealed class Cultes
{
    public Cultes(IEnumerable<Religion>? religions = null, IEnumerable<Priere>? prieres = null, IEnumerable<LieuSacre>? lieux = null,
        IEnumerable<Prophetie>? propheties = null, IEnumerable<EffetEntite>? effets = null, IEnumerable<SouvenirEntite>? souvenirs = null)
    {
        Effets = [.. effets ?? []];
        Souvenirs = [.. souvenirs ?? []];
        Religions = (religions ?? []).Select(r => r.Copier()).OrderBy(r => r.Id).ToList();
        Prieres = (prieres ?? []).Select(p => p.Copier()).OrderBy(p => p.Id).ToList();
        Lieux = (lieux ?? []).OrderBy(l => l.Id).ToList();
        Propheties = [.. propheties ?? []];
        ProchainePriere = Prieres.Count == 0 ? 1 : Prieres[^1].Id + 1;
    }

    /// <summary>Religions, de la première à la dernière (le numéro d'une religion est son rang à partir de 1).</summary>
    public List<Religion> Religions { get; }

    /// <summary>Prières en attente, et celles de l'année écoulée (les plus anciennes ne sont plus qu'en base).</summary>
    public List<Priere> Prieres { get; }

    public List<LieuSacre> Lieux { get; }

    /// <summary>Malheurs annoncés qui ne sont pas encore venus.</summary>
    public List<Prophetie> Propheties { get; }

    /// <summary>Gestes de l'Entité qui durent sur une communauté (famine, fécondité, bénédiction), en cours ou à venir.</summary>
    public List<EffetEntite> Effets { get; }

    /// <summary>Ce que les communautés se racontent des gestes visibles de l'Entité.</summary>
    public List<SouvenirEntite> Souvenirs { get; }

    /// <summary>Bénédiction en cours sur la communauté (0 à 1 : part des risques visés que l'Entité écarte ; 0 : aucune).</summary>
    public double Benediction(int communaute, long maintenant) => EffetEnCours(GesteEntite.Benir, communaute, maintenant, 0);

    /// <summary>Fécondité accordée à la communauté (facteur sur les chances de concevoir ; 1 : aucune).</summary>
    public double Fecondite(int communaute, long maintenant) => EffetEnCours(GesteEntite.Fecondite, communaute, maintenant, 1);

    private double EffetEnCours(GesteEntite geste, int communaute, long maintenant, double sinon)
    {
        if (Effets.Count == 0 || communaute == 0) return sinon;
        var force = sinon;
        foreach (var e in Effets)
            if (e.Geste == geste && e.CommunauteId == communaute && e.Debut <= maintenant && maintenant < e.Fin) force = Math.Max(force, e.Force);
        return force;
    }

    /// <summary>Numéro de la prochaine prière (au moins le numéro le plus haut déjà donné, plus un).</summary>
    public int ProchainePriere { get; set; }

    /// <summary>Religion de ce numéro, ou null.</summary>
    public Religion? Get(int id) => id >= 1 && id <= Religions.Count ? Religions[id - 1] : null;

    public Priere? Priere(int id)
    {
        var i = Prieres.BinarySearch(new Priere { Id = id, HumainId = 0, Le = 0, Sujet = 0, Jusqua = 0 }, ParNumero);
        return i >= 0 ? Prieres[i] : null;
    }

    private static readonly Comparer<Priere> ParNumero = Comparer<Priere>.Create((a, b) => a.Id.CompareTo(b.Id));

    /// <summary>Copie indépendante.</summary>
    public Cultes Copier() => new(Religions, Prieres, Lieux, Propheties, Effets, Souvenirs) { ProchainePriere = ProchainePriere };

    /// <summary>Le précepte en mots, tel que les fidèles le disent (sans le détail du paramètre).</summary>
    public static string Dire(Precepte p) => p switch
    {
        Precepte.Createur => "Une Entité venue d'ailleurs nous a faits, et elle nous parle.",
        Precepte.Partager => "Il faut partager sa nourriture.",
        Precepte.Soigner => "Il faut prendre soin des malades et des faibles.",
        Precepte.Pardonner => "Il faut pardonner.",
        Precepte.Paix => "Il ne faut pas faire la guerre.",
        Precepte.Ancetres => "Les morts veillent sur nous : il faut les honorer.",
        Precepte.Colere => "L'invisible est en colère contre nous : il faut l'apaiser.",
        Precepte.Offrandes => "Il faut offrir à l'invisible une part de ce qu'on mange.",
        Precepte.SacrificeAnimal => "Il faut offrir une bête à l'invisible.",
        Precepte.SacrificeHumain => "L'invisible veut une vie humaine.",
        Precepte.LieuSacre => "Il est un lieu où l'invisible s'est montré.",
        Precepte.Interdit => "Il est une plante qu'on ne doit pas manger.",
        Precepte.GuerreSainte => "Il faut combattre ceux qui ne croient pas comme nous.",
        Precepte.NourrirLeProphete => "Il faut nourrir celui qui porte la parole.",
        Precepte.Prophetie => "Ce qu'annonce le prophète arrive.",
        Precepte.Priere => "Il faut prier chaque jour.",
        Precepte.Chatiment => "L'invisible punit ceux qui font le mal.",
        Precepte.Defendre => "Il faut défendre les siens.",
        _ => "",
    };

    /// <summary>D'où vient un précepte, en mots (pour l'Entité seule).</summary>
    public static string Dire(OrigineDogme o) => o switch
    {
        OrigineDogme.Entite => "une parole de l'Entité",
        OrigineDogme.Deformee => "une parole de l'Entité, changée de bouche en bouche",
        OrigineDogme.Inventee => "inventé par un humain",
        OrigineDogme.Coincidence => "une coïncidence prise pour un signe",
        OrigineDogme.Catastrophe => "tiré d'une catastrophe",
        OrigineDogme.Reve => "tiré des rêves des morts",
        _ => "tiré d'un lieu où l'invisible s'est montré",
    };

    public static string Dire(SujetPriere s) => s switch
    {
        SujetPriere.Guerison => "guérison",
        SujetPriere.Nourriture => "nourriture",
        SujetPriere.Pluie => "pluie",
        SujetPriere.Accalmie => "fin d'une catastrophe",
        SujetPriere.Enfant => "enfant",
        SujetPriere.Protection => "protection dans la guerre",
        SujetPriere.Vengeance => "vengeance",
        SujetPriere.Amour => "amour",
        _ => "remerciement",
    };

    public static string Dire(IssuePriere i) => i switch
    {
        IssuePriere.EnAttente => "en attente",
        IssuePriere.ExauceeParEntite => "exaucée par l'Entité",
        IssuePriere.ExauceeParHasard => "arrivée d'elle-même",
        IssuePriere.SansReponse => "sans réponse",
        _ => "rendue",
    };

    public static string Dire(NaissanceReligion n) => n switch
    {
        NaissanceReligion.Prophete => "autour d'un prophète désigné par l'Entité",
        NaissanceReligion.ProphetSpontane => "autour d'un prophète venu de lui-même",
        NaissanceReligion.Ancetres => "du culte des morts",
        _ => "d'une séparation",
    };

    public static string Dire(SourceProphete s) => s switch
    {
        SourceProphete.Entite => "désigné par l'Entité",
        SourceProphete.Visionnaire => "visionnaire sincère",
        SourceProphete.Imposteur => "imposteur",
        _ => "",
    };
}
