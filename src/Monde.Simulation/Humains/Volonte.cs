using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Sa volonté du moment devant ce que l'Entité lui demande (0.42.8). Elle reprend ce qui comptait déjà (le respect de l'autorité, sa
/// croyance, sa foi, son amour, sa crainte ou sa révolte envers l'Entité, la psychopathie, ce que la parole lui coûte et ce qu'elle
/// rejoint de ses désirs) et y ajoute son caractère et son état : l'agréabilité rend accommodant (Graziano et Eisenberg, 1997) ;
/// la peur devant une voix ou une présence fait obéir (Milgram, 1974) ; la colère pousse à résister (Lerner et Keltner, 2001) ;
/// la fatigue, la faim et la douleur retiennent d'un effort ; le chagrin et l'abattement ôtent l'élan ; la bonne humeur et la
/// curiosité y portent. Chaque raison est pesée par son caractère : rien n'est fixe d'un humain à l'autre.
/// <para>
/// La chance d'obéir est la somme de ces raisons ; les trois plus fortes, dans le sens de ce qu'il a décidé, sont dites au journal et
/// au joueur.
/// </para>
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Effort physique d'un geste demandé, de 0 (s'asseoir) à 1 (partir loin).</summary>
    private static double Effort(Parole p) => p switch
    {
        Parole.AssiedsToi or Parole.Dors or Parole.Mange or Parole.Jeune or Parole.Ecoute or Parole.TuGuideras => 0,
        Parole.Prie => 0.1,
        Parole.Bois or Parole.Chante or Parole.ParleA or Parole.Console or Parole.Pardonne or Parole.NeTeVengePas or Parole.ResteIci => 0.25,
        Parole.CacheToi or Parole.VeilleLeFeu or Parole.Partage or Parole.ResteAvec or Parole.Quitte or Parole.PorteMaParole => 0.4,
        Parole.Enseigne or Parole.UnisToi or Parole.PrendsSoin or Parole.VaVers or Parole.DefendsLesTiens => 0.55,
        Parole.Frappe => 0.7,
        Parole.VaChercherAManger or Parole.Tue => 0.85,
        Parole.PartsVers => 1,
        _ => 0.4,
    };

    /// <summary>
    /// La chance qu'il obéisse, et les raisons les plus fortes pour (s'il obéit) et contre (s'il refuse), en mots.
    /// <paramref name="refus"/> : la raison propre à la parole, qui dit ce qu'elle lui coûte.
    /// </summary>
    private (double Obeir, Recit Pour, Recit Contre) Vouloir(Humain h, Parole parole, ModeParole mode, double cout, double envie, double foi,
        bool psychopathe, Recit? refus)
    {
        var p = h.Personnalite;
        var (amour, crainte, revolte) = (h.Amour / 100, h.Crainte / 100, h.Revolte / 100);
        var raisons = new List<(double Poids, Recit Texte)>();
        void R(double poids, string cle) => raisons.Add((poids, new Recit(cle, ("h", h))));

        // Ce qu'il pense de l'Entité (comme avant la 0.42.8), et son caractère accommodant ou non.
        var socle = 0.25;
        var autorite = 0.35 * p.Moral.Fondements.Autorite;
        var croyance = 0.25 * h.Croyance / 100;
        var parFoi = 0.15 * foi;
        var parAmour = 0.15 * amour;
        var parCrainte = 0.1 * crainte;
        var accommodant = 0.2 * (p.Agreabilite - 0.5);
        // Devant une voix ou une présence, la peur fait obéir, d'autant plus que l'on est anxieux.
        var peur = mode is ModeParole.Voix or ModeParole.Apparition ? 0.2 * h.Peur / 100 * (0.5 + p.Nevrosisme) : 0;
        var deference = socle + autorite + croyance + parFoi + parAmour + parCrainte + accommodant + peur;
        // La somme de ces raisons vaut la déférence × (1 − révolte) × (1 − coût), comme avant : chacune y garde sa part.
        var multiplie = (psychopathe ? 0.4 : 1) * (1 - 0.8 * revolte);
        R(autorite, "vol.autorite");
        R(croyance, "vol.croyance");
        R(parFoi, "vol.foi");
        R(parAmour, "vol.amour");
        R(parCrainte, "vol.crainte");
        R(accommodant, accommodant >= 0 ? "vol.accommodant" : "vol.rebelle");
        R(peur, "vol.peur");
        if (revolte > 0) R(-deference * (psychopathe ? 0.4 : 1) * 0.8 * revolte, "vol.revolte");
        if (psychopathe) R(-deference * 0.6, "vol.froideur");
        // Ce que la parole lui coûte (sa raison propre) et ce qu'elle rejoint de ses désirs.
        if (cout > 0) raisons.Add((-deference * multiplie * cout, refus ?? new Recit("vol.cout", ("h", h))));
        if (envie != 0) R(0.4 * envie, envie > 0 ? "vol.envie" : "vol.contrarie");

        // Son état du moment, pesé par son caractère.
        var effort = Effort(parole);
        if (h.Colere > 10) R(-0.3 * h.Colere / 100 * (1.2 - p.Agreabilite), "vol.colere");
        if (effort > 0 && h.Fatigue > 30) R(-0.35 * effort * (h.Fatigue - 30) / 70 * (1.3 - p.Endurance), "vol.fatigue");
        if (effort > 0 && parole is not (Parole.Mange or Parole.VaChercherAManger) && h.Faim > 40)
            R(-0.3 * effort * (h.Faim - 40) / 60 * (1.3 - p.Conscience), "vol.faim");
        if (effort > 0 && parole != Parole.Bois && h.Soif > 40) R(-0.3 * effort * (h.Soif - 40) / 60 * (1.3 - p.Conscience), "vol.soif");
        if (effort > 0 && (h.Sante < 70 || h.EstMalade(_maintenant))) R(-0.3 * effort * (100 - Math.Min(h.Sante, 85)) / 100 * (1.3 - p.Endurance), "vol.douleur");
        var abattu = Math.Max(h.Chagrin / 100, h.EstDeprime ? 0.7 : 0);
        if (abattu > 0.2) R(-0.25 * abattu * (0.5 + p.Nevrosisme), "vol.abattu");
        if (Math.Abs(h.Humeur - 50) > 15) R(0.12 * (h.Humeur - 50) / 50, h.Humeur > 50 ? "vol.joie" : "vol.morose");
        // Le curieux essaie volontiers ce qu'on lui propose (Cloninger : recherche de nouveauté).
        if (p.Ouverture > 0.6) R(0.15 * (p.Ouverture - 0.6), "vol.curiosite");

        var obeir = Math.Clamp(raisons.Sum(r => r.Poids) + socle, 0.01, 0.97);
        return (obeir, Liste(raisons.Where(r => r.Poids > 0.01).OrderByDescending(r => r.Poids)),
            Liste(raisons.Where(r => r.Poids < -0.01).OrderBy(r => r.Poids)));
    }

    /// <summary>Les trois premières raisons, en une phrase (« son amour pour l'Entité, sa foi et sa bonne humeur »).</summary>
    private static Recit Liste(IEnumerable<(double Poids, Recit Texte)> raisons)
    {
        var t = raisons.Take(3).Select(r => r.Texte).ToList();
        return t.Count switch
        {
            0 => Recit.Seul("vol.rien"),
            1 => new Recit("vol.liste1", ("a", t[0])),
            2 => new Recit("vol.liste2", ("a", t[0]), ("b", t[1])),
            _ => new Recit("vol.liste3", ("a", t[0]), ("b", t[1]), ("c", t[2])),
        };
    }
}
