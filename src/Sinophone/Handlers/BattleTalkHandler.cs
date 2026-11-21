using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using InteropGenerator.Runtime;
using Sinophone.Data;

// Lumina also has a sheet called NpcYell; this is the game's rolling yell state.
using GameNpcYell = FFXIVClientStructs.FFXIV.Client.Game.UI.NpcYell;

namespace Sinophone.Handlers;

/// <summary>
/// Translates battle talk: the large line a boss, dungeon narration, or a duty NPC shows near the
/// top of the screen.
/// </summary>
/// <remarks>
/// The text arrives through three virtual functions on <see cref="UIModule"/>:
/// <c>ShowBattleTalk</c>, <c>ShowBattleTalkImage</c>, and <c>ShowBattleTalkSound</c>. Each takes the
/// name and the line as UTF-8 pointers, so the hook replaces them before the game measures the box,
/// and the box is then sized for the Chinese. Nothing is written into a text node and nothing has to
/// be restored: a line with no translation is simply passed through.
///
/// Rows come from <c>NpcYell</c> (a yell whose output type includes the battle talk flag),
/// <c>InstanceContentTextData</c> (dungeon narration), and <c>ContentTalk</c> (duty NPC lines). All
/// three are already indexed, so this is a handler only.
///
/// Alpha and off by default, because the game can only draw Chinese with a CJK font mod.
/// </remarks>
public sealed class BattleTalkHandler : IDisposable
{
    /// <summary>How many replaced strings are kept alive. Only one line shows at a time.</summary>
    private const int BuffersKept = 8;

    // Indices into the UIModule virtual table, from ClientStructs' VirtualFunction attributes on
    // UIModuleInterface. UIModule inherits that class first, so the indices are the same.
    private const int ShowBattleTalkIndex = 198;
    private const int ShowBattleTalkImageIndex = 199;
    private const int ShowBattleTalkSoundIndex = 201;

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameInteropProvider _interop;
    private readonly PluginLog _log;
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;

    private readonly List<nint> _buffers = new(BuffersKept + 1);

    /// <summary>Set by any show hook, so the box can report whether the call was seen.</summary>
    private long _lastShowTimestamp;

    private Hook<ShowBattleTalkDelegate>? _plainHook;
    private Hook<ShowBattleTalkImageDelegate>? _imageHook;
    private Hook<ShowBattleTalkSoundDelegate>? _soundHook;
    private bool _disposed;

    private unsafe delegate void ShowBattleTalkDelegate(
        UIModule* module,
        CStringPointer name,
        CStringPointer text,
        float duration,
        byte style);

    private unsafe delegate void ShowBattleTalkImageDelegate(
        UIModule* module,
        CStringPointer name,
        CStringPointer text,
        float duration,
        uint image,
        byte style,
        int sound,
        uint entityId);

    private unsafe delegate void ShowBattleTalkSoundDelegate(
        UIModule* module,
        CStringPointer name,
        CStringPointer text,
        float duration,
        int sound,
        byte style);

