using Sinophone.Data;

namespace Sinophone.Tests;

/// <summary>
/// The conversion layer runs over the official translation, so these tests use text
/// that appears in the game data rather than single characters.
/// </summary>
public class ChineseConverterTests
{
    [Fact]
    public void Convert_ChangesSimplifiedToTraditional_WhenAsked()
    {
        Assert.Equal("簡體字", ChineseConverter.Convert("简体字", ChineseConversion.SimplifiedToTraditional));
    }

    [Fact]
    public void Convert_ChangesTraditionalToSimplified_WhenAsked()
    {
        Assert.Equal("简体字", ChineseConverter.Convert("簡體字", ChineseConversion.TraditionalToSimplified));
    }

    [Fact]
    public void Convert_UsesWholeWords_NotSingleCharacters()
    {
        // 头 alone becomes 頭 and 发 alone becomes 發, so a character-only table
        // would produce 頭發. The phrase table keeps the right character.
        Assert.Equal("頭髮", ChineseConverter.Convert("头发", ChineseConversion.SimplifiedToTraditional));
    }

    [Fact]
    public void Convert_LeavesTextUnchanged_WhenDirectionIsNone()
    {
        const string text = "「行會令」指的是冒險者行會發行的特殊任務。";

        Assert.Equal(text, ChineseConverter.Convert(text, ChineseConversion.None));
    }

    [Fact]
    public void Convert_LeavesLatinTextUnchanged()
    {
        const string text = "Hello, world. 123";

        Assert.Equal(text, ChineseConverter.Convert(text, ChineseConversion.SimplifiedToTraditional));
        Assert.Equal(text, ChineseConverter.Convert(text, ChineseConversion.TraditionalToSimplified));
    }

    [Fact]
    public void Convert_HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, ChineseConverter.Convert(null, ChineseConversion.SimplifiedToTraditional));
        Assert.Equal(string.Empty, ChineseConverter.Convert(string.Empty, ChineseConversion.TraditionalToSimplified));
    }

    [Fact]
    public void Convert_ConvertsEveryLineOfAFullTranslation()
    {
        const string traditional = "「行會令」指的是冒險者行會發行的\r\n以小隊為對象的特殊任務。";
        const string simplified = "「行会令」指的是冒险者行会发行的\r\n以小队为对象的特殊任务。";

        Assert.Equal(simplified, ChineseConverter.Convert(traditional, ChineseConversion.TraditionalToSimplified));
    }

    [Fact]
    public void IsAvailable_IsTrue_ForBothDirections()
    {
        Assert.True(ChineseConverter.IsAvailable(ChineseConversion.SimplifiedToTraditional));
        Assert.True(ChineseConverter.IsAvailable(ChineseConversion.TraditionalToSimplified));
        Assert.True(ChineseConverter.IsAvailable(ChineseConversion.None));
    }
}
