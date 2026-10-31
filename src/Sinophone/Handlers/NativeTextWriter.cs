using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Handlers;

/// <summary>
/// Writes translated text into a game addon's own text node, so the game draws it with
/// its own layout and font. Alpha and opt-in: without a font mod the game font has no
/// CJK glyphs, so the line shows the missing-glyph character.
/// </summary>
/// <remarks>
/// Remembers the game's text for each node it changes, so the plugin can put it back
/// when native mode is turned off or the plugin unloads. The saved copy only covers the
/// line on screen.
/// <para>
/// One write is not enough. The <c>Talk</c> window redraws its line while the text
/// stream runs, which replaces the node text with English after about a frame. The
/// caller keeps the Chinese on screen by calling <see cref="Reapply"/> on every draw.
/// </para>
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
    /// missing or the text is empty, so the caller can fall back to the overlay. Returns
    /// true when the node already reads that text: the line is on screen either way.
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
            return true;

        var writtenUtf8 = Encoding.UTF8.GetBytes(text);

        // Save the game's text only when the node does not already hold our own text.
        // Otherwise a second write would save Chinese as the original.
        var key = (addonName, nodeId);
        _saved[key] = _saved.TryGetValue(key, out var saved)
                      && string.Equals(current, saved.Written, StringComparison.Ordinal)
            ? saved with { Written = text, WrittenUtf8 = writtenUtf8, Reapplied = false }
            : new SavedText(current, text, writtenUtf8);

        node->SetText(text);
        node->ResizeNodeForCurrentText();

        _log.Debug("Native text: wrote {Length} chars into {Addon} node {NodeId}.",
            text.Length, addonName, nodeId);
        return true;
    }

    /// <summary>
    /// Writes a line again when the game has replaced it. Runs on every draw of the
    /// <c>Talk</c> window, so it compares the node's bytes with the last write and
    /// allocates nothing when the text still matches.
    /// </summary>
    /// <returns>True when the game's text had to be replaced.</returns>
    public bool Reapply(string addonName, uint nodeId)
    {
        var key = (addonName, nodeId);
        if (!_saved.TryGetValue(key, out var saved))
            return false;

        var node = GetNode(addonName, nodeId);
        if (node is null)
            return false;

        // Utf8String.AsSpan() is a view over the node's buffer, so this compare does
        // not allocate. ToString() here would allocate on every frame.
        if (node->NodeText.AsSpan().SequenceEqual(saved.WrittenUtf8))
            return false;

        node->SetText(saved.Written);
        node->ResizeNodeForCurrentText();

        if (!saved.Reapplied)
        {
            // Log once per line: the game replaces the node on each frame of the text
            // stream, and a log per frame would bury everything else.
            _saved[key] = saved with { Reapplied = true };
            _log.Debug("Native text: the game replaced {Addon} node {NodeId}; keeping the Chinese for this line.",
                addonName, nodeId);
        }

        return true;
    }

    /// <summary>
    /// Drops the saved text for a node without writing it back. Used when a line has no
    /// translation: the next <see cref="Reapply"/> must not put the previous line's
    /// Chinese over it.
    /// </summary>
    public void Forget(string addonName, uint nodeId)
        => _saved.Remove((addonName, nodeId));

    /// <summary>
    /// Clears a node that still holds text from the previous line. If the game has
    /// already replaced it with the current line's text, leave that text alone.
    /// Used for a speaker name when the current line has no speaker.
    /// </summary>
    public bool ClearIfStillOurs(string addonName, uint nodeId)
    {
        var key = (addonName, nodeId);
        if (!_saved.Remove(key, out var saved))
            return false;

        var node = GetNode(addonName, nodeId);
        if (node is null)
            return false;

        if (!node->NodeText.AsSpan().SequenceEqual(saved.WrittenUtf8))
            return true;

        node->SetText(string.Empty);
        node->ResizeNodeForCurrentText();
        _log.Debug("Native text: cleared stale text in {Addon} node {NodeId}.",
            addonName, nodeId);
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

    private readonly record struct SavedText(
        string Original,
        string Written,
        byte[] WrittenUtf8,
        bool Reapplied = false);
}
