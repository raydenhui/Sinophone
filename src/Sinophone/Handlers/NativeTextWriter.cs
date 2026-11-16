using System.Text;
using FFXIVClientStructs.FFXIV.Client.Graphics;
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

    /// <summary>
    /// The smallest size a fitted line may use. Below this the text is too hard to read,
    /// so the caller shows the game's own line instead.
    /// </summary>
    internal const byte MinimumFitFontSize = 10;

    private readonly IGameGui _gameGui;
    private readonly IDataManager _dataManager;
    private readonly PluginLog _log;
    private readonly Dictionary<(string Addon, uint NodeId), SavedText> _saved = [];
    private readonly Dictionary<(string Addon, nint Address), SavedText> _savedAt = [];

    /// <summary>
    /// The font size and leading the game first gave each node. The game applies them
    /// from the addon template, so it may not reset them after a fitted line. Keeping
    /// the first values stops each line from shrinking from the previous line's size.
    /// </summary>
    private readonly Dictionary<(string Addon, uint NodeId), (byte FontSize, byte LineSpacing)> _gameSizes = [];

    public NativeTextWriter(IGameGui gameGui, IDataManager dataManager, Configuration configuration, IPluginLog log)
    {
        _gameGui = gameGui;
        _dataManager = dataManager;
        _log = new PluginLog(log, configuration, LogCategory.NativeText);
    }

    /// <summary>Number of nodes currently holding text this writer put there.</summary>
    public int ChangedNodeCount => _saved.Count + _savedAt.Count;

    /// <summary>
    /// Writes <paramref name="text"/> into one text node. Returns false when the node is
    /// missing or the text is empty, so the caller can fall back to the overlay. Returns
    /// true when the node already reads that text: the line is on screen either way.
    /// </summary>
    public bool Write(string addonName, uint nodeId, string? text, UiTextStyle style = default)
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
        if (!_gameSizes.TryGetValue(key, out var gameSize))
        {
            gameSize = (node->FontSize, node->LineSpacing);
            _gameSizes[key] = gameSize;
        }

        _saved[key] = _saved.TryGetValue(key, out var saved)
                      && string.Equals(current, saved.Written, StringComparison.Ordinal)
            ? saved with
            {
                Written = text, WrittenUtf8 = writtenUtf8, Style = style, Reapplied = false,
                FitFontSize = null, FitLineSpacing = null,
            }
            : new SavedText(current, text, writtenUtf8, style,
                node->TextColor, node->EdgeColor, node->TextFlags, node->Width, node->Height,
                gameSize.FontSize, gameSize.LineSpacing);

        node->SetText(text);
        ApplyStyle(node, style);
        node->ResizeNodeForCurrentText();

        _log.Debug("Native text: wrote {Length} chars into {Addon} node {NodeId}.",
            text.Length, addonName, nodeId);
        return true;
    }

    /// <summary>
    /// Writes a line, then shrinks it until it fits the room the game gave the node for
    /// the original text. Returns false when the game's own line is left in place, so the
    /// caller can fall back to the overlay. A node that grows past the space it was given
    /// draws outside its window, which is what a long Chinese line does.
    /// </summary>
    public bool WriteFitted(string addonName, uint nodeId, string? text, UiTextStyle style = default)
    {
        if (!Write(addonName, nodeId, text, style))
            return false;

        var key = (addonName, nodeId);
        if (!_saved.TryGetValue(key, out var saved)
            || !string.Equals(saved.Written, text, StringComparison.Ordinal))
        {
            // The node already held this text, so there is no saved original to size
            // against. Nothing was written, so nothing needs fitting.
            return true;
        }

        return FitHeight(addonName, nodeId, saved.OriginalHeight);
    }

    /// <summary>
    /// Shrinks the text in a node this writer filled until it fits
    /// <paramref name="maxHeight"/>. Leading is tightened first, then the font size drops
    /// one step at a time. Returns false, with the game's text put back, when it still
    /// does not fit.
    /// </summary>
    private bool FitHeight(string addonName, uint nodeId, ushort maxHeight)
    {
        var key = (addonName, nodeId);
        var node = GetNode(addonName, nodeId);
        if (node is null || !_saved.TryGetValue(key, out var saved))
            return false;

        // Measure from the size the game gave the node, not from a size a previous
        // fitted line left behind.
        node->FontSize = saved.OriginalFontSize;
        node->LineSpacing = 0;
        node->ResizeNodeForCurrentText();

        var address = (nint)node;
        var fitted = ChooseFittingFontSize(node->FontSize, MinimumFitFontSize, maxHeight, size =>
        {
            var current = (AtkTextNode*)address;
            current->FontSize = size;
            current->ResizeNodeForCurrentText();
            return current->Height;
        });

        if (fitted is not byte fontSize)
        {
            _log.Debug(
                "Native text: {Addon} node {NodeId} needs more room than the game gave it; keeping the game text.",
                addonName, nodeId);
            Restore(addonName, nodeId);
            return false;
        }

        node->FontSize = fontSize;
        node->ResizeNodeForCurrentText();
        _saved[key] = saved with { FitFontSize = fontSize, FitLineSpacing = node->LineSpacing };

        if (fontSize < saved.OriginalFontSize || node->LineSpacing != saved.OriginalLineSpacing)
        {
            _log.Debug(
                "Native text: fitted {Addon} node {NodeId} into {Height}px at font {FontSize} (from {Original}).",
                addonName, nodeId, node->Height, fontSize, saved.OriginalFontSize);
        }

        return true;
    }

    /// <summary>
    /// Picks the largest font size at or below <paramref name="startFontSize"/> whose
    /// measured height fits <paramref name="maxHeight"/>. Returns null when even the
    /// smallest allowed size does not fit. The measuring is a callback, so the policy is
    /// tested without a live node.
    /// </summary>
    internal static byte? ChooseFittingFontSize(
        byte startFontSize,
        byte minimumFontSize,
        ushort maxHeight,
        Func<byte, ushort> measureHeight)
    {
        for (var size = startFontSize; size >= minimumFontSize; size--)
        {
            if (measureHeight(size) <= maxHeight)
                return size;

            if (size == minimumFontSize)
                break;
        }

        return null;
    }

    /// <summary>
    /// Writes into one exact text node. Selection dialogs can have several nodes with
    /// the same node id, so writing by id can change the wrong node.
    /// </summary>
    public bool WriteAt(string addonName, nint address, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var node = (AtkTextNode*)address;
        if (node is null)
            return false;

        var current = node->NodeText.ToString();
        if (string.Equals(current, text, StringComparison.Ordinal))
            return true;

        var writtenUtf8 = Encoding.UTF8.GetBytes(text);
        var key = (addonName, address);
        _savedAt[key] = _savedAt.TryGetValue(key, out var saved)
                        && string.Equals(current, saved.Written, StringComparison.Ordinal)
            ? saved with { Written = text, WrittenUtf8 = writtenUtf8, Reapplied = false }
            : new SavedText(current, text, writtenUtf8, default,
                node->TextColor, node->EdgeColor, node->TextFlags, node->Width, node->Height,
                node->FontSize, node->LineSpacing);

        node->SetText(text);
        node->ResizeNodeForCurrentText();

        _log.Debug("Native text: wrote {Length} chars into {Addon} node {NodeId} at {Address:X}.",
            text.Length, addonName, node->NodeId, address);
        return true;
    }

    /// <summary>Drops the saved text for an exact node without writing it back.</summary>
    public void ForgetAt(string addonName, nint address)
        => _savedAt.Remove((addonName, address));

    /// <summary>
    /// Reads the size the game gave an exact node before the first write. A caller that
    /// fits the node to a translation uses it to keep the line where the game drew it.
    /// </summary>
    public bool TryGetOriginalSize(string addonName, nint address, out ushort width, out ushort height)
    {
        if (_savedAt.TryGetValue((addonName, address), out var saved))
        {
            width = saved.OriginalWidth;
            height = saved.OriginalHeight;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    /// <summary>True when the exact node still holds text this writer put there.</summary>
    public bool IsStillOursAt(string addonName, nint address)
    {
        if (!_savedAt.TryGetValue((addonName, address), out var saved))
            return false;

        var node = (AtkTextNode*)address;
        return node is not null && node->NodeText.AsSpan().SequenceEqual(saved.WrittenUtf8);
    }

    /// <summary>Clears an exact node if it still holds our text.</summary>
    public bool ClearIfStillOursAt(string addonName, nint address)
    {
        var key = (addonName, address);
        if (!_savedAt.Remove(key, out var saved))
            return false;

        var node = (AtkTextNode*)address;
        if (node is null)
            return false;

        if (!node->NodeText.AsSpan().SequenceEqual(saved.WrittenUtf8))
            return true;

        node->SetText(string.Empty);
        node->ResizeNodeForCurrentText();
        _log.Debug("Native text: cleared stale text in {Addon} node {NodeId} at {Address:X}.",
            addonName, node->NodeId, address);
        return true;
    }

    /// <summary>Reapplies the saved text to an exact node.</summary>
    public bool ReapplyAt(string addonName, nint address)
    {
        var key = (addonName, address);
        if (!_savedAt.TryGetValue(key, out var saved))
            return false;

        var node = (AtkTextNode*)address;
        if (node is null)
            return false;

        if (node->NodeText.AsSpan().SequenceEqual(saved.WrittenUtf8))
        {
            ApplyStyle(node, saved.Style);
            ApplyFit(node, saved);
            return false;
        }

        node->SetText(saved.Written);
        ApplyFit(node, saved);
        node->ResizeNodeForCurrentText();

        if (!saved.Reapplied)
        {
            _savedAt[key] = saved with { Reapplied = true };
            _log.Debug("Native text: the game replaced {Addon} node {NodeId} at {Address:X}; keeping the Chinese for this line.",
                addonName, node->NodeId, address);
        }

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
        ApplyStyle(node, saved.Style);
        ApplyFit(node, saved);
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
    /// True when the node still holds the text this writer put there. Used before a
    /// second pass so a translation is not looked up again while it is on screen.
    /// </summary>
    public bool IsStillOurs(string addonName, uint nodeId)
    {
        var key = (addonName, nodeId);
        if (!_saved.TryGetValue(key, out var saved))
            return false;

        var node = GetNode(addonName, nodeId);
        return node is not null && node->NodeText.AsSpan().SequenceEqual(saved.WrittenUtf8);
    }

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
        node->TextColor = saved.OriginalTextColor;
        node->EdgeColor = saved.OriginalEdgeColor;
        node->TextFlags = saved.OriginalFlags;
        node->FontSize = saved.OriginalFontSize;
        node->LineSpacing = saved.OriginalLineSpacing;
        node->ResizeNodeForCurrentText();

        // The resize measures the text. Put the size the game had back, because a
        // handler may have changed it for the translation, such as the item tooltip's
        // one-line width.
        node->Width = saved.OriginalWidth;
        node->Height = saved.OriginalHeight;

        _log.Debug("Native text: restored {Length} chars in {Addon} node {NodeId}.",
            saved.Original.Length, addonName, nodeId);
    }

    /// <summary>Puts the game's own text back into every node this writer changed.</summary>
    public void RestoreAll()
    {
        foreach (var key in _saved.Keys.ToArray())
            Restore(key.Addon, key.NodeId);

        foreach (var key in _savedAt.Keys.ToArray())
            RestoreAt(key.Addon, key.Address);
    }

    /// <summary>Puts the game's own text back into one exact node.</summary>
    public void RestoreAt(string addonName, nint address)
    {
        var key = (addonName, address);
        if (!_savedAt.Remove(key, out var saved))
            return;

        var node = (AtkTextNode*)address;
        if (node is null)
            return;

        node->SetText(saved.Original);
        node->TextColor = saved.OriginalTextColor;
        node->EdgeColor = saved.OriginalEdgeColor;
        node->TextFlags = saved.OriginalFlags;
        node->FontSize = saved.OriginalFontSize;
        node->LineSpacing = saved.OriginalLineSpacing;
        node->ResizeNodeForCurrentText();
        node->Width = saved.OriginalWidth;
        node->Height = saved.OriginalHeight;
        _log.Debug("Native text: restored {Length} chars in {Addon} node {NodeId} at {Address:X}.",
            saved.Original.Length, addonName, node->NodeId, address);
    }

    private AtkTextNode* GetNode(string addonName, uint nodeId)
    {
        var addon = _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
        return addon is null ? null : addon->GetTextNodeById(nodeId);
    }

    /// <summary>Puts back the size and leading a fitted line needed, after the game redrew the node.</summary>
    private static void ApplyFit(AtkTextNode* node, SavedText saved)
    {
        if (saved.FitFontSize is byte fontSize)
            node->FontSize = fontSize;

        if (saved.FitLineSpacing is byte lineSpacing)
            node->LineSpacing = lineSpacing;
    }

    private void ApplyStyle(AtkTextNode* node, UiTextStyle style)
    {
        if (style.ForegroundKey is not ushort foregroundKey
            && style.GlowKey is not ushort)
        {
            return;
        }

        var sheet = _dataManager.GetExcelSheet<UIColor>();
        if (sheet is null)
            return;

        if (style.ForegroundKey is ushort fgKey
            && sheet.GetRowOrDefault(fgKey) is { } foreground)
        {
            node->TextColor = ToByteColor(foreground.Dark);
        }

        if (style.GlowKey is ushort glowKey
            && sheet.GetRowOrDefault(glowKey) is { } glow)
        {
            node->EdgeColor = ToByteColor(glow.Light);
            node->TextFlags |= TextFlags.Edge;
        }
    }

    private static ByteColor ToByteColor(uint rgba)
        => new()
        {
            R = (byte)(rgba >> 24),
            G = (byte)(rgba >> 16),
            B = (byte)(rgba >> 8),
            A = (byte)rgba,
        };

    private readonly record struct SavedText(
        string Original,
        string Written,
        byte[] WrittenUtf8,
        UiTextStyle Style,
        ByteColor OriginalTextColor,
        ByteColor OriginalEdgeColor,
        TextFlags OriginalFlags,
        ushort OriginalWidth,
        ushort OriginalHeight,
        byte OriginalFontSize,
        byte OriginalLineSpacing,
        bool Reapplied = false,
        byte? FitFontSize = null,
        byte? FitLineSpacing = null);
}
