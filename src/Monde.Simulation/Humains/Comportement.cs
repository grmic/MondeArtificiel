using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Règles de vie d'un humain, appliquées à chaque pas de 10 minutes :
/// les besoins évoluent, la santé en dépend, puis l'humain choisit une action.
/// Les seuils sont des réglages de départ, à ajuster en observant le monde.
/// </summary>
public sealed class Comportement(Carte carte, Ecosysteme ecosysteme, Alea alea)
{
    // Évolution des besoins par pas de 10 minutes.
    private const double FaimParPas = 0.23;     // affamé en 3 jours environ
    private const double SoifParPas = 0.5;      // assoiffé en un peu plus d'un jour
    private const double FatigueParPas = 0.55;  // épuisé après une longue journée sans dormir
    private const double RecuperationSommeil = 1.2;
    private const double TemperatureConfort = 15;
    private const double ChaleurCorporelle = 0.5;

    private const double ProbabiliteMaladieDeBase = 0.00004;

    private static readonly (int Dx, int Dy)[] Voisins8 =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    public void Vivre(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (!h.EstVivant) return;
        var partenaire = tous[h.PartenaireId - 1];
        Metaboliser(h, partenaire, maintenant);
        if (!h.EstVivant) return;
        Decider(h, partenaire, maintenant);
    }

    private void Metaboliser(Humain h, Humain partenaire, long maintenant)
    {
        var p = h.Personnalite;
        h.Faim += FaimParPas;
        h.Soif += SoifParPas;
        h.Fatigue += h.Activite == Activite.Dormir
            ? -RecuperationSommeil
            : FatigueParPas * (1.2 - 0.4 * p.Endurance) * (h.Activite == Activite.Repos ? 0.5 : 1);

        // Froid : la perte de chaleur dépend de l'écart au confort et de l'abri ; le corps en produit un peu.
        var temperature = Climat.Temperature(carte, h.X, h.Y, maintenant);
        var abri = Climat.Abri(carte.BiomeEn(h.X, h.Y));
        if (partenaire.EstVivant && partenaire.X == h.X && partenaire.Y == h.Y) abri += 0.3; // se tenir chaud à deux
        var perte = Math.Max(0, TemperatureConfort - temperature) * 0.05 * Math.Max(0, 1 - abri);
        h.Froid += perte - ChaleurCorporelle;

        h.Faim = Math.Clamp(h.Faim, 0, 100);
        h.Soif = Math.Clamp(h.Soif, 0, 100);
        h.Fatigue = Math.Clamp(h.Fatigue, 0, 100);
        h.Froid = Math.Clamp(h.Froid, 0, 100);

        // Dégâts sur la santé : on retient la cause la plus lourde.
        Span<double> degats = stackalloc double[6];
        if (h.Faim >= 85) degats[(int)CauseDeMort.Faim] = 0.06;
        if (h.Soif >= 85) degats[(int)CauseDeMort.Soif] = 0.25;
        if (h.Froid >= 70) degats[(int)CauseDeMort.Froid] = 0.05 + 0.15 * (h.Froid - 70) / 30;
        if (h.Fatigue >= 95) degats[(int)CauseDeMort.Epuisement] = 0.05;
        if (h.EstMalade(maintenant)) degats[(int)CauseDeMort.Maladie] = 0.04 * (1.3 - 0.6 * p.Endurance);

        double total = 0, pire = 0;
        var cause = CauseDeMort.Aucune;
        for (var i = 1; i < degats.Length; i++)
        {
            total += degats[i];
            if (degats[i] > pire) (pire, cause) = (degats[i], (CauseDeMort)i);
        }

        if (total == 0 && h.Faim < 60 && h.Soif < 60) h.Sante = Math.Min(100, h.Sante + 0.03);
        h.Sante -= total;
        if (h.Sante <= 0)
        {
            h.Sante = 0;
            h.MortLe = maintenant;
            h.CauseDeMort = cause;
            h.Activite = Activite.Mort;
            return;
        }

        // Tomber malade : plus probable quand on est affaibli, affamé ou transi.
        if (!h.EstMalade(maintenant))
        {
            var risque = ProbabiliteMaladieDeBase * (1 + (100 - h.Sante) / 25 + h.Faim / 50 + h.Froid / 50) * (1.3 - 0.6 * p.Endurance);
            if (alea.Reel() < risque)
            {
                h.MaladeJusqua = maintenant + (3 + alea.Entier(8)) * Calendrier.SecondesParJour;
            }
        }
    }

