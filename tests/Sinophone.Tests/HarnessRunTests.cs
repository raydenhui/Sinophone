using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The run schedule: which case is on screen, when it is measured, and what the report says.
/// The runner is driven frame by frame here, exactly as the plugin ticks it.
/// </summary>
public class HarnessRunTests
{
    private static HarnessCase Case(string id, HarnessCheck checks = HarnessCheck.Translated)
        => new(id, HarnessSurface.ChatBubble, "Hello", Checks: checks);

    /// <summary>Runs frames until the run ends, so a test never loops forever on a bug.</summary>
    private static void Drain(HarnessRun run, int frames = 2000)
    {
        for (var index = 0; index < frames && run.Tick(); index++)
        {
        }

        Assert.True(run.IsFinished, "The run did not finish within the frame budget.");
    }

    [Fact]
    public void Tick_Passes_WhenTheTextSettlesOnTheTranslation()
    {
        var driver = new FakeHarnessDriver()
            .ThenDraws("你好")
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        var result = Assert.Single(run.Report.Results);
        Assert.Equal(HarnessOutcome.Passed, result.Outcome);
        Assert.Equal("你好", result.Drawn);
    }

    [Fact]
    public void Tick_Fails_WhenTheEnglishLineIsDrawn()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "Hello", English = "Hello" });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        Assert.Equal(HarnessOutcome.Failed, run.Report.Results[0].Outcome);
        Assert.Contains("no translation reached", run.Report.Results[0].Detail);
    }

    [Fact]
    public void Tick_Fails_WhenTheTextNeverSettles()
    {
        var driver = new FakeHarnessDriver();
        var counter = 0;

        var run = new HarnessRun([Case("one")], new CountingDriver(driver, () => (counter++).ToString()));
        Drain(run, HarnessSettle.TimeoutFrames + 10);

        Assert.Equal(HarnessOutcome.Failed, run.Report.Results[0].Outcome);
        Assert.Contains("did not settle", run.Report.Results[0].Detail);
    }

    [Fact]
    public void Tick_Fails_WhenTheSurfaceDrawsNothing()
    {
        var driver = new FakeHarnessDriver().Repeating(new HarnessMeasurement { Drawn = null });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run, HarnessSettle.TimeoutFrames + 10);

        Assert.Equal(HarnessOutcome.Failed, run.Report.Results[0].Outcome);
        Assert.Contains("Nothing was drawn", run.Report.Results[0].Detail);
    }

    [Fact]
    public void Tick_Skips_WhenTheDriverCannotBegin()
    {
        var driver = new FakeHarnessDriver { RefuseReason = "Talk is not open." };

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        var result = Assert.Single(run.Report.Results);
        Assert.Equal(HarnessOutcome.Skipped, result.Outcome);
        Assert.Equal("Talk is not open.", result.Detail);
    }

    [Fact]
    public void Tick_Skips_WhenTheSurfaceReportsAReason()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { SkipReason = "The Talk window closed." });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        Assert.Equal(HarnessOutcome.Skipped, run.Report.Results[0].Outcome);
        Assert.Equal("The Talk window closed.", run.Report.Results[0].Detail);
    }

    [Fact]
    public void Tick_CallsBeginOncePerCase()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one"), Case("two")], driver);
        Drain(run);

        Assert.Equal(new[] { "one", "two" }, driver.Begun);
        Assert.Equal(new[] { "one", "two" }, driver.Ended);
    }

    [Fact]
    public void Tick_DoesNotEndACaseThatNeverStarted()
    {
        var driver = new FakeHarnessDriver { RefuseReason = "nothing to drive" };

        var run = new HarnessRun([Case("one"), Case("two")], driver);
        Drain(run);

        Assert.Equal(new[] { "one", "two" }, driver.Begun);
        Assert.Empty(driver.Ended);
    }

    [Fact]
    public void Tick_FinishesTheDriver_WhenTheLastCaseEnds()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        Assert.True(driver.Finished);
        Assert.True(run.IsFinished);
    }

    [Fact]
    public void Tick_KeepsTheLargestRevertCount()
    {
        var driver = new FakeHarnessDriver()
            .Then(new HarnessMeasurement { Drawn = "你好", English = "Hello", Reverts = 0 })
            .Then(new HarnessMeasurement { Drawn = "Hello", English = "Hello", Reverts = 2 })
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello", Reverts = 0 });

        var run = new HarnessRun([Case("one", HarnessCheck.NoRevert)], driver);
        Drain(run);

        var result = run.Report.Results[0];
        Assert.Equal(HarnessOutcome.Failed, result.Outcome);
        Assert.Equal(2, result.Measurement!.Reverts);
    }

    [Fact]
    public void Tick_CountsReverts_WhenTheyGrow()
    {
        var driver = new FakeHarnessDriver()
            .Then(new HarnessMeasurement { Drawn = "你好", English = "Hello", Reverts = 1 })
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello", Reverts = 3 });

        var run = new HarnessRun([Case("one", HarnessCheck.NoRevert)], driver);
        Drain(run);

        Assert.Equal(3, run.Report.Results[0].Measurement!.Reverts);
    }

    [Fact]
    public void Tick_ReportsTheFrameCountOfEveryCase_InTheReport()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one"), Case("two"), Case("three")], driver);
        Drain(run);

        Assert.Equal(3, run.Report.Results.Count);
        Assert.Equal(3, run.Completed);
        Assert.Equal(3, run.Total);
    }

    [Fact]
    public void Tick_ReportsTheCaseChecks_InTheResult()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好 <Sheet(Item,1,0)>", English = "Hello" });

        var run = new HarnessRun([Case("one", HarnessCheck.Translated | HarnessCheck.NoMacro)], driver);
        Drain(run);

        var result = run.Report.Results[0];
        Assert.Equal(2, result.Findings.Count);
        Assert.Contains(result.Findings, static finding => finding.Check == HarnessCheck.NoMacro && !finding.Passed);
    }

    [Fact]
    public void Cancel_SkipsTheCasesThatNeverRan()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one"), Case("two"), Case("three")], driver);
        run.Tick();

        run.Cancel("Stopped by the developer.");

        // The first case was started but never measured, so it is skipped with the rest.
        Assert.Equal(
            new[] { HarnessOutcome.Skipped, HarnessOutcome.Skipped, HarnessOutcome.Skipped },
            run.Report.Results.Select(static result => result.Outcome).ToArray());
        Assert.All(run.Report.Results, result => Assert.Equal("Stopped by the developer.", result.Detail));
        Assert.True(run.IsFinished);
    }

    [Fact]
    public void Cancel_FinishesTheDriver()
    {
        var driver = new FakeHarnessDriver();
        var run = new HarnessRun([Case("one")], driver);

        run.Cancel("Stopped.");

        Assert.True(driver.Finished);
    }

    [Fact]
    public void Cancel_AfterTheRunEnded_DoesNothing()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        run.Cancel("Too late.");

        Assert.Single(run.Report.Results);
    }

    [Fact]
    public void Tick_ReturnsFalse_OnceFinished()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one")], driver);
        Drain(run);

        Assert.False(run.Tick());
    }

    [Fact]
    public void Tick_WithNoCases_FinishesAtOnce()
    {
        var driver = new FakeHarnessDriver();

        var run = new HarnessRun([], driver);

        Assert.False(run.Tick());
        Assert.True(run.IsFinished);
        Assert.Empty(run.Report.Results);
    }

    [Fact]
    public void Current_IsTheCaseOnScreen()
    {
        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "你好", English = "Hello" });

        var run = new HarnessRun([Case("one"), Case("two")], driver);
        Assert.Equal("one", run.Current!.Id);

        Drain(run);

        Assert.Null(run.Current);
    }

    [Fact]
    public void Tick_UsesTheCaseExpectation_WhenItHasOne()
    {
        var harnessCase = new HarnessCase(
            "one", HarnessSurface.ChatBubble, "Hello", ExpectedChinese: "你好");

        var driver = new FakeHarnessDriver()
            .Repeating(new HarnessMeasurement { Drawn = "再見", English = "Hello", Expected = "你好" });

        var run = new HarnessRun([harnessCase], driver);
        Drain(run);

        Assert.Equal(HarnessOutcome.Failed, run.Report.Results[0].Outcome);
    }

    /// <summary>A driver that reports a new text every frame, so nothing ever settles.</summary>
    private sealed class CountingDriver(IHarnessDriver inner, Func<string> next) : IHarnessDriver
    {
        public bool Begin(HarnessCase harnessCase, out string reason) => inner.Begin(harnessCase, out reason);

        public HarnessMeasurement Measure(HarnessCase harnessCase)
            => new() { Drawn = next(), English = harnessCase.English };

        public void End(HarnessCase harnessCase) => inner.End(harnessCase);

        public void Finish() => inner.Finish();
    }
}
