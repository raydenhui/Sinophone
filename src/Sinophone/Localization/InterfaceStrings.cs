namespace Sinophone.Localization;

/// <summary>
/// Every piece of text the settings window and its status lines show. Each language fills every
/// member, so a missing translation is a compile error instead of an English line in the middle
/// of a Chinese window.
/// </summary>
public sealed class InterfaceStrings
{
    public required string TabGeneral { get; init; }
    public required string TabComponents { get; init; }
    public required string TabData { get; init; }
    public required string TabLogs { get; init; }
    public required string TabStatus { get; init; }

    public required string SectionInterface { get; init; }
    public required string InterfaceLanguageLabel { get; init; }
    public required string InterfaceLanguageHint { get; init; }
    public required string InterfaceLanguageAuto { get; init; }
    public required string LanguageEnglish { get; init; }
    public required string LanguageSimplifiedChinese { get; init; }
    public required string LanguageTraditionalChinese { get; init; }
    public required string LanguageJapanese { get; init; }

    public required string GeneralEnable { get; init; }
    public required string SectionChineseSource { get; init; }
    public required string GeneralSourceHelp { get; init; }
    public required string VariantTraditional { get; init; }
    public required string VariantTraditionalHint { get; init; }
    public required string VariantSimplified { get; init; }
    public required string VariantSimplifiedHint { get; init; }
    public required string GeneralFallback { get; init; }
    public required string GeneralFallbackHint { get; init; }
    public required string SectionConversion { get; init; }
    public required string GeneralConversionHelp { get; init; }
    public required string ConversionNone { get; init; }
    public required string ConversionToTraditional { get; init; }
    public required string ConversionToSimplified { get; init; }
    public required string SectionFontOverlay { get; init; }
    public required string FontSize { get; init; }
    public required string OverlayOpacityDefault { get; init; }
    public required string CustomFontPath { get; init; }
    public required string SectionColors { get; init; }
    public required string ColorNpcName { get; init; }
    public required string ColorTranslatedText { get; init; }
    public required string ColorOriginalText { get; init; }
    public required string GeneralColorHint { get; init; }
    public required string SectionSharedActions { get; init; }
    public required string UseDefaultColorsAll { get; init; }
    public required string ResetAll { get; init; }

    public required string TabTalk { get; init; }
    public required string TabSubtitles { get; init; }
    public required string TabChatBubbles { get; init; }
    public required string TabBattleTalk { get; init; }
    public required string TabSelection { get; init; }
    public required string TabNameplates { get; init; }
    public required string TabItemName { get; init; }

    public required string TalkEnable { get; init; }
    public required string SectionMode { get; init; }
    public required string ModeOverlay { get; init; }
    public required string ModeNative { get; init; }
    public required string TalkNativeWarning { get; init; }
    public required string OpenFontGuide { get; init; }
    public required string SectionChineseFont { get; init; }
    public required string BuiltInFont { get; init; }
    public required string BuiltInFontHint { get; init; }
    public required string ReloadFont { get; init; }
    public required string SectionDisplay { get; init; }
    public required string ShowSpeakerName { get; init; }
    public required string ShowSpeakerNameHint { get; init; }
    public required string HideWhenNoTranslation { get; init; }
    public required string HideWhenNoTranslationHint { get; init; }
    public required string SectionOtherReaders { get; init; }
    public required string OtherReadersHint { get; init; }
    public required string ShareOriginal { get; init; }
    public required string ShareTranslation { get; init; }
    public required string SectionOverlayPosition { get; init; }
    public required string LockOverlay { get; init; }
    public required string Clickthrough { get; init; }
    public required string OverlayWidth { get; init; }
    public required string OverlayOffsetX { get; init; }
    public required string OffsetFromBottom { get; init; }
    public required string AnchorOverlayBottom { get; init; }
    public required string PreviewOverlay { get; init; }
    public required string ResetTalkLayout { get; init; }
    public required string SectionAppearance { get; init; }
    public required string UseDefaultAppearance { get; init; }
    public required string FollowingDefaults { get; init; }
    public required string ColorSpeakerName { get; init; }
    public required string ColorUntranslatedFallback { get; init; }
    public required string BackgroundOpacity { get; init; }

