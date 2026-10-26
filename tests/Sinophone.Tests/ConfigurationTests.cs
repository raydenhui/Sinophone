namespace Sinophone.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var config = new Configuration();

        Assert.True(config.Enabled);
        Assert.Equal(ChineseVariant.Traditional, config.Variant);
        Assert.True(config.UseFallbackVariant);
        Assert.True(config.ShowInTalk);
        Assert.True(config.ShowInTalkSubtitle);
        Assert.True(config.ShowSpeakerName);
        Assert.True(config.ShowOriginal);
        Assert.True(config.HideWhenNoTranslation);
        Assert.True(config.LockOverlay);
        Assert.True(config.Clickthrough);
        Assert.Equal(0.72f, config.OverlayOpacity);
        Assert.Equal(20f, config.FontSize);
        Assert.Equal(620f, config.OverlayWidth);
        Assert.Equal(180f, config.OffsetFromBottom);
        Assert.Equal(string.Empty, config.CustomFontPath);
        Assert.True(config.CheckForDataUpdates);
        Assert.Equal(string.Empty, config.LastDataVersion);
    }

    [Fact]
    public void Version_StartsAtOne()
        => Assert.Equal(1, new Configuration().Version);

    [Fact]
    public void Variant_HasStableNumericValues()
    {
        // These values are persisted in the plugin config file. Changing them
        // would silently flip a user's setting.
        Assert.Equal(0, (int)ChineseVariant.Simplified);
        Assert.Equal(1, (int)ChineseVariant.Traditional);
    }
}
