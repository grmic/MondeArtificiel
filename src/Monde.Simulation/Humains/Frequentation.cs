using Monde.Simulation.Monde;

namespace Monde.Simulation.Humains;

/// <summary>
/// Combien de monde campe autour d'un lieu (0.39.1) : une communauté en disette préfère partir vers une terre où l'on campe moins,
/// parce qu'elle la partagerait. Un butineur compare ce qu'il gagnerait ailleurs, une fois partagé avec ceux qui y sont déjà
/// (théorème de la valeur marginale ; Charnov, 1976).
/// </summary>
public sealed partial class VieSociale
{
    /// <summary>Rayon (en cases) de la terre qu'un camp exploite autour de lui : environ 3 km.</summary>
    private const int RayonDeCueillette = 4;

    /// <summary>
    /// Adultes qu'une terre de ce rayon (environ 50 km²) nourrit sans s'appauvrir vite : les densités des chasseurs-cueilleurs vont de
    /// 0,05 à 0,5 habitant par km² selon les milieux (Binford, 2001 ; Marlowe, 2005) ; au-delà, chacun y gagne moins.
    /// </summary>
    private const double AdultesParTerre = 6;

    // Sommes cumulées des adultes par case (image intégrale), refaites chaque semaine.
    private int[]? _adultesCumules;

    /// <summary>Chaque semaine, avant les territoires : adultes cumulés par case, pour compter vite autour d'un lieu.</summary>
    internal void RecenserLesTerres(List<Humain> humains, Carte carte, long maintenant)
    {
        var (l, ht) = (carte.Largeur, carte.Hauteur);
        var adultes = _adultesCumules ??= new int[(l + 1) * (ht + 1)];
        var parCase = new int[l * ht];
        foreach (var h in humains.Vivants())
            if (h.EstVivant && !h.EstEnfant(maintenant) && !h.Mer.EnMer) parCase[carte.Indice(h.X, h.Y)]++;
        for (var y = 0; y < ht; y++)
        {
            var ligneA = 0;
            for (var x = 0; x < l; x++)
            {
                var i = y * l + x;
                ligneA += parCase[i];
                adultes[(y + 1) * (l + 1) + x + 1] = adultes[y * (l + 1) + x + 1] + ligneA;
            }
        }
    }

    /// <summary>Adultes qui campent dans la terre de cueillette autour de (cx, cy), au dernier recensement.</summary>
    private int AdultesAutour(int cx, int cy, Carte carte)
    {
        if (_adultesCumules is not { } a) return 0;
        var (x0, y0, x1, y1, l) = Fenetre(cx, cy, carte);
        return a[y1 * l + x1] - a[y0 * l + x1] - a[y1 * l + x0] + a[y0 * l + x0];
    }

    private static (int X0, int Y0, int X1, int Y1, int L) Fenetre(int cx, int cy, Carte carte) =>
        (Math.Max(0, cx - RayonDeCueillette), Math.Max(0, cy - RayonDeCueillette),
            Math.Min(carte.Largeur, cx + RayonDeCueillette + 1), Math.Min(carte.Hauteur, cy + RayonDeCueillette + 1), carte.Largeur + 1);
}
