using System.Text;

namespace Sinophone.Data;

/// <summary>
/// Collapses time-of-day words into one token so a line written for one time of
/// day still matches the one the game shows at another.
/// </summary>
/// <remarks>
/// The English dump stores one branch of a time conditional. Mytesyn greets the
/// player with "this fine day" in the game and "this fine evenin'" in the dump,
/// so the noun matches but the time word does not.
/// <para>
/// A collapsed key that two lines share with different Chinese is dropped by
/// <see cref="TranslationStore"/>, so these words cannot answer a line the dump
/// wrote for a different time on purpose. The collapsed key is only tried after
/// an exact miss.
/// </para>
/// </remarks>
public static class TimeOfDayWords
{
    /// <summary>
    /// Stands in for every time-of-day word. A unit separator never appears in
    /// normalized game text, so a collapsed key cannot collide with a real one.
    /// </summary>
    private const char Token = '\u001F';

    /// <summary>
    /// Replaces time-of-day words with a single token.
    /// </summary>
    /// <returns>
    /// The same string instance when the text has no time-of-day word, so a lookup
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

            if (!IsTimeOfDay(key.AsSpan(start, index - start)))
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
    /// True for a time of day, with or without a contraction: "morning's" and
    /// "morning" both collapse to the token.
    /// </summary>
    private static bool IsTimeOfDay(ReadOnlySpan<char> word)
    {
        var apostrophe = word.IndexOf('\'');
        var stem = apostrophe < 0 ? word : word[..apostrophe];

        return stem.Equals("morning", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("mornin", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("afternoon", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("evening", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("evenin", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("noon", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("day", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("night", StringComparison.OrdinalIgnoreCase);
    }
}
