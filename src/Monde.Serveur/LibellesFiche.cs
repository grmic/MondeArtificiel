using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Nature;

namespace Monde.Serveur;

/// <summary>
/// Libellés des fiches dans la langue du navigateur. Chaque fonction suit les seuils de la fonction de la simulation qu'elle
/// remplace (nommée dans son résumé) : en français, les deux écrivent la même chose (vérifié par les tests).
/// </summary>
public static partial class Lib
{
    /// <summary>Niveau d'un trait (0 à 1), comme <see cref="Psyche.Niveau"/>.</summary>
    public static string Niveau(double trait) => L.T(trait switch
    {
        < 0.25 => "niveau.tresBas",
        < 0.4 => "niveau.bas",
        < 0.6 => "niveau.moyen",
        < 0.75 => "niveau.eleve",
        _ => "niveau.tresEleve",
    });

    /// <summary>Le karma en mots, comme <see cref="Regard.Dire"/>.</summary>
    public static string Karma(double karma) => L.T(karma switch
    {
        <= -80 => "karma.lourd",
        <= -20 => "karma.charge",
        <= -5 => "karma.peuCharge",
        < 5 => "karma.neutre",
        < 50 => "karma.bienveillant",
        < 150 => "karma.lumineux",
        _ => "karma.tresLumineux",
    });

    /// <summary>La croyance en l'invisible en mots, comme <see cref="Croyances.Dire"/>.</summary>
    public static string Croyance(double c) => L.T(c switch
    {
        < 5 => "croyance.nonPense",
        < 20 => "croyance.doute",
        < 40 => "croyance.unPeu",
        < 60 => "croyance.croit",
        < 80 => "croyance.fermement",
        _ => "croyance.habite",
    });

    /// <summary>Amour, crainte ou révolte envers l'invisible en mots, comme <see cref="Regards.Dire"/>.</summary>
    public static string Sentiment(SentimentInvisible s, double v, Humain h) => v switch
    {
        < 5 => L.T($"sentiment.{s}.aucun"),
        < 25 => L.T("sentiment.unPeu"),
        < 50 => L.T("sentiment.assez"),
        < 75 => L.T("sentiment.beaucoup"),
        _ => L.T($"sentiment.{s}.extreme", ("g", G(h))),
    };

    /// <summary>Un proche nommé par son lien, comme <see cref="VieSociale.Parente"/> : « sa fille Ana », sinon son nom (la clé suit le genre de <paramref name="h"/>, la forme celui du proche).</summary>
    public static string Parente(Humain h, Humain autre)
    {
        var lien = autre switch
        {
            _ when autre.MereId == h.Id || autre.PereId == h.Id => "enfant",
            _ when h.MereId == autre.Id => "mere",
            _ when h.PereId == autre.Id => "pere",
            _ when h.PartenaireId == autre.Id || autre.PartenaireId == h.Id => "compagnon",
            _ when h.MereId != 0 && h.MereId == autre.MereId => "fratrie",
            _ when h.LienAvec(autre.Id) is { Amis: true } => "ami",
            _ => null,
        };
        return lien is null ? autre.Nom : L.T($"parente.{lien}.{G(h)}", ("nom", autre.Nom), ("g", G(autre)));
    }

