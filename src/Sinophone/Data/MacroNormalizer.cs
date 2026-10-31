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

    [GeneratedRegex(@"<(?:UIForeground|UIGlow|Unknown2D)>.*?</(?:UIForeground|UIGlow|Unknown2D)>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UiMarkupPattern();

    [GeneratedRegex(@"ObjectParameter\((\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectParameterPattern();

    [GeneratedRegex(@"IntegerParameter\((\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerParameterPattern();

    [GeneratedRegex(@"^\s*\(-[^)]*-\)", RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerPrefixPattern();

    /// <summary>
    /// The script dumps wrap italics in one asterisk and bold in two. The game draws
    /// the same emphasis, but its text node carries control codes, not asterisks, so
    /// a line with any emphasis never matched without this strip.
    /// </summary>
    [GeneratedRegex(@"\*+", RegexOptions.CultureInvariant)]
    private static partial Regex EmphasisPattern();

    /// <summary>
    /// What the dumps write where a line names the player. The live game writes the
    /// player's real name there instead, so the two do not match without help.
    /// </summary>
    public const string PlayerNamePlaceholder = "Player Player";

    /// <summary>
    /// The other placeholder form in the dumps. Some lines write one "Player" rather
    /// than two, for example "Player...and Jullus.".
    /// </summary>
    public const string SinglePlayerNamePlaceholder = "Player";

    /// <summary>
    /// Parameter index that holds the player's name in stored Chinese text. Index 1
    /// is the player; the only other indices in the current data are a chocobo (55)
    /// and one other object (56).
    /// </summary>
    public const int PlayerNameParameter = 1;

    /// <summary>
    /// Puts <see cref="PlayerNamePlaceholder" /> back where a key has the player's
    /// name. The dumps write the placeholder, so a line the game has filled in with
    /// the real name only matches once the name is swapped back.
    /// </summary>
    /// <returns>
    /// The same string instance when there is nothing to replace, so a miss does not
    /// pay for a copy.
    /// </returns>
    public static string RestorePlayerNamePlaceholder(string key, string? playerName)
        => RestorePlayerName(key, playerName, PlayerNamePlaceholder);

    /// <summary>
    /// Puts the single <see cref="SinglePlayerNamePlaceholder" /> back where a key
    /// has the player's name. Used after the double placeholder misses.
    /// </summary>
    public static string RestoreSinglePlayerNamePlaceholder(string key, string? playerName)
        => RestorePlayerName(key, playerName, SinglePlayerNamePlaceholder);

    private static string RestorePlayerName(string key, string? playerName, string placeholder)
    {
        if (key.Length == 0 || string.IsNullOrWhiteSpace(playerName))
            return key;

        var name = playerName.Trim();

        // Replacing the placeholder with itself would only churn the dictionary.
        if (name.Length == 0
            || name.Contains(placeholder, StringComparison.Ordinal)
            || !key.Contains(name, StringComparison.Ordinal))
        {
            return key;
        }

        return key.Replace(name, placeholder, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when stored text carries <c>ObjectParameter(n)</c>. Such text cannot be
    /// resolved at load time, because the answer depends on the player.
    /// </summary>
    public static bool HasObjectParameter(string? text)
        => text is not null && text.Contains("ObjectParameter(", StringComparison.Ordinal);

    /// <summary>
    /// Fills <c>ObjectParameter(n)</c> in stored Chinese text. Only the player's own
    /// name is known to the plugin; other parameters are dropped rather than shown as
    /// the literal macro text.
    /// </summary>
    private static string ResolveObjectParameters(string text, string? playerName)
        => ObjectParameterPattern().Replace(text, match =>
        {
            if (int.TryParse(match.Groups[1].Value, out var index) && index == PlayerNameParameter)
                return playerName?.Trim() ?? string.Empty;

            return string.Empty;
        });

    /// <summary>
    /// Replaces <c>IntegerParameter(n)</c> left in stored text after macro
    /// stripping. The values come from the addon's numeric AtkValues. A missing
    /// value is removed rather than shown as the macro name.
    /// </summary>
    public static string ResolveIntegerParameters(string? text, IReadOnlyList<int>? parameters)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("IntegerParameter(", StringComparison.Ordinal))
            return text ?? string.Empty;

        return IntegerParameterPattern().Replace(text, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out var index) || parameters is null)
                return string.Empty;

            return index > 0 && index <= parameters.Count
                ? parameters[index - 1].ToString()
                : string.Empty;
        });
    }

    /// <summary>
    /// Reads the name inside a leading speaker alias such as "(-Distant Echo-)".
    /// Returns an empty string when the line has no alias.
    /// </summary>
    public static string ExtractSpeakerPrefix(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var match = SpeakerPrefixPattern().Match(text);
        return match.Success ? match.Value[2..^2] : string.Empty;
    }

    /// <summary>
    /// Removes a leading speaker alias such as "(-Radio-)". The script dumps put
    /// the alias in the English line, but the game draws it in the speaker node,
    /// not in the dialogue text.
    /// </summary>
    public static string StripSpeakerPrefix(string? text)
        => string.IsNullOrEmpty(text) ? text ?? string.Empty : SpeakerPrefixPattern().Replace(text, string.Empty);

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
        cleaned = EmphasisPattern().Replace(cleaned, string.Empty);

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
    /// Prepares Chinese text for display: conditionals are resolved, macros are
    /// stripped, line breaks are preserved, and trailing whitespace on each line is
    /// trimmed.
    /// </summary>
    /// <param name="text">Text from the artifact.</param>
    /// <param name="evaluate">
    /// Answers a conditional expression. Null means the condition is unknown; the
    /// first branch is shown.
    /// </param>
    /// <param name="playerName">
    /// The player's full name, used to fill <c>ObjectParameter(1)</c>. Null leaves
    /// that parameter empty.
    /// </param>
    public static string CleanForDisplay(string? text, Func<string, bool?>? evaluate = null, string? playerName = null)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cleaned = StripInvisible(text);
        cleaned = cleaned.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        cleaned = LineBreakPattern().Replace(cleaned, "\n");
        cleaned = ConditionalText.Resolve(cleaned, evaluate);

        // The script dumps prefix some lines with a speaker alias such as
        // "(-Pattin-)". The game draws that name in the speaker node, so it is
        // metadata, not part of the dialogue the overlay should show.
        cleaned = SpeakerPrefixPattern().Replace(cleaned, string.Empty);

        // Before the macro strip: that strip removes <Highlight> and </Highlight> but
        // leaves the parameter text between them sitting in the sentence.
        cleaned = ResolveObjectParameters(cleaned, playerName);

        // UI color tags carry payload text such as F20223 or 01 between the tags.
        // Remove the whole pair, not just the tags.
        cleaned = UiMarkupPattern().Replace(cleaned, string.Empty);
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
