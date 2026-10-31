using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Où en est une parcelle.</summary>
public enum EtatParcelle : byte
{
    /// <summary>Semée ou plantée : rien n'a encore levé.</summary>
    EnTerre = 0,
    /// <summary>Levée : elle pousse.</summary>
    Pousse = 1,
    /// <summary>Mûre : graines, racines ou fruits sont bons à prendre.</summary>
    Mure = 2,
    /// <summary>Finie : récoltée, perdue ou retournée aux herbes (elle sera retirée).</summary>
    Finie = 3,
}

/// <summary>
/// Une lignée de plante cultivée : les graines (ou les boutures) qu'une communauté sème d'année en année, et les traits qu'elles
/// ont pris sous sa main. Rien n'y est décidé : chaque trait change selon la façon de semer et de récolter (voir
/// <see cref="Comportement.Recolter"/>). Une lignée abandonnée se perd ; semée à nouveau de graines sauvages, elle recule.
/// </summary>
/// <param name="Tenaces">Part des plantes dont les graines restent sur la tige à maturité (sauvage : environ une sur mille).</param>
/// <param name="Grosseur">Grosseur des graines par rapport à la plante sauvage (1) : un semis profond favorise les grosses.</param>
/// <param name="Dormance">Part des graines qui ne germent qu'un an plus tard.</param>
/// <param name="Douceur">Ce que la sélection a retiré de l'amertume de la plante sauvage (0 : rien, 1 : tout).</param>
/// <param name="Vigueur">Rendement par rapport à la plante sauvage (1), gagné en gardant les plus beaux plants à replanter.</param>
/// <param name="Semence">Graines ou boutures gardées pour semer, en kilos.</param>
/// <param name="GardienId">Celui qui garde la semence (0 : personne en particulier).</param>
/// <param name="Saisons">Ce que les semeurs ont appris de chaque saison de semis (réussites moins échecs).</param>
/// <param name="ChoisieLe">Dernière saison où l'on a choisi ce qu'on garde : une génération par an, quel que soit le nombre de parcelles.</param>
/// <param name="Domestiquee">La lignée a franchi le seuil où elle ne vit plus sans l'homme (noté une fois au journal).</param>
public sealed record Lignee(int Id, int Espece, int CommunauteId, double Tenaces, double Grosseur, double Dormance, double Douceur, double Vigueur,
    int Generations, long NeeLe, long SemeeLe, double Semence, int GardienId, int[] Saisons, long EvenementId, bool Domestiquee = false, long ChoisieLe = 0)
{
    /// <summary>Une lignée tout juste prise à la plante sauvage.</summary>
    public static Lignee Sauvage(int id, Espece e, int communaute, long maintenant, int gardien, long evenement) =>
        new(id, e.Numero, communaute, e.Aliment!.Tenaces, 1, e.Aliment.Dormance, 0, 1, 0, maintenant, maintenant, 0, gardien, new int[4], evenement);

    /// <summary>Amertume qui reste à la plante cultivée.</summary>
    public double Amertume(Aliment a) => a.Amertume * (1 - Douceur);
}

/// <summary>
/// Une parcelle : là où quelqu'un a semé ou planté une espèce, avec sa surface et ce qui y pousse. Elle se retrouve, se soigne,
/// se récolte ou s'oublie ; les herbes, les bêtes, la sécheresse et le gel la menacent.
/// </summary>
/// <param name="Ares">Surface (un are : 100 m²).</param>
/// <param name="Part">Part des plantes de la parcelle dont les graines tiennent à la tige (celle de la lignée, mêlée aux repousses
/// sauvages de l'an passé).</param>
/// <param name="Croissance">De 0 (levée) à 1 (mûre).</param>
/// <param name="Sante">Ce qui reste de vigoureux, de 1 à 0 : les bêtes, la soif, le gel l'entament.</param>
/// <param name="Herbes">Les herbes qui l'étouffent, de 0 à 1.</param>
/// <param name="SurPied">Ce qui reste à prendre, en kilos : graines qui tiennent, et graines qui tombent d'elles-mêmes.</param>
/// <param name="Tombees">Graines tombées au sol avant la récolte (en kilos) : elles relèveront, sauvages.</param>
/// <param name="Prevue">Récolte que la parcelle portait en mûrissant (kilos).</param>
/// <param name="Cendres">Gain de la terre brûlée avant le semis (0 : aucun).</param>
/// <param name="Limon">Gain du limon laissé par la crue (0 : aucun).</param>
/// <param name="Profond">Semée en enfonçant les graines au bâton (pas jetées à la volée).</param>
/// <param name="Sarclee">On en a arraché les herbes cette saison.</param>
/// <param name="VueLe">Dernière fois que quelqu'un des siens l'a gardée ou soignée.</param>
/// <param name="Saison">Saison du semis (0 à 3).</param>
/// <param name="Annees">Saisons de récolte déjà passées (plantes vivaces).</param>
public sealed record Parcelle(int Id, int X, int Y, int Espece, int LigneeId, double Ares, int SemeurId, int CommunauteId, long SemeeLe,
    EtatParcelle Etat, double Part, double Croissance, double Sante, double Herbes, double SurPiedTenaces, double SurPiedCassantes, double Tombees,
    double Prevue, long MureLe, double Cendres, double Limon, bool Profond, bool Sarclee, long VueLe, int Saison, int Annees, long EvenementId,
    double RecolteTenaces = 0, double RecolteCassantes = 0)
{
    public double SurPied => SurPiedTenaces + SurPiedCassantes;
}

