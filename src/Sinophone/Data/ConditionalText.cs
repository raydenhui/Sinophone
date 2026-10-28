using System.Text;

namespace Sinophone.Data;

/// <summary>
/// Resolves the conditionals that the CN and TC dumps keep as literal text, for
/// example <c>&lt;If(PlayerParameter(4))&gt;大姐&lt;Else/&gt;小哥&lt;/If&gt;</c>.
/// </summary>
/// <remarks>
/// Only <c>If</c> and <c>Switch</c> are resolved here. Other macros are copied and
/// left for <see cref="MacroNormalizer"/> to strip. A condition the caller does not
/// know takes the true branch, which is better than showing both branches at once.
/// </remarks>
public static class ConditionalText
{
    private const int MaxDepth = 32;

    /// <summary>True when the text contains an <c>If</c> or <c>Switch</c> tag.</summary>
    public static bool HasConditional(string? text)
        => !string.IsNullOrEmpty(text)
           && (text.Contains("<If(", StringComparison.OrdinalIgnoreCase)
               || text.Contains("<Switch(", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the text with every conditional replaced by the branch to show.
    /// <paramref name="evaluate"/> returns true or false for a condition, or null
    /// when it does not know it.
    /// </summary>
    public static string Resolve(string? text, Func<string, bool?>? evaluate)
    {
        if (string.IsNullOrEmpty(text) || !HasConditional(text))
            return text ?? string.Empty;

        var output = new StringBuilder(text.Length);
        ResolveRange(text, 0, text.Length, output, evaluate, 0);
        return output.ToString();
    }

    private static void ResolveRange(
        string text,
        int start,
        int end,
        StringBuilder output,
        Func<string, bool?>? evaluate,
        int depth)
    {
        if (depth > MaxDepth)
        {
            output.Append(text, start, end - start);
            return;
        }

        var index = start;

        while (index < end)
        {
            var open = text.IndexOf('<', index);
            if (open < 0 || open >= end)
            {
                output.Append(text, index, end - index);
                return;
            }

            output.Append(text, index, open - index);

            var tagEnd = text.IndexOf('>', open);
            if (tagEnd < 0 || tagEnd >= end)
            {
                output.Append(text, open, end - open);
                return;
            }

            if (IsTag(text, open, end, "<If("))
            {
                if (!TryFindIf(text, tagEnd + 1, end, out var elseStart, out var elseEnd, out var contentEnd, out var afterIf))
                {
                    // Malformed tag. Keep it, and the normalizer will strip it later.
                    output.Append(text, open, tagEnd - open + 1);
                    index = tagEnd + 1;
                    continue;
                }

                var conditionStart = open + 4;
                var conditionEnd = tagEnd;
                if (conditionEnd > conditionStart && text[conditionEnd - 1] == ')')
                    conditionEnd--;

                var condition = text[conditionStart..conditionEnd].Trim();
                var takeTrue = evaluate?.Invoke(condition) ?? true;

                if (takeTrue)
                {
                    ResolveRange(text, tagEnd + 1, elseStart >= 0 ? elseStart : contentEnd, output, evaluate, depth + 1);
                }
                else if (elseStart >= 0)
                {
                    ResolveRange(text, elseEnd, contentEnd, output, evaluate, depth + 1);
                }

                index = afterIf;
                continue;
            }

            if (IsTag(text, open, end, "<Switch("))
            {
                if (!TryFindSwitch(text, tagEnd + 1, end, out var switchEnd, out var afterSwitch))
                {
                    output.Append(text, open, tagEnd - open + 1);
                    index = tagEnd + 1;
                    continue;
                }

                if (TryFindFirstCase(text, tagEnd + 1, switchEnd, out var caseStart, out var caseEnd))
                    ResolveRange(text, caseStart, caseEnd, output, evaluate, depth + 1);

                index = afterSwitch;
                continue;
            }

            output.Append(text, open, tagEnd - open + 1);
            index = tagEnd + 1;
        }
    }

    /// <summary>
    /// Finds the first top-level <c>Else</c> and the matching <c>EndIf</c>.
    /// </summary>
    private static bool TryFindIf(
        string text,
        int from,
        int end,
        out int elseStart,
        out int elseEnd,
        out int contentEnd,
        out int afterIf)
    {
        elseStart = elseEnd = contentEnd = afterIf = -1;
        var depth = 1;

        for (var i = from; i < end; i++)
        {
            if (text[i] != '<')
                continue;

            if (IsTag(text, i, end, "<If("))
            {
                depth++;
                continue;
            }

            if (IsTag(text, i, end, "</If"))
            {
                depth--;
                if (depth != 0)
                    continue;

                contentEnd = i;
                var close = text.IndexOf('>', i);
                if (close < 0 || close >= end)
                    return false;

                afterIf = close + 1;
                return true;
            }

            if (depth == 1 && elseStart < 0 && IsTag(text, i, end, "<Else"))
            {
                elseStart = i;
                var close = text.IndexOf('>', i);
                if (close < 0 || close >= end)
                    return false;

                elseEnd = close + 1;
            }
        }

        return false;
    }

    private static bool TryFindSwitch(string text, int from, int end, out int switchEnd, out int afterSwitch)
    {
        switchEnd = afterSwitch = -1;
        var depth = 1;

        for (var i = from; i < end; i++)
        {
            if (text[i] != '<')
                continue;

            if (IsTag(text, i, end, "<Switch("))
            {
                depth++;
                continue;
            }

            if (!IsTag(text, i, end, "</Switch") || --depth != 0)
                continue;

            switchEnd = i;
            var close = text.IndexOf('>', i);
            if (close < 0 || close >= end)
                return false;

            afterSwitch = close + 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Picks the first case of a switch. The dumps use this for values the plugin
    /// cannot know, such as a Grand Company rank.
    /// </summary>
    private static bool TryFindFirstCase(string text, int from, int end, out int caseStart, out int caseEnd)
    {
        caseStart = caseEnd = -1;

        var index = from;
        while (index < end)
        {
            var open = text.IndexOf('<', index);
            if (open < 0 || open >= end)
                return false;

            if (!IsTag(text, open, end, "<Case("))
            {
                index = open + 1;
                continue;
            }

            var tagEnd = text.IndexOf('>', open);
            if (tagEnd < 0 || tagEnd >= end)
                return false;

            var depth = 1;
            for (var i = tagEnd + 1; i < end; i++)
            {
                if (text[i] != '<')
                    continue;

                if (IsTag(text, i, end, "<Case("))
                {
                    depth++;
                    continue;
                }

                if (!IsTag(text, i, end, "</Case") || --depth != 0)
                    continue;

                caseStart = tagEnd + 1;
                caseEnd = i;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsTag(string text, int index, int end, string tag)
        => index + tag.Length <= end
           && text.AsSpan(index, tag.Length).StartsWith(tag, StringComparison.OrdinalIgnoreCase);
}
