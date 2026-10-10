using Microsoft.AspNetCore.SignalR;
using Monde.Simulation;
using Monde.Simulation.Temps;

namespace Monde.Serveur.Hubs;

/// <summary>
/// Canal temps réel entre le serveur et les navigateurs.
/// Le serveur diffuse le message « Horloge » plusieurs fois par seconde.
/// </summary>
public sealed class MondeHub(ServiceMonde service) : Hub
{
    public const string Route = "/hub/monde";

    /// <summary>Permet au client de vérifier la liaison et d'obtenir l'heure du serveur.</summary>
    public ReponsePing Ping() => new(DateTimeOffset.UtcNow, InfosMonde.ParDefaut.Nom);

    public HorlogeDto Horloge() => HorlogeDto.Depuis(service);

    public IReadOnlyList<Vitesse> Vitesses() => Vitesse.Toutes;

    public List<HumainDto> Humains() => HumainDto.Depuis(service.Monde);

    public Task<HorlogeDto> ChoisirVitesse(int niveau)
    {
        if (!Vitesse.Existe(niveau))
        {
            // Message volontairement simple : il est renvoyé tel quel au navigateur.
            throw new HubException("Vitesse inconnue.");
        }
        return AppliquerFacteur(Vitesse.Toutes[niveau].Facteur);
    }

    /// <summary>Vitesse libre : secondes du monde par seconde réelle (0 = pause).</summary>
    public Task<HorlogeDto> ChoisirFacteur(double facteur)
    {
        if (!Vitesse.FacteurValide(facteur))
        {
            throw new HubException($"La vitesse doit valoir 0 ou être comprise entre {Vitesse.FacteurMin} et {Vitesse.FacteurMax}.");
        }
        return AppliquerFacteur(facteur);
    }

    private async Task<HorlogeDto> AppliquerFacteur(double facteur)
    {
        service.Monde.ChangerFacteur(facteur);
        if (facteur == 0) service.FacteurAtteint = 0;
        service.Sauvegarder();
        return await DiffuserHorloge();
    }

    /// <summary>Pas à pas : met en pause, puis avance d'un pas (10 min), d'une heure ou d'un jour.</summary>
    public async Task<HorlogeDto> AvancerDe(string unite)
    {
        var secondes = unite switch
        {
            "pas" => MondeSimule.SecondesParPas,
            "heure" => Calendrier.SecondesParHeure,
            "jour" => Calendrier.SecondesParJour,
            _ => throw new HubException("Unité inconnue (pas, heure ou jour)."),
        };
        var monde = service.Monde;
        monde.ChangerFacteur(0);
        service.FacteurAtteint = 0;
        monde.AvancerDe(secondes);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return await DiffuserHorloge();
    }

    /// <summary>
    /// Avance à la vitesse maximale jusqu'à une date ou un événement, puis met en pause.
    /// Types : « date » (annee, saison 1-4, jour 1-90, heure 0-23), « matin », « saison », « annee », « naissance », « mort ».
    /// </summary>
    public async Task<HorlogeDto> AllerJusqua(DemandeObjectif demande)
    {
        var monde = service.Monde;
        var maintenant = monde.Secondes;
        try
        {
            var objectif = demande.Type switch
            {
                "date" => Objectif.Date(Objectif.Secondes(demande.Annee ?? 0, demande.Saison ?? 0, demande.Jour ?? 0, demande.Heure ?? 0), maintenant),
                "matin" => Objectif.Date(Objectif.ProchainMatin(maintenant), maintenant),
                "saison" => Objectif.Date(Objectif.ProchaineSaison(maintenant), maintenant),
                "annee" => Objectif.Date(Objectif.ProchaineAnnee(maintenant), maintenant),
                "naissance" => Objectif.Evenement(TypeObjectif.Naissance, maintenant),
                "mort" => Objectif.Evenement(TypeObjectif.Mort, maintenant),
                _ => throw new HubException("Objectif inconnu."),
            };
            monde.FixerObjectif(objectif);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new HubException("Date invalide : elle doit exister dans le calendrier, être dans le futur et à moins de mille ans.");
        }
        return await DiffuserHorloge();
    }

