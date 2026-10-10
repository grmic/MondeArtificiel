using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation;

/// <summary>
/// État sauvegardable du monde. Les champs facultatifs sont absents pour un monde
/// qui n'a pas encore d'humains : ils sont alors créés à partir de la graine.
/// </summary>
/// <param name="Facteur">Vitesse : secondes du monde par seconde réelle (0 = pause).</param>
public sealed record EtatMonde(ulong Graine, long Secondes, long Pas, double Facteur)
{
    public IReadOnlyList<Humain>? Humains { get; init; }
    public ushort[]? Gibier { get; init; }
    public ushort[]? Plantes { get; init; }
    public ulong[]? Alea { get; init; }

    /// <summary>Événements du journal pas encore enregistrés (ils s'ajoutent au journal sauvegardé).</summary>
    public IReadOnlyList<EvenementJournal>? JournalNonEnregistre { get; init; }

    /// <summary>Numéro que portera le prochain événement du journal.</summary>
    public long ProchainIdJournal { get; init; } = 1;

    /// <summary>Communautés, actives ou dispersées (numérotées de 1 à N).</summary>
    public IReadOnlyList<Communaute>? Communautes { get; init; }
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
    private readonly VieSociale _social;
    private readonly List<Communaute> _communautes;
    private readonly HashSet<string> _noms;
    private long _secondes;
    private long _pas;
    private double _facteur;
    private double _reliquat;
    private Objectif? _objectif;
    private readonly List<EvenementJournal> _journalNonEnregistre = [];
    private readonly Queue<EvenementJournal> _journalRecent = new();
    private long _prochainIdJournal;

    /// <summary>Nombre d'événements récents gardés en mémoire pour les navigateurs (hors événements fréquents).</summary>
    public const int TailleJournalRecent = 500;
    private string? _objectifAtteint;

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
        _facteur = Vitesse.FacteurValide(etat.Facteur) ? etat.Facteur : 0;
        _humains = etat.Humains is { Count: > 0 } h
            ? h.Select(x => x.Copier()).OrderBy(x => x.Id).ToList()
            : GenerateurPopulation.Generer(carte, etat.Graine, etat.Secondes);
        _ecosysteme = new Ecosysteme(carte, etat.Gibier?.ToArray(), etat.Plantes?.ToArray());
        _alea = etat.Alea is { } a ? Alea.Depuis(a) : new Alea(etat.Graine ^ 0x534D554C4154UL);
        _social = new VieSociale(_alea);
        _comportement = new Comportement(carte, _ecosysteme, _alea) { Social = _social };
        _communautes = (etat.Communautes ?? []).Select(c => c.Copier()).OrderBy(c => c.Id).ToList();
        _noms = _humains.Select(x => x.Nom).ToHashSet();
        _prochainIdJournal = Math.Max(1, etat.ProchainIdJournal);
        foreach (var e in etat.JournalNonEnregistre ?? []) Ajouter(e);

