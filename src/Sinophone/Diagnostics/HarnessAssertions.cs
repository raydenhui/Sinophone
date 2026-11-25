using System.Text.RegularExpressions;

namespace Sinophone.Diagnostics;

/// <summary>One check's result.</summary>
/// <param name="Check">Which check ran.</param>
/// <param name="Passed">False when the check found a problem.</param>
/// <param name="Detail">What was found, in words a reader can act on.</param>
public readonly record struct HarnessFinding(HarnessCheck Check, bool Passed, string Detail);

/// <summary>
/// The checks a harness case runs on a drawn line. Everything here is a pure function of
/// the text and sizes the caller measured, so the rules are unit tested without a game.
/// </summary>
public static class HarnessAssertions
{
    /// <summary>
    /// Marks that must not start a rendered line. The same set <see cref="Data.CjkLineWrapper"/>
    /// keeps off a line start, so a failure here means the wrapper did not run or the game
    /// re-wrapped the line.
    /// </summary>
    public const string NoLineStart = "。，、．；：？！）］｝」』】〉》”’…";

    private static readonly Regex MacroTag = new(
        @"<(?:Sheet|StringParameter|IntegerParameter|Time|If|EnNoun|Unknown[0-9A-Fa-f]{2})",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// True when the text still holds a macro the game would draw as text. A plain tag such as
    /// <c>&lt;br&gt;</c> is not a leak: it is removed before lookup.
    /// </summary>
    public static bool HasMacroLeak(string? text)
        => !string.IsNullOrEmpty(text) && MacroTag.IsMatch(text);

    /// <summary>Checks that no macro survived into the drawn text.</summary>
    public static HarnessFinding NoMacro(string? drawn)
    {
        if (!HasMacroLeak(drawn))
            return new HarnessFinding(HarnessCheck.NoMacro, true, "No macro in the drawn text.");

        var match = MacroTag.Match(drawn!);
        return new HarnessFinding(HarnessCheck.NoMacro, false,
            $"Macro left in the drawn text at \"{Excerpt(drawn!, match.Index)}\".");
    }

    /// <summary>
    /// Checks that no rendered line starts with a closing mark. Uses the line breaks the caller
    /// passes in, which are the breaks as drawn: a node that wraps on its own has none, so the
    /// caller has to report the breaks it measured.
    /// </summary>
    public static HarnessFinding NoLeadingClosingMark(IReadOnlyList<string>? renderedLines)
    {
        if (renderedLines is null || renderedLines.Count == 0)
            return new HarnessFinding(HarnessCheck.NoLeadingClosingMark, true,
                "No rendered lines to check.");

        for (var index = 0; index < renderedLines.Count; index++)
        {
            var line = renderedLines[index];
            if (line.Length == 0)
                continue;

            if (NoLineStart.IndexOf(line[0]) >= 0)
                return new HarnessFinding(HarnessCheck.NoLeadingClosingMark, false,
                    $"Line {index + 1} starts with \"{line[0]}\": {Excerpt(line, 0)}");
        }

        return new HarnessFinding(HarnessCheck.NoLeadingClosingMark, true,
            "No line starts with a closing mark.");
    }

    /// <summary>
    /// Checks that the text fits the room the game gave it. A zero size means the caller could
    /// not measure, which is a skip, not a failure, so it passes with a note.
    /// </summary>
    public static HarnessFinding Fits(int textWidth, int textHeight, int boxWidth, int boxHeight)
    {
        if (textWidth <= 0 || textHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
            return new HarnessFinding(HarnessCheck.Fits, true,
                "No measurements; nothing to compare.");

        if (textWidth > boxWidth || textHeight > boxHeight)
            return new HarnessFinding(HarnessCheck.Fits, false,
                $"Text {textWidth}x{textHeight} is larger than the box {boxWidth}x{boxHeight}.");

        return new HarnessFinding(HarnessCheck.Fits, true,
            $"Text {textWidth}x{textHeight} fits the box {boxWidth}x{boxHeight}.");
    }

    /// <summary>
    /// Checks that the drawn text is the expected translation. With no expectation, any
    /// non-empty text that is not the English line passes.
    /// </summary>
    /// <remarks>
    /// Line breaks are ignored when the Chinese is compared: the native Talk writer wraps a long
    /// line and puts the break in the text, while the store holds the line as one string. When
    /// the English is compared, a break counts as the space it replaced, because the game wraps
    /// the English line at a space. The break itself is a display detail, and
    /// <see cref="NoLeadingClosingMark"/> checks it separately.
    /// </remarks>
    public static HarnessFinding Translated(string? drawn, string? expected, string? english)
    {
        if (string.IsNullOrWhiteSpace(drawn))
            return new HarnessFinding(HarnessCheck.Translated, false, "Nothing was drawn.");

        if (!string.IsNullOrEmpty(expected))
        {
            return string.Equals(WithoutBreaks(drawn), WithoutBreaks(expected), StringComparison.Ordinal)
                ? new HarnessFinding(HarnessCheck.Translated, true, "The expected Chinese was drawn.")
                : new HarnessFinding(HarnessCheck.Translated, false,
                    $"Expected \"{Excerpt(expected, 0)}\" but drew \"{Excerpt(drawn!, 0)}\".");
        }

        if (!string.IsNullOrEmpty(english) &&
            string.Equals(BreaksAsSpaces(drawn), BreaksAsSpaces(english), StringComparison.Ordinal))
        {
            return new HarnessFinding(HarnessCheck.Translated, false,
                "The English line was drawn; no translation reached the surface.");
        }

        return new HarnessFinding(HarnessCheck.Translated, true, "A translation was drawn.");
    }

    /// <summary>
    /// Checks that the surface still holds the translation. A revert means the game replaced it,
    /// the bug the Talk window had before the draw-time write.
    /// </summary>
    public static HarnessFinding NoRevert(int reverts)
        => reverts <= 0
            ? new HarnessFinding(HarnessCheck.NoRevert, true, "The translation stayed in place.")
            : new HarnessFinding(HarnessCheck.NoRevert, false,
                $"The game replaced the translation {reverts} time(s) after it was written.");

    /// <summary>
    /// Runs the requested checks from one set of measurements and returns the findings.
    /// Findings are returned in a fixed order, so a report reads the same every run.
    /// </summary>
    public static IReadOnlyList<HarnessFinding> Run(
        HarnessCheck checks,
        HarnessMeasurement measurement)
    {
        var findings = new List<HarnessFinding>(6);

        if (checks.HasFlag(HarnessCheck.Translated))
            findings.Add(Translated(measurement.Drawn, measurement.Expected, measurement.English));

        if (checks.HasFlag(HarnessCheck.NoMacro))
            findings.Add(NoMacro(measurement.Drawn));

        if (checks.HasFlag(HarnessCheck.Fits))
        {
            findings.Add(Fits(
                measurement.TextWidth, measurement.TextHeight,
                measurement.BoxWidth, measurement.BoxHeight));
        }

        if (checks.HasFlag(HarnessCheck.NoLeadingClosingMark))
            findings.Add(NoLeadingClosingMark(measurement.RenderedLines));

        if (checks.HasFlag(HarnessCheck.NoRevert))
            findings.Add(NoRevert(measurement.Reverts));

        // Glyphs needs the game font, so the engine side reports it. A surface that cannot
        // read the font reports -1, which is "not measured" and not a font problem.
        if (checks.HasFlag(HarnessCheck.Glyphs))
        {
            findings.Add(measurement.MissingGlyphs switch
            {
                < 0 => new HarnessFinding(HarnessCheck.Glyphs, true, "The game font could not be read."),
                > 0 => new HarnessFinding(HarnessCheck.Glyphs, false,
                    $"{measurement.MissingGlyphs} character(s) have no glyph in the game font."),
                _ => new HarnessFinding(HarnessCheck.Glyphs, true, "Every character has a glyph."),
            });
        }

        return findings;
    }

    /// <summary>
    /// Splits drawn text into lines for the closing-mark check. A wrapped node reports its own
    /// breaks; the harness falls back to the explicit breaks in the string.
    /// </summary>
    public static IReadOnlyList<string> ExplicitLines(string? text)
        => string.IsNullOrEmpty(text) ? [] : text.Split('\n');

    private static string Excerpt(string text, int index)
    {
        const int Window = 32;
        var start = Math.Max(0, index - 8);
        var length = Math.Min(Window, text.Length - start);
        var excerpt = text.Substring(start, length).Replace('\n', ' ');
        return start > 0 ? "..." + excerpt : excerpt;
    }

    /// <summary>Strips the line breaks a display adds, so a wrapped line still compares equal.</summary>
    private static string WithoutBreaks(string text)
        => text.Contains('\n') || text.Contains('\r')
            ? text.Replace("\r", string.Empty).Replace("\n", string.Empty)
            : text;

    /// <summary>
    /// Reads a break as the space it replaced. The English client wraps a line in the text and
    /// keeps the space it broke at, so a drawn English line matches a one-line source.
    /// </summary>
    private static string BreaksAsSpaces(string text)
        => text.Contains('\n') || text.Contains('\r')
            ? text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')
            : text;
}
