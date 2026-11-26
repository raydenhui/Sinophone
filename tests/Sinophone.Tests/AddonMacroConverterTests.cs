using Lumina.Text.ReadOnly;
using Sinophone.DataPrep;

namespace Sinophone.Tests;

/// <summary>
/// The Chinese Addon dumps use SaintCoinach notation. The converter turns the rows
/// the plugin needs into the macro string Lumina parses. The samples are real rows
/// from the CN dump.
/// </summary>
public class AddonMacroConverterTests
{
    [Fact]
    public void ConvertsFormatToKilo()
    {
        // Addon row 17: a number followed by a private-use icon.
        Assert.True(AddonMacroConverter.TryConvert(
            "<Format(IntegerParameter(1),FF022C)/>\uE049", out var macro));

        Assert.Equal("<kilo(lnum1,\\,)>\uE049", macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsTimeAndDatePlaceholders()
    {
        // Addon row 21: a date and clock drawn from the timestamp the game passes.
        Assert.True(AddonMacroConverter.TryConvert(
            "<Time(IntegerParameter(1))/>" +
            "<Value>TopLevelParameter(222)</Value>/" +
            "<Value>TopLevelParameter(221)</Value>/" +
            "<Value>TopLevelParameter(219)</Value> " +
            "<Value>TopLevelParameter(218)</Value>:" +
            "<TwoDigitValue>TopLevelParameter(217)</TwoDigitValue>",
            out var macro));

        Assert.Equal(
            "<settime(lnum1)>" +
            "<num(t_year)>/<num(t_mon)>/<num(t_day)> " +
            "<num(t_hour)>:<sec(t_min)>",
            macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsIfWithANestedFormat()
    {
        // Addon row 235: "9,999+" above a cap, the value below it.
        Assert.True(AddonMacroConverter.TryConvert(
            "<If(GreaterThan(IntegerParameter(1),9999))>9,999+" +
            "<Else/><Format(IntegerParameter(1),FF022C)/></If>",
            out var macro));

        Assert.Equal("<if([lnum1>9999],9\\,999+,<kilo(lnum1,\\,)>)>", macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsIfWithoutElse()
    {
        Assert.True(AddonMacroConverter.TryConvert(
            "<If(GreaterThan(IntegerParameter(1),0))>yes</If>", out var macro));

        Assert.Equal("<if([lnum1>0],yes,)>", macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsZeroPaddedValue()
    {
        // Addon row 1143: two digits when the value is non-zero, a percent sign after.
        Assert.True(AddonMacroConverter.TryConvert(
            "<If(GreaterThan(IntegerParameter(1),0))>" +
            "<ZeroPaddedValue(2)>IntegerParameter(1)</ZeroPaddedValue>" +
            "<Else/><Value>IntegerParameter(1)</Value></If>." +
            "<Value>IntegerParameter(2)</Value>% ",
            out var macro));

        Assert.Equal(
            "<if([lnum1>0],<digit(lnum1,2)>,<num(lnum1)>)>.<num(lnum2)>% ",
            macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsSheetMacros()
    {
        // Addon row 6413: "Plant <item> in <item>?"
        Assert.True(AddonMacroConverter.TryConvert(
            "确定要将<Sheet(Item,IntegerParameter(2),0)/>种植在" +
            "<Sheet(Item,IntegerParameter(1),0)/>中吗？",
            out var macro));

        Assert.Equal(
            "确定要将<sheet(Item,lnum2,0)>种植在<sheet(Item,lnum1,0)>中吗？",
            macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsSwitchCases()
    {
        Assert.True(AddonMacroConverter.TryConvert(
            "<Switch(IntegerParameter(1))><Case(1)>A</Case><Case(2)>B</Case></Switch>",
            out var macro));

        Assert.Equal("<switch(lnum1,A,B)>", macro);
        AssertParses(macro);
    }

    [Fact]
    public void ConvertsAColorType()
    {
        Assert.True(AddonMacroConverter.TryConvert(
            "<UIForeground>F201FC</UIForeground>Hi", out var macro));

        Assert.Equal("<colortype(508)>Hi", macro);
        AssertParses(macro);
    }

    [Fact]
    public void EscapesLiteralAngleText()
    {
        Assert.True(AddonMacroConverter.TryConvert("Press <t> now", out var macro));

        Assert.Equal("Press \\<t\\> now", macro);
        AssertParses(macro);
    }

    [Fact]
    public void RejectsATagItDoesNotKnow()
    {
        Assert.False(AddonMacroConverter.TryConvert("<Bogus/>", out _));
    }

    [Fact]
    public void RejectsAnUnknownPlaceholder()
    {
        Assert.False(AddonMacroConverter.TryConvert(
            "<Value>TopLevelParameter(100)</Value>", out _));
    }

    private static void AssertParses(string macro)
        => _ = ReadOnlySeString.FromMacroString(macro);
}
