using Monde.Simulation.Nature;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'on apprend d'un incendie parti d'un camp. Ceux qui en ont souffert, et celui qui avait la garde du feu, cherchent ce qui
/// l'a fait partir, comme pour les maux de ventre : le feu laissé sans personne, le vent, l'herbe trop près. Une imprudence n'est
/// pas un meurtre : aux yeux de l'Entité, c'est une petite faute, plus lourde si le gardien savait et n'a pas pris garde, et elle ne
/// compte que si l'incendie a fait du mal. Les proches des victimes, eux, lui en veulent selon leur caractère. Un incendie qui a tué
/// reste dans la mémoire du groupe.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Ce que coûte, aux yeux de l'Entité, un feu mal gardé qui a blessé ou tué (de −0,5 à −3 selon la négligence).</summary>
    internal static double PointsImprudence(double negligence, bool mort) =>
        -Math.Clamp((0.5 + 2.5 * negligence) * (mort ? 1 : 0.6), 0.5, 3);

    /// <summary>Les suites d'un incendie parti d'un camp, une fois éteint (appelé hors du pas, une fois).</summary>
    internal void ApresLeFeuDeCamp(Catastrophe c, IReadOnlyList<Humain> humains, long maintenant)
    {
        _maintenant = maintenant;
        _tous = humains;
        var gardien = c.ResponsableId > 0 && c.ResponsableId <= humains.Count && humains[c.ResponsableId - 1] is { EstVivant: true } g ? g : null;
        var touches = new HashSet<int>();
        var mort = false;
        foreach (var v in c.Victimes.Order())
        {
            var victime = humains[v - 1];
            mort |= !victime.EstVivant;
            if (gardien is null || v == gardien.Id) continue;
            foreach (var k in Proches(victime, humains))
            {
                if (k.Id == gardien.Id) continue;
                touches.Add(k.Id);
                var colere = Math.Min(100, (20 + 50 * c.Negligence) * (1.2 - k.Personnalite.Agreabilite) * (victime.EstVivant ? 0.5 : 1));
                if (colere < 5) continue;
                VieSociale.AjouterGrief(k, new Grief(gardien.Id, MotifGrief.Incendie, colere, colere, c.Id, v));
                Social?.ChangerAffection(k, gardien, -colere / 4, maintenant);
            }
        }
        if (gardien is not null && c.Victimes.Count > 0)
        {
            var points = PointsImprudence(c.Negligence, mort);
            var faute = Noter(maintenant, TypeEvenement.Catastrophe, gardien, null,
                new Recit(mort ? "feu.faute.tue" : "feu.faute.blesse", ("h", gardien), ("v", Victimes(c.Victimes.Count))),
                c.Id, karma: points);
            var p = gardien.Personnalite;
            var remords = 100 * Math.Clamp(0.5 * p.Moral.EmpathieEmotionnelle + 0.3 * p.Conscience + 0.2 * p.Moral.Compassion, 0, 1) * (1 - Psyche.Froideur(p));
            if (remords >= 25)
                Jardin.Confier(gardien, TypeIntime.Culpabilite, 0, Recits.Recit.Seul(mort ? "js.feu.tue" : "js.feu.brules"),
                    remords, faute, maintenant, $"incendie:{c.Id}");
        }
        // Les leçons : le gardien, les proches des victimes, puis ceux qui ont vu.
        var candidats = new List<(Humain H, double Poids)>();
        if (gardien is not null) candidats.Add((gardien, 1));
        foreach (var id in touches.Order()) candidats.Add((humains[id - 1], 1));
        foreach (var id in c.Temoins.Order())
        {
            if (id == gardien?.Id || touches.Contains(id)) continue;
            if (humains[id - 1] is { EstVivant: true } t) candidats.Add((t, c.Victimes.Contains(id) ? 1 : 0.4));
        }
        var dommage = c.Victimes.Count > 0 ? 1 : 0.5;
        foreach (var (h, poids) in candidats)
        {
            if (!h.EstVivant || h.EstEnfant(maintenant) || h.Sait(c.Lecon)) continue;
            if (alea.Reel() >= (0.15 + 0.6 * Ingeniosite(h)) * poids * dommage) continue;
            var recit = new Recit(c.Lecon is Effet.EteindreEnPartant or Effet.PasDansLeVent ? $"feu.lecon.{c.Lecon}" : "feu.lecon.FoyerDegage", ("h", h));
            var geste = c.Lecon switch { Effet.EteindreEnPartant => Geste.Eteindre, Effet.PasDansLeVent => Geste.Patienter, _ => Geste.Degager };
            Retenir(h, geste, Matiere.Aucune, Matiere.Feu, c.Lecon, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, humains, recit: recit);
        }
        // Un incendie qui a tué : le groupe s'en souviendra.
        if (!mort || Social is not { } social) return;
        foreach (var (communaute, morts) in c.PertesParCommunaute)
        {
            if (communaute <= 0 || communaute > social.Communautes.Count || social.Communautes[communaute - 1] is not { Active: true } co) continue;
            if (co.IncendiesSouvenus.Contains(c.Id)) continue;
            co.IncendiesSouvenus.Add(c.Id);
            if (co.IncendiesSouvenus.Count > 5) co.IncendiesSouvenus.RemoveAt(0);
            Journal.Add(new EvenementJournal(maintenant, TypeEvenement.Catastrophe, gardien?.Id ?? 0, 0,
                new Recit("feu.souvenir", ("c", co), ("v", Victimes(morts))))
            {
                Id = Numeros.Suivant(), CauseId = c.Id, CommunauteId = co.Id, Significatif = true,
            });
        }
    }

    /// <summary>« un des leurs », « trois des leurs ».</summary>
    private static Recit Victimes(int n) => n switch
    {
        1 => Recits.Recit.Seul("feu.victimes.un"),
        2 or 3 => new Recit("feu.victimes", ("n", Recits.Recit.Seul($"chiffre.{n}"))),
        _ => new Recit("feu.victimes", ("n", n)),
    };

    /// <summary>Ceux qui pleurent ou soignent une victime : son compagnon, ses parents, ses enfants, ses frères et sœurs, ses amis.</summary>
    private static IEnumerable<Humain> Proches(Humain v, IReadOnlyList<Humain> humains)
    {
        var vus = new HashSet<int>();
        foreach (var l in v.Liens)
        {
            var o = humains[l.Id - 1];
            if (!o.EstVivant || o.EstBebe(_maintenant)) continue;
            if (l.Affection >= 40 || o.Id == v.PartenaireId || SontParents(o, v)) { if (vus.Add(o.Id)) yield return o; }
        }
        foreach (var id in new[] { v.PartenaireId, v.MereId, v.PereId })
        {
            if (id <= 0 || id > humains.Count || humains[id - 1] is not { EstVivant: true } o) continue;
            if (vus.Add(o.Id)) yield return o;
        }
    }

    /// <summary>
    /// Un incendie s'est arrêté net au bord de terres déjà brûlées : ceux qui l'ont vu peuvent comprendre que le feu arrête le feu.
    /// </summary>
    internal void VoirLeFeuSArreter(Catastrophe c, IReadOnlyList<Humain> humains, long maintenant)
    {
        _maintenant = maintenant;
        _tous = humains;
        foreach (var id in c.Temoins.Order())
        {
            var h = humains[id - 1];
            if (!h.EstVivant || h.EstEnfant(maintenant) || h.Sait(Effet.BrulisProtecteur)) continue;
            if (alea.Reel() >= (0.05 + 0.25 * Ingeniosite(h)) * (0.3 + h.Personnalite.Ouverture)) continue;
            Retenir(h, Geste.Incendier, Matiere.HerbeSeche, Matiere.Feu, Effet.BrulisProtecteur, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, humains,
                recit: new Recit("feu.brulisProtecteur", ("h", h)));
        }
    }

    /// <summary>Il vient de brûler autour du camp : le savoir s'exerce, et ceux qui étaient là peuvent l'apprendre.</summary>
    internal void AvoirBruleAutourDuCamp(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        _maintenant = maintenant;
        _tous = humains;
        Retenir(h, Geste.Incendier, Matiere.HerbeSeche, Matiere.Feu, Effet.BrulisProtecteur, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, humains);
        Montrer(h, Geste.Incendier, Matiere.HerbeSeche, Matiere.Feu, Effet.BrulisProtecteur, 0, Symptome.Aucun, humains, 0);
    }
}
