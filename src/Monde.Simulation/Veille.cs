using Monde.Simulation.Humains;
using Monde.Simulation.Recits;

namespace Monde.Simulation;

/// <summary>Sortes d'alertes sur les suivis ★ (0.40.0). Les premières concernent un humain suivi, les suivantes une communauté suivie.</summary>
public enum SorteAlerte : byte
{
    /// <summary>Il meurt (la cause est dans la ligne du journal).</summary>
    Mort = 0,
    /// <summary>Un enfant lui naît.</summary>
    Enfant = 1,
    /// <summary>Il forme un couple, ou se retrouve seul alors que son partenaire vit.</summary>
    Couple = 2,
    /// <summary>Il tombe malade.</summary>
    Maladie = 3,
    /// <summary>Il frappe ou est frappé, tue ou est tué.</summary>
    Violence = 4,
    /// <summary>Il est chassé, renié, ou son exil prend fin.</summary>
    Exil = 5,
    /// <summary>Il est enlevé, ou n'est plus captif.</summary>
    Captivite = 6,
    /// <summary>Il devient figure de son groupe, prophète, prêtre ou spécialiste (ou cesse de l'être).</summary>
    Role = 7,
    /// <summary>Il fait une découverte, ou une première fois (au monde ou pour les siens).</summary>
    Decouverte = 8,
    /// <summary>L'Entité s'adresse à lui, il prie, obéit ou refuse.</summary>
    Entite = 9,
    /// <summary>Il prend la mer, voit ou touche une autre terre.</summary>
    Mer = 10,

    /// <summary>Une guerre commence ou finit, un raid, une paix, une soumission.</summary>
    Guerre = 11,
    /// <summary>La disette commence ou prend fin.</summary>
    Disette = 12,
    /// <summary>Scission, dispersion, fusion, nouveau nom : la vie du groupe.</summary>
    Groupe = 13,
    /// <summary>Une nouvelle figure à la tête du groupe.</summary>
    Chef = 14,
    /// <summary>Une religion naît, un prophète parle, un schisme.</summary>
    Religion = 15,
    /// <summary>Une catastrophe frappe le groupe.</summary>
    Catastrophe = 16,
    /// <summary>Une première fois pour le groupe.</summary>
    PremiereFois = 17,
}

/// <summary>Une alerte : ce qui est arrivé à un suivi, la ligne du journal qui le dit (0 : aucune) ou un texte à elle.</summary>
public sealed record AlerteSuivi(long Secondes, SorteAlerte Sorte, int HumainId, int CommunauteId, long EvenementId, Recit Texte, bool Pause);

/// <summary>Pour chaque sorte d'alerte : prévenir, et mettre le monde en pause.</summary>
public sealed class ReglesAlertes
{
    public static int Nombre => Enum.GetValues<SorteAlerte>().Length;

    public bool[] Prevenir { get; } = Enumerable.Repeat(true, Nombre).ToArray();
    public bool[] Pause { get; } = new bool[Nombre];

    /// <summary>Réglage par défaut : tout prévient ; la mort d'un suivi et une guerre qui touche une communauté suivie mettent en pause.</summary>
    public static ReglesAlertes ParDefaut()
    {
        var r = new ReglesAlertes();
        r.Pause[(int)SorteAlerte.Mort] = true;
        r.Pause[(int)SorteAlerte.Guerre] = true;
        return r;
    }

    public static bool HumainSeul(SorteAlerte s) => s <= SorteAlerte.Mer;
}

/// <summary>
/// La veille sur les suivis : elle lit chaque ligne du journal au moment où elle s'écrit, et regarde chaque jour ce qui a changé
/// pour eux (couple, rôle, captivité, disette, figure du groupe). Elle ne change rien au monde : elle ne fait que prévenir.
/// </summary>
internal sealed class Veille
{
    private HashSet<int> _humains = [];
    private HashSet<int> _communautes = [];
    private ReglesAlertes _regles = ReglesAlertes.ParDefaut();
    private readonly List<AlerteSuivi> _alertes = [];
    // Ce qu'on savait d'eux hier : on compare chaque jour.
    private readonly Dictionary<int, (int Partenaire, int Captif, RoleReligieux Role, Ouvrage Specialite, bool Figure)> _etatHumains = [];
    private readonly Dictionary<int, (bool Disette, int Figure, bool Active)> _etatCommunautes = [];
    // Une même chose n'alerte qu'une fois par jour (une maladie dite par plusieurs lignes, par exemple).
    private readonly HashSet<(long Jour, SorteAlerte Sorte, int Humain, int Communaute)> _dejaDites = [];

    public bool Active => _humains.Count > 0 || _communautes.Count > 0;

    public bool PauseDemandee { get; set; }

    /// <summary>Nombre d'alertes produites depuis le début (pour l'objectif « prochaine alerte d'un suivi »).</summary>
    public long Produites { get; private set; }

