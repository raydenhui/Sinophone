namespace Sinophone.Diagnostics;

/// <summary>
/// Owns the one run allowed at a time, ticks it once per frame, and writes the report.
/// </summary>
/// <remarks>
/// This half holds no game types, so the unit tests drive it: start, tick, cancel, and the
/// report file. <see cref="HarnessHost"/> adds the engine drivers on top of it.
/// </remarks>
public sealed class HarnessSession : IDisposable
{
    private readonly Func<TranslationStore?> _getStore;
    private readonly Func<string> _reportDirectory;
    private readonly Action<string> _logInfo;
    private readonly Action<Exception, string> _logWarning;

    private HarnessRun? _run;
    private bool _disposed;

    // Building cases from the store sorts every sheet text, which is far too much work to
    // repeat while the config window draws every frame. The cache is dropped when the store
    // instance changes, so a data update shows the new lines.
    private readonly Dictionary<(HarnessSurface Surface, int Limit, bool FromStore), List<HarnessCase>> _caseCache = [];
    private TranslationStore? _caseCacheStore;

    /// <param name="getStore">The loaded store, or null before one is ready.</param>
    /// <param name="reportDirectory">Where reports go. Empty disables writing.</param>
    /// <param name="logInfo">Info log, or null to stay quiet.</param>
    /// <param name="logWarning">Warning log, or null to stay quiet.</param>
    public HarnessSession(
        Func<TranslationStore?> getStore,
        Func<string> reportDirectory,
        Action<string>? logInfo = null,
        Action<Exception, string>? logWarning = null)
    {
        _getStore = getStore;
        _reportDirectory = reportDirectory;
        _logInfo = logInfo ?? (static _ => { });
        _logWarning = logWarning ?? (static (_, _) => { });
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

    /// <summary>
    /// Starts a run. Returns false when one is in flight or there is nothing to run.
    /// </summary>
    public bool Start(IReadOnlyList<HarnessCase> cases, IHarnessDriver driver, string variant, string? gameVersion)
    {
        if (IsRunning || cases.Count == 0)
            return false;

        var report = new HarnessReport
        {
            Variant = variant,
            GameVersion = gameVersion,
        };

        _run = new HarnessRun(cases, driver, report);
        _logInfo($"Testing tab: started a run of {cases.Count} case(s).");
        return true;
    }

    /// <summary>Ticks once per frame. Writes the report when the last case finishes.</summary>
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
            _logInfo($"Testing tab: run finished. {_run.Report.Summary()}");
            _run = null;
        }
        catch (Exception ex)
        {
            _logWarning(ex, "Testing tab: the run stopped after an unexpected error.");
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

    /// <summary>
    /// Builds cases from the loaded store, or the fixed sample, for one surface. The result is
    /// cached until the settings or the store change, because the tab asks on every frame.
    /// </summary>
    public IReadOnlyList<HarnessCase> BuildCases(HarnessSurface surface, int limit, bool fromStore)
    {
        var store = fromStore ? _getStore() : null;
        if (!ReferenceEquals(store, _caseCacheStore))
        {
            _caseCache.Clear();
            _caseCacheStore = store;
        }

        // The fixed sample does not depend on the limit, so it gets one entry per surface.
        var key = (surface, fromStore ? limit : 0, fromStore);
        if (_caseCache.TryGetValue(key, out var cached))
            return cached;

        List<HarnessCase> cases;
        if (fromStore)
        {
            cases = HarnessCatalog.FromStore(surface, store, limit);
        }
        else
        {
            cases = [];
            cases.AddRange(HarnessCatalog.BuiltIn.Where(harnessCase => harnessCase.Surface == surface));
        }

        _caseCache[key] = cases;
        return cases;
    }

    /// <summary>Writes a report next to the plugin config.</summary>
    public string? SaveReport(HarnessReport report)
    {
        var directory = ReportDirectory;
        if (string.IsNullOrEmpty(directory))
            return null;

        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, HarnessReport.FileNameFor(report.StartedAt));
            File.WriteAllText(path, report.ToJson());
            return path;
        }
        catch (Exception ex)
        {
            _logWarning(ex, "Testing tab: could not write the run report.");
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
