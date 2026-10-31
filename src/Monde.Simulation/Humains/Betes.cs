using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les bêtes gardées (mondes de la génération 2, depuis la 0.33.1). Deux chemins, ceux que Zeder (2012) tire de l'archéologie :
/// le commensal, où des rôdeurs attirés par les restes du camp s'enhardissent et laissent prendre leurs petits ; la proie, où le
/// chasseur rapporte le petit d'une bête qu'il a tuée. Rien n'est écrit : une espèce qui panique s'échappe, une agressive blesse,
/// une qui ne se reproduit pas captive ne donne jamais de troupeau. À chaque mise bas, on garde les plus calmes : la docilité
/// monte de génération en génération, plus vite chez les bêtes qui grandissent vite.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>La faune du monde (vide de sens hors de la génération 2).</summary>
    public Faune Faune { get; set; } = new(0);

    /// <summary>Communautés (ou gardiens seuls, en négatif) qui ont des chiens dociles : la chasse en profite.</summary>
    private HashSet<int> _chiens = [];

    /// <summary>Communautés qui vivent au milieu d'un troupeau : les maladies des bêtes passent plus souvent à l'homme.</summary>
    private HashSet<int> _eleveurs = [];

    /// <summary>Cases où un troupeau passe ses nuits : le fumier y engraisse les parcelles.</summary>
    private HashSet<int> _fumier = [];

    private const double DociliteDomestique = 0.6;

    /// <summary>Clé des chiens et des éleveurs : la communauté, ou le gardien seul (en négatif).</summary>
    private static int Maisonnee(int communaute, int gardien) => communaute != 0 ? communaute : -gardien;

    private static int Maisonnee(Humain h) => Maisonnee(h.CommunauteId, h.Id);

    /// <summary>Ses chiens l'aident à chasser : ils flairent, rabattent et retiennent la bête blessée (Lupo, 2017).</summary>
    private double AvecSesChiens(Humain h) => AvecLeRelief && _chiens.Contains(Maisonnee(h)) ? 1.3 : 1;

    /// <summary>Il vit au milieu des bêtes : le risque de maladie monte un peu (zoonoses ; Diamond, 1997).</summary>
    private double AuMilieuDesBetes(Humain h) => AvecLeRelief && _eleveurs.Contains(Maisonnee(h)) ? 1.12 : 1;

    /// <summary>
    /// Après une chasse réussie : une femelle tuée à la saison des naissances laisse parfois un petit. Le chasseur le rapporte s'il
    /// sait qu'on peut élever les petits des bêtes, ou si la pitié ou la curiosité l'emportent ; sinon on le mange.
    /// </summary>
    private void PrendreUnPetit(Humain h, int i, IReadOnlyList<Humain> tous)
    {
        if (!AvecLeRelief || h.Faim > 50) return;
        var saison = SaisonDe(_maintenant);
        if (saison > 1) return;
        var betes = Faune.Sur(carte.BiomeEn(h.X, h.Y));
        if (betes.Count == 0) return;
        var b = betes[alea.Entier(betes.Count)];
        if (b.Forme == 5 || alea.Reel() >= 0.03) return;
        var p = h.Personnalite;
        var sait = h.Savoirs.Find(s => s.Effet == Effet.Apprivoiser);
        var envie = sait is { Confiance: >= 0.4 } ? 0.3 : 0.08 * (0.5 + p.Agreabilite) * (0.5 + p.Ouverture) * (0.5 + p.Moral.Compassion);
        if (alea.Reel() >= envie) return;
        var (communaute, gardien, le, champs) = (h.CommunauteId, h.Id, _maintenant, Champs);
        long evt = 0;
        if (sait is null || sait.Essais < 3)
            evt = Noter(_maintenant, TypeEvenement.Decouverte, h, null, new Recit("bete.petit", ("h", h), ("bete", b.Dit)), sait?.EvenementId ?? 0);
        Tour.Differer(() =>
        {
            for (var k = 0; k < champs.Troupeaux.Count; k++)
            {
                var t = champs.Troupeaux[k];
                if (t.Espece != b.Numero || Maisonnee(t.CommunauteId, t.GardienId) != Maisonnee(communaute, gardien)) continue;
                champs.Troupeaux[k] = t with { Petits = t.Petits + 1 };
                return;
            }
            champs.NouveauTroupeau(b, communaute, gardien, 0, 1, b.DociliteSauvage, le, evt);
        });
        Retenir(h, Geste.Approcher, Matiere.Carcasse, Matiere.Aucune, Effet.Apprivoiser, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous);
        Montrer(h, Geste.Approcher, Matiere.Carcasse, Matiere.Aucune, Effet.Apprivoiser, 0, Symptome.Aucun, tous, evt);
    }

    /// <summary>Où vit le troupeau : au camp de sa communauté, ou là où est son gardien.</summary>
    private (int X, int Y, Humain? Gardien) OuEst(Troupeau t, IReadOnlyList<Humain> humains)
    {
        var gardien = t.GardienId > 0 && t.GardienId <= humains.Count && humains[t.GardienId - 1] is { EstVivant: true } g ? g : null;
        if (t.CommunauteId != 0 && Social is { } s && t.CommunauteId <= s.Communautes.Count && s.Communautes[t.CommunauteId - 1] is { Active: true, CentreX: >= 0 } c)
            return (c.CentreX, c.CentreY, gardien);
        return gardien is null ? (-1, -1, null) : (gardien.X, gardien.Y, gardien);
    }

    /// <summary>
    /// Une fois par jour : les rôdeurs autour des camps, puis chaque troupeau mange, grandit, met bas, s'échappe ou blesse ; on
    /// abat une bête quand on a faim ; on trait les femelles si l'on sait le faire.
    /// </summary>
    internal void JourneeDesBetes(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (!AvecLeRelief) return;
        _maintenant = maintenant;
        _tous = humains;
        Rodeurs(humains, maintenant);
        var chiens = new HashSet<int>();
        var eleveurs = new HashSet<int>();
        var fumier = new HashSet<int>();
        const double jour = Calendrier.SecondesParJour;
        for (var k = 0; k < Champs.Troupeaux.Count; k++)
        {
            var t = Champs.Troupeaux[k];
            var b = Faune.Get(t.Espece);
            var (x, y, gardien) = OuEst(t, humains);
            if (x < 0 || t.Nombre < 0.5)
            {
                // Plus personne pour les garder : elles retournent à la vie sauvage.
                if (t.Adultes >= 2 && gardien is null && t.GardienId > 0 && t.GardienId <= humains.Count)
                    Noter(maintenant, TypeEvenement.Nature, humains[t.GardienId - 1], null, new Recit("bete.retour", ("bete", b.Dit)), t.EvenementId);
                Champs.Troupeaux.RemoveAt(k--);
                continue;
            }
            var i = carte.Indice(x, y);
            var membres = new List<Humain>();
            foreach (var h in humains)
                if (h.EstVivant && (t.CommunauteId != 0 ? h.CommunauteId == t.CommunauteId : h.Id == t.GardienId)) membres.Add(h);
            if (membres.Count == 0)
            {
                Champs.Troupeaux.RemoveAt(k--);
                continue;
            }
            var poids = b.Poids * t.Taille;
            // Manger : les brouteurs autour du camp, les autres des restes des humains.
            double etat;
            var pature = 1.0;
            if (b.Regime == RegimeBete.Herbivore)
            {
                // Elles broutent à quelques kilomètres autour du camp : ce qui pousse, rapporté à ce que la terre porte en pleine saison.
                double pousse = 0, niveau = 0;
                for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    if (x + dx < 0 || y + dy < 0 || x + dx >= carte.Largeur || y + dy >= carte.Hauteur) continue;
                    var j = carte.Indice(x + dx, y + dy);
                    pousse += ecosysteme.Plantes[j];
                    niveau += ecosysteme.NiveauPlantes(j, 1);
                }
                var abondance = niveau <= 0 ? 0 : Math.Min(1, pousse / niveau);
                pature = abondance;
                Ecosysteme.Prelever(ecosysteme.Plantes, i, (int)Math.Ceiling(t.Nombre * poids / 400));
                etat = t.Etat + (abondance >= 0.3 ? 0.02 : -0.03 * (1 - abondance / 0.3));
            }
            else
            {
                var faim = membres.Average(h => h.Faim);
                etat = t.Etat + (faim < 50 ? 0.015 : -0.02 * (faim - 50) / 50);
            }
            etat = Math.Clamp(etat, 0, 1);
            var adultes = t.Adultes;
            var petits = t.Petits;
            // Les petits grandissent ; les vieux meurent (une dizaine d'années de vie) ; les affamés aussi.
            var grandis = petits / (b.AnsMaturite * Calendrier.JoursParAn);
            (adultes, petits) = (adultes + grandis, petits - grandis);
            adultes -= adultes / (10 * Calendrier.JoursParAn) + (etat < 0.3 ? 0.01 * adultes : 0);
            petits -= etat < 0.3 ? 0.02 * petits : 0;
            // S'échapper : une bête qui panique franchit tout, surtout sans gardien près d'elle.
            var garde = gardien is not null && Distance(gardien.X, gardien.Y, x, y) <= 2;
            var fuite = 0.004 * b.Panique * Math.Pow(1 - t.Docilite, 2) * (garde ? 0.4 : 1) * (b.Hierarchie ? 1 : 2) * Math.Sqrt(Math.Max(1, adultes));
            if (adultes >= 1 && alea.Reel() < fuite) adultes -= 1;
            // Charger : une bête agressive et lourde blesse qui l'approche.
            var charge = 0.0008 * b.Agressivite * Math.Pow(1 - t.Docilite, 2) * Math.Min(1.5, poids / 80) * Math.Sqrt(Math.Max(0, adultes));
            if (gardien is not null && garde && adultes >= 1 && alea.Reel() < charge)
            {
                Medecine.Blesser(gardien, 8 + 40 * alea.Reel() * Math.Min(1, poids / 150), maintenant, OrigineBlessure.Coups);
                Noter(maintenant, TypeEvenement.Medecine, gardien, null, new Recit("bete.charge", ("h", gardien), ("bete", b.Dit)), t.EvenementId);
            }
            // Mettre bas, une fois l'an au printemps, si l'espèce se reproduit captive et qu'elles sont en état.
            var saison = SaisonDe(maintenant);
            var generations = t.Generations;
            var docilite = t.Docilite;
            var taille = t.Taille;
            var miseBas = t.MiseBasLe;
            var naissance = t.NaissanceEvenementId;
            var abattue = t.Abattue;
            if (saison == 0 && b.Captive && adultes >= 1.5 && etat >= 0.4 && maintenant - t.MiseBasLe > 300 * jour)
            {
                var nes = adultes / 2 * b.Portee * etat * (0.7 + 0.4 * alea.Reel());
                petits += nes;
                miseBas = maintenant;
                generations++;
                // On garde les plus calmes pour la reproduction, on mange les plus farouches : la docilité monte, la taille baisse un temps.
                var soin = gardien is null ? 0.6 : 0.6 + 0.4 * gardien.Personnalite.Conscience;
                // Une portée par an, mais une génération seulement quand les petits sont en âge de se reproduire.
                docilite += 0.02 * soin * (1 - docilite) / Math.Max(1, b.AnsMaturite);
                taille = Math.Max(0.7, taille - 0.004);
                if (naissance == 0 && gardien is not null)
                    naissance = Noter(maintenant, TypeEvenement.Decouverte, gardien, null, new Recit("bete.naissance", ("h", gardien), ("bete", b.Dit)), t.EvenementId);
            }
            // Abattre une bête quand le groupe a faim, ou quand le troupeau dépasse ce qu'on peut garder ; jamais les deux dernières.
            var faimMoyenne = membres.Average(h => h.Faim);
            // Ce que les siens peuvent garder et nourrir : au plus une grosse bête par personne là où l'herbe abonde (une unité de
            // bétail tropical, 250 kg ; les premiers éleveurs en avaient bien moins), quelques dizaines de kilos de mangeurs de restes.
            var portee = membres.Count * (b.Regime == RegimeBete.Herbivore ? 250 * Math.Max(0.2, pature) : 40);
            var vivant = (adultes + 0.3 * petits) * poids;
            var abattues = 0.0;
            if (vivant > portee && vivant > 0)
            {
                // Le surplus est mangé (ou lâché) : on garde les meilleures, et les petits avec leurs mères.
                var f = portee / vivant;
                abattues = adultes * (1 - f);
                (adultes, petits) = (adultes * f, petits * f);
            }
            else if ((faimMoyenne > 45 || !b.Captive && adultes >= 1 && faimMoyenne > 30) && adultes >= (b.Captive ? 3 : 1) && alea.Reel() < 0.2)
            {
                abattues = 1;
                adultes -= 1;
            }
            if (abattues > 0)
            {
                // La moitié du poids se mange ; un kilo de viande apaise 10 de faim.
                var viande = abattues * poids * 0.5 * 10;
                foreach (var h in membres.OrderByDescending(h => h.Faim))
                {
                    if (viande <= 0) break;
                    var part = Math.Min(viande, h.Faim);
                    h.Faim -= part;
                    viande -= part;
                }
                if (!abattue && gardien is not null)
                    Noter(maintenant, TypeEvenement.Partage, gardien, null, new Recit("bete.abat", ("h", gardien), ("bete", b.Dit)), t.EvenementId);
                abattue = true;
            }
            // Traire : des femelles qui allaitent, assez calmes pour se laisser prendre leur lait.
            if (b.Lait > 0 && docilite >= 0.35 && adultes >= 1 && maintenant - miseBas < 150 * jour)
                Traire(t, b, adultes, docilite, membres, gardien, maintenant);
            var domestique = t.Domestique;
            if (!domestique && docilite >= DociliteDomestique && generations >= 8)
            {
                domestique = true;
                var temoin = gardien ?? membres[0];
                var e = new EvenementJournal(maintenant, TypeEvenement.Decouverte, temoin.Id, 0,
                    new Recit("bete.domestique", ("bete", b.Dit), ("h", temoin), ("g", (long)generations)))
                {
                    Id = Numeros.Suivant(), CauseId = t.EvenementId, CommunauteId = t.CommunauteId, Significatif = true,
                };
                Journal.Add(e);
            }
            Champs.Troupeaux[k] = t with
            {
                Adultes = Math.Max(0, adultes), Petits = Math.Max(0, petits), Etat = etat, Docilite = Math.Min(1, docilite), Taille = taille,
                Generations = generations, MiseBasLe = miseBas, Domestique = domestique, NaissanceEvenementId = naissance, Abattue = abattue,
            };
            var maison = Maisonnee(t.CommunauteId, t.GardienId);
            if (b.Forme == 4 && docilite >= 0.25 && adultes >= 1) chiens.Add(maison);
            if (b.Regime != RegimeBete.Carnivore && adultes >= 3)
            {
                eleveurs.Add(maison);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (x + dx >= 0 && y + dy >= 0 && x + dx < carte.Largeur && y + dy < carte.Hauteur) fumier.Add(carte.Indice(x + dx, y + dy));
            }
        }
        (_chiens, _eleveurs, _fumier) = (chiens, eleveurs, fumier);
    }

    /// <summary>
    /// Le lait : celui qui garde des femelles qui allaitent peut un jour essayer d'en prendre. Ensuite, le lait nourrit d'abord les
    /// enfants ; les adultes le digèrent mal (sans lactase, Ségurel et Bon, 2017) et n'en tirent qu'une part.
    /// </summary>
    private void Traire(Troupeau t, Bete b, double adultes, double docilite, List<Humain> membres, Humain? gardien, long maintenant)
    {
        var trayeur = membres.Find(h => h.Savoirs.Exists(s => s.Effet == Effet.Traite && s.Confiance >= 0.4));
        if (trayeur is null)
        {
            if (gardien is null || gardien.EstEnfant(maintenant)) return;
            var chance = 0.003 * (0.5 + gardien.Personnalite.Ouverture) * docilite * b.Lait;
            if (alea.Reel() >= chance) return;
            Retenir(gardien, Geste.Tordre, Matiere.Carcasse, Matiere.Aucune, Effet.Traite, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, _tous,
                recit: new Recit("bete.traite", ("h", gardien), ("bete", b.Dit)));
            return;
        }
        // Une femelle sauvage donne peu : un demi-litre par jour au plus, bien plus après des générations de traite.
        var litres = adultes / 2 * b.Lait * docilite * (0.5 + 1.5 * Math.Max(0, docilite - 0.35));
        var faim = litres * 4;
        foreach (var h in membres.OrderBy(h => h.EstEnfant(maintenant) ? 0 : 1).ThenByDescending(h => h.Faim))
        {
            if (faim <= 0) break;
            var valeur = h.EstEnfant(maintenant) ? 1 : 0.5;
            var part = Math.Min(faim, h.Faim / valeur);
            h.Faim = Math.Max(0, h.Faim - part * valeur);
            faim -= part;
        }
        if (alea.Reel() < 0.05)
            Retenir(trayeur, Geste.Tordre, Matiere.Carcasse, Matiere.Aucune, Effet.Traite, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, _tous);
    }

    /// <summary>
    /// Les rôdeurs : autour d'un camp qui dure, des charognards viennent chercher les restes. Un cœur tendre leur jette des os ;
    /// après quelques semaines, les plus hardis s'approchent, et l'on finit par prendre leurs petits (Coppinger et Coppinger, 2001).
    /// </summary>
    private void Rodeurs(IReadOnlyList<Humain> humains, long maintenant)
    {
        if (Social is not { } social) return;
        foreach (var c in social.Communautes)
        {
            if (c is not { Active: true, CentreX: >= 0 } || maintenant - c.FondeeLe < 60 * Calendrier.SecondesParJour) continue;
            Bete? rodeur = null;
            foreach (var b in Faune.Sur(carte.BiomeEn(c.CentreX, c.CentreY)))
                if (b.Charognarde && b.Captive && (rodeur is null || b.Agressivite < rodeur.Agressivite)) rodeur = b;
            if (rodeur is null || alea.Reel() >= 0.05) continue;
            var idee = Champs.IdeeDe(c.Id);
            Humain? tendre = null;
            foreach (var h in humains)
            {
                if (!h.EstVivant || h.CommunauteId != c.Id || h.Faim > 40 || h.EstEnfant(maintenant)) continue;
                var p = h.Personnalite;
                var douceur = 0.5 * p.Agreabilite + 0.5 * p.Moral.Compassion;
                if (douceur > 0.6 && (tendre is null || douceur > 0.5 * tendre.Personnalite.Agreabilite + 0.5 * tendre.Personnalite.Moral.Compassion)) tendre = h;
            }
            if (tendre is null)
            {
                idee.Rodeurs *= 0.9;
                continue;
            }
            if (idee.Rodeurs < 0.5)
                idee.RodeursEvenementId = Noter(maintenant, TypeEvenement.Nature, tendre, null, new Recit("bete.rodeurs", ("h", tendre), ("bete", rodeur.Dit)));
            idee.Rodeurs += 1;
            if (idee.Rodeurs < 5 || alea.Reel() >= 0.1) continue;
            idee.Rodeurs = 0;
            var evt = Noter(maintenant, TypeEvenement.Decouverte, tendre, null, new Recit("bete.chiots", ("h", tendre), ("bete", rodeur.Dit)), idee.RodeursEvenementId);
            var existant = Champs.Troupeaux.FindIndex(t => t.Espece == rodeur.Numero && t.CommunauteId == c.Id);
            if (existant >= 0) Champs.Troupeaux[existant] = Champs.Troupeaux[existant] with { Petits = Champs.Troupeaux[existant].Petits + 2 };
            else Champs.NouveauTroupeau(rodeur, c.Id, tendre.Id, 0, 2, Math.Min(0.5, rodeur.DociliteSauvage + 0.1), maintenant, evt);
            Retenir(tendre, Geste.Approcher, Matiere.Carcasse, Matiere.Aucune, Effet.Apprivoiser, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, humains);
        }
    }

    /// <summary>Le fumier d'un troupeau engraisse les parcelles autour du camp (+15 à 25 %).</summary>
    private double Fumier(int i) => _fumier.Contains(i) ? 1.2 : 1;
}
