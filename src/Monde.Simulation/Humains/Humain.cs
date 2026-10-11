namespace Monde.Simulation.Humains;

public enum Sexe : byte
{
    Femme = 0,
    Homme = 1,
}

/// <summary>Ce que fait un humain pendant le pas en cours.</summary>
public enum Activite : byte
{
    Repos = 0,
    Dormir = 1,
    Boire = 2,
    ChercherEau = 3,
    Cueillir = 4,
    Chasser = 5,
    ChercherNourriture = 6,
    SAbriter = 7,
    ChercherAbri = 8,
    RejoindrePartenaire = 9,
    Explorer = 10,
    Mort = 11,
    SAccoupler = 12,
    SuivreParent = 13,
    PorteParSaMere = 14,
    VeillerEnfant = 15,
    RejoindreProche = 16,
    EviterRival = 17,
    /// <summary>Partir avec sa communauté vers des terres plus riches.</summary>
    Migrer = 18,
    /// <summary>Fuir un danger : feu, eau qui monte, volcan ; ou chercher un abri contre la tempête.</summary>
    Fuir = 19,
    /// <summary>Marcher avec une troupe de guerriers vers un camp ennemi, ou l'y guetter.</summary>
    Raid = 20,
    /// <summary>Rentrer d'un raid.</summary>
    RentrerDuRaid = 21,
    /// <summary>Quitter les terres de la communauté qui l'a chassé.</summary>
    PartirEnExil = 22,
}

public enum CauseDeMort : byte
{
    Aucune = 0,
    Faim = 1,
    Soif = 2,
    Froid = 3,
    Maladie = 4,
    Epuisement = 5,
    Vieillesse = 6,
    Accouchement = 7,
    /// <summary>Suicide, au terme d'une longue détresse.</summary>
    Desespoir = 8,
    /// <summary>Tué par un autre humain (voir <see cref="Humain.MeurtrierId"/>).</summary>
    Meurtre = 9,
    /// <summary>Noyé : crue, vague géante, onde de tempête.</summary>
    Noyade = 10,
    /// <summary>Pris dans les flammes d'un incendie ou le souffle d'un impact.</summary>
    Brulure = 11,
    /// <summary>Écrasé par des rochers tombés pendant un séisme.</summary>
    Eboulement = 12,
    /// <summary>Lave, nuée brûlante ou cendres d'un volcan.</summary>
    Eruption = 13,
    /// <summary>Arbre ou branche abattus par la tempête, et autres accidents.</summary>
    Accident = 14,
    /// <summary>Coup de chaleur pendant une canicule.</summary>
    Chaleur = 15,
    /// <summary>Tué lors d'un raid entre communautés (voir <see cref="Humain.MeurtrierId"/>).</summary>
    Guerre = 16,
}

/// <summary>
/// Caractère et intelligence, innés et en partie hérités (voir <see cref="Psyche"/>).
/// Les traits vont de 0 à 1, autour de 0,5.
/// </summary>
/// <param name="Ouverture">Curiosité, imagination, goût de la nouveauté (Big Five).</param>
/// <param name="Conscience">Application, prévoyance, constance (Big Five).</param>
/// <param name="Extraversion">Besoin des autres, énergie sociale ; bas : replié sur soi (Big Five).</param>
/// <param name="Agreabilite">Bienveillance, envie de partager, de coopérer ; bas : méfiant, querelleur (Big Five).</param>
/// <param name="Nevrosisme">Sensibilité aux émotions négatives : anxiété, tristesse, colère (Big Five).</param>
/// <param name="Transcendance">Penchant spirituel et contemplatif ; bas : rationnel, terre à terre (Cloninger).</param>
/// <param name="Attachement">Force du lien avec ses enfants : élevé chez la plupart, faible chez quelques-uns.</param>
/// <param name="Endurance">Résistance physique à la fatigue et aux maladies.</param>
public sealed record Personnalite(double Ouverture, double Conscience, double Extraversion, double Agreabilite,
    double Nevrosisme, double Transcendance, double Attachement, double Endurance, Intelligence Intelligence)
{
    /// <summary>Envie d'aller voir plus loin.</summary>
    public double Curiosite => Ouverture;

    /// <summary>Envie d'être avec les autres.</summary>
    public double Sociabilite => Extraversion;

    /// <summary>Réagir tôt au danger : les consciencieux et les anxieux sont prudents.</summary>
    public double Prudence => Math.Clamp(0.6 * Conscience + 0.4 * Nevrosisme, 0, 1);

    /// <summary>Adresse, de 0 à 1, d'après l'aptitude manuelle.</summary>
    public double Adresse => Math.Clamp((Intelligence.Manuelle - 55) / 90, 0, 1);

    /// <summary>
    /// Charisme : extraverti, émotionnellement stable, chaleureux, à l'aise pour communiquer.
    /// Le meneur d'un groupe n'est pas désigné : il émerge de son charisme et de ses liens.
    /// </summary>
    public double Charisme => Math.Clamp(0.4 * Extraversion + 0.25 * (1 - Nevrosisme) + 0.15 * Agreabilite
        + 0.2 * Math.Clamp((Intelligence.Verbale - 55) / 90, 0, 1), 0, 1);
}

