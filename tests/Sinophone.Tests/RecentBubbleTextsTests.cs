using System.Text;

using Sinophone.Handlers;

namespace Sinophone.Tests;

public class RecentBubbleTextsTests
{
    [Fact]
    public void Contains_TrueForARememberedLine()
    {
        var recent = new RecentBubbleTexts();
        recent.Remember("你好");

        Assert.True(recent.Contains(Encoding.UTF8.GetBytes("你好")));
    }

    [Fact]
    public void Contains_FalseForAnotherLine()
    {
        var recent = new RecentBubbleTexts();
        recent.Remember("你好");

        Assert.False(recent.Contains(Encoding.UTF8.GetBytes("您好")));
    }

    [Fact]
    public void Contains_IsExact()
    {
        var recent = new RecentBubbleTexts();
        recent.Remember("你好");

        Assert.False(recent.Contains(Encoding.UTF8.GetBytes("你好 ")));
        Assert.False(recent.Contains(Encoding.UTF8.GetBytes("你")));
    }

    [Fact]
    public void Contains_FalseForAnEmptyLine()
    {
        var recent = new RecentBubbleTexts();
        recent.Remember(string.Empty);

        Assert.False(recent.Contains(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Remember_KeepsTheLastEightLines()
    {
        var recent = new RecentBubbleTexts();
        for (var i = 0; i < 8; i++)
            recent.Remember($"第{i}行");

        // A ninth line pushes the first one out.
        recent.Remember("第九行");

        Assert.False(recent.Contains(Encoding.UTF8.GetBytes("第0行")));
        for (var i = 1; i < 8; i++)
            Assert.True(recent.Contains(Encoding.UTF8.GetBytes($"第{i}行")));

        Assert.True(recent.Contains(Encoding.UTF8.GetBytes("第九行")));
    }

    [Fact]
    public void Remember_KeepsWorkingAfterTheRingWraps()
    {
        var recent = new RecentBubbleTexts();
        for (var i = 0; i < 20; i++)
            recent.Remember($"第{i}行");

        for (var i = 12; i < 20; i++)
            Assert.True(recent.Contains(Encoding.UTF8.GetBytes($"第{i}行")));

        Assert.False(recent.Contains(Encoding.UTF8.GetBytes("第11行")));
    }
}
