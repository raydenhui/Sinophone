using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.Interop;
using EventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

namespace Sinophone.Handlers;

/// <summary>
/// Reads the event ids the game has active while a Talk addon is open. The ids are
/// optional: a missing or changed game structure only costs the id-first lookup and
/// leaves the text fallback in place.
/// </summary>
internal sealed partial class TalkEventReader
{
    private readonly PluginLog _log;
    private readonly IDataManager? _dataManager;

    /// <summary>The cutscene sheet indexed for the current path, and its text to row map.</summary>
    private string _cutsceneSheet = string.Empty;
    private Dictionary<string, uint>? _cutsceneRows;

    public TalkEventReader(
        IPluginLog log,
        Configuration configuration,
        LogCategory category,
        IDataManager? dataManager = null)
    {
        _log = new PluginLog(log, configuration, category);
        _dataManager = dataManager;
    }

    /// <summary>
    /// Builds the id context for one line. The row id is added only when exactly one
    /// active handler maps to a known talk sheet, so an ambiguous scene falls back to
    /// text instead of guessing. Script keys are collected only when a Lua handler's
    /// value is the line on screen.
    /// </summary>
    /// <remarks>
    /// <paramref name="readChocoboTaxiStandState"/> is false for a line the store has no stand
    /// variant for, so an ordinary line does not read the game state.
    /// </remarks>
    public unsafe DialogueContext ReadContext(
        string? englishText,
        nint targetAddress = 0,
        bool includeHandlerHints = false,
        bool scanAllLuaTexts = false,
        bool readChocoboTaxiStandState = true)
    {
        var framework = EventFramework.Instance();
        if (framework is null)
            return default;

        var handlers = CollectHandlers(framework, readAllLuaTexts: false, englishText, targetAddress);
        if (handlers.Count == 0)
        {
            // A cutscene choice runs with no talk handler of its own, and the scene number is the
            // only thing that tells its VOICEMAN lines apart from another scene's.
            var scene = ReadCutsceneId(framework);
            var fallbackHints = ReadTrackedQuestHints();
            if (scanAllLuaTexts)
                MergeHints(ref fallbackHints, ReadAllLuaTextKeys(framework, englishText));

            return new DialogueContext(
                CutsceneId: scene,
                ScriptKeyHints: fallbackHints,
                ScriptRow: ReadCutsceneRow(framework, englishText));
        }

        var scriptKeys = new List<string>();
        Dictionary<string, uint>? scriptRowIds = null;
        Dictionary<string, string>? scriptTemplates = null;
        var rows = new List<DialogueRowId>();
        var cutsceneId = ReadCutsceneId(framework);
        List<string>? handlerHints = null;

        foreach (var handler in handlers)
        {
            if (TalkEventSurfaces.TryGetRow(handler.EventId, handler.ContentId, handler.EntryId, out var row))
                rows.Add(row);

            if (handler.LuaTexts is not null)
            {
                CollectScriptKeys(handler.LuaTexts, scriptKeys);
                scriptRowIds = CollectScriptRowIds(handler.LuaTexts, scriptRowIds);
                scriptTemplates = CollectScriptTemplates(handler.LuaTexts, scriptTemplates);
            }

            if (includeHandlerHints && handler.LuaKey.Length > 0)
            {
                handlerHints ??= [];
                if (!handlerHints.Contains(handler.LuaKey, StringComparer.OrdinalIgnoreCase))
                    handlerHints.Add(handler.LuaKey);
            }
        }

        MergeHints(ref handlerHints, ReadTrackedQuestHints());
        if (scanAllLuaTexts && scriptKeys.Count == 0 && cutsceneId == 0)
            MergeHints(ref handlerHints, ReadAllLuaTextKeys(framework, englishText));

        DialogueRowId? selectedRow = rows.Count == 1 ? rows[0] : null;

        if (rows.Count > 1)
        {
            var signature = string.Join(", ", rows.Select(static row => $"{row.Sheet} {row.RowId}"));
            if (!string.Equals(signature, _lastRowLog, StringComparison.Ordinal))
            {
                _lastRowLog = signature;
                _log.Debug(
                    "Talk event has {Count} candidate handlers with sheet rows; using text fallback: {Rows}.",
                    rows.Count, signature);
            }
        }
        else
        {
            _lastRowLog = string.Empty;
        }

        return new DialogueContext(
            selectedRow,
            scriptKeys.Count == 0 ? null : scriptKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cutsceneId,
            readChocoboTaxiStandState ? ReadChocoboTaxiStandUnlocked(handlers) : null,
            handlerHints,
            scriptRowIds,
            ReadCutsceneRow(framework, englishText),
            scriptTemplates);
    }

