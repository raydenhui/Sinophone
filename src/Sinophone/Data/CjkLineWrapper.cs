namespace Sinophone.Data;

/// <summary>
/// Wraps Chinese text into lines that fit a width, with the punctuation rules the game's own
/// wrapping does not apply.
/// </summary>
/// <remarks>
/// The Chinese data is already broken for the Chinese client's text box. The English client's box
/// is a little narrower with the CJK font, so the game breaks the line again at the last character
/// and a closing mark such as the full stop starts the next line. Wrapping the line before the game
/// does keeps each line inside the box, and the rules keep a closing mark off a line start and an
/// opening mark off a line end.
/// </remarks>
internal static class CjkLineWrapper
{
    /// <summary>Marks that must not start a line.</summary>
    private const string NoLineStart = "。，、．；：？！）］｝」』】〉》”’…";

    /// <summary>Marks that must not end a line.</summary>
    private const string NoLineEnd = "（［｛「『【〈《“‘";

    /// <summary>
    /// Wraps <paramref name="text"/> so no line is wider than <paramref name="maxWidth"/>.
    /// Existing line breaks are kept as paragraph breaks.
    /// </summary>
    /// <param name="text">Text to wrap.</param>
    /// <param name="maxWidth">Widest line allowed, in pixels. Zero or less returns the text as is.</param>
    /// <param name="measureWidth">Measures one candidate line, in pixels.</param>
    public static string Wrap(string? text, int maxWidth, Func<string, int> measureWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
            return text ?? string.Empty;

        var paragraphs = text.Split('\n');
        var builder = new System.Text.StringBuilder(text.Length + paragraphs.Length);

        for (var index = 0; index < paragraphs.Length; index++)
        {
            if (index > 0)
                builder.Append('\n');

            WrapParagraph(builder, paragraphs[index], maxWidth, measureWidth);
        }

        return builder.ToString();
    }

    private static void WrapParagraph(
        System.Text.StringBuilder builder,
        string paragraph,
        int maxWidth,
        Func<string, int> measureWidth)
    {
        var start = 0;

        while (start < paragraph.Length)
        {
            var remaining = paragraph[start..];

            if (measureWidth(remaining) <= maxWidth)
            {
                builder.Append(remaining);
                return;
            }

            // Longest prefix that fits. Binary search keeps the number of game measurements
            // small: a line of 50 characters costs about six.
            var low = 1;
            var high = remaining.Length - 1;
            var fit = 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (measureWidth(remaining[..middle]) <= maxWidth)
                {
                    fit = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (fit < remaining.Length && NoLineStart.Contains(remaining[fit]))
            {
                // A closing mark must not start the next line. Move the break left over the
                // whole run of closing marks, so "」。", "……", and "」！" all stay together with
                // the character they belong to. One character is not enough: the character
                // before a full stop is often another closing mark.
                while (fit > 1 && fit < remaining.Length && NoLineStart.Contains(remaining[fit]))
                    fit--;
            }
            else if (NoLineEnd.Contains(remaining[fit - 1]) && fit < remaining.Length)
            {
                // An opening mark must not end the line, so take the next character with it.
                while (fit < remaining.Length && NoLineEnd.Contains(remaining[fit - 1]))
                    fit++;
            }

            fit = Math.Clamp(fit, 1, remaining.Length);

            builder.Append(remaining, 0, fit);

            if (start + fit < paragraph.Length)
                builder.Append('\n');

            start += fit;
        }
    }
}
