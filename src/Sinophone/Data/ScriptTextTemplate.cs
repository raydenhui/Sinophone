using System.Text;
using Lumina.Text.Payloads;

namespace Sinophone.Data;

/// <summary>
/// Builds a match template from a script's raw SeString value. The game fills
/// dynamic spans such as the player name and Grand Company rank when it draws the
/// line; the dump keeps only the macros. A wildcard is placed only where the game
/// data says a span is dynamic, so the rest of the line still has to match exactly.
/// </summary>
public static partial class ScriptTextTemplate
{
    /// <summary>Private-use marker that cannot occur in normal dialogue.</summary>
    internal const char Wildcard = '\uE000';

    [GeneratedRegex(@"\s+([.,!?;:])", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"^\(-[^)]*-\)", RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerPrefix();

    [GeneratedRegex(@"\bPlayer(?:\s+Player)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerPlaceholder();

    public static string Build(ReadOnlySeStringSpan span)
    {
        var builder = new StringBuilder();

        foreach (var payload in span)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                builder.Append(Encoding.UTF8.GetString(payload.Body));
                continue;
            }

            if (payload.Type != ReadOnlySePayloadType.Macro)
                continue;

            switch (payload.MacroCode)
            {
                // The whole conditional can change between players and must be
                // allowed to become arbitrary text.
                case MacroCode.If:
                case MacroCode.Switch:
                case MacroCode.PcName:
                case MacroCode.IfPcName:
                case MacroCode.IfPcGender:
                case MacroCode.IfSelf:
                case MacroCode.Sheet:
                case MacroCode.SheetSub:
                    builder.Append(Wildcard);
                    break;

                case MacroCode.NewLine:
                case MacroCode.NonBreakingSpace:
                    builder.Append(' ');
                    break;

                case MacroCode.Hyphen:
                    builder.Append('-');
                    break;

                // Formatting macros do not add text. MacroNormalizer drops the rest
                // of these when it normalizes the template.
            }
        }

        // Reuse the same whitespace and invisible-character rules as a lookup key.
        var template = MacroNormalizer.NormalizeKey(builder.ToString());

        // Dynamic macros often leave a space before the punctuation that follows
        // them ("Greetings,󠀠 ."), while the game draws the name without that space.
        template = SpaceBeforePunctuation().Replace(template, "$1");

        // The script dump prefixes some lines with a speaker hint such as
        // "(-Voidsent Children-)". The game draws that name in the speaker node,
        // not in the dialogue text, so it must not be part of the template.
        template = SpeakerPrefix().Replace(template, string.Empty);

        // The dumps use "Player" or "Player Player" where the game inserts the
        // real character name. Treat the placeholder as one dynamic span.
        return PlayerPlaceholder().Replace(template, Wildcard.ToString());
    }

    /// <summary>
    /// True when <paramref name="textKey" /> is exactly the template with each
    /// wildcard replaced by any text. Matching is ordinal and anchored at both ends.
    /// </summary>
    public static bool Matches(string template, string textKey)
    {
        if (template.Length == 0 || textKey.Length == 0)
            return template.Length == textKey.Length;

        var parts = template.Split(Wildcard);
        if (parts.Length == 1)
            return string.Equals(template, textKey, StringComparison.Ordinal);

        var first = parts[0];
        var last = parts[^1];

        if (!textKey.StartsWith(first, StringComparison.Ordinal)
            || !textKey.EndsWith(last, StringComparison.Ordinal)
            || textKey.Length < first.Length + last.Length)
        {
            return false;
        }

        var position = first.Length;
        var end = textKey.Length - last.Length;

        for (var i = 1; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                continue;

            if (end < position)
                return false;

            var found = textKey.IndexOf(part, position, end - position, StringComparison.Ordinal);
            if (found < 0)
                return false;

            position = found + part.Length;
        }

        return position <= end;
    }
}
