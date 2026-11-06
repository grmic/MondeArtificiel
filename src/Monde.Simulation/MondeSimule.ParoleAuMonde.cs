using Monde.Simulation.Humains;
using Monde.Simulation.Recits;

namespace Monde.Simulation;

/// <summary>
/// Ce qu'une communauté (ou les isolés, <see cref="CommunauteId"/> 0) a fait d'une parole au Monde : combien l'ont entendue,
/// comprise, crue, veulent la suivre, hésitent ou refusent. <see cref="Visee"/> : la communauté que la parole visait.
/// </summary>
public sealed record BilanParoleMonde(int CommunauteId, int Entendu, int Compris, int Crus, int Veulent, int Hesitent, int Refusent,
    bool Visee, int MandatId, long EvenementId);

/// <summary>
/// Une parole de l'Entité au Monde entier, ou à toute une terre (0.37.1). <see cref="Terre"/> : la masse de terre (0 : le Monde
/// entier) ; <see cref="CibleHumain"/> : la personne désignée (« Écoutez… ») ; <see cref="CibleCommunaute"/> : la communauté
/// visée (« Tous, faites la guerre à… ») ; <see cref="Param"/> : la plante d'un avertissement. <see cref="Auteur"/> : 0 le joueur,
/// 1 l'Entité jouée par le LLM.
/// </summary>
public sealed record ParoleMonde(int Id, long Le, Parole Parole, ModeParole Mode, int Terre, int CibleHumain, int CibleCommunaute, int Param,
    long EvenementId, IReadOnlyList<BilanParoleMonde> Bilans, int Auteur = 0);

public sealed partial class MondeSimule
{
    private readonly List<ParoleMonde> _parolesMonde = [];

    /// <summary>Paroles de l'Entité au Monde, de la première à la dernière (copie).</summary>
    public List<ParoleMonde> ParolesMonde()
    {
        lock (_verrou) return [.. _parolesMonde];
    }

    /// <summary>
    /// Les terres que l'on peut viser : chaque masse de terre où vivent des humains, avec ses vivants, du plus grand continent
    /// aux îles. <see cref="Rang"/> : rang du continent par sa taille (1 : le plus grand ; 0 : une île).
    /// </summary>
    public List<(int Masse, int Rang, int Vivants)> TerresHabitees()
    {
        lock (_verrou)
        {
            var masse = Carte.Mer.Masse;
            return _humains.Vivants().Where(h => h.EstVivant).GroupBy(h => masse[Carte.Indice(h.X, h.Y)]).Where(g => g.Key > 0)
                .Select(g => (g.Key, RangDeTerre(g.Key), g.Count())).OrderBy(t => t.Item2 == 0 ? int.MaxValue : t.Item2).ThenByDescending(t => t.Item3).ToList();
        }
    }

    /// <summary>Rang du continent par sa taille (1 : le plus grand), ou 0 pour une île.</summary>
    public int RangDeTerre(int masse)
    {
        var i = Carte.Mer.Continents.ToList().IndexOf(masse);
        return i < 0 ? 0 : i + 1;
    }

