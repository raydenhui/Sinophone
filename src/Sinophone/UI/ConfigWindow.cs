using Action = System.Action;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Utility;

namespace Sinophone.UI;

/// <summary>
/// Settings window for Sinophone. Every change is applied and persisted immediately.
/// </summary>
public sealed partial class ConfigWindow : Window
{
    private static readonly Vector4 SectionColor = new(1f, 0.82f, 0.38f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.72f, 0.35f, 1f);

    private const string FontSetupUrl =
        "https://github.com/raydenhui/Sinophone/blob/main/README.en.md#font-setup";

    private readonly Configuration _configuration;
    private readonly InterfaceText _text;
    private readonly FontManager _fontManager;
    private readonly Action _applySettings;
    private readonly Action _showPreview;
    private readonly Func<string> _statusText;
    private readonly TranslationDataUpdater _dataUpdater;
    private readonly Func<string> _dataSourceText;
    private readonly Func<string> _aboutText;
    private readonly FontReplacementService _fontReplacement;
    private readonly FileDialogManager _fileDialog = new();

    private InterfaceStrings Text => _text.Strings;
#if SINOPHONE_TESTING
    // Set by the testing constructor, which chains into the main one.
    private readonly NativeTextProbe _nativeProbe = null!;
    private readonly TalkContextProbe _talkContextProbe = null!;
    private readonly HarnessHost? _harnessHost;
    private readonly IObjectTable? _objectTable;
    private readonly HarnessTabState _harness = new();
    private string _gameFontCheck = "Not checked yet.";
#endif

#if SINOPHONE_TESTING
    /// <summary>
    /// Testing build. Takes the probes and the run host, which add the "Testing" tab.
    /// The host is null when the build is not a dev plugin, so the tab still opens but the
    /// run controls say why they are off.
    /// </summary>
    public ConfigWindow(
        Configuration configuration,
        InterfaceText text,
        FontManager fontManager,
        Action applySettings,
        Action showPreview,
        Func<string> statusText,
        TranslationDataUpdater dataUpdater,
        Func<string> dataSourceText,
        Func<string> aboutText,
        FontReplacementService fontReplacement,
        NativeTextProbe nativeProbe,
        TalkContextProbe talkContextProbe,
        HarnessHost? harnessHost,
        IObjectTable objectTable)
        : this(configuration, text, fontManager, applySettings, showPreview, statusText, dataUpdater,
            dataSourceText, aboutText, fontReplacement)
    {
        _nativeProbe = nativeProbe;
        _talkContextProbe = talkContextProbe;
        _harnessHost = harnessHost;
        _objectTable = objectTable;
    }
#endif

    public ConfigWindow(
        Configuration configuration,
        InterfaceText text,
        FontManager fontManager,
        Action applySettings,
        Action showPreview,
        Func<string> statusText,
        TranslationDataUpdater dataUpdater,
        Func<string> dataSourceText,
        Func<string> aboutText,
        FontReplacementService fontReplacement)
        : base("Sinophone###SinophoneConfig", ImGuiWindowFlags.NoCollapse)
    {
        _configuration = configuration;
        _text = text;
        _fontManager = fontManager;
        _applySettings = applySettings;
        _showPreview = showPreview;
        _statusText = statusText;
        _dataUpdater = dataUpdater;
        _dataSourceText = dataSourceText;
        _aboutText = aboutText;
        _fontReplacement = fontReplacement;

        Size = new Vector2(540f, 680f);
        SizeCondition = ImGuiCond.FirstUseEver;
        AllowPinning = false;
        ShowCloseButton = true;
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        // The language can change in Dalamud's settings, so ask for the right font each frame.
        _fontManager.EnsureUiFont();
        var uiFont = _fontManager.UiHandle;
        using var font = uiFont is { Available: true } ? uiFont.Push() : null;

        _fileDialog.Draw();

        if (!ImGui.BeginTabBar("SinophoneTabs"))
            return;

        var text = Text;

        if (ImGui.BeginTabItem($"{text.TabGeneral}###General"))
        {
            DrawGeneralTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabComponents}###Components"))
        {
            DrawComponentsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabData}###Data"))
        {
            DrawDataTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabLogs}###Logs"))
        {
            DrawLogsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabStatus}###Status"))
        {
            DrawStatusTab();
            ImGui.EndTabItem();
        }

#if SINOPHONE_TESTING
        if (ImGui.BeginTabItem("Testing"))
        {
            DrawTestingTab();
            ImGui.EndTabItem();
        }
#endif

        ImGui.EndTabBar();
    }

