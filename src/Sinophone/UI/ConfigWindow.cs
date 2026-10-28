using Action = System.Action;

namespace Sinophone.UI;

/// <summary>
/// Settings window for Sinophone. Every change is applied and persisted immediately.
/// </summary>
public sealed class ConfigWindow : Window
{
    private static readonly Vector4 SectionColor = new(1f, 0.82f, 0.38f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.72f, 0.35f, 1f);

    private readonly Configuration _configuration;
    private readonly Action _applySettings;
    private readonly Action _showPreview;
    private readonly Func<string> _statusText;
    private readonly TranslationDataUpdater _dataUpdater;
    private readonly Func<string> _dataSourceText;
    private readonly Func<string> _aboutText;

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

        Size = new Vector2(500f, 640f);
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

        if (ImGui.BeginTabItem("Appearance"))
        {
            DrawAppearanceTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Position"))
        {
            DrawPositionTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Data"))
        {
            DrawDataTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Status"))
        {
            DrawStatusTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawGeneralTab()
    {
        Checkbox("Enable translations", _configuration.Enabled, v => _configuration.Enabled = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Chinese variant");
        if (ImGui.RadioButton("Traditional (TC)", _configuration.Variant == ChineseVariant.Traditional))
        {
            _configuration.Variant = ChineseVariant.Traditional;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Simplified (CN)", _configuration.Variant == ChineseVariant.Simplified))
        {
            _configuration.Variant = ChineseVariant.Simplified;
            _applySettings();
        }

        Checkbox("Fill missing rows from the other variant", _configuration.UseFallbackVariant,
            v => _configuration.UseFallbackVariant = v);

        ImGui.Spacing();
        ImGui.TextUnformatted("Convert Chinese text");
        ImGui.TextDisabled("Only changes what is shown; the data that is loaded stays the same.");

        if (ImGui.RadioButton("Keep the official script", _configuration.Conversion == ChineseConversion.None))
        {
            _configuration.Conversion = ChineseConversion.None;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Simplified to Traditional",
                _configuration.Conversion == ChineseConversion.SimplifiedToTraditional))
        {
            _configuration.Conversion = ChineseConversion.SimplifiedToTraditional;
            _applySettings();
        }

        if (ImGui.RadioButton("Traditional to Simplified",
                _configuration.Conversion == ChineseConversion.TraditionalToSimplified))
        {
            _configuration.Conversion = ChineseConversion.TraditionalToSimplified;
            _applySettings();
        }

        ImGui.Spacing();
        Checkbox("Show in Talk dialog", _configuration.ShowInTalk, v => _configuration.ShowInTalk = v);
        Checkbox("Show in Talk subtitles", _configuration.ShowInTalkSubtitle,
            v => _configuration.ShowInTalkSubtitle = v);

        ImGui.Spacing();
        if (ImGui.Button("Reset all settings"))
            ResetAll();
    }

    private void DrawAppearanceTab()
    {
        Checkbox("Show speaker name", _configuration.ShowSpeakerName, v => _configuration.ShowSpeakerName = v);
        Checkbox("Also show original English text", _configuration.ShowOriginal,
            v => _configuration.ShowOriginal = v);
        Checkbox("Hide when no translation is found", _configuration.HideWhenNoTranslation,
            v => _configuration.HideWhenNoTranslation = v);

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Colors");
        ImGui.Separator();

        ColorEdit("NPC name", _configuration.SpeakerNameColor, v => _configuration.SpeakerNameColor = v);
        ColorEdit("Translated text", _configuration.TranslatedTextColor, v => _configuration.TranslatedTextColor = v);
        ColorEdit("Original text", _configuration.OriginalTextColor, v => _configuration.OriginalTextColor = v);

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Font");
        ImGui.Separator();

        SliderFloat("Font size", _configuration.FontSize, 12f, 42f, "%.0f px",
            v => _configuration.FontSize = v);
        SliderFloat("Background opacity", _configuration.OverlayOpacity, 0f, 1f, "%.2f",
            v => _configuration.OverlayOpacity = v);

        ImGui.TextUnformatted("Custom font path (optional, .ttf/.ttc/.otf)");
        var fontPath = _configuration.CustomFontPath ?? string.Empty;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##customFontPath", ref fontPath, 512))
        {
            _configuration.CustomFontPath = fontPath;
            _applySettings();
        }

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Name overlays");
        ImGui.Separator();

        Checkbox("Show the Chinese name of the target", _configuration.ShowTargetName,
            v => _configuration.ShowTargetName = v);
        Checkbox("Show the Chinese name of the hovered item", _configuration.ShowItemName,
            v => _configuration.ShowItemName = v);
    }

    private void DrawPositionTab()
    {
        Checkbox("Lock overlay position", _configuration.LockOverlay, v => _configuration.LockOverlay = v);
        Checkbox("Click-through", _configuration.Clickthrough, v => _configuration.Clickthrough = v);

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Dialogue overlay");
        ImGui.Separator();

        SliderFloat("Horizontal offset (0 = center)###overlayOffsetX", _configuration.OverlayOffsetX, -1000f, 1000f, "%.0f px",
            v => _configuration.OverlayOffsetX = v);
        SliderFloat("Distance from screen bottom", _configuration.OffsetFromBottom, 20f, 520f, "%.0f",
            v => _configuration.OffsetFromBottom = v);
        Checkbox("Keep the bottom line fixed when text grows", _configuration.AnchorOverlayBottom,
            v => _configuration.AnchorOverlayBottom = v);
        ImGui.TextDisabled("Only matters while the overlay is unlocked and can be dragged.");

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Target name overlay");
        ImGui.Separator();

        SliderFloat("Horizontal offset (0 = center)###targetNameOffsetX", _configuration.TargetNameOffsetX, -1000f, 1000f, "%.0f px",
            v => _configuration.TargetNameOffsetX = v);
        SliderFloat("Distance from screen top", _configuration.TargetNameOffsetY, 20f, 520f, "%.0f",
            v => _configuration.TargetNameOffsetY = v);

        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Size");
        ImGui.Separator();

        SliderFloat("Width", _configuration.OverlayWidth, 280f, 1200f, "%.0f",
            v => _configuration.OverlayWidth = v);

        ImGui.Spacing();
        if (ImGui.Button("Preview overlay"))
            _showPreview();

        ImGui.SameLine();
        if (ImGui.Button("Reset layout"))
            ResetLayout();
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

    private void ResetLayout()
    {
        var defaults = new Configuration();
        _configuration.OverlayWidth = defaults.OverlayWidth;
        _configuration.OverlayOffsetX = defaults.OverlayOffsetX;
        _configuration.OffsetFromBottom = defaults.OffsetFromBottom;
        _configuration.LockOverlay = defaults.LockOverlay;
        _configuration.AnchorOverlayBottom = defaults.AnchorOverlayBottom;
        _configuration.TargetNameOffsetX = defaults.TargetNameOffsetX;
        _configuration.TargetNameOffsetY = defaults.TargetNameOffsetY;
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
        _configuration.ShowSpeakerName = defaults.ShowSpeakerName;
        _configuration.SpeakerNameColor = defaults.SpeakerNameColor;
        _configuration.TranslatedTextColor = defaults.TranslatedTextColor;
        _configuration.OriginalTextColor = defaults.OriginalTextColor;
        _configuration.ShowTargetName = defaults.ShowTargetName;
        _configuration.TargetNameOffsetX = defaults.TargetNameOffsetX;
        _configuration.TargetNameOffsetY = defaults.TargetNameOffsetY;
        _configuration.ShowItemName = defaults.ShowItemName;
        _configuration.ShowOriginal = defaults.ShowOriginal;
        _configuration.HideWhenNoTranslation = defaults.HideWhenNoTranslation;
        _configuration.LockOverlay = defaults.LockOverlay;
        _configuration.Clickthrough = defaults.Clickthrough;
        _configuration.OverlayOpacity = defaults.OverlayOpacity;
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
