namespace Sinophone;

/// <summary>
/// Displays official Chinese (Traditional / Simplified) translations for FFXIV
/// dialogue in an overlay above the game's Talk dialog.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/sinophone";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    private readonly Configuration _configuration;
    private readonly PluginLog _log;
    private readonly WindowSystem _windowSystem = new("Sinophone");
    private readonly FontManager _fontManager;
    private readonly OverlayWindow _overlay;
    private readonly NativeTextWriter _nativeTextWriter;
    private readonly NamePlateNameWriter _namePlateNameWriter;
    private readonly ItemNameWindow _itemNameWindow;
    private readonly ConfigWindow _configWindow;
    private readonly TalkHandler _talkHandler;
    private readonly SelectionDialogHandler _selectionDialogHandler;
    private readonly ItemNameHandler _itemNameHandler;
    private readonly TranslationDataUpdater _dataUpdater;
#if DEBUG
    private readonly NativeTextProbe _nativeProbe;
    private readonly TalkContextProbe _talkContextProbe;
#endif

    private TranslationStore? _store;

    /// <summary>
    /// A load started on a worker task. It reads the index cache or parses the
    /// artifacts; the game thread finishes the load. Only one may be in flight so a
    /// settings change never races an older load into the swap.
    /// </summary>
    private Task<StoreLoadResult>? _pendingParse;

    /// <summary>Where the finished index is cached, and the key it was built for.</summary>
    private string _loadCachePath = string.Empty;
    private string _loadCacheKey = string.Empty;

    /// <summary>
    /// A parsed artifact waiting for its sheet reads. The reads are spread over
    /// several frames so a large artifact never stalls the game.
    /// </summary>
    private ParsedArtifacts? _parsedForLoad;
    private IReadOnlyList<ISheetDescriptor>? _loadDescriptors;
    private SheetExtraction? _loadExtraction;

    /// <summary>The index build, which runs on a worker task once the sheet reads finish.</summary>
    private Task<TranslationStore?>? _pendingBuild;

    /// <summary>A built store waiting for the CJK font atlas to finish building.</summary>
    private TranslationStore? _storeAwaitingFont;

    private bool _disposed;

    private ChineseVariant _lastVariant;
    private ChineseConversion _lastConversion;
    private bool _lastFallback;
    private bool _lastDownloadBoth;
    private bool _lastEnabled;
    private bool _lastShowTargetName;
    private bool _lastTargetNameShowEnglish;
    private bool _lastShowItemName;
    private bool _lastItemNameNativeMode;
    private bool _lastItemNameShowEnglish;
    private bool _lastNativeTextMode;
    private bool _lastShowInSelectionDialogs;
    private bool _lastSelectionYesNo;
    private bool _lastSelectionString;
    private bool _lastSelectionIconString;
    private bool _lastSelectionOk;
    private bool _lastSelectionCutScene;
    private bool _announceUpdate;
    private bool _updateWasBusy;
    private float _lastFontSize;
    private string _lastFontPath;

    public Plugin()
    {
        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.CustomFontPath ??= string.Empty;
        _log = new PluginLog(Log, _configuration, LogCategory.Plugin);

        if (_configuration.Version < 2)
        {
            // Selection dialogs used to be tied to the Talk native text switch.
            // Keep them on for anyone who already had native text enabled.
            _configuration.ShowInSelectionDialogs = _configuration.NativeTextMode;
            _configuration.Version = 2;
            PluginInterface.SavePluginConfig(_configuration);
            _log.Debug("Migrated configuration to version 2; selection dialogs: {Enabled}.",
                _configuration.ShowInSelectionDialogs);
        }

        // Every value compared by ApplySettings must start at the saved value. A field left at its
        // default reads as "changed" on the first settings click and reloads all data, which takes
        // about 15 seconds. Conversion was missing here and did exactly that.
        _lastVariant = _configuration.Variant;
        _lastConversion = _configuration.Conversion;
        _lastFallback = _configuration.UseFallbackVariant;
        _lastDownloadBoth = _configuration.DownloadBothVariants;
        _lastEnabled = _configuration.Enabled;
        _lastShowTargetName = _configuration.ShowTargetName;
        _lastTargetNameShowEnglish = _configuration.TargetNameShowEnglish;
        _lastShowItemName = _configuration.ShowItemName;
        _lastItemNameNativeMode = _configuration.ItemNameNativeMode;
        _lastItemNameShowEnglish = _configuration.ShowOriginal;
        _lastNativeTextMode = _configuration.NativeTextMode;
        _lastShowInSelectionDialogs = _configuration.ShowInSelectionDialogs;
        _lastSelectionYesNo = _configuration.SelectionYesNo;
        _lastSelectionString = _configuration.SelectionString;
        _lastSelectionIconString = _configuration.SelectionIconString;
        _lastSelectionOk = _configuration.SelectionOk;
        _lastSelectionCutScene = _configuration.SelectionCutScene;
        _lastFontSize = _configuration.FontSize;
        _lastFontPath = _configuration.CustomFontPath;

        _dataUpdater = new TranslationDataUpdater(
            _configuration,
            PluginInterface.GetPluginConfigDirectory(),
            Log);

        _fontManager = new FontManager(PluginInterface.UiBuilder, _configuration, _log);
        _overlay = new OverlayWindow(_configuration, _fontManager);
        _nativeTextWriter = new NativeTextWriter(GameGui, DataManager, _configuration, Log);
        _namePlateNameWriter = new NamePlateNameWriter(NamePlateGui, _configuration, () => _store, Log);
        _itemNameWindow = new ItemNameWindow(_configuration, _fontManager);
#if DEBUG
        _nativeProbe = new NativeTextProbe(AddonLifecycle, GameGui, _configuration, Log);
        _talkContextProbe = new TalkContextProbe(AddonLifecycle, TargetManager, _configuration, Log);
        _configWindow = new ConfigWindow(
            _configuration,
            ApplySettings,
            ShowPreview,
            BuildStatusText,
            _dataUpdater,
            BuildDataSourceText,
            BuildAboutText,
            _nativeProbe,
            _talkContextProbe);
#else
        _configWindow = new ConfigWindow(
            _configuration,
            ApplySettings,
            ShowPreview,
            BuildStatusText,
            _dataUpdater,
            BuildDataSourceText,
            BuildAboutText);
#endif

        _windowSystem.AddWindow(_overlay);
        _windowSystem.AddWindow(_itemNameWindow);
        _windowSystem.AddWindow(_configWindow);

        _talkHandler = new TalkHandler(
            AddonLifecycle,
            Log,
            TargetManager,
            _configuration,
            () => _store,
            _overlay,
            _nativeTextWriter);
        _selectionDialogHandler = new SelectionDialogHandler(
            AddonLifecycle,
            GameGui,
            Log,
            _configuration,
            () => _store,
            _nativeTextWriter);
        _itemNameHandler = new ItemNameHandler(
            GameGui,
            DataManager,
            AddonLifecycle,
            _configuration,
            () => _store,
            _itemNameWindow,
            _nativeTextWriter,
            Log);

        // Parsing the artifacts is the slow half and touches no game state, so it
        // runs on a worker task. OnTick finishes the load on the game thread.
        StartStoreLoad();

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the settings window. Use /sinophone update or /sinophone status.",
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += OnTick;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        if (_configuration.CheckForDataUpdates)
        {
            // A release build ships no translation data, so a fresh install must
            // fetch it. Later loads only check for a newer version.
            if (TranslationDataUpdater.NeedsInitialDownload(
                    _configuration.LastDataVersion,
                    _dataUpdater.IsMissingRequiredFile(),
                    HasEmbeddedData()))
            {
                _log.Info("No translation data is installed; downloading it.");
                _announceUpdate = true;
                _updateWasBusy = true;
                _dataUpdater.StartUpdate();
                ChatGui.Print(
                    "[Sinophone] Downloading translation data (about 60 MB). Translation starts when it finishes.");
            }
            else
            {
                // This only downloads when the published version differs, so an
                // unchanged install does one manifest request and no more. The
                // current store keeps serving while the download runs.
                _dataUpdater.StartUpdate();
            }
        }

        if (!ChineseConverter.IsAvailable(_configuration.Conversion))
            _log.Warning("The conversion table for {Conversion} is missing; text will not be converted.",
                _configuration.Conversion);

        // Translation data loads in the background, so the entry counts are logged
        // by TranslationStore when the store is built.
        _log.Info("Loaded. Variant: {Variant}, conversion: {Conversion}. Loading translation data...",
            _configuration.Variant, _configuration.Conversion);
    }

    public string Name => "Sinophone";

    private void OnCommand(string command, string arguments)
    {
        switch (ParseCommand(arguments))
        {
            case CommandAction.Toggle:
                _configWindow.Toggle();
                break;

            case CommandAction.Update:
                _announceUpdate = true;
                _updateWasBusy = true;
                _dataUpdater.StartUpdate();
                ChatGui.Print("[Sinophone] Checking for translation data updates...");
                break;

            case CommandAction.Status:
                ChatGui.Print($"[Sinophone] {BuildStatusText().Replace('\n', ' ')} {BuildDataSourceText()}");
                break;

#if DEBUG
            case CommandAction.NativeTest:
                _nativeProbe.Write();
                ChatGui.Print($"[Sinophone] Native test: {_nativeProbe.LastResult}");
                break;

            case CommandAction.NativeRestore:
                _nativeProbe.Restore();
                ChatGui.Print($"[Sinophone] Native test: {_nativeProbe.LastResult}");
                break;
#endif

            default:
                ChatGui.Print(UsageText);
                break;
        }
    }

    /// <summary>
    /// Shown when the command gets an argument it does not know. The native test
    /// subcommands are only advertised in debug builds, where they exist.
    /// </summary>
    internal const string UsageText =