    private void DrawGeneralTab()
    {
        var text = Text;

        Section(text.SectionInterface);
        ImGui.TextUnformatted(text.InterfaceLanguageLabel);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##interfaceLanguage", LanguageName(_configuration.InterfaceLanguage)))
        {
            foreach (var option in InterfaceLanguageOptions)
            {
                var selected = _configuration.InterfaceLanguage == option;
                if (ImGui.Selectable(LanguageName(option), selected) && !selected)
                {
                    _configuration.InterfaceLanguage = option;
                    _applySettings();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled(text.InterfaceLanguageHint);

        Checkbox(text.GeneralEnable, _configuration.Enabled, v => _configuration.Enabled = v);

        Section(text.SectionChineseSource);
        ImGui.TextWrapped(text.GeneralSourceHelp);

        if (ImGui.RadioButton(text.VariantTraditional,
                _configuration.Variant == ChineseVariant.Traditional))
        {
            _configuration.Variant = ChineseVariant.Traditional;
            _applySettings();
        }

        ImGui.Indent();
        ImGui.TextDisabled(text.VariantTraditionalHint);
        ImGui.Unindent();

        if (ImGui.RadioButton(text.VariantSimplified,
                _configuration.Variant == ChineseVariant.Simplified))
        {
            _configuration.Variant = ChineseVariant.Simplified;
            _applySettings();
        }

        ImGui.Indent();
        ImGui.TextDisabled(text.VariantSimplifiedHint);
        ImGui.Unindent();

        Checkbox(text.GeneralFallback, _configuration.UseFallbackVariant,
            v => _configuration.UseFallbackVariant = v);
        ImGui.Indent();
        ImGui.TextDisabled(text.GeneralFallbackHint);
        ImGui.Unindent();

        Section(text.SectionConversion);
        ImGui.TextWrapped(text.GeneralConversionHelp);

        if (ImGui.RadioButton(text.ConversionNone,
                _configuration.Conversion == ChineseConversion.None))
        {
            _configuration.Conversion = ChineseConversion.None;
            _applySettings();
        }

        if (ImGui.RadioButton(text.ConversionToTraditional,
                _configuration.Conversion == ChineseConversion.SimplifiedToTraditional))
        {
            _configuration.Conversion = ChineseConversion.SimplifiedToTraditional;
            _applySettings();
        }

        if (ImGui.RadioButton(text.ConversionToSimplified,
                _configuration.Conversion == ChineseConversion.TraditionalToSimplified))
        {
            _configuration.Conversion = ChineseConversion.TraditionalToSimplified;
            _applySettings();
        }

        DrawGameFontSettings();

        Section(text.SectionFontOverlay);
        SliderFloat(text.FontSize, _configuration.FontSize, 12f, 42f, "%.0f px",
            v => _configuration.FontSize = v);
        SliderFloat(text.OverlayOpacityDefault, _configuration.OverlayOpacity, 0f, 1f, "%.2f",
            v => _configuration.OverlayOpacity = v);

        ImGui.TextUnformatted(text.CustomFontPath);
        var fontPath = _configuration.CustomFontPath ?? string.Empty;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##customFontPath", ref fontPath, 512))
        {
            _configuration.CustomFontPath = fontPath;
            _applySettings();
        }

        Section(text.SectionColors);
        ColorEdit(text.ColorNpcName, _configuration.SpeakerNameColor,
            v => _configuration.SpeakerNameColor = v);
        ColorEdit(text.ColorTranslatedText, _configuration.TranslatedTextColor,
            v => _configuration.TranslatedTextColor = v);
        ColorEdit(text.ColorOriginalText, _configuration.OriginalTextColor,
            v => _configuration.OriginalTextColor = v);

        ImGui.TextDisabled(text.GeneralColorHint);

        Section(text.SectionSharedActions);
        if (ImGui.Button(text.UseDefaultColorsAll))
            UseGlobalAppearanceForAll();

        ImGui.SameLine();
        if (ImGui.Button(text.ResetAll))
            ResetAll();
    }

    private static readonly InterfaceLanguage[] InterfaceLanguageOptions =
    [
        InterfaceLanguage.Auto,
        InterfaceLanguage.English,
        InterfaceLanguage.SimplifiedChinese,
        InterfaceLanguage.TraditionalChinese,
        InterfaceLanguage.Japanese,
    ];

    // Names are in the current language, not each language's own script: an English window uses a
    // font without CJK glyphs, so native names would draw as boxes.
    private string LanguageName(InterfaceLanguage language)
        => language switch
        {
            InterfaceLanguage.English => Text.LanguageEnglish,
            InterfaceLanguage.SimplifiedChinese => Text.LanguageSimplifiedChinese,
            InterfaceLanguage.TraditionalChinese => Text.LanguageTraditionalChinese,
            InterfaceLanguage.Japanese => Text.LanguageJapanese,
            _ => Text.InterfaceLanguageAuto,
        };

    /// <summary>
    /// The game-wide font replacement. It lives here, not in a component tab, because every
    /// native surface draws with the same game font.
    /// </summary>
    private void DrawGameFontSettings()
    {
        var text = Text;

        Section(text.SectionGameFont);
        ImGui.TextWrapped(text.GameFontHint);

        if (ImGui.Selectable($"{text.OpenFontGuide}###generalFontGuide"))
            Util.OpenLink(FontSetupUrl);

        Checkbox(text.BuiltInFont, _configuration.BuiltInFontEnabled,
            v => _configuration.BuiltInFontEnabled = v);

        if (_configuration.BuiltInFontEnabled)
        {
            ImGui.Indent();
            ImGui.TextDisabled(text.BuiltInFontHint);
            ImGui.Unindent();

            var packPath = _configuration.BuiltInFontPackPath ?? string.Empty;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##builtInFontPackPath", ref packPath, 1024))
                _configuration.BuiltInFontPackPath = packPath;

            if (ImGui.IsItemDeactivatedAfterEdit())
                _applySettings();

            if (ImGui.Button(text.ChooseFontFile))
                ChooseFontFile();

            ImGui.SameLine();
            if (ImGui.Button(text.ChooseFontFolder))
                ChooseFontFolder();

            ImGui.SameLine();
            if (ImGui.Button(text.ReloadFont))
                _fontReplacement.Reapply();
        }

        ImGui.TextWrapped(_fontReplacement.Status);

        if (_configuration.BuiltInFontEnabled && ImGui.Button(text.RestoreFont))
        {
            // Keep the path, so turning the replacement back on needs no second choice.
            _configuration.BuiltInFontEnabled = false;
            _applySettings();
        }
    }

    private void ChooseFontFile()
    {
        _fileDialog.OpenFileDialog(Text.ChooseFontFile, ".ttmp2", (ok, path) =>
        {
            if (ok && !string.IsNullOrWhiteSpace(path))
                SetFontPack(path);
        });
    }

    private void ChooseFontFolder()
    {
        _fileDialog.OpenFolderDialog(Text.ChooseFontFolder, (ok, path) =>
        {
            if (ok && !string.IsNullOrWhiteSpace(path))
                SetFontPack(path);
        });
    }

    private void SetFontPack(string path)
    {
        _configuration.BuiltInFontPackPath = path;
        _configuration.BuiltInFontEnabled = true;
        _applySettings();
    }

    private void DrawDataTab()
    {
        var text = Text;

        ImGui.TextWrapped(_dataSourceText());

        Checkbox(text.DataDownloadOnLoad, _configuration.CheckForDataUpdates,
            v => _configuration.CheckForDataUpdates = v);
        ImGui.TextDisabled(text.DataDownloadOnLoadHint);

        Checkbox(text.DataDownloadBoth, _configuration.DownloadBothVariants,
            v => _configuration.DownloadBothVariants = v);
        ImGui.TextDisabled(text.DataDownloadBothHint);

        var state = _dataUpdater.State;

        if (state.IsBusy)
        {
            if (ImGui.Button(text.Cancel))
                _dataUpdater.Cancel();
        }
        else
        {
            if (ImGui.Button(state.HasUpdate ? text.DownloadUpdate : text.DownloadOrUpdate))
                _dataUpdater.StartUpdate();

            ImGui.SameLine();
            if (ImGui.Button(text.CheckForUpdates))
                _dataUpdater.StartCheck();
        }

        if (state.IsBusy)
            ImGui.ProgressBar(state.Progress, new Vector2(-1f, 0f));

        ImGui.TextWrapped(state.Status);

        if (!string.IsNullOrEmpty(state.LastError))
            ImGui.TextColored(ErrorColor, state.LastError);
    }

    /// <summary>
    /// Group switches for the plugin's own log messages. A group off hides its debug
    /// and info lines. Warnings and errors are not filtered, so a failure is never
    /// silent.
    /// </summary>
    private void DrawLogsTab()
    {
        var text = Text;

        ImGui.TextWrapped(text.LogsIntro);

        ImGui.Spacing();
        if (ImGui.Button(text.ShowEveryGroup))
            SetAllLogGroups(true);

        ImGui.SameLine();
        if (ImGui.Button(text.HideEveryDetail))
            SetAllLogGroups(false);

        Section(text.SectionGroups);
        LogGroup(text.LogDialogue, _configuration.LogDialogue,
            v => _configuration.LogDialogue = v,
            text.LogDialogueHint);

        LogGroup(text.LogNativeText, _configuration.LogNativeText,
            v => _configuration.LogNativeText = v,
            text.LogNativeTextHint);

        LogGroup(text.LogSelection, _configuration.LogSelectionDialogs,
            v => _configuration.LogSelectionDialogs = v,
            text.LogSelectionHint);

        LogGroup(text.LogNameplates, _configuration.LogNamePlates,
            v => _configuration.LogNamePlates = v,
            text.LogNameplatesHint);

        LogGroup(text.LogItemNames, _configuration.LogItemNames,
            v => _configuration.LogItemNames = v,
            text.LogItemNamesHint);

        LogGroup(text.LogData, _configuration.LogData,
            v => _configuration.LogData = v,
            text.LogDataHint);

        LogGroup(text.LogPlugin, _configuration.LogPlugin,
            v => _configuration.LogPlugin = v,
            text.LogPluginHint);

#if SINOPHONE_TESTING
        LogGroup("Debug probes", _configuration.LogDiagnostics,
            v => _configuration.LogDiagnostics = v,
            "The testing tab's own messages in developer builds.");
#endif
    }

    private void LogGroup(string label, bool current, Action<bool> setter, string hint)
    {
        Checkbox(label, current, setter);
        ImGui.Indent();
        ImGui.TextDisabled(hint);
        ImGui.Unindent();
    }

    private void SetAllLogGroups(bool enabled)
    {
        _configuration.LogPlugin = enabled;
        _configuration.LogDialogue = enabled;
        _configuration.LogNativeText = enabled;
        _configuration.LogSelectionDialogs = enabled;
        _configuration.LogNamePlates = enabled;
        _configuration.LogItemNames = enabled;
        _configuration.LogData = enabled;
        _configuration.LogDiagnostics = enabled;
        _applySettings();
    }

    private void DrawStatusTab()
    {
        ImGui.TextColored(SectionColor, _aboutText());

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(Text.StatusWarning);
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();

        var status = _statusText();
        if (!string.IsNullOrEmpty(status))
            ImGui.TextWrapped(status);
    }

    private static void Section(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(SectionColor, label);
        ImGui.Separator();
    }

    private void Checkbox(string label, bool current, Action<bool> setter)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            _applySettings();
        }
    }

    private void SliderFloat(string label, float current, float min, float max, string format, Action<float> setter)
    {
        var value = current;
        if (ImGui.SliderFloat(label, ref value, min, max, format))
        {
            setter(value);
            _applySettings();
        }
    }

    private void ColorEdit(string label, Vector4 current, Action<Vector4> setter)
    {
        var value = current;
        if (ImGui.ColorEdit4(label, ref value, ImGuiColorEditFlags.AlphaBar))
        {
            setter(value);
            _applySettings();
        }
    }

    private void UseGlobalAppearanceForAll()
    {
        _configuration.TalkUseGlobalAppearance = true;
        _configuration.ItemNameUseGlobalAppearance = true;
        _applySettings();
    }

    private void ResetTalkLayout()
    {
        var defaults = new Configuration();
        _configuration.OverlayWidth = defaults.OverlayWidth;
        _configuration.OverlayOffsetX = defaults.OverlayOffsetX;
        _configuration.OffsetFromBottom = defaults.OffsetFromBottom;
        _configuration.LockOverlay = defaults.LockOverlay;
        _configuration.AnchorOverlayBottom = defaults.AnchorOverlayBottom;
        _applySettings();
    }

    private void ResetAll()
    {
        // The interface language is left alone: it is not a translation setting, and resetting
        // it would change the window language under the button that was just clicked.
        var defaults = new Configuration();
        _configuration.Enabled = defaults.Enabled;
        _configuration.Variant = defaults.Variant;
        _configuration.Conversion = defaults.Conversion;
        _configuration.UseFallbackVariant = defaults.UseFallbackVariant;
        _configuration.DownloadBothVariants = defaults.DownloadBothVariants;
        _configuration.ShowInTalk = defaults.ShowInTalk;
        _configuration.ShowInTalkSubtitle = defaults.ShowInTalkSubtitle;
        _configuration.ShowInChatBubbles = defaults.ShowInChatBubbles;
        _configuration.ShowInBattleTalk = defaults.ShowInBattleTalk;
        _configuration.NativeTextMode = defaults.NativeTextMode;
        _configuration.NativeTextSharesTranslation = defaults.NativeTextSharesTranslation;
        _configuration.ShowSpeakerName = defaults.ShowSpeakerName;
        _configuration.SpeakerNameColor = defaults.SpeakerNameColor;
        _configuration.TranslatedTextColor = defaults.TranslatedTextColor;
        _configuration.OriginalTextColor = defaults.OriginalTextColor;
        _configuration.ShowTargetName = defaults.ShowTargetName;
        _configuration.TargetNameShowEnglish = defaults.TargetNameShowEnglish;
        _configuration.ShowItemName = defaults.ShowItemName;
        _configuration.ItemNameNativeMode = defaults.ItemNameNativeMode;
        _configuration.ShowInSelectionDialogs = defaults.ShowInSelectionDialogs;
        _configuration.SelectionYesNo = defaults.SelectionYesNo;
        _configuration.SelectionString = defaults.SelectionString;
        _configuration.SelectionIconString = defaults.SelectionIconString;
        _configuration.SelectionOk = defaults.SelectionOk;
        _configuration.SelectionCutScene = defaults.SelectionCutScene;
        _configuration.ShowOriginal = defaults.ShowOriginal;
        _configuration.HideWhenNoTranslation = defaults.HideWhenNoTranslation;
        _configuration.LockOverlay = defaults.LockOverlay;
        _configuration.Clickthrough = defaults.Clickthrough;
        _configuration.OverlayOpacity = defaults.OverlayOpacity;
        _configuration.TalkUseGlobalAppearance = defaults.TalkUseGlobalAppearance;
        _configuration.TalkSpeakerNameColor = defaults.TalkSpeakerNameColor;
        _configuration.TalkTranslatedTextColor = defaults.TalkTranslatedTextColor;
        _configuration.TalkOriginalTextColor = defaults.TalkOriginalTextColor;
        _configuration.TalkOverlayOpacity = defaults.TalkOverlayOpacity;
        _configuration.ItemNameUseGlobalAppearance = defaults.ItemNameUseGlobalAppearance;
        _configuration.ItemNameColor = defaults.ItemNameColor;
        _configuration.ItemNameOriginalColor = defaults.ItemNameOriginalColor;
        _configuration.ItemNameOpacity = defaults.ItemNameOpacity;
        _configuration.LogPlugin = defaults.LogPlugin;
        _configuration.LogDialogue = defaults.LogDialogue;
        _configuration.LogNativeText = defaults.LogNativeText;
        _configuration.LogSelectionDialogs = defaults.LogSelectionDialogs;
        _configuration.LogNamePlates = defaults.LogNamePlates;
        _configuration.LogItemNames = defaults.LogItemNames;
        _configuration.LogData = defaults.LogData;
        _configuration.LogDiagnostics = defaults.LogDiagnostics;
        _configuration.FontSize = defaults.FontSize;
        _configuration.OverlayWidth = defaults.OverlayWidth;
        _configuration.OverlayOffsetX = defaults.OverlayOffsetX;
        _configuration.OffsetFromBottom = defaults.OffsetFromBottom;
        _configuration.AnchorOverlayBottom = defaults.AnchorOverlayBottom;
        _configuration.CustomFontPath = defaults.CustomFontPath;
        _configuration.BuiltInFontEnabled = defaults.BuiltInFontEnabled;
        _configuration.BuiltInFontPackPath = defaults.BuiltInFontPackPath;
        _configuration.CheckForDataUpdates = defaults.CheckForDataUpdates;
        _applySettings();
    }
}
