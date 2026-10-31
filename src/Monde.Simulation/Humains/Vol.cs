using Monde.Simulation.Recits;

namespace Monde.Simulation.Humains;

/// <summary>
/// Vols de nourriture. Chez les chasseurs-cueilleurs, presque tout se partage, et celui qui prend sans donner est vite repéré,
/// moqué, puis tenu à l'écart (Boehm, 1999 ; Wiessner, 2005). Voler tente surtout les peu honnêtes (honnêteté-humilité basse ;
/// Ashton et Lee, 2007) et les psychopathes, quand la faim presse. Le voleur ment et cache : il se fait prendre d'autant moins
/// qu'il ment bien, d'autant plus que sa victime lit bien les autres.
/// </summary>
public sealed partial class Comportement
{
    /// <summary>Probabilité, à chaque pas d'un humain tenaillé par la faim, très tenté, à côté d'une victime rassasiée, de la voler.</summary>
    private const double VolParPas = 0.002;

    private void Voler(Humain h, IReadOnlyList<Humain> tous, long maintenant)
    {
        if (h.Faim < 55 || h.EstEnfant(maintenant) || h.Activite == Activite.Dormir || Social is null) return;
        var p = h.Personnalite;
        var froideur = Psyche.Froideur(p);
        var tentation = Math.Clamp((0.45 - p.Moral.Honnetete) / 0.25, 0, 1) + froideur;
        // L'équité retient : prendre la part d'un autre, c'est tricher.
        tentation *= 1.3 - 0.6 * p.Moral.Fondements.Equite;
        if (tentation <= 0 || alea.Reel() >= VolParPas * tentation * (h.Faim - 55) / 45 * Regards.Retenue(h)) return;
        if (Recent(h, "vol", 3, maintenant)) return;

        Humain? victime = null;
        foreach (var v in tous.Vivants())
        {
            if (!v.EstVivant || v.Id == h.Id || v.Faim >= 35 || v.EstEnfant(maintenant) || Distance(h.X, h.Y, v.X, v.Y) > 1) continue;
            // On ne vole pas ceux qu'on aime, sauf si l'on n'aime personne.
            if (h.AffectionPour(v.Id) >= 40 * (1 - froideur) && froideur < 1) continue;
            if (v.Id == h.PartenaireId || v.MereId == h.Id || v.PereId == h.Id) continue;
            victime = v;
            break;
        }
        if (victime is null) return;
        DiffererVol(h.Id, victime.Id, tous, maintenant);
    }

    // À part : la fermeture n'est créée que s'il y a vol.
    private void DiffererVol(int ih, int iv, IReadOnlyList<Humain> tous, long maintenant) =>
        // Pendant un pas simultané, le vol (et ce qui s'ensuit) a lieu après le pas, si tous deux vivent encore.
        Tour.Differer(tous, t =>
        {
            if (t[ih - 1].EstVivant && t[iv - 1].EstVivant) VolerA(t[ih - 1], t[iv - 1], t, maintenant);
        });

