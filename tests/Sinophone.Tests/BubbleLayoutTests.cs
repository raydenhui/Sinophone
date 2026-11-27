using System.Runtime.InteropServices;

using FFXIVClientStructs.FFXIV.Component.GUI;

using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// The display half of the bubble fit: the writes into real ATK node memory, with no game.
/// <see cref="BubbleGeometryTests"/> checks the rule; these tests check that the rule reaches
/// both background nodes, the position, and the restore.
/// </summary>
public sealed unsafe class BubbleLayoutTests : IDisposable
{
    private readonly AtkTextNode* _text;
    private readonly AtkNineGridNode* _nineGrid;
    private readonly AtkResNode* _bubble;

    public BubbleLayoutTests()
    {
        _text = (AtkTextNode*)NativeMemory.AllocZeroed((nuint)sizeof(AtkTextNode));
        _nineGrid = (AtkNineGridNode*)NativeMemory.AllocZeroed((nuint)sizeof(AtkNineGridNode));
        _bubble = (AtkResNode*)NativeMemory.AllocZeroed((nuint)sizeof(AtkResNode));
    }

    public void Dispose()
    {
        NativeMemory.Free(_text);
        NativeMemory.Free(_nineGrid);
        NativeMemory.Free(_bubble);
    }

    [Fact]
    public void Capture_ReadsTheTextAndBackground()
    {
        Arrange(englishWidth: 124, englishHeight: 21, backgroundWidth: 151, backgroundHeight: 36,
            x: 100, y: 50, capsWidth: 36, capsHeight: 34, bubbleWidth: 60, bubbleHeight: 36);

        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        Assert.Equal(124, layout.TextWidth);
        Assert.Equal(21, layout.TextHeight);
        Assert.Equal(151, layout.BackgroundWidth);
        Assert.Equal(36, layout.BackgroundHeight);
        Assert.Equal(100f, layout.BackgroundX);
        Assert.Equal(50f, layout.BackgroundY);
        Assert.Equal(36, layout.CapsWidth);
        Assert.Equal(34, layout.CapsHeight);
        Assert.Equal(60, layout.BubbleWidth);
        Assert.Equal(36, layout.BubbleHeight);
    }

    [Fact]
    public void FitTo_KeepsTheGameHeight_WhenTheChineseIsNotTaller()
    {
        // Issue 223: English 124x21, Chinese 84x21, background 151x36, vertical caps 34. The
        // height must stay 36, not grow to 21 + 34. The width follows the margin and shrinks.
        Arrange(124, 21, 151, 36, 100, 50, 36, 34, 60, 36);
        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        _text->Width = 84;
        _text->Height = 21;

        var fitted = layout.FitTo(_text);

        Assert.Equal(111, _nineGrid->AtkResNode.Width);
        Assert.Equal(36, _nineGrid->AtkResNode.Height);
        Assert.Equal(100f, _nineGrid->AtkResNode.X);
        Assert.Equal(50f, _nineGrid->AtkResNode.Y);
        Assert.Equal(111, _bubble->Width);
        Assert.Equal(36, _bubble->Height);
        Assert.Equal(111, fitted.Width);
        Assert.Equal(36, fitted.Height);
        Assert.Equal(111, fitted.BubbleWidth);
        Assert.Equal(36, fitted.BubbleHeight);
        Assert.Equal(84, _text->Width);
        Assert.Equal(21, _text->Height);
    }

    [Fact]
    public void FitTo_GrowsBothSidesAndUp_SoTheTailStaysPut()
    {
        Arrange(124, 21, 151, 36, 100, 50, 36, 34, 60, 36);
        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        _text->Width = 200;
        _text->Height = 50;

        layout.FitTo(_text);

        Assert.Equal(227, _nineGrid->AtkResNode.Width);
        Assert.Equal(65, _nineGrid->AtkResNode.Height);
        Assert.Equal(62f, _nineGrid->AtkResNode.X);
        Assert.Equal(21f, _nineGrid->AtkResNode.Y);
        Assert.Equal(227, _bubble->Width);
        Assert.Equal(65, _bubble->Height);
    }

    [Fact]
    public void FitTo_ShrinksFromTheRight_SoTheTextKeepsItsLeftEdge()
    {
        Arrange(182, 37, 209, 52, 100, 50, 36, 34, 60, 36);
        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        _text->Width = 48;
        _text->Height = 37;

        layout.FitTo(_text);

        Assert.Equal(75, _nineGrid->AtkResNode.Width);
        Assert.Equal(52, _nineGrid->AtkResNode.Height);
        Assert.Equal(100f, _nineGrid->AtkResNode.X);
        Assert.Equal(50f, _nineGrid->AtkResNode.Y);
        Assert.Equal(75, _bubble->Width);
        Assert.Equal(52, _bubble->Height);
    }

    [Fact]
    public void FitTo_KeepsTheBottomEdge_WhenAStaleBackgroundShrinks()
    {
        // English 68x21 against a 212x68 background that still holds another line. The corners
        // are the fallback, and the bottom edge stays where the game put it.
        Arrange(68, 21, 212, 68, 100, 50, 36, 34, 60, 36);
        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        _text->Width = 60;
        _text->Height = 21;

        layout.FitTo(_text);

        Assert.Equal(96, _nineGrid->AtkResNode.Width);
        Assert.Equal(55, _nineGrid->AtkResNode.Height);
        Assert.Equal(100f, _nineGrid->AtkResNode.X);
        Assert.Equal(63f, _nineGrid->AtkResNode.Y);
        Assert.Equal(118f, _nineGrid->AtkResNode.Y + _nineGrid->AtkResNode.Height);
        Assert.Equal(96, _bubble->Width);
        Assert.Equal(55, _bubble->Height);
    }

