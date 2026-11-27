using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// The display rule for a translated chat bubble. The rule is pure numbers, so every case the
/// live nodes can meet is checked here instead of in the game.
/// </summary>
public class BubbleGeometryTests
{
    [Fact]
    public void Padding_KeepsTheMarginTheGameDrew()
    {
        // The issue's balloon: the game draws 27px beside a 124px line and 15px above/below a
        // 21px line.
        Assert.Equal(27, BubbleGeometry.Padding(151, 124, 34));
        Assert.Equal(15, BubbleGeometry.Padding(36, 21, 34));
    }

    [Fact]
    public void Padding_UsesTheCorners_WhenTheBackgroundIsSmallerThanTheEnglishLine()
    {
        // The game had not laid this line out: the difference is not a real margin.
        Assert.Equal(20, BubbleGeometry.Padding(100, 122, 20));
    }

    [Fact]
    public void Padding_UsesTheCorners_WhenTheBackgroundStillHoldsAnotherLine()
    {
        Assert.Equal(36, BubbleGeometry.Padding(212, 68, 36));
    }

    [Fact]
    public void Padding_UsesTheCorners_WhenTheMarginIsZero()
    {
        // A margin of zero leaves the text on the border. The corners are the safe value.
        Assert.Equal(34, BubbleGeometry.Padding(150, 150, 34));
    }

    [Fact]
    public void Padding_UsesTheMargin_WhenTheAssetHasNoCorners()
    {
        Assert.Equal(100, BubbleGeometry.Padding(200, 100, 0));
    }

    [Fact]
    public void Padding_UsesTheCorners_WhenTheEnglishSizeIsUnreadable()
    {
        // There is no English line to measure a margin from.
        Assert.Equal(20, BubbleGeometry.Padding(100, 0, 20));
        Assert.Equal(0, BubbleGeometry.Padding(100, 0, 0));
    }

    [Fact]
    public void Padding_NeverGoesBelowZero()
    {
        Assert.Equal(0, BubbleGeometry.Padding(100, 122, 0));
    }

    [Fact]
    public void FitSize_KeepsAShorterTranslationAtTheGameSize()
    {
        // The issue: English 124x21, Chinese 84x21, background 151x36, caps 36x34. Neither axis
        // may grow; the caps are not the padding.
        Assert.Equal(111, BubbleGeometry.FitSize(151, 124, 84, 36));
        Assert.Equal(36, BubbleGeometry.FitSize(36, 21, 21, 34));
    }

    [Fact]
    public void FitSize_ShrinksTheEmptyWidthOfTheEnglishLine()
    {
        // English 182 wide, background 209, Chinese 48: the 27px margin stays, nothing else does.
        Assert.Equal(75, BubbleGeometry.FitSize(209, 182, 48, 36));
        Assert.Equal(52, BubbleGeometry.FitSize(52, 37, 37, 34));
    }

    [Fact]
    public void FitSize_GrowsForALongerLineByTheSameMargin()
    {
        Assert.Equal(227, BubbleGeometry.FitSize(151, 124, 200, 36));
    }

    [Fact]
    public void FitSize_UsesTheCorners_WhenTheBackgroundDoesNotHoldTheEnglishLine()
    {
        // The captured background is not this line's layout, so the corners are the padding.
        Assert.Equal(174, BubbleGeometry.FitSize(100, 122, 154, 20));
        Assert.Equal(96, BubbleGeometry.FitSize(212, 68, 60, 36));
        Assert.Equal(55, BubbleGeometry.FitSize(68, 21, 21, 34));
    }

    [Fact]
    public void FitSize_UsesTheCornerFloor_WhenTheBackgroundMatchesTheEnglishLine()
    {
        // The margin is zero, so the corners are the only safe padding. Trusting the zero would
        // leave the Chinese on the border.
        Assert.Equal(184, BubbleGeometry.FitSize(150, 150, 150, 34));
    }

    [Fact]
    public void FitSize_KeepsTheCarriedMargin_WhenItIsSmallerThanTheCorners()
    {
        // A real margin does not have to fill the corners. 27px is the game's own padding.
        Assert.Equal(181, BubbleGeometry.FitSize(149, 122, 154, 36));
    }

