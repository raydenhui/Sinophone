namespace Sinophone.Localization;

/// <summary>
/// Resolves the settings-window language on every read, so a change in Dalamud's settings or in
/// the plugin's own setting shows up on the next frame without a reload.
/// </summary>
public sealed class InterfaceText
{
    private readonly Configuration _configuration;
    private readonly Func<string?> _dalamudLanguage;

    public InterfaceText(Configuration configuration, Func<string?> dalamudLanguage)
    {
        _configuration = configuration;
        _dalamudLanguage = dalamudLanguage;
    }

    public InterfaceLanguage Language
        => InterfaceLanguageResolver.Resolve(_configuration.InterfaceLanguage, _dalamudLanguage());

    public InterfaceStrings Strings => InterfaceStrings.For(Language);
}
