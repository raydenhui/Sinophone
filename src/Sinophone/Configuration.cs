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

    /// <summary>
    /// Show the Chinese name of the current target in a small overlay.
    /// Off by default until the surface has been checked in game.
    /// </summary>
    public bool ShowTargetName { get; set; }

    /// <summary>Distance in pixels from the top of the screen to the target name overlay.</summary>
    public float TargetNameOffsetY { get; set; } = 170f;

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

    /// <summary>True when the downloaded Simplified artifact matches <see cref="LastDataVersion"/>.</summary>
    public bool HasSimplifiedData { get; set; }

    /// <summary>True when the downloaded Traditional artifact matches <see cref="LastDataVersion"/>.</summary>
    public bool HasTraditionalData { get; set; }
}
