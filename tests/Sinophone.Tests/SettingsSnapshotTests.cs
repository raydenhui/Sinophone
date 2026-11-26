namespace Sinophone.Tests;

public class SettingsSnapshotTests
{
    [Fact]
    public void Diff_ReportsNothing_WhenNoSettingChanged()
        => Assert.Equal(default(SettingsChanges), Change(_ => { }));

    [Theory]
    [InlineData(nameof(Configuration.Variant))]
    [InlineData(nameof(Configuration.Conversion))]
    [InlineData(nameof(Configuration.UseFallbackVariant))]
    public void Diff_ReloadsTheStore_ForAStoreSetting(string setting)
    {
        var changes = Change(configuration =>
        {
            switch (setting)
            {
                case nameof(Configuration.Variant):
                    configuration.Variant = ChineseVariant.Simplified;
                    break;
                case nameof(Configuration.Conversion):
                    configuration.Conversion = ChineseConversion.SimplifiedToTraditional;
                    break;
                default:
                    configuration.UseFallbackVariant = true;
                    break;
            }
        });

        Assert.True(changes.StoreReload);
        Assert.True(changes.FontRebuild);
        Assert.True(changes.TargetRefresh);
        Assert.True(changes.ItemRefresh);
        Assert.False(changes.DownloadCheck);
    }

    [Fact]
    public void Diff_ChecksTheDownload_WhenTheBothVariantSettingChanges()
    {
        var changes = Change(configuration => configuration.DownloadBothVariants = false);

        Assert.True(changes.DownloadCheck);
        Assert.False(changes.StoreReload);
    }

    [Fact]
    public void Diff_IgnoresAFontSizeChangeBelowTheTolerance()
    {
        var changes = Change(configuration => configuration.FontSize += 0.005f);

        Assert.False(changes.FontRebuild);
    }

    [Fact]
    public void Diff_RebuildsTheFont_ForARealFontSizeChange()
    {
        var changes = Change(configuration => configuration.FontSize += 0.02f);

        Assert.True(changes.FontRebuild);
        Assert.False(changes.StoreReload);
    }

    [Fact]
    public void Diff_IgnoresTheCaseOfAFontPath()
    {
        var configuration = new Configuration { CustomFontPath = "C:/Fonts/A.ttf" };
        var before = SettingsSnapshot.Capture(configuration);

        configuration.CustomFontPath = "c:/fonts/a.ttf";

        Assert.False(SettingsSnapshot.Capture(configuration).Diff(before).FontRebuild);
    }

    [Fact]
    public void Diff_RebuildsTheFont_WhenTheFontPathChanges()
    {
        var configuration = new Configuration { CustomFontPath = "C:/Fonts/A.ttf" };
        var before = SettingsSnapshot.Capture(configuration);

        configuration.CustomFontPath = "C:/Fonts/B.ttf";

        Assert.True(SettingsSnapshot.Capture(configuration).Diff(before).FontRebuild);
    }

    [Fact]
    public void Diff_ReportsTheEnglishTargetNameChange()
    {
        var changes = Change(configuration => configuration.TargetNameShowEnglish = false);

        Assert.True(changes.TargetNameEnglishChanged);
        Assert.True(changes.TargetRefresh);
    }

    [Fact]
    public void Diff_RefreshesTheTargetName_WhenTheSurfaceIsSwitched()
    {
        var changes = Change(configuration => configuration.ShowTargetName = true);

        Assert.True(changes.TargetRefresh);
        Assert.False(changes.TargetNameEnglishChanged);
        Assert.False(changes.ItemRefresh);
    }

    [Theory]
    [InlineData(nameof(Configuration.ShowItemName))]
    [InlineData(nameof(Configuration.ItemNameNativeMode))]
    [InlineData(nameof(Configuration.ShowOriginal))]
    public void Diff_RefreshesTheItemName_ForAnItemSetting(string setting)
    {
        var changes = Change(configuration =>
        {
            switch (setting)
            {
                case nameof(Configuration.ShowItemName):
                    configuration.ShowItemName = true;
                    break;
                case nameof(Configuration.ItemNameNativeMode):
                    configuration.ItemNameNativeMode = true;
                    break;
                default:
                    configuration.ShowOriginal = false;
                    break;
            }
        });

        Assert.True(changes.ItemRefresh);
        Assert.False(changes.TargetRefresh);
    }

