using FFXIVClientStructs.FFXIV.Client.UI;

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
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly NativeTextWriter _nativeWriter;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;

    private readonly List<nint> _nodes = [];
    private readonly Dictionary<string, List<uint>> _translated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lateAttempts = new(StringComparer.Ordinal);

    private bool _disposed;

    public SelectionDialogHandler(
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        NativeTextWriter nativeWriter)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _log = log;
        _configuration = configuration;
        _getStore = getStore;
        _nativeWriter = nativeWriter;
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

        var addonName = args.AddonName;

        if (eventType == AddonEvent.PreDraw)
        {
            Reapply(addonName);
            TranslateLate(addonName);
            return;
        }

        if (eventType is AddonEvent.PostHide or AddonEvent.PostClose or AddonEvent.PreFinalize)
        {
            ClearTracked(addonName);
            return;
        }

        if (!_configuration.Enabled || !_configuration.NativeTextMode)
            return;

        if (eventType == AddonEvent.PostSetup)
            ClearTracked(addonName);

        _lateAttempts[addonName] = 0;
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

        if (string.Equals(addonName, "CutSceneSelectString", StringComparison.Ordinal))
            CollectCutSceneTextNodes(addon);

        var traceNodes = string.Equals(addonName, "CutSceneSelectString", StringComparison.Ordinal)
                         && _lateAttempts.GetValueOrDefault(addonName) <= 1;
        if (traceNodes)
            _log.Debug("CutSceneSelectString scan: {Count} text nodes.", _nodes.Count);

        foreach (var nodeAddress in _nodes)
        {
            var node = (AtkTextNode*)nodeAddress;
            var nodeId = node->NodeId;
            if (nodeId == 0 || _nativeWriter.IsStillOurs(addonName, nodeId))
                continue;

            var source = node->NodeText.ToString();
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (traceNodes)
                _log.Debug("CutSceneSelectString node {NodeId}: {Text}", nodeId, source);

            if (!TryTranslate(store, source, out var chinese))
            {
                // Drop a saved translation if this node has moved on to a line we
                // cannot translate. English stays on screen.
                _nativeWriter.ClearIfStillOurs(addonName, nodeId);
                continue;
            }

            if (_nativeWriter.Write(addonName, nodeId, chinese))
            {
                Remember(addonName, nodeId);
                _log.Debug("Selection dialog {Addon} node {NodeId}: {Source} -> {Chinese}",
                    addonName, nodeId, source, chinese);
            }
        }
    }

    private unsafe AtkUnitBase* GetAddon(string addonName)
        => _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);

    /// <summary>
    /// The cutscene dialog keeps its prompt and options in typed component fields,
    /// not in the normal node tree. Add their text nodes to the scan.
    /// </summary>
    private unsafe void CollectCutSceneTextNodes(AtkUnitBase* addon)
    {
        var cutScene = (AddonCutSceneSelectString*)addon;

        if (cutScene->TextLabel is not null && cutScene->TextLabel->AtkTextNode is not null)
            AddNode(cutScene->TextLabel->AtkTextNode);

        var list = cutScene->OptionList;
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

    private void TranslateLate(string addonName)
    {
        if (!_configuration.Enabled || !_configuration.NativeTextMode)
            return;

        var attempts = _lateAttempts.GetValueOrDefault(addonName);
        if (attempts >= MaxLateAttempts)
            return;

        _lateAttempts[addonName] = attempts + 1;
        Translate(addonName);
    }

    private void Reapply(string addonName)
    {
        if (!_configuration.Enabled || !_configuration.NativeTextMode)
            return;

        if (!_translated.TryGetValue(addonName, out var nodeIds))
            return;

        foreach (var nodeId in nodeIds)
            _nativeWriter.Reapply(addonName, nodeId);
    }

    private void Remember(string addonName, uint nodeId)
    {
        if (!_translated.TryGetValue(addonName, out var nodeIds))
        {
            nodeIds = [];
            _translated[addonName] = nodeIds;
        }

        if (!nodeIds.Contains(nodeId))
            nodeIds.Add(nodeId);
    }

    private void ClearTracked(string addonName)
    {
        _lateAttempts.Remove(addonName);

        if (!_translated.Remove(addonName, out var nodeIds))
            return;

        foreach (var nodeId in nodeIds)
            _nativeWriter.ClearIfStillOurs(addonName, nodeId);
    }

    /// <summary>
    /// Looks up one selection-dialog string. Short UI labels are also tried with a
    /// trailing period, because the Addon sheet stores "Yes." while the button can
    /// show "Yes".
    /// </summary>
    internal static bool TryTranslate(TranslationStore store, string? text, out string chinese)
    {
        chinese = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (store.TryGetDialogue(text, out chinese))
            return true;

        var trimmed = text.TrimEnd('.', '。', '!', '！', '?', '？');
        if (trimmed.Length > 0
            && trimmed != text
            && store.TryGetDialogue(trimmed, out chinese))
        {
            return true;
        }

        // "Yes" / "No" / "OK" / "Cancel" are short. Do not apply this to normal
        // dialogue, where adding a period could match a different line.
        if (text.Length <= 16
            && text.Length > 0
            && !text.EndsWith('.')
            && store.TryGetDialogue(string.Concat(text, "."), out chinese))
        {
            return true;
        }

        chinese = string.Empty;
        return false;
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
