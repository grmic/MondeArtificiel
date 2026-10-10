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
                    Personnalite = Psyche.Tirer(alea, AttachementInne(alea)),
                    Biologie = Biologie.Tirer(sexe, alea),
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

    /// <summary>La grande majorité des humains s'attachent fortement à leurs enfants ; une minorité peu ou pas.</summary>
    private static double AttachementInne(Alea alea) =>
        alea.Reel() < 0.85 ? 0.6 + 0.4 * alea.Reel() : 0.6 * alea.Reel();

    /// <summary>Part des garçons à la naissance (environ 105 garçons pour 100 filles).</summary>
    public const double ProportionGarcons = 0.512;

    /// <summary>
    /// Un nouveau-né. Son caractère et son intelligence viennent en partie de ses parents, en partie du hasard.
    /// </summary>
    public static Humain Naissance(int id, Humain mere, Humain? pere, long maintenant, Alea alea, ISet<string> nomsPris)
    {
        var attachement = Math.Clamp((pere is null ? mere.Personnalite.Attachement
            : (mere.Personnalite.Attachement + pere.Personnalite.Attachement) / 2) + (alea.Reel() - 0.5) * 0.4, 0, 1);

        var sexe = alea.Reel() < ProportionGarcons ? Sexe.Homme : Sexe.Femme;
        return new Humain
        {
            Id = id,
            Nom = Noms.Nouveau(alea, nomsPris),
            Sexe = sexe,
            PartenaireId = 0,
            MereId = mere.Id,
            PereId = pere?.Id ?? 0,
            Emplacement = mere.Emplacement,
            Naissance = maintenant,
            Personnalite = Psyche.Heriter(mere, pere, alea, attachement),
            Biologie = Biologie.Heriter(sexe, mere, pere, alea),
            X = mere.X,
            Y = mere.Y,
            Activite = Activite.PorteParSaMere,
        };
    }
}
