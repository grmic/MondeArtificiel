using Monde.Simulation.Chimie;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce qu'on fait de la chaux (mondes de la génération 2, depuis la 0.32.6). Le calcaire ou les coquillages cuits donnent une
/// pierre blanche qui chauffe quand on la mouille ; éteinte, elle fait une pâte blanche qui durcit à l'air. On en enduit la
/// hutte, on en lie les pierres de sa base, on y trempe les peaux pour en faire tomber les poils, on s'en peint ; et qui la
/// manie vive, sans savoir, se brûle les mains ou les yeux.
/// <para>
/// Repères : sols et murs enduits de chaux au Levant (Natoufien, vers 14 000 ans ; Kingery et al., 1988), murs enduits et peints
/// de Çatal Höyük ; le chaulage des peaux, qui les épile et les prépare, est l'une des plus vieilles étapes du travail du cuir
/// (Covington, 2009) ; la chaux vive est caustique (pH 12,4), elle brûle la peau et surtout les yeux.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Une pâte de chaux éteinte encore molle, qu'on étale.</summary>
    private static bool PateDeChaux(Substance s) => s.Structure == Structure.Pate && s.Part(Phase.Portlandite) >= 0.3 && s.Part(Phase.Eau) >= 0.08;

    /// <summary>De la chaux vive : elle chauffe dans l'eau, et brûle.</summary>
    private static bool ChauxVive(Substance s) => s.ChauffeDansLEau && s.Structure != Structure.Pate;

    /// <summary>De la chaux, vive ou éteinte, dont on fait une eau de chaux.</summary>
    private static bool DeLaChaux(Substance s) => s.Part(Phase.Chaux) + s.Part(Phase.Portlandite) >= 0.2 && s.Structure != Structure.Roche;

    /// <summary>Le rang d'un objet porté qui répond à <paramref name="quoi"/> (−1 : aucun).</summary>
    private int Porte(Humain h, Func<Substance, bool> quoi) =>
        h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { } s && quoi(s));

    /// <summary>Le savoir le plus sûr qui voit une matière changer ainsi sous ce geste.</summary>
    private static Savoir? SavoirDe(Humain h, Geste g, string changement) =>
        h.Savoirs.Where(s => s.Effet == Effet.Transformation && s.Geste == g && s.Confiance >= 0.3 && LireRecette(s.Pieces) is { } r
            && r.Changements.Contains(changement)).MaxBy(s => s.Confiance);

    private const string MarqueChaux = "chaux";

    /// <summary>
    /// Il étale la pâte blanche qu'il porte sur la hutte de la case : elle se raffermit, et ne s'affaissera plus si vite. Il ne
    /// sait pas encore qu'elle va durcir : il le verra en revenant (<see cref="VoirLEnduit"/>).
    /// </summary>
    internal bool Enduire(Humain h, int pate)
    {
        if (Installations.HutteEn(h.X, h.Y) is null || pate < 0 || pate >= h.Objets.Count) return false;
        var gain = 0.1 + 0.2 * AdressePour(h, null) * alea.Reel();
        h.Objets.RemoveAt(pate);
        var (x, y, le) = (h.X, h.Y, _maintenant);
        Tour.Differer(() => Installations.Enduire(x, y, le, gain));
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        var savait = SavoirDe(h, Geste.Couvrir, "durcit") is not null;
        if (!savait || JournalQuotidien) Noter(_maintenant, TypeEvenement.Journee, h, null, new Recit("chaux.enduit", ("h", h)));
        return true;
    }

    /// <summary>
    /// De retour à une hutte enduite il y a quelques jours, il voit que la pâte blanche a pris : dure, et la pluie glisse dessus.
    /// Il le retient s'il le remarque ; qui le sait enduit les huttes à son tour.
    /// </summary>
    internal void VoirLEnduit(Humain h, IReadOnlyList<Humain> tous)
    {
        if (Installations.HutteEn(h.X, h.Y) is not { EnduiteLe: > 0 } hutte || h.EstEnfant(_maintenant)) return;
        var jours = (_maintenant - hutte.EnduiteLe) / (double)Calendrier.SecondesParJour;
        if (jours < 4 || jours > 120 || SavoirDe(h, Geste.Couvrir, "durcit") is not null) return;
        var heure = 0.05 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h));
        var pas = MondeSimule.SecondesParPas / (double)Calendrier.SecondesParHeure;
        if (alea.Reel() >= 1 - Math.Pow(1 - heure, pas)) return;
        var pieces = Recette(Geste.Couvrir, Matiere.Substance, Matiere.Aucune, 0, ["durcit", "eau"], "patechaux");
        var ev = Retenir(h, Geste.Couvrir, Matiere.Substance, Matiere.Aucune, Effet.Transformation, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
            pieces: pieces, recit: new Recit("chaux.enduit.vu", ("h", h)));
        Montrer(h, Geste.Couvrir, Matiere.Substance, Matiere.Aucune, Effet.Transformation, 0, Symptome.Aucun, tous, ev, pieces);
    }

    /// <summary>
    /// Tremper une peau dans l'eau où l'on a mêlé de la chaux : en quelques jours les poils se détachent, la peau se travaille
    /// mieux et fait un meilleur vêtement. Vrai s'il l'a fait.
    /// </summary>
    internal bool Epiler(Humain h, OrigineSavoir origine, IReadOnlyList<Humain> tous)
    {
        var peau = h.Objets.FindIndex(o => o.Matiere == Matiere.Peau && o.Pieces != MarqueChaux);
        var chaux = Porte(h, DeLaChaux);
        if (peau < 0 || chaux < 0 || !EauDouceAPortee(h.X, h.Y)) return false;
        var vive = ChauxVive(SubstanceDe(h, h.Objets[chaux])!);
        var p = h.Objets[peau];
        h.Objets[peau] = p with { Qualite = Math.Min(1, p.Qualite * (1.15 + 0.2 * alea.Reel())), Pieces = MarqueChaux };
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        var pieces = Recette(Geste.Laver, Matiere.Peau, Matiere.Substance, 0, ["poils"], MarqueChaux);
        var savait = SavoirDe(h, Geste.Laver, "poils") is not null;
        var recit = new Recit("chaux.peau", ("h", h));
        if (Remarquer(h, origine, Geste.Laver, Matiere.Peau, Matiere.Substance, Effet.Transformation, pieces, h.Froid > 15 || h.Sait(Effet.Couvert)))
        {
            var ev = Retenir(h, Geste.Laver, Matiere.Peau, Matiere.Substance, Effet.Transformation, 0, Symptome.Aucun, origine, 0, tous, pieces: pieces, recit: recit);
            if (origine != OrigineSavoir.Imitation) Montrer(h, Geste.Laver, Matiere.Peau, Matiere.Substance, Effet.Transformation, 0, Symptome.Aucun, tous, ev, pieces);
        }
        else if (JournalQuotidien && savait) Noter(_maintenant, TypeEvenement.Journee, h, null, recit);
        // La chaux vive dans l'eau chauffe et ronge : qui y plonge les mains sans savoir se brûle.
        if (vive) BrulerALaChaux(h, SavoirDe(h, Geste.Melanger, "chauffe") is not null ? 0.05 : 0.2);
        return true;
    }

    /// <summary>
    /// La chaux vive mouillée chauffe et ronge : une brûlure des mains, parfois des yeux (rarement mortelle). Qui sait qu'elle chauffe
    /// s'en garde mieux. <paramref name="risque"/> : chance de se brûler.
    /// </summary>
    private void BrulerALaChaux(Humain h, double risque)
    {
        if (alea.Reel() >= risque * (1.3 - h.Personnalite.Prudence)) return;
        var yeux = alea.Reel() < 0.2;
        h.Sante -= (yeux ? 6 : 2) + 8 * alea.Reel();
        h.Humeur = Math.Max(0, h.Humeur - 5);
        var id = Noter(_maintenant, TypeEvenement.Maladie, h, null, new Recit(yeux ? "chaux.brule.yeux" : "chaux.brule.mains", ("h", h)));
        if (h.Sante <= 0) Mourir(h, _maintenant, CauseDeMort.Brulure, id);
    }

    /// <summary>
    /// Au calme, qui sait que la pâte blanche durcit sur une hutte entretient la sienne : il l'enduit s'il a de la pâte, en gâche
    /// s'il a de la chaux vive et de l'eau, ou cuit du calcaire s'il sait qu'il en sort la pierre blanche. Qui sait épiler les
    /// peaux y trempe celle qu'il porte. Vrai s'il a fait quelque chose.
    /// </summary>
    internal bool Chauler(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Objets.Exists(o => o.Matiere == Matiere.Peau && o.Pieces != MarqueChaux) && SavoirDe(h, Geste.Laver, "poils") is not null
            && Epiler(h, OrigineSavoir.Essai, tous)) return true;
        if (SavoirDe(h, Geste.Couvrir, "durcit") is null || Installations.HutteEn(h.X, h.Y) is not { } hutte) return false;
        if (hutte.EnduiteLe > 0 && _maintenant - hutte.EnduiteLe < Calendrier.SecondesParAn && hutte.Solidite > 0.6) return false;
        if (Porte(h, PateDeChaux) is >= 0 and var pate) return Enduire(h, pate);
        if (Porte(h, ChauxVive) is >= 0 and var vive && EauDouceAPortee(h.X, h.Y))
            return Pratiquer(h, Geste.Melanger, Matiere.Substance, Matiere.Eau, tous, 0, vive);
        var feu = Installations.FoyerEn(h.X, h.Y) is not null || Installations.BraiseEn(h.X, h.Y) is not null;
        if (feu && h.Objets.Count < ObjetsMax)
        {
            var cuire = h.Savoirs.Where(s => s.Effet == Effet.Transformation && s.Geste == Geste.Cuire && s.A is Matiere.Calcaire or Matiere.Coquillage
                && s.Confiance >= 0.3 && LireRecette(s.Pieces) is { } r && (r.Changements.Contains("effrite") || r.Changements.Contains("chauffe"))
                && APortee(h, s.A)).MaxBy(s => s.Confiance);
            if (cuire is not null && LireRecette(cuire.Pieces) is { } rc) return Pratiquer(h, Geste.Cuire, cuire.A, Matiere.Feu, tous, rc.Mode);
        }
        return false;
    }

    /// <summary>
    /// Bâtir une hutte quand on porte de la pâte blanche et qu'on sait qu'elle durcit : on en lie les pierres de la base et on en
    /// enduit les parois. Elle tient bien mieux. Renvoie le gain de solidité (0 : rien de tel).
    /// </summary>
    private double Lier(Humain h)
    {
        if (!AvecLaMatiere || SavoirDe(h, Geste.Couvrir, "durcit") is null || Porte(h, PateDeChaux) is not (>= 0 and var pate)) return 0;
        if (!APortee(h, Matiere.Galet) && !APortee(h, Matiere.Calcaire)) return 0;
        h.Objets.RemoveAt(pate);
        if (JournalQuotidien) Noter(_maintenant, TypeEvenement.Journee, h, null, new Recit("chaux.mortier", ("h", h)));
        return 0.15 + 0.15 * AdressePour(h, null) * alea.Reel();
    }

    /// <summary>
    /// La couleur que le peintre a sur lui, en plus de l'ocre : le blanc de la chaux, le vert de la pierre verte écrasée
    /// (pigments de malachite du Néolithique). Teinte.Aucune : rien.
    /// </summary>
    private Teinte Couleur(Humain h)
    {
        if (!AvecLaMatiere) return Teinte.Aucune;
        if (Porte(h, s => s.Part(Phase.Malachite) >= 0.3) >= 0 && (APortee(h, Matiere.Galet) || APortee(h, Matiere.Silex))) return Teinte.Vert;
        if (Porte(h, s => s.Part(Phase.Portlandite) + s.Part(Phase.Chaux) >= 0.3 && s.Structure != Structure.Roche) >= 0) return Teinte.Blanc;
        return Teinte.Aucune;
    }
}
