namespace Sinophone.Fonts;

/// <summary>What the last check found.</summary>
public enum FontModState
{
    /// <summary>Not checked yet.</summary>
    Unchecked,

    /// <summary>Penumbra is running and a mod replaces the game's font.</summary>
    ReplacedByPenumbra,

    /// <summary>Penumbra is running and leaves the font alone.</summary>
    PenumbraNoFontMod,

    /// <summary>Penumbra is not running, so there is no way to ask.</summary>
    PenumbraMissing,
}

/// <summary>
/// Answers one question for native text mode: is a font that can draw Chinese
/// replacing the game's own font right now?
/// </summary>
/// <remarks>
/// Penumbra can be asked. Its IPC resolves a game path to the file it would load, so
/// a path that resolves to something else is a path a mod replaced.
///
/// TexTools and FFXIVChnTextPatch edit the game's own data files on disk instead.
/// Neither is a Dalamud plugin, and neither leaves a marker a plugin can read, so
/// there is nothing to detect. The honest answer for those installs is that the
/// plugin cannot tell, and the user should look at the text.
///
/// This class makes no game calls, so a test can drive it without the game running.
/// </remarks>
public sealed class FontModDetector
{
    /// <summary>Penumbra IPC label. It resolves a path in the Interface collection.</summary>
    public const string ResolveLabel = "Penumbra.ResolveInterfacePath";

    /// <summary>
    /// Game paths that carry the glyphs for normal text. The game reads the .fdt table
    /// to map a codepoint to a cell in the .tex atlas, so a font mod replaces both.
    /// </summary>
    public static readonly string[] ProbePaths =
    [
        "common/font/AXIS_18.fdt",
        "common/font/font1.tex",
    ];

    /// <summary>
    /// Judges the answer Penumbra gave for each probe path. A path that comes back
    /// unchanged was not replaced; anything else points at a mod file.
    /// </summary>
    public static FontModState Evaluate(IReadOnlyList<(string Requested, string Resolved)> answers)
    {
        if (answers.Count == 0)
            return FontModState.PenumbraMissing;

        foreach (var (requested, resolved) in answers)
        {
            if (!string.Equals(requested, resolved, StringComparison.OrdinalIgnoreCase))
                return FontModState.ReplacedByPenumbra;
        }

        return FontModState.PenumbraNoFontMod;
    }

    /// <summary>One line a user can act on.</summary>
    public static string Describe(FontModState state)
        => state switch
        {
            FontModState.ReplacedByPenumbra =>
                "A Penumbra mod replaces the game font. Native text mode can draw Chinese.",
            FontModState.PenumbraNoFontMod =>
                "Penumbra is running but no mod replaces the game font. " +
                "Native text mode will show \"=\" for Chinese. Import a Chinese font mod and enable it.",
            FontModState.PenumbraMissing =>
                "Penumbra is not running, so the plugin cannot check the game font. " +
                "TexTools and FFXIVChnTextPatch give no signal either. " +
                "If Chinese draws as \"=\", replace the game font with one of those tools.",
            _ => "Not checked yet.",
        };
}