/// <summary>Ce qui est arrivé à une parcelle dans la journée, pour ceux qui l'ont semée.</summary>
public enum IssueParcelle : byte
{
    Levee = 0,
    Mure = 1,
    Perdue = 2,
    Oubliee = 3,
}

/// <summary>Ce qu'une communauté pense des parcelles semées : comme pour les réserves (voir <see cref="IdeeReserve"/>).</summary>
public sealed class IdeeDesChamps
{
    public IdeeReserve Idee { get; set; }
    public long Le { get; set; }
    public long EvenementId { get; set; }
    public double Partages { get; set; }
    public double Vols { get; set; }
    public long VolEvenementId { get; set; }
    /// <summary>Restes jetés aux rôdeurs qui tournent autour du camp (depuis la 0.33.1) : quelques semaines de restes, et ils s'enhardissent.</summary>
    public double Rodeurs { get; set; }
    public long RodeursEvenementId { get; set; }

    public IdeeDesChamps Copier() => (IdeeDesChamps)MemberwiseClone();
}

/// <summary>
/// Des bêtes gardées (génération 2, depuis la 0.33.1) : un petit pris à la chasse, des chiots de rôdeurs, puis, si l'espèce se
/// reproduit captive, un troupeau. Ses traits changent sous la main de l'homme : à chaque mise bas, on garde les plus calmes et
/// l'on mange les plus farouches (Belyaev : renards apprivoisés en quelques dizaines de générations ; Zeder, 2012).
/// </summary>
/// <param name="GardienId">Celui qui s'en occupe (0 : la communauté).</param>
/// <param name="Docilite">0 : sauvage ; 1 : ne fuit ni ne charge plus. Domestiquée au-delà de 0,6.</param>
/// <param name="Taille">Taille par rapport à la bête sauvage (1) : les bêtes gardées rapetissent d'abord.</param>
/// <param name="Etat">Embonpoint, de 0 (affamées) à 1.</param>
/// <param name="MiseBasLe">Dernière mise bas.</param>
/// <param name="Abattue">On en a déjà abattu une (noté une fois au journal).</param>
public sealed record Troupeau(int Id, int Espece, int CommunauteId, int GardienId, double Adultes, double Petits, double Docilite, double Taille,
    int Generations, long NeLe, long MiseBasLe, long EvenementId, double Etat = 1, bool Domestique = false, long NaissanceEvenementId = 0, bool Abattue = false)
{
    public double Nombre => Adultes + Petits;
}

/// <summary>Les champs du monde : parcelles, lignées, et ce que chaque communauté en pense.</summary>
public sealed class Champs
{
    public List<Parcelle> Parcelles { get; } = [];
    public List<Lignee> Lignees { get; } = [];
    public Dictionary<int, IdeeDesChamps> Idees { get; } = [];
    /// <summary>Bêtes gardées (depuis la 0.33.1).</summary>
    public List<Troupeau> Troupeaux { get; } = [];
    private int _prochaineParcelle = 1;
    private int _prochaineLignee = 1;
    private int _prochainTroupeau = 1;

    public Champs(IEnumerable<Parcelle>? parcelles = null, IEnumerable<Lignee>? lignees = null, IEnumerable<(int Communaute, IdeeDesChamps Idee)>? idees = null,
        IEnumerable<Troupeau>? troupeaux = null)
    {
        Parcelles.AddRange(parcelles ?? []);
        Troupeaux.AddRange(troupeaux ?? []);
        _prochainTroupeau = 1 + Troupeaux.Select(t => t.Id).DefaultIfEmpty(0).Max();
        Lignees.AddRange(lignees ?? []);
        foreach (var (c, i) in idees ?? []) Idees[c] = i;
        _prochaineParcelle = 1 + Parcelles.Select(p => p.Id).DefaultIfEmpty(0).Max();
        _prochaineLignee = 1 + Lignees.Select(l => l.Id).DefaultIfEmpty(0).Max();
    }

