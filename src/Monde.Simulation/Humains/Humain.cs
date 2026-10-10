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
}

public enum CauseDeMort : byte
{
    Aucune = 0,
    Faim = 1,
    Soif = 2,
    Froid = 3,
    Maladie = 4,
    Epuisement = 5,
}

/// <summary>Traits de caractère, chacun entre 0 et 1, tirés au hasard à la naissance.</summary>
public sealed record Personnalite(double Curiosite, double Prudence, double Sociabilite, double Endurance, double Adresse);

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
    public required int PartenaireId { get; init; }
    public required int Emplacement { get; init; }

    /// <summary>Date de naissance en secondes du monde (négative si avant l'an 1).</summary>
    public required long Naissance { get; init; }

    public required Personnalite Personnalite { get; init; }

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

    public long? MortLe { get; set; }
    public CauseDeMort CauseDeMort { get; set; }

    public bool EstVivant => MortLe is null;

    public bool EstMalade(long maintenant) => MaladeJusqua > maintenant;

    public double AgeEnAnnees(long maintenant) => (maintenant - Naissance) / (double)Temps.Calendrier.SecondesParAn;

    public Humain Copier() => (Humain)MemberwiseClone();
}
