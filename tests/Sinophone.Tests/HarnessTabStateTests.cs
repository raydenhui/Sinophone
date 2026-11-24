using Sinophone.Diagnostics;
using Sinophone.UI;

namespace Sinophone.Tests;

/// <summary>
/// The Testing tab's own decisions, kept out of the ImGui draw code so they can be checked:
/// when a run may start, what the tab says about it, and which target a run uses.
/// </summary>
public class HarnessTabStateTests
{
    [Fact]
    public void CanStart_IsFalse_WhenARunIsInFlight()
        => Assert.False(HarnessTabState.CanStart(alreadyRunning: true, caseCount: 5));

    [Fact]
    public void CanStart_IsFalse_WithNoCases()
        => Assert.False(HarnessTabState.CanStart(alreadyRunning: false, caseCount: 0));

    [Fact]
    public void CanStart_IsTrue_WhenIdleWithCases()
        => Assert.True(HarnessTabState.CanStart(alreadyRunning: false, caseCount: 1));

    [Fact]
    public void StartResult_ReportsTheCaseCount()
        => Assert.Contains("5", HarnessTabState.StartResult(started: true, alreadyRunning: false, caseCount: 5));

    [Fact]
    public void StartResult_SaysTheRunIsInFlight()
    {
        var result = HarnessTabState.StartResult(started: false, alreadyRunning: true, caseCount: 5);

        Assert.Contains("already in flight", result);
    }

    [Fact]
    public void StartResult_SaysThereAreNoCases()
    {
        var result = HarnessTabState.StartResult(started: false, alreadyRunning: false, caseCount: 0);

        Assert.Contains("No cases to run", result);
    }

    [Fact]
    public void StartResult_FallsBack_ToAGenericFailure()
    {
        var result = HarnessTabState.StartResult(started: false, alreadyRunning: false, caseCount: 3);

        Assert.Contains("Could not start", result);
    }

    [Fact]
    public void SelectedTarget_IsUnusable_WithNoTargets()
        => Assert.False(new HarnessTabState().SelectedTarget.IsUsable);

    [Fact]
    public void SelectedTarget_IsThePickedRow()
    {
        var state = new HarnessTabState { TargetIndex = 1 };
        state.Targets.Add(new HarnessBubbleTarget("First", 1, "player"));
        state.Targets.Add(new HarnessBubbleTarget("Second", 2, "npc"));

        Assert.Equal("Second", state.SelectedTarget.Name);
    }

    [Fact]
    public void SelectedTarget_IsUnusable_WhenTheIndexIsOutOfRange()
    {
        var state = new HarnessTabState { TargetIndex = 9 };
        state.Targets.Add(new HarnessBubbleTarget("First", 1, "player"));

        Assert.False(state.SelectedTarget.IsUsable);
    }

    [Fact]
    public void SetTargets_BuildsThePickerLabels()
    {
        var state = new HarnessTabState();

        state.SetTargets(
        [
            new HarnessBubbleTarget("Player One", 1, "player"),
            new HarnessBubbleTarget("Wandering Minstrel", 2, "npc"),
        ]);

        Assert.Equal(new[] { "Player One (player)", "Wandering Minstrel (npc)" }, state.TargetLabels);
    }

    [Fact]
    public void SetTargets_KeepsThePickedRowInRange()
    {
        var state = new HarnessTabState { TargetIndex = 9 };

        state.SetTargets([new HarnessBubbleTarget("Only One", 1, "npc")]);

        Assert.Equal(0, state.TargetIndex);
        Assert.Equal("Only One", state.SelectedTarget.Name);
    }

    [Fact]
    public void SetTargets_WithNothing_ClearsTheLabels()
    {
        var state = new HarnessTabState();
        state.SetTargets([new HarnessBubbleTarget("One", 1, "npc")]);

        state.SetTargets([]);

        Assert.Empty(state.Targets);
        Assert.Empty(state.TargetLabels);
        Assert.False(state.SelectedTarget.IsUsable);
    }

    [Fact]
    public void SubTabs_AreUnique()
        => Assert.Equal(
            HarnessTabState.SubTabs.Length,
            HarnessTabState.SubTabs.Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void SubTabs_HoldTheMergedProbes()
    {
        Assert.Contains("Native text", HarnessTabState.SubTabs);
        Assert.Contains("Talk probe", HarnessTabState.SubTabs);
    }

    [Fact]
    public void Defaults_AskForStoreCases_AndInjectedTalkLines()
    {
        var state = new HarnessTabState();

        Assert.True(state.UseStoreCases);
        Assert.True(state.InjectTalkLines);
        Assert.False(state.CaptureFrames);
        Assert.True(state.CaseLimit > 0);
    }
}
