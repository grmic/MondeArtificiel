using Monde.Simulation.Recits;
using Monde.Simulation.Temps;

namespace Monde.Simulation.Humains;

/// <summary>
/// Où dort le tout-petit (0.45.0). Rien n'est imposé : chaque soir, celle qui le porte décide, selon son caractère et la nuit.
/// <para>
/// Ce que dit la recherche. Dans la grande majorité des sociétés étudiées, le nourrisson dort avec sa mère (Barry et Paxson,
/// 1971, sur l'échantillon interculturel standard) ; chez les chasseurs-cueilleurs, presque toujours, peau contre peau
/// (McKenna et McDade, 2005). Les mères qui allaitent et dorment avec leur bébé prennent d'elles-mêmes une position
/// protectrice : sur le flanc, genoux remontés, un bras au-dessus de la tête du bébé, le bébé à hauteur du sein (Ball, 2006).
/// D'autres que la mère s'occupent souvent des petits, la grand-mère d'abord (Hrdy, 2009 ; Hawkes et al., 1998). Le froid
/// rapproche, la chaleur écarte un peu ; la peur fait serrer l'enfant contre soi. Les poids ci-dessous sont des estimations
/// qui suivent ces tendances : aucune n'est une règle.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>
    /// La mère du bébé dort : il dort aussi, là où elle l'a décidé en se couchant (une fois par nuit ; un réveil dans la nuit ne
    /// change rien). Renvoie celui contre qui il dort s'il l'a confié à un autre, sinon null.
    /// </summary>
    internal Humain? CoucherLeBebe(Humain b, Humain mere, long maintenant)
    {
        if (b.Coucher != Coucher.Aucune)
        {
            if (b.Coucher != Coucher.Confie) return null;
            // Celui qui le garde est encore là ? Sinon, elle le reprend contre elle.
            if (b.CoucheAvecId > 0 && b.CoucheAvecId <= _tous.Count && _tous[b.CoucheAvecId - 1] is { EstVivant: true } g && g.X == b.X && g.Y == b.Y)
                return g;
            b.Coucher = Coucher.Contre;
            b.CoucheAvecId = mere.Id;
            return null;
        }

        var p = mere.Personnalite;
        var t = Temperature(b.X, b.Y, maintenant);
        // Un abri ou un feu adoucissent la nuit : le froid compte moins.
        var abri = Math.Clamp(AbriEn(b.X, b.Y) + (Installations.FoyerEn(b.X, b.Y) is null ? 0 : ChaleurDuFeu), 0, 1);
        var froid = Math.Clamp((TemperatureConfort - t) / 10, 0, 1.5) * (1 - 0.6 * abri);
        var chaleur = Math.Clamp((t - 25) / 7, 0, 1);
        var peur = Math.Clamp(Math.Max(mere.Peur, mere.PeurDesBetes) / 100, 0, 1);
        var fatigue = Math.Clamp((mere.Fatigue - 70) / 30, 0, 1);
        var mal = mere.EstMalade(maintenant) || mere.Sante < 50 || mere.BlessureGrave >= 20 ? 1.0 : 0;
        var abattue = mere.EstDeprime ? 1 : mere.Chagrin > 50 ? 0.5 : 0;
        var delaisse = Delaisse(mere) ? 1.0 : 0;
        var autre = 0.0;
        foreach (var o in _tous.Vivants())
            if (o.Id != b.Id && o.EstVivant && o.MereId == mere.Id && o.EstBebe(maintenant) && o.X == b.X && o.Y == b.Y) autre = 1;
        var (garde, lien) = Gardien(b, mere, maintenant);

        // L'habitude : ce qu'elle a fait la nuit d'avant, d'autant plus qu'elle est constante.
        // Chaque mère prend vite ses habitudes : on ne change de manière que si la nuit le demande (estimation).
        double H(Coucher c) => b.CoucherPrecedent == c ? 4 + 6 * p.Conscience : 1;
        var poids = new (Coucher Maniere, double Poids)[]
        {
            (Coucher.Contre, (0.5 + 0.9 * p.Attachement + 0.3 * p.Agreabilite) * (1 + 1.5 * froid + 1.2 * peur) * (1 - 0.7 * chaleur)
                             * (1 - 0.5 * mal) * (1 - 0.4 * abattue) * H(Coucher.Contre)),
            // À côté : la chaleur, et chez les anxieuses la crainte de l'écraser.
            (Coucher.ACote, (0.25 + 0.3 * (1 - p.Attachement) + 0.2 * p.Nevrosisme) * (1 + 2.5 * chaleur) * (1 - 0.6 * Math.Min(1, froid))
                            * (1 - 0.5 * peur) * H(Coucher.ACote)),
            (Coucher.Confie, garde is null ? 0 : (0.03 + 1.2 * fatigue + 1.5 * mal + 0.8 * autre + 0.4 * abattue) * lien * (0.6 + 0.6 * p.Agreabilite)
                                                 * H(Coucher.Confie)),
            (Coucher.AEcart, (0.005 + 0.5 * abattue * (1 - p.Attachement) + 0.15 * Math.Pow(1 - p.Attachement, 3) + 0.2 * delaisse)
                             * (1 - 0.8 * peur) * (1 - 0.6 * Math.Min(1, froid)) * H(Coucher.AEcart)),
        };
        var total = poids.Sum(x => x.Poids);
        var u = alea.Reel() * total;
        var choix = Coucher.Contre;
        foreach (var (m, w) in poids)
        {
            u -= w;
            if (u < 0)
            {
                choix = m;
                break;
            }
        }
        b.Coucher = choix;
        b.CoucheAvecId = choix == Coucher.Confie ? garde!.Id : mere.Id;

        // Le journal : le premier choix, et ensuite un changement de manière (passer de contre elle à à côté d'elle, d'une nuit à
        // l'autre, est la routine de bien des mères).
        // Le bébé laissé seul à l'écart est toujours noté ; confié ou couché autrement, au plus une fois par saison (le confier à sa
        // grand-mère une nuit sur deux devient vite une habitude, pas un événement).
        var premier = b.CoucherNoteLe == 0;
        if (choix != b.CoucherPrecedent && (premier || choix == Coucher.AEcart || maintenant - b.CoucherNoteLe >= 90 * Calendrier.SecondesParJour))
        {
            b.CoucherNoteLe = maintenant;
            var raison = choix switch
            {
                Coucher.Contre => peur >= 0.3 && peur * 1.2 >= froid * 1.5 ? "peur" : froid >= 0.3 ? "froid" : "tendresse",
                Coucher.ACote => chaleur >= 0.3 ? "chaleur" : p.Nevrosisme >= 0.6 ? "crainte" : "place",
                Coucher.Confie => mal > 0 ? "mal" : fatigue >= 0.3 ? "fatigue" : autre > 0 ? "autre" : abattue > 0 ? "abattue" : "confiance",
                _ => abattue > 0 ? "abattue" : delaisse > 0 ? "delaisse" : "lassitude",
            };
            var texte = new Recit($"j.coucher.{choix}", ("m", mere), ("b", b), ("g", garde is null ? "" : garde),
                ("raison", new Recit($"j.coucher.raison.{raison}", ("m", mere), ("b", b))));
            Noter(maintenant, choix == Coucher.AEcart ? TypeEvenement.Delaissement : TypeEvenement.Lien, mere, choix == Coucher.Confie ? garde : b, texte);
        }
        return choix == Coucher.Confie ? garde : null;
    }

    /// <summary>
    /// À qui elle pourrait le confier pour la nuit, sur sa case : le père, sa propre mère, une grande sœur ou un grand frère, une
    /// autre mère qui allaite, et sinon quelqu'un des siens ; d'autant plus qu'elle l'aime, et qu'il est en état de le garder.
    /// </summary>
    private (Humain? Garde, double Lien) Gardien(Humain b, Humain mere, long maintenant)
    {
        Humain? meilleur = null;
        var score = 0.0;
        foreach (var g in _tous.Vivants())
        {
            if (!g.EstVivant || g.Id == mere.Id || g.Id == b.Id || g.X != b.X || g.Y != b.Y || g.AgeEnAnnees(maintenant) < 8) continue;
            if (g.EstMalade(maintenant) || g.Sante < 50 || g.Fatigue > 90) continue;
            double proche;
            if (g.Id == b.PereId || g.Id == mere.PartenaireId) proche = 1.0;
            else if (g.Id == mere.MereId) proche = 1.1;
            else if (g.MereId == mere.Id) proche = g.Sexe == Sexe.Femme ? 0.9 : 0.7;
            else if (g.Sexe == Sexe.Femme && g.DernierAccouchement is { } d && maintenant - d < 3 * Calendrier.SecondesParAn) proche = 0.6;
            else if (g.CommunauteId != 0 && g.CommunauteId == mere.CommunauteId) proche = 0.2;
            else continue;
            var s = proche * Math.Clamp(0.5 + (mere.AffectionPour(g.Id) + 50) / 100, 0.1, 1.5) * (0.6 + 0.5 * g.Personnalite.Attachement);
            if (s > score)
            {
                (meilleur, score) = (g, s);
            }
        }
        return (meilleur, Math.Min(1.5, score));
    }

    /// <summary>Elle ne dort plus et le jour est là : la nuit est finie, elle décidera de nouveau au prochain coucher.</summary>
    internal static void Lever(Humain b)
    {
        if (b.Coucher == Coucher.Aucune) return;
        b.CoucherPrecedent = b.Coucher;
        b.Coucher = Coucher.Aucune;
        b.CoucheAvecId = 0;
    }
}
