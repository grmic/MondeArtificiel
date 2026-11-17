using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>
/// Flux « Scène » de la vue 3D (0.41.0) : les vivants autour de la caméra, en nombres seulement (pas de texte traduit), pour être
/// demandé quatre fois par seconde sans peser. <paramref name="Humains"/> : <see cref="ChampsParHumain"/> nombres par humain, dans
/// l'ordre id, x, y, versX, versY, avance (millièmes), parSeconde (millionièmes), activité, état, âge (dixièmes d'année),
/// femme (1/0), teint (millièmes), humeur, peur, communauté, mère ; depuis la 0.41.1 : réserves, endurance (millièmes), taille
/// (centièmes d'écart), enfants mis au monde, matière du vêtement (0 : nu), usure du vêtement (millièmes) ; depuis la 0.41.8 : froid,
/// faim, fatigue, santé (0 à 100), névrosisme et extraversion (millièmes) ; depuis le suivi 3D cohérent : aisance dans l'eau (millièmes) ; depuis la 0.42.6 : matière de la semelle (0 : pieds nus) et de l'enduit de la peau (0 : rien, 11 boue,
/// 18 ocre). <paramref name="Secondes"/> : instant du dernier pas ;
/// <paramref name="Facteur"/> : vitesse réellement atteinte (secondes du monde par seconde réelle).
/// </summary>
public sealed record SceneDto(long Secondes, double Facteur, int[] Humains)
{
    public const int ChampsParHumain = 33;

    /// <summary>Rayon le plus grand qu'on peut demander, en cases.</summary>
    public const int RayonMax = 64;

    public static SceneDto Depuis(ServiceMonde service, int x, int y, int rayon)
    {
        var (secondes, humains) = service.Monde.Scene(x, y, Math.Clamp(rayon, 0, RayonMax));
        var n = new int[humains.Count * ChampsParHumain];
        var k = 0;
        foreach (var h in humains)
        {
            n[k++] = h.Id;
            n[k++] = h.X;
            n[k++] = h.Y;
            n[k++] = h.VersX;
            n[k++] = h.VersY;
            n[k++] = (int)Math.Round(h.Avance * 1000);
            n[k++] = (int)Math.Round(h.ParSeconde * 1_000_000);
            n[k++] = (int)h.Activite;
            n[k++] = h.Etat;
            n[k++] = (int)Math.Round(h.Age * 10);
            n[k++] = h.Femme ? 1 : 0;
            n[k++] = (int)Math.Round(h.Teint * 1000);
            n[k++] = h.Humeur;
            n[k++] = (int)Math.Round(h.Peur);
            n[k++] = h.Communaute;
            n[k++] = h.Mere;
            n[k++] = (int)Math.Round(h.Reserves);
            n[k++] = (int)Math.Round(h.Endurance * 1000);
            n[k++] = (int)Math.Round(h.Taille * 100);
            n[k++] = h.Enfants;
            n[k++] = (int)h.Vetement;
            n[k++] = (int)Math.Round(h.Usure * 1000);
            n[k++] = (int)Math.Round(h.Froid);
            n[k++] = (int)Math.Round(h.Faim);
            n[k++] = (int)Math.Round(h.Fatigue);
            n[k++] = (int)Math.Round(h.Sante);
            n[k++] = (int)Math.Round(h.Nevrosisme * 1000);
            n[k++] = (int)Math.Round(h.Extraversion * 1000);
            n[k++] = (int)Math.Round(Math.Clamp(h.Nage, 0, 1) * 1000);
            n[k++] = (int)h.Chaussures;
            n[k++] = (int)h.Enduit;
            n[k++] = (int)h.Coucher;
            n[k++] = h.DortContre;
        }
        return new SceneDto(secondes, service.FacteurAtteint, n);
    }
}

