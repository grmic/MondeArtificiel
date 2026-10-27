using System.Globalization;
using Monde.Simulation.Humains;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Chimie;

/// <summary>
/// Ce qu'un humain voit d'une matière et de ce qui lui est arrivé : il ne connaît ni la kaolinite ni la chaux, il voit qu'une
/// terre a durci et ne se défait plus dans l'eau, qu'une pierre a blanchi et s'effrite, qu'un bois est devenu noir et léger.
/// C'est de ces changements vus, et non d'un nom donné d'avance, que naît un savoir, et c'est par eux qu'il se dit.
/// </summary>
public static class Perception
{
    /// <summary>
    /// Les changements qu'on voit entre la matière d'avant et celle d'après, dans un ordre fixe (codes courts : « eau » pour
    /// « ne se défait plus dans l'eau », « t4 » pour « devient rouge »…). Vide : rien qu'on remarque.
    /// </summary>
    public static IReadOnlyList<string> Changements(Substance avant, Transformation t)
    {
        var apres = t.Produit;
        var l = new List<string>();
        if (t.Brulee) { l.Add("brule"); return l; }
        if (t.Eclate) l.Add(apres.Structure == Structure.Grains && avant.Structure != Structure.Grains && avant.Fissures < 1 && apres.Fissures < 1 ? "poudre" : "eclate");
        if (t.Fondue) l.Add("fond");
        if (t.Exotherme) l.Add("chauffe");
        if (!t.Eclate || l[0] == "poudre")
        {
            if (avant.Structure == Structure.Pate && apres.Structure == Structure.Crue) l.Add("seche");
            if (!avant.Plastique && apres.Plastique) l.Add("plastique");
            if (avant.SeDelite && !apres.SeDelite && avant.Structure is Structure.Pate or Structure.Crue) l.Add("eau");
            if (!avant.SeDelite && apres.SeDelite && apres.Structure != Structure.Grains) l.Add("effrite");
            if (apres.Durete >= avant.Durete + 0.08 && apres.Structure != Structure.Grains) l.Add("durcit");
            if (apres.Durete <= avant.Durete - 0.05 && avant.Structure == Structure.Metal) l.Add("doux");
            if (!avant.Sonore && apres.Sonore) l.Add("sonne");
            if (apres.Densite <= 0.7 * avant.Densite && apres.Structure != Structure.Grains) l.Add("leger");
            if (!avant.BrulesSansFumee && apres.BrulesSansFumee) l.Add("braise");
            if (!avant.Collante && apres.Collante) l.Add("colle");
            if (!avant.Brillante && apres.Brillante) l.Add("luit");
            if (avant.Malleable && (apres.Forme & Forme.Plat) != 0 && (avant.Forme & Forme.Plat) == 0) l.Add("plat");
            if (avant.Malleable && (apres.Forme & Forme.Pointu) != 0 && (avant.Forme & Forme.Pointu) == 0) l.Add("pointe");
        }
        if (apres.Teinte != avant.Teinte && apres.Teinte != Teinte.Aucune && !t.Brulee) l.Add(string.Create(CultureInfo.InvariantCulture, $"t{(int)apres.Teinte}"));
        return l;
    }

    /// <summary>Les changements vus, en mots (« elle durcit, ne se défait plus dans l'eau et devient rouge »).</summary>
    public static Liste Dire(IEnumerable<string> changements) =>
        new([.. changements.Select(c => c.Length > 1 && c[0] == 't' && char.IsDigit(c[1])
            ? (object)new Recit("chim.chg.teinte", ("t", Recit.Seul($"chim.teinte.{(Teinte)int.Parse(c[1..], CultureInfo.InvariantCulture)}")))
            : Recit.Seul($"chim.chg.{c}"))]);

    /// <summary>
    /// La famille d'une matière telle qu'on la voit : de la terre, de la terre cuite, du métal, du charbon, de la poix, de la
    /// pierre brûlée (chaux), du verre, de la poudre…
    /// </summary>
    public static string Famille(Substance s)
    {
        if (s.Structure == Structure.Metal) return "metal";
        if (s.Structure == Structure.Vitreuse) return "verre";
        if (s.Collante) return "poix";
        if (s.Part(Phase.Carbone) >= 0.5) return "charbon";
        if (s.Part(Phase.Chaux) + s.Part(Phase.Portlandite) >= 0.4) return s.Structure == Structure.Pate ? "patechaux" : "chaux";
        if (s.Structure == Structure.Cuite) return "terreCuite";
        if (s.Structure == Structure.Pate) return "pate";
        if (s.Structure == Structure.Crue) return "terre";
        if (s.Structure == Structure.Grains) return s.Part(Phase.Potasse) >= 0.05 && s.Argile < 0.1 ? "cendre" : "poudre";
        if (s.Part(Phase.Halite) >= 0.5) return "sel";
        if (s.Structure == Structure.Fibreuse)
            return s.Part(Phase.Apatite) + s.Part(Phase.Aragonite) + s.Part(Phase.Calcite) >= 0.4 ? "coquille" : "bois";
        return "pierre";
    }

    /// <summary>Le nom d'une chose, tel qu'on la décrit : sa forme, sa matière vue et sa couleur (« un objet creux de terre cuite, de couleur rouge »).</summary>
    public static Recit Nom(Substance s)
    {
        var forme = (s.Forme & Forme.Creux) != 0 ? "creux" : (s.Forme & Forme.Pointu) != 0 ? "pointe" : (s.Forme & Forme.Plat) != 0 ? "plat" : "morceau";
        if (s.Structure == Structure.Grains) forme = "tas";
        if (s.Fissures >= 1) forme = "debris";
        var teinte = s.Teinte;
        return teinte == Teinte.Aucune
            ? new Recit($"chim.nom.{forme}", ("fam", Recit.Seul($"chim.fam.{Famille(s)}")))
            : new Recit($"chim.nom.{forme}.teinte", ("fam", Recit.Seul($"chim.fam.{Famille(s)}")), ("t", Recit.Seul($"chim.teinte.{teinte}")));
    }
}
