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
    private readonly Action _applySettings;
    private readonly Action _showPreview;
    private readonly Func<string> _statusText;
    private readonly TranslationDataUpdater _dataUpdater;
    private readonly Func<string> _dataSourceText;
    private readonly Func<string> _aboutText;
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
        Action applySettings,
        Action showPreview,
        Func<string> statusText,
        TranslationDataUpdater dataUpdater,
        Func<string> dataSourceText,
        Func<string> aboutText,
        NativeTextProbe nativeProbe,
        TalkContextProbe talkContextProbe,
        HarnessHost? harnessHost,
        IObjectTable objectTable)
        : this(configuration, applySettings, showPreview, statusText, dataUpdater, dataSourceText, aboutText)
    {
        _nativeProbe = nativeProbe;
        _talkContextProbe = talkContextProbe;
        _harnessHost = harnessHost;
        _objectTable = objectTable;
    }
#endif

    public ConfigWindow(
        Configuration configuration,
        Action applySettings,
        Action showPreview,
        Func<string> statusText,
        TranslationDataUpdater dataUpdater,
        Func<string> dataSourceText,
        Func<string> aboutText)
        : base("Sinophone###SinophoneConfig", ImGuiWindowFlags.NoCollapse)
    {
        _configuration = configuration;
        _applySettings = applySettings;
        _showPreview = showPreview;
        _statusText = statusText;
        _dataUpdater = dataUpdater;
        _dataSourceText = dataSourceText;
        _aboutText = aboutText;

        Size = new Vector2(540f, 680f);
        SizeCondition = ImGuiCond.FirstUseEver;
        AllowPinning = false;
        ShowCloseButton = true;
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("SinophoneTabs"))
            return;

        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneralTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Components"))
        {
            DrawComponentsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Data"))
        {
            DrawDataTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Logs"))
        {
            DrawLogsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Status"))
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
        Checkbox("Enable translations", _configuration.Enabled, v => _configuration.Enabled = v);

        Section("Chinese text source");
        ImGui.TextWrapped(
            "Choose which official game version supplies the translation. " +
            "TC is the Traditional Chinese release for Taiwan and Hong Kong. " +
            "CN is the Simplified Chinese release for mainland China.");

        if (ImGui.RadioButton("Traditional Chinese (TC)",
                _configuration.Variant == ChineseVariant.Traditional))
        {
            _configuration.Variant = ChineseVariant.Traditional;
            _applySettings();
        }

        ImGui.Indent();
        ImGui.TextDisabled("Taiwan / Hong Kong game version");
        ImGui.Unindent();

        if (ImGui.RadioButton("Simplified Chinese (CN)",
                _configuration.Variant == ChineseVariant.Simplified))
        {
            _configuration.Variant = ChineseVariant.Simplified;
            _applySettings();
        }

        ImGui.Indent();
        ImGui.TextDisabled("Mainland China game version");
        ImGui.Unindent();

        Checkbox("Use the other version when a line is missing", _configuration.UseFallbackVariant,
            v => _configuration.UseFallbackVariant = v);
        ImGui.Indent();
        ImGui.TextDisabled(
            "May mix Traditional and Simplified characters for missing rows. " +
            "Off by default: it loads both data files and doubles the load time " +
            "for a few extra lines.");
        ImGui.Unindent();

        Section("Convert displayed text");
        ImGui.TextWrapped(
            "Optional. Changes the characters after lookup without changing which " +
            "game version supplies the translation.");

        if (ImGui.RadioButton("No conversion (keep official text)",
                _configuration.Conversion == ChineseConversion.None))
        {
            _configuration.Conversion = ChineseConversion.None;
            _applySettings();
        }

        if (ImGui.RadioButton("Simplified to Traditional (CN to TC)",
                _configuration.Conversion == ChineseConversion.SimplifiedToTraditional))
        {
            _configuration.Conversion = ChineseConversion.SimplifiedToTraditional;
            _applySettings();
        }

        if (ImGui.RadioButton("Traditional to Simplified (TC to CN)",
                _configuration.Conversion == ChineseConversion.TraditionalToSimplified))
        {
            _configuration.Conversion = ChineseConversion.TraditionalToSimplified;
            _applySettings();
        }

        Section("Font and overlay");
        SliderFloat("Font size", _configuration.FontSize, 12f, 42f, "%.0f px",
            v => _configuration.FontSize = v);
        SliderFloat("Background opacity (default)", _configuration.OverlayOpacity, 0f, 1f, "%.2f",
            v => _configuration.OverlayOpacity = v);

        ImGui.TextUnformatted("Custom font path (optional, .ttf/.ttc/.otf)");
        var fontPath = _configuration.CustomFontPath ?? string.Empty;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##customFontPath", ref fontPath, 512))
        {
            _configuration.CustomFontPath = fontPath;
            _applySettings();
        }

        Section("Colors (defaults)");
        ColorEdit("NPC name", _configuration.SpeakerNameColor, v => _configuration.SpeakerNameColor = v);
        ColorEdit("Translated text", _configuration.TranslatedTextColor, v => _configuration.TranslatedTextColor = v);
        ColorEdit("Original text", _configuration.OriginalTextColor, v => _configuration.OriginalTextColor = v);

        ImGui.TextDisabled("The item name language is on its own tab. " +
                           "The nameplate switch is on its own tab too.");

        Section("Shared actions");
        if (ImGui.Button("Use the default colors for every component"))
            UseGlobalAppearanceForAll();

        ImGui.SameLine();
        if (ImGui.Button("Reset all settings"))
            ResetAll();
    }

    private void DrawComponentsTab()
    {
        if (!ImGui.BeginTabBar("SinophoneComponentTabs"))
            return;

        if (ImGui.BeginTabItem("Talk"))
        {
            DrawTalkComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Subtitles"))
        {
            DrawSubtitleComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Chat bubbles"))
        {
            DrawChatBubbleComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Battle talk"))
        {
            DrawBattleTalkComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Selection"))
        {
            DrawSelectionComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Nameplates"))
        {
            DrawTargetNameComponentTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Item name"))
        {
            DrawItemNameComponentTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawTalkComponentTab()
    {
        Checkbox("Enable translation in the Talk dialogue", _configuration.ShowInTalk,
            v => _configuration.ShowInTalk = v);

        Section("Mode");
        if (ImGui.RadioButton("Overlay###talkOverlay", !_configuration.NativeTextMode))
        {
            _configuration.NativeTextMode = false;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Native (game dialogue box)###talkNative", _configuration.NativeTextMode))
        {
            _configuration.NativeTextMode = true;
            _applySettings();
        }

        if (_configuration.NativeTextMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
            ImGui.TextWrapped(
                "Native mode writes Chinese into the game's own dialogue box. " +
                "The game's font has no Chinese glyphs, so install a font mod first.");
            ImGui.PopStyleColor();

            if (ImGui.Selectable("Open the font setup guide"))
                Util.OpenLink(FontSetupUrl);

            ImGui.TextDisabled(FontSetupUrl);
        }

        Section("Display");
        Checkbox("Show speaker name", _configuration.ShowSpeakerName,
            v => _configuration.ShowSpeakerName = v);
        ImGui.TextDisabled("The Chinese name is shown with the English name in parentheses.");

        Checkbox("Hide when no translation is found", _configuration.HideWhenNoTranslation,
            v => _configuration.HideWhenNoTranslation = v);
        ImGui.TextDisabled(
            "When off, an untranslated line falls back to the English text. " +
            "The overlay never shows both languages at once.");

        if (_configuration.NativeTextMode)
        {
            Section("What other readers see");
            ImGui.TextDisabled(
                "The dialogue box always shows Chinese. This decides what the game's own log and " +
                "other plugins read for the same line.");

            if (ImGui.RadioButton("English original###shareEnglish", !_configuration.NativeTextSharesTranslation))
            {
                _configuration.NativeTextSharesTranslation = false;
                _applySettings();
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("Chinese translation###shareChinese", _configuration.NativeTextSharesTranslation))
            {
                _configuration.NativeTextSharesTranslation = true;
                _applySettings();
            }
        }

        Section("Overlay position");
        ImGui.BeginDisabled(_configuration.NativeTextMode);
        Checkbox("Lock overlay position", _configuration.LockOverlay,
            v => _configuration.LockOverlay = v);
        Checkbox("Click-through", _configuration.Clickthrough,
            v => _configuration.Clickthrough = v);
        SliderFloat("Width###talkWidth", _configuration.OverlayWidth, 280f, 1200f, "%.0f",
            v => _configuration.OverlayWidth = v);
        SliderFloat("Horizontal offset (0 = center)###overlayOffsetX", _configuration.OverlayOffsetX,
            -1000f, 1000f, "%.0f px", v => _configuration.OverlayOffsetX = v);
        SliderFloat("Distance from screen bottom###offsetFromBottom", _configuration.OffsetFromBottom,
            20f, 520f, "%.0f", v => _configuration.OffsetFromBottom = v);
        Checkbox("Keep the bottom line fixed when text grows", _configuration.AnchorOverlayBottom,
            v => _configuration.AnchorOverlayBottom = v);
        ImGui.EndDisabled();

        ImGui.Spacing();
        if (ImGui.Button("Preview overlay"))
            _showPreview();

        ImGui.SameLine();
        if (ImGui.Button("Reset Talk layout"))
            ResetTalkLayout();

        Section("Appearance");
        Checkbox("Use the default colors and opacity", _configuration.TalkUseGlobalAppearance,
            v => _configuration.TalkUseGlobalAppearance = v);

        if (_configuration.TalkUseGlobalAppearance)
        {
            ImGui.TextDisabled("Following the defaults on the General tab.");
        }
        else
        {
            ColorEdit("Speaker name###talkSpeakerColor", _configuration.TalkSpeakerNameColor,
                v => _configuration.TalkSpeakerNameColor = v);
            ColorEdit("Translated text###talkTranslatedColor", _configuration.TalkTranslatedTextColor,
                v => _configuration.TalkTranslatedTextColor = v);
            ColorEdit("Untranslated fallback###talkOriginalColor", _configuration.TalkOriginalTextColor,
                v => _configuration.TalkOriginalTextColor = v);
            SliderFloat("Background opacity###talkOpacity", _configuration.TalkOverlayOpacity,
                0f, 1f, "%.2f", v => _configuration.TalkOverlayOpacity = v);
        }
    }

    private void DrawChatBubbleComponentTab()
    {
        Checkbox("Translate NPC chat bubbles", _configuration.ShowInChatBubbles,
            v => _configuration.ShowInChatBubbles = v);

        ImGui.TextDisabled("The Chinese is written into the game's own bubble, above the NPC's head.");

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "Chat bubbles use the game's own font and need the font mod. " +
            "The bubble layout is still being checked in game, so this is off by default.");
        ImGui.PopStyleColor();

        if (ImGui.Selectable("Open the font setup guide###bubbleFontGuide"))
            Util.OpenLink(FontSetupUrl);
    }

    private void DrawBattleTalkComponentTab()
    {
        Checkbox("Translate battle talk", _configuration.ShowInBattleTalk,
            v => _configuration.ShowInBattleTalk = v);

        ImGui.TextDisabled(
            "Boss shouts, dungeon narration, and duty NPC lines, in the game's own box at the top " +
            "of the screen. The name follows the speaker name setting on the Talk tab.");

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "Battle talk uses the game's own font and needs the font mod. A line with no " +
            "translation stays English, and so does the line already on screen when the switch is " +
            "turned off. This is off by default.");
        ImGui.PopStyleColor();

        if (ImGui.Selectable("Open the font setup guide###battleTalkFontGuide"))
            Util.OpenLink(FontSetupUrl);
    }

    private void DrawSubtitleComponentTab()
    {
        Checkbox("Enable translation in Talk subtitles", _configuration.ShowInTalkSubtitle,
            v => _configuration.ShowInTalkSubtitle = v);

        ImGui.TextDisabled("Subtitles use the Talk overlay's position and appearance.");
        ImGui.TextDisabled("Native text is not supported for subtitles yet.");
    }

    private void DrawSelectionComponentTab()
    {
        Checkbox("Enable translation in selection dialogs", _configuration.ShowInSelectionDialogs,
            v => _configuration.ShowInSelectionDialogs = v);

        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "Selection dialogs are translated in place, with no overlay. " +
            "This needs a CJK font mod, or the Chinese text shows the missing-glyph character.");
        ImGui.PopStyleColor();

        if (ImGui.Selectable("Open the font setup guide"))
            Util.OpenLink(FontSetupUrl);

        ImGui.TextDisabled(FontSetupUrl);

        ImGui.Spacing();
        ImGui.TextDisabled("Prompts and option labels are replaced in the game's own dialog.");

        if (ImGui.CollapsingHeader("Advanced: choose dialog types"))
        {
            ImGui.BeginDisabled(!_configuration.ShowInSelectionDialogs);
            Checkbox("Yes / No prompt", _configuration.SelectionYesNo,
                v => _configuration.SelectionYesNo = v);
            Checkbox("List of text options", _configuration.SelectionString,
                v => _configuration.SelectionString = v);
            Checkbox("List of icon options", _configuration.SelectionIconString,
                v => _configuration.SelectionIconString = v);
            Checkbox("Single OK prompt", _configuration.SelectionOk,
                v => _configuration.SelectionOk = v);
            Checkbox("Cutscene choice", _configuration.SelectionCutScene,
                v => _configuration.SelectionCutScene = v);
            ImGui.EndDisabled();
        }
    }

    private void DrawTargetNameComponentTab()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped(
            "The Chinese name is written into the game's own nameplate above every visible NPC " +
            "and object, such as a summoning bell, company chest, or door. " +
            "It needs a font mod, or the name shows the missing-glyph character. " +
            "Nameplates must be visible in the game settings.");
        ImGui.PopStyleColor();

        if (ImGui.Selectable("Open the font setup guide"))
            Util.OpenLink(FontSetupUrl);

        ImGui.TextDisabled(FontSetupUrl);

        Checkbox("Show Chinese names on nameplates", _configuration.ShowTargetName,
            v => _configuration.ShowTargetName = v);

        ImGui.TextDisabled("Every visible nameplate is translated, not only the target. " +
            "Player names are not translated.");

        Checkbox("Show the English name too", _configuration.TargetNameShowEnglish,
            v => _configuration.TargetNameShowEnglish = v);
        ImGui.TextDisabled("The English name follows the Chinese name in brackets, on the same line.");
    }

    private void DrawItemNameComponentTab()
    {
        Checkbox("Show the Chinese name of the hovered item", _configuration.ShowItemName,
            v => _configuration.ShowItemName = v);

        Section("Mode");
        if (ImGui.RadioButton("Overlay###itemOverlay", !_configuration.ItemNameNativeMode))
        {
            _configuration.ItemNameNativeMode = false;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Native item tooltip###itemNative", _configuration.ItemNameNativeMode))
        {
            _configuration.ItemNameNativeMode = true;
            _applySettings();
        }

        if (_configuration.ItemNameNativeMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
            ImGui.TextWrapped(
                "Native mode writes the name into the game's own item tooltip. " +
                "The game's font has no Chinese glyphs, so install a font mod first.");
            ImGui.PopStyleColor();

            if (ImGui.Selectable("Open the font setup guide###itemFontGuide"))
                Util.OpenLink(FontSetupUrl);

            ImGui.TextDisabled(FontSetupUrl);
            ImGui.TextDisabled("A tooltip the plugin cannot write falls back to the overlay.");
        }
        else
        {
            ImGui.TextDisabled("The name follows the mouse, above the item tooltip.");
        }

        Section("Language");
        if (ImGui.RadioButton("Chinese only###itemLanguageChinese", !_configuration.ShowOriginal))
        {
            _configuration.ShowOriginal = false;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Chinese and English###itemLanguageBoth", _configuration.ShowOriginal))
        {
            _configuration.ShowOriginal = true;
            _applySettings();
        }

        ImGui.TextDisabled("The English name follows the Chinese name in brackets, on the same line.");

        if (_configuration.ItemNameNativeMode)
        {
            ImGui.TextDisabled("Native mode draws the name with the game's own colors and size.");
            return;
        }

        Section("Appearance");
        Checkbox("Use the default colors and opacity", _configuration.ItemNameUseGlobalAppearance,
            v => _configuration.ItemNameUseGlobalAppearance = v);

        if (_configuration.ItemNameUseGlobalAppearance)
        {
            ImGui.TextDisabled("Following the defaults on the General tab.");
        }
        else
        {
            ColorEdit("Translated name###itemNameColor", _configuration.ItemNameColor,
                v => _configuration.ItemNameColor = v);
            ColorEdit("English name###itemNameOriginalColor", _configuration.ItemNameOriginalColor,
                v => _configuration.ItemNameOriginalColor = v);
            SliderFloat("Background opacity###itemNameOpacity", _configuration.ItemNameOpacity,
                0f, 1f, "%.2f", v => _configuration.ItemNameOpacity = v);
        }
    }

    private void DrawDataTab()
    {
        ImGui.TextWrapped(_dataSourceText());

        Checkbox("Download data updates on load", _configuration.CheckForDataUpdates,
            v => _configuration.CheckForDataUpdates = v);
        ImGui.TextDisabled("Runs in the background. The old data stays in use until the new data is ready.");

        Checkbox("Download both language variants", _configuration.DownloadBothVariants,
            v => _configuration.DownloadBothVariants = v);
        ImGui.TextDisabled("Off downloads only the selected variant. Fallback uses the other variant only if it is already on disk.");

        var state = _dataUpdater.State;

        if (state.IsBusy)
        {
            if (ImGui.Button("Cancel"))
                _dataUpdater.Cancel();
        }
        else
        {
            if (ImGui.Button(state.HasUpdate ? "Download update" : "Download / update data"))
                _dataUpdater.StartUpdate();

            ImGui.SameLine();
            if (ImGui.Button("Check for updates"))
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
        ImGui.TextWrapped(
            "Choose which of the plugin's own messages appear in Dalamud's plugin log. " +
            "Each switch hides one group's detail messages. Warnings and errors always show.");

        ImGui.Spacing();
        if (ImGui.Button("Show every group"))
            SetAllLogGroups(true);

        ImGui.SameLine();
        if (ImGui.Button("Hide every detail message"))
            SetAllLogGroups(false);

        Section("Groups");
        LogGroup("Dialogue lookups", _configuration.LogDialogue,
            v => _configuration.LogDialogue = v,
            "Every Talk and subtitle line, with the lookup that answered it.");

        LogGroup("Native text", _configuration.LogNativeText,
            v => _configuration.LogNativeText = v,
            "Each write into the game's own dialogue box.");

        LogGroup("Selection dialogs", _configuration.LogSelectionDialogs,
            v => _configuration.LogSelectionDialogs = v,
            "Each prompt and option the plugin translates.");

        LogGroup("Nameplates", _configuration.LogNamePlates,
            v => _configuration.LogNamePlates = v,
            "One line for every name it puts on a nameplate.");

        LogGroup("Item names", _configuration.LogItemNames,
            v => _configuration.LogItemNames = v,
            "Item name lookups that find no translation.");

        LogGroup("Translation data", _configuration.LogData,
            v => _configuration.LogData = v,
            "Loading, indexing, and download checks.");

        LogGroup("Plugin", _configuration.LogPlugin,
            v => _configuration.LogPlugin = v,
            "Load, unload, and settings changes.");

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
        ImGui.TextWrapped(
            "Early development. Some NPC dialogue is not detected, " +
            "and text that exists in no game dump stays untranslated.");
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
        _configuration.CheckForDataUpdates = defaults.CheckForDataUpdates;
        _applySettings();
    }
}
