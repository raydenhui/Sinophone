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
    private readonly Dictionary<string, string> _nameLookup = new(StringComparer.Ordinal);

    private TranslationStore(ChineseVariant primaryVariant, bool usedFallback)
    {
        PrimaryVariant = primaryVariant;
        UsedFallback = usedFallback;
    }

    /// <summary>The variant whose rows were indexed first.</summary>
    public ChineseVariant PrimaryVariant { get; }

    /// <summary>True when the other variant was merged in to cover missing keys.</summary>
    public bool UsedFallback { get; }

    public int TextEntryCount => _textLookup.Count;

    public int NameEntryCount => _nameLookup.Count;

    public bool TryGetDialogue(string? englishText, out string chineseText)
    {
        chineseText = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishText);
        if (key.Length == 0)
            return false;

        return _textLookup.TryGetValue(key, out chineseText!);
    }

    public bool TryGetName(string? englishName, out string chineseName)
    {
        chineseName = string.Empty;

        var key = MacroNormalizer.NormalizeKey(englishName);
        if (key.Length == 0)
            return false;

        return _nameLookup.TryGetValue(key, out chineseName!);
    }

    /// <summary>
    /// Builds a store for the configured variant. Returns null when neither the
    /// primary nor the fallback artifact could be read.
    /// </summary>
    public static TranslationStore? Load(Configuration configuration, IDataManager dataManager, IPluginLog log)
    {
        var stopwatch = Stopwatch.StartNew();
        var descriptors = SheetCatalog.Create(dataManager);

        var primary = LoadArtifact(configuration.Variant, log);
        if (primary is null)
        {
            log.Warning("[Sinophone] Primary artifact for {Variant} was not found in the assembly.", configuration.Variant);
        }

        var fallback = configuration.UseFallbackVariant
            ? LoadArtifact(Other(configuration.Variant), log)
            : null;

        if (primary is null && fallback is null)
        {
            log.Error("[Sinophone] No translation artifacts are embedded. Talk text will not be translated.");
            return null;
        }

        var store = new TranslationStore(configuration.Variant, fallback is not null);

        if (primary is not null)
            store.Index(primary, descriptors, log);

        var primaryTextCount = store._textLookup.Count;
        var primaryNameCount = store._nameLookup.Count;

        if (fallback is not null)
            store.Index(fallback, descriptors, log);

        stopwatch.Stop();
        log.Info(
            "[Sinophone] Loaded {Variant}{Fallback} translations: {Text:N0} dialogue and {Name:N0} name entries in {Elapsed} ms " +
            "(fallback added {AddedText:N0} dialogue / {AddedName:N0} name entries).",
            store.PrimaryVariant,
            store.UsedFallback ? " + fallback" : string.Empty,
            store._textLookup.Count,
            store._nameLookup.Count,
            stopwatch.ElapsedMilliseconds,
            store._textLookup.Count - primaryTextCount,
            store._nameLookup.Count - primaryNameCount);

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
                log.Debug("[Sinophone] Artifact has no rows for sheet {Sheet}.", descriptor.Name);
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
                    var key = MacroNormalizer.NormalizeKey(englishSlots[i]);
                    if (key.Length == 0)
                        continue;

                    var display = MacroNormalizer.CleanForDisplay(chineseSlots[i]);
                    if (display.Length == 0)
                        continue;

                    if (target.TryAdd(key, display))
                        indexedSlots++;
                }

                indexedRows++;
            }
        }

        log.Debug("[Sinophone] Indexed {Rows:N0} rows and {Slots:N0} text slots from the {Language} artifact.",
            indexedRows, indexedSlots, artifact.Language);
    }

    private static TranslationArtifact? LoadArtifact(ChineseVariant variant, IPluginLog log)
    {
        try
        {
            using var stream = OpenResource(variant);
            if (stream is null)
                return null;

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<TranslationArtifact>(gzip);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[Sinophone] Failed to load the {Variant} translation artifact.", variant);
            return null;
        }
    }

    private static Stream? OpenResource(ChineseVariant variant)
    {
        var suffix = variant == ChineseVariant.Traditional ? ResourceTcSuffix : ResourceCnSuffix;
        var assembly = typeof(TranslationStore).Assembly;

        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        return name is null ? null : assembly.GetManifestResourceStream(name);
    }
}
