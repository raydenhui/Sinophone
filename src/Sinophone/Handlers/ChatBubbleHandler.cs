using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using InteropGenerator.Runtime;

// Lumina also has a sheet called NpcYell; this is the game's rolling yell state.
using GameNpcYell = FFXIVClientStructs.FFXIV.Client.Game.UI.NpcYell;

namespace Sinophone.Handlers;

/// <summary>
/// Translates the chat bubbles the game draws above an NPC's head.
/// </summary>
/// <remarks>
/// Two things feed a bubble, and both are already indexed:
/// <list type="bullet">
/// <item>A yell: an <c>NpcYell</c> row whose output type includes the balloon flag. The game
/// starts the bubble through <c>NpcYellBalloon.OpenBalloon</c>, so the hook replaces the text
/// before the bubble is laid out and the game sizes the bubble for the Chinese.</item>
/// <item>A default balloon: an <c>ENpcBase.Balloon</c> row, drawn from the character's balloon
/// state. There is no function to hook, so the bubble's text node is translated in place and the
/// node sizes are logged for the in-game check.</item>
/// </list>
/// Alpha and off by default, because native output needs the CJK font mod.
/// </remarks>
public sealed class ChatBubbleHandler : IDisposable
{
    private const string AddonName = "_MiniTalk";

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameInteropProvider _interop;
    private readonly PluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly NativeTextWriter _nativeWriter;

    private readonly IAddonLifecycle.AddonEventDelegate _onAddonEvent;

    /// <summary>
    /// The bubble text nodes this handler has seen, keyed by node address. A default balloon's
    /// text is written by the addon after it is set up, so a node can be empty the first time it
    /// is seen. The state records the text that was looked at, so a frame costs a hash compare
    /// and a line is not looked up again until the game writes a different one.
    /// </summary>
    private readonly Dictionary<nint, BubbleState> _nodes = [];

    private Hook<OpenBalloonDelegate>? _openBalloonHook;
    private bool _disposed;

    private unsafe delegate void OpenBalloonDelegate(
        NpcYellBalloon* balloon,
        CStringPointer text,
        float playTime,
        bool softOpen,
        float openDelay,
        bool printToLog,
        bool reducedCloseChecks,
        bool ignoreRangeCheck,
        byte parentBone);

    public ChatBubbleHandler(
        IAddonLifecycle addonLifecycle,
        IGameInteropProvider interop,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore,
        NativeTextWriter nativeWriter)
    {
        _addonLifecycle = addonLifecycle;
        _interop = interop;
        _log = new PluginLog(log, configuration, LogCategory.Dialogue);
        _configuration = configuration;
        _getStore = getStore;
        _nativeWriter = nativeWriter;
        _onAddonEvent = OnAddonEvent;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostHide, AddonName, _onAddonEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, _onAddonEvent);