    /// <summary>
    /// The context for a second text candidate in the same refresh, such as a Talk addon's text
    /// node. The handlers, scene and quest hints were read for the first candidate, so only the
    /// cutscene subtitle row follows the new text.
    /// </summary>
    public unsafe DialogueContext ReadRetryContext(string? englishText, in DialogueContext context)
    {
        var framework = EventFramework.Instance();
        return framework is null
            ? context
            : context with { ScriptRow = ReadCutsceneRow(framework, englishText) };
    }

    /// <summary>
    /// The row id each live script key uses. The key alone can be reused for another
    /// menu line; the pair is the line's identity in the handler's own sheet.
    /// </summary>
    private static Dictionary<string, uint>? CollectScriptRowIds(
        IReadOnlyList<LuaTextLine> lines,
        Dictionary<string, uint>? ids)
    {
        foreach (var line in lines)
        {
            if (line.Key.Length == 0)
                continue;

            ids ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            ids.TryAdd(line.Key, line.RowId);
        }

        return ids;
    }

    private static void MergeHints(ref List<string>? hints, List<string>? added)
    {
        if (added is null || added.Count == 0)
            return;

        hints ??= [];
        foreach (var key in added)
        {
            if (key.Length > 0 && !hints.Contains(key, StringComparer.OrdinalIgnoreCase))
                hints.Add(key);
        }
    }

    /// <summary>
    /// Every Lua text key in the event handler map whose value is this line. The active handler
    /// filter can miss the quest that produced a menu prompt, so a fallback scan keeps the script
    /// family available as a hint.
    /// </summary>
    private unsafe List<string>? ReadAllLuaTextKeys(EventFramework* framework, string? englishText)
    {
        var wanted = MacroNormalizer.NormalizeKey(englishText);
        if (wanted.Length == 0)
            return null;

        var keys = new List<string>();
        foreach (var (_, pointer) in framework->EventHandlerModule.EventHandlerMap)
        {
            var handler = pointer.Value;
            if (handler is null)
                continue;

            var contentId = handler->Info.EventId.ContentId;
            var texts = TryReadLuaTexts(handler, contentId, readAll: false, englishText);
            if (texts is null)
                continue;

            foreach (var line in texts)
            {
                if (line.Key.Length > 0 && !keys.Contains(line.Key, StringComparer.OrdinalIgnoreCase))
                    keys.Add(line.Key);
            }
        }

        return keys.Count > 0 ? keys : null;
    }

    /// <summary>
    /// The script ids of the quests the player is tracking. A quest line can be shared by several
    /// quest scripts, and the tracked quest names the family the line must come from, even when
    /// the quest handler is not in the active handler map anymore.
    /// </summary>
    private unsafe List<string>? ReadTrackedQuestHints()
    {
        try
        {
            var ui = UIState.Instance();
            if (ui is null)
                return null;

            var quests = ui->QuestTodoList.Todo.TrackedQuests;
            if (quests.Count == 0)
                return null;

            var hints = new List<string>(quests.Count);
            foreach (ref var quest in quests.AsSpan())
            {
                var id = quest.Id.ToString();
                if (id.Length > 0 && !hints.Contains(id, StringComparer.OrdinalIgnoreCase))
                    hints.Add(id);
            }

            return hints.Count > 0 ? hints : null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Talk event reader: could not read the tracked quests; keeping the line unqualified.");
            return null;
        }
    }

    /// <summary>
    /// The chocobo taxi stand's unlock state, or null when no stand drives the line. A stand that
    /// is not unlocked yet greets the player with the "here you can use the service" line instead
    /// of the question, and the English text is the same for both.
    /// </summary>
    private unsafe bool? ReadChocoboTaxiStandUnlocked(List<TalkHandlerSnapshot> handlers)
    {
        if (SelectChocoboTaxiStandId(handlers) is not { } standId)
            return null;

        try
        {
            var uiState = UIState.Instance();
            if (uiState is null)
                return null;

            var unlocked = uiState->IsChocoboTaxiStandUnlocked(standId);
            _log.Debug("Talk event: chocobo taxi stand {RowId} is {State}.", standId, unlocked ? "unlocked" : "locked");
            return unlocked;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Talk event reader: could not read the taxi stand state; keeping the line unqualified.");
            return null;
        }
    }

    /// <summary>
    /// The one taxi stand row that drives the line, or null when the line has no stand state to
    /// read. Only a single stand handler with an id inside the unlock table counts.
    /// </summary>
    /// <remarks>
    /// The stand row id is the handler's <c>EventId.Id</c>: event ids pack as
    /// <c>(content &lt;&lt; 16) | entry</c> and the content is <c>0x12</c>, which is the sheet base.
    /// The bit array covers that whole sheet, so an id outside it is not read.
    /// </remarks>
    internal static uint? SelectChocoboTaxiStandId(IReadOnlyList<TalkHandlerSnapshot> handlers)
    {
        uint? standId = null;
        foreach (var handler in handlers)
        {
            if ((EventHandlerContent)handler.ContentId != EventHandlerContent.ChocoboTaxiStand)
                continue;

            if (standId is not null)
                return null;

            standId = handler.EventId;
        }

        if (standId is not { } id || id is < ChocoboTaxiStandFirstRow or > ChocoboTaxiStandLastRow)
            return null;

        return id;
    }

