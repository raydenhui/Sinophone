using Dalamud.Game.Text.Evaluator;

using FFXIVClientStructs.FFXIV.Client.UI;

using Sinophone.Data;
using Sinophone.Diagnostics;

namespace Sinophone.Handlers;

/// <summary>
/// Translates the game's selection dialogs in place. Native mode only: the English
/// text in the addon is replaced with Chinese, and no overlay is shown.
/// </summary>
/// <remarks>
/// The dialog addons are small trees of text nodes. The handler walks the current
/// tree, looks each visible string up in the translation store, and writes the
/// result back into the same node. A miss leaves the English text alone.
/// </remarks>
public sealed class SelectionDialogHandler : IDisposable
{
    private static readonly string[] AddonNames =
    [
        "SelectYesno",
        "SelectString",
        "SelectIconString",
        "SelectOk",
        "CutSceneSelectString",
    ];

    private const int MaxTreeDepth = 64;

    /// <summary>
    /// The cutscene addon builds its option list after setup. Try again for a short
    /// time on later frames, then stop so a dialog with no translations does not
    /// walk its tree every frame.
    /// </summary>
    private const int MaxLateAttempts = 12;

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly PluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly NativeTextWriter _nativeWriter;
    private readonly ISeStringEvaluator _evaluator;
    private readonly TalkEventReader _eventReader;
    private readonly FailureRecorder? _failures;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;

    private readonly List<nint> _nodes = [];
    private readonly Dictionary<string, List<nint>> _translated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lateAttempts = new(StringComparer.Ordinal);

    /// <summary>
    /// The script keys the open dialog has exposed, from the active Lua handler and from the
    /// lines it has already shown. Options come from one script, so a key from the prompt can
    /// pick the line for an option that many scripts share.
    /// </summary>
    private readonly Dictionary<string, List<string>> _dialogScriptHints = new(StringComparer.Ordinal);

    /// <summary>
    /// The event context for each text already read in this dialog refresh. The context walks the
    /// whole handler map, so reading it again for every node and every retry made a large menu
    /// visibly hitch.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, DialogueContext>> _dialogContexts = new(StringComparer.Ordinal);

    /// <summary>Addons already reported as waiting for the translation store, so the log stays one line.</summary>
    private readonly HashSet<string> _waitingForStore = new(StringComparer.Ordinal);

    /// <summary>
    /// Addons whose AtkValues were already logged. The values are what a future
    /// change needs to fill the template locals exactly, but they are only read
    /// for the log once per dialog.
    /// </summary>
    private readonly HashSet<string> _loggedAtkValues = new(StringComparer.Ordinal);

    /// <summary>
    /// The last text each node failed on, so a dialog retried twelve times logs one line per node
    /// instead of twelve.
    /// </summary>
    private readonly Dictionary<nint, string> _reportedMiss = [];

    /// <summary>
    /// What this handler last wrote into each node. The writer's saved entry can be dropped while
    /// the node keeps the translation, and then the node would be looked up as if it were source
    /// text; this is what tells the two apart.
    /// </summary>
    private readonly Dictionary<nint, string> _written = [];

    private bool _disposed;

    public SelectionDialogHandler(
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        NativeTextWriter nativeWriter,
        ISeStringEvaluator evaluator,
        FailureRecorder? failures = null)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _log = new PluginLog(log, configuration, LogCategory.SelectionDialogs);
        _configuration = configuration;
        _getStore = getStore;
        _nativeWriter = nativeWriter;
        _evaluator = evaluator;
        _failures = failures;
        _eventReader = new TalkEventReader(log, configuration, LogCategory.SelectionDialogs);
        _onAddonEvent = OnAddonEvent;

