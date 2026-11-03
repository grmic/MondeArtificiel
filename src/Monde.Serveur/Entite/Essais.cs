using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur.Entite;

/// <summary>
/// Essaie un acte du LLM contre l'état du monde, sans rien changer (0.36.3) : toutes les vérifications que les pouvoirs feraient en
/// l'appliquant (numéros vivants, communauté encore là, prière encore ouverte, précision attendue, recette lisible, acte libre dans
/// ses bornes). Une faute lève <see cref="ArgumentException"/> avec une raison précise, pour que le LLM corrige au second essai au
/// lieu d'être refusé par les règles. Ce que les règles décident ensuite (obéir, refuser, exaucer) n'est pas une faute.
/// </summary>
public static class Essais
{
    // Les numéros tels quels (n°12345, pas « 12 345 ») : le LLM doit pouvoir les recopier.
    private static ArgumentException Faute(string cle, params (string, object)[] valeurs) =>
        new(L.T(cle, [.. valeurs.Select(v => v.Item2 is int i ? (v.Item1, (object)i.ToString(System.Globalization.CultureInfo.InvariantCulture)) : v)]));

    public static void Essayer(MondeSimule monde, ActeLlm a, IReadOnlyList<string>? pieces)
    {
        var (maintenant, humains) = monde.InstantaneHumains();
        var communautes = monde.Communautes();

        void Vivant(int id, string champ)
        {
            if (id < 1 || id > humains.Count) throw Faute("ess.humainInconnu", ("champ", champ), ("id", id));
            if (!humains[id - 1].EstVivant) throw Faute("ess.humainMort", ("champ", champ), ("id", id), ("nom", humains[id - 1].Nom));
        }

        void Active(int id, string champ)
        {
            if (id < 1 || id > communautes.Count || !communautes[id - 1].Active) throw Faute("ess.communaute", ("champ", champ), ("id", id));
        }

        void Destinataires(int[]? ids, int[]? groupes)
        {
            if ((ids?.Length ?? 0) + (groupes?.Length ?? 0) == 0) throw Faute("ess.destinataires");
            foreach (var id in ids ?? []) Vivant(id, "humains");
            foreach (var c in groupes ?? []) Active(c, "communautes");
        }

        switch (a.Type)
        {
            case Actes.Parler:
            {
                var p = Parole(a.Parole, collective: false);
                Mode(a.Mode);
                var ids = a.Humains is { Length: > 0 } hs ? hs : a.Humain is { } x ? [x] : null;
                Destinataires(ids, a.Communautes);
                var cible = a.Cible ?? 0;
                if (Paroles.VeutCible(p))
                {
                    if (cible == 0) throw Faute("ess.cibleManque", ("parole", p));
                    Vivant(cible, "cible");
                    if (ids is { Length: 1 } seul && seul[0] == cible && (a.Communautes?.Length ?? 0) == 0) throw Faute("ess.cibleSoi", ("parole", p));
                    if (p == Simulation.Humains.Parole.UnisToi && humains[cible - 1].EstEnfant(maintenant)) throw Faute("ess.cibleEnfant", ("nom", humains[cible - 1].Nom));
                }
                var param = a.Param ?? a.Direction;
                switch (p)
                {
                    case Simulation.Humains.Parole.PartsVers when param is not (>= 0 and <= 7):
                        throw Faute("ess.direction", ("parole", p));
                    case Simulation.Humains.Parole.PlanteDangereuse when param is not { } e || e < 1 || e > monde.Flore.Especes.Count:
                        throw Faute("ess.plante", ("parole", p));
                    case Simulation.Humains.Parole.NeVaPasALaGuerre when param is { } c && c != 0:
                        Active(c, "param");
                        break;
                }
                return;
            }
            case Actes.ParlerCommunaute:
            {
                var p = Parole(a.Parole, collective: true);
                Mode(a.Mode);
                Active(a.Communaute ?? 0, "communaute");
                var cibles = a.Cibles ?? (a.Cible is { } c ? [c] : []);
                if (Paroles.VeutCommunaute(p))
                {
                    if (cibles.Length == 0) throw Faute("ess.ciblesManque", ("parole", p));
                    foreach (var x in cibles)
                    {
                        if (x == a.Communaute) throw Faute("ess.cibleSoi", ("parole", p));
                        Active(x, "cibles");
                    }
                }
                if (p == Simulation.Humains.Parole.PartezVers && (a.Direction ?? a.Param) is not (>= 0 and <= 7)) throw Faute("ess.direction", ("parole", p));
                return;
            }
            case Actes.Penchant:
            {
                var ids = a.Humains is { Length: > 0 } hs ? hs : a.Humain is { } x ? [x] : null;
                Destinataires(ids, a.Communautes);
                if (a.Cible is { } c && c != 0) Vivant(c, "cible");
                return;
            }
            case Actes.Geste:
            {
                var demande = new DemandeGeste(a.Geste ?? "", a.Cible ?? a.Humain ?? a.Communaute ?? 0, a.Maniere ?? nameof(ManiereGeste.Cachee), a.Param ?? 0,
                    a.Calamite, a.Force ?? 0, a.Jours ?? 0);
                GesteEntite g;
                try
                {
                    g = GestesEntite.Lire(demande, monde.Pathogenes().Select(x => x.Id).ToHashSet()).Geste;
                }
                catch (ArgumentException)
                {
                    throw Faute("ess.geste", ("geste", a.Geste ?? ""), ("maniere", a.Maniere ?? ""),
                        ("gestes", string.Join(", ", Enum.GetNames<GesteEntite>())), ("manieres", string.Join(", ", Enum.GetNames<ManiereGeste>())));
                }
                if (Gestes.SurHumain(g))
                {
                    if (demande.Cible < 1 || demande.Cible > humains.Count || !humains[demande.Cible - 1].EstVivant)
                        throw Faute("llm.erreur.cibleHumain", ("geste", g), ("cible", demande.Cible));
                }
                else if (demande.Cible < 1 || demande.Cible > communautes.Count || !communautes[demande.Cible - 1].Active)
                    throw Faute("llm.erreur.cibleCommunaute", ("geste", g), ("cible", demande.Cible));
                return;
            }
            case Actes.Catastrophe:
            {
                if (CatastropheDto.TypeDepuis(a.Catastrophe) is not { } t || t is Simulation.Nature.TypeCatastrophe.HiverVolcanique or Simulation.Nature.TypeCatastrophe.HiverDImpact)
                    throw Faute("ess.catastrophe", ("cle", a.Catastrophe ?? ""), ("liste", string.Join(", ", Actes.Provoquables.Select(x => x.Cle))));
                if (a.X is not { } cx || a.Y is not { } cy || cx < 0 || cy < 0 || cx >= monde.Carte.Largeur || cy >= monde.Carte.Hauteur)
                    throw Faute("ess.lieu", ("l", monde.Carte.Largeur - 1), ("h", monde.Carte.Hauteur - 1));
                if (a.Intensite is { } i && !double.IsFinite(i)) throw Faute("erreur.lieuIntensite");
                return;
            }
            case Actes.Exaucer:
            case Actes.Refuser:
            {
                var id = a.Priere ?? 0;
                var pr = monde.Prieres().FirstOrDefault(x => x.Id == id);
                if (pr is null) throw Faute("ess.priereInconnue", ("id", id));
                if (pr.Issue != IssuePriere.EnAttente || pr.Refus != RefusPriere.Aucun || pr.Sujet == SujetPriere.Remerciement
                    || pr.CommunauteId == 0 && (pr.HumainId < 1 || pr.HumainId > humains.Count || !humains[pr.HumainId - 1].EstVivant))
                    throw Faute("ess.priereClose", ("id", id));
                return;
            }
            case Actes.SoufflerGeste:
            {
                Vivant(a.Humain ?? a.Cible ?? 0, "humain");
                if (Enum.TryParse<Geste>(a.Geste, true, out var g) && g is Geste.Manger or Geste.Appliquer && (a.Espece is not { } e || e < 1 || e > monde.Flore.Especes.Count))
                    throw Faute("ess.espece", ("geste", g));
                return;
            }
            case Actes.SoufflerCombinaison:
            {
                Vivant(a.Humain ?? a.Cible ?? 0, "humain");
                var demande = new DemandeCombinaison(a.Humain ?? a.Cible ?? 0, a.Operation ?? "", Actes.Piece(a.A, pieces ?? []) ?? "", Actes.Piece(a.B, pieces ?? []), a.Lien ?? 0);
                if (Arbre.Recette(demande) is null) throw Faute("ess.combinaison", ("operation", a.Operation ?? ""));
                return;
            }
            case Actes.Libre:
                Libres.Verifier(monde, a);
                return;
        }
    }

    /// <summary>Une parole lue sans tenir compte des majuscules ; individuelle ou collective selon l'acte.</summary>
    private static Parole Parole(string? nom, bool collective)
    {
        if (!Enum.TryParse<Parole>(nom, true, out var p) || !Enum.IsDefined(p) || p == Simulation.Humains.Parole.Penchant || Paroles.Collective(p) != collective)
            throw Faute(collective ? "ess.paroleCollective" : "ess.parole", ("parole", nom ?? ""),
                ("liste", string.Join(", ", Enum.GetValues<Parole>().Where(x => x != Simulation.Humains.Parole.Penchant && Paroles.Collective(x) == collective))));
        return p;
    }

    private static void Mode(string? nom)
    {
        if (!Enum.TryParse<ModeParole>(nom, true, out var m) || !Enum.IsDefined(m) || m == ModeParole.Penchant)
            throw Faute("ess.mode", ("mode", nom ?? ""), ("liste", string.Join(", ", Enum.GetValues<ModeParole>().Where(x => x != ModeParole.Penchant))));
    }
}
