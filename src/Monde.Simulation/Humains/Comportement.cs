using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Règles de vie d'un humain, appliquées à chaque pas de 10 minutes :
/// les besoins évoluent, la santé en dépend, puis l'humain choisit une action.
/// Les seuils sont des réglages de départ, à ajuster en observant le monde.
/// </summary>
public sealed partial class Comportement(Carte carte, Ecosysteme ecosysteme, Alea alea)
{
    // Évolution des besoins par pas de 10 minutes.
    private const double FaimParPas = 0.23;     // affamé en 3 jours environ
    private const double SoifParPas = 0.5;      // assoiffé en un peu plus d'un jour
    private const double FatigueParPas = 0.55;  // épuisé après une longue journée sans dormir
    private const double RecuperationSommeil = 1.2;
    private const double TemperatureConfort = 15;
    private const double ChaleurCorporelle = 0.5;

    private const double ProbabiliteMaladieDeBase = 0.000012; // environ une maladie tous les deux ans

    private static readonly (int Dx, int Dy)[] Voisins8 =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary>Instant du pas en cours.</summary>
    private long _maintenant;

    // Raison de la décision en cours et humain concerné, pour la pensée qui l'accompagne.
    private Motif _motif;
    private Humain? _autre;

    /// <summary>Événements survenus pendant le pas : le monde les verse au journal après le pas.</summary>
    public List<EvenementJournal> Journal { get; } = [];

    /// <summary>Morts du pas : le monde endeuille leurs proches.</summary>
    public List<Humain> Deces { get; } = [];

    /// <summary>Liens, humeur et partages (fourni par le monde).</summary>
    public VieSociale? Social { get; set; }

    private void Noter(long maintenant, TypeEvenement type, Humain h, Humain? autre, string texte) =>
        Journal.Add(new EvenementJournal(maintenant, type, h.Id, autre?.Id ?? 0, texte));

