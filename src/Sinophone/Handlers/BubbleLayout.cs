namespace Sinophone.Handlers;

/// <summary>
/// One bubble's nodes and the geometry it had before its text was replaced.
/// </summary>
/// <remarks>
/// The nine-grid is the visible bubble. The res node is the bubble's own content node, which the
/// game can leave at a default size, so it follows the fit too. The text node is never resized:
/// its width is what the game wraps at, and writing it makes the game break the line into
/// fragments.
/// </remarks>
internal readonly record struct BubbleLayout(
    nint NineGrid,
    nint Bubble,
    int TextWidth,
    int TextHeight,
    float TextX,
    float TextY,
    int BackgroundWidth,
    int BackgroundHeight,
    float BackgroundX,
    float BackgroundY,
    int BubbleWidth,
    int BubbleHeight,
    int CapsWidth,
    int CapsHeight)
{
    public static unsafe BubbleLayout Capture(
        AtkTextNode* node,
        AtkNineGridNode* nineGrid,
        AtkResNode* bubble)
        => new(
            (nint)nineGrid,
            (nint)bubble,
            node->Width,
            node->Height,
            node->X,
            node->Y,
            nineGrid is not null
                ? nineGrid->AtkResNode.Width
                : bubble is not null ? bubble->Width : 0,
            nineGrid is not null
                ? nineGrid->AtkResNode.Height
                : bubble is not null ? bubble->Height : 0,
            nineGrid is not null ? nineGrid->AtkResNode.X : 0f,
            nineGrid is not null ? nineGrid->AtkResNode.Y : 0f,
            bubble is null ? 0 : bubble->Width,
            bubble is null ? 0 : bubble->Height,
            Caps(nineGrid, horizontal: true),
            Caps(nineGrid, horizontal: false));

    /// <summary>
    /// Fits the background and the bubble's res node to the text the node now has.
    /// </summary>
    /// <remarks>
    /// The text node is left alone. A width change is placed by the fit rule: growth is centred so
    /// the tail stays in the middle, and a shrink keeps the left edge because the text is
    /// left-aligned. A height change keeps the bottom edge because the text is bottom-aligned and
    /// the tail hangs under it. When a size already matches, the node's position is left alone.
    /// </remarks>
    /// <returns>The size and position the background actually has, for the debug log.</returns>
    public unsafe BubbleFit FitTo(AtkTextNode* node)
    {
        var target = BubbleGeometry.FitRect(
            new BubbleRect(BackgroundWidth, BackgroundHeight, BackgroundX, BackgroundY),
            TextWidth,
            TextHeight,
            node->Width,
            node->Height,
            CapsWidth,
            CapsHeight);

        var width = Clamp(target.Width);
        var height = Clamp(target.Height);

        var nineGrid = NineGrid == 0 ? null : (AtkNineGridNode*)NineGrid;
        if (nineGrid is not null)
        {
            if (nineGrid->AtkResNode.Width != width)
            {
                nineGrid->AtkResNode.X = target.X;
                nineGrid->AtkResNode.Width = width;
            }

            if (nineGrid->AtkResNode.Height != height)
            {
                nineGrid->AtkResNode.Y = target.Y;
                nineGrid->AtkResNode.Height = height;
            }
        }

        var bubble = Bubble == 0 ? null : (AtkResNode*)Bubble;
        if (bubble is not null)
        {
            if (bubble->Width != width)
                bubble->Width = width;

            if (bubble->Height != height)
                bubble->Height = height;
        }

        var actualWidth = nineGrid is not null
            ? nineGrid->AtkResNode.Width
            : bubble is not null ? bubble->Width : width;
        var actualHeight = nineGrid is not null
            ? nineGrid->AtkResNode.Height
            : bubble is not null ? bubble->Height : height;

        return new BubbleFit(
            actualWidth,
            actualHeight,
            nineGrid is not null ? nineGrid->AtkResNode.X : 0f,
            nineGrid is not null ? nineGrid->AtkResNode.Y : 0f,
            bubble is not null ? bubble->Width : actualWidth,
            bubble is not null ? bubble->Height : actualHeight);
    }

    /// <summary>
    /// The balloon texture's border on one axis, or zero when the asset does not say. The nine-grid
    /// offsets are the slices the texture stretches between, which is the room the corners need.
    /// </summary>
    private static unsafe int Caps(AtkNineGridNode* nineGrid, bool horizontal)
    {
        if (nineGrid is null)
            return 0;

        var caps = horizontal
            ? nineGrid->LeftOffset + nineGrid->RightOffset
            : nineGrid->TopOffset + nineGrid->BottomOffset;

        return caps is > 0 and < 256 ? caps : 0;
    }

    /// <summary>Puts the background and the bubble's res node back to the sizes the game gave them.</summary>
    public unsafe void Restore()
    {
        if (NineGrid != 0 && (AtkNineGridNode*)NineGrid is var nineGrid && nineGrid is not null)
        {
            nineGrid->AtkResNode.Width = Clamp(BackgroundWidth);
            nineGrid->AtkResNode.Height = Clamp(BackgroundHeight);
            nineGrid->AtkResNode.X = BackgroundX;
            nineGrid->AtkResNode.Y = BackgroundY;
        }

        if (Bubble != 0 && (AtkResNode*)Bubble is var bubble && bubble is not null)
        {
            bubble->Width = Clamp(BubbleWidth);
            bubble->Height = Clamp(BubbleHeight);
        }
    }

    private static ushort Clamp(int value)
        => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
}

/// <summary>The size and position a bubble background has after a fit.</summary>
internal readonly record struct BubbleFit(
    int Width,
    int Height,
    float X,
    float Y,
    int BubbleWidth,
    int BubbleHeight);
