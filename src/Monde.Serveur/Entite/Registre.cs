using System.Text.Json;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Stockage;

namespace Monde.Serveur.Entite;

/// <summary>Ce qu'un acte a donné : en mots, son issue (1 effet, 0 sans effet, −1 refusé) et le nombre d'humains ou de prières touchés.</summary>
/// <param name="Defaire">Pour un acte libre : de quoi le défaire (JSON).</param>
public sealed record FaitEntite(IReadOnlyList<string> Resultats, int Issue, int Nombre, string Defaire = "");

/// <summary>
/// Registre des actes de l'Entité (table <c>entite_actes</c>) : chaque pouvoir exercé, par le joueur ou par le LLM, y est noté avec
/// sa cible, ce qu'il a donné et les lignes du journal qu'il a écrites.
/// </summary>
public static class Registre
{
    public const int Joueur = 0, Llm = 1;

    /// <summary>
    /// Exerce un pouvoir et le note : <paramref name="faire"/> s'exécute d'un bloc (les lignes du journal écrites sont celles de
    /// l'acte). Une demande invalide lève <see cref="ArgumentException"/> et n'est pas notée.
    /// </summary>
    public static T Faire<T>(ServiceMonde service, int auteur, ActeLlm acte, Func<MondeSimule, T> faire, Func<T, FaitEntite> lire, long echange = 0)
    {
        var (resultat, premier, dernier) = service.Monde.Executer(faire);
        Noter(service, auteur, acte, lire(resultat), premier, dernier, echange);
        return resultat;
    }

    /// <summary>Note un acte déjà exercé.</summary>
    public static void Noter(ServiceMonde service, int auteur, ActeLlm acte, FaitEntite fait, long premier, long dernier, long echange = 0)
    {
        var monde = service.Monde;
        var (humain, communaute, genre) = Cible(monde, acte);
        var ecrit = dernier >= premier;
        // Rien d'écrit au journal : rien ne s'est passé dans le monde (une communauté vide, un humain déjà guéri…).
        if (!ecrit && fait.Issue == 1) fait = fait with { Issue = 0 };
        if (auteur == Llm && ecrit) MarquesLlm.Ajouter(premier, dernier);
        if (acte.Type == Actes.Libre && ecrit) MarquesLibre.Ajouter(premier, dernier);
        service.Depot.AjouterActe(new ActeEntite(0, monde.Secondes, auteur, acte.Type, genre, humain, communaute, acte.Priere ?? 0, fait.Nombre,
            JsonSerializer.Serialize(acte, Actes.Json), JsonSerializer.Serialize(fait.Resultats, Actes.Json), fait.Issue,
            ecrit ? premier : 0, ecrit ? dernier : -1, echange, false, fait.Defaire));
    }

    /// <summary>Humain et communauté visés, et la précision (geste, parole, catastrophe).</summary>
    private static (int Humain, int Communaute, string Genre) Cible(MondeSimule monde, ActeLlm a)
    {
        int Seul(int[]? t) => t is { Length: 1 } ? t[0] : 0;
        switch (a.Type)
        {
            case Actes.Geste:
            {
                var surHumain = Enum.TryParse<GesteEntite>(a.Geste, true, out var g) && Enum.IsDefined(g) && Gestes.SurHumain(g);
                var cible = a.Cible ?? a.Humain ?? a.Communaute ?? 0;
                var communaute = 0;
                if (surHumain)
                {
                    var (_, humains) = monde.InstantaneHumains();
                    if (cible >= 1 && cible <= humains.Count) communaute = humains[cible - 1].CommunauteId;
                }
                return surHumain ? (cible, communaute, a.Geste ?? "") : (0, cible, a.Geste ?? "");
            }
            case Actes.Parler:
            case Actes.Penchant:
                return (a.Humain ?? Seul(a.Humains), Seul(a.Communautes), a.Parole ?? "");
            case Actes.ParlerCommunaute:
                return (0, a.Communaute ?? 0, a.Parole ?? "");
            case Actes.ParlerMonde:
                return (a.Cible ?? 0, a.Communaute ?? 0, a.Parole ?? "");
            case Actes.Catastrophe:
                return (0, 0, a.Catastrophe ?? "");
            case Actes.Libre:
            {
                var c = a.Changements ?? [];
                var h = c.Select(x => x.Humain).FirstOrDefault(x => x > 0);
                var (_, humains) = monde.InstantaneHumains();
                var communaute = c.Select(x => x.Communaute).FirstOrDefault(x => x > 0);
                if (communaute == 0 && h >= 1 && h <= humains.Count) communaute = humains[h - 1].CommunauteId;
                return (h, communaute, a.Visible == true ? "visible" : "cache");
            }
            case Actes.Exaucer:
            case Actes.Refuser:
            {
                var p = monde.Prieres().FirstOrDefault(x => x.Id == a.Priere);
                return (p?.HumainId ?? 0, p?.CommunauteId ?? 0, "");
            }
            default:
                return (a.Humain ?? a.Cible ?? 0, a.Communaute ?? 0, a.Geste ?? "");
        }
    }

