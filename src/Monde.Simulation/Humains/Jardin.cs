using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Ce que contient le jardin secret d'un humain.</summary>
public enum TypeIntime : byte
{
    /// <summary>Ce qu'il a l'intention de faire (se venger, partir, suivre ou refuser une guerre…).</summary>
    Intention = 0,
    Peur = 1,
    Espoir = 2,
    Joie = 3,
    Peine = 4,
    /// <summary>Ce qu'il pense d'un autre.</summary>
    Opinion = 5,
    /// <summary>Ce qu'il pense de lui-même.</summary>
    ImageDeSoi = 6,
    /// <summary>Ce qu'il croit du monde et de l'invisible.</summary>
    Croyance = 7,
    /// <summary>Ce qu'il se demande (le pourquoi des choses).</summary>
    Question = 8,
}

/// <summary>
/// Une chose intime : sa nature, qui elle concerne (0 : personne en particulier), ce qu'il se dit, sa force (0 à 100),
/// l'événement qui l'a fait naître, et une clé : une nouvelle chose de même clé remplace l'ancienne (une seule opinion
/// sur une même personne, une seule image de soi…).
/// </summary>
public sealed record Intime(long Secondes, TypeIntime Type, int SujetId, string Texte, double Intensite, long CauseId, string Cle);

/// <summary>
/// Le jardin secret : pensées et intentions intimes, peurs, espoirs, joies et peines, ce qu'il pense des autres et de
/// lui-même, ses croyances et ses questions. Personne d'autre ne le lit (sauf l'observateur). Les choses s'y effacent
/// avec le temps, plus ou moins vite selon la mémoire de chacun ; ce qui est fort dure plus longtemps.
/// </summary>
public static class Jardin
{
    /// <summary>Nombre de choses gardées : au-delà, la plus faible s'efface.</summary>
    public const int Taille = 40;

    /// <summary>Choses gardées après la mort (les plus fortes), pour que l'observateur puisse encore les lire.</summary>
    public const int TailleApresLaMort = 12;

    /// <summary>En dessous de cette force, la chose est oubliée.</summary>
    private const double Seuil = 5;

    /// <summary>Confie une chose au jardin d'un humain : elle remplace celle de même clé, ou la plus faible si le jardin est plein.</summary>
    public static void Confier(Humain h, TypeIntime type, int sujet, string texte, double intensite, long cause, long maintenant, string cle)
    {
        if (!h.EstVivant || intensite < Seuil) return;
        var jardin = h.Memoire.Jardin;
        var e = new Intime(maintenant, type, sujet, texte, Math.Min(100, intensite), cause, cle);
        var k = jardin.FindIndex(x => x.Cle == cle);
        if (k >= 0)
        {
            // Même chose, même force ou presque : rien de neuf, la date reste celle où elle est née.
            if (jardin[k].Texte == texte && Math.Abs(jardin[k].Intensite - e.Intensite) < 10) return;
            jardin.RemoveAt(k);
        }
        else if (jardin.Count >= Taille)
        {
            var faible = jardin.Select((x, i) => (x.Intensite, i)).MinBy(x => x.Intensite);
            if (faible.Intensite > e.Intensite) return;
            jardin.RemoveAt(faible.i);
        }
        jardin.Add(e);
    }

    /// <summary>Retire une chose (une intention accomplie ou abandonnée, une peur dissipée).</summary>
    public static void Retirer(Humain h, string cle) => h.Memoire.Jardin.RemoveAll(x => x.Cle == cle);

