using System.Diagnostics;

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
    public const int IndexFormatVersion = 5;

    private readonly Dictionary<string, string> _textLookup = new(StringComparer.Ordinal);
    // Names are matched without case: the sheet stores generic NPC names in lower
    // case ("skywatcher") and the game shows them capitalised ("Skywatcher").
    private readonly Dictionary<string, string> _nameLookup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Script lines under a key with the branch words collapsed: pronouns, nouns of
    /// address, and the time of day. Only tried after an exact miss, because the
    /// English dump stores one branch.
    /// </summary>
    private readonly Dictionary<string, string> _branchNeutralLookup = new(StringComparer.Ordinal);

    /// <summary>
    /// Collapsed keys that two script lines share with different Chinese. The
    /// collapsed key cannot tell the lines apart, so it is not used at all.
    /// </summary>
    private readonly HashSet<string> _ambiguousBranchKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Addon-sheet prompts with UNKNOWN placeholders, matched as templates. The
    /// game fills those values at runtime, so an exact lookup cannot match them.
    /// </summary>
    private readonly Dictionary<string, string> _addonLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiTextStyle> _styleLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Sheet, uint RowId), string> _nameRowLookup = new();
    private readonly List<(string Template, string Chinese, int[]? ParameterIndices)> _addonTemplates = [];
    private readonly List<(string Template, string Chinese)> _scriptTemplates = [];
    private readonly HashSet<string> _addonTemplateKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Translations keyed by the sheet row the game says it is showing. This is the
    /// id-first path: it resolves a line even when the same English text appears in
    /// several rows.
    /// </summary>
    private readonly Dictionary<(string Sheet, uint RowId, string Field), string[]> _rowLookup = new();

    /// <summary>Script translations keyed by the dump's text key, when the game exposes it.</summary>
    private readonly Dictionary<string, string> _scriptKeyLookup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Script lines that share one English key but have different Chinese, such as the
    /// three different "You bastard!" lines. The key alone cannot tell them apart, so
    /// the text lookup never answers from it. The candidates stay here so the speaker
    /// name can still pick one.
    /// </summary>
    private readonly Dictionary<string, List<ScriptCandidate>> _ambiguousScriptKeys = new(StringComparer.Ordinal);

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

    private int _scriptEntryCount;

    /// <summary>One line of a script key that more than one Chinese line shares.</summary>
    /// <remarks>
    /// <see cref="Keys"/> holds every dump key whose Chinese is <see cref="Display"/>, because two
    /// keys with the same Chinese are one candidate. The speaker token, the cutscene number, and
    /// the <c>UNLOCK</c> marker are all read from those keys.
    /// </remarks>
    private readonly record struct ScriptCandidate(string Display, string[] SpeakerTokens, string[] Keys)
    {
        /// <summary>Adds another dump key that holds the same Chinese line.</summary>
        public ScriptCandidate WithKey(string scriptKey, string[] tokens)
        {
            var knownKeys = Keys;
            var knownTokens = SpeakerTokens;

            if (Array.IndexOf(knownKeys, scriptKey) >= 0)
                return this;

            var added = Array.FindAll(tokens, token => Array.IndexOf(knownTokens, token) < 0);

            string[] keys = knownKeys.Length == 0 ? [scriptKey] : [.. knownKeys, scriptKey];
            string[] allTokens;
            if (added.Length == 0)
            {
                allTokens = knownTokens;
            }
            else
            {
                allTokens = [.. knownTokens, .. added];
            }

            return this with { Keys = keys, SpeakerTokens = allTokens };
        }
    }

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

    public int TextEntryCount => _textLookup.Count;

    public int NameEntryCount => _nameLookup.Count;

    /// <summary>Script-dump lines in the text lookup. Already counted in <see cref="TextEntryCount"/>.</summary>
    public int ScriptEntryCount => _scriptEntryCount;

    /// <summary>Script English keys that map to more than one Chinese line.</summary>
    internal int AmbiguousScriptKeyCount => _ambiguousScriptKeys.Count;

    internal int AddonTemplateCount => _addonTemplates.Count;

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
        out string chineseText)
    {
        if (TryGetDialogueById(englishText, context, out chineseText))
            return true;

        chineseText = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return false;

        if (TryLookupAmbiguousScript(key, speakerName, in context, out chineseText))
            return true;

        if (_textLookup.TryGetValue(key, out var stored))
        {
            chineseText = ResolveDisplay(stored);
            return true;
        }

        // Second chances, only after a miss so the common line stays one hit. The
        // game fills in the player's name where the dumps have a placeholder, and
        // script lines from the English dump hold one gender's wording.
        if (TryLookupPlayerNamePlaceholder(key, out chineseText))
            return true;

        if (TryLookupScriptTemplate(key, out chineseText))
            return true;

        if (!TryLookupBranchNeutral(key, out var match))
            return false;

        chineseText = ResolveDisplay(match);
        return true;
    }

    /// <summary>
    /// Answers a key that several script lines share. The speaker name narrows the candidates by
    /// the token in each dump key, such as <c>..._JULLUS</c>; a cutscene narrows them by the
    /// cutscene number in a <c>VOICEMAN</c> key. A key with no name and no cutscene, or with more
    /// than one candidate left, stays unanswered rather than guessing.
    /// </summary>
    private bool TryLookupAmbiguousScript(
        string key,
        string? speakerName,
        in DialogueContext context,
        out string chineseText)
    {
        chineseText = string.Empty;

        if (!_ambiguousScriptKeys.TryGetValue(key, out var candidates))
            return false;

        var narrowed = candidates;

        var words = SpeakerWords(speakerName);
        if (words.Length > 0)
        {
            var spoken = candidates.FindAll(candidate => CandidateIsSpokenBy(candidate, words));
            if (spoken.Count > 0)
                narrowed = spoken;
        }

        if (narrowed.Count == 1)
        {
            chineseText = ResolveDisplay(narrowed[0].Display);
            _log.Debug(
                "Dialogue matched one of {Count} script lines that share a key, by speaker {Speaker}: {Keys}.",
                candidates.Count, speakerName ?? string.Empty, Describe(narrowed[0]));
            return true;
        }

        var cutsceneId = context.CutsceneId;
        if (cutsceneId != 0)
        {
            var match = OnlyMatch(narrowed, candidate => HoldsCutscene(candidate, cutsceneId));
            if (match is not null)
            {
                chineseText = ResolveDisplay(match.Value.Display);
                _log.Debug(
                    "Dialogue matched the cutscene {CutsceneId} line out of {Count} script lines " +
                    "that share a key.",
                    cutsceneId, narrowed.Count);
                return true;
            }
        }

        if (MacroNormalizer.IsOnlySoundMarkers(key))
        {
            chineseText = ResolveDisplay(narrowed[0].Display);
            _log.Debug(
                "Dialogue picked one of {Count} lines that hold only a sound ({Key}).",
                narrowed.Count, key);
            return true;
        }

        if (context.ChocoboTaxiStandUnlocked is { } standUnlocked)
        {
            var match = OnlyMatch(narrowed, candidate => HoldsUnlockLine(candidate) != standUnlocked);
            if (match is not null)
            {
                chineseText = ResolveDisplay(match.Value.Display);
                _log.Debug(
                    "Dialogue picked the {State} chocobo taxi greeting out of {Count} script lines " +
                    "that share a key.",
                    standUnlocked ? "unlocked" : "locked", narrowed.Count);
                return true;
            }
        }

        // The line stays English. Name the dump keys, because a report of a missing line cannot
        // say which scenes collided from a count alone.
        _log.Debug(
            "Dialogue line has {Count} script candidates and none was chosen ({Speaker}, cutscene " +
            "{CutsceneId}): {Keys}.",
            candidates.Count,
            speakerName ?? string.Empty,
            cutsceneId,
            string.Join(", ", candidates.Select(Describe)));

        return false;
    }

    /// <summary>The dump keys behind one candidate, for the debug log.</summary>
    private static string Describe(ScriptCandidate candidate)
        => candidate.Keys.Length == 0 ? "(no key)" : string.Join(" / ", candidate.Keys);

    /// <summary>
    /// True when the candidate's dump key marks a line for a feature that has just unlocked, such
    /// as <c>TEXT_CHOCOBOTAXISTAND_00000_CHOCOBOTAXI_UNLOCK_01</c>.
    /// </summary>
    private static bool HoldsUnlockLine(ScriptCandidate candidate)
        => Array.Exists(candidate.Keys, key => key.Contains("_UNLOCK", StringComparison.OrdinalIgnoreCase));

    /// <summary>The one candidate that matches, or null when none or several do.</summary>
    private static ScriptCandidate? OnlyMatch(List<ScriptCandidate> candidates, Predicate<ScriptCandidate> matches)
    {
        ScriptCandidate? match = null;

        foreach (var candidate in candidates)
        {
            if (!matches(candidate))
                continue;

            if (match is not null)
                return null;

            match = candidate;
        }

        return match;
    }

    /// <summary>True when any of the candidate's dump keys is the line for this cutscene.</summary>
    private static bool HoldsCutscene(ScriptCandidate candidate, uint cutsceneId)
        => Array.Exists(candidate.Keys, key => CutsceneIdOf(key) == cutsceneId);

    /// <summary>
    /// The cutscene number in a <c>VOICEMAN</c> dump key: <c>TEXT_VOICEMAN_06005_003840_THANCRED</c>
    /// is cutscene 6005. Zero for any other key, so the cutscene step does not apply to it.
    /// </summary>
    internal static uint CutsceneIdOf(string? scriptKey)
    {
        const string prefix = "TEXT_VOICEMAN_";

        if (scriptKey is null || !scriptKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        var start = prefix.Length;
        var end = scriptKey.IndexOf('_', start);
        if (end <= start)
            return 0;

        return uint.TryParse(scriptKey.AsSpan(start, end - start), out var id) ? id : 0;
    }

    /// <summary>
    /// True when the dump key names the speaker, such as <c>TEXT_VOICEMAN_06005_000910_JULLUS</c>
    /// for "Jullus". Keys are matched on whole tokens so a scene name cannot match.
    /// </summary>
    private static bool CandidateIsSpokenBy(ScriptCandidate candidate, string[] speakerWords)
    {
        foreach (var token in candidate.SpeakerTokens)
        {
            foreach (var word in speakerWords)
            {
                if (string.Equals(token, word, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The words of a speaker name that can appear in a dump key. Titles and short
    /// words are dropped; "Jullus" and "Emet-Selch" both survive.
    /// </summary>
    private static string[] SpeakerWords(string? speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return [];

        var words = new List<string>(4);
        var whole = new StringBuilder(speakerName.Length);

        var start = -1;
        for (var i = 0; i <= speakerName.Length; i++)
        {
            var isLetter = i < speakerName.Length && char.IsLetter(speakerName[i]);
            if (isLetter)
            {
                whole.Append(char.ToUpperInvariant(speakerName[i]));

                if (start < 0)
                    start = i;

                continue;
            }

            if (start < 0)
                continue;

            if (i - start >= 4)
                words.Add(speakerName[start..i].ToUpperInvariant());

            start = -1;
        }

        // A dump key writes a two-part name as one token: "G'raha Tia" is GRAHATIA. The
        // apostrophe and the space are not part of the name, so the whole name without them is
        // a candidate too.
        var fullName = whole.ToString();
        if (fullName.Length >= 4 && !words.Contains(fullName))
            words.Add(fullName);

        return words.Count == 0 ? [] : words.ToArray();
    }

    /// <summary>
    /// The words in a script key that can name a speaker. Digits and short tokens such
    /// as <c>TEXT</c> are dropped so only real names remain.
    /// </summary>
    private static string[] KeySpeakerTokens(string? scriptKey)
    {
        if (string.IsNullOrEmpty(scriptKey))
            return [];

        var tokens = new List<string>(4);

        var start = -1;
        for (var i = 0; i <= scriptKey.Length; i++)
        {
            var isLetter = i < scriptKey.Length && char.IsLetter(scriptKey[i]);
            if (isLetter)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start < 0)
                continue;

            var length = i - start;
            if (length >= 4)
            {
                var token = scriptKey[start..i].ToUpperInvariant();
                if (!string.Equals(token, "TEXT", StringComparison.Ordinal) && !tokens.Contains(token))
                    tokens.Add(token);
            }

            start = -1;
        }

        return tokens.Count == 0 ? [] : tokens.ToArray();
    }

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

    private bool TryLookupPlayerNamePlaceholder(string key, out string chineseText)
    {
        chineseText = string.Empty;

        var fullName = _playerName?.Invoke()?.Trim();
        if (string.IsNullOrEmpty(fullName))
            return false;

        if (TryPlayerNameVariant(key, fullName, out chineseText))
            return true;

        var separator = fullName.IndexOf(' ');
        if (separator <= 0)
            return false;

        var firstName = fullName[..separator];
        if (string.Equals(firstName, fullName, StringComparison.Ordinal))
            return false;

        if (TryPlayerNameVariant(key, firstName, out chineseText))
            return true;

        // A line can write both forms: the dump puts "Player Player" where the game shows the full
        // name and "Player" where it shows the given name, as in "Player Player is Player of the
        // Scions". One name at a time cannot reduce that to the stored key, so put the full name in
        // the double placeholder first and the given name in what is left.
        var combined = MacroNormalizer.RestoreSinglePlayerNamePlaceholder(
            MacroNormalizer.RestorePlayerNamePlaceholder(key, fullName),
            firstName);

        return !ReferenceEquals(combined, key)
               && TryLookupWithGenderFallback(combined, out chineseText);
    }

    private bool TryPlayerNameVariant(string key, string playerName, out string chineseText)
    {
        chineseText = string.Empty;

        var doublePlaceholder = MacroNormalizer.RestorePlayerNamePlaceholder(key, playerName);
        if (!ReferenceEquals(doublePlaceholder, key)
            && TryLookupWithGenderFallback(doublePlaceholder, out chineseText))
        {
            return true;
        }

        var singlePlaceholder = MacroNormalizer.RestoreSinglePlayerNamePlaceholder(key, playerName);
        return !ReferenceEquals(singlePlaceholder, key)
               && !string.Equals(singlePlaceholder, doublePlaceholder, StringComparison.Ordinal)
               && TryLookupWithGenderFallback(singlePlaceholder, out chineseText);
    }

    private bool TryLookupScriptTemplate(string key, out string chineseText)
    {
        chineseText = string.Empty;

        foreach (var (template, chinese) in _scriptTemplates)
        {
            if (!ScriptTextTemplate.TryMatch(template, key, out _))
                continue;

            chineseText = chinese;
            return true;
        }

        return false;
    }

    private bool TryLookupWithGenderFallback(string key, out string chineseText)
    {
        chineseText = string.Empty;

        if (_textLookup.TryGetValue(key, out var stored))
        {
            chineseText = ResolveDisplay(stored);
            return true;
        }

        if (!TryLookupBranchNeutral(key, out var match))
            return false;

        chineseText = ResolveDisplay(match);
        return true;
    }

    private bool TryGetDialogueById(string? englishText, in DialogueContext context, out string chineseText)
    {
        chineseText = string.Empty;

        if (context.ScriptKeys is not null)
        {
            foreach (var scriptKey in context.ScriptKeys)
            {
                if (string.IsNullOrWhiteSpace(scriptKey))
                    continue;

                if (!_scriptKeyLookup.TryGetValue(scriptKey.Trim(), out var script))
                    continue;

                chineseText = ResolveDisplay(script);
                _log.Debug("Dialogue matched by script key {ScriptKey}.", scriptKey);
                return true;
            }
        }

        if (context.Row is not { } row)
            return false;

        if (!_rowLookup.TryGetValue((row.Sheet, row.RowId, row.Field), out var slots))
            return false;

        if (row.Slot >= 0)
        {
            if (row.Slot >= slots.Length || string.IsNullOrEmpty(slots[row.Slot]))
                return false;

            chineseText = ResolveDisplay(slots[row.Slot]);
            _log.Debug("Dialogue matched by {Sheet} row {RowId} slot {Slot}.",
                row.Sheet, row.RowId, row.Slot);
            return true;
        }

        if (TrySelectRowSlot(row, englishText, slots, out var selected, out var compared))
        {
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
            return false;
        }

        // A row with one translated slot needs no text to choose between slots.
        var onlySlot = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            if (string.IsNullOrEmpty(slots[i]))
                continue;

            if (onlySlot >= 0)
                return false;

            onlySlot = i;
        }

        if (onlySlot < 0)
            return false;

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
        out bool compared)
    {
        selected = -1;
        compared = false;

        if (string.IsNullOrWhiteSpace(englishText)
            || !_descriptorByKey.TryGetValue((row.Sheet, row.Field), out var descriptor)
            || !descriptor.TryExtract(row.RowId, out var englishSlots))
        {
            return false;
        }

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return false;

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
        stored = string.Empty;

        if (_branchNeutralLookup.Count == 0)
            return false;

        var neutral = CollapseBranchWords(key);
        if (ReferenceEquals(neutral, key))
            return false;

        if (_ambiguousBranchKeys.Contains(neutral)
            || !_branchNeutralLookup.TryGetValue(neutral, out var match))
        {
            return false;
        }

        _log.Debug("Script line matched after collapsing the branch words.");
        stored = match;
        return true;
    }

    /// <summary>
    /// Collapses every word the English dump may have resolved to another branch.
    /// </summary>
    private static string CollapseBranchWords(string key)
        => TimeOfDayWords.Neutralize(GenderedNouns.Neutralize(GenderedPronouns.Neutralize(key)));

    /// <summary>
    /// Looks up a short Addon-sheet label such as "Yes.", "No.", or "Skip cutscene?".
    /// The separate lookup keeps a same-text NpcYell or script row from winning.
    /// </summary>
    public bool TryGetAddonLabel(string? englishText, out string chineseText)
    {
        chineseText = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0 || !_addonLookup.TryGetValue(key, out var stored))
            return false;

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
    {
        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length < prefixLength)
            return false;

        var prefix = key[..prefixLength];

        foreach (var (template, _, _) in _addonTemplates)
        {
            if (template.Length == 0 || template[0] == ScriptTextTemplate.Wildcard)
                continue;

            if (template.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public bool TryGetAddonText(
        string? englishText,
        out string chineseText,
        out string[] captures,
        out int[]? parameterIndices)
    {
        chineseText = string.Empty;
        captures = [];
        parameterIndices = null;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0 || _addonTemplates.Count == 0)
            return false;

        foreach (var (template, chinese, indices) in _addonTemplates)
        {
            if (!ScriptTextTemplate.TryMatch(template, key, out captures))
                continue;

            chineseText = chinese;
            parameterIndices = indices;
            return true;
        }

        return false;
    }

    /// <summary>True when the stored line carried UI text colors.</summary>
    public bool TryGetStyle(string? displayText, out UiTextStyle style)
    {
        style = default;
        return !string.IsNullOrEmpty(displayText) && _styleLookup.TryGetValue(displayText, out style);
    }

    private void RegisterStyle(string display, string rawText)
    {
        if (UiTextStyle.TryParse(rawText, out var style))
            _styleLookup.TryAdd(display, style);
    }

    public bool TryGetName(string? englishName, out string chineseName)
    {
        chineseName = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishName);
        if (key.Length == 0)
            return false;

        if (!_nameLookup.TryGetValue(key, out var stored))
        {
            // Speaker aliases can carry punctuation, for example "Pattin?".
            // The name sheet stores the name without it.
            var trimmed = key.TrimEnd('?', '!', '.', '？', '！', '。', '…');
            if (trimmed.Length == 0 || trimmed == key || !_nameLookup.TryGetValue(trimmed, out stored))
                return false;
        }

        chineseName = ResolveDisplay(stored);
        return true;
    }

    /// <summary>
    /// Builds a store for the configured variant. Returns null when neither the
    /// primary nor the fallback artifact could be read. The opener decides where
    /// artifacts come from; the plugin prefers downloaded files and falls back to
    /// the copies embedded in the assembly.
    /// </summary>
    public static TranslationStore? Load(
        Configuration configuration,
        IDataManager dataManager,
        IPluginLog log,
        Func<ChineseVariant, Stream?> openResource,
        Func<ChineseVariant, Stream?>? fallbackResource = null,
        Func<string, string>? displayResolver = null,
        Func<string?>? playerName = null)
        => Load(
            SheetCatalog.Create(dataManager),
            configuration.Variant,
            configuration.UseFallbackVariant,
            openResource,
            log,
            fallbackResource,
            displayResolver,
            playerName,
            configuration);

    /// <summary>
    /// Reads and parses the artifacts for the configured variant without touching
    /// any Lumina sheet.
    /// </summary>
    /// <remarks>
    /// Parsing is the expensive half that is safe to run on a worker task. Reading
    /// rows from the live game sheets is not: Dalamud forbids that off the game
    /// thread. <see cref="Build"/> takes the result and finishes the load on the
    /// game thread.
    /// </remarks>
    public static ParsedArtifacts? Parse(
        Configuration configuration,
        IPluginLog log,
        Func<ChineseVariant, Stream?> openResource,
        Func<ChineseVariant, Stream?>? fallbackResource = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var parsed = ParseArtifacts(
            configuration.Variant,
            configuration.UseFallbackVariant,
            openResource,
            log,
            fallbackResource);

        if (parsed is not null)
        {
            new PluginLog(log, configuration, LogCategory.Data).Debug(
                "Parsed {Variant} artifacts in {Elapsed} ms.",
                configuration.Variant, stopwatch.ElapsedMilliseconds);
        }

        return parsed;
    }

    /// <summary>
    /// Finishes a load from artifacts that came from <see cref="Parse"/>. Does the
    /// sheet reads and the indexing, so it must run on the game thread.
    /// </summary>
    public static TranslationStore? Build(
        ParsedArtifacts parsed,
        Configuration configuration,
        IDataManager dataManager,
        IPluginLog log,
        Func<string, string>? displayResolver = null,
        Func<string?>? playerName = null)
        => Build(
            parsed,
            SheetCatalog.Create(dataManager),
            configuration.Variant,
            log,
            displayResolver,
            playerName,
            configuration);

    /// <summary>
    /// Builds a store from explicit inputs. Tests use this to supply their own
    /// sheet descriptors and artifact streams instead of the embedded ones.
    /// </summary>
    /// <param name="displayResolver">
    /// Turns stored text into what the overlay shows. Lines with a conditional are
    /// resolved on each lookup, because the answer depends on the player. Lines
    /// without one are resolved once here.
    /// </param>
    /// <param name="playerName">
    /// The player's full name. A line the game filled in with it can still match the
    /// dump's placeholder. Null skips that second lookup.
    /// </param>
    internal static TranslationStore? Load(
        IReadOnlyList<ISheetDescriptor> descriptors,
        ChineseVariant variant,
        bool useFallback,
        Func<ChineseVariant, Stream?> openResource,
        IPluginLog log,
        Func<ChineseVariant, Stream?>? fallbackResource = null,
        Func<string, string>? displayResolver = null,
        Func<string?>? playerName = null,
        Configuration? configuration = null)
    {
        var parsed = ParseArtifacts(variant, useFallback, openResource, log, fallbackResource);
        if (parsed is null)
            return null;

        return Build(parsed, descriptors, variant, log, displayResolver, playerName, configuration);
    }

    /// <summary>
    /// Builds a store from already parsed artifacts. This overload reads the live
    /// sheets and indexes on the calling thread; the plugin uses the overload below
    /// so the sheet reads never run in one frame.
    /// </summary>
    internal static TranslationStore? Build(
        ParsedArtifacts parsed,
        IReadOnlyList<ISheetDescriptor> descriptors,
        ChineseVariant variant,
        IPluginLog log,
        Func<string, string>? displayResolver = null,
        Func<string?>? playerName = null,
        Configuration? configuration = null,
        Func<string, string>? indexResolver = null)
    {
        var extraction = new SheetExtraction(parsed, descriptors);
        extraction.Extract(TimeSpan.MaxValue);
        return Build(
            parsed,
            descriptors,
            extraction,
            variant,
            log,
            displayResolver,
            playerName,
            configuration,
            indexResolver);
    }

    /// <summary>
    /// Builds a store from artifacts and English text already read from the live
    /// sheets. Touches no Lumina sheet, so it can run on a worker task.
    /// </summary>
    internal static TranslationStore? Build(
        ParsedArtifacts parsed,
        IReadOnlyList<ISheetDescriptor> descriptors,
        SheetExtraction extraction,
        ChineseVariant variant,
        IPluginLog log,
        Func<string, string>? displayResolver = null,
        Func<string?>? playerName = null,
        Configuration? configuration = null,
        Func<string, string>? indexResolver = null)
    {
        var stopwatch = Stopwatch.StartNew();

        var primary = parsed.Primary;
        var fallback = parsed.Fallback;

        if (primary is null && fallback is null)
            return null;

        // Without settings there is nothing to filter by, which is what the test
        // seams use.
        var dataLog = configuration is null
            ? new PluginLog(log)
            : new PluginLog(log, configuration, LogCategory.Data);
        var dialogueLog = configuration is null
            ? new PluginLog(log)
            : new PluginLog(log, configuration, LogCategory.Dialogue);

        var store = new TranslationStore(
            variant,
            fallback is not null,
            descriptors,
            displayResolver ?? (static text => MacroNormalizer.CleanForDisplay(text)),
            indexResolver,
            playerName,
            dialogueLog);

        if (primary is not null)
            store.Index(primary, descriptors, extraction, dataLog);

        var primaryTextCount = store._textLookup.Count;
        var primaryNameCount = store._nameLookup.Count;
        var primaryScriptCount = store._scriptEntryCount;

        if (fallback is not null)
            store.Index(fallback, descriptors, extraction, dataLog);

        // Several rows can match one prompt. A row that draws a word with a
        // conditional macro matches almost any text in its place, so its template
        // also matches prompts written for another row. The template with the most
        // literal text names the prompt most exactly, so try it first.
        store._addonTemplates.Sort(CompareAddonTemplates);

        stopwatch.Stop();
        dataLog.Info(
            "Loaded {Variant}{Fallback} translations: {Text:N0} dialogue, {Name:N0} names and {Script:N0} script lines " +
            "in {Elapsed} ms (fallback added {AddedText:N0} dialogue / {AddedName:N0} names / {AddedScript:N0} script lines).",
            store.PrimaryVariant,
            store.UsedFallback ? " + fallback" : string.Empty,
            store._textLookup.Count,
            store._nameLookup.Count,
            store._scriptEntryCount,
            stopwatch.ElapsedMilliseconds,
            store._textLookup.Count - primaryTextCount,
            store._nameLookup.Count - primaryNameCount,
            store._scriptEntryCount - primaryScriptCount);

        return store;
    }

    /// <summary>
    /// Orders Addon templates from the most literal text to the least. A template
    /// with fewer wildcards wins a tie, because it allows less text to vary.
    /// </summary>
    private static int CompareAddonTemplates(
        (string Template, string Chinese, int[]? ParameterIndices) left,
        (string Template, string Chinese, int[]? ParameterIndices) right)
    {
        var literal = ScriptTextTemplate.LiteralLength(right.Template)
            .CompareTo(ScriptTextTemplate.LiteralLength(left.Template));
        if (literal != 0)
            return literal;

        var wildcards = CountWildcards(left.Template).CompareTo(CountWildcards(right.Template));
        if (wildcards != 0)
            return wildcards;

        return string.CompareOrdinal(left.Template, right.Template);
    }

    private static int CountWildcards(string template)
    {
        var count = 0;
        foreach (var ch in template)
        {
            if (ch == ScriptTextTemplate.Wildcard)
                count++;
        }

        return count;
    }

    private static ChineseVariant Other(ChineseVariant variant)
        => variant == ChineseVariant.Traditional ? ChineseVariant.Simplified : ChineseVariant.Traditional;

    /// <summary>
    /// Reads and parses the artifacts. Returns null when neither variant is
    /// available. Touches no game state, so a caller may run this on a worker task.
    /// </summary>
    private static ParsedArtifacts? ParseArtifacts(
        ChineseVariant variant,
        bool useFallback,
        Func<ChineseVariant, Stream?> openResource,
        IPluginLog log,
        Func<ChineseVariant, Stream?>? fallbackResource)
    {
        var primary = LoadArtifact(variant, openResource, fallbackResource, log);
        if (primary is null)
        {
            log.Warning("Primary artifact for {Variant} was not available.", variant);
        }

        var fallback = useFallback
            ? LoadArtifact(Other(variant), openResource, fallbackResource, log)
            : null;

        if (primary is null && fallback is null)
        {
            log.Error("No translation artifacts are available. Talk text will not be translated.");
            return null;
        }

        return new ParsedArtifacts(variant, primary, fallback);
    }

    private void Index(
        TranslationArtifact artifact,
        IReadOnlyList<ISheetDescriptor> descriptors,
        SheetExtraction extraction,
        PluginLog log)
    {
        var indexedRows = 0;
        var indexedSlots = 0;

        foreach (var descriptor in descriptors)
        {
            if (!artifact.Sheets.TryGetValue(descriptor.Name, out var rows))
            {
                log.Debug("Artifact has no rows for sheet {Sheet}.", descriptor.Name);
                continue;
            }

            var target = descriptor.IsNameSheet ? _nameLookup : _textLookup;

            foreach (var (rowKey, fields) in rows)
            {
                if (!uint.TryParse(rowKey, out var rowId))
                    continue;

                if (!fields.TryGetValue(descriptor.FieldName, out var chineseSlots) || chineseSlots.Length == 0)
                    continue;

                if (!extraction.TryGet(descriptor.Name, descriptor.FieldName, rowId, out var extracted))
                    continue;

                var englishSlots = extracted.EnglishSlots;

                var slotCount = Math.Min(englishSlots.Count, chineseSlots.Length);

                string[]? rowSlots = null;
                var idKey = (Sheet: descriptor.Name, RowId: rowId, Field: descriptor.FieldName);

                if (!descriptor.IsNameSheet)
                {
                    if (!_rowLookup.TryGetValue(idKey, out rowSlots))
                    {
                        rowSlots = new string[chineseSlots.Length];
                        _rowLookup[idKey] = rowSlots;
                    }
                    else if (rowSlots.Length < chineseSlots.Length)
                    {
                        Array.Resize(ref rowSlots, chineseSlots.Length);
                        _rowLookup[idKey] = rowSlots;
                    }
                }

                for (var i = 0; i < slotCount; i++)
                {
                    var display = PrepareDisplay(chineseSlots[i]);
                    if (display.Length == 0)
                        continue;

                    RegisterStyle(display, chineseSlots[i]);

                    if (descriptor.IsNameSheet)
                        _nameRowLookup.TryAdd((descriptor.Name, rowId), display);

                    if (rowSlots is not null && string.IsNullOrEmpty(rowSlots[i]))
                        rowSlots[i] = display;

                    var variants = englishSlots[i];
                    for (var v = 0; v < variants.Count; v++)
                    {
                        var key = MacroNormalizer.NormalizeKey(variants[v]);
                        if (key.Length == 0)
                            continue;

                        if (target.TryAdd(key, display))
                            indexedSlots++;

                        if (string.Equals(descriptor.Name, "Addon", StringComparison.Ordinal)
                            && !descriptor.IsNameSheet)
                        {
                            _addonLookup.TryAdd(key, display);

                            if (extracted.ParameterTemplate is { } parameterTemplate
                                // "Enter ?" is only 7 literal characters and would match
                                // unrelated prompts such as "Enter the estate hall?".
                                && ScriptTextTemplate.LiteralLength(parameterTemplate.Template) >= 10
                                && _addonTemplateKeys.Add(parameterTemplate.Template))
                            {
                                _addonTemplates.Add((
                                    parameterTemplate.Template,
                                    chineseSlots[i],
                                    parameterTemplate.ParameterIndices));
                            }
                        }

                        if (string.Equals(descriptor.Name, "Addon", StringComparison.Ordinal)
                            && variants[v].Contains("UNKNOWN", StringComparison.Ordinal))
                        {
                            var template = ScriptTextTemplate.BuildFromPlainText(variants[v]);
                            if (ScriptTextTemplate.LiteralLength(template) >= 4
                                && _addonTemplateKeys.Add(template))
                            {
                                _addonTemplates.Add((template, chineseSlots[i], null));
                            }

                            var loose = ScriptTextTemplate.BuildLooseFromPlainText(variants[v]);
                            if (ScriptTextTemplate.LiteralLength(loose) >= 4
                                && !string.Equals(loose, template, StringComparison.Ordinal)
                                && _addonTemplateKeys.Add(loose))
                            {
                                _addonTemplates.Add((loose, chineseSlots[i], null));
                            }

                            // A prompt with several lines can change which detail
                            // lines the game shows. Match the first line and let the
                            // trailing wildcard cover the rest.
                            var firstLineEnd = variants[v].IndexOf('\n');
                            if (firstLineEnd > 0 && firstLineEnd < variants[v].Length - 1)
                            {
                                var firstLine = variants[v][..firstLineEnd].Trim();
                                var prefix = ScriptTextTemplate.BuildLooseFromPlainText(firstLine)
                                             + ScriptTextTemplate.Wildcard;
                                if (ScriptTextTemplate.LiteralLength(prefix) >= 4
                                    && _addonTemplateKeys.Add(prefix))
                                {
                                    _addonTemplates.Add((prefix, chineseSlots[i], null));
                                }
                            }
                        }

                        // Blank placeholders in the English Addon row become
                        // double spaces. Treat each gap between words as dynamic.
                        if (string.Equals(descriptor.Name, "Addon", StringComparison.Ordinal)
                            && variants[v].Contains("  ", StringComparison.Ordinal))
                        {
                            var wordTemplate = ScriptTextTemplate.BuildWordGapTemplate(variants[v]);
                            if (ScriptTextTemplate.LiteralLength(wordTemplate) >= 4
                                && _addonTemplateKeys.Add(wordTemplate))
                            {
                                _addonTemplates.Add((wordTemplate, chineseSlots[i], null));
                            }
                        }
                    }
                }

                indexedRows++;
            }
        }

        var indexedScripts = IndexScripts(artifact, log);

        log.Debug("Indexed {Rows:N0} rows, {Slots:N0} text slots and {Scripts:N0} script lines from the {Language} artifact.",
            indexedRows, indexedSlots, indexedScripts, artifact.Language);
    }

    /// <summary>
    /// Indexes dialogue that has no Lumina row. The artifact carries both the English
    /// key and the translated text, because there is no sheet to read the English from.
    /// </summary>
    private int IndexScripts(TranslationArtifact artifact, PluginLog log)
    {
        var added = 0;

        // Keys this artifact added, so a collision inside one artifact is told apart
        // from the other variant simply owning the line.
        var indexedInThisArtifact = new Dictionary<string, ScriptCandidate>(StringComparer.Ordinal);

        foreach (var (scriptKey, entry) in artifact.Scripts)
        {
            // Event speakers such as "Distant Echo" often exist only as a script
            // alias, not as a row in a name sheet. Learn the Chinese alias from the
            // translated line so the speaker node can use it.
            var englishAlias = MacroNormalizer.ExtractSpeakerPrefix(entry.English);
            var chineseAlias = MacroNormalizer.ExtractSpeakerPrefix(entry.Text);
            if (englishAlias.Length > 0
                && chineseAlias.Length > 0
                && !string.Equals(englishAlias, "Player", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(englishAlias, MacroNormalizer.PlayerNamePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                var aliasKey = MacroNormalizer.NormalizeKey(englishAlias);
                var aliasText = MacroNormalizer.CleanForDisplay(chineseAlias);
                if (aliasKey.Length > 0 && aliasText.Length > 0)
                    _nameLookup.TryAdd(aliasKey, aliasText);
            }

            // The dumps may prefix the English line with a speaker alias such as
            // "(-Radio-)". The game draws that name in a separate node, so the
            // dialogue text lookup must not include it.
            var key = MacroNormalizer.NormalizeKey(MacroNormalizer.StripSpeakerPrefix(entry.English));
            if (key.Length == 0 || string.Equals(key, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                log.Debug("Skipping script entry with placeholder English: {ScriptKey}", scriptKey);
                continue;
            }

            var display = PrepareDisplay(entry.Text);
            if (display.Length == 0)
                continue;

            RegisterStyle(display, entry.Text);

            // Some script dumps leave a double-space gap where a runtime value
            // such as an item name is inserted. Keep a template for that case.
            if (entry.English.Contains("  ", StringComparison.Ordinal))
            {
                var blankTemplate = ScriptTextTemplate.BuildBlankGapTemplate(
                    MacroNormalizer.StripSpeakerPrefix(entry.English));
                if (ScriptTextTemplate.LiteralLength(blankTemplate) >= 4)
                    _scriptTemplates.Add((blankTemplate, display));
            }

            // The dump key is the game's own id for this line. The game sometimes
            // exposes it through the event handler, so keep it separate from the
            // English key and let an id lookup win over duplicate text.
            if (!string.IsNullOrWhiteSpace(scriptKey))
                _scriptKeyLookup.TryAdd(scriptKey, display);

            if (_ambiguousScriptKeys.TryGetValue(key, out var known))
            {
                AddScriptCandidate(known, scriptKey, display);
                continue;
            }

            if (indexedInThisArtifact.TryGetValue(key, out var first))
            {
                if (string.Equals(first.Display, display, StringComparison.Ordinal))
                    continue;

                // Two script lines share this English key with different Chinese. The
                // key cannot tell them apart, so it must not answer either one. Keep
                // both candidates so the speaker name can still choose.
                _textLookup.Remove(key);
                added--;
                _scriptEntryCount--;

                var candidates = new List<ScriptCandidate>(3) { first };
                AddScriptCandidate(candidates, scriptKey, display);
                _ambiguousScriptKeys[key] = candidates;

                // The collapsed key would give the same wrong answer for the other
                // wording of the line.
                var collapsed = CollapseBranchWords(key);
                if (!ReferenceEquals(collapsed, key)
                    && _branchNeutralLookup.TryGetValue(collapsed, out var collapsedDisplay)
                    && string.Equals(collapsedDisplay, first.Display, StringComparison.Ordinal))
                {
                    _branchNeutralLookup.Remove(collapsed);
                }

                continue;
            }

            if (!_textLookup.TryAdd(key, display))
            {
                // The configured variant already owns this line. Its collapsed key is
                // built, and the other variant's Chinese only conflicts with it, so
                // the whole index would look ambiguous.
                continue;
            }

            added++;
            _scriptEntryCount++;
            indexedInThisArtifact[key] = new ScriptCandidate(
                display, KeySpeakerTokens(scriptKey), string.IsNullOrEmpty(scriptKey) ? [] : [scriptKey]);

            // The English dump holds one branch's wording, so keep a key with the
            // branch words collapsed for the others. A line that already matches
            // exactly keeps priority, because the exact lookup runs first.
            var neutral = CollapseBranchWords(key);
            if (!ReferenceEquals(neutral, key) && !_textLookup.ContainsKey(neutral)
                && !_branchNeutralLookup.TryAdd(neutral, display)
                && !string.Equals(_branchNeutralLookup[neutral], display, StringComparison.Ordinal))
            {
                // Two lines collapse to the same key with different Chinese. The
                // key cannot tell them apart, so it must not answer either one.
                _ambiguousBranchKeys.Add(neutral);
            }
        }

        if (_branchNeutralLookup.Count > 0)
        {
            log.Debug("Indexed {Count:N0} collapsed script keys.", _branchNeutralLookup.Count);
        }

        if (_ambiguousBranchKeys.Count > 0)
        {
            log.Debug(
                "{Count:N0} collapsed script keys map to more than one line and are not used.",
                _ambiguousBranchKeys.Count);
        }

        if (_ambiguousScriptKeys.Count > 0)
        {
            log.Debug(
                "{Count:N0} script keys map to more than one line; the speaker name picks one.",
                _ambiguousScriptKeys.Count);
        }

        return added;
    }

    /// <summary>
    /// Keeps one more line for an English key that several script lines share. A line
    /// with the same Chinese as one already kept adds nothing.
    /// </summary>
    private static void AddScriptCandidate(
        List<ScriptCandidate> candidates,
        string? scriptKey,
        string display)
    {
        var tokens = KeySpeakerTokens(scriptKey);

        for (var i = 0; i < candidates.Count; i++)
        {
            // The same dump key can arrive again from the other variant's artifact, with that
            // variant's Chinese. It is one line, not a second scene: keep the configured variant's
            // text and the key it already carries, or the candidate list grows a phantom entry and
            // the speaker and cutscene steps can no longer see that only one line matches.
            if (!string.IsNullOrEmpty(scriptKey) && Array.IndexOf(candidates[i].Keys, scriptKey) >= 0)
                return;

            if (!string.Equals(candidates[i].Display, display, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrEmpty(scriptKey))
                candidates[i] = candidates[i].WithKey(scriptKey, tokens);

            return;
        }

        candidates.Add(new ScriptCandidate(
            display, tokens, string.IsNullOrEmpty(scriptKey) ? [] : [scriptKey]));
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

            return _nameRowLookup.TryGetValue((lookupSheet, rowId), out var name)
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

    private static TranslationArtifact? LoadArtifact(
        ChineseVariant variant,
        Func<ChineseVariant, Stream?> openResource,
        Func<ChineseVariant, Stream?>? fallbackResource,
        IPluginLog log)
    {
        var artifact = TryLoadArtifact(variant, openResource, log);

        if (artifact is not null || fallbackResource is null)
            return artifact;

        // The primary stream exists but could not be read. Keep working with the
        // copy shipped in the assembly instead of running with no translations.
        log.Warning("Could not read the {Variant} artifact. Using the embedded copy.", variant);
        return TryLoadArtifact(variant, fallbackResource, log);
    }

    private static TranslationArtifact? TryLoadArtifact(
        ChineseVariant variant,
        Func<ChineseVariant, Stream?> openResource,
        IPluginLog log)
    {
        try
        {
            using var stream = openResource(variant);
            if (stream is null)
                return null;

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<TranslationArtifact>(gzip);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to load the {Variant} translation artifact.", variant);
            return null;
        }
    }

    /// <summary>Opens the artifact embedded in the plugin assembly, if present.</summary>
    public static Stream? OpenEmbeddedResource(ChineseVariant variant)
    {
        var suffix = variant == ChineseVariant.Traditional ? ResourceTcSuffix : ResourceCnSuffix;
        var assembly = typeof(TranslationStore).Assembly;

        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        return name is null ? null : assembly.GetManifestResourceStream(name);
    }
}

/// <summary>
/// English text read from the live sheets for one artifact row, plus the Addon
/// template when the row has one. Reading a sheet is a game-thread operation.
/// </summary>
internal sealed class ExtractedRow
{
    public ExtractedRow(
        IReadOnlyList<IReadOnlyList<string>> englishSlots,
        AddonParameterTemplate? parameterTemplate)
    {
        EnglishSlots = englishSlots;
        ParameterTemplate = parameterTemplate;
    }

    public IReadOnlyList<IReadOnlyList<string>> EnglishSlots { get; }

    public AddonParameterTemplate? ParameterTemplate { get; }
}

/// <summary>
/// The English half of the index, read from the live sheets. Collection can be
/// spread over several frames with <see cref="Extract"/>; the finished data holds
/// only strings, so the index build can run on a worker task.
/// </summary>
internal sealed class SheetExtraction
{
    private readonly ParsedArtifacts _parsed;
    private readonly IReadOnlyList<ISheetDescriptor> _descriptors;
    private readonly Dictionary<(string Sheet, string Field, uint RowId), ExtractedRow> _rows = new();

    /// <summary>Row ids of the sheet being read. Built one sheet at a time.</summary>
    private readonly List<uint> _rowsForDescriptor = new();
    private int _descriptorIndex;
    private int _next;

    public SheetExtraction(ParsedArtifacts parsed, IReadOnlyList<ISheetDescriptor> descriptors)
    {
        _parsed = parsed;
        _descriptors = descriptors;
    }

    public bool IsComplete
        => _descriptorIndex >= _descriptors.Count && _next >= _rowsForDescriptor.Count;

    public int RowCount => _rows.Count;

    /// <summary>
    /// Reads live sheet rows until the budget is spent. The caller runs this on the
    /// game thread between frames, so a large artifact never stalls one frame.
    /// </summary>
    public void Extract(TimeSpan budget)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            if (_next < _rowsForDescriptor.Count)
            {
                ExtractRow(_descriptors[_descriptorIndex - 1], _rowsForDescriptor[_next]);
                _next++;
            }
            else if (_descriptorIndex < _descriptors.Count)
            {
                // Build one sheet's row list at a time, so the planning work is
                // spread out with the reads.
                CollectRows(_descriptors[_descriptorIndex], _rowsForDescriptor);
                _descriptorIndex++;
                _next = 0;
                continue;
            }
            else
            {
                return;
            }

            // Check every row. A row can take longer than the whole budget, so a
            // batch check would let one frame read far past it.
            if (stopwatch.Elapsed >= budget)
                return;
        }
    }

    private void ExtractRow(ISheetDescriptor descriptor, uint rowId)
    {
        if (!descriptor.TryExtract(rowId, out var englishSlots))
            return;

        AddonParameterTemplate? parameterTemplate = null;
        if (descriptor is IParameterizedTemplateDescriptor parameterized
            && parameterized.TryBuildTemplate(rowId, out var built))
        {
            parameterTemplate = built;
        }

        _rows[(descriptor.Name, descriptor.FieldName, rowId)] =
            new ExtractedRow(englishSlots, parameterTemplate);
    }

    public bool TryGet(string sheet, string field, uint rowId, out ExtractedRow row)
    {
        if (_rows.TryGetValue((sheet, field, rowId), out var extracted))
        {
            row = extracted;
            return true;
        }

        row = null!;
        return false;
    }

    private void CollectRows(ISheetDescriptor descriptor, List<uint> rows)
    {
        rows.Clear();
        var seen = new HashSet<uint>();
        AddRows(_parsed.Primary, descriptor, rows, seen);
        AddRows(_parsed.Fallback, descriptor, rows, seen);
    }

    private static void AddRows(
        TranslationArtifact? artifact,
        ISheetDescriptor descriptor,
        List<uint> rows,
        HashSet<uint> seen)
    {
        if (artifact is null || !artifact.Sheets.TryGetValue(descriptor.Name, out var sheetRows))
            return;

        foreach (var (rowKey, fields) in sheetRows)
        {
            if (!uint.TryParse(rowKey, out var rowId))
                continue;

            if (!fields.TryGetValue(descriptor.FieldName, out var chinese) || chinese.Length == 0)
                continue;

            if (seen.Add(rowId))
                rows.Add(rowId);
        }
    }
}

/// <summary>
/// Artifacts that have been read and deserialized but not indexed yet. Holds no
/// game objects, so it is safe to hand between a worker task and the game thread.
/// </summary>
public sealed class ParsedArtifacts
{
    internal ParsedArtifacts(
        ChineseVariant variant,
        TranslationArtifact? primary,
        TranslationArtifact? fallback)
    {
        Variant = variant;
        Primary = primary;
        Fallback = fallback;
    }

    public ChineseVariant Variant { get; }

    internal TranslationArtifact? Primary { get; }

    internal TranslationArtifact? Fallback { get; }
}
