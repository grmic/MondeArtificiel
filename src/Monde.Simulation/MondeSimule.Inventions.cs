using System.Globalization;
using System.Text.Json;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Recits;

namespace Monde.Simulation;

/// <summary>
/// Les inventions de l'Entité libre (0.35.3) : une plante ou une bête que le monde n'avait jamais connue, un savoir que les lois du
/// monde jugent, un mot donné à une communauté. Elles entrent dans les systèmes existants et suivent ensuite leurs lois.
/// </summary>
public sealed partial class MondeSimule
{
    private readonly List<Invention> _inventions = [];

    /// <summary>Les inventions de l'Entité libre (disparues comprises).</summary>
    public List<Invention> InventionsDuMonde()
    {
        lock (_verrou) return [.. _inventions];
    }

    /// <summary>Remet les inventions d'un monde chargé dans sa flore et sa faune, dans l'ordre où elles sont nées.</summary>
    private void ChargerInventions(IReadOnlyList<Invention>? inventions)
    {
        foreach (var i in (inventions ?? []).OrderBy(x => x.Id))
        {
            var numero = i.Sorte == "plante" ? Flore.Ajouter(Inventions.Plante(i, Graine)).Numero : Faune.Ajouter(Inventions.Bete(i, Graine)).Numero;
            var lue = i with { Numero = numero };
            _inventions.Add(lue);
            if (!i.Disparue) continue;
            if (i.Sorte == "plante") Flore.Retirer(numero);
            else Faune.Retirer(numero);
        }
    }

    private static readonly Biome[] SansVie = [Biome.Ocean, Biome.EauPeuProfonde, Biome.Lac, Biome.SommetEnneige];

    private static readonly JsonSerializerOptions JsonMots = new() { IncludeFields = true };