    private void Decider(Humain h, Humain partenaire, long maintenant)
    {
        var p = h.Personnalite;
        var date = Calendrier.Decomposer(maintenant);

        // Un dormeur ne se réveille que si un besoin devient pressant ou s'il est reposé.
        if (h.Activite == Activite.Dormir)
        {
            var reveil = h.Soif > 80 || h.Faim > 90 || h.Froid > 80 || h.Fatigue < 5 || (date.EstJour && h.Fatigue < 35);
            if (!reveil) return;
        }

        // Urgence de chaque besoin (0 à 1 environ). Les prudents réagissent plus tôt.
        var seuil = 0.45 - 0.2 * p.Prudence;
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

        // Aucun besoin pressant : rester près de l'autre, explorer ou se reposer selon le caractère.
        var distance = Distance(h.X, h.Y, partenaire.X, partenaire.Y);
        if (partenaire.EstVivant && distance > 2 + (1 - p.Sociabilite) * 8)
        {
            Avancer(h, partenaire.X, partenaire.Y, Activite.RejoindrePartenaire);
        }
        else if (alea.Reel() < 0.15 + 0.5 * p.Curiosite)
        {
            Explorer(h);
        }
        else
        {
            h.Activite = Activite.Repos;
            h.CibleX = h.CibleY = -1;
        }
    }

    /// <summary>Urgence d'un besoin : croît avec lui, et beaucoup plus vite au-delà de 70 (danger).</summary>
    private static double Urgence(double besoin) => besoin / 100 + (besoin > 70 ? (besoin - 70) / 30 * 0.8 : 0);

    private int RayonPerception(Humain h) => 6 + (int)(h.Personnalite.Curiosite * 8);

    private void AllerBoire(Humain h)
    {
        if (EauDouceAPortee(h.X, h.Y))
        {
            h.Soif = 0;
            h.Activite = Activite.Boire;
            h.CibleX = h.CibleY = -1;
            return;
        }
        var cible = Chercher(h, i => EauDouceAPortee(i % carte.Largeur, i / carte.Largeur), _ => 0);
        if (cible is { } c) Avancer(h, c.X, c.Y, Activite.ChercherEau);
        else Explorer(h, Activite.ChercherEau);
    }

    private void AllerManger(Humain h)
    {
        var i = carte.Indice(h.X, h.Y);
        if (ecosysteme.Plantes[i] >= 1000)
        {
            var pris = Ecosysteme.Prelever(ecosysteme.Plantes, i, 8);
            h.Faim = Math.Max(0, h.Faim - pris * 2.5);
            h.Activite = Activite.Cueillir;
            h.CibleX = h.CibleY = -1;
            return;
        }
        if (ecosysteme.Gibier[i] >= 2000)
        {
            // Chasse à mains nues : réussite rare, meilleure pour les adroits et là où le gibier abonde.
            var chance = (0.1 + 0.3 * h.Personnalite.Adresse) * ecosysteme.Gibier[i] / 10_000.0;
            h.Activite = Activite.Chasser;
            h.CibleX = h.CibleY = -1;
            if (alea.Reel() < chance)
            {
                var pris = Ecosysteme.Prelever(ecosysteme.Gibier, i, 15);
                h.Faim = Math.Max(0, h.Faim - pris * 4);
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
        if (cible is { } c) Avancer(h, c.X, c.Y, Activite.ChercherNourriture);
        else Explorer(h, Activite.ChercherNourriture);
    }

    private void AllerDormir(Humain h, Humain partenaire)
    {
        // Les prudents cherchent d'abord un abri, ou rejoignent l'autre pour dormir ensemble.
        if (Climat.Abri(carte.BiomeEn(h.X, h.Y)) < 0.3 && h.Fatigue < 85 && h.Personnalite.Prudence > 0.4)
        {
            if (partenaire.EstVivant && Distance(h.X, h.Y, partenaire.X, partenaire.Y) is > 0 and <= 4)
            {
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
    }

    private void AllerSAbriter(Humain h, Humain partenaire, long maintenant)
    {
        if (partenaire.EstVivant && Distance(h.X, h.Y, partenaire.X, partenaire.Y) is > 0 and <= 6)
        {
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
        foreach (var (dx, dy) in Voisins8)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= carte.Largeur || ny >= carte.Hauteur) continue;
            if (!Climat.EstPraticable(carte.BiomeEn(nx, ny))) continue;
            var d = Distance2(nx, ny, cibleX, cibleY);
            if (d < meilleure) (meilleure, bx, by) = (d, nx, ny);
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

        while (file.Count > 0)
        {
            var (i, d) = file.Dequeue();
            if (i != debut && convient(i))
            {
                var s = score(i) - d * 300;
                if (s > meilleurScore) (meilleurScore, meilleur) = (s, (i % carte.Largeur, i / carte.Largeur));
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