        InstallOpenBalloonHook();
    }

    /// <summary>Number of bubbles whose text this handler changed.</summary>
    public int TranslatedBubbleCount => _nodes.Values.Count(static state => state.Translated);

    /// <summary>
    /// Hooks the game's own bubble start, so the Chinese line goes in before the bubble is
    /// measured. The address comes from ClientStructs, which resolves the game signature.
    /// </summary>
    private unsafe void InstallOpenBalloonHook()
    {
        try
        {
            var address = (nint)NpcYellBalloon.MemberFunctionPointers.OpenBalloon;
            if (address == 0)
            {
                _log.Warning("Chat bubbles: the game exposed no balloon start address; using text nodes.");
                return;
            }

            _openBalloonHook = _interop.HookFromAddress<OpenBalloonDelegate>(address, OnOpenBalloon);
            _openBalloonHook.Enable();
            _log.Debug("Chat bubbles: hooked the balloon start at {Address:X}.", address);
        }
        catch (Exception ex)
        {
            _openBalloonHook?.Dispose();
            _openBalloonHook = null;
            _log.Warning(ex, "Chat bubbles: could not hook the balloon start; using text nodes.");
        }
    }

    private unsafe void OnOpenBalloon(
        NpcYellBalloon* balloon,
        CStringPointer text,
        float playTime,
        bool softOpen,
        float openDelay,
        bool printToLog,
        bool reducedCloseChecks,
        bool ignoreRangeCheck,
        byte parentBone)
    {
        var hook = _openBalloonHook;
        if (hook is null)
            return;

        string? chinese = null;

        try
        {
            chinese = TryTranslateYell(text);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Chat bubbles: could not translate a yell; leaving the game text.");
        }

        if (chinese is null)
        {
            hook.Original(balloon, text, playTime, softOpen, openDelay, printToLog,
                reducedCloseChecks, ignoreRangeCheck, parentBone);
            return;
        }

        // The balloon start copies the string into the character's own Utf8String, so the
        // buffer only has to live for the call.
        var bytes = Encoding.UTF8.GetBytes(chinese);
        fixed (byte* pointer = bytes)
        {
            hook.Original(balloon, new CStringPointer(pointer), playTime, softOpen, openDelay,
                printToLog, reducedCloseChecks, ignoreRangeCheck, parentBone);
        }

        _log.Debug("Chat bubbles: translated a yell before layout ({Length} chars).", chinese.Length);
    }

    /// <summary>
    /// Looks the yell up by its game row before its text, then by its English text. Returns
    /// the Chinese line, or null when the line has no translation.
    /// </summary>
    private unsafe string? TryTranslateYell(CStringPointer text)
    {
        if (!IsEnabled())
            return null;

        var store = _getStore();
        if (store is null || !text.HasValue)
            return null;

        var english = MemoryHelper.ReadStringNullTerminated((nint)text.Value).Trim();
        if (english.Length == 0)
            return null;

        var context = ReadYellContext(english, out var parameters);
        return Resolve(store, _configuration, english, context, parameters);
    }

    /// <summary>
    /// The <c>NpcYell</c> row for the yell on screen, when the game's rolling yell list still
    /// has it. A row is used only for a yell that shows a balloon.
    /// </summary>
    private static unsafe DialogueContext ReadYellContext(string englishText, out List<int>? parameters)
    {
        parameters = null;

        var yells = GameNpcYell.Instance();
        if (yells is null)
            return default;

        foreach (var info in yells->YellInfo)
        {
            if (info.NpcYellRowId == 0 || !ShowsAsBubble(info.OutputType))
                continue;

            if (!string.Equals(info.Message.ToString().Trim(), englishText, StringComparison.Ordinal))
                continue;

            var values = info.MessageParams;
            parameters = new List<int>(values.Length);
            foreach (var value in values)
                parameters.Add(unchecked((int)value));

            return new DialogueContext(new DialogueRowId("NpcYell", info.NpcYellRowId, "Text"));
        }

        return default;
    }

    /// <summary>True when the yell's output type draws a chat bubble.</summary>
    internal static bool ShowsAsBubble(GameNpcYell.NpcYellOutputFlags outputType)
        => (outputType & GameNpcYell.NpcYellOutputFlags.ShowBalloon) != 0;

    /// <summary>
    /// The Chinese line for one bubble, or null when it should stay English. Pure apart from
    /// the store lookup, so tests can call it without a live addon.
    /// </summary>
    internal static string? Resolve(
        TranslationStore store,
        Configuration configuration,
        string? englishText,
        DialogueContext context = default,
        IReadOnlyList<int>? integerParameters = null)
    {
        if (!configuration.Enabled || !configuration.ShowInChatBubbles)
            return null;

        if (string.IsNullOrWhiteSpace(englishText))
            return null;

        if (!store.TryGetDialogue(englishText, in context, null, out var chineseText))
            return null;

        var styled = MacroNormalizer.ResolveIntegerParameters(chineseText, integerParameters);
        return string.IsNullOrWhiteSpace(styled) ? null : styled;
    }

    private bool IsEnabled()
        => !_disposed && _configuration.Enabled && _configuration.ShowInChatBubbles;

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        if (_disposed)
            return;

        switch (eventType)
        {
            case AddonEvent.PreFinalize:
                // The addon is going away, so its nodes may already be freed. Drop the
                // saved text without writing it.
                ForgetAll();
                return;

            case AddonEvent.PostHide:
                RestoreAll();
                return;

            case AddonEvent.PreDraw:
            case AddonEvent.PostSetup:
            case AddonEvent.PostRefresh:
                SyncBubbles(args);
                return;
        }
    }

    /// <summary>
    /// Translates every bubble that holds a text this handler has not seen, and puts the Chinese
    /// back when the game rewrote a node it already translated.
    /// </summary>
    /// <remarks>
    /// There is no function to hook for a default balloon: the addon fills the text node itself,
    /// and it does that after <c>PostSetup</c>. So this runs on every draw, and the per-frame work
    /// stays a hash compare against the last text seen. A node that is not translated yet is only
    /// looked up when its text changes.
    /// </remarks>
    private unsafe void SyncBubbles(AddonArgs args)
    {
        if (!IsEnabled())
            return;

        var store = _getStore();
        if (store is null)
            return;

        var addon = args.GetAddonPointer<AtkUnitBase>();
        if (addon is null)
            return;

        foreach (var entry in ((AddonMiniTalk*)addon)->TalkBubbles)
        {
            var node = entry.BubbleTextNode;
            if (node is null)
                continue;

            var address = (nint)node;
            var text = node->NodeText.AsSpan();
            var hash = Hash(text);
            var length = text.Length;

            if (_nodes.TryGetValue(address, out var state))
            {
                if (state.Translated)
                {
                    // Our Chinese is still in the node; only the background can drift.
                    if (_nativeWriter.IsStillOursAt(AddonName, address))
                    {
                        state.Layout.GrowTo(node);
                        continue;
                    }

                    // The game put the same English line back, which it does while a
                    // balloon's text stream runs.
                    if (hash == state.EnglishHash && length == state.EnglishLength)
                    {
                        _nativeWriter.ReapplyAt(AddonName, address);
                        state.Layout.GrowTo(node);
                        continue;
                    }

                    // A different line landed in this node. Drop the old translation and
                    // its sizes, then treat the node as new.
                    _nativeWriter.ForgetAt(AddonName, address);
                    state.Layout.Restore();
                    _nodes.Remove(address);
                }
                else if (hash == state.TextHash && length == state.TextLength)
                {
                    // Already looked up, and the line has no translation. Do not retry it
                    // on every frame.
                    continue;
                }
            }

            var english = length == 0 ? string.Empty : TalkHandler.ExtractPlainText(text).Trim();
            var chinese = english.Length == 0 ? null : Resolve(store, _configuration, english);

            if (chinese is null)
            {
                _nodes[address] = new BubbleState { TextHash = hash, TextLength = length };

                if (english.Length > 0)
                {
                    _log.Debug("Chat bubbles: no translation for a balloon line: {FirstLine}",
                        TalkHandler.FirstLineOf(english));
                }

                continue;
            }

            var layout = BubbleLayout.Capture(node, entry.BubbleNineGridNode, entry.BubbleResNode);

            if (!_nativeWriter.WriteAt(AddonName, address, chinese))
                continue;

            _nodes[address] = new BubbleState
            {
                TextHash = hash,
                TextLength = length,
                EnglishHash = hash,
                EnglishLength = length,
                Translated = true,
                Layout = layout,
            };

            layout.GrowTo(node);

            _log.Debug(
                "Chat bubbles: translated a balloon in place ({Length} chars, text {TextW}x{TextH}, " +
                "background {NineW}x{NineH}, bubble {BubbleW}x{BubbleH}).",
                chinese.Length, node->Width, node->Height,
                layout.NineGridWidth, layout.NineGridHeight,
                layout.BubbleWidth, layout.BubbleHeight);
        }
    }

    /// <summary>Puts the game's own bubble text and sizes back.</summary>
    public unsafe void RestoreAll()
    {
        if (_nodes.Count == 0)
        {
            _nativeWriter.RestoreAddon(AddonName);
            return;
        }

        foreach (var (address, state) in _nodes)
        {
            if (!state.Translated)
                continue;

            // The writer puts the game's English back and its own saved node size with it;
            // then the background and bubble go back to the sizes they had.
            _nativeWriter.RestoreAt(AddonName, address);
            state.Layout.Restore();
        }

        _nodes.Clear();
        _nativeWriter.RestoreAddon(AddonName);
    }

    /// <summary>Drops the translated bubbles without writing into nodes that may be gone.</summary>
    private void ForgetAll()
    {
        foreach (var (address, state) in _nodes)
        {
            if (state.Translated)
                _nativeWriter.ForgetAt(AddonName, address);
        }

        _nodes.Clear();
    }

    /// <summary>
    /// A cheap hash of the node's UTF-8 text, so a frame can tell whether the game wrote a
    /// different line without allocating a string.
    /// </summary>
    private static ulong Hash(ReadOnlySpan<byte> text)
    {
        var hash = 14695981039346656037UL;

        foreach (var value in text)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, AddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, AddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, AddonName, _onAddonEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, _onAddonEvent);

        _openBalloonHook?.Dispose();
        _openBalloonHook = null;

        RestoreAll();
    }

    /// <summary>What a bubble text node held when it was last looked at.</summary>
    private struct BubbleState
    {
        /// <summary>Hash and length of the last text seen, for a line with no translation.</summary>
        public ulong TextHash;
        public int TextLength;

        /// <summary>Hash and length of the English line this handler translated.</summary>
        public ulong EnglishHash;
        public int EnglishLength;

        /// <summary>True when the Chinese for that English line is in the node.</summary>
        public bool Translated;

        /// <summary>The sizes to put back when the bubble ends.</summary>
        public BubbleLayout Layout;
    }

    /// <summary>
    /// The sizes one bubble had before its text was replaced, and the padding around the text
    /// that the background keeps when the text grows.
    /// </summary>
    /// <remarks>
    /// The nine-grid is the visible bubble and it is larger than the text node by the padding the
    /// game drew, so the difference between those two is the padding to keep. The bubble's res
    /// node is the text-sized content box and gives no padding of its own.
    /// </remarks>
    private readonly record struct BubbleLayout(
        nint NineGrid,
        nint Bubble,
        ushort TextWidth,
        ushort TextHeight,
        ushort NineGridWidth,
        ushort NineGridHeight,
        ushort BubbleWidth,
        ushort BubbleHeight)
    {
        public static unsafe BubbleLayout Capture(
            AtkTextNode* node,
            AtkNineGridNode* nineGrid,
            AtkResNode* bubble)
            => new(
                (nint)nineGrid,
                (nint)bubble,
                node->Width,
                node->Height,
                nineGrid is null ? (ushort)0 : nineGrid->AtkResNode.Width,
                nineGrid is null ? (ushort)0 : nineGrid->AtkResNode.Height,
                bubble is null ? (ushort)0 : bubble->Width,
                bubble is null ? (ushort)0 : bubble->Height);

        /// <summary>
        /// Grows the background and the bubble to hold the text the node now has, plus the padding
        /// the game drew. The text node itself is left alone: its width is what the game wraps at,
        /// and changing it makes the game write the line back with hard breaks. Only ever grows, so
        /// a shorter line keeps the game's layout.
        /// </summary>
        public unsafe void GrowTo(AtkTextNode* node)
        {
            var paddingX = Math.Max(0, (int)NineGridWidth - TextWidth);
            var paddingY = Math.Max(0, (int)NineGridHeight - TextHeight);

            var width = Clamp(node->Width + paddingX);
            var height = Clamp(node->Height + paddingY);

            if (NineGrid != 0 && (AtkNineGridNode*)NineGrid is var nineGrid && nineGrid is not null)
            {
                if (nineGrid->AtkResNode.Width < width)
                    nineGrid->AtkResNode.Width = width;

                if (nineGrid->AtkResNode.Height < height)
                    nineGrid->AtkResNode.Height = height;
            }

            if (Bubble != 0 && (AtkResNode*)Bubble is var bubble && bubble is not null)
            {
                if (bubble->Width < width)
                    bubble->Width = width;

                if (bubble->Height < height)
                    bubble->Height = height;
            }
        }

        /// <summary>Puts the background and the bubble back to the sizes the game gave them.</summary>
        public unsafe void Restore()
        {
            if (NineGrid != 0 && (AtkNineGridNode*)NineGrid is var nineGrid && nineGrid is not null)
            {
                nineGrid->AtkResNode.Width = NineGridWidth;
                nineGrid->AtkResNode.Height = NineGridHeight;
            }

            if (Bubble != 0 && (AtkResNode*)Bubble is var bubble && bubble is not null)
            {
                bubble->Width = BubbleWidth;
                bubble->Height = BubbleHeight;
            }
        }

        private static ushort Clamp(int value)
            => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
    }
}
