namespace Sinophone.Localization;

internal static class JapaneseStrings
{
    public static readonly InterfaceStrings Value = new()
    {
        TabGeneral = "一般",
        TabComponents = "コンポーネント",
        TabData = "データ",
        TabLogs = "ログ",
        TabStatus = "ステータス",

        SectionInterface = "インターフェース",
        InterfaceLanguageLabel = "言語",
        InterfaceLanguageHint =
            "設定ウィンドウのみ変わります。翻訳は下の中国語テキストのソースに従います。",
        InterfaceLanguageAuto = "Dalamud に合わせる",
        LanguageEnglish = "英語",
        LanguageSimplifiedChinese = "簡体字中国語",
        LanguageTraditionalChinese = "繁体字中国語",
        LanguageJapanese = "日本語",

        GeneralEnable = "翻訳を有効にする",
        SectionChineseSource = "中国語テキストのソース",
        GeneralSourceHelp =
            "翻訳を提供する公式ゲームバージョンを選択します。" +
            "TC は台湾・香港向けの繁体字中国語版、CN は中国本土向けの簡体字中国語版です。",
        VariantTraditional = "繁体字中国語（TC）",
        VariantTraditionalHint = "台湾 / 香港のゲームバージョン",
        VariantSimplified = "簡体字中国語（CN）",
        VariantSimplifiedHint = "中国本土のゲームバージョン",
        GeneralFallback = "行がないときにもう一方のバージョンを使う",
        GeneralFallbackHint =
            "不足している行で繁体字と簡体字が混在することがあります。既定ではオフです。" +
            "2 つのデータファイルを読み込むため、わずかな行のために読み込み時間が倍になります。",
        SectionConversion = "表示テキストを変換",
        GeneralConversionHelp =
            "任意。参照後の文字だけを変え、翻訳を提供するゲームバージョンは変わりません。",
        ConversionNone = "変換しない（公式テキストのまま）",
        ConversionToTraditional = "簡体字から繁体字（CN → TC）",
        ConversionToSimplified = "繁体字から簡体字（TC → CN）",
        SectionFontOverlay = "フォントとオーバーレイ",
        FontSize = "フォントサイズ",
        OverlayOpacityDefault = "背景の不透明度（既定）",
        CustomFontPath = "カスタムフォントのパス（任意、.ttf/.ttc/.otf）",
        SectionColors = "色（既定）",
        ColorNpcName = "NPC 名",
        ColorTranslatedText = "翻訳テキスト",
        ColorOriginalText = "原文",
        GeneralColorHint =
            "アイテム名の言語は別のタブにあります。ネームプレートのスイッチも別のタブです。",
        SectionSharedActions = "共通の操作",
        UseDefaultColorsAll = "すべてのコンポーネントに既定の色を使う",
        ResetAll = "すべての設定をリセット",

        TabTalk = "会話",
        TabSubtitles = "字幕",
        TabChatBubbles = "チャットバブル",
        TabBattleTalk = "バトルテキスト",
        TabSelection = "選択",
        TabNameplates = "ネームプレート",
        TabItemName = "アイテム名",

        TalkEnable = "会話ウィンドウの翻訳を有効にする",
        SectionMode = "モード",
        ModeOverlay = "オーバーレイ",
        ModeNative = "ネイティブ（ゲーム自身のウィンドウ）",
        TalkNativeWarning =
            "ネイティブモードはゲーム自身の会話ボックスと字幕帯に中国語を書き込みます。" +
            "ゲームのフォントには中国語の字形がないため、先にフォント MOD を導入してください。",
        OpenFontGuide = "フォント設定ガイドを開く",
        GameFontInGeneral = "ゲームのフォント設定は「一般」タブにあります。",
        SectionGameFont = "ゲームのフォント（ネイティブモード）",
        GameFontHint =
            "ネイティブモードはゲーム自身のフォントで中国語を描画するため、置き換えると" +
            "ゲーム全体に影響します。置き換えは Sinophone の有効中だけ動作し、オフにすると" +
            "元のフォントに戻ります。Penumbra や TexTools で導入したフォントは別です。",
        BuiltInFont = "フォントパックでゲームのフォントを置き換える",
        BuiltInFontHint =
            ".ttmp2 フォントパック、またはゲームのフォントファイルを含むフォルダー。" +
            "グローバル版ではパックの (Global) 版を使用してください。" +
            "プラグインはディスクから読むだけで、フォントデータは同梱しません。",
        ChooseFontFile = ".ttmp2 を選択",
        ChooseFontFolder = "フォルダーを選択",
        ReloadFont = "ゲームのフォントを再読み込み",
        RestoreFont = "ゲーム本来のフォントに戻す",
        SectionDisplay = "表示",
        ShowSpeakerName = "話者名を表示",
        ShowSpeakerNameHint = "中国語名は英語名とともに括弧内に表示されます。",
        HideWhenNoTranslation = "翻訳がないときは隠す",
        HideWhenNoTranslationHint =
            "オフのとき、翻訳のない行は英語の原文に戻ります。" +
            "オーバーレイは 2 言語を同時に表示しません。",
        SectionOtherReaders = "他の読み取り側が見る内容",
        OtherReadersHint =
            "会話ボックスには常に中国語が表示されます。ここでは、同じ行をゲームのログや" +
            "他のプラグインが読むときの内容を決めます。",
        ShareOriginal = "英語の原文",
        ShareTranslation = "中国語の翻訳",
        SectionOverlayPosition = "オーバーレイの位置",
        LockOverlay = "オーバーレイの位置を固定",
        Clickthrough = "クリックを透過",
        OverlayWidth = "幅",
        OverlayOffsetX = "横方向のオフセット（0 = 中央）",
        OffsetFromBottom = "画面下端からの距離",
        AnchorOverlayBottom = "テキストが増えたら最終行を固定",
        PreviewOverlay = "オーバーレイをプレビュー",
        ResetTalkLayout = "会話のレイアウトをリセット",
        SectionAppearance = "外観",
        UseDefaultAppearance = "既定の色と不透明度を使う",
        FollowingDefaults = "「一般」タブの既定値に従っています。",
        ColorSpeakerName = "話者名",
        ColorUntranslatedFallback = "未翻訳のフォールバック",
        BackgroundOpacity = "背景の不透明度",

        SubtitleEnable = "会話の字幕の翻訳を有効にする",
        SubtitleHint = "字幕は会話オーバーレイの位置と外観を使います。",
        SubtitleModeHint =
            "「会話」タブの「モード」設定は字幕の描画方法も決めます。ゲーム自身の字幕帯か、" +
            "字幕帯に収まらないときはオーバーレイです。",

        BubbleEnable = "NPC のチャットバブルを翻訳",
        BubbleHint = "中国語は NPC の頭上のゲーム自身のバブルに書き込まれます。",
        BubbleWarning =
            "チャットバブルはゲーム自身のフォントを使い、フォント MOD が必要です。" +
            "バブルのレイアウトはまだゲーム内で確認中のため、既定ではオフです。",

        BattleTalkEnable = "バトルテキストを翻訳",
        BattleTalkHint =
            "ボスの叫び、ダンジョンのナレーション、任務 NPC のセリフを、画面上部の" +
            "ゲーム自身のボックスに表示します。名前は「会話」タブの話者名設定に従います。",
        BattleTalkWarning =
            "バトルテキストはゲーム自身のフォントを使い、フォント MOD が必要です。" +
            "翻訳のない行は英語のままですが、スイッチを切ったときに画面に出ている行も" +
            "英語のままです。既定ではオフです。",

        SelectionEnable = "選択ダイアログの翻訳を有効にする",
        SelectionWarning =
            "選択ダイアログはオーバーレイなしでその場で翻訳されます。" +
            "CJK フォント MOD が必要で、ないと中国語が欠字記号で表示されます。",
        SelectionHint = "プロンプトと選択肢のラベルはゲーム自身のダイアログ内で置き換えられます。",
        SelectionAdvanced = "詳細設定：ダイアログの種類を選ぶ",
        SelectionYesNo = "はい / いいえのプロンプト",
        SelectionString = "テキスト選択肢のリスト",
        SelectionIcon = "アイコン選択肢のリスト",
        SelectionOk = "OK のみのプロンプト",
        SelectionCutScene = "カットシーンの選択",

        NameplateWarning =
            "中国語名は、見えているすべての NPC やオブジェクト（召喚ベル、カンパニーチェスト、" +
            "扉など）の上のゲーム自身のネームプレートに書き込まれます。フォント MOD が必要で、" +
            "ないと名前が欠字記号で表示されます。ゲーム設定でネームプレートを表示しておく" +
            "必要があります。",
        NameplateEnable = "ネームプレートに中国語名を表示",
        NameplateHint =
            "ターゲットだけでなく、見えているすべてのネームプレートが翻訳されます。" +
            "プレイヤー名は翻訳されません。",
        NameplateShowEnglish = "英語名も表示",
        BilingualHint = "英語名は中国語名の後に括弧でくくられ、同じ行に表示されます。",

        ItemEnable = "マウスを合わせたアイテムの中国語名を表示",
        ItemNative = "ネイティブのアイテムツールチップ",
        ItemNativeWarning =
            "ネイティブモードはゲーム自身のアイテムツールチップに名前を書き込みます。" +
            "ゲームのフォントには中国語の字形がないため、先にフォント MOD を導入してください。",
        ItemNativeFallback = "プラグインが書き込めないツールチップはオーバーレイに戻ります。",
        ItemOverlayHint = "名前はマウスに追従し、アイテムツールチップの上に表示されます。",
        SectionLanguage = "言語",
        ItemChineseOnly = "中国語のみ",
        ItemBoth = "中国語と英語",
        ItemNativeAppearance = "ネイティブモードはゲーム自身の色とサイズで名前を描画します。",
        ColorTranslatedName = "翻訳名",
        ColorEnglishName = "英語名",

        DataDownloadOnLoad = "読み込み時にデータ更新をダウンロード",
        DataDownloadOnLoadHint =
            "バックグラウンドで実行します。新しいデータの準備ができるまで、古いデータを" +
            "使い続けます。",
        DataDownloadBoth = "両方の言語バリアントをダウンロード",
        DataDownloadBothHint =
            "オフでは選択したバリアントのみをダウンロードします。フォールバックは、" +
            "もう一方のバリアントがすでにディスクにある場合のみ使います。",
        Cancel = "キャンセル",
        DownloadUpdate = "更新をダウンロード",
        DownloadOrUpdate = "データをダウンロード / 更新",
        CheckForUpdates = "更新を確認",

        LogsIntro =
            "プラグイン自身のどのメッセージを Dalamud のプラグインログに表示するか選びます。" +
            "各スイッチは 1 つのグループの詳細メッセージを隠します。警告とエラーは常に表示されます。",
        ShowEveryGroup = "すべてのグループを表示",
        HideEveryDetail = "すべての詳細メッセージを隠す",
        SectionGroups = "グループ",
        LogDialogue = "会話の照合",
        LogDialogueHint = "会話と字幕の各行と、それを解決した照合。",
        LogNativeText = "ネイティブテキスト",
        LogNativeTextHint = "ゲーム自身の会話ボックスへの各書き込み。",
        LogSelection = "選択ダイアログ",
        LogSelectionHint = "プラグインが翻訳する各プロンプトと選択肢。",
        LogNameplates = "ネームプレート",
        LogNameplatesHint = "ネームプレートに書き込む名前ごとに 1 行。",
        LogItemNames = "アイテム名",
        LogItemNamesHint = "翻訳が見つからなかったアイテム名の照合。",
        LogData = "翻訳データ",
        LogDataHint = "読み込み、インデックス作成、ダウンロード確認。",
        LogPlugin = "プラグイン",
        LogPluginHint = "読み込み、読み込み解除、設定変更。",

        StatusWarning =
            "早期開発中です。一部の NPC 会話は検出されず、" +
            "どのゲームデータにもないテキストは翻訳されません。",

        StatusNoData =
            "翻訳データが読み込まれていません。設定で「データをダウンロード / 更新」を" +
            "使ってください。",
        StatusFont = "フォント：{0}",
        StatusFontMissing = "フォント：見つかりません（中国語が四角で表示される場合があります）。",
        StatusSystemFontSuffix = "（システムフォント）",
        StatusVariantSimplified = "簡体字",
        StatusVariantTraditional = "繁体字",
        StatusFallbackSuffix = " + フォールバック",
        StatusSummary = "{0}：会話 {1:N0} 件、名前 {2:N0} 件。\n{3}",
        DataSourceBuiltIn = "プラグインに組み込まれた翻訳データを使っています。",
        DataSourceMissingVariant =
            "ダウンロードしたデータを使っています。一方のバリアントがなく、" +
            "組み込みのコピーを使います。",
        DataSourceSelectedOnly =
            "ダウンロードしたデータ（{0}）を使っています。選択したバリアントのみです。",
        DataSourceBoth = "ダウンロードしたデータ（{0}）を使っています。",
        DataSourceUnknownVersion = "不明なバージョン",
        DataSourceVersion = "バージョン {0}",
        AboutText = "Sinophone {0}、作者 {1}",
    };
}
