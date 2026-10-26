using Action = System.Action;

namespace Sinophone.UI;

/// <summary>
/// Settings window for Sinophone. Every change is applied and persisted immediately.
/// </summary>
public sealed class ConfigWindow : Window
{
    private static readonly Vector4 SectionColor = new(1f, 0.82f, 0.38f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.45f, 0.45f, 1f);

    private readonly Configuration _configuration;
    private readonly Action _applySettings;
    private readonly Action _showPreview;
    private readonly Func<string> _statusText;
    private readonly TranslationDataUpdater _dataUpdater;
    private readonly Func<string> _dataSourceText;

    public ConfigWindow(
        Configuration configuration,
        Action applySettings,
        Action showPreview,
        Func<string> statusText,
        TranslationDataUpdater dataUpdater,
        Func<string> dataSourceText)
        : base("Sinophone###SinophoneConfig", ImGuiWindowFlags.NoCollapse)
    {
        _configuration = configuration;
        _applySettings = applySettings;
        _showPreview = showPreview;
        _statusText = statusText;
        _dataUpdater = dataUpdater;
        _dataSourceText = dataSourceText;

        Size = new Vector2(480f, 780f);
        SizeCondition = ImGuiCond.FirstUseEver;
        AllowPinning = false;
        ShowCloseButton = true;
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        DrawGeneralSection();
        ImGui.Spacing();

        DrawDisplaySection();
        ImGui.Spacing();

        DrawOverlaySection();
        ImGui.Spacing();

        DrawDataSection();
        ImGui.Spacing();

        DrawStatusSection();
    }

    private void DrawGeneralSection()
    {
        ImGui.TextColored(SectionColor, "General");
        ImGui.Separator();

        Checkbox("Enable translations", _configuration.Enabled, v => _configuration.Enabled = v);

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

        Checkbox("Show in Talk dialog", _configuration.ShowInTalk, v => _configuration.ShowInTalk = v);
        Checkbox("Show in Talk subtitles", _configuration.ShowInTalkSubtitle,
            v => _configuration.ShowInTalkSubtitle = v);
    }

    private void DrawDisplaySection()
    {
        ImGui.TextColored(SectionColor, "Text");
        ImGui.Separator();

        Checkbox("Show speaker name", _configuration.ShowSpeakerName, v => _configuration.ShowSpeakerName = v);
        Checkbox("Show the Chinese name of the target", _configuration.ShowTargetName,
            v => _configuration.ShowTargetName = v);
        Checkbox("Also show original English text", _configuration.ShowOriginal,
            v => _configuration.ShowOriginal = v);
        Checkbox("Hide when no translation is found", _configuration.HideWhenNoTranslation,
            v => _configuration.HideWhenNoTranslation = v);
    }

    private void DrawOverlaySection()
    {
        ImGui.TextColored(SectionColor, "Overlay");
        ImGui.Separator();

        Checkbox("Lock overlay position", _configuration.LockOverlay, v => _configuration.LockOverlay = v);
        Checkbox("Click-through", _configuration.Clickthrough, v => _configuration.Clickthrough = v);

        SliderFloat("Background opacity", _configuration.OverlayOpacity, 0f, 1f, "%.2f",
            v => _configuration.OverlayOpacity = v);
        SliderFloat("Font size", _configuration.FontSize, 12f, 42f, "%.0f px",
            v => _configuration.FontSize = v);
        SliderFloat("Width", _configuration.OverlayWidth, 280f, 1200f, "%.0f",
            v => _configuration.OverlayWidth = v);
        SliderFloat("Distance from screen bottom", _configuration.OffsetFromBottom, 20f, 520f, "%.0f",
            v => _configuration.OffsetFromBottom = v);
        SliderFloat("Target name distance from top", _configuration.TargetNameOffsetY, 20f, 520f, "%.0f",
            v => _configuration.TargetNameOffsetY = v);

        ImGui.TextUnformatted("Custom font path (optional, .ttf/.ttc/.otf)");
        var fontPath = _configuration.CustomFontPath ?? string.Empty;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##customFontPath", ref fontPath, 512))
        {
            _configuration.CustomFontPath = fontPath;
            _applySettings();
        }

        if (ImGui.Button("Preview overlay"))
            _showPreview();

        ImGui.SameLine();
        if (ImGui.Button("Reset layout"))
        {
            _configuration.OverlayWidth = 620f;
            _configuration.OffsetFromBottom = 180f;
            _configuration.LockOverlay = true;
            _applySettings();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset all settings"))
            ResetAll();
    }

    private void DrawDataSection()
    {
        ImGui.TextColored(SectionColor, "Data");
        ImGui.Separator();

        ImGui.TextWrapped(_dataSourceText());

        Checkbox("Check for data updates on load", _configuration.CheckForDataUpdates,
            v => _configuration.CheckForDataUpdates = v);

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

    private void DrawStatusSection()
    {
        ImGui.Spacing();
        ImGui.TextColored(SectionColor, "Status");
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

    private void ResetAll()
    {
        var defaults = new Configuration();
        _configuration.Enabled = defaults.Enabled;
        _configuration.Variant = defaults.Variant;
        _configuration.UseFallbackVariant = defaults.UseFallbackVariant;
        _configuration.ShowInTalk = defaults.ShowInTalk;
        _configuration.ShowInTalkSubtitle = defaults.ShowInTalkSubtitle;
        _configuration.ShowSpeakerName = defaults.ShowSpeakerName;
        _configuration.ShowTargetName = defaults.ShowTargetName;
        _configuration.TargetNameOffsetY = defaults.TargetNameOffsetY;
        _configuration.ShowOriginal = defaults.ShowOriginal;
        _configuration.HideWhenNoTranslation = defaults.HideWhenNoTranslation;
        _configuration.LockOverlay = defaults.LockOverlay;
        _configuration.Clickthrough = defaults.Clickthrough;
        _configuration.OverlayOpacity = defaults.OverlayOpacity;
        _configuration.FontSize = defaults.FontSize;
        _configuration.OverlayWidth = defaults.OverlayWidth;
        _configuration.OffsetFromBottom = defaults.OffsetFromBottom;
        _configuration.CustomFontPath = defaults.CustomFontPath;
        _configuration.CheckForDataUpdates = defaults.CheckForDataUpdates;
        _applySettings();
    }
}
