using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The checks a case runs on a drawn line. Each test names the property it protects, so a
/// failure tells you which part of the display broke rather than "the run failed".
/// </summary>
public class HarnessAssertionsTests
{
    [Theory]
    [InlineData("Hello")]
    [InlineData("你好，世界。")]
    [InlineData("Line one<br>line two")]
    [InlineData("")]
    [InlineData(null)]
    public void HasMacroLeak_IsFalse_ForTextWithNoMacro(string? text)
        => Assert.False(HarnessAssertions.HasMacroLeak(text));

    [Theory]
    [InlineData("<Sheet(ClassJob,IntegerParameter(1),0)> Oath")]
    [InlineData("You obtained <StringParameter(1)>.", "StringParameter")]
    [InlineData("<Unknown60>0262</Unknown60>")]
    [InlineData("At <Time(...)/> we leave.")]
    [InlineData("<If(PlayerParameter(4))>him<Else/>her<EndIf>")]
    [InlineData("<EnNoun(1)>")]
    public void HasMacroLeak_IsTrue_ForTextWithAMacro(string text)
        => Assert.True(HarnessAssertions.HasMacroLeak(text));

    [Fact]
    public void HasMacroLeak_IsFalse_ForABoldOrItalicMark()
        => Assert.False(HarnessAssertions.HasMacroLeak("**bold** and *italic*"));

    [Fact]
    public void NoMacro_Passes_ForPlainChinese()
    {
        var finding = HarnessAssertions.NoMacro("你好，世界。");

        Assert.True(finding.Passed);
        Assert.Equal(HarnessCheck.NoMacro, finding.Check);
    }

    [Fact]
    public void NoMacro_Fails_AndNamesTheTag()
    {
        var finding = HarnessAssertions.NoMacro("你好 <Sheet(Item,1,0)>");

        Assert.False(finding.Passed);
        Assert.Contains("Sheet", finding.Detail);
    }

    [Fact]
    public void NoMacro_ReportsTheExcerptAroundTheTag()
    {
        var text = string.Concat(new string('一', 40), "<StringParameter(2)>");

        var finding = HarnessAssertions.NoMacro(text);

        Assert.False(finding.Passed);
        Assert.Contains("StringParameter", finding.Detail);
    }

    [Fact]
    public void NoLeadingClosingMark_Passes_WhenEveryLineStartsCleanly()
    {
        var finding = HarnessAssertions.NoLeadingClosingMark(["你好，世界", "第二行"]);

        Assert.True(finding.Passed);
    }

    [Theory]
    [InlineData("。第二行")]
    [InlineData("，第二行")]
    [InlineData("」第二行")]
    [InlineData("…第二行")]
    public void NoLeadingClosingMark_Fails_WhenALineStartsWithAClosingMark(string line)
    {
        var finding = HarnessAssertions.NoLeadingClosingMark(["第一行", line]);

        Assert.False(finding.Passed);
        Assert.Contains("Line 2", finding.Detail);
    }

    [Fact]
    public void NoLeadingClosingMark_Passes_ForNoLines()
    {
        Assert.True(HarnessAssertions.NoLeadingClosingMark(null).Passed);
        Assert.True(HarnessAssertions.NoLeadingClosingMark([]).Passed);
    }

    [Fact]
    public void NoLeadingClosingMark_SkipsEmptyLines()
    {
        var finding = HarnessAssertions.NoLeadingClosingMark(["", "第二行"]);

        Assert.True(finding.Passed);
    }

    [Fact]
    public void Fits_Passes_WhenTheTextIsSmaller()
    {
        var finding = HarnessAssertions.Fits(100, 40, 200, 80);

        Assert.True(finding.Passed);
        Assert.Contains("100x40", finding.Detail);
    }

    [Fact]
    public void Fits_Passes_WhenTheTextIsExactlyTheBox()
        => Assert.True(HarnessAssertions.Fits(200, 80, 200, 80).Passed);

    [Fact]
    public void Fits_Fails_WhenTheTextIsWider()
    {
        var finding = HarnessAssertions.Fits(201, 40, 200, 80);

        Assert.False(finding.Passed);
        Assert.Contains("201x40", finding.Detail);
    }

    [Fact]
    public void Fits_Fails_WhenTheTextIsTaller()
        => Assert.False(HarnessAssertions.Fits(100, 81, 200, 80).Passed);

    [Fact]
    public void Fits_Passes_WithNoMeasurement_BecauseItIsASkip()
    {
        var finding = HarnessAssertions.Fits(0, 0, 0, 0);

        Assert.True(finding.Passed);
        Assert.Contains("No measurements", finding.Detail);
    }

