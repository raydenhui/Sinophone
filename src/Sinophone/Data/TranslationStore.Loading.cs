using System.Diagnostics;

namespace Sinophone.Data;

public sealed partial class TranslationStore
{
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

        var primaryTextCount = store.TextEntryCount;
        var primaryNameCount = store.NameEntryCount;
        var primaryScriptCount = store.ScriptEntryCount;

        if (fallback is not null)
            store.Index(fallback, descriptors, extraction, dataLog);

        // Several rows can match one prompt. A row that draws a word with a
        // conditional macro matches almost any text in its place, so its template
        // also matches prompts written for another row. The template with the most
        // literal text names the prompt most exactly, so try it first.
        store._addon.SortTemplates();

        stopwatch.Stop();
        dataLog.Info(
            "Loaded {Variant}{Fallback} translations: {Text:N0} dialogue, {Name:N0} names and {Script:N0} script lines " +
            "in {Elapsed} ms (fallback added {AddedText:N0} dialogue / {AddedName:N0} names / {AddedScript:N0} script lines).",
            store.PrimaryVariant,
            store.UsedFallback ? " + fallback" : string.Empty,
            store.TextEntryCount,
            store.NameEntryCount,
            store.ScriptEntryCount,
            stopwatch.ElapsedMilliseconds,
            store.TextEntryCount - primaryTextCount,
            store.NameEntryCount - primaryNameCount,
            store.ScriptEntryCount - primaryScriptCount);

        return store;
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
