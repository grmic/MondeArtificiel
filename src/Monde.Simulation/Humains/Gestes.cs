using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

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

    public static Recit Nom(GesteEntite g) => Recit.Seul($"gesteEntite.{g}");

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

    public static Recit NomManiere(ManiereGeste m) => Recit.Seul($"maniere.{m}");

    public static Recit NomForme(FormeMort f) => Recit.Seul($"formeMort.{f}");

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
    public Recit AgirSur(Humain h, GesteEntite geste, ManiereGeste maniere, int param, double force, double jours, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        if (!h.EstVivant) return new Recit("ent.r.mort", ("h", h));
        Recit? texte;
        long annonce;
        switch (geste)
        {
            case GesteEntite.FrapperDeMort:
            {
                var forme = (FormeMort)Math.Clamp(param, 0, 2);
                // La foudre se voit toujours ; une mort dans le sommeil, jamais.
                maniere = forme switch { FormeMort.Foudre => ManiereGeste.Foudre, FormeMort.Sommeil => ManiereGeste.Cachee, _ => maniere };
                annonce = NoterGeste(maintenant, h, new Recit("ges.mort", ("h", h), ("f", Gestes.NomForme(forme)), ("s", Signe(maniere, forme))));
                var (cause, mort) = forme switch
                {
                    FormeMort.Foudre => (CauseDeMort.Foudre, new Recit("ges.mort.foudre", ("h", h), ("age", AgeEnMots(h, maintenant)))),
                    FormeMort.MalSoudain => (CauseDeMort.MortSubite, new Recit("ges.mort.soudain", ("h", h), ("age", AgeEnMots(h, maintenant)))),
                    _ => (CauseDeMort.Sommeil, new Recit("ges.mort.sommeil", ("h", h), ("age", AgeEnMots(h, maintenant)))),
                };
                // Les témoins et les proches sont cherchés avant la mort (après, il n'est plus là où il était).
                var vecu = Temoins(h, maniere, tous, maintenant);
                Tuer(h, maintenant, cause, mort, annonce);
                FaireSavoir(geste, maniere, h, h.CommunauteId, vecu, false, 1, annonce, tous, maintenant);
                return new Recit("ges.r.mort", ("h", h), ("f", Gestes.NomForme(forme)));
            }
            case GesteEntite.RendreMalade:
            {
                annonce = NoterGeste(maintenant, h, new Recit("ges.malade", ("h", h), ("s", Signe(maniere))));
                texte = Contaminer(h, param, annonce, maintenant);
                if (texte is null) return new Recit("ges.r.dejaMalade", ("h", h));
                FaireSavoir(geste, maniere, h, h.CommunauteId, Temoins(h, maniere, tous, maintenant), false, 0.6, annonce, tous, maintenant);
                return texte;
            }
            case GesteEntite.Guerir:
            {
                if (!Souffrant(h, maintenant)) return new Recit("ges.r.sain", ("h", h));
                annonce = NoterGeste(maintenant, h, new Recit("ges.guerit", ("h", h), ("s", Signe(maniere))));
                GuerirDeTout(h, maintenant);
                NoterEntite(maintenant, TypeEvenement.Maladie, h, null, new Recit("ges.gueri", ("h", h)), annonce);
                FaireSavoir(geste, maniere, h, h.CommunauteId, Temoins(h, maniere, tous, maintenant), true, 0.7, annonce, tous, maintenant);
                return new Recit("ges.r.gueri", ("h", h));
            }
            case GesteEntite.RendreFecond:
            {
                var (fmin, fmax) = Gestes.Force(geste);
                var (jmin, jmax) = Gestes.Jours(geste);
                force = Math.Clamp(double.IsFinite(force) ? force : fmin, fmin, fmax);
                jours = Math.Clamp(double.IsFinite(jours) ? jours : jmin, jmin, jmax);
                annonce = NoterGeste(maintenant, h, new Recit("ges.fecond", ("h", h), ("f", new Decimale(force)), ("d", Duree(jours)), ("s", Signe(maniere))));
                h.FeconditeEntite = force;
                h.FeconditeJusqua = maintenant + (long)(jours * Calendrier.SecondesParJour);
                FaireSavoir(geste, maniere, h, h.CommunauteId, Temoins(h, maniere, tous, maintenant), true, 0.4, annonce, tous, maintenant);
                return h.EstFecond(maintenant) ? new Recit("ges.r.fecond", ("h", h), ("d", Duree(jours))) : new Recit("ges.r.pasFecond", ("h", h));
            }
            default:
                return Recit.Seul("ges.r.surCommunaute");
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
    private Recit? Contaminer(Humain h, int code, long cause, long maintenant)
    {
        if (code is >= 1 and < Gestes.Contagieuse && Enum.IsDefined((Maladie)code))
        {
            var fiche = Maladies.Fiche((Maladie)code);
            if (h.EstMalade(maintenant) && h.Maladie == fiche.Maladie) return null;
            h.Maladie = fiche.Maladie;
            h.MaladeJusqua = maintenant + Maladies.Duree(fiche, h, alea);
            var malade = new Recit("ges.tombeMalade", ("h", h), ("m", Recit.Seul($"maladie.{fiche.Maladie}")));
            NoterEntite(maintenant, TypeEvenement.Maladie, h, null, malade, cause);
            return malade;
        }
        if (Catastrophes is null) return null;
        var pathogene = code >= Gestes.Contagieuse ? code - Gestes.Contagieuse : 0;
        if (Catastrophes.Infecter(h, pathogene, maintenant, cause) == 0) return null;
        return new Recit("ges.r.attrape", ("h", h), ("m", Catastrophes.Pathogenes[h.InfectionId - 1].Dit));
    }

    // ───────────── Sur une communauté ─────────────

    /// <summary>
    /// L'Entité agit sur la communauté <paramref name="c"/>. <paramref name="param"/> : maladie (épidémie) ou type de
    /// catastrophe (calamité) ; <paramref name="force"/> : part des membres frappés, part perdue, facteur de fécondité,
    /// bénédiction (0 à 100) ou intensité de la calamité ; <paramref name="jours"/> : durée. Renvoie, en mots, ce qui s'est passé.
    /// </summary>
    public Recit AgirSur(Communaute c, GesteEntite geste, ManiereGeste maniere, int param, double force, double jours, List<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _tous = tous;
        var membres = tous.Vivants().Where(h => h.EstVivant && h.CommunauteId == c.Id).OrderBy(h => h.Id).ToList();
        if (membres.Count == 0 || !c.Active) return new Recit("ges.r.vide", ("c", c));
        var figure = membres.Find(h => h.Id == c.FigureId) ?? membres[0];
        var (fmin, fmax) = Gestes.Force(geste);
        var (jmin, jmax) = Gestes.Jours(geste);
        if (geste != GesteEntite.Calamite) force = Math.Clamp(double.IsFinite(force) ? force : fmin, fmin, fmax);
        jours = Math.Clamp(double.IsFinite(jours) ? jours : jmin, jmin, jmax);
        var fin = maintenant + (long)(jours * Calendrier.SecondesParJour);
        long annonce;
        Recit resultat;
        switch (geste)
        {
            case GesteEntite.Epidemie:
            {
                annonce = NoterGeste(maintenant, figure, new Recit("ges.epidemie", ("c", c), ("s", Signe(maniere))));
                // La part voulue des membres, tirée au hasard ; une maladie nouvelle naît chez le premier, et passe aux autres.
                var nombre = Math.Max(1, (int)Math.Round(force * membres.Count));
                var frappes = membres.OrderBy(_ => alea.Reel()).Take(nombre).ToList();
                var code = param;
                var malades = 0;
                foreach (var h in frappes)
                {
                    if (Contaminer(h, code, annonce, maintenant) is not null) malades++;
                    if (code == 0 && h.InfectionId != 0) code = Gestes.Contagieuse + h.InfectionId;
                }
                resultat = new Recit("ges.r.epidemie", ("n", malades), ("sur", membres.Count));
                break;
            }
            case GesteEntite.Calamite:
            {
                if (Catastrophes is null || !Enum.IsDefined((TypeCatastrophe)param)) return Recit.Seul("ges.r.calamiteInconnue");
                annonce = NoterGeste(maintenant, figure, new Recit("ges.calamite", ("c", c), ("k", Recit.Seul($"catastropheNom.{(TypeCatastrophe)param}")), ("s", Signe(maniere))));
                var id = Catastrophes.Declencher((TypeCatastrophe)param, c.CentreX, c.CentreY, force, tous, maintenant, Origine.Entite);
                if (id == 0) return Recit.Seul("ges.r.calamiteImpossible");
                resultat = new Recit("ges.r.calamite", ("k", Recit.Seul($"catastropheNom.{(TypeCatastrophe)param}")));
                break;
            }
            case GesteEntite.Famine:
            {
                annonce = NoterGeste(maintenant, figure,
                    new Recit("ges.famine", ("c", c), ("p", (long)Math.Round(100 * force)), ("d", Duree(jours)), ("s", Signe(maniere))));
                var rayon = c.Rayon + 2;
                Remplacer(new EffetEntite(geste, c.Id, maintenant, fin, force, c.CentreX, c.CentreY, rayon, annonce));
                foreach (var i in Cases(c.CentreX, c.CentreY, rayon))
                {
                    ecosysteme.Plantes[i] = (ushort)Math.Round(ecosysteme.Plantes[i] * (1 - force));
                    ecosysteme.Gibier[i] = (ushort)Math.Round(ecosysteme.Gibier[i] * (1 - force));
                }
                Fleau(maintenant);
                resultat = Recit.Seul("ges.r.famine");
                break;
            }
            case GesteEntite.SortirDeFamine:
            {
                annonce = NoterGeste(maintenant, figure, new Recit("ges.finFamine", ("c", c), ("s", Signe(maniere))));
                Cultes!.Effets.RemoveAll(e => e.Geste == GesteEntite.Famine && e.CommunauteId == c.Id);
                Fleau(maintenant);
                var saison = Ecosysteme.FacteurSaison(maintenant);
                foreach (var i in Cases(c.CentreX, c.CentreY, c.Rayon + 2))
                {
                    ecosysteme.Plantes[i] = (ushort)Math.Clamp(Math.Max(ecosysteme.Plantes[i], ecosysteme.NiveauPlantes(i, saison)), 0, Ecosysteme.Plafond);
                    ecosysteme.Gibier[i] = (ushort)Math.Clamp(Math.Max(ecosysteme.Gibier[i], ecosysteme.NiveauGibier(i)), 0, Ecosysteme.Plafond);
                }
                foreach (var h in membres)
                {
                    h.Faim = Math.Min(h.Faim, 10);
                    h.Reserves = Math.Min(100, h.Reserves + 30);
                }
                c.Disette = false;
                resultat = Recit.Seul("ges.r.finFamine");
                break;
            }
            case GesteEntite.ArreterEpidemie:
            {
                var malades = membres.Where(h => h.InfectionId != 0 || h.EstMalade(maintenant)).ToList();
                if (malades.Count == 0) return new Recit("ges.r.personneMalade", ("c", c));
                annonce = NoterGeste(maintenant, figure, new Recit("ges.arrete", ("c", c), ("s", Signe(maniere))));
                foreach (var h in malades) GuerirDeTout(h, maintenant);
                NoterEntite(maintenant, TypeEvenement.Maladie, figure, null, new Recit("ges.arrete.gueris", ("c", c), ("n", malades.Count)), annonce);
                resultat = new Recit("ges.r.gueris", ("n", malades.Count));
                break;
            }
            case GesteEntite.Fecondite:
                annonce = NoterGeste(maintenant, figure,
                    new Recit("ges.fecondite", ("c", c), ("f", new Decimale(force)), ("d", Duree(jours)), ("s", Signe(maniere))));
                Remplacer(new EffetEntite(geste, c.Id, maintenant, fin, force, c.CentreX, c.CentreY, c.Rayon, annonce));
                resultat = new Recit("ges.r.fecondite", ("d", Duree(jours)));
                break;
            case GesteEntite.Benir:
                annonce = NoterGeste(maintenant, figure, new Recit("ges.benit", ("c", c), ("f", (long)Math.Round(force)), ("d", Duree(jours)), ("s", Signe(maniere))));
                Remplacer(new EffetEntite(geste, c.Id, maintenant, fin, force / 100, c.CentreX, c.CentreY, c.Rayon, annonce));
                resultat = new Recit("ges.r.benie", ("d", Duree(jours)));
                break;
            default:
                return Recit.Seul("ges.r.surHumain");
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
    private long NoterGeste(long maintenant, Humain h, Recit texte) =>
        NoterEntite(maintenant, TypeEvenement.Geste, h, null, texte, 0, significatif: true);

    private static Recit Signe(ManiereGeste m, FormeMort? forme = null) =>
        m == ManiereGeste.Cachee ? Recit.Seul("ges.signe.cache") : forme == FormeMort.Foudre ? Recit.Seul("ges.signe.aucun") : new Recit("ges.signe", ("m", Gestes.NomManiere(m)));

    private static Recit Duree(double jours) => jours switch
    {
        < 135 => Recit.Seul("ges.duree.saison"),
        < 300 => new Recit("ges.duree.saisons", ("n", (long)Math.Round(jours / Calendrier.JoursParSaison))),
        < 540 => Recit.Seul("ges.duree.an"),
        _ => new Recit("ges.duree.ans", ("n", Math.Round(jours / Calendrier.JoursParAn, 1))),
    };

}
