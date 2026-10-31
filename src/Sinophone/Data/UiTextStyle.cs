using System.Text.RegularExpressions;

namespace Sinophone.Data;

/// <summary>
/// Foreground and glow colors carried by UIForeground / UIGlow markup. The game
/// applies these to the native text node; the plain displayed string does not keep
/// the tags.
/// </summary>
public readonly record struct UiTextStyle(uint? Foreground, uint? Glow)
{
    private static readonly Regex ForegroundPattern = new(
        @"<UIForeground>([0-9A-Fa-f]{6,8})</UIForeground>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex GlowPattern = new(
        @"<UIGlow>([0-9A-Fa-f]{6,8})</UIGlow>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? text, out UiTextStyle style)
    {
        style = default;

        if (string.IsNullOrEmpty(text))
            return false;

        var foreground = ForegroundPattern.Match(text);
        var glow = GlowPattern.Match(text);

        uint? foregroundColor = foreground.Success
            ? uint.Parse(foreground.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)
            : null;
        uint? glowColor = glow.Success
            ? uint.Parse(glow.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)
            : null;

        if (foregroundColor is null && glowColor is null)
            return false;

        style = new UiTextStyle(foregroundColor, glowColor);
        return true;
    }
}
