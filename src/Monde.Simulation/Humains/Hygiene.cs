using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// L'hygiène, trouvée par les humains eux-mêmes. Personne ne connaît les germes : le dégoût, inné, détourne déjà des
/// excréments, de la viande pourrie et de l'eau croupie (Curtis, de Barra et Aunger, 2011), et après des maux de ventre on
/// cherche ce qui les a causés. On y trouve des pratiques justes (se laver les mains, faire ses besoins loin du camp, boire
/// l'eau qui court, jeter la viande gâtée) ou de fausses causes (une plante innocente, qu'on se met à fuir) : c'est la
/// corrélation, vraie ou illusoire, qui décide, puis l'expérience, qui confirme ou fait douter.
/// <para>
/// Ce que dit la recherche sur l'effet des pratiques justes, contre les diarrhées : se laver les mains, de 30 à 47 % de moins
/// (Curtis et Cairncross, 2003 ; Ejemot-Nwadiaro et al., 2015) ; éloigner les excréments, environ 30 % de moins, surtout
/// quand tout le groupe le fait (Fewtrell et al., 2005 ; Wolf et al., 2014) ; une eau meilleure, de 10 à 25 % (Wolf et al.,
/// 2014) ; contre les vers intestinaux, l'assainissement et le lavage des mains réduisent l'infection d'un tiers à la moitié
/// (Strunz et al., 2014). L'effet de la viande flairée et jetée est une estimation.
/// </para>
/// </summary>
public static class Hygiene
{
    /// <summary>Les pratiques d'hygiène, chacune avec le geste qui la montre.</summary>
    public static readonly (Effet Effet, Geste Geste, Matiere Matiere)[] Pratiques =
    [
        (Effet.MainsPropres, Geste.Laver, Matiere.Eau),
        (Effet.Latrines, Geste.Enfouir, Matiere.Aucune),
        (Effet.EauVive, Geste.Puiser, Matiere.Eau),
        (Effet.ViandeSaine, Geste.Flairer, Matiere.Carcasse),
    ];

    /// <summary>Maladies qui passent par les mains, l'eau, la nourriture et les excréments.</summary>
    public static bool DuVentre(Maladie m) => m is Maladie.Diarrhee or Maladie.Dysenterie or Maladie.VersIntestinaux;

    /// <summary>Croit à cette pratique au point de la suivre.</summary>
    public static bool Pratique(Humain h, Effet e) => h.Savoirs.Exists(s => s.Effet == e && s.Confiance >= 0.5);

    public static bool EstPratique(Effet e) => e is Effet.MainsPropres or Effet.Latrines or Effet.EauVive or Effet.ViandeSaine;

    /// <summary>
    /// Sensibilité au dégoût (0,4 à 1,4) : plus forte chez les anxieux et les consciencieux, et chez les femmes en moyenne
    /// (Druschel et Sherman, 1999 ; Tybur et al., 2009).
    /// </summary>
    public static double Degout(Humain h) =>
        Math.Clamp(0.6 + 0.4 * h.Personnalite.Nevrosisme + 0.2 * h.Personnalite.Conscience + (h.Sexe == Sexe.Femme ? 0.15 : 0), 0.4, 1.4);

    /// <summary>
    /// Part des maux de ventre qu'évitent les pratiques : les siennes, et, pour un petit enfant, celles de qui le nourrit ;
    /// les excréments écartés protègent selon la part de sa communauté qui le fait (<paramref name="partLatrines"/>).
    /// <paramref name="eauDormante"/> : il boit d'ordinaire à une eau qui dort (lac, marais) ; qui sait chercher l'eau qui court
    /// s'en garde.
    /// </summary>
    public static double Protection(Humain h, Humain? nourricier, Maladie m, double partLatrines, bool eauDormante)
    {
        if (!DuVentre(m)) return 0;
        bool Suit(Effet e) => Pratique(h, e) || nourricier is not null && Pratique(nourricier, e);
        var vers = m == Maladie.VersIntestinaux;
        var reste = 1.0;
        if (Suit(Effet.MainsPropres)) reste *= 1 - (vers ? 0.3 : 0.35);
        var latrines = 0.3 * (Pratique(h, Effet.Latrines) ? 1 : 0) + 0.7 * partLatrines;
        reste *= 1 - (vers ? 0.4 : 0.3) * Math.Clamp(latrines, 0, 1);
        if (eauDormante && Suit(Effet.EauVive)) reste *= 1 - (vers ? 0.1 : 0.2);
        if (!vers && Suit(Effet.ViandeSaine)) reste *= 0.9;
        return 1 - reste;
    }
}

