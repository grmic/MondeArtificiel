using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>0.22.1 : tous les humains vivent chaque pas en même temps, sur plusieurs cœurs, sans que le résultat en dépende.</summary>
public sealed class PasSimultaneTests
{
    private static MondeSimule NouveauMonde(int coeurs) => new(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42)) { Coeurs = coeurs };

    private static string Journal(MondeSimule monde) =>
        string.Join("\n", monde.Etat.JournalNonEnregistre!.Select(e => $"{e.Id} {e.CauseId} {e.HumainId} {e.AutreId} {e.Karma:R} {e.Texte}"));

    [Fact]
    public void UnCoeurOuPlusieurs_MemeHistoire()
    {
        var seul = NouveauMonde(1);
        var plusieurs = NouveauMonde(4);
        seul.AvancerDe(40 * Calendrier.SecondesParJour);
        plusieurs.AvancerDe(40 * Calendrier.SecondesParJour);
        Assert.Equal(HumainsTests.Empreinte(seul), HumainsTests.Empreinte(plusieurs));
        Assert.Equal(Journal(seul), Journal(plusieurs));
        Assert.NotEmpty(seul.Etat.JournalNonEnregistre!);
    }

    [Fact]
    public void PendantLesTours_LeMondeNeChangePas()
    {
        // Chacun agit sur sa copie ; ce qu'il fait aux autres et au monde commun attend la fin du pas.
        // Seules exceptions : ses lieux connus et ses pensées, que lui seul lit et écrit, sont partagés avec sa copie.
        var monde = NouveauMonde(4);
        string? avant = null;
        var pas = 0;
        monde.Surveillant = debut =>
        {
            var empreinte = HumainsTests.Empreinte(monde, listesPropres: false) + "\n" + Journal(monde);
            if (debut) avant = empreinte;
            else
            {
                Assert.Equal(avant, empreinte);
                pas++;
            }
        };
        monde.AvancerDe(5 * Calendrier.SecondesParJour);
        Assert.Equal(5 * 144, pas);
    }

    [Fact]
    public void LesNumerosDUnPasSontEntrelacesSansChevauchement()
    {
        var a = VitesseTests.Humain(1);
        var t0 = new Tour(a, a, 1, 100, 0, 3);
        var t2 = new Tour(a, a, 2, 100, 2, 3);
        Assert.Equal([100, 103, 106], [t0.NouveauNumero(), t0.NouveauNumero(), t0.NouveauNumero()]);
        Assert.Equal([102, 105], [t2.NouveauNumero(), t2.NouveauNumero()]);
        Assert.Equal(3, t0.NumerosPris);
    }

    [Fact]
    public void HorsDUnTour_LesEffetsSAppliquentAussitot()
    {
        var x = 0;
        Tour.Differer(() => x++);
        Assert.Equal(1, x);
        Assert.False(Tour.EnCours);
        Assert.False(Tour.Autrui(VitesseTests.Humain(1)));
    }
}
