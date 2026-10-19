using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Nourrir les mères. Porter un enfant puis l'allaiter coûte à une femme un quart d'énergie en plus, et ses réserves
/// fondent si elle seule s'en charge. Chez les chasseurs-cueilleurs, d'autres pourvoient : le compagnon rapporte davantage
/// quand sa femme allaite (Marlowe, 2003, chez les Hadza), la grand-mère maternelle nourrit sa fille et ses petits-enfants
/// sevrés, ce qui raccourcit l'intervalle entre les naissances (Hawkes et al., 1998), et la famille comme le groupe y
/// contribuent (Hrdy, 2009 ; Kaplan et al., 2000). Rien n'y oblige : chacun donne selon son attachement, sa générosité et
/// ce qu'il a lui-même.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Part de nourriture qu'un proche très disposé donne à une mère, par pas (un dixième de ce qu'elle dépense en une heure).</summary>
    private const double DonParPasALaMere = 0.2;

    /// <summary>
    /// Une femme enceinte ou qui allaite reçoit de quoi manger du proche le plus disposé qui se trouve à côté d'elle et
    /// a moins faim qu'elle : de quoi revenir à une faim légère, sous laquelle son corps refait ses réserves.
    /// </summary>
    private void NourrirLaMere(Humain m, IReadOnlyList<Humain> tous)
    {
        if (m.Sexe != Sexe.Femme || m.Faim < 25 || !EstMere(m, tous)) return;
        Humain? donneur = null;
        double meilleure = 0;
        Retenir(Vivant(tous, m.PartenaireId), m, ref donneur, ref meilleure);
        Retenir(Vivant(tous, m.MereId), m, ref donneur, ref meilleure);
        foreach (var l in m.Liens) Retenir(tous[l.Id - 1], m, ref donneur, ref meilleure);
        if (donneur is null) return;
        Donner(donneur, m, Math.Min(m.Faim - 20, DonParPasALaMere * meilleure));
    }

    /// <summary>Enceinte, ou allaitant un enfant de moins de trois ans qui tète encore.</summary>
    private bool EstMere(Humain m, IReadOnlyList<Humain> tous) =>
        m.EstEnceinte || m.DernierAccouchement is { } d && _maintenant - d < 3 * Calendrier.SecondesParAn && Allaitement(m, tous, _maintenant) < 1;

    private void Retenir(Humain? d, Humain m, ref Humain? donneur, ref double meilleure)
    {
        if (d is null || d.Id == donneur?.Id) return;
        var envie = EnvieDeNourrir(d, m, _maintenant);
        if (envie > meilleure) (donneur, meilleure) = (d, envie);
    }

    private void Donner(Humain d, Humain m, double donne)
    {
        if (donne <= 0) return;
        m.Faim -= donne;
        // Comme pour tout partage, celui qui donne a cueilli ou chassé un peu plus qu'il ne mange.
        Tour.Changer(d, x => x.Faim = Math.Min(100, x.Faim + 0.6 * donne));
        VieSociale.Gratitude(d, m, 0.02);
        Social?.Partager(d, m);
    }

    /// <summary>
    /// Envie de <paramref name="d"/> de nourrir la mère <paramref name="m"/>, de 0 à 1 : le compagnon selon son attachement,
    /// sa mère surtout, son père, ses frères et sœurs, puis les siens de la communauté selon leur loyauté ; l'affection
    /// ajoute, l'inimitié retient ; la générosité module, et un cœur froid ne donne guère.
    /// </summary>
    internal static double EnvieDeNourrir(Humain d, Humain m, long maintenant)
    {
        if (!d.EstVivant || d.Id == m.Id || d.EstEnfant(maintenant) || d.Activite == Activite.Dormir || d.EstDeprime || d.CaptifDe != 0) return 0;
        if (d.Faim >= 50 || d.Faim > m.Faim - 10 || Distance(d.X, d.Y, m.X, m.Y) > 1) return 0;
        var p = d.Personnalite;
        double parente;
        if (d.Id == m.PartenaireId) parente = 0.4 + 0.4 * p.Attachement;
        else if (d.Id == m.MereId) parente = 0.6;
        else if (d.Id == m.PereId) parente = 0.4;
        else if (m.MereId != 0 && d.MereId == m.MereId) parente = 0.3;
        else if (d.CommunauteId != 0 && d.CommunauteId == m.CommunauteId) parente = 0.15 * (0.5 + p.Moral.Fondements.Loyaute);
        else return 0;
        var generosite = (0.5 * p.Agreabilite + 0.25 * p.Moral.Compassion + 0.25 * p.Moral.Honnetete) * (1 - 0.8 * Psyche.Froideur(p));
        return Math.Clamp(parente + 0.4 * d.AffectionPour(m.Id) / 100, 0, 1) * (0.5 + 0.5 * generosite);
    }
}
