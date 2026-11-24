namespace Sinophone.Diagnostics;

/// <summary>
/// Runs a list of cases one at a time through a surface driver.
/// </summary>
/// <remarks>
/// The runner is a state machine driven by frames rather than a loop, because the game has
/// to keep running between one case and the next: a case writes into a live addon, waits for
/// the game to lay it out, then measures it. It holds no game types, so the whole schedule can
/// be tested with a fake driver.
/// </remarks>
public sealed class HarnessRun
{
    private readonly IReadOnlyList<HarnessCase> _cases;
    private readonly IHarnessDriver _driver;
    private readonly HarnessReport _report;

    private HarnessSettle _settle = new();
    private int _index;
    private int _reverts;
    private bool _caseStarted;
    private HarnessMeasurement _lastMeasurement = new();
    private bool _finished;

    public HarnessRun(IReadOnlyList<HarnessCase> cases, IHarnessDriver driver, HarnessReport? report = null)
    {
        _cases = cases;
        _driver = driver;
        _report = report ?? new HarnessReport();
    }

    /// <summary>The case on screen, or null when the run has ended.</summary>
    public HarnessCase? Current => _index < _cases.Count ? _cases[_index] : null;

    /// <summary>True between the last case and <see cref="Finish"/>.</summary>
    public bool IsFinished => _finished;

    /// <summary>The report, with every case that has run so far.</summary>
    public HarnessReport Report => _report;

    /// <summary>How many cases have a result.</summary>
    public int Completed => _report.Results.Count;

    /// <summary>Total cases in this run.</summary>
    public int Total => _cases.Count;

    /// <summary>
    /// Advances the run by one frame. Returns true while the run still has work, so the host
    /// knows to keep ticking.
    /// </summary>
    public bool Tick()
    {
        if (_finished)
            return false;

        if (_index >= _cases.Count)
        {
            Finish();
            return false;
        }

        var current = _cases[_index];

        // A case that has not started has nothing in flight: write the line, then wait.
        if (!_caseStarted)
        {
            StartCase(current);
            return true;
        }

        var measurement = _driver.Measure(current);

        if (measurement.SkipReason is { Length: > 0 } skipReason)
        {
            Complete(current, HarnessOutcome.Skipped, measurement, skipReason);
            return true;
        }

        if (measurement.Reverts > _reverts)
            _reverts = measurement.Reverts;

        _lastMeasurement = measurement with { Reverts = _reverts };

        if (!_settle.Observe(measurement.Drawn))
            return true;

        if (_settle.HasTimedOut)
        {
            Complete(current, HarnessOutcome.Failed, _lastMeasurement,
                $"The text did not settle within {HarnessSettle.TimeoutFrames} frames.");
            return true;
        }

        var findings = HarnessAssertions.Run(current.Checks, _lastMeasurement);
        var withFindings = _lastMeasurement with { Findings = findings };

        if (!withFindings.Passed)
        {
            var failed = string.Join(" ", findings
                .Where(static finding => !finding.Passed)
                .Select(static finding => finding.Detail));
            Complete(current, HarnessOutcome.Failed, withFindings, failed);
            return true;
        }

        Complete(current, HarnessOutcome.Passed, withFindings, null);
        return true;
    }

    /// <summary>Stops early, recording the cases that never ran as skipped.</summary>
    public void Cancel(string reason)
    {
        if (_finished)
            return;

        while (_index < _cases.Count)
        {
            var remaining = _cases[_index];
            _report.Add(new HarnessResult(
                remaining.Id, remaining.Surface, HarnessOutcome.Skipped, remaining.English,
                Detail: reason));
            _index++;
        }

        Finish();
    }

    private void StartCase(HarnessCase current)
    {
        _settle = new HarnessSettle();
        _reverts = 0;
        _lastMeasurement = new HarnessMeasurement
        {
            English = current.English,
            Expected = current.ExpectedChinese,
        };

        if (!_driver.Begin(current, out var reason))
        {
            Complete(current, HarnessOutcome.Skipped, _lastMeasurement, reason);
            return;
        }

        _caseStarted = true;
        _settle.Reset();
    }

    private void Complete(HarnessCase current, HarnessOutcome outcome, HarnessMeasurement measurement, string? detail)
    {
        // The driver takes its screenshot here, while the surface still shows the line, and
        // puts the surface back afterwards.
        _driver.End(current);

        _report.Add(new HarnessResult(
            current.Id,
            current.Surface,
            outcome,
            current.English,
            measurement.Drawn,
            detail)
        {
            Findings = measurement.Findings,
            Measurement = measurement,
        });

        _index++;
        _caseStarted = false;
        _settle = new HarnessSettle();
        _reverts = 0;
        _lastMeasurement = new HarnessMeasurement();
    }

    private void Finish()
    {
        if (_finished)
            return;

        _finished = true;
        _report.FinishedAt = DateTimeOffset.UtcNow;
        _driver.Finish();
    }
}

/// <summary>
/// What a surface has to do for the runner: put a case on screen, measure what is there, and
/// clean up. The engine implements this; tests use a fake.
/// </summary>
public interface IHarnessDriver
{
    /// <summary>
    /// Puts the case on screen. Returns false with a reason when the surface cannot be driven,
    /// such as an addon that is not open, so the case is skipped rather than failed.
    /// </summary>
    bool Begin(HarnessCase harnessCase, out string reason);

    /// <summary>Looks at the surface now. Called once per frame until the text settles.</summary>
    HarnessMeasurement Measure(HarnessCase harnessCase);

    /// <summary>Puts the surface back the way it was before the case ran.</summary>
    void End(HarnessCase harnessCase);

    /// <summary>Called once when the run ends, or is cancelled.</summary>
    void Finish();
}
