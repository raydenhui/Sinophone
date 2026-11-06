using Serilog.Events;

namespace Sinophone.Tests;

public class PluginLogTests
{
    [Fact]
    public void Debug_Writes_WhenTheGroupIsOn()
    {
        var sink = new FakePluginLog();
        var log = new PluginLog(sink, new Configuration(), LogCategory.NamePlates);

        log.Debug("Native name: {English} -> {Chinese}.", "Mytesyn", "米泰辛");

        Assert.True(sink.Contains(LogEventLevel.Debug, "Native name"));
    }

    [Fact]
    public void Debug_Drops_WhenTheGroupIsOff()
    {
        var configuration = new Configuration { LogNamePlates = false };
        var sink = new FakePluginLog();
        var log = new PluginLog(sink, configuration, LogCategory.NamePlates);

        log.Debug("Native name: {English} -> {Chinese}.", "Mytesyn", "米泰辛");

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Info_Drops_WhenTheGroupIsOff()
    {
        var configuration = new Configuration { LogData = false };
        var sink = new FakePluginLog();
        var log = new PluginLog(sink, configuration, LogCategory.Data);

        log.Info("Loaded {Count} lines.", 1);

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void GroupsAreIndependent()
    {
        var configuration = new Configuration { LogDialogue = false };
        var sink = new FakePluginLog();

        new PluginLog(sink, configuration, LogCategory.Dialogue).Debug("hidden");
        new PluginLog(sink, configuration, LogCategory.Data).Debug("shown");

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("shown", entry.Message);
    }

    [Fact]
    public void Warning_And_Error_AlwaysWrite()
    {
        var configuration = new Configuration { LogData = false };
        var sink = new FakePluginLog();
        var log = new PluginLog(sink, configuration, LogCategory.Data);

        log.Warning("No data for this line.");
        log.Error("The artifact is broken.");

        Assert.True(sink.Contains(LogEventLevel.Warning, "No data for this line"));
        Assert.True(sink.Contains(LogEventLevel.Error, "The artifact is broken"));
    }

    [Fact]
    public void WithoutConfiguration_EveryMessageWrites()
    {
        var sink = new FakePluginLog();
        var log = new PluginLog(sink);

        log.Debug("detail");
        log.Info("summary");

        Assert.Contains(sink.Entries, e => e.Level == LogEventLevel.Debug && e.Message == "detail");
        Assert.Contains(sink.Entries, e => e.Level == LogEventLevel.Information && e.Message == "summary");
    }
}