    /// <summary>Les raisons de son humeur, de la plus lourde à la plus légère, comme <see cref="VieSociale.Phrases"/>.</summary>
    public static List<string> Raisons(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        var g = G(h);
        string Nom(int id) => id >= 1 && id <= humains.Count ? humains[id - 1].Nom : "?";
        var phrases = new List<string>();
        foreach (var r in h.Raisons)
        {
            string? phrase = r.Type switch
            {
                TypeRaison.Deuil when r.AutreId > 0 => L.T("raison.deuil", ("qui", Parente(h, humains[r.AutreId - 1])), ("g", g)),
                TypeRaison.FaimSoif => L.T("raison.faimSoif", ("g", g)),
                TypeRaison.Froid => L.T("raison.froid", ("g", g)),
                TypeRaison.Maladie => L.T("raison.maladie", ("g", g)),
                TypeRaison.Solitude => L.T("raison.solitude", ("g", g)),
                TypeRaison.Rejet => VieSociale.Hostiles(h, maintenant) switch
                {
                    [var a] => L.T("raison.hostile", ("a", Nom(a)), ("g", g)),
                    [var a, var b] => L.T("raison.hostiles", ("a", Nom(a)), ("b", Nom(b)), ("g", g)),
                    [var a, var b, ..] => L.T("raison.rejete", ("a", Nom(a)), ("b", Nom(b)), ("g", g)),
                    _ => null,
                },
                TypeRaison.Trahison when r.AutreId > 0 => L.T("raison.trahison", ("qui", Parente(h, humains[r.AutreId - 1])), ("g", g)),
                TypeRaison.Colere => L.T("raison.colere", ("g", g)),
                TypeRaison.Temperament => L.T("raison.temperament", ("g", g)),
                TypeRaison.Peur => L.T("raison.peur", ("de", L.T($"catastropheDe.{(TypeCatastrophe)r.AutreId}")), ("g", g)),
                _ => null,
            };
            if (phrase is not null) phrases.Add(phrase);
        }
        return phrases;
    }

    /// <summary>Orientation sexuelle en mots, comme <see cref="Attirance.Orienter"/>.</summary>
    public static string Orientation(Humain h)
    {
        var meme = h.Orientation.Vers(h.Sexe);
        var autre = h.Orientation.Vers(h.Sexe == Sexe.Homme ? Sexe.Femme : Sexe.Homme);
        var cle = (meme, autre) switch
        {
            ( < 0.12, _) => "hetero",
            ( < 0.5, _) when autre >= meme => "surtoutHetero",
            ( >= 0.5, >= 0.5) => "bi",
            ( >= 0.5, _) => "homo",
            _ => "deuxSexes",
        };
        return L.T($"orientation.{cle}", ("g", G(h)));
    }

    /// <summary>Ce qu'il sait de son attirance pour son propre sexe, comme <see cref="Attirance.Conscience"/>.</summary>
    public static string? Conscience(Humain h, double toleranceDuGroupe)
    {
        if (Attirance.PourSonSexe(h) < 0.15) return null;
        var cle = h.Lucidite switch
        {
            < 0.3 => "ignore",
            < 0.6 => "comprend",
            _ when toleranceDuGroupe < 0.4 => "cache",
            _ => "sait",
        };
        return L.T($"conscience.{cle}", ("g", G(h)));
    }

    /// <summary>L'idéal de beauté d'un groupe, comme <see cref="Attirance.DireIdeal"/>.</summary>
    public static string Ideal(IdealBeaute i)
    {
        var taille = i.Taille switch { > 0.5 => "grands", < -0.5 => "petits", _ => "moyenne" };
        var teint = i.Teint switch { > 0.62 => "sombre", < 0.38 => "claire", _ => "moyen" };
        var corps = i.Corpulence switch { > 0.62 => "chair", < 0.38 => "minces", _ => "moyens" };
        return L.T("ideal.phrase", ("taille", L.T($"ideal.taille.{taille}")), ("teint", L.T($"ideal.teint.{teint}")),
            ("corps", L.T($"ideal.corps.{corps}")));
    }

    /// <summary>Ce que les siens disent qu'il fait pour eux, comme <see cref="Comportement.Metier"/>.</summary>
    public static string? Metier(Humain h) =>
        h.Specialite == Ouvrage.Aucun ? null : L.T($"metier.{h.Specialite}", ("g", G(h)));