        foreach (var addonName in AddonNames)
        {
            _addonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
            _addonLifecycle.RegisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
        }
    }

    private unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        // This is a hook callback, so a throw would reach the game's frame. Wrap every
        // event here rather than in each branch: the dialogue keeps its English text.
        HookGuard.Run(_log, "Selection dialog", () => HandleAddonEvent(eventType, args));
    }

    private unsafe void HandleAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        var addonName = args.AddonName;

        if (eventType == AddonEvent.PreDraw)
        {
            if (!IsEnabled(addonName))
            {
                // Turned off while the dialog is open. Put the game's own text back.
                ClearTracked(addonName);
                return;
            }

            Reapply(addonName);
            TranslateLate(addonName);
            return;
        }

        if (eventType is AddonEvent.PostHide or AddonEvent.PostClose or AddonEvent.PreFinalize)
        {
            _dialogScriptHints.Remove(addonName);
            _dialogContexts.Remove(addonName);
            _loggedAtkValues.Remove(addonName);

            // A delayed hide from an old dialog must not clear text that a newer
            // dialog has already written into a reused node.
            if (eventType == AddonEvent.PreFinalize)
                ClearTracked(addonName);
            return;
        }

        if (eventType == AddonEvent.PostSetup)
        {
            _dialogScriptHints.Remove(addonName);
            _dialogContexts.Remove(addonName);
            _loggedAtkValues.Remove(addonName);
        }
        else if (eventType == AddonEvent.PostRefresh)
        {
            _dialogContexts.Remove(addonName);
        }

        if (!IsEnabled(addonName))
            return;

        if (eventType == AddonEvent.PostSetup)
            ForgetTracked(addonName);

        _lateAttempts[addonName] = 0;
        _reportedMiss.Clear();
        _written.Clear();
        Translate(addonName);
    }

    private unsafe void Translate(string addonName)
    {
        var store = _getStore();
        if (store is null)
            return;

        var addon = GetAddon(addonName);
        if (addon is null)
            return;

        LogAtkValues(addonName, addon);

        _nodes.Clear();

        // Most addons expose their text under RootNode, but the cutscene selection
        // addon can have its nodes only in UldManager's list. Walk both.
        if (addon->RootNode is not null)
            CollectTextNodes(addon->RootNode, 0);

        if (addon->UldManager.NodeList is not null)
        {
            for (var i = 0; i < addon->UldManager.NodeListCount; i++)
                CollectTextNodes(addon->UldManager.NodeList[i], 0);
        }

        CollectTypedTextNodes(addonName, addon);

        var traceNodes = string.Equals(addonName, "CutSceneSelectString", StringComparison.Ordinal)
                         && _lateAttempts.GetValueOrDefault(addonName) <= 1;
        if (traceNodes)
            _log.Debug("CutSceneSelectString scan: {Count} text nodes.", _nodes.Count);

        // The live cutscene task is not always there when the options appear, but every option
        // comes from the same scene. One unambiguous option can name the scene for the rest.
        var dialogCutscene = ReadDialogCutscene(store);
        if (dialogCutscene != 0)
        {
            _log.Debug(
                "Selection dialog {Addon}: using scene {Scene} from the dialog text.",
                addonName, dialogCutscene);
        }

        var scriptHints = DialogScriptHints(addonName);

        foreach (var nodeAddress in _nodes)
        {
            var node = (AtkTextNode*)nodeAddress;
            var nodeId = node->NodeId;
            if (nodeId == 0 || _nativeWriter.IsStillOursAt(addonName, nodeAddress))
                continue;

            var source = TalkHandler.ExtractPlainText(node->NodeText.AsSpan());
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (_written.TryGetValue(nodeAddress, out var written)
                && string.Equals(written, source, StringComparison.Ordinal))
            {
                continue;
            }

            if (traceNodes)
                _log.Debug("CutSceneSelectString node {NodeId}: {Text}", nodeId, source);

            var context = DialogContext(addonName, source, scriptHints.Count == 0);
            var learnedHint = AddDialogHints(scriptHints, context.ScriptKeys);
            learnedHint |= AddDialogHints(scriptHints, context.ScriptKeyHints);
            if (learnedHint)
            {
                _log.Debug(
                    "Selection dialog {Addon}: script hints now {Hints}.",
                    addonName, string.Join(", ", scriptHints));
            }

            if (context.CutsceneId == 0 && dialogCutscene != 0)
                context = context with { CutsceneId = dialogCutscene };
            if (scriptHints.Count > 0)
                context = context with { ScriptKeyHints = scriptHints };

            var trace = _failures?.Begin("selection", nameof(SelectionDialogHandler), source);
            if (trace is not null)
            {
                trace.Input("addon", addonName);
                trace.Input("node", nodeId.ToString());
                if (context.Row is { } row)
                {
                    trace.Input("sheet", row.Sheet);
                    trace.Input("row", row.RowId.ToString());
                }

                if (context.ScriptKeys is { Count: > 0 } keys)
                    trace.Input("scriptKeys", string.Join(", ", keys));
            }

            if (!SelectionPromptText.TryTranslate(store, source, context, _evaluator, out var chinese, trace))
            {
                trace?.Finish(FailureOutcome.Miss, "no-translation");

                // Drop a saved translation if this node has moved on to a line we
                // cannot translate. English stays on screen.
                _nativeWriter.ClearIfStillOursAt(addonName, nodeAddress);

                // A prompt that stays English is either a miss or the wrong timing; the text and
                // the addon name are what a report needs to tell those apart. One line per node
                // and text, because a dialog is retried a dozen times.
                if (!_reportedMiss.TryGetValue(nodeAddress, out var reported)
                    || !string.Equals(reported, source, StringComparison.Ordinal))
                {
                    _reportedMiss[nodeAddress] = source;

                    _log.Debug(
                        "Selection dialog {Addon} node {NodeId}: no translation for {Text} " +
                        "({Length} chars) — {Trace}",
                        addonName, nodeId, Excerpt(source), source.Length, DescribeMiss(store, source));
                }

                continue;
            }

            var fitted = FitPromptLines(node, source, chinese);
            if (_nativeWriter.WriteAt(addonName, nodeAddress, fitted))
            {
                _written[nodeAddress] = fitted;
                Remember(addonName, nodeAddress);
                trace?.Finish(FailureOutcome.Hit);
                _log.Debug("Selection dialog {Addon} node {NodeId}: {Source} -> {Chinese}",
                    addonName, nodeId, source, fitted);
            }
            else
            {
                trace?.Finish(FailureOutcome.Miss, "write-failed");
            }
        }
    }

    private DialogueContext DialogContext(string addonName, string source, bool scanAllLuaTexts)
    {
        if (!_dialogContexts.TryGetValue(addonName, out var contexts))
        {
            contexts = new Dictionary<string, DialogueContext>(StringComparer.Ordinal);
            _dialogContexts[addonName] = contexts;
        }

        if (contexts.TryGetValue(source, out var context))
            return context;

        context = _eventReader.ReadContext(source, includeHandlerHints: true, scanAllLuaTexts: scanAllLuaTexts);
        contexts[source] = context;
        return context;
    }

    private List<string> DialogScriptHints(string addonName)
    {
        if (!_dialogScriptHints.TryGetValue(addonName, out var hints))
        {
            hints = [];
            _dialogScriptHints[addonName] = hints;
        }

        return hints;
    }

    private static bool AddDialogHints(List<string> hints, IReadOnlyList<string>? keys)
    {
        if (keys is null)
            return false;

        var added = false;
        foreach (var key in keys)
        {
            if (key.Length == 0 || hints.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            hints.Add(key);
            added = true;
        }

        return added;
    }

    private unsafe uint ReadDialogCutscene(TranslationStore store)
    {
        var sources = new List<string>(_nodes.Count);

        foreach (var nodeAddress in _nodes)
        {
            var source = TalkHandler.ExtractPlainText(((AtkTextNode*)nodeAddress)->NodeText.AsSpan());
            if (!string.IsNullOrWhiteSpace(source))
                sources.Add(source);
        }

        return SelectionPromptText.DialogCutscene(store, sources);
    }

    private unsafe AtkUnitBase* GetAddon(string addonName)
        => _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);

    /// <summary>
    /// Logs the values the game passed to the dialog, once per dialog. The prompt's
    /// IntegerParameter and StringParameter values are in here; the exact index
    /// mapping has to be confirmed in game before it can replace the text parse.
    /// </summary>
    private unsafe void LogAtkValues(string addonName, AtkUnitBase* addon)
    {
        if (!_loggedAtkValues.Add(addonName))
            return;

        if (addon->AtkValues is null || addon->AtkValuesCount == 0)
        {
            _log.Debug("Selection dialog {Addon}: no AtkValues.", addonName);
            return;
        }

        var count = Math.Min((int)addon->AtkValuesCount, 16);
        var values = new StringBuilder(count * 12);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                values.Append(", ");

            values.Append('[').Append(i).Append("] ").Append(addon->AtkValues[i].GetValueAsString());
        }

        _log.Debug(
            "Selection dialog {Addon}: {Count} AtkValues: {Values}",
            addonName, addon->AtkValuesCount, values);
    }

    /// <summary>
    /// Some dialogs keep their prompt and options in typed component fields, not
    /// in the normal node tree. Add those text nodes to the scan.
    /// </summary>
    private unsafe void CollectTypedTextNodes(string addonName, AtkUnitBase* addon)
    {
        switch (addonName)
        {
            case "SelectYesno":
            {
                var yesNo = (AddonSelectYesno*)addon;
                if (yesNo->PromptText is not null)
                    AddNode(yesNo->PromptText);

                AddButtonNode(yesNo->YesButton);
                AddButtonNode(yesNo->NoButton);
                AddButtonNode(yesNo->AtkComponentButton238);
                break;
            }

            case "SelectOk":
            {
                var selectOk = (AddonSelectOk*)addon;
                if (selectOk->PromptText is not null)
                    AddNode(selectOk->PromptText);

                AddButtonNode(selectOk->OkButton);
                break;
            }

            case "SelectString":
                AddListNodes(((AddonSelectString*)addon)->PopupMenu.PopupMenu.List);
                break;

            case "SelectIconString":
                AddListNodes(((AddonSelectIconString*)addon)->PopupMenu.PopupMenu.List);
                break;

            case "CutSceneSelectString":
            {
                var cutScene = (AddonCutSceneSelectString*)addon;
                if (cutScene->TextLabel is not null && cutScene->TextLabel->AtkTextNode is not null)
                    AddNode(cutScene->TextLabel->AtkTextNode);

                AddListNodes(cutScene->OptionList);
                break;
            }
        }
    }

    private unsafe void AddButtonNode(AtkComponentButton* button)
    {
        if (button is not null && button->ButtonTextNode is not null)
            AddNode(button->ButtonTextNode);
    }

    private unsafe void AddListNodes(AtkComponentList* list)
    {
        if (list is null)
            return;

        var count = list->ListLength;
        for (var i = 0; i < count; i++)
        {
            var renderer = list->GetItemRenderer(i);
            if (renderer is not null && renderer->ButtonTextNode is not null)
                AddNode(renderer->ButtonTextNode);
        }
    }

    private unsafe void AddNode(AtkTextNode* textNode)
    {
        var address = (nint)textNode;
        if (!_nodes.Contains(address))
            _nodes.Add(address);
    }

    private unsafe void CollectTextNodes(AtkResNode* node, int depth)
    {
        if (node is null || depth > MaxTreeDepth)
            return;

        for (var current = node; current is not null; current = current->NextSiblingNode)
        {
            if (current->Type == NodeType.Text)
            {
                var textNode = current->GetAsAtkTextNode();
                if (textNode is not null)
                    AddNode(textNode);
            }

            // Some addons, such as the SelectString popup, keep their entries in a
            // component's own node list. Walk that list as well as the normal child
            // tree so those entries are not missed.
            if ((ushort)current->Type >= 1000)
            {
                var componentNode = (AtkComponentNode*)current;
                var component = componentNode->Component;
                if (component is not null && component->UldManager.NodeList is not null)
                {
                    for (var i = 0; i < component->UldManager.NodeListCount; i++)
                        CollectTextNodes(component->UldManager.NodeList[i], depth + 1);
                }
            }

            if (current->ChildNode is not null)
                CollectTextNodes(current->ChildNode, depth + 1);
        }
    }

    private bool IsEnabled(string addonName)
        => _configuration.Enabled
           && _configuration.ShowInSelectionDialogs
           && _configuration.IsSelectionDialogEnabled(addonName);

    private void TranslateLate(string addonName)
    {
        if (!IsEnabled(addonName))
            return;

        if (_getStore() is null)
        {
            // The index is still loading. Spending an attempt here would end the retries before
            // the store exists, and a dialog opened during a reload would stay English for its
            // whole life even though every line is in the data.
            if (_waitingForStore.Add(addonName))
                _log.Debug("Selection dialog {Addon}: the translation store is not ready yet; retrying.", addonName);

            return;
        }

        _waitingForStore.Remove(addonName);

        var attempts = _lateAttempts.GetValueOrDefault(addonName);
        if (attempts >= MaxLateAttempts)
            return;

        _lateAttempts[addonName] = attempts + 1;
        Translate(addonName);
    }

    /// <summary>
    /// Which lookup step failed, for a prompt that stayed English. Each step is re-run on its own
    /// so the log says whether the label, the dialogue text, the template, or the resolve after the
    /// template is the one that gave up.
    /// </summary>
    private static string DescribeMiss(TranslationStore store, string text)
    {
        var plain = MacroNormalizer.CleanForDisplay(text);

        var label = store.TryGetAddonLabel(plain, out _);
        var dialogue = store.TryGetDialogue(text, out _);
        var template = store.TryGetAddonText(plain, out var chinese, out var captures, out var indices);

        if (!template)
        {
            return $"label {label}, dialogue {dialogue}, template false, " +
                   $"{store.AddonTemplateCount:N0} templates, row like this: {store.HasAddonPromptLike(text)}";
        }

        var resolved = SelectionPromptText.ResolveAddonText(chinese, plain, captures, store, indices);
        return $"template true ({captures.Length} captures, {chinese.Length} chars), " +
               $"resolved {resolved.Length} chars";
    }

    /// <summary>
    /// The text on one line, long enough to compare with the sheet. The game wraps a prompt with
    /// line breaks, so the whole text is flattened rather than cut at the first break.
    /// </summary>
    private static string Excerpt(string text)
    {
        var flat = string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }

    private void Reapply(string addonName)
    {
        if (!IsEnabled(addonName))
            return;

        if (!_translated.TryGetValue(addonName, out var nodeIds))
            return;

        foreach (var address in nodeIds)
            _nativeWriter.ReapplyAt(addonName, address);
    }

    private void Remember(string addonName, nint address)
    {
        if (!_translated.TryGetValue(addonName, out var addresses))
        {
            addresses = [];
            _translated[addonName] = addresses;
        }

        if (!addresses.Contains(address))
            addresses.Add(address);
    }

    private void ClearTracked(string addonName)
    {
        _lateAttempts.Remove(addonName);

        if (!_translated.Remove(addonName, out var addresses))
            return;

        foreach (var address in addresses)
            _nativeWriter.ClearIfStillOursAt(addonName, address);
    }

    /// <summary>
    /// Drops saved state for a recycled dialog without touching the node text.
    /// Clearing here can erase a prompt that the new dialog has just written.
    /// </summary>
    private void ForgetTracked(string addonName)
    {
        _lateAttempts.Remove(addonName);

        if (!_translated.Remove(addonName, out var addresses))
            return;

        foreach (var address in addresses)
            _nativeWriter.ForgetAt(addonName, address);
    }

    private static unsafe string FitPromptLines(AtkTextNode* node, string source, string chinese)
        => SelectionPromptText.FitPromptLines(
            source,
            chinese,
            node->Width,
            node->Height,
            text => AtkTextMeasure.Measure(node, text));

    /// <summary>
    /// Puts the game's own text back into every dialog this handler has changed.
    /// Called when a setting turns the surface off, so an open dialog does not
    /// keep Chinese until it closes.
    /// </summary>
    public void RestoreAll()
    {
        if (_disposed)
            return;

        foreach (var addonName in AddonNames)
            ClearTracked(addonName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var addonName in AddonNames)
        {
            _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
            _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
            ClearTracked(addonName);
        }
    }
}
