using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation;

/// <summary>
/// Population animale par espèce (0.44.2, choix B1 et D1) : une estimation lue par l'observateur, que la simulation ne lit jamais.
/// La simulation ne compte que le gibier de chaque case ; on le partage à parts égales entre les espèces de son terrain, on le
/// convertit en poids vivant (une part de gibier donne 1,2 kg de viande ; la viande fait environ la moitié du poids vivant d'un
/// grand herbivore), puis on divise par le poids d'une bête. Un relevé par mois est gardé en mémoire (pas dans la base) pour la
/// tendance sur un an.
/// </summary>
public sealed partial class MondeSimule
{
    /// <summary>Poids vivant (kg) que représente une part de gibier : 1,2 kg de viande, à environ 50 % du poids vivant.</summary>
    public const double KgVifParPartDeGibier = 2.4;

    private const int JoursParMois = 30;
    private const int RelevesGardes = 13;

    private readonly List<(long Secondes, double[] Sauvages)> _relevesFaune = [];

    /// <summary>Bêtes sauvages estimées de chaque espèce (index : numéro − 1). À appeler sous le verrou.</summary>
    private double[] EstimerSauvages()
    {
        var resultat = new double[Faune.Betes.Count];
        var biomes = Carte.Biomes;
        var gibier = _ecosysteme.Gibier;
        for (var i = 0; i < gibier.Length; i++)
        {
            if (gibier[i] == 0) continue;
            var especes = Faune.Sur((Biome)biomes[i]);
            if (especes.Count == 0) continue;
            var kg = gibier[i] * KgVifParPartDeGibier / especes.Count;
            foreach (var b in especes) resultat[b.Numero - 1] += kg / Math.Max(1, b.Poids);
        }
        return resultat;
    }

    /// <summary>Chaque nouveau jour : un relevé tous les trente jours, les treize derniers gardés.</summary>
    private void ReleverFaune()
    {
        if (_secondes / Calendrier.SecondesParJour % JoursParMois != 0 && _relevesFaune.Count > 0) return;
        _relevesFaune.Add((_secondes, EstimerSauvages()));
        if (_relevesFaune.Count > RelevesGardes) _relevesFaune.RemoveAt(0);
    }

    /// <summary>
    /// La population de chaque espèce : sauvages estimées maintenant et au plus ancien relevé gardé (au plus un an avant), et les
    /// bêtes gardées comptées. Aussi le gibier de chaque case, pour la carte.
    /// </summary>
    public PopulationFaune Population()
    {
        lock (_verrou)
        {
            var maintenant = EstimerSauvages();
            var avant = _relevesFaune.Count > 0 ? _relevesFaune[0] : ((long Secondes, double[] Sauvages)?)null;
            var especes = new List<PopulationEspece>(Faune.Betes.Count);
            foreach (var b in Faune.Betes)
            {
                var k = b.Numero - 1;
                var troupeaux = _champs.Troupeaux.Where(t => t.Espece == b.Numero).ToList();
                double? ancien = avant is { } a && a.Sauvages.Length > k && _secondes - a.Secondes >= Calendrier.SecondesParJour * JoursParMois
                    ? a.Sauvages[k] : null;
                especes.Add(new PopulationEspece(b.Numero, maintenant[k], ancien,
                    avant is { } r ? (int)((_secondes - r.Secondes) / (Calendrier.SecondesParJour * JoursParMois)) : 0,
                    troupeaux.Sum(t => t.Adultes), troupeaux.Sum(t => t.Petits), troupeaux.Count, troupeaux.Any(t => t.Domestique)));
            }
            return new PopulationFaune(especes, [.. _ecosysteme.Gibier], [.. _champs.Troupeaux.Select(t => (t, OuVitLeTroupeau(t)))]);
        }
    }
}

/// <summary>Une espèce : sauvages estimées, il y a <paramref name="Mois"/> mois (null : pas encore de relevé assez ancien), gardées.</summary>
public sealed record PopulationEspece(int Numero, double Sauvages, double? SauvagesAvant, int Mois, double GardeesAdultes, double GardeesPetits,
    int Troupeaux, bool Domestique);

/// <summary>Population de chaque espèce, gibier de chaque case, et troupeaux gardés avec la case où ils vivent ((−1, −1) : nulle part).</summary>
public sealed record PopulationFaune(List<PopulationEspece> Especes, ushort[] Gibier, List<(Troupeau Troupeau, (int X, int Y) Case)> Troupeaux);
