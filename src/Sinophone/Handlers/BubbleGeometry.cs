namespace Sinophone.Handlers;

/// <summary>
/// The rule that fits a chat bubble's background to a translated line. Pure numbers, so every
/// case the display can meet is tested without the game.
/// </summary>
/// <remarks>
/// The game lays a balloon out as the English text plus the margin the balloon keeps. That margin
/// is the only padding worth carrying over, and it is a real inset: larger than zero and no wider
/// than the nine-grid's fixed corners. A background that does not hold such a margin is holding
/// another line or has not been laid out for this one, so the corners are the only padding that
/// can be trusted. The corners are a fallback, not a floor on every line: using them as the
/// padding for a line that already fits is what made a bubble taller than its text.
/// </remarks>
internal static class BubbleGeometry
{
    /// <summary>
    /// The padding the background keeps around the text on one axis. A background laid out for the
    /// English line gives the margin it drew. Any other background gives the balloon's corners.
    /// </summary>
    internal static int Padding(int background, int englishText, int caps)
    {
        if (englishText <= 0)
            return Math.Max(caps, 0);

        var margin = background - englishText;

        if (caps <= 0)
            return Math.Max(margin, 0);

        return margin > 0 && margin <= caps ? margin : caps;
    }

    /// <summary>
    /// The background size that holds <paramref name="chineseText"/> with the padding the game
    /// used for the English line, or with the balloon's corners when that layout is not readable.
    /// </summary>
    internal static int FitSize(int background, int englishText, int chineseText, int caps)
    {
        if (englishText <= 0)
        {
            // No English measure means no margin to carry. The corners are the only padding that
            // can be trusted, and the text itself is the floor when the asset has no corners.
            return caps > 0
                ? Math.Max(background, chineseText + caps)
                : Math.Max(background, chineseText);
        }

        if (caps <= 0)
        {
            // Without corners there is no safe padding to fall back on, so follow a wider line by
            // the margin and never shrink a background that may have been sized by the game.
            return Math.Max(background, chineseText + Math.Max(background - englishText, 0));
        }

        return chineseText + Padding(background, englishText, caps);
    }

    /// <summary>
    /// The background rectangle after the text changes. The width grows to both sides, so the tail
    /// the game placed under the text stays centred, and shrinks from the right, so the left-aligned
    /// text keeps its left edge. The height keeps the bottom edge, which the text and the tail hang
    /// from.
    /// </summary>
    internal static BubbleRect FitRect(
        BubbleRect background,
        int englishWidth,
        int englishHeight,
        int chineseWidth,
        int chineseHeight,
        int capsWidth,
        int capsHeight)
    {
        var width = FitSize(background.Width, englishWidth, chineseWidth, capsWidth);
        var height = FitSize(background.Height, englishHeight, chineseHeight, capsHeight);

        var x = background.X;
        if (width > background.Width)
            x -= (width - background.Width) / 2f;

        return new BubbleRect(width, height, x, background.Y - (height - background.Height));
    }
}

/// <summary>A bubble background's size and position, with the left/top origin an ATK node uses.</summary>
internal readonly record struct BubbleRect(int Width, int Height, float X, float Y);
