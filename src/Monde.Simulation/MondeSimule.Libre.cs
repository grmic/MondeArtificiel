using System.Globalization;
using System.Text.Json;
using Monde.Simulation.Humains;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation;

/// <summary>
/// Un changement demandé par l'Entité libre. <see cref="Quoi"/> dit lequel (voir <see cref="Libre.Sortes"/>) ; les autres champs
/// servent selon la sorte.
/// </summary>
/// <param name="Texte">Cause d'une mort, nom d'une maladie ou d'un savoir, pensée à confier, état d'une relation…</param>
/// <param name="Genre">Pour une pensée : sa sorte (Souvenir, Reve, Espoir, Peur…).</param>
/// <param name="Rayon">Pour les plantes et le gibier : rayon en cases autour de (X, Y).</param>
/// <param name="Nom">Nom d'une plante ou d'une bête inventée ; forme d'un mot donné.</param>
/// <param name="Forme">Forme d'une plante ou d'une bête inventée (voir <see cref="Inventions"/>).</param>
/// <param name="Aliment">Ce qu'une plante inventée donne à manger.</param>
/// <param name="Proprietes">Vertus d'une plante, traits d'une bête inventée.</param>
/// <param name="Geste">Geste d'un savoir nouveau (avec les matières <see cref="A"/> et <see cref="B"/>).</param>
public sealed record ChangementLibre(string Quoi, int Humain = 0, int Communaute = 0, int Autre = 0, double? Valeur = null, string? Texte = null,
    string? Genre = null, int X = -1, int Y = -1, int Rayon = 0, string? Nom = null, string? Forme = null, string? Aliment = null,
    Dictionary<string, double>? Proprietes = null, string? Geste = null, string? A = null, string? B = null);

/// <summary>Un décret demandé : une loi (<see cref="LoiDecret"/>, par son nom), un facteur, une durée en jours (0 : pour toujours).</summary>
public sealed record DecretLibre(string Loi, double Facteur, int Communaute = 0, double Jours = 0, string Raison = "");

/// <summary>Un acte de l'Entité libre : son récit, s'il se voit, ses témoins, ses changements et ses décrets.</summary>
public sealed record ActeLibre(string Recit, bool Visible, bool Bienfait, IReadOnlyList<int> Temoins, IReadOnlyList<ChangementLibre> Changements,
    IReadOnlyList<DecretLibre> Decrets, long ActeId = 0);

/// <summary>
/// Un changement fait : de quoi le défaire. <see cref="Avant"/> et <see cref="Apres"/> : les valeurs des champs touchés ;
/// <see cref="Extra"/> : ce qui ne tient pas en nombres (savoirs retirés, pensée confiée, cases de la nature…), en JSON.
/// </summary>
public sealed record ChangementFait(string Quoi, int Humain, int Communaute, int Autre, Dictionary<string, double> Avant, Dictionary<string, double> Apres,
    string Extra = "");

/// <summary>Ce qu'a donné un acte libre : ce qui s'est passé, en récits ; de quoi le défaire ; les décrets pris ; les témoins.</summary>
public sealed record ResultatLibre(IReadOnlyList<Recit> Lignes, IReadOnlyList<ChangementFait> Faits, IReadOnlyList<Decret> Decrets, int Savent, long Annonce);

/// <summary>Sortes de changements et bornes des valeurs : ce que l'Entité libre peut changer (0.35.2 ; inventions en 0.35.3).</summary>
public static class Libre
{
    /// <summary>Champs chiffrés d'un humain : bornes et accès.</summary>
    internal static readonly Dictionary<string, (double Min, double Max, Func<Humain, double> Lire, Action<Humain, double> Ecrire)> Nombres = new()
    {
        ["sante"] = (1, 100, h => h.Sante, (h, v) => h.Sante = v),
        ["faim"] = (0, 100, h => h.Faim, (h, v) => h.Faim = v),
        ["soif"] = (0, 100, h => h.Soif, (h, v) => h.Soif = v),
        ["froid"] = (0, 100, h => h.Froid, (h, v) => h.Froid = v),
        ["fatigue"] = (0, 100, h => h.Fatigue, (h, v) => h.Fatigue = v),
        ["reserves"] = (0, 100, h => h.Reserves, (h, v) => h.Reserves = v),
        ["humeur"] = (0, 100, h => h.Humeur, (h, v) => h.Humeur = v),
        ["peur"] = (0, 100, h => h.Peur, (h, v) => h.Peur = v),
        ["chagrin"] = (0, 100, h => h.Chagrin, (h, v) => h.Chagrin = v),
        ["colere"] = (0, 100, h => h.Colere, (h, v) => h.Colere = v),
        ["desir"] = (0, 100, h => h.Desir, (h, v) => h.Desir = v),
        ["croyance"] = (0, 100, h => h.Croyance, (h, v) => h.Croyance = v),
        ["amour"] = (0, 100, h => h.Amour, (h, v) => h.Amour = v),
        ["crainte"] = (0, 100, h => h.Crainte, (h, v) => h.Crainte = v),
        ["revolte"] = (0, 100, h => h.Revolte, (h, v) => h.Revolte = v),
        ["karma"] = (-100, 100, h => h.Karma, (h, v) => h.Karma = v),
        ["ferveur"] = (0, 1, h => h.Ferveur, (h, v) => h.Ferveur = v),
        ["ouverture"] = (0, 1, h => h.Personnalite.Ouverture, (h, v) => h.Personnalite = h.Personnalite with { Ouverture = v }),
        ["conscience"] = (0, 1, h => h.Personnalite.Conscience, (h, v) => h.Personnalite = h.Personnalite with { Conscience = v }),
        ["extraversion"] = (0, 1, h => h.Personnalite.Extraversion, (h, v) => h.Personnalite = h.Personnalite with { Extraversion = v }),
        ["agreabilite"] = (0, 1, h => h.Personnalite.Agreabilite, (h, v) => h.Personnalite = h.Personnalite with { Agreabilite = v }),
        ["nevrosisme"] = (0, 1, h => h.Personnalite.Nevrosisme, (h, v) => h.Personnalite = h.Personnalite with { Nevrosisme = v }),
        ["transcendance"] = (0, 1, h => h.Personnalite.Transcendance, (h, v) => h.Personnalite = h.Personnalite with { Transcendance = v }),
        ["intelligence"] = (40, 200, h => h.Personnalite.Intelligence.Generale, (h, v) => Intel(h, h.Personnalite.Intelligence with { Generale = v })),
        ["verbale"] = (40, 200, h => h.Personnalite.Intelligence.Verbale, (h, v) => Intel(h, h.Personnalite.Intelligence with { Verbale = v })),
        ["spatiale"] = (40, 200, h => h.Personnalite.Intelligence.Spatiale, (h, v) => Intel(h, h.Personnalite.Intelligence with { Spatiale = v })),
        ["manuelle"] = (40, 200, h => h.Personnalite.Intelligence.Manuelle, (h, v) => Intel(h, h.Personnalite.Intelligence with { Manuelle = v })),
        ["memoire"] = (40, 200, h => h.Personnalite.Intelligence.Memoire, (h, v) => Intel(h, h.Personnalite.Intelligence with { Memoire = v })),
        ["artistique"] = (40, 200, h => h.Personnalite.Intelligence.Artistique, (h, v) => Intel(h, h.Personnalite.Intelligence with { Artistique = v })),
    };

