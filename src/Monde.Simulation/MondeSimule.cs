using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation;

/// <summary>
/// État sauvegardable du monde. Les champs facultatifs sont absents pour un monde
/// qui n'a pas encore d'humains : ils sont alors créés à partir de la graine.
/// </summary>
public sealed record EtatMonde(ulong Graine, long Secondes, long Pas, int NiveauVitesse)
{
    public IReadOnlyList<Humain>? Humains { get; init; }
    public ushort[]? Gibier { get; init; }
    public ushort[]? Plantes { get; init; }
    public ulong[]? Alea { get; init; }
}

/// <summary>
/// Le monde en cours de simulation. Le temps avance par pas fixes de 10 minutes du monde ;
/// les secondes non encore converties en pas sont conservées pour le prochain appel.
/// Les méthodes sont protégées par un verrou : le moteur et les clients peuvent l'appeler en parallèle.
/// Tout le hasard vient d'un seul générateur sauvegardé : un monde rechargé continue exactement
/// comme s'il n'avait jamais été arrêté.
/// </summary>
public sealed class MondeSimule
{
    public const long SecondesParPas = 600;
    private const long PasParHeure = Calendrier.SecondesParHeure / SecondesParPas;
    private const long PasParJour = Calendrier.SecondesParJour / SecondesParPas;

    private readonly object _verrou = new();
    private readonly List<Humain> _humains;
    private readonly Ecosysteme _ecosysteme;
    private readonly Alea _alea;
    private readonly Comportement _comportement;
    private readonly HashSet<string> _noms;
    private long _secondes;
    private long _pas;
    private Vitesse _vitesse;
    private double _reliquat;

    public MondeSimule(Carte carte, EtatMonde etat)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(etat.Secondes);
        ArgumentOutOfRangeException.ThrowIfNegative(etat.Pas);
        if (carte.Graine != etat.Graine)
        {
            throw new ArgumentException("La carte ne correspond pas à la graine du monde.", nameof(carte));
        }

        Carte = carte;
        Graine = etat.Graine;
        _secondes = etat.Secondes;
        _pas = etat.Pas;
        _vitesse = Vitesse.Existe(etat.NiveauVitesse) ? Vitesse.Toutes[etat.NiveauVitesse] : Vitesse.Pause;
        _humains = etat.Humains is { Count: > 0 } h
            ? h.Select(x => x.Copier()).OrderBy(x => x.Id).ToList()
            : GenerateurPopulation.Generer(carte, etat.Graine, etat.Secondes);
        _ecosysteme = new Ecosysteme(carte, etat.Gibier?.ToArray(), etat.Plantes?.ToArray());
        _alea = etat.Alea is { } a ? Alea.Depuis(a) : new Alea(etat.Graine ^ 0x534D554C4154UL);
        _comportement = new Comportement(carte, _ecosysteme, _alea);
        _noms = _humains.Select(x => x.Nom).ToHashSet();

        for (var i = 0; i < _humains.Count; i++)
        {
            if (_humains[i].Id != i + 1) throw new ArgumentException("Les identifiants des humains doivent aller de 1 à N.");
        }
    }

    /// <summary>Nouveau monde : il commence à l'aube du premier jour, en pause.</summary>
    public static EtatMonde Nouveau(ulong graine) => new(graine, 6 * Calendrier.SecondesParHeure, 0, 0);

    public Carte Carte { get; }

    public ulong Graine { get; }

    public Vitesse Vitesse { get { lock (_verrou) return _vitesse; } }

    public DateMonde Date { get { lock (_verrou) return Calendrier.Decomposer(_secondes); } }

    /// <summary>Copie complète de l'état, pour la sauvegarde.</summary>
    public EtatMonde Etat
    {
        get
        {
            lock (_verrou)
            {
                return new EtatMonde(Graine, _secondes, _pas, _vitesse.Niveau)
                {
                    Humains = _humains.Select(h => h.Copier()).ToList(),
                    Gibier = _ecosysteme.Gibier.ToArray(),
                    Plantes = _ecosysteme.Plantes.ToArray(),
                    Alea = _alea.Etat,
                };
            }
        }
    }

    /// <summary>Copie légère des humains seulement (diffusion fréquente vers les navigateurs).</summary>
    public (long Secondes, List<Humain> Humains) InstantaneHumains()
    {
        lock (_verrou) return (_secondes, _humains.Select(h => h.Copier()).ToList());
    }

    public void ChangerVitesse(int niveau)
    {
        if (!Vitesse.Existe(niveau)) throw new ArgumentOutOfRangeException(nameof(niveau), niveau, "Vitesse inconnue.");
        lock (_verrou)
        {
            _vitesse = Vitesse.Toutes[niveau];
            _reliquat = 0;
        }
    }

    /// <summary>
    /// Fait avancer le monde d'une durée réelle, selon la vitesse courante.
    /// Renvoie le nombre de pas de simulation exécutés.
    /// </summary>
    public int Avancer(TimeSpan dureeReelle)
    {
        if (dureeReelle <= TimeSpan.Zero) return 0;
        lock (_verrou)
        {
            if (_vitesse.Facteur == 0) return 0;
            // Une pause du programme (veille de l'ordinateur) ne doit pas faire sauter des années :
            // l'écart pris en compte est plafonné à une seconde réelle.
            var reel = Math.Min(dureeReelle.TotalSeconds, 1.0);
            _reliquat += reel * _vitesse.Facteur;
            var secondes = (long)_reliquat;
            _reliquat -= secondes;
            return AvancerDe(secondes);
        }
    }

    /// <summary>Avance d'un nombre exact de secondes du monde (utile pour les tests et l'accéléré).</summary>
    public int AvancerDe(long secondesMonde)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(secondesMonde);
        lock (_verrou)
        {
            var fin = _secondes + secondesMonde;
            var avant = _secondes / SecondesParPas;
            var nouveaux = (int)(fin / SecondesParPas - avant);
            for (var k = 1; k <= nouveaux; k++)
            {
                // Chaque pas s'exécute à l'instant exact où il tombe.
                _secondes = (avant + k) * SecondesParPas;
                ExecuterPas();
            }
            _secondes = fin;
            return nouveaux;
        }
    }

    /// <summary>Un pas de simulation (10 minutes du monde).</summary>
    private void ExecuterPas()
    {
        _pas++;
        if (_pas % PasParHeure == 0) _ecosysteme.Repousser();
        var nouveauJour = _pas % PasParJour == 0;

        // La liste peut grandir pendant le pas : on ne fait vivre que ceux qui existaient au début.
        var nombre = _humains.Count;
        for (var i = 0; i < nombre; i++)
        {
            var humain = _humains[i];
            _comportement.Vivre(humain, _humains, _secondes);
            if (nouveauJour) _comportement.Vieillir(humain, _secondes);
        }

        foreach (var (mere, pereId) in _comportement.Accouchements)
        {
            var pere = pereId > 0 ? _humains[pereId - 1] : null;
            _humains.Add(GenerateurPopulation.Naissance(_humains.Count + 1, mere, pere, _secondes, _alea, _noms));
        }
        _comportement.Accouchements.Clear();
    }
}
