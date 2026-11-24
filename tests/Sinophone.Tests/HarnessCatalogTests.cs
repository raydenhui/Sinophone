using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// Case building: the fixed sample, the filters that keep markup and very long lines out, and
/// the length labels a run filters on.
/// </summary>
public class HarnessCatalogTests
{
    [Fact]
    public void BuiltIn_HasCasesForBothImplementedSurfaces()
    {
        Assert.Contains(HarnessCatalog.BuiltIn, static harnessCase => harnessCase.Surface == HarnessSurface.Talk);
        Assert.Contains(HarnessCatalog.BuiltIn, static harnessCase => harnessCase.Surface == HarnessSurface.ChatBubble);
    }

    [Fact]
    public void BuiltIn_IdsAreUnique()
    {
        var ids = HarnessCatalog.BuiltIn.Select(static harnessCase => harnessCase.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BuiltIn_HasEveryLengthTag()
    {
        Assert.Contains(HarnessCatalog.BuiltIn, static harnessCase => harnessCase.HasTag("short"));
        Assert.Contains(HarnessCatalog.BuiltIn, static harnessCase => harnessCase.HasTag("typical"));
        Assert.Contains(HarnessCatalog.BuiltIn, static harnessCase => harnessCase.HasTag("long"));
    }

    [Theory]
    [InlineData("Short line.", "short")]
    [InlineData("", "short")]
    [InlineData("A line that is one hundred and sixty characters or fewer and is counted as a typical dialogue line.", "typical")]
    public void LengthTag_NamesTheLength(string english, string expected)
        => Assert.Equal(expected, HarnessCatalog.LengthTag(english));

    [Fact]
    public void LengthTag_CallsAVeryLongLineLong()
        => Assert.Equal("long", HarnessCatalog.LengthTag(new string('a', 161)));

    [Fact]
    public void FromPairs_BuildsOneCasePerPair()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk,
        [
            ("Hello", "你好"),
            ("Bye", "再見"),
        ]);

        Assert.Equal(2, cases.Count);
        Assert.Equal(["Hello", "Bye"], cases.Select(static harnessCase => harnessCase.English));
        Assert.Equal(["你好", "再見"], cases.Select(static harnessCase => harnessCase.ExpectedChinese));
    }

    [Fact]
    public void FromPairs_UsesTheSurfaceInTheId()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.ChatBubble, [("Hello", "你好")]);

        Assert.StartsWith("chatbubble-", cases[0].Id);
    }

    [Fact]
    public void FromPairs_TagsEachCaseByLength()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk,
        [
            ("Hi", "嗨"),
            (new string('a', 200), "长"),
        ]);

        Assert.True(cases[0].HasTag("short"));
        Assert.True(cases[1].HasTag("long"));
    }

    [Fact]
    public void FromPairs_StopsAtTheLimit()
    {
        var pairs = Enumerable.Range(0, 50).Select(index => ($"Line {index}", $"行{index}"));

        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk, pairs, limit: 5);

        Assert.Equal(5, cases.Count);
    }

    [Fact]
    public void FromPairs_DropsEmptyHalves()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk,
        [
            ("Hello", ""),
            ("", "你好"),
            ("Bye", "再見"),
        ]);

        Assert.Equal(["Bye"], cases.Select(static harnessCase => harnessCase.English));
    }

    [Fact]
    public void FromPairs_DropsMacroLines()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk,
        [
            ("<Sheet(Item,1,0)> crate", "箱子"),
            ("Plain line", "普通的一行"),
        ]);

        Assert.Equal(["Plain line"], cases.Select(static harnessCase => harnessCase.English));
    }

    [Fact]
    public void FromPairs_DropsALineWhoseTranslationHoldsAMacro()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk,
        [
            ("Plain line", "<StringParameter(1)>你好"),
        ]);

        Assert.Empty(cases);
    }

    [Fact]
    public void FromPairs_DropsLinesOverTheLimit()
    {
        var pairs = new[] { (new string('a', 500), "长"), ("Short", "短") };

        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk, pairs, maxEnglish: 400);

        Assert.Equal(["Short"], cases.Select(static harnessCase => harnessCase.English));
    }

    [Fact]
    public void FromPairs_KeepsTheFirstOfADuplicateEnglishLine()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk,
        [
            ("Hello", "你好"),
            ("Hello", "您好"),
        ]);

        Assert.Single(cases);
        Assert.Equal("你好", cases[0].ExpectedChinese);
    }

    [Fact]
    public void FromPairs_WithNoPairs_ReturnsNothing()
        => Assert.Empty(HarnessCatalog.FromPairs(HarnessSurface.Talk, []));

    [Fact]
    public void FromPairs_WithAZeroLimit_ReturnsNothing()
    {
        var cases = HarnessCatalog.FromPairs(HarnessSurface.Talk, [("Hello", "你好")], limit: 0);

        Assert.Empty(cases);
    }

    [Fact]
    public void FromStore_WithNoStore_ReturnsNothing()
        => Assert.Empty(HarnessCatalog.FromStore(HarnessSurface.Talk, null));
}