    private void VolerA(Humain h, Humain victime, IReadOnlyList<Humain> tous, long maintenant)
    {
        // Toujours présent : Voler l'a vérifié avant de différer le vol.
        if (Social is not { } social) return;
        var p = h.Personnalite;
        var froideur = Psyche.Froideur(p);
        var pris = Math.Min(h.Faim - 20, 25 + 10 * alea.Reel());
        h.Faim -= pris;
        victime.Faim = Math.Min(100, victime.Faim + 0.6 * pris);

        // La victime s'en aperçoit, ou un témoin ; un menteur habile passe inaperçu plus souvent.
        var cache = Dissimulation(h);
        var vigilance = victime.Activite == Activite.Dormir ? 0.3 : 1;
        var surpris = alea.Reel() < 0.45 * Perspicacite(victime) * cache * vigilance;
        Humain? temoin = null;
        if (!surpris)
        {
            foreach (var t in tous.Vivants())
            {
                if (!t.EstVivant || t.Id == h.Id || t.Id == victime.Id || t.EstEnfant(maintenant) || t.Activite == Activite.Dormir
                    || Distance(t.X, t.Y, h.X, h.Y) > 1) continue;
                if (alea.Reel() < 0.25 * Perspicacite(t) * cache) temoin = t;
                break;
            }
        }

        var psychopathe = Psyche.EstPsychopathe(h, maintenant);
        if (!surpris && temoin is null)
        {
            var id = Noter(maintenant, TypeEvenement.Vol, h, victime, new Recit("j.vol.cache", ("h", h), ("v", victime)),
                karma: Regard.Vol);
            if (psychopathe)
                Confier(h, TypeIntime.ImageDeSoi, 0, Recits.Recit.Seul("js.vol.froid"), 40, id, maintenant, "vol");
            else
            {
                // Qui ressent la peine des autres a honte de ce qu'il a fait ; le froid n'éprouve rien.
                var honte = 100 * Math.Clamp(0.5 * p.Moral.EmpathieEmotionnelle + 0.3 * p.Moral.Honnetete + 0.2 * p.Moral.Fondements.Equite, 0, 1) * (1 - froideur);
                Confier(h, honte >= 30 ? TypeIntime.Peine : TypeIntime.Intention, victime.Id, honte >= 30
                    ? new Recit("js.vol.honte", ("v", victime), ("g", h))
                    : new Recit("js.vol.cache", ("v", victime)), Math.Max(20, honte), id, maintenant, "vol");
            }
            return;
        }

        var qui = surpris ? victime : temoin!;
        var texte = surpris
            ? new Recit("j.vol.surpris", ("v", victime), ("h", h))
            : new Recit("j.vol.temoin", ("t", temoin!), ("h", h), ("v", victime));
        var vol = Noter(maintenant, TypeEvenement.Vol, h, victime, texte, significatif: psychopathe, karma: Regard.Vol);
        Confier(h, TypeIntime.Intention, victime.Id, psychopathe
            ? new Recit("js.vol.vuFroid", ("v", victime), ("g", h))
            : new Recit("js.vol.vu", ("v", victime), ("g", h)), 30, vol, maintenant, "vol");

        // La victime en veut au voleur, d'autant plus qu'elle tient à l'équité ; ceux qui l'apprennent l'estiment moins.
        var vm = victime.Personnalite;
        var colere = Math.Min(100, 30 + 40 * (1 - vm.Agreabilite) + 30 * (vm.Moral.Fondements.Equite - 0.5));
        VieSociale.AjouterGrief(victime, new Grief(h.Id, MotifGrief.Vol, colere, colere, vol));
        victime.Colere = Math.Min(100, victime.Colere + colere / 2);
        social.ChangerAffection(victime, h, -colere / 4, maintenant);
        if (temoin is not null)
        {
            VieSociale.AjouterGrief(temoin, new Grief(h.Id, MotifGrief.Vol, colere / 2, colere / 2, vol));
            social.ChangerAffection(temoin, h, -colere / 8, maintenant);
        }
        social.Offenser(h, victime, 3, vol, tous, maintenant);
        // Un psychopathe démasqué : ceux qui l'ont vu le savent désormais (Hare, 1993 : derrière le charme, la manipulation).
        if (psychopathe)
        {
            foreach (var t in new[] { victime, temoin })
            {
                if (t is null) continue;
                Jardin.Confier(t, TypeIntime.Opinion, h.Id, new Recit("js.vol.masque", ("h", h)),
                    50, vol, maintenant, $"opinion:{h.Id}");
            }
        }

        // Pris sur le fait, le froid peut frapper pour garder ce qu'il a pris : violence instrumentale, sans colère
        // (Woodworth et Porter, 2002).
        if (surpris && alea.Reel() < 0.5 * froideur * (h.Sexe == Sexe.Homme ? 1.3 : 0.7))
        {
            var degats = 5 + 15 * alea.Reel();
            victime.Sante -= degats;
            Medecine.Blesser(victime, degats, maintenant, OrigineBlessure.Coups);
            victime.Humeur = Math.Max(0, victime.Humeur - degats / 3);
            var coups = Noter(maintenant, TypeEvenement.Conflit, h, victime, new Recit("j.vol.coups", ("h", h), ("v", victime)), vol,
                karma: Regard.Coups);
            VieSociale.AjouterGrief(victime, new Grief(h.Id, MotifGrief.Coups, Math.Min(100, 30 + degats), Math.Min(100, 30 + degats), coups));
            social.Offenser(h, victime, 4, coups, tous, maintenant);
            if (victime.Sante <= 0)
            {
                Mourir(victime, maintenant, CauseDeMort.Meurtre, coups, h);
                social.Offenser(h, victime, 15, victime.MortEvenementId, tous, maintenant);
            }
        }
    }
}