    [Fact]
    public void Translated_Fails_WhenNothingWasDrawn()
        => Assert.False(HarnessAssertions.Translated(null, null, "Hello").Passed);

    [Fact]
    public void Translated_Fails_WhenTheEnglishLineWasDrawn()
    {
        var finding = HarnessAssertions.Translated("Hello", null, "Hello");

        Assert.False(finding.Passed);
        Assert.Contains("no translation reached", finding.Detail);
    }

    [Fact]
    public void Translated_Passes_WhenTheTextDiffersFromTheEnglish()
        => Assert.True(HarnessAssertions.Translated("你好", null, "Hello").Passed);

    [Fact]
    public void Translated_Passes_WhenItMatchesTheExpectedChinese()
        => Assert.True(HarnessAssertions.Translated("你好", "你好", "Hello").Passed);

    [Fact]
    public void Translated_Fails_WhenItDoesNotMatchTheExpectedChinese()
    {
        var finding = HarnessAssertions.Translated("你好", "再見", "Hello");

        Assert.False(finding.Passed);
        Assert.Contains("再見", finding.Detail);
        Assert.Contains("你好", finding.Detail);
    }

    [Fact]
    public void NoRevert_Passes_WithNoReplacement()
        => Assert.True(HarnessAssertions.NoRevert(0).Passed);

    [Fact]
    public void NoRevert_Fails_WhenTheGameReplacedTheTranslation()
    {
        var finding = HarnessAssertions.NoRevert(3);

        Assert.False(finding.Passed);
        Assert.Contains("3", finding.Detail);
    }

    [Fact]
    public void Run_ReturnsOnlyTheRequestedChecks()
    {
        var measurement = new HarnessMeasurement
        {
            Drawn = "你好",
            English = "Hello",
            TextWidth = 10,
            TextHeight = 10,
            BoxWidth = 100,
            BoxHeight = 100,
        };

        var findings = HarnessAssertions.Run(HarnessCheck.Translated | HarnessCheck.Fits, measurement);

        Assert.Equal(
            new[] { HarnessCheck.Translated, HarnessCheck.Fits },
            findings.Select(static finding => finding.Check).ToArray());
    }

    [Fact]
    public void Run_ReturnsFindingsInAFixedOrder()
    {
        var measurement = new HarnessMeasurement { Drawn = "你好", English = "Hello" };

        var findings = HarnessAssertions.Run(HarnessCheck.All, measurement);

        Assert.Equal(
            new[]
            {
                HarnessCheck.Translated,
                HarnessCheck.NoMacro,
                HarnessCheck.Fits,
                HarnessCheck.NoLeadingClosingMark,
                HarnessCheck.NoRevert,
                HarnessCheck.Glyphs,
            },
            findings.Select(static finding => finding.Check).ToArray());
    }

    [Fact]
    public void Run_PassesEveryCheck_ForACleanTranslation()
    {
        var measurement = new HarnessMeasurement
        {
            Drawn = "你好，世界。",
            English = "Hello, world.",
            TextWidth = 120,
            TextHeight = 30,
            BoxWidth = 300,
            BoxHeight = 100,
            RenderedLines = ["你好，世界。"],
            Reverts = 0,
            MissingGlyphs = 0,
        };

        var findings = HarnessAssertions.Run(HarnessCheck.All, measurement);

        Assert.All(findings, static finding => Assert.True(finding.Passed, finding.Detail));
    }

    [Fact]
    public void Run_ReportsGlyphsAsFailed_WhenTheFontHasNoGlyph()
    {
        var measurement = new HarnessMeasurement
        {
            Drawn = "歷閱臺灣",
            English = "Taiwan",
            MissingGlyphs = 2,
        };

        var findings = HarnessAssertions.Run(HarnessCheck.Glyphs, measurement);

        Assert.False(findings[0].Passed);
        Assert.Contains("2", findings[0].Detail);
    }

    [Fact]
    public void ExplicitLines_SplitsOnTheTextBreak()
        => Assert.Equal(new[] { "一", "二" }, HarnessAssertions.ExplicitLines("一\n二").ToArray());

    [Fact]
    public void ExplicitLines_ReturnsNothing_ForEmptyText()
    {
        Assert.Empty(HarnessAssertions.ExplicitLines(null));
        Assert.Empty(HarnessAssertions.ExplicitLines(string.Empty));
    }
}