    public bool Vide => Parcelles.Count == 0 && Lignees.Count == 0;

    public Troupeau NouveauTroupeau(Bete b, int communaute, int gardien, double adultes, double petits, double docilite, long maintenant, long evenement)
    {
        var t = new Troupeau(_prochainTroupeau++, b.Numero, communaute, gardien, adultes, petits, docilite, 1, 0, maintenant, 0, evenement);
        Troupeaux.Add(t);
        return t;
    }

    public IdeeDesChamps IdeeDe(int communaute)
    {
        if (!Idees.TryGetValue(communaute, out var i)) Idees[communaute] = i = new IdeeDesChamps();
        return i;
    }

    public IdeeReserve IdeePour(int communaute) => Idees.TryGetValue(communaute, out var i) ? i.Idee : IdeeReserve.Aucune;

    public Lignee? Lignee(int id)
    {
        foreach (var l in Lignees) if (l.Id == id) return l;
        return null;
    }

    public Parcelle? Parcelle(int id)
    {
        foreach (var p in Parcelles) if (p.Id == id) return p;
        return null;
    }

    public Lignee NouvelleLignee(Espece e, int communaute, long maintenant, int gardien, long evenement)
    {
        var l = Humains.Lignee.Sauvage(_prochaineLignee++, e, communaute, maintenant, gardien, evenement);
        Lignees.Add(l);
        return l;
    }

    public void Changer(Lignee l)
    {
        var i = Lignees.FindIndex(x => x.Id == l.Id);
        if (i >= 0) Lignees[i] = l;
    }

    public void Changer(Parcelle p)
    {
        var i = Parcelles.FindIndex(x => x.Id == p.Id);
        if (i >= 0) Parcelles[i] = p;
    }

    /// <summary>
    /// Sème (ou plante) : s'ajoute à la parcelle de même lignée tout juste semée sur la case, ou en ouvre une nouvelle.
    /// <paramref name="part"/> : part des graines qui tiennent à la tige dans ce qu'on sème.
    /// </summary>
    public Parcelle Semer(int x, int y, Lignee l, double ares, double part, int semeur, int communaute, long maintenant, double cendres, double limon,
        bool profond, long evenement)
    {
        var saison = (int)(maintenant % Calendrier.SecondesParAn / (Calendrier.JoursParSaison * Calendrier.SecondesParJour));
        for (var i = 0; i < Parcelles.Count; i++)
        {
            var p = Parcelles[i];
            if (p.X != x || p.Y != y || p.LigneeId != l.Id || p.Etat != EtatParcelle.EnTerre) continue;
            var total = p.Ares + ares;
            return Parcelles[i] = p with
            {
                Ares = total, Part = (p.Part * p.Ares + part * ares) / total, Profond = p.Profond && profond,
                Cendres = Math.Max(p.Cendres, cendres), Limon = Math.Max(p.Limon, limon),
            };
        }
        // Les graines tombées l'an passé sur la même case relèvent, sauvages : elles se mêlent au semis.
        var repousses = 0.0;
        foreach (var p in Parcelles)
            if (p.X == x && p.Y == y && p.Etat == EtatParcelle.Finie && p.Espece == l.Espece) repousses += p.Tombees;
        var nouvelle = new Parcelle(_prochaineParcelle++, x, y, l.Espece, l.Id, ares, semeur, communaute, maintenant, EtatParcelle.EnTerre, part, 0, 1, 0,
            0, 0, 0, 0, 0, cendres, limon, profond, false, maintenant, saison, 0, evenement, 0, 0);
        if (repousses > 0)
        {
            // Une part seulement des graines tombées germe (les autres dorment, pourrissent ou sont mangées).
            var levees = repousses * (1 - l.Dormance) * 0.3;
            var semis = ares * 1.0;
            nouvelle = nouvelle with { Part = (part * semis) / (semis + levees) };
        }
        Parcelles.Add(nouvelle);
        return nouvelle;
    }

    /// <summary>Retire les parcelles finies depuis plus d'un an (leurs graines tombées ne relèvent plus).</summary>
    public void Nettoyer(long maintenant) =>
        Parcelles.RemoveAll(p => p.Etat == EtatParcelle.Finie && maintenant - Math.Max(p.MureLe, p.SemeeLe) > 2 * Calendrier.SecondesParAn);
}
