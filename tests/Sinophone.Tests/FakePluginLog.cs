using System.Globalization;
using Dalamud.Plugin.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace Sinophone.Tests;

/// <summary>
/// Records log calls so tests can assert the plugin logged the right level.
/// </summary>
internal sealed class FakePluginLog : IPluginLog
{
    public ILogger Logger { get; } = Serilog.Core.Logger.None;

    public List<(LogEventLevel Level, string Message)> Entries { get; } = [];

    /// <summary>
    /// The last exception passed with each logged message, or null when the call carried
    /// none. Kept beside <see cref="Entries"/> so a test can check the throw was logged
    /// whole, not only its message text.
    /// </summary>
    public List<Exception?> Exceptions { get; } = [];

    public bool Contains(LogEventLevel level, string fragment)
        => Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.Ordinal));

    public LogEventLevel MinimumLogLevel { get; set; } = LogEventLevel.Verbose;

    public void Fatal(string messageTemplate, params object[] values) => Record(LogEventLevel.Fatal, messageTemplate, values);
    public void Fatal(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Fatal, exception, messageTemplate, values);
    public void Error(string messageTemplate, params object[] values) => Record(LogEventLevel.Error, messageTemplate, values);
    public void Error(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Error, exception, messageTemplate, values);
    public void Warning(string messageTemplate, params object[] values) => Record(LogEventLevel.Warning, messageTemplate, values);
    public void Warning(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Warning, exception, messageTemplate, values);
    public void Information(string messageTemplate, params object[] values) => Record(LogEventLevel.Information, messageTemplate, values);
    public void Information(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Information, exception, messageTemplate, values);
    public void Info(string messageTemplate, params object[] values) => Record(LogEventLevel.Information, messageTemplate, values);
    public void Info(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Information, exception, messageTemplate, values);
    public void Debug(string messageTemplate, params object[] values) => Record(LogEventLevel.Debug, messageTemplate, values);
    public void Debug(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Debug, exception, messageTemplate, values);
    public void Verbose(string messageTemplate, params object[] values) => Record(LogEventLevel.Verbose, messageTemplate, values);
    public void Verbose(Exception? exception, string messageTemplate, params object[] values) => RecordWithException(LogEventLevel.Verbose, exception, messageTemplate, values);
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values) => RecordWithException(level, exception, messageTemplate, values);

    private void RecordWithException(LogEventLevel level, Exception? exception, string template, params object[] values)
    {
        Record(level, template, values, exception);
    }

    /// <summary>Renders the template with its values, so a test can assert on what was logged.</summary>
    private void Record(LogEventLevel level, string template, object[] values, Exception? exception = null)
    {
        var parsed = Parser.Parse(template);
        var tokens = parsed.Tokens.ToList();
        var properties = new List<LogEventProperty>(values.Length);
        for (var i = 0; i < values.Length && i < tokens.Count; i++)
        {
            if (tokens[i] is PropertyToken token)
                properties.Add(new LogEventProperty(token.PropertyName, new ScalarValue(values[i])));
        }

        var logEvent = new LogEvent(DateTimeOffset.Now, level, exception, parsed, properties);
        var writer = new StringWriter();
        Formatter.Format(logEvent, writer);
        Entries.Add((level, writer.ToString()));
        Exceptions.Add(exception);
    }

    private static readonly MessageTemplateParser Parser = new();
    private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}", CultureInfo.InvariantCulture);
}
