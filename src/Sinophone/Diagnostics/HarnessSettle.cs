namespace Sinophone.Diagnostics;

/// <summary>
/// Holds the recent samples of a text node and decides when the game has stopped
/// changing it.
/// </summary>
/// <remarks>
/// The <c>Talk</c> window reveals a line over several frames, so a harness case cannot
/// assert on the first frame it sees. The rule is the same for every surface: the text
/// must repeat unchanged for <see cref="SettleFrames"/> samples, or the case gives up
/// after <see cref="TimeoutFrames"/>. Both are counted in frames, so the rule is a pure
/// function of the samples and can be tested without a game.
/// </remarks>
public sealed class HarnessSettle
{
    /// <summary>Samples an unchanged text needs before it counts as settled.</summary>
    public const int SettleFrames = 4;

    /// <summary>Samples after which a case stops waiting, even if the text kept changing.</summary>
    public const int TimeoutFrames = 600;

    private readonly List<string> _samples = [];
    private string _last = string.Empty;
    private int _repeats;
    private int _observations;

    /// <summary>How many times the text changed during the wait.</summary>
    public int Changes { get; private set; }

    /// <summary>True once the text repeated <see cref="SettleFrames"/> times.</summary>
    public bool IsSettled { get; private set; }

    /// <summary>True when nothing settled after <see cref="TimeoutFrames"/> looks.</summary>
    public bool HasTimedOut => _observations >= TimeoutFrames && !IsSettled;

    /// <summary>The last text seen.</summary>
    public string Last => _last;

    /// <summary>Every distinct text seen, in the order it appeared.</summary>
    public IReadOnlyList<string> Samples => _samples;

    /// <summary>Starts a new wait. Clears everything the previous wait recorded.</summary>
    public void Reset()
    {
        _samples.Clear();
        _last = string.Empty;
        _repeats = 0;
        _observations = 0;
        Changes = 0;
        IsSettled = false;
    }

    /// <summary>
    /// Records one look at the node. Returns true when the case can stop waiting.
    /// A settled text and a timeout both stop the wait.
    /// </summary>
    public bool Observe(string? text)
    {
        var value = text ?? string.Empty;
        _observations++;

        if (!string.Equals(value, _last, StringComparison.Ordinal))
        {
            if (_samples.Count > 0)
                Changes++;
            _samples.Add(value);
            _last = value;
            _repeats = 1;
        }
        else
        {
            _repeats++;
        }

        if (_repeats >= SettleFrames && _samples.Count > 0)
            IsSettled = true;

        return IsSettled || HasTimedOut;
    }
}
