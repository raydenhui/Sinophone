#if SINOPHONE_TESTING
namespace Sinophone.Diagnostics;

/// <summary>
/// Drives the Talk surface. Two modes:
/// <list type="bullet">
/// <item><b>Waiting.</b> The developer talks to an NPC and the driver measures the line the
/// game is showing. This is the only way to test the id-first lookup, because an injected line
/// has no live event handler behind it.</item>
/// <item><b>Injected.</b> The driver writes the case's English line into the addon's values,
/// so a run can go without walking to an NPC. The id-first lookup is not exercised.</item>
/// </list>
/// </summary>
/// <remarks>
/// Everything here is behind <c>SINOPHONE_TESTING</c>.
/// </remarks>
public sealed class TalkHarnessDriver : IHarnessDriver
{
    private readonly Func<HarnessDialogSnapshot?> _snapshot;
    private readonly Action<string>? _inject;
    private readonly HarnessLineTracker _tracker = new();
    private string? _injected;
    private bool _armed;

    /// <summary>
    /// <paramref name="snapshot"/> reads the current Talk line, or null when Talk is not open.
    /// <paramref name="inject"/> puts an English line into the addon; leave it out to wait for
    /// the developer to talk.
    /// </summary>
    public TalkHarnessDriver(Func<HarnessDialogSnapshot?> snapshot, Action<string>? inject = null)
    {
        _snapshot = snapshot;
        _inject = inject;
    }

    /// <summary>True when each case waits for the developer to talk instead of injecting.</summary>
    public bool IsArmed => _inject is null;

    /// <summary>When set, a screenshot is taken for each settled case.</summary>
    public HarnessCapture? Capture { get; set; }

    public bool Begin(HarnessCase harnessCase, out string reason)
    {
        _injected = null;
        _armed = false;
        _tracker.Reset(null);

        if (IsArmed)
        {
            // The source line is whatever the developer plays, so the tracker has nothing to
            // compare with until the first frame. It is reset on the first sample below.
            reason = string.Empty;
            return true;
        }

        var snapshot = _snapshot();
        if (snapshot is null)
        {
            reason = "The Talk window is not open.";
            return false;
        }

        _injected = harnessCase.English;
        _tracker.Reset(_injected);
        _inject!(harnessCase.English);
        reason = string.Empty;
        return true;
    }

    public HarnessMeasurement Measure(HarnessCase harnessCase)
    {
        var snapshot = _snapshot();
        if (snapshot is null)
            return new HarnessMeasurement { SkipReason = "The Talk window closed." };

        var drawn = snapshot.Text;
        var english = IsArmed ? snapshot.English : _injected;

        if (IsArmed && !_armed)
        {
            // The first sample of a played line is its English, so that is the source.
            _tracker.Reset(drawn);
            _armed = true;
        }

        _tracker.Observe(drawn);

        return new HarnessMeasurement
        {
            Drawn = drawn,
            English = english,
            Expected = harnessCase.ExpectedChinese,
            RenderedLines = snapshot.Lines,
            TextWidth = snapshot.TextWidth,
            TextHeight = snapshot.TextHeight,
            BoxWidth = snapshot.BoxWidth,
            BoxHeight = snapshot.BoxHeight,
            Reverts = _tracker.Reverts,
            MissingGlyphs = snapshot.MissingGlyphs,
        };
    }

    /// <summary>
    /// Takes a screenshot while the line is still on screen, then forgets the injected line so a
    /// later sample is read as it was played.
    /// </summary>
    public void End(HarnessCase harnessCase)
    {
        if (Capture is not null)
        {
            Capture.Request($"talk-{harnessCase.Id}");
        }

        _injected = null;
    }

    public void Finish() => _injected = null;
}

/// <summary>What the Talk window looks like at one moment.</summary>
/// <param name="Text">The line the game drew.</param>
/// <param name="English">The English line the game was given.</param>
/// <param name="Lines">The drawn lines, when the node reports them.</param>
/// <param name="TextWidth">Measured width of the drawn text.</param>
/// <param name="TextHeight">Measured height of the drawn text.</param>
/// <param name="BoxWidth">Width of the room the game gave the text.</param>
/// <param name="BoxHeight">Height of the room the game gave the text.</param>
/// <param name="MissingGlyphs">Characters the font has no glyph for, or 0 when not measured.</param>
public readonly record struct HarnessDialogSnapshot(
    string Text,
    string English,
    IReadOnlyList<string>? Lines,
    int TextWidth,
    int TextHeight,
    int BoxWidth,
    int BoxHeight,
    int MissingGlyphs);
#endif