/// <summary>
/// Un humain et son état. Les besoins vont de 0 (satisfait) à 100 (extrême) ;
/// la santé va de 100 (pleine forme) à 0 (mort).
/// </summary>
public sealed class Humain
{
    public required int Id { get; init; }

    /// <summary>
    /// Nom tiré au hasard pour que l'observateur puisse le suivre.
    /// Les humains n'ont pas encore de langage : ce n'est pas un nom qu'ils se donnent.
    /// </summary>
    public required string Nom { get; init; }

    public required Sexe Sexe { get; init; }
    /// <summary>Compagnon ou compagne (0 si aucun). Un couple peut se former plus tard entre deux humains seuls.</summary>
    public required int PartenaireId { get; set; }

    /// <summary>Parents (0 pour les humains de départ, créés par l'Entité).</summary>
    public int MereId { get; init; }
    public int PereId { get; init; }
    public required int Emplacement { get; init; }

    /// <summary>Date de naissance en secondes du monde (négative si avant l'an 1).</summary>
    public required long Naissance { get; init; }

    public required Personnalite Personnalite { get; init; }

    public required Biologie Biologie { get; init; }

    /// <summary>Traits physiques (symétrie, harmonie, taille, teint, allure) : la beauté en découle, selon l'idéal de chaque groupe.</summary>
    public Apparence Apparence { get; set; } = new(0.5, 0.5, 0, 0.5, 0.5);

    private Orientation? _orientation;

    /// <summary>Orientation sexuelle (attirance vers les hommes, vers les femmes). Par défaut : vers l'autre sexe.</summary>
    public Orientation Orientation
    {
        get => _orientation ??= Sexe == Sexe.Femme ? new Orientation(1, 0) : new Orientation(0, 1);
        set => _orientation = value;
    }

    /// <summary>
    /// Ce qu'il sait de son attirance pour son propre sexe, de 0 (il l'ignore) à 1 (il la connaît). Elle grandit à chaque
    /// trouble ressenti, plus vite chez les esprits ouverts.
    /// </summary>
    public double Lucidite { get; set; } = 1;

    /// <summary>Exclusions : chassé de sa communauté, renié par un parent, ou parti avec un exclu.</summary>
    public List<Exclusion> Exclusions { get; set; } = [];

