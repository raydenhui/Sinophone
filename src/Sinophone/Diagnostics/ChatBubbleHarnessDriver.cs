#if SINOPHONE_TESTING
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Sinophone.Diagnostics;

/// <summary>
/// Drives the chat bubble surface: starts a bubble for each case above a picked character and
/// reads back what the game drew.
/// </summary>
/// <remarks>
/// A case with no usable target is skipped rather than failed, because a bubble needs a
/// character that is still in the zone. Everything here is behind <c>SINOPHONE_TESTING</c>.
/// </remarks>
public sealed class ChatBubbleHarnessDriver : IHarnessDriver
{
    private readonly HarnessBubbleTarget _target;
    private readonly Func<string?>? _text;
    private readonly HarnessLineTracker _tracker = new();
    private string? _injected;

    public ChatBubbleHarnessDriver(HarnessBubbleTarget target, Func<string?>? text = null)
    {
        _target = target;
        _text = text;
    }

    /// <summary>Target a bubble is shown above.</summary>
    public HarnessBubbleTarget Target => _target;

    /// <summary>Paths of captures taken during this run.</summary>
    public List<string> Captures { get; } = [];

    /// <summary>When set, a screenshot is taken for each case that settles.</summary>
    public HarnessCapture? Capture { get; set; }

    /// <summary>
    /// Starts the bubble. The case's English line is what the bubble is given, so the handler
    /// translates it exactly as it would a yell from the game.
    /// </summary>
    public bool Begin(HarnessCase harnessCase, out string reason)
    {
        if (!_target.IsUsable)
        {
            reason = "Pick an object first.";
            return false;
        }

        _injected = _text?.Invoke() ?? harnessCase.English;
        _tracker.Reset(_injected);

        return ChatBubbleHarness.Open(_target, _injected, out reason);
    }

    /// <summary>
    /// Reads the bubble. A text that differs from the injected English counts as translated; a
    /// translated bubble that goes back to the English line counts as a revert, which is the
    /// problem the Talk window had.
    /// </summary>
    public HarnessMeasurement Measure(HarnessCase harnessCase)
    {
        var (text, _) = ChatBubbleHarness.Read(_target);
        var drawn = text.Length == 0 ? null : text;

        if (drawn is null)
        {
            return new HarnessMeasurement
            {
                Drawn = null,
                English = harnessCase.English,
                Expected = harnessCase.ExpectedChinese,
                SkipReason = "The bubble closed before it could be read.",
            };
        }

        _tracker.Observe(drawn);

        return new HarnessMeasurement
        {
            Drawn = drawn,
            English = harnessCase.English,
            Expected = harnessCase.ExpectedChinese,
            Reverts = _tracker.Reverts,
        };
    }

    /// <summary>
    /// Takes a screenshot of the settled bubble, when capture is on, then closes the bubble so
    /// the next case starts from a clean window.
    /// </summary>
    public void End(HarnessCase harnessCase)
    {
        if (Capture is not null)
        {
            var path = Capture.Save($"bubble-{harnessCase.Id}");
            if (path is not null)
                Captures.Add(path);
        }

        ChatBubbleHarness.Close(_target);
    }

    public void Finish()
    {
        ChatBubbleHarness.Close(_target);
        _injected = null;
    }
}
#endif