    public required string SubtitleEnable { get; init; }
    public required string SubtitleHint { get; init; }
    public required string SubtitleModeHint { get; init; }

    public required string BubbleEnable { get; init; }
    public required string BubbleHint { get; init; }
    public required string BubbleWarning { get; init; }

    public required string BattleTalkEnable { get; init; }
    public required string BattleTalkHint { get; init; }
    public required string BattleTalkWarning { get; init; }

    public required string SelectionEnable { get; init; }
    public required string SelectionWarning { get; init; }
    public required string SelectionHint { get; init; }
    public required string SelectionAdvanced { get; init; }
    public required string SelectionYesNo { get; init; }
    public required string SelectionString { get; init; }
    public required string SelectionIcon { get; init; }
    public required string SelectionOk { get; init; }
    public required string SelectionCutScene { get; init; }

    public required string NameplateWarning { get; init; }
    public required string NameplateEnable { get; init; }
    public required string NameplateHint { get; init; }
    public required string NameplateShowEnglish { get; init; }
    public required string BilingualHint { get; init; }

    public required string ItemEnable { get; init; }
    public required string ItemNative { get; init; }
    public required string ItemNativeWarning { get; init; }
    public required string ItemNativeFallback { get; init; }
    public required string ItemOverlayHint { get; init; }
    public required string SectionLanguage { get; init; }
    public required string ItemChineseOnly { get; init; }
    public required string ItemBoth { get; init; }
    public required string ItemNativeAppearance { get; init; }
    public required string ColorTranslatedName { get; init; }
    public required string ColorEnglishName { get; init; }

    public required string DataDownloadOnLoad { get; init; }
    public required string DataDownloadOnLoadHint { get; init; }
    public required string DataDownloadBoth { get; init; }
    public required string DataDownloadBothHint { get; init; }
    public required string Cancel { get; init; }
    public required string DownloadUpdate { get; init; }
    public required string DownloadOrUpdate { get; init; }
    public required string CheckForUpdates { get; init; }

    public required string LogsIntro { get; init; }
    public required string ShowEveryGroup { get; init; }
    public required string HideEveryDetail { get; init; }
    public required string SectionGroups { get; init; }
    public required string LogDialogue { get; init; }
    public required string LogDialogueHint { get; init; }
    public required string LogNativeText { get; init; }
    public required string LogNativeTextHint { get; init; }
    public required string LogSelection { get; init; }
    public required string LogSelectionHint { get; init; }
    public required string LogNameplates { get; init; }
    public required string LogNameplatesHint { get; init; }
    public required string LogItemNames { get; init; }
    public required string LogItemNamesHint { get; init; }
    public required string LogData { get; init; }
    public required string LogDataHint { get; init; }
    public required string LogPlugin { get; init; }
    public required string LogPluginHint { get; init; }

    public required string StatusWarning { get; init; }

    public required string StatusNoData { get; init; }
    public required string StatusFont { get; init; }
    public required string StatusFontMissing { get; init; }
    public required string StatusSystemFontSuffix { get; init; }
    public required string StatusVariantSimplified { get; init; }
    public required string StatusVariantTraditional { get; init; }
    public required string StatusFallbackSuffix { get; init; }
    public required string StatusSummary { get; init; }
    public required string DataSourceBuiltIn { get; init; }
    public required string DataSourceMissingVariant { get; init; }
    public required string DataSourceSelectedOnly { get; init; }
    public required string DataSourceBoth { get; init; }
    public required string DataSourceUnknownVersion { get; init; }
    public required string DataSourceVersion { get; init; }
    public required string AboutText { get; init; }

    public static InterfaceStrings For(InterfaceLanguage language)
        => language switch
        {
            InterfaceLanguage.SimplifiedChinese => SimplifiedChineseStrings.Value,
            InterfaceLanguage.TraditionalChinese => TraditionalChineseStrings.Value,
            InterfaceLanguage.Japanese => JapaneseStrings.Value,
            _ => EnglishStrings.Value,
        };
}