    [Fact]
    public void FitTo_UsesTheNewLinesGameLayout_AfterALongLine()
    {
        // Line one: English 185x53 in a 212x68 background. The game then lays line two out for
        // its own English 68x21, which is 95x36. The handler must capture that layout, not put
        // the long line's 212x68 back. The second line then fits to 87x36, not 96x55.
        Arrange(185, 53, 212, 68, 100, 50, 36, 34, 60, 36);
        var first = BubbleLayout.Capture(_text, _nineGrid, _bubble);
        _text->Width = 140;
        _text->Height = 53;
        first.FitTo(_text);

        // The game writes line two's English and lays the bubble out for it.
        _text->Width = 68;
        _text->Height = 21;
        _nineGrid->AtkResNode.Width = 95;
        _nineGrid->AtkResNode.Height = 36;
        _nineGrid->AtkResNode.X = 110;
        _nineGrid->AtkResNode.Y = 60;

        var second = BubbleLayout.Capture(_text, _nineGrid, _bubble);
        _text->Width = 60;
        _text->Height = 21;
        second.FitTo(_text);

        Assert.Equal(87, _nineGrid->AtkResNode.Width);
        Assert.Equal(36, _nineGrid->AtkResNode.Height);
        Assert.Equal(110f, _nineGrid->AtkResNode.X);
        Assert.Equal(60f, _nineGrid->AtkResNode.Y);
    }

    [Fact]
    public void FitTo_IsIdempotent_SoThePerFrameCallDoesNotDrift()
    {
        Arrange(124, 21, 151, 36, 100, 50, 36, 34, 60, 36);
        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        _text->Width = 200;
        _text->Height = 50;

        layout.FitTo(_text);
        var firstX = _nineGrid->AtkResNode.X;
        var firstY = _nineGrid->AtkResNode.Y;

        layout.FitTo(_text);
        layout.FitTo(_text);

        Assert.Equal(firstX, _nineGrid->AtkResNode.X);
        Assert.Equal(firstY, _nineGrid->AtkResNode.Y);
        Assert.Equal(227, _nineGrid->AtkResNode.Width);
        Assert.Equal(65, _nineGrid->AtkResNode.Height);
    }

    [Fact]
    public void FitTo_WithoutANineGrid_SizesOnlyTheBubbleNode()
    {
        // A bubble style with no nine-grid asset: the res node is the only background to size.
        Arrange(124, 21, 151, 36, 100, 50, 0, 0, 60, 36);
        var layout = BubbleLayout.Capture(_text, nineGrid: null, _bubble);

        _text->Width = 84;
        _text->Height = 21;

        var fitted = layout.FitTo(_text);

        Assert.Equal(84, _bubble->Width);
        Assert.Equal(36, _bubble->Height);
        Assert.Equal(84, fitted.Width);
        Assert.Equal(36, fitted.Height);
    }

    [Fact]
    public void Restore_PutsTheCapturedGeometryBack()
    {
        Arrange(124, 21, 151, 36, 100, 50, 36, 34, 60, 36);
        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        _text->Width = 200;
        _text->Height = 50;
        layout.FitTo(_text);
        layout.Restore();

        Assert.Equal(151, _nineGrid->AtkResNode.Width);
        Assert.Equal(36, _nineGrid->AtkResNode.Height);
        Assert.Equal(100f, _nineGrid->AtkResNode.X);
        Assert.Equal(50f, _nineGrid->AtkResNode.Y);
        Assert.Equal(60, _bubble->Width);
        Assert.Equal(36, _bubble->Height);
    }

    [Fact]
    public void Capture_SplitsTheCornerOffsetsAcrossBothSides()
    {
        Arrange(100, 20, 127, 35, 10, 20, capsWidth: 27, capsHeight: 15, 50, 30);

        var layout = BubbleLayout.Capture(_text, _nineGrid, _bubble);

        Assert.Equal(27, layout.CapsWidth);
        Assert.Equal(15, layout.CapsHeight);
    }

    private void Arrange(
        int englishWidth,
        int englishHeight,
        int backgroundWidth,
        int backgroundHeight,
        float x,
        float y,
        int capsWidth,
        int capsHeight,
        int bubbleWidth,
        int bubbleHeight)
    {
        _text->Width = (ushort)englishWidth;
        _text->Height = (ushort)englishHeight;
        _text->X = x;
        _text->Y = y;

        _nineGrid->AtkResNode.Width = (ushort)backgroundWidth;
        _nineGrid->AtkResNode.Height = (ushort)backgroundHeight;
        _nineGrid->AtkResNode.X = x;
        _nineGrid->AtkResNode.Y = y;
        _nineGrid->LeftOffset = (short)(capsWidth / 2);
        _nineGrid->RightOffset = (short)(capsWidth - capsWidth / 2);
        _nineGrid->TopOffset = (short)(capsHeight / 2);
        _nineGrid->BottomOffset = (short)(capsHeight - capsHeight / 2);

        _bubble->Width = (ushort)bubbleWidth;
        _bubble->Height = (ushort)bubbleHeight;
    }
}
