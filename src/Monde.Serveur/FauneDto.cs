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
        .. ((monde.Configuration?.Generation ?? 0) >= 2 ? monde.Faune.Betes.Concat(monde.Faune.Nageurs) : monde.Faune.Betes).Select(b => new EspeceDto(b.Numero, b.Forme, b.Trait, (int)Math.Round(b.Poids), (int)b.Regime,
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
        if (!faune.Existe(numero)) return null;
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
        // Une bête connue des humains (0.45.1) : ce qu'elle a fait.
        var connue = id <= -MondeSimule.IdFauveScene && id > -MondeSimule.IdNageurScene
            ? monde.FauvesConnus().Connus.Find(f => f.Id == -id - MondeSimule.IdFauveScene)
            : null;
        if (connue is not null) lignes.AddRange(FauveDto.LignesDe(monde, connue));
        else if (t is null)
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

/// <summary>
/// La fenêtre Faune et le calque des animaux (0.44.2) : chaque espèce avec sa population (sauvages estimées, gardées comptées),
/// le gibier de chaque case (entiers sur 16 bits, petit-boutiste, en base 64) et les troupeaux gardés (case, espèce, nombre).
/// </summary>
public sealed record PopulationFauneDto(List<PopulationEspeceDto> Especes, string Gibier, double KgVifParPart, List<TroupeauCarteDto> Troupeaux,
    List<FauveDto>? Connus = null, List<AttaqueCarteDto>? Attaques = null)
{
    public static PopulationFauneDto Depuis(MondeSimule monde)
    {
        var p = monde.Population();
        var faune = monde.Faune;
        var especes = p.Especes.Select(e =>
        {
            var b = faune.Get(e.Numero);
            return new PopulationEspeceDto(e.Numero, EspeceDto.NomDe(b), L.T($"biome.{b.Biome}"), (int)b.Biome, b.Forme, Math.Round(b.Poids, 1), (int)b.Regime,
                (long)Math.Round(e.Sauvages), e.SauvagesAvant is { } a ? (long)Math.Round(a) : null, e.Mois,
                (int)Math.Round(e.GardeesAdultes), (int)Math.Round(e.GardeesPetits), e.Troupeaux, e.Domestique, faune.EstDisparue(e.Numero));
        }).ToList();
        var octets = new byte[p.Gibier.Length * 2];
        for (var i = 0; i < p.Gibier.Length; i++) (octets[2 * i], octets[2 * i + 1]) = ((byte)(p.Gibier[i] & 0xFF), (byte)(p.Gibier[i] >> 8));
        var troupeaux = p.Troupeaux.Where(t => t.Case.X >= 0)
            .Select(t => new TroupeauCarteDto(t.Case.X, t.Case.Y, t.Troupeau.Espece, (int)Math.Round(t.Troupeau.Adultes), (int)Math.Round(t.Troupeau.Petits),
                t.Troupeau.Domestique))
            .ToList();
        // 0.45.1 : les bêtes connues et les attaques de l'année, pour la fenêtre Faune et le calque des animaux.
        var (connus, attaques) = monde.FauvesConnus();
        var maintenant = monde.Secondes;
        return new PopulationFauneDto(especes, Convert.ToBase64String(octets), MondeSimule.KgVifParPartDeGibier, troupeaux, FauveDto.Tous(monde),
            [.. attaques.Select(a => new AttaqueCarteDto(a.X, a.Y, (int)((maintenant - a.Le) / Monde.Simulation.Temps.Calendrier.SecondesParJour), a.Espece, a.Mort,
                a.EvenementId, connus.Exists(f => f.Id == a.FauveId && f.EnVie) ? -(MondeSimule.IdFauveScene + a.FauveId) : 0))]);
    }
}

public sealed record PopulationEspeceDto(int Numero, string Nom, string Biome, int BiomeNumero, int Forme, double Poids, int Regime, long Sauvages,
    long? SauvagesAvant, int Mois, int GardeesAdultes, int GardeesPetits, int Troupeaux, bool Domestique, bool Disparue);

public sealed record TroupeauCarteDto(int X, int Y, int Espece, int Adultes, int Petits, bool Domestique);

/// <summary>
/// Une bête connue des humains (0.45.1) : celle qui a attaqué, à sa dernière place connue. <paramref name="Groupe"/> : son numéro dans
/// le flux Camp, pour la suivre en 3D.
/// </summary>
public sealed record FauveDto(int Id, int Espece, string Nom, int X, int Y, int Attaques, int Victimes, bool EnVie, string VueLe, int Groupe,
    List<string> Details)
{
    public static List<FauveDto> Tous(MondeSimule monde)
    {
        var (connus, _) = monde.FauvesConnus();
        return [.. connus.Where(f => monde.Faune.Existe(f.Espece)).OrderByDescending(f => f.EnVie).ThenByDescending(f => f.VuLe).Select(f =>
            new FauveDto(f.Id, f.Espece, EspeceDto.NomDe(monde.Faune.Get(f.Espece)), f.X, f.Y, f.Attaques, f.Victimes, f.EnVie, L.Date(f.VuLe),
                -(MondeSimule.IdFauveScene + f.Id), LignesDe(monde, f)))];
    }

    internal static List<string> LignesDe(MondeSimule monde, Fauve f)
    {
        var l = new List<string> { L.T("fauve3d.connue", ("a", f.Attaques), ("v", f.Victimes)), L.T("fauve3d.age", ("n", Math.Max(1, (int)Math.Round(f.Age)))) };
        if (f.Blessure >= 0.3) l.Add(L.T("fauve3d.blessure"));
        if (f.Habitude >= 0.5) l.Add(L.T("fauve3d.habitude"));
        if (!f.EnVie)
            l.Add(f.TueParId > 0 && monde.NomHumain(f.TueParId) is { Length: > 0 } n ? L.T("fauve3d.tueePar", ("nom", n)) : L.T("fauve3d.morte"));
        return l;
    }
}

/// <summary>Une attaque de l'année, pour la carte (0.45.1) : où, depuis combien de jours, l'espèce, mortelle ou non, la ligne du journal.</summary>
public sealed record AttaqueCarteDto(int X, int Y, int Jours, int Espece, bool Mort, long Evenement, int Groupe);
