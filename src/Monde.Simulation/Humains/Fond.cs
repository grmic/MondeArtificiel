using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Les humains « de fond » (option « Vitesse : humains de fond simulés en groupe », 0.36.0). Un humain de fond garde tout
/// ce qui fait de lui quelqu'un, et son corps vit chaque pas comme celui des autres (faim, soif, fatigue, froid, santé,
/// grossesse). Mais tant qu'il est au calme, il ne décide pas à chaque pas : il reste où il est, avec les siens, et ne joue
/// son temps libre (idées, essais, gestes appris, visites, exploration) qu'une fois par heure, avec la chance de toute l'heure.
/// Au moindre besoin ou événement (soif, faim, nuit, danger, maladie, désir, parole de l'Entité, voyage, raid, visite,
/// ouvrage, nuit à passer ailleurs), il vit le pas en entier, comme tout le monde : c'est ce qu'on appelle, dans les jeux à grand monde, une
/// intelligence « à niveau de détail » (Brockington, 2002).
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Pas joués d'un coup par un humain de fond au calme : une heure.</summary>
    public const int PasParCreneau = (int)(Calendrier.SecondesParHeure / MondeSimule.SecondesParPas);

    // Humains de fond au calme pour l'heure (par numéro), et pas qu'ils n'ont pas vécus depuis : leur corps les rattrape d'un bloc
    // (faim, soif, fatigue, froid, santé) au pas suivant qu'ils vivent. Chaque humain n'écrit que sa case : sûr sur plusieurs cœurs.
    private bool[] _auCalme = [];
    private int[] _pasDus = [];

    /// <summary>Avant chaque pas : de la place pour chaque humain né (sans humains de fond, rien n'est au calme).</summary>
    internal void PreparerFond(int humains, bool actif)
    {
        if (!actif)
        {
            if (_auCalme.Length > 0) Array.Clear(_auCalme);
            return;
        }
        if (_auCalme.Length > humains) return;
        var taille = humains + 256;
        Array.Resize(ref _auCalme, taille);
        Array.Resize(ref _pasDus, taille);
    }

    /// <summary>Pas que le corps de cet humain doit rattraper (lui compris), remis à zéro.</summary>
    private int PasARattraper(Humain h)
    {
        if (h.Id >= _pasDus.Length) return 1;
        var dus = _pasDus[h.Id];
        _pasDus[h.Id] = 0;
        return 1 + dus;
    }

    /// <summary>Nombre de pas que représente le pas en cours (1, sauf pour le temps libre d'un humain de fond).</summary>
    [ThreadStatic] private static int _cadence;

    /// <summary>
    /// Un tirage pour une chance « par pas » : divisé par le nombre de pas joués d'un coup, il rend cette chance autant de fois
    /// plus probable, pour que l'humain de fond ait autant d'idées et d'essais par jour que les autres. Hors du temps libre
    /// d'un humain de fond, c'est exactement <c>alea.Reel()</c>.
    /// </summary>
    private double TiragePas() => _cadence > 1 ? alea.Reel() / _cadence : alea.Reel();

    /// <summary>
    /// Le pas d'un humain de fond. Le désir ne le réveille pas : il cherche à s'accoupler pendant son heure de temps libre
    /// (s'il doit marcher pour cela, il vit ensuite chaque pas jusqu'à l'arrivée). <paramref name="creneau"/> : c'est son heure de temps libre (une fois par heure, à un pas
    /// qui dépend de son numéro). Renvoie vrai s'il a vécu le pas en entier (pour les comptes).
    /// </summary>
    public bool VivreEnFond(Humain h, IReadOnlyList<Humain> tous, long maintenant, bool creneau)
    {
        // Au calme depuis sa dernière heure (0.40.0) : il ne vit pas ce pas ; son corps le rattrapera d'un bloc à la prochaine heure.
        if (!creneau && h.Id < _auCalme.Length && _auCalme[h.Id] && _pasDus[h.Id] < PasParCreneau - 1)
        {
            _pasDus[h.Id]++;
            return false;
        }
        if (!Commencer(h, tous, maintenant, out var partenaire)) return true;
        var calme = AuCalme(h, partenaire, tous, maintenant);
        // Pour l'heure qui vient, au calme d'un bloc : seulement s'il n'attend pas d'enfant (la grossesse se vit pas à pas)
        // et s'il ne doit pas veiller sur un petit.
        if (h.Id < _auCalme.Length) _auCalme[h.Id] = calme && h.EnceinteDepuis is null && !h.EstPetitEnfant(maintenant);
        if (!calme)
        {
            Agir(h, partenaire, tous, maintenant);
            return true;
        }
        if (creneau)
        {
            _cadence = PasParCreneau;
            try
            {
                Agir(h, partenaire, tous, maintenant);
            }
            finally
            {
                _cadence = 0;
            }
            return true;
        }
        // Au calme, entre deux heures : il ne fait que vivre là où il est. Les siens le nourrissent comme à chaque pas, et
        // la grossesse suit son cours.
        NourrirEntreProches(h, partenaire, tous, maintenant);
        if (!h.EstVivant) return false;
        Grossesse(h, tous, maintenant);
        return false;
    }

    /// <summary>
    /// Vrai si rien ne demande que l'humain vive ce pas en entier : il dort sans raison de se réveiller, ou il est éveillé,
    /// oisif, sans besoin pressant ni rien en cours. Boire l'eau qui est à ses pieds se fait ici, comme dans <see cref="Decider"/>.
    /// </summary>
    private bool AuCalme(Humain h, Humain? partenaire, IReadOnlyList<Humain> tous, long maintenant)
    {
        // Ce qui ne peut pas attendre l'heure suivante.
        if (h.Mer.EnMer || h.Impulsions.Count > 0 || h.CaptifDe != 0 || h.DemandeAId != 0 || h.VisiteChezId != 0 || h.RassemblementId > 0 || h.Rentrer)
            return false;
        if (!Climat.EstPraticable(carte.BiomeEn(h.X, h.Y)) || Catastrophes?.DangerEn(h.X, h.Y) is not null) return false;
        if (_raids.ContainsKey(h.Id)) return false;
        if (h.EstMalade(maintenant) || h.InfectionId != 0 || Medecine.PlaieOuverte(h, maintenant)) return false;
        // Un geste en cours, une marche en cours, la promenade de l'explorateur : chaque pas compte (on ramasse ce qu'on trouve
        // en chemin, on voit le pays).
        if (h.Activite is not (Activite.Repos or Activite.Dormir)) return false;
        if (h.CibleX >= 0) return false;

        var date = Calendrier.Decomposer(maintenant);
        // Le feu du camp qui faiblit, le soir celui qu'on garde pour la nuit, au matin la braise à ranimer : qui sait le
        // nourrir veille dessus pas à pas, éveillé ou réveillé par le froid du foyer.
        if (!h.EstEnfant(maintenant) && h.Sait(Effet.Foyer) && h.Activite != Activite.Dormir)
        {
            if (Installations.FoyerEn(h.X, h.Y) is { } foyer && (foyer.Heures < 3 || date.Heure is >= 20 and <= 23 && foyer.Heures < 6)) return false;
            if (date.Heure is >= 5 and <= 9 && Installations.BraiseEn(h.X, h.Y) is not null) return false;
        }
        // Qui a froid et sait d'où vient la chaleur cherche le feu, l'allume ou essaie de l'allumer, pas après pas : chaque
        // essai compte, c'est ainsi qu'on finit par le faire naître.
        if (h.Activite != Activite.Dormir && (h.Froid > 15 || _temperature < 14) && (h.Sait(Effet.Chaleur) || h.Sait(Effet.Flamme))) return false;
        if (h.Activite == Activite.Dormir)
        {
            // Même règle de réveil que dans Decider (le danger a été vu plus haut).
            var reveil = h.Soif > 80 || h.Faim > 90 || h.Froid > 80 || h.Fatigue < 5 || (date.EstJour && h.Fatigue < 35);
            if (!reveil) return true;
        }
        if (Bannissement.Exil(h, maintenant) > 0) return false;
        if (Social?.CommunauteDe(h) is { DestinationX: >= 0 }) return false;
        if (partenaire is not null && h.EstEnfant(maintenant) && Distance(h.X, h.Y, partenaire.X, partenaire.Y) > 1) return false;

        // Les besoins, comme dans Decider.
        var p = h.Personnalite;
        var abattu = h.EstDeprime || h.Chagrin > 50;
        var seuil = 0.45 - 0.2 * p.Prudence + (abattu ? 0.1 : 0);
        if (h.Soif >= 25 && EauDouceAPortee(h.X, h.Y))
        {
            h.Soif = 0;
            h.Memoire.Retenir(TypeLieu.Eau, h.X, h.Y, maintenant, Aptitude(h));
        }
        // Le besoin le plus pressant, dans le même ordre que Decider (à égalité, le premier).
        var (valeur, besoin) = (Urgence(h.Soif) * 1.2, Activite.Boire);
        Plus(Urgence(h.Faim), Activite.Cueillir, ref valeur, ref besoin);
        Plus(Urgence(h.Fatigue) * 0.9 + (date.EstJour ? 0 : 0.35), Activite.Dormir, ref valeur, ref besoin);
        Plus(Urgence(h.Froid) * (0.8 + p.Prudence * 0.4), Activite.SAbriter, ref valeur, ref besoin);
        // Réveillé dans la nuit et reposé, il se rendort là où il est, comme AllerDormir le ferait sans bouger
        // (assez abrité, près du feu, épuisé, ou peu prudent).
        if (valeur >= seuil && besoin == Activite.Dormir && valeur < 0.8 && !h.EstPetitEnfant(maintenant)
            && (AbriEn(h.X, h.Y) >= 0.3 || Installations.FoyerEn(h.X, h.Y) is not null || h.Fatigue >= 85 || p.Prudence <= 0.4))
        {
            h.Activite = Activite.Dormir;
            h.CibleX = h.CibleY = -1;
            if (AbriEn(h.X, h.Y) >= 0.4) h.Memoire.Retenir(TypeLieu.Abri, h.X, h.Y, maintenant, Aptitude(h));
            return true;
        }
        if (valeur >= seuil) return false;
        // Un petit enfant dans le besoin, que l'on doit mener à l'eau ou à manger.
        if (!h.EstEnfant(maintenant) && PetitDansLeBesoin(h, tous, maintenant) is not null) return false;
        return true;
    }
}
