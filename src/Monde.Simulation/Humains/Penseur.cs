using Monde.Simulation.Recits;

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
    public static Recit? Penser(Humain h, Motif motif, Humain? autre, long maintenant)
    {
        if (!h.EstVivant || h.EstBebe(maintenant)) return null;
        var vers = Direction(h);
        object a = autre is null ? Recit.Seul("p.lautre") : autre;
        var variante = (int)((h.Id + maintenant / 600) % 2);
        Recit Choix(string cle1, string cle2, params (string, object?)[] args) => P(variante == 0 ? cle1 : cle2, args);
        static Recit P(string cle, params (string, object?)[] args) => args.Length == 0 ? Recit.Seul(cle) : new(cle, args);

        return h.Activite switch
        {
            Activite.Boire => Choix("p.boire.1", "p.boire.2"),
            Activite.ChercherEau => motif switch
            {
                Motif.Vu => P("p.eau.vue", ("vers", vers)),
                Motif.Souvenir => P("p.eau.souvenir", ("vers", vers)),
                _ => Choix("p.eau.cherche.1", "p.eau.cherche.2"),
            },
            Activite.Cueillir => Choix("p.cueillir.1", "p.cueillir.2"),
            Activite.Chasser => Choix("p.chasser.1", "p.chasser.2"),
            Activite.ChercherNourriture => motif switch
            {
                Motif.Vu => P("p.manger.vu", ("vers", vers)),
                Motif.Souvenir => P("p.manger.souvenir", ("vers", vers)),
                _ => P("p.manger.cherche"),
            },
            Activite.Dormir => h.Fatigue > 80 ? P("p.epuise", ("h", h)) : Choix("p.dormir.1", "p.dormir.2"),
            Activite.SAbriter => Choix("p.sabriter.1", "p.sabriter.2"),
            Activite.ChercherAbri => motif == Motif.Souvenir ? P("p.abri.souvenir", ("vers", vers)) : P("p.abri.cherche", ("vers", vers)),
            Activite.RejoindrePartenaire => motif == Motif.Desir
                ? P("p.desir", ("a", a))
                : Choix("p.partenaire.1", "p.partenaire.2", ("a", a)),
            Activite.SAccoupler => P("p.accoupler", ("a", a)),
            Activite.VeillerEnfant => P("p.veillerEnfant", ("a", a)),
            Activite.SuivreParent => P("p.suivreParent", ("a", a)),
            Activite.Explorer => Choix("p.explorer.1", "p.explorer.2", ("vers", vers)),
            Activite.EviterRival => Choix("p.rival.1", "p.rival.2", ("a", a), ("vers", vers)),
            Activite.RejoindreProche => Choix("p.proche.1", "p.proche.2", ("a", a)),
            Activite.Repos when h.Chagrin > 30 && autre is not null && !autre.EstVivant => Choix("p.deuil.1", "p.deuil.2", ("a", a)),
            Activite.Migrer => motif == Motif.Guide
                ? Choix("p.migrer.guide.1", "p.migrer.guide.2", ("vers", vers))
                : Choix("p.migrer.suit.1", "p.migrer.suit.2", ("vers", vers)),
            Activite.Fuir => motif switch
            {
                Motif.Feu => Choix("p.fuir.feu.1", "p.fuir.feu.2", ("vers", vers)),
                Motif.Eau => Choix("p.fuir.eau.1", "p.fuir.eau.2", ("vers", vers)),
                Motif.Volcan => Choix("p.fuir.volcan.1", "p.fuir.volcan.2", ("vers", vers)),
                Motif.Tempete => variante == 0 ? P("p.fuir.tempete.1", ("vers", vers)) : P("p.fuir.tempete.2"),
                _ => P("p.fuir", ("vers", vers)),
            },
            Activite.Raid when h.CibleX < 0 => Choix("p.raid.guet.1", "p.raid.guet.2"),
            Activite.Raid => Choix("p.raid.1", "p.raid.2", ("vers", vers)),
            Activite.SuivreRavisseur => Choix("p.ravisseur.1", "p.ravisseur.2"),
            Activite.PartirEnExil => variante == 0 ? P("p.exil.1", ("h", h), ("vers", vers)) : P("p.exil.2"),
            Activite.RentrerDuRaid => variante == 0 ? P("p.rentrer.1", ("vers", vers)) : P("p.rentrer.2"),
            Activite.Experimenter => Choix("p.experimenter.1", "p.experimenter.2"),
            Activite.Enseigner => Choix("p.enseigner.1", "p.enseigner.2", ("a", a)),
            Activite.Fabriquer => Choix("p.fabriquer.1", "p.fabriquer.2"),
            Activite.Veiller => Choix("p.veiller.1", "p.veiller.2"),
            Activite.ChercherLeFeu => Choix("p.chercherFeu.1", "p.chercherFeu.2"),
            Activite.Nager => Choix("p.nager.1", "p.nager.2"),
            Activite.Soigner => Choix("p.soigner.1", "p.soigner.2"),
            Activite.Repos when h.Reserves < 10 && h.Faim > 50 => Choix("p.affame.1", "p.affame.2"),
            Activite.Repos when h.EstDeprime => Choix("p.deprime.1", "p.deprime.2"),
            Activite.Repos when h.Humeur > 75 => Choix("p.bien.1", "p.bien.2"),
            Activite.Repos => Choix("p.repos.1", "p.repos.2"),
            _ => null,
        };
    }

    /// <summary>Clés des directions : [nord ou sud][est ou ouest] (0 : ni l'un ni l'autre).</summary>
    private static readonly string[,] Vers =
    {
        { "p.vers.ici", "p.vers.est", "p.vers.ouest" },
        { "p.vers.nord", "p.vers.nord-est", "p.vers.nord-ouest" },
        { "p.vers.sud", "p.vers.sud-est", "p.vers.sud-ouest" },
    };

    /// <summary>« vers le nord-est », d'après la destination ; « ici » si elle est atteinte, rien s'il n'y en a pas.</summary>
    internal static object Direction(Humain h)
    {
        if (h.CibleX < 0) return "";
        int dx = h.CibleX - h.X, dy = h.CibleY - h.Y;
        if (dx == 0 && dy == 0) return Recit.Seul("p.vers.ici");
        var ns = Math.Abs(dy) * 2 >= Math.Abs(dx) ? (dy < 0 ? 1 : 2) : 0;
        var eo = Math.Abs(dx) * 2 >= Math.Abs(dy) ? (dx < 0 ? 2 : 1) : 0;
        return Recit.Seul(Vers[ns, eo]);
    }
}
