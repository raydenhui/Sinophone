namespace Sinophone.Localization;

internal static class TraditionalChineseStrings
{
    public static readonly InterfaceStrings Value = new()
    {
        TabGeneral = "一般",
        TabComponents = "組件",
        TabData = "資料",
        TabLogs = "記錄",
        TabStatus = "狀態",

        SectionInterface = "介面",
        InterfaceLanguageLabel = "語言",
        InterfaceLanguageHint = "只會變更設定視窗。翻譯仍由下方的中文文字來源決定。",
        InterfaceLanguageAuto = "跟隨 Dalamud",
        LanguageEnglish = "英文",
        LanguageSimplifiedChinese = "簡體中文",
        LanguageTraditionalChinese = "繁體中文",
        LanguageJapanese = "日文",

        GeneralEnable = "啟用翻譯",
        SectionChineseSource = "中文文字來源",
        GeneralSourceHelp =
            "選擇提供翻譯的官方遊戲版本。TC 是台灣與香港的繁體中文版本，" +
            "CN 是中國大陸的簡體中文版本。",
        VariantTraditional = "繁體中文（TC）",
        VariantTraditionalHint = "台灣 / 香港遊戲版本",
        VariantSimplified = "簡體中文（CN）",
        VariantSimplifiedHint = "中國大陸遊戲版本",
        GeneralFallback = "缺少該行時使用另一個版本",
        GeneralFallbackHint =
            "缺少的行可能混用繁體和簡體字。預設關閉：它會載入兩個資料檔案，" +
            "為了少量額外的行而讓載入時間加倍。",
        SectionConversion = "轉換顯示的文字",
        GeneralConversionHelp = "選用。只改變查找後顯示的字形，不改變提供翻譯的遊戲版本。",
        ConversionNone = "不轉換（保留官方文字）",
        ConversionToTraditional = "簡體轉繁體（CN 轉 TC）",
        ConversionToSimplified = "繁體轉簡體（TC 轉 CN）",
        SectionFontOverlay = "字型與覆蓋視窗",
        FontSize = "字型大小",
        OverlayOpacityDefault = "背景不透明度（預設）",
        CustomFontPath = "自訂字型路徑（選用，.ttf/.ttc/.otf）",
        SectionColors = "顏色（預設）",
        ColorNpcName = "NPC 名稱",
        ColorTranslatedText = "翻譯文字",
        ColorOriginalText = "原文",
        GeneralColorHint = "道具名稱的語言在另一個分頁，姓名板開關也在另一個分頁。",
        SectionSharedActions = "共用動作",
        UseDefaultColorsAll = "所有組件使用預設顏色",
        ResetAll = "重設所有設定",

        TabTalk = "對話",
        TabSubtitles = "字幕",
        TabChatBubbles = "聊天氣泡",
        TabBattleTalk = "戰鬥台詞",
        TabSelection = "選擇",
        TabNameplates = "姓名板",
        TabItemName = "道具名稱",

        TalkEnable = "在對話視窗中啟用翻譯",
        SectionMode = "模式",
        ModeOverlay = "覆蓋視窗",
        ModeNative = "原生（遊戲自己的視窗）",
        TalkNativeWarning =
            "原生模式會把中文寫入遊戲自己的對話框和字幕列。" +
            "遊戲字型沒有中文字形，請先安裝字型模組。",
        OpenFontGuide = "開啟字型設定指南",
        GameFontInGeneral = "遊戲字型設定位於「一般」分頁。",
        SectionGameFont = "遊戲字型（原生模式）",
        GameFontHint =
            "原生模式用遊戲自己的字型顯示中文，所以取代字型會影響整個遊戲。" +
            "取代只在 Sinophone 啟用時生效，關閉後即恢復原字型。" +
            "透過 Penumbra 或 TexTools 安裝的字型不受此處控制。",
        BuiltInFont = "用字型包取代遊戲字型",
        BuiltInFontHint =
            ".ttmp2 字型包，或包含遊戲字型檔案的資料夾。" +
            "國際服請使用字型包的 (Global) 版本。" +
            "外掛只從你的磁碟讀取，不附帶字型資料。",
        ChooseFontFile = "選擇 .ttmp2",
        ChooseFontFolder = "選擇資料夾",
        ReloadFont = "重新載入遊戲字型",
        RestoreFont = "恢復遊戲原字型",
        SectionDisplay = "顯示",
        ShowSpeakerName = "顯示說話者名稱",
        ShowSpeakerNameHint = "中文名稱會和英文名稱一起顯示在括號中。",
        HideWhenNoTranslation = "找不到翻譯時隱藏",
        HideWhenNoTranslationHint =
            "關閉時，沒有翻譯的行會回退為英文原文。" +
            "覆蓋視窗不會同時顯示兩種語言。",
        SectionOtherReaders = "其他讀取者看到的內容",
        OtherReadersHint =
            "對話框始終顯示中文。這裡決定遊戲自己的記錄和其他外掛讀到這一行時看到什麼。",
        ShareOriginal = "英文原文",
        ShareTranslation = "中文翻譯",
        SectionOverlayPosition = "覆蓋視窗位置",
        LockOverlay = "鎖定覆蓋視窗位置",
        Clickthrough = "滑鼠穿透",
        OverlayWidth = "寬度",
        OverlayOffsetX = "水平位移（0 = 置中）",
        OffsetFromBottom = "距螢幕底部距離",
        AnchorOverlayBottom = "文字變多時固定底行",
        PreviewOverlay = "預覽覆蓋視窗",
        ResetTalkLayout = "重設對話版面",
        SectionAppearance = "外觀",
        UseDefaultAppearance = "使用預設顏色和不透明度",
        FollowingDefaults = "跟隨「一般」分頁中的預設值。",
        ColorSpeakerName = "說話者名稱",
        ColorUntranslatedFallback = "未翻譯回退",
        BackgroundOpacity = "背景不透明度",

        SubtitleEnable = "在對話字幕中啟用翻譯",
        SubtitleHint = "字幕使用對話覆蓋視窗的位置和外觀。",
        SubtitleModeHint =
            "「對話」分頁的「模式」設定也決定字幕的繪製方式：遊戲自己的字幕列，" +
            "或在字幕列放不下時使用覆蓋視窗。",

        BubbleEnable = "翻譯 NPC 聊天氣泡",
        BubbleHint = "中文會寫入 NPC 頭頂遊戲自己的氣泡中。",
        BubbleWarning =
            "聊天氣泡使用遊戲自己的字型，需要字型模組。" +
            "氣泡版面仍在遊戲內檢查，因此預設關閉。",

        BattleTalkEnable = "翻譯戰鬥台詞",
        BattleTalkHint =
            "首領喊話、副本旁白和任務 NPC 台詞，顯示在螢幕上方的遊戲自己的框中。" +
            "名稱跟隨「對話」分頁的說話者名稱設定。",
        BattleTalkWarning =
            "戰鬥台詞使用遊戲自己的字型，需要字型模組。沒有翻譯的行保持英文，" +
            "關閉開關時已經在螢幕上的行也保持英文。預設關閉。",

        SelectionEnable = "在選擇對話框中啟用翻譯",
        SelectionWarning =
            "選擇對話框原地翻譯，沒有覆蓋視窗。" +
            "這需要中日韓字型模組，否則中文會顯示缺字符號。",
        SelectionHint = "提示和選項文字會在遊戲自己的對話框中取代。",
        SelectionAdvanced = "進階：選擇對話框類型",
        SelectionYesNo = "是 / 否提示",
        SelectionString = "文字選項清單",
        SelectionIcon = "圖示選項清單",
        SelectionOk = "單一確定提示",
        SelectionCutScene = "過場選項",

        NameplateWarning =
            "中文名稱會寫入每個可見 NPC 和物件（例如召喚鈴、部隊置物櫃或門）上方" +
            "遊戲自己的姓名板。需要字型模組，否則名稱會顯示缺字符號。" +
            "遊戲設定中必須顯示姓名板。",
        NameplateEnable = "在姓名板上顯示中文名稱",
        NameplateHint = "每個可見的姓名板都會翻譯，不只是目標。玩家名稱不翻譯。",
        NameplateShowEnglish = "同時顯示英文名稱",
        BilingualHint = "英文名稱跟在中文名稱後面，放在括號中，同一行顯示。",

        ItemEnable = "顯示滑鼠所指道具的中文名稱",
        ItemNative = "原生道具提示",
        ItemNativeWarning =
            "原生模式會把名稱寫入遊戲自己的道具提示。" +
            "遊戲字型沒有中文字形，請先安裝字型模組。",
        ItemNativeFallback = "外掛無法寫入的提示會回退到覆蓋視窗。",
        ItemOverlayHint = "名稱跟隨滑鼠，顯示在道具提示上方。",
        SectionLanguage = "語言",
        ItemChineseOnly = "僅中文",
        ItemBoth = "中文和英文",
        ItemNativeAppearance = "原生模式使用遊戲自己的顏色和字級繪製名稱。",
        ColorTranslatedName = "翻譯名稱",
        ColorEnglishName = "英文名稱",

        DataDownloadOnLoad = "載入時下載資料更新",
        DataDownloadOnLoadHint = "在背景執行。新資料準備好之前，舊資料會繼續使用。",
        DataDownloadBoth = "下載兩種語言版本",
        DataDownloadBothHint =
            "關閉時只下載所選的版本。" +
            "回退僅在另一個版本已在磁碟上時使用它。",
        Cancel = "取消",
        DownloadUpdate = "下載更新",
        DownloadOrUpdate = "下載 / 更新資料",
        CheckForUpdates = "檢查更新",

        LogsIntro =
            "選擇外掛的哪些訊息出現在 Dalamud 外掛記錄中。" +
            "每個開關隱藏一個群組的詳細訊息。警告和錯誤始終顯示。",
        ShowEveryGroup = "顯示所有群組",
        HideEveryDetail = "隱藏所有詳細訊息",
        SectionGroups = "群組",
        LogDialogue = "對話查找",
        LogDialogueHint = "每一行對話和字幕，以及回答它的查找。",
        LogNativeText = "原生文字",
        LogNativeTextHint = "每次寫入遊戲自己的對話框。",
        LogSelection = "選擇對話框",
        LogSelectionHint = "外掛翻譯的每個提示和選項。",
        LogNameplates = "姓名板",
        LogNameplatesHint = "每個寫入姓名板的名稱一行。",
        LogItemNames = "道具名稱",
        LogItemNamesHint = "沒有找到翻譯的道具名稱查找。",
        LogData = "翻譯資料",
        LogDataHint = "載入、索引和下載檢查。",
        LogPlugin = "外掛",
        LogPluginHint = "載入、卸載和設定變更。",

        StatusWarning =
            "早期開發階段。部分 NPC 對話無法辨識，" +
            "任何遊戲資料匯出中都不存在的文字保持未翻譯。",

        StatusNoData = "沒有載入翻譯資料。請在設定中使用「下載 / 更新資料」。",
        StatusFont = "字型：{0}",
        StatusFontMissing = "字型：找不到（中文可能顯示為方框）。",
        StatusSystemFontSuffix = "（系統字型）",
        StatusVariantSimplified = "簡體",
        StatusVariantTraditional = "繁體",
        StatusFallbackSuffix = " + 後備",
        StatusSummary = "{0}：{1:N0} 條對話項目，{2:N0} 條名稱項目。\n{3}",
        DataSourceBuiltIn = "使用外掛內建的翻譯資料。",
        DataSourceMissingVariant = "使用已下載的資料。缺少一個版本，使用內建副本。",
        DataSourceSelectedOnly = "使用已下載的資料（{0}），僅限所選版本。",
        DataSourceBoth = "使用已下載的資料（{0}）。",
        DataSourceUnknownVersion = "未知版本",
        DataSourceVersion = "版本 {0}",
        AboutText = "Sinophone {0}，作者 {1}",
    };
}
