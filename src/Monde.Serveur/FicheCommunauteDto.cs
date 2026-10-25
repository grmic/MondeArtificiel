using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Un humain nommé, avec une valeur (QI, karma…), pour les « plus haut » et « plus bas » de la fiche.</summary>
public sealed record NommeDto(int Id, string Nom, double Valeur);

/// <summary>Une année de la communauté : naissances (de mères membres), morts (de membres), et le solde des arrivées et départs.</summary>
/// <param name="Solde">Arrivées moins départs, déduits de l'écart de population (null : population inconnue en début ou fin d'année).</param>
public sealed record AnneeCommunauteDto(int An, int Naissances, int Morts, int MortsMoins5, int? Population, int? Solde);

/// <summary>Un indicateur de la communauté, comparé au monde entier et aux chasseurs-cueilleurs réels (null : pas assez de cas).</summary>
public sealed record IndicateurDto(string Nom, double? Communaute, double? Monde, string? Reference, string Unite, int Cas);

/// <summary>Une découverte faite par un membre (estimee : communauté retrouvée après coup, faute d'avoir été notée).</summary>
public sealed record DecouverteMembreDto(string Date, string Savoir, int Humain, string Nom, string Origine, long Evenement, bool Estimee);

/// <summary>Un membre, pour la liste triable de la fiche.</summary>
public sealed record MembreFicheDto(int Id, string Nom, string Sexe, int Age, int Qi, double Karma, int Croyance, string Etape, string? Role);

/// <summary>Population : pyramide des âges, années, indicateurs, causes de mort et courbe.</summary>
/// <param name="Pyramide">Par tranche de 5 ans (0-4 … 80+) : hommes, femmes.</param>
public sealed record PopulationDto(IReadOnlyList<int[]> Pyramide, IReadOnlyList<AnneeCommunauteDto> Annees, IReadOnlyList<IndicateurDto> Indicateurs,
    IReadOnlyList<string> Causes, int MortsEnTout, IReadOnlyList<TailleDto> Tailles);

/// <summary>Sexes par tranche (enfants, 14-44 ans, 45 ans et plus) et orientations des adultes en cinq classes.</summary>
public sealed record SexesDto(int[] Hommes, int[] Femmes, IReadOnlyList<string> Classes, int[] Orientations, int CouplesMemeSexe, string? Moeurs);

/// <summary>Intelligence des membres de 6 ans et plus.</summary>
/// <param name="Repartition">Moins de 70, 70-84, 85-114, 115-129, 130 et plus.</param>
/// <param name="Aptitudes">Verbale, spatiale, manuelle, mémoire, artistique : moyenne et qui excelle.</param>
public sealed record IntelligenceDto(int Mesures, double Moyenne, double Mediane, int[] Repartition, NommeDto? PlusHaut, NommeDto? PlusBas,
    IReadOnlyList<AptitudeDto> Aptitudes);

public sealed record AptitudeDto(string Nom, double Moyenne, double MoyenneMonde, NommeDto? Meilleur);

/// <summary>Caractère moyen des adultes (0 à 1), comparé au monde ; karma moyen, meilleur et pire.</summary>
public sealed record CaractereDto(IReadOnlyList<AptitudeDto> Traits, IReadOnlyList<string> Distinctifs, double? KarmaMoyen, NommeDto? MeilleurKarma,
    NommeDto? PireKarma);

/// <summary>Savoirs : découvertes de ses membres, appris d'autres groupes, perdus, et ceux que seuls un ou deux membres détiennent.</summary>
public sealed record SavoirsCommunauteDto(IReadOnlyList<DecouverteMembreDto> Decouvertes, IReadOnlyList<string> Connus, IReadOnlyList<string> Appris,
    IReadOnlyList<string> Perdus, IReadOnlyList<FragileDto>? Fragiles = null);

/// <summary>Un savoir que seuls un ou deux membres détiennent : s'ils meurent ou l'oublient sans l'avoir appris à d'autres, il se perd.</summary>
public sealed record FragileDto(string Savoir, IReadOnlyList<NommeDto> Detenteurs);

/// <summary>Un malade ou un captif, avec ce qu'il a ou d'où il vient.</summary>
public sealed record PersonneDto(int Id, string Nom, string Detail);

/// <summary>Territoire, nourriture et santé.</summary>
public sealed record VieCommunauteDto(int Huttes, int Foyers, IReadOnlyList<PersonneDto> Malades, int MortsDeMaladie, IReadOnlyList<CatastropheDto> Catastrophes,
    IReadOnlyList<PersonneDto> Captifs, FeuReservesDto? Feu = null, RencontresCommunauteDto? Rencontres = null);

