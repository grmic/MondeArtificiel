namespace Monde.Simulation.Humains;

/// <summary>
/// Enseigner exprès : au repos, près des siens, celui qui maîtrise un geste le montre lentement à un enfant, un proche ou un
/// curieux, guide sa main et le corrige. L'élève apprend bien plus souvent qu'en regardant par hasard, et part avec une
/// meilleure part de l'habileté de son maître (voir <see cref="Heritage"/>).
/// <para>
/// Ce que dit la recherche : l'enseignement existe chez les chasseurs-cueilleurs, surtout sous des formes discrètes (montrer,
/// corriger, confier une tâche), dès la petite enfance (Kline, 2015 ; Hewlett et Roulette, 2016) ; l'enfant apprend
/// d'abord de ses parents, puis des autres adultes et des enfants plus âgés (Lew-Levy et al., 2017) ; un geste difficile
/// se transmet bien mieux montré, et mieux encore expliqué avec des mots (Morgan et al., 2015, taille de la pierre).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// Un humain au repos montre peut-être un geste à quelqu'un qui est près de lui. Renvoie vrai s'il a enseigné.
    /// L'envie vient de la patience (conscience, agréabilité) et de la capacité à voir ce que l'autre ne comprend pas
    /// (empathie cognitive) ; le psychopathe et le peu compatissant ne montrent guère qu'aux leurs.
    /// </summary>
    internal bool Enseigner(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        return Enseigner(h, tous);
    }

    private bool Enseigner(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Savoirs.Count == 0 || h.AgeEnAnnees(_maintenant) < 9 || h.Humeur < 25 || h.CaptifDe > 0) return false;
        var p = h.Personnalite;
        // Celui que les siens disent spécialiste montre plus volontiers : s'il mourait sans élève, son geste mourrait avec lui.
        var patience = (0.3 + 0.35 * p.Conscience + 0.35 * p.Agreabilite) * (0.5 + p.Moral.EmpathieCognitive) * (h.Specialite != Ouvrage.Aucun ? 1.5 : 1);
        if (alea.Reel() >= 0.004 * patience) return false;

        // L'élève : quelqu'un d'éveillé sur la même case, ses enfants d'abord, puis ceux qu'il aime, puis les curieux.
        var jaloux = Psyche.EstPsychopathe(h, _maintenant) || p.Moral.Compassion < 0.25;
        Humain? eleve = null;
        Savoir? lecon = null;
        var meilleur = 0.0;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.EstBebe(_maintenant) || o.Activite == Activite.Dormir) continue;
            var parent = o.MereId == h.Id || o.PereId == h.Id;
            var affection = h.AffectionPour(o.Id);
            if (affection < 0 || jaloux && !parent && affection < 50) continue;
            var proche = parent ? 3 : 1 + Math.Max(0, affection) / 50;
            // Les enfants viennent d'eux-mêmes regarder et demander ; parmi les adultes, surtout les curieux.
            var demande = o.EstEnfant(_maintenant) ? 1.5 : 0.4 + o.Personnalite.Ouverture;
            if (ChoisirLecon(h, o) is not { } s) continue;
            var poids = proche * demande * (0.5 + alea.Reel());
            if (poids <= meilleur) continue;
            (eleve, lecon, meilleur) = (o, s, poids);
        }
        if (eleve is null || lecon is null) return false;

        h.Activite = Activite.Enseigner;
        h.CibleX = h.CibleY = -1;
        _autre = eleve;
        // Comprendre demande de l'attention et, pour un geste difficile, de l'intelligence ; un mot commun aide beaucoup.
        var difficile = Difficile(lecon.Effet, lecon.Pieces);
        var idee = lecon.Espece > 0 ? Idees.Plante(lecon.Espece) : Idees.Savoir(lecon.Effet);
        var chance = (0.35 + 0.35 * eleve.Personnalite.Ouverture) * (difficile ? 0.5 + 0.5 * Ingeniosite(eleve) : 1)
                     * (Langues.Partagent(h, eleve, idee) ? 1.3 : 0.85);
        if (alea.Reel() >= chance) return true;

        var sait = eleve.Savoirs.Exists(s => s.Meme(lecon.Geste, lecon.A, lecon.B, lecon.Effet, lecon.Espece, lecon.Symptome) && s.Pieces == lecon.Pieces);
        var heritage = Heritage(eleve, lecon, lecon.Effet, lecon.Pieces, true);
        string? recit = null;
        if (!sait)
        {
            var guider = lecon.Geste is Geste.Manger or Geste.Appliquer ? $"{h.Nom} montre à {eleve.Nom} une plante et ce qu'elle fait"
                : $"{h.Nom} guide la main de {eleve.Nom} et lui montre le geste, lentement";
            recit = lecon.MaitreId > 0 && lecon.MaitreId <= tous.Count
                ? $"{guider} ; {Il(h)} l'avait appris de {tous[lecon.MaitreId - 1].Nom}."
                : $"{guider}.";
        }
        Retenir(eleve, lecon.Geste, lecon.A, lecon.B, lecon.Effet, lecon.Espece, lecon.Symptome, OrigineSavoir.Imitation, h.Id, tous,
            pieces: lecon.Pieces, recit: recit, heritage: heritage, karmaMaitre: sait ? 0 : Regard.Enseigner);
        VieSociale.Gratitude(eleve, h, sait ? 0.5 : 1.5);
        return true;
    }

    /// <summary>
    /// Ce que le maître montrerait à cet élève : un savoir sûr qu'il emploie, que l'élève ignore ou fait nettement moins bien,
    /// et dont il a les matières sous la main. Les gestes difficiles et les plantes qui tuent passent avant le reste ;
    /// une main d'enfant n'apprend pas encore à tailler.
    /// </summary>
    private Savoir? ChoisirLecon(Humain h, Humain eleve)
    {
        Savoir? choix = null;
        var meilleur = 0.0;
        var petit = eleve.EstPetitEnfant(_maintenant);
        foreach (var s in h.Savoirs)
        {
            if (s.Confiance < 0.6 || s.Succes < 2 || s.Effet is Effet.Rien or Effet.Tiedeur or Effet.Visions or Effet.Chaleur) continue;
            var difficile = Difficile(s.Effet, s.Pieces);
            if (petit && difficile) continue;
            var sien = eleve.Savoirs.Find(x => x.Meme(s.Geste, s.A, s.B, s.Effet, s.Espece, s.Symptome) && x.Pieces == s.Pieces);
            if (sien is not null && (!difficile || sien.Succes * 2 > s.Succes)) continue;
            if (!Montrable(h, s)) continue;
            var poids = (difficile ? 2 : 1) * (s.Effet == Effet.Poison ? 2 : 1) * (sien is null ? 1 : 0.5) * (Fait(h.Specialite, s) ? 2 : 1) * (0.5 + alea.Reel());
            if (poids <= meilleur) continue;
            (choix, meilleur) = (s, poids);
        }
        return choix;
    }

    /// <summary>On ne montre un geste que si l'on a ses matières sous la main (un objet assemblé : si l'on en porte un).</summary>
    private bool Montrable(Humain h, Savoir s)
    {
        if (s.Geste is Geste.Manger or Geste.Appliquer) return s.Espece > 0 && Flore.Sur(carte.BiomeEn(h.X, h.Y)).Any(e => e.Numero == s.Espece);
        if (s.Pieces.Length > 0) return h.Objets.Exists(o => o.Pieces == s.Pieces);
        return APortee(h, s.A) && APortee(h, s.B);
    }
}
