using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>Ce que l'Entité peut faire à un humain (les quatre premiers) ou à une communauté (les autres).</summary>
public enum GesteEntite : byte
{
    FrapperDeMort = 0,
    RendreMalade = 1,
    Guerir = 2,
    RendreFecond = 3,
    Epidemie = 10,
    Calamite = 11,
    Famine = 12,
    SortirDeFamine = 13,
    ArreterEpidemie = 14,
    Fecondite = 15,
    Benir = 16,
}

/// <summary>Comment le geste se montre : caché (il passe pour naturel), ou accompagné d'un signe.</summary>
public enum ManiereGeste : byte
{
    Cachee = 0,
    /// <summary>La foudre, ou un éclair dans un ciel clair : on le voit de loin.</summary>
    Foudre = 1,
    /// <summary>Une voix venue de nulle part, entendue de ceux qui sont là.</summary>
    Voix = 2,
    /// <summary>Une forme lumineuse, vue de ceux qui sont là.</summary>
    Apparition = 3,
    /// <summary>Un rêve partagé par ceux qui sont touchés (et, pour un humain, par ses proches).</summary>
    Reve = 4,
}

/// <summary>Forme de la mort donnée par l'Entité.</summary>
public enum FormeMort : byte
{
    /// <summary>La foudre : toujours visible.</summary>
    Foudre = 0,
    /// <summary>Un mal soudain : une mort subite, sauf si un signe l'accompagne.</summary>
    MalSoudain = 1,
    /// <summary>Dans son sommeil : toujours prise pour une mort paisible.</summary>
    Sommeil = 2,
}

/// <summary>
/// Geste de l'Entité qui dure sur une communauté : famine (sur les terres autour de (<paramref name="X"/>, <paramref name="Y"/>)
/// dans un rayon de <paramref name="Rayon"/> cases, <paramref name="Force"/> : part perdue), fécondité (facteur) ou bénédiction
/// (part des risques écartés).
/// </summary>
public sealed record EffetEntite(GesteEntite Geste, int CommunauteId, long Debut, long Fin, double Force, int X, int Y, int Rayon, long EvenementId);

/// <summary>
/// Ce qu'une communauté se raconte d'un geste visible de l'Entité. <paramref name="Force"/> (0 à 100) s'efface au fil des
/// générations (voir <see cref="Regards.DemiVieSouvenir"/>) ; <paramref name="Faute"/> : la faute que le groupe s'est trouvée
/// pour un châtiment (voir <see cref="Gestes"/>).
/// </summary>
public sealed record SouvenirEntite(int CommunauteId, string Texte, long Le, bool Bienfait, double Force, long EvenementId, int Faute);

/// <summary>
/// Les gestes de l'Entité : leurs noms, ce qu'ils font de bien ou de mal, et les bornes de leur durée et de leur force.
/// Pour la fécondité, la chance de concevoir par cycle vaut au mieux 20 à 30 % selon l'âge et la santé (Wilcox et al., 1995) :
/// le geste la multiplie au plus par deux. Au-delà de ces bornes, le monde sort de ce que la recherche permet de chiffrer.
/// </summary>
public static class Gestes
{
    /// <summary>Fautes qu'un groupe frappé se trouve.</summary>
    public const int FauteAucune = 0, FauteVol = 1, FauteMeurtre = 2, FauteGuerre = 3;

    public static bool SurHumain(GesteEntite g) => g <= GesteEntite.RendreFecond;

    public static bool EstBienfait(GesteEntite g) => g is GesteEntite.Guerir or GesteEntite.RendreFecond or GesteEntite.SortirDeFamine
        or GesteEntite.ArreterEpidemie or GesteEntite.Fecondite or GesteEntite.Benir;

    public static string Nom(GesteEntite g) => g switch
    {
        GesteEntite.FrapperDeMort => "Frapper de mort",
        GesteEntite.RendreMalade => "Rendre malade",
        GesteEntite.Guerir => "Guérir",
        GesteEntite.RendreFecond => "Rendre fécond(e)",
        GesteEntite.Epidemie => "Épidémie",
        GesteEntite.Calamite => "Calamité",
        GesteEntite.Famine => "Famine",
        GesteEntite.SortirDeFamine => "Sortir de la famine",
        GesteEntite.ArreterEpidemie => "Arrêter une épidémie",
        GesteEntite.Fecondite => "Fécondité",
        GesteEntite.Benir => "Bénir",
        _ => "",
    };