    public void Vivre(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstVivant) return;
        _maintenant = maintenant;
        if (h.EstBebe(maintenant))
        {
            VivreBebe(h, Vivant(tous, h.MereId), maintenant);
            return;
        }
        // Pour un enfant, le « compagnon » à rejoindre (pour dormir, se tenir chaud) est un parent.
        var partenaire = h.EstEnfant(maintenant)
            ? Vivant(tous, h.MereId) ?? Vivant(tous, h.PereId)
            : Vivant(tous, h.PartenaireId);
        var chaleurPartagee = AuMemeEndroit(h, partenaire) || AuMemeEndroit(h, Vivant(tous, h.MereId)) || AuMemeEndroit(h, Vivant(tous, h.PereId))
                              || AmiAuMemeEndroit(h, tous);
        Metaboliser(h, chaleurPartagee, maintenant);
        if (!h.EstVivant) return;
        if (h.EstEnfant(maintenant)) NourrirEnfant(h, Vivant(tous, h.MereId), Vivant(tous, h.PereId));
        PartagerAvecProches(h, tous);
        Grossesse(h, tous, maintenant);
        if (!h.EstVivant) return;
        var avant = h.Activite;
        (_motif, _autre) = (Motif.Aucun, null);
        Decider(h, partenaire, tous, maintenant);
        if (h.Activite != avant && Penseur.Penser(h, _motif, _autre, maintenant) is { } pensee)
        {
            var routine = EstRoutine(h.Activite, _motif);
            if (routine) Routine.Compter(h.Id, h.Activite);
            // Au journal : les gestes de tous les jours sont résumés une fois par jour ; une autre pensée est notée,
            // sauf si une pensée de même sens figure déjà parmi les dernières (activités qui alternent).
            var nouvelle = !routine && !h.Memoire.PenseeRecente(pensee, Memoire.PenseesMax);
            if (h.Memoire.Penser(maintenant, pensee) && nouvelle) Noter(maintenant, TypeEvenement.Pensee, h, _autre, $"{h.Nom} : « {pensee} »");
        }
    }

    /// <summary>Gestes quotidiens (boire, manger, dormir, se reposer, aller vers ce qu'on voit ou vers les siens) : résumés chaque jour.</summary>
    public JourneeType Routine { get; } = new();

    private static bool EstRoutine(Activite a, Motif motif) => a switch
    {
        Activite.Boire or Activite.Cueillir or Activite.Chasser or Activite.Dormir or Activite.Repos or Activite.Explorer
            or Activite.SAbriter => true,
        Activite.ChercherEau or Activite.ChercherNourriture or Activite.ChercherAbri => motif is Motif.Vu or Motif.Aucun,
        Activite.RejoindrePartenaire => motif != Motif.Desir,
        Activite.RejoindreProche or Activite.SuivreParent or Activite.VeillerEnfant => true,
        _ => false,
    };

    /// <summary>Se serrer contre quelqu'un qu'on aime réchauffe autant que contre son compagnon.</summary>
    private static bool AmiAuMemeEndroit(Humain h, IReadOnlyList<Humain> tous)
    {
        foreach (var l in h.Liens)
        {
            if (l.Affection < 20) continue;
            var a = tous[l.Id - 1];
            if (a.EstVivant && a.X == h.X && a.Y == h.Y) return true;
        }
        return false;
    }

    /// <summary>
    /// Un humain affamé reçoit un peu de nourriture d'un proche rassasié à côté de lui : d'autant plus que celui-ci
    /// est bienveillant et l'aime, ou vit dans la même communauté. Le partage de nourriture hors de la famille est
    /// universel chez les chasseurs-cueilleurs (Kaplan et Hill, 1985) et nourrit la reconnaissance.
    /// </summary>
    private void PartagerAvecProches(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Faim < 60 || h.EstBebe(_maintenant)) return;
        foreach (var l in h.Liens)
        {
            var d = tous[l.Id - 1];
            if (!d.EstVivant || d.Faim >= 40 || d.EstBebe(_maintenant) || d.EstDeprime) continue;
            if (Distance(h.X, h.Y, d.X, d.Y) > 1) continue;
            var lien = d.LienAvec(h.Id);
            if (lien is null) continue;
            var communaute = d.CommunauteId != 0 && d.CommunauteId == h.CommunauteId ? 0.3 : 0;
            var envie = Math.Clamp(lien.Affection / 100 + communaute, 0, 1) * d.Personnalite.Agreabilite;
            if (envie <= 0.05) continue;
            var donne = Math.Min(h.Faim - 40, 0.6 * envie);
            h.Faim -= donne;
            d.Faim = Math.Min(100, d.Faim + 0.6 * donne);
            VieSociale.Gratitude(d, h, 0.05);
            Social?.PartagesDuJour.Add((d.Id, h.Id));
            return;
        }
    }

    private static bool AuMemeEndroit(Humain h, Humain? autre) => autre is not null && autre.X == h.X && autre.Y == h.Y;

    private static Humain? Vivant(IReadOnlyList<Humain> tous, int id) =>
        id > 0 && id <= tous.Count && tous[id - 1].EstVivant ? tous[id - 1] : null;

    private void Metaboliser(Humain h, bool chaleurPartagee, long maintenant)
    {
        var p = h.Personnalite;
        var enceinte = h.EstEnceinte;
        h.Faim += FaimParPas + (enceinte ? 0.05 : 0);
        h.Soif += SoifParPas;
        h.Fatigue += h.Activite == Activite.Dormir
            ? -RecuperationSommeil
            : FatigueParPas * (1.2 - 0.4 * p.Endurance) * (h.Activite == Activite.Repos ? 0.5 : 1) * (enceinte ? 1.2 : 1);

        // Froid : la perte de chaleur dépend de l'écart au confort et de l'abri ; le corps en produit un peu.
        var temperature = Climat.Temperature(carte, h.X, h.Y, maintenant);
        var abri = Climat.Abri(carte.BiomeEn(h.X, h.Y));
        if (chaleurPartagee) abri += 0.3; // se tenir chaud avec son compagnon ou sa famille
        var perte = Math.Max(0, TemperatureConfort - temperature) * 0.05 * Math.Max(0, 1 - abri);
        h.Froid += perte - ChaleurCorporelle;

        // Le désir monte chez les adultes en âge d'avoir des enfants, s'ils ne sont pas en détresse.
        if (h.EstFecond(maintenant) && !h.EstMalade(maintenant) && h.Faim < 70 && h.Soif < 70)
        {
            h.Desir = Math.Min(100, h.Desir + DesirParPas);
        }

        h.Faim = Math.Clamp(h.Faim, 0, 100);
        h.Soif = Math.Clamp(h.Soif, 0, 100);
        h.Fatigue = Math.Clamp(h.Fatigue, 0, 100);
        h.Froid = Math.Clamp(h.Froid, 0, 100);
        h.InconfortDuJour += (Math.Max(0, h.Faim - 50) + Math.Max(0, h.Soif - 50) + Math.Max(0, h.Froid - 40)) / 50
                             + (h.EstMalade(maintenant) ? 0.6 : 0);

        Souffrir(h, maintenant, 1);
    }

    /// <summary>Dégâts sur la santé selon les besoins, mort éventuelle, puis risque de tomber malade.</summary>
    private void Souffrir(Humain h, long maintenant, double fragilite)
    {
        var p = h.Personnalite;
        Span<double> degats = stackalloc double[8];
        if (h.Faim >= 85) degats[(int)CauseDeMort.Faim] = 0.06;
        if (h.Soif >= 85) degats[(int)CauseDeMort.Soif] = 0.25;
        if (h.Froid >= 70) degats[(int)CauseDeMort.Froid] = 0.05 + 0.15 * (h.Froid - 70) / 30;
        if (h.Fatigue >= 95) degats[(int)CauseDeMort.Epuisement] = 0.05;
        if (h.EstMalade(maintenant)) degats[(int)CauseDeMort.Maladie] = 0.03 * (1.3 - 0.6 * p.Endurance);

        double total = 0, pire = 0;
        var cause = CauseDeMort.Aucune;
        for (var i = 1; i < degats.Length; i++)
        {
            total += degats[i] * fragilite;
            if (degats[i] > pire) (pire, cause) = (degats[i], (CauseDeMort)i);
        }

        if (total == 0 && h.Faim < 60 && h.Soif < 60) h.Sante = Math.Min(100, h.Sante + 0.03);
        h.Sante -= total;
        if (h.Sante <= 0)
        {
            Mourir(h, maintenant, cause);
            return;
        }

        // Tomber malade : plus probable quand on est affaibli, affamé ou transi.
        if (!h.EstMalade(maintenant))
        {
            // La détresse affaiblit les défenses du corps.
            var risque = ProbabiliteMaladieDeBase * (1 + (100 - h.Sante) / 25 + h.Faim / 50 + h.Froid / 50) * (1.3 - 0.6 * p.Endurance)
                         * (h.EstDeprime ? 1.3 : 1);
            if (alea.Reel() < risque * fragilite)
            {
                h.MaladeJusqua = maintenant + (3 + alea.Entier(8)) * Calendrier.SecondesParJour;
                Noter(maintenant, TypeEvenement.Maladie, h, null, $"{h.Nom} tombe malade.");
            }
        }
    }

    private void Mourir(Humain h, long maintenant, CauseDeMort cause)
    {
        Noter(maintenant, TypeEvenement.Mort, h, null, cause == CauseDeMort.Desespoir
            ? $"{h.Nom}, accablé{(h.Sexe == Sexe.Femme ? "e" : "")} de tristesse, met fin à ses jours, à {AgeEnMots(h, maintenant)}."
            : $"{h.Nom} meurt {Libelles.Cause(cause)}, à {AgeEnMots(h, maintenant)}.");
        Deces.Add(h);
        h.Sante = 0;
        h.MortLe = maintenant;
        h.CauseDeMort = cause;
        h.Activite = Activite.Mort;
        h.EnceinteDepuis = null;
    }

    private void Decider(Humain h, Humain? partenaire, IReadOnlyList<Humain> tous, long maintenant)
    {
        var p = h.Personnalite;
        var date = Calendrier.Decomposer(maintenant);

        // Un dormeur ne se réveille que si un besoin devient pressant ou s'il est reposé.
        if (h.Activite == Activite.Dormir)
        {
            var reveil = h.Soif > 80 || h.Faim > 90 || h.Froid > 80 || h.Fatigue < 5 || (date.EstJour && h.Fatigue < 35);
            if (!reveil) return;
        }

        // Urgence de chaque besoin (0 à 1 environ). Les prudents réagissent plus tôt ; le désespoir fait négliger ses besoins.
        var abattu = h.EstDeprime || h.Chagrin > 50;
        var seuil = 0.45 - 0.2 * p.Prudence + (abattu ? 0.1 : 0);
        var urgences = new (double Valeur, Activite Besoin)[]
        {
            (Urgence(h.Soif) * 1.2, Activite.Boire),
            (Urgence(h.Faim), Activite.Cueillir),
            (Urgence(h.Fatigue) * 0.9 + (date.EstJour ? 0 : 0.35), Activite.Dormir),
            (Urgence(h.Froid) * (0.8 + p.Prudence * 0.4), Activite.SAbriter),
        };
        var (valeur, besoin) = urgences.MaxBy(u => u.Valeur);

        if (valeur >= seuil)
        {
            switch (besoin)
            {
                case Activite.Boire: AllerBoire(h); return;
                case Activite.Cueillir: AllerManger(h); return;
                case Activite.Dormir: AllerDormir(h, partenaire); return;
                case Activite.SAbriter: AllerSAbriter(h, partenaire, maintenant); return;
            }
        }

        // Aucun besoin pressant. Un enfant suit ses parents.
        if (h.EstEnfant(maintenant))
        {
            var parent = Vivant(tous, h.MereId) ?? Vivant(tous, h.PereId);
            _autre = parent;
            if (parent is not null && Distance(h.X, h.Y, parent.X, parent.Y) > 1) Avancer(h, parent.X, parent.Y, Activite.SuivreParent);
            else
            {
                h.Activite = Activite.Repos;
                h.CibleX = h.CibleY = -1;
            }
            return;
        }

        // Un adulte : veiller sur ses enfants, l'instinct de reproduction, puis rester près de l'autre, explorer ou se reposer.
        if (EnfantAVeiller(h, tous, maintenant) is { } enfant)
        {
            _autre = enfant;
            Avancer(h, enfant.X, enfant.Y, Activite.VeillerEnfant);
            return;
        }
        if (h.Desir >= SeuilDesir && ChercherAccouplement(h, partenaire, tous, maintenant)) return;

        if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) > 2 + (1 - p.Sociabilite) * 8)
        {
            _autre = partenaire;
            Avancer(h, partenaire.X, partenaire.Y, Activite.RejoindrePartenaire);
            return;
        }
        // On s'écarte de ceux qu'on ne supporte plus : c'est ainsi que les groupes trop tendus se scindent.
        if (RivalProche(h, tous) is { } rival && (h.Activite == Activite.EviterRival || alea.Reel() < 0.6 - 0.4 * p.Agreabilite))
        {
            _autre = rival;
            int dx = Math.Sign(h.X - rival.X), dy = Math.Sign(h.Y - rival.Y);
            if (dx == 0 && dy == 0) dx = 1;
            Avancer(h, Math.Clamp(h.X + 8 * dx, 0, carte.Largeur - 1), Math.Clamp(h.Y + 8 * dy, 0, carte.Hauteur - 1), Activite.EviterRival);
            if (h.CibleX >= 0) return;
        }
        // Les sociables vont retrouver ceux qu'ils aiment ; c'est ainsi que les groupes se forment.
        if (!abattu && (h.Activite == Activite.RejoindreProche || alea.Reel() < 0.1 + 0.4 * p.Extraversion)
            && ProcheARejoindre(h, tous) is { } proche)
        {
            _autre = proche;
            Avancer(h, proche.X, proche.Y, Activite.RejoindreProche);
            return;
        }
        if (alea.Reel() < (0.15 + 0.5 * p.Curiosite) * (abattu ? 0.3 : 1))
        {
            Explorer(h);
        }
        else
        {
            h.Activite = Activite.Repos;
            h.CibleX = h.CibleY = -1;
            // Au repos, l'endeuillé pense à celui qu'il a perdu.
            if (h.Chagrin > 30 && h.DeuilDeId > 0) _autre = tous[h.DeuilDeId - 1];
        }
    }

    /// <summary>Un humain hostile à moins de 3 cases, ou null.</summary>
    private static Humain? RivalProche(Humain h, IReadOnlyList<Humain> tous)
    {
        foreach (var l in h.Liens)
        {
            if (!l.Rivaux) continue;
            var a = tous[l.Id - 1];
            if (a.EstVivant && Distance(h.X, h.Y, a.X, a.Y) <= 3) return a;
        }
        return null;
    }

    /// <summary>L'humain aimé le plus attirant à rejoindre : affection forte, pas trop loin, mais pas déjà à côté.</summary>
    private Humain? ProcheARejoindre(Humain h, IReadOnlyList<Humain> tous)
    {
        Humain? choisi = null;
        var meilleur = double.MinValue;
        foreach (var l in h.Liens)
        {
            if (l.Affection < 30) continue;
            var a = tous[l.Id - 1];
            if (!a.EstVivant || a.EstBebe(_maintenant)) continue;
            var d = Distance(h.X, h.Y, a.X, a.Y);
            if (d <= 3 || d > 25) continue;
            var score = l.Affection - d;
            if (score > meilleur) (meilleur, choisi) = (score, a);
        }
        return choisi;
    }

    internal static string AgeEnMots(Humain h, long maintenant)
    {
        var age = h.AgeEnAnnees(maintenant);
        return age < 1 ? $"{Math.Max(0, (int)(age * 12))} mois" : $"{(int)age} an{(age >= 2 ? "s" : "")}";
    }

    /// <summary>Urgence d'un besoin : croît avec lui, et beaucoup plus vite au-delà de 70 (danger).</summary>
    private static double Urgence(double besoin) => besoin / 100 + (besoin > 70 ? (besoin - 70) / 30 * 0.8 : 0);

    private int RayonPerception(Humain h) =>
        h.EstEnfant(_maintenant) ? 5 : 6 + (int)(h.Personnalite.Curiosite * 8);

    private void AllerBoire(Humain h)
    {
        if (EauDouceAPortee(h.X, h.Y))
        {
            h.Soif = 0;
            h.Activite = Activite.Boire;
            h.CibleX = h.CibleY = -1;
            h.Memoire.Retenir(TypeLieu.Eau, h.X, h.Y, _maintenant, Aptitude(h));
            return;
        }
        var cible = Chercher(h, i => EauDouceAPortee(i % carte.Largeur, i / carte.Largeur), _ => 0);
        if (cible is { } c)
        {
            _motif = Motif.Vu;
            Avancer(h, c.X, c.Y, Activite.ChercherEau);
        }
        else if (!AllerAuSouvenir(h, TypeLieu.Eau, Activite.ChercherEau))
        {
            _motif = Motif.Inconnu;
            Explorer(h, Activite.ChercherEau);
        }
    }

    /// <summary>
    /// Rien d'utile en vue : l'humain se rend au lieu de ce type le plus proche dont il se souvient.
    /// Arrivé sur place sans rien trouver, il est déçu : un souvenir faible s'efface, un souvenir solide s'affaiblit.
    /// </summary>
    private bool AllerAuSouvenir(Humain h, TypeLieu type, Activite activite)
    {
        while (h.Memoire.PlusProche(type, h.X, h.Y, _maintenant) is { } lieu)
        {
            if (Distance(h.X, h.Y, lieu.X, lieu.Y) <= 1)
            {
                h.Memoire.Decevoir(lieu, _maintenant); // déjà là, et rien : le souvenir s'affaiblit
                continue;
            }
            _motif = Motif.Souvenir;
            Avancer(h, lieu.X, lieu.Y, activite);
            if (h.CibleX < 0) h.Memoire.Oublier(lieu); // devenu inaccessible
            return h.CibleX >= 0;
        }
        return false;
    }

    private static double Aptitude(Humain h) => h.Personnalite.Intelligence.Memoire;

    private void AllerManger(Humain h)
    {
        var i = carte.Indice(h.X, h.Y);
        if (ecosysteme.Plantes[i] >= 1000)
        {
            var pris = Ecosysteme.Prelever(ecosysteme.Plantes, i, 8);
            h.Faim = Math.Max(0, h.Faim - pris * 2.5);
            h.Activite = Activite.Cueillir;
            h.CibleX = h.CibleY = -1;
            h.Memoire.Retenir(TypeLieu.Nourriture, h.X, h.Y, _maintenant, Aptitude(h));
            return;
        }
        if (ecosysteme.Gibier[i] >= 2000 && !h.EstEnfant(_maintenant))
        {
            // Chasse à mains nues : réussite rare, meilleure pour les adroits et là où le gibier abonde.
            var chance = (0.1 + 0.3 * h.Personnalite.Adresse) * ecosysteme.Gibier[i] / 10_000.0;
            h.Activite = Activite.Chasser;
            h.CibleX = h.CibleY = -1;
            if (alea.Reel() < chance)
            {
                var pris = Ecosysteme.Prelever(ecosysteme.Gibier, i, 15);
                h.Faim = Math.Max(0, h.Faim - pris * 4);
                h.Memoire.Retenir(TypeLieu.Nourriture, h.X, h.Y, _maintenant, Aptitude(h));
            }
            else
            {
                h.Fatigue = Math.Min(100, h.Fatigue + 1);
            }
            return;
        }
        var cible = Chercher(h,
            j => ecosysteme.Plantes[j] >= 1000 || ecosysteme.Gibier[j] >= 2000,
            j => ecosysteme.Plantes[j] + ecosysteme.Gibier[j] / 2);
        if (cible is { } c)
        {
            _motif = Motif.Vu;
            Avancer(h, c.X, c.Y, Activite.ChercherNourriture);
        }
        else if (!AllerAuSouvenir(h, TypeLieu.Nourriture, Activite.ChercherNourriture))
        {
            _motif = Motif.Inconnu;
            Explorer(h, Activite.ChercherNourriture);
        }
    }

    private void AllerDormir(Humain h, Humain? partenaire)
    {
        // Les prudents cherchent d'abord un abri, ou rejoignent l'autre pour dormir ensemble.
        if (Climat.Abri(carte.BiomeEn(h.X, h.Y)) < 0.3 && h.Fatigue < 85 && h.Personnalite.Prudence > 0.4)
        {
            if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) is > 0 and <= 4)
            {
                _autre = partenaire;
                Avancer(h, partenaire.X, partenaire.Y, Activite.RejoindrePartenaire);
                return;
            }
            var abri = Chercher(h, j => Climat.Abri((Biome)carte.Biomes[j]) >= 0.4, _ => 0, rayonMax: 4);
            if (abri is { } a)
            {
                Avancer(h, a.X, a.Y, Activite.ChercherAbri);
                return;
            }
        }
        h.Activite = Activite.Dormir;
        h.CibleX = h.CibleY = -1;
        if (Climat.Abri(carte.BiomeEn(h.X, h.Y)) >= 0.4) h.Memoire.Retenir(TypeLieu.Abri, h.X, h.Y, _maintenant, Aptitude(h));
    }

    private void AllerSAbriter(Humain h, Humain? partenaire, long maintenant)
    {
        if (partenaire is not null && Distance(h.X, h.Y, partenaire.X, partenaire.Y) is > 0 and <= 6)
        {
            _autre = partenaire;
            Avancer(h, partenaire.X, partenaire.Y, Activite.RejoindrePartenaire);
            return;
        }
        // Chercher l'endroit le plus chaud à portée : plus bas, mieux abrité.
        int Confort(int i) => (int)((Climat.Temperature(carte, i % carte.Largeur, i / carte.Largeur, maintenant)
                                     + Climat.Abri((Biome)carte.Biomes[i]) * 12) * 1000);
        var ici = Confort(carte.Indice(h.X, h.Y));
        var mieux = Chercher(h, j => Confort(j) > ici + 1500, Confort);
        if (mieux is { } m) Avancer(h, m.X, m.Y, Activite.ChercherAbri);
        else
        {
            // Un abri connu pas trop loin (une demi-journée de marche) vaut le déplacement.
            var abri = h.Memoire.PlusProche(TypeLieu.Abri, h.X, h.Y, maintenant);
            if (abri is not null && Distance(h.X, h.Y, abri.X, abri.Y) is > 1 and <= 12
                && Climat.Abri(carte.BiomeEn(h.X, h.Y)) < 0.4)
            {
                _motif = Motif.Souvenir;
                Avancer(h, abri.X, abri.Y, Activite.ChercherAbri);
                if (h.CibleX >= 0) return;
            }
            h.Activite = Activite.SAbriter;
            h.CibleX = h.CibleY = -1;
        }
    }

    private void Explorer(Humain h, Activite activite = Activite.Explorer)
    {
        // Garde la même direction tant que la destination d'exploration n'est pas atteinte.
        if (h.Activite != activite || h.CibleX < 0 || EstArrive(h))
        {
            var rayon = RayonPerception(h);
            h.CibleX = Math.Clamp(h.X + alea.Entier(2 * rayon + 1) - rayon, 0, carte.Largeur - 1);
            h.CibleY = Math.Clamp(h.Y + alea.Entier(2 * rayon + 1) - rayon, 0, carte.Hauteur - 1);
        }
        Avancer(h, h.CibleX, h.CibleY, activite);
    }

    /// <summary>
    /// Un pas vers la cible. En terrain dégagé, on prend la case voisine qui rapproche le plus du but ;
    /// si un obstacle (côte, lac) bloque, on passe en mode détour : un chemin est calculé à chaque pas
    /// jusqu'à l'arrivée.
    /// </summary>
    private void Avancer(Humain h, int cibleX, int cibleY, Activite activite)
    {
        if (h.CibleX != cibleX || h.CibleY != cibleY) h.Detour = false;
        h.Activite = activite;
        h.CibleX = cibleX;
        h.CibleY = cibleY;
        if (EstArrive(h))
        {
            h.Detour = false;
            return;
        }

        var (bx, by) = h.Detour ? (-1, -1) : PasDirect(h.X, h.Y, cibleX, cibleY);
        if (bx < 0)
        {
            var pas = PremierPasDuChemin(carte.Indice(h.X, h.Y), carte.Indice(cibleX, cibleY));
            if (pas < 0)
            {
                // Inaccessible : on abandonne cette destination.
                h.CibleX = h.CibleY = -1;
                h.Detour = false;
                return;
            }
            h.Detour = true;
            (bx, by) = (pas % carte.Largeur, pas / carte.Largeur);
        }

        h.X = bx;
        h.Y = by;
        if (carte.BiomeEn(bx, by) is Biome.Montagne or Biome.SommetEnneige) h.Fatigue = Math.Min(100, h.Fatigue + 0.8);
    }

    /// <summary>Arrivé : sur la cible, ou à côté si la cible elle-même est de l'eau.</summary>
    private bool EstArrive(Humain h) =>
        (h.X == h.CibleX && h.Y == h.CibleY)
        || (!Climat.EstPraticable(carte.BiomeEn(h.CibleX, h.CibleY)) && Distance(h.X, h.Y, h.CibleX, h.CibleY) <= 1);

    private (int X, int Y) PasDirect(int x, int y, int cibleX, int cibleY)
    {
        var meilleure = Distance2(x, y, cibleX, cibleY);
        var (bx, by) = (-1, -1);
        var egalites = 0;
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            if (!Climat.EstPraticable(carte.BiomeEn(nx, ny))) continue;
            var d = Distance2(nx, ny, cibleX, cibleY);
            if (d < meilleure) (meilleure, bx, by, egalites) = (d, nx, ny, 1);
            else if (d == meilleure && bx >= 0 && alea.Entier(++egalites) == 0) (bx, by) = (nx, ny);
        }
        return (bx, by);
    }

    /// <summary>
    /// Plus court chemin (parcours en largeur, 8 directions) vers la cible ou une case voisine d'elle ;
    /// renvoie la première case du chemin, ou -1 si la cible est hors de portée.
    /// </summary>
    private int PremierPasDuChemin(int depart, int cible)
    {
        const int RayonMax = 40;
        int cx = cible % carte.Largeur, cy = cible / carte.Largeur;
        var parent = new Dictionary<int, int> { [depart] = depart };
        var file = new Queue<(int Indice, int Distance)>();
        file.Enqueue((depart, 0));
        while (file.Count > 0)
        {
            var (i, d) = file.Dequeue();
            int x = i % carte.Largeur, y = i / carte.Largeur;
            if (i != depart && Distance(x, y, cx, cy) <= (Climat.EstPraticable((Biome)carte.Biomes[cible]) ? 0 : 1))
            {
                while (parent[i] != depart) i = parent[i];
                return i;
            }
            if (d >= RayonMax) continue;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = carte.Indice(nx, ny);
                if (parent.ContainsKey(j) || !Climat.EstPraticable((Biome)carte.Biomes[j])) continue;
                parent[j] = i;
                file.Enqueue((j, d + 1));
            }
        }
        return -1;
    }

    /// <summary>
    /// Parcours en largeur autour de l'humain (dans son rayon de perception) ; renvoie la case
    /// qui satisfait <paramref name="convient"/> avec le meilleur score, en pénalisant la distance.
    /// </summary>
    private (int X, int Y)? Chercher(Humain h, Func<int, bool> convient, Func<int, int> score, int? rayonMax = null)
    {
        var rayon = rayonMax ?? RayonPerception(h);
        var debut = carte.Indice(h.X, h.Y);
        var vus = new HashSet<int> { debut };
        var file = new Queue<(int Indice, int Distance)>();
        file.Enqueue((debut, 0));
        (int X, int Y)? meilleur = null;
        var meilleurScore = int.MinValue;
        var egalites = 0;

        while (file.Count > 0)
        {
            var (i, d) = file.Dequeue();
            if (i != debut && convient(i))
            {
                var s = score(i) - d * 300;
                if (s > meilleurScore)
                {
                    (meilleurScore, meilleur, egalites) = (s, (i % carte.Largeur, i / carte.Largeur), 1);
                }
                else if (s == meilleurScore && alea.Entier(++egalites) == 0)
                {
                    // Égalité : tirage équitable, sinon le sens du parcours favoriserait toujours le nord-ouest.
                    meilleur = (i % carte.Largeur, i / carte.Largeur);
                }
            }
            if (d >= rayon) continue;
            int x = i % carte.Largeur, y = i / carte.Largeur;
            foreach (var (dx, dy) in Voisins8)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
                var j = carte.Indice(nx, ny);
                if (!vus.Add(j)) continue;
                // L'eau non praticable peut être une cible (on boit depuis la rive) mais on ne la traverse pas.
                if (Climat.EstPraticable((Biome)carte.Biomes[j])) file.Enqueue((j, d + 1));
            }
        }
        return meilleur;
    }

    private bool EauDouceAPortee(int x, int y)
    {
        if (Biomes.EstEauDouce(carte.BiomeEn(x, y))) return true;
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < carte.Largeur && ny < carte.Hauteur && Biomes.EstEauDouce(carte.BiomeEn(nx, ny)))
            {
                return true;
            }
        }
        return false;
    }

    private static int Distance(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    private static int Distance2(int x1, int y1, int x2, int y2) => (x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2);
}
