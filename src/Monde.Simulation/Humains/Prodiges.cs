using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce que les humains perçoivent d'un acte de l'Entité libre (0.35.2) : ceux qui en sont témoins en gardent le souvenir d'un
/// prodige ; leur croyance, leur amour, leur crainte ou leur révolte bougent selon les mêmes règles que pour un geste visible,
/// puis le récit gagne la communauté et les proches, plus faiblement.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Fait vivre un prodige à <paramref name="temoins"/>. <paramref name="cible"/> : l'humain que l'acte touche d'abord (null :
    /// une communauté ou un lieu) ; <paramref name="resume"/> : le prodige en quelques mots (texte de l'Entité). Rend le nombre
    /// d'humains qui le savent.
    /// </summary>
    internal int Prodige(IReadOnlyList<Humain> temoins, Humain? cible, int communaute, bool bienfait, string resume, long annonce,
        IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        var touches = new Dictionary<int, (Humain Qui, double Ampleur, bool Direct)>();
        foreach (var o in temoins)
            if (o.EstVivant && !o.EstBebe(maintenant)) touches[o.Id] = (o, 1, true);
        if (touches.Count == 0) return 0;
        // Le récit : la communauté, et les proches de la cible où qu'ils soient.
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.EstBebe(maintenant) || touches.ContainsKey(o.Id)) continue;
            var a = communaute != 0 && o.CommunauteId == communaute ? 0.4 : 0;
            if (cible is not null && (Famille(o, cible) || o.AffectionPour(cible.Id) >= 40)) a = Math.Max(a, 0.6);
            if (a > 0) touches[o.Id] = (o, a, false);
        }
        var (faute, culpabilite) = bienfait ? (Gestes.FauteAucune, 0.0) : cible is not null ? (FauteDe(cible, tous), 0.0) : Culpabilite(communaute, tous, maintenant);
        var reputation = cible is null || bienfait ? 0 : Reputation(cible, tous, maintenant);
        object nom = cible ?? (object?)Social?.Communautes.FirstOrDefault(c => c.Id == communaute) ?? Recit.Seul("reg.lesSiens");
        var court = resume.Length > 160 ? resume[..157].TrimEnd() + "…" : resume;
        foreach (var (o, a, direct) in touches.Values.OrderBy(t => t.Qui.Id))
        {
            var p = o.Personnalite;
            var autorite = p.Moral.Fondements.Autorite;
            var soi = cible?.Id == o.Id;
            var proche = cible is not null && !soi && (Famille(o, cible) || o.AffectionPour(cible.Id) >= 40);
            Croyances.Ajouter(o, (direct ? 10 : 4) * a, direct ? new Recit("libre.cro.aVu", ("h", o)) : Recit.Seul("cro.raconte"), annonce, maintenant, signe: direct);
            if (direct) Confier(o, TypeIntime.Emerveillement, cible?.Id ?? 0, new Recit("libre.js.prodige", ("r", court)), 50, annonce, maintenant, $"libre:prodige:{annonce}");
            var f = a * (0.4 + 0.6 * o.Croyance / 100);
            if (bienfait)
            {
                Regards.Ajouter(o, SentimentInvisible.Amour, 15 * f * (0.6 + 0.8 * p.Agreabilite), soi ? new Recit("reg.beniSoi", ("h", o)) : new Recit("reg.beni", ("x", nom)), annonce, maintenant, direct);
                continue;
            }
            var m = cible is not null ? Merite(o, cible, reputation) : MeriteCommunaute(o, culpabilite);
            if (m >= 0)
            {
                Regards.Ajouter(o, SentimentInvisible.Crainte, 15 * f * (0.4 + 0.6 * m) * (0.6 + 0.5 * p.Nevrosisme + 0.5 * autorite),
                    soi ? new Recit("reg.puniSoi", ("h", o)) : new Recit("reg.puni", ("x", nom)), annonce, maintenant, direct);
                continue;
            }
            var injuste = -m;
            Regards.Ajouter(o, SentimentInvisible.Crainte, 8 * f * (0.6 + 0.8 * p.Nevrosisme), new Recit("reg.frappe", ("x", nom)), annonce, maintenant, direct);
            Regards.Ajouter(o, SentimentInvisible.Amour, -15 * f * injuste * (1.3 - 0.6 * autorite), new Recit("reg.frappeInjuste", ("x", nom)), annonce, maintenant);
            Regards.Ajouter(o, SentimentInvisible.Revolte, 20 * f * injuste * Math.Max(0.1, 1.6 - p.Agreabilite - 0.6 * autorite) * (proche || soi ? 1.5 : 1),
                new Recit("reg.frappeInjuste", ("x", nom)), annonce, maintenant);
        }
        var vus = touches.Values.Count(t => t.Direct);
        var porteur = cible is { EstVivant: true } ? cible : touches.Values.Where(t => t.Direct).OrderBy(t => t.Qui.Id).First().Qui;
        Noter(maintenant, TypeEvenement.Croyance, porteur, null, new Recit("libre.temoins", ("n", vus), ("savent", touches.Count)), annonce);
        if (communaute != 0 && Cultes is { } cultes)
            cultes.Souvenirs.Add(new SouvenirEntite(communaute, new Recit("libre.souvenir", ("r", court)).Encoder(), maintenant, bienfait, 60, annonce, faute));
        return touches.Count;
    }
}
