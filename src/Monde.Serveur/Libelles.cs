using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Serveur;

/// <summary>Libellés du serveur partagés par plusieurs fenêtres, dans la langue du navigateur.</summary>
public static partial class Lib
{
    /// <summary>Genre grammatical d'un humain, pour les textes accordés (« m », « f »).</summary>
    public static string G(Humain h) => h.Sexe == Sexe.Femme ? "f" : "m";

    /// <summary>Objectif « aller jusqu'à » en mots : la date visée, ou l'événement attendu.</summary>
    public static string Objectif(Objectif o) => o.Type switch
    {
        TypeObjectif.Date => L.Date(o.Limite),
        TypeObjectif.Naissance => L.T("objectif.naissance"),
        _ => L.T("objectif.mort"),
    };

    public static string Objectif(ObjectifAtteint a) =>
        a.RienEnCentAns ? L.T("objectif.rienEnCentAns", ("objectif", Objectif(a.Objectif))) : Objectif(a.Objectif);

    /// <summary>Annonce de l'extinction : le dernier humain, l'âge du monde, combien ont vécu.</summary>
    public static string Extinction(MondeSimule monde)
    {
        var (maintenant, humains) = monde.InstantaneHumains();
        if (humains.Count == 0) return L.T("extinction.plusPersonne");
        var dernier = humains.OrderBy(h => h.MortLe).ThenBy(h => h.Id).Last();
        return L.T("extinction.annonce", ("nom", dernier.Nom), ("g", G(dernier)), ("duree", L.Duree(dernier.MortLe ?? maintenant)),
            ("n", humains.Count));
    }

    /// <summary>Cause de la mort : « de faim », « au combat »…</summary>
    public static string Cause(CauseDeMort cause) => cause == CauseDeMort.Aucune ? "" : L.T($"cause.{cause}");

    /// <summary>État d'âme en un ou deux mots, accordé.</summary>
    public static string Humeur(Humain h)
    {
        var cle = h switch
        {
            { EstDeprime: true } => "deprime",
            { Colere: > 60 } => "furieux",
            { Chagrin: > 30 } => "endeuil",
            { Humeur: > 75 } => "joyeux",
            { Humeur: > 60 } => "content",
            { Humeur: > 40 } => "serein",
            { Humeur: > 25 } => "triste",
            _ => "abattu",
        };
        return L.T($"humeur.{cle}", ("g", G(h)));
    }

    /// <summary>Premier défaut des réglages d'un monde, en mots, ou null s'ils sont valides.</summary>
    public static string? Erreur(ConfigurationMonde c) => c.Probleme() switch
    {
        ProblemeConfiguration.Couples => L.T("config.erreur.couples", ("min", ConfigurationMonde.CouplesMin), ("max", ConfigurationMonde.CouplesMax)),
        ProblemeConfiguration.Taille => L.T("config.erreur.taille",
            ("min", L.Distance(Simulation.Nature.Echelle.Kilometres(ConfigurationMonde.TailleMin))),
            ("max", L.Distance(Simulation.Nature.Echelle.Kilometres(ConfigurationMonde.TailleMax)))),
        ProblemeConfiguration.Continents => L.T("config.erreur.continents", ("min", ConfigurationMonde.ContinentsMin), ("max", ConfigurationMonde.ContinentsMax)),
        ProblemeConfiguration.Placement => L.T("config.erreur.placement"),
        ProblemeConfiguration.Generation => L.T("config.erreur.generation"),
        _ => null,
    };

    /// <summary>Où partent les couples, en mots.</summary>
    public static string Placement(ConfigurationMonde c) => c.Placement switch
    {
        ConfigurationMonde.PlacementTous => L.T("config.placement.tous"),
        1 => c.Continents == 1 ? L.T("config.placement.seul") : L.T("config.placement.plusGrand"),
        var n => L.T("config.placement.rang", ("rang", n)),
    };

    /// <summary>Message d'une erreur de la simulation à montrer au navigateur, dans sa langue quand elle est connue.</summary>
    public static string Message(Exception e) => e switch
    {
        ContinentsInsuffisants c => L.T("config.erreur.continentsInsuffisants", ("n", c.Disponibles), ("rang", c.Demande)),
        CarteTropPetitePourEloignes c => L.T("config.erreur.tropPetitePourEloignes", ("km", L.Distance(c.Kilometres))),
        _ => e.Message,
    };

