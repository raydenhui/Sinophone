using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Diagnostics;

/// <summary>
/// Temporary probe for native text mode. Writes Chinese into the game's own Talk
/// text nodes so we can see whether the game font has CJK glyphs.
/// </summary>
/// <remarks>
/// This is test code on a throwaway branch. It does not ship, and it keeps no
/// settings, so a plugin reload clears everything it changed.
/// </remarks>
public sealed unsafe class NativeTextProbe : IDisposable
{
    /// <summary>
    /// Traditional first, because the last run showed Traditional glyphs drawing and
    /// some Simplified ones showing "=". Keep them on separate lines so a failure is
    /// easy to place.
    /// </summary>
    public const string DefaultSample =
        "這是中文樣本：遊戲字型能否顯示漢字？\n这是中文样本：游戏字体能否显示汉字？";

    /// <summary>
    /// Sample presets for the settings tab. Each one answers a different question.
    /// </summary>
    public static readonly (string Label, string Text)[] Presets =
    [
        ("Traditional only", "這是中文樣本：遊戲字型能否顯示漢字？"),
        ("Simplified only", "这是中文样本：游戏字体能否显示汉字？"),
        ("Traditional vs simplified pairs", "這这 樣样 遊游 戲戏 體体 顯显 漢汉"),
        ("Latin control", "Latin control line ABC 123"),
    ];

    private const string TalkAddonName = "Talk";

    // Confirmed from the live addon: node 2 is the speaker name, node 3 is the dialogue.
    private const uint NameNodeId = 2;
    private const uint TextNodeId = 3;

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;

    private string _savedName = string.Empty;
    private string _savedText = string.Empty;
    private bool _hasSaved;

    public NativeTextProbe(IAddonLifecycle addonLifecycle, IGameGui gameGui, IPluginLog log)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _log = log;
        _onAddonEvent = OnAddonEvent;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, TalkAddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, TalkAddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostShow, TalkAddonName, _onAddonEvent);
    }

    /// <summary>Text written into the dialogue node.</summary>
    public string Sample { get; set; } = DefaultSample;

    /// <summary>Text written into the speaker name node.</summary>
    public string NameSample { get; set; } = "中文名字";

    /// <summary>Font the sample is drawn with. The game only offers these six.</summary>
    public FontType FontType { get; set; } = FontType.Axis;

    /// <summary>
    /// Rewrite the sample every time the game refreshes the Talk window. Needed if
    /// the game puts its own text back after a single write.
    /// </summary>
    public bool Sticky { get; set; }

    /// <summary>What the last write did. Shown in the settings window.</summary>
    public string LastResult { get; private set; } = "No write yet.";

    /// <summary>
    /// Writes the sample into the open Talk window. Returns false when Talk is not
    /// open, which is the usual case outside a conversation.
    /// </summary>
    public bool Write()
    {
        var talk = GetTalkAddon();
        if (talk is null)
        {
            LastResult = "Talk is not open. Talk to an NPC first.";
            return false;
        }

        var textNode = talk->GetTextNodeById(TextNodeId);
        if (textNode is null)
        {
            LastResult = $"Text node {TextNodeId} was not found.";
            _log.Warning("Native probe: text node {NodeId} was not found.", TextNodeId);
            return false;
        }

        var nameNode = talk->GetTextNodeById(NameNodeId);

        // Save the game's own text once, before the first write, so Restore can put
        // it back without the sample being saved over it.
        if (!_hasSaved)
        {
            _savedText = textNode->NodeText.ToString();
            _savedName = nameNode is null ? string.Empty : nameNode->NodeText.ToString();
            _hasSaved = true;
        }

        var before = textNode->NodeText.ToString();

        // Set the font first: changing it can rebuild the layout, which would
        // throw away the text set before it.
        textNode->SetFont(FontType);
        textNode->SetText(Sample);
        textNode->ResizeNodeForCurrentText();

        if (nameNode is not null)
        {
            nameNode->SetText(NameSample);
            nameNode->ResizeNodeForCurrentText();
        }

        var after = textNode->NodeText.ToString();
        LastResult = after == Sample
            ? $"Wrote {Sample.Length} chars to node {TextNodeId} with font {FontType}. " +
              $"Name node: {(nameNode is null ? "missing" : "written")}. Was: \"{Truncate(before)}\"."
            : $"Write did not stick. Node {TextNodeId} still reads \"{Truncate(after)}\".";

        _log.Info("Native probe: {Result}", LastResult);
        return true;
    }

    /// <summary>Puts the game's own text back into both nodes.</summary>
    public bool Restore()
    {
        var talk = GetTalkAddon();
        if (talk is null)
        {
            LastResult = "Talk is not open. Open a conversation, then restore.";
            return false;
        }

        if (!_hasSaved)
        {
            LastResult = "Nothing was saved, so there is nothing to restore.";
            return false;
        }

        var textNode = talk->GetTextNodeById(TextNodeId);
        if (textNode is null)
        {
            LastResult = $"Text node {TextNodeId} was not found.";
            return false;
        }

        textNode->SetText(_savedText);
        textNode->ResizeNodeForCurrentText();

        var nameNode = talk->GetTextNodeById(NameNodeId);
        if (nameNode is not null)
        {
            nameNode->SetText(_savedName);
            nameNode->ResizeNodeForCurrentText();
        }

        LastResult = $"Restored the saved text ({_savedText.Length} chars).";
        _log.Info("Native probe: {Result}", LastResult);
        return true;
    }

    /// <summary>Forgets the saved text, so the next write saves the current text.</summary>
    public void ResetSaved()
    {
        _hasSaved = false;
        _savedText = string.Empty;
        _savedName = string.Empty;
        LastResult = "Saved text cleared.";
    }

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (!Sticky)
            return;

        // The game rewrites the nodes from AtkValues on every refresh, so the sample
        // has to go back in after the game is done with them.
        _log.Debug("Native probe: sticky rewrite on {Event}.", eventType);
        Write();
    }

    private AddonTalk* GetTalkAddon()
    {
        var addon = _gameGui.GetAddonByName<AtkUnitBase>(TalkAddonName, 1);
        return addon is null ? null : (AddonTalk*)addon;
    }

    private static string Truncate(string text)
        => text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 60), "...");

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, TalkAddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, TalkAddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostShow, TalkAddonName, _onAddonEvent);
    }
}
