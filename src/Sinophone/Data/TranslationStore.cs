using Sinophone.Diagnostics;

namespace Sinophone.Data;

/// <summary>
/// Loads a generated translation artifact and resolves it against the live Lumina
/// sheets so English text read from the game can be mapped to Chinese text.
/// </summary>
/// <remarks>
/// Both variants are merged into a single lookup when fallback is enabled: the
/// configured variant is indexed first and always wins, and the other variant only
/// fills keys the primary variant does not cover.
/// </remarks>
public sealed partial class TranslationStore
{
    public const string ResourceCnSuffix = "Sinophone.cn.json.gz";
    public const string ResourceTcSuffix = "Sinophone.tc.json.gz";

    /// <summary>
    /// Version of the index this class builds. The index cache is keyed on it, so bump it when
    /// the entries change: a new sheet or field in <see cref="SheetCatalog"/>, a change to
    /// <see cref="MacroNormalizer.NormalizeKey"/>, the Addon templates, or the script indexes.
    /// </summary>
    /// <remarks>
    /// The assembly identity is deliberately not part of the key. It changes on every build, which
    /// threw the cache away after every plugin update and every dev reload, and a full reindex
    /// costs about 20 seconds.
    /// </remarks>
    public const int IndexFormatVersion = 11;

    private readonly DialogueTextIndex _dialogue = new();
    private readonly NameIndex _names = new();
    private readonly AddonIndex _addon = new();
    private readonly ScriptIndex _scripts = new();
    private readonly Dictionary<string, UiTextStyle> _styleLookup = new(StringComparer.Ordinal);

    /// <summary>
    /// Translations keyed by the sheet row the game says it is showing. This is the
    /// id-first path: it resolves a line even when the same English text appears in
    /// several rows. A sheet row is not a name and not a script line, so it stays here
    /// rather than in one of the indexes.
    /// </summary>
    private readonly Dictionary<(string Sheet, uint RowId, string Field), string[]> _rowLookup = new();

    private readonly Dictionary<(string Sheet, string Field), ISheetDescriptor> _descriptorByKey = new();

    private readonly Func<string, string> _displayResolver;

    /// <summary>
    /// The resolver used while indexing. The build can run on a worker task, so the
    /// plugin passes a copy that holds the player state read on the game thread.
    /// Lookups still use the live resolver.
    /// </summary>
    private readonly Func<string, string> _indexResolver;
    private readonly Func<string?>? _playerName;
    private readonly PluginLog _log;

    private TranslationStore(
        ChineseVariant primaryVariant,
        bool usedFallback,
        IReadOnlyList<ISheetDescriptor> descriptors,
        Func<string, string> displayResolver,
        Func<string, string>? indexResolver,
        Func<string?>? playerName,
        PluginLog log)
    {
        PrimaryVariant = primaryVariant;
        UsedFallback = usedFallback;
        _displayResolver = displayResolver;
        _indexResolver = indexResolver ?? displayResolver;
        _playerName = playerName;
        _log = log;

        foreach (var descriptor in descriptors)
            _descriptorByKey.TryAdd((descriptor.Name, descriptor.FieldName), descriptor);
    }

    /// <summary>The variant whose rows were indexed first.</summary>
    public ChineseVariant PrimaryVariant { get; }

    /// <summary>True when the other variant was merged in to cover missing keys.</summary>
    public bool UsedFallback { get; }

    public int TextEntryCount => _dialogue.Count;

    /// <summary>
    /// The English text of every sheet row, with the Chinese stored for it. Sorted, so a
    /// sample taken from it is the same on every run. The stored Chinese is the raw lookup
    /// value; the display may resolve conditionals or convert it. Used by the debug testing
    /// tab to sample lines, which then asks the store for the answer. The list is built per
    /// call, so a caller keeps the result instead of asking again.
    /// </summary>
    public IReadOnlyList<(string English, string Chinese)> SheetTextEntries => _dialogue.SheetEntries();

    public int NameEntryCount => _names.Count;

    /// <summary>Script-dump lines in the text lookup. Already counted in <see cref="TextEntryCount"/>.</summary>
    public int ScriptEntryCount => _dialogue.ScriptEntryCount;