        for (var i = 0; i < _humains.Count; i++)
        {
            if (_humains[i].Id != i + 1) throw new ArgumentException("Les identifiants des humains doivent aller de 1 à N.");
        }
    }

    /// <summary>Nouveau monde : il commence à l'aube du premier jour, en pause.</summary>
    public static EtatMonde Nouveau(ulong graine) => new(graine, 6 * Calendrier.SecondesParHeure, 0, 0);

    public Carte Carte { get; }

    public ulong Graine { get; }

    /// <summary>Vitesse demandée : secondes du monde par seconde réelle (0 = pause).</summary>
    public double Facteur { get { lock (_verrou) return _facteur; } }

    /// <summary>Objectif « avancer jusqu'à » en cours, ou null.</summary>
    public Objectif? Objectif { get { lock (_verrou) return _objectif; } }

    public long Secondes { get { lock (_verrou) return _secondes; } }

    /// <summary>Date, pas, vitesse et objectif lus ensemble, sans copier les humains (diffusion fréquente).</summary>
    public (long Secondes, long Pas, double Facteur, Objectif? Objectif) Horloge
    {
        get { lock (_verrou) return (_secondes, _pas, _facteur, _objectif); }
    }

    public DateMonde Date { get { lock (_verrou) return Calendrier.Decomposer(_secondes); } }

    /// <summary>Copie complète de l'état, pour la sauvegarde.</summary>
    public EtatMonde Etat
    {
        get
        {
            lock (_verrou)
            {
                return new EtatMonde(Graine, _secondes, _pas, _facteur)
                {
                    Humains = _humains.Select(h => h.Copier()).ToList(),
                    Gibier = _ecosysteme.Gibier.ToArray(),
                    Plantes = _ecosysteme.Plantes.ToArray(),
                    Alea = _alea.Etat,
                    JournalNonEnregistre = _journalNonEnregistre.ToList(),
                    ProchainIdJournal = _prochainIdJournal,
                    Communautes = _communautes.Select(c => c.Copier()).ToList(),
                };
            }
        }
    }

    /// <summary>Après une sauvegarde : les <paramref name="nombre"/> premiers événements en attente sont enregistrés.</summary>
    public void MarquerJournalEnregistre(int nombre)
    {
        lock (_verrou) _journalNonEnregistre.RemoveRange(0, Math.Min(nombre, _journalNonEnregistre.Count));
    }

    /// <summary>Événements récents dont le numéro dépasse <paramref name="apresId"/>, du plus ancien au plus récent.</summary>
    public List<EvenementJournal> JournalRecent(long apresId = 0)
    {
        lock (_verrou) return _journalRecent.Where(e => e.Id > apresId).ToList();
    }

    private void Ajouter(EvenementJournal evenement)
    {
        var numerote = evenement.Id > 0 ? evenement : evenement with { Id = _prochainIdJournal };
        _prochainIdJournal = Math.Max(_prochainIdJournal, numerote.Id + 1);
        _journalNonEnregistre.Add(numerote);
        if (TypesEvenement.EstFrequent(numerote.Type)) return;
        _journalRecent.Enqueue(numerote);
        if (_journalRecent.Count > TailleJournalRecent) _journalRecent.Dequeue();
    }

    /// <summary>Copie des communautés (actives et dispersées).</summary>
    public List<Communaute> Communautes()
    {
        lock (_verrou) return _communautes.Select(c => c.Copier()).ToList();
    }

    /// <summary>Copie légère des humains seulement (diffusion fréquente vers les navigateurs).</summary>
    public (long Secondes, List<Humain> Humains) InstantaneHumains()
    {
        lock (_verrou) return (_secondes, _humains.Select(h => h.Copier()).ToList());
    }

    /// <summary>Choisit une vitesse préréglée (0 = pause). Annule un objectif en cours.</summary>
    public void ChangerVitesse(int niveau)
    {
        if (!Vitesse.Existe(niveau)) throw new ArgumentOutOfRangeException(nameof(niveau), niveau, "Vitesse inconnue.");
        ChangerFacteur(Vitesse.Toutes[niveau].Facteur);
    }

    /// <summary>Choisit une vitesse libre (0 = pause). Annule un objectif en cours.</summary>
    public void ChangerFacteur(double facteur)
    {
        if (!Vitesse.FacteurValide(facteur)) throw new ArgumentOutOfRangeException(nameof(facteur), facteur, "Vitesse hors limites.");
        lock (_verrou)
        {
            _facteur = facteur;
            _reliquat = 0;
            _objectif = null;
        }
    }

    /// <summary>Lance un « avancer jusqu'à » : le monde file à la vitesse maximale, puis se met en pause.</summary>
    public void FixerObjectif(Objectif objectif)
    {
        lock (_verrou)
        {
            if (objectif.Limite <= _secondes) throw new ArgumentOutOfRangeException(nameof(objectif), "L'objectif est déjà passé.");
            _objectif = objectif;
            _reliquat = 0;
        }
    }

    /// <summary>Renvoie, une seule fois, la description du dernier objectif atteint (null sinon).</summary>
    public string? PrendreObjectifAtteint()
    {
        lock (_verrou)
        {
            var atteint = _objectifAtteint;
            _objectifAtteint = null;
            return atteint;
        }
    }

    /// <summary>
    /// Fait avancer le monde d'une durée réelle, selon la vitesse courante (vitesse maximale si un objectif est en cours).
    /// Avec un <paramref name="budget"/>, le calcul s'arrête quand il est épuisé : si la machine ne suit pas,
    /// le retard est abandonné (le monde ralentit au lieu de s'emballer). Renvoie le nombre de pas exécutés.
    /// </summary>
    public int Avancer(TimeSpan dureeReelle, TimeSpan? budget = null)
    {
        if (dureeReelle <= TimeSpan.Zero) return 0;
        lock (_verrou)
        {
            var facteur = _objectif is null ? _facteur : Vitesse.FacteurMax;
            if (facteur == 0) return 0;
            // Une pause du programme (veille de l'ordinateur) ne doit pas faire sauter des années :
            // l'écart pris en compte est plafonné à une seconde réelle.
            var reel = Math.Min(dureeReelle.TotalSeconds, 1.0);
            _reliquat += reel * facteur;
            var secondes = (long)_reliquat;
            _reliquat -= secondes;
            var (pas, complet) = AvancerInterne(secondes, budget);
            if (!complet) _reliquat = 0;
            return pas;
        }
    }

    /// <summary>Avance d'un nombre exact de secondes du monde (pas à pas, tests).</summary>
    public int AvancerDe(long secondesMonde)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(secondesMonde);
        lock (_verrou) return AvancerInterne(secondesMonde, null).Pas;
    }

    private (int Pas, bool Complet) AvancerInterne(long secondesMonde, TimeSpan? budget)
    {
        var chrono = budget is null ? null : System.Diagnostics.Stopwatch.StartNew();
        var fin = _secondes + secondesMonde;
        var objectif = _objectif;
        if (objectif is not null) fin = Math.Min(fin, objectif.Limite);
        var nombreAvant = _humains.Count;
        var vivantsAvant = objectif?.Type == TypeObjectif.Mort ? _humains.Count(h => h.EstVivant) : 0;

        var pas = 0;
        var evenement = false;
        while (true)
        {
            // Chaque pas s'exécute à l'instant exact où il tombe.
            var prochain = (_secondes / SecondesParPas + 1) * SecondesParPas;
            if (prochain > fin)
            {
                _secondes = fin;
                break;
            }
            _secondes = prochain;
            ExecuterPas();
            pas++;

            if (objectif is not null)
            {
                evenement = objectif.Type switch
                {
                    TypeObjectif.Naissance => _humains.Count > nombreAvant,
                    // Les naissances du pas sont vivantes : on compare aux vivants d'avant, nouveau-nés compris.
                    TypeObjectif.Mort => _humains.Count(h => h.EstVivant) < vivantsAvant + (_humains.Count - nombreAvant),
                    _ => false,
                };
                if (evenement) break;
            }
            if (chrono is not null && chrono.Elapsed >= budget) return (pas, false);
        }

        if (objectif is not null && (evenement || _secondes >= objectif.Limite))
        {
            _objectifAtteint = evenement || objectif.Type == TypeObjectif.Date
                ? objectif.Description
                : $"{objectif.Description} : rien en cent ans";
            _objectif = null;
            _facteur = 0;
            _reliquat = 0;
            return (pas, false);
        }
        return (pas, true);
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
            var bebe = GenerateurPopulation.Naissance(_humains.Count + 1, mere, pere, _secondes, _alea, _noms);
            _humains.Add(bebe);
            var fils = bebe.Sexe == Sexe.Femme ? "fille" : "fils";
            _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Naissance, bebe.Id, mere.Id,
                $"{bebe.Nom} naît, {fils} de {mere.Nom}{(pere is null ? "" : $" et de {pere.Nom}")}."));
            // Une naissance réjouit la mère, et le père s'il est là et attaché à ses enfants.
            VieSociale.Rejouir(mere, 20 * mere.Personnalite.Attachement);
            if (pere is { EstVivant: true }) VieSociale.Rejouir(pere, 15 * pere.Personnalite.Attachement);
        }
        _comportement.Accouchements.Clear();

        foreach (var mort in _comportement.Deces) _social.Endeuiller(mort, _humains, _secondes);
        _comportement.Deces.Clear();

        if (nouveauJour)
        {
            _comportement.Journal.AddRange(_comportement.Routine.Resumer(_humains, _secondes));
            _social.Journee(_humains, _secondes);
            Oublier();
            _social.Humeurs(_humains, _secondes);
            if (_pas / PasParJour % VieSociale.JoursEntreRegroupements == 0) _social.Regrouper(_humains, _communautes, _secondes, _noms);
        }
        foreach (var e in _comportement.Journal) Ajouter(e);
        _comportement.Journal.Clear();
        foreach (var e in _social.Journal) Ajouter(e);
        _social.Journal.Clear();
    }

    /// <summary>Une fois par jour, chacun peut oublier des lieux, plus ou moins vite selon sa mémoire.</summary>
    private void Oublier()
    {
        foreach (var h in _humains)
        {
            if (!h.EstVivant || h.EstBebe(_secondes)) continue;
            foreach (var l in h.Memoire.OublierAvecLeTemps(h.Personnalite.Intelligence.Memoire, _alea))
            {
                var quoi = l.Type switch
                {
                    TypeLieu.Eau => "un point d'eau",
                    TypeLieu.Nourriture => "un coin où manger",
                    _ => "un abri",
                };
                _comportement.Journal.Add(new EvenementJournal(_secondes, TypeEvenement.Souvenir, h.Id, 0,
                    $"{h.Nom} a oublié {quoi} (en {l.X}, {l.Y}){(l.Rappels > 0 ? $", où {Pronom(h)} était allé{(h.Sexe == Sexe.Femme ? "e" : "")} {l.Rappels + 1} fois" : "")}."));
            }
        }
    }

    private static string Pronom(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "il";
}
