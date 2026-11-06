namespace Sinophone;

/// <summary>
/// Writes the plugin's own messages through one settings group. Turning the group off
/// hides its detail messages. Warnings and errors always show, so a failure is never
/// silent.
/// </summary>
public readonly struct PluginLog
{
    private readonly IPluginLog _log;
    private readonly Configuration? _configuration;
    private readonly LogCategory _category;

    public PluginLog(IPluginLog log, Configuration configuration, LogCategory category)
    {
        _log = log;
        _configuration = configuration;
        _category = category;
    }

    /// <summary>
    /// Writes every message. Used where no settings are available, such as the test
    /// seams in <see cref="TranslationStore"/>.
    /// </summary>
    public PluginLog(IPluginLog log)
    {
        _log = log;
        _configuration = null;
        _category = LogCategory.Plugin;
    }

    private bool IsEnabled => _configuration is null || _configuration.IsLogEnabled(_category);

    /// <summary>Per-event detail. Hidden when the group is off.</summary>
    public void Debug(string messageTemplate, params object[] propertyValues)
    {
        if (IsEnabled)
            _log.Debug(messageTemplate, propertyValues);
    }

    /// <summary>Per-event detail. Hidden when the group is off.</summary>
    public void Debug(Exception? exception, string messageTemplate, params object[] propertyValues)
    {
        if (IsEnabled)
            _log.Debug(exception, messageTemplate, propertyValues);
    }

    /// <summary>Once-per-load detail. Hidden when the group is off.</summary>
    public void Info(string messageTemplate, params object[] propertyValues)
    {
        if (IsEnabled)
            _log.Info(messageTemplate, propertyValues);
    }

    /// <summary>Something missing that the plugin works around. Always shown.</summary>
    public void Warning(string messageTemplate, params object[] propertyValues)
        => _log.Warning(messageTemplate, propertyValues);

    /// <summary>Something missing that the plugin works around. Always shown.</summary>
    public void Warning(Exception? exception, string messageTemplate, params object[] propertyValues)
        => _log.Warning(exception, messageTemplate, propertyValues);

    /// <summary>A feature is broken and the user will notice it. Always shown.</summary>
    public void Error(string messageTemplate, params object[] propertyValues)
        => _log.Error(messageTemplate, propertyValues);

    /// <summary>A feature is broken and the user will notice it. Always shown.</summary>
    public void Error(Exception? exception, string messageTemplate, params object[] propertyValues)
        => _log.Error(exception, messageTemplate, propertyValues);
}