    /// <summary>Script English keys that map to more than one Chinese line.</summary>
    internal int AmbiguousScriptKeyCount => _scripts.AmbiguousKeyCount;

    internal int AddonTemplateCount => _addon.TemplateCount;

    public bool TryGetDialogue(string? englishText, out string chineseText)
        => TryGetDialogue(englishText, default, null, out chineseText);

    /// <summary>
    /// Resolves one line, trying the game's ids before the English text. A missing
    /// id or an old artifact simply falls through to the text lookup, so every
    /// surface keeps the previous behaviour as a fallback.
    /// </summary>
    public bool TryGetDialogue(string? englishText, in DialogueContext context, out string chineseText)
        => TryGetDialogue(englishText, in context, null, out chineseText);

    /// <summary>
    /// Resolves one line, trying the game's ids before the English text.
    /// <paramref name="speakerName"/> is optional and only used to choose between
    /// script lines that share one English key, such as a generic "You bastard!".
    /// </summary>
    public bool TryGetDialogue(
        string? englishText,
        in DialogueContext context,
        string? speakerName,
        out string chineseText,
        FailureTrace? trace = null)
    {
        trace?.Step("lookup", "enter");

        if (TryGetDialogueById(englishText, context, out chineseText, trace))
            return true;

        chineseText = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
        {
            trace?.Step("text", "empty");
            return false;
        }

        trace?.Input("normalized", key);

        if (TryLookupAmbiguousScript(key, speakerName, in context, out chineseText, trace))
            return true;

        if (_dialogue.TryGetExact(key, out var stored))
        {
            trace?.Step("text.exact", "hit");
            chineseText = ResolveDisplay(stored);
            return true;
        }

        trace?.Step("text.exact", "miss");

        // Second chances, only after a miss so the common line stays one hit. The
        // game fills in the player's name where the dumps have a placeholder, and
        // script lines from the English dump hold one gender's wording.
        if (TryLookupPlayerNamePlaceholder(key, in context, speakerName, out chineseText))
        {
            trace?.Step("text.playerName", "hit", approximate: true);
            return true;
        }

        trace?.Step("text.playerName", "miss");

        if (TryLookupScriptTemplate(key, out chineseText))
        {
            trace?.Step("text.scriptTemplate", "hit", approximate: true);
            return true;
        }

        trace?.Step("text.scriptTemplate", "miss");

        if (!TryLookupBranchNeutral(key, out var match))
        {
            trace?.Step("text.branchNeutral", "miss");
            return false;
        }

        trace?.Step("text.branchNeutral", "hit", approximate: true);
        chineseText = ResolveDisplay(match);
        return true;
    }

    /// <summary>
    /// The cutscene number every script line that holds this English text belongs to, or 0
    /// when the line is not from exactly one cutscene. A selection dialog's options all come
    /// from the cutscene it sits in, so one option can name the scene for another.
    /// </summary>
    public bool TryGetCutsceneId(string? englishText, out uint cutsceneId)
    {
        cutsceneId = 0;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return false;

        return _scripts.TryGetCutsceneId(key, out cutsceneId);
    }

    /// <summary>
    /// Answers a key that several script lines share. The script index picks a candidate by the
    /// speaker, the cutscene, the chocobo stand state, or the script family; the store resolves
    /// the chosen line for display.
    /// </summary>
    private bool TryLookupAmbiguousScript(
        string key,
        string? speakerName,
        in DialogueContext context,
        out string chineseText,
        FailureTrace? trace = null)
    {
        if (!_scripts.TryLookupAmbiguous(
                key,
                speakerName,
                in context,
                _dialogue.Contains(key),
                _log,
                trace,
                out var stored))
        {
            chineseText = string.Empty;
            return false;
        }

        chineseText = ResolveDisplay(stored);
        return true;
    }

    /// <summary>The cutscene number in a <c>VOICEMAN</c> dump key. Zero for any other key.</summary>
    internal static uint CutsceneIdOf(string? scriptKey) => ScriptIndex.CutsceneIdOf(scriptKey);

    /// <summary>The script a dump key belongs to, without the line suffix.</summary>
    internal static string ScriptFamilyOf(string? key) => ScriptIndex.ScriptFamilyOf(key);

