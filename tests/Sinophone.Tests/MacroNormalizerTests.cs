using Sinophone.Data;

namespace Sinophone.Tests;

public class MacroNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeKey_ReturnsEmpty_ForBlankInput(string? input)
        => Assert.Equal(string.Empty, MacroNormalizer.NormalizeKey(input));

    [Fact]
    public void NormalizeKey_StripsSeStringMacros()
    {
        var actual = MacroNormalizer.NormalizeKey("Hello <b>there</b>");
        Assert.Equal("Hello there", actual);
    }

    [Fact]
    public void NormalizeKey_StripsLineBreakMacros()
    {
        // <br> becomes whitespace, then collapses to a single space.
        var actual = MacroNormalizer.NormalizeKey("First line<br>Second line");
        Assert.Equal("First line Second line", actual);
    }

    [Fact]
    public void NormalizeKey_CollapsesLineBreaksToSingleSpace()
    {
        var actual = MacroNormalizer.NormalizeKey("One\r\nTwo\nThree");
        Assert.Equal("One Two Three", actual);
    }

    [Fact]
    public void NormalizeKey_CollapsesRepeatedWhitespace()
    {
        var actual = MacroNormalizer.NormalizeKey("a     b\t\tc");
        Assert.Equal("a b c", actual);
    }

    [Fact]
    public void NormalizeKey_TrimsLeadingAndTrailingWhitespace()
    {
        var actual = MacroNormalizer.NormalizeKey("  padded  ");
        Assert.Equal("padded", actual);
    }

    [Fact]
    public void NormalizeKey_RemovesInvisibleCharacters()
    {
        // soft hyphen, zero-width space, ZWNJ, ZWJ, BOM
        var input = "a­b​c‌d‍e﻿f";
        var actual = MacroNormalizer.NormalizeKey(input);
        Assert.Equal("abcdef", actual);
    }

    [Fact]
    public void NormalizeKey_StripsReplacementCharacters_WhenTextContainsDiamonds()
    {
        // Malformed UTF-8 decodes to U+FFFD. Lumina text has no replacement
        // characters, so they must not survive into the dictionary key.
        var actual = MacroNormalizer.NormalizeKey("I'm the hired captain of the �Orion�.");
        Assert.Equal("I'm the hired captain of the Orion.", actual);
    }

    [Fact]
    public void NormalizeKey_StripsSeStringControlMarkers_WhenTextContainsObliqueWords()
    {
        // The Talk addon wraps oblique words in raw SeString control bytes.
        // These are the exact code points logged around "Orion".
        const string fromGame =
            "I'm the hired captain of the \u0002\u001a\u0002\u0002\u0003Orion\u0002\u001a\u0002\u0001\u0003. " +
            "She's a fine vessel.";
        const string fromSheet =
            "I'm the hired captain of the Orion. She's a fine vessel.";

        Assert.Equal(fromSheet, MacroNormalizer.NormalizeKey(fromGame));
    }

    [Fact]
    public void NormalizeKey_PreservesTabsAndLineBreaks_WhenStrippingControls()
    {
        // Control stripping must not eat the whitespace that key collapsing
        // and display line breaks rely on.
        var actual = MacroNormalizer.NormalizeKey("a\tb\nc\rd");
        Assert.Equal("a b c d", actual);
    }

    [Fact]
    public void NormalizeKey_TreatsNonBreakingSpaceAsSpace()
    {
        var actual = MacroNormalizer.NormalizeKey("a b");
        Assert.Equal("a b", actual);
    }

    [Fact]
    public void NormalizeKey_KeepsChineseText()
    {
        var actual = MacroNormalizer.NormalizeKey("歡迎來到拂曉血盟。");
        Assert.Equal("歡迎來到拂曉血盟。", actual);
    }

    [Fact]
    public void NormalizeKey_IsStableAcrossEquivalentInputs()
    {
        // Game text and Lumina text differ in whitespace and macros but should
        // produce the same key. This is the whole point of normalization.
        const string fromGame = "Hey,  did you know?<br>There's a place.";
        const string fromSheet = "Hey, did you know?\r\nThere's a place.";

        Assert.Equal(
            MacroNormalizer.NormalizeKey(fromGame),
            MacroNormalizer.NormalizeKey(fromSheet));
    }

    [Fact]
    public void CleanForDisplay_PreservesLineBreaks()
    {
        var actual = MacroNormalizer.CleanForDisplay("第一行<br>第二行");
        Assert.Equal("第一行\n第二行", actual);
    }

    [Fact]
    public void CleanForDisplay_NormalizesCarriageReturns()
    {
        var actual = MacroNormalizer.CleanForDisplay("第一行\r\n第二行\r第三行");
        Assert.Equal("第一行\n第二行\n第三行", actual);
    }

    [Fact]
    public void CleanForDisplay_StripsMacros()
    {
        var actual = MacroNormalizer.CleanForDisplay("你好<b>世界</b>");
        Assert.Equal("你好世界", actual);
    }

    [Fact]
    public void CleanForDisplay_TrimsTrailingWhitespacePerLine()
    {
        var actual = MacroNormalizer.CleanForDisplay("第一行   \n第二行　");
        Assert.Equal("第一行\n第二行", actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CleanForDisplay_ReturnsEmpty_ForBlankInput(string? input)
        => Assert.Equal(string.Empty, MacroNormalizer.CleanForDisplay(input));

    [Fact]
    public void CleanForDisplay_KeepsInnerWhitespace()
    {
        var actual = MacroNormalizer.CleanForDisplay("a  b");
        Assert.Equal("a  b", actual);
    }

    [Fact]
    public void CleanForDisplay_ResolvesConditional_WhenEvaluatorIsProvided()
    {
        var actual = MacroNormalizer.CleanForDisplay(
            "<If(PlayerParameter(4))>大姐<Else/>小哥</If>，<br>你好。",
            _ => false);

        Assert.Equal("小哥，\n你好。", actual);
    }

    [Fact]
    public void CleanForDisplay_UsesFirstBranch_WhenConditionIsUnknown()
    {
        var actual = MacroNormalizer.CleanForDisplay("<If(X)>甲<Else/>乙</If>");

        Assert.Equal("甲", actual);
    }
}
