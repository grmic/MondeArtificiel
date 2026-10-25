using Monde.Simulation.Monde;

using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>Ce que deux communautés sont l'une pour l'autre, constaté chaque semaine à partir des liens entre leurs membres.</summary>
public enum EtatRelation : byte
{
    Indifferentes = 0,
    /// <summary>Leurs membres s'apprécient.</summary>
    Alliees = 1,
    /// <summary>Elles vivent sur les mêmes terres et la nourriture y manque.</summary>
    Rivales = 2,
    /// <summary>Leurs membres ne se supportent pas.</summary>
    Hostiles = 3,
    /// <summary>Le sang a coulé entre elles : raids et vengeances jusqu'à la paix ou la défaite de l'une (voir <see cref="Guerre"/>).</summary>
    EnGuerre = 4,
}

/// <summary>
/// Relation entre deux communautés (<see cref="A"/> &lt; <see cref="B"/>), et l'événement qui l'a fait naître
/// (cause d'une fusion décidée entre alliées, par exemple).
/// </summary>
/// <param name="PacteEvenementId">Pacte juré entre alliées contre un ennemi commun (0 : aucun).</param>
public sealed record RelationCommunautes(int A, int B, EtatRelation Etat, long EvenementId = 0, long PacteEvenementId = 0);

/// <summary>
/// Territoires, disettes, migrations et relations entre communautés. Rien n'est décrété : la disette se constate
/// (réserves des membres, terres épuisées), le groupe part vers des terres dont ses membres se souviennent,
/// et la relation entre deux groupes résulte des liens entre leurs membres et de la concurrence pour la nourriture.
/// Les bandes de chasseurs-cueilleurs se déplacent quand le rendement de leurs terres baisse (Kelly, 2013) et
/// les conflits entre groupes augmentent avec la pénurie (Ember et Ember, 1992).
/// </summary>
public sealed partial class VieSociale
{
    // Seuils de la disette, avec un écart entre l'entrée et la sortie pour éviter les allers-retours.
    private const double ReservesDisette = 30;
    private const double ReservesFinDisette = 40;
    private const double AbondanceDisette = 0.4;
    private const double AbondanceFinDisette = 0.5;

    // Seuils des relations (affection moyenne entre les membres des deux groupes), avec le même principe.
    private const double SeuilAlliance = 20;
    private const double FinAlliance = 10;
    private const double SeuilInimitie = -15;
    private const double FinInimitie = -5;

    /// <summary>
    /// Distance maximale des terres connues vers lesquelles un groupe en disette accepte de partir : environ 32 km,
    /// une à deux journées de marche avec les enfants. Les bandes de chasseurs-cueilleurs déplacent leur camp de
    /// quelques kilomètres à quelques dizaines de kilomètres (Kelly, 2013 ; Binford, 2001).
    /// </summary>
    private static readonly int DistanceMigrationMax = Nature.Echelle.Cases(32);

    private readonly Dictionary<(int A, int B), EtatRelation> _relations = [];
    private readonly Dictionary<(int A, int B), long> _debutsRelations = [];

    /// <summary>Communautés du monde (fournies par le monde), pour les comportements de groupe.</summary>
    public IReadOnlyList<Communaute> Communautes { get; set; } = [];

    /// <summary>Relations qui ne sont pas indifférentes, dans un ordre stable.</summary>
    public List<RelationCommunautes> Relations =>
        _relations.Where(r => r.Value != EtatRelation.Indifferentes).OrderBy(r => r.Key.A).ThenBy(r => r.Key.B)
            .Select(r => new RelationCommunautes(r.Key.A, r.Key.B, r.Value, _debutsRelations.GetValueOrDefault(r.Key),
                _pactes.GetValueOrDefault(r.Key))).ToList();

    public void ChargerRelations(IEnumerable<RelationCommunautes> relations)
    {
        _relations.Clear();
        _debutsRelations.Clear();
        _pactes.Clear();
        foreach (var r in relations)
        {
            _relations[Paire(r.A, r.B)] = r.Etat;
            if (r.EvenementId > 0) _debutsRelations[Paire(r.A, r.B)] = r.EvenementId;
            if (r.PacteEvenementId > 0) _pactes[Paire(r.A, r.B)] = r.PacteEvenementId;
        }
    }