    private string _lastCutscenePath = string.Empty;

    /// <summary>The last sheet-row list logged, so a menu that retries does not repeat it.</summary>
    private string _lastRowLog = string.Empty;

    /// <summary>
    /// The scene number in a cutscene path, such as 6005 from
    /// <c>cut_scene/060/VoiceMan_06005</c>. Zero when the path carries no number, so the caller can
    /// fall back to the task's own id.
    /// </summary>
    internal static uint SceneNumberFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;

        var name = path.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        var match = SceneNumberPattern().Match(name);
        return match.Success && uint.TryParse(match.Groups[1].Value, out var scene) ? scene : 0;
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"(?:_|^)(\d{4,6})(?:\.|$)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SceneNumberPattern();

    /// <summary>First row of the <c>ChocoboTaxiStand</c> sheet, which is also its event content.</summary>
    private const uint ChocoboTaxiStandFirstRow = 0x120000;

    /// <summary>Last row of the <c>ChocoboTaxiStand</c> sheet: the base plus its last index.</summary>
    private const uint ChocoboTaxiStandLastRow = 0x120000 + 92;

    /// <summary>
    /// The cutscene the game is playing, or 0 when it plays none. A <c>VOICEMAN</c> dump key
    /// carries the same number, so one English line used in two cutscenes can be told apart.
    /// </summary>
    /// <remarks>
    /// The cutscene runs as a task under the event scene module. Only the task the game is playing
    /// counts; a list that holds another task type, or a structure that moved, leaves the id at 0
    /// and the lookup behaves as it did before.
    /// </remarks>
    private unsafe uint ReadCutsceneId(EventFramework* framework)
    {
        try
        {
            var tasks = framework->EventSceneModule.TaskManager.Tasks;

            for (var i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i].Value;
                if (task is null || task->Type != EventSceneTaskType.PlayCutScene)
                    continue;

                var play = (PlayCutSceneTask*)task;

                // The task's id is the cutscene row, which is not the number a VOICEMAN dump key
                // carries; the path is (cut_scene/060/VoiceMan_06005), and its number is. Keep the
                // id when the path says nothing.
                var path = play->CutscenePath.ToString();
                var scene = SceneNumberFromPath(path);
                if (scene != 0)
                {
                    if (!string.Equals(path, _lastCutscenePath, StringComparison.Ordinal))
                    {
                        _lastCutscenePath = path;
                        _log.Debug(
                            "Talk event: cutscene {Id} path {Path}, using scene {Scene}.",
                            play->CutsceneId, path, scene);
                    }

                    return scene;
                }

                // No path number: leave the scene at 0. A cutscene row id is not a VoiceMan scene
                // number, so passing it could only narrow to the wrong line, or to none.
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Talk event reader: could not read the cutscene id; using text fallback.");
        }

        return 0;
    }

    /// <summary>
    /// The row of the cutscene sheet that holds this subtitle. The cutscene path is
    /// also the sheet name, and row ids are the same in every language, so a match
    /// on the live text gives the artifact's Chinese line.
    /// </summary>
    private unsafe DialogueRowId? ReadCutsceneRow(EventFramework* framework, string? liveText)
    {
        if (_dataManager is null || string.IsNullOrWhiteSpace(liveText))
            return null;

        try
        {
            var path = ReadCutscenePath(framework);
            if (path.Length == 0)
                return null;

            var rowId = CutsceneRowFor(path, liveText);
            if (rowId is not { } found)
                return null;

            _log.Debug("Talk event: cutscene subtitle matched {Sheet} row {RowId}.", path, found);
            return new DialogueRowId(path, found, string.Empty);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Talk event reader: could not read the cutscene subtitle row.");
            return null;
        }
    }

    private unsafe string ReadCutscenePath(EventFramework* framework)
    {
        var tasks = framework->EventSceneModule.TaskManager.Tasks;

        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i].Value;
            if (task is null || task->Type != EventSceneTaskType.PlayCutScene)
                continue;

