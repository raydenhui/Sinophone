#if SINOPHONE_TESTING
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Sinophone.Diagnostics;

/// <summary>
/// Counts the characters the game font has no glyph for.
/// </summary>
/// <remarks>
/// The docs say the plugin cannot tell whether the font mod is installed. This asks the game's
/// own font manager instead: a codepoint with no glyph is one the game draws as the missing-glyph
/// character. It is a strong hint, not proof, because a font can hold a glyph the game still
/// refuses to draw. Everything here is behind <c>SINOPHONE_TESTING</c>.
/// </remarks>
public static unsafe class GlyphProbe
{
    /// <summary>
    /// Characters in <paramref name="text"/> with no glyph in <paramref name="fontType"/>.
    /// Returns -1 when the font manager is not reachable, so a missing structure is reported as
    /// "not measured" rather than as a font problem.
    /// </summary>
    public static int CountMissing(string? text, FontType fontType)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var manager = GetFontManager();
        if (manager is null)
            return -1;

        var setIndex = (int)fontType;
        if (setIndex < 0 || setIndex >= AtkFontManager.FontSetCount || manager->FontSets is null)
            return -1;

        var fontSet = &manager->FontSets[setIndex];
        var font = fontSet->PickFont(false);
        if (font is null)
            return -1;

        var missing = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            // Space has no glyph in a font that lays it out as an advance.
            if (Rune.IsWhiteSpace(rune))
                continue;

            if (fontSet->GetGlyph((uint)rune.Value, font) is null)
                missing++;
        }

        return missing;
    }

    private static AtkFontManager* GetFontManager()
    {
        try
        {
            var stage = AtkStage.Instance();
            return stage is null ? null : stage->AtkFontManager;
        }
        catch
        {
            // A failed signature resolve is a reason to skip the check, not to fail a case.
            return null;
        }
    }
}
#endif