    private static (int, int) Paire(int a, int b) => a < b ? (a, b) : (b, a);

    public EtatRelation RelationEntre(int a, int b) => _relations.GetValueOrDefault(Paire(a, b));

    /// <summary>Communauté active d'un humain, ou null.</summary>
    public Communaute? CommunauteDe(Humain h) =>
        h.CommunauteId > 0 && h.CommunauteId <= Communautes.Count && Communautes[h.CommunauteId - 1] is { Active: true } c ? c : null;

    /// <summary>
    /// Une fois par semaine, après les regroupements : territoire de chaque communauté, disette, migration,
    /// puis relations entre communautés.
    /// </summary>
    public void Territoires(List<Humain> humains, List<Communaute> communautes, Ecosysteme nature, Carte carte, long maintenant)
    {
        var actives = communautes.Where(c => c.Active).ToList();
        var membres = humains.Vivants().Where(h => h.EstVivant && h.CommunauteId != 0).GroupBy(h => h.CommunauteId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in actives)
        {
            if (!membres.TryGetValue(c.Id, out var groupe)) continue;
            var adultes = groupe.Where(h => !h.EstEnfant(maintenant)).ToList();
            if (adultes.Count == 0) continue;
            // Le camp reste où vivent ceux qui sont restés : ceux qui campent chez un autre groupe ou rendent visite à un proche
            // ne le déplacent pas. S'ils sont tous partis, il ne bouge pas.
            var auCamp = adultes.Where(h => h.VisiteChezId == 0 && !h.Rentrer && (h.RassemblementId == 0 || RassemblementDe(h)?.HoteId == c.Id)
                                            && !h.Mer.EnMer && h.Mer.Motif != MotifMer.Sejour).ToList();
            if (auCamp.Count > 0)
            {
                c.CentreX = (int)Math.Round(auCamp.Average(h => h.X));
                c.CentreY = (int)Math.Round(auCamp.Average(h => h.Y));
                c.Rayon = Math.Clamp(auCamp.Max(h => Math.Max(Math.Abs(h.X - c.CentreX), Math.Abs(h.Y - c.CentreY))), 3, 15);
            }
            // Les terres où le groupe cherche sa nourriture débordent un peu de l'endroit où il vit.
            c.Abondance = nature.Abondance(c.CentreX, c.CentreY, c.Rayon + 3);
            var reserves = adultes.Average(h => h.Reserves);
            Disette(c, groupe, reserves, nature, carte, maintenant);
        }
        foreach (var c in communautes.Where(c => !c.Active && (c.Disette || c.DestinationX >= 0)))
        {
            c.Disette = false;
            c.DestinationX = c.DestinationY = -1;
        }
        PrendreLaMer(actives, membres, carte, maintenant);
        Relier(humains, actives, membres, maintenant);
        Rencontres(humains, actives, membres, maintenant);
        Guerroyer(humains, actives, membres, maintenant);
        Exiler(humains, actives, membres, maintenant);
        Identites(humains, communautes, membres, carte, maintenant);
    }

