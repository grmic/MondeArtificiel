using Monde.Simulation.Monde;
using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Ce que le corps endure dehors, et les idées qui en naissent (0.42.6, mondes à matière : génération 2 et plus). Rien n'y dit
/// de se chausser, de s'enduire ni de se couvrir : le sol meurtrit les pieds tendres, le soleil brûle les peaux claires, les
/// moustiques piquent au crépuscule. Celui qui souffre essaie, remarque, imagine ; ce qu'il trouve, les autres le voient et
/// l'imitent, ou l'oublient.
/// <para>
/// Ce que dit la recherche. La marche pieds nus forme une corne épaisse qui protège la plante sans lui ôter sa sensibilité
/// (Holowka et al., 2019, chez des Kényans qui vont pieds nus) ; elle se perd chez qui se chausse. Les plus anciennes sandales
/// conservées sont des nattes d'écorce tressée liées au pied (Fort Rock, Oregon, vers 10 000 ans) ; l'anatomie des orteils
/// suggère des chaussures dès 40 000 ans (Trinkaus, 2005). Une peau sombre, riche en mélanine, brûle bien moins au soleil
/// (Jablonski et Chaplin, 2000). L'ocre mêlée de graisse protège la peau des ultraviolets (Rifkin et al., 2015) ; les Himba
/// s'en enduisent chaque jour (otjize). La boue séchée garde des piqûres et du soleil. Se peindre le corps est l'une des plus
/// anciennes parures connues, et marque souvent l'appartenance au groupe (Watts, 2009).
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Ce que le sol fait aux pieds nus, de 0 (herbe, mousse) à 1 (rocaille) ; le sable chauffé à blanc brûle en plus.</summary>
    private static double Rudesse(Biome b, double temperature, bool midi) => b switch
    {
        Biome.Montagne => 1.0,
        Biome.SommetEnneige => 0.8,
        Biome.Desert => 0.7 + (midi && temperature >= 33 ? 0.6 : 0),
        Biome.Lande => 0.6,
        Biome.Mangrove => 0.5,
        Biome.ForetTropicale => 0.35,
        Biome.ForetDeConiferes or Biome.Riviere => 0.3,
        Biome.Plage => 0.25 + (midi && temperature >= 33 ? 0.5 : 0),
        Biome.ForetDeFeuillus or Biome.PlaineDeFougeres => 0.2,
        Biome.Marais => 0.15,
        _ => 0.2,
    };

    /// <summary>Ce qui arrive de soleil sur la peau en plein jour, selon le couvert : nul sous les arbres serrés, fort sur le sable et la neige.</summary>
    private static double Exposition(Biome b) => b switch
    {
        Biome.SommetEnneige => 1.2,
        Biome.Plage => 1.1,
        Biome.Desert => 1.0,
        Biome.Montagne => 0.9,
        Biome.Lande => 0.8,
        Biome.PlaineDeFougeres or Biome.Riviere or Biome.Lac or Biome.EauPeuProfonde => 0.7,
        Biome.Marais => 0.6,
        Biome.Mangrove => 0.4,
        Biome.ForetDeConiferes => 0.3,
        Biome.ForetDeFeuillus => 0.25,
        Biome.ForetTropicale => 0.15,
        _ => 0.7,
    };

    /// <summary>Activités où l'on marche, dans sa case ou vers une autre.</summary>
    private static bool AuPas(Humain h) =>
        h.CibleX >= 0 || h.Activite is Activite.Cueillir or Activite.Chasser or Activite.ChercherNourriture or Activite.Explorer or Activite.Pecher
            or Activite.Cultiver;

    /// <summary>Ce que l'enduit de la peau retient du soleil et des piqûres (boue, ocre), s'il tient encore.</summary>
    private double Enduit(Humain h, bool contreLeSoleil)
    {
        if (h.Enduit == Matiere.Aucune || h.EnduitJusqua < _maintenant) return 0;
        return h.Enduit switch
        {
            Matiere.Ocre => contreLeSoleil ? 0.7 : 0.4,
            Matiere.Argile => contreLeSoleil ? 0.55 : 0.6,
            _ => 0.3,
        };
    }

    /// <summary>Ce que le vêtement couvre de peau (pour le soleil et les piqûres, pas pour le froid).</summary>
    private static double Couvre(Humain h) => h.Vetement is { } v ? (v.Matiere == Matiere.Peau ? 0.6 : 0.35) * (0.5 + 0.5 * v.Usure) : 0;

    /// <summary>Ce que les chaussures portées épargnent aux pieds.</summary>
    private static double Semelle(Humain h) => h.Chaussures is { } c ? Facons.Valeur(Fonction.Chausser, c) : 0;

    /// <summary>
    /// Les gênes du corps pendant ces pas : pieds meurtris, coups de soleil, piqûres ; la corne qui se forme ou se perd ; l'usure
    /// des chaussures ; l'enduit que l'eau emporte. Quelques remarques en passant : la peau enduite n'a pas brûlé.
    /// </summary>
    private void Inconforts(Humain h, long maintenant, int pas)
    {
        if (!h.EstVivant || h.Mer.EnMer) return;
        var age = h.AgeEnAnnees(maintenant);
        if (h.Cal < 0)
        {
            // Avant la 0.42.6, chacun allait déjà pieds nus : sa plante s'est endurcie avec les années.
            h.Cal = age < 2 ? 0 : Math.Clamp(Math.Min(1, age / 12) * (0.55 + 0.35 * alea.Reel()), 0, 1);
        }
        if (h.EstPetitEnfant(maintenant) && h.Activite is Activite.PorteParSaMere) return;
        var instant = Calendrier.Decomposer(maintenant);
        var midi = instant.EstJour && instant.Heure is >= 10 and < 16;
        var biome = carte.BiomeEn(h.X, h.Y);
        var temperature = _temperature;

        // Les pieds : la marche sur un sol rude les meurtrit, d'autant plus que la plante est tendre et nue.
        var semelle = Semelle(h);
        if (h.Activite == Activite.Nager || biome is Biome.Lac or Biome.EauPeuProfonde) { }
        else if (AuPas(h))
        {
            var rude = Rudesse(biome, temperature, midi);
            var protege = 1 - (1 - 0.75 * h.Cal) * (1 - semelle);
            h.Pieds = Math.Min(100, h.Pieds + pas * 1.2 * rude * (1 - protege));
            // Pieds nus, la corne se forme ; chaussé, elle se perd peu à peu.
            h.Cal = semelle > 0.3
                ? Math.Max(0, h.Cal - pas * 0.0002 * semelle)
                : Math.Min(1, h.Cal + pas * 0.0004 * (0.5 + rude) * (1 - h.Cal));
            if (h.Chaussures is { } c && Facons.LireObjet(c.Pieces) is { } x)
            {
                // Environ six heures de marche par jour : une paire tient tant de jours de marche, plus ou moins selon le sol.
                var usure = pas * (0.5 + rude) / (36 * Facons.JoursDeMarche(x.Facture.Principale));
                h.Chaussures = c.Usure - usure <= 0 ? null : c with { Usure = c.Usure - usure };
                if (h.Chaussures is null) RenforcerFacture(h, c);
            }
        }
        else h.Pieds = Math.Max(0, h.Pieds - pas * (h.Activite is Activite.Repos or Activite.Dormir or Activite.Veiller ? 0.6 : 0.2));

        // Le soleil : en plein jour, sous un ciel ouvert, la peau claire brûle bien plus vite que la sombre.
        var saison = instant.Saison switch { Saison.Ete => 1.0, Saison.Hiver => 0.4, _ => 0.7 };
        var soleil = 0.0;
        if (midi && temperature >= 18 && AbriEn(h.X, h.Y) < 0.4 && h.Activite != Activite.Dormir)
            soleil = 0.4 * Exposition(biome) * saison * (1 - 0.85 * h.Apparence.Teint);
        var contreSoleil = 1 - (1 - Couvre(h)) * (1 - Enduit(h, true));
        if (soleil > 0) h.CoupDeSoleil = Math.Min(100, h.CoupDeSoleil + pas * soleil * (1 - contreSoleil));
        else h.CoupDeSoleil = Math.Max(0, h.CoupDeSoleil - pas * 0.1);

        // Les moustiques : au crépuscule et la nuit, là où l'eau dort et où il fait chaud ; la fumée les chasse.
        var soir = !instant.EstJour || instant.Heure is >= 17 and < 21 or >= 4 and < 7;
        var piqures = 0.0;
        if (soir && temperature >= 18 && Moustiques(biome))
            piqures = 0.4 * (Installations.FoyerEn(h.X, h.Y) is not null ? 0.3 : 1);
        var contrePiqures = 1 - (1 - 0.5 * Couvre(h)) * (1 - Enduit(h, false));
        if (piqures > 0) h.Piqures = Math.Min(100, h.Piqures + pas * piqures * (1 - contrePiqures));
        else h.Piqures = Math.Max(0, h.Piqures - pas * 0.15);

        // L'eau emporte l'enduit.
        if (h.Activite == Activite.Nager && h.Enduit != Matiere.Aucune) h.Enduit = Matiere.Aucune;
        if (h.Enduit != Matiere.Aucune && h.EnduitJusqua < maintenant) h.Enduit = Matiere.Aucune;

        // La peau enduite n'a pas brûlé, ou n'a pas été piquée : un esprit attentif le remarque, et le retient.
        if (h.Enduit != Matiere.Aucune && (soleil > 0.8 || piqures > 0.8) && !h.Savoirs.Exists(s => s.Effet == Effet.Enduit && s.A == h.Enduit)
            && alea.Reel() < pas * 0.0003 * (0.3 + h.Personnalite.Ouverture))
        {
            var cle = soleil > 0.8 ? "ing.remarque.Enduit.soleil" : "ing.remarque.Enduit.piqures";
            var ev = Retenir(h, Geste.SeCouvrir, h.Enduit, Matiere.Aucune, Effet.Enduit, 0, Symptome.Aucun, OrigineSavoir.Observation, 0, _tous,
                recit: new Recit(cle, ("h", h), ("a", Physique.Nom(h.Enduit))));
            Montrer(h, Geste.SeCouvrir, h.Enduit, Matiere.Aucune, Effet.Enduit, 0, Symptome.Aucun, _tous, ev);
        }
    }

    /// <summary>S'enduire la peau de boue ou d'ocre (pour se protéger, ou pour se parer).</summary>
    private void Enduire(Humain h, Matiere m, bool parure)
    {
        h.Enduit = m;
        h.EnduitPare = parure;
        var jours = m == Matiere.Ocre ? 1 + 2 * alea.Reel() : 0.5 + alea.Reel();
        h.EnduitJusqua = _maintenant + (long)(jours * Calendrier.SecondesParJour);
    }

    /// <summary>
    /// Au calme : soulager ce qui fait mal (pieds, soleil, piqûres), avec ce qu'on sait ou en essayant, et se parer. Vrai s'il a agi.
    /// </summary>
    private bool SoulagerSonCorps(Humain h, IReadOnlyList<Humain> tous)
    {
        if (!AvecLaMatiere || h.EstPetitEnfant(_maintenant)) return false;
        var p = h.Personnalite;
        var instant = Calendrier.Decomposer(_maintenant);
        var midi = instant.EstJour && instant.Heure is >= 10 and < 16;

        // Les pieds : refaire ce qu'on sait nouer au pied, ou y penser ; trop meurtris, on s'arrête.
        if (h.Pieds >= 35 && !h.EstEnfant(_maintenant))
        {
            if (Semelle(h) < 0.3 && FaconnerPour(h, Fonction.Chausser, tous)) return true;
            if (Semelle(h) < 0.3 && InventerParCombinaison(h, Fonction.Chausser, 2 + h.Pieds / 25, tous)) return true;
        }
        if (h.Pieds >= 75 && TiragePas() < 0.5)
        {
            h.Activite = Activite.Repos;
            h.CibleX = h.CibleY = -1;
            return true;
        }

        // Le soleil ou les piqûres : s'enduire si l'on sait que cela protège, se couvrir, gagner l'ombre, ou essayer quelque chose.
        var brule = h.CoupDeSoleil >= 30 && midi;
        var pique = h.Piqures >= 30;
        if (brule || pique)
        {
            if (h.Enduit == Matiere.Aucune)
            {
                foreach (var s in h.Savoirs)
                {
                    if (s.Effet != Effet.Enduit || s.Confiance < 0.3 || !APortee(h, s.A) || TiragePas() >= 0.3 + 0.4 * p.Conscience) continue;
                    h.Activite = Activite.Fabriquer;
                    h.CibleX = h.CibleY = -1;
                    Faire(h, Geste.SeCouvrir, s.A, Matiere.Aucune, OrigineSavoir.Essai, tous);
                    return true;
                }
            }
            if (brule && h.Vetement is null)
            {
                var peau = h.Porte(Matiere.Peau) && h.Savoirs.Exists(s => s.Effet == Effet.Couvert && s.A == Matiere.Peau);
                var feuilles = APortee(h, Matiere.GrandesFeuilles) && h.Savoirs.Exists(s => s.Effet == Effet.Couvert && s.A == Matiere.GrandesFeuilles);
                if ((peau || feuilles) && TiragePas() < 0.3 + 0.4 * p.Conscience)
                {
                    h.Activite = Activite.Fabriquer;
                    h.CibleX = h.CibleY = -1;
                    Faire(h, Geste.SeCouvrir, peau ? Matiere.Peau : Matiere.GrandesFeuilles, Matiere.Aucune, OrigineSavoir.Essai, tous);
                    return true;
                }
            }
            // Essayer : la boue du bord de l'eau, la terre rouge, de grandes feuilles. Les esprits ouverts y pensent plus vite.
            if (TiragePas() < 0.0006 * (0.3 + p.Ouverture) * (1 + Math.Max(h.CoupDeSoleil, h.Piqures) / 50))
            {
                var essais = new List<Matiere>(3);
                if (APortee(h, Matiere.Argile)) essais.Add(Matiere.Argile);
                // La terre rouge est rare et se broie : on y pense bien moins souvent qu'à la boue du bord de l'eau.
                if (APortee(h, Matiere.Ocre) && (essais.Count == 0 || alea.Reel() < 0.15)) essais.Add(Matiere.Ocre);
                if (brule && h.Vetement is null && APortee(h, Matiere.GrandesFeuilles)) essais.Add(Matiere.GrandesFeuilles);
                if (essais.Count > 0)
                {
                    h.Activite = Activite.Experimenter;
                    h.CibleX = h.CibleY = -1;
                    Faire(h, Geste.SeCouvrir, essais[alea.Entier(essais.Count)], Matiere.Aucune, OrigineSavoir.Essai, tous);
                    return true;
                }
            }
            // À midi, la peau brûlée cherche l'ombre d'un arbre ou d'un abri.
            if (brule && AbriEn(h.X, h.Y) < 0.4 && Exposition(carte.BiomeEn(h.X, h.Y)) > 0.4
                && Chercher(h, j => AbriEn(j) >= 0.4 || Exposition((Biome)carte.Biomes[j]) <= 0.3, _ => 0, rayonMax: 2, scoreMax: 0) is var (ox, oy))
            {
                Avancer(h, ox, oy, Activite.SAbriter);
                if (h.CibleX >= 0) return true;
            }
        }

        // La pudeur, là où la communauté en a fait une règle : on ne reste pas nu.
        if (h.Vetement is null && !h.EstEnfant(_maintenant) && Social?.CommunauteDe(h) is { PudeurLe: > 0 }
            && TiragePas() < 0.2 + 0.5 * p.Moral.Fondements.Purete)
        {
            var peau = h.Porte(Matiere.Peau) && h.Sait(Effet.Couvert);
            var feuilles = APortee(h, Matiere.GrandesFeuilles) && h.Sait(Effet.Couvert);
            if (peau || feuilles)
            {
                h.Activite = Activite.Fabriquer;
                h.CibleX = h.CibleY = -1;
                Faire(h, Geste.SeCouvrir, peau ? Matiere.Peau : Matiere.GrandesFeuilles, Matiere.Aucune, OrigineSavoir.Essai, tous);
                return true;
            }
        }

        return SeParer(h, tous);
    }

    /// <summary>
    /// Se peindre le corps d'ocre pour être vu, en dehors de la danse : surtout les extravertis et ceux qui ont l'œil, d'autant plus
    /// que les autres au camp le font, et bien plus là où c'est devenu le signe de la communauté. Vrai s'il l'a fait.
    /// </summary>
    private bool SeParer(Humain h, IReadOnlyList<Humain> tous)
    {
        if (h.Enduit == Matiere.Ocre && h.EnduitJusqua > _maintenant || !Humains.Arts.Connait(h, Effet.Ocre)) return false;
        var p = h.Personnalite;
        var envie = 0.002 * (0.2 + p.Extraversion + 0.5 * p.Ouverture + Humains.Arts.Aptitude(h));
        if (TiragePas() >= envie * 8) return false;
        // Les autres du camp, peints ou non : on fait comme eux.
        var peints = 0;
        var presents = 0;
        foreach (var o in tous.Vivants())
        {
            if (o.Id == h.Id || !o.EstVivant || o.X != h.X || o.Y != h.Y || o.EstEnfant(_maintenant)) continue;
            presents++;
            if (o.Enduit == Matiere.Ocre && o.EnduitJusqua > _maintenant) peints++;
        }
        var imitation = presents == 0 ? 0 : (double)peints / presents;
        var signe = Social?.CommunauteDe(h) is { SigneDuCorpsLe: > 0 } ? 4 : 1;
        if (alea.Reel() >= (1 + 3 * imitation) * signe / 8 || !APortee(h, Matiere.Ocre)) return false;
        h.Activite = Activite.Fabriquer;
        h.CibleX = h.CibleY = -1;
        Faire(h, Geste.SeCouvrir, Matiere.Ocre, Matiere.Aucune, OrigineSavoir.Essai, tous);
        if (h.Enduit == Matiere.Ocre) h.EnduitPare = true;
        return true;
    }
}
