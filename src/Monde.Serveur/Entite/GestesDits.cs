using System.Text.RegularExpressions;

namespace Monde.Serveur.Entite;

/// <summary>
/// Ce que l'humain dit faire de son corps en répondant à l'Entité (0.45.19) : « je ferme les yeux », « I kneel down ». Un filet sous
/// le champ « gestes » de sa réponse : vu le 18/11, Meisen répondait « je ferme les yeux » sans le dire dans « gestes », et ses yeux
/// restaient ouverts. Comme <see cref="GestesLus"/> : ni question, ni négation (« je ne fermerai pas les yeux »).
/// </summary>
public static class GestesDits
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Mon = @"(?:les|mes|ses|ton|tes|mon|son|my|his|her|their|the)";

    /// <summary>Chaque geste et ses tournures, en français et en anglais (texte en minuscules, traits d'union en espaces).</summary>
    private static readonly (string Geste, Regex Motif)[] Motifs =
    [
        ("CacherYeux", new($@"\b(?:cach|couvr|couvre)\w*\s+{Mon}\s+yeux\b|\bmains?\s+sur\s+{Mon}\s+yeux\b|\b(?:cover|hid|hide)\w*\s+{Mon}\s+eyes\b|\bhands?\s+over\s+{Mon}\s+eyes\b", Options)),
        ("OuvrirYeux", new($@"\b(?:r?ouvr|r?ouvre)\w*\s+{Mon}\s+yeux\b|\bopen\w*\s+{Mon}\s+eyes\b", Options)),
        ("FermerYeux", new($@"\bferm\w*\s+{Mon}\s+yeux\b|\byeux\s+fermés\b|\b(?:clos|shut)\w*\s+{Mon}\s+eyes\b|\beyes\s+(?:closed|shut)\b", Options)),
        ("Couvrir", new($@"\b(?:cach|couvr|couvre)\w*\s+(?:le|mon|son)\s+visage\b|\bvisage\s+dans\s+{Mon}\s+mains\b|\b(?:cover|hid|hide|bur)\w*\s+{Mon}\s+face\b|\bface\s+in\s+{Mon}\s+hands\b", Options)),
        ("RegarderCiel", new($@"\bregard\w*\s+(?:vers\s+le|le|au)\s+ciel\b|\bl[eè]v\w*\s+{Mon}\s+yeux\s+(?:vers\s+le|au)\s+ciel\b|\bl[eè]v\w*\s+{Mon}\s+yeux\b|\blook\w*\s+up\b|\b(?:at|to|towards?)\s+the\s+sky\b|\brais\w*\s+{Mon}\s+eyes\b", Options)),
        ("RegarderSol", new($@"\bbaiss\w*\s+(?:les\s+yeux|la\s+tête|le\s+regard|{Mon}\s+yeux)\b|\bregard\w*\s+(?:le\s+sol|par\s+terre|à\s+terre)\b|\blook\w*\s+down\b|\blower\w*\s+{Mon}\s+(?:eyes|head|gaze)\b|\b(?:at|to)\s+the\s+ground\b", Options)),
        ("Sourire", new(@"\bsouri(?:s|t|re|ant|ons|ez)\b|\bsmil(?:e|es|ed|ing)\b", Options)),
        ("Crier", new(@"\b(?:crie|cries|crient|crier|criant|hurle|hurles|hurlent|hurler)\b|\b(?:scream|shout|yell)\w*\b|\bcr(?:y|ies|ied)\s+out\b", Options)),
        ("Pleurer", new(@"\bpleur\w*|\blarmes\b|\b(?:weep|wept|sob)\w*|\bcry(?:ing)?\b(?!\s+out)|\btears\b", Options)),
        ("Prosterne", new($@"\bprostern\w*|\bfront\s+(?:contre\s+le|au|sur\s+le)\s+sol\b|\bprostrat\w*|\bforehead\s+(?:to|on|against)\s+the\s+ground\b", Options)),
        ("Genoux", new(@"\b(?:m'|s'|t')?agenouill\w*|\bà\s+genoux?\b|\bmets?\s+à\s+genoux?\b|\bknee?l\w*|\bknelt\b|\bon\s+(?:my|his|her|your)\s+knees\b", Options)),
        ("Recroqueviller", new($@"\brecroquevill\w*|\ben\s+boule\b|\bgenoux\s+contre\s+(?:la|ma|sa)\s+poitrine\b|\bcurl\w*\s+up\b|\bhuddl\w*", Options)),
        ("CoucherVentre", new($@"\bsur\s+le\s+ventre\b|\bface\s+contre\s+(?:terre|le\s+sol)\b|\bon\s+{Mon}\s+(?:stomach|belly)\b|\bface\s+down\b", Options)),
        ("CoucherDos", new($@"\b(?:m'|s'|t')allong\w*|\b(?:me|se|te)\s+couch\w*|\bsur\s+le\s+dos\b|\b(?:lie|lay|lying)\s+down\b|\bon\s+{Mon}\s+back\b", Options)),
        ("Asseoir", new(@"\b(?:m'|s'|t')ass(?:ie|oi|ey|eoi)\w*|\bassise?\b|\bsit(?:s|ting)?(?:\s+down)?\b|\bsat\b", Options)),
        ("Debout", new(@"\b(?:me|se|te)\s+(?:re)?l[eè]v\w*\b|\b(?:re)?l[eè]ve\s+toi\b|\b(?:re)?levez\s+vous\b|\bdebout\b|\b(?:stand|stood|get|got|rise|rose)\w*\s+up\b", Options)),
        ("Sauter", new(@"\bsaut\w*|\bbondi\w*|\bbond(?:s|it)?\b|\bjump\w*|\bleap\w*|\bhop(?:s|ped|ping)?\b", Options)),
        ("TournerSurSoi", new(@"\btourn\w*\s+sur\s+(?:moi|lui|elle|soi|toi)(?:\s*même)?\b|\bpivot\w*|\bspin\w*|\bspun\b|\btwirl\w*", Options)),
        ("Danser", new(@"\bdans(?:e|es|ent|er|ons|ez|ant|ait)\b|\bdanc\w*", Options)),
        ("LeverBras", new($@"\b(?:é|e)?l[eè]v\w*\s+{Mon}\s+(?:deux\s+)?(?:bras|mains)\b|\b(?:les|leur)\s+(?:é|e)?l[eè]v\w*\s+vers\s+le\s+ciel\b|\b(?:é|e)l[eè]v\w*\s+{Mon}\s+(?:deux\s+)?(?:bras|mains)\b|\bbras\s+(?:levés|au\s+ciel|en\s+l'air)\b|\brais\w*\s+(?:{Mon}|both)\s+(?:arms|hands)\b|\barms\s+(?:raised|up)\b", Options)),
        ("Tendre", new($@"\btend\w*\s+(?:la|ma|sa|{Mon})\s+mains?\b|\breach\w*\s+out\b|\b(?:hold|held|stretch)\w*\s+out\s+{Mon}\s+hands?\b", Options)),
        ("Montrer", new(@"\b(?:montr|désign|pointe)\w*\s+du\s+doigt\b|\bpoint\w*\s+(?:at|to|towards?)\b", Options)),
        ("FrapperSol", new(@"\b(?:frapp|tap)\w*\s+(?:le\s+sol|du\s+pied)\b|\btrépign\w*|\bstamp\w*|\bstomp\w*", Options)),
        ("TournerDos", new($@"\btourn\w*\s+le\s+dos\b|\bdos\s+tourné\b|\bturn\w*\s+{Mon}\s+back\b", Options)),
        ("Hausser", new(@"\bhauss\w*\s+(?:les\s+)?épaules\b|\bshrug\w*", Options)),
        ("Refuser", new($@"\bcrois\w*\s+{Mon}\s+bras\b|\bsecou\w*\s+la\s+tête\b|\bcross\w*\s+{Mon}\s+arms\b|\bshak\w*\s+{Mon}\s+head\b", Options)),
        ("Reculer", new(@"\brecul\w*|\bpas\s+en\s+arrière\b|\bstep\w*\s+back\b|\bback\w*\s+away\b|\bretreat\w*", Options)),
        ("CourirVers", new(@"\bcour(?:s|t|ir|ons|ez|ent|ant)\s+(?:vers|jusqu'à)\s+(?:toi|vous|la\s+lumière|elle)\b|\b(?:run|ran|rush)\w*\s+(?:towards?|to)\s+(?:you|the\s+light)\b", Options)),
        ("Fuir", new(@"\bfu(?:is|it|ir|yons|yez|ient|yant)\b|\b(?:m'|s'|t')enfu\w*|\b(?:flee|fled|escap)\w*|\b(?:run|ran)\w*\s+away\b", Options)),
        ("MarcherVers", new(@"\b(?:march|avanc|approch|vien|vais|va)\w*\s+(?:vers|jusqu'à)\s+(?:toi|vous|la\s+lumière|elle)\b|\b(?:m'|s')approch\w*|\bj'avance\b|\b(?:walk|come|came|go|step|mov)\w*\s+(?:towards?|closer|up\s+to)\b|\bapproach\w*", Options)),
        ("TournerAutour", new(@"\btourn\w*\s+autour\b|\bfai\w*\s+le\s+tour\b|\bcircl\w*|\bwalk\w*\s+(?:a)?round\s+(?:you|the\s+light|it)\b", Options)),
        ("Eloigner", new(@"\b(?:m'|s'|t')éloign\w*|\bm'en\s+vais\b|\bs'en\s+va\b|\b(?:walk|go|went|mov)\w*\s+away\b", Options)),
        ("Rejoindre", new(@"\brejoin\w*|\bjoin\w*\s+(?:the\s+others|my|his|her)\b|\bgo\w*\s+(?:back\s+)?to\s+(?:the\s+others|my\s+(?:mother|father|people|family))\b", Options)),
        ("Revenir", new(@"\brevien\w*|\breviendr\w*|\bje\s+retourne\s+(?:à\s+ma\s+place|où\s+j'étais)\b|\bcome\s+back\b|\bcame\s+back\b", Options)),
    ];

    /// <summary>Gestes qui en excluent un autre trouvé dans la même phrase (le plus précis gagne).</summary>
    private static readonly (string Garde, string Ote)[] Exclus =
    [
        ("CacherYeux", "FermerYeux"), ("CoucherVentre", "CoucherDos"), ("Recroqueviller", "CoucherDos"), ("Recroqueviller", "Genoux"),
        ("CourirVers", "MarcherVers"), ("Prosterne", "Genoux"), ("Crier", "Pleurer"), ("RegarderCiel", "LeverBras"),
    ];

    private static readonly Regex Negation = new(@"\bne\s+\S+(?:\s+\S+)?\s+(?:pas|jamais|plus|point)\b|\bn'\S+\s+(?:pas|jamais|plus)\b|\b(?:don't|do\s+not|won't|will\s+not|can't|cannot|never|not)\b", Options);

    /// <summary>Les gestes que <paramref name="texte"/> dit faire, dans l'ordre, chacun une fois, au plus <paramref name="max"/>.</summary>
    public static List<string> Lire(string texte, int max = 3)
    {
        var trouves = new List<(int Position, string Geste)>();
        foreach (Match m in Regex.Matches(texte, @"[^.!?…\n]+[.!?…]*"))
        {
            var phrase = m.Value.Trim();
            if (phrase.Length == 0 || phrase.EndsWith('?')) continue;
            var t = Regex.Replace(phrase.ToLowerInvariant().Replace('’', '\'').Replace('-', ' '), @"\s+", " ");
            var ici = new List<(int Position, string Geste)>();
            foreach (var (g, motif) in Motifs)
            {
                if (trouves.Exists(x => x.Geste == g)) continue;
                var k = motif.Match(t);
                if (!k.Success) continue;
                // « Je ne fermerai pas les yeux » : une négation avant le geste l'annule.
                if (Negation.Match(t) is { Success: true } n && n.Index < k.Index + k.Length && n.Index > k.Index - 25) continue;
                ici.Add((m.Index + k.Index, g));
            }
            ici.RemoveAll(x => Exclus.Any(e => e.Ote == x.Geste && ici.Exists(y => y.Geste == e.Garde)));
            trouves.AddRange(ici);
        }
        return trouves.OrderBy(x => x.Position).Select(x => x.Geste).Distinct().Take(max).ToList();
    }
}