    /// <summary>Son rôle de soignant, comme <see cref="Simulation.Humains.Medecine.Role"/>.</summary>
    public static string? Soignant(Humain h)
    {
        var roles = new List<string>();
        if (h.GuerisseurDepuis > 0) roles.Add(L.T("soignant.guerisseur", ("g", G(h))));
        if (h.ChamanDepuis > 0) roles.Add(L.T("soignant.chaman", ("g", G(h))));
        if (h.CharlatanDepuis > 0) roles.Add(L.T("soignant.charlatan", ("g", G(h))));
        return roles.Count == 0 ? null : string.Join(L.T("commun.virgule"), roles);
    }

    /// <summary>Le style d'une communauté, comme <see cref="Arts.Style"/>.</summary>
    public static string? Style(IReadOnlyList<Humain> membres, IEnumerable<Oeuvre> oeuvres)
    {
        if (membres.Count == 0) return null;
        var parties = new List<string>();
        var chantent = membres.Count(h => Arts.Connait(h, Effet.Chant));
        if (chantent > 0)
        {
            var e = membres.Average(h => h.Personnalite.Extraversion);
            var n = membres.Average(h => h.Personnalite.Nevrosisme);
            var tempo = e > 0.55 ? "vifs" : e < 0.45 ? "lents" : "poses";
            var ton = n > 0.55 ? "plaintifs" : n < 0.45 ? "joyeux" : "neutre";
            var paroles = membres.Count(h => Arts.Connait(h, Effet.Chant) && h.StadeParole >= 1) * 2 > chantent;
            parties.Add(L.T(paroles ? "style.chantsParoles" : "style.chants", ("tempo", L.T($"style.tempo.{tempo}")), ("ton", L.T($"style.ton.{ton}"))));
        }
        var rythmes = membres.SelectMany(h => h.Savoirs).Where(s => s.Effet == Effet.Rythme && s.Confiance >= 0.3).ToList();
        if (rythmes.Count > 0)
        {
            var bois = rythmes.Count(s => Physique.EstBois(s.A) || Physique.EstBois(s.B) || s.A == Matiere.Tronc || s.B == Matiere.Tronc);
            var calebasse = rythmes.Count(s => s.A == Matiere.Calebasse || s.B == Matiere.Calebasse);
            parties.Add(L.T(calebasse * 2 > rythmes.Count ? "style.rythmesCalebasse" : bois * 2 > rythmes.Count ? "style.rythmesBois" : "style.rythmesPierre"));
        }
        if (membres.Any(h => Arts.Connait(h, Effet.Danse)))
            parties.Add(L.T(membres.Average(h => h.Personnalite.Agreabilite) < 0.45 ? "style.dansesGuerrieres" : "style.dansesCercle"));
        if (membres.Any(h => Arts.Connait(h, Effet.Ocre))) parties.Add(L.T("style.ocre"));
        var motif = oeuvres.GroupBy(o => o.Motif).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (motif is not null) parties.Add(L.T("style.peint", ("motif", L.T($"style.motif.{motif.Key}"))));
        if (parties.Count == 0) return null;
        var texte = string.Join(L.T("commun.virgule"), parties);
        return L.T("style.phrase", ("texte", char.ToUpperInvariant(texte[0]) + texte[1..]));
    }

    /// <summary>Direction d'un point vers un autre (« le nord-est »), comme <see cref="Lieux.Vers"/>. Y croît vers le sud.</summary>
    public static string Vers(int x0, int y0, int x1, int y1)
    {
        var secteur = (int)Math.Round(Math.Atan2(y1 - y0, x1 - x0) / (Math.PI / 4));
        return L.T($"vers.{(secteur + 8) % 8}");
    }

    /// <summary>Nom d'une catastrophe avec son article (« le séisme »), comme <see cref="TypesCatastrophe.Nom"/>.</summary>
    public static string Catastrophe(TypeCatastrophe t) => L.T($"catastropheNom.{t}");

    /// <summary>Nom d'une maladie ordinaire (« une pneumonie »), comme le nom de sa fiche dans <see cref="Maladies"/>.</summary>
    public static string Maladie(Maladie m) => L.T($"maladieOrdinaire.{m}");
}
