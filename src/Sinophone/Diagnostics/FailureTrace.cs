namespace Sinophone.Diagnostics;

/// <summary>
/// Collects the steps of one lookup. A handler makes a trace only when the
/// recorder is on and the case is new, so a normal build pays nothing.
/// </summary>
public sealed class FailureTrace
{
    private const int MaxSteps = 64;

    private readonly FailureRecorder _recorder;
    private bool _finished;

    internal FailureTrace(
        FailureRecorder recorder,
        string subjectKey,
        string surface,
        string component,
        string subject)
    {
        _recorder = recorder;
        SubjectKey = subjectKey;
        Case = new FailureCase
        {
            Id = FailureRecorder.MakeId(subjectKey),
            Surface = surface,
            Component = component,
            SubjectKey = subjectKey,
            Build = recorder.Build,
        };
        Case.Input["text"] = FailureText.Fit(subject);
    }

    internal FailureCase Case { get; }

    internal string SubjectKey { get; }

    internal bool Finished => _finished;

    public void Input(string name, string? value)
        => Case.Input[name] = FailureText.Fit(value);

    public void Data(string name, string? value)
        => Case.Data[name] = FailureText.Fit(value);

    public void Step(string stage, string result, string? detail = null, bool approximate = false)
    {
        if (_finished)
            return;

        if (Case.Steps.Count >= MaxSteps)
        {
            if (Case.Steps.Count == MaxSteps)
                Case.Steps.Add(new FailureStep(stage, "truncated"));

            return;
        }

        Case.Steps.Add(new FailureStep(stage, result, FailureText.Fit(detail)));
        Case.Approximate |= approximate;
    }

    public void Finish(FailureOutcome outcome, string? reason = null)
    {
        if (_finished)
            return;

        _finished = true;
        Case.Outcome = outcome;
        Case.Reason = reason;
        _recorder.Complete(this);
    }
}