    public void Definir(IEnumerable<int> humains, IEnumerable<int> communautes, ReglesAlertes regles, IReadOnlyList<Humain> tous,
        IReadOnlyList<Communaute> groupes)
    {
        _humains = [.. humains];
        _communautes = [.. communautes];
        _regles = regles;
        // Ce qu'ils sont aujourd'hui sert de point de départ : on ne prévient pas de ce qui était déjà vrai.
        foreach (var id in _etatHumains.Keys.Where(id => !_humains.Contains(id)).ToList()) _etatHumains.Remove(id);
        foreach (var id in _humains)
            if (!_etatHumains.ContainsKey(id) && id > 0 && id <= tous.Count) _etatHumains[id] = Etat(tous[id - 1], groupes);
        foreach (var id in _etatCommunautes.Keys.Where(id => !_communautes.Contains(id)).ToList()) _etatCommunautes.Remove(id);
        foreach (var c in groupes)
            if (_communautes.Contains(c.Id) && !_etatCommunautes.ContainsKey(c.Id)) _etatCommunautes[c.Id] = (c.Disette, c.FigureId, c.Active);
    }

    public List<AlerteSuivi> Prendre()
    {
        var a = _alertes.ToList();
        _alertes.Clear();
        return a;
    }

    private static (int, int, RoleReligieux, Ouvrage, bool) Etat(Humain h, IReadOnlyList<Communaute> groupes) =>
        (h.PartenaireId, h.CaptifDe, h.Role, h.Specialite,
            h.CommunauteId != 0 && groupes.FirstOrDefault(c => c.Id == h.CommunauteId) is { Active: true } c && c.FigureId == h.Id);

    private void Alerter(long maintenant, SorteAlerte sorte, int humain, int communaute, long evenement, Recit texte)
    {
        if (!_regles.Prevenir[(int)sorte]) return;
        if (!_dejaDites.Add((maintenant / Temps.Calendrier.SecondesParJour, sorte, humain, communaute))) return;
        if (_dejaDites.Count > 10_000) _dejaDites.Clear();
        var pause = _regles.Pause[(int)sorte];
        _alertes.Add(new AlerteSuivi(maintenant, sorte, humain, communaute, evenement, texte, pause));
        Produites++;
        if (pause) PauseDemandee = true;
    }

    private static readonly HashSet<string> ClesGroupe =
    [
        "j.communaute.scission", "j.communaute.disperse", "j.communaute.dureePas", "j.communaute.renait",
        "j.fusion.decident", "j.fusion.fond", "j.fusion.fondNom", "j.nom.renomme", "j.nom.nomme",
    ];

    /// <summary>Une ligne vient de s'écrire au journal : concerne-t-elle un suivi ?</summary>
    public void Examiner(EvenementJournal e, IReadOnlyList<Humain> tous)
    {
        if (!Active || e.Recit is not { } texte) return;
        var cle = texte.Cle;
        var h = e.HumainId;
        var a = e.AutreId;
        if (_humains.Count > 0)
        {
            var sorte = (SorteAlerte?)null;
            var qui = 0;
            switch (e.Type)
            {
                case TypeEvenement.Mort when _humains.Contains(h):
                    (sorte, qui) = (SorteAlerte.Mort, h);
                    break;
                case TypeEvenement.Mort when _humains.Contains(a) && cle == "j.mort.meurtre":
                    (sorte, qui) = (SorteAlerte.Violence, a);
                    break;
                case TypeEvenement.Naissance when cle == "j.naissance" && h > 0 && h <= tous.Count:
                    var bebe = tous[h - 1];
                    if (_humains.Contains(bebe.MereId)) (sorte, qui) = (SorteAlerte.Enfant, bebe.MereId);
                    else if (_humains.Contains(bebe.PereId)) (sorte, qui) = (SorteAlerte.Enfant, bebe.PereId);
                    break;
                case TypeEvenement.Maladie when _humains.Contains(h):
                    (sorte, qui) = (SorteAlerte.Maladie, h);
                    break;
                case TypeEvenement.Conflit when cle is "j.frappe" or "j.sePrendA" && (_humains.Contains(h) || _humains.Contains(a)):
                    (sorte, qui) = (SorteAlerte.Violence, _humains.Contains(h) ? h : a);
                    break;
                case TypeEvenement.Exclusion when cle.StartsWith("j.exil.", StringComparison.Ordinal) && (_humains.Contains(h) || _humains.Contains(a)):
                    (sorte, qui) = (SorteAlerte.Exil, _humains.Contains(h) ? h : a);
                    break;
                case TypeEvenement.Mer when e.Significatif && _humains.Contains(h):
                    (sorte, qui) = (SorteAlerte.Mer, h);
                    break;
            }
            if (sorte is null && _humains.Contains(h))
            {
                if (e.Origine == Origine.Entite || cle.StartsWith("pri.", StringComparison.Ordinal) || cle.StartsWith("mandat.", StringComparison.Ordinal))
                    (sorte, qui) = (SorteAlerte.Entite, h);
                else if (e.Premiere != 0 || e.Type == TypeEvenement.Decouverte && e.Significatif)
                    (sorte, qui) = (SorteAlerte.Decouverte, h);
            }
            if (sorte is { } s) Alerter(e.Secondes, s, qui, 0, e.Id, texte);
        }
        if (_communautes.Count > 0)
        {
            var c = _communautes.Contains(e.CommunauteId) ? e.CommunauteId : _communautes.Contains(e.AutreCommunauteId) ? e.AutreCommunauteId : 0;
            if (c == 0) return;
            SorteAlerte? sorte = e.Type switch
            {
                TypeEvenement.Guerre when e.Significatif => SorteAlerte.Guerre,
                TypeEvenement.Catastrophe when e.Significatif => SorteAlerte.Catastrophe,
                TypeEvenement.Religion when e.Significatif => SorteAlerte.Religion,
                TypeEvenement.Communaute when ClesGroupe.Contains(cle) => SorteAlerte.Groupe,
                _ when (e.Premiere & PremieresFois.PourLaCommunaute) != 0 => SorteAlerte.PremiereFois,
                _ => null,
            };
            if (sorte is { } s) Alerter(e.Secondes, s, 0, c, e.Id, texte);
        }
    }

