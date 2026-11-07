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
        "https://github.com/raydenhui/Sinophone/blob/main/README.md#font-setup";

    private readonly Configuration _configuration;
    private readonly Action _applySettings;
    private readonly Action _showPreview;
    private readonly Func<string> _statusText;
    private readonly TranslationDataUpdater _dataUpdater;
    private readonly Func<string> _dataSourceText;
    private readonly Func<string> _aboutText;
#if DEBUG
    // Set by the debug-only constructor, which chains into the main one.
    private readonly NativeTextProbe _nativeProbe = null!;
    private readonly TalkContextProbe _talkContextProbe = null!;
#endif

#if DEBUG
    /// <summary>
    /// Debug build. Takes the native test probe, which adds a "Native test" tab.
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
        TalkContextProbe talkContextProbe)
        : this(configuration, applySettings, showPreview, statusText, dataUpdater, dataSourceText, aboutText)
    {
        _nativeProbe = nativeProbe;
        _talkContextProbe = talkContextProbe;
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

#if DEBUG
        if (ImGui.BeginTabItem("Native test"))
        {
            DrawNativeTestTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Talk probe"))
        {
            DrawTalkProbeTab();
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

        Checkbox("Also show the English name in the item name overlay",
            _configuration.ShowOriginal, v => _configuration.ShowOriginal = v);
        ImGui.TextDisabled("The nameplate switch is on its own tab. " +
                           "The Talk overlay always shows both names.");

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

        ImGui.TextDisabled("The name follows the mouse, above the item tooltip.");

        Section("Mode");
        ImGui.RadioButton("Overlay###itemOverlay", true);
        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.RadioButton("Native item tooltip (planned)###itemNative", false);
        ImGui.EndDisabled();
        ImGui.TextDisabled("Native item names are planned. See the tracking issue.");

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

        Checkbox("Check for data updates on load", _configuration.CheckForDataUpdates,
            v => _configuration.CheckForDataUpdates = v);

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

#if DEBUG
        LogGroup("Debug probes", _configuration.LogDiagnostics,
            v => _configuration.LogDiagnostics = v,
            "The Talk and native text probes in debug builds.");
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
            "Not usable yet. Some NPC dialogue is not detected. " +
            "The first usable release is planned for 0.2.0.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();

        var status = _statusText();
        if (!string.IsNullOrEmpty(status))
            ImGui.TextWrapped(status);
    }

#if DEBUG
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

#if DEBUG
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
        _configuration.NativeTextMode = defaults.NativeTextMode;
        _configuration.ShowSpeakerName = defaults.ShowSpeakerName;
        _configuration.SpeakerNameColor = defaults.SpeakerNameColor;
        _configuration.TranslatedTextColor = defaults.TranslatedTextColor;
        _configuration.OriginalTextColor = defaults.OriginalTextColor;
        _configuration.ShowTargetName = defaults.ShowTargetName;
        _configuration.TargetNameShowEnglish = defaults.TargetNameShowEnglish;
        _configuration.ShowItemName = defaults.ShowItemName;
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
