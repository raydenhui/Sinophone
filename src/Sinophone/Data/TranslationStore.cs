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
public sealed class TranslationStore
{
    public const string ResourceCnSuffix = "Sinophone.cn.json.gz";
    public const string ResourceTcSuffix = "Sinophone.tc.json.gz";

    private readonly Dictionary<string, string> _textLookup = new(StringComparer.Ordinal);
    // Names are matched without case: the sheet stores generic NPC names in lower
    // case ("skywatcher") and the game shows them capitalised ("Skywatcher").
    private readonly Dictionary<string, string> _nameLookup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Script lines under a key with the gendered pronouns collapsed. Only tried
    /// after an exact miss, because the English dump stores one gender branch.
    /// </summary>
    private readonly Dictionary<string, string> _genderNeutralLookup = new(StringComparer.Ordinal);

    /// <summary>
    /// Addon-sheet prompts with UNKNOWN placeholders, matched as templates. The
    /// game fills those values at runtime, so an exact lookup cannot match them.
    /// </summary>
    private readonly Dictionary<string, string> _addonLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiTextStyle> _styleLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Sheet, uint RowId), string> _nameRowLookup = new();
    private readonly List<(string Template, string Chinese, int[]? ParameterIndices)> _addonTemplates = [];
    private readonly HashSet<string> _addonTemplateKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Translations keyed by the sheet row the game says it is showing. This is the
    /// id-first path: it resolves a line even when the same English text appears in
    /// several rows.
    /// </summary>
    private readonly Dictionary<(string Sheet, uint RowId, string Field), string[]> _rowLookup = new();

    /// <summary>Script translations keyed by the dump's text key, when the game exposes it.</summary>
    private readonly Dictionary<string, string> _scriptKeyLookup = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<(string Sheet, string Field), ISheetDescriptor> _descriptorByKey = new();

    private readonly Func<string, string> _displayResolver;
    private readonly Func<string?>? _playerName;
    private readonly IPluginLog? _log;

    private int _scriptEntryCount;

    private TranslationStore(
        ChineseVariant primaryVariant,
        bool usedFallback,
        IReadOnlyList<ISheetDescriptor> descriptors,
        Func<string, string> displayResolver,
        Func<string?>? playerName,
        IPluginLog? log)
    {
        PrimaryVariant = primaryVariant;
        UsedFallback = usedFallback;
        _displayResolver = displayResolver;
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

    internal int AddonTemplateCount => _addonTemplates.Count;

    public bool TryGetDialogue(string? englishText, out string chineseText)
        => TryGetDialogue(englishText, default, out chineseText);

    /// <summary>
    /// Resolves one line, trying the game's ids before the English text. A missing
    /// id or an old artifact simply falls through to the text lookup, so every
    /// surface keeps the previous behaviour as a fallback.
    /// </summary>
    public bool TryGetDialogue(string? englishText, in DialogueContext context, out string chineseText)
    {
        if (TryGetDialogueById(englishText, context, out chineseText))
            return true;

        chineseText = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return false;

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

        if (!TryLookupGenderNeutral(key, out var match))
            return false;

        chineseText = ResolveDisplay(match);
        return true;
    }

    /// <summary>
    /// Retries a miss with the player's name put back as a dump placeholder. The
    /// game sometimes uses the full character name and sometimes only the first
    /// name, while the dump writes only the placeholder.
    /// </summary>
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
        return !string.Equals(firstName, fullName, StringComparison.Ordinal)
               && TryPlayerNameVariant(key, firstName, out chineseText);
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

    private bool TryLookupWithGenderFallback(string key, out string chineseText)
    {
        chineseText = string.Empty;

        if (_textLookup.TryGetValue(key, out var stored))
        {
            chineseText = ResolveDisplay(stored);
            return true;
        }

        if (!TryLookupGenderNeutral(key, out var match))
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
                _log?.Debug("Dialogue matched by script key {ScriptKey}.", scriptKey);
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
            _log?.Debug("Dialogue matched by {Sheet} row {RowId} slot {Slot}.",
                row.Sheet, row.RowId, row.Slot);
            return true;
        }

        if (TrySelectRowSlot(row, englishText, slots, out var selected))
        {
            chineseText = ResolveDisplay(slots[selected]);
            _log?.Debug("Dialogue matched by {Sheet} row {RowId} after selecting slot {Slot}.",
                row.Sheet, row.RowId, selected);
            return true;
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
        _log?.Debug("Dialogue matched by {Sheet} row {RowId}, the only translated slot.",
            row.Sheet, row.RowId);
        return true;
    }

    private bool TrySelectRowSlot(
        DialogueRowId row,
        string? englishText,
        string[] slots,
        out int selected)
    {
        selected = -1;

        if (string.IsNullOrWhiteSpace(englishText)
            || !_descriptorByKey.TryGetValue((row.Sheet, row.Field), out var descriptor)
            || !descriptor.TryExtract(row.RowId, out var englishSlots))
        {
            return false;
        }

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return false;

        for (var i = 0; i < slots.Length && i < englishSlots.Count; i++)
        {
            if (string.IsNullOrEmpty(slots[i]))
                continue;

            foreach (var variant in englishSlots[i])
            {
                if (MacroNormalizer.NormalizeKey(variant) != key)
                    continue;

                selected = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches a script line that the English dump wrote for the other gender.
    /// </summary>
    private bool TryLookupGenderNeutral(string key, out string stored)
    {
        stored = string.Empty;

        if (_genderNeutralLookup.Count == 0)
            return false;

        var neutral = GenderedPronouns.Neutralize(key);
        if (ReferenceEquals(neutral, key))
            return false;

        if (!_genderNeutralLookup.TryGetValue(neutral, out var match))
            return false;

        _log?.Debug("Script line matched after collapsing the gendered pronouns.");
        stored = match;
        return true;
    }

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
            playerName);

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
        Func<string?>? playerName = null)
    {
        var stopwatch = Stopwatch.StartNew();

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

        var store = new TranslationStore(
            variant,
            fallback is not null,
            descriptors,
            displayResolver ?? (static text => MacroNormalizer.CleanForDisplay(text)),
            playerName,
            log);

        if (primary is not null)
            store.Index(primary, descriptors, log);

        var primaryTextCount = store._textLookup.Count;
        var primaryNameCount = store._nameLookup.Count;
        var primaryScriptCount = store._scriptEntryCount;

        if (fallback is not null)
            store.Index(fallback, descriptors, log);

        stopwatch.Stop();
        log.Info(
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

    private static ChineseVariant Other(ChineseVariant variant)
        => variant == ChineseVariant.Traditional ? ChineseVariant.Simplified : ChineseVariant.Traditional;

    private void Index(TranslationArtifact artifact, IReadOnlyList<ISheetDescriptor> descriptors, IPluginLog log)
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

                if (!descriptor.TryExtract(rowId, out var englishSlots))
                    continue;

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

                            if (descriptor is IParameterizedTemplateDescriptor parameterized
                                && parameterized.TryBuildTemplate(rowId, out var parameterTemplate)
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

        var indexedScripts = IndexScripts(artifact);

        log.Debug("Indexed {Rows:N0} rows, {Slots:N0} text slots and {Scripts:N0} script lines from the {Language} artifact.",
            indexedRows, indexedSlots, indexedScripts, artifact.Language);
    }

    /// <summary>
    /// Indexes dialogue that has no Lumina row. The artifact carries both the English
    /// key and the translated text, because there is no sheet to read the English from.
    /// </summary>
    private int IndexScripts(TranslationArtifact artifact)
    {
        var added = 0;

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
                _log?.Debug("Skipping script entry with placeholder English: {ScriptKey}", scriptKey);
                continue;
            }

            var display = PrepareDisplay(entry.Text);
            if (display.Length == 0)
                continue;

            RegisterStyle(display, entry.Text);

            // The dump key is the game's own id for this line. The game sometimes
            // exposes it through the event handler, so keep it separate from the
            // English key and let an id lookup win over duplicate text.
            if (!string.IsNullOrWhiteSpace(scriptKey))
                _scriptKeyLookup.TryAdd(scriptKey, display);

            if (_textLookup.TryAdd(key, display))
            {
                added++;
                _scriptEntryCount++;
            }

            // The English dump holds one gender's wording, so keep a key with the
            // pronouns collapsed for the other one. A line that already matches
            // exactly keeps priority, because the exact lookup runs first.
            var neutral = GenderedPronouns.Neutralize(key);
            if (!ReferenceEquals(neutral, key) && !_textLookup.ContainsKey(neutral))
                _genderNeutralLookup.TryAdd(neutral, display);
        }

        if (_genderNeutralLookup.Count > 0)
        {
            _log?.Debug("Indexed {Count:N0} gender-neutral script keys.", _genderNeutralLookup.Count);
        }

        return added;
    }

    /// <summary>
    /// Resolves text now when nothing in it depends on the player, so a lookup stays a
    /// dictionary hit. Conditionals and <c>ObjectParameter</c> text are kept raw and
    /// resolved per lookup. Such lines are rare.
    /// </summary>
    private string PrepareDisplay(string rawText)
        => NeedsLateResolution(rawText) ? rawText : ResolveDisplayText(rawText);

    private string ResolveDisplay(string stored)
        => NeedsLateResolution(stored) ? ResolveDisplayText(stored) : stored;

    private string ResolveDisplayText(string text)
        => _displayResolver(ResolveSheetMacros(text));

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
