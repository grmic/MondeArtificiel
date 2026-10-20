using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Nature;

/// <summary>Catastrophes provoquées à la demande (par l'Entité, ou pour observer leurs effets).</summary>
public sealed partial class Catastrophes
{
    /// <summary>
    /// Provoque une catastrophe en (<paramref name="x"/>, <paramref name="y"/>). L'intensité dépend du type :
    /// magnitude (séisme), hauteur de la vague en mètres (tsunami), indice d'explosivité (éruption du volcan le plus
    /// proche), ampleur 1 à 3 (crue), catégorie 1 à 5 (tempête), écart en °C (froid, canicule), mégatonnes (impact).
    /// Une épidémie commence par le vivant le plus proche, atteint d'une maladie nouvelle.
    /// Renvoie le numéro de l'événement qui l'annonce, ou 0 si elle n'a pas pu avoir lieu (pas de volcan, rien à brûler…).
    /// </summary>
    public long Declencher(TypeCatastrophe type, int x, int y, double intensite, List<Humain> humains, long maintenant, Origine origine)
    {
        x = Math.Clamp(x, 0, _carte.Largeur - 1);
        y = Math.Clamp(y, 0, _carte.Hauteur - 1);
        if (!double.IsFinite(intensite)) intensite = 0;
        long id = type switch
        {
            TypeCatastrophe.Seisme => Seisme(x, y, Math.Clamp(intensite, 3, 9.5), humains, maintenant, origine).Id,
            TypeCatastrophe.Tsunami => Tsunami(x, y, Math.Clamp(intensite, 1, 40), 0, humains, maintenant, origine)?.Id ?? 0,
            TypeCatastrophe.Eruption => Geologie.Volcans.MinBy(v => Distance(v.X, v.Y, x, y)) is { } v
                && !_liste.Any(c => c.Active && c.Type == TypeCatastrophe.Eruption && c.Parametre == v.Numero)
                ? Eruption(v, (int)Math.Clamp(Math.Round(intensite), 1, 7), humains, maintenant, origine).Id : 0,
            TypeCatastrophe.Crue => Crue(_ecosysteme.Region(x, y), (int)Math.Clamp(Math.Round(intensite), 1, 3), humains, maintenant, origine, 0)?.Id ?? 0,
            TypeCatastrophe.Incendie => Inflammabilite(_carte.BiomeEn(x, y)) > 0 && !EstDangereuse(_carte.Indice(x, y))
                ? Allumer(x, y, humains, maintenant, origine, 0).Id : 0,
            TypeCatastrophe.Cyclone => Cyclone(x, y, (int)Math.Clamp(Math.Round(intensite), 1, 5), humains, maintenant, origine).Id,
            TypeCatastrophe.VagueDeFroid => VagueDeFroid(-Math.Clamp(Math.Abs(intensite), 3, 20), 7, humains, maintenant, origine).Id,
            TypeCatastrophe.Canicule => Canicule(Math.Clamp(Math.Abs(intensite), 3, 15), 7, humains, maintenant, origine).Id,
            TypeCatastrophe.Eclipse => Eclipse(1, humains, maintenant, origine).Id,
            TypeCatastrophe.Comete => Comete(humains, maintenant, origine).Id,
            TypeCatastrophe.Aurore => Aurore(humains, maintenant, origine).Id,
            TypeCatastrophe.Bolide => Bolide(humains, maintenant, origine).Id,
            TypeCatastrophe.Impact => Impact(x, y, Math.Clamp(intensite, 0.1, EnergieMax), humains, maintenant, origine).Id,
            TypeCatastrophe.Epidemie => Contaminer(x, y, humains, maintenant, origine),
            _ => 0,
        };
        CalculerClimat();
        CalculerDangers();
        return id;
    }

    /// <summary>Le vivant le plus proche tombe malade d'une maladie nouvelle (le premier malade d'une épidémie).</summary>
    private long Contaminer(int x, int y, List<Humain> humains, long maintenant, Origine origine)
    {
        var h = humains.Vivants().Where(h => h.EstVivant && h.InfectionId == 0).MinBy(h => Distance(h.X, h.Y, x, y));
        if (h is null) return 0;
        var p = NouveauPathogene(h, maintenant);
        var id = Noter(maintenant, h, $"{h.Nom} tombe malade d'un mal inconnu : {p.Nom} vient d'apparaître.", type: TypeEvenement.Maladie, origine: origine);
        _pathogenes[^1] = p with { EvenementId = id };
        Infecter(h, _pathogenes[^1], maintenant, id);
        return id;
    }

    /// <summary>
    /// L'Entité rend <paramref name="h"/> malade d'une maladie contagieuse déjà apparue (<paramref name="pathogeneId"/>) ou
    /// nouvelle (0). Elle passe outre l'immunité ; la maladie suit ensuite son cours (incubation, contagion, guérison ou mort).
    /// Renvoie le numéro de l'événement, ou 0 s'il est déjà malade d'un mal contagieux.
    /// </summary>
    public long Infecter(Humain h, int pathogeneId, long maintenant, long cause)
    {
        if (!h.EstVivant || h.InfectionId != 0 || pathogeneId < 0 || pathogeneId > _pathogenes.Count) return 0;
        long id;
        if (pathogeneId == 0)
        {
            var nouveau = NouveauPathogene(h, maintenant);
            id = Noter(maintenant, h, $"{h.Nom} tombe malade d'un mal inconnu : {nouveau.Nom} vient d'apparaître.", cause, type: TypeEvenement.Maladie,
                origine: Origine.Entite);
            _pathogenes[^1] = nouveau with { EvenementId = id };
            pathogeneId = nouveau.Id;
        }
        else id = Noter(maintenant, h, $"{h.Nom} attrape {_pathogenes[pathogeneId - 1].Nom}.", cause, type: TypeEvenement.Maladie, origine: Origine.Entite);
        var p = _pathogenes[pathogeneId - 1];
        h.Immunites.RemoveAll(i => i.PathogeneId == p.Id);
        Infecter(h, p, maintenant, id);
        return id;
    }

    /// <summary>L'Entité guérit <paramref name="h"/> de son mal contagieux : il en reste protégé pendant <paramref name="jours"/>.</summary>
    public void Delivrer(Humain h, long maintenant, double jours)
    {
        if (h.InfectionId == 0) return;
        var id = h.InfectionId;
        h.Immunites.RemoveAll(i => i.PathogeneId == id);
        h.Immunites.Add(new Immunite(id, maintenant + (long)(jours * Calendrier.SecondesParJour)));
        (h.InfectionId, h.InfectionEvenementId) = (0, 0);
    }
}
