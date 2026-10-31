using System.Globalization;
using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les idées qui naissent du voisinage de ce qu'on sait. Rien ici ne dit quoi inventer : chaque chose connue devient une pièce,
/// chaque geste qu'on sait faire peut s'appliquer à une autre pièce, et deux pièces peuvent se joindre. L'humain rapproche,
/// essaie, et les lois (<see cref="Facons"/>) disent si cela tient, et ce que cela vaut pour ses besoins.
/// <para>
/// Ce que dit la recherche : l'invention recombine surtout ce qui existe déjà (Arthur, 2009 ; Youn et al., 2015 : la plupart
/// des brevets combinent des techniques connues) ; ce qui est à un pas de ce qu'on sait, le « possible adjacent » (Kauffman,
/// 2000), s'élargit à chaque trouvaille, si bien que le rythme des idées croît avec ce qu'on sait. Les idées viennent par
/// analogie (Gentner, 1983), par transposition d'un geste à une autre matière, et par la remarque d'une propriété en passant
/// (la branche qui revient d'un coup). Un essai raté fait souvent rejeter une idée qui aurait pu marcher ailleurs.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Besoins auxquels une chose façonnée peut répondre.</summary>
    private static readonly Fonction[] FonctionsUtiles = [Fonction.Chasser, Fonction.Couper, Fonction.Contenir, Fonction.Pecher];

    /// <summary>
    /// Multiplicateur de la venue des idées, choisi par le joueur (« Esprits fertiles », 1 : le monde tel quel). Ce n'est pas une
    /// loi du monde : c'est un geste de l'Entité, signalé au journal.
    /// </summary>
    public int EspritsFertiles { get; set; } = 1;

    /// <summary>Clé d'une trouvaille dans le monde : son effet et sa structure (comme dans <see cref="Decouverte"/>).</summary>
    public static string CleDeSavoir(Effet e, string? pieces) => $"{(int)e}|{Lois.Structure(pieces)}";

    private static string CleDeSavoir(Savoir s) => CleDeSavoir(s.Effet, s.Pieces);

    // ------------------------------------------------------------------ Ce qu'il a sous la main, ce qu'il sait faire

    private Atelier AtelierDe(Humain h, List<Matiere> materiaux)
    {
        double durete = 0, fil = 0;
        var pointe = false;
        var abrasif = false;
        foreach (var m in materiaux)
        {
            if (!Facture.EstBrute(m)) continue;
            var t = Facons.De(m);
            if (t.Durete >= 0.45 && t.Masse < 5) durete = Math.Max(durete, t.Durete);
            fil = Math.Max(fil, t.Fil);
            if (t.A(Forme.Pointu) && t.Durete >= 0.5) pointe = true;
            if (m is Matiere.Sable or Matiere.Galet or Matiere.Calcaire) abrasif = true;
        }
        foreach (var o in h.Objets)
        {
            fil = Math.Max(fil, Lois.Coupe(o) > 0.3 ? 0.6 : 0);
            if (o.Matiere == Matiere.Facture && Facons.LireObjet(o.Pieces) is { } x && Facons.Calculer(x.Facture) is { Reussie: true } c)
            {
                fil = Math.Max(fil, c.Traits.Fil);
                if (c.Traits.A(Forme.Pointu) && c.Traits.Durete >= 0.5) pointe = true;
            }
        }
        return new Atelier(durete, fil, pointe, materiaux.Contains(Matiere.Feu), materiaux.Contains(Matiere.Eau), abrasif);
    }

    /// <summary>Les gestes et jointures qu'il sait faire, chacun avec le savoir qui le lui a appris (pour la filiation).</summary>
    private static (Dictionary<Facon, string> Facons, Dictionary<Jointure, string> Jointures) GestesConnus(Humain h)
    {
        var facons = new Dictionary<Facon, string>();
        var jointures = new Dictionary<Jointure, string>();
        string? Par(Func<Savoir, bool> p) => h.Savoirs.Find(s => s.Effet != Effet.Rien && s.Confiance >= 0.4 && p(s)) is { } s ? CleDeSavoir(s) : null;
        string? Effet_(Effet e) => Par(s => s.Effet == e);
        string? Remarque(string r) => Par(s => s.Effet == Effet.Remarque && Facons.LireRemarque(s.Pieces) == r);
        void F(Facon f, string? k) { if (k is not null) facons.TryAdd(f, k); }
        void J(Jointure j, string? k) { if (k is not null) jointures.TryAdd(j, k); }

        F(Facon.Aiguiser, Effet_(Effet.Pointe));
        F(Facon.Fendre, Effet_(Effet.Decoupe) ?? Effet_(Effet.TranchantEmmanche) ?? Remarque("fil"));
        F(Facon.Evider, Effet_(Effet.EauPortee) ?? Remarque("etanche"));
        F(Facon.Courber, Remarque("ressort") ?? Effet_(Effet.Durci));
        F(Facon.Tresser, Effet_(Effet.Corde) ?? Remarque("souple"));
        F(Facon.Polir, Par(s => s.Geste == Geste.Frotter && s.Effet is Effet.Tiedeur or Effet.Chaleur or Effet.Flamme) ?? Remarque("dur"));
        F(Facon.Encocher, Effet_(Effet.Decoupe));
        F(Facon.Durcir, Effet_(Effet.Durci));
        F(Facon.Percer, Effet_(Effet.Percage));
        var corde = Effet_(Effet.Corde);
        J(Jointure.Bout, Effet_(Effet.PointeEmmanchee) ?? corde);
        J(Jointure.Travers, Effet_(Effet.TranchantEmmanche) ?? corde);
        if (corde is not null) J(Jointure.Bouts, Remarque("ressort"));
        J(Jointure.Enduire, Effet_(Effet.Modelage) ?? Par(s => s.Effet == Effet.Transformation && s.Geste == Geste.Couvrir));
        if (corde is not null) J(Jointure.Coudre, Effet_(Effet.Percage));
        // Ce qui revient d'un coup, ou un crochet qui retient : de quoi pousser une autre chose.
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Facture || s.Confiance < 0.4 || Facons.LireObjet(s.Pieces) is not { } x) continue;
            var cle = CleDeSavoir(s);
            var hf = new HashSet<Facon>();
            var hj = new HashSet<Jointure>();
            x.Facture.Gestes(hf, hj);
            foreach (var f in hf) facons.TryAdd(f, cle);
            foreach (var j in hj) jointures.TryAdd(j, cle);
            if (Facons.Calculer(x.Facture) is { Reussie: true } c)
            {
                if (c.Traits.Tension > 0 || c.Traits.A(Forme.Crochet) && c.Traits.A(Forme.Long)) jointures.TryAdd(Jointure.Propulse, cle);
                if (c.Traits.A(Forme.Crochet) && c.Traits.Longueur <= 0.4 && corde is not null) jointures.TryAdd(Jointure.Ligne, cle);
            }
        }
        return (facons, jointures);
    }

    /// <summary>
    /// Les pièces auxquelles il peut penser : ce qu'il a sous la main ou sur lui, et les choses qu'il sait faire (avec leurs
    /// parties). Chacune avec le savoir d'où elle vient (vide pour une matière brute).
    /// </summary>
    private static List<(Facture Piece, string Parent)> PiecesConnues(Humain h, List<Matiere> materiaux)
    {
        var l = new List<(Facture, string)>();
        foreach (var m in materiaux) if (Facture.EstBrute(m)) l.Add((new Brute(m), ""));
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Facture || s.Confiance < 0.4 || Facons.LireObjet(s.Pieces) is not { } x) continue;
            var cle = CleDeSavoir(s);
            l.Add((x.Facture, cle));
            if (x.Facture is Faconnee fx && fx.X is not Brute) l.Add((fx.X, cle));
            if (x.Facture is Jointe jx)
            {
                if (jx.A is not Brute) l.Add((jx.A, cle));
                if (jx.B is not Brute) l.Add((jx.B, cle));
            }
            if (l.Count >= 24) break;
        }
        return l;
    }

    /// <summary>Les liens qu'il a sous la main : corde, fibres, résine ou poix (près du feu).</summary>
    private List<Matiere> LiensDe(Humain h, List<Matiere> materiaux)
    {
        var l = new List<Matiere>();
        foreach (var m in materiaux) if (m is Matiere.Corde or Matiere.Fibres or Matiere.Resine or Matiere.Ecorce or Matiere.Peau) l.Add(m);
        if (PorteUnePoix(h)) l.Add(Matiere.Substance);
        return l;
    }

    /// <summary>Ce que vaut, pour ce besoin, le meilleur de ce qu'il porte ou sait faire.</summary>
    private static double Meilleur(Humain h, Fonction f)
    {
        var v = 0.0;
        foreach (var o in h.Objets)
            v = Math.Max(v, f switch
            {
                Fonction.Chasser => Lois.Chasse(o),
                Fonction.Couper => Lois.Coupe(o),
                Fonction.Contenir => o.Matiere is Matiere.Calebasse ? Math.Log(1 + 1.9) : Facons.Valeur(f, o),
                _ => Facons.Valeur(f, o),
            });
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Facture || s.Confiance < 0.4 || Facons.LireObjet(s.Pieces) is not { } x || x.Fonction != f) continue;
            if (Facons.Calculer(x.Facture) is { Reussie: true } c) v = Math.Max(v, 0.8 * Facons.Valeur(f, c.Traits));
        }
        return v;
    }

    // ------------------------------------------------------------------ Remarquer

    /// <summary>
    /// En manipulant une matière, remarquer ce qu'elle a de particulier : la branche verte qui plie et revient, la coquille qui
    /// garde l'eau, l'éclat qui coupe. Une remarque n'est pas un savoir-faire, mais elle donne des idées. Vrai s'il a remarqué.
    /// </summary>
    private bool Remarquer(Humain h, List<Matiere> materiaux, IReadOnlyList<Humain> tous)
    {
        if (materiaux.Count == 0) return false;
        var m = materiaux[alea.Entier(materiaux.Count)];
        if (!Facture.EstBrute(m)) return false;
        var props = Facons.Proprietes(Facons.De(m)).ToList();
        if (props.Count == 0) return false;
        var r = props[alea.Entier(props.Count)];
        if (h.Savoirs.Exists(s => s.Effet == Effet.Remarque && Facons.LireRemarque(s.Pieces) == r)) return false;
        if (alea.Reel() >= 0.15 + 0.45 * h.Personnalite.Ouverture) return false;
        h.Activite = Activite.Experimenter;
        h.CibleX = h.CibleY = -1;
        var pieces = "R|" + r;
        var ev = Retenir(h, Geste.Faconner, m, Matiere.Aucune, Effet.Remarque, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous, pieces: pieces,
            recit: new Recit($"fac.remarque.{r}", ("h", h), ("a", Physique.Nom(m))));
        Montrer(h, Geste.Faconner, m, Matiere.Aucune, Effet.Remarque, 0, Symptome.Aucun, tous, ev, pieces);
        return true;
    }

    // ------------------------------------------------------------------ Imaginer une combinaison

    /// <summary>
    /// Une idée par combinaison : façonner une pièce d'une manière qu'il connaît, joindre deux pièces, ou refaire une chose
    /// qu'il sait faire en changeant une pièce. <paramref name="pousse"/> : le besoin qui presse (1 : simple curiosité).
    /// Vrai s'il a essayé quelque chose.
    /// </summary>
    private bool InventerParCombinaison(Humain h, Fonction besoin, double pousse, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.Savoirs.Count == 0) return false;
        var p = h.Personnalite;
        var ingenieux = 0.2 + Ingeniosite(h);
        // Le possible adjacent : plus on sait de choses, plus il y a de rapprochements à faire.
        var connus = 0;
        foreach (var s in h.Savoirs) if (s.Effet != Effet.Rien && s.Confiance >= 0.4) connus++;
        var voisinage = Math.Min(3, Math.Log(1 + connus) / Math.Log(10));
        if (alea.Reel() >= 0.00015 * ingenieux * ingenieux * (0.5 + p.Ouverture) * pousse * voisinage * EspritsFertiles) return false;

        var materiaux = Materiaux(h);
        var (facons, jointures) = GestesConnus(h);
        var pieces = PiecesConnues(h, materiaux);
        if (pieces.Count == 0) return false;
        var idee = Imaginer(h, pieces, facons, jointures, LiensDe(h, materiaux));
        if (idee is null) return false;
        return Essayer(h, idee.Value.Facture, idee.Value.Parents, besoin, OrigineSavoir.Reflexion, tous);
    }

    /// <summary>
    /// Refaire sur une pièce le geste qui vient de la façonner (polir ce qui est poli, aiguiser ce qui est aiguisé) n'est pas une
    /// idée nouvelle ; seul fendre encore donne une autre pièce (plus mince).
    /// </summary>
    private static bool Redite(Facon f, Facture x) => f != Facon.Fendre && x is Faconnee fx && fx.F == f;

    private (Facture Facture, List<string> Parents)? Imaginer(Humain h, List<(Facture Piece, string Parent)> pieces, Dictionary<Facon, string> facons,
        Dictionary<Jointure, string> jointures, List<Matiere> liens)
    {
        var parents = new List<string>();
        void Parent(string k) { if (k.Length > 0 && !parents.Contains(k)) parents.Add(k); }
        (Facture, string) Piece(Func<Traits, bool>? role = null)
        {
            var possibles = role is null ? pieces : pieces.FindAll(x => Facons.Calculer(x.Piece) is { Reussie: true } c && role(c.Traits));
            if (possibles.Count == 0) return (null!, "");
            // L'esprit revient volontiers à ce qu'il a déjà fait.
            var faites = possibles.FindAll(x => x.Parent.Length > 0);
            var l = faites.Count > 0 && alea.Reel() < 0.5 ? faites : possibles;
            return l[alea.Entier(l.Count)];
        }

        var choix = alea.Reel();
        var refaire = pieces.FindAll(x => x.Parent.Length > 0 && x.Piece is not Brute);
        if (choix < 0.4 || jointures.Count == 0 && refaire.Count == 0)
        {
            // Façonner une pièce : un geste connu, ou, rarement, un geste qu'il n'a jamais vu faire.
            Facon f;
            if (facons.Count > 0 && alea.Reel() >= 0.1 * h.Personnalite.Ouverture)
            {
                var k = facons.Keys.ElementAt(alea.Entier(facons.Count));
                f = k;
                Parent(facons[k]);
            }
            else f = (Facon)(1 + alea.Entier(9));
            var (x, px) = Piece();
            if (x is null || Redite(f, x)) return null;
            Parent(px);
            return (new Faconnee(f, x), parents);
        }
        if (choix < 0.8 && jointures.Count > 0)
        {
            var j = jointures.Keys.ElementAt(alea.Entier(jointures.Count));
            Parent(jointures[j]);
            // Les rôles : l'esprit met ensemble ce qui va ensemble (une chose longue et une chose dure, un lien et une chose qui plie).
            (Facture, string) a, b;
            var lien = Matiere.Aucune;
            switch (j)
            {
                case Jointure.Bout or Jointure.Travers:
                    a = Piece(t => t.A(Forme.Long) && t.Souplesse <= 0.7);
                    b = Piece(t => t.Durete >= 0.4 || t.Fil > 0.2);
                    if (liens.Count == 0) return null;
                    lien = liens[alea.Entier(liens.Count)];
                    break;
                case Jointure.Bouts:
                    a = Piece(t => t.A(Forme.Long) && t.Souplesse is > 0.2 and < 0.9);
                    b = Piece(t => t.A(Forme.Long) && t.Souplesse >= 0.8);
                    break;
                case Jointure.Propulse:
                    a = Piece(t => t.Tension > 0 || t.A(Forme.Crochet));
                    b = Piece(t => t.A(Forme.Long) && t.Masse < 3);
                    break;
                case Jointure.Enduire:
                    a = Piece(t => t.A(Forme.Creux) || t.A(Forme.Plat));
                    b = Piece(t => t.Durete < 0.1 && t.Masse > 0.1 || t.Lien > 0.4 && !t.A(Forme.Long));
                    break;
                case Jointure.Coudre:
                    a = Piece(t => t.A(Forme.Plat) && t.Souplesse >= 0.4);
                    b = Piece(t => t.A(Forme.Long) && t.Souplesse >= 0.8);
                    break;
                default:
                    a = Piece(t => t.A(Forme.Crochet));
                    b = Piece(t => t.A(Forme.Long) && t.Souplesse >= 0.8);
                    break;
            }
            if (a.Item1 is null || b.Item1 is null) return null;
            Parent(a.Item2);
            Parent(b.Item2);
            return (new Jointe(j, lien, a.Item1, b.Item1), parents);
        }
        if (refaire.Count == 0) return null;
        // Refaire une chose qu'il sait faire en changeant une pièce brute pour une autre qui lui ressemble, ou en la façonnant encore.
        var (modele, pm) = refaire[alea.Entier(refaire.Count)];
        Parent(pm);
        if (facons.Count > 0 && alea.Reel() < 0.4)
        {
            var k = facons.Keys.ElementAt(alea.Entier(facons.Count));
            if (Redite(k, modele)) return null;
            Parent(facons[k]);
            return (new Faconnee(k, modele), parents);
        }
        var bruts = new List<Matiere>();
        foreach (var x in pieces) if (x.Piece is Brute br) bruts.Add(br.M);
        var cible = modele.Matieres().Keys.ToList();
        if (cible.Count == 0 || bruts.Count == 0) return null;
        var ancienne = cible[alea.Entier(cible.Count)];
        var analogues = bruts.FindAll(m => m != ancienne && Analogue(ancienne, m));
        if (analogues.Count == 0) return null;
        var nouvelle = analogues[alea.Entier(analogues.Count)];
        return (Remplacer(modele, ancienne, nouvelle), parents);
    }

    private static Facture Remplacer(Facture f, Matiere ancienne, Matiere nouvelle) => f switch
    {
        Brute b when b.M == ancienne => new Brute(nouvelle),
        Faconnee x => x with { X = Remplacer(x.X, ancienne, nouvelle) },
        Jointe j => j with { A = Remplacer(j.A, ancienne, nouvelle), B = Remplacer(j.B, ancienne, nouvelle), Lien = j.Lien == ancienne ? nouvelle : j.Lien },
        _ => f,
    };

    /// <summary>A-t-il de quoi faire la chose ici (chaque matière, en nombre) ?</summary>
    private bool APiecesPour(Humain h, Facture f)
    {
        foreach (var (m, n) in f.Matieres()) if (Combien(h, m) < n) return false;
        return true;
    }

    private void ConsommerPour(Humain h, Facture f)
    {
        foreach (var (m, n) in f.Matieres()) Consommer(h, m, n);
    }

    /// <summary>
    /// Essayer la chose imaginée : la faire avec ce qu'on a, voir ce qu'elle vaut pour chaque besoin, la garder si elle fait
    /// mieux que ce qu'on avait. Le journal dit l'idée, ce qui est arrivé et pourquoi (les lois en donnent la raison).
    /// </summary>
    private bool Essayer(Humain h, Facture f, List<string> parents, Fonction besoin, OrigineSavoir origine, IReadOnlyList<Humain> tous)
    {
        // Une chose trop compliquée pour qu'on la retienne, ou illisible.
        if (Facture.Lire(f.Ecrire()) is null) return false;
        var recette = f.Ecrire();
        var rien = "F|0|" + recette;
        // Déjà su, ou déjà vu échouer (tant qu'il s'en souvient).
        if (h.Savoirs.Exists(s => (s.Effet == Effet.Facture && Facons.LireObjet(s.Pieces) is { } x && x.Facture.Ecrire() == recette)
                                  || s.Effet == Effet.Rien && s.Pieces == rien)) return false;
        var question = new Recit("fac.idee.question", ("h", h), ("geste", Facons.DireGeste(f)));
        var entete = origine == OrigineSavoir.Entite ? new Recit("fac.idee.souffle", ("h", h), ("question", question)) : question;
        if (!APiecesPour(h, f))
        {
            if (origine == OrigineSavoir.Entite)
                Noter(_maintenant, TypeEvenement.Pensee, h, null, new Recit("fac.idee.pasDeQuoi", ("question", entete), ("h", h)));
            return false;
        }
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        var materiaux = Materiaux(h);
        var c = Facons.Construire(f, AtelierDe(h, materiaux));
        if (!c.Reussie)
        {
            Echouer(h, rien, f, origine, new Recit("fac.idee.rate", ("question", entete), ("h", h), ("raison", c.Echec!)));
            return true;
        }
        // Le besoin qui presse d'abord ; sinon, ce à quoi la chose sert le mieux, si elle fait mieux que ce qu'il a.
        var fonction = Fonction.Aucune;
        var gain = 0.0;
        foreach (var fn in FonctionsUtiles)
        {
            var v = Facons.Valeur(fn, c.Traits);
            if (v <= 0) continue;
            var mieux = v - Meilleur(h, fn);
            if (mieux <= 0.05) continue;
            var poids = mieux * (fn == besoin ? 2 : 1);
            if (poids > gain) (fonction, gain) = (fn, poids);
        }
        if (fonction == Fonction.Aucune)
        {
            var utile = FonctionsUtiles.Any(fn => Facons.Valeur(fn, c.Traits) > 0);
            Echouer(h, rien, f, origine, new Recit(utile ? "fac.idee.pasMieux" : "fac.idee.inutile", ("question", entete), ("h", h),
                ("objet", Facons.Decrire(f))));
            return true;
        }
        var pieces = Facons.Ecrire(fonction, f);
        var recit = new Recit("fac.idee.marche", ("question", entete), ("h", h), ("objet", Facons.Decrire(f)), ("usage", Usage(fonction, c.Traits)));
        var b = f is Jointe jj ? jj.B.Principale : Matiere.Aucune;
        var ev = Retenir(h, Geste.Faconner, f.Principale, b, Effet.Facture, 0, Symptome.Aucun, origine, 0, tous, pieces: pieces, recit: recit,
            parents: string.Join(';', parents));
        Faire(h, f, fonction, pieces);
        Montrer(h, Geste.Faconner, f.Principale, b, Effet.Facture, 0, Symptome.Aucun, tous, ev, pieces);
        return true;
    }

    /// <summary>L'idée n'a rien donné : il retient qu'elle ne marche pas (à tort peut-être), et la question lui reste.</summary>
    private void Echouer(Humain h, string rien, Facture f, OrigineSavoir origine, Recit texte)
    {
        var id = Noter(_maintenant, origine == OrigineSavoir.Entite ? TypeEvenement.Decouverte : TypeEvenement.Pensee, h, null, texte);
        h.Savoirs.Add(new Savoir(Geste.Faconner, f.Principale, Matiere.Aucune, Effet.Rien, 0, Symptome.Aucun, 1, 0, _maintenant, _maintenant,
            origine, 0, id, rien));
        Jardin.Confier(h, TypeIntime.Question, 0, new Recit("fac.jardin.rate", ("geste", Facons.DireGeste(f))), 15 + 25 * h.Personnalite.Ouverture,
            id, _maintenant, "fac:" + f.Ecrire());
    }

    /// <summary>Ce que la chose apporte, dit avec les grandeurs des lois (« elle part à 40 m/s et porte à 80 m »).</summary>
    private static Recit Usage(Fonction f, Traits t)
    {
        var c = CultureInfo.InvariantCulture;
        return f switch
        {
            Fonction.Chasser => t.Vitesse > 0
                ? new Recit("fac.usage.lancer", ("v", Math.Round(t.Vitesse).ToString(c)), ("portee", Math.Round(t.Vitesse * t.Vitesse / (2 * Lois.Gravite)).ToString(c)))
                : Recit.Seul("fac.usage.Chasser"),
            Fonction.Contenir => new Recit("fac.usage.Contenir", ("l", Math.Round(Facons.Eau(t), 1).ToString(c))),
            _ => Recit.Seul($"fac.usage.{f}"),
        };
    }

    /// <summary>Fait la chose et la prend sur soi (les pièces portées sont prises ; ce que la case offre ne manque pas).</summary>
    private void Faire(Humain h, Facture f, Fonction fonction, string pieces)
    {
        ConsommerPour(h, f);
        var savoir = h.Savoirs.Find(s => s.Effet == Effet.Facture && s.Pieces == pieces);
        var qualite = Math.Clamp(0.3 + 0.5 * AdressePour(h, savoir) * (0.6 + 0.4 * alea.Reel()), 0, 1);
        Prendre(h, new Objet(Matiere.Facture, qualite, 1, Pieces: pieces));
    }

    /// <summary>La chose a servi : le savoir qui l'a faite gagne en confiance.</summary>
    private void RenforcerFacture(Humain h, Objet o)
    {
        var i = h.Savoirs.FindIndex(s => s.Effet == Effet.Facture && s.Pieces == o.Pieces);
        if (i >= 0) h.Savoirs[i] = h.Savoirs[i] with { Essais = h.Savoirs[i].Essais + 1, Succes = h.Savoirs[i].Succes + 1, UtiliseLe = _maintenant };
    }

    // ------------------------------------------------------------------ Refaire ce qu'on sait faire

    /// <summary>
    /// Un besoin : refaire la meilleure chose qu'il sait faire pour lui, s'il n'en porte pas d'aussi bonne et qu'il a de quoi.
    /// Parfois, en la refaisant, il essaie une variante (l'effet cliquet). Vrai s'il a agi.
    /// </summary>
    private bool FaconnerPour(Humain h, Fonction f, IReadOnlyList<Humain> tous)
    {
        Savoir? meilleur = null;
        Facture? facture = null;
        var valeur = 0.0;
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Facture || s.Confiance < 0.3 || Facons.LireObjet(s.Pieces) is not { } x || x.Fonction != f) continue;
            if (Facons.Calculer(x.Facture) is not { Reussie: true } c) continue;
            var v = Facons.Valeur(f, c.Traits) * (0.5 + 0.5 * s.Confiance);
            if (v > valeur) (meilleur, facture, valeur) = (s, x.Facture, v);
        }
        if (meilleur is null || facture is null) return false;
        var porte = 0.0;
        foreach (var o in h.Objets)
            porte = Math.Max(porte, f switch { Fonction.Chasser => Lois.Chasse(o), Fonction.Couper => Lois.Coupe(o), _ => Facons.Valeur(f, o) });
        if (porte >= 0.8 * Facons.Valeur(f, Facons.Calculer(facture).Traits)) return false;
        if (!APiecesPour(h, facture)) return false;
        // Une variante, en le refaisant.
        if (alea.Reel() < 0.1 * Inventivite(h) * EspritsFertiles)
        {
            var materiaux = Materiaux(h);
            var (facons, jointures) = GestesConnus(h);
            if (Imaginer(h, [(facture, CleDeSavoir(meilleur))], facons, jointures, LiensDe(h, materiaux)) is { } v
                && Essayer(h, v.Facture, v.Parents, f, OrigineSavoir.Reflexion, tous)) return true;
        }
        if (!Facons.Construire(facture, AtelierDe(h, Materiaux(h))).Reussie) return false;
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        Faire(h, facture, f, meilleur.Pieces);
        var i = h.Savoirs.IndexOf(meilleur);
        h.Savoirs[i] = meilleur with { UtiliseLe = _maintenant };
        Montrer(h, Geste.Faconner, meilleur.A, meilleur.B, Effet.Facture, 0, Symptome.Aucun, tous, 0, meilleur.Pieces);
        return true;
    }

    /// <summary>Se munir de ce qui manque, avec les choses qu'il sait façonner (génération 2). Vrai s'il a agi.</summary>
    private bool OutillerParFacons(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!AvecLaMatiere || !h.Savoirs.Exists(s => s.Effet == Effet.Facture)) return false;
        if (alea.Reel() >= 0.05 * (0.5 + h.Personnalite.Conscience)) return false;
        foreach (var f in FonctionsUtiles)
            if (FaconnerPour(h, f, tous)) return true;
        return false;
    }

    /// <summary>Au calme : une idée, par curiosité ou pour un besoin du moment (génération 2). Vrai s'il a essayé.</summary>
    private bool IdeeOuverte(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!AvecLaMatiere) return false;
        var besoin = FonctionsUtiles[alea.Entier(FonctionsUtiles.Length)];
        return InventerParCombinaison(h, besoin, h.Faim > 40 || h.Soif > 40 ? 2 : 1, tous);
    }

    /// <summary>Une idée par combinaison, poussée par ce besoin (pour les essais).</summary>
    internal bool InventerParCombinaisonPourTest(Humain h, Fonction f, IReadOnlyList<Humain> tous) =>
        AvecLaMatiere && InventerParCombinaison(h, f, 3, tous);

    // ------------------------------------------------------------------ L'Entité souffle une combinaison

    /// <summary>
    /// L'Entité souffle une combinaison (la recette écrite comme dans <see cref="Facture"/>) : l'humain l'essaie s'il a de quoi,
    /// et les lois en décident comme pour une idée venue de lui. Renvoie l'effet obtenu (Rien si cela n'a pas marché ou s'il
    /// n'a pas de quoi).
    /// </summary>
    public Effet SoufflerFacture(Humain h, string recette, IReadOnlyList<Humain> tous, long maintenant)
    {
        _maintenant = maintenant;
        _temperature = Temperature(h.X, h.Y, maintenant);
        if (Facture.Lire(recette) is not { } f || !h.EstVivant) return Effet.Rien;
        var avant = h.Savoirs.Count(s => s.Effet == Effet.Facture);
        // L'Entité peut souffler une idée déjà rejetée : il y repense.
        h.Savoirs.RemoveAll(s => s.Effet == Effet.Rien && s.Pieces == "F|0|" + f.Ecrire());
        Essayer(h, f, [], Fonction.Aucune, OrigineSavoir.Entite, tous);
        return h.Savoirs.Count(s => s.Effet == Effet.Facture) > avant ? Effet.Facture : Effet.Rien;
    }

    /// <summary>
    /// Le possible adjacent : ce qui est à un pas de ce que savent ces humains (un geste connu sur une chose connue, deux choses
    /// connues jointes d'une manière connue), et que personne au monde n'a encore trouvé. Rien n'y dit si cela marcherait.
    /// Renvoie le nombre de ces combinaisons et quelques-unes, toujours les mêmes pour un même état.
    /// </summary>
    public (int Total, List<Facture> Exemples) Adjacents(IEnumerable<Humain> humains, int max = 40)
    {
        if (!AvecLaMatiere) return (0, []);
        var facons = new HashSet<Facon>();
        var jointures = new HashSet<Jointure>();
        var pieces = new Dictionary<string, Facture>(StringComparer.Ordinal);
        var liens = new HashSet<Matiere>();
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            var (f, j) = GestesConnus(h);
            facons.UnionWith(f.Keys);
            jointures.UnionWith(j.Keys);
            foreach (var s in h.Savoirs)
            {
                if (s.Effet == Effet.Rien || s.Confiance < 0.4) continue;
                foreach (var m in new[] { s.A, s.B })
                {
                    if (!Facture.EstBrute(m)) continue;
                    pieces.TryAdd(((int)m).ToString(CultureInfo.InvariantCulture), new Brute(m));
                    if (m is Matiere.Corde or Matiere.Fibres or Matiere.Resine) liens.Add(m);
                }
                if (s.Effet == Effet.Facture && Facons.LireObjet(s.Pieces) is { } x) pieces.TryAdd(x.Facture.Ecrire(), x.Facture);
            }
        }
        var trouvees = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in Installations.Decouvertes) if (d.Effet == Effet.Facture) trouvees.Add(Lois.Structure(d.Pieces));
        var liste = pieces.Values.OrderBy(p => p.Ecrire(), StringComparer.Ordinal).Take(40).ToList();
        var candidats = new SortedSet<string>(StringComparer.Ordinal);
        void Ajouter(Facture f)
        {
            var e = f.Ecrire();
            if (Facture.Lire(e) is null || trouvees.Contains("F|" + e)) return;
            candidats.Add(e);
        }
        foreach (var f in facons)
            foreach (var p in liste) Ajouter(new Faconnee(f, p));
        foreach (var j in jointures)
            foreach (var a in liste)
            foreach (var b in liste)
            {
                if (ReferenceEquals(a, b) && a is not Brute) continue;
                if (j is Jointure.Bout or Jointure.Travers)
                {
                    foreach (var l in liens) Ajouter(new Jointe(j, l, a, b));
                }
                else Ajouter(new Jointe(j, Matiere.Aucune, a, b));
            }
        var tous = candidats.ToList();
        var exemples = new List<Facture>();
        if (tous.Count > 0)
        {
            var pas = Math.Max(1, tous.Count / max);
            for (var i = 0; i < tous.Count && exemples.Count < max; i += pas) exemples.Add(Facture.Lire(tous[i])!);
        }
        return (tous.Count, exemples);
    }

    /// <summary>Pour l'interface du joueur : les pièces auxquelles l'humain peut penser, et les gestes et jointures qu'il connaît.</summary>
    public (List<string> Pieces, List<Facon> Facons, List<Jointure> Jointures, List<Matiere> Liens) PossiblesPour(Humain h, long maintenant)
    {
        if (!AvecLaMatiere) return ([], [], [], []);
        _maintenant = maintenant;
        var materiaux = Materiaux(h);
        var pieces = PiecesConnues(h, materiaux).Select(x => x.Piece.Ecrire()).Distinct().ToList();
        return (pieces, [.. Enum.GetValues<Facon>()], [.. Enum.GetValues<Jointure>()], LiensDe(h, materiaux));
    }
}