    /// <summary>
    /// L'Entité parle au Monde entier, ou à toute une terre (<paramref name="terre"/>, 0 : partout) : chaque vivant en âge de
    /// comprendre l'entend en même temps, et la reçoit selon ses règles (comprendre, croire, obéir ou non). Un signe n'est vu
    /// que de ceux qui sont éveillés ; une voix ou une apparition réveille les dormeurs. Une parole qui vise une communauté est
    /// débattue par chacune des autres (voir <see cref="VieSociale.Recevoir"/>) ; ceux qu'elle vise l'entendent aussi. Rend la
    /// parole et ses bilans, ce que chacun en a fait et les mandats créés ; null si la parole ne peut pas être dite au Monde.
    /// </summary>
    public (ParoleMonde Parole, List<(int Humain, Recit Resultat)> Resultats, List<Mandat> Mandats)? ParlerAuMonde(Parole parole, ModeParole mode,
        int terre, int cibleHumain, int cibleCommunaute, int param, int auteur = 0)
    {
        lock (_verrou)
        {
            if (!Paroles.AuMonde(parole) || mode == ModeParole.Penchant) return null;
            var cible = parole == Parole.Ecoute && cibleHumain >= 1 && cibleHumain <= _humains.Count && _humains[cibleHumain - 1].EstVivant
                ? _humains[cibleHumain - 1] : null;
            if (parole == Parole.Ecoute && cible is null) return null;
            var visee = Paroles.Collective(parole) && cibleCommunaute >= 1 && cibleCommunaute <= _communautes.Count && _communautes[cibleCommunaute - 1].Active
                ? _communautes[cibleCommunaute - 1] : null;
            if (Paroles.Collective(parole) && visee is null) return null;
            if (parole == Parole.PlanteDangereuse && (param < 1 || param > Flore.Especes.Count)) return null;
            var masses = Carte.Mer.Masse;
            if (terre != 0 && !_humains.Vivants().Any(h => h.EstVivant && masses[Carte.Indice(h.X, h.Y)] == terre)) return null;

            // Qui l'entend : les vivants en âge de comprendre, sur la terre choisie ; un signe, seulement ceux qui sont éveillés.
            var ecoutent = _humains.Vivants().Where(h => h.EstVivant && !h.EstBebe(_secondes) && h.Id != cible?.Id
                                                         && (terre == 0 || masses[Carte.Indice(h.X, h.Y)] == terre)
                                                         && (mode != ModeParole.Signe || h.Activite != Activite.Dormir))
                .OrderBy(h => h.Id).ToList();
            foreach (var h in ecoutent)
            {
                if (mode is ModeParole.Voix or ModeParole.Apparition && h.Activite == Activite.Dormir) h.Activite = Activite.Repos;
                if (_fond is { } f && h.Id < f.Length) f[h.Id] = false;
            }
            object? extra = parole switch
            {
                Parole.PlanteDangereuse => Flore.Get(param).Dit,
                _ when visee is not null => visee,
                _ => null,
            };
            object portee = terre == 0 ? Recit.Seul("monde.portee.tout")
                : RangDeTerre(terre) is var rang and > 0 ? new Recit("monde.portee.continent", ("k", rang)) : Recit.Seul("monde.portee.ile");
            var id = _comportement.NoterAuMonde(_secondes, new Recit("monde.parle", ("m", Recit.Seul($"modeParole.{mode}")),
                ("ph", Paroles.Phrase(parole, cible, extra, monde: true)), ("portee", portee), ("n", ecoutent.Count)), cible);

            var resultats = new List<(int Humain, Recit Resultat)>();
            var mandats = new List<Mandat>();
            if (visee is null)
            {
                foreach (var h in ecoutent)
                    resultats.Add((h.Id, _comportement.Parler(h, parole, mode, cible, param, _humains, _secondes, ensemble: true, cause: id, monde: true)));
            }
            else
            {
                // Les adultes de chaque autre groupe la reçoivent pour le groupe, qui en débattra ; ceux qu'elle vise l'entendent ;
                // les isolés et les captifs la reçoivent seuls. Les enfants des groupes n'ont pas voix au débat.
                var parGroupe = ecoutent.Where(h => h.CommunauteId > 0 && h.CommunauteId != visee.Id && h.CaptifDe == 0 && !h.EstEnfant(_secondes))
                    .GroupBy(h => h.CommunauteId).OrderBy(g => g.Key);
                foreach (var g in parGroupe)
                {
                    var a = _communautes[g.Key - 1];
                    if (!a.Active) continue;
                    foreach (var h in g)
                        resultats.Add((h.Id, _comportement.RecevoirPourLeGroupe(h, parole, mode, a, [visee], 0, _humains, _secondes, id, monde: true)));
                    mandats.AddRange(_social.Recevoir(a, parole, mode, [visee], 0, _humains, _secondes, id));
                }
                foreach (var h in ecoutent.Where(h => h.CommunauteId == visee.Id))
                    resultats.Add((h.Id, _comportement.RecevoirEnVise(h, parole, mode, visee, _humains, _secondes, id)));
                foreach (var h in ecoutent.Where(h => h.CommunauteId != visee.Id && (h.CommunauteId == 0 || h.CaptifDe != 0) && !h.EstEnfant(_secondes)))
                    resultats.Add((h.Id, _comportement.RecevoirSansGroupe(h, parole, mode, visee, _humains, _secondes, id)));
            }

            // Le bilan, par communauté (et pour les isolés), noté sous la parole.
            var bilans = new List<BilanParoleMonde>();
            var groupeDe = ecoutent.ToDictionary(h => h.Id, h => h.CaptifDe != 0 ? 0 : h.CommunauteId);
            foreach (var g in resultats.GroupBy(r => groupeDe[r.Humain]).OrderBy(g => g.Key))
            {
                int compris = 0, crus = 0, veulent = 0, hesitent = 0, refusent = 0;
                foreach (var (_, r) in g)
                {
                    var cle = r.Cle;
                    if (cle is "ent.r.incomprise" or "ent.r.mort" or "ent.r.bebe") continue;
                    compris++;
                    if (cle == "ent.r.incrue") continue;
                    crus++;
                    if (cle is "ent.r.refusee" or "ent.r.groupe.refuse" or "ent.r.dementie") refusent++;
                    else if (cle is "ent.r.hesite" or "ent.r.groupe.hesite") hesitent++;
                    else if (cle is not ("ent.r.monde.seul" or "ent.r.monde.peur" or "ent.r.monde.espoir")) veulent++;
                }
                var estVisee = visee is not null && g.Key == visee.Id;
                var c = g.Key > 0 ? _communautes[g.Key - 1] : null;
                var texte = c is null
                    ? new Recit("monde.bilan.seuls", ("n", g.Count()), ("compris", compris), ("crus", crus), ("v", veulent), ("hes", hesitent), ("r", refusent))
                    : new Recit(estVisee ? "monde.bilan.vise" : "monde.bilan", ("c", c), ("n", g.Count()), ("compris", compris), ("crus", crus),
                        ("v", veulent), ("hes", hesitent), ("r", refusent));
                var evenement = _comportement.NoterAuMonde(_secondes, texte, null, id, g.Key);
                var mandat = mandats.FirstOrDefault(m => m.CommunauteId == g.Key)?.Id ?? 0;
                bilans.Add(new BilanParoleMonde(g.Key, g.Count(), compris, crus, veulent, hesitent, refusent, estVisee, mandat, evenement));
            }
            var pm = new ParoleMonde(_parolesMonde.Count + 1, _secondes, parole, mode, terre, cible?.Id ?? 0, visee?.Id ?? 0, param, id, bilans, auteur);
            _parolesMonde.Add(pm);
            VerserJournal();
            return (pm, resultats, mandats.Select(m => m.Copier()).ToList());
        }
    }
}
