using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.22.3 : une communauté ne se défait plus en quelques semaines (rester, s'absenter, se retrouver).</summary>
public sealed class CommunautesStablesTests
{
    private const long Jour = Calendrier.SecondesParJour;
    private const long Maintenant = 30 * Calendrier.SecondesParAn;

    /// <summary>Six adultes amis, au même endroit.</summary>
    private static List<Humain> Amis(int nombre = 6)
    {
        var tous = Enumerable.Range(1, nombre).Select(i => VitesseTests.Humain(i)).ToList();
        foreach (var h in tous) (h.X, h.Y) = (50 + h.Id % 2, 50);
        foreach (var a in tous)
        {
            foreach (var b in tous.Where(b => b != a)) a.Liens.Add(new Lien(b.Id, 60, 60, Maintenant, Amis: true));
        }
        return tous;
    }

    private static void Regrouper(VieSociale social, List<Humain> tous, List<Communaute> communautes, long t) =>
        social.Regrouper(tous, communautes, t, new HashSet<string>());

    [Fact]
    public void DeuxAdultesQuiVivent_Ensemble_GardentLeurCommunaute()
    {
        var social = new VieSociale(new Alea(1));
        var tous = Amis();
        var communautes = new List<Communaute>();
        Regrouper(social, tous, communautes, Maintenant);
        var c = Assert.Single(communautes);

        // Quatre meurent : les deux survivants restent la même communauté (il en faut cinq pour la fonder, pas pour la garder).
        foreach (var h in tous.Take(4)) h.MortLe = Maintenant + Jour;
        Regrouper(social, tous, communautes, Maintenant + 7 * Jour);
        Assert.True(c.Active);
        Assert.All(tous.Skip(4), h => Assert.Equal(c.Id, h.CommunauteId));

        // Seul, le dernier n'est plus une communauté.
        tous[4].MortLe = Maintenant + 8 * Jour;
        Regrouper(social, tous, communautes, Maintenant + 14 * Jour);
        Assert.False(c.Active);
        Assert.Equal(0, tous[5].CommunauteId);
        Assert.Equal(c.Id, tous[5].AncienneCommunauteId);
    }

    [Fact]
    public void QuatreAdultes_NeFondentPasDeCommunaute()
    {
        var social = new VieSociale(new Alea(1));
        var tous = Amis(4);
        var communautes = new List<Communaute>();
        Regrouper(social, tous, communautes, Maintenant);
        Assert.Empty(communautes);
    }

    [Fact]
    public void QuiSEloigneUnTemps_ResteDesSiens_PuisLesQuitte()
    {
        var social = new VieSociale(new Alea(1));
        var tous = Amis();
        var communautes = new List<Communaute>();
        Regrouper(social, tous, communautes, Maintenant);
        var c = Assert.Single(communautes);

        // Parti explorer à une vingtaine de cases depuis une semaine : il est toujours des leurs.
        var parti = tous[0];
        parti.X += 20;
        Regrouper(social, tous, communautes, Maintenant + 7 * Jour);
        Assert.Equal(c.Id, parti.CommunauteId);

        // Plus de quatre mois sans les revoir (le plus long délai possible) : il n'en est plus.
        Assert.True(VieSociale.AbsenceToleree(parti, tous[1], parti.Liens[0]) <= 18 * 7 * Jour);
        Regrouper(social, tous, communautes, Maintenant + 19 * 7 * Jour);
        Assert.Equal(0, parti.CommunauteId);
        Assert.True(c.Active);
    }

    [Fact]
    public void AbsenceToleree_DUneSemaineAQuatreMois_SelonLaLoyauteEtLAffection()
    {
        var (a, b) = (VitesseTests.Humain(1), VitesseTests.Humain(2));
        var froid = VieSociale.AbsenceToleree(a, b, new Lien(2, 30, -20, 0));
        var chaud = VieSociale.AbsenceToleree(a, b, new Lien(2, 30, 90, 0));
        Assert.True(froid < chaud);
        Assert.InRange(froid, 7 * Jour, 18 * 7 * Jour);
        Assert.InRange(chaud, 7 * Jour, 18 * 7 * Jour);
    }

    [Fact]
    public void DesAnciensQuiSeRetrouvent_FontRenaitreLeurCommunaute()
    {
        var social = new VieSociale(new Alea(1));
        var tous = Amis();
        var communautes = new List<Communaute>();
        Regrouper(social, tous, communautes, Maintenant);
        var c = Assert.Single(communautes);

        // Chacun part de son côté, longtemps : la communauté se disperse.
        foreach (var h in tous) h.X = 10 + 15 * h.Id;
        Regrouper(social, tous, communautes, Maintenant + 26 * 7 * Jour);
        Assert.False(c.Active);
        Assert.All(tous, h => Assert.Equal(c.Id, h.AncienneCommunauteId));

        // Ils se retrouvent : c'est elle qui renaît, pas une nouvelle.
        foreach (var h in tous) (h.X, h.Y) = (50 + h.Id % 2, 50);
        Regrouper(social, tous, communautes, Maintenant + 27 * 7 * Jour);
        Assert.Single(communautes);
        Assert.True(c.Active);
        Assert.All(tous, h => Assert.Equal((c.Id, 0), (h.CommunauteId, h.AncienneCommunauteId)));
        Assert.Contains(social.Journal, j => j.Texte.Contains("la communauté renaît"));
    }
}
