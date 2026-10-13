using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Crée les 33 couples de départ, un par emplacement. Tout est tiré au hasard depuis la graine.</summary>
public static class GenerateurPopulation
{
    /// <summary>Âges extrêmes des fondateurs au premier jour.</summary>
    public const double AgeMin = 14, AgeMax = 40;

    public static List<Humain> Generer(Carte carte, ulong graine, long maintenant)
    {
        var alea = new Alea(graine ^ 0x48554D41494E53UL); // flux distinct de celui de la carte
        var noms = Noms.Generer(alea, carte.Emplacements.Count * 2);
        var humains = new List<Humain>(noms.Count);

        foreach (var emplacement in carte.Emplacements)
        {
            var idFemme = humains.Count + 1;
            var idHomme = idFemme + 1;
            // Des fondateurs de tous âges, de 14 à 40 ans (choix de zem) : sinon tous vieillissent et meurent en même temps,
            // et la population s'effondre une génération plus tard. L'homme est le plus souvent un peu plus âgé que sa compagne.
            var ageFemme = AgeMin + alea.Reel() * (AgeMax - AgeMin);
            var ageHomme = Math.Clamp(ageFemme + 3 + 4 * Biologie.Normale(alea), AgeMin, AgeMax);
            foreach (var (id, sexe, partenaire) in new[] { (idFemme, Sexe.Femme, idHomme), (idHomme, Sexe.Homme, idFemme) })
            {
                var age = sexe == Sexe.Femme ? ageFemme : ageHomme;
                humains.Add(new Humain
                {
                    Id = id,
                    Nom = noms[id - 1],
                    Sexe = sexe,
                    PartenaireId = partenaire,
                    Emplacement = emplacement.Numero,
                    Naissance = maintenant - (long)(age * Calendrier.SecondesParAn),
                    Personnalite = Psyche.Tirer(alea, AttachementInne(alea), sexe),
                    Biologie = Biologie.Tirer(sexe, alea),
                    X = emplacement.X,
                    Y = emplacement.Y,
                    Faim = alea.Reel() * 30,
                    Soif = alea.Reel() * 30,
                    Fatigue = alea.Reel() * 30,
                    Reserves = 40 + alea.Reel() * 30,
                });
                // Apparence et orientation viennent d'un flux à part, tiré du numéro : le reste du monde n'en dépend pas.
                var h = humains[^1];
                (h.Apparence, h.Orientation, h.Lucidite) = Attirance.De(id, sexe, h.Personnalite.Ouverture);
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
    /// Un nouveau-né. Son caractère, son intelligence, son apparence et en partie son orientation viennent de ses parents,
    /// le reste du hasard. Un enfant attiré par son propre sexe ne le sait pas encore : il le découvrira peut-être en grandissant.
    /// </summary>
    public static Humain Naissance(int id, Humain mere, Humain? pere, long maintenant, Alea alea, ISet<string> nomsPris,
        IReadOnlyList<Humain>? tous = null)
    {
        var attachement = Math.Clamp((pere is null ? mere.Personnalite.Attachement
            : (mere.Personnalite.Attachement + pere.Personnalite.Attachement) / 2) + (alea.Reel() - 0.5) * 0.4, 0, 1);

        var sexe = alea.Reel() < ProportionGarcons ? Sexe.Homme : Sexe.Femme;
        var penchant = pere is null ? Attirance.PourSonSexe(mere) : Math.Max(Attirance.PourSonSexe(mere), Attirance.PourSonSexe(pere));
        var freresAines = sexe == Sexe.Homme ? (tous ?? []).Count(a => a.MereId == mere.Id && a.Sexe == Sexe.Homme) : 0;
        var orientation = Attirance.TirerOrientation(sexe, alea, penchant, freresAines);
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
            Personnalite = Psyche.Heriter(mere, pere, alea, attachement, sexe, maintenant),
            Biologie = Biologie.Heriter(sexe, mere, pere, alea),
            Apparence = Attirance.HeriterApparence(mere.Apparence, pere?.Apparence, alea),
            Orientation = orientation,
            Lucidite = orientation.Vers(sexe) < 0.15 ? 1 : 0,
            X = mere.X,
            Y = mere.Y,
            Activite = Activite.PorteParSaMere,
            // Un nouveau-né naît avec un peu de graisse, d'autant plus que sa mère était bien nourrie.
            Reserves = Math.Clamp(15 + mere.Reserves / 4, 15, 40),
        };
    }
}
