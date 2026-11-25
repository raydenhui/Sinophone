using System.Runtime.InteropServices;

using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using InteropGenerator.Runtime;

using Sinophone.Diagnostics;

namespace Sinophone.Handlers;

/// <summary>
/// Watches the <c>Talk</c> and <c>TalkSubtitle</c> addons, looks up the Chinese
/// translation for the English text in their AtkValues, and feeds it to the overlay.
/// </summary>
public sealed class TalkHandler : IDisposable
{
    private const string TalkAddonName = "Talk";
    private const string TalkSubtitleAddonName = "TalkSubtitle";

    private const uint TextValueIndex = 0;
    private const uint NameValueIndex = 1;

    /// <summary>
    /// The first text node <c>TalkSubtitle</c> draws its line in. The band carries three,
    /// and the layout log reads all three.
    /// </summary>
    private const uint SubtitleFirstTextNodeId = 2;

    /// <summary>How many handed-out string buffers stay alive, in case the addon keeps a pointer.</summary>
    private const int ValueBufferHistory = 8;

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameInteropProvider _interop;
    private readonly IGameGui _gameGui;
    private readonly PluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly OverlayWindow _overlay;
    private readonly NativeTextWriter _nativeWriter;
    private readonly ChatLogRestorer _chatLogRestorer;
    private readonly TalkEventReader _eventReader;
    private readonly ITargetManager _targetManager;
    private readonly FailureRecorder? _failures;

    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;
    private bool _layoutLogPending;
    private bool _subtitleLogPending;

    /// <summary>
    /// The value hook for each addon. <c>Talk</c> and <c>TalkSubtitle</c> are separate vtable
    /// entries, so each addon gets its own pair, installed when its addon is first up.
    /// </summary>
    private readonly Dictionary<string, AddonValueHooks> _valueHooks = new(StringComparer.Ordinal);

    /// <summary>Addons whose vtable could not be hooked, so no second attempt is made.</summary>
    private readonly HashSet<string> _valueHookUnavailable = new(StringComparer.Ordinal);

    /// <summary>
    /// The line each addon was last laid out with, per addon. A later event reads the values
    /// again and uses this to know the hook already handled that line: <c>Talk</c> so the
    /// Chinese in a shared value is not written twice, <c>TalkSubtitle</c> so the overlay
    /// stays hidden, because that path writes values rather than text nodes.
    /// </summary>
    private readonly Dictionary<string, LaidOutLine> _laidOutLines = new(StringComparer.Ordinal);

    /// <summary>
    /// The cutscene scene learned from the lines of the open addon. A cutscene task is not
    /// always reachable while its lines are on screen, but every line in one scene names the
    /// same scene, so a unique line can qualify the ambiguous ones that follow.
    /// </summary>
    private readonly Dictionary<string, uint> _dialogScenes = new(StringComparer.Ordinal);

    /// <summary>
    /// The unmanaged UTF-8 buffers handed to the game as <c>AtkValue</c> strings. The addon copies
    /// the text, but a buffer is kept for a few lines in case it keeps the pointer, so a stale
    /// pointer never points at freed memory.
    /// </summary>
    private readonly Queue<nint> _valueBuffers = new();
    private bool _disposed;

    private unsafe delegate void AddonSetupDelegate(AtkUnitBase* addon, uint valueCount, AtkValue* values);

    private unsafe delegate bool AddonRefreshDelegate(AtkUnitBase* addon, uint valueCount, AtkValue* values);

    public TalkHandler(
        IAddonLifecycle addonLifecycle,
        IPluginLog log,
        IGameInteropProvider interop,
        IGameGui gameGui,
        ITargetManager targetManager,
        IDataManager dataManager,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        OverlayWindow overlay,
        NativeTextWriter nativeWriter,
        ChatLogRestorer chatLogRestorer,
        FailureRecorder? failures = null)
    {
        _addonLifecycle = addonLifecycle;
        _interop = interop;
        _gameGui = gameGui;
        _log = new PluginLog(log, configuration, LogCategory.Dialogue);
        _targetManager = targetManager;
        _configuration = configuration;
        _getStore = getStore;
        _overlay = overlay;
        _nativeWriter = nativeWriter;
        _chatLogRestorer = chatLogRestorer;
        _failures = failures;
        _eventReader = new TalkEventReader(log, configuration, LogCategory.Dialogue, dataManager);
        _onAddonEvent = OnAddonEvent;

        Register(TalkAddonName);
        Register(TalkSubtitleAddonName);
    }

    private void Register(string addonName)
    {
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);