            return ((PlayCutSceneTask*)task)->CutscenePath.ToString();
        }

        return string.Empty;
    }

    private uint? CutsceneRowFor(string path, string liveText)
    {
        if (!string.Equals(path, _cutsceneSheet, StringComparison.Ordinal))
        {
            _cutsceneSheet = path;
            _cutsceneRows = LoadCutsceneRows(path);
        }

        var key = MacroNormalizer.NormalizeKey(liveText);
        if (key.Length == 0 || _cutsceneRows is null)
            return null;

        if (!_cutsceneRows.TryGetValue(key, out var rowId) || rowId == uint.MaxValue)
            return null;

        return rowId;
    }

    private Dictionary<string, uint>? LoadCutsceneRows(string path)
    {
        try
        {
            var sheet = _dataManager!.GetExcelSheet<RawRow>(name: path);
            if (sheet is null)
            {
                _log.Debug("Talk event: cutscene sheet {Sheet} is not loaded.", path);
                return null;
            }

            var rows = new List<(uint RowId, string Text)>(sheet.Count);
            foreach (var row in sheet)
                rows.Add((row.RowId, row.ReadStringColumn(1).ExtractText()));

            var index = BuildCutsceneRowIndex(rows);
            _log.Debug("Talk event: indexed {Count} cutscene rows for {Sheet}.", index.Count, path);
            return index;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Talk event reader: could not index cutscene sheet {Sheet}.", path);
            return null;
        }
    }

    /// <summary>
    /// Maps a subtitle to its row id. A text that two rows share is marked
    /// ambiguous with id 0, so it falls back to the scene path instead of guessing.
    /// </summary>
    internal static Dictionary<string, uint> BuildCutsceneRowIndex(IEnumerable<(uint RowId, string Text)> rows)
    {
        var index = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (var (rowId, text) in rows)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var key = MacroNormalizer.NormalizeKey(text);
            if (key.Length == 0)
                continue;

            // Row 0 is a real row. MaxValue marks a text that two rows share.
            if (!index.TryAdd(key, rowId))
                index[key] = uint.MaxValue;
        }

        return index;
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
            ReadLuaKey(handler, eventId.ContentId),
            luaTexts);
    }

    /// <summary>
    /// The Lua script name, such as <c>REGUNI5BASEOMICRON_00760</c>. It names the script that
    /// produced the dialog, so a selection option shared by many places can pick its line.
    /// </summary>
    private static unsafe string ReadLuaKey(EventHandler* handler, EventHandlerContent contentId)
    {
        if (contentId is not (EventHandlerContent.Quest
            or EventHandlerContent.CustomTalk
            or EventHandlerContent.GuildLeveAssignment))
        {
            return string.Empty;
        }

        return ((LuaEventHandler*)handler)->LuaKey.ToString();
    }

    private static void AddActiveId(HashSet<uint> ids, EventId eventId)
    {
        if (eventId.Id != 0)
            ids.Add(eventId.Id);
    }

    private static unsafe List<LuaTextLine>? TryReadLuaTexts(
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

        var result = new List<LuaTextLine>(readAll ? lua->LuaTexts.Count : 4);
        var wanted = readAll ? string.Empty : MacroNormalizer.NormalizeKey(englishText);

        foreach (var (rowId, text) in lua->LuaTexts)
        {
            var key = text.Key.ToString();
            var value = text.Value.ToString();
            var template = ScriptTextTemplate.Build(text.Value.AsSpan());

            // The product path selects the key from the game's own Lua text map.
            // Only spans the script marks as dynamic may vary; the rest of the
            // line still has to match exactly.
            if (!readAll)
            {
                if (value.Length == 0)
                    continue;

                if (!ScriptTextTemplate.Matches(template, wanted))
                    continue;
            }

            result.Add(new LuaTextLine(key, value, rowId, template));
        }

        return result;
    }

    /// <summary>
    /// The live template for each script key, so the store can tell whether the dump's
    /// wording is a branch of the line the game could have drawn. The template comes from
    /// the same raw value the key was read from.
    /// </summary>
    internal static Dictionary<string, string>? CollectScriptTemplates(
        IReadOnlyList<LuaTextLine> lines,
        Dictionary<string, string>? templates)
    {
        foreach (var line in lines)
        {
            if (line.Key.Length == 0 || line.Template.Length == 0)
                continue;

            templates ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            templates.TryAdd(line.Key, line.Template);
        }

        return templates;
    }

    private static void CollectScriptKeys(
        IReadOnlyList<LuaTextLine> luaTexts,
        List<string> scriptKeys)
    {
        foreach (var line in luaTexts)
        {
            if (line.Key.Length == 0 || line.Value.Length == 0)
                continue;

            // TryReadLuaTexts already kept only the values that match the live
            // line, so every key here is a candidate for this line.
            scriptKeys.Add(line.Key);
        }
    }
}

/// <summary>One line from a Lua handler's text map.</summary>
internal readonly record struct LuaTextLine(string Key, string Value, uint RowId, string Template);

/// <summary>One active event handler captured by the debug probe.</summary>
internal sealed record TalkHandlerSnapshot(
    uint EventId,
    int ContentId,
    ushort EntryId,
    short Scene,
    string LuaKey,
    IReadOnlyList<LuaTextLine>? LuaTexts);

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
