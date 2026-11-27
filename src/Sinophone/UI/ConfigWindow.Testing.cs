using Action = System.Action;
using Dalamud.Utility;

namespace Sinophone.UI;

public sealed partial class ConfigWindow
{
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

        Section("Game font");
        ImGui.TextWrapped(
            "Reads the game's loaded font. Penumbra and the built-in replacement both work by " +
            "replacing the game's font files, so reload the font after changing either one. If " +
            "the Chinese characters are missing, check that the pack is the (Global) version.");
        if (ImGui.Button("Check the Chinese font"))
            _gameFontCheck = GlyphProbe.Describe(FontType.Axis);

        ImGui.SameLine();
        if (ImGui.Button("Check every font"))
        {
            _gameFontCheck = string.Join(
                "\n",
                Enum.GetValues<FontType>().Select(static font => GlyphProbe.Describe(font)));
        }

        ImGui.TextWrapped(_gameFontCheck);

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
            if (result.Findings.Count > 0 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(string.Join(
                    "\n",
                    result.Findings.Select(static finding =>
                        $"{finding.Check}: {(finding.Passed ? "pass" : "FAIL")} - {finding.Detail}")));
            }
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

#if SINOPHONE_TESTING
    /// <summary>Keeps a scanned node label short enough to read in the list.</summary>
    private static string Shorten(string text)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= 48 ? flat : string.Concat(flat.AsSpan(0, 48), "...");
    }
#endif
}
