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

    /// <summary>
    /// Bêtes sauvages estimées de chaque espèce (index : numéro − 1, puis les grands nageurs à la suite). À appeler sous le verrou.
    /// 0.45.0 : là où vivent les prédateurs (mondes au relief), les carnassiers sont comptés comme la simulation les compte, d'après
    /// les proies que la terre porte (<see cref="Comportement.Carnassiers"/>) ; le gibier se partage entre les autres espèces.
    /// </summary>
    private double[] EstimerSauvages()
    {
        var resultat = new double[Faune.Betes.Count + Faune.Nageurs.Count];
        var biomes = Carte.Biomes;
        var gibier = _ecosysteme.Gibier;
        var predateurs = (Configuration?.Generation ?? 0) >= 2;
        for (var i = 0; i < gibier.Length; i++)
        {
            var especes = Faune.Sur((Biome)biomes[i]);
            if (predateurs)
            {
                foreach (var (b, nombre, _) in _comportement.Carnassiers(i % Carte.Largeur, i / Carte.Largeur))
                    resultat[Index(b.Numero)] += nombre;
                especes = [.. especes.Where(b => b.Regime != RegimeBete.Carnivore)];
            }
            if (gibier[i] == 0 || especes.Count == 0) continue;
            var kg = gibier[i] * KgVifParPartDeGibier / especes.Count;
            foreach (var b in especes) resultat[b.Numero - 1] += kg / Math.Max(1, b.Poids);
        }
        return resultat;
    }

    /// <summary>Place d'une espèce dans les relevés : les bêtes de la faune, puis les grands nageurs.</summary>
    private int Index(int numero) => numero >= Faune.PremierNageur ? Faune.Betes.Count + numero - Faune.PremierNageur : numero - 1;

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
            foreach (var b in (Configuration?.Generation ?? 0) >= 2 ? Faune.Betes.Concat(Faune.Nageurs) : Faune.Betes)
            {
                var k = Index(b.Numero);
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

public sealed partial class MondeSimule
{
    /// <summary>Les bêtes que les humains connaissent (vivantes ou tuées depuis moins de deux ans) et les attaques de l'année (0.45.1).</summary>
    public (List<Fauve> Connus, List<AttaqueDeBete> Attaques) FauvesConnus()
    {
        lock (_verrou) return ([.. _comportement.Fauves], [.. _comportement.AttaquesRecentes]);
    }

    /// <summary>Une attaque de l'année d'après sa ligne du journal (0.45.1) : l'espèce, la bête connue si elle vit encore (son groupe du flux Camp, sinon 0), la case.</summary>
    public (int Espece, int Groupe, int X, int Y)? AttaqueDuJournal(long evenement)
    {
        lock (_verrou)
        {
            var a = _comportement.AttaquesRecentes.Find(x => x.EvenementId == evenement);
            if (a is null) return null;
            var vit = _comportement.Fauves.Exists(f => f.Id == a.FauveId && f.EnVie);
            return (a.Espece, vit ? -(IdFauveScene + a.FauveId) : 0, a.X, a.Y);
        }
    }

    /// <summary>
    /// Où aller voir une espèce (0.45.1) : la case la plus proche de (x, y) où la vue 3D en montre une, et le groupe à suivre dans le
    /// flux Camp ; une bête connue vivante d'abord, pour un carnassier. Null si elle n'est montrée nulle part.
    /// </summary>
    public (int X, int Y, int Groupe)? OuVoirEspece(int numero, int x, int y)
    {
        lock (_verrou)
        {
            if (!Faune.Existe(numero)) return null;
            var b = Faune.Get(numero);
            (int X, int Y, int Groupe)? meilleur = null;
            var dMin = double.MaxValue;
            void Voir(int cx, int cy, int groupe)
            {
                var d = Math.Abs(cx - x) + Math.Abs(cy - y);
                if (d < dMin) (dMin, meilleur) = (d, (cx, cy, groupe));
            }
            var predateurs = (Configuration?.Generation ?? 0) >= 2;
            if (predateurs)
                foreach (var f in _comportement.Fauves)
                    if (f.EnVie && f.Espece == numero) Voir(f.X, f.Y, -(IdFauveScene + f.Id));
            if (meilleur is not null) return meilleur;
            var carte = Carte;
            for (var i = 0; i < carte.Biomes.Length; i++)
            {
                var cx = i % carte.Largeur;
                var cy = i / carte.Largeur;
                if (b.Eau)
                {
                    if (!predateurs || Faune.NageurDe((Biome)carte.Biomes[i]) is not { } n || n.Numero != numero) continue;
                    var nombre = _comportement.Carnassiers(cx, cy).Find(c => c.Bete.Numero == numero).Nombre;
                    var tirage = ((uint)(cx * 2246822519u ^ cy * 3266489917u) >> 8) / (double)(1 << 24);
                    if (tirage < nombre) Voir(cx, cy, -(IdNageurScene + i));
                    continue;
                }
                if (_ecosysteme.Gibier[i] < 500 || Faune.VueSur((Biome)carte.Biomes[i], cx, cy) != b) continue;
                // Comme dans le flux Camp : un troupeau sur trois cases environ.
                var hache = (uint)(cx * 2654435761u ^ cy * 40503u) >> 7;
                if (b.Hierarchie && hache % 3 != 0) continue;
                Voir(cx, cy, -(i + 1));
            }
            return meilleur;
        }
    }
}

/// <summary>Une espèce : sauvages estimées, il y a <paramref name="Mois"/> mois (null : pas encore de relevé assez ancien), gardées.</summary>
public sealed record PopulationEspece(int Numero, double Sauvages, double? SauvagesAvant, int Mois, double GardeesAdultes, double GardeesPetits,
    int Troupeaux, bool Domestique);

/// <summary>Population de chaque espèce, gibier de chaque case, et troupeaux gardés avec la case où ils vivent ((−1, −1) : nulle part).</summary>
public sealed record PopulationFaune(List<PopulationEspece> Especes, ushort[] Gibier, List<(Troupeau Troupeau, (int X, int Y) Case)> Troupeaux);