    /// <summary>« 12 kg de viande séchée », « 4 litres d'eau ».</summary>
    public static string Reserve(SorteReserve sorte, double kg) => L.T($"reserve.{sorte}", ("n", (int)Math.Max(1, Math.Round(kg))));

    /// <summary>Rubrique d'une parole de l'Entité dans le menu.</summary>
    public static string RubriqueParole(Parole p) => L.T(p switch
    {
        Parole.NAiePasPeur or Parole.TuNEsPasSeul or Parole.TaPeinePassera => "rubriqueParole.rassurer",
        Parole.UnisToi or Parole.ResteAvec or Parole.Quitte => "rubriqueParole.unir",
        Parole.PrendsSoin or Parole.Partage or Parole.Console => "rubriqueParole.soin",
        Parole.Pardonne or Parole.NeTeVengePas or Parole.NeVaPasALaGuerre => "rubriqueParole.apaiser",
        Parole.PartsVers or Parole.ResteIci or Parole.DefendsLesTiens => "rubriqueParole.agir",
        Parole.CatastropheArrive or Parole.PlanteDangereuse => "rubriqueParole.avertir",
        Parole.TuGuideras or Parole.Ecoute or Parole.PorteMaParole => "rubriqueParole.designer",
        Parole.JeSuisCelleQuiVousAFaits => "rubriqueParole.reveler",
        _ => "rubriqueParole.nuire",
    });

    /// <summary>
    /// Ce que dit l'Entité. <paramref name="extraMin"/> : la même précision, telle qu'on la met au milieu d'une phrase
    /// (« contre la communauté … » en français).
    /// </summary>
    public static string Phrase(Parole p, string? cible, string? extra, string? extraMin, bool feminin = false) => p switch
    {
        Parole.Penchant => "",
        Parole.NeVaPasALaGuerre when extra is not null => L.T("phrase.NeVaPasALaGuerre.contre", ("extra", extra), ("extraMin", extraMin ?? extra)),
        _ => L.T($"phrase.{p}", ("cible", cible), ("extra", extra), ("g", feminin ? "f" : "m")),
    };

    /// <summary>Niveau d'un score d'intelligence (échelle des QI) en mots, comme <see cref="Intelligence.Qualifier"/>.</summary>
    public static string Qi(double score) => L.T(score switch
    {
        < 70 => "qi.tresFaible",
        < 80 => "qi.limite",
        < 90 => "qi.moyenneFaible",
        < 110 => "qi.moyenne",
        < 120 => "qi.moyenneForte",
        < 130 => "qi.superieure",
        < 145 => "qi.tresSuperieure",
        _ => "qi.exceptionnelle",
    });

    /// <summary>Valeur signée avec une décimale : « +2,5 », « −1 ».</summary>
    public static string Signe(double v, int decimales = 1) => (v > 0 ? "+" : "") + L.Nombre(Math.Round(v, decimales), decimales);

    /// <summary>Sens d'un mot inventé par les humains (« eau », un savoir, une plante), comme <see cref="Idees.Libelle"/>.</summary>
    public static string Idee(string idee, Flore? flore = null)
    {
        if (Idees.Libelles.ContainsKey(idee)) return L.T($"idee.{idee}");
        if (idee.StartsWith("savoir:", StringComparison.Ordinal) && int.TryParse(idee[7..], out var e)) return L.T($"effet.{(Effet)e}");
        return Idees.Libelle(idee, flore);
    }

    /// <summary>Le mot tel que l'observateur le lit, comme <see cref="Langues.Dire"/> : « kasha », ou [signe : la main à plat qui ondule…].</summary>
    public static string Mot(string forme, bool signe) => signe ? L.T("langue.signe", ("signe", Signe(forme))) : forme;

    /// <summary>Un signe décrit : forme de la main, mouvement, lieu (6, 8 et 7 possibilités, comme dans Langues).</summary>
    public static string Signe(string forme)
    {
        var p = forme.Split('.');
        return p.Length == 3 && int.TryParse(p[0], out var m) && int.TryParse(p[1], out var v) && int.TryParse(p[2], out var l)
               && m is >= 0 and < 6 && v is >= 0 and < 8 && l is >= 0 and < 7
            ? L.T("langue.signeDecrit", ("main", L.T($"signe.main.{m}")), ("mouvement", L.T($"signe.mouvement.{v}")), ("lieu", L.T($"signe.lieu.{l}")))
            : forme;
    }
}
