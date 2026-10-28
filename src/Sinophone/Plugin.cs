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
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    private readonly Configuration _configuration;
    private readonly WindowSystem _windowSystem = new("Sinophone");
    private readonly FontManager _fontManager;
    private readonly OverlayWindow _overlay;
    private readonly TargetNameWindow _targetNameWindow;
    private readonly ItemNameWindow _itemNameWindow;
    private readonly ConfigWindow _configWindow;
    private readonly TalkHandler _talkHandler;
    private readonly TargetNameHandler _targetNameHandler;
    private readonly ItemNameHandler _itemNameHandler;
    private readonly TranslationDataUpdater _dataUpdater;

    private TranslationStore? _store;

    private ChineseVariant _lastVariant;
    private ChineseConversion _lastConversion;
    private bool _lastFallback;
    private bool _lastDownloadBoth;
    private bool _lastEnabled;
    private bool _lastShowTargetName;
    private bool _lastShowItemName;
    private bool _announceUpdate;
    private bool _updateWasBusy;
    private float _lastFontSize;
    private string _lastFontPath;

    public Plugin()
    {
        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.CustomFontPath ??= string.Empty;

        _lastVariant = _configuration.Variant;
        _lastFallback = _configuration.UseFallbackVariant;
        _lastDownloadBoth = _configuration.DownloadBothVariants;
        _lastEnabled = _configuration.Enabled;
        _lastShowTargetName = _configuration.ShowTargetName;
        _lastShowItemName = _configuration.ShowItemName;
        _lastFontSize = _configuration.FontSize;
        _lastFontPath = _configuration.CustomFontPath;

        _dataUpdater = new TranslationDataUpdater(
            _configuration,
            PluginInterface.GetPluginConfigDirectory(),
            Log);

        _fontManager = new FontManager(PluginInterface.UiBuilder.FontAtlas, _configuration);
        _overlay = new OverlayWindow(_configuration, _fontManager);
        _targetNameWindow = new TargetNameWindow(_configuration, _fontManager);
        _itemNameWindow = new ItemNameWindow(_configuration, _fontManager);
        _configWindow = new ConfigWindow(
            _configuration,
            ApplySettings,
            ShowPreview,
            BuildStatusText,
            _dataUpdater,
            BuildDataSourceText,
            BuildAboutText);

        _windowSystem.AddWindow(_overlay);
        _windowSystem.AddWindow(_targetNameWindow);
        _windowSystem.AddWindow(_itemNameWindow);
        _windowSystem.AddWindow(_configWindow);

        _talkHandler = new TalkHandler(AddonLifecycle, Log, _configuration, () => _store, _overlay);
        _targetNameHandler = new TargetNameHandler(
            AddonLifecycle,
            TargetManager,
            _configuration,
            () => _store,
            _targetNameWindow);
        _itemNameHandler = new ItemNameHandler(
            GameGui,
            DataManager,
            _configuration,
            () => _store,
            _itemNameWindow,
            Log);

        _store = TranslationStore.Load(
            _configuration,
            DataManager,
            Log,
            _dataUpdater.OpenData,
            TranslationStore.OpenEmbeddedResource,
            ResolveDisplayText);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the settings window. Use /sinophone update or /sinophone status.",
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += OnTick;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        if (_configuration.CheckForDataUpdates)
            _dataUpdater.StartCheck();

        if (!ChineseConverter.IsAvailable(_configuration.Conversion))
            Log.Warning("The conversion table for {Conversion} is missing; text will not be converted.",
                _configuration.Conversion);

        Log.Info("Loaded. Variant: {Variant}, conversion: {Conversion}, entries: {Entries:N0}.",
            _configuration.Variant, _configuration.Conversion, _store?.TextEntryCount ?? 0);
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

            default:
                ChatGui.Print("[Sinophone] Usage: /sinophone [update|status]");
                break;
        }
    }

    internal enum CommandAction
    {
        Toggle,
        Update,
        Status,
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
        Log.Info("Preview requested. Font handle: {HasFont}, font: {Font}, plugin enabled: {Enabled}.",
            _fontManager.Handle is not null, _fontManager.ActiveFontPath ?? "(none)", _configuration.Enabled);

        _overlay.SetContent(
            "這是 Sinophone 的預覽文字，用於檢查中文字型是否正常顯示。\n这是 Sinophone 的预览文字，用于检查中文字体是否正常显示。",
            "This is a preview of the Sinophone overlay.",
            "Sinophone",
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

    /// <summary>
    /// Resolves conditionals in stored Chinese text and applies the optional
    /// simplified/traditional conversion. The artifact keeps conditionals because the
    /// branch depends on the player.
    /// </summary>
    private string ResolveDisplayText(string text)
        => ChineseConverter.Convert(
            MacroNormalizer.CleanForDisplay(text, EvaluateCondition),
            _configuration.Conversion);

    /// <summary>
    /// Answers the conditionals the dumps use. Only the player's gender is known;
    /// every other condition returns null, and the first branch is shown.
    /// </summary>
    private bool? EvaluateCondition(string condition)
    {
        if (!condition.Trim().Equals("PlayerParameter(4)", StringComparison.OrdinalIgnoreCase))
            return null;

        return PlayerState.IsLoaded ? PlayerState.Sex == Sex.Female : null;
    }

    /// <summary>
    /// Applies a finished download on the game thread: the updater runs in the
    /// background and only raises a flag.
    /// </summary>
    private void OnTick()
    {
        AnnounceUpdateResult();

        if (!_dataUpdater.ConsumeReloadRequest())
            return;

        SaveConfiguration();
        _store = TranslationStore.Load(
            _configuration,
            DataManager,
            Log,
            _dataUpdater.OpenData,
            TranslationStore.OpenEmbeddedResource,
            ResolveDisplayText);
        _overlay.Hide();
        _targetNameHandler.Refresh();
        _itemNameHandler.Refresh();
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

        var needsTargetRefresh = needsStoreReload
                                 || _configuration.Enabled != _lastEnabled
                                 || _configuration.ShowTargetName != _lastShowTargetName;

        var needsItemRefresh = needsStoreReload
                               || _configuration.Enabled != _lastEnabled
                               || _configuration.ShowItemName != _lastShowItemName;

        _lastVariant = _configuration.Variant;
        _lastConversion = _configuration.Conversion;
        _lastFallback = _configuration.UseFallbackVariant;
        _lastDownloadBoth = _configuration.DownloadBothVariants;
        _lastEnabled = _configuration.Enabled;
        _lastShowTargetName = _configuration.ShowTargetName;
        _lastShowItemName = _configuration.ShowItemName;
        _lastFontSize = _configuration.FontSize;
        _lastFontPath = _configuration.CustomFontPath ?? string.Empty;

        if (needsStoreReload)
        {
            _store = TranslationStore.Load(
                _configuration,
                DataManager,
                Log,
                _dataUpdater.OpenData,
                TranslationStore.OpenEmbeddedResource,
                ResolveDisplayText);

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
            _targetNameHandler.Refresh();

        if (needsItemRefresh)
            _itemNameHandler.Refresh();

        SaveConfiguration();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= OnTick;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        CommandManager.RemoveHandler(CommandName);

        _dataUpdater.Dispose();
        _talkHandler.Dispose();
        _targetNameHandler.Dispose();
        _itemNameHandler.Dispose();
        _windowSystem.RemoveAllWindows();
        _fontManager.Dispose();
    }
}
