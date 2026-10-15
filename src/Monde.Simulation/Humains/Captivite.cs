namespace Monde.Simulation.Humains;

/// <summary>
/// Les femmes enlevées lors des raids. Dans les guerres des sociétés sans État, les vainqueurs tuent surtout des hommes
/// et emmènent souvent des femmes plutôt que de les tuer (Keeley, 1996 ; chez les Yanomami, Chagnon, 1988 ; Bowles, 2009).
/// La captive suit celui qui l'a prise ; elle peut s'enfuir pour rejoindre les siens, ou finir par vivre parmi ses
/// ravisseurs quand elle s'y est attachée ou y a eu un enfant. Chaque étape est notée avec sa cause.
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>
    /// Une fois par jour, pour chaque captive : libre si son ravisseur est mort ; elle reste de son plein gré quand elle
    /// s'est attachée à quelqu'un de chez ses ravisseurs ou y a eu un enfant ; sinon elle peut s'enfuir, plus volontiers
    /// si elle est ouverte et résolue, moins si elle est anxieuse ou si les siens sont loin.
    /// </summary>
    public void Captivites(List<Humain> humains, long maintenant)
    {
        foreach (var h in humains)
        {
            if (h.CaptifDe == 0) continue;
            if (!h.EstVivant) { h.CaptifDe = 0; continue; }
            var ravisseur = humains[h.CaptifDe - 1];
            var chez = ravisseur.CommunauteId;
            if (!ravisseur.EstVivant)
            {
                Liberer(h, $"{ravisseur.Nom} est mort : {h.Nom}, qu'il avait enlevée, n'est plus captive.", ravisseur.MortEvenementId, maintenant);
                continue;
            }
            var attachee = h.Liens.Any(l => l.Affection >= 40 && l.Id != ravisseur.Id && humains[l.Id - 1] is { EstVivant: true } a
                                            && a.CommunauteId == chez && chez != 0)
                           || h.AffectionPour(ravisseur.Id) >= 50
                           || h.DernierAccouchement is { } n && n > h.CaptifLe;
            if (attachee)
            {
                var nom = chez != 0 && chez <= Communautes.Count ? $"la {Minuscule(Communautes[chez - 1].Nom)}" : $"les siens de {ravisseur.Nom}";
                Liberer(h, $"{h.Nom} ne cherche plus à fuir : elle vit désormais parmi {nom}.", 0, maintenant);
                continue;
            }
            var p = h.Personnalite;
            var fuite = 0.003 * (0.5 + p.Ouverture) * (1.4 - p.Nevrosisme) * (0.5 + h.Personnalite.Moral.Fondements.Loyaute);
            if (alea.Reel() < fuite)
                Liberer(h, $"{h.Nom} s'enfuit de chez {ravisseur.Nom}, qui l'avait enlevée, pour retrouver les siens.", 0, maintenant);
        }
    }

    private void Liberer(Humain h, string texte, long cause, long maintenant)
    {
        h.CaptifDe = 0;
        Noter(maintenant, TypeEvenement.Guerre, h, null, texte, cause, significatif: true);
        h.Memoire.Jardin.RemoveAll(x => x.Cle == "captive");
    }
}

public sealed partial class Comportement
{
    /// <summary>
    /// Chance qu'un guerrier qui a le dessus emmène une femme plutôt que de la tuer : bien plus souvent une jeune femme ;
    /// moins quand il est froid ou rancunier, plus quand il est compatissant.
    /// </summary>
    internal static double ChanceDEnlever(Humain guerrier, Humain femme, long maintenant)
    {
        var age = femme.AgeEnAnnees(maintenant);
        var p = guerrier.Personnalite;
        var base_ = age < 40 ? 0.7 : 0.3;
        return Math.Clamp(base_ * (1 - 0.6 * Psyche.Froideur(p)) + 0.25 * (p.Moral.Compassion - 0.5) - 0.15 * (p.Agreabilite < 0.3 ? 1 : 0), 0.05, 0.95);
    }

    private void Enlever(Humain captive, Humain ravisseur, Communaute c, Communaute cible, long cause, List<Humain> humains, long maintenant)
    {
        if (!captive.EstVivant) return;
        captive.CaptifDe = ravisseur.Id;
        captive.CaptifLe = maintenant;
        var id = Noter(maintenant, TypeEvenement.Guerre, ravisseur, captive,
            $"{ravisseur.Nom} enlève {captive.Nom} ({AgeEnMots(captive, maintenant)}) lors du raid de la {Minuscule(c.Nom)} contre la {Minuscule(cible.Nom)}.",
            cause, significatif: true);
        Jardin.Confier(captive, TypeIntime.Peine, ravisseur.Id, $"{ravisseur.Nom} m'a arrachée aux miens. Je veux les revoir.",
            60 + 30 * captive.Personnalite.Nevrosisme, id, maintenant, "captive");
        Jardin.Confier(ravisseur, TypeIntime.Joie, captive.Id, $"J'ai ramené {captive.Nom} de chez la {Minuscule(cible.Nom)}.",
            30 + 30 * (1 - ravisseur.Personnalite.Agreabilite), id, maintenant, $"captive:{captive.Id}");
        // Ses proches en gardent la rancune, comme d'un meurtre, un peu moins fort.
        Social!.Offenser(ravisseur, captive, 10, id, humains, maintenant);
    }
}
