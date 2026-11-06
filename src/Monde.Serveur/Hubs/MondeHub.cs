using Microsoft.AspNetCore.SignalR;
using Monde.Simulation;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Serveur.Hubs;

/// <summary>
/// Canal temps réel entre le serveur et les navigateurs.
/// Le serveur diffuse le message « Horloge » plusieurs fois par seconde.
/// </summary>
public sealed class MondeHub(ServiceMonde service, ServiceCarte cartes, Entite.EntiteLlm entite, Entite.Dialogues dialogues) : Hub
{
    public const string Route = "/hub/monde";

    /// <summary>Permet au client de vérifier la liaison et d'obtenir l'heure du serveur.</summary>
    public ReponsePing Ping() => new(DateTimeOffset.UtcNow, InfosMonde.ParDefaut.Nom);

    public HorlogeDto Horloge() => HorlogeDto.Depuis(service);

    public IReadOnlyList<Vitesse> Vitesses() => Vitesse.Toutes.Select(v => v with { Nom = L.T($"vitesse.{v.Niveau}") }).ToList();

    public List<HumainDto> Humains() => HumainDto.Depuis(service.Monde);

    public Task<HorlogeDto> ChoisirVitesse(int niveau)
    {
        if (!Vitesse.Existe(niveau))
        {
            // Message volontairement simple : il est renvoyé tel quel au navigateur.
            throw new HubException(L.T("vitesse.inconnue"));
        }
        return AppliquerFacteur(Vitesse.Toutes[niveau].Facteur);
    }

    /// <summary>Vitesse libre : secondes du monde par seconde réelle (0 = pause).</summary>
    public Task<HorlogeDto> ChoisirFacteur(double facteur)
    {
        if (!Vitesse.FacteurValide(facteur))
        {
            throw new HubException(L.T("vitesse.horsLimites", ("min", Vitesse.FacteurMin), ("max", Vitesse.FacteurMax)));
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
            _ => throw new HubException(L.T("erreur.unite")),
        };
        var monde = service.Monde;
        monde.ChangerFacteur(0);
        service.FacteurAtteint = 0;
        monde.AvancerDe(secondes);
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
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
            throw new HubException(L.T("erreur.plusPersonne"));
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
                _ => throw new HubException(L.T("erreur.objectif")),
            };
            monde.FixerObjectif(objectif);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new HubException(L.T("erreur.date"));
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
        if (limite is < 1 or > Stockage.DepotMonde.LimiteJournal || communaute is < 1) throw new HubException(L.T("erreur.filtreCatastrophes"));
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

    /// <summary>Roches, sols, pentes, rivières, fonds marins, rivages, grottes, sources et gisements, pour les calques de la carte.</summary>
    public TerrainDto Terrain() => TerrainDto.Depuis(service.Monde);

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

    /// <summary>Rubrique « Journal et taille de la base » : réglages, taille et rythme mesurés.</summary>
    public JournalBaseDto JournalBase() => JournalBaseDto.Depuis(service);

    /// <summary>Conservation (années, 0 = illimitée) et compression du journal ; l'entretien suit en arrière-plan.</summary>
    public JournalBaseDto ChoisirJournalBase(int conserverAns, int quotidienAns, bool effacerMarquants, bool effacerSuivis, bool compresser, int compresserAns)
    {
        try
        {
            service.ReglagesJournal = new ReglagesJournal(conserverAns, quotidienAns, effacerMarquants, effacerSuivis, compresser, compresserAns);
        }
        catch (ArgumentException e)
        {
            throw new HubException(e.Message);
        }
        return JournalBaseDto.Depuis(service);
    }

    /// <summary>
    /// Humains suivis dans un navigateur (leur journal est gardé quand le journal ancien est effacé). Chaque navigateur
    /// envoie ses suivis en se connectant, puis chaque changement ; un humain n'est retiré que si on cesse de le suivre.
    /// </summary>
    public void SignalerSuivis(int[] ajoutes, int[] retires)
    {
        if (service.Depot.ChangerSuivis(ajoutes ?? [], retires ?? [])) service.SuivisChanges();
    }

