using Serilog.Events;
using Sinophone.Data;
using Sinophone.Handlers;

namespace Sinophone.Tests;

/// <summary>
/// Hook callbacks run inside the game's frame. A throw there does not stay in the plugin, so
/// every handler routes its callback through <see cref="HookGuard"/>. These tests check that
/// the guard holds: the action may fail, the frame must not.
/// </summary>
public class HookGuardTests
{
    private static PluginLog Log(FakePluginLog log)
        => new(log, new Configuration(), LogCategory.Dialogue);

    [Fact]
    public void Run_DoesNotThrow_WhenTheActionThrows()
    {
        var log = new FakePluginLog();

        var exception = Record.Exception(() =>
            HookGuard.Run(Log(log), "Talk", () => throw new InvalidOperationException("boom")));

        Assert.Null(exception);
    }

    [Fact]
    public void Run_LogsAWarning_WhenTheActionThrows()
    {
        var log = new FakePluginLog();

        HookGuard.Run(Log(log), "Talk", () => throw new InvalidOperationException("boom"));

        Assert.Contains(log.Entries, e => e.Level == LogEventLevel.Warning && e.Message.Contains("Talk"));
    }

    [Fact]
    public void Run_LogsTheException_SoTheCauseIsNotLost()
    {
        var log = new FakePluginLog();

        HookGuard.Run(Log(log), "Talk", () => throw new InvalidOperationException("boom"));

        var index = log.Entries.FindIndex(e => e.Level == LogEventLevel.Warning);
        Assert.True(index >= 0, "no warning was logged");
        Assert.IsType<InvalidOperationException>(log.Exceptions[index]);
    }

    [Fact]
    public void Run_RunsTheAction_WhenItDoesNotThrow()
    {
        var log = new FakePluginLog();
        var ran = false;

        HookGuard.Run(Log(log), "Talk", () => ran = true);

        Assert.True(ran);
        Assert.DoesNotContain(log.Entries, e => e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void RunGeneric_ReturnsTheFallback_WhenTheFunctionThrows()
    {
        var log = new FakePluginLog();

        var result = HookGuard.Run<string?>(
            Log(log),
            "Talk",
            () => throw new InvalidOperationException("boom"),
            fallback: null);

        Assert.Null(result);
    }

    [Fact]
    public void RunGeneric_ReturnsTheValue_WhenTheFunctionSucceeds()
    {
        var log = new FakePluginLog();

        var result = HookGuard.Run(Log(log), "Talk", () => "你好。", fallback: null);

        Assert.Equal("你好。", result);
    }

    [Fact]
    public void Run_KeepsRunning_AfterAnEarlierCallThrew()
    {
        // One failed line must not poison the rest of the session.
        var log = new FakePluginLog();
        var after = false;

        HookGuard.Run(Log(log), "Talk", () => throw new InvalidOperationException("boom"));
        HookGuard.Run(Log(log), "Talk", () => after = true);

        Assert.True(after);
    }
}
