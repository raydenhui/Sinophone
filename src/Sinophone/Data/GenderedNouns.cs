using System.Text;

namespace Sinophone.Data;

/// <summary>
/// Collapses gendered nouns of address into one token so the male and female form
/// of a line produce the same lookup key.
/// </summary>
/// <remarks>
/// The English dump stores one gender branch. A warp inn greeter writes "Greetin's,
/// lass" for a female player, and the game shows "Greetin's, lad" to a male one.
/// Pronouns alone do not cover that, because the words around them are the same.
/// <para>
/// Only nouns used to address the player are listed. Roles such as father and
/// mother are not, because a line about one is not a line about the other. The
/// collapsed key is only tried after an exact miss.
/// </para>
/// </remarks>
public static class GenderedNouns
{
    /// <summary>
    /// Stands in for every gendered noun. A unit separator never appears in
    /// normalized game text, so a collapsed key cannot collide with a real one.
    /// </summary>
    private const char Token = '\u001F';

    /// <summary>
    /// Replaces gendered nouns with a single token.
    /// </summary>
    /// <returns>
    /// The same string instance when the text has no gendered noun, so a lookup
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

            if (!IsGenderedNoun(key.AsSpan(start, index - start)))
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
    /// True for a noun of address, with or without a contraction: "lad's" and
    /// "lad" both collapse to the token.
    /// </summary>
    private static bool IsGenderedNoun(ReadOnlySpan<char> word)
    {
        var apostrophe = word.IndexOf('\'');
        var stem = apostrophe < 0 ? word : word[..apostrophe];

        return stem.Equals("lad", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("lass", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("sir", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("madam", StringComparison.OrdinalIgnoreCase);
    }
}
