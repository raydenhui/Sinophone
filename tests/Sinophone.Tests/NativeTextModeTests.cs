using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// The choice between the overlay and the game's own windows. The write itself needs a live
/// addon, so it is covered by docs/native-mode-checklist.md instead.
/// </summary>
public class NativeTextModeTests
{
    [Fact]
    public void ShouldUseNativeText_IsFalse_WhenTheSettingIsOff()
        => Assert.False(TalkHandler.ShouldUseNativeText(new Configuration(), "Talk"));

    [Fact]
    public void ShouldUseNativeText_IsTrue_ForTalk_WhenTheSettingIsOn()
        => Assert.True(TalkHandler.ShouldUseNativeText(
            new Configuration { NativeTextMode = true }, "Talk"));

    [Fact]
    public void ShouldUseNativeText_IsTrue_ForSubtitles_WhenTheSettingIsOn()
        => Assert.True(TalkHandler.ShouldUseNativeText(
            new Configuration { NativeTextMode = true }, "TalkSubtitle"));

    [Theory]
    [InlineData("Journal")]
    [InlineData("talk")]
    [InlineData("TalkSubtitles")]
    public void ShouldUseNativeText_IsFalse_ForEveryOtherAddon(string addonName)
        => Assert.False(TalkHandler.ShouldUseNativeText(
            new Configuration { NativeTextMode = true }, addonName));

    [Fact]
    public void ShouldWriteNativeText_IsTrue_ForTalk_WithoutTheValueHook()
        => Assert.True(TalkHandler.ShouldWriteNativeText(
            new Configuration { NativeTextMode = true }, "Talk", valuesHooked: false));

    [Fact]
    public void ShouldWriteNativeText_IsTrue_ForASubtitle_WithTheValueHook()
        => Assert.True(TalkHandler.ShouldWriteNativeText(
            new Configuration { NativeTextMode = true }, "TalkSubtitle", valuesHooked: true));

    [Fact]
    public void ShouldWriteNativeText_IsFalse_ForASubtitle_WithoutTheValueHook()
        => Assert.False(TalkHandler.ShouldWriteNativeText(
            new Configuration { NativeTextMode = true }, "TalkSubtitle", valuesHooked: false));

    [Fact]
    public void ShouldWriteNativeText_IsFalse_WhenTheSettingIsOff()
        => Assert.False(TalkHandler.ShouldWriteNativeText(
            new Configuration(), "TalkSubtitle", valuesHooked: true));

    [Fact]
    public void ShouldReapplyNativeText_IsTrue_ForTalk_WhenNativeModeIsOn()
        => Assert.True(TalkHandler.ShouldReapplyNativeText(
            new Configuration { NativeTextMode = true }, "Talk"));

    [Fact]
    public void ShouldReapplyNativeText_IsFalse_WhenThePluginIsDisabled()
        => Assert.False(TalkHandler.ShouldReapplyNativeText(
            new Configuration { Enabled = false, NativeTextMode = true }, "Talk"));

    [Fact]
    public void ShouldReapplyNativeText_IsFalse_ForSubtitles()
        => Assert.False(TalkHandler.ShouldReapplyNativeText(
            new Configuration { NativeTextMode = true }, "TalkSubtitle"));

    [Fact]
    public void SharesTranslation_IsFalse_ByDefault()
        => Assert.False(TalkHandler.SharesTranslation(new Configuration { NativeTextMode = true }));

    [Fact]
    public void SharesTranslation_IsTrue_OnlyWhenNativeModeIsOn()
    {
        Assert.True(TalkHandler.SharesTranslation(
            new Configuration { NativeTextMode = true, NativeTextSharesTranslation = true }));

        // The overlay never writes the game's values, so the choice does not apply.
        Assert.False(TalkHandler.SharesTranslation(
            new Configuration { NativeTextMode = false, NativeTextSharesTranslation = true }));
    }

    [Fact]
    public void IsLaidOutByHook_IsTrue_WhenTheValueAlreadyHoldsTheChineseLine()
        => Assert.True(TalkHandler.IsLaidOutByHook("你好，旅行者。", "你好，旅行者。"));

    [Fact]
    public void IsLaidOutByHook_IsFalse_WhenTheValueIsTheEnglishLine()
        => Assert.False(TalkHandler.IsLaidOutByHook("Hello, traveler.", "你好，旅行者。"));

    [Fact]
    public void IsLaidOutByHook_IsFalse_WhenNothingWasLaidOut()
    {
        Assert.False(TalkHandler.IsLaidOutByHook("Hello, traveler.", null));
        Assert.False(TalkHandler.IsLaidOutByHook(null, "你好，旅行者。"));
    }

    [Fact]
    public void ChooseFittingFontSize_KeepsTheStartingSize_WhenItAlreadyFits()
    {
        var measured = new List<byte>();

        var fitted = NativeTextWriter.ChooseFittingFontSize(14, 10, 100, size =>
        {
            measured.Add(size);
            return 90;
        });

        Assert.Equal((byte)14, fitted);
        Assert.Equal([(byte)14], measured);
    }

    [Fact]
    public void ChooseFittingFontSize_StepsDown_UntilTheTextFits()
    {
        // A smaller font wraps into fewer lines, so the height drops with the size.
        static ushort Height(byte size) => (ushort)(size * 10);

        var fitted = NativeTextWriter.ChooseFittingFontSize(14, 10, 115, Height);

        Assert.Equal((byte)11, fitted);
    }

    [Fact]
    public void ChooseFittingFontSize_ReturnsNull_WhenEvenTheSmallestSizeOverflows()
        => Assert.Null(NativeTextWriter.ChooseFittingFontSize(14, 10, 5, static _ => 500));

    [Fact]
    public void ChooseFittingFontSize_UsesTheSmallestSize_WhenItIsTheOnlyFit()
        => Assert.Equal(
            (byte)10,
            NativeTextWriter.ChooseFittingFontSize(
                14, 10, 100, static size => (ushort)(size == 10 ? 90 : 200)));

    [Fact]
    public void ChooseFittingFontSize_ReturnsNull_WhenTheMinimumIsAboveTheStart()
        => Assert.Null(NativeTextWriter.ChooseFittingFontSize(10, 14, 100, static _ => 10));
}