    /// <summary>Option « humains de fond simulés en groupe » et son seuil (nombre de vivants, 0 : toujours).</summary>
    public ConfigurationDto ChoisirFond(bool actif, int seuil)
    {
        if (seuil < 0 || seuil > 10_000_000) throw new HubException(L.T("erreur.fondSeuil"));
        service.Fond = (actif, seuil);
        return ConfigurationDto.Depuis(service);
    }

    /// <summary>Où en est un humain (et sa communauté) avec les humains de fond, pour sa fiche.</summary>
    public FondDto EtatFond(int humain, int communaute)
    {
        var (humains, communautes) = service.GardesEnIndividu();
        var monde = service.Monde;
        return new FondDto(monde.HumainsDeFond, monde.EstDeFond(humain), humains.Contains(humain), communautes.Contains(communaute),
            monde.DeFond, monde.SeuilFond);
    }

    /// <summary>« Garder en individu » : un humain (<paramref name="communaute"/> faux) ou une communauté entière.</summary>
    public FondDto GarderEnIndividu(bool communaute, int id, bool garder, int humain, int communauteDuHumain)
    {
        if (id < 1) throw new HubException(L.T("erreur.fondNumero"));
        service.GarderEnIndividu(communaute, id, garder);
        return EtatFond(humain, communauteDuHumain);
    }

    /// <summary>Copie du monde pour un client SQL (journal en clair, phrase rédigée), dans le dossier « analyses ».</summary>
    public async Task<string> CopierPourAnalyse(int? anDebut, int? anFin)
    {
        if (anDebut is < 1 || anFin is < 1 || anDebut > anFin) throw new HubException(L.T("journalBase.periodeInvalide"));
        var (chemin, lignes) = await Task.Run(() => service.CopierPourAnalyse(L.Ecrire, anDebut, anFin));
        return L.T("journalBase.copieFaite", ("n", lignes), ("chemin", chemin));
    }

    /// <summary>Cœurs donnés au calcul : 1 = l'un après l'autre (classique), 0 = tous, 2 ou plus = ce nombre, en même temps.</summary>
    public ConfigurationDto ChoisirCoeurs(int coeurs)
    {
        if (coeurs < 0 || coeurs > ServiceMonde.CoeursMachine) throw new HubException(L.T("erreur.coeurs", ("max", ServiceMonde.CoeursMachine)));
        service.Coeurs = coeurs;
        return ConfigurationDto.Depuis(service);
    }

    /// <summary>Clémence du monde, de 0 à 100 % des risques visés écartés par l'Entité.</summary>
    public ConfigurationDto ChoisirClemence(int clemence)
    {
        if (clemence is < 0 or > 100) throw new HubException(L.T("erreur.clemence"));
        service.Clemence = clemence;
        return ConfigurationDto.Depuis(service);
    }