    [Fact]
    public void Diff_ReportsANativeTextModeChange()
    {
        var changes = Change(configuration => configuration.NativeTextMode = true);

        Assert.True(changes.NativeTextModeChanged);
        Assert.False(changes.SharesTranslationChanged);
    }

    [Fact]
    public void Diff_ReportsASharedTranslationChange()
    {
        var changes = Change(configuration => configuration.NativeTextSharesTranslation = true);

        Assert.True(changes.SharesTranslationChanged);
    }

    [Fact]
    public void Diff_ReportsAChatBubbleChange()
    {
        var changes = Change(configuration => configuration.ShowInChatBubbles = true);

        Assert.True(changes.ChatBubbleSettingsChanged);
    }

    [Theory]
    [InlineData(nameof(Configuration.ShowInSelectionDialogs))]
    [InlineData(nameof(Configuration.SelectionYesNo))]
    [InlineData(nameof(Configuration.SelectionString))]
    [InlineData(nameof(Configuration.SelectionIconString))]
    [InlineData(nameof(Configuration.SelectionOk))]
    [InlineData(nameof(Configuration.SelectionCutScene))]
    public void Diff_ReportsASelectionDialogChange(string setting)
    {
        var changes = Change(configuration =>
        {
            switch (setting)
            {
                case nameof(Configuration.ShowInSelectionDialogs):
                    configuration.ShowInSelectionDialogs = true;
                    break;
                case nameof(Configuration.SelectionYesNo):
                    configuration.SelectionYesNo = false;
                    break;
                case nameof(Configuration.SelectionString):
                    configuration.SelectionString = false;
                    break;
                case nameof(Configuration.SelectionIconString):
                    configuration.SelectionIconString = false;
                    break;
                case nameof(Configuration.SelectionOk):
                    configuration.SelectionOk = false;
                    break;
                default:
                    configuration.SelectionCutScene = false;
                    break;
            }
        });

        Assert.True(changes.SelectionSettingsChanged);
    }

    [Fact]
    public void Diff_ReportsAPluginDisable()
    {
        var changes = Change(configuration => configuration.Enabled = false);

        Assert.True(changes.PluginDisabled);
        Assert.True(changes.PluginPowerChanged);
        Assert.True(changes.TargetRefresh);
        Assert.True(changes.ItemRefresh);
    }

    [Fact]
    public void Diff_ReportsAPluginEnable_WithoutADisable()
    {
        var configuration = new Configuration { Enabled = false };
        var before = SettingsSnapshot.Capture(configuration);

        configuration.Enabled = true;

        var changes = SettingsSnapshot.Capture(configuration).Diff(before);
        Assert.False(changes.PluginDisabled);
        Assert.True(changes.PluginPowerChanged);
    }

    [Fact]
    public void Diff_ReportsAFontPackChange()
    {
        var changes = Change(configuration => configuration.BuiltInFontEnabled = true);

        Assert.True(changes.FontPackChanged);
    }

    [Fact]
    public void Diff_IgnoresTheCaseOfAFontPackPath()
    {
        var configuration = new Configuration { BuiltInFontPackPath = "C:/Packs/A.ttmp2" };
        var before = SettingsSnapshot.Capture(configuration);

        configuration.BuiltInFontPackPath = "c:/packs/a.ttmp2";

        Assert.False(SettingsSnapshot.Capture(configuration).Diff(before).FontPackChanged);
    }

    [Fact]
    public void Capture_TreatsANullPathAsEmpty()
    {
        var configuration = new Configuration
        {
            CustomFontPath = null!,
            BuiltInFontPackPath = null!,
        };

        var captured = SettingsSnapshot.Capture(configuration);

        Assert.Equal(string.Empty, captured.CustomFontPath);
        Assert.Equal(string.Empty, captured.BuiltInFontPackPath);
        Assert.Equal(default(SettingsChanges), captured.Diff(captured));
    }

    private static SettingsChanges Change(Action<Configuration> mutate)
    {
        var configuration = new Configuration();
        var before = SettingsSnapshot.Capture(configuration);

        mutate(configuration);

        return SettingsSnapshot.Capture(configuration).Diff(before);
    }
}
