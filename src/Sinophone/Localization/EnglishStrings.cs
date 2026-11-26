namespace Sinophone.Localization;

internal static class EnglishStrings
{
    public static readonly InterfaceStrings Value = new()
    {
        TabGeneral = "General",
        TabComponents = "Components",
        TabData = "Data",
        TabLogs = "Logs",
        TabStatus = "Status",

        SectionInterface = "Interface",
        InterfaceLanguageLabel = "Language",
        InterfaceLanguageHint =
            "Only the settings window changes. The translation still follows the Chinese text " +
            "source below.",
        InterfaceLanguageAuto = "Follow Dalamud",
        LanguageEnglish = "English",
        LanguageSimplifiedChinese = "Simplified Chinese",
        LanguageTraditionalChinese = "Traditional Chinese",
        LanguageJapanese = "Japanese",

        GeneralEnable = "Enable translations",
        SectionChineseSource = "Chinese text source",
        GeneralSourceHelp =
            "Choose which official game version supplies the translation. " +
            "TC is the Traditional Chinese release for Taiwan and Hong Kong. " +
            "CN is the Simplified Chinese release for mainland China.",
        VariantTraditional = "Traditional Chinese (TC)",
        VariantTraditionalHint = "Taiwan / Hong Kong game version",
        VariantSimplified = "Simplified Chinese (CN)",
        VariantSimplifiedHint = "Mainland China game version",
        GeneralFallback = "Use the other version when a line is missing",
        GeneralFallbackHint =
            "May mix Traditional and Simplified characters for missing rows. " +
            "Off by default: it loads both data files and doubles the load time " +
            "for a few extra lines.",
        SectionConversion = "Convert displayed text",
        GeneralConversionHelp =
            "Optional. Changes the characters after lookup without changing which " +
            "game version supplies the translation.",
        ConversionNone = "No conversion (keep official text)",
        ConversionToTraditional = "Simplified to Traditional (CN to TC)",
        ConversionToSimplified = "Traditional to Simplified (TC to CN)",
        SectionFontOverlay = "Font and overlay",
        FontSize = "Font size",
        OverlayOpacityDefault = "Background opacity (default)",
        CustomFontPath = "Custom font path (optional, .ttf/.ttc/.otf)",
        SectionColors = "Colors (defaults)",
        ColorNpcName = "NPC name",
        ColorTranslatedText = "Translated text",
        ColorOriginalText = "Original text",
        GeneralColorHint =
            "The item name language is on its own tab. " +
            "The nameplate switch is on its own tab too.",
        SectionSharedActions = "Shared actions",
        UseDefaultColorsAll = "Use the default colors for every component",
        ResetAll = "Reset all settings",

        TabTalk = "Talk",
        TabSubtitles = "Subtitles",
        TabChatBubbles = "Chat bubbles",
        TabBattleTalk = "Battle talk",
        TabSelection = "Selection",
        TabNameplates = "Nameplates",
        TabItemName = "Item name",

        TalkEnable = "Enable translation in the Talk dialogue",
        SectionMode = "Mode",
        ModeOverlay = "Overlay",
        ModeNative = "Native (game's own windows)",
        TalkNativeWarning =
            "Native mode writes Chinese into the game's own dialogue box and subtitle band. " +
            "The game's font has no Chinese glyphs, so install a font mod first.",
        OpenFontGuide = "Open the font setup guide",
        SectionChineseFont = "Chinese font",
        BuiltInFont = "Replace the game font with a font pack",
        BuiltInFontHint =
            "A .ttmp2 font mod or a folder that holds the game's font files. The plugin reads " +
            "the pack from your disk and ships no font data.",
        ReloadFont = "Reload the game font",
        SectionDisplay = "Display",
        ShowSpeakerName = "Show speaker name",
        ShowSpeakerNameHint = "The Chinese name is shown with the English name in parentheses.",
        HideWhenNoTranslation = "Hide when no translation is found",
        HideWhenNoTranslationHint =
            "When off, an untranslated line falls back to the English text. " +
            "The overlay never shows both languages at once.",
        SectionOtherReaders = "What other readers see",
        OtherReadersHint =
            "The dialogue box always shows Chinese. This decides what the game's own log and " +
            "other plugins read for the same line.",
        ShareOriginal = "English original",
        ShareTranslation = "Chinese translation",
        SectionOverlayPosition = "Overlay position",
        LockOverlay = "Lock overlay position",
        Clickthrough = "Click-through",
        OverlayWidth = "Width",
        OverlayOffsetX = "Horizontal offset (0 = center)",
        OffsetFromBottom = "Distance from screen bottom",
        AnchorOverlayBottom = "Keep the bottom line fixed when text grows",
        PreviewOverlay = "Preview overlay",
        ResetTalkLayout = "Reset Talk layout",
        SectionAppearance = "Appearance",
        UseDefaultAppearance = "Use the default colors and opacity",
        FollowingDefaults = "Following the defaults on the General tab.",
        ColorSpeakerName = "Speaker name",
        ColorUntranslatedFallback = "Untranslated fallback",
        BackgroundOpacity = "Background opacity",

        SubtitleEnable = "Enable translation in Talk subtitles",
        SubtitleHint = "Subtitles use the Talk overlay's position and appearance.",
        SubtitleModeHint =
            "The Talk tab's Mode setting also decides how subtitles are drawn: in the game's own " +
            "subtitle band, or on the overlay when the band cannot take the line.",

        BubbleEnable = "Translate NPC chat bubbles",
        BubbleHint = "The Chinese is written into the game's own bubble, above the NPC's head.",
        BubbleWarning =
            "Chat bubbles use the game's own font and need the font mod. " +
            "The bubble layout is still being checked in game, so this is off by default.",

        BattleTalkEnable = "Translate battle talk",
        BattleTalkHint =
            "Boss shouts, dungeon narration, and duty NPC lines, in the game's own box at the top " +
            "of the screen. The name follows the speaker name setting on the Talk tab.",
        BattleTalkWarning =
            "Battle talk uses the game's own font and needs the font mod. A line with no " +
            "translation stays English, and so does the line already on screen when the switch is " +
            "turned off. This is off by default.",

        SelectionEnable = "Enable translation in selection dialogs",
        SelectionWarning =
            "Selection dialogs are translated in place, with no overlay. " +
            "This needs a CJK font mod, or the Chinese text shows the missing-glyph character.",
        SelectionHint = "Prompts and option labels are replaced in the game's own dialog.",
        SelectionAdvanced = "Advanced: choose dialog types",
        SelectionYesNo = "Yes / No prompt",
        SelectionString = "List of text options",
        SelectionIcon = "List of icon options",
        SelectionOk = "Single OK prompt",
        SelectionCutScene = "Cutscene choice",

        NameplateWarning =
            "The Chinese name is written into the game's own nameplate above every visible NPC " +
            "and object, such as a summoning bell, company chest, or door. " +
            "It needs a font mod, or the name shows the missing-glyph character. " +
            "Nameplates must be visible in the game settings.",
        NameplateEnable = "Show Chinese names on nameplates",
        NameplateHint =
            "Every visible nameplate is translated, not only the target. " +
            "Player names are not translated.",
        NameplateShowEnglish = "Show the English name too",
        BilingualHint = "The English name follows the Chinese name in brackets, on the same line.",

        ItemEnable = "Show the Chinese name of the hovered item",
        ItemNative = "Native item tooltip",
        ItemNativeWarning =
            "Native mode writes the name into the game's own item tooltip. " +
            "The game's font has no Chinese glyphs, so install a font mod first.",
        ItemNativeFallback = "A tooltip the plugin cannot write falls back to the overlay.",
        ItemOverlayHint = "The name follows the mouse, above the item tooltip.",
        SectionLanguage = "Language",
        ItemChineseOnly = "Chinese only",
        ItemBoth = "Chinese and English",
        ItemNativeAppearance = "Native mode draws the name with the game's own colors and size.",
        ColorTranslatedName = "Translated name",
        ColorEnglishName = "English name",

        DataDownloadOnLoad = "Download data updates on load",
        DataDownloadOnLoadHint =
            "Runs in the background. The old data stays in use until the new data is ready.",
        DataDownloadBoth = "Download both language variants",
        DataDownloadBothHint =
            "Off downloads only the selected variant. Fallback uses the other variant only if it " +
            "is already on disk.",
        Cancel = "Cancel",
        DownloadUpdate = "Download update",
        DownloadOrUpdate = "Download / update data",
        CheckForUpdates = "Check for updates",

        LogsIntro =
            "Choose which of the plugin's own messages appear in Dalamud's plugin log. " +
            "Each switch hides one group's detail messages. Warnings and errors always show.",
        ShowEveryGroup = "Show every group",
        HideEveryDetail = "Hide every detail message",
        SectionGroups = "Groups",
        LogDialogue = "Dialogue lookups",
        LogDialogueHint = "Every Talk and subtitle line, with the lookup that answered it.",
        LogNativeText = "Native text",
        LogNativeTextHint = "Each write into the game's own dialogue box.",
        LogSelection = "Selection dialogs",
        LogSelectionHint = "Each prompt and option the plugin translates.",
        LogNameplates = "Nameplates",
        LogNameplatesHint = "One line for every name it puts on a nameplate.",
        LogItemNames = "Item names",
        LogItemNamesHint = "Item name lookups that find no translation.",
        LogData = "Translation data",
        LogDataHint = "Loading, indexing, and download checks.",
        LogPlugin = "Plugin",
        LogPluginHint = "Load, unload, and settings changes.",

        StatusWarning =
            "Early development. Some NPC dialogue is not detected, " +
            "and text that exists in no game dump stays untranslated.",

        StatusNoData = "No translation data loaded. Use \"Download / update data\" in the settings.",
        StatusFont = "Font: {0}",
        StatusFontMissing = "Font: not found (Chinese text may render as boxes).",
        StatusSystemFontSuffix = " (system font)",
        StatusVariantSimplified = "Simplified",
        StatusVariantTraditional = "Traditional",
        StatusFallbackSuffix = " + fallback",
        StatusSummary = "{0}: {1:N0} dialogue entries, {2:N0} name entries.\n{3}",
        DataSourceBuiltIn = "Using the translation data built into the plugin.",
        DataSourceMissingVariant =
            "Using downloaded data. One variant is missing and uses the built-in copy.",
        DataSourceSelectedOnly = "Using downloaded data ({0}) for the selected variant only.",
        DataSourceBoth = "Using downloaded data ({0}).",
        DataSourceUnknownVersion = "unknown version",
        DataSourceVersion = "version {0}",
        AboutText = "Sinophone {0} by {1}",
    };
}