    /// <summary>Bornes de la durée en jours (0 : sans durée).</summary>
    public static (double Min, double Max) Jours(GesteEntite g) => g switch
    {
        GesteEntite.RendreFecond or GesteEntite.Fecondite => (90, 1080),
        GesteEntite.Famine => (90, 720),
        GesteEntite.Benir => (90, 1800),
        _ => (0, 0),
    };

    /// <summary>Bornes de la force : facteur de fécondité, part perdue (famine), part des membres frappés (épidémie), bénédiction (0 à 100).</summary>
    public static (double Min, double Max) Force(GesteEntite g) => g switch
    {
        GesteEntite.RendreFecond or GesteEntite.Fecondite => (1.5, 2),
        GesteEntite.Famine => (0.5, 0.9),
        GesteEntite.Epidemie => (0.1, 0.6),
        GesteEntite.Benir => (0, 100),
        _ => (0, 0),
    };

    public static string NomManiere(ManiereGeste m) => m switch
    {
        ManiereGeste.Foudre => "la foudre",
        ManiereGeste.Voix => "une voix",
        ManiereGeste.Apparition => "une apparition",
        ManiereGeste.Reve => "un rêve",
        _ => "caché",
    };

    public static string NomForme(FormeMort f) => f switch
    {
        FormeMort.Foudre => "la foudre",
        FormeMort.MalSoudain => "un mal soudain",
        _ => "dans son sommeil",
    };

    /// <summary>Code d'une maladie choisie : 1 à 99, une maladie ordinaire ; 100 + n, la maladie contagieuse n ; 0, une maladie nouvelle.</summary>
    public const int Contagieuse = 100;
}