    /// <summary>Collapses every word the English dump may have resolved to another branch.</summary>
    private static string CollapseBranchWords(string key) => DialogueTextIndex.CollapseBranchWords(key);

    /// <summary>
    /// Retries a miss with the player's name put back as a dump placeholder. The
    /// game sometimes uses the full character name and sometimes only the first
    /// name, while the dump writes only the placeholder.
    /// </summary>
    /// <summary>The name the store was given, for a log line that has to explain a miss.</summary>
    private string DescribePlayerName()
    {
        var name = _playerName?.Invoke();
        return string.IsNullOrWhiteSpace(name) ? "<empty>" : name;
    }

    /// <summary>
    /// The key with the player's name put back as the dump placeholder, or empty when no name is
    /// known or the key does not hold it.
    /// </summary>
    private string PlayerNamePlaceholderKey(string key)
    {
        var fullName = _playerName?.Invoke()?.Trim();
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var withFullName = RestorePlaceholder(key, fullName);
        if (withFullName.Length > 0)
            return withFullName;

        var separator = fullName.IndexOf(' ');
        return separator > 0 ? RestorePlaceholder(key, fullName[..separator]) : string.Empty;

        static string RestorePlaceholder(string source, string name)
        {
            if (!source.Contains(name, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var replaced = MacroNormalizer.RestoreSinglePlayerNamePlaceholder(source, name);
            return ReferenceEquals(replaced, source) ? string.Empty : replaced;
        }
    }

    private bool TryLookupPlayerNamePlaceholder(
        string key,
        in DialogueContext context,
        string? speakerName,
        out string chineseText)
    {
        chineseText = string.Empty;

        var fullName = _playerName?.Invoke()?.Trim();
        if (string.IsNullOrEmpty(fullName))
            return false;

        if (TryPlayerNameVariant(key, fullName, in context, speakerName, out chineseText))
            return true;

        var separator = fullName.IndexOf(' ');
        if (separator <= 0)
            return false;

        var firstName = fullName[..separator];
        if (string.Equals(firstName, fullName, StringComparison.Ordinal))
            return false;

        if (TryPlayerNameVariant(key, firstName, in context, speakerName, out chineseText))
            return true;

        // A line can write both forms: the dump puts "Player Player" where the game shows the full
        // name and "Player" where it shows the given name, as in "Player Player is Player of the
        // Scions". One name at a time cannot reduce that to the stored key, so put the full name in
        // the double placeholder first and the given name in what is left.
        var combined = MacroNormalizer.RestoreSinglePlayerNamePlaceholder(
            MacroNormalizer.RestorePlayerNamePlaceholder(key, fullName),
            firstName);

        return !ReferenceEquals(combined, key)
               && TryLookupWithGenderFallback(combined, in context, speakerName, out chineseText);
    }

    private bool TryPlayerNameVariant(
        string key,
        string playerName,
        in DialogueContext context,
        string? speakerName,
        out string chineseText)
    {
        chineseText = string.Empty;

        var doublePlaceholder = MacroNormalizer.RestorePlayerNamePlaceholder(key, playerName);
        if (!ReferenceEquals(doublePlaceholder, key)
            && TryLookupWithGenderFallback(doublePlaceholder, in context, speakerName, out chineseText))
        {
            return true;
        }

        var singlePlaceholder = MacroNormalizer.RestoreSinglePlayerNamePlaceholder(key, playerName);
        return !ReferenceEquals(singlePlaceholder, key)
               && !string.Equals(singlePlaceholder, doublePlaceholder, StringComparison.Ordinal)
               && TryLookupWithGenderFallback(singlePlaceholder, in context, speakerName, out chineseText);
    }

    private bool TryLookupScriptTemplate(string key, out string chineseText)
        => _scripts.TryGetTemplate(key, out chineseText);

    private bool TryLookupWithGenderFallback(
        string key,
        in DialogueContext context,
        string? speakerName,
        out string chineseText)
    {
        chineseText = string.Empty;

        if (TryLookupAmbiguousScript(key, speakerName, in context, out chineseText))
            return true;

        if (_dialogue.TryGetExact(key, out var stored))
        {
            chineseText = ResolveDisplay(stored);
            return true;
        }

        if (!TryLookupBranchNeutral(key, out var match))
            return false;

        chineseText = ResolveDisplay(match);
        return true;
    }

    /// <summary>
    /// True when two English lines are close enough to be the same dump line. The game can
    /// substitute a race, class, or other player word that the dump resolved to one value; those
    /// lines share a long prefix or suffix. A reused menu key names a completely different line
    /// and shares neither.
    /// </summary>
    private static bool IsCloseEnough(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        var length = Math.Min(a.Length, b.Length);
        if (length < 12)
            return false;

        var prefix = 0;
        while (prefix < length && a[prefix] == b[prefix])
            prefix++;

        if (prefix >= 12)
            return true;

        var suffix = 0;
        while (suffix < length && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
            suffix++;

        return suffix >= 12;
    }

    private bool TryGetDialogueById(
        string? englishText,
        in DialogueContext context,
        out string chineseText,
        FailureTrace? trace = null)
    {
        chineseText = string.Empty;

        // A cutscene subtitle or another script line the game names by sheet row.
        if (context.ScriptRow is { } scriptRow)
        {
            if (_scripts.TryGetRowText(scriptRow.Sheet, scriptRow.RowId, out var rowText))
            {
                if (trace is not null)
                    trace.Step("id.scriptRow", "hit", $"{scriptRow.Sheet} row {scriptRow.RowId}");
                chineseText = ResolveDisplay(rowText);
                _log.Debug(
                    "Dialogue matched by {Sheet} row {RowId}.",
                    scriptRow.Sheet, scriptRow.RowId);
                return true;
            }

            if (trace is not null)
                trace.Step("id.scriptRow", "miss", $"{scriptRow.Sheet} row {scriptRow.RowId}");
        }

        if (context.ScriptKeys is not null)
        {
            var liveKey = MacroNormalizer.NormalizeKey(englishText);
            foreach (var scriptKey in context.ScriptKeys)
            {
                if (string.IsNullOrWhiteSpace(scriptKey))
                    continue;

                var trimmed = scriptKey.Trim();
                if (!_scripts.TryGetText(trimmed, out var script))
                {
                    trace?.Step("id.scriptKey", "not-indexed", trimmed);
                    continue;
                }

                // The live Lua text map can reuse a key for another line. The live row
                // id is the same id the dump wrote for the line, so a match needs no
                // text comparison and works in any client language.
                if (context.ScriptRowIds is not null
                    && context.ScriptRowIds.TryGetValue(trimmed, out var liveRowId)
                    && _scripts.TryGetRowId(trimmed, out var artifactRowId))
                {
                    if (artifactRowId != liveRowId)
                    {
                        _log.Debug(
                            "Dialogue ignored script key {ScriptKey}: the live row {LiveRow} is not {DumpRow}.",
                            trimmed, liveRowId, artifactRowId);
                        if (trace is not null)
                        {
                            trace.Step(
                                "id.scriptKey",
                                "row-mismatch",
                                $"{trimmed}: live row {liveRowId} vs dump row {artifactRowId}");
                        }

                        continue;
                    }

                    if (trace is not null)
                        trace.Step("id.scriptKey", "hit", $"{trimmed} row {liveRowId}");
                    chineseText = ResolveDisplay(script);
                    _log.Debug(
                        "Dialogue matched by script key {ScriptKey} and row {RowId}.",
                        scriptKey, liveRowId);
                    return true;
                }

                // Without a live row id, trust the key only when the dump paired it with
                // the same English line. Put the player name back and collapse gender/time
                // wording first, because those differences are expected.
                if (_scripts.TryGetPairedEnglish(trimmed, out var pairedKey))
                {
                    var liveForCompare = PlayerNamePlaceholderKey(liveKey);
                    if (liveForCompare.Length == 0)
                        liveForCompare = liveKey;

                    if (!string.Equals(
                            CollapseBranchWords(liveForCompare),
                            CollapseBranchWords(pairedKey),
                            StringComparison.Ordinal)
                        && !IsCloseEnough(liveForCompare, pairedKey))
                    {
                        _log.Debug(
                            "Dialogue ignored script key {ScriptKey}: it names {Paired}, not {Live}.",
                            trimmed, pairedKey, liveKey);
                        if (trace is not null)
                        {
                            trace.Step(
                                "id.scriptKey",
                                "rejected",
                                $"{trimmed}: live \"{liveForCompare}\" vs dump \"{pairedKey}\"");
                        }

                        continue;
                    }
                }

                trace?.Step("id.scriptKey", "hit", trimmed);
                chineseText = ResolveDisplay(script);
                _log.Debug("Dialogue matched by script key {ScriptKey}.", scriptKey);
                return true;
            }
        }

        if (context.Row is not { } row)
        {
            trace?.Step("id.sheetRow", "none");
            return false;
        }

        if (!_rowLookup.TryGetValue((row.Sheet, row.RowId, row.Field), out var slots))
        {
            if (trace is not null)
                trace.Step("id.sheetRow", "missing", $"{row.Sheet} row {row.RowId} {row.Field}");
            return false;
        }

        if (row.Slot >= 0)
        {
            if (row.Slot >= slots.Length || string.IsNullOrEmpty(slots[row.Slot]))
            {
                if (trace is not null)
                    trace.Step("id.sheetRow", "empty-slot", $"{row.Sheet} row {row.RowId} slot {row.Slot}");
                return false;
            }

            trace?.Step("id.sheetRow", "hit", $"{row.Sheet} row {row.RowId} slot {row.Slot}");
            chineseText = ResolveDisplay(slots[row.Slot]);
            _log.Debug("Dialogue matched by {Sheet} row {RowId} slot {Slot}.",
                row.Sheet, row.RowId, row.Slot);
            return true;
        }

        if (TrySelectRowSlot(row, englishText, slots, out var selected, out var compared, trace))
        {
            if (trace is not null)
                trace.Step("id.sheetRow", "hit", $"{row.Sheet} row {row.RowId} slot {selected}");
            chineseText = ResolveDisplay(slots[selected]);
            _log.Debug("Dialogue matched by {Sheet} row {RowId} after selecting slot {Slot}.",
                row.Sheet, row.RowId, selected);
            return true;
        }

        // The row's own English was readable and holds no slot for this line, so
        // the row is not where the line came from. Guessing the only translated
        // slot here showed one page's Chinese on the next page as well.
        if (compared)
        {
            _log.Debug("Dialogue did not match any slot of {Sheet} row {RowId}; using the text fallback.",
                row.Sheet, row.RowId);
            if (trace is not null)
            {
                trace.Step(
                    "id.sheetRow",
                    "slot-mismatch",
                    $"{row.Sheet} row {row.RowId} {row.Field}");
            }

            return false;
        }

        // A row with one translated slot needs no text to choose between slots.
        var onlySlot = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            if (string.IsNullOrEmpty(slots[i]))
                continue;

            if (onlySlot >= 0)
            {
                if (trace is not null)
                    trace.Step("id.sheetRow", "several-slots", $"{row.Sheet} row {row.RowId}");
                return false;
            }

            onlySlot = i;
        }

        if (onlySlot < 0)
        {
            if (trace is not null)
                trace.Step("id.sheetRow", "no-slot", $"{row.Sheet} row {row.RowId}");
            return false;
        }

        trace?.Step("id.sheetRow", "only-slot", $"{row.Sheet} row {row.RowId} slot {onlySlot}", approximate: true);
        chineseText = ResolveDisplay(slots[onlySlot]);
        _log.Debug("Dialogue matched by {Sheet} row {RowId}, the only translated slot.",
            row.Sheet, row.RowId);
        return true;
    }

    /// <summary>
    /// Picks the translated slot whose own English matches the line on screen.
    /// </summary>
    /// <param name="compared">
    /// True when the row's own English slots were read and compared with the line.
    /// False when there was nothing to compare against, so a single translated slot
    /// is still a safe answer.
    /// </param>
    private bool TrySelectRowSlot(
        DialogueRowId row,
        string? englishText,
        string[] slots,
        out int selected,
        out bool compared,
        FailureTrace? trace = null)
    {
        selected = -1;
        compared = false;

        if (string.IsNullOrWhiteSpace(englishText)
            || !_descriptorByKey.TryGetValue((row.Sheet, row.Field), out var descriptor)
            || !descriptor.TryExtract(row.RowId, out var englishSlots))
        {
            trace?.Step("id.sheetRow.compare", "no-live-text");
            return false;
        }

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
        {
            trace?.Step("id.sheetRow.compare", "empty-key");
            return false;
        }

        // The sheet writes "Player" where the game inserts the character's name, so the live line
        // never equals the sheet's wording. Put the placeholder back before comparing, or every row
        // that names the player looks like a slot that does not match.
        var nameKey = PlayerNamePlaceholderKey(key);
        if (nameKey.Length == 0)
        {
            _log.Debug(
                "Row {Sheet} {RowId}: the slot compare had no player name to put back ({Name}), " +
                "so the sheet's Player placeholder could not match.",
                row.Sheet, row.RowId, DescribePlayerName());
        }

        compared = true;

        for (var i = 0; i < slots.Length && i < englishSlots.Count; i++)
        {
            if (string.IsNullOrEmpty(slots[i]))
                continue;

            foreach (var variant in englishSlots[i])
            {
                var sheetKey = MacroNormalizer.NormalizeKey(variant);
                if (sheetKey != key && (nameKey.Length == 0 || sheetKey != nameKey))
                    continue;

                selected = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches a script line that the English dump wrote for another branch of a
    /// gender or time conditional.
    /// </summary>
    private bool TryLookupBranchNeutral(string key, out string stored)
    {
        if (!_dialogue.TryGetBranchNeutral(key, out stored))
            return false;

        _log.Debug("Line matched after collapsing the branch words.");
        return true;
    }

    /// <summary>
    /// <summary>
    /// Looks up a short Addon-sheet label such as "Yes.", "No.", or "Skip cutscene?".
    /// The separate lookup keeps a same-text NpcYell or script row from winning.
    /// </summary>
    public bool TryGetAddonLabel(string? englishText, out string chineseText, FailureTrace? trace = null)
    {
        chineseText = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
        {
            trace?.Step("addon.label", "empty");
            return false;
        }

        if (!_addon.TryGetLabel(key, out var stored))
        {
            trace?.Step("addon.label", "miss");
            return false;
        }

        trace?.Step("addon.label", "hit");
        chineseText = ResolveDisplay(stored);
        return true;
    }

    /// <summary>
    /// Resolves an Addon-sheet prompt that contains runtime placeholders.
    /// </summary>
    public bool TryGetAddonText(string? englishText, out string chineseText)
        => TryGetAddonText(englishText, out chineseText, out _, out _);

    public bool TryGetAddonText(
        string? englishText,
        out string chineseText,
        out string[] captures)
        => TryGetAddonText(englishText, out chineseText, out captures, out _);

    /// <summary>
    /// Resolves an Addon-sheet prompt and returns the captured values plus the
    /// IntegerParameter / StringParameter indices that produced them.
    /// </summary>
    /// <summary>
    /// True when an Addon row in the data could hold this prompt: some template's literal prefix
    /// matches the start of the text. False means the dumps have no row for it at all, which is
    /// what a live client newer than the data looks like.
    /// </summary>
    public bool HasAddonPromptLike(string? englishText, int prefixLength = 8)
        => _addon.HasPromptLike(MacroNormalizer.NormalizeKey(englishText), prefixLength);

    public bool TryGetAddonText(
        string? englishText,
        out string chineseText,
        out string[] captures,
        out int[]? parameterIndices)
        => TryGetAddonText(englishText, out chineseText, out captures, out parameterIndices, out _);

    /// <summary>
    /// Resolves an Addon-sheet prompt and returns the converted Chinese macro string,
    /// when the artifact carried one for that row.
    /// </summary>
    public bool TryGetAddonText(
        string? englishText,
        out string chineseText,
        out string[] captures,
        out int[]? parameterIndices,
        out string? macroTemplate,
        FailureTrace? trace = null)
    {
        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
        {
            chineseText = string.Empty;
            captures = [];
            parameterIndices = null;
            macroTemplate = null;
            trace?.Step("addon.template", "empty");
            return false;
        }

        return _addon.TryGetText(key, trace, out chineseText, out captures, out parameterIndices, out macroTemplate);
    }

    /// <summary>True when the stored line carried UI text colors.</summary>
    public bool TryGetStyle(string? displayText, out UiTextStyle style)
    {
        style = default;
        return !string.IsNullOrEmpty(displayText) && _styleLookup.TryGetValue(displayText, out style);
    }

    /// <summary>
    /// Resolves a name by the sheet row the game names, without reading the live
    /// text. The item tooltip knows its row id, so this works on every client
    /// language and cannot pick another row that shares the same name.
    /// </summary>
    public bool TryGetNameById(
        string sheet,
        uint rowId,
        out string chineseName,
        FailureTrace? trace = null)
    {
        chineseName = string.Empty;

        if (!_names.TryGetRow(sheet, rowId, out var stored))
        {
            if (trace is not null)
                trace.Step("name.id", "miss", $"{sheet} row {rowId}");
            return false;
        }

        trace?.Step("name.id", "hit", $"{sheet} row {rowId}");
        chineseName = ResolveDisplay(stored);
        return chineseName.Length > 0;
    }

    public bool TryGetName(string? englishName, out string chineseName, FailureTrace? trace = null)
    {
        chineseName = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishName);
        if (key.Length == 0)
        {
            trace?.Step("name", "empty");
            return false;
        }

        trace?.Input("name", key);

        var match = _names.Match(key, out var stored, out var matchedKey);

        if (match == NameMatch.Exact)
        {
            trace?.Step("name.exact", "hit");
            chineseName = ResolveDisplay(stored);
            return true;
        }

        if (match == NameMatch.None)
        {
            trace?.Step("name.exact", "miss");
            return false;
        }

        trace?.Step("name.trimmed", "hit", matchedKey, approximate: true);
        chineseName = ResolveDisplay(stored);
        return true;
    }

    /// <summary>
    /// The speaker alias attached to this exact script line, if it has one. A generic alias such
    /// as "All" is used by several different groups with different Chinese labels, so the line's
    /// own alias is the only one that names the speaker correctly.
    /// </summary>
    public bool TryGetLineSpeakerAlias(string? englishText, out string englishName, out string chineseName)
    {
        englishName = string.Empty;
        chineseName = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0 || !_dialogue.TryGetAlias(key, out var alias))
            return false;

        englishName = alias.English;
        chineseName = alias.Chinese;
        return chineseName.Length > 0;
    }

    /// <summary>
    /// Resolves text now when nothing in it depends on the player, so a lookup stays a
    /// dictionary hit. Conditionals and <c>ObjectParameter</c> text are kept raw and
    /// resolved per lookup. Such lines are rare.
    /// </summary>
    private string PrepareDisplay(string rawText)
        => NeedsLateResolution(rawText) ? rawText : ResolveWith(_indexResolver, rawText);

    private string ResolveDisplay(string stored)
        => NeedsLateResolution(stored) ? ResolveWith(_displayResolver, stored) : stored;

    private string ResolveWith(Func<string, string> resolver, string text)
        => resolver(ResolveSheetMacros(text));

    private string ResolveSheetMacros(string text)
    {
        if (!text.Contains("<Sheet(", StringComparison.Ordinal))
            return text;

        return Regex.Replace(text, @"<Sheet\(([^,]+),(\d+),\d+\)\s*/>", match =>
        {
            if (!uint.TryParse(match.Groups[2].Value, out var rowId))
                return match.Value;

            var sheet = match.Groups[1].Value.Trim();
            var lookupSheet = sheet switch
            {
                "EObj" => "EObjName",
                "ENpc" => "ENpcResident",
                "BNpc" => "BNpcName",
                _ => sheet,
            };

            return _names.TryGetRow(lookupSheet, rowId, out var name)
                ? name
                : match.Value;
        });
    }

    /// <summary>
    /// Text whose display depends on the player, so it cannot be resolved at load.
    /// Both checks are cheaper than the work they guard against.
    /// </summary>
    private static bool NeedsLateResolution(string text)
        => ConditionalText.HasConditional(text) || MacroNormalizer.HasObjectParameter(text);
}
