using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur.Entite;

/// <summary>
/// Les pouvoirs de l'Entité, vérifiés puis appliqués : les mêmes pour le joueur (par le hub) et pour le LLM. Une demande invalide
/// lève <see cref="ArgumentException"/> avec un message pour l'utilisateur (ou pour le LLM), et ne change rien.
/// </summary>
public static class Pouvoirs
{
    /// <summary>Catastrophe en (X, Y) ; rend le numéro de l'événement qui l'annonce (0 si elle n'a pas pu avoir lieu).</summary>
    public static long Declencher(MondeSimule monde, DemandeCatastrophe demande)
    {
        if (CatastropheDto.TypeDepuis(demande.Type) is not { } type || type is Simulation.Nature.TypeCatastrophe.HiverVolcanique or Simulation.Nature.TypeCatastrophe.HiverDImpact)
            throw new ArgumentException(L.T("erreur.typeCatastrophe"));
        if (demande.X < 0 || demande.Y < 0 || demande.X >= monde.Carte.Largeur || demande.Y >= monde.Carte.Hauteur || !double.IsFinite(demande.Intensite))
            throw new ArgumentException(L.T("erreur.lieuIntensite"));
        return monde.Declencher(type, demande.X, demande.Y, demande.Intensite);
    }

    /// <summary>Souffle un geste ; rend ce que l'humain a obtenu, en mots.</summary>
    public static string Souffler(MondeSimule monde, DemandeIntuition demande)
    {
        if (!Enum.TryParse<Geste>(demande.Geste, out var geste) || !Enum.IsDefined(geste)
            || !Enum.TryParse<Matiere>(demande.A, out var a) || !Enum.IsDefined(a)
            || !Enum.TryParse<Matiere>(demande.B ?? nameof(Matiere.Aucune), out var b) || !Enum.IsDefined(b))
            throw new ArgumentException(L.T("erreur.geste"));
        // Liste blanche : seules les matières qui ont un sens pour ce geste ; une plante doit être désignée par son espèce.
        var plante = geste is Geste.Manger or Geste.Appliquer;
        if (!Physique.Convient(geste, a, b) || plante && (demande.Espece < 1 || demande.Espece > monde.Flore.Especes.Count))
            throw new ArgumentException(L.T("erreur.gesteMatieres"));
        var effet = monde.Souffler(demande.Humain, geste, a, b, plante ? demande.Espece : 0);
        if (plante && effet == Effet.Rien) return L.T("geste.planteEssayee");
        return effet == Effet.Rien ? L.T("geste.rien") : L.T("geste.obtenu", ("effet", L.T($"effet.{effet}")));
    }

    /// <summary>Souffle une combinaison ; rend ce qui est arrivé, en mots.</summary>
    public static string SoufflerCombinaison(MondeSimule monde, DemandeCombinaison demande)
    {
        if (Arbre.Recette(demande) is not { } recette) throw new ArgumentException(L.T("erreur.combinaison"));
        var (effet, recit) = monde.SoufflerFacture(demande.Humain, recette);
        return recit is not null ? L.Rediger(recit) : effet == Effet.Rien ? L.T("fac.souffle.rien") : L.T("geste.obtenu", ("effet", L.T("effet.Facture")));
    }

    public static string Exaucer(MondeSimule monde, int priere)
    {
        if (priere < 1) throw new ArgumentException(L.T("erreur.priere"));
        return L.Rediger(monde.Exaucer(priere));
    }

    public static string Refuser(MondeSimule monde, int priere, bool signe)
    {
        if (priere < 1) throw new ArgumentException(L.T("erreur.priere"));
        return L.Rediger(monde.Refuser(priere, signe));
    }

    /// <summary>Un geste sur un humain ou une communauté ; rend ce qui s'est passé, en mots.</summary>
    public static string Agir(MondeSimule monde, DemandeGeste demande)
    {
        if (demande is null) throw new ArgumentException(L.T("erreur.demandeVide"));
        var (geste, maniere, param) = GestesEntite.Lire(demande, monde.Pathogenes().Select(p => p.Id).ToHashSet());
        return L.Rediger(monde.Agir(geste, demande.Cible, maniere, param, demande.Force, demande.Jours));
    }

