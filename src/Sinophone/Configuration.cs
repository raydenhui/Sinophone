using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Sinophone;

public enum ChineseVariant
{
    Simplified = 0,
    Traditional = 1,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public ChineseVariant Variant { get; set; } = ChineseVariant.Traditional;

    /// <summary>
    /// When the primary variant has no row for a line, fall back to the other variant.
    /// </summary>
    public bool UseFallbackVariant { get; set; } = true;

    public bool ShowInTalk { get; set; } = true;

    public bool ShowInTalkSubtitle { get; set; } = true;

    public bool ShowSpeakerName { get; set; } = true;

    public bool ShowOriginal { get; set; } = true;

    /// <summary>
    /// When false, the original text is shown even if no Chinese translation was found.
    /// </summary>
    public bool HideWhenNoTranslation { get; set; } = true;

    public bool LockOverlay { get; set; } = true;

    public bool Clickthrough { get; set; } = true;

    public float OverlayOpacity { get; set; } = 0.72f;

    public float FontSize { get; set; } = 20f;

    public float OverlayWidth { get; set; } = 620f;

    public float OffsetFromBottom { get; set; } = 180f;

    /// <summary>
    /// Optional path to a .ttf/.ttc/.otf file. Leave empty to use a Windows CJK system font.
    /// </summary>
    public string CustomFontPath { get; set; } = string.Empty;

    /// <summary>
    /// Check the published translation data for a newer version when the plugin loads.
    /// </summary>
    public bool CheckForDataUpdates { get; set; } = true;

    /// <summary>
    /// Version of the translation data downloaded into the plugin config directory.
    /// Empty when the copy embedded in the plugin is in use.
    /// </summary>
    public string LastDataVersion { get; set; } = string.Empty;
}