    /// <summary>« Esprits fertiles » : 1 (le monde tel quel), 3, 10 ou 30 fois plus d'idées par combinaison.</summary>
    public ConfigurationDto ChoisirEspritsFertiles(int facteur)
    {
        if (Array.IndexOf(MondeSimule.EspritsFertilesPermis, facteur) < 0) throw new HubException(L.T("erreur.esprits"));
        service.EspritsFertiles = facteur;
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
            return new ApercuDto(false, Lib.Message(e));
        }
    }

    /// <summary>
    /// Crée un monde neuf avec ces réglages (graine absente : au hasard). L'ancien monde est gardé en copie, sauf si l'on a
    /// décoché la case.
    /// Des réglages impossibles sont refusés sans rien changer.
    /// </summary>
    public async Task<HorlogeDto> CreerMonde(DemandeConfiguration demande)
    {
        MondeSimule monde;
        try
        {
            monde = service.Reinitialiser(demande.Valider(), demande.GarderAncien);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            throw new HubException(Lib.Message(e));
        }
        service.FacteurAtteint = 0;
        await Clients.Diffuser("MondeReinitialise", () => (uint)monde.Graine);
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
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

    /// <summary>Met une sauvegarde (nom tiré de la liste) à la corbeille du système ; rend la liste à jour.</summary>
    public List<SauvegardeDto> MettreALaCorbeille(string nom)
    {
        try
        {
            service.MettreALaCorbeille(nom);
        }
        catch (Exception e) when (e is ArgumentException or CorbeilleRefusee)
        {
            throw new HubException(e.Message);
        }
        catch (FileNotFoundException)
        {
            throw new HubException(L.T("sauvegarde.disparue"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Le message du système peut contenir des chemins : on ne les montre pas au navigateur.
            throw new HubException(L.T("corbeille.refus"));
        }
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
        await Clients.Diffuser("MondeReinitialise", () => (uint)monde.Graine);
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        return await DiffuserHorloge();
    }

    /// <summary>Trajet à pied le moins pénible entre deux cases (null si l'une est dans l'eau ou si on ne peut pas y aller à pied).</summary>
    public TrajetDto? Trajet(int x1, int y1, int x2, int y2)
    {
        var carte = service.Monde.Carte;
        if (Math.Min(Math.Min(x1, y1), Math.Min(x2, y2)) < 0 || Math.Max(x1, x2) >= carte.Largeur || Math.Max(y1, y2) >= carte.Hauteur)
        {
            throw new HubException(L.T("erreur.pointsHorsCarte"));
        }
        return TrajetDto.Depuis(carte, x1, y1, x2, y2);
    }

    /// <summary>Maladies apparues depuis le début du monde.</summary>
    public List<MaladieDto> Maladies() => MaladieDto.Toutes(service.Monde);

    /// <summary>L'Entité provoque une catastrophe. Renvoie le numéro de l'événement qui l'annonce (0 si elle n'a pas pu avoir lieu).</summary>
    public async Task<long> Declencher(DemandeCatastrophe demande)
    {        var monde = service.Monde;
        var id = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande), m => Entite.Pouvoirs.Declencher(m, demande),
            r => Entite.Registre.Texte(L.T(r > 0 ? "llm.resultat.catastrophe" : "llm.resultat.catastropheImpossible"), r > 0)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
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
        var texte = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande), m => Entite.Pouvoirs.SoufflerFait(m, demande),
            r => Entite.Registre.Texte(r.Texte, r.Reussi))).Texte;
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        return texte;
    }

    /// <summary>L'arbre des savoirs du monde (0) ou d'une communauté, et le possible adjacent.</summary>
    public ArbreDto ArbreDesSavoirs(int communaute) => Serveur.Arbre.Construire(service, communaute);

    /// <summary>Pièces, façons, jointures et liens que l'Entité peut proposer de combiner à cet humain.</summary>
    public ChoixCombinaisonDto ChoixCombinaison(int humain) => Serveur.Arbre.Choix(service, humain);

    /// <summary>
    /// L'Entité souffle une combinaison à un humain : il l'essaie aussitôt avec ce qu'il a ; les lois du monde décident. Renvoie
    /// ce qui est arrivé, en mots (la ligne du journal).
    /// </summary>
    public async Task<string> SoufflerCombinaison(DemandeCombinaison demande)
    {
        var texte = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande), m => Entite.Pouvoirs.SoufflerCombinaisonFait(m, demande),
            r => Entite.Registre.Texte(r.Texte, r.Reussi))).Texte;
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        return texte;
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
        var texte = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, new Entite.ActeLlm { Type = Entite.Actes.Exaucer, Priere = priere },
            m => Entite.Pouvoirs.Exaucer(m, priere), r => Entite.Registre.Texte(r, true)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        return texte;
    }

    /// <summary>L'Entité refuse une prière en attente, en secret (par défaut) ou par un signe. Renvoie, en mots, ce qui s'est passé.</summary>
    public async Task<string> Refuser(int priere, bool signe = false)
    {
        var texte = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, new Entite.ActeLlm { Type = Entite.Actes.Refuser, Priere = priere, Signe = signe },
            m => Entite.Pouvoirs.Refuser(m, priere, signe), r => Entite.Registre.Texte(r, true)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        return texte;
    }

    private int EnAttente() => service.Monde.Prieres().Count(p => p.Issue == Simulation.Humains.IssuePriere.EnAttente && p.Refus == Simulation.Humains.RefusPriere.Aucun);

    /// <summary>L'Entité exauce toutes les prières en attente. Renvoie le bilan en mots.</summary>
    public async Task<string> ExaucerToutes()
    {
        var n = EnAttente();
        var texte = Entite.Registre.Faire(service, Entite.Registre.Joueur, new Entite.ActeLlm { Type = "exaucer_toutes" }, m => m.ExaucerToutes(),
            r => Entite.Registre.Texte(L.Rediger(r), n > 0, n));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        return L.Rediger(texte);
    }

    /// <summary>L'Entité refuse toutes les prières en attente, en secret (par défaut) ou par un signe. Renvoie le bilan en mots.</summary>
    public async Task<string> RefuserToutes(bool signe = false)
    {
        var n = EnAttente();
        var texte = Entite.Registre.Faire(service, Entite.Registre.Joueur, new Entite.ActeLlm { Type = "refuser_toutes", Signe = signe }, m => m.RefuserToutes(signe),
            r => Entite.Registre.Texte(L.Rediger(r), n > 0, n));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        return L.Rediger(texte);
    }

    /// <summary>Gestes que l'Entité peut faire, manières, formes de mort et maladies.</summary>
    public ChoixGestesDto ChoixGestes() => GestesEntite.Choix(service);

    /// <summary>L'Entité fait un geste sur un humain ou une communauté. Renvoie, en mots, ce qui s'est passé.</summary>
    public async Task<string> Agir(DemandeGeste demande)
    {
        var monde = service.Monde;
        var texte = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande), m => Entite.Pouvoirs.Agir(m, demande),
            r => Entite.Registre.Texte(r, true)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
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
        var resultats = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande), m => Entite.Pouvoirs.Parler(m, demande),
            r => Entite.Registre.Chacun(monde, r)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        return Resultats(resultats);
    }

    /// <summary>Paroles qu'on peut adresser à une communauté entière, et les communautés qu'elles peuvent viser (null : elle n'existe plus).</summary>
    public ChoixParolesCommunauteDto? ChoixParolesCommunaute(int communaute) => ParolesEntite.ChoixCommunaute(service, communaute);

    /// <summary>
    /// L'Entité parle à une communauté entière, envers d'autres ou vers une direction : chaque adulte la reçoit et dit ce qu'il
    /// en pense ; le groupe en débattra le jour venu. Renvoie ce que chacun en a fait et quand le groupe en débattra.
    /// </summary>
    public async Task<ResultatParoleCommunauteDto> ParlerACommunaute(DemandeParoleCommunaute demande)
    {
        var monde = service.Monde;
        var (resultats, groupe) = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande),
            m => Entite.Pouvoirs.ParlerACommunaute(m, demande), r => Entite.Registre.Chacun(monde, r.Membres) with { Resultats = r.Groupe.Concat(Entite.Registre.Chacun(monde, r.Membres).Resultats).ToList() }));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        return new ResultatParoleCommunauteDto(Resultats(resultats), groupe);
    }

    /// <summary>Paroles qu'on peut dire au Monde, manières, plantes, communautés et terres habitées (0.37.1).</summary>
    public ChoixParolesMondeDto ChoixParolesMonde() => ParolesMonde.Choix(service);

    /// <summary>Paroles de l'Entité au Monde, de la plus récente à la plus ancienne.</summary>
    public List<ParoleMondeDto> ParolesAuMonde() => ParolesMonde.Liste(service);

    /// <summary>
    /// L'Entité parle au Monde entier, ou à toute une terre : chacun l'entend, la comprend, y croit ou non et la suit ou non ; une
    /// parole qui vise une communauté est débattue par chacune des autres. Renvoie le bilan par communauté.
    /// </summary>
    public async Task<ResultatParoleMondeDto> ParlerAuMonde(DemandeParoleMonde demande)
    {
        var monde = service.Monde;
        var (parole, resultats) = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande),
            m => ParolesMonde.Parler(m, demande, Entite.Registre.Joueur), r => new Entite.FaitEntite(
                [.. r.Parole.Bilans.Select(b => ParolesMonde.Bilan(b, monde.Communautes()))], r.Resultats.Count > 0 ? 1 : 0, r.Resultats.Count)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        return ParolesMonde.Resultat(monde, parole, resultats);

    }

    /// <summary>L'Entité inspire un penchant amoureux (ou une passion) à des humains, pour quelqu'un ou pour qui le monde choisit.</summary>
    public async Task<List<ResultatParoleDto>> Inspirer(DemandePenchant demande)
    {
        var monde = service.Monde;
        var resultats = Appeler(() => Entite.Registre.Faire(service, Entite.Registre.Joueur, Entite.Registre.Depuis(demande), m => Entite.Pouvoirs.Inspirer(m, demande),
            r => Entite.Registre.Chacun(monde, r)));
        service.Sauvegarder();
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        return Resultats(resultats);
    }

    /// <summary>L'Entité-LLM : connexion, réglages du monde, file d'attente, dernière erreur, propositions en attente.</summary>
    public Entite.EtatEntiteLlmDto EtatEntiteLlm() => entite.Etat();

    /// <summary>Change la connexion au LLM (gardée pour l'installation).</summary>
    public Entite.EtatEntiteLlmDto ReglerConnexionLlm(Entite.ConnexionLlm connexion)
    {
        Appeler(() => entite.Connexion = connexion ?? throw new ArgumentException(L.T("erreur.demandeVide")));
        return entite.Etat();
    }

    /// <summary>Teste une connexion : rend les modèles installés (erreur lisible si le LLM ne répond pas).</summary>
    public async Task<List<string>> ModelesLlm(Entite.ConnexionLlm connexion)
    {
        if (connexion?.Erreur() is { } erreur) throw new HubException(erreur);
        try
        {
            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await entite.Modeles(connexion!, delai.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            throw new HubException(L.T("llm.erreur.connexion", ("detail", e is TaskCanceledException ? L.T("llm.erreur.delai") : e.Message)));
        }
    }

    /// <summary>Change les réglages de l'Entité-LLM pour ce monde ; elle parlera la langue de ce navigateur.</summary>
    public Entite.EtatEntiteLlmDto ReglerEntiteLlm(Entite.ReglagesEntiteLlm reglages)
    {
        Appeler(() => { entite.Regler(reglages ?? throw new ArgumentException(L.T("erreur.demandeVide")), L.Courante.Code); return 0; });
        return entite.Etat();
    }

    /// <summary>« Que ferait l'Entité ? » pour un humain (ou une communauté si humain = 0) ; rend la place dans la file.</summary>
    public int QueFeraitLEntite(int humain, int communaute) => Appeler(() => entite.Demander(humain, communaute));

    // ───────────── Dialogue direct avec un humain (0.36.2) ─────────────

    /// <summary>Ouvre un dialogue avec un humain (voie : Reve, Voix, Apparition ; prière : celle par laquelle il appelle, 0 : aucune).</summary>
    public Entite.DialogueDto? OuvrirDialogue(int humain, string voie, int priere = 0, bool attendre = false) => Appeler(() => dialogues.Ouvrir(humain, voie, priere, attendre));

    /// <summary>Vrai si l'humain dort (on ne lui parle en rêve que s'il dort).</summary>
    public bool Endormi(int humain) => service.Monde.Dort(humain);

    /// <summary>Le rêve attendu (null : aucun).</summary>
    public Entite.AttenteDto? DialogueAttendu() => dialogues.Attente();

    /// <summary>Ne plus attendre son sommeil.</summary>
    public void AnnulerAttenteDialogue() => dialogues.AnnulerAttente();

    /// <summary>L'Entité dit quelque chose ; la réponse arrive par « Dialogue ».</summary>
    public Entite.DialogueDto DireAuDialogue(long id, string texte) => Appeler(() => dialogues.Dire(id, texte));

    public Entite.DialogueDto TerminerDialogue(long id) => Appeler(() => dialogues.Terminer(id));

    public Entite.DialogueDto? DialogueEnCours() => dialogues.Courant();

    public Entite.DialogueDto LireDialogue(long id) => Appeler(() => dialogues.Dto(id));

    public List<Entite.DialogueResumeDto> ListeDialogues(int humain = 0) => dialogues.Liste(humain);

    /// <summary>Échanges avec le LLM, du plus récent au plus ancien (100 au plus), filtrés par humain, communauté ou statut.</summary>
    public List<Entite.EchangeDto> EchangesLlm(int humain = 0, int communaute = 0, int? statut = null, long avant = 0) =>
        service.Depot.Echanges(humain, communaute, statut, avant).Select(e => entite.Dto(e, complet: false)).ToList();

    /// <summary>Un échange complet : ce qu'a lu le LLM et sa réponse brute compris.</summary>
    public Entite.EchangeDto? EchangeLlm(long id) => service.Depot.Echange(id) is { } e ? entite.Dto(e, complet: true) : null;

    /// <summary>Le joueur accepte une proposition du LLM : ses actes sont appliqués maintenant. Rend ce que les règles en ont fait.</summary>
    public async Task<List<string>> AccepterProposition(long id)
    {
        try
        {
            return await entite.Appliquer(id);
        }
        catch (ArgumentException e)
        {
            throw new HubException(e.Message);
        }
    }

    /// <summary>Le joueur rejette une proposition du LLM.</summary>
    public Entite.EtatEntiteLlmDto RejeterProposition(long id)
    {
        Appeler(() => { entite.Rejeter(id); return 0; });
        return entite.Etat();
    }

    /// <summary>Oublie le caractère que le LLM s'était donné (il s'en choisira un autre).</summary>
    public Entite.EtatEntiteLlmDto OublierCaractereLlm()
    {
        entite.OublierCaractere();
        return entite.Etat();
    }

    /// <summary>Fenêtre « Actes de l'Entité » : comptes, courbe par année et une page d'actes (du plus récent au plus ancien).</summary>
    public Entite.ActesEntiteDto ActesEntite(Entite.FiltreActesDto filtre) => Appeler(() => Entite.ActesEntite.Lister(service, filtre));

    /// <summary>Les lignes du journal écrites par un acte de l'Entité.</summary>
    public List<EvenementDto> JournalActe(long id) => Entite.ActesEntite.Journal(service, id);

    /// <summary>Défait un acte de l'Entité libre : les valeurs d'avant reviennent, les suites restent.</summary>
    public async Task<List<string>> DefaireActe(long id)
    {
        var lignes = Appeler(() => Entite.Libres.Defaire(service, id));
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(service.Monde));
        await Clients.Diffuser("EntiteLlm", () => entite.Etat());
        return lignes;
    }

    /// <summary>Lève un décret de l'Entité libre.</summary>
    public List<Entite.DecretDto> LeverDecret(int id)
    {
        Appeler(() =>
        {
            var (r, premier, dernier) = service.Monde.Executer(m => m.LeverDecret(id));
            if (r is null) throw new ArgumentException(L.T("libre.erreur.decret"));
            Entite.MarquesLibre.Ajouter(premier, dernier);
            Entite.Registre.Noter(service, Entite.Registre.Joueur, new Entite.ActeLlm { Type = "lever_decret", Param = id }, Entite.Registre.Texte(L.Rediger(r), true),
                premier, dernier);
            return 0;
        });
        service.Sauvegarder();
        return Entite.ActesEntite.Decrets(service);
    }

    /// <summary>Les décrets de l'Entité libre (en vigueur d'abord).</summary>
    public List<Entite.DecretDto> Decrets() => Entite.ActesEntite.Decrets(service);

    /// <summary>Applique un pouvoir de l'Entité ; une demande invalide devient une erreur pour le navigateur.</summary>
    private static T Appeler<T>(Func<T> pouvoir)
    {
        try
        {
            return pouvoir();
        }
        catch (ArgumentException e)
        {
            throw new HubException(e.Message);
        }
    }

    private List<ResultatParoleDto> Resultats(List<(int Humain, Simulation.Recits.Recit Resultat)> resultats)
    {
        var (_, humains) = service.Monde.InstantaneHumains();
        return resultats.Select(r => new ResultatParoleDto(r.Humain, r.Humain >= 1 && r.Humain <= humains.Count ? humains[r.Humain - 1].Nom : "?", L.Rediger(r.Resultat))).ToList();
    }

    /// <summary>Au redémarrage du serveur : reprendre la dernière vitesse (vrai) ou repartir en pause (faux).</summary>
    public async Task<HorlogeDto> ChoisirRedemarrage(bool reprendreVitesse)
    {
        service.ReprendreVitesse = reprendreVitesse;
        return await DiffuserHorloge();
    }

    /// <summary>Fiche détaillée d'un humain (null s'il n'existe pas).</summary>
    public FicheDto? Fiche(int id) => FicheDto.Depuis(service, id);

    /// <summary>Visages de plusieurs humains, pour les vignettes des listes (200 au plus).</summary>
    public List<VisageDto> Visages(int[] ids) => VisageDto.De(service.Monde, ids ?? []);

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
        if (!filtre.EstValide)
        {
            throw new HubException(L.T("erreur.filtreJournal"));
        }
        var types = EvenementDto.TypesDepuis(filtre.Types) ?? throw new HubException(L.T("erreur.typeEvenement"));
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
        await Clients.Diffuser("Horloge", () => HorlogeDto.Depuis(service));
        return HorlogeDto.Depuis(service);
    }

    /// <summary>
    /// Recommence le monde à l'an 1 avec la graine donnée (ou une graine au hasard si elle est absente), et les mêmes réglages.
    /// Tous les navigateurs connectés sont prévenus pour recharger la carte.
    /// </summary>
    public async Task<HorlogeDto> Reinitialiser(long? graine)
    {
        if (graine is < 0 or > uint.MaxValue)
        {
            throw new HubException(L.T("erreur.graine"));
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
            throw new HubException(Lib.Message(e));
        }
        service.FacteurAtteint = 0;
        await Clients.Diffuser("MondeReinitialise", () => valeur);
        await Clients.Diffuser("Humains", () => HumainDto.Depuis(monde));
        return await DiffuserHorloge();
    }
}