/// <summary>
/// Gestes de l'Entité sur un humain ou une communauté. Chacun va au journal comme un acte de l'Entité ; un geste visible
/// est vu de ceux qui sont là, puis raconté (voir <see cref="Comportement"/>, regard sur l'invisible).
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Facteur appliqué aux risques visés par la clémence : celle du monde, ou la bénédiction de sa communauté.</summary>
    private double AdouciPour(Humain h, long maintenant) =>
        Cultes is { Effets.Count: > 0 } c ? 1 - Math.Max(Clemence, c.Benediction(h.CommunauteId, maintenant)) : Adouci;

    /// <summary>Fécondité accordée par l'Entité au couple (facteur ; 1 : aucune).</summary>
    private double FeconditeDonnee(Humain femme, Humain homme, long maintenant)
    {
        var f = Math.Max(femme.FeconditeJusqua > maintenant ? femme.FeconditeEntite : 1, homme.FeconditeJusqua > maintenant ? homme.FeconditeEntite : 1);
        return Cultes is { Effets.Count: > 0 } c ? Math.Max(f, c.Fecondite(femme.CommunauteId, maintenant)) : f;
    }

    // ───────────── Sur un humain ─────────────

    /// <summary>
    /// L'Entité agit sur <paramref name="h"/>. <paramref name="param"/> : forme de la mort, ou maladie (voir
    /// <see cref="Gestes.Contagieuse"/>) ; <paramref name="force"/> et <paramref name="jours"/> : pour la fécondité. Renvoie, en
    /// mots, ce qui s'est passé.
    /// </summary>
    public string AgirSur(Humain h, GesteEntite geste, ManiereGeste maniere, int param, double force, double jours, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        if (!h.EstVivant) return $"{h.Nom} est mort{E(h)}.";
        string texte;
        long annonce;
        switch (geste)
        {
            case GesteEntite.FrapperDeMort:
            {
                var forme = (FormeMort)Math.Clamp(param, 0, 2);
                // La foudre se voit toujours ; une mort dans le sommeil, jamais.
                maniere = forme switch { FormeMort.Foudre => ManiereGeste.Foudre, FormeMort.Sommeil => ManiereGeste.Cachee, _ => maniere };
                annonce = NoterGeste(maintenant, h, $"✦ L'Entité frappe {h.Nom} de mort ({Gestes.NomForme(forme)}{Signe(maniere, forme)}).");
                var (cause, mort) = forme switch
                {
                    FormeMort.Foudre => (CauseDeMort.Foudre, $"{h.Nom} est frappé{E(h)} par la foudre tombée d'un ciel sans nuage, à {AgeEnMots(h, maintenant)}."),
                    FormeMort.MalSoudain => (CauseDeMort.MortSubite, $"{h.Nom} s'effondre d'un coup, emporté{E(h)} par un mal soudain, à {AgeEnMots(h, maintenant)}."),
                    _ => (CauseDeMort.Sommeil, $"{h.Nom} ne se réveille pas : {Il(h)} est mort{E(h)} dans son sommeil, à {AgeEnMots(h, maintenant)}."),
                };
                // Les témoins et les proches sont cherchés avant la mort (après, il n'est plus là où il était).
                var vecu = Temoins(h, maniere, tous, maintenant);
                Tuer(h, maintenant, cause, mort, annonce);
                FaireSavoir(geste, maniere, h, h.CommunauteId, vecu, false, 1, annonce, tous, maintenant);
                return $"{h.Nom} est mort{E(h)} ({Gestes.NomForme(forme)}).";
            }
            case GesteEntite.RendreMalade:
            {
                annonce = NoterGeste(maintenant, h, $"✦ L'Entité rend {h.Nom} malade{Signe(maniere)}.");
                texte = Contaminer(h, param, annonce, maintenant);
                if (texte.Length == 0) return $"{h.Nom} est déjà malade de ce mal.";
                FaireSavoir(geste, maniere, h, h.CommunauteId, Temoins(h, maniere, tous, maintenant), false, 0.6, annonce, tous, maintenant);
                return texte;
            }
            case GesteEntite.Guerir:
            {
                if (!Souffrant(h, maintenant)) return $"{h.Nom} n'a ni maladie ni blessure.";
                annonce = NoterGeste(maintenant, h, $"✦ L'Entité guérit {h.Nom}{Signe(maniere)}.");
                GuerirDeTout(h, maintenant);
                NoterEntite(maintenant, TypeEvenement.Maladie, h, null, $"Le mal quitte {h.Nom} d'un coup : {Il(h)} guérit.", annonce);
                FaireSavoir(geste, maniere, h, h.CommunauteId, Temoins(h, maniere, tous, maintenant), true, 0.7, annonce, tous, maintenant);
                return $"{h.Nom} guérit.";
            }
            case GesteEntite.RendreFecond:
            {
                var (fmin, fmax) = Gestes.Force(geste);
                var (jmin, jmax) = Gestes.Jours(geste);
                force = Math.Clamp(double.IsFinite(force) ? force : fmin, fmin, fmax);
                jours = Math.Clamp(double.IsFinite(jours) ? jours : jmin, jmin, jmax);
                annonce = NoterGeste(maintenant, h, $"✦ L'Entité rend {h.Nom} plus fécond{E(h)} (chances de concevoir × {force:0.0}) pour {Duree(jours)}{Signe(maniere)}.");
                h.FeconditeEntite = force;
                h.FeconditeJusqua = maintenant + (long)(jours * Calendrier.SecondesParJour);
                FaireSavoir(geste, maniere, h, h.CommunauteId, Temoins(h, maniere, tous, maintenant), true, 0.4, annonce, tous, maintenant);
                return h.EstFecond(maintenant) ? $"{h.Nom} est plus fécond{E(h)} pour {Duree(jours)}." : $"{h.Nom} n'est pas en âge de concevoir : rien ne changera.";
            }
            default:
                return "Ce geste porte sur une communauté.";
        }
    }

    /// <summary>Malade ou blessé.</summary>
    private static bool Souffrant(Humain h, long maintenant) => h.EstMalade(maintenant) || h.InfectionId != 0 || h.PlaieLe > 0 || h.Sante < 60;

    /// <summary>Le mal le quitte d'un coup (maladie ou blessure) ; d'un mal contagieux, il reste protégé un an.</summary>
    private void GuerirDeTout(Humain m, long maintenant)
    {
        m.MaladeJusqua = Math.Min(m.MaladeJusqua, maintenant);
        if (m.InfectionId != 0)
        {
            if (Catastrophes is not null) Catastrophes.Delivrer(m, maintenant, Calendrier.JoursParAn);
            else
            {
                m.Immunites.Add(new Immunite(m.InfectionId, maintenant + Calendrier.SecondesParAn));
                (m.InfectionId, m.InfectionEvenementId) = (0, 0);
            }
        }
        m.PlaieLe = 0;
        m.Sante = Math.Max(m.Sante, 85);
    }

    /// <summary>
    /// <paramref name="h"/> tombe malade de la maladie choisie (voir <see cref="Gestes.Contagieuse"/>). Renvoie, en mots, ce qui
    /// s'est passé (vide : il en est déjà malade).
    /// </summary>
    private string Contaminer(Humain h, int code, long cause, long maintenant)
    {
        if (code is >= 1 and < Gestes.Contagieuse && Enum.IsDefined((Maladie)code))
        {
            var fiche = Maladies.Fiche((Maladie)code);
            if (h.EstMalade(maintenant) && h.Maladie == fiche.Maladie) return "";
            h.Maladie = fiche.Maladie;
            h.MaladeJusqua = maintenant + Maladies.Duree(fiche, h, alea);
            NoterEntite(maintenant, TypeEvenement.Maladie, h, null, $"{h.Nom} tombe malade : {fiche.Nom}.", cause);
            return $"{h.Nom} tombe malade : {fiche.Nom}.";
        }
        if (Catastrophes is null) return "";
        var pathogene = code >= Gestes.Contagieuse ? code - Gestes.Contagieuse : 0;
        if (Catastrophes.Infecter(h, pathogene, maintenant, cause) == 0) return "";
        return $"{h.Nom} a attrapé {Catastrophes.Pathogenes[h.InfectionId - 1].Nom}.";
    }

    // ───────────── Sur une communauté ─────────────

    /// <summary>
    /// L'Entité agit sur la communauté <paramref name="c"/>. <paramref name="param"/> : maladie (épidémie) ou type de
    /// catastrophe (calamité) ; <paramref name="force"/> : part des membres frappés, part perdue, facteur de fécondité,
    /// bénédiction (0 à 100) ou intensité de la calamité ; <paramref name="jours"/> : durée. Renvoie, en mots, ce qui s'est passé.
    /// </summary>
    public string AgirSur(Communaute c, GesteEntite geste, ManiereGeste maniere, int param, double force, double jours, List<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        var membres = tous.Vivants().Where(h => h.EstVivant && h.CommunauteId == c.Id).OrderBy(h => h.Id).ToList();
        if (membres.Count == 0 || !c.Active) return $"La {Minuscule(c.Nom)} n'a plus de membres.";
        var figure = membres.Find(h => h.Id == c.FigureId) ?? membres[0];
        var (fmin, fmax) = Gestes.Force(geste);
        var (jmin, jmax) = Gestes.Jours(geste);
        if (geste != GesteEntite.Calamite) force = Math.Clamp(double.IsFinite(force) ? force : fmin, fmin, fmax);
        jours = Math.Clamp(double.IsFinite(jours) ? jours : jmin, jmin, jmax);
        var nom = Minuscule(c.Nom);
        var fin = maintenant + (long)(jours * Calendrier.SecondesParJour);
        long annonce;
        string resultat;
        switch (geste)
        {
            case GesteEntite.Epidemie:
            {
                annonce = NoterGeste(maintenant, figure, $"✦ L'Entité envoie une épidémie sur la {nom}{Signe(maniere)}.");
                // La part voulue des membres, tirée au hasard ; une maladie nouvelle naît chez le premier, et passe aux autres.
                var nombre = Math.Max(1, (int)Math.Round(force * membres.Count));
                var frappes = membres.OrderBy(_ => alea.Reel()).Take(nombre).ToList();
                var code = param;
                var malades = 0;
                foreach (var h in frappes)
                {
                    if (Contaminer(h, code, annonce, maintenant).Length > 0) malades++;
                    if (code == 0 && h.InfectionId != 0) code = Gestes.Contagieuse + h.InfectionId;
                }
                resultat = $"{malades} membre{(malades > 1 ? "s" : "")} sur {membres.Count} tombe{(malades > 1 ? "nt" : "")} malade{(malades > 1 ? "s" : "")}.";
                break;
            }
            case GesteEntite.Calamite:
            {
                if (Catastrophes is null || !Enum.IsDefined((TypeCatastrophe)param)) return "Calamité inconnue.";
                annonce = NoterGeste(maintenant, figure, $"✦ L'Entité frappe la {nom} d'une calamité : {TypesCatastrophe.Nom((TypeCatastrophe)param)}{Signe(maniere)}.");
                var id = Catastrophes.Declencher((TypeCatastrophe)param, c.CentreX, c.CentreY, force, tous, maintenant, Origine.Entite);
                if (id == 0) return "Cette calamité ne peut pas avoir lieu ici.";
                resultat = $"{TypesCatastrophe.Nom((TypeCatastrophe)param)} frappe ses terres.";
                break;
            }
            case GesteEntite.Famine:
            {
                annonce = NoterGeste(maintenant, figure,
                    $"✦ L'Entité affame la {nom} : {Math.Round(100 * force)} % du gibier et des plantes de ses terres disparaissent pour {Duree(jours)}{Signe(maniere)}.");
                var rayon = c.Rayon + 2;
                Remplacer(new EffetEntite(geste, c.Id, maintenant, fin, force, c.CentreX, c.CentreY, rayon, annonce));
                foreach (var i in Cases(c.CentreX, c.CentreY, rayon))
                {
                    ecosysteme.Plantes[i] = (ushort)Math.Round(ecosysteme.Plantes[i] * (1 - force));
                    ecosysteme.Gibier[i] = (ushort)Math.Round(ecosysteme.Gibier[i] * (1 - force));
                }
                Fleau(maintenant);
                resultat = "Le gibier et les plantes s'effondrent sur ses terres.";
                break;
            }
            case GesteEntite.SortirDeFamine:
            {
                annonce = NoterGeste(maintenant, figure, $"✦ L'Entité sort la {nom} de la famine{Signe(maniere)}.");
                Cultes!.Effets.RemoveAll(e => e.Geste == GesteEntite.Famine && e.CommunauteId == c.Id);
                Fleau(maintenant);
                var saison = Ecosysteme.FacteurSaison(maintenant);
                foreach (var i in Cases(c.CentreX, c.CentreY, c.Rayon + 2))
                {
                    ecosysteme.Plantes[i] = (ushort)Math.Clamp(Math.Max(ecosysteme.Plantes[i], ecosysteme.NiveauPlantes(i, saison)), 0, 10_000);
                    ecosysteme.Gibier[i] = (ushort)Math.Clamp(Math.Max(ecosysteme.Gibier[i], ecosysteme.NiveauGibier(i)), 0, 10_000);
                }
                foreach (var h in membres)
                {
                    h.Faim = Math.Min(h.Faim, 10);
                    h.Reserves = Math.Min(100, h.Reserves + 30);
                }
                c.Disette = false;
                resultat = "Le gibier et les plantes reviennent ; la faim tombe.";
                break;
            }
            case GesteEntite.ArreterEpidemie:
            {
                var malades = membres.Where(h => h.InfectionId != 0 || h.EstMalade(maintenant)).ToList();
                if (malades.Count == 0) return $"Personne n'est malade dans la {nom}.";
                annonce = NoterGeste(maintenant, figure, $"✦ L'Entité arrête le mal qui frappe la {nom}{Signe(maniere)}.");
                foreach (var h in malades) GuerirDeTout(h, maintenant);
                NoterEntite(maintenant, TypeEvenement.Maladie, figure, null, $"Le mal quitte d'un coup les malades de la {nom} : {malades.Count} guérissent.", annonce);
                resultat = $"{malades.Count} malade{(malades.Count > 1 ? "s" : "")} guéri{(malades.Count > 1 ? "s" : "")}.";
                break;
            }
            case GesteEntite.Fecondite:
                annonce = NoterGeste(maintenant, figure,
                    $"✦ L'Entité rend les femmes de la {nom} plus fécondes (chances de concevoir × {force:0.0}) pour {Duree(jours)}{Signe(maniere)}.");
                Remplacer(new EffetEntite(geste, c.Id, maintenant, fin, force, c.CentreX, c.CentreY, c.Rayon, annonce));
                resultat = $"Fécondité accordée pour {Duree(jours)}.";
                break;
            case GesteEntite.Benir:
                annonce = NoterGeste(maintenant, figure, $"✦ L'Entité bénit la {nom} ({Math.Round(force)} sur 100) pour {Duree(jours)}{Signe(maniere)}.");
                Remplacer(new EffetEntite(geste, c.Id, maintenant, fin, force / 100, c.CentreX, c.CentreY, c.Rayon, annonce));
                resultat = $"Bénie pour {Duree(jours)} : moins de maladies, de morts en couches et de petits morts.";
                break;
            default:
                return "Ce geste porte sur un humain.";
        }
        var vecu = maniere == ManiereGeste.Cachee ? [] : membres.Where(h => h.EstVivant && !h.EstBebe(maintenant)).Select(h => (h, 1.0, true)).ToList();
        FaireSavoir(geste, maniere, null, c.Id, vecu, Gestes.EstBienfait(geste), 1, annonce, tous, maintenant);
        return resultat;
    }

    /// <summary>
    /// Chaque jour : les gestes qui durent et sont finis s'effacent, et la famine envoyée pèse sur les terres (voir
    /// <see cref="Ecosysteme.Fleau"/>).
    /// </summary>
    public void EffetsDuJour(long maintenant)
    {
        if (Cultes is not { } cultes) return;
        var avant = cultes.Effets.Count;
        cultes.Effets.RemoveAll(e => e.Fin <= maintenant);
        if (cultes.Effets.Count != avant || (ecosysteme.Fleau is null) != !cultes.Effets.Exists(e => e.Geste == GesteEntite.Famine)) Fleau(maintenant);
    }

    /// <summary>Un nouveau geste qui dure remplace celui de même sorte déjà en cours sur la communauté.</summary>
    private void Remplacer(EffetEntite e)
    {
        Cultes!.Effets.RemoveAll(x => x.Geste == e.Geste && x.CommunauteId == e.CommunauteId);
        Cultes.Effets.Add(e);
    }

    /// <summary>Recalcule la part des plantes et du gibier perdue sur chaque tuile, d'après les famines en cours.</summary>
    private void Fleau(long maintenant)
    {
        var famines = Cultes?.Effets.Where(e => e.Geste == GesteEntite.Famine && e.Debut <= maintenant && maintenant < e.Fin).ToList() ?? [];
        if (famines.Count == 0)
        {
            ecosysteme.Fleau = null;
            return;
        }
        var fleau = new double[carte.Largeur * carte.Hauteur];
        foreach (var f in famines)
            foreach (var i in Cases(f.X, f.Y, f.Rayon))
                fleau[i] = Math.Max(fleau[i], f.Force);
        ecosysteme.Fleau = fleau;
    }

    /// <summary>Indices des cases à <paramref name="rayon"/> cases au plus de (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    private IEnumerable<int> Cases(int cx, int cy, int rayon)
    {
        for (var y = Math.Max(0, cy - rayon); y <= Math.Min(carte.Hauteur - 1, cy + rayon); y++)
            for (var x = Math.Max(0, cx - rayon); x <= Math.Min(carte.Largeur - 1, cx + rayon); x++)
                yield return carte.Indice(x, y);
    }

    /// <summary>Note le geste lui-même (★) : un acte de l'Entité, que l'observateur seul voit en entier.</summary>
    private long NoterGeste(long maintenant, Humain h, string texte) =>
        NoterEntite(maintenant, TypeEvenement.Geste, h, null, texte, 0, significatif: true);

    private static string Signe(ManiereGeste m, FormeMort? forme = null) =>
        m == ManiereGeste.Cachee ? ", caché" : forme == FormeMort.Foudre ? "" : $", avec {Gestes.NomManiere(m)}";

    private static string Duree(double jours) => jours switch
    {
        < 135 => "une saison",
        < 300 => $"{Math.Round(jours / Calendrier.JoursParSaison)} saisons",
        < 540 => "un an",
        _ => $"{Math.Round(jours / Calendrier.JoursParAn, 1):0.#} ans",
    };

}
