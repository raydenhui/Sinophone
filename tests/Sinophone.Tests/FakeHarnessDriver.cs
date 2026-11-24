using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// A driver whose answers are scripted, so a run can be driven frame by frame without a game.
/// </summary>
internal sealed class FakeHarnessDriver : IHarnessDriver
{
    private Queue<HarnessMeasurement> _script = new();
    private HarnessMeasurement _steady = new();
    private bool _beginFails;

    /// <summary>Cases the driver was asked to start.</summary>
    public List<string> Begun { get; } = [];

    /// <summary>Cases the driver was asked to clean up.</summary>
    public List<string> Ended { get; } = [];

    /// <summary>True once the run asked the driver to finish.</summary>
    public bool Finished { get; private set; }

    /// <summary>Measurement returned once the script runs out.</summary>
    public HarnessMeasurement Steady
    {
        get => _steady;
        set => _steady = value;
    }

    /// <summary>Reason <see cref="Begin"/> refuses, or null to accept every case.</summary>
    public string? RefuseReason { get; set; }

    /// <summary>Sets what every frame returns after the scripted ones run out.</summary>
    public FakeHarnessDriver Repeating(HarnessMeasurement measurement)
    {
        _steady = measurement;
        return this;
    }

    /// <summary>Queues one measurement for the next frame.</summary>
    public FakeHarnessDriver Then(HarnessMeasurement measurement)
    {
        _script.Enqueue(measurement);
        return this;
    }

    /// <summary>Queues a measurement that draws <paramref name="text"/>.</summary>
    public FakeHarnessDriver ThenDraws(string text)
        => Then(new HarnessMeasurement { Drawn = text });

    public bool Begin(HarnessCase harnessCase, out string reason)
    {
        Begun.Add(harnessCase.Id);

        if (RefuseReason is { } refusal)
        {
            reason = refusal;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public HarnessMeasurement Measure(HarnessCase harnessCase)
        => _script.Count > 0 ? _script.Dequeue() : _steady;

    public void End(HarnessCase harnessCase) => Ended.Add(harnessCase.Id);

    public void Finish() => Finished = true;
}