    /// <summary>Ce qu'a donné un acte qui rend un texte : un effet s'il a écrit au journal.</summary>
    public static FaitEntite Texte(string texte, bool effet, int nombre = 1) => new([texte], effet ? 1 : 0, nombre);

    /// <summary>Ce qu'a donné une parole ou un penchant : ce que chacun en a fait.</summary>
    public static FaitEntite Chacun(MondeSimule monde, List<(int Humain, Recit Resultat)> r)
    {
        var (_, humains) = monde.InstantaneHumains();
        string Nom(int id) => id >= 1 && id <= humains.Count ? humains[id - 1].Nom : "?";
        var lignes = r.Take(40).Select(x => $"{Nom(x.Humain)} : {L.Rediger(x.Resultat)}").ToList();
        if (r.Count > 40) lignes.Add(L.T("llm.resultat.etAutres", ("n", r.Count - 40)));
        if (r.Count == 0) lignes.Add(L.T("llm.resultat.personne"));
        return new FaitEntite(lignes, r.Count > 0 ? 1 : 0, r.Count);
    }

    // Les demandes du joueur, écrites comme les actes du LLM : une seule forme dans le registre.

    public static ActeLlm Depuis(DemandeGeste d) => new()
    {
        Type = Actes.Geste, Geste = d.Geste, Cible = d.Cible, Maniere = d.Maniere, Param = d.Param, Calamite = d.Calamite, Force = d.Force, Jours = d.Jours,
    };

    public static ActeLlm Depuis(DemandeParole d) => new()
    {
        Type = Actes.Parler, Humains = d.Humains, Communautes = d.Communautes, Parole = d.Parole, Mode = d.Mode, Cible = d.Cible, Param = d.Param,
    };

    public static ActeLlm Depuis(DemandeParoleCommunaute d) => new()
    {
        Type = Actes.ParlerCommunaute, Communaute = d.Communaute, Parole = d.Parole, Mode = d.Mode, Cibles = d.Cibles, Direction = d.Direction,
    };

    public static ActeLlm Depuis(DemandeParoleMonde d) => new()
    {
        Type = Actes.ParlerMonde, Parole = d.Parole, Mode = d.Mode, Terre = d.Terre, Cible = d.Cible, Communaute = d.Communaute, Param = d.Param,
    };

    public static ActeLlm Depuis(DemandePenchant d) => new()
    {
        Type = Actes.Penchant, Humains = d.Humains, Communautes = d.Communautes, Cible = d.Cible, Mutuel = d.Mutuel, Passion = d.Passion,
    };

    public static ActeLlm Depuis(DemandeCatastrophe d) => new() { Type = Actes.Catastrophe, Catastrophe = d.Type, X = d.X, Y = d.Y, Intensite = d.Intensite };

    public static ActeLlm Depuis(DemandeIntuition d) => new() { Type = Actes.SoufflerGeste, Humain = d.Humain, Geste = d.Geste, A = d.A, B = d.B, Espece = d.Espece };

    public static ActeLlm Depuis(DemandeCombinaison d) => new() { Type = Actes.SoufflerCombinaison, Humain = d.Humain, Operation = d.Operation, A = d.A, B = d.B, Lien = d.Lien };
}
