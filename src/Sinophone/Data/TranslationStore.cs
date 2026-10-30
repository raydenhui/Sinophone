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

    private readonly Func<string, string> _displayResolver;
    private readonly Func<string?>? _playerName;
    private readonly IPluginLog? _log;

    private int _scriptEntryCount;

    private TranslationStore(
        ChineseVariant primaryVariant,
        bool usedFallback,
        Func<string, string> displayResolver,
        Func<string?>? playerName,
        IPluginLog? log)
    {
        PrimaryVariant = primaryVariant;
        UsedFallback = usedFallback;
        _displayResolver = displayResolver;
        _playerName = playerName;
        _log = log;
    }

    /// <summary>The variant whose rows were indexed first.</summary>
    public ChineseVariant PrimaryVariant { get; }

    /// <summary>True when the other variant was merged in to cover missing keys.</summary>
    public bool UsedFallback { get; }

    public int TextEntryCount => _textLookup.Count;

    public int NameEntryCount => _nameLookup.Count;

    /// <summary>Script-dump lines in the text lookup. Already counted in <see cref="TextEntryCount"/>.</summary>
    public int ScriptEntryCount => _scriptEntryCount;

    public bool TryGetDialogue(string? englishText, out string chineseText)
    {
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
        var alternate = MacroNormalizer.RestorePlayerNamePlaceholder(key, _playerName?.Invoke());

        if (!ReferenceEquals(alternate, key) && _textLookup.TryGetValue(alternate, out var restored))
        {
            chineseText = ResolveDisplay(restored);
            return true;
        }

        if (!TryLookupGenderNeutral(alternate, out var match))
            return false;

        chineseText = ResolveDisplay(match);
        return true;
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

    public bool TryGetName(string? englishName, out string chineseName)
    {
        chineseName = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishName);
        if (key.Length == 0)
            return false;

        if (!_nameLookup.TryGetValue(key, out var stored))
            return false;

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

                for (var i = 0; i < slotCount; i++)
                {
                    var display = PrepareDisplay(chineseSlots[i]);
                    if (display.Length == 0)
                        continue;

                    var variants = englishSlots[i];
                    for (var v = 0; v < variants.Count; v++)
                    {
                        var key = MacroNormalizer.NormalizeKey(variants[v]);
                        if (key.Length == 0)
                            continue;

                        if (target.TryAdd(key, display))
                            indexedSlots++;
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

        foreach (var entry in artifact.Scripts.Values)
        {
            var key = MacroNormalizer.NormalizeKey(entry.English);
            if (key.Length == 0)
                continue;

            var display = PrepareDisplay(entry.Text);
            if (display.Length == 0)
                continue;

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
        => NeedsLateResolution(rawText) ? rawText : _displayResolver(rawText);

    private string ResolveDisplay(string stored)
        => NeedsLateResolution(stored) ? _displayResolver(stored) : stored;

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
