using Dalamud.Plugin.Services;
using Serilog;
using Serilog.Events;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Records log calls so tests can assert the plugin logged the right level.
/// </summary>
internal sealed class FakePluginLog : IPluginLog
{
    public ILogger Logger { get; } = Serilog.Core.Logger.None;

    public List<(LogEventLevel Level, string Message)> Entries { get; } = [];

    public bool Contains(LogEventLevel level, string fragment)
        => Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.Ordinal));

    public LogEventLevel MinimumLogLevel { get; set; } = LogEventLevel.Verbose;

    public void Fatal(string messageTemplate, params object[] values) => Record(LogEventLevel.Fatal, messageTemplate);
    public void Fatal(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Fatal, messageTemplate);
    public void Error(string messageTemplate, params object[] values) => Record(LogEventLevel.Error, messageTemplate);
    public void Error(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Error, messageTemplate);
    public void Warning(string messageTemplate, params object[] values) => Record(LogEventLevel.Warning, messageTemplate);
    public void Warning(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Warning, messageTemplate);
    public void Information(string messageTemplate, params object[] values) => Record(LogEventLevel.Information, messageTemplate);
    public void Information(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Information, messageTemplate);
    public void Info(string messageTemplate, params object[] values) => Record(LogEventLevel.Information, messageTemplate);
    public void Info(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Information, messageTemplate);
    public void Debug(string messageTemplate, params object[] values) => Record(LogEventLevel.Debug, messageTemplate);
    public void Debug(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Debug, messageTemplate);
    public void Verbose(string messageTemplate, params object[] values) => Record(LogEventLevel.Verbose, messageTemplate);
    public void Verbose(Exception? exception, string messageTemplate, params object[] values) => Record(LogEventLevel.Verbose, messageTemplate);
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values) => Record(level, messageTemplate);

    private void Record(LogEventLevel level, string message) => Entries.Add((level, message));
}
