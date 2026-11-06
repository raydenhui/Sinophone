using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Sinophone;

public enum ChineseVariant
{
    Simplified = 0,
    Traditional = 1,
}

/// <summary>
/// Optional conversion applied to the translated text before it is shown.
/// </summary>
public enum ChineseConversion
{
    None = 0,
    SimplifiedToTraditional = 1,
    TraditionalToSimplified = 2,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool Enabled { get; set; } = true;

    public ChineseVariant Variant { get; set; } = ChineseVariant.Traditional;

    /// <summary>
    /// Converts the translated text after lookup. Independent of <see cref="Variant"/>,
    /// which picks the data that is loaded.
    /// </summary>
    public ChineseConversion Conversion { get; set; } = ChineseConversion.None;

    /// <summary>
    /// When the primary variant has no row for a line, fall back to the other variant.
    /// </summary>
    /// <remarks>
    /// Off by default. Loading both artifacts doubles load time and memory, and the
    /// other variant only fills a handful of lines. Measured on the 2026-11-01 data,
    /// Traditional from Simplified adds 6 lines the Traditional dump lacks. Turn it
    /// on to see Simplified text where the Traditional dump has no row.
    /// </remarks>
    public bool UseFallbackVariant { get; set; }

    /// <summary>
    /// Download both language variants. Off downloads only <see cref="Variant"/>,
    /// which keeps updates small but leaves the fallback variant without data.
    /// </summary>
    public bool DownloadBothVariants { get; set; } = true;

    public bool ShowInTalk { get; set; } = true;

    public bool ShowInTalkSubtitle { get; set; } = true;

    /// <summary>
    /// Alpha. Write the translation into the game's own dialogue box instead of drawing
    /// the overlay. Needs a font mod: without one the game font has no CJK glyphs and the
    /// line shows the missing-glyph character. Off by default.
    /// </summary>
    public bool NativeTextMode { get; set; }

    public bool ShowSpeakerName { get; set; } = true;

    /// <summary>Color of the speaker name line in the dialogue overlay.</summary>
    public Vector4 SpeakerNameColor { get; set; } = new(1f, 0.84f, 0.35f, 1f);

