using System.Text.RegularExpressions;
using Sinophone.Data;

namespace Sinophone.Coverage;

/// <summary>
/// The dynamic parts of a line, and the game values the harness feeds them.
/// </summary>
/// <remarks>
/// A row is not one case but several when it carries a value the game fills at runtime. Each
/// category gets plausible game values, so the line is measured with the substitutions the handlers
/// really perform rather than with the raw dump text.
/// </remarks>
internal static partial class Dynamic
{
    /// <summary>Values used for <c>IntegerParameter(n)</c>, one vector per case.</summary>
    public static readonly int[][] IntegerVectors =
    [
        [1, 1, 1, 1],
        [7, 42, 999, 3],
        [0, 65535, 12, 1],
    ];

    [GeneratedRegex(@"<If\(", RegexOptions.CultureInvariant)]
    private static partial Regex ConditionalPattern();

    [GeneratedRegex(@"<Sheet\(", RegexOptions.CultureInvariant)]
    private static partial Regex SheetPattern();

    [GeneratedRegex(@"IntegerParameter\(|ZeroPaddedValue\(", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"ObjectParameter\(", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectPattern();

    [GeneratedRegex(@"Player Player|(?<!\w)Player(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerPattern();

    /// <summary>
    /// True for a label the game assembles itself, such as <c>8:00 a.m.</c>, <c>Lv. UNKNOWN</c>, or
    /// <c>UNKNOWN/UNKNOWN</c>. The Chinese client builds those too, so counting them as misses
    /// understates coverage; the report counts them apart.
    /// </summary>
    public static bool IsRuntimeLabel(string english)
    {
        if (english.Contains("UNKNOWN", StringComparison.Ordinal))
            return true;

        var trimmed = english.Trim();
        return ClockPattern().IsMatch(trimmed) || LevelPattern().IsMatch(trimmed);
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}\s*[ap]\.m\.$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"^Lv\.", RegexOptions.CultureInvariant)]
    private static partial Regex LevelPattern();

    /// <summary>The categories a line carries. Empty for a plain line.</summary>
    public static string[] Tags(string english)
    {
        var tags = new List<string>(4);

        if (ConditionalPattern().IsMatch(english))
            tags.Add("conditional");
        if (SheetPattern().IsMatch(english))
            tags.Add("sheet-name");
        if (IntegerPattern().IsMatch(english))
            tags.Add("integer");
        if (ObjectPattern().IsMatch(english))
            tags.Add("object");
        if (PlayerPattern().IsMatch(english))
            tags.Add("player-name");
        if (english.Contains("  ", StringComparison.Ordinal))
            tags.Add("blank-gap");

        return [.. tags];
    }

    /// <summary>The player names the harness substitutes, so both placeholder forms are covered.</summary>
    public static readonly string?[] PlayerNames = [null, "Y'shtola Rhul"];
}