    private static void Intel(Humain h, Intelligence i) => h.Personnalite = h.Personnalite with { Intelligence = i };

    /// <summary>Champs internes notés pour défaire les changements qui ne sont pas de simples nombres.</summary>
    internal static readonly Dictionary<string, (Func<Humain, double> Lire, Action<Humain, double> Ecrire)> Internes = new()
    {
        ["naissance"] = (h => h.Naissance, (h, v) => h.Naissance = (long)v),
        ["maladeJusqua"] = (h => h.MaladeJusqua, (h, v) => h.MaladeJusqua = (long)v),
        ["maladie"] = (h => (double)h.Maladie, (h, v) => h.Maladie = (Maladie)(byte)v),
        ["infection"] = (h => h.InfectionId, (h, v) => h.InfectionId = (int)v),
        ["infecteLe"] = (h => h.InfecteLe, (h, v) => h.InfecteLe = (long)v),
        ["symptomesLe"] = (h => h.SymptomesLe, (h, v) => h.SymptomesLe = (long)v),
        ["guerisonLe"] = (h => h.GuerisonLe, (h, v) => h.GuerisonLe = (long)v),
        ["infectionEvenement"] = (h => h.InfectionEvenementId, (h, v) => h.InfectionEvenementId = (long)v),
        ["plaieLe"] = (h => h.PlaieLe, (h, v) => h.PlaieLe = (long)v),
        ["plaieSoin"] = (h => h.PlaieSoin, (h, v) => h.PlaieSoin = v),
        ["blessureGrave"] = (h => h.BlessureGrave, (h, v) => h.BlessureGrave = v),
        ["blessureOrigine"] = (h => (double)h.BlessureOrigine, (h, v) => h.BlessureOrigine = (OrigineBlessure)(byte)v),
        ["x"] = (h => h.X, (h, v) => h.X = (int)v),
        ["y"] = (h => h.Y, (h, v) => h.Y = (int)v),
        ["communaute"] = (h => h.CommunauteId, (h, v) => h.CommunauteId = (int)v),
        ["mortLe"] = (h => h.MortLe ?? double.NaN, (h, v) => h.MortLe = double.IsNaN(v) ? null : (long)v),
        ["cause"] = (h => (double)h.CauseDeMort, (h, v) => h.CauseDeMort = (CauseDeMort)(byte)v),
        ["activite"] = (h => (double)h.Activite, (h, v) => h.Activite = (Activite)(byte)v),
        ["enceinteDepuis"] = (h => h.EnceinteDepuis ?? double.NaN, (h, v) => h.EnceinteDepuis = double.IsNaN(v) ? null : (long)v),
        ["terme"] = (h => h.TermeGrossesse ?? double.NaN, (h, v) => h.TermeGrossesse = double.IsNaN(v) ? null : (long)v),
        ["pereEnfant"] = (h => h.PereEnfantId, (h, v) => h.PereEnfantId = (int)v),
    };

    /// <summary>Sortes qui touchent un humain sans être un simple nombre.</summary>
    public static readonly string[] SortesHumain =
        ["age", "mourir", "ressusciter", "guerir", "maladie", "blessure", "lien", "communaute", "deplacer", "pensee", "oublier", "savoir", "oublier_savoir", "enfant"];

    /// <summary>Sortes qui touchent une communauté ou la nature.</summary>
    public static readonly string[] SortesMonde = ["relation", "plantes", "gibier", "pluie"];

    /// <summary>Inventions (0.35.3) : ce que le monde n'avait jamais connu.</summary>
    public static readonly string[] SortesInvention = ["plante_nouvelle", "bete_nouvelle", "savoir_nouveau", "mot"];

    /// <summary>Toutes les sortes de changements.</summary>
    public static IEnumerable<string> Sortes => Nombres.Keys.Concat(SortesHumain).Concat(SortesMonde).Concat(SortesInvention);

    /// <summary>Bornes d'un champ chiffré (pour la consigne du LLM).</summary>
    public static (double Min, double Max)? Bornes(string quoi) => Nombres.TryGetValue(quoi, out var n) ? (n.Min, n.Max) : null;

    /// <summary>Valeurs chiffrées d'un humain (pour ce que lit le LLM).</summary>
    public static IEnumerable<(string Quoi, double Valeur)> Valeurs(Humain h) => Nombres.Select(n => (n.Key, n.Value.Lire(h)));

    /// <summary>États d'une relation entre deux communautés qu'on peut imposer.</summary>
    public static readonly string[] Relations = ["guerre", "paix", "alliance", "hostiles", "indifferentes"];

    /// <summary>Rayon maximal d'un changement de la nature, en cases.</summary>
    public const int RayonMax = 20;

    /// <summary>Plus de changements par acte : au-delà, l'acte est refusé.</summary>
    public const int ChangementsMax = 40;

