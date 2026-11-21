using System.Text.RegularExpressions;

namespace Monde.Simulation.Humains;

/// <summary>
/// L'ordre que contient une révélation (0.45.21) : « Allez à la rencontre de Meisen », « Go to Meisen »… Une révélation est un
/// savoir ; quand ses mots disent aussi d'aller vers une personne vivante, ceux qui y croient l'entendent aussi comme cet ordre
/// (voir <see cref="Parole.VaVers"/>). Seul cet ordre est reconnu : les autres passent par « Parler au Monde ».
/// </summary>
public static partial class OrdresReveles
{
    // « allez / va / partez / rendez-vous … vers / voir / chez / rejoindre / retrouver / à la rencontre de / auprès de NOM »,
    // « go / walk / travel … to / see / meet / find / join NOM ». Le nom suit de près le verbe, dans la même phrase.
    [GeneratedRegex(@"\b(?:allez|aller|va|vas|partez|rendez-vous|marchez|rejoignez|retrouvez)\b[^.!?;\n]{0,40}?(?:\bvers|\bvoir|\bchez|\brejoindre|\bretrouver|\brencontrer|\bà la rencontre (?:de|d['’])|\bauprès (?:de|d['’])|\brejoignez|\bretrouvez)\s*(?<nom>\p{L}[\p{L}'’-]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Francais();

    [GeneratedRegex(@"\b(?:go|walk|travel|journey|head)\b[^.!?;\n]{0,40}?\b(?:to|see|meet|find|join|towards?)\s+(?<nom>\p{L}[\p{L}'’-]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Anglais();

    /// <summary>
    /// La personne vivante vers qui la révélation dit d'aller ; null s'il n'y en a pas. Si plusieurs vivants portent ce nom, celui
    /// de <paramref name="communaute"/> d'abord, sinon le premier né.
    /// </summary>
    public static Humain? VaVers(string texte, IReadOnlyList<Humain> vivants, int communaute = 0)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        foreach (var m in Francais().Matches(texte).Concat(Anglais().Matches(texte)))
        {
            var nom = m.Groups["nom"].Value.Trim('\'', '’', '-');
            var memes = vivants.Where(h => h.EstVivant && string.Equals(h.Nom, nom, StringComparison.OrdinalIgnoreCase)).ToList();
            if (memes.Count == 0) continue;
            return memes.FirstOrDefault(h => communaute > 0 && h.CommunauteId == communaute) ?? memes.MinBy(h => h.Id);
        }
        return null;
    }
}
