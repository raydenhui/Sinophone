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
    public void Convert_UsesTheTraditionalFormsTheTcDataUses()
    {
        // OpenCC's base table keeps older variants (爲, 裏, 纔). The TC game data
        // uses the Taiwan forms, so the conversion must not put the old ones back.
        Assert.Equal("剛才", ChineseConverter.Convert("刚才", ChineseConversion.SimplifiedToTraditional));
        Assert.Equal("裡面", ChineseConverter.Convert("里面", ChineseConversion.SimplifiedToTraditional));
        Assert.Equal("行為", ChineseConverter.Convert("行为", ChineseConversion.SimplifiedToTraditional));
    }

    [Fact]
    public void Convert_LeavesTraditionalTextUnchanged_WhenAskedForTraditional()
    {
        // The setting can be on while the selected variant is already traditional.
        // Converting that text again would corrupt shared characters.
        const string traditional = "「行會令」指的是冒險者行會發行的以小隊為對象的特殊任務。";

        Assert.Equal(traditional, ChineseConverter.Convert(traditional, ChineseConversion.SimplifiedToTraditional));
    }

    [Fact]
    public void Convert_LeavesSimplifiedTextUnchanged_WhenAskedForSimplified()
    {
        const string simplified = "「行会令」指的是冒险者行会发行的以小队为对象的特殊任务。";

        Assert.Equal(simplified, ChineseConverter.Convert(simplified, ChineseConversion.TraditionalToSimplified));
    }

    [Fact]
    public void Convert_ConvertsMixedText_WhenMostCharactersNeedIt()
    {
        Assert.Equal("這是簡體", ChineseConverter.Convert("這是简体", ChineseConversion.SimplifiedToTraditional));
    }

    [Fact]
    public void Convert_ConvertsPhrasesMadeOfSharedCharacters()
    {
        // 只 and 身 are the same in both scripts, so only the phrase shows that the
        // line is simplified. The TC data writes 隻身.
        Assert.Equal("他隻身一人", ChineseConverter.Convert("他只身一人", ChineseConversion.SimplifiedToTraditional));
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