    /// <summary>Plus de décrets par acte.</summary>
    public const int DecretsMax = 4;

    internal static string Nombre(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
}

public sealed partial class MondeSimule
{
    private readonly Decrets _decrets;

    /// <summary>Les décrets de l'Entité libre (tous, levés et finis compris).</summary>
    public List<Decret> Decrets()
    {
        lock (_verrou) return [.. _decrets.Liste];
    }

    /// <summary>Lève un décret ; rend le récit de ce qui s'est passé, ou null s'il n'était plus en vigueur.</summary>
    public Recit? LeverDecret(int id)
    {
        lock (_verrou)
        {
            if (_decrets.Lever(id, _secondes) is not { } d) return null;
            var r = new Recit("libre.decret.leve", ("loi", Recit.Seul($"loi.{d.Loi}")), ("f", new Decimale(d.Facteur, 2)));
            Ajouter(new EvenementJournal(_secondes, TypeEvenement.Geste, 0, 0, r, Origine.Entite) { Significatif = true, CommunauteId = d.CommunauteId });
            return r;
        }
    }

    /// <summary>
    /// L'Entité libre agit : chaque changement est vérifié d'abord (numéros, bornes, carte) ; s'il y a une faute, rien n'est fait
    /// et <see cref="ArgumentException"/> en dit la raison. Sinon tout est appliqué tel quel, noté au journal (le récit, puis une
    /// ligne par changement), et les témoins d'un acte visible en gardent un prodige.
    /// </summary>
    public ResultatLibre Libre(ActeLibre acte)
    {
        lock (_verrou)
        {
            if (acte.Changements.Count == 0 && acte.Decrets.Count == 0) throw Faute("vide");
            if (acte.Changements.Count > Simulation.Libre.ChangementsMax) throw Faute("tropDeChangements");
            if (acte.Decrets.Count > Simulation.Libre.DecretsMax) throw Faute("tropDeDecrets");
            foreach (var c in acte.Changements) Verifier(c);
            foreach (var d in acte.Decrets)
            {
                VerifierDecret(d);
                if (d.Communaute != 0 && !CommunauteDe(d.Communaute, "decret").Active) throw Faute($"dispersee|decret|{d.Communaute}");
            }

            var maintenant = _secondes;
            var touches = acte.Changements.Select(c => c.Humain).Where(id => id >= 1 && id <= _humains.Count).Distinct().Select(id => _humains[id - 1]).ToList();
            var principal = touches.FirstOrDefault();
            var communaute = principal?.CommunauteId ?? acte.Changements.Select(c => c.Communaute).FirstOrDefault(c => c > 0);
            var recit = acte.Recit.Trim();
            if (recit.Length > 2000) recit = recit[..2000];
            var annonce = _numeros.Suivant();
            Ajouter(new EvenementJournal(maintenant, TypeEvenement.Geste, principal?.Id ?? 0, 0, new Recit("libre.recit", ("t", recit)), Origine.Entite)
            {
                Id = annonce, Significatif = true, CommunauteId = communaute,
            });
            // Les témoins sont cherchés avant les changements (un mort, un humain déplacé n'est plus là où il était).
            var temoins = acte.Visible ? Temoins(acte, touches, maintenant) : [];
            var lignes = new List<Recit>();
            var faits = new List<ChangementFait>();
            foreach (var c in acte.Changements)
            {
                var (fait, ligne) = Appliquer(c, annonce, maintenant);
                faits.Add(fait);
                lignes.Add(ligne);
            }
            var decrets = new List<Decret>();
            foreach (var d in acte.Decrets)
            {
                var loi = Enum.Parse<LoiDecret>(d.Loi, ignoreCase: true);
                var fin = d.Jours > 0 ? maintenant + (long)(d.Jours * Calendrier.SecondesParJour) : 0;
                var decret = _decrets.Ajouter(loi, d.Communaute, d.Facteur, maintenant, fin, (d.Raison ?? "").Trim(), acte.ActeId);
                decrets.Add(decret);
                var r = new Recit(fin == 0 ? "libre.decret" : "libre.decret.duree", ("loi", Recit.Seul($"loi.{loi}")), ("f", new Decimale(decret.Facteur, 2)),
                    ("ou", d.Communaute > 0 ? _communautes[d.Communaute - 1] : Recit.Seul("libre.partout")), ("j", (long)Math.Round(d.Jours)), ("raison", decret.Raison));
                Ajouter(new EvenementJournal(maintenant, TypeEvenement.Geste, principal?.Id ?? 0, 0, r, Origine.Entite) { CauseId = annonce, CommunauteId = d.Communaute });
                lignes.Add(r);
            }
            var savent = 0;
            if (acte.Visible && temoins.Count > 0)
                savent = _comportement.Prodige(temoins, principal, communaute, acte.Bienfait, recit, annonce, _humains, maintenant);
            _effetsConnus = EffetsConnus();
            VerserJournal();
            return new ResultatLibre(lignes, faits, decrets, savent, annonce);
        }
    }

    /// <summary>
    /// Défait un acte libre : les valeurs d'avant sont remises (santé, liens, savoirs, terrain…), dans l'ordre inverse ; les décrets
    /// sont levés. Ce que les humains ont fait depuis reste. Rend ce qui a été défait, en récits.
    /// </summary>
    public List<Recit> Defaire(IReadOnlyList<ChangementFait> faits, IReadOnlyList<int> decrets, long cause)
    {
        lock (_verrou)
        {
            var maintenant = _secondes;
            var lignes = new List<Recit>();
            var annonce = _numeros.Suivant();
            Ajouter(new EvenementJournal(maintenant, TypeEvenement.Geste, faits.Select(f => f.Humain).FirstOrDefault(h => h > 0), 0,
                Recit.Seul("libre.defait"), Origine.Entite) { Id = annonce, CauseId = cause, Significatif = true });
            foreach (var f in faits.Reverse())
            {
                if (Retablir(f, maintenant) is { } r)
                {
                    lignes.Add(r);
                    Ajouter(new EvenementJournal(maintenant, TypeEvenement.Geste, f.Humain, f.Autre, r, Origine.Entite) { CauseId = annonce, CommunauteId = f.Communaute });
                }
            }
            foreach (var id in decrets)
            {
                if (_decrets.Lever(id, maintenant) is not { } d) continue;
                var r = new Recit("libre.decret.leve", ("loi", Recit.Seul($"loi.{d.Loi}")), ("f", new Decimale(d.Facteur, 2)));
                lignes.Add(r);
                Ajouter(new EvenementJournal(maintenant, TypeEvenement.Geste, 0, 0, r, Origine.Entite) { CauseId = annonce, CommunauteId = d.CommunauteId });
            }
            _effetsConnus = EffetsConnus();
            VerserJournal();
            return lignes;
        }
    }

