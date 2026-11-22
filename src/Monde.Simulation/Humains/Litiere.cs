using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Dormir par le froid (0.45.32) : la chaleur du feu et des autres en degrés, la litière, et le sommeil que le froid hache.
/// <para>
/// Ce que dit la recherche. Tous les grands singes se font chaque soir un nid de branches et de feuilles, qui isole du sol (Fruth et
/// Hohmann, 1996 ; Samson et Hunt, 2012) ; les humains font des litières d'herbes depuis au moins 200 000 ans (Border Cave, Wadley
/// et al., 2020). Le geste est ici inné, comme chez nos cousins : ce n'est pas une découverte. Des Aborigènes du désert dormaient
/// nus par des nuits proches de 0 °C, mais sur un sol sec, entre deux petits feux, d'un sommeil interrompu (Scholander et al.,
/// 1958). Le feu ne chauffe qu'un côté du corps, et son rayonnement tombe avec le carré de la distance. Le froid hache le sommeil
/// (Haskell et al., 1981). Les valeurs ci-dessous sont des estimations qui suivent ces mesures.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Durée de vie d'une litière (s) : les herbes se tassent et prennent l'humidité en quelques jours.</summary>
    private const double TenueDeLaLitiere = 6 * Calendrier.SecondesParJour;

    /// <summary>
    /// Chaleur que le feu de la case ajoute à la température ressentie (°C) : de quelques degrés à l'écart jusqu'à une douzaine tout
    /// près d'un bon feu. Chacun a sa place autour (de 1,2 à 2,2 m environ), plus près quand il a froid ; un feu couvert chauffe peu.
    /// </summary>
    internal double ChaleurDuFeuSur(Humain h)
    {
        if (Installations.FoyerEn(h.X, h.Y) is not { } f) return 0;
        var force = Math.Clamp(0.4 + 0.15 * f.Heures, 0.4, 1.2) * (f.Couvert ? 0.35 : 1);
        var place = 1.2 + ((uint)h.Id * 2654435761u % 1000) / 1000.0;
        var distance = place * (1 - 0.3 * Math.Clamp(h.Froid / 60, 0, 1));
        return Math.Min(14, 10 * force * Math.Pow(1.2 / distance, 2));
    }

    /// <summary>
    /// Chaleur des proches contre qui on se serre (°C ressentis) : davantage couché, et d'autant plus qu'on est petit (un enfant
    /// blotti contre un adulte).
    /// </summary>
    internal double ChaleurDesAutres(Humain h, long maintenant) =>
        (h.Activite is Activite.Dormir or Activite.SAbriter ? 3 : 1.5) * Math.Clamp(160 / Stature(h, maintenant), 0.8, 1.6);

    /// <summary>
    /// Qualité de la litière sous lui ici (0 à 1) : la sienne, ou celle d'un parent ou du compagnon qui dort sur la même case. Elle
    /// se dégrade avec les jours.
    /// </summary>
    internal double LitiereSous(Humain h, long maintenant) => LitiereSous(h, _tous, maintenant);

    /// <summary>Même chose, parmi les humains donnés (rangés par identifiant, à partir de 1).</summary>
    internal static double LitiereSous(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        var q = LitiereDe(h, h.X, h.Y, maintenant);
        if (q >= 0.5) return q;
        foreach (var id in (ReadOnlySpan<int>)[h.MereId, h.PereId, h.PartenaireId])
        {
            if (id <= 0 || id > tous.Count || tous[id - 1] is not { EstVivant: true, Id: var oid } o || oid != id) continue;
            q = Math.Max(q, LitiereDe(o, h.X, h.Y, maintenant));
        }
        return q;
    }

    private static double LitiereDe(Humain h, int x, int y, long maintenant) =>
        h.Litiere <= 0 || h.LitiereX != x || h.LitiereY != y ? 0 : h.Litiere * Math.Exp(-Math.Max(0, maintenant - h.LitiereLe) / TenueDeLaLitiere);

    /// <summary>Ce que la case offre pour une litière (0 à 1) : des rameaux de conifère, des fougères, des herbes, ou presque rien.</summary>
    private double MatiereDeLitiere(int x, int y)
    {
        var i = carte.Indice(x, y);
        var biome = carte.BiomeEn(x, y) switch
        {
            Biome.ForetDeConiferes => 1.0,
            Biome.ForetDeFeuillus or Biome.ForetTropicale => 0.9,
            Biome.PlaineDeFougeres => 0.8,
            Biome.Marais => 0.7,
            Biome.Mangrove => 0.6,
            Biome.Lande => 0.5,
            Biome.Montagne => 0.3,
            Biome.Desert or Biome.Plage or Biome.SommetEnneige => 0.1,
            _ => 0.4,
        };
        return Math.Clamp(biome * (0.5 + 0.5 * Math.Min(1, ecosysteme.Plantes[i] / 4000.0)), 0, 1);
    }

    /// <summary>La nuit est-elle rude ici : de la neige sous soi, ou un froid vif, sans grotte ni hutte ?</summary>
    private bool NuitRude(Humain h, double temperature) =>
        AbriEn(h.X, h.Y) < 0.5 && (NeigeSur(carte.Indice(h.X, h.Y)) >= 0.5 || temperature < 5);

    /// <summary>
    /// En se couchant par une nuit froide, on écarte la neige et on étale de quoi s'isoler du sol, sauf si la litière de la veille
    /// (la sienne, ou celle des siens) tient encore. Sa qualité dépend de ce qu'il y a autour, du soin et de l'adresse, et de la
    /// fatigue : l'épuisé se jette à terre. Un petit enfant dort sur celle des siens.
    /// </summary>
    private void FaireSaLitiere(Humain h, long maintenant)
    {
        var temperature = Temperature(h.X, h.Y, maintenant);
        if (temperature >= 12 && NeigeSur(carte.Indice(h.X, h.Y)) < 0.5) return;
        if (Installations.AbriEn(h.X, h.Y) > 0 || Climat.Abri(carte.Terrain.GrotteA(h.X, h.Y)) >= 0.5) return;
        if (h.AgeEnAnnees(maintenant) < 4) return;
        var matiere = MatiereDeLitiere(h.X, h.Y);
        var p = h.Personnalite;
        var adresse = Math.Clamp((p.Intelligence.Manuelle - 55) / 90, 0, 1);
        var qualite = matiere * (0.45 + 0.35 * p.Conscience + 0.2 * adresse) * (1 - 0.6 * Math.Clamp((h.Fatigue - 85) / 15, 0, 1));
        if (LitiereSous(h, maintenant) >= 0.7 * qualite) return;
        var premiere = h.LitiereLe == 0;
        (h.LitiereX, h.LitiereY, h.Litiere, h.LitiereLe) = (h.X, h.Y, qualite, maintenant);
        // Un geste de tous les soirs froids : noté la première fois seulement, et dans le journal quotidien (pas par défaut).
        if (premiere && qualite > 0.05 && JournalQuotidien)
        {
            var neige = NeigeSur(carte.Indice(h.X, h.Y)) >= 0.5;
            var sorte = carte.BiomeEn(h.X, h.Y) switch
            {
                Biome.ForetDeConiferes or Biome.ForetDeFeuillus or Biome.ForetTropicale or Biome.Mangrove => "branches",
                Biome.PlaineDeFougeres or Biome.Lande => "fougeres",
                _ => "herbes",
            };
            Noter(maintenant, TypeEvenement.Journee, h, null, new Recit(neige ? "j.litiere.neige" : "j.litiere.froid", ("h", h),
                ("matiere", new Recit($"j.litiere.{sorte}"))));
        }
    }

    /// <summary>
    /// Le froid hache le sommeil : on récupère moins, et on se réveille d'autant plus souvent qu'on a froid (on s'assoit près du
    /// feu, on se serre, puis on se rendort).
    /// </summary>
    private static double SommeilSousLeFroid(Humain h) => 1 - 0.6 * Math.Clamp((h.Froid - 25) / 45, 0, 1);

    private bool ReveilleParLeFroid(Humain h) => h.Froid > 40 && alea.Reel() < (h.Froid - 40) / 200;
}
