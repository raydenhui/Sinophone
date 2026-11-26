using Sinophone.Localization;

namespace Sinophone.Tests;

public class InterfaceLanguageTests
{
    [Theory]
    [InlineData("en", InterfaceLanguage.English)]
    [InlineData("ja", InterfaceLanguage.Japanese)]
    [InlineData("zh", InterfaceLanguage.SimplifiedChinese)]
    [InlineData("tw", InterfaceLanguage.TraditionalChinese)]
    public void FromDalamudCode_MapsTheFourLanguages(string code, InterfaceLanguage expected)
        => Assert.Equal(expected, InterfaceLanguageResolver.FromDalamudCode(code));

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ko")]
    [InlineData("ZH")]
    [InlineData("")]
    [InlineData(null)]
    public void FromDalamudCode_FallsBackToEnglish_ForOtherCodes(string? code)
        => Assert.Equal(InterfaceLanguage.English, InterfaceLanguageResolver.FromDalamudCode(code));

    [Fact]
    public void Auto_FollowsDalamud()
    {
        var configuration = new Configuration { InterfaceLanguage = InterfaceLanguage.Auto };
        var text = new InterfaceText(configuration, () => "tw");

        Assert.Equal(InterfaceLanguage.TraditionalChinese, text.Language);
    }

    [Fact]
    public void ExplicitSetting_Wins_OverDalamud()
    {
        var configuration = new Configuration { InterfaceLanguage = InterfaceLanguage.Japanese };
        var text = new InterfaceText(configuration, () => "zh");

        Assert.Equal(InterfaceLanguage.Japanese, text.Language);
    }

    [Fact]
    public void Default_IsAuto()
        => Assert.Equal(InterfaceLanguage.Auto, new Configuration().InterfaceLanguage);

    [Theory]
    [InlineData(InterfaceLanguage.English, false)]
    [InlineData(InterfaceLanguage.Japanese, false)]
    [InlineData(InterfaceLanguage.SimplifiedChinese, true)]
    [InlineData(InterfaceLanguage.TraditionalChinese, true)]
    public void NeedsCjkFont_IsTrueOnlyForChinese(InterfaceLanguage language, bool expected)
        => Assert.Equal(expected, InterfaceLanguageResolver.NeedsCjkFont(language));
}