    // ───────────── Vérifier ─────────────

    /// <summary>Une faute « libre.erreur.code|a|b… », mise en mots par le serveur.</summary>
    private static ArgumentException Faute(string code) => new($"libre.erreur.{code}");

    private Humain HumainDe(int id, string quoi)
    {
        if (id < 1 || id > _humains.Count) throw Faute($"humain|{quoi}|{id}");
        return _humains[id - 1];
    }

    private Communaute CommunauteDe(int id, string quoi)
    {
        if (id < 1 || id > _communautes.Count) throw Faute($"communaute|{quoi}|{id}");
        return _communautes[id - 1];
    }

    private static double Borne(double? v, double min, double max, string quoi)
    {
        if (v is not { } x || !double.IsFinite(x) || x < min || x > max)
            throw Faute($"valeur|{quoi}|{(v is { } y ? Simulation.Libre.Nombre(y) : "?")}|{Simulation.Libre.Nombre(min)}|{Simulation.Libre.Nombre(max)}");
        return x;
    }

    private void SurLaCarte(int x, int y, string quoi)
    {
        if (x < 0 || y < 0 || x >= Carte.Largeur || y >= Carte.Hauteur) throw Faute($"lieu|{quoi}|{x}|{y}");
    }

    private void Verifier(ChangementLibre c)
    {
        var quoi = c.Quoi ?? "";
        if (Simulation.Libre.Nombres.TryGetValue(quoi, out var n))
        {
            if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
            Borne(c.Valeur, n.Min, n.Max, quoi);
            return;
        }
        switch (quoi)
        {
            case "age":
                HumainDe(c.Humain, quoi);
                Borne(c.Valeur, 0, 120, quoi);
                return;
            case "mourir":
            case "guerir":
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (quoi == "mourir" && c.Texte is { Length: > 0 } t && !Enum.TryParse<CauseDeMort>(t, true, out _)) throw Faute($"texte|{quoi}|{t}");
                return;
            case "ressusciter":
                if (HumainDe(c.Humain, quoi).EstVivant) throw Faute($"vivant|{quoi}|{c.Humain}");
                return;
            case "maladie":
            {
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (!Enum.TryParse<Maladie>(c.Texte, true, out var m) || m == Maladie.Aucune || !Enum.IsDefined(m)) throw Faute($"texte|{quoi}|{c.Texte}");
                if (c.Valeur is not null) Borne(c.Valeur, 1, 3650, quoi);
                return;
            }
            case "blessure":
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                Borne(c.Valeur, 5, 99, quoi);
                return;
            case "lien":
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                HumainDe(c.Autre, quoi);
                if (c.Autre == c.Humain) throw Faute($"autre|{quoi}|{c.Autre}");
                Borne(c.Valeur, -100, 100, quoi);
                return;
            case "communaute":
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (c.Communaute != 0 && !CommunauteDe(c.Communaute, quoi).Active) throw Faute($"dispersee|{quoi}|{c.Communaute}");
                return;
            case "deplacer":
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                SurLaCarte(c.X, c.Y, quoi);
                if (!Climat.EstPraticable(Carte.BiomeEn(c.X, c.Y))) throw Faute($"eau|{quoi}|{c.X}|{c.Y}");
                return;
            case "pensee":
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (string.IsNullOrWhiteSpace(c.Texte)) throw Faute($"texte|{quoi}|");
                if (c.Genre is { Length: > 0 } g && !Enum.TryParse<TypeIntime>(g, true, out _)) throw Faute($"texte|{quoi}|{g}");
                if (c.Valeur is not null) Borne(c.Valeur, 5, 100, quoi);
                return;
            case "oublier":
            {
                var h = HumainDe(c.Humain, quoi);
                if (TrouverIntime(h, c.Texte) < 0) throw Faute($"texte|{quoi}|{c.Texte}");
                return;
            }
            case "savoir":
            {
                if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (!Enum.TryParse<Effet>(c.Texte, true, out var e) || !Enum.IsDefined(e)) throw Faute($"texte|{quoi}|{c.Texte}");
                if (ModeleDe(e) is null) throw Faute($"inconnu|{quoi}|{c.Texte}");
                return;
            }
            case "oublier_savoir":
            {
                var h = HumainDe(c.Humain, quoi);
                if (!Enum.TryParse<Effet>(c.Texte, true, out var e) || !h.Savoirs.Exists(s => s.Effet == e)) throw Faute($"texte|{quoi}|{c.Texte}");
                return;
            }
            case "enfant":
            {
                var f = HumainDe(c.Humain, quoi);
                if (!f.EstVivant || f.Sexe != Sexe.Femme || f.EstEnfant(_secondes) || f.EstEnceinte) throw Faute($"enfant|{quoi}|{c.Humain}");
                if (c.Autre != 0 && HumainDe(c.Autre, quoi).Sexe != Sexe.Homme) throw Faute($"autre|{quoi}|{c.Autre}");
                return;
            }
            case "relation":
                if (!CommunauteDe(c.Communaute, quoi).Active || !CommunauteDe(c.Autre, quoi).Active || c.Communaute == c.Autre)
                    throw Faute($"dispersee|{quoi}|{c.Autre}");
                if (Array.IndexOf(Simulation.Libre.Relations, c.Texte) < 0) throw Faute($"texte|{quoi}|{c.Texte}");
                return;
            case "plantes":
            case "gibier":
                SurLaCarte(c.X, c.Y, quoi);
                Borne(c.Rayon, 0, Simulation.Libre.RayonMax, quoi);
                Borne(c.Valeur, 0, 10, quoi);
                return;
            case "pluie":
                SurLaCarte(c.X, c.Y, quoi);
                Borne(c.Valeur, 0.1, 3, quoi);
                return;
            default:
                if (!VerifierInvention(c)) throw Faute($"sorte|{quoi}");
                return;
        }
    }

