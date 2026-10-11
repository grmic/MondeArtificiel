using Monde.Simulation.Humains;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>
/// Maladies contagieuses. Elles naissent d'un contact avec une bête (zoonose : la plupart des maladies humaines nouvelles
/// viennent des animaux, Jones et al., 2008), puis passent d'un humain à l'autre quand ils sont ensemble. Chez des
/// chasseurs-cueilleurs dispersés en petites bandes, la plupart s'éteignent vite ; une maladie qui se transmet bien peut
/// ravager une bande, puis passer à une autre lors d'une rencontre. Les survivants sont immunisés, à vie ou pour un temps.
/// </summary>
public sealed partial class Catastrophes
{
    /// <summary>
    /// Probabilité, à chaque bête prise et dépecée, qu'une maladie passe de l'animal à l'humain. Réglé pour qu'une maladie
    /// nouvelle apparaisse en moyenne tous les dix à vingt ans dans une population d'une centaine de chasseurs.
    /// </summary>
    private const double PassageParPrise = 5e-6;

    /// <summary>Nombre moyen de personnes sur la même case qu'un malade (contacts proches), pour régler la transmission.</summary>
    private const double ContactsProches = 2.5;

    private static readonly string[] NomsMaladies =
    [
        "la fièvre rouge", "la toux sèche", "le flux de ventre", "la fièvre tachetée", "le mal des pustules", "la fièvre tremblante",
        "la toux sanglante", "le mal noir", "la fièvre jaune", "la fièvre des marais", "le mal des yeux rouges", "la langueur",
        "la fièvre des os", "le mal qui gonfle", "la toux des chiens", "la fièvre blanche",
    ];

    /// <summary>Une bête vient d'être prise et dépecée : une maladie peut passer à l'humain.</summary>
    public void Prise(Humain h, long maintenant)
    {
        if (h.InfectionId != 0 || _alea.Reel() >= PassageParPrise) return;
        // Une maladie déjà connue peut revenir de son réservoir animal (comme Ebola, d'une épidémie à l'autre).
        var p = _pathogenes.Count > 0 && _alea.Reel() < 0.5 ? _pathogenes[_alea.Entier(_pathogenes.Count)] : NouveauPathogene(h, maintenant);
        if (EstImmunise(h, p, maintenant)) return;
        var texte = p.ApparuLe == maintenant
            ? $"{h.Nom} a attrapé une maladie inconnue en dépeçant une bête : {p.Nom} vient d'apparaître."
            : $"{h.Nom} a attrapé {p.Nom} en dépeçant une bête.";
        var id = Noter(maintenant, h, texte, p.EvenementId, type: TypeEvenement.Maladie);
        if (p.ApparuLe == maintenant)
        {
            _pathogenes[^1] = p with { EvenementId = id };
            p = _pathogenes[^1];
        }
        Infecter(h, p, maintenant, id);
    }

    /// <summary>
    /// Traits d'une maladie nouvelle. Le nombre de personnes qu'un malade contamine (R0) est le plus souvent proche de 1
    /// ou en dessous (Lloyd-Smith et al., 2009) ; la létalité est le plus souvent de quelques pour cent, rarement très haute.
    /// </summary>
    private Pathogene NouveauPathogene(Humain h, long maintenant)
    {
        var r0 = LogNormale(1.2, 0.6);
        var incubation = Math.Clamp(LogNormale(4, 0.5), 1, 21);
        var duree = Math.Clamp(LogNormale(8, 0.4), 3, 30);
        var letalite = Math.Clamp(LogNormale(0.03, 1.1), 0.002, 0.7);
        var immunite = _alea.Reel() < 0.7 ? 0 : Math.Clamp(LogNormale(5, 0.8), 0.5, 40);
        var transmission = r0 / (ContactsProches * 24 * duree);
        var id = _pathogenes.Count + 1;
        var libres = NomsMaladies.Where(n => _pathogenes.All(p => p.Nom != n)).ToList();
        var nom = libres.Count > 0 ? libres[_alea.Entier(libres.Count)] : $"{NomsMaladies[_alea.Entier(NomsMaladies.Length)]} ({id})";
        var p = new Pathogene(id, nom, transmission, incubation, duree, letalite, immunite, maintenant, 0);
        _pathogenes.Add(p);
        return p;
    }

