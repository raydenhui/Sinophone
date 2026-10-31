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
    public unsafe DialogueContext ReadContext(string? englishText, nint targetAddress = 0)
    {
        var framework = EventFramework.Instance();
        if (framework is null)
            return default;

        var handlers = CollectHandlers(framework, readAllLuaTexts: false, englishText, targetAddress);
        if (handlers.Count == 0)
            return default;

        var scriptKeys = new List<string>();
        var rows = new List<DialogueRowId>();

        foreach (var handler in handlers)
        {
            if (TalkEventSurfaces.TryGetRow(handler.EventId, handler.ContentId, handler.EntryId, out var row))
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
    public unsafe TalkEventSnapshot? ReadSnapshot(nint targetAddress = 0)
    {
        var framework = EventFramework.Instance();
        if (framework is null)
            return null;

        var handlers = CollectHandlers(framework, readAllLuaTexts: true, englishText: null, targetAddress);
        var sceneData = new int[framework->SceneData.Count];
        var values = framework->SceneData.Values;
        for (var i = 0; i < sceneData.Length && i < values.Length; i++)
            sceneData[i] = values[i];

        return new TalkEventSnapshot(
            framework->Scene,
            framework->SceneGameObjectId.Id,
            sceneData,
            framework->EventHandlerModule.EventHandlerMap.Count,
            ToState(framework->EventState1),
            ToState(framework->EventState2),
            handlers);
    }

    private static TalkEventState ToState(EventState state)
        => new(
            state.EventId.Id,
            (int)state.EventId.ContentId,
            state.EventId.EntryId,
            state.ObjectId.Id,
            state.CurrentEvent,
            state.CurrentEventParam);

    private unsafe List<TalkHandlerSnapshot> CollectHandlers(
        EventFramework* framework,
        bool readAllLuaTexts,
        string? englishText,
        nint targetAddress)
    {
        var result = new List<TalkHandlerSnapshot>();

        // The player usually targets the NPC or object before talking. Its
        // EventHandler pointer is the handler running the dialogue. This is the only
        // link that survives the event state being cleared when Talk opens.
        if (targetAddress != 0)
        {
            foreach (var (_, pointer) in framework->EventHandlerModule.EventHandlerMap)
            {
                var handler = pointer.Value;
                if (handler is null || handler->Info.EventId.Id == 0)
                    continue;

                var ownsTarget = handler->EventGameObject is not null
                                 && (nint)handler->EventGameObject == targetAddress;

                if (!ownsTarget && handler->SceneGameObject is not null)
                    ownsTarget = (nint)handler->SceneGameObject == targetAddress;

                if (ownsTarget)
                    result.Add(CreateSnapshot(handler, readAllLuaTexts, englishText));
            }

            if (result.Count > 0)
                return result;
        }

        var activeIds = new HashSet<uint>();
        AddActiveId(activeIds, framework->EventState1.EventId);
        AddActiveId(activeIds, framework->EventState2.EventId);

        foreach (var (_, pointer) in framework->EventHandlerModule.EventHandlerMap)
        {
            var handler = pointer.Value;
            if (handler is null)
                continue;

            var eventId = handler->Info.EventId;
            if (eventId.Id == 0)
                continue;

            // EventState is the interaction the player is actually in. Scene is -1
            // for many handlers, so it alone cannot identify the Talk line.
            if (activeIds.Count > 0)
            {
                if (!activeIds.Contains(eventId.Id))
                    continue;
            }
            else if (handler->Scene != framework->Scene)
            {
                continue;
            }

            result.Add(CreateSnapshot(handler, readAllLuaTexts, englishText));
        }

        return result;
    }

    private static unsafe TalkHandlerSnapshot CreateSnapshot(
        EventHandler* handler,
        bool readAllLuaTexts,
        string? englishText)
    {
        var eventId = handler->Info.EventId;
        var luaTexts = TryReadLuaTexts(handler, eventId.ContentId, readAllLuaTexts, englishText);
        return new TalkHandlerSnapshot(
            eventId.Id,
            (int)eventId.ContentId,
            eventId.EntryId,
            handler->Scene,
            luaTexts);
    }

    private static void AddActiveId(HashSet<uint> ids, EventId eventId)
    {
        if (eventId.Id != 0)
            ids.Add(eventId.Id);
    }

    private static unsafe List<KeyValuePair<string, string>>? TryReadLuaTexts(
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
    int HandlerCount,
    TalkEventState EventState1,
    TalkEventState EventState2,
    IReadOnlyList<TalkHandlerSnapshot> Handlers);

/// <summary>The interaction currently held in one of EventFramework's two event slots.</summary>
internal sealed record TalkEventState(
    uint EventId,
    int ContentId,
    ushort EntryId,
    ulong ObjectId,
    byte CurrentEvent,
    uint CurrentEventParam);

/// <summary>Maps an event content id to a sheet row whose text the Talk window can show.</summary>
internal static class TalkEventSurfaces
{
    public static bool TryGetRow(uint eventId, int contentId, ushort entryId, out DialogueRowId row)
    {
        switch ((EventHandlerContent)contentId)
        {
            case EventHandlerContent.DefaultTalk:
                // DefaultTalk row ids are packed as (content << 16) | entry, which is
                // exactly EventId.Id. EntryId alone is only the low half.
                row = new DialogueRowId("DefaultTalk", eventId, "Text");
                return true;

            case EventHandlerContent.GuildLeveAssignment:
                row = new DialogueRowId("GuildleveAssignmentTalk", entryId, "Talk");
                return true;

            case EventHandlerContent.CraftLeveClient:
                row = new DialogueRowId("CraftLeveTalk", entryId, "Talk");
                return true;

            case EventHandlerContent.GimmickAccessor:
            case EventHandlerContent.GimmickRect:
                row = new DialogueRowId("GimmickTalk", entryId, "Message");
                return true;

            case EventHandlerContent.GimmickBill:
                row = new DialogueRowId("GimmickBill", entryId, "Text");
                return true;

            case EventHandlerContent.GoldSaucerTalk:
                row = new DialogueRowId("GoldSaucerTalk", entryId, "Message");
                return true;

            default:
                row = default;
                return false;
        }
    }
}