    private void Disette(Communaute c, List<Humain> groupe, double reserves, Ecosysteme nature, Carte carte, long maintenant)
    {
        var figure = groupe.FirstOrDefault(h => h.Id == c.FigureId) ?? groupe[0];
        // Une communauté qui a mis de côté de quoi nourrir tous les siens un mois tient bon, sans partir ni désespérer.
        var vivres = Installations is null ? 0 : Comportement.JoursDeVivres(Installations.Reserves, c.Id, groupe.Count);
        if (!c.Disette && vivres < 30 && (reserves < ReservesDisette || c.Abondance < AbondanceDisette))
        {
            c.Disette = true;
            c.DisetteEvenementId = Noter(maintenant, TypeEvenement.Communaute, figure, null, c.Abondance < AbondanceDisette
                ? new Recit("terr.disette.terre", ("c", c), ("p", Libelles.Pourcent(c.Abondance)))
                : new Recit("terr.disette.faim", ("c", c)), communaute: c.Id);
        }
        else if (c.Disette && reserves > ReservesFinDisette && c.Abondance > AbondanceFinDisette)
        {
            c.Disette = false;
            c.DisettesSurmontees++;
            if (c.FusionVersId == 0) c.DestinationX = c.DestinationY = -1;
            Noter(maintenant, TypeEvenement.Communaute, figure, null, new Recit("terr.disette.fin", ("c", c)), c.DisetteEvenementId,
                communaute: c.Id);
            return;
        }
        // Un groupe en route pour en rejoindre un autre ne cherche pas d'autres terres.
        if (c.FusionVersId != 0) return;

        if (c.DestinationX >= 0 && Math.Max(Math.Abs(c.CentreX - c.DestinationX), Math.Abs(c.CentreY - c.DestinationY)) <= 4)
        {
            c.InstallationEvenementId = Noter(maintenant, TypeEvenement.Communaute, figure, null,
                new Recit("terr.installe", ("c", c), ("ou", Lieux.DirectionR(carte, c.CentreX, c.CentreY))),
                communaute: c.Id);
            c.Migrations++;
            c.DestinationX = c.DestinationY = -1;
        }
        if (!c.Disette || c.DestinationX >= 0 || c.Abondance >= AbondanceFinDisette) return;

        // Les membres mettent en commun ce dont ils se souviennent : le groupe part vers les terres connues
        // les plus riches, si elles valent nettement mieux que celles où il vit.
        var meilleur = (X: -1, Y: -1, Abondance: c.Abondance + 0.15, Qui: (Humain?)null);
        foreach (var h in groupe.Where(h => !h.EstEnfant(maintenant)).OrderBy(h => h.Id))
        {
            foreach (var l in h.Memoire.Lieux)
            {
                if (l.Type != TypeLieu.Nourriture) continue;
                var d = Math.Max(Math.Abs(l.X - c.CentreX), Math.Abs(l.Y - c.CentreY));
                if (d <= c.Rayon + 2 || d > DistanceMigrationMax) continue;
                var abondance = nature.Abondance(l.X, l.Y, 4);
                if (abondance > meilleur.Abondance) meilleur = (l.X, l.Y, abondance, h);
            }
        }
        if (meilleur.Qui is { } guide)
        {
            (c.DestinationX, c.DestinationY) = (meilleur.X, meilleur.Y);
            Noter(maintenant, TypeEvenement.Communaute, guide, null,
                new Recit("terr.part", ("c", c), ("vers", Lieux.VersR(c.CentreX, c.CentreY, meilleur.X, meilleur.Y)), ("d", Lieux.DistanceR(c.CentreX, c.CentreY, meilleur.X, meilleur.Y)), ("g", guide)),
                c.DisetteEvenementId, communaute: c.Id);
            return;
        }

        // Personne ne connaît mieux : le groupe tente sa chance plus loin, dans une direction praticable.
        for (var essai = 0; essai < 8; essai++)
        {
            var angle = alea.Reel() * 2 * Math.PI;
            // Une à deux journées de marche, comme un déplacement de camp ordinaire : 12 à 20 km.
            var distance = Nature.Echelle.Cases(12) + alea.Entier(Nature.Echelle.Cases(8) + 1);
            var x = Math.Clamp(c.CentreX + (int)Math.Round(Math.Cos(angle) * distance), 0, carte.Largeur - 1);
            var y = Math.Clamp(c.CentreY + (int)Math.Round(Math.Sin(angle) * distance), 0, carte.Hauteur - 1);
            if (!Climat.EstPraticable(carte.BiomeEn(x, y)) || carte.Plantes[carte.Indice(x, y)] + carte.Gibier[carte.Indice(x, y)] < 40) continue;
            (c.DestinationX, c.DestinationY) = (x, y);
            Noter(maintenant, TypeEvenement.Communaute, figure, null,
                new Recit("terr.aventure", ("c", c), ("vers", Lieux.VersR(c.CentreX, c.CentreY, x, y)), ("d", Lieux.DistanceR(c.CentreX, c.CentreY, x, y))),
                c.DisetteEvenementId, communaute: c.Id);
            return;
        }
    }