/// <summary>
/// Les rencontres et les métiers d'une communauté : ceux que les siens disent spécialistes (et combien d'ouvrages ils ont faits pour
/// d'autres), ses rassemblements avec d'autres groupes (du plus récent au plus ancien), ses membres en visite ailleurs, ses visiteurs,
/// et ceux qui sont venus d'une autre communauté.
/// </summary>
public sealed record RencontresCommunauteDto(IReadOnlyList<PersonneDto> Specialistes, IReadOnlyList<RassemblementDto> Rassemblements,
    IReadOnlyList<PersonneDto> EnVisite, IReadOnlyList<PersonneDto> Visiteurs, IReadOnlyList<PersonneDto> VenusDAilleurs);

/// <summary>Un rassemblement avec une autre communauté : quand, avec qui, combien de jours, et ce qui s'y est passé.</summary>
public sealed record RassemblementDto(string Debut, int Autre, string NomAutre, bool Hote, int Jours, bool EnCours, int Participants, int Cadeaux,
    int Appris, int Couples, long Evenement, long Bilan);

/// <summary>
/// Le feu et les réserves d'une communauté : braises qui couvent, combien savent allumer, son porteur du feu, ses réserves
/// (en jours de vivres pour tous), l'idée qu'elle se fait des réserves et les incendies dont elle se souvient.
/// </summary>
public sealed record FeuReservesDto(int Braises, int SaventAllumer, int Adultes, NommeDto? Porteur, double JoursDeVivres, IReadOnlyList<string> Reserves,
    string? Idee, IReadOnlyList<CatastropheDto> Incendies);

/// <summary>Foi : religions présentes, croyance, porteurs, lieux sacrés proches et prières récentes.</summary>
/// <param name="Amour">Moyennes des adultes du regard sur l'invisible : amour, crainte, révolte (0 à 100).</param>
/// <param name="Effets">Gestes de l'Entité qui pèsent encore sur elle.</param>
/// <param name="Souvenirs">Ce qu'elle se raconte des gestes visibles de l'Entité.</param>
public sealed record FoiCommunauteDto(int? Croyance, IReadOnlyList<string> Religions, IReadOnlyList<PorteurDto> Porteurs, IReadOnlyList<string> Lieux,
    IReadOnlyList<PriereDto> Prieres, int? Amour, int? Crainte, int? Revolte, IReadOnlyList<string> Effets, IReadOnlyList<SouvenirDto> Souvenirs,
    BilanPrieresDto? Bilan = null);

/// <summary>Un geste visible de l'Entité dont la communauté se souvient (force 0 à 100 : il s'efface au fil des générations).</summary>
public sealed record SouvenirDto(string Date, string Texte, bool Bienfait, int Force, string? Faute, long Evenement);

