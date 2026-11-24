using Sinophone.Diagnostics;

namespace Sinophone.Tests;

/// <summary>
/// The run session: one run at a time, ticked per frame, with the report written at the end.
/// Nothing here needs the game, so the whole schedule is checked against a fake driver.
/// </summary>
public class HarnessSessionTests
{
    private static HarnessCase Case(string id)
        => new(id, HarnessSurface.ChatBubble, "Hello");

    private static HarnessMeasurement Translated => new() { Drawn = "你好", English = "Hello" };

    private static HarnessSession Session(Func<string>? directory = null)
        => new(() => null, directory ?? (static () => string.Empty));

    private static void Drain(HarnessSession session, int frames = 100)
    {
        for (var index = 0; index < frames && session.IsRunning; index++)
            session.Tick();
    }

    [Fact]
    public void Start_RefusesASecondRun()
    {
        var session = Session();

        var first = session.Start([Case("a")], new FakeHarnessDriver(), "Traditional", null);
        var second = session.Start([Case("b")], new FakeHarnessDriver(), "Traditional", null);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void Start_RefusesWithNoCases()
        => Assert.False(Session().Start([], new FakeHarnessDriver(), "Traditional", null));

    [Fact]
    public void Tick_FinishesTheRun_AndKeepsTheReport()
    {
        var session = Session();
        var driver = new FakeHarnessDriver().Repeating(Translated);

        session.Start([Case("a")], driver, "Traditional", null);
        Drain(session);

        Assert.False(session.IsRunning);
        Assert.Null(session.Run);
        Assert.NotNull(session.LastReport);
        Assert.Equal(1, session.LastReport!.PassedCount);
    }

    [Fact]
    public void Tick_WithNoRun_DoesNothing()
    {
        var session = Session();

        session.Tick();

        Assert.Null(session.LastReport);
    }

    [Fact]
    public void Start_KeepsTheVariantAndVersion_InTheReport()
    {
        var session = Session();
        session.Start([Case("a")], new FakeHarnessDriver().Repeating(Translated), "Traditional", "7.2");
        Drain(session);

        Assert.Equal("Traditional", session.LastReport!.Variant);
        Assert.Equal("7.2", session.LastReport.GameVersion);
    }

    [Fact]
    public void Cancel_StopsTheRun_AndKeepsTheReport()
    {
        var session = Session();
        session.Start([Case("a"), Case("b")], new FakeHarnessDriver().Repeating(Translated), "Traditional", null);
        session.Tick();

        session.Cancel("Stopped.");

        Assert.False(session.IsRunning);
        Assert.Contains(session.LastReport!.Results, static result => result.Detail == "Stopped.");
    }

    [Fact]
    public void Cancel_WithNoRun_DoesNothing()
    {
        var session = Session();

        session.Cancel("Stopped.");

        Assert.Null(session.LastReport);
    }

    [Fact]
    public void Dispose_CancelsTheRun()
    {
        var session = Session();
        session.Start([Case("a")], new FakeHarnessDriver().Repeating(Translated), "Traditional", null);
        session.Tick();

        session.Dispose();

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Dispose_IsSafe_Twice()
    {
        var session = Session();

        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void Tick_AfterDispose_DoesNothing()
    {
        var session = Session();
        session.Start([Case("a")], new FakeHarnessDriver().Repeating(Translated), "Traditional", null);
        session.Dispose();

        session.Tick();

        Assert.Null(session.LastReport);
    }

    [Fact]
    public void BuildCases_FromTheBuiltInSample_NeedsNoStore()
    {
        var cases = Session().BuildCases(HarnessSurface.ChatBubble, 10, fromStore: false);

        Assert.NotEmpty(cases);
        Assert.All(cases, static harnessCase => Assert.Equal(HarnessSurface.ChatBubble, harnessCase.Surface));
    }

    [Fact]
    public void BuildCases_FromTheStore_IsEmpty_WithNoStore()
        => Assert.Empty(Session().BuildCases(HarnessSurface.Talk, 10, fromStore: true));

    [Fact]
    public void BuildCases_ReturnsTheBuiltInSample_ForTalk()
    {
        var cases = Session().BuildCases(HarnessSurface.Talk, 10, fromStore: false);

        Assert.NotEmpty(cases);
        Assert.All(cases, static harnessCase => Assert.Equal(HarnessSurface.Talk, harnessCase.Surface));
    }

    [Fact]
    public void SaveReport_ReturnsNull_WithNoDirectory()
        => Assert.Null(Session().SaveReport(new HarnessReport()));

    [Fact]
    public void SaveReport_WritesTheFile_AndTheReportCanBeRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sinophone-harness-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = Session(() => directory);
            var report = new HarnessReport();
            report.Add(new HarnessResult("one", HarnessSurface.Talk, HarnessOutcome.Passed, "Hello"));

            var path = session.SaveReport(report);

            Assert.NotNull(path);
            Assert.True(File.Exists(path));

            var read = HarnessReport.FromJson(File.ReadAllText(path!));
            Assert.NotNull(read);
            Assert.Equal(1, read!.PassedCount);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tick_WritesTheReport_ToTheDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sinophone-harness-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = Session(() => directory);
            session.Start([Case("a")], new FakeHarnessDriver().Repeating(Translated), "Traditional", null);
            Drain(session);

            Assert.NotNull(session.LastReportPath);
            Assert.True(File.Exists(session.LastReportPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveReport_KeepsTwoRunsApart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sinophone-harness-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = Session(() => directory);
            var first = new HarnessReport { StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
            var second = new HarnessReport { StartedAt = DateTimeOffset.UtcNow };

            var firstPath = session.SaveReport(first);
            var secondPath = session.SaveReport(second);

            Assert.NotEqual(firstPath, secondPath);
            Assert.True(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Start_AfterFinish_IsAllowed()
    {
        var session = Session();
        session.Start([Case("a")], new FakeHarnessDriver().Repeating(Translated), "Traditional", null);
        Drain(session);

        var restarted = session.Start([Case("b")], new FakeHarnessDriver().Repeating(Translated), "Traditional", null);

        Assert.True(restarted);
    }
}
