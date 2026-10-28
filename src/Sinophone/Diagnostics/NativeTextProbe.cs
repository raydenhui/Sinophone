#if DEBUG
namespace Sinophone.Diagnostics;

/// <summary>
/// Development-only probe for native text mode. Writes Chinese into a text node of any
/// game addon, so we can see whether the game font draws CJK glyphs and whether a node
/// can be changed at all.
/// </summary>
/// <remarks>
/// Debug builds only. Everything that touches this class is also behind <c>#if DEBUG</c>,
/// so a release build contains no probe code and no "Native test" tab.
/// It stores nothing on disk, so a plugin reload undoes everything it changed.
/// </remarks>
public sealed unsafe class NativeTextProbe : IDisposable
{
    /// <summary>
    /// Traditional first, because the first run showed Traditional glyphs drawing and
    /// some Simplified ones showing "=". The "=" is the game's missing-glyph glyph.
    /// </summary>
    public const string DefaultSample =
        "這是中文樣本：遊戲字型能否顯示漢字？\n这是中文样本：游戏字体能否显示汉字？";

    /// <summary>Sample presets. Each one answers a different question.</summary>
    public static readonly (string Label, string Text)[] Presets =
    [
        ("Traditional only", "這是中文樣本：遊戲字型能否顯示漢字？"),
        ("Simplified only", "这是中文样本：游戏字体能否显示汉字？"),
        ("Traditional vs simplified pairs", "這这 樣样 遊游 戲戏 體体 顯显 漢汉"),
        ("Rare Traditional", "歷閱臺灣龍鳳龜"),
        ("Latin control", "Latin control line ABC 123"),
    ];

    /// <summary>Addons with text worth trying. Any other name can be typed in.</summary>
    public static readonly string[] KnownAddons =
    [
        "Talk",
        "TalkSubtitle",
        "Journal",
        "JournalDetail",
        "ItemDetail",
        "TargetInfo",
        "RecommendList",
    ];

    private const int MaxTreeDepth = 64;

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private readonly List<(uint Id, string Text)> _scanned = [];

    private string _registeredAddon = string.Empty;
    private string _savedAddon = string.Empty;
    private uint _savedNodeId;
    private string _savedText = string.Empty;
    private bool _hasSaved;

    public NativeTextProbe(IAddonLifecycle addonLifecycle, IGameGui gameGui, IPluginLog log)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _log = log;
        _onAddonEvent = OnAddonEvent;