        // Talk puts the Chinese and its style back after the game update and again before
        // drawing, because the text stream can replace both during the update. The subtitle
        // addon uses the same events for the one-time layout log a run reads.
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, addonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
    }

    private void Unregister(string addonName)
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostClose, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, addonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, addonName, _onAddonEvent);
    }

    /// <summary>
    /// Hooks one addon's own setup and refresh so the Chinese line goes into the
    /// <c>AtkValue</c> strings before the game lays the window out. The game then
    /// measures and sizes the dialogue box or subtitle band for the translation, which is
    /// the only way a long line stays inside it. Hooking the text node after layout only
    /// resizes the node and leaves the box at the English size. Falls back to the overlay
    /// when the addon is not up yet or the hook cannot be placed.
    /// </summary>
    /// <remarks>
    /// <c>Talk</c> and <c>TalkSubtitle</c> have separate vtable entries, so each addon gets
    /// its own hook pair, installed when that addon is first up.
    /// </remarks>
    private unsafe void EnsureValueHook(string addonName, AddonArgs args)
    {
        if (_valueHooks.ContainsKey(addonName) || _valueHookUnavailable.Contains(addonName))
            return;

        Hook<AddonRefreshDelegate>? refreshHook = null;
        Hook<AddonSetupDelegate>? setupHook = null;

        try
        {
            // The lifecycle args carry the addon for most events, but not all of them; the
            // game object keeps the same addon and is always reachable by name.
            var addon = args.GetAddonPointer<AtkUnitBase>();
            if (addon is null)
                addon = _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);

            var table = addon is null ? null : addon->VirtualTable;
            if (table is null)
            {
                _log.Debug("{Addon} layout hook: the addon is not reachable yet; will try again.", addonName);
                return;
            }

            var refreshAddress = (nint)table->OnRefresh;
            var setupAddress = (nint)table->OnSetup;
            if (refreshAddress == 0 || setupAddress == 0)
            {
                _valueHookUnavailable.Add(addonName);
                _log.Debug("{Addon} layout hook: the addon exposed no refresh address; using the overlay.", addonName);
                return;
            }

            // The hook belongs to one addon, so its callback carries the name: a vtable entry
            // is shared by every instance of that addon class.
            var name = addonName;
            refreshHook = _interop.HookFromAddress<AddonRefreshDelegate>(
                refreshAddress,
                (pointer, valueCount, values) => OnAddonRefresh(name, pointer, valueCount, values));
            setupHook = _interop.HookFromAddress<AddonSetupDelegate>(
                setupAddress,
                (pointer, valueCount, values) => OnAddonSetup(name, pointer, valueCount, values));
            refreshHook.Enable();
            setupHook.Enable();
            _valueHooks[name] = new AddonValueHooks(refreshHook, setupHook);
            _log.Debug("{Addon} layout hook: replaced the AtkValue text before layout at {Address:X}.",
                name, refreshAddress);
        }
        catch (Exception ex)
        {
            refreshHook?.Dispose();
            setupHook?.Dispose();
            _valueHookUnavailable.Add(addonName);
            _log.Warning(ex, "{Addon} layout hook: could not hook the addon; using the overlay.", addonName);
        }
    }

    private unsafe void OnAddonSetup(string addonName, AtkUnitBase* addon, uint valueCount, AtkValue* values)
    {
        var swapped = TryLayOutTranslatedValues(addonName, addon, values, valueCount);

        try
        {
            if (_valueHooks.TryGetValue(addonName, out var hooks))
                hooks.Setup.Original(addon, valueCount, values);
        }
        finally
        {
            // The addon has copied the line by now. Put the caller's own values back, so
            // everything that reads them after this call - the chat log's Story tab and any other
            // plugin that reads the caller's data - sees the English line.
            RestoreValues(values, swapped);
        }
    }

    private unsafe bool OnAddonRefresh(string addonName, AtkUnitBase* addon, uint valueCount, AtkValue* values)
    {
        var swapped = TryLayOutTranslatedValues(addonName, addon, values, valueCount);

        try
        {
            return _valueHooks.TryGetValue(addonName, out var hooks)
                   && hooks.Refresh.Original(addon, valueCount, values);
        }
        finally
        {
            RestoreValues(values, swapped);
        }
    }

    /// <summary>The two hooks one addon's vtable exposes.</summary>
    private sealed class AddonValueHooks(Hook<AddonRefreshDelegate> refresh, Hook<AddonSetupDelegate> setup)
    {
        public Hook<AddonRefreshDelegate> Refresh { get; } = refresh;

        public Hook<AddonSetupDelegate> Setup { get; } = setup;

        public void Dispose()
        {
            Refresh.Dispose();
            Setup.Dispose();
        }
    }

    /// <summary>The caller's values, saved before the Chinese was put in.</summary>
    private readonly record struct SwappedValues(bool Swapped, AtkValue Text, AtkValue Name, bool HasName);

    /// <summary>One line an addon was laid out with: the English it read and the Chinese drawn.</summary>
    private readonly record struct LaidOutLine(string English, string Chinese);

    /// <summary>Puts the caller's own values back once the game has read the Chinese.</summary>
    private unsafe void RestoreValues(AtkValue* values, SwappedValues swapped)
    {
        if (!swapped.Swapped || values is null)
            return;

        values[TextValueIndex] = swapped.Text;

        if (swapped.HasName)
            values[NameValueIndex] = swapped.Name;

        _log.Debug(
            "Layout hook: put the caller's values back; the game passes {Type}.",
            swapped.Text.Type);
    }

    /// <summary>
    /// The detours run inside the game's own call, so a failure must leave the English
    /// text in place rather than throw through the game.
    /// </summary>
    private unsafe SwappedValues TryLayOutTranslatedValues(
        string addonName,
        AtkUnitBase* addon,
        AtkValue* values,
        uint count)
    {
        try
        {
            return LayOutTranslatedValues(addonName, addon, values, count);
        }
        catch (Exception ex)
        {
            _laidOutLines.Remove(addonName);
            _log.Warning(ex, "{Addon} layout hook: could not translate the line; leaving the game text.",
                addonName);
            return default;
        }
    }

    /// <summary>
    /// Replaces the English strings in the addon's values with the translation. Runs
    /// once per line, before the game reads the values, so the game's own layout and
    /// wrapping apply to the Chinese. A line with no translation is left alone.
    /// </summary>
    private unsafe SwappedValues LayOutTranslatedValues(
        string addonName,
        AtkUnitBase* addon,
        AtkValue* values,
        uint count)
    {
        _laidOutLines.Remove(addonName);

        if (_disposed || values is null || count == 0)
            return default;

        var isSubtitle = IsSubtitle(addonName);
        var shown = isSubtitle ? _configuration.ShowInTalkSubtitle : _configuration.ShowInTalk;
        if (!_configuration.Enabled || !_configuration.NativeTextMode || !shown)
            return default;

        var store = _getStore();
        if (store is null)
            return default;

        var englishText = ReadStringValue(values, TextValueIndex, count);
        if (string.IsNullOrWhiteSpace(englishText))
            return default;

        // A subtitle has no speaker name. Reading index 1 there would either be empty or
        // another value that is not a name.
        var englishName = isSubtitle ? null : ReadStringValue(values, NameValueIndex, count);

        // A cutscene subtitle is a VOICEMAN script line, and the live cutscene task names the
        // scene while it plays, so a subtitle reads the same context a Talk line does.
        var context = WithRememberedScene(
            store,
            addonName,
            englishText,
            _eventReader.ReadContext(englishText, _targetManager.Target?.Address ?? 0, scanAllLuaTexts: true));

        var trace = BeginTrace(addonName, englishText, englishName);
        trace?.Input("path", "native");
        DescribeContext(trace, context);

        var content = Resolve(
            store,
            _configuration,
            englishText,
            englishName,
            context,
            ReadIntegerParameters(values, count),
            trace);

        // A line with no translation leaves the English in place. Writing an empty
        // string would blank the dialogue box.
        if (content is null || string.IsNullOrWhiteSpace(content.Value.ChineseText))
        {
            trace?.Step("display.native", "no-text");
            trace?.Finish(FailureOutcome.Miss, "no-translation");
            return default;
        }

        trace?.Step("display.native", "written");
        trace?.Finish(FailureOutcome.Hit);

        // The subtitle band is one line the game scales, never a wrapped box: the addon's own
        // width and scale decide the fit. The dialogue box is wrapped because its game-side
        // breaks move a closing mark onto the next rendered line.
        var chineseText = isSubtitle
            ? content.Value.ChineseText
            : WrapForTalkBox((AddonTalk*)addon, content.Value.ChineseText);
        var chineseName = isSubtitle ? string.Empty : content.Value.ChineseName;
        var hasName = !isSubtitle && NameValueIndex < count && !string.IsNullOrWhiteSpace(chineseName);

        // The shares setting is about the Story tab, which the game fills from the Talk addon.
        // A subtitle always gets the caller's values back, so nothing downstream sees Chinese.
        if (!isSubtitle && SharesTranslation(_configuration))
        {
            // The caller owns these values and frees them, so hand it managed strings and leave
            // them in place: the game's log and anything else that reads the line see Chinese.
            values[TextValueIndex].SetManagedString(chineseText);
            if (hasName)
                values[NameValueIndex].SetManagedString(chineseName);

            _laidOutLines[addonName] = new LaidOutLine(englishText, chineseText);
            _layoutLogPending = !isSubtitle;
            _subtitleLogPending = isSubtitle;
            ForgetNativeText(addonName);
            _overlay.Hide();

            _log.Debug(
                "{Addon} layout hook: supplied the Chinese line and left it in place for other " +
                "readers ({Length} chars).",
                addonName, chineseText.Length);

            return default;
        }

        // Save the caller's values before replacing them, so they can be put back after the game
        // has copied the Chinese.
        var swapped = new SwappedValues(true, values[TextValueIndex], hasName ? values[NameValueIndex] : default, hasName);

        // A plain String value is caller-owned, so the game copies it and never frees it. The
        // buffer stays alive for a few lines in case the addon keeps the pointer anyway.
        values[TextValueIndex].Type = AtkValueType.String;
        values[TextValueIndex].String = new CStringPointer((byte*)AllocateValueBuffer(chineseText));

        if (hasName)
        {
            values[NameValueIndex].Type = AtkValueType.String;
            values[NameValueIndex].String = new CStringPointer((byte*)AllocateValueBuffer(chineseName));
        }

        // The game logs NPC dialogue into the Story tab from the text it was given. Remember both
        // forms and the name, so the log message can be put back to English. A subtitle is
        // remembered too: whether the game logs one is not confirmed yet, and a remembered line
        // the log never shows costs only a slot.
        _chatLogRestorer.Remember(chineseText, englishText);
        _chatLogRestorer.Remember(content.Value.ChineseText, englishText);

        if (hasName && !string.IsNullOrWhiteSpace(englishName))
            _chatLogRestorer.Remember(chineseName, englishName);

        _laidOutLines[addonName] = new LaidOutLine(englishText, chineseText);
        _layoutLogPending = !isSubtitle;
        _subtitleLogPending = isSubtitle;

        // The game is about to draw this line itself, so the overlay and any text node
        // this writer changed for the previous line have to go.
        ForgetNativeText(addonName);
        _overlay.Hide();

        _log.Debug(
            "{Addon} layout hook: supplied the Chinese line in the AtkValues before layout " +
            "(name {NameLength} chars, text {Length} chars, first line: {FirstLine}).",
            addonName, chineseName.Length, chineseText.Length,
            FirstLineOf(chineseText));

        return swapped;
    }

    /// <summary>
    /// Copies one string into unmanaged UTF-8 memory and keeps it for the last few lines. Returns
    /// the pointer the <c>AtkValue</c> points at.
    /// </summary>
    private nint AllocateValueBuffer(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var pointer = Marshal.AllocHGlobal(bytes.Length + 1);

        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        Marshal.WriteByte(pointer, bytes.Length, 0);

        _valueBuffers.Enqueue(pointer);

        while (_valueBuffers.Count > ValueBufferHistory)
            Marshal.FreeHGlobal(_valueBuffers.Dequeue());

        return pointer;
    }

    /// <summary>
    /// True when the Chinese is left in the caller's values, so the game's log and other readers
    /// see the translation instead of the English line.
    /// </summary>
    internal static bool SharesTranslation(Configuration configuration)
        => configuration.NativeTextMode && configuration.NativeTextSharesTranslation;

    /// <summary>
    /// Wraps the Chinese line to the width of the Talk text area before the game lays it out.
    /// </summary>
    /// <remarks>
    /// The Chinese data is broken for the Chinese client's box. With the CJK font the rendered
    /// glyphs are a little wider, so the game breaks the last character of a data line onto the
    /// next rendered line, and a closing mark such as the full stop then starts a line.
    /// <see cref="CjkLineWrapper"/> wraps the line to the box width first, keeping closing marks
    /// off a line start, so the game has nothing left to break. A box that is not laid out yet
    /// leaves the line as it is.
    /// </remarks>
    private unsafe string WrapForTalkBox(AddonTalk* addon, string chineseText)
    {
        if (addon is null || chineseText.Length == 0)
            return chineseText;

        var node = addon->AtkTextNode228;
        if (node is null)
            return chineseText;

        var width = node->AtkResNode.Width;
        if (width < 120)
            width = node->Width;

        if (width < 120)
            return chineseText;

        var address = (nint)node;

        int Measure(string candidate)
        {
            var bytes = Encoding.UTF8.GetBytes(candidate);
            ushort measuredWidth = 0;
            ushort measuredHeight = 0;

            fixed (byte* pointer = bytes)
                ((AtkTextNode*)address)->GetTextDrawSize(&measuredWidth, &measuredHeight, pointer);

            return measuredWidth;
        }

        // Leave one glyph of slack. Our measurement and the game's layout can differ by a pixel or
        // two at the line end, and a line that measured to the exact width is the one the game
        // breaks again, which moves a closing mark onto the next line.
        var wrapWidth = Math.Max(120, width - node->FontSize);

        var wrapped = CjkLineWrapper.Wrap(chineseText, wrapWidth, Measure);
        if (!string.Equals(wrapped, chineseText, StringComparison.Ordinal))
        {
            _log.Debug("Talk layout hook: wrapped the line to {Width}px into {Lines} lines.",
                wrapWidth, wrapped.Count('\n') + 1);
        }

        return wrapped;
    }

    /// <summary>
    /// The first line of a line, shortened for a log. An empty result means the text
    /// starts with a line break, which draws as a blank first line.
    /// </summary>
    internal static string FirstLineOf(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "(empty)";

        var end = text.IndexOfAny(['\r', '\n']);
        var line = end < 0 ? text : text[..end];

        if (line.Length == 0)
            return "(blank line first)";

        return line.Length > 24 ? string.Concat(line.AsSpan(0, 24), "...") : line;
    }

    /// <summary>
    /// Logs the size of the name and text nodes next to the size of the dialogue box,
    /// once for the line that was just laid out. A box much taller than its text leaves
    /// blank space above the line.
    /// </summary>
    private unsafe void LogTalkLayout(AddonArgs args)
    {
        _layoutLogPending = false;

        if (!_configuration.IsLogEnabled(LogCategory.NativeText))
            return;

        try
        {
            var addon = args.GetAddonPointer<AtkUnitBase>();
            if (addon is null)
                return;

            var talk = (AddonTalk*)addon;
            _log.Debug(
                "Talk layout: name {NameHeight}px, text {TextHeight}px, text background {BackgroundHeight}px, " +
                "window {WindowHeight}px, root {RootHeight}px.",
                talk->AtkTextNode220 is null ? 0 : talk->AtkTextNode220->Height,
                talk->AtkTextNode228 is null ? 0 : talk->AtkTextNode228->Height,
                talk->AtkResNode230 is null ? 0 : talk->AtkResNode230->Height,
                addon->WindowNode is null ? 0 : addon->WindowNode->AtkResNode.Height,
                addon->RootNode is null ? 0 : addon->RootNode->Height);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Talk layout: could not read the addon sizes.");
        }
    }

    /// <summary>
    /// Logs the subtitle band once per line, after the game laid it out. The figures answer
    /// what the native path depends on: whether the band grows for a longer line or scales
    /// the text down, and where the addon puts the line.
    /// </summary>
    private unsafe void LogSubtitleLayout(AddonArgs args)
    {
        if (!_configuration.IsLogEnabled(LogCategory.NativeText))
            return;

        try
        {
            var addon = args.GetAddonPointer<AtkUnitBase>();
            if (addon is null)
                return;

            var subtitle = (AddonTalkSubtitle*)addon;
            _log.Debug(
                "Subtitle layout: max width {MaxWidth}px, scale {Scale}, band {BandWidth}x{BandHeight}, " +
                "text nodes {First}, {Second}, {Third}.",
                subtitle->SubtitleMaxWidth,
                subtitle->SubtitleScale,
                addon->WindowNode is null ? 0 : addon->WindowNode->AtkResNode.Width,
                addon->WindowNode is null ? 0 : addon->WindowNode->AtkResNode.Height,
                DescribeTextNode(addon, SubtitleFirstTextNodeId),
                DescribeTextNode(addon, SubtitleFirstTextNodeId + 1),
                DescribeTextNode(addon, SubtitleFirstTextNodeId + 2));
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Subtitle layout: could not read the addon sizes.");
        }
    }

    private static unsafe string DescribeTextNode(AtkUnitBase* addon, uint nodeId)
    {
        var node = addon->GetTextNodeById(nodeId);
        return node is null
            ? $"{nodeId}: none"
            : $"{nodeId}: {node->Width}x{node->Height}, {node->NodeText.ToString().Length} chars";
    }

    /// <summary>
    /// True when the Chinese in the addon's values came from the layout hook, so the
    /// line is already translated and must not be looked up or written again.
    /// </summary>
    internal static bool IsLaidOutByHook(string? englishText, string? laidOutChineseText)
        => !string.IsNullOrEmpty(laidOutChineseText)
           && string.Equals(englishText, laidOutChineseText, StringComparison.Ordinal);

    private unsafe void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        var addonName = args.AddonName;

        if (eventType == AddonEvent.PostSetup
            || eventType is AddonEvent.PostClose or AddonEvent.PreFinalize)
        {
            _dialogScenes.Remove(addonName);
        }

        // Install only while the addon is alive. A closing addon can already be freed,
        // and its vtable is not safe to read then.
        if (eventType is AddonEvent.PostSetup or AddonEvent.PostRefresh
            or AddonEvent.PostUpdate or AddonEvent.PreDraw)
        {
            EnsureValueHook(addonName, args);
        }

        if (eventType is AddonEvent.PostUpdate or AddonEvent.PreDraw)
        {
            ReapplyNativeText(addonName);

            // Once per line, after the game has laid the window out: the numbers say
            // whether the box is taller than the text, which draws as a blank line at
            // the top because the box grows upward.
            if (_layoutLogPending && eventType is AddonEvent.PreDraw
                && IsTalk(addonName))
            {
                LogTalkLayout(args);
            }

            if (_subtitleLogPending && eventType is AddonEvent.PreDraw && IsSubtitle(addonName))
            {
                _subtitleLogPending = false;
                LogSubtitleLayout(args);
            }

            return;
        }

        if (ShouldHideOverlay(eventType))
        {
            _laidOutLines.Remove(addonName);
            _subtitleLogPending = false;
            _overlay.Hide();
            _log.Debug("{Addon} {Event}; overlay hidden.", addonName, eventType);
            return;
        }

        _log.Debug("{Event} fired for {Addon} ({ArgType}).",
            eventType, addonName, args.GetType().Name);

        if (!_configuration.Enabled)
        {
            _nativeWriter.RestoreAll();
            _overlay.Hide();
            _log.Debug("Plugin disabled in settings; overlay hidden.");
            return;
        }

        var isSubtitle = IsSubtitle(addonName);

        if (isSubtitle && !_configuration.ShowInTalkSubtitle)
        {
            _log.Debug("{Addon} overlay disabled in settings.", addonName);
            return;
        }

        if (!isSubtitle && !_configuration.ShowInTalk)
        {
            _log.Debug("{Addon} overlay disabled in settings.", addonName);
            return;
        }

        var store = _getStore();
        if (store is null)
        {
            ForgetNativeText(addonName);
            _log.Debug("No translation data loaded; overlay not shown.");
            return;
        }

        if (!TryGetAtkValues(args, out var values, out var count))
        {
            _log.Debug("{Event} for {Addon} carried no usable AtkValues ({ArgType}).",
                eventType, addonName, args.GetType().Name);
            return;
        }

        _log.Debug("{Addon} supplied {Count} AtkValues.", addonName, count);
        LogAtkValues(addonName, values, count);
        var integerParameters = ReadIntegerParameters(values, count);
        if (integerParameters.Count > 0)
            _log.Debug("{Addon} integer parameters: {Parameters}.", addonName, string.Join(", ", integerParameters));

        var englishText = ReadStringValue(values, TextValueIndex, count);
        // Read the name even when it is not shown: a line that several script lines
        // share is matched by its speaker, such as three different "You bastard!".
        // A subtitle has no name value.
        var englishName = isSubtitle ? null : ReadStringValue(values, NameValueIndex, count);

        _log.Debug("{Event} for {Addon}: {TextLength} text chars, name: {HasName}, text: {Text}",
            eventType, addonName, englishText?.Length ?? 0, !string.IsNullOrWhiteSpace(englishName),
            englishText ?? "(null)");

        if (IsLaidOutByHook(englishText, LaidOut(addonName)?.Chinese))
        {
            ForgetNativeText(addonName);
            _overlay.Hide();
            _log.Debug("{Addon} line was laid out from the AtkValue text; no text node write needed.",
                addonName);
            return;
        }

        var context = WithRememberedScene(
            store,
            addonName,
            englishText,
            _eventReader.ReadContext(englishText, _targetManager.Target?.Address ?? 0, scanAllLuaTexts: true));

        var trace = BeginTrace(addonName, englishText, englishName);
        trace?.Input("path", "overlay");
        DescribeContext(trace, context);

        var content = Resolve(store, _configuration, englishText, englishName, context, integerParameters, trace);

        if (content is null && IsTalk(addonName))
        {
            foreach (var candidate in GetTextNodeCandidates(args, englishName))
            {
                _log.Debug("{Addon} AtkValue text missed; retrying with addon text nodes: {Text}",
                    addonName, candidate);
                var retryContext = WithRememberedScene(
                    store,
                    addonName,
                    candidate,
                    _eventReader.ReadContext(candidate, _targetManager.Target?.Address ?? 0, scanAllLuaTexts: true));

                if (context.CutsceneId != 0 && retryContext.CutsceneId == 0)
                    retryContext = retryContext with { CutsceneId = context.CutsceneId };

                content = Resolve(
                    store,
                    _configuration,
                    candidate,
                    englishName,
                    retryContext,
                    integerParameters,
                    trace);
                if (content is not null)
                {
                    trace?.Step("talk.retry", "hit", candidate);
                    englishText = candidate;
                    break;
                }

                trace?.Step("talk.retry", "miss", candidate);
            }
        }

        if (content is null)
        {
            trace?.Finish(FailureOutcome.Miss, "no-translation");
            ForgetNativeText(addonName);
            _overlay.Hide();
            _log.Debug("{Addon} line has no usable translation; overlay hidden. " +
                       "Text empty: {TextEmpty}, hide when no translation: {HideWhenMissing}.",
                addonName, string.IsNullOrWhiteSpace(englishText), _configuration.HideWhenNoTranslation);
            return;
        }

        if (content.Value.ChineseText.Length == 0)
            trace?.Finish(FailureOutcome.Miss, "no-translation");
        else
            trace?.Finish(FailureOutcome.Hit);

        if (ShouldWriteNativeText(_configuration, addonName, IsValueHookInstalled(addonName)))
        {
            if (isSubtitle)
            {
                // The value hook laid the Chinese out and the game sized the band for it, so
                // there is no text node to write: the addon drew from the values. The line
                // resolved here again tells us the hook handled this same line.
                if (LaidOut(addonName) is { } laidOut
                    && string.Equals(laidOut.English, englishText, StringComparison.Ordinal))
                {
                    _overlay.Hide();
                    _log.Debug("{Addon} line was laid out from the AtkValues; overlay hidden.", addonName);
                    return;
                }
            }
            else if (WriteNativeText(content.Value))
            {
                _overlay.Hide();
                _log.Debug("Native text written for {Addon}; overlay hidden.",
                    addonName);
                return;
            }
            else
            {
                // The write failed, so the overlay is the surface for this line. Keep the
                // previous line's Chinese out of the game's box.
                ForgetNativeText(addonName);
            }
        }

        _overlay.SetContent(content.Value.ChineseText, englishText!, content.Value.ChineseName, isSubtitle);
        _log.Debug("Overlay shown for {Addon}: {ChineseLength} Chinese chars.",
            addonName, content.Value.ChineseText.Length);
    }

    private static bool IsTalk(string addonName)
        => string.Equals(addonName, TalkAddonName, StringComparison.Ordinal);

    private static bool IsSubtitle(string addonName)
        => string.Equals(addonName, TalkSubtitleAddonName, StringComparison.Ordinal);

    private bool IsValueHookInstalled(string addonName) => _valueHooks.ContainsKey(addonName);

    private LaidOutLine? LaidOut(string addonName)
        => _laidOutLines.TryGetValue(addonName, out var laidOut) ? laidOut : null;

    /// <summary>
    /// True when the translation should go into the game's own window. Native mode covers
    /// <c>Talk</c> and <c>TalkSubtitle</c>. A failed write falls back to the overlay.
    /// </summary>
    internal static bool ShouldUseNativeText(Configuration configuration, string addonName)
        => configuration.NativeTextMode && (IsTalk(addonName) || IsSubtitle(addonName));

    /// <summary>
    /// True when the addon's Chinese can be drawn natively. A subtitle is laid out from its
    /// <c>AtkValues</c>, so it needs that hook; without it the overlay is the only surface.
    /// </summary>
    internal static bool ShouldWriteNativeText(
        Configuration configuration,
        string addonName,
        bool valuesHooked)
        => ShouldUseNativeText(configuration, addonName)
           && (!IsSubtitle(addonName) || valuesHooked);

    /// <summary>
    /// True when the Chinese written into the game's dialogue box has to be put back on
    /// each draw. Talk only: a subtitle is drawn from its values, so nothing is written into
    /// its nodes to reapply. This runs every frame while the addon is visible, so it stays a
    /// plain setting check; the draw-time compare lives in <see cref="NativeTextWriter.Reapply"/>.
    /// </summary>
    internal static bool ShouldReapplyNativeText(Configuration configuration, string addonName)
        => configuration.Enabled && IsTalk(addonName) && ShouldUseNativeText(configuration, addonName);

    private void ReapplyNativeText(string addonName)
    {
        if (!ShouldReapplyNativeText(_configuration, addonName))
            return;

        // No-ops for a node this writer never changed, so a line on the overlay is
        // left alone.
        _nativeWriter.Reapply(addonName, NativeTextWriter.TalkNameNodeId);
        _nativeWriter.Reapply(addonName, NativeTextWriter.TalkTextNodeId);
    }

    /// <summary>
    /// Stops keeping a line's Chinese on screen. The game's own English line then stays
    /// in place, which is what an untranslated line needs. A subtitle is left alone: it is
    /// drawn from values that were already put back, so it has no saved node text.
    /// </summary>
    private void ForgetNativeText(string addonName)
    {
        if (!IsTalk(addonName))
            return;

        _nativeWriter.ClearIfStillOurs(addonName, NativeTextWriter.TalkNameNodeId);
        _nativeWriter.ClearIfStillOurs(addonName, NativeTextWriter.TalkTextNodeId);
    }

    /// <summary>
    /// Writes the Chinese line, and the Chinese speaker name when the name is shown, into
    /// the Talk addon's text nodes. Returns true when the dialogue line was written.
    /// </summary>
    private bool WriteNativeText(OverlayContent content)
    {
        var wroteName = !string.IsNullOrWhiteSpace(content.ChineseName)
            ? _nativeWriter.WriteFitted(TalkAddonName, NativeTextWriter.TalkNameNodeId, content.ChineseName)
            : ClearName();

        var wroteLine = _nativeWriter.WriteFitted(
            TalkAddonName, NativeTextWriter.TalkTextNodeId, content.ChineseText, content.Style);

        // The overlay is the surface when the line cannot fit, so the name must not stay
        // in the game's box next to it.
        if (!wroteLine && wroteName)
            _nativeWriter.Restore(TalkAddonName, NativeTextWriter.TalkNameNodeId);

        return wroteLine;
    }

    /// <summary>
    /// Empties the speaker name node when the line has no Chinese name, so the
    /// previous line's name does not stay above the new line.
    /// </summary>
    private bool ClearName()
    {
        _nativeWriter.ClearIfStillOurs(TalkAddonName, NativeTextWriter.TalkNameNodeId);
        return false;
    }

    /// <summary>Puts the game's own text back into every node native mode changed.</summary>
    public void RestoreNativeText()
    {
        _nativeWriter.RestoreAll();
        _laidOutLines.Clear();
    }

    /// <summary>
    /// Qualifies a line with the cutscene scene. The live task wins when it is readable. Otherwise
    /// a line that names exactly one scene teaches the scene, and a later line that several scenes
    /// share uses it. The memory lasts for one open addon and is cleared at setup and close.
    /// </summary>
    private DialogueContext WithRememberedScene(
        TranslationStore store,
        string addonName,
        string? englishText,
        DialogueContext context)
    {
        if (context.CutsceneId != 0)
        {
            _dialogScenes[addonName] = context.CutsceneId;
            return context;
        }

        if (store.TryGetCutsceneId(englishText, out var scene))
        {
            _dialogScenes[addonName] = scene;
            return context with { CutsceneId = scene };
        }

        if (_dialogScenes.TryGetValue(addonName, out var remembered) && remembered != 0)
        {
            _log.Debug(
                "{Addon}: using scene {Scene} remembered from an earlier line for {Text}.",
                addonName, remembered, englishText ?? string.Empty);
            return context with { CutsceneId = remembered };
        }

        return context;
    }

    /// <summary>
    /// Decides what the overlay should show for one dialogue line. Pure apart from
    /// the store lookup, so tests can call it without building addon args.
    /// Returns null when nothing should be shown.
    /// </summary>
    internal static OverlayContent? Resolve(
        TranslationStore store,
        Configuration configuration,
        string? englishText,
        string? englishName,
        DialogueContext context = default,
        IReadOnlyList<int>? integerParameters = null,
        FailureTrace? trace = null)
    {
        if (string.IsNullOrWhiteSpace(englishText))
        {
            trace?.Step("talk.text", "empty");
            return null;
        }


        var hasLineAlias = store.TryGetLineSpeakerAlias(englishText, out var aliasEnglish, out var aliasChinese);
        if (hasLineAlias)
        {
            englishName = aliasEnglish;
        }
        else
        {
            var alias = MacroNormalizer.ExtractSpeakerPrefix(englishText);
            if (alias.Length > 0)
                englishName = alias;
        }

        trace?.Input("name", englishName);

        var hasText = store.TryGetDialogue(englishText, in context, englishName, out var chineseText, trace);
        trace?.Step("talk.text", hasText ? "hit" : "miss");

        if (!hasText && configuration.HideWhenNoTranslation)
        {
            trace?.Step("display", "hidden");
            return null;
        }

        var chineseName = string.Empty;
        if (configuration.ShowSpeakerName && !string.IsNullOrWhiteSpace(englishName))
        {
            if (!hasLineAlias)
                store.TryGetName(englishName, out chineseName, trace);
            else
                chineseName = aliasChinese;

            if (chineseName.Length == 0)
                trace?.Step("talk.name", "miss");
        }

        var speakerName = configuration.ShowSpeakerName
            ? FormatSpeakerName(chineseName, englishName)
            : string.Empty;

        var styledText = MacroNormalizer.ResolveIntegerParameters(chineseText, integerParameters);
        store.TryGetStyle(styledText, out var style);

        return new OverlayContent(styledText, speakerName, style);
    }

    private FailureTrace? BeginTrace(string addonName, string? text, string? name)
    {
        var trace = _failures?.Begin("talk", nameof(TalkHandler), text);
        if (trace is null)
            return null;

        trace.Input("addon", addonName);
        trace.Input("name", name);
        return trace;
    }

    private static void DescribeContext(FailureTrace? trace, in DialogueContext context)
    {
        if (trace is null)
            return;

        if (context.Row is { } row)
        {
            trace.Input("sheet", row.Sheet);
            trace.Input("row", row.RowId.ToString());
            trace.Input("slot", row.Slot.ToString());
            trace.Input("field", row.Field);
        }

        if (context.ScriptKeys is { Count: > 0 } keys)
            trace.Input("scriptKeys", string.Join(", ", keys));

        if (context.CutsceneId != 0)
            trace.Input("cutscene", context.CutsceneId.ToString());

        if (context.ScriptKeyHints is { Count: > 0 } hints)
            trace.Input("scriptHints", string.Join(", ", hints));
    }

    /// <summary>Shows the Chinese name with the English name in parentheses.</summary>
    internal static string FormatSpeakerName(string? chineseName, string? englishName)
    {
        var chinese = chineseName?.Trim() ?? string.Empty;
        var english = englishName?.Trim() ?? string.Empty;

        if (chinese.Length == 0)
            return english;

        // A masked name such as "???" is intentional. Do not reveal the English
        // name next to it.
        if (chinese.All(static ch => ch is '?' or '？'))
            return chinese;

        if (english.Length == 0 || string.Equals(chinese, english, StringComparison.Ordinal))
            return chinese;

        return $"{chinese} ({english})";
    }

    internal readonly record struct OverlayContent(
        string ChineseText,
        string ChineseName,
        UiTextStyle Style = default);

    private static unsafe List<int> ReadIntegerParameters(AtkValue* values, uint count)
    {
        var parameters = new List<int>();

        for (uint i = 2; i < count; i++)
        {
            ref var value = ref values[i];

            switch (value.Type)
            {
                case AtkValueType.Int:
                    parameters.Add(value.Int);
                    break;
                case AtkValueType.UInt:
                    parameters.Add(unchecked((int)value.UInt));
                    break;
            }
        }

        return parameters;
    }

    /// <summary>
    /// Diagnostic dump of every AtkValue in the event. Shows which index holds the
    /// dialogue text and what the game actually passed.
    /// </summary>
    private unsafe void LogAtkValues(string addonName, AtkValue* values, uint count)
    {
        for (uint i = 0; i < count; i++)
        {
            ref var value = ref values[i];

            if (!IsStringValue(value.Type))
            {
                _log.Debug("  {Addon} AtkValue[{Index}] type {Type}.", addonName, i, value.Type);
                continue;
            }

            var text = ReadStringValue(values, i, count);
            if (text is null)
            {
                _log.Debug("  {Addon} AtkValue[{Index}] string, unreadable.", addonName, i);
                continue;
            }

            var preview = text.Length > 120 ? string.Concat(text.AsSpan(0, 120), "...") : text;
            _log.Debug("  {Addon} AtkValue[{Index}] string, {Length} chars: {Text}",
                addonName, i, text.Length, preview);
        }
    }

    /// <summary>
    /// True for the events that mean the addon is gone. A conversation usually
    /// ends by hiding the Talk addon, not by closing it, so listening for
    /// <see cref="AddonEvent.PostClose"/> alone left the overlay on screen.
    /// </summary>
    internal static bool ShouldHideOverlay(AddonEvent eventType)
        => eventType is AddonEvent.PostClose or AddonEvent.PostHide or AddonEvent.PreFinalize;

    /// <summary>
    /// True for both string kinds. The game sends dialogue as ManagedString
    /// (Managed | String); the plain String kind is a caller-owned cstring. Both
    /// store the pointer in the same union field, so one read path serves both.
    /// </summary>
    internal static bool IsStringValue(AtkValueType type)
        => type == AtkValueType.String || type == AtkValueType.ManagedString;

    private static unsafe bool TryGetAtkValues(AddonArgs args, out AtkValue* values, out uint count)
    {
        switch (args)
        {
            case AddonSetupArgs setup when setup.AtkValues != IntPtr.Zero && setup.AtkValueCount > 0:
                values = (AtkValue*)setup.AtkValues;
                count = setup.AtkValueCount;
                return true;

            case AddonRefreshArgs refresh when refresh.AtkValues != IntPtr.Zero && refresh.AtkValueCount > 0:
                values = (AtkValue*)refresh.AtkValues;
                count = refresh.AtkValueCount;
                return true;

            default:
                values = null;
                count = 0;
                return false;
        }
    }

    /// <summary>
    /// Reads the addon's text nodes and joins the dialogue segments. A line
    /// with oblique words is split across several text nodes while
    /// <c>AtkValue[0]</c> carries only the first segment. Returns candidates in
    /// preference order: segments without the speaker name first, then every
    /// segment in case the name is part of the sentence.
    /// </summary>
    private unsafe List<string> GetTextNodeCandidates(AddonArgs args, string? englishName)
    {
        var candidates = new List<string>(2);

        try
        {
            var addon = args.GetAddonPointer<AtkUnitBase>();
            _log.Debug("Reading text nodes for {Addon}: addon found: {Found}.",
                args.AddonName, addon is not null);
            if (addon is null)
                return candidates;

            var talkAddon = (AddonTalk*)addon;
            var textNodes = new[]
            {
                talkAddon->AtkTextNode220,
                talkAddon->AtkTextNode228,
                talkAddon->AtkTextNode238,
                talkAddon->AtkTextNode240,
                talkAddon->AtkTextNode248,
            };

            var segments = new List<string>(textNodes.Length);
            foreach (var node in textNodes)
            {
                if (node is null)
                {
                    _log.Debug("  {Addon} text node: null.", args.AddonName);
                    continue;
                }

                var text = ExtractPlainText(node->NodeText.AsSpan()).Trim();
                _log.Debug("  {Addon} text node {NodeId}: {Length} chars: {Text}",
                    args.AddonName, node->NodeId, text.Length, text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                segments.Add(text);
            }

            if (segments.Count == 0)
                return candidates;

            var withoutName = string.Join(" ", segments.Where(s => !IsName(s, englishName)));
            if (!string.IsNullOrWhiteSpace(withoutName))
                candidates.Add(withoutName);

            var withName = string.Join(" ", segments);
            if (!string.IsNullOrWhiteSpace(withName) && withName != withoutName)
                candidates.Add(withName);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to read text nodes from {Addon}.", args.AddonName);
        }

        return candidates;
    }

    private static bool IsName(string text, string? englishName)
        => !string.IsNullOrWhiteSpace(englishName)
           && string.Equals(text.Trim(), englishName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a game string as plain text. The Talk text node holds a SeString, and a
    /// line with italics or bold carries a formatting macro in the middle of it.
    /// Reading the bytes as UTF-8 leaves the macro's payload bytes in the sentence,
    /// so the lookup key never matched. ExtractText keeps the text and drops the
    /// macros.
    /// </summary>
    internal static string ExtractPlainText(ReadOnlySpan<byte> seString)
        => new ReadOnlySeStringSpan(seString).ExtractText();

    private unsafe string? ReadStringValue(AtkValue* values, uint index, uint count)
    {
        if (values is null || index >= count)
            return null;

        try
        {
            ref var value = ref values[index];

            if (!IsStringValue(value.Type) || !value.String.HasValue)
                return null;

            // Stopping at the first formatting macro cut the line at its italics,
            // so an emphasized AtkValue text missed and only the text node retry
            // had a chance. Keep reading and drop the macros instead.
            var text = MemoryHelper.ReadSeStringAsString(
                out _,
                (nint)value.String.Value,
                8192,
                false,
                string.Empty);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to read AtkValue {Index}.", index);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister(TalkAddonName);
        Unregister(TalkSubtitleAddonName);
        foreach (var hooks in _valueHooks.Values)
            hooks.Dispose();

        _valueHooks.Clear();
        _valueHookUnavailable.Clear();
        _laidOutLines.Clear();

        while (_valueBuffers.Count > 0)
            Marshal.FreeHGlobal(_valueBuffers.Dequeue());

        _nativeWriter.RestoreAll();
        _overlay.Hide();
    }
}