    /// <summary>Maladie ordinaire en cours (non contagieuse), pour le journal : l'humain ne sait pas la nommer.</summary>
    public Maladie Maladie { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public double Faim { get; set; }

    /// <summary>
    /// Réserves du corps (graisse), de 0 à 100 : environ six semaines de nourriture quand elles sont pleines.
    /// Elles se remplissent quand on mange à sa faim et se vident pendant les disettes ; quand elles sont basses,
    /// la faim se fait sentir plus vite, les maladies frappent plus, et une femme conçoit moins.
    /// </summary>
    public double Reserves { get; set; } = 50;
    public double Soif { get; set; }
    public double Fatigue { get; set; }
    public double Froid { get; set; }
    public double Sante { get; set; } = 100;
    public Activite Activite { get; set; }

    /// <summary>Destination en cours, ou -1 si aucune.</summary>
    public int CibleX { get; set; } = -1;
    public int CibleY { get; set; } = -1;

    /// <summary>Vrai quand l'humain contourne un obstacle pour atteindre sa cible.</summary>
    public bool Detour { get; set; }

    /// <summary>
    /// Chemin déjà fait vers la case suivante, en mètres « de terrain plat » : à pas lents (enfant, âgé, terrain
    /// difficile), il faut plus de dix minutes pour passer d'une case à l'autre. Le chemin fait est gardé même si
    /// l'humain s'arrête ou change d'idée en route : sans cela, un humain épuisé qui hésite n'atteindrait jamais
    /// la case voisine.
    /// </summary>
    public double Progression { get; set; }

    /// <summary>Dernier instant où cette mère portait un bébé (ce pas-ci ou le précédent, selon l'ordre) : ralentit sa marche.</summary>
    public long BebePorteLe { get; set; } = -1;

    /// <summary>Fin de la maladie en secondes du monde (0 si en bonne santé).</summary>
    public long MaladeJusqua { get; set; }

    /// <summary>Désir, de 0 à 100 : un instinct qui monte avec le temps chez les adultes.</summary>
    public double Desir { get; set; }

    /// <summary>Début de la grossesse en secondes du monde, ou null.</summary>
    public long? EnceinteDepuis { get; set; }

    /// <summary>Date prévue de l'accouchement : la durée de grossesse varie d'une grossesse à l'autre.</summary>
    public long? TermeGrossesse { get; set; }

    /// <summary>Père de l'enfant attendu (0 si aucune grossesse).</summary>
    public int PereEnfantId { get; set; }

    /// <summary>Date du dernier accouchement .</summary>
    public long? DernierAccouchement { get; set; }

    /// <summary>Lieux connus et dernières pensées.</summary>
    public Memoire Memoire { get; set; } = new();

    /// <summary>Liens avec les autres humains (familiarité, affection), du plus ancien au plus récent.</summary>
    public List<Lien> Liens { get; set; } = [];

    /// <summary>Humeur, de 0 (au plus mal) à 100 (joyeux) ; 50 est neutre.</summary>
    public double Humeur { get; set; } = 50;

    /// <summary>Chagrin, de 0 à 100 : monte à la perte d'un proche, puis s'apaise plus ou moins vite selon chacun.</summary>
    public double Chagrin { get; set; }

    /// <summary>Humain le plus pleuré en ce moment (0 si aucun).</summary>
    public int DeuilDeId { get; set; }

    /// <summary>Jours d'affilée passés au plus mal (humeur basse) : au-delà de deux semaines, l'humain sombre.</summary>
    public int JoursSombres { get; set; }

    /// <summary>Début d'une dépression, ou null.</summary>
    public long? DeprimeDepuis { get; set; }

    /// <summary>Communauté à laquelle il appartient (0 : aucune).</summary>
    public int CommunauteId { get; set; }

    /// <summary>Inconfort cumulé depuis le début de la journée (faim, soif, froid, maladie), pour l'humeur.</summary>
    public double InconfortDuJour { get; set; }

    /// <summary>Part de l'inconfort du jour due à la faim et à la soif, puis au froid (le reste vient de la maladie).</summary>
    public double FaimSoifDuJour { get; set; }
    public double FroidDuJour { get; set; }

    /// <summary>Jours d'affilée passés sans la compagnie d'un être aimé.</summary>
    public int JoursSeul { get; set; }

    /// <summary>Colère, de 0 à 100 : elle flambe à l'offense et retombe en quelques jours, mais se ranime à la vue de l'offenseur.</summary>
    public double Colere { get; set; }

    /// <summary>Rancunes : ce que l'humain reproche à d'autres (trahison, coups, meurtre d'un proche). Elles s'effacent avec le temps, ou non.</summary>
    public List<Grief> Griefs { get; set; } = [];

    /// <summary>Raisons de l'humeur du jour, de la plus lourde à la plus légère (calculées chaque jour).</summary>
    public List<Raison> Raisons { get; set; } = [];

    /// <summary>Événement du journal à l'origine du deuil en cours (la mort pleurée), 0 sinon.</summary>
    public long DeuilEvenementId { get; set; }

    /// <summary>Événement du journal qui a marqué le début de la dépression en cours, 0 sinon.</summary>
    public long DepressionEvenementId { get; set; }

    /// <summary>Événement du journal qui rapporte sa mort, 0 s'il est vivant.</summary>
    public long MortEvenementId { get; set; }

    /// <summary>Humain qui l'a tué (0 si sa mort n'est pas un meurtre).</summary>
    public int MeurtrierId { get; set; }

    /// <summary>Humains aimés côtoyés aujourd'hui, pour l'humeur.</summary>
    public int CompagnieDuJour { get; set; }

    /// <summary>Peur, de 0 à 100 : laissée par une catastrophe ou un phénomène du ciel, elle s'apaise plus ou moins vite selon chacun.</summary>
    public double Peur { get; set; }

    /// <summary>Catastrophe à l'origine de la peur (événement du journal qui l'annonce), et son type.</summary>
    public long PeurEvenementId { get; set; }
    public Nature.TypeCatastrophe PeurType { get; set; }

    /// <summary>Maladie contagieuse en cours (0 : aucune) : contamination, premiers signes, fin de la maladie.</summary>
    public int InfectionId { get; set; }
    public long InfecteLe { get; set; }
    public long SymptomesLe { get; set; }
    public long GuerisonLe { get; set; }

    /// <summary>Événement du journal qui rapporte la contamination (cause des suites : symptômes, mort, contagion).</summary>
    public long InfectionEvenementId { get; set; }

    /// <summary>Maladies contre lesquelles il est immunisé, jusqu'à quand.</summary>
    public List<Nature.Immunite> Immunites { get; set; } = [];

    public bool EstDeprime => DeprimeDepuis is not null;

    /// <summary>Lien avec un autre humain, ou null.</summary>
    public Lien? LienAvec(int id)
    {
        foreach (var l in Liens) if (l.Id == id) return l;
        return null;
    }

    /// <summary>Affection pour un autre humain (0 sans lien).</summary>
    public double AffectionPour(int id) => LienAvec(id)?.Affection ?? 0;

    public long? MortLe { get; set; }
    public CauseDeMort CauseDeMort { get; set; }

    public bool EstVivant => MortLe is null;

    public bool EstMalade(long maintenant) => MaladeJusqua > maintenant;

    public bool EstEnceinte => EnceinteDepuis is not null;

    /// <summary>Bébé (moins de 2 ans) : vit dans les bras de sa mère.</summary>
    public bool EstBebe(long maintenant) => AgeEnAnnees(maintenant) < 2;

    /// <summary>Enfant ou bébé (moins de 14 ans) : suit ses parents, ne se reproduit pas.</summary>
    public bool EstEnfant(long maintenant) => AgeEnAnnees(maintenant) < 14;

    /// <summary>Fécondité de 0 à environ 1,6 selon l'âge et la biologie propre (0 : enfant, ménopause ou stérilité).</summary>
    public double Fecondite(long maintenant)
    {
        var age = AgeEnAnnees(maintenant);
        return Sexe == Sexe.Femme ? Biologie.FeconditeFemme(age) : Biologie.FeconditeHomme(age);
    }

    /// <summary>En âge de procréer (pubère, avant la ménopause) : l'instinct existe, même chez un humain stérile.</summary>
    public bool EstFecond(long maintenant)
    {
        var age = AgeEnAnnees(maintenant);
        return age >= 14 && age >= Biologie.AgePuberte && age < Biologie.AgeFinFertilite;
    }

    public double AgeEnAnnees(long maintenant) => (maintenant - Naissance) / (double)Temps.Calendrier.SecondesParAn;

    /// <summary>Copie indépendante (la mémoire et les liens aussi sont copiés).</summary>
    public Humain Copier()
    {
        var copie = (Humain)MemberwiseClone();
        copie.Memoire = Memoire.Copier();
        copie.Liens = [.. Liens];
        copie.Griefs = [.. Griefs];
        copie.Raisons = [.. Raisons];
        copie.Immunites = [.. Immunites];
        copie.Exclusions = [.. Exclusions];
        return copie;
    }
}