    /// <summary>Chaque jour : ce qui a changé pour eux depuis hier.</summary>
    public void Constater(long maintenant, IReadOnlyList<Humain> tous, IReadOnlyList<Communaute> groupes)
    {
        if (!Active) return;
        foreach (var id in _humains)
        {
            if (id <= 0 || id > tous.Count) continue;
            var h = tous[id - 1];
            if (!h.EstVivant) continue;
            var maintenantEtat = Etat(h, groupes);
            if (!_etatHumains.TryGetValue(id, out var avant))
            {
                _etatHumains[id] = maintenantEtat;
                continue;
            }
            _etatHumains[id] = maintenantEtat;
            var (partenaire, captif, role, specialite, figure) = maintenantEtat;
            if (partenaire != avant.Partenaire)
            {
                if (partenaire > 0 && partenaire <= tous.Count)
                    Alerter(maintenant, SorteAlerte.Couple, id, 0, 0, new Recit("alerte.couple", ("h", h), ("p", tous[partenaire - 1])));
                else if (avant.Partenaire > 0 && avant.Partenaire <= tous.Count && tous[avant.Partenaire - 1] is { EstVivant: true } ancien)
                    Alerter(maintenant, SorteAlerte.Couple, id, 0, 0, new Recit("alerte.separation", ("h", h), ("p", ancien)));
            }
            if (captif != avant.Captif)
            {
                if (captif > 0 && captif <= tous.Count)
                    Alerter(maintenant, SorteAlerte.Captivite, id, 0, 0, new Recit("alerte.captif", ("h", h), ("r", tous[captif - 1])));
                else Alerter(maintenant, SorteAlerte.Captivite, id, 0, 0, new Recit("alerte.libre", ("h", h)));
            }
            if (role != avant.Role)
                Alerter(maintenant, SorteAlerte.Role, id, 0, 0, new Recit($"alerte.role.{role}", ("h", h)));
            if (specialite != avant.Specialite && specialite != Ouvrage.Aucun)
                Alerter(maintenant, SorteAlerte.Role, id, 0, 0, new Recit("alerte.specialiste", ("h", h), ("role", Recit.Seul($"ouv.role.{specialite}"))));
            if (figure && !avant.Figure && groupes.FirstOrDefault(c => c.Id == h.CommunauteId) is { } groupe)
                Alerter(maintenant, SorteAlerte.Role, id, 0, 0, new Recit("alerte.figure", ("h", h), ("c", groupe)));
        }
        foreach (var c in groupes)
        {
            if (!_communautes.Contains(c.Id)) continue;
            var etat = (c.Disette, c.FigureId, c.Active);
            if (!_etatCommunautes.TryGetValue(c.Id, out var avant))
            {
                _etatCommunautes[c.Id] = etat;
                continue;
            }
            _etatCommunautes[c.Id] = etat;
            if (c.Active && c.Disette != avant.Disette)
                Alerter(maintenant, SorteAlerte.Disette, 0, c.Id, 0, new Recit(c.Disette ? "alerte.disette" : "alerte.disette.fin", ("c", c)));
            if (c.Active && c.FigureId != avant.Figure && c.FigureId > 0 && c.FigureId <= tous.Count)
                Alerter(maintenant, SorteAlerte.Chef, 0, c.Id, 0, new Recit("alerte.chef", ("c", c), ("h", tous[c.FigureId - 1])));
            if (!c.Active && avant.Active)
                Alerter(maintenant, SorteAlerte.Groupe, 0, c.Id, 0, new Recit("alerte.dispersee", ("c", c)));
        }
    }
}
