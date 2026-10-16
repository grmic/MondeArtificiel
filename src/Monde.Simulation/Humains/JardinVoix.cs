using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// La voix intérieure : ce que chacun se dit, composé d'après son caractère. Une même situation ne donne pas la même
/// pensée à un anxieux et à un serein, à un cœur chaleureux et à un froid, à un esprit vif en mots et à un taiseux.
/// Le choix d'une pensée est pondéré par les traits qui la rendent plausible (Big Five, attachement adulte, empathie,
/// psychopathie, fondements moraux, aptitude verbale), puis tiré de façon stable : tant que rien ne change, la même
/// pensée revient, elle n'est pas réécrite.
/// <para>
/// Désir et amour suivent les trois systèmes de Fisher (1998) : désir (pulsion), attirance (amour passionné) et
/// attachement ; l'amour d'un couple est lu avec les trois composantes de Sternberg (1986) : intimité, passion,
/// engagement. Jalousie : Buss et al. (1992), Buss (2000) ; la différence entre les sexes est modeste
/// (Sagarin et al., 2012). Émotions morales : culpabilité tournée vers l'acte, honte tournée vers soi (Tangney et al.,
/// 2007) ; fierté authentique ou orgueilleuse (Tracy et Robins, 2007) ; envie bénigne ou malveillante
/// (van de Ven et al., 2009). Émerveillement : Keltner et Haidt (2003), plus fréquent chez les esprits ouverts
/// (Shiota et al., 2006). Rêves : leur contenu prolonge les soucis du jour (hypothèse de continuité, Schredl et
/// Hofmann, 2003) ; on s'en souvient plus quand on est ouvert (Schredl, 2003), les cauchemars sont plus fréquents chez les
/// anxieux (Levin et Nielsen, 2007). Souvenirs : la mémoire rappelle plus volontiers ce qui s'accorde à l'humeur
/// (Bower, 1981). Ennui : plus fréquent chez les impulsifs et les anxieux (Culp, 2006).
/// </para>
/// </summary>
public static partial class Jardin
{
    /// <summary>Une pensée possible, son poids selon le caractère, et son registre (0 : mots simples, 1 : courant, 2 : imagé).</summary>
    internal readonly record struct V(string Texte, double Poids, int Registre = 1);

    private const long Semaine = 7 * Calendrier.SecondesParJour;

    /// <summary>Compose les pensées de la semaine (appelée par <see cref="Mediter"/>).</summary>
    private static void Composer(Humain h, IReadOnlyList<Humain> humains, Communaute? communaute, Carte? carte, long maintenant)
    {
        var semaine = maintenant / Semaine;
        // Ce qu'on pensait d'un vivant n'a plus cours quand il meurt : reste la peine, le manque, les souvenirs.
        h.Memoire.Jardin.RemoveAll(x => x.SujetId > 0 && x.SujetId != h.Id && x.SujetId <= humains.Count && !humains[x.SujetId - 1].EstVivant
            && x.Type is TypeIntime.Opinion or TypeIntime.Desir or TypeIntime.Amour or TypeIntime.Jalousie or TypeIntime.Envie or TypeIntime.Intention or TypeIntime.Peur);
        var ideal = communaute?.Ideal ?? IdealBeaute.ParDefaut;
        Opinions(h, humains, maintenant);
        ImageDeSoi(h, humains, communaute, maintenant);
        Couple(h, humains, ideal, maintenant);
        Desirer(h, humains, ideal, maintenant);
        Jalouser(h, humains, communaute, maintenant);
        Manquer(h, humains, maintenant, semaine);
        Fierte(h, humains, communaute, ideal, maintenant, semaine);
        Fautes(h, humains, maintenant);
        Envier(h, humains, ideal, maintenant, semaine);
        Craindre(h, humains, communaute, maintenant);
        Rever(h, humains, ideal, maintenant, semaine);
        SeSouvenir(h, humains, maintenant, semaine);
        SEmerveiller(h, humains, carte, maintenant, semaine);
        SEnnuyer(h, maintenant, semaine);
        SeDemander(h, maintenant, semaine);
    }

    // ───────────── Ce qu'il pense des autres ─────────────

