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
    /// A short sample with characters the stock Axis font misses and characters it draws, so a
    /// developer can see at once whether the loaded font carries Chinese.
    /// </summary>
    public const string Sample = "這是中文樣本。这是中文样本。歷閱臺灣龍鳳龜";

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

    /// <summary>
    /// A readable result for the Testing tab. Unlike <see cref="CountMissing"/>, this one
    /// allocates and names the characters, which is fine for a button press but not for a frame.
    /// </summary>
    public static string Describe(FontType fontType)
    {
        var manager = GetFontManager();
        if (manager is null)
            return $"{fontType}: the game font manager could not be read.";

        var setIndex = (int)fontType;
        if (setIndex < 0 || setIndex >= AtkFontManager.FontSetCount || manager->FontSets is null)
            return $"{fontType}: the game font set could not be read.";

        var fontSet = &manager->FontSets[setIndex];
        var font = fontSet->PickFont(false);
        if (font is null)
            return $"{fontType}: the game font is not built.";

        var missing = new List<string>();
        var total = 0;
        foreach (var rune in Sample.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                continue;

            total++;
            if (fontSet->GetGlyph((uint)rune.Value, font) is null)
                missing.Add(rune.ToString());
        }

        if (missing.Count == 0)
            return $"{fontType}: all {total} sample characters have a glyph.";

        return $"{fontType}: {missing.Count} of {total} sample characters have no glyph: " +
               string.Join(" ", missing);
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
