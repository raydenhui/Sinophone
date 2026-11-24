#if SINOPHONE_TESTING
using Dalamud.Interface.Textures;

namespace Sinophone.Diagnostics;

/// <summary>
/// Owns the testing tab's runs: builds a run from a pick of cases, ticks it once per frame,
/// and keeps the last report.
/// </summary>
/// <remarks>
/// A run is frame-driven, so the game keeps drawing between cases. Only one run can be in
/// flight, because two would write into the same window and each measure the other's line.
/// Everything here is behind <c>SINOPHONE_TESTING</c>.
/// </remarks>
public sealed class HarnessHost : IDisposable
{
    private readonly Configuration _configuration;
    private readonly Func<TranslationStore?> _getStore;
    private readonly PluginLog _log;
    private readonly Func<string> _reportDirectory;
    private readonly HarnessCapture? _capture;
    private readonly TalkHarnessSurface _talkSurface;

    private HarnessRun? _run;
    private bool _disposed;

    public HarnessHost(
        Configuration configuration,
        Func<TranslationStore?> getStore,
        IGameGui gameGui,
        ITextureProvider textures,
        ITextureReadbackProvider readback,
        Func<string> reportDirectory,
        IPluginLog log)
    {
        _configuration = configuration;
        _getStore = getStore;
        _reportDirectory = reportDirectory;
        _log = new PluginLog(log, configuration, LogCategory.Diagnostics);
        _talkSurface = new TalkHarnessSurface(gameGui, _log);

        var directory = reportDirectory();
        _capture = string.IsNullOrEmpty(directory)
            ? null
            : new HarnessCapture(textures, readback, _log) { Directory = directory };
    }

    /// <summary>The run in flight, or null when none is running.</summary>
    public HarnessRun? Run => _run;

    /// <summary>True while a run is ticking.</summary>
    public bool IsRunning => _run is { IsFinished: false };

    /// <summary>The most recent finished report, or null before the first run.</summary>
    public HarnessReport? LastReport { get; private set; }

    /// <summary>Where the last report was written, or null when it could not be.</summary>
    public string? LastReportPath { get; private set; }

    /// <summary>Where reports are written. Empty when the plugin has no config directory.</summary>
    public string ReportDirectory => _reportDirectory();

    /// <summary>True when a capture can be taken.</summary>
    public bool CanCapture => _capture is not null;

    /// <summary>True when the Talk window is open.</summary>
    public bool IsTalkOpen => _talkSurface.IsOpen;

    /// <summary>
    /// Starts a run. Returns false when one is already in flight or there is nothing to run.
    /// </summary>
    /// <param name="cases">Cases to run, in order.</param>
    /// <param name="driver">Surface driver for those cases.</param>
    /// <param name="captureFrames">Take a screenshot per case.</param>
    public bool Start(IReadOnlyList<HarnessCase> cases, IHarnessDriver driver, bool captureFrames = false)
    {
        if (IsRunning || cases.Count == 0)
            return false;

        if (driver is ChatBubbleHarnessDriver bubble)
            bubble.Capture = captureFrames ? _capture : null;
        else if (driver is TalkHarnessDriver talk)
            talk.Capture = captureFrames ? _capture : null;

        var report = new HarnessReport
        {
            Variant = _configuration.Variant.ToString(),
            GameVersion = _configuration.LastDataVersion,
        };

        _run = new HarnessRun(cases, driver, report);
        _talkSurface.ClearInjected();
        _log.Info("Testing tab: started a run of {Count} case(s).", cases.Count);
        return true;
    }

    /// <summary>
    /// Tick once per frame. Stops the run and writes the report when the last case finishes.
    /// </summary>
    public void Tick()
    {
        if (_disposed || _run is null)
            return;

        try
        {
            if (_run.Tick())
                return;

            LastReportPath = SaveReport(_run.Report);
            LastReport = _run.Report;
            _log.Info("Testing tab: run finished. {Summary}", _run.Report.Summary());
            _run = null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Testing tab: the run stopped after an unexpected error.");
            Cancel("The run stopped after an error.");
        }
    }

    /// <summary>Stops the current run and records the cases that never ran as skipped.</summary>
    public void Cancel(string reason)
    {
        if (_run is null)
            return;

        _run.Cancel(reason);
        LastReportPath = SaveReport(_run.Report);
        LastReport = _run.Report;
        _run = null;
    }

    /// <summary>Starts a run that waits for the developer to talk to an NPC.</summary>
    public bool StartTalkRun(IReadOnlyList<HarnessCase> cases, bool injectLines, bool captureFrames)
    {
        var driver = new TalkHarnessDriver(
            () => _talkSurface.Snapshot(null),
            injectLines
                ? english =>
                {
                    if (!_talkSurface.Inject(english))
                        _log.Warning("Testing tab: could not inject a line; the run will wait.");
                }
                : null);

        return Start(cases, driver, captureFrames);
    }

    /// <summary>Starts a run of chat bubbles above one character.</summary>
    public bool StartBubbleRun(IReadOnlyList<HarnessCase> cases, HarnessBubbleTarget target, bool captureFrames)
        => Start(cases, new ChatBubbleHarnessDriver(target), captureFrames);

    /// <summary>Builds cases from the loaded store for one surface.</summary>
    public List<HarnessCase> BuildCases(HarnessSurface surface, int limit, bool fromStore)
        => fromStore
            ? HarnessCatalog.FromStore(surface, _getStore(), limit)
            : [.. HarnessCatalog.BuiltIn.Where(harnessCase => harnessCase.Surface == surface)];

    /// <summary>Writes a report next to the plugin config.</summary>
    public string? SaveReport(HarnessReport report)
    {
        var directory = ReportDirectory;
        if (string.IsNullOrEmpty(directory))
            return null;

        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, HarnessReport.FileNameFor(report.StartedAt));
            File.WriteAllText(path, report.ToJson());
            return path;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Testing tab: could not write the run report.");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _run?.Cancel("The plugin unloaded during the run.");
        _run = null;
    }
}
#endif
