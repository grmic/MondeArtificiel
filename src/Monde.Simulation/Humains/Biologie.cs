using Monde.Simulation.Monde;

namespace Monde.Simulation.Humains;

/// <summary>
/// Traits biologiques innés, propres à chaque humain et transmis en partie à ses enfants :
/// d'une génération à l'autre, la population peut donc évoluer (ceux qui ont plus d'enfants transmettent plus).
/// </summary>
/// <param name="AgePuberte">Âge (années) où la fécondité commence.</param>
/// <param name="AgeFinFertilite">Âge (années) de la ménopause ; très élevé chez les hommes, dont la fécondité baisse sans s'arrêter.</param>
/// <param name="Fertilite">Facteur individuel autour de 1 (0 : stérile).</param>
public sealed record Biologie(double AgePuberte, double AgeFinFertilite, double Fertilite)
{
    /// <summary>Valeurs pour un humain enregistré avant l'existence de ces traits.</summary>
    public static Biologie ParDefaut(Sexe sexe) => new(16, sexe == Sexe.Femme ? 49 : 90, 1);

    /// <summary>Tirage pour un humain de départ (créé par l'Entité, sans parents).</summary>
    public static Biologie Tirer(Sexe sexe, Alea alea) => Construire(sexe, alea, 16, sexe == Sexe.Femme ? 49 : 90, 1);

    /// <summary>Tirage pour un nouveau-né : moyenne des parents de même nature, plus une variation propre.</summary>
    public static Biologie Heriter(Sexe sexe, Humain mere, Humain? pere, Alea alea)
    {
        var puberte = pere is null ? mere.Biologie.AgePuberte : (mere.Biologie.AgePuberte + pere.Biologie.AgePuberte) / 2;
        // La ménopause se transmet surtout par la lignée maternelle.
        var fin = sexe == Sexe.Femme ? mere.Biologie.AgeFinFertilite : 90;
        var parents = pere is null ? mere.Biologie.Fertilite : (mere.Biologie.Fertilite + pere.Biologie.Fertilite) / 2;
        // Moitié héritée, moitié propre : un enfant de parents peu féconds ne l'est pas forcément.
        return Construire(sexe, alea, puberte, fin, 0.5 + parents / 2);
    }

    private static Biologie Construire(Sexe sexe, Alea alea, double puberte, double fin, double fertilite)
    {
        // Puberté des chasseurs-cueilleurs : vers 16 ans (écart-type d'environ 1,2 an).
        var agePuberte = Math.Clamp(puberte + Normale(alea) * 1.2, 12, 20);
        // Ménopause : vers 49 ans (écart-type d'environ 3,5 ans).
        var ageFin = sexe == Sexe.Femme ? Math.Clamp(fin + Normale(alea) * 3.5, 40, 58) : 90;
        // Environ 3 % de stérilité primaire dans les populations sans contraception.
        var facteur = alea.Reel() < 0.03 ? 0 : Math.Clamp(fertilite + Normale(alea) * 0.2, 0.3, 1.6);
        return new Biologie(agePuberte, ageFin, facteur);
    }

    /// <summary>Loi normale centrée réduite approchée (somme de 12 uniformes) : arithmétique simple, résultat identique sur tout processeur.</summary>
    public static double Normale(Alea alea)
    {
        var somme = 0.0;
        for (var i = 0; i < 12; i++) somme += alea.Reel();
        return somme - 6;
    }

    /// <summary>
    /// Fécondité relative d'une femme selon l'âge (populations sans contraception,
    /// Menken, Trussell et Larsen 1986) : 1 à 20-24 ans, 0,86 à 30-34 ans, 0,36 à 40-44 ans.
    /// Les premières années après la puberté sont peu fécondes (sous-fécondité de l'adolescence).
    /// </summary>
    public double FeconditeFemme(double age)
    {
        if (age < AgePuberte || age >= AgeFinFertilite) return 0;
        var adolescence = Math.Min(1, 0.3 + 0.7 * (age - AgePuberte) / 4);
        double selonAge = age switch
        {
            < 25 => 1.0,
            < 30 => 0.94,
            < 35 => 0.86,
            < 40 => 0.70,
            < 45 => 0.36,
            _ => 0.05,
        };
        return Fertilite * adolescence * selonAge;
    }

    /// <summary>Fécondité d'un homme : elle baisse lentement après 40 ans, sans s'arrêter net.</summary>
    public double FeconditeHomme(double age)
    {
        if (age < AgePuberte) return 0;
        var selonAge = age < 40 ? 1 : Math.Max(0.1, 1 - (age - 40) / 50);
        return Fertilite * selonAge;
    }

    /// <summary>Risque de fausse couche selon l'âge de la mère (Nybo Andersen et al., BMJ 2000).</summary>
    public static double RisqueFausseCouche(double ageMere) => ageMere switch
    {
        < 20 => 0.13,
        < 30 => 0.09,
        < 35 => 0.11,
        < 40 => 0.20,
        < 45 => 0.40,
        _ => 0.75,
    };
}