    /// <summary>Au redémarrage du serveur : reprendre la dernière vitesse (vrai) ou repartir en pause (faux).</summary>
    public async Task<HorlogeDto> ChoisirRedemarrage(bool reprendreVitesse)
    {
        service.ReprendreVitesse = reprendreVitesse;
        return await DiffuserHorloge();
    }

    /// <summary>Fiche détaillée d'un humain (null s'il n'existe pas).</summary>
    public FicheDto? Fiche(int id) => FicheDto.Depuis(service, id);

    /// <summary>Journal filtré, du plus récent au plus ancien (500 événements au plus par demande).</summary>
    public List<EvenementDto> Journal(FiltreJournal filtre)
    {
        if (filtre.Types is { Length: > 20 } || filtre.Limite is < 1 or > Stockage.DepotMonde.LimiteJournal)
        {
            throw new HubException("Filtre du journal invalide.");
        }
        var types = EvenementDto.TypesDepuis(filtre.Types) ?? throw new HubException("Type d'événement inconnu.");
        return FicheDto.Journal(service, filtre, types).Select(EvenementDto.Depuis).ToList();
    }

    private async Task<HorlogeDto> DiffuserHorloge()
    {
        var horloge = HorlogeDto.Depuis(service);
        await Clients.All.SendAsync("Horloge", horloge);
        return horloge;
    }

    /// <summary>
    /// Recommence le monde à l'an 1 avec la graine donnée (ou une graine au hasard si elle est absente).
    /// Tous les navigateurs connectés sont prévenus pour recharger la carte.
    /// </summary>
    public async Task<HorlogeDto> Reinitialiser(long? graine)
    {
        if (graine is < 0 or > uint.MaxValue)
        {
            throw new HubException("La graine doit être un entier entre 0 et 4294967295.");
        }
        var valeur = graine is { } g ? (uint)g : (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        var monde = service.Reinitialiser(valeur);
        service.FacteurAtteint = 0;
        await Clients.All.SendAsync("MondeReinitialise", valeur);
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return await DiffuserHorloge();
    }
}

public sealed record ReponsePing(DateTimeOffset HeureServeur, string Monde);

/// <summary>Demande « avancer jusqu'à » venue du navigateur.</summary>
public sealed record DemandeObjectif(string Type, long? Annee = null, int? Saison = null, int? Jour = null, int? Heure = null);

/// <summary>État de l'horloge envoyé au navigateur.</summary>
/// <param name="NiveauVitesse">Vitesse préréglée en cours, ou -1 pour une vitesse libre ou un objectif.</param>
/// <param name="Facteur">Vitesse demandée (secondes du monde par seconde réelle).</param>
/// <param name="FacteurAtteint">Vitesse réellement atteinte, mesurée.</param>
/// <param name="Objectif">Objectif « avancer jusqu'à » en cours, ou null.</param>
public sealed record HorlogeDto(
    long Secondes,
    long Pas,
    string Texte,
    long Annee,
    string Saison,
    int Jour,
    int Heure,
    int Minute,
    bool EstJour,
    double Lumiere,
    int NiveauVitesse,
    double Facteur,
    double FacteurAtteint,
    string? Objectif,
    bool ReprendreVitesse)
{
    public static HorlogeDto Depuis(ServiceMonde service)
    {
        var (secondes, pas, facteurDemande, objectif) = service.Monde.Horloge;
        var facteur = objectif is null ? facteurDemande : Vitesse.FacteurMax;
        var date = Calendrier.Decomposer(secondes);
        return new HorlogeDto(secondes, pas, date.ToString(), date.Annee, date.NomSaison,
            date.JourDeSaison, date.Heure, date.Minute, date.EstJour, date.Lumiere,
            objectif is null ? Vitesse.NiveauDe(facteur) : -1, facteur,
            facteur == 0 ? 0 : service.FacteurAtteint, objectif?.Description, service.ReprendreVitesse);
    }
}
