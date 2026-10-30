using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Comment les humains découvrent, sans que rien ne leur soit donné : ils manipulent ce qui les entoure par jeu ou par
/// besoin, constatent ce qui arrive (selon <see cref="Physique"/>), s'en souviennent si cela sert, le refont, le
/// rapprochent de ce qu'ils savent déjà, et l'apprennent aux autres sans le vouloir, en se laissant regarder.
/// <para>
/// Ce que dit la recherche : chez les enfants et les adultes, la plupart des innovations naissent du jeu et de
/// l'exploration, puis se répandent par imitation (Carr, Kendal et Flynn, 2016) ; les savoirs techniques se transmettent
/// surtout en regardant faire, et se perdent quand une population petite ou isolée n'a plus assez de praticiens
/// habiles (Henrich, 2004 : la Tasmanie). Les remèdes naissent d'essais et d'erreurs : un malade qui guérit après avoir
/// mangé une plante croit qu'elle l'a guéri, à tort ou à raison.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>La flore du monde (fournie par le monde).</summary>
    public Flore Flore { get; set; } = new(0);

    /// <summary>Feux, huttes et découvertes du monde (fournis par le monde).</summary>
    public Installations Installations { get; set; } = new();

    /// <summary>Humains morts pendant un remède : ceux qui le leur avaient donné ou conseillé en doutent.</summary>
    private readonly List<(int Espece, Symptome Mal, int SoigneurId)> _remedesEchoues = [];

    private const int ObjetsMax = 4;

    /// <summary>Ingéniosité (0 à 1) : l'intelligence générale, qui permet de rapprocher deux choses.</summary>
    private static double Ingeniosite(Humain h) => Math.Clamp((h.Personnalite.Intelligence.Generale - 55) / 90, 0, 1);

    /// <summary>Adresse pour ce geste : l'aptitude manuelle, plus l'habitude (dix réussites rendent nettement plus sûr).</summary>
    private static double AdressePour(Humain h, Savoir? s) =>
        Math.Clamp(0.8 * h.Personnalite.Adresse + Math.Min(0.25, 0.025 * (s?.Succes ?? 0)), 0, 1);

    private bool FeuIci(Humain h) =>
        Installations.FoyerEn(h.X, h.Y) is not null || Installations.BraiseEn(h.X, h.Y) is not null || PorteLeFeu(h)
        || Catastrophes?.BruleAutour(h.X, h.Y) == true;

    /// <summary>Sécheresse de l'herbe et du bois, de 0 à 1 : la saison, les pluies de l'année, le terrain.</summary>
    private double Sec(Humain h)
    {
        var saison = Calendrier.Decomposer(_maintenant).Saison switch
        {
            Saison.Ete => 1.0,
            Saison.Automne => 0.8,
            Saison.Printemps => 0.5,
            _ => 0.35,
        };
        var i = carte.Indice(h.X, h.Y);
        var pluie = Math.Clamp(1.4 - ecosysteme.PluieEn(i), 0.2, 1.2);
        var terrain = carte.BiomeEn(h.X, h.Y) switch
        {
            Biome.Marais or Biome.ForetTropicale or Biome.Riviere or Biome.Mangrove => 0.5,
            Biome.Lande => 1.1,
            Biome.Desert => 1.2,
            _ => 1.0,
        };
        return Math.Clamp(saison * pluie * terrain, 0, 1);
    }

    private ContexteGeste Contexte(Humain h, Savoir? s = null) =>
        new(FeuIci(h), EauDouceAPortee(h.X, h.Y), Sec(h), carte.BiomeEn(h.X, h.Y), AdressePour(h, s), Ingeniosite(h),
            s is null ? 0 : h.Savoirs.Where(x => x.Effet == Effet.Flamme && x.Geste == s.Geste && x.A == s.A && x.B == s.B).Sum(x => x.Succes),
            h.Objets.Count == 0 ? 0 : h.Objets.Max(Lois.Coupe), Humains.Arts.Aptitude(h));

    private bool VolcanProche(int x, int y) =>
        Catastrophes?.Geologie.Volcans.Any(v => Distance(v.X, v.Y, x, y) <= 25) == true;

    /// <summary>Matières à portée de main : ce que la case offre, ce qu'on porte, le feu et l'eau s'il y en a.</summary>
    private List<Matiere> Materiaux(Humain h)
    {
        var l = Disponibles(h).ToList();
        foreach (var o in h.Objets) if (!l.Contains(o.Matiere)) l.Add(o.Matiere);
        if (FeuIci(h)) l.Add(Matiere.Feu);
        if (EauDouceAPortee(h.X, h.Y)) l.Add(Matiere.Eau);
        return l;
    }

    private bool APortee(Humain h, Matiere m) => m switch
    {
        Matiere.Aucune => true,
        Matiere.Feu => FeuIci(h),
        Matiere.Eau => EauDouceAPortee(h.X, h.Y),
        _ => h.Porte(m) || Disponibles(h).Contains(m),
    };

    // ------------------------------------------------------------------ Jouer, essayer

    /// <summary>
    /// Au repos, l'humain manipule ce qu'il a sous la main : il frappe deux pierres, frotte deux bâtons, tord une liane,
    /// goûte une plante… Les enfants jouent bien plus que les adultes ; les esprits ouverts essaient plus.
    /// La plupart des essais ne donnent rien.
    /// </summary>
    private bool Experimenter(Humain h, IReadOnlyList<Humain> tous)
    {
        var p = h.Personnalite;
        var enfant = h.EstEnfant(_maintenant);
        var envie = 0.004 * (0.3 + p.Ouverture) * (enfant ? 2.5 : 1) * (h.Humeur < 30 ? 0.4 : 1);
        if (alea.Reel() >= envie) return false;

        var materiaux = Materiaux(h);
        // Les gestes du jeu : frapper, frotter, tordre, entasser, pousser… (assembler, percer, trancher demandent une intention).
        // Là où la matière a sa chimie, on pétrit aussi, on laisse au feu, on mêle, on souffle sur la braise.
        var tirage = alea.Entier(AvecLaMatiere ? 13 + GestesDeLaMatiere.Length : 13);
        var geste = tirage >= 13 ? GestesDeLaMatiere[tirage - 13] : tirage == 12 ? Geste.Pousser : (Geste)tirage;
        var a = materiaux.Count == 0 ? Matiere.Aucune : materiaux[alea.Entier(materiaux.Count)];
        var b = materiaux.Count == 0 ? Matiere.Aucune : materiaux[alea.Entier(materiaux.Count)];
        if (tirage >= 13) return JouerAvecLaMatiere(h, geste, a, b, materiaux, tous);
        if (geste is Geste.Manger or Geste.Appliquer)
        {
            // Goûter une plante inconnue : seulement les plus audacieux, et pas les petits (l'aversion pour la nouveauté
            // alimentaire protège des poisons). Les enfants mangent surtout ce qu'ils ont vu manger les adultes (Cashdan, 1994) :
            // leur goût du jeu ne va que rarement jusqu'à avaler l'inconnu.
            if (h.EstPetitEnfant(_maintenant) || alea.Reel() >= 0.15 * p.Ouverture * (enfant ? 0.2 : 1)) return false;
            var especes = Flore.Sur(carte.BiomeEn(h.X, h.Y));
            if (especes.Count == 0) return false;
            var espece = especes[alea.Entier(especes.Count)];
            if (h.Savoirs.Exists(s => s.Espece == espece.Numero && s.Effet == Effet.Poison && s.Confiance >= 0.5)) return false;
            h.Activite = Activite.Experimenter;
            h.CibleX = h.CibleY = -1;
            Gouter(h, espece, geste, tous, OrigineSavoir.Essai);
            return true;
        }
        // Qui sait nager y retourne volontiers quand il fait chaud.
        if (h.Nage > 0 || h.Sait(Effet.Nage))
        {
            if (_temperature >= 24 && EauProfondeAPortee(h.X, h.Y) && alea.Reel() < 0.5) geste = Geste.Nager;
        }
        if (geste == Geste.Nager)
        {
            // On entre dans l'eau surtout quand il fait chaud.
            if (!EauProfondeAPortee(h.X, h.Y) || _temperature < 24 || h.EstPetitEnfant(_maintenant)) return false;
            a = b = Matiere.Eau;
        }
        if (geste == Geste.Approcher && FeuIci(h) && alea.Reel() < 0.3) a = Matiere.Aucune;
        // Un outil neuf s'essaie sur tout : celui qui a une hache en main frappe le tronc couché à ses pieds.
        if (geste == Geste.Frapper && materiaux.Contains(Matiere.Tronc) && h.Objets.Exists(o => Lois.Coupe(o) > 0.3) && alea.Reel() < 0.3)
            (geste, a, b) = (Geste.Trancher, Matiere.Tronc, Matiere.Assemblage);
        // On pousse une chose sur le sol, ou sur l'eau si l'on est au bord.
        if (geste == Geste.Pousser) b = EauDouceAPortee(h.X, h.Y) && alea.Reel() < 0.5 ? Matiere.Eau : Matiere.Aucune;

        h.Activite = Activite.Experimenter;
        h.CibleX = h.CibleY = -1;
        Faire(h, geste, a, b, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>Fait un geste et constate ce qui arrive. Renvoie l'effet obtenu.</summary>
    private Effet Faire(Humain h, Geste geste, Matiere a, Matiere b, OrigineSavoir origine, IReadOnlyList<Humain> tous, Humain? maitre = null)
    {
        var connu = h.Savoirs.Find(s => s.Geste == geste && s.A == a && s.B == b && s.Espece == 0);
        var r = Physique.Tenter(geste, a, b, Contexte(h, connu), alea);
        if (geste == Geste.Nager) Baignade(h, r.Effet == Effet.Nage);
        if (AvecLaMatiere)
        {
            // Là où les lois d'hier ne disent rien, celles de la matière jugent : une argile approchée du feu, un cuivre frappé.
            if (r.Effet == Effet.Rien && geste is Geste.Approcher or Geste.Frapper) return FaireMatiere(h, geste, a, b, origine, tous, maitre);
            // L'éclat tiré d'un silex chauffé est plus fin.
            if (r.Effet == Effet.Eclat && a == Matiere.Silex && SilexChauffe(h) is >= 0 and var ic)
            {
                h.Objets.RemoveAt(ic);
                r = r with { Qualite = Math.Min(1, r.Qualite + 0.15) };
            }
        }
        Constater(h, geste, a, b, r, origine, tous, maitre);
        return r.Effet;
    }

    /// <summary>
    /// Ce qui arrive après un geste : le produit est pris, le feu allumé, l'abri dressé ; l'humain retient le geste s'il a
    /// donné quelque chose, et ceux qui le regardaient peuvent l'imiter.
    /// </summary>
    private void Constater(Humain h, Geste g, Matiere a, Matiere b, Resultat r, OrigineSavoir origine, IReadOnlyList<Humain> tous,
        Humain? maitre = null, Recit? recit = null)
    {
        if (r.Effet == Effet.Rien)
        {
            // Un geste qu'il croyait savoir faire a échoué : il y croit un peu moins.
            var i = h.Savoirs.FindIndex(s => s.Geste == g && s.A == a && s.B == b && s.Espece == 0);
            if (i >= 0) h.Savoirs[i] = h.Savoirs[i] with { Essais = h.Savoirs[i].Essais + 1 };
            return;
        }

        // Encore faut-il s'en apercevoir et y voir un intérêt : ce qui répond à un besoin du moment frappe l'esprit ;
        // le reste n'est remarqué que par les curieux. Un geste qu'on sait déjà faire, lui, est reconnu.
        if (origine == OrigineSavoir.Essai && !h.Savoirs.Exists(s => s.Geste == g && s.A == a && s.B == b && s.Effet == r.Effet))
        {
            // Une cadence, une couleur : ce sont l'oreille et l'œil qui la remarquent.
            var remarque = Pertinent(h, r.Effet) ? 0.5 + 0.5 * Ingeniosite(h)
                : r.Effet is Effet.Rythme or Effet.Ocre ? 0.1 + 0.3 * h.Personnalite.Ouverture + 0.4 * Humains.Arts.Aptitude(h)
                : 0.15 + 0.45 * h.Personnalite.Ouverture;
            if (alea.Reel() >= remarque) return;
        }
        var evenement = Retenir(h, g, a, b, r.Effet, 0, Symptome.Aucun, origine, maitre?.Id ?? 0, tous, recit: recit);
        Produire(h, g, a, b, r, evenement);
        if (origine != OrigineSavoir.Imitation) Montrer(h, g, a, b, r.Effet, 0, Symptome.Aucun, tous, evenement);
    }

    /// <summary>L'effet répond-il à ce que l'humain ressent en ce moment (froid, faim, soif) ?</summary>
    private bool Pertinent(Humain h, Effet e) => e switch
    {
        // Qui sait emporter le feu sur un brandon voit tout de suite ce que vaut une braise qui dure.
        Effet.PorteFeu => h.Sait(Effet.Brandon),
        Effet.Couvert or Effet.Abri or Effet.Chaleur or Effet.Flamme or Effet.Foyer => h.Froid > 15 || _temperature < 14,
        Effet.Tubercules or Effet.Decoupe or Effet.Chasse or Effet.Cuisson => h.Faim > 30,
        Effet.EauPortee => h.Soif > 30,
        _ => false,
    };

    /// <summary>Ce que produit le geste réussi.</summary>
    private void Produire(Humain h, Geste g, Matiere a, Matiere b, Resultat r, long evenement)
    {
        switch (r.Effet)
        {
            case Effet.Eclat or Effet.Pointe or Effet.Durci or Effet.Corde or Effet.Brandon or Effet.PorteFeu:
                if (r.Effet == Effet.Durci) h.Objets.RemoveAll(o => o.Matiere == Matiere.BatonPointu);
                if (r.Effet == Effet.Pointe) User(h, b, 0.1);
                if (r.Effet == Effet.Brandon) h.Objets.RemoveAll(o => o.Matiere == Matiere.Branche || o.Matiere == Matiere.BoisSec);
                if (r.Effet == Effet.PorteFeu && h.Objets.FindIndex(o => o.Matiere == a) is var corde and >= 0) h.Objets.RemoveAt(corde);
                Prendre(h, new Objet(r.Produit, r.Qualite, 1));
                break;
            case Effet.Decoupe:
                User(h, b, 0.15);
                Prendre(h, new Objet(Matiere.Peau, r.Qualite, 1));
                break;
            case Effet.Flamme:
            {
                // Pendant un pas simultané, le feu prend sur la case après le pas.
                var (x, y, id, le, degage) = (h.X, h.Y, h.Id, _maintenant, DegagerLeFoyer(h));
                Tour.Differer(() => Installations.Allumer(x, y, 1, le, id, evenement, degage));
                break;
            }
            case Effet.Foyer:
            {
                // Le feu porté (brandon ou mèche qui couve) posé sous le bois devient un foyer ; le bois ajouté nourrit le feu
                // quelques heures.
                var porte = b is Matiere.Brandon or Matiere.PorteFeu;
                var ailleurs = porte && Installations.FoyerEn(h.X, h.Y) is null && Installations.BraiseEn(h.X, h.Y) is null;
                if (porte && h.Objets.FindIndex(o => o.Matiere == b) is var ip and >= 0) h.Objets.RemoveAt(ip);
                // Avec une hache, on fend de grosses bûches qui brûlent bien plus longtemps que des branches cassées à la main.
                var bois = (4 + 4 * alea.Reel()) * (1 + (h.Objets.Count == 0 ? 0 : h.Objets.Max(Lois.Coupe)));
                var (fx, fy, fid, fle, degage) = (h.X, h.Y, h.Id, _maintenant, DegagerLeFoyer(h));
                Tour.Differer(() => Installations.Nourrir(fx, fy, bois, fle, fid, evenement, degage));
                if (ailleurs) AvoirPorteLeFeu(h);
                break;
            }
            case Effet.Couvert:
                if (a == Matiere.Peau)
                {
                    var peau = h.Objets.Find(o => o.Matiere == Matiere.Peau);
                    if (peau is not null) h.Objets.Remove(peau);
                    // Une alêne de métal perce la peau proprement : cousue bord à bord, elle ferme mieux et garde mieux la chaleur.
                    var alene = h.Objets.Exists(Attirance.Alene) ? 1.25 : 1;
                    h.Vetement = new Objet(Matiere.Peau, (peau?.Qualite ?? 0.5) * r.Qualite / 0.6 * alene, 1);
                }
                else h.Vetement = new Objet(a, r.Qualite, 1);
                break;
            case Effet.Abri:
                // Une hache taille des perches droites et solides : la hutte tient mieux.
                var coupe = h.Objets.Count == 0 ? 0 : h.Objets.Max(Lois.Coupe);
                var (bx, by, bid, ble, solidite) = (h.X, h.Y, h.Id, _maintenant, Math.Clamp(r.Qualite * (1 + 0.4 * coupe), 0, 1));
                // Des pierres liées de pâte blanche à la base, les parois enduites : elle tient bien mieux.
                var lie = Lier(h);
                Tour.Differer(() =>
                {
                    Installations.Batir(bx, by, solidite, ble, bid, evenement);
                    if (lie > 0) Installations.Enduire(bx, by, ble, lie);
                });
                h.Memoire.Retenir(TypeLieu.Abri, h.X, h.Y, _maintenant, Aptitude(h));
                break;
            case Effet.EauPortee:
                // Une calebasse (on en cueille une s'il le faut), ou la peau qu'il porte, nouée en outre.
                var recipient = h.Objets.Find(o => o.Matiere == a);
                if (recipient is null && a == Matiere.Calebasse) Prendre(h, new Objet(Matiere.Calebasse, 1, 1, Plein: true));
                else if (recipient is not null) h.Objets[h.Objets.IndexOf(recipient)] = recipient with { Plein = true };
                break;
            case Effet.Rondelle:
                if (b == Matiere.Assemblage && h.Objets.MaxBy(Lois.Coupe) is { } outil) UserObjet(h, outil, 0.04);
                else User(h, b, 0.04);
                Prendre(h, new Objet(Matiere.Rondelle, r.Qualite, 1));
                break;
            case Effet.Percage:
                User(h, b, 0.06);
                if (r.Produit == Matiere.RondellePercee)
                {
                    var rondelle = h.Objets.Find(o => o.Matiere == Matiere.Rondelle);
                    if (rondelle is not null) h.Objets.Remove(rondelle);
                    h.Objets.Add(new Objet(Matiere.RondellePercee, Math.Min(r.Qualite, rondelle?.Qualite ?? r.Qualite), 1));
                }
                break;
            case Effet.Tubercules:
                var i = carte.Indice(h.X, h.Y);
                var pris = Ecosysteme.Prelever(ecosysteme.Plantes, i, 4);
                h.Faim = Math.Max(0, h.Faim - 8 - 3 * pris);
                User(h, a, 0.03);
                break;
        }
    }

    /// <summary>Use cet objet-là (un objet assemblé parmi d'autres) ; hors d'usage, il est jeté.</summary>
    private static void UserObjet(Humain h, Objet o, double usure)
    {
        var i = h.Objets.IndexOf(o);
        if (i < 0) return;
        if (o.Usure - usure <= 0) h.Objets.RemoveAt(i);
        else h.Objets[i] = o with { Usure = o.Usure - usure };
    }

    /// <summary>Use un objet porté ; hors d'usage, il est jeté.</summary>
    private static void User(Humain h, Matiere m, double usure)
    {
        var o = h.Objets.Find(x => x.Matiere == m);
        if (o is null) return;
        var i = h.Objets.IndexOf(o);
        if (o.Usure - usure <= 0) h.Objets.RemoveAt(i);
        else h.Objets[i] = o with { Usure = o.Usure - usure };
    }

    /// <summary>Garde un objet sur soi : s'il n'y a plus de place, le moins utile est laissé.</summary>
    private static void Prendre(Humain h, Objet objet)
    {
        var meme = h.Objets.Find(o => o.Matiere == objet.Matiere && (o.Matiere != Matiere.Assemblage || MemeUsage(o, objet))
            && (o.Matiere != Matiere.Substance || MemeChose(o, objet)));
        if (meme is not null)
        {
            if (meme.Qualite * meme.Usure < objet.Qualite * objet.Usure) h.Objets[h.Objets.IndexOf(meme)] = objet;
            return;
        }
        if (h.Objets.Count >= ObjetsMax)
        {
            // On laisse ce dont on ne connaît pas d'usage, sinon le plus usé.
            var laisse = h.Objets.OrderBy(o => o.Matiere is Matiere.Assemblage or Matiere.Carcasse or Matiere.Substance || h.Savoirs.Exists(s => (s.A == o.Matiere || s.B == o.Matiere) && s.Confiance >= 0.5) ? 1 : 0)
                .ThenBy(o => o.Qualite * o.Usure).First();
            h.Objets.Remove(laisse);
        }
        h.Objets.Add(objet);
    }

    /// <summary>
    /// Retient (ou renforce) un savoir. Un savoir nouveau est noté au journal ; la toute première fois qu'un effet est obtenu
    /// dans le monde, c'est un tournant (★). Renvoie le numéro de l'événement qui le rapporte (0 s'il était déjà connu).
    /// <paramref name="heritage"/> : réussites que l'élève tient de l'habileté de son modèle (voir <see cref="Heritage"/>) ;
    /// <paramref name="karmaMaitre"/> : ce que vaut, pour le maître, de l'avoir enseigné (voir <see cref="Regard.Enseigner"/>).
    /// </summary>
    private long Retenir(Humain h, Geste g, Matiere a, Matiere b, Effet e, int espece, Symptome mal, OrigineSavoir origine, int maitreId,
        IReadOnlyList<Humain> tous, bool succes = true, string pieces = "", Recit? recit = null, int heritage = 0, double karmaMaitre = 0)
    {
        // Ce qu'un autre apprend en regardant celui dont c'est le tour, il le retient après le pas.
        if (Tour.Autrui(h))
        {
            var ih = h.Id;
            Tour.Differer(tous, t => Retenir(t[ih - 1], g, a, b, e, espece, mal, origine, maitreId, t, succes, pieces, recit, heritage, karmaMaitre));
            return 0;
        }
        var i = h.Savoirs.FindIndex(s => s.Meme(g, a, b, e, espece, mal) && s.Pieces == pieces);
        if (i >= 0)
        {
            var s = h.Savoirs[i];
            // Un maître corrige le geste de celui qui le savait mal : il en garde ce qu'il a compris de mieux.
            var gain = Math.Max(0, heritage - s.Succes);
            h.Savoirs[i] = s with { Essais = s.Essais + 1 + gain, Succes = s.Succes + (succes ? 1 : 0) + gain, UtiliseLe = _maintenant };
            return 0;
        }
        var structure = Lois.Structure(pieces);
        var premier = !Installations.Decouvertes.Exists(d => d.Effet == e && Lois.Structure(d.Pieces) == structure);
        // Un savoir que tout le monde avait perdu, retrouvé : c'est aussi un tournant.
        var retrouve = !premier && espece == 0 && !tous.Vivants().Any(o => o.EstVivant && o.Savoirs.Exists(s => s.Effet == e && s.Espece == 0));
        var nom = espece > 0 ? Flore.Get(espece).Dit : null;
        var nouveau = new Savoir(g, a, b, e, espece, mal, 1 + heritage, (succes ? 1 : 0) + heritage, _maintenant, _maintenant, origine, maitreId, 0, pieces);
        var maitre = maitreId > 0 && maitreId <= tous.Count ? tous[maitreId - 1] : null;
        var description = Physique.Decrire(nouveau, nom);
        var comment = recit is not null ? recit : origine switch
        {
            OrigineSavoir.Essai => Raconter(h, nouveau, nom),
            OrigineSavoir.Observation => Observation(h, nouveau),
            OrigineSavoir.Reflexion => new Recit("ing.reflexion", ("h", h)),
            OrigineSavoir.Imitation => maitre is null ? new Recit("ing.imitation.autre", ("h", h)) : new Recit("ing.imitation", ("h", h), ("m", maitre)),
            _ => new Recit("ing.entite", ("h", h)),
        };
        // Un savoir banal (le feu chauffe, le bois frotté tiédit) n'est pas noté chez chacun : seulement la première fois au monde.
        // Un chant, un rythme, une danse, la parure d'ocre appris des autres : cela se fait à chaque veillée.
        var banal = e is Effet.Chaleur or Effet.Tiedeur || origine == OrigineSavoir.Imitation && e is Effet.Etincelle or Effet.Chant or Effet.Rythme or Effet.Danse or Effet.Ocre;
        // Appris d'un autre groupe, ou de quelqu'un venu d'ailleurs, alors que personne des siens ne le savait : le savoir entre
        // dans sa communauté, et le journal dit d'où il vient.
        var venu = premier || retrouve || maitre is null || e is Effet.Chaleur or Effet.Tiedeur ? null : VenuDAilleurs(h, maitre, e, structure, tous);
        if (maitre is not null && h.RassemblementId != 0 && maitre.RassemblementId == h.RassemblementId && maitre.CommunauteId != h.CommunauteId)
            Social?.ApprisAuRassemblement(h.RassemblementId);
        long id = 0;
        if (premier || retrouve || !banal || venu is not null)
        {
            var texte = premier ? new Recit("ing.premiere", ("comment", comment), ("h", h), ("savoir", description))
                : retrouve ? new Recit("ing.retrouve", ("comment", comment), ("h", h), ("savoir", description))
                : venu is not null ? new Recit("ing.venu", ("venu", venu.Value.Texte), ("comment", comment), ("h", h), ("savoir", description))
                : new Recit("ing.comprend", ("comment", comment), ("h", h), ("savoir", description));
            id = Noter(_maintenant, TypeEvenement.Decouverte, h, maitre, texte, significatif: premier || retrouve || venu is not null, karmaAutre: karmaMaitre,
                autreCommunaute: venu?.Source ?? 0);
        }
        h.Savoirs.Add(nouveau with { EvenementId = id });
        if (premier)
        {
            // Deux qui trouvent la même chose dans le même pas : la première au monde est celle du premier dans l'ordre.
            var decouverte = new Decouverte(e, h.Id, _maintenant, id, origine, pieces, h.CommunauteId);
            Tour.Differer(() =>
            {
                if (!Installations.Decouvertes.Exists(d => d.Effet == decouverte.Effet && Lois.Structure(d.Pieces) == structure)) Installations.Decouvertes.Add(decouverte);
            });
        }
        if (premier || !banal)
        {
            Jardin.Confier(h, TypeIntime.Question, 0, Question(nouveau), 30 + 30 * h.Personnalite.Ouverture, id, _maintenant, $"savoir:{(int)e}:{espece}{(pieces.Length > 0 ? ":" + pieces : "")}");
        }
        return id;
    }

    /// <summary>
    /// Un savoir qui entre dans la communauté de <paramref name="h"/> : appris d'un membre d'un autre groupe (en visite, au
    /// rassemblement, chez des voisins), ou de quelqu'un des siens venu d'ailleurs, alors qu'aucun autre des siens ne le sait.
    /// Renvoie la phrase qui le dit et la communauté d'où il vient, ou null.
    /// </summary>
    private (Recit Texte, int Source)? VenuDAilleurs(Humain h, Humain maitre, Effet e, string structure, IReadOnlyList<Humain> tous)
    {
        if (h.CommunauteId == 0 || Social is not { } social || social.CommunauteDe(h) is not { } siens) return null;
        var etranger = maitre.CommunauteId != 0 && maitre.CommunauteId != h.CommunauteId;
        var source = etranger ? maitre.CommunauteId
            : maitre.CommunauteId == h.CommunauteId && maitre.VenuDeId != 0 && maitre.VenuDeId != h.CommunauteId ? maitre.VenuDeId : 0;
        if (source == 0 || source > social.Communautes.Count) return null;
        foreach (var o in tous.Vivants())
        {
            if (!o.EstVivant || o.Id == h.Id || o.Id == maitre.Id || o.CommunauteId != h.CommunauteId) continue;
            if (o.Savoirs.Exists(s => s.Effet == e && s.Espece == 0 && Lois.Structure(s.Pieces) == structure)) return null;
        }
        var (nh, ns) = (siens, social.Communautes[source - 1]);
        object ou = h.VisiteChezId == maitre.Id ? new Recit("ing.ou.visite", ("h", h))
            : maitre.VisiteChezId == h.Id ? new Recit("ing.ou.venu", ("m", maitre))
            : h.RassemblementId != 0 && h.RassemblementId == maitre.RassemblementId ? Recits.Recit.Seul("ing.ou.rassemblement")
            : "";
        var texte = etranger
            ? new Recit("ing.venu.etranger", ("nh", nh), ("ns", ns), ("h", h), ("m", maitre), ("ou", ou))
            : new Recit("ing.venu.siens", ("m", maitre), ("ns", ns), ("nh", nh));
        return (texte, source);
    }

    /// <summary>Ce qu'il s'est passé, dit simplement.</summary>
    private static Recit Raconter(Humain h, Savoir s, object? plante)
    {
        return s.Effet switch
        {
            Effet.Eclat => new Recit("ing.essai.Eclat", ("h", h), ("a", Physique.Nom(s.A)), ("b", Physique.Nom(s.B))),
            Effet.Etincelle => new Recit("ing.essai.Etincelle", ("h", h), ("a", Physique.Nom(s.A)), ("b", Physique.Nom(s.B))),
            Effet.Flamme when s.Geste == Geste.Frotter => new Recit("ing.essai.Flamme.frotter", ("h", h)),
            Effet.Flamme => new Recit("ing.essai.Flamme", ("h", h), ("a", Physique.Nom(s.A)), ("b", Physique.Nom(s.B))),
            Effet.Tiedeur => new Recit("ing.essai.Tiedeur", ("h", h)),
            Effet.Pointe => new Recit("ing.essai.Pointe", ("h", h), ("b", Physique.Nom(s.B))),
            Effet.Corde => new Recit("ing.essai.Corde", ("h", h)),
            Effet.Couvert => new Recit(s.A == Matiere.Peau ? "ing.essai.Couvert.peau" : "ing.essai.Couvert", ("h", h)),
            Effet.Abri => new Recit("ing.essai.Abri", ("h", h)),
            Effet.Tubercules => new Recit("ing.essai.Tubercules", ("h", h), ("a", Physique.Nom(s.A))),
            Effet.Nage => new Recit("ing.essai.Nage", ("h", h)),
            Effet.Durci => new Recit("ing.essai.Durci", ("h", h)),
            Effet.Brandon => new Recit("ing.essai.Brandon", ("h", h)),
            Effet.PorteFeu => new Recit(s.A == Matiere.Corde ? "ing.essai.PorteFeu.corde" : "ing.essai.PorteFeu", ("h", h), ("a", Physique.Nom(s.A))),
            Effet.Cuisson => new Recit("ing.essai.Cuisson", ("h", h)),
            Effet.Decoupe => new Recit("ing.essai.Decoupe", ("h", h)),
            Effet.Foyer => new Recit("ing.essai.Foyer", ("h", h)),
            Effet.EauPortee => new Recit("ing.essai.EauPortee", ("h", h)),
            Effet.Chasse => new Recit("ing.essai.Chasse", ("h", h), ("a", Physique.Nom(s.A))),
            Effet.Chaleur => new Recit("ing.essai.Chaleur", ("h", h)),
            Effet.Poison => new Recit("ing.essai.Poison", ("h", h), ("plante", plante)),
            Effet.Visions => new Recit("ing.essai.Visions", ("h", h), ("plante", plante)),
            Effet.Soulagement => new Recit("ing.essai.Soulagement", ("h", h), ("plante", plante)),
            Effet.Roule => new Recit("ing.essai.Roule", ("h", h), ("a", Physique.Nom(s.A))),
            Effet.Flotte => new Recit("ing.essai.Flotte", ("h", h), ("a", Physique.Nom(s.A))),
            Effet.Percage => new Recit("ing.essai.Percage", ("h", h), ("a", Physique.Nom(s.A)), ("b", Physique.Nom(s.B))),
            Effet.Rondelle => new Recit("ing.essai.Rondelle", ("h", h), ("a", Physique.Nom(s.A))),
            Effet.Pansement when s.A == Matiere.Eau => new Recit("ing.essai.Pansement.eau", ("h", h)),
            Effet.Pansement => new Recit("ing.essai.Pansement", ("h", h)),
            Effet.Rythme => new Recit("ing.essai.Rythme", ("h", h), ("a", Physique.Nom(s.A)), ("b", Physique.Nom(s.B))),
            Effet.Ocre => new Recit("ing.essai.Ocre", ("h", h)),
            Effet.Chant => new Recit("ing.essai.Chant", ("h", h)),
            Effet.Danse => new Recit("ing.essai.Danse", ("h", h)),
            Effet.Peinture => new Recit("ing.essai.Peinture", ("h", h)),
            _ => new Recit("ing.essai.autre", ("h", h)),
        };
    }

    private static Recit Observation(Humain h, Savoir s) => s.Effet switch
    {
        Effet.Chaleur => new Recit("ing.observation.Chaleur", ("h", h)),
        Effet.Brandon => new Recit("ing.observation.Brandon", ("h", h)),
        Effet.Cuisson => new Recit("ing.observation.Cuisson", ("h", h)),
        Effet.Peinture => new Recit("ing.observation.Peinture", ("h", h)),
        _ => new Recit("ing.observation.autre", ("h", h)),
    };

    /// <summary>La question que le savoir fait naître, au jardin secret.</summary>
    private static Recit Question(Savoir s) => Recits.Recit.Seul(s.Effet switch
    {
        Effet.Etincelle => "ing.question.Etincelle",
        Effet.Flamme => "ing.question.Flamme",
        Effet.Chaleur => "ing.question.Chaleur",
        Effet.Visions => "ing.question.Visions",
        Effet.Poison => "ing.question.Poison",
        Effet.Soulagement => "ing.question.Soulagement",
        Effet.Nage => "ing.question.Nage",
        Effet.Roule => "ing.question.Roule",
        Effet.Flotte => "ing.question.Flotte",
        Effet.Flottaison => "ing.question.Flottaison",
        Effet.Navigation => "ing.question.Navigation",
        Effet.RouleSurAxe => "ing.question.RouleSurAxe",
        Effet.Pansement => "ing.question.Pansement",
        Effet.Rythme => "ing.question.Rythme",
        Effet.Chant => "ing.question.Chant",
        Effet.Danse => "ing.question.Danse",
        Effet.Ocre => "ing.question.Ocre",
        Effet.Peinture => "ing.question.Peinture",
        Effet.MainsPropres or Effet.Latrines or Effet.EauVive or Effet.ViandeSaine => "ing.question.MainsPropres",
        Effet.Fumage => "ing.question.Fumage",
        Effet.Fumee => "ing.question.Fumee",
        Effet.Brulis => "ing.question.Brulis",
        Effet.Braises => "ing.question.Braises",
        Effet.PorteFeu => "ing.question.PorteFeu",
        Effet.FoyerDegage or Effet.PasDansLeVent or Effet.EteindreEnPartant => "ing.question.FoyerDegage",
        Effet.BrulisProtecteur => "ing.question.BrulisProtecteur",
        Effet.CacheDEau or Effet.Sechage or Effet.Provisions or Effet.CacheFroide => "ing.question.CacheDEau",
        Effet.Transformation => "ing.question.Transformation",
        Effet.Modelage => "ing.question.Modelage",
        _ => "ing.question.autre",
    });

    /// <summary>
    /// Ceux qui regardent peuvent imiter : d'autant plus qu'ils sont curieux, qu'ils aiment celui qui fait le geste ou
    /// l'admirent, et que celui-ci montre bien (empathie cognitive). Les enfants imitent bien plus (Csibra et Gergely, 2009).
    /// On regarde plus volontiers le plus habile (biais de prestige : Henrich et Gil-White, 2001), et l'on ne retient de
    /// son geste qu'une part de son habileté (voir <see cref="Heritage"/>).
    /// </summary>
    private void Montrer(Humain h, Geste g, Matiere a, Matiere b, Effet e, int espece, Symptome mal, IReadOnlyList<Humain> tous, long cause,
        string pieces = "")
    {
        if (e is Effet.Tiedeur or Effet.Visions) return;
        var modele = h.Savoirs.Find(s => s.Meme(g, a, b, e, espece, mal) && s.Pieces == pieces);
        // Le geste sûr d'un habile attire les regards ; le geste hésitant d'un débutant, moins (1 pour un modèle moyen).
        var prestige = (0.5 + AdressePour(h, modele) / AdresseMoyenne) / 1.5;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.EstBebe(_maintenant) || o.Activite == Activite.Dormir) continue;
            if (o.Savoirs.Exists(s => s.Meme(g, a, b, e, espece, mal) && s.Pieces == pieces)) continue;
            var lien = o.LienAvec(h.Id);
            var affection = lien?.Affection ?? 0;
            if (affection < -30) continue;
            var attention = (0.2 + 0.6 * o.Personnalite.Ouverture) * (o.EstEnfant(_maintenant) ? 1.8 : 1)
                            * (0.5 + affection / 100) * (0.6 + 0.8 * h.Personnalite.Moral.EmpathieCognitive) * (0.5 + h.Personnalite.Charisme)
                            // On regarde de plus près les gestes d'un guérisseur reconnu (les enfants apprennent les remèdes
                            // de ceux qu'on dit savoir soigner : McDade et al., 2007 ; Salali et al., 2016).
                            * (h.GuerisseurDepuis > 0 && e is Effet.Soulagement or Effet.Pansement ? 1.6 : 1)
                            // Un mot commun pour la chose montrée aide à comprendre ce qu'on regarde.
                            * (Langues.Partagent(h, o, espece > 0 ? Idees.Plante(espece) : Idees.Savoir(e)) ? 1.4 : 1)
                            * prestige * Mefiance(o, h);
            // Comprendre un geste compliqué demande de l'intelligence et de l'adresse.
            var difficile = Difficile(e, pieces) ? 0.5 + 0.5 * Ingeniosite(o) : 1;
            if (alea.Reel() >= 0.35 * attention * difficile) continue;
            Retenir(o, g, a, b, e, espece, mal, OrigineSavoir.Imitation, h.Id, tous, pieces: pieces, heritage: Heritage(o, modele, e, pieces, false));
            VieSociale.Gratitude(o, h, 0.5);
        }
    }

    /// <summary>
    /// Ce qu'on accorde au geste d'un autre groupe : celui d'un allié se regarde comme celui des siens ; celui d'un inconnu avec
    /// un peu de réserve, celui d'un rival ou d'un ennemi avec méfiance (on imite moins volontiers ceux qu'on ne tient pas pour
    /// siens : Buttelmann et al., 2013 ; Wood et al., 2013).
    /// </summary>
    private double Mefiance(Humain spectateur, Humain modele)
    {
        if (spectateur.CommunauteId == 0 || modele.CommunauteId == 0 || spectateur.CommunauteId == modele.CommunauteId || Social is not { } social) return 1;
        return social.RelationEntre(spectateur.CommunauteId, modele.CommunauteId) switch
        {
            EtatRelation.Alliees => 1,
            EtatRelation.Indifferentes => 0.85,
            EtatRelation.Rivales => 0.6,
            _ => 0.35,
        };
    }

    /// <summary>Gestes qu'il faut de l'intelligence et de l'adresse pour comprendre : le feu par friction, la pointe durcie, l'éclat qui découpe, le trou percé, l'objet assemblé.</summary>
    private static bool Difficile(Effet e, string pieces) => e is Effet.Flamme or Effet.Durci or Effet.Decoupe or Effet.Percage || pieces.Length > 0;

    /// <summary>
    /// Adresse moyenne d'un humain à un geste qu'il connaît à peine (aptitude manuelle 100 ± 15 : environ 0,4 avec la part
    /// de l'habitude). Sert d'étalon au prestige : un modèle moyen est regardé comme avant.
    /// </summary>
    private const double AdresseMoyenne = 0.4;

    /// <summary>
    /// Ce que l'élève garde de l'habileté de son modèle, en réussites : le plus souvent une part seulement, parfois davantage
    /// chez un élève adroit et ingénieux. C'est la copie imparfaite du modèle de Henrich (2004) : un geste difficile ne tient
    /// que si le groupe compte assez de praticiens habiles pour qu'il s'en trouve toujours un bon à copier. Un maître qui
    /// montre exprès transmet mieux qu'un geste vu par hasard (Morgan et al., 2015).
    /// </summary>
    internal int Heritage(Humain eleve, Savoir? modele, Effet e, string pieces, bool enseigne)
    {
        if (modele is null || modele.Succes <= 1) return 0;
        var aptitude = 0.5 * eleve.Personnalite.Adresse + 0.5 * Ingeniosite(eleve);
        var mediane = (enseigne ? 0.45 : 0.25) + 0.35 * aptitude - (Difficile(e, pieces) ? 0.1 : 0);
        var part = Math.Min(1.25, mediane * Math.Exp(0.5 * Biologie.Normale(alea)));
        // La réussite qu'il vient de voir compte déjà comme la sienne (voir Retenir).
        return Math.Max(0, (int)(part * (modele.Succes - 1)));
    }

    private static string Majuscule(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------------ Se servir de ce qu'on sait

    /// <summary>
    /// Un besoin presse : l'humain se sert de ce qu'il sait (le feu, une peau, une hutte, un bâton à fouir), ou, s'il est
    /// ingénieux, rapproche deux choses qu'il sait déjà. Renvoie vrai s'il a agi.
    /// </summary>
    private bool UtiliserSavoirs(Humain h, Activite besoin, double valeur, double seuil, IReadOnlyList<Humain> tous)
    {
        if (h.Savoirs.Count == 0 && !PorteLeFeu(h)) return false;
        var nuit = !Calendrier.Decomposer(_maintenant).EstJour;
        // La nuit, le froid fait rester près du feu, mais pas au point d'y mourir de soif ou de faim : un besoin pressant
        // l'emporte sur la chaleur.
        var pressant = besoin is Activite.Boire or Activite.Cueillir && valeur >= seuil;
        var froid = besoin == Activite.SAbriter && valeur >= seuil || (besoin == Activite.Dormir || nuit && !pressant) && _temperature < 14;
        if (froid && Rechauffer(h, besoin, tous)) return true;
        if (besoin == Activite.Cueillir && valeur >= seuil && Cuisiner(h, tous)) return true;
        if (besoin == Activite.Cueillir && valeur >= seuil && Fouir(h, tous)) return true;
        return false;
    }

    private bool Rechauffer(Humain h, Activite besoin, IReadOnlyList<Humain> tous)
    {
        var foyer = Installations.FoyerEn(h.X, h.Y);
        // Près d'un feu : on l'alimente s'il faiblit, puis on dort ou on se chauffe à côté.
        if (foyer is not null)
        {
            if (NourrirDeCharbon(h)) return true;
            if (foyer.Heures < 6 && h.Sait(Effet.Foyer) && APortee(h, Matiere.BoisSec)
                && Faire(h, Geste.Entasser, Matiere.BoisSec, Matiere.Feu, OrigineSavoir.Essai, tous) == Effet.Foyer)
            {
                h.Activite = Activite.Veiller;
                h.CibleX = h.CibleY = -1;
                return true;
            }
            if (besoin == Activite.Dormir) return false;
            if (AvecLaMatiere && AuCoinDuFeu(h, tous)) return true;
            if (!h.Sait(Effet.Chaleur))
            {
                Constater(h, Geste.Approcher, Matiere.Aucune, Matiere.Feu, new Resultat(Effet.Chaleur, Matiere.Aucune, 1), OrigineSavoir.Essai, tous);
            }
            h.Activite = Activite.Veiller;
            h.CibleX = h.CibleY = -1;
            return true;
        }
        // Une braise qui couve : un peu de bois la ranime.
        if (RanimerLaBraise(h, tous)) return true;
        // Un brandon à la main, ou une mèche qui couve : on en fait un foyer.
        if (FeuPorte(h) is { } porte && (h.Sait(Effet.Foyer) || h.Sait(Effet.Brandon) || h.Sait(Effet.PorteFeu)) && APortee(h, Matiere.BoisSec))
        {
            Faire(h, Geste.Entasser, Matiere.BoisSec, porte, OrigineSavoir.Essai, tous);
            h.Activite = Activite.Fabriquer;
            h.CibleX = h.CibleY = -1;
            return true;
        }
        // Un feu connu, à portée de vue.
        if (h.Sait(Effet.Chaleur) && Installations.Foyers.Count > 0)
        {
            var rayon = RayonPerception(h);
            Foyer? proche = null;
            foreach (var f in Installations.Foyers)
                if (Distance(f.X, f.Y, h.X, h.Y) <= rayon && (proche is null || Distance(f.X, f.Y, h.X, h.Y) < Distance(proche.X, proche.Y, h.X, h.Y))) proche = f;
            if (proche is not null)
            {
                Avancer(h, proche.X, proche.Y, Activite.ChercherAbri);
                if (h.CibleX >= 0) return true;
            }
        }
        // Allumer un feu, si l'on sait le faire et qu'on a ce qu'il faut.
        if (h.Sait(Effet.Chaleur) || h.Sait(Effet.Flamme))
        {
            foreach (var s in h.Savoirs)
            {
                if (s.Effet != Effet.Flamme || s.Confiance < 0.2 || !APortee(h, s.A) || !APortee(h, s.B)) continue;
                if (s.Geste == Geste.Frapper && !APortee(h, Matiere.HerbeSeche)) continue;
                h.Activite = Activite.Fabriquer;
                h.CibleX = h.CibleY = -1;
                Faire(h, s.Geste, s.A, s.B, OrigineSavoir.Essai, tous);
                return true;
            }
            // Réfléchir : une étincelle ressemble à une petite flamme ; le bois frotté chauffe. Et si…
            if (h.Sait(Effet.Chaleur))
            {
                foreach (var s in h.Savoirs)
                {
                    if (!Physique.Ressemble(s.Effet, Effet.Flamme) || !APortee(h, s.A) || !APortee(h, s.B)) continue;
                    if (s.Geste == Geste.Frapper && !APortee(h, Matiere.HerbeSeche)) continue;
                    if (s.Geste == Geste.Frotter && (s.A != Matiere.BoisSec || s.B != Matiere.BoisSec)) continue;
                    if (alea.Reel() >= 0.15 * Ingeniosite(h) * (0.5 + h.Personnalite.Conscience)) continue;
                    h.Activite = Activite.Fabriquer;
                    h.CibleX = h.CibleY = -1;
                    var r = Physique.Tenter(s.Geste, s.A, s.B, Contexte(h, s), alea);
                    if (r.Effet == Effet.Flamme) Constater(h, s.Geste, s.A, s.B, r, OrigineSavoir.Reflexion, tous);
                    return true;
                }
            }
        }
        // Se couvrir.
        if (h.Vetement is not { Usure: > 0.2 })
        {
            var peau = h.Porte(Matiere.Peau) && h.Savoirs.Exists(s => s.Effet == Effet.Couvert && s.A == Matiere.Peau);
            var feuilles = APortee(h, Matiere.GrandesFeuilles) && h.Savoirs.Exists(s => s.Effet == Effet.Couvert && s.A == Matiere.GrandesFeuilles);
            // Qui sait qu'une couverture de feuilles protège peut penser à la peau qu'il porte.
            if (!peau && h.Porte(Matiere.Peau) && h.Sait(Effet.Couvert) && alea.Reel() < 0.1 * Ingeniosite(h)) peau = true;
            if (peau || feuilles)
            {
                h.Activite = Activite.Fabriquer;
                h.CibleX = h.CibleY = -1;
                Faire(h, Geste.SeCouvrir, peau ? Matiere.Peau : Matiere.GrandesFeuilles, Matiere.Aucune, OrigineSavoir.Essai, tous);
                return true;
            }
        }
        // Bâtir une hutte pour la nuit.
        if (besoin == Activite.Dormir && _temperature < 12 && h.Sait(Effet.Abri) && !HutteProche(h.X, h.Y)
            && APortee(h, Matiere.GrandesFeuilles) && APortee(h, Matiere.Branche) && alea.Reel() < 0.3 + 0.4 * h.Personnalite.Conscience)
        {
            h.Activite = Activite.Fabriquer;
            h.CibleX = h.CibleY = -1;
            Faire(h, Geste.Entasser, Matiere.GrandesFeuilles, Matiere.Branche, OrigineSavoir.Essai, tous);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Au calme, un adulte refait l'outil qui lui manque, s'il sait le faire et en a la matière : un éclat pour découper,
    /// une pointe pour chasser, durcie au feu s'il en connaît le secret. C'est en refaisant les gestes qu'on les garde,
    /// et que les autres les apprennent en regardant (Stout, 2011).
    /// </summary>
    private bool Outiller(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Savoirs.Count == 0 || alea.Reel() >= 0.05 * (0.5 + h.Personnalite.Conscience)) return false;
        Savoir? Moyen(Effet e) => h.Savoirs.Where(s => s.Effet == e && s.Espece == 0 && s.Confiance >= 0.4 && APortee(h, s.A) && APortee(h, s.B))
            .MaxBy(s => s.Confiance);
        var lame = h.Porte(Matiere.Eclat) || h.Porte(Matiere.Obsidienne);
        var arme = h.Porte(Matiere.Epieu) || h.Porte(Matiere.BatonPointu);
        // Durcir au feu la pointe qu'on porte.
        if (h.Porte(Matiere.BatonPointu) && FeuIci(h) && h.Sait(Effet.Durci))
            return Fabriquer(h, Geste.Approcher, Matiere.BatonPointu, Matiere.Feu, tous);
        // Tailler une pointe.
        if (!arme && (h.Sait(Effet.Chasse) || h.Sait(Effet.Pointe)) && Moyen(Effet.Pointe) is { } pointe)
            return Fabriquer(h, pointe.Geste, pointe.A, pointe.B, tous);
        // Tailler un éclat, pour découper ou pour tailler une pointe.
        if (!lame && (h.Sait(Effet.Decoupe) || h.Sait(Effet.Pointe) || h.Sait(Effet.Eclat)) && Moyen(Effet.Eclat) is { } eclat)
            return Fabriquer(h, eclat.Geste, eclat.A, eclat.B, tous);
        return false;
    }

    private bool Fabriquer(Humain h, Geste g, Matiere a, Matiere b, IReadOnlyList<Humain> tous)
    {
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        Faire(h, g, a, b, OrigineSavoir.Essai, tous);
        return true;
    }

    private bool HutteProche(int x, int y)
    {
        foreach (var hu in Installations.Huttes) if (Distance(hu.X, hu.Y, x, y) <= 2) return true;
        return false;
    }

    /// <summary>Plus rien à cueillir ici : celui qui sait fouir déterre des racines.</summary>
    private bool Fouir(Humain h, IReadOnlyList<Humain> tous)
    {
        if (ecosysteme.Plantes[carte.Indice(h.X, h.Y)] >= 1000) return false;
        var s = h.Savoirs.Where(x => x.Effet == Effet.Tubercules && x.Confiance >= 0.3 && APortee(h, x.A)).MaxBy(x => x.Confiance);
        if (s is null) return false;
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        Faire(h, Geste.Fouir, s.A, Matiere.Aucune, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>
    /// Ramasser ce qui sert : celui qui sait tirer des étincelles d'un silex et d'une pyrite les garde sur lui ; celui qui
    /// sait garder l'eau dans une calebasse en prend une ; un brandon se consume.
    /// </summary>
    private void Ramasser(Humain h)
    {
        for (var i = h.Objets.Count - 1; i >= 0; i--)
        {
            var o = h.Objets[i];
            // Un brandon se consume en une heure et demie environ ; une mèche qui couve tient de quatre heures (fibres lâches)
            // à une journée (corde bien serrée).
            var usure = o.Matiere switch
            {
                Matiere.Brandon => 0.11,
                Matiere.PorteFeu => 1 / ((4 + 20 * o.Qualite) * (Calendrier.SecondesParHeure / (double)MondeSimule.SecondesParPas)),
                _ => 0,
            };
            if (usure == 0) continue;
            if (o.Usure <= usure) h.Objets.RemoveAt(i);
            else h.Objets[i] = o with { Usure = o.Usure - usure };
        }
        if (h.Savoirs.Count == 0 || h.Objets.Count >= ObjetsMax) return;
        foreach (var s in h.Savoirs)
        {
            if (s.Confiance < 0.5 || s.Effet is Effet.Rien or Effet.Tiedeur or Effet.Chaleur) continue;
            foreach (var m in new[] { s.A, s.B })
            {
                if (m is not (Matiere.Silex or Matiere.Pyrite or Matiere.Obsidienne or Matiere.Calebasse or Matiere.Branche) || h.Porte(m)) continue;
                if (m == Matiere.Branche && s.Effet != Effet.Tubercules) continue;
                if (!Physique.Disponibles(carte, h.X, h.Y, false, VolcanProche(h.X, h.Y)).Contains(m)) continue;
                Prendre(h, new Objet(m, 0.5 + 0.5 * alea.Reel(), 1));
                if (h.Objets.Count >= ObjetsMax) return;
            }
        }
    }

    // ------------------------------------------------------------------ Le feu des incendies, la chasse, l'eau

    /// <summary>
    /// Près d'un incendie, on découvre la chaleur du feu ; un curieux peut en ramasser une branche qui brûle ;
    /// sur une terre qui vient de brûler, on trouve des bêtes grillées par les flammes, tendres et faciles à manger
    /// (les chimpanzés de la savane cherchent la nourriture après les feux de brousse ; Pruetz et Herzog, 2017).
    /// </summary>
    private void ObserverLeFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        if (Catastrophes is null || h.EstBebe(_maintenant) || h.Activite == Activite.Dormir) return;
        var flammes = Catastrophes.BruleAutour(h.X, h.Y);
        if (flammes || Catastrophes.BraisesEn(h.X, h.Y, _maintenant))
        {
            if (h.Faim > 40 && !flammes && alea.Reel() < 0.01)
            {
                // Des bêtes surprises par le feu, cuites sur place : faciles à mâcher (Pruetz et Herzog, 2017).
                h.Faim = Math.Max(0, h.Faim - 25);
                Constater(h, Geste.Approcher, Matiere.Carcasse, Matiere.Feu, new Resultat(Effet.Cuisson, Matiere.Aucune, 1), OrigineSavoir.Observation, tous);
            }
            if (!h.Sait(Effet.Chaleur) && alea.Reel() < (h.Froid > 15 ? 0.3 : 0.05))
                Constater(h, Geste.Approcher, Matiere.Aucune, Matiere.Feu, new Resultat(Effet.Chaleur, Matiere.Aucune, 1), OrigineSavoir.Observation, tous);
            if (!h.EstPetitEnfant(_maintenant) && APortee(h, Matiere.Branche) && !h.Porte(Matiere.Brandon)
                && alea.Reel() < (h.Sait(Effet.Brandon) ? 0.5 : 0.01 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h))))
            {
                Constater(h, Geste.Approcher, Matiere.Branche, Matiere.Feu, new Resultat(Effet.Brandon, Matiere.Brandon, 1),
                    h.Sait(Effet.Brandon) ? OrigineSavoir.Essai : OrigineSavoir.Observation, tous);
            }
        }
    }

    /// <summary>
    /// De la fumée à l'horizon : les plus curieux vont voir, une fois le front passé. Les chimpanzés de Fongoli suivent
    /// calmement les feux de savane et fouillent les terres brûlées (Pruetz et LaDuke, 2010 ; Pruetz et Herzog, 2017) :
    /// c'est ainsi, pense-t-on, que nos ancêtres ont apprivoisé le feu avant de savoir l'allumer.
    /// </summary>
    private bool AllerVoirLeFeu(Humain h)
    {
        if (Catastrophes is null || h.EstEnfant(_maintenant)) return false;
        if (Catastrophes.FumeeEnVue(h.X, h.Y, 3 * RayonPerception(h), _maintenant) is not { } cible) return false;
        if (cible == (h.X, h.Y)) return false;
        var enRoute = h.Activite == Activite.Explorer && Distance(h.CibleX, h.CibleY, cible.X, cible.Y) <= 2;
        if (!enRoute)
        {
            var p = h.Personnalite;
            var connu = h.Sait(Effet.Chaleur) || h.Sait(Effet.Brandon) ? 3 : 1;
            if (alea.Reel() >= 0.02 * (0.2 + p.Ouverture) * (1 - 0.5 * p.Nevrosisme) * connu) return false;
        }
        Avancer(h, cible.X, cible.Y, Activite.Explorer);
        return h.CibleX >= 0;
    }

    /// <summary>
    /// Arme portée pour chasser, et le gain de réussite qu'elle donne : un épieu double presque les prises ; une pointe
    /// de pierre emmanchée, qui perce mieux et plus profond, fait mieux encore (Churchill, 1993 ; Wilkins et al., 2012).
    /// </summary>
    private static (Objet? Arme, double Gain) ArmeDeChasse(Humain h)
    {
        var arme = h.Objets.MaxBy(Lois.Chasse);
        return arme is null || Lois.Chasse(arme) <= 0 ? (null, 0) : (arme, Lois.Chasse(arme));
    }

    /// <summary>Après une prise : découper la bête, la faire cuire, retenir que l'arme a servi. Renvoie le facteur de nourriture obtenue.</summary>
    private double ApresLaChasse(Humain h, Objet? arme, IReadOnlyList<Humain> tous)
    {
        var facteur = 1.0;
        if (arme is not null)
        {
            UserObjet(h, arme, 0.05);
            if (arme.Matiere != Matiere.Assemblage)
                Constater(h, Geste.Frapper, arme.Matiere, Matiere.Carcasse, new Resultat(Effet.Chasse, Matiere.Aucune, 1), OrigineSavoir.Essai, tous);
            else RenforcerAssemblage(h, arme);
        }
        var lame = h.Porte(Matiere.Eclat) ? Matiere.Eclat : h.Porte(Matiere.Obsidienne) ? Matiere.Obsidienne : Matiere.Aucune;
        if (lame != Matiere.Aucune && (h.Sait(Effet.Decoupe) || alea.Reel() < 0.15 * (0.3 + h.Personnalite.Ouverture)))
        {
            if (Faire(h, Geste.Gratter, Matiere.Carcasse, lame, OrigineSavoir.Essai, tous) == Effet.Decoupe) facteur *= 1.25;
        }
        if (FeuIci(h) && (h.Sait(Effet.Cuisson) || alea.Reel() < 0.1 + 0.2 * h.Personnalite.Ouverture))
        {
            // La viande cuite est plus facile à digérer et donne plus d'énergie (Carmody et al., 2011) ; la cuisson tue
            // une partie des germes et des parasites.
            Constater(h, Geste.Approcher, Matiere.Carcasse, Matiere.Feu, new Resultat(Effet.Cuisson, Matiere.Aucune, 1), OrigineSavoir.Essai, tous);
            facteur *= 1.25;
            h.CuitJusqua = _maintenant + 3 * Calendrier.SecondesParJour;
        }
        return facteur;
    }

    /// <summary>Boire à sa calebasse (ou à son outre) quand l'eau est loin.</summary>
    private bool BoireALaCalebasse(Humain h)
    {
        var c = h.Objets.Find(o => o.Matiere is Matiere.Calebasse or Matiere.Peau or Matiere.Substance && o.Plein);
        if (c is null) return false;
        h.Objets[h.Objets.IndexOf(c)] = c with { Plein = false, Usure = c.Usure - 0.01 };
        h.Soif = Math.Max(0, h.Soif - 45);
        h.Activite = Activite.Boire;
        h.CibleX = h.CibleY = -1;
        return true;
    }

    /// <summary>
    /// Après avoir bu : remplir sa calebasse, ou en prendre une, si l'on sait qu'elle garde l'eau ; sinon remplir la peau
    /// qu'il porte, s'il sait qu'une outre la garde.
    /// </summary>
    private void Remplir(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Objets.Exists(o => o.Matiere is Matiere.Calebasse or Matiere.Peau or Matiere.Substance && o.Plein)) return;
        // Un pot de terre cuite se remplit comme une calebasse.
        if (RemplirLePot(h)) return;
        var c = h.Objets.Find(o => o.Matiere == Matiere.Calebasse);
        if (c is null && h.Porte(Matiere.Peau) && h.Savoirs.Exists(s => s.Effet == Effet.EauPortee && s.A == Matiere.Peau && s.Confiance >= 0.4)
            && !APortee(h, Matiere.Calebasse))
        {
            Faire(h, Geste.Remplir, Matiere.Peau, Matiere.Eau, OrigineSavoir.Essai, tous);
            return;
        }
        if (h.Sait(Effet.EauPortee) ? (c is not null || APortee(h, Matiere.Calebasse))
            : c is not null && alea.Reel() < 0.02 * (0.3 + h.Personnalite.Ouverture))
        {
            Faire(h, Geste.Remplir, Matiere.Calebasse, Matiere.Eau, OrigineSavoir.Essai, tous);
        }
    }

    private bool EauProfondeAPortee(int x, int y)
    {
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            if (carte.BiomeEn(nx, ny) is Biome.Lac or Biome.EauPeuProfonde) return true;
        }
        return false;
    }

    /// <summary>
    /// Un bain en eau profonde : on apprend à nager peu à peu ; celui qui ne sait pas encore, fatigué ou transi, peut se noyer.
    /// La noyade reste une cause de mort notable dans les sociétés de chasseurs-cueilleurs.
    /// </summary>
    private void Baignade(Humain h, bool reussi)
    {
        h.Activite = Activite.Nager;
        if (reussi) h.Nage = Math.Min(1, h.Nage + 0.04 + 0.06 * h.Personnalite.Adresse);
        var risque = 0.002 * (1 - h.Nage) * (1 + h.Fatigue / 50 + h.Froid / 50) * (h.EstEnfant(_maintenant) ? 2 : 1);
        if (alea.Reel() < risque) Mourir(h, _maintenant, CauseDeMort.Noyade);
    }

    /// <summary>Peut-il traverser cette eau à la nage ?</summary>
    private bool PeutNager(Humain h) => h.Nage >= 0.4 && h.Fatigue < 70 && h.Froid < 50;

    // ------------------------------------------------------------------ Se soigner

    /// <summary>
    /// Un malade cherche à se soulager : une plante qu'il croit bonne pour son mal ; sinon un proche qui connaît un remède
    /// le lui donne ; sinon, s'il est audacieux ou désespéré, il en essaie une au hasard. Le corps décide du reste :
    /// la plante agit selon ses vertus réelles, et l'humain en tire une croyance, juste ou fausse.
    /// </summary>
    private bool SeSoigner(Humain h, IReadOnlyList<Humain> tous)
    {
        var malade = h.EstMalade(_maintenant);
        var fievre = h.InfectionId != 0 && h.SymptomesLe <= _maintenant && h.GuerisonLe > _maintenant;
        if (!malade && !fievre || h.EstBebe(_maintenant)) return false;
        if (_maintenant - h.RemedeLe < 12 * Calendrier.SecondesParHeure) return false;
        var mal = malade ? Physique.SymptomeDe(h.Maladie) : Symptome.Fievre;
        var especes = Flore.Sur(carte.BiomeEn(h.X, h.Y));
        if (especes.Count == 0) return false;
        bool Ici(int numero) => especes.Any(e => e.Numero == numero);

        // Ce qu'il croit bon pour son mal.
        var remede = h.Savoirs.Where(s => s.Effet == Effet.Soulagement && s.Symptome == mal && s.Confiance >= 0.45 && Ici(s.Espece))
            .MaxBy(s => s.Confiance);
        if (remede is not null)
        {
            PrendreRemede(h, remede.Espece, remede.Geste, mal, 0, tous, OrigineSavoir.Essai);
            return true;
        }
        // Le guérisseur, le chaman, ou celui qui se dit tel, s'il est tout près.
        if (AppelerGuerisseur(h, mal, tous)) return true;
        // Un proche le soigne : il lui donne la plante qu'il croit bonne.
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.EstEnfant(_maintenant) || o.Activite == Activite.Dormir) continue;
            var s = o.Savoirs.Where(x => x.Effet == Effet.Soulagement && x.Symptome == mal && x.Confiance >= 0.55 && Ici(x.Espece)).MaxBy(x => x.Confiance);
            if (s is null || o.AffectionPour(h.Id) < -10) continue;
            var envie = (0.3 + 0.7 * o.Personnalite.Moral.Compassion) * (1 - Psyche.Froideur(o.Personnalite));
            if (alea.Reel() >= envie) continue;
            Tour.Changer(o, x => x.Activite = Activite.Soigner);
            var plante = Flore.Get(s.Espece);
            Noter(_maintenant, TypeEvenement.Entraide, o, h,
                new Recit("ing.remede", ("o", o), ("h", h), ("plante", plante.Dit), ("mal", Physique.Mal(mal))), karma: Regard.Remede);
            MarquerSoin(o, h);
            Medecine.Placebo(h, o, false);
            PrendreRemede(h, s.Espece, s.Geste, mal, o.Id, tous, OrigineSavoir.Imitation);
            return true;
        }
        // Essayer une plante au hasard : l'audace, la souffrance et le désespoir y poussent. Un enfant malade, lui, attend
        // qu'on le soigne.
        if (h.EstEnfant(_maintenant)) return false;
        var detresse = Math.Clamp((100 - h.Sante) / 60 + (malade ? Maladies.Fiche(h.Maladie).Gravite / 3 : 0.3), 0, 1.5);
        if (alea.Reel() >= 0.04 * (0.3 + h.Personnalite.Ouverture) * detresse) return false;
        var essai = especes[alea.Entier(especes.Count)];
        if (h.Savoirs.Exists(s => s.Espece == essai.Numero && (s.Effet == Effet.Poison || s.Symptome == mal && s.Confiance < 0.3))) return false;
        var geste = mal is Symptome.Plaie or Symptome.Peau && alea.Reel() < 0.6 ? Geste.Appliquer : Geste.Manger;
        PrendreRemede(h, essai.Numero, geste, mal, 0, tous, OrigineSavoir.Essai);
        return true;
    }

    /// <summary>Prend une plante contre un mal : ses vertus réelles agissent, la croyance viendra à la guérison.</summary>
    private void PrendreRemede(Humain h, int numero, Geste geste, Symptome mal, int soigneurId, IReadOnlyList<Humain> tous, OrigineSavoir origine)
    {
        h.Activite = Activite.Soigner;
        h.CibleX = h.CibleY = -1;
        var espece = Flore.Get(numero);
        var effet = Gouter(h, espece, geste, tous, origine);
        if (!h.EstVivant || effet == Effet.Poison) return;
        h.RemedeEspece = numero;
        h.RemedeGeste = geste;
        h.RemedeSymptome = mal;
        h.RemedeLe = _maintenant;
        var malade = h.Id;
        Tour.Differer(() => RemedeSoigneur[malade] = soigneurId);
    }

    /// <summary>Qui a donné son dernier remède à un malade (pour le crédit ou le doute qui suivra).</summary>
    public Dictionary<int, int> RemedeSoigneur { get; } = [];

    /// <summary>
    /// Effets réels d'une plante mangée ou appliquée : poison, visions, fièvre qui tombe, maladie abrégée. Renvoie l'effet
    /// que l'humain remarque tout de suite (poison, visions), sinon Rien.
    /// </summary>
    private Effet Gouter(Humain h, Espece espece, Geste geste, IReadOnlyList<Humain> tous, OrigineSavoir origine)
    {
        h.Activite = h.Activite == Activite.Soigner ? Activite.Soigner : Activite.Experimenter;
        if (geste == Geste.Manger && espece.Toxicite > 0)
        {
            // La plupart des plantes toxiques sont amères : on recrache souvent la première bouchée (le rejet de l'amer
            // protège des poisons : Glendinning, 1994). La dose fait le poison : avalée, elle rend malade, rarement elle tue.
            // Les enfants sentent l'amer plus fort que les adultes et le rejettent davantage (Mennella et al., 2005).
            var recrachee = alea.Reel() < 0.4 + 0.4 * espece.Toxicite + (h.EstEnfant(_maintenant) ? 0.15 : 0);
            var degats = 45 * espece.Toxicite * (0.4 + alea.Reel()) * (h.EstEnfant(_maintenant) ? 1.6 : 1) * (recrachee ? 0.1 : 1);
            // Bouillie au feu, l'eau jetée, une plante perd une part de ses poisons solubles (Johns et Kubo, 1988).
            if (AvecLaMatiere && geste == Geste.Manger && SavoirDeLaCuisine(h) is not null && FeuIci(h)) degats *= 0.3 + 0.4 * alea.Reel();
            h.Sante -= degats;
            h.Soif = Math.Min(100, h.Soif + (recrachee ? 2 : 15));
            h.Humeur = Math.Max(0, h.Humeur - (recrachee ? 2 : 10));
            var id = Noter(_maintenant, TypeEvenement.Maladie, h, null, recrachee
                ? new Recit("ing.recrache", ("h", h), ("plante", espece.Dit))
                : new Recit("ing.vomit", ("h", h), ("plante", espece.Dit)));
            if (h.Sante <= 0)
            {
                Mourir(h, _maintenant, CauseDeMort.Empoisonnement, id);
                // Ceux qui l'ont vu mourir retiennent que la plante tue.
                foreach (var o in tous.Vivants())
                {
                    if (o.Id == h.Id || !o.EstVivant || Distance(o.X, o.Y, h.X, h.Y) > 1 || o.EstBebe(_maintenant)) continue;
                    Retenir(o, Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Poison, espece.Numero, Symptome.Aucun, OrigineSavoir.Observation, h.Id, tous);
                }
                return Effet.Poison;
            }
            Retenir(h, Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Poison, espece.Numero, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
            Montrer(h, Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Poison, espece.Numero, Symptome.Aucun, tous, id);
            return Effet.Poison;
        }
        if (geste == Geste.Manger && espece.Psychotrope > 0 && alea.Reel() < espece.Psychotrope)
        {
            var id = Noter(_maintenant, TypeEvenement.Decouverte, h, null,
                new Recit("ing.visions", ("h", h), ("plante", espece.Dit)));
            Jardin.Confier(h, TypeIntime.Croyance, 0, Recits.Recit.Seul("js.visions"),
                40 + 40 * h.Personnalite.Transcendance, id, _maintenant, $"visions:{espece.Numero}");
            Croyances.Ajouter(h, 8 * (0.5 + h.Personnalite.Transcendance), new Recit("cro.visions", ("plante", espece.Dit)), id, _maintenant, signe: true);
            Retenir(h, Geste.Manger, Matiere.Plante, Matiere.Aucune, Effet.Visions, espece.Numero, Symptome.Aucun, origine, 0, tous);
        }
        // Les vertus agissent sur la maladie réelle, que l'humain le sache ou non.
        if (h.EstMalade(_maintenant))
        {
            var e = espece.Efficacite(h.Maladie, geste);
            if (e > 0)
            {
                var reste = h.MaladeJusqua - _maintenant;
                h.MaladeJusqua = _maintenant + (long)(reste * (1 - 0.6 * e));
            }
        }
        if (espece.Febrifuge > 0 && geste == Geste.Manger && (h.EstMalade(_maintenant) || h.InfectionId != 0))
        {
            // La fièvre tombe, la douleur s'apaise : on se sent mieux, sans être guéri.
            h.Humeur = Math.Min(100, h.Humeur + 8 * espece.Febrifuge);
            h.Sante = Math.Min(100, h.Sante + 2 * espece.Febrifuge);
        }
        return Effet.Rien;
    }

    /// <summary>
    /// Chaque jour : celui qui a guéri après une plante croit qu'elle l'a guéri (et celui qui la lui a donnée y croit plus) ;
    /// les morts pendant un remède font douter ceux qui le leur avaient donné.
    /// </summary>
    public void BilanDesRemedes(IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant || h.RemedeEspece == 0) continue;
            var encore = h.EstMalade(maintenant) || h.InfectionId != 0 && h.GuerisonLe > maintenant;
            if (encore && maintenant - h.RemedeLe < 30 * Calendrier.SecondesParJour) continue;
            var soigneur = RemedeSoigneur.GetValueOrDefault(h.Id);
            if (!encore)
            {
                var origine = soigneur > 0 ? OrigineSavoir.Imitation : OrigineSavoir.Essai;
                Retenir(h, h.RemedeGeste, Matiere.Plante, Matiere.Aucune, Effet.Soulagement, h.RemedeEspece, h.RemedeSymptome, origine, soigneur, tous);
                if (soigneur > 0 && soigneur <= tous.Count && tous[soigneur - 1].EstVivant)
                {
                    var s = tous[soigneur - 1];
                    var i = s.Savoirs.FindIndex(x => x.Effet == Effet.Soulagement && x.Espece == h.RemedeEspece && x.Symptome == h.RemedeSymptome);
                    if (i >= 0) s.Savoirs[i] = s.Savoirs[i] with { Essais = s.Savoirs[i].Essais + 1, Succes = s.Savoirs[i].Succes + 1, UtiliseLe = maintenant };
                    VieSociale.Gratitude(h, s, 3);
                }
            }
            h.RemedeEspece = 0;
            RemedeSoigneur.Remove(h.Id);
        }
        foreach (var (espece, mal, soigneurId) in _remedesEchoues)
        {
            if (soigneurId <= 0 || soigneurId > tous.Count) continue;
            var s = tous[soigneurId - 1];
            var i = s.Savoirs.FindIndex(x => x.Effet == Effet.Soulagement && x.Espece == espece && x.Symptome == mal);
            if (i >= 0) s.Savoirs[i] = s.Savoirs[i] with { Essais = s.Savoirs[i].Essais + 1 };
        }
        _remedesEchoues.Clear();
    }

    /// <summary>Mort pendant un remède : noté pour le doute du soigneur.</summary>
    private void RemedeEchoue(Humain h)
    {
        if (h.RemedeEspece == 0) return;
        var (espece, mal, malade) = (h.RemedeEspece, h.RemedeSymptome, h.Id);
        Tour.Differer(() =>
        {
            _remedesEchoues.Add((espece, mal, RemedeSoigneur.GetValueOrDefault(malade, malade)));
            RemedeSoigneur.Remove(malade);
        });
    }

    // ------------------------------------------------------------------ Usure, oubli

    /// <summary>
    /// Chaque jour : les vêtements s'usent (les feuilles en quelques jours, une peau en un an environ), et les savoirs
    /// qu'on n'emploie plus s'oublient, d'autant moins vite qu'on les a souvent employés (courbe d'Ebbinghaus).
    /// Renvoie les savoirs oubliés, pour le journal.
    /// </summary>
    public List<(Humain Humain, Savoir Savoir)> UserEtOublier(IReadOnlyList<Humain> tous, long maintenant)
    {
        var oublies = new List<(Humain, Savoir)>();
        foreach (var h in tous.Vivants())
        {
            if (!h.EstVivant) continue;
            if (h.Vetement is { } v)
            {
                var usure = v.Matiere == Matiere.Peau ? 1 / 365.0 : 1 / 8.0;
                h.Vetement = v.Usure - usure <= 0 ? null : v with { Usure = v.Usure - usure };
            }
            if (h.Savoirs.Count == 0) continue;
            var facteur = Memoire.Facteur(h.Personnalite.Intelligence.Memoire);
            for (var i = h.Savoirs.Count - 1; i >= 0; i--)
            {
                var s = h.Savoirs[i];
                var stabilite = 120.0 * facteur * Math.Pow(1 + s.Succes, 1.3) * Calendrier.SecondesParJour;
                // Un poison, une plante qui a fait voir d'autres mondes, la morsure chaude du feu ne s'oublient guère.
                if (s.Effet is Effet.Poison or Effet.Visions or Effet.Chaleur) stabilite *= 10;
                // Ce qui a un nom se retient mieux : le mot rappelle la chose.
                if (Langues.Indice(h, s.Espece > 0 ? Idees.Plante(s.Espece) : Idees.Savoir(s.Effet)) >= 0) stabilite *= 1.5;
                // Le geste qui a sauvé une plaie (le sang, la douleur) reste en mémoire, même si les blessures sont rares.
                else if (s.Effet == Effet.Pansement) stabilite *= 5;
                if (maintenant - s.UtiliseLe < 1.4 * stabilite) continue;
                h.Savoirs.RemoveAt(i);
                oublies.Add((h, s));
            }
        }
        return oublies;
    }

    /// <summary>L'Entité souffle une intuition : l'humain fait aussitôt le geste suggéré (rien ne garantit qu'il réussisse).</summary>
    public Effet Souffler(Humain h, Geste geste, Matiere a, Matiere b, IReadOnlyList<Humain> tous, long maintenant, int espece = 0)
    {
        var debut = Journal.Count;
        try
        {
            return geste is Geste.Manger or Geste.Appliquer ? SoufflePlante(h, geste, espece, tous, maintenant) : Souffle(h, geste, a, b, tous, maintenant);
        }
        finally
        {
            // Ce que l'humain a vécu de cette intuition est marqué au journal comme venu de l'Entité (ce que d'autres
            // en ont appris en le regardant reste de leur fait).
            for (var i = debut; i < Journal.Count; i++)
            {
                if (Journal[i].HumainId == h.Id) Journal[i] = Journal[i] with { Origine = Origine.Entite };
            }
        }
    }

    /// <summary>
    /// L'Entité souffle l'idée d'une plante : l'humain la goûte, ou s'en soigne s'il souffre. Ses vertus réelles agissent ;
    /// la croyance viendra, ou non, de ce qu'il ressentira.
    /// </summary>
    private Effet SoufflePlante(Humain h, Geste geste, int numero, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _temperature = Temperature(h.X, h.Y, maintenant);
        var espece = Flore.Sur(carte.BiomeEn(h.X, h.Y)).FirstOrDefault(e => e.Numero == numero);
        if (espece is null)
        {
            Noter(maintenant, TypeEvenement.Decouverte, h, null,
                new Recit("ing.souffle.absente", ("h", h)));
            return Effet.Rien;
        }
        var malade = h.EstMalade(maintenant) || h.InfectionId != 0 && h.SymptomesLe <= maintenant && h.GuerisonLe > maintenant;
        Noter(maintenant, TypeEvenement.Decouverte, h, null, geste == Geste.Manger
            ? new Recit(malade ? "ing.souffle.manger.malade" : "ing.souffle.manger", ("h", h), ("plante", espece.Dit))
            : new Recit(malade ? "ing.souffle.appliquer.malade" : "ing.souffle.appliquer", ("h", h), ("plante", espece.Dit)));
        if (malade)
        {
            var mal = h.EstMalade(maintenant) ? Physique.SymptomeDe(h.Maladie) : Symptome.Fievre;
            PrendreRemede(h, espece.Numero, geste, mal, 0, tous, OrigineSavoir.Entite);
            return h.Savoirs.Exists(s => s.Espece == espece.Numero && s.Effet == Effet.Poison) ? Effet.Poison : Effet.Rien;
        }
        h.Activite = Activite.Experimenter;
        h.CibleX = h.CibleY = -1;
        return Gouter(h, espece, geste, tous, OrigineSavoir.Entite);
    }

    private Effet Souffle(Humain h, Geste geste, Matiere a, Matiere b, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _temperature = Temperature(h.X, h.Y, maintenant);
        if (!APortee(h, a) || !APortee(h, b))
        {
            var manque = APortee(h, a) ? b : a;
            Noter(maintenant, TypeEvenement.Decouverte, h, null,
                new Recit("ing.souffle.manque", ("h", h), ("geste", Physique.Phrase(geste, a, b)), ("manque", Physique.Nom(manque))));
            return Effet.Rien;
        }
        var r = Physique.Tenter(geste, a, b, Contexte(h), alea);
        if (r.Effet == Effet.Rien)
        {
            Noter(maintenant, TypeEvenement.Decouverte, h, null,
                new Recit("ing.souffle.rien", ("h", h), ("geste", Physique.Phrase(geste, a, b))));
            return Effet.Rien;
        }
        Constater(h, geste, a, b, r, OrigineSavoir.Entite, tous);
        return r.Effet;
    }
}