    /// <summary>Color of the translated line in the dialogue overlay.</summary>
    public Vector4 TranslatedTextColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>Color of the original English line in the dialogue overlay.</summary>
    public Vector4 OriginalTextColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1f);

    /// <summary>
    /// Show the Chinese name of the current target in a small overlay.
    /// Off by default until the surface has been checked in game.
    /// </summary>
    public bool ShowTargetName { get; set; }

    /// <summary>Distance in pixels from the top of the screen to the target name overlay.</summary>
    public float TargetNameOffsetY { get; set; } = 170f;

    /// <summary>Horizontal offset from the screen center for the target name overlay.</summary>
    public float TargetNameOffsetX { get; set; }

    /// <summary>
    /// Show the Chinese name of the item under the mouse in a small overlay.
    /// Off by default until the surface has been checked in game.
    /// </summary>
    public bool ShowItemName { get; set; }

    /// <summary>
    /// Translate the game's selection dialogs in place. Native text only: the
    /// English prompt and options are replaced with Chinese, with no overlay.
    /// Off by default because it needs a CJK font mod.
    /// </summary>
    public bool ShowInSelectionDialogs { get; set; }

    /// <summary>Translate the Yes/No prompt and its buttons.</summary>
    public bool SelectionYesNo { get; set; } = true;

    /// <summary>Translate the list of text options.</summary>
    public bool SelectionString { get; set; } = true;

    /// <summary>Translate the list of icon options.</summary>
    public bool SelectionIconString { get; set; } = true;

    /// <summary>Translate the single OK prompt.</summary>
    public bool SelectionOk { get; set; } = true;

    /// <summary>Translate the cutscene choice prompt and its options.</summary>
    public bool SelectionCutScene { get; set; } = true;

    /// <summary>
    /// Show the English name under the translated name in the target and item
    /// windows. The Talk overlay always shows both names on one line.
    /// </summary>
    public bool ShowOriginal { get; set; } = true;

    /// <summary>
    /// Talk overlay only. When false, the English line is shown if no Chinese
    /// translation was found.
    /// </summary>
    public bool HideWhenNoTranslation { get; set; } = true;

    public bool LockOverlay { get; set; } = true;

    public bool Clickthrough { get; set; } = true;

    public float OverlayOpacity { get; set; } = 0.72f;

    /// <summary>
    /// Use the global colors and opacity for the Talk overlay. Turn off to set
    /// them for Talk and its subtitles only.
    /// </summary>
    public bool TalkUseGlobalAppearance { get; set; } = true;

    public Vector4 TalkSpeakerNameColor { get; set; } = new(1f, 0.84f, 0.35f, 1f);

    public Vector4 TalkTranslatedTextColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>Color of the English fallback when a line has no translation.</summary>
    public Vector4 TalkOriginalTextColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1f);

    public float TalkOverlayOpacity { get; set; } = 0.72f;

    /// <summary>Use the global colors and opacity for the target name window.</summary>
    public bool TargetNameUseGlobalAppearance { get; set; } = true;

    public Vector4 TargetNameColor { get; set; } = new(1f, 0.84f, 0.35f, 1f);

    public Vector4 TargetNameOriginalColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1f);

    public float TargetNameOpacity { get; set; } = 0.72f;

    /// <summary>Use the global colors and opacity for the item name window.</summary>
    public bool ItemNameUseGlobalAppearance { get; set; } = true;

    public Vector4 ItemNameColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector4 ItemNameOriginalColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1f);

    public float ItemNameOpacity { get; set; } = 0.72f;

    public float FontSize { get; set; } = 20f;

    public float OverlayWidth { get; set; } = 620f;

    public float OffsetFromBottom { get; set; } = 180f;

    /// <summary>Horizontal offset from the screen center for the dialogue overlay.</summary>
    public float OverlayOffsetX { get; set; }

    /// <summary>
    /// When true, the overlay keeps its bottom edge fixed when the text height changes,
    /// so new lines grow upward instead of covering the Talk window.
    /// </summary>
    public bool AnchorOverlayBottom { get; set; } = true;

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

    /// <summary>
    /// True when one selection-dialog addon is switched on. The master switch
    /// <see cref="ShowInSelectionDialogs"/> is checked by the handler.
    /// </summary>
    public bool IsSelectionDialogEnabled(string addonName)
        => addonName switch
        {
            "SelectYesno" => SelectionYesNo,
            "SelectString" => SelectionString,
            "SelectIconString" => SelectionIconString,
            "SelectOk" => SelectionOk,
            "CutSceneSelectString" => SelectionCutScene,
            _ => false,
        };

    // Per-component appearance falls back to the global value until the user
    // turns the override off. Kept as methods so the config file only stores
    // the fields above.

    public Vector4 GetTalkSpeakerNameColor()
        => TalkUseGlobalAppearance ? SpeakerNameColor : TalkSpeakerNameColor;

    public Vector4 GetTalkTranslatedTextColor()
        => TalkUseGlobalAppearance ? TranslatedTextColor : TalkTranslatedTextColor;

    public Vector4 GetTalkOriginalTextColor()
        => TalkUseGlobalAppearance ? OriginalTextColor : TalkOriginalTextColor;

    public float GetTalkOpacity()
        => TalkUseGlobalAppearance ? OverlayOpacity : TalkOverlayOpacity;

    public Vector4 GetTargetNameColor()
        => TargetNameUseGlobalAppearance ? SpeakerNameColor : TargetNameColor;

    public Vector4 GetTargetNameOriginalColor()
        => TargetNameUseGlobalAppearance ? OriginalTextColor : TargetNameOriginalColor;

    public float GetTargetNameOpacity()
        => TargetNameUseGlobalAppearance ? OverlayOpacity : TargetNameOpacity;

    public Vector4 GetItemNameColor()
        => ItemNameUseGlobalAppearance ? TranslatedTextColor : ItemNameColor;

    public Vector4 GetItemNameOriginalColor()
        => ItemNameUseGlobalAppearance ? OriginalTextColor : ItemNameOriginalColor;

    public float GetItemNameOpacity()
        => ItemNameUseGlobalAppearance ? OverlayOpacity : ItemNameOpacity;
}