/// <summary>
/// Fiche d'une communauté : ce que montre déjà la fenêtre Communautés (<see cref="Base"/>), et le reste rangé par thème.
/// </summary>
public sealed record FicheCommunauteDto(CommunauteDto Base, string Age, int Vivants, int Adultes, PopulationDto Population, SexesDto Sexes,
    IntelligenceDto Intelligence, CaractereDto Caractere, SavoirsCommunauteDto Savoirs, VieCommunauteDto Vie, FoiCommunauteDto Foi,
    IReadOnlyList<EvenementDto> Marquants, IReadOnlyList<EvenementDto> Entite, IReadOnlyList<MembreFicheDto> Membres,
    AutoriteDto? Autorite = null, IReadOnlyList<MandatDto>? Paroles = null)
{
    private static System.Globalization.NumberFormatInfo Fr => L.Courante.Nombres;

    /// <summary>Années montrées au plus dans le tableau (les plus récentes).</summary>
    private const int AnneesMax = 60;

    public static FicheCommunauteDto? Depuis(ServiceMonde service, int id)
    {
        var monde = service.Monde;
        var communautes = monde.Communautes();
        if (id < 1 || id > communautes.Count) return null;
        var instantane = monde.InstantaneHumains();
        var (maintenant, humains) = instantane;
        if (CommunauteDto.Une(service, id, instantane) is not { } dto) return null;
        var c = communautes[id - 1];
        var membres = humains.Where(h => h.EstVivant && h.CommunauteId == id).ToList();
        var adultes = membres.Where(h => !h.EstEnfant(maintenant)).ToList();
        var vivants = humains.Where(h => h.EstVivant).ToList();
        var adultesMonde = vivants.Where(h => !h.EstEnfant(maintenant)).ToList();

        // Journal : événements marquants de la communauté, en séparant les gestes de l'Entité.
        var marquants = FicheDto.Journal(service, new FiltreJournal(Limite: 300, Communaute: id, Significatifs: true), [])
            .Select(EvenementDto.Depuis).ToList();

        var fin = c.DisperseeLe ?? maintenant;
        return new FicheCommunauteDto(dto, AgeEnMots(fin - c.FondeeLe), membres.Count, adultes.Count,
            CalculerPopulation(service, c, humains, membres, vivants, maintenant),
            CalculerSexes(membres, maintenant, dto.Moeurs),
            CalculerIntelligence(membres, vivants, maintenant),
            CalculerCaractere(adultes, adultesMonde),
            CalculerSavoirs(service, c, humains, membres),
            CalculerVie(service, c, humains, membres, maintenant),
            CalculerFoi(service, id, humains, membres, adultes, communautes),
            marquants.Where(e => e.Origine != "entite").Take(150).ToList(),
            marquants.Where(e => e.Origine == "entite").Take(60).ToList(),
            membres.OrderBy(h => h.Naissance).Select(h => new MembreFicheDto(h.Id, h.Nom, h.Sexe == Sexe.Femme ? "femme" : "homme",
                (int)h.AgeEnAnnees(maintenant), (int)Math.Round(h.Personnalite.Intelligence.Generale), Math.Round(h.Karma, 1), (int)Math.Round(h.Croyance),
                h.EstBebe(maintenant) ? "bébé" : h.EstEnfant(maintenant) ? "enfant" : "adulte", Role(h))).ToList(),
            ParolesEntite.Autorite(service, id), ParolesEntite.Mandats(service, id));
    }

    private static string? Role(Humain h) => h.Role switch
    {
        RoleReligieux.Prophete => L.T("role.prophete", ("g", Lib.G(h))),
        RoleReligieux.Pretre => L.T("role.pretre", ("g", Lib.G(h))),
        _ => Lib.Metier(h),
    };

    private static string AgeEnMots(long duree)
    {
        var ans = (int)(duree / Calendrier.SecondesParAn);
        if (ans >= 1) return L.T("duree.ans", ("n", ans));
        var jours = (int)(duree / Calendrier.SecondesParJour);
        return L.T("duree.jours", ("n", Math.Max(1, jours)));
    }

    // ───────────── Population ─────────────

    private static PopulationDto CalculerPopulation(ServiceMonde service, Communaute c, List<Humain> humains, List<Humain> membres, List<Humain> vivants,
        long maintenant)
    {
        const int Tranches = 17;
        var pyramide = Enumerable.Range(0, Tranches).Select(_ => new int[2]).ToList();
        foreach (var h in membres)
            pyramide[Math.Min(Tranches - 1, (int)(h.AgeEnAnnees(maintenant) / 5))][h.Sexe == Sexe.Femme ? 1 : 0]++;

        // Les morts gardent la communauté où ils vivaient en dernier ; les nouveau-nés, celle de leur mère.
        var nes = humains.Where(h => h.CommunauteNaissance == c.Id).ToList();
        var morts = humains.Where(h => !h.EstVivant && h.CommunauteId == c.Id && h.MortLe is not null).ToList();
        var tailles = Territoires.Tailles(service, c.Id);
        var points = tailles.Select(t => (t.Secondes, t.Membres)).ToList();
        if (c.Active) points.Add((maintenant, membres.Count));
        int? PopulationA(long t)
        {
            // Le relevé le plus proche, à une saison près.
            var proche = points.Where(p => Math.Abs(p.Secondes - t) <= Calendrier.SecondesParAn / 4).OrderBy(p => Math.Abs(p.Secondes - t)).ToList();
            return proche.Count > 0 ? proche[0].Membres : null;
        }
        var an = Calendrier.SecondesParAn;
        var premier = (int)(c.FondeeLe / an);
        var dernier = (int)(((c.DisperseeLe ?? maintenant) - 1) / an);
        var annees = new List<AnneeCommunauteDto>();
        for (var a = Math.Max(premier, dernier - AnneesMax + 1); a <= dernier; a++)
        {
            long debut = a * an, finAn = (a + 1) * an;
            var n = nes.Count(h => h.Naissance >= debut && h.Naissance < finAn);
            var m = morts.Where(h => h.MortLe >= debut && h.MortLe < finAn).ToList();
            var popDebut = a == premier ? 0 : PopulationA(debut);
            var popFin = PopulationA(Math.Min(finAn, c.DisperseeLe ?? maintenant));
            int? solde = popDebut is { } p0 && popFin is { } p1 ? p1 - p0 - n + m.Count : null;
            annees.Add(new AnneeCommunauteDto(a + 1, n, m.Count, m.Count(h => (h.MortLe!.Value - h.Naissance) < 5 * an), popFin, solde));
        }

        var indicateurs = new List<IndicateurDto>
        {
            Fecondite(nes, membres, humains.Where(h => h.CommunauteNaissance != 0 || h.MereId != 0).ToList(), vivants, maintenant),
            Survie(nes, humains.Where(h => h.MereId != 0).ToList(), maintenant),
            AgeAuDeces(morts, humains.Where(h => !h.EstVivant && h.MortLe is not null).ToList()),
        };
        var causes = morts.GroupBy(h => h.CauseDeMort).OrderByDescending(g => g.Count())
            .Select(g => L.T("population.cause", ("cause", Lib.Cause(g.Key)), ("n", g.Count()))).ToList();
        return new PopulationDto(pyramide, annees, indicateurs, causes, morts.Count, tailles);
    }

    /// <summary>Naissances de la dernière année pour 100 femmes de 15 à 44 ans (chasseurs-cueilleurs : environ 20 à 30).</summary>
    private static IndicateurDto Fecondite(List<Humain> nes, List<Humain> membres, List<Humain> nesMonde, List<Humain> vivants, long maintenant)
    {
        var depuis = maintenant - Calendrier.SecondesParAn;
        static bool Feconde(Humain h, long t) => h.Sexe == Sexe.Femme && h.AgeEnAnnees(t) is >= 15 and < 45;
        var femmes = membres.Count(h => Feconde(h, maintenant));
        var femmesMonde = vivants.Count(h => Feconde(h, maintenant));
        double? Taux(int naissances, int f) => f < 5 ? null : Math.Round(100.0 * naissances / f, 1);
        return new IndicateurDto(L.T("indicateur.fecondite"),
            Taux(nes.Count(h => h.Naissance >= depuis), femmes), Taux(nesMonde.Count(h => h.Naissance >= depuis), femmesMonde),
            L.T("indicateur.fecondite.reference"), "", femmes);
    }

    /// <summary>Part des enfants nés il y a 15 à 40 ans qui ont atteint 15 ans (chasseurs-cueilleurs : environ 57 %, Gurven et Kaplan 2007).</summary>
    private static IndicateurDto Survie(List<Humain> nes, List<Humain> nesMonde, long maintenant)
    {
        var an = Calendrier.SecondesParAn;
        bool Cohorte(Humain h) => h.Naissance <= maintenant - 15 * an && h.Naissance > maintenant - 40 * an;
        static bool Atteint(Humain h) => h.MortLe is not { } m || m - h.Naissance >= 15 * Calendrier.SecondesParAn;
        (double?, int) Part(IEnumerable<Humain> liste)
        {
            var cohorte = liste.Where(Cohorte).ToList();
            return (cohorte.Count < 10 ? null : Math.Round(100.0 * cohorte.Count(Atteint) / cohorte.Count), cohorte.Count);
        }
        var (communaute, cas) = Part(nes);
        return new IndicateurDto(L.T("indicateur.survie"), communaute, Part(nesMonde).Item1,
            L.T("indicateur.survie.reference"), "%", cas);
    }

    /// <summary>Âge moyen au décès de ceux qui ont passé 15 ans (chasseurs-cueilleurs : environ 51 ans de plus en moyenne à 15 ans, soit ~66).</summary>
    private static IndicateurDto AgeAuDeces(List<Humain> morts, List<Humain> mortsMonde)
    {
        (double?, int) Moyenne(IEnumerable<Humain> liste)
        {
            var ages = liste.Select(h => (h.MortLe!.Value - h.Naissance) / (double)Calendrier.SecondesParAn).Where(a => a >= 15).ToList();
            return (ages.Count < 5 ? null : Math.Round(ages.Average(), 1), ages.Count);
        }
        var (communaute, cas) = Moyenne(morts);
        return new IndicateurDto(L.T("indicateur.ageDeces"), communaute, Moyenne(mortsMonde).Item1,
            L.T("indicateur.ageDeces.reference"), L.T("indicateur.ageDeces.unite"), cas);
    }

    // ───────────── Sexes et orientations ─────────────

    private static string Enleve(Humain h) => L.T("captif.enleve", ("g", Lib.G(h)));

    /// <summary>Classes d'orientation, dans l'ordre de <see cref="ClasseOrientation"/>.</summary>
    private static string[] ClassesOrientation => [.. Enumerable.Range(0, 5).Select(i => L.T($"classeOrientation.{i}"))];

    /// <summary>
    /// Classe d'orientation d'un adulte, d'après ses deux attirances continues (vers l'autre sexe, vers le sien), à la manière de
    /// l'échelle de Kinsey : la part de l'attirance qui va vers son propre sexe.
    /// </summary>
    public static int ClasseOrientation(Humain h)
    {
        var autre = h.Orientation.Vers(h.Sexe == Sexe.Homme ? Sexe.Femme : Sexe.Homme);
        var meme = h.Orientation.Vers(h.Sexe);
        if (Math.Max(autre, meme) < 0.2) return 4;
        var part = meme / (autre + meme);
        return part switch { < 0.1 => 0, < 0.35 => 1, <= 0.65 => 2, _ => 3 };
    }

    private static SexesDto CalculerSexes(List<Humain> membres, long maintenant, string? moeurs)
    {
        int Tranche(Humain h) => h.EstEnfant(maintenant) ? 0 : h.AgeEnAnnees(maintenant) < 45 ? 1 : 2;
        var hommes = new int[3];
        var femmes = new int[3];
        foreach (var h in membres) (h.Sexe == Sexe.Femme ? femmes : hommes)[Tranche(h)]++;
        var orientations = new int[5];
        foreach (var h in membres.Where(h => !h.EstEnfant(maintenant))) orientations[ClasseOrientation(h)]++;
        var ids = membres.Select(h => h.Id).ToHashSet();
        var couples = membres.Count(h => h.PartenaireId > h.Id && ids.Contains(h.PartenaireId)
                                         && membres.First(x => x.Id == h.PartenaireId).Sexe == h.Sexe);
        return new SexesDto(hommes, femmes, ClassesOrientation, orientations, couples, moeurs);
    }

    // ───────────── Intelligence ─────────────

    private static IntelligenceDto CalculerIntelligence(List<Humain> membres, List<Humain> vivants, long maintenant)
    {
        var mesures = membres.Where(h => h.AgeEnAnnees(maintenant) >= 6).ToList();
        var mesuresMonde = vivants.Where(h => h.AgeEnAnnees(maintenant) >= 6).ToList();
        var repartition = new int[5];
        foreach (var h in mesures)
        {
            var qi = h.Personnalite.Intelligence.Generale;
            repartition[qi < 70 ? 0 : qi < 85 ? 1 : qi < 115 ? 2 : qi < 130 ? 3 : 4]++;
        }
        NommeDto? Nomme(Humain? h, Func<Humain, double> v) => h is null ? null : new NommeDto(h.Id, h.Nom, Math.Round(v(h)));
        double Gen(Humain h) => h.Personnalite.Intelligence.Generale;
        var aptitudes = new (string Nom, Func<Humain, double> Valeur)[]
        {
            (L.T("aptitude.verbale"), h => h.Personnalite.Intelligence.Verbale),
            (L.T("aptitude.spatiale"), h => h.Personnalite.Intelligence.Spatiale),
            (L.T("aptitude.manuelle"), h => h.Personnalite.Intelligence.Manuelle),
            (L.T("aptitude.memoire"), h => h.Personnalite.Intelligence.Memoire),
            (L.T("aptitude.artistique"), h => h.Personnalite.Intelligence.Artistique),
        };
        var tries = mesures.Select(Gen).Order().ToList();
        return new IntelligenceDto(mesures.Count,
            mesures.Count == 0 ? 0 : Math.Round(tries.Average(), 1),
            mesures.Count == 0 ? 0 : Math.Round(tries.Count % 2 == 1 ? tries[tries.Count / 2] : (tries[tries.Count / 2 - 1] + tries[tries.Count / 2]) / 2, 1),
            repartition, Nomme(mesures.MaxBy(Gen), Gen), Nomme(mesures.MinBy(Gen), Gen),
            aptitudes.Select(a => new AptitudeDto(a.Nom, mesures.Count == 0 ? 0 : Math.Round(mesures.Average(a.Valeur), 1),
                mesuresMonde.Count == 0 ? 0 : Math.Round(mesuresMonde.Average(a.Valeur), 1), Nomme(mesures.MaxBy(a.Valeur), a.Valeur))).ToList());
    }

    // ───────────── Caractère ─────────────

    private static CaractereDto CalculerCaractere(List<Humain> adultes, List<Humain> adultesMonde)
    {
        var traits = new (string Cle, Func<Humain, double> Valeur)[]
        {
            ("ouverture", h => h.Personnalite.Ouverture),
            ("conscience", h => h.Personnalite.Conscience),
            ("extraversion", h => h.Personnalite.Extraversion),
            ("agreabilite", h => h.Personnalite.Agreabilite),
            ("nevrosisme", h => h.Personnalite.Nevrosisme),
        };
        var liste = new List<AptitudeDto>();
        var distinctifs = new List<string>();
        foreach (var t in traits)
        {
            var m = adultes.Count == 0 ? 0 : adultes.Average(t.Valeur);
            var monde = adultesMonde.Count == 0 ? 0 : adultesMonde.Average(t.Valeur);
            liste.Add(new AptitudeDto(L.T($"trait.{t.Cle}"), Math.Round(m, 2), Math.Round(monde, 2), null));
            // Un écart d'un tiers d'écart-type des moyennes individuelles (~0,15) se voit déjà dans un groupe.
            if (adultes.Count >= 5 && Math.Abs(m - monde) >= 0.05) distinctifs.Add(L.T("caractere.distinctif", ("comment", L.T($"caractere.{t.Cle}.{(m > monde ? "haut" : "bas")}")),
                ("trait", L.T($"trait.{t.Cle}")), ("v", m.ToString("0.00", Fr)), ("monde", monde.ToString("0.00", Fr))));
        }
        NommeDto? Karma(Humain? h) => h is null ? null : new NommeDto(h.Id, h.Nom, Math.Round(h.Karma, 1));
        return new CaractereDto(liste, distinctifs, adultes.Count == 0 ? null : Math.Round(adultes.Average(h => h.Karma), 1),
            Karma(adultes.MaxBy(h => h.Karma)), Karma(adultes.MinBy(h => h.Karma)));
    }

    // ───────────── Savoirs ─────────────

    private static SavoirsCommunauteDto CalculerSavoirs(ServiceMonde service, Communaute c, List<Humain> humains, List<Humain> membres)
    {
        var (_, _, decouvertes) = service.Monde.Installations();
        Humain? H(int id) => id >= 1 && id <= humains.Count ? humains[id - 1] : null;
        // Découverte d'avant la 0.27 : la communauté n'était pas notée ; celle du journal, sinon la dernière de l'inventeur.
        var siennes = decouvertes
            .Select(d => (D: d, Communaute: d.CommunauteId != 0 ? d.CommunauteId : 0, Estimee: false))
            .Where(x => x.Communaute == c.Id || x.Communaute == 0 && H(x.D.HumainId)?.CommunauteId == c.Id)
            .Select(x => x.Communaute == 0 ? x with { Estimee = true } : x)
            .OrderBy(x => x.D.Le)
            .Select(x => new DecouverteMembreDto(L.Date(x.D.Le), Decouvertes.NomDecouverte(x.D.Effet, x.D.Pieces), x.D.HumainId,
                H(x.D.HumainId)?.Nom ?? "?", SavoirsDto.Origine(x.D.Origine), x.D.EvenementId, x.Estimee))
            .ToList();

        static bool Utile(Savoir s) => s.Confiance >= 0.4 && s.Effet is not (Effet.Rien or Effet.Tiedeur);
        var ids = membres.Select(h => h.Id).ToHashSet();
        var connus = membres.SelectMany(m => m.Savoirs.Where(Utile).Select(s => s.Effet)).ToHashSet();
        var appris = membres.SelectMany(m => m.Savoirs.Where(s => Utile(s) && s.MaitreId != 0 && !ids.Contains(s.MaitreId)
                                                                   && H(s.MaitreId) is { CommunauteId: var autre } && autre != c.Id && autre != 0)
                .Select(s => (s.Effet, Autre: H(s.MaitreId)!.CommunauteId)))
            .Distinct().GroupBy(x => x.Effet)
            .Select(g => service.Monde.Communautes()[g.First().Autre - 1].Nom is var nom
                ? L.T("savoirs.venuDe", ("effet", L.T($"effet.{g.Key}")), ("nom", L.CommunautePhrase(nom)), ("nomMin", L.CommunauteMin(nom))) : "").ToList();
        var perdus = humains.Where(h => !h.EstVivant && h.CommunauteId == c.Id)
            .SelectMany(h => h.Savoirs.Where(Utile).Select(s => s.Effet)).Distinct().Where(e => !connus.Contains(e))
            .Order().Select(e => L.T($"effet.{e}")).ToList();
        var fragiles = membres.SelectMany(m => m.Savoirs.Where(s => Utile(s) && s.Espece == 0).Select(s => (s.Effet, Membre: m)))
            .DistinctBy(x => (x.Effet, x.Membre.Id)).GroupBy(x => x.Effet).Where(g => g.Count() <= 2 && membres.Count >= 4).OrderBy(g => g.Key)
            .Select(g => new FragileDto(L.T($"effet.{g.Key}"), g.Select(x => new NommeDto(x.Membre.Id, x.Membre.Nom, 0)).ToList())).ToList();
        return new SavoirsCommunauteDto(siennes, Decouvertes.DeCommunaute(membres), appris, perdus, fragiles);
    }


    // ───────────── Territoire, nourriture, santé ─────────────

    private static VieCommunauteDto CalculerVie(ServiceMonde service, Communaute c, List<Humain> humains, List<Humain> membres, long maintenant)
    {
        var (foyers, huttes, _) = service.Monde.Installations();
        bool Proche(int x, int y) => c.Active && Math.Max(Math.Abs(x - c.CentreX), Math.Abs(y - c.CentreY)) <= c.Rayon + 1;
        var malades = membres.Where(h => h.EstMalade(maintenant) && h.Maladie != Maladie.Aucune)
            .Select(h => new PersonneDto(h.Id, h.Nom, Lib.Maladie(h.Maladie))).ToList();
        var mortsMaladie = humains.Count(h => !h.EstVivant && h.CommunauteId == c.Id && h.CauseDeMort == CauseDeMort.Maladie);
        var catastrophes = CatastropheDto.Liste(service, c.Id, 40).Where(x => !x.Phenomene || x.Morts > 0).ToList();
        var captifs = membres.Where(h => h.CaptifDe != 0)
            .Select(h => new PersonneDto(h.Id, h.Nom, h.CaptifDe <= humains.Count ? L.T("captif.par", ("enleve", Enleve(h)), ("nom", humains[h.CaptifDe - 1].Nom)) : Enleve(h))).ToList();
        return new VieCommunauteDto(huttes.Count(h => Proche(h.X, h.Y)), foyers.Count(f => Proche(f.X, f.Y)), malades, mortsMaladie, catastrophes, captifs,
            CalculerFeu(service, c, membres, maintenant, Proche), CalculerRencontres(service, c, humains, membres, maintenant));
    }

    private static RencontresCommunauteDto CalculerRencontres(ServiceMonde service, Communaute c, List<Humain> humains, List<Humain> membres, long maintenant)
    {
        var communautes = service.Monde.Communautes();
        string NomDe(int id) => id >= 1 && id <= communautes.Count ? communautes[id - 1].Nom : "?";
        var specialistes = membres.Where(h => h.Specialite != Ouvrage.Aucun)
            .Select(h => new PersonneDto(h.Id, h.Nom, L.T("specialiste.phrase", ("metier", Lib.Metier(h)), ("n", h.Servis(h.Specialite))))).ToList();
        var rassemblements = service.Monde.Rassemblements().Where(r => r.HoteId == c.Id || r.InviteeId == c.Id).OrderByDescending(r => r.Id).Take(20)
            .Select(r =>
            {
                var autre = r.HoteId == c.Id ? r.InviteeId : r.HoteId;
                var fin = r.FiniLe > 0 ? r.FiniLe : maintenant;
                return new RassemblementDto(L.Date(r.Debut), autre, L.Communaute(NomDe(autre)), r.HoteId == c.Id,
                    Math.Max(1, (int)Math.Round((fin - r.Debut) / (double)Calendrier.SecondesParJour)), r.Actif, r.Participants, r.Cadeaux, r.Appris, r.Couples,
                    r.EvenementId, r.BilanEvenementId);
            }).ToList();
        string Chez(Humain h) => h.VisiteChezId >= 1 && h.VisiteChezId <= humains.Count ? humains[h.VisiteChezId - 1] is var o ? L.T("visite.chez", ("nom", o.Nom), ("de", L.CommunautePhrase(NomDe(o.CommunauteId))), ("deMin", L.CommunauteMin(NomDe(o.CommunauteId)))) : "" : "";
        var enVisite = membres.Where(h => h.VisiteChezId != 0).Select(h => new PersonneDto(h.Id, h.Nom, h.VisiteArriveeLe == 0 ? L.T("visite.enRoute", ("chez", Chez(h))) : Chez(h))).ToList();
        var visiteurs = humains.Where(h => h.EstVivant && h.VisiteChezId >= 1 && h.VisiteChezId <= humains.Count && humains[h.VisiteChezId - 1].CommunauteId == c.Id)
            .Select(h => new PersonneDto(h.Id, h.Nom, L.T("visite.visiteur", ("de", L.CommunautePhrase(NomDe(h.CommunauteId))), ("deMin", L.CommunauteMin(NomDe(h.CommunauteId))), ("nom", humains[h.VisiteChezId - 1].Nom)))).ToList();
        var venus = membres.Where(h => h.VenuDeId != 0).Select(h => new PersonneDto(h.Id, h.Nom, L.T("visite.venu", ("de", L.CommunautePhrase(NomDe(h.VenuDeId))), ("deMin", L.CommunauteMin(NomDe(h.VenuDeId))), ("g", Lib.G(h))))).ToList();
        return new RencontresCommunauteDto(specialistes, rassemblements, enVisite, visiteurs, venus);
    }

    private static FeuReservesDto CalculerFeu(ServiceMonde service, Communaute c, List<Humain> membres, long maintenant, Func<int, int, bool> proche)
    {
        var (braises, reserves) = service.Monde.BraisesEtReserves();
        var adultes = membres.Where(h => !h.EstEnfant(maintenant)).ToList();
        var saventAllumer = adultes.Count(h => h.Savoirs.Exists(s => s.Effet == Effet.Flamme && s.Confiance >= 0.4));
        var porteur = adultes.Where(h => h.PorteurDuFeuDepuis > 0).MaxBy(h => h.FeuxPortes);
        var siennes = reserves.Where(r => r.CommunauteId == c.Id && r.Kg > 0).ToList();
        var lignes = siennes.GroupBy(r => r.Sorte).OrderBy(g => g.Key)
            .Select(g => Lib.Reserve(g.Key, g.Sum(r => r.Kg)) + (g.Count() > 1 ? L.T("reserves.caches", ("n", g.Count())) : "")).ToList();
        var idee = c.IdeeReserve switch
        {
            IdeeReserve.Commune => L.T("reserves.commune"),
            IdeeReserve.ASoi => L.T("reserves.aSoi"),
            _ => null,
        };
        var incendies = c.IncendiesSouvenus.Select(id => CatastropheDto.Trouver(service, id)).OfType<Catastrophe>()
            .Select(x => CatastropheDto.Depuis(x, service.Monde.Communautes(), maintenant)).ToList();
        return new FeuReservesDto(braises.Count(b => proche(b.X, b.Y)), saventAllumer, adultes.Count,
            porteur is null ? null : new NommeDto(porteur.Id, porteur.Nom, porteur.FeuxPortes),
            Math.Round(Comportement.JoursDeVivres(siennes, c.Id, membres.Count), 1), lignes, idee, incendies);
    }

    // ───────────── Foi ─────────────

    private static FoiCommunauteDto CalculerFoi(ServiceMonde service, int id, List<Humain> humains, List<Humain> membres, List<Humain> adultes,
        IReadOnlyList<Communaute> communautes)
    {
        var monde = service.Monde;
        var (religions, lieux, _) = monde.Religions();
        var c = communautes[id - 1];
        var parts = membres.Where(h => h.ReligionId >= 1 && h.ReligionId <= religions.Count).GroupBy(h => h.ReligionId)
            .OrderByDescending(g => g.Count())
            .Select(g => L.T("foi.part", ("nom", religions[g.Key - 1].Nom), ("sens", L.Garde(religions[g.Key - 1].Sens)), ("n", g.Count()), ("pourcent", Math.Round(100.0 * g.Count() / membres.Count))))
            .ToList();
        var sans = membres.Count(h => h.ReligionId == 0);
        if (parts.Count > 0 && sans > 0) parts.Add(L.T("foi.sans", ("n", sans), ("pourcent", Math.Round(100.0 * sans / membres.Count))));
        var noms = communautes.ToDictionary(x => x.Id, x => L.Communaute(x.Nom));
        var porteurs = membres.Where(h => h.Role != RoleReligieux.Aucun && h.ReligionId >= 1 && h.ReligionId <= religions.Count)
            .Select(h => new PorteurDto(h.Id, h.Nom, L.T("foi.porteur", ("role", Role(h)), ("religion", religions[h.ReligionId - 1].Nom)),
                h.Role == RoleReligieux.Prophete ? L.T($"sourceProphete.{h.SourceProphete}") : null, h.Miracles, null, true)).ToList();
        var proches = c.Active
            ? lieux.Where(l => Math.Max(Math.Abs(l.X - c.CentreX), Math.Abs(l.Y - c.CentreY)) <= c.Rayon + 5)
                .Select(l => l.ReligionId >= 1 && l.ReligionId <= religions.Count ? L.T("foi.lieu", ("raison", L.Garde(l.Raison)), ("religion", religions[l.ReligionId - 1].Nom)) : L.Garde(l.Raison)!).ToList()
            : [];
        var ids = membres.Select(h => h.Id).ToHashSet();
        var prieres = Serveur.Religions.Prieres(service, enAttente: false, limite: 1000)
            .Where(p => p.CollectiveId == id || (p.CollectiveId == 0 && ids.Contains(p.Humain))).Take(12).ToList();
        int? Moyenne(Func<Humain, double> f) => adultes.Count == 0 ? null : (int)Math.Round(adultes.Average(f));
        var (gestes, souvenirs) = monde.GestesEnCours();
        var effets = gestes.Where(e => e.CommunauteId == id).Select(e => e.Geste switch
        {
            GesteEntite.Famine => L.T("effetEntite.famine", ("v", Math.Round(100 * e.Force))),
            GesteEntite.Fecondite => L.T("effetEntite.fecondite", ("v", e.Force.ToString("0.0", L.Courante.Nombres))),
            GesteEntite.Benir => L.T("effetEntite.benir", ("v", Math.Round(100 * e.Force))),
            _ => L.T($"gesteEntite.{e.Geste}"),
        } + L.T("effetEntite.fin", ("date", L.Date(e.Fin)))).ToList();
        var recits = souvenirs.Where(s => s.CommunauteId == id).OrderByDescending(s => s.Le).Take(20)
            .Select(s => new SouvenirDto(L.Date(s.Le), L.Garde(s.Texte)!, s.Bienfait, (int)Math.Round(s.Force), s.Faute switch
            {
                Gestes.FauteVol => L.T("faute.vol"),
                Gestes.FauteMeurtre => L.T("faute.meurtre"),
                Gestes.FauteGuerre => L.T("faute.guerre"),
                _ when !s.Bienfait => L.T("faute.inconnue"),
                _ => null,
            }, s.EvenementId)).ToList();
        return new FoiCommunauteDto(Moyenne(h => h.Croyance), parts, porteurs, proches, prieres,
            Moyenne(h => h.Amour), Moyenne(h => h.Crainte), Moyenne(h => h.Revolte), effets, recits, Serveur.Religions.Bilan(service, ids, id));
    }
}