#if DEBUG
        "[Sinophone] Usage: /sinophone [update|status|native|native restore]";
#else
        "[Sinophone] Usage: /sinophone [update|status]";
#endif

    internal enum CommandAction
    {
        Toggle,
        Update,
        Status,
#if DEBUG
        NativeTest,
        NativeRestore,
#endif
        Unknown,
    }

    internal static CommandAction ParseCommand(string? arguments)
    {
        var command = (arguments ?? string.Empty).Trim().ToLowerInvariant();

        return command switch
        {
            "" or "toggle" => CommandAction.Toggle,
            "update" => CommandAction.Update,
            "status" => CommandAction.Status,
#if DEBUG
            "native" => CommandAction.NativeTest,
            "native restore" => CommandAction.NativeRestore,
#endif
            _ => CommandAction.Unknown,
        };
    }

    /// <summary>
    /// Prints the result of an update started from chat. The updater runs on a
    /// worker task, so the message is printed here on the game thread.
    /// </summary>
    private void AnnounceUpdateResult()
    {
        var state = _dataUpdater.State;

        if (state.IsBusy)
        {
            _updateWasBusy = true;
            return;
        }

        if (!_updateWasBusy)
            return;

        _updateWasBusy = false;

        if (!_announceUpdate)
            return;

        _announceUpdate = false;
        ChatGui.Print($"[Sinophone] {state.Status}");
    }

    private void OpenConfigUi()
        => _configWindow.IsOpen = true;

    private void ShowPreview()
    {
        _log.Info("Preview requested. Font handle: {HasFont}, font: {Font}, plugin enabled: {Enabled}.",
            _fontManager.Handle is not null, _fontManager.ActiveFontPath ?? "(none)", _configuration.Enabled);

        _overlay.SetContent(
            "這是 Sinophone 的預覽文字，用於檢查中文字型是否正常顯示。\n这是 Sinophone 的预览文字，用于检查中文字体是否正常显示。",
            "This is a preview of the Sinophone overlay.",
            "Sinophone",
            false);
    }

    private string BuildAboutText()
    {
        var manifest = PluginInterface.Manifest;
        var assemblyVersion = manifest.AssemblyVersion;
        var version = assemblyVersion.ToString(assemblyVersion.Build >= 0 ? 3 : 2);
        var author = string.IsNullOrWhiteSpace(manifest.Author) ? "raydenhui" : manifest.Author;

        return $"Sinophone {version} by {author}";
    }

    private string BuildStatusText()
    {
        var store = _store;
        if (store is null)
            return "No translation data loaded. Use \"Download / update data\" in the settings.";

        var font = _fontManager.ActiveFontPath;
        var fontText = font is null
            ? "Font: not found (Chinese text may render as boxes)."
            : $"Font: {font}{(_fontManager.UsingSystemFont ? " (system font)" : string.Empty)}";

        return $"{store.PrimaryVariant}{(store.UsedFallback ? " + fallback" : string.Empty)}: " +
               $"{store.TextEntryCount:N0} dialogue entries, {store.NameEntryCount:N0} name entries.\n{fontText}";
    }

    private string BuildDataSourceText()
    {
        var hasCn = File.Exists(_dataUpdater.DataFilePath(ChineseVariant.Simplified));
        var hasTc = File.Exists(_dataUpdater.DataFilePath(ChineseVariant.Traditional));

        if (!hasCn && !hasTc)
            return "Using the translation data built into the plugin.";

        var version = string.IsNullOrEmpty(_configuration.LastDataVersion)
            ? "unknown version"
            : $"version {_configuration.LastDataVersion}";

        if (!hasCn || !hasTc)
        {
            return _configuration.DownloadBothVariants
                ? "Using downloaded data. One variant is missing and uses the built-in copy."
                : $"Using downloaded data ({version}) for the selected variant only.";
        }

        return $"Using downloaded data ({version}).";
    }

    /// <summary>True when this build carries a translation artifact inside the assembly.</summary>
    private bool HasEmbeddedData()
    {
        using var stream = TranslationStore.OpenEmbeddedResource(_configuration.Variant);
        return stream is not null;
    }

    /// <summary>
    /// Resolves conditionals in stored Chinese text and applies the optional
    /// simplified/traditional conversion. The artifact keeps conditionals because the
    /// branch depends on the player.
    /// </summary>
    private string ResolveDisplayText(string text)
        => ChineseConverter.Convert(
            MacroNormalizer.CleanForDisplay(text, EvaluateCondition, PlayerState.CharacterName),
            _configuration.Conversion);

    /// <summary>
    /// Answers the conditionals the dumps use. Only the player's gender is known;
    /// every other condition returns null, and the first branch is shown.
    /// </summary>
    private bool? EvaluateCondition(string condition)
    {
        if (!PlayerState.IsLoaded)
            return null;

        return PlayerConditionEvaluator.Evaluate(
            condition,
            PlayerState.Sex == Sex.Female,
            PlayerState.GrandCompany.RowId,
            PlayerState.GetGrandCompanyRank(PlayerState.GrandCompany.Value));
    }

    /// <summary>
    /// Applies a finished download on the game thread: the updater runs in the
    /// background and only raises a flag. A parsed artifact is also finished here,
    /// because reading the live sheets has to happen on the game thread.
    /// </summary>
    private void OnTick()
    {
        AdvanceStoreLoad();
        AnnounceUpdateResult();

        if (!_dataUpdater.ConsumeReloadRequest())
            return;

        SaveConfiguration();
        StartStoreLoad();
    }

    /// <summary>
    /// Starts parsing the artifacts on a worker task. Replaces any load still in
    /// flight, so a settings change is never overwritten by an older result.
    /// </summary>
    private void StartStoreLoad()
    {
        var configuration = _configuration;
        var log = Log;
        var openResource = _dataUpdater.OpenData;

        // Descriptors hold Lumina objects, so they are created on the game thread.
        // A cached index needs them too: some lookups still read the live English
        // text to choose a row's slot.
        var descriptors = SheetCatalog.Create(DataManager);
        var cachePath = Path.Combine(
            PluginInterface.GetPluginConfigDirectory(),
            TranslationStore.CacheFileName);
        var cacheKey = BuildStoreCacheKey();
        var dialogueLog = new PluginLog(log, configuration, LogCategory.Dialogue);

        _parsedForLoad = null;
        _loadDescriptors = descriptors;
        _loadExtraction = null;
        _pendingBuild = null;
        _storeAwaitingFont = null;
        _loadCachePath = cachePath;
        _loadCacheKey = cacheKey;

        _pendingParse = Task.Run(() =>
        {
            // A cache hit skips the artifact parse, the sheet reads, and the index
            // build. The live sheets are read only when it misses.
            if (TranslationStore.TryLoadCache(
                    cachePath,
                    cacheKey,
                    descriptors,
                    dialogueLog,
                    ResolveDisplayText,
                    () => PlayerState.CharacterName,
                    out var cached))
            {
                return new StoreLoadResult(cached, null);
            }

            return new StoreLoadResult(
                null,
                TranslationStore.Parse(
                    configuration,
                    log,
                    openResource,
                    TranslationStore.OpenEmbeddedResource));
        });
    }

    /// <summary>
    /// Ties a cached index to the inputs it was built from. A game patch changes the
    /// English text the index is keyed on, and a new download changes the artifact,
    /// so either one makes the cache stale. The plugin build is part of the key
    /// because an index change, such as a new match rule, only ships in a new build.
    /// </summary>
    private string BuildStoreCacheKey()
    {
        var configuration = _configuration;

        return string.Join(
            "|",
            configuration.Variant.ToString(),
            configuration.UseFallbackVariant.ToString(),
            configuration.Conversion.ToString(),
            typeof(TranslationStore).Assembly.ManifestModule.ModuleVersionId.ToString(),
            GetGameDataVersion(),
            _dataUpdater.SourceIdentity(ChineseVariant.Simplified),
            _dataUpdater.SourceIdentity(ChineseVariant.Traditional));
    }

    private string GetGameDataVersion()
    {
        try
        {
            return DataManager.GameData.Repositories.TryGetValue("ffxiv", out var repository)
                ? repository.Version
                : "unknown";
        }
        catch (Exception ex)
        {
            // An unknown version only costs a rebuild, so do not fail the load.
            _log.Debug(ex, "Could not read the game data version.");
            return "unknown";
        }
    }

    /// <summary>
    /// The most time the game thread spends reading game sheets in one frame.
    /// The rest of the load runs on worker tasks.
    /// </summary>
    private static readonly TimeSpan SheetReadBudget = TimeSpan.FromMilliseconds(4);

    /// <summary>
    /// Moves a load started by <see cref="StartStoreLoad"/> one step forward: parse
    /// on a worker task, read the live sheets in small slices, index on a worker
    /// task, then swap the store in. No step runs long enough to stall a frame.
    /// </summary>
    private void AdvanceStoreLoad()
    {
        if (_disposed)
            return;

        if (_storeAwaitingFont is { } ready)
        {
            // Chinese drawn before the atlas is built shows as boxes, so the
            // store waits for it.
            if (!_fontManager.IsReady)
                return;

            _storeAwaitingFont = null;
            SwapStore(ready);
        }

        if (_pendingParse is { } parse)
        {
            if (!parse.IsCompleted)
                return;

            _pendingParse = null;

            if (parse.IsFaulted)
            {
                _log.Error(parse.Exception, "Failed to load the translation data.");
                return;
            }

            var result = parse.Result;

            if (result.Cached is { } cached)
            {
                // The cache holds the finished index, so the sheet reads and the
                // build are skipped. The store still waits for the font atlas.
                _parsedForLoad = null;
                _loadDescriptors = null;
                _loadExtraction = null;

                if (!_fontManager.IsReady)
                {
                    _storeAwaitingFont = cached;
                    return;
                }

                SwapStore(cached);
                return;
            }

            var parsed = result.Parsed;
            if (parsed is null)
            {
                _log.Warning("No translation artifacts are available. Talk text will not be translated.");
                return;
            }

            _parsedForLoad = parsed;
            _loadExtraction = new SheetExtraction(parsed, _loadDescriptors!);
        }

        if (_parsedForLoad is null)
            return;

        if (_loadExtraction is { IsComplete: false } extraction)
        {
            extraction.Extract(SheetReadBudget);
            if (!extraction.IsComplete)
                return;

            new PluginLog(Log, _configuration, LogCategory.Data).Debug(
                "Read {Rows:N0} sheet rows for the {Variant} index across several frames.",
                extraction.RowCount,
                _parsedForLoad.Variant);
        }

        if (_pendingBuild is null)
        {
            var parsed = _parsedForLoad;
            var descriptors = _loadDescriptors!;
            var prepared = _loadExtraction!;
            var variant = parsed.Variant;
            var log = Log;
            var configuration = _configuration;
            var indexResolver = CreateLoadDisplayResolver();
            var cachePath = _loadCachePath;
            var cacheKey = _loadCacheKey;
            var dataLog = new PluginLog(log, configuration, LogCategory.Data);

            // Building touches no game sheet now, so it can run off the game
            // thread. It uses a copy of the player state, but lookups keep the live
            // resolver, so a character switch is still picked up.
            _pendingBuild = Task.Run(() =>
            {
                var built = TranslationStore.Build(
                    parsed,
                    descriptors,
                    prepared,
                    variant,
                    log,
                    ResolveDisplayText,
                    () => PlayerState.CharacterName,
                    configuration,
                    indexResolver);

                // Saving here keeps the disk write off the game thread. The next
                // start reads this file instead of re-reading the sheets.
                if (built is not null)
                    TranslationStore.SaveCache(cachePath, cacheKey, built, dataLog);

                return built;
            });
        }

        if (!_pendingBuild.IsCompleted)
            return;

        var build = _pendingBuild;
        _pendingBuild = null;
        _parsedForLoad = null;
        _loadDescriptors = null;
        _loadExtraction = null;

        if (build.IsFaulted)
        {
            _log.Error(build.Exception, "Failed to build the translation store.");
            return;
        }

        var store = build.Result;
        if (store is null)
            return;

        if (!_fontManager.IsReady)
        {
            _log.Debug("Store built; waiting for the CJK font atlas before using it.");
            _storeAwaitingFont = store;
            return;
        }

        SwapStore(store);
    }

    private void SwapStore(TranslationStore store)
    {
        _log.Debug("Translation store is ready; swapped it in.");
        _store = store;
        _overlay.Hide();
        RefreshNamePlates(namesChanged: true);
        _itemNameHandler.Refresh();
    }

    /// <summary>
    /// The display resolver for the index build. The build runs on a worker task,
    /// so the player state it may need is read once here and captured.
    /// </summary>
    private Func<string, string> CreateLoadDisplayResolver()
    {
        var loaded = PlayerState.IsLoaded;
        var isFemale = loaded && PlayerState.Sex == Sex.Female;
        var companyRowId = loaded ? PlayerState.GrandCompany.RowId : 0;
        var companyRank = loaded
            ? PlayerState.GetGrandCompanyRank(PlayerState.GrandCompany.Value)
            : 0;
        var characterName = loaded ? PlayerState.CharacterName : null;
        var conversion = _configuration.Conversion;

        return text => ChineseConverter.Convert(
            MacroNormalizer.CleanForDisplay(
                text,
                condition => PlayerConditionEvaluator.Evaluate(condition, isFemale, companyRowId, companyRank),
                characterName),
            conversion);
    }

    /// <summary>
    /// Turns nameplate translation on or off for the current settings. A new store or a
    /// changed English-name setting needs a fresh lookup, so every plate is laid out again.
    /// </summary>
    private void RefreshNamePlates(bool namesChanged = false)
    {
        if (namesChanged)
            _namePlateNameWriter.Reset();

        if (_configuration.Enabled && _configuration.ShowTargetName)
            _namePlateNameWriter.Enable();
        else
            _namePlateNameWriter.Disable();
    }

    private void SaveConfiguration()
        => PluginInterface.SavePluginConfig(_configuration);

    private void ApplySettings()
    {
        var needsStoreReload = _configuration.Variant != _lastVariant
                               || _configuration.Conversion != _lastConversion
                               || _configuration.UseFallbackVariant != _lastFallback;

        var needsDownloadCheck = _configuration.DownloadBothVariants != _lastDownloadBoth;

        var needsFontRebuild = needsStoreReload
                               || Math.Abs(_configuration.FontSize - _lastFontSize) > 0.01f
                               || !string.Equals(_configuration.CustomFontPath, _lastFontPath, StringComparison.OrdinalIgnoreCase);

        var targetNameEnglishChanged = _configuration.TargetNameShowEnglish != _lastTargetNameShowEnglish;
        var needsTargetRefresh = needsStoreReload
                                 || _configuration.Enabled != _lastEnabled
                                 || _configuration.ShowTargetName != _lastShowTargetName
                                 || targetNameEnglishChanged;

        var needsItemRefresh = needsStoreReload
                               || _configuration.Enabled != _lastEnabled
                               || _configuration.ShowItemName != _lastShowItemName
                               || _configuration.ItemNameNativeMode != _lastItemNameNativeMode
                               || _configuration.ShowOriginal != _lastItemNameShowEnglish;

        var nativeTextModeChanged = _lastNativeTextMode != _configuration.NativeTextMode;
        var selectionSettingsChanged = _lastShowInSelectionDialogs != _configuration.ShowInSelectionDialogs
                                       || _lastSelectionYesNo != _configuration.SelectionYesNo
                                       || _lastSelectionString != _configuration.SelectionString
                                       || _lastSelectionIconString != _configuration.SelectionIconString
                                       || _lastSelectionOk != _configuration.SelectionOk
                                       || _lastSelectionCutScene != _configuration.SelectionCutScene;
        var pluginDisabled = _lastEnabled && !_configuration.Enabled;

        _lastVariant = _configuration.Variant;
        _lastConversion = _configuration.Conversion;
        _lastFallback = _configuration.UseFallbackVariant;
        _lastDownloadBoth = _configuration.DownloadBothVariants;
        _lastEnabled = _configuration.Enabled;
        _lastShowTargetName = _configuration.ShowTargetName;
        _lastTargetNameShowEnglish = _configuration.TargetNameShowEnglish;
        _lastShowItemName = _configuration.ShowItemName;
        _lastItemNameNativeMode = _configuration.ItemNameNativeMode;
        _lastItemNameShowEnglish = _configuration.ShowOriginal;
        _lastNativeTextMode = _configuration.NativeTextMode;
        _lastShowInSelectionDialogs = _configuration.ShowInSelectionDialogs;
        _lastSelectionYesNo = _configuration.SelectionYesNo;
        _lastSelectionString = _configuration.SelectionString;
        _lastSelectionIconString = _configuration.SelectionIconString;
        _lastSelectionOk = _configuration.SelectionOk;
        _lastSelectionCutScene = _configuration.SelectionCutScene;
        _lastFontSize = _configuration.FontSize;
        _lastFontPath = _configuration.CustomFontPath ?? string.Empty;

        if (nativeTextModeChanged || pluginDisabled)
        {
            // Turning native mode off, or disabling the plugin, must put the game's own
            // line back instead of leaving Chinese in the dialogue box until the next line.
            // Turning native mode on hides the overlay, which it replaces. The restore is
            // a no-op when native mode wrote nothing.
            _talkHandler.RestoreNativeText();
            _overlay.Hide();
        }

        if (selectionSettingsChanged || pluginDisabled)
        {
            // Put the game's own prompt back now; the handler re-translates on the
            // next dialog event when the setting is on.
            _selectionDialogHandler.RestoreAll();
            _log.Debug("Selection dialog settings changed; restored the game's text.");
        }

        if (needsStoreReload)
        {
            // Settings changed while a load may be running. Start over on the new
            // variant; the older task's result is dropped before it is built.
            StartStoreLoad();

            // The user now needs a variant that is not on disk.
            if (!string.IsNullOrEmpty(_configuration.LastDataVersion) && _dataUpdater.IsMissingRequiredFile())
                _dataUpdater.StartUpdate();
        }

        // Turning the second variant back on needs its file, which may never have been downloaded.
        if (needsDownloadCheck
            && !string.IsNullOrEmpty(_configuration.LastDataVersion)
            && _dataUpdater.IsMissingRequiredFile())
        {
            _dataUpdater.StartUpdate();
        }

        if (needsFontRebuild)
            _fontManager.Rebuild();

        if (needsTargetRefresh)
            RefreshNamePlates(needsStoreReload || targetNameEnglishChanged);

        if (needsItemRefresh)
            _itemNameHandler.Refresh();

        SaveConfiguration();
    }

    public void Dispose()
    {
        // Drop a load in flight so it is never built into a disposed plugin.
        _disposed = true;
        _pendingParse = null;
        _parsedForLoad = null;
        _loadExtraction = null;
        _loadDescriptors = null;
        _pendingBuild = null;
        _storeAwaitingFont = null;

        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= OnTick;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        CommandManager.RemoveHandler(CommandName);

        _dataUpdater.Dispose();
#if DEBUG
        _nativeProbe.Dispose();
        _talkContextProbe.Dispose();
#endif
        _selectionDialogHandler.Dispose();
        _talkHandler.Dispose();
        _namePlateNameWriter.Disable();
        _itemNameHandler.Dispose();
        _windowSystem.RemoveAllWindows();
        _fontManager.Dispose();
    }

    /// <summary>
    /// What the worker task found: a usable index cache, or the parsed artifacts
    /// that still need their sheet reads and build.
    /// </summary>
    private sealed record StoreLoadResult(TranslationStore? Cached, ParsedArtifacts? Parsed);
}
