#if SINOPHONE_TESTING
namespace Sinophone.Diagnostics;

/// <summary>
/// The testing tab's engine side: builds the surface drivers and hands every run to
/// <see cref="HarnessSession"/>, which owns the schedule and the report.
/// </summary>
/// <remarks>
/// Everything here is behind <c>SINOPHONE_TESTING</c> and needs the game. The rules a run
/// follows live in the session and the harness core, which are unit tested.
/// </remarks>
public sealed class HarnessHost : IDisposable
{
    private readonly Configuration _configuration;
    private readonly PluginLog _log;
    private readonly HarnessCapture? _capture;
    private readonly TalkHarnessSurface _talkSurface;
    private readonly HarnessSession _session;
    private readonly Func<string, string?> _englishForChinese;

    public HarnessHost(
        Configuration configuration,
        Func<TranslationStore?> getStore,
        IGameGui gameGui,
        ITextureProvider textures,
        ITextureReadbackProvider readback,
        Func<string, string?> englishForChinese,
        Func<string> reportDirectory,
        IPluginLog log)
    {
        _configuration = configuration;
        _log = new PluginLog(log, configuration, LogCategory.Diagnostics);
        _talkSurface = new TalkHarnessSurface(gameGui, _log);
        _englishForChinese = englishForChinese;

        _session = new HarnessSession(
            getStore,
            reportDirectory,
            message => _log.Info("{Message}", message),
            (exception, message) => _log.Warning(exception, "{Message}", message));

        var directory = reportDirectory();
        _capture = string.IsNullOrEmpty(directory)
            ? null
            : new HarnessCapture(textures, readback, _log) { Directory = directory };
    }

    /// <summary>The run in flight, or null when none is running.</summary>
    public HarnessRun? Run => _session.Run;

    /// <summary>True while a run is ticking.</summary>
    public bool IsRunning => _session.IsRunning;

    /// <summary>The most recent finished report, or null before the first run.</summary>
    public HarnessReport? LastReport => _session.LastReport;

    /// <summary>Where the last report was written, or null when it could not be.</summary>
    public string? LastReportPath => _session.LastReportPath;

    /// <summary>Where reports are written.</summary>
    public string ReportDirectory => _session.ReportDirectory;

    /// <summary>True when a capture can be taken.</summary>
    public bool CanCapture => _capture is not null;

    /// <summary>True when the Talk window is open.</summary>
    public bool IsTalkOpen => _talkSurface.IsOpen;

    /// <summary>Ticks the run once per frame, and collects any finished capture.</summary>
    public void Tick()
    {
        _session.Tick();
        _capture?.Pump();
    }

    /// <summary>Paths of screenshots written so far.</summary>
    public IReadOnlyList<string> Captured => _capture?.Captured ?? [];

    /// <summary>Stops the current run.</summary>
    public void Cancel(string reason) => _session.Cancel(reason);

    /// <summary>Builds cases for one surface.</summary>
    public IReadOnlyList<HarnessCase> BuildCases(HarnessSurface surface, int limit, bool fromStore)
        => _session.BuildCases(surface, limit, fromStore);

    /// <summary>
    /// Starts a Talk run. With <paramref name="injectLines"/> the case's English is written into
    /// the addon; without it the run waits for the developer to play a line.
    /// </summary>
    public bool StartTalkRun(IReadOnlyList<HarnessCase> cases, bool injectLines, bool captureFrames)
    {
        var driver = injectLines
            ? new TalkHarnessDriver(
                () => _talkSurface.Snapshot(null),
                english =>
                {
                    if (!_talkSurface.Inject(english))
                        _log.Warning("Testing tab: could not inject a line; the case will read the window as it is.");
                },
                _talkSurface.ClearInjected)
            : new TalkHarnessDriver(
                () => _talkSurface.Snapshot(null),
                englishForChinese: _englishForChinese);

        driver.Capture = captureFrames ? _capture : null;
        _talkSurface.ClearInjected();

        // An armed run reads whatever line is on screen, so more than one case would measure
        // the same line again.
        var casesToRun = injectLines ? cases : HarnessCatalog.SingleLine(cases);

        return _session.Start(casesToRun, driver, _configuration.Variant.ToString(), _configuration.LastDataVersion);
    }

    /// <summary>Starts a chat bubble run above one character.</summary>
    public bool StartBubbleRun(IReadOnlyList<HarnessCase> cases, HarnessBubbleTarget target, bool captureFrames)
    {
        var driver = new ChatBubbleHarnessDriver(target) { Capture = captureFrames ? _capture : null };
        return _session.Start(cases, driver, _configuration.Variant.ToString(), _configuration.LastDataVersion);
    }

    public void Dispose()
    {
        _session.Dispose();
        _talkSurface.ClearInjected();
    }
}
#endif
