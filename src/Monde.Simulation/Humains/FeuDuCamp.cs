using Monde.Simulation.Monde;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce que le feu apprend, au-delà de la chaleur. Chez les chasseurs-cueilleurs, le feu du camp brûle presque chaque soir,
/// qu'il fasse froid ou non : il éclaire la veillée, cuit, tient les bêtes et les insectes à distance (Wiessner, 2014,
/// chez les Ju/'hoansi). Sans cette habitude, qui ne sait allumer le feu que pour se chauffer le perd dans un pays chaud.
/// <para>
/// Ce que les humains peuvent en tirer, par l'observation et l'essai : la fumée chasse les moustiques (elle réduit les
/// piqûres sans les supprimer ; l'effet sur le paludisme est une estimation) ; la viande suspendue dans la fumée se garde
/// des semaines au lieu de pourrir en trois jours ; devant les flammes, les bêtes fuient et se découvrent, et l'on chasse
/// mieux sur l'herbe qui brûle (chez les Martu, les femmes brûlent les herbes pour débusquer le gibier, et la chasse y
/// rapporte nettement plus : Bird et al., 2005). Mettre le feu exprès a ses risques : il va où le vent le pousse.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Terres humides et chaudes, où les moustiques abondent.</summary>
    private static bool Moustiques(Biome b) => b is Biome.Marais or Biome.ForetTropicale or Biome.PlaineDeFougeres or Biome.Riviere or Biome.Mangrove;

    private bool FoyerProche(int x, int y)
    {
        foreach (var f in Installations.Foyers) if (Distance(f.X, f.Y, x, y) <= 1) return true;
        return false;
    }

    /// <summary>
    /// Au calme, on nourrit le feu du camp qui faiblit ; le soir, celui qui sait faire le feu l'allume au camp s'il n'y en a
    /// pas déjà un : pour la lumière et la veillée, pour cuire, et contre les moustiques s'il sait que la fumée les chasse.
    /// Vrai s'il s'y est mis.
    /// </summary>
    private bool FeuDuSoir(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || h.CaptifDe != 0 || !APortee(h, Matiere.BoisSec)) return false;
        // Le feu du camp qui faiblit : qui sait le nourrir y remet du bois, de jour comme de nuit. C'est ainsi qu'on garde
        // un feu qu'on ne sait pas rallumer.
        if (Installations.FoyerEn(h.X, h.Y) is { Heures: < 3 } && h.Sait(Effet.Foyer) && alea.Reel() < 0.1)
        {
            h.Activite = Activite.Veiller;
            h.CibleX = h.CibleY = -1;
            Faire(h, Geste.Entasser, Matiere.BoisSec, Matiere.Feu, OrigineSavoir.Essai, tous);
            return true;
        }
        var heure = Calendrier.Decomposer(_maintenant).Heure;
        if (heure < 17 || heure > 22) return false;
        var porte = FeuPorte(h);
        var flamme = h.Savoirs.Find(s => s.Effet == Effet.Flamme && s.Confiance >= 0.3);
        if (FoyerProche(h.X, h.Y)) return false;
        // Une braise couve encore au camp : on la ranime plutôt que d'allumer un autre feu.
        if (RanimerLaBraise(h, tous)) return true;
        if (porte is null && flamme is null) return false;
        // Qui a appris ce que fait le vent sur l'herbe sèche attend qu'il tombe, s'il n'a pas froid.
        if (AttendreLeCalme(h, tous)) return false;
        var moustiques = Moustiques(carte.BiomeEn(h.X, h.Y)) && _temperature >= 18;
        var motif = 1 + (moustiques && h.Sait(Effet.Fumee) ? 2 : 0) + (h.Porte(Matiere.Carcasse) ? 1 : 0) + (h.CommunauteId != 0 ? 0.5 : 0);
        if (alea.Reel() >= 0.02 * (0.5 + h.Personnalite.Conscience) * motif) return false;
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        if (porte is { } p && h.Sait(Effet.Foyer)) Faire(h, Geste.Entasser, Matiere.BoisSec, p, OrigineSavoir.Essai, tous);
        else if (flamme is not null && APortee(h, flamme.A) && APortee(h, flamme.B) && (flamme.Geste != Geste.Frapper || APortee(h, Matiere.HerbeSeche)))
        {
            // La flamme née, qui sait faire durer un feu y met aussitôt du bois pour la soirée.
            if (Faire(h, flamme.Geste, flamme.A, flamme.B, OrigineSavoir.Essai, tous) == Effet.Flamme && h.Sait(Effet.Foyer))
            {
                var (x, y, id, le, bois) = (h.X, h.Y, h.Id, _maintenant, 4 + 4 * alea.Reel());
                Tour.Differer(() => Installations.Nourrir(x, y, bois, le, id, 0));
            }
        }
        else return false;
        return true;
    }

    /// <summary>
    /// Pendant son sommeil près du feu, en terre à moustiques : il peut remarquer qu'ils piquent moins dans la fumée.
    /// La fumée évite une part des piqûres (estimation : un tiers des paludismes).
    /// </summary>
    private void SentirLaFumee(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Activite != Activite.Dormir || h.EstEnfant(_maintenant) || h.Sait(Effet.Fumee)) return;
        if (!Moustiques(carte.BiomeEn(h.X, h.Y)) || _temperature < 18 || Installations.FoyerEn(h.X, h.Y) is null) return;
        if (alea.Reel() >= 0.0005 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h))) return;
        Retenir(h, Geste.Approcher, Matiere.Aucune, Matiere.Feu, Effet.Fumee, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            recit: new Recit("feu.moustiques", ("h", h)));
    }

    /// <summary>Un feu brûle sous la hutte où il se tient : la fumée reste à l'intérieur.</summary>
    private bool FumeeInterieure(Humain h) =>
        Installations.FoyerEn(h.X, h.Y) is not null && Installations.HutteEn(h.X, h.Y) is not null;

    /// <summary>Part des paludismes qu'évite le sommeil dans la fumée d'un feu (estimation).</summary>
    private double ProtectionFumee(Humain h, Maladie m) =>
        m == Maladie.Paludisme && FoyerProche(h.X, h.Y) ? 0.33 : 0;

    /// <summary>
    /// Près d'un feu, avec de la viande sur soi : qui sait la fumer la suspend dans la fumée ; un curieux peut le découvrir
    /// en l'y laissant (elle n'a pas pourri quelques jours plus tard). Vrai s'il s'y est mis.
    /// </summary>
    private bool FumerLaViande(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.EstEnfant(_maintenant) || Installations.FoyerEn(h.X, h.Y) is null) return false;
        var i = h.Objets.FindIndex(o => o.Matiere == Matiere.Carcasse && !o.Plein);
        if (i < 0) return false;
        var sait = h.Savoirs.Exists(s => s.Effet == Effet.Fumage && s.Confiance >= 0.4);
        if (!sait && alea.Reel() >= 0.003 * (0.3 + h.Personnalite.Ouverture) * (0.5 + Ingeniosite(h))) return false;
        h.Objets[i] = h.Objets[i] with { Plein = true, Usure = 1 };
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        var id = Retenir(h, Geste.Fumer, Matiere.Carcasse, Matiere.Feu, Effet.Fumage, 0, Symptome.Aucun, OrigineSavoir.Essai, 0, tous,
            recit: new Recit("feu.fumage", ("h", h)));
        Montrer(h, Geste.Fumer, Matiere.Carcasse, Matiere.Feu, Effet.Fumage, 0, Symptome.Aucun, tous, id);
        return true;
    }

    /// <summary>
    /// Après une bonne prise, qui sait fumer la viande en garde une part pour les jours maigres, s'il peut la porter
    /// (elle se fumera au prochain feu).
    /// </summary>
    private void GarderDeLaViande(Humain h, double mange)
    {
        if (h.Porte(Matiere.Carcasse) || !h.Savoirs.Exists(s => s.Effet is Effet.Fumage or Effet.Sechage or Effet.CacheFroide && s.Confiance >= 0.5)) return;
        // Une bête de taille moyenne pour ce gibier (de 25 à 75 kg) ; la moitié environ se mange.
        var viande = Math.Max(0, 0.5 * (25 + 50 * alea.Reel()) - mange);
        var garde = Math.Min(viande, Lois.PorterABras(Force(h, _maintenant)));
        if (garde < 2) return;
        var unites = Ecosysteme.Prelever(ecosysteme.Gibier, carte.Indice(h.X, h.Y), Math.Clamp((int)Math.Ceiling(garde / KgParPart), 1, 30));
        garde = Math.Min(garde, unites * KgParPart);
        if (garde < 1) return;
        Prendre(h, new Objet(Matiere.Carcasse, Math.Clamp(garde / 100, 0, 1), 1));
    }

    /// <summary>Sur une terre qui brûle ou fume encore, les bêtes débusquées sont plus faciles à prendre.</summary>
    private bool TerreEnFeu(Humain h) =>
        Catastrophes is { } c && (c.BruleAutour(h.X, h.Y) || c.BraisesEn(h.X, h.Y, _maintenant));

    /// <summary>Une prise sur une terre en feu : un esprit attentif fait le lien entre les flammes et la chasse.</summary>
    private void ChasseAuFeu(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Sait(Effet.Brulis) || alea.Reel() >= 0.3 * (0.3 + Ingeniosite(h))) return;
        Retenir(h, Geste.Incendier, Matiere.HerbeSeche, Matiere.Feu, Effet.Brulis, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, tous,
            recit: new Recit("feu.brulis", ("h", h)));
    }

    /// <summary>
    /// Affamé, sans gibier à portée, sur des herbes sèches : qui sait que le feu débusque les bêtes et sait l'allumer y met
    /// le feu. L'incendie part après le pas, et va où le vent le pousse. Vrai s'il l'a fait.
    /// </summary>
    private bool Incendier(Humain h, IReadOnlyList<Humain> tous)
    {
        if (Catastrophes is null || h.EstEnfant(_maintenant) || h.Faim < 50 || TerreEnFeu(h)) return false;
        if (!h.Savoirs.Exists(s => s.Effet == Effet.Brulis && s.Confiance >= 0.5)) return false;
        if (carte.BiomeEn(h.X, h.Y) is not (Biome.PlaineDeFougeres or Biome.ForetDeConiferes or Biome.ForetDeFeuillus or Biome.Lande) || Sec(h) < 0.6) return false;
        if (ecosysteme.Gibier[carte.Indice(h.X, h.Y)] >= 2000) return false;
        var flamme = h.Savoirs.Find(s => s.Effet == Effet.Flamme && s.Confiance >= 0.3 && APortee(h, s.A) && APortee(h, s.B));
        if (!PorteLeFeu(h) && flamme is null) return false;
        if (AttendreLeCalme(h, tous)) return false;
        // La prudence retient : le feu peut se retourner contre les siens.
        if (alea.Reel() >= 0.05 * (1.2 - h.Personnalite.Prudence)) return false;
        h.Activite = Activite.Chasser;
        h.CibleX = h.CibleY = -1;
        var id = Noter(_maintenant, TypeEvenement.Catastrophe, h, null,
            new Recit("feu.debusquer", ("h", h)));
        var (x, y, le) = (h.X, h.Y, _maintenant);
        Tour.Differer(tous, t =>
        {
            if (Catastrophes is { } c && t is List<Humain> l) c.MettreLeFeu(x, y, l, le, id);
        });
        return true;
    }
}
