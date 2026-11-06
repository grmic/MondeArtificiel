using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les commandements de l'Entité au Monde (0.37.1) : « Ne tuez pas », « Partagez », « Prenez soin des faibles ». Ils durent chez
/// ceux qui les ont acceptés (une consigne, voir <see cref="Impulsion"/>), se racontent aux enfants, et s'oublient quand plus
/// personne ne les raconte. Une norme se transmet surtout par les parents et les proches, et s'éteint sans eux (Henrich, 2016 ;
/// Bisin et Verdier, 2001, sur la transmission verticale des valeurs).
/// </summary>
public sealed partial class Comportement
{
    private static readonly Parole[] Commandements = [Parole.NeTuezPas, Parole.PartagezTous, Parole.PrenezSoinDesFaibles];

    /// <summary>
    /// Chaque jour : ceux qui suivent un commandement partagent avec qui a faim, veillent sur un faible tout près, et le racontent
    /// à leurs enfants. Rien ne se passe (ni hasard tiré) tant que personne ne suit de commandement.
    /// </summary>
    public void VivreLesCommandements(IReadOnlyList<Humain> humains, long maintenant)
    {
        _maintenant = maintenant;
        List<(Humain H, Impulsion I)>? fideles = null;
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.Impulsions.Count == 0 || h.EstEnfant(maintenant) && h.AgeEnAnnees(maintenant) < 8) continue;
            foreach (var p in Commandements)
                if (h.Consigne(p, maintenant) is { } i) (fideles ??= []).Add((h, i));
        }
        if (fideles is null) return;
        foreach (var (h, i) in fideles)
        {
            // Une fois par semaine environ, chacun selon la force qu'il donne au commandement.
            if (alea.Reel() >= i.Force / 7) continue;
            switch (i.Parole)
            {
                case Parole.PartagezTous when h.Faim < 45:
                {
                    var affame = Proche(h, humains, maintenant, o => o.Faim >= 60, o => o.Faim);
                    if (affame is null) break;
                    var don = Math.Min(i.Reponse == Reponse.AMoitie ? 10 : 25, 70 - h.Faim);
                    Tour.Changer(affame, x => x.Faim = Math.Max(0, x.Faim - don));
                    h.Faim += 0.7 * don;
                    VieSociale.Gratitude(h, affame, 0.3);
                    Noter(maintenant, TypeEvenement.Partage, h, affame, new Recit("cmd.partage", ("h", h), ("x", affame)), i.EvenementId, karma: Regard.Partage);
                    break;
                }
                case Parole.PrenezSoinDesFaibles:
                {
                    // Le plus faible tout près : un malade, un blessé, un vieillard affamé, un enfant seul qui a faim.
                    var faible = Proche(h, humains, maintenant,
                        o => (o.EstMalade(maintenant) || o.InfectionId != 0 || o.BlessureGrave > 0) && o.Sante < 70
                             || o.AgeEnAnnees(maintenant) >= 60 && o.Faim >= 50 || o.EstEnfant(maintenant) && o.Faim >= 60,
                        o => 100 - o.Sante + o.Faim / 2);
                    if (faible is null || h.EstMalade(maintenant)) break;
                    var gain = (i.Reponse == Reponse.AMoitie ? 1 : 2) + 4 * h.Personnalite.Moral.Compassion;
                    Tour.Changer(faible, x =>
                    {
                        x.Sante = Math.Min(100, x.Sante + gain);
                        if (x.Faim >= 50 && h.Faim < 50) x.Faim -= 15;
                    });
                    Social?.ChangerAffection(faible, h, 4, maintenant);
                    Noter(maintenant, TypeEvenement.Entraide, h, faible, new Recit("cmd.soigne", ("h", h), ("x", faible)), i.EvenementId, karma: Regard.Veiller);
                    break;
                }
            }
            Transmettre(h, i, humains, maintenant);
        }
    }

    /// <summary>Le plus pressant de ceux qui sont tout près (une case) et qui en ont besoin, ou null.</summary>
    private static Humain? Proche(Humain h, IReadOnlyList<Humain> humains, long maintenant, Func<Humain, bool> besoin, Func<Humain, double> urgence)
    {
        Humain? meilleur = null;
        foreach (var o in humains.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || Distance(o.X, o.Y, h.X, h.Y) > 1 || !besoin(o)) continue;
            if (meilleur is null || urgence(o) > urgence(meilleur)) meilleur = o;
        }
        return meilleur;
    }

    /// <summary>
    /// Raconter le commandement : d'abord à un de ses enfants de 5 à 15 ans, sinon à un proche de sa communauté qui l'aime. Celui
    /// qui ne le suit pas encore le reçoit plus ou moins fort selon qu'il aime celui qui raconte (un adulte moins qu'un enfant :
    /// transmission verticale et oblique, Cavalli-Sforza et Feldman, 1981) ; celui qui le suit déjà s'en souvient plus longtemps.
    /// Raconter l'entretient aussi chez celui qui raconte ; sans personne à qui le dire, il s'efface à son terme.
    /// </summary>
    private void Transmettre(Humain h, Impulsion i, IReadOnlyList<Humain> humains, long maintenant)
    {
        Humain? enfant = null, proche = null;
        foreach (var o in humains.Vivants())
        {
            if (!o.EstVivant || o.Id == h.Id) continue;
            if ((o.MereId == h.Id || o.PereId == h.Id) && o.AgeEnAnnees(maintenant) is >= 5 and < 16)
            {
                if (enfant is null || enfant.Consigne(i.Parole, maintenant) is not null && o.Consigne(i.Parole, maintenant) is null) enfant = o;
            }
            else if (proche is null && h.CommunauteId > 0 && o.CommunauteId == h.CommunauteId && !o.EstEnfant(maintenant)
                     && Distance(o.X, o.Y, h.X, h.Y) <= 2 && o.AffectionPour(h.Id) >= 30)
                proche = o;
        }
        var auditeur = enfant ?? proche;
        if (auditeur is null) return;
        var estime = Math.Clamp(0.5 + auditeur.AffectionPour(h.Id) / 200, 0.2, 1);
        var adulte = enfant is null;
        if (alea.Reel() >= (adulte ? 0.3 : 0.5) * estime) return;
        var tient = auditeur.Consigne(i.Parole, maintenant);
        var entretien = (long)((60 + 120 * i.Force) * Calendrier.SecondesParJour);
        if (tient is not null)
        {
            // Il le suit déjà : se le redire entre fidèles le fait durer chez l'un et l'autre.
            var j = auditeur.Impulsions.IndexOf(tient);
            Tour.Changer(auditeur, x =>
            {
                if (j >= 0 && j < x.Impulsions.Count && x.Impulsions[j] == tient) x.Impulsions[j] = tient with { Jusqua = Math.Max(tient.Jusqua, maintenant + entretien) };
            });
        }
        else
        {
            var force = Math.Clamp(i.Force * (adulte ? 0.4 + 0.4 * estime : 0.6 + 0.4 * estime), 0.1, 1);
            var jours = (60 + 300 * force) * (0.7 + 0.6 * alea.Reel());
            var id = Noter(maintenant, TypeEvenement.Parole, h, auditeur, new Recit("cmd.transmis", ("h", h), ("x", auditeur), ("ph", Paroles.Phrase(i.Parole, null, null, monde: true))),
                i.EvenementId);
            Tour.Changer(auditeur, x => Garder(x, new Impulsion(i.Parole, i.Mode, 0, 0, force, maintenant, maintenant + (long)(jours * Calendrier.SecondesParJour),
                maintenant, Reponse.Acceptee, id)));
        }
        // Le dire l'entretient chez soi.
        var k = h.Impulsions.IndexOf(i);
        if (k >= 0) h.Impulsions[k] = i with { Jusqua = Math.Max(i.Jusqua, maintenant + entretien) };
    }
}
