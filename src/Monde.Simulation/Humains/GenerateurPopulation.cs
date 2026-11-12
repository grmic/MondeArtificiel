using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Crée les 33 couples de départ, un par emplacement. Tout est tiré au hasard depuis la graine.</summary>
public static class GenerateurPopulation
{
    /// <summary>Âges extrêmes des fondateurs au premier jour.</summary>
    public const double AgeMin = 14, AgeMax = 40;

    /// <summary>
    /// Pente de la pyramide des âges des fondateurs (génération 4) : la part de chaque âge décroît d'environ 2,3 % par an de 14 à 40 ans,
    /// comme dans une population de chasseurs-cueilleurs (survie de 0,60 à 15 ans et 0,43 à 40 ans, croissance d'environ 1 % par an ;
    /// Gurven et Kaplan, 2007) : plus de jeunes adultes que de quadragénaires.
    /// </summary>
    public const double PenteAges = 0.023;

    public static List<Humain> Generer(Carte carte, ulong graine, long maintenant, int generation = 0)
    {
        var alea = new Alea(graine ^ 0x48554D41494E53UL); // flux distinct de celui de la carte
        // Génération 4 (0.39.0) : les âges viennent d'un flux à part ; le flux principal est tiré comme avant, le reste ne change pas.
        var aleaAges = generation >= 4 ? new Alea(graine ^ 0x4147455346UL) : null;
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
            // Avant la génération 4, l'âge de l'homme butait sur 40 ans : un fondateur sur quatre avait de 35 à 40 ans.
            if (aleaAges is not null) (ageFemme, ageHomme) = AgesDuCouple(aleaAges);
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
                h.Croyance = h.CroyanceNotee = Croyances.Initiale(id, h.Personnalite.Transcendance);
            }
        }
        return humains;
    }

    /// <summary>
    /// Âges d'un couple de fondateurs (génération 4) : la femme selon la pyramide (<see cref="PenteAges"/>), l'homme plus âgé de
    /// 3 ans en moyenne (écart-type 4), tiré à nouveau tant qu'il sort de 14-40 ans plutôt que ramené à la borne.
    /// </summary>
    internal static (double Femme, double Homme) AgesDuCouple(Alea alea)
    {
        // Loi exponentielle tronquée à [AgeMin, AgeMax], tirée par inversion.
        var etendue = 1 - Math.Exp(-PenteAges * (AgeMax - AgeMin));
        var femme = AgeMin - Math.Log(1 - alea.Reel() * etendue) / PenteAges;
        for (var essai = 0; essai < 50; essai++)
        {
            var homme = femme + 3 + 4 * Biologie.Normale(alea);
            if (homme is >= AgeMin and <= AgeMax) return (femme, homme);
        }
        return (femme, Math.Clamp(femme + 3, AgeMin, AgeMax));
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
        var bebe = new Humain
        {
            Id = id,
            Nom = Noms.Nouveau(alea, nomsPris),
            Sexe = sexe,
            PartenaireId = 0,
            MereId = mere.Id,
            CommunauteNaissance = mere.CommunauteId,
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
        // Sa croyance en l'invisible se règlera ensuite sur celle des siens (voir Croyances.Journee).
        bebe.Croyance = bebe.CroyanceNotee = Croyances.Initiale(id, bebe.Personnalite.Transcendance);
        return bebe;
    }
}
