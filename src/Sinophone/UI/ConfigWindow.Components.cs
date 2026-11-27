using Action = System.Action;
using Dalamud.Utility;

namespace Sinophone.UI;

public sealed partial class ConfigWindow
{
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
            ImGui.TextDisabled(text.GameFontInGeneral);
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
}
