using System.Globalization;
using Dalamud.Plugin.Services;
using Serilog;
using Serilog.Events;

namespace Sinophone.Coverage;

/// <summary>
/// Prints the store's warnings and errors. Debug and info are dropped: the coverage run reads
/// thousands of lines and the store logs one per sheet.
/// </summary>
internal sealed class ConsoleLog : IPluginLog
{
    public ILogger Logger { get; } = Serilog.Core.Logger.None;

    public LogEventLevel MinimumLogLevel { get; set; } = LogEventLevel.Warning;

    public void Fatal(string messageTemplate, params object[] values) => Print(LogEventLevel.Fatal, messageTemplate, values);
    public void Fatal(Exception? exception, string messageTemplate, params object[] values) => Print(LogEventLevel.Fatal, messageTemplate, values, exception);
    public void Error(string messageTemplate, params object[] values) => Print(LogEventLevel.Error, messageTemplate, values);
    public void Error(Exception? exception, string messageTemplate, params object[] values) => Print(LogEventLevel.Error, messageTemplate, values, exception);
    public void Warning(string messageTemplate, params object[] values) => Print(LogEventLevel.Warning, messageTemplate, values);
    public void Warning(Exception? exception, string messageTemplate, params object[] values) => Print(LogEventLevel.Warning, messageTemplate, values, exception);
    public void Information(string messageTemplate, params object[] values) { }
    public void Information(Exception? exception, string messageTemplate, params object[] values) { }
    public void Info(string messageTemplate, params object[] values) { }
    public void Info(Exception? exception, string messageTemplate, params object[] values) { }
    public void Debug(string messageTemplate, params object[] values) { }
    public void Debug(Exception? exception, string messageTemplate, params object[] values) { }
    public void Verbose(string messageTemplate, params object[] values) { }
    public void Verbose(Exception? exception, string messageTemplate, params object[] values) { }
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)
        => Print(level, messageTemplate, values, exception);

    private static void Print(LogEventLevel level, string template, object[] values, Exception? exception = null)
    {
        var message = template;
        for (var i = 0; i < values.Length; i++)
            message = message.Replace("{" + i + "}", Convert.ToString(values[i], CultureInfo.InvariantCulture) ?? "", StringComparison.Ordinal);

        Console.Error.WriteLine($"{level,-7} {message}{(exception is null ? "" : $" ({exception.Message})")}");
    }
}
