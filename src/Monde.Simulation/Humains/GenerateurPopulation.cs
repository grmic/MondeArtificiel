using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Crée les 33 couples de départ, un par emplacement. Tout est tiré au hasard depuis la graine.</summary>
public static class GenerateurPopulation
{
    public static List<Humain> Generer(Carte carte, ulong graine, long maintenant)
    {
        var alea = new Alea(graine ^ 0x48554D41494E53UL); // flux distinct de celui de la carte
        var noms = Noms.Generer(alea, carte.Emplacements.Count * 2);
        var humains = new List<Humain>(noms.Count);

        foreach (var emplacement in carte.Emplacements)
        {
            var idFemme = humains.Count + 1;
            var idHomme = idFemme + 1;
            foreach (var (id, sexe, partenaire) in new[] { (idFemme, Sexe.Femme, idHomme), (idHomme, Sexe.Homme, idFemme) })
            {
                var age = 18 + alea.Reel() * 8; // jeunes adultes de 18 à 26 ans
                humains.Add(new Humain
                {
                    Id = id,
                    Nom = noms[id - 1],
                    Sexe = sexe,
                    PartenaireId = partenaire,
                    Emplacement = emplacement.Numero,
                    Naissance = maintenant - (long)(age * Calendrier.SecondesParAn),
                    Personnalite = new Personnalite(alea.Reel(), alea.Reel(), alea.Reel(), alea.Reel(), alea.Reel()),
                    X = emplacement.X,
                    Y = emplacement.Y,
                    Faim = alea.Reel() * 30,
                    Soif = alea.Reel() * 30,
                    Fatigue = alea.Reel() * 30,
                });
            }
        }
        return humains;
    }

    /// <summary>
    /// Un nouveau-né. Son caractère mêle ceux de ses parents, avec une part de hasard.
    /// </summary>
    public static Humain Naissance(int id, Humain mere, Humain? pere, long maintenant, Alea alea, ISet<string> nomsPris)
    {
        double Herite(Func<Personnalite, double> trait)
        {
            var moyenne = pere is null ? trait(mere.Personnalite) : (trait(mere.Personnalite) + trait(pere.Personnalite)) / 2;
            return Math.Clamp(moyenne + (alea.Reel() - 0.5) * 0.4, 0, 1);
        }

        return new Humain
        {
            Id = id,
            Nom = Noms.Nouveau(alea, nomsPris),
            Sexe = alea.Entier(2) == 0 ? Sexe.Femme : Sexe.Homme,
            PartenaireId = 0,
            MereId = mere.Id,
            PereId = pere?.Id ?? 0,
            Emplacement = mere.Emplacement,
            Naissance = maintenant,
            Personnalite = new Personnalite(
                Herite(p => p.Curiosite), Herite(p => p.Prudence), Herite(p => p.Sociabilite),
                Herite(p => p.Endurance), Herite(p => p.Adresse)),
            X = mere.X,
            Y = mere.Y,
            Activite = Activite.PorteParSaMere,
        };
    }
}
