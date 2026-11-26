using Action = System.Action;
using Dalamud.Utility;

namespace Sinophone.UI;

/// <summary>
/// Settings window for Sinophone. Every change is applied and persisted immediately.
/// </summary>
public sealed class ConfigWindow : Window
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

    private InterfaceStrings Text => _text.Strings;
#if SINOPHONE_TESTING
    // Set by the testing constructor, which chains into the main one.
    private readonly NativeTextProbe _nativeProbe = null!;
    private readonly TalkContextProbe _talkContextProbe = null!;
    private readonly HarnessHost? _harnessHost;
    private readonly IObjectTable? _objectTable;
    private readonly HarnessTabState _harness = new();
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

    private void DrawComponentsTab()
    {
        if (!ImGui.BeginTabBar("SinophoneComponentTabs"))
            return;

        var text = Text;

        if (ImGui.BeginTabItem($"{text.TabTalk}###ComponentTalk"))
        {
            DrawTalkComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabSubtitles}###ComponentSubtitles"))
        {
            DrawSubtitleComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabChatBubbles}###ComponentChatBubbles"))
        {
            DrawChatBubbleComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabBattleTalk}###ComponentBattleTalk"))
        {
            DrawBattleTalkComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabSelection}###ComponentSelection"))
        {
            DrawSelectionComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabNameplates}###ComponentNameplates"))
        {
            DrawTargetNameComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{text.TabItemName}###ComponentItemName"))
        {
            DrawItemNameComponentTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawTalkComponentTab()
    {
        var text = Text;

        Checkbox(text.TalkEnable, _configuration.ShowInTalk,
            v => _configuration.ShowInTalk = v);

        Section(text.SectionMode);
        if (ImGui.RadioButton($"{text.ModeOverlay}###talkOverlay", !_configuration.NativeTextMode))
        {
            _configuration.NativeTextMode = false;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton($"{text.ModeNative}###talkNative", _configuration.NativeTextMode))
        {
            _configuration.NativeTextMode = true;
            _applySettings();
        }

        if (_configuration.NativeTextMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
            ImGui.TextWrapped(text.TalkNativeWarning);
            ImGui.PopStyleColor();

            if (ImGui.Selectable($"{text.OpenFontGuide}###talkFontGuide"))
                Util.OpenLink(FontSetupUrl);

            ImGui.TextDisabled(FontSetupUrl);
        }

        Section(text.SectionChineseFont);
        Checkbox(text.BuiltInFont, _configuration.BuiltInFontEnabled,
            v => _configuration.BuiltInFontEnabled = v);

        if (_configuration.BuiltInFontEnabled)
        {
            ImGui.TextDisabled(text.BuiltInFontHint);
            var packPath = _configuration.BuiltInFontPackPath ?? string.Empty;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##builtInFontPackPath", ref packPath, 1024))
                _configuration.BuiltInFontPackPath = packPath;

            if (ImGui.IsItemDeactivatedAfterEdit())
                _applySettings();

            if (ImGui.Button(text.ReloadFont))
                _fontReplacement.Reapply();

            ImGui.SameLine();
            ImGui.TextWrapped(_fontReplacement.Status);
        }

        Section(text.SectionDisplay);
        Checkbox(text.ShowSpeakerName, _configuration.ShowSpeakerName,
            v => _configuration.ShowSpeakerName = v);
        ImGui.TextDisabled(text.ShowSpeakerNameHint);

        Checkbox(text.HideWhenNoTranslation, _configuration.HideWhenNoTranslation,
            v => _configuration.HideWhenNoTranslation = v);
        ImGui.TextDisabled(text.HideWhenNoTranslationHint);

        if (_configuration.NativeTextMode)
        {
            Section(text.SectionOtherReaders);
            ImGui.TextDisabled(text.OtherReadersHint);

            if (ImGui.RadioButton($"{text.ShareOriginal}###shareEnglish",
                    !_configuration.NativeTextSharesTranslation))
            {
                _configuration.NativeTextSharesTranslation = false;
                _applySettings();
            }

            ImGui.SameLine();
            if (ImGui.RadioButton($"{text.ShareTranslation}###shareChinese",
                    _configuration.NativeTextSharesTranslation))
            {
                _configuration.NativeTextSharesTranslation = true;
                _applySettings();
            }
        }

        Section(text.SectionOverlayPosition);
        ImGui.BeginDisabled(_configuration.NativeTextMode);
        Checkbox(text.LockOverlay, _configuration.LockOverlay,
            v => _configuration.LockOverlay = v);
        Checkbox(text.Clickthrough, _configuration.Clickthrough,
            v => _configuration.Clickthrough = v);
        SliderFloat($"{text.OverlayWidth}###talkWidth", _configuration.OverlayWidth, 280f, 1200f, "%.0f",
            v => _configuration.OverlayWidth = v);
        SliderFloat($"{text.OverlayOffsetX}###overlayOffsetX", _configuration.OverlayOffsetX,
            -1000f, 1000f, "%.0f px", v => _configuration.OverlayOffsetX = v);
        SliderFloat($"{text.OffsetFromBottom}###offsetFromBottom", _configuration.OffsetFromBottom,
            20f, 520f, "%.0f", v => _configuration.OffsetFromBottom = v);
        Checkbox(text.AnchorOverlayBottom, _configuration.AnchorOverlayBottom,
            v => _configuration.AnchorOverlayBottom = v);
        ImGui.EndDisabled();

        ImGui.Spacing();
        if (ImGui.Button(text.PreviewOverlay))
            _showPreview();

        ImGui.SameLine();
        if (ImGui.Button(text.ResetTalkLayout))
            ResetTalkLayout();

        Section(text.SectionAppearance);
        Checkbox(text.UseDefaultAppearance, _configuration.TalkUseGlobalAppearance,
            v => _configuration.TalkUseGlobalAppearance = v);

        if (_configuration.TalkUseGlobalAppearance)
        {
            ImGui.TextDisabled(text.FollowingDefaults);
        }
        else
        {
            ColorEdit($"{text.ColorSpeakerName}###talkSpeakerColor", _configuration.TalkSpeakerNameColor,
                v => _configuration.TalkSpeakerNameColor = v);
            ColorEdit($"{text.ColorTranslatedText}###talkTranslatedColor",
                _configuration.TalkTranslatedTextColor,
                v => _configuration.TalkTranslatedTextColor = v);
            ColorEdit($"{text.ColorUntranslatedFallback}###talkOriginalColor",
                _configuration.TalkOriginalTextColor,
                v => _configuration.TalkOriginalTextColor = v);
            SliderFloat($"{text.BackgroundOpacity}###talkOpacity", _configuration.TalkOverlayOpacity,
                0f, 1f, "%.2f", v => _configuration.TalkOverlayOpacity = v);
        }
    }

    private void DrawChatBubbleComponentTab()
    {
        var text = Text;

        Checkbox(text.BubbleEnable, _configuration.ShowInChatBubbles,
            v => _configuration.ShowInChatBubbles = v);

        ImGui.TextDisabled(text.BubbleHint);

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(text.BubbleWarning);
        ImGui.PopStyleColor();

        if (ImGui.Selectable($"{text.OpenFontGuide}###bubbleFontGuide"))
            Util.OpenLink(FontSetupUrl);
    }

    private void DrawBattleTalkComponentTab()
    {
        var text = Text;

        Checkbox(text.BattleTalkEnable, _configuration.ShowInBattleTalk,
            v => _configuration.ShowInBattleTalk = v);

        ImGui.TextDisabled(text.BattleTalkHint);

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(text.BattleTalkWarning);
        ImGui.PopStyleColor();

        if (ImGui.Selectable($"{text.OpenFontGuide}###battleTalkFontGuide"))
            Util.OpenLink(FontSetupUrl);
    }

    private void DrawSubtitleComponentTab()
    {
        var text = Text;

        Checkbox(text.SubtitleEnable, _configuration.ShowInTalkSubtitle,
            v => _configuration.ShowInTalkSubtitle = v);

        ImGui.TextDisabled(text.SubtitleHint);
        ImGui.TextDisabled(text.SubtitleModeHint);
    }

    private void DrawSelectionComponentTab()
    {
        var text = Text;

        Checkbox(text.SelectionEnable, _configuration.ShowInSelectionDialogs,
            v => _configuration.ShowInSelectionDialogs = v);

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(text.SelectionWarning);
        ImGui.PopStyleColor();

        if (ImGui.Selectable($"{text.OpenFontGuide}###selectionFontGuide"))
            Util.OpenLink(FontSetupUrl);

        ImGui.TextDisabled(FontSetupUrl);

        ImGui.Spacing();
        ImGui.TextDisabled(text.SelectionHint);

        if (ImGui.CollapsingHeader($"{text.SelectionAdvanced}###selectionAdvanced"))
        {
            ImGui.BeginDisabled(!_configuration.ShowInSelectionDialogs);
            Checkbox(text.SelectionYesNo, _configuration.SelectionYesNo,
                v => _configuration.SelectionYesNo = v);
            Checkbox(text.SelectionString, _configuration.SelectionString,
                v => _configuration.SelectionString = v);
            Checkbox(text.SelectionIcon, _configuration.SelectionIconString,
                v => _configuration.SelectionIconString = v);
            Checkbox(text.SelectionOk, _configuration.SelectionOk,
                v => _configuration.SelectionOk = v);
            Checkbox(text.SelectionCutScene, _configuration.SelectionCutScene,
                v => _configuration.SelectionCutScene = v);
            ImGui.EndDisabled();
        }
    }

    private void DrawTargetNameComponentTab()
    {
        var text = Text;

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(text.NameplateWarning);
        ImGui.PopStyleColor();

        if (ImGui.Selectable($"{text.OpenFontGuide}###nameplateFontGuide"))
            Util.OpenLink(FontSetupUrl);

        ImGui.TextDisabled(FontSetupUrl);

        Checkbox(text.NameplateEnable, _configuration.ShowTargetName,
            v => _configuration.ShowTargetName = v);

        ImGui.TextDisabled(text.NameplateHint);

        Checkbox(text.NameplateShowEnglish, _configuration.TargetNameShowEnglish,
            v => _configuration.TargetNameShowEnglish = v);
        ImGui.TextDisabled(text.BilingualHint);
    }

    private void DrawItemNameComponentTab()
    {
        var text = Text;

        Checkbox(text.ItemEnable, _configuration.ShowItemName,
            v => _configuration.ShowItemName = v);

        Section(text.SectionMode);
        if (ImGui.RadioButton($"{text.ModeOverlay}###itemOverlay", !_configuration.ItemNameNativeMode))
        {
            _configuration.ItemNameNativeMode = false;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton($"{text.ItemNative}###itemNative", _configuration.ItemNameNativeMode))
        {
            _configuration.ItemNameNativeMode = true;
            _applySettings();
        }

        if (_configuration.ItemNameNativeMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
            ImGui.TextWrapped(text.ItemNativeWarning);
            ImGui.PopStyleColor();

            if (ImGui.Selectable($"{text.OpenFontGuide}###itemFontGuide"))
                Util.OpenLink(FontSetupUrl);

            ImGui.TextDisabled(FontSetupUrl);
            ImGui.TextDisabled(text.ItemNativeFallback);
        }
        else
        {
            ImGui.TextDisabled(text.ItemOverlayHint);
        }

        Section(text.SectionLanguage);
        if (ImGui.RadioButton($"{text.ItemChineseOnly}###itemLanguageChinese", !_configuration.ShowOriginal))
        {
            _configuration.ShowOriginal = false;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton($"{text.ItemBoth}###itemLanguageBoth", _configuration.ShowOriginal))
        {
            _configuration.ShowOriginal = true;
            _applySettings();
        }

        ImGui.TextDisabled(text.BilingualHint);

        if (_configuration.ItemNameNativeMode)
        {
            ImGui.TextDisabled(text.ItemNativeAppearance);
            return;
        }

        Section(text.SectionAppearance);
        Checkbox(text.UseDefaultAppearance, _configuration.ItemNameUseGlobalAppearance,
            v => _configuration.ItemNameUseGlobalAppearance = v);

        if (_configuration.ItemNameUseGlobalAppearance)
        {
            ImGui.TextDisabled(text.FollowingDefaults);
        }
        else
        {
            ColorEdit($"{text.ColorTranslatedName}###itemNameColor", _configuration.ItemNameColor,
                v => _configuration.ItemNameColor = v);
            ColorEdit($"{text.ColorEnglishName}###itemNameOriginalColor", _configuration.ItemNameOriginalColor,
                v => _configuration.ItemNameOriginalColor = v);
            SliderFloat($"{text.BackgroundOpacity}###itemNameOpacity", _configuration.ItemNameOpacity,
                0f, 1f, "%.2f", v => _configuration.ItemNameOpacity = v);
        }
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

#if SINOPHONE_TESTING
    /// <summary>
    /// Development-only tab. Everything here drives the live game, so it writes into real
    /// addons. The sub-tabs follow the Components tab: one per surface, plus the run controls
    /// and the results.
    /// </summary>
    private void DrawTestingTab()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "Test code. These tabs write into the game's own windows to check the display. " +
            "Reload the plugin to undo everything.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        Checkbox(
            "Log the path of each failure case file",
            _configuration.LogFailureFiles,
            value => _configuration.LogFailureFiles = value);

        if (_harnessHost is null)
        {
            ImGui.Spacing();
            ImGui.TextColored(WarningColor,
                "Runs are off: this build has the testing tab but is not loaded as a dev plugin.");
        }

        ImGui.Spacing();
        ImGui.Separator();

        if (!ImGui.BeginTabBar("SinophoneTestingTabs"))
            return;

        for (var index = 0; index < HarnessTabState.SubTabs.Length; index++)
        {
            if (!ImGui.BeginTabItem(HarnessTabState.SubTabs[index]))
                continue;

            switch (HarnessTabState.SubTabs[index])
            {
                case "Runs":
                    DrawHarnessRunsTab();
                    break;
                case "Talk":
                    DrawHarnessTalkTab();
                    break;
                case "Chat bubbles":
                    DrawHarnessBubbleTab();
                    break;
                case "Native text":
                    DrawNativeTestTab();
                    break;
                case "Talk probe":
                    DrawTalkProbeTab();
                    break;
                case "Results":
                    DrawHarnessResultsTab();
                    break;
            }

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    /// <summary>Shared run controls: what to build, how many, and whether to capture.</summary>
    private void DrawHarnessRunControls()
    {
        var limit = _harness.CaseLimit;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.SliderInt("Cases", ref limit, 1, 200))
            _harness.CaseLimit = limit;

        var fromStore = _harness.UseStoreCases;
        if (ImGui.Checkbox("Use lines from the loaded data", ref fromStore))
            _harness.UseStoreCases = fromStore;
        ImGui.TextDisabled("Off runs the fixed sample lines, which need no translation data.");

        var capture = _harness.CaptureFrames;
        if (ImGui.Checkbox("Save a screenshot per case", ref capture))
            _harness.CaptureFrames = capture;

        if (_harness.CaptureFrames && _harnessHost is { CanCapture: false })
        {
            ImGui.TextColored(WarningColor,
                "Screenshots are off: the plugin has no config directory or no PNG encoder.");
        }
    }

    /// <summary>Starts a run and records what the tab should say about it.</summary>
    private void StartRun(IReadOnlyList<HarnessCase> cases, Func<int, bool> start)
    {
        var running = _harnessHost?.IsRunning ?? false;
        var started = cases.Count > 0 && HarnessTabState.CanStart(running, cases.Count) && start(cases.Count);

        _harness.LastResult = HarnessTabState.StartResult(started, running, cases.Count);
        _harness.LastReportPath = null;
    }

    private void DrawHarnessRunsTab()
    {
        ImGui.TextWrapped(
            "Runs a list of lines through one surface and records what the game drew. " +
            "A run is frame-driven, so the game keeps drawing between cases.");

        Section("Setup");
        DrawHarnessRunControls();

        Section("Run");

        if (_harnessHost is null)
        {
            ImGui.TextColored(WarningColor, "Runs need a dev plugin load.");
            return;
        }

        var host = _harnessHost;
        var talkCases = host.BuildCases(HarnessSurface.Talk, _harness.CaseLimit, _harness.UseStoreCases);
        var bubbleCases = host.BuildCases(HarnessSurface.ChatBubble, _harness.CaseLimit, _harness.UseStoreCases);

        // An armed run measures the line on screen, so it really runs one case.
        var talkToRun = _harness.InjectTalkLines
            ? talkCases
            : HarnessCatalog.SingleLine(talkCases);

        ImGui.BeginDisabled(host.IsRunning);

        if (ImGui.Button("Run Talk cases"))
            StartRun(talkToRun, _ => host.StartTalkRun(talkCases, _harness.InjectTalkLines, _harness.CaptureFrames));

        ImGui.SameLine();
        ImGui.TextDisabled($"{talkToRun.Count} case(s), Talk {(host.IsTalkOpen ? "open" : "closed")}.");

        if (ImGui.Button("Run bubble cases"))
        {
            var target = _harness.SelectedTarget;
            StartRun(bubbleCases, _ => host.StartBubbleRun(bubbleCases, target, _harness.CaptureFrames));
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_harness.SelectedTarget.IsUsable
            ? $"{bubbleCases.Count} case(s) above {_harness.SelectedTarget.Name}."
            : "Pick a target on the Chat bubbles tab.");

        ImGui.EndDisabled();

        ImGui.Spacing();
        if (ImGui.Button("Cancel run"))
            _harnessHost.Cancel("Cancelled from the Testing tab.");

        ImGui.SameLine();
        if (ImGui.Button("Refresh targets"))
        {
            RefreshHarnessTargets();
            _harness.LastResult = $"{_harness.Targets.Count} object(s) in the zone.";
        }

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Last action");
        ImGui.TextWrapped(_harness.LastResult);
        DrawHarnessProgress();
    }

    private void DrawHarnessTalkTab()
    {
        ImGui.TextWrapped(
            "Drives the Talk window. Waiting mode measures the line you played, so the id-first " +
            "lookup is exercised. Inject mode writes the case's English into the addon instead, " +
            "which tests the display but not the ids.");

        Section("Mode");
        var inject = _harness.InjectTalkLines;
        if (ImGui.Checkbox("Write the English line into Talk", ref inject))
            _harness.InjectTalkLines = inject;

        ImGui.TextDisabled(_harness.InjectTalkLines
            ? "Each case is written into the addon. Talk must be open."
            : "Start while the line you want is on screen. One line is measured per run, using " +
              "the English the plugin logged for it.");

        Section("Cases");
        if (_harnessHost is { } talkHost)
        {
            var cases = talkHost.BuildCases(HarnessSurface.Talk, _harness.CaseLimit, _harness.UseStoreCases);
            DrawHarnessCaseList(cases);

            if (!_configuration.Enabled || !_configuration.ShowInTalk)
            {
                ImGui.TextColored(WarningColor,
                    "Talk translation is off in the components, so a run will draw the English line.");
            }

            if (!_configuration.NativeTextMode)
            {
                ImGui.TextColored(WarningColor,
                    "Native text mode is off, so this reads the game's own node, which stays English. " +
                    "The overlay is not measured by a run.");
            }

            ImGui.Spacing();
            if (ImGui.Button("Run these Talk cases"))
                StartRun(cases, _ => talkHost.StartTalkRun(cases, _harness.InjectTalkLines, _harness.CaptureFrames));
        }
        else
        {
            ImGui.TextDisabled("Runs need a dev plugin load.");
        }

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Window");
        ImGui.TextUnformatted(_harnessHost?.IsTalkOpen == true ? "Talk is open." : "Talk is not open.");
        DrawHarnessProgress();
    }

    private void DrawHarnessBubbleTab()
    {
        ImGui.TextWrapped(
            "Starts a yell bubble above a picked character and reads back what the game drew. " +
            "The bubble goes through the same handler as a real yell, so the lookup and sizing " +
            "are both tested.");

        Section("Target");
        if (ImGui.Button("Refresh objects"))
            RefreshHarnessTargets();

        ImGui.SameLine();
        ImGui.TextDisabled("The player is first, so a bubble can be tested without walking anywhere.");

        if (_harness.Targets.Count == 0)
        {
            ImGui.TextDisabled("No objects listed yet. Refresh once you are in a zone.");
        }
        else
        {
            var names = _harness.TargetLabels;
            var index = _harness.TargetIndex;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("##bubbleTarget", ref index, names))
                _harness.TargetIndex = index;
        }

        Section("Try one bubble");
        var previewCase = new HarnessCase(
            "bubble-preview",
            HarnessSurface.ChatBubble,
            "Adventurer! Over here! I have something for you.");

        if (ImGui.Button("Show a sample bubble"))
        {
            if (ChatBubbleHarness.Open(_harness.SelectedTarget, previewCase.English, out var reason))
                _harness.LastResult = "Bubble started.";
            else
                _harness.LastResult = reason;
        }

        ImGui.SameLine();
        if (ImGui.Button("Close bubble"))
        {
            ChatBubbleHarness.Close(_harness.SelectedTarget);
            _harness.LastResult = "Bubble closed.";
        }

        ImGui.SameLine();
        ImGui.TextDisabled("A bubble with no duration stays open until closed.");

        var read = ChatBubbleHarness.Read(_harness.SelectedTarget);
        ImGui.TextWrapped($"Bubble holds: {(read.Text.Length == 0 ? "(nothing)" : read.Text)} [{read.State}]");

        Section("Cases");
        if (_harnessHost is { } bubbleHost)
        {
            var cases = bubbleHost.BuildCases(HarnessSurface.ChatBubble, _harness.CaseLimit, _harness.UseStoreCases);
            DrawHarnessCaseList(cases);

            if (!_configuration.Enabled || !_configuration.ShowInChatBubbles)
            {
                ImGui.TextColored(WarningColor,
                    "Chat bubble translation is off in the components, so a run will draw the English line.");
            }

            ImGui.Spacing();
            if (ImGui.Button("Run these bubble cases"))
            {
                var target = _harness.SelectedTarget;
                StartRun(cases, _ => bubbleHost.StartBubbleRun(cases, target, _harness.CaptureFrames));
            }
        }
        else
        {
            ImGui.TextDisabled("Runs need a dev plugin load.");
        }

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Default balloons");
        ImGui.TextWrapped(
            "Not covered yet. A default balloon is drawn from the character's balloon state " +
            "and has no start function to call, so the harness covers yell bubbles only.");

        DrawHarnessProgress();
    }

    private void DrawHarnessResultsTab()
    {
        if (_harnessHost is not { } host || host.LastReport is not { } report)
        {
            ImGui.TextDisabled("No run has finished yet.");
            return;
        }

        ImGui.TextColored(SectionColor, report.Summary());
        var path = _harness.LastReportPath ?? host.LastReportPath;
        if (!string.IsNullOrEmpty(path))
            ImGui.TextDisabled($"Saved to {path}");

        var captures = host.Captured.Count;
        if (captures > 0)
            ImGui.TextDisabled($"{captures} screenshot(s) in {host.ReportDirectory}");

        ImGui.Spacing();
        if (!ImGui.BeginTable("##harnessResults", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Case", ImGuiTableColumnFlags.WidthFixed, 160f);
        ImGui.TableSetupColumn("Surface", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("Outcome", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Detail", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var result in report.Results)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(result.Id);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(result.Surface.ToString());

            ImGui.TableNextColumn();
            ImGui.TextColored(OutcomeColor(result.Outcome), result.Outcome.ToString());

            ImGui.TableNextColumn();
            ImGui.TextWrapped(result.Detail ?? result.Drawn ?? string.Empty);
        }

        ImGui.EndTable();
    }

    private static Vector4 OutcomeColor(HarnessOutcome outcome)
        => outcome switch
        {
            HarnessOutcome.Passed => new Vector4(0.55f, 0.9f, 0.55f, 1f),
            HarnessOutcome.Failed => new Vector4(1f, 0.45f, 0.45f, 1f),
            HarnessOutcome.Skipped => new Vector4(0.75f, 0.75f, 0.75f, 1f),
            _ => new Vector4(1f, 0.72f, 0.35f, 1f),
        };

    private void DrawHarnessCaseList(IReadOnlyList<HarnessCase> cases)
    {
        if (cases.Count == 0)
        {
            ImGui.TextDisabled("No cases. Load translation data, or turn off store cases.");
            return;
        }

        foreach (var harnessCase in cases.Take(12))
        {
            var tag = harnessCase.Tags is { Count: > 0 } ? $" [{string.Join(",", harnessCase.Tags)}]" : string.Empty;
            ImGui.TextWrapped($"{harnessCase.Id}{tag}: {Shorten(harnessCase.English)}");
        }

        if (cases.Count > 12)
            ImGui.TextDisabled($"...and {cases.Count - 12} more.");
    }

    private void DrawHarnessProgress()
    {
        if (_harnessHost?.Run is not { } run)
            return;

        ImGui.Spacing();
        ImGui.ProgressBar(
            run.Total == 0 ? 0f : run.Completed / (float)run.Total,
            new Vector2(-1f, 0f),
            $"{run.Completed}/{run.Total}");
    }

    private void RefreshHarnessTargets()
    {
        if (_objectTable is null)
        {
            _harness.SetTargets([]);
            return;
        }

        _harness.SetTargets(ChatBubbleHarness.VisibleTargets(_objectTable));
    }
#endif

#if SINOPHONE_TESTING
    /// <summary>
    /// Development-only tab for the native text probe. Writes Chinese into the game's
    /// own text nodes, so we can see whether the game font has CJK glyphs.
    /// </summary>
    private void DrawNativeTestTab()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "Test code. This writes into the game's dialogue box to check whether its font " +
            "can draw Chinese. Reload the plugin to undo everything.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();

        // Target first: everything below acts on the addon and node picked here.
        ImGui.TextColored(SectionColor, "Target");
        if (ImGui.BeginCombo("##nativeAddon", _nativeProbe.AddonName))
        {
            foreach (var name in NativeTextProbe.KnownAddons)
            {
                if (ImGui.Selectable(name, name == _nativeProbe.AddonName))
                    _nativeProbe.SetAddon(name);
            }

            ImGui.EndCombo();
        }

        var customAddon = _nativeProbe.AddonName;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputText("Custom addon", ref customAddon, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            _nativeProbe.SetAddon(customAddon.Trim());

        ImGui.SameLine();
        if (ImGui.Button("Scan"))
            _nativeProbe.Scan();

        foreach (var node in _nativeProbe.ScannedNodes)
        {
            var label = $"Node {node.Id}: {Shorten(node.Text)}";
            if (ImGui.Selectable(label, node.Id == _nativeProbe.NodeId))
                _nativeProbe.NodeId = node.Id;
        }

        var nodeId = (int)_nativeProbe.NodeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Node id", ref nodeId) && nodeId >= 0)
            _nativeProbe.NodeId = (uint)nodeId;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(SectionColor, "Sample");
        foreach (var preset in NativeTextProbe.Presets)
        {
            if (ImGui.Button(preset.Label))
                _nativeProbe.Sample = preset.Text;

            if (preset.Label != NativeTextProbe.Presets[^1].Label)
                ImGui.SameLine();
        }

        var sample = _nativeProbe.Sample;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextMultiline("##nativeSample", ref sample, 2048, new Vector2(-1f, 70f)))
            _nativeProbe.Sample = sample;

        ImGui.Spacing();
        ImGui.TextUnformatted("Font");
        var fontType = _nativeProbe.FontType;
        if (ImGui.BeginCombo("##nativeFont", fontType.ToString()))
        {
            foreach (var value in Enum.GetValues<FontType>())
            {
                if (ImGui.Selectable(value.ToString(), value == fontType))
                    _nativeProbe.FontType = value;
            }

            ImGui.EndCombo();
        }

        if (ImGui.Button("Try next font"))
            _nativeProbe.WriteWithNextFont();

        ImGui.SameLine();
        ImGui.TextDisabled("Cycles the six game fonts and writes each time.");

        var sticky = _nativeProbe.Sticky;
        if (ImGui.Checkbox("Keep the sample on every refresh", ref sticky))
            _nativeProbe.Sticky = sticky;
        ImGui.TextDisabled("Turn on if the game puts its own text back after one write.");

        ImGui.Spacing();
        if (ImGui.Button("Write sample"))
            _nativeProbe.Write();

        ImGui.SameLine();
        if (ImGui.Button("Restore original"))
            _nativeProbe.Restore();

        ImGui.SameLine();
        if (ImGui.Button("Forget saved text"))
            _nativeProbe.ResetSaved();

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Last result");
        ImGui.TextWrapped(_nativeProbe.LastResult);
    }
#endif

#if SINOPHONE_TESTING
    /// <summary>
    /// Development-only tab for the issue #56 event probe. Logs the ids and Lua text
    /// keys the game exposes at the moment Talk opens.
    /// </summary>
    private void DrawTalkProbeTab()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "Test code. Logs the current event ids, scene data and Lua text keys " +
            "while Talk is open. Use it to check which id a line actually exposes.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();

        var enabled = _talkContextProbe.Enabled;
        if (ImGui.Checkbox("Log every Talk refresh", ref enabled))
            _talkContextProbe.Enabled = enabled;

        ImGui.TextDisabled("Turn on, talk to an NPC, then read the plugin log.");

        ImGui.Spacing();
        if (ImGui.Button("Log current event"))
            _talkContextProbe.LogCurrent();

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Last result");
        ImGui.TextWrapped(_talkContextProbe.LastResult);
    }
#endif

    /// <summary>Keeps a scanned node label short enough to read in the list.</summary>
    private static string Shorten(string text)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= 48 ? flat : string.Concat(flat.AsSpan(0, 48), "...");
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
