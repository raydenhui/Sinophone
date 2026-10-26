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
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    private readonly Configuration _configuration;
    private readonly WindowSystem _windowSystem = new("Sinophone");
    private readonly FontManager _fontManager;
    private readonly OverlayWindow _overlay;
    private readonly TargetNameWindow _targetNameWindow;
    private readonly ConfigWindow _configWindow;
    private readonly TalkHandler _talkHandler;
    private readonly TargetNameHandler _targetNameHandler;
    private readonly TranslationDataUpdater _dataUpdater;

    private TranslationStore? _store;

    private ChineseVariant _lastVariant;
    private bool _lastFallback;
    private bool _lastEnabled;
    private bool _lastShowTargetName;
    private float _lastFontSize;
    private string _lastFontPath;

    public Plugin()
    {
        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.CustomFontPath ??= string.Empty;

        _lastVariant = _configuration.Variant;
        _lastFallback = _configuration.UseFallbackVariant;
        _lastEnabled = _configuration.Enabled;
        _lastShowTargetName = _configuration.ShowTargetName;
        _lastFontSize = _configuration.FontSize;
        _lastFontPath = _configuration.CustomFontPath;

        _dataUpdater = new TranslationDataUpdater(
            _configuration,
            PluginInterface.GetPluginConfigDirectory(),
            Log);

        _fontManager = new FontManager(PluginInterface.UiBuilder.FontAtlas, _configuration);
        _overlay = new OverlayWindow(_configuration, _fontManager);
        _targetNameWindow = new TargetNameWindow(_configuration, _fontManager);
        _configWindow = new ConfigWindow(
            _configuration,
            ApplySettings,
            ShowPreview,
            BuildStatusText,
            _dataUpdater,
            BuildDataSourceText);

        _windowSystem.AddWindow(_overlay);
        _windowSystem.AddWindow(_targetNameWindow);
        _windowSystem.AddWindow(_configWindow);

        _talkHandler = new TalkHandler(AddonLifecycle, Log, _configuration, () => _store, _overlay);
        _targetNameHandler = new TargetNameHandler(
            AddonLifecycle,
            TargetManager,
            _configuration,
            () => _store,
            _targetNameWindow);

        _store = TranslationStore.Load(_configuration, DataManager, Log, _dataUpdater.OpenData);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Sinophone settings window.",
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += OnTick;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        if (_configuration.CheckForDataUpdates)
            _dataUpdater.StartCheck();

        Log.Info("[Sinophone] Loaded. Variant: {Variant}, entries: {Entries:N0}.",
            _configuration.Variant, _store?.TextEntryCount ?? 0);
    }

    public string Name => "Sinophone";

    private void OnCommand(string command, string arguments)
        => _configWindow.Toggle();

    private void OpenConfigUi()
        => _configWindow.IsOpen = true;

    private void ShowPreview()
    {
        _overlay.SetContent(
            "這是 Sinophone 的預覽文字，用於檢查中文字型是否正常顯示。\n这是 Sinophone 的预览文字，用于检查中文字体是否正常显示。",
            "This is a preview of the Sinophone overlay.",
            "Sinophone",
            "Sinophone",
            false);
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

        if (!hasCn || !hasTc)
            return "Using downloaded data. One variant is missing and uses the built-in copy.";

        var version = string.IsNullOrEmpty(_configuration.LastDataVersion)
            ? "unknown version"
            : $"version {_configuration.LastDataVersion}";

        return $"Using downloaded data ({version}).";
    }

    /// <summary>
    /// Applies a finished download on the game thread: the updater runs in the
    /// background and only raises a flag.
    /// </summary>
    private void OnTick()
    {
        if (!_dataUpdater.ConsumeReloadRequest())
            return;

        SaveConfiguration();
        _store = TranslationStore.Load(_configuration, DataManager, Log, _dataUpdater.OpenData);
        _overlay.Hide();
        _targetNameHandler.Refresh();
    }

    private void SaveConfiguration()
        => PluginInterface.SavePluginConfig(_configuration);

    private void ApplySettings()
    {
        var needsStoreReload = _configuration.Variant != _lastVariant
                               || _configuration.UseFallbackVariant != _lastFallback;

        var needsFontRebuild = needsStoreReload
                               || Math.Abs(_configuration.FontSize - _lastFontSize) > 0.01f
                               || !string.Equals(_configuration.CustomFontPath, _lastFontPath, StringComparison.OrdinalIgnoreCase);

        var needsTargetRefresh = needsStoreReload
                                 || _configuration.Enabled != _lastEnabled
                                 || _configuration.ShowTargetName != _lastShowTargetName;

        _lastVariant = _configuration.Variant;
        _lastFallback = _configuration.UseFallbackVariant;
        _lastEnabled = _configuration.Enabled;
        _lastShowTargetName = _configuration.ShowTargetName;
        _lastFontSize = _configuration.FontSize;
        _lastFontPath = _configuration.CustomFontPath ?? string.Empty;

        if (needsStoreReload)
            _store = TranslationStore.Load(_configuration, DataManager, Log, _dataUpdater.OpenData);

        if (needsFontRebuild)
            _fontManager.Rebuild();

        if (needsTargetRefresh)
            _targetNameHandler.Refresh();

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
        _windowSystem.RemoveAllWindows();
        _fontManager.Dispose();
    }
}
