using System.Globalization;
using Monde.Simulation.Chimie;
using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// La matière et la chaleur (mondes de la génération 2, depuis la 0.32.0). Les humains pétrissent, mêlent, frappent, laissent
/// les choses au feu ; ce qu'elles deviennent sort des lois de la matière (<see cref="Reactions"/>, <see cref="Thermique"/>), et
/// l'humain n'en retient que ce qu'il voit changer (<see cref="Perception"/>). Rien ici ne nomme le pot, la chaux ni le charbon :
/// un savoir dit « cette chose, ainsi chauffée, durcit et ne se défait plus dans l'eau », et celui qui le sait peut le refaire.
/// <para>
/// Repères : les plus anciennes terres cuites sont des figurines (Dolní Věstonice, 26 000 ans), les plus anciens pots ont
/// 20 000 ans (Xianrendong : Wu et al., 2012), cuits en meule ou en fosse vers 600-850 °C ; la colle de brai d'écorce de bouleau
/// a au moins 200 000 ans (Mazza et al., 2006) ; le silex chauffé pour mieux le tailler, 70 000 à 160 000 ans (Brown et al.,
/// 2009) ; la chaux, environ 12 000 ans (Kingery et al., 1988) ; le cuivre natif martelé puis recuit, 10 000 ans (Çayönü).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Génération du monde (voir <see cref="ConfigurationMonde.Generation"/>) : la matière et la chaleur ne vivent qu'à partir de 2.</summary>
    public int Generation { get; set; }

    /// <summary>La matière a sa chimie dans ce monde.</summary>
    private bool AvecLaMatiere => Generation >= 2;

    /// <summary>Gestes de la matière tirés au jeu, après les treize gestes de toujours.</summary>
    private static readonly Geste[] GestesDeLaMatiere = [Geste.Modeler, Geste.Cuire, Geste.Melanger, Geste.Souffler, Geste.Etouffer, Geste.Plonger];

    private IEnumerable<Matiere> Disponibles(Humain h) => Physique.Disponibles(carte, h.X, h.Y, Sec(h) > 0.55, VolcanProche(h.X, h.Y), Generation);

    /// <summary>De l'eau puisée, pour mêler ou pour mouiller.</summary>
    private static readonly Substance EauPuisee = Substance.De(Structure.Grains, 0, 0.5, (Phase.Eau, 1));

    // ------------------------------------------------------------------ Ce que sont les choses

    /// <summary>Un objet porté qui a sa composition (pâte, terre cuite, minerai…) : la composition, séchée selon le temps passé.</summary>
    private Substance? SubstanceDe(Humain h, Objet o)
    {
        if (Substance.Lire(o.Pieces) is not { } s) return null;
        if (s.Structure == Structure.Pate && s.FaconneLe > 0)
            s = Reactions.Secher(s, (_maintenant - s.FaconneLe) / (double)Calendrier.SecondesParJour, Sec(h));
        // Une pâte de chaux qu'on garde en boule durcit lentement à l'air.
        if (s.FaconneLe > 0 && s.Part(Phase.Portlandite) > 0 && s.Structure is Structure.Roche)
            s = Reactions.Carbonater(s, (_maintenant - s.FaconneLe) / (double)Calendrier.SecondesParJour, 3);
        return s;
    }

    /// <summary>
    /// La matière <paramref name="m"/> que l'humain a sous la main : l'objet qu'il porte s'il a sa composition, sinon ce que le lieu
    /// offre (l'argile de la berge, le calcaire de la falaise, le morceau d'un gisement qui affleure). <paramref name="objet"/> :
    /// rang de l'objet porté (−1 : pris sur place).
    /// </summary>
    private Substance? SubstanceDe(Humain h, Matiere m, out int objet)
    {
        objet = -1;
        for (var i = 0; i < h.Objets.Count; i++)
        {
            if (h.Objets[i].Matiere != m || SubstanceDe(h, h.Objets[i]) is not { } s) continue;
            objet = i;
            return s;
        }
        if (m == Matiere.Eau) return EauDouceAPortee(h.X, h.Y) ? EauPuisee : null;
        if (m == Matiere.Minerai)
            return carte.Terrain.GisementA(h.X, h.Y) is { } g && APorteeDeMain(h, g) ? Matieres.Minerai(g.Type) : null;
        if (m == Matiere.Substance) return null;
        if (!h.Porte(m) && !Disponibles(h).Contains(m)) return null;
        return Matieres.De(m, carte, h.X, h.Y);
    }

    /// <summary>Le nom de la matière telle qu'on la voit (l'objet porté s'il a sa composition, sinon son nom de toujours).</summary>
    private static Recit NomDe(Matiere m, Substance? s) => m is Matiere.Substance or Matiere.Minerai && s is not null ? Perception.Nom(s) : Physique.Nom(m);

    /// <summary>Un récipient fait de main d'homme qui garde l'eau (un pot de terre cuite).</summary>
    private static bool PotEtanche(Objet o) => o.Matiere == Matiere.Substance && Substance.Lire(o.Pieces) is { TientLEau: true };

    /// <summary>Un objet qui porte l'eau : calebasse, outre de peau, pot.</summary>
    private static bool Recipient(Objet o) => o.Matiere is Matiere.Calebasse or Matiere.Peau || PotEtanche(o);

    // ------------------------------------------------------------------ La chaleur du feu ici

    /// <summary>
    /// Comment on met une chose au feu : 0 jetée dans les flammes, 1 approchée peu à peu puis mise dedans, 2 enfouie sous la braise,
    /// 3 en fosse, sous un tas de bois qu'on allume (la cuisson des premiers potiers), 4 dans un creux de terre cuite posé dans
    /// le charbon ardent (un creuset).
    /// </summary>
    private const int ModesDeCuisson = 5;

    /// <summary>Mettre au feu dans un creuset : un creux de terre cuite enfoncé dans la braise de charbon.</summary>
    private const int Creuset = 4;

    /// <summary>La chauffe qu'une chose reçoit ici, de cette façon (null : pas de vrai feu sur la case).</summary>
    private Chauffe? ChauffeIci(Humain h, int mode, bool souffle)
    {
        var foyer = Installations.FoyerEn(h.X, h.Y);
        var braise = foyer is null ? Installations.BraiseEn(h.X, h.Y) : null;
        if (foyer is null && braise is null) return null;
        var forme = mode == 3 ? FormeFoyer.Fosse : mode == Creuset ? FormeFoyer.Enceinte : foyer is { Degage: true } ? FormeFoyer.Pierres : FormeFoyer.Ouvert;
        // Le bois mort ramassé tient 12 à 25 % d'eau selon la saison et la pluie ; une braise seule chauffe moins longtemps.
        var humidite = Math.Clamp(0.12 + 0.3 * (1 - Sec(h)), 0.1, 0.5);
        var vent = 4 * (Catastrophes?.VentEn(h.X, h.Y, _maintenant) ?? 1);
        // Le creuset s'enfonce dans un tas de charbon de bois : celui du foyer, ou celui qu'on y verse.
        var charbon = mode == Creuset ? 1 : foyer?.Charbon ?? 0;
        var t = Thermique.Temperature(forme, humidite, charbon, vent, souffle, _temperature, foyer?.Heures ?? 0.5);
        if (foyer is null) t *= 0.85;
        // Une petite heure à deux heures dans les flammes ; des heures sous la braise ou dans la fosse, selon la patience.
        var patience = 0.3 + 0.7 * h.Personnalite.Conscience;
        return mode switch
        {
            0 => new Chauffe(t * 0.95, 0.4 + 1.6 * patience * alea.Reel(), 15, Atmosphere.Oxydante),
            1 => new Chauffe(t * 0.95, 0.5 + 1.5 * patience * alea.Reel(), 4, Atmosphere.Oxydante),
            2 => new Chauffe(t * 0.5, 1.5 + 5 * patience * alea.Reel(), 2, Atmosphere.Reductrice),
            // Dans le creuset, sous le charbon, l'air manque : ce qu'il contient chauffe presque autant que la braise, sans air.
            Creuset => new Chauffe(t * 0.95, 0.3 + 1.2 * patience * alea.Reel(), 10, Atmosphere.Reductrice),
            _ => new Chauffe(t * 0.92, 1.5 + 3.5 * patience * alea.Reel(), 5, Atmosphere.Reductrice),
        };
    }

    private bool SaitSouffler(Humain h) => h.Savoirs.Exists(s => s.Effet == Effet.Transformation && s.Geste == Geste.Souffler && s.Confiance >= 0.4);

    // ------------------------------------------------------------------ Faire

    /// <summary>
    /// Un geste sur la matière, jugé par ses lois. <paramref name="mode"/> : façon de cuire (−1 : il en choisit une). Renvoie
    /// l'effet constaté (Transformation, Modelage, Eclat), ou Rien.
    /// </summary>
    internal Effet FaireMatiere(Humain h, Geste g, Matiere a, Matiere b, OrigineSavoir origine, IReadOnlyList<Humain> tous, Humain? maitre = null,
        int mode = -1, int objet = -1)
    {
        var ia = objet;
        var sa = objet >= 0 && objet < h.Objets.Count ? SubstanceDe(h, h.Objets[objet]) : SubstanceDe(h, a, out ia);
        switch (g)
        {
            case Geste.Modeler:
            {
                if (sa is not { Plastique: true }) return Rien(h, g, a, b);
                // Ce qu'on veut en faire : un creux pour porter l'eau quand on en manque, sinon ce que les mains inventent.
                var creux = mode == 1 || mode < 0 && (h.Soif > 30 && !h.Objets.Exists(Recipient) || alea.Reel() < 0.5);
                var masse = creux ? 0.8 + 1.7 * alea.Reel() : 0.1 + 0.3 * alea.Reel();
                var faconne = sa with { Forme = creux ? Forme.Creux | Forme.Rond : Forme.Aucune, Masse = masse, FaconneLe = _maintenant };
                var qualite = Math.Clamp(0.25 + 0.6 * AdressePour(h, null) * alea.Reel() + 0.15 * Humains.Arts.Aptitude(h), 0, 1);
                var pieces = creux ? "M|1" : "M|0";
                // La chose modelée est là, qu'il y voie ou non une trouvaille.
                if (ia >= 0) h.Objets.RemoveAt(ia);
                Prendre(h, new Objet(Matiere.Substance, qualite, 1, Pieces: faconne.Arrondie.Signature));
                if (!Remarquer(h, origine, g, a, b, Effet.Modelage, pieces, creux && h.Soif > 30)) return Effet.Rien;
                var recit = new Recit(creux ? "chim.modele.creux" : "chim.modele.forme", ("h", h), ("a", NomDe(a, sa)));
                var ev = Retenir(h, g, a, b, Effet.Modelage, 0, Symptome.Aucun, origine, maitre?.Id ?? 0, tous, pieces: pieces, recit: recit);
                if (origine != OrigineSavoir.Imitation) Montrer(h, g, a, b, Effet.Modelage, 0, Symptome.Aucun, tous, ev, pieces);
                return Effet.Modelage;
            }
            case Geste.Cuire or Geste.Approcher:
            {
                if (sa is null) return Rien(h, g, a, b);
                if (g == Geste.Cuire && mode < 0) mode = ChoisirMode(h);
                // Dans le creuset, on met ce qui n'est ni le creux ni le charbon.
                if (mode == Creuset && objet < 0 && a == Matiere.Substance && (sa is { Structure: Structure.Cuite } && (sa.Forme & Forme.Creux) != 0 || sa.BrulesSansFumee))
                {
                    ia = h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { } x
                        && !(x.Structure == Structure.Cuite && (x.Forme & Forme.Creux) != 0) && !x.BrulesSansFumee);
                    if (ia < 0) return Rien(h, g, a, b);
                    sa = SubstanceDe(h, h.Objets[ia])!;
                }
                // Approchée du feu, une chose chauffe au bord, un moment ; mise au feu, elle y reste. Qui sait que souffler attise le
                // feu le fait, sauf pour ce qu'on enfouit sous la braise.
                Chauffe? c;
                if (g == Geste.Approcher) c = ChauffeIci(h, 0, false) is { } x ? x with { Temperature = x.Temperature * 0.55, Heures = 0.3, Montee = 8 } : null;
                else if (mode == Creuset && !MettreAuCreuset(h, ref ia)) c = null;
                // Au creuset, qui sait que souffler attise le feu souffle sans relâche (à la bouche, par un roseau).
                else c = ChauffeIci(h, mode, mode != 2 && SaitSouffler(h) && (mode == Creuset || alea.Reel() < 0.5));
                if (c is not { } chauffe) return Rien(h, g, a, b);
                var t = Reactions.Chauffer(sa, chauffe, alea);
                return Constater(h, g, a, b, g == Geste.Approcher ? 0 : mode, sa, ia, t, origine, tous, maitre);
            }
            case Geste.Etouffer:
            {
                // Couvrir de terre un feu bien nourri : le bois couve des heures sans air et se change en charbon (meule ou fosse
                // charbonnière : 25 à 35 % de la masse du bois). Le feu est perdu.
                if (Installations.FoyerEn(h.X, h.Y) is not { } foyer || sa is null || !Physique.EstBois(a) && a != Matiere.Tronc) return Rien(h, g, a, b);
                var temperature = Thermique.Temperature(FormeFoyer.Couvert, Math.Clamp(0.12 + 0.3 * (1 - Sec(h)), 0.1, 0.5), foyer.Charbon, 0, false,
                    _temperature, foyer.Heures);
                var charge = sa with { Masse = 4 + 6 * alea.Reel() };
                var t = Reactions.Chauffer(charge, new Chauffe(temperature, 6 + 6 * alea.Reel(), 2, Atmosphere.Reductrice), alea);
                var (fx, fy) = (h.X, h.Y);
                Tour.Differer(() => Installations.Eteindre(fx, fy));
                return Constater(h, g, a, Matiere.Feu, 0, charge, -1, t, origine, tous, maitre);
            }
            case Geste.Melanger:
            {
                var sb = SubstanceDe(h, b, out var ib);
                // Deux choses portées de la même sorte (une poudre et du charbon) : la seconde est une autre que la première.
                if (a == b && a == Matiere.Substance && ia >= 0)
                {
                    ib = h.Objets.FindIndex(ia + 1, o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is not null);
                    sb = ib >= 0 ? SubstanceDe(h, h.Objets[ib]) : null;
                }
                if (sa is null || sb is null || a == b && ia == ib) return Rien(h, g, a, b);
                // On ne mêle que ce qui se divise : poudre, pâte, terre, eau, et pour lier la terre de l'herbe ou des fibres
                // (deux pierres, une branche et un galet ne se mêlent pas).
                if (!SeMele(sa, a) || !SeMele(sb, b) || !Divise(sa) && !Divise(sb)) return Rien(h, g, a, b);
                // On mêle à peu près autant de l'un que de l'autre, ou un peu d'eau à beaucoup de terre.
                var pa = sa with { Masse = b == Matiere.Eau ? 2 : 1.5 };
                var pb = sb with { Masse = b == Matiere.Eau ? 0.3 + 0.4 * alea.Reel() : 0.4 + 0.8 * alea.Reel() };
                var t = Reactions.Melanger(pa, pb, _maintenant);
                // L'objet porté qu'on a mêlé n'est plus.
                if (ib >= 0 && ib != ia) h.Objets.RemoveAt(ib);
                if (ia > ib && ib >= 0) ia--;
                var effet = Constater(h, g, a, b, 0, pa, ia, t, origine, tous, maitre);
                // La pierre blanche qui chauffe dans l'eau ronge les mains de qui la remue sans s'en garder.
                if (t.Exotherme) BrulerALaChaux(h, SavoirDe(h, Geste.Melanger, "chauffe") is not null ? 0.03 : 0.12);
                return effet;
            }
            case Geste.Frapper:
            {
                if (sa is null) return Effet.Rien;
                var marteau = SubstanceDe(h, b, out _)?.Durete ?? 0;
                if (marteau < 0.4) return Effet.Rien;
                // Une pierre à cassure lisse (silex, obsidienne) donne des éclats ; chauffée lentement, elle en donne de meilleurs.
                var conchoidale = sa.Part(Phase.Calcedoine) + sa.Part(Phase.CalcedoineChauffee) + sa.Part(Phase.VerreVolcanique) >= 0.8 && sa.Fissures < 0.5;
                if (conchoidale)
                {
                    var chauffee = sa.Part(Phase.CalcedoineChauffee) > 0.5;
                    var adresse = AdressePour(h, null);
                    if (alea.Reel() >= 0.3 + 0.45 * adresse + (chauffee ? 0.1 : 0)) return Effet.Rien;
                    var qualite = Math.Clamp(0.5 + 0.4 * adresse * alea.Reel() + (chauffee ? 0.15 : 0), 0, 1);
                    Constater(h, g, a, b, new Resultat(Effet.Eclat, Matiere.Eclat, qualite), origine, tous, maitre);
                    return Effet.Eclat;
                }
                var t = Reactions.Marteler(sa, marteau, AdressePour(h, null), alea);
                return Constater(h, g, a, b, 0, sa, ia, t, origine, tous, maitre);
            }
            case Geste.Souffler:
            {
                // Souffler sur les braises : le feu reprend, rougit, chauffe plus fort.
                if (ChauffeIci(h, 0, false) is not { } avant || ChauffeIci(h, 0, true) is not { } apres) return Rien(h, g, a, b);
                if (Thermique.Incandescence(apres.Temperature) <= Thermique.Incandescence(avant.Temperature) && alea.Reel() < 0.5) return Rien(h, g, a, b);
                return Comprendre(h, g, Matiere.Feu, Matiere.Aucune, 0, ["attise"], null, origine, tous, maitre, new Recit("chim.essai.Souffler", ("h", h)));
            }
            case Geste.Plonger:
            {
                // Une pierre chauffée jetée dans l'eau d'une outre, d'un pot ou d'un creux : l'eau tiédit, ou bout s'il y a assez de pierres.
                if (sa is null || sa.Structure != Structure.Roche || ChauffeIci(h, 1, false) is not { } c || !EauDouceAPortee(h.X, h.Y)) return Rien(h, g, a, b);
                var pierres = 1 + alea.Entier(6);
                var eau = Reactions.Plonger(sa with { Masse = sa.Masse * pierres }, Math.Min(c.Temperature, 700), 3, _temperature);
                // Qui a faim y met les racines qu'il vient d'arracher, dans une outre, une calebasse ou un creux garni de peau.
                if (eau >= 99.9 && h.Faim > 20 && origine == OrigineSavoir.Essai && PeutBouillir(h, true) && alea.Reel() < 0.3 + 0.4 * h.Personnalite.Ouverture)
                    return Bouillir(h, true, origine, tous, maitre);
                var l = new List<string> { eau >= 99.9 ? "bout" : "tiede" };
                if (sa.Part(Phase.Quartz) + sa.Part(Phase.Calcedoine) >= 0.5 && alea.Reel() < 0.5) l.Add("fend");
                return Comprendre(h, g, a, Matiere.Eau, 0, l, null, origine, tous, maitre, new Recit("chim.essai.Plonger", ("h", h), ("a", NomDe(a, sa))));
            }
        }
        return Effet.Rien;
    }

    /// <summary>
    /// Au jeu, un geste de la matière : pétrir la terre mouillée, laisser une chose au feu, mêler deux matières, souffler sur les
    /// braises, couvrir de terre un feu nourri, jeter une pierre chaude dans l'eau. Faux si le geste n'a pas de sens ici (pas de
    /// feu, pas d'argile).
    /// </summary>
    private bool JouerAvecLaMatiere(Humain h, Geste geste, Matiere a, Matiere b, List<Matiere> materiaux, IReadOnlyList<Humain> tous)
    {
        var feu = Installations.FoyerEn(h.X, h.Y) is not null || Installations.BraiseEn(h.X, h.Y) is not null;
        switch (geste)
        {
            case Geste.Modeler:
                // Une pâte blanche à la main, devant la hutte : on l'étale sur les parois, pour voir.
                if (Installations.HutteEn(h.X, h.Y) is not null && Porte(h, PateDeChaux) is >= 0 and var blanche && alea.Reel() < 0.5)
                    return Enduire(h, blanche);
                // On pétrit ce qu'on a de mou sous la main : une pâte qu'on porte, ou la terre de la berge.
                var pate = h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { Plastique: true });
                if (pate < 0 && !materiaux.Contains(Matiere.Argile)) return false;
                h.Activite = Activite.Experimenter;
                h.CibleX = h.CibleY = -1;
                FaireMatiere(h, geste, pate >= 0 ? Matiere.Substance : Matiere.Argile, Matiere.Aucune, OrigineSavoir.Essai, tous, objet: pate);
                return true;
            case Geste.Cuire or Geste.Souffler or Geste.Etouffer:
                if (!feu) return false;
                // Des racines dans le pot plein qu'on porte, posé sur le feu.
                if (geste == Geste.Cuire && a == Matiere.Plante)
                {
                    if (!PeutBouillir(h, false)) return false;
                    Bouillir(h, false, OrigineSavoir.Essai, tous);
                    return true;
                }
                if (geste == Geste.Etouffer && !Physique.EstBois(a)) a = materiaux.Contains(Matiere.BoisSec) ? Matiere.BoisSec : Matiere.Aucune;
                if (a is Matiere.Aucune or Matiere.Feu or Matiere.Eau or Matiere.Plante or Matiere.Carcasse && geste != Geste.Souffler) return false;
                b = Matiere.Feu;
                break;
            case Geste.Melanger:
                if (a is Matiere.Aucune or Matiere.Feu || b is Matiere.Aucune or Matiere.Feu || a == b) return false;
                // Une peau qu'on porte, trempée dans l'eau où l'on a mis de la pierre blanche.
                if ((a == Matiere.Peau || b == Matiere.Peau) && Porte(h, DeLaChaux) >= 0) return Epiler(h, OrigineSavoir.Essai, tous);
                break;
            case Geste.Plonger:
                if (!feu || !materiaux.Contains(Matiere.Eau) || a is not (Matiere.Galet or Matiere.Silex or Matiere.Calcaire)) return false;
                b = Matiere.Eau;
                break;
        }
        h.Activite = Activite.Experimenter;
        h.CibleX = h.CibleY = -1;
        FaireMatiere(h, geste, a, b, OrigineSavoir.Essai, tous);
        return true;
    }

    /// <summary>
    /// Au coin du feu, les mains s'occupent : une chose modelée qu'on porte finit posée près des braises, ou dedans. C'est ainsi,
    /// sans le vouloir, que la terre a d'abord cuit (les figurines de Dolní Věstonice, 26 000 ans, cuites dans des foyers :
    /// Vandiver et al., 1989).
    /// </summary>
    private bool AuCoinDuFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        if (alea.Reel() >= 0.003 * (0.3 + h.Personnalite.Ouverture)) return false;
        // Un pot plein posé sur les braises, avec les racines qu'on a sous la main : l'eau bout, les racines s'attendrissent.
        if (h.Faim > 20 && h.Objets.Exists(o => o.Plein && PotEtanche(o)) && PeutBouillir(h, false) && alea.Reel() < 0.5)
        {
            h.Activite = Activite.Veiller;
            Bouillir(h, false, OrigineSavoir.Essai, tous);
            return true;
        }
        var i = h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { Structure: Structure.Crue or Structure.Pate });
        if (i < 0) return false;
        h.Activite = Activite.Veiller;
        h.CibleX = h.CibleY = -1;
        FaireMatiere(h, Geste.Cuire, Matiere.Substance, Matiere.Feu, OrigineSavoir.Essai, tous, mode: alea.Reel() < 0.5 ? 0 : 1, objet: i);
        return true;
    }

    /// <summary>
    /// Préparer le creuset : il faut un creux de terre cuite (autre que la chose qu'on y met) et du charbon de bois, celui du
    /// foyer ou un morceau qu'on porte et qu'on y verse. Le creux peut se fendre au feu : une terre mal cuite ou déjà fêlée
    /// supporte mal le choc d'une chaleur de plus de 1 000 °C (Martinón-Torres et Rehren, 2009). Faux s'il manque quelque chose.
    /// </summary>
    private bool MettreAuCreuset(Humain h, ref int contenu)
    {
        var creux = -1;
        for (var i = 0; i < h.Objets.Count && creux < 0; i++)
            if (i != contenu && SubstanceDe(h, h.Objets[i]) is { Structure: Structure.Cuite } s && (s.Forme & Forme.Creux) != 0 && s.Fissures < 1) creux = i;
        if (creux < 0) return false;
        if (Installations.FoyerEn(h.X, h.Y) is not { Charbon: >= 0.5 })
        {
            var charbon = h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { BrulesSansFumee: true });
            if (charbon < 0 || charbon == contenu) return false;
            Oter(h, charbon, ref contenu);
            if (creux > charbon) creux--;
        }
        var pot = SubstanceDe(h, h.Objets[creux])!;
        if (alea.Reel() < 0.08 + 0.25 * (1 - pot.Frittage) + 0.5 * pot.Fissures) Oter(h, creux, ref contenu);
        return true;
    }

    /// <summary>Ôte un objet porté, en gardant le rang d'un autre juste.</summary>
    private static void Oter(Humain h, int i, ref int autre)
    {
        h.Objets.RemoveAt(i);
        if (autre > i) autre--;
    }

    /// <summary>Une matière qui se divise et se mêle à une autre : poudre, pâte, terre crue, eau.</summary>
    private static bool Divise(Substance s) => s.Structure is Structure.Grains or Structure.Pate || s.Structure == Structure.Crue && s.Forme == Forme.Aucune;

    /// <summary>Ce qu'on peut mêler : ce qui se divise, ou de l'herbe sèche, des fibres qu'on y écrase.</summary>
    private static bool SeMele(Substance s, Matiere m) => Divise(s) || m is Matiere.HerbeSeche or Matiere.Fibres;

    /// <summary>Deux choses façonnées qui servent à la même chose (deux pots, deux morceaux de charbon) : on ne garde que la meilleure.</summary>
    private static bool MemeChose(Objet a, Objet b) =>
        Substance.Lire(a.Pieces) is { } x && Substance.Lire(b.Pieces) is { } y
        && Perception.Famille(x) == Perception.Famille(y) && (x.Forme & Forme.Creux) == (y.Forme & Forme.Creux) && x.Structure == y.Structure;

    /// <summary>Un essai qui n'a rien donné : s'il croyait savoir le faire, il y croit un peu moins.</summary>
    private Effet Rien(Humain h, Geste g, Matiere a, Matiere b)
    {
        var i = h.Savoirs.FindIndex(s => s.Geste == g && s.A == a && s.B == b && s.Effet is Effet.Transformation or Effet.Modelage);
        if (i >= 0) h.Savoirs[i] = h.Savoirs[i] with { Essais = h.Savoirs[i].Essais + 1 };
        return Effet.Rien;
    }

    /// <summary>
    /// Mettre au feu : dans les flammes le plus souvent ; un prudent l'approche d'abord ; certains l'enfouissent sous la braise ;
    /// qui sait fouir creuse une fosse et la couvre de bois.
    /// </summary>
    private int ChoisirMode(Humain h)
    {
        var u = alea.Reel();
        // Qui porte un creux de terre cuite et a du charbon sous la main l'y met parfois, au cœur de la braise.
        if (u < 0.12 && h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { Structure: Structure.Cuite } p && (p.Forme & Forme.Creux) != 0)
            && (Installations.FoyerEn(h.X, h.Y) is { Charbon: >= 0.5 } || h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { BrulesSansFumee: true })))
            return Creuset;
        var prudent = 0.15 + 0.3 * h.Personnalite.Conscience;
        if (u < 0.2) return 2;
        if (u < 0.2 + prudent) return 1;
        if (h.Sait(Effet.Tubercules) && u > 0.9) return 3;
        return 0;
    }

    /// <summary>
    /// Ce qui arrive après un geste sur la matière : l'humain voit ce qui a changé, s'en souvient s'il le remarque, prend ce
    /// qui reste. Renvoie Transformation si quelque chose a changé.
    /// </summary>
    private Effet Constater(Humain h, Geste g, Matiere a, Matiere b, int mode, Substance avant, int objet, Transformation t, OrigineSavoir origine,
        IReadOnlyList<Humain> tous, Humain? maitre)
    {
        var changements = Perception.Changements(avant, t);
        // Mêlées, deux couleurs en font une autre, une pâte s'allège d'herbe : cela se voit sans surprendre. Ce qui frappe, c'est
        // une pâte qui se pétrit, une matière qui chauffe d'elle-même ou qui colle.
        if (g == Geste.Melanger) changements = [.. changements.Where(c => c is "plastique" or "chauffe" or "colle" or "durcit")];
        if (changements.Count == 0) return Rien(h, g, a, b);
        var effet = Comprendre(h, g, a, b, mode, changements, t, origine, tous, maitre,
            new Recit($"chim.essai.{g}", ("h", h), ("a", NomDe(a, avant)), ("b", Physique.Nom(b))), Perception.Famille(avant));
        // Ce qu'il tenait est devenu autre chose : il le garde s'il en reste une pièce entière.
        var garde = !t.Brulee && !t.Eclate && t.Produit.Masse >= 0.01 || changements.Contains("poudre");
        if (objet >= 0 && objet < h.Objets.Count)
        {
            var qualite = h.Objets[objet].Qualite;
            if (garde) h.Objets[objet] = new Objet(Matiere.Substance, qualite, 1, Pieces: t.Produit.Arrondie.Signature);
            else h.Objets.RemoveAt(objet);
        }
        else if (garde && effet != Effet.Rien && Utile(h, t.Produit))
            Prendre(h, new Objet(Matiere.Substance, Math.Clamp(0.4 + 0.5 * AdressePour(h, null) * alea.Reel(), 0, 1), 1, Pieces: t.Produit.Arrondie.Signature));
        return effet == Effet.Rien ? Effet.Rien : Effet.Transformation;
    }

    /// <summary>Une chose qu'on emporte : ce qui garde l'eau, brûle en braise, colle, luit, se plie, ou qu'on vient de modeler.</summary>
    private static bool Utile(Humain h, Substance s) =>
        s.TientLEau || s.BrulesSansFumee || s.Collante || s.Malleable || s.Part(Phase.CalcedoineChauffee) > 0.5 || s.Plastique
        || s.Structure == Structure.Cuite;

    /// <summary>
    /// Le savoir que l'humain tire de ce qu'il a vu : « cette chose, ainsi traitée, change ainsi ». Il le retient s'il le remarque
    /// (ce qui répond à un besoin frappe l'esprit, le reste n'est vu que des curieux), et ceux qui regardent peuvent l'imiter.
    /// </summary>
    private Effet Comprendre(Humain h, Geste g, Matiere a, Matiere b, int mode, IReadOnlyList<string> changements, Transformation? t,
        OrigineSavoir origine, IReadOnlyList<Humain> tous, Humain? maitre, Recit recit, string famille = "")
    {
        var pieces = Recette(g, a, b, mode, changements, a == Matiere.Substance ? famille : "");
        var utile = t is { } x && (x.Produit.TientLEau && (h.Soif > 30 || !h.Objets.Exists(Recipient)) || x.Produit.BrulesSansFumee && h.Froid > 15
            || x.Produit.Collante && h.Sait(Effet.PointeEmmanchee));
        if (!Remarquer(h, origine, g, a, b, Effet.Transformation, pieces, utile)) return Effet.Rien;
        var ev = Retenir(h, g, a, b, Effet.Transformation, 0, Symptome.Aucun, origine, maitre?.Id ?? 0, tous, pieces: pieces, recit: recit);
        if (origine != OrigineSavoir.Imitation) Montrer(h, g, a, b, Effet.Transformation, 0, Symptome.Aucun, tous, ev, pieces);
        return Effet.Transformation;
    }

    /// <summary>Le remarque-t-il ? Un savoir qu'il a déjà est reconnu ; un savoir nouveau, selon qu'il sert ou que l'humain est curieux.</summary>
    private bool Remarquer(Humain h, OrigineSavoir origine, Geste g, Matiere a, Matiere b, Effet e, string pieces, bool utile)
    {
        if (origine != OrigineSavoir.Essai || h.Savoirs.Exists(s => s.Geste == g && s.A == a && s.B == b && s.Effet == e && s.Pieces == pieces)) return true;
        var remarque = utile ? 0.5 + 0.5 * Ingeniosite(h) : 0.15 + 0.45 * h.Personnalite.Ouverture;
        return alea.Reel() < remarque;
    }

    /// <summary>
    /// La recette telle qu'elle est gardée avec le savoir : « T|geste|matière|seconde|façon|changements vus|famille » (la famille :
    /// ce qu'était la chose façonnée qu'on a mise au feu, vide pour une matière brute). Deux savoirs sont la même trouvaille (au sens
    /// de la chronologie) s'ils changent la même matière de la même façon visible (voir <see cref="Lois.Structure"/>).
    /// </summary>
    internal static string Recette(Geste g, Matiere a, Matiere b, int mode, IEnumerable<string> changements, string famille = "") =>
        string.Create(CultureInfo.InvariantCulture, $"T|{(int)g}|{(int)a}|{(int)b}|{mode}|{string.Join('+', changements)}|{famille}");

    /// <summary>Relit une recette : geste, matières, façon, changements, famille (null si ce n'en est pas une).</summary>
    internal static (Geste Geste, Matiere A, Matiere B, int Mode, string[] Changements, string Famille)? LireRecette(string? pieces)
    {
        if (pieces is null || !pieces.StartsWith("T|", StringComparison.Ordinal)) return null;
        var p = pieces.Split('|');
        if (p.Length != 7 || !int.TryParse(p[1], CultureInfo.InvariantCulture, out var g) || !int.TryParse(p[2], CultureInfo.InvariantCulture, out var a)
            || !int.TryParse(p[3], CultureInfo.InvariantCulture, out var b) || !int.TryParse(p[4], CultureInfo.InvariantCulture, out var mode)) return null;
        return ((Geste)g, (Matiere)a, (Matiere)b, mode, p[5].Length == 0 ? [] : p[5].Split('+'), p[6]);
    }

    // ------------------------------------------------------------------ Refaire ce qu'on sait

    /// <summary>Le savoir le plus sûr qui change cette matière de cette façon visible (par exemple : l'argile qui garde l'eau).</summary>
    private Savoir? Recette(Humain h, Matiere a, string changement) =>
        h.Savoirs.Where(s => s.Effet == Effet.Transformation && s.A == a && s.Confiance >= 0.3 && LireRecette(s.Pieces) is { } r && r.Changements.Contains(changement))
            .MaxBy(s => s.Confiance);

    /// <summary>
    /// Au calme, un adulte refait ce qu'il sait de la matière : cuire le pot qu'il a modelé quand il a séché, en modeler un s'il
    /// n'a rien pour porter l'eau, chauffer un silex sous la braise, couvrir un feu pour en tirer du charbon. C'est en refaisant
    /// qu'on garde ces savoirs et que les autres les apprennent. Vrai s'il l'a fait.
    /// </summary>
    private bool PratiquerLaMatiere(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!AvecLaMatiere) return false;
        VoirLEnduit(h, tous);
        if (h.Savoirs.Count == 0 || alea.Reel() >= 0.05 * (0.5 + h.Personnalite.Conscience)) return false;
        if (Chauler(h, tous)) return true;
        // Un peu faim, au feu, et l'on sait cuire dans l'eau : on met des racines à bouillir sans attendre d'être affamé.
        if (h.Faim > 30 && SavoirDeLaCuisine(h) is not null && Cuisiner(h, tous)) return true;
        var feu = Installations.FoyerEn(h.X, h.Y) is not null || Installations.BraiseEn(h.X, h.Y) is not null;
        // Le pot modelé attend d'être sec : le patient attend qu'il le soit tout à fait, l'impatient le met au feu plus tôt.
        if (feu)
        {
            for (var i = 0; i < h.Objets.Count; i++)
            {
                if (SubstanceDe(h, h.Objets[i]) is not { Structure: Structure.Pate or Structure.Crue } pate || (pate.Forme & Forme.Creux) == 0) continue;
                var attente = 0.03 + 0.08 * (1 - h.Personnalite.Conscience);
                if (pate.Part(Phase.Eau) > attente) break;
                var recette = Recette(h, Matiere.Substance, "eau") ?? Recette(h, Matiere.Argile, "eau");
                var mode = recette is not null && LireRecette(recette.Pieces) is { } r ? r.Mode : ChoisirMode(h);
                return Pratiquer(h, Geste.Cuire, Matiere.Substance, Matiere.Feu, tous, mode, i);
            }
        }
        // Rien pour porter l'eau : qui sait que la terre cuite la garde en modèle un, d'une terre mêlée de sable s'il l'a appris.
        if (!h.Objets.Exists(Recipient) && !h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { Structure: Structure.Pate or Structure.Crue })
            && (Recette(h, Matiere.Substance, "eau") ?? Recette(h, Matiere.Argile, "eau")) is not null && APortee(h, Matiere.Argile))
            return Pratiquer(h, Geste.Modeler, Matiere.Argile, Matiere.Aucune, tous, 1);
        // Qui a vu le métal sortir de la pierre verte va en écraser là où elle affleure, s'il sait qu'elle tombe en poudre.
        if (SavoirDuCuivre(h) is not null && !h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { } m && MineraiDeCuivre(m))
            && carte.Terrain.GisementA(h.X, h.Y) is { Type: Nature.Gite.Malachite or Nature.Gite.CuivreNatif } gc && APorteeDeMain(h, gc)
            && h.Objets.Count < ObjetsMax && Recette(h, Matiere.Minerai, "poudre") is { } broyer && APortee(h, broyer.B))
            return Pratiquer(h, Geste.Frapper, Matiere.Minerai, broyer.B, tous, 0);
        // Une goutte de cuivre se bat en plaque, qu'on porte en parure ; de deux plaques, on étire l'une en pointe pour percer les peaux.
        if (Battre(h, tous)) return true;
        if (!feu) return false;
        // Puis il met la poudre au feu, comme il l'a vu faire : le plus souvent au creuset, sous le charbon.
        if (SavoirDuCuivre(h) is { } cuivre && LireRecette(cuivre.Pieces) is { } rc)
        {
            var i = h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { } m && MineraiDeCuivre(m));
            if (i >= 0) return Pratiquer(h, Geste.Cuire, Matiere.Substance, Matiere.Feu, tous, rc.Mode, i);
        }
        // Un silex chauffé sous la braise se taille mieux.
        if (APortee(h, Matiere.Silex) && Recette(h, Matiere.Silex, "luit") is { } silex && LireRecette(silex.Pieces) is { } rs
            && !h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { } x && x.Part(Phase.CalcedoineChauffee) > 0.5))
            return Pratiquer(h, Geste.Cuire, Matiere.Silex, Matiere.Feu, tous, rs.Mode);
        // Du charbon pour la nuit et pour les feux qui doivent chauffer fort.
        if (h.Objets.Count < ObjetsMax && !h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { BrulesSansFumee: true })
            && h.Savoirs.Exists(s => s.Effet == Effet.Transformation && s.Geste == Geste.Etouffer && s.Confiance >= 0.4) && APortee(h, Matiere.BoisSec)
            && Installations.FoyerEn(h.X, h.Y) is { Heures: > 2 } && alea.Reel() < 0.2)
            return Pratiquer(h, Geste.Etouffer, Matiere.BoisSec, Matiere.Feu, tous, 0);
        return false;
    }

    /// <summary>
    /// Battre le métal qu'on porte, comme on l'a appris : la goutte devient une plaque (la parure), et quand on en a déjà une,
    /// une autre plaque devient une pointe (l'alêne). Vrai s'il l'a fait.
    /// </summary>
    private bool Battre(Humain h, IReadOnlyList<Humain> tous)
    {
        var goutte = -1;
        var plaques = new List<int>();
        for (var i = 0; i < h.Objets.Count; i++)
        {
            if (h.Objets[i].Matiere != Matiere.Substance || SubstanceDe(h, h.Objets[i]) is not { Malleable: true } m || m.Masse < 0.005) continue;
            if (m.Forme == Forme.Aucune && m.Porosite <= 0.03) goutte = i;
            else if ((m.Forme & Forme.Plat) != 0) plaques.Add(i);
        }
        var (objet, changement) = goutte >= 0 ? (goutte, "plat") : plaques.Count >= 2 && !h.Objets.Exists(Attirance.Alene) ? (plaques[1], "pointe") : (-1, "");
        if (objet < 0 || (Recette(h, Matiere.Substance, changement) ?? Recette(h, Matiere.Minerai, changement)) is not { Geste: Geste.Frapper } battre || !APortee(h, battre.B)) return false;
        return Pratiquer(h, Geste.Frapper, Matiere.Substance, battre.B, tous, 0, objet);
    }

    /// <summary>Le savoir le plus sûr qui a fait sortir du métal d'une pierre au feu (null s'il n'en a pas).</summary>
    private Savoir? SavoirDuCuivre(Humain h) =>
        h.Savoirs.Where(s => s.Effet == Effet.Transformation && s.Geste == Geste.Cuire && s.Confiance >= 0.3 && LireRecette(s.Pieces) is { } r
            && (r.Changements.Contains("goutte") || r.Changements.Contains("points"))).MaxBy(s => s.Confiance);

    /// <summary>Ce qu'on porte et d'où le feu tire du cuivre : pierre verte ou noire broyée, ou grains de cuivre à fondre ensemble.</summary>
    private static bool MineraiDeCuivre(Substance s) =>
        s.Structure != Structure.Metal && s.Part(Phase.Malachite) + s.Part(Phase.Tenorite) + s.Part(Phase.Cuivre) >= 0.1
        || s.Structure == Structure.Metal && s.Forme == Forme.Aucune && s.Masse < 0.02 && s.Porosite > 0.03;

    private bool Pratiquer(Humain h, Geste g, Matiere a, Matiere b, IReadOnlyList<Humain> tous, int mode, int objet = -1)
    {
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        FaireMatiere(h, g, a, b, OrigineSavoir.Essai, tous, mode: mode, objet: objet);
        return true;
    }

    /// <summary>Remplir son pot à la rivière. Vrai s'il l'a fait.</summary>
    private bool RemplirLePot(Humain h)
    {
        if (!AvecLaMatiere || h.Objets.Exists(o => o.Plein && Recipient(o))) return false;
        var i = h.Objets.FindIndex(PotEtanche);
        if (i < 0) return false;
        h.Objets[i] = h.Objets[i] with { Plein = true };
        return true;
    }

    /// <summary>
    /// Nourrir le feu de charbon de bois : il brûle deux fois plus longtemps que le même poids de bois, en braise, presque sans
    /// fumée, et plus chaud. Vrai s'il l'a fait.
    /// </summary>
    private bool NourrirDeCharbon(Humain h)
    {
        if (!AvecLaMatiere || Installations.FoyerEn(h.X, h.Y) is not { Heures: < 6 }) return false;
        var i = h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { BrulesSansFumee: true });
        if (i < 0) return false;
        var charbon = Substance.Lire(h.Objets[i].Pieces)!;
        h.Objets.RemoveAt(i);
        // 31 MJ/kg contre 14 pour du bois sec à l'air : un kilo de charbon tient le feu comme deux kilos de bois.
        var heures = 2 * charbon.Masse * charbon.PouvoirCalorifique / Thermique.Bois(0.2).PouvoirCalorifique;
        var (x, y, id) = (h.X, h.Y, h.Id);
        Tour.Differer(() => { if (Installations.FoyerEn(x, y) is { } f) Installations.Alimenter(f, heures, id, charbon: 1); });
        h.Activite = Activite.Veiller;
        h.CibleX = h.CibleY = -1;
        return true;
    }

    /// <summary>Un lien qui colle (poix de brai, bitume) porté sur soi : le meilleur des liens, chauffé.</summary>
    private bool PorteUnePoix(Humain h) => AvecLaMatiere && h.Objets.Exists(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { Collante: true });

    /// <summary>Un silex chauffé porté : celui qui taille un éclat prend celui-là.</summary>
    private int SilexChauffe(Humain h) =>
        !AvecLaMatiere ? -1 : h.Objets.FindIndex(o => o.Matiere == Matiere.Substance && SubstanceDe(h, o) is { } s && s.Part(Phase.CalcedoineChauffee) > 0.5);
}
