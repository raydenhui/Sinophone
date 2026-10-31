using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.Interop;
using EventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

namespace Sinophone.Handlers;

/// <summary>
/// Reads the event ids the game has active while a Talk addon is open. The ids are
/// optional: a missing or changed game structure only costs the id-first lookup and
/// leaves the text fallback in place.
/// </summary>
internal sealed class TalkEventReader
{
    private readonly IPluginLog _log;

    public TalkEventReader(IPluginLog log) => _log = log;

    /// <summary>
    /// Builds the id context for one line. The row id is added only when exactly one
    /// active handler maps to a known talk sheet, so an ambiguous scene falls back to
    /// text instead of guessing. Script keys are collected only when a Lua handler's
    /// value is the line on screen.
    /// </summary>
    public unsafe DialogueContext ReadContext(string? englishText)
    {
        var framework = EventFramework.Instance();
        if (framework is null)
            return default;

        var handlers = CollectHandlers(framework, readAllLuaTexts: false, englishText);
        if (handlers.Count == 0)
            return default;

        var scriptKeys = new List<string>();
        var rows = new List<DialogueRowId>();

        foreach (var handler in handlers)
        {
            if (TalkEventSurfaces.TryGetRow(handler.ContentId, handler.EntryId, out var row))
                rows.Add(row);

            if (handler.LuaTexts is not null)
                CollectScriptKeys(handler.LuaTexts, englishText, scriptKeys);
        }

        DialogueRowId? selectedRow = rows.Count == 1 ? rows[0] : null;

        if (rows.Count > 1)
        {
            _log.Debug("Talk event has {Count} candidate handlers with sheet rows; using text fallback.", rows.Count);
        }

        return new DialogueContext(
            selectedRow,
            scriptKeys.Count == 0 ? null : scriptKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>
    /// Full diagnostic view used by the debug probe. It reads every active handler and
    /// all Lua text pairs, not just the ones that match the line.
    /// </summary>
    public unsafe TalkEventSnapshot? ReadSnapshot()
    {
        var framework = EventFramework.Instance();
        if (framework is null)
            return null;

        var handlers = CollectHandlers(framework, readAllLuaTexts: true, englishText: null);
        var sceneData = new int[framework->SceneData.Count];
        var values = framework->SceneData.Values;
        for (var i = 0; i < sceneData.Length && i < values.Length; i++)
            sceneData[i] = values[i];

        return new TalkEventSnapshot(
            framework->Scene,
            framework->SceneGameObjectId.Id,
            sceneData,
            handlers);
    }

    private unsafe List<TalkHandlerSnapshot> CollectHandlers(
        EventFramework* framework,
        bool readAllLuaTexts,
        string? englishText)
    {
        var result = new List<TalkHandlerSnapshot>();

        foreach (var (_, pointer) in framework->EventHandlerModule.EventHandlerMap)
        {
            var handler = pointer.Value;
            if (handler is null)
                continue;

            var eventId = handler->Info.EventId;
            if (eventId.Id == 0)
                continue;

            // The Talk addon does not name its handler, so the current scene is the
            // only cheap link between the window and an event.
            if (handler->Scene != framework->Scene)
                continue;

            var luaTexts = TryReadLuaTexts(handler, eventId.ContentId, readAllLuaTexts, englishText);
            result.Add(new TalkHandlerSnapshot(
                eventId.Id,
                (int)eventId.ContentId,
                eventId.EntryId,
                handler->Scene,
                luaTexts));
        }

        return result;
    }

    private unsafe List<KeyValuePair<string, string>>? TryReadLuaTexts(
        EventHandler* handler,
        EventHandlerContent contentId,
        bool readAll,
        string? englishText)
    {
        // Only these handler kinds derive from LuaEventHandler. Casting any other kind
        // would read past its allocation.
        if (contentId is not (EventHandlerContent.Quest
            or EventHandlerContent.CustomTalk
            or EventHandlerContent.GuildLeveAssignment))
        {
            return null;
        }

        var lua = (LuaEventHandler*)handler;
        if (lua->LuaTexts.Count == 0)
            return null;

        var result = new List<KeyValuePair<string, string>>(readAll ? lua->LuaTexts.Count : 4);
        var wanted = readAll ? string.Empty : MacroNormalizer.NormalizeKey(englishText);

        foreach (var (_, text) in lua->LuaTexts)
        {
            var key = text.Key.ToString();
            var value = text.Value.ToString();

            // The product path only needs the keys whose value is the line on
            // screen. The probe keeps every pair so it can show the whole map.
            if (!readAll && (value.Length == 0 || MacroNormalizer.NormalizeKey(value) != wanted))
                continue;

            result.Add(new KeyValuePair<string, string>(key, value));
        }

        return result;
    }

    private static void CollectScriptKeys(
        IReadOnlyList<KeyValuePair<string, string>> luaTexts,
        string? englishText,
        List<string> scriptKeys)
    {
        if (string.IsNullOrWhiteSpace(englishText))
            return;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return;

        foreach (var (luaKey, value) in luaTexts)
        {
            if (luaKey.Length == 0 || value.Length == 0)
                continue;

            if (MacroNormalizer.NormalizeKey(value) == key)
                scriptKeys.Add(luaKey);
        }
    }
}

/// <summary>One active event handler captured by the debug probe.</summary>
internal sealed record TalkHandlerSnapshot(
    uint EventId,
    int ContentId,
    ushort EntryId,
    short Scene,
    IReadOnlyList<KeyValuePair<string, string>>? LuaTexts);

/// <summary>The event and scene state captured by the debug probe.</summary>
internal sealed record TalkEventSnapshot(
    short Scene,
    ulong SceneGameObjectId,
    IReadOnlyList<int> SceneData,
    IReadOnlyList<TalkHandlerSnapshot> Handlers);

/// <summary>Maps an event content id to a sheet row whose text the Talk window can show.</summary>
internal static class TalkEventSurfaces
{
    public static bool TryGetRow(int contentId, ushort entryId, out DialogueRowId row)
    {
        switch ((EventHandlerContent)contentId)
        {
            case EventHandlerContent.DefaultTalk:
                row = new DialogueRowId("DefaultTalk", entryId, "Text");
                return true;

            case EventHandlerContent.GoldSaucerTalk:
                row = new DialogueRowId("GoldSaucerTalk", entryId, "Message");
                return true;

            case EventHandlerContent.GimmickBill:
                row = new DialogueRowId("GimmickBill", entryId, "Text");
                return true;

            default:
                row = default;
                return false;
        }
    }
}