    private static bool EstImmunise(Humain h, Pathogene p, long maintenant) =>
        h.Immunites.Any(i => i.PathogeneId == p.Id && (i.Jusqua is null || i.Jusqua > maintenant));

    private void Infecter(Humain h, Pathogene p, long maintenant, long evenement)
    {
        h.InfectionId = p.Id;
        h.InfecteLe = maintenant;
        h.SymptomesLe = maintenant + (long)(p.IncubationJours * (0.5 + _alea.Reel()) * Calendrier.SecondesParJour);
        h.GuerisonLe = h.SymptomesLe + (long)(p.DureeJours * (0.6 + 0.8 * _alea.Reel()) * Calendrier.SecondesParJour);
        h.InfectionEvenementId = evenement;
    }

    private Pathogene? PathogeneDe(Humain h) => h.InfectionId > 0 && h.InfectionId <= _pathogenes.Count ? _pathogenes[h.InfectionId - 1] : null;

    private string NomPathogene(int id) => id > 0 && id <= _pathogenes.Count ? _pathogenes[id - 1].Nom : "la maladie";

    /// <summary>
    /// Chaque heure : les malades contagieux (dès la veille des premiers signes, et jusqu'à la guérison) contaminent
    /// ceux qui sont sur leur case, plus rarement sur une case voisine ; les maladies progressent ; on en meurt ou on guérit.
    /// </summary>
    private void Contagion(List<Humain> humains, long maintenant)
    {
        if (_pathogenes.Count == 0) return;
        var malades = humains.Where(h => h.EstVivant && h.InfectionId != 0).ToList();
        if (malades.Count == 0)
        {
            foreach (var c in _liste.Where(c => c.Active && c.Type == TypeCatastrophe.Epidemie).ToList()) Terminer(c, humains, maintenant);
            return;
        }

        // Qui est où : les contacts se font sur la même case ou une case voisine.
        var parCase = new Dictionary<int, List<Humain>>();
        foreach (var h in humains)
        {
            if (!h.EstVivant) continue;
            var i = _carte.Indice(h.X, h.Y);
            if (!parCase.TryGetValue(i, out var l)) parCase[i] = l = [];
            l.Add(h);
        }

        foreach (var m in malades)
        {
            var p = PathogeneDe(m);
            if (p is null || maintenant < m.SymptomesLe - Calendrier.SecondesParJour || maintenant >= m.GuerisonLe) continue;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = m.X + dx, ny = m.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= _carte.Largeur || ny >= _carte.Hauteur) continue;
                    if (!parCase.TryGetValue(_carte.Indice(nx, ny), out var voisins)) continue;
                    var risque = p.Transmission * (dx == 0 && dy == 0 ? 1 : 0.3);
                    foreach (var v in voisins)
                    {
                        if (v.Id == m.Id || v.InfectionId != 0 || EstImmunise(v, p, maintenant) || _alea.Reel() >= risque) continue;
                        var id = Noter(maintenant, v, $"{v.Nom} attrape {p.Nom} auprès de {m.Nom}.", m.InfectionEvenementId,
                            type: TypeEvenement.Maladie);
                        Infecter(v, p, maintenant, id);
                    }
                }
            }
        }

        foreach (var m in malades)
        {
            if (!m.EstVivant) continue;
            var p = PathogeneDe(m);
            if (p is null)
            {
                m.InfectionId = 0;
                continue;
            }
            // Premiers signes : la maladie affaiblit comme toute autre (voir Comportement).
            if (maintenant >= m.SymptomesLe && m.MaladeJusqua < m.GuerisonLe)
            {
                m.MaladeJusqua = m.GuerisonLe;
                Noter(maintenant, m, $"{m.Nom} tombe malade : {p.Nom}.", m.InfectionEvenementId, type: TypeEvenement.Maladie);
            }
            if (maintenant < m.GuerisonLe) continue;
            Guerir(m, p, humains, maintenant);
        }

        Epidemies(humains, maintenant);
    }

    /// <summary>
    /// Fin de la maladie : on en meurt (risque bien plus grand pour les tout-petits, les vieillards, les affamés et les
    /// affaiblis, comme pour la plupart des infections) ou on guérit, immunisé.
    /// </summary>
    private void Guerir(Humain m, Pathogene p, List<Humain> humains, long maintenant)
    {
        var age = m.AgeEnAnnees(maintenant);
        var fragilite = (age < 5 ? 2.5 : age < 15 ? 0.6 : age > 60 ? 2.5 : age > 45 ? 1.5 : 1)
                        * (m.Reserves < 20 ? 1.8 : 1) * (1.3 - 0.6 * m.Personnalite.Endurance) * (m.Sante < 50 ? 1.5 : 1);
        var evenement = m.InfectionEvenementId;
        if (_alea.Reel() < Math.Min(0.95, p.Letalite * fragilite) && Comportement is not null)
        {
            // Le mort garde la maladie qui l'a emporté (pour le bilan de chaque maladie).
            Comportement.Tuer(m, maintenant, CauseDeMort.Maladie, $"{m.Nom} meurt de {p.Nom}, à {Comportement.AgeEnMots(m, maintenant)}.", evenement);
            if (_liste.FirstOrDefault(c => c.Active && c.Type == TypeCatastrophe.Epidemie && c.Parametre == p.Id) is { } epidemie)
            {
                epidemie.Morts++;
                if (m.CommunauteId > 0)
                {
                    epidemie.PertesParCommunaute[m.CommunauteId] = epidemie.PertesParCommunaute.GetValueOrDefault(m.CommunauteId) + 1;
                }
            }
            return;
        }
        m.InfectionId = 0;
        m.InfectionEvenementId = 0;
        m.MaladeJusqua = Math.Min(m.MaladeJusqua, maintenant);
        m.Immunites.RemoveAll(i => i.PathogeneId == p.Id);
        m.Immunites.Add(new Immunite(p.Id, p.ImmuniteAns > 0 ? maintenant + (long)(p.ImmuniteAns * Calendrier.SecondesParAn) : null));
        Noter(maintenant, m, $"{m.Nom} guérit de {p.Nom}.", evenement, type: TypeEvenement.Maladie);
    }

    /// <summary>
    /// Une maladie qui touche au moins quatre personnes à la fois devient une épidémie : elle entre dans l'histoire
    /// des communautés touchées, avec ses morts.
    /// </summary>
    private void Epidemies(List<Humain> humains, long maintenant)
    {
        foreach (var groupe in humains.Where(h => h.EstVivant && h.InfectionId != 0).GroupBy(h => h.InfectionId).OrderBy(g => g.Key))
        {
            var p = _pathogenes[groupe.Key - 1];
            var malades = groupe.ToList();
            var epidemie = _liste.FirstOrDefault(c => c.Active && c.Type == TypeCatastrophe.Epidemie && c.Parametre == p.Id);
            var cx = (int)Math.Round(malades.Average(h => h.X));
            var cy = (int)Math.Round(malades.Average(h => h.Y));
            var rayon = (int)Math.Ceiling(malades.Max(h => Distance(h.X, h.Y, cx, cy))) + 2;
            if (epidemie is null)
            {
                if (malades.Count < 4) continue;
                var premier = malades.MinBy(h => h.InfecteLe)!;
                epidemie = Commencer(TypeCatastrophe.Epidemie, cx, cy, rayon, malades.Count, $"Épidémie de {p.Nom}",
                    $"{Majuscule(p.Nom)} se répand {Ou(cx, cy)} : déjà {malades.Count} malades.", humains, maintenant,
                    cause: premier.InfectionEvenementId, parametre: p.Id);
            }
            epidemie.Rayon = Math.Max(epidemie.Rayon, rayon);
            foreach (var h in malades)
            {
                if (!epidemie.Temoins.Add(h.Id)) continue;
                Toucher(epidemie, h);
            }
        }
        foreach (var c in _liste.Where(c => c.Active && c.Type == TypeCatastrophe.Epidemie).ToList())
        {
            if (!humains.Any(h => h.EstVivant && h.InfectionId == c.Parametre)) Terminer(c, humains, maintenant);
        }
    }
}
