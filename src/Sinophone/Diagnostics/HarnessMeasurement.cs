namespace Sinophone.Diagnostics;

/// <summary>
/// What one case measured on the surface it drove. The engine fills this in; the checks in
/// <see cref="HarnessAssertions"/> only read it. Keeping it plain data means the rules can be
/// tested from a literal, and a run can be written to disk.
/// </summary>
public sealed record HarnessMeasurement
{
    /// <summary>The text the surface drew.</summary>
    public string? Drawn { get; init; }

    /// <summary>The English line the case sent.</summary>
    public string? English { get; init; }

    /// <summary>The Chinese the store should answer with, when the case knows it.</summary>
    public string? Expected { get; init; }

    /// <summary>Measured width of the drawn text, in pixels. Zero when not measured.</summary>
    public int TextWidth { get; init; }

    /// <summary>Measured height of the drawn text, in pixels. Zero when not measured.</summary>
    public int TextHeight { get; init; }

    /// <summary>Width of the room the game gave the text, in pixels.</summary>
    public int BoxWidth { get; init; }

    /// <summary>Height of the room the game gave the text, in pixels.</summary>
    public int BoxHeight { get; init; }

    /// <summary>Lines as drawn, when the caller knows the breaks.</summary>
    public IReadOnlyList<string>? RenderedLines { get; init; }

    /// <summary>Times the game replaced the translation after it was written.</summary>
    public int Reverts { get; init; }

    /// <summary>Characters in the drawn text that the game font has no glyph for.</summary>
    public int MissingGlyphs { get; init; }

    /// <summary>Why the case could not run, when it did not.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Results the checks produced, filled in by the runner.</summary>
    public IReadOnlyList<HarnessFinding> Findings { get; init; } = [];

    /// <summary>True when every finding passed.</summary>
    public bool Passed => Findings.All(static finding => finding.Passed);
}