/// <summary>
/// Le temps qu'il fait sur chaque case pour la vue 3D, le jour donné : température à midi (°C, arrondie, de −128 à 127) et à
/// l'aube. Le client y pose la neige (sous 0 °C) et le givre.
/// </summary>
public sealed record TemperaturesDto(int Jour, int Largeur, int Hauteur, sbyte[] Midi, sbyte[] Aube)
{
    public static TemperaturesDto Depuis(MondeSimule monde)
    {
        var secondes = monde.Secondes;
        var debut = secondes - secondes % Calendrier.SecondesParJour;
        var carte = monde.Carte;
        var midi = new sbyte[carte.Largeur * carte.Hauteur];
        var aube = new sbyte[midi.Length];
        // L'écart du moment (vague de froid, canicule, hiver volcanique) compte, comme pour les humains (0.43.1).
        var ecart = monde.AnomalieTemperature;
        for (var y = 0; y < carte.Hauteur; y++)
        {
            for (var x = 0; x < carte.Largeur; x++)
            {
                var i = y * carte.Largeur + x;
                midi[i] = (sbyte)Math.Clamp(Math.Round(Climat.Temperature(carte, x, y, debut + 12 * Calendrier.SecondesParHeure) + ecart), -128, 127);
                aube[i] = (sbyte)Math.Clamp(Math.Round(Climat.Temperature(carte, x, y, debut + 5 * Calendrier.SecondesParHeure) + ecart), -128, 127);
            }
        }
        return new TemperaturesDto((int)(debut / Calendrier.SecondesParJour), carte.Largeur, carte.Hauteur, midi, aube);
    }
}

/// <summary>
/// Le camp et les bêtes autour de la caméra pour la vue 3D (0.41.1), en nombres : <paramref name="Huttes"/> par 5 (id, x, y,
/// solidité en millièmes, enduite 1/0) ; <paramref name="Champs"/> par 6 (id, x, y, ares, état, croissance en millièmes) ;
/// <paramref name="Betes"/> par 13 (id, x, y, espèce, forme, poids en kg, adultes, petits, gardées 1/0, gardien, docilité en millièmes ;
/// 0.44.0 ; puis bête connue et minutes depuis sa dernière attaque, −1 sinon ; 0.45.1).
/// </summary>
public sealed record CampDto(int[] Huttes, int[] Champs, int[] Betes)
{
    public static CampDto Depuis(MondeSimule monde, int x, int y, int rayon)
    {
        var (huttes, champs, betes) = monde.Camp(x, y, Math.Clamp(rayon, 0, SceneDto.RayonMax));
        return new CampDto(
            [.. huttes.SelectMany(h => new[] { h.Id, h.X, h.Y, (int)Math.Round(h.Solidite * 1000), h.Enduite ? 1 : 0 })],
            [.. champs.SelectMany(c => new[] { c.Id, c.X, c.Y, (int)Math.Round(c.Ares), c.Etat, (int)Math.Round(c.Croissance * 1000) })],
            [.. betes.SelectMany(b => new[] { b.Id, b.X, b.Y, b.Espece, b.Forme, (int)Math.Round(b.Poids), b.Adultes, b.Petits, b.Gardees ? 1 : 0, b.GardienId,
                (int)Math.Round(b.Docilite * 1000), b.Fauve, b.MinutesDepuisAttaque })]);
    }
}

/// <summary>
/// Le déroulé d'un assaut pour la vue 3D (0.42.0). <paramref name="Duels"/> par 5 : assaillant, défenseur, arme de l'assaillant,
/// arme du défenseur, issue (voir <see cref="IssueDuel"/>), dans l'ordre où ils ont eu lieu. <paramref name="Secondes"/> : instant de
/// l'assaut ; <paramref name="X"/>, <paramref name="Y"/> : camp attaqué.
/// </summary>
public sealed record AssautDto(long Id, long Secondes, int X, int Y, int Assaillants, int Cible, int[] Troupe, int[] Defenseurs,
    int[] Endormis, int[] Duels, int[] MortsDeBlessures)
{
    public static AssautDto[] Depuis(MondeSimule monde, int x, int y, int rayon) =>
    [
        .. monde.Assauts(x, y, Math.Clamp(rayon, 0, SceneDto.RayonMax)).Select(a => new AssautDto(a.EvenementId, a.Secondes, a.X, a.Y,
            a.Assaillants, a.Cible, a.Troupe, a.Defenseurs, a.Endormis,
            [.. a.Duels.SelectMany(d => new[] { d.Assaillant, d.Defenseur, (int)d.ArmeAssaillant, (int)d.ArmeDefenseur, (int)d.Issue })],
            [.. a.MortsDeBlessures])),
    ];
}