    public BattleTalkHandler(
        IAddonLifecycle addonLifecycle,
        IGameInteropProvider interop,
        IPluginLog log,
        Configuration configuration,
        Func<TranslationStore?> getStore)
    {
        _addonLifecycle = addonLifecycle;
        _interop = interop;
        _log = new PluginLog(log, configuration, LogCategory.Dialogue);
        _configuration = configuration;
        _getStore = getStore;

        InstallHooks();

        // The box opens after the show call. This line says which half failed: no hook, a hook
        // that never fires, a setting that is off, or a lookup that misses.
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, BattleTalkAddonName, OnBattleTalkSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, BattleTalkAddonName, OnBattleTalkFinalize);
    }

    private const string BattleTalkAddonName = "_BattleTalk";

    private void OnBattleTalkSetup(AddonEvent type, AddonArgs args)
    {
        if (!IsConfigured())
            return;

        var sinceShow = _lastShowTimestamp == 0
            ? -1
            : (int)Stopwatch.GetElapsedTime(_lastShowTimestamp).TotalMilliseconds;

        _log.Debug(
            "Battle talk: the box is up; hooks installed: {Hooked}, show call seen: {Seen} " +
            "({Since} ms ago), translated so far: {Count}.",
            _plainHook is not null, _lastShowTimestamp != 0, sinceShow, TranslatedLineCount);
    }

    private void OnBattleTalkFinalize(AddonEvent type, AddonArgs args) => ForgetBuffers();

    private bool IsConfigured() => _configuration.Enabled && _configuration.ShowInBattleTalk;

    /// <summary>Number of battle talk lines this handler replaced.</summary>
    public int TranslatedLineCount { get; private set; }

    /// <summary>
    /// Places the hooks again when the first attempt found no UI module. The plugin can load before
    /// the module is up, so the setting change retries instead of staying English until a reload.
    /// </summary>
    public void EnsureHooks()
    {
        // Any hook means the install got that far; hooking again would double-hook one form.
        if (_disposed || _plainHook is not null || _imageHook is not null || _soundHook is not null)
            return;

        InstallHooks();
    }

    /// <summary>True once at least one show function is hooked, for the startup log.</summary>
    public bool IsHooked => _plainHook is not null || _imageHook is not null || _soundHook is not null;

    /// <summary>
    /// Places the three hooks. The virtual table is read from the live module, so a table that
    /// moved leaves every hook unplaced and every line English.
    /// </summary>
    private unsafe void InstallHooks()
    {
        try
        {
            var module = UIModule.Instance();
            if (module is null)
            {
                _log.Warning("Battle talk: the UI module is not reachable; every line stays English.");
                return;
            }

            var table = *(nint**)(nint)module;
            if (table is null)
            {
                _log.Warning("Battle talk: the UI module exposed no virtual table; every line stays English.");
                return;
            }

            _plainHook = Hook<ShowBattleTalkDelegate>(table[ShowBattleTalkIndex], OnShowBattleTalk);
            _imageHook = Hook<ShowBattleTalkImageDelegate>(table[ShowBattleTalkImageIndex], OnShowBattleTalkImage);
            _soundHook = Hook<ShowBattleTalkSoundDelegate>(table[ShowBattleTalkSoundIndex], OnShowBattleTalkSound);

            _log.Info(
                "Battle talk: hooked ShowBattleTalk at {Plain:X}, the image form at {Image:X}, and the " +
                "sound form at {Sound:X}.",
                table[ShowBattleTalkIndex], table[ShowBattleTalkImageIndex], table[ShowBattleTalkSoundIndex]);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Battle talk: could not hook the UI module; every line stays English.");
            DisposeHooks();
        }
    }

    private Hook<T>? Hook<T>(nint address, T detour)
        where T : Delegate
    {
        if (address == 0)
        {
            _log.Warning("Battle talk: the UI module exposed no address for one form; it stays English.");
            return null;
        }

        var hook = _interop.HookFromAddress(address, detour);
        hook.Enable();
        return hook;
    }

    private unsafe void OnShowBattleTalk(
        UIModule* module,
        CStringPointer name,
        CStringPointer text,
        float duration,
        byte style)
    {
        var hook = _plainHook;
        if (hook is null)
            return;

        _lastShowTimestamp = Stopwatch.GetTimestamp();

        try
        {
            if (TryTranslate(name, text, out var chineseName, out var chineseText))
            {
                hook.Original(module, chineseName, chineseText, duration, style);
                return;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Battle talk: could not translate a line; leaving the game text.");
        }

        hook.Original(module, name, text, duration, style);
    }

    private unsafe void OnShowBattleTalkImage(
        UIModule* module,
        CStringPointer name,
        CStringPointer text,
        float duration,
        uint image,
        byte style,
        int sound,
        uint entityId)
    {
        var hook = _imageHook;
        if (hook is null)
            return;

        _lastShowTimestamp = Stopwatch.GetTimestamp();

        try
        {
            if (TryTranslate(name, text, out var chineseName, out var chineseText))
            {
                hook.Original(module, chineseName, chineseText, duration, image, style, sound, entityId);
                return;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Battle talk: could not translate a line with an image; leaving the game text.");
        }

        hook.Original(module, name, text, duration, image, style, sound, entityId);
    }

    private unsafe void OnShowBattleTalkSound(
        UIModule* module,
        CStringPointer name,
        CStringPointer text,
        float duration,
        int sound,
        byte style)
    {
        var hook = _soundHook;
        if (hook is null)
            return;

        _lastShowTimestamp = Stopwatch.GetTimestamp();

        try
        {
            if (TryTranslate(name, text, out var chineseName, out var chineseText))
            {
                hook.Original(module, chineseName, chineseText, duration, sound, style);
                return;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Battle talk: could not translate a line with a sound; leaving the game text.");
        }

        hook.Original(module, name, text, duration, sound, style);
    }

    /// <summary>
    /// Looks the line up and returns UTF-8 pointers to the Chinese. Both pointers fall back to the
    /// game's own when that half has no translation, so a name without a translation still shows
    /// the translated line.
    /// </summary>
    private unsafe bool TryTranslate(
        CStringPointer name,
        CStringPointer text,
        out CStringPointer chineseName,
        out CStringPointer chineseText)
    {
        chineseName = name;
        chineseText = text;

        if (!IsEnabled() || !text.HasValue)
            return false;

        var store = _getStore();
        if (store is null)
        {
            // The index is still loading. A line that plays now stays English; the log says why, so
            // a report of a missed line can be told apart from a hook that never fired.
            _log.Debug("Battle talk: the translation store is not ready yet; leaving the line English.");
            return false;
        }

        var english = MemoryHelper.ReadStringNullTerminated((nint)text.Value).Trim();
        if (english.Length == 0)
            return false;

        var englishName = name.HasValue
            ? MemoryHelper.ReadStringNullTerminated((nint)name.Value).Trim()
            : string.Empty;

        var yell = ReadYell(english, out var context, out var parameters);
        if (yell.HideSpeakerName)
            englishName = string.Empty;
        else if (englishName.Length == 0 && yell.Name.Length > 0)
            englishName = yell.Name;

        var content = Resolve(store, _configuration, english, englishName, context, parameters);
        if (content is null)
        {
            _log.Debug("Battle talk: no translation for a line: {FirstLine}", TalkHandler.FirstLineOf(english));
            return false;
        }

        var styled = content.Value;
        if (styled.ChineseText.Length == 0)
            return false;

        chineseText = Allocate(styled.ChineseText);
        if (styled.ChineseName.Length > 0)
            chineseName = Allocate(styled.ChineseName);

        TranslatedLineCount++;
        _log.Debug(
            "Battle talk: replaced a line before the game measured it ({Length} chars, name: {HasName}).",
            styled.ChineseText.Length, styled.ChineseName.Length > 0);

        return true;
    }

    /// <summary>
    /// The <c>NpcYell</c> row on screen, when the game's rolling yell list still has it. A row is
    /// used only for a yell that shows battle talk.
    /// </summary>
    private static unsafe YellMatch ReadYell(
        string englishText,
        out DialogueContext context,
        out List<int>? parameters)
    {
        context = default;
        parameters = null;

        var yells = GameNpcYell.Instance();
        if (yells is null)
            return default;

        foreach (var info in yells->YellInfo)
        {
            if (info.NpcYellRowId == 0 || !ShowsAsBattleTalk(info.OutputType))
                continue;

            if (!string.Equals(info.Message.ToString().Trim(), englishText, StringComparison.Ordinal))
                continue;

            var values = info.MessageParams;
            parameters = new List<int>(values.Length);
            foreach (var value in values)
                parameters.Add(unchecked((int)value));

            context = new DialogueContext(new DialogueRowId("NpcYell", info.NpcYellRowId, "Text"));

            return new YellMatch(
                info.Name.ToString().Trim(),
                (info.Flags & GameNpcYell.NpcYellFlags.HideSpeakerName) != 0);
        }

        return default;
    }

    /// <summary>True when the yell's output type draws battle talk.</summary>
    internal static bool ShowsAsBattleTalk(GameNpcYell.NpcYellOutputFlags outputType)
        => (outputType & GameNpcYell.NpcYellOutputFlags.ShowBattleTalk) != 0;

    /// <summary>
    /// The Chinese for one battle talk line, or null when it should stay English. Pure apart from
    /// the store lookup, so tests can call it without a live game.
    /// </summary>
    internal static TalkHandler.OverlayContent? Resolve(
        TranslationStore store,
        Configuration configuration,
        string? englishText,
        string? englishName,
        DialogueContext context = default,
        IReadOnlyList<int>? integerParameters = null)
    {
        if (!configuration.Enabled || !configuration.ShowInBattleTalk)
            return null;

        if (string.IsNullOrWhiteSpace(englishText))
            return null;


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


        // Battle talk has no overlay, so a miss is not a decision to hide: the game's own line
        // simply stays. Only a translated line is replaced.
        if (!store.TryGetDialogue(englishText, in context, englishName, out var chineseText))
            return null;

        var chineseName = string.Empty;
        if (configuration.ShowSpeakerName && !string.IsNullOrWhiteSpace(englishName))
        {
            if (!hasLineAlias)
                store.TryGetName(englishName, out chineseName);
            else
                chineseName = aliasChinese;
        }

        var speakerName = configuration.ShowSpeakerName
            ? TalkHandler.FormatSpeakerName(chineseName, englishName)
            : string.Empty;

        var styledText = MacroNormalizer.ResolveIntegerParameters(chineseText, integerParameters);
        return string.IsNullOrWhiteSpace(styledText)
            ? null
            : new TalkHandler.OverlayContent(styledText, speakerName);
    }

    private bool IsEnabled()
        => !_disposed && IsConfigured();

    /// <summary>
    /// Copies text into unmanaged UTF-8 memory and keeps it for the next few lines. The game copies
    /// the strings into its own fields, but keeping a few costs nothing and covers a copy that
    /// happens after the call returns.
    /// </summary>
    private unsafe CStringPointer Allocate(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var pointer = Marshal.AllocHGlobal(bytes.Length + 1);

        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        Marshal.WriteByte(pointer, bytes.Length, 0);

        _buffers.Add(pointer);
        while (_buffers.Count > BuffersKept)
        {
            Marshal.FreeHGlobal(_buffers[0]);
            _buffers.RemoveAt(0);
        }

        return new CStringPointer((byte*)pointer);
    }

    private void DisposeHooks()
    {
        _plainHook?.Dispose();
        _imageHook?.Dispose();
        _soundHook?.Dispose();
        _plainHook = null;
        _imageHook = null;
        _soundHook = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, BattleTalkAddonName, OnBattleTalkSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, BattleTalkAddonName, OnBattleTalkFinalize);
        DisposeHooks();
        ForgetBuffers();
    }

    private void ForgetBuffers()
    {
        foreach (var pointer in _buffers)
            Marshal.FreeHGlobal(pointer);

        _buffers.Clear();
    }

    /// <summary>What the game's yell list said about the line on screen.</summary>
    private readonly record struct YellMatch(string Name, bool HideSpeakerName);
}
