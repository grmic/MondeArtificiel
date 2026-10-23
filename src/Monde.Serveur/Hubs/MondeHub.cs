using Microsoft.AspNetCore.SignalR;
using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Serveur.Hubs;

/// <summary>
/// Canal temps réel entre le serveur et les navigateurs.
/// Le serveur diffuse le message « Horloge » plusieurs fois par seconde.
/// </summary>
public sealed class MondeHub(ServiceMonde service, ServiceCarte cartes) : Hub
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
        if (monde.Eteint && demande.Type is "naissance" or "mort")
            throw new HubException("Plus aucun humain : il n'y aura plus de naissance ni de mort.");
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

    /// <summary>Mettre le monde en pause quand une catastrophe menace des humains.</summary>
    public async Task<HorlogeDto> ChoisirPauseCatastrophe(bool pause)
    {
        service.PauseSurCatastrophe = pause;
        return await DiffuserHorloge();
    }

    /// <summary>Catastrophes et phénomènes, du plus récent au plus ancien, éventuellement ceux qui ont touché une communauté.</summary>
    public List<CatastropheDto> Catastrophes(int? communaute, int limite = 200)
    {
        if (limite is < 1 or > Stockage.DepotMonde.LimiteJournal || communaute is < 1) throw new HubException("Filtre des catastrophes invalide.");
        return CatastropheDto.Liste(service, communaute, limite);
    }

    /// <summary>Une catastrophe avec ses cases touchées (null si elle n'existe pas).</summary>
    public DetailCatastropheDto? Catastrophe(long id)
    {
        if (id < 1 || CatastropheDto.Trouver(service, id) is not { } c) return null;
        return new DetailCatastropheDto(CatastropheDto.Depuis(c, service.Monde.Communautes(), service.Monde.Secondes), [.. c.Cases.Order()], [.. c.Front.Keys.Order()]);
    }

    /// <summary>Failles, volcans et sismicité du continent.</summary>
    public GeologieDto Geologie() => GeologieDto.Depuis(service.Monde);

    /// <summary>Caractéristiques du Monde : échelle, surface et dimensions du continent, relief, climat, population, marche.</summary>
    public CaracteristiquesDto Caracteristiques() => CaracteristiquesDto.Depuis(service);

    /// <summary>Réglages du monde courant et limites acceptées.</summary>
    public ConfigurationDto Configuration() => ConfigurationDto.Depuis(service);

    /// <summary>Noter ou non au journal la vie quotidienne de chacun (boire, manger, dormir, aller et venir).</summary>
    public ConfigurationDto ChoisirJournalQuotidien(bool oui)
    {
        service.JournalQuotidien = oui;
        return ConfigurationDto.Depuis(service);
    }

    /// <summary>Cœurs donnés au calcul : 1 = l'un après l'autre (classique), 0 = tous, 2 ou plus = ce nombre, en même temps.</summary>
    public ConfigurationDto ChoisirCoeurs(int coeurs)
    {
        if (coeurs < 0 || coeurs > ServiceMonde.CoeursMachine) throw new HubException($"Nombre de cœurs invalide (0 à {ServiceMonde.CoeursMachine}).");
        service.Coeurs = coeurs;
        return ConfigurationDto.Depuis(service);
    }

    /// <summary>Clémence du monde, de 0 à 100 % des risques visés écartés par l'Entité.</summary>
    public ConfigurationDto ChoisirClemence(int clemence)
    {
        if (clemence is < 0 or > 100) throw new HubException("Clémence invalide (0 à 100 %).");
        service.Clemence = clemence;
        return ConfigurationDto.Depuis(service);
    }

    /// <summary>Aperçu de réglages : la carte est générée (et gardée pour l'afficher), le monde courant ne change pas.</summary>
    public ApercuDto ApercuConfiguration(DemandeConfiguration demande)
    {
        ConfigurationMonde configuration;
        try
        {
            configuration = (demande with { Graine = demande.Graine ?? (uint)service.Monde.Graine }).Valider();
        }
        catch (ArgumentException e)
        {
            return new ApercuDto(false, e.Message);
        }
        try
        {
            return new ApercuDto(true, ConfigurationDto.Resumer(configuration, cartes.Obtenir(configuration)));
        }
        catch (InvalidOperationException e)
        {
            return new ApercuDto(false, e.Message);
        }
    }

    /// <summary>
    /// Crée un monde neuf avec ces réglages (graine absente : au hasard). L'ancien monde est gardé en copie.
    /// Des réglages impossibles sont refusés sans rien changer.
    /// </summary>
    public async Task<HorlogeDto> CreerMonde(DemandeConfiguration demande)
    {
        MondeSimule monde;
        try
        {
            monde = service.Reinitialiser(demande.Valider());
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            throw new HubException(e.Message);
        }
        service.FacteurAtteint = 0;
        await Clients.All.SendAsync("MondeReinitialise", (uint)monde.Graine);
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return await DiffuserHorloge();
    }

    /// <summary>Sauvegardes que l'on peut restaurer, la plus récente d'abord.</summary>
    public List<SauvegardeDto> Sauvegardes() => SauvegardeDto.Toutes(service);

    /// <summary>Sauvegarde le monde tel qu'il est, sans l'arrêter ; rend la liste à jour.</summary>
    public List<SauvegardeDto> Sauvegarder()
    {
        service.SauvegarderMaintenant();
        return SauvegardeDto.Toutes(service);
    }

    /// <summary>
    /// Remplace le monde par une sauvegarde (nom tiré de la liste), en pause ; le monde courant est sauvegardé avant.
    /// Tous les navigateurs connectés rechargent le monde.
    /// </summary>
    public async Task<HorlogeDto> Restaurer(string nom)
    {
        MondeSimule monde;
        try
        {
            monde = service.Restaurer(nom);
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException)
        {
            throw new HubException(e.Message);
        }
        service.FacteurAtteint = 0;
        await Clients.All.SendAsync("MondeReinitialise", (uint)monde.Graine);
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return await DiffuserHorloge();
    }

    /// <summary>Trajet à pied le moins pénible entre deux cases (null si l'une est dans l'eau ou si on ne peut pas y aller à pied).</summary>
    public TrajetDto? Trajet(int x1, int y1, int x2, int y2)
    {
        var carte = service.Monde.Carte;
        if (Math.Min(Math.Min(x1, y1), Math.Min(x2, y2)) < 0 || Math.Max(x1, x2) >= carte.Largeur || Math.Max(y1, y2) >= carte.Hauteur)
        {
            throw new HubException("Points hors de la carte.");
        }
        return TrajetDto.Depuis(carte, x1, y1, x2, y2);
    }

    /// <summary>Maladies apparues depuis le début du monde.</summary>
    public List<MaladieDto> Maladies() => MaladieDto.Toutes(service.Monde);

    /// <summary>L'Entité provoque une catastrophe. Renvoie le numéro de l'événement qui l'annonce (0 si elle n'a pas pu avoir lieu).</summary>
    public async Task<long> Declencher(DemandeCatastrophe demande)
    {
        var monde = service.Monde;
        if (CatastropheDto.TypeDepuis(demande.Type) is not { } type || type is Simulation.Nature.TypeCatastrophe.HiverVolcanique or Simulation.Nature.TypeCatastrophe.HiverDImpact)
        {
            throw new HubException("Type de catastrophe inconnu.");
        }
        if (demande.X < 0 || demande.Y < 0 || demande.X >= monde.Carte.Largeur || demande.Y >= monde.Carte.Hauteur || !double.IsFinite(demande.Intensite))
        {
            throw new HubException("Lieu ou intensité invalide.");
        }
        var id = monde.Declencher(type, demande.X, demande.Y, demande.Intensite);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return id;
    }

    /// <summary>Chronologie des découvertes : la première fois que chaque savoir est apparu dans le monde.</summary>
    public List<DecouverteDto> Decouvertes() => Serveur.Decouvertes.Chronologie(service);

    /// <summary>Courbe des savoirs vivants du monde (0) ou d'une communauté, avec les savoirs perdus et retrouvés.</summary>
    public ProgresDto Progres(int communaute) => Serveur.Decouvertes.Progres(service, communaute);

    /// <summary>Critères de la fenêtre « Remarquables ».</summary>
    public List<CritereRemarquableDto> CriteresRemarquables() => Serveur.Remarquables.ListeCriteres();

    /// <summary>Les humains les plus remarquables selon un critère (les plus bas si demandé), vivants ou aussi les morts.</summary>
    public List<RemarquableDto> Remarquables(string critere, bool bas, bool morts) => Serveur.Remarquables.Classer(service, critere, bas, morts);

    /// <summary>Gestes et matières que l'Entité peut souffler.</summary>
    public ChoixIntuitionDto ChoixIntuition(int humain) => Serveur.Decouvertes.Choix(service, humain);

    /// <summary>
    /// L'Entité souffle à un humain l'idée d'un geste. Il l'essaie aussitôt avec ce qu'il a sous la main ; les lois du monde
    /// décident du résultat. Renvoie ce qu'il a obtenu, en mots.
    /// </summary>
    public async Task<string> Souffler(DemandeIntuition demande)
    {
        if (!Enum.TryParse<Simulation.Humains.Geste>(demande.Geste, out var geste) || !Enum.IsDefined(geste)
            || !Enum.TryParse<Simulation.Humains.Matiere>(demande.A, out var a) || !Enum.IsDefined(a)
            || !Enum.TryParse<Simulation.Humains.Matiere>(demande.B ?? nameof(Simulation.Humains.Matiere.Aucune), out var b) || !Enum.IsDefined(b))
        {
            throw new HubException("Geste ou matière inconnus.");
        }
        // Liste blanche : seules les matières qui ont un sens pour ce geste ; une plante doit être désignée par son espèce.
        var plante = geste is Simulation.Humains.Geste.Manger or Simulation.Humains.Geste.Appliquer;
        if (!Simulation.Humains.Physique.Convient(geste, a, b) || plante && (demande.Espece < 1 || demande.Espece > service.Monde.Flore.Especes.Count))
        {
            throw new HubException("Ce geste ne va pas avec ces matières.");
        }
        var effet = service.Monde.Souffler(demande.Humain, geste, a, b, plante ? demande.Espece : 0);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(service.Monde));
        if (plante && effet == Simulation.Humains.Effet.Rien) return "La plante a été essayée ; le journal dira ce qui a été ressenti, et la croyance qui en naît.";
        return effet == Simulation.Humains.Effet.Rien ? "Rien n'en est sorti (le journal dit ce qu'il en a fait)." : "Obtenu : " + Simulation.Humains.Physique.NomEffet(effet) + ".";
    }

    /// <summary>Prières en mémoire (en attente seulement, ou aussi celles de l'année écoulée).</summary>
    public List<PriereDto> Prieres(bool enAttente = false) => Serveur.Religions.Prieres(service, enAttente);

    /// <summary>Religions, avec leurs préceptes, leurs fidèles et ceux qui les portent.</summary>
    public List<ReligionDto> Religions() => Serveur.Religions.Toutes(service);

    /// <summary>Lieux sacrés, pour la carte.</summary>
    public List<LieuSacreDto> LieuxSacres() => Serveur.Religions.Lieux(service);

    /// <summary>L'Entité exauce une prière en attente. Renvoie, en mots, ce qui s'est passé.</summary>
    public async Task<string> Exaucer(int priere)
    {
        if (priere < 1) throw new HubException("Prière inconnue.");
        var texte = service.Monde.Exaucer(priere);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(service.Monde));
        return texte;
    }

    /// <summary>L'Entité refuse une prière en attente, en secret (par défaut) ou par un signe. Renvoie, en mots, ce qui s'est passé.</summary>
    public async Task<string> Refuser(int priere, bool signe = false)
    {
        if (priere < 1) throw new HubException("Prière inconnue.");
        var texte = service.Monde.Refuser(priere, signe);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(service.Monde));
        return texte;
    }

    /// <summary>Gestes que l'Entité peut faire, manières, formes de mort et maladies.</summary>
    public ChoixGestesDto ChoixGestes() => GestesEntite.Choix(service);

    /// <summary>L'Entité fait un geste sur un humain ou une communauté. Renvoie, en mots, ce qui s'est passé.</summary>
    public async Task<string> Agir(DemandeGeste demande)
    {
        if (demande is null) throw new HubException("Demande vide.");
        var monde = service.Monde;
        (Simulation.Humains.GesteEntite geste, Simulation.Humains.ManiereGeste maniere, int param) lu;
        try
        {
            lu = GestesEntite.Lire(demande, monde.Pathogenes().Select(p => p.Id).ToHashSet());
        }
        catch (ArgumentException e)
        {
            throw new HubException(e.Message);
        }
        var texte = monde.Agir(lu.geste, demande.Cible, lu.maniere, lu.param, demande.Force, demande.Jours);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return texte;
    }

    /// <summary>Paroles, manières de se manifester, directions, plantes et communautés que l'Entité peut choisir.</summary>
    public ChoixParolesDto ChoixParoles() => ParolesEntite.Choix(service);

    /// <summary>Cherche des humains vivants (40 au plus) par leur nom, leur communauté, leur situation ou leur proximité.</summary>
    public List<HumainTrouveDto> ChercherHumains(FiltreHumains filtre) => ParolesEntite.Chercher(service, filtre);

    /// <summary>
    /// L'Entité parle : chacun des humains visés (ou chaque membre des communautés visées) comprend, croit, obéit ou refuse
    /// selon ce qu'il est. Renvoie ce que chacun en a fait.
    /// </summary>
    public async Task<List<ResultatParoleDto>> Parler(DemandeParole demande)
    {
        var monde = service.Monde;
        if (!Enum.TryParse<Simulation.Humains.Parole>(demande.Parole, out var parole) || !Enum.IsDefined(parole) || parole == Simulation.Humains.Parole.Penchant
            || !Enum.TryParse<Simulation.Humains.ModeParole>(demande.Mode, out var mode) || !Enum.IsDefined(mode) || mode == Simulation.Humains.ModeParole.Penchant)
        {
            throw new HubException("Parole ou manière inconnues.");
        }
        var (humainsIds, communautes) = Destinataires(demande.Humains, demande.Communautes);
        var (maintenant, humains) = monde.InstantaneHumains();
        var veutCible = Simulation.Humains.Paroles.VeutCible(parole);
        if (veutCible && (demande.Cible < 1 || demande.Cible > humains.Count || !humains[demande.Cible - 1].EstVivant))
            throw new HubException("Il faut désigner un humain vivant.");
        if (parole == Simulation.Humains.Parole.UnisToi && humains[demande.Cible - 1].EstEnfant(maintenant))
            throw new HubException("Pour s'unir, il faut désigner un adulte.");
        var param = parole switch
        {
            Simulation.Humains.Parole.PartsVers when demande.Param is >= 0 and <= 7 => demande.Param,
            Simulation.Humains.Parole.PlanteDangereuse when demande.Param >= 1 && demande.Param <= monde.Flore.Especes.Count => demande.Param,
            Simulation.Humains.Parole.NeVaPasALaGuerre when demande.Param >= 0 && demande.Param <= monde.Communautes().Count => demande.Param,
            Simulation.Humains.Parole.PartsVers or Simulation.Humains.Parole.PlanteDangereuse or Simulation.Humains.Parole.NeVaPasALaGuerre
                => throw new HubException("Précision invalide."),
            _ => 0,
        };
        var resultats = monde.Parler(humainsIds, communautes, parole, mode, veutCible ? demande.Cible : 0, param);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return Resultats(resultats);
    }

    /// <summary>L'Entité inspire un penchant amoureux (ou une passion) à des humains, pour quelqu'un ou pour qui le monde choisit.</summary>
    public async Task<List<ResultatParoleDto>> Inspirer(DemandePenchant demande)
    {
        var monde = service.Monde;
        var (humainsIds, communautes) = Destinataires(demande.Humains, demande.Communautes);
        var (_, humains) = monde.InstantaneHumains();
        if (demande.Cible != 0 && (demande.Cible < 1 || demande.Cible > humains.Count || !humains[demande.Cible - 1].EstVivant))
            throw new HubException("Il faut désigner un humain vivant.");
        var resultats = monde.Inspirer(humainsIds, communautes, demande.Cible, demande.Mutuel, demande.Passion, demande.Orientation);
        service.Sauvegarder();
        await Clients.All.SendAsync("Humains", HumainDto.Depuis(monde));
        return Resultats(resultats);
    }

    /// <summary>Destinataires validés : au moins un, 500 humains et 50 communautés au plus.</summary>
    private static (int[] Humains, int[] Communautes) Destinataires(int[]? humains, int[]? communautes)
    {
        humains ??= [];
        communautes ??= [];
        if (humains.Length + communautes.Length == 0 || humains.Length > 500 || communautes.Length > 50)
            throw new HubException("Destinataires invalides.");
        return (humains.Distinct().ToArray(), communautes.Distinct().ToArray());
    }

    private List<ResultatParoleDto> Resultats(List<(int Humain, string Resultat)> resultats)
    {
        var (_, humains) = service.Monde.InstantaneHumains();
        return resultats.Select(r => new ResultatParoleDto(r.Humain, r.Humain >= 1 && r.Humain <= humains.Count ? humains[r.Humain - 1].Nom : "?", r.Resultat)).ToList();
    }

    /// <summary>Au redémarrage du serveur : reprendre la dernière vitesse (vrai) ou repartir en pause (faux).</summary>
    public async Task<HorlogeDto> ChoisirRedemarrage(bool reprendreVitesse)
    {
        service.ReprendreVitesse = reprendreVitesse;
        return await DiffuserHorloge();
    }

    /// <summary>Fiche détaillée d'un humain (null s'il n'existe pas).</summary>
    public FicheDto? Fiche(int id) => FicheDto.Depuis(service, id);

    /// <summary>Communautés actives puis dispersées, avec leurs membres.</summary>
    public List<CommunauteDto> Communautes() => CommunauteDto.Toutes(service);

    /// <summary>Fiche d'une communauté, rangée par thème (null si elle n'existe pas).</summary>
    public FicheCommunauteDto? FicheCommunaute(int id) => FicheCommunauteDto.Depuis(service, id);

    /// <summary>Terres des communautés : maintenant (secondes absentes) ou à une date archivée (début de saison).</summary>
    public TerritoiresDto? Territoires(long? secondes) =>
        secondes is { } s ? (s < 0 ? null : Serveur.Territoires.Archives(service, s)) : Serveur.Territoires.Actuels(service);

    /// <summary>Dates auxquelles les terres des communautés ont été archivées.</summary>
    public List<DateArchiveDto> DatesTerritoires() => Serveur.Territoires.Dates(service);

    /// <summary>Taille d'une communauté au début de chaque saison (vide si elle n'existe pas).</summary>
    public List<TailleDto> TaillesCommunaute(int id) => id < 1 ? [] : Serveur.Territoires.Tailles(service, id);

    /// <summary>Courbes démographiques : le monde, les communautés les plus peuplées (au plus 30) et les événements marquants.</summary>
    public DemographieDto Demographie(int limite = 10) => DemographieDto.Calculer(service, limite);

    /// <summary>Nature : abondance des terres, pluies des régions et indicateurs du monde.</summary>
    public NatureDto Nature() => NatureDto.Depuis(service.Monde);

    /// <summary>Feux qui flambent ou couvent, et réserves, pour la carte.</summary>
    public FeuxDto Feux() => FeuxDto.Depuis(service.Monde);

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

    /// <summary>Un événement par son numéro (pour remonter la chaîne des causes), ou null s'il n'existe pas.</summary>
    public EvenementDto? Evenement(long id)
    {
        if (id < 1) return null;
        var e = service.Monde.EvenementEnMemoire(id) ?? service.Depot.LireEvenement(id);
        return e is null ? null : EvenementDto.Depuis(e);
    }

    private async Task<HorlogeDto> DiffuserHorloge()
    {
        var horloge = HorlogeDto.Depuis(service);
        await Clients.All.SendAsync("Horloge", horloge);
        return horloge;
    }

    /// <summary>
    /// Recommence le monde à l'an 1 avec la graine donnée (ou une graine au hasard si elle est absente), et les mêmes réglages.
    /// Tous les navigateurs connectés sont prévenus pour recharger la carte.
    /// </summary>
    public async Task<HorlogeDto> Reinitialiser(long? graine)
    {
        if (graine is < 0 or > uint.MaxValue)
        {
            throw new HubException("La graine doit être un entier entre 0 et 4294967295.");
        }
        var valeur = graine is { } g ? (uint)g : (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        MondeSimule monde;
        try
        {
            monde = service.Reinitialiser(valeur);
        }
        catch (InvalidOperationException e)
        {
            // Avec cette graine, les réglages du monde ne laissent pas la place aux couples : rien n'a changé.
            throw new HubException(e.Message);
        }
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
    bool ReprendreVitesse,
    bool PauseCatastrophe,
    bool Eteint)
{
    public static HorlogeDto Depuis(ServiceMonde service)
    {
        var (secondes, pas, facteurDemande, objectif) = service.Monde.Horloge;
        var facteur = objectif is null ? facteurDemande : Vitesse.FacteurMax;
        var date = Calendrier.Decomposer(secondes);
        return new HorlogeDto(secondes, pas, date.ToString(), date.Annee, date.NomSaison,
            date.JourDeSaison, date.Heure, date.Minute, date.EstJour, date.Lumiere,
            objectif is null ? Vitesse.NiveauDe(facteur) : -1, facteur,
            facteur == 0 ? 0 : service.FacteurAtteint, objectif?.Description, service.ReprendreVitesse, service.PauseSurCatastrophe, service.Monde.Eteint);
    }
}
