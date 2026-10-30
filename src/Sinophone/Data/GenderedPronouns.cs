using System.Text;

namespace Sinophone.Data;

/// <summary>
/// Collapses gendered pronouns into one token so the male and female form of a line
/// produce the same lookup key.
/// </summary>
/// <remarks>
/// Script lines come from the English dump, which resolves a gender conditional to
/// one branch before the plugin sees it. A line written for a female player says
/// "her body"; the game shows "his body" to a male player, so an exact match misses.
/// Replacing every gendered pronoun with one token makes both forms agree.
/// <para>
/// Roles are not kept: "his" and "him" both become the token, which is what lets
/// "on her spirit" match "on his spirit". The collapsed key is only tried after an
/// exact miss, so it can never take a line that already matches.
/// </para>
/// </remarks>
public static class GenderedPronouns
{
    /// <summary>
    /// Stands in for every gendered pronoun. A unit separator never appears in
    /// normalized game text, so a collapsed key cannot collide with a real one.
    /// </summary>
    private const char Token = '\u001F';

    /// <summary>
    /// Replaces gendered pronouns with a single token.
    /// </summary>
    /// <returns>
    /// The same string instance when the text has no gendered pronoun, so a lookup
    /// that cannot need this does not pay for a copy.
    /// </returns>
    public static string Neutralize(string key)
    {
        if (key.Length == 0)
            return key;

        StringBuilder? builder = null;
        var copied = 0;
        var index = 0;

        while (index < key.Length)
        {
            if (!IsWordChar(key[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < key.Length && IsWordChar(key[index]))
                index++;

            if (!IsGenderedPronoun(key.AsSpan(start, index - start)))
                continue;

            builder ??= new StringBuilder(key.Length);
            builder.Append(key, copied, start - copied);
            builder.Append(Token);
            copied = index;
        }

        if (builder is null)
            return key;

        builder.Append(key, copied, key.Length - copied);
        return builder.ToString();
    }

    private static bool IsWordChar(char value) => char.IsLetter(value) || value == '\'';

    /// <summary>
    /// True for a pronoun, with or without a contraction: "she's" and "he's" both
    /// collapse to the token.
    /// </summary>
    private static bool IsGenderedPronoun(ReadOnlySpan<char> word)
    {
        var apostrophe = word.IndexOf('\'');
        var stem = apostrophe < 0 ? word : word[..apostrophe];

        return stem.Equals("he", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("him", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("his", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("himself", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("she", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("her", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("hers", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("herself", StringComparison.OrdinalIgnoreCase);
    }
}
