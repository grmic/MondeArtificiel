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
}

/// <summary>Traits de caractère, chacun entre 0 et 1, tirés au hasard à la naissance.</summary>
/// <remarks>
/// Attachement : force du lien avec ses enfants. Élevé chez la plupart, faible chez quelques-uns ;
/// un parent peu attaché s'éloigne de ses enfants, et peut abandonner un bébé quand il souffre.
/// </remarks>
public sealed record Personnalite(double Curiosite, double Prudence, double Sociabilite, double Endurance, double Adresse,
    double Attachement);

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

    public int X { get; set; }
    public int Y { get; set; }
    public double Faim { get; set; }
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

    public Humain Copier() => (Humain)MemberwiseClone();
}