    /// <summary>
    /// Une épreuve laisse sa trace sur le caractère (voir <see cref="Psyche.Eprouver"/>) ; quand elle est profonde,
    /// l'humain le sait et le garde pour lui, avec l'événement qui l'a changé.
    /// </summary>
    public static void Marquer(Humain h, Epreuve epreuve, double intensite, long cause, long maintenant, Monde.Alea alea)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return;
        var (apres, trace) = Psyche.Eprouver(h.Personnalite, epreuve, intensite, alea);
        h.Personnalite = apres;
        if (trace is not null) Confier(h, TypeIntime.ImageDeSoi, 0, trace, 40 + 40 * intensite, cause, maintenant, $"epreuve:{epreuve}");
    }

    /// <summary>
    /// Une semaine passe : chaque chose s'estompe, d'autant moins vite que la mémoire est bonne et la chose forte
    /// (demi-vie de quelques semaines à plusieurs mois ; les croyances tiennent bien plus longtemps).
    /// </summary>
    public static void Estomper(Humain h)
    {
        var jardin = h.Memoire.Jardin;
        if (jardin.Count == 0) return;
        if (!h.EstVivant)
        {
            if (jardin.Count > TailleApresLaMort)
            {
                var gardees = jardin.OrderByDescending(x => x.Intensite).Take(TailleApresLaMort).ToHashSet();
                jardin.RemoveAll(x => !gardees.Contains(x));
            }
            return;
        }
        var memoire = Math.Clamp(h.Personnalite.Intelligence.Memoire / 100, 0.5, 1.6);
        for (var i = jardin.Count - 1; i >= 0; i--)
        {
            var x = jardin[i];
            var semaines = x.Type switch
            {
                TypeIntime.Croyance => 80,
                TypeIntime.Opinion or TypeIntime.ImageDeSoi => 20,
                _ => 6,
            } * memoire * (0.5 + x.Intensite / 100);
            var reste = x.Intensite * Math.Pow(0.5, 1 / semaines);
            if (reste < Seuil) jardin.RemoveAt(i);
            else jardin[i] = x with { Intensite = reste };
        }
    }

    /// <summary>
    /// Une fois par semaine, chacun fait le point en silence : ce qu'il pense de ceux qui comptent pour lui, ce qu'il
    /// pense de lui-même, ce qu'il craint et ce qu'il espère, d'après ce qu'il vit.
    /// </summary>
    public static void Mediter(Humain h, IReadOnlyList<Humain> humains, IReadOnlyList<Communaute> communautes, long maintenant)
    {
        Estomper(h);
        if (!h.EstVivant || h.EstEnfant(maintenant)) return;
        var p = h.Personnalite;

        // Ce qu'il pense des autres : ceux qui comptent le plus, en bien ou en mal.
        foreach (var l in h.Liens.Where(l => Math.Abs(l.Affection) >= 40).OrderByDescending(l => Math.Abs(l.Affection)).Take(4))
        {
            var autre = humains[l.Id - 1];
            var grief = h.Griefs.Where(g => g.ContreId == l.Id).MaxBy(g => g.Intensite);
            var texte = (l.Affection, grief) switch
            {
                (_, { Motif: MotifGrief.Meurtre, VictimeId: > 0 } g) => $"Je hais {autre.Nom} : {Il(autre)} a tué {humains[g.VictimeId - 1].Nom}.",
                (_, { Motif: MotifGrief.Infidelite }) => $"{autre.Nom} m'a trahi{E(h)} ; je ne l'oublie pas.",
                (_, { Motif: MotifGrief.Coups }) => $"{autre.Nom} m'a frappé{E(h)} : je me méfie {De(autre)}.",
                ( >= 75, _) when h.PartenaireId == l.Id => $"{autre.Nom} est tout pour moi.",
                ( >= 75, _) => $"{autre.Nom} est un être cher ; je ferais tout pour {Pronom(autre)}.",
                ( >= 40, _) => $"Je peux compter sur {autre.Nom}.",
                ( <= -60, _) => $"Je ne supporte plus {autre.Nom}.",
                _ => $"Je me méfie de {autre.Nom}.",
            };
            Confier(h, TypeIntime.Opinion, l.Id, texte, Math.Abs(l.Affection), grief?.EvenementId ?? 0, maintenant, $"opinion:{l.Id}");
        }

        // Ce qu'il pense de lui-même.
        var communaute = h.CommunauteId > 0 && h.CommunauteId <= communautes.Count ? communautes[h.CommunauteId - 1] : null;
        var (soi, force) = h switch
        {
            { DeprimeDepuis: not null } => ("Je ne vaux rien ; à quoi bon ?", 80.0),
            { JoursSeul: >= 14 } => ("Personne ne veut de moi.", 60.0),
            _ when communaute?.FigureId == h.Id => ("Les miens se tournent vers moi : je dois les guider.", 70.0),
            { Sante: < 40 } => ("Mon corps me trahit ; je ne suis plus bon à grand-chose.", 55.0),
            _ when h.Humeur >= 70 && p.Extraversion >= 0.6 => ("Je suis fort et les autres m'apprécient.", 45.0),
            _ when h.Humeur < 30 => ("Rien ne me réussit.", 45.0),
            _ when p.Nevrosisme >= 0.7 => ("Je crains de ne pas être à la hauteur.", 35.0),
            _ => ("Je fais ce qu'il faut pour vivre.", 15.0),
        };
        Confier(h, TypeIntime.ImageDeSoi, h.Id, soi, force, h.DepressionEvenementId, maintenant, "soi");

        // Ce qu'il craint et ce qu'il espère, selon ce qu'il vit.
        if (h.Faim >= 70 || h.Reserves < 15) Confier(h, TypeIntime.Peur, 0, "J'ai peur de mourir de faim.", 40 + h.Faim / 2, communaute?.DisetteEvenementId ?? 0, maintenant, "peur:faim");
        else Retirer(h, "peur:faim");
        if (communaute is { Disette: true, DestinationX: >= 0 })
        {
            Confier(h, TypeIntime.Espoir, 0, $"J'espère que les terres {Lieux.Vers(communaute.CentreX, communaute.CentreY, communaute.DestinationX, communaute.DestinationY)} nous nourriront.",
                50, communaute.DisetteEvenementId, maintenant, "espoir:migration");
        }
        if (h.EnceinteDepuis is not null) Confier(h, TypeIntime.Espoir, 0, "J'espère que l'enfant que je porte naîtra en bonne santé.", 55, 0, maintenant, "espoir:enfant");
        else Retirer(h, "espoir:enfant");
        if (h.Peur >= 30) Confier(h, TypeIntime.Peur, 0, $"J'ai peur que {TypesCatastrophe.Nom(h.PeurType)} revienne.", h.Peur, h.PeurEvenementId, maintenant, "peur:catastrophe");
    }

    /// <summary>Une perte : la peine du deuil.</summary>
    public static void Pleurer(Humain h, Humain mort, string parente, double chagrin, long maintenant) =>
        Confier(h, TypeIntime.Peine, mort.Id, $"La mort de {parente} me ronge.", chagrin, mort.MortEvenementId, maintenant, $"deuil:{mort.Id}");

    /// <summary>
    /// Après un phénomène terrifiant, certains y voient une volonté cachée (on prête volontiers une intention à ce qui
    /// nous dépasse : Guthrie, 1993 ; Barrett, 2000), surtout les esprits portés vers l'invisible (transcendance) ;
    /// les curieux, eux, se demandent ce qui l'a causé.
    /// </summary>
    public static void Interpreter(Humain h, TypeCatastrophe type, long evenement, double peur, Monde.Alea alea, long maintenant)
    {
        if (!h.EstVivant || h.EstEnfant(maintenant)) return;
        var p = h.Personnalite;
        var nom = TypesCatastrophe.Nom(type);
        if (alea.Reel() < 0.6 * p.Transcendance * Math.Min(1, peur / 50))
        {
            var croyance = type switch
            {
                TypeCatastrophe.Seisme => "Quelque chose de très puissant vit sous la terre ; quand il s'agite, tout tremble.",
                TypeCatastrophe.Eruption => "La montagne est vivante, et sa colère brûle.",
                TypeCatastrophe.Tsunami => "La grande eau a voulu nous prendre ; elle est en colère contre nous.",
                TypeCatastrophe.Crue => "La rivière a ses humeurs : elle donne et elle reprend.",
                TypeCatastrophe.Incendie => "Le feu tombé du ciel punit ceux qui l'ont mérité.",
                TypeCatastrophe.Cyclone => "Le vent a une volonté ; il frappe qui il veut.",
                TypeCatastrophe.Epidemie => "Quelqu'un d'invisible nous rend malades.",
                TypeCatastrophe.Comete or TypeCatastrophe.Bolide or TypeCatastrophe.Impact or TypeCatastrophe.Eclipse or TypeCatastrophe.Aurore
                    => "Le ciel nous parle ; ses signes annoncent quelque chose.",
                TypeCatastrophe.VagueDeFroid or TypeCatastrophe.HiverVolcanique => "Le froid est envoyé par quelqu'un que nous avons fâché.",
                _ => "Ce qui nous frappe n'arrive pas par hasard : quelqu'un, quelque part, le veut.",
            };
            Confier(h, TypeIntime.Croyance, 0, croyance, 30 + 70 * p.Transcendance, evenement, maintenant, $"croyance:{type}");
        }
        else if (alea.Reel() < 0.5 * p.Ouverture)
        {
            Confier(h, TypeIntime.Question, 0, $"Pourquoi {nom} ? D'où cela vient-il, et qui l'a voulu ?", 30 + 50 * p.Ouverture, evenement, maintenant, $"question:{type}");
        }
    }

    private static string E(Humain h) => h.Sexe == Sexe.Femme ? "e" : "";

    private static string Il(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "il";

    private static string De(Humain h) => h.Sexe == Sexe.Femme ? "d'elle" : "de lui";

    private static string Pronom(Humain h) => h.Sexe == Sexe.Femme ? "elle" : "lui";
}
