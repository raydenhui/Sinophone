using System.Text;
using System.Text.RegularExpressions;

namespace Sinophone.Data;

/// <summary>
/// Removes SeString macros and invisible characters so English text read from
/// the game can be matched against English text read from Lumina.
/// </summary>
public static partial class MacroNormalizer
{
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakPattern();

    [GeneratedRegex(@"<[^<>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex MacroPattern();

    /// <summary>
    /// Normalizes text for use as a dictionary key. All whitespace, including
    /// line breaks, is collapsed to a single space.
    /// </summary>
    public static string NormalizeKey(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cleaned = StripInvisible(text);

        // Turn <br> into a space before the generic macro strip, which would
        // otherwise glue the words on either side together.
        cleaned = LineBreakPattern().Replace(cleaned, " ");
        cleaned = MacroPattern().Replace(cleaned, string.Empty);

        var builder = new StringBuilder(cleaned.Length);
        var previousWasWhitespace = false;

        foreach (var ch in cleaned)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0 && !previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Prepares Chinese text for display: macros are stripped, line breaks are
    /// preserved, and trailing whitespace on each line is trimmed.
    /// </summary>
    public static string CleanForDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cleaned = StripInvisible(text);
        cleaned = cleaned.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        cleaned = LineBreakPattern().Replace(cleaned, "\n");
        cleaned = MacroPattern().Replace(cleaned, string.Empty);

        var lines = cleaned.Split('\n');
        var builder = new StringBuilder(cleaned.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.Append('\n');

            builder.Append(lines[i].TrimEnd());
        }

        return builder.ToString().Trim();
    }

    private static string StripInvisible(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '­': // soft hyphen
                case '​': // zero width space
                case '‌': // zero width non-joiner
                case '‍': // zero width joiner
                case '﻿': // zero width no-break space / BOM
                case '�': // replacement character from malformed UTF-8
                    continue;
                case ' ': // non-breaking space
                    builder.Append(' ');
                    break;
                case '\t': // whitespace stays so key collapsing and display
                case '\n': // line breaks keep working
                case '\r':
                    builder.Append(ch);
                    break;
                default:
                    // The Talk addon wraps oblique words in raw SeString control
                    // bytes (U+0002, U+0003, etc.). The sheet text has no such
                    // markers, so drop every other control character.
                    if (char.IsControl(ch))
                        continue;

                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }
}
