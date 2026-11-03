using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// « Langage avancé » (0.37.0) : les fondateurs arrivent avec une langue complète. Toutes les langues humaines connues le sont,
/// y compris celles des chasseurs-cueilleurs sans écriture ni métal ; un peuple de chasseurs-cueilleurs qui partage une langue
/// compte quelques centaines de personnes (environ 500 : Birdsell, 1953). Les fondateurs d'une même terre forment donc un seul
/// peuple, avec une langue mère : ses sons, ses mots (courts pour les choses de tous les jours : Zipf, 1935), son ordre des mots
/// (tiré selon sa fréquence dans les langues du monde : Dryer, 2013). Chacun la connaît selon son aisance avec les mots et sa
/// mémoire. Ensuite, tout reste vivant : les enfants apprennent et déforment, les mots non dits s'oublient, les communautés qui
/// se séparent voient leurs parlers dériver. Les mots des savoirs et des plantes naissent avec eux.
/// </summary>
public static class LangueHeritee
{
    /// <summary>Flux de hasard à part (tiré de la graine) : le reste du monde de départ n'en dépend pas.</summary>
    private const ulong Flux = 0x4C414E47554553UL;

    /// <summary>
    /// Donne une langue mère aux fondateurs de chaque terre de départ, et à chacun un nom dans cette langue. Rend les événements
    /// du journal (un par terre), à ajouter au monde ; leurs numéros viennent de <paramref name="numeros"/>.
    /// </summary>
    public static List<EvenementJournal> Donner(IReadOnlyList<Humain> fondateurs, Carte carte, ConfigurationMonde configuration, long maintenant,
        Numeroteur numeros)
    {
        var alea = new Alea(configuration.Graine ^ Flux);
        var idees = Idees.DUneLangueHeritee(configuration.Generation).ToList();
        var evenements = new List<EvenementJournal>();
        var masses = carte.Mer.Masse;
        foreach (var peuple in fondateurs.GroupBy(h => masses[carte.Indice(h.X, h.Y)]).OrderBy(g => g.Key))
        {
            var membres = peuple.OrderBy(h => h.Id).ToList();
            var sons = Sons.Tirer(alea);
            var ordre = Langues.TirerOrdre(alea);
            var lexique = Forger(idees, sons, alea);
            var porteParole = membres.MaxBy(h => h.Personnalite.Intelligence.Verbale)!;
            var e = new EvenementJournal(maintenant, TypeEvenement.Langage, porteParole.Id, 0,
                new Recit("lang.herite", ("n", membres.Count), ("mots", lexique.Count), ("o", Recit.Seul($"ordreMots.{ordre}")),
                    ("exemple", Langues.Dit(lexique["eau"].Forme, lexique["eau"].Signe))))
            {
                Id = numeros.Suivant(), Significatif = true,
            };
            evenements.Add(e);
            var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in membres)
            {
                Apprendre(h, lexique, alea, maintenant);
                h.StadeParole = 3;
                h.Ordre = ordre;
                // Ils ont appris à parler enfants, au milieu des phrases : leurs enfants régulariseront comme eux.
                h.ParoleEnfant = h.ParoleNative = true;
                Nommer(h, lexique, sons, noms, alea, maintenant, e.Id);
            }
        }
        return evenements;
    }

    /// <summary>Une forme par idée, dans les sons de la langue ; une part des mots se fait par signes, selon le penchant de la langue.</summary>
    private static Dictionary<string, (string Forme, bool Signe)> Forger(List<string> idees, Sons sons, Alea alea)
    {
        var lexique = new Dictionary<string, (string Forme, bool Signe)>();
        var prises = new HashSet<string>(StringComparer.Ordinal);
        foreach (var idee in idees)
        {
            // Les noms de chose qu'on chasse se font plus volontiers par signes (on chasse en silence), comme à l'invention d'un mot.
            var chasse = idee is "gibier" or "predateur" or "chasser" or "fuir";
            var signe = alea.Reel() < Math.Min(0.9, sons.Signes * (chasse ? 2 : 1));
            var forme = "";
            for (var essai = 0; essai < 8; essai++)
            {
                forme = signe ? Langues.SigneNeuf(alea) : Langues.Forger(sons, alea, Idees.Courante(idee) ? 1 + alea.Entier(2) : 2 + alea.Entier(2));
                if (!prises.Contains(forme)) break;
            }
            prises.Add(forme);
            lexique[idee] = (forme, signe);
        }
        return lexique;
    }

    /// <summary>
    /// Ce que le fondateur sait de la langue : presque tous les mots de tous les jours, et des autres d'autant plus qu'il est à l'aise
    /// avec les mots et qu'il a de la mémoire ; chaque mot plus ou moins ancré.
    /// </summary>
    private static void Apprendre(Humain h, Dictionary<string, (string Forme, bool Signe)> lexique, Alea alea, long maintenant)
    {
        var aptitude = Langues.Aptitude(h);
        var memoire = Math.Clamp((h.Personnalite.Intelligence.Memoire - 55) / 90, 0, 1);
        foreach (var (idee, (forme, signe)) in lexique)
        {
            var courante = Idees.Courante(idee) || Idees.NomsSeuls.Contains(idee);
            var connu = courante ? 0.9 + 0.1 * aptitude : 0.3 + 0.45 * aptitude + 0.25 * memoire;
            if (alea.Reel() >= connu) continue;
            var force = Math.Min(1, (courante ? 0.6 : 0.35) + 0.4 * alea.Reel() * (0.5 + 0.5 * memoire));
            // AuteurId 0 : personne ne l'a inventé, il vient de la langue des fondateurs ; il l'a appris enfant (vers 2 à 6 ans).
            var appris = Math.Min(maintenant, h.Naissance + (long)((2 + 4 * alea.Reel()) * Calendrier.SecondesParAn));
            h.Vocabulaire.Add(new Mot(idee, forme, signe, force, appris, 0));
        }
    }

    /// <summary>Un nom dans sa langue, qui veut dire quelque chose (le soleil, la rivière, un oiseau…), choisi selon son caractère.</summary>
    private static void Nommer(Humain h, Dictionary<string, (string Forme, bool Signe)> lexique, Sons sons, HashSet<string> pris, Alea alea,
        long maintenant, long evenement)
    {
        var p = h.Personnalite;
        var candidats = new List<(string Idee, double Poids)>();
        void Si(string idee, double poids)
        {
            if (poids > 0 && lexique.TryGetValue(idee, out var m) && !m.Signe) candidats.Add((idee, poids));
        }
        foreach (var x in new[] { "soleil", "lune", "etoiles", "pluie", "vent", "riviere", "pierre", "arbre", "oiseau", "poisson", "herbe" }) Si(x, 1);
        Si("joie", 3 * p.Extraversion * p.Extraversion);
        Si("vent", 2 * p.Ouverture);
        Si("pierre", 2 * p.Conscience);
        Si("gibier", 1.5 * p.Adresse);
        if (candidats.Count == 0) return;
        var tirage = alea.Reel() * candidats.Sum(x => x.Poids);
        var idee = candidats[^1].Idee;
        foreach (var (i, w) in candidats)
        {
            if (tirage < w) { idee = i; break; }
            tirage -= w;
        }
        var nom = lexique[idee].Forme;
        if (alea.Reel() < 0.5) nom += Langues.Forger(sons, alea, 1);
        while (pris.Contains(nom)) nom += Langues.Forger(sons, alea, 1);
        pris.Add(nom);
        nom = char.ToUpperInvariant(nom[0]) + nom[1..];
        (h.NomLangue, h.NomSens, h.NomLangueLe, h.NomParId, h.NomEvenementId) =
            (nom, ((Recit)Idees.Dit(idee)).Encoder(), maintenant, 0, evenement);
    }
}