    private static void VerifierDecret(DecretLibre d)
    {
        if (!Enum.TryParse<LoiDecret>(d.Loi, true, out var l) || !Enum.IsDefined(l)) throw Faute($"loi|{d.Loi}");
        Borne(d.Facteur, Simulation.Decrets.FacteurMin, Simulation.Decrets.FacteurMax, "decret");
        Borne(d.Jours, 0, 365_000, "decret");
    }

    /// <summary>Le savoir qui sert de modèle pour donner un effet : celui d'un vivant d'abord, le plus sûr.</summary>
    private Savoir? ModeleDe(Effet e) =>
        _humains.Where(h => h.EstVivant).SelectMany(h => h.Savoirs).Where(s => s.Effet == e).MaxBy(s => s.Confiance)
        ?? _humains.SelectMany(h => h.Savoirs).Where(s => s.Effet == e).MaxBy(s => s.Confiance);

    /// <summary>Une chose du jardin secret désignée par « secondes|clé » (ce que lit le LLM, numéroté J1, J2…).</summary>
    private static int TrouverIntime(Humain h, string? reference)
    {
        if (reference is null) return -1;
        var i = reference.IndexOf('|');
        if (i < 0 || !long.TryParse(reference[..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return -1;
        var cle = reference[(i + 1)..];
        return h.Memoire.Jardin.FindIndex(x => x.Secondes == s && x.Cle == cle);
    }

    // ───────────── Appliquer ─────────────

    private List<Humain> Temoins(ActeLibre acte, List<Humain> touches, long maintenant)
    {
        var ids = new HashSet<int>();
        foreach (var id in acte.Temoins) if (id >= 1 && id <= _humains.Count && _humains[id - 1].EstVivant) ids.Add(id);
        if (ids.Count == 0)
        {
            // Sans témoins nommés : ceux qui sont touchés, et ceux qui sont auprès d'eux ou du lieu.
            var lieux = touches.Where(h => h.EstVivant).Select(h => (h.X, h.Y, R: 1)).Concat(acte.Changements.Where(c => c.X >= 0).Select(c => (c.X, c.Y, R: Math.Max(1, c.Rayon)))).ToList();
            foreach (var h in touches) if (h.EstVivant) ids.Add(h.Id);
            foreach (var o in _humains.Vivants)
                if (o.EstVivant && lieux.Any(l => Math.Max(Math.Abs(o.X - l.X), Math.Abs(o.Y - l.Y)) <= l.R)) ids.Add(o.Id);
        }
        return ids.Order().Select(id => _humains[id - 1]).ToList();
    }

    private static Dictionary<string, double> Noter(Humain h, params string[] champs) =>
        champs.ToDictionary(c => c, c => Simulation.Libre.Nombres.TryGetValue(c, out var n) ? n.Lire(h) : Simulation.Libre.Internes[c].Lire(h));

    private (ChangementFait Fait, Recit Ligne) Appliquer(ChangementLibre c, long annonce, long maintenant)
    {
        var quoi = c.Quoi;
        Recit Ecrire(Recit r, Humain? h = null, Humain? autre = null, int communaute = 0, int autreCommunaute = 0)
        {
            var e = new EvenementJournal(maintenant, TypeEvenement.Geste, h?.Id ?? 0, autre?.Id ?? 0, r, Origine.Entite)
            {
                CauseId = annonce, CommunauteId = communaute != 0 ? communaute : h?.CommunauteId ?? 0, AutreCommunauteId = autreCommunaute,
            };
            Ajouter(e);
            return r;
        }
        if (Simulation.Libre.Nombres.TryGetValue(quoi, out var n))
        {
            var h = _humains[c.Humain - 1];
            var avant = Noter(h, quoi);
            n.Ecrire(h, c.Valeur!.Value);
            var r = Ecrire(new Recit("libre.ch.nombre", ("h", h), ("q", Recit.Seul($"libre.q.{quoi}")), ("avant", Arrondi(avant[quoi], n.Max)), ("apres", Arrondi(c.Valeur.Value, n.Max))), h);
            return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, quoi)), r);
        }
        switch (quoi)
        {
            case "age":
            {
                var h = _humains[c.Humain - 1];
                var avant = Noter(h, "naissance");
                var ancien = h.AgeEnAnnees(maintenant);
                h.Naissance = maintenant - (long)(c.Valeur!.Value * Calendrier.SecondesParAn);
                var r = Ecrire(new Recit("libre.ch.age", ("h", h), ("avant", new Decimale(ancien, 0)), ("apres", new Decimale(c.Valeur.Value, 0))), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, "naissance")), r);
            }
            case "mourir":
            {
                var h = _humains[c.Humain - 1];
                var champs = new[] { "sante", "mortLe", "cause", "activite", "enceinteDepuis", "terme", "pereEnfant" };
                var avant = Noter(h, champs);
                var cause = c.Texte is { Length: > 0 } t ? Enum.Parse<CauseDeMort>(t, true) : CauseDeMort.MortSubite;
                var r = new Recit("libre.ch.mourir", ("h", h), ("age", (long)Math.Floor(h.AgeEnAnnees(maintenant))), ("cause", Libelles.CauseR(cause)));
                _comportement.TuerLibre(h, maintenant, cause, r, annonce);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, champs)), r);
            }
            case "ressusciter":
            {
                var h = _humains[c.Humain - 1];
                var champs = new[] { "sante", "mortLe", "cause", "activite", "faim", "soif", "froid", "fatigue" };
                var avant = Noter(h, champs);
                h.MortLe = null;
                h.CauseDeMort = CauseDeMort.Aucune;
                h.Activite = Activite.Repos;
                (h.Sante, h.Faim, h.Soif, h.Froid, h.Fatigue) = (Math.Max(60, c.Valeur ?? 60), 20, 20, 0, 20);
                _humains.Revenir(h);
                _noms.Add(h.Nom);
                _eteint = false;
                var r = Ecrire(new Recit("libre.ch.ressusciter", ("h", h)), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, champs)), r);
            }
            case "guerir":
            {
                var h = _humains[c.Humain - 1];
                var champs = new[] { "sante", "maladeJusqua", "maladie", "infection", "infecteLe", "symptomesLe", "guerisonLe", "infectionEvenement", "plaieLe" };
                var avant = Noter(h, champs);
                _comportement.GuerirLibre(h, maintenant);
                var r = Ecrire(new Recit("libre.ch.guerir", ("h", h)), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, champs)), r);
            }
            case "maladie":
            {
                var h = _humains[c.Humain - 1];
                var avant = Noter(h, "maladie", "maladeJusqua");
                var m = Enum.Parse<Maladie>(c.Texte!, true);
                h.Maladie = m;
                h.MaladeJusqua = maintenant + (long)((c.Valeur ?? 7) * Calendrier.SecondesParJour);
                var r = Ecrire(new Recit("libre.ch.maladie", ("h", h), ("m", Recit.Seul($"maladie.{m}")), ("j", (long)Math.Round(c.Valeur ?? 7))), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, "maladie", "maladeJusqua")), r);
            }
            case "blessure":
            {
                var h = _humains[c.Humain - 1];
                var champs = new[] { "sante", "plaieLe", "plaieSoin", "blessureGrave", "blessureOrigine" };
                var avant = Noter(h, champs);
                h.Sante = Math.Max(1, h.Sante - c.Valeur!.Value);
                Medecine.Blesser(h, c.Valeur.Value, maintenant, OrigineBlessure.Coups);
                var r = Ecrire(new Recit("libre.ch.blessure", ("h", h), ("n", (long)Math.Round(c.Valeur.Value))), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, champs)), r);
            }
            case "lien":
            {
                var h = _humains[c.Humain - 1];
                var o = _humains[c.Autre - 1];
                var ancien = h.LienAvec(o.Id);
                var v = c.Valeur!.Value;
                var nouveau = ancien is null ? new Lien(o.Id, 40, v, maintenant) : ancien with { Affection = v, Derniere = maintenant };
                var i = h.Liens.FindIndex(l => l.Id == o.Id);
                if (i >= 0) h.Liens[i] = nouveau;
                else h.Liens.Add(nouveau);
                var r = Ecrire(new Recit("libre.ch.lien", ("h", h), ("o", o), ("avant", (long)Math.Round(ancien?.Affection ?? 0)), ("apres", (long)Math.Round(v))), h, o);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, o.Id, [], [], ancien is null ? "" : JsonSerializer.Serialize(ancien)), r);
            }
            case "communaute":
            {
                var h = _humains[c.Humain - 1];
                var champs = new[] { "communaute", "x", "y" };
                var avant = Noter(h, champs);
                var ancienne = h.CommunauteId;
                h.CommunauteId = c.Communaute;
                if (c.Communaute != 0 && _communautes[c.Communaute - 1].FigureId is var f and > 0 && f <= _humains.Count && _humains[f - 1].EstVivant && f != h.Id)
                    (h.X, h.Y) = (_humains[f - 1].X, _humains[f - 1].Y);
                object vers = c.Communaute == 0 ? Recit.Seul("libre.seul") : _communautes[c.Communaute - 1];
                object de = ancienne == 0 ? Recit.Seul("libre.seul") : _communautes[ancienne - 1];
                var r = Ecrire(new Recit("libre.ch.communaute", ("h", h), ("de", de), ("vers", vers)), h, communaute: c.Communaute != 0 ? c.Communaute : ancienne);
                return (new ChangementFait(quoi, h.Id, c.Communaute, ancienne, avant, Noter(h, champs)), r);
            }
            case "deplacer":
            {
                var h = _humains[c.Humain - 1];
                var avant = Noter(h, "x", "y");
                (h.X, h.Y, h.CibleX, h.CibleY, h.Chemin) = (c.X, c.Y, -1, -1, null);
                var r = Ecrire(new Recit("libre.ch.deplacer", ("h", h), ("ou", Lieux.DirectionR(Carte, c.X, c.Y)), ("x", c.X), ("y", c.Y)), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, avant, Noter(h, "x", "y")), r);
            }
            case "pensee":
            {
                var h = _humains[c.Humain - 1];
                var type = c.Genre is { Length: > 0 } g ? Enum.Parse<TypeIntime>(g, true) : TypeIntime.Souvenir;
                var texte = c.Texte!.Trim();
                if (texte.Length > 500) texte = texte[..500];
                var cle = $"libre:{annonce}:{h.Id}:{h.Memoire.Jardin.Count}";
                Jardin.Confier(h, type, 0, texte, c.Valeur ?? 60, annonce, maintenant, cle);
                var r = Ecrire(new Recit("libre.ch.pensee", ("h", h), ("t", texte)), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, [], [], cle), r);
            }
            case "oublier":
            {
                var h = _humains[c.Humain - 1];
                var i = TrouverIntime(h, c.Texte);
                var chose = h.Memoire.Jardin[i];
                h.Memoire.Jardin.RemoveAt(i);
                var r = Ecrire(new Recit("libre.ch.oublier", ("h", h), ("t", (object?)chose.Recit ?? chose.Brut ?? "")), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, [], [], JsonSerializer.Serialize(IntimeGarde.De(chose))), r);
            }
            case "savoir":
            {
                var h = _humains[c.Humain - 1];
                var e = Enum.Parse<Effet>(c.Texte!, true);
                var modele = ModeleDe(e)!;
                var donne = modele with
                {
                    Essais = 1, Succes = 1, AppriLe = maintenant, UtiliseLe = maintenant, Origine = OrigineSavoir.Entite, MaitreId = 0, EvenementId = annonce,
                };
                var deja = h.Savoirs.Exists(s => s.Meme(donne.Geste, donne.A, donne.B, donne.Effet, donne.Espece, donne.Symptome) && s.Pieces == donne.Pieces);
                if (!deja) h.Savoirs.Add(donne);
                var r = Ecrire(new Recit(deja ? "libre.ch.savoir.deja" : "libre.ch.savoir", ("h", h), ("e", Recit.Seul($"effet.{e}"))), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, [], [], deja ? "" : JsonSerializer.Serialize(SavoirGarde.De(donne))), r);
            }
            case "oublier_savoir":
            {
                var h = _humains[c.Humain - 1];
                var e = Enum.Parse<Effet>(c.Texte!, true);
                var retires = h.Savoirs.Where(s => s.Effet == e).ToList();
                h.Savoirs.RemoveAll(s => s.Effet == e);
                var r = Ecrire(new Recit("libre.ch.oublierSavoir", ("h", h), ("e", Recit.Seul($"effet.{e}"))), h);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, [], [], JsonSerializer.Serialize(retires.Select(s => SavoirGarde.De(s)).ToList())), r);
            }
            case "enfant":
            {
                var f = _humains[c.Humain - 1];
                var champs = new[] { "enceinteDepuis", "terme", "pereEnfant" };
                var avant = Noter(f, champs);
                var pere = c.Autre != 0 ? c.Autre : f.PartenaireId;
                // Une grossesse donnée : l'enfant naît selon les règles ordinaires, dans une saison.
                f.EnceinteDepuis = maintenant;
                f.TermeGrossesse = maintenant + (long)(Calendrier.JoursParSaison * Calendrier.SecondesParJour);
                f.PereEnfantId = pere;
                var r = pere > 0
                    ? Ecrire(new Recit("libre.ch.enfant", ("h", f), ("p", _humains[pere - 1])), f, _humains[pere - 1])
                    : Ecrire(new Recit("libre.ch.enfantSeule", ("h", f)), f);
                return (new ChangementFait(quoi, f.Id, f.CommunauteId, pere, avant, Noter(f, champs)), r);
            }
            case "relation":
            {
                var (a, b) = (_communautes[c.Communaute - 1], _communautes[c.Autre - 1]);
                var avant = _social.ImposerRelation(a, b, c.Texte!, annonce, _humains, maintenant);
                var r = Ecrire(new Recit("libre.ch.relation", ("a", a), ("b", b), ("etat", Recit.Seul($"libre.rel.{c.Texte}"))), null, null, a.Id, b.Id);
                return (new ChangementFait(quoi, 0, a.Id, b.Id, new() { ["etat"] = (double)avant.Etat, ["guerre"] = avant.GuerreCreee }, [], c.Texte!), r);
            }
            case "plantes":
            case "gibier":
            {
                var cases = quoi == "plantes" ? _ecosysteme.Plantes : _ecosysteme.Gibier;
                var garde = new List<int>();
                for (var y = Math.Max(0, c.Y - c.Rayon); y <= Math.Min(Carte.Hauteur - 1, c.Y + c.Rayon); y++)
                    for (var x = Math.Max(0, c.X - c.Rayon); x <= Math.Min(Carte.Largeur - 1, c.X + c.Rayon); x++)
                    {
                        var i = Carte.Indice(x, y);
                        garde.Add(i);
                        garde.Add(cases[i]);
                        cases[i] = (ushort)Math.Clamp(Math.Round(cases[i] * c.Valeur!.Value, MidpointRounding.AwayFromZero), 0, Ecosysteme.Plafond);
                    }
                var r = Ecrire(new Recit($"libre.ch.{quoi}", ("ou", Lieux.DirectionR(Carte, c.X, c.Y)), ("x", c.X), ("y", c.Y), ("r", c.Rayon),
                    ("f", new Decimale(c.Valeur!.Value, 2))));
                return (new ChangementFait(quoi, 0, 0, 0, [], [], string.Join(",", garde)), r);
            }
            case "pluie":
            {
                var region = _ecosysteme.Region(c.X, c.Y);
                var avant = new Dictionary<string, double> { ["region"] = region, ["pluie"] = _ecosysteme.Pluies[region] };
                _ecosysteme.Pluies[region] = c.Valeur!.Value;
                var r = Ecrire(new Recit("libre.ch.pluie", ("ou", Lieux.DirectionR(Carte, c.X, c.Y)), ("x", c.X), ("y", c.Y), ("p", Libelles.Pourcent(c.Valeur.Value))));
                return (new ChangementFait(quoi, 0, 0, 0, avant, new() { ["region"] = region, ["pluie"] = c.Valeur.Value }), r);
            }
            default:
                return AppliquerInvention(c, annonce, maintenant, (r, h, com) => Ecrire(r, h, null, com));
        }
    }

    private static object Arrondi(double v, double max) => max <= 1 ? new Decimale(v, 2) : (object)(long)Math.Round(v);

    // ───────────── Défaire ─────────────

    private Recit? Retablir(ChangementFait f, long maintenant)
    {
        var h = f.Humain >= 1 && f.Humain <= _humains.Count ? _humains[f.Humain - 1] : null;
        void Remettre()
        {
            if (h is null) return;
            foreach (var (champ, v) in f.Avant)
            {
                if (Simulation.Libre.Nombres.TryGetValue(champ, out var n)) n.Ecrire(h, v);
                else if (Simulation.Libre.Internes.TryGetValue(champ, out var i)) i.Ecrire(h, v);
            }
        }
        if (Simulation.Libre.Nombres.TryGetValue(f.Quoi, out var nombre))
        {
            if (h is null || !h.EstVivant) return null;
            Remettre();
            return new Recit("libre.d.nombre", ("h", h), ("q", Recit.Seul($"libre.q.{f.Quoi}")), ("v", Arrondi(nombre.Lire(h), nombre.Max)));
        }
        switch (f.Quoi)
        {
            case "age":
            case "maladie":
            case "blessure":
            case "deplacer":
            case "communaute":
            case "guerir":
                if (h is null || !h.EstVivant) return null;
                Remettre();
                return new Recit($"libre.d.{f.Quoi}", ("h", h));
            case "mourir":
                // Il revient comme il était au moment de sa mort.
                if (h is null || h.EstVivant) return null;
                Remettre();
                _humains.Revenir(h);
                _eteint = false;
                return new Recit("libre.d.mourir", ("h", h));
            case "ressusciter":
                if (h is null || !h.EstVivant) return null;
                Remettre();
                _humains.Recenser();
                return new Recit("libre.d.ressusciter", ("h", h));
            case "enfant":
                // La grossesse est retirée si l'enfant n'est pas encore né ; né, il reste.
                if (h is null || !h.EstVivant || h.EnceinteDepuis is null || h.PereEnfantId != (int)f.Apres.GetValueOrDefault("pereEnfant")) return null;
                Remettre();
                return new Recit("libre.d.enfant", ("h", h));
            case "lien":
            {
                if (h is null) return null;
                var i = h.Liens.FindIndex(l => l.Id == f.Autre);
                if (f.Extra.Length > 0)
                {
                    var ancien = JsonSerializer.Deserialize<Lien>(f.Extra)!;
                    if (i >= 0) h.Liens[i] = ancien;
                    else h.Liens.Add(ancien);
                }
                else if (i >= 0) h.Liens.RemoveAt(i);
                return f.Autre >= 1 && f.Autre <= _humains.Count ? new Recit("libre.d.lien", ("h", h), ("o", _humains[f.Autre - 1])) : null;
            }
            case "pensee":
                if (h is null) return null;
                h.Memoire.Jardin.RemoveAll(i => i.Cle == f.Extra);
                return new Recit("libre.d.pensee", ("h", h));
            case "oublier":
                if (h is null || f.Extra.Length == 0) return null;
                h.Memoire.Jardin.Add(JsonSerializer.Deserialize<IntimeGarde>(f.Extra)!.Rendre());
                return new Recit("libre.d.oublier", ("h", h));
            case "savoir":
            {
                if (h is null || f.Extra.Length == 0) return null;
                var s = JsonSerializer.Deserialize<SavoirGarde>(f.Extra)!.Rendre();
                h.Savoirs.RemoveAll(x => x.Meme(s.Geste, s.A, s.B, s.Effet, s.Espece, s.Symptome) && x.Pieces == s.Pieces && x.Origine == OrigineSavoir.Entite);
                return new Recit("libre.d.savoir", ("h", h), ("e", Recit.Seul($"effet.{s.Effet}")));
            }
            case "oublier_savoir":
            {
                if (h is null) return null;
                var rendus = JsonSerializer.Deserialize<List<SavoirGarde>>(f.Extra) ?? [];
                foreach (var g in rendus)
                {
                    var s = g.Rendre();
                    if (!h.Savoirs.Exists(x => x.Meme(s.Geste, s.A, s.B, s.Effet, s.Espece, s.Symptome) && x.Pieces == s.Pieces)) h.Savoirs.Add(s);
                }
                return rendus.Count == 0 ? null : new Recit("libre.d.savoir", ("h", h), ("e", Recit.Seul($"effet.{rendus[0].Effet}")));
            }
            case "relation":
            {
                if (f.Communaute < 1 || f.Communaute > _communautes.Count || f.Autre < 1 || f.Autre > _communautes.Count) return null;
                var (a, b) = (_communautes[f.Communaute - 1], _communautes[f.Autre - 1]);
                _social.RetablirRelation(a, b, (EtatRelation)(byte)f.Avant.GetValueOrDefault("etat"), f.Avant.GetValueOrDefault("guerre") > 0, maintenant);
                return new Recit("libre.d.relation", ("a", a), ("b", b));
            }
            case "plantes":
            case "gibier":
            {
                var cases = f.Quoi == "plantes" ? _ecosysteme.Plantes : _ecosysteme.Gibier;
                var t = f.Extra.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                for (var k = 0; k + 1 < t.Length; k += 2) if (t[k] >= 0 && t[k] < cases.Length) cases[t[k]] = (ushort)t[k + 1];
                return Recit.Seul($"libre.d.{f.Quoi}");
            }
            case "pluie":
            {
                var r = (int)f.Avant["region"];
                if (r < 0 || r >= _ecosysteme.Pluies.Length) return null;
                _ecosysteme.Pluies[r] = f.Avant["pluie"];
                return Recit.Seul("libre.d.pluie");
            }
            default:
                return RetablirInvention(f, h);
        }
    }
}