public sealed partial class Comportement
{
    /// <summary>Part des membres de chaque communauté qui écartent leurs excréments du camp (recalculée chaque jour).</summary>
    private Dictionary<int, double> _partLatrines = [];

    /// <summary>Enfants morts d'un mal de ventre depuis hier : leurs parents en chercheront la cause.</summary>
    private readonly List<int> _mortsDuVentre = [];

    /// <summary>Boit-il d'ordinaire à une eau qui dort : un lac ou un marais tout près, et pas de rivière ?</summary>
    private bool EauDormante(int x, int y)
    {
        var dormante = false;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var b = carte.BiomeEn(nx, ny);
                if (b == Biome.Riviere) return false;
                if (b is Biome.Lac or Biome.Marais) dormante = true;
            }
        }
        return dormante;
    }

    /// <summary>Part des maux de ventre que ses pratiques (ou celles de qui le nourrit) lui évitent.</summary>
    private double ProtectionHygiene(Humain h, Maladie m, IReadOnlyList<Humain> tous)
    {
        if (!Hygiene.DuVentre(m)) return 0;
        var nourricier = h.AgeEnAnnees(_maintenant) < 8 ? Gardien(h, tous, _maintenant) : null;
        return Hygiene.Protection(h, nourricier, m, h.CommunauteId != 0 ? _partLatrines.GetValueOrDefault(h.CommunauteId) : 0, EauDormante(h.X, h.Y));
    }

    /// <summary>Malade du ventre malgré ses pratiques : il y croit un peu moins.</summary>
    private static void DouterHygiene(Humain h)
    {
        for (var i = 0; i < h.Savoirs.Count; i++)
        {
            var s = h.Savoirs[i];
            if (Hygiene.EstPratique(s.Effet)) h.Savoirs[i] = s with { Essais = s.Essais + 1 };
        }
    }

    /// <summary>
    /// Chaque jour : la part de chaque communauté qui écarte ses excréments ; ceux qui sortent de maux de ventre, ou dont un
    /// enfant en est mort, en cherchent la cause ; une fois par mois, une pratique suivie sans retomber malade se confirme
    /// (à tort ou à raison : le mal est rare de toute façon), et les autres la voient faire.
    /// </summary>
    public void Hygienes(IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        var membres = new Dictionary<int, (int Tous, int Suivent)>();
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.CommunauteId == 0 || h.EstEnfant(maintenant)) continue;
            var (n, k) = membres.GetValueOrDefault(h.CommunauteId);
            membres[h.CommunauteId] = (n + 1, k + (Hygiene.Pratique(h, Effet.Latrines) ? 1 : 0));
        }
        _partLatrines = membres.ToDictionary(p => p.Key, p => p.Value.Suivent / (double)p.Value.Tous);

        const long Jour = Calendrier.SecondesParJour;
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.EstEnfant(maintenant)) continue;
            // Guéri hier de maux de ventre.
            if (Hygiene.DuVentre(h.Maladie) && h.MaladeJusqua <= maintenant && h.MaladeJusqua > maintenant - Jour)
                ChercherLaCause(h, null, tous);
        }
        // Un parent dont l'enfant est mort d'un mal de ventre cherche, lui aussi, ce qui l'a tué.
        foreach (var id in _mortsDuVentre)
        {
            var mort = tous[id - 1];
            foreach (var pid in new[] { mort.MereId, mort.PereId })
            {
                if (Vivant(tous, pid) is { } parent && !parent.EstEnfant(maintenant)) ChercherLaCause(parent, mort, tous);
            }
        }
        _mortsDuVentre.Clear();

        // Une fois par mois pour chacun : la pratique tenue se confirme et se montre.
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || (maintenant / Jour + h.Id) % 30 != 0) continue;
            foreach (var (effet, geste, matiere) in Hygiene.Pratiques)
            {
                var s = h.Savoirs.Find(x => x.Effet == effet);
                if (s is null || s.Confiance < 0.5) continue;
                // Malade du ventre ce mois-ci : le doute est déjà venu (voir DouterHygiene).
                if (Hygiene.DuVentre(h.Maladie) && h.MaladeJusqua > maintenant - 30 * Jour) continue;
                Retenir(h, geste, matiere, Matiere.Aucune, effet, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
                // Les autres ne le voient faire qu'à l'occasion, et une habitude s'imite moins vite qu'un outil.
                if (alea.Reel() < 0.2) Montrer(h, geste, matiere, Matiere.Aucune, effet, 0, Symptome.Aucun, tous, s.EvenementId);
            }
        }
    }

    /// <summary>
    /// Ce qui a pu donner ces maux de ventre ? L'esprit relie le mal à ce qui dégoûte et à ce qui précédait : les mains
    /// sales, les excréments près du camp, l'eau croupie, la viande qui sentait, ou une plante mangée ces jours-là
    /// (l'aversion pour un goût après un malaise naît d'une seule fois : Garcia et Koelling, 1966). Le plus souvent, on n'en
    /// tire rien.
    /// </summary>
    private void ChercherLaCause(Humain h, Humain? enfantMort, IReadOnlyList<Humain> tous)
    {
        var p = h.Personnalite;
        var chagrin = enfantMort is null ? 1 : 2.5;
        if (alea.Reel() >= 0.08 * chagrin * (0.3 + Ingeniosite(h)) * (0.5 + p.Ouverture) * Hygiene.Degout(h)) return;

        var communaute = h.CommunauteId != 0 && _partLatrines.ContainsKey(h.CommunauteId);
        var dormante = EauDormante(h.X, h.Y);
        var viande = h.CuitJusqua > _maintenant - 5 * Calendrier.SecondesParJour || h.Porte(Matiere.Carcasse);
        var especes = Flore.Sur(carte.BiomeEn(h.X, h.Y));
        Span<double> poids =
        [
            Hygiene.Pratique(h, Effet.MainsPropres) ? 0 : 1,
            Hygiene.Pratique(h, Effet.Latrines) ? 0 : communaute ? 1 : 0.3,
            Hygiene.Pratique(h, Effet.EauVive) ? 0 : dormante ? 1.5 : 0.15,
            Hygiene.Pratique(h, Effet.ViandeSaine) ? 0 : viande ? 1 : 0.3,
            especes.Count == 0 ? 0 : 1.2,
        ];
        var total = 0.0;
        foreach (var w in poids) total += w;
        if (total <= 0) return;
        var tirage = alea.Reel() * total;
        var choix = 0;
        while (choix < poids.Length - 1 && (tirage -= poids[choix]) >= 0) choix++;

        var qui = enfantMort is null ? new Recit("hyg.qui.remis", ("h", h)) : new Recit("hyg.qui.enfant", ("h", h), ("e", enfantMort));
        if (choix == 4)
        {
            // Une fausse cause : une plante d'ici, innocente ou non, qu'on croit désormais mauvaise.
            var espece = especes[alea.Entier(especes.Count)];
            Retenir(h, Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Poison, espece.Numero, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
                recit: new Recit("hyg.plante", ("qui", qui), ("plante", espece.Dit), ("h", h)));
            return;
        }
        var (effet, geste, matiere) = Hygiene.Pratiques[choix];
        var recit = new Recit(effet is Effet.MainsPropres or Effet.Latrines or Effet.EauVive ? $"hyg.{effet}" : "hyg.ViandeSaine", ("qui", qui), ("h", h));
        Retenir(h, geste, matiere, Matiere.Aucune, effet, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous, recit: recit);
    }
}
