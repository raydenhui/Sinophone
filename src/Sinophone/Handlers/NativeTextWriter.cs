using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Handlers;

/// <summary>
/// Writes translated text into a game addon's own text node, so the game draws it with
/// its own layout and font. Alpha and opt-in: without a font mod the game font has no
/// CJK glyphs, so the line shows the missing-glyph character.
/// </summary>
/// <remarks>
/// Remembers the game's text for each node it changes, so the plugin can put it back
/// when native mode is turned off or the plugin unloads. The game rewrites its text on
/// the next line, so the saved copy only covers the line on screen.
/// </remarks>
public sealed unsafe class NativeTextWriter
{
    /// <summary>
    /// Node holding the dialogue line in the <c>Talk</c> addon. Confirmed from the live
    /// addon log: node 2 is the speaker name, node 3 is the dialogue.
    /// </summary>
    public const uint TalkTextNodeId = 3;

    /// <summary>Node holding the speaker name in the <c>Talk</c> addon.</summary>
    public const uint TalkNameNodeId = 2;

    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly Dictionary<(string Addon, uint NodeId), SavedText> _saved = [];

    public NativeTextWriter(IGameGui gameGui, IPluginLog log)
    {
        _gameGui = gameGui;
        _log = log;
    }

    /// <summary>Number of nodes currently holding text this writer put there.</summary>
    public int ChangedNodeCount => _saved.Count;

    /// <summary>
    /// Writes <paramref name="text"/> into one text node. Returns false when the node is
    /// missing, the text is empty, or the node already reads that text, so the caller can
    /// fall back to the overlay.
    /// </summary>
    public bool Write(string addonName, uint nodeId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var node = GetNode(addonName, nodeId);
        if (node is null)
        {
            _log.Debug("Native text: no text node {NodeId} in {Addon}.", nodeId, addonName);
            return false;
        }

        var current = node->NodeText.ToString();
        if (string.Equals(current, text, StringComparison.Ordinal))
            return false;

        // Save the game's text only when the node does not already hold our own text.
        // Otherwise a second write would save Chinese as the original.
        var key = (addonName, nodeId);
        _saved[key] = _saved.TryGetValue(key, out var saved)
                      && string.Equals(current, saved.Written, StringComparison.Ordinal)
            ? saved with { Written = text }
            : new SavedText(current, text);

        node->SetText(text);
        node->ResizeNodeForCurrentText();

        _log.Debug("Native text: wrote {Length} chars into {Addon} node {NodeId}.",
            text.Length, addonName, nodeId);
        return true;
    }

    /// <summary>Puts the game's own text back into one node.</summary>
    public void Restore(string addonName, uint nodeId)
    {
        var key = (addonName, nodeId);
        if (!_saved.Remove(key, out var saved))
            return;

        var node = GetNode(addonName, nodeId);
        if (node is null)
        {
            _log.Debug("Native text: node {NodeId} in {Addon} is gone; nothing to restore.",
                nodeId, addonName);
            return;
        }

        node->SetText(saved.Original);
        node->ResizeNodeForCurrentText();

        _log.Debug("Native text: restored {Length} chars in {Addon} node {NodeId}.",
            saved.Original.Length, addonName, nodeId);
    }

    /// <summary>Puts the game's own text back into every node this writer changed.</summary>
    public void RestoreAll()
    {
        if (_saved.Count == 0)
            return;

        foreach (var key in _saved.Keys.ToArray())
            Restore(key.Addon, key.NodeId);
    }

    private AtkTextNode* GetNode(string addonName, uint nodeId)
    {
        var addon = _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
        return addon is null ? null : addon->GetTextNodeById(nodeId);
    }

    private readonly record struct SavedText(string Original, string Written);
}