    /// <summary>Relations entre communautés : affection moyenne entre leurs membres, et concurrence pour les mêmes terres.</summary>
    private void Relier(List<Humain> humains, List<Communaute> actives, Dictionary<int, List<Humain>> membres, long maintenant)
    {
        var ids = actives.Select(c => c.Id).ToHashSet();
        foreach (var cle in _relations.Keys.Where(k => !ids.Contains(k.A) || !ids.Contains(k.B)).ToList()) _relations.Remove(cle);
        foreach (var cle in _debutsRelations.Keys.Where(k => !_relations.ContainsKey(k)).ToList()) _debutsRelations.Remove(cle);
        foreach (var cle in _offenses.Keys.Where(k => !ids.Contains(k.A) || !ids.Contains(k.B)).ToList()) _offenses.Remove(cle);

        // Somme et nombre des liens entre membres de deux communautés.
        var sommes = new Dictionary<(int, int), (double Somme, int Nombre)>();
        foreach (var h in humains.Vivants())
        {
            if (!h.EstVivant || h.CommunauteId == 0 || !ids.Contains(h.CommunauteId)) continue;
            foreach (var l in h.Liens)
            {
                var autre = humains[l.Id - 1];
                if (l.Id < h.Id || !autre.EstVivant || autre.CommunauteId == 0 || autre.CommunauteId == h.CommunauteId
                    || !ids.Contains(autre.CommunauteId)) continue;
                var cle = Paire(h.CommunauteId, autre.CommunauteId);
                var (somme, nombre) = sommes.GetValueOrDefault(cle);
                sommes[cle] = (somme + l.Affection, nombre + 1);
            }
        }

        for (var i = 0; i < actives.Count; i++)
        {
            for (var j = i + 1; j < actives.Count; j++)
            {
                var (a, b) = (actives[i], actives[j]);
                var cle = Paire(a.Id, b.Id);
                var avant = _relations.GetValueOrDefault(cle);
                var (somme, nombre) = sommes.GetValueOrDefault(cle);
                // Trois liens au moins pour juger ; sinon les groupes s'ignorent.
                var affection = nombre >= 3 ? somme / nombre : 0;
                var voisines = Math.Max(Math.Abs(a.CentreX - b.CentreX), Math.Abs(a.CentreY - b.CentreY)) <= a.Rayon + b.Rayon + 4;
                var concurrence = voisines && (a.Disette || b.Disette);

                // La guerre ne s'éteint pas d'elle-même : il faut la paix, une défaite ou la disparition d'un des groupes.
                if (avant == EtatRelation.EnGuerre) continue;
                // Un serment d'alliance juré sur la parole de l'Entité tient tant que l'amitié ne tourne pas à l'inimitié.
                if (avant == EtatRelation.Alliees && affection > SeuilInimitie && Serment(a.Id, b.Id)) continue;
                var apres = avant switch
                {
                    EtatRelation.Alliees when affection >= FinAlliance => EtatRelation.Alliees,
                    EtatRelation.Hostiles when affection <= FinInimitie => EtatRelation.Hostiles,
                    _ when affection >= SeuilAlliance => EtatRelation.Alliees,
                    _ when affection <= SeuilInimitie => EtatRelation.Hostiles,
                    _ when concurrence => EtatRelation.Rivales,
                    _ => EtatRelation.Indifferentes,
                };
                if (apres == avant) continue;
                if (apres == EtatRelation.Indifferentes) _relations.Remove(cle);
                else _relations[cle] = apres;
                _debutsRelations.Remove(cle);

                var figureA = humains[a.FigureId - 1];
                var figureB = humains[b.FigureId - 1];
                var texte = new Recit(apres switch
                {
                    EtatRelation.Alliees => "terr.rel.alliees",
                    EtatRelation.Rivales => "terr.rel.rivales",
                    EtatRelation.Hostiles => "terr.rel.hostiles",
                    _ => avant switch
                    {
                        EtatRelation.Alliees => "terr.rel.finAmitie",
                        EtatRelation.Rivales => "terr.rel.finRivalite",
                        _ => "terr.rel.finHostilite",
                    },
                }, ("a", a), ("b", b));
                // Une hostilité qui suit une violence entre les deux groupes cite cette violence comme cause.
                var id = Noter(maintenant, TypeEvenement.Communaute, figureA, figureB, texte,
                    apres switch
                    {
                        EtatRelation.Hostiles => _offenses.GetValueOrDefault(cle),
                        EtatRelation.Rivales => a.Disette ? a.DisetteEvenementId : b.DisetteEvenementId,
                        _ => 0,
                    }, communaute: a.Id, autreCommunaute: b.Id);
                if (apres != EtatRelation.Indifferentes) _debutsRelations[cle] = id;
            }
        }
    }
}

