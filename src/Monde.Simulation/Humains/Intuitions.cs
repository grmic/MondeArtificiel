using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les idées qui naissent d'elles-mêmes. Au calme, un esprit ingénieux revient sur ce qu'il sait et se pose une question :
/// « ce qui marche avec ceci marcherait-il avec cela, qui lui ressemble ? ». Il l'essaie, et conclut : juste, quand le geste
/// donne quelque chose ; faux, parfois, quand un essai malheureux lui fait rejeter une idée qui aurait pu marcher. La
/// pensée par analogie est au cœur de l'invention (Gentner, 1983 ; Holyoak et Thagard, 1995) ; elle vient plus aux
/// intelligents et aux curieux, et davantage quand un besoin presse.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Deux matières se ressemblent pour l'esprit si elles partagent une forme (long, rond, creux, pointu, tranchant…) ou
    /// ont presque la même dureté.
    /// </summary>
    private static bool Analogue(Matiere a, Matiere b)
    {
        if (a == b || a == Matiere.Aucune || b == Matiere.Aucune) return false;
        var (pa, pb) = (Lois.De(a), Lois.De(b));
        // L'eau, le feu, une plante : pas des objets qu'on compare par leur forme.
        if (pa.Masse <= 0 || pb.Masse <= 0) return false;
        return (pa.Forme & pb.Forme) != 0 || Math.Abs(pa.Durete - pb.Durete) < 0.08;
    }

    /// <summary>
    /// Au repos : peut-être une idée, tirée d'un savoir sûr, en changeant l'une de ses matières pour une autre qui lui
    /// ressemble et qu'il a sous la main. L'essai suit, et sa conclusion, juste ou fausse, lui reste. Vrai s'il a essayé.
    /// </summary>
    internal bool AvoirUneIdee(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.Savoirs.Count == 0) return false;
        var p = h.Personnalite;
        var besoin = h.Faim > 40 || h.Soif > 40 || h.Froid > 30 ? 2 : 1;
        var ingenieux = 0.2 + Ingeniosite(h);
        if (alea.Reel() >= 0.002 * ingenieux * ingenieux * (0.5 + p.Ouverture) * besoin) return false;

        var depart = h.Savoirs[alea.Entier(h.Savoirs.Count)];
        if (depart.Confiance < 0.5 || depart.Espece != 0 || depart.Pieces.Length > 0) return false;
        var forme = Physique.Formes.FirstOrDefault(f => f.Geste == depart.Geste);
        if (forme is null || !Physique.Convient(depart.Geste, depart.A, depart.B)) return false;
        // Changer la première matière, ou la seconde.
        var surA = depart.B == Matiere.Aucune || alea.Reel() < 0.5;
        var liste = surA ? forme.A : forme.B;
        var ancienne = surA ? depart.A : depart.B;
        Span<Matiere> possibles = stackalloc Matiere[liste.Length];
        var n = 0;
        foreach (var m in liste)
            if (Analogue(ancienne, m) && APortee(h, m)) possibles[n++] = m;
        if (n == 0) return false;
        var nouvelle = possibles[alea.Entier(n)];
        var (a, b) = surA ? (nouvelle, depart.B) : (depart.A, nouvelle);
        var cle = $"idee:{(int)depart.Geste}:{(int)a}:{(int)b}";
        if (h.Savoirs.Exists(s => s.Geste == depart.Geste && s.A == a && s.B == b) || h.Memoire.Jardin.Exists(x => x.Cle == cle)) return false;

        h.Activite = Activite.Experimenter;
        h.CibleX = h.CibleY = -1;
        var idee = Physique.Phrase(depart.Geste, a, b);
        var question = new Recit("idee.question", ("h", h), ("depart", Physique.Phrase(depart.Geste, depart.A, depart.B)), ("effet", Physique.NomEffet(depart.Effet)), ("idee", idee));
        var connu = h.Savoirs.Find(s => s.Geste == depart.Geste && s.A == a && s.B == b && s.Espece == 0);
        var r = Physique.Tenter(depart.Geste, a, b, Contexte(h, connu), alea);
        if (r.Effet != Effet.Rien)
        {
            var effet = r.Effet == depart.Effet ? Recits.Recit.Seul("idee.marche") : new Recit("idee.autreEffet", ("h", h), ("effet", Physique.NomEffet(r.Effet)));
            Constater(h, depart.Geste, a, b, r, OrigineSavoir.Reflexion, tous, recit: new Recit("idee.essaie", ("question", question), ("h", h), ("effet", effet)));
            return true;
        }
        // Rien : il en conclut que cela ne se peut pas (à tort, peut-être : un seul essai ne prouve rien).
        var id = Noter(_maintenant, TypeEvenement.Pensee, h, null, new Recit("idee.rien", ("question", question), ("h", h)));
        Jardin.Confier(h, TypeIntime.Question, 0, new Recit("idee.impossible", ("idee", idee)), 20 + 20 * p.Ouverture, id, _maintenant, cle);
        return true;
    }
}