    [Fact]
    public void FitSize_FollowsTheMargin_WhenTheAssetHasNoCorners()
    {
        // Without corners, a wider line still grows by the margin and a shorter one is left alone.
        Assert.Equal(250, BubbleGeometry.FitSize(200, 100, 150, 0));
        Assert.Equal(200, BubbleGeometry.FitSize(200, 100, 60, 0));
        Assert.Equal(154, BubbleGeometry.FitSize(100, 122, 154, 0));
    }

    [Fact]
    public void FitSize_HoldsTheText_WhenTheEnglishSizeIsUnreadable()
    {
        // There is no margin to carry. The corners are the only trusted padding.
        Assert.Equal(170, BubbleGeometry.FitSize(100, 0, 150, 20));
        Assert.Equal(100, BubbleGeometry.FitSize(100, 0, 50, 20));

        // No corners either: at least the text itself fits.
        Assert.Equal(150, BubbleGeometry.FitSize(100, 0, 150, 0));
    }

    [Fact]
    public void FitRect_LeavesTheGameLayout_WhenTheTextFits()
    {
        // The issue's first bubble at 151x36 on screen at (100, 50).
        var fitted = BubbleGeometry.FitRect(
            new BubbleRect(151, 36, 100, 50),
            englishWidth: 124,
            englishHeight: 21,
            chineseWidth: 84,
            chineseHeight: 21,
            capsWidth: 36,
            capsHeight: 34);

        Assert.Equal(new BubbleRect(111, 36, 100, 50), fitted);
    }

    [Fact]
    public void FitRect_GrowsFromBothSides_SoTheTailStaysCentred()
    {
        var background = new BubbleRect(151, 36, 100, 50);

        var fitted = BubbleGeometry.FitRect(background, 124, 21, 200, 50, 36, 34);

        Assert.Equal(227, fitted.Width);
        Assert.Equal(65, fitted.Height);
        Assert.Equal(62f, fitted.X);
        Assert.Equal(21f, fitted.Y);
        Assert.Equal(background.X + background.Width / 2f, fitted.X + fitted.Width / 2f);
        Assert.Equal(background.Y + background.Height, fitted.Y + fitted.Height);
    }

    [Fact]
    public void FitRect_ShrinksFromTheRight_SoTheLeftAlignedTextKeepsItsEdge()
    {
        var background = new BubbleRect(209, 52, 100, 50);

        var fitted = BubbleGeometry.FitRect(background, 182, 37, 48, 37, 36, 34);

        Assert.Equal(75, fitted.Width);
        Assert.Equal(52, fitted.Height);
        Assert.Equal(background.X, fitted.X);
        Assert.Equal(background.Y, fitted.Y);
    }

    [Fact]
    public void FitRect_KeepsTheBottomEdge_WhenTheBackgroundShrinks()
    {
        // English 68x21 against a stale 212x68: the fallback is 60 + caps, and the bottom stays.
        var background = new BubbleRect(212, 68, 100, 50);

        var fitted = BubbleGeometry.FitRect(background, 68, 21, 60, 21, 36, 34);

        Assert.Equal(96, fitted.Width);
        Assert.Equal(55, fitted.Height);
        Assert.Equal(background.X, fitted.X);
        Assert.Equal(63f, fitted.Y);
        Assert.Equal(background.Y + background.Height, fitted.Y + fitted.Height);
    }

    [Fact]
    public void FitRect_CentresAnOddGrowthExactly()
    {
        var fitted = BubbleGeometry.FitRect(new BubbleRect(100, 50, 10, 20), 100, 21, 101, 21, 30, 30);

        // Margin zero is a corner fallback, so the target is 101 + 30 = 131, a 31px growth.
        Assert.Equal(131, fitted.Width);
        Assert.Equal(-5.5f, fitted.X);
        Assert.Equal(10f + 100f / 2f, fitted.X + fitted.Width / 2f);
    }

    [Fact]
    public void FitRect_HoldsTheText_WhenTheEnglishSizeIsUnreadable()
    {
        var fitted = BubbleGeometry.FitRect(new BubbleRect(100, 36, 10, 20), 0, 0, 150, 50, 20, 20);

        Assert.Equal(170, fitted.Width);
        Assert.Equal(70, fitted.Height);
        Assert.Equal(-25f, fitted.X);
        Assert.Equal(-14f, fitted.Y);
    }

