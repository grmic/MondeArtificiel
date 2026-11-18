using Monde.Simulation.Humains;
using Monde.Simulation.Recits;

namespace Monde.Simulation;

/// <summary>Ce que l'Entité révèle, ce que ses mots font ressentir, ce qu'on lui demande et lui promet (0.42.9).</summary>
public sealed partial class MondeSimule
{
    /// <summary>Longueur maximale d'une révélation, en caractères.</summary>
    public const int RevelationMax = 600;

    /// <summary>Ce que l'Entité a révélé, de la première révélation à la dernière (copie).</summary>
    public List<Revelation> Revelations()
    {
        lock (_verrou) return _cultes.Revelations.Select(r => r.Copier()).ToList();
    }

    /// <summary>Pour chaque révélation : combien de vivants la savent, combien y croient, et leur fidélité moyenne.</summary>
    public Dictionary<int, (int Savent, int Croient, double Fidelite)> SavoirsDesRevelations()
    {
        lock (_verrou)
        {
            var d = new Dictionary<int, (int, int, double)>();
            foreach (var h in _humains.Vivants())
            {
                if (!h.EstVivant) continue;
                foreach (var c in h.Revelations)
                {
                    var (s, k, f) = d.GetValueOrDefault(c.Id);
                    d[c.Id] = (s + 1, k + (c.Croit ? 1 : 0), f + c.Fidelite);
                }
            }
            return d.ToDictionary(x => x.Key, x => (x.Value.Item1, x.Value.Item2, x.Value.Item1 == 0 ? 0 : x.Value.Item3 / x.Value.Item1));
        }
    }

    private Revelation NouvelleRevelation(string texte, PorteeRevelation portee, int destinataire, ModeParole mode, int auteur)
    {
        var t = texte.Trim();
        if (t.Length == 0) throw new ArgumentException("texte");
        if (t.Length > RevelationMax) t = t[..RevelationMax];
        if (mode is not (ModeParole.Reve or ModeParole.Voix or ModeParole.Apparition)) throw new ArgumentException("mode");
        var r = new Revelation
        {
            Id = _cultes.Revelations.Count + 1, Le = _secondes, Texte = t, Portee = portee, Destinataire = destinataire, Mode = mode, Auteur = auteur,
        };
        _cultes.Revelations.Add(r);
        return r;
    }

    /// <summary>
    /// Au cours d'un dialogue, l'Entité révèle <paramref name="texte"/> à l'humain : il le garde en mémoire (voir
    /// <see cref="Comportement.Reveler"/>). Rend la ligne du journal.
    /// </summary>
    public long RevelerEnDialogue(int humainId, string texte, ModeParole mode, bool croit, long cause, int auteur = 0)
    {
        lock (_verrou)
        {
            if (humainId < 1 || humainId > _humains.Count || !_humains[humainId - 1].EstVivant) throw new ArgumentException("humain");
            var r = NouvelleRevelation(texte, PorteeRevelation.Humain, humainId, mode, auteur);
            var id = _comportement.Reveler(_humains[humainId - 1], r, croit, cause, _secondes);
            VerserJournal();
            return id;
        }
    }

    /// <summary>
    /// L'Entité révèle <paramref name="texte"/> à toute une communauté (<paramref name="communaute"/> &gt; 0) ou au Monde entier (0) :
    /// chacun, en âge de comprendre, la comprend ou non, y croit ou non, et garde ce qu'il a compris. Une voix ou une apparition
    /// réveille les dormeurs. Rend la révélation, combien l'ont entendue, comprise et crue.
    /// </summary>
    public (Revelation Revelation, int Entendu, int Compris, int Crus) Reveler(int communaute, string texte, ModeParole mode, int auteur = 0)
    {
        lock (_verrou)
        {
            if (communaute < 0 || communaute > _communautes.Count || communaute > 0 && !_communautes[communaute - 1].Active)
                throw new ArgumentException("communaute");
            var ecoutent = _humains.Vivants().Where(h => h.EstVivant && !h.EstBebe(_secondes) && (communaute == 0 || h.CommunauteId == communaute))
                .OrderBy(h => h.Id).ToList();
            if (ecoutent.Count == 0) throw new ArgumentException("communaute");
            var r = NouvelleRevelation(texte, communaute == 0 ? PorteeRevelation.Monde : PorteeRevelation.Communaute, communaute, mode, auteur);
            foreach (var h in ecoutent)
            {
                if (mode is ModeParole.Voix or ModeParole.Apparition && h.Activite == Activite.Dormir) h.Activite = Activite.Repos;
                if (_fond is { } f && h.Id < f.Length) f[h.Id] = false;
            }
            var m = Recit.Seul($"modeParole.{mode}");
            var texteCourt = Comportement.Court(r.Texte);
            r.EvenementId = communaute == 0
                ? _comportement.NoterAuMonde(_secondes, new Recit("rev.monde", ("m", m), ("t", texteCourt), ("n", ecoutent.Count)), null)
                : _comportement.NoterAuMonde(_secondes, new Recit("rev.communaute", ("m", m), ("c", _communautes[communaute - 1]), ("t", texteCourt),
                    ("n", ecoutent.Count)), null, 0, communaute);
            var (compris, crus) = _comportement.RevelerA(ecoutent, r, r.EvenementId, _secondes);
            VerserJournal();
            return (r.Copier(), ecoutent.Count, compris, crus);
        }
    }

    /// <summary>Il ressent le ton des mots de l'Entité (voir <see cref="Comportement.RessentirTon"/>) ; null : rien.</summary>
    public Recit? RessentirTon(int humainId, TonParole ton, ModeParole mode, bool croit, long cause)
    {
        lock (_verrou)
        {
            if (humainId < 1 || humainId > _humains.Count) throw new ArgumentException("humain");
            var r = _comportement.RessentirTon(_humains[humainId - 1], ton, mode, croit, cause, _secondes);
            VerserJournal();
            return r;
        }
    }

    /// <summary>Au cours d'un dialogue, il demande lui-même quelque chose à l'Entité : une vraie prière. Rend la ligne du journal (0 : rien).</summary>
    public long DemanderEnDialogue(int humainId, SujetPriere sujet, int pour, long cause)
    {
        lock (_verrou)
        {
            if (humainId < 1 || humainId > _humains.Count) throw new ArgumentException("humain");
            var id = _comportement.DemanderEnDialogue(_humains[humainId - 1], sujet, pour, _humains, cause, _secondes);
            VerserJournal();
            return id;
        }
    }

    /// <summary>Au cours d'un dialogue, il promet un geste à l'Entité. Rend la ligne du journal (0 : rien de promis).</summary>
    /// <summary>Ce que son corps a fait en répondant à l'Entité, au journal (0.45.19) ; renvoie l'événement (0 : rien).</summary>
    public long NoterGestes(int humainId, IReadOnlyList<string> gestes, long cause)
    {
        lock (_verrou)
        {
            if (humainId < 1 || humainId > _humains.Count) throw new ArgumentException("humain");
            var id = _comportement.NoterGestes(_humains[humainId - 1], gestes, cause, _secondes);
            VerserJournal();
            return id;
        }
    }

    public long Promettre(int humainId, Parole parole, ModeParole mode, long cause)
    {
        lock (_verrou)
        {
            if (humainId < 1 || humainId > _humains.Count) throw new ArgumentException("humain");
            var id = _comportement.Promettre(_humains[humainId - 1], parole, mode, cause, _secondes);
            VerserJournal();
            return id;
        }
    }
}
