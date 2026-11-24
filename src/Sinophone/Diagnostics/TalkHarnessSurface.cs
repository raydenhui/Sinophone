#if SINOPHONE_TESTING
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Sinophone.Diagnostics;

/// <summary>
/// Reads the <c>Talk</c> window for a harness run, and injects a line when a run asks for one.
/// </summary>
/// <remarks>
/// Reading is the same two steps the handler uses: <c>AtkTextNode228</c> is the dialogue node,
/// and the English the game was given comes from <c>AtkValue[0]</c> while the refresh is in
/// flight. An injected line has no live event handler, so a line written this way tests the
/// text fallback and the display, not the id-first lookup.
/// </remarks>
public sealed unsafe class TalkHarnessSurface
{
    private const string TalkAddonName = "Talk";

    private readonly IGameGui _gameGui;
    private readonly PluginLog _log;

    /// <summary>The English line the last injected case asked for.</summary>
    private string? _injectedEnglish;

    public TalkHarnessSurface(IGameGui gameGui, PluginLog log)
    {
        _gameGui = gameGui;
        _log = log;
    }

    /// <summary>True when the Talk window is open and ready.</summary>
    public bool IsOpen
    {
        get
        {
            var addon = GetAddon();
            return addon is not null && addon->IsVisible && addon->IsReady;
        }
    }

    /// <summary>
    /// The current line, or null when Talk is not open. The English half is the injected line
    /// for an injected case, and the text node's own English for a line the developer played.
    /// </summary>
    public HarnessDialogSnapshot? Snapshot(string? expectedEnglish)
    {
        var addon = GetAddon();
        if (addon is null || !addon->IsVisible)
            return null;

        var node = addon->AtkTextNode228;
        if (node is null)
            return null;

        var text = node->NodeText.ToString();
        var english = _injectedEnglish ?? expectedEnglish ?? string.Empty;

        var textWidth = 0;
        var textHeight = 0;
        try
        {
            ushort width = 0;
            ushort height = 0;
            node->GetTextDrawSize(&width, &height);
            textWidth = width;
            textHeight = height;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Testing tab: could not measure the Talk text node.");
        }

        return new HarnessDialogSnapshot(
            text,
            english,
            HarnessAssertions.ExplicitLines(text),
            textWidth > 0 ? textWidth : node->AtkResNode.Width,
            textHeight > 0 ? textHeight : node->AtkResNode.Height,
            node->AtkResNode.Width,
            node->AtkResNode.Height,
            GlyphProbe.CountMissing(text, node->FontType));
    }

    /// <summary>
    /// Pushes <paramref name="english"/> into the addon as the next line. Returns false when
    /// Talk is not open.
    /// </summary>
    public bool Inject(string english)
    {
        var addon = GetAddon();
        if (addon is null || !addon->IsReady)
            return false;

        try
        {
            var values = stackalloc AtkValue[2];
            values[0].SetManagedString(english);
            values[1].SetManagedString(string.Empty);

            addon->OnRefresh(2, values);
            _injectedEnglish = english;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Testing tab: could not inject a Talk line.");
            return false;
        }
    }

    /// <summary>Forgets the injected line, so a later line is read as it was played.</summary>
    public void ClearInjected() => _injectedEnglish = null;

    private AddonTalk* GetAddon()
    {
        try
        {
            var addon = _gameGui.GetAddonByName<AtkUnitBase>(TalkAddonName, 1);
            return addon is null ? null : (AddonTalk*)addon;
        }
        catch
        {
            return null;
        }
    }
}
#endif
