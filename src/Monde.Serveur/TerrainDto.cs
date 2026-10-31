using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Serveur;

/// <summary>Une grotte ou un abri sous roche, pour la carte.</summary>
public sealed record GrotteVueDto(int X, int Y, bool SousRoche, int Metres, bool Source);

/// <summary>Une source, pour la carte : son type (Douce, Chaude, Salee) et son nom dans la langue de l'observateur.</summary>
public sealed record SourceVueDto(int X, int Y, string Type, string Nom);

/// <summary>Un gisement, pour la carte : ce qu'il est, à quelle profondeur, et combien de vivants s'en souviennent.</summary>
public sealed record GisementVueDto(int X, int Y, string Type, string Profondeur, int Connu);

/// <summary>
/// Le terrain et la mer pour les calques de la carte : une valeur par case (octets, en Base64) et les noms à afficher dans
/// la langue de l'observateur. <paramref name="Debit"/> : 0 hors des rivières, sinon 1 + 40 × log₁₀(1 + m³/s).
/// <paramref name="Profondeur"/> : fond de la mer en dizaines de mètres (255 au-delà). <paramref name="Pente"/> : en unités
/// d'altitude (40 m). <paramref name="Lits"/> : les rivières, par paires (case, case en aval). L'Entité voit tout : les
/// gisements que nul ne connaît sont montrés aussi. Depuis la 0.32.7 (mondes de la génération 2) : <paramref name="Parois"/> et
/// <paramref name="Passages"/>, par paires de cases voisines (une fois chacune) ; <paramref name="Cols"/>, des cases ;
/// <paramref name="Limon"/>, une valeur par case (0 à 250 : ce que le limon des crues ajoute à la pousse, de 0 à +100 %).
/// </summary>
public sealed record TerrainDto(int Largeur, int Hauteur, byte[] Roches, byte[] Sols, byte[] Pente, byte[] Debit, byte[] Profondeur,
    byte[] Rivages, byte[] Recifs, IReadOnlyList<int> Lits, IReadOnlyList<string> NomsRoches, IReadOnlyList<string> NomsSols, IReadOnlyList<string> NomsRivages,
    IReadOnlyList<GrotteVueDto> Grottes, IReadOnlyList<SourceVueDto> Sources, IReadOnlyList<GisementVueDto> Gisements,
    IReadOnlyList<int> Parois, IReadOnlyList<int> Passages, IReadOnlyList<int> Cols, byte[] Limon, IReadOnlyList<ChampVueDto>? Champs = null)
{
    public static TerrainDto Depuis(MondeSimule monde)
    {
        var carte = monde.Carte;
        var t = carte.Terrain;
        var m = carte.Mer;
        var n = carte.Largeur * carte.Hauteur;
        var debit = new byte[n];
        var lits = new List<int>();
        var profondeur = new byte[n];
        for (var i = 0; i < n; i++)
        {
            if (t.Debit[i] > 0)
            {
                debit[i] = (byte)Math.Clamp(1 + 40 * Math.Log10(1 + t.Debit[i]), 1, 255);
                if (t.Aval[i] >= 0) lits.AddRange([i, t.Aval[i]]);
            }
            profondeur[i] = (byte)Math.Clamp(Math.Round(m.Profondeur[i] / 10.0), 0, 255);
        }

        // Combien de vivants se souviennent de chaque gisement.
        var (_, humains) = monde.InstantaneHumains();
        var connus = new Dictionary<int, int>();
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            foreach (var l in h.Memoire.Lieux)
            {
                if (l.Type == TypeLieu.Gisement && t.GisementA(l.X, l.Y) is { } g) connus[g.Numero] = connus.GetValueOrDefault(g.Numero) + 1;
            }
        }

        // Le relief et le limon ne comptent que dans les mondes de la génération 2.
        var parois = new List<int>();
        var passages = new List<int>();
        var cols = new List<int>();
        var limon = new byte[n];
        if (monde.Configuration.Generation >= 2)
        {
            var r = carte.Relief;
            var l = carte.Largeur;
            for (var i = 0; i < n; i++)
            {
                if (r.Cols[i]) cols.Add(i);
                int x = i % l, y = i / l;
                foreach (var (dx, dy) in (ReadOnlySpan<(int, int)>)[(1, 0), (0, 1), (1, 1), (-1, 1)])
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= l || ny >= carte.Hauteur) continue;
                    var j = ny * l + nx;
                    switch (r.Entre(i, j))
                    {
                        case Franchissement.Paroi: parois.AddRange([i, j]); break;
                        case Franchissement.Passage: passages.AddRange([i, j]); break;
                    }
                }
            }
            var e = monde.CarteLimon();
            for (var i = 0; i < n; i++) limon[i] = (byte)Math.Min(250, e[i] / 40);
        }

        return new TerrainDto(carte.Largeur, carte.Hauteur,
            [.. t.Roches.Select(r => (byte)r)], [.. t.Sols.Select(s => (byte)s)], t.Pente, debit, profondeur,
            [.. m.Rivages.Select(r => (byte)r)], [.. m.Recif.Select(r => r ? (byte)1 : (byte)0)], lits,
            [.. Enum.GetValues<Roche>().Select(r => L.T($"roche.{r}"))],
            [.. Enum.GetValues<Sol>().Select(s => L.T($"sol.{s}"))],
            [.. Enum.GetValues<Rivage>().Select(r => r == Rivage.Aucun ? "" : L.T($"carac.rivage.{r}"))],
            [.. t.Grottes.Select(g => new GrotteVueDto(g.X, g.Y, g.SousRoche, g.Metres, g.Source))],
            [.. t.Sources.Select(s => new SourceVueDto(s.X, s.Y, s.Type.ToString(), L.T($"carac.sources.{s.Type}")))],
            [.. t.Gisements.Select(g => new GisementVueDto(g.X, g.Y, L.T($"gite.{g.Type}"), L.T($"enfouissement.{g.Profondeur}"),
                connus.GetValueOrDefault(g.Numero)))],
            parois, passages, cols, limon, ChampsDe(monde));
    }

    /// <summary>Les parcelles semées (0.33.0), case par case : plante, surface et état, et si sa lignée est domestiquée.</summary>
    private static List<ChampVueDto> ChampsDe(MondeSimule monde)
    {
        var (parcelles, lignees, _) = monde.Champs();
        var domestiquees = lignees.Where(l => l.Domestiquee).Select(l => l.Id).ToHashSet();
        return [.. parcelles.Where(p => p.Etat != EtatParcelle.Finie).Select(p => new ChampVueDto(p.X, p.Y, (int)p.Etat, Math.Round(p.Ares, 1),
            monde.Flore.Get(p.Espece).Dit is Monde.Simulation.Recits.Recit r ? L.Rediger(r) : monde.Flore.Get(p.Espece).Nom, domestiquees.Contains(p.LigneeId)))];
    }
}

/// <summary>Une parcelle sur la carte : état 0 semée, 1 qui pousse, 2 mûre.</summary>
public sealed record ChampVueDto(int X, int Y, int Etat, double Ares, string Plante, bool Domestiquee);