/// <summary>Noms de lieux pour le journal : les humains n'en ont pas encore, l'observateur oui.</summary>
public static class Lieux
{
    private static readonly string[] Directions = ["l'est", "le sud-est", "le sud", "le sud-ouest", "l'ouest", "le nord-ouest", "le nord", "le nord-est"];

    /// <summary>Direction d'un point vers un autre (« le nord-est »). Y croît vers le sud.</summary>
    public static string Vers(int x0, int y0, int x1, int y1)
    {
        var angle = Math.Atan2(y1 - y0, x1 - x0);
        var secteur = (int)Math.Round(angle / (Math.PI / 4));
        return Directions[(secteur + 8) % 8];
    }

    private static readonly string[] Cles = ["est", "sud-est", "sud", "sud-ouest", "ouest", "nord-ouest", "nord", "nord-est"];

    /// <summary>« vers le nord-est », dans la langue de l'observateur (clés <c>p.vers.*</c>).</summary>
    public static Recits.Recit VersRecit(int x0, int y0, int x1, int y1)
    {
        var secteur = (int)Math.Round(Math.Atan2(y1 - y0, x1 - x0) / (Math.PI / 4));
        return Recits.Recit.Seul("p.vers." + Cles[(secteur + 8) % 8]);
    }

    /// <summary>« le nord-est », dans la langue de l'observateur (clés <c>lieu.dir.*</c>).</summary>
    public static Recit VersR(int x0, int y0, int x1, int y1)
    {
        var secteur = (int)Math.Round(Math.Atan2(y1 - y0, x1 - x0) / (Math.PI / 4));
        return Recit.Seul("lieu.dir." + Cles[(secteur + 8) % 8]);
    }

    /// <summary>« au nord-est », « à l'est » : la direction d'un point vers un autre, dans la langue de l'observateur.</summary>
    public static Recit PartieR(int x0, int y0, int x1, int y1)
    {
        var secteur = (int)Math.Round(Math.Atan2(y1 - y0, x1 - x0) / (Math.PI / 4));
        return Recit.Seul("lieu.partie." + Cles[(secteur + 8) % 8]);
    }

    /// <summary>Partie du continent où se trouve un point (« au nord-ouest », « au centre »), dans la langue de l'observateur.</summary>
    public static Recit DirectionR(Carte carte, int x, int y) => Recit.Seul($"lieu.partie.{Coin(carte, x, y)}");

    /// <summary>Partie du continent où se trouve un point, en clé : « centre », « nord-ouest »…</summary>
    public static string Coin(Carte carte, int x, int y)
    {
        int cx = carte.Largeur / 2, cy = carte.Hauteur / 2;
        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) < carte.Largeur / 6) return "centre";
        var secteur = (int)Math.Round(Math.Atan2(y - cy, x - cx) / (Math.PI / 4));
        return Cles[(secteur + 8) % 8];
    }

    /// <summary>Distance à vol d'oiseau, à écrire dans la langue de l'observateur.</summary>
    public static Recit DistanceR(int x0, int y0, int x1, int y1) => Nature.Echelle.DistanceR(Nature.Echelle.Kilometres(x0, y0, x1, y1));

    /// <summary>Distance à vol d'oiseau en mots (« 18 km »), pour le journal.</summary>
    public static string Distance(int x0, int y0, int x1, int y1) => Nature.Echelle.Distance(Nature.Echelle.Kilometres(x0, y0, x1, y1));

    /// <summary>Partie du continent où se trouve un point (« au nord-ouest », « au centre »).</summary>
    public static string Direction(Carte carte, int x, int y)
    {
        int cx = carte.Largeur / 2, cy = carte.Hauteur / 2;
        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) < carte.Largeur / 6) return "au centre";
        var vers = Vers(cx, cy, x, y);
        return vers.StartsWith("l'", StringComparison.Ordinal) ? "à " + vers : "au " + vers[3..];
    }
}
