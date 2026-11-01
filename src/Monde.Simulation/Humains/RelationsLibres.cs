using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Relations entre communautés imposées par l'Entité libre (0.35.2), et leur retour en arrière.</summary>
public sealed partial class VieSociale
{
    /// <summary>
    /// Impose l'état <paramref name="etat"/> (guerre, paix, alliance, hostiles, indifferentes) entre <paramref name="a"/> et
    /// <paramref name="b"/> ; rend l'état d'avant et 1 si une guerre a été ouverte. Ensuite, les groupes vivent selon les règles :
    /// une guerre imposée se poursuit par des raids, une paix peut se rompre.
    /// </summary>
    internal (EtatRelation Etat, double GuerreCreee) ImposerRelation(Communaute a, Communaute b, string etat, long evenement, List<Humain> humains, long maintenant)
    {
        var cle = Paire(a.Id, b.Id);
        var avant = _relations.GetValueOrDefault(cle);
        var creee = 0.0;
        switch (etat)
        {
            case "guerre":
                if (GuerreEntre(a.Id, b.Id) is null) creee = 1;
                Ensanglanter(a, b, 0, 0, evenement, maintenant);
                _pactes.Remove(cle);
                break;
            case "paix":
                if (GuerreEntre(a.Id, b.Id) is { } g)
                {
                    g.Issue = IssueGuerre.Paix;
                    g.Fin = maintenant;
                    g.FinEvenementId = evenement;
                }
                FaireLaPaix(a, b, humains, evenement);
                break;
            case "alliance":
                if (GuerreEntre(a.Id, b.Id) is { } g2)
                {
                    g2.Issue = IssueGuerre.Paix;
                    g2.Fin = maintenant;
                    g2.FinEvenementId = evenement;
                }
                _relations[cle] = EtatRelation.Alliees;
                _debutsRelations[cle] = evenement;
                _pactes[cle] = evenement;
                break;
            case "hostiles":
                _relations[cle] = EtatRelation.Hostiles;
                _debutsRelations[cle] = evenement;
                _pactes.Remove(cle);
                break;
            default:
                _relations.Remove(cle);
                _debutsRelations.Remove(cle);
                _pactes.Remove(cle);
                break;
        }
        return (avant, creee);
    }

    /// <summary>Remet l'état d'avant ; une guerre ouverte par l'acte et encore en cours se clôt en paix.</summary>
    internal void RetablirRelation(Communaute a, Communaute b, EtatRelation avant, bool guerreCreee, long maintenant)
    {
        var cle = Paire(a.Id, b.Id);
        if (guerreCreee && GuerreEntre(a.Id, b.Id) is { } g)
        {
            g.Issue = IssueGuerre.Paix;
            g.Fin = maintenant;
        }
        if (avant != EtatRelation.Alliees) _pactes.Remove(cle);
        if (avant == EtatRelation.Indifferentes) _relations.Remove(cle);
        else _relations[cle] = avant;
    }
}

public sealed partial class Comportement
{
    /// <summary>L'Entité libre fait mourir <paramref name="h"/> (la ligne du journal dit comment).</summary>
    internal void TuerLibre(Humain h, long maintenant, CauseDeMort cause, Recit texte, long annonce) => Tuer(h, maintenant, cause, texte, annonce);

    /// <summary>L'Entité libre guérit <paramref name="h"/> de tout.</summary>
    internal void GuerirLibre(Humain h, long maintenant)
    {
        _maintenant = maintenant;
        GuerirDeTout(h, maintenant);
    }
}
