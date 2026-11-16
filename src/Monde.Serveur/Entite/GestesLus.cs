using System.Text.RegularExpressions;
using Monde.Simulation.Humains;

namespace Monde.Serveur.Entite;

/// <summary>
/// Les gestes de tous les jours que l'Entité demande en toutes lettres (0.44.4) : « assieds-toi et prie », « dors », « sit down
/// and pray ». Un filet sous la traduction du LLM : un petit modèle, ou un modèle qui réfléchit et s'arrête avant de répondre,
/// rendait parfois une liste vide, et l'humain ne recevait aucun ordre. On ne garde que les phrases à l'impératif : ni question,
/// ni négation (« ne prie pas »), ni « je t'en prie ».
/// </summary>
public static class GestesLus
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Chaque geste et ses tournures, en français et en anglais (sur le texte en minuscules, traits d'union en espaces).</summary>
    private static readonly (Parole Parole, Regex Motif)[] Motifs =
    [
        (Parole.VaChercherAManger, new(@"\b(?:va|allez|pars|partez)\s+(?:chercher|trouver|cueillir)\s+(?:à|de quoi)\s+manger\b|\b(?:cherche|cherchez)\s+(?:à|de quoi)\s+manger\b|\b(?:go\s+and\s+|go\s+)?(?:find|gather|look\s+for)\s+food\b", Options)),
        (Parole.VeilleLeFeu, new(@"\b(?:veille|veillez|garde|gardez|entretiens|entretenez)\s+(?:sur\s+)?(?:le|ton|votre)\s+feu\b|\b(?:tend|keep|watch|mind)\s+(?:over\s+)?the\s+fire\b", Options)),
        (Parole.AssiedsToi, new(@"\b(?:assieds|assois|asseyez|assoyez)\s+(?:toi|vous)\b|\bsit(?:\s+down)?\b|\bbe\s+seated\b", Options)),
        (Parole.Prie, new(@"(?<!\ben\s)(?<!\bt'en\s)(?<!\bvous\sen\s)\b(?:prie|priez)\b|\b(?:recueille\s+toi|recueillez\s+vous)\b|\bpray\b", Options)),
        (Parole.Dors, new(@"\b(?:dors|dormez|endors\s+toi|endormez\s+vous)\b|\b(?:go\s+to\s+)?sleep\b", Options)),
        (Parole.Mange, new(@"\b(?:mange|mangez)\b|\beat\b", Options)),
        (Parole.Bois, new(@"(?<!\b(?:du|le|de|des|les|en|au|ce|son|sa|ton|mon|un)\s)\bbois\b(?!\s+(?:sec|mort|vert|dur|flotté))|\bbuvez\b|\bdrink\b", Options)),
        (Parole.Chante, new(@"\b(?:chante|chantez)\b|\bsing\b", Options)),
        (Parole.CacheToi, new(@"\b(?:cache|cachez)\s+(?:toi|vous)\b|\bhide(?:\s+yourself)?\b", Options)),
        // « Enseigne le son sacré à ta communauté », « raconte aux tiens ce que je t'ai dit », « tell your people » (0.44.11).
        (Parole.Transmets, new(@"\b(?:enseigne|enseignez|apprends|apprenez|raconte|racontez|transmets|transmettez|répète|répétez|annonce|annoncez|dis|dites|porte|portez)\b[^.!?]*?\b(?:à|aux)\s+(?:ta|ton|tes|votre|vos|toute\s+ta|tous\s+les)\s+(?:communauté|tribu|clan|peuple|gens|famille|proches|frères|sœurs|siens|tiens)\b|\b(?:teach|tell|spread|pass\s+on|share)\b[^.!?]*?\b(?:to\s+)?(?:your|all\s+your)\s+(?:community|tribe|clan|people|family|kin)\b", Options)),
        (Parole.Jeune, new(@"\b(?:jeûne|jeûnez)\b|\bfast\s+(?:for|until|today|now)\b|\bdo\s+not\s+eat\b", Options)),
    ];

    private static readonly Regex Negation = new(@"\bne\s+\S+(?:\s+\S+)?\s+(?:pas|jamais|plus|point)\b|\bn'\S+\s+(?:pas|jamais|plus)\b|\b(?:don't|do\s+not|never|stop)\b", Options);

    /// <summary>Les gestes demandés dans <paramref name="texte"/>, dans l'ordre où ils sont dits, chacun une fois.</summary>
    public static List<Parole> Lire(string texte)
    {
        var trouves = new List<(int Position, Parole Parole)>();
        foreach (Match m in Regex.Matches(texte, @"[^.!?…\n]+[.!?…]*"))
        {
            var phrase = m.Value.Trim();
            if (phrase.Length == 0 || phrase.EndsWith('?')) continue;
            var t = Regex.Replace(phrase.ToLowerInvariant().Replace('’', '\'').Replace('-', ' '), @"\s+", " ");
            foreach (var (p, motif) in Motifs)
            {
                if (trouves.Exists(x => x.Parole == p)) continue;
                var k = motif.Match(t);
                if (!k.Success) continue;
                // « Ne prie pas », « don't eat » : une défense, pas un ordre (le jeûne « do not eat » est voulu).
                if (p != Parole.Jeune && Negation.Match(t) is { Success: true } n && n.Index < k.Index + k.Length) continue;
                // « Va chercher à manger » n'est pas aussi « mange ».
                if (p == Parole.Mange && trouves.Exists(x => x.Parole == Parole.VaChercherAManger)) continue;
                trouves.Add((m.Index + k.Index, p));
            }
        }
        return trouves.OrderBy(x => x.Position).Select(x => x.Parole).ToList();
    }
}
