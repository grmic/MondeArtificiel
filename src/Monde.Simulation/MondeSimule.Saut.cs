using System.Diagnostics;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Simulation;

/// <summary>Un saut dans le futur en cours : d'où, jusqu'où, et depuis combien de temps réel.</summary>
public sealed record SautEnCours(long Debut, long Cible, long Maintenant, TimeSpan Ecoule)
{
    /// <summary>Part faite, de 0 à 1.</summary>
    public double Avancement => Cible <= Debut ? 1 : Math.Clamp((double)(Maintenant - Debut) / (Cible - Debut), 0, 1);

    /// <summary>Années du monde par minute réelle (0 tant qu'on ne peut pas le dire).</summary>
    public double AnsParMinute => Ecoule.TotalSeconds < 2 || Maintenant <= Debut
        ? 0 : (Maintenant - Debut) / (double)Calendrier.SecondesParAn / Ecoule.TotalMinutes;

    /// <summary>Durée réelle restante au rythme mesuré (null tant qu'on ne peut pas le dire).</summary>
    public TimeSpan? Reste => AnsParMinute <= 0 ? null : TimeSpan.FromMinutes((Cible - Maintenant) / (double)Calendrier.SecondesParAn / AnsParMinute);
}

/// <summary>
/// Bilan d'un saut dans le futur (0.40.0) : ce qui s'est passé entre <see cref="Debut"/> et <see cref="Fin"/>. Les lignes du journal de la
/// période ont les numéros de <see cref="PremierEvenement"/> à <see cref="DernierEvenement"/>. <see cref="Interrompu"/> : arrêté avant la
/// date visée (pause demandée par une alerte, une catastrophe, ou par le joueur).
/// </summary>
public sealed record BilanSaut(long Debut, long Fin, long Cible, bool Interrompu, TimeSpan Duree, long PremierEvenement, long DernierEvenement,
    int VivantsAvant, int VivantsApres, int Naissances, IReadOnlyDictionary<CauseDeMort, int> Morts, int MortsNes, int Decouvertes,
    int PremieresAuMonde, int CommunautesNees, int CommunautesDisparues, int Raids, int ReligionsNees);

public sealed partial class MondeSimule
{
    private sealed class CompteurSaut
    {
        public required long Debut;
        public required long Cible;
        public required long PremierEvenement;
        public required int VivantsAvant;
        public required int CommunautesAvant;
        public required HashSet<int> ActivesAvant;
        public required int ReligionsAvant;
        public readonly Stopwatch Chrono = Stopwatch.StartNew();
        public int Naissances, MortsNes, Decouvertes, Premieres, Raids;
        public readonly Dictionary<CauseDeMort, int> Morts = [];

        public void Compter(EvenementJournal e, IReadOnlyList<Humain> tous)
        {
            switch (e.Type)
            {
                case TypeEvenement.Naissance when e.Recit?.Cle == "j.naissance":
                    Naissances++;
                    break;
                case TypeEvenement.Mort when e.HumainId > 0 && e.HumainId <= tous.Count:
                    var cause = tous[e.HumainId - 1].CauseDeMort;
                    Morts[cause] = Morts.GetValueOrDefault(cause) + 1;
                    break;
                case TypeEvenement.MortNe:
                    MortsNes++;
                    break;
                case TypeEvenement.Decouverte when e.Significatif:
                    Decouvertes++;
                    break;
                case TypeEvenement.Guerre when e.Recit?.Cle == "guerre.raid":
                    Raids++;
                    break;
            }
            if ((e.Premiere & PremieresFois.AuMonde) != 0) Premieres++;
        }
    }

    private CompteurSaut? _saut;

    /// <summary>
    /// Saute de <paramref name="ans"/> années dans le futur : le monde file à la vitesse maximale jusqu'à la date visée, puis se met
    /// en pause ; <see cref="PrendreBilanSaut"/> dit alors ce qui s'est passé.
    /// </summary>
    public void Sauter(int ans)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ans, 1);
        lock (_verrou)
        {
            var objectif = Objectif.Date(_secondes + ans * Calendrier.SecondesParAn, _secondes);
            FixerObjectif(objectif);
            _saut = new CompteurSaut
            {
                Debut = _secondes, Cible = objectif.Limite, PremierEvenement = _numeros.Prochain,
                VivantsAvant = _humains.Vivants().Count(h => h.EstVivant), CommunautesAvant = _communautes.Count,
                ActivesAvant = [.. _communautes.Where(c => c.Active).Select(c => c.Id)], ReligionsAvant = _cultes.Religions.Count,
            };
        }
    }

    /// <summary>Le saut en cours (null s'il n'y en a pas, ou s'il vient de finir).</summary>
    public SautEnCours? Saut
    {
        get
        {
            var s = _saut;
            return s is null || _objectif is not { } o || o.Limite != s.Cible ? null : new SautEnCours(s.Debut, s.Cible, Volatile.Read(ref _secondes), s.Chrono.Elapsed);
        }
    }

    /// <summary>Le saut est fini (date atteinte, ou monde arrêté avant) : rend son bilan, une seule fois ; sinon null.</summary>
    public BilanSaut? PrendreBilanSaut()
    {
        lock (_verrou)
        {
            if (_saut is not { } s || _objectif is not null && _objectif.Limite == s.Cible) return null;
            _saut = null;
            var actives = _communautes.Where(c => c.Active).Select(c => c.Id).ToHashSet();
            var nees = _communautes.Count - s.CommunautesAvant;
            var disparues = s.ActivesAvant.Count(id => !actives.Contains(id)) + _communautes.Skip(s.CommunautesAvant).Count(c => !c.Active);
            return new BilanSaut(s.Debut, _secondes, s.Cible, _secondes < s.Cible, s.Chrono.Elapsed, s.PremierEvenement, _numeros.Prochain - 1,
                s.VivantsAvant, _humains.Vivants().Count(h => h.EstVivant), s.Naissances, s.Morts, s.MortsNes, s.Decouvertes, s.Premieres,
                nees, disparues, s.Raids, _cultes.Religions.Count - s.ReligionsAvant);
        }
    }
}
