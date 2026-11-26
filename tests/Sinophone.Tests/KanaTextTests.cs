using Sinophone.Data;

namespace Sinophone.Tests;

public class KanaTextTests
{
    [Theory]
    [InlineData("ステラ・ドラゴン")]
    [InlineData("ぁ")]
    [InlineData("ｶﾀｶﾅ")]
    [InlineData("ミ・コト")]
    public void HasKana_DetectsJapanese(string text)
        => Assert.True(KanaText.HasKana(text));

    [Theory]
    [InlineData("拜克・拉各")]
    [InlineData("你好")]
    [InlineData("ｰ")]
    [InlineData("Blade of the Behemoth")]
    public void HasKana_IgnoresTextThatOnlyLooksJapanese(string text)
        => Assert.False(KanaText.HasKana(text));

    [Fact]
    public void TryAddPreferred_AddsANewKey()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Equal(KanaTextAdd.Added, KanaText.TryAddPreferred(map, "Hello.", "你好。"));
        Assert.Equal("你好。", map["Hello."]);
    }

    [Fact]
    public void TryAddPreferred_KeepsTheFirstValue_WhenBothAreChinese()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["Hello."] = "你好。" };

        Assert.Equal(KanaTextAdd.Unchanged, KanaText.TryAddPreferred(map, "Hello.", "哈囉。"));
        Assert.Equal("你好。", map["Hello."]);
    }

    [Fact]
    public void TryAddPreferred_ReplacesJapaneseWithChinese()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["Stella"] = "ステラ" };

        Assert.Equal(KanaTextAdd.Replaced, KanaText.TryAddPreferred(map, "Stella", "斯特拉"));
        Assert.Equal("斯特拉", map["Stella"]);
    }

    [Fact]
    public void TryAddPreferred_KeepsChinese_WhenAJapaneseRowComesLater()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["Stella"] = "斯特拉" };

        Assert.Equal(KanaTextAdd.Unchanged, KanaText.TryAddPreferred(map, "Stella", "ステラ"));
        Assert.Equal("斯特拉", map["Stella"]);
    }

    [Fact]
    public void TryAddPreferred_KeepsJapanese_WhenNeitherRowIsLocalised()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["Stella"] = "ステラ" };

        Assert.Equal(KanaTextAdd.Unchanged, KanaText.TryAddPreferred(map, "Stella", "ステラ二号"));
        Assert.Equal("ステラ", map["Stella"]);
    }
}