    /// <summary>Le mot donné (« idée|forme ») ; l'idée est une idée du langage, un effet (un savoir) ou « plante:n ».</summary>
    private string? IdeeDe(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        var t = texte.Trim();
        if (Idees.Libelles.ContainsKey(t)) return t;
        if (Enum.TryParse<Effet>(t, true, out var e) && Enum.IsDefined(e) && e != Effet.Rien) return Idees.Savoir(e);
        if (t.StartsWith("plante:", StringComparison.Ordinal) && int.TryParse(t[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n >= 1 && n <= Flore.Especes.Count) return t;
        return null;
    }

    /// <summary>Une forme de mot donnée par l'Entité : des lettres seulement, en minuscules (null si elle n'en a pas donné de valable).</summary>
    private static string? FormeDeMot(string? nom)
    {
        if (nom is null) return null;
        var lettres = new string([.. nom.Trim().ToLowerInvariant().Where(char.IsLetter)]);
        return lettres.Length is >= 2 and <= 16 ? lettres : null;
    }

    private (Geste G, Matiere A, Matiere B)? GesteDe(ChangementLibre c)
    {
        if (!Enum.TryParse<Geste>(c.Geste, true, out var g) || !Enum.IsDefined(g)) return null;
        var a = Matiere.Aucune;
        var b = Matiere.Aucune;
        if (c.A is { Length: > 0 } ta && (!Enum.TryParse(ta, true, out a) || !Enum.IsDefined(a))) return null;
        if (c.B is { Length: > 0 } tb && (!Enum.TryParse(tb, true, out b) || !Enum.IsDefined(b))) return null;
        return (g, a, b);
    }

    /// <summary>La graine du jugement d'un geste donné : la même à la vérification et à l'application.</summary>
    private ulong GraineDuGeste(int humain, Geste g, Matiere a, Matiere b) =>
        Graine ^ (ulong)_secondes * 0x9E3779B97F4A7C15UL ^ (ulong)humain << 20 ^ (ulong)g << 12 ^ (ulong)a << 6 ^ (ulong)b;

    /// <summary>Plus d'essais pour qu'un geste donné réussisse : au-delà, les lois du monde n'en tirent rien.</summary>
    private const int EssaisDuGeste = 32;

    /// <summary>
    /// Ce que les lois du monde tirent d'un geste donné par l'Entité : le premier résultat parmi quelques essais (un geste qui ne
    /// réussit qu'une fois sur dix est possible ; un geste qui ne donne jamais rien ne l'est pas). Toujours le même pour un même
    /// humain, un même geste et un même instant.
    /// </summary>
    private Resultat Juger(Humain h, Geste g, Matiere a, Matiere b, long maintenant)
    {
        for (var k = 0; k < EssaisDuGeste; k++)
        {
            var r = _comportement.JugerGeste(h, g, a, b, GraineDuGeste(h.Id, g, a, b) + (ulong)k * 0x632BE59BD9B4E019UL, maintenant);
            if (r.Effet != Effet.Rien) return r;
        }
        return default;
    }

    /// <summary>Vérifie un changement d'invention ; false si ce n'en est pas un.</summary>
    private bool VerifierInvention(ChangementLibre c)
    {
        var quoi = c.Quoi ?? "";
        switch (quoi)
        {
            case "plante_nouvelle":
            case "bete_nouvelle":
            {
                SurLaCarte(c.X, c.Y, quoi);
                if (Array.IndexOf(SansVie, Carte.BiomeEn(c.X, c.Y)) >= 0) throw Faute($"terrain|{quoi}|{c.X}|{c.Y}");
                var formes = quoi == "plante_nouvelle" ? Inventions.FormesPlante : Inventions.FormesBete;
                if (Array.IndexOf(formes, (c.Forme ?? "").Trim().ToLowerInvariant()) < 0) throw Faute($"forme|{quoi}|{c.Forme}");
                if (string.IsNullOrWhiteSpace(c.Nom)) throw Faute($"nom|{quoi}");
                if (quoi == "plante_nouvelle" && c.Aliment is { Length: > 0 } al && Array.IndexOf(Inventions.Aliments, al.Trim().ToLowerInvariant()) < 0)
                    throw Faute($"texte|{quoi}|{al}");
                foreach (var (k, v) in c.Proprietes ?? [])
                {
                    if (quoi == "plante_nouvelle")
                    {
                        if (Array.IndexOf(Inventions.VertusPlante, k) < 0) throw Faute($"texte|{quoi}|{k}");
                        Borne(v, 0, 1, $"{quoi}.{k}");
                    }
                    else
                    {
                        if (!Inventions.TraitsBete.TryGetValue(k, out var b)) throw Faute($"texte|{quoi}|{k}");
                        Borne(v, b.Min, b.Max, $"{quoi}.{k}");
                    }
                }
                return true;
            }
            case "savoir_nouveau":
            {
                var h = HumainDe(c.Humain, quoi);
                if (!h.EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (GesteDe(c) is not { } x) throw Faute($"geste|{quoi}|{c.Geste} {c.A} {c.B}");
                var r = Juger(h, x.G, x.A, x.B, _secondes);
                if (r.Effet == Effet.Rien) throw Faute($"lois|{quoi}|{c.Geste} {c.A} {c.B}");
                return true;
            }
            case "mot":
            {
                if (c.Communaute != 0)
                {
                    if (!CommunauteDe(c.Communaute, quoi).Active) throw Faute($"dispersee|{quoi}|{c.Communaute}");
                }
                else if (!HumainDe(c.Humain, quoi).EstVivant) throw Faute($"mort|{quoi}|{c.Humain}");
                if (IdeeDe(c.Texte) is null) throw Faute($"idee|{quoi}|{c.Texte}");
                return true;
            }
            default:
                return false;
        }
    }

    private (ChangementFait Fait, Recit Ligne) AppliquerInvention(ChangementLibre c, long annonce, long maintenant, Func<Recit, Humain?, int, Recit> ecrire)
    {
        var quoi = c.Quoi;
        switch (quoi)
        {
            case "plante_nouvelle":
            case "bete_nouvelle":
            {
                var plante = quoi == "plante_nouvelle";
                var forme = Array.IndexOf(plante ? Inventions.FormesPlante : Inventions.FormesBete, c.Forme!.Trim().ToLowerInvariant());
                var nom = c.Nom!.Trim();
                if (nom.Length > Inventions.NomMax) nom = nom[..Inventions.NomMax];
                var aliment = plante && c.Aliment is { Length: > 0 } al ? (SorteAliment)Array.IndexOf(Inventions.Aliments, al.Trim().ToLowerInvariant()) : SorteAliment.Aucune;
                var biome = Carte.BiomeEn(c.X, c.Y);
                var i = new Invention(_inventions.Count + 1, plante ? "plante" : "bete", 0, biome, forme, nom, new(c.Proprietes ?? []), aliment, maintenant, annonce);
                var numero = plante ? Flore.Ajouter(Inventions.Plante(i, Graine)).Numero : Faune.Ajouter(Inventions.Bete(i, Graine)).Numero;
                i = i with { Numero = numero };
                _inventions.Add(i);
                var r = ecrire(new Recit(plante ? "libre.ch.plante" : "libre.ch.bete", ("nom", nom),
                    ("forme", Recit.Seul($"libre.forme.{(plante ? Inventions.FormesPlante : Inventions.FormesBete)[forme]}")), ("terrain", Recit.Seul($"biome.{biome}")),
                    ("ou", Lieux.DirectionR(Carte, c.X, c.Y))), null, 0);
                return (new ChangementFait(quoi, 0, 0, i.Id, [], [], ""), r);
            }
            case "savoir_nouveau":
            {
                var h = _humains[c.Humain - 1];
                var (g, a, b) = GesteDe(c)!.Value;
                var res = Juger(h, g, a, b, maintenant);
                if (res.Effet == Effet.Rien)
                    return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, [], []), ecrire(new Recit("libre.ch.savoirRien", ("h", h), ("geste", Physique.Phrase(g, a, b))), h, 0));
                var avant = h.Savoirs.ToList();
                _comportement.DonnerGeste(h, g, a, b, res, _humains, maintenant);
                VerserJournal();
                var appris = h.Savoirs.Where(s => !avant.Contains(s)).Select(s => SavoirGarde.De(s)).ToList();
                var r = ecrire(new Recit("libre.ch.savoirNouveau", ("h", h), ("geste", Physique.Phrase(g, a, b)), ("e", Recit.Seul($"effet.{res.Effet}"))), h, 0);
                return (new ChangementFait(quoi, h.Id, h.CommunauteId, 0, [], [], JsonSerializer.Serialize(appris)), r);
            }
            case "mot":
            {
                var idee = IdeeDe(c.Texte)!;
                var com = c.Communaute != 0 ? _communautes[c.Communaute - 1] : null;
                var cibles = com is not null ? _humains.Vivants.Where(x => x.CommunauteId == com.Id).ToList() : [_humains[c.Humain - 1]];
                var langueDe = com ?? (cibles[0].CommunauteId > 0 ? _communautes[cibles[0].CommunauteId - 1] : null);
                var langue = Langues.LangueDe(langueDe);
                var forme = FormeDeMot(c.Nom) ?? Langues.Forger(_social.SonsDe(langueDe), new Alea(Graine ^ (ulong)annonce), 2);
                var gardes = new List<MotGarde>();
                foreach (var h in cibles)
                {
                    var k = Langues.Indice(h, idee, langue);
                    gardes.Add(new MotGarde(h.Id, idee, k >= 0 ? h.Vocabulaire[k] : null));
                    var mot = new Mot(idee, forme, false, 0.8, maintenant, 0, langue);
                    if (k >= 0) h.Vocabulaire[k] = mot;
                    else h.Vocabulaire.Add(mot);
                }
                var sens = Idees.Dit(idee, Flore);
                var r = com is not null
                    ? ecrire(new Recit("libre.ch.mot", ("c", com), ("dit", Langues.Dit(forme, false)), ("sens", sens)), null, com.Id)
                    : ecrire(new Recit("libre.ch.motSeul", ("h", cibles[0]), ("dit", Langues.Dit(forme, false)), ("sens", sens)), cibles[0], 0);
                return (new ChangementFait(quoi, com is null ? cibles[0].Id : 0, com?.Id ?? 0, 0, [], new() { ["le"] = maintenant }, JsonSerializer.Serialize(gardes, JsonMots)), r);
            }
            default:
                throw Faute($"sorte|{quoi}");
        }
    }

    private Recit? RetablirInvention(ChangementFait f, Humain? h)
    {
        switch (f.Quoi)
        {
            case "plante_nouvelle":
            case "bete_nouvelle":
            {
                var k = _inventions.FindIndex(i => i.Id == f.Autre);
                if (k < 0 || _inventions[k].Disparue) return null;
                var i = _inventions[k] with { Disparue = true };
                _inventions[k] = i;
                if (i.Sorte == "plante") Flore.Retirer(i.Numero);
                else Faune.Retirer(i.Numero);
                return new Recit(i.Sorte == "plante" ? "libre.d.plante" : "libre.d.bete", ("nom", i.Nom));
            }
            case "savoir_nouveau":
            {
                if (h is null || f.Extra.Length == 0) return null;
                var appris = JsonSerializer.Deserialize<List<SavoirGarde>>(f.Extra) ?? [];
                foreach (var g in appris)
                {
                    var s = g.Rendre();
                    h.Savoirs.RemoveAll(x => x.Meme(s.Geste, s.A, s.B, s.Effet, s.Espece, s.Symptome) && x.Pieces == s.Pieces && x.Origine == OrigineSavoir.Entite);
                }
                return appris.Count == 0 ? null : new Recit("libre.d.savoirNouveau", ("h", h));
            }
            case "mot":
            {
                var gardes = JsonSerializer.Deserialize<List<MotGarde>>(f.Extra, JsonMots) ?? [];
                var le = (long)f.Apres.GetValueOrDefault("le");
                var rendus = 0;
                string? forme = null;
                foreach (var g in gardes)
                {
                    if (g.Humain < 1 || g.Humain > _humains.Count) continue;
                    var x = _humains[g.Humain - 1];
                    // Le mot donné est retiré (s'il est encore là, tel quel) ; l'ancien revient.
                    var k = x.Vocabulaire.FindIndex(m => m.AuteurId == 0 && m.Le == le && m.Idee == g.Idee);
                    if (k < 0) continue;
                    forme ??= x.Vocabulaire[k].Forme;
                    if (g.Avant is { } avant) x.Vocabulaire[k] = avant;
                    else x.Vocabulaire.RemoveAt(k);
                    rendus++;
                }
                return rendus == 0 ? null : new Recit("libre.d.mot", ("dit", Langues.Dit(forme!, false)));
            }
            default:
                return null;
        }
    }
}

/// <summary>Le mot qu'un humain avait pour une idée avant que l'Entité ne lui en donne un (null : il n'en avait pas).</summary>
internal sealed record MotGarde(int Humain, string Idee, Mot? Avant);