    /// <summary>Une parole à des humains ou aux membres de communautés ; rend ce que chacun en a fait.</summary>
    public static List<(int Humain, Recit Resultat)> Parler(MondeSimule monde, DemandeParole demande)
    {
        if (!Enum.TryParse<Parole>(demande.Parole, out var parole) || !Enum.IsDefined(parole) || parole == Parole.Penchant
            || !Enum.TryParse<ModeParole>(demande.Mode, out var mode) || !Enum.IsDefined(mode) || mode == ModeParole.Penchant)
            throw new ArgumentException(L.T("erreur.parole"));
        var (humainsIds, communautes) = Destinataires(demande.Humains, demande.Communautes);
        var (maintenant, humains) = monde.InstantaneHumains();
        var veutCible = Paroles.VeutCible(parole);
        if (veutCible && (demande.Cible < 1 || demande.Cible > humains.Count || !humains[demande.Cible - 1].EstVivant))
            throw new ArgumentException(L.T("erreur.humainVivant"));
        if (parole == Parole.UnisToi && humains[demande.Cible - 1].EstEnfant(maintenant))
            throw new ArgumentException(L.T("erreur.adulte"));
        var param = parole switch
        {
            Parole.PartsVers when demande.Param is >= 0 and <= 7 => demande.Param,
            Parole.PlanteDangereuse when demande.Param >= 1 && demande.Param <= monde.Flore.Especes.Count => demande.Param,
            Parole.NeVaPasALaGuerre when demande.Param >= 0 && demande.Param <= monde.Communautes().Count => demande.Param,
            Parole.PartsVers or Parole.PlanteDangereuse or Parole.NeVaPasALaGuerre => throw new ArgumentException(L.T("erreur.precision")),
            _ => 0,
        };
        return monde.Parler(humainsIds, communautes, parole, mode, veutCible ? demande.Cible : 0, param);
    }

    /// <summary>Une parole à toute une communauté ; rend ce que chaque adulte en a fait et, par communauté visée, ce que le groupe en fera.</summary>
    public static (List<(int Humain, Recit Resultat)> Membres, List<string> Groupe) ParlerACommunaute(MondeSimule monde, DemandeParoleCommunaute demande)
    {
        if (!Enum.TryParse<Parole>(demande.Parole, out var parole) || !Enum.IsDefined(parole) || !Paroles.Collective(parole)
            || !Enum.TryParse<ModeParole>(demande.Mode, out var mode) || !Enum.IsDefined(mode) || mode == ModeParole.Penchant)
            throw new ArgumentException(L.T("erreur.parole"));
        var communautes = monde.Communautes();
        if (demande.Communaute < 1 || demande.Communaute > communautes.Count || !communautes[demande.Communaute - 1].Active)
            throw new ArgumentException(L.T("erreur.communaute"));
        var cibles = (demande.Cibles ?? []).Distinct().Take(20).ToList();
        if (Paroles.VeutCommunaute(parole)
            && (cibles.Count == 0 || cibles.Any(c => c < 1 || c > communautes.Count || c == demande.Communaute || !communautes[c - 1].Active)))
            throw new ArgumentException(L.T("erreur.cibles"));
        if (parole == Parole.PartezVers && demande.Direction is < 0 or > 7) throw new ArgumentException(L.T("erreur.precision"));
        var (resultats, mandats) = monde.ParlerACommunaute(demande.Communaute, parole, mode, cibles, demande.Direction);
        var groupe = mandats.Select(m => L.T("mandat.resultat", ("c", m.CibleId > 0 ? L.CommunautePhrase(communautes[m.CibleId - 1].Nom) : L.T($"direction.{m.Direction}")),
                ("v", m.Veulent), ("hes", m.Hesitent), ("r", m.Refusent), ("i", m.Incredules + m.Incompris), ("date", L.Date(m.DebatLe))))
            .ToList();
        return (resultats, groupe);
    }

    /// <summary>Un penchant inspiré à des humains ; rend ce que chacun a ressenti.</summary>
    public static List<(int Humain, Recit Resultat)> Inspirer(MondeSimule monde, DemandePenchant demande)
    {
        var (humainsIds, communautes) = Destinataires(demande.Humains, demande.Communautes);
        var (_, humains) = monde.InstantaneHumains();
        if (demande.Cible != 0 && (demande.Cible < 1 || demande.Cible > humains.Count || !humains[demande.Cible - 1].EstVivant))
            throw new ArgumentException(L.T("erreur.humainVivant"));
        return monde.Inspirer(humainsIds, communautes, demande.Cible, demande.Mutuel, demande.Passion, demande.Orientation);
    }

    /// <summary>Destinataires validés : au moins un, 500 humains et 50 communautés au plus.</summary>
    public static (int[] Humains, int[] Communautes) Destinataires(int[]? humains, int[]? communautes)
    {
        humains ??= [];
        communautes ??= [];
        if (humains.Length + communautes.Length == 0 || humains.Length > 500 || communautes.Length > 50)
            throw new ArgumentException(L.T("erreur.destinataires"));
        return (humains.Distinct().ToArray(), communautes.Distinct().ToArray());
    }
}