/// <summary>Un savoir mis de côté pour être rendu (enregistrement simple, lisible en JSON).</summary>
internal sealed record SavoirGarde(byte Geste, byte A, byte B, byte Effet, int Espece, byte Symptome, int Essais, int Succes, long AppriLe, long UtiliseLe,
    byte Origine, int MaitreId, long EvenementId, string Pieces)
{
    public static SavoirGarde De(Savoir s) => new((byte)s.Geste, (byte)s.A, (byte)s.B, (byte)s.Effet, s.Espece, (byte)s.Symptome, s.Essais, s.Succes, s.AppriLe, s.UtiliseLe,
        (byte)s.Origine, s.MaitreId, s.EvenementId, s.Pieces);

    public Savoir Rendre() => new((Geste)Geste, (Matiere)A, (Matiere)B, (Effet)Effet, Espece, (Symptome)Symptome, Essais, Succes, AppriLe, UtiliseLe,
        (OrigineSavoir)Origine, MaitreId, EvenementId, Pieces);
}

/// <summary>Une chose du jardin secret mise de côté pour être rendue (son récit est gardé encodé).</summary>
internal sealed record IntimeGarde(long Secondes, byte Type, int SujetId, string? Brut, double Intensite, long CauseId, string Cle, string? Recit)
{
    public static IntimeGarde De(Intime i) => new(i.Secondes, (byte)i.Type, i.SujetId, i.Brut, i.Intensite, i.CauseId, i.Cle, i.Recit?.Encoder());

    public Intime Rendre() => new(Secondes, (TypeIntime)Type, SujetId, Brut, Intensite, CauseId, Cle, Recits.Recit.Decoder(Recit));
}
