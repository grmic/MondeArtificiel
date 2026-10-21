using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Tests;

/// <summary>
/// 0.29.0 : le feu gardé, les feux de camp et les réserves respectent le calcul sur plusieurs cœurs. Tous les adultes savent
/// d'emblée allumer, garder et porter le feu, sécher, amasser et cacher, pour que ces gestes se produisent souvent.
/// La durée se règle par MONDE_GARDE_JOURS (60 jours par défaut).
/// </summary>
public sealed class GardeFeuTests
{
    private static readonly int Jours = int.TryParse(Environment.GetEnvironmentVariable("MONDE_GARDE_JOURS"), out var j) ? j : 60;

    private static readonly (Geste G, Matiere A, Matiere B, Effet E)[] Savoirs =
    [
        (Geste.Frotter, Matiere.BoisSec, Matiere.BoisSec, Effet.Flamme), (Geste.Entasser, Matiere.BoisSec, Matiere.Feu, Effet.Foyer),
        (Geste.Approcher, Matiere.Branche, Matiere.Feu, Effet.Brandon), (Geste.Couvrir, Matiere.Aucune, Matiere.Feu, Effet.Braises),
        (Geste.Approcher, Matiere.Corde, Matiere.Feu, Effet.PorteFeu), (Geste.Degager, Matiere.Aucune, Matiere.Feu, Effet.FoyerDegage),
        (Geste.Patienter, Matiere.Aucune, Matiere.Feu, Effet.PasDansLeVent), (Geste.Eteindre, Matiere.Aucune, Matiere.Feu, Effet.EteindreEnPartant),
        (Geste.Incendier, Matiere.HerbeSeche, Matiere.Feu, Effet.BrulisProtecteur), (Geste.Cacher, Matiere.Calebasse, Matiere.Aucune, Effet.CacheDEau),
        (Geste.Secher, Matiere.Carcasse, Matiere.Aucune, Effet.Sechage), (Geste.Amasser, Matiere.Plante, Matiere.Aucune, Effet.Provisions),
        (Geste.Cacher, Matiere.Carcasse, Matiere.Aucune, Effet.CacheFroide), (Geste.Fumer, Matiere.Carcasse, Matiere.Feu, Effet.Fumage),
        (Geste.Remplir, Matiere.Peau, Matiere.Eau, Effet.EauPortee),
    ];

    private static MondeSimule NouveauMonde(int coeurs)
    {
        var etat = new MondeSimule(MondeSimuleTests.Carte42, MondeSimule.Nouveau(42)).Etat;
        foreach (var h in etat.Humains!)
        {
            foreach (var (g, a, b, e) in Savoirs) h.Savoirs.Add(new Savoir(g, a, b, e, 0, Symptome.Aucun, 10, 9, 0, 0, OrigineSavoir.Essai, 0, 0));
            if (h.Id % 3 == 0) h.Objets.Add(new Objet(Matiere.Calebasse, 0.8, 1) { Plein = true });
            if (h.Id % 4 == 0) h.Objets.Add(new Objet(Matiere.Corde, 0.6, 1));
        }
        return new MondeSimule(MondeSimuleTests.Carte42, etat) { Coeurs = coeurs };
    }

    private static string Feux(MondeSimule monde)
    {
        var etat = monde.Etat;
        return $"{string.Join(";", etat.Foyers ?? [])}\n{string.Join(";", etat.Braises ?? [])}\n{string.Join(";", etat.Reserves ?? [])}\n"
               + string.Join(";", etat.Humains!.Select(h => $"{h.Id}:{h.FeuxPortes}:{h.PorteurDuFeuDepuis}:{h.FeuDemandeLe}:{string.Join(",", h.Objets)}:{h.Karma:R}"))
               + "\n" + string.Join(";", (etat.Communautes ?? []).Select(c => $"{c.Id}:{c.IdeeReserve}:{c.PartagesReserve:R}:{c.VolsReserve:R}:{string.Join(",", c.IncendiesSouvenus)}"));
    }

    private static string Journal(MondeSimule monde) =>
        string.Join("\n", monde.Etat.JournalNonEnregistre!.Select(e => $"{e.Id} {e.CauseId} {e.HumainId} {e.AutreId} {e.Karma:R} {e.Texte}"));

    [Fact]
    public void FeuEtReserves_DeuxCoeursOuQuatre_MemeHistoire()
    {
        var deux = NouveauMonde(2);
        var quatre = NouveauMonde(4);
        deux.JournalQuotidien = quatre.JournalQuotidien = true;
        deux.AvancerDe(Jours * Calendrier.SecondesParJour);
        quatre.AvancerDe(Jours * Calendrier.SecondesParJour);
        Assert.Equal(HumainsTests.Empreinte(deux), HumainsTests.Empreinte(quatre));
        Assert.Equal(Feux(deux), Feux(quatre));
        Assert.Equal(Journal(deux), Journal(quatre));
        // Les gestes du feu et des réserves se sont bien produits.
        var journal = Journal(deux);
        Assert.True(deux.Etat.Foyers is { Count: > 0 } || deux.Etat.Braises is { Count: > 0 }, "aucun feu");
    }

    [Fact]
    public void FeuEtReserves_PendantLesTours_LeMondeNeChangePas()
    {
        var monde = NouveauMonde(4);
        string? avant = null;
        var ecarts = 0;
        monde.Surveillant = debut =>
        {
            var empreinte = HumainsTests.Empreinte(monde, listesPropres: false) + "\n" + Feux(monde) + "\n" + Journal(monde);
            if (debut) avant = empreinte;
            else if (avant != empreinte) ecarts++;
        };
        monde.AvancerDe(Math.Min(Jours, 20) * Calendrier.SecondesParJour);
        Assert.Equal(0, ecarts);
    }
}
