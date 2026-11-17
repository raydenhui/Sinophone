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
        var lines = new List<string>(4);
        var start = 0;

        while (start < paragraph.Length)
        {
            var remaining = paragraph[start..];

            if (measureWidth(remaining) <= maxWidth)
            {
                lines.Add(remaining);
                break;
            }

            var fit = LongestFittingPrefix(remaining, maxWidth, measureWidth);
            fit = KeepMarksWithTheirCharacter(remaining, fit);

            lines.Add(remaining[..fit]);
            start += fit;
        }

        BalanceLastLines(lines, maxWidth, measureWidth);

        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
                builder.Append('\n');

            builder.Append(lines[index]);
        }
    }

    /// <summary>
    /// Longest prefix of <paramref name="text"/> that fits. Binary search keeps the number of
    /// game measurements small: a line of 50 characters costs about six.
    /// </summary>
    private static int LongestFittingPrefix(string text, int maxWidth, Func<string, int> measureWidth)
    {
        var low = 1;
        var high = text.Length - 1;
        var fit = 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (measureWidth(text[..middle]) <= maxWidth)
            {
                fit = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return fit;
    }

    /// <summary>
    /// Moves a break so no line starts with a closing mark and none ends with an opening mark.
    /// The break moves over the whole run of marks, because "」。", "……" and "」！" are as bad as
    /// a lone full stop.
    /// </summary>
    private static int KeepMarksWithTheirCharacter(string text, int fit)
    {
        if (fit < text.Length && NoLineStart.Contains(text[fit]))
        {
            while (fit > 1 && fit < text.Length && NoLineStart.Contains(text[fit]))
                fit--;
        }
        else if (NoLineEnd.Contains(text[fit - 1]) && fit < text.Length)
        {
            while (fit < text.Length && NoLineEnd.Contains(text[fit - 1]))
                fit++;
        }

        return Math.Clamp(fit, 1, text.Length);
    }

    /// <summary>
    /// Re-splits the last two lines when the last one is much shorter than the width. A line that
    /// keeps one character and its mark looks like a mistake, so the two lines are split near
    /// their middle instead, as long as both still fit.
    /// </summary>
    private static void BalanceLastLines(List<string> lines, int maxWidth, Func<string, int> measureWidth)
    {
        if (lines.Count < 2)
            return;

        // A last line at a quarter of the width or more reads as intentional.
        if (measureWidth(lines[^1]) * 4 >= maxWidth)
            return;

        var combined = lines[^2] + lines[^1];
        var best = 0;
        var bestDifference = int.MaxValue;

        for (var cut = 1; cut < combined.Length; cut++)
        {
            if (NoLineStart.Contains(combined[cut]) || NoLineEnd.Contains(combined[cut - 1]))
                continue;

            var first = measureWidth(combined[..cut]);
            if (first > maxWidth)
                break;

            var second = measureWidth(combined[cut..]);
            if (second > maxWidth)
                continue;

            var difference = Math.Abs(first - second);
            if (difference >= bestDifference)
                continue;

            best = cut;
            bestDifference = difference;
        }

        if (best == 0)
            return;

        lines[^2] = combined[..best];
        lines[^1] = combined[best..];
    }
}
