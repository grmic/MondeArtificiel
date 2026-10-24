using Monde.Simulation.Monde;
using Monde.Simulation.Nature;
using Monde.Simulation.Temps;

using Monde.Simulation.Recits;

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
    internal readonly record struct V(Recit Texte, double Poids, int Registre = 1);

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
            var x = autre;
            var rel = Relation(h, autre);
            var niveau = l.Affection switch { >= 75 => 3, >= 40 => 2, <= -60 => -2, _ => -1 };
            V[] options;
            if (grief is { Motif: MotifGrief.Meurtre, VictimeId: > 0 } gm)
            {
                var v = humains[gm.VictimeId - 1];
                options =
                [
                    new(new Recit("jv.opinions.1", ("x", x), ("v", v)), 0.6),
                    new(new Recit("jv.opinions.2", ("v", v), ("x", x)), 0.3, 2),
                    new(new Recit("jv.opinions.3", ("x", x), ("v", v)), 0.05 + 2 * S(p.Agreabilite) + S(m.Compassion)),
                    new(new Recit("jv.opinions.4", ("x", x), ("v", v)), 0.05 + 2 * Bas(p.Agreabilite) + 2 * f),
                    new(new Recit("jv.opinions.5", ("x", x), ("v", v)), 0.3, 0),
                ];
            }
            else if (grief is { Motif: MotifGrief.Infidelite })
            {
                options =
                [
                    new(new Recit("jv.opinions.6", ("x", x), ("g", h)), 0.5),
                    new(new Recit("jv.opinions.7", ("x", x)), 0.05 + 2 * S(m.AnxieteAffective) + S(p.Nevrosisme)),
                    new(new Recit("jv.opinions.8", ("x", x), ("g", h)), 0.05 + 2 * S(p.Agreabilite)),
                    new(new Recit("jv.opinions.9", ("x", x), ("g", h)), 0.05 + 2 * S(m.EvitementAffectif)),
                    new(new Recit("jv.opinions.10", ("x", x)), 0.05 + 2 * S(m.Fondements.Purete), 2),
                ];
            }
            else if (grief is { Motif: MotifGrief.Coups })
            {
                options =
                [
                    new(new Recit("jv.opinions.11", ("x", x), ("g", h)), 0.5),
                    new(new Recit("jv.opinions.12", ("x", x), ("g", h)), 0.05 + 2 * Bas(p.Agreabilite)),
                    new(new Recit("jv.opinions.13", ("x", x), ("g", h)), 0.05 + 2 * S(p.Nevrosisme)),
                    new(new Recit("jv.opinions.14", ("x", x)), 0.1 + S(m.Fondements.Soin), 2),
                ];
            }
            else if (grief is { Motif: MotifGrief.Rival })
            {
                options =
                [
                    new(new Recit("jv.opinions.15", ("x", x)), 0.3 + S(m.Fondements.Loyaute)),
                    new(new Recit("jv.opinions.16", ("x", x)), 0.3 + Bas(p.Agreabilite)),
                    new(new Recit("jv.opinions.17", ("x", x)), 0.1 + S(p.Nevrosisme), 2),
                ];
            }
            else if (grief is { Motif: MotifGrief.Vol })
            {
                options =
                [
                    new(new Recit("jv.opinions.18", ("x", x)), 0.4),
                    new(new Recit("jv.opinions.19", ("x", x)), 0.3 + S(m.Fondements.Equite)),
                    new(new Recit("jv.opinions.20", ("x", x)), 0.05 + 2 * Bas(p.Agreabilite)),
                ];
            }
            else if (grief is { Motif: MotifGrief.Incendie, VictimeId: > 0 } gi)
            {
                var v = humains[gi.VictimeId - 1];
                options =
                [
                    new(new Recit("jv.opinions.21", ("x", x), ("v", v)), 0.5),
                    new(new Recit("jv.opinions.22", ("x", x), ("v", v)), 0.05 + 2 * S(p.Agreabilite)),
                    new(new Recit("jv.opinions.23", ("x", x)), 0.05 + 2 * Bas(p.Agreabilite) + S(p.Nevrosisme)),
                ];
            }
            else if (grief is { Motif: MotifGrief.Trahison })
            {
                options =
                [
                    new(new Recit("jv.opinions.24", ("x", x)), 0.4),
                    new(new Recit("jv.opinions.25", ("x", x)), 0.1 + 2 * S(m.Fondements.Loyaute)),
                ];
            }
            else options = niveau switch
            {
                3 => Cher(h, autre, l, rel, f, maintenant),
                2 => [
                    new(new Recit("jv.opinions.26", ("x", x)), 0.5),
                    new(new Recit("jv.opinions.27", ("x", x)), 0.4, 0),
                    new(new Recit("jv.opinions.28", ("x", x)), 0.05 + 2 * S(p.Extraversion)),
                    new(new Recit("jv.opinions.29", ("x", x)), 0.05 + 2 * S(p.Nevrosisme)),
                    new(new Recit("jv.opinions.30", ("x", x)), 0.05 + S(m.Honnetete) + S(p.Agreabilite)),
                    new(new Recit("jv.opinions.31", ("x", x)), l.Familiarite < 30 ? 1 : 0),
                    new(new Recit("jv.opinions.32", ("x", x)), 0.05 + 2 * S(p.Conscience)),
                    new(new Recit("jv.opinions.33", ("x", x)), 3 * f, 0),
                    new(new Recit("jv.opinions.34", ("x", x)), 0.3, 0),
                    new(new Recit("jv.opinions.35", ("x", x)), Utiles(autre) > Utiles(h) + 1 ? 0.2 + 2 * S(p.Ouverture) : 0),
                    new(new Recit("jv.opinions.36", ("x", x)), 0.1 + S(p.Extraversion), 2),
                    new(rel == "parent" ? new Recit("jv.opinions.eleve", ("x", x), ("g", h)) : new Recit("jv.opinions.desNotres", ("x", x)), 0.05 + 2 * S(m.Fondements.Loyaute)),
                ],
                -2 => [
                    new(new Recit("jv.opinions.37", ("x", x)), 0.5),
                    new(new Recit("jv.opinions.38", ("x", x)), 0.1 + S(m.Fondements.Purete)),
                    new(new Recit("jv.opinions.39", ("x", x)), 0.3, 2),
                    new(new Recit("jv.opinions.40", ("x", x)), 0.05 + 2 * Bas(p.Agreabilite)),
                    new(new Recit("jv.opinions.41", ("x", x)), 0.05 + 2 * S(p.Agreabilite) + S(m.Compassion)),
                    new(new Recit("jv.opinions.42", ("x", x), ("g", h)), 0.05 + 2 * S(p.Nevrosisme)),
                    new(new Recit("jv.opinions.43", ("x", x)), 3 * f),
                    new(new Recit("jv.opinions.44", ("x", x)), 0.2 + S(m.EvitementAffectif), 0),
                ],
                _ => [
                    new(new Recit("jv.opinions.45", ("x", x)), 0.5),
                    new(new Recit("jv.opinions.46", ("x", x)), 0.1 + Bas(p.Extraversion)),
                    new(new Recit("jv.opinions.47", ("x", x)), 0.05 + 2 * S(p.Nevrosisme)),
                    new(new Recit("jv.opinions.48", ("x", x)), autre.CommunauteId != h.CommunauteId ? 0.2 + 2 * S(m.Fondements.Loyaute) : 0),
                    new(new Recit("jv.opinions.49", ("x", x)), 0.05 + 2 * S(p.Conscience)),
                    new(new Recit("jv.opinions.50", ("x", x)), 0.1 + S(p.Agreabilite)),
                    new(new Recit("jv.opinions.51", ("x", x)), autre.Naissance > h.Naissance ? 2 * S(m.Fondements.Autorite) : 0),
                    new(new Recit("jv.opinions.52", ("x", x)), 0.3, 0),
                ],
            };
            var texte = Varier(h, $"opinion:{l.Id}", niveau * 100 + (int)(grief?.Motif ?? (MotifGrief)99), x.Nom, options);
            if (texte is not null)
                Confier(h, TypeIntime.Opinion, l.Id, texte, Math.Abs(l.Affection), grief?.EvenementId ?? 0, maintenant, $"opinion:{l.Id}");
        }
    }

    /// <summary>Ceux qui comptent le plus : la pensée dépend du lien (enfant, parent, frère ou sœur, ami) et du caractère.</summary>
    private static V[] Cher(Humain h, Humain autre, Lien l, string rel, double f, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var x = autre;
        return rel switch
        {
            "enfant" => [
                new(new Recit("jv.cher.1", ("x", x)), 0.3 + p.Attachement),
                new(new Recit("jv.cher.2", ("x", x)), 0.5, 0),
                new(new Recit("jv.cher.3", ("x", x)), 0.1 + S(p.Ouverture) + S(m.EmpathieEmotionnelle), 2),
                new(new Recit("jv.cher.4", ("x", x)), 0.05 + 2 * S(p.Nevrosisme) + S(m.AnxieteAffective)),
                new(new Recit("jv.cher.5", ("x", x)), 0.05 + 2 * S(p.Conscience)),
                new(new Recit("jv.cher.6", ("x", x)), 0.05 + 2 * S(p.Extraversion)),
                new(new Recit("jv.cher.7", ("x", x)), 0.05 + 2 * S(m.EvitementAffectif)),
                new(new Recit("jv.cher.8", ("x", x), ("g", h)), 0.1 + S(p.Extraversion) + Bas(p.Agreabilite)),
                new(new Recit("jv.cher.9", ("x", x)), 3 * f, 0),
            ],
            "parent" => [
                new(new Recit("jv.cher.10", ("x", x)), 0.3 + S(p.Conscience)),
                new(new Recit("jv.cher.11", ("x", x)), 0.05 + S(p.Nevrosisme) + S(m.Fondements.Autorite)),
                new(new Recit("jv.cher.12", ("x", x)), 0.05 + S(p.Nevrosisme) + S(m.AnxieteAffective)),
                new(new Recit("jv.cher.13", ("x", x)), autre.AgeEnAnnees(maintenant) >= 45 ? 0.2 + S(m.Compassion) + S(m.Fondements.Soin) : 0),
                new(new Recit("jv.cher.2", ("x", x)), 0.5, 0),
                new(new Recit("jv.cher.14", ("x", x), ("g", h)), 0.1 + S(m.AnxieteAffective) + S(m.EmpathieEmotionnelle), 2),
                new(new Recit("jv.cher.15", ("x", x)), 0.05 + S(p.Ouverture) + S(m.EmpathieCognitive)),
                new(new Recit("jv.cher.16", ("x", x)), 0.05 + 2 * S(m.Fondements.Autorite)),
            ],
            "fratrie" => [
                new(new Recit("jv.cher.17", ("x", x)), 0.4, 2),
                new(new Recit("jv.cher.2", ("x", x)), 0.5, 0),
                new(new Recit("jv.cher.18", ("x", x)), 0.05 + S(p.Nevrosisme) + Bas(p.Agreabilite)),
                new(new Recit("jv.cher.19", ("x", x)), 0.05 + 2 * S(m.Fondements.Loyaute) + Bas(p.Agreabilite)),
                new(new Recit("jv.cher.20", ("x", x)), 0.2 + S(p.Extraversion)),
                new(new Recit("jv.cher.21", ("x", x)), 3 * f),
            ],
            _ => [
                new(new Recit("jv.cher.22", ("x", x)), 0.4),
                new(new Recit("jv.cher.23", ("x", x)), 0.05 + S(p.Extraversion) + S(m.EmpathieCognitive)),
                new(new Recit("jv.cher.24", ("x", x)), 0.1 + S(m.EmpathieEmotionnelle), 2),
                new(new Recit("jv.cher.25", ("x", x)), 0.05 + 2 * S(p.Nevrosisme)),
                new(new Recit("jv.cher.26", ("x", x)), 0.05 + 2 * S(m.Fondements.Loyaute)),
                new(new Recit("jv.cher.27", ("x", x)), 2 * S(autre.Personnalite.Charisme, 0.55)),
                new(new Recit("jv.cher.28", ("x", x)), 0.05 + S(m.Honnetete) + S(p.Agreabilite)),
                new(new Recit("jv.cher.29", ("x", x)), l.Amis ? 0.5 : 0, 0),
                new(new Recit("jv.cher.30", ("x", x)), 0.05 + 2 * S(m.EvitementAffectif)),
                new(new Recit("jv.cher.31", ("x", x)), 0.05 + 2 * S(m.AnxieteAffective)),
                new(new Recit("jv.cher.32", ("x", x)), 3 * f),
                new(new Recit("jv.cher.33", ("x", x)), 0.05 + S(p.Ouverture) + S(p.Extraversion), 2),
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
                new(Recit.Seul("jv.imageDeSoi.1"), 0.5),
                new(Recit.Seul("jv.imageDeSoi.2"), 0.3, 2),
                new(new Recit("jv.imageDeSoi.3", ("g", h)), 0.4, 0),
                new(Recit.Seul("jv.imageDeSoi.4"), 0.05 + S(p.Nevrosisme) + S(m.AnxieteAffective)),
            }),
            { JoursSeul: >= 14 } => (2, 60.0, new V[]
            {
                new(Recit.Seul("jv.imageDeSoi.5"), 0.5),
                new(new Recit("jv.imageDeSoi.6", ("g", h)), 0.3 + S(p.Nevrosisme)),
                new(new Recit("jv.imageDeSoi.7", ("g", h)), 0.05 + 3 * S(m.EvitementAffectif)),
                new(Recit.Seul("jv.imageDeSoi.8"), 0.1 + S(p.Extraversion), 2),
            }),
            _ when communaute?.FigureId == h.Id => (3, 70.0, new V[]
            {
                new(Recit.Seul("jv.imageDeSoi.9"), 0.4 + S(p.Conscience)),
                new(Recit.Seul("jv.imageDeSoi.10"), 0.05 + 2 * S(p.Nevrosisme)),
                new(Recit.Seul("jv.imageDeSoi.11"), 0.05 + 2 * Bas(p.Agreabilite) + 2 * f),
                new(Recit.Seul("jv.imageDeSoi.12"), 0.05 + 2 * S(m.Fondements.Equite)),
            }),
            { Sante: < 40 } => (4, 55.0, new V[]
            {
                new(new Recit("jv.imageDeSoi.13", ("g", h)), 0.5),
                new(Recit.Seul("jv.imageDeSoi.14"), 0.05 + S(m.Compassion) + S(p.Agreabilite)),
                new(Recit.Seul("jv.imageDeSoi.15"), 0.05 + 2 * S(p.Transcendance), 2),
                new(Recit.Seul("jv.imageDeSoi.16"), 0.3, 0),
            }),
            _ when h.Humeur >= 70 && p.Extraversion >= 0.6 => (5, 45.0, new V[]
            {
                new(new Recit("jv.imageDeSoi.17", ("g", h)), 0.4),
                new(Recit.Seul("jv.imageDeSoi.18"), 0.3, 0),
                new(new Recit("jv.imageDeSoi.19", ("g", h)), 0.3 + S(p.Agreabilite)),
                new(Recit.Seul("jv.imageDeSoi.20"), 0.2 + S(p.Ouverture), 2),
            }),
            _ when h.Humeur < 30 => (6, 45.0, new V[]
            {
                new(Recit.Seul("jv.imageDeSoi.21"), 0.5),
                new(Recit.Seul("jv.imageDeSoi.22"), 0.05 + S(p.Transcendance) + S(p.Nevrosisme)),
                new(Recit.Seul("jv.imageDeSoi.23"), 0.2, 2),
            }),
            _ when p.Nevrosisme >= 0.7 => (7, 35.0, new V[]
            {
                new(Recit.Seul("jv.imageDeSoi.24"), 0.5),
                new(Recit.Seul("jv.imageDeSoi.25"), 0.2 + S(m.AnxieteAffective)),
                new(new Recit("jv.imageDeSoi.plusFort", ("g", h)), 0.3),
            }),
            _ => (8, 15.0, new V[]
            {
                new(Recit.Seul("jv.imageDeSoi.26"), 0.4),
                new(Recit.Seul("jv.imageDeSoi.27"), 0.3, 0),
                new(Recit.Seul("jv.imageDeSoi.28"), 0.05 + 2 * S(p.Conscience)),
                new(Recit.Seul("jv.imageDeSoi.29"), 0.05 + 2 * Bas(p.Conscience)),
                new(Recit.Seul("jv.imageDeSoi.30"), h.AgeEnAnnees(maintenant) < 22 ? 0.2 + S(p.Ouverture) : 0),
                new(Recit.Seul("jv.imageDeSoi.31"), humains.EnfantsDe(h.Id).Any(e => e.EstVivant && e.EstEnfant(maintenant)) ? 0.2 + p.Attachement : 0),
                new(Recit.Seul("jv.imageDeSoi.32"), 0.05 + 2 * S(p.Extraversion)),
                new(new Recit("jv.imageDeSoi.33", ("g", h)), 0.05 + 2 * Bas(p.Extraversion)),
                new(Recit.Seul("jv.imageDeSoi.34"), 0.05 + 2 * S(p.Agreabilite)),
                new(Recit.Seul("jv.imageDeSoi.35"), 3 * f),
                new(Recit.Seul("jv.imageDeSoi.36"), qi >= 120 ? 0.3 + S(p.Ouverture) : 0, 2),
                new(Recit.Seul("jv.imageDeSoi.37"), qi < 82 ? 0.3 : 0, 0),
                new(Recit.Seul("jv.imageDeSoi.38"), p.Intelligence.Manuelle >= 118 ? 0.4 : 0),
                new(Recit.Seul("jv.imageDeSoi.39"), p.Intelligence.Verbale >= 118 ? 0.3 + S(p.Extraversion) : 0, 2),
            }),
        };
        var texte = Choisir(h, "soi", cas, options) ?? Recit.Seul("jv.imageDeSoi.26");
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
        var x = c;
        var (forme, options) = (i, pa, e) switch
        {
            (true, true, true) => (1, new V[]
            {
                new(new Recit("jv.couple.1", ("x", x)), 0.4),
                new(new Recit("jv.couple.2", ("x", x)), 0.3, 2),
                new(new Recit("jv.couple.3", ("x", x)), 0.2 + S(p.Ouverture), 2),
                new(new Recit("jv.cher.2", ("x", x)), 0.4, 0),
                new(new Recit("jv.couple.4", ("x", x)), 0.1 + S(p.Conscience)),
            }),
            (true, true, false) => (2, new V[]
            {
                new(new Recit("jv.couple.5", ("x", x)), 0.4),
                new(new Recit("jv.couple.6", ("x", x)), 0.3 + S(m.AnxieteAffective)),
                new(new Recit("jv.couple.7", ("x", x)), 0.2 + S(p.Ouverture), 2),
                new(new Recit("jv.cher.2", ("x", x)), 0.3, 0),
            }),
            (true, false, true) => (3, new V[]
            {
                new(new Recit("jv.couple.8", ("x", x)), 0.3, 2),
                new(Recit.Seul("jv.couple.9"), 0.3 + S(p.Ouverture), 2),
                new(new Recit("jv.couple.10", ("x", x)), 0.4, 0),
                new(new Recit("jv.couple.11", ("x", x)), 0.2 + S(m.EmpathieCognitive)),
            }),
            (false, true, true) => (4, new V[]
            {
                new(new Recit("jv.couple.12", ("x", x)), 0.4),
                new(new Recit("jv.couple.13", ("x", x)), 0.3 + Bas(p.Extraversion)),
            }),
            (false, false, true) => (5, new V[]
            {
                new(new Recit("jv.couple.14", ("x", x)), 0.4),
                new(new Recit("jv.couple.15", ("x", x)), 0.3),
                new(new Recit("jv.couple.16", ("x", x)), 0.05 + S(p.Conscience) + S(m.Fondements.Autorite)),
            }),
            (false, true, false) => (6, new V[]
            {
                new(new Recit("jv.couple.17", ("x", x)), 0.4),
                new(new Recit("jv.couple.18", ("x", x)), 0.3, 0),
            }),
            (true, false, false) => (7, new V[]
            {
                new(new Recit("jv.couple.19", ("x", x)), 0.5),
                new(new Recit("jv.couple.20", ("x", x)), 0.3, 0),
            }),
            _ => (8, new V[]
            {
                new(new Recit("jv.couple.21", ("x", x)), 0.4),
                new(new Recit("jv.couple.22", ("x", x)), 0.3, 2),
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
                new V(new Recit("jv.cher.31", ("x", x)), 0.5),
                new V(new Recit("jv.couple.23", ("x", x)), 0.3, 2),
                new V(new Recit("jv.couple.24", ("x", x)), 0.4, 0)),
            StyleAttachement.Evitant => Choisir(h, "couple-style", 2,
                new V(new Recit("jv.couple.25", ("x", x), ("g", h)), 0.5),
                new V(new Recit("jv.couple.26", ("x", x)), 0.3),
                new V(new Recit("jv.couple.27", ("x", x)), 0.3, 0)),
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
        foreach (var a in humains.Vivants())
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
        var x = cible;
        var compagnon = h.PartenaireId > 0 && humains[h.PartenaireId - 1].EstVivant ? humains[h.PartenaireId - 1] : null;
        var moinsBeau = Attirance.Attractivite(h, ideal, maintenant) + 10 < Attirance.Attractivite(cible, ideal, maintenant);
        var options = new List<V>
        {
            new(new Recit("jv.desirer.1", ("x", x)), 0.4, 0),
            new(new Recit("jv.desirer.2", ("x", x)), 0.2 + S(p.Ouverture)),
            new(new Recit("jv.desirer.3", ("x", x)), 0.1 + S(p.Nevrosisme) + Bas(p.Extraversion)),
            new(new Recit("jv.desirer.4", ("x", x)), 0.3),
            new(new Recit("jv.desirer.5", ("x", x)), 0.05 + 2 * Bas(p.Extraversion)),
            new(new Recit("jv.desirer.6", ("x", x)), 0.05 + 2 * Bas(p.Conscience)),
            new(new Recit("jv.desirer.7", ("x", x)), moinsBeau ? 0.2 + S(p.Nevrosisme) : 0),
            new(new Recit("jv.desirer.8", ("x", x)), 0.05 + 2 * S(p.Ouverture) + S(p.Intelligence.Artistique / 150), 2),
            new(new Recit("jv.desirer.9", ("x", x)), 0.05 + 2 * S(p.Extraversion) * Bas(p.Nevrosisme, 0.6)),
            new(new Recit("jv.desirer.10", ("x", x)), 3 * f),
        };
        if (cible.Sexe == h.Sexe) options.Add(new(new Recit("jv.desirer.11", ("x", x)), 0.6 + Bas(p.Ouverture)));
        if (compagnon is not null)
        {
            options.Add(new(new Recit("jv.desirer.12", ("x", x), ("c", compagnon)), 0.05 + S(p.Conscience) + S(m.Honnetete) + S(m.Fondements.Purete)));
            options.Add(new(new Recit("jv.desirer.13", ("c", compagnon), ("x", x)), 0.05 + Bas(p.Conscience) + S(p.Extraversion) * Bas(m.Honnetete)));
        }
        var texte = Choisir(h, "desir", cible.Id * 4 + (compagnon is null ? 0 : 1) + (moinsBeau ? 2 : 0), [.. options]);
        var force = (15 + 50 * meilleur) * (0.6 + 0.6 * h.Desir / 100);
        if (texte is not null) Confier(h, TypeIntime.Desir, cible.Id, texte, force, 0, maintenant, "desir");

        // L'attirance devient amour quand l'affection s'y ajoute.
        var affection = h.AffectionPour(cible.Id);
        if (affection < 50 || meilleur < exigence + 0.1) return;
        var partage = cible.AffectionPour(h.Id) >= 50;
        var amour = Choisir(h, "amour", cible.Id * 2 + (partage ? 1 : 0),
            new V(new Recit("jv.desirer.14", ("x", x)), 0.5),
            new V(new Recit("jv.desirer.15", ("x", x)), partage ? 0 : 0.3 + S(p.Nevrosisme)),
            new V(new Recit("jv.desirer.16", ("x", x)), 0.05 + 2 * S(m.AnxieteAffective)),
            new V(new Recit("jv.desirer.17", ("x", x)), 0.05 + 2 * S(m.EvitementAffectif)),
            new V(new Recit("jv.desirer.18", ("x", x)), compagnon is null ? 0.05 + 2 * S(p.Extraversion) : 0),
            new V(new Recit("jv.desirer.19", ("x", x)), 0.05 + S(p.Ouverture), 2),
            new V(new Recit("jv.desirer.20", ("x", x)), compagnon is not null ? 0.2 + S(p.Conscience) : 0));
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
        var homme = h.Sexe == Sexe.Homme;
        var texte = Choisir(h, "jalousie", rival.Id,
            new V(new Recit("jv.jalouser.1", ("r", rival), ("p", c)), homme ? 0.45 : 0.35),
            new V(new Recit("jv.jalouser.2", ("p", c), ("r", rival)), (homme ? 0.45 : 0.3) * (0.5 + p.Nevrosisme)),
            new V(new Recit("jv.jalouser.3", ("p", c), ("r", rival)), (homme ? 0.3 : 0.2) * (0.5 + S(p.Ouverture)), 2),
            new V(new Recit("jv.jalouser.4", ("p", c), ("r", rival)), homme ? 0.3 : 0.45),
            new V(new Recit("jv.jalouser.5", ("p", c), ("r", rival)), (homme ? 0.3 : 0.45) * (0.5 + m.AnxieteAffective)),
            new V(new Recit("jv.jalouser.6", ("r", rival), ("p", c)), 0.05 + 2 * Bas(p.Agreabilite) + h.Colere / 100),
            new V(new Recit("jv.jalouser.7", ("p", c), ("r", rival)), (1 - p.Nevrosisme) * (1 - m.AnxieteAffective)),
            new V(new Recit("jv.jalouser.8", ("r", rival), ("p", c)), 0.05 + 2 * S(p.Nevrosisme)),
            new V(new Recit("jv.jalouser.9", ("p", c), ("r", rival)), 0.3, 0));
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
            var x = mort;
            var rel = Relation(h, mort);
            var ansDepuis = jours / 365;
            var texte = Varier(h, $"manque:{mort.Id}", 0, x.Nom,
                new V(new Recit("jv.manquer.1", ("x", x)), 0.5, 0),
                new V(new Recit("jv.manquer.2", ("x", x)), 0.05 + 2 * S(p.Transcendance)),
                new V(new Recit("jv.manquer.3", ("x", x)), 0.05 + S(p.Transcendance) + S(m.AnxieteAffective)),
                new V(new Recit("jv.manquer.4", ("x", x)), 0.05 + 2 * Bas(p.Nevrosisme)),
                new V(new Recit("jv.manquer.5", ("x", x)), 0.05 + 2 * S(p.Ouverture)),
                new V(new Recit("jv.manquer.6", ("x", x)), rel == "enfant" ? 0.2 + S(p.Conscience) + p.Attachement : 0),
                new V(new Recit("jv.manquer.7", ("x", x)), rel == "enfant" && ansDepuis >= 2 ? 0.4 : 0),
                new V(new Recit("jv.manquer.8", ("x", x)), mort.Id == h.PartenaireId || mort.PartenaireId == h.Id ? 0.5 : 0, 2),
                new V(new Recit("jv.manquer.9", ("x", x)), mort.Id == h.PartenaireId || mort.PartenaireId == h.Id ? 0.4 : 0),
                new V(new Recit("jv.manquer.10", ("x", x)), rel == "parent" ? 0.5 : 0.1),
                new V(new Recit("jv.manquer.11", ("x", x)), 0.2, 2));
            if (texte is not null) Confier(h, TypeIntime.Manque, mort.Id, texte, force, mort.MortEvenementId, maintenant, $"manque:{mort.Id}");
        }

        // Ceux qui sont vivants mais loin : on ne les a pas vus depuis longtemps.
        var absent = h.Liens.Where(l => l.Affection >= 60 && maintenant - l.Derniere > 45 * jour && humains[l.Id - 1].EstVivant)
            .MaxBy(l => l.Affection);
        if (absent is not null)
        {
            var a = humains[absent.Id - 1];
            var texte = Choisir(h, $"manque:{a.Id}", 1,
                new V(new Recit("jv.manquer.12", ("a", a)), 0.3 + S(m.AnxieteAffective)),
                new V(new Recit("jv.manquer.13", ("a", a)), 0.5, 0),
                new V(new Recit("jv.manquer.14", ("a", a)), 0.1 + S(p.Ouverture), 2),
                new V(new Recit("jv.manquer.15", ("a", a)), 0.05 + 2 * S(m.EvitementAffectif)));
            if (texte is not null) Confier(h, TypeIntime.Manque, a.Id, texte, 15 + 0.4 * absent.Affection * (0.6 + 0.8 * m.AnxieteAffective), 0, maintenant, $"manque:{a.Id}");
        }

        // La nostalgie vient avec l'âge, plus souvent aux émotifs (Wildschut et al., 2006).
        var age = h.AgeEnAnnees(maintenant);
        if (age < 40 || !Tirer(h, "nostalgie", semaine / Rythme(h), 0.25 + 0.4 * p.Nevrosisme)) return;
        var nostalgie = Choisir(h, "nostalgie", semaine / Rythme(h),
            new V(Recit.Seul("jv.manquer.16"), 0.1 + S(p.Extraversion)),
            new V(Recit.Seul("jv.manquer.17"), 0.1 + S(p.Nevrosisme)),
            new V(Recit.Seul("jv.manquer.18"), h.Sante < 70 || age >= 55 ? 0.5 : 0),
            new V(Recit.Seul("jv.manquer.19"), 0.4, 0),
            new V(Recit.Seul("jv.manquer.20"), 0.1 + S(p.Ouverture), 2),
            new V(Recit.Seul("jv.manquer.21"), age >= 55 ? 0.4 : 0),
            new V(new Recit("jv.manquer.22", ("g", h)), age >= 50 ? 0.3 : 0));
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
        var eleves = trouve is null ? 0 : humains.Vivants().Count(a => a.EstVivant && a.Savoirs.Exists(s => s.MaitreId == h.Id));
        var enfants = humains.EnfantsDe(h.Id).Count();
        var figure = communaute?.FigureId == h.Id;
        var beau = Attirance.Attractivite(h, ideal, maintenant) >= 75;
        if (trouve is null && !figure && enfants < 3 && !beau)
        {
            Retirer(h, "fierte");
            return;
        }
        var (savoirMoi, savoirInf) = trouve is null ? (null, null) : (EffetMoi(trouve.Effet), new Recit($"effetInf.{trouve.Effet}"));
        var texte = Choisir(h, "fierte", (trouve is null ? 0 : 1) + (eleves > 0 ? 2 : 0) + (figure ? 4 : 0) + (enfants >= 3 ? 8 : 0) + (beau ? 16 : 0),
            new V(new Recit("jv.fierte.1", ("g", h), ("savoir", savoirMoi)), trouve is null ? 0 : authentique),
            new V(new Recit("jv.fierte.2", ("savoir", savoirInf)), trouve is null ? 0 : orgueil),
            new V(new Recit("jv.fierte.3", ("savoir", savoirMoi)), trouve is null ? 0 : 0.3, 0),
            new V(new Recit("jv.fierte.4", ("g", h)), eleves > 0 ? authentique + 0.3 : 0),
            new V(Recit.Seul("jv.fierte.5"), eleves > 0 ? orgueil : 0),
            new V(new Recit("jv.fierte.6", ("g", h)), figure ? authentique : 0),
            new V(Recit.Seul("jv.fierte.7"), figure ? orgueil : 0),
            new V(new Recit("jv.fierte.8", ("n", enfants)), enfants >= 3 ? 0.2 + p.Attachement : 0),
            new V(new Recit("jv.fierte.9", ("g", h)), beau ? 0.1 + S(p.Extraversion) + 0.5 * orgueil : 0));
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
        foreach (var a in humains.Vivants())
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
        var x = victime;
        var acte = new Recit(pire.Motif switch
        {
            MotifGrief.Coups => "jv.acte.coups",
            MotifGrief.Infidelite => "jv.acte.infidelite",
            MotifGrief.Vol => "jv.acte.vol",
            MotifGrief.Meurtre => "jv.acte.meurtre",
            MotifGrief.Rival => "jv.acte.rival",
            MotifGrief.Incendie => "jv.acte.incendie",
            _ => "jv.acte.autre",
        }, ("x", x), ("g", h));
        var cas = culpabilite >= honte && culpabilite > 1.2 ? 1 : honte > 1 ? 2 : 3;
        var (type, texte) = cas switch
        {
            1 => (TypeIntime.Culpabilite, Choisir(h, "faute", (int)pire.Motif,
                new V(new Recit("jv.fautes.1", ("acte", acte)), 0.5),
                new V(new Recit("jv.fautes.2", ("acte", acte)), 0.3, 2),
                new V(new Recit("jv.fautes.3", ("acte", acte), ("x", x)), 0.1 + S(p.Extraversion) + S(p.Agreabilite)),
                new V(new Recit("jv.fautes.4", ("acte", acte)), 0.3, 0))),
            2 => (TypeIntime.Honte, Choisir(h, "faute", 10 + (int)pire.Motif,
                new V(new Recit("jv.fautes.5", ("g", h), ("x", x)), 0.4),
                new V(new Recit("jv.fautes.6", ("x", x)), 0.4),
                new V(Recit.Seul("jv.fautes.7"), 0.2 + S(m.AnxieteAffective), 2))),
            _ => (TypeIntime.Opinion, Choisir(h, "faute", 20 + (int)pire.Motif,
                new V(new Recit("jv.fautes.8", ("x", x)), 0.5),
                new V(new Recit("jv.fautes.9", ("x", x)), 0.3 + 2 * f))),
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
        foreach (var a in humains.Vivants())
        {
            if (!a.EstVivant || a.Id == h.Id || a.Sexe != h.Sexe || a.CommunauteId != h.CommunauteId || a.EstEnfant(maintenant)) continue;
            var beaute = Attirance.Attractivite(a, ideal, maintenant) - moi - 20;
            var savoir = (Utiles(a) - Utiles(h) - 2) * 5.0;
            var couple = seul && a.PartenaireId > 0 ? 10.0 : 0;
            var (e, k) = new[] { (beaute, 1), (savoir, 2), (couple, 3) }.MaxBy(t => t.Item1);
            if (e > ecart) (ecart, cible, motif) = (e, a, k);
        }
        if (cible is null) return;
        var x = cible;
        var maligne = 0.05 + S(p.Nevrosisme) + Bas(p.Agreabilite) + Bas(m.Honnetete);
        var benigne = 0.1 + S(p.Conscience) + S(p.Ouverture) + 0.5 * S(p.Agreabilite);
        var texte = motif switch
        {
            1 => Choisir(h, "envie", cible.Id * 4 + 1,
                new V(new Recit("jv.envier.1", ("x", x)), maligne),
                new V(new Recit("jv.envier.2", ("g", h), ("x", x)), benigne),
                new V(new Recit("jv.envier.3", ("x", x)), 0.3, 0)),
            2 => Choisir(h, "envie", cible.Id * 4 + 2,
                new V(new Recit("jv.envier.4", ("x", x)), benigne),
                new V(new Recit("jv.envier.5", ("x", x)), maligne),
                new V(new Recit("jv.envier.6", ("x", x)), 0.3, 0)),
            _ => Choisir(h, "envie", cible.Id * 4 + 3,
                new V(new Recit("jv.envier.7", ("x", x)), 0.4),
                new V(new Recit("jv.envier.8", ("x", x)), maligne),
                new V(new Recit("jv.envier.9", ("x", x)), benigne)),
        };
        if (texte is not null) Confier(h, TypeIntime.Envie, cible.Id, texte, 20 + Math.Min(40, ecart) * (0.5 + p.Nevrosisme), 0, maintenant, "envie");
    }

    // ───────────── Peurs et espoirs ─────────────

    private static void Craindre(Humain h, IReadOnlyList<Humain> humains, Communaute? communaute, long maintenant)
    {
        var p = h.Personnalite;
        var m = p.Moral;
        var petits = humains.EnfantsDe(h.Id).Any(e => e.EstVivant && e.EstEnfant(maintenant));
        if (h.Faim >= 70 || h.Reserves < 15)
        {
            var texte = Choisir(h, "peur:faim", petits ? 1 : 0,
                new V(Recit.Seul("jv.craindre.1"), 0.5),
                new V(Recit.Seul("jv.craindre.2"), 0.3, 2),
                new V(Recit.Seul("jv.craindre.3"), 0.05 + 2 * S(p.Nevrosisme)),
                new V(Recit.Seul("jv.craindre.4"), petits ? 0.4 + p.Attachement : 0),
                new V(Recit.Seul("jv.craindre.5"), 0.3, 0));
            Confier(h, TypeIntime.Peur, 0, texte!, 40 + h.Faim / 2, communaute?.DisetteEvenementId ?? 0, maintenant, "peur:faim");
        }
        else Retirer(h, "peur:faim");

        if (communaute is { Disette: true, DestinationX: >= 0 })
        {
            Confier(h, TypeIntime.Espoir, 0, new Recit("jv.craindre.migration", ("vers", Lieux.VersRecit(communaute.CentreX, communaute.CentreY, communaute.DestinationX, communaute.DestinationY))),
                50, communaute.DisetteEvenementId, maintenant, "espoir:migration");
        }
        if (h.EnceinteDepuis is not null)
        {
            var texte = Choisir(h, "espoir:enfant", 0,
                new V(Recit.Seul("jv.craindre.6"), 0.5),
                new V(Recit.Seul("jv.craindre.7"), 0.1 + p.Attachement),
                new V(Recit.Seul("jv.craindre.8"), 0.05 + 2 * S(p.Nevrosisme)),
                new V(Recit.Seul("jv.craindre.9"), 0.2 + S(p.Ouverture), 2));
            Confier(h, TypeIntime.Espoir, 0, texte!, 55, 0, maintenant, "espoir:enfant");
        }
        else Retirer(h, "espoir:enfant");

        if (h.Peur >= 30)
        {
            var nom = CatastropheLe(h.PeurType);
            var texte = Choisir(h, "peur:catastrophe", (int)h.PeurType,
                new V(new Recit("jv.craindre.10", ("cat", nom)), 0.5),
                new V(new Recit("jv.craindre.11", ("cat", nom)), 0.05 + 2 * S(p.Nevrosisme)),
                new V(new Recit("jv.craindre.12", ("cat", nom)), 0.05 + S(p.Conscience) * Bas(p.Nevrosisme, 0.6) * 2),
                new V(new Recit("jv.craindre.13", ("cat", nom)), 0.3, 0));
            Confier(h, TypeIntime.Peur, 0, texte!, h.Peur, h.PeurEvenementId, maintenant, "peur:catastrophe");
        }

        // Peur de finir seul : les anxieux en amour, sans compagnon.
        var age = h.AgeEnAnnees(maintenant);
        if (h.PartenaireId == 0 && age >= 18 && m.AnxieteAffective >= 0.6)
            Confier(h, TypeIntime.Peur, 0, Choisir(h, "peur:seul", 0,
                new V(new Recit("jv.craindre.14", ("g", h)), 0.5),
                new V(Recit.Seul("jv.craindre.15"), 0.3, 0),
                new V(Recit.Seul("jv.craindre.16"), 0.2, 2))!, 20 + 40 * m.AnxieteAffective, 0, maintenant, "peur:seul");
        else Retirer(h, "peur:seul");

        // La mort qui approche : l'âge ou la maladie, plus présente aux anxieux ; les esprits tournés vers l'invisible se demandent où l'on va.
        if (age >= 50 || h.Sante < 45)
            Confier(h, TypeIntime.Peur, 0, Choisir(h, "peur:mort", h.Sante < 45 ? 1 : 0,
                new V(Recit.Seul("jv.craindre.17"), 0.3 + S(p.Nevrosisme)),
                new V(Recit.Seul("jv.craindre.18"), 0.05 + 2 * S(p.Transcendance)),
                new V(Recit.Seul("jv.craindre.19"), 0.1 + S(m.Compassion) + p.Attachement / 2),
                new V(Recit.Seul("jv.craindre.20"), 0.05 + 2 * Bas(p.Nevrosisme)))!,
                15 + 30 * p.Nevrosisme + (h.Sante < 45 ? 20 : 0), 0, maintenant, "peur:mort");
        else Retirer(h, "peur:mort");

        // Espoirs de l'âge adulte : trouver quelqu'un, avoir des enfants.
        if (h.PartenaireId == 0 && age is >= 16 and < 45 && m.AnxieteAffective < 0.6)
            Confier(h, TypeIntime.Espoir, 0, Choisir(h, "espoir:amour", 0,
                new V(Recit.Seul("jv.craindre.21"), 0.5),
                new V(Recit.Seul("jv.craindre.22"), 0.2 + S(p.Ouverture), 2),
                new V(Recit.Seul("jv.craindre.23"), 0.2 + S(p.Extraversion), 0))!, 20 + 20 * p.Extraversion, 0, maintenant, "espoir:amour");
        else Retirer(h, "espoir:amour");
        var enfants = humains.EnfantsDe(h.Id).Count();
        if (enfants == 0 && age is >= 18 and < 40 && p.Attachement >= 0.6)
            Confier(h, TypeIntime.Espoir, 0, Choisir(h, "espoir:enfants", 0,
                new V(Recit.Seul("jv.craindre.24"), 0.5),
                new V(Recit.Seul("jv.craindre.25"), 0.3, 2))!, 20 + 30 * p.Attachement, 0, maintenant, "espoir:enfants");
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
            options.Add(new(new Recit("jv.rever.1", ("m", mort)), 1.0));
            options.Add(new(new Recit("jv.rever.2", ("m", mort)), 0.2 + 2 * S(p.Transcendance)));
            options.Add(new(new Recit("jv.rever.3", ("m", mort)), 0.5));
            cause = h.DeuilEvenementId;
        }
        if (h.Peur >= 20 && cauchemar)
        {
            options.Add(new(h.PeurType switch
            {
                TypeCatastrophe.Seisme => Recit.Seul("jv.cauchemar.seisme"),
                TypeCatastrophe.Eruption => Recit.Seul("jv.cauchemar.eruption"),
                TypeCatastrophe.Tsunami or TypeCatastrophe.Crue => Recit.Seul("jv.cauchemar.eau"),
                TypeCatastrophe.Incendie => Recit.Seul("jv.cauchemar.feu"),
                TypeCatastrophe.Cyclone => Recit.Seul("jv.cauchemar.vent"),
                TypeCatastrophe.Epidemie => Recit.Seul("jv.cauchemar.epidemie"),
                TypeCatastrophe.VagueDeFroid or TypeCatastrophe.HiverVolcanique or TypeCatastrophe.HiverDImpact => new Recit("jv.cauchemar.froid", ("g", h)),
                TypeCatastrophe.Comete or TypeCatastrophe.Bolide or TypeCatastrophe.Impact or TypeCatastrophe.Eclipse or TypeCatastrophe.Aurore => Recit.Seul("jv.cauchemar.ciel"),
                _ => Recit.Seul("jv.cauchemar.autre"),
            }, 1.5));
            cause = h.PeurEvenementId;
        }
        if (h.Faim >= 60)
        {
            options.Add(new(Recit.Seul(h.Sait(Effet.Cuisson) ? "jv.rever.festin" : "jv.rever.fruits"), 1.2));
        }
        var desir = h.Memoire.Jardin.Find(x => x.Cle == "desir");
        if (desir is not null && desir.SujetId > 0)
        {
            var x = humains[desir.SujetId - 1];
            options.Add(new(new Recit("jv.rever.4", ("x", x)), 0.6));
            options.Add(new(new Recit("jv.rever.5", ("x", x)), 0.4));
        }
        var jalousie = h.Memoire.Jardin.Find(x => x.Cle == "jalousie");
        if (jalousie is not null && h.PartenaireId > 0)
            options.Add(new(new Recit("jv.rever.6", ("p", humains[h.PartenaireId - 1]), ("r", humains[jalousie.SujetId - 1])), 0.8));
        // Puis les rêves de toujours.
        if (cauchemar)
        {
            options.Add(new(Recit.Seul("jv.rever.7"), 0.5));
            options.Add(new(Recit.Seul("jv.rever.8"), 0.4));
            options.Add(new(new Recit("jv.rever.9", ("g", h)), 0.2 + S(p.Moral.AnxieteAffective)));
            options.Add(new(Recit.Seul("jv.rever.10"), 0.3, 2));
        }
        else
        {
            options.Add(new(Recit.Seul("jv.rever.11"), 0.2 + S(p.Ouverture)));
            options.Add(new(Recit.Seul("jv.rever.12"), 0.3));
            options.Add(new(Recit.Seul("jv.rever.13"), 0.05 + S(p.Transcendance) + S(p.Ouverture)));
            options.Add(new(Recit.Seul("jv.rever.14"), 0.3));
            options.Add(new(Recit.Seul("jv.rever.15"), 0.05 + 2 * S(p.Transcendance), 2));
            options.Add(new(Recit.Seul("jv.rever.16"), h.MereId > 0 ? 0.3 + S(p.Moral.AnxieteAffective) : 0));
            options.Add(new(Recit.Seul("jv.rever.17"), 0.05 + S(p.Ouverture) + S(p.Intelligence.Generale / 150)));
            options.Add(new(Recit.Seul("jv.rever.18"), 0.4, 0));
            options.Add(new(Recit.Seul("jv.rever.19"), 0.2, 2));
        }
        var texte = Choisir(h, "reve", semaine, [.. options]);
        if (texte is null) return;
        Confier(h, TypeIntime.Reve, mort?.Id ?? 0, texte, 25 + (cauchemar ? 25 : 10) + 20 * p.Ouverture, cause, maintenant, "reve");
        if (mort is not null && texte.Texte.Contains(mort.Nom))
            Croyances.Ajouter(h, 1.5 * (0.3 + p.Transcendance), $"{Il(h)} a revu {mort.Nom} en rêve", cause, maintenant);
        // Le rêve d'un mort qui revient : de là peut naître l'idée que les morts vivent ailleurs (Tylor, 1871).
        if (mort is not null && texte.Texte.Contains("veille sur moi") && Tirer(h, "croyance:morts", semaine, 0.5 * p.Transcendance))
        {
            Confier(h, TypeIntime.Croyance, 0, Recit.Seul("jv.rever.croyanceMorts"), 30 + 60 * p.Transcendance, cause, maintenant, "croyance:morts");
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
            var nom = EffetMoi(premier.Effet);
            options.Add(premier.Origine == OrigineSavoir.Entite
                ? new(new Recit("jv.seSouvenir.1", ("savoir", nom)), 0.6 * gai + S(p.Transcendance))
                : new(new Recit("jv.seSouvenir.2", ("savoir", nom)), 0.8 * gai));
        }
        var maitre = h.Savoirs.Where(s => s.Origine == OrigineSavoir.Imitation && s.MaitreId > 0).MinBy(s => s.AppriLe);
        if (maitre is not null)
            options.Add(new(new Recit("jv.seSouvenir.3", ("m", humains[maitre.MaitreId - 1]), ("savoir", EffetMoi(maitre.Effet))), 0.4 * gai + S(p.Moral.Honnetete)));
        var aine = humains.EnfantsDe(h.Id).MinBy(e => e.Naissance);
        if (aine is not null) options.Add(new(new Recit("jv.seSouvenir.4", ("a", aine)), 0.5 * gai + p.Attachement / 2));
        var mere = h.MereId > 0 ? humains[h.MereId - 1] : null;
        if (mere is { EstVivant: false }) options.Add(new(new Recit("jv.seSouvenir.5", ("m", mere)), 0.5 * triste, 2));
        if (h.PeurEvenementId > 0)
            options.Add(new(new Recit("jv.seSouvenir.6", ("cat", CatastropheLe(h.PeurType))), 0.5 * triste + S(p.Nevrosisme)));
        if (h.MereId > 0) options.Add(new(Recit.Seul("jv.seSouvenir.7"), 0.3 * gai));
        else
        {
            options.Add(new(Recit.Seul("jv.seSouvenir.8"), 0.4));
            options.Add(new(new Recit("jv.seSouvenir.9", ("g", h)), 0.1 + S(p.Transcendance) + S(p.Ouverture), 2));
        }
        if (h.Memoire.Jardin.Exists(x => x.Cle.StartsWith("exil", StringComparison.Ordinal)))
            options.Add(new(new Recit("jv.seSouvenir.exil", ("g", h)), 0.6 * triste));
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
        var bebe = humains.EnfantsDe(h.Id).FirstOrDefault(e => e.EstVivant && e.EstBebe(maintenant));
        var options = new List<V>
        {
            new(Recit.Seul("jv.sEmerveiller.1"), 0.3 + S(p.Ouverture)),
            new(Recit.Seul("jv.sEmerveiller.2"), 0.3, 0),
            new(Recit.Seul("jv.sEmerveiller.3"), 0.2, 2),
            new(Recit.Seul("jv.sEmerveiller.4"), 0.3),
            new(Recit.Seul("jv.sEmerveiller.5"), 0.05 + 2 * S(p.Transcendance), 2),
        };
        options.AddRange(biome switch
        {
            Biome.Montagne or Biome.SommetEnneige => [
                new V(new Recit("jv.sEmerveiller.6", ("g", h)), 0.8),
                new V(Recit.Seul("jv.sEmerveiller.7"), 0.4 + S(p.Ouverture))],
            Biome.Ocean or Biome.EauPeuProfonde or Biome.Plage => [
                new V(Recit.Seul("jv.sEmerveiller.8"), 0.8),
                new V(Recit.Seul("jv.sEmerveiller.9"), 0.4, 2)],
            Biome.ForetDeConiferes => [new V(Recit.Seul("jv.sEmerveiller.10"), 0.8)],
            Biome.ForetTropicale => [new V(Recit.Seul("jv.sEmerveiller.11"), 0.8, 2)],
            Biome.Desert => [new V(Recit.Seul("jv.sEmerveiller.12"), 0.8)],
            Biome.Riviere => [new V(Recit.Seul("jv.sEmerveiller.13"), 0.8)],
            Biome.Lac => [new V(Recit.Seul("jv.sEmerveiller.14"), 0.8, 2)],
            Biome.Marais => [new V(Recit.Seul("jv.sEmerveiller.15"), 0.8)],
            _ => [new V(Recit.Seul("jv.sEmerveiller.16"), 0.8)],
        });
        if (bebe is not null) options.Add(new(new Recit("jv.sEmerveiller.17", ("b", bebe)), 0.6 + p.Attachement));
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
            new V(Recit.Seul("jv.sEnnuyer.1"), 0.5),
            new V(Recit.Seul("jv.sEnnuyer.2"), 0.4, 0),
            new V(Recit.Seul("jv.sEnnuyer.3"), 0.1 + 2 * S(p.Ouverture)),
            new V(Recit.Seul("jv.sEnnuyer.4"), 0.2 + S(p.Ouverture), 2),
            new V(Recit.Seul("jv.sEnnuyer.5"), 0.1 + S(p.Nevrosisme)),
            new V(Recit.Seul("jv.sEnnuyer.6"), 0.1 + S(p.Extraversion)));
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
            new V(Recit.Seul("jv.seDemander.1"), 0.4),
            new V(Recit.Seul("jv.seDemander.2"), 0.4),
            new V(Recit.Seul("jv.seDemander.3"), 0.4),
            new V(Recit.Seul("jv.seDemander.4"), 0.3 + S(p.Ouverture)),
            new V(Recit.Seul("jv.seDemander.5"), 0.4, 0),
            new V(Recit.Seul("jv.seDemander.6"), 0.3 + S(p.Nevrosisme)),
            new V(Recit.Seul("jv.seDemander.7"), 0.05 + 2 * S(p.Transcendance)),
            new V(Recit.Seul("jv.seDemander.8"), 0.1 + vif),
            new V(Recit.Seul("jv.seDemander.9"), 0.1 + vif),
            new V(Recit.Seul("jv.seDemander.10"), 0.2 + vif),
            new V(Recit.Seul("jv.seDemander.11"), h.Sait(Effet.Flamme) || h.Sait(Effet.Foyer) ? 0.4 + vif : 0),
            new V(Recit.Seul("jv.seDemander.12"), h.Sait(Effet.Soulagement) || h.Sait(Effet.Poison) ? 0.5 : 0),
            new V(Recit.Seul("jv.seDemander.13"), 0.05 + 2 * S(p.Intelligence.Spatiale / 100, 1.1)),
            new V(Recit.Seul("jv.seDemander.14"), 2 * vif, 2),
            new V(Recit.Seul("jv.seDemander.15"), 0.2 + S(p.Moral.EmpathieCognitive)),
            new V(Recit.Seul("jv.seDemander.16"), 0.3),
            new V(Recit.Seul("jv.seDemander.17"), 0.1 + vif));
        if (texte is not null) Confier(h, TypeIntime.Question, 0, texte, 25 + 40 * p.Ouverture, 0, maintenant, "question:monde");
    }

    // ───────────── Outils ─────────────

    /// <summary>
    /// Choisit une pensée parmi celles qu'autorise le caractère : chaque option pèse selon les traits qui la rendent
    /// plausible, et selon l'aptitude verbale (les mots simples aux taiseux, les images aux esprits vifs en mots).
    /// Le tirage est stable pour une même clé et un même palier : la pensée ne change que si la situation change.
    /// </summary>
    private static Recit? Choisir(Humain h, string cle, long palier, params V[] options) => Varier(h, cle, palier, null, options);

    /// <summary>
    /// Comme <see cref="Choisir"/>, pour une pensée sur quelqu'un (<paramref name="sujet"/>) : une forme déjà employée
    /// ailleurs dans le même jardin pour quelqu'un d'autre pèse moins, pour qu'un jardin ne répète pas dix fois la même phrase.
    /// La comparaison se fait sur les phrases françaises : le choix ne dépend pas de la langue de l'observateur.
    /// </summary>
    internal static Recit? Varier(Humain h, string cle, long palier, string? sujet, params V[] options)
    {
        var verbal = (h.Personnalite.Intelligence.Verbale - 100) / 15;
        var total = 0.0;
        Span<double> poids = stackalloc double[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            var o = options[i];
            var w = Math.Max(0, o.Poids) * o.Registre switch { 0 => Math.Exp(-0.7 * verbal), 2 => Math.Exp(0.7 * verbal), _ => 1 };
            if (w > 0 && sujet is not null && o.Texte.Texte is var phrase && phrase.IndexOf(sujet, StringComparison.Ordinal) is var k and >= 0)
            {
                var (avant, apres) = (phrase[..k], phrase[(k + sujet.Length)..]);
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

    /// <summary>Un savoir dit à la première personne (« me vêtir », « dress myself »).</summary>
    private static Recit EffetMoi(Effet e) => Recit.Seul($"effetMoi.{e}");

    /// <summary>« le séisme », « the earthquake ».</summary>
    internal static Recit CatastropheLe(TypeCatastrophe t) => Recit.Seul($"catastropheLe.{t}");

    /// <summary>Savoirs qui servent (sans compter ce qu'il sait ne pas marcher).</summary>
    private static int Utiles(Humain h) => h.Savoirs.Count(s => s.Effet != Effet.Rien);
}