    private static void Opinions(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = Froid(p);
        foreach (var l in h.Liens.Where(l => Math.Abs(l.Affection) >= 40 && humains[l.Id - 1].EstVivant)
                     .OrderByDescending(l => Math.Abs(l.Affection)).Take(4))
        {
            var autre = humains[l.Id - 1];
            var grief = h.Griefs.Where(g => g.ContreId == l.Id).MaxBy(g => g.Intensite);
            // Le compagnon a sa propre rubrique (l'amour), sauf quand une rancune pèse plus que lui.
            if (h.PartenaireId == l.Id && grief is null) continue;
            var (x, il, le, lui) = (autre.Nom, Il(autre), Le(autre), Pronom(autre));
            var rel = Relation(h, autre);
            var niveau = l.Affection switch { >= 75 => 3, >= 40 => 2, <= -60 => -2, _ => -1 };
            V[] options;
            if (grief is { Motif: MotifGrief.Meurtre, VictimeId: > 0 } gm)
            {
                var v = humains[gm.VictimeId - 1].Nom;
                options =
                [
                    new($"Je hais {x} : {il} a tué {v}.", 0.6),
                    new($"Le sang de {v} crie encore ; {x} l'a versé.", 0.3, 2),
                    new($"{x} a tué {v}. Je voudrais pardonner ; je ne peux pas.", 0.05 + 2 * S(p.Agreabilite) + S(m.Compassion)),
                    new($"{x} a tué {v}. Je m'en souviendrai le moment venu.", 0.05 + 2 * Bas(p.Agreabilite) + 2 * f),
                    new($"{x} a tué {v}.", 0.3, 0),
                ];
            }
            else if (grief is { Motif: MotifGrief.Infidelite })
            {
                options =
                [
                    new($"{x} m'a trahi{E(h)} ; je ne l'oublie pas.", 0.5),
                    new($"{x} est allé{E(autre)} vers quelqu'un d'autre ; je ne vaux donc rien à ses yeux ?", 0.05 + 2 * S(m.AnxieteAffective) + S(p.Nevrosisme)),
                    new($"{x} m'a trahi{E(h)}. Je lui pardonnerai peut-être.", 0.05 + 2 * S(p.Agreabilite)),
                    new($"{x} m'a trahi{E(h)} ; je n'attendais rien de personne, de toute façon.", 0.05 + 2 * S(m.EvitementAffectif)),
                    new($"{x} a souillé notre lien.", 0.05 + 2 * S(m.Fondements.Purete), 2),
                ];
            }
            else if (grief is { Motif: MotifGrief.Coups })
            {
                options =
                [
                    new($"{x} m'a frappé{E(h)} : je me méfie {De(autre)}.", 0.5),
                    new($"{x} m'a frappé{E(h)} ; je lui rendrai ses coups.", 0.05 + 2 * Bas(p.Agreabilite)),
                    new($"J'ai peur de {x} depuis qu'{il} m'a frappé{E(h)}.", 0.05 + 2 * S(p.Nevrosisme)),
                    new($"{x} frappe ; {il} ne vaut pas mieux qu'une bête.", 0.1 + S(m.Fondements.Soin), 2),
                ];
            }
            else if (grief is { Motif: MotifGrief.Rival })
            {
                options =
                [
                    new($"{x} m'a pris ce qui était à moi.", 0.3 + S(m.Fondements.Loyaute)),
                    new($"Je ne pardonnerai pas à {x}.", 0.3 + Bas(p.Agreabilite)),
                    new($"{x} me nargue ; {il} croit avoir gagné.", 0.1 + S(p.Nevrosisme), 2),
                ];
            }
            else if (grief is { Motif: MotifGrief.Vol })
            {
                options =
                [
                    new($"{x} m'a volé ma part ; je {le} surveille.", 0.4),
                    new($"{x} prend ce qui n'est pas à {lui}.", 0.3 + S(m.Fondements.Equite)),
                    new($"La prochaine fois que {x} touche à ma part, {il} s'en souviendra.", 0.05 + 2 * Bas(p.Agreabilite)),
                ];
            }
            else if (grief is { Motif: MotifGrief.Trahison })
            {
                options =
                [
                    new($"{x} nous a quittés pour nos ennemis.", 0.4),
                    new($"{x} est un traître ; qu'{il} ne revienne jamais.", 0.1 + 2 * S(m.Fondements.Loyaute)),
                ];
            }
            else options = niveau switch
            {
                3 => Cher(h, autre, l, rel, f, maintenant),
                2 => [
                    new($"Je peux compter sur {x}.", 0.5),
                    new($"J'aime bien {x}.", 0.4, 0),
                    new($"{x} est de bonne compagnie.", 0.05 + 2 * S(p.Extraversion)),
                    new($"{x} est fiable, je crois.", 0.05 + 2 * S(p.Nevrosisme)),
                    new($"{x} est quelqu'un de bien.", 0.05 + S(m.Honnetete) + S(p.Agreabilite)),
                    new($"Je ne connais pas encore bien {x}, mais {il} me plaît.", l.Familiarite < 30 ? 1 : 0),
                    new($"{x} ne rechigne jamais à la peine ; je respecte cela.", 0.05 + 2 * S(p.Conscience)),
                    new($"{x} peut m'être utile.", 3 * f, 0),
                    new($"On s'entend bien, {x} et moi.", 0.3, 0),
                    new($"{x} sait faire des choses que j'ignore ; je devrais l'observer.", Utiles(autre) > Utiles(h) + 1 ? 0.2 + 2 * S(p.Ouverture) : 0),
                    new($"Avec {x}, le temps passe vite.", 0.1 + S(p.Extraversion), 2),
                    new($"{x} {(rel == "parent" ? "m'a élevé" + E(h) : "est des nôtres")} ; cela compte.", 0.05 + 2 * S(m.Fondements.Loyaute)),
                ],
                -2 => [
                    new($"Je ne supporte plus {x}.", 0.5),
                    new($"{x} me fait horreur.", 0.1 + S(m.Fondements.Purete)),
                    new($"La seule voix de {x} me fait serrer les dents.", 0.3, 2),
                    new($"Un jour, {x} paiera.", 0.05 + 2 * Bas(p.Agreabilite)),
                    new($"J'essaie d'aimer {x}, mais je n'y arrive pas.", 0.05 + 2 * S(p.Agreabilite) + S(m.Compassion)),
                    new($"{x} me veut du mal, j'en suis sûr{E(h)}.", 0.05 + 2 * S(p.Nevrosisme)),
                    new($"{x} est faible ; je saurai m'en servir.", 3 * f),
                    new($"{x}, je l'évite.", 0.2 + S(m.EvitementAffectif), 0),
                ],
                _ => [
                    new($"Je me méfie de {x}.", 0.5),
                    new($"{x} parle trop.", 0.1 + Bas(p.Extraversion)),
                    new($"Je n'aime pas la façon dont {x} me regarde.", 0.05 + 2 * S(p.Nevrosisme)),
                    new($"{x} n'est pas des nôtres.", autre.CommunauteId != h.CommunauteId ? 0.2 + 2 * S(m.Fondements.Loyaute) : 0),
                    new($"{x} laisse les autres faire sa part.", 0.05 + 2 * S(p.Conscience)),
                    new($"{x} m'agace, mais il faut bien vivre ensemble.", 0.1 + S(p.Agreabilite)),
                    new($"{x} ne respecte pas ceux qui sont plus âgés.", autre.Naissance > h.Naissance ? 2 * S(m.Fondements.Autorite) : 0),
                    new($"{x} ne me revient pas.", 0.3, 0),
                ],
            };
            var texte = Varier(h, $"opinion:{l.Id}", niveau * 100 + (int)(grief?.Motif ?? (MotifGrief)99), x, options);
            if (texte is not null)
                Confier(h, TypeIntime.Opinion, l.Id, texte, Math.Abs(l.Affection), grief?.EvenementId ?? 0, maintenant, $"opinion:{l.Id}");
        }
    }

    /// <summary>Ceux qui comptent le plus : la pensée dépend du lien (enfant, parent, frère ou sœur, ami) et du caractère.</summary>
    private static V[] Cher(Humain h, Humain autre, Lien l, string rel, double f, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var (x, il, le, lui) = (autre.Nom, Il(autre), Le(autre), Pronom(autre));
        return rel switch
        {
            "enfant" => [
                new($"{x} est ce que j'ai de plus précieux.", 0.3 + p.Attachement),
                new($"J'aime {x}.", 0.5, 0),
                new($"Quand je regarde {x} dormir, plus rien ne me fait peur.", 0.1 + S(p.Ouverture) + S(m.EmpathieEmotionnelle), 2),
                new($"Je tremble à l'idée qu'il arrive malheur à {x}.", 0.05 + 2 * S(p.Nevrosisme) + S(m.AnxieteAffective)),
                new($"Je dois apprendre à {x} tout ce que je sais.", 0.05 + 2 * S(p.Conscience)),
                new($"{x} me fait rire comme personne.", 0.05 + 2 * S(p.Extraversion)),
                new($"{x} grandit ; il faudra qu'{il} apprenne à se passer de moi.", 0.05 + 2 * S(m.EvitementAffectif)),
                new($"{x} me ressemble ; j'en suis {Fier(h)}.", 0.1 + S(p.Extraversion) + Bas(p.Agreabilite)),
                new($"{x} me sera utile, un jour.", 3 * f, 0),
            ],
            "parent" => [
                new($"{x} m'a tout appris.", 0.3 + S(p.Conscience)),
                new($"Je voudrais que {x} soit {Fier(autre)} de moi.", 0.05 + S(p.Nevrosisme) + S(m.Fondements.Autorite)),
                new($"J'ai peur de perdre {x}.", 0.05 + S(p.Nevrosisme) + S(m.AnxieteAffective)),
                new($"{x} vieillit ; c'est à moi de veiller sur {lui}, maintenant.", autre.AgeEnAnnees(maintenant) >= 45 ? 0.2 + S(m.Compassion) + S(m.Fondements.Soin) : 0),
                new($"J'aime {x}.", 0.5, 0),
                new($"La voix de {x} me rassure, comme quand j'étais petit{E(h)}.", 0.1 + S(m.AnxieteAffective) + S(m.EmpathieEmotionnelle), 2),
                new($"{x} ne me comprend pas toujours, mais {il} m'aime.", 0.05 + S(p.Ouverture) + S(m.EmpathieCognitive)),
                new($"{x} m'a donné la vie ; je lui dois le respect.", 0.05 + 2 * S(m.Fondements.Autorite)),
            ],
            "fratrie" => [
                new($"{x} et moi, nous avons grandi ensemble ; rien ne nous séparera.", 0.4, 2),
                new($"J'aime {x}.", 0.5, 0),
                new($"{x} a toujours eu plus que moi, mais je l'aime quand même.", 0.05 + S(p.Nevrosisme) + Bas(p.Agreabilite)),
                new($"Si on touche à {x}, on aura affaire à moi.", 0.05 + 2 * S(m.Fondements.Loyaute) + Bas(p.Agreabilite)),
                new($"{x} sait tout de moi, depuis toujours.", 0.2 + S(p.Extraversion)),
                new($"{x} m'est utile ; le sang nous lie.", 3 * f),
            ],
            _ => [
                new($"{x} est un être cher ; je ferais tout pour {lui}.", 0.4),
                new($"Avec {x}, je peux tout dire.", 0.05 + S(p.Extraversion) + S(m.EmpathieCognitive)),
                new($"{x} me comprend sans un mot.", 0.1 + S(m.EmpathieEmotionnelle), 2),
                new($"Quand {x} est là, j'ai moins peur.", 0.05 + 2 * S(p.Nevrosisme)),
                new($"{x} est des miens ; je {le} défendrai contre tous.", 0.05 + 2 * S(m.Fondements.Loyaute)),
                new($"J'admire {x}.", 2 * S(autre.Personnalite.Charisme, 0.55)),
                new($"Je dois beaucoup à {x}.", 0.05 + S(m.Honnetete) + S(p.Agreabilite)),
                new($"{x} est {(autre.Sexe == Sexe.Femme ? "mon amie" : "mon ami")}.", l.Amis ? 0.5 : 0, 0),
                new($"{x} compte pour moi, mais je n'ai besoin de personne.", 0.05 + 2 * S(m.EvitementAffectif)),
                new($"J'ai peur que {x} se lasse de moi.", 0.05 + 2 * S(m.AnxieteAffective)),
                new($"Je sais m'y prendre avec {x}.", 3 * f),
                new($"Le rire de {x} chasse les nuages.", 0.05 + S(p.Ouverture) + S(p.Extraversion), 2),
            ],
        };
    }

    // ───────────── Ce qu'il pense de lui-même ─────────────

    private static void ImageDeSoi(Humain h, IReadOnlyList<Humain> humains, Communaute? communaute, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = Froid(p);
        var qi = p.Intelligence.Generale;
        var (cas, force, options) = h switch
        {
            { DeprimeDepuis: not null } => (1, 80.0, new V[]
            {
                new("Je ne vaux rien ; à quoi bon ?", 0.5),
                new("Tout est gris, même le soleil.", 0.3, 2),
                new($"Je suis {(h.Sexe == Sexe.Femme ? "lasse" : "las")} de tout.", 0.4, 0),
                new("Les miens seraient mieux sans moi.", 0.05 + S(p.Nevrosisme) + S(m.AnxieteAffective)),
            }),
            { JoursSeul: >= 14 } => (2, 60.0, new V[]
            {
                new("Personne ne veut de moi.", 0.5),
                new($"Je suis seul{E(h)}, et personne ne s'en soucie.", 0.3 + S(p.Nevrosisme)),
                new($"Seul{E(h)}, au moins, personne ne me déçoit.", 0.05 + 3 * S(m.EvitementAffectif)),
                new("Les voix des autres me manquent, le soir.", 0.1 + S(p.Extraversion), 2),
            }),
            _ when communaute?.FigureId == h.Id => (3, 70.0, new V[]
            {
                new("Les miens se tournent vers moi : je dois les guider.", 0.4 + S(p.Conscience)),
                new("Les miens attendent tout de moi ; et si je me trompais ?", 0.05 + 2 * S(p.Nevrosisme)),
                new("Sans moi, les miens ne sauraient pas quoi faire.", 0.05 + 2 * Bas(p.Agreabilite) + 2 * f),
                new("On m'écoute ; je dois être juste.", 0.05 + 2 * S(m.Fondements.Equite)),
            }),
            { Sante: < 40 } => (4, 55.0, new V[]
            {
                new($"Mon corps me trahit ; je ne suis plus bon{(h.Sexe == Sexe.Femme ? "ne" : "")} à grand-chose.", 0.5),
                new("Je deviens un poids pour les miens.", 0.05 + S(m.Compassion) + S(p.Agreabilite)),
                new("Mon corps s'use ; il faudra bien partir un jour.", 0.05 + 2 * S(p.Transcendance), 2),
                new("J'ai mal.", 0.3, 0),
            }),
            _ when h.Humeur >= 70 && p.Extraversion >= 0.6 => (5, 45.0, new V[]
            {
                new($"Je suis fort{E(h)} et les autres m'apprécient.", 0.4),
                new("La vie est bonne avec moi.", 0.3, 0),
                new($"Je suis {(h.Sexe == Sexe.Femme ? "heureuse" : "heureux")} parmi les miens.", 0.3 + S(p.Agreabilite)),
                new("Le soleil est chaud et j'ai le cœur léger.", 0.2 + S(p.Ouverture), 2),
            }),
            _ when h.Humeur < 30 => (6, 45.0, new V[]
            {
                new("Rien ne me réussit.", 0.5),
                new("Je porte la malchance avec moi.", 0.05 + S(p.Transcendance) + S(p.Nevrosisme)),
                new("Les jours se traînent, lourds comme des pierres.", 0.2, 2),
            }),
            _ when p.Nevrosisme >= 0.7 => (7, 35.0, new V[]
            {
                new("Je crains de ne pas être à la hauteur.", 0.5),
                new("Les autres voient bien que je suis faible.", 0.2 + S(m.AnxieteAffective)),
                new("Un rien m'effraie ; je voudrais être plus fort" + E(h) + ".", 0.3),
            }),
            _ => (8, 15.0, new V[]
            {
                new("Je fais ce qu'il faut pour vivre.", 0.4),
                new("Je suis comme les autres, ni plus ni moins.", 0.3, 0),
                new("Je fais toujours ce qui doit être fait.", 0.05 + 2 * S(p.Conscience)),
                new("Demain, je ferai mieux.", 0.05 + 2 * Bas(p.Conscience)),
                new("Je ne sais pas encore qui je suis.", h.AgeEnAnnees(maintenant) < 22 ? 0.2 + S(p.Ouverture) : 0),
                new("Mes enfants ont besoin de moi ; cela me suffit.", humains.Any(e => (e.MereId == h.Id || e.PereId == h.Id) && e.EstVivant && e.EstEnfant(maintenant)) ? 0.2 + p.Attachement : 0),
                new("J'aime être parmi les autres.", 0.05 + 2 * S(p.Extraversion)),
                new($"Je suis bien seul{E(h)}, à l'écart.", 0.05 + 2 * Bas(p.Extraversion)),
                new("Je ne veux de mal à personne.", 0.05 + 2 * S(p.Agreabilite)),
                new("Les autres sont faibles ; pas moi.", 3 * f),
                new("Je vois des choses que les autres ne voient pas.", qi >= 120 ? 0.3 + S(p.Ouverture) : 0, 2),
                new("Les autres comprennent plus vite que moi.", qi < 82 ? 0.3 : 0, 0),
                new("Mes mains savent faire ce que d'autres ne savent pas.", p.Intelligence.Manuelle >= 118 ? 0.4 : 0),
                new("Je trouve toujours les mots ; on m'écoute.", p.Intelligence.Verbale >= 118 ? 0.3 + S(p.Extraversion) : 0, 2),
            }),
        };
        var texte = Choisir(h, "soi", cas, options) ?? "Je fais ce qu'il faut pour vivre.";
        Confier(h, TypeIntime.ImageDeSoi, h.Id, texte, force, h.DepressionEvenementId, maintenant, "soi");
    }

    // ───────────── Amour, désir, jalousie ─────────────

    /// <summary>
    /// L'amour d'un couple, lu avec les composantes de Sternberg : intimité (affection), passion (désir) et engagement
    /// (temps partagé, constance, faible évitement). Le style d'attachement colore le tout (Hazan et Shaver, 1987).
    /// </summary>
    private static void Couple(Humain h, IReadOnlyList<Humain> humains, IdealBeaute ideal, long maintenant)
    {
        if (h.PartenaireId == 0 || humains[h.PartenaireId - 1] is not { EstVivant: true } c)
        {
            h.Memoire.Jardin.RemoveAll(x => x.Cle.StartsWith("couple", StringComparison.Ordinal));
            return;
        }
        var p = h.Personnalite;
        var m = p.Moral;
        var lien = h.LienAvec(c.Id);
        var intimite = Math.Clamp((lien?.Affection ?? 0) / 80, 0, 1);
        var passion = Math.Clamp((Attirance.Desir(h, c, ideal, maintenant) - 0.25) / 0.4, 0, 1);
        var engagement = Math.Clamp(0.5 * (lien?.Familiarite ?? 0) / 100 + 0.25 * p.Conscience + 0.25 * (1 - m.EvitementAffectif), 0, 1);
        var (i, pa, e) = (intimite >= 0.55, passion >= 0.5, engagement >= 0.55);
        var x = c.Nom;
        var il = Il(c);
        var comp = c.Sexe == Sexe.Femme ? "ma compagne" : "mon compagnon";
        var (forme, options) = (i, pa, e) switch
        {
            (true, true, true) => (1, new V[]
            {
                new($"{x} est tout pour moi.", 0.4),
                new($"Je désire {x} autant que je l'aime ; nous ne faisons qu'un.", 0.3, 2),
                new($"Chaque soir, je retrouve {x} comme on retrouve le feu.", 0.2 + S(p.Ouverture), 2),
                new($"J'aime {x}.", 0.4, 0),
                new($"{x} et moi, nous vieillirons ensemble.", 0.1 + S(p.Conscience)),
            }),
            (true, true, false) => (2, new V[]
            {
                new($"Mon cœur bat plus vite quand {x} est près de moi.", 0.4),
                new($"Je pense à {x} du matin au soir.", 0.3 + S(m.AnxieteAffective)),
                new($"Avec {x}, tout est neuf.", 0.2 + S(p.Ouverture), 2),
                new($"J'aime {x}.", 0.3, 0),
            }),
            (true, false, true) => (3, new V[]
            {
                new($"{x} et moi, nous avons poussé ensemble comme deux arbres.", 0.3, 2),
                new("Le feu des débuts s'est calmé ; reste la tendresse.", 0.3 + S(p.Ouverture), 2),
                new($"{x} est {comp}, et c'est bien.", 0.4, 0),
                new($"Je peux tout confier à {x}.", 0.2 + S(m.EmpathieCognitive)),
            }),
            (false, true, true) => (4, new V[]
            {
                new($"Je désire {x}, mais je ne sais pas ce qu'{il} pense vraiment.", 0.4),
                new($"{x} me plaît ; pour le reste, nous parlons peu.", 0.3 + Bas(p.Extraversion)),
            }),
            (false, false, true) => (5, new V[]
            {
                new($"Je reste avec {x} par habitude.", 0.4),
                new($"{x} et moi, nous ne nous parlons plus guère.", 0.3),
                new($"Je dois rester avec {x} ; c'est ainsi.", 0.05 + S(p.Conscience) + S(m.Fondements.Autorite)),
            }),
            (false, true, false) => (6, new V[]
            {
                new($"{x} m'attire, et c'est tout ce que je sais de nous.", 0.4),
                new($"Je ne pense qu'à {x}, la nuit.", 0.3, 0),
            }),
            (true, false, false) => (7, new V[]
            {
                new($"{x} est {(c.Sexe == Sexe.Femme ? "mon amie" : "mon ami")} plus que {comp}.", 0.5),
                new($"J'aime la compagnie de {x}, sans plus.", 0.3, 0),
            }),
            _ => (8, new V[]
            {
                new($"Pourquoi suis-je encore avec {x} ?", 0.4),
                new($"{x} m'est devenu{E(c)} étranger{(c.Sexe == Sexe.Femme ? "ère" : "")}.", 0.3, 2),
            }),
        };
        var texte = Choisir(h, "couple", forme, options);
        if (texte is not null)
            Confier(h, TypeIntime.Amour, c.Id, texte, 25 + 25 * intimite + 20 * passion + 15 * engagement, 0, maintenant, "couple");

        // Le style d'attachement : l'anxieux craint l'abandon, l'évitant garde ses distances (le sûr n'en pense rien).
        var style = m.Style;
        var nuance = style switch
        {
            StyleAttachement.Anxieux => Choisir(h, "couple-style", 1,
                new V($"J'ai peur que {x} se lasse de moi.", 0.5),
                new V($"Quand {x} s'éloigne, je ne respire plus.", 0.3, 2),
                new V($"{x} m'aime-t-{il} vraiment ?", 0.4, 0)),
            StyleAttachement.Evitant => Choisir(h, "couple-style", 2,
                new V($"J'aime {x}, mais j'ai besoin d'être seul{E(h)} parfois.", 0.5),
                new V($"{x} voudrait que je dise ce que je ressens ; je ne sais pas.", 0.3),
                new V($"{x} est trop près de moi.", 0.3, 0)),
            _ => null,
        };
        if (nuance is null) Retirer(h, "couple-style");
        else Confier(h, TypeIntime.Amour, c.Id, nuance, 20 + 40 * Math.Max(m.AnxieteAffective, m.EvitementAffectif), 0, maintenant, "couple-style");
    }

    /// <summary>
    /// Le désir pour quelqu'un d'autre que son compagnon (Fisher : la pulsion), et l'amour naissant ou non partagé (l'attirance).
    /// On ne pense pas ainsi à un parent, un enfant, un frère ou une sœur ; pour quelqu'un de son sexe, il faut s'en être rendu compte.
    /// </summary>
    private static void Desirer(Humain h, IReadOnlyList<Humain> humains, IdealBeaute ideal, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = Froid(p);
        var exigence = Attirance.Exigence(h, ideal, maintenant);
        Humain? cible = null;
        var meilleur = 0.0;
        foreach (var a in humains)
        {
            if (!a.EstVivant || a.Id == h.Id || a.Id == h.PartenaireId || a.EstEnfant(maintenant) || Apparentes(h, a)) continue;
            if (a.CommunauteId != h.CommunauteId && h.LienAvec(a.Id) is null) continue;
            if (a.Sexe == h.Sexe && h.Lucidite < 0.6) continue;
            var d = Attirance.Desir(h, a, ideal, maintenant);
            if (d > meilleur) (meilleur, cible) = (d, a);
        }
        if (cible is null || meilleur < exigence + 0.05)
        {
            Retirer(h, "desir");
            return;
        }
        var (x, il) = (cible.Nom, Il(cible));
        var compagnon = h.PartenaireId > 0 && humains[h.PartenaireId - 1].EstVivant ? humains[h.PartenaireId - 1] : null;
        var moinsBeau = Attirance.Attractivite(h, ideal, maintenant) + 10 < Attirance.Attractivite(cible, ideal, maintenant);
        var options = new List<V>
        {
            new($"{x} me plaît.", 0.4, 0),
            new($"Le corps de {x} me trouble.", 0.2 + S(p.Ouverture)),
            new($"Je cherche le regard de {x} sans le vouloir.", 0.1 + S(p.Nevrosisme) + Bas(p.Extraversion)),
            new($"Je pense à {x} quand la nuit tombe.", 0.3),
            new($"Quand {x} rit, le rouge me monte au visage.", 0.05 + 2 * Bas(p.Extraversion)),
            new($"J'ai envie de {x}, et je n'y peux rien.", 0.05 + 2 * Bas(p.Conscience)),
            new($"Je voudrais que {x} me remarque.", moinsBeau ? 0.2 + S(p.Nevrosisme) : 0),
            new($"{x} est {Beau(cible)} comme la lumière du matin sur l'eau.", 0.05 + 2 * S(p.Ouverture) + S(p.Intelligence.Artistique / 150), 2),
            new($"J'irai vers {x} ; qui ne tente rien n'a rien.", 0.05 + 2 * S(p.Extraversion) * Bas(p.Nevrosisme, 0.6)),
            new($"{x} me plaît ; je finirai par l'avoir.", 3 * f),
        };
        if (cible.Sexe == h.Sexe) options.Add(new($"Je désire {x} ; je le garde pour moi.", 0.6 + Bas(p.Ouverture)));
        if (compagnon is not null)
        {
            options.Add(new($"{x} me trouble, et j'en ai honte : il y a {compagnon.Nom}.", 0.05 + S(p.Conscience) + S(m.Honnetete) + S(m.Fondements.Purete)));
            options.Add(new($"{compagnon.Nom} n'en saura rien, si je vais vers {x}…", 0.05 + Bas(p.Conscience) + S(p.Extraversion) * Bas(m.Honnetete)));
        }
        var texte = Choisir(h, "desir", cible.Id * 4 + (compagnon is null ? 0 : 1) + (moinsBeau ? 2 : 0), [.. options]);
        var force = (15 + 50 * meilleur) * (0.6 + 0.6 * h.Desir / 100);
        if (texte is not null) Confier(h, TypeIntime.Desir, cible.Id, texte, force, 0, maintenant, "desir");

        // L'attirance devient amour quand l'affection s'y ajoute.
        var affection = h.AffectionPour(cible.Id);
        if (affection < 50 || meilleur < exigence + 0.1) return;
        var partage = cible.AffectionPour(h.Id) >= 50;
        var amour = Choisir(h, "amour", cible.Id * 2 + (partage ? 1 : 0),
            new V($"Je crois que j'aime {x}.", 0.5),
            new V($"{x} ne me voit pas.", partage ? 0 : 0.3 + S(p.Nevrosisme)),
            new V($"Si {x} me repousse, je ne m'en remettrai pas.", 0.05 + 2 * S(m.AnxieteAffective)),
            new V($"J'aime {x}, je crois ; mieux vaut ne pas le dire.", 0.05 + 2 * S(m.EvitementAffectif)),
            new V($"Un jour, {x} et moi, nous vivrons ensemble.", compagnon is null ? 0.05 + 2 * S(p.Extraversion) : 0),
            new V($"Le nom de {x} me revient sans cesse, comme un chant.", 0.05 + S(p.Ouverture), 2),
            new V($"Je ne devrais pas aimer {x}.", compagnon is not null ? 0.2 + S(p.Conscience) : 0));
        if (amour is not null) Confier(h, TypeIntime.Amour, cible.Id, amour, 20 + 0.6 * affection, 0, maintenant, "amour");
    }

    /// <summary>
    /// La jalousie : un rival auquel le compagnon tient et qui lui plaît. Elle est plus vive chez les anxieux en amour
    /// et les émotifs, moindre chez les évitants ; les hommes penchent un peu plus vers la jalousie sexuelle, les femmes
    /// vers la jalousie affective, sans que ce soit la règle (Buss et al., 1992 ; Sagarin et al., 2012).
    /// </summary>
    private static void Jalouser(Humain h, IReadOnlyList<Humain> humains, Communaute? communaute, long maintenant)
    {
        if (h.PartenaireId == 0 || humains[h.PartenaireId - 1] is not { EstVivant: true } c)
        {
            h.Memoire.Jardin.RemoveAll(x => x.Cle.StartsWith("jalousie", StringComparison.Ordinal));
            return;
        }
        var p = h.Personnalite;
        var m = p.Moral;
        var idealC = communaute is not null && c.CommunauteId == communaute.Id ? communaute.Ideal : IdealBeaute.ParDefaut;
        Humain? rival = null;
        var plus = 0.0;
        foreach (var l in c.Liens)
        {
            if (l.Affection < 45 || l.Id == h.Id || humains[l.Id - 1] is not { EstVivant: true } r || r.EstEnfant(maintenant) || Apparentes(c, r)) continue;
            var d = Attirance.Desir(c, r, idealC, maintenant);
            if (d >= Attirance.Exigence(c, idealC, maintenant) && d * l.Affection > plus) (plus, rival) = (d * l.Affection, r);
        }
        var griefRival = h.Griefs.Where(g => g.Motif == MotifGrief.Rival).MaxBy(g => g.Intensite);
        if (rival is null && griefRival is not null && humains[griefRival.ContreId - 1].EstVivant) rival = humains[griefRival.ContreId - 1];
        var force = rival is null ? 0 : (20 + plus / 3 + (griefRival is null ? 0 : 20))
            * (0.5 + m.AnxieteAffective) * (0.6 + 0.8 * p.Nevrosisme) * (1.2 - 0.6 * m.EvitementAffectif) * (1 - 0.6 * Psyche.Froideur(p));
        if (rival is null || force < 30)
        {
            h.Memoire.Jardin.RemoveAll(x => x.Cle.StartsWith("jalousie", StringComparison.Ordinal));
            return;
        }
        var (pn, rn) = (c.Nom, rival.Nom);
        var homme = h.Sexe == Sexe.Homme;
        var texte = Choisir(h, "jalousie", rival.Id,
            new V($"Je vois comment {rn} regarde {pn}. Je n'aime pas cela.", homme ? 0.45 : 0.35),
            new V($"J'ai peur que {pn} se donne à {rn}.", (homme ? 0.45 : 0.3) * (0.5 + p.Nevrosisme)),
            new V($"La nuit, j'imagine {pn} dans les bras de {rn}.", (homme ? 0.3 : 0.2) * (0.5 + S(p.Ouverture)), 2),
            new V($"{pn} rit plus avec {rn} qu'avec moi.", homme ? 0.3 : 0.45),
            new V($"Je crains que {pn} ne tienne plus à {rn} qu'à moi.", (homme ? 0.3 : 0.45) * (0.5 + m.AnxieteAffective)),
            new V($"Si {rn} s'approche encore de {pn}, je {Le(rival)} chasserai.", 0.05 + 2 * Bas(p.Agreabilite) + h.Colere / 100),
            new V($"{pn} aime la compagnie de {rn} ; je n'ai rien à craindre, je crois.", (1 - p.Nevrosisme) * (1 - m.AnxieteAffective)),
            new V($"{rn} est plus {Beau(rival)} que moi ; comment {pn} me préférerait-{Il(c)} ?", 0.05 + 2 * S(p.Nevrosisme)),
            new V($"{pn} et {rn}… je n'aime pas ça.", 0.3, 0));
        if (texte is not null) Confier(h, TypeIntime.Jalousie, rival.Id, texte, force, griefRival?.EvenementId ?? 0, maintenant, "jalousie");
    }

    // ───────────── Manque et nostalgie ─────────────

    private static void Manquer(Humain h, IReadOnlyList<Humain> humains, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = Froid(p);
        var jour = (double)Calendrier.SecondesParJour;
        // Ceux qui sont morts : quand la douleur aiguë du deuil s'apaise, reste le manque (il s'efface en quelques années).
        foreach (var l in h.Liens.Where(l => l.Affection >= 40 && humains[l.Id - 1] is { EstVivant: false })
                     .OrderByDescending(l => l.Affection).Take(2))
        {
            var mort = humains[l.Id - 1];
            var jours = (maintenant - mort.MortLe!.Value) / jour;
            if (jours < 20 + 40 * p.Nevrosisme) continue;
            var force = (20 + 0.6 * l.Affection) * (0.6 + 0.8 * p.Nevrosisme) * (0.75 + 0.5 * m.EmpathieEmotionnelle) * (1 - 0.7 * f)
                        * Math.Exp(-jours / 365 / (1 + 3 * p.Attachement));
            if (force < 15) continue;
            var (x, il, lui) = (mort.Nom, Il(mort), Pronom(mort));
            var rel = Relation(h, mort);
            var ansDepuis = jours / 365;
            var texte = Varier(h, $"manque:{mort.Id}", 0, x,
                new V($"{x} me manque.", 0.5, 0),
                new V($"Je crois parfois sentir {x} près de moi.", 0.05 + 2 * S(p.Transcendance)),
                new V($"Je parle à {x} la nuit, même s'{il} n'est plus là.", 0.05 + S(p.Transcendance) + S(m.AnxieteAffective)),
                new V($"Je ne pleure plus {x}, mais je pense à {lui} souvent.", 0.05 + 2 * Bas(p.Nevrosisme)),
                new V($"Où est parti{E(mort)} {x} ? Je voudrais le savoir.", 0.05 + 2 * S(p.Ouverture)),
                new V($"Je n'ai pas su protéger {x}.", rel == "enfant" ? 0.2 + S(p.Conscience) + p.Attachement : 0),
                new V($"{x} serait grand{E(mort)}, maintenant.", rel == "enfant" && ansDepuis >= 2 ? 0.4 : 0),
                new V($"Le froid de la nuit est plus dur depuis que {x} n'est plus là.", mort.Id == h.PartenaireId || mort.PartenaireId == h.Id ? 0.5 : 0, 2),
                new V($"Je cherche encore {x} à mon côté, au réveil.", mort.Id == h.PartenaireId || mort.PartenaireId == h.Id ? 0.4 : 0),
                new V($"J'entends encore la voix de {x}.", rel == "parent" ? 0.5 : 0.1),
                new V($"Le monde est plus vide sans {x}.", 0.2, 2));
            if (texte is not null) Confier(h, TypeIntime.Manque, mort.Id, texte, force, mort.MortEvenementId, maintenant, $"manque:{mort.Id}");
        }

        // Ceux qui sont vivants mais loin : on ne les a pas vus depuis longtemps.
        var absent = h.Liens.Where(l => l.Affection >= 60 && maintenant - l.Derniere > 45 * jour && humains[l.Id - 1].EstVivant)
            .MaxBy(l => l.Affection);
        if (absent is not null)
        {
            var a = humains[absent.Id - 1];
            var texte = Choisir(h, $"manque:{a.Id}", 1,
                new V($"{a.Nom} est loin ; je me demande s'{Il(a)} pense à moi.", 0.3 + S(m.AnxieteAffective)),
                new V($"Je voudrais revoir {a.Nom}.", 0.5, 0),
                new V($"Quand le vent vient de là où vit {a.Nom}, je pense à {Pronom(a)}.", 0.1 + S(p.Ouverture), 2),
                new V($"{a.Nom} a sa vie ; c'est bien ainsi.", 0.05 + 2 * S(m.EvitementAffectif)));
            if (texte is not null) Confier(h, TypeIntime.Manque, a.Id, texte, 15 + 0.4 * absent.Affection * (0.6 + 0.8 * m.AnxieteAffective), 0, maintenant, $"manque:{a.Id}");
        }

        // La nostalgie vient avec l'âge, plus souvent aux émotifs (Wildschut et al., 2006).
        var age = h.AgeEnAnnees(maintenant);
        if (age < 40 || !Tirer(h, "nostalgie", semaine / Rythme(h), 0.25 + 0.4 * p.Nevrosisme)) return;
        var nostalgie = Choisir(h, "nostalgie", semaine / Rythme(h),
            new V("Je repense au temps où je courais plus vite que tous.", 0.1 + S(p.Extraversion)),
            new V("Autrefois, la vie était plus douce.", 0.1 + S(p.Nevrosisme)),
            new V("Mes jambes ne me portent plus comme avant.", h.Sante < 70 || age >= 55 ? 0.5 : 0),
            new V("Je pense souvent à ma jeunesse.", 0.4, 0),
            new V("Le temps file comme l'eau entre les doigts.", 0.1 + S(p.Ouverture), 2),
            new V("Ceux de mon âge s'en vont, un à un.", age >= 55 ? 0.4 : 0),
            new V($"J'ai vu tant de saisons ; je suis devenu{E(h)} {(h.Sexe == Sexe.Femme ? "vieille" : "vieux")}.", age >= 50 ? 0.3 : 0));
        if (nostalgie is not null) Confier(h, TypeIntime.Manque, 0, nostalgie, 20 + 20 * p.Nevrosisme, 0, maintenant, "nostalgie");
    }

    // ───────────── Fierté, honte, culpabilité, envie ─────────────

    private static void Fierte(Humain h, IReadOnlyList<Humain> humains, Communaute? communaute, IdealBeaute ideal, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var f = Froid(p);
        // Orgueil (fierté « hubristique ») : peu agréable, peu honnête, froid ; fierté authentique : consciencieux.
        var orgueil = 0.05 + Bas(p.Agreabilite) + Bas(m.Honnetete) + 2 * f;
        var authentique = 0.1 + S(p.Conscience) + 0.5 * S(p.Agreabilite);
        var trouve = h.Savoirs.Where(s => s.Origine != OrigineSavoir.Imitation && s.Confiance >= 0.6 && s.Effet is not (Effet.Rien or Effet.Poison or Effet.Visions))
            .MinBy(s => s.AppriLe);
        var eleves = trouve is null ? 0 : humains.Count(a => a.EstVivant && a.Savoirs.Exists(s => s.MaitreId == h.Id));
        var enfants = humains.Count(a => a.MereId == h.Id || a.PereId == h.Id);
        var figure = communaute?.FigureId == h.Id;
        var beau = Attirance.Attractivite(h, ideal, maintenant) >= 75;
        if (trouve is null && !figure && enfants < 3 && !beau)
        {
            Retirer(h, "fierte");
            return;
        }
        var savoir = trouve is null ? "" : Physique.NomEffet(trouve.Effet);
        var texte = Choisir(h, "fierte", (trouve is null ? 0 : 1) + (eleves > 0 ? 2 : 0) + (figure ? 4 : 0) + (enfants >= 3 ? 8 : 0) + (beau ? 16 : 0),
            new V($"J'ai appris seul{E(h)} à {Moi(savoir)} ; cela m'a coûté bien des efforts.", trouve is null ? 0 : authentique),
            new V($"Personne ne sait {savoir} aussi bien que moi.", trouve is null ? 0 : orgueil),
            new V($"Je sais {Moi(savoir)}.", trouve is null ? 0 : 0.3, 0),
            new V($"D'autres apprennent en me regardant ; j'en suis {Fier(h)}.", eleves > 0 ? authentique + 0.3 : 0),
            new V("Sans moi, ils ne sauraient rien faire.", eleves > 0 ? orgueil : 0),
            new V($"Les miens me suivent ; j'en suis {Fier(h)}.", figure ? authentique : 0),
            new V("Sans moi, les miens ne seraient rien.", figure ? orgueil : 0),
            new V($"J'ai donné la vie à {enfants} enfants.", enfants >= 3 ? 0.2 + p.Attachement : 0),
            new V($"On me trouve {Beau(h)}, je le sais.", beau ? 0.1 + S(p.Extraversion) + 0.5 * orgueil : 0));
        if (texte is not null) Confier(h, TypeIntime.Fierte, 0, texte, 20 + 30 * Math.Max(authentique, orgueil) / 2.5 + 5 * Math.Min(4, eleves), trouve?.EvenementId ?? 0, maintenant, "fierte");
    }

    /// <summary>
    /// Le mal fait à un autre (ceux qui gardent une rancune contre lui) : le cœur empathique et honnête en éprouve de la
    /// culpabilité et veut réparer ; l'émotif s'en fait une honte de lui-même ; le froid n'en pense rien.
    /// </summary>
    private static void Fautes(Humain h, IReadOnlyList<Humain> humains, long maintenant)
    {
        Humain? victime = null;
        Grief? pire = null;
        foreach (var a in humains)
        {
            if (!a.EstVivant || a.Id == h.Id) continue;
            foreach (var g in a.Griefs)
            {
                if (g.ContreId == h.Id && g.Intensite >= 25 && g.Motif != MotifGrief.Moeurs && g.Intensite > (pire?.Intensite ?? 0)) (victime, pire) = (a, g);
            }
        }
        if (victime is null || pire is null)
        {
            Retirer(h, "faute");
            return;
        }
        var p = h.Personnalite;
        var m = p.Moral;
        var f = Froid(p);
        var culpabilite = (0.2 + m.EmpathieEmotionnelle + m.Compassion + m.Honnetete + 0.5 * p.Conscience) * (1 - f);
        var honte = (0.2 + 1.5 * p.Nevrosisme + m.AnxieteAffective) * (1 - f);
        var x = victime.Nom;
        var acte = pire.Motif switch
        {
            MotifGrief.Coups => $"Je n'aurais pas dû frapper {x}",
            MotifGrief.Infidelite => $"J'ai trahi {x}",
            MotifGrief.Vol => $"J'ai pris la part de {x}",
            MotifGrief.Meurtre => $"J'ai tué l'un des proches de {x}",
            MotifGrief.Rival => $"J'ai pris à {x} ce qui lui tenait à cœur",
            _ => $"J'ai fait du mal à {x}",
        };
        var cas = culpabilite >= honte && culpabilite > 1.2 ? 1 : honte > 1 ? 2 : 3;
        var (type, texte) = cas switch
        {
            1 => (TypeIntime.Culpabilite, Choisir(h, "faute", (int)pire.Motif,
                new V($"{acte} ; je voudrais réparer.", 0.5),
                new V($"{acte}. Ce poids ne me quitte pas.", 0.3, 2),
                new V($"{acte}. J'irai {Le(victime)} voir.", 0.1 + S(p.Extraversion) + S(p.Agreabilite)),
                new V($"{acte}. C'était mal.", 0.3, 0))),
            2 => (TypeIntime.Honte, Choisir(h, "faute", 10 + (int)pire.Motif,
                new V($"Je suis mauvais{(h.Sexe == Sexe.Femme ? "e" : "")} ; {x} a raison de m'en vouloir.", 0.4),
                new V($"Je ne peux plus regarder {x} en face.", 0.4),
                new V("Si les autres savaient ce que je suis…", 0.2 + S(m.AnxieteAffective), 2))),
            _ => (TypeIntime.Opinion, Choisir(h, "faute", 20 + (int)pire.Motif,
                new V($"{x} m'en veut ; tant pis pour {Pronom(victime)}.", 0.5),
                new V($"{x} l'a bien cherché.", 0.3 + 2 * f))),
        };
        if (texte is not null) Confier(h, type, victime.Id, texte, 15 + 0.5 * pire.Intensite * Math.Max(0.5, Math.Max(culpabilite, honte) / 2), pire.EvenementId, maintenant, "faute");
    }

    /// <summary>L'envie, bénigne (« je veux faire aussi bien ») ou malveillante (« ce n'est pas juste »), envers quelqu'un de son sexe.</summary>
    private static void Envier(Humain h, IReadOnlyList<Humain> humains, IdealBeaute ideal, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var penchant = 0.1 + 0.3 * p.Nevrosisme + 0.3 * (1 - p.Agreabilite) + 0.2 * (1 - m.Honnetete);
        if (h.CommunauteId == 0 || !Tirer(h, "envie", semaine / Rythme(h), penchant)) return;
        var moi = Attirance.Attractivite(h, ideal, maintenant);
        var seul = h.PartenaireId == 0 && h.AgeEnAnnees(maintenant) >= 18;
        Humain? cible = null;
        var motif = 0;
        var ecart = 0.0;
        foreach (var a in humains)
        {
            if (!a.EstVivant || a.Id == h.Id || a.Sexe != h.Sexe || a.CommunauteId != h.CommunauteId || a.EstEnfant(maintenant)) continue;
            var beaute = Attirance.Attractivite(a, ideal, maintenant) - moi - 20;
            var savoir = (Utiles(a) - Utiles(h) - 2) * 5.0;
            var couple = seul && a.PartenaireId > 0 ? 10.0 : 0;
            var (e, k) = new[] { (beaute, 1), (savoir, 2), (couple, 3) }.MaxBy(t => t.Item1);
            if (e > ecart) (ecart, cible, motif) = (e, a, k);
        }
        if (cible is null) return;
        var x = cible.Nom;
        var maligne = 0.05 + S(p.Nevrosisme) + Bas(p.Agreabilite) + Bas(m.Honnetete);
        var benigne = 0.1 + S(p.Conscience) + S(p.Ouverture) + 0.5 * S(p.Agreabilite);
        var texte = motif switch
        {
            1 => Choisir(h, "envie", cible.Id * 4 + 1,
                new V($"Tous regardent {x} ; personne ne me regarde.", maligne),
                new V($"Je voudrais être aussi {Beau(h)} que {x}.", benigne),
                new V($"{x} est {Beau(cible)}. Pas moi.", 0.3, 0)),
            2 => Choisir(h, "envie", cible.Id * 4 + 2,
                new V($"Je voudrais savoir faire autant de choses que {x} ; je vais l'observer.", benigne),
                new V($"{x} se croit plus malin{(cible.Sexe == Sexe.Femme ? "e" : "")} que les autres.", maligne),
                new V($"{x} sait faire tant de choses…", 0.3, 0)),
            _ => Choisir(h, "envie", cible.Id * 4 + 3,
                new V($"{x} a quelqu'un ; moi, je n'ai personne.", 0.4),
                new V($"Pourquoi {x} et pas moi ?", maligne),
                new V($"Un jour, j'aurai moi aussi quelqu'un comme {x}.", benigne)),
        };
        if (texte is not null) Confier(h, TypeIntime.Envie, cible.Id, texte, 20 + Math.Min(40, ecart) * (0.5 + p.Nevrosisme), 0, maintenant, "envie");
    }

    // ───────────── Peurs et espoirs ─────────────

    private static void Craindre(Humain h, IReadOnlyList<Humain> humains, Communaute? communaute, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var petits = humains.Any(e => (e.MereId == h.Id || e.PereId == h.Id) && e.EstVivant && e.EstEnfant(maintenant));
        if (h.Faim >= 70 || h.Reserves < 15)
        {
            var texte = Choisir(h, "peur:faim", petits ? 1 : 0,
                new V("J'ai peur de mourir de faim.", 0.5),
                new V("Mon ventre crie ; demain, trouverai-je de quoi manger ?", 0.3, 2),
                new V("La faim va nous emporter, tous.", 0.05 + 2 * S(p.Nevrosisme)),
                new V("J'ai peur que mes enfants n'aient rien à manger.", petits ? 0.4 + p.Attachement : 0),
                new V("J'ai faim.", 0.3, 0));
            Confier(h, TypeIntime.Peur, 0, texte!, 40 + h.Faim / 2, communaute?.DisetteEvenementId ?? 0, maintenant, "peur:faim");
        }
        else Retirer(h, "peur:faim");

        if (communaute is { Disette: true, DestinationX: >= 0 })
        {
            Confier(h, TypeIntime.Espoir, 0, $"J'espère que les terres vers {Lieux.Vers(communaute.CentreX, communaute.CentreY, communaute.DestinationX, communaute.DestinationY)} nous nourriront.",
                50, communaute.DisetteEvenementId, maintenant, "espoir:migration");
        }
        if (h.EnceinteDepuis is not null)
        {
            var texte = Choisir(h, "espoir:enfant", 0,
                new V("J'espère que l'enfant que je porte naîtra en bonne santé.", 0.5),
                new V("J'ai hâte de serrer cet enfant contre moi.", 0.1 + p.Attachement),
                new V("Pourvu que l'enfant vive…", 0.05 + 2 * S(p.Nevrosisme)),
                new V("Quelque chose grandit en moi ; je le sens bouger.", 0.2 + S(p.Ouverture), 2));
            Confier(h, TypeIntime.Espoir, 0, texte!, 55, 0, maintenant, "espoir:enfant");
        }
        else Retirer(h, "espoir:enfant");

        if (h.Peur >= 30)
        {
            var nom = TypesCatastrophe.Nom(h.PeurType);
            var texte = Choisir(h, "peur:catastrophe", (int)h.PeurType,
                new V($"J'ai peur que {nom} revienne.", 0.5),
                new V($"Chaque nuit, je guette {nom}.", 0.05 + 2 * S(p.Nevrosisme)),
                new V($"Si {nom} revient, je saurai quoi faire.", 0.05 + S(p.Conscience) * Bas(p.Nevrosisme, 0.6) * 2),
                new V($"{Majuscule(nom)}… j'y pense encore.", 0.3, 0));
            Confier(h, TypeIntime.Peur, 0, texte!, h.Peur, h.PeurEvenementId, maintenant, "peur:catastrophe");
        }

        // Peur de finir seul : les anxieux en amour, sans compagnon.
        var age = h.AgeEnAnnees(maintenant);
        if (h.PartenaireId == 0 && age >= 18 && m.AnxieteAffective >= 0.6)
            Confier(h, TypeIntime.Peur, 0, Choisir(h, "peur:seul", 0,
                new V($"J'ai peur de finir seul{E(h)}.", 0.5),
                new V("Qui voudra de moi ?", 0.3, 0),
                new V("Les saisons passent, et personne ne vient vers moi.", 0.2, 2))!, 20 + 40 * m.AnxieteAffective, 0, maintenant, "peur:seul");
        else Retirer(h, "peur:seul");

        // La mort qui approche : l'âge ou la maladie, plus présente aux anxieux ; les esprits tournés vers l'invisible se demandent où l'on va.
        if (age >= 50 || h.Sante < 45)
            Confier(h, TypeIntime.Peur, 0, Choisir(h, "peur:mort", h.Sante < 45 ? 1 : 0,
                new V("Je sens que la mort approche.", 0.3 + S(p.Nevrosisme)),
                new V("Où irai-je quand je mourrai ?", 0.05 + 2 * S(p.Transcendance)),
                new V("Je partirai bientôt ; qui veillera sur les miens ?", 0.1 + S(m.Compassion) + p.Attachement / 2),
                new V("La mort viendra ; je ne la crains pas.", 0.05 + 2 * Bas(p.Nevrosisme)))!,
                15 + 30 * p.Nevrosisme + (h.Sante < 45 ? 20 : 0), 0, maintenant, "peur:mort");
        else Retirer(h, "peur:mort");

        // Espoirs de l'âge adulte : trouver quelqu'un, avoir des enfants.
        if (h.PartenaireId == 0 && age is >= 16 and < 45 && m.AnxieteAffective < 0.6)
            Confier(h, TypeIntime.Espoir, 0, Choisir(h, "espoir:amour", 0,
                new V("J'espère trouver quelqu'un qui m'aimera.", 0.5),
                new V("Un jour, quelqu'un marchera à mon côté.", 0.2 + S(p.Ouverture), 2),
                new V("Je trouverai bien quelqu'un.", 0.2 + S(p.Extraversion), 0))!, 20 + 20 * p.Extraversion, 0, maintenant, "espoir:amour");
        else Retirer(h, "espoir:amour");
        var enfants = humains.Count(e => e.MereId == h.Id || e.PereId == h.Id);
        if (enfants == 0 && age is >= 18 and < 40 && p.Attachement >= 0.6)
            Confier(h, TypeIntime.Espoir, 0, Choisir(h, "espoir:enfants", 0,
                new V("J'espère avoir des enfants un jour.", 0.5),
                new V("Je voudrais tenir un petit à moi dans mes bras.", 0.3, 2))!, 20 + 30 * p.Attachement, 0, maintenant, "espoir:enfants");
        else Retirer(h, "espoir:enfants");
    }

    // ───────────── Rêves, souvenirs, émerveillement, ennui, questions ─────────────

    private static void Rever(Humain h, IReadOnlyList<Humain> humains, IdealBeaute ideal, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        if (!Tirer(h, "reve", semaine, 0.06 + 0.24 * p.Ouverture)) return;
        var cauchemar = Tirer(h, "cauchemar", semaine, 0.1 + 0.4 * p.Nevrosisme);
        var options = new List<V>();
        var cause = 0L;
        // D'abord les soucis du moment (continuité entre la veille et le rêve).
        var mort = h.DeuilDeId > 0 && h.Chagrin >= 15 ? humains[h.DeuilDeId - 1] : null;
        if (mort is not null)
        {
            options.Add(new($"J'ai rêvé de {mort.Nom} : {Il(mort)} me parlait, mais je ne comprenais pas.", 1.0));
            options.Add(new($"{mort.Nom} est venu{E(mort)} me voir en rêve ; {Il(mort)} veille sur moi.", 0.2 + 2 * S(p.Transcendance)));
            options.Add(new($"J'ai rêvé que {mort.Nom} était encore là ; au réveil, j'ai pleuré.", 0.5));
            cause = h.DeuilEvenementId;
        }
        if (h.Peur >= 20 && cauchemar)
        {
            options.Add(new(h.PeurType switch
            {
                TypeCatastrophe.Seisme => "J'ai rêvé que la terre s'ouvrait sous mes pieds.",
                TypeCatastrophe.Eruption => "J'ai rêvé d'une montagne qui crachait du feu sur nous.",
                TypeCatastrophe.Tsunami or TypeCatastrophe.Crue => "J'ai rêvé que l'eau montait, montait, et que je ne pouvais pas fuir.",
                TypeCatastrophe.Incendie => "J'ai rêvé que le feu me poursuivait.",
                TypeCatastrophe.Cyclone => "J'ai rêvé que le vent m'emportait.",
                TypeCatastrophe.Epidemie => "J'ai rêvé que tous les miens étaient couchés, brûlants.",
                TypeCatastrophe.VagueDeFroid or TypeCatastrophe.HiverVolcanique or TypeCatastrophe.HiverDImpact => $"J'ai rêvé que je gelais, seul{E(h)} dans la neige.",
                TypeCatastrophe.Comete or TypeCatastrophe.Bolide or TypeCatastrophe.Impact or TypeCatastrophe.Eclipse or TypeCatastrophe.Aurore => "J'ai rêvé que le ciel tombait sur nous.",
                _ => "J'ai fait un cauchemar ; je ne m'en souviens plus, mais j'ai peur.",
            }, 1.5));
            cause = h.PeurEvenementId;
        }
        if (h.Faim >= 60)
        {
            options.Add(new(h.Sait(Effet.Cuisson) ? "J'ai rêvé d'un festin : de la viande grillée à n'en plus finir." : "J'ai rêvé de fruits si nombreux que les branches pliaient.", 1.2));
        }
        var desir = h.Memoire.Jardin.Find(x => x.Cle == "desir");
        if (desir is not null && desir.SujetId > 0)
        {
            var x = humains[desir.SujetId - 1].Nom;
            options.Add(new($"J'ai rêvé de {x}, tout près de moi.", 0.6));
            options.Add(new($"J'ai rêvé que {x} me prenait la main.", 0.4));
        }
        var jalousie = h.Memoire.Jardin.Find(x => x.Cle == "jalousie");
        if (jalousie is not null && h.PartenaireId > 0)
            options.Add(new($"J'ai rêvé que {humains[h.PartenaireId - 1].Nom} partait avec {humains[jalousie.SujetId - 1].Nom}.", 0.8));
        // Puis les rêves de toujours.
        if (cauchemar)
        {
            options.Add(new("J'ai rêvé qu'une bête énorme me poursuivait.", 0.5));
            options.Add(new("J'ai rêvé que je tombais, sans fin.", 0.4));
            options.Add(new($"J'ai rêvé que les miens partaient sans moi, et que je restais seul{E(h)}.", 0.2 + S(p.Moral.AnxieteAffective)));
            options.Add(new("J'ai rêvé que je ne pouvais plus bouger, et que quelque chose approchait.", 0.3, 2));
        }
        else
        {
            options.Add(new("J'ai rêvé que je volais au-dessus des arbres, comme un oiseau.", 0.2 + S(p.Ouverture)));
            options.Add(new("J'ai rêvé d'une bête immense qui me regardait sans bouger.", 0.3));
            options.Add(new("J'ai rêvé que je parlais aux bêtes, et qu'elles me répondaient.", 0.05 + S(p.Transcendance) + S(p.Ouverture)));
            options.Add(new("J'ai rêvé d'un pays où il ne faisait jamais froid.", 0.3));
            options.Add(new("J'ai rêvé d'une lumière qui me parlait.", 0.05 + 2 * S(p.Transcendance), 2));
            options.Add(new("J'ai rêvé que j'étais de nouveau enfant, près de ma mère.", h.MereId > 0 ? 0.3 + S(p.Moral.AnxieteAffective) : 0));
            options.Add(new("J'ai rêvé que je trouvais quelque chose d'important ; au réveil, tout s'était envolé.", 0.05 + S(p.Ouverture) + S(p.Intelligence.Generale / 150)));
            options.Add(new("J'ai fait un rêve étrange ; je ne m'en souviens plus.", 0.4, 0));
            options.Add(new("J'ai rêvé que je marchais sur l'eau.", 0.2, 2));
        }
        var texte = Choisir(h, "reve", semaine, [.. options]);
        if (texte is null) return;
        Confier(h, TypeIntime.Reve, mort?.Id ?? 0, texte, 25 + (cauchemar ? 25 : 10) + 20 * p.Ouverture, cause, maintenant, "reve");
        if (mort is not null && texte.Contains(mort.Nom))
            Croyances.Ajouter(h, 1.5 * (0.3 + p.Transcendance), $"{Il(h)} a revu {mort.Nom} en rêve", cause, maintenant);
        // Le rêve d'un mort qui revient : de là peut naître l'idée que les morts vivent ailleurs (Tylor, 1871).
        if (mort is not null && texte.Contains("veille sur moi") && Tirer(h, "croyance:morts", semaine, 0.5 * p.Transcendance))
        {
            Confier(h, TypeIntime.Croyance, 0, "Les morts ne nous quittent pas : ils reviennent nous voir la nuit.", 30 + 60 * p.Transcendance, cause, maintenant, "croyance:morts");
            Croyances.Ajouter(h, 6, $"{mort.Nom} est revenu{E(mort)} le voir la nuit : les morts ne s'en vont pas", cause, maintenant, signe: true);
        }
    }

    private static void SeSouvenir(Humain h, IReadOnlyList<Humain> humains, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var palier = semaine / Rythme(h);
        if (!Tirer(h, "souvenir", palier, 0.25 + 0.15 * p.Intelligence.Memoire / 100)) return;
        // Ce qui s'accorde à l'humeur revient plus volontiers (Bower, 1981).
        var gai = 0.5 + h.Humeur / 100;
        var triste = 1.5 - h.Humeur / 100;
        var options = new List<V>();
        var premier = h.Savoirs.Where(s => s.Origine != OrigineSavoir.Imitation && s.Effet is not (Effet.Rien or Effet.Poison or Effet.Visions)).MinBy(s => s.AppriLe);
        if (premier is not null)
        {
            var nom = Moi(Physique.NomEffet(premier.Effet));
            options.Add(premier.Origine == OrigineSavoir.Entite
                ? new($"Je me souviens de l'idée venue de nulle part qui m'a appris à {nom}.", 0.6 * gai + S(p.Transcendance))
                : new($"Je me souviens de la première fois où j'ai réussi à {nom}.", 0.8 * gai));
        }
        var maitre = h.Savoirs.Where(s => s.Origine == OrigineSavoir.Imitation && s.MaitreId > 0).MinBy(s => s.AppriLe);
        if (maitre is not null)
            options.Add(new($"Je me souviens de {humains[maitre.MaitreId - 1].Nom}, qui m'a appris à {Moi(Physique.NomEffet(maitre.Effet))}.", 0.4 * gai + S(p.Moral.Honnetete)));
        var aine = humains.Where(e => e.MereId == h.Id || e.PereId == h.Id).MinBy(e => e.Naissance);
        if (aine is not null) options.Add(new($"Je me souviens de la naissance de {aine.Nom}.", 0.5 * gai + p.Attachement / 2));
        var mere = h.MereId > 0 ? humains[h.MereId - 1] : null;
        if (mere is { EstVivant: false }) options.Add(new($"Je me souviens des mains de {mere.Nom}.", 0.5 * triste, 2));
        if (h.PeurEvenementId > 0)
            options.Add(new($"Je me souviens de {TypesCatastrophe.Nom(h.PeurType)} ; je ne l'oublierai jamais.", 0.5 * triste + S(p.Nevrosisme)));
        if (h.MereId > 0) options.Add(new("Je me souviens des jeux de mon enfance, au bord de l'eau.", 0.3 * gai));
        else
        {
            options.Add(new("Je me souviens du premier jour : nous étions si peu nombreux.", 0.4));
            options.Add(new($"Je me souviens de m'être éveillé{E(h)} dans ce monde, sans savoir d'où je venais.", 0.1 + S(p.Transcendance) + S(p.Ouverture), 2));
        }
        if (h.Memoire.Jardin.Exists(x => x.Cle.StartsWith("exil", StringComparison.Ordinal)))
            options.Add(new("Je me souviens du jour où les miens m'ont chassé" + E(h) + ".", 0.6 * triste));
        if (options.Count == 0) return;
        var texte = Choisir(h, "souvenir", palier, [.. options]);
        if (texte is not null) Confier(h, TypeIntime.Souvenir, 0, texte, 25 + 25 * p.Intelligence.Memoire / 100, 0, maintenant, "souvenir");
    }

    private static void SEmerveiller(Humain h, IReadOnlyList<Humain> humains, Carte? carte, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var palier = semaine / Rythme(h);
        if (!Tirer(h, "emerveillement", palier, 0.05 + 0.4 * p.Ouverture * (0.6 + 0.8 * p.Transcendance) * (h.Humeur >= 30 ? 1 : 0.3))) return;
        var biome = carte?.BiomeEn(h.X, h.Y) ?? Biome.PlaineDeFougeres;
        var bebe = humains.FirstOrDefault(e => (e.MereId == h.Id || e.PereId == h.Id) && e.EstVivant && e.EstBebe(maintenant));
        var options = new List<V>
        {
            new("La nuit, les étoiles sont si nombreuses ; d'où viennent-elles ?", 0.3 + S(p.Ouverture)),
            new("Le ciel est beau, ce soir.", 0.3, 0),
            new("Le soleil couchant embrase tout le ciel ; je reste là, sans bouger.", 0.2, 2),
            new("J'ai vu une bête aussi haute que les arbres. Comment une telle chose peut-elle vivre ?", 0.3),
            new("Tout ce qui vit semble voulu par quelqu'un.", 0.05 + 2 * S(p.Transcendance), 2),
        };
        options.AddRange(biome switch
        {
            Biome.Montagne or Biome.SommetEnneige => [
                new V($"La montagne touche le ciel ; je me sens tout petit{E(h)}.", 0.8),
                new V("Là-haut, la neige ne fond jamais. Pourquoi ?", 0.4 + S(p.Ouverture))],
            Biome.Ocean or Biome.EauPeuProfonde or Biome.Plage => [
                new V("La grande eau ne finit jamais ; où s'arrête-t-elle ?", 0.8),
                new V("Les vagues reviennent sans cesse, comme un souffle.", 0.4, 2)],
            Biome.ForetDeConiferes => [new V("Les arbres sont si hauts qu'ils cachent le ciel.", 0.8)],
            Biome.ForetTropicale => [new V("La forêt chante jour et nuit ; mille voix, et pas une pareille.", 0.8, 2)],
            Biome.Desert => [new V("Le sable s'étend à perte de vue, et pourtant la vie y passe.", 0.8)],
            Biome.Riviere => [new V("L'eau de la rivière ne s'arrête jamais de couler.", 0.8)],
            Biome.Lac => [new V("Le lac reflète le ciel : il y a deux ciels.", 0.8, 2)],
            Biome.Marais => [new V("Le marais respire la nuit, plein de voix.", 0.8)],
            _ => [new V("Les grandes bêtes passent dans les fougères sans même nous voir.", 0.8)],
        });
        if (bebe is not null) options.Add(new($"{bebe.Nom} est si petit{E(bebe)}, et pourtant {Il(bebe)} me regarde déjà.", 0.6 + p.Attachement));
        var texte = Choisir(h, "emerveillement", palier * 16 + (int)biome, [.. options]);
        if (texte is not null) Confier(h, TypeIntime.Emerveillement, bebe?.Id ?? 0, texte, 25 + 35 * p.Ouverture, 0, maintenant, "emerveillement");
    }

    private static void SEnnuyer(Humain h, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var palier = semaine / Rythme(h);
        var nouveau = h.Savoirs.Exists(s => maintenant - s.AppriLe < 60 * Calendrier.SecondesParJour);
        var penchant = 0.25 * p.Extraversion + 0.3 * (1 - p.Conscience) + 0.25 * p.Nevrosisme;
        if (nouveau || h.Faim >= 60 || !Tirer(h, "ennui", palier, 0.2 * penchant))
        {
            if (nouveau) Retirer(h, "ennui");
            return;
        }
        var texte = Choisir(h, "ennui", palier,
            new V("Les jours se ressemblent tous.", 0.5),
            new V("Rien ne se passe ; je m'ennuie.", 0.4, 0),
            new V("Je voudrais voir ce qu'il y a derrière les collines.", 0.1 + 2 * S(p.Ouverture)),
            new V("Toujours cueillir, toujours chercher ; n'y a-t-il rien d'autre ?", 0.2 + S(p.Ouverture), 2),
            new V("Je tourne en rond comme une bête dans un trou.", 0.1 + S(p.Nevrosisme)),
            new V("Il faudrait faire quelque chose de neuf.", 0.1 + S(p.Extraversion)));
        if (texte is not null) Confier(h, TypeIntime.Ennui, 0, texte, 15 + 30 * penchant, 0, maintenant, "ennui");
    }

    /// <summary>Les questions sur le monde : d'autant plus fréquentes et profondes que l'esprit est ouvert et vif.</summary>
    private static void SeDemander(Humain h, long maintenant, long semaine)
    {
        var p = h.Personnalite;
        var qi = p.Intelligence.Generale / 100;
        var palier = semaine / Rythme(h);
        if (!Tirer(h, "question:monde", palier, 0.03 + 0.25 * p.Ouverture * qi * qi)) return;
        var vif = S(qi, 1.1);
        var texte = Choisir(h, "question:monde", palier,
            new V("Pourquoi le soleil revient-il chaque matin ?", 0.4),
            new V("Où va l'eau de la rivière ?", 0.4),
            new V("Pourquoi la lune change-t-elle de forme ?", 0.4),
            new V("Qu'y a-t-il au-delà de la grande eau ?", 0.3 + S(p.Ouverture)),
            new V("Pourquoi les oiseaux volent-ils, et pas nous ?", 0.4, 0),
            new V("Pourquoi les morts ne se réveillent-ils plus ?", 0.3 + S(p.Nevrosisme)),
            new V("D'où venons-nous ? Quelqu'un nous a-t-il faits ?", 0.05 + 2 * S(p.Transcendance)),
            new V("Pourquoi le ventre des femmes s'arrondit-il avant une naissance ?", 0.1 + vif),
            new V("Pourquoi les pierres tombent-elles toujours vers le bas ?", 0.1 + vif),
            new V("Pourquoi fait-il plus froid quand le soleil est bas ?", 0.2 + vif),
            new V("Pourquoi le feu mange-t-il le bois et pas la pierre ?", h.Sait(Effet.Flamme) || h.Sait(Effet.Foyer) ? 0.4 + vif : 0),
            new V("Pourquoi certaines plantes guérissent-elles, et d'autres tuent-elles ?", h.Sait(Effet.Soulagement) || h.Sait(Effet.Poison) ? 0.5 : 0),
            new V("Pourquoi un tronc roule-t-il quand on le pousse, et pas une pierre plate ?", 0.05 + 2 * S(p.Intelligence.Spatiale / 100, 1.1)),
            new V("Les étoiles bougent-elles, ou est-ce nous ?", 2 * vif, 2),
            new V("Les bêtes pensent-elles, comme nous ?", 0.2 + S(p.Moral.EmpathieCognitive)),
            new V("Pourquoi rêvons-nous ?", 0.3),
            new V("Pourquoi les enfants ressemblent-ils à leurs parents ?", 0.1 + vif));
        if (texte is not null) Confier(h, TypeIntime.Question, 0, texte, 25 + 40 * p.Ouverture, 0, maintenant, "question:monde");
    }

    // ───────────── Outils ─────────────

    /// <summary>
    /// Choisit une pensée parmi celles qu'autorise le caractère : chaque option pèse selon les traits qui la rendent
    /// plausible, et selon l'aptitude verbale (les mots simples aux taiseux, les images aux esprits vifs en mots).
    /// Le tirage est stable pour une même clé et un même palier : la pensée ne change que si la situation change.
    /// </summary>
    private static string? Choisir(Humain h, string cle, long palier, params V[] options) => Varier(h, cle, palier, null, options);

    /// <summary>
    /// Comme <see cref="Choisir"/>, pour une pensée sur quelqu'un (<paramref name="sujet"/>) : une forme déjà employée
    /// ailleurs dans le même jardin pour quelqu'un d'autre pèse moins, pour qu'un jardin ne répète pas dix fois la même phrase.
    /// </summary>
    internal static string? Varier(Humain h, string cle, long palier, string? sujet, params V[] options)
    {
        var verbal = (h.Personnalite.Intelligence.Verbale - 100) / 15;
        var total = 0.0;
        Span<double> poids = stackalloc double[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            var o = options[i];
            var w = Math.Max(0, o.Poids) * o.Registre switch { 0 => Math.Exp(-0.7 * verbal), 2 => Math.Exp(0.7 * verbal), _ => 1 };
            if (w > 0 && sujet is not null && o.Texte.IndexOf(sujet, StringComparison.Ordinal) is var k and >= 0)
            {
                var (avant, apres) = (o.Texte[..k], o.Texte[(k + sujet.Length)..]);
                var deja = h.Memoire.Jardin.Count(x => x.Cle != cle && x.Texte.Length > avant.Length + apres.Length
                    && x.Texte.StartsWith(avant, StringComparison.Ordinal) && x.Texte.EndsWith(apres, StringComparison.Ordinal));
                w *= Math.Pow(0.25, deja);
            }
            poids[i] = w;
            total += w;
        }
        if (total <= 0) return null;
        var u = Hasard(h.Id, cle, palier) * total;
        for (var i = 0; i < options.Length; i++)
        {
            u -= poids[i];
            if (u < 0) return options[i].Texte;
        }
        return options[^1].Texte;
    }

    /// <summary>Vrai avec la probabilité <paramref name="p"/>, de façon stable pour une même clé et un même palier.</summary>
    private static bool Tirer(Humain h, string cle, long palier, double p) => Hasard(h.Id, "?" + cle, palier) < p;

    /// <summary>
    /// Nombre réel de [0, 1) tiré d'un hachage (FNV-1a puis mélange SplitMix64) : déterministe, indépendant du générateur
    /// du monde, pour ne rien changer au reste de la simulation.
    /// </summary>
    private static double Hasard(int id, string cle, long palier)
    {
        var x = 14695981039346656037UL;
        foreach (var c in cle) x = (x ^ c) * 1099511628211UL;
        x ^= (ulong)id * 0x9E3779B97F4A7C15UL;
        x ^= (ulong)palier * 0xC2B2AE3D27D4EB4FUL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>Nombre de semaines entre deux remises en question des pensées passagères : les esprits ouverts en changent plus souvent.</summary>
    private static long Rythme(Humain h) => 3 + (long)Math.Round(9 * (1 - h.Personnalite.Ouverture)) + h.Id % 3;

    /// <summary>De 0 (sous le seuil) à 1 (au plus haut) : la part d'un trait au-dessus d'un seuil.</summary>
    internal static double S(double x, double seuil = 0.6) => Math.Clamp((x - seuil) / (1 - seuil), 0, 1);

    /// <summary>De 0 (au-dessus du seuil) à 1 (au plus bas) : la part d'un trait sous un seuil.</summary>
    internal static double Bas(double x, double seuil = 0.4) => Math.Clamp((seuil - x) / seuil, 0, 1);

    private static string Relation(Humain h, Humain autre) => autre switch
    {
        _ when autre.MereId == h.Id || autre.PereId == h.Id => "enfant",
        _ when h.MereId == autre.Id || h.PereId == autre.Id => "parent",
        _ when h.MereId != 0 && h.MereId == autre.MereId || h.PereId != 0 && h.PereId == autre.PereId => "fratrie",
        _ => "autre",
    };

    private static bool Apparentes(Humain a, Humain b) => Relation(a, b) != "autre";

    /// <summary>Penchant psychopathique, compté surtout près du haut de l'échelle (le carré écrase les petites valeurs).</summary>
    private static double Froid(Personnalite p) => Math.Pow(Psyche.Froideur(p), 2);

    /// <summary>« se vêtir » devient « me vêtir » : un savoir dit à la première personne.</summary>
    private static string Moi(string s) => s.StartsWith("se ", StringComparison.Ordinal) ? "me " + s[3..] : s.StartsWith("s'", StringComparison.Ordinal) ? "m'" + s[2..] : s;

    private static string Le(Humain h) => h.Sexe == Sexe.Femme ? "la" : "le";

    private static string Fier(Humain h) => h.Sexe == Sexe.Femme ? "fière" : "fier";

    private static string Beau(Humain h) => h.Sexe == Sexe.Femme ? "belle" : "beau";

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Savoirs qui servent (sans compter ce qu'il sait ne pas marcher).</summary>
    private static int Utiles(Humain h) => h.Savoirs.Count(s => s.Effet != Effet.Rien);
}
