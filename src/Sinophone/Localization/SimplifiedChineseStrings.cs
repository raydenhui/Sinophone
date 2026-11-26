namespace Sinophone.Localization;

internal static class SimplifiedChineseStrings
{
    public static readonly InterfaceStrings Value = new()
    {
        TabGeneral = "常规",
        TabComponents = "组件",
        TabData = "数据",
        TabLogs = "日志",
        TabStatus = "状态",

        SectionInterface = "界面",
        InterfaceLanguageLabel = "语言",
        InterfaceLanguageHint = "只改变设置窗口。翻译仍由下方的中文文本来源决定。",
        InterfaceLanguageAuto = "跟随 Dalamud",
        LanguageEnglish = "英文",
        LanguageSimplifiedChinese = "简体中文",
        LanguageTraditionalChinese = "繁体中文",
        LanguageJapanese = "日文",

        GeneralEnable = "启用翻译",
        SectionChineseSource = "中文文本来源",
        GeneralSourceHelp =
            "选择提供翻译的官方游戏版本。TC 是台湾和香港的繁体中文版本，" +
            "CN 是中国大陆的简体中文版本。",
        VariantTraditional = "繁体中文（TC）",
        VariantTraditionalHint = "台湾 / 香港游戏版本",
        VariantSimplified = "简体中文（CN）",
        VariantSimplifiedHint = "中国大陆游戏版本",
        GeneralFallback = "缺少该行时使用另一个版本",
        GeneralFallbackHint =
            "缺失的行可能混用繁体和简体字。默认关闭：它会加载两个数据文件，" +
            "为少量额外的行让加载时间翻倍。",
        SectionConversion = "转换显示的文字",
        GeneralConversionHelp = "可选。只改变查找后显示的字形，不改变提供翻译的游戏版本。",
        ConversionNone = "不转换（保留官方文字）",
        ConversionToTraditional = "简体转繁体（CN 转 TC）",
        ConversionToSimplified = "繁体转简体（TC 转 CN）",
        SectionFontOverlay = "字体与覆盖窗口",
        FontSize = "字体大小",
        OverlayOpacityDefault = "背景不透明度（默认）",
        CustomFontPath = "自定义字体路径（可选，.ttf/.ttc/.otf）",
        SectionColors = "颜色（默认）",
        ColorNpcName = "NPC 名称",
        ColorTranslatedText = "翻译文字",
        ColorOriginalText = "原文",
        GeneralColorHint = "道具名称的语言在单独的标签页，姓名板开关也在单独的标签页。",
        SectionSharedActions = "共用操作",
        UseDefaultColorsAll = "所有组件使用默认颜色",
        ResetAll = "重置所有设置",

        TabTalk = "对话",
        TabSubtitles = "字幕",
        TabChatBubbles = "聊天气泡",
        TabBattleTalk = "战斗台词",
        TabSelection = "选择",
        TabNameplates = "姓名板",
        TabItemName = "道具名称",

        TalkEnable = "在对话窗口中启用翻译",
        SectionMode = "模式",
        ModeOverlay = "覆盖窗口",
        ModeNative = "原生（游戏自己的窗口）",
        TalkNativeWarning =
            "原生模式会把中文写入游戏自己的对话框和字幕栏。" +
            "游戏字体没有中文字形，请先安装字体模组。",
        OpenFontGuide = "打开字体设置指南",
        SectionChineseFont = "中文字体",
        BuiltInFont = "用字体包替换游戏字体",
        BuiltInFontHint =
            ".ttmp2 字体模组，或包含游戏字体文件的文件夹。" +
            "插件只从你的磁盘读取，不附带字体数据。",
        ReloadFont = "重新加载游戏字体",
        SectionDisplay = "显示",
        ShowSpeakerName = "显示说话者名称",
        ShowSpeakerNameHint = "中文名称会和英文名称一起显示在括号中。",
        HideWhenNoTranslation = "找不到翻译时隐藏",
        HideWhenNoTranslationHint =
            "关闭时，没有翻译的行会回退为英文原文。" +
            "覆盖窗口不会同时显示两种语言。",
        SectionOtherReaders = "其他读取者看到的内容",
        OtherReadersHint =
            "对话框始终显示中文。这里决定游戏自己的日志和其他插件读到这一行时看到什么。",
        ShareOriginal = "英文原文",
        ShareTranslation = "中文翻译",
        SectionOverlayPosition = "覆盖窗口位置",
        LockOverlay = "锁定覆盖窗口位置",
        Clickthrough = "鼠标穿透",
        OverlayWidth = "宽度",
        OverlayOffsetX = "水平偏移（0 = 居中）",
        OffsetFromBottom = "距屏幕底部距离",
        AnchorOverlayBottom = "文字变多时固定底行",
        PreviewOverlay = "预览覆盖窗口",
        ResetTalkLayout = "重置对话布局",
        SectionAppearance = "外观",
        UseDefaultAppearance = "使用默认颜色和不透明度",
        FollowingDefaults = "跟随“常规”标签页中的默认值。",
        ColorSpeakerName = "说话者名称",
        ColorUntranslatedFallback = "未翻译回退",
        BackgroundOpacity = "背景不透明度",

        SubtitleEnable = "在对话字幕中启用翻译",
        SubtitleHint = "字幕使用对话覆盖窗口的位置和外观。",
        SubtitleModeHint =
            "“对话”标签页的“模式”设置也决定字幕的绘制方式：游戏自己的字幕栏，" +
            "或在字幕栏放不下时使用覆盖窗口。",

        BubbleEnable = "翻译 NPC 聊天气泡",
        BubbleHint = "中文会写入 NPC 头顶游戏自己的气泡中。",
        BubbleWarning =
            "聊天气泡使用游戏自己的字体，需要字体模组。" +
            "气泡布局仍在游戏内检查，因此默认关闭。",

        BattleTalkEnable = "翻译战斗台词",
        BattleTalkHint =
            "首领喊话、副本旁白和任务 NPC 台词，显示在屏幕上方的游戏自己的框中。" +
            "名称跟随“对话”标签页的说话者名称设置。",
        BattleTalkWarning =
            "战斗台词使用游戏自己的字体，需要字体模组。没有翻译的行保持英文，" +
            "关闭开关时已经在屏幕上的行也保持英文。默认关闭。",

        SelectionEnable = "在选择对话框中启用翻译",
        SelectionWarning =
            "选择对话框原地翻译，没有覆盖窗口。" +
            "这需要中日韩字体模组，否则中文会显示缺字符号。",
        SelectionHint = "提示和选项文字会在游戏自己的对话框中替换。",
        SelectionAdvanced = "高级：选择对话框类型",
        SelectionYesNo = "是 / 否提示",
        SelectionString = "文字选项列表",
        SelectionIcon = "图标选项列表",
        SelectionOk = "单一确定提示",
        SelectionCutScene = "过场选项",

        NameplateWarning =
            "中文名称会写入每个可见 NPC 和物件（例如召唤铃、部队储物柜或门）上方" +
            "游戏自己的姓名板。需要字体模组，否则名称会显示缺字符号。" +
            "游戏设置中必须显示姓名板。",
        NameplateEnable = "在姓名板上显示中文名称",
        NameplateHint = "每个可见的姓名板都会翻译，不只是目标。玩家名称不翻译。",
        NameplateShowEnglish = "同时显示英文名称",
        BilingualHint = "英文名称跟在中文名称后面，放在括号中，同一行显示。",

        ItemEnable = "显示鼠标所指道具的中文名称",
        ItemNative = "原生道具提示",
        ItemNativeWarning =
            "原生模式会把名称写入游戏自己的道具提示。" +
            "游戏字体没有中文字形，请先安装字体模组。",
        ItemNativeFallback = "插件无法写入的提示会回退到覆盖窗口。",
        ItemOverlayHint = "名称跟随鼠标，显示在道具提示上方。",
        SectionLanguage = "语言",
        ItemChineseOnly = "仅中文",
        ItemBoth = "中文和英文",
        ItemNativeAppearance = "原生模式使用游戏自己的颜色和字号绘制名称。",
        ColorTranslatedName = "翻译名称",
        ColorEnglishName = "英文名称",

        DataDownloadOnLoad = "加载时下载数据更新",
        DataDownloadOnLoadHint = "在后台运行。新数据准备好之前，旧数据会继续使用。",
        DataDownloadBoth = "下载两种语言版本",
        DataDownloadBothHint =
            "关闭时只下载所选的版本。" +
            "回退仅在另一个版本已在磁盘上时使用它。",
        Cancel = "取消",
        DownloadUpdate = "下载更新",
        DownloadOrUpdate = "下载 / 更新数据",
        CheckForUpdates = "检查更新",

        LogsIntro =
            "选择插件的哪些消息出现在 Dalamud 插件日志中。" +
            "每个开关隐藏一个组的详细消息。警告和错误始终显示。",
        ShowEveryGroup = "显示所有组",
        HideEveryDetail = "隐藏所有详细消息",
        SectionGroups = "组",
        LogDialogue = "对话查找",
        LogDialogueHint = "每一行对话和字幕，以及回答它的查找。",
        LogNativeText = "原生文字",
        LogNativeTextHint = "每次写入游戏自己的对话框。",
        LogSelection = "选择对话框",
        LogSelectionHint = "插件翻译的每个提示和选项。",
        LogNameplates = "姓名板",
        LogNameplatesHint = "每个写入姓名板的名称一行。",
        LogItemNames = "道具名称",
        LogItemNamesHint = "没有找到翻译的道具名称查找。",
        LogData = "翻译数据",
        LogDataHint = "加载、索引和下载检查。",
        LogPlugin = "插件",
        LogPluginHint = "加载、卸载和设置变更。",

        StatusWarning =
            "早期开发阶段。部分 NPC 对话无法识别，" +
            "任何游戏数据导出中都不存在的文字保持未翻译。",

        StatusNoData = "没有加载翻译数据。请在设置中使用“下载 / 更新数据”。",
        StatusFont = "字体：{0}",
        StatusFontMissing = "字体：未找到（中文可能显示为方框）。",
        StatusSystemFontSuffix = "（系统字体）",
        StatusVariantSimplified = "简体",
        StatusVariantTraditional = "繁体",
        StatusFallbackSuffix = " + 后备",
        StatusSummary = "{0}：{1:N0} 条对话条目，{2:N0} 条名称条目。\n{3}",
        DataSourceBuiltIn = "使用插件内置的翻译数据。",
        DataSourceMissingVariant = "使用已下载的数据。缺少一个版本，使用内置副本。",
        DataSourceSelectedOnly = "使用已下载的数据（{0}），仅限所选版本。",
        DataSourceBoth = "使用已下载的数据（{0}）。",
        DataSourceUnknownVersion = "未知版本",
        DataSourceVersion = "版本 {0}",
        AboutText = "Sinophone {0}，作者 {1}",
    };
}