    /// <summary>
    /// Holds the rule over many generated bubbles. The examples above are hand-picked; these
    /// cases catch a rule that only works on them. The seed is fixed, so a failure is repeatable.
    /// </summary>
    [Fact]
    public void Fit_KeepsTheTextInside_ForEveryGeneratedBubble()
    {
        foreach (var (background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight) in Generate())
        {
            var fitted = BubbleGeometry.FitRect(
                background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight);

            Assert.True(fitted.Width >= chineseWidth, $"width {fitted.Width} < text {chineseWidth}");
            Assert.True(fitted.Height >= chineseHeight, $"height {fitted.Height} < text {chineseHeight}");
            Assert.True(fitted.Width >= 0);
            Assert.True(fitted.Height >= 0);
        }
    }

    [Fact]
    public void Fit_KeepsTheBottomEdge_ForEveryGeneratedBubble()
    {
        foreach (var (background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight) in Generate())
        {
            var fitted = BubbleGeometry.FitRect(
                background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight);

            Assert.Equal(background.Y + background.Height, fitted.Y + fitted.Height);
        }
    }

    [Fact]
    public void Fit_KeepsTheAnchors_ForEveryGeneratedBubble()
    {
        foreach (var (background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight) in Generate())
        {
            var fitted = BubbleGeometry.FitRect(
                background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight);

            if (fitted.Width > background.Width)
            {
                // Growth is centred, so the tail under the middle of the bubble does not move.
                Assert.Equal(
                    background.X + background.Width / 2f,
                    fitted.X + fitted.Width / 2f);
            }
            else
            {
                // A shrink keeps the left edge, so the left-aligned text never leaves the bubble.
                Assert.Equal(background.X, fitted.X);
            }
        }
    }

    [Fact]
    public void Fit_FollowsARealMargin_ForEveryGeneratedBubble()
    {
        foreach (var (background, englishWidth, englishHeight, chineseWidth, chineseHeight, capsWidth, capsHeight) in Generate())
        {
            var width = BubbleGeometry.FitSize(background.Width, englishWidth, chineseWidth, capsWidth);
            var height = BubbleGeometry.FitSize(background.Height, englishHeight, chineseHeight, capsHeight);

            var widthMargin = background.Width - englishWidth;
            if (englishWidth > 0 && capsWidth > 0 && widthMargin > 0 && widthMargin <= capsWidth)
                Assert.Equal(chineseWidth + widthMargin, width);

            var heightMargin = background.Height - englishHeight;
            if (englishHeight > 0 && capsHeight > 0 && heightMargin > 0 && heightMargin <= capsHeight)
                Assert.Equal(chineseHeight + heightMargin, height);
        }
    }

    private static IEnumerable<(BubbleRect Background, int EnglishWidth, int EnglishHeight, int ChineseWidth, int ChineseHeight, int CapsWidth, int CapsHeight)> Generate()
    {
        var random = new Random(20261127);

        for (var i = 0; i < 2_000; i++)
        {
            var englishWidth = random.Next(0, 401);
            var englishHeight = random.Next(0, 201);
            var chineseWidth = random.Next(0, 401);
            var chineseHeight = random.Next(0, 201);
            var capsWidth = random.Next(0, 81);
            var capsHeight = random.Next(0, 81);

            // Cover a real margin, a stale background, an unlaid background, and zero.
            var backgroundWidth = random.Next(0, 4) switch
            {
                0 => englishWidth + random.Next(1, Math.Max(capsWidth, 1) + 1),
                1 => englishWidth + capsWidth + random.Next(1, 101),
                2 => Math.Max(0, englishWidth - random.Next(0, 101)),
                _ => englishWidth,
            };
            var backgroundHeight = random.Next(0, 4) switch
            {
                0 => englishHeight + random.Next(1, Math.Max(capsHeight, 1) + 1),
                1 => englishHeight + capsHeight + random.Next(1, 101),
                2 => Math.Max(0, englishHeight - random.Next(0, 101)),
                _ => englishHeight,
            };

            var x = random.Next(-200, 201);
            var y = random.Next(-200, 201);

            yield return (
                new BubbleRect(backgroundWidth, backgroundHeight, x, y),
                englishWidth,
                englishHeight,
                chineseWidth,
                chineseHeight,
                capsWidth,
                capsHeight);
        }
    }
}
