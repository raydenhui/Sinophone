namespace Sinophone.UI;

/// <summary>
/// What the Testing tab remembers between frames: which sub-tab is open, what the last
/// pick was, and the controls of a run being set up. Keeping it apart from the window means
/// the tab's decisions are plain state a test can set up without ImGui.
/// </summary>
public sealed class HarnessTabState
{
    /// <summary>
    /// Where the tab is drawn. Listed here so the order can be tested; the tab bar itself reads
    /// this array, so adding a surface means one entry, not a new switch on a string.
    /// </summary>
    public static readonly string[] SubTabs =
    [
        "Runs",
        "Talk",
        "Chat bubbles",
        "Native text",
        "Talk probe",
        "Results",
    ];

    /// <summary>Index into <see cref="SubTabs"/>.</summary>
    public int SelectedTab { get; set; }

    /// <summary>How many cases a store-backed run builds.</summary>
    public int CaseLimit { get; set; } = HarnessCatalog.DefaultSampleSize;

    /// <summary>Build cases from the loaded store instead of the fixed ones.</summary>
    public bool UseStoreCases { get; set; } = true;

    /// <summary>Take a screenshot for each case.</summary>
    public bool CaptureFrames { get; set; }

    /// <summary>Write the case's English line into Talk instead of waiting for the developer.</summary>
    public bool InjectTalkLines { get; set; } = true;

    /// <summary>Selected row in the bubble target list.</summary>
    public int TargetIndex { get; set; }

    /// <summary>The targets listed the last time the picker was refreshed.</summary>
    public List<HarnessBubbleTarget> Targets { get; } = [];

    /// <summary>Report path from the last run, when it was written.</summary>
    public string? LastReportPath { get; set; }

    /// <summary>The sentence shown for the last action the tab took.</summary>
    public string LastResult { get; set; } = "No run yet.";

    /// <summary>The target picked for a bubble run, or an unusable one when none is picked.</summary>
    public HarnessBubbleTarget SelectedTarget
        => TargetIndex >= 0 && TargetIndex < Targets.Count
            ? Targets[TargetIndex]
            : default;

    /// <summary>
    /// True when a run may start. A run cannot overlap another, and there must be a case to run.
    /// </summary>
    public static bool CanStart(bool alreadyRunning, int caseCount)
        => !alreadyRunning && caseCount > 0;

    /// <summary>
    /// The sentence to show after asking for a start, given what the tab could do. Kept here so
    /// the wording is tested rather than written inline in a draw method.
    /// </summary>
    public static string StartResult(bool started, bool alreadyRunning, int caseCount)
    {
        if (started)
            return $"Started a run of {caseCount} case(s).";

        if (alreadyRunning)
            return "A run is already in flight. Cancel it first.";

        return caseCount == 0
            ? "No cases to run. Load translation data first, or turn off store cases."
            : "Could not start the run.";
    }
}