        AddonName = "Talk";
        NodeId = 3;
    }

    /// <summary>Addon to read and write. Changing it moves the sticky listener.</summary>
    public string AddonName { get; private set; }

    /// <summary>Text node to write. Scanned from the addon's node tree.</summary>
    public uint NodeId { get; set; }

    /// <summary>Text written into the node.</summary>
    public string Sample { get; set; } = DefaultSample;

    /// <summary>Font the sample is drawn with. The game only offers these six.</summary>
    public FontType FontType { get; set; } = FontType.Axis;

    private bool _sticky;

    /// <summary>
    /// Rewrite the sample every time the addon refreshes. Needed if the game puts its
    /// own text back after a single write.
    /// </summary>
    public bool Sticky
    {
        get => _sticky;
        set
        {
            if (_sticky == value)
                return;

            _sticky = value;

            if (value)
                RegisterSticky();
            else
                UnregisterSticky();
        }
    }

    /// <summary>Text nodes found by the last scan.</summary>
    public IReadOnlyList<(uint Id, string Text)> ScannedNodes => _scanned;

    /// <summary>What the last action did. Shown in the settings window.</summary>
    public string LastResult { get; private set; } = "No action yet.";

    /// <summary>Points the probe at another addon.</summary>
    public void SetAddon(string addonName)
    {
        if (string.Equals(AddonName, addonName, StringComparison.Ordinal))
            return;

        UnregisterSticky();
        AddonName = addonName;
        _scanned.Clear();
        LastResult = $"Target addon: {addonName}. Scan to list its text nodes.";

        if (Sticky)
            RegisterSticky();
    }

    /// <summary>
    /// Walks the addon's node tree and records every text node with its node id and
    /// current text. This is how we find the right node for an addon we have not
    /// looked at before.
    /// </summary>
    public bool Scan()
    {
        _scanned.Clear();

        var addon = GetAddon();
        if (addon is null)
        {
            LastResult = $"{AddonName} is not open. Open it first.";
            return false;
        }

        if (addon->RootNode is not null)
            CollectTextNodes(addon->RootNode, 0);

        LastResult = _scanned.Count == 0
            ? $"{AddonName} has no text nodes."
            : $"Found {_scanned.Count} text nodes in {AddonName}. Pick one below.";

        _log.Info("Native probe: {Result}", LastResult);
        return true;
    }

    /// <summary>Writes the sample into the selected node.</summary>
    public bool Write()
    {
        var addon = GetAddon();
        if (addon is null)
        {
            LastResult = $"{AddonName} is not open. Open it first.";
            return false;
        }

        var node = addon->GetTextNodeById(NodeId);
        if (node is null)
        {
            LastResult = $"Node {NodeId} is not a text node in {AddonName}. Scan and pick one.";
            _log.Warning("Native probe: node {NodeId} missing in {Addon}.", NodeId, AddonName);
            return false;
        }

        // Save the game's own text once, before the first write, so Restore can put it
        // back without the sample being saved over it.
        if (!_hasSaved || _savedNodeId != NodeId || _savedAddon != AddonName)
        {
            _savedText = node->NodeText.ToString();
            _savedAddon = AddonName;
            _savedNodeId = NodeId;
            _hasSaved = true;
        }

        var before = node->NodeText.ToString();

        // Set the font first: changing it can rebuild the layout, which would throw
        // away text set before it.
        node->SetFont(FontType);
        node->SetText(Sample);
        node->ResizeNodeForCurrentText();

        var after = node->NodeText.ToString();
        LastResult = after == Sample
            ? $"Wrote {Sample.Length} chars to {AddonName} node {NodeId} with font {FontType}. " +
              $"Was: \"{Truncate(before)}\"."
            : $"Write did not stick. {AddonName} node {NodeId} still reads \"{Truncate(after)}\".";

        _log.Info("Native probe: {Result}", LastResult);
        return true;
    }

    /// <summary>Puts the game's own text back into the saved node.</summary>
    public bool Restore()
    {
        if (!_hasSaved)
        {
            LastResult = "Nothing was saved, so there is nothing to restore.";
            return false;
        }

        var addon = GetAddon();
        if (addon is null)
        {
            LastResult = $"{_savedAddon} is not open. Open it to restore the text.";
            return false;
        }

        var node = addon->GetTextNodeById(_savedNodeId);
        if (node is null)
        {
            LastResult = $"Node {_savedNodeId} is gone from {_savedAddon}.";
            return false;
        }

        node->SetText(_savedText);
        node->ResizeNodeForCurrentText();

        LastResult = $"Restored {_savedText.Length} chars to {_savedAddon} node {_savedNodeId}.";
        _log.Info("Native probe: {Result}", LastResult);
        return true;
    }

    /// <summary>Forgets the saved text, so the next write saves the current text.</summary>
    public void ResetSaved()
    {
        _hasSaved = false;
        _savedText = string.Empty;
        _savedAddon = string.Empty;
        LastResult = "Saved text cleared.";
    }

    /// <summary>Draws the sample with the next font in the list and writes it.</summary>
    public void WriteWithNextFont()
    {
        var fonts = Enum.GetValues<FontType>();
        var index = Array.IndexOf(fonts, FontType);
        FontType = fonts[(index + 1) % fonts.Length];
        Write();
    }

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (!Sticky)
            return;

        _log.Debug("Native probe: sticky rewrite of {Addon} on {Event}.", AddonName, eventType);
        Write();
    }

    private void RegisterSticky()
    {
        UnregisterSticky();
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, _onAddonEvent);
        _registeredAddon = AddonName;
    }

    private void UnregisterSticky()
    {
        if (_registeredAddon.Length == 0)
            return;

        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, _registeredAddon, _onAddonEvent);
        _registeredAddon = string.Empty;
    }

    private void CollectTextNodes(AtkResNode* node, int depth)
    {
        if (depth > MaxTreeDepth)
            return;

        for (var current = node; current is not null; current = current->NextSiblingNode)
        {
            if (current->Type == NodeType.Text)
            {
                var textNode = current->GetAsAtkTextNode();
                if (textNode is not null)
                    _scanned.Add((current->NodeId, textNode->NodeText.ToString()));
            }

            if (current->ChildNode is not null)
                CollectTextNodes(current->ChildNode, depth + 1);
        }
    }

    private AtkUnitBase* GetAddon()
    {
        var addon = _gameGui.GetAddonByName<AtkUnitBase>(AddonName, 1);
        return addon is null ? null : addon;
    }

    private static string Truncate(string text)
        => text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 60), "...");

    public void Dispose() => UnregisterSticky();
}
#endif
