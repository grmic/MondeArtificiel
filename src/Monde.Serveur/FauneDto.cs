using Monde.Simulation;
using Monde.Simulation.Humains;

namespace Monde.Serveur;

/// <summary>
/// Une espèce de la faune, pour la vue 3D (0.44.0) : de quoi calculer son corps (forme, trait, poids) et son comportement à
/// l'affichage (régime, panique, agressivité, vie en troupeau). Panique et agressivité en millièmes.
/// </summary>
public sealed record EspeceDto(int Numero, int Forme, int Trait, int Poids, int Regime, int Panique, int Agressivite, bool Troupeau,
    bool Charognarde, string Nom, bool Disparue)
{
    public static List<EspeceDto> Toutes(MondeSimule monde) =>
    [
        .. monde.Faune.Betes.Select(b => new EspeceDto(b.Numero, b.Forme, b.Trait, (int)Math.Round(b.Poids), (int)b.Regime,
            (int)Math.Round(b.Panique * 1000), (int)Math.Round(b.Agressivite * 1000), b.Hierarchie, b.Charognarde, NomDe(b),
            monde.Faune.EstDisparue(b.Numero))),
    ];

    internal static string NomDe(Bete b) => b.Dit is Recit r ? L.Rediger(r) : b.Dit.ToString() ?? "";
}

/// <summary>La petite fiche d'une bête cliquée dans la vue 3D (0.44.0) : son nom et quelques lignes.</summary>
public sealed record FicheBeteDto(string Titre, List<string> Lignes)
{
    /// <summary><paramref name="id"/> &gt; 0 : un troupeau ; sinon des bêtes sauvages de l'espèce <paramref name="espece"/>.</summary>
    public static FicheBeteDto? Depuis(MondeSimule monde, int id, int espece)
    {
        var faune = monde.Faune;
        Troupeau? t = id > 0 ? monde.Troupeaux().Find(x => x.Id == id) : null;
        var numero = t?.Espece ?? espece;
        if (numero < 1 || numero > faune.Betes.Count) return null;
        var b = faune.Get(numero);
        var titre = EspeceDto.NomDe(b);
        titre = titre.Length > 0 ? char.ToUpper(titre[0]) + titre[1..] : titre;
        var lignes = new List<string>
        {
            L.T("bete3d.poids", ("kg", Math.Round(b.Poids * Math.Clamp(t?.Taille ?? 1, 0.3, 2)))),
            L.T($"bete3d.regime.{(int)b.Regime}"),
            L.T(b.Hierarchie ? "bete3d.troupeau" : b.Forme == 5 ? "bete3d.solitaire" : "bete3d.petitsgroupes"),
            L.T(b.Agressivite > 0.65 ? "bete3d.humeur.agressive" : b.Panique > 0.65 ? "bete3d.humeur.craintive" : "bete3d.humeur.calme"),
        };
        if (t is null)
            lignes.Add(L.T("bete3d.sauvage"));
        else
        {
            var gardien = t.GardienId > 0 && monde.NomHumain(t.GardienId) is { Length: > 0 } n ? n : null;
            var communaute = t.CommunauteId > 0 ? monde.Communautes().Find(c => c.Id == t.CommunauteId)?.Nom : null;
            lignes.Add(gardien is not null
                ? L.T("bete3d.gardee.par", ("nom", gardien))
                : communaute is not null ? L.T("bete3d.gardee.communaute", ("c", L.Communaute(communaute))) : L.T("bete3d.gardee"));
            lignes.Add(L.T("bete3d.nombre", ("n", (int)Math.Round(t.Adultes)), ("p", (int)Math.Round(t.Petits))));
            lignes.Add(L.T(t.Domestique ? "bete3d.domestique" : "bete3d.docilite", ("d", Math.Round(100 * t.Docilite)), ("g", t.Generations)));
        }
        if (faune.EstDisparue(numero)) lignes.Add(L.T("bete3d.disparue"));
        return new FicheBeteDto(titre, lignes);
    }
}
