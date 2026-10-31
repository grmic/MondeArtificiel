using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// « Ce qu'on jette repousse » (mondes de la génération 2, depuis la 0.32.8). Autour d'un camp, les graines, noyaux et bouts de
/// racine jetés germent sur une terre remuée et enrichie de déchets : les chasseurs-cueilleurs retrouvent en revenant à un ancien
/// camp les plantes qu'ils y ont mangées (Anderson, 1952, l'hypothèse du « tas d'ordures » ; jardins involontaires des campements
/// décrits par l'ethnographie). Un observateur peut faire le lien, puis essayer d'en enfouir exprès, près du camp ou sur une terre
/// que la crue a rendue riche. Ce n'est pas encore l'agriculture : ni champ, ni récolte attendue, ni plante choisie.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Des pousses qui se voient : au moins 15 % de plus que ce que la terre donne d'elle-même.</summary>
    private const int RejetsVisibles = 1_500;

    /// <summary>Le savoir des pousses du camp, s'il l'a.</summary>
    private static Savoir? SavoirDesRejets(Humain h) => h.Savoirs.Find(s => s.Effet == Effet.Repousse);

    /// <summary>
    /// Il cueille sur la case <paramref name="i"/>, riche de pousses près du camp des siens : il peut remarquer que ce qu'on y a jeté
    /// a repoussé ; s'il le savait, il le confirme. Puis, s'il y croit, il peut enfouir lui-même quelques graines de ce qu'il mange.
    /// </summary>
    internal void ConstaterLesRejets(Humain h, int i)
    {
        if (h.EstEnfant(_maintenant)) return;
        var sait = SavoirDesRejets(h);
        if (ecosysteme.Rejets[i] >= RejetsVisibles && (sait is null || _maintenant - sait.UtiliseLe >= 30 * Calendrier.SecondesParJour))
        {
            if (sait is not null)
            {
                Retenir(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Repousse, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, _tous);
            }
            else
            {
                var p = h.Personnalite;
                var chance = 0.003 * (0.3 + Croyances.Analytique(h)) * (0.5 + p.Ouverture) * (0.5 + Math.Clamp((p.Intelligence.Spatiale - 55) / 90, 0, 1));
                if (alea.Reel() < chance)
                {
                    Retenir(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Repousse, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, _tous,
                        recit: new Recit("rejets.constat", ("h", h)));
                    return;
                }
            }
        }
        Enfouir(h, i);
    }

    /// <summary>
    /// Il croit que ce qu'on enfouit repousse : de temps en temps, il garde quelques graines ou un bout de racine de ce qu'il cueille
    /// et les met en terre là où il les retrouvera, près du camp, ou sur la terre que la crue a laissée riche s'il connaît le limon.
    /// </summary>
    private void Enfouir(Humain h, int i)
    {
        if (SavoirDesRejets(h) is not { Confiance: >= 0.5 } s || h.Faim > 70) return;
        var limon = ecosysteme.Limon[i] >= LimonVisible && SavoirDuLimon(h) is { Confiance: >= 0.5 };
        var camp = h.CommunauteId != 0 && Social?.Communautes.FirstOrDefault(c => c.Id == h.CommunauteId) is { Active: true } c
                   && Distance(c.CentreX, c.CentreY, h.X, h.Y) <= 2;
        if (!limon && !camp) return;
        var p = h.Personnalite;
        if (alea.Reel() >= 0.02 * (0.5 + p.Conscience) * (limon ? 1.5 : 1)) return;
        var gain = 0.02 + 0.04 * alea.Reel();
        var nature = ecosysteme;
        Tour.Differer(() => nature.Germer(i, gain));
        // Les premières fois se racontent ; ensuite c'est un geste de tous les jours.
        if (s.Essais < 3)
            Noter(_maintenant, TypeEvenement.Decouverte, h, null, new Recit(limon ? "rejets.enfouit.limon" : "rejets.enfouit", ("h", h)), s.EvenementId);
        var k = h.Savoirs.IndexOf(s);
        h.Savoirs[k] = s with { Essais = s.Essais + 1, UtiliseLe = _maintenant };
    }
}

public sealed partial class VieSociale
{
    /// <summary>
    /// Chaque semaine : autour du camp d'une communauté, ce qu'on a jeté germe à la belle saison, d'autant plus que le groupe est
    /// nombreux (quelques pour cent de pousse en plus par semaine pour une trentaine de personnes). Sans tirer de hasard.
    /// </summary>
    internal void Jeter(Communaute c, int membres, Ecosysteme nature, Monde.Carte carte, long maintenant)
    {
        if (Ecosysteme.FacteurSaison(maintenant) < 0.8 || c.CentreX < 0) return;
        var gain = 0.0015 * Math.Min(membres, 60);
        nature.Germer(carte.Indice(c.CentreX, c.CentreY), gain);
        // Un peu plus loin, là où l'on va jeter les restes : la case voisine, d'un côté qui change avec les semaines.
        var semaine = (int)(maintenant / (7 * Calendrier.SecondesParJour));
        var (dx, dy) = ((semaine % 3) - 1, (semaine / 3 % 3) - 1);
        int x = c.CentreX + dx, y = c.CentreY + dy;
        if ((dx != 0 || dy != 0) && x >= 0 && y >= 0 && x < carte.Largeur && y < carte.Hauteur) nature.Germer(carte.Indice(x, y), gain / 2);
    }
}
