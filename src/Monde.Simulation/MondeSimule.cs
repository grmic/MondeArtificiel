using Monde.Simulation.Temps;

namespace Monde.Simulation;

/// <summary>État sauvegardable du monde.</summary>
public sealed record EtatMonde(ulong Graine, long Secondes, long Pas, int NiveauVitesse);

/// <summary>
/// Le monde en cours de simulation. Le temps avance par pas fixes de 10 minutes du monde ;
/// les secondes non encore converties en pas sont conservées pour le prochain appel.
/// Les méthodes sont protégées par un verrou : le moteur et les clients peuvent l'appeler en parallèle.
/// </summary>
public sealed class MondeSimule
{
    public const long SecondesParPas = 600;

    private readonly object _verrou = new();
    private long _secondes;
    private long _pas;
    private Vitesse _vitesse;
    private double _reliquat;

    public MondeSimule(EtatMonde etat)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(etat.Secondes);
        ArgumentOutOfRangeException.ThrowIfNegative(etat.Pas);
        Graine = etat.Graine;
        _secondes = etat.Secondes;
        _pas = etat.Pas;
        _vitesse = Vitesse.Existe(etat.NiveauVitesse) ? Vitesse.Toutes[etat.NiveauVitesse] : Vitesse.Pause;
    }

    /// <summary>Nouveau monde : il commence à l'aube du premier jour, en pause.</summary>
    public static EtatMonde Nouveau(ulong graine) => new(graine, 6 * Calendrier.SecondesParHeure, 0, 0);

    public ulong Graine { get; }

    public Vitesse Vitesse { get { lock (_verrou) return _vitesse; } }

    public EtatMonde Etat
    {
        get { lock (_verrou) return new EtatMonde(Graine, _secondes, _pas, _vitesse.Niveau); }
    }

    public DateMonde Date { get { lock (_verrou) return Calendrier.Decomposer(_secondes); } }

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
            var avant = _secondes / SecondesParPas;
            _secondes += secondesMonde;
            var apres = _secondes / SecondesParPas;
            var nouveaux = (int)(apres - avant);
            for (var i = 0; i < nouveaux; i++)
            {
                ExecuterPas();
            }
            return nouveaux;
        }
    }

    /// <summary>Un pas de simulation (10 minutes du monde). Les humains y agiront à l'étape 4.</summary>
    private void ExecuterPas()
    {
        _pas++;
    }
}
