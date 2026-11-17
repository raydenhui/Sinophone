using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// The Chinese line is wrapped before the game lays it out, so a closing mark never starts a
/// rendered line. The measure is one unit per character, which makes the break points the test's.
/// </summary>
public class CjkLineWrapperTests
{
    private static readonly Func<string, int> Characters = static text => text.Length;

    [Fact]
    public void Wrap_LeavesTextThatFits()
        => Assert.Equal("一二三", CjkLineWrapper.Wrap("一二三", 5, Characters));

    [Fact]
    public void Wrap_BreaksAtTheWidth()
        => Assert.Equal("一二三四五\n六七八", CjkLineWrapper.Wrap("一二三四五六七八", 5, Characters));

    [Fact]
    public void Wrap_DoesNotStartALineWithAClosingMark()
        => Assert.Equal("一二三四\n五。六", CjkLineWrapper.Wrap("一二三四五。六", 5, Characters));

    [Fact]
    public void Wrap_DoesNotEndALineWithAnOpeningMark()
        => Assert.Equal("一二三四「五\n六", CjkLineWrapper.Wrap("一二三四「五六", 5, Characters));

    [Fact]
    public void Wrap_KeepsExistingLineBreaks()
        => Assert.Equal("一二\n三四", CjkLineWrapper.Wrap("一二\n三四", 5, Characters));

    [Fact]
    public void Wrap_WrapsEachParagraph()
        => Assert.Equal("一二三四五\n六\n七八九十", CjkLineWrapper.Wrap("一二三四五六\n七八九十", 5, Characters));

    [Fact]
    public void Wrap_ReturnsText_WhenTheWidthIsNotUsable()
    {
        Assert.Equal("一二三", CjkLineWrapper.Wrap("一二三", 0, Characters));
        Assert.Equal("一二三", CjkLineWrapper.Wrap("一二三", -1, Characters));
    }

    [Fact]
    public void Wrap_HandlesEmptyText()
    {
        Assert.Equal(string.Empty, CjkLineWrapper.Wrap(string.Empty, 5, Characters));
        Assert.Equal(string.Empty, CjkLineWrapper.Wrap(null, 5, Characters));
    }

    [Fact]
    public void Wrap_EmitsSingleCharacters_WhenOneCharacterIsWiderThanTheWidth()
        => Assert.Equal("一\n二\n三", CjkLineWrapper.Wrap("一二三", 1, Characters));

    [Fact]
    public void Wrap_KeepsTheFullStopWithTheLastCharacter_ForTheReportedLine()
    {
        // The sentence that put its full stop on the next rendered line with the CJK font.
        const string line = "每次像這樣眺望大海的時候，想要探險的心就會躍躍欲試。";

        var wrapped = CjkLineWrapper.Wrap(line, 26, Characters);

        foreach (var part in wrapped.Split('\n'))
            Assert.DoesNotContain(part[0].ToString(), "。，、；：？！");
    }
}
