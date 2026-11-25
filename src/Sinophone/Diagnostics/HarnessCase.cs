namespace Sinophone.Diagnostics;

/// <summary>
/// A surface the testing tab can drive. The value names the component, not a single
/// addon: one surface can cover several addons, such as the four selection dialogs.
/// </summary>
public enum HarnessSurface
{
    Talk,
    TalkSubtitle,
    ChatBubble,
    BattleTalk,
    Selection,
    ItemName,
    NamePlate,
}

/// <summary>A case passed, failed, or has not run yet.</summary>
public enum HarnessOutcome
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped,
}

/// <summary>
/// What one case checks. Every check is one property of the drawn line, so a failure
/// says which property broke instead of "the display is wrong".
/// </summary>
[Flags]
public enum HarnessCheck
{
    None = 0,

    /// <summary>The Chinese line reached the surface at all.</summary>
    Translated = 1 << 0,

    /// <summary>No macro tag is left in the drawn text.</summary>
    NoMacro = 1 << 1,

    /// <summary>The text fits the room the game gave it.</summary>
    Fits = 1 << 2,

    /// <summary>No rendered line starts with a closing mark.</summary>
    NoLeadingClosingMark = 1 << 3,

    /// <summary>Every character has a glyph in the game font.</summary>
    Glyphs = 1 << 4,

    /// <summary>The game did not replace the translation after it was written.</summary>
    NoRevert = 1 << 5,

    All = Translated | NoMacro | Fits | NoLeadingClosingMark | Glyphs | NoRevert,
}

/// <summary>
/// One thing to try: a surface, an English line, and the checks to run on the result.
/// Cases are plain data so a run can be written to disk and replayed, and so a case can
/// be built without the game.
/// </summary>
/// <param name="Id">Stable name, also used as the report key.</param>
/// <param name="Surface">Component to drive.</param>
/// <param name="English">The line the surface is given.</param>
/// <param name="Name">Speaker name, when the surface shows one.</param>
/// <param name="ExpectedChinese">Chinese the store should answer with, when known.</param>
/// <param name="Checks">Checks to run on the result.</param>
/// <param name="Tags">Free labels such as <c>long</c> or <c>tc</c>, used to filter a run.</param>
public sealed record HarnessCase(
    string Id,
    HarnessSurface Surface,
    string English,
    string? Name = null,
    string? ExpectedChinese = null,
    HarnessCheck Checks = HarnessCheck.All,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>True when <paramref name="tag"/> is one of this case's tags.</summary>
    public bool HasTag(string tag)
        => Tags is not null && Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}
