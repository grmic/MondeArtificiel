using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Semer, soigner, récolter, garder la semence (mondes de la génération 2, depuis la 0.33.0). Rien n'est décrété : qui croit que
/// « ce qu'on enfouit repousse » met en terre ce qu'il mange ; s'il revient et trouve les plantes levées, il sait qu'on peut les
/// semer. Arracher les herbes, couper les tiges plutôt que secouer les épis, garder des graines pour l'an prochain, semer sur le
/// limon ou sur une terre brûlée : chaque geste se découvre, se confirme, s'enseigne ou se perd.
/// <para>
/// La plante change sous la main de l'homme sans qu'il le veuille. Secouer les épis dans un panier ne garde que les graines qui
/// tombent : rien ne change. Arracher ou couper les tiges mûres garde celles qui restent attachées, et la lignée perd peu à peu
/// son égrenage ; ressemer ses propres graines plutôt que des sauvages fait avancer la sélection, des graines sauvages mêlées la
/// retardent (Hillman et Davies, 1990 : 20 à 200 ans dans le meilleur des cas ; l'archéologie en montre 1 000 à 2 000 au
/// Proche-Orient : Tanno et Willcox, 2006 ; Fuller, 2007). Semer profond au bâton favorise les grosses graines et la levée rapide ;
/// replanter le plus beau tubercule va bien plus vite, parce que la bouture est la même plante.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Les champs du monde (fourni par le monde).</summary>
    public Champs Champs { get; set; } = new();

    /// <summary>Communautés qui vivent près de leurs parcelles (camp qui ne bouge plus), recalculées chaque jour.</summary>
    private HashSet<int> _sedentaires = [];

    /// <summary>Kilos semés par are : graines à la volée (100 kg/ha), tubercules (1,5 t/ha), plants et noyaux.</summary>
    private static double SemenceParAre(Aliment a) => a.Sorte switch
    {
        SorteAliment.Graines or SorteAliment.Gousses => 1.0,
        SorteAliment.Tubercules => 15,
        _ => 0.3,
    };

    /// <summary>La saison (0 à 3).</summary>
    private static int SaisonDe(long maintenant) => (int)(maintenant % Calendrier.SecondesParAn / (Calendrier.JoursParSaison * Calendrier.SecondesParJour));

    /// <summary>
    /// L'espèce qu'il mange en cueillant sur la case <paramref name="i"/> aujourd'hui (0 : rien de nourrissant), tirée de la case et du
    /// jour sans toucher au hasard : celle qui est mûre ou qui abonde en ce moment.
    /// </summary>
    internal int EspeceCueillie(int i, long maintenant)
    {
        var liste = Flore.NourricieresSur(carte.BiomeEn(i % carte.Largeur, i / carte.Largeur));
        if (liste.Count == 0) return 0;
        var jour = (ulong)(maintenant / Calendrier.SecondesParJour / 9);
        var etat = carte.Graine ^ ((ulong)i * 0x9E3779B97F4A7C15UL) ^ (jour * 0xC2B2AE3D27D4EB4FUL);
        return liste[(int)(Alea.SplitMix64(ref etat) % (ulong)liste.Count)].Numero;
    }

    /// <summary>Le savoir de semer cette espèce, s'il l'a.</summary>
    private static Savoir? SavoirDuSemis(Humain h, int espece) => h.Savoirs.Find(s => s.Effet == Effet.Semis && s.Espece == espece);

    /// <summary>
    /// Peut-il prendre dans cette parcelle sans voler ? La sienne, celle d'un proche ou d'un mort ; celle d'un des siens si le groupe
    /// tient les champs pour communs, ou s'il n'en a pas d'idée et que le semeur ne lui en veut pas. Ailleurs, c'est voler.
    /// </summary>
    internal bool PermisAuChamp(Humain h, Parcelle p, IReadOnlyList<Humain> tous)
    {
        if (p.SemeurId == h.Id || p.SemeurId <= 0 || p.SemeurId > tous.Count) return true;
        var semeur = tous[p.SemeurId - 1];
        if (!semeur.EstVivant || semeur.Id == h.PartenaireId || SontParents(semeur, h) || semeur.AffectionPour(h.Id) >= 30) return true;
        if (h.CommunauteId == 0 || h.CommunauteId != p.CommunauteId) return false;
        var idee = Champs.IdeePour(p.CommunauteId);
        return idee == IdeeReserve.Commune || idee == IdeeReserve.Aucune && semeur.AffectionPour(h.Id) >= -10;
    }

    /// <summary>
    /// Au calme : aller récolter ce qui est mûr, arracher les herbes d'une parcelle, garder ses semis, ou semer. Vrai s'il s'y est mis.
    /// </summary>
    private bool Cultiver(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!AvecLeRelief || h.EstEnfant(_maintenant) || h.CaptifDe != 0 || h.Activite == Activite.Dormir) return false;
        var parcelles = Champs.Parcelles;
        var sait = h.Savoirs.Exists(s => s.Effet is Effet.Semis or Effet.Repousse);
        if (!sait && parcelles.Count == 0) return false;
        // Sur une parcelle des siens : on la garde des bêtes, rien qu'en étant là.
        if (parcelles.Count > 0) Garder(h);
        // Déjà en route vers une parcelle : on continue.
        var enRoute = h.Activite == Activite.Cultiver && h.CibleX >= 0;
        if (!enRoute && alea.Reel() >= 0.25) return false;
        if (parcelles.Count > 0 && Soigner(h, tous)) return true;
        return sait && Semer(h, tous);
    }

    /// <summary>Il est sur une case où les siens ont semé : sa présence écarte les bêtes.</summary>
    private void Garder(Humain h)
    {
        // Par indice : hors d'un pas simultané, ce qui est différé s'applique tout de suite.
        for (var k = 0; k < Champs.Parcelles.Count; k++)
        {
            var p = Champs.Parcelles[k];
            if (p.X != h.X || p.Y != h.Y || p.Etat is EtatParcelle.Finie || p.VueLe >= _maintenant - Calendrier.SecondesParJour / 2) continue;
            if (p.SemeurId != h.Id && (h.CommunauteId == 0 || p.CommunauteId != h.CommunauteId)) continue;
            var (id, le, champs) = (p.Id, _maintenant, Champs);
            Tour.Differer(() => { if (champs.Parcelle(id) is { } q) champs.Changer(q with { VueLe = Math.Max(q.VueLe, le) }); });
        }
    }

    /// <summary>
    /// La parcelle la plus proche qui l'appelle : mûre à récolter (jusqu'à deux journées de marche pour la sienne ou celle des siens),
    /// ou étouffée d'herbes, s'il sait sarcler. Vrai s'il y travaille ou s'y rend.
    /// </summary>
    private bool Soigner(Humain h, IReadOnlyList<Humain> tous)
    {
        var sarcle = h.Savoirs.Find(s => s.Effet == Effet.Sarclage) is { Confiance: >= 0.4 };
        Parcelle? cible = null;
        var meilleure = int.MaxValue;
        var portee = RayonPerception(h);
        foreach (var p in Champs.Parcelles)
        {
            if (p.Etat is EtatParcelle.EnTerre or EtatParcelle.Finie) continue;
            var siens = p.SemeurId == h.Id || h.CommunauteId != 0 && p.CommunauteId == h.CommunauteId || p.SemeurId == h.PartenaireId;
            var d = Distance(h.X, h.Y, p.X, p.Y);
            if (d >= meilleure || d > (siens ? 40 : portee)) continue;
            var interet = p.Etat == EtatParcelle.Mure ? p.SurPied >= 0.5
                : (sarcle || p.SemeurId == h.Id) && p.Herbes >= 0.3 && siens;
            if (!interet) continue;
            // Prendre chez un autre groupe, c'est voler : seulement affamé et tenté.
            if (p.Etat == EtatParcelle.Mure && !PermisAuChamp(h, p, tous) && (h.Faim < 55 || TentationDeVoler(h) <= 0)) continue;
            (cible, meilleure) = (p, d);
        }
        if (cible is null) return false;
        if (meilleure > 0)
        {
            _motif = Motif.Souvenir;
            Avancer(h, cible.X, cible.Y, Activite.Cultiver);
            return h.CibleX >= 0;
        }
        h.Activite = Activite.Cultiver;
        h.CibleX = h.CibleY = -1;
        if (cible.Etat == EtatParcelle.Mure) Recolter(h, cible, tous);
        else Sarcler(h, cible, sarcle, tous);
        return true;
    }

    /// <summary>
    /// Il arrache les herbes qui étouffent la parcelle ; s'il ne le savait pas, la voyant envahie, il peut avoir l'idée de le faire.
    /// Un adulte sarcle environ un are à l'heure à la main.
    /// </summary>
    private void Sarcler(Humain h, Parcelle p, bool sait, IReadOnlyList<Humain> tous)
    {
        if (!sait)
        {
            if (p.Herbes < 0.5 || SavoirDuSarclage(h) is not null) return;
            var chance = 0.02 * (0.3 + Croyances.Analytique(h)) * (0.5 + h.Personnalite.Conscience);
            if (alea.Reel() >= chance) return;
            Retenir(h, Geste.Gratter, Matiere.Plante, Matiere.Aucune, Effet.Sarclage, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
                recit: new Recit("champ.sarcler.idee", ("h", h)));
        }
        var arrache = Math.Min(1, 0.17 / Math.Max(0.1, p.Ares)) * (0.6 + 0.6 * h.Personnalite.Conscience);
        var (id, champs, le) = (p.Id, Champs, _maintenant);
        Tour.Differer(() =>
        {
            if (champs.Parcelle(id) is { Etat: EtatParcelle.Pousse } q) champs.Changer(q with { Herbes = q.Herbes * (1 - Math.Min(1, arrache)), Sarclee = true, VueLe = le });
        });
        var evt = Retenir(h, Geste.Gratter, Matiere.Plante, Matiere.Aucune, Effet.Sarclage, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        Montrer(h, Geste.Gratter, Matiere.Plante, Matiere.Aucune, Effet.Sarclage, 0, Symptome.Aucun, tous, evt);
    }

    private static Savoir? SavoirDuSarclage(Humain h) => h.Savoirs.Find(s => s.Effet == Effet.Sarclage);

    /// <summary>
    /// La façon de récolter qu'il connaît le mieux, ou, la première fois, celle que ses mains et ses outils lui inspirent : secouer
    /// les épis dans un récipient, les arracher, ou couper les tiges d'un éclat tranchant.
    /// </summary>
    private Geste FaconDeRecolter(Humain h)
    {
        Savoir? meilleur = null;
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Moisson || s.Geste == Geste.Gratter && !APortee(h, Matiere.Eclat)) continue;
            if (meilleur is null || s.Confiance > meilleur.Confiance) meilleur = s;
        }
        // Un savoir dont on doute laisse essayer autre chose.
        if (meilleur is { Confiance: >= 0.45 } || meilleur is not null && alea.Reel() >= 0.1) return meilleur.Geste;
        var p = h.Personnalite;
        if (APortee(h, Matiere.Eclat) && alea.Reel() < 0.2 + 0.4 * p.Adresse) return Geste.Gratter;
        var recipient = h.Objets.Exists(o => o.Matiere is Matiere.Calebasse or Matiere.Peau) || APortee(h, Matiere.GrandesFeuilles);
        return recipient && alea.Reel() < 0.7 ? Geste.Frapper : Geste.Tordre;
    }

    /// <summary>
    /// Il récolte un moment (dix minutes) sur une parcelle mûre. Ce qu'il prend dépend de la façon : secouer ne fait tomber que les
    /// graines prêtes à se détacher ; arracher ou couper prend les tiges entières, avec les graines qui tiennent. Il mange s'il a
    /// faim, met le reste en réserve, et, s'il sait garder la semence, en met une part de côté pour semer.
    /// </summary>
    internal void Recolter(Humain h, Parcelle p, IReadOnlyList<Humain> tous)
    {
        if (Champs.Lignee(p.LigneeId) is not { } l || Flore.Get(p.Espece).Aliment is not { } a) return;
        var pers = h.Personnalite;
        var bras = Sequelles.Gene(h, Membre.Bras, _maintenant);
        double tenaces, cassantes;
        Geste facon;
        if (a.SeDisperse)
        {
            facon = FaconDeRecolter(h);
            // Une à deux heures de travail pour un kilo de grain sauvage à la main (Harlan, 1967), plus vite à l'éclat tranchant.
            var force = (0.6 + 0.6 * pers.Adresse) * (1 - 0.4 * bras);
            var total = p.SurPied;
            if (facon == Geste.Frapper)
            {
                // Secouer ou battre les épis au-dessus d'un panier : tombe ce qui se détache seul.
                cassantes = Math.Min(p.SurPiedCassantes, 0.35 * force);
                tenaces = Math.Min(p.SurPiedTenaces, 0.03 * force);
            }
            else
            {
                var kg = Math.Min(total, (facon == Geste.Gratter ? 0.35 : 0.22) * force);
                tenaces = total <= 0 ? 0 : kg * p.SurPiedTenaces / total;
                cassantes = kg - tenaces;
            }
        }
        else
        {
            facon = a.Sorte == SorteAliment.Tubercules ? Geste.Fouir : Geste.Tordre;
            var kg = Math.Min(p.SurPiedTenaces, (a.Sorte switch { SorteAliment.Tubercules => 2.0, SorteAliment.Fruits => 1.5, _ => 0.8 }) * (0.7 + 0.5 * pers.Adresse) * (1 - 0.4 * bras));
            (tenaces, cassantes) = (kg, 0);
        }
        var pris = tenaces + cassantes;
        var permis = PermisAuChamp(h, p, tous);
        var (id, champs, le, premiere) = (p.Id, Champs, _maintenant, p.RecolteTenaces + p.RecolteCassantes <= 0);
        Tour.Differer(() =>
        {
            if (champs.Parcelle(id) is not { } q) return;
            champs.Changer(q with
            {
                SurPiedTenaces = Math.Max(0, q.SurPiedTenaces - tenaces), SurPiedCassantes = Math.Max(0, q.SurPiedCassantes - cassantes),
                RecolteTenaces = q.RecolteTenaces + tenaces, RecolteCassantes = q.RecolteCassantes + cassantes, VueLe = le,
            });
        });
        if (a.SeDisperse)
        {
            // Une poignée pour dix minutes de peine : la façon déçoit, et l'on doute.
            var evt = Retenir(h, facon, Matiere.Plante, facon == Geste.Gratter ? Matiere.Eclat : Matiere.Aucune, Effet.Moisson, 0, Symptome.Aucun,
                OrigineSavoir.Essai, 0, tous, succes: pris >= 0.12);
            Montrer(h, facon, Matiere.Plante, facon == Geste.Gratter ? Matiere.Eclat : Matiere.Aucune, Effet.Moisson, 0, Symptome.Aucun, tous, evt);
        }
        if (pris <= 0) return;
        if (premiere && p.CommunauteId != 0 && (p.SemeurId == h.Id || permis))
            Noter(_maintenant, TypeEvenement.Decouverte, h, null, new Recit("champ.recolte", ("h", h), ("plante", Flore.Get(p.Espece).Dit)), p.EvenementId);
        if (!permis)
        {
            // Pris en cachette : il mange ce qu'il a pris, et ne garde rien.
            h.Faim = Math.Max(0, h.Faim - pris * a.FaimParKg);
            VolerDansLeChamp(h, p, tous);
            return;
        }
        if (p.SemeurId != h.Id && p.SemeurId > 0 && h.CommunauteId != 0 && p.CommunauteId == h.CommunauteId)
        {
            var c = h.CommunauteId;
            Tour.Differer(() => champs.IdeeDe(c).Partages++);
        }
        // Ce qu'on garde pour semer : une part, si l'on sait qu'il faut garder de la semence.
        var garde = 0.0;
        if (h.Savoirs.Find(s => s.Effet == Effet.Semence) is { Confiance: >= 0.4 })
        {
            garde = pris * (0.12 + 0.15 * pers.Conscience);
            GarderLaSemence(h, p, l, a, tenaces, cassantes, garde, premiere);
            var evt = Retenir(h, Geste.Amasser, Matiere.Plante, Matiere.Aucune, Effet.Semence, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
            Montrer(h, Geste.Amasser, Matiere.Plante, Matiere.Aucune, Effet.Semence, 0, Symptome.Aucun, tous, evt);
        }
        var reste = pris - garde;
        // L'amer se mange cuit ou trempé ; cru, on n'en avale qu'une part.
        var amer = l.Amertume(a);
        var cuit = h.Sait(Effet.Cuisson) || h.Sait(Effet.Transformation);
        var valeur = a.FaimParKg * (amer > 0.4 && !cuit ? 0.4 : 1);
        if (h.Faim > 20 && valeur > 0)
        {
            var mange = Math.Min(reste, h.Faim / valeur);
            h.Faim = Math.Max(0, h.Faim - mange * valeur);
            h.PartDesChamps = Math.Min(1, h.PartDesChamps + 0.02 * mange * valeur / FaimParJour * 10);
            reste -= mange;
        }
        if (reste < 0.2) return;
        var sorte = a.Sorte switch
        {
            SorteAliment.Graines or SorteAliment.Gousses => SorteReserve.Grains,
            SorteAliment.Tubercules => SorteReserve.Tubercules,
            SorteAliment.Noix => SorteReserve.NoixGraines,
            _ => (SorteReserve?)null,
        };
        if (sorte is null) return;
        var kgReserve = reste * a.FaimParKg * (amer > 0.4 && !cuit ? 0.4 : 1) / Reserve.FaimParKg(sorte.Value);
        var qualite = Math.Clamp(0.4 + 0.5 * pers.Conscience * (0.5 + 0.5 * alea.Reel()), 0, 1);
        Poser(h, sorte.Value, kgReserve, qualite, Geste.Amasser, Matiere.Plante, Effet.Provisions, tous);
        h.Activite = Activite.Cultiver;
    }

    /// <summary>
    /// Il met de côté une part de sa récolte pour semer. Ce qu'il garde porte la façon dont il a récolté : la lignée prend la part
    /// de graines tenaces de ce qu'il a pris. Les boutures, elles, sont choisies parmi les plus belles et les moins amères.
    /// </summary>
    private void GarderLaSemence(Humain h, Parcelle p, Lignee l, Aliment a, double tenaces, double cassantes, double garde, bool premiere)
    {
        var pers = h.Personnalite;
        // L'œil pour choisir : soin et sens des formes.
        var choix = 0.3 + 0.7 * Math.Clamp(0.5 * pers.Conscience + 0.5 * (pers.Intelligence.Spatiale - 70) / 60, 0, 1);
        var (lid, champs, gardien, le, profond) = (l.Id, Champs, h.Id, _maintenant, p.Profond);
        var part = tenaces + cassantes <= 0 ? l.Tenaces : tenaces / (tenaces + cassantes);
        Tour.Differer(() =>
        {
            if (champs.Lignee(lid) is not { } x) return;
            var total = x.Semence + garde;
            // Une génération par an : ce qu'on garde de dix parcelles la même saison est une seule génération.
            var nouvelle = premiere && le - x.ChoisieLe > Calendrier.SecondesParAn / 2;
            double Meler(double ancien, double neuf) => total <= 0 ? neuf : (ancien * x.Semence + neuf * garde) / total;
            Lignee n;
            if (a.SeDisperse)
            {
                // Des graines de plantes qui ont levé la première année : moins de dormantes. Semées profond : les grosses l'emportent.
                var grosseur = nouvelle ? x.Grosseur + (profond ? 0.015 : 0.002) * Math.Max(0, 1.8 - x.Grosseur) / 0.8 : x.Grosseur;
                var dormance = nouvelle ? x.Dormance * 0.85 : x.Dormance;
                n = x with
                {
                    Tenaces = Meler(x.Tenaces, part), Grosseur = Meler(x.Grosseur, grosseur), Dormance = Meler(x.Dormance, dormance),
                    Vigueur = 1 + 0.6 * (Meler(x.Grosseur, grosseur) - 1),
                };
            }
            else
            {
                // La bouture est la même plante : choisir la plus belle avance vite ; une graine d'arbre, bien moins.
                var vite = a.Bouture ? 1.0 : 0.25;
                n = nouvelle
                    ? x with
                    {
                        Vigueur = x.Vigueur + 0.04 * choix * vite * Math.Max(0, 3 - x.Vigueur) / 2,
                        Douceur = x.Douceur + 0.05 * choix * vite * (1 - x.Douceur),
                    }
                    : x;
            }
            n = n with { Semence = total, GardienId = gardien, Generations = x.Generations + (nouvelle ? 1 : 0), ChoisieLe = nouvelle ? le : x.ChoisieLe };
            champs.Changer(n);
        });
    }

    /// <summary>Il a pris en cachette dans la parcelle d'un autre : comme un vol dans une réserve.</summary>
    private void VolerDansLeChamp(Humain h, Parcelle p, IReadOnlyList<Humain> tous)
    {
        if (p.SemeurId <= 0 || p.SemeurId > tous.Count) return;
        var semeur = tous[p.SemeurId - 1];
        var (champs, c, ih, iv, le, cause) = (Champs, p.CommunauteId, h.Id, semeur.Id, _maintenant, p.EvenementId);
        Tour.Differer(tous, t =>
        {
            var idee = c != 0 ? champs.IdeeDe(c) : null;
            if (idee is not null) idee.Vols++;
            var id = Surprendre(t[ih - 1], t[iv - 1], c, Recit.Seul("champ.quoi"), cause, t, le);
            if (idee is not null && id != 0) idee.VolEvenementId = id;
        });
    }

    /// <summary>
    /// Semer : il a de quoi (semence gardée des siens, ou graines sauvages mûres de l'espèce qu'il sait semer) et un lieu qui s'y
    /// prête (près du camp, le limon d'une crue retirée, une terre brûlée). La saison se choisit d'après ce que les siens en ont
    /// appris. Vrai s'il a semé.
    /// </summary>
    private bool Semer(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim > 60 || h.CibleX >= 0) return false;
        var i = carte.Indice(h.X, h.Y);
        if (!Climat.EstPraticable(carte.BiomeEn(h.X, h.Y)) || carte.Plantes[i] == 0 || Catastrophes?.EstDangereuse(i) == true) return false;
        // Le lieu : le limon d'une crue retirée, une terre brûlée, ou tout près du camp.
        var limon = ecosysteme.Limon[i] >= LimonVisible && SavoirDuLimon(h) is { Confiance: >= 0.5 };
        var cendres = ecosysteme.Degats[i] is > 300 and < 8000 && ecosysteme.Limon[i] == 0;
        var essart = cendres && h.Savoirs.Find(s => s.Effet == Effet.Essart) is { Confiance: >= 0.5 };
        var camp = Social?.CommunauteDe(h) is { CentreX: >= 0 } c ? Distance(c.CentreX, c.CentreY, h.X, h.Y) <= 3 : DansSonCamp(h);
        if (!limon && !essart && !camp) return false;
        // L'espèce : celle qu'il sait semer, dont il a de la semence ou dont les graines sauvages sont mûres ici.
        var saison = SaisonDe(_maintenant);
        Savoir? semis = null;
        Lignee? lignee = null;
        var sauvage = false;
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Semis || s.Confiance < 0.4 || Flore.Get(s.Espece).Aliment is not { } a) continue;
            var l = LigneeDe(h, s.Espece);
            var reserve = l is { Semence: > 0 } && l.Semence >= SemenceParAre(a) * 0.2;
            var ici = !reserve && Murit(a, saison) && Flore.NourricieresSur(carte.BiomeEn(h.X, h.Y)).Any(e => e.Numero == s.Espece);
            if (!reserve && !ici) continue;
            // Ce que les siens savent des saisons : on ne sème pas là où l'on a échoué, sauf pour voir.
            var score = l?.Saisons[saison] ?? 0;
            if (score < 0 && alea.Reel() >= 0.05 || saison == 3 && score <= 0 && alea.Reel() >= 0.2) continue;
            if (semis is null || s.Confiance > semis.Confiance) (semis, lignee, sauvage) = (s, l, !reserve);
        }
        if (semis is null)
        {
            PenserALaSemence(h, tous);
            return false;
        }
        // Assez semé pour les siens : environ dix ares par bouche, d'autant plus qu'il y croit.
        var bouches = 1.0 + (Vivant(tous, h.PartenaireId) is not null ? 1 : 0);
        foreach (var e in tous.EnfantsDe(h.Id)) if (e.EstVivant && e.EstEnfant(_maintenant)) bouches += 0.6;
        var siens = 0.0;
        foreach (var p in Champs.Parcelles) if (p.SemeurId == h.Id && p.Etat is EtatParcelle.EnTerre or EtatParcelle.Pousse) siens += p.Ares;
        if (siens >= 10 * bouches * semis.Confiance) return false;
        if (alea.Reel() >= 0.15 * (0.5 + h.Personnalite.Conscience)) return false;

        var aliment = Flore.Get(semis.Espece).Aliment!;
        // Dix minutes : un demi-are à la volée, un peu moins en enfonçant les graines au bâton.
        var profond = aliment.SeDisperse && (h.Sait(Effet.Tubercules) || APortee(h, Matiere.BatonPointu)) && alea.Reel() < 0.3 + 0.5 * h.Personnalite.Conscience;
        var ares = (profond ? 0.3 : 0.5) * (aliment.SeDisperse ? 1 : 0.6);
        var kg = ares * SemenceParAre(aliment);
        if (!sauvage && lignee is not null) kg = Math.Min(kg, lignee.Semence);
        ares = kg / SemenceParAre(aliment);
        var part = sauvage || lignee is null ? aliment.Tenaces : lignee.Tenaces;
        if (sauvage && lignee is { } l0) part = (l0.Tenaces * l0.Semence + aliment.Tenaces * kg) / Math.Max(1e-9, l0.Semence + kg);
        h.Activite = Activite.Cultiver;
        h.CibleX = h.CibleY = -1;
        var gainCendres = essart || cendres ? 0.3 + 0.4 * Math.Min(1, ecosysteme.Degats[i] / 5000.0) : 0;
        var gainLimon = limon ? ecosysteme.Limon[i] / 10_000.0 : 0;
        // Les premières fois se racontent ; ensuite, c'est un geste de saison.
        long evt = 0;
        if (semis.Essais < 3 || limon && semis.Essais < 6)
            evt = Noter(_maintenant, TypeEvenement.Decouverte, h, null,
                new Recit(limon ? "champ.seme.limon" : essart ? "champ.seme.cendres" : "champ.seme", ("h", h), ("plante", Flore.Get(semis.Espece).Dit)),
                semis.EvenementId);
        var (x, y, ih, communaute, le, champs, espece, lid) = (h.X, h.Y, h.Id, h.CommunauteId, _maintenant, Champs, semis.Espece, lignee?.Id ?? 0);
        var flore = Flore;
        var preleve = !sauvage;
        Tour.Differer(() =>
        {
            var l = lid > 0 ? champs.Lignee(lid) : null;
            l ??= champs.NouvelleLignee(flore.Get(espece), communaute, le, ih, evt);
            if (preleve) l = l with { Semence = Math.Max(0, l.Semence - kg) };
            champs.Changer(l with { SemeeLe = le });
            champs.Semer(x, y, l, ares, part, ih, communaute, le, gainCendres, gainLimon, profond, evt);
        });
        Retenir(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Semis, semis.Espece, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        Montrer(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Semis, semis.Espece, Symptome.Aucun, tous, evt);
        // Semer sur le limon fait vivre le savoir de la crue.
        if (limon && SavoirDuLimon(h) is { } sl) h.Savoirs[h.Savoirs.IndexOf(sl)] = sl with { UtiliseLe = _maintenant };
        return true;
    }

    /// <summary>Hors d'une communauté : son camp, c'est là où il dort d'ordinaire (le lieu d'abri dont il se souvient le mieux).</summary>
    private bool DansSonCamp(Humain h) => h.Memoire.PlusProche(TypeLieu.Abri, h.X, h.Y, _maintenant) is { } l && Distance(l.X, l.Y, h.X, h.Y) <= 1;

    /// <summary>La lignée de cette espèce que sa communauté (ou lui seul) sème déjà, la mieux pourvue en semence.</summary>
    private Lignee? LigneeDe(Humain h, int espece)
    {
        Lignee? choisie = null;
        foreach (var l in Champs.Lignees)
        {
            if (l.Espece != espece) continue;
            if (h.CommunauteId != 0 ? l.CommunauteId != h.CommunauteId : l.GardienId != h.Id) continue;
            if (choisie is null || l.Semence > choisie.Semence) choisie = l;
        }
        return choisie;
    }

    /// <summary>Les graines sauvages (ou les fruits) sont-ils mûrs en cette saison ? Racines et boutures se prennent hors de l'hiver.</summary>
    private static bool Murit(Aliment a, int saison) => a.Sorte switch
    {
        SorteAliment.Tubercules => saison != 3,
        _ => saison is 1 or 2,
    };

    /// <summary>
    /// Il voudrait semer et n'a plus de graines : tout a été mangé pendant l'hiver. Il peut se dire qu'il fallait en garder.
    /// </summary>
    private void PenserALaSemence(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Sait(Effet.Semence) || SaisonDe(_maintenant) is 1 or 2) return;
        var regrette = false;
        foreach (var s in h.Savoirs)
        {
            if (s.Effet != Effet.Semis || s.Confiance < 0.4) continue;
            if (LigneeDe(h, s.Espece) is { Semence: <= 0, Generations: >= 0 } l && l.SemeeLe < _maintenant - Calendrier.SecondesParAn / 2) { regrette = true; break; }
        }
        if (!regrette) return;
        var p = h.Personnalite;
        if (alea.Reel() >= 0.002 * (0.3 + Croyances.Analytique(h)) * (0.5 + p.Conscience)) return;
        Retenir(h, Geste.Amasser, Matiere.Plante, Matiere.Aucune, Effet.Semence, 0, Symptome.Aucun, OrigineSavoir.Reflexion, 0, tous,
            recit: new Recit("champ.semence.idee", ("h", h)));
    }

    /// <summary>
    /// Il a enfoui quelques graines de ce qu'il mangeait (voir <see cref="Enfouir"/>) : s'il s'agit d'une plante qui nourrit, elles
    /// font une toute petite parcelle, qu'il retrouvera peut-être levée.
    /// </summary>
    private void EnfouirUneEspece(Humain h, int i)
    {
        var espece = EspeceCueillie(i, _maintenant);
        if (espece == 0 || Flore.Get(espece).Aliment is not { } a || !Murit(a, SaisonDe(_maintenant))) return;
        var ares = 0.05 + 0.1 * alea.Reel();
        var (x, y, ih, communaute, le, champs, flore) = (h.X, h.Y, h.Id, h.CommunauteId, _maintenant, Champs, Flore);
        var limon = ecosysteme.Limon[i] / 10_000.0;
        Tour.Differer(() =>
        {
            Lignee? l = null;
            foreach (var x0 in champs.Lignees)
                if (x0.Espece == espece && (communaute != 0 ? x0.CommunauteId == communaute : x0.GardienId == ih)) { l = x0; break; }
            l ??= champs.NouvelleLignee(flore.Get(espece), communaute, le, ih, 0);
            champs.Semer(x, y, l, ares, flore.Get(espece).Aliment!.Tenaces, ih, communaute, le, 0, limon, false, 0);
        });
    }

    /// <summary>
    /// Une fois par jour, ce qui pousse dans les parcelles : levée selon la saison et la dormance, croissance selon les pluies, herbes,
    /// bêtes qu'on ne chasse pas, gel, soif ; à maturité, les graines qui ne tiennent pas tombent chaque jour. Ensuite, ceux qui ont
    /// semé constatent ce qu'il en est advenu.
    /// </summary>
    internal void JourneeDesChamps(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (!AvecLeRelief || Champs.Vide) return;
        var saisonF = Ecosysteme.FacteurSaison(maintenant);
        var midi = maintenant - maintenant % Calendrier.SecondesParJour + 12 * Calendrier.SecondesParHeure;
        var issues = new List<(Parcelle P, IssueParcelle Issue)>();
        var sedentaires = new HashSet<int>();
        const double jour = Calendrier.SecondesParJour;
        for (var k = 0; k < Champs.Parcelles.Count; k++)
        {
            var p = Champs.Parcelles[k];
            if (p.Etat == EtatParcelle.Finie) continue;
            var a = Flore.Get(p.Espece).Aliment;
            var l = Champs.Lignee(p.LigneeId);
            if (a is null || l is null)
            {
                Champs.Parcelles[k] = p with { Etat = EtatParcelle.Finie };
                continue;
            }
            var i = carte.Indice(p.X, p.Y);
            var temperature = Temperature(p.X, p.Y, midi);
            var pluie = Ecosysteme.EffetPluie(ecosysteme.PluieEn(i));
            var arrose = carte.BiomeEn(p.X, p.Y) == Biome.Riviere || DistanceEau(p.X, p.Y) <= 1;
            var garde = maintenant - p.VueLe < 2 * jour;
            var capacite = carte.Gibier[i] * 100.0;
            var betes = capacite <= 0 ? 0 : Math.Min(1, ecosysteme.Gibier[i] / capacite);
            var jours = (maintenant - p.SemeeLe) / jour;
            if (p.CommunauteId != 0 && p.Etat != EtatParcelle.EnTerre && Social is { } so && p.CommunauteId <= so.Communautes.Count
                && so.Communautes[p.CommunauteId - 1] is { Active: true } cc && Distance(cc.CentreX, cc.CentreY, p.X, p.Y) <= 3)
                sedentaires.Add(p.CommunauteId);
            // Une plante vivace que plus personne ne vient voir depuis deux ans retourne aux herbes.
            if (a.Vivace && maintenant - p.VueLe > 2 * Calendrier.SecondesParAn)
            {
                Champs.Parcelles[k] = p with { Etat = EtatParcelle.Finie, MureLe = maintenant };
                ecosysteme.Germer(i, Math.Min(0.1, p.Ares / 200));
                issues.Add((p, IssueParcelle.Oubliee));
                continue;
            }
            switch (p.Etat)
            {
                case EtatParcelle.EnTerre:
                {
                    var sante = p.Sante;
                    if (temperature < 6 || saisonF < 0.6)
                    {
                        // Trop froid pour lever : la graine attend, et les rongeurs, les oiseaux, la pourriture en prennent.
                        sante -= 0.004;
                    }
                    else if (jours >= 8 + 8 * (1.3 - pluie))
                    {
                        // Ce qui dort ne lève pas cette année.
                        var levee = a.SeDisperse || !a.Bouture ? 1 - 0.8 * l.Dormance : 1;
                        Champs.Parcelles[k] = p with { Etat = EtatParcelle.Pousse, Sante = sante * levee, Croissance = 0 };
                        issues.Add((p, IssueParcelle.Levee));
                        break;
                    }
                    if (sante < 0.05 || jours > 200)
                    {
                        Champs.Parcelles[k] = p with { Etat = EtatParcelle.Finie, Sante = 0 };
                        issues.Add((p, IssueParcelle.Perdue));
                    }
                    else Champs.Parcelles[k] = p with { Sante = sante };
                    break;
                }
                case EtatParcelle.Pousse:
                {
                    // L'hiver, la plante vivace dort ; la plante de l'année gèle.
                    if (a.Vivace && saisonF < 0.6) break;
                    var sante = p.Sante;
                    if (!a.Vivace && temperature < 0) sante -= 0.03;
                    var duree = a.Maturite + (p.Annees == 0 ? Calendrier.JoursParAn * a.AnsAvantDeDonner : 0);
                    var croissance = p.Croissance + saisonF * Math.Clamp(pluie, 0.5, 1.2) / duree;
                    var herbes = Math.Min(1, p.Herbes + 0.012 * ecosysteme.Sol(i) * saisonF * (1 - p.Herbes + 0.05));
                    sante -= 0.004 * betes * (garde ? 0.25 : 1);
                    if (!arrose && ecosysteme.PluieEn(i) < 0.75) sante -= 0.004 * (0.75 - ecosysteme.PluieEn(i)) / 0.4;
                    if (sante < 0.05)
                    {
                        Champs.Parcelles[k] = p with { Etat = EtatParcelle.Finie, Sante = 0 };
                        issues.Add((p, IssueParcelle.Perdue));
                        break;
                    }
                    if (croissance < 1)
                    {
                        Champs.Parcelles[k] = p with { Croissance = croissance, Sante = sante, Herbes = herbes };
                        break;
                    }
                    // Mûre : la récolte qu'elle porte.
                    var kg = p.Ares / 100 * a.Rendement * l.Vigueur * (a.SeDisperse ? Math.Pow(l.Grosseur, 0.7) : 1) * sante * (1 - 0.7 * herbes)
                             * ecosysteme.Sol(i) * (1 + p.Limon) * (1 + p.Cendres) * Math.Clamp(pluie, 0.5, 1.2);
                    var tenaces = a.SeDisperse ? kg * p.Part : kg;
                    Champs.Parcelles[k] = p with
                    {
                        Etat = EtatParcelle.Mure, Croissance = 1, Sante = sante, Herbes = herbes, SurPiedTenaces = tenaces, SurPiedCassantes = kg - tenaces,
                        Prevue = kg, MureLe = maintenant, RecolteTenaces = 0, RecolteCassantes = 0,
                    };
                    issues.Add((Champs.Parcelles[k], IssueParcelle.Mure));
                    break;
                }
                case EtatParcelle.Mure:
                {
                    var depuis = (maintenant - p.MureLe) / jour;
                    var (ten, cas, tombees) = (p.SurPiedTenaces, p.SurPiedCassantes, p.Tombees);
                    if (a.SeDisperse)
                    {
                        var chute = cas * a.Egrenage / 7;
                        (cas, tombees) = (cas - chute, tombees + chute);
                        ten *= 0.995;
                    }
                    else ten *= a.Sorte switch
                    {
                        SorteAliment.Tubercules => 0.998,
                        SorteAliment.Fruits => depuis > 20 ? 0.96 : 0.995,
                        _ => depuis > 30 ? 0.98 : 0.997,
                    };
                    var mange = 1 - 0.01 * betes * (garde ? 0.3 : 1);
                    (ten, cas) = (ten * mange, cas * mange);
                    var fin = depuis > (a.Sorte == SorteAliment.Tubercules ? 150 : a.SeDisperse ? 60 : 45) || ten + cas < 0.03 * p.Prevue;
                    if (!fin)
                    {
                        Champs.Parcelles[k] = p with { SurPiedTenaces = ten, SurPiedCassantes = cas, Tombees = tombees };
                        break;
                    }
                    var recoltee = p.RecolteTenaces + p.RecolteCassantes > 0;
                    if (a.Vivace)
                    {
                        // La plante vivace repart à la belle saison suivante.
                        Champs.Parcelles[k] = p with
                        {
                            Etat = EtatParcelle.Pousse, Croissance = 0, SurPiedTenaces = 0, SurPiedCassantes = 0, Tombees = tombees, Annees = p.Annees + 1,
                            Herbes = p.Herbes * 0.5, Sarclee = false, Sante = Math.Max(p.Sante, 0.6),
                        };
                    }
                    else
                    {
                        Champs.Parcelles[k] = p with { Etat = EtatParcelle.Finie, SurPiedTenaces = 0, SurPiedCassantes = 0, Tombees = tombees + ten + cas };
                        // Ce qui est tombé relève l'an prochain, sauvage, parmi les herbes.
                        ecosysteme.Germer(i, Math.Min(0.08, (tombees + ten + cas) / Math.Max(1, p.Ares) / 50));
                    }
                    if (!recoltee) issues.Add((p, IssueParcelle.Oubliee));
                    break;
                }
            }
        }
        _sedentaires = sedentaires;
        // La semence se garde mal : les rongeurs et l'humidité en prennent ; une bouture sèche en quelques mois.
        for (var k = Champs.Lignees.Count - 1; k >= 0; k--)
        {
            var l = Champs.Lignees[k];
            var a = Flore.Get(l.Espece).Aliment;
            var semence = l.Semence * (a is { Bouture: true } ? 0.99 : 0.9985);
            if (semence < 0.01) semence = 0;
            var vivante = semence > 0 || Champs.Parcelles.Exists(p => p.LigneeId == l.Id && p.Etat != EtatParcelle.Finie);
            if (!vivante && maintenant - l.SemeeLe > Calendrier.SecondesParAn) Champs.Lignees.RemoveAt(k);
            else if (semence != l.Semence) Champs.Lignees[k] = l with { Semence = semence };
        }
        Champs.Nettoyer(maintenant);
        foreach (var (p, issue) in issues) Constater(p, issue, humains, maintenant);
        Domestiquer(humains, maintenant);
    }

    /// <summary>
    /// Celui qui a semé apprend ce qu'il est advenu de sa parcelle, s'il est assez près pour le voir : levée, mûre (il comprend
    /// peut-être, la première fois, que ce qu'il avait enfoui a donné cela), ou perdue. Les siens en tirent la leçon des saisons.
    /// </summary>
    private void Constater(Parcelle p, IssueParcelle issue, IReadOnlyList<Humain> humains, long maintenant)
    {
        if (Champs.Lignee(p.LigneeId) is { } l && issue is IssueParcelle.Mure or IssueParcelle.Perdue)
        {
            var saisons = (int[])l.Saisons.Clone();
            saisons[p.Saison] = Math.Clamp(saisons[p.Saison] + (issue == IssueParcelle.Mure ? 1 : -1), -5, 5);
            Champs.Changer(l with { Saisons = saisons });
        }
        if (p.SemeurId <= 0 || p.SemeurId > humains.Count || humains[p.SemeurId - 1] is not { EstVivant: true } h) return;
        if (Distance(h.X, h.Y, p.X, p.Y) > 6) return;
        _maintenant = maintenant;
        _tous = humains;
        var sait = SavoirDuSemis(h, p.Espece);
        if (issue == IssueParcelle.Mure)
        {
            if (sait is null)
            {
                var chance = 0.5 * (0.3 + Croyances.Analytique(h)) * (0.5 + h.Personnalite.Ouverture);
                if (alea.Reel() >= chance) return;
                Retenir(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Semis, p.Espece, Symptome.Aucun, OrigineSavoir.Observation, 0, humains,
                    recit: new Recit("champ.constat", ("h", h), ("plante", Flore.Get(p.Espece).Dit)));
            }
            else Retenir(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Semis, p.Espece, Symptome.Aucun, OrigineSavoir.Observation, 0, humains);
            // Ce qui a poussé sur la cendre pousse fort : il peut faire le lien.
            if (p.Cendres >= 0.2 && !h.Sait(Effet.Essart) && alea.Reel() < 0.3 * (0.3 + Croyances.Analytique(h)))
                Retenir(h, Geste.Incendier, Matiere.Plante, Matiere.Aucune, Effet.Essart, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, humains,
                    recit: new Recit("champ.essart.constat", ("h", h)));
            else if (p.Cendres >= 0.2 && h.Sait(Effet.Essart))
                Retenir(h, Geste.Incendier, Matiere.Plante, Matiere.Aucune, Effet.Essart, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, humains);
            if (p.Sarclee && SavoirDuSarclage(h) is not null)
                Retenir(h, Geste.Gratter, Matiere.Plante, Matiere.Aucune, Effet.Sarclage, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, humains);
        }
        else if (issue == IssueParcelle.Perdue && sait is not null)
        {
            Retenir(h, Geste.Enfouir, Matiere.Plante, Matiere.Aucune, Effet.Semis, p.Espece, Symptome.Aucun, OrigineSavoir.Observation, 0, humains, succes: false);
            if (p.Ares >= 3)
                Noter(maintenant, TypeEvenement.Pensee, h, null, new Recit("champ.perdue", ("h", h), ("plante", Flore.Get(p.Espece).Dit)), p.EvenementId);
        }
    }

    /// <summary>
    /// Une lignée qui ne s'égrène presque plus (graines), ou bien plus grosse et douce que la plante sauvage (racines, fruits) : elle
    /// ne vivrait plus sans l'homme. Noté une fois au journal.
    /// </summary>
    private void Domestiquer(IReadOnlyList<Humain> humains, long maintenant)
    {
        for (var k = 0; k < Champs.Lignees.Count; k++)
        {
            var l = Champs.Lignees[k];
            if (l.Domestiquee || Flore.Get(l.Espece).Aliment is not { } a) continue;
            var faite = a.SeDisperse ? l.Tenaces >= 0.5 : l.Vigueur >= 1.8 && l.Douceur >= 0.5;
            if (!faite) continue;
            Champs.Lignees[k] = l with { Domestiquee = true };
            var gardien = l.GardienId > 0 && l.GardienId <= humains.Count ? humains[l.GardienId - 1] : null;
            if (gardien is null) continue;
            var e = new EvenementJournal(maintenant, TypeEvenement.Decouverte, gardien.Id, 0,
                new Recit(a.SeDisperse ? "champ.lignee.graines" : "champ.lignee.plants", ("plante", Flore.Get(l.Espece).Dit), ("h", gardien),
                    ("g", (long)l.Generations)))
            {
                Id = Numeros.Suivant(), CauseId = l.EvenementId, CommunauteId = l.CommunauteId, Significatif = true,
            };
            Journal.Add(e);
        }
    }

    /// <summary>
    /// Une fois par jour, ce que chaque communauté qui a des parcelles pense des champs : comme pour les réserves, après des
    /// récoltes partagées dans un groupe généreux, l'idée que les champs sont à tous ; après des vols, qu'ils sont à qui les a semés.
    /// </summary>
    internal void IdeesDesChamps(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (!AvecLeRelief || Social is not { } social || Champs.Idees.Count == 0) return;
        foreach (var (id, idee) in Champs.Idees.OrderBy(x => x.Key))
        {
            idee.Partages *= 0.99;
            idee.Vols *= 0.99;
            if (idee.Partages < 3 && idee.Vols < 1 || id < 1 || id > social.Communautes.Count || social.Communautes[id - 1] is not { Active: true } c) continue;
            double generosite = 0, confiance = 0;
            var n = 0;
            Humain? figure = null;
            foreach (var h in humains.Vivants())
            {
                if (!h.EstVivant || h.CommunauteId != c.Id || h.EstEnfant(maintenant)) continue;
                var p = h.Personnalite;
                generosite += (0.5 * p.Agreabilite + 0.25 * p.Moral.Compassion + 0.25 * p.Moral.Honnetete) * (1 - 0.8 * Psyche.Froideur(p));
                confiance += 0.5 * (p.Agreabilite + p.Moral.Honnetete);
                n++;
                if (h.Id == c.FigureId) figure = h;
            }
            if (n == 0) continue;
            (generosite, confiance) = (generosite / n, confiance / n);
            var aSoi = idee.Idee != IdeeReserve.ASoi && idee.Vols >= 1 && alea.Reel() < 0.02 * Math.Min(1, idee.Vols / 3) * Math.Max(0, 1 - confiance);
            var commune = !aSoi && idee.Idee != IdeeReserve.Commune && idee.Partages >= 3 && generosite > 0.3
                          && alea.Reel() < 0.01 * (generosite - 0.3) * Math.Min(1, idee.Partages / 10);
            if (!aSoi && !commune) continue;
            var avant = idee.Idee;
            idee.Idee = aSoi ? IdeeReserve.ASoi : IdeeReserve.Commune;
            idee.Le = maintenant;
            var texte = new Recit(aSoi ? "champ.idee.aSoi" : "champ.idee.commune", ("c", c),
                ("fin", avant == IdeeReserve.Aucune ? "" : Recit.Seul(aSoi ? "champ.idee.plusPartage" : "champ.idee.plusGarde")));
            var e = new EvenementJournal(maintenant, TypeEvenement.Communaute, figure?.Id ?? 0, 0, texte)
            {
                Id = Numeros.Suivant(), CauseId = aSoi ? idee.VolEvenementId : 0, CommunauteId = c.Id, Significatif = true,
            };
            Journal.Add(e);
            idee.EvenementId = e.Id;
        }
    }

    /// <summary>
    /// Ce que les parcelles des siens promettent, en jours de vivres pour tout le groupe : ce qui pousse près du camp et ce qui est
    /// mûr. Une communauté qui attend une récolte ne part pas au premier creux.
    /// </summary>
    public static double JoursDeRecolte(Champs champs, Flore flore, Communaute c, int membres)
    {
        if (membres <= 0 || champs.Parcelles.Count == 0) return 0;
        var vivres = 0.0;
        foreach (var p in champs.Parcelles)
        {
            if (p.CommunauteId != c.Id || p.Etat is EtatParcelle.EnTerre or EtatParcelle.Finie) continue;
            if (Math.Max(Math.Abs(p.X - c.CentreX), Math.Abs(p.Y - c.CentreY)) > VieSociale.DistanceCommunaute) continue;
            if (flore.Get(p.Espece).Aliment is not { } a) continue;
            var kg = p.Etat == EtatParcelle.Mure ? p.SurPied : p.Ares / 100 * a.Rendement * p.Sante * (1 - 0.7 * p.Herbes) * (champs.Lignee(p.LigneeId)?.Vigueur ?? 1);
            vivres += kg * a.FaimParKg;
        }
        return vivres / (FaimParJour * membres);
    }

    /// <summary>Vit-il dans une communauté qui ne bouge plus, près de ses parcelles ?</summary>
    private bool Sedentaire(Humain h) => h.CommunauteId != 0 && _sedentaires.Contains(h.CommunauteId);
}
