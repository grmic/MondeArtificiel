namespace Monde.Simulation.Humains;

/// <summary>Pourquoi l'humain a choisi sa destination.</summary>
public enum Motif : byte
{
    Aucun = 0,
    /// <summary>Il voit ce qu'il cherche.</summary>
    Vu = 1,
    /// <summary>Il se souvient d'un lieu.</summary>
    Souvenir = 2,
    /// <summary>Il ne sait pas où aller et cherche au hasard.</summary>
    Inconnu = 3,
    /// <summary>L'instinct de reproduction le pousse vers quelqu'un.</summary>
    Desir = 4,
    /// <summary>Le groupe part ; c'est lui qui en a le souvenir ou l'élan.</summary>
    Guide = 5,
    /// <summary>Il fuit le feu.</summary>
    Feu = 6,
    /// <summary>Il fuit l'eau qui monte.</summary>
    Eau = 7,
    /// <summary>Il fuit le volcan.</summary>
    Volcan = 8,
    /// <summary>Il cherche un abri contre la tempête.</summary>
    Tempete = 9,
}

/// <summary>
/// Traduit une décision en pensée courte, pour l'observateur (les humains n'ont pas de langage).
/// Le choix d'une formulation ne consomme pas de hasard : la simulation reste la même avec ou sans pensées.
/// </summary>
public static class Penseur
{
    public static string? Penser(Humain h, Motif motif, Humain? autre, long maintenant)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return null;
        var e = h.Sexe == Sexe.Femme ? "e" : "";
        var vers = Direction(h);
        var nom = autre?.Nom ?? "l'autre";
        var variante = (int)((h.Id + maintenant / 600) % 2);
        string Choix(string a, string b) => variante == 0 ? a : b;

        return h.Activite switch
        {
            Activite.Boire => Choix("Je bois enfin.", "L'eau fraîche apaise ma gorge."),
            Activite.ChercherEau => motif switch
            {
                Motif.Vu => $"J'ai soif. Je vois de l'eau{vers}.",
                Motif.Souvenir => $"J'ai soif. Je me souviens d'une eau{vers}.",
                _ => Choix("J'ai soif et je ne vois pas d'eau. Je cherche plus loin.", "Ma gorge est sèche. Où trouver de l'eau ?"),
            },
            Activite.Cueillir => Choix("Je mange ce que je trouve ici.", "Ces plantes calment ma faim."),
            Activite.Chasser => Choix("Je guette une bête.", "J'essaie d'attraper une proie."),
            Activite.ChercherNourriture => motif switch
            {
                Motif.Vu => $"J'ai faim. Il y a de quoi manger{vers}.",
                Motif.Souvenir => $"J'ai faim. Je me souviens d'un endroit où manger{vers}.",
                _ => "J'ai faim et il n'y a rien autour. Je cherche plus loin.",
            },
            Activite.Dormir => h.Fatigue > 80 ? $"Je suis épuisé{e}." : Choix("Je ferme les yeux.", "Le sommeil me gagne."),
            Activite.SAbriter => Choix("J'ai froid. Je me recroqueville.", "Le froid me mord. Je ne bouge plus."),
            Activite.ChercherAbri => motif == Motif.Souvenir
                ? $"J'ai froid. Je me souviens d'un endroit abrité{vers}."
                : $"J'ai froid. Il ferait meilleur{vers}.",
            Activite.RejoindrePartenaire => motif == Motif.Desir
                ? $"Quelque chose me pousse vers {nom}."
                : Choix($"Je veux retrouver {nom}.", $"{nom} est loin. Je vais vers {Pronom(autre)}."),
            Activite.SAccoupler => $"Je m'unis à {nom}.",
            Activite.VeillerEnfant => $"Où est {nom} ? Je vais {Pronom(autre, complement: true)} chercher.",
            Activite.SuivreParent => $"Je reste près de {nom}.",
            Activite.Explorer => Choix($"Que trouve-t-on{vers} ?", $"Je pars voir{vers}."),
            Activite.EviterRival => Choix($"Je ne supporte plus {nom}. Je m'éloigne.", $"{nom} m'exaspère. Je pars{vers}."),
            Activite.RejoindreProche => Choix($"Je vais retrouver {nom}.", $"{nom} me manque. Je vais vers {Pronom(autre)}."),
            Activite.Repos when h.Chagrin > 30 && autre is not null && !autre.EstVivant =>
                Choix($"Je pense à {nom}. {Majuscule(Pronom(autre, sujet: true))} n'est plus là.", $"{nom} me manque."),
            Activite.Migrer => motif == Motif.Guide
                ? Choix($"Ici, la terre ne nous nourrit plus. Je mène les miens{vers}.", $"Il faut partir. Je montre le chemin{vers}.")
                : Choix($"Les miens partent{vers}. Je les suis.", $"Ici, il n'y a plus rien. Nous partons{vers}."),
            Activite.Fuir => motif switch
            {
                Motif.Feu => Choix($"Le feu ! Je fuis les flammes{vers}.", $"La fumée m'étouffe. Je cours{vers}."),
                Motif.Eau => Choix($"L'eau monte ! Je cours vers les hauteurs{vers}.", $"L'eau arrive. Vite, plus haut{vers} !"),
                Motif.Volcan => Choix($"La montagne gronde et fume. Je m'en éloigne{vers}.", $"La montagne crache le feu. Je fuis{vers}."),
                Motif.Tempete => Choix($"Le vent hurle. Je cherche un abri{vers}.", "Les arbres plient. Il faut s'abriter."),
                _ => $"Il faut fuir{vers} !",
            },
            Activite.Repos when h.Reserves < 10 && h.Faim > 50 => Choix("Mes os percent ma peau.", "Je n'ai plus de forces."),
            Activite.Repos when h.EstDeprime => Choix("Rien n'a plus de goût.", "Je n'ai envie de rien."),
            Activite.Repos when h.Humeur > 75 => Choix("Il fait bon vivre ici.", "Je me sens bien."),
            Activite.Repos => Choix("Tout est calme. Je me repose.", "Rien ne presse."),
            _ => null,
        };
    }

    private static string Pronom(Humain? autre, bool complement = false, bool sujet = false) =>
        autre?.Sexe == Sexe.Femme ? (complement ? "la" : "elle") : (complement ? "le" : sujet ? "il" : "lui");

    private static string Majuscule(string mot) => char.ToUpperInvariant(mot[0]) + mot[1..];

    /// <summary>« vers le nord-est », d'après la destination ; vide s'il n'y en a pas.</summary>
    internal static string Direction(Humain h)
    {
        if (h.CibleX < 0) return "";
        int dx = h.CibleX - h.X, dy = h.CibleY - h.Y;
        if (dx == 0 && dy == 0) return " ici";
        var ns = Math.Abs(dy) * 2 >= Math.Abs(dx) ? (dy < 0 ? "nord" : "sud") : "";
        var eo = Math.Abs(dx) * 2 >= Math.Abs(dy) ? (dx < 0 ? "ouest" : "est") : "";
        return $" vers le {(ns.Length > 0 && eo.Length > 0 ? $"{ns}-{eo}" : ns + eo)}";
    }
}
