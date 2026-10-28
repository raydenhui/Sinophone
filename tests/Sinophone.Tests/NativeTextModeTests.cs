using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// The choice between the overlay and the game's own dialogue box. The write itself
/// needs a live addon, so it is covered by docs/native-mode-checklist.md instead.
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
    public void ShouldUseNativeText_IsFalse_ForSubtitles_WhenTheSettingIsOn()
        => Assert.False(TalkHandler.ShouldUseNativeText(
            new Configuration { NativeTextMode = true }, "TalkSubtitle"));

    [Theory]
    [InlineData("Journal")]
    [InlineData("TalkSubtitle")]
    [InlineData("talk")]
    public void ShouldUseNativeText_IsFalse_ForEveryOtherAddon(string addonName)
        => Assert.False(TalkHandler.ShouldUseNativeText(
            new Configuration { NativeTextMode = true }, addonName));
}
