using System.Text;
using Lumina.Text.Payloads;

namespace Sinophone.Data;

/// <summary>
/// Splits a SeString into the text of each conditional branch.
/// </summary>
/// <remarks>
/// Lumina's <c>ExtractText</c> skips <c>If</c> macros entirely, branches included.
/// A row such as <c>&lt;If(gender)&gt;Sir&lt;Else/&gt;Madam&lt;/If&gt;, hello.</c> became
/// <c>, hello.</c>, which never matched the line the game showed. Each branch is
/// returned as its own string so every possible line gets a key.
/// </remarks>
public static class SeStringVariants
{
    /// <summary>Guard against a pathological row with many nested conditionals.</summary>
    private const int MaxVariants = 16;

    /// <summary>Returns one string per possible branch combination.</summary>
    public static IReadOnlyList<string> Extract(ReadOnlySeString value)
    {
        var span = value.AsSpan();

        return HasConditional(span)
            ? ExtractBranches(span)
            : new[] { value.ExtractText() };
    }

    private static bool HasConditional(ReadOnlySeStringSpan span)
    {
        foreach (var payload in span)
        {
            if (payload.Type == ReadOnlySePayloadType.Macro && payload.MacroCode == MacroCode.If)
                return true;
        }

        return false;
    }

    private static List<string> ExtractBranches(ReadOnlySeStringSpan span)
    {
        var variants = new List<string> { string.Empty };

        foreach (var payload in span)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                AppendText(variants, Encoding.UTF8.GetString(payload.Body));
                continue;
            }

            if (payload.Type != ReadOnlySePayloadType.Macro)
                continue;

            if (payload.MacroCode == MacroCode.If)
            {
                var branches = ExtractIfBranches(payload);
                if (branches is not null)
                {
                    variants = Combine(variants, branches);
                    continue;
                }
            }

            AppendText(variants, MacroText(payload.MacroCode));
        }

        return variants;
    }

    private static List<string>? ExtractIfBranches(ReadOnlySePayloadSpan payload)
    {
        if (payload.TryGetExpression(out _, out var onTrue, out var onFalse))
        {
            if (onTrue.TryGetString(out var trueText) && onFalse.TryGetString(out var falseText))
            {
                var branches = new List<string>(ExtractBranches(trueText));
                branches.AddRange(ExtractBranches(falseText));
                return branches;
            }

            return null;
        }

        // An If with no false branch is still a valid payload.
        if (payload.TryGetExpression(out _, out var onlyTrue) && onlyTrue.TryGetString(out var text))
            return ExtractBranches(text);

        return null;
    }

    private static List<string> Combine(List<string> prefixes, List<string> branches)
    {
        var result = new List<string>(Math.Min(prefixes.Count * branches.Count, MaxVariants));

        foreach (var prefix in prefixes)
        {
            foreach (var branch in branches)
            {
                if (result.Count >= MaxVariants)
                    return result;

                result.Add(prefix + branch);
            }
        }

        return result;
    }

    private static void AppendText(List<string> variants, string text)
    {
        if (text.Length == 0)
            return;

        for (var i = 0; i < variants.Count; i++)
            variants[i] += text;
    }

    private static string MacroText(MacroCode code)
        => code switch
        {
            MacroCode.NewLine => "\n",
            MacroCode.NonBreakingSpace => "\u00A0",
            MacroCode.Hyphen => "-",
            _ => string.Empty,
        };
}