public sealed record ReponsePing(DateTimeOffset HeureServeur, string Monde);

/// <summary>Demande « avancer jusqu'à » venue du navigateur.</summary>
public sealed record DemandeObjectif(string Type, long? Annee = null, int? Saison = null, int? Jour = null, int? Heure = null);

/// <summary>
/// Les humains de fond, vus d'une fiche : option active, cet humain est de fond en ce moment, il est gardé en individu, sa
/// communauté l'est, combien de vivants sont de fond, et le seuil.
/// </summary>
public sealed record FondDto(bool Actif, bool DeFond, bool Garde, bool CommunauteGardee, int Nombre, int Seuil);

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
    bool Eteint,
    int DeFond)
{
    public static HorlogeDto Depuis(ServiceMonde service)
    {
        var (secondes, pas, facteurDemande, objectif) = service.Monde.Horloge;
        var facteur = objectif is null ? facteurDemande : Vitesse.FacteurMax;
        var date = Calendrier.Decomposer(secondes);
        return new HorlogeDto(secondes, pas, L.Date(date), date.Annee, L.Saison(date.Saison),
            date.JourDeSaison, date.Heure, date.Minute, date.EstJour, date.Lumiere,
            objectif is null ? Vitesse.NiveauDe(facteur) : -1, facteur,
            facteur == 0 ? 0 : service.FacteurAtteint, objectif is null ? null : Lib.Objectif(objectif), service.ReprendreVitesse, service.PauseSurCatastrophe, service.Monde.Eteint,
            service.Monde.DeFond);
    }
}
