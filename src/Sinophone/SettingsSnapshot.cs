namespace Sinophone;

/// <summary>
/// The settings <see cref="Plugin.ApplySettings"/> compares. One capture is taken at load and
/// another on every change, so a value cannot be compared without also being updated.
/// </summary>
/// <remarks>
/// Every field here is compared in <see cref="Diff"/>. A setting that should trigger work must be
/// added to both, or the change is silently ignored. Conversion was once missing from the old
/// hand-written snapshot and a change to it reloaded no data.
/// </remarks>
internal readonly record struct SettingsSnapshot(
    ChineseVariant Variant,
    ChineseConversion Conversion,
    bool UseFallbackVariant,
    bool DownloadBothVariants,
    bool Enabled,
    bool ShowTargetName,
    bool TargetNameShowEnglish,
    bool ShowItemName,
    bool ItemNameNativeMode,
    bool ShowOriginal,
    bool NativeTextMode,
    bool NativeTextSharesTranslation,
    bool BuiltInFontEnabled,
    string BuiltInFontPackPath,
    bool ShowInChatBubbles,
    bool ShowInSelectionDialogs,
    bool SelectionYesNo,
    bool SelectionString,
    bool SelectionIconString,
    bool SelectionOk,
    bool SelectionCutScene,
    float FontSize,
    string CustomFontPath)
{
    public static SettingsSnapshot Capture(Configuration configuration) => new(
        configuration.Variant,
        configuration.Conversion,
        configuration.UseFallbackVariant,
        configuration.DownloadBothVariants,
        configuration.Enabled,
        configuration.ShowTargetName,
        configuration.TargetNameShowEnglish,
        configuration.ShowItemName,
        configuration.ItemNameNativeMode,
        configuration.ShowOriginal,
        configuration.NativeTextMode,
        configuration.NativeTextSharesTranslation,
        configuration.BuiltInFontEnabled,
        configuration.BuiltInFontPackPath ?? string.Empty,
        configuration.ShowInChatBubbles,
        configuration.ShowInSelectionDialogs,
        configuration.SelectionYesNo,
        configuration.SelectionString,
        configuration.SelectionIconString,
        configuration.SelectionOk,
        configuration.SelectionCutScene,
        configuration.FontSize,
        configuration.CustomFontPath ?? string.Empty);

    /// <summary>What changed between <paramref name="previous"/> and this snapshot.</summary>
    public SettingsChanges Diff(SettingsSnapshot previous)
    {
        var storeReload = Variant != previous.Variant
                          || Conversion != previous.Conversion
                          || UseFallbackVariant != previous.UseFallbackVariant;

        var fontRebuild = storeReload
                          || Math.Abs(FontSize - previous.FontSize) > 0.01f
                          || PathChanged(CustomFontPath, previous.CustomFontPath);

        var targetNameEnglishChanged = TargetNameShowEnglish != previous.TargetNameShowEnglish;
        var targetRefresh = storeReload
                            || Enabled != previous.Enabled
                            || ShowTargetName != previous.ShowTargetName
                            || targetNameEnglishChanged;

        var itemRefresh = storeReload
                          || Enabled != previous.Enabled
                          || ShowItemName != previous.ShowItemName
                          || ItemNameNativeMode != previous.ItemNameNativeMode
                          || ShowOriginal != previous.ShowOriginal;

        return new SettingsChanges(
            StoreReload: storeReload,
            DownloadCheck: DownloadBothVariants != previous.DownloadBothVariants,
            FontRebuild: fontRebuild,
            TargetNameEnglishChanged: targetNameEnglishChanged,
            TargetRefresh: targetRefresh,
            ItemRefresh: itemRefresh,
            NativeTextModeChanged: NativeTextMode != previous.NativeTextMode,
            SharesTranslationChanged: NativeTextSharesTranslation != previous.NativeTextSharesTranslation,
            ChatBubbleSettingsChanged: ShowInChatBubbles != previous.ShowInChatBubbles,
            SelectionSettingsChanged: SelectionSettingsChanged(previous),
            PluginDisabled: previous.Enabled && !Enabled,
            PluginPowerChanged: Enabled != previous.Enabled,
            FontPackChanged: BuiltInFontEnabled != previous.BuiltInFontEnabled
                             || PathChanged(BuiltInFontPackPath, previous.BuiltInFontPackPath));
    }

    private bool SelectionSettingsChanged(SettingsSnapshot previous)
        => ShowInSelectionDialogs != previous.ShowInSelectionDialogs
           || SelectionYesNo != previous.SelectionYesNo
           || SelectionString != previous.SelectionString
           || SelectionIconString != previous.SelectionIconString
           || SelectionOk != previous.SelectionOk
           || SelectionCutScene != previous.SelectionCutScene;

    private static bool PathChanged(string current, string previous)
        => !string.Equals(current, previous, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The work a settings change needs, worked out once per comparison.</summary>
internal readonly record struct SettingsChanges(
    bool StoreReload,
    bool DownloadCheck,
    bool FontRebuild,
    bool TargetNameEnglishChanged,
    bool TargetRefresh,
    bool ItemRefresh,
    bool NativeTextModeChanged,
    bool SharesTranslationChanged,
    bool ChatBubbleSettingsChanged,
    bool SelectionSettingsChanged,
    bool PluginDisabled,
    bool PluginPowerChanged,
    bool FontPackChanged);
